using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Concurrent;
using System.IO;
using System.Collections;
using System.Reflection;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class FolderPortalWatcherCoordinatorTests
{
    [TestMethod]
    public async Task WatcherError_RefreshesAndRebindsThenReceivesNewFileEvents()
    {
        string root = CreateTestDirectory();
        try
        {
            var errors = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            var changes = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var coordinator = new FolderPortalWatcherCoordinator((_, version) =>
            {
                if (!errors.TrySetResult(version)) changes.TrySetResult(version);
            }, TimeSpan.FromMilliseconds(30));
            Assert.IsTrue(coordinator.TryBind("portal", root));
            FileSystemWatcher old = GetWatcher(coordinator, "portal");
            RaiseError(old);
            long version = await errors.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsTrue(coordinator.IsCurrentNotification("portal", version));
            Assert.IsTrue(coordinator.TryBind("portal", root));
            Assert.AreNotSame(old, GetWatcher(coordinator, "portal"));
            Assert.IsFalse(coordinator.IsCurrentNotification("portal", version));

            RaiseError(old); // 旧监听的迟到错误不能破坏新监听或发出额外通知。
            await Task.Delay(100);
            Assert.IsFalse(changes.Task.IsCompleted);
            File.WriteAllText(Path.Combine(root, "new.txt"), "content");
            long newVersion = await changes.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(coordinator.IsCurrentNotification("portal", newVersion));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task WatcherError_AfterUnbindOrDisposeDoesNotPublishRefresh()
    {
        string root = CreateTestDirectory();
        try
        {
            int callbacks = 0;
            using var coordinator = new FolderPortalWatcherCoordinator(
                (_, _) => Interlocked.Increment(ref callbacks), TimeSpan.FromMilliseconds(40));
            Assert.IsTrue(coordinator.TryBind("portal", root));
            FileSystemWatcher old = GetWatcher(coordinator, "portal");
            RaiseError(old);
            coordinator.Unbind("portal");
            RaiseError(old);
            Assert.IsTrue(coordinator.TryBind("portal", root));
            FileSystemWatcher current = GetWatcher(coordinator, "portal");
            coordinator.Dispose();
            RaiseError(current);
            await Task.Delay(150);
            Assert.AreEqual(0, Volatile.Read(ref callbacks));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static FileSystemWatcher GetWatcher(FolderPortalWatcherCoordinator coordinator, string id)
    {
        var registrations = (IDictionary)typeof(FolderPortalWatcherCoordinator)
            .GetField("_registrations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(coordinator)!;
        object registration = registrations[id]!;
        return (FileSystemWatcher)registration.GetType().GetProperty("Watcher")!.GetValue(registration)!;
    }

    private static void RaiseError(FileSystemWatcher watcher) =>
        typeof(FileSystemWatcher).GetMethod("OnError", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(watcher, [new ErrorEventArgs(new InternalBufferOverflowException("test overflow"))]);

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
