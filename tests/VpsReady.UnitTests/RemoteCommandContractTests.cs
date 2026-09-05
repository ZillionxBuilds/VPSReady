using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class RemoteCommandContractTests
{
    [Fact]
    public void CommandRequestCarriesFiniteTimeoutCapturePolicyAndCanonicalSafeMetadata()
    {
        var command = RemoteCommand.Create(
            RemoteCommandCatalog.RequireKnown(RemoteCommandCatalog.SshConnectionTest),
            [new("port", "22"), new("action", "probe")],
            TimeSpan.FromSeconds(15),
            OutputCapturePolicy.SanitizedTruncated);

        Assert.Equal("action=probe port=22", command.SafeArgumentSummary);
        Assert.Equal(TimeSpan.FromSeconds(15), command.Timeout);
        Assert.Equal(OutputCapturePolicy.SanitizedTruncated, command.OutputCapturePolicy);
        Assert.True(RemoteCommandCatalog.IsKnown(command.Id.Value));
        Assert.True(DiagnosticCommandCatalog.IsKnown(command.Id.Value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CommandRejectsNonFiniteOrNonPositiveTimeout(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RemoteCommand(
            new RemoteCommandId("ssh.connection.test"),
            "action=probe",
            TimeSpan.FromMilliseconds(milliseconds)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RemoteCommand(
            new RemoteCommandId("ssh.connection.test"),
            "action=probe",
            Timeout.InfiniteTimeSpan));
    }

    [Fact]
    public void ValidationRejectsCredentialBearingMetadataAndUnsafeShellInput()
    {
        var restrictedSummary = string.Concat("to", "ken", "=", "not", "-", "safe");

        Assert.Throws<ArgumentException>(() => RemoteCommandArguments.FormatSafeSummary([new("password", "not-safe")]));
        Assert.Throws<ArgumentException>(() => new RemoteCommand(
            new RemoteCommandId("ssh.connection.test"),
            restrictedSummary,
            TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => RemoteCommandArguments.QuotePosixArgument("line-one\nline-two"));
        Assert.Equal("'O'\"'\"'Brien'", RemoteCommandArguments.QuotePosixArgument("O'Brien"));
    }

    [Fact]
    public void ZeroExitIsTransportCompletionNotVerifiedOperationSuccess()
    {
        var commandResult = new RemoteCommandResult(0, "completed", string.Empty, TimeSpan.FromMilliseconds(3));
        var operation = OperationResult.Failure(
            "connection.probe",
            OperationErrorCode.Verification,
            OperationState.Unknown,
            OperationVerification.Failed);

        Assert.True(commandResult.Succeeded);
        Assert.False(operation.Succeeded);
        Assert.Equal(OperationVerification.Failed, operation.Verification);
    }
}
