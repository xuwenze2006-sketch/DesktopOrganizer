using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopSelectionRefreshTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ReconcileSelectionForNewDesktopItems_HandlesSelectionAndAnchorIndependently(
        bool replacementWasSelected,
        bool replacementWasAnchor)
    {
        var selectedItemNames = new HashSet<string>(
            ["keep.txt"],
            StringComparer.OrdinalIgnoreCase);
        if (replacementWasSelected)
        {
            selectedItemNames.Add("SAME.TXT");
        }

        var originalAnchor = new GroupRangeSelectionAnchor(
            "group",
            replacementWasAnchor ? "Same.Txt" : "keep.txt");
        var newItemNames = new HashSet<string>(
            ["same.txt"],
            StringComparer.OrdinalIgnoreCase);

        GroupRangeSelectionAnchor? result = MainWindow.ReconcileSelectionForNewDesktopItems(
            selectedItemNames,
            originalAnchor,
            newItemNames);

        Assert.IsTrue(selectedItemNames.SetEquals(["keep.txt"]));
        if (replacementWasAnchor)
        {
            Assert.IsNull(result);
        }
        else
        {
            Assert.AreSame(originalAnchor, result);
        }
    }
}
