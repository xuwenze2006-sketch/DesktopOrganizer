using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class OperationCenterWindowTests
{
    [TestMethod]
    public void JournalGrid_CopiesCompleteRowsWithHeaders()
    {
        XDocument document = LoadOperationCenterXaml();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement grid = document.Descendants().Single(element =>
            string.Equals(
                (string?)element.Attribute(xaml + "Name"),
                "JournalGrid",
                StringComparison.Ordinal));

        Assert.AreEqual("Single", grid.Attribute("SelectionMode")?.Value);
        Assert.AreEqual("FullRow", grid.Attribute("SelectionUnit")?.Value);
        Assert.AreEqual("IncludeHeader", grid.Attribute("ClipboardCopyMode")?.Value);
        Assert.AreEqual(
            8,
            grid.Descendants().Count(element => element.Name.LocalName is
                "DataGridTemplateColumn" or "DataGridTextColumn"));
        AssertClipboardBinding(document, "时间", "{Binding TimeText}");
        AssertClipboardBinding(document, "项目", "{Binding ItemText}");
        AssertClipboardBinding(document, "来源", "{Binding SourceText}");
        AssertClipboardBinding(document, "目标", "{Binding TargetText}");
        AssertClipboardBinding(document, "撤销能力", "{Binding ReversibilityText}");
        AssertClipboardBinding(document, "错误 / 说明", "{Binding ErrorText}");
        AssertTextColumnBinding(document, "类型", "{Binding KindText}");
        AssertTextColumnBinding(document, "状态", "{Binding StateText}");
        Assert.IsTrue(document.Descendants().Any(element =>
            element.Name.LocalName == "TextBlock" &&
            ((string?)element.Attribute("Text"))?.Contains(
                "Ctrl+C 复制详情",
                StringComparison.Ordinal) == true));
    }

    [TestMethod]
    public void FindRestoredSelectionIndex_FollowsSameEntryAcrossRefresh()
    {
        Assert.AreEqual(
            1,
            OperationCenterWindow.FindRestoredSelectionIndex(
                ["new", "selected", "old"],
                "SELECTED"));
    }

    [TestMethod]
    public void FindRestoredSelectionIndex_DoesNotSelectReplacementOrFirstRow()
    {
        Assert.AreEqual(
            -1,
            OperationCenterWindow.FindRestoredSelectionIndex(
                ["new", "other"],
                "missing"));
        Assert.AreEqual(
            -1,
            OperationCenterWindow.FindRestoredSelectionIndex(
                ["new", "other"],
                null));
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

    private static XDocument LoadOperationCenterXaml(
        [CallerFilePath] string sourceFilePath = "")
    {
        string testDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Cannot resolve the test source directory.");
        string projectRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", ".."));
        return XDocument.Load(Path.Combine(projectRoot, "OperationCenterWindow.xaml"));
    }
}
