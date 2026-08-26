// 分组折叠、智能布局与撤销
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private double GetGroupDisplayHeight(GroupInfo group)
        {
            return group.IsCollapsed ? GroupHeaderHeight : Math.Max(GroupMinHeight, group.Height);
        }

        private Rect GetGroupBounds(GroupInfo group)
        {
            return new Rect(group.X, group.Y, group.Width, GetGroupDisplayHeight(group));
        }

        private void ToggleGroupCollapsed(GroupInfo group)
        {
            group.IsCollapsed = !group.IsCollapsed;
            ClampGroupToCanvas(group);
            RebuildDesktopIconsAndSaveLayout();
            StatusText.Text = group.IsCollapsed
                ? $"已收起“{group.Name}”"
                : $"已展开“{group.Name}”";
        }

        private void CollapseGroupsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_appLayout.Groups.Count == 0)
            {
                StatusText.Text = "当前没有分组";
                return;
            }

            bool collapse = _appLayout.Groups.Any(group => !group.IsCollapsed);
            foreach (GroupInfo group in _appLayout.Groups)
            {
                group.IsCollapsed = collapse;
                ClampGroupToCanvas(group);
            }

            RebuildDesktopIconsAndSaveLayout();
            StatusText.Text = collapse ? "已收起全部分组" : "已展开全部分组";
        }

        private void UpdateCollapseGroupsButton()
        {
            bool hasGroups = _appLayout.Groups.Count > 0;
            CollapseGroupsButton.IsEnabled = hasGroups;
            CollapseGroupsButton.Content = hasGroups && _appLayout.Groups.All(group => group.IsCollapsed)
                ? "全部展开"
                : "全部收起";
        }

        private void SmartArrangeGroupsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_appLayout.Groups.Count == 0)
            {
                StatusText.Text = "当前没有可排列的分组";
                return;
            }

            _lastSmartLayoutSnapshot = CaptureGroupLayoutSnapshot();
            UndoSmartLayoutButton.IsEnabled = true;
            int newlyCollapsed = ArrangeGroupsSmartly();
            RebuildDesktopIconsAndSaveLayout();
            string workspaceNote = _lastSmartLayoutPreservedWorkspace
                ? "，已保留主屏底部约三分之一临时区域"
                : _appLayout.ReserveTemporaryWorkspace
                    ? "，因空间不足已使用完整工作区"
                    : string.Empty;
            StatusText.Text = newlyCollapsed > 0
                ? $"智能布局完成，收起 {newlyCollapsed} 个自动分类{workspaceNote}"
                : $"智能布局完成{workspaceNote}；可点击“撤销布局”恢复";
        }

        private Dictionary<string, GroupLayoutSnapshot> CaptureGroupLayoutSnapshot()
        {
            var snapshot = new Dictionary<string, GroupLayoutSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (GroupInfo group in _appLayout.Groups)
            {
                snapshot[group.Id] = new GroupLayoutSnapshot(
                    group.X,
                    group.Y,
                    group.Width,
                    group.Height,
                    group.IsCollapsed,
                    group.IsSizeLocked);
            }

            return snapshot;
        }

        private void UndoSmartLayoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastSmartLayoutSnapshot == null)
            {
                UndoSmartLayoutButton.IsEnabled = false;
                StatusText.Text = "没有可撤销的智能布局";
                return;
            }

            foreach (GroupInfo group in _appLayout.Groups)
            {
                if (!_lastSmartLayoutSnapshot.TryGetValue(group.Id, out GroupLayoutSnapshot? snapshot) ||
                    snapshot == null)
                {
                    continue;
                }

                group.X = snapshot.X;
                group.Y = snapshot.Y;
                group.Width = snapshot.Width;
                group.Height = snapshot.Height;
                group.IsCollapsed = snapshot.IsCollapsed;
                group.IsSizeLocked = snapshot.IsSizeLocked;
                ClampGroupToCanvas(group);
            }

            _lastSmartLayoutSnapshot = null;
            UndoSmartLayoutButton.IsEnabled = false;
            RebuildDesktopIconsAndSaveLayout();
            StatusText.Text = "已恢复智能布局前的位置和尺寸";
        }

        /// <summary>
        /// 使用可变宽度的二维瀑布流排列分类框。优先保留主屏底部约三分之一临时区域；
        /// 若空间不足，先收起自动分类，再在必要时使用完整桌面高度。
        /// </summary>
        private int ArrangeGroupsSmartly()
        {
            const double margin = 16;
            const double gap = 12;

            foreach (GroupInfo group in _appLayout.Groups.Where(group => !group.IsSizeLocked))
            {
                AutoFitGroup(group, clampPosition: false);
            }

            List<Rect> fullWorkspaces = GetSmartLayoutWorkspaces(
                reserveTemporaryWorkspace: false,
                margin);
            List<Rect> reservedWorkspaces = GetSmartLayoutWorkspaces(
                _appLayout.ReserveTemporaryWorkspace,
                margin);
            double maximumWorkspaceWidth = fullWorkspaces.Max(workspace => workspace.Width);

            foreach (GroupInfo group in _appLayout.Groups)
            {
                double maximumWidth = group.IsSizeLocked
                    ? maximumWorkspaceWidth
                    : Math.Min(GroupMaxAutoWidth, maximumWorkspaceWidth);
                group.Width = Math.Clamp(
                    group.Width,
                    GroupMinWidth,
                    Math.Max(GroupMinWidth, maximumWidth));
            }

            List<GroupInfo> ordered = _appLayout.Groups
                .OrderBy(group => group.IsAutoCategory ? 1 : 0)
                .ThenByDescending(group => group.Width * GetGroupDisplayHeight(group))
                .ThenByDescending(group => group.ItemNames.Count)
                .ThenBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            int newlyCollapsed = 0;
            bool placed = TryPackGroupLayout(ordered, reservedWorkspaces, gap);
            bool reservedPlacementSucceeded = placed && _appLayout.ReserveTemporaryWorkspace;

            while (!placed)
            {
                GroupInfo? collapseCandidate = ordered
                    .Where(group => group.IsAutoCategory && !group.IsCollapsed)
                    .OrderByDescending(GetGroupDisplayHeight)
                    .ThenByDescending(group => group.ItemNames.Count)
                    .FirstOrDefault();
                if (collapseCandidate == null)
                {
                    break;
                }

                collapseCandidate.IsCollapsed = true;
                newlyCollapsed++;
                placed = TryPackGroupLayout(ordered, reservedWorkspaces, gap);
                reservedPlacementSucceeded = placed && _appLayout.ReserveTemporaryWorkspace;
            }

            if (!placed && _appLayout.ReserveTemporaryWorkspace)
            {
                placed = TryPackGroupLayout(ordered, fullWorkspaces, gap);
            }

            if (!placed)
            {
                ApplyFallbackGroupFlow(ordered, fullWorkspaces, gap);
            }

            foreach (GroupInfo group in ordered)
            {
                ClampGroupToCanvas(group);
            }

            _lastSmartLayoutPreservedWorkspace = reservedPlacementSucceeded;
            return newlyCollapsed;
        }

        private List<Rect> GetSmartLayoutWorkspaces(
            bool reserveTemporaryWorkspace,
            double margin)
        {
            var workspaces = new List<Rect>();
            foreach (DesktopMonitorRegion monitor in _desktopGeometry.Monitors
                         .OrderByDescending(item => item.IsPrimary)
                         .ThenBy(item => item.WorkArea.Left)
                         .ThenBy(item => item.WorkArea.Top))
            {
                Rect workArea = monitor.WorkArea;
                double topInset = monitor.IsPrimary ? 70 : margin;
                double left = workArea.Left + margin;
                double top = workArea.Top + topInset;
                double right = workArea.Right - margin;
                double bottom = workArea.Bottom - margin;

                if (reserveTemporaryWorkspace && monitor.IsPrimary)
                {
                    bottom = Math.Min(
                        bottom,
                        Math.Max(top + 260, workArea.Top + workArea.Height * 0.66));
                }

                if (right - left >= GroupMinWidth && bottom - top >= GroupHeaderHeight)
                {
                    workspaces.Add(new Rect(left, top, right - left, bottom - top));
                }
            }

            if (workspaces.Count == 0)
            {
                Rect primary = GetPrimaryWorkArea();
                workspaces.Add(new Rect(
                    primary.Left,
                    primary.Top,
                    Math.Max(GroupMinWidth, primary.Width),
                    Math.Max(GroupHeaderHeight, primary.Height)));
            }

            return workspaces;
        }

        private bool TryPackGroupLayout(
            IReadOnlyList<GroupInfo> groups,
            IReadOnlyList<Rect> workspaces,
            double gap)
        {
            var placedRects = new List<Rect>(groups.Count + 1);
            Rect? recycleObstacle = GetRecycleBinWidgetObstacle();
            if (recycleObstacle.HasValue)
            {
                placedRects.Add(recycleObstacle.Value);
            }
            var placements = new List<(GroupInfo Group, Point Position)>(groups.Count);

            foreach (GroupInfo group in groups)
            {
                double height = GetGroupDisplayHeight(group);
                var candidates = new List<(Point Point, Rect Workspace)>();
                foreach (Rect workspace in workspaces)
                {
                    candidates.Add((new Point(workspace.Left, workspace.Top), workspace));
                    foreach (Rect placed in placedRects.Where(rect => workspace.IntersectsWith(rect)))
                    {
                        candidates.Add((new Point(placed.Right + gap, placed.Top), workspace));
                        candidates.Add((new Point(placed.Left, placed.Bottom + gap), workspace));
                        candidates.Add((new Point(workspace.Left, placed.Bottom + gap), workspace));
                    }
                }

                (Point Point, Rect Workspace)? selected = null;
                foreach ((Point point, Rect workspace) in candidates
                    .Where(candidate =>
                        double.IsFinite(candidate.Point.X) &&
                        double.IsFinite(candidate.Point.Y))
                    .Select(candidate => (
                        new Point(
                            Math.Max(candidate.Workspace.Left, Math.Round(candidate.Point.X)),
                            Math.Max(candidate.Workspace.Top, Math.Round(candidate.Point.Y))),
                        candidate.Workspace))
                    .Distinct()
                    .OrderBy(candidate => candidate.Item2.Top)
                    .ThenBy(candidate => candidate.Item2.Left)
                    .ThenBy(candidate => candidate.Item1.Y)
                    .ThenBy(candidate => candidate.Item1.X))
                {
                    double width = Math.Min(group.Width, workspace.Width);
                    var candidateRect = new Rect(point.X, point.Y, width, height);
                    if (IsGroupPlacementAvailable(candidateRect, placedRects, workspace, gap))
                    {
                        selected = (point, workspace);
                        break;
                    }
                }

                if (selected == null)
                {
                    return false;
                }

                double placedWidth = Math.Min(group.Width, selected.Value.Workspace.Width);
                group.Width = Math.Max(GroupMinWidth, placedWidth);
                var rect = new Rect(
                    selected.Value.Point.X,
                    selected.Value.Point.Y,
                    group.Width,
                    height);
                placedRects.Add(rect);
                placements.Add((group, selected.Value.Point));
            }

            foreach ((GroupInfo group, Point position) in placements)
            {
                group.X = position.X;
                group.Y = position.Y;
            }

            return true;
        }

        private static bool IsGroupPlacementAvailable(
            Rect candidate,
            IReadOnlyList<Rect> placedRects,
            Rect workspace,
            double gap)
        {
            if (!workspace.Contains(candidate))
            {
                return false;
            }

            Rect padded = candidate;
            padded.Inflate(gap / 2, gap / 2);
            return placedRects.All(existing => !padded.IntersectsWith(existing));
        }

        private void ApplyFallbackGroupFlow(
            IReadOnlyList<GroupInfo> groups,
            IReadOnlyList<Rect> workspaces,
            double gap)
        {
            int workspaceIndex = 0;
            Rect workspace = workspaces[workspaceIndex];
            double x = workspace.Left;
            double y = workspace.Top;
            double rowHeight = 0;

            foreach (GroupInfo group in groups)
            {
                double height = GetGroupDisplayHeight(group);
                group.Width = Math.Min(group.Width, workspace.Width);
                if (x + group.Width > workspace.Right && x > workspace.Left)
                {
                    x = workspace.Left;
                    y += rowHeight + gap;
                    rowHeight = 0;
                }

                if (y + height > workspace.Bottom && workspaceIndex + 1 < workspaces.Count)
                {
                    workspace = workspaces[++workspaceIndex];
                    x = workspace.Left;
                    y = workspace.Top;
                    rowHeight = 0;
                    group.Width = Math.Min(group.Width, workspace.Width);
                }

                group.X = x;
                group.Y = y;
                x += group.Width + gap;
                rowHeight = Math.Max(rowHeight, height);
            }
        }

        // ==================== 分组拖拽、缩放、重命名与删除 ====================

    }
}
