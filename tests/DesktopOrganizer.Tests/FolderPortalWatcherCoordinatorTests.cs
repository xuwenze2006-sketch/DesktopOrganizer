using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Concurrent;
using System.IO;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class FolderPortalWatcherCoordinatorTests
{
    [TestMethod]
    public async Task NotifyChanged_DebouncesRepeatedEventsPerPortal()
    {
        string root = CreateTestDirectory();
        try
        {
            var calls = new ConcurrentQueue<string>();
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var coordinator = new FolderPortalWatcherCoordinator(
                (portalId, _) =>
                {
                    calls.Enqueue(portalId);
                    completion.TrySetResult();
                },
                TimeSpan.FromMilliseconds(40));

            Assert.IsTrue(coordinator.TryBind("portal-1", root));
            Assert.IsTrue(coordinator.NotifyChanged("portal-1"));
            Assert.IsTrue(coordinator.NotifyChanged("portal-1"));
            Assert.IsTrue(coordinator.NotifyChanged("portal-1"));

            await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Delay(100);

            CollectionAssert.AreEqual(new[] { "portal-1" }, calls.ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task RebindAndUnbind_CancelOldPendingRefresh()
    {
        string firstRoot = CreateTestDirectory();
        string secondRoot = CreateTestDirectory();
        try
        {
            int callCount = 0;
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var coordinator = new FolderPortalWatcherCoordinator(
                (_, _) =>
                {
                    Interlocked.Increment(ref callCount);
                    completion.TrySetResult();
                },
                TimeSpan.FromMilliseconds(80));

            Assert.IsTrue(coordinator.TryBind("portal-1", firstRoot));
            Assert.IsTrue(coordinator.NotifyChanged("portal-1"));
            Assert.IsTrue(coordinator.TryBind("portal-1", secondRoot));
            Assert.AreEqual(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(secondRoot)),
                coordinator.GetBoundPath("portal-1"));
            await Task.Delay(140);
            Assert.AreEqual(0, Volatile.Read(ref callCount));

            Assert.IsTrue(coordinator.NotifyChanged("portal-1"));
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual(1, Volatile.Read(ref callCount));

            coordinator.Unbind("portal-1");
            Assert.IsFalse(coordinator.NotifyChanged("portal-1"));
            Assert.IsNull(coordinator.GetBoundPath("portal-1"));
        }
        finally
        {
            Directory.Delete(firstRoot, recursive: true);
            Directory.Delete(secondRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task CancelPending_StopsScheduledRefreshWithoutUnbinding()
    {
        string root = CreateTestDirectory();
        try
        {
            int callCount = 0;
            using var coordinator = new FolderPortalWatcherCoordinator(
                (_, _) => Interlocked.Increment(ref callCount),
                TimeSpan.FromMilliseconds(60));

            Assert.IsTrue(coordinator.TryBind("portal-1", root));
            Assert.IsTrue(coordinator.NotifyChanged("portal-1"));
            coordinator.CancelPending("portal-1");

            await Task.Delay(120);

            Assert.AreEqual(0, Volatile.Read(ref callCount));
            Assert.IsTrue(coordinator.IsBound("portal-1"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task CancelPending_InvalidatesAlreadyPublishedNotification()
    {
        string root = CreateTestDirectory();
        try
        {
            var completion = new TaskCompletionSource<long>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var coordinator = new FolderPortalWatcherCoordinator(
                (_, notificationVersion) => completion.TrySetResult(notificationVersion),
                TimeSpan.FromMilliseconds(30));

            Assert.IsTrue(coordinator.TryBind("portal-1", root));
            Assert.IsTrue(coordinator.NotifyChanged("portal-1"));
            long notificationVersion = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsTrue(coordinator.IsCurrentNotification("portal-1", notificationVersion));

            coordinator.CancelPending("portal-1");

            Assert.IsFalse(coordinator.IsCurrentNotification("portal-1", notificationVersion));
            Assert.IsTrue(coordinator.IsBound("portal-1"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task FileSystemEvent_RequestsDebouncedRefresh()
    {
        string root = CreateTestDirectory();
        try
        {
            var completion = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var coordinator = new FolderPortalWatcherCoordinator(
                (portalId, _) => completion.TrySetResult(portalId),
                TimeSpan.FromMilliseconds(80));
            Assert.IsTrue(coordinator.TryBind("portal-1", root));

            File.WriteAllText(Path.Combine(root, "new-item.txt"), "content");

            string portalId = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual("portal-1", portalId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
