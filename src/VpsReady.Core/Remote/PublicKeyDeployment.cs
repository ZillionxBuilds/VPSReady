using System.Security.Cryptography;
using System.Runtime.InteropServices;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;

namespace VpsReady.Core.Remote;

/// <summary>
/// Owns transient public-key text until the deployment workflow consumes it.
/// Its content is deliberately unavailable through ToString or diagnostic APIs.
/// </summary>
public sealed class PublicKeyDeploymentMaterial : IDisposable
{
    private char[]? characters;

    public PublicKeyDeploymentMaterial(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || value.Length > 16 * 1024 || ContainsDisallowedControl(value))
        {
            throw new ArgumentException("Public-key material must be bounded printable text.", nameof(value));
        }

        characters = value.ToArray();
    }

    public int Length => characters?.Length ?? throw new InvalidOperationException("The public-key material has been cleared.");

    public char[] CopyForUse()
    {
        var value = characters ?? throw new InvalidOperationException("The public-key material has been cleared.");
        return value.ToArray();
    }

    public void Clear()
    {
        var value = Interlocked.Exchange(ref characters, null);
        if (value is not null)
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(value.AsSpan()));
        }
    }

    public void Dispose() => Clear();

    public override string ToString() => "PublicKeyDeploymentMaterial [redacted]";

    private static bool ContainsDisallowedControl(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (char.IsControl(character) && character is not '\r' and not '\n')
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Separate narrow payload path: the command carries only safe metadata while
/// this transient value is used solely by the concrete remote adapter.
/// </summary>
public interface IPublicKeyDeploymentTransport : IRemoteTransport
{
    Task<RemoteCommandResult> ExecutePublicKeyDeploymentAsync(
        RemoteCommand command,
        ReadOnlyMemory<char> canonicalPublicKey,
        DiagnosticPhase phase,
        CancellationToken cancellationToken);
}

public static class PublicKeyDeploymentErrorCatalog
{
    public const string InvalidInput = "SSH_PUBLIC_KEY_DEPLOYMENT_INPUT_INVALID";
    public const string Unsupported = "SSH_PUBLIC_KEY_DEPLOYMENT_UNSUPPORTED";
    public const string Privilege = "SSH_PUBLIC_KEY_DEPLOYMENT_PRIVILEGE_FAILED";
    public const string Command = "SSH_PUBLIC_KEY_DEPLOYMENT_COMMAND_FAILED";
    public const string Verification = "SSH_PUBLIC_KEY_DEPLOYMENT_VERIFICATION_FAILED";
    public const string Cancelled = "SSH_PUBLIC_KEY_DEPLOYMENT_CANCELLED";
    public const string Recovery = "SSH_PUBLIC_KEY_DEPLOYMENT_RECOVERY_FAILED";
}

public sealed record PublicKeyDeploymentOperationResult(
    OperationResult Result,
    bool AlreadyPresent,
    string? DeploymentErrorCode)
{
    public override string ToString() => "PublicKeyDeploymentOperationResult [safe summary only]";
}

/// <summary>
/// Application-facing boundary for the accepted deployment workflow. It keeps
/// desktop composition independent from the concrete remote adapter.
/// </summary>
public interface IPublicKeyDeployment
{
    Task<PublicKeyDeploymentOperationResult> DeployAsync(
        IRemoteTransport transport,
        PublicKeyDeploymentMaterial material,
        CancellationToken cancellationToken = default);
}
