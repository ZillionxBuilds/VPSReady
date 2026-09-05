using System.Security.Cryptography;
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
        catch (Exception) when (IsParserException())
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
        catch (IOException) { error = ExistingSshKeySelectionErrorCatalog.LocalIo; return false; }
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
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

    private static bool IsParserException() => true;

    private enum OpenSshEnvelope { Unencrypted, Encrypted, Corrupt }
}

internal interface IExistingSshKeySelectionObserver { void BeforeRead(string path); }
internal sealed class NoExistingSshKeySelectionObserver : IExistingSshKeySelectionObserver
{
    public static NoExistingSshKeySelectionObserver Instance { get; } = new();
    public void BeforeRead(string path) { }
}
