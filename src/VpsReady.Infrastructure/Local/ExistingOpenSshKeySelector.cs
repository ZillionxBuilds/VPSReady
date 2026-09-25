using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Utilities;
using Org.BouncyCastle.OpenSsl;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;
using Renci.SshNet;

namespace VpsReady.Infrastructure.Local;

/// <summary>
/// Read-only selector for an existing unencrypted OpenSSH-v1 ED25519 private
/// key. It never copies, rewrites, repermissions, or exposes selected content.
/// </summary>
public sealed class ExistingOpenSshKeySelector : IExistingSshKeySelector
{
    private const int MaximumPrivateKeyBytes = 256 * 1024;
    private static readonly byte[] OpenSshMagic = "openssh-key-v1\0"u8.ToArray();
    private readonly IDiagnosticSink diagnostics;
    private readonly IExistingSshKeySelectionObserver observer;

    public ExistingOpenSshKeySelector(IDiagnosticSink diagnostics)
        : this(diagnostics, NoExistingSshKeySelectionObserver.Instance)
    {
    }

    internal ExistingOpenSshKeySelector(IDiagnosticSink diagnostics, IExistingSshKeySelectionObserver observer)
    {
        this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        this.observer = observer ?? throw new ArgumentNullException(nameof(observer));
    }

    public Task<ExistingSshKeySelectionResult> SelectAsync(
        ExistingSshKeySelectionRequest request,
        CorrelationIds correlation,
        CancellationToken cancellationToken) => InspectAsync(request, correlation, null, null, cancellationToken);

    public async Task<SelectedPublicKeyReadResult> ReadPublicKeyAsync(
        ExistingSshKeySelectionResult selectedKey, CorrelationIds correlation, CancellationToken cancellationToken)
    {
        PublicKeyDeploymentMaterial? material = null;
        if (!selectedKey.Succeeded)
        {
            return new(OperationResult.Failure(correlation.OperationId, OperationErrorCode.Validation, OperationState.Unchanged), null);
        }
        var inspected = await InspectAsync(new ExistingSshKeySelectionRequest(selectedKey.Location!.PrivateKeyPath),
            correlation, selectedKey.Metadata!.Fingerprint,
            (_, publicBlob) => material = new PublicKeyDeploymentMaterial(("ssh-ed25519 " + Convert.ToBase64String(publicBlob)).AsSpan()), cancellationToken).ConfigureAwait(false);
        if (!inspected.Succeeded)
        {
            material?.Dispose();
            material = null;
        }
        return new(inspected.Operation, material);
    }

    // The actual transport consumes this already-parsed key, never reopens a
    // path after identity comparison. All file buffers are cleared by InspectAsync.
    internal static async Task<PrivateKeyFile> OpenForAuthenticationAsync(ExistingSshKeyLocation location, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(location.ExpectedFingerprint))
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.KeyIdentity);
        }
        PrivateKeyFile? key = null;
        var selector = new ExistingOpenSshKeySelector(new SilentKeyReadSink());
        var inspected = await selector.InspectAsync(new ExistingSshKeySelectionRequest(location.PrivateKeyPath),
            CorrelationIds.Create("key_use"), location.ExpectedFingerprint,
            (privateBytes, _) =>
            {
                using var stream = new MemoryStream(privateBytes, writable: false);
                key = new PrivateKeyFile(stream);
            }, cancellationToken).ConfigureAwait(false);
        if (!inspected.Succeeded || key is null)
        {
            key?.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            throw new RemoteTransportException(RemoteTransportFailureKind.KeyIdentity);
        }
        return key;
    }

    private async Task<ExistingSshKeySelectionResult> InspectAsync(
        ExistingSshKeySelectionRequest request, CorrelationIds correlation,
        string? expectedFingerprint, Action<byte[], byte[]>? consume, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(correlation);
        await PublishAsync(DiagnosticEventCatalog.ExistingKeySelectionStarted, correlation, DiagnosticPhase.Validate, DiagnosticStatus.Started, null, CancellationToken.None).ConfigureAwait(false);
        byte[]? fileContents = null;
        byte[]? pemContents = null;
        byte[]? publicBytes = null;
        byte[]? companionBytes = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryValidateRegularPath(request.PrivateKeyPath, out var path, out var pathError))
            {
                return await FailAsync(correlation, pathError!, OperationErrorCode.Validation, OperationVerification.NotRun).ConfigureAwait(false);
            }

            observer.BeforeRead(path);
            fileContents = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            using var reader = new StreamReader(new MemoryStream(fileContents, writable: false), Encoding.ASCII, detectEncodingFromByteOrderMarks: false);
            var pem = new PemReader(reader).ReadPemObject();
            if (pem is null)
            {
                return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.Corrupt, OperationErrorCode.Parse, OperationVerification.Failed).ConfigureAwait(false);
            }

            if (!string.Equals(pem.Type, "OPENSSH PRIVATE KEY", StringComparison.Ordinal))
            {
                return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.Unsupported, OperationErrorCode.Unsupported, OperationVerification.Failed).ConfigureAwait(false);
            }

            pemContents = pem.Content;
            var envelope = ClassifyOpenSshEnvelope(pemContents);
            if (envelope == OpenSshEnvelope.Encrypted)
            {
                return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.Encrypted, OperationErrorCode.Unsupported, OperationVerification.Failed).ConfigureAwait(false);
            }

            if (envelope == OpenSshEnvelope.Corrupt)
            {
                return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.Corrupt, OperationErrorCode.Parse, OperationVerification.Failed).ConfigureAwait(false);
            }

            var privateKey = OpenSshPrivateKeyUtilities.ParsePrivateKeyBlob(pemContents);
            if (privateKey is not Ed25519PrivateKeyParameters ed25519)
            {
                return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.Unsupported, OperationErrorCode.Unsupported, OperationVerification.Failed).ConfigureAwait(false);
            }

            publicBytes = OpenSshPublicKeyUtilities.EncodePublicKey(ed25519.GeneratePublicKey());
            var fingerprint = OpenSshUserKeyFingerprint.FromBlob(publicBytes);
            if (expectedFingerprint is not null && !string.Equals(expectedFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.Corrupt, OperationErrorCode.Validation, OperationVerification.Failed).ConfigureAwait(false);
            }
            if (!TryValidateRegularPath(path + ".pub", out var publicPath, out pathError))
            {
                return await FailAsync(correlation, pathError!, OperationErrorCode.Validation, OperationVerification.NotRun).ConfigureAwait(false);
            }
            companionBytes = await ReadBoundedAsync(publicPath, cancellationToken, 16 * 1024).ConfigureAwait(false);
            var companion = new UTF8Encoding(false, true).GetString(companionBytes).Trim();
            using var material = new PublicKeyDeploymentMaterial(companion.AsSpan());
            if (companion.Contains('\n') || companion.Contains('\r')
                || !UbuntuAuthorizedKeysCommandCatalog.TryPrepare(material, out var prepared)
                || prepared is null
                || !string.Equals(prepared.CanonicalText, "ssh-ed25519 " + Convert.ToBase64String(publicBytes), StringComparison.Ordinal))
            {
                return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.Corrupt, OperationErrorCode.Validation, OperationVerification.Failed).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            consume?.Invoke(fileContents, publicBytes);
            var metadata = new ExistingSshKeyMetadata("ed25519", fingerprint);
            var operation = OperationResult.Success(correlation.OperationId, OperationState.Unchanged);
            await PublishAsync(DiagnosticEventCatalog.ExistingKeySelectionSucceeded, correlation, DiagnosticPhase.Verify, DiagnosticStatus.Succeeded, null, CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return ExistingSshKeySelectionResult.Success(operation, new ExistingSshKeyLocation(path, fingerprint), metadata);
        }
        catch (OperationCanceledException)
        {
            var operation = OperationResult.Cancellation(correlation.OperationId, OperationState.Unchanged);
            await PublishAsync(DiagnosticEventCatalog.ExistingKeySelectionCancelled, correlation, DiagnosticPhase.Validate, DiagnosticStatus.Cancelled, OperationErrorCode.Cancelled.ToStableCode(), CancellationToken.None).ConfigureAwait(false);
            return ExistingSshKeySelectionResult.Failure(operation, ExistingSshKeySelectionErrorCatalog.Cancelled);
        }
        catch (FileNotFoundException)
        {
            return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.Missing, OperationErrorCode.LocalIo, OperationVerification.NotRun).ConfigureAwait(false);
        }
        catch (DirectoryNotFoundException)
        {
            return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.Missing, OperationErrorCode.LocalIo, OperationVerification.NotRun).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.Permission, OperationErrorCode.LocalIo, OperationVerification.NotRun).ConfigureAwait(false);
        }
        catch (NativeOpenException exception) when (exception.IsAccessDenied)
        {
            return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.Permission, OperationErrorCode.LocalIo, OperationVerification.NotRun).ConfigureAwait(false);
        }
        catch (NativeOpenException exception) when (exception.IsMissing)
        {
            return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.Missing, OperationErrorCode.LocalIo, OperationVerification.NotRun).ConfigureAwait(false);
        }
        catch (NativeOpenException exception) when (exception.IsUnsafeTarget)
        {
            return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.InvalidTarget, OperationErrorCode.Validation, OperationVerification.NotRun).ConfigureAwait(false);
        }
        catch (WindowsOpenException exception) when (exception.IsAccessDenied)
        {
            return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.Permission, OperationErrorCode.LocalIo, OperationVerification.NotRun).ConfigureAwait(false);
        }
        catch (WindowsOpenException exception) when (exception.IsMissing)
        {
            return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.Missing, OperationErrorCode.LocalIo, OperationVerification.NotRun).ConfigureAwait(false);
        }
        catch (UnsafeKeySelectionPathException)
        {
            return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.InvalidTarget, OperationErrorCode.Validation, OperationVerification.NotRun).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.Corrupt, OperationErrorCode.Parse, OperationVerification.Failed).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.Corrupt, OperationErrorCode.Parse, OperationVerification.Failed).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.LocalIo, OperationErrorCode.LocalIo, OperationVerification.NotRun).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.Corrupt, OperationErrorCode.Parse, OperationVerification.Failed).ConfigureAwait(false);
        }
        finally
        {
            if (fileContents is not null)
            {
                CryptographicOperations.ZeroMemory(fileContents);
            }
            if (pemContents is not null)
            {
                CryptographicOperations.ZeroMemory(pemContents);
            }
            if (publicBytes is not null)
            {
                CryptographicOperations.ZeroMemory(publicBytes);
            }
            if (companionBytes is not null)
            {
                CryptographicOperations.ZeroMemory(companionBytes);
            }
        }
    }

    private async Task<ExistingSshKeySelectionResult> FailAsync(CorrelationIds correlation, string errorCode, OperationErrorCode operationError, OperationVerification verification)
    {
        var operation = OperationResult.Failure(correlation.OperationId, operationError, OperationState.Unchanged, verification);
        await PublishAsync(DiagnosticEventCatalog.ExistingKeySelectionFailed, correlation, DiagnosticPhase.Verify, DiagnosticStatus.Failed, errorCode, CancellationToken.None).ConfigureAwait(false);
        return ExistingSshKeySelectionResult.Failure(operation, errorCode);
    }

    private Task PublishAsync(string eventId, CorrelationIds correlation, DiagnosticPhase phase, DiagnosticStatus status, string? errorCode, CancellationToken cancellationToken) =>
        diagnostics.WriteAsync(new StructuredDiagnosticEvent(eventId, "SSH key selection", status is DiagnosticStatus.Failed ? DiagnosticLevel.Error : DiagnosticLevel.Information,
            correlation.ForStep(phase.ToString().ToLowerInvariant()), phase, status,
            status == DiagnosticStatus.Succeeded ? "Existing SSH key was validated." : status == DiagnosticStatus.Cancelled ? "Existing SSH key selection was cancelled." : status == DiagnosticStatus.Started ? "Existing SSH key selection started." : "Existing SSH key selection did not complete safely.",
            ErrorCode: errorCode, Action: "Select existing SSH key", Context: new Dictionary<string, DiagnosticValue> { ["file_class"] = new(DiagnosticDataClassification.PublicSafe, "local_ssh_private_key") }), cancellationToken);

    private static bool TryValidateRegularPath(string candidate, out string path, out string? error)
    {
        path = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate) || !string.Equals(Path.GetFullPath(candidate), candidate, StringComparison.Ordinal))
        {
            error = ExistingSshKeySelectionErrorCatalog.InvalidTarget;
            return false;
        }

        try
        {
            RejectReparsePointHierarchy(candidate);
            var attributes = File.GetAttributes(candidate);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                error = ExistingSshKeySelectionErrorCatalog.InvalidTarget;
                return false;
            }

            path = candidate;
            return true;
        }
        catch (FileNotFoundException) { error = ExistingSshKeySelectionErrorCatalog.Missing; return false; }
        catch (DirectoryNotFoundException)
        {
            // Framework path inspection presents a child of an existing regular
            // file as DirectoryNotFoundException. It is not an absent target:
            // its parent is an unsafe non-directory component and must retain
            // the same invalid-target boundary as native openat ENOTDIR.
            error = HasExistingNonDirectoryParent(candidate)
                ? ExistingSshKeySelectionErrorCatalog.InvalidTarget
                : ExistingSshKeySelectionErrorCatalog.Missing;
            return false;
        }
        catch (UnauthorizedAccessException) { error = ExistingSshKeySelectionErrorCatalog.Permission; return false; }
        catch (UnsafeKeySelectionPathException) { error = ExistingSshKeySelectionErrorCatalog.InvalidTarget; return false; }
        catch (IOException) { error = ExistingSshKeySelectionErrorCatalog.LocalIo; return false; }
    }

    private static bool HasExistingNonDirectoryParent(string path)
    {
        var root = Path.GetPathRoot(path) ?? throw new UnsafeKeySelectionPathException();
        var segments = path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        foreach (var segment in segments[..^1])
        {
            current = Path.Combine(current, segment);
            try
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.Directory) == 0)
                {
                    return true;
                }
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
        }

        return false;
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken cancellationToken, int maximumBytes = MaximumPrivateKeyBytes)
    {
        await using var stream = OpenNoFollowReadStream(path);
        if (stream.Length <= 0 || stream.Length > maximumBytes)
        {
            throw new InvalidDataException();
        }
        var bytes = new byte[(int)stream.Length];
        try
        {
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new InvalidDataException();
                }
                offset += read;
            }
            if (stream.Length != bytes.Length || stream.ReadByte() != -1)
            {
                throw new InvalidDataException();
            }
            return bytes;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw;
        }
    }

    private static FileStream OpenNoFollowReadStream(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return OpenWindowsSafeReadStream(path);
        }

        var segments = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var directoryHandle = OpenUnix("/", UnixOpenFlags.ReadOnly | UnixOpenFlags.Directory | UnixOpenFlags.NoFollow);
        try
        {
            for (var index = 0; index < segments.Length - 1; index++)
            {
                var next = OpenUnixAt(directoryHandle, segments[index], UnixOpenFlags.ReadOnly | UnixOpenFlags.Directory | UnixOpenFlags.NoFollow, requiredDirectory: true);
                directoryHandle.Dispose();
                directoryHandle = next;
            }

            var fileHandle = OpenUnixAt(directoryHandle, segments[^1], UnixOpenFlags.ReadOnly | UnixOpenFlags.NoFollow | UnixOpenFlags.NonBlocking, requiredDirectory: false);
            try
            {
                VerifyRegularUnixFile(fileHandle);
                return new FileStream(fileHandle, FileAccess.Read, 4096, isAsync: false);
            }
            catch
            {
                fileHandle.Dispose();
                throw;
            }
        }
        finally
        {
            directoryHandle.Dispose();
        }
    }

    private static SafeFileHandle OpenUnix(string path, UnixOpenFlags flags)
    {
        var descriptor = OperatingSystem.IsMacOS() ? OpenMac(path, TranslateUnixFlags(flags)) : OpenLinux(path, TranslateUnixFlags(flags));
        return CreateUnixHandle(descriptor, requiredDirectory: false);
    }

    private static SafeFileHandle OpenUnixAt(SafeFileHandle directory, string name, UnixOpenFlags flags, bool requiredDirectory)
    {
        var descriptor = OperatingSystem.IsMacOS() ? OpenAtMac(directory.DangerousGetHandle().ToInt32(), name, TranslateUnixFlags(flags)) : OpenAtLinux(directory.DangerousGetHandle().ToInt32(), name, TranslateUnixFlags(flags));
        return CreateUnixHandle(descriptor, requiredDirectory);
    }

    private static SafeFileHandle CreateUnixHandle(int descriptor, bool requiredDirectory)
    {
        if (descriptor < 0)
        {
            throw new NativeOpenException(Marshal.GetLastPInvokeError(), requiredDirectory);
        }

        return new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
    }

    private static int TranslateUnixFlags(UnixOpenFlags flags)
    {
        var translated = 0;
        if ((flags & UnixOpenFlags.NoFollow) != 0)
        {
            translated |= OperatingSystem.IsMacOS() ? 0x100 : 0x20000;
        }
        if ((flags & UnixOpenFlags.Directory) != 0 && !OperatingSystem.IsMacOS())
        {
            translated |= 0x10000;
        }
        if ((flags & UnixOpenFlags.NonBlocking) != 0)
        {
            translated |= OperatingSystem.IsMacOS() ? 0x4 : 0x800;
        }
        return translated;
    }

    private static void VerifyRegularUnixFile(SafeFileHandle handle)
    {
        const int statBufferLength = 256;
        var statBuffer = Marshal.AllocHGlobal(statBufferLength);
        try
        {
            var status = OperatingSystem.IsMacOS()
                ? FStatMac(handle.DangerousGetHandle().ToInt32(), statBuffer)
                : FStatLinux(handle.DangerousGetHandle().ToInt32(), statBuffer);
            if (status != 0)
            {
                throw new NativeOpenException(Marshal.GetLastPInvokeError(), requiredDirectory: false);
            }

            // Darwin places st_mode after the 32-bit device field. Linux lays it out
            // differently on its two supported 64-bit ABIs; unsupported ABIs fail closed.
            var mode = OperatingSystem.IsMacOS()
                ? (ushort)Marshal.ReadInt16(statBuffer, 4)
                : RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => Marshal.ReadInt32(statBuffer, 24),
                    Architecture.Arm64 => Marshal.ReadInt32(statBuffer, 16),
                    _ => throw new UnsafeKeySelectionPathException(),
                };
            const int fileTypeMask = 0xF000;
            const int regularFile = 0x8000;
            if ((mode & fileTypeMask) != regularFile)
            {
                throw new UnsafeKeySelectionPathException();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(statBuffer);
        }
    }

    private static FileStream OpenWindowsSafeReadStream(string path)
    {
        var handle = CreateWindowsHandle(path);
        try
        {
            VerifyRegularWindowsFile(handle, path);
            return new FileStream(handle, FileAccess.Read, 4096, isAsync: false);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static SafeFileHandle CreateWindowsHandle(string path)
    {
        const uint genericRead = 0x80000000;
        const uint fileShareRead = 0x00000001;
        const uint openExisting = 3;
        const uint fileFlagSequentialScan = 0x08000000;
        const uint fileFlagOpenReparsePoint = 0x00200000;
        const uint fileFlagBackupSemantics = 0x02000000;
        var handle = CreateFileWindows(path, genericRead, fileShareRead, IntPtr.Zero, openExisting, fileFlagSequentialScan | fileFlagOpenReparsePoint | fileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new WindowsOpenException(Marshal.GetLastPInvokeError());
        }

        return handle;
    }

    private static void VerifyRegularWindowsFile(SafeFileHandle handle, string expectedPath)
    {
        const uint fileTypeDisk = 1;
        if (GetFileTypeWindows(handle) != fileTypeDisk)
        {
            throw new UnsafeKeySelectionPathException();
        }

        if (!GetFileInformationByHandleWindows(handle, out var information))
        {
            throw new WindowsOpenException(Marshal.GetLastPInvokeError());
        }

        if ((information.FileAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
            || !WindowsPathsMatch(expectedPath, GetFinalWindowsPath(handle)))
        {
            throw new UnsafeKeySelectionPathException();
        }
    }

    private static string GetFinalWindowsPath(SafeFileHandle handle)
    {
        var length = GetFinalPathNameByHandleWindows(handle, null, 0, 0);
        if (length == 0)
        {
            throw new WindowsOpenException(Marshal.GetLastPInvokeError());
        }

        var buffer = new char[checked((int)length + 1)];
        var copied = GetFinalPathNameByHandleWindows(handle, buffer, (uint)buffer.Length, 0);
        if (copied == 0 || copied >= buffer.Length)
        {
            throw new WindowsOpenException(Marshal.GetLastPInvokeError());
        }

        return new string(buffer, 0, checked((int)copied));
    }

    private static bool WindowsPathsMatch(string expectedPath, string openedPath)
    {
        const string extendedPrefix = @"\\?\";
        const string extendedUncPrefix = @"\\?\UNC\";
        if (openedPath.StartsWith(extendedUncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            openedPath = @"\\" + openedPath[extendedUncPrefix.Length..];
        }
        else if (openedPath.StartsWith(extendedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            openedPath = openedPath[extendedPrefix.Length..];
        }

        return string.Equals(Path.GetFullPath(expectedPath), Path.GetFullPath(openedPath), StringComparison.OrdinalIgnoreCase);
    }

    private static void RejectReparsePointHierarchy(string path)
    {
        var root = Path.GetPathRoot(path) ?? throw new UnsafeKeySelectionPathException();
        var current = root;
        foreach (var segment in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnsafeKeySelectionPathException();
            }
        }
    }

    private static OpenSshEnvelope ClassifyOpenSshEnvelope(ReadOnlySpan<byte> blob)
    {
        if (!blob.StartsWith(OpenSshMagic) || !TryReadSshString(blob[OpenSshMagic.Length..], out var cipher))
        {
            return OpenSshEnvelope.Corrupt;
        }
        return cipher.SequenceEqual("none"u8) ? OpenSshEnvelope.Unencrypted : OpenSshEnvelope.Encrypted;
    }

    private static bool TryReadSshString(ReadOnlySpan<byte> bytes, out ReadOnlySpan<byte> value)
    {
        value = default;
        if (bytes.Length < 4)
        {
            return false;
        }
        var length = (int)((uint)bytes[0] << 24 | (uint)bytes[1] << 16 | (uint)bytes[2] << 8 | bytes[3]);
        if (length < 0 || length > bytes.Length - 4)
        {
            return false;
        }
        value = bytes.Slice(4, length);
        return true;
    }

    private enum OpenSshEnvelope { Unencrypted, Encrypted, Corrupt }

    private sealed class SilentKeyReadSink : IDiagnosticSink
    {
        // The enclosing key-auth workflow owns correlated safe outcome events.
        public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Flags]
    private enum UnixOpenFlags
    {
        ReadOnly = 0,
        NoFollow = 1,
        Directory = 2,
        NonBlocking = 4,
    }

    private sealed class UnsafeKeySelectionPathException : IOException;

    private sealed class NativeOpenException(int error, bool requiredDirectory) : IOException
    {
        private const int PermissionDenied = 13;
        private const int PermissionNotPermitted = 1;
        private const int NoEntry = 2;
        private const int NotDirectory = 20;
        private const int LinuxTooManySymbolicLinks = 40;
        private const int DarwinTooManySymbolicLinks = 62;

        public bool IsAccessDenied => error is PermissionDenied or PermissionNotPermitted;
        public bool IsMissing => error == NoEntry;
        public bool IsUnsafeTarget => error is LinuxTooManySymbolicLinks or DarwinTooManySymbolicLinks
            || requiredDirectory && error == NotDirectory;
    }

    private sealed class WindowsOpenException(int error) : IOException
    {
        private const int AccessDenied = 5;
        private const int FileNotFound = 2;
        private const int PathNotFound = 3;

        public bool IsAccessDenied => error == AccessDenied;
        public bool IsMissing => error is FileNotFound or PathNotFound;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsByHandleFileInformation
    {
        public FileAttributes FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

#pragma warning disable CA2101 // Unix open/openat paths are explicitly ANSI UTF-8 marshalled below.
    [DllImport("libc", EntryPoint = "open", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int OpenLinux([MarshalAs(UnmanagedType.LPStr)] string path, int flags);
    [DllImport("libc", EntryPoint = "openat", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int OpenAtLinux(int directory, [MarshalAs(UnmanagedType.LPStr)] string path, int flags);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "open", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int OpenMac([MarshalAs(UnmanagedType.LPStr)] string path, int flags);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "openat", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int OpenAtMac(int directory, [MarshalAs(UnmanagedType.LPStr)] string path, int flags);
    [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static extern int FStatLinux(int descriptor, IntPtr statBuffer);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "fstat", SetLastError = true)]
    private static extern int FStatMac(int descriptor, IntPtr statBuffer);
#pragma warning restore CA2101
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileWindows(string path, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleWindows(SafeFileHandle handle, out WindowsByHandleFileInformation information);
    [DllImport("kernel32.dll", EntryPoint = "GetFileType", SetLastError = true)]
    private static extern uint GetFileTypeWindows(SafeFileHandle handle);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandleWindows(SafeFileHandle handle, char[]? path, uint length, uint flags);
}

internal interface IExistingSshKeySelectionObserver { void BeforeRead(string path); }
internal sealed class NoExistingSshKeySelectionObserver : IExistingSshKeySelectionObserver
{
    public static NoExistingSshKeySelectionObserver Instance { get; } = new();
    public void BeforeRead(string path) { }
}
