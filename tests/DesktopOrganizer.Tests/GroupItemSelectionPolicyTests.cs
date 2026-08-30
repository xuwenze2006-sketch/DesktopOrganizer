using Microsoft.VisualStudio.TestTools.UnitTesting;

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
}
