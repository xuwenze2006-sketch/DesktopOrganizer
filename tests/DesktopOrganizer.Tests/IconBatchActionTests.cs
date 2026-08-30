using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class IconBatchActionTests
{
    [TestMethod]
    public void RemoveCompletedRecycleItemsFromSelection_RemovesOnlySucceededItems()
    {
        var selectedItemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "success.txt",
            "failed.txt",
            "canceled.txt",
            "selected-later.txt"
        };

        MainWindow.RemoveCompletedRecycleItemsFromSelection(selectedItemNames, []);

        Assert.HasCount(4, selectedItemNames);

        MainWindow.RemoveCompletedRecycleItemsFromSelection(
            selectedItemNames,
            ["SUCCESS.TXT"]);

        Assert.HasCount(3, selectedItemNames);
        Assert.DoesNotContain("success.txt", selectedItemNames);
        Assert.Contains("failed.txt", selectedItemNames);
        Assert.Contains("canceled.txt", selectedItemNames);
        Assert.Contains("selected-later.txt", selectedItemNames);
    }

    [STATestMethod]
    public void RemoveSelectedItemsFromGroups_WhenNothingIsGrouped_PreservesSelection()
    {
        var window = new MainWindow(startQuietly: false);
        FieldInfo selectedItemNamesField = typeof(MainWindow).GetField(
            "_selectedItemNames",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到桌面项目选择集合。");
        var selectedItemNames = (HashSet<string>)(selectedItemNamesField.GetValue(window)
            ?? throw new AssertFailedException("桌面项目选择集合尚未初始化。"));
        string firstItemName = $"free-{Guid.NewGuid():N}-a";
        string secondItemName = $"free-{Guid.NewGuid():N}-b";
        selectedItemNames.Add(firstItemName);
        selectedItemNames.Add(secondItemName);

        MethodInfo removeSelectedItemsMethod = typeof(MainWindow).GetMethod(
            "RemoveSelectedItemsFromGroups",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到批量移出分类框动作。");

        removeSelectedItemsMethod.Invoke(window, null);

        Assert.HasCount(2, selectedItemNames);
        Assert.Contains(firstItemName, selectedItemNames);
        Assert.Contains(secondItemName, selectedItemNames);
        Assert.AreEqual("所选项目不在分类框中", window.StatusText.Text);
    }
}
