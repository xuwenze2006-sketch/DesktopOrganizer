namespace DesktopOrganizer
{
    /// <summary>桌面搜索内置视图。所有视图都只投影调用方提供的当前桌面快照。</summary>
    internal enum DesktopSmartView
    {
        All,
        Unclassified,
        RecentlyAdded,
        PendingConfirmation,
        RecentlyMoved
    }

    /// <summary>
    /// 调用方在构建索引时提供的项目元数据。搜索组件不会自行读取文件、目录或正文。
    /// </summary>
    internal sealed record DesktopSearchItemMetadata(
        IReadOnlyList<string> Tags,
        DateTimeOffset? FirstSeenUtc = null,
        DateTimeOffset? LastMovedUtc = null,
        bool IsPendingConfirmation = false);

    internal sealed record DesktopSearchRequest(
        string? Text,
        DesktopSmartView View,
        DateTimeOffset UtcNow,
        int Limit = 50,
        TimeSpan? RecentWindow = null);

    internal sealed record DesktopSearchResult(
        string DisplayName,
        string Location,
        string TypeKey,
        string TypeDisplayName,
        string? GroupId,
        string? GroupName,
        IReadOnlyList<string> Tags,
        DateTimeOffset? FirstSeenUtc,
        DateTimeOffset? LastMovedUtc,
        bool IsPendingConfirmation);

    /// <summary>
    /// 当前桌面项目的内存搜索索引。构建后会复制所需字段，既不持有可变布局集合，
    /// 也不访问文件系统；刷新桌面后由调用方用新快照重建即可。
    /// </summary>
    internal sealed class DesktopSearchIndex
    {
        private static readonly TimeSpan DefaultRecentWindow = TimeSpan.FromDays(7);
        private readonly IReadOnlyList<IndexedDesktopItem> _items;

        private DesktopSearchIndex(IReadOnlyList<IndexedDesktopItem> items)
        {
            _items = items;
        }

        public static DesktopSearchIndex Build(
            IReadOnlyDictionary<string, string> currentDesktopItems,
            IReadOnlyDictionary<string, DesktopCategoryDefinition> classifications,
            IEnumerable<GroupInfo> groups,
            IReadOnlyDictionary<string, DesktopSearchItemMetadata> metadataByItem)
        {
            ArgumentNullException.ThrowIfNull(currentDesktopItems);
            ArgumentNullException.ThrowIfNull(classifications);
            ArgumentNullException.ThrowIfNull(groups);
            ArgumentNullException.ThrowIfNull(metadataByItem);

            Dictionary<string, List<DesktopSearchGroup>> groupsByItem = BuildGroupLookup(groups);
            Dictionary<string, DesktopCategoryDefinition> normalizedClassifications = classifications
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => pair.Value)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);
            Dictionary<string, DesktopSearchItemMetadata> normalizedMetadata = metadataByItem
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => pair.Value)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);

            var indexedItems = new List<IndexedDesktopItem>(currentDesktopItems.Count);
            IEnumerable<KeyValuePair<string, string>> orderedItems = currentDesktopItems
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .ThenBy(pair => pair.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.Value ?? string.Empty, StringComparer.Ordinal);
            var indexedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach ((string displayName, string? location) in orderedItems)
            {
                // Windows 桌面不允许仅大小写不同的两个显示名。若调用方仍传入这种
                // 非法快照，固定保留排序后的第一项，避免结果依赖字典枚举顺序。
                if (!indexedNames.Add(displayName))
                {
                    continue;
                }

                normalizedClassifications.TryGetValue(
                    displayName,
                    out DesktopCategoryDefinition? classification);
                normalizedMetadata.TryGetValue(displayName, out DesktopSearchItemMetadata? metadata);
                groupsByItem.TryGetValue(displayName, out List<DesktopSearchGroup>? itemGroups);

                IReadOnlyList<string> tags = NormalizeTags(metadata?.Tags);
                IReadOnlyList<DesktopSearchGroup> copiedGroups = itemGroups is null
                    ? Array.Empty<DesktopSearchGroup>()
                    : itemGroups.ToArray();
                indexedItems.Add(new IndexedDesktopItem(
                    displayName,
                    location ?? string.Empty,
                    classification?.Key ?? string.Empty,
                    classification?.DisplayName ?? string.Empty,
                    copiedGroups,
                    tags,
                    metadata?.FirstSeenUtc,
                    metadata?.LastMovedUtc,
                    metadata?.IsPendingConfirmation == true));
            }

            return new DesktopSearchIndex(indexedItems);
        }

        public IReadOnlyList<DesktopSearchResult> Search(DesktopSearchRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Limit <= 0)
            {
                return Array.Empty<DesktopSearchResult>();
            }

            TimeSpan recentWindow = request.RecentWindow ?? DefaultRecentWindow;
            if (recentWindow <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    "近期时间窗口必须大于零。");
            }

            IReadOnlyList<string> tokens = Tokenize(request.Text);
            DateTimeOffset utcNow = request.UtcNow.ToUniversalTime();
            DateTimeOffset recentThreshold = utcNow - recentWindow;

            return _items
                .Where(item => MatchesView(item, request.View, recentThreshold, utcNow))
                .Select(item => TryScore(item, tokens, out int score)
                    ? new ScoredDesktopItem(item, score)
                    : null)
                .OfType<ScoredDesktopItem>()
                .OrderByDescending(result => result.Score)
                .ThenBy(result => result.Item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(result => result.Item.DisplayName, StringComparer.Ordinal)
                .ThenBy(result => result.Item.Location, StringComparer.OrdinalIgnoreCase)
                .ThenBy(result => result.Item.Location, StringComparer.Ordinal)
                .Take(request.Limit)
                .Select(result => result.Item.ToResult())
                .ToList();
        }

        private static Dictionary<string, List<DesktopSearchGroup>> BuildGroupLookup(
            IEnumerable<GroupInfo> groups)
        {
            var result = new Dictionary<string, List<DesktopSearchGroup>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (GroupInfo group in groups
                         .Where(group => group != null)
                         .OrderBy(group => group.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(group => group.Name ?? string.Empty, StringComparer.Ordinal)
                         .ThenBy(group => group.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(group => group.Id ?? string.Empty, StringComparer.Ordinal))
            {
                string groupId = group.Id ?? string.Empty;
                string groupName = group.Name ?? string.Empty;
                foreach (string itemName in (group.ItemNames ?? new List<string>())
                             .Where(name => !string.IsNullOrWhiteSpace(name))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!result.TryGetValue(itemName, out List<DesktopSearchGroup>? itemGroups))
                    {
                        itemGroups = new List<DesktopSearchGroup>();
                        result[itemName] = itemGroups;
                    }

                    if (!itemGroups.Any(existing =>
                            existing.Id.Equals(groupId, StringComparison.OrdinalIgnoreCase)))
                    {
                        itemGroups.Add(new DesktopSearchGroup(groupId, groupName));
                    }
                }
            }

            return result;
        }

        private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags)
        {
            if (tags == null || tags.Count == 0)
            {
                return Array.Empty<string>();
            }

            return tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .ThenBy(tag => tag, StringComparer.Ordinal)
                .ToArray();
        }

        private static IReadOnlyList<string> Tokenize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<string>();
            }

            var tokens = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var token = new StringBuilder();

            void CommitToken()
            {
                if (token.Length == 0)
                {
                    return;
                }

                string value = token.ToString();
                token.Clear();
                if (seen.Add(value))
                {
                    tokens.Add(value);
                }
            }

            foreach (char character in text)
            {
                if (char.IsWhiteSpace(character))
                {
                    CommitToken();
                }
                else
                {
                    token.Append(character);
                }
            }

            CommitToken();
            return tokens;
        }

        private static bool MatchesView(
            IndexedDesktopItem item,
            DesktopSmartView view,
            DateTimeOffset recentThreshold,
            DateTimeOffset utcNow)
        {
            return view switch
            {
                DesktopSmartView.All => true,
                DesktopSmartView.Unclassified => item.Groups.Count == 0,
                DesktopSmartView.RecentlyAdded => IsRecent(
                    item.FirstSeenUtc,
                    recentThreshold,
                    utcNow),
                DesktopSmartView.PendingConfirmation => item.IsPendingConfirmation,
                DesktopSmartView.RecentlyMoved => IsRecent(
                    item.LastMovedUtc,
                    recentThreshold,
                    utcNow),
                _ => throw new ArgumentOutOfRangeException(nameof(view), view, "未知的智能视图。")
            };
        }

        private static bool IsRecent(
            DateTimeOffset? timestamp,
            DateTimeOffset threshold,
            DateTimeOffset utcNow)
        {
            if (!timestamp.HasValue)
            {
                return false;
            }

            DateTimeOffset utcTimestamp = timestamp.Value.ToUniversalTime();
            return utcTimestamp >= threshold && utcTimestamp <= utcNow;
        }

        private static bool TryScore(
            IndexedDesktopItem item,
            IReadOnlyList<string> tokens,
            out int score)
        {
            score = 0;
            foreach (string token in tokens)
            {
                int tokenScore = ScoreField(item.DisplayName, token, exact: 400, prefix: 300, contains: 200);
                tokenScore = Math.Max(
                    tokenScore,
                    ScoreFields(item.Tags, token, exact: 160, prefix: 150, contains: 140));
                tokenScore = Math.Max(
                    tokenScore,
                    ScoreFields(
                        item.Groups.Select(group => group.Name),
                        token,
                        exact: 130,
                        prefix: 120,
                        contains: 110));
                tokenScore = Math.Max(
                    tokenScore,
                    ScoreField(item.TypeDisplayName, token, exact: 100, prefix: 90, contains: 80));
                tokenScore = Math.Max(
                    tokenScore,
                    ScoreField(item.TypeKey, token, exact: 100, prefix: 90, contains: 80));

                if (tokenScore == 0)
                {
                    score = 0;
                    return false;
                }

                score += tokenScore;
            }

            return true;
        }

        private static int ScoreFields(
            IEnumerable<string> values,
            string token,
            int exact,
            int prefix,
            int contains)
        {
            int score = 0;
            foreach (string value in values)
            {
                score = Math.Max(score, ScoreField(value, token, exact, prefix, contains));
            }
            return score;
        }

        private static int ScoreField(
            string? value,
            string token,
            int exact,
            int prefix,
            int contains)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            if (value.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                return exact;
            }

            if (value.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                return prefix;
            }

            return value.Contains(token, StringComparison.OrdinalIgnoreCase) ? contains : 0;
        }

        private sealed record DesktopSearchGroup(string Id, string Name);

        private sealed record IndexedDesktopItem(
            string DisplayName,
            string Location,
            string TypeKey,
            string TypeDisplayName,
            IReadOnlyList<DesktopSearchGroup> Groups,
            IReadOnlyList<string> Tags,
            DateTimeOffset? FirstSeenUtc,
            DateTimeOffset? LastMovedUtc,
            bool IsPendingConfirmation)
        {
            public DesktopSearchResult ToResult()
            {
                DesktopSearchGroup? primaryGroup = Groups.FirstOrDefault();
                return new DesktopSearchResult(
                    DisplayName,
                    Location,
                    TypeKey,
                    TypeDisplayName,
                    primaryGroup?.Id,
                    primaryGroup?.Name,
                    Tags.ToArray(),
                    FirstSeenUtc,
                    LastMovedUtc,
                    IsPendingConfirmation);
            }
        }

        private sealed record ScoredDesktopItem(IndexedDesktopItem Item, int Score);
    }
}
