using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Infrastructure.Remote;
using VpsReady.Tests;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class UfwToggleWorkflowTests
{
    private const string ActiveWithSshAllows = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        [ 2] 22/tcp (v6)                ALLOW IN    Anywhere (v6)
        """;

    private const string ActiveMissingV6 = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        """;

    private static string StoredSshAllows => StoredUfwFixture.Create();
    private static string StoredEmpty => StoredUfwFixture.Create(allow4: false, allow6: false);

    [Fact]
    public void CatalogUsesKnownBoundedCLocaleCommandsAndRejectsInvalidSshPort()
    {
        var ensure = UbuntuFirewallCommandCatalog.CreateActiveSshAllowEnsureRequest(22, UfwIpFamily.Ipv6);
        var enable = UbuntuFirewallCommandCatalog.CreateToggleRequest(enable: true);
        var disable = UbuntuFirewallCommandCatalog.CreateToggleRequest(enable: false);

        Assert.Equal(RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure, ensure.Id.Value);
        Assert.Equal("family=ipv6 port=22", ensure.SafeArgumentSummary);
        Assert.All([ensure, enable, disable], command =>
        {
            Assert.True(DiagnosticCommandCatalog.IsKnown(command.Id.Value));
            Assert.Equal(TimeSpan.FromSeconds(15), command.Timeout);
            Assert.Equal(OutputCapturePolicy.MetadataOnly, command.OutputCapturePolicy);
            Assert.Equal(0, command.MaximumOutputBytes);
        });
        Assert.Contains("LC_ALL=C LANG=C", UbuntuFirewallCommandCatalog.RequireShellCommand(ensure), StringComparison.Ordinal);
        Assert.Contains("ufw --force enable", UbuntuFirewallCommandCatalog.RequireShellCommand(enable), StringComparison.Ordinal);
        Assert.Contains("ufw --force disable", UbuntuFirewallCommandCatalog.RequireShellCommand(disable), StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => UbuntuFirewallCommandCatalog.CreateActiveSshAllowEnsureRequest(0, UfwIpFamily.Ipv4));
    }

    [Fact]
    public void DisplayReportCannotEstablishEitherStoredFamily()
    {
        Assert.Null(UfwStoredSshParser.Parse("Added user rules (see 'ufw status' for running firewall):\nufw allow 22/tcp"));
        Assert.True(UfwStoredSshParser.Parse(StoredSshAllows)!.HasRequiredAllows);
        Assert.False(UfwStoredSshParser.Parse(StoredUfwFixture.Create(allow6: false))!.HasRequiredAllows);
    }

    [Fact]
    public async Task EnableRequiresConfirmationWithoutRemoteEffects()
    {
        var transport = new RecordingTransport();
        var diagnostics = new RecordingSanitizedSink();
        var result = await Workflow(diagnostics).EnableAsync(transport, confirmed: false);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("VALIDATION_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Empty(transport.Commands);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("22\n2222")]
    [InlineData("22\0")]
    public async Task MissingOrAmbiguousServerPortCannotEnableFirewall(string port)
    {
        var transport = new RecordingTransport(Result(port));
        var result = await Workflow(new RecordingSanitizedSink()).EnableAsync(transport, confirmed: true);
        Assert.False(result.Result.Succeeded);
        Assert.Single(transport.Commands);
        Assert.Equal(RemoteCommandCatalog.SshSessionPortRead, transport.Commands[0].Id.Value);
    }

    [Fact]
    public async Task EnableEnsuresBothFamiliesThenSucceedsOnlyAfterFreshActiveVerification()
    {
        var transport = new RecordingTransport(
            Result("22"), Result("Status: inactive"), Result(StoredEmpty), Result(string.Empty), Result(string.Empty), Result(StoredSshAllows), Result(string.Empty), Result(ActiveWithSshAllows), Result(string.Empty));
        var diagnostics = new RecordingSanitizedSink();

        var result = await Workflow(diagnostics).EnableAsync(transport, confirmed: true);

        Assert.True(result.Result.Succeeded);
        Assert.Equal(OperationState.Applied, result.Result.State);
        Assert.Equal(OperationVerification.Passed, result.Result.Verification);
        Assert.Equal([RemoteCommandCatalog.SshSessionPortRead, RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.UbuntuUfwStoredSshRead, RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure, RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure, RemoteCommandCatalog.UbuntuUfwStoredSshRead, RemoteCommandCatalog.UbuntuUfwEnable, RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.SshConnectionTest], transport.Commands.Select(command => command.Id.Value));
        Assert.Equal("family=ipv4 port=22", transport.Commands[3].SafeArgumentSummary);
        Assert.Equal("family=ipv6 port=22", transport.Commands[4].SafeArgumentSummary);
        Assert.Equal(DiagnosticEventCatalog.OperationSucceeded, diagnostics.Events[^1].EventId);
    }

    [Fact]
    public async Task CancellationAfterStoredPreflightDoesNotDispatchSshAllowOrEnable()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new CancellationIgnoringTransport(cancellation, RemoteCommandCatalog.UbuntuUfwStoredSshRead, 1,
            Result("22"), Result("Status: inactive"), Result(StoredEmpty), Result(""), Result(""), Result(StoredSshAllows), Result(""), Result(ActiveWithSshAllows), Result(""));
        var diagnostics = new RecordingSanitizedSink();

        var result = await Workflow(diagnostics).EnableAsync(transport, confirmed: true, cancellation.Token);

        Assert.Equal(OperationErrorCode.Cancelled, result.Result.ErrorCode);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value is RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure or RemoteCommandCatalog.UbuntuUfwEnable);
        Assert.Single(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationCancelled);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task CancellationAfterVerifiedStoredAllowsDoesNotDispatchEnable()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new CancellationIgnoringTransport(cancellation, RemoteCommandCatalog.UbuntuUfwStoredSshRead, 2,
            Result("22"), Result("Status: inactive"), Result(StoredEmpty), Result(""), Result(""), Result(StoredSshAllows), Result("Status: inactive"), Result(ActiveWithSshAllows), Result(""));
        var diagnostics = new RecordingSanitizedSink();

        var result = await Workflow(diagnostics).EnableAsync(transport, confirmed: true, cancellation.Token);

        Assert.Equal(OperationErrorCode.Cancelled, result.Result.ErrorCode);
        Assert.Equal(OperationState.PartiallyApplied, result.Result.State);
        Assert.Equal(2, transport.Commands.Count(command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure));
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwEnable);
        Assert.Single(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationCancelled);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task CancellationAfterEnableDispatchVerifiesCurrentStateAndSshContinuityWithoutFurtherMutation()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new LateEnableCancellationTransport(cancellation);
        var diagnostics = new RecordingSanitizedSink();

        var outcome = await Workflow(diagnostics).EnableAsync(transport, confirmed: true, cancellation.Token);

        Assert.True(outcome.Result.Cancelled);
        Assert.Equal(OperationState.Applied, outcome.Result.State);
        Assert.Equal(OperationVerification.Passed, outcome.Result.Verification);
        Assert.Equal(OperationRecovery.Succeeded, outcome.Result.Recovery);
        Assert.True(outcome.SnapshotIsCurrent);
        Assert.Equal(UfwFirewallState.Active, outcome.Snapshot?.State);
        Assert.Equal(22, outcome.SessionSshPort);
        Assert.Equal("Firewall enable was cancelled, but the active state, SSH allow rules and current-session continuity were verified.", outcome.Result.UserMessage);
        Assert.Equal(DiagnosticEventCatalog.OperationCancelled, Assert.Single(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationCancelled).EventId);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);

        var enableIndex = transport.Commands.FindIndex(command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwEnable);
        Assert.True(enableIndex >= 0);
        Assert.Equal([RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.SshConnectionTest],
            transport.Commands.Skip(enableIndex + 1).Select(command => command.Id.Value));
        Assert.DoesNotContain(transport.Commands.Skip(enableIndex + 1), command => command.Id.Value is
            RemoteCommandCatalog.UbuntuUfwEnable or RemoteCommandCatalog.UbuntuUfwDisable or RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure);
    }

    [Fact]
    public async Task CancellationAfterEnableWithUnreadableStateReportsUnknownAndSafeNextStep()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new LateEnableCancellationTransport(cancellation, lateStateExitCode: 1);
        var diagnostics = new RecordingSanitizedSink();

        var outcome = await Workflow(diagnostics).EnableAsync(transport, confirmed: true, cancellation.Token);

        Assert.True(outcome.Result.Cancelled);
        Assert.Equal(OperationState.Unknown, outcome.Result.State);
        Assert.Equal(OperationVerification.Unknown, outcome.Result.Verification);
        Assert.Equal(OperationRecovery.Failed, outcome.Result.Recovery);
        Assert.False(outcome.SnapshotIsCurrent);
        Assert.Null(outcome.Snapshot);
        Assert.Contains("Do not retry", outcome.Result.NextAction, StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);

        var enableIndex = transport.Commands.FindIndex(command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwEnable);
        Assert.Equal([RemoteCommandCatalog.UbuntuUfwRuleListRead], transport.Commands.Skip(enableIndex + 1).Select(command => command.Id.Value));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CancellationAfterEnableFailsClosedWhenSshRulesOrContinuityAreNotVerified(bool missingIpv6, bool failContinuity)
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new LateEnableCancellationTransport(
            cancellation,
            lateStateOutput: missingIpv6 ? ActiveMissingV6 : null,
            continuityExitCode: failContinuity ? 255 : 0);
        var diagnostics = new RecordingSanitizedSink();

        var outcome = await Workflow(diagnostics).EnableAsync(transport, confirmed: true, cancellation.Token);

        Assert.True(outcome.Result.Cancelled);
        Assert.Equal(OperationState.PartiallyApplied, outcome.Result.State);
        Assert.Equal(OperationVerification.Failed, outcome.Result.Verification);
        Assert.Equal(OperationRecovery.Failed, outcome.Result.Recovery);
        Assert.True(outcome.SnapshotIsCurrent);
        Assert.Equal(UfwFirewallState.Active, outcome.Snapshot?.State);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Theory]
    [InlineData("network", false, "NETWORK_UNAVAILABLE")]
    [InlineData("timeout", false, "OPERATION_TIMEOUT")]
    [InlineData("timeout-exception", false, "OPERATION_TIMEOUT")]
    [InlineData("network", true, "RECOVERY_FAILED")]
    public async Task EnableTransportFailureAfterPossibleMutationPerformsReadOnlyVerification(
        string failureKind,
        bool failRecoveryRead,
        string expectedErrorCode)
    {
        var transport = new LateEnableTransportFailureTransport(failureKind, failRecoveryRead);
        var diagnostics = new RecordingSanitizedSink();

        var outcome = await Workflow(diagnostics).EnableAsync(transport, confirmed: true);

        Assert.False(outcome.Result.Succeeded);
        Assert.Equal(expectedErrorCode, outcome.Result.ErrorCode?.ToStableCode());
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationRecoveryRequired);

        var enableIndex = transport.Commands.FindIndex(command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwEnable);
        Assert.True(enableIndex >= 0);
        Assert.DoesNotContain(transport.Commands.Skip(enableIndex + 1), command => command.Id.Value is
            RemoteCommandCatalog.UbuntuUfwEnable or RemoteCommandCatalog.UbuntuUfwDisable or RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure);

        if (failRecoveryRead)
        {
            Assert.Equal(OperationState.Unknown, outcome.Result.State);
            Assert.Equal(OperationVerification.Unknown, outcome.Result.Verification);
            Assert.Equal(OperationRecovery.Failed, outcome.Result.Recovery);
            Assert.False(outcome.SnapshotIsCurrent);
            Assert.Contains("Stop further changes", outcome.Result.NextAction, StringComparison.OrdinalIgnoreCase);
            Assert.Equal([RemoteCommandCatalog.UbuntuUfwRuleListRead], transport.Commands.Skip(enableIndex + 1).Select(command => command.Id.Value));
        }
        else
        {
            Assert.Equal(OperationState.Applied, outcome.Result.State);
            Assert.Equal(OperationVerification.Passed, outcome.Result.Verification);
            Assert.Equal(OperationRecovery.Succeeded, outcome.Result.Recovery);
            Assert.True(outcome.SnapshotIsCurrent);
            Assert.Equal(UfwFirewallState.Active, outcome.Snapshot?.State);
            Assert.Contains("continuity were verified", outcome.Result.UserMessage, StringComparison.Ordinal);
            Assert.Contains("UFW is already active", outcome.Result.NextAction, StringComparison.Ordinal);
            Assert.Equal([RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.SshConnectionTest],
                transport.Commands.Skip(enableIndex + 1).Select(command => command.Id.Value));
        }
    }

    [Fact]
    public async Task CancellationAfterDisablePreflightDoesNotDispatchDisable()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new CancellationIgnoringTransport(cancellation, RemoteCommandCatalog.UbuntuUfwRuleListRead, 1,
            Result(ActiveWithSshAllows), Result(""), Result("Status: inactive"));
        var diagnostics = new RecordingSanitizedSink();

        var result = await Workflow(diagnostics).DisableAsync(transport, confirmed: true, cancellation.Token);

        Assert.Equal(OperationErrorCode.Cancelled, result.Result.ErrorCode);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwDisable);
        Assert.Single(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationCancelled);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task OrdinaryUpstreamNormalizedReportDoesNotPreventSupportedEnable()
    {
        // Verified using UFW 0.36.2 UFWFrontend.get_show_added: both-family
        // and IPv4-only objects produce this identical display report.
        var normalized = "Added user rules (see 'ufw status' for running firewall):\nufw allow 22/tcp";
        Assert.Null(UfwStoredSshParser.Parse(normalized));
        var transport = new RecordingTransport(Result("22"), Result("Status: inactive"), Result(StoredEmpty), Result(string.Empty), Result(string.Empty), Result(StoredSshAllows), Result(string.Empty), Result(ActiveWithSshAllows), Result(string.Empty));
        var result = await Workflow(new RecordingSanitizedSink()).EnableAsync(transport, confirmed: true);
        Assert.True(result.Result.Succeeded);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwAddedRulesRead);
    }

    [Fact]
    public async Task ActiveFirewallWithoutBothSshFamiliesFailsBeforeAnyMutation()
    {
        var transport = new RecordingTransport(Result("22"), Result(ActiveMissingV6), Result(StoredUfwFixture.Create(allow6: false)));
        var result = await Workflow(new RecordingSanitizedSink()).EnableAsync(transport, confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("VALIDATION_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(3, transport.Commands.Count);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwEnable);
    }

    [Fact]
    public async Task RangeStartingAtSshPortDoesNotCountAsExactReachabilityRule()
    {
        var rangeInsteadOfExact = ActiveWithSshAllows.Replace("[ 1] 22/tcp", "[ 1] 22:23/tcp", StringComparison.Ordinal);
        var transport = new RecordingTransport(Result("22"), Result(rangeInsteadOfExact), Result(StoredSshAllows));

        var result = await Workflow(new RecordingSanitizedSink()).EnableAsync(transport, confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal("VALIDATION_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwEnable);
    }

    [Fact]
    public async Task MalformedStoredPortBlocksEnableBeforeAnyMutation()
    {
        var malformed = StoredSshAllows.Replace("port=22\n", "port=22\0\n", StringComparison.Ordinal);
        var transport = new RecordingTransport(Result("22"), Result("Status: inactive"), Result(malformed));
        var diagnostics = new RecordingSanitizedSink();

        var result = await Workflow(diagnostics).EnableAsync(transport, confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(3, transport.Commands.Count);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal("UNSUPPORTED_ENVIRONMENT", result.Result.ErrorCode?.ToStableCode());
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwEnable);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationFailed && item.CommandId == RemoteCommandCatalog.UbuntuUfwStoredSshRead);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task AlreadyActiveSafeFirewallVerifiesAuthenticatedContinuityBeforeIdempotentSuccess()
    {
        var transport = new RecordingTransport(Result("22"), Result(ActiveWithSshAllows), Result(StoredSshAllows), Result(string.Empty));
        var diagnostics = new RecordingSanitizedSink();

        var result = await Workflow(diagnostics).EnableAsync(transport, confirmed: true);

        Assert.True(result.Result.Succeeded);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal([RemoteCommandCatalog.SshSessionPortRead, RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.UbuntuUfwStoredSshRead, RemoteCommandCatalog.SshConnectionTest], transport.Commands.Select(command => command.Id.Value));
        Assert.Equal(DiagnosticEventCatalog.OperationSucceeded, diagnostics.Events[^1].EventId);
        Assert.Equal(RemoteCommandCatalog.SshConnectionTest, diagnostics.Events[^1].CommandId);
    }

    [Fact]
    public async Task AlreadyActiveContinuityFailureRefreshesWithoutFalseSuccess()
    {
        var transport = new RecordingTransport(Result("22"), Result(ActiveWithSshAllows), Result(StoredSshAllows), Result(string.Empty, exitCode: 25), Result(ActiveWithSshAllows));
        var diagnostics = new RecordingSanitizedSink();

        var result = await Workflow(diagnostics).EnableAsync(transport, confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("REMOTE_COMMAND_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.Equal([RemoteCommandCatalog.SshSessionPortRead, RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.UbuntuUfwStoredSshRead, RemoteCommandCatalog.SshConnectionTest, RemoteCommandCatalog.UbuntuUfwRuleListRead], transport.Commands.Select(command => command.Id.Value));
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task EnableVerificationMismatchUsesReadOnlyRecoveryAndNeverReportsSuccess()
    {
        var transport = new RecordingTransport(
            Result("22"), Result("Status: inactive"), Result(StoredEmpty), Result(string.Empty), Result(string.Empty), Result(StoredSshAllows), Result(string.Empty), Result("Status: inactive"), Result("Status: inactive"));
        var diagnostics = new RecordingSanitizedSink();
        var result = await Workflow(diagnostics).EnableAsync(transport, confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("VERIFICATION_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.Equal(RemoteCommandCatalog.UbuntuUfwRuleListRead, transport.Commands[^1].Id.Value);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task DisableIsExplicitAndSucceedsOnlyAfterFreshInactiveVerification()
    {
        var transport = new RecordingTransport(Result(ActiveWithSshAllows), Result(string.Empty), Result("Status: inactive"));
        var result = await Workflow(new RecordingSanitizedSink()).DisableAsync(transport, confirmed: true);

        Assert.True(result.Result.Succeeded);
        Assert.Equal(OperationState.Applied, result.Result.State);
        Assert.Equal([RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.UbuntuUfwDisable, RemoteCommandCatalog.UbuntuUfwRuleListRead], transport.Commands.Select(command => command.Id.Value));
    }

    [Fact]
    public async Task DisableThatDropsAfterApplyingReconnectsAndRunsSeparatelyCorrelatedVerifiedEnable()
    {
        var diagnostics = new RecordingSanitizedSink();
        var run = DiagnosticRunContext.StartSession();
        var disableCorrelation = run.StartOperation("disable_firewall");
        var disableDiagnostics = SessionOperationDiagnostics.ForFirewall(disableCorrelation, fallbackSink: null, "disable");
        var transport = new PostEffectDropTransport();

        var result = await Workflow(diagnostics).DisableAsync(transport, confirmed: true, disableDiagnostics);
        await disableDiagnostics.FinalizeAsync(result.Result);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("NETWORK_UNAVAILABLE", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.True(transport.FirewallActive);
        Assert.Equal(1, transport.ReconnectAttempts);
        Assert.Contains(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwEnable);
        Assert.Contains(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure
            && command.SafeArgumentSummary == "family=ipv4 port=22");
        Assert.Contains(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure
            && command.SafeArgumentSummary == "family=ipv6 port=22");
        Assert.Equal(
            [
                RemoteCommandCatalog.UbuntuUfwRuleListRead,
                RemoteCommandCatalog.UbuntuUfwDisable,
                RemoteCommandCatalog.SshSessionPortRead,
                RemoteCommandCatalog.UbuntuUfwRuleListRead,
                RemoteCommandCatalog.UbuntuUfwStoredSshRead,
                RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure,
                RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure,
                RemoteCommandCatalog.UbuntuUfwStoredSshRead,
                RemoteCommandCatalog.UbuntuUfwEnable,
                RemoteCommandCatalog.UbuntuUfwRuleListRead,
                RemoteCommandCatalog.SshConnectionTest,
            ],
            transport.Commands.Select(command => command.Id.Value));

        var disableStarted = Assert.Single(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationStarted && item.Action == "DisableFirewall");
        var recoveryStarted = Assert.Single(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationStarted && item.Action == "EnableFirewall");
        Assert.NotEqual(disableStarted.Correlation.OperationId, recoveryStarted.Correlation.OperationId);
        Assert.Equal(disableStarted.Correlation.SessionId, recoveryStarted.Correlation.SessionId);
        Assert.Equal(disableStarted.Correlation.RunId, recoveryStarted.Correlation.RunId);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationFailed
            && item.Correlation.OperationId == disableStarted.Correlation.OperationId);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded
            && item.Correlation.OperationId == disableStarted.Correlation.OperationId);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded
            && item.Correlation.OperationId == recoveryStarted.Correlation.OperationId);
    }

    [Fact]
    public async Task DisableCancelledAfterApplyReconnectsAndRestoresPriorActiveState()
    {
        using var cancellation = new CancellationTokenSource();
        var diagnostics = new RecordingSanitizedSink();
        var run = DiagnosticRunContext.StartSession();
        var disableCorrelation = run.StartOperation("disable_firewall");
        var disableDiagnostics = SessionOperationDiagnostics.ForFirewall(disableCorrelation, fallbackSink: null, "disable");
        var transport = new PostEffectDropTransport(cancellation);

        var result = await Workflow(diagnostics).DisableAsync(transport, confirmed: true, disableDiagnostics, cancellation.Token);
        await disableDiagnostics.FinalizeAsync(result.Result);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(OperationErrorCode.Cancelled, result.Result.ErrorCode);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal(OperationVerification.Failed, result.Result.Verification);
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.True(transport.FirewallActive);
        Assert.Equal(1, transport.ReconnectAttempts);
        Assert.Contains(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwEnable);

        var disableStarted = Assert.Single(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationStarted && item.Action == "DisableFirewall");
        var recoveryStarted = Assert.Single(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationStarted && item.Action == "EnableFirewall");
        Assert.NotEqual(disableStarted.Correlation.OperationId, recoveryStarted.Correlation.OperationId);
        Assert.Equal(disableStarted.Correlation.SessionId, recoveryStarted.Correlation.SessionId);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationCancelled
            && item.Correlation.OperationId == disableStarted.Correlation.OperationId);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded
            && item.Correlation.OperationId == disableStarted.Correlation.OperationId);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded
            && item.Correlation.OperationId == recoveryStarted.Correlation.OperationId);
    }

    [Theory]
    [InlineData(22, true)]
    [InlineData(2222, true)]
    [InlineData(2222, false)]
    public async Task EnableRespectsServerPortAndConfiguredIpv6WithoutChangingIpv6(int port, bool ipv6)
    {
        var queue = new List<RemoteCommandResult> { Result(port.ToString(System.Globalization.CultureInfo.InvariantCulture)), Result("Status: inactive"), Result(StoredUfwFixture.Create(port, allow4: false, allow6: false, ipv6: ipv6)), Result("") };
        if (ipv6) { queue.Add(Result("")); }
        queue.AddRange([Result(StoredUfwFixture.Create(port, ipv6: ipv6)), Result(""), Result((ipv6 ? ActiveWithSshAllows : ActiveMissingV6).Replace("22/tcp", $"{port}/tcp", StringComparison.Ordinal)), Result("")]);
        var transport = new RecordingTransport([.. queue]);
        var result = await Workflow(new RecordingSanitizedSink()).EnableAsync(transport, true);
        Assert.True(result.Result.Succeeded);
        Assert.Equal(ipv6 ? 2 : 1, transport.Commands.Count(command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("deny")]
    [InlineData("ipv6-disabled-session")]
    [InlineData("port-changed")]
    public async Task UnsupportedPreflightNeverMutates(string kind)
    {
        var evidence = kind switch
        {
            "deny" => StoredUfwFixture.Create(rules4: "-A ufw-user-input -p tcp --dport 22 -j DROP\n" + StoredUfwFixture.Allow(false, 22)),
            "ipv6-disabled-session" => StoredUfwFixture.Create(ipv6: false, session6: true),
            "port-changed" => StoredUfwFixture.Create(2222),
            _ => "unknown",
        };
        var transport = new RecordingTransport(Result("22"), Result("Status: inactive"), Result(evidence));
        var result = await Workflow(new RecordingSanitizedSink()).EnableAsync(transport, true);
        Assert.False(result.Result.Succeeded);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal(3, transport.Commands.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(77)]
    public async Task MissingFamilyOrLostPrivilegeAfterEnsureNeverEnables(int exitCode)
    {
        var transport = new RecordingTransport(Result("22"), Result("Status: inactive"), Result(StoredEmpty), Result(""), Result(""), Result(StoredUfwFixture.Create(allow6: false), exitCode), Result("Status: inactive"));
        var diagnostics = new RecordingSanitizedSink();
        var result = await Workflow(diagnostics).EnableAsync(transport, true);
        Assert.False(result.Result.Succeeded);
        Assert.Equal(OperationState.PartiallyApplied, result.Result.State);
        Assert.Equal(exitCode == 77 ? "PRIVILEGE_DENIED" : "VERIFICATION_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwEnable);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    private static UfwToggleWorkflow Workflow(RecordingSanitizedSink sink) => new(new RedactingDiagnosticSink(new FailClosedRedactor(), sink));
    private static RemoteCommandResult Result(string output, int exitCode = 0) => new(exitCode, output, string.Empty, TimeSpan.FromMilliseconds(5));

    private sealed class RecordingTransport(params RemoteCommandResult[] results) : IRemoteTransport
    {
        private readonly Queue<RemoteCommandResult> results = new(results);
        public List<RemoteCommand> Commands { get; } = [];
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            return VpsReady.Tests.ProductionOutput.CaptureAsync(command, results.Count == 0 ? throw new InvalidOperationException("Unexpected command.") : results.Dequeue(), cancellationToken);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PostEffectDropTransport : IRebootReconnectTransport
    {
        private readonly CancellationTokenSource? cancelAfterDisable;
        private bool connected = true;
        private bool allowedIpv4 = true;
        private bool allowedIpv6 = true;
        private bool droppedAfterDisable;

        public PostEffectDropTransport(CancellationTokenSource? cancelAfterDisable = null) =>
            this.cancelAfterDisable = cancelAfterDisable;

        public List<RemoteCommand> Commands { get; } = [];
        public bool FirewallActive { get; private set; } = true;
        public int ReconnectAttempts { get; private set; }

        public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            if (!connected)
            {
                throw new RemoteTransportException(RemoteTransportFailureKind.Network);
            }

            if (command.Id.Value == RemoteCommandCatalog.UbuntuUfwDisable && !droppedAfterDisable)
            {
                FirewallActive = false;
                droppedAfterDisable = true;
                if (cancelAfterDisable is not null)
                {
                    cancelAfterDisable.Cancel();
                    return await ProductionOutput.CaptureAsync(command, Result(string.Empty), CancellationToken.None);
                }

                connected = false;
                throw new RemoteTransportException(RemoteTransportFailureKind.Network);
            }

            var output = command.Id.Value switch
            {
                RemoteCommandCatalog.SshSessionPortRead => "22",
                RemoteCommandCatalog.UbuntuUfwRuleListRead => FirewallActive ? ActiveWithSshAllows : "Status: inactive",
                RemoteCommandCatalog.UbuntuUfwStoredSshRead => StoredSshAllows,
                RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure => EnsureSshAllow(command),
                RemoteCommandCatalog.UbuntuUfwEnable => EnableFirewall(),
                RemoteCommandCatalog.SshConnectionTest => FirewallActive && allowedIpv4 && allowedIpv6
                    ? string.Empty
                    : throw new RemoteTransportException(RemoteTransportFailureKind.Network),
                _ => throw new InvalidOperationException($"Unexpected test command '{command.Id.Value}'."),
            };

            return await ProductionOutput.CaptureAsync(command, Result(output), cancellationToken);
        }

        public Task ReconnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            ReconnectAttempts++;
            connected = true;
            return Task.CompletedTask;
        }

        public Task<BootIdentityReadResult> ReadBootIdentityAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(BootIdentityReadResult.Unavailable);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private string EnsureSshAllow(RemoteCommand command)
        {
            if (command.SafeArgumentSummary == "family=ipv4 port=22")
            {
                allowedIpv4 = true;
            }
            else if (command.SafeArgumentSummary == "family=ipv6 port=22")
            {
                allowedIpv6 = true;
            }
            else
            {
                throw new InvalidOperationException("Unexpected SSH-allow request in the recovery test.");
            }

            return string.Empty;
        }

        private string EnableFirewall()
        {
            if (!allowedIpv4 || !allowedIpv6)
            {
                throw new InvalidOperationException("Firewall recovery was attempted before SSH allows were ensured.");
            }

            FirewallActive = true;
            return string.Empty;
        }
    }

    // Deliberately ignores the caller token to model a transport that returns
    // after cancellation. The workflow must own its pre-mutation boundary.
    private sealed class CancellationIgnoringTransport(
        CancellationTokenSource cancellation,
        string cancelAfterCommandId,
        int cancelAfterOccurrence,
        params RemoteCommandResult[] results) : IRemoteTransport
    {
        private readonly Queue<RemoteCommandResult> queuedResults = new(results);
        private int matchingCommands;
        public List<RemoteCommand> Commands { get; } = [];

        public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            var result = await ProductionOutput.CaptureAsync(command,
                queuedResults.Count == 0 ? throw new InvalidOperationException("Unexpected command.") : queuedResults.Dequeue(),
                CancellationToken.None);
            if (command.Id.Value == cancelAfterCommandId && ++matchingCommands == cancelAfterOccurrence)
            {
                cancellation.Cancel();
            }

            return result;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class LateEnableCancellationTransport(
        CancellationTokenSource cancellation,
        string? lateStateOutput = null,
        int lateStateExitCode = 0,
        int continuityExitCode = 0) : IRemoteTransport
    {
        private int storedReads;
        private int listReads;
        private bool active;
        private bool allowedIpv4;
        private bool allowedIpv6;

        public List<RemoteCommand> Commands { get; } = [];

        public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            cancellationToken.ThrowIfCancellationRequested();

            var (output, exitCode) = command.Id.Value switch
            {
                RemoteCommandCatalog.SshSessionPortRead => ("22", 0),
                RemoteCommandCatalog.UbuntuUfwRuleListRead => listReads++ == 0
                    ? ("Status: inactive", 0)
                    : (lateStateOutput ?? (active ? ActiveWithSshAllows : "Status: inactive"), lateStateExitCode),
                RemoteCommandCatalog.UbuntuUfwStoredSshRead => storedReads++ == 0 ? (StoredEmpty, 0) : (StoredSshAllows, 0),
                RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure => EnsureSshAllow(command),
                RemoteCommandCatalog.UbuntuUfwEnable => EnableAfterEffect(cancellationToken),
                RemoteCommandCatalog.SshConnectionTest => (string.Empty, continuityExitCode),
                _ => throw new InvalidOperationException($"Unexpected test command '{command.Id.Value}'."),
            };

            return await VpsReady.Tests.ProductionOutput.CaptureAsync(command, Result(output, exitCode), cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private (string Output, int ExitCode) EnsureSshAllow(RemoteCommand command)
        {
            if (command.SafeArgumentSummary == "family=ipv4 port=22")
            {
                allowedIpv4 = true;
            }
            else if (command.SafeArgumentSummary == "family=ipv6 port=22")
            {
                allowedIpv6 = true;
            }
            else
            {
                throw new InvalidOperationException("Unexpected SSH-allow request in the cancellation regression.");
            }

            return (string.Empty, 0);
        }

        private (string Output, int ExitCode) EnableAfterEffect(CancellationToken cancellationToken)
        {
            if (!allowedIpv4 || !allowedIpv6)
            {
                throw new InvalidOperationException("UFW enable was dispatched before both SSH families were allowed.");
            }

            active = true;
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return (string.Empty, 0);
        }
    }

    private sealed class LateEnableTransportFailureTransport(string failureKind, bool failRecoveryRead) : IRemoteTransport
    {
        private int listReads;
        private int storedReads;
        private bool active;
        private bool allowedIpv4;
        private bool allowedIpv6;

        public List<RemoteCommand> Commands { get; } = [];

        public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);

            if (command.Id.Value == RemoteCommandCatalog.UbuntuUfwEnable)
            {
                if (!allowedIpv4 || !allowedIpv6)
                {
                    throw new InvalidOperationException("The test attempted UFW enable before ensuring both SSH families.");
                }

                active = true;
                throw failureKind switch
                {
                    "network" => new RemoteTransportException(RemoteTransportFailureKind.Network),
                    "timeout" => new RemoteTransportException(RemoteTransportFailureKind.Timeout),
                    "timeout-exception" => new TimeoutException("test-owned timeout after firewall effect"),
                    _ => new InvalidOperationException("Unknown test transport failure kind."),
                };
            }

            var result = command.Id.Value switch
            {
                RemoteCommandCatalog.SshSessionPortRead => Result("22"),
                RemoteCommandCatalog.UbuntuUfwRuleListRead => listReads++ == 0
                    ? Result("Status: inactive")
                    : failRecoveryRead ? Result("", exitCode: 1) : Result(active ? ActiveWithSshAllows : "Status: inactive"),
                RemoteCommandCatalog.UbuntuUfwStoredSshRead => Result(storedReads++ == 0 ? StoredEmpty : StoredSshAllows),
                RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure => EnsureSshAllow(command),
                RemoteCommandCatalog.SshConnectionTest => Result(string.Empty),
                _ => throw new InvalidOperationException($"Unexpected test command '{command.Id.Value}'."),
            };

            return await ProductionOutput.CaptureAsync(command, result, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private RemoteCommandResult EnsureSshAllow(RemoteCommand command)
        {
            if (command.SafeArgumentSummary == "family=ipv4 port=22")
            {
                allowedIpv4 = true;
            }
            else if (command.SafeArgumentSummary == "family=ipv6 port=22")
            {
                allowedIpv6 = true;
            }
            else
            {
                throw new InvalidOperationException("Unexpected SSH allow request in the transport-failure regression.");
            }

            return Result(string.Empty);
        }
    }

    private sealed class RecordingSanitizedSink : ISanitizedDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];
        public Task WriteSanitizedAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken) { Events.Add(diagnosticEvent); return Task.CompletedTask; }
    }
}
