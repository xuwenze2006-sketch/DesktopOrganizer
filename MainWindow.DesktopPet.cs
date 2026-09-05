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
            DesktopPetToggle.IsChecked = _appLayout.DesktopPet.IsVisible;
            ApplyDesktopPetPosition();
            UpdateDesktopPetVisibility();
        }

        private void DesktopPetToggle_Click(object sender, RoutedEventArgs e) =>
            SetDesktopPetVisible(DesktopPetToggle.IsChecked == true);

        private void HideDesktopPet_Click(object sender, RoutedEventArgs e) => SetDesktopPetVisible(false);

        private void SetDesktopPetVisible(bool visible)
        {
            _appLayout.DesktopPet.IsVisible = visible;
            DesktopPetToggle.IsChecked = visible;
            ApplyDesktopPetPosition();
            UpdateDesktopPetVisibility();
            SaveLayout();
            StatusText.Text = !visible ? "小猫已隐藏，可在系统页重新开启"
                : _organizerPaused ? "小猫已开启，继续整理后显示"
                : _isSafeModeActive ? "小猫已开启，关闭安全模式后显示"
                : "小猫已出现：轻点互动，拖动换位置";
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
                _appLayout.DesktopPet.X ?? primary.Right - DesktopPetWidget.WidgetSize - 32,
                _appLayout.DesktopPet.Y ?? primary.Bottom - DesktopPetWidget.WidgetSize - 112,
                updateLayout: true);
        }

        private void SetDesktopPetPosition(double x, double y, bool updateLayout)
        {
            Point position = ClampRectToUsableDesktop(x, y, DesktopPetWidget.WidgetSize, DesktopPetWidget.WidgetSize);
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
            Point mapped = MapItemPosition(pet.X.Value, pet.Y.Value,
                DesktopPetWidget.WidgetSize, DesktopPetWidget.WidgetSize, source, target);
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
                DesktopPet.SetDragging(true);
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
