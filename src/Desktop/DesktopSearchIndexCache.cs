namespace DesktopOrganizer
{
    /// <summary>
    /// 在界面线程比较搜索输入的独立快照，未变化时复用索引。
    /// 不依赖布局修改事件，因此原地修改和工作区整体替换都能被下一次查询发现。
    /// </summary>
    internal sealed class DesktopSearchIndexCache
    {
        private InputSnapshot? _snapshot;
        private DesktopSearchIndex? _index;

        public DesktopSearchIndex GetOrBuild(
            Dictionary<string, string> items,
            Dictionary<string, DesktopCategoryDefinition> categories,
            AppLayoutData layout)
        {
            if (_snapshot?.Matches(items, categories, layout) == true)
            {
                return _index!;
            }

            var metadata = items.Keys.ToDictionary(
                name => name,
                name => new DesktopSearchItemMetadata(
                    layout.ItemTags.TryGetValue(name, out List<string>? tags)
                        ? tags
                        : Array.Empty<string>(),
                    TryReadMetadataTime(layout.ItemFirstSeenUtcTicks, name),
                    TryReadMetadataTime(layout.ItemLastMovedUtcTicks, name),
                    layout.InboxItems.ContainsKey(name)),
                StringComparer.OrdinalIgnoreCase);
            DesktopSearchIndex index = DesktopSearchIndex.Build(items, categories, layout.Groups, metadata);
            var snapshot = new InputSnapshot(items, categories, layout);
            _snapshot = snapshot;
            _index = index;
            return index;
        }

        public void Clear()
        {
            _snapshot = null;
            _index = null;
        }

        private static DateTimeOffset? TryReadMetadataTime(
            IReadOnlyDictionary<string, long> source, string name)
        {
            if (!source.TryGetValue(name, out long ticks) ||
                ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            {
                return null;
            }
            return new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
        }

        private sealed class InputSnapshot
        {
            private readonly DictionarySnapshot<string> _items;
            private readonly DictionarySnapshot<DesktopCategoryDefinition> _categories;
            private readonly DictionarySnapshot<List<string>> _tags;
            private readonly DictionarySnapshot<long> _firstSeen;
            private readonly DictionarySnapshot<long> _lastMoved;
            private readonly HashSet<string> _inboxNames;
            private readonly IEqualityComparer<string> _inboxComparer;
            private readonly GroupSnapshot?[] _groups;

            public InputSnapshot(
                Dictionary<string, string> items,
                Dictionary<string, DesktopCategoryDefinition> categories,
                AppLayoutData layout)
            {
                _items = new(items);
                _categories = new(categories);
                _tags = new(layout.ItemTags, tags => tags is null ? null! : new List<string>(tags));
                _firstSeen = new(layout.ItemFirstSeenUtcTicks);
                _lastMoved = new(layout.ItemLastMovedUtcTicks);
                _inboxNames = new(layout.InboxItems.Keys, StringComparer.Ordinal);
                _inboxComparer = layout.InboxItems.Comparer;
                _groups = layout.Groups.Select(group => group is null ? null : new GroupSnapshot(
                    group.Id, group.Name, group.ItemNames?.ToArray() ?? [])).ToArray();
            }

            public bool Matches(
                Dictionary<string, string> items,
                Dictionary<string, DesktopCategoryDefinition> categories,
                AppLayoutData layout)
            {
                if (!_items.Matches(items) || !_categories.Matches(categories) ||
                    !_tags.Matches(layout.ItemTags, TagsMatch) ||
                    !_firstSeen.Matches(layout.ItemFirstSeenUtcTicks) ||
                    !_lastMoved.Matches(layout.ItemLastMovedUtcTicks) ||
                    !_inboxComparer.Equals(layout.InboxItems.Comparer) ||
                    _inboxNames.Count != layout.InboxItems.Count ||
                    _groups.Length != layout.Groups.Count)
                {
                    return false;
                }

                foreach (string name in layout.InboxItems.Keys)
                {
                    if (!_inboxNames.Contains(name)) return false;
                }

                for (int index = 0; index < _groups.Length; index++)
                {
                    GroupSnapshot? previous = _groups[index];
                    GroupInfo? current = layout.Groups[index];
                    if (previous is null || current is null)
                    {
                        if (previous is not null || current is not null) return false;
                        continue;
                    }
                    if (!string.Equals(previous.Id, current.Id, StringComparison.Ordinal) ||
                        !string.Equals(previous.Name, current.Name, StringComparison.Ordinal) ||
                        !NamesMatch(previous.ItemNames, current.ItemNames))
                    {
                        return false;
                    }
                }
                return true;
            }

            private static bool TagsMatch(List<string>? previous, List<string>? current) =>
                previous is null ? current is null : current is not null && NamesMatch(previous, current);

            private static bool NamesMatch(IReadOnlyList<string> previous, IReadOnlyList<string>? current)
            {
                if (previous.Count != (current?.Count ?? 0)) return false;
                for (int index = 0; index < previous.Count; index++)
                {
                    if (!string.Equals(previous[index], current![index], StringComparison.Ordinal)) return false;
                }
                return true;
            }

            private sealed record GroupSnapshot(string? Id, string? Name, string[] ItemNames);
        }

        private sealed class DictionarySnapshot<TValue>
        {
            private readonly Dictionary<string, TValue> _entries;
            private readonly IEqualityComparer<string> _comparer;

            public DictionarySnapshot(Dictionary<string, TValue> source, Func<TValue, TValue>? copy = null)
            {
                // Ordinal 保留原始键大小写；源比较器另外保存，避免遗漏查找语义变化。
                _entries = new(source.Count, StringComparer.Ordinal);
                foreach ((string name, TValue value) in source)
                    _entries.Add(name, copy is null ? value : copy(value));
                _comparer = source.Comparer;
            }

            public bool Matches(Dictionary<string, TValue> source, Func<TValue, TValue, bool>? equals = null)
            {
                if (!_comparer.Equals(source.Comparer) || _entries.Count != source.Count) return false;
                foreach ((string name, TValue value) in source)
                {
                    if (!_entries.TryGetValue(name, out TValue? previous) ||
                        !(equals is null ? EqualityComparer<TValue>.Default.Equals(previous, value) : equals(previous, value)))
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }
}
