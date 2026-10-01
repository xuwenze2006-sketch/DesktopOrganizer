namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private bool TryExpandGroupWithoutOverlap(GroupInfo group)
        {
            if (!group.IsCollapsed) return true;
            var prospective = new GroupInfo {
                X=group.X,Y=group.Y,Width=group.Width,Height=group.Height,
                ItemNames=group.ItemNames,IsSizeLocked=group.IsSizeLocked,
                UseUniformTrackWidth=group.UseUniformTrackWidth,DesktopRole=group.DesktopRole };
            AutoFitGroup(prospective,clampPosition:false);
            var expanded=new Rect(group.X,group.Y,prospective.Width,Math.Max(GroupMinHeight,prospective.Height));
            var obstacles=GetGroupPlacementObstacles(group);
            if (_lightDesktopSection != null && TryGetElementBoundsOnCanvas(_lightDesktopSection,out Rect section))
                obstacles.Add(section);
            if (!GroupExpansionPlacementPolicy.TryFind(expanded,
                _desktopGeometry.Monitors.Select(monitor=>monitor.WorkArea),
                obstacles,out Point position)) return false;
            // Auto-fit can grow after moving to a larger monitor. Validate that final size there.
            prospective.X=position.X;prospective.Y=position.Y;
            DesktopMonitorRegion target=GetMonitorForItemRect(GetGroupBounds(prospective));
            AutoFitGroup(prospective,clampPosition:false);
            expanded=new Rect(position.X,position.Y,prospective.Width,Math.Max(GroupMinHeight,prospective.Height));
            if (!GroupExpansionPlacementPolicy.TryFind(expanded,[target.WorkArea],obstacles,out position)) return false;
            group.X=position.X;group.Y=position.Y;group.Width=prospective.Width;group.Height=prospective.Height;
            group.IsCollapsed=false;
            return true;
        }

        private void ReportGroupExpansionNoSpace(string name) =>
            ReportGroupExpansionFailure($"空间不足，未展开“{name}”");

        private void ReportGroupExpansionFailure(string reason)
        {
            string summary=reason.StartsWith("已展开",StringComparison.Ordinal) ? "部分未展开 · 详情" : "空间不足 · 详情";
            var details=new StackPanel();
            details.Children.Add(new TextBlock {
                Text="暂时无法展开",FontSize=14,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8) });
            details.Children.Add(new TextBlock {
                Text=reason,FontSize=12.5,LineHeight=19,TextWrapping=TextWrapping.Wrap });
            details.Children.Add(new TextBlock {
                Text="可以这样做",FontSize=11.5,FontWeight=FontWeights.SemiBold,
                Foreground=(Brush)FindResource("SecondaryTextBrush"),Margin=new Thickness(0,14,0,6) });
            details.Children.Add(new TextBlock {
                Text="先腾出空白区域，再展开。\n临时查看：退出编辑模式，确认程序未暂停，再悬停折叠分组标题。",
                FontSize=12.5,LineHeight=20,TextWrapping=TextWrapping.Wrap });

            // Once another status replaces this summary, show that status instead of stale failure details.
            var detailStyle=new Style(typeof(StackPanel));
            detailStyle.Setters.Add(new Setter(VisibilityProperty,Visibility.Collapsed));
            var showDetails=new DataTrigger {
                Binding=new System.Windows.Data.Binding(nameof(TextBlock.Text)) {Source=StatusText},Value=summary };
            showDetails.Setters.Add(new Setter(VisibilityProperty,Visibility.Visible));
            detailStyle.Triggers.Add(showDetails);
            details.Style=detailStyle;
            var fallback=new TextBlock {FontSize=12.5,LineHeight=19,TextWrapping=TextWrapping.Wrap};
            fallback.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(nameof(TextBlock.Text)) {Source=StatusText});
            var fallbackStyle=new Style(typeof(TextBlock));
            var hideFallback=new DataTrigger {
                Binding=new System.Windows.Data.Binding(nameof(TextBlock.Text)) {Source=StatusText},Value=summary };
            hideFallback.Setters.Add(new Setter(VisibilityProperty,Visibility.Collapsed));
            fallbackStyle.Triggers.Add(hideFallback);
            fallback.Style=fallbackStyle;
            var content=new Grid();content.Children.Add(details);content.Children.Add(fallback);
            double workWidth=_desktopGeometry.FindMonitorForPoint(GetControlPanelPosition()).WorkArea.Width;
            double width=Math.Max(1,Math.Min(320,workWidth-24));
            var tooltip=new ToolTip {
                Style=(Style)FindResource("StatusDetailToolTipStyle"),Width=width,MaxWidth=width,
                Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom,
                PlacementTarget=ControlPanelDragHandle,VerticalOffset=6,Content=content };
            tooltip.Opened += (_, _) =>
            {
                double currentWorkWidth=_desktopGeometry.FindMonitorForPoint(GetControlPanelPosition()).WorkArea.Width;
                double currentWidth=Math.Max(1,Math.Min(320,currentWorkWidth-24));
                tooltip.Width=currentWidth;tooltip.MaxWidth=currentWidth;
            };
            StatusText.Text=summary;
            StatusText.ToolTip=tooltip;
        }

    }
}
