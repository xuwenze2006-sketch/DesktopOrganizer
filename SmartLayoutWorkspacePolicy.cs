using System.Windows;

namespace DesktopOrganizer
{
    internal static class SmartLayoutWorkspacePolicy
    {
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

        private static bool IsFiniteUsableRect(Rect rect) =>
            !rect.IsEmpty &&
            IsFinitePoint(rect.TopLeft) &&
            double.IsFinite(rect.Width) &&
            double.IsFinite(rect.Height) &&
            rect.Width > 0 &&
            rect.Height > 0;

        private static bool IsFinitePoint(Point point) =>
            double.IsFinite(point.X) && double.IsFinite(point.Y);
    }
}
