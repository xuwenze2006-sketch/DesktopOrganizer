namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private static bool IsLightDesktopEntry(GroupInfo group) =>
            group.DesktopRole == DesktopZoneRole.Other && !group.IsSizeLocked;

        private static double GetGroupDisplayWidth(GroupInfo group) =>
            IsLightDesktopEntry(group) ? LightDesktopLayoutPolicy.EntryWidth : group.Width;

        // OrderBy 保留同级自定义分组的列表顺序，JSON 和工作区均保存该顺序。
        private List<GroupInfo> GetOtherCategoryEntries() => _appLayout.Groups.Where(IsLightDesktopEntry)
            .OrderBy(LightDesktopLayoutPolicy.EntryOrder).ToList();

        private Point GetOtherCategoryOrigin()
        {
            var entries = GetOtherCategoryEntries();
            if (entries.Count > 0) return new Point(entries.Min(group => group.X), entries.Min(group => group.Y));
            GroupInfo? project = _appLayout.Groups.FirstOrDefault(group => group.DesktopRole == DesktopZoneRole.Projects);
            if (project != null) return new Point(project.X, project.Y + GetGroupDisplayHeight(project) + 24);
            Point position = FindAvailableAutoGroupPosition(LightDesktopLayoutPolicy.ColumnWidth, 150);
            return new Point(position.X, position.Y + 24);
        }

        private void ReflowOtherCategoryAnchors(Point origin)
        {
            var entries = GetOtherCategoryEntries();
            for (int index = 0; index < entries.Count; index++)
            {
                entries[index].X = origin.X + index % 2 *
                    (LightDesktopLayoutPolicy.EntryWidth + LightDesktopLayoutPolicy.EntryGap);
                // 持久化坐标只表达桌面占位；滚动内容的完整行号由容器计算，不能写到屏幕外。
                entries[index].Y = origin.Y + Math.Min(index / 2, LightDesktopLayoutPolicy.EntryVisibleRows - 1) *
                    (GroupHeaderHeight + LightDesktopLayoutPolicy.EntryGap);
            }
        }

        private void SetOtherCategoryMembership(GroupInfo group, bool join)
        {
            if (!_appLayout.IsEditMode || !_appLayout.Groups.Contains(group)) return;
            Point origin = GetOtherCategoryOrigin();
            ApplyOtherCategoryMembership(group, join);
            if (!join)
            {
                Point position = FindAvailableAutoGroupPosition(group.Width, GetGroupDisplayHeight(group));
                group.X = position.X; group.Y = position.Y;
                ClampGroupToCanvas(group);
            }
            ReflowOtherCategoryAnchors(origin);
            _lastSmartLayoutSnapshot = null;
            UndoSmartLayoutButton.IsEnabled = false;
            RebuildDesktopIconsAndSaveLayout();
            if (join) _lightDesktopSectionScroll?.ScrollToEnd();
            StatusText.Text = join ? $"已将“{group.Name}”移入其他分类" : $"“{group.Name}”已恢复为独立分组";
        }

        private void ApplyOtherCategoryMembership(GroupInfo group, bool join)
        {
            StopGroupPeek();
            group.DesktopRole = join ? DesktopZoneRole.Other : DesktopZoneRole.None;
            group.IsSizeLocked = false;
            group.IsCollapsed = join;
            group.UseUniformTrackWidth = join;
            AutoFitGroup(group, clampPosition: false);
            // 手动移出的分组保留独立尺寸，下一次智能布局不会按内置名称自动收回。
            if (!join) group.IsSizeLocked = true;
        }

        private bool IsPointInOtherCategories(Point point) => _lightDesktopSection != null &&
            TryGetElementBoundsOnCanvas(_lightDesktopSection, out Rect bounds) && bounds.Contains(point);

        private void PromoteOtherCategoryEntryForDrag(Border entry)
        {
            Point position = entry.TranslatePoint(new Point(), IconCanvas);
            double width = entry.ActualWidth;
            (VisualTreeHelper.GetParent(entry) as Panel)?.Children.Remove(entry);
            entry.Margin = new Thickness(0);
            entry.Width = width;
            entry.Background = LightDesktopSurfaceBrush;
            Canvas.SetLeft(entry, position.X); Canvas.SetTop(entry, position.Y);
            IconCanvas.Children.Add(entry);
        }

        private bool CompleteOtherCategoryDrag(GroupInfo group, UIElement visual, bool commit)
        {
            bool wasEntry = IsLightDesktopEntry(group);
            double left = SafeCanvasCoordinate(Canvas.GetLeft(visual));
            double top = SafeCanvasCoordinate(Canvas.GetTop(visual));
            Point pointer = _groupDragLastCanvasPoint ?? new Point(left + GetGroupDisplayWidth(group) / 2,
                top + GroupHeaderHeight / 2);
            bool join = IsPointInOtherCategories(pointer);
            if (!wasEntry && !(commit && join && group.DesktopRole == DesktopZoneRole.None)) return false;

            Point origin = GetOtherCategoryOrigin();
            bool changed = commit && wasEntry != join;
            GroupInfo? detached = null;
            if (changed && !join)
            {
                detached = new GroupInfo { X = left, Y = top, ItemNames = group.ItemNames };
                AutoFitGroup(detached, clampPosition: false);
                ClampGroupToCanvas(detached);
                if (IntersectsFolderPortal(GetGroupBounds(detached)))
                {
                    changed = false;
                    StatusText.Text = "分组不能覆盖只读文件夹入口，已恢复原位置";
                }
            }
            if (changed)
            {
                ApplyOtherCategoryMembership(group, join);
                if (detached != null) { group.X = detached.X; group.Y = detached.Y; }
                ReflowOtherCategoryAnchors(origin);
                _lastSmartLayoutSnapshot = null;
                UndoSmartLayoutButton.IsEnabled = false;
            }
            ResetGroupDragState();
            RebuildDesktopIcons();
            if (changed)
            {
                SaveLayout();
                if (join) _lightDesktopSectionScroll?.ScrollToEnd();
                StatusText.Text = join ? $"已将“{group.Name}”移入其他分类" : $"“{group.Name}”已恢复为独立分组";
            }
            return true;
        }

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
                .ToList();
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
                    entryTop + Math.Min(index / 2, LightDesktopLayoutPolicy.EntryVisibleRows - 1) *
                    (GroupHeaderHeight + LightDesktopLayoutPolicy.EntryGap));
            }
            double rightBottom = entries.Count == 0 ? Height(DesktopZoneRole.Projects) :
                entryTop + Math.Min(Math.Ceiling(entries.Count / 2.0), LightDesktopLayoutPolicy.EntryVisibleRows) *
                    (GroupHeaderHeight + LightDesktopLayoutPolicy.EntryGap);
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
