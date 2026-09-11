namespace DesktopOrganizer
{
    internal sealed record GroupItemDropResult(
        bool Applied,
        bool Changed,
        bool MovedBetweenGroups,
        int TargetIndex)
    {
        public static GroupItemDropResult Rejected { get; } = new(
            Applied: false,
            Changed: false,
            MovedBetweenGroups: false,
            TargetIndex: -1);
    }

    internal sealed record GroupItemBatchMoveResult(
        bool TargetFound,
        IReadOnlyList<string> MovedNames)
    {
        public int MovedCount => MovedNames.Count;
    }

    /// <summary>
    /// 分类框图标拖放的纯模型策略。落点边界以当前可见顺序为准；应用时只更新
    /// 虚拟分组成员关系、手工归属标记与自定义排序，不执行任何文件系统操作。
    /// </summary>
    internal static class GroupItemDropPolicy
    {
        /// <summary>
        /// 将面板内局部坐标换算为行优先排列中的插入边界。单元格左半区表示
        /// 插入到该项之前，右半区表示插入到该项之后；结果始终位于 0..itemCount。
        /// </summary>
        public static int CalculateInsertionBoundary(
            double localX,
            double localY,
            int columnCount,
            double slotWidth,
            double rowHeight,
            double verticalOffset,
            int itemCount)
        {
            if (columnCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(columnCount));
            }

            if (!double.IsFinite(slotWidth) || slotWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotWidth));
            }

            if (!double.IsFinite(rowHeight) || rowHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rowHeight));
            }

            if (!double.IsFinite(localX))
            {
                throw new ArgumentOutOfRangeException(nameof(localX));
            }

            if (!double.IsFinite(localY))
            {
                throw new ArgumentOutOfRangeException(nameof(localY));
            }

            if (!double.IsFinite(verticalOffset))
            {
                throw new ArgumentOutOfRangeException(nameof(verticalOffset));
            }

            if (itemCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemCount));
            }

            if (itemCount == 0)
            {
                return 0;
            }

            double contentY = Math.Max(0, localY + Math.Max(0, verticalOffset));
            double rowValue = Math.Floor(contentY / rowHeight);
            int maximumRowCount = (int)Math.Ceiling(itemCount / (double)columnCount);
            if (rowValue >= maximumRowCount)
            {
                return itemCount;
            }

            int row = Math.Max(0, (int)rowValue);
            if (localX <= 0)
            {
                return Math.Clamp(row * columnCount, 0, itemCount);
            }

            double rowWidth = columnCount * slotWidth;
            if (localX >= rowWidth)
            {
                return Math.Clamp((row + 1) * columnCount, 0, itemCount);
            }

            int column = Math.Clamp((int)Math.Floor(localX / slotWidth), 0, columnCount - 1);
            double positionWithinSlot = localX - column * slotWidth;
            int boundary = row * columnCount + column;
            if (positionWithinSlot >= slotWidth / 2)
            {
                boundary++;
            }

            return Math.Clamp(boundary, 0, itemCount);
        }

        /// <summary>
        /// 返回插入边界在虚拟化网格中的可视指示条。行尾边界显示在下一行开头；
        /// 只有“追加到完整末行”保留在末项右侧，避免指示条落到尚不存在的新行。
        /// </summary>
        public static Rect CalculateInsertionIndicatorRect(
            int insertionBoundary,
            double localY,
            int columnCount,
            double slotWidth,
            double rowHeight,
            double verticalOffset,
            int itemCount)
        {
            if (columnCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(columnCount));
            }

            if (!double.IsFinite(slotWidth) || slotWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotWidth));
            }

            if (!double.IsFinite(rowHeight) || rowHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rowHeight));
            }

            if (!double.IsFinite(localY))
            {
                throw new ArgumentOutOfRangeException(nameof(localY));
            }

            if (!double.IsFinite(verticalOffset))
            {
                throw new ArgumentOutOfRangeException(nameof(verticalOffset));
            }

            if (itemCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemCount));
            }

            int boundary = Math.Clamp(insertionBoundary, 0, itemCount);
            int row;
            int column;
            int boundaryRow = boundary / columnCount;
            int pointerRow = Math.Max(
                0,
                (int)Math.Floor(
                    Math.Max(0, localY + Math.Max(0, verticalOffset)) /
                    rowHeight));
            bool preferPreviousRow =
                boundary > 0 &&
                boundary % columnCount == 0 &&
                (pointerRow < boundaryRow || boundary == itemCount);
            if (preferPreviousRow)
            {
                row = boundaryRow - 1;
                column = columnCount;
            }
            else
            {
                row = boundary / columnCount;
                column = boundary % columnCount;
            }

            const double thickness = 4;
            double inset = Math.Min(8, rowHeight / 4);
            double gridWidth = columnCount * slotWidth;
            double x = Math.Clamp(
                column * slotWidth - thickness / 2,
                0,
                Math.Max(0, gridWidth - thickness));
            double y = row * rowHeight - Math.Max(0, verticalOffset) + inset;
            return new Rect(
                x,
                y,
                Math.Min(thickness, gridWidth),
                Math.Max(4, rowHeight - inset * 2));
        }

        /// <summary>
        /// 将项目插入目标分类框。targetVisibleOrder 必须是投放发生前目标框的
        /// 当前可见顺序；这样从名称/类型等自动排序切换到 Custom 时不会跳回旧顺序。
        /// </summary>
        public static GroupItemDropResult Apply(
            IList<GroupInfo> groups,
            string itemName,
            string? sourceGroupId,
            string targetGroupId,
            int targetBoundary,
            IReadOnlyList<string> targetVisibleOrder)
        {
            ArgumentNullException.ThrowIfNull(groups);
            ArgumentNullException.ThrowIfNull(targetVisibleOrder);

            if (string.IsNullOrWhiteSpace(itemName) || string.IsNullOrWhiteSpace(targetGroupId))
            {
                return GroupItemDropResult.Rejected;
            }

            GroupInfo? target = groups.FirstOrDefault(group =>
                group.Id.Equals(targetGroupId, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                return GroupItemDropResult.Rejected;
            }

            List<(GroupInfo Group, List<string> Items, List<string> ManualItems, GroupSortMode SortMode)>
                originalStates = groups
                    .Select(group => (
                        Group: group,
                        Items: (group.ItemNames ?? new List<string>()).ToList(),
                        ManualItems: (group.ManuallyAssignedItemNames ?? new List<string>()).ToList(),
                        SortMode: group.SortMode))
                    .ToList();

            GroupInfo? source = string.IsNullOrWhiteSpace(sourceGroupId)
                ? null
                : groups.FirstOrDefault(group =>
                    group.Id.Equals(sourceGroupId, StringComparison.OrdinalIgnoreCase) &&
                    Contains(group.ItemNames, itemName));
            source ??= groups.FirstOrDefault(group => Contains(group.ItemNames, itemName));

            bool movedBetweenGroups = source != null && !ReferenceEquals(source, target);
            bool createsManualAssignment = source == null || movedBetweenGroups;
            List<string> originalTargetItems = target.ItemNames ?? new List<string>();
            var targetMembers = new HashSet<string>(
                originalTargetItems,
                StringComparer.OrdinalIgnoreCase);
            List<string> visibleItems = DistinctNames(targetVisibleOrder)
                .Where(targetMembers.Contains)
                .ToList();

            int clampedBoundary = Math.Clamp(targetBoundary, 0, visibleItems.Count);
            int existingVisibleIndex = visibleItems.FindIndex(name => NamesEqual(name, itemName));
            if (existingVisibleIndex >= 0 && existingVisibleIndex < clampedBoundary)
            {
                // 边界基于拖动开始前的可见列表；移除源项后，其后的边界需左移一格。
                clampedBoundary--;
            }

            visibleItems.RemoveAll(name => NamesEqual(name, itemName));
            int targetIndex = Math.Clamp(clampedBoundary, 0, visibleItems.Count);
            visibleItems.Insert(targetIndex, itemName);

            // 可见列表可能暂时不包含尚未实现或已从桌面消失、等待刷新清理的条目。
            // 保留这些既有成员，但不能让它们改变用户当前看到的插入位置。
            foreach (string existingName in originalTargetItems)
            {
                if (!NamesEqual(existingName, itemName) && !Contains(visibleItems, existingName))
                {
                    visibleItems.Add(existingName);
                }
            }

            foreach (GroupInfo group in groups)
            {
                group.ItemNames ??= new List<string>();
                group.ItemNames.RemoveAll(name => NamesEqual(name, itemName));

                group.ManuallyAssignedItemNames ??= new List<string>();
                if (ReferenceEquals(group, target))
                {
                    if (createsManualAssignment ||
                        Contains(group.ManuallyAssignedItemNames, itemName))
                    {
                        EnsureSingleName(group.ManuallyAssignedItemNames, itemName);
                    }
                }
                else
                {
                    group.ManuallyAssignedItemNames.RemoveAll(name =>
                        NamesEqual(name, itemName));
                }
            }

            target.ItemNames = visibleItems;
            target.SortMode = GroupSortMode.Custom;

            bool changed = originalStates.Any(state =>
                state.SortMode != state.Group.SortMode ||
                !SequenceEqual(state.Items, state.Group.ItemNames) ||
                !SequenceEqual(state.ManualItems, state.Group.ManuallyAssignedItemNames));

            return new GroupItemDropResult(
                Applied: true,
                Changed: changed,
                MovedBetweenGroups: movedBetweenGroups,
                TargetIndex: targetIndex);
        }

        /// <summary>
        /// 把一组已加载项目追加到目标分类框。批量入口没有指针插入位置，因此按稳定名称
        /// 顺序追加，并保留目标当前排序模式；只修改虚拟成员关系和本地布局元数据。
        /// </summary>
        public static GroupItemBatchMoveResult ApplyBatch(
            IList<GroupInfo> groups,
            IDictionary<string, IconPosition> freeIcons,
            IDictionary<string, IconPosition> autoClassificationOriginalPositions,
            IEnumerable<string> itemNames,
            string targetGroupId)
        {
            ArgumentNullException.ThrowIfNull(groups);
            ArgumentNullException.ThrowIfNull(freeIcons);
            ArgumentNullException.ThrowIfNull(autoClassificationOriginalPositions);
            ArgumentNullException.ThrowIfNull(itemNames);

            GroupInfo? target = string.IsNullOrWhiteSpace(targetGroupId)
                ? null
                : groups.FirstOrDefault(group =>
                    group.Id.Equals(targetGroupId, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                return new GroupItemBatchMoveResult(false, Array.Empty<string>());
            }

            List<string> movedNames = DistinctNames(itemNames)
                .Where(name => !Contains(target.ItemNames, name))
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (movedNames.Count == 0)
            {
                return new GroupItemBatchMoveResult(true, movedNames);
            }

            target.ItemNames ??= new List<string>();
            target.ManuallyAssignedItemNames ??= new List<string>();
            foreach (string name in movedNames)
            {
                bool movedFromAutoCategory = groups.Any(group =>
                    !ReferenceEquals(group, target) &&
                    group.IsAutoCategory &&
                    Contains(group.ItemNames, name));

                foreach (GroupInfo group in groups)
                {
                    group.ItemNames ??= new List<string>();
                    group.ItemNames.RemoveAll(item => NamesEqual(item, name));
                    group.ManuallyAssignedItemNames ??= new List<string>();
                    group.ManuallyAssignedItemNames.RemoveAll(item => NamesEqual(item, name));
                }

                target.ItemNames.Add(name);
                EnsureSingleName(target.ManuallyAssignedItemNames, name);
                freeIcons.Remove(name);
                if (movedFromAutoCategory)
                {
                    autoClassificationOriginalPositions.Remove(name);
                }
            }

            return new GroupItemBatchMoveResult(true, movedNames);
        }

        private static IEnumerable<string> DistinctNames(IEnumerable<string> names)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string? name in names)
            {
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                {
                    yield return name;
                }
            }
        }

        private static bool Contains(IEnumerable<string>? names, string itemName) =>
            names?.Any(name => NamesEqual(name, itemName)) == true;

        private static void EnsureSingleName(List<string> names, string itemName)
        {
            int firstIndex = names.FindIndex(name => NamesEqual(name, itemName));
            if (firstIndex < 0)
            {
                names.Add(itemName);
                return;
            }

            for (int index = names.Count - 1; index > firstIndex; index--)
            {
                if (NamesEqual(names[index], itemName))
                {
                    names.RemoveAt(index);
                }
            }
        }

        private static bool SequenceEqual(
            IReadOnlyList<string>? left,
            IReadOnlyList<string> right)
        {
            if (left == null || left.Count != right.Count)
            {
                return false;
            }

            for (int index = 0; index < left.Count; index++)
            {
                if (!NamesEqual(left[index], right[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool NamesEqual(string? left, string? right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
