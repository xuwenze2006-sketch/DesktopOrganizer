namespace DesktopOrganizer
{
    internal static class GroupPeekPolicy
    {
        public static bool CanArm(
            bool isCollapsed,
            int itemCount,
            bool isLayoutEditing,
            bool isOrganizerPaused) =>
            isCollapsed && itemCount > 0 && !isLayoutEditing && !isOrganizerPaused;

        public static GroupPeekPresentation GetPresentation(
            bool isCollapsed,
            bool isPeekActive,
            double expandedHeight,
            double headerHeight,
            double groupTop,
            double workAreaTop,
            double workAreaBottom)
        {
            double normalizedHeaderHeight = Math.Max(0, headerHeight);
            double normalizedExpandedHeight = Math.Max(normalizedHeaderHeight, expandedHeight);
            if (!isCollapsed)
            {
                return new GroupPeekPresentation(normalizedExpandedHeight, BodyVisible: true);
            }

            if (!isPeekActive)
            {
                return new GroupPeekPresentation(normalizedHeaderHeight, BodyVisible: false);
            }

            double availableBelow = Math.Max(
                normalizedHeaderHeight,
                workAreaBottom - groupTop);
            double availableAbove = Math.Max(
                normalizedHeaderHeight,
                groupTop - workAreaTop + normalizedHeaderHeight);
            bool opensAbove = availableBelow < normalizedExpandedHeight &&
                availableAbove > availableBelow;
            return new GroupPeekPresentation(
                Math.Min(
                    normalizedExpandedHeight,
                    opensAbove ? availableAbove : availableBelow),
                BodyVisible: true,
                OpensAbove: opensAbove);
        }
    }

    internal readonly record struct GroupPeekPresentation(
        double VisualHeight,
        bool BodyVisible,
        bool OpensAbove = false);
}
