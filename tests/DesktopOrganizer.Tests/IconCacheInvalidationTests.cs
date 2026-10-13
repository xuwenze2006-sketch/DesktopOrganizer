using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class IconCacheInvalidationTests
{
    [TestMethod]
    public void RemovedItem_InvalidatesItsPreviousLocation()
    {
        const string path = @"C:\Desktop\A.exe";
        HashSet<string> result = IconCacheInvalidation.FindChangedLocations(
            Items(("A.exe", path)),
            Identities(("A.exe", Identity(path, "old-id"))),
            Items(),
            Identities());

        CollectionAssert.AreEquivalent(new[] { path }, result.ToArray());
    }

    [TestMethod]
    public void SamePathReplacement_InvalidatesWhenIdentityChanges()
    {
        const string path = @"C:\Desktop\A.exe";
        HashSet<string> result = IconCacheInvalidation.FindChangedLocations(
            Items(("A.exe", path)),
            Identities(("A.exe", Identity(path, "old-id"))),
            Items(("A.exe", path)),
            Identities(("A.exe", Identity(path, "new-id"))));

        CollectionAssert.AreEquivalent(new[] { path }, result.ToArray());
    }

    [TestMethod]
    public void UnchangedItem_DoesNotInvalidateLocation()
    {
        const string path = @"C:\Desktop\A.exe";
        HashSet<string> result = IconCacheInvalidation.FindChangedLocations(
            Items(("A.exe", path)),
            Identities(("A.exe", Identity(path, "same-id"))),
            Items(("A.exe", path)),
            Identities(("A.exe", Identity(path, "same-id"))));

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void AddedItem_InvalidatesAnyPriorCacheForItsLocation()
    {
        const string path = @"C:\Desktop\A.exe";
        HashSet<string> result = IconCacheInvalidation.FindChangedLocations(
            Items(),
            Identities(),
            Items(("A.exe", path)),
            Identities(("A.exe", Identity(path, "new-id"))));

        CollectionAssert.AreEquivalent(new[] { path }, result.ToArray());
    }

    [TestMethod]
    public void CandidateKeys_IncludeFolderPathFilePathAndExtensionForms()
    {
        const string path = @"C:\Desktop\A.exe";
        IReadOnlyList<string> result = IconCacheInvalidation.GetCandidateCacheKeys(path);

        CollectionAssert.AreEquivalent(
            new[] { "folder:" + path, "path:" + path, "ext:.exe" },
            result.ToArray());
    }

    [TestMethod]
    [DataRow("A.txt")]
    [DataRow("A.TXT")]
    [DataRow("A.pdf")]
    public void SameOrdinaryFile_ContentChangeKeepsSharedIcon(string name)
    {
        string path = @"C:\Desktop\" + name;
        var before = ReliableIdentity(path);
        var after = ReliableIdentity(path);
        after.LastWriteTimeUtcTicks++;
        Assert.HasCount(0, IconCacheInvalidation.FindChangedLocations(
            Items((name, path)), Identities((name, before)), Items((name, path)), Identities((name, after))));
    }

    [TestMethod]
    [DataRow("id-changed")]
    [DataRow("id-missing")]
    [DataRow("creation-changed")]
    [DataRow("creation-missing")]
    [DataRow("creation-invalid")]
    [DataRow("write-missing")]
    [DataRow("old-write-missing")]
    [DataRow("write-invalid")]
    [DataRow("folder")]
    [DataRow("type-changed")]
    [DataRow("identity-path-changed")]
    [DataRow("shell-kind")]
    public void UncertainOrDifferentIdentity_ContentChangeStillInvalidates(string change)
    {
        const string path = @"C:\Desktop\A.txt";
        var before = ReliableIdentity(path);
        var after = ReliableIdentity(path);
        after.LastWriteTimeUtcTicks++;
        switch (change)
        {
            case "id-changed": after.FileId = "replacement"; break;
            case "id-missing": before.FileId = after.FileId = null; break;
            case "creation-changed": after.CreationTimeUtcTicks++; break;
            case "creation-missing": before.CreationTimeUtcTicks = after.CreationTimeUtcTicks = null; break;
            case "creation-invalid": before.CreationTimeUtcTicks = after.CreationTimeUtcTicks = 0; break;
            case "write-missing": after.LastWriteTimeUtcTicks = null; break;
            case "old-write-missing": before.LastWriteTimeUtcTicks = null; break;
            case "write-invalid": after.LastWriteTimeUtcTicks = 0; break;
            case "folder": before.IsDirectory = after.IsDirectory = true; break;
            case "type-changed": after.IsDirectory = true; break;
            case "identity-path-changed": after.LastKnownPath = @"C:\Desktop\Other.txt"; break;
            case "shell-kind": before.Kind = after.Kind = DesktopItemKind.ShellNamespace; break;
            default: Assert.Fail("未知边界。"); break;
        }
        CollectionAssert.AreEquivalent(new[] { path }, IconCacheInvalidation.FindChangedLocations(
            Items(("A.txt", path)), Identities(("A.txt", before)),
            Items(("A.txt", path)), Identities(("A.txt", after))).ToArray());
    }

    [TestMethod]
    [DataRow("A.EXE")]
    [DataRow("A.LNK")]
    [DataRow("A.URL")]
    [DataRow("A.ICO")]
    [DataRow("A")]
    public void FileSpecificIcon_ContentChangeStillInvalidates(string name)
    {
        string path = @"C:\Desktop\" + name;
        var before = ReliableIdentity(path);
        var after = ReliableIdentity(path);
        after.LastWriteTimeUtcTicks++;
        CollectionAssert.AreEquivalent(new[] { path }, IconCacheInvalidation.FindChangedLocations(
            Items((name, path)), Identities((name, before)),
            Items((name, path)), Identities((name, after))).ToArray());
    }

    private static DesktopItemIdentityInfo ReliableIdentity(string path) => new()
    {
        Kind = DesktopItemKind.FileSystem, LastKnownPath = path, FileId = "stable-id",
        CreationTimeUtcTicks = new DateTime(2025, 1, 1).Ticks,
        LastWriteTimeUtcTicks = new DateTime(2025, 1, 2).Ticks
    };

    [TestMethod]
    public void IconLoadRequestKey_DifferentCacheVersion_DoesNotDeduplicate()
    {
        var stale = new IconLoadRequestKey("path:C:\\Desktop\\A.exe", generation: 4, cacheVersion: 1);
        var replacement = new IconLoadRequestKey("path:C:\\Desktop\\A.exe", generation: 4, cacheVersion: 2);

        Assert.AreNotEqual(stale, replacement);
    }

    [TestMethod]
    public void IconLoadRequestKey_SameVersion_RemainsCaseInsensitive()
    {
        var first = new IconLoadRequestKey("path:C:\\Desktop\\A.exe", generation: 4, cacheVersion: 2);
        var second = new IconLoadRequestKey("PATH:c:\\desktop\\a.EXE", generation: 4, cacheVersion: 2);

        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
    }

    private static Dictionary<string, string> Items(
        params (string Name, string Location)[] items) =>
        items.ToDictionary(
            item => item.Name,
            item => item.Location,
            StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, DesktopItemIdentityInfo> Identities(
        params (string Name, DesktopItemIdentityInfo Identity)[] items) =>
        items.ToDictionary(
            item => item.Name,
            item => item.Identity,
            StringComparer.OrdinalIgnoreCase);

    private static DesktopItemIdentityInfo Identity(string path, string fileId) => new()
    {
        Kind = DesktopItemKind.FileSystem,
        LastKnownPath = path,
        FileId = fileId,
        CreationTimeUtcTicks = 123,
        IsDirectory = false
    };
}
