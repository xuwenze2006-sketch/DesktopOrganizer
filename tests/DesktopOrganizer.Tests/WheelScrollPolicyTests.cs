using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class WheelScrollPolicyTests
{
    [TestMethod]
    public void ZeroLines_DisablesWheelScrolling()
    {
        double distance = WheelScrollPolicy.GetVerticalDistance(
            wheelScrollLines: 0,
            rowHeight: 78,
            viewportHeight: 234);

        Assert.AreEqual(0, distance);
    }

    [TestMethod]
    public void PageScroll_UsesViewportHeight()
    {
        double distance = WheelScrollPolicy.GetVerticalDistance(
            WheelScrollPolicy.PageScroll,
            rowHeight: 78,
            viewportHeight: 234);

        Assert.AreEqual(234, distance);
    }

    [TestMethod]
    public void PositiveLineCount_UsesConfiguredRows()
    {
        double distance = WheelScrollPolicy.GetVerticalDistance(
            wheelScrollLines: 3,
            rowHeight: 78,
            viewportHeight: 500);

        Assert.AreEqual(234, distance);
    }

    [TestMethod]
    public void PositiveLineCount_LargerThanViewport_RemainsLineBased()
    {
        double distance = WheelScrollPolicy.GetVerticalDistance(
            wheelScrollLines: 10,
            rowHeight: 78,
            viewportHeight: 200);

        Assert.AreEqual(780, distance);
    }

    [TestMethod]
    public void UnexpectedNegativeValue_FailsClosed()
    {
        double distance = WheelScrollPolicy.GetVerticalDistance(
            wheelScrollLines: -2,
            rowHeight: 78,
            viewportHeight: 234);

        Assert.AreEqual(0, distance);
    }

    [TestMethod]
    public void PageScroll_InvalidViewport_FailsClosed()
    {
        double distance = WheelScrollPolicy.GetVerticalDistance(
            WheelScrollPolicy.PageScroll,
            rowHeight: 78,
            viewportHeight: double.NaN);

        Assert.AreEqual(0, distance);
    }

    [TestMethod]
    public void LineScroll_InvalidRowHeight_FailsClosed()
    {
        double distance = WheelScrollPolicy.GetVerticalDistance(
            wheelScrollLines: 3,
            rowHeight: double.PositiveInfinity,
            viewportHeight: 234);

        Assert.AreEqual(0, distance);
    }
}
