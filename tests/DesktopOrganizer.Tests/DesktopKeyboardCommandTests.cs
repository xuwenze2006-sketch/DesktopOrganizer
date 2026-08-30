using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows.Input;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopKeyboardCommandTests
{
    [TestMethod]
    [DataRow(0x41u, true, false, false, false, (int)NativeMethods.DesktopKeyboardCommand.SelectAllItems)]
    [DataRow(0x46u, true, false, false, false, (int)NativeMethods.DesktopKeyboardCommand.OpenDesktopSearch)]
    [DataRow(0x5Au, true, false, false, false, (int)NativeMethods.DesktopKeyboardCommand.UndoFileMove)]
    [DataRow(0x1Bu, false, false, false, false, (int)NativeMethods.DesktopKeyboardCommand.ClearSelection)]
    [DataRow(0x74u, false, false, false, false, (int)NativeMethods.DesktopKeyboardCommand.RefreshDesktop)]
    [DataRow(0x74u, true, false, false, false, -1)]
    [DataRow(0x74u, false, true, false, false, -1)]
    [DataRow(0x74u, false, false, true, false, -1)]
    [DataRow(0x74u, false, false, false, true, -1)]
    [DataRow(0x41u, false, false, false, false, -1)]
    [DataRow(0x41u, true, true, false, false, -1)]
    [DataRow(0x41u, true, false, true, false, -1)]
    [DataRow(0x41u, true, false, false, true, -1)]
    [DataRow(0x46u, false, false, false, false, -1)]
    [DataRow(0x46u, true, true, false, false, -1)]
    [DataRow(0x46u, true, false, true, false, -1)]
    [DataRow(0x46u, true, false, false, true, -1)]
    public void NativeResolver_RequiresExactDesktopShortcut(
        uint virtualKey,
        bool controlDown,
        bool shiftDown,
        bool altDown,
        bool windowsDown,
        int expected)
    {
        NativeMethods.DesktopKeyboardCommand? command =
            NativeMethods.ResolveDesktopKeyboardCommand(
                virtualKey,
                controlDown,
                shiftDown,
                altDown,
                windowsDown);

        Assert.AreEqual(
            expected < 0 ? null : (NativeMethods.DesktopKeyboardCommand)expected,
            command);
    }

    [TestMethod]
    [DataRow(Key.A, ModifierKeys.Control, false, (int)NativeMethods.DesktopKeyboardCommand.SelectAllItems)]
    [DataRow(Key.F, ModifierKeys.Control, false, (int)NativeMethods.DesktopKeyboardCommand.OpenDesktopSearch)]
    [DataRow(Key.Z, ModifierKeys.Control, false, (int)NativeMethods.DesktopKeyboardCommand.UndoFileMove)]
    [DataRow(Key.Escape, ModifierKeys.None, false, (int)NativeMethods.DesktopKeyboardCommand.ClearSelection)]
    [DataRow(Key.F5, ModifierKeys.None, false, (int)NativeMethods.DesktopKeyboardCommand.RefreshDesktop)]
    [DataRow(Key.F5, ModifierKeys.None, true, -1)]
    [DataRow(Key.F5, ModifierKeys.Control, false, -1)]
    [DataRow(Key.F5, ModifierKeys.Shift, false, -1)]
    [DataRow(Key.F5, ModifierKeys.Alt, false, -1)]
    [DataRow(Key.F5, ModifierKeys.Windows, false, -1)]
    [DataRow(Key.F, ModifierKeys.Control, true, -1)]
    [DataRow(Key.F, ModifierKeys.None, false, -1)]
    [DataRow(Key.F, ModifierKeys.Control | ModifierKeys.Shift, false, -1)]
    [DataRow(Key.F, ModifierKeys.Control | ModifierKeys.Alt, false, -1)]
    [DataRow(Key.F, ModifierKeys.Control | ModifierKeys.Windows, false, -1)]
    [DataRow(Key.A, ModifierKeys.None, false, -1)]
    [DataRow(Key.A, ModifierKeys.Control | ModifierKeys.Shift, false, -1)]
    [DataRow(Key.A, ModifierKeys.Control | ModifierKeys.Alt, false, -1)]
    public void WpfResolver_MatchesNativeShortcutSet(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        int expected)
    {
        NativeMethods.DesktopKeyboardCommand? command =
            MainWindow.ResolveDesktopKeyboardCommand(key, modifiers, isRepeat);

        Assert.AreEqual(
            expected < 0 ? null : (NativeMethods.DesktopKeyboardCommand)expected,
            command);
    }

    [TestMethod]
    public void DesktopSearchReservation_AllowsOnlyOneWindowUntilReleased()
    {
        int reservation = 0;

        Assert.IsTrue(MainWindow.TryReserveDesktopSearchDialog(ref reservation));
        Assert.IsFalse(MainWindow.TryReserveDesktopSearchDialog(ref reservation));

        MainWindow.ReleaseDesktopSearchDialog(ref reservation);

        Assert.IsTrue(MainWindow.TryReserveDesktopSearchDialog(ref reservation));
        MainWindow.ReleaseDesktopSearchDialog(ref reservation);
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void DesktopRefreshShortcut_DefersToFocusedFolderPortalList(
        bool folderPortalListHasKeyboardFocus,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            MainWindow.CanRouteDesktopRefreshShortcut(
                folderPortalListHasKeyboardFocus));
    }

    [TestMethod]
    public void ReplaceSelectionWithAllLoadedItems_ReplacesStalePartialSelectionAndIsIdempotent()
    {
        var selected = new HashSet<string>(["A", "stale"], StringComparer.OrdinalIgnoreCase);

        int firstCount = MainWindow.ReplaceSelectionWithAllLoadedItems(
            selected,
            ["a", "B", "b"]);
        int secondCount = MainWindow.ReplaceSelectionWithAllLoadedItems(
            selected,
            ["A", "B"]);

        Assert.AreEqual(2, firstCount);
        Assert.AreEqual(2, secondCount);
        Assert.IsTrue(selected.SetEquals(["A", "B"]));
    }
}
