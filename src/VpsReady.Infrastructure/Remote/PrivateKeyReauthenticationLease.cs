using System.Security.Cryptography;
using Renci.SshNet;
using Renci.SshNet.Security;
using VpsReady.Application;
using VpsReady.Core.Local;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Captured identity A, not an unchecked file location. Every attempt parses a
/// fresh graph from the same bounded snapshot. Owned buffers clear on disposal;
/// SSH.NET's transient passphrase strings/managed key allocations cannot be zeroed by us.
/// </summary>
internal sealed class PrivateKeyReauthenticationLease : IPrivateKeyCredential
{
    private readonly object gate = new();
    private byte[]? bytes;
    private readonly PasswordSessionSecret? unlockHolder;

    internal PrivateKeyReauthenticationLease(ReadOnlySpan<byte> bytes, IPasswordCredential? passphrase, string algorithm, string fingerprint)
    {
        this.bytes = bytes.ToArray();
        Algorithm = algorithm;
        Fingerprint = fingerprint;
        if (passphrase is not null)
        {
            var characters = new char[passphrase.Length];
            try { passphrase.CopyTo(characters); unlockHolder = new PasswordSessionSecret(characters); }
            catch { CryptographicOperations.ZeroMemory(this.bytes); this.bytes = null; throw; }
            finally { Array.Clear(characters); }
        }
    }

    public string Algorithm { get; }
    public string Fingerprint { get; }
    public bool IsCleared { get { lock (gate) { return bytes is null; } } }
    public int Length { get { lock (gate) { return bytes?.Length ?? 0; } } }
    [System.Text.Json.Serialization.JsonIgnore]
    public IPasswordCredential? UnlockCredential => unlockHolder;
    public void CopyTo(Span<byte> destination)
    {
        lock (gate)
        {
            var source = bytes ?? throw new RemoteTransportException(RemoteTransportFailureKind.KeyIdentity);
            if (destination.Length != source.Length) { throw new ArgumentException("Credential buffer length mismatch."); }
            source.CopyTo(destination);
        }
    }

    internal static PrivateKeyReauthenticationLease Capture(IPrivateKeyCredential key)
    {
        var copy = new byte[key.Length];
        try { key.CopyTo(copy); return new(copy, key.UnlockCredential, key.Algorithm, key.Fingerprint); }
        finally { CryptographicOperations.ZeroMemory(copy); }
    }

    internal PrivateKeyFile OpenAttempt()
    {
        lock (gate)
        {
            var data = bytes ?? throw new RemoteTransportException(RemoteTransportFailureKind.KeyIdentity);
            var key = Parse(data, unlockHolder);
            if (!string.Equals(Fingerprint, OpenSshUserKeyFingerprint.FromBlob(key.HostKeyAlgorithms.First().Data), StringComparison.Ordinal))
            {
                key.Dispose();
                throw new RemoteTransportException(RemoteTransportFailureKind.KeyIdentity);
            }
            return key;
        }
    }

    internal static PrivateKeyFile Parse(byte[] bytes, IPasswordCredential? unlock)
    {
        char[]? characters = null;
        try
        {
            if (unlock is not null) { characters = new char[unlock.Length]; unlock.CopyTo(characters); }
            using var stream = new MemoryStream(bytes, writable: false);
            return new PrivateKeyFile(stream, characters is null ? null : new string(characters));
        }
        finally { if (characters is not null) { Array.Clear(characters); } }
    }

    /// <summary>Do not offer the legacy SHA-1 ssh-rsa signature algorithm.</summary>
    internal sealed class Sha2KeySource(PrivateKeyFile key) : IPrivateKeySource
    {
        public IReadOnlyCollection<HostAlgorithm> HostKeyAlgorithms { get; } = key.HostKeyAlgorithms.Where(algorithm => algorithm.Name != "ssh-rsa").ToArray();
    }

    public void Dispose()
    {
        lock (gate)
        {
            var old = bytes;
            bytes = null;
            if (old is not null) { CryptographicOperations.ZeroMemory(old); }
            unlockHolder?.Clear();
        }
    }
    public override string ToString() => "[private credential redacted]";
}
