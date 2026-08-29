using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class ControlPanelPlacementPolicyTests
{
    [TestMethod]
    public void ClampToWorkArea_EdgeRequest_KeepsFourteenDipInset()
    {
        Point result = ControlPanelPlacementPolicy.ClampToWorkArea(
            new Rect(0, 0, 1920, 1040),
            x: 1800,
            y: 0,
            width: 500,
            height: 80,
            edgeInset: 14);

        Assert.AreEqual(1406, result.X);
        Assert.AreEqual(14, result.Y);
    }

    [TestMethod]
    public void ClampToWorkArea_InteriorRequest_PreservesPosition()
    {
        Point result = ControlPanelPlacementPolicy.ClampToWorkArea(
            new Rect(0, 0, 1920, 1040),
            x: 900,
            y: 120,
            width: 500,
            height: 80,
            edgeInset: 14);

        Assert.AreEqual(900, result.X);
        Assert.AreEqual(120, result.Y);
    }

    [TestMethod]
    public void ClampToWorkArea_OversizedPanel_FallsBackToWorkAreaEdge()
    {
        Point result = ControlPanelPlacementPolicy.ClampToWorkArea(
            new Rect(0, 0, 300, 200),
            x: 50,
            y: 50,
            width: 400,
            height: 240,
            edgeInset: 14);

        Assert.AreEqual(0, result.X);
        Assert.AreEqual(0, result.Y);
    }

    [TestMethod]
    public void ClampToWorkArea_NegativeOrigin_UsesMonitorCoordinates()
    {
        Point result = ControlPanelPlacementPolicy.ClampToWorkArea(
            new Rect(-1280, -200, 1280, 1024),
            x: -1400,
            y: -300,
            width: 500,
            height: 80,
            edgeInset: 14);

        Assert.AreEqual(-1266, result.X);
        Assert.AreEqual(-186, result.Y);
    }

    [TestMethod]
    public void ResizeFromNearestHorizontalEdge_RightAnchoredRoundTripDoesNotDrift()
    {
        Rect workArea = new(0, 0, 1920, 1040);
        Point expanded = ControlPanelPlacementPolicy.ResizeFromNearestHorizontalEdge(
            workArea,
            x: 1582,
            y: 14,
            previousWidth: 324,
            newWidth: 438,
            newHeight: 282,
            edgeInset: 14);
        Point collapsed = ControlPanelPlacementPolicy.ResizeFromNearestHorizontalEdge(
            workArea,
            expanded.X,
            expanded.Y,
            previousWidth: 438,
            newWidth: 324,
            newHeight: 52,
            edgeInset: 14);

        Assert.AreEqual(1468, expanded.X);
        Assert.AreEqual(1582, collapsed.X);
        Assert.AreEqual(14, expanded.Y);
        Assert.AreEqual(14, collapsed.Y);
    }

    [TestMethod]
    public void ResizeFromNearestHorizontalEdge_LeftSideInteriorRoundTripDoesNotDrift()
    {
        Rect workArea = new(0, 0, 1920, 1040);
        Point expanded = ControlPanelPlacementPolicy.ResizeFromNearestHorizontalEdge(
            workArea,
            x: 220,
            y: 100,
            previousWidth: 324,
            newWidth: 438,
            newHeight: 282,
            edgeInset: 14);
        Point collapsed = ControlPanelPlacementPolicy.ResizeFromNearestHorizontalEdge(
            workArea,
            expanded.X,
            expanded.Y,
            previousWidth: 438,
            newWidth: 324,
            newHeight: 52,
            edgeInset: 14);

        Assert.AreEqual(220, expanded.X);
        Assert.AreEqual(220, collapsed.X);
        Assert.AreEqual(100, expanded.Y);
        Assert.AreEqual(100, collapsed.Y);
    }
}
