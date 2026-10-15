using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class PhysicalFolderDropTests
{
    [TestMethod]
    [DataRow(10, true, false, false)]
    [DataRow(29.9, true, false, false)]
    [DataRow(30, true, false, true)]
    [DataRow(50, true, false, true)]
    [DataRow(69.9, true, false, true)]
    [DataRow(70, true, false, false)]
    [DataRow(89, true, false, false)]
    [DataRow(10, true, true, true)]
    [DataRow(89, true, true, true)]
    [DataRow(10, false, false, true)]
    [DataRow(89, false, false, true)]
    [DataRow(91, false, true, false)]
    public void HitTest_SeparatesFolderCenterFromInsertionEdges(
        double x, bool insideGroup, bool shift, bool expected)
    {
        Assert.AreEqual(expected, MainWindow.IsPhysicalFolderDropHit(
            new Rect(10, 20, 80, 70), new Point(x, 40), insideGroup, shift));
    }

    [STATestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Preview_CenterEdgesAndRepeatedUpdatesRemainMutuallyExclusive(bool sameGroup)
    {
        using var fixture = new PreviewFixture();
        fixture.Source.Tag = new IconTag
        {
            DisplayName = "source.txt", FullPath = @"C:\Test\source.txt",
            Group = sameGroup ? fixture.Group : new GroupInfo { Id = "other" }
        };

        fixture.Preview(new Point(50, 30));
        fixture.AssertFolderSelected();
        fixture.Preview(new Point(50, 30));
        fixture.AssertFolderSelected();

        fixture.Preview(new Point(5, 30));
        fixture.AssertInsertionSelected();
        fixture.Preview(new Point(95, 30));
        fixture.AssertInsertionSelected();
        fixture.Preview(new Point(150, 30)); // 普通文件中央仍是插入。
        fixture.AssertInsertionSelected();
        fixture.Preview(new Point(50, 30));
        fixture.AssertFolderSelected();

        fixture.Preview(new Point(350, 230));
        Assert.IsNull(fixture.Field("_activePhysicalFolderDropPath"));
        Assert.IsNull(fixture.Field("_activeGroupDropTarget"));
    }

    [STATestMethod]
    public void Preview_ClippedFolderCannotReceiveDropOutsideViewport()
    {
        using var fixture = new PreviewFixture();
        // 模拟缓存行位于裁剪视口以外，但其控件仍已实现。
        fixture.Panel.Height = 20;
        fixture.Panel.Measure(new Size(300, 20));
        fixture.Panel.Arrange(new Rect(0, 0, 300, 20));
        Assert.AreEqual(20, fixture.Panel.ActualHeight);
        fixture.Preview(new Point(50, 50));
        Assert.IsNull(fixture.Field("_activePhysicalFolderDropPath"));
    }

    [STATestMethod]
    public void Preview_SourceFolderAndShellItemsCannotBecomePhysicalMoves()
    {
        using var fixture = new PreviewFixture();
        fixture.Source.Tag = new IconTag { FullPath = PreviewFixture.FolderPath };
        fixture.Preview(new Point(50, 30));
        fixture.AssertInsertionSelected();
        fixture.Source.Tag = new IconTag { Kind = DesktopItemKind.ShellNamespace };
        fixture.Preview(new Point(50, 30));
        fixture.AssertInsertionSelected();
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Complete_FreeIconCancelRestoresPreviewBeforeOneDefaultCancelPrompt(bool collision)
    {
        using var fixture = new CompletionFixture();
        fixture.SeedCollision(collision);
        fixture.PrepareFreeDragWithNeighborPreview();
        string before = fixture.LayoutJson;
        object undo = fixture.CaptureUndo();
        int prompts = 0;

        fixture.Complete((message, title, buttons, image, defaultResult) =>
        {
            prompts++;
            fixture.AssertPrompt(message, buttons, image, defaultResult, collision);
            fixture.AssertInteractionRestored();
            fixture.AssertFreeVisualsRestored();
            fixture.AssertNoOperationOrSave();
            return MessageBoxResult.Cancel;
        });

        Assert.AreEqual(1, prompts);
        Assert.AreEqual(before, fixture.LayoutJson);
        fixture.AssertUndo(undo);
        fixture.AssertNoOperationOrSave();
        fixture.AssertSourceAndCollisionUnchanged(collision);
    }

    [STATestMethod]
    public void Complete_NonemptyGroupedIconCancelRestoresRealPanelAndKeepsOrderAndUndo()
    {
        using var fixture = new CompletionFixture();
        fixture.PrepareGroupedDrag();
        string before = fixture.LayoutJson;
        object undo = fixture.CaptureUndo();
        int prompts = 0;

        fixture.Complete((message, title, buttons, image, defaultResult) =>
        {
            prompts++;
            fixture.AssertInteractionRestored();
            fixture.AssertGroupedVisualRestored();
            fixture.AssertNoOperationOrSave();
            return MessageBoxResult.Cancel;
        });

        Assert.AreEqual(1, prompts);
        Assert.AreEqual(before, fixture.LayoutJson);
        fixture.AssertGroupedVisualRestored();
        fixture.AssertUndo(undo);
        fixture.AssertNoOperationOrSave();
        fixture.AssertSourceAndCollisionUnchanged(false);
    }

    [STATestMethod]
    public void Complete_CancelPreservesRefreshChangesMadeDuringConfirmation()
    {
        using var fixture = new CompletionFixture();
        fixture.PrepareFreeDragWithNeighborPreview();
        fixture.Complete((message, title, buttons, image, defaultResult) =>
        {
            fixture.AssertInteractionRestored();
            fixture.Layout.FreeIcons[CompletionFixture.NeighborName].X = 700;
            fixture.Rebuild();
            return MessageBoxResult.Cancel;
        });

        Assert.AreEqual(700, fixture.Layout.FreeIcons[CompletionFixture.NeighborName].X);
        fixture.AssertNoOperationOrSave();
        fixture.AssertSourceAndCollisionUnchanged(false);
    }

    [STATestMethod]
    public void Complete_CancelDoesNotStopAnUnrelatedPendingLayoutSave()
    {
        using var fixture = new CompletionFixture();
        fixture.PrepareFreeDragWithNeighborPreview();
        fixture.Invoke("SaveLayout");
        Assert.IsTrue(fixture.Field<DispatcherTimer>("_layoutSaveTimer").IsEnabled);
        fixture.Complete((message, title, buttons, image, defaultResult) => MessageBoxResult.Cancel);
        Assert.IsTrue(fixture.Field<DispatcherTimer>("_layoutSaveTimer").IsEnabled);
        Assert.IsTrue(fixture.Field<bool>("_layoutDirty"));
        Assert.AreEqual(0, fixture.BackgroundWrites);
        fixture.AssertSourceAndCollisionUnchanged(false);
    }

    [STATestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Complete_IdentityChangedDuringConfirmationRejectsQueue(bool replaceSource)
    {
        using var fixture = new CompletionFixture();
        fixture.PrepareFreeDragWithNeighborPreview();
        string before = fixture.LayoutJson;
        fixture.Complete((message, title, buttons, image, defaultResult) =>
        {
            if (replaceSource)
            {
                File.Move(fixture.SourcePath, fixture.SourcePath + ".original");
                File.WriteAllText(fixture.SourcePath, "replacement source");
            }
            else
            {
                Directory.Move(fixture.TargetPath, fixture.TargetPath + ".original");
                Directory.CreateDirectory(fixture.TargetPath);
            }
            return MessageBoxResult.OK;
        });

        Assert.AreEqual(before, fixture.LayoutJson);
        fixture.AssertNoOperationOrSave();
        Assert.IsTrue(File.Exists(fixture.SourcePath));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.TargetPath, CompletionFixture.SourceName)));
        StringAssert.Contains(fixture.Window.StatusText.Text, replaceSource ? "未排队移动" : "未执行移动");
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Complete_ConfirmQueuesTemporaryMoveOnceWithoutOverwriting(bool collision)
    {
        using var fixture = new CompletionFixture();
        fixture.SeedCollision(collision);
        fixture.PrepareFreeDragWithNeighborPreview();
        int prompts = 0;
        fixture.Complete((message, title, buttons, image, defaultResult) =>
        {
            prompts++;
            fixture.AssertPrompt(message, buttons, image, defaultResult, collision);
            fixture.AssertInteractionRestored();
            fixture.AssertNoOperationOrSave();
            return MessageBoxResult.OK;
        });
        fixture.PumpUntil(() => fixture.Field<int>("_fileOperationCount") == 0);

        Assert.AreEqual(1, prompts);
        Assert.IsFalse(File.Exists(fixture.SourcePath));
        string moved = Path.Combine(fixture.TargetPath, collision ? "source (2).txt" : CompletionFixture.SourceName);
        Assert.AreEqual("source content", File.ReadAllText(moved));
        if (collision)
            Assert.AreEqual("existing content", File.ReadAllText(Path.Combine(fixture.TargetPath, CompletionFixture.SourceName)));
        Assert.IsTrue(File.Exists(fixture.JournalPath));
        Assert.AreEqual(1, fixture.Field<FileOperationJournalData>("_fileOperationJournal").Entries.Count);
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Complete_PendingMoveRejectsWorkspaceCreationAndPreservesSaveState(bool saveAlreadyScheduled)
    {
        using var fixture = new CompletionFixture();
        fixture.PrepareGroupedDrag();
        fixture.Invoke("ResetAllInteractionState", true);
        fixture.Layout.Workspaces.Clear();
        fixture.Layout.ActiveWorkspaceId = null;
        Assert.IsTrue(fixture.Window.TryCreateWorkspace("A", out string initialError), initialError);
        string originalWorkspace = fixture.Layout.ActiveWorkspaceId!;
        string[] originalOrder = fixture.Layout.Groups.Single().ItemNames.ToArray();
        IconPosition originalAutoPosition = fixture.Layout.AutoClassificationOriginalPositions[CompletionFixture.SourceName];
        fixture.DetachGroupedSource();
        if (saveAlreadyScheduled)
            fixture.Invoke("SaveLayout");
        else
        {
            fixture.Field<DispatcherTimer>("_layoutSaveTimer").Stop();
            typeof(MainWindow).GetField("_layoutDirty", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(fixture.Window, false);
        }

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<int> barrier = fixture.Field<FileOperationService>("_fileOperationService")
            .Enqueue<int>(token => { entered.Set(); release.Wait(token); return 0; });
        bool created = false;
        string createError = string.Empty;
        string switchError = string.Empty;
        string layoutBefore = string.Empty;
        string layoutAfter = string.Empty;
        string? activeBefore = null;
        string? activeAfter = null;
        int countBefore = 0, countAfter = 0, writesBefore = 0, writesAfter = 0;
        bool dirtyBefore = false, dirtyAfter = false, timerBefore = false, timerAfter = false;
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)), "临时队列应进入受控等待。");
            fixture.Complete((_, _, _, _, _) => MessageBoxResult.OK);
            Assert.AreEqual(1, fixture.Field<int>("_fileOperationCount"), "使用实际排队的临时移动。");
            Assert.IsTrue(File.Exists(fixture.SourcePath));
            Assert.IsFalse(fixture.Window.TryActivateWorkspace(originalWorkspace, out switchError));
            layoutBefore = fixture.LayoutJson;
            activeBefore = fixture.Layout.ActiveWorkspaceId;
            countBefore = fixture.Layout.Workspaces.Count;
            dirtyBefore = fixture.Field<bool>("_layoutDirty");
            timerBefore = fixture.Field<DispatcherTimer>("_layoutSaveTimer").IsEnabled;
            writesBefore = fixture.BackgroundWrites;
            created = fixture.Window.TryCreateWorkspace("B", out createError);
            layoutAfter = fixture.LayoutJson;
            activeAfter = fixture.Layout.ActiveWorkspaceId;
            countAfter = fixture.Layout.Workspaces.Count;
            dirtyAfter = fixture.Field<bool>("_layoutDirty");
            timerAfter = fixture.Field<DispatcherTimer>("_layoutSaveTimer").IsEnabled;
            writesAfter = fixture.BackgroundWrites;
        }
        finally
        {
            release.Set();
        }

        // Drain real operations before asserting the rejected action, including on RED.
        fixture.PumpUntil(() => fixture.Field<int>("_fileOperationCount") == 0 && barrier.IsCompleted);
        Assert.AreEqual("source content", File.ReadAllText(Path.Combine(fixture.TargetPath, CompletionFixture.SourceName)));
        Assert.IsFalse(File.Exists(fixture.SourcePath));
        Assert.IsTrue((bool)fixture.Invoke("QueueUndoLastFileMove")!);
        fixture.PumpUntil(() => fixture.Field<int>("_fileOperationCount") == 0 && File.Exists(fixture.SourcePath));
        string[] orderAfterUndo = fixture.Layout.Groups.Single().ItemNames.ToArray();
        bool hasAutoPosition = fixture.Layout.AutoClassificationOriginalPositions.TryGetValue(CompletionFixture.SourceName, out IconPosition? autoPositionAfterUndo);
        Assert.AreEqual("source content", File.ReadAllText(fixture.SourcePath));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.TargetPath, CompletionFixture.SourceName)));
        Assert.IsTrue(fixture.Window.TryCreateWorkspace("after-task", out string completedError), completedError);
        Assert.AreNotEqual(originalWorkspace, fixture.Layout.ActiveWorkspaceId);
        Assert.AreEqual("after-task", fixture.Layout.Workspaces.Single(workspace => workspace.Id == fixture.Layout.ActiveWorkspaceId).Name);
        Assert.IsTrue(fixture.Window.TryActivateWorkspace(originalWorkspace, out string completedSwitchError), completedSwitchError);

        Assert.IsFalse(created, "文件任务未完成时，新建并激活不得绕过工作区切换保护。");
        Assert.AreEqual(switchError, createError);
        Assert.AreEqual(layoutBefore, layoutAfter, "拒绝不得修改任何工作区快照或根布局。");
        Assert.AreEqual(activeBefore, activeAfter);
        Assert.AreEqual(countBefore, countAfter);
        Assert.AreEqual(dirtyBefore, dirtyAfter);
        Assert.AreEqual(timerBefore, timerAfter, "包括保留原先已有的待保存任务。");
        Assert.AreEqual(saveAlreadyScheduled, timerBefore);
        Assert.AreEqual(writesBefore, writesAfter);
        CollectionAssert.AreEqual(originalOrder, orderAfterUndo);
        Assert.IsTrue(hasAutoPosition);
        Assert.AreEqual(originalAutoPosition.X, autoPositionAfterUndo!.X);
        Assert.AreEqual(originalAutoPosition.Y, autoPositionAfterUndo.Y);
    }

    private sealed class CompletionFixture : IDisposable
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        public const string SourceName = "source.txt";
        public const string NeighborName = "neighbor.txt";
        public string Root { get; } = Path.Combine(TestProjectFiles.Root, "artifacts", "physical-folder-drop", Guid.NewGuid().ToString("N"));
        public string SourcePath => Path.Combine(Root, SourceName);
        public string TargetPath => Path.Combine(Root, "target");
        public string JournalPath => Path.Combine(Root, "operation-journal.json");
        public MainWindow Window { get; }
        public AppLayoutData Layout => Field<AppLayoutData>("_appLayout");
        public string LayoutJson => JsonSerializer.Serialize(Layout);
        public int BackgroundWrites;
        private readonly OffscreenSource _source;
        private readonly SynchronizationContext? _previousSynchronizationContext;
        private FrameworkElement _dragged = null!;
        private FrameworkElement _neighbor = null!;
        private GroupInfo? _group;

        public CompletionFixture()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(TargetPath);
            File.WriteAllText(SourcePath, "source content");
            Window = new MainWindow(false, _ => Interlocked.Increment(ref BackgroundWrites), new DesktopPathResolver(_ => Root));
            _previousSynchronizationContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Window.Dispatcher));
            Set("_desktopSnapshotInitialized", true);
            Set("_isSafeModeActive", true);
            Set("_desktopGeometry", new DesktopGeometry([new DesktopMonitorRegion { DeviceName = "test", Bounds = new Rect(0, 0, 1200, 800), WorkArea = new Rect(0, 0, 1200, 800), IsPrimary = true }]));
            Set("_layoutFilePath", Path.Combine(Root, "layout.json"));
            Set("_layoutExitRecoveryPath", Path.Combine(Root, "layout-recovery.json"));
            Set("_sessionMarkerPath", Path.Combine(Root, "session.json"));
            Set("_fileOperationJournalPath", JournalPath);
            Set("_fileOperationJournalStore", new FileOperationJournalStore(JournalPath));
            AppDiagnostics diagnostics = Field<AppDiagnostics>("_diagnostics");
            typeof(AppDiagnostics).GetField("_logDirectory", PrivateInstance)!.SetValue(diagnostics, Root);
            typeof(AppDiagnostics).GetField("_logPath", PrivateInstance)!.SetValue(diagnostics, Path.Combine(Root, "diagnostics.log"));
            Layout.Groups.Clear();
            Layout.FolderPortals.Clear();
            Layout.RecycleBinWidget.IsVisible = false;
            Layout.SnapToGrid = false;
            Layout.IsEditMode = true;
            Layout.FreeIcons["target"] = new IconPosition { X = 600, Y = 480 };
            Field<Dictionary<string, string>>("_desktopItems")[SourceName] = SourcePath;
            Field<Dictionary<string, string>>("_desktopItems")["target"] = TargetPath;
            Field<Dictionary<string, System.Windows.Media.Imaging.BitmapSource?>>("_iconCache")["ext:.txt"] = null;
            Window.RootGrid = new Grid();
            Window.IconCanvas = new Canvas { Width = 1200, Height = 800 };
            Window.RootGrid.Children.Add(Window.IconCanvas);
            _source = new OffscreenSource(Window.RootGrid);
        }

        public void SeedCollision(bool collision)
        {
            if (collision) File.WriteAllText(Path.Combine(TargetPath, SourceName), "existing content");
        }

        public void PrepareFreeDragWithNeighborPreview()
        {
            string neighborPath = Path.Combine(Root, NeighborName);
            File.WriteAllText(neighborPath, "neighbor content");
            Field<Dictionary<string, string>>("_desktopItems")[NeighborName] = neighborPath;
            Layout.FreeIcons[SourceName] = new IconPosition { X = 40, Y = 50 };
            Layout.FreeIcons[NeighborName] = new IconPosition { X = 140, Y = 50 };
            Layout.AutoClassificationOriginalPositions[SourceName] = new IconPosition { X = 40, Y = 50 };
            _dragged = new Border { Tag = new IconTag { DisplayName = SourceName, FullPath = SourcePath } };
            _neighbor = new Border { Tag = new IconTag { DisplayName = NeighborName, FullPath = neighborPath } };
            Window.IconCanvas.Children.Add(_dragged);
            Window.IconCanvas.Children.Add(_neighbor);
            Field<Dictionary<string, FrameworkElement>>("_freeIconVisuals")[SourceName] = _dragged;
            Field<Dictionary<string, FrameworkElement>>("_freeIconVisuals")[NeighborName] = _neighbor;
            Set("_pushPreviewOriginalPositions", Layout.FreeIcons.ToDictionary(pair => pair.Key, pair => new IconPosition { X = pair.Value.X, Y = pair.Value.Y }));
            Set("_pushPreviewPositions", new Dictionary<string, IconPosition> { [SourceName] = new() { X = 440, Y = 450 }, [NeighborName] = new() { X = 240, Y = 50 } });
            Set("_pushPreviewDraggedName", SourceName);
            Canvas.SetLeft(_neighbor, 240);
            Canvas.SetTop(_neighbor, 50);
            SetDrag();
        }

        public void PrepareGroupedDrag()
        {
            _group = new GroupInfo { Id = "nonempty", Name = "非空测试分类", X = 30, Y = 30, Width = 360, Height = 240, IsSizeLocked = true, ItemNames = ["before.txt", SourceName, "after.txt"] };
            Layout.Groups.Add(_group);
            foreach (string name in _group.ItemNames.Where(name => name != SourceName))
            {
                string path = Path.Combine(Root, name);
                File.WriteAllText(path, name);
                Field<Dictionary<string, string>>("_desktopItems")[name] = path;
            }
            Layout.AutoClassificationOriginalPositions[SourceName] = new IconPosition { X = 40, Y = 50 };
            Rebuild();
            DetachGroupedSource();
        }

        public void DetachGroupedSource()
        {
            VirtualizingGroupPanel panel = GroupPanel;
            panel.Measure(new Size(panel.Width, 160));
            panel.Arrange(new Rect(0, 0, panel.Width, 160));
            _dragged = panel.Children.OfType<FrameworkElement>().Single(element => element.Tag is IconTag tag && tag.DisplayName == SourceName);
            Assert.IsTrue(panel.DetachForDrag(_dragged));
            Window.IconCanvas.Children.Add(_dragged);
            Set("_groupedIconDragSourceGroup", _group!);
            SetDrag();
        }

        private VirtualizingGroupPanel GroupPanel => Field<Dictionary<string, VirtualizingGroupPanel>>("_groupItemPanels")[_group!.Id];
        private void SetDrag()
        {
            Canvas.SetLeft(_dragged, 440);
            Canvas.SetTop(_dragged, 450);
            Set("_draggedElement", _dragged);
            Set("_dragOriginalPosition", new IconPosition { X = 40, Y = 50 });
            Set("_dragAllowsLayoutMove", true);
            Set("_activePhysicalFolderDropPath", TargetPath);
        }

        public object CaptureUndo()
        {
            object snapshot = Invoke("CaptureGroupLayoutSnapshot")!;
            Set("_lastSmartLayoutSnapshot", snapshot);
            Window.UndoSmartLayoutButton.IsEnabled = true;
            return snapshot;
        }

        public void AssertUndo(object snapshot)
        {
            Assert.AreSame(snapshot, Field<object>("_lastSmartLayoutSnapshot"));
            Assert.IsTrue(Window.UndoSmartLayoutButton.IsEnabled);
        }

        public void AssertPrompt(string message, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult, bool collision)
        {
            StringAssert.Contains(message, Path.GetFullPath(TargetPath));
            StringAssert.Contains(message, SourceName);
            Assert.AreEqual(MessageBoxButton.OKCancel, buttons);
            Assert.AreEqual(MessageBoxImage.Question, image);
            Assert.AreEqual(MessageBoxResult.Cancel, defaultResult);
            Assert.AreEqual(collision, message.Contains("自动生成", StringComparison.Ordinal));
        }

        public void AssertInteractionRestored()
        {
            Assert.IsNull(Field<object?>("_draggedElement"));
            Assert.IsNull(Field<object?>("_groupedIconDragSourceGroup"));
            Assert.IsNull(Field<object?>("_dragOriginalPosition"));
            Assert.IsNull(Field<object?>("_pushPreviewPositions"));
            Assert.IsNull(Field<object?>("_pushPreviewOriginalPositions"));
            Assert.IsNull(Field<object?>("_activePhysicalFolderDropPath"));
            Assert.IsFalse(Field<bool>("_dragAllowsLayoutMove"));
        }

        public void AssertFreeVisualsRestored()
        {
            Assert.AreEqual(40, Canvas.GetLeft(_dragged));
            Assert.AreEqual(50, Canvas.GetTop(_dragged));
            Assert.AreEqual(140, Canvas.GetLeft(_neighbor));
            Assert.AreEqual(50, Canvas.GetTop(_neighbor));
        }

        public void AssertGroupedVisualRestored()
        {
            // 离屏重建没有 HWND 布局循环；仅完成测量，不再次重建或恢复模型。
            Assert.IsTrue(GroupPanel.IsStable, "取消后不能保留源槽位的拖动抑制。");
            GroupPanel.Measure(new Size(GroupPanel.Width, 160));
            GroupPanel.Arrange(new Rect(0, 0, GroupPanel.Width, 160));
            CollectionAssert.AreEqual(new[] { "before.txt", SourceName, "after.txt" }, GroupPanel.GetItemNamesSnapshot().ToArray());
            Assert.AreEqual(3, GroupPanel.Children.OfType<FrameworkElement>().Count(element => element.Tag is IconTag));
            FrameworkElement restored = GroupPanel.Children.OfType<FrameworkElement>().Single(element => element.Tag is IconTag tag && tag.DisplayName == SourceName);
            Assert.AreSame(GroupPanel, VisualTreeHelper.GetParent(restored));
        }

        public void AssertNoOperationOrSave()
        {
            Assert.AreEqual(0, Field<int>("_fileOperationCount"));
            Assert.AreEqual(0, Field<HashSet<string>>("_pendingFileOperationPaths").Count);
            Assert.AreEqual(0, Field<FileOperationJournalData>("_fileOperationJournal").Entries.Count);
            Assert.IsFalse(File.Exists(JournalPath));
            Assert.IsFalse(Field<DispatcherTimer>("_layoutSaveTimer").IsEnabled);
            Assert.IsFalse(Field<bool>("_layoutDirty"));
            Assert.AreEqual(0, BackgroundWrites);
        }

        public void AssertSourceAndCollisionUnchanged(bool collision)
        {
            Assert.AreEqual("source content", File.ReadAllText(SourcePath));
            string destination = Path.Combine(TargetPath, SourceName);
            if (collision) Assert.AreEqual("existing content", File.ReadAllText(destination));
            else Assert.IsFalse(File.Exists(destination));
        }

        public T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, PrivateInstance)!.GetValue(Window)!;
        private void Set(string name, object value) => typeof(MainWindow).GetField(name, PrivateInstance)!.SetValue(Window, value);
        public object? Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, PrivateInstance)!.Invoke(Window, args);
        public void Rebuild()
        {
            Invoke("RebuildDesktopIconsCore", new HashSet<string>(), false);
            Window.RootGrid.Measure(new Size(1200, 800));
            Window.RootGrid.Arrange(new Rect(0, 0, 1200, 800));
        }

        public void Complete(Func<string, string, MessageBoxButton, MessageBoxImage, MessageBoxResult, MessageBoxResult> confirm)
        {
            var targets = Field<Dictionary<string, FrameworkElement>>("_physicalFolderDropTargets");
            if (!targets.ContainsKey(TargetPath))
            {
                var target = new Border { Width = 100, Height = 100, Tag = new IconTag { DisplayName = "target", FullPath = TargetPath } };
                Canvas.SetLeft(target, 600);
                Canvas.SetTop(target, 480);
                Window.IconCanvas.Children.Add(target);
                targets[TargetPath] = target;
            }
            Window.RootGrid.Measure(new Size(1200, 800));
            Window.RootGrid.Arrange(new Rect(0, 0, 1200, 800));
            MethodInfo? method = typeof(MainWindow).GetMethod("CompleteIconDrop", PrivateInstance);
            Assert.IsNotNull(method, "MouseUp 和测试应共同执行最终命中、确认与布局保存的完整完成路径。");
            method.Invoke(Window, [new Point(610, 490), confirm]);
        }

        public void PumpUntil(Func<bool> completed)
        {
            var frame = new DispatcherFrame();
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { if (completed() || elapsed.Elapsed > TimeSpan.FromSeconds(10)) frame.Continue = false; };
            timer.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
            Assert.IsTrue(completed(), "临时文件操作应在期限内完成。");
        }

        public void Dispose()
        {
            Set("_isClosing", true);
            foreach (FieldInfo field in typeof(MainWindow).GetFields(PrivateInstance).Where(field => field.FieldType == typeof(DispatcherTimer)))
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
            _source.Dispose();
            SynchronizationContext.SetSynchronizationContext(_previousSynchronizationContext);
            string parent = Path.GetFullPath(Path.Combine(TestProjectFiles.Root, "artifacts", "physical-folder-drop")) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(Root);
            Assert.IsTrue(target.StartsWith(parent, StringComparison.OrdinalIgnoreCase));
            Directory.Delete(target, recursive: true);
        }
    }
    private sealed class PreviewFixture : IDisposable
    {
        public const string FolderPath = @"C:\Test\folder";
        private readonly MainWindow _window = new(startQuietly: false);
        private readonly OffscreenSource _source;
        public GroupInfo Group { get; } = new() { Id = "group", Name = "测试" };
        public Border Source { get; } = new() { Tag = new IconTag { FullPath = @"C:\Test\source.txt" } };
        public VirtualizingGroupPanel Panel { get; }

        public PreviewFixture()
        {
            // 仅离屏测量测试控件，不显示窗口、不访问或移动真实文件。
            _window.IconCanvas = new Canvas();
            _source = new OffscreenSource(_window.IconCanvas);
            Panel = new VirtualizingGroupPanel(
                [new("folder", FolderPath), new("file.txt", @"C:\Test\file.txt")],
                item =>
                {
                    var visual = new Border
                    {
                        Tag = new IconTag { FullPath = item.FullPath, DisplayName = item.DisplayName, Group = Group }
                    };
                    if (item.FullPath == FolderPath)
                    {
                        ((Dictionary<string, FrameworkElement>)Field("_physicalFolderDropTargets")!)[FolderPath] = visual;
                    }
                    return visual;
                }, _ => { }, 3, 100, 80, 0)
            { Width = 300, Height = 160, VerticalAlignment = VerticalAlignment.Top };
            var target = new Border { Width = 300, Height = 200, Child = Panel };
            _window.IconCanvas.Children.Add(target);
            ((AppLayoutData)Field("_appLayout")!).Groups.Add(Group);
            ((Dictionary<string, FrameworkElement>)Field("_groupDropTargets")!)[Group.Id] = target;
            ((Dictionary<string, VirtualizingGroupPanel>)Field("_groupItemPanels")!)[Group.Id] = Panel;
            Layout();
            Assert.IsTrue(target.IsVisible,
                $"离屏目标可见性：canvas={_window.IconCanvas.IsVisible}，target={target.IsVisible}，尺寸={target.RenderSize}，面板={Panel.RenderSize}");
            object[] boundsArguments = [target, Rect.Empty];
            bool hasBounds = (bool)typeof(MainWindow)
                .GetMethod("TryGetElementBoundsOnCanvas", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(_window, boundsArguments)!;
            Assert.IsTrue(hasBounds && ((Rect)boundsArguments[1]).Contains(new Point(50, 30)),
                $"目标边界：{boundsArguments[1]}，尺寸={target.RenderSize}，位置={target.TranslatePoint(new Point(), _window.IconCanvas)}");
        }

        public void Layout()
        {
            _window.IconCanvas.Measure(new Size(400, 300));
            _window.IconCanvas.Arrange(new Rect(0, 0, 400, 300));
        }

        public object? Field(string name) => typeof(MainWindow)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_window);

        public void Preview(Point point)
        {
            foreach (string method in new[] { "UpdateGroupDropPreview", "UpdatePhysicalFolderDropPreview" })
            {
                typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_window, [point, Source]);
            }
        }

        public void AssertFolderSelected()
        {
            Assert.AreEqual(FolderPath, Field("_activePhysicalFolderDropPath"));
            Assert.IsNull(Field("_activeGroupDropTarget"));
            Assert.IsFalse(Panel.Children.OfType<Border>().Any(child => !child.IsHitTestVisible));
            StringAssert.Contains(_window.StatusText.Text, "松开可移入真实文件夹");
        }

        public void AssertInsertionSelected()
        {
            Assert.IsNull(Field("_activePhysicalFolderDropPath"));
            Assert.AreSame(Group, Field("_activeGroupDropTarget"));
            Assert.IsTrue(Panel.Children.OfType<Border>().Any(child => !child.IsHitTestVisible));
        }

        public void Dispose()
        {
            ((DispatcherTimer)Field("_layoutSaveTimer")!).Stop();
            _source.Dispose();
        }
    }

    private sealed class OffscreenSource : PresentationSource, IDisposable
    {
        private readonly Visual _root;
        private bool _disposed;
        public OffscreenSource(Visual root)
        {
            _root = root;
            AddSource();
            RootChanged(null, root);
        }

        public override Visual RootVisual { get => _root; set => throw new NotSupportedException(); }
        public override bool IsDisposed => _disposed;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
        public void Dispose()
        {
            RootChanged(_root, null);
            RemoveSource();
            _disposed = true;
        }
    }
}
