namespace DesktopOrganizer
{
    internal sealed record GroupItemSelectionPlan(
        IReadOnlyList<string> ItemNames,
        bool Select)
    {
        public int ItemCount => ItemNames.Count;
    }

    internal sealed record GroupRangeSelectionAnchor(
        string GroupId,
        string ItemName);

    internal sealed record GroupRangeSelectionPlan(
        IReadOnlyList<string> ItemNames,
        bool UsedAnchor)
    {
        public int ItemCount => ItemNames.Count;
    }

    /// <summary>
    /// 分类框整组选取的纯集合策略。只考虑当前仍存在的桌面项目，并保留组外选择。
    /// </summary>
    internal static class GroupItemSelectionPolicy
    {
        public static GroupItemSelectionPlan CreatePlan(
            IEnumerable<string> groupItemNames,
            IEnumerable<string> existingItemNames,
            IReadOnlySet<string> selectedItemNames)
        {
            ArgumentNullException.ThrowIfNull(groupItemNames);
            ArgumentNullException.ThrowIfNull(existingItemNames);
            ArgumentNullException.ThrowIfNull(selectedItemNames);

            var existing = new HashSet<string>(existingItemNames, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> itemNames = groupItemNames
                .Where(name =>
                    !string.IsNullOrWhiteSpace(name) &&
                    existing.Contains(name) &&
                    seen.Add(name))
                .ToList();
            bool select = itemNames.Any(name => !selectedItemNames.Contains(name));
            return new GroupItemSelectionPlan(itemNames, select);
        }

        public static int Apply(
            ISet<string> selectedItemNames,
            GroupItemSelectionPlan plan)
        {
            ArgumentNullException.ThrowIfNull(selectedItemNames);
            ArgumentNullException.ThrowIfNull(plan);

            int changedCount = 0;
            foreach (string name in plan.ItemNames)
            {
                bool changed = plan.Select
                    ? selectedItemNames.Add(name)
                    : selectedItemNames.Remove(name);
                if (changed)
                {
                    changedCount++;
                }
            }
            return changedCount;
        }
    }

    /// <summary>
    /// 按调用方提供的当前显示顺序创建同组连续选择计划。范围只做并集，
    /// 不清除组外或范围外的已有选择。
    /// </summary>
    internal static class GroupRangeSelectionPolicy
    {
        public static GroupRangeSelectionPlan CreatePlan(
            string targetGroupId,
            IEnumerable<string> orderedItemNames,
            IReadOnlySet<string> selectedItemNames,
            GroupRangeSelectionAnchor? anchor,
            string endpointItemName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetGroupId);
            ArgumentNullException.ThrowIfNull(orderedItemNames);
            ArgumentNullException.ThrowIfNull(selectedItemNames);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> orderedNames = orderedItemNames
                .Where(name => !string.IsNullOrWhiteSpace(name) && seen.Add(name))
                .ToList();
            int endpointIndex = orderedNames.FindIndex(name =>
                name.Equals(endpointItemName, StringComparison.OrdinalIgnoreCase));
            if (endpointIndex < 0)
            {
                return new GroupRangeSelectionPlan([], UsedAnchor: false);
            }

            int anchorIndex = -1;
            if (anchor != null &&
                anchor.GroupId.Equals(targetGroupId, StringComparison.OrdinalIgnoreCase) &&
                selectedItemNames.Any(name =>
                    name.Equals(anchor.ItemName, StringComparison.OrdinalIgnoreCase)))
            {
                anchorIndex = orderedNames.FindIndex(name =>
                    name.Equals(anchor.ItemName, StringComparison.OrdinalIgnoreCase));
            }

            if (anchorIndex < 0)
            {
                return new GroupRangeSelectionPlan(
                    [orderedNames[endpointIndex]],
                    UsedAnchor: false);
            }

            int firstIndex = Math.Min(anchorIndex, endpointIndex);
            int itemCount = Math.Abs(anchorIndex - endpointIndex) + 1;
            return new GroupRangeSelectionPlan(
                orderedNames.GetRange(firstIndex, itemCount),
                UsedAnchor: true);
        }

        public static int Apply(
            ISet<string> selectedItemNames,
            GroupRangeSelectionPlan plan)
        {
            ArgumentNullException.ThrowIfNull(selectedItemNames);
            ArgumentNullException.ThrowIfNull(plan);

            int changedCount = 0;
            foreach (string name in plan.ItemNames)
            {
                bool alreadySelected = selectedItemNames.Any(selected =>
                    selected.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (!alreadySelected && selectedItemNames.Add(name))
                {
                    changedCount++;
                }
            }
            return changedCount;
        }
    }
}
