using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows.Controls;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class IconTagMenuContractTests
{
    [STATestMethod]
    public void FreeAndGroupedIconMenusExposeLocalTagSummaryAndEditor()
    {
        var window = new MainWindow(startQuietly: false);

        AssertTagCommands(window.CreateIconContextMenu(
            @"C:\Desktop\item.txt",
            "item.txt",
            parentGroup: null));
        AssertTagCommands(window.CreateIconContextMenu(
            @"C:\Desktop\item.txt",
            "item.txt",
            new GroupInfo { Name = "资料" }));
    }

    private static void AssertTagCommands(ContextMenu menu)
    {
        List<MenuItem> items = menu.Items.OfType<MenuItem>().ToList();
        MenuItem summary = items.Single(item =>
            string.Equals(item.Header as string, "本地标签：无", StringComparison.Ordinal));
        MenuItem editor = items.Single(item =>
            string.Equals(item.Header as string, "编辑此项目的本地标签…", StringComparison.Ordinal));
        MenuItem batchAdd = items.Single(item =>
            string.Equals(item.Header as string, "为所选项目添加本地标签…", StringComparison.Ordinal));
        MenuItem batchRemove = items.Single(item =>
            string.Equals(item.Header as string, "从所选项目移除本地标签…", StringComparison.Ordinal));

        Assert.IsFalse(summary.IsEnabled);
        Assert.IsTrue(editor.IsEnabled);
        Assert.AreEqual(System.Windows.Visibility.Collapsed, batchAdd.Visibility);
        Assert.AreEqual(System.Windows.Visibility.Collapsed, batchRemove.Visibility);
    }
}
