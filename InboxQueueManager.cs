namespace DesktopOrganizer
{
    /// <summary>待整理项目当前所处的人工审阅状态。</summary>
    internal enum InboxReviewState
    {
        Pending,
        Deferred
    }

    /// <summary>
    /// 分类建议的证据可靠性。Conservative 只能用于展示，不能触发接受或自动归组。
    /// </summary>
    internal enum ClassificationReliability
    {
        Conservative,
        Reliable
    }

    /// <summary>一个待整理项目及最近一次完整扫描提供的分类建议。</summary>
    internal sealed class InboxItemInfo
    {
        public DesktopItemIdentityInfo Identity { get; set; } = new();
        public DateTime DetectedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public string SuggestedCategoryKey { get; set; } = string.Empty;
        public string SuggestedCategoryName { get; set; } = string.Empty;
        public int SuggestedCategoryOrder { get; set; }
        public string MatchReason { get; set; } = string.Empty;
        public ClassificationReliability Reliability { get; set; }
        public InboxReviewState ReviewState { get; set; }
    }

    /// <summary>扫描阶段交给收件箱的分类建议；不包含任何文件系统操作。</summary>
    internal sealed record InboxClassificationSuggestion(
        DesktopCategoryDefinition Category,
        ClassificationReliability Reliability,
        string MatchReason);

    internal sealed record InboxReconcileResult(
        bool BaselineEstablished,
        bool BaselineChanged,
        bool Changed,
        int Added,
        int Updated,
        int Removed);

    internal sealed record InboxAcceptancePlan(
        string DisplayName,
        DesktopCategoryDefinition TargetCategory,
        DesktopItemIdentityInfo ExpectedIdentity);

    internal enum InboxActionOutcome
    {
        Planned,
        Applied,
        NoChange,
        NotFound,
        StaleIdentity,
        Unreliable,
        ManualGroupProtected,
        InvalidManualGroup
    }

    internal sealed record InboxActionResult(
        InboxActionOutcome Outcome,
        bool Changed)
    {
        public bool Succeeded => Outcome is
            InboxActionOutcome.Planned or
            InboxActionOutcome.Applied or
            InboxActionOutcome.NoChange;
    }

    /// <summary>
    /// 待整理收件箱的纯内存模型服务。调用方负责提供已经完成的桌面扫描、分类建议和当前布局；
    /// 本类型不枚举目录、不读取文件，也不执行移动、重命名或删除。
    /// </summary>
    internal static class InboxQueueManager
    {
        private const string MissingEvidenceReason = "当前完整扫描未提供分类证据";

        public static InboxReconcileResult Reconcile(
            Dictionary<string, InboxItemInfo> inbox,
            bool baselineEstablished,
            bool completeScan,
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> previousIdentities,
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> currentIdentities,
            IReadOnlyDictionary<string, InboxClassificationSuggestion> suggestions,
            DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(inbox);
            ArgumentNullException.ThrowIfNull(previousIdentities);
            ArgumentNullException.ThrowIfNull(currentIdentities);
            ArgumentNullException.ThrowIfNull(suggestions);

            if (!completeScan)
            {
                return new InboxReconcileResult(
                    baselineEstablished,
                    BaselineChanged: false,
                    Changed: false,
                    Added: 0,
                    Updated: 0,
                    Removed: 0);
            }

            Dictionary<string, InboxItemInfo> working = CloneInbox(inbox);
            int added = 0;
            int updated = 0;
            int removed = 0;

            foreach ((string oldName, InboxItemInfo oldEntry) in working.ToList())
            {
                string? currentName = FindUniqueCurrentName(
                    oldName,
                    oldEntry.Identity,
                    currentIdentities);
                if (currentName == null)
                {
                    working.Remove(oldName);
                    removed++;
                    continue;
                }

                InboxItemInfo refreshed = Clone(oldEntry);
                refreshed.Identity = Clone(currentIdentities[currentName]);
                if (TryGetActualEntry(
                        suggestions,
                        currentName,
                        out InboxClassificationSuggestion? suggestion) &&
                    suggestion is not null)
                {
                    ApplySuggestion(refreshed, suggestion);
                }
                else
                {
                    refreshed.Reliability = ClassificationReliability.Conservative;
                    refreshed.MatchReason = MissingEvidenceReason;
                }

                bool keyChanged = !string.Equals(oldName, currentName, StringComparison.Ordinal);
                bool valueChanged = !InboxEntriesEqual(oldEntry, refreshed);
                if (keyChanged || valueChanged)
                {
                    refreshed.UpdatedUtc = utcNow;
                }

                if (keyChanged)
                {
                    working.Remove(oldName);
                    working[currentName] = refreshed;
                }
                else
                {
                    working[oldName] = refreshed;
                }

                if (keyChanged || valueChanged)
                {
                    updated++;
                }
            }

            if (baselineEstablished)
            {
                foreach ((string currentName, DesktopItemIdentityInfo currentIdentity) in currentIdentities)
                {
                    if (IsKnownIdentity(currentName, currentIdentity, previousIdentities) ||
                        !TryGetActualEntry(
                            suggestions,
                            currentName,
                            out InboxClassificationSuggestion? suggestion) ||
                        suggestion is null)
                    {
                        continue;
                    }

                    string? existingKey = FindActualKey(working, currentName);
                    if (existingKey != null)
                    {
                        working.Remove(existingKey);
                    }

                    var entry = new InboxItemInfo
                    {
                        Identity = Clone(currentIdentity),
                        DetectedUtc = utcNow,
                        UpdatedUtc = utcNow,
                        ReviewState = InboxReviewState.Pending
                    };
                    ApplySuggestion(entry, suggestion);
                    working[currentName] = entry;
                    added++;
                }
            }

            bool changed = !InboxDictionariesEqual(inbox, working);
            if (changed)
            {
                inbox.Clear();
                foreach ((string name, InboxItemInfo entry) in working)
                {
                    inbox[name] = Clone(entry);
                }
            }

            return new InboxReconcileResult(
                BaselineEstablished: true,
                BaselineChanged: !baselineEstablished,
                Changed: changed,
                Added: added,
                Updated: updated,
                Removed: removed);
        }

        public static InboxActionResult TryCreateAcceptancePlan(
            IReadOnlyDictionary<string, InboxItemInfo> inbox,
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> currentIdentities,
            IEnumerable<GroupInfo> groups,
            string displayName,
            out InboxAcceptancePlan? plan)
        {
            ArgumentNullException.ThrowIfNull(inbox);
            ArgumentNullException.ThrowIfNull(currentIdentities);
            ArgumentNullException.ThrowIfNull(groups);
            plan = null;

            if (!TryGetCurrentEntry(
                    inbox,
                    currentIdentities,
                    displayName,
                    out string? actualName,
                    out InboxItemInfo? entry,
                    out DesktopItemIdentityInfo? currentIdentity,
                    out InboxActionResult rejection))
            {
                return rejection;
            }

            if (entry!.Reliability != ClassificationReliability.Reliable)
            {
                return new InboxActionResult(InboxActionOutcome.Unreliable, Changed: false);
            }

            if (groups.Any(group =>
                    group is not null &&
                    !group.IsAutoCategory &&
                    string.IsNullOrWhiteSpace(group.UserRuleId) &&
                    group.ItemNames?.Contains(actualName!, StringComparer.OrdinalIgnoreCase) == true))
            {
                return new InboxActionResult(
                    InboxActionOutcome.ManualGroupProtected,
                    Changed: false);
            }

            plan = new InboxAcceptancePlan(
                actualName!,
                new DesktopCategoryDefinition(
                    entry.SuggestedCategoryKey,
                    entry.SuggestedCategoryName,
                    entry.SuggestedCategoryOrder),
                Clone(currentIdentity!));
            return new InboxActionResult(InboxActionOutcome.Planned, Changed: false);
        }

        public static IReadOnlyList<InboxAcceptancePlan> PlanAutomaticAcceptances(
            IReadOnlyDictionary<string, InboxItemInfo> inbox,
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> currentIdentities,
            IEnumerable<GroupInfo> groups,
            bool paused)
        {
            ArgumentNullException.ThrowIfNull(inbox);
            ArgumentNullException.ThrowIfNull(currentIdentities);
            ArgumentNullException.ThrowIfNull(groups);
            if (paused)
            {
                return Array.Empty<InboxAcceptancePlan>();
            }

            List<GroupInfo> groupList = groups.Where(group => group is not null).ToList();
            var plans = new List<InboxAcceptancePlan>();
            foreach ((string displayName, InboxItemInfo entry) in inbox
                         .OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                if (entry is null || entry.ReviewState != InboxReviewState.Pending)
                {
                    continue;
                }

                InboxActionResult result = TryCreateAcceptancePlan(
                    inbox,
                    currentIdentities,
                    groupList,
                    displayName,
                    out InboxAcceptancePlan? plan);
                if (result.Outcome == InboxActionOutcome.Planned && plan is not null)
                {
                    plans.Add(plan);
                }
            }

            return plans;
        }

        public static InboxActionResult CompleteAcceptance(
            Dictionary<string, InboxItemInfo> inbox,
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> currentIdentities,
            InboxAcceptancePlan plan)
        {
            ArgumentNullException.ThrowIfNull(inbox);
            ArgumentNullException.ThrowIfNull(currentIdentities);
            ArgumentNullException.ThrowIfNull(plan);

            if (!TryGetCurrentEntry(
                    inbox,
                    currentIdentities,
                    plan.DisplayName,
                    out string? actualName,
                    out InboxItemInfo? entry,
                    out DesktopItemIdentityInfo? currentIdentity,
                    out InboxActionResult rejection))
            {
                return rejection;
            }

            if (!IdentitiesMatch(entry!.Identity, plan.ExpectedIdentity) ||
                !IdentitiesMatch(currentIdentity!, plan.ExpectedIdentity))
            {
                return new InboxActionResult(InboxActionOutcome.StaleIdentity, Changed: false);
            }

            inbox.Remove(FindActualKey(inbox, actualName!)!);
            return new InboxActionResult(InboxActionOutcome.Applied, Changed: true);
        }

        public static InboxActionResult Defer(
            Dictionary<string, InboxItemInfo> inbox,
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> currentIdentities,
            string displayName,
            DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(inbox);
            ArgumentNullException.ThrowIfNull(currentIdentities);
            if (!TryGetCurrentEntry(
                    inbox,
                    currentIdentities,
                    displayName,
                    out string? actualName,
                    out InboxItemInfo? entry,
                    out _,
                    out InboxActionResult rejection))
            {
                return rejection;
            }

            if (entry!.ReviewState == InboxReviewState.Deferred)
            {
                return new InboxActionResult(InboxActionOutcome.NoChange, Changed: false);
            }

            entry.ReviewState = InboxReviewState.Deferred;
            entry.UpdatedUtc = utcNow;
            inbox[FindActualKey(inbox, actualName!)!] = entry;
            return new InboxActionResult(InboxActionOutcome.Applied, Changed: true);
        }

        public static InboxActionResult LeaveOnDesktop(
            Dictionary<string, InboxItemInfo> inbox,
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> currentIdentities,
            string displayName)
        {
            ArgumentNullException.ThrowIfNull(inbox);
            ArgumentNullException.ThrowIfNull(currentIdentities);
            if (!TryGetCurrentEntry(
                    inbox,
                    currentIdentities,
                    displayName,
                    out string? actualName,
                    out _,
                    out _,
                    out InboxActionResult rejection))
            {
                return rejection;
            }

            inbox.Remove(FindActualKey(inbox, actualName!)!);
            return new InboxActionResult(InboxActionOutcome.Applied, Changed: true);
        }

        public static InboxActionResult TryMoveToManualGroup(
            Dictionary<string, InboxItemInfo> inbox,
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> currentIdentities,
            List<GroupInfo> groups,
            Dictionary<string, IconPosition> freeIcons,
            Dictionary<string, IconPosition> autoClassificationOriginalPositions,
            string displayName,
            string targetGroupId)
        {
            ArgumentNullException.ThrowIfNull(inbox);
            ArgumentNullException.ThrowIfNull(currentIdentities);
            ArgumentNullException.ThrowIfNull(groups);
            ArgumentNullException.ThrowIfNull(freeIcons);
            ArgumentNullException.ThrowIfNull(autoClassificationOriginalPositions);

            if (!TryGetCurrentEntry(
                    inbox,
                    currentIdentities,
                    displayName,
                    out string? actualName,
                    out _,
                    out _,
                    out InboxActionResult rejection))
            {
                return rejection;
            }

            GroupInfo? target = groups.FirstOrDefault(group =>
                group is not null &&
                !string.IsNullOrWhiteSpace(group.Id) &&
                group.Id.Equals(targetGroupId, StringComparison.OrdinalIgnoreCase));
            if (target == null || target.IsAutoCategory ||
                !string.IsNullOrWhiteSpace(target.UserRuleId) || target.ItemNames == null ||
                groups.Any(group => group is null || group.ItemNames == null))
            {
                return new InboxActionResult(
                    InboxActionOutcome.InvalidManualGroup,
                    Changed: false);
            }

            foreach (GroupInfo group in groups)
            {
                group.ItemNames.RemoveAll(item =>
                    item.Equals(actualName, StringComparison.OrdinalIgnoreCase));
            }
            if (!target.ItemNames.Contains(actualName!, StringComparer.OrdinalIgnoreCase))
            {
                target.ItemNames.Add(actualName!);
            }

            freeIcons.Remove(FindActualKey(freeIcons, actualName!) ?? actualName!);
            autoClassificationOriginalPositions.Remove(
                FindActualKey(autoClassificationOriginalPositions, actualName!) ?? actualName!);
            inbox.Remove(FindActualKey(inbox, actualName!)!);
            return new InboxActionResult(InboxActionOutcome.Applied, Changed: true);
        }

        internal static bool IdentitiesMatch(
            DesktopItemIdentityInfo first,
            DesktopItemIdentityInfo second)
        {
            if (first is null || second is null ||
                first.Kind != second.Kind ||
                first.IsDirectory != second.IsDirectory)
            {
                return false;
            }

            if (first.Kind == DesktopItemKind.ShellNamespace)
            {
                if (!string.IsNullOrWhiteSpace(first.ShellParsingName) &&
                    !string.IsNullOrWhiteSpace(second.ShellParsingName))
                {
                    return first.ShellParsingName.Equals(
                        second.ShellParsingName,
                        StringComparison.OrdinalIgnoreCase);
                }

                return PersistedPathsEqual(first.LastKnownPath, second.LastKnownPath);
            }

            bool firstHasFileId = !string.IsNullOrWhiteSpace(first.FileId);
            bool secondHasFileId = !string.IsNullOrWhiteSpace(second.FileId);
            if (firstHasFileId && secondHasFileId)
            {
                return first.FileId!.Equals(second.FileId, StringComparison.OrdinalIgnoreCase) &&
                    (!first.CreationTimeUtcTicks.HasValue ||
                     !second.CreationTimeUtcTicks.HasValue ||
                     first.CreationTimeUtcTicks.Value == second.CreationTimeUtcTicks.Value);
            }

            return PersistedPathsEqual(first.LastKnownPath, second.LastKnownPath);
        }

        private static bool TryGetCurrentEntry(
            IReadOnlyDictionary<string, InboxItemInfo> inbox,
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> currentIdentities,
            string displayName,
            out string? actualName,
            out InboxItemInfo? entry,
            out DesktopItemIdentityInfo? currentIdentity,
            out InboxActionResult rejection)
        {
            actualName = FindActualKey(inbox, displayName);
            entry = null;
            currentIdentity = null;
            if (actualName == null || !inbox.TryGetValue(actualName, out entry) || entry is null)
            {
                rejection = new InboxActionResult(InboxActionOutcome.NotFound, Changed: false);
                return false;
            }

            string? currentName = FindActualKey(currentIdentities, actualName);
            if (currentName == null ||
                !currentIdentities.TryGetValue(currentName, out currentIdentity) ||
                currentIdentity is null ||
                !IdentitiesMatch(entry.Identity, currentIdentity))
            {
                rejection = new InboxActionResult(InboxActionOutcome.StaleIdentity, Changed: false);
                return false;
            }

            actualName = currentName;
            rejection = new InboxActionResult(InboxActionOutcome.NoChange, Changed: false);
            return true;
        }

        private static string? FindUniqueCurrentName(
            string oldName,
            DesktopItemIdentityInfo oldIdentity,
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> currentIdentities)
        {
            if (TryGetActualEntry(
                    currentIdentities,
                    oldName,
                    out DesktopItemIdentityInfo? sameNameIdentity) &&
                sameNameIdentity is not null &&
                IdentitiesMatch(oldIdentity, sameNameIdentity))
            {
                return FindActualKey(currentIdentities, oldName);
            }

            List<string> matches = currentIdentities
                .Where(pair => pair.Value is not null && IdentitiesMatch(oldIdentity, pair.Value))
                .Select(pair => pair.Key)
                .Take(2)
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private static bool IsKnownIdentity(
            string currentName,
            DesktopItemIdentityInfo currentIdentity,
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> previousIdentities)
        {
            foreach ((string previousName, DesktopItemIdentityInfo previousIdentity) in previousIdentities)
            {
                if (previousIdentity is null)
                {
                    continue;
                }

                if (IdentitiesMatch(previousIdentity, currentIdentity))
                {
                    return true;
                }

                if (previousName.Equals(currentName, StringComparison.OrdinalIgnoreCase) &&
                    SamePathFallbackMatches(previousIdentity, currentIdentity))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SamePathFallbackMatches(
            DesktopItemIdentityInfo previous,
            DesktopItemIdentityInfo current)
        {
            if (previous.Kind != current.Kind || previous.IsDirectory != current.IsDirectory)
            {
                return false;
            }

            bool bothHaveStablePhysicalIds = previous.Kind == DesktopItemKind.FileSystem &&
                !string.IsNullOrWhiteSpace(previous.FileId) &&
                !string.IsNullOrWhiteSpace(current.FileId);
            return !bothHaveStablePhysicalIds &&
                PersistedPathsEqual(previous.LastKnownPath, current.LastKnownPath);
        }

        private static bool PersistedPathsEqual(string? first, string? second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            {
                return false;
            }

            return first.Trim().TrimEnd('\\', '/').Equals(
                second.Trim().TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplySuggestion(
            InboxItemInfo entry,
            InboxClassificationSuggestion suggestion)
        {
            ArgumentNullException.ThrowIfNull(suggestion.Category);
            entry.SuggestedCategoryKey = suggestion.Category.Key ?? string.Empty;
            entry.SuggestedCategoryName = suggestion.Category.DisplayName ?? string.Empty;
            entry.SuggestedCategoryOrder = suggestion.Category.Order;
            entry.Reliability = Enum.IsDefined(suggestion.Reliability)
                ? suggestion.Reliability
                : ClassificationReliability.Conservative;
            entry.MatchReason = string.IsNullOrWhiteSpace(suggestion.MatchReason)
                ? MissingEvidenceReason
                : suggestion.MatchReason.Trim();
        }

        private static Dictionary<string, InboxItemInfo> CloneInbox(
            IReadOnlyDictionary<string, InboxItemInfo> source)
        {
            var clone = new Dictionary<string, InboxItemInfo>(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, InboxItemInfo entry) in source)
            {
                if (!string.IsNullOrWhiteSpace(name) && entry is not null)
                {
                    clone[name] = Clone(entry);
                }
            }
            return clone;
        }

        private static InboxItemInfo Clone(InboxItemInfo entry) => new()
        {
            Identity = Clone(entry.Identity),
            DetectedUtc = entry.DetectedUtc,
            UpdatedUtc = entry.UpdatedUtc,
            SuggestedCategoryKey = entry.SuggestedCategoryKey,
            SuggestedCategoryName = entry.SuggestedCategoryName,
            SuggestedCategoryOrder = entry.SuggestedCategoryOrder,
            MatchReason = entry.MatchReason,
            Reliability = entry.Reliability,
            ReviewState = entry.ReviewState
        };

        private static DesktopItemIdentityInfo Clone(DesktopItemIdentityInfo identity) => new()
        {
            Kind = identity.Kind,
            LastKnownPath = identity.LastKnownPath,
            FileId = identity.FileId,
            ShellParsingName = identity.ShellParsingName,
            CreationTimeUtcTicks = identity.CreationTimeUtcTicks,
            LastWriteTimeUtcTicks = identity.LastWriteTimeUtcTicks,
            IsDirectory = identity.IsDirectory
        };

        private static bool InboxDictionariesEqual(
            IReadOnlyDictionary<string, InboxItemInfo> first,
            IReadOnlyDictionary<string, InboxItemInfo> second)
        {
            if (first.Count != second.Count)
            {
                return false;
            }

            foreach ((string name, InboxItemInfo firstEntry) in first)
            {
                string? secondName = FindActualKey(second, name);
                if (secondName == null ||
                    !string.Equals(name, secondName, StringComparison.Ordinal) ||
                    !second.TryGetValue(secondName, out InboxItemInfo? secondEntry) ||
                    secondEntry is null ||
                    !InboxEntriesEqual(firstEntry, secondEntry))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool InboxEntriesEqual(InboxItemInfo first, InboxItemInfo second) =>
            IdentitiesExactlyEqual(first.Identity, second.Identity) &&
            first.DetectedUtc == second.DetectedUtc &&
            first.UpdatedUtc == second.UpdatedUtc &&
            string.Equals(first.SuggestedCategoryKey, second.SuggestedCategoryKey, StringComparison.Ordinal) &&
            string.Equals(first.SuggestedCategoryName, second.SuggestedCategoryName, StringComparison.Ordinal) &&
            first.SuggestedCategoryOrder == second.SuggestedCategoryOrder &&
            string.Equals(first.MatchReason, second.MatchReason, StringComparison.Ordinal) &&
            first.Reliability == second.Reliability &&
            first.ReviewState == second.ReviewState;

        private static bool IdentitiesExactlyEqual(
            DesktopItemIdentityInfo first,
            DesktopItemIdentityInfo second) =>
            first.Kind == second.Kind &&
            string.Equals(first.LastKnownPath, second.LastKnownPath, StringComparison.Ordinal) &&
            string.Equals(first.FileId, second.FileId, StringComparison.Ordinal) &&
            string.Equals(first.ShellParsingName, second.ShellParsingName, StringComparison.Ordinal) &&
            first.CreationTimeUtcTicks == second.CreationTimeUtcTicks &&
            first.LastWriteTimeUtcTicks == second.LastWriteTimeUtcTicks &&
            first.IsDirectory == second.IsDirectory;

        private static bool TryGetActualEntry<T>(
            IReadOnlyDictionary<string, T> dictionary,
            string key,
            out T? value)
        {
            string? actualKey = FindActualKey(dictionary, key);
            if (actualKey != null && dictionary.TryGetValue(actualKey, out value))
            {
                return true;
            }

            value = default;
            return false;
        }

        private static string? FindActualKey<T>(
            IEnumerable<KeyValuePair<string, T>> dictionary,
            string key) =>
            string.IsNullOrWhiteSpace(key)
                ? null
                : dictionary.Select(pair => pair.Key).FirstOrDefault(candidate =>
                    candidate.Equals(key, StringComparison.OrdinalIgnoreCase));
    }
}
