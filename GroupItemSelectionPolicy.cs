namespace DesktopOrganizer
{
    internal sealed record GroupItemSelectionPlan(
        IReadOnlyList<string> ItemNames,
        bool Select)
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
}
