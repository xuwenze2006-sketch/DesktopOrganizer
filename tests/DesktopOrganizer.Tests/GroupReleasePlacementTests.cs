using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class GroupReleasePlacementTests
{
    [STATestMethod]
    public void DeleteGroup_SnapDisabled_AvoidsBottomClampAndExistingFreeIcon()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            const string existingName = "Existing.txt";
            const string firstName = "Released-A.txt";
            const string secondName = "Released-B.txt";
            AppLayoutData layout = PrepareLayout(window, 1200, 800);
            layout.FreeIcons[existingName] = new IconPosition { X = 20, Y = 710 };
            GroupInfo group = CreateGroup(firstName, secondName);
            layout.Groups.Add(group);
            PrepareDesktopItems(window, existingName, firstName, secondName);

            InvokeDeleteGroup(window, group);

            Assert.IsFalse(layout.Groups.Contains(group));
            Assert.AreEqual(20, layout.FreeIcons[existingName].X, 0.001);
            Assert.AreEqual(710, layout.FreeIcons[existingName].Y, 0.001);
            Assert.AreEqual(20, layout.FreeIcons[firstName].X, 0.001);
            Assert.AreEqual(604, layout.FreeIcons[firstName].Y, 0.001);
            Assert.AreEqual(110, layout.FreeIcons[secondName].X, 0.001);
            Assert.AreEqual(710, layout.FreeIcons[secondName].Y, 0.001);
            Assert.IsFalse(HasPositiveAreaOverlap(
                GetIconBounds(layout.FreeIcons[existingName]),
                GetIconBounds(layout.FreeIcons[firstName])));
            Assert.IsFalse(HasPositiveAreaOverlap(
                GetIconBounds(layout.FreeIcons[existingName]),
                GetIconBounds(layout.FreeIcons[secondName])));
            Assert.IsFalse(HasPositiveAreaOverlap(
                GetIconBounds(layout.FreeIcons[firstName]),
                GetIconBounds(layout.FreeIcons[secondName])));
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    public void DeleteGroup_SnapDisabled_NoCapacityKeepsGroupAndFreeIconsUnchanged()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            const string existingName = "Existing.txt";
            const string firstName = "Released-A.txt";
            const string secondName = "Released-B.txt";
            AppLayoutData layout = PrepareLayout(window, 180, 90);
            layout.FreeIcons[existingName] = new IconPosition { X = 0, Y = 0 };
            GroupInfo group = CreateGroup(firstName, secondName);
            group.X = 0;
            group.Y = 0;
            layout.Groups.Add(group);
            PrepareDesktopItems(window, existingName, firstName, secondName);
            object snapshot = CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;

            InvokeDeleteGroup(window, group);

            Assert.IsTrue(layout.Groups.Contains(group));
            CollectionAssert.AreEqual(new[] { firstName, secondName }, group.ItemNames);
            CollectionAssert.AreEqual(
                new[] { firstName, secondName },
                group.ManuallyAssignedItemNames);
            Assert.HasCount(1, layout.FreeIcons);
            Assert.AreEqual(0, layout.FreeIcons[existingName].X, 0.001);
            Assert.AreEqual(0, layout.FreeIcons[existingName].Y, 0.001);
            Assert.IsFalse(layout.FreeIcons.ContainsKey(firstName));
            Assert.IsFalse(layout.FreeIcons.ContainsKey(secondName));
            Assert.AreSame(snapshot, GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsTrue(window.UndoSmartLayoutButton.IsEnabled);
            Assert.AreEqual(
                $"没有足够的可用位置，分组“{group.Name}”未删除",
                window.StatusText.Text);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    private static AppLayoutData PrepareLayout(MainWindow window, double width, double height)
    {
        AppLayoutData layout = GetField<AppLayoutData>(window, "_appLayout");
        layout.Groups.Clear();
        layout.FolderPortals.Clear();
        layout.FreeIcons.Clear();
        layout.SnapToGrid = false;
        layout.RecycleBinWidget.IsVisible = false;
        SetField(
            window,
            "_desktopGeometry",
            new DesktopGeometry(
            [
                new DesktopMonitorRegion
                {
                    DeviceName = "TEST",
                    Bounds = new Rect(0, 0, width, height),
                    WorkArea = new Rect(0, 0, width, height),
                    IsPrimary = true
                }
            ]));
        return layout;
    }

    private static GroupInfo CreateGroup(string firstName, string secondName) => new()
    {
        Id = "deleted-group",
        Name = "待删除分类",
        X = 20,
        Y = 640,
        Width = 190,
        Height = 134,
        IsSizeLocked = true,
        ItemNames = [firstName, secondName],
        ManuallyAssignedItemNames = [firstName, secondName]
    };

    private static void PrepareDesktopItems(MainWindow window, params string[] names)
    {
        Dictionary<string, string> desktopItems = GetField<Dictionary<string, string>>(
            window,
            "_desktopItems");
        desktopItems.Clear();
        foreach (string name in names)
        {
            desktopItems[name] = $@"C:\Desktop\{name}";
        }
    }

    private static void InvokeDeleteGroup(MainWindow window, GroupInfo group)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "DeleteGroup",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到删除分组入口。");
        method.Invoke(window, [group]);
    }

    private static Rect GetIconBounds(IconPosition position) =>
        new(position.X, position.Y, 90, 90);

    private static bool HasPositiveAreaOverlap(Rect first, Rect second) =>
        first.Left < second.Right &&
        first.Right > second.Left &&
        first.Top < second.Bottom &&
        first.Bottom > second.Top;

    private static object CaptureSmartLayoutSnapshot(MainWindow window)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "CaptureGroupLayoutSnapshot",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到 CaptureGroupLayoutSnapshot 方法。");
        object snapshot = method.Invoke(window, null)
            ?? throw new AssertFailedException("CaptureGroupLayoutSnapshot 未返回快照。");
        SetField(window, "_lastSmartLayoutSnapshot", snapshot);
        return snapshot;
    }

    private static object? GetRawField(MainWindow window, string fieldName)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException($"未找到字段 {fieldName}。");
        return field.GetValue(window);
    }

    private static T GetField<T>(MainWindow window, string fieldName)
        where T : class
    {
        return typeof(MainWindow).GetField(
                   fieldName,
                   BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as T
               ?? throw new AssertFailedException($"字段 {fieldName} 尚未初始化。");
    }

    private static void SetField(MainWindow window, string fieldName, object value)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException($"未找到字段 {fieldName}。");
        field.SetValue(window, value);
    }
}
