using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows.Input;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class GroupItemSelectionPolicyTests
{
    [TestMethod]
    public void CreatePlanAndApply_PartialSelectionCompletesGroupAndPreservesOutsideSelection()
    {
        var selected = new HashSet<string>(["b.txt", "Outside"], StringComparer.OrdinalIgnoreCase);

        GroupItemSelectionPlan plan = GroupItemSelectionPolicy.CreatePlan(
            ["A.txt", "B.txt", "a.TXT", "Missing", ""],
            ["A.txt", "B.txt", "Outside"],
            selected);
        int changedCount = GroupItemSelectionPolicy.Apply(selected, plan);

        Assert.IsTrue(plan.Select);
        Assert.AreEqual(2, plan.ItemCount);
        Assert.AreEqual(1, changedCount);
        Assert.IsTrue(selected.SetEquals(["A.txt", "B.txt", "Outside"]));
    }

    [TestMethod]
    public void CreatePlanAndApply_FullySelectedGroupRemovesOnlyGroupItems()
    {
        var selected = new HashSet<string>(["A", "B", "Outside"], StringComparer.OrdinalIgnoreCase);

        GroupItemSelectionPlan plan = GroupItemSelectionPolicy.CreatePlan(
            ["A", "B"],
            ["A", "B", "Outside"],
            selected);
        int changedCount = GroupItemSelectionPolicy.Apply(selected, plan);

        Assert.IsFalse(plan.Select);
        Assert.AreEqual(2, changedCount);
        CollectionAssert.AreEqual(new[] { "Outside" }, selected.ToArray());
    }

    [TestMethod]
    public void CreatePlanAndApply_MissingOrBlankMembersDoNothing()
    {
        var selected = new HashSet<string>(["Outside"], StringComparer.OrdinalIgnoreCase);

        GroupItemSelectionPlan plan = GroupItemSelectionPolicy.CreatePlan(
            ["Missing", "", "   "],
            ["Outside"],
            selected);
        int changedCount = GroupItemSelectionPolicy.Apply(selected, plan);

        Assert.IsFalse(plan.Select);
        Assert.AreEqual(0, plan.ItemCount);
        Assert.AreEqual(0, changedCount);
        CollectionAssert.AreEqual(new[] { "Outside" }, selected.ToArray());
    }

    [TestMethod]
    public void CreateRangePlan_UsesDisplayOrderForForwardAndReverseRanges()
    {
        string[] displayOrder = ["D", "B", "A", "C", "E"];
        var forwardSelection = new HashSet<string>(["b"], StringComparer.OrdinalIgnoreCase);
        var reverseSelection = new HashSet<string>(["c"], StringComparer.OrdinalIgnoreCase);

        GroupRangeSelectionPlan forward = GroupRangeSelectionPolicy.CreatePlan(
            "group",
            displayOrder,
            forwardSelection,
            new GroupRangeSelectionAnchor("GROUP", "B"),
            "c");
        GroupRangeSelectionPlan reverse = GroupRangeSelectionPolicy.CreatePlan(
            "group",
            displayOrder,
            reverseSelection,
            new GroupRangeSelectionAnchor("group", "C"),
            "b");

        Assert.IsTrue(forward.UsedAnchor);
        Assert.IsTrue(reverse.UsedAnchor);
        CollectionAssert.AreEqual(new[] { "B", "A", "C" }, forward.ItemNames.ToArray());
        CollectionAssert.AreEqual(new[] { "B", "A", "C" }, reverse.ItemNames.ToArray());
    }

    [TestMethod]
    public void CreateRangePlan_DeduplicatesNamesAndKeepsFirstDisplayPosition()
    {
        var selected = new HashSet<string>(["a"], StringComparer.OrdinalIgnoreCase);

        GroupRangeSelectionPlan plan = GroupRangeSelectionPolicy.CreatePlan(
            "group",
            ["A", "b", "a", "C", "B"],
            selected,
            new GroupRangeSelectionAnchor("group", "a"),
            "c");

        Assert.IsTrue(plan.UsedAnchor);
        CollectionAssert.AreEqual(new[] { "A", "b", "C" }, plan.ItemNames.ToArray());
    }

    [TestMethod]
    public void CreateRangePlan_InvalidAnchorFallsBackToEndpoint()
    {
        string[] displayOrder = ["A", "B", "C"];
        var selected = new HashSet<string>(["A"], StringComparer.OrdinalIgnoreCase);
        GroupRangeSelectionAnchor?[] invalidAnchors =
        [
            null,
            new GroupRangeSelectionAnchor("other-group", "A"),
            new GroupRangeSelectionAnchor("group", "B"),
            new GroupRangeSelectionAnchor("group", "Missing")
        ];

        foreach (GroupRangeSelectionAnchor? anchor in invalidAnchors)
        {
            GroupRangeSelectionPlan plan = GroupRangeSelectionPolicy.CreatePlan(
                "group",
                displayOrder,
                selected,
                anchor,
                "C");

            Assert.IsFalse(plan.UsedAnchor);
            CollectionAssert.AreEqual(new[] { "C" }, plan.ItemNames.ToArray());
        }

        var selectedMissingAnchor = new HashSet<string>(["Missing"], StringComparer.OrdinalIgnoreCase);
        GroupRangeSelectionPlan missingFromDisplayOrder = GroupRangeSelectionPolicy.CreatePlan(
            "group",
            displayOrder,
            selectedMissingAnchor,
            new GroupRangeSelectionAnchor("group", "Missing"),
            "C");

        Assert.IsFalse(missingFromDisplayOrder.UsedAnchor);
        CollectionAssert.AreEqual(
            new[] { "C" },
            missingFromDisplayOrder.ItemNames.ToArray());
    }

    [TestMethod]
    public void CreateRangePlan_MissingEndpointReturnsEmptyPlan()
    {
        var selected = new HashSet<string>(["A"], StringComparer.OrdinalIgnoreCase);

        GroupRangeSelectionPlan plan = GroupRangeSelectionPolicy.CreatePlan(
            "group",
            ["A", "B"],
            selected,
            new GroupRangeSelectionAnchor("group", "A"),
            "Missing");

        Assert.IsFalse(plan.UsedAnchor);
        Assert.AreEqual(0, plan.ItemCount);
    }

    [TestMethod]
    public void ApplyRangePlan_PreservesExistingSelectionAndIsIdempotentIgnoringCase()
    {
        var selected = new HashSet<string>(["b", "Outside"], StringComparer.Ordinal);
        var plan = new GroupRangeSelectionPlan(["B", "A", "C"], UsedAnchor: true);

        int firstChangedCount = GroupRangeSelectionPolicy.Apply(selected, plan);
        int secondChangedCount = GroupRangeSelectionPolicy.Apply(selected, plan);

        Assert.AreEqual(2, firstChangedCount);
        Assert.AreEqual(0, secondChangedCount);
        Assert.AreEqual(4, selected.Count);
        Assert.IsTrue(selected.Contains("b"));
        Assert.IsTrue(selected.Contains("A"));
        Assert.IsTrue(selected.Contains("C"));
        Assert.IsTrue(selected.Contains("Outside"));
    }

    [TestMethod]
    [DataRow(ModifierKeys.Shift, 1, true)]
    [DataRow(ModifierKeys.None, 1, false)]
    [DataRow(ModifierKeys.Control | ModifierKeys.Shift, 1, false)]
    [DataRow(ModifierKeys.Alt | ModifierKeys.Shift, 1, false)]
    [DataRow(ModifierKeys.Shift, 2, false)]
    public void ShouldBeginGroupedRangeSelection_RequiresPlainSingleShiftClick(
        ModifierKeys modifiers,
        int clickCount,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            MainWindow.ShouldBeginGroupedRangeSelection(modifiers, clickCount));
    }
}
