using VpsReady.Application;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class PasswordFrameworkInputTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("ABC")]
    [InlineData("123!@#;:'\"[]{}\\")]
    [InlineData("éก€😀")]
    public void ComposedFrameworkTextIsCopiedExactlyAndCleared(string text)
    {
        using var input = new ConnectionSecretInput();
        Assert.True(input.TryAppendText(text.AsSpan()));
        using var submitted = input.TakeForSubmission();
        Assert.True(submitted.Characters.SequenceEqual(text));
        Assert.Equal(0, input.Length);
        submitted.Dispose();
        Assert.True(submitted.Characters.IndexOfAnyExcept('\0') < 0);
    }

    [Fact]
    public void BackspaceRemovesAWholeSupplementaryCharacterAndInvalidTextIsAtomic()
    {
        using var input = new ConnectionSecretInput();
        Assert.True(input.TryAppendText("a😀"));
        input.Backspace();
        Assert.Equal(1, input.Length);
        Assert.False(input.TryAppendText("invalid\n"));
        Assert.False(input.TryAppendText(new string('x', 4096)));
        Assert.False(input.TryAppendText(['\ud800']));
        using var submitted = input.TakeForSubmission();
        Assert.True(submitted.Characters.SequenceEqual("a"));
    }

    [Theory]
    [InlineData('é')]
    [InlineData('ก')]
    [InlineData('€')]
    public void FrameworkCharacterIsPreservedWithoutUsKeyboardSubstitution(char character)
    {
        using var input = new ConnectionSecretInput();
        input.Append(character);
        using var submitted = input.TakeForSubmission();
        Assert.True(submitted.Characters.SequenceEqual([character]));
    }
}
