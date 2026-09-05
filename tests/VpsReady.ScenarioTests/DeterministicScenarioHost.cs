using System.Globalization;
using System.Text.RegularExpressions;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.ScenarioTests;

/// <summary>
/// In-memory implementation of the production remote transport boundary.
/// It never creates a socket or invokes a shell.  Every supported command reads
/// or mutates <see cref="ScenarioHostState"/> and every other command fails.
/// </summary>
public sealed partial class DeterministicScenarioHost : IRemoteTransport
{
    public DeterministicScenarioHost(string scenarioId)
        : this(ScenarioHostState.CreateDefault(scenarioId), new ScenarioFaultPlan())
    {
    }

    public DeterministicScenarioHost(ScenarioHostState state, ScenarioFaultPlan faults)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Faults = faults ?? throw new ArgumentNullException(nameof(faults));
    }

    public ScenarioHostState State { get; }

    public ScenarioFaultPlan Faults { get; }

    public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteAsync(command, InferPhase(command.Id.Value), cancellationToken);
    }

    /// <summary>
    /// Executes a command under an explicit operation phase.  Production
    /// commands will eventually provide this context through their operation
    /// adapter; the overload lets tests inject each phase without changing the
    /// Core transport contract.
    /// </summary>
    public async Task<RemoteCommandResult> ExecuteAsync(
        RemoteCommand command,
        DiagnosticPhase phase,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Id);
        if (!ScenarioCommandIds.IsKnown(command.Id.Value))
        {
            throw new InvalidOperationException($"Scenario '{State.ScenarioId}' does not recognize command ID '{command.Id.Value}'.");
        }

        EnsureSafeArgumentSummary(command);

        if (command.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "A finite positive command timeout is required.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (State.Ssh.CommandLatency > command.Timeout)
        {
            throw new TimeoutException($"Scenario command '{command.Id.Value}' exceeded its finite timeout.");
        }

        if (State.Ssh.DisconnectNextCommand)
        {
            State.Ssh.DisconnectNextCommand = false;
            throw new ScenarioDisconnectException($"Scenario disconnected while executing '{command.Id.Value}'.");
        }

        if (Faults.TryTake(phase, command.Id.Value, out var fault) && fault is not null)
        {
            var faultResult = await ScenarioFaultPlan.ApplyToCommandAsync(fault, command, cancellationToken).ConfigureAwait(false);
            if (faultResult is not null)
            {
                return new RemoteCommandResult(
                    faultResult.ExitCode,
                    faultResult.StandardOutput,
                    faultResult.StandardError,
                    State.Ssh.CommandLatency + faultResult.Duration,
                    command.OutputCapturePolicy);
            }
        }

        var result = command.Id.Value switch
        {
            ScenarioCommandIds.CounterRead => Result(State.Counter.ToString(CultureInfo.InvariantCulture)),
            ScenarioCommandIds.CounterIncrement => Result((++State.Counter).ToString(CultureInfo.InvariantCulture)),
            ScenarioCommandIds.SshAuthenticate => Authenticate(),
            ScenarioCommandIds.SshTrustInspect => TrustInspect(),
            ScenarioCommandIds.SshTrustAccept => TrustAccept(command),
            ScenarioCommandIds.SshPermissions => Permissions(),
            ScenarioCommandIds.SshKeyAuthenticate => KeyAuthenticate(),
            ScenarioCommandIds.SshAuthorizedKeysList => AuthorizedKeysList(),
            ScenarioCommandIds.SshAuthorizedKeysInstall => AuthorizedKeysInstall(command),
            ScenarioCommandIds.UbuntuFactsRead => Result(State.Ubuntu.RenderFacts()),
            ScenarioCommandIds.UbuntuHostnameRead => Result(State.Hostname),
            ScenarioCommandIds.UbuntuHostnameSet => SetHostname(command),
            ScenarioCommandIds.UbuntuTimezoneRead => Result(State.Timezone),
            ScenarioCommandIds.UbuntuTimezoneSet => SetTimezone(command),
            RemoteCommandCatalog.UbuntuOsReleaseRead => Result($"ID={State.Ubuntu.Distribution.ToLowerInvariant()}\nVERSION=\"{State.Ubuntu.Version}\""),
            RemoteCommandCatalog.UbuntuKernelArchitectureRead => Result($"Linux {State.Ubuntu.Kernel} {State.Ubuntu.Architecture}"),
            RemoteCommandCatalog.UbuntuHostnameRead => Result(State.Hostname),
            RemoteCommandCatalog.UbuntuUptimeRead => Result("93600.00 1200.00"),
            RemoteCommandCatalog.UbuntuCurrentUserRead => Result(State.Ssh.UserName),
            RemoteCommandCatalog.UbuntuPrivilegeRead => PrivilegeFacts(),
            RemoteCommandCatalog.UbuntuCpuRead => Result("processor\t: 0\nmodel name\t: Scenario CPU\n\nprocessor\t: 1\nmodel name\t: Scenario CPU"),
            RemoteCommandCatalog.UbuntuMemoryRead => Result("MemTotal:       2097152 kB\nMemAvailable:    1048576 kB"),
            RemoteCommandCatalog.UbuntuRootDiskRead => Result("/dev/vda1 20G 10G 10G 50% /"),
            RemoteCommandCatalog.SshSessionPortRead => Result(State.Ssh.ActiveSshPort.ToString(CultureInfo.InvariantCulture)),
            RemoteCommandCatalog.UbuntuUfwAvailabilityRead => Result($"ufw={(State.Ufw.Status == ScenarioUfwStatus.Absent ? "unavailable" : "available")}"),
            RemoteCommandCatalog.UbuntuUfwStatusRead => FactUfwStatus(),
            ScenarioCommandIds.UfwStatus => UfwStatus(),
            ScenarioCommandIds.UfwRulesList => UfwRulesList(),
            ScenarioCommandIds.UfwRuleAdd => AddUfwRule(command),
            ScenarioCommandIds.UfwRuleRemove => RemoveUfwRule(command),
            ScenarioCommandIds.UfwEnable => EnableUfw(),
            ScenarioCommandIds.UfwDisable => DisableUfw(),
            ScenarioCommandIds.RemoteFileRead => ReadRemoteFile(command),
            ScenarioCommandIds.RemoteFileWrite => WriteRemoteFile(command),
            ScenarioCommandIds.RemoteFileChmod => ChmodRemoteFile(command),
            ScenarioCommandIds.AptUpdate => AptUpdate(),
            ScenarioCommandIds.AptUpgrade => AptUpgrade(),
            ScenarioCommandIds.RebootRequired => Result($"required={State.Apt.RebootRequired.ToString().ToLowerInvariant()}"),
            ScenarioCommandIds.Reboot => Reboot(command),
            ScenarioCommandIds.Reconnect => Reconnect(),
            _ => throw new InvalidOperationException($"Scenario '{State.ScenarioId}' does not recognize command ID '{command.Id.Value}'."),
        };

        return new RemoteCommandResult(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.Duration,
            command.OutputCapturePolicy);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private RemoteCommandResult Authenticate()
    {
        State.Ssh.ConnectionAttempts++;
        State.Ssh.LastAuthenticationMethod = "password";
        if (State.Ssh.HostKey == ScenarioHostKeyState.Unknown)
        {
            return Failure(22, "Host key is unknown and requires explicit trust.");
        }

        if (State.Ssh.HostKey == ScenarioHostKeyState.Changed)
        {
            return Failure(23, "Host key changed; explicit review is required.");
        }

        if (State.Ssh.Authentication == ScenarioAuthenticationState.Denied)
        {
            return Failure(24, "Authentication denied by deterministic scenario.");
        }

        State.Ssh.IsConnected = true;
        return Result("authenticated=true");
    }

    private RemoteCommandResult TrustInspect()
    {
        var status = State.Ssh.HostKey switch
        {
            ScenarioHostKeyState.Unknown => "unknown",
            ScenarioHostKeyState.Matching => "matching",
            ScenarioHostKeyState.Changed => "changed",
            _ => "unknown",
        };
        return Result($"status={status} fingerprint={State.Ssh.HostKeyFingerprint}");
    }

    private RemoteCommandResult TrustAccept(RemoteCommand command)
    {
        var replaceChanged = string.Equals(GetArgument(command, "replace"), "yes", StringComparison.OrdinalIgnoreCase);
        if (State.Ssh.HostKey == ScenarioHostKeyState.Changed && !replaceChanged)
        {
            return Failure(23, "Changed host key remains blocked without explicit replacement review.");
        }

        State.Ssh.KnownHostFingerprint = State.Ssh.HostKeyFingerprint;
        State.Ssh.HostKey = ScenarioHostKeyState.Matching;
        return Result("trusted=true");
    }

    private RemoteCommandResult Permissions()
    {
        return Result($"root={State.Ssh.RootAvailable.ToString().ToLowerInvariant()} sudo={State.Ssh.SudoAvailable.ToString().ToLowerInvariant()}");
    }

    private RemoteCommandResult KeyAuthenticate()
    {
        State.Ssh.ConnectionAttempts++;
        State.Ssh.LastAuthenticationMethod = "key";
        if (State.Ssh.HostKey != ScenarioHostKeyState.Matching)
        {
            return Failure(23, "Key authentication is blocked until host trust is matching.");
        }

        if (!State.Ssh.KeyAuthenticationSucceeds)
        {
            return Failure(24, "Key authentication failed in deterministic scenario.");
        }

        if (State.Ssh.AuthorizedKeyFingerprints.Count == 0)
        {
            return Failure(24, "No authorized key is installed for key-authentication verification.");
        }

        State.Ssh.IsConnected = true;
        return Result("key_authenticated=true");
    }

    private RemoteCommandResult AuthorizedKeysList()
    {
        var output = string.Join(Environment.NewLine, State.Ssh.AuthorizedKeyFingerprints.Order(StringComparer.Ordinal));
        return Result(output);
    }

    private RemoteCommandResult AuthorizedKeysInstall(RemoteCommand command)
    {
        var fingerprint = GetArgument(command, "fingerprint");
        if (string.IsNullOrWhiteSpace(fingerprint) || !State.Ssh.StagedPublicKeys.TryGetValue(fingerprint, out var publicKey))
        {
            return Failure(2, "A staged public-key fingerprint is required; key material is never a command argument.");
        }

        if (!HasPrivilege())
        {
            return Failure(13, "Permission denied while updating authorized keys.");
        }

        var changed = State.Ssh.AuthorizedKeyFingerprints.Add(fingerprint);
        var path = $"/home/{State.Ssh.UserName}/.ssh/authorized_keys";
        var entry = State.RemoteFiles.Files[path];
        if (changed && !entry.Contents.Contains(publicKey, StringComparison.Ordinal))
        {
            var newline = entry.Contents.Length == 0 || entry.Contents.EndsWith('\n') ? string.Empty : Environment.NewLine;
            State.RemoteFiles.Files[path] = entry with { Contents = entry.Contents + newline + publicKey + Environment.NewLine };
        }

        return Result($"changed={changed.ToString().ToLowerInvariant()} fingerprint={fingerprint}");
    }

    private RemoteCommandResult SetHostname(RemoteCommand command)
    {
        var hostname = GetArgument(command, "hostname");
        if (string.IsNullOrWhiteSpace(hostname) || hostname.Any(char.IsControl))
        {
            return Failure(2, "Hostname is invalid.");
        }

        State.Hostname = hostname;
        State.Ubuntu.RawFactsOutput = null;
        return Result($"hostname={State.Hostname}");
    }

    private RemoteCommandResult SetTimezone(RemoteCommand command)
    {
        var timezone = GetArgument(command, "timezone");
        if (string.IsNullOrWhiteSpace(timezone) || timezone.Any(char.IsControl))
        {
            return Failure(2, "Timezone is invalid.");
        }

        State.Timezone = timezone;
        State.Ubuntu.RawFactsOutput = null;
        return Result($"timezone={State.Timezone}");
    }

    private RemoteCommandResult UfwStatus()
    {
        return State.Ufw.Status switch
        {
            ScenarioUfwStatus.Absent => Failure(127, "ufw is unavailable in this scenario."),
            ScenarioUfwStatus.Error => Failure(1, State.Ufw.ErrorMessage),
            _ => Result($"status={State.Ufw.Status.ToString().ToLowerInvariant()}"),
        };
    }

    private RemoteCommandResult PrivilegeFacts()
    {
        var root = State.Ssh.RootAvailable.ToString().ToLowerInvariant();
        var sudo = State.Ssh.RootAvailable
            ? "not_required"
            : State.Ssh.SudoAvailable ? "available" : "unavailable";
        return Result($"root={root}\nsudo={sudo}");
    }

    private RemoteCommandResult FactUfwStatus()
    {
        return State.Ufw.Status switch
        {
            ScenarioUfwStatus.Absent => Result("ufw=unavailable"),
            ScenarioUfwStatus.Error => Failure(1, State.Ufw.ErrorMessage),
            _ => Result($"Status: {State.Ufw.Status.ToString().ToLowerInvariant()}"),
        };
    }

    private RemoteCommandResult UfwRulesList()
    {
        if (State.Ufw.Status == ScenarioUfwStatus.Absent)
        {
            return Failure(127, "ufw is unavailable in this scenario.");
        }

        if (State.Ufw.Status == ScenarioUfwStatus.Error)
        {
            return Failure(1, State.Ufw.ErrorMessage);
        }

        var output = string.Join(Environment.NewLine, State.Ufw.Rules.OrderBy(rule => rule.RuleId, StringComparer.Ordinal).Select(ScenarioValueFormatting.FormatRule));
        return Result(output);
    }

    private RemoteCommandResult AddUfwRule(RemoteCommand command)
    {
        if (State.Ufw.Status is ScenarioUfwStatus.Absent or ScenarioUfwStatus.Error)
        {
            return Failure(127, "ufw is unavailable in this scenario.");
        }

        if (!Enum.TryParse<ScenarioRuleProtocol>(GetArgument(command, "protocol"), true, out var protocol)
            || !int.TryParse(GetArgument(command, "port"), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535
            || !Enum.TryParse<ScenarioIpFamily>(GetArgument(command, "family") ?? "Ipv4", true, out var family))
        {
            return Failure(2, "Firewall rule arguments are invalid.");
        }

        var source = GetArgument(command, "source") ?? "Anywhere";
        var duplicate = State.Ufw.FindDuplicate(protocol, port, source, family);
        if (duplicate is not null)
        {
            return Result($"changed=false {ScenarioValueFormatting.FormatRule(duplicate)}");
        }

        var nextId = NextRuleId();
        var rule = new ScenarioFirewallRule(nextId, protocol, port, source, family);
        State.Ufw.Rules.Add(rule);
        return Result($"changed=true {ScenarioValueFormatting.FormatRule(rule)}");
    }

    private RemoteCommandResult RemoveUfwRule(RemoteCommand command)
    {
        var ruleId = GetArgument(command, "rule_id");
        var rule = string.IsNullOrWhiteSpace(ruleId) ? null : State.Ufw.Find(ruleId);
        if (rule is null)
        {
            return Failure(4, "The selected firewall rule is stale or missing.");
        }

        if (rule.Protocol == ScenarioRuleProtocol.Tcp && rule.Port == State.Ssh.ActiveSshPort)
        {
            return Failure(13, "The active SSH rule cannot be removed through the normal scenario flow.");
        }

        State.Ufw.Rules.Remove(rule);
        return Result($"changed=true removed_rule_id={rule.RuleId}");
    }

    private RemoteCommandResult EnableUfw()
    {
        if (State.Ufw.Status is ScenarioUfwStatus.Absent or ScenarioUfwStatus.Error)
        {
            return Failure(127, "ufw is unavailable in this scenario.");
        }

        if (!State.Ufw.HasActiveSshAllow(State.Ssh.ActiveSshPort))
        {
            return Failure(13, "Firewall enable blocked until the active SSH port has a verified allow rule.");
        }

        State.Ufw.Status = ScenarioUfwStatus.Active;
        return Result("status=active");
    }

    private RemoteCommandResult DisableUfw()
    {
        if (State.Ufw.Status is ScenarioUfwStatus.Absent or ScenarioUfwStatus.Error)
        {
            return Failure(127, "ufw is unavailable in this scenario.");
        }

        State.Ufw.Status = ScenarioUfwStatus.Inactive;
        return Result("status=inactive");
    }

    private RemoteCommandResult ReadRemoteFile(RemoteCommand command)
    {
        var path = GetRequiredPath(command);
        if (path is null)
        {
            return Failure(2, "A safe absolute remote path is required.");
        }

        if (State.RemoteFiles.PermissionDeniedPaths.Contains(path))
        {
            return Failure(13, "Permission denied while reading remote file.");
        }

        if (!State.RemoteFiles.Files.TryGetValue(path, out var entry) || entry.IsDirectory)
        {
            return Failure(2, "Remote file was not found.");
        }

        return Result(entry.Contents);
    }

    private RemoteCommandResult WriteRemoteFile(RemoteCommand command)
    {
        var path = GetRequiredPath(command);
        if (path is null)
        {
            return Failure(2, "A safe absolute remote path is required.");
        }

        if (!State.RemoteFiles.StagedWrites.Remove(path, out var contents))
        {
            return Failure(2, "No out-of-band file content is staged for this safe write command.");
        }

        if (State.RemoteFiles.PermissionDeniedPaths.Contains(path) || State.RemoteFiles.ReadOnlyPaths.Contains(path) || !HasPrivilege())
        {
            return Failure(13, "Permission denied while writing remote file.");
        }

        var existing = State.RemoteFiles.Files.TryGetValue(path, out var oldEntry)
            ? oldEntry
            : new ScenarioRemoteFileEntry(string.Empty, State.Ssh.UserName, State.Ssh.UserName, "0600", false);
        State.RemoteFiles.Files[path] = existing with { Contents = contents, IsDirectory = false };
        return Result("changed=true");
    }

    private RemoteCommandResult ChmodRemoteFile(RemoteCommand command)
    {
        var path = GetRequiredPath(command);
        var mode = GetArgument(command, "mode");
        if (path is null || string.IsNullOrWhiteSpace(mode))
        {
            return Failure(2, "A safe path and permission mode are required.");
        }

        if (State.RemoteFiles.PermissionDeniedPaths.Contains(path) || !State.RemoteFiles.Files.TryGetValue(path, out var entry))
        {
            return Failure(13, "Permission denied or remote file missing.");
        }

        State.RemoteFiles.Files[path] = entry with { Permissions = mode };
        return Result($"permissions={mode}");
    }

    private RemoteCommandResult AptUpdate()
    {
        if (State.Apt.IsLocked)
        {
            return Failure(100, "Could not get lock /var/lib/dpkg/lock (scenario apt lock). ");
        }

        if (State.Apt.InteractiveBlocker || !State.Apt.RefreshSucceeds)
        {
            return Failure(1, "Package index refresh failed in the deterministic scenario.");
        }

        return Result(State.Apt.RefreshOutput);
    }

    private RemoteCommandResult AptUpgrade()
    {
        if (State.Apt.IsLocked)
        {
            return Failure(100, "Could not get lock /var/lib/dpkg/lock (scenario apt lock). ");
        }

        if (State.Apt.InteractiveBlocker || !State.Apt.UpgradeSucceeds)
        {
            return Failure(1, "Package upgrade failed in the deterministic scenario.");
        }

        return Result(State.Apt.UpgradeOutput);
    }

    private RemoteCommandResult Reboot(RemoteCommand command)
    {
        if (!string.Equals(GetArgument(command, "confirm"), "yes", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(2, "Reboot requires explicit confirmation.");
        }

        State.Reboot.IsRebooting = true;
        State.Reboot.ReconnectAttempts = 0;
        State.Ssh.IsConnected = false;
        return Result("reboot=started");
    }

    private RemoteCommandResult Reconnect()
    {
        State.Reboot.ReconnectAttempts++;
        if (State.Reboot.IsRebooting && !State.Reboot.ReconnectSucceeds)
        {
            return Failure(110, "Reconnect timeout in deterministic scenario.");
        }

        if (!State.Ssh.ReconnectSucceeds)
        {
            return Failure(110, "Reconnect failed in deterministic scenario.");
        }

        State.Reboot.IsRebooting = false;
        State.Ssh.IsConnected = true;
        return Result("connected=true");
    }

    private bool HasPrivilege() => State.Ssh.RootAvailable || State.Ssh.SudoAvailable;

    private string NextRuleId()
    {
        var number = 1;
        while (State.Ufw.Rules.Any(rule => string.Equals(rule.RuleId, $"rule-{number:000}", StringComparison.Ordinal)))
        {
            number++;
        }

        return $"rule-{number:000}";
    }

    private static string? GetRequiredPath(RemoteCommand command)
    {
        var path = GetArgument(command, "path");
        return !string.IsNullOrWhiteSpace(path) && path.StartsWith('/') && !path.Any(char.IsControl)
            ? path
            : null;
    }

    private static string? GetArgument(RemoteCommand command, string name)
    {
        foreach (var token in command.SafeArgumentSummary.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = token.IndexOf('=');
            if (separator > 0 && string.Equals(token[..separator], name, StringComparison.Ordinal))
            {
                return token[(separator + 1)..];
            }
        }

        return null;
    }

    private static void EnsureSafeArgumentSummary(RemoteCommand command)
    {
        if (SensitiveArgumentRegex().IsMatch(command.SafeArgumentSummary)
            || command.SafeArgumentSummary.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Scenario command '{command.Id.Value}' received a credential-bearing argument summary.");
        }
    }

    [GeneratedRegex("\\b(password|passphrase|private[_-]?key|token|secret)\\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveArgumentRegex();

    private RemoteCommandResult Result(string standardOutput) => new(0, standardOutput, string.Empty, State.Ssh.CommandLatency);

    private RemoteCommandResult Failure(int exitCode, string standardError) => new(exitCode, string.Empty, standardError, State.Ssh.CommandLatency);

    private static DiagnosticPhase InferPhase(string commandId)
    {
        if (commandId.Contains("reconnect", StringComparison.Ordinal))
        {
            return DiagnosticPhase.Recovery;
        }

        if (commandId.Contains("set", StringComparison.Ordinal)
            || commandId.Contains("add", StringComparison.Ordinal)
            || commandId.Contains("remove", StringComparison.Ordinal)
            || commandId.Contains("enable", StringComparison.Ordinal)
            || commandId.Contains("disable", StringComparison.Ordinal)
            || commandId.Contains("write", StringComparison.Ordinal)
            || commandId.Contains("install", StringComparison.Ordinal)
            || commandId.Equals(ScenarioCommandIds.Reboot, StringComparison.Ordinal)
            || commandId.Equals(ScenarioCommandIds.AptUpdate, StringComparison.Ordinal)
            || commandId.Equals(ScenarioCommandIds.AptUpgrade, StringComparison.Ordinal))
        {
            return DiagnosticPhase.Apply;
        }

        return commandId.Contains("verify", StringComparison.Ordinal)
            ? DiagnosticPhase.Verify
            : DiagnosticPhase.Preflight;
    }
}
