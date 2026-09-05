using System.Text.Json;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;

namespace VpsReady.ScenarioTests;

/// <summary>
/// In-memory sanitized sink for scenario tests. The shared production
/// RedactingDiagnosticSink is the only ingress in scenario composition.
/// </summary>
public sealed class ScenarioDiagnosticRecorder : ISanitizedDiagnosticSink
{
    private readonly List<StructuredDiagnosticEvent> events = [];
    private readonly Lock sync = new();

    public IReadOnlyList<StructuredDiagnosticEvent> Events
    {
        get
        {
            lock (sync)
            {
                return events.ToArray();
            }
        }
    }

    public IReadOnlyList<string> ActivityMessages => Events.Select(diagnosticEvent => diagnosticEvent.Message).ToArray();

    public async Task WriteSanitizedAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            events.Add(diagnosticEvent);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public string ToJsonLines()
    {
        return string.Join(
            Environment.NewLine,
            Events.Select(diagnosticEvent => JsonSerializer.Serialize(diagnosticEvent)));
    }
}

public sealed record ScenarioOperationContext(
    string ScenarioId,
    string RunId,
    string OperationId,
    ScenarioHostState State,
    ScenarioFaultPlan Faults,
    ScenarioDiagnosticRecorder Diagnostics);

public sealed record ScenarioOperationResult(
    OperationResult Result,
    DiagnosticPhase? FailurePhase,
    bool VerificationCompleted,
    bool RecoveryAttempted,
    bool RecoverySucceeded,
    IReadOnlyList<StructuredDiagnosticEvent> Events)
{
    public string OperationId => Result.OperationId;

    public bool Succeeded => Result.Succeeded;

    public bool Cancelled => Result.Cancelled;

    public string? ErrorCode => Result.ErrorCode?.ToStableCode();
}

/// <summary>
/// Small test-only pipeline that enforces validate -> preflight -> plan -> apply
/// -> verify and makes recovery/fault behavior observable.  It is intentionally
/// not used by the production composition root.
/// </summary>
public sealed class ScenarioOperationRunner
{
    private readonly ScenarioHostState state;
    private readonly ScenarioFaultPlan faults;
    private readonly ScenarioDiagnosticRecorder recorder;
    private readonly IDiagnosticSink diagnostics;
    private readonly string runId;

    public ScenarioOperationRunner(
        ScenarioHostState state,
        ScenarioFaultPlan faults,
        ScenarioDiagnosticRecorder recorder,
        IDiagnosticSink diagnostics,
        string? runId = null)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.faults = faults ?? throw new ArgumentNullException(nameof(faults));
        this.recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        this.runId = string.IsNullOrWhiteSpace(runId) ? $"run-{state.ScenarioId}" : runId;
    }

    public async Task<ScenarioOperationResult> RunAsync(
        string operationId,
        Func<ScenarioOperationContext, CancellationToken, Task>? validate = null,
        Func<ScenarioOperationContext, CancellationToken, Task>? preflight = null,
        Func<ScenarioOperationContext, CancellationToken, Task>? plan = null,
        Func<ScenarioOperationContext, CancellationToken, Task>? apply = null,
        Func<ScenarioOperationContext, CancellationToken, Task>? verify = null,
        Func<ScenarioOperationContext, CancellationToken, Task>? recovery = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            throw new ArgumentException("A stable operation ID is required.", nameof(operationId));
        }

        var context = new ScenarioOperationContext(state.ScenarioId, runId, operationId, state, faults, recorder);
        var steps = new (DiagnosticPhase Phase, Func<ScenarioOperationContext, CancellationToken, Task>? Action)[]
        {
            (DiagnosticPhase.Validate, validate),
            (DiagnosticPhase.Preflight, preflight),
            (DiagnosticPhase.Plan, plan),
            (DiagnosticPhase.Apply, apply),
            (DiagnosticPhase.Verify, verify),
        };

        var verificationCompleted = false;
        foreach (var (phase, action) in steps)
        {
            if (phase == DiagnosticPhase.Verify && action is null)
            {
                return await FailAsync(
                    context,
                    phase,
                    new InvalidOperationException("A verification step is required before an operation can succeed."),
                    recovery,
                    verificationCompleted,
                    cancellationToken).ConfigureAwait(false);
            }

            var failure = await RunPhaseAsync(context, phase, action, cancellationToken).ConfigureAwait(false);
            if (failure is null)
            {
                if (phase == DiagnosticPhase.Verify)
                {
                    verificationCompleted = true;
                }

                continue;
            }

            return await FailAsync(context, phase, failure, recovery, verificationCompleted, cancellationToken).ConfigureAwait(false);
        }

        return new ScenarioOperationResult(
            OperationResult.Success(operationId, OperationState.Unchanged),
            FailurePhase: null,
            VerificationCompleted: verificationCompleted,
            RecoveryAttempted: false,
            RecoverySucceeded: false,
            Events: recorder.Events);
    }

    private async Task<Exception?> RunPhaseAsync(
        ScenarioOperationContext context,
        DiagnosticPhase phase,
        Func<ScenarioOperationContext, CancellationToken, Task>? action,
        CancellationToken cancellationToken)
    {
        await WriteEventAsync(context, phase, DiagnosticStatus.Started, DiagnosticLevel.Information, $"{phase} started", cancellationToken).ConfigureAwait(false);
        try
        {
            if (faults.TryTake(phase, null, out var fault) && fault is not null)
            {
                throw ExceptionFor(fault);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (action is not null)
            {
                await action(context, cancellationToken).ConfigureAwait(false);
            }

            await WriteEventAsync(context, phase, DiagnosticStatus.Succeeded, DiagnosticLevel.Information, $"{phase} succeeded", cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException exception)
        {
            await WriteEventAsync(context, phase, DiagnosticStatus.Cancelled, DiagnosticLevel.Warning, $"{phase} cancelled", CancellationToken.None).ConfigureAwait(false);
            return exception;
        }
        catch (Exception exception)
        {
            await WriteEventAsync(context, phase, DiagnosticStatus.Failed, DiagnosticLevel.Error, $"{phase} failed: {SafeExceptionMessage(exception)}", CancellationToken.None).ConfigureAwait(false);
            return exception;
        }
    }

    private async Task<ScenarioOperationResult> FailAsync(
        ScenarioOperationContext context,
        DiagnosticPhase phase,
        Exception failure,
        Func<ScenarioOperationContext, CancellationToken, Task>? recovery,
        bool verificationCompleted,
        CancellationToken cancellationToken)
    {
        var recoveryAttempted = false;
        var recoverySucceeded = false;

        await WriteEventAsync(
            context,
            phase,
            failure is OperationCanceledException ? DiagnosticStatus.Cancelled : DiagnosticStatus.RecoveryRequired,
            failure is OperationCanceledException ? DiagnosticLevel.Warning : DiagnosticLevel.Error,
            failure is OperationCanceledException ? $"{phase} cancelled" : $"{phase} requires recovery",
            CancellationToken.None).ConfigureAwait(false);

        if (failure is not OperationCanceledException && recovery is not null)
        {
            recoveryAttempted = true;
            recoverySucceeded = await RunPhaseAsync(context, DiagnosticPhase.Recovery, recovery, cancellationToken).ConfigureAwait(false) is null;
        }

        var state = StateFor(phase);
        var result = failure is OperationCanceledException
            ? OperationResult.Cancellation(context.OperationId, state)
            : OperationResult.Failure(
                context.OperationId,
                recoveryAttempted && !recoverySucceeded ? OperationErrorCode.Recovery : ErrorCodeFor(failure),
                state,
                phase == DiagnosticPhase.Verify ? OperationVerification.Failed : OperationVerification.NotRun,
                RecoveryFor(recoveryAttempted, recoverySucceeded));

        return new ScenarioOperationResult(
            result,
            FailurePhase: phase,
            VerificationCompleted: verificationCompleted,
            RecoveryAttempted: recoveryAttempted,
            RecoverySucceeded: recoverySucceeded,
            Events: recorder.Events);
    }

    private Task WriteEventAsync(
        ScenarioOperationContext context,
        DiagnosticPhase phase,
        DiagnosticStatus status,
        DiagnosticLevel level,
        string message,
        CancellationToken cancellationToken)
    {
        var correlation = new CorrelationIds(
            $"session-{context.ScenarioId}",
            context.RunId,
            context.OperationId,
            phase.ToString().ToLowerInvariant());
        return diagnostics.WriteAsync(
            new StructuredDiagnosticEvent(
                EventIdFor(status),
                "Scenario",
                level,
                correlation,
                phase,
                status,
                message),
            cancellationToken);
    }

    private static string EventIdFor(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Started => DiagnosticEventCatalog.OperationStarted,
        DiagnosticStatus.Running => DiagnosticEventCatalog.OperationRunning,
        DiagnosticStatus.Succeeded => DiagnosticEventCatalog.OperationSucceeded,
        DiagnosticStatus.Warning => DiagnosticEventCatalog.OperationWarning,
        DiagnosticStatus.Failed => DiagnosticEventCatalog.OperationFailed,
        DiagnosticStatus.Cancelled => DiagnosticEventCatalog.OperationCancelled,
        DiagnosticStatus.RecoveryRequired => DiagnosticEventCatalog.OperationRecoveryRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown diagnostic status."),
    };

    private static string SafeExceptionMessage(Exception exception)
    {
        return exception switch
        {
            ScenarioFaultException => "scenario fault",
            TimeoutException => "timeout",
            ScenarioDisconnectException => "disconnect",
            _ => "operation failure",
        };
    }

    private static OperationErrorCode ErrorCodeFor(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => OperationErrorCode.Cancelled,
            TimeoutException => OperationErrorCode.Timeout,
            ScenarioDisconnectException => OperationErrorCode.Network,
            ScenarioFaultException fault when fault.Fault.Kind == ScenarioFaultKind.PermissionDenied => OperationErrorCode.Privilege,
            ScenarioFaultException fault when fault.Fault.Kind == ScenarioFaultKind.NonZeroExit => OperationErrorCode.Command,
            ScenarioFaultException fault when fault.Fault.Kind == ScenarioFaultKind.MalformedOutput => OperationErrorCode.Parse,
            ScenarioFaultException fault when fault.Fault.Kind == ScenarioFaultKind.VerificationMismatch => OperationErrorCode.Verification,
            ScenarioFaultException => OperationErrorCode.Unexpected,
            _ => OperationErrorCode.Unexpected,
        };
    }

    private static OperationState StateFor(DiagnosticPhase phase) => phase switch
    {
        DiagnosticPhase.Validate or DiagnosticPhase.Preflight or DiagnosticPhase.Plan => OperationState.Unchanged,
        _ => OperationState.PartiallyApplied,
    };

    private static OperationRecovery RecoveryFor(bool attempted, bool succeeded) => attempted
        ? succeeded ? OperationRecovery.Succeeded : OperationRecovery.Failed
        : OperationRecovery.NotRequired;

    private static Exception ExceptionFor(ScenarioFault fault) => fault.Kind switch
    {
        ScenarioFaultKind.Cancellation => new OperationCanceledException(),
        ScenarioFaultKind.Timeout => new TimeoutException(),
        ScenarioFaultKind.Disconnect => new ScenarioDisconnectException("Injected deterministic scenario disconnect."),
        _ => new ScenarioFaultException(fault),
    };
}
