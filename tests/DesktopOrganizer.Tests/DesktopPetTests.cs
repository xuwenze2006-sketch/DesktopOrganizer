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
public sealed class DesktopPetTests
{
    [STATestMethod]
    public void Preferences_UpgradeNullAndInvalidValues_AndRoundTripHiddenPosition()
    {
        using var f = new Fixture();
        Assert.IsFalse(JsonSerializer.Deserialize<AppLayoutData>("{\"Version\":19}")!.DesktopPet.IsVisible);
        f.Layout.DesktopPet = null!;
        f.Invoke("NormalizeLayout");
        Assert.IsNotNull(f.Layout.DesktopPet);
        Assert.IsFalse(f.Layout.DesktopPet.IsVisible);
        f.Layout.DesktopPet.X = double.NaN;
        f.Layout.DesktopPet.Y = double.PositiveInfinity;
        f.Invoke("NormalizeLayout");
        Assert.IsNull(f.Layout.DesktopPet.X);
        Assert.IsNull(f.Layout.DesktopPet.Y);
        f.Invoke("SetDesktopPetPosition", 280.5, 321.25, true);
        AppLayoutData restored = LayoutJsonSerializer.Deserialize(JsonSerializer.Serialize(f.Layout)).Layout;
        Assert.IsFalse(restored.DesktopPet.IsVisible);
        Assert.AreEqual(280.5, restored.DesktopPet.X);
        Assert.AreEqual(321.25, restored.DesktopPet.Y);
    }

    [STATestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    [DataRow(2.0)]
    public void RenderingAndHitTesting_FollowOpaquePixelsAndDraggedPosition(double scale)
    {
        using var f = new Fixture();
        f.Enable();
        f.Invoke("SetDesktopPetPosition", 300.0, 200.0, true);
        f.Measure(scale);
        Assert.AreEqual(scale, VisualTreeHelper.GetDpi(f.Pet).DpiScaleX);
        Assert.IsTrue(f.Hit(356, 275), "小猫身体可点击。");
        Assert.IsFalse(f.Hit(301, 201), "素材周围透明留白必须穿透。");
        Assert.IsNull(f.Pet.InputHitTest(new Point(1, 1)), "WPF 命中同样应穿透透明像素。");
        Assert.AreSame(f.Pet, f.Pet.InputHitTest(new Point(56, 75)));
        Assert.AreSame(f.Pet, f.Window.RootGrid.InputHitTest(new Point(356, 275)),
            "空的图标画布不能挡住猫的输入。");
        var icon = new Border { Width = 112, Height = 112, Background = Brushes.White };
        Canvas.SetLeft(icon, 300);
        Canvas.SetTop(icon, 200);
        f.Window.IconCanvas.Children.Add(icon);
        f.Measure(scale);
        Assert.AreSame(icon, f.Window.RootGrid.InputHitTest(new Point(356, 275)),
            "重叠时文件图标必须优先收到输入。");
        f.Window.IconCanvas.Children.Remove(icon);

        f.Invoke("SetDesktopPetPosition", 500.0, 250.0, true);
        f.Measure(scale);
        Assert.IsFalse(f.Hit(356, 275), "拖动后旧位置不得保留命中区域。");
        Assert.IsTrue(f.Hit(556, 325));
        f.Render($"cat-idle-{scale}.png", scale);
        for (int i = 0; i < 5; i++) f.Pet.AdvanceAnimation();
        Assert.AreEqual(2, f.Pet.CurrentFrame, "待机之后应进入打盹。");
        f.Render($"cat-sleep-{scale}.png", scale);
        f.Pet.ReactToTouch();
        Assert.AreEqual(4, f.Pet.CurrentFrame);
        f.Render($"cat-touch-{scale}.png", scale);
    }

    [STATestMethod]
    public void SystemTab_ToggleFitsAndCanReopenHiddenPet()
    {
        using var f = new Fixture();
        f.Window.ControlPanel.Visibility = Visibility.Visible;
        f.Window.ExpandedCommands.Visibility = Visibility.Visible;
        f.Window.ControlPanelTabs.SelectedItem = f.Window.SystemCommandTab;
        f.Measure(1.5);
        Assert.IsTrue(f.Window.DesktopPetToggle.IsVisible);
        Assert.IsTrue(f.Window.DesktopPetToggle.ActualHeight >= 30);
        Point bottom = f.Window.DesktopPetToggle.TranslatePoint(
            new Point(0, f.Window.DesktopPetToggle.ActualHeight), f.Window.ExpandedCommands);
        Assert.IsTrue(bottom.Y <= f.Window.ExpandedCommands.ActualHeight);
        f.Window.DesktopPetToggle.IsChecked = true;
        f.Window.DesktopPetToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.IsTrue(f.Layout.DesktopPet.IsVisible);
        Assert.IsTrue(f.Pet.AnimationRunning);
        f.Invoke("SetDesktopPetPosition", 500.0, 250.0, true);
        f.Measure(1.5);
        f.Render("cat-system-toggle-1.5.png", 1.5);
    }

    [STATestMethod]
    public void Animation_HiddenPausedSafeOrClosing_StopsWithoutSavingLayout()
    {
        using var f = new Fixture();
        f.Enable();
        Assert.IsTrue(f.Pet.AnimationRunning);
        long generation = f.Field<long>("_layoutSaveGeneration");
        bool dirty = f.Field<bool>("_layoutDirty");
        for (int i = 0; i < 12; i++) f.Pet.AdvanceAnimation();
        Assert.AreEqual(generation, f.Field<long>("_layoutSaveGeneration"));
        Assert.AreEqual(dirty, f.Field<bool>("_layoutDirty"), "动画不应写布局。");

        foreach (string reason in new[] { "_organizerPaused", "_isSafeModeActive", "_isClosing" })
        {
            f.Set(reason, true);
            f.Invoke("UpdateDesktopPetVisibility");
            Assert.AreEqual(Visibility.Collapsed, f.Pet.Visibility);
            Assert.IsFalse(f.Pet.AnimationRunning);
            int frame = f.Pet.CurrentFrame;
            f.Pet.AdvanceAnimation();
            Assert.AreEqual(frame, f.Pet.CurrentFrame, "排队的 tick 在停止后不得推进。");
            Assert.IsTrue(f.Layout.DesktopPet.IsVisible, "临时暂停不能覆盖显示偏好。");
            f.Set(reason, false);
            f.Invoke("UpdateDesktopPetVisibility");
            Assert.IsTrue(f.Pet.AnimationRunning);
        }
        f.Invoke("SetDesktopPetVisible", false);
        Assert.IsFalse(f.Pet.AnimationRunning);
        Assert.IsFalse(f.Layout.DesktopPet.IsVisible);
        Assert.IsFalse(f.Window.DesktopPetToggle.IsChecked);
    }

    [STATestMethod]
    [DataRow("_organizerPaused", "继续整理")]
    [DataRow("_isSafeModeActive", "关闭安全模式")]
    public void EnableDuringTemporaryPause_PreservesPreferenceAndExplainsHiddenState(string reason, string expectedHint)
    {
        using var f = new Fixture();
        f.Set(reason, true);
        f.Invoke("SetDesktopPetVisible", true);
        Assert.IsTrue(f.Layout.DesktopPet.IsVisible);
        Assert.AreEqual(Visibility.Collapsed, f.Pet.Visibility);
        Assert.IsFalse(f.Pet.AnimationRunning);
        StringAssert.Contains(f.Window.StatusText.Text, expectedHint);
    }

    [STATestMethod]
    public void Drag_CommitOnce_OrRestoreOnPauseAndCaptureLoss()
    {
        using var f = new Fixture();
        f.Enable();
        f.BeginFakeDrag(new Point(200, 220), new Point(350, 330));
        Assert.IsTrue((bool)f.Invoke("IsUserInteractionActive")!);
        f.Invoke("CompleteDesktopPetDrag", true);
        Assert.AreEqual(350, f.Layout.DesktopPet.X);
        Assert.AreEqual(330, f.Layout.DesktopPet.Y);
        f.Invoke("CompleteDesktopPetDrag", false);
        Assert.AreEqual(350, f.Layout.DesktopPet.X, "松开后失捕获不能再次回滚或提交。");

        f.BeginFakeDrag(new Point(350, 330), new Point(600, 400));
        f.Set("_organizerPaused", true);
        f.Invoke("UpdateDesktopPetVisibility");
        Assert.IsFalse(f.Field<bool>("_isDesktopPetDragging"));
        Assert.AreEqual(new Point(350, 330), f.Invoke("GetDesktopPetPosition"));
        Assert.AreEqual(350, f.Layout.DesktopPet.X);
        Assert.IsFalse(f.Pet.AnimationRunning);

        f.Set("_organizerPaused", false);
        f.Invoke("UpdateDesktopPetVisibility");
        f.BeginFakeDrag(new Point(350, 330), new Point(600, 400));
        f.Invoke("ResetAllInteractionState", true);
        Assert.AreEqual(new Point(350, 330), f.Invoke("GetDesktopPetPosition"));
        Assert.IsFalse(f.Field<bool>("_isDesktopPetDragging"));
    }

    [STATestMethod]
    public void MonitorRemoval_ClampsPet_ButWorkspaceRemapDoesNotMoveIt()
    {
        using var f = new Fixture();
        f.Enable();
        f.Invoke("SetDesktopPetPosition", 500.0, 250.0, true);
        WorkspaceLayoutState snapshot = WorkspaceLayoutManager.Capture(f.Layout);
        f.Layout.DesktopPet.IsVisible = false;
        f.Layout.DesktopPet.X = 700;
        WorkspaceLayoutManager.Apply(f.Layout, snapshot);
        Assert.AreEqual(700, f.Layout.DesktopPet.X);
        Assert.IsFalse(f.Layout.DesktopPet.IsVisible, "工作区不应重新打开全局配件。");
        DesktopGeometry dual = new([
            Monitor("PRIMARY", new Rect(0, 0, 1000, 700), true),
            Monitor("SECONDARY", new Rect(1000, 0, 800, 700), false)]);
        f.Invoke("RemapLayoutBetweenGeometries", dual, f.Geometry);
        Assert.AreEqual(700, f.Layout.DesktopPet.X, "工作区拓扑迁移不应挪动全局配件。");
        f.Layout.DesktopPet.X = 1600;
        f.Layout.DesktopPet.Y = 620;
        Assert.IsTrue((bool)f.Invoke("RemapDesktopPet", dual, f.Geometry)!);
        f.Invoke("ApplyDesktopPetPosition");
        Assert.IsTrue(f.Layout.DesktopPet.X >= 0 && f.Layout.DesktopPet.X <= 1000 - DesktopPetWidget.WidgetSize);
        Assert.IsTrue(f.Layout.DesktopPet.Y >= 0 && f.Layout.DesktopPet.Y <= 700 - DesktopPetWidget.WidgetSize);
    }

    private static DesktopMonitorRegion Monitor(string name, Rect area, bool primary) => new()
    { DeviceName = name, Bounds = area, WorkArea = area, IsPrimary = primary };

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        public MainWindow Window { get; } = new(false, _ => { });
        public DesktopPetWidget Pet => Window.DesktopPet;
        public AppLayoutData Layout => Field<AppLayoutData>("_appLayout");
        public DesktopGeometry Geometry { get; } = new([Monitor("PRIMARY", new Rect(0, 0, 1000, 700), true)]);
        private readonly OffscreenSource _source;

        public Fixture()
        {
            Window.Content = null;
            Window.ControlPanel.Visibility = Visibility.Collapsed;
            Window.ControlPanelRestoreButton.Visibility = Visibility.Collapsed;
            Window.RecycleBinWidget.Visibility = Visibility.Collapsed;
            Window.RootGrid.Background = new SolidColorBrush(Color.FromRgb(178, 211, 215));
            _source = new OffscreenSource(Window.RootGrid);
            Set("_desktopGeometry", Geometry);
        }

        public void Enable()
        {
            Layout.DesktopPet.IsVisible = true;
            Invoke("InitializeDesktopPet");
            Measure(1);
        }

        public T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(Window)!;
        public void Set(string name, object value) => typeof(MainWindow).GetField(name, Private)!.SetValue(Window, value);
        public object? Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(Window, args);
        public bool Hit(double x, double y) => (bool)Invoke("IsInteractiveClientPoint", new Point(x, y))!;

        public void BeginFakeDrag(Point start, Point preview)
        {
            Invoke("SetDesktopPetPosition", start.X, start.Y, true);
            Set("_isDesktopPetDragging", true);
            Set("_desktopPetDragMoved", true);
            Set("_desktopPetDragStartPosition", start);
            Pet.SetDragging(true);
            Invoke("SetDesktopPetPosition", preview.X, preview.Y, false);
        }

        public void Measure(double scale)
        {
            VisualTreeHelper.SetRootDpi(Window.RootGrid, new DpiScale(scale, scale));
            Window.RootGrid.Measure(new Size(1000, 700));
            Window.RootGrid.Arrange(new Rect(0, 0, 1000, 700));
            Window.RootGrid.UpdateLayout();
        }

        public void Render(string name, double scale, [System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "")
        {
            Window.RootGrid.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)(1000 * scale), (int)(700 * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(Window.RootGrid);
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect((int)(556 * scale), (int)(340 * scale), 1, 1), pixel, 4, 0);
            Assert.IsTrue(pixel[0] < 150, "打包的真实猫图应绘制到桌面上，不能只是空控件。");
            if (Environment.GetEnvironmentVariable("DESKTOPORGANIZER_RENDER_PET_QA") != "1") return;
            string directory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "../../artifacts/desktop-pet"));
            Directory.CreateDirectory(directory);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, name));
            encoder.Save(stream);
        }

        public void Dispose()
        {
            Set("_isClosing", true);
            Invoke("UpdateDesktopPetVisibility");
            Field<DispatcherTimer>("_layoutSaveTimer").Stop();
            Field<CancellationTokenSource>("_lifetimeCts").Cancel();
            Field<FileOperationService>("_fileOperationService").Dispose();
            Field<FolderPortalWatcherCoordinator>("_folderPortalWatcherCoordinator").Dispose();
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
