using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Runtime.CompilerServices;
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
    public void RuleEditor_WiresSaveShortcutOnlyInsideEditor()
    {
        XDocument document = LoadRuleManagerXaml();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement editor = FindNamedElement(document, xaml, "RuleEditorScroll");
        XElement ruleList = FindNamedElement(document, xaml, "RuleList");
        XElement saveButton = FindNamedElement(document, xaml, "SaveDraftButton");

        Assert.AreEqual(
            "RuleEditor_PreviewKeyDown",
            editor.Attribute("PreviewKeyDown")?.Value);
        Assert.IsNull(document.Root?.Attribute("PreviewKeyDown"));
        Assert.IsNull(ruleList.Attribute("PreviewKeyDown"));
        StringAssert.Contains(saveButton.Attribute("ToolTip")?.Value, "Ctrl+S");
        StringAssert.Contains(saveButton.Attribute("ToolTip")?.Value, "草稿");
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
