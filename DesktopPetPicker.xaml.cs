namespace DesktopOrganizer
{
    public partial class DesktopPetPicker : UserControl
    {
        internal sealed record Card(string Id, string Name, string Description, BitmapSource Preview);
        internal event Action? ApplyRequested;
        internal event Action? CancelRequested;
        internal string SelectedCharacterId => (Characters.SelectedItem as Card)?.Id ?? DesktopPetWidget.CatCharacterId;
        internal bool ShowOnDesktop => ShowPet.IsChecked == true;
        internal bool SceneSelected => ShowScene.IsChecked == true;
        internal string? SelectedSceneId => (Scenes.SelectedItem as Card)?.Id;
        private readonly DesktopPetWidget _preview = new();
        private bool _ready;
        private bool _sceneChosen;
        private bool _refreshing;
        private CancellationTokenSource? _importCancellation;
        internal void CancelImport() => _importCancellation?.Cancel();

        internal DesktopPetPicker(string characterId, bool visible, bool scene, DesktopPetLayoutInfo? composition = null)
        {
            InitializeComponent();
            Characters.SelectedValuePath = nameof(Card.Id);
            Scenes.SelectedValuePath = nameof(Card.Id);
            ReloadLibrary(characterId, composition?.SceneId ?? DesktopPetScene.DefaultId(characterId));
            ShowPet.IsChecked = visible;
            ShowScene.IsChecked = scene;
            CharacterScale.Value = composition?.CharacterScale ?? 1;
            CharacterX.Value = composition?.CharacterShiftX ?? 0;
            CharacterY.Value = composition?.CharacterShiftY ?? 0;
            _sceneChosen = composition?.SceneId is not null;
            _preview.HorizontalAlignment = HorizontalAlignment.Center;
            _preview.VerticalAlignment = VerticalAlignment.Bottom;
            PreviewStage.Children.Add(_preview);
            _ready = true;
            Loaded += (_, _) => { _preview.SetActive(true); UpdatePreview(); };
            Unloaded += (_, _) => { _preview.SetActive(false); CancelImport(); };
            UpdatePreview();
            if (DesktopPetLibrary.Shared.LoadWarning is string warning) SelectedDescription.Text = warning;
        }

        private void ReloadLibrary(string? character, string? scene)
        {
            _refreshing = true;
            try
            {
                Characters.ItemsSource = DesktopPetCatalog.Characters.Where(c => !DesktopPetLibrary.Shared.IsRemoved(c.Id))
                    .Select(c => new Card(c.Id, c.Name, c.Description, DesktopPetCatalog.Preview(c.Id))).ToArray();
                Scenes.ItemsSource = DesktopPetScene.Scenes.Where(s => !DesktopPetLibrary.Shared.IsRemoved(s.Id))
                    .Select(s => new Card(s.Id, s.Name, "可与任意角色搭配", DesktopPetScene.GetBackground(s.Id).Image)).ToArray();
                Characters.SelectedValue = character;
                if (Characters.SelectedItem is null && Characters.Items.Count > 0) Characters.SelectedIndex = 0;
                if (Characters.Items.Count == 0) ShowPet.IsChecked = false;
                Scenes.SelectedValue = scene;
                if (Scenes.SelectedItem is null && Scenes.Items.Count > 0) Scenes.SelectedIndex = 0;
                if (Scenes.SelectedItem is null) ShowScene.IsChecked = false;
            }
            finally { _refreshing = false; }
            UpdatePreview();
        }

        private void Characters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedDescription is null || _refreshing) return;
            bool selected = Characters.SelectedItem is Card;
            ApplyButton.IsEnabled = _importCancellation is null && (selected || Characters.Items.Count == 0);
            PreviewButton.IsEnabled = selected;
            SelectedDescription.Text = Characters.SelectedItem is Card card
                ? "已选择：" + card.Name + (card.Id == DesktopPetCatalog.SunsetDuoId
                    ? " · 轻点相视，待机时也会自然互动" : " · 轻点互动，拖动换位置") : "请选择桌面伙伴";
            if (ShowScene is not null && !_sceneChosen)
            {
                _refreshing = true;
                Scenes.SelectedValue = DesktopPetScene.DefaultId(SelectedCharacterId);
                _refreshing = false;
                ShowScene.Content = "搭配" + DesktopPetScene.NameFor(SelectedCharacterId);
                if (SelectedCharacterId is "pink-haired" or DesktopPetCatalog.SunsetDuoId) ShowScene.IsChecked = true;
            }
            UpdatePreview();
        }

        private void Scenes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshing || !_ready) return;
            _sceneChosen = true;
            ShowScene.IsChecked = Scenes.SelectedItem is Card;
            UpdatePreview();
        }
        private void UpdatePreview()
        {
            if (!_ready || _refreshing) return;
            bool selected = Characters.SelectedItem is Card;
            ApplyButton.IsEnabled = _importCancellation is null && (selected || Characters.Items.Count == 0);
            PreviewButton.IsEnabled = selected;
            _preview.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            ShowPet.IsEnabled = Characters.Items.Count > 0;
            if (Characters.Items.Count == 0) ShowPet.IsChecked = false;
            _preview.SetCharacter(SelectedCharacterId);
            _preview.SetComposition(SelectedSceneId, CharacterScale.Value, CharacterX.Value, CharacterY.Value);
            if (SelectedSceneId is null) ShowScene.IsChecked = false;
            _preview.SetSceneEnabled(SceneSelected && SelectedSceneId is not null);
            PlacementControls.IsEnabled = _preview.SceneEnabled;
            ShowScene.IsEnabled = Scenes.SelectedItem is Card;
            ShowScene.Content = "搭配" + ((Scenes.SelectedItem as Card)?.Name ?? "背景");
            SelectedDescription.Text = selected ? "已选择：" + (Characters.SelectedItem as Card)!.Name +
                (_preview.SceneEnabled ? " ＋ " + (Scenes.SelectedItem as Card)!.Name : " · 无背景") : "素材库为空，可添加或恢复角色";
        }
        internal void CopyCompositionTo(DesktopPetLayoutInfo layout)
        {
            layout.SceneId = SelectedSceneId;
            layout.CharacterScale = CharacterScale.Value;
            layout.CharacterShiftX = CharacterX.Value;
            layout.CharacterShiftY = CharacterY.Value;
        }
        private void SceneVisibility_Changed(object sender, RoutedEventArgs e) => UpdatePreview();
        private void Placement_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdatePreview();
        private void ResetPlacement_Click(object sender, RoutedEventArgs e)
        { CharacterScale.Value = 1; CharacterX.Value = CharacterY.Value = 0; }
        private void Wave_Click(object sender, RoutedEventArgs e) => _preview.Wave();
        private void Touch_Click(object sender, RoutedEventArgs e) => _preview.ReactToTouch();
        private void Rest_Click(object sender, RoutedEventArgs e) => _preview.Rest();
        private void Wake_Click(object sender, RoutedEventArgs e) => _preview.WakeUp();
        private void Picker_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (CompositionPanel is null) return;
            bool wide = e.NewSize.Width >= 720;
            CompositionPanel.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
            PreviewColumn.Width = new GridLength(wide ? 374 : 0);
        }
        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            if (_importCancellation is not null) return;
            bool scene = LibraryTabs.SelectedIndex == 1;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = scene ? "添加背景图片" : "添加角色图片或动画",
                Filter = scene ? "PNG 背景|*.png" : "角色素材（PNG 或动画清单）|*.png;*.json|PNG 静态角色|*.png|动画清单|*.json"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            using var cancellation = new CancellationTokenSource();
            _importCancellation = cancellation;
            LibraryActions.IsEnabled = LibraryTabs.IsEnabled = ApplyButton.IsEnabled = false;
            SelectedDescription.Text = "正在导入素材…关闭窗口可取消尚未完成的导入。";
            string? resultMessage = null;
            try
            {
                var asset = await DesktopPetLibrary.Shared.ImportAsync(dialog.FileName, scene ? "scene" : "character", cancellation.Token);
                if (!IsLoaded) return;
                ReloadLibrary(scene ? SelectedCharacterId : asset.Id, scene ? asset.Id : SelectedSceneId);
                if (scene) { _sceneChosen = true; ShowScene.IsChecked = true; }
                UpdatePreview();
                resultMessage = "已添加“" + asset.Name + "”。素材库已保存；点击应用更新桌面搭配。";
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { resultMessage = "已取消导入。"; }
            catch (Exception ex) when (DesktopPetLibrary.IsAssetError(ex))
            { if (IsLoaded) MessageBox.Show(Window.GetWindow(this), "无法添加素材：" + ex.Message, "添加素材"); }
            finally
            {
                _importCancellation = null;
                LibraryActions.IsEnabled = LibraryTabs.IsEnabled = true;
                if (IsLoaded) { UpdatePreview(); if (resultMessage is not null) SelectedDescription.Text = resultMessage; }
            }
        }
        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (_importCancellation is not null) return;
            Card? card = (LibraryTabs.SelectedIndex == 1 ? Scenes.SelectedItem : Characters.SelectedItem) as Card;
            if (card is null) return;
            try
            {
                DesktopPetLibrary.Shared.SetRemoved(card.Id, true);
                ReloadLibrary(SelectedCharacterId, SelectedSceneId);
                SelectedDescription.Text = "已从素材库移除“" + card.Name + "”，可恢复。原文件保留；应用后更新桌面搭配。";
            }
            catch (Exception ex) when (DesktopPetLibrary.IsAssetError(ex))
            { MessageBox.Show(Window.GetWindow(this), "无法移除素材：" + ex.Message, "移除素材"); }
        }
        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (_importCancellation is not null) return;
            try { DesktopPetLibrary.Shared.RestoreAll(); ReloadLibrary(SelectedCharacterId, SelectedSceneId); }
            catch (Exception ex) when (DesktopPetLibrary.IsAssetError(ex))
            { MessageBox.Show(Window.GetWindow(this), "无法恢复素材：" + ex.Message, "恢复素材"); }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (_importCancellation is null && (Characters.SelectedItem is Card || Characters.Items.Count == 0)) ApplyRequested?.Invoke();
        }
        private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (Characters.SelectedItem is not Card) return;
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
