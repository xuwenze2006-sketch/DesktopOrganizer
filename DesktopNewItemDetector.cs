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
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> next,
            IReadOnlyDictionary<string, string>? confirmedRenames = null)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string>? confirmedRenameTargets = confirmedRenames?.Values
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach ((string nextName, DesktopItemIdentityInfo nextIdentity) in next)
            {
                if (confirmedRenameTargets?.Contains(nextName) == true)
                {
                    continue;
                }

                // 旧名称在同一刷新批次中被另一实体重新占用时，不能再用旧路径回退
                // 把它当作原项目；已确认的原项目正在上面的目标名称中。
                if (confirmedRenames?.ContainsKey(nextName) == true)
                {
                    result.Add(nextName);
                    continue;
                }

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
