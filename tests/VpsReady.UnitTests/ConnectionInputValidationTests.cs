using VpsReady.Application;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class ConnectionInputValidationTests
{
    [Fact]
    public void ValidInputUsesDefaultPortAndTimeoutWithoutStringifyingCredential()
    {
        var inputBuffer = new[] { 's', 'a', 'f', 'e', '-', '4', '8' };

        var result = ConnectionInputValidator.Validate(
            "2001:db8::48",
            null,
            "ubuntu",
            inputBuffer);

        var connection = Assert.IsType<ValidatedConnectionInput>(result.Connection);
        using (connection)
        {
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
            Assert.Equal("2001:db8::48", connection.Endpoint.Host);
            Assert.Equal(ConnectionInputValidator.DefaultPort, connection.Endpoint.Port);
            Assert.Equal("ubuntu", connection.Endpoint.UserName);
            Assert.Equal(ConnectionInputValidator.DefaultTimeout, connection.Timeout);
            Assert.Equal("[credential redacted]", connection.Password.ToString());
            Assert.DoesNotContain("safe-48", connection.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("safe-48", result.ToString(), StringComparison.Ordinal);
            Assert.False(connection.Password.IsCleared);
        }

        Assert.True(connection.Password.IsCleared);
    }

    [Theory]
    [InlineData("", "22", "ubuntu", ConnectionInputValidationError.HostRequiredOrInvalid)]
    [InlineData("bad\tserver", "22", "ubuntu", ConnectionInputValidationError.HostRequiredOrInvalid)]
    [InlineData("server.example", "0", "ubuntu", ConnectionInputValidationError.PortInvalid)]
    [InlineData("server.example", "65536", "ubuntu", ConnectionInputValidationError.PortInvalid)]
    [InlineData("server.example", "22x", "ubuntu", ConnectionInputValidationError.PortInvalid)]
    [InlineData("server.example", "22", "", ConnectionInputValidationError.UserNameRequiredOrInvalid)]
    [InlineData("server.example", "22", "ops\nuser", ConnectionInputValidationError.UserNameRequiredOrInvalid)]
    public void BlankControlAndInvalidFieldsFailBeforeCredentialHolderIsCreated(
        string host,
        string port,
        string user,
        ConnectionInputValidationError expectedError)
    {
        var inputBuffer = new[] { 's', 'a', 'f', 'e', '-', '4', '8' };

        var result = ConnectionInputValidator.Validate(host, port, user, inputBuffer);

        Assert.False(result.IsValid);
        Assert.Null(result.Connection);
        Assert.Contains(expectedError, result.Errors);
        Assert.DoesNotContain("safe-48", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BlankOrControlCredentialFailsClosedWithoutCreatingConnection()
    {
        var blank = ConnectionInputValidator.Validate("server.example", "22", "ubuntu", "   ".AsSpan());
        var control = ConnectionInputValidator.Validate("server.example", "22", "ubuntu", new[] { 'a', '\n' });

        Assert.False(blank.IsValid);
        Assert.Contains(ConnectionInputValidationError.PasswordRequiredOrInvalid, blank.Errors);
        Assert.Null(blank.Connection);
        Assert.False(control.IsValid);
        Assert.Contains(ConnectionInputValidationError.PasswordRequiredOrInvalid, control.Errors);
        Assert.Null(control.Connection);
    }

    [Fact]
    public void NonPositiveOrInfiniteTimeoutFailsClosed()
    {
        var inputBuffer = new[] { 's', 'a', 'f', 'e', '-', '4', '8' };

        var zero = ConnectionInputValidator.Validate("server.example", "22", "ubuntu", inputBuffer, TimeSpan.Zero);
        var negative = ConnectionInputValidator.Validate("server.example", "22", "ubuntu", inputBuffer, TimeSpan.FromSeconds(-1));
        var infinite = ConnectionInputValidator.Validate("server.example", "22", "ubuntu", inputBuffer, Timeout.InfiniteTimeSpan);

        Assert.All(new[] { zero, negative, infinite }, result =>
        {
            Assert.False(result.IsValid);
            Assert.Contains(ConnectionInputValidationError.TimeoutInvalid, result.Errors);
            Assert.Null(result.Connection);
        });
    }
}
