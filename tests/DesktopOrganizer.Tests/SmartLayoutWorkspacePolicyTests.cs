using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class SmartLayoutWorkspacePolicyTests
{
    [TestMethod]
    public void TryCreateWorkspace_PrimaryUsesUniformWorkAreaMargin()
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

}
