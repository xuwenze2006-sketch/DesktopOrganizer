namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private bool _isDesktopPetDragging;
        private bool _desktopPetDragMoved;
        private Point _desktopPetDragStartMouse;
        private Point _desktopPetDragStartPosition;

        private void InitializeDesktopPet()
        {
            DesktopPet.SetCharacter(_appLayout.DesktopPet.CharacterId);
            UpdateDesktopPetDescription();
            DesktopPetToggle.IsChecked = _appLayout.DesktopPet.IsVisible;
            ApplyDesktopPetPosition();
            UpdateDesktopPetVisibility();
        }

        private void DesktopPetToggle_Click(object sender, RoutedEventArgs e) =>
            SetDesktopPetVisible(DesktopPetToggle.IsChecked == true);

        private void HideDesktopPet_Click(object sender, RoutedEventArgs e) => SetDesktopPetVisible(false);

        private string DesktopPetName => DesktopPet.CharacterId == DesktopPetWidget.VPetCharacterId ? "萝莉斯" : "小猫";

        private void UpdateDesktopPetDescription()
        {
            DesktopPet.ToolTip = DesktopPet.CharacterId == DesktopPetWidget.VPetCharacterId
                ? "萝莉斯 · VPet／虚拟主播模拟器制作组；轻点摸头，拖动提起，右键切换角色或查看来源"
                : "小黑猫 · 轻点互动，拖动换位置，右键切换角色或隐藏";
            System.Windows.Automation.AutomationProperties.SetName(DesktopPet, "桌面宠物 · " + DesktopPetName);
        }

        private void DesktopPetCharacter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: string id }) SetDesktopPetCharacter(id);
        }

        private void SetDesktopPetCharacter(string id)
        {
            string normalized = DesktopPetWidget.NormalizeCharacterId(id);
            if (_appLayout.DesktopPet.CharacterId == normalized) return;
            CompleteDesktopPetDrag(commit: false);
            _appLayout.DesktopPet.CharacterId = normalized;
            DesktopPet.SetCharacter(normalized);
            ApplyDesktopPetPosition();
            UpdateDesktopPetDescription();
            UpdateDesktopPetVisibility();
            SaveLayout();
            StatusText.Text = "已选择" + DesktopPetName + (_appLayout.DesktopPet.IsVisible
                ? "；轻点互动，右键查看动作和素材来源"
                : "；打开“桌面宠物”即可显示");
        }

        private void DesktopPetMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu) return;
            foreach (MenuItem item in menu.Items.OfType<MenuItem>())
            {
                if (item.Tag is string id && (id == DesktopPetWidget.CatCharacterId || id == DesktopPetWidget.VPetCharacterId))
                    item.IsChecked = id == _appLayout.DesktopPet.CharacterId;
                if (item.Tag is "action") item.IsEnabled = DesktopPet.IsVisible && !_isDesktopPetDragging;
            }
        }

        private void DesktopPetTouch_Click(object sender, RoutedEventArgs e) => DesktopPet.ReactToTouch();
        private void DesktopPetRest_Click(object sender, RoutedEventArgs e) => DesktopPet.Rest();
        private void DesktopPetWake_Click(object sender, RoutedEventArgs e) => DesktopPet.WakeUp();

        private void DesktopPetSource_Click(object sender, RoutedEventArgs e)
        {
            using Stream stream = VPetAnimation.OpenResource("ThirdParty/VPet-ANIMATION-LICENSE.txt");
            using var reader = new StreamReader(stream);
            var panel = new DockPanel { Margin = new Thickness(20) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var source = new Button { Content = "访问 VPet 来源项目", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 12, 10, 0) };
            var close = new Button { Content = "关闭", IsCancel = true, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 12, 0, 0) };
            source.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo("https://github.com/LorisYounger/VPet") { UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                { MessageBox.Show("无法打开浏览器。可复制来源地址：https://github.com/LorisYounger/VPet", "素材来源"); }
            };
            buttons.Children.Add(source);
            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Bottom);
            panel.Children.Add(buttons);
            panel.Children.Add(new TextBox { Text = reader.ReadToEnd(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0) });
            var dialog = new Window { Title = "萝莉斯 · VPet 动画来源与授权", Width = 580, Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = panel };
            if (!_isAttachedToDesktop) dialog.Owner = this;
            close.Click += (_, _) => dialog.Close();
            dialog.ShowDialog();
        }

        private void SetDesktopPetVisible(bool visible)
        {
            _appLayout.DesktopPet.IsVisible = visible;
            DesktopPetToggle.IsChecked = visible;
            ApplyDesktopPetPosition();
            UpdateDesktopPetVisibility();
            SaveLayout();
            StatusText.Text = DesktopPetName + (!visible ? "已隐藏，可在系统页重新开启"
                : _organizerPaused ? "已开启，继续整理后显示"
                : _isSafeModeActive ? "已开启，关闭安全模式后显示"
                : "已出现：轻点互动，拖动换位置");
        }

        private void UpdateDesktopPetVisibility()
        {
            bool visible = _appLayout.DesktopPet.IsVisible && !_organizerPaused && !_isSafeModeActive && !_isClosing;
            if (!visible) CompleteDesktopPetDrag(commit: false);
            DesktopPet.SetActive(visible);
            DesktopPet.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private Point GetDesktopPetPosition() => new(DesktopPet.Margin.Left, DesktopPet.Margin.Top);

        private void ApplyDesktopPetPosition()
        {
            Rect primary = GetPrimaryWorkArea();
            SetDesktopPetPosition(
                _appLayout.DesktopPet.X ?? primary.Right - DesktopPet.Width - 32,
                _appLayout.DesktopPet.Y ?? primary.Bottom - DesktopPet.Height - 112,
                updateLayout: true);
        }

        private void SetDesktopPetPosition(double x, double y, bool updateLayout)
        {
            Point position = ClampRectToUsableDesktop(x, y, DesktopPet.Width, DesktopPet.Height);
            DesktopPet.Margin = new Thickness(position.X, position.Y, 0, 0);
            if (updateLayout)
            {
                _appLayout.DesktopPet.X = position.X;
                _appLayout.DesktopPet.Y = position.Y;
            }
        }

        private bool RemapDesktopPet(DesktopGeometry source, DesktopGeometry target)
        {
            DesktopPetLayoutInfo pet = _appLayout.DesktopPet;
            if (!pet.X.HasValue || !pet.Y.HasValue) return false;
            double size = DesktopPetWidget.GetCharacterSize(pet.CharacterId);
            Point mapped = MapItemPosition(pet.X.Value, pet.Y.Value,
                size, size, source, target);
            bool changed = Math.Abs(pet.X.Value - mapped.X) > .01 || Math.Abs(pet.Y.Value - mapped.Y) > .01;
            pet.X = mapped.X;
            pet.Y = mapped.Y;
            return changed;
        }

        private void DesktopPet_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || _organizerPaused || _isClosing) return;
            RecoverStaleInteractionState();
            _desktopPetDragStartMouse = e.GetPosition(RootGrid);
            _desktopPetDragStartPosition = GetDesktopPetPosition();
            _desktopPetDragMoved = false;
            _isDesktopPetDragging = Mouse.Capture(DesktopPet, CaptureMode.Element);
            if (_isDesktopPetDragging)
            {
                e.Handled = true;
            }
        }

        private void DesktopPet_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDesktopPetDragging || e.LeftButton != MouseButtonState.Pressed) return;
            Vector delta = e.GetPosition(RootGrid) - _desktopPetDragStartMouse;
            if (!_desktopPetDragMoved &&
                Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (!_desktopPetDragMoved) DesktopPet.SetDragging(true);
            _desktopPetDragMoved = true;
            SetDesktopPetPosition(_desktopPetDragStartPosition.X + delta.X,
                _desktopPetDragStartPosition.Y + delta.Y, updateLayout: false);
            e.Handled = true;
        }

        private void DesktopPet_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDesktopPetDragging) return;
            bool tapped = !_desktopPetDragMoved;
            CompleteDesktopPetDrag(commit: true);
            if (tapped) DesktopPet.ReactToTouch();
            e.Handled = true;
        }

        private void DesktopPet_LostMouseCapture(object sender, MouseEventArgs e) =>
            CompleteDesktopPetDrag(commit: false);

        private void CompleteDesktopPetDrag(bool commit)
        {
            if (!_isDesktopPetDragging) return;
            bool moved = _desktopPetDragMoved;
            _isDesktopPetDragging = false;
            _desktopPetDragMoved = false;
            if (!commit)
                SetDesktopPetPosition(_desktopPetDragStartPosition.X, _desktopPetDragStartPosition.Y, updateLayout: false);
            if (ReferenceEquals(Mouse.Captured, DesktopPet)) Mouse.Capture(null);
            DesktopPet.SetDragging(false);
            if (commit && moved)
            {
                Point position = GetDesktopPetPosition();
                SetDesktopPetPosition(position.X, position.Y, updateLayout: true);
                SaveLayout();
            }
        }
    }
}
