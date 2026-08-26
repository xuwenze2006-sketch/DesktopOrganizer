namespace DesktopOrganizer
{
    /// <summary>
    /// 根据上一份已接受的持久化身份与本次完整快照判断真正新增的桌面项目。
    /// 新项目资格与是否有可用网格、是否已经生成自由坐标无关。
    /// </summary>
    internal static class DesktopNewItemDetector
    {
        public static HashSet<string> FindNewItemNames(
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> current,
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> next)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string nextName, DesktopItemIdentityInfo nextIdentity) in next)
            {
                if (IsKnownItem(nextName, nextIdentity, current))
                {
                    continue;
                }

                result.Add(nextName);
            }

            return result;
        }

        private static bool IsKnownItem(
            string nextName,
            DesktopItemIdentityInfo nextIdentity,
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> current)
        {
            foreach ((string currentName, DesktopItemIdentityInfo currentIdentity) in current)
            {
                if (StableIdentityMatches(currentIdentity, nextIdentity))
                {
                    return true;
                }

                if (currentName.Equals(nextName, StringComparison.OrdinalIgnoreCase) &&
                    SamePathFallbackMatches(currentIdentity, nextIdentity))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool StableIdentityMatches(
            DesktopItemIdentityInfo current,
            DesktopItemIdentityInfo next)
        {
            if (current.Kind != next.Kind)
            {
                return false;
            }

            if (current.Kind == DesktopItemKind.ShellNamespace)
            {
                return !string.IsNullOrWhiteSpace(current.ShellParsingName) &&
                    !string.IsNullOrWhiteSpace(next.ShellParsingName) &&
                    current.ShellParsingName.Equals(
                        next.ShellParsingName,
                        StringComparison.OrdinalIgnoreCase);
            }

            if (string.IsNullOrWhiteSpace(current.FileId) ||
                string.IsNullOrWhiteSpace(next.FileId) ||
                !current.FileId.Equals(next.FileId, StringComparison.OrdinalIgnoreCase) ||
                current.IsDirectory != next.IsDirectory)
            {
                return false;
            }

            return !current.CreationTimeUtcTicks.HasValue ||
                !next.CreationTimeUtcTicks.HasValue ||
                current.CreationTimeUtcTicks.Value == next.CreationTimeUtcTicks.Value;
        }

        private static bool SamePathFallbackMatches(
            DesktopItemIdentityInfo current,
            DesktopItemIdentityInfo next)
        {
            if (current.Kind != next.Kind || current.IsDirectory != next.IsDirectory)
            {
                return false;
            }

            bool bothHaveStablePhysicalIds = current.Kind == DesktopItemKind.FileSystem &&
                !string.IsNullOrWhiteSpace(current.FileId) &&
                !string.IsNullOrWhiteSpace(next.FileId);
            if (bothHaveStablePhysicalIds)
            {
                return false;
            }

            return ShellItemLocation.AreEquivalent(current.LastKnownPath, next.LastKnownPath);
        }
    }
}
