namespace DesktopOrganizer
{
    internal static class ControlPanelPlacementPolicy
    {
        public static Point ClampToWorkArea(
            Rect workArea,
            double x,
            double y,
            double width,
            double height,
            double edgeInset)
        {
            width = Math.Max(0, width);
            height = Math.Max(0, height);
            edgeInset = Math.Max(0, edgeInset);

            double horizontalInset = width + edgeInset * 2 <= workArea.Width
                ? edgeInset
                : 0;
            double verticalInset = height + edgeInset * 2 <= workArea.Height
                ? edgeInset
                : 0;
            double minX = workArea.Left + horizontalInset;
            double minY = workArea.Top + verticalInset;
            double maxX = Math.Max(minX, workArea.Right - horizontalInset - width);
            double maxY = Math.Max(minY, workArea.Bottom - verticalInset - height);
            return new Point(
                Math.Clamp(x, minX, maxX),
                Math.Clamp(y, minY, maxY));
        }

        public static Point ResizeFromNearestHorizontalEdge(
            Rect workArea,
            double x,
            double y,
            double previousWidth,
            double newWidth,
            double newHeight,
            double edgeInset)
        {
            previousWidth = Math.Max(0, previousWidth);
            newWidth = Math.Max(0, newWidth);
            edgeInset = Math.Max(0, edgeInset);

            double previousInset = previousWidth + edgeInset * 2 <= workArea.Width
                ? edgeInset
                : 0;
            double leftAnchor = workArea.Left + previousInset;
            double rightAnchor = workArea.Right - previousInset;
            double distanceToLeft = Math.Abs(x - leftAnchor);
            double distanceToRight = Math.Abs(rightAnchor - (x + previousWidth));
            double anchoredX = distanceToRight <= distanceToLeft
                ? x + previousWidth - newWidth
                : x;

            return ClampToWorkArea(
                workArea,
                anchoredX,
                y,
                newWidth,
                newHeight,
                edgeInset);
        }
    }
}
