using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopGeometryTests
{
    [TestMethod]
    public void Constructor_CalculatesVirtualCanvasBounds()
    {
        var geometry = new DesktopGeometry(
        [
            Monitor("DISPLAY1", new Rect(0, 0, 1920, 1080), primary: true),
            Monitor("DISPLAY2", new Rect(1920, 0, 1280, 1024))
        ]);

        Assert.AreEqual(3200, geometry.CanvasBounds.Width, 0.001);
        Assert.AreEqual(1080, geometry.CanvasBounds.Height, 0.001);
        Assert.AreEqual("DISPLAY1", geometry.PrimaryMonitor.DeviceName);
    }

    [TestMethod]
    public void FindMonitorForPoint_ReturnsContainingMonitor()
    {
        var geometry = CreateTwoMonitorGeometry();

        DesktopMonitorRegion result = geometry.FindMonitorForPoint(new Point(2500, 400));

        Assert.AreEqual("DISPLAY2", result.DeviceName);
    }

    [TestMethod]
    public void FindMonitorForPoint_InGap_ReturnsNearestMonitor()
    {
        var geometry = new DesktopGeometry(
        [
            Monitor("LEFT", new Rect(0, 0, 100, 100), primary: true),
            Monitor("RIGHT", new Rect(200, 0, 100, 100))
        ]);

        DesktopMonitorRegion result = geometry.FindMonitorForPoint(new Point(160, 50));

        Assert.AreEqual("RIGHT", result.DeviceName);
    }

    [TestMethod]
    public void FindMonitorForRect_ReturnsMonitorWithLargestWorkAreaOverlap()
    {
        var geometry = CreateTwoMonitorGeometry();
        var item = new Rect(1800, 100, 500, 300);

        DesktopMonitorRegion result = geometry.FindMonitorForRect(item);

        Assert.AreEqual("DISPLAY2", result.DeviceName);
    }

    [TestMethod]
    public void HasMixedDpi_ReflectsPerMonitorDpi()
    {
        var geometry = new DesktopGeometry(
        [
            Monitor("DISPLAY1", new Rect(0, 0, 1920, 1080), primary: true, dpi: 96),
            Monitor("DISPLAY2", new Rect(1920, 0, 1706, 960), dpi: 144)
        ]);

        Assert.IsTrue(geometry.HasMixedDpi);
    }

    [TestMethod]
    public void IsEquivalentTo_IgnoresMonitorOrderAndSmallRoundingDifferences()
    {
        var first = new DesktopGeometry(
        [
            Monitor("DISPLAY1", new Rect(0, 0, 1920, 1080), primary: true),
            Monitor("DISPLAY2", new Rect(1920, 0, 1280, 1024))
        ]);
        var second = new DesktopGeometry(
        [
            Monitor("display2", new Rect(1920.4, 0.2, 1279.7, 1024.2)),
            Monitor("display1", new Rect(0.2, 0.1, 1919.8, 1079.7), primary: true)
        ]);

        Assert.IsTrue(first.IsEquivalentTo(second));
    }

    [TestMethod]
    public void IsEquivalentTo_ReturnsFalseWhenDpiChanges()
    {
        var first = CreateTwoMonitorGeometry();
        var second = new DesktopGeometry(
        [
            Monitor("DISPLAY1", new Rect(0, 0, 1920, 1080), primary: true),
            Monitor("DISPLAY2", new Rect(1920, 0, 1280, 1024), dpi: 120)
        ]);

        Assert.IsFalse(first.IsEquivalentTo(second));
    }

    private static DesktopGeometry CreateTwoMonitorGeometry() =>
        new(
        [
            Monitor("DISPLAY1", new Rect(0, 0, 1920, 1080), primary: true),
            Monitor("DISPLAY2", new Rect(1920, 0, 1280, 1024))
        ]);

    private static DesktopMonitorRegion Monitor(
        string name,
        Rect bounds,
        bool primary = false,
        uint dpi = 96)
    {
        return new DesktopMonitorRegion
        {
            DeviceName = name,
            Bounds = bounds,
            WorkArea = bounds,
            IsPrimary = primary,
            DpiX = dpi,
            DpiY = dpi
        };
    }
}
