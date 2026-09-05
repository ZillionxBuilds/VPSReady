using System.Text;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Session-scoped, clearable in-memory reauthentication material. It never
/// exposes password text or a buffer; each transport attempt receives a new
/// temporary UTF-8 array which its caller must clear in a finally block.
/// </summary>
public sealed class PasswordReauthenticationLease : IDisposable
{
    private readonly object gate = new();
    private char[]? characters;

    public PasswordReauthenticationLease(IPasswordCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        var copy = new char[credential.Length];
        try
        {
            credential.CopyTo(copy);
            characters = copy;
            copy = null!;
        }
        finally
        {
            if (copy is not null)
            {
                Array.Clear(copy);
            }
        }
    }

    public bool IsCleared
    {
        get
        {
            lock (gate)
            {
                return characters is null;
            }
        }
    }

    /// <summary>Creates a fresh temporary byte buffer for exactly one authentication attempt.</summary>
    internal byte[] MaterializeUtf8()
    {
        char[]? copy = null;
        try
        {
            lock (gate)
            {
                var value = characters ?? throw new InvalidOperationException("The reauthentication credential has been cleared.");
                copy = value.ToArray();
            }

            return Encoding.UTF8.GetBytes(copy);
        }
        finally
        {
            if (copy is not null)
            {
                Array.Clear(copy);
            }
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            var value = characters;
            characters = null;
            if (value is not null)
            {
                Array.Clear(value);
            }
        }
    }

    public void Dispose() => Clear();

    public override string ToString() => "[credential redacted]";
}
