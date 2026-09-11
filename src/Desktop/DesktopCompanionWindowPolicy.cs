namespace DesktopOrganizer
{
    internal readonly record struct DesktopCompanionScreenBounds(
        int Left,
        int Top,
        int Right,
        int Bottom);

    internal readonly record struct DesktopCompanionPlacementPlan(
        bool ShouldPlace,
        bool RequestActivation,
        bool UseTopmostBand,
        int DelayedRecheckCount);

    internal static class DesktopCompanionWindowPolicy
    {
        private const long WsChild = 0x40000000L;
        private const long WsExToolWindow = 0x00000080L;
        private const long WsExLayered = 0x00080000L;
        private const long WsExNoActivate = 0x08000000L;
        private const int MinimumCompanionSide = 16;
        private const double MaximumDesktopCoverage = 0.5;

        public static bool IsShownWindowCandidate(
            long windowStyle,
            string? className,
            bool isExternalProcess) =>
            isExternalProcess &&
            (windowStyle & WsChild) == 0 &&
            !IsExcludedClassName(className);

        public static bool IsPotentialCompanion(
            long windowStyle,
            long extendedWindowStyle,
            int width,
            int height,
            int desktopWidth,
            int desktopHeight,
            double maximumMonitorCoverage,
            string? className,
            bool isExternalProcess,
            bool isVisible,
            bool isMinimized)
        {
            bool hasCompanionStyle =
                (extendedWindowStyle & WsExLayered) != 0 &&
                ((extendedWindowStyle & WsExToolWindow) != 0 ||
                 (extendedWindowStyle & WsExNoActivate) != 0);
            bool hasBoundedCompanionArea =
                width >= MinimumCompanionSide &&
                height >= MinimumCompanionSide &&
                desktopWidth > 0 &&
                desktopHeight > 0 &&
                double.IsFinite(maximumMonitorCoverage) &&
                maximumMonitorCoverage >= 0 &&
                maximumMonitorCoverage < MaximumDesktopCoverage &&
                (double)width * height <
                (double)desktopWidth * desktopHeight * MaximumDesktopCoverage;

            return isExternalProcess &&
                   isVisible &&
                   !isMinimized &&
                   (windowStyle & WsChild) == 0 &&
                   hasBoundedCompanionArea &&
                   hasCompanionStyle &&
                   !IsExcludedClassName(className);
        }

        public static bool TryCalculateMaximumMonitorCoverage(
            DesktopCompanionScreenBounds candidate,
            IReadOnlyList<DesktopCompanionScreenBounds> monitors,
            out double maximumCoverage)
        {
            bool foundMonitor = false;
            double detectedMaximum = 0;
            foreach (DesktopCompanionScreenBounds monitor in monitors)
            {
                long monitorWidth = Math.Max(0L, (long)monitor.Right - monitor.Left);
                long monitorHeight = Math.Max(0L, (long)monitor.Bottom - monitor.Top);
                if (monitorWidth == 0 || monitorHeight == 0)
                {
                    continue;
                }

                foundMonitor = true;
                long intersectionWidth = Math.Max(
                    0L,
                    Math.Min((long)candidate.Right, monitor.Right) -
                    Math.Max((long)candidate.Left, monitor.Left));
                long intersectionHeight = Math.Max(
                    0L,
                    Math.Min((long)candidate.Bottom, monitor.Bottom) -
                    Math.Max((long)candidate.Top, monitor.Top));
                double coverage =
                    (double)intersectionWidth * intersectionHeight /
                    (monitorWidth * monitorHeight);
                detectedMaximum = Math.Max(detectedMaximum, coverage);
            }

            maximumCoverage = detectedMaximum;
            return foundMonitor;
        }

        public static bool ShouldPlaceAboveOrganizer(
            bool isPotentialCompanion,
            bool organizerIsAboveCandidate,
            bool candidateIsAboveDesktopHost) =>
            isPotentialCompanion &&
            organizerIsAboveCandidate &&
            candidateIsAboveDesktopHost;

        public static DesktopCompanionPlacementPlan CreatePlacementPlan(
            bool isPotentialCompanion,
            bool organizerIsAboveCandidate,
            bool candidateIsAboveDesktopHost) =>
            new(
                ShouldPlaceAboveOrganizer(
                    isPotentialCompanion,
                    organizerIsAboveCandidate,
                    candidateIsAboveDesktopHost),
                RequestActivation: false,
                UseTopmostBand: false,
                DelayedRecheckCount: 1);

        public static bool IsExcludedClassName(string? className)
        {
            if (string.IsNullOrWhiteSpace(className))
            {
                return true;
            }

            return string.Equals(className, "Progman", StringComparison.Ordinal) ||
                   string.Equals(className, "WorkerW", StringComparison.Ordinal) ||
                   string.Equals(className, "Shell_TrayWnd", StringComparison.Ordinal) ||
                   string.Equals(className, "#32768", StringComparison.Ordinal) ||
                   className.Contains("Tooltip", StringComparison.OrdinalIgnoreCase) ||
                   className.Contains("tooltips", StringComparison.OrdinalIgnoreCase) ||
                   className.Contains("PopupWindow", StringComparison.OrdinalIgnoreCase) ||
                   className.Contains("InputSite", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(className, "IME", StringComparison.OrdinalIgnoreCase) ||
                   className.StartsWith("MSCTFIME", StringComparison.OrdinalIgnoreCase) ||
                   className.Contains("Notification", StringComparison.OrdinalIgnoreCase) ||
                   className.Contains("ForegroundStaging", StringComparison.OrdinalIgnoreCase);
        }
    }
}
