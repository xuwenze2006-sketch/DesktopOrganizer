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
                    (!IdentityMetadataEquals(currentIdentity, nextIdentity) &&
                     !CanReuseSharedExtensionIcon(currentLocation, currentIdentity, nextIdentity)))
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

        private static bool CanReuseSharedExtensionIcon(
            string location,
            DesktopItemIdentityInfo first,
            DesktopItemIdentityInfo second)
        {
            // ext: 使用文件属性提取类型图标，内容变化不影响图像。只在可靠的同一文件上
            // 忽略修改时间；缺失身份、替换、目录和按文件提取的图标仍按原规则失效。
            return first.Kind == DesktopItemKind.FileSystem &&
                   !first.IsDirectory &&
                   !string.IsNullOrWhiteSpace(first.FileId) &&
                   first.CreationTimeUtcTicks > DateTime.UnixEpoch.Ticks &&
                   first.LastWriteTimeUtcTicks > DateTime.UnixEpoch.Ticks &&
                   second.LastWriteTimeUtcTicks > DateTime.UnixEpoch.Ticks &&
                   IdentityMetadataEquals(first, second, compareLastWriteTime: false) &&
                   DesktopItemTypeLookup.UsesSharedExtensionIcon(Path.GetExtension(location));
        }

        private static bool IdentityMetadataEquals(
            DesktopItemIdentityInfo first,
            DesktopItemIdentityInfo second,
            bool compareLastWriteTime = true)
        {
            return first.Kind == second.Kind &&
                   ShellItemLocation.AreEquivalent(first.LastKnownPath, second.LastKnownPath) &&
                   string.Equals(first.FileId, second.FileId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(first.ShellParsingName, second.ShellParsingName, StringComparison.OrdinalIgnoreCase) &&
                   first.CreationTimeUtcTicks == second.CreationTimeUtcTicks &&
                   (!compareLastWriteTime || first.LastWriteTimeUtcTicks == second.LastWriteTimeUtcTicks) &&
                   first.IsDirectory == second.IsDirectory;
        }
    }
}
