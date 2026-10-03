namespace DesktopOrganizer
{
    public partial class DesktopPetPicker : UserControl
    {
        internal sealed record Card(string Id, string Name, string Description, BitmapSource Preview,
            bool Favorite = false, bool Hidden = false, bool InUse = false)
        {
            public string FavoriteGlyph => Favorite ? "★" : "☆";
            public string FavoriteHint => Favorite ? "取消收藏" : "收藏素材";
            public string Badge => InUse ? (Hidden ? "桌面使用 · 已隐藏" : "桌面正在使用") : Hidden ? "已隐藏" : "";
        }
        private sealed record Draft(string? Character, string? Scene, bool Visible, bool ShowScene, double Scale, double X, double Y);
        internal event Action? ApplyRequested;
        internal event Action? CancelRequested;
        internal event Action? LibraryChanged;
        internal string SelectedCharacterId => _characterId ?? DesktopPetWidget.CatCharacterId;
        internal bool ShowOnDesktop => ShowPet.IsChecked == true;
        internal bool SceneSelected => ShowScene.IsChecked == true;
        internal string? SelectedSceneId => _sceneId;
        internal bool HasUnappliedChanges => _initial is not null && CaptureDraft() != _initial;
        private readonly DesktopPetWidget _preview = new();
        private string? _characterId;
        private string? _sceneId;
        private Draft? _initial;
        private bool _initialSceneChosen;
        private bool _ready;
        private bool _sceneChosen;
        private bool _refreshing;
        private CancellationTokenSource? _importCancellation;
        internal void CancelImport() => _importCancellation?.Cancel();

        internal DesktopPetPicker(string characterId, bool visible, bool scene, DesktopPetLayoutInfo? composition = null)
        {
            InitializeComponent();
            Characters.SelectedValuePath = Scenes.SelectedValuePath = nameof(Card.Id);
            _characterId = DesktopPetCatalog.Find(characterId).Id;
            _sceneId = DesktopPetScene.ResolveId(composition?.SceneId, _characterId);
            ShowPet.IsChecked = visible;
            ShowScene.IsChecked = scene;
            CharacterScale.Value = composition?.CharacterScale ?? 1;
            CharacterX.Value = composition?.CharacterShiftX ?? 0;
            CharacterY.Value = composition?.CharacterShiftY ?? 0;
            _sceneChosen = _initialSceneChosen = composition?.SceneId is not null;
            _initial = CaptureDraft();
            _preview.HorizontalAlignment = HorizontalAlignment.Center;
            _preview.VerticalAlignment = VerticalAlignment.Bottom;
            PreviewStage.Children.Add(_preview);
            _ready = true;
            RefreshPresets();
            RefreshCards();
            Loaded += (_, _) => { _preview.SetActive(true); UpdatePreview(); };
            Unloaded += (_, _) => { _preview.SetActive(false); CancelImport(); };
            UpdatePreview();
            if (DesktopPetLibrary.Shared.LoadWarning is string warning) LibraryNotice.Text = warning;
        }

        private Draft CaptureDraft() => new(_characterId, _sceneId, ShowOnDesktop, SceneSelected,
            CharacterScale.Value, CharacterX.Value, CharacterY.Value);

        private void RefreshCards()
        {
            if (!_ready) return;
            var library = DesktopPetLibrary.Shared;
            string query = SearchBox.Text.Trim();
            int filter = LibraryFilter.SelectedIndex;
            bool Matches(string id, string name) =>
                (filter == 2 ? library.IsRemoved(id) : !library.IsRemoved(id)) &&
                (filter != 1 || library.IsFavorite(id)) &&
                (name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 library.GetOriginalName(id).Contains(query, StringComparison.OrdinalIgnoreCase));
            Card MakeCard(string id, string name, string description, BitmapSource preview, bool inUse) =>
                new(id, name, description, preview, library.IsFavorite(id), library.IsRemoved(id), inUse);
            _refreshing = true;
            try
            {
                Characters.ItemsSource = DesktopPetCatalog.Characters.Where(c => Matches(c.Id, c.Name))
                    .Select(c => MakeCard(c.Id, c.Name, c.Description, DesktopPetCatalog.Preview(c.Id),
                        _initial is { Visible: true } && _initial.Character == c.Id)).ToArray();
                Scenes.ItemsSource = DesktopPetScene.Scenes.Where(s => Matches(s.Id, s.Name))
                    .Select(s => MakeCard(s.Id, s.Name, "可与任意角色搭配", DesktopPetScene.GetBackground(s.Id).Image,
                        _initial is { Visible: true, ShowScene: true } && _initial.Scene == s.Id)).ToArray();
                // 列表只是视图。筛选或隐藏当前素材，不能清空或替换搭配草稿。
                Characters.SelectedValue = _characterId;
                Scenes.SelectedValue = _sceneId;
            }
            finally { _refreshing = false; }
            UpdateEmptyState();
            UpdatePreview();
        }
        private void Search_Changed(object sender, TextChangedEventArgs e) => RefreshCards();
        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            LibraryNotice.Text = LibraryFilter.SelectedIndex == 2 ? "已隐藏素材仅供管理；点 ⋯ 恢复后可重新试选"
                : "点卡片试选，点 ⋯ 或按 F2 管理素材";
            RefreshCards();
        }
        private void LibraryTab_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_ready && ReferenceEquals(e.Source, LibraryTabs)) UpdateEmptyState();
        }
        private void UpdateEmptyState()
        {
            CharactersTab.Header = $"角色 · {Characters.Items.Count}";
            ScenesTab.Header = $"背景 · {Scenes.Items.Count}";
            ClearSearchButton.Visibility = SearchBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            var list = LibraryTabs.SelectedIndex == 1 ? Scenes : Characters;
            EmptyLibrary.Visibility = list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyLibrary.Text = SearchBox.Text.Trim().Length > 0 ? "没有匹配的素材\n试试原名，或清空搜索"
                : LibraryFilter.SelectedIndex == 2 ? "没有隐藏的素材" : LibraryFilter.SelectedIndex == 1
                ? "还没有收藏\n点击卡片上的 ☆ 即可收藏" : "素材库为空\n可添加素材，或在「已隐藏」中恢复";
        }
        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            SearchBox.Focus();
        }
        private void Characters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || _refreshing || Characters.SelectedItem is not Card card || card.Hidden) return;
            _characterId = card.Id;
            if (!_sceneChosen)
            {
                string defaultId = DesktopPetScene.DefaultId(card.Id);
                if (!DesktopPetLibrary.Shared.IsRemoved(defaultId))
                {
                    _sceneId = defaultId;
                    _refreshing = true;
                    try { Scenes.SelectedValue = _sceneId; }
                    finally { _refreshing = false; }
                    if (card.Id is "pink-haired" or DesktopPetCatalog.SunsetDuoId) ShowScene.IsChecked = true;
                }
            }
            UpdatePreview();
        }
        private void Scenes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || _refreshing || Scenes.SelectedItem is not Card card || card.Hidden) return;
            _sceneId = card.Id;
            _sceneChosen = true;
            ShowScene.IsChecked = true;
            UpdatePreview();
        }
        private void UpdatePreview()
        {
            if (!_ready || _refreshing) return;
            UpdatePresetState();
            bool selected = _characterId is not null;
            ApplyButton.IsEnabled = _importCancellation is null;
            PreviewButton.IsEnabled = selected && _importCancellation is null;
            _preview.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            ShowPet.IsEnabled = selected;
            _preview.SetCharacter(SelectedCharacterId);
            _preview.SetComposition(_sceneId, CharacterScale.Value, CharacterX.Value, CharacterY.Value);
            _preview.SetSceneEnabled(SceneSelected && _sceneId is not null);
            PlacementControls.IsEnabled = _preview.SceneEnabled;
            PlacementHint.Visibility = _preview.SceneEnabled ? Visibility.Collapsed : Visibility.Visible;
            ScaleReadout.Text = CharacterScale.Value.ToString("0%", System.Globalization.CultureInfo.InvariantCulture);
            HorizontalReadout.Text = OffsetReadout(CharacterX.Value, "左", "右");
            VerticalReadout.Text = OffsetReadout(CharacterY.Value, "上", "下");
            ShowScene.IsEnabled = _sceneId is not null;
            string character = DesktopPetCatalog.Find(SelectedCharacterId).Name;
            string scene = _sceneId is null ? "背景" : DesktopPetLibrary.Shared.GetDisplayName(_sceneId);
            ShowScene.Content = "显示背景";
            ShowScene.ToolTip = scene;
            SelectedDescription.Text = selected ? "正在试选：" + character +
                (_preview.SceneEnabled ? " ＋ " + scene : " · 无背景") : "请选择桌面伙伴";
            SelectedDescription.ToolTip = SelectedDescription.Text;
            DraftStatus.Text = HasUnappliedChanges ? "搭配尚未应用" : "当前桌面搭配";
            ApplyButton.Content = HasUnappliedChanges ? "应用到桌面" : "完成";
            RestoreCompositionButton.IsEnabled = HasUnappliedChanges;
            DesktopStatus.Visibility = HasUnappliedChanges && ActualHeight >= 500 ? Visibility.Visible : Visibility.Collapsed;
            if (_initial is not null)
            {
                DesktopStatus.Text = (_initial.Visible ? "桌面正在使用：" : "桌宠已关闭：") +
                    DesktopPetCatalog.Find(_initial.Character).Name +
                    (_initial.ShowScene && _initial.Scene is not null ? " ＋ " + DesktopPetLibrary.Shared.GetDisplayName(_initial.Scene) : "");
                DesktopStatus.ToolTip = DesktopStatus.Text;
            }
        }
        private static string OffsetReadout(double value, string negative, string positive) => Math.Abs(value) < .5 ? "默认"
            : (value < 0 ? negative : positive) + " " + Math.Abs(value).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        internal void CopyCompositionTo(DesktopPetLayoutInfo layout)
        {
            layout.SceneId = _sceneId;
            layout.CharacterScale = CharacterScale.Value;
            layout.CharacterShiftX = CharacterX.Value;
            layout.CharacterShiftY = CharacterY.Value;
        }
        private void Visibility_Changed(object sender, RoutedEventArgs e) => UpdatePreview();
        private void Placement_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdatePreview();
        private void ResetPlacement_Click(object sender, RoutedEventArgs e)
        { CharacterScale.Value = 1; CharacterX.Value = CharacterY.Value = 0; }
        private void RestoreComposition_Click(object sender, RoutedEventArgs e)
        {
            if (_initial is null) return;
            _refreshing = true;
            try
            {
                _characterId = _initial.Character; _sceneId = _initial.Scene;
                ShowPet.IsChecked = _initial.Visible; ShowScene.IsChecked = _initial.ShowScene;
                CharacterScale.Value = _initial.Scale; CharacterX.Value = _initial.X; CharacterY.Value = _initial.Y;
                _sceneChosen = _initialSceneChosen;
            }
            finally { _refreshing = false; }
            RefreshCards();
        }
        private void Wave_Click(object sender, RoutedEventArgs e) => _preview.Wave();
        private void Touch_Click(object sender, RoutedEventArgs e) => _preview.ReactToTouch();
        private void Rest_Click(object sender, RoutedEventArgs e) => _preview.Rest();
        private void Wake_Click(object sender, RoutedEventArgs e) => _preview.WakeUp();

        private void Picker_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout();
        private void NarrowView_Click(object sender, RoutedEventArgs e) => UpdateResponsiveLayout();
        private void UpdateResponsiveLayout()
        {
            if (!_ready) return;
            bool wide = ActualWidth >= 720;
            bool compact = ActualHeight < 500;
            WorkshopTitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            IntroText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            DesktopStatus.Visibility = !compact && HasUnappliedChanges ? Visibility.Visible : Visibility.Collapsed;
            NarrowNavigation.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
            bool preview = wide || PreviewView.IsChecked == true;
            LibraryPanel.Visibility = wide || !preview ? Visibility.Visible : Visibility.Collapsed;
            CompositionPanel.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
            PreviewColumn.Width = new GridLength(wide ? 350 : 0);
            Grid.SetColumn(CompositionPanel, wide ? 1 : 0);
            CompositionPanel.Margin = new Thickness(wide ? 14 : 0, 0, 0, 0);
            bool scrollWhole = !wide || ActualHeight < 560;
            CompositionScroll.VerticalScrollBarVisibility = scrollWhole ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            PlacementScroll.VerticalScrollBarVisibility = scrollWhole ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            CompositionContent.MinHeight = scrollWhole ? 390 : 0;
            PreviewViewbox.Height = wide ? Math.Clamp(ActualHeight - 465, 95, 230) : 150;
        }
        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (_importCancellation is not null) return;
            if (HasUnappliedChanges) ApplyRequested?.Invoke();
            else CancelRequested?.Invoke();
        }
        private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (_characterId is null) return;
            var pet = new DesktopPetWidget();
            pet.SetCharacter(SelectedCharacterId);
            pet.SetComposition(SelectedSceneId, CharacterScale.Value, CharacterX.Value, CharacterY.Value);
            pet.SetSceneEnabled(SceneSelected);
            var stage = new Grid { Height = 370, Background = new SolidColorBrush(Color.FromRgb(221, 233, 232)) };
            pet.HorizontalAlignment = HorizontalAlignment.Center;
            pet.VerticalAlignment = VerticalAlignment.Bottom;
            stage.Children.Add(pet);
            var panel = new DockPanel { Margin = new Thickness(16) };
            var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            void Add(string name, Action action)
            {
                var button = new Button { Content = name, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(3) };
                button.Click += (_, _) => action();
                actions.Children.Add(button);
            }
            bool duo = SelectedCharacterId == DesktopPetCatalog.SunsetDuoId;
            Add("挥手", pet.Wave);
            Add(duo ? "相视一笑" : "摸头", pet.ReactToTouch);
            if (duo) Add("牵手轻摆", pet.SwayHands);
            Add(duo ? "一起移动" : "提起", () => pet.SetDragging(true));
            Add(duo ? "放稳" : "松手", () => pet.SetDragging(false));
            Add("休息", pet.Rest);
            Add("唤醒", pet.WakeUp);
            DockPanel.SetDock(actions, Dock.Bottom);
            panel.Children.Add(actions);
            panel.Children.Add(stage);
            var preview = new Window { Title = DesktopPetCatalog.Find(SelectedCharacterId).Name + " · 动作预览",
                Content = panel, Width = 480, Height = duo ? 560 : 500, MinWidth = 380, MinHeight = duo ? 520 : 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this) };
            preview.Loaded += (_, _) => { _preview.SetActive(false); pet.SetActive(true); };
            preview.Closed += (_, _) => { pet.SetActive(false); _preview.SetActive(true); };
            preview.ShowDialog();
        }
    }
}
