using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class UniformGroupLayoutTests
{
    [STATestMethod]
    [DataRow(true, 2, 1.0)]
    [DataRow(true, 6, 1.5)]
    [DataRow(true, 12, 1.5)]
    [DataRow(false, 6, 1.25)]
    public void ActualGroup_UsesCenteredContentWithoutStretchingIcons(bool compact, int count, double scale)
    {
        var window = CreateWindow();
        Field<AppLayoutData>(window, "_appLayout").CompactGroupLayout = compact;
        var group = Group("两行名称测试", count);
        group.X = 0;
        group.Y = 0;
        group.UseUniformTrackWidth = true;
        Invoke(window, "AutoFitGroup", group, false);
        BitmapSource icon = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 210, 140, 60, 255 }, 4);
        icon.Freeze();
        Field<Dictionary<string, BitmapSource?>>(window, "_iconCache")["ext:.txt"] = icon;
        var existing = group.ItemNames.ToDictionary(name => name, name => @"C:\Test\" + name);
        var visual = (FrameworkElement)Invoke(window, "CreateGroupVisual", group, existing)!;
        var canvas = new Canvas { Background = Brushes.LightGray, UseLayoutRounding = true };
        VisualTreeHelper.SetRootDpi(canvas, new DpiScale(scale, scale));
        canvas.Children.Add(visual);
        var size = new Size(group.Width + 4, group.Height + 4);
        canvas.Measure(size);
        canvas.Arrange(new Rect(size));
        VirtualizingGroupPanel panel = Field<Dictionary<string, VirtualizingGroupPanel>>(
            window, "_groupItemPanels")[group.Id];
        Assert.IsTrue(panel.Children.Count > 0);
        double contentCenter = panel.TranslatePoint(new Point(panel.ActualWidth / 2, 0), visual).X;
        Assert.AreEqual(visual.ActualWidth / 2, contentCenter, 10,
            "少列图标整体居中，允许滚动条占用空间。");
        foreach (Border tile in panel.Children)
        {
            Assert.IsTrue(tile.Width <= 80);
            var content = (StackPanel)tile.Child;
            var label = (TextBlock)((Border)content.Children[1]).Child;
            Assert.AreEqual(42, ((FrameworkElement)content.Children[0]).Height);
            Assert.AreEqual(11, label.FontSize);
            Assert.IsTrue(label.ActualHeight >= 32 - 1 / scale);
            Assert.IsTrue(label.TranslatePoint(new Point(0, label.ActualHeight), canvas).Y <= size.Height,
                "完整卡片渲染必须包含最后一行文字。");
        }
        if (Environment.GetEnvironmentVariable("DESKTOPORGANIZER_RENDER_LAYOUT_QA") == "1")
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            string output = Path.Combine(root, "artifacts", "uniform-group-layout");
            Directory.CreateDirectory(output);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale),
                (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, $"group-{compact}-{count}-{scale}.png"));
            encoder.Save(stream);
        }
    }

    [STATestMethod]
    [DataRow(true, 1, 1)]
    [DataRow(true, 2, 2)]
    [DataRow(true, 6, 3)]
    [DataRow(true, 9, 3)]
    [DataRow(true, 12, 4)]
    [DataRow(false, 2, 2)]
    [DataRow(false, 6, 3)]
    [DataRow(false, 12, 3)]
    public void UniformOuterWidth_PreservesContentColumnsAndIconSize(bool compact, int count, int columns)
    {
        var window = CreateWindow();
        Field<AppLayoutData>(window, "_appLayout").CompactGroupLayout = compact;
        var group = Group("test", count);
        group.UseUniformTrackWidth = true;

        Invoke(window, "AutoFitGroup", group, false);

        Assert.AreEqual(352, group.Width);
        Assert.AreEqual(columns, Invoke(window, "GetDesiredGroupColumnCount", group));
        double tileWidth = (double)Invoke(window, "GetGroupedIconTileWidth", group)!;
        double panelWidth = (double)Invoke(window, "GetGroupedIconPanelWidth", group)!;
        Assert.IsTrue(tileWidth <= 80, "宽卡片不能把内部图标格拉大。");
        Assert.AreEqual(columns * (tileWidth + (compact ? 3 : 4)), panelWidth);
        Assert.IsFalse((bool)Invoke(window, "AutoFitGroup", group, false)!, "刷新不能再缩回内容宽度。");
    }

    [STATestMethod]
    public void SmartArrange_AlignsExpandedTopRow_PutsCollapsedBelow_PreservesUndo()
    {
        var window = CreateWindow();
        AppLayoutData layout = Field<AppLayoutData>(window, "_appLayout");
        layout.CompactGroupLayout = true;
        layout.ReserveTemporaryWorkspace = true;
        layout.Groups = [Group("shortcuts", 26), Group("folders", 13, true),
            Group("documents", 12), Group("projects", 9), Group("software", 6),
            Group("network", 3), Group("pictures", 2), Group("archives", 2, true)];
        object snapshot = Invoke(window, "CaptureGroupLayoutSnapshot")!;

        Assert.IsTrue((bool)Invoke(window, "ArrangeGroupsSmartly")!);

        Assert.IsTrue(layout.Groups.All(group => group.UseUniformTrackWidth && group.Width == 352));
        Assert.HasCount(3, layout.Groups.Select(group => group.X).Distinct().ToList());
        double top = layout.Groups.Min(group => group.Y);
        foreach (GroupInfo group in layout.Groups.Where(group => !group.IsCollapsed && group.ItemNames.Count >= 9))
        {
            Assert.AreEqual(top, group.Y);
        }
        foreach (GroupInfo collapsed in layout.Groups.Where(group => group.IsCollapsed))
        {
            foreach (GroupInfo expanded in layout.Groups.Where(group => !group.IsCollapsed && group.X == collapsed.X))
            {
                Assert.IsTrue(collapsed.Y >= expanded.Y + expanded.Height);
            }
        }
        foreach (GroupInfo group in layout.Groups)
        {
            Rect bounds = (Rect)Invoke(window, "GetGroupBounds", group)!;
            Assert.IsTrue(bounds.Bottom <= 800, "保留主屏底部约三分之一留白。");
            foreach (GroupInfo other in layout.Groups.Where(other => other != group))
            {
                Assert.IsFalse(bounds.IntersectsWith((Rect)Invoke(window, "GetGroupBounds", other)!));
            }
        }

        Invoke(window, "RestoreGroupLayoutSnapshot", snapshot);
        Assert.IsTrue(layout.Groups.All(group => !group.UseUniformTrackWidth && group.Width == 280));
    }

    [STATestMethod]
    public void SmartArrange_LeavesManuallyLockedDimensionsUntouched()
    {
        var window = CreateWindow();
        var group = Group("manual", 3);
        group.IsSizeLocked = true;
        group.Width = 240;
        group.Height = 190;
        Field<AppLayoutData>(window, "_appLayout").Groups.Add(group);

        Assert.IsTrue((bool)Invoke(window, "ArrangeGroupsSmartly")!);

        Assert.AreEqual(240, group.Width);
        Assert.AreEqual(190, group.Height);
        Assert.IsFalse(group.UseUniformTrackWidth);
    }

    [TestMethod]
    public void LayoutAndWorkspaceRoundTrip_PreserveUniformWidthPreference()
    {
        var group = Group("saved", 2);
        group.UseUniformTrackWidth = true;
        var layout = new AppLayoutData { Groups = [group] };
        var loaded = LayoutJsonSerializer.Deserialize(JsonSerializer.Serialize(layout)).Layout;
        Assert.IsTrue(loaded.Groups[0].UseUniformTrackWidth);
        var profile = WorkspaceLayoutManager.CreateAndActivate(layout, "layout", DateTime.UnixEpoch);
        Assert.IsTrue(profile.Layout.Groups[0].UseUniformTrackWidth);
        Assert.IsFalse(JsonSerializer.Deserialize<GroupInfo>("{}")!.UseUniformTrackWidth,
            "旧布局不能在加载时被自动重排。");
    }

    [TestMethod]
    public void FileMoveUndoSnapshot_PreservesUniformWidthPreference()
    {
        var group = Group("saved", 2);
        group.UseUniformTrackWidth = true;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        object snapshot = typeof(MainWindow).GetMethod("ToJournalGroupSnapshot", flags)!
            .Invoke(null, [group, null])!;
        var restored = (GroupInfo)typeof(MainWindow).GetMethod("FromJournalGroupSnapshot", flags)!
            .Invoke(null, [snapshot])!;
        Assert.IsTrue(restored.UseUniformTrackWidth);
    }

    private static MainWindow CreateWindow()
    {
        var window = new MainWindow(startQuietly: false);
        var layout = Field<AppLayoutData>(window, "_appLayout");
        layout.Groups.Clear();
        layout.FolderPortals.Clear();
        layout.RecycleBinWidget.IsVisible = false;
        window.RecycleBinWidget.Visibility = Visibility.Collapsed;
        typeof(MainWindow).GetField("_desktopGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, new DesktopGeometry([new DesktopMonitorRegion
            {
                DeviceName = "TEST", IsPrimary = true,
                Bounds = new Rect(0, 0, 1600, 1200), WorkArea = new Rect(0, 0, 1600, 1200)
            }]));
        return window;
    }

    private static GroupInfo Group(string name, int count, bool collapsed = false) => new()
    {
        Name = name, IsCollapsed = collapsed,
        ItemNames = Enumerable.Range(0, count).Select(i => $"{name}-{i}.txt").ToList()
    };

    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static object? Invoke(MainWindow window, string name, params object[] arguments) => typeof(MainWindow)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
}
