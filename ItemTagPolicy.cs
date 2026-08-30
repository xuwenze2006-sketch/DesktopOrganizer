namespace DesktopOrganizer
{
    internal static class ItemTagPolicy
    {
        private static readonly char[] EditorSeparators = [',', '，', ';', '；', '\t', '\r', '\n'];

        public static List<string> ParseEditorText(string? text) => Normalize(
            (text ?? string.Empty).Split(
                EditorSeparators,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        public static List<string> Normalize(IEnumerable<string>? tags) =>
            (tags ?? Array.Empty<string>())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        public static string FormatEditorText(IEnumerable<string>? tags) =>
            string.Join("，", Normalize(tags));

        public static Dictionary<string, List<string>> NormalizeDictionary(
            IEnumerable<KeyValuePair<string, List<string>>> source)
        {
            ArgumentNullException.ThrowIfNull(source);
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, List<string>? tags) in source)
            {
                if (string.IsNullOrWhiteSpace(name) || tags == null)
                {
                    continue;
                }

                List<string> normalized = Normalize(tags);
                if (normalized.Count > 0)
                {
                    result[name] = normalized;
                }
            }
            return result;
        }

        public static bool SetTags(
            Dictionary<string, List<string>> itemTags,
            string displayName,
            IEnumerable<string>? tags)
        {
            ArgumentNullException.ThrowIfNull(itemTags);
            ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
            string? existingKey = FindKey(itemTags, displayName);
            List<string> normalized = Normalize(tags);
            if (normalized.Count == 0)
            {
                return existingKey != null && itemTags.Remove(existingKey);
            }

            if (existingKey != null &&
                itemTags[existingKey].SequenceEqual(normalized, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            itemTags[existingKey ?? displayName] = normalized;
            return true;
        }

        public static bool AddTag(
            Dictionary<string, List<string>> itemTags,
            string displayName,
            string tag) =>
            AddTags(itemTags, displayName, [tag]);

        public static bool AddTags(
            Dictionary<string, List<string>> itemTags,
            string displayName,
            IEnumerable<string>? tags)
        {
            ArgumentNullException.ThrowIfNull(itemTags);
            List<string> additions = Normalize(tags);
            if (additions.Count == 0)
            {
                return false;
            }

            string? existingKey = FindKey(itemTags, displayName);
            var combined = existingKey == null
                ? new List<string>()
                : itemTags[existingKey].ToList();
            combined.AddRange(additions);
            return SetTags(itemTags, displayName, combined);
        }

        public static bool RemoveTags(
            Dictionary<string, List<string>> itemTags,
            string displayName,
            IEnumerable<string>? tags)
        {
            ArgumentNullException.ThrowIfNull(itemTags);
            string? existingKey = FindKey(itemTags, displayName);
            List<string> removals = Normalize(tags);
            if (existingKey == null || removals.Count == 0)
            {
                return false;
            }

            var removalSet = new HashSet<string>(removals, StringComparer.OrdinalIgnoreCase);
            List<string> existing = itemTags[existingKey];
            List<string> remaining = existing
                .Where(tag => !removalSet.Contains(tag))
                .ToList();
            return remaining.Count != existing.Count &&
                   SetTags(itemTags, existingKey, remaining);
        }

        private static string? FindKey(
            IReadOnlyDictionary<string, List<string>> itemTags,
            string displayName) =>
            itemTags.Keys.FirstOrDefault(key =>
                key.Equals(displayName, StringComparison.OrdinalIgnoreCase));
    }
}
