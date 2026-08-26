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
