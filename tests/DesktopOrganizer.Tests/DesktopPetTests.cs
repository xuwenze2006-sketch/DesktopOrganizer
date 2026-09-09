using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopPetTests
{
    [STATestMethod]
    public void Preferences_UpgradeNullAndInvalidValues_AndRoundTripHiddenPosition()
    {
        using var f = new Fixture();
        Assert.IsFalse(JsonSerializer.Deserialize<AppLayoutData>("{\"Version\":19}")!.DesktopPet.IsVisible);
        Assert.AreEqual(DesktopPetWidget.CatCharacterId,
            JsonSerializer.Deserialize<AppLayoutData>("{\"Version\":19}")!.DesktopPet.CharacterId);
        f.Layout.DesktopPet = null!;
        f.Invoke("NormalizeLayout");
        Assert.IsNotNull(f.Layout.DesktopPet);
        Assert.IsFalse(f.Layout.DesktopPet.IsVisible);
        f.Layout.DesktopPet.X = double.NaN;
        f.Layout.DesktopPet.Y = double.PositiveInfinity;
        f.Invoke("NormalizeLayout");
        Assert.IsNull(f.Layout.DesktopPet.X);
        Assert.IsNull(f.Layout.DesktopPet.Y);
        f.Invoke("SetDesktopPetPosition", 280.5, 321.25, true);
        AppLayoutData restored = LayoutJsonSerializer.Deserialize(JsonSerializer.Serialize(f.Layout)).Layout;
        Assert.IsFalse(restored.DesktopPet.IsVisible);
        Assert.AreEqual(280.5, restored.DesktopPet.X);
        Assert.AreEqual(321.25, restored.DesktopPet.Y);
        foreach (string? id in new string?[] { null, "", "future-character" })
        {
            f.Layout.DesktopPet.CharacterId = id!;
            f.Invoke("NormalizeLayout");
            Assert.AreEqual(DesktopPetWidget.CatCharacterId, f.Layout.DesktopPet.CharacterId);
            Assert.AreEqual(280.5, f.Layout.DesktopPet.X);
        }
    }

    [STATestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    [DataRow(2.0)]
    public void RenderingAndHitTesting_FollowOpaquePixelsAndDraggedPosition(double scale)
    {
        using var f = new Fixture();
        f.Enable();
        f.Invoke("SetDesktopPetPosition", 300.0, 200.0, true);
        f.Measure(scale);
        Assert.AreEqual(scale, VisualTreeHelper.GetDpi(f.Pet).DpiScaleX);
        Assert.IsTrue(f.Hit(356, 275), "小猫身体可点击。");
        Assert.IsFalse(f.Hit(301, 201), "素材周围透明留白必须穿透。");
        Assert.IsNull(f.Pet.InputHitTest(new Point(1, 1)), "WPF 命中同样应穿透透明像素。");
        Assert.AreSame(f.Pet, f.Pet.InputHitTest(new Point(56, 75)));
        Assert.AreSame(f.Pet, f.Window.RootGrid.InputHitTest(new Point(356, 275)),
            "空的图标画布不能挡住猫的输入。");
        var icon = new Border { Width = 112, Height = 112, Background = Brushes.White };
        Canvas.SetLeft(icon, 300);
        Canvas.SetTop(icon, 200);
        f.Window.IconCanvas.Children.Add(icon);
        f.Measure(scale);
        Assert.AreSame(icon, f.Window.RootGrid.InputHitTest(new Point(356, 275)),
            "重叠时文件图标必须优先收到输入。");
        f.Window.IconCanvas.Children.Remove(icon);

        f.Invoke("SetDesktopPetPosition", 500.0, 250.0, true);
        f.Measure(scale);
        Assert.IsFalse(f.Hit(356, 275), "拖动后旧位置不得保留命中区域。");
        Assert.IsTrue(f.Hit(556, 325));
        f.Render($"cat-idle-{scale}.png", scale);
        for (int i = 0; i < 5; i++) f.Pet.AdvanceAnimation();
        Assert.AreEqual(2, f.Pet.CurrentFrame, "待机之后应进入打盹。");
        f.Render($"cat-sleep-{scale}.png", scale);
        f.Pet.ReactToTouch();
        Assert.AreEqual(4, f.Pet.CurrentFrame);
        f.Render($"cat-touch-{scale}.png", scale);
    }

    [STATestMethod]
    public void SystemTab_ToggleFitsAndCanReopenHiddenPet()
    {
        using var f = new Fixture();
        f.Window.ControlPanel.Visibility = Visibility.Visible;
        f.Window.ExpandedCommands.Visibility = Visibility.Visible;
        f.Window.ControlPanelTabs.SelectedItem = f.Window.SystemCommandTab;
        f.Measure(1.5);
        Assert.IsTrue(f.Window.DesktopPetToggle.IsVisible);
        Assert.IsTrue(f.Window.DesktopPetToggle.ActualHeight >= 30);
        Point bottom = f.Window.DesktopPetToggle.TranslatePoint(
            new Point(0, f.Window.DesktopPetToggle.ActualHeight), f.Window.ExpandedCommands);
        Assert.IsTrue(bottom.Y <= f.Window.ExpandedCommands.ActualHeight);
        f.Window.DesktopPetToggle.IsChecked = true;
        f.Window.DesktopPetToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.IsTrue(f.Layout.DesktopPet.IsVisible);
        Assert.IsTrue(f.Pet.AnimationRunning);
        f.Invoke("SetDesktopPetPosition", 500.0, 250.0, true);
        f.Measure(1.5);
        f.Render("cat-system-toggle-1.5.png", 1.5);
    }

    [STATestMethod]
    [DataRow("pixelpaws")]
    [DataRow("vpet")]
    public void Animation_HiddenPausedSafeOrClosing_StopsWithoutSavingLayout(string character)
    {
        using var f = new Fixture();
        f.Enable(character);
        Assert.IsTrue(f.Pet.AnimationRunning);
        long generation = f.Field<long>("_layoutSaveGeneration");
        bool dirty = f.Field<bool>("_layoutDirty");
        for (int i = 0; i < 12; i++) f.Pet.AdvanceAnimation();
        Assert.AreEqual(generation, f.Field<long>("_layoutSaveGeneration"));
        Assert.AreEqual(dirty, f.Field<bool>("_layoutDirty"), "动画不应写布局。");

        foreach (string reason in new[] { "_organizerPaused", "_isSafeModeActive", "_isClosing" })
        {
            f.Set(reason, true);
            f.Invoke("UpdateDesktopPetVisibility");
            Assert.AreEqual(Visibility.Collapsed, f.Pet.Visibility);
            Assert.IsFalse(f.Pet.AnimationRunning);
            int frame = f.Pet.CurrentFrame;
            f.Pet.AdvanceAnimation();
            Assert.AreEqual(frame, f.Pet.CurrentFrame, "排队的 tick 在停止后不得推进。");
            Assert.IsTrue(f.Layout.DesktopPet.IsVisible, "临时暂停不能覆盖显示偏好。");
            f.Set(reason, false);
            f.Invoke("UpdateDesktopPetVisibility");
            Assert.IsTrue(f.Pet.AnimationRunning);
        }
        f.Invoke("SetDesktopPetVisible", false);
        Assert.IsFalse(f.Pet.AnimationRunning);
        Assert.IsFalse(f.Layout.DesktopPet.IsVisible);
        Assert.IsFalse(f.Window.DesktopPetToggle.IsChecked);
    }

    [STATestMethod]
    [DataRow("_organizerPaused", "继续整理")]
    [DataRow("_isSafeModeActive", "关闭安全模式")]
    public void EnableDuringTemporaryPause_PreservesPreferenceAndExplainsHiddenState(string reason, string expectedHint)
    {
        using var f = new Fixture();
        f.Set(reason, true);
        f.Invoke("SetDesktopPetVisible", true);
        Assert.IsTrue(f.Layout.DesktopPet.IsVisible);
        Assert.AreEqual(Visibility.Collapsed, f.Pet.Visibility);
        Assert.IsFalse(f.Pet.AnimationRunning);
        StringAssert.Contains(f.Window.StatusText.Text, expectedHint);
    }

    [STATestMethod]
    [DataRow("pixelpaws")]
    [DataRow("vpet")]
    public void Drag_CommitOnce_OrRestoreOnPauseAndCaptureLoss(string character)
    {
        using var f = new Fixture();
        f.Enable(character);
        f.BeginFakeDrag(new Point(200, 220), new Point(350, 330));
        Assert.IsTrue((bool)f.Invoke("IsUserInteractionActive")!);
        f.Invoke("CompleteDesktopPetDrag", true);
        Assert.AreEqual(350, f.Layout.DesktopPet.X);
        Assert.AreEqual(330, f.Layout.DesktopPet.Y);
        f.Invoke("CompleteDesktopPetDrag", false);
        Assert.AreEqual(350, f.Layout.DesktopPet.X, "松开后失捕获不能再次回滚或提交。");

        f.BeginFakeDrag(new Point(350, 330), new Point(600, 400));
        f.Set("_organizerPaused", true);
        f.Invoke("UpdateDesktopPetVisibility");
        Assert.IsFalse(f.Field<bool>("_isDesktopPetDragging"));
        Assert.AreEqual(new Point(350, 330), f.Invoke("GetDesktopPetPosition"));
        Assert.AreEqual(350, f.Layout.DesktopPet.X);
        Assert.IsFalse(f.Pet.AnimationRunning);

        f.Set("_organizerPaused", false);
        f.Invoke("UpdateDesktopPetVisibility");
        f.BeginFakeDrag(new Point(350, 330), new Point(600, 400));
        f.Invoke("ResetAllInteractionState", true);
        Assert.AreEqual(new Point(350, 330), f.Invoke("GetDesktopPetPosition"));
        Assert.IsFalse(f.Field<bool>("_isDesktopPetDragging"));
    }

    [STATestMethod]
    [DataRow("pixelpaws")]
    [DataRow("vpet")]
    public void MonitorRemoval_ClampsPet_ButWorkspaceRemapDoesNotMoveIt(string character)
    {
        using var f = new Fixture();
        f.Enable(character);
        f.Invoke("SetDesktopPetPosition", 500.0, 250.0, true);
        WorkspaceLayoutState snapshot = WorkspaceLayoutManager.Capture(f.Layout);
        f.Layout.DesktopPet.IsVisible = false;
        f.Layout.DesktopPet.X = 700;
        WorkspaceLayoutManager.Apply(f.Layout, snapshot);
        Assert.AreEqual(700, f.Layout.DesktopPet.X);
        Assert.IsFalse(f.Layout.DesktopPet.IsVisible, "工作区不应重新打开全局配件。");
        Assert.AreEqual(character, f.Layout.DesktopPet.CharacterId);
        DesktopGeometry dual = new([
            Monitor("PRIMARY", new Rect(0, 0, 1000, 700), true),
            Monitor("SECONDARY", new Rect(1000, 0, 800, 700), false)]);
        f.Invoke("RemapLayoutBetweenGeometries", dual, f.Geometry);
        Assert.AreEqual(700, f.Layout.DesktopPet.X, "工作区拓扑迁移不应挪动全局配件。");
        f.Layout.DesktopPet.X = 1600;
        f.Layout.DesktopPet.Y = 620;
        Assert.IsTrue((bool)f.Invoke("RemapDesktopPet", dual, f.Geometry)!);
        f.Invoke("ApplyDesktopPetPosition");
        Assert.IsTrue(f.Layout.DesktopPet.X >= 0 && f.Layout.DesktopPet.X <= 1000 - f.Pet.Width);
        Assert.IsTrue(f.Layout.DesktopPet.Y >= 0 && f.Layout.DesktopPet.Y <= 700 - f.Pet.Height);
    }

    [STATestMethod]
    public void CharacterMenu_PreservesHiddenChoice_AndCancelsDragBeforeChangingSize()
    {
        using var f = new Fixture();
        ContextMenu menu = f.Window.DesktopPetToggle.ContextMenu;
        Assert.AreNotSame(menu, f.Pet.ContextMenu, "两个入口不能争用同一个菜单实例。");
        MenuItem vpet = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, "vpet"));
        vpet.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        f.Measure(1);
        Assert.AreEqual("vpet", f.Layout.DesktopPet.CharacterId);
        Assert.AreEqual("vpet", f.Pet.CharacterId);
        Assert.AreEqual(168, f.Pet.Width);
        Assert.AreEqual(Visibility.Collapsed, f.Pet.Visibility);
        Assert.IsFalse(f.Layout.DesktopPet.IsVisible);
        Assert.IsFalse(f.Pet.AnimationRunning);
        AppLayoutData saved = LayoutJsonSerializer.Deserialize(JsonSerializer.Serialize(f.Layout)).Layout;
        Assert.AreEqual("vpet", saved.DesktopPet.CharacterId);
        Assert.IsFalse(saved.DesktopPet.IsVisible);
        f.Invoke("DesktopPetMenu_Opened", menu, new RoutedEventArgs());
        Assert.IsTrue(vpet.IsChecked);
        Assert.IsTrue(menu.Items.OfType<MenuItem>().Where(item => Equals(item.Tag, "action")).All(item => !item.IsEnabled));

        f.Enable("vpet");
        Assert.AreEqual(168, f.Pet.ActualWidth);
        f.BeginFakeDrag(new Point(300, 200), new Point(650, 400));
        f.Invoke("SetDesktopPetCharacter", "pixelpaws");
        Assert.IsFalse(f.Field<bool>("_isDesktopPetDragging"));
        Assert.AreEqual(new Point(300, 200), f.Invoke("GetDesktopPetPosition"));
        Assert.AreEqual(300, f.Layout.DesktopPet.X);
        Assert.AreEqual("idle", f.Pet.CurrentClip);
        f.Invoke("SetDesktopPetPosition", 888.0, 588.0, true);
        f.Invoke("SetDesktopPetCharacter", "vpet");
        Assert.AreEqual(new Point(832, 532), f.Invoke("GetDesktopPetPosition"), "更大角色须重新夹入工作区。");
    }

    [STATestMethod]
    public void VPetActions_CompleteTransitions_ReuseTimerAndCache_WithoutSaving()
    {
        using var f = new Fixture();
        f.Enable("vpet");
        var timer = (DispatcherTimer)typeof(DesktopPetWidget).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Pet)!;
        long generation = f.Field<long>("_layoutSaveGeneration");
        bool dirty = f.Field<bool>("_layoutDirty");
        AdvanceUntil(f.Pet, "sleepStart", 200);
        AdvanceUntil(f.Pet, "sleepLoop");
        for (int i = 0; i < 18; i++) f.Pet.AdvanceAnimation();
        Assert.AreEqual("sleepLoop", f.Pet.CurrentClip);
        f.Pet.ReactToTouch();
        Assert.AreEqual("sleepEnd", f.Pet.CurrentClip, "睡眠中触摸应先唤醒。");
        AdvanceUntil(f.Pet, "touchStart");
        AdvanceUntil(f.Pet, "touchLoop");
        AdvanceUntil(f.Pet, "touchEnd");
        AdvanceUntil(f.Pet, "idle");
        for (int iteration = 0; iteration < 100; iteration++)
        {
            f.Pet.SetDragging(true);
            Assert.AreEqual("raisedStart", f.Pet.CurrentClip);
            Assert.IsTrue(f.Pet.AnimationRunning, "可见时提起动画应继续播放。");
            AdvanceUntil(f.Pet, "raisedLoop");
            for (int i = 0; i < 15; i++) f.Pet.AdvanceAnimation();
            Assert.AreEqual("raisedLoop", f.Pet.CurrentClip);
            f.Pet.SetDragging(false);
            Assert.AreEqual("raisedEnd", f.Pet.CurrentClip);
            AdvanceUntil(f.Pet, "idle");
            f.Pet.Rest();
            AdvanceUntil(f.Pet, "sleepLoop");
            f.Pet.WakeUp();
            AdvanceUntil(f.Pet, "idle");
            f.Pet.SetCharacter("pixelpaws");
            f.Pet.SetCharacter("vpet");
        }
        Assert.AreSame(timer, typeof(DesktopPetWidget).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Pet));
        Assert.AreEqual(generation, f.Field<long>("_layoutSaveGeneration"));
        Assert.AreEqual(dirty, f.Field<bool>("_layoutDirty"));
        foreach (string clip in VPetAnimation.ClipNames) Assert.AreSame(VPetAnimation.GetClip(clip), VPetAnimation.GetClip(clip));
        f.Pet.Rest();
        f.Invoke("SetDesktopPetVisible", false);
        Assert.IsFalse(timer.IsEnabled);
        f.Pet.AdvanceAnimation();
        Assert.AreEqual("idle", f.Pet.CurrentClip);
    }

    [STATestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    [DataRow(2.0)]
    public void VPetRendering_UsesPackagedActions_AndSharesNativeAndWpfHitGeometry(double scale)
    {
        using var f = new Fixture();
        f.Enable("vpet");
        f.Invoke("SetDesktopPetPosition", 300.0, 200.0, true);
        f.Measure(scale);
        Assert.IsTrue(f.Hit(384, 284));
        Assert.IsFalse(f.Hit(301, 201));
        Assert.AreSame(f.Pet, f.Window.RootGrid.InputHitTest(new Point(384, 284)));
        var icon = new Border { Width = 168, Height = 168, Background = Brushes.White };
        Canvas.SetLeft(icon, 300);
        Canvas.SetTop(icon, 200);
        f.Window.IconCanvas.Children.Add(icon);
        f.Measure(scale);
        Assert.AreSame(icon, f.Window.RootGrid.InputHitTest(new Point(384, 284)));
        f.Window.IconCanvas.Children.Remove(icon);

        var actions = new Action[] { () => { }, f.Pet.ReactToTouch,
            () => AdvanceUntil(f.Pet, "touchLoop"), () => AdvanceUntil(f.Pet, "touchEnd"),
            () => f.Pet.SetDragging(true), () => AdvanceUntil(f.Pet, "raisedLoop"),
            () => f.Pet.SetDragging(false), f.Pet.Rest, () => AdvanceUntil(f.Pet, "sleepLoop"), f.Pet.WakeUp };
        foreach (Action action in actions)
        {
            action();
            f.Measure(scale);
            VPetAnimation.Frame frame = VPetAnimation.GetClip(f.Pet.CurrentClip).Frames[f.Pet.CurrentFrame];
            Assert.IsTrue(frame.Image.IsFrozen);
            Assert.IsTrue(frame.Alpha.Any(alpha => alpha > 32));
            Assert.IsTrue(frame.Alpha.Any(alpha => alpha == 0));
            for (int y = 4; y < 168; y += 8)
            for (int x = 4; x < 168; x += 8)
            {
                bool expected = frame.Alpha[(int)(y * 192.0 / 168) * 192 + (int)(x * 192.0 / 168)] > 32;
                Assert.AreEqual(expected, f.Hit(300 + x, 200 + y));
                Assert.AreEqual(expected, f.Pet.InputHitTest(new Point(x, y)) != null);
            }
            f.RenderVPet(scale);
        }
        f.Invoke("SetDesktopPetPosition", 600.0, 200.0, true);
        f.Measure(scale);
        Assert.IsFalse(f.Hit(384, 284));
        int frames = VPetAnimation.ClipNames.Sum(name => VPetAnimation.GetClip(name).Frames.Length);
        Assert.AreEqual(72, frames);
        using var license = new StreamReader(VPetAnimation.OpenResource("ThirdParty/VPet-ANIMATION-LICENSE.txt"));
        StringAssert.Contains(license.ReadToEnd(), "https://github.com/LorisYounger/VPet");
    }

    [STATestMethod]
    public void ScenePreference_RoundTrips_PreservesCharacterPosition_AndKeepsOldLayoutsStandalone()
    {
        using var f = new Fixture();
        Assert.IsFalse(JsonSerializer.Deserialize<AppLayoutData>("{\"Version\":19}")!.DesktopPet.ShowScene);
        f.Enable("vpet");
        f.Invoke("SetDesktopPetPosition", 400.0, 300.0, true);
        ContextMenu menu = f.Pet.ContextMenu;
        MenuItem scene = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, "scene"));
        scene.IsChecked = true;
        scene.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        f.Measure(1);
        Assert.IsTrue(f.Layout.DesktopPet.ShowScene);
        Assert.AreEqual(360, f.Pet.ActualWidth);
        Assert.AreEqual(new Point(232, 163), f.Invoke("GetDesktopPetPosition"));
        Assert.AreEqual(new Rect(168, 137, 168, 168), f.Pet.CharacterBounds);
        f.Invoke("SetDesktopPetVisible", false);
        AppLayoutData saved = LayoutJsonSerializer.Deserialize(JsonSerializer.Serialize(f.Layout)).Layout;
        Assert.IsTrue(saved.DesktopPet.ShowScene);
        Assert.IsFalse(saved.DesktopPet.IsVisible);
        Assert.IsFalse(f.Pet.AnimationRunning);
        f.Invoke("InitializeDesktopPet");
        f.Invoke("DesktopPetMenu_Opened", f.Window.DesktopPetToggle.ContextMenu, new RoutedEventArgs());
        Assert.IsTrue(f.Window.DesktopPetToggle.ContextMenu.Items.OfType<MenuItem>()
            .Single(item => Equals(item.Tag, "scene")).IsChecked);
        f.Invoke("SetDesktopPetScene", false);
        f.Measure(1);
        Assert.AreEqual(new Point(400, 300), f.Invoke("GetDesktopPetPosition"));
        Assert.AreEqual(168, f.Pet.Width);
        Assert.AreEqual(Visibility.Collapsed, f.Pet.Visibility);
    }

    [STATestMethod]
    [DataRow(1.0, "vpet")]
    [DataRow(1.25, "vpet")]
    [DataRow(1.5, "vpet")]
    [DataRow(2.0, "vpet")]
    [DataRow(1.0, "pixelpaws")]
    [DataRow(2.0, "pixelpaws")]
    public void SceneRendering_AlignsCharacters_AndSharesTransparentHitGeometry(double scale, string character)
    {
        using var f = new Fixture();
        f.Enable(character);
        f.Invoke("SetDesktopPetScene", true);
        f.Invoke("SetDesktopPetPosition", 300.0, 200.0, true);
        f.Measure(scale);
        Assert.AreEqual(360, f.Pet.ActualWidth);
        Assert.IsTrue(DesktopPetScene.Background.Image.IsFrozen);
        Assert.IsTrue(DesktopPetScene.Foreground.Image.IsFrozen);
        Assert.IsTrue(f.Hit(420, 350), "树冠必须能收到鼠标输入。");
        Assert.IsFalse(f.Pet.ContainsCharacterPoint(new Point(120, 150)), "树冠不能被当成摸头目标。");
        Assert.IsFalse(f.Hit(301, 201), "场景透明留白必须穿透。");
        Assert.IsFalse(f.Hit(630, 330), "角色之外的场景空白必须穿透。");
        Assert.IsTrue(f.Pet.ContainsCharacterPoint(character == "vpet" ? new Point(252, 221) : new Point(252, 267)));
        for (int y = 5; y < 360; y += 10)
        for (int x = 5; x < 360; x += 10)
            Assert.AreEqual(f.Hit(300 + x, 200 + y), f.Pet.InputHitTest(new Point(x, y)) != null);
        var icon = new Border { Width = 100, Height = 100, Background = Brushes.White };
        Canvas.SetLeft(icon, 390);
        Canvas.SetTop(icon, 300);
        f.Window.IconCanvas.Children.Add(icon);
        f.Measure(scale);
        Assert.AreSame(icon, f.Window.RootGrid.InputHitTest(new Point(420, 350)), "图标仍在场景上方。");
        f.Window.IconCanvas.Children.Remove(icon);
        f.RenderScene(scale);
        long generation = f.Field<long>("_layoutSaveGeneration");
        Rect bounds = f.Pet.CharacterBounds;
        object timer = typeof(DesktopPetWidget).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Pet)!;
        f.Pet.Rest();
        if (character == "vpet") AdvanceUntil(f.Pet, "sleepLoop");
        f.Measure(scale);
        f.RenderScene(scale);
        Assert.AreEqual(bounds, f.Pet.CharacterBounds, "入睡时沿用同一角色画布，不能裁边造成漂移。");
        Assert.AreEqual(generation, f.Field<long>("_layoutSaveGeneration"));
        Assert.AreSame(timer, typeof(DesktopPetWidget).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Pet));
        f.Invoke("SetDesktopPetVisible", false);
        Assert.IsFalse(f.Hit(420, 350));
        Assert.IsFalse(f.Pet.AnimationRunning);
    }

    [STATestMethod]
    public void SceneDrag_CancelToggleAndMonitorRemoval_KeepWholeSceneInBounds()
    {
        using var f = new Fixture();
        f.Enable("vpet");
        f.Invoke("SetDesktopPetScene", true);
        f.Invoke("SetDesktopPetPosition", 900.0, 620.0, true);
        Assert.AreEqual(new Point(640, 340), f.Invoke("GetDesktopPetPosition"));
        f.BeginFakeDrag(new Point(300, 200), new Point(500, 320));
        Assert.AreEqual("raisedStart", f.Pet.CurrentClip);
        f.Invoke("CompleteDesktopPetDrag", true);
        Assert.AreEqual("raisedEnd", f.Pet.CurrentClip);
        Assert.AreEqual(500, f.Layout.DesktopPet.X);
        f.BeginFakeDrag(new Point(300, 200), new Point(500, 320));
        f.Invoke("SetDesktopPetScene", false);
        Assert.IsFalse(f.Field<bool>("_isDesktopPetDragging"));
        Assert.AreEqual(new Point(468, 337), f.Invoke("GetDesktopPetPosition"), "关闭场景应先撤销未提交拖动，再保留角色位置。");
        f.Invoke("SetDesktopPetScene", true);
        f.Invoke("SetDesktopPetCharacter", "pixelpaws");
        f.Measure(1);
        Assert.AreEqual(360, f.Pet.ActualWidth);
        Assert.AreEqual(new Rect(196, 192, 112, 112), f.Pet.CharacterBounds);
        DesktopGeometry dual = new([
            Monitor("PRIMARY", new Rect(0, 0, 1000, 700), true),
            Monitor("SECONDARY", new Rect(1000, 0, 800, 700), false)]);
        f.Layout.DesktopPet.X = 1440;
        f.Layout.DesktopPet.Y = 340;
        Assert.IsTrue((bool)f.Invoke("RemapDesktopPet", dual, f.Geometry)!);
        f.Invoke("ApplyDesktopPetPosition");
        Assert.AreEqual(640, f.Layout.DesktopPet.X);
        Assert.AreEqual(340, f.Layout.DesktopPet.Y);
        f.BeginFakeDrag(new Point(300, 200), new Point(500, 320));
        f.Set("_organizerPaused", true);
        f.Invoke("UpdateDesktopPetVisibility");
        Assert.AreEqual(new Point(300, 200), f.Invoke("GetDesktopPetPosition"));
        Assert.IsFalse(f.Pet.AnimationRunning);
    }

    [STATestMethod]
    public void SceneTap_DoesNotTriggerCharacterAction_WhileCharacterTapDoes()
    {
        using var f = new Fixture();
        f.Enable("vpet");
        f.Invoke("SetDesktopPetScene", true);
        f.Pet.Rest();
        AdvanceUntil(f.Pet, "sleepLoop");
        var e = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
            System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseUpEvent };
        f.Set("_isDesktopPetDragging", true);
        f.Set("_desktopPetDragMoved", false);
        f.Set("_desktopPetDragCharacter", false);
        f.Invoke("DesktopPet_MouseUp", f.Pet, e);
        Assert.AreEqual("sleepLoop", f.Pet.CurrentClip);
        f.Set("_isDesktopPetDragging", true);
        f.Set("_desktopPetDragCharacter", true);
        f.Invoke("DesktopPet_MouseUp", f.Pet, e);
        Assert.AreEqual("sleepEnd", f.Pet.CurrentClip);
        AdvanceUntil(f.Pet, "touchStart");
    }

    private static void AdvanceUntil(DesktopPetWidget pet, string clip, int maximumFrames = 100)
    {
        for (int frame = 0; frame < maximumFrames && pet.CurrentClip != clip; frame++) pet.AdvanceAnimation();
        Assert.AreEqual(clip, pet.CurrentClip, "动作应在有限帧内完成转换。");
    }

    private static DesktopMonitorRegion Monitor(string name, Rect area, bool primary) => new()
    { DeviceName = name, Bounds = area, WorkArea = area, IsPrimary = primary };

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        public MainWindow Window { get; } = new(false, _ => { });
        public DesktopPetWidget Pet => Window.DesktopPet;
        public AppLayoutData Layout => Field<AppLayoutData>("_appLayout");
        public DesktopGeometry Geometry { get; } = new([Monitor("PRIMARY", new Rect(0, 0, 1000, 700), true)]);
        private readonly OffscreenSource _source;

        public Fixture()
        {
            Window.Content = null;
            Window.ControlPanel.Visibility = Visibility.Collapsed;
            Window.ControlPanelRestoreButton.Visibility = Visibility.Collapsed;
            Window.RecycleBinWidget.Visibility = Visibility.Collapsed;
            Window.RootGrid.Background = new SolidColorBrush(Color.FromRgb(178, 211, 215));
            _source = new OffscreenSource(Window.RootGrid);
            Set("_desktopGeometry", Geometry);
        }

        public void Enable(string character = "pixelpaws")
        {
            Layout.DesktopPet.CharacterId = character;
            Layout.DesktopPet.IsVisible = true;
            Invoke("InitializeDesktopPet");
            Measure(1);
        }

        public T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(Window)!;
        public void Set(string name, object value) => typeof(MainWindow).GetField(name, Private)!.SetValue(Window, value);
        public object? Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(Window, args);
        public bool Hit(double x, double y) => (bool)Invoke("IsInteractiveClientPoint", new Point(x, y))!;

        public void BeginFakeDrag(Point start, Point preview)
        {
            Invoke("SetDesktopPetPosition", start.X, start.Y, true);
            Set("_isDesktopPetDragging", true);
            Set("_desktopPetDragMoved", true);
            Set("_desktopPetDragStartPosition", start);
            Pet.SetDragging(true);
            Invoke("SetDesktopPetPosition", preview.X, preview.Y, false);
        }

        public void Measure(double scale)
        {
            VisualTreeHelper.SetRootDpi(Window.RootGrid, new DpiScale(scale, scale));
            Window.RootGrid.Measure(new Size(1000, 700));
            Window.RootGrid.Arrange(new Rect(0, 0, 1000, 700));
            Window.RootGrid.UpdateLayout();
        }

        public void Render(string name, double scale)
        {
            Window.RootGrid.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)(1000 * scale), (int)(700 * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(Window.RootGrid);
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect((int)(556 * scale), (int)(340 * scale), 1, 1), pixel, 4, 0);
            Assert.IsTrue(pixel[0] < 150, "打包的真实猫图应绘制到桌面上，不能只是空控件。");
            if (Environment.GetEnvironmentVariable("DESKTOPORGANIZER_RENDER_PET_QA") != "1") return;
            string directory = Path.Combine(TestProjectFiles.Root, "artifacts/desktop-pet");
            Directory.CreateDirectory(directory);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, name));
            encoder.Save(stream);
        }

        public void RenderVPet(double scale)
        {
            Window.RootGrid.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)(1000 * scale), (int)(700 * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(Window.RootGrid);
            var crop = new CroppedBitmap(bitmap, new Int32Rect((int)(300 * scale), (int)(200 * scale),
                (int)(168 * scale), (int)(168 * scale)));
            var pixels = new byte[crop.PixelWidth * crop.PixelHeight * 4];
            crop.CopyPixels(pixels, crop.PixelWidth * 4, 0);
            Assert.IsTrue(pixels.Where((value, index) => index % 4 == 0).Any(value => value < 80), "真实角色应绘制出深色轮廓。");
            if (Environment.GetEnvironmentVariable("DESKTOPORGANIZER_RENDER_PET_QA") != "1") return;
            string directory = Path.Combine(TestProjectFiles.Root, "artifacts/vpet-adaptation/render");
            Directory.CreateDirectory(directory);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(crop));
            using var stream = File.Create(Path.Combine(directory, $"vpet-{Pet.CurrentClip}-{scale}.png"));
            encoder.Save(stream);
        }

        public void RenderScene(double scale)
        {
            Brush background = Window.RootGrid.Background;
            Window.RootGrid.Background = null;
            Window.RootGrid.UpdateLayout();
            var desktop = new RenderTargetBitmap((int)(1000 * scale), (int)(700 * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            desktop.Render(Window.RootGrid);
            Window.RootGrid.Background = background;
            var bitmap = new CroppedBitmap(desktop, new Int32Rect((int)(Pet.Margin.Left * scale), (int)(Pet.Margin.Top * scale),
                (int)(360 * scale), (int)(360 * scale)));
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect((int)(120 * scale), (int)(150 * scale), 1, 1), pixel, 4, 0);
            Assert.IsTrue(pixel[1] > pixel[0] && pixel[1] > pixel[2] && pixel[3] == 255,
                "打包的树冠应绘制为不透明绿色，不能仅通过命中检查。");
            bitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            Assert.AreEqual(0, pixel[3]);
            bitmap.CopyPixels(new Int32Rect((int)(175 * scale), (int)(316 * scale), 1, 1), pixel, 4, 0);
            Assert.IsTrue(pixel[2] > pixel[1], "前景粉色小花必须显示在草地上。");
            if (Environment.GetEnvironmentVariable("DESKTOPORGANIZER_RENDER_PET_QA") != "1") return;
            string directory = Path.Combine(TestProjectFiles.Root, "artifacts/scene-integration/render");
            Directory.CreateDirectory(directory);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, $"{Pet.CharacterId}-{Pet.CurrentClip}-{scale}.png"));
            encoder.Save(stream);
        }

        public void Dispose()
        {
            Set("_isClosing", true);
            Invoke("UpdateDesktopPetVisibility");
            Field<DispatcherTimer>("_layoutSaveTimer").Stop();
            Field<CancellationTokenSource>("_lifetimeCts").Cancel();
            Field<FileOperationService>("_fileOperationService").Dispose();
            Field<FolderPortalWatcherCoordinator>("_folderPortalWatcherCoordinator").Dispose();
            _source.Dispose();
        }
    }

    private sealed class OffscreenSource : PresentationSource, IDisposable
    {
        private readonly Visual _root;
        private bool _disposed;
        public OffscreenSource(Visual root) { _root = root; AddSource(); RootChanged(null, root); }
        public override Visual RootVisual { get => _root; set => throw new NotSupportedException(); }
        public override bool IsDisposed => _disposed;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
        public void Dispose() { RootChanged(_root, null); RemoveSource(); _disposed = true; }
    }
}
