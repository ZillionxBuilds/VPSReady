using System.Security.Cryptography;

namespace VpsReady.Core.Local;

/// <summary>Local user-key identity only. Never changes persisted server host-key trust.</summary>
public static class OpenSshUserKeyFingerprint
{
    /// <summary>Hashes the serialized SSH public blob, including algorithm and length prefixes.</summary>
    public static string FromBlob(ReadOnlySpan<byte> validatedPublicBlob)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(validatedPublicBlob, hash);
        return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
    }
}
