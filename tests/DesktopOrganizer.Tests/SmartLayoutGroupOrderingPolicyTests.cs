using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class SmartLayoutGroupOrderingPolicyTests
{
    [TestMethod]
    public void Order_ExpandedFirst_ThenMoreItemsRegardlessOfAreaOrAutoCategory()
    {
        GroupInfo fewManual = CreateGroup("few-manual", 2, width: 1000, height: 1000);
        GroupInfo manyAuto = CreateGroup("many-auto", 27, width: 180, height: 42, isAutoCategory: true);
        manyAuto.IsCollapsed = true;
        GroupInfo middle = CreateGroup("middle", 13, width: 352, height: 300, isAutoCategory: true);

        List<GroupInfo> ordered = SmartLayoutGroupOrderingPolicy.Order(
            [fewManual, manyAuto, middle],
            group => group.Width * (group.IsCollapsed ? 42 : group.Height));

        CollectionAssert.AreEqual(
            new[] { "middle", "few-manual", "many-auto" },
            ordered.Select(group => group.Name).ToArray());
    }

    [TestMethod]
    public void OrderedGroups_PlaceMostPopulatedGroupsInTopRow()
    {
        GroupInfo[] groups =
        [
            CreateGroup("two-a", 2),
            CreateGroup("twenty-seven", 27, isAutoCategory: true),
            CreateGroup("three", 3),
            CreateGroup("thirteen", 13, isAutoCategory: true),
            CreateGroup("ten", 10, isAutoCategory: true),
            CreateGroup("two-b", 2)
        ];
        List<GroupInfo> ordered = SmartLayoutGroupOrderingPolicy.Order(
            groups,
            group => group.Width * group.Height);
        CompactGroupGridItem[] items = ordered
            .Select(group => new CompactGroupGridItem(group.Id, group.Width, group.Height))
            .ToArray();

        Dictionary<string, Point>? plan = CompactGroupGridPlanner.TryPlan(
            items,
            [new Rect(0, 0, 1080, 400)],
            [],
            trackWidth: 352,
            gap: 12,
            maximumColumns: 3,
            preserveInputVerticalOrder: true);

        Assert.IsNotNull(plan);
        foreach (GroupInfo group in groups.Where(group => group.ItemNames.Count >= 10))
        {
            Assert.AreEqual(0, plan[group.Id].Y, $"{group.Name} should be in the top row.");
        }
        foreach (GroupInfo group in groups.Where(group => group.ItemNames.Count < 10))
        {
            Assert.AreEqual(112, plan[group.Id].Y, $"{group.Name} should be below the larger groups.");
        }
    }

    [TestMethod]
    public void OrderedWideGroups_NeverPlaceFewerItemsAboveMoreItems()
    {
        GroupInfo[] groups =
        [
            CreateGroup("twenty-seven", 27, width: 716, isAutoCategory: true),
            CreateGroup("thirteen", 13, width: 716, isAutoCategory: true),
            CreateGroup("ten", 10, width: 352, isAutoCategory: true)
        ];
        List<GroupInfo> ordered = SmartLayoutGroupOrderingPolicy.Order(
            groups,
            group => group.Width * group.Height);
        CompactGroupGridItem[] items = ordered
            .Select(group => new CompactGroupGridItem(group.Id, group.Width, group.Height))
            .ToArray();

        Dictionary<string, Point>? plan = CompactGroupGridPlanner.TryPlan(
            items,
            [new Rect(0, 0, 1080, 400)],
            [],
            trackWidth: 352,
            gap: 12,
            maximumColumns: 3,
            preserveInputVerticalOrder: true);

        Assert.IsNotNull(plan);
        Assert.IsTrue(
            plan[groups[2].Id].Y >= plan[groups[1].Id].Y,
            "A group with fewer items must not be placed above a more populated group.");
    }

    private static GroupInfo CreateGroup(
        string name,
        int itemCount,
        double width = 352,
        double height = 100,
        bool isAutoCategory = false) =>
        new()
        {
            Name = name,
            Width = width,
            Height = height,
            IsAutoCategory = isAutoCategory,
            ItemNames = Enumerable.Range(0, itemCount)
                .Select(index => $"item-{index}")
                .ToList()
        };
}
