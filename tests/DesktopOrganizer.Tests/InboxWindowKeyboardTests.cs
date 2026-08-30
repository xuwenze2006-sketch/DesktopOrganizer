using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Xml.Linq;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class InboxWindowKeyboardTests
{
    [TestMethod]
    [DataRow(Key.Enter, ModifierKeys.None, (int)InboxKeyboardAction.AcceptSuggestion)]
    [DataRow(Key.Enter, ModifierKeys.Control, (int)InboxKeyboardAction.LeaveOnDesktop)]
    [DataRow(Key.Enter, ModifierKeys.Shift, (int)InboxKeyboardAction.Defer)]
    [DataRow(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift, (int)InboxKeyboardAction.None)]
    [DataRow(Key.Enter, ModifierKeys.Alt, (int)InboxKeyboardAction.None)]
    [DataRow(Key.Space, ModifierKeys.None, (int)InboxKeyboardAction.None)]
    public void ResolveKeyboardAction_RequiresExactEnterShortcut(
        Key key,
        ModifierKeys modifiers,
        int expected)
    {
        Assert.AreEqual(
            (InboxKeyboardAction)expected,
            InboxWindow.ResolveKeyboardAction(key, modifiers));
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
        StringAssert.Contains(inboxList.Attribute("ToolTip")?.Value, "F2");
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
