namespace VpsReady.Core.Local;

/// <summary>
/// Portable single-component names for a generated private key. The public
/// counterpart is always the same name plus ".pub". This deliberately does
/// not accept directory separators, whitespace, extensions or device names.
/// </summary>
public static class LocalSshKeyNamePolicy
{
    public const int MaximumLength = 64;

    public static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaximumLength || !IsAsciiLetterOrDigit(name[0]))
        {
            return false;
        }

        foreach (var character in name)
        {
            if (!IsAsciiLetterOrDigit(character) && character is not ('_' or '-'))
            {
                return false;
            }
        }

        return !IsWindowsDeviceName(name);
    }

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    private static bool IsWindowsDeviceName(string name) =>
        name.Equals("CON", StringComparison.OrdinalIgnoreCase)
        || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
        || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
        || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
        || (name.Length == 4
            && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && name[3] is >= '1' and <= '9');
}
