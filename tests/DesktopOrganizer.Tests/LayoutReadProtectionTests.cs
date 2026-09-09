using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Reflection;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class LayoutReadProtectionTests
{
    [STATestMethod]
    public void LockedMainLayout_CannotBeOverwrittenAfterLockIsReleased()
    {
        using var f = new Fixture();
        const string original = "{\"Version\":18,\"SaveGeneration\":9,\"FreeIcons\":{}}";
        File.WriteAllText(f.LayoutPath, original);
        using (File.Open(f.LayoutPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            f.Invoke("LoadMainLayoutOrDefault");

        f.Invoke("QueueLayoutWrite");
        f.Invoke("SaveLayoutNow");
        Assert.AreEqual(0, f.BackgroundWrites);
        Assert.AreEqual(original, File.ReadAllText(f.LayoutPath));
        Assert.IsFalse(File.Exists(f.PendingPath));
        Assert.IsTrue(f.Field<bool>("_layoutWriteProtected"));
    }

    [STATestMethod]
    public void OldPendingSnapshot_WhenMainIsLocked_CannotReplaceNewerMain()
    {
        using var f = new Fixture();
        const string original = "{\"Version\":18,\"SaveGeneration\":9,\"FreeIcons\":{}}";
        const string pending = "{\"Version\":18,\"SaveGeneration\":1,\"FreeIcons\":{}}";
        File.WriteAllText(f.LayoutPath, original);
        File.WriteAllText(f.PendingPath, pending);
        using (File.Open(f.LayoutPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            f.Invoke("LoadLayout");

        f.Invoke("SaveLayoutNow");
        Assert.AreEqual(original, File.ReadAllText(f.LayoutPath));
        Assert.AreEqual(pending, File.ReadAllText(f.PendingPath));
        Assert.IsTrue(f.Field<bool>("_layoutWriteProtected"));
    }

    [STATestMethod]
    public void MissingLayout_CanStillBeCreated()
    {
        using var f = new Fixture();
        f.Invoke("LoadMainLayoutOrDefault");
        f.Invoke("SaveLayoutNow");
        Assert.IsFalse(f.Field<bool>("_layoutWriteProtected"));
        Assert.IsTrue(File.Exists(f.LayoutPath));
        Assert.IsNotNull(LayoutJsonSerializer.Deserialize(File.ReadAllText(f.LayoutPath)).Layout);
    }

    [STATestMethod]
    public void InvalidLayout_IsPreservedBeforeSavingReplacement()
    {
        using var f = new Fixture();
        const string broken = "{broken layout";
        File.WriteAllText(f.LayoutPath, broken);
        f.Invoke("LoadMainLayoutOrDefault");
        f.Invoke("SaveLayout");
        f.Invoke("SaveLayoutNow");
        string[] backups = Directory.GetFiles(f.Root, "layout.corrupt-*.json");
        Assert.HasCount(1, backups);
        Assert.AreEqual(broken, File.ReadAllText(backups[0]));
        Assert.IsNotNull(LayoutJsonSerializer.Deserialize(File.ReadAllText(f.LayoutPath)).Layout);
    }

    [TestMethod]
    public void PreservedRecovery_AndLatestExitSnapshotBothSurvive()
    {
        string root = CreateRoot();
        try
        {
            string pending = Path.Combine(root, "layout.pending-exit.json");
            File.WriteAllText(pending, "original ambiguous snapshot");
            PendingExitLayoutRecovery.WriteSnapshot(pending, "latest snapshot", preserveExisting: true);
            Assert.AreEqual("latest snapshot", File.ReadAllText(pending));
            string[] preserved = Directory.GetFiles(root, "*.preserved-*.json");
            Assert.HasCount(1, preserved);
            Assert.AreEqual("original ambiguous snapshot", File.ReadAllText(preserved[0]));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void LockedRecovery_PreservationFailureDoesNotOverwriteOriginal()
    {
        string root = CreateRoot();
        try
        {
            string pending = Path.Combine(root, "layout.pending-exit.json");
            File.WriteAllText(pending, "original");
            using (File.Open(pending, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.ThrowsExactly<IOException>(() =>
                    PendingExitLayoutRecovery.WriteSnapshot(pending, "latest", preserveExisting: true));
            Assert.AreEqual("original", File.ReadAllText(pending));
            PendingExitLayoutRecovery.WriteSnapshot(pending, "latest", preserveExisting: true);
            Assert.AreEqual("latest", File.ReadAllText(pending));
            Assert.HasCount(1, Directory.GetFiles(root, "*.preserved-*.json"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "DesktopOrganizer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        public string Root { get; } = CreateRoot();
        public string LayoutPath => Path.Combine(Root, "layout.json");
        public string PendingPath => Path.Combine(Root, "layout.pending-exit.json");
        public MainWindow Window { get; }
        public int BackgroundWrites;
        public Fixture()
        {
            Window = new MainWindow(false, _ => Interlocked.Increment(ref BackgroundWrites));
            Set("_layoutFilePath", LayoutPath);
            Set("_layoutExitRecoveryPath", PendingPath);
        }
        public T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, Flags)!.GetValue(Window)!;
        private void Set(string name, object value) => typeof(MainWindow).GetField(name, Flags)!.SetValue(Window, value);
        public void Invoke(string name) => typeof(MainWindow).GetMethod(name, Flags)!.Invoke(Window, null);
        public void Dispose()
        {
            Field<DispatcherTimer>("_layoutSaveTimer").Stop();
            Set("_isClosing", true);
            Field<CancellationTokenSource>("_lifetimeCts").Cancel();
            Assert.IsTrue(SpinWait.SpinUntil(() => Field<int>("_layoutWriterRunning") == 0, TimeSpan.FromSeconds(5)));
            Field<FileOperationService>("_fileOperationService").Dispose();
            Field<FolderPortalWatcherCoordinator>("_folderPortalWatcherCoordinator").Dispose();
            Field<CancellationTokenSource>("_lifetimeCts").Dispose();
            Field<SemaphoreSlim>("_layoutWriteGate").Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }
}
