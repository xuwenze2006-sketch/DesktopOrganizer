namespace DesktopOrganizer;

public partial class DesktopPetPicker
{
    private DesktopPetLibrary.Preset? SelectedPreset => PresetsCombo.SelectedItem as DesktopPetLibrary.Preset;
    private DesktopPetLibrary.Composition CaptureComposition() => new(SelectedCharacterId, SelectedSceneId, SceneSelected,
        CharacterScale.Value, CharacterX.Value, CharacterY.Value);

    private void RefreshPresets(string? selectId = null)
    {
        string? id = selectId ?? SelectedPreset?.Id;
        PresetsCombo.ItemsSource = DesktopPetLibrary.Shared.Presets;
        PresetsCombo.SelectedValue = id;
        UpdatePresetState();
    }

    private void UpdatePresetState()
    {
        if (!_ready) return;
        PresetControls.IsEnabled = _importCancellation is null;
        PresetPlaceholder.Visibility = SelectedPreset is null ? Visibility.Visible : Visibility.Collapsed;
        LoadPresetButton.IsEnabled = PresetMenuButton.IsEnabled = SelectedPreset is not null;
        PresetNotice.Text = SelectedPreset is not { } preset ? "保存喜欢的搭配，之后可随时载入。"
            : preset.Composition == CaptureComposition() ? "试选与所选搭配一致；保存不会应用到桌面。"
            : "点「载入」试选；点 ⋯ 可用当前试选更新它。";
    }

    private void Preset_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePresetState();

    internal bool TryLoadPreset(string id)
    {
        if (_importCancellation is not null) return false;
        var preset = DesktopPetLibrary.Shared.Presets.FirstOrDefault(p => p.Id == id);
        string? problem = preset is null ? "这个搭配已不存在，请重新选择。" : DesktopPetLibrary.Shared.GetCompositionProblem(preset.Composition);
        if (problem is not null) { PresetNotice.Text = problem; return false; }
        var composition = preset!.Composition;
        _refreshing = true;
        try
        {
            _characterId = composition.CharacterId; _sceneId = composition.SceneId;
            _sceneChosen = true;
            ShowScene.IsChecked = composition.ShowScene;
            CharacterScale.Value = composition.Scale; CharacterX.Value = composition.X; CharacterY.Value = composition.Y;
        }
        finally { _refreshing = false; }
        RefreshCards();
        RefreshPresets(id);
        PresetNotice.Text = "已载入“" + preset.Name + "”；点击应用才更新桌面。";
        return true;
    }

    private bool ChangePreset(Action change, string message, Func<string?>? selection = null)
    {
        if (_importCancellation is not null) return false;
        try
        {
            change();
            RefreshPresets(selection?.Invoke());
            PresetNotice.Text = message;
            return true;
        }
        catch (Exception ex) when (DesktopPetLibrary.IsAssetError(ex))
        {
            PresetNotice.Text = "未能保存：" + ex.Message;
            return false;
        }
    }

    internal bool TrySavePreset(string name)
    {
        string? id = null;
        return ChangePreset(() => id = DesktopPetLibrary.Shared.AddPreset(name, CaptureComposition()).Id,
            "搭配已保存，可随时载入；桌面保持不变。", () => id);
    }

    internal bool TryUpdatePreset(string id) => ChangePreset(() => DesktopPetLibrary.Shared.UpdatePreset(id, CaptureComposition()),
        "已用当前试选更新搭配；桌面保持不变。", () => id);

    private void LoadPreset_Click(object sender, RoutedEventArgs e)
    { if (SelectedPreset is { } preset) TryLoadPreset(preset.Id); }

    private string? AskPresetName(string title, string initial)
    {
        var dialog = new SimpleInputDialog("搭配名称（1–40 字）：", initial) { Title = title, Owner = Window.GetWindow(this) };
        dialog.InputBox.MaxLength = 40;
        return dialog.ShowDialog() == true ? dialog.ResultText : null;
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_importCancellation is not null) return;
        string initial = DesktopPetCatalog.Find(SelectedCharacterId).Name;
        if (initial.Length > 30) initial = initial[..30];
        initial += "的搭配";
        string unique = initial;
        for (int suffix = 2; DesktopPetLibrary.Shared.Presets.Any(p => string.Equals(p.Name, unique, StringComparison.OrdinalIgnoreCase)); suffix++)
            unique = initial + " " + suffix;
        if (AskPresetName("保存新搭配", unique) is string name) TrySavePreset(name);
    }

    internal ContextMenu CreatePresetMenu(DesktopPetLibrary.Preset preset)
    {
        var menu = new ContextMenu();
        void Add(string label, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = label, IsEnabled = enabled && _importCancellation is null };
            item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        Add("更新所选搭配…", () =>
        {
            if (MessageBox.Show(Window.GetWindow(this), "用当前试选替换“" + preset.Name + "”的角色、背景、大小和位置？",
                "更新搭配", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
                TryUpdatePreset(preset.Id);
        }, preset.Composition != CaptureComposition());
        Add("改名…", () =>
        {
            if (AskPresetName("修改搭配名称", preset.Name) is string name)
                ChangePreset(() => DesktopPetLibrary.Shared.RenamePreset(preset.Id, name), "搭配名称已保存。", () => preset.Id);
        });
        menu.Items.Add(new Separator());
        Add("删除搭配…", () =>
        {
            if (MessageBox.Show(Window.GetWindow(this), "删除“" + preset.Name + "”？只删除这条搭配记录，素材与当前桌面保持不变。",
                "删除搭配", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
                ChangePreset(() => DesktopPetLibrary.Shared.DeletePreset(preset.Id), "搭配记录已删除；素材和当前试选保持不变。");
        });
        return menu;
    }

    private void PresetMenu_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPreset is not { } preset) return;
        var menu = CreatePresetMenu(preset);
        menu.PlacementTarget = PresetMenuButton; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true;
    }
}
