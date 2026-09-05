using Avalonia.Input;
using VpsReady.Application;
using VpsReady.Desktop;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class PrintablePasswordKeyMapperTests
{
    [Theory]
    [InlineData(Key.A, KeyModifiers.None, 'a')]
    [InlineData(Key.A, KeyModifiers.Shift, 'A')]
    [InlineData(Key.Z, KeyModifiers.None, 'z')]
    [InlineData(Key.Z, KeyModifiers.Shift, 'Z')]
    [InlineData(Key.D1, KeyModifiers.None, '1')]
    [InlineData(Key.D1, KeyModifiers.Shift, '!')]
    [InlineData(Key.D0, KeyModifiers.Shift, ')')]
    public void PrintablePasswordKeyMapperPreservesCaseAndShiftedDigits(Key key, KeyModifiers modifiers, char expected)
    {
        Assert.True(PrintablePasswordKeyMapper.TryMap(key, modifiers, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(Key.Oem1, KeyModifiers.None, ';')]
    [InlineData(Key.Oem1, KeyModifiers.Shift, ':')]
    [InlineData(Key.OemPlus, KeyModifiers.None, '=')]
    [InlineData(Key.OemPlus, KeyModifiers.Shift, '+')]
    [InlineData(Key.OemComma, KeyModifiers.None, ',')]
    [InlineData(Key.OemComma, KeyModifiers.Shift, '<')]
    [InlineData(Key.OemMinus, KeyModifiers.None, '-')]
    [InlineData(Key.OemMinus, KeyModifiers.Shift, '_')]
    [InlineData(Key.OemPeriod, KeyModifiers.None, '.')]
    [InlineData(Key.OemPeriod, KeyModifiers.Shift, '>')]
    [InlineData(Key.Oem2, KeyModifiers.None, '/')]
    [InlineData(Key.Oem2, KeyModifiers.Shift, '?')]
    [InlineData(Key.Oem3, KeyModifiers.None, '`')]
    [InlineData(Key.Oem3, KeyModifiers.Shift, '~')]
    [InlineData(Key.Oem4, KeyModifiers.None, '[')]
    [InlineData(Key.Oem4, KeyModifiers.Shift, '{')]
    [InlineData(Key.Oem5, KeyModifiers.None, '\\')]
    [InlineData(Key.Oem5, KeyModifiers.Shift, '|')]
    [InlineData(Key.Oem6, KeyModifiers.None, ']')]
    [InlineData(Key.Oem6, KeyModifiers.Shift, '}')]
    [InlineData(Key.Oem7, KeyModifiers.None, '\'')]
    [InlineData(Key.Oem7, KeyModifiers.Shift, '"')]
    [InlineData(Key.Oem102, KeyModifiers.None, '<')]
    [InlineData(Key.Oem102, KeyModifiers.Shift, '>')]
    [InlineData(Key.Space, KeyModifiers.None, ' ')]
    public void PrintablePasswordKeyMapperMapsRecognizedAsciiPunctuation(Key key, KeyModifiers modifiers, char expected)
    {
        Assert.True(PrintablePasswordKeyMapper.TryMap(key, modifiers, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(Key.A, KeyModifiers.Control)]
    [InlineData(Key.Oem2, KeyModifiers.Alt)]
    [InlineData(Key.Enter, KeyModifiers.None)]
    public void PrintablePasswordKeyMapperFailsClosedForModifiedOrNonPrintableInput(Key key, KeyModifiers modifiers)
    {
        Assert.False(PrintablePasswordKeyMapper.TryMap(key, modifiers, out _));
    }

    [Fact]
    public void PrintablePasswordKeyMapperFeedsClearableInputWithoutCreatingTextValue()
    {
        using var entry = new ConnectionSecretInput();
        Assert.True(PrintablePasswordKeyMapper.TryMap(Key.A, KeyModifiers.Shift, out var upper));
        Assert.True(PrintablePasswordKeyMapper.TryMap(Key.D1, KeyModifiers.Shift, out var punctuation));
        entry.Append(upper);
        entry.Append(punctuation);

        using var submitted = entry.TakeForSubmission();
        Assert.True(submitted.Characters.SequenceEqual(['A', '!']));
        Assert.Equal(0, entry.Length);
        Assert.DoesNotContain("A!", entry.ToString(), StringComparison.Ordinal);
    }
}
