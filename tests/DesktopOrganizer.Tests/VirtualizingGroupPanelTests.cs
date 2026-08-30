using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;
using System.Windows.Controls;

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

    private static VirtualizingGroupPanel CreatePanel(
        int itemCount,
        out Dictionary<string, FrameworkElement> visuals)
    {
        var createdVisuals = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
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
            _ => { },
            columnCount: 3,
            slotWidth: 100,
            rowHeight: 80,
            initialVerticalOffset: 0);

        visuals = createdVisuals;
        return panel;
    }
}
