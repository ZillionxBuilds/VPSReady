using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Local;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class PublicKeyDeploymentWorkflowScenarioTests
{
    [Fact]
    public async Task DeploymentPreservesExistingEntriesRepairsPermissionsAndIsIdempotent()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.public-key-deployment");
        var state = services.GetRequiredService<ScenarioHostState>();
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var workflow = new PublicKeyDeploymentWorkflow(services.GetRequiredService<IDiagnosticSink>());
        var path = $"/home/{state.Ssh.UserName}/.ssh/authorized_keys";
        var directory = $"/home/{state.Ssh.UserName}/.ssh";
        state.RemoteFiles.Files[path] = state.RemoteFiles.Files[path] with { Contents = "legacy-entry\n", Owner = "wrong", Group = "wrong", Permissions = "0644" };
        state.RemoteFiles.Files[directory] = state.RemoteFiles.Files[directory] with { Owner = "wrong", Group = "wrong", Permissions = "0755" };

        var characters = await CreatePublicKeyCharactersAsync();
        try
        {
            using var key = new PublicKeyDeploymentMaterial(characters);
            var first = await workflow.DeployAsync(host, key);

            Assert.True(first.Result.Succeeded);
            Assert.False(first.AlreadyPresent);
            Assert.StartsWith("legacy-entry", state.RemoteFiles.Files[path].Contents, StringComparison.Ordinal);
            Assert.Equal("0700", state.RemoteFiles.Files[directory].Permissions);
            Assert.Equal("0600", state.RemoteFiles.Files[path].Permissions);
            Assert.Equal(state.Ssh.UserName, state.RemoteFiles.Files[path].Owner);

            using var retry = new PublicKeyDeploymentMaterial(characters);
            var second = await workflow.DeployAsync(host, retry);
            Assert.True(second.Result.Succeeded);
            Assert.True(second.AlreadyPresent);
            Assert.Single(state.Ssh.AuthorizedKeyFingerprints);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(characters.AsSpan()));
        }
    }

    [Fact]
    public async Task VerifyFaultPreservesAccessAndReturnsReadOnlyRecoveryNotSuccess()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.public-key-verify-fault");
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var faults = services.GetRequiredService<ScenarioFaultPlan>();
        faults.Inject(DiagnosticPhase.Verify, ScenarioFaultKind.VerificationMismatch, "deployment-verify", RemoteCommandCatalog.UbuntuAuthorizedKeysVerify);
        using var key = await CreateMaterialAsync();

        var result = await new PublicKeyDeploymentWorkflow(services.GetRequiredService<IDiagnosticSink>()).DeployAsync(host, key);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(OperationErrorCode.Verification, result.Result.ErrorCode);
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.NotEmpty(host.State.Ssh.AuthorizedKeyFingerprints);
    }

    [Theory]
    [InlineData(DiagnosticPhase.Preflight, ScenarioFaultKind.NonZeroExit, OperationState.Unchanged)]
    [InlineData(DiagnosticPhase.Apply, ScenarioFaultKind.PermissionDenied, OperationState.PartiallyApplied)]
    public async Task PhaseFaultsFailClosedWithoutFalseSuccess(DiagnosticPhase phase, ScenarioFaultKind faultKind, OperationState expectedState)
    {
        await using var services = ScenarioComposition.Create($"scenario.e2.public-key-{phase.ToString().ToLowerInvariant()}");
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        services.GetRequiredService<ScenarioFaultPlan>().Inject(phase, faultKind, $"deployment-{phase}");
        using var key = await CreateMaterialAsync();

        var result = await new PublicKeyDeploymentWorkflow(services.GetRequiredService<IDiagnosticSink>()).DeployAsync(host, key);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(expectedState, result.Result.State);
        Assert.DoesNotContain("key", host.State.Ssh.LastAuthenticationMethod ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancellationClearsPayloadAndDoesNotMutateOrReportSuccess()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.public-key-cancel");
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        using var key = await CreateMaterialAsync();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var result = await new PublicKeyDeploymentWorkflow(services.GetRequiredService<IDiagnosticSink>()).DeployAsync(host, key, cancelled.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Empty(host.State.Ssh.AuthorizedKeyFingerprints);
        Assert.Throws<InvalidOperationException>(() => _ = key.Length);
    }

    [Fact]
    public async Task ApplyDisconnectTriggersReadOnlyRecoveryAndNeverReportsSuccess()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.public-key-disconnect");
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        services.GetRequiredService<ScenarioFaultPlan>().Inject(DiagnosticPhase.Apply, ScenarioFaultKind.Disconnect, "deployment-disconnect", RemoteCommandCatalog.UbuntuAuthorizedKeysInstall);
        using var key = await CreateMaterialAsync();

        var result = await new PublicKeyDeploymentWorkflow(services.GetRequiredService<IDiagnosticSink>()).DeployAsync(host, key);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(OperationState.PartiallyApplied, result.Result.State);
        Assert.Equal(OperationRecovery.Failed, result.Result.Recovery);
        Assert.Equal(PublicKeyDeploymentErrorCatalog.Recovery, result.DeploymentErrorCode);
    }

    [Fact]
    public async Task DiagnosticsContainOnlySafeIdentifiersAndUnknownCommandsFailLoudly()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.public-key-diagnostics");
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        using var key = await CreateMaterialAsync();

        var result = await new PublicKeyDeploymentWorkflow(services.GetRequiredService<IDiagnosticSink>()).DeployAsync(host, key);

        Assert.True(result.Result.Succeeded);
        Assert.Contains(recorder.Events, item => item.EventId == DiagnosticEventCatalog.PublicKeyDeploymentStarted);
        Assert.Contains(recorder.Events, item => item.EventId == DiagnosticEventCatalog.PublicKeyDeploymentSucceeded);
        Assert.All(recorder.Events, item => Assert.DoesNotContain("ssh-ed25519", item.Message, StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.ExecuteAsync(new RemoteCommand(new RemoteCommandId("scenario.public-key.unknown"), string.Empty, TimeSpan.FromSeconds(1)), CancellationToken.None));
    }

    private static async Task<PublicKeyDeploymentMaterial> CreateMaterialAsync()
    {
        var characters = await CreatePublicKeyCharactersAsync();
        try
        {
            return new PublicKeyDeploymentMaterial(characters);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(characters.AsSpan()));
        }
    }

    private static async Task<char[]> CreatePublicKeyCharactersAsync()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "generated-public-key-deployment", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var privatePath = Path.Combine(root, "id_ed25519");
        try
        {
            var generated = await new Ed25519OpenSshKeyPairGenerator(new ScenarioKeyDiagnosticSink()).GenerateAsync(
                new LocalEd25519KeyGenerationRequest(privatePath),
                DiagnosticRunContext.StartSession().StartOperation("generate_key"),
                CancellationToken.None);
            Assert.True(generated.Succeeded, generated.GenerationErrorCode);
            var text = await File.ReadAllTextAsync(privatePath + ".pub");
            return text.ToCharArray();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
