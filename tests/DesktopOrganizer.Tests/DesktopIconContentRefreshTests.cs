using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopIconContentRefreshTests
{
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod]
    public void SharedIcons_UseOneSuccessfulLoadAndFillEveryVisual()
    {
        using var loader = new ControlledIconLoader();
        using var fixture = new DesktopPathRefreshTests.Fixture(iconLoadService: loader.Service);
        WriteFiles(fixture, 12);
        loader.Hold();
        try
        {
            fixture.Refresh();
            Assert.HasCount(1, fixture.Field<System.Collections.IDictionary>("_queuedIconLoads"));
            Assert.HasCount(1, fixture.Field<System.Collections.IDictionary>("_pendingIconVisuals"));
        }
        finally { loader.Release(); }
        fixture.WaitForIcons();
        Assert.AreEqual(1, loader.LoadCount);
        BitmapSource source = fixture.Field<Dictionary<string, BitmapSource?>>("_iconCache")["ext:.txt"]!;
        Assert.IsNotNull(source);
        Assert.IsTrue(source.IsFrozen);
        foreach (FrameworkElement visual in Visuals(fixture).Values)
            Assert.AreSame(source, IconImage(visual).Source);
    }

    [STATestMethod]
    public void ContentChange_ReusesSharedIconsAndPublishesNewWriteTime()
    {
        using var loader = new ControlledIconLoader();
        using var fixture = new DesktopPathRefreshTests.Fixture(iconLoadService: loader.Service);
        WriteFiles(fixture, 12);
        fixture.Refresh();
        fixture.WaitForIcons();
        var before = new Dictionary<string, FrameworkElement>(Visuals(fixture));
        BitmapSource source = (BitmapSource)IconImage(before["file00.txt"]).Source;
        string fileId = fixture.Layout.ItemIdentities["file00.txt"].FileId!;
        int loadsBefore = loader.LoadCount;
        DateTime changedTime = new(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        File.AppendAllText(Path.Combine(fixture.UserA, "file00.txt"), ".");
        File.SetLastWriteTimeUtc(Path.Combine(fixture.UserA, "file00.txt"), changedTime);
        var recorder = new DesktopRefreshPerformanceRecorder();
        fixture.Window.RefreshPerformanceRecorder = recorder;
        fixture.Refresh();
        fixture.Window.RefreshPerformanceRecorder = null;
        fixture.WaitForIcons();
        int reused = before.Count(pair => ReferenceEquals(pair.Value, Visuals(fixture)[pair.Key]));
        TestContext.WriteLine($"created={12 - reused}, reused={reused}, loads={loader.LoadCount - loadsBefore}");
        Assert.IsTrue(recorder.Completed);
        Assert.AreEqual(changedTime.Ticks, fixture.Layout.ItemIdentities["file00.txt"].LastWriteTimeUtcTicks);
        Assert.AreEqual(fileId, fixture.Layout.ItemIdentities["file00.txt"].FileId);
        Assert.AreEqual(12, reused, "普通文件内容变化不应重建共享类型图标。");
        Assert.AreEqual(loadsBefore, loader.LoadCount);
        Assert.AreSame(source, IconImage(Visuals(fixture)["file00.txt"]).Source);
    }

    [STATestMethod]
    public void ForceRefresh_ReloadsSharedIconAndRejectsOldGeneration()
    {
        using var loader = new ControlledIconLoader();
        using var fixture = new DesktopPathRefreshTests.Fixture(iconLoadService: loader.Service);
        WriteFiles(fixture, 2);
        fixture.Refresh();
        fixture.WaitForIcons();
        FrameworkElement before = Visuals(fixture)["file00.txt"];
        BitmapSource source = (BitmapSource)IconImage(before).Source;
        IconLoadRequestKey oldRequest = CurrentRequest(fixture, "ext:.txt");
        int loadsBefore = loader.LoadedKeys.Count(key => key == "ext:.txt");

        fixture.Refresh(clearIconCache: true);
        fixture.WaitForIcons();

        Assert.AreNotSame(before, Visuals(fixture)["file00.txt"]);
        // 强制刷新还会加载回收站的独立 Shell 图标；这里核对共享文件图标的加载数。
        TestContext.WriteLine($"force-refresh loads: {string.Join(", ", loader.LoadedKeys)}");
        Assert.AreEqual(loadsBefore + 1, loader.LoadedKeys.Count(key => key == "ext:.txt"));
        BitmapSource replacement = (BitmapSource)IconImage(Visuals(fixture)["file00.txt"]).Source;
        Assert.AreNotSame(source, replacement);
        CompleteOldRequest(fixture, oldRequest, source);
        Assert.AreSame(replacement, fixture.Field<Dictionary<string, BitmapSource?>>("_iconCache")["ext:.txt"]);
    }

    [STATestMethod]
    public void ExecutableContentChange_ReloadsOwnIconAndRejectsOldVersion()
    {
        using var loader = new ControlledIconLoader();
        using var fixture = new DesktopPathRefreshTests.Fixture(iconLoadService: loader.Service);
        WriteFiles(fixture, 1);
        string path = Path.Combine(fixture.UserA, "tool.exe");
        File.WriteAllText(path, "fixture");
        fixture.Refresh();
        fixture.WaitForIcons();
        FrameworkElement stable = Visuals(fixture)["file00.txt"];
        FrameworkElement before = Visuals(fixture)["tool.exe"];
        BitmapSource source = (BitmapSource)IconImage(before).Source;
        IconLoadRequestKey oldRequest = CurrentRequest(fixture, "path:" + path);
        int loadsBefore = loader.LoadCount;

        File.AppendAllText(path, ".");
        File.SetLastWriteTimeUtc(path, new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        fixture.Refresh();
        fixture.WaitForIcons();

        Assert.AreSame(stable, Visuals(fixture)["file00.txt"]);
        Assert.AreNotSame(before, Visuals(fixture)["tool.exe"]);
        Assert.AreEqual(loadsBefore + 1, loader.LoadCount);
        BitmapSource replacement = (BitmapSource)IconImage(Visuals(fixture)["tool.exe"]).Source;
        Assert.AreNotSame(source, replacement);
        CompleteOldRequest(fixture, oldRequest, source);
        Assert.AreSame(replacement, fixture.Field<Dictionary<string, BitmapSource?>>("_iconCache")["path:" + path]);
    }

    [STATestMethod]
    public void ContentChange_UpdatesModifiedTimeSortWithoutReloadingSharedIcon()
    {
        using var loader = new ControlledIconLoader();
        using var fixture = new DesktopPathRefreshTests.Fixture(iconLoadService: loader.Service);
        WriteFiles(fixture, 3);
        DateTime start = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(fixture.UserA, "file00.txt"), start);
        File.SetLastWriteTimeUtc(Path.Combine(fixture.UserA, "file01.txt"), start.AddDays(1));
        var group = new GroupInfo
        {
            Id = "modified-sort", Name = "时间排序", IsSizeLocked = true, IsCollapsed = true,
            SortMode = GroupSortMode.ModifiedTime, ItemNames = ["file00.txt", "file01.txt"]
        };
        fixture.Layout.Groups.Add(group);
        fixture.Refresh();
        fixture.WaitForIcons();
        var panels = fixture.Field<Dictionary<string, VirtualizingGroupPanel>>("_groupItemPanels");
        CollectionAssert.AreEqual(new[] { "file01.txt", "file00.txt" }, panels[group.Id].GetItemNamesSnapshot().ToArray());
        FrameworkElement stable = Visuals(fixture)["file02.txt"];
        int loadsBefore = loader.LoadCount;

        File.AppendAllText(Path.Combine(fixture.UserA, "file00.txt"), ".");
        File.SetLastWriteTimeUtc(Path.Combine(fixture.UserA, "file00.txt"), start.AddDays(2));
        fixture.Refresh();
        fixture.WaitForIcons();

        CollectionAssert.AreEqual(new[] { "file00.txt", "file01.txt" }, panels[group.Id].GetItemNamesSnapshot().ToArray());
        Assert.AreEqual(start.AddDays(2).Ticks, fixture.Layout.ItemIdentities["file00.txt"].LastWriteTimeUtcTicks);
        Assert.AreSame(stable, Visuals(fixture)["file02.txt"]);
        Assert.AreEqual(loadsBefore, loader.LoadCount);
    }

    private static IconLoadRequestKey CurrentRequest(DesktopPathRefreshTests.Fixture fixture, string key) =>
        new(key, fixture.Field<int>("_iconVisualGeneration"),
            fixture.Field<Dictionary<string, int>>("_iconCacheVersions").GetValueOrDefault(key));

    private static void CompleteOldRequest(DesktopPathRefreshTests.Fixture fixture, IconLoadRequestKey request, BitmapSource source) =>
        typeof(MainWindow).GetMethod("ApplyCompletedIconLoad", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(fixture.Window, [new IconLoadResult(request, source, TimeSpan.Zero)]);

    private static void WriteFiles(DesktopPathRefreshTests.Fixture fixture, int count)
    {
        for (int i = 0; i < count; i++)
            File.WriteAllText(Path.Combine(fixture.UserA, $"file{i:D2}.txt"), "content");
    }

    private static Dictionary<string, FrameworkElement> Visuals(DesktopPathRefreshTests.Fixture fixture) =>
        fixture.Field<Dictionary<string, FrameworkElement>>("_freeIconVisuals");

    private static Image IconImage(FrameworkElement visual) =>
        ((Grid)((StackPanel)((Border)visual).Child).Children[0]).Children.OfType<Image>().Single();
}
