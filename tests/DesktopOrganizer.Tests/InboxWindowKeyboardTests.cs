using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Xml.Linq;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class InboxWindowKeyboardTests
{
    [TestMethod]
    [DataRow(Key.Enter, ModifierKeys.None, false, (int)InboxKeyboardAction.AcceptSuggestion)]
    [DataRow(Key.Enter, ModifierKeys.Control, false, (int)InboxKeyboardAction.LeaveOnDesktop)]
    [DataRow(Key.Enter, ModifierKeys.Shift, false, (int)InboxKeyboardAction.Defer)]
    [DataRow(Key.Enter, ModifierKeys.None, true, (int)InboxKeyboardAction.None)]
    [DataRow(Key.Enter, ModifierKeys.Control, true, (int)InboxKeyboardAction.None)]
    [DataRow(Key.Enter, ModifierKeys.Shift, true, (int)InboxKeyboardAction.None)]
    [DataRow(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift, false, (int)InboxKeyboardAction.None)]
    [DataRow(Key.Enter, ModifierKeys.Alt, false, (int)InboxKeyboardAction.None)]
    [DataRow(Key.Space, ModifierKeys.None, false, (int)InboxKeyboardAction.None)]
    public void ResolveKeyboardAction_RequiresExactInitialEnterShortcut(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        int expected)
    {
        Assert.AreEqual(
            (InboxKeyboardAction)expected,
            InboxWindow.ResolveKeyboardAction(key, modifiers, isRepeat));
    }

    [TestMethod]
    [DataRow(Key.S, ModifierKeys.Control, false, true)]
    [DataRow(Key.S, ModifierKeys.None, false, false)]
    [DataRow(Key.S, ModifierKeys.Control, true, false)]
    [DataRow(Key.S, ModifierKeys.Control | ModifierKeys.Shift, false, false)]
    [DataRow(Key.S, ModifierKeys.Alt, false, false)]
    [DataRow(Key.Enter, ModifierKeys.Control, false, false)]
    public void ShouldSaveTagsFromKeyboard_RequiresExactNonRepeatControlS(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            InboxWindow.ShouldSaveTagsFromKeyboard(key, modifiers, isRepeat));
    }

    [TestMethod]
    [DataRow(Key.F2, ModifierKeys.None, false, true, true)]
    [DataRow(Key.F2, ModifierKeys.None, false, false, false)]
    [DataRow(Key.F2, ModifierKeys.None, true, true, false)]
    [DataRow(Key.F2, ModifierKeys.Control, false, true, false)]
    [DataRow(Key.F2, ModifierKeys.Shift, false, true, false)]
    [DataRow(Key.F2, ModifierKeys.Alt, false, true, false)]
    [DataRow(Key.F2, ModifierKeys.Windows, false, true, false)]
    [DataRow(Key.F3, ModifierKeys.None, false, true, false)]
    public void ShouldFocusTagsFromKeyboard_RequiresExactInitialF2AndEditableSelection(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool canEditTags,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            InboxWindow.ShouldFocusTagsFromKeyboard(
                key,
                modifiers,
                isRepeat,
                canEditTags));
    }

    [TestMethod]
    [DataRow(true, true, true)]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(false, false, false)]
    public void ShouldReturnFocusToInboxList_RequiresSuccessfulSaveAndSelection(
        bool saveSucceeded,
        bool hasSelection,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            InboxWindow.ShouldReturnFocusToInboxList(
                saveSucceeded,
                hasSelection));
    }

    [TestMethod]
    [DataRow(null, null, false)]
    [DataRow("", "", false)]
    [DataRow("原标签", "原标签", false)]
    [DataRow("原标签", "新标签", true)]
    [DataRow("原标签", "", true)]
    [DataRow("", "新标签", true)]
    public void HasUnsavedTagEditorText_ComparesAgainstLoadedText(
        string? loadedText,
        string? currentText,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            InboxWindow.HasUnsavedTagEditorText(loadedText, currentText));
    }

    [STATestMethod]
    public void InboxSelectionChanged_WithUnsavedTags_RestoresOriginalItem()
    {
        var mainWindow = new MainWindow(startQuietly: false);
        FieldInfo appLayoutField = typeof(MainWindow).GetField(
            "_appLayout",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到当前布局。");
        var layout = (AppLayoutData)(appLayoutField.GetValue(mainWindow)
            ?? throw new AssertFailedException("当前布局尚未初始化。"));
        layout.InboxItems.Clear();
        layout.ItemTags.Clear();
        layout.InboxItems["a-first.txt"] = new InboxItemInfo
        {
            DetectedUtc = DateTime.UnixEpoch,
            UpdatedUtc = DateTime.UnixEpoch,
            SuggestedCategoryName = "文档",
            MatchReason = "测试",
            ReviewState = InboxReviewState.Pending
        };
        layout.InboxItems["b-second.txt"] = new InboxItemInfo
        {
            DetectedUtc = DateTime.UnixEpoch.AddSeconds(1),
            UpdatedUtc = DateTime.UnixEpoch.AddSeconds(1),
            SuggestedCategoryName = "文档",
            MatchReason = "测试",
            ReviewState = InboxReviewState.Pending
        };
        layout.ItemTags["a-first.txt"] = ["原标签"];
        layout.ItemTags["b-second.txt"] = ["第二项标签"];
        var window = new InboxWindow(mainWindow);
        object originalSelection = window.InboxList.SelectedItem;

        window.TagEditorBox.Text = "未保存草稿";
        window.InboxList.SelectedIndex = 1;

        Assert.AreSame(originalSelection, window.InboxList.SelectedItem);
        Assert.AreEqual(0, window.InboxList.SelectedIndex);
        Assert.AreEqual("未保存草稿", window.TagEditorBox.Text);
        StringAssert.Contains(window.StatusText.Text, "Ctrl+S");

        window.TagEditorBox.Text = "原标签";
        window.InboxList.SelectedIndex = 1;

        Assert.AreEqual(1, window.InboxList.SelectedIndex);
        Assert.AreEqual("第二项标签", window.TagEditorBox.Text);
        Assert.AreEqual(string.Empty, window.StatusText.Text);
    }

    [TestMethod]
    [DataRow(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift, false, true, true)]
    [DataRow(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift, true, true, false)]
    [DataRow(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift, false, false, false)]
    [DataRow(Key.Enter, ModifierKeys.Control, false, true, false)]
    [DataRow(Key.Enter, ModifierKeys.Shift, false, true, false)]
    [DataRow(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt, false, true, false)]
    [DataRow(Key.Space, ModifierKeys.Control | ModifierKeys.Shift, false, true, false)]
    public void ShouldAcceptAllReliableFromKeyboard_RequiresExactInitialShortcutAndEnabledAction(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool canAcceptAll,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            InboxWindow.ShouldAcceptAllReliableFromKeyboard(
                key,
                modifiers,
                isRepeat,
                canAcceptAll));
    }

    [TestMethod]
    public void FindManualGroupSelectionIndex_FollowsStableIdAcrossRefresh()
    {
        ManualGroupChoice[] reorderedGroups =
        [
            new("other", "同名分组"),
            new("target", "同名分组")
        ];

        Assert.AreEqual(
            1,
            InboxWindow.FindManualGroupSelectionIndex(
                reorderedGroups,
                "TARGET"));
        Assert.AreEqual(
            0,
            InboxWindow.FindManualGroupSelectionIndex(
                [new("duplicate", "旧名称"), new("DUPLICATE", "新名称")],
                "duplicate"));
    }

    [TestMethod]
    public void FindManualGroupSelectionIndex_FallsBackToFirstOrNone()
    {
        ManualGroupChoice[] groups =
        [
            new("first", "第一组"),
            new("second", "第二组")
        ];

        Assert.AreEqual(0, InboxWindow.FindManualGroupSelectionIndex(groups, "missing"));
        Assert.AreEqual(0, InboxWindow.FindManualGroupSelectionIndex(groups, null));
        Assert.AreEqual(0, InboxWindow.FindManualGroupSelectionIndex(groups, "   "));
        Assert.AreEqual(
            -1,
            InboxWindow.FindManualGroupSelectionIndex([], "missing"));
    }

    [TestMethod]
    [DataRow(0, 4, 0)]
    [DataRow(2, 4, 2)]
    [DataRow(4, 4, 3)]
    [DataRow(99, 2, 1)]
    [DataRow(-1, 2, 0)]
    [DataRow(0, 0, -1)]
    public void ResolvePostActionSelectionIndex_ContinuesAtRemovedItemsPosition(
        int previousIndex,
        int itemCount,
        int expected)
    {
        Assert.AreEqual(
            expected,
            InboxWindow.ResolvePostActionSelectionIndex(
                previousIndex,
                itemCount));
    }

    [TestMethod]
    public void InboxWindow_WiresKeyboardHandlersOnlyToTheirTargetControls()
    {
        XDocument document = LoadInboxWindowXaml();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement inboxList = FindNamedElement(document, xaml, "InboxList");
        XElement tagEditor = FindNamedElement(document, xaml, "TagEditorBox");
        XElement saveTagsButton = FindNamedElement(document, xaml, "SaveTagsButton");
        XElement acceptAllReliableButton = FindNamedElement(document, xaml, "AcceptAllReliableButton");
        XElement groupSelector = FindNamedElement(document, xaml, "ManualGroupSelector");

        Assert.AreEqual(
            "InboxList_PreviewKeyDown",
            inboxList.Attribute("PreviewKeyDown")?.Value);
        string? inboxListToolTip = inboxList.Attribute("ToolTip")?.Value;
        StringAssert.Contains(inboxListToolTip, "Enter 接受建议");
        StringAssert.Contains(inboxListToolTip, "Ctrl+Enter 留在桌面");
        StringAssert.Contains(inboxListToolTip, "Shift+Enter 以后再说");
        StringAssert.Contains(inboxListToolTip, "Ctrl+Shift+Enter 接受全部可靠建议");
        StringAssert.Contains(inboxListToolTip, "F2 编辑所选项目的本地标签");
        Assert.AreEqual(
            "TagEditorBox_PreviewKeyDown",
            tagEditor.Attribute("PreviewKeyDown")?.Value);
        Assert.IsNull(document.Root?.Attribute("PreviewKeyDown"));
        Assert.IsNull(groupSelector.Attribute("PreviewKeyDown"));
        StringAssert.Contains(tagEditor.Attribute("ToolTip")?.Value, "Ctrl+S");
        StringAssert.Contains(tagEditor.Attribute("ToolTip")?.Value, "返回列表");
        StringAssert.Contains(saveTagsButton.Attribute("ToolTip")?.Value, "Ctrl+S");
        StringAssert.Contains(saveTagsButton.Attribute("ToolTip")?.Value, "返回列表");
        StringAssert.Contains(acceptAllReliableButton.Attribute("ToolTip")?.Value, "Ctrl+Shift+Enter");
        Assert.AreEqual("Enter", FindButton(document, "接受建议").Attribute("ToolTip")?.Value);
        Assert.AreEqual("Ctrl+Enter", FindButton(document, "留在桌面").Attribute("ToolTip")?.Value);
        Assert.AreEqual("Shift+Enter", FindButton(document, "以后再说").Attribute("ToolTip")?.Value);
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

    private static XDocument LoadInboxWindowXaml(
        [CallerFilePath] string sourceFilePath = "")
    {
        string testDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Cannot resolve the test source directory.");
        string projectRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", ".."));
        return XDocument.Load(Path.Combine(projectRoot, "InboxWindow.xaml"));
    }
}
