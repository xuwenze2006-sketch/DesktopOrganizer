using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Runtime.CompilerServices;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopCompanionNativeContractTests
{
    [TestMethod]
    public void WindowEvents_UseChildStyleInsteadOfRejectingOwnedTopLevelWindows()
    {
        string source = ReadProjectFile("NativeMethods.WindowEvents.cs");

        StringAssert.Contains(source, "(style & WS_CHILD) != 0");
        Assert.IsFalse(
            source.Contains("GetParent(root) != IntPtr.Zero", StringComparison.Ordinal),
            "Owned top-level popup windows must not be rejected as child windows.");
    }

    [TestMethod]
    public void CompanionPlacement_UsesAtomicBatchWithoutForegroundOrTopmost()
    {
        string source = ReadProjectFile("NativeMethods.DesktopLayer.cs");
        const string methodStart = "public static int TryPlaceDesktopCompanionsAboveOrganizer";
        const string methodEnd = "private static IntPtr NormalizeRootWindow";
        int start = source.IndexOf(methodStart, StringComparison.Ordinal);
        int end = source.IndexOf(methodEnd, start, StringComparison.Ordinal);

        Assert.IsTrue(start >= 0 && end > start, "Cannot locate companion placement method.");
        string method = source[start..end];
        StringAssert.Contains(method, "GetWindow(organizerWindow, GW_HWNDPREV)");
        StringAssert.Contains(method, "BeginDeferWindowPos");
        StringAssert.Contains(method, "DeferWindowPos");
        StringAssert.Contains(method, "EndDeferWindowPos");
        StringAssert.Contains(method, "SWP_NOACTIVATE");
        StringAssert.Contains(method, "insertAfter = root");
        Assert.IsFalse(method.Contains("SWP_ASYNCWINDOWPOS", StringComparison.Ordinal));
        Assert.IsFalse(method.Contains("HWND_TOPMOST", StringComparison.Ordinal));
        Assert.IsFalse(method.Contains("SetForegroundWindow", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CompanionShowHandling_UsesOneOrderedTwoPhaseBatch()
    {
        string source = ReadProjectFile("MainWindow.DesktopHost.cs");

        StringAssert.Contains(
            source,
            "ProcessDesktopCompanionLayerCorrectionsAfterShownAsync");
        StringAssert.Contains(
            source,
            "ProcessPendingDesktopCompanionLayerCorrections(clear: false)");
        StringAssert.Contains(
            source,
            "ProcessPendingDesktopCompanionLayerCorrections(clear: true)");
        StringAssert.Contains(source, "await Task.Run(");
        StringAssert.Contains(source, ".OrderBy(candidate => candidate.Depth)");
        Assert.IsFalse(
            source.Contains(
                "RecheckDesktopCompanionLayerAfterShownAsync",
                StringComparison.Ordinal),
            "Each SHOW must not start a separate per-window delayed task.");
    }

    [TestMethod]
    public void CompanionSizing_UsesPerMonitorCoverageBeforeVirtualDesktopFallback()
    {
        string source = ReadProjectFile("NativeMethods.DesktopLayer.cs");

        StringAssert.Contains(
            source,
            "TryGetMaximumMonitorCoverage(rect, out maximumMonitorCoverage)");
        StringAssert.Contains(source, "EnumDisplayMonitors(");
        StringAssert.Contains(
            source,
            "(double)desktopWidth * desktopHeight");
    }

    private static string ReadProjectFile(
        string fileName,
        [CallerFilePath] string sourceFilePath = "")
    {
        string testDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Cannot resolve the test source directory.");
        string projectRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", ".."));
        return File.ReadAllText(Path.Combine(projectRoot, fileName));
    }
}
