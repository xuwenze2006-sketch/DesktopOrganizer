namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private static bool IsLightDesktopEntry(GroupInfo group) =>
            group.DesktopRole == DesktopZoneRole.Other && !group.IsSizeLocked;

        private static double GetGroupDisplayWidth(GroupInfo group) =>
            IsLightDesktopEntry(group) ? LightDesktopLayoutPolicy.EntryWidth : group.Width;

        private bool TryArrangeLightDesktop()
        {
            var roles = _appLayout.Groups.ToDictionary(group => group,
                LightDesktopLayoutPolicy.ResolveRole);
            GroupInfo? shortcuts = roles.FirstOrDefault(pair => pair.Value == DesktopZoneRole.Shortcuts).Key;
            GroupInfo? projects = roles.FirstOrDefault(pair => pair.Value == DesktopZoneRole.Projects).Key;
            if (shortcuts == null || projects == null) return false;
            GroupInfo? research = roles.FirstOrDefault(pair => pair.Value == DesktopZoneRole.Research).Key;
            var entries = roles.Where(pair => pair.Value == DesktopZoneRole.Other)
                .Select(pair => pair.Key).OrderBy(LightDesktopLayoutPolicy.EntryOrder)
                .ThenBy(group => group.Id, StringComparer.Ordinal).ToList();
            var offsets = new Dictionary<GroupInfo, Point>();
            double Height(DesktopZoneRole role) => GroupHeaderHeight + 14 +
                LightDesktopLayoutPolicy.Rows(role) * GetGroupedIconRowHeight();
            double width = LightDesktopLayoutPolicy.ColumnWidth;
            double gap = LightDesktopLayoutPolicy.Gap;
            offsets[shortcuts] = new Point(0, 0);
            offsets[projects] = new Point(width + gap, 0);
            double leftBottom = Height(DesktopZoneRole.Shortcuts);
            if (research != null)
            {
                offsets[research] = new Point(0, leftBottom + gap);
                leftBottom += gap + Height(DesktopZoneRole.Research);
            }
            double entryTop = Height(DesktopZoneRole.Projects) + 24;
            for (int index = 0; index < entries.Count; index++)
            {
                offsets[entries[index]] = new Point(width + gap +
                    index % 2 * (LightDesktopLayoutPolicy.EntryWidth + LightDesktopLayoutPolicy.EntryGap),
                    entryTop + index / 2 * (GroupHeaderHeight + LightDesktopLayoutPolicy.EntryGap));
            }
            double rightBottom = entries.Count == 0 ? Height(DesktopZoneRole.Projects) :
                entryTop + Math.Ceiling(entries.Count / 2.0) * (GroupHeaderHeight + LightDesktopLayoutPolicy.EntryGap);
            var obstacles = GetSmartLayoutObstacles();
            obstacles.AddRange(_appLayout.Groups.Where(group => !offsets.ContainsKey(group)).Select(GetGroupBounds));
            obstacles.AddRange(_appLayout.FreeIcons.Values.Select(position =>
                new Rect(position.X, position.Y, IconCellWidth, IconCellHeight)));
            var cluster = new CompactGroupGridItem("light-desktop", width * 2 + gap,
                Math.Max(leftBottom, rightBottom));
            Dictionary<string, Point>? plan = null;
            bool reserved = _appLayout.ReserveTemporaryWorkspace;
            foreach (bool reserve in reserved ? new[] { true, false } : new[] { false })
            {
                plan = CompactGroupGridPlanner.TryPlan([cluster], GetSmartLayoutWorkspaces(reserve, 16),
                    obstacles, cluster.Width, gap, maximumColumns: 1);
                if (plan != null) { reserved = reserve; break; }
            }
            if (plan == null) return false;
            Point origin = plan["light-desktop"];
            foreach ((GroupInfo group, Point offset) in offsets)
            {
                bool firstApplication = group.DesktopRole == DesktopZoneRole.None;
                group.DesktopRole = roles[group];
                group.UseUniformTrackWidth = true;
                group.IsCollapsed = IsLightDesktopEntry(group) || (!firstApplication && group.IsCollapsed);
                group.Width = width;
                group.Height = Height(group.DesktopRole);
                group.X = origin.X + offset.X;
                group.Y = origin.Y + offset.Y;
            }
            _lastSmartLayoutPreservedWorkspace = reserved;
            return true;
        }
    }
}
