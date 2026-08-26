using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class AutoClassificationCategoryPolicyTests
{
    private static readonly DesktopCategoryDefinition Folders = new("folders", "文件夹", 30);
    private static readonly DesktopCategoryDefinition DevelopmentProjects =
        new("development-projects", "开发项目", 20);

    [TestMethod]
    public void BuildEffectiveLookup_UnreliableExistingAutoMember_PreservesCurrentCategory()
    {
        var scanned = new Dictionary<string, DesktopCategoryDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Project"] = Folders
        };
        var group = new GroupInfo
        {
            IsAutoCategory = true,
            AutoCategoryKey = "development-projects",
            Name = "我的项目",
            ItemNames = new List<string> { "Project" }
        };

        Dictionary<string, DesktopCategoryDefinition> effective =
            AutoClassificationCategoryPolicy.BuildEffectiveLookup(
                scanned,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new[] { group });

        Assert.AreEqual("development-projects", effective["Project"].Key);
        Assert.AreEqual("我的项目", effective["Project"].DisplayName);
    }

    [TestMethod]
    public void BuildEffectiveLookup_ReliableExistingAutoMember_UsesScannedCategory()
    {
        var scanned = new Dictionary<string, DesktopCategoryDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Project"] = Folders
        };
        var group = new GroupInfo
        {
            IsAutoCategory = true,
            AutoCategoryKey = "development-projects",
            ItemNames = new List<string> { "Project" }
        };

        Dictionary<string, DesktopCategoryDefinition> effective =
            AutoClassificationCategoryPolicy.BuildEffectiveLookup(
                scanned,
                new HashSet<string>(new[] { "Project" }, StringComparer.OrdinalIgnoreCase),
                new[] { group });

        Assert.AreEqual("folders", effective["Project"].Key);
    }

    [TestMethod]
    public void BuildEffectiveLookup_UnreliableFreeItem_UsesScannedFallback()
    {
        var scanned = new Dictionary<string, DesktopCategoryDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Loose"] = DevelopmentProjects
        };

        Dictionary<string, DesktopCategoryDefinition> effective =
            AutoClassificationCategoryPolicy.BuildEffectiveLookup(
                scanned,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                Array.Empty<GroupInfo>());

        Assert.AreEqual("development-projects", effective["Loose"].Key);
    }
}
