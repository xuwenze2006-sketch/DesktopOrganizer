using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class ShellDesktopItemPolicyTests
{
    [TestMethod]
    [DataRow("::{645FF040-5081-101B-9F08-00AA002F954E}")]
    [DataRow("::{645ff040-5081-101b-9f08-00aa002f954e}")]
    [DataRow("shell:::{645FF040-5081-101B-9F08-00AA002F954E}")]
    [DataRow("::{00021400-0000-0000-C000-000000000046}\\::{645FF040-5081-101B-9F08-00AA002F954E}")]
    public void IsRecycleBin_RecognizesStableClassIdInCommonParsingForms(string parsingName)
    {
        Assert.IsTrue(ShellDesktopItemPolicy.IsRecycleBin(parsingName));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("回收站")]
    [DataRow("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")] // 此电脑
    [DataRow("::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}")] // 网络
    [DataRow("::{26EE0668-A00A-44D7-9371-BEB064C98683}")] // 控制面板
    [DataRow("::{B2B4A4D1-2754-4140-A2EB-9A76D9D7CDC6}")] // Linux / WSL
    public void IsRecycleBin_RejectsNamesAndOtherShellClassIds(string? parsingName)
    {
        Assert.IsFalse(ShellDesktopItemPolicy.IsRecycleBin(parsingName));
    }

    [TestMethod]
    public void ShouldShowOnDesktop_AllowsRecycleBinOnly()
    {
        var recycleBin = new ShellNamespaceItem(
            "任意本地化名称",
            ShellDesktopItemPolicy.RecycleBinParsingName,
            null,
            true);
        var thisPc = new ShellNamespaceItem(
            "此电脑",
            "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
            null,
            true);

        Assert.IsTrue(ShellDesktopItemPolicy.ShouldShowOnDesktop(recycleBin));
        Assert.IsFalse(ShellDesktopItemPolicy.ShouldShowOnDesktop(thisPc));
    }
}
