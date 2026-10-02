using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopSearchPerformanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Build_CaseInsensitiveInputsStayWithinAllocationBudget()
    {
        var fixture = new SearchFixture();
        long bytes = AllocatedBytesPerCall(() => GC.KeepAlive(DesktopSearchIndex.Build(
            fixture.Items, fixture.Categories, [], fixture.Metadata)));

        TestContext.WriteLine($"1000 项索引构建分配：{bytes} 字节");
        Assert.IsLessThan(400_000L, bytes,
            "已经大小写无关的分类与元数据不应在每次输入时重复分组并复制字典。");
    }

    [TestMethod]
    public void Search_BlankQueryWithSmallLimitStaysWithinAllocationBudget()
    {
        var fixture = new SearchFixture();
        DesktopSearchIndex index = DesktopSearchIndex.Build(
            fixture.Items, fixture.Categories, [], fixture.Metadata);
        var request = new DesktopSearchRequest(null, DesktopSmartView.All, Now, Limit: 10);

        long bytes = AllocatedBytesPerCall(() => GC.KeepAlive(index.Search(request)));

        Assert.HasCount(10, index.Search(request));
        TestContext.WriteLine($"1000 项空查询、10 项结果分配：{bytes} 字节");
        Assert.IsLessThan(16_000L, bytes,
            "空查询应复用已排序的索引，不为全部项目分配评分和排序数据。");
    }

    [TestMethod]
    public void Search_NameMatchesAvoidLowerWeightMetadataAllocations()
    {
        var fixture = new SearchFixture();
        foreach (string name in fixture.Items.Keys)
            fixture.Metadata[name] = new DesktopSearchItemMetadata(["重要", "项目", "待办"]);
        var group = new GroupInfo { Id = "work", Name = "工作资料", ItemNames = fixture.Items.Keys.ToList() };
        DesktopSearchIndex index = DesktopSearchIndex.Build(
            fixture.Items, fixture.Categories, [group], fixture.Metadata);
        var request = new DesktopSearchRequest("item", DesktopSmartView.All, Now);

        long bytes = AllocatedBytesPerCall(() => GC.KeepAlive(index.Search(request)));

        Assert.HasCount(50, index.Search(request));
        TestContext.WriteLine($"1000 项名称命中查询分配：{bytes} 字节");
        Assert.IsLessThan(160_000L, bytes,
            "名称已命中时，不应继续枚举得分更低的标签、分组与类型。");
    }

    private static long AllocatedBytesPerCall(Action action)
    {
        for (int warmup = 0; warmup < 5; warmup++) action();
        const int iterations = 8;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < iterations; iteration++) action();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
    }

    private sealed class SearchFixture
    {
        public Dictionary<string, string> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DesktopCategoryDefinition> Categories { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DesktopSearchItemMetadata> Metadata { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public SearchFixture()
        {
            var category = new DesktopCategoryDefinition("documents", "文档", 0);
            for (int index = 0; index < 1000; index++)
            {
                string name = $"Item{index:D5}.txt";
                Items.Add(name, @"C:\Desktop\" + name);
                Categories.Add(name, category);
                Metadata.Add(name, new DesktopSearchItemMetadata([]));
            }
        }
    }
}
