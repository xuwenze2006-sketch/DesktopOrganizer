using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopShellMenuSelectionTests
{
    [TestMethod]
    public void ShouldClearItemSelection_RequiresOpenEventLiveWindowAndSelection()
    {
        Assert.IsTrue(MainWindow.ShouldClearItemSelectionForDesktopShellMenu(
            NativeMethods.ExternalWindowEventKind.DesktopShellMenuOpened,
            isClosing: false,
            selectedItemCount: 1));
        Assert.IsTrue(MainWindow.ShouldClearItemSelectionForDesktopShellMenu(
            NativeMethods.ExternalWindowEventKind.DesktopShellMenuOpened,
            isClosing: false,
            selectedItemCount: 3));

        Assert.IsFalse(MainWindow.ShouldClearItemSelectionForDesktopShellMenu(
            NativeMethods.ExternalWindowEventKind.DesktopShellMenuOpened,
            isClosing: true,
            selectedItemCount: 1));
        Assert.IsFalse(MainWindow.ShouldClearItemSelectionForDesktopShellMenu(
            NativeMethods.ExternalWindowEventKind.DesktopShellMenuOpened,
            isClosing: false,
            selectedItemCount: 0));
        Assert.IsFalse(MainWindow.ShouldClearItemSelectionForDesktopShellMenu(
            NativeMethods.ExternalWindowEventKind.DesktopShellMenuClosed,
            isClosing: false,
            selectedItemCount: 1));
        Assert.IsFalse(MainWindow.ShouldClearItemSelectionForDesktopShellMenu(
            NativeMethods.ExternalWindowEventKind.Foreground,
            isClosing: false,
            selectedItemCount: 1));
        Assert.IsFalse(MainWindow.ShouldClearItemSelectionForDesktopShellMenu(
            NativeMethods.ExternalWindowEventKind.Shown,
            isClosing: false,
            selectedItemCount: 1));
    }
}
