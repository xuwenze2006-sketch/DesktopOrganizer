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
        var picker = new DesktopPetPicker("black-haired", true, false);
        picker.Scenes.ItemsSource = Array.Empty<DesktopPetPicker.Card>();
        picker.Characters.SelectedValue = "pink-haired";
        Assert.IsNull(picker.SelectedSceneId);
        Assert.IsFalse(picker.SceneSelected, "移除全部背景后，角色默认值不得重新开启已移除的背景。");
        Assert.IsFalse(picker.ShowScene.IsEnabled);
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
        picker.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }

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
