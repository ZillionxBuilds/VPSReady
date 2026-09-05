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

    public async Task<ExistingSshKeySelectionResult> SelectAsync(
        ExistingSshKeySelectionRequest request,
        CorrelationIds correlation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(correlation);
        await PublishAsync(DiagnosticEventCatalog.ExistingKeySelectionStarted, correlation, DiagnosticPhase.Validate, DiagnosticStatus.Started, null, CancellationToken.None).ConfigureAwait(false);
        byte[]? fileContents = null;
        byte[]? pemContents = null;
        byte[]? publicBytes = null;
        byte[]? fingerprintBytes = null;
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

            publicBytes = ed25519.GeneratePublicKey().GetEncoded();
            fingerprintBytes = SHA256.HashData(publicBytes);
            var metadata = new ExistingSshKeyMetadata("ed25519", $"SHA256:{Convert.ToBase64String(fingerprintBytes).TrimEnd('=')}");
            var operation = OperationResult.Success(correlation.OperationId, OperationState.Unchanged);
            await PublishAsync(DiagnosticEventCatalog.ExistingKeySelectionSucceeded, correlation, DiagnosticPhase.Verify, DiagnosticStatus.Succeeded, null, CancellationToken.None).ConfigureAwait(false);
            return ExistingSshKeySelectionResult.Success(operation, new ExistingSshKeyLocation(path), metadata);
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
        catch (NativeOpenException exception) when (exception.IsNoFollowViolation)
        {
            return await FailAsync(correlation, ExistingSshKeySelectionErrorCatalog.InvalidTarget, OperationErrorCode.Validation, OperationVerification.NotRun).ConfigureAwait(false);
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
            if (fingerprintBytes is not null)
            {
                CryptographicOperations.ZeroMemory(fingerprintBytes);
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
        catch (DirectoryNotFoundException) { error = ExistingSshKeySelectionErrorCatalog.Missing; return false; }
        catch (UnauthorizedAccessException) { error = ExistingSshKeySelectionErrorCatalog.Permission; return false; }
        catch (UnsafeKeySelectionPathException) { error = ExistingSshKeySelectionErrorCatalog.InvalidTarget; return false; }
        catch (IOException) { error = ExistingSshKeySelectionErrorCatalog.LocalIo; return false; }
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = OpenNoFollowReadStream(path);
        if (stream.Length is <= 0 or > MaximumPrivateKeyBytes)
        {
            throw new InvalidDataException();
        }
        var bytes = new byte[(int)stream.Length];
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

        return bytes;
    }

    private static FileStream OpenNoFollowReadStream(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new UnsafeKeySelectionPathException();
        }

        var segments = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var directoryHandle = OpenUnix("/", UnixOpenFlags.ReadOnly | UnixOpenFlags.Directory | UnixOpenFlags.NoFollow);
        try
        {
            for (var index = 0; index < segments.Length - 1; index++)
            {
                var next = OpenUnixAt(directoryHandle, segments[index], UnixOpenFlags.ReadOnly | UnixOpenFlags.Directory | UnixOpenFlags.NoFollow);
                directoryHandle.Dispose();
                directoryHandle = next;
            }

            var fileHandle = OpenUnixAt(directoryHandle, segments[^1], UnixOpenFlags.ReadOnly | UnixOpenFlags.NoFollow | UnixOpenFlags.NonBlocking);
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
        return CreateUnixHandle(descriptor);
    }

    private static SafeFileHandle OpenUnixAt(SafeFileHandle directory, string name, UnixOpenFlags flags)
    {
        var descriptor = OperatingSystem.IsMacOS() ? OpenAtMac(directory.DangerousGetHandle().ToInt32(), name, TranslateUnixFlags(flags)) : OpenAtLinux(directory.DangerousGetHandle().ToInt32(), name, TranslateUnixFlags(flags));
        return CreateUnixHandle(descriptor);
    }

    private static SafeFileHandle CreateUnixHandle(int descriptor)
    {
        if (descriptor < 0)
        {
            throw new NativeOpenException(Marshal.GetLastPInvokeError());
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
                throw new NativeOpenException(Marshal.GetLastPInvokeError());
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

    [Flags]
    private enum UnixOpenFlags
    {
        ReadOnly = 0,
        NoFollow = 1,
        Directory = 2,
        NonBlocking = 4,
    }

    private sealed class UnsafeKeySelectionPathException : IOException;

    private sealed class NativeOpenException(int error) : IOException
    {
        private const int PermissionDenied = 13;
        private const int PermissionNotPermitted = 1;
        private const int NoEntry = 2;
        private const int LinuxTooManySymbolicLinks = 40;
        private const int DarwinTooManySymbolicLinks = 62;

        public bool IsAccessDenied => error is PermissionDenied or PermissionNotPermitted;
        public bool IsMissing => error == NoEntry;
        public bool IsNoFollowViolation => error is LinuxTooManySymbolicLinks or DarwinTooManySymbolicLinks;
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
}

internal interface IExistingSshKeySelectionObserver { void BeforeRead(string path); }
internal sealed class NoExistingSshKeySelectionObserver : IExistingSshKeySelectionObserver
{
    public static NoExistingSshKeySelectionObserver Instance { get; } = new();
    public void BeforeRead(string path) { }
}
