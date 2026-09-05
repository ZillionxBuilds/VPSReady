using System.Text;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;

namespace VpsReady.Infrastructure.Local;

/// <summary>
/// Adds a single, validated OpenSSH alias without rewriting user-owned text.
/// Existing aliases are never silently changed: an equivalent effective alias
/// is idempotent and every other explicit alias is returned for user review.
/// </summary>
public sealed class OpenSshConfigEditor : IOpenSshConfigEditor
{
    private static readonly string[] ManagedDirectives = ["HostName", "User", "Port", "IdentityFile", "IdentitiesOnly"];
    private readonly IPlatformPaths platformPaths;
    private readonly ILocalFileStore fileStore;
    private readonly IDiagnosticSink diagnostics;

    public OpenSshConfigEditor(IPlatformPaths platformPaths, ILocalFileStore fileStore, IDiagnosticSink diagnostics)
    {
        this.platformPaths = platformPaths ?? throw new ArgumentNullException(nameof(platformPaths));
        this.fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public async Task<OpenSshConfigEditResult> AddAliasAsync(
        OpenSshConfigEditRequest request,
        CorrelationIds correlation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(correlation);
        await PublishAsync(DiagnosticEventCatalog.OpenSshConfigEditStarted, correlation, DiagnosticPhase.Validate, DiagnosticStatus.Started, null, CancellationToken.None).ConfigureAwait(false);

        AtomicWriteResult? committedWrite = null;
        LocalConfigSnapshot? originalSnapshot = null;
        string? configPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryCreateDesiredAlias(request, out var desired))
            {
                return await FailAsync(correlation, OpenSshConfigEditErrorCatalog.InvalidInput, OperationErrorCode.Validation, DiagnosticPhase.Validate, OperationState.Unchanged, OperationVerification.NotRun).ConfigureAwait(false);
            }

            configPath = platformPaths.ResolvePath(LocalStorageArea.Ssh, "config");
            originalSnapshot = await ReadSnapshotAsync(configPath, cancellationToken).ConfigureAwait(false);
            if (!TryDecodeConfig(originalSnapshot.Contents, out var document, out var hasUtf8Bom) || !TryParseHostBlocks(document, out var blocks))
            {
                return await FailAsync(correlation, OpenSshConfigEditErrorCatalog.InvalidConfig, OperationErrorCode.Parse, DiagnosticPhase.Validate, OperationState.Unchanged, OperationVerification.NotRun).ConfigureAwait(false);
            }

            var explicitBlocks = blocks.Where(block => block.Patterns.Any(pattern => string.Equals(pattern, desired.Alias, StringComparison.OrdinalIgnoreCase))).ToList();
            if (explicitBlocks.Count > 1)
            {
                return await FailAsync(correlation, OpenSshConfigEditErrorCatalog.DuplicateAlias, OperationErrorCode.Validation, DiagnosticPhase.Validate, OperationState.Unchanged, OperationVerification.NotRun).ConfigureAwait(false);
            }

            if (explicitBlocks.Count == 1)
            {
                var effective = GetEffectiveValues(blocks, desired.Alias);
                if (desired.IsEquivalentTo(effective))
                {
                    var unchanged = OperationResult.Success(correlation.OperationId, OperationState.Unchanged);
                    await PublishAsync(DiagnosticEventCatalog.OpenSshConfigEditSucceeded, correlation, DiagnosticPhase.Verify, DiagnosticStatus.Succeeded, null, CancellationToken.None).ConfigureAwait(false);
                    return OpenSshConfigEditResult.Success(unchanged, OpenSshConfigEditDisposition.Unchanged);
                }

                return await FailAsync(correlation, OpenSshConfigEditErrorCatalog.AliasExists, OperationErrorCode.Validation, DiagnosticPhase.Preflight, OperationState.Unchanged, OperationVerification.NotRun).ConfigureAwait(false);
            }

            // A generated block is prepended so its values are obtained before
            // any preserved wildcard/default block. This deliberately respects
            // OpenSSH's first-obtained-value rule without changing that text.
            var newline = DetermineLineEnding(document);
            var replacementDocument = desired.Render(newline) + document;
            var replacement = EncodeConfig(replacementDocument, hasUtf8Bom);
            cancellationToken.ThrowIfCancellationRequested();
            committedWrite = await fileStore.WriteAtomicallyAsync(
                configPath,
                replacement,
                new AtomicWriteOptions(LocalFileCollisionPolicy.ReplaceWithBackup, RestrictPermissions: true, CreateBackup: true),
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            var verified = await fileStore.ReadAsync(configPath, cancellationToken).ConfigureAwait(false);
            if (!verified.Span.SequenceEqual(replacement))
            {
                return await RecoverAfterCommittedWriteAsync(correlation, configPath, originalSnapshot, committedWrite, OpenSshConfigEditErrorCatalog.LocalIo, OperationErrorCode.Verification).ConfigureAwait(false);
            }

            var succeeded = OperationResult.Success(correlation.OperationId, OperationState.Applied);
            await PublishAsync(DiagnosticEventCatalog.OpenSshConfigEditSucceeded, correlation, DiagnosticPhase.Verify, DiagnosticStatus.Succeeded, null, CancellationToken.None).ConfigureAwait(false);
            return OpenSshConfigEditResult.Success(succeeded, OpenSshConfigEditDisposition.Created);
        }
        catch (OperationCanceledException)
        {
            if (committedWrite is not null && originalSnapshot is not null && configPath is not null)
            {
                return await RecoverAfterCommittedWriteAsync(correlation, configPath, originalSnapshot, committedWrite, OpenSshConfigEditErrorCatalog.Cancelled, OperationErrorCode.Cancelled).ConfigureAwait(false);
            }

            var operation = OperationResult.Cancellation(correlation.OperationId, OperationState.Unchanged, OperationVerification.NotRun);
            await PublishAsync(DiagnosticEventCatalog.OpenSshConfigEditCancelled, correlation, DiagnosticPhase.Apply, DiagnosticStatus.Cancelled, OperationErrorCode.Cancelled.ToStableCode(), CancellationToken.None).ConfigureAwait(false);
            return OpenSshConfigEditResult.Failure(operation, OpenSshConfigEditErrorCatalog.Cancelled);
        }
        catch (UnauthorizedAccessException)
        {
            if (committedWrite is not null && originalSnapshot is not null && configPath is not null)
            {
                return await RecoverAfterCommittedWriteAsync(correlation, configPath, originalSnapshot, committedWrite, OpenSshConfigEditErrorCatalog.Permission, OperationErrorCode.LocalIo).ConfigureAwait(false);
            }

            return await FailAsync(correlation, OpenSshConfigEditErrorCatalog.Permission, OperationErrorCode.LocalIo, DiagnosticPhase.Apply, OperationState.Unknown, OperationVerification.NotRun).ConfigureAwait(false);
        }
        catch (IOException)
        {
            if (committedWrite is not null && originalSnapshot is not null && configPath is not null)
            {
                return await RecoverAfterCommittedWriteAsync(correlation, configPath, originalSnapshot, committedWrite, OpenSshConfigEditErrorCatalog.LocalIo, OperationErrorCode.LocalIo).ConfigureAwait(false);
            }

            return await FailAsync(correlation, OpenSshConfigEditErrorCatalog.LocalIo, OperationErrorCode.LocalIo, DiagnosticPhase.Apply, OperationState.Unknown, OperationVerification.NotRun).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return await FailAsync(correlation, OpenSshConfigEditErrorCatalog.InvalidInput, OperationErrorCode.Validation, DiagnosticPhase.Validate, OperationState.Unchanged, OperationVerification.NotRun).ConfigureAwait(false);
        }
        finally
        {
            // Config contents and local paths are user data. Do not retain them
            // beyond this call or place them on any diagnostic surface.
        }
    }

    private async Task<LocalConfigSnapshot> ReadSnapshotAsync(string configPath, CancellationToken cancellationToken)
    {
        try
        {
            return new LocalConfigSnapshot(true, await fileStore.ReadAsync(configPath, cancellationToken).ConfigureAwait(false));
        }
        catch (FileNotFoundException)
        {
            return new LocalConfigSnapshot(false, ReadOnlyMemory<byte>.Empty);
        }
        catch (DirectoryNotFoundException)
        {
            return new LocalConfigSnapshot(false, ReadOnlyMemory<byte>.Empty);
        }
    }

    private async Task<OpenSshConfigEditResult> RecoverAfterCommittedWriteAsync(
        CorrelationIds correlation,
        string configPath,
        LocalConfigSnapshot original,
        AtomicWriteResult committedWrite,
        string errorCode,
        OperationErrorCode operationError)
    {
        try
        {
            if (original.Exists)
            {
                if (string.IsNullOrWhiteSpace(committedWrite.BackupPath))
                {
                    throw new IOException("The committed config change has no recoverable backup.");
                }

                var backup = await fileStore.ReadAsync(committedWrite.BackupPath, CancellationToken.None).ConfigureAwait(false);
                if (!backup.Span.SequenceEqual(original.Contents.Span))
                {
                    throw new IOException("The committed config backup did not match the original.");
                }

                await fileStore.WriteAtomicallyAsync(
                    configPath,
                    backup,
                    new AtomicWriteOptions(LocalFileCollisionPolicy.ReplaceWithBackup, RestrictPermissions: true, CreateBackup: false),
                    CancellationToken.None).ConfigureAwait(false);
                var restored = await fileStore.ReadAsync(configPath, CancellationToken.None).ConfigureAwait(false);
                if (!restored.Span.SequenceEqual(original.Contents.Span))
                {
                    throw new IOException("The original config could not be verified after recovery.");
                }
            }
            else if (fileStore is IRecoverableLocalFileStore recoverableStore)
            {
                await recoverableStore.DeleteIfExistsAsync(configPath, CancellationToken.None).ConfigureAwait(false);
                try
                {
                    _ = await fileStore.ReadAsync(configPath, CancellationToken.None).ConfigureAwait(false);
                    throw new IOException("The newly created config remained after recovery.");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            else
            {
                throw new IOException("The newly created config cannot be safely removed by this local store.");
            }

            return await FailAsync(correlation, errorCode, operationError, DiagnosticPhase.Recovery, OperationState.Unchanged, OperationVerification.NotRun, OperationRecovery.Succeeded).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return await FailAsync(correlation, OpenSshConfigEditErrorCatalog.LocalIo, OperationErrorCode.Recovery, DiagnosticPhase.Recovery, OperationState.Unknown, OperationVerification.Unknown, OperationRecovery.Failed).ConfigureAwait(false);
        }
    }

    private async Task<OpenSshConfigEditResult> FailAsync(
        CorrelationIds correlation,
        string errorCode,
        OperationErrorCode operationError,
        DiagnosticPhase phase,
        OperationState state,
        OperationVerification verification,
        OperationRecovery recovery = OperationRecovery.NotRequired)
    {
        var operation = OperationResult.Failure(correlation.OperationId, operationError, state, verification, recovery);
        await PublishAsync(DiagnosticEventCatalog.OpenSshConfigEditFailed, correlation, phase, DiagnosticStatus.Failed, errorCode, CancellationToken.None).ConfigureAwait(false);
        return OpenSshConfigEditResult.Failure(operation, errorCode);
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
                "OpenSSH config",
                status is DiagnosticStatus.Failed ? DiagnosticLevel.Error : DiagnosticLevel.Information,
                correlation.ForStep(phase.ToString().ToLowerInvariant()),
                phase,
                status,
                status switch
                {
                    DiagnosticStatus.Started => "Local OpenSSH config edit started.",
                    DiagnosticStatus.Succeeded => "Local OpenSSH config alias was verified.",
                    DiagnosticStatus.Cancelled => "Local OpenSSH config edit was cancelled.",
                    _ => "Local OpenSSH config edit did not complete safely.",
                },
                ErrorCode: errorCode,
                Action: "Edit local OpenSSH config",
                Context: new Dictionary<string, DiagnosticValue>
                {
                    ["file_class"] = new(DiagnosticDataClassification.PublicSafe, "local_openssh_config"),
                }),
            cancellationToken);

    private static bool TryCreateDesiredAlias(OpenSshConfigEditRequest request, out DesiredAlias desired)
    {
        desired = default!;
        if (!IsSafeAlias(request.Alias) || !IsSafeHostName(request.HostName) || !IsSafeUser(request.User) || request.Port is < 1 or > 65535 || !request.IdentitiesOnly || !TryNormalizeIdentityFile(request.IdentityFile, out var identityFile))
        {
            return false;
        }

        desired = new DesiredAlias(request.Alias, request.HostName, request.User, request.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), identityFile);
        return true;
    }

    private static bool IsSafeAlias(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 255 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static bool IsSafeHostName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255 || value.Any(char.IsControl) || value.Any(char.IsWhiteSpace) || value.Contains('*') || value.Contains('?') || value.Contains('!'))
        {
            return false;
        }

        return value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or ':' or '-');
    }

    private static bool IsSafeUser(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && char.IsAsciiLetterOrDigit(value[0]) && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-');

    private static bool TryNormalizeIdentityFile(string value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || !Path.IsPathFullyQualified(value))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(value);
            if (!string.Equals(fullPath, value, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return false;
            }

            normalized = fullPath.Replace('\\', '/');
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryDecodeConfig(ReadOnlyMemory<byte> bytes, out string document, out bool hasUtf8Bom)
    {
        hasUtf8Bom = bytes.Length >= 3 && bytes.Span[..3].SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf });
        try
        {
            document = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(hasUtf8Bom ? bytes[3..].Span : bytes.Span);
            return !document.Contains('\r') || !document.Replace("\r\n", string.Empty, StringComparison.Ordinal).Contains('\r');
        }
        catch (DecoderFallbackException)
        {
            document = string.Empty;
            return false;
        }
    }

    private static byte[] EncodeConfig(string document, bool includeUtf8Bom)
    {
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(document);
        return includeUtf8Bom ? [0xef, 0xbb, 0xbf, .. text] : text;
    }

    private static string DetermineLineEnding(string document) => document.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static bool TryParseHostBlocks(string document, out IReadOnlyList<HostBlock> blocks)
    {
        var parsed = new List<HostBlock>();
        HostBlock? current = null;
        foreach (var line in document.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = IndexOfWhitespace(trimmed);
            var directive = separator < 0 ? trimmed : trimmed[..separator];
            var remainder = separator < 0 ? string.Empty : trimmed[(separator + 1)..].TrimStart();
            if (string.Equals(directive, "Host", StringComparison.OrdinalIgnoreCase))
            {
                var patterns = SplitArguments(remainder);
                if (patterns is null || patterns.Count == 0)
                {
                    blocks = [];
                    return false;
                }

                current = new HostBlock(patterns);
                parsed.Add(current);
                continue;
            }

            if (string.Equals(directive, "Match", StringComparison.OrdinalIgnoreCase))
            {
                current = null;
                continue;
            }

            if (current is not null && ManagedDirectives.Contains(directive, StringComparer.OrdinalIgnoreCase))
            {
                var values = SplitArguments(remainder);
                if (values is null || values.Count != 1)
                {
                    blocks = [];
                    return false;
                }

                current.AddValue(directive, values[0]);
            }
        }

        blocks = parsed;
        return true;
    }

    private static int IndexOfWhitespace(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsWhiteSpace(value[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static List<string>? SplitArguments(string value)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];

            // Windows OpenSSH configurations commonly contain a quoted native
            // path such as C:\\Users\\name\\.ssh\\id_ed25519. Those slashes are
            // ordinary path characters, not generic escapes. The sole escape
            // we interpret in quoted input is an escaped quote emitted by our
            // own renderer.
            if (quoted && character == '\\' && index + 1 < value.Length && value[index + 1] == '"')
            {
                builder.Append('"');
                index++;
                continue;
            }

            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            // OpenSSH permits an inline comment after an argument. Preserve the
            // original line, but exclude its comment text from value comparison.
            if (!quoted && character == '#' && builder.Length == 0 && values.Count > 0)
            {
                break;
            }

            if (!quoted && char.IsWhiteSpace(character))
            {
                if (builder.Length > 0)
                {
                    values.Add(builder.ToString());
                    builder.Clear();
                }

                continue;
            }

            builder.Append(character);
        }

        if (quoted)
        {
            return null;
        }

        if (builder.Length > 0)
        {
            values.Add(builder.ToString());
        }

        return values;
    }

    private static Dictionary<string, string> GetEffectiveValues(IEnumerable<HostBlock> blocks, string alias)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in blocks.Where(block => MatchesAlias(block.Patterns, alias)))
        {
            foreach (var pair in block.Values)
            {
                values.TryAdd(pair.Key, pair.Value);
            }
        }

        return values;
    }

    private static bool MatchesAlias(IEnumerable<string> patterns, string alias)
    {
        var positiveMatch = false;
        foreach (var pattern in patterns)
        {
            if (pattern.Length > 1 && pattern[0] == '!')
            {
                if (MatchesPattern(pattern[1..], alias))
                {
                    return false;
                }

                continue;
            }

            positiveMatch |= MatchesPattern(pattern, alias);
        }

        return positiveMatch;
    }

    private static bool MatchesPattern(string pattern, string value)
    {
        var patternIndex = 0;
        var valueIndex = 0;
        var wildcard = -1;
        var wildcardValue = 0;
        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length && (pattern[patternIndex] == '?' || char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(value[valueIndex])))
            {
                patternIndex++;
                valueIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                wildcard = patternIndex++;
                wildcardValue = valueIndex;
            }
            else if (wildcard >= 0)
            {
                patternIndex = wildcard + 1;
                valueIndex = ++wildcardValue;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    private sealed class HostBlock(IReadOnlyList<string> patterns)
    {
        private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<string> Patterns { get; } = patterns;
        public IReadOnlyDictionary<string, string> Values => values;

        public void AddValue(string directive, string value) => values.TryAdd(directive, value);
    }

    private sealed record DesiredAlias(string Alias, string HostName, string User, string Port, string IdentityFile)
    {
        public bool IsEquivalentTo(Dictionary<string, string> values) =>
            values.TryGetValue("HostName", out var hostName) && string.Equals(hostName, HostName, StringComparison.OrdinalIgnoreCase) &&
            values.TryGetValue("User", out var user) && string.Equals(user, User, StringComparison.Ordinal) &&
            values.TryGetValue("Port", out var port) && string.Equals(port, Port, StringComparison.Ordinal) &&
            values.TryGetValue("IdentityFile", out var identityFile) && string.Equals(NormalizeIdentityForComparison(identityFile), IdentityFile, StringComparison.Ordinal) &&
            values.TryGetValue("IdentitiesOnly", out var identitiesOnly) && string.Equals(identitiesOnly, "yes", StringComparison.OrdinalIgnoreCase);

        public string Render(string newline) => $"Host {Alias}{newline}    HostName {HostName}{newline}    User {User}{newline}    Port {Port}{newline}    IdentityFile \"{IdentityFile.Replace("\"", "\\\"", StringComparison.Ordinal)}\"{newline}    IdentitiesOnly yes{newline}{newline}";
    }

    private sealed record LocalConfigSnapshot(bool Exists, ReadOnlyMemory<byte> Contents);

    private static string NormalizeIdentityForComparison(string value) => value.Replace('\\', '/');
}
