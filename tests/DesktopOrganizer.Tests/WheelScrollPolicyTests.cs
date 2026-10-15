using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class WheelScrollPolicyTests
{
    [TestMethod]
    [DataRow(-120, 48d)]
    [DataRow(-30, 12d)]
    [DataRow(-1, 0.4d)]
    [DataRow(-240, 96d)]
    [DataRow(60, -24d)]
    [DataRow(0, 0d)]
    public void RawDelta_PreservesDirectionAndFractionalWheelInput(int delta, double expected)
    {
        Assert.AreEqual(expected, WheelScrollPolicy.GetVerticalOffsetChange(delta, 3, 90, 270), 0.0001);
    }

    [TestMethod]
    public void LargeLineCount_DoesNotSkipMoreThanOneViewportPerNotch()
    {
        Assert.AreEqual(200, WheelScrollPolicy.GetVerticalDistance(100, 90, 200));
    }

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
    public void PositiveLineCount_UsesSmallPixelLinesInsteadOfWholeIconRows()
    {
        double distance = WheelScrollPolicy.GetVerticalDistance(
            wheelScrollLines: 3,
            rowHeight: 78,
            viewportHeight: 500);

        Assert.AreEqual(48, distance);
    }

    [TestMethod]
    public void PositiveLineCount_RespectsConfiguredLinesWithoutMultiplyingIconHeight()
    {
        double distance = WheelScrollPolicy.GetVerticalDistance(
            wheelScrollLines: 10,
            rowHeight: 78,
            viewportHeight: 200);

        Assert.AreEqual(160, distance);
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
