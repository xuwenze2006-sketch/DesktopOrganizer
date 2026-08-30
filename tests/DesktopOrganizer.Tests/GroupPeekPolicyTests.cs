using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class GroupPeekPolicyTests
{
    [TestMethod]
    public void CanArm_RequiresCollapsedNonEmptyGroupInLockedActiveLayout()
    {
        Assert.IsTrue(GroupPeekPolicy.CanArm(
            isCollapsed: true,
            itemCount: 3,
            isLayoutEditing: false,
            isOrganizerPaused: false));
        Assert.IsFalse(GroupPeekPolicy.CanArm(true, 0, false, false));
        Assert.IsFalse(GroupPeekPolicy.CanArm(false, 3, false, false));
        Assert.IsFalse(GroupPeekPolicy.CanArm(true, 3, true, false));
        Assert.IsFalse(GroupPeekPolicy.CanArm(true, 3, false, true));
    }

    [TestMethod]
    public void GetPresentation_CollapsedWithoutPeek_ShowsHeaderOnly()
    {
        GroupPeekPresentation presentation = GroupPeekPolicy.GetPresentation(
            isCollapsed: true,
            isPeekActive: false,
            expandedHeight: 240,
            headerHeight: 38,
            groupTop: 100,
            workAreaTop: 0,
            workAreaBottom: 900);

        Assert.AreEqual(38, presentation.VisualHeight);
        Assert.IsFalse(presentation.BodyVisible);
    }

    [TestMethod]
    public void GetPresentation_CollapsedPeek_UsesAvailableExpandedHeightOnly()
    {
        GroupPeekPresentation fullPreview = GroupPeekPolicy.GetPresentation(
            isCollapsed: true,
            isPeekActive: true,
            expandedHeight: 240,
            headerHeight: 38,
            groupTop: 100,
            workAreaTop: 0,
            workAreaBottom: 900);
        GroupPeekPresentation cappedPreview = GroupPeekPolicy.GetPresentation(
            isCollapsed: true,
            isPeekActive: true,
            expandedHeight: 240,
            headerHeight: 38,
            groupTop: 80,
            workAreaTop: 0,
            workAreaBottom: 200);

        Assert.AreEqual(240, fullPreview.VisualHeight);
        Assert.AreEqual(120, cappedPreview.VisualHeight);
        Assert.IsTrue(fullPreview.BodyVisible);
        Assert.IsTrue(cappedPreview.BodyVisible);
        Assert.IsFalse(fullPreview.OpensAbove);
        Assert.IsFalse(cappedPreview.OpensAbove);
    }

    [TestMethod]
    public void GetPresentation_NearWorkAreaBottom_OpensAboveAndKeepsFullHeight()
    {
        GroupPeekPresentation presentation = GroupPeekPolicy.GetPresentation(
            isCollapsed: true,
            isPeekActive: true,
            expandedHeight: 240,
            headerHeight: 38,
            groupTop: 760,
            workAreaTop: 0,
            workAreaBottom: 900);

        Assert.AreEqual(240, presentation.VisualHeight);
        Assert.IsTrue(presentation.BodyVisible);
        Assert.IsTrue(presentation.OpensAbove);
    }

    [TestMethod]
    public void GetPresentation_ExpandedGroup_IgnoresPeekStateAndWorkAreaCap()
    {
        GroupPeekPresentation withoutPeek = GroupPeekPolicy.GetPresentation(
            isCollapsed: false,
            isPeekActive: false,
            expandedHeight: 240,
            headerHeight: 38,
            groupTop: 860,
            workAreaTop: 0,
            workAreaBottom: 900);
        GroupPeekPresentation withPeek = GroupPeekPolicy.GetPresentation(
            isCollapsed: false,
            isPeekActive: true,
            expandedHeight: 240,
            headerHeight: 38,
            groupTop: 860,
            workAreaTop: 0,
            workAreaBottom: 900);

        Assert.AreEqual(withoutPeek, withPeek);
        Assert.AreEqual(240, withoutPeek.VisualHeight);
        Assert.IsTrue(withoutPeek.BodyVisible);
    }
}
