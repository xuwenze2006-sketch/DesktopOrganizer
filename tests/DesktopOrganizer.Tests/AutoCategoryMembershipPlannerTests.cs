using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class AutoCategoryMembershipPlannerTests
{
    private static readonly DesktopCategoryDefinition Folders = new("folders", "文件夹", 30);
    private static readonly DesktopCategoryDefinition DevelopmentProjects =
        new("development-projects", "开发项目", 20);

    [TestMethod]
    public void Plan_ReliableChangedAutoMember_ReturnsMove()
    {
        GroupInfo source = CreateAutoGroup("source", "folders", "Project");
        var categories = new Dictionary<string, DesktopCategoryDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Project"] = DevelopmentProjects
        };

        IReadOnlyList<AutoCategoryMembershipMove> moves =
            AutoCategoryMembershipPlanner.Plan(
                new[] { source },
                categories,
                new HashSet<string>(new[] { "Project" }, StringComparer.OrdinalIgnoreCase),
                paused: false);

        Assert.AreEqual(1, moves.Count);
        Assert.AreEqual("Project", moves[0].ItemName);
        Assert.AreEqual("source", moves[0].SourceGroupId);
        Assert.AreEqual("development-projects", moves[0].TargetCategory.Key);
    }

    [TestMethod]
    public void Plan_UnreliableChangedAutoMember_DoesNotMove()
    {
        GroupInfo source = CreateAutoGroup("source", "development-projects", "Project");
        var categories = new Dictionary<string, DesktopCategoryDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Project"] = Folders
        };

        IReadOnlyList<AutoCategoryMembershipMove> moves =
            AutoCategoryMembershipPlanner.Plan(
                new[] { source },
                categories,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                paused: false);

        Assert.AreEqual(0, moves.Count);
    }

    [TestMethod]
    public void Plan_ManualGroupMember_DoesNotMove()
    {
        var manualGroup = new GroupInfo
        {
            Id = "manual",
            IsAutoCategory = false,
            ItemNames = new List<string> { "Project" }
        };
        var categories = new Dictionary<string, DesktopCategoryDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Project"] = DevelopmentProjects
        };

        IReadOnlyList<AutoCategoryMembershipMove> moves =
            AutoCategoryMembershipPlanner.Plan(
                new[] { manualGroup },
                categories,
                new HashSet<string>(new[] { "Project" }, StringComparer.OrdinalIgnoreCase),
                paused: false);

        Assert.AreEqual(0, moves.Count);
    }

    [TestMethod]
    public void Plan_WhenPaused_DoesNotMove()
    {
        GroupInfo source = CreateAutoGroup("source", "folders", "Project");
        var categories = new Dictionary<string, DesktopCategoryDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Project"] = DevelopmentProjects
        };

        IReadOnlyList<AutoCategoryMembershipMove> moves =
            AutoCategoryMembershipPlanner.Plan(
                new[] { source },
                categories,
                new HashSet<string>(new[] { "Project" }, StringComparer.OrdinalIgnoreCase),
                paused: true);

        Assert.AreEqual(0, moves.Count);
    }

    [TestMethod]
    public void Plan_UnchangedCategory_DoesNotMove()
    {
        GroupInfo source = CreateAutoGroup("source", "folders", "Folder");
        var categories = new Dictionary<string, DesktopCategoryDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Folder"] = Folders
        };

        IReadOnlyList<AutoCategoryMembershipMove> moves =
            AutoCategoryMembershipPlanner.Plan(
                new[] { source },
                categories,
                new HashSet<string>(new[] { "Folder" }, StringComparer.OrdinalIgnoreCase),
                paused: false);

        Assert.AreEqual(0, moves.Count);
    }

    [TestMethod]
    public void Apply_ExistingTarget_MergesAndRemovesEmptySource()
    {
        GroupInfo source = CreateAutoGroup("source", "folders", "Project");
        GroupInfo target = CreateAutoGroup("target", "development-projects", "Existing");
        var groups = new List<GroupInfo> { source, target };
        var moves = new[]
        {
            new AutoCategoryMembershipMove("Project", "source", DevelopmentProjects)
        };

        AutoCategoryMembershipUpdateResult result = AutoCategoryMembershipPlanner.Apply(
            groups,
            moves,
            _ => true,
            category => CreateAutoGroup("created", category.Key));

        Assert.IsTrue(result.Changed);
        Assert.AreEqual(1, groups.Count);
        Assert.AreSame(target, groups[0]);
        CollectionAssert.AreEqual(new[] { "Existing", "Project" }, target.ItemNames);
    }

    [TestMethod]
    public void Apply_MissingTarget_CreatesGroupAndKeepsNonEmptySource()
    {
        GroupInfo source = CreateAutoGroup("source", "folders", "Keep", "Project");
        var groups = new List<GroupInfo> { source };
        var moves = new[]
        {
            new AutoCategoryMembershipMove("Project", "source", DevelopmentProjects)
        };
        int createCount = 0;

        AutoCategoryMembershipUpdateResult result = AutoCategoryMembershipPlanner.Apply(
            groups,
            moves,
            _ => true,
            category =>
            {
                createCount++;
                return CreateAutoGroup("created", category.Key);
            });

        Assert.IsTrue(result.Changed);
        Assert.AreEqual(1, createCount);
        Assert.AreEqual(2, groups.Count);
        CollectionAssert.AreEqual(new[] { "Keep" }, source.ItemNames);
        GroupInfo created = groups.Single(group => group.Id == "created");
        CollectionAssert.AreEqual(new[] { "Project" }, created.ItemNames);
    }

    [TestMethod]
    public void Apply_SwappingCategories_PreservesBothGroups()
    {
        GroupInfo folders = CreateAutoGroup("folders-group", "folders", "Project");
        GroupInfo projects = CreateAutoGroup("projects-group", "development-projects", "Folder");
        var groups = new List<GroupInfo> { folders, projects };
        var moves = new[]
        {
            new AutoCategoryMembershipMove("Project", "folders-group", DevelopmentProjects),
            new AutoCategoryMembershipMove("Folder", "projects-group", Folders)
        };

        AutoCategoryMembershipUpdateResult result = AutoCategoryMembershipPlanner.Apply(
            groups,
            moves,
            _ => true,
            category => CreateAutoGroup("unexpected", category.Key));

        Assert.IsTrue(result.Changed);
        Assert.AreEqual(2, groups.Count);
        CollectionAssert.AreEqual(new[] { "Folder" }, folders.ItemNames);
        CollectionAssert.AreEqual(new[] { "Project" }, projects.ItemNames);
    }

    private static GroupInfo CreateAutoGroup(string id, string categoryKey, params string[] names) =>
        new()
        {
            Id = id,
            IsAutoCategory = true,
            AutoCategoryKey = categoryKey,
            ItemNames = names.ToList()
        };
}
