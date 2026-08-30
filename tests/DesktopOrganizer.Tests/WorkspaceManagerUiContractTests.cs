using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class WorkspaceManagerUiContractTests
{
    [TestMethod]
    public void PreviewText_RemainsScrollableAtMinimumWindowSize()
    {
        XDocument document = LoadWorkspaceManagerXaml();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement preview = document.Descendants().Single(element =>
            string.Equals(
                (string?)element.Attribute(xaml + "Name"),
                "PreviewText",
                StringComparison.Ordinal));
        XElement scrollViewer = preview.Ancestors().Single(element =>
            element.Name.LocalName == "ScrollViewer");

        Assert.AreEqual("Auto", scrollViewer.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.AreEqual("Disabled", scrollViewer.Attribute("HorizontalScrollBarVisibility")?.Value);
    }

    private static XDocument LoadWorkspaceManagerXaml(
        [CallerFilePath] string sourceFilePath = "")
    {
        string testDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Cannot resolve the test source directory.");
        string projectRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", ".."));
        return XDocument.Load(Path.Combine(projectRoot, "WorkspaceManagerWindow.xaml"));
    }
}
