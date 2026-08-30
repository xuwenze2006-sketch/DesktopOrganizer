using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Reflection;
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
        StringAssert.Contains(workspaceList.Attribute("ToolTip")?.Value, "Ctrl+S");
        StringAssert.Contains(workspaceList.Attribute("ToolTip")?.Value, "Delete");
        StringAssert.Contains(workspaceList.Attribute("ToolTip")?.Value, "返回后可继续键盘操作");
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
                    "用当前布局覆盖…",
                    StringComparison.Ordinal))
                .Attribute("ToolTip")?.Value,
            "Ctrl+S");
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
    [DataRow(Key.S, ModifierKeys.Control, false, true, true)]
    [DataRow(Key.S, ModifierKeys.Control, false, false, false)]
    [DataRow(Key.S, ModifierKeys.Control, true, true, false)]
    [DataRow(Key.S, ModifierKeys.None, false, true, false)]
    [DataRow(Key.S, ModifierKeys.Control | ModifierKeys.Shift, false, true, false)]
    [DataRow(Key.S, ModifierKeys.Control | ModifierKeys.Alt, false, true, false)]
    [DataRow(Key.S, ModifierKeys.Alt, false, true, false)]
    [DataRow(Key.D, ModifierKeys.Control, false, true, false)]
    public void ShouldOverwriteFromKeyboard_RequiresExactInitialControlSOnSelection(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool hasSelection,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            WorkspaceManagerWindow.ShouldOverwriteFromKeyboard(
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

    [TestMethod]
    [DataRow(true, true, true)]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    [DataRow(false, false, false)]
    public void ShouldRestoreWorkspaceListFocus_RequiresVisibleWindowAndEnabledList(
        bool windowIsVisible,
        bool listIsEnabled,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            WorkspaceManagerWindow.ShouldRestoreWorkspaceListFocusAfterKeyboardAction(
                windowIsVisible,
                listIsEnabled));
    }

    [TestMethod]
    public void ResolvePostDeleteSelectionId_PrefersNextThenPrevious()
    {
        IReadOnlyList<string> workspaceIds = ["a", "b", "c"];

        Assert.AreEqual(
            "b",
            WorkspaceManagerWindow.ResolvePostDeleteSelectionId(workspaceIds, 0));
        Assert.AreEqual(
            "c",
            WorkspaceManagerWindow.ResolvePostDeleteSelectionId(workspaceIds, 1));
        Assert.AreEqual(
            "b",
            WorkspaceManagerWindow.ResolvePostDeleteSelectionId(workspaceIds, 2));
        Assert.IsNull(WorkspaceManagerWindow.ResolvePostDeleteSelectionId(["only"], 0));
        Assert.IsNull(WorkspaceManagerWindow.ResolvePostDeleteSelectionId([], 0));
        Assert.IsNull(WorkspaceManagerWindow.ResolvePostDeleteSelectionId(workspaceIds, -1));
        Assert.IsNull(WorkspaceManagerWindow.ResolvePostDeleteSelectionId(workspaceIds, 3));
    }

    [STATestMethod]
    public void WorkspaceCommandButtons_FollowSelectionAndActiveState()
    {
        var mainWindow = new MainWindow(startQuietly: false);
        FieldInfo appLayoutField = typeof(MainWindow).GetField(
            "_appLayout",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到当前布局。");
        var layout = (AppLayoutData)(appLayoutField.GetValue(mainWindow)
            ?? throw new AssertFailedException("当前布局尚未初始化。"));
        layout.Workspaces.Clear();
        layout.ActiveWorkspaceId = null;
        var emptyWindow = new WorkspaceManagerWindow(mainWindow);

        Assert.IsFalse(emptyWindow.DuplicateWorkspaceButton.IsEnabled);
        Assert.IsFalse(emptyWindow.ActivateWorkspaceButton.IsEnabled);
        Assert.IsFalse(emptyWindow.OverwriteWorkspaceButton.IsEnabled);
        Assert.IsFalse(emptyWindow.RenameWorkspaceButton.IsEnabled);
        Assert.IsFalse(emptyWindow.DeleteWorkspaceButton.IsEnabled);

        layout.Workspaces.Add(new WorkspaceProfileInfo
        {
            Id = "active",
            Name = "A 当前"
        });
        layout.Workspaces.Add(new WorkspaceProfileInfo
        {
            Id = "inactive",
            Name = "B 备用"
        });
        layout.ActiveWorkspaceId = "active";
        var populatedWindow = new WorkspaceManagerWindow(mainWindow);

        Assert.IsTrue(populatedWindow.DuplicateWorkspaceButton.IsEnabled);
        Assert.IsFalse(populatedWindow.ActivateWorkspaceButton.IsEnabled);
        Assert.IsTrue(populatedWindow.OverwriteWorkspaceButton.IsEnabled);
        Assert.IsTrue(populatedWindow.RenameWorkspaceButton.IsEnabled);
        Assert.IsTrue(populatedWindow.DeleteWorkspaceButton.IsEnabled);

        populatedWindow.WorkspaceList.SelectedIndex = 1;

        Assert.IsTrue(populatedWindow.DuplicateWorkspaceButton.IsEnabled);
        Assert.IsTrue(populatedWindow.ActivateWorkspaceButton.IsEnabled);
        Assert.IsTrue(populatedWindow.OverwriteWorkspaceButton.IsEnabled);
        Assert.IsTrue(populatedWindow.RenameWorkspaceButton.IsEnabled);
        Assert.IsTrue(populatedWindow.DeleteWorkspaceButton.IsEnabled);

        populatedWindow.WorkspaceList.SelectedIndex = -1;

        Assert.IsFalse(populatedWindow.DuplicateWorkspaceButton.IsEnabled);
        Assert.IsFalse(populatedWindow.ActivateWorkspaceButton.IsEnabled);
        Assert.IsFalse(populatedWindow.OverwriteWorkspaceButton.IsEnabled);
        Assert.IsFalse(populatedWindow.RenameWorkspaceButton.IsEnabled);
        Assert.IsFalse(populatedWindow.DeleteWorkspaceButton.IsEnabled);
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
