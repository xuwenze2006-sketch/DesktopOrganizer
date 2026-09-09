using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class LightDesktopLayoutTests
{
    [STATestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    [DataRow(2.0)]
    public void LightLayout_HasTwoStableColumns_CompactEntries_AndReadableRows(double scale)
    {
        using var f = new Fixture(scale);
        GroupInfo shortcuts = f.Group("快捷方式"), research = f.Group("科研软件"), projects = f.Group("开发项目");
        Assert.AreEqual(shortcuts.X, research.X);
        Assert.AreEqual(shortcuts.Y, projects.Y);
        Assert.AreEqual(4, f.Invoke("GetDesiredGroupColumnCount", shortcuts));
        Assert.AreEqual(3, f.Invoke("GetDesiredGroupColumnCount", projects));
        Assert.IsTrue(projects.X > shortcuts.X + shortcuts.Width);
        foreach (GroupInfo group in f.Layout.Groups)
        {
            Rect bounds = (Rect)f.Invoke("GetGroupBounds", group)!;
            Assert.IsTrue(bounds.Bottom <= 800);
            foreach (GroupInfo other in f.Layout.Groups.Where(other => other != group))
                Assert.IsFalse(bounds.IntersectsWith((Rect)f.Invoke("GetGroupBounds", other)!));
        }
        Assert.HasCount(5, f.Field<Dictionary<string, Border>>("_lightDesktopEntries"));
        var panels = f.Field<Dictionary<string, VirtualizingGroupPanel>>("_groupItemPanels");
        foreach (GroupInfo group in new[] { shortcuts, research, projects })
        {
            VirtualizingGroupPanel panel = panels[group.Id];
            Assert.IsTrue(panel.Children.Count > 0);
            int columns = (int)f.Invoke("GetDesiredGroupColumnCount", group)!;
            int visibleCount = Math.Min(group.ItemNames.Count, LightDesktopLayoutPolicy.Rows(group.DesktopRole) * columns);
            foreach (Border tile in panel.Children.Cast<Border>().Take(visibleCount))
            {
                var label = (TextBlock)((Border)((StackPanel)tile.Child).Children[1]).Child;
                Assert.IsTrue(label.ActualHeight >= 32 - 1 / scale);
                Assert.IsTrue(label.TranslatePoint(new Point(0, label.ActualHeight), panel).Y <= panel.ActualHeight + 1 / scale,
                    "可见最后一行的双行文字必须完整落在正文内。" );
            }
        }
        Border section = f.Field<List<FrameworkElement>>("_lightDesktopDecorations").OfType<Border>().Single();
        FrameworkElement projectVisual = f.Visual(projects);
        Assert.AreEqual(scale, VisualTreeHelper.GetDpi(projectVisual).DpiScaleX);
        Assert.AreEqual(scale, VisualTreeHelper.GetDpi(section).DpiScaleX);
        Assert.AreEqual(Canvas.GetLeft(projectVisual) + projectVisual.ActualWidth,
            Canvas.GetLeft(section) + section.ActualWidth, 0.01,
            "连接底板须对齐包含边框的实际右边缘。");
        Assert.AreEqual(projects.Y + projects.Height,
            Canvas.GetTop(section), 1 / scale, "底板只托入项目底部留白，不能移动入口或缩减正文高度。");
        RenderTargetBitmap frame = f.RenderFrame();
        Point projectOrigin = projectVisual.TranslatePoint(new Point(), f.Window.RootGrid);
        int seamY = (int)Math.Round(section.TranslatePoint(new Point(), f.Window.RootGrid).Y * scale);
        var seamPixels = new byte[4 * 8];
        frame.CopyPixels(new Int32Rect((int)((projects.X + projects.Width / 2) * scale), seamY - 1, 1, 8),
            seamPixels, 4, 0);
        int[] red = Enumerable.Range(0, 8).Select(index => (int)seamPixels[index * 4 + 2]).ToArray();
        f.Render($"light-desktop-{scale}.png");
        Assert.IsTrue(red.Max() - red.Min() < 40,
            $"接缝只能是浅分隔线，不能透出深色壁纸形成裂缝。像素={string.Join(',', red)}");
        foreach (double edge in new[] { projectOrigin.X, projectOrigin.X + projectVisual.ActualWidth })
        {
            int edgeX = (int)Math.Round(edge * scale) - 4;
            var above = new byte[8 * 4];
            var below = new byte[8 * 4];
            frame.CopyPixels(new Int32Rect(edgeX, seamY - 8, 8, 1), above, above.Length, 0);
            frame.CopyPixels(new Int32Rect(edgeX, seamY + 12, 8, 1), below, below.Length, 0);
            int difference = Enumerable.Range(0, above.Length).Where(index => index % 4 != 3)
                .Max(index => Math.Abs(above[index] - below[index]));
            Assert.IsTrue(difference < 20,
                $"接缝上下的实际着色边缘应一致。edge={edge}, delta={difference}, dpi={VisualTreeHelper.GetDpi(projectVisual).DpiScaleX}, widths={projectVisual.ActualWidth}/{section.ActualWidth}");
        }
    }

    [STATestMethod]
    public void Section_DetachesAndReconnects_WhenProjectCollapsesOrDragIsCancelled()
    {
        using var f = new Fixture();
        GroupInfo project = f.Group("开发项目");
        string anchors = JsonSerializer.Serialize(f.Layout.Groups.Select(g => new { g.Id, g.X, g.Y }));
        Assert.AreEqual(0, ((Border)f.Visual(project)).CornerRadius.BottomLeft);
        f.Invoke("ToggleGroupCollapsed", project); f.Measure();
        Assert.IsTrue(((Border)f.Visual(project)).CornerRadius.BottomLeft > 0);
        Assert.IsTrue(f.Field<List<FrameworkElement>>("_lightDesktopDecorations")
            .OfType<Border>().Single().CornerRadius.TopLeft > 0);
        f.Invoke("ToggleGroupCollapsed", project); f.Measure();
        Assert.AreEqual(0, ((Border)f.Visual(project)).CornerRadius.BottomLeft);

        f.SetField("_draggedElement", f.Visual(project));
        f.SetField("_draggedGroup", project);
        f.SetField("_draggedIsGroup", true);
        f.SetField("_groupDragMoved", true);
        f.SetField("_groupDragStartPosition", new Point(project.X, project.Y));
        Canvas.SetLeft(f.Visual(project), project.X + 60);
        f.Invoke("RefreshLightDesktopSection"); f.Measure();
        Assert.IsTrue(((Border)f.Visual(project)).CornerRadius.BottomLeft > 0);
        f.Invoke("CompleteGroupDrag", false); f.Measure();
        Assert.AreEqual(0, ((Border)f.Visual(project)).CornerRadius.BottomLeft);
        Assert.AreEqual(anchors, JsonSerializer.Serialize(f.Layout.Groups.Select(g => new { g.Id, g.X, g.Y })));
    }

    [STATestMethod]
    public void Section_Detaches_WhenProjectIsResizedOrEntriesAreMovedAway()
    {
        using var f = new Fixture();
        GroupInfo project = f.Group("开发项目");
        double originalWidth = project.Width;
        f.Layout.IsEditMode = true;
        var thumb = ((Grid)((Border)f.Visual(project)).Child).Children
            .OfType<System.Windows.Controls.Primitives.Thumb>().Single();
        f.Invoke("ResizeThumb_DragDelta", thumb,
            new System.Windows.Controls.Primitives.DragDeltaEventArgs(30, 0));
        f.Measure();
        Assert.IsTrue(((Border)f.Visual(project)).CornerRadius.BottomLeft > 0);

        project.IsSizeLocked = false;
        project.Width = originalWidth;
        f.Invoke("RebuildDesktopIcons"); f.Measure();
        Assert.AreEqual(0, ((Border)f.Visual(project)).CornerRadius.BottomLeft);
        foreach (GroupInfo entry in f.Layout.Groups.Where(g => g.DesktopRole == DesktopZoneRole.Other))
            entry.X += 60;
        f.Invoke("RefreshLightDesktopEntries"); f.Measure();
        Assert.IsTrue(((Border)f.Visual(project)).CornerRadius.BottomLeft > 0);
        Assert.IsTrue(f.Field<List<FrameworkElement>>("_lightDesktopDecorations")
            .OfType<Border>().Single().CornerRadius.TopLeft > 0);
        f.Render("light-desktop-detached.png");
    }

    [STATestMethod]
    public void ItemCountAndRename_DoNotChangeColumnsViewportOrAnchors()
    {
        using var f = new Fixture();
        GroupInfo group = f.Group("开发项目");
        string before = JsonSerializer.Serialize(f.Layout.Groups.Select(g => new { g.Id, g.X, g.Y, g.Width, g.Height }));
        group.Name = "我的项目";
        f.AddItem(group, "新增双行项目名称.txt");
        f.Invoke("RebuildDesktopIcons"); f.Measure();
        Assert.AreEqual(3, f.Invoke("GetDesiredGroupColumnCount", group));
        Assert.AreEqual(before, JsonSerializer.Serialize(f.Layout.Groups.Select(g => new { g.Id, g.X, g.Y, g.Width, g.Height })));
    }

    [STATestMethod]
    public void Drawer_OpenSwitchRefreshSearchAndClose_KeepAllAnchors()
    {
        using var f = new Fixture();
        GroupInfo documents = f.Group("文档"), images = f.Group("图片");
        string before = JsonSerializer.Serialize(f.Layout.Groups);
        f.Invoke("ToggleGroupCollapsed", documents); f.Measure();
        Assert.AreEqual(documents.Id, f.Field<string>("_lightDesktopDrawerId"));
        Assert.IsTrue(documents.IsCollapsed);
        Assert.IsTrue(f.Visual(documents).IsVisible);
        Assert.IsTrue(f.Field<Dictionary<string, Border>>("_lightDesktopEntries")[documents.Id].IsVisible);
        Assert.IsTrue(Canvas.GetLeft(f.Visual(documents)) > f.Group("开发项目").X);
        f.Invoke("ToggleGroupCollapsed", images); f.Measure();
        Assert.IsFalse(f.Visual(documents).IsVisible);
        Assert.IsTrue(f.Visual(images).IsVisible);
        f.Invoke("ClearSelectionFromKeyboard"); f.Measure();
        Assert.IsFalse(f.Visual(images).IsVisible);
        f.Window.LocateDesktopSearchResult(documents.ItemNames[0]); f.Measure();
        Assert.AreEqual(documents.Id, f.Field<string>("_lightDesktopDrawerId"));
        Assert.AreEqual(before, JsonSerializer.Serialize(f.Layout.Groups));
        f.AddItem(documents, "后来新增的双行文档.txt");
        f.Invoke("RebuildDesktopIcons"); f.Measure();
        Assert.IsTrue(f.Visual(documents).IsVisible, "刷新重建卡片仍保留点击展开状态。");
        f.Invoke("RequestGroupPeek", f.Group("快捷方式").Id);
        Assert.IsNull(f.Field<string?>("_pendingGroupPeekId"));
        f.Invoke("DismissLightDesktopDrawerFromPointer", f.Window.RootGrid);
        Assert.IsNull(f.Field<string?>("_lightDesktopDrawerId"));
    }

    [STATestMethod]
    public void Drawer_BlocksCoveredGroupAndPhysicalFolderDrop_WithoutFileOperations()
    {
        using var f = new Fixture();
        GroupInfo documents = f.Group("文档"), images = f.Group("图片");
        f.Invoke("OpenLightDesktopDrawer", documents); f.Measure();
        FrameworkElement drawer = f.Visual(documents);
        Point point = drawer.TranslatePoint(new Point(50, 65), f.Window.IconCanvas);
        var coveredGroup = new Border { Width = 100, Height = 100, Background = Brushes.Beige, Tag = images };
        Canvas.SetLeft(coveredGroup, point.X - 50); Canvas.SetTop(coveredGroup, point.Y - 50);
        Panel.SetZIndex(coveredGroup, 100); f.Window.IconCanvas.Children.Add(coveredGroup);
        f.Field<Dictionary<string, FrameworkElement>>("_groupDropTargets")[images.Id] = coveredGroup;
        var coveredFolder = new Border { Width = 80, Height = 84, Background = Brushes.Yellow,
            Tag = new IconTag { DisplayName = "folder", FullPath = @"C:\Test\folder" } };
        Canvas.SetLeft(coveredFolder, point.X - 40); Canvas.SetTop(coveredFolder, point.Y - 42);
        f.Window.IconCanvas.Children.Add(coveredFolder);
        f.Field<Dictionary<string, FrameworkElement>>("_physicalFolderDropTargets")[@"C:\Test\folder"] = coveredFolder;
        f.Measure();
        var dragged = new Border { Tag = new IconTag { DisplayName = "source.txt", FullPath = @"C:\Test\source.txt" } };
        Assert.AreEqual(450, Panel.GetZIndex(drawer));
        Assert.AreSame(drawer, f.Field<Dictionary<string, FrameworkElement>>("_groupDropTargets")[documents.Id]);
        Assert.IsTrue(drawer.IsVisible);
        f.Invoke("UpdateGroupDropPreview", point, dragged);
        Assert.AreSame(documents, f.Field<GroupInfo>("_activeGroupDropTarget"),
            $"point={point}, drawer={Canvas.GetLeft(drawer)},{Canvas.GetTop(drawer)} {drawer.ActualWidth}x{drawer.ActualHeight}, selected={f.Field<GroupInfo>("_activeGroupDropTarget").Name}");
        f.Invoke("UpdatePhysicalFolderDropPreview", point, dragged);
        Assert.IsNull(f.Field<string?>("_activePhysicalFolderDropPath"));
        Assert.AreSame(documents, f.Field<GroupInfo>("_activeGroupDropTarget"));
    }

    [STATestMethod]
    public void SmartLayout_UndoAndRoundTrips_PreserveRole_LeaveCustomLockedGroupAlone()
    {
        using var f = new Fixture(arrange: false);
        var custom = new GroupInfo { Name = "自定义固定组", X = 1200, Y = 30, Width = 230, Height = 160, IsSizeLocked = true };
        f.Layout.Groups.Add(custom);
        object snapshot = f.Invoke("CaptureGroupLayoutSnapshot")!;
        Assert.IsTrue((bool)f.Invoke("ArrangeGroupsSmartly")!);
        Assert.AreEqual(1200, custom.X); Assert.AreEqual(230, custom.Width);
        var restored = LayoutJsonSerializer.Deserialize(JsonSerializer.Serialize(f.Layout)).Layout;
        Assert.AreEqual(DesktopZoneRole.Projects, restored.Groups.Single(g => g.Name == "开发项目").DesktopRole);
        var workspace = WorkspaceLayoutManager.CreateAndActivate(f.Layout, "test", DateTime.UnixEpoch);
        Assert.AreEqual(DesktopZoneRole.Other, workspace.Layout.Groups.Single(g => g.Name == "文档").DesktopRole);
        GroupInfo group = f.Group("文档");
        object journal = typeof(MainWindow).GetMethod("ToJournalGroupSnapshot", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [group, null])!;
        var fromJournal = (GroupInfo)typeof(MainWindow).GetMethod("FromJournalGroupSnapshot", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [journal])!;
        Assert.AreEqual(group.DesktopRole, fromJournal.DesktopRole);
        f.Invoke("RestoreGroupLayoutSnapshot", snapshot);
        Assert.IsTrue(f.Layout.Groups.All(g => g.DesktopRole == DesktopZoneRole.None));
    }

    [STATestMethod]
    public void EditModeProvidesEntryHandle_AndAllToggleDescribesMainGroups()
    {
        using var f = new Fixture();
        f.Layout.IsEditMode = true; f.Invoke("RebuildDesktopIcons"); f.Measure();
        Border entry = f.Field<Dictionary<string, Border>>("_lightDesktopEntries")[f.Group("文档").Id];
        Assert.AreEqual("LightDesktopEntryDragHandle", ((FrameworkElement)((Grid)entry.Child).Children[0]).Name);
        StringAssert.Contains((string)f.Invoke("GetVisibleGroupsToggleHeader")!, "主要分组");
        f.Invoke("ToggleAllGroupsCollapsed");
        f.Invoke("ToggleAllGroupsCollapsed");
        Assert.IsTrue(f.Layout.Groups.Where(g => g.DesktopRole == DesktopZoneRole.Other).All(g => g.IsCollapsed));
        StringAssert.Contains(f.Window.StatusText.Text, "其他分类可点击入口查看");
    }

    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; } = new(startQuietly: false);
        public AppLayoutData Layout => Field<AppLayoutData>("_appLayout");
        private readonly OffscreenSource _source;
        private readonly double _scale;
        public Fixture(double scale = 1, bool arrange = true)
        {
            _scale = scale;
            Window.Content = null;
            Window.RootGrid.Background = new LinearGradientBrush(Color.FromRgb(106, 167, 186), Color.FromRgb(35, 85, 104), 90);
            Window.ControlPanel.Visibility = Visibility.Collapsed;
            Window.RecycleBinWidget.Visibility = Visibility.Collapsed;
            Window.ControlPanelRestoreButton.Visibility = Visibility.Collapsed;
            _source = new OffscreenSource(Window.RootGrid);
            Layout.Groups.Clear(); Layout.FolderPortals.Clear(); Layout.FreeIcons.Clear(); Layout.RecycleBinWidget.IsVisible = false;
            typeof(MainWindow).GetField("_desktopGeometry", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Window,
                new DesktopGeometry([new DesktopMonitorRegion { DeviceName = "TEST", IsPrimary = true,
                    Bounds = new Rect(0, 0, 1600, 1200), WorkArea = new Rect(0, 0, 1600, 1200) }]));
            BitmapSource icon = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 220, 150, 40, 255 }, 4);
            icon.Freeze(); Field<Dictionary<string, BitmapSource?>>("_iconCache")["ext:.txt"] = icon;
            foreach ((string name, string? key, int count) in new (string, string?, int)[] {
                ("快捷方式", "shortcuts", 26), ("科研软件", null, 6), ("开发项目", "development-projects", 9),
                ("文档", "documents", 12), ("文件夹", "folders", 13), ("科学上网", null, 3),
                ("图片", "images", 2), ("压缩包", "archives", 2) })
            {
                var group = new GroupInfo { Name = name, AutoCategoryKey = key };
                Layout.Groups.Add(group);
                for (int i = 0; i < count; i++) AddItem(group, $"{name}双行名称{i}.txt");
            }
            if (arrange) { Assert.IsTrue((bool)Invoke("ArrangeGroupsSmartly")!); Invoke("RebuildDesktopIcons"); Measure(); }
        }
        public void AddItem(GroupInfo group, string name)
        { group.ItemNames.Add(name); Field<Dictionary<string, string>>("_desktopItems")[name] = @"C:\Test\" + name; }
        public GroupInfo Group(string name) => Layout.Groups.Single(g => g.Name == name);
        public FrameworkElement Visual(GroupInfo group) => Field<Dictionary<string, FrameworkElement>>("_groupVisuals")[group.Id];
        public T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Window)!;
        public void SetField(string name, object value) => typeof(MainWindow)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Window, value);
        public object? Invoke(string name, params object[] args)
        {
            MethodInfo method = typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
            object?[] supplied = args.Cast<object?>().Concat(method.GetParameters().Skip(args.Length)
                .Select(parameter => parameter.DefaultValue)).ToArray();
            return method.Invoke(Window, supplied);
        }
        public void Measure()
        {
            VisualTreeHelper.SetRootDpi(Window.RootGrid, new DpiScale(_scale, _scale));
            Window.IconCanvas.InvalidateMeasure(); Window.RootGrid.InvalidateMeasure();
            Window.RootGrid.Measure(new Size(1600, 1200));
            Window.RootGrid.Arrange(new Rect(0, 0, 1600, 1200));
            Window.RootGrid.UpdateLayout();
        }
        public void Render(string name)
        {
            if (Environment.GetEnvironmentVariable("DESKTOPORGANIZER_RENDER_LIGHT_QA") != "1") return;
            string output = Path.Combine(TestProjectFiles.Root, "artifacts/light-desktop");
            Directory.CreateDirectory(output);
            RenderTargetBitmap bitmap = RenderFrame();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, name)); encoder.Save(stream);
        }
        public RenderTargetBitmap RenderFrame()
        {
            var bitmap = new RenderTargetBitmap((int)(1600 * _scale), (int)(1200 * _scale), 96 * _scale, 96 * _scale, PixelFormats.Pbgra32);
            bitmap.Render(Window.RootGrid);
            return bitmap;
        }
        public void Dispose() { Field<DispatcherTimer>("_layoutSaveTimer").Stop(); Invoke("StopGroupPeek"); _source.Dispose(); }
    }
    private sealed class OffscreenSource : PresentationSource, IDisposable
    {
        private readonly Visual _root;
        private bool _disposed;
        public OffscreenSource(Visual root) { _root = root; AddSource(); RootChanged(null, root); }
        public override Visual RootVisual { get => _root; set => throw new NotSupportedException(); }
        public override bool IsDisposed => _disposed;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
        public void Dispose() { RootChanged(_root, null); RemoveSource(); _disposed = true; }
    }
}
