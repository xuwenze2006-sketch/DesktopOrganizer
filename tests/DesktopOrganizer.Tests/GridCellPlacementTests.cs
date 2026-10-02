using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class GridCellPlacementTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(40, 30, 40, 30)]
    [DataRow(-4, -8, 0, 0)]
    [DataRow(90, 80, 79, 59)]
    public void FindNearest_AvailablePreferenceNeedsOnlyOneAvailabilityCheck(
        int preferredColumn, int preferredRow, int expectedColumn, int expectedRow)
    {
        int checks = 0;
        GridCell? result = GridCellSearch.FindNearest(80, 60, preferredColumn, preferredRow,
            (_, _) => { checks++; return true; });

        Assert.AreEqual(new GridCell(expectedColumn, expectedRow), result);
        Assert.AreEqual(1, checks,
            "首选格可用时已经找到距离为零的最优位置，不应继续查询整个网格。");
    }

    [TestMethod]
    public void FindNearest_BlockedPreferenceAvoidsRepeatedChecksAndPreservesTieOrder()
    {
        var visits = new Dictionary<GridCell, int>();
        GridCell? result = GridCellSearch.FindNearest(8, 6, 3, 2, (column, row) =>
        {
            var cell = new GridCell(column, row);
            visits[cell] = visits.GetValueOrDefault(cell) + 1;
            return cell != new GridCell(3, 2);
        });

        Assert.AreEqual(new GridCell(3, 1), result);
        Assert.IsLessThanOrEqualTo(48, visits.Count);
        Assert.IsTrue(visits.Values.All(count => count == 1),
            "首选格不可用时，回退搜索不应重复执行同一格的几何检查。");
    }

    [TestMethod]
    [DataRow(0, 10)]
    [DataRow(10, 0)]
    [DataRow(-1, 10)]
    [DataRow(10, -1)]
    public void FindNearest_InvalidDimensionsDoNotCheckAvailability(int columns, int rows)
    {
        int checks = 0;
        GridCell? result = GridCellSearch.FindNearest(columns, rows, 0, 0,
            (_, _) => { checks++; return true; });

        Assert.IsNull(result);
        Assert.AreEqual(0, checks);
    }

    [TestMethod]
    public void RefreshPlan_FreePreferredCellsNeedOnlyOneGeometryCheckPerItem()
    {
        List<GridPlacementRequest> requests = Enumerable.Range(0, 120)
            .Select(index => new GridPlacementRequest($"item-{index}", index % 20, index / 20))
            .ToList();
        int checks = 0;
        Dictionary<string, GridCell?> result = GridRefreshPlacementPlanner.Plan(
            20, 12, requests, [], (_, _) => { checks++; return true; });

        Assert.HasCount(120, result);
        foreach (GridPlacementRequest request in requests)
            Assert.AreEqual(new GridCell(request.PreferredColumn, request.PreferredRow), result[request.Key]);
        TestContext.WriteLine($"20×12 网格放置 120 个项目的几何检查次数：{checks}");
        Assert.AreEqual(120, checks,
            "默认位置全部可用时，批量刷新不应为每个新项目重查整个网格。");
    }

    [TestMethod]
    public void FindNearest_PreferredCellAvailable_ReturnsPreferredCell()
    {
        GridCell? result = GridCellSearch.FindNearest(
            columns: 4,
            rows: 3,
            preferredColumn: 2,
            preferredRow: 1,
            (_, _) => true);

        Assert.AreEqual(new GridCell(2, 1), result);
    }

    [TestMethod]
    public void FindNearest_EqualDistance_UsesRowThenColumnOrder()
    {
        GridCell? result = GridCellSearch.FindNearest(
            columns: 3,
            rows: 3,
            preferredColumn: 1,
            preferredRow: 1,
            (column, row) => (column, row) is (1, 0) or (0, 1));

        Assert.AreEqual(new GridCell(1, 0), result);
    }

    [TestMethod]
    public void FindNearest_PreferredCellUnavailable_SkipsIt()
    {
        GridCell? result = GridCellSearch.FindNearest(
            columns: 3,
            rows: 1,
            preferredColumn: 1,
            preferredRow: 0,
            (column, _) => column != 1);

        Assert.AreEqual(new GridCell(0, 0), result);
    }

    [TestMethod]
    public void FindNearest_OutOfRangePreference_IsClampedBeforeSearch()
    {
        GridCell? result = GridCellSearch.FindNearest(
            columns: 3,
            rows: 2,
            preferredColumn: 99,
            preferredRow: -4,
            (_, _) => true);

        Assert.AreEqual(new GridCell(2, 0), result);
    }

    [TestMethod]
    public void FindNearest_NoAvailableCell_ReturnsNullInsteadOfPreferredCell()
    {
        GridCell? result = GridCellSearch.FindNearest(
            columns: 2,
            rows: 2,
            preferredColumn: 0,
            preferredRow: 0,
            (_, _) => false);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryPlan_ReservesEachPlacementForFollowingRequests()
    {
        Dictionary<string, GridCell>? result = GridPlacementPlanner.TryPlan(
            columns: 2,
            rows: 1,
            requests:
            [
                new GridPlacementRequest("first", 0, 0),
                new GridPlacementRequest("second", 0, 0)
            ],
            occupied: [],
            (_, _) => true,
            StringComparer.OrdinalIgnoreCase);

        Assert.IsNotNull(result);
        Assert.AreEqual(new GridCell(0, 0), result["first"]);
        Assert.AreEqual(new GridCell(1, 0), result["second"]);
    }

    [TestMethod]
    public void TryPlan_InsufficientCapacity_ReturnsNullWithoutChangingOccupiedInput()
    {
        var occupied = new HashSet<GridCell> { new(0, 0) };

        Dictionary<string, GridCell>? result = GridPlacementPlanner.TryPlan(
            columns: 2,
            rows: 1,
            requests:
            [
                new GridPlacementRequest("first", 1, 0),
                new GridPlacementRequest("second", 1, 0)
            ],
            occupied,
            (_, _) => true,
            StringComparer.OrdinalIgnoreCase);

        Assert.IsNull(result);
        CollectionAssert.AreEquivalent(
            new[] { new GridCell(0, 0) },
            occupied.ToArray());
    }

    [TestMethod]
    public void RefreshPlan_NewEarlierName_DoesNotTakePersistedLaterItemsCell()
    {
        Dictionary<string, GridCell?> result = GridRefreshPlacementPlanner.Plan(
            columns: 2,
            rows: 1,
            newItemRequests: [new GridPlacementRequest("A-new", 0, 0)],
            persistedCells: [new GridCell(0, 0)],
            (_, _) => true,
            StringComparer.OrdinalIgnoreCase);

        Assert.AreEqual(new GridCell(1, 0), result["A-new"]);
    }

    [TestMethod]
    public void RefreshPlan_FullGrid_ReturnsNullForOverflowWithoutInventingCell()
    {
        Dictionary<string, GridCell?> result = GridRefreshPlacementPlanner.Plan(
            columns: 1,
            rows: 1,
            newItemRequests: [new GridPlacementRequest("new", 0, 0)],
            persistedCells: [new GridCell(0, 0)],
            (_, _) => true,
            StringComparer.OrdinalIgnoreCase);

        Assert.IsNull(result["new"]);
    }
}
