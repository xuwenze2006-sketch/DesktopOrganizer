using System.Reflection;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopSearchCacheTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Cache_ReusesEqualInputsAcrossRequestsAndRefreshesTimeViews()
    {
        var cache = new DesktopSearchIndexCache();
        var items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Report.txt"] = "test:report" };
        var categories = new Dictionary<string, DesktopCategoryDefinition>(StringComparer.OrdinalIgnoreCase);
        DateTimeOffset now = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
        var layout = new AppLayoutData
        {
            Groups = [new GroupInfo { Id = "work", Name = "工作", ItemNames = ["Report.txt"] }],
            ItemTags = new() { ["Report.txt"] = ["重要"] },
            ItemFirstSeenUtcTicks = new() { ["Report.txt"] = now.AddDays(-7).UtcTicks }
        };
        DesktopSearchIndex index = cache.GetOrBuild(items, categories, layout);
        Assert.HasCount(1, index.Search(new("report", DesktopSmartView.All, now)));
        Assert.HasCount(1, index.Search(new(null, DesktopSmartView.RecentlyAdded, now)));
        Assert.IsEmpty(index.Search(new(null, DesktopSmartView.RecentlyAdded, now.AddTicks(1))));

        // 值相同的新布局/集合，以及无关几何变化，都不应重新构建搜索数据。
        var replacement = new AppLayoutData
        {
            Groups = [new GroupInfo { Id = "work", Name = "工作", ItemNames = ["Report.txt"], X = 300, IsCollapsed = true }],
            ItemTags = new() { ["Report.txt"] = ["重要"] },
            ItemFirstSeenUtcTicks = new() { ["Report.txt"] = now.AddDays(-7).UtcTicks }
        };
        Assert.AreSame(index, cache.GetOrBuild(new(items, items.Comparer), new(categories, categories.Comparer), replacement));
        replacement.ItemTags["Report.txt"][0] = "完成";
        Assert.AreNotSame(index, cache.GetOrBuild(items, categories, replacement));
    }

    [TestMethod]
    public void Cache_ClearReleasesTheReusableIndex()
    {
        var cache = new DesktopSearchIndexCache();
        var items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var categories = new Dictionary<string, DesktopCategoryDefinition>(StringComparer.OrdinalIgnoreCase);
        var layout = new AppLayoutData();
        DesktopSearchIndex before = cache.GetOrBuild(items, categories, layout);
        Assert.AreSame(before, cache.GetOrBuild(items, categories, layout));

        cache.Clear();

        Assert.AreNotSame(before, cache.GetOrBuild(items, categories, layout));
    }

    [STATestMethod]
    public void LoadedSearch_UpdatesDesktopNamesPathsAndClassification()
    {
        using var fixture = new Fixture();
        fixture.Seed();
        Assert.AreEqual("文档", fixture.Search("report").Single().TypeDisplayName);

        fixture.Items["Report.txt"] = "new location";
        Assert.AreEqual("new location", fixture.Search("report").Single().Location);
        fixture.Categories["Report.txt"] = new("images", "文档", 1);
        Assert.AreEqual("images", fixture.Search("report").Single().TypeKey);
        fixture.Categories["Report.txt"] = new("images", "图片", 1);
        DesktopSearchResult changed = fixture.Search("report 图片").Single();
        Assert.AreEqual("new location", changed.Location);
        Assert.AreEqual("images", changed.TypeKey);
        fixture.Items.Remove("Report.txt");
        fixture.Items.Add("REPORT.txt", "renamed location");
        Assert.AreEqual("REPORT.txt", fixture.Search("report").Single().DisplayName);
        fixture.Items.Remove("REPORT.txt");
        Assert.IsEmpty(fixture.Search("report"));
        fixture.Items.Add("Added.txt", "added location");
        Assert.AreEqual("Added.txt", fixture.Search("added").Single().DisplayName);
    }

    [STATestMethod]
    public void LoadedSearch_UpdatesGroupNamesIdsAndSameCountMembershipChanges()
    {
        using var fixture = new Fixture();
        fixture.Seed();
        GroupInfo group = fixture.Layout.Groups[0];
        Assert.HasCount(1, fixture.Search("工作"));

        group.Name = "研究";
        Assert.IsEmpty(fixture.Search("工作"));
        Assert.HasCount(1, fixture.Search("研究"));
        group.Id = "research";
        Assert.AreEqual("research", fixture.Search("研究").Single().GroupId);
        group.ItemNames[0] = "Other.txt";
        Assert.AreEqual("Other.txt", fixture.Search("研究").Single().DisplayName);
        Assert.AreEqual("Report.txt", fixture.Search(null, DesktopSmartView.Unclassified).Single().DisplayName);
        fixture.Layout.Groups.Clear();
        Assert.HasCount(2, fixture.Search(null, DesktopSmartView.Unclassified));
    }

    [STATestMethod]
    public void LoadedSearch_CopiesResultTagsAndDetectsSameCountTagChanges()
    {
        using var fixture = new Fixture();
        fixture.Seed();
        DesktopSearchResult result = fixture.Search("重要").Single();
        ((string[])result.Tags)[0] = "调用方修改";
        CollectionAssert.AreEqual(new[] { "重要" }, fixture.Search("重要").Single().Tags.ToArray());

        fixture.Layout.ItemTags["Report.txt"][0] = "完成";
        Assert.IsEmpty(fixture.Search("重要"));
        Assert.AreEqual("Report.txt", fixture.Search("完成").Single().DisplayName);
    }

    [STATestMethod]
    public void LoadedSearch_UpdatesInboxMembershipAndMetadataTimes()
    {
        using var fixture = new Fixture();
        fixture.Seed();
        Assert.AreEqual("Report.txt", fixture.Search(null, DesktopSmartView.PendingConfirmation).Single().DisplayName);
        fixture.Layout.InboxItems.Remove("Report.txt");
        fixture.Layout.InboxItems.Add("Other.txt", new InboxItemInfo());
        Assert.AreEqual("Other.txt", fixture.Search(null, DesktopSmartView.PendingConfirmation).Single().DisplayName);

        Assert.AreEqual("Report.txt", fixture.Search(null, DesktopSmartView.RecentlyAdded).Single().DisplayName);
        fixture.Layout.ItemFirstSeenUtcTicks["Report.txt"] = 0;
        Assert.IsEmpty(fixture.Search(null, DesktopSmartView.RecentlyAdded));
        fixture.Layout.ItemFirstSeenUtcTicks["Other.txt"] = DateTime.UtcNow.AddHours(-1).Ticks;
        Assert.AreEqual("Other.txt", fixture.Search(null, DesktopSmartView.RecentlyAdded).Single().DisplayName);
        Assert.AreEqual("Report.txt", fixture.Search(null, DesktopSmartView.RecentlyMoved).Single().DisplayName);
        fixture.Layout.ItemLastMovedUtcTicks["Report.txt"] = long.MaxValue;
        Assert.IsEmpty(fixture.Search(null, DesktopSmartView.RecentlyMoved));
        fixture.Layout.ItemLastMovedUtcTicks["Other.txt"] = DateTime.UtcNow.AddHours(-1).Ticks;
        Assert.AreEqual("Other.txt", fixture.Search(null, DesktopSmartView.RecentlyMoved).Single().DisplayName);
    }

    [STATestMethod]
    [DataRow("tags")]
    [DataRow("first-seen")]
    [DataRow("last-moved")]
    [DataRow("inbox")]
    public void LoadedSearch_DetectsLookupComparerChanges(string field)
    {
        using var fixture = new Fixture();
        fixture.Seed();
        switch (field)
        {
            case "tags":
                fixture.Layout.ItemTags = new(StringComparer.OrdinalIgnoreCase) { ["report.txt"] = ["大小写标签"] };
                Assert.HasCount(1, fixture.Search("大小写标签"));
                fixture.Layout.ItemTags = new(fixture.Layout.ItemTags, StringComparer.Ordinal);
                Assert.IsEmpty(fixture.Search("大小写标签"));
                break;
            case "first-seen":
                fixture.Layout.ItemFirstSeenUtcTicks = new(StringComparer.OrdinalIgnoreCase) { ["report.txt"] = DateTime.UtcNow.AddHours(-1).Ticks };
                Assert.HasCount(1, fixture.Search(null, DesktopSmartView.RecentlyAdded));
                fixture.Layout.ItemFirstSeenUtcTicks = new(fixture.Layout.ItemFirstSeenUtcTicks, StringComparer.Ordinal);
                Assert.IsEmpty(fixture.Search(null, DesktopSmartView.RecentlyAdded));
                break;
            case "last-moved":
                fixture.Layout.ItemLastMovedUtcTicks = new(StringComparer.OrdinalIgnoreCase) { ["report.txt"] = DateTime.UtcNow.AddHours(-1).Ticks };
                Assert.HasCount(1, fixture.Search(null, DesktopSmartView.RecentlyMoved));
                fixture.Layout.ItemLastMovedUtcTicks = new(fixture.Layout.ItemLastMovedUtcTicks, StringComparer.Ordinal);
                Assert.IsEmpty(fixture.Search(null, DesktopSmartView.RecentlyMoved));
                break;
            case "inbox":
                fixture.Layout.InboxItems = new(StringComparer.OrdinalIgnoreCase) { ["report.txt"] = new() };
                Assert.HasCount(1, fixture.Search(null, DesktopSmartView.PendingConfirmation));
                fixture.Layout.InboxItems = new(fixture.Layout.InboxItems, StringComparer.Ordinal);
                Assert.IsEmpty(fixture.Search(null, DesktopSmartView.PendingConfirmation));
                break;
        }
    }

    [STATestMethod]
    public void LoadedSearch_DetectsReplacementLayoutAndDesktopDictionaries()
    {
        using var fixture = new Fixture();
        fixture.Seed();
        Assert.HasCount(1, fixture.Search("重要 工作 文档"));

        fixture.Set("_appLayout", new AppLayoutData
        {
            Groups = [new GroupInfo { Id = "new", Name = "新工作区", ItemNames = ["Other.txt"] }],
            ItemTags = new() { ["Other.txt"] = ["替换标签"] }
        });
        fixture.Set("_desktopCategories", new Dictionary<string, DesktopCategoryDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Other.txt"] = new("images", "图片", 0)
        });
        fixture.Set("_desktopItems", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Other.txt"] = "replacement location"
        });

        Assert.IsEmpty(fixture.Search("重要"));
        DesktopSearchResult result = fixture.Search("替换标签 新工作区 图片").Single();
        Assert.AreEqual("Other.txt", result.DisplayName);
        Assert.AreEqual("replacement location", result.Location);
    }

    [STATestMethod]
    [DataRow(false, 32_000L)]
    [DataRow(true, 180_000L)]
    public void LoadedSearch_UnchangedDataStaysWithinQueryAllocationBudget(bool nameQuery, long budget)
    {
        using var fixture = new Fixture();
        for (int index = 0; index < 1000; index++)
        {
            string name = $"Item{index:D5}.txt";
            fixture.Items[name] = "test:" + name;
            fixture.Categories[name] = new("documents", "文档", 0);
            fixture.Layout.ItemTags[name] = ["重要", "项目", "待办"];
        }
        fixture.Layout.Groups.Add(new GroupInfo { Id = "work", Name = "工作", ItemNames = fixture.Items.Keys.ToList() });
        string?[] queries = nameQuery ? ["item", "Item", " item "] : [null, "", " "];
        for (int warmup = 0; warmup < 5; warmup++) fixture.Search(queries[warmup % queries.Length]);
        const int iterations = 8;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < iterations; iteration++)
            GC.KeepAlive(fixture.Search(queries[iteration % queries.Length]));
        long bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;

        Assert.HasCount(50, fixture.Search(queries[0]));
        TestContext.WriteLine($"1000 项完整搜索调用，名称查询={nameQuery}，每次分配={bytes} 字节");
        Assert.IsLessThan(budget, bytes, "输入或视图变化不应重新构建未变化的搜索数据。");
    }

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        public MainWindow Window { get; } = new(false, _ => { }, new DesktopPathResolver(_ => string.Empty));
        public AppLayoutData Layout => Field<AppLayoutData>("_appLayout");
        public Dictionary<string, string> Items => Field<Dictionary<string, string>>("_desktopItems");
        public Dictionary<string, DesktopCategoryDefinition> Categories => Field<Dictionary<string, DesktopCategoryDefinition>>("_desktopCategories");
        public IReadOnlyList<DesktopSearchResult> Search(string? text, DesktopSmartView view = DesktopSmartView.All) =>
            Window.SearchLoadedDesktopItems(text, view);
        public T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, PrivateInstance)!.GetValue(Window)!;
        public void Set(string name, object value) => typeof(MainWindow).GetField(name, PrivateInstance)!.SetValue(Window, value);
        private void Invoke(string name) => typeof(MainWindow).GetMethod(name, PrivateInstance)!.Invoke(Window, null);

        public void Seed()
        {
            Items["Report.txt"] = "original location";
            Items["Other.txt"] = "other location";
            Categories["Report.txt"] = new("documents", "文档", 0);
            Layout.Groups.Add(new GroupInfo { Id = "work", Name = "工作", ItemNames = ["Report.txt"] });
            Layout.ItemTags["Report.txt"] = ["重要"];
            Layout.InboxItems["Report.txt"] = new();
            Layout.ItemFirstSeenUtcTicks["Report.txt"] = DateTime.UtcNow.AddHours(-1).Ticks;
            Layout.ItemLastMovedUtcTicks["Report.txt"] = DateTime.UtcNow.AddHours(-1).Ticks;
        }

        public void Dispose()
        {
            // 不调用 Show/Loaded，不加载用户数据；搜索为纯内存调用，没有异步任务入队。
            Set("_isClosing", true);
            foreach (FieldInfo field in typeof(MainWindow).GetFields(PrivateInstance)
                         .Where(field => field.FieldType == typeof(DispatcherTimer)))
                ((DispatcherTimer)field.GetValue(Window)!).Stop();
            Field<CancellationTokenSource>("_lifetimeCts").Cancel();
            Invoke("CancelScheduledDesktopRefresh");
            Invoke("StopDesktopWatchers");
            Field<RefreshCancellationEpoch>("_refreshCancellationEpoch").Dispose();
            Invoke("StopAsyncIconLoading");
            Field<FileOperationService>("_fileOperationService").Dispose();
            Field<FolderPortalWatcherCoordinator>("_folderPortalWatcherCoordinator").Dispose();
            Field<CancellationTokenSource>("_lifetimeCts").Dispose();
            Field<SemaphoreSlim>("_layoutWriteGate").Dispose();
        }
    }
}
