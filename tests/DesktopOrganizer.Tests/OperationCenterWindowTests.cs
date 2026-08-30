using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Threading;
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
                "Ctrl+C",
                StringComparison.Ordinal) == true));
        Assert.IsTrue(document.Descendants().Any(element =>
            element.Name.LocalName == "TextBlock" &&
            ((string?)element.Attribute("Text"))?.Contains(
                "每秒自动刷新",
                StringComparison.Ordinal) == true));
        Assert.IsTrue(document.Descendants().Any(element =>
            element.Name.LocalName == "TextBlock" &&
            ((string?)element.Attribute("Text"))?.Contains(
                "默认按最近时间排序",
                StringComparison.Ordinal) == true));

        XElement copyButton = FindNamedElement(
            document,
            xaml,
            "CopySelectedDetailsButton");
        Assert.AreEqual(
            "{x:Static ApplicationCommands.Copy}",
            copyButton.Attribute("Command")?.Value);
        Assert.AreEqual(
            "{Binding ElementName=JournalGrid}",
            copyButton.Attribute("CommandTarget")?.Value);
        StringAssert.Contains(copyButton.Attribute("ToolTip")?.Value, "Ctrl+C");
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

    [STATestMethod]
    public void RefreshView_PreservesUserSortAndSelectedEntryIdentity()
    {
        DateTime now = DateTime.UtcNow;
        var journal = new FileOperationJournalData
        {
            Entries =
            [
                NewJournalEntry(
                    "recent",
                    now,
                    FileOperationJournalKind.MoveIntoFolder,
                    "recent.txt"),
                NewJournalEntry(
                    "selected",
                    now.AddMinutes(-1),
                    FileOperationJournalKind.EmptyRecycleBin,
                    "selected.txt")
            ]
        };
        var window = new OperationCenterWindow(() => journal, () => null);
        DispatcherTimer refreshTimer = (DispatcherTimer)(typeof(OperationCenterWindow)
            .GetField("_refreshTimer", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(window)
            ?? throw new AssertFailedException("未找到操作中心刷新计时器。"));
        refreshTimer.Stop();

        var sortDescription = new SortDescription(
            "KindText",
            ListSortDirection.Ascending);
        window.JournalGrid.Items.SortDescriptions.Add(sortDescription);
        object selectedRow = FindOperationRow(window, "selected");
        if (window.JournalGrid.Items.IndexOf(selectedRow) != 0)
        {
            window.JournalGrid.Items.SortDescriptions.Clear();
            sortDescription = new SortDescription(
                "KindText",
                ListSortDirection.Descending);
            window.JournalGrid.Items.SortDescriptions.Add(sortDescription);
        }

        DataGridColumn kindColumn = window.JournalGrid.Columns.Single(column =>
            string.Equals(column.Header as string, "类型", StringComparison.Ordinal));
        kindColumn.SortDirection = sortDescription.Direction;
        selectedRow = FindOperationRow(window, "selected");
        Assert.AreEqual(0, window.JournalGrid.Items.IndexOf(selectedRow));
        window.JournalGrid.SelectedItem = selectedRow;

        MethodInfo refreshViewMethod = typeof(OperationCenterWindow).GetMethod(
            "RefreshView",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到操作中心刷新入口。");
        refreshViewMethod.Invoke(window, null);

        Assert.AreEqual(1, window.JournalGrid.Items.SortDescriptions.Count);
        Assert.AreEqual(
            sortDescription.PropertyName,
            window.JournalGrid.Items.SortDescriptions[0].PropertyName);
        Assert.AreEqual(
            sortDescription.Direction,
            window.JournalGrid.Items.SortDescriptions[0].Direction);
        Assert.AreEqual(sortDescription.Direction, kindColumn.SortDirection);
        Assert.AreEqual("selected", GetOperationRowId(window.JournalGrid.Items[0]));
        Assert.AreEqual("selected", GetOperationRowId(window.JournalGrid.SelectedItem));
    }

    [TestMethod]
    [DataRow(true, true, true, true, true)]
    [DataRow(false, true, true, true, false)]
    [DataRow(true, false, true, true, false)]
    [DataRow(true, true, false, true, false)]
    [DataRow(true, true, true, false, false)]
    public void ShouldRestoreJournalGridFocus_RequiresFocusedRestoredRowAndLiveGrid(
        bool selectedRowHadKeyboardFocus,
        bool sameEntryRestored,
        bool windowIsVisible,
        bool gridIsEnabled,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            OperationCenterWindow.ShouldRestoreJournalGridFocus(
                selectedRowHadKeyboardFocus,
                sameEntryRestored,
                windowIsVisible,
                gridIsEnabled));
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

    private static FileOperationJournalEntry NewJournalEntry(
        string id,
        DateTime requestedUtc,
        FileOperationJournalKind kind,
        string displayName) => new()
        {
            Id = id,
            BatchId = "operation-center-sort-test",
            RequestedUtc = requestedUtc,
            Kind = kind,
            DisplayName = displayName
        };

    private static object FindOperationRow(
        OperationCenterWindow window,
        string id) => window.JournalGrid.Items
            .Cast<object>()
            .Single(row => GetOperationRowId(row).Equals(
                id,
                StringComparison.OrdinalIgnoreCase));

    private static string GetOperationRowId(object? row) =>
        row?.GetType()
            .GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)?
            .GetValue(row) as string
        ?? throw new AssertFailedException("操作中心行缺少账本 ID。");

    private static XElement FindNamedElement(
        XDocument document,
        XNamespace xaml,
        string name) =>
        document.Descendants().Single(element =>
            string.Equals(
                (string?)element.Attribute(xaml + "Name"),
                name,
                StringComparison.Ordinal));

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
