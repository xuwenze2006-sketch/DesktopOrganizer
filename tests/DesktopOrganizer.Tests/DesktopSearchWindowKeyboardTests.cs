using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Xml.Linq;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopSearchWindowKeyboardTests
{
    [TestMethod]
    [DataRow(-1, 0, 1, -1)]
    [DataRow(-1, 3, 1, 0)]
    [DataRow(-1, 3, -1, 2)]
    [DataRow(0, 3, -1, 0)]
    [DataRow(0, 3, 1, 1)]
    [DataRow(2, 3, 1, 2)]
    [DataRow(5, 3, 1, 0)]
    public void GetNextSelectionIndex_ClampsAndHandlesMissingSelection(
        int currentIndex,
        int itemCount,
        int direction,
        int expected)
    {
        Assert.AreEqual(
            expected,
            DesktopSearchWindow.GetNextSelectionIndex(
                currentIndex,
                itemCount,
                direction));
    }

    [TestMethod]
    [DataRow(Key.Enter, ModifierKeys.None, (int)DesktopSearchKeyboardAction.Locate)]
    [DataRow(Key.Enter, ModifierKeys.Control, (int)DesktopSearchKeyboardAction.Open)]
    [DataRow(Key.Enter, ModifierKeys.Shift, (int)DesktopSearchKeyboardAction.Reveal)]
    [DataRow(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift, (int)DesktopSearchKeyboardAction.None)]
    [DataRow(Key.Enter, ModifierKeys.Alt, (int)DesktopSearchKeyboardAction.None)]
    [DataRow(Key.Escape, ModifierKeys.None, (int)DesktopSearchKeyboardAction.None)]
    public void ResolveKeyboardAction_RequiresExactEnterShortcut(
        Key key,
        ModifierKeys modifiers,
        int expected)
    {
        Assert.AreEqual(
            (DesktopSearchKeyboardAction)expected,
            DesktopSearchWindow.ResolveKeyboardAction(key, modifiers));
    }

    [TestMethod]
    [DataRow(Key.F, ModifierKeys.Control, false, true)]
    [DataRow(Key.F, ModifierKeys.Control, true, false)]
    [DataRow(Key.F, ModifierKeys.None, false, false)]
    [DataRow(Key.F, ModifierKeys.Control | ModifierKeys.Shift, false, false)]
    [DataRow(Key.F, ModifierKeys.Alt, false, false)]
    [DataRow(Key.Enter, ModifierKeys.Control, false, false)]
    public void ShouldFocusQueryFromKeyboard_RequiresExactInitialControlF(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            DesktopSearchWindow.ShouldFocusQueryFromKeyboard(
                key,
                modifiers,
                isRepeat));
    }

    [TestMethod]
    [DataRow((int)DesktopSearchKeyboardAction.Locate, true)]
    [DataRow((int)DesktopSearchKeyboardAction.Open, false)]
    [DataRow((int)DesktopSearchKeyboardAction.Reveal, false)]
    [DataRow((int)DesktopSearchKeyboardAction.None, false)]
    public void ShouldCloseAfterAction_OnlyReturnsToDesktopAfterLocate(
        int action,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            DesktopSearchWindow.ShouldCloseAfterAction((DesktopSearchKeyboardAction)action));
    }

    [TestMethod]
    [DataRow(MouseButton.Left, true, true)]
    [DataRow(MouseButton.Left, false, false)]
    [DataRow(MouseButton.Right, true, false)]
    [DataRow(MouseButton.Middle, true, false)]
    public void ShouldLocateFromResultDoubleClick_RequiresLeftButtonOnResultItem(
        MouseButton changedButton,
        bool isResultItem,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            DesktopSearchWindow.ShouldLocateFromResultDoubleClick(
                changedButton,
                isResultItem));
    }

    [STATestMethod]
    public void ResultActions_FollowCurrentSearchSelection()
    {
        var mainWindow = new MainWindow(startQuietly: false);
        FieldInfo desktopItemsField = typeof(MainWindow).GetField(
            "_desktopItems",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到已加载桌面项目集合。");
        var desktopItems = (Dictionary<string, string>)(desktopItemsField.GetValue(mainWindow)
            ?? throw new AssertFailedException("已加载桌面项目集合尚未初始化。"));
        string itemName = $"search-action-{Guid.NewGuid():N}.txt";
        desktopItems[itemName] = $@"C:\Desktop\{itemName}";
        var window = new DesktopSearchWindow(mainWindow);

        Assert.AreEqual(0, window.ResultsList.SelectedIndex);
        Assert.IsTrue(window.ResultActionPanel.IsEnabled);

        window.ResultsList.SelectedIndex = -1;
        Assert.IsFalse(window.ResultActionPanel.IsEnabled);

        window.ResultsList.SelectedIndex = 0;
        Assert.IsTrue(window.ResultActionPanel.IsEnabled);

        window.QueryBox.Text = $"missing-{Guid.NewGuid():N}";
        Assert.AreEqual(0, window.ResultsList.Items.Count);
        Assert.IsFalse(window.ResultActionPanel.IsEnabled);

        window.QueryBox.Text = string.Empty;
        Assert.AreEqual(1, window.ResultsList.Items.Count);
        Assert.IsTrue(window.ResultActionPanel.IsEnabled);
    }

    [TestMethod]
    public void SearchWindow_WiresWindowQueryShortcutAndControlActions()
    {
        XDocument document = LoadSearchWindowXaml();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement queryBox = document
            .Descendants()
            .Single(element =>
                string.Equals(
                    (string?)element.Attribute(xaml + "Name"),
                    "QueryBox",
                    StringComparison.Ordinal));
        XElement resultsList = document
            .Descendants()
            .Single(element =>
                string.Equals(
                    (string?)element.Attribute(xaml + "Name"),
                    "ResultsList",
                    StringComparison.Ordinal));
        XElement resultActionPanel = document
            .Descendants()
            .Single(element =>
                string.Equals(
                    (string?)element.Attribute(xaml + "Name"),
                    "ResultActionPanel",
                    StringComparison.Ordinal));

        Assert.AreEqual(
            "QueryBox_PreviewKeyDown",
            queryBox.Attribute("PreviewKeyDown")?.Value);
        Assert.AreEqual(
            "ResultsList_PreviewKeyDown",
            resultsList.Attribute("PreviewKeyDown")?.Value);
        Assert.AreEqual(
            "ResultsList_MouseDoubleClick",
            resultsList.Attribute("MouseDoubleClick")?.Value);
        Assert.AreEqual(
            "ResultsList_SelectionChanged",
            resultsList.Attribute("SelectionChanged")?.Value);
        Assert.AreEqual("False", resultActionPanel.Attribute("IsEnabled")?.Value);
        Assert.AreEqual(
            "DesktopSearchWindow_PreviewKeyDown",
            document.Root?.Attribute("PreviewKeyDown")?.Value);

        XElement locateButton = FindButton(document, "定位并高亮");
        XElement openButton = FindButton(document, "打开");
        XElement revealButton = FindButton(document, "在资源管理器中显示");
        Assert.AreEqual(resultActionPanel, locateButton.Parent);
        Assert.AreEqual(resultActionPanel, openButton.Parent);
        Assert.AreEqual(resultActionPanel, revealButton.Parent);
        Assert.AreEqual("Locate_Click", locateButton.Attribute("Click")?.Value);
        Assert.AreEqual("Open_Click", openButton.Attribute("Click")?.Value);
        Assert.AreEqual("Reveal_Click", revealButton.Attribute("Click")?.Value);
        Assert.AreEqual(
            "Enter（定位后返回桌面）",
            locateButton.Attribute("ToolTip")?.Value);
        Assert.AreEqual("Ctrl+Enter", openButton.Attribute("ToolTip")?.Value);
        Assert.AreEqual("Shift+Enter", revealButton.Attribute("ToolTip")?.Value);
        StringAssert.Contains(queryBox.Attribute("ToolTip")?.Value, "Enter 定位并返回桌面");
        StringAssert.Contains(queryBox.Attribute("ToolTip")?.Value, "Ctrl+Enter 打开");
        StringAssert.Contains(queryBox.Attribute("ToolTip")?.Value, "Shift+Enter");
        StringAssert.Contains(queryBox.Attribute("ToolTip")?.Value, "Ctrl+F");
        StringAssert.Contains(
            resultsList.Attribute("ToolTip")?.Value,
            "Ctrl+F 返回搜索框并全选查询文本");
        StringAssert.Contains(resultsList.Attribute("ToolTip")?.Value, "双击结果");
    }

    private static XElement FindButton(XDocument document, string content) =>
        document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button" &&
                string.Equals(
                    (string?)element.Attribute("Content"),
                    content,
                    StringComparison.Ordinal));

    private static XDocument LoadSearchWindowXaml(
        [CallerFilePath] string sourceFilePath = "")
    {
        string testDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Cannot resolve the test source directory.");
        string projectRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", ".."));
        return XDocument.Load(Path.Combine(projectRoot, "DesktopSearchWindow.xaml"));
    }
}
