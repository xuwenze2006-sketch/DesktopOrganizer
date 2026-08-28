namespace DesktopOrganizer
{
    internal static class SmartLayoutGroupOrderingPolicy
    {
        public static List<GroupInfo> Order(
            IEnumerable<GroupInfo> groups,
            Func<GroupInfo, double> displayAreaSelector)
        {
            ArgumentNullException.ThrowIfNull(groups);
            ArgumentNullException.ThrowIfNull(displayAreaSelector);

            return groups
                .OrderByDescending(group => group.ItemNames.Count)
                .ThenByDescending(displayAreaSelector)
                .ThenBy(group => group.IsAutoCategory ? 1 : 0)
                .ThenBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }
}
