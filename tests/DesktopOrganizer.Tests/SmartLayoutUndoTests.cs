using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class SmartLayoutUndoTests
{
    [STATestMethod]
    public void CompleteGroupDrag_CommittedMoveInvalidatesSmartLayoutUndo()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            GroupInfo group = PrepareGroup(window);
            object snapshot = CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;

            var draggedElement = new Border();
            Canvas.SetLeft(draggedElement, 180);
            Canvas.SetTop(draggedElement, 160);
            PrepareGroupDrag(window, group, draggedElement, moved: true);

            InvokeCompleteGroupDrag(window, commit: true);

            Assert.AreEqual(180, group.X, 0.01);
            Assert.AreEqual(160, group.Y, 0.01);
            Assert.IsNull(GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsFalse(window.UndoSmartLayoutButton.IsEnabled);
            Assert.IsNotNull(snapshot);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    public void CompleteGroupDrag_CanceledMovePreservesSmartLayoutUndo()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            GroupInfo group = PrepareGroup(window);
            object snapshot = CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;

            var draggedElement = new Border();
            Canvas.SetLeft(draggedElement, 180);
            Canvas.SetTop(draggedElement, 160);
            PrepareGroupDrag(window, group, draggedElement, moved: true);

            InvokeCompleteGroupDrag(window, commit: false);

            Assert.AreEqual(40, group.X, 0.01);
            Assert.AreEqual(40, group.Y, 0.01);
            Assert.AreSame(snapshot, GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsTrue(window.UndoSmartLayoutButton.IsEnabled);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    public void CompleteGroupDrag_ReturnedToStartPreservesSmartLayoutUndo()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            GroupInfo group = PrepareGroup(window);
            object snapshot = CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;

            var draggedElement = new Border();
            Canvas.SetLeft(draggedElement, 40);
            Canvas.SetTop(draggedElement, 40);
            PrepareGroupDrag(window, group, draggedElement, moved: true);

            InvokeCompleteGroupDrag(window, commit: true);

            Assert.AreEqual(40, group.X, 0.01);
            Assert.AreEqual(40, group.Y, 0.01);
            Assert.AreSame(snapshot, GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsTrue(window.UndoSmartLayoutButton.IsEnabled);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    public void ResizeThumb_DimensionChangeInvalidatesSmartLayoutUndo()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            GroupInfo group = PrepareGroup(window);
            GetField<AppLayoutData>(window, "_appLayout").IsEditMode = true;
            object snapshot = CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;
            var thumb = new Thumb { Tag = group };

            InvokeResizeThumbDragDelta(window, thumb, horizontalChange: 24, verticalChange: 18);

            Assert.AreEqual(304, group.Width, 0.01);
            Assert.AreEqual(218, group.Height, 0.01);
            Assert.IsTrue(group.IsSizeLocked);
            Assert.IsNull(GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsFalse(window.UndoSmartLayoutButton.IsEnabled);
            Assert.IsNotNull(snapshot);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void ResizeThumb_ZeroDimensionChangeOnlyInvalidatesWhenLockStateChanges(
        bool initiallyLocked,
        bool shouldInvalidateUndo)
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            GroupInfo group = PrepareGroup(window);
            group.IsSizeLocked = initiallyLocked;
            GetField<AppLayoutData>(window, "_appLayout").IsEditMode = true;
            object snapshot = CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;
            var thumb = new Thumb { Tag = group };

            InvokeResizeThumbDragDelta(window, thumb, horizontalChange: 0, verticalChange: 0);

            Assert.AreEqual(280, group.Width, 0.01);
            Assert.AreEqual(200, group.Height, 0.01);
            Assert.IsTrue(group.IsSizeLocked);
            if (shouldInvalidateUndo)
            {
                Assert.IsNull(GetRawField(window, "_lastSmartLayoutSnapshot"));
                Assert.IsFalse(window.UndoSmartLayoutButton.IsEnabled);
            }
            else
            {
                Assert.AreSame(snapshot, GetRawField(window, "_lastSmartLayoutSnapshot"));
                Assert.IsTrue(window.UndoSmartLayoutButton.IsEnabled);
            }
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    public void ResizeThumb_SubpixelChangeOnLockedGroupInvalidatesSmartLayoutUndo()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            GroupInfo group = PrepareGroup(window);
            group.IsSizeLocked = true;
            GetField<AppLayoutData>(window, "_appLayout").IsEditMode = true;
            CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;
            var thumb = new Thumb { Tag = group };

            InvokeResizeThumbDragDelta(window, thumb, horizontalChange: 0.005, verticalChange: 0);

            Assert.AreEqual(280.005, group.Width, 0.0001);
            Assert.AreEqual(200, group.Height, 0.0001);
            Assert.IsNull(GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsFalse(window.UndoSmartLayoutButton.IsEnabled);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    public void ToggleGroupCollapsed_InvalidatesSmartLayoutUndo()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            GroupInfo group = PrepareGroup(window);
            object snapshot = CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;

            InvokePrivateMethod(window, "ToggleGroupCollapsed", group);

            Assert.IsTrue(group.IsCollapsed);
            Assert.IsNull(GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsFalse(window.UndoSmartLayoutButton.IsEnabled);
            Assert.IsNotNull(snapshot);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    public void ToggleAllGroupsCollapsed_InvalidatesSmartLayoutUndo()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            GroupInfo firstGroup = PrepareGroup(window);
            GroupInfo secondGroup = new()
            {
                Id = "second-group",
                Name = "已收起分类",
                X = 340,
                Y = 40,
                Width = 280,
                Height = 200,
                IsCollapsed = true
            };
            GetField<AppLayoutData>(window, "_appLayout").Groups.Add(secondGroup);
            object snapshot = CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;

            InvokePrivateMethod(window, "ToggleAllGroupsCollapsed");

            Assert.IsTrue(firstGroup.IsCollapsed);
            Assert.IsTrue(secondGroup.IsCollapsed);
            Assert.IsNull(GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsFalse(window.UndoSmartLayoutButton.IsEnabled);
            Assert.IsNotNull(snapshot);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    [DataRow(true, true)]
    [DataRow(false, false)]
    public void LocateDesktopSearchResult_OnlyInvalidatesUndoWhenItExpandsGroup(
        bool initiallyCollapsed,
        bool shouldInvalidateUndo)
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            const string itemName = "Located.txt";
            GroupInfo group = PrepareGroup(window);
            group.IsCollapsed = initiallyCollapsed;
            group.IsSizeLocked = true;
            group.ItemNames = [itemName];
            Dictionary<string, string> desktopItems = GetField<Dictionary<string, string>>(
                window,
                "_desktopItems");
            desktopItems.Clear();
            desktopItems[itemName] = $@"C:\Desktop\{itemName}";
            object snapshot = CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;

            window.LocateDesktopSearchResult(itemName);

            Assert.IsFalse(group.IsCollapsed);
            if (shouldInvalidateUndo)
            {
                Assert.IsNull(GetRawField(window, "_lastSmartLayoutSnapshot"));
                Assert.IsFalse(window.UndoSmartLayoutButton.IsEnabled);
            }
            else
            {
                Assert.AreSame(snapshot, GetRawField(window, "_lastSmartLayoutSnapshot"));
                Assert.IsTrue(window.UndoSmartLayoutButton.IsEnabled);
            }
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    public void AutoFitGroupAndUnlock_LockStateChangeInvalidatesSmartLayoutUndo()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            GroupInfo group = PrepareAutoFitGroup(window);
            InvokeAutoFitGroup(window, group);
            double fittedWidth = group.Width;
            double fittedHeight = group.Height;
            group.IsSizeLocked = true;
            CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;

            InvokeAutoFitGroupAndUnlock(window, group);

            Assert.AreEqual(fittedWidth, group.Width);
            Assert.AreEqual(fittedHeight, group.Height);
            Assert.IsFalse(group.IsSizeLocked);
            Assert.IsNull(GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsFalse(window.UndoSmartLayoutButton.IsEnabled);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    public void AutoFitGroupAndUnlock_SubpixelDimensionChangeInvalidatesSmartLayoutUndo()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            GroupInfo group = PrepareAutoFitGroup(window);
            InvokeAutoFitGroup(window, group);
            double fittedWidth = group.Width;
            group.Width += 0.25;
            CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;

            InvokeAutoFitGroupAndUnlock(window, group);

            Assert.AreEqual(fittedWidth, group.Width);
            Assert.IsNull(GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsFalse(window.UndoSmartLayoutButton.IsEnabled);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    public void AutoFitGroupAndUnlock_PositionClampInvalidatesSmartLayoutUndo()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            GroupInfo group = PrepareAutoFitGroup(window);
            InvokeAutoFitGroup(window, group);
            double fittedWidth = group.Width;
            double fittedHeight = group.Height;
            group.X = 1_000_000;
            group.Y = 1_000_000;
            CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;

            InvokeAutoFitGroupAndUnlock(window, group);

            Assert.AreEqual(fittedWidth, group.Width);
            Assert.AreEqual(fittedHeight, group.Height);
            Assert.AreEqual(1200 - fittedWidth, group.X, 0.001);
            Assert.AreEqual(800 - fittedHeight, group.Y, 0.001);
            Assert.IsNull(GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsFalse(window.UndoSmartLayoutButton.IsEnabled);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    public void AutoFitGroupAndUnlock_UnchangedLayoutPreservesSmartLayoutUndo()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            GroupInfo group = PrepareAutoFitGroup(window);
            InvokeAutoFitGroup(window, group);
            object snapshot = CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;

            InvokeAutoFitGroupAndUnlock(window, group);

            Assert.IsFalse(group.IsSizeLocked);
            Assert.AreSame(snapshot, GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsTrue(window.UndoSmartLayoutButton.IsEnabled);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    public void AutoFitGroupAndUnlock_RebuildSizeChangeInvalidatesSmartLayoutUndo()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            GroupInfo group = PrepareAutoFitGroup(window);
            group.ItemNames = ["Missing-A.txt", "Missing-B.txt"];
            InvokeAutoFitGroup(window, group);
            Assert.AreEqual(220, group.Width);
            CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;

            InvokeAutoFitGroupAndUnlock(window, group);

            Assert.IsEmpty(group.ItemNames);
            Assert.AreEqual(190, group.Width);
            Assert.IsNull(GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsFalse(window.UndoSmartLayoutButton.IsEnabled);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    public void CompactGroupLayoutToggle_SizeChangeInvalidatesSmartLayoutUndo()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            GroupInfo group = PrepareAutoFitGroup(window);
            InvokeAutoFitGroup(window, group);
            Assert.AreEqual(190, group.Width);
            Assert.AreEqual(134, group.Height);
            CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;
            window.CompactGroupLayoutToggle.IsChecked = true;

            InvokeCompactGroupLayoutToggle(window);

            Assert.IsTrue(GetField<AppLayoutData>(window, "_appLayout").CompactGroupLayout);
            Assert.AreEqual(180, group.Width);
            Assert.AreEqual(130, group.Height);
            Assert.IsNull(GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsFalse(window.UndoSmartLayoutButton.IsEnabled);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    public void CompactGroupLayoutToggle_UnchangedLockedGroupPreservesSmartLayoutUndo()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            GroupInfo group = PrepareAutoFitGroup(window);
            InvokeAutoFitGroup(window, group);
            group.IsSizeLocked = true;
            object snapshot = CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;
            window.CompactGroupLayoutToggle.IsChecked = true;

            InvokeCompactGroupLayoutToggle(window);

            Assert.IsTrue(GetField<AppLayoutData>(window, "_appLayout").CompactGroupLayout);
            Assert.AreEqual(40, group.X);
            Assert.AreEqual(40, group.Y);
            Assert.AreEqual(190, group.Width);
            Assert.AreEqual(134, group.Height);
            Assert.IsFalse(group.IsCollapsed);
            Assert.IsTrue(group.IsSizeLocked);
            Assert.HasCount(1, GetField<AppLayoutData>(window, "_appLayout").Groups);
            Assert.AreSame(snapshot, GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsTrue(window.UndoSmartLayoutButton.IsEnabled);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    public void CompactGroupLayoutToggle_CollisionRearrangementInvalidatesSmartLayoutUndo()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            GroupInfo firstGroup = PrepareAutoFitGroup(window);
            InvokeAutoFitGroup(window, firstGroup);
            firstGroup.IsSizeLocked = true;
            var secondGroup = new GroupInfo
            {
                Id = "second-group",
                Name = "重叠分类",
                X = firstGroup.X,
                Y = firstGroup.Y,
                Width = firstGroup.Width,
                Height = firstGroup.Height,
                IsSizeLocked = true
            };
            AppLayoutData layout = GetField<AppLayoutData>(window, "_appLayout");
            layout.Groups.Add(secondGroup);
            layout.ReserveTemporaryWorkspace = false;
            layout.RecycleBinWidget.IsVisible = false;
            CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;
            window.CompactGroupLayoutToggle.IsChecked = true;

            InvokeCompactGroupLayoutToggle(window);

            Assert.IsTrue(layout.CompactGroupLayout);
            Assert.IsFalse(new Rect(
                firstGroup.X,
                firstGroup.Y,
                firstGroup.Width,
                firstGroup.Height).IntersectsWith(new Rect(
                    secondGroup.X,
                    secondGroup.Y,
                    secondGroup.Width,
                    secondGroup.Height)));
            Assert.IsNull(GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsFalse(window.UndoSmartLayoutButton.IsEnabled);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    public void AddManualGroup_InvalidatesUndoWhenNewGroupOccupiesSnapshotPosition()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            AppLayoutData layout = GetField<AppLayoutData>(window, "_appLayout");
            layout.Groups.Clear();
            layout.FolderPortals.Clear();
            layout.CompactGroupLayout = false;
            layout.RecycleBinWidget.IsVisible = false;
            SetField(
                window,
                "_desktopGeometry",
                new DesktopGeometry(
                [
                    new DesktopMonitorRegion
                    {
                        DeviceName = "TEST",
                        Bounds = new Rect(0, 0, 1200, 800),
                        WorkArea = new Rect(0, 0, 1200, 800),
                        IsPrimary = true
                    }
                ]));
            var existingGroup = new GroupInfo
            {
                Id = "existing-group",
                Name = "现有分类",
                X = 20,
                Y = 112,
                Width = 190,
                Height = 134,
                IsSizeLocked = true
            };
            layout.Groups.Add(existingGroup);
            CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;
            existingGroup.X = 500;
            existingGroup.Y = 400;

            InvokePrivateMethod(window, "AddManualGroup", "后来新增");

            GroupInfo newGroup = layout.Groups.Single(group =>
                !group.Id.Equals(existingGroup.Id, StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(20, newGroup.X, 0.001);
            Assert.AreEqual(112, newGroup.Y, 0.001);
            Assert.AreEqual(500, existingGroup.X, 0.001);
            Assert.AreEqual(400, existingGroup.Y, 0.001);
            Assert.IsTrue(new Rect(
                20,
                112,
                existingGroup.Width,
                existingGroup.Height).IntersectsWith(new Rect(
                    newGroup.X,
                    newGroup.Y,
                    newGroup.Width,
                    newGroup.Height)));
            Assert.IsNull(GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsFalse(window.UndoSmartLayoutButton.IsEnabled);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    [STATestMethod]
    public void DeleteGroup_InvalidatesUndoWhenReleasedIconOccupiesSnapshotPosition()
    {
        var window = new MainWindow(startQuietly: false);
        DispatcherTimer layoutSaveTimer = GetField<DispatcherTimer>(window, "_layoutSaveTimer");
        try
        {
            const string itemName = "Released.txt";
            AppLayoutData layout = GetField<AppLayoutData>(window, "_appLayout");
            layout.Groups.Clear();
            layout.FolderPortals.Clear();
            layout.SnapToGrid = false;
            layout.CompactGroupLayout = false;
            layout.RecycleBinWidget.IsVisible = false;
            SetField(
                window,
                "_desktopGeometry",
                new DesktopGeometry(
                [
                    new DesktopMonitorRegion
                    {
                        DeviceName = "TEST",
                        Bounds = new Rect(0, 0, 1200, 800),
                        WorkArea = new Rect(0, 0, 1200, 800),
                        IsPrimary = true
                    }
                ]));
            Dictionary<string, string> desktopItems = GetField<Dictionary<string, string>>(
                window,
                "_desktopItems");
            desktopItems.Clear();
            desktopItems[itemName] = $@"C:\Desktop\{itemName}";
            var remainingGroup = new GroupInfo
            {
                Id = "remaining-group",
                Name = "保留分类",
                X = 20,
                Y = 256,
                Width = 190,
                Height = 134,
                IsSizeLocked = true
            };
            var deletedGroup = new GroupInfo
            {
                Id = "deleted-group",
                Name = "待删除分类",
                X = 500,
                Y = 400,
                Width = 190,
                Height = 134,
                IsSizeLocked = true,
                ItemNames = [itemName]
            };
            layout.Groups.Add(remainingGroup);
            layout.Groups.Add(deletedGroup);
            CaptureSmartLayoutSnapshot(window);
            window.UndoSmartLayoutButton.IsEnabled = true;
            remainingGroup.X = 500;
            remainingGroup.Y = 400;
            deletedGroup.X = 20;
            deletedGroup.Y = 112;

            InvokePrivateMethod(window, "DeleteGroup", deletedGroup);

            Assert.HasCount(1, layout.Groups);
            Assert.AreSame(remainingGroup, layout.Groups[0]);
            Assert.AreEqual(500, remainingGroup.X, 0.001);
            Assert.AreEqual(400, remainingGroup.Y, 0.001);
            Assert.IsTrue(layout.FreeIcons.TryGetValue(itemName, out IconPosition? releasedPosition));
            Assert.IsNotNull(releasedPosition);
            Assert.AreEqual(20, releasedPosition.X, 0.001);
            Assert.AreEqual(256, releasedPosition.Y, 0.001);
            Assert.IsTrue(new Rect(
                20,
                256,
                remainingGroup.Width,
                remainingGroup.Height).Contains(new Point(
                    releasedPosition.X,
                    releasedPosition.Y)));
            Assert.IsNull(GetRawField(window, "_lastSmartLayoutSnapshot"));
            Assert.IsFalse(window.UndoSmartLayoutButton.IsEnabled);
        }
        finally
        {
            layoutSaveTimer.Stop();
        }
    }

    private static GroupInfo PrepareGroup(MainWindow window)
    {
        AppLayoutData layout = GetField<AppLayoutData>(window, "_appLayout");
        layout.Groups.Clear();
        layout.FolderPortals.Clear();
        var group = new GroupInfo
        {
            Id = "group",
            Name = "手工分类",
            X = 40,
            Y = 40,
            Width = 280,
            Height = 200
        };
        layout.Groups.Add(group);
        return group;
    }

    private static GroupInfo PrepareAutoFitGroup(MainWindow window)
    {
        AppLayoutData layout = GetField<AppLayoutData>(window, "_appLayout");
        layout.CompactGroupLayout = false;
        SetField(
            window,
            "_desktopGeometry",
            new DesktopGeometry(
            [
                new DesktopMonitorRegion
                {
                    DeviceName = "TEST",
                    Bounds = new Rect(0, 0, 1200, 800),
                    WorkArea = new Rect(0, 0, 1200, 800),
                    IsPrimary = true
                }
            ]));
        return PrepareGroup(window);
    }

    private static object CaptureSmartLayoutSnapshot(MainWindow window)
    {
        MethodInfo captureMethod = typeof(MainWindow).GetMethod(
            "CaptureGroupLayoutSnapshot",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到分组布局快照入口。");
        object snapshot = captureMethod.Invoke(window, null)
            ?? throw new AssertFailedException("未能创建分组布局快照。");
        SetField(window, "_lastSmartLayoutSnapshot", snapshot);
        return snapshot;
    }

    private static void PrepareGroupDrag(
        MainWindow window,
        GroupInfo group,
        UIElement draggedElement,
        bool moved)
    {
        SetField(window, "_draggedElement", draggedElement);
        SetField(window, "_draggedGroup", group);
        SetField(window, "_draggedIsGroup", true);
        SetField(window, "_groupDragMoved", moved);
        SetField(window, "_groupDragStartPosition", new Point(group.X, group.Y));
    }

    private static void InvokeCompleteGroupDrag(MainWindow window, bool commit)
    {
        MethodInfo completeMethod = typeof(MainWindow).GetMethod(
            "CompleteGroupDrag",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到分组拖动完成入口。");
        completeMethod.Invoke(window, [commit]);
    }

    private static void InvokeResizeThumbDragDelta(
        MainWindow window,
        Thumb thumb,
        double horizontalChange,
        double verticalChange)
    {
        MethodInfo resizeMethod = typeof(MainWindow).GetMethod(
            "ResizeThumb_DragDelta",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到分组缩放入口。");
        resizeMethod.Invoke(
            window,
            [thumb, new DragDeltaEventArgs(horizontalChange, verticalChange)]);
    }

    private static object? InvokePrivateMethod(
        MainWindow window,
        string methodName,
        params object[] arguments)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException($"未找到入口 {methodName}。");
        return method.Invoke(window, arguments);
    }

    private static void InvokeAutoFitGroupAndUnlock(MainWindow window, GroupInfo group) =>
        InvokePrivateMethod(window, "AutoFitGroupAndUnlock", group);

    private static void InvokeAutoFitGroup(MainWindow window, GroupInfo group) =>
        InvokePrivateMethod(window, "AutoFitGroup", group, true);

    private static void InvokeCompactGroupLayoutToggle(MainWindow window) =>
        InvokePrivateMethod(
            window,
            "CompactGroupLayoutToggle_Click",
            window.CompactGroupLayoutToggle,
            new RoutedEventArgs());

    private static T GetField<T>(MainWindow window, string fieldName)
        where T : class
    {
        return GetRawField(window, fieldName) as T
            ?? throw new AssertFailedException($"字段 {fieldName} 尚未初始化。");
    }

    private static object? GetRawField(MainWindow window, string fieldName)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException($"未找到字段 {fieldName}。");
        return field.GetValue(window);
    }

    private static void SetField(MainWindow window, string fieldName, object? value)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException($"未找到字段 {fieldName}。");
        field.SetValue(window, value);
    }
}
