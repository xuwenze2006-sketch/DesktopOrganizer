using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class RecycleBinDropTests
{
    [STATestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    [DataRow(2.0)]
    public void Preview_UsesWidgetCoordinates_ClearsOtherTargets_AndRestoresAppearance(double scale)
    {
        using var f = new Fixture(scale);
        Brush background = f.Window.RecycleBinWidget.Background;
        f.Set("_activeGroupDropTarget", new GroupInfo());
        f.Set("_activePhysicalFolderDropPath", @"C:\Test\folder");

        Assert.IsTrue(f.Preview(f.Target));
        Assert.IsTrue(f.Field<bool>("_isRecycleBinDropTarget"));
        Assert.IsNull(f.Field<object?>("_activeGroupDropTarget"));
        Assert.IsNull(f.Field<object?>("_activePhysicalFolderDropPath"));
        Assert.AreNotSame(background, f.Window.RecycleBinWidget.Background);
        StringAssert.Contains(f.Window.StatusText.Text, "需确认");

        Assert.IsFalse(f.Preview(new Point(700, 20)));
        Assert.IsFalse(f.Field<bool>("_isRecycleBinDropTarget"));
        Assert.AreSame(background, f.Window.RecycleBinWidget.Background);
    }

    [STATestMethod]
    public void FastMouseUp_RechecksFinalPoint_RestoresOriginBeforeRequestingOnlyDraggedItem()
    {
        using var f = new Fixture();
        f.Field<HashSet<string>>("_selectedItemNames").UnionWith([Fixture.Name, "other.txt"]);
        int calls = 0;
        Assert.IsTrue(f.Drop(f.Target, (path, name) =>
        {
            calls++;
            Assert.AreEqual(Fixture.Path, path);
            Assert.AreEqual(Fixture.Name, name);
            Assert.IsNull(f.Field<object?>("_draggedElement"));
            Assert.IsFalse(f.Field<bool>("_isRecycleBinDropTarget"));
            Assert.AreEqual(40, Canvas.GetLeft(f.Dragged));
            Assert.AreEqual(50, Canvas.GetTop(f.Dragged));
        }));
        Assert.AreEqual(1, calls);
        Assert.IsTrue(f.Field<HashSet<string>>("_selectedItemNames").Contains("other.txt"));
        Assert.HasCount(1, f.Layout.FreeIcons); // 模拟用户取消确认，不执行任何文件操作。
        Assert.IsFalse(f.Field<DispatcherTimer>("_layoutSaveTimer").IsEnabled);
    }

    [STATestMethod]
    public void MouseUpOutside_DoesNotReuseStaleRecycleTarget()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Preview(f.Target));
        Assert.IsFalse(f.Drop(new Point(700, 20), (_, _) => Assert.Fail("不应请求回收")));
        Assert.IsFalse(f.Field<bool>("_isRecycleBinDropTarget"));
    }

    [STATestMethod]
    [DataRow("shell-kind")]
    [DataRow("shell-path")]
    [DataRow("pending")]
    public void UnrecyclableDrop_IsHandledWithoutFallingThroughOrRequestingDeletion(string reason)
    {
        using var f = new Fixture();
        var tag = (IconTag)f.Dragged.Tag;
        if (reason == "shell-kind") tag.Kind = DesktopItemKind.ShellNamespace;
        if (reason == "shell-path") tag.FullPath = ShellItemLocation.Encode(
            ShellDesktopItemPolicy.RecycleBinParsingName, true);
        if (reason == "pending") f.Field<HashSet<string>>("_pendingFileOperationPaths").Add(Fixture.Path);

        Assert.IsTrue(f.Drop(f.Target, (_, _) => Assert.Fail("不可回收项不能进入确认/执行入口")));

        Assert.AreEqual(40, Canvas.GetLeft(f.Dragged));
        Assert.AreEqual(50, Canvas.GetTop(f.Dragged));
        Assert.IsNull(f.Field<object?>("_draggedElement"));
        StringAssert.Contains(f.Window.StatusText.Text, "不能");
    }

    [STATestMethod]
    public void CoveredOrHiddenWidget_CannotReceiveDrop()
    {
        using var f = new Fixture();
        var cover = new Border
        {
            Width = 252, Height = 122, Margin = f.Window.RecycleBinWidget.Margin,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.White
        };
        Panel.SetZIndex(cover, 10000);
        f.Window.RootGrid.Children.Add(cover);
        f.Measure();
        Assert.IsFalse(f.Preview(f.Target), "上层面板应挡住回收站。");
        f.Window.RootGrid.Children.Remove(cover);
        f.Measure();
        Assert.IsTrue(f.Preview(f.Target));
        f.Window.RecycleBinWidget.Visibility = Visibility.Hidden;
        Assert.IsFalse(f.Preview(f.Target));
        f.Window.RecycleBinWidget.Visibility = Visibility.Visible;
        f.Set("_organizerPaused", true);
        Assert.IsFalse(f.Preview(f.Target));
    }

    [STATestMethod]
    public void CancelInteraction_ClearsRecycleHighlightWithoutRequestingOperation()
    {
        using var f = new Fixture();
        Brush background = f.Window.RecycleBinWidget.Background;
        Assert.IsTrue(f.Preview(f.Target));
        f.Invoke("ResetAllInteractionState", true);
        Assert.AreSame(background, f.Window.RecycleBinWidget.Background);
        Assert.IsFalse(f.Field<bool>("_isRecycleBinDropTarget"));
        Assert.AreEqual(40, Canvas.GetLeft(f.Dragged));
    }

    [STATestMethod]
    public void GroupedIcon_IsBackInOriginalGroupBeforeConfirmation()
    {
        using var f = new Fixture();
        var group = new GroupInfo { ItemNames = [Fixture.Name], IsSizeLocked = true };
        f.Layout.Groups.Add(group);
        f.Layout.FreeIcons.Clear();
        ((IconTag)f.Dragged.Tag).Group = group;
        f.Set("_groupedIconDragSourceGroup", group);
        f.Set("_dragOriginalPosition", null);
        bool requested = false;

        Assert.IsTrue(f.Drop(f.Target, (_, _) =>
        {
            requested = true;
            Assert.IsNull(f.Field<object?>("_groupedIconDragSourceGroup"));
            CollectionAssert.AreEqual(new[] { Fixture.Name }, group.ItemNames);
            Assert.IsFalse(f.Layout.FreeIcons.ContainsKey(Fixture.Name));
            Assert.IsFalse(f.Window.IconCanvas.Children.Contains(f.Dragged));
        }));
        Assert.IsTrue(requested);
    }

    private sealed class Fixture : IDisposable
    {
        public const string Name = "source.txt";
        public const string Path = @"C:\Test\source.txt";
        public MainWindow Window { get; } = new(startQuietly: false);
        public AppLayoutData Layout => Field<AppLayoutData>("_appLayout");
        public Border Dragged { get; } = new()
        {
            Width = 80, Height = 84,
            Tag = new IconTag { DisplayName = Name, FullPath = Path }
        };
        private readonly OffscreenSource _source;
        public Point Target => Window.RecycleBinWidget.TranslatePoint(new Point(100, 60), Window.IconCanvas);

        public Fixture(double scale = 1)
        {
            Set("_desktopSnapshotInitialized", true);
            Window.RootGrid = new Grid();
            Window.IconCanvas = new Canvas { Margin = new Thickness(20, 15, 0, 0) };
            Window.RecycleBinWidget = new Border
            {
                Width = 252, Height = 122, Margin = new Thickness(30, 450, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Beige, BorderBrush = Brushes.Gray
            };
            Panel.SetZIndex(Window.RecycleBinWidget, 9000);
            Window.RootGrid.Children.Add(Window.IconCanvas);
            Window.RootGrid.Children.Add(Window.RecycleBinWidget);
            VisualTreeHelper.SetRootDpi(Window.RootGrid, new DpiScale(scale, scale));
            _source = new OffscreenSource(Window.RootGrid);
            Layout.Groups.Clear();
            Layout.FolderPortals.Clear();
            Layout.RecycleBinWidget.IsVisible = true;
            Layout.FreeIcons[Name] = new IconPosition { X = 40, Y = 50 };
            Field<Dictionary<string, string>>("_desktopItems")[Name] = Path;
            Field<Dictionary<string, BitmapSource?>>("_iconCache")["ext:.txt"] = null;
            Window.IconCanvas.Children.Add(Dragged);
            Canvas.SetLeft(Dragged, 150);
            Canvas.SetTop(Dragged, 480);
            Set("_draggedElement", Dragged);
            Set("_dragOriginalPosition", new IconPosition { X = 40, Y = 50 });
            Set("_dragAllowsLayoutMove", true);
            Measure();
        }

        public void Measure()
        {
            Window.RootGrid.Measure(new Size(900, 700));
            Window.RootGrid.Arrange(new Rect(0, 0, 900, 700));
        }
        public bool Preview(Point point) => (bool)Invoke("UpdateRecycleBinDropPreview", point, Dragged)!;
        public bool Drop(Point point, Action<string, string> request) =>
            (bool)Invoke("TryCompleteRecycleBinIconDrop", point, Dragged, request)!;
        public T Field<T>(string name) => (T)typeof(MainWindow)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Window)!;
        public void Set(string name, object? value) => typeof(MainWindow)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Window, value);
        public object? Invoke(string name, params object[] args) => typeof(MainWindow)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Window, args);
        public void Dispose()
        {
            Field<DispatcherTimer>("_layoutSaveTimer").Stop();
            _source.Dispose();
        }
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
