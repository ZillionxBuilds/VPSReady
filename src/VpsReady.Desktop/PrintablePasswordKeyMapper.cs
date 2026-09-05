using Avalonia.Input;

namespace VpsReady.Desktop;

/// <summary>
/// Maps only recognized printable US-ASCII key events into the clearable
/// connection-input boundary. Control, composition, and platform-modifier
/// events deliberately have no mapping.
/// </summary>
internal static class PrintablePasswordKeyMapper
{
    public static bool TryMap(Key key, KeyModifiers modifiers, out char value)
    {
        value = default;
        if ((modifiers & ~KeyModifiers.Shift) != KeyModifiers.None)
        {
            return false;
        }

        var shifted = (modifiers & KeyModifiers.Shift) != KeyModifiers.None;
        if (key is >= Key.A and <= Key.Z)
        {
            value = (char)((shifted ? 'A' : 'a') + ((int)key - (int)Key.A));
            return true;
        }

        return key switch
        {
            Key.D0 => Map(shifted ? ')' : '0', out value),
            Key.D1 => Map(shifted ? '!' : '1', out value),
            Key.D2 => Map(shifted ? '@' : '2', out value),
            Key.D3 => Map(shifted ? '#' : '3', out value),
            Key.D4 => Map(shifted ? '$' : '4', out value),
            Key.D5 => Map(shifted ? '%' : '5', out value),
            Key.D6 => Map(shifted ? '^' : '6', out value),
            Key.D7 => Map(shifted ? '&' : '7', out value),
            Key.D8 => Map(shifted ? '*' : '8', out value),
            Key.D9 => Map(shifted ? '(' : '9', out value),
            Key.Space => Map(' ', out value),
            Key.Oem1 => Map(shifted ? ':' : ';', out value),
            Key.OemPlus => Map(shifted ? '+' : '=', out value),
            Key.OemComma => Map(shifted ? '<' : ',', out value),
            Key.OemMinus => Map(shifted ? '_' : '-', out value),
            Key.OemPeriod => Map(shifted ? '>' : '.', out value),
            Key.Oem2 => Map(shifted ? '?' : '/', out value),
            Key.Oem3 => Map(shifted ? '~' : '`', out value),
            Key.Oem4 => Map(shifted ? '{' : '[', out value),
            Key.Oem5 => Map(shifted ? '|' : '\\', out value),
            Key.Oem6 => Map(shifted ? '}' : ']', out value),
            Key.Oem7 => Map(shifted ? '"' : '\'', out value),
            Key.Oem102 => Map(shifted ? '>' : '<', out value),
            Key.NumPad0 => Map('0', out value),
            Key.NumPad1 => Map('1', out value),
            Key.NumPad2 => Map('2', out value),
            Key.NumPad3 => Map('3', out value),
            Key.NumPad4 => Map('4', out value),
            Key.NumPad5 => Map('5', out value),
            Key.NumPad6 => Map('6', out value),
            Key.NumPad7 => Map('7', out value),
            Key.NumPad8 => Map('8', out value),
            Key.NumPad9 => Map('9', out value),
            _ => false,
        };
    }

    private static bool Map(char mapped, out char value)
    {
        value = mapped;
        return true;
    }
}
