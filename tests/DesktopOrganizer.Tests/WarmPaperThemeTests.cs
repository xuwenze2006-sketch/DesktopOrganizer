using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Windows.Media;
using System.Xml.Linq;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class WarmPaperThemeTests
{
    [TestMethod]
    public void Palette_UsesLightPaperSurfaceAndReadableText()
    {
        Color surface = WarmPaperTheme.PanelSurfaceColor;

        Assert.IsTrue(RelativeLuminance(surface) > 0.85);
        Assert.IsTrue(ContrastRatio(surface, WarmPaperTheme.PrimaryTextColor) >= 7.0);
        Assert.IsTrue(ContrastRatio(surface, WarmPaperTheme.SecondaryTextColor) >= 4.5);
        Assert.IsTrue(ContrastRatio(surface, WarmPaperTheme.MutedTextColor) >= 4.5);
    }

    [TestMethod]
    public void MainWindow_ConnectsPaperPaletteToPanelAndRecycleWidget()
    {
        XDocument document = LoadMainWindowXaml();

        Assert.AreEqual(
            WarmPaperTheme.PanelSurfaceColor,
            ReadBrushColor(document, "PanelSurfaceBrush"));
        Assert.AreEqual(
            WarmPaperTheme.BorderColor,
            ReadBrushColor(document, "PanelBorderBrush"));
        Assert.AreEqual(
            WarmPaperTheme.PrimaryTextColor,
            ReadBrushColor(document, "PrimaryTextBrush"));
        Assert.AreEqual(
            WarmPaperTheme.SecondaryTextColor,
            ReadBrushColor(document, "SecondaryTextBrush"));
        Assert.AreEqual(
            WarmPaperTheme.MutedTextColor,
            ReadBrushColor(document, "MutedTextBrush"));
        Assert.AreEqual(
            WarmPaperTheme.SageAccentColor,
            ReadBrushColor(document, "AccentBrush"));
        Assert.AreEqual(
            WarmPaperTheme.WarmHoverColor,
            ReadBrushColor(document, "WarmHoverBrush"));
        Assert.AreEqual(
            WarmPaperTheme.WarmPressedColor,
            ReadBrushColor(document, "WarmPressedBrush"));

        XElement panel = FindNamedElement(document, "ControlPanel");
        XElement recycleWidget = FindNamedElement(document, "RecycleBinWidget");
        Assert.AreEqual("#EEF0F6F6", panel.Attribute("Background")?.Value);
        Assert.AreEqual("#20708088", panel.Attribute("BorderBrush")?.Value);
        Assert.AreEqual("#E8F0F6F6", recycleWidget.Attribute("Background")?.Value);
        Assert.AreEqual("#20708088", recycleWidget.Attribute("BorderBrush")?.Value);
    }

    private static Color ReadBrushColor(XDocument document, string key)
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement brush = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "SolidColorBrush" &&
                string.Equals((string?)element.Attribute(xaml + "Key"), key, StringComparison.Ordinal));
        return (Color)ColorConverter.ConvertFromString(
            brush.Attribute("Color")?.Value ?? throw new InvalidOperationException($"{key} has no color."))!;
    }

    private static XElement FindNamedElement(XDocument document, string name)
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document
            .Descendants()
            .Single(element => string.Equals(
                (string?)element.Attribute(xaml + "Name"),
                name,
                StringComparison.Ordinal));
    }

    private static double ContrastRatio(Color first, Color second)
    {
        double light = Math.Max(RelativeLuminance(first), RelativeLuminance(second));
        double dark = Math.Min(RelativeLuminance(first), RelativeLuminance(second));
        return (light + 0.05) / (dark + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linearize(byte component)
        {
            double value = component / 255.0;
            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linearize(color.R) +
               0.7152 * Linearize(color.G) +
               0.0722 * Linearize(color.B);
    }

    private static XDocument LoadMainWindowXaml()
    {
        string projectRoot = TestProjectFiles.Root;
        return XDocument.Load(Path.Combine(projectRoot, "MainWindow.xaml"));
    }
}
