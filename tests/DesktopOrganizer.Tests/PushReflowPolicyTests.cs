using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class PushReflowPolicyTests
{
    [TestMethod]
    public void CanConfigure_EditModeWithGridOutsideSafeMode_ReturnsTrue()
    {
        Assert.IsTrue(PushReflowPolicy.CanConfigure(
            safeModeActive: false,
            snapToGrid: true,
            editMode: true));
    }

    [TestMethod]
    [DataRow(true, true, true)]
    [DataRow(false, false, true)]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    public void CanConfigure_WhenAnyRequirementIsMissing_ReturnsFalse(
        bool safeModeActive,
        bool snapToGrid,
        bool editMode)
    {
        Assert.IsFalse(PushReflowPolicy.CanConfigure(
            safeModeActive,
            snapToGrid,
            editMode));
    }

    [TestMethod]
    public void IsActive_WhenPreferenceIsDisabled_ReturnsFalse()
    {
        Assert.IsFalse(PushReflowPolicy.IsActive(
            safeModeActive: false,
            snapToGrid: true,
            editMode: true,
            preferenceEnabled: false));
    }

    [TestMethod]
    public void IsActive_LockedLayoutKeepsPreferenceButReturnsFalse()
    {
        Assert.IsFalse(PushReflowPolicy.IsActive(
            safeModeActive: false,
            snapToGrid: true,
            editMode: false,
            preferenceEnabled: true));
    }
}
