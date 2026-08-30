using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopSearchWindowKeyboardTests
{
    [TestMethod]
    [DataRow(-1, 0, 1, -1)]
    [DataRow(-1, 3, 1, 0)]
    [DataRow(-1, 3, -1, 2)]
    [DataRow(0, 3, -1, 0)]
    [DataRow(0, 3, 1, 1)]
    [DataRow(2, 3, 1, 2)]
    [DataRow(5, 3, 1, 0)]
    public void GetNextSelectionIndex_ClampsAndHandlesMissingSelection(
        int currentIndex,
        int itemCount,
        int direction,
        int expected)
    {
        Assert.AreEqual(
            expected,
            DesktopSearchWindow.GetNextSelectionIndex(
                currentIndex,
                itemCount,
                direction));
    }

    [TestMethod]
    public void SearchWindow_WiresKeyboardHandlersOnlyToSearchControls()
    {
        XDocument document = LoadSearchWindowXaml();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement queryBox = document
            .Descendants()
            .Single(element =>
                string.Equals(
                    (string?)element.Attribute(xaml + "Name"),
                    "QueryBox",
                    StringComparison.Ordinal));
        XElement resultsList = document
            .Descendants()
            .Single(element =>
                string.Equals(
                    (string?)element.Attribute(xaml + "Name"),
                    "ResultsList",
                    StringComparison.Ordinal));

        Assert.AreEqual(
            "QueryBox_PreviewKeyDown",
            queryBox.Attribute("PreviewKeyDown")?.Value);
        Assert.AreEqual(
            "ResultsList_PreviewKeyDown",
            resultsList.Attribute("PreviewKeyDown")?.Value);
        Assert.IsNull(document.Root?.Attribute("PreviewKeyDown"));
    }

    private static XDocument LoadSearchWindowXaml(
        [CallerFilePath] string sourceFilePath = "")
    {
        string testDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Cannot resolve the test source directory.");
        string projectRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", ".."));
        return XDocument.Load(Path.Combine(projectRoot, "DesktopSearchWindow.xaml"));
    }
}
