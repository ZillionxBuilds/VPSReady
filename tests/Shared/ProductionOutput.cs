using System.Text;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.Tests;

internal static class ProductionOutput
{
    // Fixture text is wire output, not a production result. Pass it through the
    // same bounded capture and typed-evidence parser as the SSH.NET adapter.
    internal static async Task<RemoteCommandResult> CaptureAsync(RemoteCommand command, RemoteCommandResult wire, CancellationToken cancellationToken)
    {
        using var stdout = new MemoryStream(Encoding.UTF8.GetBytes(wire.StandardOutput));
        using var stderr = new MemoryStream(Encoding.UTF8.GetBytes(wire.StandardError));
        return await SshNetBoundedOutputCapture.ReadResultAsync(command, wire.ExitCode, stdout, stderr, wire.Duration, cancellationToken);
    }
}
