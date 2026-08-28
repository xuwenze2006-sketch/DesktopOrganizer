using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class FolderPortalServiceTests
{
    [TestMethod]
    public void LayoutPolicy_NormalizesSafelyAndRemovesDuplicateRoots()
    {
        string root = Path.Combine(Path.GetTempPath(), "portal-layout-root");
        List<FolderPortalInfo> normalized = FolderPortalLayoutPolicy.Normalize(
        [
            new FolderPortalInfo
            {
                Id = "same",
                Name = "  资料  ",
                RootPath = root,
                RootIdentity = " root-id ",
                CurrentRelativePath = Path.Combine("child", "..", "outside"),
                X = double.NaN,
                Width = 20,
                Height = 5000
            },
            new FolderPortalInfo
            {
                Id = "other",
                RootPath = root + Path.DirectorySeparatorChar,
                RootIdentity = "root-id"
            },
            new FolderPortalInfo
            {
                RootPath = "relative-root",
                RootIdentity = "relative-id"
            }
        ]);

        Assert.AreEqual(1, normalized.Count);
        FolderPortalInfo portal = normalized[0];
        Assert.AreEqual("资料", portal.Name);
        Assert.AreEqual("root-id", portal.RootIdentity);
        Assert.AreEqual(string.Empty, portal.CurrentRelativePath);
        Assert.AreEqual(40, portal.X);
        Assert.AreEqual(300, portal.Width);
        Assert.AreEqual(680, portal.Height);
    }

    [TestMethod]
    public void TryResolveRelativePath_RejectsRootedAndParentTraversalPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "portal-root");

        Assert.IsFalse(FolderPortalService.TryResolveRelativePath(
            "relative-root",
            "child",
            out _,
            out _,
            out _));
        Assert.IsFalse(FolderPortalService.TryResolveRelativePath(
            root,
            Path.Combine(Path.GetPathRoot(root)!, "outside"),
            out _,
            out _,
            out _));
        Assert.IsFalse(FolderPortalService.TryResolveRelativePath(
            root,
            Path.Combine("child", "..", "outside"),
            out _,
            out _,
            out _));
    }

    [TestMethod]
    public void IsPathWithinRoot_RejectsSimilarPrefixOutsideRoot()
    {
        string parent = Path.Combine(Path.GetTempPath(), "portal");
        string child = Path.Combine(parent, "child");
        string similarPrefix = parent + "-other";

        Assert.IsTrue(FolderPortalService.IsPathWithinRoot(parent, parent));
        Assert.IsTrue(FolderPortalService.IsPathWithinRoot(parent, child));
        Assert.IsFalse(FolderPortalService.IsPathWithinRoot(parent, similarPrefix));
    }

    [TestMethod]
    public void Read_EnumeratesOnlyTopDirectory()
    {
        string root = CreateTestDirectory();
        try
        {
            string directFile = Path.Combine(root, "direct.txt");
            string childDirectory = Path.Combine(root, "child");
            string nestedFile = Path.Combine(childDirectory, "nested.txt");
            File.WriteAllText(directFile, "direct");
            Directory.CreateDirectory(childDirectory);
            File.WriteAllText(nestedFile, "nested");

            FolderPortalService service = CreateServiceWithIdentity("root-id");
            PortalReadResult result = service.Read(CreatePortal(root, "root-id"));

            Assert.IsTrue(result.Success, result.ErrorMessage);
            CollectionAssert.AreEquivalent(
                new[] { "direct.txt", "child" },
                result.Entries.Select(entry => entry.Name).ToArray());
            Assert.IsFalse(result.Entries.Any(entry => entry.FullPath.Equals(
                nestedFile,
                StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Read_StopsAfterDetectingFiveHundredAndFirstEntry()
    {
        string root = Path.Combine(Path.GetTempPath(), "portal-limit-root");
        int yielded = 0;
        IEnumerable<string> Enumerate(string _)
        {
            for (int index = 0; index < 800; index++)
            {
                yielded++;
                yield return Path.Combine(root, $"item-{index:0000}.txt");
            }
        }

        var service = new FolderPortalService(
            ReadExpectedIdentity,
            _ => true,
            Enumerate,
            _ => FileAttributes.Normal,
            _ => DateTime.UnixEpoch);

        PortalReadResult result = service.Read(CreatePortal(root, "root-id"));

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual(FolderPortalService.MaximumEntryCount, result.Entries.Count);
        Assert.IsTrue(result.IsTruncated);
        Assert.AreEqual(FolderPortalService.MaximumEntryCount + 1, yielded);
    }

    [TestMethod]
    public void Read_ReparseDirectoryIsVisibleButCannotNavigate()
    {
        string root = Path.Combine(Path.GetTempPath(), "portal-reparse-root");
        string link = Path.Combine(root, "linked-folder");
        int enumerationCalls = 0;
        var service = new FolderPortalService(
            ReadExpectedIdentity,
            path => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                    path.Equals(link, StringComparison.OrdinalIgnoreCase),
            path =>
            {
                enumerationCalls++;
                return path.Equals(root, StringComparison.OrdinalIgnoreCase)
                    ? new[] { link }
                    : Array.Empty<string>();
            },
            path => path.Equals(link, StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.Directory | FileAttributes.ReparsePoint
                : FileAttributes.Directory,
            _ => DateTime.UnixEpoch);

        PortalReadResult rootResult = service.Read(CreatePortal(root, "root-id"));
        PortalDirectoryEntry entry = rootResult.Entries.Single();

        Assert.IsTrue(rootResult.Success, rootResult.ErrorMessage);
        Assert.IsTrue(entry.IsDirectory);
        Assert.IsTrue(entry.IsReparsePoint);
        Assert.IsFalse(entry.CanNavigate);

        FolderPortalInfo nestedPortal = CreatePortal(root, "root-id");
        nestedPortal.CurrentRelativePath = "linked-folder";
        PortalReadResult nestedResult = service.Read(nestedPortal);

        Assert.IsFalse(nestedResult.Success);
        StringAssert.Contains(nestedResult.ErrorMessage, "重解析点");
        Assert.AreEqual(1, enumerationCalls, "拒绝重解析点时不应枚举链接目标。");
    }

    [TestMethod]
    public void Read_ChangedRootIdentityDoesNotInvokeEnumerator()
    {
        string root = Path.Combine(Path.GetTempPath(), "portal-identity-root");
        int enumerationCalls = 0;
        var service = new FolderPortalService(
            (string _, out string identity) =>
            {
                identity = "replacement-id";
                return true;
            },
            _ => true,
            _ =>
            {
                enumerationCalls++;
                return Array.Empty<string>();
            },
            _ => FileAttributes.Directory,
            _ => DateTime.UnixEpoch);

        PortalReadResult result = service.Read(CreatePortal(root, "original-id"));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ErrorMessage, "身份已改变");
        Assert.AreEqual(0, enumerationCalls);
    }

    [TestMethod]
    public void Read_RootIdentityChangedDuringValidationDoesNotInvokeEnumerator()
    {
        string root = Path.Combine(Path.GetTempPath(), "portal-race-root");
        int identityReads = 0;
        int enumerationCalls = 0;
        var service = new FolderPortalService(
            (string _, out string identity) =>
            {
                identity = ++identityReads == 1 ? "root-id" : "replacement-id";
                return true;
            },
            _ => true,
            _ =>
            {
                enumerationCalls++;
                return Array.Empty<string>();
            },
            _ => FileAttributes.Directory,
            _ => DateTime.UnixEpoch);

        PortalReadResult result = service.Read(CreatePortal(root, "root-id"));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ErrorMessage, "读取前已改变");
        Assert.AreEqual(2, identityReads);
        Assert.AreEqual(0, enumerationCalls);
    }

    [TestMethod]
    public void Read_EnumeratorReturningNonDirectChildFailsClosed()
    {
        string root = Path.Combine(Path.GetTempPath(), "portal-boundary-root");
        string nested = Path.Combine(root, "child", "nested.txt");
        var service = new FolderPortalService(
            ReadExpectedIdentity,
            _ => true,
            _ => new[] { nested },
            _ => FileAttributes.Normal,
            _ => DateTime.UnixEpoch);

        PortalReadResult result = service.Read(CreatePortal(root, "root-id"));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.Entries.Count);
        StringAssert.Contains(result.ErrorMessage, "指定目录之外");
    }

    [TestMethod]
    public void Read_CanceledRequestNeverInvokesEnumerator()
    {
        string root = Path.Combine(Path.GetTempPath(), "portal-canceled-root");
        int enumerationCalls = 0;
        var service = new FolderPortalService(
            ReadExpectedIdentity,
            _ => true,
            _ =>
            {
                enumerationCalls++;
                return Array.Empty<string>();
            },
            _ => FileAttributes.Directory,
            _ => DateTime.UnixEpoch);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            service.Read(CreatePortal(root, "root-id"), cancellation.Token));
        Assert.AreEqual(0, enumerationCalls);
    }

    private static FolderPortalService CreateServiceWithIdentity(string expectedIdentity) => new(
        (string _, out string identity) =>
        {
            identity = expectedIdentity;
            return true;
        },
        Directory.Exists,
        path => Directory.EnumerateFileSystemEntries(
            path,
            "*",
            SearchOption.TopDirectoryOnly),
        File.GetAttributes,
        File.GetLastWriteTimeUtc);

    private static FolderPortalInfo CreatePortal(string rootPath, string rootIdentity) => new()
    {
        RootPath = rootPath,
        RootIdentity = rootIdentity
    };

    private static bool ReadExpectedIdentity(string _, out string identity)
    {
        identity = "root-id";
        return true;
    }

    private static string CreateTestDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "DesktopOrganizer.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
