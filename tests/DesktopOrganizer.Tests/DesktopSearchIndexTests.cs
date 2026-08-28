using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopSearchIndexTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 6, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Search_MultiWordQueryCanMatchAcrossNameTypeGroupAndTags()
    {
        DesktopSearchIndex index = BuildIndex(
            items: [("Quarterly Plan.docx", @"Z:\path-that-does-not-exist\Quarterly Plan.docx")],
            categories: [("Quarterly Plan.docx", Category("documents", "文档"))],
            groups:
            [
                new GroupInfo
                {
                    Id = "work",
                    Name = "工作资料",
                    ItemNames = ["Quarterly Plan.docx"]
                }
            ],
            metadata:
            [
                ("Quarterly Plan.docx", Metadata(tags: ["重要"]))
            ]);

        IReadOnlyList<DesktopSearchResult> results = index.Search(Request(
            "quarterly 文档 工作 重要",
            DesktopSmartView.All));

        Assert.HasCount(1, results);
        Assert.AreEqual("Quarterly Plan.docx", results[0].DisplayName);
        Assert.AreEqual("work", results[0].GroupId);
        Assert.AreEqual("工作资料", results[0].GroupName);
        CollectionAssert.AreEqual(new[] { "重要" }, results[0].Tags.ToArray());
    }

    [TestMethod]
    public void Search_EveryTokenMustMatchSomeIndexedField()
    {
        DesktopSearchIndex index = BuildIndex(
            items: [("Plan.docx", @"not-a-real-location")],
            categories: [("Plan.docx", Category("documents", "文档"))],
            groups: [],
            metadata: [("Plan.docx", Metadata(tags: ["工作"]))]);

        Assert.HasCount(1, index.Search(Request("plan 文档 工作", DesktopSmartView.All)));
        Assert.IsEmpty(index.Search(Request("plan 文档 私人", DesktopSmartView.All)));
    }

    [TestMethod]
    public void Build_OnlyIndexesCurrentSnapshotAndCopiesCallerCollections()
    {
        var tags = new List<string> { "原始标签" };
        var items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Current.txt"] = @"?:\invalid\Current.txt"
        };
        var categories = new Dictionary<string, DesktopCategoryDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Current.txt"] = Category("documents", "文档"),
            ["Ghost.txt"] = Category("documents", "幽灵类型")
        };
        var metadata = new Dictionary<string, DesktopSearchItemMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["Current.txt"] = Metadata(tags),
            ["Ghost.txt"] = Metadata(tags: ["幽灵标签"])
        };

        DesktopSearchIndex index = DesktopSearchIndex.Build(
            items,
            categories,
            Array.Empty<GroupInfo>(),
            metadata);
        tags[0] = "修改后标签";
        items["Later.txt"] = @"?:\invalid\Later.txt";

        IReadOnlyList<DesktopSearchResult> all = index.Search(Request(null, DesktopSmartView.All));

        Assert.HasCount(1, all);
        Assert.AreEqual("Current.txt", all[0].DisplayName);
        CollectionAssert.AreEqual(new[] { "原始标签" }, all[0].Tags.ToArray());
        Assert.IsEmpty(index.Search(Request("幽灵", DesktopSmartView.All)));
        Assert.IsEmpty(index.Search(Request("Later", DesktopSmartView.All)));
    }

    [TestMethod]
    public void Build_MatchesClassificationGroupAndMetadataKeysCaseInsensitively()
    {
        DesktopSearchIndex index = BuildIndex(
            items: [("Report.PDF", @"C:\Desktop\Report.PDF")],
            categories: [("report.pdf", Category("documents", "文档"))],
            groups:
            [
                new GroupInfo
                {
                    Id = "archive",
                    Name = "归档",
                    ItemNames = ["REPORT.pdf"]
                }
            ],
            metadata: [("REPORT.PDF", Metadata(tags: ["完成", "完成", "  稳定  "]))]);

        DesktopSearchResult result = AssertExactlyOne(index.Search(Request(
            "文档 归档 稳定",
            DesktopSmartView.All)));

        Assert.AreEqual("archive", result.GroupId);
        CollectionAssert.AreEqual(new[] { "完成", "稳定" }, result.Tags.ToArray());
    }

    [TestMethod]
    public void Search_UnclassifiedReturnsOnlyItemsOutsideAllGroups()
    {
        DesktopSearchIndex index = BuildIndex(
            items:
            [
                ("Free.txt", @"C:\Desktop\Free.txt"),
                ("Grouped.txt", @"C:\Desktop\Grouped.txt")
            ],
            categories: [],
            groups:
            [
                new GroupInfo
                {
                    Id = "manual",
                    Name = "手工分组",
                    ItemNames = ["Grouped.txt"]
                }
            ],
            metadata: []);

        IReadOnlyList<DesktopSearchResult> results = index.Search(Request(
            null,
            DesktopSmartView.Unclassified));

        Assert.HasCount(1, results);
        Assert.AreEqual("Free.txt", results[0].DisplayName);
    }

    [TestMethod]
    public void Search_RecentlyAddedUsesInclusiveWindowAndRejectsFutureTimestamps()
    {
        DesktopSearchIndex index = BuildIndex(
            items:
            [
                ("Boundary.txt", "boundary"),
                ("Recent.txt", "recent"),
                ("Old.txt", "old"),
                ("Future.txt", "future"),
                ("Unknown.txt", "unknown")
            ],
            categories: [],
            groups: [],
            metadata:
            [
                ("Boundary.txt", Metadata(firstSeenUtc: Now - TimeSpan.FromDays(7))),
                ("Recent.txt", Metadata(firstSeenUtc: Now - TimeSpan.FromHours(1))),
                ("Old.txt", Metadata(firstSeenUtc: Now - TimeSpan.FromDays(7) - TimeSpan.FromTicks(1))),
                ("Future.txt", Metadata(firstSeenUtc: Now + TimeSpan.FromTicks(1)))
            ]);

        IReadOnlyList<DesktopSearchResult> results = index.Search(Request(
            null,
            DesktopSmartView.RecentlyAdded));

        CollectionAssert.AreEqual(
            new[] { "Boundary.txt", "Recent.txt" },
            results.Select(result => result.DisplayName).ToArray());
    }

    [TestMethod]
    public void Search_PendingConfirmationUsesOnlyCallerProvidedFlag()
    {
        DesktopSearchIndex index = BuildIndex(
            items: [("Pending.txt", "pending"), ("Done.txt", "done")],
            categories: [],
            groups: [],
            metadata:
            [
                ("Pending.txt", Metadata(pending: true)),
                ("Done.txt", Metadata(pending: false))
            ]);

        DesktopSearchResult result = AssertExactlyOne(index.Search(Request(
            null,
            DesktopSmartView.PendingConfirmation)));

        Assert.AreEqual("Pending.txt", result.DisplayName);
        Assert.IsTrue(result.IsPendingConfirmation);
    }

    [TestMethod]
    public void Search_RecentlyMovedUsesCallerProvidedTimestampAndCustomWindow()
    {
        DesktopSearchIndex index = BuildIndex(
            items: [("Moved.txt", "moved"), ("Older.txt", "older")],
            categories: [],
            groups: [],
            metadata:
            [
                ("Moved.txt", Metadata(lastMovedUtc: Now - TimeSpan.FromHours(12))),
                ("Older.txt", Metadata(lastMovedUtc: Now - TimeSpan.FromDays(2)))
            ]);

        DesktopSearchResult result = AssertExactlyOne(index.Search(new DesktopSearchRequest(
            Text: null,
            View: DesktopSmartView.RecentlyMoved,
            UtcNow: Now,
            Limit: 50,
            RecentWindow: TimeSpan.FromDays(1))));

        Assert.AreEqual("Moved.txt", result.DisplayName);
        Assert.AreEqual(Now - TimeSpan.FromHours(12), result.LastMovedUtc);
    }

    [TestMethod]
    public void Search_RanksNameMatchesBeforeMetadataMatchesThenUsesStableOrderingAndLimit()
    {
        DesktopSearchIndex first = BuildIndex(
            items:
            [
                ("Zulu.txt", "4"),
                ("Alpha notes.txt", "2"),
                ("Alpha", "1"),
                ("Beta.txt", "3")
            ],
            categories: [],
            groups:
            [
                new GroupInfo { Id = "alpha-group", Name = "Alpha", ItemNames = ["Zulu.txt"] }
            ],
            metadata: [("Beta.txt", Metadata(tags: ["Alpha"]))]);
        DesktopSearchIndex second = BuildIndex(
            items:
            [
                ("Beta.txt", "3"),
                ("Alpha", "1"),
                ("Alpha notes.txt", "2"),
                ("Zulu.txt", "4")
            ],
            categories: [],
            groups:
            [
                new GroupInfo { Id = "alpha-group", Name = "Alpha", ItemNames = ["Zulu.txt"] }
            ],
            metadata: [("Beta.txt", Metadata(tags: ["Alpha"]))]);

        string[] firstNames = first.Search(new DesktopSearchRequest(
                "alpha",
                DesktopSmartView.All,
                Now,
                Limit: 3))
            .Select(result => result.DisplayName)
            .ToArray();
        string[] secondNames = second.Search(new DesktopSearchRequest(
                "alpha",
                DesktopSmartView.All,
                Now,
                Limit: 3))
            .Select(result => result.DisplayName)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Alpha", "Alpha notes.txt", "Beta.txt" }, firstNames);
        CollectionAssert.AreEqual(firstNames, secondNames);
    }

    [TestMethod]
    public void Search_EmptyQueryUsesStableCaseInsensitiveThenOrdinalOrdering()
    {
        DesktopSearchIndex index = BuildIndex(
            items: [("beta", "3"), ("alpha", "2"), ("Alpha", "1")],
            categories: [],
            groups: [],
            metadata: []);

        IReadOnlyList<DesktopSearchResult> results = index.Search(Request(null, DesktopSmartView.All));

        // 仅大小写不同的名称属于非法桌面快照；索引固定保留 Ordinal 排序靠前的一个。
        CollectionAssert.AreEqual(
            new[] { "Alpha", "beta" },
            results.Select(result => result.DisplayName).ToArray());
    }

    [TestMethod]
    public void Search_NonPositiveLimitReturnsNoResults()
    {
        DesktopSearchIndex index = BuildIndex(
            items: [("One.txt", "one")],
            categories: [],
            groups: [],
            metadata: []);

        Assert.IsEmpty(index.Search(new DesktopSearchRequest(
            null,
            DesktopSmartView.All,
            Now,
            Limit: 0)));
    }

    [TestMethod]
    public void Search_NonPositiveRecentWindowIsRejected()
    {
        DesktopSearchIndex index = BuildIndex(
            items: [("One.txt", "one")],
            categories: [],
            groups: [],
            metadata: []);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => index.Search(
            new DesktopSearchRequest(
                null,
                DesktopSmartView.RecentlyAdded,
                Now,
                Limit: 50,
                RecentWindow: TimeSpan.Zero)));
    }

    private static DesktopSearchIndex BuildIndex(
        IEnumerable<(string Name, string Location)> items,
        IEnumerable<(string Name, DesktopCategoryDefinition Category)> categories,
        IEnumerable<GroupInfo> groups,
        IEnumerable<(string Name, DesktopSearchItemMetadata Metadata)> metadata)
    {
        return DesktopSearchIndex.Build(
            items.ToDictionary(
                item => item.Name,
                item => item.Location,
                StringComparer.Ordinal),
            categories.ToDictionary(
                item => item.Name,
                item => item.Category,
                StringComparer.Ordinal),
            groups.ToList(),
            metadata.ToDictionary(
                item => item.Name,
                item => item.Metadata,
                StringComparer.Ordinal));
    }

    private static DesktopCategoryDefinition Category(string key, string displayName) =>
        new(key, displayName, Order: 0);

    private static DesktopSearchItemMetadata Metadata(
        IReadOnlyList<string>? tags = null,
        DateTimeOffset? firstSeenUtc = null,
        DateTimeOffset? lastMovedUtc = null,
        bool pending = false) =>
        new(
            tags ?? Array.Empty<string>(),
            firstSeenUtc,
            lastMovedUtc,
            pending);

    private static DesktopSearchResult AssertExactlyOne(IReadOnlyList<DesktopSearchResult> results)
    {
        Assert.HasCount(1, results);
        return results[0];
    }

    private static DesktopSearchRequest Request(string? text, DesktopSmartView view) =>
        new(text, view, Now);
}
