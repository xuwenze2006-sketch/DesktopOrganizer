using System.IO;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopPathResolverTests
{
    private const string CommonPath = @"C:\Users\Public\Desktop";

    [TestMethod]
    [DataRow(@"D:\用户文件\桌面")]
    [DataRow(@"C:\Users\测试用户\OneDrive\Desktop")]
    [DataRow(@"\\server\users\小明\桌面")]
    public void Refresh_UsesReportedDesktopPathsWithoutGuessing(string userPath)
    {
        var queried = new List<Environment.SpecialFolder>();
        var checkedPaths = new List<string>();
        var resolver = new DesktopPathResolver(folder =>
        {
            queried.Add(folder);
            return folder == Environment.SpecialFolder.DesktopDirectory ? userPath : CommonPath;
        }, path =>
        {
            checkedPaths.Add(path);
            return true;
        });

        Assert.IsFalse(resolver.Current.IsAvailable);
        DesktopPathSnapshot snapshot = resolver.Refresh();

        Assert.IsTrue(snapshot.IsAvailable);
        Assert.AreEqual(userPath, snapshot.UserDesktop.Path);
        Assert.AreEqual(userPath, snapshot.UserDesktop.LastKnownGoodPath);
        Assert.AreEqual(CommonPath, snapshot.CommonDesktop.Path);
        Assert.IsNull(snapshot.UserDesktop.ErrorMessage);
        CollectionAssert.AreEqual(new[]
        {
            Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolder.CommonDesktopDirectory
        }, queried.ToArray());
        CollectionAssert.AreEqual(new[] { userPath, CommonPath }, checkedPaths.ToArray());
        Assert.AreSame(snapshot, resolver.Current);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow(@"relative\Desktop")]
    public void Refresh_UnresolvedUserPathDoesNotBecomeAnEmptySuccessfulDesktop(string userPath)
    {
        var checkedPaths = new List<string>();
        var resolver = new DesktopPathResolver(
            folder => folder == Environment.SpecialFolder.DesktopDirectory ? userPath : CommonPath,
            path => { checkedPaths.Add(path); return true; });

        DesktopPathSnapshot snapshot = resolver.Refresh();

        Assert.IsFalse(snapshot.IsAvailable);
        Assert.IsFalse(snapshot.UserDesktop.IsAvailable);
        Assert.IsNull(snapshot.UserDesktop.Path);
        Assert.IsNull(snapshot.UserDesktop.LastKnownGoodPath);
        Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot.UserDesktop.ErrorMessage));
        Assert.IsTrue(snapshot.CommonDesktop.IsAvailable);
        CollectionAssert.AreEqual(new[] { CommonPath }, checkedPaths.ToArray());
    }

    [TestMethod]
    public void Refresh_MissingCommonDesktopRemainsAnUnavailableSource()
    {
        const string userPath = @"D:\用户文件\桌面";
        var resolver = new DesktopPathResolver(
            folder => folder == Environment.SpecialFolder.DesktopDirectory ? userPath : CommonPath,
            path => path != CommonPath);

        DesktopPathSnapshot snapshot = resolver.Refresh();

        Assert.IsTrue(snapshot.UserDesktop.IsAvailable);
        Assert.IsFalse(snapshot.CommonDesktop.IsAvailable);
        Assert.AreEqual(CommonPath, snapshot.CommonDesktop.Path);
        Assert.IsNull(snapshot.CommonDesktop.LastKnownGoodPath);
        Assert.IsFalse(snapshot.IsAvailable);
    }

    [TestMethod]
    public void Refresh_PathMigrationAndRecoveryAreReportedWithoutUsingOldPathAsFallback()
    {
        string userPath = @"D:\原桌面";
        bool exists = true;
        var resolver = new DesktopPathResolver(
            folder => folder == Environment.SpecialFolder.DesktopDirectory ? userPath : CommonPath,
            _ => exists);

        DesktopPathSnapshot original = resolver.Refresh();
        Assert.AreEqual(original.Revision, resolver.Refresh().Revision);
        userPath = @"D:\新桌面";
        DesktopPathSnapshot migrated = resolver.Refresh();
        Assert.IsTrue(migrated.Revision > original.Revision);
        Assert.AreEqual(userPath, migrated.UserDesktop.Path);
        Assert.AreEqual(userPath, migrated.UserDesktop.LastKnownGoodPath);

        userPath = string.Empty;
        DesktopPathSnapshot unresolved = resolver.Refresh();
        Assert.IsTrue(unresolved.Revision > migrated.Revision);
        Assert.IsNull(unresolved.UserDesktop.Path);
        Assert.AreEqual(migrated.UserDesktop.Path, unresolved.UserDesktop.LastKnownGoodPath);
        Assert.IsFalse(unresolved.IsAvailable);
        Assert.AreEqual(unresolved.Revision, resolver.Refresh().Revision);

        userPath = @"D:\新桌面";
        exists = false;
        DesktopPathSnapshot missing = resolver.Refresh();
        Assert.IsFalse(missing.IsAvailable);
        Assert.AreEqual(userPath, missing.UserDesktop.Path);
        Assert.AreEqual(userPath, missing.UserDesktop.LastKnownGoodPath);
        exists = true;
        DesktopPathSnapshot recovered = resolver.Refresh();
        Assert.IsTrue(recovered.IsAvailable);
        Assert.IsTrue(recovered.Revision > missing.Revision);
        Assert.AreEqual(userPath, recovered.UserDesktop.Path);
        Assert.IsTrue(original.IsAvailable, "之前返回的快照不能随后改变。");
        Assert.AreEqual(@"D:\原桌面", original.UserDesktop.Path);
    }

    [TestMethod]
    public void Refresh_CasingAndTrailingSeparatorDoNotSignalPathMigration()
    {
        string userPath = @"D:\Users\Example\Desktop";
        var resolver = new DesktopPathResolver(
            folder => folder == Environment.SpecialFolder.DesktopDirectory ? userPath : CommonPath,
            _ => true);
        DesktopPathSnapshot first = resolver.Refresh();
        userPath = @"d:\users\example\desktop\";

        Assert.AreEqual(first.Revision, resolver.Refresh().Revision);
    }

    [TestMethod]
    public void Refresh_LookupOrAccessFailurePreservesLastKnownGoodWithoutClaimingAvailability()
    {
        const string userPath = @"D:\用户文件\桌面";
        bool lookupFails = false;
        bool accessFails = false;
        var resolver = new DesktopPathResolver(folder =>
        {
            if (lookupFails && folder == Environment.SpecialFolder.DesktopDirectory)
                throw new IOException("lookup unavailable");
            return folder == Environment.SpecialFolder.DesktopDirectory ? userPath : CommonPath;
        }, path => accessFails && path == userPath
            ? throw new UnauthorizedAccessException("access denied")
            : true);
        resolver.Refresh();

        lookupFails = true;
        DesktopPathSnapshot lookupFailure = resolver.Refresh();
        Assert.IsFalse(lookupFailure.IsAvailable);
        Assert.IsNull(lookupFailure.UserDesktop.Path);
        Assert.AreEqual(userPath, lookupFailure.UserDesktop.LastKnownGoodPath);
        StringAssert.Contains(lookupFailure.UserDesktop.ErrorMessage, "lookup unavailable");

        lookupFails = false;
        accessFails = true;
        DesktopPathSnapshot accessFailure = resolver.Refresh();
        Assert.IsFalse(accessFailure.IsAvailable);
        Assert.AreEqual(userPath, accessFailure.UserDesktop.Path);
        Assert.AreEqual(userPath, accessFailure.UserDesktop.LastKnownGoodPath);
        StringAssert.Contains(accessFailure.UserDesktop.ErrorMessage, "access denied");
    }

    [TestMethod]
    public void Refresh_CancellationIsNotConvertedToAnUnavailableDesktop()
    {
        var resolver = new DesktopPathResolver(_ => throw new OperationCanceledException(), _ => true);
        DesktopPathSnapshot previous = resolver.Current;

        Assert.ThrowsExactly<OperationCanceledException>(() => resolver.Refresh());
        Assert.AreSame(previous, resolver.Current);
    }

    [TestMethod]
    public async Task Current_DoesNotBlockWhileSystemLookupIsWaiting()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var resolver = new DesktopPathResolver(folder =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("probe was not released");
            return folder == Environment.SpecialFolder.DesktopDirectory ? @"D:\桌面" : CommonPath;
        }, _ => true);
        DesktopPathSnapshot previous = resolver.Current;
        Task<DesktopPathSnapshot> refresh = Task.Run(resolver.Refresh);
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(3)));
            DesktopPathSnapshot observed = await Task.Run(() => resolver.Current)
                .WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreSame(previous, observed);
        }
        finally
        {
            release.Set();
            await refresh.WaitAsync(TimeSpan.FromSeconds(3));
        }
        Assert.IsTrue(resolver.Current.IsAvailable);
    }
}
