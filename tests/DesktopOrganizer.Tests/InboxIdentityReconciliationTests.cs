using System.Collections;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class InboxIdentityReconciliationTests
{
    private static readonly InboxClassificationSuggestion Suggestion = new(
        new DesktopCategoryDefinition("documents", "文档", 40),
        ClassificationReliability.Reliable, "测试证据");

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void KnownBaseline_VisitsPreviousSnapshotAtMostOnce(bool shell, bool stableId)
    {
        const int count = 1000;
        var previous = new Dictionary<string, DesktopItemIdentityInfo>();
        var current = new Dictionary<string, DesktopItemIdentityInfo>();
        for (int index = 0; index < count; index++)
        {
            DesktopItemIdentityInfo identity = Identity($"id-{index}", $@"C:\Desktop\Item{index}", shell);
            if (!stableId) identity.FileId = identity.ShellParsingName = null;
            previous.Add($"Old{index}", identity);
            current.Add($"New{index}", identity);
        }
        var counted = new CountingDictionary(previous);
        var inbox = new Dictionary<string, InboxItemInfo>();

        InboxReconcileResult result = Reconcile(inbox, counted, current, baseline: true);

        Assert.IsFalse(result.Changed);
        Assert.AreEqual(0, result.Added);
        Assert.HasCount(0, inbox);
        TestContext.WriteLine($"1000 项已知身份 shell={shell}, stableId={stableId}: 旧表访问 {counted.EntryVisits} 次");
        Assert.IsLessThanOrEqualTo(count, counted.EntryVisits,
            "已知身份核对不应为每个当前项目重新遍历旧身份表。");
    }

    [TestMethod]
    [DataRow("same", false, true)]
    [DataRow("case", false, true)]
    [DataRow("rename", false, true)]
    [DataRow("same", false, false)]
    [DataRow("rename", true, true)]
    [DataRow("case", true, false)]
    public void QueuedItems_VisitCurrentSnapshotAtMostOnce(string naming, bool shell, bool stableId)
    {
        const int count = 1000;
        var inbox = new Dictionary<string, InboxItemInfo>();
        var current = new Dictionary<string, DesktopItemIdentityInfo>();
        for (int index = 0; index < count; index++)
        {
            string name = $"Item{index}";
            DesktopItemIdentityInfo identity = Identity($"id-{index}", $@"C:\Desktop\{name}", shell);
            if (!stableId) identity.FileId = identity.ShellParsingName = null;
            inbox.Add(name, Entry(identity));
            current.Add(naming == "same" ? name : naming == "case" ? name.ToUpperInvariant() : $"Renamed{index}", identity);
        }
        var counted = new CountingDictionary(current);

        InboxReconcileResult result = Reconcile(inbox, new Dictionary<string, DesktopItemIdentityInfo>(), counted);

        Assert.HasCount(count, inbox);
        Assert.AreEqual(0, result.Removed);
        Assert.AreEqual(naming == "same" ? 0 : count, result.Updated);
        foreach (string name in current.Keys)
        {
            Assert.IsTrue(inbox.ContainsKey(name));
            Assert.AreEqual(InboxReviewState.Deferred, inbox[name].ReviewState);
            Assert.AreEqual(DateTime.UnixEpoch, inbox[name].DetectedUtc);
        }
        TestContext.WriteLine($"1000 项队列身份 naming={naming}, shell={shell}, stableId={stableId}: 新表访问 {counted.EntryVisits} 次");
        Assert.IsLessThanOrEqualTo(count, counted.EntryVisits,
            "队列身份核对不应按项目重新遍历当前身份表。");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void KnownIdentity_MatchesPairwiseRulesWithPartialMetadata(bool sameName)
    {
        // 身份关系可能不传递；将索引结果与原逐对判定比较，而非生成统一身份键。
        DesktopItemIdentityInfo[] variants = IdentityVariants();
        foreach (DesktopItemIdentityInfo previous in variants)
        foreach (DesktopItemIdentityInfo current in variants)
        {
            bool expectedKnown = InboxQueueManager.IdentitiesMatch(previous, current) ||
                (sameName && previous.Kind == current.Kind && previous.IsDirectory == current.IsDirectory &&
                 !(previous.Kind == DesktopItemKind.FileSystem &&
                   !string.IsNullOrWhiteSpace(previous.FileId) && !string.IsNullOrWhiteSpace(current.FileId)) &&
                 PathsEqual(previous.LastKnownPath, current.LastKnownPath));
            var inbox = new Dictionary<string, InboxItemInfo>();

            InboxReconcileResult result = Reconcile(inbox,
                new Dictionary<string, DesktopItemIdentityInfo> { ["Report"] = previous },
                new Dictionary<string, DesktopItemIdentityInfo> { [sameName ? "REPORT" : "Other"] = current },
                baseline: true);

            Assert.AreEqual(expectedKnown ? 0 : 1, result.Added,
                $"old={Describe(previous)}; current={Describe(current)}; sameName={sameName}");
        }
    }

    [TestMethod]
    public void QueueMatching_MatchesLinearReferenceWithPartialMetadataAndCaseCollisions()
    {
        DesktopItemIdentityInfo[] variants = IdentityVariants();
        var random = new Random(1309);
        for (int iteration = 0; iteration < 400; iteration++)
        {
            var current = new Dictionary<string, DesktopItemIdentityInfo>(StringComparer.Ordinal);
            int count = 1 + iteration % 7;
            for (int index = 0; index < count; index++)
                current.Add(index == 0 ? "Report" : index == 1 ? "REPORT" : $"Other{index}", variants[random.Next(variants.Length)]);
            DesktopItemIdentityInfo oldIdentity = variants[random.Next(variants.Length)];
            string oldName = iteration % 2 == 0 ? "report" : "Old";
            KeyValuePair<string, DesktopItemIdentityInfo> sameName = current.FirstOrDefault(pair =>
                pair.Key.Equals(oldName, StringComparison.OrdinalIgnoreCase));
            string[] matches = current.Where(pair => InboxQueueManager.IdentitiesMatch(oldIdentity, pair.Value))
                .Select(pair => pair.Key).ToArray();
            string? expectedName = sameName.Value != null && InboxQueueManager.IdentitiesMatch(oldIdentity, sameName.Value)
                ? sameName.Key : matches.Length == 1 ? matches[0] : null;
            var inbox = new Dictionary<string, InboxItemInfo> { [oldName] = Entry(oldIdentity) };

            Reconcile(inbox, new Dictionary<string, DesktopItemIdentityInfo>(), current);

            CollectionAssert.AreEqual(expectedName == null ? Array.Empty<string>() : new[] { expectedName }, inbox.Keys.ToArray(),
                $"iteration={iteration}");
            if (expectedName != null) Assert.AreEqual(InboxReviewState.Deferred, inbox[expectedName].ReviewState);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void StableIdAndPathMatchOneEntry_DoNotCreateFalseAmbiguity(bool shell)
    {
        DesktopItemIdentityInfo identity = Identity("id", @"C:\Desktop\Report", shell);
        var inbox = new Dictionary<string, InboxItemInfo> { ["Old"] = Entry(identity) };

        Reconcile(inbox, new Dictionary<string, DesktopItemIdentityInfo>(),
            new Dictionary<string, DesktopItemIdentityInfo> { ["New"] = identity });

        CollectionAssert.AreEqual(new[] { "New" }, inbox.Keys.ToArray());
        Assert.AreEqual(InboxReviewState.Deferred, inbox["New"].ReviewState);
    }

    [TestMethod]
    [DataRow("Old", 0)]
    [DataRow("REPORT", 1)]
    public void CaseCollidingMatches_PreserveAmbiguityAndFirstSameNamePriority(string oldName, int expectedCount)
    {
        DesktopItemIdentityInfo identity = Identity("id", @"C:\Desktop\Report");
        var inbox = new Dictionary<string, InboxItemInfo> { [oldName] = Entry(identity) };
        var current = new Dictionary<string, DesktopItemIdentityInfo>(StringComparer.Ordinal)
        {
            ["Report"] = identity,
            ["REPORT"] = identity
        };

        Reconcile(inbox, new Dictionary<string, DesktopItemIdentityInfo>(), current);

        Assert.HasCount(expectedCount, inbox);
        if (expectedCount > 0) Assert.AreEqual("Report", inbox.Keys.Single());
    }

    [TestMethod]
    public void NullFirstSameNameEntry_DoesNotHideLaterUniqueIdentity()
    {
        DesktopItemIdentityInfo identity = Identity("id", @"C:\Desktop\Report");
        var inbox = new Dictionary<string, InboxItemInfo> { ["REPORT"] = Entry(identity) };

        Reconcile(inbox, new Dictionary<string, DesktopItemIdentityInfo>(),
            new Dictionary<string, DesktopItemIdentityInfo>(StringComparer.Ordinal)
            {
                ["Report"] = null!,
                ["REPORT"] = identity
            });

        Assert.AreEqual("REPORT", inbox.Keys.Single());
    }

    [TestMethod]
    public void NullCurrentIdentityWithoutSuggestion_IsSkipped()
    {
        var inbox = new Dictionary<string, InboxItemInfo>();
        InboxReconcileResult result = InboxQueueManager.Reconcile(inbox, true, true,
            new Dictionary<string, DesktopItemIdentityInfo>(),
            new Dictionary<string, DesktopItemIdentityInfo> { ["Report"] = null! },
            new Dictionary<string, InboxClassificationSuggestion>(), DateTime.UnixEpoch);

        Assert.IsFalse(result.Changed);
        Assert.AreEqual(0, result.Added);
        Assert.HasCount(0, inbox);
    }

    [TestMethod]
    public void LaterScanWithReplacedIdentity_DoesNotReusePreviousIndex()
    {
        DesktopItemIdentityInfo oldIdentity = Identity("old", @"C:\Desktop\Report");
        var previous = new Dictionary<string, DesktopItemIdentityInfo> { ["Report"] = oldIdentity };
        var current = new Dictionary<string, DesktopItemIdentityInfo>(previous);
        var inbox = new Dictionary<string, InboxItemInfo>();
        Assert.AreEqual(0, Reconcile(inbox, previous, current, baseline: true).Added);
        current["Report"] = Identity("replacement", @"C:\Desktop\Report");

        Assert.AreEqual(1, Reconcile(inbox, previous, current, baseline: true).Added);
        Assert.AreEqual("replacement", inbox["Report"].Identity.FileId);
    }

    [TestMethod]
    public void IncompleteScanWithoutRenames_DoesNotReadIdentityTables()
    {
        var counted = new CountingDictionary(new Dictionary<string, DesktopItemIdentityInfo>
        {
            ["Report"] = Identity("id", @"C:\Desktop\Report")
        });
        InboxReconcileResult result = InboxQueueManager.Reconcile(new(), true, false, counted, counted,
            new Dictionary<string, InboxClassificationSuggestion>(), DateTime.UnixEpoch);

        Assert.IsFalse(result.Changed);
        Assert.AreEqual(0, counted.EntryVisits);
    }

    private static InboxReconcileResult Reconcile(
        Dictionary<string, InboxItemInfo> inbox,
        IReadOnlyDictionary<string, DesktopItemIdentityInfo> previous,
        IReadOnlyDictionary<string, DesktopItemIdentityInfo> current,
        bool baseline = false)
    {
        // Suggestions 使用 Keys，计数器仅测 Reconcile 内的身份条目遍历。
        var suggestions = current.Keys.ToDictionary(name => name, _ => Suggestion, StringComparer.Ordinal);
        return InboxQueueManager.Reconcile(inbox, baseline, true, previous, current, suggestions, DateTime.UnixEpoch.AddDays(1));
    }

    private static InboxItemInfo Entry(DesktopItemIdentityInfo identity) => new()
    {
        Identity = identity,
        DetectedUtc = DateTime.UnixEpoch,
        UpdatedUtc = DateTime.UnixEpoch,
        ReviewState = InboxReviewState.Deferred,
        Reliability = Suggestion.Reliability,
        SuggestedCategoryKey = Suggestion.Category.Key,
        SuggestedCategoryName = Suggestion.Category.DisplayName,
        SuggestedCategoryOrder = Suggestion.Category.Order,
        MatchReason = Suggestion.MatchReason
    };

    private static DesktopItemIdentityInfo Identity(string? id, string path, bool shell = false) => new()
    {
        Kind = shell ? DesktopItemKind.ShellNamespace : DesktopItemKind.FileSystem,
        FileId = shell ? null : id,
        ShellParsingName = shell ? id : null,
        LastKnownPath = path,
        CreationTimeUtcTicks = 100
    };

    private static DesktopItemIdentityInfo[] IdentityVariants() =>
        (from shell in new[] { false, true }
         from folder in new[] { false, true }
         from id in new[] { null, "ID", "id", "different" }
         from ticks in new long?[] { null, 100, 200 }
         from path in new[] { @"C:\Desktop\Report", @" c:\desktop\report\ ", @"D:\Other", " ", "/", "\\" }
         select new DesktopItemIdentityInfo
         {
             Kind = shell ? DesktopItemKind.ShellNamespace : DesktopItemKind.FileSystem,
             IsDirectory = folder,
             FileId = shell ? null : id,
             ShellParsingName = shell ? id : null,
             LastKnownPath = path,
             CreationTimeUtcTicks = ticks
         }).ToArray();

    private static bool PathsEqual(string first, string second) =>
        !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second) &&
        first.Trim().TrimEnd('\\', '/').Equals(second.Trim().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static string Describe(DesktopItemIdentityInfo identity) =>
        $"{identity.Kind}/{identity.IsDirectory}/{identity.FileId ?? identity.ShellParsingName}/{identity.CreationTimeUtcTicks}/{identity.LastKnownPath}";

    private sealed class CountingDictionary(IReadOnlyDictionary<string, DesktopItemIdentityInfo> entries)
        : IReadOnlyDictionary<string, DesktopItemIdentityInfo>
    {
        public int EntryVisits { get; private set; }
        public int Count => entries.Count;
        public DesktopItemIdentityInfo this[string key] => entries[key];
        public IEnumerable<string> Keys => entries.Keys;
        public IEnumerable<DesktopItemIdentityInfo> Values => entries.Values;
        public bool ContainsKey(string key) => entries.ContainsKey(key);
        public bool TryGetValue(string key, out DesktopItemIdentityInfo value) => entries.TryGetValue(key, out value!);
        public IEnumerator<KeyValuePair<string, DesktopItemIdentityInfo>> GetEnumerator()
        {
            foreach (KeyValuePair<string, DesktopItemIdentityInfo> entry in entries)
            {
                EntryVisits++;
                yield return entry;
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
