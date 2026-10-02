using System.Diagnostics;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Infrastructure.Local;

namespace VpsReady.UnitTests;

[Trait("Category", "E3")]
public sealed class GeneratedKeyOpenSshInteropTests
{
    [SshKeygenFact]
    public async Task GeneratedPrivateKeyYieldsTheSamePublicKeyThroughInstalledOpenSsh()
    {
        await using var workspace = new KeyWorkspace();
        var generator = new Ed25519OpenSshKeyPairGenerator(new CollectingDiagnosticSink());
        var generated = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);
        Assert.True(generated.Succeeded);

        var start = new ProcessStartInfo(SshKeygenFactAttribute.Executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-y");
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add(workspace.PrivateKeyPath);

        using var process = Process.Start(start);
        Assert.NotNull(process);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); }
        }

        var publicFromOpenSsh = (await outputTask).Trim();
        _ = await errorTask; // Do not include tool output or local paths in an assertion message.
        Assert.Equal(0, process.ExitCode);
        Assert.Equal((await File.ReadAllTextAsync(workspace.PublicKeyPath)).Trim(), publicFromOpenSsh);
    }
}
