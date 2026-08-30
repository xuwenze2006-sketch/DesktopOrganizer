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

            StopGroupPeek();
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

            Dictionary<string, GroupLayoutSnapshot> attemptSnapshot = CaptureGroupLayoutSnapshot();
            bool arranged = ArrangeGroupsSmartly();
            if (!arranged)
            {
                RestoreGroupLayoutSnapshot(attemptSnapshot);
                StatusText.Text = "可用桌面空间不足，布局保持不变";
                return;
            }

            _lastSmartLayoutSnapshot = attemptSnapshot;
            UndoSmartLayoutButton.IsEnabled = true;
            RebuildDesktopIconsAndSaveLayout();
            string workspaceNote = _lastSmartLayoutPreservedWorkspace
                ? "，已保留主屏底部约三分之一临时区域"
                : _appLayout.ReserveTemporaryWorkspace
                    ? "，因空间不足已使用完整工作区"
                    : string.Empty;
            StatusText.Text = $"智能布局完成{workspaceNote}；已按内容数量优先排列，可点击“撤销布局”恢复";
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

        private void RestoreGroupLayoutSnapshot(
            IReadOnlyDictionary<string, GroupLayoutSnapshot> layoutSnapshot)
        {
            foreach (GroupInfo group in _appLayout.Groups)
            {
                if (!layoutSnapshot.TryGetValue(group.Id, out GroupLayoutSnapshot? snapshot) ||
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
        }

        private void UndoSmartLayoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastSmartLayoutSnapshot == null)
            {
                UndoSmartLayoutButton.IsEnabled = false;
                StatusText.Text = "没有可撤销的智能布局";
                return;
            }

            RestoreGroupLayoutSnapshot(_lastSmartLayoutSnapshot);

            _lastSmartLayoutSnapshot = null;
            UndoSmartLayoutButton.IsEnabled = false;
            RebuildDesktopIconsAndSaveLayout();
            StatusText.Text = "已恢复智能布局前的位置和尺寸";
        }

        /// <summary>
        /// 使用最多三轨的响应式瀑布流排列分类框。内容更多的分类优先占用顶部位置，
        /// 保持各分类当前展开/收起状态；保留区放不下时再使用完整桌面高度。
        /// </summary>
        private bool ArrangeGroupsSmartly()
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

            List<GroupInfo> ordered = SmartLayoutGroupOrderingPolicy.Order(
                _appLayout.Groups,
                group => group.Width * GetGroupDisplayHeight(group));

            bool placed = TryPackGroupLayout(ordered, reservedWorkspaces, gap);
            bool reservedPlacementSucceeded = placed && _appLayout.ReserveTemporaryWorkspace;

            if (!placed && _appLayout.ReserveTemporaryWorkspace)
            {
                placed = TryPackGroupLayout(ordered, fullWorkspaces, gap);
            }

            if (!placed)
            {
                placed = TryApplyFallbackGroupFlow(ordered, fullWorkspaces, gap);
            }

            if (!placed)
            {
                _lastSmartLayoutPreservedWorkspace = false;
                return false;
            }

            foreach (GroupInfo group in ordered)
            {
                ClampGroupToCanvas(group);
            }

            _lastSmartLayoutPreservedWorkspace = reservedPlacementSucceeded;
            return true;
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
                Rect? workspace = SmartLayoutWorkspacePolicy.TryCreateWorkspace(
                    monitor.WorkArea,
                    monitor.IsPrimary,
                    reserveTemporaryWorkspace,
                    margin,
                    GroupMinWidth,
                    GroupHeaderHeight);
                if (workspace.HasValue)
                {
                    workspaces.Add(workspace.Value);
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
            const double compactTrackWidth = 352;
            const int maximumColumns = 3;

            List<Rect> obstacles = GetSmartLayoutObstacles();

            List<CompactGroupGridItem> items = groups
                .Select(group => new CompactGroupGridItem(
                    group.Id,
                    group.Width,
                    GetGroupDisplayHeight(group)))
                .ToList();
            Dictionary<string, Point>? placements = CompactGroupGridPlanner.TryPlan(
                items,
                workspaces,
                obstacles,
                compactTrackWidth,
                gap,
                maximumColumns,
                preserveInputVerticalOrder: true);
            if (placements == null)
            {
                return false;
            }

            foreach (GroupInfo group in groups)
            {
                Point position = placements[group.Id];
                group.X = position.X;
                group.Y = position.Y;
            }
            return true;
        }

        private bool TryApplyFallbackGroupFlow(
            IReadOnlyList<GroupInfo> groups,
            IReadOnlyList<Rect> workspaces,
            double gap)
        {
            List<Rect> obstacles = GetSmartLayoutObstacles();

            List<CompactGroupGridItem> items = groups
                .Select(group => new CompactGroupGridItem(
                    group.Id,
                    group.Width,
                    GetGroupDisplayHeight(group)))
                .ToList();
            Dictionary<string, Point>? placements = CompactGroupGridPlanner.TryPlan(
                items,
                workspaces,
                obstacles,
                GroupMinWidth,
                gap,
                maximumColumns: int.MaxValue,
                preserveInputVerticalOrder: true);
            if (placements == null)
            {
                return false;
            }

            foreach (GroupInfo group in groups)
            {
                Point position = placements[group.Id];
                group.X = position.X;
                group.Y = position.Y;
            }

            return true;
        }

        private List<Rect> GetSmartLayoutObstacles()
        {
            return SmartLayoutObstaclePolicy.Create(
                GetFolderPortalObstacles(),
                GetRecycleBinWidgetObstacle());
        }

        // ==================== 分组拖拽、缩放、重命名与删除 ====================

    }
}
