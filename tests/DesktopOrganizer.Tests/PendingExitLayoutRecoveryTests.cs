using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class PendingExitLayoutRecoveryTests
{
    [TestMethod]
    public void ReadAndPromote_ValidPending_ReplacesMainLayout()
    {
        string root = CreateTestDirectory();
        try
        {
            string pendingPath = Path.Combine(root, "layout.pending-exit.json");
            string layoutPath = Path.Combine(root, "layout.json");
            const string recoveryJson = "{\"Version\":15,\"SaveGeneration\":2,\"FreeIcons\":{}}";
            File.WriteAllText(
                layoutPath,
                "{\"Version\":15,\"SaveGeneration\":1,\"FreeIcons\":{}}");
            File.WriteAllText(pendingPath, recoveryJson);
            DateTime olderWriteTime = DateTime.UtcNow.AddMinutes(-2);
            File.SetLastWriteTimeUtc(pendingPath, olderWriteTime);
            File.SetLastWriteTimeUtc(layoutPath, olderWriteTime.AddMinutes(1));

            PendingExitLayoutRecoveryResult result =
                PendingExitLayoutRecovery.ReadAndPromote(pendingPath, layoutPath);

            Assert.AreEqual(PendingExitLayoutRecoveryState.Promoted, result.State);
            Assert.AreEqual(recoveryJson, result.Json);
            Assert.IsFalse(File.Exists(pendingPath));
            Assert.AreEqual(recoveryJson, File.ReadAllText(layoutPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ReadAndPromote_InvalidPending_QuarantinesOnlyPendingLayout()
    {
        string root = CreateTestDirectory();
        try
        {
            string pendingPath = Path.Combine(root, "layout.pending-exit.json");
            string layoutPath = Path.Combine(root, "layout.json");
            const string mainJson = "{\"Version\":15,\"SaveGeneration\":1,\"FreeIcons\":{}}";
            File.WriteAllText(layoutPath, mainJson);
            File.WriteAllText(pendingPath, "{not-json");

            PendingExitLayoutRecoveryResult result =
                PendingExitLayoutRecovery.ReadAndPromote(pendingPath, layoutPath);

            Assert.AreEqual(PendingExitLayoutRecoveryState.Invalid, result.State);
            Assert.IsNull(result.Json);
            Assert.IsFalse(File.Exists(pendingPath));
            Assert.IsTrue(File.Exists(pendingPath + ".invalid"));
            Assert.AreEqual(mainJson, File.ReadAllText(layoutPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ReadAndPromote_StructurallyInvalidPending_PreservesMainLayout()
    {
        string root = CreateTestDirectory();
        try
        {
            string pendingPath = Path.Combine(root, "layout.pending-exit.json");
            string layoutPath = Path.Combine(root, "layout.json");
            const string mainJson = "{\"Version\":15,\"SaveGeneration\":1,\"FreeIcons\":{}}";
            File.WriteAllText(layoutPath, mainJson);
            File.WriteAllText(
                pendingPath,
                "{\"Version\":15,\"FreeIcons\":[]}");

            PendingExitLayoutRecoveryResult result =
                PendingExitLayoutRecovery.ReadAndPromote(pendingPath, layoutPath);

            Assert.AreEqual(PendingExitLayoutRecoveryState.Invalid, result.State);
            Assert.AreEqual(mainJson, File.ReadAllText(layoutPath));
            Assert.IsFalse(File.Exists(pendingPath));
            Assert.IsTrue(File.Exists(pendingPath + ".invalid"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ReadAndPromote_OlderPending_DoesNotReplaceNewerMain()
    {
        string root = CreateTestDirectory();
        try
        {
            string pendingPath = Path.Combine(root, "layout.pending-exit.json");
            string layoutPath = Path.Combine(root, "layout.json");
            const string mainJson = "{\"Version\":15,\"SaveGeneration\":2,\"FreeIcons\":{}}";
            const string pendingJson = "{\"Version\":15,\"SaveGeneration\":1,\"FreeIcons\":{}}";
            File.WriteAllText(layoutPath, mainJson);
            File.WriteAllText(pendingPath, pendingJson);

            PendingExitLayoutRecoveryResult result =
                PendingExitLayoutRecovery.ReadAndPromote(pendingPath, layoutPath);

            Assert.AreEqual(PendingExitLayoutRecoveryState.Superseded, result.State);
            Assert.IsNull(result.Json);
            Assert.AreEqual(mainJson, File.ReadAllText(layoutPath));
            Assert.AreEqual(pendingJson, File.ReadAllText(pendingPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ReadAndPromote_EqualGenerationWithDifferentContent_PreservesBothLayouts()
    {
        string root = CreateTestDirectory();
        try
        {
            string pendingPath = Path.Combine(root, "layout.pending-exit.json");
            string layoutPath = Path.Combine(root, "layout.json");
            const string mainJson = "{\"Version\":15,\"SaveGeneration\":2,\"SnapToGrid\":true,\"FreeIcons\":{}}";
            const string pendingJson = "{\"Version\":15,\"SaveGeneration\":2,\"SnapToGrid\":false,\"FreeIcons\":{}}";
            File.WriteAllText(layoutPath, mainJson);
            File.WriteAllText(pendingPath, pendingJson);

            PendingExitLayoutRecoveryResult result =
                PendingExitLayoutRecovery.ReadAndPromote(pendingPath, layoutPath);

            Assert.AreEqual(PendingExitLayoutRecoveryState.Conflict, result.State);
            Assert.IsNull(result.Json);
            Assert.AreEqual(mainJson, File.ReadAllText(layoutPath));
            Assert.AreEqual(pendingJson, File.ReadAllText(pendingPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ReadAndPromote_MainLocked_LoadsRecoveryAndRetriesLater()
    {
        string root = CreateTestDirectory();
        try
        {
            string pendingPath = Path.Combine(root, "layout.pending-exit.json");
            string layoutPath = Path.Combine(root, "layout.json");
            const string mainJson = "{\"Version\":15,\"SaveGeneration\":1,\"FreeIcons\":{}}";
            const string recoveryJson = "{\"Version\":15,\"SaveGeneration\":2,\"FreeIcons\":{}}";
            File.WriteAllText(layoutPath, mainJson);
            File.WriteAllText(pendingPath, recoveryJson);

            PendingExitLayoutRecoveryResult deferred;
            using (File.Open(layoutPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                deferred = PendingExitLayoutRecovery.ReadAndPromote(pendingPath, layoutPath);

                Assert.AreEqual(PendingExitLayoutRecoveryState.Deferred, deferred.State);
                Assert.AreEqual(recoveryJson, deferred.Json);
                Assert.IsFalse(string.IsNullOrWhiteSpace(deferred.PendingIdentity));
                Assert.IsTrue(File.Exists(pendingPath));
                Assert.IsFalse(File.Exists(pendingPath + ".invalid"));
            }

            Assert.AreEqual(mainJson, File.ReadAllText(layoutPath));

            PendingExitLayoutRecoveryResult retried =
                PendingExitLayoutRecovery.ReadAndPromote(pendingPath, layoutPath);

            Assert.AreEqual(PendingExitLayoutRecoveryState.Promoted, retried.State);
            Assert.AreEqual(recoveryJson, File.ReadAllText(layoutPath));
            Assert.IsFalse(File.Exists(pendingPath));
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
