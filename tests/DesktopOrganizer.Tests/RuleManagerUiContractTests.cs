using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class RuleManagerUiContractTests
{
    [TestMethod]
    public void DisableAllRulesButton_ExposesBatchStopActionAndScope()
    {
        XDocument document = LoadRuleManagerXaml();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement button = document.Descendants().Single(element =>
            string.Equals(
                (string?)element.Attribute(xaml + "Name"),
                "DisableAllRulesButton",
                StringComparison.Ordinal));

        Assert.AreEqual("DisableAllRules_Click", button.Attribute("Click")?.Value);
        StringAssert.Contains(button.Attribute("ToolTip")?.Value, "保留已有虚拟整理结果");
        StringAssert.Contains(button.Attribute("ToolTip")?.Value, "不修改真实文件");
    }

    [TestMethod]
    [DataRow(Key.S, ModifierKeys.Control, false, true, true)]
    [DataRow(Key.S, ModifierKeys.Control, false, false, false)]
    [DataRow(Key.S, ModifierKeys.Control, true, true, false)]
    [DataRow(Key.S, ModifierKeys.None, false, true, false)]
    [DataRow(Key.S, ModifierKeys.Control | ModifierKeys.Shift, false, true, false)]
    [DataRow(Key.S, ModifierKeys.Control | ModifierKeys.Alt, false, true, false)]
    [DataRow(Key.S, ModifierKeys.Alt, false, true, false)]
    [DataRow(Key.Enter, ModifierKeys.Control, false, true, false)]
    public void ShouldSaveDraftFromKeyboard_RequiresExactInitialControlSForUnsavedEditor(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool hasUnsavedEditor,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            RuleManagerWindow.ShouldSaveDraftFromKeyboard(
                key,
                modifiers,
                isRepeat,
                hasUnsavedEditor));
    }

    [TestMethod]
    [DataRow(Key.N, ModifierKeys.Control, false, false, true)]
    [DataRow(Key.N, ModifierKeys.Control, false, true, false)]
    [DataRow(Key.N, ModifierKeys.Control, true, false, false)]
    [DataRow(Key.N, ModifierKeys.None, false, false, false)]
    [DataRow(Key.N, ModifierKeys.Control | ModifierKeys.Shift, false, false, false)]
    [DataRow(Key.N, ModifierKeys.Control | ModifierKeys.Alt, false, false, false)]
    [DataRow(Key.N, ModifierKeys.Alt, false, false, false)]
    [DataRow(Key.S, ModifierKeys.Control, false, false, false)]
    public void ShouldCreateRuleFromKeyboard_RequiresCleanEditorAndExactInitialControlN(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool hasUnsavedEditor,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            RuleManagerWindow.ShouldCreateRuleFromKeyboard(
                key,
                modifiers,
                isRepeat,
                hasUnsavedEditor));
    }

    [TestMethod]
    [DataRow(false, "rule-id", false)]
    [DataRow(true, "rule-id", true)]
    [DataRow(false, null, true)]
    [DataRow(false, "", true)]
    [DataRow(false, "   ", true)]
    [DataRow(true, null, true)]
    public void HasUnsavedRuleEditor_RequiresCleanPersistedRule(
        bool editorDirty,
        string? editingRuleId,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            RuleManagerWindow.HasUnsavedRuleEditor(
                editorDirty,
                editingRuleId));
    }

    [STATestMethod]
    public void RuleEditorCommandButtons_FollowUnsavedEditorState()
    {
        var mainWindow = new MainWindow(startQuietly: false);
        FieldInfo appLayoutField = typeof(MainWindow).GetField(
            "_appLayout",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到当前布局。");
        var layout = (AppLayoutData)(appLayoutField.GetValue(mainWindow)
            ?? throw new AssertFailedException("当前布局尚未初始化。"));
        layout.UserRules.Add(new UserOrganizationRuleInfo
        {
            Id = "clean-enabled-rule",
            Name = "已启用规则",
            Lifecycle = UserRuleLifecycle.Enabled,
            Extensions = [".txt"],
            ActionKind = OrganizationRuleActionKind.SendToInbox
        });
        var persistedWindow = new RuleManagerWindow(mainWindow);

        Assert.IsFalse(persistedWindow.SaveDraftButton.IsEnabled);
        Assert.IsTrue(persistedWindow.NewRuleButton.IsEnabled);
        Assert.IsTrue(persistedWindow.DisableAllRulesButton.IsEnabled);

        persistedWindow.RuleNameBox.Text = "已修改规则";

        Assert.IsTrue(persistedWindow.SaveDraftButton.IsEnabled);
        Assert.IsFalse(persistedWindow.NewRuleButton.IsEnabled);
        Assert.IsFalse(persistedWindow.DisableAllRulesButton.IsEnabled);

        var newRuleWindow = new RuleManagerWindow(mainWindow);
        newRuleWindow.NewRuleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.IsNull(newRuleWindow.RuleList.SelectedItem);
        Assert.IsTrue(newRuleWindow.SaveDraftButton.IsEnabled);
        Assert.IsFalse(newRuleWindow.NewRuleButton.IsEnabled);
        Assert.IsFalse(newRuleWindow.DisableAllRulesButton.IsEnabled);
    }

    [TestMethod]
    [DataRow(Key.Enter, ModifierKeys.None, false, true, false, false, false, (int)RuleListKeyboardAction.Preview)]
    [DataRow(Key.Enter, ModifierKeys.None, false, false, true, false, false, (int)RuleListKeyboardAction.ExecuteOnce)]
    [DataRow(Key.Enter, ModifierKeys.None, false, false, false, true, false, (int)RuleListKeyboardAction.Enable)]
    [DataRow(Key.Enter, ModifierKeys.None, false, false, false, false, true, (int)RuleListKeyboardAction.Disable)]
    [DataRow(Key.Enter, ModifierKeys.None, false, false, false, false, false, (int)RuleListKeyboardAction.None)]
    [DataRow(Key.Enter, ModifierKeys.None, false, true, true, false, false, (int)RuleListKeyboardAction.None)]
    [DataRow(Key.Enter, ModifierKeys.None, false, false, false, true, true, (int)RuleListKeyboardAction.None)]
    [DataRow(Key.Enter, ModifierKeys.None, true, true, false, false, false, (int)RuleListKeyboardAction.None)]
    [DataRow(Key.Enter, ModifierKeys.Control, false, true, false, false, false, (int)RuleListKeyboardAction.None)]
    [DataRow(Key.Enter, ModifierKeys.Shift, false, true, false, false, false, (int)RuleListKeyboardAction.None)]
    [DataRow(Key.Enter, ModifierKeys.Alt, false, true, false, false, false, (int)RuleListKeyboardAction.None)]
    [DataRow(Key.Space, ModifierKeys.None, false, true, false, false, false, (int)RuleListKeyboardAction.None)]
    public void ResolveRuleListKeyboardAction_RequiresPlainInitialEnterAndOneAvailableAction(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool canPreview,
        bool canExecuteOnce,
        bool canEnable,
        bool canDisable,
        int expected)
    {
        Assert.AreEqual(
            (RuleListKeyboardAction)expected,
            RuleManagerWindow.ResolveRuleListKeyboardAction(
                key,
                modifiers,
                isRepeat,
                canPreview,
                canExecuteOnce,
                canEnable,
                canDisable));
    }

    [TestMethod]
    [DataRow(Key.Delete, ModifierKeys.None, false, true, true)]
    [DataRow(Key.Delete, ModifierKeys.None, false, false, false)]
    [DataRow(Key.Delete, ModifierKeys.None, true, true, false)]
    [DataRow(Key.Delete, ModifierKeys.Control, false, true, false)]
    [DataRow(Key.Delete, ModifierKeys.Shift, false, true, false)]
    [DataRow(Key.Delete, ModifierKeys.Alt, false, true, false)]
    [DataRow(Key.Delete, ModifierKeys.Windows, false, true, false)]
    [DataRow(Key.Back, ModifierKeys.None, false, true, false)]
    public void ShouldDeleteRuleFromKeyboard_RequiresExactInitialDeleteAndAvailableRule(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool canDelete,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            RuleManagerWindow.ShouldDeleteRuleFromKeyboard(
                key,
                modifiers,
                isRepeat,
                canDelete));
    }

    [TestMethod]
    [DataRow(Key.F2, ModifierKeys.None, false, true, true)]
    [DataRow(Key.F2, ModifierKeys.None, false, false, false)]
    [DataRow(Key.F2, ModifierKeys.None, true, true, false)]
    [DataRow(Key.F2, ModifierKeys.Control, false, true, false)]
    [DataRow(Key.F2, ModifierKeys.Shift, false, true, false)]
    [DataRow(Key.F2, ModifierKeys.Alt, false, true, false)]
    [DataRow(Key.F2, ModifierKeys.Windows, false, true, false)]
    [DataRow(Key.Enter, ModifierKeys.None, false, true, false)]
    public void ShouldFocusRuleNameFromKeyboard_RequiresExactInitialF2AndEditableSelection(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool canEditSelectedRuleName,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            RuleManagerWindow.ShouldFocusRuleNameFromKeyboard(
                key,
                modifiers,
                isRepeat,
                canEditSelectedRuleName));
    }

    [TestMethod]
    [DataRow(true, true, true)]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    [DataRow(false, false, false)]
    public void ShouldRestoreRuleListFocus_RequiresVisibleWindowAndEnabledList(
        bool windowIsVisible,
        bool listIsEnabled,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            RuleManagerWindow.ShouldRestoreRuleListFocusAfterKeyboardAction(
                windowIsVisible,
                listIsEnabled));
    }

    [TestMethod]
    public void RuleWindowAndRuleList_WireShortcutsOnlyToTheirScopes()
    {
        XDocument document = LoadRuleManagerXaml();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement editor = FindNamedElement(document, xaml, "RuleEditorScroll");
        XElement ruleList = FindNamedElement(document, xaml, "RuleList");
        XElement saveButton = FindNamedElement(document, xaml, "SaveDraftButton");
        XElement deleteButton = FindNamedElement(document, xaml, "DeleteButton");
        XElement newRuleButton = FindNamedElement(document, xaml, "NewRuleButton");

        Assert.IsNull(editor.Attribute("PreviewKeyDown"));
        Assert.AreEqual(
            "RuleManagerWindow_PreviewKeyDown",
            document.Root?.Attribute("PreviewKeyDown")?.Value);
        Assert.AreEqual(
            "RuleList_PreviewKeyDown",
            ruleList.Attribute("PreviewKeyDown")?.Value);
        StringAssert.Contains(ruleList.Attribute("ToolTip")?.Value, "Enter");
        StringAssert.Contains(ruleList.Attribute("ToolTip")?.Value, "连续");
        StringAssert.Contains(ruleList.Attribute("ToolTip")?.Value, "F2 编辑所选规则名称");
        StringAssert.Contains(ruleList.Attribute("ToolTip")?.Value, "Delete");
        StringAssert.Contains(saveButton.Attribute("ToolTip")?.Value, "Ctrl+S");
        StringAssert.Contains(saveButton.Attribute("ToolTip")?.Value, "窗口任意焦点");
        StringAssert.Contains(saveButton.Attribute("ToolTip")?.Value, "仅有未保存内容时可用");
        StringAssert.Contains(saveButton.Attribute("ToolTip")?.Value, "草稿");
        Assert.AreEqual("新建规则", newRuleButton.Attribute("Content")?.Value);
        Assert.AreEqual("NewRule_Click", newRuleButton.Attribute("Click")?.Value);
        StringAssert.Contains(newRuleButton.Attribute("ToolTip")?.Value, "Ctrl+N");
        StringAssert.Contains(newRuleButton.Attribute("ToolTip")?.Value, "已保存");
        StringAssert.Contains(deleteButton.Attribute("ToolTip")?.Value, "Delete");
        Assert.AreEqual("Delete_Click", deleteButton.Attribute("Click")?.Value);
        foreach (string buttonName in new[]
                 {
                     "PreviewButton",
                     "ExecuteOnceButton",
                     "EnableButton",
                     "DisableButton"
                 })
        {
            StringAssert.Contains(
                FindNamedElement(document, xaml, buttonName).Attribute("ToolTip")?.Value,
                "Enter");
        }
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

    private static XDocument LoadRuleManagerXaml(
        [CallerFilePath] string sourceFilePath = "")
    {
        string testDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Cannot resolve the test source directory.");
        string projectRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", ".."));
        return XDocument.Load(Path.Combine(projectRoot, "RuleManagerWindow.xaml"));
    }
}
