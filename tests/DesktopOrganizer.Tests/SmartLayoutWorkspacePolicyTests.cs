using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class SmartLayoutWorkspacePolicyTests
{
    [TestMethod]
    public void TryCreateWorkspace_PrimaryUsesNormalTopMarginInsteadOfFullWidthPanelStrip()
    {
        Rect? workspace = SmartLayoutWorkspacePolicy.TryCreateWorkspace(
            new Rect(0, 0, 1920, 1040),
            isPrimary: true,
            reserveTemporaryWorkspace: false,
            margin: 16,
            minimumWidth: 180,
            minimumHeight: 42);

        Assert.IsNotNull(workspace);
        Assert.AreEqual(new Rect(16, 16, 1888, 1008), workspace.Value);
    }

    [TestMethod]
    public void TryCreateWorkspace_ReservedPrimaryKeepsBottomTemporaryArea()
    {
        Rect? workspace = SmartLayoutWorkspacePolicy.TryCreateWorkspace(
            new Rect(0, 0, 1920, 1040),
            isPrimary: true,
            reserveTemporaryWorkspace: true,
            margin: 16,
            minimumWidth: 180,
            minimumHeight: 42);

        Assert.IsNotNull(workspace);
        Assert.AreEqual(16, workspace.Value.Top);
        Assert.AreEqual(1040 * 0.66, workspace.Value.Bottom, 0.001);
    }

    [TestMethod]
    public void TryCreateCompactPanelObstacle_AddsOnlyCompactHeaderChrome()
    {
        Rect? obstacle = SmartLayoutWorkspacePolicy.TryCreateCompactPanelObstacle(
            new Point(1500, 14),
            SmartLayoutWorkspacePolicy.CompactPanelHeaderSize,
            new Thickness(8),
            new Thickness(1));

        Assert.IsNotNull(obstacle);
        Assert.AreEqual(new Rect(1500, 14, 324, 52), obstacle.Value);
    }

    [TestMethod]
    public void CompactPanelObstacle_BlocksOnlyIntersectingTopTrack()
    {
        CompactGroupGridItem[] items =
        [
            new("left", 352, 100),
            new("middle", 352, 100),
            new("right", 352, 100)
        ];
        var workspace = new Rect(16, 16, 1080, 220);
        Rect panelObstacle = SmartLayoutWorkspacePolicy.TryCreateCompactPanelObstacle(
            new Point(744, 14),
            SmartLayoutWorkspacePolicy.CompactPanelHeaderSize,
            new Thickness(8),
            new Thickness(1))!.Value;

        Dictionary<string, Point>? plan = CompactGroupGridPlanner.TryPlan(
            items,
            [workspace],
            [panelObstacle],
            trackWidth: 352,
            gap: 12,
            maximumColumns: 3);

        Assert.IsNotNull(plan);
        Assert.AreEqual(new Point(16, 16), plan["left"]);
        Assert.AreEqual(new Point(380, 16), plan["middle"]);
        Assert.AreEqual(new Point(744, 78), plan["right"]);
    }
}
