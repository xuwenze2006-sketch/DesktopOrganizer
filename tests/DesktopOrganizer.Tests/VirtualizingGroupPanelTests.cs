using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class VirtualizingGroupPanelTests
{
    [STATestMethod]
    public void CalculateInsertionBoundary_IncludesCurrentVerticalOffset()
    {
        VirtualizingGroupPanel panel = CreatePanel(itemCount: 15, out _);
        panel.Measure(new Size(300, 80));
        panel.Arrange(new Rect(0, 0, 300, 80));
        panel.SetVerticalOffset(160);

        var position = new Point(225, 20);
        int expected = GroupItemDropPolicy.CalculateInsertionBoundary(
            position.X,
            position.Y,
            columnCount: 3,
            slotWidth: 100,
            rowHeight: 80,
            verticalOffset: 160,
            itemCount: 15);
        int boundaryWithoutOffset = GroupItemDropPolicy.CalculateInsertionBoundary(
            position.X,
            position.Y,
            columnCount: 3,
            slotWidth: 100,
            rowHeight: 80,
            verticalOffset: 0,
            itemCount: 15);

        Assert.AreNotEqual(expected, boundaryWithoutOffset);
        Assert.AreEqual(expected, panel.CalculateInsertionBoundary(position));
    }

    [STATestMethod]
    public void CalculateInsertionBoundary_AfterDetach_StillUsesCompleteItemSnapshot()
    {
        VirtualizingGroupPanel panel = CreatePanel(
            itemCount: 8,
            out Dictionary<string, FrameworkElement> visuals);
        panel.Measure(new Size(300, 80));
        panel.Arrange(new Rect(0, 0, 300, 80));
        int boundaryBeforeDetach = panel.CalculateInsertionBoundary(
            new Point(10_000, 10_000));

        Assert.IsTrue(panel.DetachForDrag(visuals["item-0"]));
        int boundaryAfterDetach = panel.CalculateInsertionBoundary(
            new Point(10_000, 10_000));

        int boundaryWithSuppressedCount = GroupItemDropPolicy.CalculateInsertionBoundary(
            localX: 10_000,
            localY: 10_000,
            columnCount: 3,
            slotWidth: 100,
            rowHeight: 80,
            verticalOffset: 0,
            itemCount: 7);
        Assert.AreNotEqual(boundaryWithSuppressedCount, boundaryBeforeDetach);
        Assert.AreEqual(boundaryBeforeDetach, boundaryAfterDetach);
        CollectionAssert.AreEqual(
            Enumerable.Range(0, 8).Select(index => $"item-{index}").ToArray(),
            panel.GetItemNamesSnapshot().ToArray());
        Assert.IsFalse(panel.IsStable);
    }

    [STATestMethod]
    public void InsertionIndicator_MovesWithinSamePanelAndClearsWithoutChangingItems()
    {
        VirtualizingGroupPanel panel = CreatePanel(itemCount: 8, out _);
        panel.Measure(new Size(300, 160));
        panel.Arrange(new Rect(0, 0, 300, 160));
        int initialChildCount = panel.Children.Count;

        int firstBoundary = panel.ShowInsertionIndicator(
            new Point(25, 20),
            Brushes.SeaGreen);
        panel.Measure(new Size(300, 160));
        panel.Arrange(new Rect(0, 0, 300, 160));
        Border indicator = panel.Children
            .OfType<Border>()
            .Single(child => !child.IsHitTestVisible);
        Point firstIndicatorPosition = indicator.TranslatePoint(new Point(), panel);
        int secondBoundary = panel.ShowInsertionIndicator(
            new Point(275, 100),
            Brushes.SeaGreen);
        panel.Measure(new Size(300, 160));
        panel.Arrange(new Rect(0, 0, 300, 160));
        Point secondIndicatorPosition = indicator.TranslatePoint(new Point(), panel);

        Assert.AreEqual(0, firstBoundary);
        Assert.AreEqual(6, secondBoundary);
        Assert.AreEqual(new Point(0, 8), firstIndicatorPosition);
        Assert.AreEqual(new Point(296, 88), secondIndicatorPosition);
        Assert.AreEqual(new Size(4, 64), indicator.RenderSize);
        Assert.AreEqual(initialChildCount + 1, panel.Children.Count);
        CollectionAssert.AreEqual(
            Enumerable.Range(0, 8).Select(index => $"item-{index}").ToArray(),
            panel.GetItemNamesSnapshot().ToArray());

        panel.ClearInsertionIndicator();

        Assert.AreEqual(initialChildCount, panel.Children.Count);
    }

    [STATestMethod]
    public void ReleaseRealizedContainers_RecyclesViewportAndPreservesScrollOffset()
    {
        VirtualizingGroupPanel panel = CreatePanel(
            itemCount: 30,
            out _,
            out List<FrameworkElement> recycledVisuals);
        panel.Measure(new Size(300, 80));
        panel.Arrange(new Rect(0, 0, 300, 80));
        panel.SetVerticalOffset(160);
        panel.Measure(new Size(300, 80));
        panel.Arrange(new Rect(0, 0, 300, 80));
        int realizedBeforeRelease = panel.Children.Count;
        int recycledBeforeRelease = recycledVisuals.Count;
        double offsetBeforeRelease = panel.VerticalOffset;

        panel.ReleaseRealizedContainers();

        Assert.AreEqual(0, panel.Children.Count);
        Assert.AreEqual(
            realizedBeforeRelease,
            recycledVisuals.Count - recycledBeforeRelease);
        Assert.AreEqual(offsetBeforeRelease, panel.VerticalOffset);

        panel.Measure(new Size(300, 80));
        panel.Arrange(new Rect(0, 0, 300, 80));

        Assert.IsGreaterThan(0, panel.Children.Count);
        Assert.AreEqual(offsetBeforeRelease, panel.VerticalOffset);
    }

    [STATestMethod]
    public void ReleaseRealizedContainers_DoesNotRecycleDetachedDragSource()
    {
        VirtualizingGroupPanel panel = CreatePanel(
            itemCount: 8,
            out Dictionary<string, FrameworkElement> visuals,
            out List<FrameworkElement> recycledVisuals);
        panel.Measure(new Size(300, 160));
        panel.Arrange(new Rect(0, 0, 300, 160));
        FrameworkElement detached = visuals["item-0"];
        Assert.IsTrue(panel.DetachForDrag(detached));
        int realizedAfterDetach = panel.Children.Count;

        panel.ReleaseRealizedContainers();

        Assert.AreEqual(0, panel.Children.Count);
        Assert.AreEqual(realizedAfterDetach, recycledVisuals.Count);
        Assert.IsFalse(recycledVisuals.Contains(detached));
        Assert.IsFalse(panel.IsStable);
    }

    private static VirtualizingGroupPanel CreatePanel(
        int itemCount,
        out Dictionary<string, FrameworkElement> visuals)
    {
        return CreatePanel(itemCount, out visuals, out _);
    }

    private static VirtualizingGroupPanel CreatePanel(
        int itemCount,
        out Dictionary<string, FrameworkElement> visuals,
        out List<FrameworkElement> recycledVisuals)
    {
        var createdVisuals = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
        var recycled = new List<FrameworkElement>();
        var items = Enumerable.Range(0, itemCount)
            .Select(index => new GroupVirtualItem($"item-{index}", $@"C:\Desktop\item-{index}.txt"))
            .ToList();
        var panel = new VirtualizingGroupPanel(
            items,
            item =>
            {
                var visual = new Border();
                createdVisuals[item.DisplayName] = visual;
                return visual;
            },
            recycled.Add,
            columnCount: 3,
            slotWidth: 100,
            rowHeight: 80,
            initialVerticalOffset: 0);

        visuals = createdVisuals;
        recycledVisuals = recycled;
        return panel;
    }
}
