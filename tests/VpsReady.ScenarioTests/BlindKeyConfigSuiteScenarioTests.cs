using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

/// <summary>
/// C408's cross-workflow blind suite. It deliberately composes only the
/// deterministic mutable scenario host; no production transport is replaced
/// and no external endpoint or credential is involved.
/// </summary>
[Trait("Category", "E2")]
public sealed class BlindKeyConfigSuiteScenarioTests
{
    private const string PublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIEbFmEDB3D6oqfg1T3AdzKmAj6JFCfw7ZySYRXqfEpcZ";

    [Fact]
    public async Task DeploymentDuplicateAndSeparateKeyVerificationPreserveRemoteStateAndSafeCorrelation()
    {
        await using var services = ScenarioComposition.Create("c408.mutable-key-success");
        var state = services.GetRequiredService<ScenarioHostState>();
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var diagnostics = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var authorizedKeys = $"/home/{state.Ssh.UserName}/.ssh/authorized_keys";
        const string legacyAuthorizedKeys = "legacy-entry\n";
        state.RemoteFiles.Files[authorizedKeys] = state.RemoteFiles.Files[authorizedKeys] with { Contents = legacyAuthorizedKeys, Permissions = "0644" };

        using var firstKey = new PublicKeyDeploymentMaterial(PublicKey.AsSpan());
        var first = await services.GetRequiredService<PublicKeyDeploymentWorkflow>().DeployAsync(host, firstKey);
        using var secondKey = new PublicKeyDeploymentMaterial(PublicKey.AsSpan());
        var duplicate = await services.GetRequiredService<PublicKeyDeploymentWorkflow>().DeployAsync(host, secondKey);
        var verification = await services.GetRequiredService<KeyAuthenticationVerificationWorkflow>().VerifyAsync(Request());

        Assert.True(first.Result.Succeeded);
        Assert.True(duplicate.Result.Succeeded);
        Assert.True(duplicate.AlreadyPresent);
        Assert.True(verification.Result.Succeeded);
        Assert.StartsWith(legacyAuthorizedKeys, state.RemoteFiles.Files[authorizedKeys].Contents, StringComparison.Ordinal);
        Assert.Equal("0644", state.RemoteFiles.Files[authorizedKeys].Permissions);
        Assert.Equal(state.Ssh.UserName, state.RemoteFiles.Files[authorizedKeys].Owner);
        Assert.Single(state.Ssh.AuthorizedKeyFingerprints);
        var diagnosticSurfaces = string.Concat(
            string.Join("\n", diagnostics.ActivityMessages),
            "\n",
            diagnostics.ToJsonLines(),
            "\n",
            JsonSerializer.Serialize(diagnostics.Events),
            "\n",
            JsonSerializer.Serialize(diagnostics.Events.Select(item => item.ToActivityEntry())));
        Assert.DoesNotContain(PublicKey, diagnosticSurfaces, StringComparison.Ordinal);
        Assert.DoesNotContain("ssh-ed25519", diagnosticSurfaces, StringComparison.Ordinal);
        Assert.All(diagnostics.Events, item => Assert.False(string.IsNullOrWhiteSpace(item.Correlation.OperationId)));
    }

    [Fact]
    public async Task ChangedTrustAndVerifyFaultsNeverReportSuccessOrMutateASecondKey()
    {
        await using var trustServices = ScenarioComposition.Create("c408.changed-trust", state => state.Ssh.HostKey = ScenarioHostKeyState.Changed);
        var trustResult = await trustServices.GetRequiredService<KeyAuthenticationVerificationWorkflow>().VerifyAsync(Request());
        Assert.Equal(OperationErrorCode.HostTrust, trustResult.Result.ErrorCode);
        Assert.DoesNotContain(trustServices.GetRequiredService<ScenarioDiagnosticRecorder>().Events, item => item.EventId == DiagnosticEventCatalog.KeyAuthenticationVerificationSucceeded);

        await using var faultServices = ScenarioComposition.Create("c408.deploy-verify-fault");
        var host = faultServices.GetRequiredService<DeterministicScenarioHost>();
        faultServices.GetRequiredService<ScenarioFaultPlan>().Inject(DiagnosticPhase.Verify, ScenarioFaultKind.VerificationMismatch, "c408-verify-fault", RemoteCommandCatalog.UbuntuAuthorizedKeysVerify);
        using var material = new PublicKeyDeploymentMaterial(PublicKey.AsSpan());
        var faultResult = await faultServices.GetRequiredService<PublicKeyDeploymentWorkflow>().DeployAsync(host, material);

        Assert.False(faultResult.Result.Succeeded);
        Assert.Equal(OperationErrorCode.Verification, faultResult.Result.ErrorCode);
        Assert.DoesNotContain(faultServices.GetRequiredService<ScenarioDiagnosticRecorder>().Events, item => item.EventId == DiagnosticEventCatalog.PublicKeyDeploymentSucceeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.ExecuteAsync(new RemoteCommand(new RemoteCommandId("c408.unknown-command"), string.Empty, TimeSpan.FromSeconds(1)), CancellationToken.None));
    }

    private static KeyAuthenticationVerificationRequest Request() => new(
        new RemoteEndpoint("scenario-c408-host", 22, "scenario-user"),
        new KnownHostIdentity("scenario-c408-host", 22),
        ExistingSshKeySelectionResult.Success(
            OperationResult.Success("c408-selected-key", OperationState.Unchanged),
            new ExistingSshKeyLocation("/scenario/private/id_ed25519"),
            new ExistingSshKeyMetadata("ed25519", "SHA256:opaque")),
        TimeSpan.FromSeconds(1));
}
