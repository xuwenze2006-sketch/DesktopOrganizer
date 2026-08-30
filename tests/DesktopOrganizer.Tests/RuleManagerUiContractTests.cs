using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class RuleManagerUiContractTests
{
    [TestMethod]
    public void DisableAllRulesButton_ExposesBatchStopActionAndScope()
    {
        XDocument document = LoadRuleManagerXaml();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement button = document.Descendants().Single(element =>
            string.Equals(
                (string?)element.Attribute(xaml + "Name"),
                "DisableAllRulesButton",
                StringComparison.Ordinal));

        Assert.AreEqual("DisableAllRules_Click", button.Attribute("Click")?.Value);
        StringAssert.Contains(button.Attribute("ToolTip")?.Value, "保留已有虚拟整理结果");
        StringAssert.Contains(button.Attribute("ToolTip")?.Value, "不修改真实文件");
    }

    private static XDocument LoadRuleManagerXaml(
        [CallerFilePath] string sourceFilePath = "")
    {
        string testDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Cannot resolve the test source directory.");
        string projectRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", ".."));
        return XDocument.Load(Path.Combine(projectRoot, "RuleManagerWindow.xaml"));
    }
}
