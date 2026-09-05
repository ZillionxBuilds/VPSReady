using System.Collections.Concurrent;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Utilities;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;

namespace VpsReady.Infrastructure.Local;

/// <summary>
/// The only production writer for generated local SSH pairs. It uses the
/// Principal-approved Bouncy Castle format utilities and deliberately has no
/// process runner or ssh-keygen fallback.
/// </summary>
public sealed class Ed25519OpenSshKeyPairGenerator : ILocalEd25519KeyGenerator
{
    private const string TransactionDirectoryPrefix = ".vpsready-keytxn-";
    private const string ManifestFileName = "manifest.json";
    private const string StagedPrivateFileName = "private.key";
    private const string StagedPublicFileName = "public.key";
    private const int ManifestVersion = 1;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> TargetLocks = new(StringComparer.Ordinal);
    private readonly IDiagnosticSink diagnostics;
    private readonly IKeyPairTransactionFaultInjector faultInjector;

    public Ed25519OpenSshKeyPairGenerator(IDiagnosticSink diagnostics)
        : this(diagnostics, NoKeyPairTransactionFaultInjector.Instance)
    {
    }

    internal Ed25519OpenSshKeyPairGenerator(IDiagnosticSink diagnostics, IKeyPairTransactionFaultInjector faultInjector)
    {
        this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        this.faultInjector = faultInjector ?? throw new ArgumentNullException(nameof(faultInjector));
    }

    public async Task<LocalEd25519KeyGenerationResult> GenerateAsync(
        LocalEd25519KeyGenerationRequest request,
        CorrelationIds correlation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(correlation);

        if (!TryCreatePaths(request, out var paths))
        {
            return await FailAsync(
                correlation,
                LocalEd25519KeyGenerationErrorCatalog.InvalidTarget,
                OperationErrorCode.Validation,
                OperationState.Unchanged,
                OperationVerification.NotRun,
                OperationRecovery.NotRequired,
                cancellationToken).ConfigureAwait(false);
        }

        var gate = TargetLocks.GetOrAdd(paths.PrivateFinalPath, _ => new SemaphoreSlim(1, 1));
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await CancelAsync(correlation, OperationState.Unchanged, OperationVerification.NotRun).ConfigureAwait(false);
        }

        byte[]? privatePayload = null;
        string? transactionDirectory = null;
        try
        {
            await PublishAsync(
                DiagnosticEventCatalog.LocalKeyGenerationStarted,
                correlation,
                DiagnosticPhase.Validate,
                DiagnosticStatus.Started,
                errorCode: null,
                cancellationToken).ConfigureAwait(false);

            await RecoverMatchingTransactionsAsync(paths, cancellationToken).ConfigureAwait(false);
            ValidateNoCollision(paths);
            cancellationToken.ThrowIfCancellationRequested();

            transactionDirectory = CreateTransactionDirectory(paths);
            faultInjector.ThrowIfInjected(KeyPairTransactionStage.StagingCreated);
            WriteManifest(transactionDirectory, paths);

            var pair = GenerateKeyPair();
            var privateKey = (Ed25519PrivateKeyParameters)pair.Private;
            var publicKey = (Ed25519PublicKeyParameters)pair.Public;
            privatePayload = OpenSshPrivateKeyUtilities.EncodePrivateKey(privateKey);
            var publicPayload = OpenSshPublicKeyUtilities.EncodePublicKey(publicKey);
            try
            {
                await WritePrivatePemAsync(Path.Combine(transactionDirectory, StagedPrivateFileName), privatePayload, cancellationToken).ConfigureAwait(false);
                faultInjector.ThrowIfInjected(KeyPairTransactionStage.PrivateStaged);
                await WritePublicKeyAsync(Path.Combine(transactionDirectory, StagedPublicFileName), publicPayload, cancellationToken).ConfigureAwait(false);
                faultInjector.ThrowIfInjected(KeyPairTransactionStage.PublicStaged);

                VerifyPairCorrespondence(
                    Path.Combine(transactionDirectory, StagedPrivateFileName),
                    Path.Combine(transactionDirectory, StagedPublicFileName));
                faultInjector.ThrowIfInjected(KeyPairTransactionStage.StagedPairVerified);

                FinalizeNoReplace(Path.Combine(transactionDirectory, StagedPrivateFileName), paths.PrivateFinalPath);
                VerifyPrivatePermissions(paths.PrivateFinalPath);
                faultInjector.ThrowIfInjected(KeyPairTransactionStage.PrivateFinalized);

                FinalizeNoReplace(Path.Combine(transactionDirectory, StagedPublicFileName), paths.PublicFinalPath);
                faultInjector.ThrowIfInjected(KeyPairTransactionStage.PublicFinalized);

                VerifyPairCorrespondence(paths.PrivateFinalPath, paths.PublicFinalPath);
                VerifyPrivatePermissions(paths.PrivateFinalPath);
                faultInjector.ThrowIfInjected(KeyPairTransactionStage.FinalPairVerified);

                DeleteTransactionDirectoryIfOwned(paths, transactionDirectory);
                transactionDirectory = null;

                var operation = OperationResult.Success(correlation.OperationId);
                await PublishAsync(
                    DiagnosticEventCatalog.LocalKeyGenerationSucceeded,
                    correlation,
                    DiagnosticPhase.Verify,
                    DiagnosticStatus.Succeeded,
                    errorCode: null,
                    cancellationToken).ConfigureAwait(false);
                return LocalEd25519KeyGenerationResult.Success(
                    operation,
                    new LocalEd25519KeyPairLocation(paths.PrivateFinalPath, paths.PublicFinalPath));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(publicPayload);
            }
        }
        catch (OperationCanceledException)
        {
            var recovery = RecoverAfterFailure(paths, transactionDirectory);
            return await CancelAsync(correlation, recovery.State, recovery.Verification).ConfigureAwait(false);
        }
        catch (KeyPairGenerationException exception)
        {
            var recovery = RecoverAfterFailure(paths, transactionDirectory);
            var errorCode = recovery.Failed ? LocalEd25519KeyGenerationErrorCatalog.Recovery : exception.StableCode;
            var operationError = recovery.Failed ? OperationErrorCode.Recovery : exception.OperationError;
            return await FailAsync(
                correlation,
                errorCode,
                operationError,
                recovery.State,
                recovery.Verification,
                recovery.Recovery,
                cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            var recovery = RecoverAfterFailure(paths, transactionDirectory);
            return await FailAsync(
                correlation,
                recovery.Failed ? LocalEd25519KeyGenerationErrorCatalog.Recovery : LocalEd25519KeyGenerationErrorCatalog.Permission,
                recovery.Failed ? OperationErrorCode.Recovery : OperationErrorCode.LocalIo,
                recovery.State,
                recovery.Verification,
                recovery.Recovery,
                cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            var recovery = RecoverAfterFailure(paths, transactionDirectory);
            return await FailAsync(
                correlation,
                recovery.Failed ? LocalEd25519KeyGenerationErrorCatalog.Recovery : LocalEd25519KeyGenerationErrorCatalog.LocalIo,
                recovery.Failed ? OperationErrorCode.Recovery : OperationErrorCode.LocalIo,
                recovery.State,
                recovery.Verification,
                recovery.Recovery,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            var recovery = RecoverAfterFailure(paths, transactionDirectory);
            return await FailAsync(
                correlation,
                recovery.Failed ? LocalEd25519KeyGenerationErrorCatalog.Recovery : LocalEd25519KeyGenerationErrorCatalog.Format,
                recovery.Failed ? OperationErrorCode.Recovery : OperationErrorCode.LocalIo,
                recovery.State,
                recovery.Verification,
                recovery.Recovery,
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            var recovery = RecoverAfterFailure(paths, transactionDirectory);
            return await FailAsync(
                correlation,
                recovery.Failed ? LocalEd25519KeyGenerationErrorCatalog.Recovery : LocalEd25519KeyGenerationErrorCatalog.Format,
                recovery.Failed ? OperationErrorCode.Recovery : OperationErrorCode.LocalIo,
                recovery.State,
                recovery.Verification,
                recovery.Recovery,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (privatePayload is not null)
            {
                CryptographicOperations.ZeroMemory(privatePayload);
            }

            gate.Release();
        }
    }

    private async Task<LocalEd25519KeyGenerationResult> FailAsync(
        CorrelationIds correlation,
        string generationErrorCode,
        OperationErrorCode operationError,
        OperationState state,
        OperationVerification verification,
        OperationRecovery recovery,
        CancellationToken cancellationToken)
    {
        var operation = OperationResult.Failure(correlation.OperationId, operationError, state, verification, recovery);
        await PublishAsync(
            DiagnosticEventCatalog.LocalKeyGenerationFailed,
            correlation,
            recovery == OperationRecovery.Failed ? DiagnosticPhase.Recovery : DiagnosticPhase.Apply,
            DiagnosticStatus.Failed,
            generationErrorCode,
            CancellationToken.None).ConfigureAwait(false);
        return LocalEd25519KeyGenerationResult.Failure(operation, generationErrorCode);
    }

    private async Task<LocalEd25519KeyGenerationResult> CancelAsync(
        CorrelationIds correlation,
        OperationState state,
        OperationVerification verification)
    {
        var operation = OperationResult.Cancellation(correlation.OperationId, state, verification);
        await PublishAsync(
            DiagnosticEventCatalog.LocalKeyGenerationCancelled,
            correlation,
            DiagnosticPhase.Recovery,
            DiagnosticStatus.Cancelled,
            OperationErrorCode.Cancelled.ToStableCode(),
            CancellationToken.None).ConfigureAwait(false);
        return LocalEd25519KeyGenerationResult.Failure(operation, LocalEd25519KeyGenerationErrorCatalog.Cancelled);
    }

    private Task PublishAsync(
        string eventId,
        CorrelationIds correlation,
        DiagnosticPhase phase,
        DiagnosticStatus status,
        string? errorCode,
        CancellationToken cancellationToken) => diagnostics.WriteAsync(
            new StructuredDiagnosticEvent(
                eventId,
                "SSH key generation",
                status is DiagnosticStatus.Failed ? DiagnosticLevel.Error : DiagnosticLevel.Information,
                correlation.ForStep(phase.ToString().ToLowerInvariant()),
                phase,
                status,
                status switch
                {
                    DiagnosticStatus.Started => "Local SSH key generation started.",
                    DiagnosticStatus.Succeeded => "Local SSH key pair was generated and verified.",
                    DiagnosticStatus.Cancelled => "Local SSH key generation was cancelled.",
                    _ => "Local SSH key generation did not complete safely.",
                },
                ErrorCode: errorCode,
                Action: "Generate local SSH key",
                Context: new Dictionary<string, DiagnosticValue>
                {
                    ["algorithm"] = new(DiagnosticDataClassification.PublicSafe, "ed25519"),
                    ["file_class"] = new(DiagnosticDataClassification.PublicSafe, "local_ssh_key_pair"),
                }),
            cancellationToken);

    private static bool TryCreatePaths(LocalEd25519KeyGenerationRequest request, out KeyPairPaths paths)
    {
        paths = default;
        if (string.IsNullOrWhiteSpace(request.PrivateKeyPath) || !Path.IsPathFullyQualified(request.PrivateKeyPath))
        {
            return false;
        }

        var privatePath = Path.GetFullPath(request.PrivateKeyPath);
        if (!string.Equals(privatePath, request.PrivateKeyPath, StringComparison.Ordinal)
            || string.Equals(Path.GetExtension(privatePath), ".pub", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(Path.GetFileName(privatePath))
            || Path.GetFileName(privatePath).StartsWith(TransactionDirectoryPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parentDirectory = Path.GetDirectoryName(privatePath);
        if (string.IsNullOrWhiteSpace(parentDirectory) || !Directory.Exists(parentDirectory))
        {
            return false;
        }

        try
        {
            LocalPathPolicy.ValidateRoot(parentDirectory);
            RejectReparsePointHierarchy(parentDirectory);
            RejectReparsePoint(privatePath);
            RejectReparsePoint(privatePath + ".pub");
        }
        catch (IOException)
        {
            return false;
        }

        paths = new KeyPairPaths(privatePath, privatePath + ".pub", parentDirectory);
        return true;
    }

    private static void ValidateNoCollision(KeyPairPaths paths)
    {
        if (PathExists(paths.PrivateFinalPath) || PathExists(paths.PublicFinalPath))
        {
            throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Collision, OperationErrorCode.LocalIo);
        }
    }

    private static string CreateTransactionDirectory(KeyPairPaths paths)
    {
        var transactionDirectory = Path.Combine(paths.ParentDirectory, $"{TransactionDirectoryPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(transactionDirectory);
        RestrictDirectoryPermissions(transactionDirectory);
        return transactionDirectory;
    }

    private static void WriteManifest(string transactionDirectory, KeyPairPaths paths)
    {
        var manifest = new TransactionManifest(
            ManifestVersion,
            GetTransactionId(transactionDirectory),
            Path.GetFileName(paths.PrivateFinalPath),
            Path.GetFileName(paths.PublicFinalPath));
        var manifestPath = Path.Combine(transactionDirectory, ManifestFileName);
        using var stream = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        JsonSerializer.Serialize(stream, manifest);
        stream.Flush(flushToDisk: true);
    }

    private static AsymmetricCipherKeyPair GenerateKeyPair()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        return generator.GenerateKeyPair();
    }

    private static async Task WritePrivatePemAsync(string path, byte[] privatePayload, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 4096, leaveOpen: true);
        var pemWriter = new Org.BouncyCastle.OpenSsl.PemWriter(writer);
        pemWriter.WriteObject(new Org.BouncyCastle.Utilities.IO.Pem.PemObject("OPENSSH PRIVATE KEY", privatePayload));
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
        VerifyPrivatePermissions(path);
    }

    private static async Task WritePublicKeyAsync(string path, byte[] publicPayload, CancellationToken cancellationToken)
    {
        var publicLine = $"ssh-ed25519 {Convert.ToBase64String(publicPayload)}{Environment.NewLine}";
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 4096, leaveOpen: true);
        await writer.WriteAsync(publicLine.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static void FinalizeNoReplace(string stagedPath, string finalPath)
    {
        RejectReparsePoint(finalPath);
        if (PathExists(finalPath))
        {
            throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Collision, OperationErrorCode.LocalIo);
        }

        File.Move(stagedPath, finalPath, overwrite: false);
    }

    private static void VerifyPairCorrespondence(string privatePath, string publicPath)
    {
        EnsureRegularNonReparseFile(privatePath);
        EnsureRegularNonReparseFile(publicPath);
        var privateKey = ReadPrivateKey(privatePath);
        var expectedPublic = privateKey.GeneratePublicKey().GetEncoded();
        var actualPublic = ReadPublicKey(publicPath).GetEncoded();
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expectedPublic, actualPublic))
            {
                throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Verification, OperationErrorCode.Verification);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedPublic);
            CryptographicOperations.ZeroMemory(actualPublic);
        }
    }

    private static Ed25519PrivateKeyParameters ReadPrivateKey(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var textReader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: false);
        var pemObject = new Org.BouncyCastle.OpenSsl.PemReader(textReader).ReadPemObject();
        if (pemObject is null || !string.Equals(pemObject.Type, "OPENSSH PRIVATE KEY", StringComparison.Ordinal))
        {
            throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Verification, OperationErrorCode.Verification);
        }

        var content = pemObject.Content;
        try
        {
            return OpenSshPrivateKeyUtilities.ParsePrivateKeyBlob(content) as Ed25519PrivateKeyParameters
                ?? throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Verification, OperationErrorCode.Verification);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(content);
        }
    }

    private static Ed25519PublicKeyParameters ReadPublicKey(string path)
    {
        var line = File.ReadAllText(path, Encoding.ASCII).Trim();
        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length != 2 || !string.Equals(fields[0], "ssh-ed25519", StringComparison.Ordinal))
        {
            throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Verification, OperationErrorCode.Verification);
        }

        byte[] encoded;
        try
        {
            encoded = Convert.FromBase64String(fields[1]);
        }
        catch (FormatException)
        {
            throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Verification, OperationErrorCode.Verification);
        }

        try
        {
            return OpenSshPublicKeyUtilities.ParsePublicKey(encoded) as Ed25519PublicKeyParameters
                ?? throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Verification, OperationErrorCode.Verification);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
        }
    }

    private static void VerifyPrivatePermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            VerifyWindowsPrivateFilePermissions(path);
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var mode = File.GetUnixFileMode(path);
        var disallowed = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                         UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if ((mode & disallowed) != 0)
        {
            throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Permission, OperationErrorCode.LocalIo);
        }
    }

    private static void RestrictDirectoryPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            RestrictWindowsDirectoryPermissions(path);
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var mode = File.GetUnixFileMode(path);
        var disallowed = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                         UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if ((mode & disallowed) != 0)
        {
            throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Permission, OperationErrorCode.LocalIo);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyWindowsPrivateFilePermissions(string path)
    {
        ApplyWindowsCurrentUserOnlyAccess(path, isDirectory: false);
    }

    [SupportedOSPlatform("windows")]
    private static void RestrictWindowsDirectoryPermissions(string path)
    {
        ApplyWindowsCurrentUserOnlyAccess(path, isDirectory: true);
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindowsCurrentUserOnlyAccess(string path, bool isDirectory)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Permission, OperationErrorCode.LocalIo);
        FileSystemSecurity security = isDirectory ? new DirectorySecurity() : new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(user);
        security.ResetAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));

        if (isDirectory)
        {
            new DirectoryInfo(path).SetAccessControl((DirectorySecurity)security);
        }
        else
        {
            new FileInfo(path).SetAccessControl((FileSecurity)security);
        }

        FileSystemSecurity applied = isDirectory
            ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        if (applied.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !owner.Equals(user))
        {
            throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Permission, OperationErrorCode.LocalIo);
        }

        var rules = applied.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .OfType<FileSystemAccessRule>()
            .ToArray();
        var currentUserCanFullyControl = rules.Any(rule =>
            rule.AccessControlType == AccessControlType.Allow
            && rule.IdentityReference is SecurityIdentifier ruleIdentity
            && ruleIdentity.Equals(user)
            && (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl);
        var otherIdentityCanAccess = rules.Any(rule =>
            rule.AccessControlType == AccessControlType.Allow
            && rule.IdentityReference is SecurityIdentifier ruleIdentity
            && !ruleIdentity.Equals(user));
        if (!currentUserCanFullyControl || otherIdentityCanAccess)
        {
            throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Permission, OperationErrorCode.LocalIo);
        }
    }

    private static Task RecoverMatchingTransactionsAsync(KeyPairPaths paths, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var directory in Directory.EnumerateDirectories(paths.ParentDirectory, $"{TransactionDirectoryPrefix}*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = RecoverTransaction(paths, directory);
        }

        return Task.CompletedTask;
    }

    private static RecoveryResult RecoverAfterFailure(KeyPairPaths paths, string? transactionDirectory)
    {
        if (transactionDirectory is null)
        {
            return RecoveryResult.Unchanged;
        }

        try
        {
            if (IsOwnedTransactionDirectory(transactionDirectory)
                && !Directory.EnumerateFileSystemEntries(transactionDirectory, "*", SearchOption.TopDirectoryOnly).Any())
            {
                DeleteEmptyTransactionDirectoryIfOwned(transactionDirectory);
                return RecoveryResult.Unchanged with { Recovery = OperationRecovery.Succeeded };
            }

            var finalPairRemains = RecoverTransaction(paths, transactionDirectory);
            return finalPairRemains
                ? new RecoveryResult(false, OperationState.Applied, OperationVerification.Passed, OperationRecovery.Succeeded)
                : RecoveryResult.Unchanged with { Recovery = OperationRecovery.Succeeded };
        }
        catch
        {
            return RecoveryResult.FailedResult;
        }
    }

    private static bool RecoverTransaction(KeyPairPaths paths, string transactionDirectory)
    {
        ValidateOwnedMatchingTransaction(paths, transactionDirectory);

        var stagedPrivate = Path.Combine(transactionDirectory, StagedPrivateFileName);
        var stagedPublic = Path.Combine(transactionDirectory, StagedPublicFileName);
        var privateFinalExists = PathExists(paths.PrivateFinalPath);
        var publicFinalExists = PathExists(paths.PublicFinalPath);
        var stagedPrivateExists = PathExists(stagedPrivate);
        var stagedPublicExists = PathExists(stagedPublic);

        if (privateFinalExists && publicFinalExists)
        {
            VerifyPairCorrespondence(paths.PrivateFinalPath, paths.PublicFinalPath);
            DeleteTransactionDirectoryIfOwned(paths, transactionDirectory);
            return true;
        }

        if (privateFinalExists && stagedPublicExists)
        {
            VerifyPairCorrespondence(paths.PrivateFinalPath, stagedPublic);
            File.Delete(paths.PrivateFinalPath);
            DeleteTransactionDirectoryIfOwned(paths, transactionDirectory);
            return false;
        }

        if (publicFinalExists && stagedPrivateExists)
        {
            VerifyPairCorrespondence(stagedPrivate, paths.PublicFinalPath);
            File.Delete(paths.PublicFinalPath);
            DeleteTransactionDirectoryIfOwned(paths, transactionDirectory);
            return false;
        }

        if (!privateFinalExists && !publicFinalExists && stagedPrivateExists && stagedPublicExists)
        {
            VerifyPairCorrespondence(stagedPrivate, stagedPublic);
            DeleteTransactionDirectoryIfOwned(paths, transactionDirectory);
            return false;
        }

        // The private payload is written before its public counterpart. A crash or
        // deterministic fault at that precise boundary leaves a restricted,
        // manifest-owned private staging file and no user-visible final files.
        // Validate that exact staged key before deleting only the transaction
        // directory. Any unexpected final, public-only, malformed, or tampered
        // state remains a hard recovery failure.
        if (!privateFinalExists && !publicFinalExists && stagedPrivateExists && !stagedPublicExists)
        {
            VerifyStagedPrivateKey(stagedPrivate);
            DeleteTransactionDirectoryIfOwned(paths, transactionDirectory);
            return false;
        }

        // A restart can occur immediately after the manifest is made durable and
        // before a staged key is created. There is no payload to preserve, and the
        // manifest-matching, allow-listed transaction directory is safe to remove.
        if (!privateFinalExists && !publicFinalExists && !stagedPrivateExists && !stagedPublicExists)
        {
            DeleteTransactionDirectoryIfOwned(paths, transactionDirectory);
            return false;
        }

        throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Recovery, OperationErrorCode.Recovery);
    }

    private static bool TryReadManifest(string transactionDirectory, out TransactionManifest manifest)
    {
        manifest = default!;
        try
        {
            var path = Path.Combine(transactionDirectory, ManifestFileName);
            if (!IsRegularNonReparseFile(path))
            {
                return false;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024, FileOptions.SequentialScan);
            if (stream.Length > 1024)
            {
                return false;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
            var serialized = reader.ReadToEnd();
            if (!IsRegularNonReparseFile(path))
            {
                return false;
            }

            manifest = JsonSerializer.Deserialize<TransactionManifest>(serialized)!;
            return manifest is not null;
        }
        catch (IOException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ManifestMatches(KeyPairPaths paths, string transactionDirectory, TransactionManifest manifest) =>
        manifest.Version == ManifestVersion
        && string.Equals(manifest.TransactionId, GetTransactionId(transactionDirectory), StringComparison.Ordinal)
        && string.Equals(manifest.PrivateFileName, Path.GetFileName(paths.PrivateFinalPath), StringComparison.Ordinal)
        && string.Equals(manifest.PublicFileName, Path.GetFileName(paths.PublicFinalPath), StringComparison.Ordinal)
        && IsSafeLeafName(manifest.PrivateFileName)
        && IsSafeLeafName(manifest.PublicFileName);

    private static bool IsSafeLeafName(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal)
        && value is not "." and not "..";

    private static bool IsOwnedTransactionDirectory(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Directory) != 0
                && (attributes & FileAttributes.ReparsePoint) == 0
                && Guid.TryParseExact(Path.GetFileName(path)[TransactionDirectoryPrefix.Length..], "N", out _);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string GetTransactionId(string transactionDirectory)
    {
        var name = Path.GetFileName(transactionDirectory);
        if (!name.StartsWith(TransactionDirectoryPrefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(name[TransactionDirectoryPrefix.Length..], "N", out var transactionId))
        {
            throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Recovery, OperationErrorCode.Recovery);
        }

        return transactionId.ToString("N");
    }

    private static void ValidateOwnedMatchingTransaction(KeyPairPaths paths, string transactionDirectory)
    {
        if (!IsOwnedTransactionDirectory(transactionDirectory)
            || !TryReadManifest(transactionDirectory, out var manifest)
            || !ManifestMatches(paths, transactionDirectory, manifest))
        {
            throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Recovery, OperationErrorCode.Recovery);
        }

        ValidateTransactionEntries(transactionDirectory);
    }

    private static void DeleteTransactionDirectoryIfOwned(KeyPairPaths paths, string transactionDirectory)
    {
        ValidateOwnedMatchingTransaction(paths, transactionDirectory);
        var entries = Directory.EnumerateFileSystemEntries(transactionDirectory, "*", SearchOption.TopDirectoryOnly).ToArray();
        foreach (var entry in entries)
        {
            EnsureRegularNonReparseFile(entry);
            File.Delete(entry);
        }

        Directory.Delete(transactionDirectory, recursive: false);
    }

    private static void DeleteEmptyTransactionDirectoryIfOwned(string transactionDirectory)
    {
        if (!IsOwnedTransactionDirectory(transactionDirectory)
            || Directory.EnumerateFileSystemEntries(transactionDirectory, "*", SearchOption.TopDirectoryOnly).Any())
        {
            throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Recovery, OperationErrorCode.Recovery);
        }

        Directory.Delete(transactionDirectory, recursive: false);
    }

    private static void ValidateTransactionEntries(string transactionDirectory)
    {
        var allowedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            ManifestFileName,
            StagedPrivateFileName,
            StagedPublicFileName,
        };
        var entries = Directory.EnumerateFileSystemEntries(transactionDirectory, "*", SearchOption.TopDirectoryOnly).ToArray();
        if (entries.Any(entry => !allowedFiles.Contains(Path.GetFileName(entry)) || !IsRegularNonReparseFile(entry)))
        {
            throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Recovery, OperationErrorCode.Recovery);
        }
    }

    private static bool PathExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static void VerifyStagedPrivateKey(string path)
    {
        EnsureRegularNonReparseFile(path);
        _ = ReadPrivateKey(path);
    }

    private static void EnsureRegularNonReparseFile(string path)
    {
        if (!IsRegularNonReparseFile(path))
        {
            throw new KeyPairGenerationException(LocalEd25519KeyGenerationErrorCatalog.Recovery, OperationErrorCode.Recovery);
        }
    }

    private static bool IsRegularNonReparseFile(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void RejectReparsePoint(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("A key target cannot be a symbolic link or reparse point.");
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static void RejectReparsePointHierarchy(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new IOException("A key target must have an absolute non-reparse parent directory.");
        }

        RejectReparsePoint(root);
        var current = root;
        var relative = path[root.Length..];
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            RejectReparsePoint(current);
        }
    }

    private readonly record struct KeyPairPaths(string PrivateFinalPath, string PublicFinalPath, string ParentDirectory);

    private sealed record TransactionManifest(int Version, string TransactionId, string PrivateFileName, string PublicFileName);

    private readonly record struct RecoveryResult(
        bool Failed,
        OperationState State,
        OperationVerification Verification,
        OperationRecovery Recovery)
    {
        public static RecoveryResult Unchanged { get; } = new(false, OperationState.Unchanged, OperationVerification.NotRun, OperationRecovery.NotRequired);

        public static RecoveryResult FailedResult { get; } = new(true, OperationState.Unknown, OperationVerification.Unknown, OperationRecovery.Failed);
    }
}

internal enum KeyPairTransactionStage
{
    StagingCreated,
    PrivateStaged,
    PublicStaged,
    StagedPairVerified,
    PrivateFinalized,
    PublicFinalized,
    FinalPairVerified,
}

internal interface IKeyPairTransactionFaultInjector
{
    void ThrowIfInjected(KeyPairTransactionStage stage);
}

internal sealed class NoKeyPairTransactionFaultInjector : IKeyPairTransactionFaultInjector
{
    public static NoKeyPairTransactionFaultInjector Instance { get; } = new();

    public void ThrowIfInjected(KeyPairTransactionStage stage)
    {
    }
}

internal sealed class KeyPairGenerationException(string stableCode, OperationErrorCode operationError)
    : Exception("Local SSH key generation did not complete safely.")
{
    public string StableCode { get; } = stableCode;

    public OperationErrorCode OperationError { get; } = operationError;
}
