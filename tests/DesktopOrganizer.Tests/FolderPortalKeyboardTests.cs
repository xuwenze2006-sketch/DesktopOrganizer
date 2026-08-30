using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows.Input;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class FolderPortalKeyboardTests
{
    [TestMethod]
    [DataRow(Key.Enter, ModifierKeys.None, false, true, true)]
    [DataRow(Key.Enter, ModifierKeys.None, true, true, false)]
    [DataRow(Key.Enter, ModifierKeys.None, false, false, false)]
    [DataRow(Key.Enter, ModifierKeys.Control, false, true, false)]
    [DataRow(Key.Enter, ModifierKeys.Shift, false, true, false)]
    [DataRow(Key.Enter, ModifierKeys.Alt, false, true, false)]
    [DataRow(Key.Space, ModifierKeys.None, false, true, false)]
    public void ShouldOpenFolderPortalEntryFromKeyboard_RequiresPlainInitialEnterOnEntry(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool hasEntry,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            MainWindow.ShouldOpenFolderPortalEntryFromKeyboard(
                key,
                modifiers,
                isRepeat,
                hasEntry));
    }
}
