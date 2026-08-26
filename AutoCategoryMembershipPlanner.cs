namespace DesktopOrganizer
{
    internal sealed record AutoCategoryMembershipMove(
        string ItemName,
        string SourceGroupId,
        DesktopCategoryDefinition TargetCategory);

    internal sealed record AutoCategoryMembershipUpdateResult(
        bool Changed,
        IReadOnlyList<GroupInfo> AffectedGroups);

    /// <summary>
    /// 只规划已经处于自动分类框中的项目迁移；自由图标和手工分组不在输入范围内。
    /// </summary>
    internal static class AutoCategoryMembershipPlanner
    {
        public static IReadOnlyList<AutoCategoryMembershipMove> Plan(
            IEnumerable<GroupInfo> groups,
            IReadOnlyDictionary<string, DesktopCategoryDefinition> categories,
            IReadOnlySet<string> reliableCategoryNames,
            bool paused)
        {
            if (paused)
            {
                return Array.Empty<AutoCategoryMembershipMove>();
            }

            var moves = new List<AutoCategoryMembershipMove>();
            foreach (GroupInfo group in groups.Where(candidate => candidate.IsAutoCategory))
            {
                foreach (string name in group.ItemNames)
                {
                    if (!reliableCategoryNames.Contains(name) ||
                        !categories.TryGetValue(name, out DesktopCategoryDefinition? targetCategory) ||
                        targetCategory is null ||
                        string.Equals(
                            group.AutoCategoryKey,
                            targetCategory.Key,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    moves.Add(new AutoCategoryMembershipMove(name, group.Id, targetCategory));
                }
            }

            return moves;
        }

        public static AutoCategoryMembershipUpdateResult Apply(
            List<GroupInfo> groups,
            IReadOnlyList<AutoCategoryMembershipMove> moves,
            Func<string, bool> itemExists,
            Func<DesktopCategoryDefinition, GroupInfo> createTargetGroup)
        {
            var appliedMoves = new List<AutoCategoryMembershipMove>();
            var affectedGroups = new HashSet<GroupInfo>();
            foreach (AutoCategoryMembershipMove move in moves)
            {
                if (!itemExists(move.ItemName))
                {
                    continue;
                }

                GroupInfo? source = groups.FirstOrDefault(group =>
                    group.IsAutoCategory &&
                    group.Id.Equals(move.SourceGroupId, StringComparison.OrdinalIgnoreCase));
                if (source == null)
                {
                    continue;
                }

                int removed = source.ItemNames.RemoveAll(name =>
                    name.Equals(move.ItemName, StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                {
                    continue;
                }

                appliedMoves.Add(move);
                affectedGroups.Add(source);
            }

            if (appliedMoves.Count == 0)
            {
                return new AutoCategoryMembershipUpdateResult(
                    Changed: false,
                    Array.Empty<GroupInfo>());
            }

            var targetCategoryKeys = new HashSet<string>(
                appliedMoves.Select(move => move.TargetCategory.Key),
                StringComparer.OrdinalIgnoreCase);
            groups.RemoveAll(group =>
                group.IsAutoCategory &&
                group.ItemNames.Count == 0 &&
                !targetCategoryKeys.Contains(group.AutoCategoryKey ?? string.Empty));

            foreach (IGrouping<string, AutoCategoryMembershipMove> targetMoves in appliedMoves.GroupBy(
                         move => move.TargetCategory.Key,
                         StringComparer.OrdinalIgnoreCase))
            {
                DesktopCategoryDefinition category = targetMoves.First().TargetCategory;
                GroupInfo? target = groups.LastOrDefault(group =>
                    group.IsAutoCategory &&
                    string.Equals(
                        group.AutoCategoryKey,
                        category.Key,
                        StringComparison.OrdinalIgnoreCase));
                if (target == null)
                {
                    target = createTargetGroup(category);
                    groups.Add(target);
                }

                foreach (string name in targetMoves.Select(move => move.ItemName))
                {
                    if (!target.ItemNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        target.ItemNames.Add(name);
                    }
                }

                target.ItemNames = target.ItemNames
                    .Where(itemExists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                affectedGroups.Add(target);
            }

            groups.RemoveAll(group =>
                group.IsAutoCategory && group.ItemNames.Count == 0);
            return new AutoCategoryMembershipUpdateResult(
                Changed: true,
                affectedGroups.Where(groups.Contains).ToList());
        }
    }
}
