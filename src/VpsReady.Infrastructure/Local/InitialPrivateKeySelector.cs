using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using Org.BouncyCastle.OpenSsl;
using Renci.SshNet.Common;
using Renci.SshNet.Security;
using VpsReady.Core.Local;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.Infrastructure.Local;

/// <summary>Private-only initial login selector. Deployment pair validation is deliberately separate.</summary>
public sealed class InitialPrivateKeySelector : IInitialPrivateKeySelector
{
    public async Task<InitialPrivateKeySelectionResult> SelectAsync(string path, IPasswordCredential? passphrase, CancellationToken cancellationToken)
    {
        byte[]? bytes = null;
        try
        {
            bytes = await ExistingOpenSshKeySelector.ReadForInitialAuthenticationAsync(path, cancellationToken).ConfigureAwait(false);
            // Restrict the product subset, regardless of the library's wider format support.
            if (!bytes.AsSpan().StartsWith(Encoding.ASCII.GetBytes("-----BEGIN " + "OPENSSH PRIVATE KEY-----"))
                && !bytes.AsSpan().StartsWith(Encoding.ASCII.GetBytes("-----BEGIN " + "RSA PRIVATE KEY-----")))
            {
                return new(null, InitialPrivateKeyError.UnsupportedFormat);
            }
            if (!HasBoundedSupportedEnvelope(bytes)) { return new(null, InitialPrivateKeyError.UnsupportedFormat); }
            cancellationToken.ThrowIfCancellationRequested();
            using var key = PrivateKeyReauthenticationLease.Parse(bytes, passphrase);
            var algorithm = key.Key switch
            {
                ED25519Key => "ssh-ed25519",
                RsaKey when key.Key.KeyLength is 2048 or 3072 or 4096 => "ssh-rsa",
                _ => null,
            };
            if (algorithm is null || key.Certificate is not null) { return new(null, InitialPrivateKeyError.UnsupportedFormat); }
            var fingerprint = OpenSshUserKeyFingerprint.FromBlob(key.HostKeyAlgorithms.First().Data);
            cancellationToken.ThrowIfCancellationRequested();
            return new(new PrivateKeyReauthenticationLease(bytes, passphrase, algorithm, fingerprint), null);
        }
        catch (OperationCanceledException) { return new(null, InitialPrivateKeyError.Cancelled); }
        catch (SshPassPhraseNullOrEmptyException) { return new(null, InitialPrivateKeyError.PassphraseRequired); }
        catch (UnauthorizedAccessException) { return new(null, InitialPrivateKeyError.Permission); }
        catch (InvalidDataException) { return new(null, InitialPrivateKeyError.InvalidFile); }
        catch (IOException) { return new(null, InitialPrivateKeyError.InvalidFile); }
        catch { return new(null, InitialPrivateKeyError.InvalidKeyOrPassphrase); }
        finally { if (bytes is not null) { CryptographicOperations.ZeroMemory(bytes); } }
    }

    // Parse only the documented envelope metadata, never cryptographic material.
    // Limit attacker-controlled bcrypt work before invoking the maintained parser.
    private static bool HasBoundedSupportedEnvelope(byte[] bytes)
    {
        byte[]? decoded = null;
        try
        {
            using var reader = new StreamReader(new MemoryStream(bytes, writable: false), Encoding.ASCII, false);
            var pem = new PemReader(reader).ReadPemObject();
            if (pem is null) { return false; }
            decoded = pem.Content;
            if (pem.Type == "RSA PRIVATE KEY") { return pem.Headers.Count == 0; }
            ReadOnlySpan<byte> remaining = decoded;
            if (!remaining.StartsWith("openssh-key-v1\0"u8)) { return false; }
            remaining = remaining[15..];
            if (!ReadString(ref remaining, out var cipher) || !ReadString(ref remaining, out var kdf)
                || !ReadString(ref remaining, out var options) || remaining.Length < 4
                || BinaryPrimitives.ReadUInt32BigEndian(remaining) != 1) { return false; }
            if (cipher.SequenceEqual("none"u8)) { return kdf.SequenceEqual("none"u8) && options.IsEmpty; }
            if (!cipher.SequenceEqual("aes256-ctr"u8) || !kdf.SequenceEqual("bcrypt"u8)) { return false; }
            if (!ReadString(ref options, out var salt) || salt.Length is < 16 or > 64 || options.Length != 4) { return false; }
            var rounds = BinaryPrimitives.ReadUInt32BigEndian(options);
            return rounds is >= 1 and <= 64;
        }
        finally { if (decoded is not null) { CryptographicOperations.ZeroMemory(decoded); } }
    }

    private static bool ReadString(ref ReadOnlySpan<byte> input, out ReadOnlySpan<byte> value)
    {
        value = default;
        if (input.Length < 4) { return false; }
        var length = BinaryPrimitives.ReadUInt32BigEndian(input);
        input = input[4..];
        if (length > input.Length) { return false; }
        value = input[..(int)length];
        input = input[(int)length..];
        return true;
    }
}
