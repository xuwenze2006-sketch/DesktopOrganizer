using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class LayoutContractTests
{
    [TestMethod]
    public void NewLayout_UsesCurrentVersionAndSafeDefaults()
    {
        var layout = new AppLayoutData();

        Assert.AreEqual(19, layout.Version);
        Assert.IsTrue(layout.SnapToGrid);
        Assert.IsTrue(layout.PushReflowEnabled);
        Assert.IsTrue(layout.AutoCollapseControlPanel);
        Assert.IsTrue(layout.CompactGroupLayout);
        Assert.IsTrue(layout.ReserveTemporaryWorkspace);
        Assert.AreEqual(0, layout.FreeIcons.Count);
        Assert.AreEqual(0, layout.Groups.Count);
        Assert.AreEqual(0, layout.Workspaces.Count);
        Assert.AreEqual(0, layout.FolderPortals.Count);
        Assert.IsNull(layout.ActiveWorkspaceId);
        Assert.IsTrue(layout.RecycleBinWidget.IsVisible);
    }

    [TestMethod]
    public void JsonRoundTrip_PreservesLayoutIdentityAndTopologyFields()
    {
        var original = new AppLayoutData
        {
            Version = 19,
            SaveGeneration = 42,
            SnapToGrid = false,
            AutoClassifyNewItems = true,
            ControlPanelX = 123.5,
            ControlPanelY = 456.25,
            RecycleBinWidget = new RecycleBinWidgetLayoutInfo
            {
                X = 1500,
                Y = 820,
                IsVisible = false
            },
            FreeIcons = new Dictionary<string, IconPosition>
            {
                ["readme.txt"] = new IconPosition { X = 11.5, Y = 22.5 }
            },
            Groups =
            [
                new GroupInfo
                {
                    Id = "group-1",
                    Name = "文档",
                    ItemNames = ["readme.txt"],
                    ManuallyAssignedItemNames = ["readme.txt"],
                    SortMode = GroupSortMode.Name,
                    IsAutoCategory = true,
                    AutoCategoryKey = "documents",
                    UserRuleId = null
                }
            ],
            DesktopTopology =
            [
                new DesktopMonitorLayoutInfo
                {
                    DeviceName = "DISPLAY1",
                    BoundsWidth = 1920,
                    BoundsHeight = 1080,
                    WorkWidth = 1920,
                    WorkHeight = 1040,
                    IsPrimary = true,
                    DpiX = 96,
                    DpiY = 96
                }
            ],
            ItemIdentities = new Dictionary<string, DesktopItemIdentityInfo>
            {
                ["readme.txt"] = new DesktopItemIdentityInfo
                {
                    Kind = DesktopItemKind.FileSystem,
                    LastKnownPath = @"C:\Users\Test\Desktop\readme.txt",
                    FileId = "volume:file-id",
                    CreationTimeUtcTicks = 123456789,
                    LastWriteTimeUtcTicks = 223456789
                },
                ["此电脑"] = new DesktopItemIdentityInfo
                {
                    Kind = DesktopItemKind.ShellNamespace,
                    ShellParsingName = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"
                }
            },
            InboxBaselineEstablished = true,
            InboxItems = new Dictionary<string, InboxItemInfo>
            {
                ["readme.txt"] = new InboxItemInfo
                {
                    Identity = new DesktopItemIdentityInfo
                    {
                        LastKnownPath = @"C:\Users\Test\Desktop\readme.txt",
                        FileId = "volume:file-id"
                    },
                    SuggestedCategoryKey = "documents",
                    SuggestedCategoryName = "文档",
                    MatchReason = "扩展名 .txt",
                    Reliability = ClassificationReliability.Reliable
                }
            },
            ItemTags = new Dictionary<string, List<string>>
            {
                ["readme.txt"] = ["重要", "资料"]
            },
            ItemFirstSeenUtcTicks = new Dictionary<string, long>
            {
                ["readme.txt"] = DateTime.UnixEpoch.Ticks
            },
            UserRules =
            [
                new UserOrganizationRuleInfo
                {
                    Id = "rule-1",
                    Name = "重要文档",
                    Lifecycle = UserRuleLifecycle.Previewed,
                    Extensions = [".txt"],
                    ActionKind = OrganizationRuleActionKind.AddTag,
                    ActionTargetName = "重要"
                }
            ],
            FolderPortals =
            [
                new FolderPortalInfo
                {
                    Id = "portal-1",
                    Name = "资料入口",
                    RootPath = @"C:\Data",
                    RootIdentity = "volume:folder-id",
                    CurrentRelativePath = @"Projects\Current",
                    X = 500,
                    Y = 180,
                    Width = 420,
                    Height = 360,
                    IsCollapsed = true
                }
            ]
        };

        string json = JsonSerializer.Serialize(original);
        AppLayoutData restored = JsonSerializer.Deserialize<AppLayoutData>(json)
            ?? throw new AssertFailedException("布局 JSON 反序列化返回 null。");

        Assert.AreEqual(19, restored.Version);
        Assert.AreEqual(42L, restored.SaveGeneration);
        Assert.IsFalse(restored.SnapToGrid);
        Assert.IsTrue(restored.AutoClassifyNewItems);
        Assert.IsTrue(restored.ControlPanelX.HasValue);
        Assert.IsTrue(restored.ControlPanelY.HasValue);
        Assert.AreEqual(123.5, restored.ControlPanelX.GetValueOrDefault());
        Assert.AreEqual(456.25, restored.ControlPanelY.GetValueOrDefault());
        Assert.AreEqual(1500, restored.RecycleBinWidget.X.GetValueOrDefault());
        Assert.AreEqual(820, restored.RecycleBinWidget.Y.GetValueOrDefault());
        Assert.IsFalse(restored.RecycleBinWidget.IsVisible);
        Assert.AreEqual(11.5, restored.FreeIcons["readme.txt"].X);
        Assert.AreEqual(GroupSortMode.Name, restored.Groups[0].SortMode);
        Assert.AreEqual("documents", restored.Groups[0].AutoCategoryKey);
        CollectionAssert.AreEqual(
            new[] { "readme.txt" },
            restored.Groups[0].ManuallyAssignedItemNames);
        Assert.AreEqual(96u, restored.DesktopTopology[0].DpiX);
        Assert.AreEqual("volume:file-id", restored.ItemIdentities["readme.txt"].FileId);
        Assert.AreEqual(223456789L, restored.ItemIdentities["readme.txt"].LastWriteTimeUtcTicks);
        Assert.AreEqual(
            DesktopItemKind.ShellNamespace,
            restored.ItemIdentities["此电脑"].Kind);
        Assert.IsTrue(restored.InboxBaselineEstablished);
        Assert.AreEqual(ClassificationReliability.Reliable, restored.InboxItems["readme.txt"].Reliability);
        CollectionAssert.AreEqual(new[] { "重要", "资料" }, restored.ItemTags["readme.txt"]);
        Assert.AreEqual(DateTime.UnixEpoch.Ticks, restored.ItemFirstSeenUtcTicks["readme.txt"]);
        Assert.AreEqual("重要文档", restored.UserRules[0].Name);
        Assert.AreEqual(UserRuleLifecycle.Previewed, restored.UserRules[0].Lifecycle);
        Assert.AreEqual("portal-1", restored.FolderPortals[0].Id);
        Assert.AreEqual(@"C:\Data", restored.FolderPortals[0].RootPath);
        Assert.AreEqual(@"Projects\Current", restored.FolderPortals[0].CurrentRelativePath);
        Assert.IsTrue(restored.FolderPortals[0].IsCollapsed);
    }

    [TestMethod]
    public void LegacyGroupWithoutManualAssignments_UsesEmptyCompatibleDefault()
    {
        GroupInfo restored = JsonSerializer.Deserialize<GroupInfo>(
            """{"ItemNames":["legacy.txt"]}""")
            ?? throw new AssertFailedException("旧分组 JSON 反序列化返回 null。");

        CollectionAssert.AreEqual(new[] { "legacy.txt" }, restored.ItemNames);
        Assert.IsNotNull(restored.ManuallyAssignedItemNames);
        Assert.AreEqual(0, restored.ManuallyAssignedItemNames.Count);
    }
}
