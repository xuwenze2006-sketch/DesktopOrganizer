using System.Collections;
using System.Diagnostics;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopIdentityReconciliationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void SameNames_ReconcileWithLinearEntryVisits(bool shell, bool changeCase)
    {
        const int count = 1000;
        var previous = new Dictionary<string, DesktopItemIdentityInfo>(StringComparer.Ordinal);
        var next = new Dictionary<string, DesktopItemIdentityInfo>(StringComparer.Ordinal);
        for (int index = 0; index < count; index++)
        {
            string oldName = $"Item{index:D4}";
            string nextName = changeCase ? oldName.ToUpperInvariant() : oldName;
            previous.Add(oldName, Identity(oldName, $"id-{index}", shell));
            next.Add(nextName, Identity(nextName, $"id-{index}", shell));
        }

        var countedPrevious = new CountingDictionary(previous);
        var countedNext = new CountingDictionary(next);
        Dictionary<string, MainWindow.DesktopItemRenameCandidate> renames = Reconcile(countedPrevious, countedNext);
        Assert.HasCount(changeCase ? count : 0, renames);
        if (changeCase)
        {
            foreach (string name in previous.Keys)
            {
                Assert.AreEqual(name.ToUpperInvariant(), renames[name].NewName);
                Assert.AreEqual(shell ? "shell-case" : "file-id-case", renames[name].Source);
            }
        }

        // 耗时只作记录，门禁使用整个候选核对过程的确定访问次数。
        for (int warmup = 0; warmup < 3; warmup++) Reconcile(previous, next);
        const int iterations = 8;
        var stopwatch = new Stopwatch();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        stopwatch.Start();
        for (int iteration = 0; iteration < iterations; iteration++) Reconcile(previous, next);
        stopwatch.Stop();
        long bytes = (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / iterations;
        TestContext.WriteLine($"1000 项身份核对 shell={shell}, changeCase={changeCase}: 新表访问 {countedNext.EntryVisits} 次，旧表访问 {countedPrevious.EntryVisits} 次，单次 {stopwatch.Elapsed.TotalMilliseconds / iterations:F3} ms，分配 {bytes} 字节");
        Assert.IsLessThanOrEqualTo(count, countedPrevious.EntryVisits);
        Assert.IsLessThanOrEqualTo(3 * count, countedNext.EntryVisits,
            "身份核对不应为每个旧项目从头扫描新身份表。");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CaseCollidingNames_PreserveFirstMatchingEntry(bool shell)
    {
        var previous = new Dictionary<string, DesktopItemIdentityInfo>
        {
            ["REPORT"] = Identity("REPORT", "same-id", shell)
        };
        var next = new Dictionary<string, DesktopItemIdentityInfo>(StringComparer.Ordinal)
        {
            ["Report"] = Identity("Report", "same-id", shell),
            ["REPORT"] = Identity("REPORT", "same-id", shell)
        };

        Dictionary<string, MainWindow.DesktopItemRenameCandidate> renames = Reconcile(previous, next);

        Assert.HasCount(1, renames);
        Assert.AreEqual("Report", renames["REPORT"].NewName);
        Assert.AreEqual(shell ? "shell-case" : "file-id-case", renames["REPORT"].Source);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SameNameWithDifferentIdentity_FallsBackToUniqueStableIdentity(bool shell)
    {
        var previous = new Dictionary<string, DesktopItemIdentityInfo>
        {
            ["Original"] = Identity("Original", "old-id", shell)
        };
        var next = new Dictionary<string, DesktopItemIdentityInfo>
        {
            ["ORIGINAL"] = Identity("ORIGINAL", "replacement-id", shell),
            ["Renamed"] = Identity("Renamed", "OLD-ID", shell)
        };

        Dictionary<string, MainWindow.DesktopItemRenameCandidate> renames = Reconcile(previous, next);

        Assert.HasCount(1, renames);
        MainWindow.DesktopItemRenameCandidate rename = renames["Original"];
        Assert.AreEqual("Renamed", rename.NewName);
        Assert.AreEqual(previous["Original"].LastKnownPath, rename.OldFullPath);
        Assert.AreEqual(next["Renamed"].LastKnownPath, rename.NewFullPath);
        Assert.AreEqual(shell ? "shell-parsing-name" : "file-id", rename.Source);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MultipleStableIdentityMatches_DoNotRename(bool shell)
    {
        var previous = new Dictionary<string, DesktopItemIdentityInfo>
        {
            ["Original"] = Identity("Original", "same-id", shell)
        };
        var next = new Dictionary<string, DesktopItemIdentityInfo>
        {
            ["First"] = Identity("First", "same-id", shell),
            ["Second"] = Identity("Second", "same-id", shell)
        };

        Assert.HasCount(0, Reconcile(previous, next));
    }

    [TestMethod]
    [DataRow("file-id")]
    [DataRow("creation-time")]
    [DataRow("directory")]
    [DataRow("kind")]
    public void SameNameWithIncompatibleIdentity_DoesNotRename(string change)
    {
        DesktopItemIdentityInfo changed = Identity("ORIGINAL");
        switch (change)
        {
            case "file-id": changed.FileId = null; break;
            case "creation-time": changed.CreationTimeUtcTicks++; break;
            case "directory": changed.IsDirectory = true; break;
            case "kind": changed.Kind = DesktopItemKind.ShellNamespace; break;
        }

        Assert.HasCount(0, Reconcile(
            new Dictionary<string, DesktopItemIdentityInfo> { ["Original"] = Identity("Original") },
            new Dictionary<string, DesktopItemIdentityInfo> { ["ORIGINAL"] = changed }));
    }

    [TestMethod]
    public void WatcherRename_TakesPrecedenceOverStableIdentityCandidate()
    {
        var watcher = new MainWindow.DesktopItemRenameCandidate(
            "Original", "WatcherTarget", "old-path", "watcher-path", "watcher");
        var renames = new Dictionary<string, MainWindow.DesktopItemRenameCandidate>(StringComparer.OrdinalIgnoreCase)
        {
            [watcher.OldName] = watcher
        };

        MainWindow.ReconcileDesktopIdentityRenames(
            new Dictionary<string, DesktopItemIdentityInfo> { ["Original"] = Identity("Original") },
            new Dictionary<string, DesktopItemIdentityInfo> { ["StableTarget"] = Identity("StableTarget") },
            renames);

        Assert.HasCount(1, renames);
        Assert.AreSame(watcher, renames["Original"]);
    }

    [TestMethod]
    public void ConflictingWatcherAndIdentityTargets_RemoveBothCandidates()
    {
        var renames = new Dictionary<string, MainWindow.DesktopItemRenameCandidate>(StringComparer.OrdinalIgnoreCase)
        {
            ["WatcherSource"] = new("WatcherSource", "TARGET", "old-path", "new-path", "watcher")
        };

        MainWindow.ReconcileDesktopIdentityRenames(
            new Dictionary<string, DesktopItemIdentityInfo> { ["Original"] = Identity("Original") },
            new Dictionary<string, DesktopItemIdentityInfo> { ["Target"] = Identity("Target") },
            renames);

        Assert.HasCount(0, renames);
    }

    [TestMethod]
    public void EmptyIdentities_ProduceNoRenames()
    {
        Assert.HasCount(0, Reconcile(
            new Dictionary<string, DesktopItemIdentityInfo>(),
            new Dictionary<string, DesktopItemIdentityInfo>()));
    }

    private static DesktopItemIdentityInfo Identity(string name, string id = "same-id", bool shell = false) => new()
    {
        Kind = shell ? DesktopItemKind.ShellNamespace : DesktopItemKind.FileSystem,
        LastKnownPath = shell ? ShellItemLocation.Encode(id, false) : @"C:\Desktop\" + name,
        FileId = shell ? null : id,
        ShellParsingName = shell ? id : null,
        CreationTimeUtcTicks = 100,
        LastWriteTimeUtcTicks = 200
    };

    private static Dictionary<string, MainWindow.DesktopItemRenameCandidate> Reconcile(
        IReadOnlyDictionary<string, DesktopItemIdentityInfo> previous,
        IReadOnlyDictionary<string, DesktopItemIdentityInfo> next)
    {
        var renames = new Dictionary<string, MainWindow.DesktopItemRenameCandidate>(StringComparer.OrdinalIgnoreCase);
        MainWindow.ReconcileDesktopIdentityRenames(previous, next, renames);
        return renames;
    }

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
