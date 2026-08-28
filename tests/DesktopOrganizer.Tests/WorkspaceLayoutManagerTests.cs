using Microsoft.VisualStudio.TestTools.UnitTesting;

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

        Assert.AreEqual(workspace.Id, layout.ActiveWorkspaceId);
        Assert.AreEqual(10, workspace.Layout.FreeIcons["one.txt"].X);
        Assert.AreEqual("分组", workspace.Layout.Groups[0].Name);
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

        bool activated = WorkspaceLayoutManager.TryActivate(
            layout,
            first.Id,
            DateTime.UnixEpoch.AddMinutes(2));

        Assert.IsTrue(activated);
        Assert.AreEqual(20, layout.FreeIcons["one.txt"].X);
        Assert.AreEqual(30, second.Layout.FreeIcons["one.txt"].X);
        Assert.AreEqual("stable-id", layout.ItemIdentities["one.txt"].FileId);
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
                !group.ItemNames.Contains("one.txt", StringComparer.OrdinalIgnoreCase))));
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
                ItemNames = [name]
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
        ]
    };
}
