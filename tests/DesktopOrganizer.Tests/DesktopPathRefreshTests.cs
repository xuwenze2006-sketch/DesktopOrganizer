using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopPathRefreshTests
{
    [STATestMethod]
    public void IconType_LabelAndTooltipUseTheAcceptedFolderType()
    {
        using var fixture = new Fixture();
        const string name = "folder.lnk";
        string path = Directory.CreateDirectory(Path.Combine(fixture.UserA, name)).FullName;
        fixture.Refresh();
        Directory.Delete(path);
        // 固定图标缓存，避免本用例触发后台 Shell 图标提取。
        fixture.Field<Dictionary<string, BitmapSource?>>("_iconCache")["folder:" + path] = null;
        fixture.Field<Dictionary<string, System.Windows.FrameworkElement>>("_physicalFolderDropTargets").Clear();

        Border tile = fixture.CreateIcon(path, name);

        var content = (StackPanel)tile.Child;
        var label = (TextBlock)((Border)content.Children[1]).Child;
        Assert.AreEqual(name, label.Text, "快照中的文件夹即使以 .lnk 结尾，也不应裁剪名称。");
        StringAssert.Contains((string)tile.ToolTip, "真实文件夹");
        Assert.IsFalse(fixture.Field<Dictionary<string, System.Windows.FrameworkElement>>("_physicalFolderDropTargets")
            .ContainsKey(path), "真实文件操作的目标登记仍需确认目录存在。");
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void IconType_ReusesAcceptedScanUntilTheNextSnapshot(bool initiallyFolder)
    {
        using var fixture = new Fixture();
        string path = Path.Combine(fixture.UserA, "subject.txt");
        if (initiallyFolder) Directory.CreateDirectory(path);
        else File.WriteAllText(path, "before");
        fixture.Refresh();
        string beforeKey = initiallyFolder ? "folder:" + path : "ext:.txt";
        string afterKey = initiallyFolder ? "ext:.txt" : "folder:" + path;
        Assert.AreEqual(beforeKey, fixture.IconCacheKey(path));

        if (initiallyFolder)
        {
            Directory.Delete(path);
            File.WriteAllText(path, "after");
        }
        else
        {
            File.Delete(path);
            Directory.CreateDirectory(path);
        }
        // 后台扫描本身不发布状态；UI 接受快照前使用此前读取成功的类型。
        fixture.Scan();
        Assert.AreEqual(beforeKey, fixture.IconCacheKey(path));
        fixture.Refresh();
        Assert.AreEqual(afterKey, fixture.IconCacheKey(path));
    }

    [STATestMethod]
    public void IconType_IncompleteScanKeepsTheLastAcceptedTypes()
    {
        using var fixture = new Fixture();
        string path = Directory.CreateDirectory(Path.Combine(fixture.UserA, "subject.txt")).FullName;
        fixture.Refresh();
        Directory.Delete(path);
        File.WriteAllText(path, "replacement");
        fixture.CommonPath = Path.Combine(fixture.Root, "unavailable");

        fixture.Refresh();

        Assert.IsTrue(fixture.Field<bool>("_desktopScanUnavailable"));
        Assert.AreEqual("folder:" + path, fixture.IconCacheKey(path));
        fixture.CommonPath = fixture.Common;
        fixture.Refresh();
        Assert.AreEqual("ext:.txt", fixture.IconCacheKey(path));
    }

    [STATestMethod]
    public void IconType_UnchangedScanReplacesRuntimeTypesBeforeReturning()
    {
        using var fixture = new Fixture();
        string path = Path.Combine(fixture.UserA, "subject.txt");
        File.WriteAllText(path, "file");
        fixture.Refresh();
        fixture.Field<DesktopItemTypeLookup>("_desktopItemTypes").ReplaceSnapshot(
            new Dictionary<string, bool> { [path] = true });
        Assert.AreEqual("folder:" + path, fixture.IconCacheKey(path));

        fixture.Refresh();

        Assert.AreEqual("ext:.txt", fixture.IconCacheKey(path));
    }

    [STATestMethod]
    public void IconType_DesktopMigrationDropsOldPathAndIgnoresSameNameIdentity()
    {
        using var fixture = new Fixture();
        string oldPath = Directory.CreateDirectory(Path.Combine(fixture.UserA, "subject.txt")).FullName;
        fixture.Refresh();
        string newPath = Path.Combine(fixture.UserB, "subject.txt");
        File.WriteAllText(newPath, "new file");
        // 新路径没有已接受的类型，不能根据旧路径的同名持久化身份判断为文件夹。
        Assert.AreEqual("file-system-file", fixture.VisualKind(newPath));
        fixture.UserPath = fixture.UserB;
        fixture.Refresh();
        Directory.Delete(oldPath);

        Assert.AreEqual("ext:.txt", fixture.IconCacheKey(newPath));
        Assert.AreEqual("ext:.txt", fixture.IconCacheKey(oldPath));
        Assert.AreEqual("missing", fixture.VisualKind(oldPath));
    }

    [STATestMethod]
    [DataRow("file")]
    [DataRow("folder")]
    [DataRow("missing")]
    public void IconType_OnlySuccessfulAttributeReadsProduceKnownTypes(string kind)
    {
        using var fixture = new Fixture();
        string path = Path.Combine(fixture.UserA, "subject.txt");
        if (kind == "file") File.WriteAllText(path, "file");
        else if (kind == "folder") Directory.CreateDirectory(path);
        object?[] arguments = [path, null];

        typeof(MainWindow).GetMethod("CreateDesktopItemIdentity", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments);

        if (kind == "missing") Assert.IsNull(arguments[1]);
        else Assert.AreEqual(kind == "folder", arguments[1]);
    }

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
        var tip = (ToolTip)fixture.Window.StatusText.ToolTip;
        var content = (StackPanel)tip.Content;
        StringAssert.Contains(string.Join("\n", content.Children.OfType<TextBlock>().Select(block => block.Text)), fixture.UserB);

        File.WriteAllText(Path.Combine(fixture.UserB, "watcher.txt"), "event");
        fixture.WaitForItem("watcher.txt");
        Assert.AreEqual(Path.Combine(fixture.UserB, "watcher.txt"), fixture.Items["watcher.txt"]);
    }

    internal sealed record ScanResult(
        Dictionary<string, string> Items,
        bool Complete,
        DesktopPathSnapshot Paths);

    internal sealed class Fixture : IDisposable
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

        public Fixture(bool safeMode = true, ShellIconLoadService? iconLoadService = null,
            Action? beforePathRead = null)
        {
            Root = Path.Combine(Path.GetTempPath(), "DesktopOrganizer.Tests", Guid.NewGuid().ToString("N"));
            UserA = Directory.CreateDirectory(Path.Combine(Root, "原用户桌面")).FullName;
            UserB = Directory.CreateDirectory(Path.Combine(Root, "迁移后桌面")).FullName;
            Common = Directory.CreateDirectory(Path.Combine(Root, "公共桌面")).FullName;
            UserPath = UserA;
            CommonPath = Common;
            Resolver = new DesktopPathResolver(folder =>
            {
                beforePathRead?.Invoke();
                return folder == Environment.SpecialFolder.DesktopDirectory ? UserPath : CommonPath;
            });
            // 不调用 Show/Loaded，不读用户布局，写入委托只接收内存快照。
            Window = new MainWindow(false, _writes.Enqueue, Resolver);
            if (iconLoadService != null)
            {
                Field<ShellIconLoadService>("_shellIconLoadService").Dispose();
                Set("_shellIconLoadService", iconLoadService);
            }
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

        internal void Set(string name, object value) =>
            typeof(MainWindow).GetField(name, PrivateInstance)!.SetValue(Window, value);

        internal object? Invoke(string name, params object?[] arguments)
        {
            MethodInfo? method = typeof(MainWindow).GetMethod(name, PrivateInstance);
            Assert.IsNotNull(method, $"缺少运行入口 {name}");
            return method.Invoke(Window, arguments);
        }

        public void Synchronize()
        {
            var task = (Task)Invoke("CheckDesktopSynchronizationAsync")!;
            PumpUntil(() => task.IsCompleted, "基础同步巡检未结束。");
            task.GetAwaiter().GetResult();
            WaitForRefreshIdle();
        }

        public void WaitForRefreshIdle()
        {
            PumpUntil(() => Field<int>("_refreshWorkerRunning") == 0 &&
                Field<int>("_refreshRequested") == 0, "基础同步刷新尚未结束。");
        }

        public ScanResult Scan()
        {
            object snapshot = Invoke("ScanDesktopSnapshot", CancellationToken.None)!;
            T Property<T>(string name) => (T)snapshot.GetType().GetProperty(name)!.GetValue(snapshot)!;
            return new ScanResult(Property<Dictionary<string, string>>("Items"),
                Property<bool>("PhysicalScanComplete"), Property<DesktopPathSnapshot>("Paths"));
        }

        public void Refresh(bool clearIconCache = false)
        {
            var task = (Task)Invoke("RefreshDesktopSnapshotAsync", clearIconCache, null)!;
            _refreshes.Add(task);
            PumpUntil(() => task.IsCompleted, "桌面刷新未在预期时间内结束。");
            task.GetAwaiter().GetResult();
        }

        public void WaitForIcons() => PumpUntil(() =>
            Field<System.Collections.IDictionary>("_queuedIconLoads").Count == 0 &&
            Field<System.Collections.IDictionary>("_pendingIconVisuals").Count == 0,
            "图标排队或回填未完成。");

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

        public string IconCacheKey(string path) => (string)typeof(MainWindow).GetMethod(
            "GetIconCacheKey", PrivateInstance | BindingFlags.Static)!.Invoke(Window, [path])!;

        public string VisualKind(string path) => (string)Invoke("GetDesktopItemVisualKind", path)!;

        public Border CreateIcon(string path, string name) => (Border)Invoke("CreateIconVisual", path, name, null)!;

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
