using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
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
        AppLayoutData layout = GetAppLayout(mainWindow);
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

    [STATestMethod]
    public void AcceptAllReliable_PreservesCleanSelectionWhenNothingCanBeAccepted()
    {
        var mainWindow = new MainWindow(startQuietly: false);
        AppLayoutData layout = GetAppLayout(mainWindow);
        layout.InboxItems.Clear();
        AddInboxItem(layout, "a-first.txt", 0);
        AddInboxItem(layout, "b-selected.txt", 1);
        var window = new InboxWindow(mainWindow);
        window.InboxList.SelectedIndex = 1;

        InvokeAcceptAllReliable(window);

        Assert.AreEqual("b-selected.txt", GetSelectedName(window));
        Assert.AreEqual(1, window.InboxList.SelectedIndex);
        StringAssert.Contains(window.StatusText.Text, "没有可批量接受");
    }

    [STATestMethod]
    public void AcceptAllReliable_WithUnsavedTags_DoesNotRefreshOrDiscardDraft()
    {
        var mainWindow = new MainWindow(startQuietly: false);
        AppLayoutData layout = GetAppLayout(mainWindow);
        const string displayName = "draft.txt";
        PrepareActionableInboxItem(layout, displayName);
        var window = new InboxWindow(mainWindow);
        object itemsSource = window.InboxList.ItemsSource;
        object selectedItem = window.InboxList.SelectedItem;
        Assert.IsTrue(window.AcceptAllReliableButton.IsEnabled);
        window.TagEditorBox.Text = "未保存草稿";

        InvokeAcceptAllReliable(window);

        Assert.AreSame(itemsSource, window.InboxList.ItemsSource);
        Assert.AreSame(selectedItem, window.InboxList.SelectedItem);
        Assert.AreEqual("未保存草稿", window.TagEditorBox.Text);
        Assert.IsTrue(layout.InboxItems.ContainsKey(displayName));
        Assert.AreEqual(InboxReviewState.Pending, layout.InboxItems[displayName].ReviewState);
        Assert.IsEmpty(layout.Groups);
        StringAssert.Contains(window.StatusText.Text, "Ctrl+S");
    }

    [STATestMethod]
    [DataRow((int)InboxKeyboardAction.AcceptSuggestion)]
    [DataRow((int)InboxKeyboardAction.LeaveOnDesktop)]
    [DataRow((int)InboxKeyboardAction.Defer)]
    public void SelectedAction_WithUnsavedTags_DoesNotRunOrDiscardDraft(int actionValue)
    {
        var mainWindow = new MainWindow(startQuietly: false);
        AppLayoutData layout = GetAppLayout(mainWindow);
        const string displayName = "draft.txt";
        PrepareActionableInboxItem(layout, displayName);
        var window = new InboxWindow(mainWindow);
        object itemsSource = window.InboxList.ItemsSource;
        object selectedItem = window.InboxList.SelectedItem;
        window.TagEditorBox.Text = "未保存草稿";

        InvokeSelectedAction(window, (InboxKeyboardAction)actionValue);

        Assert.AreSame(itemsSource, window.InboxList.ItemsSource);
        Assert.AreSame(selectedItem, window.InboxList.SelectedItem);
        Assert.AreEqual("未保存草稿", window.TagEditorBox.Text);
        Assert.IsTrue(layout.InboxItems.ContainsKey(displayName));
        Assert.AreEqual(InboxReviewState.Pending, layout.InboxItems[displayName].ReviewState);
        Assert.IsEmpty(layout.Groups);
        CollectionAssert.AreEqual(new[] { "原标签" }, layout.ItemTags[displayName]);
        StringAssert.Contains(window.StatusText.Text, "Ctrl+S");
    }

    [STATestMethod]
    public void SelectedAction_WithCleanTags_StillRunsAndRefreshes()
    {
        var mainWindow = new MainWindow(startQuietly: false);
        AppLayoutData layout = GetAppLayout(mainWindow);
        const string displayName = "clean.txt";
        PrepareActionableInboxItem(layout, displayName);
        layout.InboxItems[displayName].ReviewState = InboxReviewState.Deferred;
        var window = new InboxWindow(mainWindow);
        object itemsSource = window.InboxList.ItemsSource;
        Assert.AreEqual("原标签", window.TagEditorBox.Text);

        InvokeSelectedAction(window, InboxKeyboardAction.Defer);

        Assert.AreNotSame(itemsSource, window.InboxList.ItemsSource);
        Assert.AreEqual(displayName, GetSelectedName(window));
        Assert.AreEqual("原标签", window.TagEditorBox.Text);
        Assert.AreEqual(InboxReviewState.Deferred, layout.InboxItems[displayName].ReviewState);
        StringAssert.Contains(window.StatusText.Text, "以后处理");
    }

    [STATestMethod]
    public void MoveToManualGroup_WithUnsavedTags_DoesNotRunOrDiscardDraft()
    {
        var mainWindow = new MainWindow(startQuietly: false);
        AppLayoutData layout = GetAppLayout(mainWindow);
        const string displayName = "draft.txt";
        PrepareActionableInboxItem(layout, displayName);
        var targetGroup = new GroupInfo { Id = "manual", Name = "手工分组" };
        layout.Groups.Add(targetGroup);
        var window = new InboxWindow(mainWindow);
        object itemsSource = window.InboxList.ItemsSource;
        object selectedItem = window.InboxList.SelectedItem;
        Assert.AreEqual(0, window.ManualGroupSelector.SelectedIndex);
        window.TagEditorBox.Text = "未保存草稿";

        InvokeMoveToManualGroup(window);

        Assert.AreSame(itemsSource, window.InboxList.ItemsSource);
        Assert.AreSame(selectedItem, window.InboxList.SelectedItem);
        Assert.AreEqual("未保存草稿", window.TagEditorBox.Text);
        Assert.IsTrue(layout.InboxItems.ContainsKey(displayName));
        Assert.IsEmpty(targetGroup.ItemNames);
        Assert.IsEmpty(targetGroup.ManuallyAssignedItemNames);
        CollectionAssert.AreEqual(new[] { "原标签" }, layout.ItemTags[displayName]);
        StringAssert.Contains(window.StatusText.Text, "Ctrl+S");
    }

    [STATestMethod]
    public void RefreshAfterBulkAccept_UsesPreviousOrderForSurvivorAndAdjacentFallback()
    {
        var mainWindow = new MainWindow(startQuietly: false);
        AppLayoutData layout = GetAppLayout(mainWindow);
        layout.InboxItems.Clear();
        AddInboxItem(layout, "a-first.txt", 0);
        AddInboxItem(layout, "b-middle.txt", 1);
        AddInboxItem(layout, "c-last.txt", 2);
        var window = new InboxWindow(mainWindow);
        string[] previousOrder = ["a-first.txt", "b-middle.txt", "c-last.txt"];

        window.InboxList.SelectedIndex = 1;
        layout.InboxItems.Remove("a-first.txt");
        InvokeRefresh(window, null, 1, previousOrder);
        Assert.AreEqual("b-middle.txt", GetSelectedName(window));
        Assert.AreEqual(0, window.InboxList.SelectedIndex);

        layout.InboxItems.Remove("b-middle.txt");
        InvokeRefresh(window, null, 1, previousOrder);
        Assert.AreEqual("c-last.txt", GetSelectedName(window));
        Assert.AreEqual(0, window.InboxList.SelectedIndex);

        AddInboxItem(layout, "d-tail.txt", 3);
        InvokeRefresh(window, "d-tail.txt", 1);
        layout.InboxItems.Remove("d-tail.txt");
        InvokeRefresh(window, null, 1, ["c-last.txt", "d-tail.txt"]);
        Assert.AreEqual("c-last.txt", GetSelectedName(window));
        Assert.AreEqual(0, window.InboxList.SelectedIndex);
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
    public void ResolvePostBulkActionSelectionName_PrefersSelectedThenNextThenPrevious()
    {
        string[] previousOrder = ["r1", "r2", "l1", "l2"];

        Assert.AreEqual(
            "r2",
            InboxWindow.ResolvePostBulkActionSelectionName(
                previousOrder,
                1,
                ["r2", "l1", "l2"]));
        Assert.AreEqual(
            "l1",
            InboxWindow.ResolvePostBulkActionSelectionName(
                previousOrder,
                1,
                ["l1", "l2"]));
        Assert.AreEqual(
            "l1",
            InboxWindow.ResolvePostBulkActionSelectionName(
                ["l1", "r1", "r2"],
                2,
                ["L1"]));
        Assert.IsNull(InboxWindow.ResolvePostBulkActionSelectionName(
            previousOrder,
            1,
            []));
        Assert.IsNull(InboxWindow.ResolvePostBulkActionSelectionName(
            previousOrder,
            -1,
            ["l1"]));
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

    private static AppLayoutData GetAppLayout(MainWindow mainWindow)
    {
        FieldInfo appLayoutField = typeof(MainWindow).GetField(
            "_appLayout",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到当前布局。");
        return (AppLayoutData)(appLayoutField.GetValue(mainWindow)
            ?? throw new AssertFailedException("当前布局尚未初始化。"));
    }

    private static void AddInboxItem(
        AppLayoutData layout,
        string displayName,
        int detectedSeconds)
    {
        layout.InboxItems[displayName] = new InboxItemInfo
        {
            DetectedUtc = DateTime.UnixEpoch.AddSeconds(detectedSeconds),
            UpdatedUtc = DateTime.UnixEpoch.AddSeconds(detectedSeconds),
            SuggestedCategoryName = "文档",
            MatchReason = "测试",
            Reliability = ClassificationReliability.Conservative,
            ReviewState = InboxReviewState.Pending
        };
    }

    private static void PrepareActionableInboxItem(
        AppLayoutData layout,
        string displayName)
    {
        layout.InboxItems.Clear();
        layout.ItemIdentities.Clear();
        layout.ItemTags.Clear();
        layout.Groups.Clear();
        layout.FreeIcons.Clear();
        layout.AutoClassificationOriginalPositions.Clear();
        var persistedIdentity = new DesktopItemIdentityInfo
        {
            LastKnownPath = $@"C:\Desktop\{displayName}",
            FileId = $"volume:{displayName}",
            CreationTimeUtcTicks = DateTime.UnixEpoch.Ticks
        };
        layout.InboxItems[displayName] = new InboxItemInfo
        {
            Identity = persistedIdentity,
            DetectedUtc = DateTime.UnixEpoch,
            UpdatedUtc = DateTime.UnixEpoch,
            SuggestedCategoryKey = "documents",
            SuggestedCategoryName = "文档",
            SuggestedCategoryOrder = 40,
            MatchReason = "扩展名 .txt",
            Reliability = ClassificationReliability.Reliable,
            ReviewState = InboxReviewState.Pending
        };
        layout.ItemIdentities[displayName] = new DesktopItemIdentityInfo
        {
            LastKnownPath = persistedIdentity.LastKnownPath,
            FileId = persistedIdentity.FileId,
            CreationTimeUtcTicks = persistedIdentity.CreationTimeUtcTicks
        };
        layout.ItemTags[displayName] = ["原标签"];
    }

    private static void InvokeAcceptAllReliable(InboxWindow window)
    {
        MethodInfo handler = typeof(InboxWindow).GetMethod(
            "AcceptAllReliable_Click",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到批量接受处理器。");
        handler.Invoke(window, [window.AcceptAllReliableButton, new RoutedEventArgs()]);
    }

    private static void InvokeSelectedAction(
        InboxWindow window,
        InboxKeyboardAction action)
    {
        MethodInfo method = typeof(InboxWindow).GetMethod(
            "ExecuteSelectedAction",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到单项收件箱动作入口。");
        method.Invoke(window, [action]);
    }

    private static void InvokeMoveToManualGroup(InboxWindow window)
    {
        MethodInfo handler = typeof(InboxWindow).GetMethod(
            "MoveToManualGroup_Click",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到加入手工分组处理器。");
        handler.Invoke(window, [window, new RoutedEventArgs()]);
    }

    private static void InvokeRefresh(
        InboxWindow window,
        string? selectedName,
        int fallbackIndex,
        IReadOnlyList<string>? previousDisplayOrder = null)
    {
        MethodInfo refresh = typeof(InboxWindow).GetMethod(
            "Refresh",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到收件箱刷新方法。");
        refresh.Invoke(window, [selectedName, fallbackIndex, previousDisplayOrder]);
    }

    private static string GetSelectedName(InboxWindow window) =>
        (window.InboxList.SelectedItem as InboxListItemView)?.DisplayName
        ?? throw new AssertFailedException("收件箱刷新后没有选中项目。");

    private static XDocument LoadInboxWindowXaml(
        [CallerFilePath] string sourceFilePath = "")
    {
        string testDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Cannot resolve the test source directory.");
        string projectRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", ".."));
        return XDocument.Load(Path.Combine(projectRoot, "InboxWindow.xaml"));
    }
}
