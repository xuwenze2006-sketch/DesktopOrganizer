// 控制面板拖动与输入状态恢复
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private void ControlPanelDragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || IsPointerOverButton(e.OriginalSource as DependencyObject))
            {
                return;
            }

            _isControlPanelDragging = true;
            _controlPanelDragMoved = false;
            _controlPanelDragStartMousePoint = e.GetPosition(RootGrid);
            _controlPanelDragStartPosition = GetControlPanelPosition();
            if (!Mouse.Capture(ControlPanelDragHandle, CaptureMode.Element))
            {
                _isControlPanelDragging = false;
                ScheduleControlPanelAutoCollapse();
                return;
            }
            e.Handled = true;
        }

        private void ControlPanelDragHandle_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isControlPanelDragging || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point current = e.GetPosition(RootGrid);
            double deltaX = current.X - _controlPanelDragStartMousePoint.X;
            double deltaY = current.Y - _controlPanelDragStartMousePoint.Y;
            if (!_controlPanelDragMoved &&
                Math.Abs(deltaX) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(deltaY) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _controlPanelDragMoved = true;
            SetControlPanelPosition(
                _controlPanelDragStartPosition.X + deltaX,
                _controlPanelDragStartPosition.Y + deltaY,
                updateLayout: true);
            e.Handled = true;
        }

        private void ControlPanelDragHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CompleteControlPanelDrag();
            if (_controlPanelDragMoved)
            {
                e.Handled = true;
            }
        }

        private void ControlPanelDragHandle_LostMouseCapture(object sender, MouseEventArgs e)
        {
            CompleteControlPanelDrag();
        }

        private void CompleteControlPanelDrag()
        {
            if (!_isControlPanelDragging)
            {
                return;
            }

            _isControlPanelDragging = false;
            if (ReferenceEquals(Mouse.Captured, ControlPanelDragHandle))
            {
                Mouse.Capture(null);
            }

            if (_controlPanelDragMoved)
            {
                ClampControlPanelToCanvas();
                SaveLayout();
                StatusText.Text = "总面板位置已保存";
            }

            ScheduleControlPanelAutoCollapse();
        }

        private static bool IsPointerOverButton(DependencyObject? source)
        {
            DependencyObject? current = source;
            while (current != null)
            {
                if (current is ButtonBase)
                {
                    return true;
                }

                try
                {
                    current = VisualTreeHelper.GetParent(current);
                }
                catch (InvalidOperationException)
                {
                    current = LogicalTreeHelper.GetParent(current);
                }
                catch (ArgumentException)
                {
                    current = LogicalTreeHelper.GetParent(current);
                }
            }

            return false;
        }

        private void RootGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 每次新的输入开始前清理上一次意外中断的捕获/拖动状态。
            // 绑定在根容器而不是控制面板，分组按钮和动态生成的图标也能受保护。
            RecoverStaleInteractionState();
            if (ShouldClearItemSelectionFromRootPointer(
                    e.ChangedButton,
                    Keyboard.Modifiers,
                    e.OriginalSource as DependencyObject))
            {
                ClearItemSelection();
            }
        }

        internal static bool ShouldClearItemSelectionFromRootPointer(
            MouseButton changedButton,
            ModifierKeys modifiers,
            DependencyObject? originalSource) =>
            changedButton == MouseButton.Left &&
            !modifiers.HasFlag(ModifierKeys.Control) &&
            !IsPointerOverIcon(originalSource) &&
            !IsPointerOverButton(originalSource);

        private static bool IsPointerOverIcon(DependencyObject? source)
        {
            DependencyObject? current = source;
            while (current != null)
            {
                if (current is FrameworkElement element && element.Tag is IconTag)
                {
                    return true;
                }

                try
                {
                    current = VisualTreeHelper.GetParent(current);
                }
                catch
                {
                    current = LogicalTreeHelper.GetParent(current);
                }
            }

            return false;
        }

        private void RootGrid_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            QueueStaleInputRecovery();
        }

        private void RootGrid_MouseLeave(object sender, MouseEventArgs e)
        {
            if (Mouse.LeftButton == MouseButtonState.Released)
            {
                QueueStaleInputRecovery();
            }
        }

        private void QueueStaleInputRecovery()
        {
            if (_isClosing || Interlocked.Exchange(ref _staleCaptureRecoveryQueued, 1) == 1)
            {
                return;
            }

            // 使用 Background 优先级保证本轮 MouseUp/Click 完整结束后再释放异常捕获，
            // 避免干扰正常按钮 Click，同时不会让捕获遗留到下一次点击。
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    try
                    {
                        ReleaseStaleButtonCapture();
                        RecoverStaleInteractionState();
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _staleCaptureRecoveryQueued, 0);
                    }
                }));
        }

        private bool IsUserInteractionActive()
        {
            return _isControlPanelDragging ||
                   _isRecycleBinWidgetDragging ||
                   _draggedFolderPortal != null ||
                   _folderPortalDragCaptureElement != null ||
                   _draggedElement != null ||
                   _pendingIconDragElement != null ||
                   _groupDragCaptureElement != null ||
                   _groupedIconDragSourceGroup != null ||
                   _pushPreviewOriginalPositions != null ||
                   _pushPreviewPositions != null;
        }

        private void RecoverStaleInteractionState()
        {
            ReleaseStaleButtonCapture();

            if (!IsUserInteractionActive())
            {
                return;
            }

            IInputElement? captured = Mouse.Captured;
            bool hasExpectedCapture =
                ReferenceEquals(captured, ControlPanelDragHandle) ||
                ReferenceEquals(captured, RecycleBinWidgetDragHandle) ||
                ReferenceEquals(captured, _folderPortalDragCaptureElement) ||
                ReferenceEquals(captured, _groupDragCaptureElement) ||
                ReferenceEquals(captured, _draggedElement);

            if (Mouse.LeftButton == MouseButtonState.Pressed &&
                (hasExpectedCapture || _pendingIconDragElement != null))
            {
                return;
            }

            ResetAllInteractionState(restoreDraggedVisual: true);
            StatusText.Text = "已恢复异常中断的拖动状态";
        }

        private void ReleaseStaleButtonCapture()
        {
            if (Mouse.LeftButton != MouseButtonState.Released ||
                Mouse.Captured is not ButtonBase capturedButton)
            {
                return;
            }

            // Button 正常会在 MouseUp 后释放捕获；仍然保留说明输入路由已异常中断。
            // 覆盖总面板和动态分组按钮，但不干预 ContextMenu/Popup 中的控件。
            bool belongsToMainVisualTree = IsDescendantOf(capturedButton, RootGrid);
            bool wasRemovedFromVisualTree = PresentationSource.FromVisual(capturedButton) == null;
            if (belongsToMainVisualTree || wasRemovedFromVisualTree)
            {
                Mouse.Capture(null);
            }
        }

        private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
        {
            DependencyObject? current = child;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }

                try
                {
                    current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
                }
                catch (InvalidOperationException)
                {
                    current = LogicalTreeHelper.GetParent(current);
                }
                catch (ArgumentException)
                {
                    current = LogicalTreeHelper.GetParent(current);
                }
            }

            return false;
        }

        private void ResetAllInteractionState(bool restoreDraggedVisual)
        {
            UIElement? draggedElement = _draggedElement;
            GroupInfo? draggedGroup = _draggedGroup;
            bool draggedWasGroup = _draggedIsGroup;
            bool groupedIconWasDetached = _groupedIconDragSourceGroup != null;
            bool recycleWidgetWasDragging = _isRecycleBinWidgetDragging;
            bool folderPortalWasDragging = _draggedFolderPortal != null;
            bool needsVisualRebuild = restoreDraggedVisual && groupedIconWasDetached;

            if (restoreDraggedVisual && recycleWidgetWasDragging)
            {
                SetRecycleBinWidgetPosition(
                    _recycleBinWidgetDragStartPosition.X,
                    _recycleBinWidgetDragStartPosition.Y,
                    updateLayout: false);
            }

            if (folderPortalWasDragging)
            {
                CancelFolderPortalDrag(commit: false);
            }

            // 先清除面板拖动标志，避免释放鼠标捕获时 LostMouseCapture 再次提交拖动。
            _isControlPanelDragging = false;
            _controlPanelDragMoved = false;
            _isRecycleBinWidgetDragging = false;
            _recycleBinWidgetDragMoved = false;
            _isCompletingIconDrop = true;
            _isCompletingGroupDrag = true;
            try
            {
                IInputElement? captured = Mouse.Captured;
                bool expectedCapture =
                    ReferenceEquals(captured, ControlPanelDragHandle) ||
                    ReferenceEquals(captured, RecycleBinWidgetDragHandle) ||
                    ReferenceEquals(captured, _folderPortalDragCaptureElement) ||
                    ReferenceEquals(captured, _groupDragCaptureElement) ||
                    ReferenceEquals(captured, draggedElement);
                bool staleCaptureInsideWindow =
                    captured is DependencyObject dependencyObject &&
                    IsDescendantOf(dependencyObject, RootGrid) &&
                    (Mouse.LeftButton == MouseButtonState.Released || _isClosing);

                if (expectedCapture || staleCaptureInsideWindow)
                {
                    Mouse.Capture(null);
                }
            }
            finally
            {
                _isCompletingIconDrop = false;
                _isCompletingGroupDrag = false;
            }

            if (restoreDraggedVisual && draggedElement != null)
            {
                if (draggedWasGroup && draggedGroup != null)
                {
                    draggedGroup.X = _groupDragStartPosition.X;
                    draggedGroup.Y = _groupDragStartPosition.Y;
                    Canvas.SetLeft(draggedElement, draggedGroup.X);
                    Canvas.SetTop(draggedElement, draggedGroup.Y);
                    Panel.SetZIndex(draggedElement, 100);
                }
                else if (draggedElement is FrameworkElement iconElement &&
                         iconElement.Tag is IconTag iconTag)
                {
                    if (_pushPreviewOriginalPositions != null &&
                        _pushPreviewOriginalPositions.TryGetValue(
                            iconTag.DisplayName,
                            out IconPosition? originalPosition))
                    {
                        Canvas.SetLeft(iconElement, originalPosition.X);
                        Canvas.SetTop(iconElement, originalPosition.Y);
                        Panel.SetZIndex(iconElement, 200);
                    }
                    else if (_dragOriginalPosition != null && !groupedIconWasDetached)
                    {
                        Canvas.SetLeft(iconElement, _dragOriginalPosition.X);
                        Canvas.SetTop(iconElement, _dragOriginalPosition.Y);
                        Panel.SetZIndex(iconElement, 200);
                    }
                }
            }

            ClearPhysicalFolderDropPreview();
            ClearGroupDropPreview();
            ClearRecycleBinDropPreview();
            CancelPushPreview(restoreVisuals: restoreDraggedVisual);
            EndPushPreviewSession();
            _draggedElement = null;
            _draggedGroup = null;
            _draggedIsGroup = false;
            _groupDragCaptureElement = null;
            _groupDragMoved = false;
            _groupedIconDragSourceGroup = null;
            _dragOriginalPosition = null;
            _dragAllowsLayoutMove = false;
            ClearPendingIconDrag();
            _isControlPanelDragging = false;
            _controlPanelDragMoved = false;
            _isRecycleBinWidgetDragging = false;
            _recycleBinWidgetDragMoved = false;

            if (needsVisualRebuild && !_isClosing && _desktopItems.Count > 0)
            {
                RebuildDesktopIcons();
            }
        }

        private void ScheduleControlPanelClamp(DispatcherPriority priority)
        {
            if (_isClosing ||
                _pendingPanelClampOperation?.Status is DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing)
            {
                return;
            }

            _pendingPanelClampOperation = Dispatcher.BeginInvoke(priority, new Action(() =>
            {
                _pendingPanelClampOperation = null;
                if (!_isClosing)
                {
                    ClampControlPanelToCanvas();
                }
            }));
        }

        private void ApplyControlPanelPosition()
        {
            Size panelSize = MeasureControlPanelContentSize();
            Rect primaryWorkArea = GetPrimaryWorkArea();
            double defaultX = Math.Max(
                primaryWorkArea.Left,
                primaryWorkArea.Right - panelSize.Width - 14);
            double x = _appLayout.ControlPanelX ?? defaultX;
            double y = _appLayout.ControlPanelY ?? (primaryWorkArea.Top + 14);
            SetControlPanelPosition(x, y, updateLayout: true);
        }

        private Size MeasureControlPanelContentSize()
        {
            ControlPanel.InvalidateMeasure();
            ControlPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Thickness margin = ControlPanel.Margin;
            double horizontalMargin =
                (double.IsFinite(margin.Left) ? margin.Left : 0) +
                (double.IsFinite(margin.Right) ? margin.Right : 0);
            double verticalMargin =
                (double.IsFinite(margin.Top) ? margin.Top : 0) +
                (double.IsFinite(margin.Bottom) ? margin.Bottom : 0);
            double desiredWidth = ControlPanel.DesiredSize.Width - horizontalMargin;
            double desiredHeight = ControlPanel.DesiredSize.Height - verticalMargin;
            double width = double.IsFinite(desiredWidth) && desiredWidth > 0
                ? desiredWidth
                : GetRenderedLength(
                    ControlPanel.ActualWidth,
                    ControlPanel.Width,
                    ControlPanel.DesiredSize.Width);
            double height = double.IsFinite(desiredHeight) && desiredHeight > 0
                ? desiredHeight
                : GetRenderedLength(
                    ControlPanel.ActualHeight,
                    ControlPanel.Height,
                    ControlPanel.DesiredSize.Height);
            return new Size(Math.Max(0, width), Math.Max(0, height));
        }

        private Point GetControlPanelPosition()
        {
            Thickness margin = ControlPanel.Margin;
            return new Point(SafeCanvasCoordinate(margin.Left), SafeCanvasCoordinate(margin.Top));
        }

        private void ClampControlPanelToCanvas()
        {
            Point position = GetControlPanelPosition();
            SetControlPanelPosition(position.X, position.Y, updateLayout: true);
            PositionControlPanelRestoreButton();
        }

        private void PositionControlPanelRestoreButton()
        {
            ControlPanelRestoreButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double width = GetRenderedLength(
                ControlPanelRestoreButton.ActualWidth,
                ControlPanelRestoreButton.Width,
                ControlPanelRestoreButton.DesiredSize.Width);
            double height = GetRenderedLength(
                ControlPanelRestoreButton.ActualHeight,
                ControlPanelRestoreButton.Height,
                ControlPanelRestoreButton.DesiredSize.Height);
            Point panelPosition = GetControlPanelPosition();
            DesktopMonitorRegion monitor = _desktopGeometry.FindMonitorForPoint(panelPosition);
            Point clamped = ClampRectToUsableDesktop(
                monitor.WorkArea.Right - width - 14,
                monitor.WorkArea.Top + 14,
                width,
                height);
            ControlPanelRestoreButton.Margin = new Thickness(clamped.X, clamped.Y, 0, 0);
        }

        private void SetControlPanelPosition(
            double x,
            double y,
            bool updateLayout,
            Size? measuredSize = null)
        {
            Size panelSize = measuredSize ?? MeasureControlPanelContentSize();
            double width = panelSize.Width;
            double height = panelSize.Height;
            var requested = new Rect(
                SafeCanvasCoordinate(x),
                SafeCanvasCoordinate(y),
                Math.Max(0, SafeCanvasCoordinate(width)),
                Math.Max(0, SafeCanvasCoordinate(height)));
            DesktopMonitorRegion monitor = GetMonitorForItemRect(requested);
            Point clamped = ControlPanelPlacementPolicy.ClampToWorkArea(
                monitor.WorkArea,
                requested.X,
                requested.Y,
                requested.Width,
                requested.Height,
                edgeInset: 14);
            x = clamped.X;
            y = clamped.Y;
            ControlPanel.Margin = new Thickness(x, y, 0, 0);

            if (updateLayout)
            {
                _appLayout.ControlPanelX = x;
                _appLayout.ControlPanelY = y;
            }
        }

        private void RequestExit()
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(new Action(RequestExit));
                return;
            }

            Close();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e) => RequestExit();

        private SimpleInputDialog CreateInputDialog(string prompt, string defaultValue)
        {
            var dialog = new SimpleInputDialog(prompt, defaultValue);

            // 主窗口被固定在 Progman 正上方的桌面底层。输入对话框不继承该 owner，
            // 避免对话框也受桌面 Z 序约束；它应作为普通顶级窗口显示在用户前方。
            if (!_isAttachedToDesktop)
            {
                dialog.Owner = this;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            return dialog;
        }

    }
}
