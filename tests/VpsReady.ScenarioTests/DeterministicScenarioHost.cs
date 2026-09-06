using System.Globalization;
using System.Text.RegularExpressions;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

/// <summary>
/// In-memory implementation of the production remote transport boundary.
/// It never creates a socket or invokes a shell.  Every supported command reads
/// or mutates <see cref="ScenarioHostState"/> and every other command fails.
/// </summary>
public sealed partial class DeterministicScenarioHost : IPublicKeyDeploymentTransport, IHostnameChangeTransport
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

    /// <summary>Hostname is supplied through the dedicated ephemeral operation boundary, never command metadata.</summary>
    public async Task<RemoteCommandResult> ExecuteHostnameChangeAsync(RemoteCommand command, string validatedHostname, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Id.Value != RemoteCommandCatalog.UbuntuHostnameChangeApply || !HostnameChangeValidator.TryNormalize(validatedHostname, out var hostname))
        {
            throw new ArgumentException("Scenario hostname apply requires a strict catalog command and hostname.", nameof(command));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (State.Ssh.CommandLatency > command.Timeout)
        {
            throw new TimeoutException("Scenario hostname apply exceeded its finite timeout.");
        }

        if (Faults.TryTake(DiagnosticPhase.Apply, command.Id.Value, out var fault) && fault is not null)
        {
            var faultResult = await ScenarioFaultPlan.ApplyToCommandAsync(fault, command, cancellationToken).ConfigureAwait(false);
            if (faultResult is not null)
            {
                return new RemoteCommandResult(faultResult.ExitCode, faultResult.StandardOutput, faultResult.StandardError, State.Ssh.CommandLatency + faultResult.Duration, command.OutputCapturePolicy);
            }
        }

        State.Hostname = hostname;
        State.Ubuntu.RawFactsOutput = null;
        return new RemoteCommandResult(0, string.Empty, string.Empty, State.Ssh.CommandLatency, command.OutputCapturePolicy);
    }

    public async Task<HostnameReadResult> ReadHostnameAsync(RemoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Id.Value is not (RemoteCommandCatalog.UbuntuHostnameChangeRead or RemoteCommandCatalog.UbuntuHostnameChangeVerify))
        {
            throw new ArgumentException("Scenario hostname reads require a hostname catalog command.", nameof(command));
        }

        var phase = command.Id.Value == RemoteCommandCatalog.UbuntuHostnameChangeVerify ? DiagnosticPhase.Verify : DiagnosticPhase.Plan;
        var response = await ExecuteWireAsync(command, phase, cancellationToken).ConfigureAwait(false);
        return response.Succeeded && HostnameChangeValidator.TryNormalize(response.StandardOutput, out var hostname)
            ? new HostnameReadResult(hostname, true)
            : HostnameReadResult.Unavailable;
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
        var wire = await ExecuteWireAsync(command, phase, cancellationToken).ConfigureAwait(false);
        return RemoteCommandCatalog.IsKnown(command.Id.Value)
            ? await VpsReady.Tests.ProductionOutput.CaptureAsync(command, wire, cancellationToken).ConfigureAwait(false)
            : wire;
    }

    internal async Task<RemoteCommandResult> ExecuteWireAsync(RemoteCommand command, DiagnosticPhase phase, CancellationToken cancellationToken)
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
            if (command.Id.Value == RemoteCommandCatalog.UbuntuRebootApply
                && fault.Kind is ScenarioFaultKind.Disconnect or ScenarioFaultKind.DropConnection)
            {
                // A reboot can close SSH after the remote command has begun.
                // This stateful fault differs from a pre-dispatch network loss:
                // it records the reboot effect before simulating the expected
                // command-channel disconnect that the workflow must recover.
                _ = RebootProduction();
                throw new ScenarioDisconnectException($"Scenario reboot disconnected after dispatch '{fault.FaultId}'.");
            }

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

        if (!HasPrivilege() && State.Ufw.Status != ScenarioUfwStatus.Absent
            && command.Id.Value is RemoteCommandCatalog.UbuntuUfwStatusRead or RemoteCommandCatalog.UbuntuUfwDetectionRead or RemoteCommandCatalog.UbuntuUfwRuleListRead or RemoteCommandCatalog.UbuntuUfwAddedRulesRead or RemoteCommandCatalog.UbuntuUfwStoredSshRead)
        {
            return Failure(77, "Non-interactive UFW read privilege is unavailable.");
        }

        var result = command.Id.Value switch
        {
            ScenarioCommandIds.CounterRead => Result(State.Counter.ToString(CultureInfo.InvariantCulture)),
            ScenarioCommandIds.CounterIncrement => Result((++State.Counter).ToString(CultureInfo.InvariantCulture)),
            ScenarioCommandIds.SshAuthenticate => Authenticate(),
            RemoteCommandCatalog.SshConnectionTest => State.Ssh.IsConnected
                ? Result("authenticated=true")
                : Failure(25, "Connection verification requires an authenticated session."),
            ScenarioCommandIds.SshTrustInspect => TrustInspect(),
            ScenarioCommandIds.SshTrustAccept => TrustAccept(command),
            ScenarioCommandIds.SshPermissions => Permissions(),
            ScenarioCommandIds.SshKeyAuthenticate => KeyAuthenticate(),
            ScenarioCommandIds.SshAuthorizedKeysList => AuthorizedKeysList(),
            ScenarioCommandIds.SshAuthorizedKeysInstall => AuthorizedKeysInstall(command),
            RemoteCommandCatalog.UbuntuAuthorizedKeysInspect => AuthorizedKeysInspect(command),
            RemoteCommandCatalog.UbuntuAuthorizedKeysInstall => AuthorizedKeysInstall(command),
            RemoteCommandCatalog.UbuntuAuthorizedKeysVerify => AuthorizedKeysVerify(command),
            ScenarioCommandIds.UbuntuFactsRead => Result(State.Ubuntu.RenderFacts()),
            ScenarioCommandIds.UbuntuHostnameRead => Result(State.Hostname),
            ScenarioCommandIds.UbuntuHostnameSet => SetHostname(command),
            ScenarioCommandIds.UbuntuTimezoneRead => Result(State.Timezone),
            ScenarioCommandIds.UbuntuTimezoneSet => SetTimezone(command),
            RemoteCommandCatalog.UbuntuOsReleaseRead => Result($"ID={State.Ubuntu.Distribution.ToLowerInvariant()}\nVERSION=\"{State.Ubuntu.Version}\""),
            RemoteCommandCatalog.UbuntuKernelArchitectureRead => Result($"Linux {State.Ubuntu.Kernel} {State.Ubuntu.Architecture}"),
            RemoteCommandCatalog.UbuntuHostnameRead => Result(State.Hostname),
            RemoteCommandCatalog.UbuntuHostnameChangeRead => Result(State.Hostname),
            RemoteCommandCatalog.UbuntuHostnameChangeVerify => Result(State.Hostname),
            RemoteCommandCatalog.UbuntuUptimeRead => Result("93600.00 1200.00"),
            RemoteCommandCatalog.UbuntuCurrentUserRead => Result(State.Ssh.UserName),
            RemoteCommandCatalog.UbuntuPrivilegeRead => PrivilegeFacts(),
            RemoteCommandCatalog.UbuntuCpuRead => Result("processor\t: 0\nmodel name\t: Scenario CPU\n\nprocessor\t: 1\nmodel name\t: Scenario CPU"),
            RemoteCommandCatalog.UbuntuMemoryRead => Result("MemTotal:       2097152 kB\nMemAvailable:    1048576 kB"),
            RemoteCommandCatalog.UbuntuRootDiskRead => Result("/dev/vda1 21474836480 10737418240 10737418240 50% /"),
            RemoteCommandCatalog.SshSessionPortRead => Result(State.Ssh.ActiveSshPort.ToString(CultureInfo.InvariantCulture)),
            RemoteCommandCatalog.UbuntuUfwAvailabilityRead => Result($"ufw={(State.Ufw.Status == ScenarioUfwStatus.Absent ? "unavailable" : "available")}"),
            RemoteCommandCatalog.UbuntuUfwStatusRead => FactUfwStatus(),
            RemoteCommandCatalog.UbuntuUfwDetectionRead => FirewallDetection(),
            RemoteCommandCatalog.UbuntuUfwRuleListRead => FirewallRuleList(),
            RemoteCommandCatalog.UbuntuUfwAddedRulesRead => FirewallAddedRules(),
            RemoteCommandCatalog.UbuntuUfwStoredSshRead => FirewallStoredRules(),
            RemoteCommandCatalog.UbuntuUfwAllowRuleAdd => AddUfwRule(command),
            RemoteCommandCatalog.UbuntuUfwSelectedRuleRemove => RemoveUfwRuleBySemantic(command),
            RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure => AddUfwRule(command),
            RemoteCommandCatalog.UbuntuUfwEnable => EnableUfwWithoutClientSafetyGuard(),
            // C305 deliberately uses a production-shaped effect boundary.  The
            // client workflow, not this scenario host, must prove the SSH
            // continuity check before enable; privilege remains a remote
            // mutation concern for both toggle operations.
            RemoteCommandCatalog.UbuntuUfwDisable => DisableUfwProduction(),
            RemoteCommandCatalog.UbuntuAptIndexUpdate => AptIndexUpdate(),
            RemoteCommandCatalog.UbuntuAptIndexVerify => AptIndexVerify(),
            RemoteCommandCatalog.UbuntuAptUpgradePlan => Result($"upgrade_plan_packages={State.Apt.PlannedUpgradePackageCount}:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            RemoteCommandCatalog.UbuntuAptUpgradeApply => AptUpgradeProduction(),
            RemoteCommandCatalog.UbuntuAptUpgradeVerify => AptUpgradeVerify(),
            RemoteCommandCatalog.UbuntuRebootRequiredRead => Result($"reboot_required={State.Apt.RebootRequired.ToString().ToLowerInvariant()}"),
            RemoteCommandCatalog.UbuntuRebootApply => RebootProduction(),
            RemoteCommandCatalog.SshReconnectVerify => State.Ssh.IsConnected ? Result("reconnect=verified") : Failure(25, "Reconnect verification requires an authenticated session."),
            RemoteCommandCatalog.UbuntuBootIdentityRead => Result(State.Reboot.BootIdentity),
            RemoteCommandCatalog.UbuntuTimezoneCurrentRead => Result(State.Timezone),
            RemoteCommandCatalog.UbuntuTimezoneAvailableList => Result(string.Join('\n', State.Ubuntu.AvailableTimezones)),
            RemoteCommandCatalog.UbuntuTimezoneApply => SetTimezone(command),
            RemoteCommandCatalog.UbuntuTimezoneVerifyRead => Result(State.Timezone),
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

    internal Task ReconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reconnect = Reconnect();
        if (!reconnect.Succeeded)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Timeout);
        }

        return Task.CompletedTask;
    }

    public Task<RemoteCommandResult> ExecutePublicKeyDeploymentAsync(
        RemoteCommand command,
        ReadOnlyMemory<char> canonicalPublicKey,
        DiagnosticPhase phase,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var fingerprint = GetArgument(command, "fingerprint");
        if (string.IsNullOrWhiteSpace(fingerprint) || canonicalPublicKey.IsEmpty)
        {
            return Task.FromResult(Failure(2, "Validated public-key metadata is required."));
        }

        // Scenario-only mutable host state models the same narrow payload
        // boundary. It never makes this payload a RemoteCommand argument.
        State.Ssh.StagePublicKey(fingerprint, new string(canonicalPublicKey.Span));
        return ExecuteAsync(command, phase, cancellationToken);
    }

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

    private RemoteCommandResult AuthorizedKeysInspect(RemoteCommand command)
    {
        var fingerprint = GetArgument(command, "fingerprint");
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return Failure(2, "A public-key fingerprint is required.");
        }

        var path = $"/home/{State.Ssh.UserName}/.ssh/authorized_keys";
        if (!State.RemoteFiles.Files.TryGetValue(path, out var entry) || entry.IsDirectory)
        {
            return Failure(2, "Authorized-keys target is unsafe.");
        }

        return Result($"present={State.Ssh.AuthorizedKeyFingerprints.Contains(fingerprint).ToString().ToLowerInvariant()}");
    }

    private RemoteCommandResult AuthorizedKeysInstall(RemoteCommand command)
    {
        var fingerprint = GetArgument(command, "fingerprint");
        if (string.IsNullOrWhiteSpace(fingerprint) || !State.Ssh.StagedPublicKeys.TryGetValue(fingerprint, out var publicKey))
        {
            return Failure(2, "A staged public-key fingerprint is required; key material is never a command argument.");
        }

        if (command.Id.Value == RemoteCommandCatalog.UbuntuAuthorizedKeysInstall)
        {
            var targetDirectory = $"/home/{State.Ssh.UserName}/.ssh";
            var target = $"{targetDirectory}/authorized_keys";
            if (!State.RemoteFiles.Files.TryGetValue(targetDirectory, out var directoryEntry)
                || !State.RemoteFiles.Files.TryGetValue(target, out var keyEntry)
                || !directoryEntry.IsDirectory || keyEntry.IsDirectory
                || directoryEntry.Owner != State.Ssh.UserName || keyEntry.Owner != State.Ssh.UserName
                || directoryEntry.Permissions is not ("0700" or "0750" or "0755")
                || keyEntry.Permissions is not ("0600" or "0640" or "0644")
                || State.RemoteFiles.ReadOnlyPaths.Contains(target) || State.RemoteFiles.PermissionDeniedPaths.Contains(target))
            {
                return Failure(77, "Authorized-keys state requires explicit review.");
            }
            var installed = State.Ssh.AuthorizedKeyFingerprints.Add(fingerprint);
            if (installed)
            {
                var separator = keyEntry.Contents.Length == 0 || keyEntry.Contents.EndsWith('\n') ? string.Empty : "\n";
                State.RemoteFiles.Files[target] = keyEntry with { Contents = keyEntry.Contents + separator + publicKey + "\n" };
            }
            return Result(string.Empty);
        }

        if (!HasPrivilege())
        {
            return Failure(13, "Permission denied while updating authorized keys.");
        }

        var changed = State.Ssh.AuthorizedKeyFingerprints.Add(fingerprint);
        var directory = $"/home/{State.Ssh.UserName}/.ssh";
        var path = $"/home/{State.Ssh.UserName}/.ssh/authorized_keys";
        var entry = State.RemoteFiles.Files[path];
        State.RemoteFiles.Files[directory] = State.RemoteFiles.Files[directory] with
        {
            Owner = State.Ssh.UserName,
            Group = State.Ssh.UserName,
            Permissions = "0700",
        };
        State.RemoteFiles.Files[path] = entry with
        {
            Owner = State.Ssh.UserName,
            Group = State.Ssh.UserName,
            Permissions = "0600",
        };
        entry = State.RemoteFiles.Files[path];
        if (changed && !entry.Contents.Contains(publicKey, StringComparison.Ordinal))
        {
            var newline = entry.Contents.Length == 0 || entry.Contents.EndsWith('\n') ? string.Empty : Environment.NewLine;
            State.RemoteFiles.Files[path] = entry with { Contents = entry.Contents + newline + publicKey + Environment.NewLine };
        }

        return Result($"changed={changed.ToString().ToLowerInvariant()} fingerprint={fingerprint}");
    }

    private RemoteCommandResult AuthorizedKeysVerify(RemoteCommand command)
    {
        var fingerprint = GetArgument(command, "fingerprint");
        var home = $"/home/{State.Ssh.UserName}";
        var directory = $"{home}/.ssh";
        var path = $"{directory}/authorized_keys";
        if (string.IsNullOrWhiteSpace(fingerprint)
            || !State.Ssh.AuthorizedKeyFingerprints.Contains(fingerprint)
            || !State.RemoteFiles.Files.TryGetValue(directory, out var sshDirectory)
            || !State.RemoteFiles.Files.TryGetValue(path, out var keys)
            || !sshDirectory.IsDirectory
            || keys.IsDirectory
            || sshDirectory.Owner != State.Ssh.UserName
            || sshDirectory.Permissions is not ("0700" or "0750" or "0755")
            || keys.Owner != State.Ssh.UserName
            || keys.Permissions is not ("0600" or "0640" or "0644"))
        {
            return Failure(4, "Authorized-keys state did not pass fresh verification.");
        }

        return Result("verified=true");
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
        if (timezone is null || !UbuntuTimezoneCommandCatalog.IsIanaIdentifier(timezone) || !State.Ubuntu.AvailableTimezones.Contains(timezone, StringComparer.Ordinal))
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

    private RemoteCommandResult FirewallDetection() => State.Ufw.Status switch
    {
        ScenarioUfwStatus.Absent => Result("ufw=unavailable"),
        ScenarioUfwStatus.Error => Failure(1, State.Ufw.ErrorMessage),
        ScenarioUfwStatus.Active => Result(ActiveNumberedUfwStatus()),
        _ => Result($"Status: {State.Ufw.Status.ToString().ToLowerInvariant()}"),
    };

    private RemoteCommandResult FirewallRuleList() => State.Ufw.Status switch
    {
        ScenarioUfwStatus.Absent => Result("ufw=unavailable"),
        ScenarioUfwStatus.Error => Failure(1, State.Ufw.ErrorMessage),
        ScenarioUfwStatus.Active => Result(State.Ufw.NumberedStatusOverride ?? ActiveNumberedUfwStatus()),
        _ => Result($"Status: {State.Ufw.Status.ToString().ToLowerInvariant()}"),
    };

    private RemoteCommandResult FirewallAddedRules()
    {
        if (State.Ufw.Status == ScenarioUfwStatus.Absent)
        {
            return Result("ufw=unavailable");
        }

        if (State.Ufw.Status == ScenarioUfwStatus.Error)
        {
            return Failure(1, State.Ufw.ErrorMessage);
        }

        var rules = State.Ufw.Rules.Select(rule => rule.Source == "Anywhere"
            ? $"ufw {rule.Action.ToLowerInvariant()} {rule.Port.ToString(CultureInfo.InvariantCulture)}/{rule.Protocol.ToString().ToLowerInvariant()}"
            : $"ufw {rule.Action.ToLowerInvariant()} from {rule.Source} to any port {rule.Port.ToString(CultureInfo.InvariantCulture)} proto {rule.Protocol.ToString().ToLowerInvariant()}").Distinct(StringComparer.Ordinal);
        return Result(string.Join(Environment.NewLine, ["Added user rules (see 'ufw status' for running firewall):", .. rules]));
    }

    private RemoteCommandResult FirewallStoredRules()
    {
        if (!State.Ufw.StoredProfileSupported) { return Failure(2, "Unsupported stored policy fixture."); }
        string Rules(ScenarioIpFamily family) => string.Concat(State.Ufw.Rules.Where(rule => rule.IpFamily == family).Select(rule =>
            $"-A {(family == ScenarioIpFamily.Ipv6 ? "ufw6" : "ufw")}-user-input -p {rule.Protocol.ToString().ToLowerInvariant()} --dport {rule.Port}"
            + (rule.Source == "Anywhere" ? "" : $" -s {rule.Source}") + $" -j {(rule.Action == "ALLOW" ? "ACCEPT" : "DROP")}\n"));
        return Result(VpsReady.Tests.StoredUfwFixture.Create(State.Ssh.ActiveSshPort, ipv6: State.Ufw.Ipv6Enabled,
            session6: State.Ufw.SessionIsIpv6, rules4: Rules(ScenarioIpFamily.Ipv4), rules6: Rules(ScenarioIpFamily.Ipv6)));
    }

    private RemoteCommandResult DisableUfwProduction()
    {
        if (State.Ufw.Status is ScenarioUfwStatus.Absent or ScenarioUfwStatus.Error)
        {
            return Failure(127, "ufw is unavailable in this scenario.");
        }

        if (!HasPrivilege())
        {
            return Failure(13, "Permission denied while disabling the firewall.");
        }

        State.Ufw.Status = ScenarioUfwStatus.Inactive;
        return Result("status=inactive");
    }

    private string ActiveNumberedUfwStatus()
    {
        var rules = State.Ufw.Rules
            .Select((rule, index) => string.Join(
                ' ',
                $"[{index + 1,2}]",
                $"{rule.Port.ToString(CultureInfo.InvariantCulture)}/{rule.Protocol.ToString().ToLowerInvariant()}" + (rule.IpFamily == ScenarioIpFamily.Ipv6 ? " (v6)" : string.Empty),
                $"{rule.Action} IN",
                rule.Source + (rule.IpFamily == ScenarioIpFamily.Ipv6 ? " (v6)" : string.Empty)));
        return string.Join(Environment.NewLine,
            ["Status: active", string.Empty, "     To                         Action      From", "     --                         ------      ----", .. rules]);
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

        if (!HasPrivilege())
        {
            return Failure(13, "Permission denied while adding a firewall rule.");
        }

        var requestedProtocol = GetArgument(command, "protocol")
            ?? (command.Id.Value == RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure ? "Tcp" : null);
        if (!Enum.TryParse<ScenarioRuleProtocol>(requestedProtocol, true, out var protocol)
            || !int.TryParse(GetArgument(command, "port"), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535
            || !Enum.TryParse<ScenarioIpFamily>(GetArgument(command, "family") ?? "Ipv4", true, out var family))
        {
            return Failure(2, "Firewall rule arguments are invalid.");
        }

        var source = GetArgument(command, "source") ?? "Anywhere";
        source = (source, family) switch
        {
            ("0.0.0.0/0", ScenarioIpFamily.Ipv4) or ("::/0", ScenarioIpFamily.Ipv6) => "Anywhere",
            _ => source,
        };
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

    /// <summary>
    /// Mirrors C304 production semantics at the server-side effect boundary.
    /// The command accepts a validated full semantic rule, never a mutable
    /// display number. It deliberately does not duplicate client SSH-port
    /// policy, so E2 can expose a regression where production would otherwise
    /// delete an unintended SSH row after a concurrent reorder.
    /// </summary>
    private RemoteCommandResult RemoveUfwRuleBySemantic(RemoteCommand command)
    {
        if (State.Ufw.Status is ScenarioUfwStatus.Absent or ScenarioUfwStatus.Error)
        {
            return Failure(127, "ufw is unavailable in this scenario.");
        }

        if (!HasPrivilege())
        {
            return Failure(13, "Permission denied while removing a firewall rule.");
        }

        if (!Enum.TryParse<ScenarioRuleProtocol>(GetArgument(command, "protocol"), true, out var protocol)
            || !Enum.TryParse<ScenarioIpFamily>(GetArgument(command, "family"), true, out var family)
            || !int.TryParse(GetArgument(command, "port"), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535
            || !Enum.TryParse<UfwRuleAction>(GetArgument(command, "action"), true, out var action))
        {
            return Failure(2, "Firewall rule removal arguments are invalid.");
        }

        var source = GetArgument(command, "source") ?? string.Empty;
        source = (source, family) switch
        {
            ("0.0.0.0/0", ScenarioIpFamily.Ipv4) or ("::/0", ScenarioIpFamily.Ipv6) => "Anywhere",
            _ => source,
        };
        var expectedAction = action.ToString().ToUpperInvariant();
        var rule = State.Ufw.Rules.FirstOrDefault(candidate =>
            candidate.Protocol == protocol
            && candidate.Port == port
            && string.Equals(candidate.Source, source, StringComparison.Ordinal)
            && candidate.IpFamily == family
            && string.Equals(candidate.Action, expectedAction, StringComparison.Ordinal));
        if (rule is null)
        {
            return Failure(4, "The selected firewall rule is stale or missing.");
        }

        State.Ufw.Rules.Remove(rule);
        return Result("changed=true");
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

    private RemoteCommandResult EnableUfwWithoutClientSafetyGuard()
    {
        if (State.Ufw.Status is ScenarioUfwStatus.Absent or ScenarioUfwStatus.Error)
        {
            return Failure(127, "ufw is unavailable in this scenario.");
        }

        if (!HasPrivilege())
        {
            return Failure(13, "Permission denied while enabling UFW.");
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

        if (State.Apt.InteractiveBlocker)
        {
            return Failure(30, "Package upgrade requires interactive input in the deterministic scenario.");
        }

        if (!State.Apt.UpgradeSucceeds)
        {
            return Failure(1, "Package upgrade failed in the deterministic scenario.");
        }

        return Result(State.Apt.UpgradeOutput);
    }

    private RemoteCommandResult AptIndexUpdate()
    {
        var result = AptUpdate();
        if (result.Succeeded)
        {
            State.Apt.IndexGeneration++;
        }

        return result;
    }

    private RemoteCommandResult AptIndexVerify() => State.Apt.IndexGeneration > 0 && State.Apt.IndexVerificationSucceeds
        ? Result("apt_index=refreshed")
        : Failure(4, "Package index verification failed in the deterministic scenario.");

    private RemoteCommandResult AptUpgradeProduction()
    {
        var result = AptUpgrade();
        if (result.Succeeded)
        {
            State.Apt.UpgradeGeneration++;
        }

        return result;
    }

    private RemoteCommandResult AptUpgradeVerify() => State.Apt.UpgradeGeneration > 0 && State.Apt.UpgradeVerificationSucceeds
        ? Result("package_upgrade=verified")
        : Failure(4, "Package upgrade verification failed in the deterministic scenario.");

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

    private RemoteCommandResult RebootProduction()
    {
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

        if (State.Ssh.HostKey != ScenarioHostKeyState.Matching)
        {
            return Failure(23, "Host key changed; explicit review is required.");
        }

        State.Reboot.IsRebooting = false;
        if (State.Reboot.AdvanceBootIdentityOnReconnect)
        {
            State.Reboot.BootGeneration++;
            State.Reboot.BootIdentity = $"00000000-0000-0000-0000-{State.Reboot.BootGeneration:D12}";
        }
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
        if (commandId is RemoteCommandCatalog.UbuntuTimezoneCurrentRead or RemoteCommandCatalog.UbuntuTimezoneAvailableList)
        {
            return DiagnosticPhase.Plan;
        }

        if (commandId == RemoteCommandCatalog.UbuntuTimezoneVerifyRead)
        {
            return DiagnosticPhase.Verify;
        }

        if (commandId == RemoteCommandCatalog.UbuntuAptUpgradePlan)
        {
            return DiagnosticPhase.Plan;
        }

        if (commandId is RemoteCommandCatalog.UbuntuAptUpgradeVerify or RemoteCommandCatalog.UbuntuRebootRequiredRead)
        {
            return DiagnosticPhase.Verify;
        }

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
            || commandId.Equals(ScenarioCommandIds.AptUpgrade, StringComparison.Ordinal)
            || commandId.Equals(RemoteCommandCatalog.UbuntuAptIndexUpdate, StringComparison.Ordinal)
            || commandId.Equals(RemoteCommandCatalog.UbuntuAptUpgradeApply, StringComparison.Ordinal)
            || commandId.Equals(RemoteCommandCatalog.UbuntuTimezoneApply, StringComparison.Ordinal)
            || commandId.Equals(RemoteCommandCatalog.UbuntuRebootApply, StringComparison.Ordinal))
        {
            return DiagnosticPhase.Apply;
        }

        return commandId.Contains("verify", StringComparison.Ordinal)
            ? DiagnosticPhase.Verify
            : DiagnosticPhase.Preflight;
    }
}
