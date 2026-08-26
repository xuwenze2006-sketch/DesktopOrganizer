using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class QuietStartupVisibilityPolicyTests
{
    [TestMethod]
    public void ShouldHideControlPanel_QuietStartupWithTray_ReturnsTrue()
    {
        Assert.IsTrue(QuietStartupVisibilityPolicy.ShouldHideControlPanel(
            startQuietly: true,
            trayRegistered: true));
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(false, false)]
    [DataRow(true, false)]
    public void ShouldHideControlPanel_WithoutBothRequirements_ReturnsFalse(
        bool startQuietly,
        bool trayRegistered)
    {
        Assert.IsFalse(QuietStartupVisibilityPolicy.ShouldHideControlPanel(
            startQuietly,
            trayRegistered));
    }
}
