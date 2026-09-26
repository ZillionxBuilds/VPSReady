using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
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
    public async Task AppliedKeyRemainsUnverifiedForSessionOutcomeAfterLateCancellation()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.public-key-late-session-cancel");
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var sink = services.GetRequiredService<IDiagnosticSink>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        await using var session = new ApplicationSession();
        await session.StartAsync(new RemoteEndpoint("scenario-private-host", 22, "scenario-user"), host);
        using var key = await CreateMaterialAsync();
        var correlation = CorrelationIds.Create("deploy_key");
        var operationDiagnostics = SessionOperationDiagnostics.ForPublicKeyDeployment(correlation, sink);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var action = session.RunOperationForSessionAsync(
            correlation.OperationId,
            TimeSpan.FromSeconds(10),
            async (transport, token) =>
            {
                var workflow = await new PublicKeyDeploymentWorkflow(sink)
                    .DeployAsync(transport, key, operationDiagnostics, token);
                entered.TrySetResult();
                await release.Task;
                return workflow.Result;
            },
            session.Snapshot.SessionId!,
            cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEmpty(host.State.Ssh.AuthorizedKeyFingerprints);
            cancellation.Cancel();
        }
        finally
        {
            release.TrySetResult();
        }

        var result = await action.WaitAsync(TimeSpan.FromSeconds(5));
        await operationDiagnostics.FinalizeAsync(result);
        Assert.True(result.Cancelled);
        Assert.Equal(OperationState.Unknown, result.State);
        var terminal = Assert.Single(recorder.Events, entry =>
            entry.Correlation.OperationId == result.OperationId
            && entry.EventId is DiagnosticEventCatalog.PublicKeyDeploymentSucceeded or
                DiagnosticEventCatalog.PublicKeyDeploymentFailed or
                DiagnosticEventCatalog.PublicKeyDeploymentCancelled);
        Assert.Equal(DiagnosticEventCatalog.PublicKeyDeploymentCancelled, terminal.EventId);
        Assert.Equal(OperationErrorCode.Cancelled.ToStableCode(), terminal.ErrorCode);
        Assert.DoesNotContain(DiagnosticEventCatalog.PublicKeyDeploymentSucceeded, recorder.ToJsonLines(), StringComparison.Ordinal);
        Assert.DoesNotContain("ssh-ed25519", recorder.ToJsonLines(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeploymentPreservesExistingEntriesAndSafePermissionsAndIsIdempotent()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.public-key-deployment");
        var state = services.GetRequiredService<ScenarioHostState>();
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var workflow = new PublicKeyDeploymentWorkflow(services.GetRequiredService<IDiagnosticSink>());
        var path = $"/home/{state.Ssh.UserName}/.ssh/authorized_keys";
        var directory = $"/home/{state.Ssh.UserName}/.ssh";
        state.RemoteFiles.Files[path] = state.RemoteFiles.Files[path] with { Contents = "legacy-entry\n", Group = "retained-group", Permissions = "0644" };
        state.RemoteFiles.Files[directory] = state.RemoteFiles.Files[directory] with { Permissions = "0755" };

        var characters = await CreatePublicKeyCharactersAsync();
        try
        {
            using var key = new PublicKeyDeploymentMaterial(characters);
            var first = await workflow.DeployAsync(host, key);

            Assert.True(first.Result.Succeeded);
            Assert.False(first.AlreadyPresent);
            Assert.StartsWith("legacy-entry", state.RemoteFiles.Files[path].Contents, StringComparison.Ordinal);
            Assert.Equal("0755", state.RemoteFiles.Files[directory].Permissions);
            Assert.Equal("0644", state.RemoteFiles.Files[path].Permissions);
            Assert.Equal("retained-group", state.RemoteFiles.Files[path].Group);
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
    public async Task DeploymentCannotTakeOverForeignOwnedAuthorizedKeys()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.public-key-foreign-owner");
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var path = $"/home/{host.State.Ssh.UserName}/.ssh/authorized_keys";
        var original = host.State.RemoteFiles.Files[path] with { Owner = "foreign-owner" };
        host.State.RemoteFiles.Files[path] = original;
        using var key = await CreateMaterialAsync();
        var result = await new PublicKeyDeploymentWorkflow(services.GetRequiredService<IDiagnosticSink>()).DeployAsync(host, key);
        Assert.False(result.Result.Succeeded);
        Assert.Equal(original, host.State.RemoteFiles.Files[path]);
        Assert.Empty(host.State.Ssh.AuthorizedKeyFingerprints);
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
    public async Task CancellationAfterSimulatedVerifyDoesNotReportDeploymentSuccess()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.public-key-late-cancel");
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        using var key = await CreateMaterialAsync();
        using var cancellation = new CancellationTokenSource();
        var diagnostics = new CancelAfterVerifyCommandSink(services.GetRequiredService<IDiagnosticSink>(), cancellation);

        var result = await new PublicKeyDeploymentWorkflow(diagnostics).DeployAsync(host, key, cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.NotEmpty(host.State.Ssh.AuthorizedKeyFingerprints);
        Assert.DoesNotContain(recorder.Events, item => item.EventId == DiagnosticEventCatalog.PublicKeyDeploymentSucceeded);
        Assert.Single(recorder.Events, item => item.EventId == DiagnosticEventCatalog.PublicKeyDeploymentCancelled && item.Phase == DiagnosticPhase.Verify);
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

    private sealed class CancelAfterVerifyCommandSink(IDiagnosticSink inner, CancellationTokenSource cancellation) : IDiagnosticSink
    {
        public async Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken)
        {
            await inner.WriteAsync(entry, cancellationToken);
            if (entry.EventId == DiagnosticEventCatalog.CommandCompleted && entry.Phase == DiagnosticPhase.Verify)
            {
                cancellation.Cancel();
            }
        }
    }
}
