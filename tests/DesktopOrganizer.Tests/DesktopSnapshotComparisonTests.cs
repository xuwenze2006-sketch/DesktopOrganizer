using System.Collections;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopSnapshotComparisonTests
{
    private static readonly MethodInfo CompareSnapshot = typeof(MainWindow).GetMethod(
        "DesktopSnapshotMatches", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly Type SnapshotType = typeof(MainWindow).GetNestedType(
        "DesktopScanSnapshot", BindingFlags.NonPublic)!;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void UnchangedSnapshot_VisitsCurrentNamesAtMostLinearly()
    {
        var current = new Snapshot();
        var next = new Snapshot();
        for (int index = 0; index < 512; index++)
        {
            string name = $"Item{index:D4}.txt";
            string path = @"C:\Desktop\" + name;
            current.Add(name, path);
            next.Add(name, path);
        }
        var countedItems = new CountingDictionary(current.Items);

        Assert.IsTrue(Matches(current, next, countedItems));

        TestContext.WriteLine($"512 个项目的名称访问次数：{countedItems.NameVisits}");
        Assert.IsLessThanOrEqualTo(1024, countedItems.NameVisits,
            "未变化的快照不应对每个项目重新遍历全部桌面名称。");
    }

    [TestMethod]
    public void UnchangedSnapshot_IgnoresEnumerationOrderAndPathCasing()
    {
        var current = new Snapshot();
        current.Add("Alpha.txt", @"C:\Desktop\Alpha.txt");
        current.Add("Beta.txt", @"C:\Desktop\Beta.txt", reliable: false);
        var next = new Snapshot();
        next.Add("Beta.txt", @"c:\desktop\BETA.txt", reliable: false);
        next.Add("Alpha.txt", @"c:\desktop\ALPHA.txt");

        Assert.IsTrue(Matches(current, next));
    }

    [TestMethod]
    [DataRow("report.TXT", @"C:\Desktop\Report.txt", "documents", true, false)]
    [DataRow("Other.txt", @"C:\Desktop\Report.txt", "documents", true, false)]
    [DataRow("Report.txt", @"D:\Desktop\Report.txt", "documents", true, false)]
    [DataRow("Report.txt", @"C:\Desktop\Report.txt", "images", true, false)]
    [DataRow("Report.txt", @"C:\Desktop\Report.txt", "documents", false, false)]
    [DataRow("Report.txt", @"C:\Desktop\Report.txt", "DOCUMENTS", true, true)]
    public void SnapshotComparison_PreservesChangeDetection(
        string nextName, string nextPath, string nextCategory, bool nextReliable, bool expected)
    {
        var current = new Snapshot();
        current.Add("Report.txt", @"C:\Desktop\Report.txt");
        var next = new Snapshot();
        next.Add(nextName, nextPath, nextCategory, nextReliable);

        Assert.AreEqual(expected, Matches(current, next));
    }

    [TestMethod]
    [DataRow("items")]
    [DataRow("categories")]
    [DataRow("reliability")]
    public void DifferentCounts_ReturnWithoutEnumeratingNames(string changedCollection)
    {
        var current = new Snapshot();
        current.Add("Report.txt", @"C:\Desktop\Report.txt");
        var next = new Snapshot();
        next.Add("Report.txt", @"C:\Desktop\Report.txt");
        switch (changedCollection)
        {
            case "items": next.Items.Clear(); break;
            case "categories": next.Categories.Clear(); break;
            case "reliability": next.Reliable.Clear(); break;
        }
        var countedItems = new CountingDictionary(current.Items);

        Assert.IsFalse(Matches(current, next, countedItems));
        Assert.AreEqual(0, countedItems.NameVisits);
    }

    [TestMethod]
    public void EmptySnapshots_Match()
    {
        Assert.IsTrue(Matches(new Snapshot(), new Snapshot()));
    }

    [TestMethod]
    public void SnapshotComparison_DetectsReliabilityChangesWithEqualCounts()
    {
        var current = new Snapshot();
        current.Add("Alpha.txt", @"C:\Desktop\Alpha.txt");
        current.Add("Beta.txt", @"C:\Desktop\Beta.txt", reliable: false);
        var next = new Snapshot();
        next.Add("Alpha.txt", @"C:\Desktop\Alpha.txt", reliable: false);
        next.Add("Beta.txt", @"C:\Desktop\Beta.txt");

        Assert.IsFalse(Matches(current, next));
    }

    [TestMethod]
    public void ShellSnapshot_DetectsItemKindChanges()
    {
        const string parsingName = "::{00000000-0000-0000-0000-000000000001}";
        var current = new Snapshot();
        current.Add("Shell item", ShellItemLocation.Encode(parsingName, isFolder: true));
        var next = new Snapshot();
        next.Add("Shell item", ShellItemLocation.Encode(parsingName, isFolder: false));

        Assert.IsFalse(Matches(current, next));
    }

    private static bool Matches(
        Snapshot current, Snapshot next, IReadOnlyDictionary<string, string>? currentItems = null)
    {
        var unavailable = new DesktopPathState(null, null, false, null);
        object scan = Activator.CreateInstance(SnapshotType,
            next.Items, next.Categories, next.Reliable,
            new Dictionary<string, DesktopItemIdentityInfo>(StringComparer.OrdinalIgnoreCase),
            true, true, new DesktopPathSnapshot(unavailable, unavailable, 1))!;
        return (bool)CompareSnapshot.Invoke(null,
            [currentItems ?? current.Items, current.Categories, current.Reliable, scan])!;
    }

    private sealed class Snapshot
    {
        public Dictionary<string, string> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DesktopCategoryDefinition> Categories { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Reliable { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string name, string path, string category = "documents", bool reliable = true)
        {
            Items.Add(name, path);
            Categories.Add(name, new DesktopCategoryDefinition(category, category, 0));
            if (reliable) Reliable.Add(name);
        }
    }

    private sealed class CountingDictionary(IReadOnlyDictionary<string, string> items)
        : IReadOnlyDictionary<string, string>
    {
        public int NameVisits { get; private set; }
        public int Count => items.Count;
        public string this[string key] => items[key];
        public IEnumerable<string> Values => items.Values;
        public IEnumerable<string> Keys => EnumerateNames();
        public bool ContainsKey(string key) => items.ContainsKey(key);
        public bool TryGetValue(string key, out string value) => items.TryGetValue(key, out value!);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private IEnumerable<string> EnumerateNames()
        {
            foreach (string name in items.Keys)
            {
                NameVisits++;
                yield return name;
            }
        }
    }
}
