using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class ShellItemLocationTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void EncodeAndDecode_RoundTripsParsingNameAndKind(bool isFolder)
    {
        const string parsingName = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";

        string encoded = ShellItemLocation.Encode(parsingName, isFolder);
        bool decoded = ShellItemLocation.TryDecode(
            encoded,
            out string restoredParsingName,
            out bool restoredIsFolder);

        Assert.IsTrue(decoded);
        Assert.AreEqual(parsingName, restoredParsingName);
        Assert.AreEqual(isFolder, restoredIsFolder);
        Assert.IsTrue(encoded.StartsWith("shell-namespace:", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("C:\\Users\\Test\\Desktop\\file.txt")]
    [DataRow("shell-namespace:folder:not-base64")]
    [DataRow("shell-namespace:unknown:YWJj")]
    public void TryDecode_InvalidValue_ReturnsFalse(string? value)
    {
        bool decoded = ShellItemLocation.TryDecode(
            value,
            out string parsingName,
            out bool isFolder);

        Assert.IsFalse(decoded);
        Assert.AreEqual(string.Empty, parsingName);
        Assert.IsFalse(isFolder);
    }

    [TestMethod]
    public void AreEquivalent_UsesCaseInsensitiveShellIdentityAndRequiresMatchingKind()
    {
        string first = ShellItemLocation.Encode(
            "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
            isFolder: true);
        string same = ShellItemLocation.Encode(
            "::{20d04fe0-3aea-1069-a2d8-08002b30309d}",
            isFolder: true);
        string differentKind = ShellItemLocation.Encode(
            "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
            isFolder: false);

        Assert.IsTrue(ShellItemLocation.AreEquivalent(first, same));
        Assert.IsFalse(ShellItemLocation.AreEquivalent(first, differentKind));
    }

    [TestMethod]
    public void AreEquivalent_ForFileSystemLocations_IsCaseInsensitive()
    {
        Assert.IsTrue(ShellItemLocation.AreEquivalent(
            @"C:\Users\Test\Desktop\Readme.txt",
            @"c:\users\test\desktop\readme.txt"));
    }
}
