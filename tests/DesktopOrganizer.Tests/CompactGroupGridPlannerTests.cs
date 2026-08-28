using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class CompactGroupGridPlannerTests
{
    [TestMethod]
    public void TryPlan_VariableHeights_UsesThreeCompactTracks()
    {
        CompactGroupGridItem[] items =
        [
            new("a", 280, 300),
            new("b", 280, 200),
            new("c", 280, 100),
            new("d", 280, 80),
            new("e", 280, 80),
            new("f", 280, 80)
        ];

        Dictionary<string, Point>? plan = CompactGroupGridPlanner.TryPlan(
            items,
            [new Rect(0, 0, 920, 600)],
            [],
            trackWidth: 300,
            gap: 10,
            maximumColumns: 3);

        Assert.IsNotNull(plan);
        CollectionAssert.AreEquivalent(
            new[] { 0d, 310d, 620d },
            plan.Values.Select(point => point.X).Distinct().ToArray());
        Assert.AreEqual(new Point(620, 110), plan["d"]);
        Assert.AreEqual(new Point(310, 210), plan["f"]);
        AssertNoOverlap(items, plan, gap: 10);
    }

    [TestMethod]
    public void TryPlan_WideCard_SpansTwoTracksWithoutResizing()
    {
        CompactGroupGridItem[] items =
        [
            new("wide", 610, 120),
            new("small-1", 280, 100),
            new("small-2", 280, 100)
        ];

        Dictionary<string, Point>? plan = CompactGroupGridPlanner.TryPlan(
            items,
            [new Rect(0, 0, 920, 400)],
            [],
            trackWidth: 300,
            gap: 10,
            maximumColumns: 3);

        Assert.IsNotNull(plan);
        Assert.AreEqual(new Point(0, 0), plan["wide"]);
        Assert.AreEqual(new Point(620, 0), plan["small-1"]);
        Assert.AreEqual(new Point(620, 110), plan["small-2"]);
        AssertNoOverlap(items, plan, gap: 10);
    }

    [TestMethod]
    public void TryPlan_WidthJustAboveTrack_SpansNextTrack()
    {
        CompactGroupGridItem[] items =
        [
            new("two-track", 353, 100),
            new("three-track", 717, 100)
        ];

        Dictionary<string, Point>? plan = CompactGroupGridPlanner.TryPlan(
            items,
            [new Rect(0, 0, 1080, 400)],
            [],
            trackWidth: 352,
            gap: 12,
            maximumColumns: 3);

        Assert.IsNotNull(plan);
        Assert.AreEqual(new Point(0, 0), plan["two-track"]);
        Assert.AreEqual(new Point(0, 112), plan["three-track"]);
        AssertNoOverlap(items, plan, gap: 12);
    }

    [TestMethod]
    public void TryPlan_ObstacleBlocksOneTrack_UsesOtherTracks()
    {
        CompactGroupGridItem[] items =
        [
            new("a", 280, 100),
            new("b", 280, 100),
            new("c", 280, 100)
        ];
        var obstacle = new Rect(310, 0, 280, 160);

        Dictionary<string, Point>? plan = CompactGroupGridPlanner.TryPlan(
            items,
            [new Rect(0, 0, 920, 500)],
            [obstacle],
            trackWidth: 300,
            gap: 10,
            maximumColumns: 3);

        Assert.IsNotNull(plan);
        Assert.AreEqual(new Point(0, 0), plan["a"]);
        Assert.AreEqual(new Point(620, 0), plan["b"]);
        Assert.AreEqual(new Point(0, 110), plan["c"]);
        foreach ((string id, Point position) in plan)
        {
            CompactGroupGridItem item = items.Single(candidate => candidate.Id == id);
            Assert.IsFalse(new Rect(position.X, position.Y, item.Width, item.Height)
                .IntersectsWith(obstacle));
        }
    }

    [TestMethod]
    public void TryPlan_NarrowWorkspace_DegradesToTwoColumns()
    {
        CompactGroupGridItem[] items =
        [
            new("a", 280, 100),
            new("b", 280, 100),
            new("c", 280, 100)
        ];

        Dictionary<string, Point>? plan = CompactGroupGridPlanner.TryPlan(
            items,
            [new Rect(0, 0, 610, 400)],
            [],
            trackWidth: 300,
            gap: 10,
            maximumColumns: 3);

        Assert.IsNotNull(plan);
        Assert.AreEqual(new Point(0, 0), plan["a"]);
        Assert.AreEqual(new Point(310, 0), plan["b"]);
        Assert.AreEqual(new Point(0, 110), plan["c"]);
    }

    [TestMethod]
    public void TryPlan_ProductionTrackWidth_DegradesToTwoColumns()
    {
        CompactGroupGridItem[] items =
        [
            new("a", 352, 100),
            new("b", 352, 100),
            new("c", 352, 100)
        ];
        var workspace = new Rect(20, 40, 716, 400);

        Dictionary<string, Point>? plan = CompactGroupGridPlanner.TryPlan(
            items,
            [workspace],
            [],
            trackWidth: 352,
            gap: 12,
            maximumColumns: 3);

        Assert.IsNotNull(plan);
        Assert.AreEqual(new Point(20, 40), plan["a"]);
        Assert.AreEqual(new Point(384, 40), plan["b"]);
        Assert.AreEqual(new Point(20, 152), plan["c"]);
        AssertWithinAnyWorkspace(items, plan, [workspace]);
    }

    [TestMethod]
    public void TryPlan_FlexibleFallbackTracksCanUseMoreThanThreeColumns()
    {
        CompactGroupGridItem[] items =
        [
            new("a", 176, 100),
            new("b", 176, 100),
            new("c", 176, 100),
            new("d", 176, 100)
        ];

        Dictionary<string, Point>? plan = CompactGroupGridPlanner.TryPlan(
            items,
            [new Rect(0, 0, 756, 120)],
            [],
            trackWidth: 180,
            gap: 12,
            maximumColumns: int.MaxValue);

        Assert.IsNotNull(plan);
        CollectionAssert.AreEqual(
            new[] { 0d, 192d, 384d, 576d },
            items.Select(item => plan[item.Id].X).ToArray());
    }

    [TestMethod]
    public void TryPlan_FlexibleFallback_AllowsCardToUseTrailingWorkspaceRemainder()
    {
        CompactGroupGridItem[] items =
        [
            new("full-width", 1888, 100)
        ];
        var workspace = new Rect(16, 20, 1888, 120);

        Dictionary<string, Point>? plan = CompactGroupGridPlanner.TryPlan(
            items,
            [workspace],
            [],
            trackWidth: 180,
            gap: 12,
            maximumColumns: int.MaxValue);

        Assert.IsNotNull(plan);
        Assert.AreEqual(new Point(16, 20), plan["full-width"]);
        AssertWithinAnyWorkspace(items, plan, [workspace]);
    }

    [TestMethod]
    public void TryPlan_PrioritizesWorkspaceOrderOverNegativeCoordinates()
    {
        CompactGroupGridItem[] items =
        [
            new("a", 280, 90),
            new("b", 280, 90),
            new("c", 280, 90),
            new("d", 280, 90)
        ];

        Dictionary<string, Point>? plan = CompactGroupGridPlanner.TryPlan(
            items,
            [
                new Rect(0, 0, 920, 100),
                new Rect(-920, 0, 920, 100)
            ],
            [],
            trackWidth: 300,
            gap: 10,
            maximumColumns: 3);

        Assert.IsNotNull(plan);
        Assert.AreEqual(new Point(0, 0), plan["a"]);
        Assert.AreEqual(new Point(310, 0), plan["b"]);
        Assert.AreEqual(new Point(620, 0), plan["c"]);
        Assert.AreEqual(new Point(-920, 0), plan["d"]);
    }

    [TestMethod]
    public void TryPlan_WhenEveryWorkspaceIsTooShort_ReturnsNoPartialPlan()
    {
        CompactGroupGridItem[] items =
        [
            new("a", 280, 100),
            new("b", 280, 100)
        ];

        Dictionary<string, Point>? plan = CompactGroupGridPlanner.TryPlan(
            items,
            [new Rect(0, 0, 300, 150)],
            [],
            trackWidth: 300,
            gap: 10,
            maximumColumns: 3);

        Assert.IsNull(plan);
    }

    private static void AssertNoOverlap(
        IReadOnlyList<CompactGroupGridItem> items,
        IReadOnlyDictionary<string, Point> plan,
        double gap)
    {
        Rect[] bounds = items
            .Select(item =>
            {
                Point position = plan[item.Id];
                return new Rect(position.X, position.Y, item.Width, item.Height);
            })
            .ToArray();

        for (int index = 0; index < bounds.Length; index++)
        {
            for (int other = index + 1; other < bounds.Length; other++)
            {
                Rect first = bounds[index];
                Rect second = bounds[other];
                Assert.IsFalse(
                    first.IntersectsWith(second),
                    $"Items {items[index].Id} and {items[other].Id} overlap.");

                double horizontalGap = Math.Max(
                    0,
                    Math.Max(second.Left - first.Right, first.Left - second.Right));
                double verticalGap = Math.Max(
                    0,
                    Math.Max(second.Top - first.Bottom, first.Top - second.Bottom));
                Assert.IsTrue(
                    horizontalGap >= gap - 0.01 || verticalGap >= gap - 0.01,
                    $"Items {items[index].Id} and {items[other].Id} are too close.");
            }
        }
    }

    private static void AssertWithinAnyWorkspace(
        IReadOnlyList<CompactGroupGridItem> items,
        IReadOnlyDictionary<string, Point> plan,
        IReadOnlyList<Rect> workspaces)
    {
        foreach (CompactGroupGridItem item in items)
        {
            Point position = plan[item.Id];
            var bounds = new Rect(position.X, position.Y, item.Width, item.Height);
            Assert.IsTrue(
                workspaces.Any(workspace => workspace.Contains(bounds)),
                $"Item {item.Id} is outside every workspace.");
        }
    }
}
