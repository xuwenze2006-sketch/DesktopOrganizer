using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class GroupLayoutCollisionDetectorTests
{
    [TestMethod]
    public void HasCollision_SeparatedGroups_ReturnsFalse()
    {
        Rect[] groups =
        [
            new Rect(0, 0, 280, 200),
            new Rect(292, 0, 280, 200)
        ];

        Assert.IsFalse(GroupLayoutCollisionDetector.HasCollision(groups));
    }

    [TestMethod]
    public void HasCollision_ExpandedGroupsOverlap_ReturnsTrue()
    {
        Rect[] groups =
        [
            new Rect(0, 0, 352, 200),
            new Rect(292, 0, 352, 200)
        ];

        Assert.IsTrue(GroupLayoutCollisionDetector.HasCollision(groups));
    }

    [TestMethod]
    public void HasCollision_RecycleBinObstacleOverlap_ReturnsTrue()
    {
        Rect[] groups = [new Rect(0, 0, 280, 200)];
        var recycleBin = new Rect(250, 160, 90, 90);

        Assert.IsTrue(GroupLayoutCollisionDetector.HasCollision(groups, recycleBin));
    }
}
