using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopPetLibraryTests
{
    [STATestMethod]
    public void PersonalMetadata_SurvivesImportHideRestoreAndRestart_WithoutReloadingFrames()
    {
        using var f = new Files();
        var library = new DesktopPetLibrary(f.Library);
        var asset = library.Import(f.Image("first.png", 32, 32), "character");
        var clip = library.GetClip(asset.Id, "idle");
        library.SetDisplayName(asset.Id, "  我的伙伴  ");
        library.SetDisplayName("tiananmen", "我的背景");
        library.SetFavorite(asset.Id, true);
        library.SetFavorite("tiananmen", true);
        library.SetRemoved(asset.Id, true);
        library.SetRemoved("tiananmen", true);
        library.SetRemoved(asset.Id, false);
        library.Import(f.Image("second.png", 32, 32), "scene");
        Assert.AreSame(clip, library.GetClip(asset.Id, "idle"));
        var restarted = new DesktopPetLibrary(f.Library);
        Assert.IsNull(restarted.LoadWarning);
        Assert.AreEqual("我的伙伴", restarted.GetDisplayName(asset.Id));
        Assert.AreEqual("我的背景", restarted.GetDisplayName("tiananmen"));
        Assert.IsTrue(restarted.IsFavorite(asset.Id) && restarted.IsFavorite("tiananmen"));
        Assert.IsFalse(restarted.IsRemoved(asset.Id));
        Assert.IsTrue(restarted.IsRemoved("tiananmen"));
        restarted.RestoreAll();
        restarted.SetDisplayName(asset.Id, null);
        restarted = new DesktopPetLibrary(f.Library);
        Assert.AreEqual("first", restarted.GetDisplayName(asset.Id));
        Assert.AreEqual("我的背景", restarted.GetDisplayName("tiananmen"));
        Assert.IsTrue(restarted.IsFavorite(asset.Id));
        Assert.IsFalse(restarted.IsRemoved("tiananmen"));
    }

    [STATestMethod]
    public void PersonalMetadata_InvalidNameOrFailedSavePreservesDiskAndSnapshot()
    {
        using var f = new Files();
        var library = new DesktopPetLibrary(f.Library);
        library.SetDisplayName("vpet", "小萝");
        string path = Path.Combine(f.Library, "library.json");
        string before = File.ReadAllText(path);
        var snapshot = library.ReadSnapshot();
        foreach (string invalid in new[] { " ", new string('名', 41), "换\n行" })
            Assert.Throws<ArgumentException>(() => library.SetDisplayName("vpet", invalid));
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            foreach (Action write in new Action[] { () => library.SetDisplayName("vpet", "新名称"),
                () => library.SetFavorite("vpet", true), () => library.SetRemoved("vpet", true) })
            {
                try { write(); Assert.Fail("被锁定的索引不得发布更改。"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        Assert.AreSame(snapshot, library.ReadSnapshot());
        Assert.AreEqual(before, File.ReadAllText(path));
        Assert.AreEqual("小萝", library.GetDisplayName("vpet"));
        Assert.IsFalse(library.IsFavorite("vpet") || library.IsRemoved("vpet"));
    }

    [STATestMethod]
    public void PersonalMetadata_OldIndexLoadsAndCorruptNewFieldsAreProtected()
    {
        using var f = new Files();
        Directory.CreateDirectory(f.Library);
        string path = Path.Combine(f.Library, "library.json");
        File.WriteAllText(path, "{\"Assets\":[],\"Removed\":[\"vpet\"]}");
        var library = new DesktopPetLibrary(f.Library);
        Assert.IsNull(library.LoadWarning);
        Assert.IsTrue(library.IsRemoved("vpet"));
        library.SetDisplayName("vpet", "小萝");
        foreach (string invalid in new[] { "{\"DisplayNames\":null}", "{\"Favorites\":null}", "{\"DisplayNames\":{\"vpet\":\" \"}}" })
        {
            File.WriteAllText(path, invalid);
            library = new DesktopPetLibrary(f.Library);
            Assert.IsNotNull(library.LoadWarning);
            Assert.Throws<IOException>(() => library.SetDisplayName("vpet", "小萝"));
            Assert.AreEqual(invalid, File.ReadAllText(path));
        }
    }

    [STATestMethod]
    public void PersonalNames_LoadForBuiltInAndImportedCatalogs_WithoutChangingIdentity()
    {
        using var f = new Files();
        var library = new DesktopPetLibrary(f.Library);
        var imported = library.Import(f.Image("original.png", 32, 32), "character");
        string path = Path.Combine(f.Library, "library.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Assets = library.Assets, Removed = Array.Empty<string>(),
            DisplayNames = new Dictionary<string, string>
            { ["vpet"] = "小萝", ["tiananmen"] = "我的背景", [imported.Id] = "我的伙伴" },
            Favorites = new[] { "vpet", imported.Id }
        }));
        var original = DesktopPetLibrary.Shared;
        var shared = typeof(DesktopPetLibrary).GetField("_shared",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        try
        {
            shared.SetValue(null, new DesktopPetLibrary(f.Library));
            Assert.AreEqual("小萝", DesktopPetCatalog.Find("vpet").Name);
            Assert.AreEqual("小萝", DesktopPetCatalog.Characters.Single(c => c.Id == "vpet").Name);
            Assert.AreEqual("我的伙伴", DesktopPetCatalog.Find(imported.Id).Name);
            Assert.AreEqual("我的背景", DesktopPetScene.NameFor("pink-haired"));
            Assert.AreEqual("original", DesktopPetLibrary.Shared.FindAsset(imported.Id)!.Name);
            Assert.IsTrue(Directory.Exists(Path.Combine(f.Library, imported.Id)));
        }
        finally { shared.SetValue(null, original); }
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ImportedActions_SwitchImmediatelyWithoutWaitingForMissingOrLongClips(bool hasTouch)
    {
        using var f = new Files();
        f.Image("actions.png", 64, 32);
        var clips = new Dictionary<string, object>
        {
            ["idle"] = new { sheet = "actions.png", frames = new[] { new { index = 0, milliseconds = 30000 } } }
        };
        if (hasTouch) clips["touchStart"] = new { sheet = "actions.png", frames = new[] { new { index = 1, milliseconds = 30000 } } };
        string manifest = Path.Combine(f.Source, "manifest.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new { cellSize = 32, columns = 2, clips }));
        var library = new DesktopPetLibrary(f.Library);
        var asset = library.Import(manifest, "character");
        var original = DesktopPetLibrary.Shared;
        // 全套测试禁用并行；临时库只在本用例内替换，结束恢复，不触碰真实用户目录。
        var shared = typeof(DesktopPetLibrary).GetField("_shared",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var pet = new DesktopPetWidget();
        using var source = new OffscreenSource(pet);
        try
        {
            shared.SetValue(null, library);
            pet.SetCharacter(asset.Id); pet.SetActive(true);
            pet.ReactToTouch();
            pet.Wave();
            Assert.AreEqual("wave", pet.CurrentClip, "自定义素材缺动作或动作很长时，不能排队等待收尾。");
            pet.Rest();
            Assert.AreEqual("sleepStart", pet.CurrentClip);
            pet.WakeUp();
            Assert.AreEqual("idle", pet.CurrentClip, "缺少睡眠素材时，无需等待待机帧模拟唤醒。");
        }
        finally
        {
            pet.SetActive(false);
            shared.SetValue(null, original);
        }
    }

    [STATestMethod]
    public void ImportFailure_RollsBackFilesAndSnapshot_WhenLicenseCopyFails()
    {
        using var f = new Files();
        f.Image("actions.png", 64, 32);
        string manifest = f.Manifest("actions.png", 1, 120);
        string license = Path.Combine(f.Source, "LICENSE.txt");
        File.WriteAllText(license, "keep this license");
        var library = new DesktopPetLibrary(f.Library);
        var before = library.Assets;
        using (var locked = new FileStream(license, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => library.Import(manifest, "character"));
        Assert.AreSame(before, library.Assets);
        Assert.IsEmpty(Directory.GetFileSystemEntries(f.Library), "失败不得遗留暂存目录、索引或目标素材。");
        Assert.IsTrue(File.Exists(manifest));
        Assert.AreEqual("keep this license", File.ReadAllText(license));
        var successful = library.Import(manifest, "character");
        Assert.AreEqual(successful.Id, library.Assets.Single().Id, "失败后应能再次导入。");
    }

    [STATestMethod]
    public void IndexWriteFailure_PreservesOldSnapshotAndFiles_WithoutPublishingNewAsset()
    {
        using var f = new Files();
        string source = f.Image("first.png", 64, 32);
        var library = new DesktopPetLibrary(f.Library);
        var original = library.Import(source, "scene");
        var before = library.Assets;
        string indexPath = Path.Combine(f.Library, "library.json");
        string previous = File.ReadAllText(indexPath);
        using (var locked = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { library.Import(source, "character"); Assert.Fail("索引锁定时不应成功提交。"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            Assert.AreSame(before, library.Assets);
            CollectionAssert.AreEquivalent(new[] { original.Id, "library.json" },
                Directory.GetFileSystemEntries(f.Library).Select(Path.GetFileName).ToArray());
        }
        Assert.AreEqual(previous, File.ReadAllText(indexPath));
        Assert.IsFalse(library.IsRemoved(original.Id));
    }

    [STATestMethod]
    public void EmptyStemName_RemainsValidAfterRestart()
    {
        using var f = new Files();
        var library = new DesktopPetLibrary(f.Library);
        var asset = library.Import(f.Image(".png", 32, 32), "character");
        Assert.IsFalse(string.IsNullOrWhiteSpace(asset.Name));
        var restarted = new DesktopPetLibrary(f.Library);
        Assert.IsNull(restarted.LoadWarning);
        Assert.AreEqual(asset, restarted.Assets.Single());
    }

    [STATestMethod]
    public void InvalidMetadata_IsReportedWithoutOverwritingTheIndex()
    {
        using var f = new Files();
        Directory.CreateDirectory(f.Library);
        string path = Path.Combine(f.Library, "library.json");
        string invalid = "{\"Assets\":[{\"Id\":\"../outside\",\"Name\":\"bad\",\"Kind\":\"scene\",\"Animated\":false}],\"Removed\":[]}";
        File.WriteAllText(path, invalid);
        var library = new DesktopPetLibrary(f.Library);
        Assert.IsNotNull(library.LoadWarning);
        Assert.IsTrue(DesktopPetLibrary.IsAssetError(new InvalidDataException()));
        Assert.Throws<IOException>(() => library.RestoreAll());
        Assert.AreEqual(invalid, File.ReadAllText(path));
    }

    [STATestMethod]
    public void AsyncImport_ReturnsFrozenImages_AndPreCancelledImportDoesNotTouchLibrary()
    {
        using var f = new Files();
        string path = f.Image("async.png", 64, 128);
        var library = new DesktopPetLibrary(f.Library);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => library.ImportAsync(path, "character", cancelled.Token).GetAwaiter().GetResult());
        Assert.IsFalse(Directory.Exists(f.Library));
        var snapshot = library.Assets;
        var first = library.ImportAsync(path, "character").GetAwaiter().GetResult();
        var image = library.GetClip(first.Id, "idle").Frames[0].Image;
        Assert.IsTrue(image.IsFrozen, "后台解码的位图必须能安全跨线程使用。");
        Assert.AreEqual(512, image.PixelWidth);
        Assert.IsEmpty(snapshot, "原快照不得随着后续导入变化。");
        Assert.AreEqual(first, library.FindAsset(first.Id));
    }

    [STATestMethod]
    public void ConcurrentImportsAndReaders_SeeOnlyCompleteImmutableSnapshots()
    {
        using var f = new Files();
        string path = f.Image("parallel.png", 32, 64);
        var library = new DesktopPetLibrary(f.Library);
        var pending = Enumerable.Range(0, 3).Select(_ => library.ImportAsync(path, "character")).ToArray();
        var snapshots = new List<IReadOnlyList<DesktopPetLibrary.Asset>>();
        var read = Task.Run(() =>
        {
            while (!pending.All(t => t.IsCompleted))
            {
                var current = library.Assets;
                foreach (var asset in current)
                    Assert.IsTrue(library.GetClip(asset.Id, "idle").Frames[0].Image.IsFrozen);
                if (snapshots.Count == 0 || !ReferenceEquals(snapshots[^1], current)) snapshots.Add(current);
                Thread.Yield();
            }
        });
        Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        read.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        Assert.HasCount(3, library.Assets);
        Assert.HasCount(3, new DesktopPetLibrary(f.Library).Assets);
        Assert.AreEqual(3, library.Assets.Select(a => a.Id).Distinct().Count());
    }

    [STATestMethod]
    public void RepeatedCatalogQueries_ReuseSnapshotsWithoutPerFrameListAllocations()
    {
        var characters = DesktopPetCatalog.Characters;
        var scenes = DesktopPetScene.Scenes;
        for (int i = 0; i < 100; i++)
        { _ = DesktopPetCatalog.Find("vpet"); _ = DesktopPetScene.ResolveId("tiananmen", "vpet"); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
        {
            _ = DesktopPetCatalog.Find("vpet");
            _ = DesktopPetCatalog.Contains("pink-haired");
            _ = DesktopPetScene.ResolveId("tiananmen", "vpet");
            _ = DesktopPetCatalog.Characters;
            _ = DesktopPetScene.Scenes;
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsLessThan(65536L, bytes, $"重复查询分配了 {bytes} 字节，不应每帧重建目录。");
        Assert.AreSame(characters, DesktopPetCatalog.Characters);
        Assert.AreSame(scenes, DesktopPetScene.Scenes);
    }

    [STATestMethod]
    public void ImportImage_CopiesAndFitsWithoutStretching_RemovalIsRecoverableAcrossRestart()
    {
        using var f = new Files();
        string png = f.Image("partner.png", 80, 160);
        File.WriteAllText(Path.Combine(f.Source, "LICENSE.txt"), "test license");
        var library = new DesktopPetLibrary(f.Library);
        var asset = library.Import(png, "character");
        Assert.IsFalse(asset.Animated);
        Assert.AreEqual("partner", asset.Name);
        var idle = library.GetClip(asset.Id, "idle");
        Assert.AreEqual(512, idle.Frames[0].Image.PixelWidth);
        Assert.AreEqual(0, idle.Frames[0].Alpha[256 * 512 + 10], "窄图两侧留透明空白。");
        Assert.AreEqual(255, idle.Frames[0].Alpha[256 * 512 + 256]);
        Assert.AreSame(idle, library.GetClip(asset.Id, "wave"), "静态图片缺少动作时安全回退。");
        Assert.AreEqual("test license", File.ReadAllText(Path.Combine(f.Library, asset.Id, "LICENSE.txt")));
        library.SetRemoved(asset.Id, true);
        var reloaded = new DesktopPetLibrary(f.Library);
        Assert.IsTrue(reloaded.IsRemoved(asset.Id));
        Assert.IsTrue(File.Exists(png), "移除不删除用户原图。");
        Assert.IsTrue(File.Exists(Path.Combine(f.Library, asset.Id, "image.png")));
        reloaded.RestoreAll();
        Assert.IsFalse(new DesktopPetLibrary(f.Library).IsRemoved(asset.Id));
    }

    [STATestMethod]
    public void ImportScene_UsesOwnCopyAndPreservesTransparency()
    {
        using var f = new Files();
        string png = f.Image("wide.png", 160, 80);
        var library = new DesktopPetLibrary(f.Library);
        var asset = library.Import(png, "scene");
        File.Delete(png);
        var image = new DesktopPetLibrary(f.Library).GetImage(asset.Id);
        Assert.AreEqual(1080, image.Image.PixelHeight);
        Assert.AreEqual(0, image.Alpha[100 * 1080 + 540]);
        Assert.AreEqual(255, image.Alpha[1000 * 1080 + 540]);
    }

    [STATestMethod]
    public void AnimationImport_PreservesFramesAndTimings_AndMissingActionsFallback()
    {
        using var f = new Files();
        f.Image("actions.png", 64, 32);
        var library = new DesktopPetLibrary(f.Library);
        var asset = library.Import(f.Manifest("actions.png", 1, 120), "character");
        Assert.IsTrue(asset.Animated);
        var imported = new DesktopPetLibrary(f.Library);
        var clip = imported.GetClip(asset.Id, "idle");
        Assert.AreEqual(32, clip.Frames[0].Image.PixelWidth);
        Assert.AreEqual(120, clip.Milliseconds[0]);
        Assert.AreSame(clip, imported.GetClip(asset.Id, "sleepStart"));
    }

    [STATestMethod]
    [DataRow("../escape.png", 0, 120)]
    [DataRow("C:\\escape.png", 0, 120)]
    [DataRow("actions.png", 20, 120)]
    [DataRow("actions.png", -1, 120)]
    [DataRow("actions.png", 0, 0)]
    public void InvalidAnimation_DoesNotEnterLibrary(string sheet, int index, int milliseconds)
    {
        using var f = new Files();
        f.Image("actions.png", 64, 32);
        var library = new DesktopPetLibrary(f.Library);
        Assert.Throws<InvalidDataException>(() => library.Import(f.Manifest(sheet, index, milliseconds), "character"));
        Assert.IsEmpty(library.Assets);
        Assert.IsFalse(File.Exists(Path.Combine(f.Library, "library.json")));
    }

    [STATestMethod]
    public void CorruptLibrary_PreservesOriginalIndexAndReportsWarning()
    {
        using var f = new Files();
        Directory.CreateDirectory(f.Library);
        string index = Path.Combine(f.Library, "library.json");
        File.WriteAllText(index, "{broken");
        var library = new DesktopPetLibrary(f.Library);
        Assert.IsNotNull(library.LoadWarning);
        Assert.Throws<IOException>(() => library.RestoreAll());
        Assert.Throws<IOException>(() => library.Import(f.Image("valid.png", 32, 32), "scene"));
        Assert.AreEqual("{broken", File.ReadAllText(index));
    }

    [STATestMethod]
    public void MissingImportedFile_DoesNotOverwriteLibraryOrPreventOtherAssetsLoading()
    {
        using var f = new Files();
        var library = new DesktopPetLibrary(f.Library);
        var asset = library.Import(f.Image("scene.png", 32, 32), "scene");
        string index = File.ReadAllText(Path.Combine(f.Library, "library.json"));
        File.Delete(Path.Combine(f.Library, asset.Id, "image.png"));
        var reloaded = new DesktopPetLibrary(f.Library);
        Assert.IsNotNull(reloaded.LoadWarning);
        Assert.IsEmpty(reloaded.Assets);
        Assert.Throws<IOException>(() => reloaded.RestoreAll());
        Assert.AreEqual(index, File.ReadAllText(Path.Combine(f.Library, "library.json")));
    }

    [STATestMethod]
    public void Picker_IndependentBackgroundAndPlacement_RoundTripWithoutMutatingSavedSelection()
    {
        var saved = new DesktopPetLayoutInfo { CharacterId = "black-haired", ShowScene = true, SceneId = "tiananmen",
            CharacterScale = .8, CharacterShiftX = -80, CharacterShiftY = 5, IsVisible = true };
        var picker = new DesktopPetPicker(saved.CharacterId, true, true, saved);
        picker.Characters.SelectedValue = "vpet";
        Assert.AreEqual("tiananmen", picker.SelectedSceneId, "换角色不覆盖用户选定的背景。");
        Assert.AreEqual("black-haired", saved.CharacterId, "预览不修改桌面选择。");
        picker.Scenes.SelectedValue = "tree-grass";
        picker.Characters.SelectedValue = "pink-haired";
        Assert.AreEqual("tree-grass", picker.SelectedSceneId);
        picker.CharacterScale.Value = 1.25;
        picker.CopyCompositionTo(saved);
        var roundTrip = JsonSerializer.Deserialize<DesktopPetLayoutInfo>(JsonSerializer.Serialize(saved))!;
        Assert.AreEqual("tree-grass", roundTrip.SceneId);
        Assert.AreEqual(1.25, roundTrip.CharacterScale);
        Assert.AreEqual(-80, roundTrip.CharacterShiftX);
    }

    [STATestMethod]
    public void Composition_RenderAndHitTestingUseTheSameMovedCharacterBounds()
    {
        var pet = new DesktopPetWidget();
        using var source = new OffscreenSource(pet);
        pet.SetCharacter("black-haired"); pet.SetSceneEnabled(true); pet.SetActive(true);
        pet.SetComposition("tiananmen", .65, -150, -30);
        pet.Measure(new Size(360, 360)); pet.Arrange(new Rect(0, 0, 360, 360));
        var bounds = pet.CharacterBounds;
        Assert.IsTrue(bounds.Width < 168 && bounds.X < 80);
        for (int y = 0; y < 360; y += 7)
            for (int x = 0; x < 360; x += 7)
            {
                var point = new Point(x, y);
                var frame = pet.CurrentVPetFrame;
                bool hit = bounds.Contains(point) && point.X < bounds.Right && point.Y < bounds.Bottom &&
                    frame.Alpha[(int)((y - bounds.Y) * frame.Image.PixelHeight / bounds.Height) * frame.Image.PixelWidth +
                        (int)((x - bounds.X) * frame.Image.PixelWidth / bounds.Width)] > 32;
                Assert.AreEqual(hit, pet.ContainsCharacterPoint(point));
            }
        pet.SetComposition("tiananmen", double.NaN, double.PositiveInfinity, -9999);
        Assert.IsTrue(double.IsFinite(pet.CharacterBounds.X));
        Assert.IsTrue(pet.CharacterBounds.Top >= 0 && pet.CharacterBounds.Bottom <= 360);
        pet.SetActive(false);
    }

    [STATestMethod]
    public void Picker_MissingBackgroundCannotBeReenabledByCharacterDefault()
    {
        using var f = new Files();
        var original = DesktopPetLibrary.Shared;
        var shared = typeof(DesktopPetLibrary).GetField("_shared",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var library = new DesktopPetLibrary(f.Library);
        shared.SetValue(null, library);
        try
        {
            foreach (var scene in DesktopPetScene.Scenes) library.SetRemoved(scene.Id, true);
            var picker = new DesktopPetPicker("black-haired", true, false);
            picker.Characters.SelectedValue = "pink-haired";
            Assert.AreEqual("tree-grass", picker.SelectedSceneId, "隐藏只影响素材库，保留原来的搭配。");
            Assert.IsFalse(picker.SceneSelected, "隐藏的默认背景不得被换角色操作自动开启。");
            Assert.AreEqual(0, picker.Scenes.Items.Count);
        }
        finally { shared.SetValue(null, original); }
    }

    [STATestMethod]
    public void Picker_RendersWidePreviewAndKeepsNarrowApplyButtonAccessible()
    {
        var picker = new DesktopPetPicker("black-haired", true, true,
            new DesktopPetLayoutInfo { SceneId = "tiananmen", CharacterShiftX = -55, CharacterScale = .95 });
        using var source = new OffscreenSource(picker);
        picker.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        picker.Measure(new Size(920, 690)); picker.Arrange(new Rect(0, 0, 920, 690)); picker.UpdateLayout();
        Assert.AreEqual(Visibility.Visible, picker.CompositionPanel.Visibility);
        Assert.IsTrue(picker.PreviewStage.ActualWidth > 0);
        string directory = Path.Combine(TestProjectFiles.Root, "artifacts", "material-library-qa");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap(920, 690, 96, 96, PixelFormats.Pbgra32); bitmap.Render(picker);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(Path.Combine(directory, "picker.png"))) encoder.Save(output);
        picker.Measure(new Size(410, 360)); picker.Arrange(new Rect(0, 0, 410, 360)); picker.UpdateLayout();
        Assert.AreEqual(Visibility.Collapsed, picker.CompositionPanel.Visibility);
        Assert.IsTrue(picker.ApplyButton.TranslatePoint(new Point(0, picker.ApplyButton.ActualHeight), picker).Y <= 360);
        picker.PreviewView.IsChecked = true;
        picker.PreviewView.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        picker.UpdateLayout();
        Assert.AreEqual(Visibility.Visible, picker.CompositionPanel.Visibility, "窄窗口也要能访问预览和参数。");
        Assert.AreEqual(Visibility.Collapsed, picker.LibraryPanel.Visibility);
        Assert.IsTrue(picker.CompositionScroll.ScrollableHeight > 0);
        picker.CompositionScroll.ScrollToBottom(); picker.UpdateLayout();
        var restorePoint = picker.ResetPlacementButton.TranslatePoint(new Point(0, 0), picker.CompositionScroll);
        Assert.IsTrue(restorePoint.Y >= 0 && restorePoint.Y < picker.CompositionScroll.ActualHeight);
        var narrow = new RenderTargetBitmap(410, 360, 96, 96, PixelFormats.Pbgra32); narrow.Render(picker);
        var narrowEncoder = new PngBitmapEncoder(); narrowEncoder.Frames.Add(BitmapFrame.Create(narrow));
        using (var output = File.Create(Path.Combine(directory, "picker-narrow.png"))) narrowEncoder.Save(output);
        picker.Measure(new Size(920, 360)); picker.Arrange(new Rect(0, 0, 920, 360)); picker.UpdateLayout();
        Assert.IsTrue(picker.CompositionScroll.ScrollableHeight > 0, "宽而矮的窗口必须能滚到参数和重置入口。");
        picker.CompositionScroll.ScrollToBottom(); picker.UpdateLayout();
        restorePoint = picker.ResetPlacementButton.TranslatePoint(new Point(0, 0), picker.CompositionScroll);
        Assert.IsTrue(restorePoint.Y >= 0 && restorePoint.Y < picker.CompositionScroll.ActualHeight);
        picker.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }

    [STATestMethod]
    public void Picker_SearchAndLibraryFiltersPreserveDraft_AndHiddenItemCanBeRestoredAlone()
    {
        using var f = new Files();
        var original = DesktopPetLibrary.Shared;
        var shared = typeof(DesktopPetLibrary).GetField("_shared",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var library = new DesktopPetLibrary(f.Library);
        shared.SetValue(null, library);
        try
        {
            var picker = new DesktopPetPicker("black-haired", true, true,
                new DesktopPetLayoutInfo { SceneId = "tiananmen" });
            var search = (System.Windows.Controls.TextBox)picker.FindName("SearchBox");
            Assert.IsNotNull(search, "作坊应提供名称搜索。");
            search.Text = "找不到的名字";
            Assert.AreEqual(0, picker.Characters.Items.Count);
            Assert.AreEqual("black-haired", picker.SelectedCharacterId);
            Assert.AreEqual("tiananmen", picker.SelectedSceneId);
            Assert.IsTrue(picker.ShowOnDesktop && picker.SceneSelected);
            search.Text = "";
            var filters = (System.Windows.Controls.ComboBox)picker.FindName("LibraryFilter");
            filters.SelectedIndex = 1;
            Assert.AreEqual(0, picker.Characters.Items.Count);
            Assert.AreEqual("black-haired", picker.SelectedCharacterId);
            filters.SelectedIndex = 0;
            var card = (DesktopPetPicker.Card)picker.Characters.SelectedItem;
            var menuMethod = typeof(DesktopPetPicker).GetMethod("CreateCardMenu",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert.IsNotNull(menuMethod, "每张卡片应提供独立操作菜单。");
            var menu = (System.Windows.Controls.ContextMenu)menuMethod.Invoke(picker, [card])!;
            ((System.Windows.Controls.MenuItem)menu.Items.Cast<object>().Single(i => i is System.Windows.Controls.MenuItem { Header: "隐藏素材" }))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Assert.IsTrue(library.IsRemoved("black-haired"));
            Assert.AreEqual("black-haired", picker.SelectedCharacterId, "隐藏素材不应偷偷替换桌面草稿。");
            library.SetRemoved("vpet", true);
            filters.SelectedIndex = 2;
            picker.Characters.SelectedValue = "black-haired";
            card = (DesktopPetPicker.Card)picker.Characters.SelectedItem;
            menu = (System.Windows.Controls.ContextMenu)menuMethod.Invoke(picker, [card])!;
            ((System.Windows.Controls.MenuItem)menu.Items.Cast<object>().Single(i => i is System.Windows.Controls.MenuItem { Header: "恢复到素材库" }))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Assert.IsFalse(library.IsRemoved("black-haired"));
            Assert.IsTrue(library.IsRemoved("vpet"), "逐项恢复不能顺带恢复其它素材。");
            Assert.AreEqual("black-haired", picker.SelectedCharacterId);
        }
        finally { shared.SetValue(null, original); }
    }

    [STATestMethod]
    public void Picker_CardActionsTargetTheirOwnCard_AndRestoringDraftKeepsLibraryChanges()
    {
        using var f = new Files();
        var original = DesktopPetLibrary.Shared;
        var shared = typeof(DesktopPetLibrary).GetField("_shared",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var library = new DesktopPetLibrary(f.Library);
        library.SetDisplayName("vpet", "小萝");
        shared.SetValue(null, library);
        try
        {
            var saved = new DesktopPetLayoutInfo { CharacterId = "black-haired", SceneId = "tiananmen",
                ShowScene = true, IsVisible = true, CharacterScale = .8, CharacterShiftX = -12 };
            var picker = new DesktopPetPicker(saved.CharacterId, true, true, saved);
            Assert.IsFalse(picker.HasUnappliedChanges);
            var target = picker.Characters.Items.Cast<DesktopPetPicker.Card>().Single(c => c.Id == "vpet");
            Assert.AreEqual(target, DesktopPetPicker.KeyboardCard(picker.Characters,
                new System.Windows.Controls.Button { DataContext = target }), "F2应作用于键盘所在卡片。");
            ClickMenu(picker.CreateCardMenu(target), "收藏素材");
            Assert.IsTrue(library.IsFavorite("vpet"));
            Assert.IsFalse(library.IsFavorite("black-haired"));
            Assert.AreEqual("black-haired", picker.SelectedCharacterId);
            Assert.IsFalse(picker.HasUnappliedChanges, "收藏不属于桌面搭配更改。");
            picker.LibraryFilter.SelectedIndex = 1;
            Assert.AreEqual(1, picker.Characters.Items.Count);
            picker.Characters.SelectedValue = "vpet";
            picker.CharacterScale.Value = 1.3;
            picker.CharacterX.Value = 56;
            picker.ShowPet.IsChecked = false;
            Assert.IsTrue(picker.HasUnappliedChanges);
            Assert.AreEqual("搭配尚未应用", picker.DraftStatus.Text);
            Assert.AreEqual(.8, saved.CharacterScale);
            Assert.AreEqual("black-haired", saved.CharacterId);
            picker.RestoreCompositionButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.AreEqual("black-haired", picker.SelectedCharacterId);
            Assert.AreEqual("tiananmen", picker.SelectedSceneId);
            Assert.AreEqual(.8, picker.CharacterScale.Value);
            Assert.AreEqual(-12, picker.CharacterX.Value);
            Assert.IsTrue(picker.ShowOnDesktop && picker.SceneSelected);
            Assert.IsFalse(picker.HasUnappliedChanges);
            Assert.IsTrue(library.IsFavorite("vpet"));
            Assert.AreEqual("小萝", library.GetDisplayName("vpet"));
            ClickMenu(picker.CreateCardMenu(target), "恢复原名");
            Assert.AreEqual("萝莉斯", library.GetDisplayName("vpet"));
            Assert.AreEqual("萝莉斯", ((DesktopPetPicker.Card)picker.Characters.Items[0]).Name);
            Assert.IsTrue(library.IsFavorite("vpet"));
            picker.SearchBox.Text = "萝莉斯";
            Assert.AreEqual(1, picker.Characters.Items.Count);
        }
        finally { shared.SetValue(null, original); }
    }

    [STATestMethod]
    public void Picker_SearchRecoveryAndPlacementReadouts_KeepDraftAndExplainDisabledControls()
    {
        var picker = new DesktopPetPicker("black-haired", true, true,
            new DesktopPetLayoutInfo { SceneId = "tiananmen", CharacterScale = .8, CharacterShiftX = -24, CharacterShiftY = 16 });
        using var source = new OffscreenSource(picker);
        picker.Measure(new Size(920, 690)); picker.Arrange(new Rect(0, 0, 920, 690)); picker.UpdateLayout();
        var clear = picker.FindName("ClearSearchButton") as System.Windows.Controls.Button;
        Assert.IsNotNull(clear, "搜索应提供一键清空入口。");
        Assert.AreEqual(Visibility.Collapsed, clear.Visibility);
        picker.SearchBox.Text = "找不到";
        Assert.AreEqual(Visibility.Visible, clear.Visibility);
        Assert.AreEqual(0, picker.Characters.Items.Count);
        Assert.AreEqual("角色 · 0", picker.CharactersTab.Header);
        Assert.AreEqual("背景 · 0", picker.ScenesTab.Header);
        clear.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.AreEqual("", picker.SearchBox.Text);
        Assert.AreEqual("black-haired", picker.SelectedCharacterId);
        Assert.IsFalse(picker.HasUnappliedChanges);
        var scale = (System.Windows.Controls.TextBlock)picker.FindName("ScaleReadout");
        var horizontal = (System.Windows.Controls.TextBlock)picker.FindName("HorizontalReadout");
        var vertical = (System.Windows.Controls.TextBlock)picker.FindName("VerticalReadout");
        Assert.AreEqual("80%", scale.Text);
        StringAssert.Contains(horizontal.Text, "左 24");
        StringAssert.Contains(vertical.Text, "下 16");
        picker.CharacterScale.Value = 1.25; picker.CharacterX.Value = 32; picker.CharacterY.Value = -20;
        Assert.AreEqual("125%", scale.Text);
        StringAssert.Contains(horizontal.Text, "右 32");
        StringAssert.Contains(vertical.Text, "上 20");
        picker.ShowScene.IsChecked = false;
        var hint = (System.Windows.Controls.TextBlock)picker.FindName("PlacementHint");
        Assert.AreEqual(Visibility.Visible, hint.Visibility);
        StringAssert.Contains(hint.Text, "开启背景");
        picker.ShowScene.IsChecked = true;
        Assert.AreEqual(Visibility.Collapsed, hint.Visibility);
        picker.ResetPlacementButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.AreEqual("100%", scale.Text);
        Assert.AreEqual("默认", horizontal.Text, "零偏移代表角色原始锚点，不代表场景几何中心。");
        Assert.AreEqual("默认", vertical.Text);
    }

    [STATestMethod]
    public void Picker_DirtyStatusRemainsVisibleFromNarrowLibrary_AndCanRestoreWithoutSwitchingViews()
    {
        var picker = new DesktopPetPicker("black-haired", true, true, new DesktopPetLayoutInfo { SceneId = "tiananmen" });
        using var source = new OffscreenSource(picker);
        picker.Measure(new Size(410, 360)); picker.Arrange(new Rect(0, 0, 410, 360)); picker.UpdateLayout();
        picker.Characters.SelectedValue = "vpet"; picker.UpdateLayout();
        Assert.IsTrue(picker.DraftStatus.IsVisible, "未应用提示不能藏在窄窗的另一页。");
        Assert.IsTrue(picker.RestoreCompositionButton.IsVisible, "恢复原搭配应能直接从素材页操作。");
        Assert.IsTrue(picker.RestoreCompositionButton.IsEnabled);
        var point = picker.RestoreCompositionButton.TranslatePoint(new Point(0, picker.RestoreCompositionButton.ActualHeight), picker);
        Assert.IsTrue(point.Y <= 360);
        picker.RestoreCompositionButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.AreEqual("black-haired", picker.SelectedCharacterId);
        Assert.IsFalse(picker.HasUnappliedChanges);
    }

    [STATestMethod]
    public void Picker_MetadataActionsKeepScrollPosition_AndUnchangedDoneDoesNotApply()
    {
        var library = DesktopPetLibrary.Shared;
        bool favorite = library.IsFavorite("sunset-duo");
        var picker = new DesktopPetPicker("pixelpaws", true, false);
        using var source = new OffscreenSource(picker);
        try
        {
            picker.Measure(new Size(600, 550)); picker.Arrange(new Rect(0, 0, 600, 550)); picker.UpdateLayout();
            var scroll = Descendants<System.Windows.Controls.ScrollViewer>(picker.Characters).First();
            scroll.ScrollToBottom(); picker.UpdateLayout();
            double before = scroll.VerticalOffset;
            Assert.IsTrue(before > 0);
            var card = picker.Characters.Items.Cast<DesktopPetPicker.Card>().Single(c => c.Id == "sunset-duo");
            ClickMenu(picker.CreateCardMenu(card), card.FavoriteHint);
            picker.UpdateLayout();
            Assert.AreEqual(before, scroll.VerticalOffset, 1, "收藏操作不应把当前浏览位置带回顶部。");
            Assert.IsFalse(picker.HasUnappliedChanges);
            int applied = 0, closed = 0;
            picker.ApplyRequested += () => applied++;
            picker.CancelRequested += () => closed++;
            picker.ApplyButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.AreEqual(0, applied, "仅管理素材后点击完成，无需再保存未改变的桌面布局。");
            Assert.AreEqual(1, closed);
        }
        finally { library.SetFavorite("sunset-duo", favorite); }
    }

    [STATestMethod]
    public void Picker_HiddenCardRestoresDirectly_WithoutChangingDraftOrOtherHiddenItems()
    {
        var library = DesktopPetLibrary.Shared;
        bool vpetHidden = library.IsRemoved("vpet"), pinkHidden = library.IsRemoved("pink-haired");
        try
        {
            library.SetRemoved("vpet", true); library.SetRemoved("pink-haired", true);
            var picker = new DesktopPetPicker("black-haired", true, false);
            using var source = new OffscreenSource(picker);
            picker.LibraryFilter.SelectedIndex = 2;
            picker.Measure(new Size(410, 360)); picker.Arrange(new Rect(0, 0, 410, 360)); picker.UpdateLayout();
            var restore = Descendants<System.Windows.Controls.Button>(picker.Characters).FirstOrDefault(b =>
                b.Content is "恢复" && b.DataContext is DesktopPetPicker.Card { Id: "vpet" });
            Assert.IsNotNull(restore, "隐藏卡片应直接提供恢复入口。");
            Assert.IsTrue(restore.IsVisible);
            restore.BringIntoView(); picker.UpdateLayout();
            var scroll = Descendants<System.Windows.Controls.ScrollViewer>(picker.Characters).First();
            var top = restore.TranslatePoint(new Point(0, 0), scroll);
            Assert.IsTrue(top.Y >= 0 && top.Y + restore.ActualHeight <= scroll.ActualHeight);
            var image = new RenderTargetBitmap(410, 360, 96, 96, PixelFormats.Pbgra32); image.Render(picker);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            string directory = Path.Combine(TestProjectFiles.Root, "artifacts", "material-library-qa");
            Directory.CreateDirectory(directory);
            using (var output = File.Create(Path.Combine(directory, "picker-hidden.png"))) encoder.Save(output);
            restore.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.IsFalse(library.IsRemoved("vpet"));
            Assert.IsTrue(library.IsRemoved("pink-haired"));
            Assert.AreEqual("black-haired", picker.SelectedCharacterId);
            Assert.IsFalse(picker.HasUnappliedChanges);
        }
        finally { library.SetRemoved("vpet", vpetHidden); library.SetRemoved("pink-haired", pinkHidden); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void ClickMenu(System.Windows.Controls.ContextMenu menu, string header) =>
        menu.Items.OfType<System.Windows.Controls.MenuItem>().Single(i => Equals(i.Header, header))
            .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));

    private sealed class Files : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "DesktopPetLibraryTests-" + Guid.NewGuid().ToString("N"));
        internal string Source => Path.Combine(_root, "source");
        internal string Library => Path.Combine(_root, "library");
        internal Files() { Directory.CreateDirectory(Source); }
        internal string Image(string name, int width, int height)
        {
            var pixels = Enumerable.Repeat((byte)255, width * height * 4).ToArray();
            var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            string path = Path.Combine(Source, name); using var stream = File.Create(path); encoder.Save(stream); return path;
        }
        internal string Manifest(string sheet, int index, int milliseconds)
        {
            string path = Path.Combine(Source, "manifest.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { cellSize = 32, columns = 2,
                clips = new { idle = new { sheet, frames = new[] { new { index, milliseconds } } } } }));
            return path;
        }
        public void Dispose() { Directory.Delete(_root, true); }
    }
    private sealed class OffscreenSource : PresentationSource, IDisposable
    {
        private readonly Visual _root;
        private bool _disposed;
        internal OffscreenSource(Visual root) { _root = root; AddSource(); RootChanged(null, root); }
        public override Visual RootVisual { get => _root; set => throw new NotSupportedException(); }
        public override bool IsDisposed => _disposed;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
        public void Dispose() { RootChanged(_root, null); RemoveSource(); _disposed = true; }
    }
}
