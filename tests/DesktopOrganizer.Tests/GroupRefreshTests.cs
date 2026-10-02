using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class GroupRefreshTests
{
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod]
    [DataRow(0, "zeta.txt,beta.bin,folder,alpha.txt,shell folder,shell item,missing.txt")]
    [DataRow(1, "alpha.txt,beta.bin,folder,missing.txt,shell folder,shell item,zeta.txt")]
    [DataRow(2, "folder,shell folder,beta.bin,shell item,alpha.txt,missing.txt,zeta.txt")]
    [DataRow(3, "folder,alpha.txt,beta.bin,zeta.txt,missing.txt,shell folder,shell item")]
    [DataRow(4, "folder,alpha.txt,beta.bin,zeta.txt,missing.txt,shell folder,shell item")]
    [DataRow(5, "folder,shell folder,alpha.txt,beta.bin,missing.txt,shell item,zeta.txt")]
    public void Rebuild_PreservesSortOrderAndReusesStableGroup(int sortMode, string expected)
    {
        using var fixture = new Fixture();
        fixture.SeedSortItems();
        fixture.Group.SortMode = (GroupSortMode)sortMode;
        fixture.Group.ItemNames.Add("not in snapshot");

        fixture.Rebuild();

        CollectionAssert.AreEqual(expected.Split(','), fixture.Panel.GetItemNamesSnapshot().ToArray());
        Assert.IsFalse(fixture.Group.ItemNames.Contains("not in snapshot"));
        FrameworkElement visual = fixture.Visual;
        VirtualizingGroupPanel panel = fixture.Panel;
        fixture.Rebuild();
        Assert.AreSame(visual, fixture.Visual);
        Assert.AreSame(panel, fixture.Panel);
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Rebuild_ResortsAfterFileTimeChangesOnTheNextRefresh(bool creation)
    {
        using var fixture = new Fixture();
        fixture.SeedSortItems();
        fixture.Group.SortMode = creation ? GroupSortMode.CreatedTime : GroupSortMode.ModifiedTime;
        fixture.Rebuild();
        FrameworkElement before = fixture.Visual;
        Assert.AreEqual("folder", fixture.Panel.GetItemNamesSnapshot()[0]);

        string path = fixture.Items["zeta.txt"];
        DateTime changed = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        if (creation) File.SetCreationTimeUtc(path, changed);
        else File.SetLastWriteTimeUtc(path, changed);
        fixture.Rebuild();

        Assert.AreNotSame(before, fixture.Visual);
        Assert.AreEqual("zeta.txt", fixture.Panel.GetItemNamesSnapshot()[0]);
        FrameworkElement after = fixture.Visual;
        fixture.Rebuild();
        Assert.AreSame(after, fixture.Visual);
    }

    [STATestMethod]
    public void Rebuild_RefreshesChangedPathsEvenWhenNamesAndOrderMatch()
    {
        using var fixture = new Fixture();
        fixture.AddItem("same.txt", Path.Combine(fixture.Root, "old.txt"));
        fixture.Rebuild();
        FrameworkElement before = fixture.Visual;

        string newPath = Path.Combine(fixture.Root, "new.txt");
        fixture.Items["same.txt"] = newPath;
        fixture.Rebuild();

        Assert.AreNotSame(before, fixture.Visual);
        Assert.IsTrue(fixture.Panel.MatchesItems([new GroupVirtualItem("same.txt", newPath)]));
    }

    [STATestMethod]
    public void Rebuild_ReplacesGroupWithAnUnstableDragPanel()
    {
        using var fixture = new Fixture();
        fixture.AddItem("same.txt", Path.Combine(fixture.Root, "same.txt"));
        fixture.Rebuild();
        FrameworkElement before = fixture.Visual;
        // 用轻量控件模拟面板的真实拖拽抑制，无需图标加载或窗口显示。
        var dragPanel = new VirtualizingGroupPanel(
            [new GroupVirtualItem("same.txt", fixture.Items["same.txt"])],
            _ => new Border(), _ => { }, 1, 80, 80, 0);
        dragPanel.Measure(new Size(80, 80));
        Assert.IsTrue(dragPanel.DetachForDrag((FrameworkElement)dragPanel.Children[0]));
        fixture.Field<Dictionary<string, VirtualizingGroupPanel>>("_groupItemPanels")[fixture.Group.Id] = dragPanel;

        fixture.Rebuild();

        Assert.AreNotSame(before, fixture.Visual);
        Assert.IsTrue(fixture.Panel.IsStable);
        CollectionAssert.AreEqual(new[] { "same.txt" }, fixture.Panel.GetItemNamesSnapshot().ToArray());
    }

    [STATestMethod]
    public void Rebuild_NameSortingStaysWithinOneSortAllocationBudget()
    {
        using var fixture = new Fixture();
        for (int index = 999; index >= 0; index--)
        {
            string name = $"Item{index:D5}.txt";
            fixture.AddItem(name, Path.Combine(fixture.Root, name));
            fixture.Layout.ItemIdentities[name] = new DesktopItemIdentityInfo { IsDirectory = false };
        }

        fixture.Group.SortMode = GroupSortMode.Custom;
        long customBytes = AllocatedBytesPerRefresh(fixture);
        fixture.Group.SortMode = GroupSortMode.Name;
        long nameBytes = AllocatedBytesPerRefresh(fixture);

        TestContext.WriteLine($"1000 项稳定分组刷新：自定义顺序 {customBytes} 字节，名称排序 {nameBytes} 字节，排序增量 {nameBytes - customBytes} 字节");
        // 单次排序需要项目/键/索引缓冲；预算留出余量，但不容纳第二次完整排序。
        Assert.IsLessThan(32_000L, nameBytes - customBytes,
            "同一轮稳定刷新应共享一次排序结果，不重复分配整组的排序缓冲。");
    }

    private static long AllocatedBytesPerRefresh(Fixture fixture)
    {
        for (int warmup = 0; warmup < 5; warmup++) fixture.Rebuild();
        FrameworkElement visual = fixture.Visual;
        const int iterations = 8;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < iterations; iteration++) fixture.Rebuild();
        long bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
        Assert.AreSame(visual, fixture.Visual, "测量稳定刷新，排除视觉重建的分配。");
        return bytes;
    }

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "DesktopOrganizer.Tests", Guid.NewGuid().ToString("N"));
        public MainWindow Window { get; }
        public AppLayoutData Layout => Field<AppLayoutData>("_appLayout");
        public Dictionary<string, string> Items => Field<Dictionary<string, string>>("_desktopItems");
        public GroupInfo Group { get; } = new() { Id = "test-group", Name = "测试分组", IsSizeLocked = true, IsCollapsed = true };
        public FrameworkElement Visual => Field<Dictionary<string, FrameworkElement>>("_groupVisuals")[Group.Id];
        public VirtualizingGroupPanel Panel => Field<Dictionary<string, VirtualizingGroupPanel>>("_groupItemPanels")[Group.Id];

        public Fixture()
        {
            Directory.CreateDirectory(Root);
            var resolver = new DesktopPathResolver(_ => Root);
            // 不调用 Show/Loaded，不读写用户布局，也不启动桌面扫描或文件监听。
            Window = new MainWindow(false, _ => { }, resolver);
            Set("_desktopSnapshotInitialized", true);
            Set("_isSafeModeActive", true);
            Layout.Groups.Clear();
            Layout.Groups.Add(Group);
            Layout.FolderPortals.Clear();
            Layout.RecycleBinWidget.IsVisible = false;
            Layout.SnapToGrid = false;
            AppDiagnostics diagnostics = Field<AppDiagnostics>("_diagnostics");
            typeof(AppDiagnostics).GetField("_logDirectory", PrivateInstance)!.SetValue(diagnostics, Root);
            typeof(AppDiagnostics).GetField("_logPath", PrivateInstance)!.SetValue(diagnostics, Path.Combine(Root, "diagnostics.log"));
        }

        public void AddItem(string name, string path)
        {
            Group.ItemNames.Add(name);
            Items.Add(name, path);
        }

        public void SeedSortItems()
        {
            foreach ((string name, int day) in new[] { ("zeta.txt", 1), ("beta.bin", 2), ("folder", 4), ("alpha.txt", 3) })
            {
                string path = Path.Combine(Root, name);
                if (name == "folder") Directory.CreateDirectory(path);
                else File.WriteAllText(path, "test");
                DateTime time = new(2024, 1, day, 0, 0, 0, DateTimeKind.Utc);
                if (name == "folder")
                {
                    Directory.SetCreationTimeUtc(path, time);
                    Directory.SetLastWriteTimeUtc(path, time);
                }
                else
                {
                    File.SetCreationTimeUtc(path, time);
                    File.SetLastWriteTimeUtc(path, time);
                }
                AddItem(name, path);
            }
            AddItem("shell folder", ShellItemLocation.Encode("test-shell-folder", true));
            AddItem("shell item", ShellItemLocation.Encode("test-shell-item", false));
            AddItem("missing.txt", Path.Combine(Root, "missing.txt"));
        }

        public T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, PrivateInstance)!.GetValue(Window)!;
        private void Set(string name, object value) => typeof(MainWindow).GetField(name, PrivateInstance)!.SetValue(Window, value);
        private void Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, PrivateInstance)!.Invoke(Window, args);
        public void Rebuild() => Invoke("RebuildDesktopIconsCore", new HashSet<string>(), false);

        public void Dispose()
        {
            Set("_isClosing", true);
            foreach (FieldInfo field in typeof(MainWindow).GetFields(PrivateInstance)
                         .Where(field => field.FieldType == typeof(DispatcherTimer)))
                ((DispatcherTimer)field.GetValue(Window)!).Stop();
            Field<CancellationTokenSource>("_lifetimeCts").Cancel();
            Invoke("CancelScheduledDesktopRefresh");
            Invoke("StopDesktopWatchers");
            Field<RefreshCancellationEpoch>("_refreshCancellationEpoch").Dispose();
            Invoke("StopAsyncIconLoading");
            Field<FileOperationService>("_fileOperationService").Dispose();
            Field<FolderPortalWatcherCoordinator>("_folderPortalWatcherCoordinator").Dispose();
            Field<CancellationTokenSource>("_lifetimeCts").Dispose();
            Field<SemaphoreSlim>("_layoutWriteGate").Dispose();

            string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopOrganizer.Tests")) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(Root);
            Assert.IsTrue(target.StartsWith(parent, StringComparison.OrdinalIgnoreCase));
            Directory.Delete(target, recursive: true);
        }
    }
}
