using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class InboxTagUiContractTests
{
    [TestMethod]
    public void InboxWindow_WiresLocalTagEditorAndListSummary()
    {
        XDocument document = LoadInboxWindowXaml();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement editor = FindNamedElement(document, xaml, "TagEditorBox");
        XElement saveButton = FindNamedElement(document, xaml, "SaveTagsButton");
        XElement tagSummary = document.Descendants().Single(element =>
            string.Equals(
                (string?)element.Attribute("Text"),
                "{Binding TagsSummary}",
                StringComparison.Ordinal));

        Assert.AreEqual("TextBox", editor.Name.LocalName);
        Assert.AreEqual("Button", saveButton.Name.LocalName);
        Assert.AreEqual("SaveTags_Click", saveButton.Attribute("Click")?.Value);
        Assert.AreEqual("TextBlock", tagSummary.Name.LocalName);
    }

    private static XElement FindNamedElement(
        XDocument document,
        XNamespace xaml,
        string name) =>
        document.Descendants().Single(element =>
            string.Equals(
                (string?)element.Attribute(xaml + "Name"),
                name,
                StringComparison.Ordinal));

    private static XDocument LoadInboxWindowXaml(
        [CallerFilePath] string sourceFilePath = "")
    {
        string testDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Cannot resolve the test source directory.");
        string projectRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", ".."));
        return XDocument.Load(Path.Combine(projectRoot, "InboxWindow.xaml"));
    }
}
