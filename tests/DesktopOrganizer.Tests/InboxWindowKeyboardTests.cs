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
    public void InboxWindow_WiresKeyboardHandlerOnlyToList()
    {
        XDocument document = LoadInboxWindowXaml();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement inboxList = FindNamedElement(document, xaml, "InboxList");
        XElement tagEditor = FindNamedElement(document, xaml, "TagEditorBox");
        XElement groupSelector = FindNamedElement(document, xaml, "ManualGroupSelector");

        Assert.AreEqual(
            "InboxList_PreviewKeyDown",
            inboxList.Attribute("PreviewKeyDown")?.Value);
        Assert.IsNull(document.Root?.Attribute("PreviewKeyDown"));
        Assert.IsNull(tagEditor.Attribute("PreviewKeyDown"));
        Assert.IsNull(groupSelector.Attribute("PreviewKeyDown"));
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
