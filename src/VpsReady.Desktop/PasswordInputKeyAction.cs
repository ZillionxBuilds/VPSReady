using Avalonia.Input;

namespace VpsReady.Desktop;

internal enum PasswordInputKeyAction { None, Backspace, Clear, RejectPaste }

/// <summary>Key events control editing only. Text comes exclusively from TextInput.</summary>
internal static class PasswordInputKeys
{
    internal static PasswordInputKeyAction Classify(Key key, KeyModifiers modifiers) => (key, modifiers) switch
    {
        (Key.Back, KeyModifiers.None) => PasswordInputKeyAction.Backspace,
        (Key.Escape, KeyModifiers.None) => PasswordInputKeyAction.Clear,
        (Key.V, var value) when (value & (KeyModifiers.Control | KeyModifiers.Meta)) != 0 => PasswordInputKeyAction.RejectPaste,
        (Key.Insert, KeyModifiers.Shift) => PasswordInputKeyAction.RejectPaste,
        _ => PasswordInputKeyAction.None,
    };
}
