using Avalonia.Input;
using VpsReady.Desktop;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class PasswordInputKeyActionTests
{
    [Theory]
    [InlineData(Key.A, KeyModifiers.None)]
    [InlineData(Key.A, KeyModifiers.Shift)]
    [InlineData(Key.CapsLock, KeyModifiers.None)]
    [InlineData(Key.Oem1, KeyModifiers.None)]
    [InlineData(Key.D1, KeyModifiers.Shift)]
    [InlineData(Key.Tab, KeyModifiers.None)]
    [InlineData(Key.Tab, KeyModifiers.Shift)]
    [InlineData(Key.Left, KeyModifiers.None)]
    [InlineData(Key.A, KeyModifiers.Control)]
    [InlineData(Key.A, KeyModifiers.Meta)]
    [InlineData(Key.E, KeyModifiers.Alt)]
    public void LayoutAndNavigationEventsNeverCreateOrRejectText(Key key, KeyModifiers modifiers) =>
        Assert.Equal(PasswordInputKeyAction.None, PasswordInputKeys.Classify(key, modifiers));

    [Theory]
    [InlineData(Key.Back, KeyModifiers.None, (int)PasswordInputKeyAction.Backspace)]
    [InlineData(Key.Escape, KeyModifiers.None, (int)PasswordInputKeyAction.Clear)]
    [InlineData(Key.V, KeyModifiers.Control, (int)PasswordInputKeyAction.RejectPaste)]
    [InlineData(Key.V, KeyModifiers.Meta, (int)PasswordInputKeyAction.RejectPaste)]
    [InlineData(Key.Insert, KeyModifiers.Shift, (int)PasswordInputKeyAction.RejectPaste)]
    public void EditingAndPasteActionsAreExplicit(Key key, KeyModifiers modifiers, int expected) =>
        Assert.Equal(expected, (int)PasswordInputKeys.Classify(key, modifiers));
}
