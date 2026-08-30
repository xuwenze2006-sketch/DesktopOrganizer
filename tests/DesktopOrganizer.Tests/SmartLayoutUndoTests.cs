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
