using System.Collections;
using System.Diagnostics;
using System.Reflection;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopIdentityComparisonTests
{
    private static readonly MethodInfo Compare = typeof(MainWindow).GetMethod(
        "DesktopIdentityMapsEqual", BindingFlags.Static | BindingFlags.NonPublic)!;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void UnchangedIdentities_VisitCurrentEntriesAtMostLinearly()
    {
        var current = new Dictionary<string, DesktopItemIdentityInfo>(StringComparer.OrdinalIgnoreCase);
        var next = new Dictionary<string, DesktopItemIdentityInfo>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < 1000; index++)
        {
            string name = $"Item{index:D4}.txt";
            current.Add(name, Identity(name));
            next.Add(name, Identity(name));
        }
        var counted = new CountingDictionary(current);
        Assert.IsTrue(Matches(counted, next));

        // 耗时仅记录供离线比较；门禁使用确定的访问次数，不依赖机器负载。
        for (int warmup = 0; warmup < 3; warmup++) Matches(current, next);
        const int iterations = 16;
        var stopwatch = new Stopwatch();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        stopwatch.Start();
        for (int iteration = 0; iteration < iterations; iteration++)
            Assert.IsTrue(Matches(current, next));
        stopwatch.Stop();
        long bytes = (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / iterations;
        TestContext.WriteLine($"1000 项身份比较：旧项访问 {counted.EntryVisits} 次，单次 {stopwatch.Elapsed.TotalMilliseconds / iterations:F3} ms，分配 {bytes} 字节");
        Assert.IsLessThanOrEqualTo(2000, counted.EntryVisits,
            "稳定刷新不应为每个项目重新枚举整个旧身份列表。");
    }

    [TestMethod]
    public void EqualIdentities_IgnoreEnumerationOrderAndMetadataCasing()
    {
        var current = new Dictionary<string, DesktopItemIdentityInfo>(StringComparer.Ordinal)
        {
            ["Alpha.txt"] = Identity("Alpha.txt"),
            ["Beta.txt"] = Identity("Beta.txt")
        };
        DesktopItemIdentityInfo alpha = Identity("Alpha.txt");
        alpha.LastKnownPath = @"c:\desktop\ALPHA.txt";
        alpha.FileId = "file-id";
        var next = new Dictionary<string, DesktopItemIdentityInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["Beta.txt"] = Identity("Beta.txt"),
            ["Alpha.txt"] = alpha
        };

        Assert.IsTrue(Matches(current, next));
    }

    [TestMethod]
    [DataRow("name-case")]
    [DataRow("name")]
    [DataRow("path")]
    [DataRow("file-id")]
    [DataRow("creation-time")]
    [DataRow("write-time")]
    [DataRow("directory")]
    [DataRow("kind")]
    [DataRow("shell-name")]
    public void ChangedIdentity_StillRequiresRefresh(string change)
    {
        DesktopItemIdentityInfo changed = Identity();
        string nextName = "Report.txt";
        switch (change)
        {
            case "name-case": nextName = "REPORT.txt"; break;
            case "name": nextName = "Other.txt"; break;
            case "path": changed.LastKnownPath = @"D:\Desktop\Report.txt"; break;
            case "file-id": changed.FileId = "replacement-id"; break;
            case "creation-time": changed.CreationTimeUtcTicks = null; break;
            case "write-time": changed.LastWriteTimeUtcTicks++; break;
            case "directory": changed.IsDirectory = true; break;
            case "kind": changed.Kind = DesktopItemKind.ShellNamespace; break;
            case "shell-name": changed.ShellParsingName = "shell-item"; break;
        }

        Assert.IsFalse(Matches(
            new Dictionary<string, DesktopItemIdentityInfo> { ["Report.txt"] = Identity() },
            new Dictionary<string, DesktopItemIdentityInfo> { [nextName] = changed }));
    }

    [TestMethod]
    public void ShellIdentities_PreserveCaseInsensitiveParsingNameComparison()
    {
        DesktopItemIdentityInfo first = Identity();
        DesktopItemIdentityInfo second = Identity();
        first.Kind = second.Kind = DesktopItemKind.ShellNamespace;
        first.LastKnownPath = second.LastKnownPath = ShellItemLocation.Encode("shell-item", false);
        first.ShellParsingName = "SHELL-ITEM";
        second.ShellParsingName = "shell-item";
        var current = new Dictionary<string, DesktopItemIdentityInfo> { ["Shell"] = first };
        var next = new Dictionary<string, DesktopItemIdentityInfo> { ["Shell"] = second };

        Assert.IsTrue(Matches(current, next));
        second.ShellParsingName = "other-shell-item";
        Assert.IsFalse(Matches(current, next));
    }

    [TestMethod]
    public void DifferentCounts_ReturnWithoutReadingEntries()
    {
        var current = new CountingDictionary(new Dictionary<string, DesktopItemIdentityInfo>
        {
            ["Report.txt"] = Identity()
        });

        Assert.IsFalse(Matches(current, new Dictionary<string, DesktopItemIdentityInfo>()));
        Assert.AreEqual(0, current.EntryVisits);
    }

    [TestMethod]
    public void EmptyIdentityMaps_Match()
    {
        Assert.IsTrue(Matches(new Dictionary<string, DesktopItemIdentityInfo>(),
            new Dictionary<string, DesktopItemIdentityInfo>()));
    }

    [TestMethod]
    public void CaseCollidingKeys_PreserveFirstMatchingEntrySemantics()
    {
        // IReadOnlyDictionary 也可能使用区分大小写的比较器；旧逻辑取首次匹配项。
        var current = new Dictionary<string, DesktopItemIdentityInfo>(StringComparer.Ordinal)
        {
            ["Report.txt"] = Identity(),
            ["REPORT.txt"] = Identity()
        };
        var next = new Dictionary<string, DesktopItemIdentityInfo>(current, StringComparer.Ordinal);

        Assert.IsFalse(Matches(current, next));
    }

    private static DesktopItemIdentityInfo Identity(string name = "Report.txt") => new()
    {
        Kind = DesktopItemKind.FileSystem,
        LastKnownPath = @"C:\Desktop\" + name,
        FileId = "FILE-ID",
        CreationTimeUtcTicks = 100,
        LastWriteTimeUtcTicks = 200,
        IsDirectory = false
    };

    private static bool Matches(
        IReadOnlyDictionary<string, DesktopItemIdentityInfo> current,
        IReadOnlyDictionary<string, DesktopItemIdentityInfo> next) =>
        (bool)Compare.Invoke(null, [current, next])!;

    private sealed class CountingDictionary(IReadOnlyDictionary<string, DesktopItemIdentityInfo> entries)
        : IReadOnlyDictionary<string, DesktopItemIdentityInfo>
    {
        public int EntryVisits { get; private set; }
        public int Count => entries.Count;
        public DesktopItemIdentityInfo this[string key] => entries[key];
        public IEnumerable<string> Keys => Enumerate().Select(pair => pair.Key);
        public IEnumerable<DesktopItemIdentityInfo> Values => Enumerate().Select(pair => pair.Value);
        public bool ContainsKey(string key) => entries.ContainsKey(key);
        public bool TryGetValue(string key, out DesktopItemIdentityInfo value) => entries.TryGetValue(key, out value!);
        public IEnumerator<KeyValuePair<string, DesktopItemIdentityInfo>> GetEnumerator() => Enumerate().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private IEnumerable<KeyValuePair<string, DesktopItemIdentityInfo>> Enumerate()
        {
            foreach (KeyValuePair<string, DesktopItemIdentityInfo> entry in entries)
            {
                EntryVisits++;
                yield return entry;
            }
        }
    }
}
