using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class GridCellPlacementTests
{
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
