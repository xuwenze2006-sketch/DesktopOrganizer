using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopNewItemDetectorTests
{
    [TestMethod]
    public void FindNewItemNames_KnownIdentityWithoutLayoutPosition_IsNotNew()
    {
        Dictionary<string, DesktopItemIdentityInfo> current = new()
        {
            ["overflow.txt"] = Physical(@"C:\Desktop\overflow.txt", "volume:file", 10)
        };
        Dictionary<string, DesktopItemIdentityInfo> next = new()
        {
            ["overflow.txt"] = Physical(@"C:\Desktop\overflow.txt", "volume:file", 10)
        };

        HashSet<string> result = DesktopNewItemDetector.FindNewItemNames(current, next);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void FindNewItemNames_PreviouslyUnknownIdentity_IsNew()
    {
        Dictionary<string, DesktopItemIdentityInfo> next = new()
        {
            ["new.txt"] = Physical(@"C:\Desktop\new.txt", "volume:new", 20)
        };

        HashSet<string> result = DesktopNewItemDetector.FindNewItemNames(
            new Dictionary<string, DesktopItemIdentityInfo>(),
            next);

        CollectionAssert.AreEquivalent(new[] { "new.txt" }, result.ToArray());
    }

    [TestMethod]
    public void FindNewItemNames_RenamedStableIdentity_IsNotNew()
    {
        Dictionary<string, DesktopItemIdentityInfo> current = new()
        {
            ["old.txt"] = Physical(@"C:\Desktop\old.txt", "volume:same", 30)
        };
        Dictionary<string, DesktopItemIdentityInfo> next = new()
        {
            ["renamed.txt"] = Physical(@"C:\Desktop\renamed.txt", "volume:same", 30)
        };

        HashSet<string> result = DesktopNewItemDetector.FindNewItemNames(current, next);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void FindNewItemNames_SameNameReplacementWithDifferentIdentity_IsNew()
    {
        Dictionary<string, DesktopItemIdentityInfo> current = new()
        {
            ["same.txt"] = Physical(@"C:\Desktop\same.txt", "volume:old", 40)
        };
        Dictionary<string, DesktopItemIdentityInfo> next = new()
        {
            ["same.txt"] = Physical(@"C:\Desktop\same.txt", "volume:new", 50)
        };

        HashSet<string> result = DesktopNewItemDetector.FindNewItemNames(current, next);

        CollectionAssert.AreEquivalent(new[] { "same.txt" }, result.ToArray());
    }

    [TestMethod]
    public void FindNewItemNames_MissingFileIdFallsBackToSamePath()
    {
        Dictionary<string, DesktopItemIdentityInfo> current = new()
        {
            ["unknown.txt"] = Physical(@"C:\Desktop\unknown.txt", null, null)
        };
        Dictionary<string, DesktopItemIdentityInfo> next = new()
        {
            ["unknown.txt"] = Physical(@"C:\Desktop\unknown.txt", null, null)
        };

        HashSet<string> result = DesktopNewItemDetector.FindNewItemNames(current, next);

        Assert.AreEqual(0, result.Count);
    }

    private static DesktopItemIdentityInfo Physical(
        string path,
        string? fileId,
        long? creationTimeUtcTicks) => new()
    {
        Kind = DesktopItemKind.FileSystem,
        LastKnownPath = path,
        FileId = fileId,
        CreationTimeUtcTicks = creationTimeUtcTicks,
        IsDirectory = false
    };
}
