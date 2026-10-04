using System.Xml.Linq;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class InitialPrivateKeyFolderHintTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void FolderHintsAreStaticWrappedGuidanceBesideThePrivateKeyPicker()
    {
        var markup = LoadMarkup();
        var hints = NamedElement(markup, "InitialPrivateKeyFolderHints");
        var keyPanel = Assert.IsType<XElement>(hints.Parent);
        Assert.Equal("{Binding ConnectionOverview.IsPrivateKeyMode}", keyPanel.Attribute("IsVisible")?.Value);
        var picker = Assert.Single(keyPanel.Elements(), element => element.Attribute("Click")?.Value == "ChooseInitialPrivateKeyAsync");
        Assert.Same(picker, hints.PreviousNode);
        Assert.All(hints.Elements(), hint =>
        {
            Assert.Equal("TextBlock", hint.Name.LocalName);
            Assert.Equal("Wrap", hint.Attribute("TextWrapping")?.Value);
            var text = Assert.IsType<string>(hint.Attribute("Text")?.Value);
            Assert.DoesNotContain("{Binding", text, StringComparison.Ordinal);
            Assert.DoesNotContain("{x:Static", text, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("InitialPrivateKeyMacFolderHint", "macOS:", "~/.ssh", "/Users/your-name/.ssh")]
    [InlineData("InitialPrivateKeyWindowsFolderHint", "Windows:", @"%USERPROFILE%\.ssh", @"C:\Users\your-name\.ssh")]
    [InlineData("InitialPrivateKeyLinuxFolderHint", "Linux:", "~/.ssh", "/home/your-name/.ssh")]
    public void EachOperatingSystemHasItsGenericOpenSshFolder(string name, string operatingSystem, string shorthand, string example)
    {
        var text = NamedElement(LoadMarkup(), name).Attribute("Text")!.Value;
        Assert.Contains(operatingSystem, text, StringComparison.Ordinal);
        Assert.Contains(shorthand, text, StringComparison.Ordinal);
        Assert.Contains(example, text, StringComparison.Ordinal);
    }

    [Fact]
    public void HintsExplainHiddenMacFolderAndPrivateNotPublicSelectionWithoutAutoSelection()
    {
        var markup = LoadMarkup();
        var hints = NamedElement(markup, "InitialPrivateKeyFolderHints");
        Assert.Contains("your key may be elsewhere", hints.Elements().First().Attribute("Text")!.Value, StringComparison.Ordinal);
        Assert.Contains("Cmd+Shift+G", NamedElement(markup, "InitialPrivateKeyMacFolderHint").Attribute("Text")!.Value, StringComparison.Ordinal);
        var fileHint = NamedElement(markup, "InitialPrivateKeyFileHint").Attribute("Text")!.Value;
        Assert.Contains("id_ed25519 or id_rsa", fileHint, StringComparison.Ordinal);
        Assert.Contains("not the .pub file", fileHint, StringComparison.Ordinal);
        Assert.Contains("No key is selected automatically", fileHint, StringComparison.Ordinal);
    }

    private static XElement NamedElement(XDocument markup, string name) =>
        Assert.Single(markup.Descendants(), element => element.Attribute(Xaml + "Name")?.Value == name);

    private static XDocument LoadMarkup()
    {
        using var stream = typeof(InitialPrivateKeyFolderHintTests).Assembly.GetManifestResourceStream("VpsReady.UnitTests.MainWindow.axaml")
            ?? throw new InvalidOperationException("Embedded window markup is unavailable.");
        return XDocument.Load(stream);
    }
}
