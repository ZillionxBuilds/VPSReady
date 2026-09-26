using System.Xml.Linq;

namespace VpsReady.UnitTests;

public sealed class NavigationContrastTests
{
    [Fact]
    public void NavigationKeepsAccessibleHelpWithoutDisplayingAVisualTooltip()
    {
        XDocument window = LoadMarkup("MainWindow.axaml");
        XElement button = Assert.Single(window.Descendants(), element =>
            element.Name.LocalName == "Button" && (string?)element.Attribute("Classes") == "nav");

        Assert.Null(button.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "ToolTip.Tip"));
        Assert.Equal("{Binding Hint}", button.Attributes().Single(attribute =>
            attribute.Name.LocalName == "AutomationProperties.HelpText").Value);
    }

    [Theory]
    [InlineData("Button.nav.selected TextBlock", "Button.nav.selected /template/ ContentPresenter")]
    [InlineData("Button.nav.selected:pointerover TextBlock", "Button.nav.selected:pointerover /template/ ContentPresenter")]
    [InlineData("Button.nav:pointerover TextBlock", "Button.nav:pointerover /template/ ContentPresenter")]
    public void NavigationLabelHasReadableContrastAgainstItsRenderedSurface(string labelSelector, string surfaceSelector)
    {
        XDocument styles = LoadMarkup("App.axaml");
        string foreground = SetterValue(styles, labelSelector, "Foreground");
        string background = SetterValue(styles, surfaceSelector, "Background");

        Assert.True(ContrastRatio(foreground, background) >= 4.5,
            $"{labelSelector} foreground {foreground} is not readable on {surfaceSelector} background {background}.");
    }

    [Fact]
    public void NavigationLabelPinsReadableForegroundWhenThemeChangesButtonHoverState()
    {
        XDocument window = LoadMarkup("MainWindow.axaml");
        XElement button = Assert.Single(window.Descendants(), element =>
            element.Name.LocalName == "Button" && (string?)element.Attribute("Classes") == "nav");
        XElement label = Assert.Single(button.Descendants(), element => element.Name.LocalName == "TextBlock");
        string foreground = (string?)label.Attribute("Foreground") ?? string.Empty;

        Assert.Equal("#FFFFFF", foreground);
        Assert.True(ContrastRatio(foreground, "#14243D") >= 4.5);

        XDocument styles = LoadMarkup("App.axaml");
        foreach (string selector in new[]
        {
            "Button.nav:pointerover /template/ ContentPresenter",
            "Button.nav.selected /template/ ContentPresenter",
            "Button.nav.selected:pointerover /template/ ContentPresenter",
        })
        {
            Assert.True(ContrastRatio(foreground, SetterValue(styles, selector, "Background")) >= 4.5, selector);
        }
    }

    private static XDocument LoadMarkup(string name)
    {
        using Stream stream = typeof(NavigationContrastTests).Assembly.GetManifestResourceStream(
            $"VpsReady.UnitTests.{name}") ?? throw new InvalidOperationException($"Missing {name} test resource.");
        return XDocument.Load(stream);
    }

    private static string SetterValue(XDocument document, string selector, string property)
    {
        XElement style = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Style" && (string?)element.Attribute("Selector") == selector);
        XElement setter = Assert.Single(style.Elements(), element =>
            element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == property);
        return (string?)setter.Attribute("Value") ?? throw new InvalidOperationException($"Missing {property} value for {selector}.");
    }

    private static double ContrastRatio(string foreground, string background)
    {
        double lighter = Math.Max(Luminance(foreground), Luminance(background));
        double darker = Math.Min(Luminance(foreground), Luminance(background));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(string color)
    {
        Assert.Matches("^#[0-9A-Fa-f]{6}$", color);
        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);

        double Channel(int index)
        {
            double value = Convert.ToInt32(color.Substring(index, 2), 16) / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }
}
