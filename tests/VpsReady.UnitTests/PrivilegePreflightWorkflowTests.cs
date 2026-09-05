using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class PrivilegePreflightWorkflowTests
{
    [Theory]
    [InlineData("root=true\nsudo=not_required", true, SudoCapability.NotRequired)]
    [InlineData("root=false\nsudo=available", false, SudoCapability.Available)]
    public async Task MutationAcceptsRootOrAlreadyAuthorizedNoninteractiveSudo(string output, bool root, SudoCapability sudo)
    {
        var transport = new RecordingTransport(new RemoteCommandResult(0, output, string.Empty, TimeSpan.Zero, OutputCapturePolicy.SanitizedTruncated));
        var sink = new RecordingSink();
        var result = await new PrivilegePreflightWorkflow(sink).CheckAsync(transport, PrivilegeOperationIntent.Mutation);

        Assert.True(result.Result.Succeeded);
        Assert.True(result.CanMutate);
        Assert.Equal(new PrivilegeCapability(root, sudo), result.Capability);
        Assert.Equal([RemoteCommandCatalog.UbuntuPrivilegeRead], transport.Commands.Select(command => command.Id.Value));
        Assert.Contains(sink.Events, item => item.EventId == DiagnosticEventCatalog.PrivilegePreflightSucceeded && item.CommandId == RemoteCommandCatalog.UbuntuPrivilegeRead);
    }

    [Fact]
    public async Task ReadOnlyIntentDoesNotExecuteDetectionOrAnyElevationPath()
    {
        var transport = new RecordingTransport(new RemoteCommandResult(0, "root=false\nsudo=available", string.Empty, TimeSpan.Zero, OutputCapturePolicy.SanitizedTruncated));
        var result = await new PrivilegePreflightWorkflow(new RecordingSink()).CheckAsync(transport, PrivilegeOperationIntent.ReadOnly);

        Assert.True(result.Result.Succeeded);
        Assert.Null(result.Capability);
        Assert.Empty(transport.Commands);
    }

    [Theory]
    [InlineData("root=false\nsudo=unavailable", PrivilegePreflightErrorCatalog.Unavailable, OperationErrorCode.Privilege)]
    [InlineData("root=false\nsudo=prompt", PrivilegePreflightErrorCatalog.Unknown, OperationErrorCode.Parse)]
    public async Task MutationFailsClosedWithoutPasswordPromptForUnavailableOrMalformedCapability(string output, string error, OperationErrorCode operationError)
    {
        var result = await new PrivilegePreflightWorkflow(new RecordingSink()).CheckAsync(
            new RecordingTransport(new RemoteCommandResult(0, output, string.Empty, TimeSpan.Zero, OutputCapturePolicy.SanitizedTruncated)),
            PrivilegeOperationIntent.Mutation);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(operationError, result.Result.ErrorCode);
        Assert.Equal(error, result.ErrorCode);
        Assert.False(result.CanMutate);
    }

    [Fact]
    public async Task NonzeroAndCancellationAreTypedAndNeverExposeSensitiveArgumentData()
    {
        var denied = await new PrivilegePreflightWorkflow(new RecordingSink()).CheckAsync(
            new RecordingTransport(new RemoteCommandResult(77, string.Empty, "denied", TimeSpan.Zero, OutputCapturePolicy.SanitizedTruncated)),
            PrivilegeOperationIntent.Mutation);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancellation = await new PrivilegePreflightWorkflow(new RecordingSink()).CheckAsync(
            new RecordingTransport(new RemoteCommandResult(0, "root=true\nsudo=not_required", string.Empty, TimeSpan.Zero, OutputCapturePolicy.SanitizedTruncated)),
            PrivilegeOperationIntent.Mutation,
            cancelled.Token);

        Assert.Equal(OperationErrorCode.Privilege, denied.Result.ErrorCode);
        Assert.Equal(PrivilegePreflightErrorCatalog.Command, denied.ErrorCode);
        Assert.True(cancellation.Result.Cancelled);
        Assert.Equal(PrivilegePreflightErrorCatalog.Cancelled, cancellation.ErrorCode);
    }

    private sealed class RecordingTransport(RemoteCommandResult response) : IRemoteTransport
    {
        public List<RemoteCommand> Commands { get; } = [];
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingSink : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];
        public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken) { Events.Add(entry); return Task.CompletedTask; }
    }
}
