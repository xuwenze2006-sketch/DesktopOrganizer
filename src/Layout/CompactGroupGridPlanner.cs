using System.Windows;

namespace DesktopOrganizer
{
    internal readonly record struct CompactGroupGridItem(
        string Id,
        double Width,
        double Height);

    /// <summary>
    /// 在按优先级排列的工作区中，把可变高度卡片放入最多三条固定轨道。
    /// 规划过程不修改输入；只有全部卡片都有位置时才返回完整结果。
    /// </summary>
    internal static class CompactGroupGridPlanner
    {
        private const double CoordinateTolerance = 0.01;

        public static Dictionary<string, Point>? TryPlan(
            IReadOnlyList<CompactGroupGridItem> items,
            IReadOnlyList<Rect> workspaces,
            IReadOnlyList<Rect> obstacles,
            double trackWidth,
            double gap,
            int maximumColumns,
            bool preserveInputVerticalOrder = false)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(workspaces);
            ArgumentNullException.ThrowIfNull(obstacles);

            if (!double.IsFinite(trackWidth) || trackWidth <= 0 ||
                !double.IsFinite(gap) || gap < 0 ||
                maximumColumns <= 0)
            {
                return null;
            }

            var result = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
            if (items.Count == 0)
            {
                return result;
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CompactGroupGridItem item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id) ||
                    !double.IsFinite(item.Width) || item.Width <= 0 ||
                    !double.IsFinite(item.Height) || item.Height <= 0)
                {
                    return null;
                }
            }

            List<WorkspaceState> workspaceStates = workspaces
                .Where(IsFiniteUsableRect)
                .Select((workspace, index) =>
                    WorkspaceState.TryCreate(workspace, index, trackWidth, gap, maximumColumns))
                .OfType<WorkspaceState>()
                .ToList();
            if (workspaceStates.Count == 0)
            {
                return null;
            }

            var occupied = obstacles
                .Where(IsFiniteUsableRect)
                .ToList();

            if (preserveInputVerticalOrder)
            {
                return TryPlanPreservingInputVerticalOrder(
                    items,
                    workspaceStates,
                    occupied,
                    gap);
            }

            foreach (CompactGroupGridItem item in items)
            {
                PlacementCandidate? selected = null;
                foreach (WorkspaceState workspace in workspaceStates)
                {
                    PlacementCandidate? candidate = FindBestCandidate(
                        item,
                        workspace,
                        occupied,
                        gap,
                        workspace.Bounds.Top);
                    if (candidate.HasValue)
                    {
                        // 工作区列表已经按产品优先级排序；只有当前工作区放不下时才使用下一块。
                        selected = candidate;
                        break;
                    }
                }

                if (!selected.HasValue)
                {
                    return null;
                }

                PlacementCandidate placement = selected.Value;
                result[item.Id] = new Point(placement.Bounds.X, placement.Bounds.Y);
                occupied.Add(placement.Bounds);
                double nextBottom = placement.Bounds.Bottom + gap;
                for (int column = placement.StartColumn;
                     column < placement.StartColumn + placement.ColumnSpan;
                     column++)
                {
                    placement.Workspace.ColumnBottoms[column] = Math.Max(
                        placement.Workspace.ColumnBottoms[column],
                        nextBottom);
                }
            }

            return result;
        }

        private static Dictionary<string, Point>? TryPlanPreservingInputVerticalOrder(
            IReadOnlyList<CompactGroupGridItem> items,
            IReadOnlyList<WorkspaceState> workspaces,
            List<Rect> occupied,
            double gap)
        {
            const int maximumSearchStates = 25_000;
            var result = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
            int visitedStates = 0;
            bool searchLimitReached = false;

            bool TryPlaceItem(int itemIndex)
            {
                if (itemIndex >= items.Count)
                {
                    return true;
                }

                if (++visitedStates > maximumSearchStates)
                {
                    searchLimitReached = true;
                    return false;
                }

                CompactGroupGridItem item = items[itemIndex];
                foreach (WorkspaceState workspace in workspaces)
                {
                    List<PlacementCandidate> candidates = FindPlacementCandidates(
                        item,
                        workspace,
                        occupied,
                        gap,
                        workspace.MinimumPlacementY);
                    foreach (PlacementCandidate placement in candidates)
                    {
                        double previousMinimumY = placement.Workspace.MinimumPlacementY;
                        double[] previousColumnBottoms = new double[placement.ColumnSpan];
                        for (int offset = 0; offset < placement.ColumnSpan; offset++)
                        {
                            previousColumnBottoms[offset] = placement.Workspace.ColumnBottoms[
                                placement.StartColumn + offset];
                        }

                        result[item.Id] = new Point(placement.Bounds.X, placement.Bounds.Y);
                        occupied.Add(placement.Bounds);
                        placement.Workspace.MinimumPlacementY = Math.Max(
                            placement.Workspace.MinimumPlacementY,
                            placement.Bounds.Y);
                        double nextBottom = placement.Bounds.Bottom + gap;
                        for (int column = placement.StartColumn;
                             column < placement.StartColumn + placement.ColumnSpan;
                             column++)
                        {
                            placement.Workspace.ColumnBottoms[column] = Math.Max(
                                placement.Workspace.ColumnBottoms[column],
                                nextBottom);
                        }

                        if (TryPlaceItem(itemIndex + 1))
                        {
                            return true;
                        }

                        result.Remove(item.Id);
                        occupied.RemoveAt(occupied.Count - 1);
                        placement.Workspace.MinimumPlacementY = previousMinimumY;
                        for (int offset = 0; offset < placement.ColumnSpan; offset++)
                        {
                            placement.Workspace.ColumnBottoms[placement.StartColumn + offset] =
                                previousColumnBottoms[offset];
                        }

                        if (searchLimitReached)
                        {
                            return false;
                        }
                    }
                }

                return false;
            }

            return TryPlaceItem(0) ? result : null;
        }

        private static PlacementCandidate? FindBestCandidate(
            CompactGroupGridItem item,
            WorkspaceState workspace,
            IReadOnlyList<Rect> occupied,
            double gap,
            double minimumPlacementY)
        {
            List<PlacementCandidate> candidates = FindPlacementCandidates(
                item,
                workspace,
                occupied,
                gap,
                minimumPlacementY);
            return candidates.Count == 0 ? null : candidates[0];
        }

        private static List<PlacementCandidate> FindPlacementCandidates(
            CompactGroupGridItem item,
            WorkspaceState workspace,
            IReadOnlyList<Rect> occupied,
            double gap,
            double minimumPlacementY)
        {
            int span = GetRequiredColumnSpan(item.Width, workspace.TrackWidth, gap);
            if (span > workspace.ColumnCount)
            {
                if (item.Width > workspace.Bounds.Width + CoordinateTolerance)
                {
                    return [];
                }

                // 固定轨道右侧可能留有不足一轨的余量。允许能完整放入工作区的
                // 全宽卡片占用全部现有轨道，避免兜底布局误判为空间不足。
                span = workspace.ColumnCount;
            }

            double trackSpanWidth = span * workspace.TrackWidth + (span - 1) * gap;
            bool canUseTrailingWorkspaceRemainder =
                span == workspace.ColumnCount &&
                item.Width <= workspace.Bounds.Width + CoordinateTolerance;
            if (!canUseTrailingWorkspaceRemainder &&
                item.Width > trackSpanWidth + CoordinateTolerance)
            {
                return [];
            }

            List<Rect> workspaceOccupied = FilterOccupiedForWorkspace(
                occupied,
                workspace.Bounds,
                gap);

            var candidates = new List<PlacementCandidate>(workspace.ColumnCount - span + 1);
            for (int startColumn = 0;
                 startColumn + span <= workspace.ColumnCount;
                 startColumn++)
            {
                double initialY = Math.Max(workspace.Bounds.Top, minimumPlacementY);
                for (int column = startColumn; column < startColumn + span; column++)
                {
                    initialY = Math.Max(initialY, workspace.ColumnBottoms[column]);
                }

                double x = workspace.Bounds.Left +
                           startColumn * (workspace.TrackWidth + gap);
                double? y = FindFirstAvailableY(
                    workspace.Bounds,
                    workspaceOccupied,
                    x,
                    initialY,
                    item.Width,
                    item.Height,
                    gap);
                if (!y.HasValue)
                {
                    continue;
                }

                candidates.Add(new PlacementCandidate(
                    workspace,
                    startColumn,
                    span,
                    new Rect(x, y.Value, item.Width, item.Height)));
            }

            return candidates
                .OrderBy(candidate => candidate.Bounds.Y)
                .ThenBy(candidate => candidate.StartColumn)
                .ToList();
        }

        internal static List<Rect> FilterOccupiedForWorkspace(
            IReadOnlyList<Rect> occupied,
            Rect workspace,
            double gap)
        {
            Rect collisionScope = workspace;
            collisionScope.Inflate(gap / 2, gap / 2);
            return occupied
                .Where(collisionScope.IntersectsWith)
                .ToList();
        }

        private static double? FindFirstAvailableY(
            Rect workspace,
            IReadOnlyList<Rect> occupied,
            double x,
            double initialY,
            double width,
            double height,
            double gap)
        {
            double y = Math.Max(workspace.Top, initialY);
            for (int attempt = 0; attempt <= occupied.Count; attempt++)
            {
                var candidate = new Rect(x, y, width, height);
                if (!workspace.Contains(candidate))
                {
                    return null;
                }

                Rect padded = candidate;
                padded.Inflate(gap / 2, gap / 2);
                List<Rect> collisions = occupied
                    .Where(padded.IntersectsWith)
                    .ToList();
                if (collisions.Count == 0)
                {
                    return y;
                }

                double nextY = collisions.Max(rect => rect.Bottom + gap);
                if (!double.IsFinite(nextY) || nextY <= y + CoordinateTolerance)
                {
                    return null;
                }

                y = nextY;
            }

            return null;
        }

        private static int GetRequiredColumnSpan(double width, double trackWidth, double gap)
        {
            double adjustedWidth = Math.Max(0, width - CoordinateTolerance);
            double rawSpan = (adjustedWidth + gap) / (trackWidth + gap);
            return Math.Max(1, (int)Math.Ceiling(rawSpan));
        }

        private static bool IsFiniteUsableRect(Rect rect) =>
            !rect.IsEmpty &&
            double.IsFinite(rect.X) &&
            double.IsFinite(rect.Y) &&
            double.IsFinite(rect.Width) &&
            double.IsFinite(rect.Height) &&
            rect.Width > 0 &&
            rect.Height > 0;

        private sealed class WorkspaceState
        {
            private WorkspaceState(
                Rect bounds,
                int sourceIndex,
                double trackWidth,
                int columnCount)
            {
                Bounds = bounds;
                SourceIndex = sourceIndex;
                TrackWidth = trackWidth;
                ColumnCount = columnCount;
                ColumnBottoms = Enumerable.Repeat(bounds.Top, columnCount).ToArray();
                MinimumPlacementY = bounds.Top;
            }

            public Rect Bounds { get; }
            public int SourceIndex { get; }
            public double TrackWidth { get; }
            public int ColumnCount { get; }
            public double[] ColumnBottoms { get; }
            public double MinimumPlacementY { get; set; }

            public static WorkspaceState? TryCreate(
                Rect bounds,
                int sourceIndex,
                double preferredTrackWidth,
                double gap,
                int maximumColumns)
            {
                double trackWidth = Math.Min(preferredTrackWidth, bounds.Width);
                int columnCount = Math.Min(
                    maximumColumns,
                    Math.Max(1, (int)Math.Floor(
                        (bounds.Width + gap) / (trackWidth + gap) + CoordinateTolerance)));
                while (columnCount > 1 &&
                       columnCount * trackWidth + (columnCount - 1) * gap >
                       bounds.Width + CoordinateTolerance)
                {
                    columnCount--;
                }

                return new WorkspaceState(bounds, sourceIndex, trackWidth, columnCount);
            }
        }

        private readonly record struct PlacementCandidate(
            WorkspaceState Workspace,
            int StartColumn,
            int ColumnSpan,
            Rect Bounds);
    }
}
