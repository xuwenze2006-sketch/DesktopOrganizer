using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class FreeIconPlacementPlannerTests
{
    private static readonly Rect WorkArea = new(0, 0, 1200, 800);

    [TestMethod]
    public void TryPlan_NearBottomPreservesVerticalCellSpacing()
    {
        Dictionary<string, Point>? plan = FreeIconPlacementPlanner.TryPlan(
            [
                new FreeIconPlacementRequest("A", 250, 640),
                new FreeIconPlacementRequest("B", 250, 730)
            ],
            [WorkArea],
            [],
            90,
            90,
            StringComparer.OrdinalIgnoreCase);

        Assert.IsNotNull(plan);
        Assert.AreEqual(new Point(250, 640), plan["A"]);
        Assert.AreEqual(new Point(250, 550), plan["B"]);
    }

    [TestMethod]
    public void TryPlan_ExistingObstacleReservesPreferredColumn()
    {
        Dictionary<string, Point>? plan = FreeIconPlacementPlanner.TryPlan(
            [
                new FreeIconPlacementRequest("A", 250, 640),
                new FreeIconPlacementRequest("B", 250, 730)
            ],
            [WorkArea],
            [new Rect(250, 640, 90, 90)],
            90,
            90,
            StringComparer.OrdinalIgnoreCase);

        Assert.IsNotNull(plan);
        Assert.AreEqual(new Point(250, 550), plan["A"]);
        Assert.AreEqual(new Point(250, 460), plan["B"]);
    }

    [TestMethod]
    public void TryPlan_NoCapacityReturnsNullWithoutPartialResult()
    {
        Dictionary<string, Point>? plan = FreeIconPlacementPlanner.TryPlan(
            [new FreeIconPlacementRequest("A", 0, 0)],
            [new Rect(0, 0, 90, 90)],
            [new Rect(0, 0, 90, 90)],
            90,
            90,
            StringComparer.OrdinalIgnoreCase);

        Assert.IsNull(plan);
    }

    [TestMethod]
    public void TryPlan_NonIntegralWorkAreaIncludesClampedEdgeSlot()
    {
        Dictionary<string, Point>? plan = FreeIconPlacementPlanner.TryPlan(
            [new FreeIconPlacementRequest("A", 0, 0)],
            [new Rect(0, 0, 190, 90)],
            [new Rect(0, 0, 100, 90)],
            90,
            90,
            StringComparer.OrdinalIgnoreCase);

        Assert.IsNotNull(plan);
        Assert.AreEqual(new Point(100, 0), plan["A"]);
    }
}
