// 收起分类框的临时悬停预览；只改变当前视觉，不写入布局。
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private const int GroupNormalZIndex = 100;
        private const int GroupPeekZIndex = 450;
        private const double GroupPeekWorkAreaGap = 8;

        private readonly Dictionary<string, GroupPeekVisualRegistration> _groupPeekVisuals =
            new(StringComparer.OrdinalIgnoreCase);
        private string? _pendingGroupPeekId;
        private string? _activeGroupPeekId;
        private string? _groupPeekChildMenuGroupId;

        private void RequestGroupPeek(string groupId)
        {
            if (string.Equals(_activeGroupPeekId, groupId, StringComparison.OrdinalIgnoreCase))
            {
                _groupPeekCloseTimer.Stop();
                return;
            }

            if (!_groupPeekVisuals.TryGetValue(groupId, out GroupPeekVisualRegistration? registration) ||
                !GroupPeekPolicy.CanArm(
                    registration.Group.IsCollapsed,
                    registration.Group.ItemNames.Count,
                    _appLayout.IsEditMode,
                    _organizerPaused))
            {
                return;
            }

            _pendingGroupPeekId = groupId;
            _groupPeekOpenTimer.Stop();
            _groupPeekOpenTimer.Start();
        }

        private void ScheduleGroupPeekClose(string groupId)
        {
            if (string.Equals(_pendingGroupPeekId, groupId, StringComparison.OrdinalIgnoreCase))
            {
                _pendingGroupPeekId = null;
                _groupPeekOpenTimer.Stop();
            }

            if (!string.Equals(_activeGroupPeekId, groupId, StringComparison.OrdinalIgnoreCase) ||
                !_groupPeekVisuals.TryGetValue(groupId, out GroupPeekVisualRegistration? registration) ||
                registration.HeaderMenu.IsOpen ||
                string.Equals(
                    _groupPeekChildMenuGroupId,
                    groupId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _groupPeekCloseTimer.Stop();
            _groupPeekCloseTimer.Start();
        }

        private void GroupPeekOpenTimer_Tick(object? sender, EventArgs e)
        {
            _groupPeekOpenTimer.Stop();
            string? groupId = _pendingGroupPeekId;
            _pendingGroupPeekId = null;
            if (groupId == null ||
                !_groupPeekVisuals.TryGetValue(groupId, out GroupPeekVisualRegistration? registration) ||
                !registration.Container.IsMouseOver ||
                !GroupPeekPolicy.CanArm(
                    registration.Group.IsCollapsed,
                    registration.Group.ItemNames.Count,
                    _appLayout.IsEditMode,
                    _organizerPaused))
            {
                return;
            }

            GroupPeekPresentation presentation = GetGroupPeekPresentation(
                registration.Group,
                isPeekActive: true);
            if (presentation.VisualHeight <= GroupHeaderHeight + 1)
            {
                return;
            }

            if (_activeGroupPeekId is string activeId &&
                !activeId.Equals(groupId, StringComparison.OrdinalIgnoreCase))
            {
                CloseGroupPeek(activeId);
            }

            // 先发布 active 状态，使正文首次实现图标时使用“展开分组”加载优先级。
            _activeGroupPeekId = groupId;
            ApplyGroupPeekPresentation(registration, presentation, isPeekActive: true);
        }

        private void GroupPeekCloseTimer_Tick(object? sender, EventArgs e)
        {
            _groupPeekCloseTimer.Stop();
            string? groupId = _activeGroupPeekId;
            if (groupId == null ||
                !_groupPeekVisuals.TryGetValue(groupId, out GroupPeekVisualRegistration? registration))
            {
                return;
            }

            bool capturedWithinGroup =
                Mouse.Captured is DependencyObject captured &&
                IsDescendantOf(captured, registration.Container);
            if (registration.Container.IsMouseOver ||
                registration.HeaderMenu.IsOpen ||
                string.Equals(
                    _groupPeekChildMenuGroupId,
                    groupId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (capturedWithinGroup)
            {
                _groupPeekCloseTimer.Start();
                return;
            }

            bool relatedIconInteraction =
                _groupedIconDragSourceGroup != null &&
                ReferenceEquals(_groupedIconDragSourceGroup, registration.Group) &&
                _draggedElement != null;
            if (!relatedIconInteraction &&
                Mouse.LeftButton == MouseButtonState.Pressed &&
                _pendingIconDragElement?.Tag is IconTag pendingTag)
            {
                relatedIconInteraction = ReferenceEquals(pendingTag.Group, registration.Group);
            }

            if (relatedIconInteraction)
            {
                _groupPeekCloseTimer.Start();
                return;
            }

            CloseGroupPeek(groupId);
        }

        private void RegisterGroupPeekVisual(
            GroupInfo group,
            Border container,
            Grid outer,
            Border header,
            Border body,
            Border bodyDivider,
            VirtualizingGroupPanel itemsPanel,
            ContextMenu headerMenu)
        {
            UnregisterGroupPeekVisual(group.Id);
            var registration = new GroupPeekVisualRegistration(
                group,
                container,
                outer,
                header,
                body,
                bodyDivider,
                itemsPanel,
                headerMenu);
            _groupPeekVisuals[group.Id] = registration;
            ApplyGroupPeekVisualState(group.Id);
        }

        private void NotifyGroupPeekChildMenuOpened(string groupId)
        {
            _groupPeekChildMenuGroupId = groupId;
            if (string.Equals(_activeGroupPeekId, groupId, StringComparison.OrdinalIgnoreCase))
            {
                _groupPeekCloseTimer.Stop();
            }
        }

        private void NotifyGroupPeekChildMenuClosed(string groupId)
        {
            if (string.Equals(
                    _groupPeekChildMenuGroupId,
                    groupId,
                    StringComparison.OrdinalIgnoreCase))
            {
                _groupPeekChildMenuGroupId = null;
            }

            if (_groupPeekVisuals.TryGetValue(groupId, out GroupPeekVisualRegistration? registration) &&
                !registration.Container.IsMouseOver)
            {
                ScheduleGroupPeekClose(groupId);
            }
        }

        private void UnregisterGroupPeekVisual(string groupId)
        {
            if (string.Equals(_pendingGroupPeekId, groupId, StringComparison.OrdinalIgnoreCase))
            {
                _pendingGroupPeekId = null;
                _groupPeekOpenTimer.Stop();
            }

            if (string.Equals(_activeGroupPeekId, groupId, StringComparison.OrdinalIgnoreCase))
            {
                _activeGroupPeekId = null;
                _groupPeekCloseTimer.Stop();
            }

            if (string.Equals(
                    _groupPeekChildMenuGroupId,
                    groupId,
                    StringComparison.OrdinalIgnoreCase))
            {
                _groupPeekChildMenuGroupId = null;
            }

            _groupPeekVisuals.Remove(groupId);
        }

        private void CloseGroupPeek(string groupId)
        {
            if (!string.Equals(_activeGroupPeekId, groupId, StringComparison.OrdinalIgnoreCase) ||
                !_groupPeekVisuals.TryGetValue(groupId, out GroupPeekVisualRegistration? registration))
            {
                return;
            }

            _activeGroupPeekId = null;
            ApplyGroupPeekVisualState(groupId);
            registration.ItemsPanel.ReleaseRealizedContainers();
        }

        private void StopGroupPeek()
        {
            _pendingGroupPeekId = null;
            _groupPeekChildMenuGroupId = null;
            _groupPeekOpenTimer.Stop();
            _groupPeekCloseTimer.Stop();
            if (_activeGroupPeekId is string activeId)
            {
                CloseGroupPeek(activeId);
            }
        }

        private bool IsGroupPeekActive(GroupInfo group) =>
            _activeGroupPeekId != null &&
            _activeGroupPeekId.Equals(group.Id, StringComparison.OrdinalIgnoreCase) &&
            _groupPeekVisuals.TryGetValue(group.Id, out GroupPeekVisualRegistration? registration) &&
            ReferenceEquals(registration.Group, group);

        private void RefreshGroupPeekVisualAfterRebuild(
            GroupInfo group,
            FrameworkElement groupVisual)
        {
            if (_groupPeekVisuals.TryGetValue(group.Id, out GroupPeekVisualRegistration? registration) &&
                ReferenceEquals(registration.Container, groupVisual))
            {
                ApplyGroupPeekVisualState(group.Id);
                return;
            }

            Panel.SetZIndex(groupVisual, GroupNormalZIndex);
        }

        private void ApplyGroupPeekVisualState(string groupId)
        {
            if (!_groupPeekVisuals.TryGetValue(groupId, out GroupPeekVisualRegistration? registration))
            {
                return;
            }

            bool isPeekActive = IsGroupPeekActive(registration.Group);
            ApplyGroupPeekPresentation(
                registration,
                GetGroupPeekPresentation(registration.Group, isPeekActive),
                isPeekActive);
        }

        private GroupPeekPresentation GetGroupPeekPresentation(
            GroupInfo group,
            bool isPeekActive)
        {
            DesktopMonitorRegion monitor = GetMonitorForItemRect(
                new Rect(group.X, group.Y, group.Width, GroupHeaderHeight));
            return GroupPeekPolicy.GetPresentation(
                group.IsCollapsed,
                isPeekActive,
                Math.Max(GroupMinHeight, group.Height),
                GroupHeaderHeight,
                group.Y,
                monitor.WorkArea.Top + GroupPeekWorkAreaGap,
                monitor.WorkArea.Bottom - GroupPeekWorkAreaGap);
        }

        private static void ApplyGroupPeekPresentation(
            GroupPeekVisualRegistration registration,
            GroupPeekPresentation presentation,
            bool isPeekActive)
        {
            registration.Outer.Height = presentation.VisualHeight;
            registration.Body.Visibility = presentation.BodyVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            bool opensAbove = isPeekActive && presentation.OpensAbove;
            registration.Outer.RowDefinitions[0].Height = opensAbove
                ? new GridLength(1, GridUnitType.Star)
                : GridLength.Auto;
            registration.Outer.RowDefinitions[1].Height = opensAbove
                ? GridLength.Auto
                : new GridLength(1, GridUnitType.Star);
            Grid.SetRow(registration.Header, opensAbove ? 1 : 0);
            Grid.SetRow(registration.Body, opensAbove ? 0 : 1);
            registration.Header.CornerRadius = !presentation.BodyVisible
                ? new CornerRadius(11)
                : opensAbove
                    ? new CornerRadius(0, 0, 11, 11)
                    : new CornerRadius(11, 11, 0, 0);
            registration.Body.CornerRadius = opensAbove
                ? new CornerRadius(11, 11, 0, 0)
                : new CornerRadius(0, 0, 11, 11);
            registration.BodyDivider.VerticalAlignment = opensAbove
                ? VerticalAlignment.Bottom
                : VerticalAlignment.Top;
            Canvas.SetTop(
                registration.Container,
                opensAbove
                    ? registration.Group.Y - (presentation.VisualHeight - GroupHeaderHeight)
                    : registration.Group.Y);
            Panel.SetZIndex(
                registration.Container,
                isPeekActive ? GroupPeekZIndex : GroupNormalZIndex);
        }

        private sealed record GroupPeekVisualRegistration(
            GroupInfo Group,
            Border Container,
            Grid Outer,
            Border Header,
            Border Body,
            Border BodyDivider,
            VirtualizingGroupPanel ItemsPanel,
            ContextMenu HeaderMenu);
    }
}
