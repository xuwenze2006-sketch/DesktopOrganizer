using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Xml.Linq;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class RulePreviewWindowUiContractTests
{
    [TestMethod]
    public void PreviewGrid_CopiesCompleteRowsWithHeadersWithoutChangingDialogActions()
    {
        XDocument document = LoadRulePreviewWindowXaml();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement grid = FindNamedElement(document, xaml, "PreviewGrid");

        Assert.AreEqual("Single", grid.Attribute("SelectionMode")?.Value);
        Assert.AreEqual("FullRow", grid.Attribute("SelectionUnit")?.Value);
        Assert.AreEqual("IncludeHeader", grid.Attribute("ClipboardCopyMode")?.Value);
        StringAssert.Contains(grid.Attribute("ToolTip")?.Value, "Ctrl+C");
        Assert.AreEqual(
            5,
            grid.Descendants().Count(element => element.Name.LocalName is
                "DataGridTemplateColumn" or "DataGridTextColumn"));
        AssertTextColumnBinding(document, "项目", "{Binding DisplayName}");
        AssertTextColumnBinding(document, "状态", "{Binding Status}");
        AssertTextColumnBinding(document, "目标", "{Binding Target}");
        AssertClipboardBinding(document, "命中原因", "{Binding Explanation}");
        AssertClipboardBinding(document, "冲突", "{Binding ConflictText}");

        XElement copyButton = FindNamedElement(
            document,
            xaml,
            "CopySelectedPreviewButton");
        Assert.AreEqual(
            "{x:Static ApplicationCommands.Copy}",
            copyButton.Attribute("Command")?.Value);
        Assert.AreEqual(
            "{Binding ElementName=PreviewGrid}",
            copyButton.Attribute("CommandTarget")?.Value);
        StringAssert.Contains(copyButton.Attribute("ToolTip")?.Value, "Ctrl+C");
        StringAssert.Contains(copyButton.Attribute("ToolTip")?.Value, "五列");
        StringAssert.Contains(copyButton.Attribute("ToolTip")?.Value, "表头");
        Assert.IsNull(copyButton.Attribute("Click"));
        Assert.IsNull(copyButton.Attribute("IsDefault"));
        Assert.IsNull(copyButton.Attribute("IsCancel"));

        XElement cancelButton = FindButton(document, "取消");
        Assert.AreEqual("True", cancelButton.Attribute("IsCancel")?.Value);
        Assert.AreEqual("Cancel_Click", cancelButton.Attribute("Click")?.Value);
        XElement primaryButton = FindNamedElement(document, xaml, "PrimaryButton");
        Assert.AreEqual("True", primaryButton.Attribute("IsDefault")?.Value);
        Assert.AreEqual("Primary_Click", primaryButton.Attribute("Click")?.Value);
    }

    private static void AssertClipboardBinding(
        XDocument document,
        string header,
        string expectedBinding)
    {
        XElement column = document.Descendants().Single(element =>
            element.Name.LocalName == "DataGridTemplateColumn" &&
            string.Equals(
                (string?)element.Attribute("Header"),
                header,
                StringComparison.Ordinal));
        Assert.AreEqual(
            expectedBinding,
            column.Attribute("ClipboardContentBinding")?.Value);
    }

    private static void AssertTextColumnBinding(
        XDocument document,
        string header,
        string expectedBinding)
    {
        XElement column = document.Descendants().Single(element =>
            element.Name.LocalName == "DataGridTextColumn" &&
            string.Equals(
                (string?)element.Attribute("Header"),
                header,
                StringComparison.Ordinal));
        Assert.AreEqual(expectedBinding, column.Attribute("Binding")?.Value);
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

    private static XElement FindButton(XDocument document, string content) =>
        document.Descendants().Single(element =>
            element.Name.LocalName == "Button" &&
            string.Equals(
                (string?)element.Attribute("Content"),
                content,
                StringComparison.Ordinal));

    private static XDocument LoadRulePreviewWindowXaml()
    {
        string projectRoot = TestProjectFiles.Root;
        return XDocument.Load(Path.Combine(projectRoot, "RulePreviewWindow.xaml"));
    }
}
