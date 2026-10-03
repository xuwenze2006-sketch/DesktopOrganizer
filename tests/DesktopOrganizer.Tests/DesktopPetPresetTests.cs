using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopPetPresetTests
{
    private static DesktopPetLibrary.Composition Morning => new("black-haired", "tiananmen", true, .85, -70, 18);
    private static DesktopPetLibrary.Composition Evening => new("pink-haired", "tree-grass", false, 1.2, 32, -48);

    [STATestMethod]
    public void Presets_RoundTripIndependentCompositions_ThroughMetadataChangesAndDeletion()
    {
        using var f = new Fixture();
        var first = f.Library.AddPreset("  清晨  ", Morning);
        var second = f.Library.AddPreset("夜晚", Evening);
        f.Library.SetDisplayName("black-haired", "我的伙伴");
        f.Library.SetRemoved("black-haired", true);
        f.Library.SetFavorite("tiananmen", true);
        f.Library.RenamePreset(first.Id, "上班");
        f.Library.UpdatePreset(second.Id, Evening with { Scale = 1.4 });
        var restarted = new DesktopPetLibrary(f.Root);
        Assert.IsNull(restarted.LoadWarning);
        Assert.AreEqual(Morning, restarted.Presets.Single(p => p.Id == first.Id).Composition);
        Assert.AreEqual("上班", restarted.Presets.Single(p => p.Id == first.Id).Name);
        Assert.AreEqual(1.4, restarted.Presets.Single(p => p.Id == second.Id).Composition.Scale);
        Assert.IsNull(restarted.GetCompositionProblem(Morning)); // 隐藏不等于素材缺失。
        restarted.DeletePreset(first.Id);
        restarted = new DesktopPetLibrary(f.Root);
        Assert.AreEqual(second.Id, restarted.Presets.Single().Id);
        Assert.AreEqual("我的伙伴", restarted.GetDisplayName("black-haired"));
        Assert.IsTrue(restarted.IsRemoved("black-haired") && restarted.IsFavorite("tiananmen"));
    }

    [STATestMethod]
    public void Presets_FailedWritesPreserveDiskAndPublishedSnapshot()
    {
        using var f = new Fixture();
        var preset = f.Library.AddPreset("清晨", Morning);
        string path = Path.Combine(f.Root, "library.json");
        string before = File.ReadAllText(path);
        var snapshot = f.Library.ReadSnapshot();
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            foreach (Action change in new Action[] { () => f.Library.AddPreset("夜晚", Evening),
                () => f.Library.UpdatePreset(preset.Id, Evening), () => f.Library.RenamePreset(preset.Id, "新名字"),
                () => f.Library.DeletePreset(preset.Id) })
            {
                try { change(); Assert.Fail("写入失败时不得发布新状态。"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        Assert.AreSame(snapshot, f.Library.ReadSnapshot());
        Assert.AreEqual(before, File.ReadAllText(path));
        Assert.AreEqual(preset, f.Library.Presets.Single());
    }

    [STATestMethod]
    public void Presets_ValidateNamesReferencesAndGeometry_AndProtectCorruptIndexes()
    {
        using var f = new Fixture();
        string path = Path.Combine(f.Root, "library.json");
        File.WriteAllText(path, "{\"Assets\":[],\"Removed\":[]}");
        var library = new DesktopPetLibrary(f.Root);
        Assert.IsNull(library.LoadWarning);
        Assert.AreEqual(0, library.Presets.Count);
        var preset = library.AddPreset("Home", Morning);
        foreach (string invalid in new[] { " ", new string('名', 41), "换\n行", " home " })
            Assert.Throws<ArgumentException>(() => library.AddPreset(invalid, Morning));
        library.RenamePreset(preset.Id, "HOME"); // 自己可只改变大小写。
        foreach (var invalid in new[] { Morning with { Scale = double.NaN }, Morning with { X = 241 },
            Morning with { SceneId = null }, Morning with { CharacterId = "tiananmen" },
            Morning with { SceneId = "vpet" }, Morning with { CharacterId = "user-" + Guid.NewGuid().ToString("N") } })
            Assert.Throws<ArgumentException>(() => library.UpdatePreset(preset.Id, invalid));
        string before = File.ReadAllText(path);
        foreach (string invalid in new[] { "{\"Presets\":null}", "{\"Presets\":[null]}",
            JsonSerializer.Serialize(new { Presets = new[] { preset, preset } }),
            JsonSerializer.Serialize(new { Presets = new[] { preset with { Composition = Morning with { Scale = 0 } } } }) })
        {
            File.WriteAllText(path, invalid);
            var broken = new DesktopPetLibrary(f.Root);
            Assert.IsNotNull(broken.LoadWarning);
            Assert.Throws<IOException>(() => broken.AddPreset("新搭配", Morning));
            Assert.AreEqual(invalid, File.ReadAllText(path));
        }
        File.WriteAllText(path, before);
        Assert.AreEqual(Morning, new DesktopPetLibrary(f.Root).Presets.Single().Composition);
    }

    [STATestMethod]
    public void Picker_PresetsLoadOnlyIntoDraft_AndExplicitUpdateDoesNotChangeOtherPresets()
    {
        using var f = new Fixture();
        var first = f.Library.AddPreset("清晨", Morning);
        var second = f.Library.AddPreset("夜晚", Evening);
        var saved = new DesktopPetLayoutInfo { CharacterId = "vpet", SceneId = "tree-grass", CharacterScale = 1 };
        var picker = new DesktopPetPicker("vpet", false, false, saved);
        int applied = 0;
        picker.ApplyRequested += () => applied++;
        var selector = (ComboBox)picker.FindName("PresetsCombo");
        selector.SelectedValue = first.Id;
        Assert.AreEqual("vpet", picker.SelectedCharacterId, "选择记录不能悄悄载入。");
        Click(picker, "LoadPresetButton");
        Assert.AreEqual("black-haired", picker.SelectedCharacterId);
        Assert.IsTrue(picker.HasUnappliedChanges);
        Assert.IsFalse(picker.ShowOnDesktop, "搭配不改变全局显示开关。");
        var draft = new DesktopPetLayoutInfo(); picker.CopyCompositionTo(draft);
        Assert.AreEqual("tiananmen", draft.SceneId);
        Assert.AreEqual(.85, draft.CharacterScale);
        Assert.AreEqual(-70d, draft.CharacterShiftX);
        Assert.AreEqual(18d, draft.CharacterShiftY);
        Assert.AreEqual("vpet", saved.CharacterId);
        Assert.AreEqual(0, applied);
        ((Slider)picker.FindName("CharacterScale")).Value = 1.1;
        Assert.AreEqual(Morning, f.Library.Presets.Single(p => p.Id == first.Id).Composition);
        Assert.IsTrue(picker.TryUpdatePreset(first.Id));
        Assert.AreEqual(1.1, f.Library.Presets.Single(p => p.Id == first.Id).Composition.Scale);
        Assert.AreEqual(Evening, f.Library.Presets.Single(p => p.Id == second.Id).Composition);
        Assert.IsTrue(picker.TrySavePreset("副本"));
        Assert.AreEqual(3, f.Library.Presets.Count);
        Click(picker, "RestoreCompositionButton");
        Assert.AreEqual("vpet", picker.SelectedCharacterId);
        Assert.IsFalse(picker.HasUnappliedChanges);
        Assert.AreEqual(3, f.Library.Presets.Count);
        Assert.AreEqual(0, applied);
    }

    [STATestMethod]
    public void Picker_HiddenPresetAssetsRemainUsable_MissingAssetsCannotSilentlyFallback()
    {
        using var f = new Fixture();
        var valid = f.Library.AddPreset("隐藏也能用", Morning);
        f.Library.SetRemoved("black-haired", true);
        f.Library.SetRemoved("tiananmen", true);
        var picker = new DesktopPetPicker("vpet", true, false);
        Assert.IsTrue(picker.TryLoadPreset(valid.Id));
        Assert.AreEqual("black-haired", picker.SelectedCharacterId);
        var missing = valid with { Id = Guid.NewGuid().ToString("N"), Name = "素材已缺失",
            Composition = Morning with { CharacterId = "user-" + Guid.NewGuid().ToString("N") } };
        File.WriteAllText(Path.Combine(f.Root, "library.json"), JsonSerializer.Serialize(new { Presets = new[] { missing } }));
        f.Replace(new DesktopPetLibrary(f.Root));
        Assert.IsNull(f.Library.LoadWarning);
        Assert.IsFalse(picker.TryLoadPreset(missing.Id));
        Assert.AreEqual("black-haired", picker.SelectedCharacterId);
        StringAssert.Contains(((TextBlock)picker.FindName("PresetNotice")).Text, "缺失");
        Assert.AreEqual(missing, f.Library.Presets.Single());
    }

    [STATestMethod]
    public void Picker_PresetControlsRemainAccessibleWideAndNarrow_AndReportSaveFailure()
    {
        using var f = new Fixture();
        var preset = f.Library.AddPreset("下班后的庭院", Morning);
        var picker = new DesktopPetPicker("black-haired", true, true);
        Assert.IsTrue(picker.TryLoadPreset(preset.Id));
        Assert.IsFalse(picker.CreatePresetMenu(preset).Items.OfType<MenuItem>().First().IsEnabled,
            "试选相同时不能误覆盖所选搭配。");
        using var source = new OffscreenSource(picker);
        picker.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        try
        {
            foreach (var size in new[] { new Size(920, 690), new Size(410, 360) })
            {
                picker.Measure(size); picker.Arrange(new Rect(size)); picker.UpdateLayout();
                ((RadioButton)picker.FindName("PreviewView")).IsChecked = true;
                ((RadioButton)picker.FindName("PreviewView")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                ((ScrollViewer)picker.FindName("CompositionScroll")).ScrollToTop(); picker.UpdateLayout();
                foreach (string name in new[] { "PresetsCombo", "LoadPresetButton", "SavePresetButton", "PresetMenuButton", "ApplyButton" })
                {
                    var control = (FrameworkElement)picker.FindName(name);
                    Assert.IsTrue(control.IsVisible, name);
                    var bounds = control.TransformToAncestor(picker).TransformBounds(new Rect(control.RenderSize));
                    Assert.IsTrue(bounds.Left >= 0 && bounds.Right <= size.Width && bounds.Top >= 0 && bounds.Bottom <= size.Height, name + ": " + bounds);
                }
                var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(picker);
                string dir = Path.Combine(TestProjectFiles.Root, "artifacts", "material-library-qa");
                Directory.CreateDirectory(dir);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(dir, size.Width > 500 ? "presets-wide.png" : "presets-narrow.png"));
                encoder.Save(output);
            }
            using var locked = new FileStream(Path.Combine(f.Root, "library.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.IsFalse(picker.TrySavePreset("保存失败"));
            StringAssert.Contains(((TextBlock)picker.FindName("PresetNotice")).Text, "未能保存");
            Assert.AreEqual(preset, f.Library.Presets.Single());
        }
        finally { picker.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
    }

    private static void Click(DesktopPetPicker picker, string name) =>
        ((Button)picker.FindName(name)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "PetPresets-" + Guid.NewGuid().ToString("N"));
        internal DesktopPetLibrary Library { get; private set; }
        private readonly DesktopPetLibrary _original = DesktopPetLibrary.Shared;
        private static readonly FieldInfo SharedField = typeof(DesktopPetLibrary).GetField("_shared", BindingFlags.Static | BindingFlags.NonPublic)!;
        internal Fixture() { Directory.CreateDirectory(Root); Library = new DesktopPetLibrary(Root); SharedField.SetValue(null, Library); }
        internal void Replace(DesktopPetLibrary library) { Library = library; SharedField.SetValue(null, library); }
        public void Dispose() { SharedField.SetValue(null, _original); Directory.Delete(Root, true); }
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
