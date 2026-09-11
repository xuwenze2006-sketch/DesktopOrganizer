namespace DesktopOrganizer
{
    internal static class IconCacheInvalidation
    {
        public static HashSet<string> FindChangedLocations(
            IReadOnlyDictionary<string, string> currentItems,
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> currentIdentities,
            IReadOnlyDictionary<string, string> nextItems,
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> nextIdentities)
        {
            ArgumentNullException.ThrowIfNull(currentItems);
            ArgumentNullException.ThrowIfNull(currentIdentities);
            ArgumentNullException.ThrowIfNull(nextItems);
            ArgumentNullException.ThrowIfNull(nextIdentities);

            var changedLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, string currentLocation) in currentItems)
            {
                if (!nextItems.TryGetValue(name, out string? nextLocation))
                {
                    changedLocations.Add(currentLocation);
                    continue;
                }

                if (!ShellItemLocation.AreEquivalent(currentLocation, nextLocation))
                {
                    changedLocations.Add(currentLocation);
                    changedLocations.Add(nextLocation);
                    continue;
                }

                if (!currentIdentities.TryGetValue(name, out DesktopItemIdentityInfo? currentIdentity) ||
                    !nextIdentities.TryGetValue(name, out DesktopItemIdentityInfo? nextIdentity) ||
                    !IdentityMetadataEquals(currentIdentity, nextIdentity))
                {
                    changedLocations.Add(currentLocation);
                    changedLocations.Add(nextLocation);
                }
            }

            foreach ((string name, string nextLocation) in nextItems)
            {
                if (!currentItems.ContainsKey(name))
                {
                    changedLocations.Add(nextLocation);
                }
            }

            return changedLocations;
        }

        public static IReadOnlyList<string> GetCandidateCacheKeys(string location)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(location);
            if (ShellItemLocation.TryDecode(location, out string parsingName, out _))
            {
                return ["shell:" + parsingName];
            }

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "folder:" + location,
                "path:" + location
            };
            string extension = Path.GetExtension(location).ToLowerInvariant();
            if (!string.IsNullOrEmpty(extension))
            {
                keys.Add("ext:" + extension);
            }

            return keys.ToList();
        }

        private static bool IdentityMetadataEquals(
            DesktopItemIdentityInfo first,
            DesktopItemIdentityInfo second)
        {
            return first.Kind == second.Kind &&
                   ShellItemLocation.AreEquivalent(first.LastKnownPath, second.LastKnownPath) &&
                   string.Equals(first.FileId, second.FileId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(first.ShellParsingName, second.ShellParsingName, StringComparison.OrdinalIgnoreCase) &&
                   first.CreationTimeUtcTicks == second.CreationTimeUtcTicks &&
                   first.LastWriteTimeUtcTicks == second.LastWriteTimeUtcTicks &&
                   first.IsDirectory == second.IsDirectory;
        }
    }
}
