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

        internal DesktopPetPicker(string characterId, bool visible, bool scene)
        {
            InitializeComponent();
            Characters.ItemsSource = DesktopPetCatalog.Characters.Select(character => new Card(character.Id,
                character.Name, character.Description, DesktopPetCatalog.Preview(character.Id))).ToArray();
            Characters.SelectedValuePath = nameof(Card.Id);
            Characters.SelectedValue = DesktopPetWidget.NormalizeCharacterId(characterId);
            ShowPet.IsChecked = visible;
            ShowScene.IsChecked = scene;
        }

        private void Characters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedDescription is null) return;
            bool selected = Characters.SelectedItem is Card;
            ApplyButton.IsEnabled = PreviewButton.IsEnabled = selected;
            SelectedDescription.Text = Characters.SelectedItem is Card card
                ? "已选择：" + card.Name + (card.Id == DesktopPetCatalog.SunsetDuoId
                    ? " · 两人一起移动，轻点点头回应" : " · 轻点互动，拖动换位置") : "请选择桌面伙伴";
            if (ShowScene is not null)
            {
                ShowScene.Content = "搭配" + DesktopPetScene.NameFor(SelectedCharacterId);
                if (SelectedCharacterId == DesktopPetCatalog.SunsetDuoId) ShowScene.IsChecked = true;
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (Characters.SelectedItem is Card) ApplyRequested?.Invoke();
        }
        private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (Characters.SelectedItem is not Card) return;
            var pet = new DesktopPetWidget();
            pet.SetCharacter(SelectedCharacterId);
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
            Add(duo ? "点头招呼" : "挥手", pet.Wave);
            Add(duo ? "轻点回应" : "摸头", pet.ReactToTouch);
            Add(duo ? "一起移动" : "提起", () => pet.SetDragging(true));
            Add(duo ? "放稳" : "松手", () => pet.SetDragging(false));
            Add("休息", pet.Rest);
            Add("唤醒", pet.WakeUp);
            DockPanel.SetDock(actions, Dock.Bottom);
            panel.Children.Add(actions);
            panel.Children.Add(stage);
            var preview = new Window { Title = DesktopPetCatalog.Find(SelectedCharacterId).Name + " · 动作预览",
                Content = panel, Width = 480, Height = 500, MinWidth = 380, MinHeight = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this) };
            preview.Loaded += (_, _) => pet.SetActive(true);
            preview.Closed += (_, _) => pet.SetActive(false);
            preview.ShowDialog();
        }
    }
}
