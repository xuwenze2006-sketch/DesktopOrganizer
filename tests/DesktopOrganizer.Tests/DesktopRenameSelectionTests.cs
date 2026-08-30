using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections;
using System.IO;
using System.Reflection;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopRenameSelectionTests
{
    [STATestMethod]
    public void ApplyDesktopRenameBatch_MigratesRangeAnchorAndPreservesLaterShiftRange()
    {
        var window = new MainWindow(startQuietly: false);
        AppLayoutData layout = GetField<AppLayoutData>(window, "_appLayout");
        var group = new GroupInfo
        {
            Id = "group",
            ItemNames = ["A.txt", "B.txt", "C.txt"]
        };
        layout.Groups.Clear();
        layout.Groups.Add(group);

        HashSet<string> selectedItemNames = GetField<HashSet<string>>(window, "_selectedItemNames");
        selectedItemNames.Clear();
        selectedItemNames.Add("a.TXT");
        SetField(
            window,
            "_groupRangeSelectionAnchor",
            new GroupRangeSelectionAnchor("GROUP", "A.TXT"));

        Assert.IsTrue(ApplyRenameBatch(window, ("a.txt", "Renamed.txt")));

        GroupRangeSelectionAnchor migratedAnchor = GetField<GroupRangeSelectionAnchor>(
            window,
            "_groupRangeSelectionAnchor");
        Assert.AreEqual("group", migratedAnchor.GroupId, ignoreCase: true);
        Assert.AreEqual("Renamed.txt", migratedAnchor.ItemName);
        CollectionAssert.AreEqual(
            new[] { "Renamed.txt", "B.txt", "C.txt" },
            group.ItemNames);
        Assert.IsTrue(selectedItemNames.SetEquals(["Renamed.txt"]));

        GroupRangeSelectionPlan plan = GroupRangeSelectionPolicy.CreatePlan(
            group.Id,
            group.ItemNames,
            selectedItemNames,
            migratedAnchor,
            "C.txt");

        Assert.IsTrue(plan.UsedAnchor);
        CollectionAssert.AreEqual(
            new[] { "Renamed.txt", "B.txt", "C.txt" },
            plan.ItemNames.ToArray());

        Assert.IsTrue(ApplyRenameBatch(window, ("B.txt", "B2.txt")));
        GroupRangeSelectionAnchor anchorAfterUnrelatedRename = GetField<GroupRangeSelectionAnchor>(
            window,
            "_groupRangeSelectionAnchor");
        Assert.AreEqual("Renamed.txt", anchorAfterUnrelatedRename.ItemName);
    }

    [STATestMethod]
    public void ApplyDesktopRenameBatch_ClearsAnchorWhenRenameTargetReplacesAnchoredItem()
    {
        var window = new MainWindow(startQuietly: false);
        AppLayoutData layout = GetField<AppLayoutData>(window, "_appLayout");
        var group = new GroupInfo
        {
            Id = "group",
            ItemNames = ["Source.txt", "Occupied.txt", "Tail.txt"]
        };
        layout.Groups.Clear();
        layout.Groups.Add(group);

        HashSet<string> selectedItemNames = GetField<HashSet<string>>(window, "_selectedItemNames");
        selectedItemNames.Clear();
        selectedItemNames.Add("Occupied.txt");
        SetField(
            window,
            "_groupRangeSelectionAnchor",
            new GroupRangeSelectionAnchor("group", "Occupied.txt"));

        Assert.IsTrue(ApplyRenameBatch(window, ("Source.txt", "Occupied.txt")));

        Assert.IsNull(GetNullableField<GroupRangeSelectionAnchor>(
            window,
            "_groupRangeSelectionAnchor"));
        Assert.IsEmpty(selectedItemNames);
        CollectionAssert.AreEqual(
            new[] { "Occupied.txt", "Tail.txt" },
            group.ItemNames);
    }

    private static bool ApplyRenameBatch(
        MainWindow window,
        params (string OldName, string NewName)[] renames)
    {
        Type candidateType = typeof(MainWindow).GetNestedType(
            "DesktopItemRenameCandidate",
            BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到桌面重命名候选类型。");
        ConstructorInfo constructor = candidateType.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 5);
        Type listType = typeof(List<>).MakeGenericType(candidateType);
        var candidates = (IList)(Activator.CreateInstance(listType)
            ?? throw new AssertFailedException("无法创建桌面重命名候选列表。"));

        foreach ((string oldName, string newName) in renames)
        {
            candidates.Add(constructor.Invoke(
            [
                oldName,
                newName,
                Path.Combine(@"C:\Desktop", oldName),
                Path.Combine(@"C:\Desktop", newName),
                "test"
            ]));
        }

        MethodInfo applyMethod = typeof(MainWindow).GetMethod(
            "ApplyDesktopRenameBatch",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到桌面重命名迁移入口。");
        return (bool)(applyMethod.Invoke(window, [candidates])
            ?? throw new AssertFailedException("桌面重命名迁移未返回结果。"));
    }

    private static T GetField<T>(MainWindow window, string fieldName)
        where T : class
    {
        return GetNullableField<T>(window, fieldName)
            ?? throw new AssertFailedException($"字段 {fieldName} 尚未初始化。");
    }

    private static T? GetNullableField<T>(MainWindow window, string fieldName)
        where T : class
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException($"未找到字段 {fieldName}。");
        return field.GetValue(window) as T;
    }

    private static void SetField(MainWindow window, string fieldName, object? value)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException($"未找到字段 {fieldName}。");
        field.SetValue(window, value);
    }
}
