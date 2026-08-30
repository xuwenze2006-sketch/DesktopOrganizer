using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class SimpleInputDialogUiContractTests
{
    [TestMethod]
    public void DialogButtons_WireStandardEnterAndEscapeActions()
    {
        XDocument document = LoadSimpleInputDialogXaml();
        XElement cancelButton = FindButton(document, "取消");
        XElement okButton = FindButton(document, "确定");

        Assert.AreEqual("True", cancelButton.Attribute("IsCancel")?.Value);
        Assert.AreEqual("Cancel_Click", cancelButton.Attribute("Click")?.Value);
        Assert.AreEqual("True", okButton.Attribute("IsDefault")?.Value);
        Assert.AreEqual("Ok_Click", okButton.Attribute("Click")?.Value);
    }

    private static XElement FindButton(XDocument document, string content) =>
        document.Descendants().Single(element =>
            element.Name.LocalName == "Button" &&
            string.Equals(
                (string?)element.Attribute("Content"),
                content,
                StringComparison.Ordinal));

    private static XDocument LoadSimpleInputDialogXaml(
        [CallerFilePath] string sourceFilePath = "")
    {
        string testDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Cannot resolve the test source directory.");
        string projectRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", ".."));
        return XDocument.Load(Path.Combine(projectRoot, "SimpleInputDialog.xaml"));
    }
}
