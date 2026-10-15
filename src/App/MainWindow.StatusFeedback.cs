namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private void SetStatusDetails(string text, string title = "桌面位置")
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(new TextBlock
            {
                Text = text, FontSize = 12.5, LineHeight = 20, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("SecondaryTextBrush")
            });
            StatusText.ToolTip = CreateStatusDetailToolTip(content);
        }

        private ToolTip CreateStatusDetailToolTip(UIElement content)
        {
            var tooltip = new ToolTip
            {
                Style = (Style)FindResource("StatusDetailToolTipStyle"),
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                PlacementTarget = ControlPanelDragHandle, VerticalOffset = 6, Content = content
            };
            void UpdateWidth()
            {
                double workWidth = _desktopGeometry.FindMonitorForPoint(GetControlPanelPosition()).WorkArea.Width;
                double width = Math.Max(1, Math.Min(320, workWidth - 24));
                tooltip.Width = width;
                tooltip.MaxWidth = width;
            }
            UpdateWidth();
            tooltip.Opened += (_, _) => UpdateWidth();
            return tooltip;
        }
    }
}
