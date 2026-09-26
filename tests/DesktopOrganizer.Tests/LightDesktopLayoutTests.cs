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
    [DataRow("图片", 120.0, 0.0, true)]
    [DataRow("图片", 0.0, 300.0, true)]
    [DataRow("图片", -180.0, 0.0, false)]
    [DataRow("文档", 0.0, 130.0, true)]
    [DataRow("文档", 400.0, 0.0, true)]
    public void DraggingOneOtherEntry_KeepsSection_AndRestoresStandaloneGroupOnlyOutside(
        string movedName, double changeX, double changeY, bool detached)
    {
        using var f = new Fixture();
        GroupInfo moved = f.Group(movedName);
        GroupInfo[] remaining = f.Layout.Groups
            .Where(group => group.DesktopRole == DesktopZoneRole.Other && group != moved)
            .ToArray();
        string mainCards = JsonSerializer.Serialize(f.Layout.Groups.Where(group => group.DesktopRole != DesktopZoneRole.Other));
        Point start = new(moved.X, moved.Y);
        Border anchor = f.Field<Dictionary<string, Border>>("_lightDesktopEntries")[moved.Id];
        f.Invoke("PromoteOtherCategoryEntryForDrag", anchor);
        f.SetField("_draggedElement", anchor);
        f.SetField("_draggedGroup", moved);
        f.SetField("_draggedIsGroup", true);
        f.SetField("_groupDragMoved", true);
        f.SetField("_groupDragStartPosition", start);
        Canvas.SetLeft(anchor, moved.X + changeX);
        Canvas.SetTop(anchor, moved.Y + changeY);

        f.Invoke("CompleteGroupDrag", true);
        f.Measure();
        Assert.AreEqual(detached ? DesktopZoneRole.None : DesktopZoneRole.Other, moved.DesktopRole);
        Assert.AreEqual(start.X + (detached ? changeX : 0), moved.X);
        Assert.AreEqual(start.Y + (detached ? changeY : 0), moved.Y);
        Assert.AreEqual(mainCards, JsonSerializer.Serialize(f.Layout.Groups.Where(group =>
            group != moved && group.DesktopRole != DesktopZoneRole.Other)));
        AssertSurfaces();

        f.Invoke("RebuildDesktopIcons");
        f.Measure();
        AssertSurfaces();
        f.Render($"light-desktop-entry-detached-{movedName}-{changeX}-{changeY}.png");

        void AssertSurfaces()
        {
            Border[] decorations = f.Field<List<FrameworkElement>>("_lightDesktopDecorations")
                .OfType<Border>().ToArray();
            Border? section = decorations.SingleOrDefault(border =>
                FindChildren<TextBlock>(border).Any(text => text.Text == "其他分类"));
            Assert.IsNotNull(section, "其余入口仍应保留带标题的浅色底板。");
            Assert.IsInstanceOfType<SolidColorBrush>(section.Background);
            foreach (GroupInfo group in remaining)
            {
                Border entry = f.Field<Dictionary<string, Border>>("_lightDesktopEntries")[group.Id];
                Point position = entry.TranslatePoint(new Point(), f.Window.IconCanvas);
                Assert.IsTrue(position.X >= Canvas.GetLeft(section) - 0.01 &&
                    position.X + entry.ActualWidth <=
                    Canvas.GetLeft(section) + section.ActualWidth + 0.01 &&
                    position.Y >= Canvas.GetTop(section) - 0.01 &&
                    position.Y + entry.ActualHeight <= Canvas.GetTop(section) + section.ActualHeight + 0.01,
                    $"“{group.Name}”应仍在其他分类底板内。");
            }

            if (detached)
            {
                Assert.IsTrue(f.Visual(moved).IsVisible, "拖出后应恢复带底板的独立分组。");
                Assert.IsInstanceOfType<SolidColorBrush>(((Border)f.Visual(moved)).Background);
                Assert.IsFalse(moved.IsCollapsed);
                Assert.AreEqual(DesktopZoneRole.None, LightDesktopLayoutPolicy.ResolveRole(moved));
            }
        }
    }

    [STATestMethod]
    public void FractionalEntryPositions_DoNotLeaveEveryEntryWithoutASurface()
    {
        using var f = new Fixture();
        f.Group("文档").X += 0.4;
        f.Group("图片").X += 1.3;

        f.Invoke("RefreshLightDesktopEntries");
        f.Measure();

        Border[] surfaces = f.Field<List<FrameworkElement>>("_lightDesktopDecorations")
            .OfType<Border>().ToArray();
        Assert.IsNotEmpty(surfaces, "小数坐标不能让整个其他分类区域失去背景。");
        foreach (GroupInfo group in f.Layout.Groups.Where(group =>
                     group.DesktopRole == DesktopZoneRole.Other))
        {
            Border entry = f.Field<Dictionary<string, Border>>("_lightDesktopEntries")[group.Id];
            Point center = new(group.X + LightDesktopLayoutPolicy.EntryWidth / 2,
                group.Y + entry.ActualHeight / 2);
            Assert.IsTrue(surfaces.Any(surface =>
                new Rect(Canvas.GetLeft(surface), Canvas.GetTop(surface),
                    surface.ActualWidth, surface.ActualHeight).Contains(center)),
                $"“{group.Name}”入口应处于浅色背景上。");
        }
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

    [STATestMethod]
    public void CustomGroup_CanJoinAndLeaveOtherCategories_WithoutChangingItsItemsOrMainCards()
    {
        using var f = new Fixture();
        f.Layout.IsEditMode = true;
        f.Invoke("AddManualGroup", "我的资料");
        GroupInfo custom = f.Group("我的资料");
        f.AddItem(custom, "手动保存.txt");
        string mainCards = JsonSerializer.Serialize(f.Layout.Groups.Where(g =>
            g.DesktopRole is DesktopZoneRole.Shortcuts or DesktopZoneRole.Research or DesktopZoneRole.Projects));
        f.Invoke("RebuildDesktopIcons"); f.Measure();
        ContextMenu menu = FindChildren<Border>(f.Visual(custom))
            .Select(border => border.ContextMenu).First(candidate => candidate != null)!;
        menu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "移入其他分类")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        f.Measure();
        Assert.AreEqual(DesktopZoneRole.Other, custom.DesktopRole);
        Assert.IsTrue(custom.IsCollapsed);
        Assert.HasCount(1, custom.ItemNames);
        Assert.IsTrue(f.Field<Dictionary<string, Border>>("_lightDesktopEntries").ContainsKey(custom.Id));
        var restored = LayoutJsonSerializer.Deserialize(JsonSerializer.Serialize(f.Layout)).Layout;
        Assert.AreEqual(DesktopZoneRole.Other, restored.Groups.Single(g => g.Id == custom.Id).DesktopRole);
        var workspace = WorkspaceLayoutManager.CreateAndActivate(f.Layout, "分组归入", DateTime.UnixEpoch);
        Assert.AreEqual(DesktopZoneRole.Other, workspace.Layout.Groups.Single(g => g.Id == custom.Id).DesktopRole);

        f.Field<Dictionary<string, Border>>("_lightDesktopEntries")[custom.Id].ContextMenu!
            .Items.OfType<MenuItem>().Single(item => (string)item.Header == "移出其他分类")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        f.Measure();
        Assert.AreEqual(DesktopZoneRole.None, custom.DesktopRole);
        Assert.IsFalse(custom.IsCollapsed);
        Assert.IsTrue(f.Visual(custom).IsVisible);
        Assert.HasCount(1, custom.ItemNames);
        Assert.AreEqual(mainCards, JsonSerializer.Serialize(f.Layout.Groups.Where(g =>
            g.DesktopRole is DesktopZoneRole.Shortcuts or DesktopZoneRole.Research or DesktopZoneRole.Projects)));
    }

    [STATestMethod]
    [DataRow(1.0, 5)]
    [DataRow(1.25, 5)]
    [DataRow(1.5, 5)]
    [DataRow(2.0, 5)]
    [DataRow(1.0, 6)]
    [DataRow(1.25, 6)]
    [DataRow(1.5, 6)]
    [DataRow(2.0, 6)]
    [DataRow(1.0, 7)]
    [DataRow(1.25, 7)]
    [DataRow(1.5, 7)]
    [DataRow(2.0, 7)]
    public void OtherCategoryViewport_OnlyScrollsWhenRowsOverflow(double scale, int count)
    {
        using var f = new Fixture(scale);
        for (int index = 5; index < count; index++) f.Invoke("AddManualGroupCore", $"新增{index}", true);
        f.Measure();
        ScrollViewer scroll = f.Field<ScrollViewer>("_lightDesktopSectionScroll");
        bool overflow = count > 6;
        Assert.AreEqual(overflow ? Visibility.Visible : Visibility.Collapsed, scroll.ComputedVerticalScrollBarVisibility);
        Assert.AreEqual(overflow, scroll.ScrollableHeight > 0, $"内容={scroll.ExtentHeight}，视口={scroll.ViewportHeight}");
        scroll.ScrollToEnd(); f.Measure();
        GroupInfo lastGroup = ((List<GroupInfo>)f.Invoke("GetOtherCategoryEntries")!)[^1];
        Border last = f.Field<Dictionary<string, Border>>("_lightDesktopEntries")[lastGroup.Id];
        Point lastBottom = last.TranslatePoint(new Point(0, last.ActualHeight), scroll);
        Assert.IsTrue(lastBottom.Y <= scroll.ViewportHeight + 1 / scale, "最后一行应完整可见。");
        Border section = f.Field<Border>("_lightDesktopSection");
        Assert.IsTrue(section.ActualHeight - last.TranslatePoint(new Point(0, last.ActualHeight), section).Y >= 4,
            "最后一行与底部圆角之间应留有空隙。");
    }

    [STATestMethod]
    [DataRow(1120.0)]
    [DataRow(1162.0)]
    public void OtherCategoryViewport_NearScreenBottom_RemainsScrollable(double top)
    {
        using var f = new Fixture(1.5);
        f.Invoke("ReflowOtherCategoryAnchors", new Point(400, top));
        f.Invoke("RefreshLightDesktopEntries"); f.Measure();
        ScrollViewer scroll = f.Field<ScrollViewer>("_lightDesktopSectionScroll");
        Border section = f.Field<Border>("_lightDesktopSection");
        Assert.IsTrue(scroll.ScrollableHeight > 0);
        Assert.IsTrue(Canvas.GetTop(section) + section.ActualHeight <= 1200);
        scroll.ScrollToEnd(); f.Measure();
        Border last = f.Field<Dictionary<string, Border>>("_lightDesktopEntries")[f.Group("压缩包").Id];
        Assert.IsTrue(last.TranslatePoint(new Point(0, last.ActualHeight), scroll).Y <= scroll.ViewportHeight + 1);
    }

    [STATestMethod]
    public void OtherCategoryEntry_ReflectsOpenDrawer_AcrossSwitchRefreshAndClose()
    {
        using var f = new Fixture();
        GroupInfo documents = f.Group("文档"), pictures = f.Group("图片");
        System.Windows.Controls.Primitives.ToggleButton Entry(GroupInfo group) =>
            FindChildren<System.Windows.Controls.Primitives.ToggleButton>(
                f.Field<Dictionary<string, Border>>("_lightDesktopEntries")[group.Id]).Single();
        void Toggle(GroupInfo group) =>
            ((System.Windows.Automation.Provider.IToggleProvider)
                new System.Windows.Automation.Peers.ToggleButtonAutomationPeer(Entry(group))
                    .GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle)).Toggle();
        void AssertState(GroupInfo group, bool open)
        {
            var button = Entry(group);
            Assert.AreEqual(open, button.IsChecked);
            Assert.AreEqual(open, f.Visual(group).IsVisible, "入口状态必须与实际抽屉同步。");
            Assert.AreEqual($"{group.Name}，{group.ItemNames.Count} 项，{(open ? "已展开" : "已收起")}",
                System.Windows.Automation.AutomationProperties.GetName(button));
            Assert.IsTrue(FindChildren<TextBlock>(button).Any(text => text.Text == (open ? "‹" : "›")));
        }
        AssertState(documents, false);
        Toggle(documents);
        f.Measure(); AssertState(documents, true);
        Toggle(pictures);
        f.Measure(); AssertState(documents, false); AssertState(pictures, true);
        f.Invoke("RebuildDesktopIcons"); f.Measure(); AssertState(pictures, true);
        f.Invoke("StopGroupPeek"); f.Measure(); AssertState(pictures, false);
        Toggle(pictures); f.Measure(); AssertState(pictures, true);
        Toggle(pictures); f.Measure(); AssertState(pictures, false);
        f.SetField("_organizerPaused", true);
        Toggle(pictures); f.Measure(); AssertState(pictures, false);
    }

    [STATestMethod]
    public void ManyOtherCategories_ScrollWithinThreeRows_AndPreserveOffsetAcrossRefresh()
    {
        using var f = new Fixture();
        GroupInfo first = f.Group("文档");
        for (int index = 0; index < 20; index++)
            f.Layout.Groups.Add(new GroupInfo { Name = $"自定义{index}", DesktopRole = DesktopZoneRole.Other,
                X = first.X, Y = first.Y + 84, IsCollapsed = true });
        f.Invoke("RebuildDesktopIcons"); f.Measure();
        Border section = f.Field<List<FrameworkElement>>("_lightDesktopDecorations").OfType<Border>().Single();
        ScrollViewer? scroll = FindChildren<ScrollViewer>(section).SingleOrDefault();
        Assert.IsNotNull(scroll, "其他分类内容过多时必须提供滚动视口。");
        Assert.IsTrue(section.ActualHeight <= 160);
        Assert.IsTrue(scroll.ScrollableHeight > 0);
        Assert.IsTrue((bool)f.Invoke("IsInteractiveClientPoint", section.TranslatePoint(new Point(10, 10), f.Window.IconCanvas))!);
        scroll.ScrollToEnd(); f.Measure();
        Assert.IsTrue(scroll.VerticalOffset > 0);
        Border last = f.Field<Dictionary<string, Border>>("_lightDesktopEntries")[f.Group("自定义19").Id];
        Point center = last.TranslatePoint(new Point(last.ActualWidth / 2, last.ActualHeight / 2), f.Window.IconCanvas);
        Assert.IsTrue((bool)f.Invoke("IsPointInsideClippedAncestors", last, center)!);
        Border hidden = f.Field<Dictionary<string, Border>>("_lightDesktopEntries")[first.Id];
        Point hiddenCenter = hidden.TranslatePoint(new Point(40, 16), f.Window.IconCanvas);
        f.Invoke("UpdateGroupDropPreview", hiddenCenter, new Border());
        Assert.AreNotSame(first, f.Field<GroupInfo?>("_activeGroupDropTarget"));
        f.Invoke("UpdateGroupDropPreview", center, new Border());
        Assert.AreSame(f.Group("自定义19"), f.Field<GroupInfo?>("_activeGroupDropTarget"));
        double offset = scroll.VerticalOffset;
        f.Invoke("RebuildDesktopIcons"); f.Measure();
        section = f.Field<List<FrameworkElement>>("_lightDesktopDecorations").OfType<Border>().Single();
        scroll = FindChildren<ScrollViewer>(section).Single();
        Assert.AreEqual(offset, scroll.VerticalOffset, 0.01);
        f.Render("other-categories-scrolled.png");
        f.Window.LocateDesktopSearchResult(first.ItemNames[0]); f.Measure();
        scroll = f.Field<ScrollViewer>("_lightDesktopSectionScroll");
        Assert.AreEqual(0, scroll.VerticalOffset, 0.01, "搜索定位应将隐藏的分类入口滚入视口。");
        f.Layout.Groups.RemoveAll(g => g.Name.StartsWith("自定义", StringComparison.Ordinal));
        f.Invoke("RebuildDesktopIcons"); f.Measure();
        section = f.Field<List<FrameworkElement>>("_lightDesktopDecorations").OfType<Border>().Single();
        scroll = FindChildren<ScrollViewer>(section).Single();
        Assert.AreEqual(0, scroll.VerticalOffset);
    }

    private static IEnumerable<T> FindChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T descendant in FindChildren<T>(child)) yield return descendant;
        }
    }

    [STATestMethod]
    public void CreateGroupOption_AddsDirectlyToOtherCategories_WithoutAffectingOtherDialogs()
    {
        using var f = new Fixture();
        var dialog = new SimpleInputDialog("名称", "新分组");
        Assert.AreEqual(Visibility.Collapsed, dialog.OptionCheckBox.Visibility);
        dialog.ConfigureOption("加入其他分类", true);
        Assert.IsTrue(dialog.IsOptionChecked);
        Assert.AreEqual(Visibility.Visible, dialog.OptionCheckBox.Visibility);
        f.Invoke("AddManualGroupCore", "新建并归入", dialog.IsOptionChecked); f.Measure();
        GroupInfo created = f.Group("新建并归入");
        Assert.AreEqual(DesktopZoneRole.Other, created.DesktopRole);
        Assert.IsTrue(f.Field<Dictionary<string, Border>>("_lightDesktopEntries").ContainsKey(created.Id));
        Assert.IsNull(f.Field<object?>("_lastSmartLayoutSnapshot"));
    }

    [STATestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ScrolledEntry_CancelAndRecovery_RestoreContainerAndOffset(bool recover, bool emptyDesktop)
    {
        using var f = new Fixture();
        if (emptyDesktop)
        {
            f.Field<Dictionary<string, string>>("_desktopItems").Clear();
            foreach (GroupInfo group in f.Layout.Groups) group.ItemNames.Clear();
        }
        for (int index = 0; index < 8; index++) f.Invoke("AddManualGroupCore", $"滚动组{index}", true);
        f.Measure();
        ScrollViewer scroll = f.Field<ScrollViewer>("_lightDesktopSectionScroll");
        scroll.ScrollToEnd(); f.Measure();
        double offset = scroll.VerticalOffset;
        GroupInfo moved = f.Group("滚动组7");
        string before = JsonSerializer.Serialize(f.Layout.Groups);
        Border entry = f.Field<Dictionary<string, Border>>("_lightDesktopEntries")[moved.Id];
        f.Invoke("PromoteOtherCategoryEntryForDrag", entry);
        f.SetField("_draggedElement", entry); f.SetField("_draggedGroup", moved);
        f.SetField("_draggedIsGroup", true); f.SetField("_groupDragMoved", true);
        f.SetField("_groupDragStartPosition", new Point(moved.X, moved.Y));
        f.SetField("_groupDragLastCanvasPoint", new Point(1000, 700));
        Canvas.SetLeft(entry, 1000); Canvas.SetTop(entry, 700);
        if (recover) f.Invoke("ResetAllInteractionState", true);
        else f.Invoke("CompleteGroupDrag", false);
        f.Measure();
        Assert.AreEqual(before, JsonSerializer.Serialize(f.Layout.Groups));
        entry = f.Field<Dictionary<string, Border>>("_lightDesktopEntries")[moved.Id];
        Assert.AreNotSame(f.Window.IconCanvas, VisualTreeHelper.GetParent(entry));
        Assert.AreEqual(offset, f.Field<ScrollViewer>("_lightDesktopSectionScroll").VerticalOffset, 0.01);
        Assert.IsNull(f.Field<Point?>("_groupDragLastCanvasPoint"));
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OrdinaryGroup_DragIntoOtherCategories_ChangesMembershipOnlyOnCommit(bool commit)
    {
        using var f = new Fixture();
        f.Invoke("AddManualGroup", "待归入"); f.Measure();
        GroupInfo moved = f.Group("待归入");
        Point start = new(moved.X, moved.Y);
        FrameworkElement visual = f.Visual(moved);
        Border section = f.Field<Border>("_lightDesktopSection");
        Point target = section.TranslatePoint(new Point(40, 40), f.Window.IconCanvas);
        f.SetField("_draggedElement", visual); f.SetField("_draggedGroup", moved);
        f.SetField("_draggedIsGroup", true); f.SetField("_groupDragMoved", true);
        f.SetField("_groupDragStartPosition", start); f.SetField("_groupDragLastCanvasPoint", target);
        Canvas.SetLeft(visual, target.X); Canvas.SetTop(visual, target.Y);
        f.Invoke("CompleteGroupDrag", commit); f.Measure();
        Assert.AreEqual(commit ? DesktopZoneRole.Other : DesktopZoneRole.None, moved.DesktopRole);
        if (!commit) Assert.AreEqual(start, new Point(moved.X, moved.Y));
        else Assert.IsTrue(f.Field<Dictionary<string, Border>>("_lightDesktopEntries").ContainsKey(moved.Id));
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DeletingFirstRow_KeepsOtherCategoriesAtTheSamePosition(bool automaticRemoval)
    {
        using var f = new Fixture();
        Border section = f.Field<Border>("_lightDesktopSection");
        Point origin = new(Canvas.GetLeft(section), Canvas.GetTop(section));
        foreach (string name in new[] { "文档", "文件夹" })
        {
            GroupInfo group = f.Group(name);
            group.ItemNames.Clear();
            if (automaticRemoval)
            {
                group.IsAutoCategory = true;
                f.Invoke("RemoveMissingAndDuplicateGroupItems", f.Field<Dictionary<string, string>>("_desktopItems"));
                f.Invoke("RebuildDesktopIcons");
            }
            else f.Invoke("DeleteGroup", group);
            f.Measure();
        }
        section = f.Field<Border>("_lightDesktopSection");
        Assert.AreEqual(origin, new Point(Canvas.GetLeft(section), Canvas.GetTop(section)));
    }

    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; } = new(startQuietly: false);
        public AppLayoutData Layout => Field<AppLayoutData>("_appLayout");
        private readonly OffscreenSource _source;
        private readonly double _scale;
        public Fixture(double scale = 1, bool arrange = true)
        {
            SetField("_desktopSnapshotInitialized", true);
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
