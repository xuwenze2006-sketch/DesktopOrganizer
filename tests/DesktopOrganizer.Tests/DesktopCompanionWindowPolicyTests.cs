using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopCompanionWindowPolicyTests
{
    private const long WsPopup = unchecked((long)0x80000000);
    private const long WsChild = 0x40000000;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExLayered = 0x00080000;
    private const long WsExNoActivate = 0x08000000;
    private const int DesktopWidth = 1920;
    private const int DesktopHeight = 1080;
    private const long CompanionStyle = WsExLayered | WsExToolWindow | WsExNoActivate;

    [TestMethod]
    public void IsPotentialCompanion_AcceptsSmallLayeredTopLevelToolWindow()
    {
        Assert.IsTrue(IsPotential(
            extendedWindowStyle: WsExLayered | WsExToolWindow,
            width: 64,
            height: 64,
            className: "Chrome_WidgetWin_1"));
    }

    [TestMethod]
    public void IsPotentialCompanion_AcceptsLayeredNoActivateWindowWithoutToolStyle()
    {
        Assert.IsTrue(IsPotential(
            extendedWindowStyle: WsExLayered | WsExNoActivate,
            width: 72,
            height: 96));
    }

    [TestMethod]
    public void IsPotentialCompanion_RequiresLayeredAndCompanionStyleCombination()
    {
        Assert.IsFalse(IsPotential(extendedWindowStyle: WsExLayered));
        Assert.IsFalse(IsPotential(
            extendedWindowStyle: WsExToolWindow | WsExNoActivate));
    }

    [TestMethod]
    public void IsPotentialCompanion_RejectsChildAndOrdinaryApplicationWindows()
    {
        Assert.IsFalse(IsPotential(windowStyle: WsChild));
        Assert.IsFalse(IsPotential(
            extendedWindowStyle: 0,
            width: 1280,
            height: 800,
            className: "NormalApplicationWindow"));
    }

    [TestMethod]
    public void IsPotentialCompanion_RejectsNonInteractiveWindowFacts()
    {
        Assert.IsFalse(IsPotential(isExternalProcess: false));
        Assert.IsFalse(IsPotential(isVisible: false));
        Assert.IsFalse(IsPotential(isMinimized: true));
        Assert.IsFalse(IsPotential(width: 0));
        Assert.IsFalse(IsPotential(height: 0));
        Assert.IsFalse(IsPotential(className: null));
        Assert.IsFalse(IsPotential(desktopWidth: 0));
        Assert.IsFalse(IsPotential(desktopHeight: 0));
        Assert.IsFalse(IsPotential(maximumMonitorCoverage: double.NaN));
    }

    [TestMethod]
    public void IsPotentialCompanion_RejectsTinyAndDesktopCoveringLayeredWindows()
    {
        Assert.IsFalse(IsPotential(width: 1, height: 1));
        Assert.IsFalse(IsPotential(width: 15, height: 96));
        Assert.IsFalse(IsPotential(width: 96, height: 15));
        Assert.IsFalse(IsPotential(width: DesktopWidth, height: DesktopHeight));
        Assert.IsFalse(IsPotential(width: DesktopWidth / 2, height: DesktopHeight));
        Assert.IsFalse(IsPotential(width: 1400, height: 900));
        Assert.IsFalse(IsPotential(
            width: 1920,
            height: 1080,
            desktopWidth: 5760,
            desktopHeight: 1080,
            maximumMonitorCoverage: 1));
    }

    [TestMethod]
    public void IsShownWindowCandidate_DefersFinalStyleAndGeometryChecks()
    {
        Assert.IsTrue(DesktopCompanionWindowPolicy.IsShownWindowCandidate(
            WsPopup,
            "RuntimePetWindow",
            isExternalProcess: true));

        Assert.IsFalse(DesktopCompanionWindowPolicy.IsShownWindowCandidate(
            WsChild,
            "RuntimePetWindow",
            isExternalProcess: true));
        Assert.IsFalse(DesktopCompanionWindowPolicy.IsShownWindowCandidate(
            WsPopup,
            "RuntimePetWindow",
            isExternalProcess: false));
        Assert.IsFalse(DesktopCompanionWindowPolicy.IsShownWindowCandidate(
            WsPopup,
            "DesktopTooltipWindow",
            isExternalProcess: true));
    }

    [TestMethod]
    public void MonitorCoverage_FullSingleScreenInThreeScreenDesktopIsOne()
    {
        DesktopCompanionScreenBounds[] monitors =
        [
            new(0, 0, 1920, 1080),
            new(1920, 0, 3840, 1080),
            new(3840, 0, 5760, 1080)
        ];

        bool result = DesktopCompanionWindowPolicy.TryCalculateMaximumMonitorCoverage(
            new DesktopCompanionScreenBounds(1920, 0, 3840, 1080),
            monitors,
            out double coverage);

        Assert.IsTrue(result);
        Assert.AreEqual(1d, coverage, 0.000001);
    }

    [TestMethod]
    public void MonitorCoverage_UsesLargestIntersectionAcrossScreens()
    {
        DesktopCompanionScreenBounds[] monitors =
        [
            new(0, 0, 1920, 1080),
            new(1920, 0, 3840, 1080)
        ];

        bool result = DesktopCompanionWindowPolicy.TryCalculateMaximumMonitorCoverage(
            new DesktopCompanionScreenBounds(1440, 0, 2400, 1080),
            monitors,
            out double coverage);

        Assert.IsTrue(result);
        Assert.AreEqual(0.25d, coverage, 0.000001);
    }

    [TestMethod]
    public void MonitorCoverage_HandlesNegativeScreenCoordinates()
    {
        DesktopCompanionScreenBounds[] monitors =
        [
            new(-1920, 0, 0, 1080),
            new(0, 0, 1920, 1080)
        ];

        bool result = DesktopCompanionWindowPolicy.TryCalculateMaximumMonitorCoverage(
            new DesktopCompanionScreenBounds(-1920, 0, 0, 1080),
            monitors,
            out double coverage);

        Assert.IsTrue(result);
        Assert.AreEqual(1d, coverage, 0.000001);
    }

    [TestMethod]
    public void MonitorCoverage_HalfScreenBoundaryIsRejectedAndSlightlyLessIsAccepted()
    {
        DesktopCompanionScreenBounds[] monitors = [new(0, 0, 1920, 1080)];

        Assert.IsTrue(DesktopCompanionWindowPolicy.TryCalculateMaximumMonitorCoverage(
            new DesktopCompanionScreenBounds(0, 0, 960, 1080),
            monitors,
            out double halfCoverage));
        Assert.AreEqual(0.5d, halfCoverage, 0.000001);
        Assert.IsFalse(IsPotential(
            width: 960,
            height: 1080,
            maximumMonitorCoverage: halfCoverage));

        Assert.IsTrue(DesktopCompanionWindowPolicy.TryCalculateMaximumMonitorCoverage(
            new DesktopCompanionScreenBounds(0, 0, 959, 1080),
            monitors,
            out double lowerCoverage));
        Assert.IsTrue(lowerCoverage < 0.5d);
        Assert.IsTrue(IsPotential(
            width: 959,
            height: 1080,
            maximumMonitorCoverage: lowerCoverage));
    }

    [TestMethod]
    public void MonitorCoverage_ReportsFailureWhenNoValidMonitorExists()
    {
        Assert.IsFalse(DesktopCompanionWindowPolicy.TryCalculateMaximumMonitorCoverage(
            new DesktopCompanionScreenBounds(0, 0, 96, 120),
            Array.Empty<DesktopCompanionScreenBounds>(),
            out double emptyCoverage));
        Assert.AreEqual(0d, emptyCoverage);

        Assert.IsFalse(DesktopCompanionWindowPolicy.TryCalculateMaximumMonitorCoverage(
            new DesktopCompanionScreenBounds(0, 0, 96, 120),
            [new DesktopCompanionScreenBounds(0, 0, 0, 1080)],
            out double invalidCoverage));
        Assert.AreEqual(0d, invalidCoverage);
    }

    [TestMethod]
    [DataRow("Progman")]
    [DataRow("WorkerW")]
    [DataRow("Shell_TrayWnd")]
    [DataRow("#32768")]
    [DataRow("DesktopTooltipWindow")]
    [DataRow("PopupWindowSiteBridge")]
    [DataRow("InputSiteWindow")]
    [DataRow("IME")]
    [DataRow("MSCTFIME UI")]
    [DataRow("NotificationWindow")]
    [DataRow("ForegroundStaging")]
    public void IsPotentialCompanion_RejectsShellAndTransientClasses(string className)
    {
        Assert.IsFalse(IsPotential(className: className));
    }

    [TestMethod]
    public void ShouldPlaceAboveOrganizer_RequiresExactDesktopBandOrdering()
    {
        Assert.IsTrue(DesktopCompanionWindowPolicy.ShouldPlaceAboveOrganizer(
            isPotentialCompanion: true,
            organizerIsAboveCandidate: true,
            candidateIsAboveDesktopHost: true));

        Assert.IsFalse(DesktopCompanionWindowPolicy.ShouldPlaceAboveOrganizer(
            isPotentialCompanion: true,
            organizerIsAboveCandidate: false,
            candidateIsAboveDesktopHost: true));

        Assert.IsFalse(DesktopCompanionWindowPolicy.ShouldPlaceAboveOrganizer(
            isPotentialCompanion: true,
            organizerIsAboveCandidate: true,
            candidateIsAboveDesktopHost: false));

        Assert.IsFalse(DesktopCompanionWindowPolicy.ShouldPlaceAboveOrganizer(
            isPotentialCompanion: false,
            organizerIsAboveCandidate: true,
            candidateIsAboveDesktopHost: true));
    }

    [TestMethod]
    public void CreatePlacementPlan_NeverActivatesOrUsesTopmostAndRechecksOnce()
    {
        DesktopCompanionPlacementPlan plan =
            DesktopCompanionWindowPolicy.CreatePlacementPlan(
                isPotentialCompanion: true,
                organizerIsAboveCandidate: true,
                candidateIsAboveDesktopHost: true);

        Assert.IsTrue(plan.ShouldPlace);
        Assert.IsFalse(plan.RequestActivation);
        Assert.IsFalse(plan.UseTopmostBand);
        Assert.AreEqual(1, plan.DelayedRecheckCount);
    }

    [TestMethod]
    public void IsPotentialCompanion_DoesNotTreatRuntimeClassNameAsIme()
    {
        Assert.IsTrue(IsPotential(className: "RuntimePetWindow"));
    }

    private static bool IsPotential(
        long windowStyle = WsPopup,
        long extendedWindowStyle = CompanionStyle,
        int width = 96,
        int height = 120,
        int desktopWidth = DesktopWidth,
        int desktopHeight = DesktopHeight,
        double maximumMonitorCoverage = 0.01,
        string? className = "PetWindow",
        bool isExternalProcess = true,
        bool isVisible = true,
        bool isMinimized = false) =>
        DesktopCompanionWindowPolicy.IsPotentialCompanion(
            windowStyle,
            extendedWindowStyle,
            width,
            height,
            desktopWidth,
            desktopHeight,
            maximumMonitorCoverage,
            className,
            isExternalProcess,
            isVisible,
            isMinimized);
}
