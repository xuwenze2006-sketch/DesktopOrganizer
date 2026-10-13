namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopItemTypeLookupTests
{
    [TestMethod]
    [DataRow(false, ".TXT", "ext:.txt")]
    [DataRow(false, ".EXE", "path:C:\\Desktop\\item.EXE")]
    [DataRow(false, ".lnk", "path:C:\\Desktop\\item.lnk")]
    [DataRow(false, ".url", "path:C:\\Desktop\\item.url")]
    [DataRow(false, ".ico", "path:C:\\Desktop\\item.ico")]
    [DataRow(false, "", "path:C:\\Desktop\\item")]
    [DataRow(true, ".txt", "folder:C:\\Desktop\\item.txt")]
    public void KnownTypes_DetermineKeysAndVisualsWithoutFileSystemAccess(bool isDirectory, string extension, string key)
    {
        var lookup = new DesktopItemTypeLookup(_ => throw new AssertFailedException("重复目录查询"),
            _ => throw new AssertFailedException("重复文件查询"));
        string path = @"C:\Desktop\item" + extension;
        lookup.ReplaceSnapshot(new Dictionary<string, bool> { [path.ToLowerInvariant()] = isDirectory });

        Assert.AreEqual(key, lookup.GetIconCacheKey(path));
        Assert.AreEqual(isDirectory ? "file-system-folder" : "file-system-file", lookup.GetVisualKind(path));
        Assert.AreEqual(isDirectory ? string.Empty : extension, lookup.GetTypeSortKey(path));
        Assert.AreEqual(isDirectory, lookup.IsFolderLike(path));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ShellEncoding_TakesPrecedenceOverPhysicalTypeSnapshot(bool isFolder)
    {
        var lookup = new DesktopItemTypeLookup(_ => throw new AssertFailedException("Shell 不应查询磁盘"),
            _ => throw new AssertFailedException("Shell 不应查询磁盘"));
        string location = ShellItemLocation.Encode("test-shell", isFolder);
        lookup.ReplaceSnapshot(new Dictionary<string, bool> { [location] = !isFolder });

        Assert.AreEqual("shell:test-shell", lookup.GetIconCacheKey(location));
        Assert.AreEqual(isFolder ? "shell-folder" : "shell-item", lookup.GetVisualKind(location));
        Assert.AreEqual(isFolder ? string.Empty : ".shell", lookup.GetTypeSortKey(location));
        Assert.AreEqual(isFolder, lookup.IsFolderLike(location));
    }

    [TestMethod]
    public void UnknownPaths_RecheckFileSystemAndDistinguishMissingItems()
    {
        const string path = @"C:\Desktop\unknown.txt";
        bool directory = true;
        bool file = false;
        var lookup = new DesktopItemTypeLookup(
            queried => { Assert.AreEqual(path, queried); return directory; },
            queried => { Assert.AreEqual(path, queried); return file; });

        Assert.AreEqual("folder:" + path, lookup.GetIconCacheKey(path));
        Assert.AreEqual("file-system-folder", lookup.GetVisualKind(path));
        directory = false;
        file = true;
        Assert.AreEqual("ext:.txt", lookup.GetIconCacheKey(path));
        Assert.AreEqual("file-system-file", lookup.GetVisualKind(path));
        file = false;
        Assert.AreEqual("missing", lookup.GetVisualKind(path));
    }

    [TestMethod]
    public void ReplacementSnapshot_DropsRemovedPathsAndDoesNotRetainMutableInput()
    {
        const string first = @"C:\Desktop\first.txt";
        const string second = @"D:\Desktop\first.txt";
        int queries = 0;
        var lookup = new DesktopItemTypeLookup(_ => { queries++; return false; }, _ => false);
        var known = new Dictionary<string, bool> { [first] = true };
        lookup.ReplaceSnapshot(known);
        known[first] = false;
        Assert.AreEqual("folder:" + first, lookup.GetIconCacheKey(first));

        lookup.ReplaceSnapshot(new Dictionary<string, bool> { [second] = true });

        Assert.AreEqual("folder:" + second, lookup.GetIconCacheKey(second));
        Assert.AreEqual("ext:.txt", lookup.GetIconCacheKey(first));
        Assert.AreEqual(1, queries, "移除的路径必须回退查询，不能沿用旧快照。");
    }
}
