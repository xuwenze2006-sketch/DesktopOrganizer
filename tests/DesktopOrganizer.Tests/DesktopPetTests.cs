using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    public void SystemTab_PickerEntryFitsAndApplyingSelectionCanReopenHiddenPet()
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
        Assert.AreEqual("选择桌宠", f.Window.DesktopPetToggle.Content);
        f.Invoke("ApplyDesktopPetSelection", "pixelpaws", true, false);
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
            Assert.AreEqual("fall", f.Pet.CurrentClip);
            AdvanceUntil(f.Pet, "raisedEnd");
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
    public void VPetLanding_PlaysImpactThenRecovery_WithAQuickerUnevenRhythm()
    {
        using var f = new Fixture();
        f.Enable("vpet");
        f.Pet.SetDragging(true);
        f.Pet.SetDragging(false);
        AdvanceUntil(f.Pet, "raisedEnd");
        VPetAnimation.Clip landing = VPetAnimation.GetClip("raisedEnd");
        var timer = (DispatcherTimer)typeof(DesktopPetWidget)
            .GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Pet)!;

        // 保留接触、缓冲、撑起和站稳姿势，跳过重复趴地与末尾大幅摇摆。
        int[] expectedCells = [0, 4, 6, 8, 1, 3, 7, 11, 13, 14, 15, 18, 19, 20];
        int[] expectedDurations = [45, 45, 65, 160, 70, 80, 85, 85, 95, 85, 80, 85, 110, 150];
        Assert.HasCount(expectedCells.Length, landing.Frames);
        Assert.HasCount(expectedCells.Length, landing.Milliseconds);
        Assert.AreEqual(1240, landing.Milliseconds.Sum());
        using Stream stream = VPetAnimation.OpenResource("Assets/Pets/VPet/RaisedEnd.png");
        var sheet = new BitmapImage();
        sheet.BeginInit();
        sheet.CacheOption = BitmapCacheOption.OnLoad;
        sheet.StreamSource = stream;
        sheet.EndInit();
        sheet.Freeze();

        for (int step = 0; step < expectedCells.Length; step++)
        {
            Assert.AreEqual("raisedEnd", f.Pet.CurrentClip);
            Assert.AreEqual(step, f.Pet.CurrentFrame);
            int cell = expectedCells[step];
            var expected = new CroppedBitmap(sheet, new Int32Rect(cell % 8 * 192, cell / 8 * 192, 192, 192));
            byte[] expectedPixels = BgraPixels(expected);
            VPetAnimation.Frame current = landing.Frames[f.Pet.CurrentFrame];
            CollectionAssert.AreEqual(expectedPixels, BgraPixels(current.Image),
                $"落地第 {step} 帧应使用原图集第 {cell} 格，不能交错回退到前一段姿势。");
            byte[] expectedAlpha = Enumerable.Range(0, 192 * 192)
                .Select(pixel => expectedPixels[pixel * 4 + 3]).ToArray();
            CollectionAssert.AreEqual(expectedAlpha, current.Alpha,
                "透明命中缓存也必须跟随实际播放图像。");

            int expectedMilliseconds = expectedDurations[step];
            Assert.AreEqual(expectedMilliseconds, landing.Milliseconds[step],
                "落地缓冲须稍停，起身须连续，不能恢复均匀慢放或重复趴地。");
            Assert.AreEqual(TimeSpan.FromMilliseconds(expectedMilliseconds), timer.Interval);
            f.Pet.AdvanceAnimation();
        }
        Assert.AreEqual("idle", f.Pet.CurrentClip, "完整恢复站立后才回到待机。");
    }

    [STATestMethod]
    [DataRow(false, 1.0)]
    [DataRow(true, 1.5)]
    [DataRow(true, 2.0)]
    public void VPetFall_KeepsTheReleasePose_Accelerates_AndMovesHitGeometryWithTheImage(bool scene, double scale)
    {
        using var f = new Fixture();
        f.Enable("vpet");
        f.Invoke("SetDesktopPetScene", scene);
        f.Invoke("SetDesktopPetPosition", 300.0, 200.0, true);
        f.Measure(scale);
        f.Pet.SetDragging(true);
        AdvanceUntil(f.Pet, "raisedLoop");
        f.Pet.AdvanceAnimation();
        VPetAnimation.Frame releasedFrame = f.Pet.CurrentVPetFrame;
        var preview = new List<(BitmapSource Image, int Milliseconds)>();
        void Capture(int milliseconds)
        {
            if (Environment.GetEnvironmentVariable("DESKTOPORGANIZER_RENDER_FALL_QA") != "1") return;
            f.Window.RootGrid.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)(1000 * scale),
                (int)(700 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            Brush originalBackground = f.Window.RootGrid.Background;
            f.Window.RootGrid.Background = null;
            try { bitmap.Render(f.Window.RootGrid); }
            finally { f.Window.RootGrid.Background = originalBackground; }
            var crop = new CroppedBitmap(bitmap, new Int32Rect((int)(f.Pet.Margin.Left * scale),
                (int)(f.Pet.Margin.Top * scale), (int)(f.Pet.ActualWidth * scale), (int)(f.Pet.ActualHeight * scale)));
            Assert.IsTrue(BgraPixels(crop).Where((value, index) => index % 4 == 3).Any(alpha => alpha > 32),
                "动作预览必须包含实际控件画面，不能导出空白图。");
            crop.Freeze();
            preview.Add((crop, milliseconds));
        }
        Capture(200);
        long saveGeneration = f.Field<long>("_layoutSaveGeneration");
        Thickness originalPosition = f.Pet.Margin;
        f.Pet.SetDragging(false);
        Assert.AreEqual("fall", f.Pet.CurrentClip);
        Assert.AreSame(releasedFrame, f.Pet.CurrentVPetFrame, "松手不能立刻换成趴地图像。");
        Assert.IsTrue(f.Pet.CharacterMotionTransform.IsIdentity);
        var timer = (DispatcherTimer)typeof(DesktopPetWidget)
            .GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Pet)!;
        Rect bounds = f.Pet.CharacterBounds;
        double lastY = 0, lastVelocity = 0;
        int fallDuration = 0;
        for (int step = 0; step < 6; step++)
        {
            Assert.AreEqual("fall", f.Pet.CurrentClip);
            Assert.AreSame(releasedFrame, f.Pet.CurrentVPetFrame, "下落复用冻结帧，不逐帧生成位图。");
            Matrix motion = f.Pet.CharacterMotionTransform;
            Point pivot = new(bounds.X + bounds.Width * .5, bounds.Y + bounds.Height * .6);
            double y = motion.Transform(pivot).Y - pivot.Y;
            if (step > 0)
            {
                Assert.IsTrue(y > lastY);
                Assert.IsTrue(y - lastY > lastVelocity, "下落位移应逐步增大。");
                lastVelocity = y - lastY;
            }
            lastY = y;
            fallDuration += (int)timer.Interval.TotalMilliseconds;
            Capture((int)timer.Interval.TotalMilliseconds);
            Matrix inverse = motion;
            inverse.Invert();
            for (int py = 2; py < f.Pet.ActualHeight; py += 5)
            for (int px = 2; px < f.Pet.ActualWidth; px += 5)
            {
                Point sample = new(px, py);
                Point local = inverse.Transform(sample);
                bool inside = local.X >= bounds.Left && local.X < bounds.Right &&
                    local.Y >= bounds.Top && local.Y < bounds.Bottom;
                bool expected = inside && releasedFrame.Alpha[(int)((local.Y - bounds.Y) * 192 / bounds.Height) * 192 +
                    (int)((local.X - bounds.X) * 192 / bounds.Width)] > 32;
                Assert.AreEqual(expected, f.Pet.ContainsCharacterPoint(sample), "透明命中应随旋转和下落移动。");
            }
            Assert.AreEqual(originalPosition, f.Pet.Margin, "下落只改变角色画面，不能移动整棵树或写回布局。");
            f.Pet.AdvanceAnimation();
        }
        Assert.AreEqual(152, fallDuration);
        Assert.AreEqual("raisedEnd", f.Pet.CurrentClip);
        Assert.IsTrue(f.Pet.CharacterMotionTransform.IsIdentity);
        for (int remaining = 0; remaining < 20 && f.Pet.CurrentClip != "idle"; remaining++)
        {
            Capture((int)timer.Interval.TotalMilliseconds);
            f.Pet.AdvanceAnimation();
        }
        Assert.AreEqual("idle", f.Pet.CurrentClip);
        Capture(650);
        if (preview.Count > 0) SaveFallPreview(preview, $"fall-{(scene ? "scene" : "standalone")}-{scale}");
        Assert.AreEqual(saveGeneration, f.Field<long>("_layoutSaveGeneration"));
    }

    [STATestMethod]
    public void VPetFall_CanBeInterruptedWithoutLeavingAMotionOffsetOrTimer()
    {
        using var f = new Fixture();
        f.Enable("vpet");
        void Fall()
        {
            f.Pet.SetDragging(true);
            f.Pet.SetDragging(false);
            f.Pet.AdvanceAnimation();
            Assert.IsFalse(f.Pet.CharacterMotionTransform.IsIdentity);
        }
        Fall();
        f.Pet.SetDragging(true);
        Assert.AreEqual("raisedStart", f.Pet.CurrentClip);
        Assert.IsTrue(f.Pet.CharacterMotionTransform.IsIdentity);
        f.Pet.SetDragging(false);
        f.Pet.ReactToTouch();
        Assert.AreEqual("touchStart", f.Pet.CurrentClip);
        Assert.IsTrue(f.Pet.CharacterMotionTransform.IsIdentity);
        Fall();
        f.Pet.SetCharacter("pixelpaws");
        Assert.IsTrue(f.Pet.CharacterMotionTransform.IsIdentity);
        f.Pet.SetCharacter("vpet");
        Fall();
        f.Pet.SetActive(false);
        Assert.AreEqual("idle", f.Pet.CurrentClip);
        Assert.IsFalse(f.Pet.AnimationRunning);
        Assert.IsTrue(f.Pet.CharacterMotionTransform.IsIdentity);
    }

    [STATestMethod]
    public void VPetIdleBlink_PreservesOriginalFramesAndHitGeometry_AndChangesOnlyTheEyes()
    {
        using var f = new Fixture();
        f.Enable("vpet");
        VPetAnimation.Clip idle = VPetAnimation.GetClip("idle");
        var timer = (DispatcherTimer)typeof(DesktopPetWidget)
            .GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Pet)!;
        Assert.HasCount(10, idle.Frames);
        Assert.HasCount(10, idle.Milliseconds);
        Assert.AreEqual(3000, idle.Milliseconds.Sum());
        using Stream stream = VPetAnimation.OpenResource("Assets/Pets/VPet/Idle.png");
        var originalSheet = new BitmapImage();
        originalSheet.BeginInit();
        originalSheet.CacheOption = BitmapCacheOption.OnLoad;
        originalSheet.StreamSource = stream;
        originalSheet.EndInit();
        originalSheet.Freeze();

        for (int step = 0; step < 8; step++)
        {
            Assert.AreEqual("idle", f.Pet.CurrentClip);
            Assert.AreEqual(step, f.Pet.CurrentFrame);
            var original = new CroppedBitmap(originalSheet, new Int32Rect(step * 192, 0, 192, 192));
            CollectionAssert.AreEqual(BgraPixels(original), BgraPixels(idle.Frames[step].Image),
                $"待机原第 {step} 帧的像素必须完整保留。");
            f.Pet.AdvanceAnimation();
        }

        Assert.AreEqual(8, f.Pet.CurrentFrame);
        Assert.AreEqual(110, idle.Milliseconds[8]);
        Assert.AreEqual(TimeSpan.FromMilliseconds(110), timer.Interval);
        byte[] originalPixels = BgraPixels(new CroppedBitmap(originalSheet, new Int32Rect(0, 0, 192, 192)));
        VPetAnimation.Frame closedEyes = idle.Frames[8];
        byte[] closedPixels = BgraPixels(closedEyes.Image);
        byte[] originalAlpha = Enumerable.Range(0, 192 * 192)
            .Select(pixel => originalPixels[pixel * 4 + 3]).ToArray();
        CollectionAssert.AreEqual(originalAlpha, closedEyes.Alpha,
            "生成闭眼帧不能改变原人物的透明命中边界。");

        Int32Rect[] eyeRegions = [new(77, 49, 15, 15), new(99, 46, 13, 16)];
        int changedPixels = 0;
        for (int pixel = 0; pixel < 192 * 192; pixel++)
        {
            int offset = pixel * 4;
            if (originalPixels.AsSpan(offset, 4).SequenceEqual(closedPixels.AsSpan(offset, 4)))
                continue;
            changedPixels++;
            int x = pixel % 192;
            int y = pixel / 192;
            Assert.IsTrue(eyeRegions.Any(region => x >= region.X && x < region.X + region.Width &&
                y >= region.Y && y < region.Y + region.Height),
                $"闭眼修改不能影响眼部范围外的像素 ({x}, {y})。");
        }
        Assert.IsTrue(changedPixels > 0, "闭眼帧必须包含实际变化，不能重复原睁眼图片。");

        f.Pet.AdvanceAnimation();
        Assert.AreEqual("idle", f.Pet.CurrentClip);
        Assert.AreEqual(9, f.Pet.CurrentFrame);
        CollectionAssert.AreEqual(originalPixels, BgraPixels(idle.Frames[9].Image),
            "眨眼后必须恢复原第零帧，避免人物身体或画布跳动。");
        Assert.AreEqual(1390, idle.Milliseconds[9]);
        Assert.AreEqual(TimeSpan.FromMilliseconds(1390), timer.Interval);
        f.Pet.AdvanceAnimation();
        Assert.AreEqual("idle", f.Pet.CurrentClip);
        Assert.AreEqual(0, f.Pet.CurrentFrame);
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
            VPetAnimation.Frame frame = f.Pet.CurrentVPetFrame;
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
        Assert.AreEqual(67, frames);
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
        Assert.AreEqual("fall", f.Pet.CurrentClip);
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

    private static void SaveFallPreview(List<(BitmapSource Image, int Milliseconds)> frames, string name, string folder = "artifacts/pet-fall-v3")
    {
        string directory = Path.Combine(TestProjectFiles.Root, folder);
        Directory.CreateDirectory(directory);
        var encoder = new GifBitmapEncoder();
        int size = frames[0].Image.PixelWidth;
        var contact = new DrawingVisual();
        using (DrawingContext grid = contact.RenderOpen())
        {
            grid.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, size * 6, size * 4));
            for (int i = 0; i < frames.Count; i++)
            {
                var canvas = new DrawingVisual();
                using (DrawingContext drawing = canvas.RenderOpen())
                {
                    drawing.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, size, size));
                    drawing.DrawImage(frames[i].Image, new Rect(0, 0, size, size));
                }
                var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(canvas);
                var metadata = new BitmapMetadata("gif");
                metadata.SetQuery("/grctlext/Delay", (ushort)Math.Max(1, Math.Round(frames[i].Milliseconds / 10.0)));
                metadata.SetQuery("/grctlext/Disposal", (byte)2);
                encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
                grid.DrawImage(bitmap, new Rect(i % 6 * size, i / 6 * size, size, size));
            }
        }
        using var encoded = new MemoryStream();
        encoder.Save(encoded);
        byte[] gif = encoded.ToArray();
        int headerLength = 13 + ((gif[10] & 0x80) != 0 ? 3 * (1 << ((gif[10] & 7) + 1)) : 0);
        // GIF 的循环扩展；帧时值来自上面实际控件的计时器。
        byte[] loop = [0x21, 0xff, 0x0b, 0x4e, 0x45, 0x54, 0x53, 0x43, 0x41, 0x50, 0x45, 0x32, 0x2e, 0x30, 3, 1, 0, 0, 0];
        using (var output = File.Create(Path.Combine(directory, name + ".gif")))
        {
            output.Write(gif, 0, headerLength);
            output.Write(loop);
            int offset = headerLength, frame = 0, elapsed = 0, encodedTime = 0;
            int EndSubBlocks(int position)
            {
                while (position < gif.Length && gif[position] != 0) position += gif[position] + 1;
                if (position >= gif.Length) throw new InvalidDataException("Incomplete GIF data.");
                return position + 1;
            }
            // WPF 编码器可能忽略多帧延时元数据；按实际帧时值重写图形控制扩展。
            while (offset < gif.Length)
            {
                int start = offset;
                if (gif[offset] == 0x21)
                {
                    byte label = gif[offset + 1];
                    offset = EndSubBlocks(offset + 2);
                    if (label != 0xf9) output.Write(gif, start, offset - start);
                }
                else if (gif[offset] == 0x2c)
                {
                    elapsed += frames[frame++].Milliseconds;
                    int nextTime = (int)Math.Round(elapsed / 10.0, MidpointRounding.AwayFromZero);
                    int delay = Math.Max(1, nextTime - encodedTime);
                    encodedTime += delay;
                    output.Write([0x21, 0xf9, 4, 8, (byte)(delay & 255), (byte)(delay >> 8), 0, 0]);
                    byte flags = gif[offset + 9];
                    offset += 10 + ((flags & 0x80) != 0 ? 3 * (1 << ((flags & 7) + 1)) : 0);
                    offset = EndSubBlocks(offset + 1);
                    output.Write(gif, start, offset - start);
                }
                else if (gif[offset] == 0x3b)
                {
                    output.WriteByte(0x3b);
                    break;
                }
                else throw new InvalidDataException("Unexpected GIF block.");
            }
            Assert.AreEqual(frames.Count, frame);
        }
        var decoded = new GifBitmapDecoder(new Uri(Path.Combine(directory, name + ".gif")),
            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert.AreEqual(frames.Count, decoded.Frames.Count);
        int decodedTime = decoded.Frames.Sum(frame => Convert.ToInt32(((BitmapMetadata)frame.Metadata).GetQuery("/grctlext/Delay")) * 10);
        Assert.IsTrue(Math.Abs(decodedTime - frames.Sum(frame => frame.Milliseconds)) <= 5,
            "GIF 总时长须与实际控件一致，仅允许厘秒精度误差。");
        var sheet = new RenderTargetBitmap(size * 6, size * 4, 96, 96, PixelFormats.Pbgra32);
        sheet.Render(contact);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(sheet));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        png.Save(stream);
    }

    private static byte[] BgraPixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        converted.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return pixels;
    }

    [STATestMethod]
    [DataRow("black-haired", false, 1.0)]
    [DataRow("pink-haired", true, 1.5)]
    [DataRow("black-haired", true, 2.0)]
    public void ImportedCharacters_AnimateAllTransitions_KeepTransparencyAndReuseFrames(string id, bool scene, double scale)
    {
        using var f = new Fixture();
        f.Enable(id);
        f.Invoke("SetDesktopPetScene", scene);
        f.Invoke("SetDesktopPetPosition", 300.0, 200.0, true);
        f.Measure(scale);
        Assert.AreEqual(id, f.Pet.CharacterId);
        Assert.AreEqual(scene ? 360 : 168, f.Pet.Width);
        var idle = VPetAnimation.GetClip(id, "idle");
        Assert.AreSame(idle.Frames[0], VPetAnimation.GetClip(id, "touchEnd").Frames[^1]);
        CollectionAssert.AreEqual(idle.Frames[0].Alpha, idle.Frames[3].Alpha, "眨眼不得改变身体轮廓。");
        long generation = f.Field<long>("_layoutSaveGeneration");
        Assert.IsFalse(f.Pet.ContainsCharacterPoint(new Point(0, 0)));
        string[] clips = ["idle", "wave", "touchStart", "touchLoop", "touchEnd", "raisedStart", "raisedLoop", "raisedEnd", "sleepStart", "sleepLoop", "sleepEnd"];
        foreach (string name in clips)
        {
            var clip = VPetAnimation.GetClip(id, name);
            Assert.AreSame(clip, VPetAnimation.GetClip(id, name));
            Assert.AreEqual(clip.Frames.Length, clip.Milliseconds.Length);
            Assert.IsTrue(clip.Milliseconds.All(ms => ms >= 30 && ms <= 3000));
            foreach (var frame in clip.Frames)
            {
                Assert.IsTrue(frame.Image.IsFrozen);
                Assert.AreEqual(256, frame.Image.PixelWidth);
                Assert.IsTrue(frame.Alpha.Count(a => a == 0) > frame.Alpha.Length * .55, "周围留白必须真正透明。");
                Assert.IsTrue(frame.Alpha.Count(a => a > 230) > 1800, "不能交付空帧。");
                Assert.IsTrue(Enumerable.Range(0, 256).All(i => frame.Alpha[i] == 0 && frame.Alpha[255*256+i] == 0), "上下边缘不能截断人物。");
            }
        }
        f.Pet.Wave(); Assert.AreEqual("wave", f.Pet.CurrentClip); AdvanceUntil(f.Pet, "idle");
        f.Pet.ReactToTouch(); AdvanceUntil(f.Pet, "touchLoop"); AdvanceUntil(f.Pet, "idle");
        f.Pet.SetDragging(true); AdvanceUntil(f.Pet, "raisedLoop");
        var released = f.Pet.CurrentVPetFrame;
        f.Pet.SetDragging(false);
        Assert.AreEqual("fall", f.Pet.CurrentClip);
        Assert.AreSame(released, f.Pet.CurrentVPetFrame);
        f.Pet.AdvanceAnimation();
        Assert.IsTrue(f.Pet.CharacterMotionTransform.OffsetY > 0);
        Assert.AreEqual(0, f.Pet.CharacterMotionTransform.M12, "新角色应竖直下落，不额外旋转。");
        AdvanceUntil(f.Pet, "raisedEnd"); AdvanceUntil(f.Pet, "idle");
        Assert.IsTrue(f.Pet.CharacterMotionTransform.IsIdentity);
        f.Pet.Rest(); AdvanceUntil(f.Pet, "sleepLoop");
        f.Pet.Wave(); Assert.AreEqual("sleepEnd", f.Pet.CurrentClip);
        AdvanceUntil(f.Pet, "wave"); AdvanceUntil(f.Pet, "idle");
        f.Pet.SetDragging(true); f.Pet.SetDragging(false); f.Pet.ReactToTouch();
        Assert.AreEqual("touchStart", f.Pet.CurrentClip);
        Assert.IsTrue(f.Pet.CharacterMotionTransform.IsIdentity);
        Assert.AreEqual(generation, f.Field<long>("_layoutSaveGeneration"), "动作不得写回布局。");
        f.Invoke("SetDesktopPetVisible", false);
        Assert.IsFalse(f.Pet.AnimationRunning);
        f.Pet.SetCharacter("pixelpaws");
        Assert.AreEqual("idle", f.Pet.CurrentClip);
    }

    [STATestMethod]
    [DataRow(1.0)]
    [DataRow(1.5)]
    [DataRow(2.0)]
    public void PinkScene_PreservesTransparentDesktopArea_AndWaveFootAnchor(double scale)
    {
        using var f = new Fixture();
        f.Enable("pink-haired");
        f.Invoke("SetDesktopPetScene", true);
        f.Measure(scale);
        var scene = DesktopPetScene.BackgroundFor("pink-haired");
        Assert.AreSame(scene, DesktopPetScene.BackgroundFor("pink-haired"));
        Assert.IsTrue(scene.Image.IsFrozen);
        Assert.IsFalse(DesktopPetScene.HasForeground("pink-haired"));
        Assert.IsTrue(scene.Alpha.Count(a => a == 0) > scene.Alpha.Length * .5);
        Assert.IsFalse(f.Pet.ContainsOpaquePoint(new Point(180, 30)), "屋檐上方必须穿透桌面，不能保留照片矩形。");
        Assert.IsTrue(f.Pet.ContainsOpaquePoint(new Point(180, 220)), "城楼实体仍能拖动。");
        int width = scene.Image.PixelWidth;
        for (int y = 0; y < 360; y += 5)
        for (int x = 0; x < 360; x += 5)
        {
            bool background = scene.Alpha[(int)(y * scene.Image.PixelHeight / 360.0) * width + (int)(x * width / 360.0)] > 32;
            Assert.AreEqual(background || f.Pet.ContainsCharacterPoint(new Point(x, y)),
                f.Pet.ContainsOpaquePoint(new Point(x, y)), "透明命中必须与新场景和人物一致。");
        }
        var idle = VPetAnimation.GetClip("pink-haired", "idle").Frames[0];
        var wave = VPetAnimation.GetClip("pink-haired", "wave");
        Assert.AreEqual(7, wave.Frames.Distinct().Count(), "六张新挥手姿势加回到待机，不能沿用整人旋转帧。");
        static int FootRow(VPetAnimation.Frame frame) => Array.FindLastIndex(frame.Alpha, a => a > 230) / 256;
        foreach (var frame in wave.Frames)
            Assert.IsTrue(Math.Abs(FootRow(frame) - FootRow(idle)) <= 2, "挥手时不能上下跳脚。");
        f.Invoke("SetDesktopPetScene", false);
        Assert.AreEqual(168, f.Pet.Width);
        Assert.IsFalse(f.Pet.SceneEnabled);
    }

    [STATestMethod]
    public void PinkScene_PickerDefaultsNewSelectionToScene_AndRespectsSavedOptOut()
    {
        var picker = new DesktopPetPicker("vpet", true, false);
        picker.Characters.SelectedValue = "pink-haired";
        Assert.IsTrue(picker.SceneSelected);
        Assert.AreEqual("搭配透明夕阳城楼", picker.ShowScene.Content);
        Assert.AreSame(DesktopPetScene.PinkPreview.Image, DesktopPetCatalog.Preview("pink-haired"));
        var reopened = new DesktopPetPicker("pink-haired", true, false);
        Assert.IsFalse(reopened.SceneSelected, "重新打开不能覆盖用户关闭背景的决定。");
        using var f = new Fixture();
        f.Enable("vpet");
        f.Invoke("SetDesktopPetCharacter", "pink-haired");
        Assert.IsTrue(f.Pet.SceneEnabled);
        f.Invoke("SetDesktopPetScene", false);
        var saved = LayoutJsonSerializer.Deserialize(JsonSerializer.Serialize(f.Layout)).Layout.DesktopPet;
        Assert.AreEqual("pink-haired", saved.CharacterId);
        Assert.IsFalse(saved.ShowScene);
    }

    [STATestMethod]
    public void PetPicker_AllImportedCardsAreVisible_StagedSelectionDoesNotChangeDesktopUntilApplied()
    {
        using var f = new Fixture();
        f.Enable();
        var picker = new DesktopPetPicker("pixelpaws", false, false);
        using var source = new OffscreenSource(picker);
        VisualTreeHelper.SetRootDpi(picker, new DpiScale(1.5, 1.5));
        picker.Measure(new Size(600, 620));
        picker.Arrange(new Rect(0, 0, 600, 620));
        picker.UpdateLayout();
        Assert.AreEqual(5, picker.Characters.Items.Count);
        foreach (DesktopPetPicker.Card card in picker.Characters.Items)
        {
            Assert.IsNotNull(card.Preview);
            Assert.IsTrue(card.Preview.IsFrozen);
            var item = (ListBoxItem)picker.Characters.ItemContainerGenerator.ContainerFromItem(card);
            Assert.IsTrue(item.ActualHeight > 150);
        }
        picker.Characters.SelectedValue = "pink-haired";
        picker.ShowPet.IsChecked = true;
        picker.ShowScene.IsChecked = true;
        Assert.AreEqual("pixelpaws", f.Layout.DesktopPet.CharacterId, "选择卡片只是预览，尚未应用。");
        bool requested = false;
        picker.ApplyRequested += () => { requested = true; f.Invoke("ApplyDesktopPetSelection", picker.SelectedCharacterId, picker.ShowOnDesktop, picker.SceneSelected); };
        picker.ApplyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.IsTrue(requested);
        Assert.AreEqual("pink-haired", f.Pet.CharacterId);
        Assert.IsTrue(f.Pet.SceneEnabled);
        Assert.IsTrue(f.Layout.DesktopPet.IsVisible);
        var saved = LayoutJsonSerializer.Deserialize(JsonSerializer.Serialize(f.Layout)).Layout;
        Assert.AreEqual("pink-haired", saved.DesktopPet.CharacterId);
        f.Invoke("NormalizeLayout");
        Assert.AreEqual("pink-haired", f.Layout.DesktopPet.CharacterId);
        var reopened = new DesktopPetPicker(saved.DesktopPet.CharacterId, saved.DesktopPet.IsVisible, saved.DesktopPet.ShowScene);
        Assert.AreEqual("pink-haired", reopened.SelectedCharacterId);
        Assert.IsTrue(reopened.ShowOnDesktop && reopened.SceneSelected);
        StringAssert.Contains(picker.SelectedDescription.Text, "粉发伙伴");
        picker.UpdateLayout();
        if (Environment.GetEnvironmentVariable("DESKTOPORGANIZER_RENDER_IMPORTED_QA") == "1")
        {
            string directory = Path.Combine(TestProjectFiles.Root, "artifacts/two-character-actions-v2");
            var image = new RenderTargetBitmap(900, 930, 144, 144, PixelFormats.Pbgra32);
            image.Render(picker);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var output = File.Create(Path.Combine(directory, "picker-preview.png")); encoder.Save(output);
        }
        // A narrow window remains scrollable and keeps its apply controls on screen.
        picker.Measure(new Size(410, 360)); picker.Arrange(new Rect(0, 0, 410, 360)); picker.UpdateLayout();
        Point button = picker.ApplyButton.TranslatePoint(new Point(0, picker.ApplyButton.ActualHeight), picker);
        Assert.IsTrue(button.Y <= 360);
        Assert.IsTrue(picker.Characters.ActualHeight > 60);
        picker.Characters.SelectedIndex = -1;
        Assert.IsFalse(picker.ApplyButton.IsEnabled);
        Assert.IsFalse(picker.PreviewButton.IsEnabled);
        requested = false;
        picker.ApplyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.IsFalse(requested);
        picker.Characters.SelectedValue = "black-haired";
        Assert.IsTrue(picker.ApplyButton.IsEnabled && picker.PreviewButton.IsEnabled);
    }

    [STATestMethod]
    [DataRow("black-haired")]
    [DataRow("pink-haired")]
    public void ImportedCharacters_RenderRealActionsAndMatchingHitGeometry(string id)
    {
        using var f = new Fixture();
        f.Enable(id);
        f.Invoke("SetDesktopPetScene", true);
        f.Invoke("SetDesktopPetPosition", 300.0, 200.0, true);
        f.Measure(1.5);
        var timer = (DispatcherTimer)typeof(DesktopPetWidget).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Pet)!;
        var preview = new List<(BitmapSource Image, int Milliseconds)>();
        void Capture(int milliseconds)
        {
            Rect bounds = f.Pet.CharacterBounds;
            var frame = f.Pet.CurrentVPetFrame;
            Matrix inverse = f.Pet.CharacterMotionTransform;
            inverse.Invert();
            for (int y = 0; y < 360; y += 5)
            for (int x = 0; x < 360; x += 5)
            {
                Point p = inverse.Transform(new Point(x, y));
                bool inside = p.X >= bounds.Left && p.X < bounds.Right && p.Y >= bounds.Top && p.Y < bounds.Bottom;
                bool opaque = inside && frame.Alpha[(int)((p.Y-bounds.Top)*256/bounds.Height)*256 + (int)((p.X-bounds.Left)*256/bounds.Width)] > 32;
                Assert.AreEqual(opaque, f.Pet.ContainsCharacterPoint(new Point(x, y)));
            }
            f.Window.RootGrid.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1500, 1050, 144, 144, PixelFormats.Pbgra32);
            bitmap.Render(f.Window.RootGrid);
            var crop = new CroppedBitmap(bitmap, new Int32Rect(450, 300, 540, 540));
            Assert.IsTrue(BgraPixels(crop).Where((v, i) => i % 4 == 0).Any(v => v < 80));
            if (Environment.GetEnvironmentVariable("DESKTOPORGANIZER_RENDER_IMPORTED_QA") == "1")
                preview.Add((crop, milliseconds));
        }
        void PlayUntilIdle()
        {
            for (int i = 0; i < 60 && f.Pet.CurrentClip != "idle"; i++)
            { Capture((int)timer.Interval.TotalMilliseconds); f.Pet.AdvanceAnimation(); }
            Assert.AreEqual("idle", f.Pet.CurrentClip);
            Capture(500);
        }
        Capture(600);
        f.Pet.Wave(); PlayUntilIdle();
        f.Pet.ReactToTouch(); PlayUntilIdle();
        if (preview.Count > 0) { SaveFallPreview(preview, id+"-interaction", "artifacts/two-character-actions-v2"); preview.Clear(); }
        f.Pet.SetDragging(true); AdvanceUntil(f.Pet, "raisedLoop"); Capture(400);
        f.Pet.SetDragging(false); PlayUntilIdle();
        if (preview.Count > 0) { SaveFallPreview(preview, id+"-landing", "artifacts/two-character-actions-v2"); preview.Clear(); }
        f.Pet.Rest();
        for (int i = 0; i < 10 && f.Pet.CurrentClip != "sleepLoop"; i++)
        { Capture((int)timer.Interval.TotalMilliseconds); f.Pet.AdvanceAnimation(); }
        Assert.AreEqual("sleepLoop", f.Pet.CurrentClip);
        Capture(1000); f.Pet.WakeUp(); PlayUntilIdle();
        if (preview.Count > 0) SaveFallPreview(preview, id+"-rest", "artifacts/two-character-actions-v2");
    }

    [STATestMethod]
    [DataRow(false, 1.0)]
    [DataRow(true, 1.5)]
    [DataRow(true, 2.0)]
    public void SunsetDuo_UsesAlignedTransparentFrames_WholePairMovesAndStopsWithoutFalling(bool scene, double scale)
    {
        const string id = DesktopPetCatalog.SunsetDuoId;
        using var f = new Fixture();
        f.Enable(id);
        f.Invoke("SetDesktopPetScene", scene);
        f.Invoke("SetDesktopPetPosition", 300.0, 200.0, true);
        f.Measure(scale);
        Assert.AreEqual(360, f.Pet.Width);
        Assert.AreEqual(new Rect(0, 0, 360, 360), f.Pet.CharacterBounds);
        var idle = VPetAnimation.GetClip(id, "idle");
        Assert.AreSame(idle.Frames[0], VPetAnimation.GetClip(id, "touchEnd").Frames[^1]);
        // Blink inserts alter eye color only, while breathing retains its own contour.
        CollectionAssert.AreEqual(idle.Frames[2].Alpha, idle.Frames[3].Alpha);
        CollectionAssert.AreEqual(idle.Frames[8].Alpha, idle.Frames[9].Alpha);
        Assert.IsFalse(BgraPixels(idle.Frames[2].Image).SequenceEqual(BgraPixels(idle.Frames[3].Image)));
        string[] names = ["idle", "wave", "touchStart", "touchLoop", "touchEnd", "raisedStart", "raisedLoop", "raisedEnd", "sleepStart", "sleepLoop", "sleepEnd"];
        foreach (string name in names)
        {
            var clip = VPetAnimation.GetClip(id, name);
            Assert.AreSame(clip, VPetAnimation.GetClip(id, name));
            Assert.AreEqual(clip.Frames.Length, clip.Milliseconds.Length);
            Assert.IsTrue(clip.Milliseconds.All(ms => ms >= 80 && ms <= 1000));
            foreach (var frame in clip.Frames)
            {
                Assert.IsTrue(frame.Image.IsFrozen);
                Assert.AreEqual(512, frame.Image.PixelWidth);
                Assert.AreEqual(512, frame.Image.PixelHeight);
                Assert.IsTrue(frame.Alpha.Count(a => a == 0) > frame.Alpha.Length * .5);
                Assert.IsTrue(frame.Alpha.Count(a => a > 230) > 25000);
                Assert.IsTrue(frame.Alpha.Take(512).All(a => a == 0) && frame.Alpha.TakeLast(512).All(a => a == 0));
                Assert.IsTrue(frame.Alpha.Skip(465 * 512).SequenceEqual(idle.Frames[0].Alpha.Skip(465 * 512)), "脚底在所有动作中保持原位。");
            }
        }
        long generation = f.Field<long>("_layoutSaveGeneration");
        f.Pet.ReactToTouch(); AdvanceUntil(f.Pet, "touchLoop"); AdvanceUntil(f.Pet, "idle");
        f.Pet.SetDragging(true); AdvanceUntil(f.Pet, "raisedLoop");
        for (int i = 0; i < 12; i++) { f.Pet.AdvanceAnimation(); Assert.IsTrue(f.Pet.CharacterMotionTransform.IsIdentity); }
        f.Pet.SetDragging(false);
        Assert.AreEqual("raisedEnd", f.Pet.CurrentClip, "组合停止移动时不能出现单人下落变换。");
        AdvanceUntil(f.Pet, "idle");
        f.Pet.Rest(); AdvanceUntil(f.Pet, "sleepLoop");
        f.Pet.Wave(); Assert.AreEqual("sleepEnd", f.Pet.CurrentClip);
        AdvanceUntil(f.Pet, "wave"); AdvanceUntil(f.Pet, "idle");
        f.Pet.Rest(); AdvanceUntil(f.Pet, "sleepLoop"); f.Pet.WakeUp(); AdvanceUntil(f.Pet, "idle");
        Assert.AreEqual(generation, f.Field<long>("_layoutSaveGeneration"), "动画不能触发布局磁盘保存。");
        for (int y = 0; y < 360; y += 7)
        for (int x = 0; x < 360; x += 7)
        {
            Point point = new(x, y);
            bool character = f.Pet.CurrentVPetFrame.Alpha[(int)(y * 512.0 / 360) * 512 + (int)(x * 512.0 / 360)] > 32;
            bool background = scene && DesktopPetScene.ContainsOpaquePoint(point, new Size(360, 360), id);
            Assert.AreEqual(character, f.Pet.ContainsCharacterPoint(point));
            Assert.AreEqual(character || background, f.Pet.ContainsOpaquePoint(point));
        }
        Assert.IsFalse(f.Pet.ContainsOpaquePoint(new Point(5, 180)), "肖像左右透明区域应穿透鼠标。");
        Assert.AreEqual(scene, f.Pet.ContainsOpaquePoint(new Point(70, 35)), "背景只能在开启时接收拖动。");
        f.Invoke("SetDesktopPetVisible", false);
        Assert.IsFalse(f.Pet.AnimationRunning);
        Assert.IsFalse(f.Pet.ContainsOpaquePoint(new Point(180, 180)));
    }

    [STATestMethod]
    public void SunsetDuo_PickerStagesSelectionAndPersistsOptionalBackground_QuickMenuKeepsLegacyCharacters()
    {
        using var f = new Fixture();
        f.Enable("vpet");
        var picker = new DesktopPetPicker("vpet", true, false);
        picker.Characters.SelectedValue = DesktopPetCatalog.SunsetDuoId;
        Assert.IsTrue(picker.SceneSelected, "首次选择双人组合默认搭配夕阳背景。");
        StringAssert.Contains(picker.ShowScene.Content.ToString()!, "夕阳城楼");
        Assert.AreEqual("vpet", f.Pet.CharacterId);
        picker.ApplyRequested += () => f.Invoke("ApplyDesktopPetSelection", picker.SelectedCharacterId, picker.ShowOnDesktop, picker.SceneSelected);
        picker.ApplyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        f.Invoke("NormalizeLayout");
        var saved = LayoutJsonSerializer.Deserialize(JsonSerializer.Serialize(f.Layout)).Layout.DesktopPet;
        Assert.AreEqual(DesktopPetCatalog.SunsetDuoId, saved.CharacterId);
        Assert.IsTrue(saved.ShowScene && saved.IsVisible);
        var reopened = new DesktopPetPicker(saved.CharacterId, saved.IsVisible, saved.ShowScene);
        Assert.AreEqual(saved.CharacterId, reopened.SelectedCharacterId);
        Assert.IsTrue(reopened.SceneSelected);
        using (var source = new OffscreenSource(reopened))
        {
            reopened.Measure(new Size(600, 620)); reopened.Arrange(new Rect(0, 0, 600, 620)); reopened.UpdateLayout();
            reopened.Characters.ScrollIntoView(reopened.Characters.SelectedItem); reopened.UpdateLayout();
            var card = (ListBoxItem)reopened.Characters.ItemContainerGenerator.ContainerFromItem(reopened.Characters.SelectedItem);
            Assert.IsTrue(card.ActualHeight > 150);
            if (Environment.GetEnvironmentVariable("DESKTOPORGANIZER_RENDER_SUNSET_QA") == "1")
            {
                var image = new RenderTargetBitmap(600, 620, 96, 96, PixelFormats.Pbgra32); image.Render(reopened);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                string path = Path.Combine(TestProjectFiles.Root, "artifacts/sunset-duo-actions-v2/picker-preview.png");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var output = File.Create(path); encoder.Save(output);
            }
        }
        picker.ShowScene.IsChecked = false;
        picker.ApplyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.IsFalse(f.Pet.SceneEnabled);
        var withoutScene = new DesktopPetPicker(f.Pet.CharacterId, true, false);
        Assert.IsFalse(withoutScene.SceneSelected, "重新打开选择器要保留用户关闭背景的决定。");
        f.Invoke("SetDesktopPetCharacter", "vpet");
        Assert.AreEqual(168, f.Pet.Width);
        Assert.AreSame(DesktopPetScene.Background, DesktopPetScene.BackgroundFor("vpet"));
        f.Invoke("SetDesktopPetCharacter", DesktopPetCatalog.SunsetDuoId);
        Assert.IsTrue(f.Pet.SceneEnabled);
        var menu = f.Pet.ContextMenu;
        f.Invoke("DesktopPetMenu_Opened", menu, new RoutedEventArgs());
        Assert.IsTrue(menu.Items.OfType<MenuItem>().Single(i => Equals(i.Tag, DesktopPetCatalog.SunsetDuoId)).IsChecked);
        Assert.AreEqual("夕阳城楼", menu.Items.OfType<MenuItem>().Single(i => Equals(i.Tag, "scene")).Header);
        f.Invoke("SetDesktopPetCharacter", "pink-haired");
        f.Invoke("DesktopPetMenu_Opened", menu, new RoutedEventArgs());
        Assert.AreEqual("透明夕阳城楼", menu.Items.OfType<MenuItem>().Single(i => Equals(i.Tag, "scene")).Header);
    }

    [STATestMethod]
    public void SunsetDuo_ActualControlRendersBackgroundAndExportsTimedAnimation()
    {
        using var f = new Fixture();
        f.Enable(DesktopPetCatalog.SunsetDuoId);
        f.Invoke("SetDesktopPetScene", true);
        f.Invoke("SetDesktopPetPosition", 300.0, 200.0, true);
        f.Measure(1.5);
        var timer = (DispatcherTimer)typeof(DesktopPetWidget).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Pet)!;
        bool export = Environment.GetEnvironmentVariable("DESKTOPORGANIZER_RENDER_SUNSET_QA") == "1";
        var preview = new List<(BitmapSource Image, int Milliseconds)>();
        BitmapSource Capture(int milliseconds)
        {
            f.Window.RootGrid.UpdateLayout();
            var image = new RenderTargetBitmap(1500, 1050, 144, 144, PixelFormats.Pbgra32);
            image.Render(f.Window.RootGrid);
            var crop = new CroppedBitmap(image, new Int32Rect(450, 300, 540, 540));
            if (export) preview.Add((crop, milliseconds));
            return crop;
        }
        byte[] first = BgraPixels(Capture((int)timer.Interval.TotalMilliseconds));
        for (int i = 1; i < VPetAnimation.GetClip(DesktopPetCatalog.SunsetDuoId, "idle").Frames.Length; i++)
        { f.Pet.AdvanceAnimation(); Capture((int)timer.Interval.TotalMilliseconds); }
        f.Pet.Wave();
        for (int i = 0; i < 30 && f.Pet.CurrentClip != "idle"; i++)
        { Capture((int)timer.Interval.TotalMilliseconds); f.Pet.AdvanceAnimation(); }
        Assert.AreEqual("idle", f.Pet.CurrentClip);
        if (export) { SaveFallPreview(preview, "desktop-idle-and-greeting", "artifacts/sunset-duo-actions-v2"); preview.Clear(); }
        f.Pet.Rest();
        AdvanceUntil(f.Pet, "sleepLoop");
        byte[] resting = BgraPixels(Capture((int)timer.Interval.TotalMilliseconds));
        Assert.IsFalse(first.SequenceEqual(resting), "真实控件必须呈现闭眼变化。");
        // Bottom shoes and the sunset sky are static even when the expression changes.
        Assert.IsTrue(first.Skip(500 * 540 * 4).SequenceEqual(resting.Skip(500 * 540 * 4)));
        for (int i = 1; i < 8; i++) { f.Pet.AdvanceAnimation(); Capture((int)timer.Interval.TotalMilliseconds); }
        f.Pet.WakeUp();
        for (int i = 0; i < 30 && f.Pet.CurrentClip != "idle"; i++)
        { Capture((int)timer.Interval.TotalMilliseconds); f.Pet.AdvanceAnimation(); }
        Assert.AreEqual("idle", f.Pet.CurrentClip);
        Capture(500);
        if (export) SaveFallPreview(preview, "desktop-rest-and-wake", "artifacts/sunset-duo-actions-v2");
        f.Invoke("SetDesktopPetScene", false); f.Measure(1.5);
        Assert.IsFalse(first.SequenceEqual(BgraPixels(Capture(300))), "关闭背景应实际改变绘制结果。");
    }

    [STATestMethod]
    public void SunsetDuo_AmbientActionsCycleBeforeRest_AndHiddenOrDraggedPetsDoNotReact()
    {
        using var f = new Fixture(); f.Enable(DesktopPetCatalog.SunsetDuoId);
        long generation = f.Field<long>("_layoutSaveGeneration");
        foreach (string next in new[] { "wave", "look", "sway", "sleepStart" })
        {
            AdvanceUntil(f.Pet, next, 50);
            if (next != "sleepStart") AdvanceUntil(f.Pet, "idle");
        }
        AdvanceUntil(f.Pet, "sleepLoop");
        AdvanceUntil(f.Pet, "idle", 30);
        f.Pet.Rest(); AdvanceUntil(f.Pet, "sleepLoop");
        for (int i = 0; i < 24; i++) f.Pet.AdvanceAnimation();
        Assert.AreEqual("sleepLoop", f.Pet.CurrentClip, "手动休息应持续，自动依偎才会自行结束。");
        f.Pet.SwayHands(); Assert.AreEqual("sleepEnd", f.Pet.CurrentClip);
        AdvanceUntil(f.Pet, "sway"); AdvanceUntil(f.Pet, "idle");
        f.Pet.SetDragging(true); f.Pet.LookAtPartner(); f.Pet.SwayHands();
        Assert.AreEqual("raisedStart", f.Pet.CurrentClip);
        f.Pet.SetDragging(false); AdvanceUntil(f.Pet, "idle");
        Assert.AreEqual(generation, f.Field<long>("_layoutSaveGeneration"));
        f.Invoke("SetDesktopPetVisible", false); f.Pet.LookAtPartner(); f.Pet.SwayHands();
        Assert.AreEqual("idle", f.Pet.CurrentClip); Assert.IsFalse(f.Pet.AnimationRunning);
        f.Enable("pink-haired"); f.Pet.LookAtPartner(); f.Pet.SwayHands();
        Assert.AreEqual("idle", f.Pet.CurrentClip, "组合专用动作不能套用到其他角色。");
        var menu = f.Pet.ContextMenu;
        f.Invoke("DesktopPetMenu_Opened", menu, new RoutedEventArgs());
        Assert.IsTrue(menu.Items.OfType<MenuItem>().Where(i => Equals(i.Tag, "duo-action")).All(i => i.Visibility == Visibility.Collapsed));
    }

    [STATestMethod]
    [DataRow("look")]
    [DataRow("sway")]
    public void SunsetDuo_AddedActionsChangeUpperBody_KeepFeetAndHitGeometry(string action)
    {
        using var f = new Fixture(); f.Enable(DesktopPetCatalog.SunsetDuoId);
        f.Invoke("SetDesktopPetScene", true); f.Invoke("SetDesktopPetPosition", 300.0, 200.0, true); f.Measure(1.5);
        byte[] original = BgraPixels(f.Pet.CurrentVPetFrame.Image);
        var timer = (DispatcherTimer)typeof(DesktopPetWidget).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Pet)!;
        if (action == "look") f.Pet.LookAtPartner(); else f.Pet.SwayHands();
        Assert.AreEqual(action, f.Pet.CurrentClip);
        int largestChange = 0;
        var preview = new List<(BitmapSource Image, int Milliseconds)>();
        for (int step = 0; step < 30 && f.Pet.CurrentClip != "idle"; step++)
        {
            var frame = f.Pet.CurrentVPetFrame; byte[] current = BgraPixels(frame.Image);
            int changed = 0;
            for (int i = 0; i < current.Length; i += 4)
                if (Math.Abs(current[i] - original[i]) + Math.Abs(current[i+1] - original[i+1]) + Math.Abs(current[i+2] - original[i+2]) > 45) changed++;
            largestChange = Math.Max(largestChange, changed);
            Assert.IsTrue(original.Skip(465*512*4).SequenceEqual(current.Skip(465*512*4)), "肢体动作不得改动原脚底像素。");
            for (int y = 0; y < 360; y += 7)
            for (int x = 0; x < 360; x += 7)
                Assert.AreEqual(frame.Alpha[(int)(y*512.0/360)*512+(int)(x*512.0/360)] > 32, f.Pet.ContainsCharacterPoint(new Point(x,y)));
            if (Environment.GetEnvironmentVariable("DESKTOPORGANIZER_RENDER_SUNSET_QA") == "1")
            {
                f.Window.RootGrid.UpdateLayout();
                var image = new RenderTargetBitmap(1500, 1050, 144, 144, PixelFormats.Pbgra32); image.Render(f.Window.RootGrid);
                preview.Add((new CroppedBitmap(image,new Int32Rect(450,300,540,540)),(int)timer.Interval.TotalMilliseconds));
            }
            f.Pet.AdvanceAnimation();
        }
        Assert.AreEqual("idle", f.Pet.CurrentClip);
        Assert.IsTrue(largestChange > 1000, "新动作必须带来明显肢体或朝向变化，不能只是原有眨眼微动。");
        if (preview.Count > 0) SaveFallPreview(preview,"desktop-"+action,"artifacts/sunset-duo-actions-v2");
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
