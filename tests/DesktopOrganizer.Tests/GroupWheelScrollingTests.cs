using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class GroupWheelScrollingTests
{
    [STATestMethod]
    public void Wheel_InterpolatesToSmallerDistanceAndStopsWithoutIdleWork()
    {
        using var fixture = new Fixture();
        VirtualizingGroupPanel panel = fixture.Panel;
        panel.ScrollMouseWheel(-120, 3, animate: true);
        Assert.IsTrue(panel.IsWheelScrolling);
        Assert.AreEqual(0, panel.VerticalOffset, "The first input must not jump straight to its destination.");
        panel.AdvanceWheelScroll(TimeSpan.FromMilliseconds(16));
        double firstFrame = panel.VerticalOffset;
        Assert.IsTrue(firstFrame > 0 && firstFrame < 24);
        panel.AdvanceWheelScroll(TimeSpan.FromMilliseconds(60));
        Assert.IsTrue(panel.VerticalOffset > firstFrame && panel.VerticalOffset < 48);
        panel.AdvanceWheelScroll(TimeSpan.FromMilliseconds(120));
        Assert.AreEqual(48, panel.VerticalOffset, 0.01);
        Assert.IsFalse(panel.IsWheelScrolling);
        Assert.AreEqual(panel.VerticalOffset, fixture.Offsets.Last(), "Saved position must match the visible offset.");
        panel.AdvanceWheelScroll(TimeSpan.FromSeconds(1));
        Assert.AreEqual(48, panel.VerticalOffset, 0.01);
    }

    [STATestMethod]
    public void FractionalWheel_AccumulatesWithoutExpandingSmallDeltasToFullNotches()
    {
        using var fixture = new Fixture();
        for (int i = 0; i < 4; i++) fixture.Panel.ScrollMouseWheel(-30, 3, animate: true);
        fixture.Panel.AdvanceWheelScroll(TimeSpan.FromMilliseconds(120));
        Assert.AreEqual(48, fixture.Panel.VerticalOffset, 0.01);
    }

    [STATestMethod]
    public void TinyWheelInput_ReachesExactTargetWithoutLosingTheFinalFraction()
    {
        using var fixture = new Fixture();
        for (int i = 0; i < 20; i++)
        {
            fixture.Panel.ScrollMouseWheel(-1, 1, animate: true);
            fixture.Panel.AdvanceWheelScroll(TimeSpan.FromMilliseconds(60));
            fixture.Panel.AdvanceWheelScroll(TimeSpan.FromMilliseconds(120));
        }
        Assert.AreEqual(20 * 16 / 120d, fixture.Panel.VerticalOffset, 0.00001);
        Assert.AreEqual(fixture.Panel.VerticalOffset, fixture.Offsets.Last(), 0.00001);
    }

    [STATestMethod]
    public void ReverseWheel_TurnsImmediatelyInsteadOfFinishingOldDirection()
    {
        using var fixture = new Fixture();
        var panel = fixture.Panel;
        panel.SetVerticalOffset(200);
        panel.ScrollMouseWheel(-120, 3, animate: true);
        panel.ScrollMouseWheel(-120, 3, animate: true);
        panel.AdvanceWheelScroll(TimeSpan.FromMilliseconds(30));
        double beforeReverse = panel.VerticalOffset;
        panel.ScrollMouseWheel(120, 3, animate: true);
        panel.AdvanceWheelScroll(TimeSpan.FromMilliseconds(16));
        Assert.IsTrue(panel.VerticalOffset < beforeReverse);
        panel.AdvanceWheelScroll(TimeSpan.FromMilliseconds(120));
        Assert.AreEqual(beforeReverse - 48, panel.VerticalOffset, 0.01);
    }

    [STATestMethod]
    public void DirectOffsetAndGeometryChange_CancelPendingWheelMotion()
    {
        using var fixture = new Fixture();
        var panel = fixture.Panel;
        panel.ScrollMouseWheel(-120, 3, animate: true);
        panel.SetVerticalOffset(150);
        panel.AdvanceWheelScroll(TimeSpan.FromMilliseconds(120));
        Assert.AreEqual(150, panel.VerticalOffset);
        Assert.IsFalse(panel.IsWheelScrolling);
        panel.ScrollMouseWheel(-120, 3, animate: true);
        panel.Measure(new Size(300, 0));
        Assert.IsFalse(panel.IsWheelScrolling);
        Assert.AreEqual(150, panel.VerticalOffset, "Collapse retains the last visible position.");
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HiddenOrReleasedPanel_StopsWheelTimer(bool release)
    {
        using var fixture = new Fixture();
        var panel = fixture.Panel;
        panel.ScrollMouseWheel(-120, 3, animate: true);
        if (release) panel.ReleaseRealizedContainers();
        else panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert.IsFalse(panel.IsWheelScrolling);
        panel.AdvanceWheelScroll(TimeSpan.FromMilliseconds(120));
        Assert.AreEqual(0, panel.VerticalOffset);
    }

    [STATestMethod]
    public void BoundariesAndDisabledAnimation_DoNotLeavePendingOverscroll()
    {
        using var fixture = new Fixture();
        var panel = fixture.Panel;
        panel.ScrollMouseWheel(-120, 3, animate: false);
        Assert.AreEqual(48, panel.VerticalOffset);
        Assert.IsFalse(panel.IsWheelScrolling);
        panel.ScrollMouseWheel(-120, 0, animate: true);
        Assert.AreEqual(48, panel.VerticalOffset);
        double maximum = panel.ExtentHeight - panel.ViewportHeight;
        panel.SetVerticalOffset(maximum - 10);
        for (int i = 0; i < 10; i++) panel.ScrollMouseWheel(-120, 3, animate: true);
        panel.AdvanceWheelScroll(TimeSpan.FromMilliseconds(120));
        Assert.AreEqual(maximum, panel.VerticalOffset);
        panel.ScrollMouseWheel(30, 3, animate: true);
        panel.AdvanceWheelScroll(TimeSpan.FromMilliseconds(120));
        Assert.AreEqual(maximum - 12, panel.VerticalOffset);
    }

    [STATestMethod]
    public void ScrollOwner_PreviewConsumesRawDelta_AndDetachesWhenReplaced()
    {
        using var fixture = new Fixture();
        var first = new ScrollViewer();
        var second = new ScrollViewer();
        fixture.Panel.ScrollOwner = first;
        var wheel = Wheel(-30);
        first.RaiseEvent(wheel);
        Assert.IsTrue(wheel.Handled, "The ScrollViewer must not process the same input again as a whole notch.");
        fixture.Panel.AdvanceWheelScroll(TimeSpan.FromMilliseconds(120));
        double expected = Math.Min(fixture.Panel.ExtentHeight - fixture.Panel.ViewportHeight,
            WheelScrollPolicy.GetVerticalOffsetChange(-30, SystemParameters.WheelScrollLines, 90, 270));
        Assert.AreEqual(expected, fixture.Panel.VerticalOffset, 0.01);
        fixture.Panel.ScrollOwner = second;
        wheel = Wheel(-30);
        first.RaiseEvent(wheel);
        Assert.IsFalse(wheel.Handled);
    }

    [STATestMethod]
    public void RealScrollViewer_LayoutFramesKeepAnimationAndVirtualizationIntact()
    {
        using var windowFixture = new DesktopPathRefreshTests.Fixture();
        using var fixture = new Fixture();
        var scroll = new ScrollViewer { Content = fixture.Panel, Width = 300, Height = 270,
            CanContentScroll = true, Style = (Style)windowFixture.Window.FindResource("GroupScrollViewerStyle") };
        using var source = new OffscreenSource(scroll);
        void Layout()
        {
            scroll.Measure(new Size(300, 270));
            scroll.Arrange(new Rect(0, 0, 300, 270));
            scroll.UpdateLayout();
        }
        Layout();
        Assert.AreSame(scroll, fixture.Panel.ScrollOwner);
        fixture.Panel.ScrollMouseWheel(-120, 3, animate: true);
        double previous = 0;
        foreach (int elapsed in new[] { 16, 32, 48, 64, 80, 96, 120 })
        {
            fixture.Panel.AdvanceWheelScroll(TimeSpan.FromMilliseconds(elapsed));
            Layout();
            Assert.IsTrue(fixture.Panel.VerticalOffset >= previous);
            Assert.AreEqual(elapsed < 120, fixture.Panel.IsWheelScrolling,
                "ScrollViewer layout/offset synchronization must not cancel an active wheel animation.");
            Assert.IsTrue(fixture.Panel.Children.Count <= 21, "Scrolling must retain bounded virtualization.");
            previous = fixture.Panel.VerticalOffset;
        }
        Assert.AreEqual(48, scroll.VerticalOffset, 0.01);
        Assert.AreEqual(-48, fixture.Panel.Children[0].TranslatePoint(new Point(), fixture.Panel).Y, 0.01);
    }

    [STATestMethod]
    public void MouseDown_StopsMotionBeforeDraggingWithoutConsumingTheClick()
    {
        using var fixture = new Fixture();
        var scroll = new ScrollViewer();
        fixture.Panel.ScrollOwner = scroll;
        fixture.Panel.ScrollMouseWheel(-120, 3, animate: true);
        fixture.Panel.AdvanceWheelScroll(TimeSpan.FromMilliseconds(30));
        double current = fixture.Panel.VerticalOffset;
        var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        { RoutedEvent = Mouse.PreviewMouseDownEvent };
        scroll.RaiseEvent(down);
        Assert.IsFalse(down.Handled);
        Assert.IsFalse(fixture.Panel.IsWheelScrolling);
        fixture.Panel.AdvanceWheelScroll(TimeSpan.FromMilliseconds(120));
        Assert.AreEqual(current, fixture.Panel.VerticalOffset);
    }

    [STATestMethod]
    public void DispatcherTimer_DrivesMotionToCompletionWithoutOpeningAWindow()
    {
        using var fixture = new Fixture();
        fixture.Panel.ScrollMouseWheel(-120, 3, animate: true);
        var frame = new DispatcherFrame();
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timeout.Tick += (_, _) => frame.Continue = false;
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        poll.Tick += (_, _) => { if (!fixture.Panel.IsWheelScrolling) frame.Continue = false; };
        timeout.Start(); poll.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timeout.Stop(); poll.Stop(); }
        Assert.IsFalse(fixture.Panel.IsWheelScrolling);
        Assert.AreEqual(48, fixture.Panel.VerticalOffset, 0.1);
        Assert.IsTrue(fixture.Offsets.Count > 1, "Animation must produce intermediate positions.");
    }

    private static MouseWheelEventArgs Wheel(int delta) => new(Mouse.PrimaryDevice, 0, delta)
    { RoutedEvent = Mouse.PreviewMouseWheelEvent };

    private sealed class Fixture : IDisposable
    {
        public List<double> Offsets { get; } = [];
        public VirtualizingGroupPanel Panel { get; }
        public Fixture()
        {
            Panel = new VirtualizingGroupPanel(Enumerable.Range(0, 90)
                .Select(i => new GroupVirtualItem($"item-{i}", $"test-{i}")).ToArray(),
                _ => new Border(), _ => { }, 3, 100, 90, 0, Offsets.Add);
            Panel.Measure(new Size(300, 270));
            Panel.Arrange(new Rect(0, 0, 300, 270));
        }
        public void Dispose() { Panel.ScrollOwner = null; Panel.ReleaseRealizedContainers(); }
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
