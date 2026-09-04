using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.ScenarioTests;

/// <summary>
/// Deterministic, one-shot fault queue.  A command may select a fault by phase
/// and optionally command ID; an unqualified fault applies to the next command
/// or operation step in that phase.
/// </summary>
public sealed class ScenarioFaultPlan
{
    private readonly List<ScenarioFault> faults = [];
    private readonly Lock sync = new();

    public IReadOnlyList<ScenarioFault> Pending
    {
        get
        {
            lock (sync)
            {
                return faults.ToArray();
            }
        }
    }

    public void Inject(ScenarioFault fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        if (string.IsNullOrWhiteSpace(fault.FaultId))
        {
            throw new ArgumentException("A stable fault ID is required.", nameof(fault));
        }

        lock (sync)
        {
            faults.Add(fault);
        }
    }

    public void Inject(
        DiagnosticPhase phase,
        ScenarioFaultKind kind,
        string faultId,
        string? commandId = null,
        int exitCode = 1,
        string standardError = "Injected deterministic scenario fault.",
        TimeSpan delay = default)
    {
        Inject(new ScenarioFault(phase, faultId, kind, commandId, exitCode, standardError, delay));
    }

    public bool TryTake(DiagnosticPhase phase, string? commandId, out ScenarioFault? fault)
    {
        lock (sync)
        {
            var index = faults.FindIndex(candidate =>
                candidate.Phase == phase
                && (candidate.CommandId is null || string.Equals(candidate.CommandId, commandId, StringComparison.Ordinal)));
            if (index < 0)
            {
                fault = null;
                return false;
            }

            fault = faults[index];
            faults.RemoveAt(index);
            return true;
        }
    }

    public void ThrowIfInjected(DiagnosticPhase phase, string operationId, string? commandId = null)
    {
        if (!TryTake(phase, commandId, out var fault) || fault is null)
        {
            return;
        }

        throw new ScenarioFaultException(fault with { CommandId = commandId ?? fault.CommandId });
    }

    public static async Task<RemoteCommandResult?> ApplyToCommandAsync(
        ScenarioFault fault,
        RemoteCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (fault.Delay > TimeSpan.Zero)
        {
            if (fault.Delay > command.Timeout)
            {
                throw new TimeoutException($"Injected timeout '{fault.FaultId}' exceeded the command timeout.");
            }

            await Task.Delay(fault.Delay, cancellationToken).ConfigureAwait(false);
        }

        return fault.Kind switch
        {
            ScenarioFaultKind.Throw => throw new ScenarioFaultException(fault),
            ScenarioFaultKind.Timeout => throw new TimeoutException($"Injected timeout '{fault.FaultId}'."),
            ScenarioFaultKind.Cancellation => throw new OperationCanceledException($"Injected cancellation '{fault.FaultId}'.", cancellationToken),
            ScenarioFaultKind.Disconnect => throw new ScenarioDisconnectException($"Injected disconnect '{fault.FaultId}'."),
            ScenarioFaultKind.PermissionDenied => new RemoteCommandResult(13, string.Empty, "Permission denied (injected scenario fault).", fault.Delay),
            ScenarioFaultKind.NonZeroExit => new RemoteCommandResult(fault.ExitCode == 0 ? 1 : fault.ExitCode, string.Empty, fault.StandardError, fault.Delay),
            ScenarioFaultKind.MalformedOutput => new RemoteCommandResult(0, "<malformed scenario output>", fault.StandardError, fault.Delay),
            ScenarioFaultKind.VerificationMismatch => new RemoteCommandResult(4, "verification=mismatch", fault.StandardError, fault.Delay),
            _ => throw new ArgumentOutOfRangeException(nameof(fault), fault.Kind, "Unknown scenario fault kind."),
        };
    }
}
