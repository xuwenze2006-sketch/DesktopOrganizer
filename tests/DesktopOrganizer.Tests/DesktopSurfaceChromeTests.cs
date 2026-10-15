using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopSurfaceChromeTests
{
    [STATestMethod]
    [DataRow(1d)]
    [DataRow(1.5d)]
    [DataRow(2d)]
    public void StatusDetails_AfterRefreshAndPathChange_WrapWithinMonitor(double scale)
    {
        using var fixture = new DesktopPathRefreshTests.Fixture();
        fixture.Refresh();
        AssertStatusCard(fixture.Window, scale, "refresh");
        fixture.UserPath = fixture.UserB;
        fixture.Apply(fixture.Resolver.Refresh());
        AssertStatusCard(fixture.Window, scale, "paths");
        ToolTip tip = (ToolTip)fixture.Window.StatusText.ToolTip;
        StringAssert.Contains(string.Join("\n", Descendants<TextBlock>(tip).Select(block => block.Text)), fixture.UserB);
        typeof(MainWindow).GetField("_desktopGeometry", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(fixture.Window, new DesktopGeometry([new DesktopMonitorRegion {
                DeviceName = "narrow", IsPrimary = true, Bounds = new Rect(0, 0, 240, 800), WorkArea = new Rect(0, 0, 240, 800) }]));
        tip.RaiseEvent(new RoutedEventArgs(ToolTip.OpenedEvent));
        Assert.IsTrue(tip.Width <= 216, "The card must adapt when moved to a narrow monitor.");
        fixture.UserPath = Path.Combine(fixture.Root, "unavailable");
        fixture.Refresh();
        AssertStatusCard(fixture.Window, scale, "unavailable");
        fixture.UserPath = fixture.UserA;
        fixture.Refresh();
        AssertStatusCard(fixture.Window, scale, "recovered");
    }

    [STATestMethod]
    [DataRow(1d)]
    [DataRow(1.5d)]
    [DataRow(2d)]
    public void GroupScrollBar_AvoidsResizeCorner_AndStillScrollsToLastItem(double scale)
    {
        using var fixture = new DesktopPathRefreshTests.Fixture();
        var rows = new StackPanel();
        for (int i = 0; i < 30; i++)
            rows.Children.Add(new TextBlock { Text = $"桌面项目 {i + 1:00}", Height = 40, Margin = new Thickness(12, 0, 0, 0) });
        var scroll = new ScrollViewer {
            Width = 300, Height = 280, Content = rows, CanContentScroll = false,
            Style = (Style)fixture.Window.FindResource("GroupScrollViewerStyle") };
        var body = new Grid { Width = 300, Height = 280, Background = WarmPaperTheme.SoftSurfaceBrush };
        var resize = new Thumb { Style = (Style)fixture.Window.FindResource("GroupResizeThumbStyle"),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        body.Children.Add(scroll);
        body.Children.Add(resize);
        using var source = new OffscreenSource(body);
        VisualTreeHelper.SetRootDpi(body, new DpiScale(scale, scale));
        Layout(body, new Size(300, 280));
        var bar = (ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar", scroll);
        Assert.AreEqual(Visibility.Visible, bar.Visibility);
        Rect bounds = bar.TransformToAncestor(scroll).TransformBounds(new Rect(bar.RenderSize));
        Rect resizeBounds = resize.TransformToAncestor(body).TransformBounds(new Rect(resize.RenderSize));
        Assert.IsTrue(bounds.Bottom <= resizeBounds.Top,
            $"Scrollbar bottom {bounds.Bottom} overlaps the resize corner starting at {resizeBounds.Top}.");
        var track = (Track)bar.Template.FindName("PART_Track", bar);
        track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0));
        track.Thumb.RaiseEvent(new DragDeltaEventArgs(0, 30));
        track.Thumb.RaiseEvent(new DragCompletedEventArgs(0, 30, false));
        Layout(scroll, new Size(300, 280));
        Assert.IsTrue(scroll.VerticalOffset > 0, "Dragging the thumb must scroll the content.");
        double previous = scroll.VerticalOffset;
        ScrollBar.PageDownCommand.Execute(null, bar);
        Layout(scroll, new Size(300, 280));
        Assert.IsTrue(scroll.VerticalOffset > previous, "Clicking the track must still page down.");
        scroll.ScrollToBottom();
        Layout(scroll, new Size(300, 280));
        Assert.AreEqual(scroll.ScrollableHeight, scroll.VerticalOffset, 0.01);
        var last = (FrameworkElement)rows.Children[^1];
        Rect lastBounds = last.TransformToAncestor(scroll).TransformBounds(new Rect(last.RenderSize));
        Assert.IsTrue(lastBounds.Top >= 0 && lastBounds.Bottom <= scroll.ActualHeight + 1);
        Render(body, scale, "scroll-bottom");
        rows.Children.Clear();
        rows.Children.Add(new TextBlock { Text = "仅一个项目", Height = 40 });
        Layout(scroll, new Size(300, 280));
        Assert.AreEqual(Visibility.Collapsed, bar.Visibility);
    }

    private static void AssertStatusCard(MainWindow window, double scale, string name)
    {
        Assert.IsInstanceOfType<ToolTip>(window.StatusText.ToolTip,
            "Refresh must not replace the styled card with an unbounded system tooltip.");
        var tip = (ToolTip)window.StatusText.ToolTip;
        Assert.AreSame(window.ControlPanelDragHandle, tip.PlacementTarget);
        Assert.AreEqual(PlacementMode.Bottom, tip.Placement);
        using var source = new OffscreenSource(tip);
        VisualTreeHelper.SetRootDpi(tip, new DpiScale(scale, scale));
        Layout(tip, new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.IsTrue(tip.ActualWidth <= 320);
        var blocks = Descendants<TextBlock>(tip).ToArray();
        Assert.IsTrue(blocks.Any(block => block.Text.Contains("用户桌面")));
        Assert.IsTrue(blocks.Where(block => block.Text.Length > 30).All(block => block.TextWrapping == TextWrapping.Wrap));
        Render(tip, scale, name);
    }

    private static void Layout(FrameworkElement element, Size size)
    {
        element.Measure(size);
        element.Arrange(new Rect(new Point(), element.DesiredSize));
        element.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Render(FrameworkElement element, double scale, string name)
    {
        if (Environment.GetEnvironmentVariable("DESKTOPORGANIZER_RENDER_CHROME_QA") != "1") return;
        string directory = Path.Combine(TestProjectFiles.Root, "artifacts/ui-chrome-fix-20261015/previews");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale),
            (int)Math.Ceiling(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, $"{name}-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png"));
        encoder.Save(stream);
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
