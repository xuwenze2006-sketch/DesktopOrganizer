using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Windows;
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

    [STATestMethod]
    public void OpeningMenuForUnselectedGroupedItem_ReplacesSelectionAndRangeAnchor()
    {
        var window = new MainWindow(startQuietly: false);
        var group = new GroupInfo
        {
            Id = "group",
            Name = "资料",
            ItemNames = ["A.txt", "B.txt", "C.txt", "D.txt", "E.txt"]
        };
        HashSet<string> selectedItemNames = GetField<HashSet<string>>(
            window,
            "_selectedItemNames");
        selectedItemNames.Add("A.txt");
        SetField(
            window,
            "_groupRangeSelectionAnchor",
            new GroupRangeSelectionAnchor(group.Id, "A.txt"));

        RaiseOpened(window.CreateIconContextMenu(
            @"C:\Desktop\C.txt",
            "C.txt",
            group));

        GroupRangeSelectionAnchor anchor = GetField<GroupRangeSelectionAnchor>(
            window,
            "_groupRangeSelectionAnchor");
        Assert.IsTrue(selectedItemNames.SetEquals(["C.txt"]));
        Assert.AreEqual(group.Id, anchor.GroupId);
        Assert.AreEqual("C.txt", anchor.ItemName);

        GroupRangeSelectionPlan plan = GroupRangeSelectionPolicy.CreatePlan(
            group.Id,
            group.ItemNames,
            selectedItemNames,
            anchor,
            "E.txt");
        Assert.IsTrue(plan.UsedAnchor);
        CollectionAssert.AreEqual(
            new[] { "C.txt", "D.txt", "E.txt" },
            plan.ItemNames.ToArray());
    }

    [STATestMethod]
    public void OpeningMenuForUnselectedFreeItem_ClearsGroupedRangeAnchor()
    {
        var window = new MainWindow(startQuietly: false);
        HashSet<string> selectedItemNames = GetField<HashSet<string>>(
            window,
            "_selectedItemNames");
        selectedItemNames.Add("Grouped.txt");
        SetField(
            window,
            "_groupRangeSelectionAnchor",
            new GroupRangeSelectionAnchor("group", "Grouped.txt"));

        RaiseOpened(window.CreateIconContextMenu(
            @"C:\Desktop\Free.txt",
            "Free.txt",
            parentGroup: null));

        Assert.IsTrue(selectedItemNames.SetEquals(["Free.txt"]));
        Assert.IsNull(GetNullableField<GroupRangeSelectionAnchor>(
            window,
            "_groupRangeSelectionAnchor"));
    }

    [STATestMethod]
    public void OpeningMenuForAlreadySelectedItem_PreservesMultiSelectionAndRangeAnchor()
    {
        var window = new MainWindow(startQuietly: false);
        var group = new GroupInfo { Id = "group", Name = "资料" };
        HashSet<string> selectedItemNames = GetField<HashSet<string>>(
            window,
            "_selectedItemNames");
        selectedItemNames.UnionWith(["B.txt", "C.txt"]);
        var originalAnchor = new GroupRangeSelectionAnchor(group.Id, "B.txt");
        SetField(window, "_groupRangeSelectionAnchor", originalAnchor);

        RaiseOpened(window.CreateIconContextMenu(
            @"C:\Desktop\C.txt",
            "C.txt",
            group));

        Assert.IsTrue(selectedItemNames.SetEquals(["B.txt", "C.txt"]));
        Assert.AreSame(
            originalAnchor,
            GetField<GroupRangeSelectionAnchor>(window, "_groupRangeSelectionAnchor"));
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

    private static void RaiseOpened(ContextMenu menu) =>
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));

    private static T GetField<T>(MainWindow window, string fieldName) where T : class =>
        GetNullableField<T>(window, fieldName)
        ?? throw new AssertFailedException($"Field {fieldName} was null.");

    private static T? GetNullableField<T>(MainWindow window, string fieldName) where T : class
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException($"Missing field {fieldName}.");
        return field.GetValue(window) as T;
    }

    private static void SetField(MainWindow window, string fieldName, object? value)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException($"Missing field {fieldName}.");
        field.SetValue(window, value);
    }
}
