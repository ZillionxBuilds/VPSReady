using System.Windows.Input;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Application;

public enum ReadinessFilter { All, NeedsAttention, Unknown, Passed, OptionalManual }

/// <summary>Session authority and presentation. No automatic check, transport ownership or mutation.</summary>
public sealed class ReadinessViewModel : ObservableObject, IDisposable
{
    private readonly object gate = new();
    private readonly IApplicationSession session;
    private readonly IReadinessCollector collector;
    private readonly IDiagnosticSink diagnostics;
    private readonly Action<ReadinessActionTarget> navigate;
    private readonly TimeProvider clock;
    private readonly ITimer freshnessTimer;
    private CancellationTokenSource? activeCancellation;
    private ReadinessSnapshot? snapshot;
    private Dictionary<ReadinessCheckId, ReadinessCheckResult> liveRows = [];
    private string? observedSessionId;
    private string? observedExternalOperation;
    private string? ownOperation;
    private string? runId;
    private long generation;
    private bool checking;
    private bool disposed;
    private ReadinessFilter filter;
    private DateTimeOffset lastTickUtc;
    private long lastTickTimestamp;

    public ReadinessViewModel(IApplicationSession session, IReadinessCollector collector, IDiagnosticSink diagnostics,
        Action<ReadinessActionTarget> navigate, TimeProvider? timeProvider = null)
    {
        this.session = session;
        this.collector = collector;
        this.diagnostics = diagnostics;
        this.navigate = navigate;
        clock = timeProvider ?? TimeProvider.System;
        observedSessionId = session.Snapshot.SessionId;
        CancelCommand = new DelegateCommand(Cancel);
        session.StateChanged += SessionChanged;
        lastTickUtc = clock.GetUtcNow();
        lastTickTimestamp = clock.GetTimestamp();
        freshnessTimer = clock.CreateTimer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public ICommand CancelCommand { get; }
    public bool CanCheck => !disposed && session.Snapshot.IsConnected && session.Snapshot.ActiveOperationId is null && !IsChecking;
    public bool IsChecking { get { lock (gate) { return checking; } } }
    public bool CanCancel => IsChecking;
    public bool IsDisconnected => !session.Snapshot.IsConnected;
    public bool CanExportEvidence => !IsChecking && RunId is not null && OperationId is not null;
    public string Profile { get; } = CoreBasicReadinessProfile.DisplayName + " · v" + CoreBasicReadinessProfile.Version;
    public string? OperationId { get { lock (gate) { return ownOperation ?? lastOperationId; } } }
    private string? lastOperationId;
    public string? RunId { get { lock (gate) { return runId; } } }
    public IReadOnlyList<string> ManualExclusions { get; } = CoreBasicReadinessProfile.ManualExclusions;
    public IReadOnlyList<ReadinessFilter> Filters { get; } = Enum.GetValues<ReadinessFilter>();
    public ReadinessFilter Filter { get => filter; set { if (SetProperty(ref filter, value)) { OnPropertyChanged(nameof(Rows)); } } }
    public ReadinessVerdict Verdict
    {
        get
        {
            lock (gate)
            {
                var current = session.Snapshot;
                return ReadinessEvaluator.Evaluate(snapshot, current.IsConnected, current.SessionId, generation, clock, checking);
            }
        }
    }
    public string Status => Verdict switch
    {
        ReadinessVerdict.NotChecked => "Not checked. Connect, then explicitly Check VPS Ready. No automatic inspection.",
        ReadinessVerdict.Checking => "Checking — read-only. Previous green results are not current.",
        ReadinessVerdict.Stale => "STALE — reconnect or explicitly recheck. Retained findings are historical context only.",
        ReadinessVerdict.UnsupportedProfile => "Unsupported profile — this fresh observation does not identify Ubuntu.",
        ReadinessVerdict.Incomplete => "INCOMPLETE — required evidence is unavailable, cancelled, expired or diagnostics could not be persisted.",
        ReadinessVerdict.NeedsAttention => "Needs attention — a required check failed. Review the finding before changing anything.",
        ReadinessVerdict.ReadyWithWarnings => "Ready with warnings — all nine required checks passed; advisory limitations remain. Not real-VPS qualification.",
        ReadinessVerdict.Ready => "Ready — current Core Basic observations passed. Provider/workload/manual checks are not assessed.",
        _ => "Incomplete.",
    };
    public string EvidenceTime => snapshot is null ? "No observation yet." : $"Observed {snapshot.ObservedUtc:O}. Fresh for at most 300 monotonic seconds; no automatic recheck.";
    public IReadOnlyList<ReadinessRowViewModel> Rows
    {
        get
        {
            lock (gate)
            {
                var selected = checking ? liveRows.Values.ToDictionary(row => row.Id) : snapshot?.Rows.ToDictionary(row => row.Id) ?? [];
                return CoreBasicReadinessProfile.Checks.Select(definition => new ReadinessRowViewModel(definition,
                    selected.GetValueOrDefault(definition.Id) ?? new(definition.Id, ReadinessCheckState.NotRun, ReadinessReason.NotRun, ReadinessSource.None),
                    snapshot?.SessionId ?? observedSessionId ?? "", snapshot?.Generation ?? generation, navigate))
                    .Where(row => filter switch
                    {
                        ReadinessFilter.NeedsAttention => row.State is ReadinessCheckState.Fail or ReadinessCheckState.Warn or ReadinessCheckState.Error,
                        ReadinessFilter.Unknown => row.State is ReadinessCheckState.Unknown or ReadinessCheckState.NotRun,
                        ReadinessFilter.Passed => row.State == ReadinessCheckState.Pass,
                        ReadinessFilter.OptionalManual => !row.Required,
                        _ => true,
                    }).ToArray();
            }
        }
    }

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = session.Snapshot;
        long capturedGeneration;
        CorrelationIds correlation;
        CancellationToken checkCancellation;
        lock (gate)
        {
            if (!CanCheck || current.SessionId is null) { return; }
            checking = true;
            liveRows = [];
            activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            checkCancellation = activeCancellation.Token;
            capturedGeneration = ++generation;
            ownOperation = DiagnosticCorrelationFactory.NewOperationId();
            runId = DiagnosticCorrelationFactory.NewRunId();
            correlation = new(current.SessionId, runId, ownOperation, "validate");
        }
        RefreshPresentation();
        ReadinessSnapshot? observed = null;
        try
        {
            var result = await session.RunOperationForSessionAsync(correlation.OperationId, CoreBasicReadinessProfile.TotalTimeout,
                async (transport, operationToken) =>
                {
                    observed = await collector.CollectAsync(transport, correlation.SessionId, capturedGeneration, correlation,
                        row => Progress(row, capturedGeneration), operationToken).ConfigureAwait(false);
                    return observed.Execution switch
                    {
                        ReadinessExecution.Completed => OperationResult.Success(correlation.OperationId, OperationState.Unchanged),
                        ReadinessExecution.Cancelled => OperationResult.Cancellation(correlation.OperationId, OperationState.Unchanged),
                        _ => OperationResult.Failure(correlation.OperationId, observed.Execution == ReadinessExecution.DiagnosticsFailed
                            ? OperationErrorCode.LocalIo : observed.Execution == ReadinessExecution.TimedOut ? OperationErrorCode.Timeout : OperationErrorCode.Unexpected, OperationState.Unchanged),
                    };
                }, current.SessionId, checkCancellation).ConfigureAwait(false);

            observed ??= new(correlation.SessionId, capturedGeneration, CoreBasicReadinessProfile.Version, clock.GetTimestamp(), clock.GetUtcNow(),
                result.Cancelled ? ReadinessExecution.Cancelled : ReadinessExecution.CollectorFailed, []);
            if (!result.Succeeded && observed.Execution == ReadinessExecution.Completed)
            {
                observed = WithExecution(observed, result.Cancelled ? ReadinessExecution.Cancelled
                    : result.ErrorCode == OperationErrorCode.Timeout ? ReadinessExecution.TimedOut : ReadinessExecution.CollectorFailed);
            }
            var effective = ReadinessEvaluator.Evaluate(observed, session.Snapshot.IsConnected, session.Snapshot.SessionId,
                Generation, clock);
            // Exactly one terminal event, chosen only after session cancellation,
            // identity and timeout authority. Never let an old green completion revive.
            try
            {
                await diagnostics.WriteAsync(new(result.Cancelled ? DiagnosticEventCatalog.ReadinessCancelled
                    : result.Succeeded && effective != ReadinessVerdict.Stale ? DiagnosticEventCatalog.ReadinessCompleted : DiagnosticEventCatalog.ReadinessFailed,
                    "VPS Ready", DiagnosticLevel.Information, correlation.ForStep("verify"), DiagnosticPhase.Verify,
                    result.Cancelled ? DiagnosticStatus.Cancelled : effective is ReadinessVerdict.Ready or ReadinessVerdict.ReadyWithWarnings
                        ? DiagnosticStatus.Succeeded : DiagnosticStatus.Warning,
                    $"Read-only inspection finished: {effective}. No server changes were made. REAL VPS: NOT TESTED.",
                    ErrorCode: result.ErrorCode?.ToStableCode(), Action: "CheckVpsReady", Verification: result.Verification), CancellationToken.None).ConfigureAwait(false);
            }
            catch { observed = WithExecution(observed, ReadinessExecution.DiagnosticsFailed); }
            lock (gate)
            {
                if (generation == capturedGeneration && session.Snapshot.SessionId == current.SessionId && !disposed) { snapshot = observed; }
            }
        }
        finally
        {
            lock (gate)
            {
                lastOperationId = ownOperation;
                ownOperation = null;
                checking = false;
                activeCancellation?.Dispose();
                activeCancellation = null;
            }
            RefreshPresentation();
        }
    }

    public bool IsCurrent(ReadinessActionTarget target) => target.SessionId == session.Snapshot.SessionId
        && target.Generation == Generation && Verdict is not (ReadinessVerdict.Stale or ReadinessVerdict.NotChecked or ReadinessVerdict.Checking);
    public string? GuidanceFor(ReadinessActionTarget target)
    {
        lock (gate)
        {
            if (!IsCurrent(target)) { return null; }
            var finding = snapshot?.Rows.FirstOrDefault(row => row.Id == target.CheckId);
            return finding is null ? null : finding.ReasonCode + ": " + ReadinessGuidance.For(finding.Reason);
        }
    }
    public void Invalidate()
    {
        lock (gate) { generation++; activeCancellation?.Cancel(); }
        RefreshPresentation();
    }
    public void Cancel() { lock (gate) { activeCancellation?.Cancel(); } }
    private long Generation { get { lock (gate) { return generation; } } }
    private void Progress(ReadinessCheckResult row, long expectedGeneration)
    {
        lock (gate) { if (!checking || generation != expectedGeneration || disposed) { return; } liveRows[row.Id] = row; }
        OnPropertyChanged(nameof(Rows));
    }
    private void SessionChanged(object? sender, EventArgs e)
    {
        var current = session.Snapshot;
        lock (gate)
        {
            if (current.SessionId != observedSessionId)
            {
                observedSessionId = current.SessionId;
                generation++;
                activeCancellation?.Cancel();
            }
            if (current.ActiveOperationId is { } operation && operation != ownOperation && operation != observedExternalOperation)
            {
                // Conservative invalidation before dispatch, including failed or
                // cancelled mutations and reboot reconnect with the same session ID.
                observedExternalOperation = operation;
                generation++;
                activeCancellation?.Cancel();
            }
            if (current.ActiveOperationId is null) { observedExternalOperation = null; }
        }
        RefreshPresentation();
    }
    private void Tick()
    {
        lock (gate)
        {
            if (disposed) { return; }
            var utc = clock.GetUtcNow();
            var timestamp = clock.GetTimestamp();
            var elapsed = clock.GetElapsedTime(lastTickTimestamp, timestamp);
            var wallGap = utc - lastTickUtc;
            // Wall time never proves freshness; discontinuity can only invalidate.
            if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromSeconds(5) || wallGap < TimeSpan.Zero
                || wallGap - elapsed > TimeSpan.FromSeconds(5)) { generation++; activeCancellation?.Cancel(); }
            lastTickUtc = utc;
            lastTickTimestamp = timestamp;
        }
        OnPropertyChanged(nameof(Verdict)); OnPropertyChanged(nameof(Status));
    }
    private void RefreshPresentation()
    {
        foreach (var property in new[] { nameof(CanCheck), nameof(IsChecking), nameof(CanCancel), nameof(IsDisconnected), nameof(CanExportEvidence), nameof(Verdict), nameof(Status), nameof(Rows), nameof(EvidenceTime), nameof(OperationId), nameof(RunId) })
        {
            OnPropertyChanged(property);
        }
    }
    private static ReadinessSnapshot WithExecution(ReadinessSnapshot value, ReadinessExecution execution) => new(value.SessionId,
        value.Generation, value.ProfileVersion, value.ObservedTimestamp, value.ObservedUtc, execution, value.Rows, value.UnsupportedPlatform);
    public void Dispose()
    {
        lock (gate) { if (disposed) { return; } disposed = true; generation++; activeCancellation?.Cancel(); }
        freshnessTimer.Dispose();
        session.StateChanged -= SessionChanged;
    }
}

public sealed class ReadinessRowViewModel
{
    private readonly ReadinessCheckDefinition definition;
    private readonly ReadinessCheckResult result;
    public ReadinessRowViewModel(ReadinessCheckDefinition definition, ReadinessCheckResult result, string sessionId, long generation,
        Action<ReadinessActionTarget> navigate)
    {
        this.definition = definition; this.result = result;
        NavigateCommand = new DelegateCommand(() => navigate(new(definition.Page, definition.Section, definition.Id, sessionId, generation)));
    }
    public bool Required => definition.Required;
    public string Title => definition.Title;
    public string Tag => Required ? "Required" : "Advisory";
    public ReadinessCheckState State => result.State;
    public string ReasonCode => result.ReasonCode;
    public string Reason => ReadinessGuidance.For(result.Reason);
    public string Evidence => $"{result.Source} · {(result.ObservedUtc is { } utc ? utc.ToString("O") : "Not observed")}" +
        (definition.Id == ReadinessCheckId.A02 ? $" · Cached upgrades: {result.CachedUpgradeCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}; upstream freshness NOT PROVED" : "");
    public string Explanation => $"{definition.Id} · {definition.Key}. {Reason} This profile is observational; navigation never changes configuration or grants consent.";
    public string NavigationLabel => $"Open {definition.Page} / {definition.Section}";
    public ICommand NavigateCommand { get; }
}

public static class ReadinessGuidance
{
    public static string For(ReadinessReason reason) => reason switch
    {
        ReadinessReason.InsufficientReadPrivilege => "Required reads did not prove privilege. Use a permitted account; do not change sudoers or reuse a key passphrase as a server password.",
        ReadinessReason.AmbiguousSshProtection => "SSH policy/source/family evidence is incomplete. Preserve narrow source rules; do not expand to Anywhere. A new login proves only the current route.",
        ReadinessReason.UnprotectedSshPort => "A necessary exact SSH allow was not found in the supported policy. Preserve the current access path and independently verify replacement access before changing rules.",
        ReadinessReason.CacheFreshnessUnknown => "Only existing package indexes were simulated. Zero cached upgrades does not prove current security updates. Refresh indexes only through the explicit System workflow.",
        ReadinessReason.CachedUpdatesAvailable => "Cached indexes report available upgrades; current upstream freshness is not proved. Review the System plan separately.",
        ReadinessReason.RootReadOnly => "Root is mounted read-only. Review storage outside this checklist; no disk repair or mount mutation is offered here.",
        ReadinessReason.DiskHeadroomLow or ReadinessReason.CapacityWarning => "Review root-disk capacity (required 1 GiB; advisory 2 GiB and 10%). No workload-fit or automatic cleanup claim is made.",
        ReadinessReason.TimeNotSynchronized or ReadinessReason.TimeSyncUnknown => "Actual clock synchronization is not proved. Service enablement alone is insufficient. This app does not automatically enable/configure time services.",
        ReadinessReason.FixtureUncovered => "This Ubuntu version is outside the 22.04/24.04 fixture set; not a claim of support expiry or real-host qualification.",
        ReadinessReason.PasswordModeAdvice => "Password mode is valid Core Basic access; prefer a separately verified key when appropriate. Password/MFA policy for the whole server is not assessed.",
        ReadinessReason.CredentialUnavailable => "The active authentication lease cannot open a fresh login. Reconnect explicitly; do not enable password fallback.",
        ReadinessReason.AuthenticationRejected => "A new authentication attempt was rejected. The surviving main channel is not evidence of fresh login; no automatic retry or fallback was performed.",
        ReadinessReason.HostTrustFailed => "The new connection requires host-identity review. Never silently accept a changed fingerprint.",
        ReadinessReason.RebootRequired => "Ubuntu reports a pending reboot. Use the separate explicit reboot workflow only after reconnect capability and safety prerequisites are verified.",
        ReadinessReason.PackageAuditProblems => "The complete package audit reported problems. Resolve package consistency through a reviewed workflow, not through this checklist.",
        ReadinessReason.NotRun => "Not checked yet. A missing observation never counts as Pass.",
        ReadinessReason.ObservedPass or ReadinessReason.TrustedSessionVerified or ReadinessReason.FreshLoginVerified
            or ReadinessReason.UfwActive or ReadinessReason.SshProtectionVerified or ReadinessReason.PackageAuditClean
            or ReadinessReason.DiskHeadroomPass or ReadinessReason.RebootNotRequired or ReadinessReason.KeyModeVerified
            or ReadinessReason.IdentityTimeObserved or ReadinessReason.TimeSynchronized or ReadinessReason.CapacityPass
            or ReadinessReason.FixtureCovered => "The specific current read-only observation passed. Manual/provider/workload policy is not assessed.",
        _ => "The observation could not be confirmed safely. Reconnect or explicitly recheck; review diagnostics using the operation ID. No server changes were made.",
    };
}
