using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class FileOperationIdentityGuardTests
{
    [TestMethod]
    public void Matches_UnchangedFile_ReturnsTrue()
    {
        string root = CreateTestDirectory();
        try
        {
            string path = Path.Combine(root, "item.txt");
            File.WriteAllText(path, "original");

            Assert.IsTrue(FileOperationIdentityGuard.TryCapture(path, out string identity));
            Assert.IsTrue(FileOperationIdentityGuard.Matches(path, identity));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Matches_SamePathReplacementFile_ReturnsFalse()
    {
        string root = CreateTestDirectory();
        try
        {
            string path = Path.Combine(root, "item.txt");
            string originalPath = Path.Combine(root, "original.txt");
            File.WriteAllText(path, "original");
            Assert.IsTrue(FileOperationIdentityGuard.TryCapture(path, out string identity));

            File.Move(path, originalPath);
            File.WriteAllText(path, "replacement");

            Assert.IsFalse(FileOperationIdentityGuard.Matches(path, identity));
            Assert.IsTrue(FileOperationIdentityGuard.Matches(originalPath, identity));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Matches_SamePathReplacementDirectory_ReturnsFalse()
    {
        string root = CreateTestDirectory();
        try
        {
            string path = Path.Combine(root, "folder");
            string originalPath = Path.Combine(root, "original-folder");
            Directory.CreateDirectory(path);
            Assert.IsTrue(FileOperationIdentityGuard.TryCapture(path, out string identity));

            Directory.Move(path, originalPath);
            Directory.CreateDirectory(path);

            Assert.IsFalse(FileOperationIdentityGuard.Matches(path, identity));
            Assert.IsTrue(FileOperationIdentityGuard.Matches(originalPath, identity));
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
