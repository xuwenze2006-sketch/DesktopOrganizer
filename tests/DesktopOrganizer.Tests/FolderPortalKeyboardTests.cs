using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows.Input;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class FolderPortalKeyboardTests
{
    [TestMethod]
    public void FindRestoredSelectionIndex_FollowsSameFullPathAcrossReorder()
    {
        Assert.AreEqual(
            1,
            MainWindow.FindFolderPortalRestoredSelectionIndex(
                [@"C:\Root\Second.txt", @"C:\Root\First.txt"],
                @"c:\root\FIRST.txt"));
    }

    [TestMethod]
    public void FindRestoredSelectionIndex_DoesNotSelectMissingOrSameNamedEntry()
    {
        Assert.AreEqual(
            -1,
            MainWindow.FindFolderPortalRestoredSelectionIndex(
                [@"C:\Other\Selected.txt", @"C:\Root\Replacement.txt"],
                @"C:\Root\Selected.txt"));
        Assert.AreEqual(
            -1,
            MainWindow.FindFolderPortalRestoredSelectionIndex(
                [@"C:\Root\First.txt"],
                null));
        Assert.AreEqual(
            -1,
            MainWindow.FindFolderPortalRestoredSelectionIndex(
                Array.Empty<string>(),
                @"C:\Root\First.txt"));
    }

    [TestMethod]
    public void FindRestoredSelectionIndex_PrefersDirectoryLeftByUpNavigation()
    {
        Assert.AreEqual(
            1,
            MainWindow.FindFolderPortalRestoredSelectionIndex(
                [@"C:\Root\Other", @"C:\Root\Child"],
                @"C:\Root\Child\OldSelection.txt",
                @"c:\root\CHILD"));
        Assert.AreEqual(
            -1,
            MainWindow.FindFolderPortalRestoredSelectionIndex(
                [@"C:\Root\Child\OldSelection.txt"],
                @"C:\Root\Child\OldSelection.txt",
                @"C:\Root\Missing"));
    }

    [TestMethod]
    public void GetSelectionPathAfterNavigateUp_ReturnsDirectoryBeingLeft()
    {
        Assert.AreEqual(
            @"C:\Root\Child\Nested",
            MainWindow.GetFolderPortalSelectionPathAfterNavigateUp(
                @"C:\Root",
                @"Child\Nested"));
        Assert.IsNull(
            MainWindow.GetFolderPortalSelectionPathAfterNavigateUp(
                @"C:\Root",
                string.Empty));
        Assert.IsNull(
            MainWindow.GetFolderPortalSelectionPathAfterNavigateUp(
                @"C:\Root",
                "   "));
    }

    [TestMethod]
    public void GetSelectionPathAfterNavigateRoot_ReturnsTopLevelBranch()
    {
        string? nestedBranch = MainWindow.GetFolderPortalSelectionPathAfterNavigateRoot(
            @"C:\Root",
            @"Projects\2026\Report");

        Assert.AreEqual(@"C:\Root\Projects", nestedBranch);
        Assert.AreEqual(
            @"C:\Root\Projects",
            MainWindow.GetFolderPortalSelectionPathAfterNavigateRoot(
                @"C:\Root",
                "Projects/2026"));
        Assert.AreEqual(
            @"C:\Root\Projects",
            MainWindow.GetFolderPortalSelectionPathAfterNavigateRoot(
                @"C:\Root",
                "Projects"));
        Assert.IsNull(
            MainWindow.GetFolderPortalSelectionPathAfterNavigateRoot(
                @"C:\Root",
                string.Empty));
        Assert.AreEqual(
            1,
            MainWindow.FindFolderPortalRestoredSelectionIndex(
                [@"C:\Root\Other", @"C:\Root\Projects"],
                selectedEntryPath: null,
                nestedBranch));
    }

    [TestMethod]
    [DataRow(@"C:\Root\Child", true, false, -1, true)]
    [DataRow(@"C:\Root\Child", true, true, -1, true)]
    [DataRow(@"C:\Root\Child", false, false, 1, true)]
    [DataRow(@"C:\Root\Child", false, false, -1, false)]
    [DataRow(@"C:\Root\Child", false, true, 1, false)]
    [DataRow(null, false, false, 1, false)]
    [DataRow("", true, false, 1, false)]
    public void ShouldRetainPreferredSelection_UntilReadyListCanApplyIt(
        string? preferredSelectionPath,
        bool isLoading,
        bool hasReplacementList,
        int restoredSelectionIndex,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            MainWindow.ShouldRetainFolderPortalPreferredSelection(
                preferredSelectionPath,
                isLoading,
                hasReplacementList,
                restoredSelectionIndex));
    }

    [TestMethod]
    [DataRow(@"C:\Root\Child\Nested", @"Child", @"Child", null, true)]
    [DataRow(@"C:\Root\Child\Nested", @"Child", @"CHILD", null, true)]
    [DataRow(@"C:\Root\Child\Nested", @"Child", @"Child\Nested", null, false)]
    [DataRow(@"C:\Root\Child\Nested", @"Child", @"Child", @"C:\Root\Other", false)]
    [DataRow(null, @"Child", @"Child", null, false)]
    [DataRow(@"C:\Root\Child\Nested", null, @"Child", null, false)]
    public void ShouldPreservePendingSelection_RequiresSameTargetRead(
        string? pendingSelectionPath,
        string? pendingSelectionRelativePath,
        string requestedRelativePath,
        string? preferredSelectionPath,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            MainWindow.ShouldPreserveFolderPortalPendingSelection(
                pendingSelectionPath,
                pendingSelectionRelativePath,
                requestedRelativePath,
                preferredSelectionPath));
    }

    [TestMethod]
    public void CanRetainPendingSelectionAfterFailedRead_RequiresValidatedStaleEntry()
    {
        Assert.IsTrue(
            MainWindow.CanRetainFolderPortalPendingSelectionAfterFailedRead(
                @"C:\Root\Child",
                "",
                "",
                [@"C:\Root\Other", @"c:\root\CHILD"]));
        Assert.IsFalse(
            MainWindow.CanRetainFolderPortalPendingSelectionAfterFailedRead(
                @"C:\Root\Child",
                @"Child",
                @"Child\Nested",
                [@"C:\Root\Child"]));
        Assert.IsFalse(
            MainWindow.CanRetainFolderPortalPendingSelectionAfterFailedRead(
                @"C:\Root\Child",
                "",
                "",
                [@"C:\Root\Other"]));
        Assert.IsFalse(
            MainWindow.CanRetainFolderPortalPendingSelectionAfterFailedRead(
                null,
                "",
                "",
                [@"C:\Root\Child"]));
    }

    [TestMethod]
    [DataRow(true, false, true, true, true)]
    [DataRow(false, false, true, true, false)]
    [DataRow(true, true, true, true, false)]
    [DataRow(true, false, false, true, false)]
    [DataRow(true, false, true, false, false)]
    public void ShouldRestoreListFocus_RequiresFocusedCurrentLiveReplacement(
        bool previousListHadFocus,
        bool isClosing,
        bool isCurrentState,
        bool hasReplacementList,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            MainWindow.ShouldRestoreFolderPortalListFocus(
                previousListHadFocus,
                isClosing,
                isCurrentState,
                hasReplacementList));
    }

    [TestMethod]
    [DataRow(Key.System, Key.Up, Key.Up)]
    [DataRow(Key.System, Key.Home, Key.Home)]
    [DataRow(Key.System, Key.Back, Key.Back)]
    [DataRow(Key.System, Key.Left, Key.Left)]
    [DataRow(Key.Up, Key.None, Key.Up)]
    [DataRow(Key.Back, Key.None, Key.Back)]
    public void ResolveFolderPortalKeyboardKey_UsesSystemKeyOnlyForAltEvents(
        Key key,
        Key systemKey,
        Key expected)
    {
        Assert.AreEqual(
            expected,
            MainWindow.ResolveFolderPortalKeyboardKey(key, systemKey));
    }

    [TestMethod]
    [DataRow(Key.Up, ModifierKeys.Alt, false, true, true)]
    [DataRow(Key.Up, ModifierKeys.Alt, true, true, false)]
    [DataRow(Key.Up, ModifierKeys.Alt, false, false, false)]
    [DataRow(Key.Up, ModifierKeys.None, false, true, false)]
    [DataRow(Key.Up, ModifierKeys.Control, false, true, false)]
    [DataRow(Key.Up, ModifierKeys.Alt | ModifierKeys.Control, false, true, false)]
    [DataRow(Key.Left, ModifierKeys.Alt, false, true, false)]
    [DataRow(Key.Down, ModifierKeys.Alt, false, true, false)]
    [DataRow(Key.Back, ModifierKeys.None, false, true, true)]
    [DataRow(Key.Back, ModifierKeys.None, true, true, false)]
    [DataRow(Key.Back, ModifierKeys.None, false, false, false)]
    [DataRow(Key.Back, ModifierKeys.Control, false, true, false)]
    [DataRow(Key.Back, ModifierKeys.Shift, false, true, false)]
    [DataRow(Key.Back, ModifierKeys.Alt, false, true, false)]
    [DataRow(Key.Back, ModifierKeys.Windows, false, true, false)]
    [DataRow(Key.Back, ModifierKeys.Control | ModifierKeys.Shift, false, true, false)]
    public void ShouldNavigateFolderPortalUpFromKeyboard_RequiresInitialSupportedShortcutBelowRoot(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool canNavigateUp,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            MainWindow.ShouldNavigateFolderPortalUpFromKeyboard(
                key,
                modifiers,
                isRepeat,
                canNavigateUp));
    }

    [TestMethod]
    [DataRow(Key.Home, ModifierKeys.Alt, false, true)]
    [DataRow(Key.Home, ModifierKeys.Alt, true, false)]
    [DataRow(Key.Home, ModifierKeys.None, false, false)]
    [DataRow(Key.Home, ModifierKeys.Control, false, false)]
    [DataRow(Key.Home, ModifierKeys.Shift, false, false)]
    [DataRow(Key.Home, ModifierKeys.Alt | ModifierKeys.Control, false, false)]
    [DataRow(Key.Home, ModifierKeys.Alt | ModifierKeys.Shift, false, false)]
    [DataRow(Key.Home, ModifierKeys.Alt | ModifierKeys.Windows, false, false)]
    [DataRow(Key.Up, ModifierKeys.Alt, false, false)]
    [DataRow(Key.Left, ModifierKeys.Alt, false, false)]
    public void ShouldNavigateFolderPortalRootFromKeyboard_RequiresExactInitialAltHome(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            MainWindow.ShouldNavigateFolderPortalRootFromKeyboard(
                key,
                modifiers,
                isRepeat));
    }

    [TestMethod]
    [DataRow(Key.F5, ModifierKeys.None, false, true)]
    [DataRow(Key.F5, ModifierKeys.None, true, false)]
    [DataRow(Key.F5, ModifierKeys.Control, false, false)]
    [DataRow(Key.F5, ModifierKeys.Shift, false, false)]
    [DataRow(Key.F5, ModifierKeys.Alt, false, false)]
    [DataRow(Key.F4, ModifierKeys.None, false, false)]
    [DataRow(Key.Home, ModifierKeys.None, false, false)]
    public void ShouldRefreshFolderPortalFromKeyboard_RequiresPlainInitialF5(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            MainWindow.ShouldRefreshFolderPortalFromKeyboard(
                key,
                modifiers,
                isRepeat));
    }

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

    [TestMethod]
    [DataRow(MouseButton.Left, true)]
    [DataRow(MouseButton.Right, false)]
    [DataRow(MouseButton.Middle, false)]
    [DataRow(MouseButton.XButton1, false)]
    [DataRow(MouseButton.XButton2, false)]
    public void ShouldOpenFolderPortalEntryFromDoubleClick_RequiresLeftButton(
        MouseButton changedButton,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            MainWindow.ShouldOpenFolderPortalEntryFromDoubleClick(changedButton));
    }

    [TestMethod]
    [DataRow(Key.Enter, ModifierKeys.Shift, false, true, true)]
    [DataRow(Key.Enter, ModifierKeys.Shift, true, true, false)]
    [DataRow(Key.Enter, ModifierKeys.Shift, false, false, false)]
    [DataRow(Key.Enter, ModifierKeys.None, false, true, false)]
    [DataRow(Key.Enter, ModifierKeys.Control, false, true, false)]
    [DataRow(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift, false, true, false)]
    [DataRow(Key.Enter, ModifierKeys.Alt, false, true, false)]
    [DataRow(Key.Space, ModifierKeys.Shift, false, true, false)]
    public void ShouldRevealFolderPortalEntryFromKeyboard_RequiresExactInitialShiftEnterOnEntry(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool hasEntry,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            MainWindow.ShouldRevealFolderPortalEntryFromKeyboard(
                key,
                modifiers,
                isRepeat,
                hasEntry));
    }

    [TestMethod]
    [DataRow(Key.C, ModifierKeys.Control, false, true, true)]
    [DataRow(Key.C, ModifierKeys.Control, true, true, false)]
    [DataRow(Key.C, ModifierKeys.Control, false, false, false)]
    [DataRow(Key.C, ModifierKeys.None, false, true, false)]
    [DataRow(Key.C, ModifierKeys.Control | ModifierKeys.Shift, false, true, false)]
    [DataRow(Key.C, ModifierKeys.Alt, false, true, false)]
    [DataRow(Key.Enter, ModifierKeys.Control, false, true, false)]
    public void ShouldCopyFolderPortalPathFromKeyboard_RequiresExactInitialControlCOnEntry(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat,
        bool hasEntry,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            MainWindow.ShouldCopyFolderPortalPathFromKeyboard(
                key,
                modifiers,
                isRepeat,
                hasEntry));
    }
}
