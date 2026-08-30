using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopSearchLocateTests
{
    [STATestMethod]
    public void LocateDesktopSearchResult_SynchronizesGroupedRangeAnchor()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            const string firstName = "A.txt";
            const string locatedName = "B.txt";
            const string endpointName = "C.txt";
            const string freeName = "Free.txt";
            Dictionary<string, string> desktopItems = GetField<Dictionary<string, string>>(
                window,
                "_desktopItems");
            desktopItems.Clear();
            desktopItems[firstName] = $@"C:\Desktop\{firstName}";
            desktopItems[locatedName] = $@"C:\Desktop\{locatedName}";
            desktopItems[endpointName] = $@"C:\Desktop\{endpointName}";
            desktopItems[freeName] = $@"C:\Desktop\{freeName}";

            AppLayoutData layout = GetField<AppLayoutData>(window, "_appLayout");
            layout.Groups.Clear();
            layout.FreeIcons.Clear();
            layout.FolderPortals.Clear();
            var group = new GroupInfo
            {
                Id = "group",
                Name = "工作",
                X = 40,
                Y = 40,
                Width = 320,
                Height = 220,
                IsSizeLocked = true,
                SortMode = GroupSortMode.Custom,
                ItemNames = [firstName, locatedName, endpointName]
            };
            layout.Groups.Add(group);
            layout.FreeIcons[freeName] = new IconPosition { X = 480, Y = 280 };

            window.LocateDesktopSearchResult(locatedName);

            HashSet<string> selectedItemNames = GetField<HashSet<string>>(
                window,
                "_selectedItemNames");
            GroupRangeSelectionAnchor groupedAnchor = GetField<GroupRangeSelectionAnchor>(
                window,
                "_groupRangeSelectionAnchor");
            Assert.IsTrue(selectedItemNames.SetEquals([locatedName]));
            Assert.AreEqual(group.Id, groupedAnchor.GroupId);
            Assert.AreEqual(locatedName, groupedAnchor.ItemName);

            GroupRangeSelectionPlan plan = GroupRangeSelectionPolicy.CreatePlan(
                group.Id,
                group.ItemNames,
                selectedItemNames,
                groupedAnchor,
                endpointName);
            Assert.IsTrue(plan.UsedAnchor);
            CollectionAssert.AreEqual(
                new[] { locatedName, endpointName },
                plan.ItemNames.ToArray());

            window.LocateDesktopSearchResult(freeName);

            Assert.IsTrue(selectedItemNames.SetEquals([freeName]));
            Assert.IsNull(GetNullableField<GroupRangeSelectionAnchor>(
                window,
                "_groupRangeSelectionAnchor"));
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
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
}
