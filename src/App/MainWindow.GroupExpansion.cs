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
            string message=$"{reason}；请先腾出空白区域。临时查看内容需退出编辑模式、确认程序未暂停，再悬停折叠分组标题";
            StatusText.Text=message;
            StatusText.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source=StatusText });
        }
    }
}