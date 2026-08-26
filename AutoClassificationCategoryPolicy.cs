namespace DesktopOrganizer
{
    internal static class AutoClassificationCategoryPolicy
    {
        public static Dictionary<string, DesktopCategoryDefinition> BuildEffectiveLookup(
            IReadOnlyDictionary<string, DesktopCategoryDefinition> scannedCategories,
            IReadOnlySet<string> reliableCategoryNames,
            IEnumerable<GroupInfo> groups)
        {
            var result = new Dictionary<string, DesktopCategoryDefinition>(
                scannedCategories,
                StringComparer.OrdinalIgnoreCase);

            foreach (GroupInfo group in groups.Where(candidate =>
                         candidate.IsAutoCategory &&
                         !string.IsNullOrWhiteSpace(candidate.AutoCategoryKey)))
            {
                foreach (string name in group.ItemNames)
                {
                    if (reliableCategoryNames.Contains(name) ||
                        !result.TryGetValue(name, out DesktopCategoryDefinition? fallbackCategory) ||
                        fallbackCategory is null)
                    {
                        continue;
                    }

                    result[name] = new DesktopCategoryDefinition(
                        group.AutoCategoryKey!,
                        string.IsNullOrWhiteSpace(group.Name) ? fallbackCategory.DisplayName : group.Name,
                        fallbackCategory.Order);
                }
            }

            return result;
        }
    }
}
