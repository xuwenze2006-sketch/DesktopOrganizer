using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows.Input;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopKeyboardCommandTests
{
    [TestMethod]
    [DataRow(0x41u, true, false, false, false, (int)NativeMethods.DesktopKeyboardCommand.SelectAllItems)]
    [DataRow(0x5Au, true, false, false, false, (int)NativeMethods.DesktopKeyboardCommand.UndoFileMove)]
    [DataRow(0x1Bu, false, false, false, false, (int)NativeMethods.DesktopKeyboardCommand.ClearSelection)]
    [DataRow(0x41u, false, false, false, false, -1)]
    [DataRow(0x41u, true, true, false, false, -1)]
    [DataRow(0x41u, true, false, true, false, -1)]
    [DataRow(0x41u, true, false, false, true, -1)]
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
    [DataRow(Key.A, ModifierKeys.Control, (int)NativeMethods.DesktopKeyboardCommand.SelectAllItems)]
    [DataRow(Key.Z, ModifierKeys.Control, (int)NativeMethods.DesktopKeyboardCommand.UndoFileMove)]
    [DataRow(Key.Escape, ModifierKeys.None, (int)NativeMethods.DesktopKeyboardCommand.ClearSelection)]
    [DataRow(Key.A, ModifierKeys.None, -1)]
    [DataRow(Key.A, ModifierKeys.Control | ModifierKeys.Shift, -1)]
    [DataRow(Key.A, ModifierKeys.Control | ModifierKeys.Alt, -1)]
    public void WpfResolver_MatchesNativeShortcutSet(
        Key key,
        ModifierKeys modifiers,
        int expected)
    {
        NativeMethods.DesktopKeyboardCommand? command =
            MainWindow.ResolveDesktopKeyboardCommand(key, modifiers);

        Assert.AreEqual(
            expected < 0 ? null : (NativeMethods.DesktopKeyboardCommand)expected,
            command);
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
