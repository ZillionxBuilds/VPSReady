using System.Text.Json;
using System.Text.RegularExpressions;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;

namespace VpsReady.ScenarioTests;

/// <summary>
/// Fail-closed redactor used by the test-only diagnostic recorder when the
/// production Infrastructure project is intentionally not referenced by the
/// scenario test project.  Unit tests exercise the production redactor itself.
/// </summary>
public sealed partial class ScenarioRedactor : IRedactor
{
    private const string Omitted = "PAYLOAD_OMITTED_BY_REDACTION_POLICY";
    private readonly List<string> sensitiveValues = [];
    private readonly Lock sync = new();

    public void RegisterSensitiveValue(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lock (sync)
            {
                sensitiveValues.Add(value);
            }
        }
    }

    public RedactionResult Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new RedactionResult(value, false);
        }

        lock (sync)
        {
            if (sensitiveValues.Any(sensitiveValue => value.Contains(sensitiveValue, StringComparison.Ordinal)))
            {
                return new RedactionResult(Omitted, true);
            }
        }

        if (PrivateKeyRegex().IsMatch(value) || SensitiveFieldRegex().IsMatch(value) || AuthorizationRegex().IsMatch(value))
        {
            return new RedactionResult(Omitted, true);
        }

        return new RedactionResult(TokenRegex().Replace(value, "[REDACTED_TOKEN]"), false);
    }

    [GeneratedRegex("-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex("\\b(password|passphrase|token|secret)\\s*[=:]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveFieldRegex();

    [GeneratedRegex("authorization\\s*:\\s*\\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex("\\b(?:ghp|github_pat|sk)-[A-Za-z0-9_-]{12,}\\b", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}

/// <summary>
/// In-memory diagnostic sink for scenario tests.  It applies the same redactor
/// before retaining Activity-like messages or JSONL-like records.
/// </summary>
public sealed class ScenarioDiagnosticRecorder(IRedactor redactor) : IDiagnosticSink
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

    public async Task WriteAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        cancellationToken.ThrowIfCancellationRequested();
        var safeMessage = redactor.Redact(diagnosticEvent.Message);
        var safeEvent = diagnosticEvent with { Message = safeMessage.SafeText };
        lock (sync)
        {
            events.Add(safeEvent);
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
    private readonly ScenarioDiagnosticRecorder diagnostics;
    private readonly string runId;

    public ScenarioOperationRunner(
        ScenarioHostState state,
        ScenarioFaultPlan faults,
        ScenarioDiagnosticRecorder diagnostics,
        string? runId = null)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.faults = faults ?? throw new ArgumentNullException(nameof(faults));
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

        var context = new ScenarioOperationContext(state.ScenarioId, runId, operationId, state, faults, diagnostics);
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
            Events: diagnostics.Events);
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
            Events: diagnostics.Events);
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
                $"scenario.operation.{phase.ToString().ToLowerInvariant()}",
                "Scenario",
                level,
                correlation,
                phase,
                status,
                message),
            cancellationToken);
    }

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
