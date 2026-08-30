using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class GroupItemDropPolicyTests
{
    [TestMethod]
    [DataRow(49.9, 0, 0, 0)]
    [DataRow(50.0, 0, 0, 1)]
    [DataRow(100.0, 0, 0, 1)]
    [DataRow(149.9, 0, 0, 1)]
    [DataRow(150.0, 0, 0, 2)]
    [DataRow(50.0, 80, 0, 4)]
    [DataRow(50.0, 0, 80, 4)]
    public void CalculateInsertionBoundary_UsesCellHalvesAndVerticalOffset(
        double x,
        double y,
        double verticalOffset,
        int expected)
    {
        int actual = GroupItemDropPolicy.CalculateInsertionBoundary(
            x,
            y,
            columnCount: 3,
            slotWidth: 100,
            rowHeight: 80,
            verticalOffset,
            itemCount: 8);

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void CalculateInsertionBoundary_ClampsOutsideGridAndPartialLastRow()
    {
        Assert.AreEqual(
            0,
            GroupItemDropPolicy.CalculateInsertionBoundary(
                -50, -20, 3, 100, 80, 0, itemCount: 5));
        Assert.AreEqual(
            3,
            GroupItemDropPolicy.CalculateInsertionBoundary(
                500, 20, 3, 100, 80, 0, itemCount: 5));
        Assert.AreEqual(
            5,
            GroupItemDropPolicy.CalculateInsertionBoundary(
                500, 100, 3, 100, 80, 0, itemCount: 5));
        Assert.AreEqual(
            5,
            GroupItemDropPolicy.CalculateInsertionBoundary(
                20, 500, 3, 100, 80, 0, itemCount: 5));
        Assert.AreEqual(
            0,
            GroupItemDropPolicy.CalculateInsertionBoundary(
                20, 20, 3, 100, 80, 0, itemCount: 0));
    }

    [TestMethod]
    public void Apply_CrossGroup_UsesVisibleTargetOrderAndMovesManualMarker()
    {
        GroupInfo source = Group("source", GroupSortMode.Name, "One", "Move.txt", "Two");
        source.ManuallyAssignedItemNames = ["MOVE.TXT"];
        GroupInfo target = Group("target", GroupSortMode.Name, "Zulu.txt", "Alpha.txt");
        var groups = new List<GroupInfo> { source, target };

        GroupItemDropResult result = GroupItemDropPolicy.Apply(
            groups,
            "Move.txt",
            source.Id,
            target.Id,
            targetBoundary: 1,
            targetVisibleOrder: ["Alpha.txt", "Zulu.txt"]);

        Assert.IsTrue(result.Applied);
        Assert.IsTrue(result.Changed);
        Assert.IsTrue(result.MovedBetweenGroups);
        Assert.AreEqual(1, result.TargetIndex);
        CollectionAssert.AreEqual(new[] { "One", "Two" }, source.ItemNames);
        Assert.AreEqual(0, source.ManuallyAssignedItemNames.Count);
        CollectionAssert.AreEqual(
            new[] { "Alpha.txt", "Move.txt", "Zulu.txt" },
            target.ItemNames);
        CollectionAssert.AreEqual(new[] { "Move.txt" }, target.ManuallyAssignedItemNames);
        Assert.AreEqual(GroupSortMode.Custom, target.SortMode);
    }

    [TestMethod]
    public void Apply_CrossAutoGroups_PreventsImmediateAutomaticRollback()
    {
        GroupInfo source = Group("documents", GroupSortMode.Name, "Move.txt");
        source.IsAutoCategory = true;
        source.AutoCategoryKey = "documents";
        GroupInfo target = Group("folders", GroupSortMode.Name);
        target.IsAutoCategory = true;
        target.AutoCategoryKey = "folders";
        var groups = new List<GroupInfo> { source, target };

        GroupItemDropResult drop = GroupItemDropPolicy.Apply(
            groups,
            "Move.txt",
            source.Id,
            target.Id,
            targetBoundary: 0,
            targetVisibleOrder: []);
        var categories = new Dictionary<string, DesktopCategoryDefinition>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Move.txt"] = new DesktopCategoryDefinition("documents", "文档", 10)
        };

        IReadOnlyList<AutoCategoryMembershipMove> rollbackMoves =
            AutoCategoryMembershipPlanner.Plan(
                groups,
                categories,
                new HashSet<string>(["Move.txt"], StringComparer.OrdinalIgnoreCase),
                paused: false);

        Assert.IsTrue(drop.Applied);
        Assert.AreEqual(0, rollbackMoves.Count);
        CollectionAssert.AreEqual(new[] { "Move.txt" }, target.ItemNames);
        CollectionAssert.AreEqual(new[] { "Move.txt" }, target.ManuallyAssignedItemNames);
    }

    [TestMethod]
    public void Apply_SameGroupMovingEarlier_RebuildsFromCurrentVisibleOrder()
    {
        GroupInfo target = Group("target", GroupSortMode.Name, "z.txt", "a.txt", "m.txt");

        GroupItemDropResult result = GroupItemDropPolicy.Apply(
            new List<GroupInfo> { target },
            "z.txt",
            target.Id,
            target.Id,
            targetBoundary: 1,
            targetVisibleOrder: ["a.txt", "m.txt", "z.txt"]);

        Assert.IsTrue(result.Applied);
        Assert.IsFalse(result.MovedBetweenGroups);
        Assert.AreEqual(1, result.TargetIndex);
        CollectionAssert.AreEqual(new[] { "a.txt", "z.txt", "m.txt" }, target.ItemNames);
        Assert.AreEqual(GroupSortMode.Custom, target.SortMode);
        Assert.AreEqual(0, target.ManuallyAssignedItemNames.Count);
    }

    [TestMethod]
    public void Apply_SameGroupMovingLater_AdjustsBoundaryAfterRemovingSource()
    {
        GroupInfo target = Group("target", GroupSortMode.Custom, "A", "B", "C", "D");

        GroupItemDropResult result = GroupItemDropPolicy.Apply(
            new List<GroupInfo> { target },
            "B",
            target.Id,
            target.Id,
            targetBoundary: 4,
            targetVisibleOrder: ["A", "B", "C", "D"]);

        Assert.AreEqual(3, result.TargetIndex);
        CollectionAssert.AreEqual(new[] { "A", "C", "D", "B" }, target.ItemNames);
    }

    [TestMethod]
    public void Apply_CurrentBoundaryWithExistingMarker_IsNoOp()
    {
        GroupInfo target = Group("target", GroupSortMode.Custom, "A", "B", "C");
        target.ManuallyAssignedItemNames = ["b"];

        GroupItemDropResult result = GroupItemDropPolicy.Apply(
            new List<GroupInfo> { target },
            "B",
            target.Id,
            target.Id,
            targetBoundary: 2,
            targetVisibleOrder: ["A", "B", "C"]);

        Assert.IsTrue(result.Applied);
        Assert.IsFalse(result.Changed);
        Assert.AreEqual(1, result.TargetIndex);
        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, target.ItemNames);
        CollectionAssert.AreEqual(new[] { "b" }, target.ManuallyAssignedItemNames);
    }

    [TestMethod]
    public void Apply_RemovesCaseInsensitiveDuplicatesAndOldMarkersFromEveryGroup()
    {
        GroupInfo source = Group("source", GroupSortMode.Custom, "move", "Keep");
        source.ManuallyAssignedItemNames = ["MOVE"];
        GroupInfo duplicate = Group("duplicate", GroupSortMode.Custom, "Other", "MOVE");
        duplicate.ManuallyAssignedItemNames = ["move"];
        GroupInfo target = Group("target", GroupSortMode.Custom, "Before", "Move", "After");
        target.ManuallyAssignedItemNames = ["MOVE"];
        var groups = new List<GroupInfo> { source, duplicate, target };

        GroupItemDropResult result = GroupItemDropPolicy.Apply(
            groups,
            "Move",
            source.Id,
            target.Id,
            targetBoundary: 0,
            targetVisibleOrder: ["Before", "Move", "After", "AFTER"]);

        Assert.IsTrue(result.Applied);
        CollectionAssert.AreEqual(new[] { "Keep" }, source.ItemNames);
        CollectionAssert.AreEqual(new[] { "Other" }, duplicate.ItemNames);
        CollectionAssert.AreEqual(new[] { "Move", "Before", "After" }, target.ItemNames);
        Assert.AreEqual(
            1,
            groups.Sum(group => group.ItemNames.Count(name =>
                name.Equals("move", StringComparison.OrdinalIgnoreCase))));
        Assert.AreEqual(
            1,
            groups.Sum(group => group.ManuallyAssignedItemNames.Count(name =>
                name.Equals("move", StringComparison.OrdinalIgnoreCase))));
    }

    [TestMethod]
    public void Apply_InvalidTargetHasNoSideEffects()
    {
        GroupInfo source = Group("source", GroupSortMode.Name, "Move", "Keep");
        source.ManuallyAssignedItemNames = ["Move"];
        string[] originalItems = source.ItemNames.ToArray();
        string[] originalMarkers = source.ManuallyAssignedItemNames.ToArray();

        GroupItemDropResult result = GroupItemDropPolicy.Apply(
            new List<GroupInfo> { source },
            "Move",
            source.Id,
            "missing",
            targetBoundary: 0,
            targetVisibleOrder: []);

        Assert.IsFalse(result.Applied);
        Assert.IsFalse(result.Changed);
        CollectionAssert.AreEqual(originalItems, source.ItemNames);
        CollectionAssert.AreEqual(originalMarkers, source.ManuallyAssignedItemNames);
        Assert.AreEqual(GroupSortMode.Name, source.SortMode);
    }

    private static GroupInfo Group(string id, GroupSortMode sortMode, params string[] names) =>
        new()
        {
            Id = id,
            SortMode = sortMode,
            ItemNames = names.ToList(),
            ManuallyAssignedItemNames = new List<string>()
        };
}
