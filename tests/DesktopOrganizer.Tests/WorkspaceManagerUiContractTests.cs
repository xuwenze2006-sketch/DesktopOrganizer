using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
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

    [TestMethod]
    public void WorkspaceList_WiresDoubleClickActivation()
    {
        XDocument document = LoadWorkspaceManagerXaml();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement workspaceList = document.Descendants().Single(element =>
            string.Equals(
                (string?)element.Attribute(xaml + "Name"),
                "WorkspaceList",
                StringComparison.Ordinal));

        Assert.AreEqual(
            "WorkspaceList_MouseDoubleClick",
            workspaceList.Attribute("MouseDoubleClick")?.Value);
        Assert.AreEqual(
            "WorkspaceList_PreviewKeyDown",
            workspaceList.Attribute("PreviewKeyDown")?.Value);
        StringAssert.Contains(workspaceList.Attribute("ToolTip")?.Value, "F2");
        StringAssert.Contains(workspaceList.Attribute("ToolTip")?.Value, "Ctrl+D");
        StringAssert.Contains(workspaceList.Attribute("ToolTip")?.Value, "Delete");
        StringAssert.Contains(
            document.Descendants().Single(element =>
                element.Name.LocalName == "Button" &&
                string.Equals(
                    (string?)element.Attribute("Content"),
                    "新建当前快照",
                    StringComparison.Ordinal))
                .Attribute("ToolTip")?.Value,
            "Ctrl+N");
        StringAssert.Contains(
            document.Descendants().Single(element =>
                element.Name.LocalName == "Button" &&
                string.Equals(
                    (string?)element.Attribute("Content"),
                    "复制快照…",
                    StringComparison.Ordinal))
                .Attribute("ToolTip")?.Value,
            "Ctrl+D");
        StringAssert.Contains(
            document.Descendants().Single(element =>
                element.Name.LocalName == "Button" &&
                string.Equals(
                    (string?)element.Attribute("Content"),
                    "删除…",
                    StringComparison.Ordinal))
                .Attribute("ToolTip")?.Value,
            "Delete");
        Assert.AreEqual(
            "WorkspaceManagerWindow_PreviewKeyDown",
            document.Root?.Attribute("PreviewKeyDown")?.Value);
    }

    [TestMethod]
    [DataRow(Key.N, ModifierKeys.Control, false, true)]
    [DataRow(Key.N, ModifierKeys.Control, true, false)]
    [DataRow(Key.N, ModifierKeys.None, false, false)]
    [DataRow(Key.N, ModifierKeys.Control | ModifierKeys.Shift, false, false)]
    [DataRow(Key.N, ModifierKeys.Control | ModifierKeys.Alt, false, false)]
    [DataRow(Key.N, ModifierKeys.Alt, false, false)]
    [DataRow(Key.D, ModifierKeys.Control, false, false)]
    public void ShouldCreateFromKeyboard_RequiresExactInitialControlN(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            WorkspaceManagerWindow.ShouldCreateFromKeyboard(
                key,
                modifiers,
                isRepeat));
    }

    [TestMethod]
    [DataRow(Key.D, ModifierKeys.Control, false, true, true)]
    [DataRow(Key.D, ModifierKeys.Control, false, false, false)]
    [DataRow(Key.D, ModifierKeys.Control, true, true, false)]
    [DataRow(Key.D, ModifierKeys.None, false, true, false)]
    [DataRow(Key.D, ModifierKeys.Control | ModifierKeys.Shift, false, true, false)]
    [DataRow(Key.D, ModifierKeys.Alt, false, true, false)]
    [DataRow(Key.Enter, ModifierKeys.None, false, true, false)]
    [DataRow(Key.F2, ModifierKeys.None, false, true, false)]
    public void ShouldDuplicateFromKeyboard_RequiresExactInitialControlDOnSelection(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool hasSelection,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            WorkspaceManagerWindow.ShouldDuplicateFromKeyboard(
                key,
                modifiers,
                isRepeat,
                hasSelection));
    }

    [TestMethod]
    [DataRow(Key.Delete, ModifierKeys.None, false, true, true)]
    [DataRow(Key.Delete, ModifierKeys.None, false, false, false)]
    [DataRow(Key.Delete, ModifierKeys.None, true, true, false)]
    [DataRow(Key.Delete, ModifierKeys.Control, false, true, false)]
    [DataRow(Key.Delete, ModifierKeys.Shift, false, true, false)]
    [DataRow(Key.Delete, ModifierKeys.Alt, false, true, false)]
    [DataRow(Key.Delete, ModifierKeys.Control | ModifierKeys.Shift, false, true, false)]
    [DataRow(Key.Enter, ModifierKeys.None, false, true, false)]
    public void ShouldDeleteFromKeyboard_RequiresPlainInitialDeleteOnSelection(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool hasSelection,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            WorkspaceManagerWindow.ShouldDeleteFromKeyboard(
                key,
                modifiers,
                isRepeat,
                hasSelection));
    }

    [TestMethod]
    [DataRow(Key.F2, ModifierKeys.None, false, true, true)]
    [DataRow(Key.F2, ModifierKeys.None, false, false, false)]
    [DataRow(Key.F2, ModifierKeys.None, true, true, false)]
    [DataRow(Key.F2, ModifierKeys.Control, false, true, false)]
    [DataRow(Key.F2, ModifierKeys.Shift, false, true, false)]
    [DataRow(Key.F2, ModifierKeys.Alt, false, true, false)]
    [DataRow(Key.Enter, ModifierKeys.None, false, true, false)]
    public void ShouldRenameFromKeyboard_RequiresPlainNonRepeatF2OnSelection(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool hasSelection,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            WorkspaceManagerWindow.ShouldRenameFromKeyboard(
                key,
                modifiers,
                isRepeat,
                hasSelection));
    }

    [TestMethod]
    [DataRow(Key.Enter, ModifierKeys.None, true, false, true)]
    [DataRow(Key.Enter, ModifierKeys.None, false, false, false)]
    [DataRow(Key.Enter, ModifierKeys.None, true, true, false)]
    [DataRow(Key.Enter, ModifierKeys.Control, true, false, false)]
    [DataRow(Key.Enter, ModifierKeys.Shift, true, false, false)]
    [DataRow(Key.Space, ModifierKeys.None, true, false, false)]
    public void ShouldActivateFromKeyboard_RequiresPlainEnterOnInactiveSelection(
        Key key,
        ModifierKeys modifiers,
        bool hasSelection,
        bool isActive,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            WorkspaceManagerWindow.ShouldActivateFromKeyboard(
                key,
                modifiers,
                hasSelection,
                isActive));
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
