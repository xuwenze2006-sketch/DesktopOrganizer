// 分组拖动、缩放、删除与重命名
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private void GroupHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement header ||
                header.Tag is not GroupInfo group ||
                e.ChangedButton != MouseButton.Left ||
                _draggedElement != null)
            {
                return;
            }

            if (!_appLayout.IsEditMode)
            {
                return;
            }

            // 标题栏中的收起、菜单、删除按钮继续保持正常点击，不触发分组拖动。
            DependencyObject? source = e.OriginalSource as DependencyObject;
            while (source != null && source != header)
            {
                if (source is ButtonBase)
                {
                    return;
                }

                source = VisualTreeHelper.GetParent(source) ?? LogicalTreeHelper.GetParent(source);
            }

            if (e.ClickCount == 2)
            {
                AutoFitGroupAndUnlock(group);
                e.Handled = true;
                return;
            }

            UIElement? container = FindGroupContainer(header);
            if (container == null)
            {
                return;
            }

            ClearPendingIconDrag();
            _draggedElement = container;
            _draggedGroup = group;
            _draggedIsGroup = true;
            _groupDragCaptureElement = header;
            _groupDragStartPosition = new Point(group.X, group.Y);
            _groupDragMouseDownCanvasPoint = e.GetPosition(IconCanvas);
            _groupDragMoved = false;
            _dragStartOffset = e.GetPosition(container);

            // 必须由接收 Move/Up 事件的标题栏捕获鼠标，而不是外层分组容器。
            // 这样即使光标移出标题栏，拖动仍能连续进行。
            if (!Mouse.Capture(header, CaptureMode.Element))
            {
                ResetGroupDragState();
                return;
            }

            e.Handled = true;
        }

        private void GroupHeader_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggedElement == null ||
                !_draggedIsGroup ||
                _draggedGroup == null ||
                _groupDragCaptureElement == null ||
                e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point canvasPosition = e.GetPosition(IconCanvas);
            if (!_groupDragMoved)
            {
                double deltaX = canvasPosition.X - _groupDragMouseDownCanvasPoint.X;
                double deltaY = canvasPosition.Y - _groupDragMouseDownCanvasPoint.Y;
                if (Math.Abs(deltaX) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(deltaY) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                _groupDragMoved = true;
                Panel.SetZIndex(_draggedElement, 500);
            }

            double left = canvasPosition.X - _dragStartOffset.X;
            double top = canvasPosition.Y - _dragStartOffset.Y;
            ClampGroupCoordinates(_draggedGroup, ref left, ref top);
            Canvas.SetLeft(_draggedElement, left);
            Canvas.SetTop(_draggedElement, top);
            e.Handled = true;
        }

        private void GroupHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggedElement == null || !_draggedIsGroup)
            {
                return;
            }

            CompleteGroupDrag(commit: _groupDragMoved);
            e.Handled = true;
        }

        private void GroupHeader_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isCompletingGroupDrag ||
                _draggedElement == null ||
                !_draggedIsGroup ||
                !ReferenceEquals(sender, _groupDragCaptureElement))
            {
                return;
            }

            // Alt+Tab、系统弹窗等意外中断时回到拖动前位置，避免留下半完成状态。
            CompleteGroupDrag(commit: false);
        }

        private void CompleteGroupDrag(bool commit)
        {
            if (_draggedElement == null || !_draggedIsGroup)
            {
                ResetGroupDragState();
                return;
            }

            UIElement draggedElement = _draggedElement;
            GroupInfo? draggedGroup = _draggedGroup;
            bool moved = _groupDragMoved;

            _isCompletingGroupDrag = true;
            try
            {
                if (_groupDragCaptureElement != null &&
                    ReferenceEquals(Mouse.Captured, _groupDragCaptureElement))
                {
                    Mouse.Capture(null);
                }
            }
            finally
            {
                _isCompletingGroupDrag = false;
            }

            if (draggedGroup != null)
            {
                if (commit && moved)
                {
                    draggedGroup.X = SafeCanvasCoordinate(Canvas.GetLeft(draggedElement));
                    draggedGroup.Y = SafeCanvasCoordinate(Canvas.GetTop(draggedElement));
                    ClampGroupToCanvas(draggedGroup);
                }
                else
                {
                    draggedGroup.X = _groupDragStartPosition.X;
                    draggedGroup.Y = _groupDragStartPosition.Y;
                }

                Canvas.SetLeft(draggedElement, draggedGroup.X);
                Canvas.SetTop(draggedElement, draggedGroup.Y);
            }

            Panel.SetZIndex(draggedElement, 100);
            ResetGroupDragState();

            if (commit && moved)
            {
                SaveLayout();
                StatusText.Text = draggedGroup == null
                    ? "分组位置已保存"
                    : $"已移动“{draggedGroup.Name}”";
            }
        }

        private void ResetGroupDragState()
        {
            _draggedElement = null;
            _draggedGroup = null;
            _draggedIsGroup = false;
            _groupDragCaptureElement = null;
            _groupDragMoved = false;
        }

        private UIElement? FindGroupContainer(DependencyObject start)
        {
            DependencyObject? current = start;
            while (current != null)
            {
                DependencyObject? parent = VisualTreeHelper.GetParent(current);
                if (parent == IconCanvas && current is UIElement ui)
                {
                    return ui;
                }

                current = parent;
            }

            return null;
        }

        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!_appLayout.IsEditMode)
            {
                return;
            }

            if (sender is not FrameworkElement thumb || thumb.Tag is not GroupInfo group)
            {
                return;
            }

            DesktopMonitorRegion monitor = GetMonitorForItemRect(GetGroupBounds(group));
            double maxWidth = Math.Max(GroupMinWidth, monitor.WorkArea.Right - group.X);
            double maxHeight = Math.Max(GroupMinHeight, monitor.WorkArea.Bottom - group.Y);
            group.IsSizeLocked = true;
            group.Width = Math.Clamp(group.Width + e.HorizontalChange, GroupMinWidth, maxWidth);
            group.Height = Math.Clamp(group.Height + e.VerticalChange, GroupMinHeight, maxHeight);

            if (VisualTreeHelper.GetParent(thumb) is Grid grid)
            {
                grid.Width = group.Width;
                grid.Height = group.Height;
            }
        }

        private void DeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            if (!_appLayout.IsEditMode)
            {
                StatusText.Text = "请先开启编辑布局";
                return;
            }

            if (sender is not FrameworkElement fe || fe.Tag is not GroupInfo group)
            {
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                $"删除分组“{group.Name}”？\n分组中的图标会回到桌面，不会删除任何文件。",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            List<string> releasedNames = group.ItemNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(name => _desktopItems.ContainsKey(name))
                .ToList();
            var releaseRequests = new List<(string Name, IconPosition Requested)>(releasedNames.Count);
            for (int i = 0; i < releasedNames.Count; i++)
            {
                string name = releasedNames[i];
                var position = new IconPosition
                {
                    X = group.X + (i % 4) * IconCellWidth,
                    Y = group.Y + GetGroupDisplayHeight(group) + 10 + (i / 4) * IconCellHeight
                };
                ClampIconPosition(position);
                releaseRequests.Add((name, position));
            }

            Dictionary<string, IconPosition> releasedPositions;
            if (_appLayout.SnapToGrid)
            {
                var releasedNameSet = new HashSet<string>(releasedNames, StringComparer.OrdinalIgnoreCase);
                var ignoredGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { group.Id };
                Dictionary<string, IconPosition>? plan = TryPlanAlignedIconPositions(
                    releaseRequests,
                    GetOccupiedFreeGridCells(releasedNameSet),
                    ignoredGroupIds);
                if (plan == null)
                {
                    StatusText.Text = $"没有足够的可用网格，分组“{group.Name}”未删除";
                    return;
                }

                releasedPositions = plan;
            }
            else
            {
                releasedPositions = releaseRequests.ToDictionary(
                    request => request.Name,
                    request => request.Requested,
                    StringComparer.OrdinalIgnoreCase);
            }

            foreach ((string name, IconPosition position) in releasedPositions)
            {
                _appLayout.FreeIcons[name] = position;
            }

            if (group.IsAutoCategory)
            {
                foreach (string name in group.ItemNames)
                {
                    _appLayout.AutoClassificationOriginalPositions.Remove(name);
                }
            }

            _appLayout.Groups.Remove(group);
            RebuildDesktopIconsAndSaveLayout();
            StatusText.Text = $"已删除分组“{group.Name}”，恢复 {releasedPositions.Count} 个自由图标";
        }

        private void RenameGroup(GroupInfo group)
        {
            if (!_appLayout.IsEditMode)
            {
                StatusText.Text = "请先开启编辑布局";
                return;
            }

            SimpleInputDialog dialog = CreateInputDialog("重命名分组：", group.Name);
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ResultText))
            {
                group.Name = dialog.ResultText.Trim();
                RebuildDesktopIconsAndSaveLayout();
            }
        }

        // ==================== 图标网格对齐 ====================

    }
}
