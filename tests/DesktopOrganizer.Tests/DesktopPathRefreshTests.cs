using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopPathRefreshTests
{
    [STATestMethod]
    public void Scan_ReReadsMigratedDesktopAndMergesTheCommonDesktop()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.UserA, "old.txt"), "old");
        File.WriteAllText(Path.Combine(fixture.UserB, "new.txt"), "new");
        File.WriteAllText(Path.Combine(fixture.Common, "public.txt"), "public");

        ScanResult before = fixture.Scan();
        Assert.IsTrue(before.Complete);
        CollectionAssert.AreEquivalent(new[] { "old.txt", "public.txt" }, before.Items.Keys.ToArray());
        fixture.UserPath = fixture.UserB;
        ScanResult after = fixture.Scan();

        Assert.IsTrue(after.Complete);
        CollectionAssert.AreEquivalent(new[] { "new.txt", "public.txt" }, after.Items.Keys.ToArray());
        Assert.AreEqual(Path.Combine(fixture.UserB, "new.txt"), after.Items["new.txt"]);
        Assert.IsTrue(after.Paths.Revision > before.Paths.Revision);
    }

    [STATestMethod]
    public void Scan_EmptyUserPathCannotProduceACompleteSnapshot()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Common, "public.txt"), "public");
        fixture.UserPath = string.Empty;

        ScanResult scan = fixture.Scan();

        Assert.IsFalse(scan.Complete);
        Assert.IsFalse(scan.Paths.UserDesktop.IsAvailable);
        Assert.IsNull(scan.Paths.UserDesktop.Path);
        Assert.IsEmpty(scan.Items, "用户路径未确定时，不用公共桌面的部分项目替代完整快照。");
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Rebuild_BeforeFirstCompleteSnapshotPreservesSavedLayout(bool firstScanFailed)
    {
        using var fixture = new Fixture();
        fixture.SeedLayout();
        string before = fixture.LayoutPositionsAndGroups();
        if (firstScanFailed)
        {
            fixture.UserPath = string.Empty;
            fixture.Refresh();
            Assert.IsTrue(fixture.Field<bool>("_desktopScanUnavailable"));
        }
        Assert.IsFalse(fixture.Field<bool>("_desktopSnapshotInitialized"));

        fixture.Rebuild();

        Assert.AreEqual(before, fixture.LayoutPositionsAndGroups(),
            "尚无完整快照时，缺失扫描结果不能作为清理已有布局的依据。");
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Refresh_UnavailableCommonDesktopKeepsCompleteLayoutUntilRecovery(bool pathIsEmpty)
    {
        using var fixture = new Fixture();
        fixture.WriteLayoutItems();
        File.WriteAllText(Path.Combine(fixture.Common, "public.txt"), "public");
        fixture.SeedLayout();
        fixture.Refresh();
        Assert.IsTrue(fixture.Field<bool>("_desktopSnapshotInitialized"));
        string before = fixture.LayoutPositionsAndGroups();
        string[] knownNames = fixture.Items.Keys.ToArray();

        fixture.CommonPath = pathIsEmpty ? string.Empty : Path.Combine(fixture.Root, "missing-common");
        fixture.Refresh();

        Assert.IsTrue(fixture.Field<bool>("_desktopScanUnavailable"));
        Assert.AreEqual(before, fixture.LayoutPositionsAndGroups());
        CollectionAssert.AreEquivalent(knownNames, fixture.Items.Keys.ToArray());

        fixture.CommonPath = fixture.Common;
        File.WriteAllText(Path.Combine(fixture.Common, "recovered.txt"), "recovered");
        fixture.Refresh();

        Assert.IsFalse(fixture.Field<bool>("_desktopScanUnavailable"));
        Assert.IsTrue(fixture.Items.ContainsKey("public.txt"));
        Assert.IsTrue(fixture.Items.ContainsKey("recovered.txt"));
        CollectionAssert.AreEqual(new[] { "grouped.txt" }, fixture.Layout.Groups.Single().ItemNames);
    }

    [STATestMethod]
    public void ClearClassification_BeforeFirstCompleteSnapshotPreservesGroupsAndOriginalPositions()
    {
        using var fixture = new Fixture();
        fixture.SeedLayout();
        fixture.Layout.Groups.Single().IsAutoCategory = true;
        string before = fixture.LayoutPositionsAndGroups();
        fixture.ClearClassification();
        Assert.AreEqual(before, fixture.LayoutPositionsAndGroups());
        StringAssert.Contains(fixture.Window.StatusText.Text, "尚未完整读取");
    }

    [STATestMethod]
    public void Refresh_ExistingEmptyDirectoriesRemoveOnlyTrulyMissingLayoutItems()
    {
        using var fixture = new Fixture();
        fixture.WriteLayoutItems();
        fixture.SeedLayout();
        fixture.Refresh();
        Assert.HasCount(2, fixture.Items);

        File.Delete(Path.Combine(fixture.UserA, "grouped.txt"));
        File.Delete(Path.Combine(fixture.UserA, "free.txt"));
        fixture.Refresh();

        Assert.IsTrue(fixture.Field<bool>("_desktopSnapshotInitialized"));
        Assert.IsFalse(fixture.Field<bool>("_desktopScanUnavailable"));
        Assert.IsEmpty(fixture.Items);
        Assert.IsEmpty(fixture.Layout.Groups.Single().ItemNames);
        Assert.IsFalse(fixture.Layout.FreeIcons.ContainsKey("free.txt"));
        Assert.IsFalse(fixture.Layout.AutoClassificationOriginalPositions.ContainsKey("grouped.txt"));
        Assert.IsTrue(Directory.Exists(fixture.UserA));
        Assert.IsTrue(Directory.Exists(fixture.Common));
    }

    [STATestMethod]
    public void ApplyDesktopPaths_ReusesWatchersThenMigratesDeduplicatesAndRejectsOldRevision()
    {
        using var fixture = new Fixture(safeMode: false);
        DesktopPathSnapshot first = fixture.Resolver.Refresh();
        Assert.IsTrue(fixture.Apply(first));
        FileSystemWatcher[] originalWatchers = fixture.Watchers.ToArray();
        Assert.HasCount(2, originalWatchers);

        Assert.IsTrue(fixture.Apply(fixture.Resolver.Refresh()));
        CollectionAssert.AreEqual(originalWatchers, fixture.Watchers.ToArray(),
            "位置和监听状态不变时不能反复重建监听器。");

        fixture.UserPath = fixture.UserB;
        fixture.CommonPath = fixture.UserB;
        DesktopPathSnapshot migrated = fixture.Resolver.Refresh();
        Assert.IsTrue(fixture.Apply(migrated));
        FileSystemWatcher replacement = fixture.Watchers.Single();
        Assert.AreEqual(fixture.UserB, replacement.Path);
        Assert.IsFalse(originalWatchers.Contains(replacement));
        CollectionAssert.AreEqual(new[] { fixture.UserB }, fixture.WatcherPaths());

        Assert.IsFalse(fixture.Apply(first), "迟到的旧结果不能把监听器迁回旧桌面。");
        Assert.AreSame(replacement, fixture.Watchers.Single());
        Assert.AreSame(migrated, fixture.Field<DesktopPathSnapshot>("_desktopPaths"));
    }

    [STATestMethod]
    public void BackgroundProbe_DetectsMigrationAndNewWatcherRefreshesTheNewDesktop()
    {
        using var fixture = new Fixture(safeMode: false);
        File.WriteAllText(Path.Combine(fixture.UserA, "old.txt"), "old");
        File.WriteAllText(Path.Combine(fixture.UserB, "new.txt"), "new");
        fixture.Refresh();
        Assert.IsTrue(fixture.Items.ContainsKey("old.txt"));

        fixture.UserPath = fixture.UserB;
        fixture.Probe();
        fixture.WaitForItem("new.txt");
        Assert.IsFalse(fixture.Items.ContainsKey("old.txt"));
        StringAssert.Contains((string)fixture.Window.StatusText.ToolTip, fixture.UserB);

        File.WriteAllText(Path.Combine(fixture.UserB, "watcher.txt"), "event");
        fixture.WaitForItem("watcher.txt");
        Assert.AreEqual(Path.Combine(fixture.UserB, "watcher.txt"), fixture.Items["watcher.txt"]);
    }

    private sealed record ScanResult(
        Dictionary<string, string> Items,
        bool Complete,
        DesktopPathSnapshot Paths);

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(8);
        private readonly ConcurrentQueue<string> _writes = new();
        private readonly List<Task> _refreshes = [];

        public string Root { get; }
        public string UserA { get; }
        public string UserB { get; }
        public string Common { get; }
        public string UserPath { get; set; }
        public string CommonPath { get; set; }
        public DesktopPathResolver Resolver { get; }
        public MainWindow Window { get; }
        public AppLayoutData Layout => Field<AppLayoutData>("_appLayout");
        public Dictionary<string, string> Items => Field<Dictionary<string, string>>("_desktopItems");
        public List<FileSystemWatcher> Watchers => Field<List<FileSystemWatcher>>("_watchers");

        public Fixture(bool safeMode = true)
        {
            Root = Path.Combine(Path.GetTempPath(), "DesktopOrganizer.Tests", Guid.NewGuid().ToString("N"));
            UserA = Directory.CreateDirectory(Path.Combine(Root, "原用户桌面")).FullName;
            UserB = Directory.CreateDirectory(Path.Combine(Root, "迁移后桌面")).FullName;
            Common = Directory.CreateDirectory(Path.Combine(Root, "公共桌面")).FullName;
            UserPath = UserA;
            CommonPath = Common;
            Resolver = new DesktopPathResolver(folder => folder == Environment.SpecialFolder.DesktopDirectory
                ? UserPath : CommonPath);
            // 不调用 Show/Loaded，不读用户布局，写入委托只接收内存快照。
            Window = new MainWindow(false, _writes.Enqueue, Resolver);
            Set("_isSafeModeActive", safeMode);
            Layout.SnapToGrid = false;
            Layout.RecycleBinWidget.IsVisible = false;
            AppDiagnostics diagnostics = Field<AppDiagnostics>("_diagnostics");
            typeof(AppDiagnostics).GetField("_logDirectory", PrivateInstance)!.SetValue(diagnostics, Root);
            typeof(AppDiagnostics).GetField("_logPath", PrivateInstance)!
                .SetValue(diagnostics, Path.Combine(Root, "diagnostics.log"));
        }

        public T Field<T>(string name) =>
            (T)typeof(MainWindow).GetField(name, PrivateInstance)!.GetValue(Window)!;

        private void Set(string name, object value) =>
            typeof(MainWindow).GetField(name, PrivateInstance)!.SetValue(Window, value);

        private object? Invoke(string name, params object?[] arguments) =>
            typeof(MainWindow).GetMethod(name, PrivateInstance)!.Invoke(Window, arguments);

        public ScanResult Scan()
        {
            object snapshot = Invoke("ScanDesktopSnapshot", CancellationToken.None)!;
            T Property<T>(string name) => (T)snapshot.GetType().GetProperty(name)!.GetValue(snapshot)!;
            return new ScanResult(Property<Dictionary<string, string>>("Items"),
                Property<bool>("PhysicalScanComplete"), Property<DesktopPathSnapshot>("Paths"));
        }

        public void Refresh()
        {
            var task = (Task)Invoke("RefreshDesktopSnapshotAsync", false, null)!;
            _refreshes.Add(task);
            PumpUntil(() => task.IsCompleted, "桌面刷新未在预期时间内结束。");
            task.GetAwaiter().GetResult();
        }

        public bool Apply(DesktopPathSnapshot snapshot) => (bool)Invoke("ApplyDesktopPaths", snapshot)!;

        public void Probe()
        {
            var task = (Task)Invoke("CheckDesktopPathChangesAsync")!;
            PumpUntil(() => task.IsCompleted, "后台桌面位置探测未结束。");
            task.GetAwaiter().GetResult();
        }

        public void WaitForItem(string name) => PumpUntil(() => Items.ContainsKey(name),
            $"新桌面项目 {name} 未通过自动刷新出现。");

        public string[] WatcherPaths() => (string[])Invoke("GetDesktopWatcherPaths")!;

        public void Rebuild() => Invoke("RebuildDesktopIconsCore", new HashSet<string>(), false);

        public void ClearClassification() => Invoke("ClearAutoClassificationAfterConfirmation",
            (object)Layout.Groups.ToArray());

        public void WriteLayoutItems()
        {
            File.WriteAllText(Path.Combine(UserA, "grouped.txt"), "grouped");
            File.WriteAllText(Path.Combine(UserA, "free.txt"), "free");
        }

        public void SeedLayout()
        {
            Layout.Groups.Add(new GroupInfo
            {
                Id = "saved-group", Name = "原有分组", ItemNames = ["grouped.txt"],
                X = 50, Y = 80, Width = 300, Height = 240, IsSizeLocked = true
            });
            Layout.FreeIcons["free.txt"] = new IconPosition { X = 420, Y = 360 };
            Layout.AutoClassificationOriginalPositions["grouped.txt"] = new IconPosition { X = 500, Y = 480 };
        }

        public string LayoutPositionsAndGroups() => JsonSerializer.Serialize(new
        {
            Layout.Groups,
            Layout.FreeIcons,
            Layout.AutoClassificationOriginalPositions
        });

        private void PumpUntil(Func<bool> completed, string error)
        {
            if (completed()) return;
            var elapsed = Stopwatch.StartNew();
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background, Window.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(10)
            };
            timer.Tick += (_, _) =>
            {
                if (completed() || elapsed.Elapsed >= WaitTimeout) frame.Continue = false;
            };
            timer.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
            Assert.IsTrue(completed(), error);
        }

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
            PumpUntil(() => _refreshes.All(task => task.IsCompleted) &&
                Field<int>("_refreshWorkerRunning") == 0 && Field<int>("_layoutWriterRunning") == 0 &&
                Field<int>("_incompleteScanRetryQueued") == 0 && Field<int>("_desktopPathCheckRunning") == 0,
                "fixture 后台任务尚未退出，不能删除测试目录。");
            Field<CancellationTokenSource>("_lifetimeCts").Dispose();
            Field<SemaphoreSlim>("_layoutWriteGate").Dispose();

            string testParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopOrganizer.Tests")) +
                Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(Root);
            Assert.IsTrue(target.StartsWith(testParent, StringComparison.OrdinalIgnoreCase));
            Directory.Delete(target, recursive: true);
        }
    }
}
