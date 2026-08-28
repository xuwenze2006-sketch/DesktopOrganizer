using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class SmartLayoutObstaclePolicyTests
{
    [TestMethod]
    public void Create_ContainsOnlyFolderPortalsAndRecycleBin()
    {
        Rect[] portals =
        [
            new Rect(20, 30, 280, 320),
            new Rect(340, 30, 280, 240)
        ];
        var recycleBin = new Rect(20, 700, 250, 120);

        List<Rect> obstacles = SmartLayoutObstaclePolicy.Create(portals, recycleBin);

        CollectionAssert.AreEqual(
            new[] { portals[0], portals[1], recycleBin },
            obstacles);
    }

    [TestMethod]
    public void Create_WithoutRecycleBin_ReturnsOnlyFolderPortals()
    {
        Rect[] portals = [new Rect(20, 30, 280, 320)];

        List<Rect> obstacles = SmartLayoutObstaclePolicy.Create(portals, recycleBinObstacle: null);

        CollectionAssert.AreEqual(portals, obstacles);
    }
}
