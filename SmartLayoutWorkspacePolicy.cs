using System.Windows;

namespace DesktopOrganizer
{
    internal static class SmartLayoutWorkspacePolicy
    {
        public const double CompactPanelHeaderWidth = 306;
        public const double CompactPanelHeaderHeight = 34;

        public static Size CompactPanelHeaderSize =>
            new(CompactPanelHeaderWidth, CompactPanelHeaderHeight);

        public static Rect? TryCreateWorkspace(
            Rect workArea,
            bool isPrimary,
            bool reserveTemporaryWorkspace,
            double margin,
            double minimumWidth,
            double minimumHeight)
        {
            if (!IsFiniteUsableRect(workArea) ||
                !double.IsFinite(margin) || margin < 0 ||
                !double.IsFinite(minimumWidth) || minimumWidth <= 0 ||
                !double.IsFinite(minimumHeight) || minimumHeight <= 0)
            {
                return null;
            }

            double left = workArea.Left + margin;
            double top = workArea.Top + margin;
            double right = workArea.Right - margin;
            double bottom = workArea.Bottom - margin;

            if (reserveTemporaryWorkspace && isPrimary)
            {
                bottom = Math.Min(
                    bottom,
                    Math.Max(top + 260, workArea.Top + workArea.Height * 0.66));
            }

            double width = right - left;
            double height = bottom - top;
            return width >= minimumWidth && height >= minimumHeight
                ? new Rect(left, top, width, height)
                : null;
        }

        public static Rect? TryCreateCompactPanelObstacle(
            Point position,
            Size compactHeaderSize,
            Thickness padding,
            Thickness borderThickness)
        {
            if (!IsFinitePoint(position) ||
                !IsFinitePositiveSize(compactHeaderSize) ||
                !IsFiniteNonNegativeThickness(padding) ||
                !IsFiniteNonNegativeThickness(borderThickness))
            {
                return null;
            }

            double width = compactHeaderSize.Width +
                           padding.Left + padding.Right +
                           borderThickness.Left + borderThickness.Right;
            double height = compactHeaderSize.Height +
                            padding.Top + padding.Bottom +
                            borderThickness.Top + borderThickness.Bottom;
            return double.IsFinite(width) && double.IsFinite(height) && width > 0 && height > 0
                ? new Rect(position, new Size(width, height))
                : null;
        }

        private static bool IsFiniteUsableRect(Rect rect) =>
            !rect.IsEmpty &&
            IsFinitePoint(rect.TopLeft) &&
            double.IsFinite(rect.Width) &&
            double.IsFinite(rect.Height) &&
            rect.Width > 0 &&
            rect.Height > 0;

        private static bool IsFinitePoint(Point point) =>
            double.IsFinite(point.X) && double.IsFinite(point.Y);

        private static bool IsFinitePositiveSize(Size size) =>
            !size.IsEmpty &&
            double.IsFinite(size.Width) &&
            double.IsFinite(size.Height) &&
            size.Width > 0 &&
            size.Height > 0;

        private static bool IsFiniteNonNegativeThickness(Thickness thickness) =>
            double.IsFinite(thickness.Left) && thickness.Left >= 0 &&
            double.IsFinite(thickness.Top) && thickness.Top >= 0 &&
            double.IsFinite(thickness.Right) && thickness.Right >= 0 &&
            double.IsFinite(thickness.Bottom) && thickness.Bottom >= 0;
    }
}
