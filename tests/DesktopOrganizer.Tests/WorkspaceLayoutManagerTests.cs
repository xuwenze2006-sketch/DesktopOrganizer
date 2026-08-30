using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class WorkspaceLayoutManagerTests
{
    [TestMethod]
    public void CreateAndActivate_CapturesIndependentVisualSnapshot()
    {
        var layout = CreateLayout("one.txt", 10);
        DateTime now = new(2026, 8, 28, 1, 2, 3, DateTimeKind.Utc);

        WorkspaceProfileInfo workspace = WorkspaceLayoutManager.CreateAndActivate(layout, "工作", now);
        layout.FreeIcons["one.txt"].X = 999;
        layout.Groups[0].Name = "changed";
        layout.Groups[0].ManuallyAssignedItemNames.Clear();
        layout.FolderPortals[0].Name = "changed portal";

        Assert.AreEqual(workspace.Id, layout.ActiveWorkspaceId);
        Assert.AreEqual(10, workspace.Layout.FreeIcons["one.txt"].X);
        Assert.AreEqual("分组", workspace.Layout.Groups[0].Name);
        Assert.AreEqual("rule-1", workspace.Layout.Groups[0].UserRuleId);
        CollectionAssert.AreEqual(
            new[] { "one.txt" },
            workspace.Layout.Groups[0].ManuallyAssignedItemNames);
        Assert.AreEqual("资料入口", workspace.Layout.FolderPortals[0].Name);
        Assert.AreEqual(now, workspace.CreatedUtc);
    }

    [TestMethod]
    public void Activate_SavesCurrentWorkspaceThenAppliesTargetWithoutChangingIdentities()
    {
        var layout = CreateLayout("one.txt", 10);
        layout.ItemIdentities["one.txt"] = new DesktopItemIdentityInfo
        {
            LastKnownPath = @"C:\Desktop\one.txt",
            FileId = "stable-id"
        };
        WorkspaceProfileInfo first = WorkspaceLayoutManager.CreateAndActivate(
            layout,
            "工作",
            DateTime.UnixEpoch);
        layout.FreeIcons["one.txt"].X = 20;
        WorkspaceProfileInfo second = WorkspaceLayoutManager.CreateAndActivate(
            layout,
            "学习",
            DateTime.UnixEpoch.AddMinutes(1));
        layout.FreeIcons["one.txt"].X = 30;
        layout.Groups[0].ManuallyAssignedItemNames.Clear();

        bool activated = WorkspaceLayoutManager.TryActivate(
            layout,
            first.Id,
            DateTime.UnixEpoch.AddMinutes(2));

        Assert.IsTrue(activated);
        Assert.AreEqual(20, layout.FreeIcons["one.txt"].X);
        Assert.AreEqual(30, second.Layout.FreeIcons["one.txt"].X);
        Assert.AreEqual(120, layout.FolderPortals[0].X);
        Assert.AreEqual("stable-id", layout.ItemIdentities["one.txt"].FileId);
        CollectionAssert.AreEqual(
            new[] { "one.txt" },
            layout.Groups[0].ManuallyAssignedItemNames);
    }

    [TestMethod]
    public void Duplicate_CopiesInactiveSnapshotWithoutActivatingApplyingOrAliasing()
    {
        var layout = CreateLayout("one.txt", 10);
        layout.RecycleBinWidget = new RecycleBinWidgetLayoutInfo
        {
            X = 44,
            Y = 55,
            IsVisible = true
        };
        layout.AutoClassificationOriginalPositions["one.txt"] = new IconPosition
        {
            X = 5,
            Y = 6
        };
        DateTime firstTime = DateTime.UnixEpoch;
        DateTime secondTime = firstTime.AddMinutes(1);
        DateTime duplicateTime = firstTime.AddMinutes(2);
        WorkspaceProfileInfo first = WorkspaceLayoutManager.CreateAndActivate(
            layout,
            "工作",
            firstTime);
        layout.FreeIcons["one.txt"].X = 20;
        WorkspaceProfileInfo second = WorkspaceLayoutManager.CreateAndActivate(
            layout,
            "学习",
            secondTime);
        layout.FreeIcons["one.txt"].X = 30;
        layout.Groups[0].Name = "当前根布局";
        layout.FolderPortals[0].X = 300;

        WorkspaceProfileInfo? duplicate = WorkspaceLayoutManager.Duplicate(
            layout,
            first.Id,
            " 工作副本 ",
            duplicateTime);

        Assert.IsNotNull(duplicate);
        Assert.AreNotEqual(first.Id, duplicate.Id);
        Assert.AreEqual("工作副本", duplicate.Name);
        Assert.AreEqual(duplicateTime, duplicate.CreatedUtc);
        Assert.AreEqual(duplicateTime, duplicate.UpdatedUtc);
        Assert.AreEqual(second.Id, layout.ActiveWorkspaceId);
        Assert.AreEqual(30, layout.FreeIcons["one.txt"].X);
        Assert.AreEqual("当前根布局", layout.Groups[0].Name);
        Assert.AreEqual(300, layout.FolderPortals[0].X);
        Assert.AreEqual(20, duplicate.Layout.FreeIcons["one.txt"].X);
        Assert.AreEqual("分组", duplicate.Layout.Groups[0].Name);
        Assert.AreEqual(120, duplicate.Layout.FolderPortals[0].X);
        Assert.AreEqual(first.Layout.Groups[0].Id, duplicate.Layout.Groups[0].Id);
        Assert.AreEqual(first.Layout.FolderPortals[0].Id, duplicate.Layout.FolderPortals[0].Id);
        Assert.AreEqual(secondTime, first.UpdatedUtc);

        duplicate.Layout.FreeIcons["one.txt"].X = 999;
        duplicate.Layout.Groups[0].Name = "changed";
        duplicate.Layout.Groups[0].ItemNames.Clear();
        duplicate.Layout.FolderPortals[0].X = 999;
        duplicate.Layout.DesktopTopology[0].WorkWidth = 999;
        duplicate.Layout.RecycleBinWidget.X = 999;
        duplicate.Layout.AutoClassificationOriginalPositions["one.txt"].X = 999;

        Assert.AreEqual(20, first.Layout.FreeIcons["one.txt"].X);
        Assert.AreEqual("分组", first.Layout.Groups[0].Name);
        CollectionAssert.AreEqual(new[] { "one.txt" }, first.Layout.Groups[0].ItemNames);
        Assert.AreEqual(120, first.Layout.FolderPortals[0].X);
        Assert.AreEqual(1920, first.Layout.DesktopTopology[0].WorkWidth);
        Assert.AreEqual(44, first.Layout.RecycleBinWidget.X);
        Assert.AreEqual(5, first.Layout.AutoClassificationOriginalPositions["one.txt"].X);
    }

    [TestMethod]
    public void Duplicate_MissingSourceDoesNotMutateLayout()
    {
        var layout = CreateLayout("one.txt", 10);
        _ = WorkspaceLayoutManager.CreateAndActivate(layout, "工作", DateTime.UnixEpoch);
        string before = JsonSerializer.Serialize(layout);

        WorkspaceProfileInfo? duplicate = WorkspaceLayoutManager.Duplicate(
            layout,
            "missing",
            "副本",
            DateTime.UnixEpoch.AddMinutes(1));

        Assert.IsNull(duplicate);
        Assert.AreEqual(before, JsonSerializer.Serialize(layout));
    }

    [TestMethod]
    public void Duplicate_InvalidOrExistingNameDoesNotMutateLayout()
    {
        var layout = CreateLayout("one.txt", 10);
        WorkspaceProfileInfo source = WorkspaceLayoutManager.CreateAndActivate(
            layout,
            "工作",
            DateTime.UnixEpoch);
        string before = JsonSerializer.Serialize(layout);

        Assert.ThrowsExactly<ArgumentException>(() => WorkspaceLayoutManager.Duplicate(
            layout,
            source.Id,
            " ",
            DateTime.UnixEpoch.AddMinutes(1)));
        Assert.ThrowsExactly<InvalidOperationException>(() => WorkspaceLayoutManager.Duplicate(
            layout,
            source.Id,
            " 工作 ",
            DateTime.UnixEpoch.AddMinutes(1)));

        Assert.AreEqual(before, JsonSerializer.Serialize(layout));
    }

    [TestMethod]
    public void DeleteActiveWorkspace_LeavesCurrentLayoutAndClearsActiveId()
    {
        var layout = CreateLayout("one.txt", 10);
        WorkspaceProfileInfo workspace = WorkspaceLayoutManager.CreateAndActivate(
            layout,
            "临时项目",
            DateTime.UnixEpoch);

        bool deleted = WorkspaceLayoutManager.Delete(layout, workspace.Id);

        Assert.IsTrue(deleted);
        Assert.IsNull(layout.ActiveWorkspaceId);
        Assert.AreEqual(10, layout.FreeIcons["one.txt"].X);
        Assert.AreEqual(0, layout.Workspaces.Count);
    }

    [TestMethod]
    public void Normalize_RepairsDuplicateIdsAndNames()
    {
        var layout = new AppLayoutData
        {
            Workspaces =
            [
                new WorkspaceProfileInfo { Id = "same", Name = "工作" },
                new WorkspaceProfileInfo { Id = "same", Name = "工作" }
            ],
            ActiveWorkspaceId = "missing"
        };

        WorkspaceLayoutManager.Normalize(layout);

        Assert.AreEqual(2, layout.Workspaces.Select(item => item.Id).Distinct().Count());
        Assert.AreEqual(2, layout.Workspaces.Select(item => item.Name).Distinct().Count());
        Assert.IsNull(layout.ActiveWorkspaceId);
    }

    [TestMethod]
    public void RemoveItemFromSnapshots_RemovesStalePhysicalItemEverywhere()
    {
        var layout = CreateLayout("one.txt", 10);
        _ = WorkspaceLayoutManager.CreateAndActivate(layout, "工作", DateTime.UnixEpoch);
        _ = WorkspaceLayoutManager.CreateAndActivate(layout, "学习", DateTime.UnixEpoch.AddMinutes(1));

        WorkspaceLayoutManager.RemoveItemFromSnapshots(layout, "ONE.TXT");

        Assert.IsTrue(layout.Workspaces.All(workspace =>
            !workspace.Layout.FreeIcons.ContainsKey("one.txt") &&
            workspace.Layout.Groups.All(group =>
                !group.ItemNames.Contains("one.txt", StringComparer.OrdinalIgnoreCase) &&
                !group.ManuallyAssignedItemNames.Contains(
                    "one.txt",
                    StringComparer.OrdinalIgnoreCase))));
    }

    [TestMethod]
    public void Normalize_RepairsDamagedNestedSnapshot()
    {
        var layout = new AppLayoutData
        {
            Workspaces =
            [
                new WorkspaceProfileInfo
                {
                    Layout = new WorkspaceLayoutState
                    {
                        Groups =
                        [
                            new GroupInfo { Id = "same", Name = " ", ItemNames = null! },
                            new GroupInfo
                            {
                                Id = "same",
                                ItemNames = ["one.txt", "ONE.TXT", " "],
                                ManuallyAssignedItemNames = ["ONE.TXT", "ghost.txt", " ", "one.txt"],
                                SortMode = (GroupSortMode)999
                            }
                        ],
                        DesktopTopology =
                        [
                            new DesktopMonitorLayoutInfo { DeviceName = "DISPLAY1" },
                            new DesktopMonitorLayoutInfo { DeviceName = "display1" },
                            new DesktopMonitorLayoutInfo { DeviceName = " " }
                        ]
                    }
                }
            ]
        };

        WorkspaceLayoutManager.Normalize(layout);

        WorkspaceLayoutState snapshot = layout.Workspaces[0].Layout;
        Assert.AreEqual(2, snapshot.Groups.Select(group => group.Id).Distinct().Count());
        Assert.AreEqual("未命名分组", snapshot.Groups[0].Name);
        Assert.AreEqual(GroupSortMode.Custom, snapshot.Groups[1].SortMode);
        CollectionAssert.AreEqual(new[] { "one.txt" }, snapshot.Groups[1].ItemNames);
        CollectionAssert.AreEqual(
            new[] { "one.txt" },
            snapshot.Groups[1].ManuallyAssignedItemNames);
        Assert.AreEqual(1, snapshot.DesktopTopology.Count);
        Assert.AreEqual(3, snapshot.Version);
    }

    [TestMethod]
    public void CaptureAndApply_DeepClonesFolderPortalsAndPreviewCountsThem()
    {
        var layout = CreateLayout("one.txt", 10);
        WorkspaceProfileInfo workspace = WorkspaceLayoutManager.CreateAndActivate(
            layout,
            "工作",
            DateTime.UnixEpoch);
        layout.FolderPortals[0].X = 777;

        Assert.AreEqual(120, workspace.Layout.FolderPortals[0].X);
        WorkspacePreview preview = WorkspaceLayoutManager.GetPreviews(layout).Single();
        Assert.AreEqual(1, preview.PortalCount);

        Assert.IsTrue(WorkspaceLayoutManager.TryActivate(
            layout,
            workspace.Id,
            DateTime.UnixEpoch.AddMinutes(1)));
        Assert.AreEqual(777, layout.FolderPortals[0].X,
            "激活当前工作区不得覆盖尚未保存的当前布局。");
    }

    private static AppLayoutData CreateLayout(string name, double x) => new()
    {
        FreeIcons = new Dictionary<string, IconPosition>
        {
            [name] = new IconPosition { X = x, Y = 20 }
        },
        Groups =
        [
            new GroupInfo
            {
                Id = "group-1",
                Name = "分组",
                UserRuleId = "rule-1",
                ItemNames = [name],
                ManuallyAssignedItemNames = [name]
            }
        ],
        DesktopTopology =
        [
            new DesktopMonitorLayoutInfo
            {
                DeviceName = "DISPLAY1",
                WorkWidth = 1920,
                WorkHeight = 1040
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
                X = 120,
                Y = 160
            }
        ]
    };
}
