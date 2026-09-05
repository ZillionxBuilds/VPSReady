using System.Security.Cryptography;
using System.Runtime.InteropServices;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Local;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class PublicKeyDeploymentWorkflowTests
{
    [Fact]
    public async Task DeploysValidatedKeyWithSafeCataloguedCommandsAndFreshVerification()
    {
        await using var workspace = new KeyWorkspace();
        var key = await CreateMaterialAsync(workspace);
        var diagnostics = new CollectingDiagnosticSink();
        var transport = new RecordingDeploymentTransport(alreadyPresent: false);

        var result = await new PublicKeyDeploymentWorkflow(diagnostics).DeployAsync(transport, key);

        Assert.True(result.Result.Succeeded);
        Assert.False(result.AlreadyPresent);
        Assert.Null(result.DeploymentErrorCode);
        Assert.Equal(
            [RemoteCommandCatalog.UbuntuAuthorizedKeysInspect, RemoteCommandCatalog.UbuntuAuthorizedKeysInstall, RemoteCommandCatalog.UbuntuAuthorizedKeysVerify],
            transport.Commands.Select(command => command.Id.Value));
        Assert.All(transport.Commands, command =>
        {
            Assert.StartsWith("fingerprint=SHA256:", command.SafeArgumentSummary, StringComparison.Ordinal);
            Assert.DoesNotContain("ssh-ed25519", command.SafeArgumentSummary, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(diagnostics.Events, item => item.Message.Contains("ssh-ed25519", StringComparison.Ordinal));
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.PublicKeyDeploymentSucceeded && item.Phase == DiagnosticPhase.Verify);
        Assert.Throws<InvalidOperationException>(() => _ = key.Length);
    }

    [Fact]
    public async Task ExistingKeyStillRepairsAndVerifiesWithoutAddingASecondEntry()
    {
        await using var workspace = new KeyWorkspace();
        var key = await CreateMaterialAsync(workspace);
        var transport = new RecordingDeploymentTransport(alreadyPresent: true);

        var result = await new PublicKeyDeploymentWorkflow(new CollectingDiagnosticSink()).DeployAsync(transport, key);

        Assert.True(result.Result.Succeeded);
        Assert.True(result.AlreadyPresent);
        Assert.Equal(3, transport.Commands.Count);
        Assert.Equal(RemoteCommandCatalog.UbuntuAuthorizedKeysInstall, transport.Commands[1].Id.Value);
    }

    [Fact]
    public async Task VerificationFailureNeverReturnsSuccessAndPerformsReadOnlyRecovery()
    {
        await using var workspace = new KeyWorkspace();
        var key = await CreateMaterialAsync(workspace);
        var transport = new RecordingDeploymentTransport(alreadyPresent: false, failFirstVerify: true);

        var result = await new PublicKeyDeploymentWorkflow(new CollectingDiagnosticSink()).DeployAsync(transport, key);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(OperationErrorCode.Verification, result.Result.ErrorCode);
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.Equal(PublicKeyDeploymentErrorCatalog.Verification, result.DeploymentErrorCode);
        Assert.Equal(
            [DiagnosticPhase.Preflight, DiagnosticPhase.Apply, DiagnosticPhase.Verify, DiagnosticPhase.Recovery],
            transport.Phases);
        Assert.Equal(RemoteCommandCatalog.UbuntuAuthorizedKeysVerify, transport.Commands[^1].Id.Value);
    }

    [Fact]
    public async Task InvalidMaterialFailsBeforeTransportAndIsCleared()
    {
        var characters = "not-an-openssh-public-key".ToCharArray();
        var key = new PublicKeyDeploymentMaterial(characters);
        Array.Clear(characters);
        var transport = new RecordingDeploymentTransport(alreadyPresent: false);

        var result = await new PublicKeyDeploymentWorkflow(new CollectingDiagnosticSink()).DeployAsync(transport, key);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(OperationErrorCode.Validation, result.Result.ErrorCode);
        Assert.Equal(PublicKeyDeploymentErrorCatalog.InvalidInput, result.DeploymentErrorCode);
        Assert.Empty(transport.Commands);
        Assert.Throws<InvalidOperationException>(() => _ = key.Length);
    }

    [Fact]
    public void CatalogRejectsNonCanonicalPayloadAndUnknownCommand()
    {
        var command = new RemoteCommand(
            RemoteCommandCatalog.RequireKnown(RemoteCommandCatalog.UbuntuAuthorizedKeysInspect),
            "fingerprint=SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            TimeSpan.FromSeconds(1),
            OutputCapturePolicy.SanitizedTruncated,
            64);

        Assert.Throws<ArgumentException>(() => UbuntuAuthorizedKeysCommandCatalog.RequireShellCommand(command, "ssh-ed25519 value with-comment".AsSpan()));
        Assert.Throws<ArgumentOutOfRangeException>(() => RemoteCommandCatalog.RequireKnown("ubuntu.ssh.authorized-keys.unknown"));
    }

    private static async Task<PublicKeyDeploymentMaterial> CreateMaterialAsync(KeyWorkspace workspace)
    {
        var generated = await new Ed25519OpenSshKeyPairGenerator(new CollectingDiagnosticSink()).GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);
        Assert.True(generated.Succeeded, generated.GenerationErrorCode);
        var text = await File.ReadAllTextAsync(workspace.PublicKeyPath);
        var characters = text.ToCharArray();
        try
        {
            return new PublicKeyDeploymentMaterial(characters);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(characters.AsSpan()));
        }
    }

    private sealed class RecordingDeploymentTransport(bool alreadyPresent, bool failFirstVerify = false) : IPublicKeyDeploymentTransport
    {
        private bool verifyFailed;

        public List<RemoteCommand> Commands { get; } = [];

        public List<DiagnosticPhase> Phases { get; } = [];

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException("C404 must use the narrow public-key deployment boundary.");

        public Task<RemoteCommandResult> ExecutePublicKeyDeploymentAsync(RemoteCommand command, ReadOnlyMemory<char> canonicalPublicKey, DiagnosticPhase phase, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            Phases.Add(phase);
            Assert.False(canonicalPublicKey.IsEmpty);
            var result = command.Id.Value switch
            {
                RemoteCommandCatalog.UbuntuAuthorizedKeysInspect => new RemoteCommandResult(0, alreadyPresent ? "present=true" : "present=false", string.Empty, TimeSpan.Zero, command.OutputCapturePolicy),
                RemoteCommandCatalog.UbuntuAuthorizedKeysInstall => new RemoteCommandResult(0, string.Empty, string.Empty, TimeSpan.Zero, command.OutputCapturePolicy),
                RemoteCommandCatalog.UbuntuAuthorizedKeysVerify when failFirstVerify && !verifyFailed => FailVerify(command),
                RemoteCommandCatalog.UbuntuAuthorizedKeysVerify => new RemoteCommandResult(0, string.Empty, string.Empty, TimeSpan.Zero, command.OutputCapturePolicy),
                _ => throw new InvalidOperationException("Unknown command must fail loudly."),
            };
            return Task.FromResult(result);
        }

        private RemoteCommandResult FailVerify(RemoteCommand command)
        {
            verifyFailed = true;
            return new RemoteCommandResult(4, string.Empty, string.Empty, TimeSpan.Zero, command.OutputCapturePolicy);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
