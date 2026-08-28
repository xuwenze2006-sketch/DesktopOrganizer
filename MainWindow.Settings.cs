// 常用命令、设置、安全模式与控制面板显示
// 本文件由 v1.12 完整功能重构拆分；行为逻辑保持自 v1.11.6 不变。
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            RefreshDesktop();
        }

        private void RefreshDesktop()
        {
            RequestDesktopRefresh(clearIconCache: true, statusMessage: "桌面项目已刷新");
        }

        private void OpenDiagnosticsLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_diagnostics.LogDirectory);
                Process.Start(new ProcessStartInfo(_diagnostics.LogDirectory) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"无法打开日志目录：{exception.Message}",
                    "DesktopOrganizer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void AlignIconsButton_Click(object sender, RoutedEventArgs e)
        {
            AlignFreeIconsToGrid(showFeedback: true);
        }

        private void SnapToGridToggle_Click(object sender, RoutedEventArgs e)
        {
            _appLayout.SnapToGrid = SnapToGridToggle.IsChecked == true;
            CancelPushPreview(restoreVisuals: true);
            EndPushPreviewSession();
            UpdatePushReflowAvailability();

            if (_appLayout.SnapToGrid)
            {
                AlignFreeIconsToGrid(showFeedback: true);
            }
            else
            {
                SaveLayout();
                StatusText.Text = "网格吸附已关闭，可自由摆放图标；挤压排列暂不可用";
            }
        }

        private void PushReflowToggle_Click(object sender, RoutedEventArgs e)
        {
            if (!PushReflowPolicy.CanConfigure(
                    _isSafeModeActive,
                    _appLayout.SnapToGrid,
                    _appLayout.IsEditMode))
            {
                PushReflowToggle.IsChecked = _appLayout.PushReflowEnabled;
                StatusText.Text = _isSafeModeActive
                    ? "安全模式下挤压排列已暂停，原设置保持不变"
                    : !_appLayout.SnapToGrid
                        ? "需要先开启网格吸附"
                        : "需要先进入编辑布局";
                return;
            }

            _appLayout.PushReflowEnabled = PushReflowToggle.IsChecked == true;
            if (!_appLayout.PushReflowEnabled)
            {
                CancelPushPreview(restoreVisuals: true);
                EndPushPreviewSession();
            }

            SaveLayout();
            StatusText.Text = _appLayout.PushReflowEnabled
                ? "挤压排列已开启：拖到其它图标位置可实时向后挤压"
                : "挤压排列已关闭";
        }

        private void UpdatePushReflowAvailability()
        {
            PushReflowToggle.IsChecked = _appLayout.PushReflowEnabled;
            PushReflowToggle.IsEnabled = PushReflowPolicy.CanConfigure(
                _isSafeModeActive,
                _appLayout.SnapToGrid,
                _appLayout.IsEditMode);
            PushReflowToggle.ToolTip = _isSafeModeActive
                ? "安全模式下已暂停挤压预览；退出后恢复原设置"
                : !_appLayout.SnapToGrid
                    ? "需要先开启网格吸附"
                    : !_appLayout.IsEditMode
                        ? "需要先进入编辑布局"
                        : "拖到其它图标位置时，实时把该图标及后续图标向后挤；拖离目标会恢复预览前布局";
        }

        private void SafeModeToggle_Click(object sender, RoutedEventArgs e)
        {
            _isSafeModeActive = SafeModeToggle.IsChecked == true;
            ApplySafeModeState(showStatus: true);
        }

        private void ApplySafeModeState(bool showStatus, bool updateWatchers = true)
        {
            if (_isSafeModeActive)
            {
                if (updateWatchers)
                {
                    StopDesktopWatchers();
                    CancelScheduledDesktopRefresh();
                    CancelActiveDesktopRefresh();
                }

                CancelPushPreview(restoreVisuals: true);
                EndPushPreviewSession();
                if (showStatus)
                {
                    StatusText.Text = "安全模式已开启：实时监听、自动归类和挤压预览已暂停；原设置未改变";
                }
            }
            else
            {
                if (updateWatchers && IsLoaded && !_isClosing)
                {
                    // 安全模式中的手动刷新会更新分类缓存但暂停自动组迁移；先消费这份
                    // 已验证快照，否则随后相同的扫描会因“无变化”直接返回。
                    if (_desktopSnapshotInitialized)
                    {
                        RebuildDesktopIcons();
                    }

                    StartDesktopWatchers();
                    RequestDesktopRefresh(
                        clearIconCache: false,
                        statusMessage: "安全模式已关闭：原设置已恢复，桌面状态已同步");
                }
                else if (showStatus)
                {
                    StatusText.Text = "安全模式已关闭：原设置已恢复";
                }
            }

            SafeModeToggle.IsChecked = _isSafeModeActive;
            PushReflowToggle.IsChecked = _appLayout.PushReflowEnabled;
            AutoClassifyNewItemsToggle.IsChecked = _appLayout.AutoClassifyNewItems;
            UpdatePushReflowAvailability();
            UpdateAutoClassificationControls();
            _diagnostics.Log(
                $"SAFE_MODE enabled={_isSafeModeActive}, " +
                $"pushPreference={_appLayout.PushReflowEnabled}, " +
                $"autoClassifyPreference={_appLayout.AutoClassifyNewItems}");
        }

        private void HealthMonitorTimer_Tick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            TimeSpan expected = _healthMonitorTimer.Interval;
            TimeSpan elapsed = now - _lastHealthTickUtc;
            _lastHealthTickUtc = now;
            if (elapsed > TimeSpan.FromSeconds(30))
            {
                // 系统睡眠/休眠后的长间隔不视为 UI 卡顿。
                _consecutiveHealthWarnings = 0;
                _diagnostics.Log($"RESUME monitorGapMs={elapsed.TotalMilliseconds:F0}");
                return;
            }

            TimeSpan dispatcherDelay = elapsed > expected ? elapsed - expected : TimeSpan.Zero;
            ProcessHealthSnapshot health = _diagnostics.CaptureHealth(dispatcherDelay);

            bool warning = health.DispatcherDelay >= TimeSpan.FromSeconds(1.5) ||
                           health.WorkingSetBytes >= 800L * 1024 * 1024 ||
                           health.HandleCount >= 8_000;
            if (warning)
            {
                _diagnostics.Log("HEALTH " + health);
                _consecutiveHealthWarnings++;
            }
            else
            {
                _consecutiveHealthWarnings = 0;
            }

            if (!_isSafeModeActive && !_safeModeTriggeredThisSession &&
                (health.IsCritical || _consecutiveHealthWarnings >= 3))
            {
                _safeModeTriggeredThisSession = true;
                _isSafeModeActive = true;
                ApplySafeModeState(showStatus: false);
                StatusText.Text = "检测到界面持续延迟，已自动进入安全模式";
                _diagnostics.Log("AUTO_SAFE_MODE reason=" + health);
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            NativeMethods.DesktopKeyboardCommand? command = null;
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                command = NativeMethods.DesktopKeyboardCommand.UndoFileMove;
            }
            else if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                command = NativeMethods.DesktopKeyboardCommand.ClearSelection;
            }

            if (command != null && ExecuteDesktopKeyboardCommand(command.Value))
            {
                e.Handled = true;
            }
        }

        private bool DesktopKeyboardCommandReceived(
            NativeMethods.DesktopKeyboardCommand command)
        {
            if (_isClosing ||
                Dispatcher.HasShutdownStarted ||
                !CanExecuteDesktopKeyboardCommand(command))
            {
                return false;
            }

            try
            {
                // 低级键盘钩子必须立即返回；真实文件撤销与视觉更新只能在
                // Dispatcher 上执行，避免钩子超时后被 Windows 静默移除。
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    new Action(() =>
                    {
                        if (!_isClosing)
                        {
                            ExecuteDesktopKeyboardCommand(command);
                        }
                    }));
                return true;
            }
            catch (InvalidOperationException)
            {
                // Dispatcher 正在退出。
                return false;
            }
        }

        private bool CanExecuteDesktopKeyboardCommand(
            NativeMethods.DesktopKeyboardCommand command)
        {
            return command switch
            {
                NativeMethods.DesktopKeyboardCommand.UndoFileMove =>
                    _fileMoveHistory.First != null && !HasPendingFileOperations,
                NativeMethods.DesktopKeyboardCommand.ClearSelection =>
                    _selectedItemNames.Count > 0,
                _ => false
            };
        }

        private bool ExecuteDesktopKeyboardCommand(
            NativeMethods.DesktopKeyboardCommand command)
        {
            return command switch
            {
                NativeMethods.DesktopKeyboardCommand.UndoFileMove =>
                    TryUndoLastFileMove(),
                NativeMethods.DesktopKeyboardCommand.ClearSelection
                    when _selectedItemNames.Count > 0 => ClearSelectionFromKeyboard(),
                _ => false
            };
        }

        private bool ClearSelectionFromKeyboard()
        {
            ClearItemSelection();
            return true;
        }

        private void UndoFileMoveButton_Click(object sender, RoutedEventArgs e)
        {
            TryUndoLastFileMove();
        }

        private static GroupInfo? CreateUndoGroupSnapshot(GroupInfo? group)
        {
            if (group == null)
            {
                return null;
            }

            return new GroupInfo
            {
                Id = group.Id,
                Name = group.Name,
                X = group.X,
                Y = group.Y,
                Width = group.Width,
                Height = group.Height,
                IsCollapsed = group.IsCollapsed,
                IsAutoCategory = group.IsAutoCategory,
                AutoCategoryKey = group.AutoCategoryKey,
                UserRuleId = group.UserRuleId,
                IsSizeLocked = group.IsSizeLocked,
                SortMode = group.SortMode,
                ItemNames = new List<string>()
            };
        }

        private void PushFileMoveHistory(FileMoveUndoRecord record)
        {
            _fileMoveHistory.AddFirst(record);
            while (_fileMoveHistory.Count > 20)
            {
                _fileMoveHistory.RemoveLast();
            }
            UpdateUndoFileMoveButton();
        }

        private void UpdateUndoFileMoveButton()
        {
            FileMoveUndoRecord? next = _fileMoveHistory.First?.Value;
            UndoFileMoveButton.IsEnabled = next != null && !HasPendingFileOperations;
            UndoFileMoveButton.Content = HasPendingFileOperations ? "文件处理中…" : "撤销文件移动";
            UndoFileMoveButton.ToolTip = HasPendingFileOperations
                ? "真实文件移动、删除或撤销正在后台串行执行"
                : next != null
                    ? $"撤销：把“{next.DisplayName}”移回原位置"
                    : "当前没有可撤销的真实文件移动";
        }

        private bool TryUndoLastFileMove() => QueueUndoLastFileMove();

        private void EditModeToggle_Click(object sender, RoutedEventArgs e)
        {
            bool editMode = EditModeToggle.IsChecked == true;
            if (_appLayout.IsEditMode == editMode)
            {
                return;
            }

            ResetAllInteractionState(restoreDraggedVisual: true);
            _appLayout.IsEditMode = editMode;
            EditModeToggle.Content = editMode ? "完成编辑" : "编辑布局";
            UpdatePushReflowAvailability();
            RebuildDesktopIconsAndSaveLayout();
            StatusText.Text = editMode
                ? "已进入编辑布局：可拖动、缩放、移出和删除"
                : "已锁定布局：双击图标可打开，避免误移动";
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            SetOrganizerPaused(!_organizerPaused);
        }

        private void SetOrganizerPaused(bool paused)
        {
            if (_isClosing || _organizerPaused == paused)
            {
                return;
            }

            _organizerPaused = paused;

            if (_organizerPaused)
            {
                IconCanvas.Visibility = Visibility.Collapsed;
                UpdateRecycleBinWidgetVisibility();
                if (_nativeIconsWereVisible)
                {
                    NativeMethods.RestoreNativeDesktopIcons();
                }

                PauseButton.Content = "继续整理";
                QuickPauseButton.Content = "继续";
                PauseButton.ToolTip = "继续整理并重新隐藏系统桌面图标";
                QuickPauseButton.ToolTip = "继续整理并重新隐藏系统桌面图标";
                StatusText.Text = "已暂停";
            }
            else
            {
                IconCanvas.Visibility = Visibility.Visible;
                UpdateRecycleBinWidgetVisibility();
                bool newlyHidden = NativeMethods.EnsureNativeDesktopIconsHidden();
                if (newlyHidden && !_nativeIconsWereVisible)
                {
                    _nativeIconsWereVisible = true;
                    WriteSessionMarker();
                }

                RebuildDesktopIcons();
                PauseButton.Content = "暂停整理";
                QuickPauseButton.Content = "暂停";
                PauseButton.ToolTip = "暂停整理并临时恢复系统桌面图标";
                QuickPauseButton.ToolTip = "暂停整理并临时恢复系统桌面图标";
                StatusText.Text = "整理已继续";
            }

            _trayIcon?.RefreshToolTip();
        }

        private void AutoStartToggle_Click(object sender, RoutedEventArgs e)
        {
            bool enable = AutoStartToggle.IsChecked == true;
            SetAutoStartEnabled(enable, showErrorDialog: true);
        }

        private void ToggleAutoStartFromTray()
        {
            bool enable = !StartupManager.IsEnabledForCurrentExecutable();
            SetAutoStartEnabled(enable, showErrorDialog: false);
        }

        private void SetAutoStartEnabled(bool enabled, bool showErrorDialog)
        {
            if (StartupManager.TrySetEnabled(enabled, out string? errorMessage))
            {
                AutoStartToggle.IsChecked = enabled;
                StatusText.Text = enabled
                    ? "已开启开机自动启动；登录后将安静启动到通知区域"
                    : "已关闭开机自动启动";
                return;
            }

            AutoStartToggle.IsChecked = StartupManager.IsEnabledForCurrentExecutable();
            StatusText.Text = "修改开机启动设置失败";

            if (showErrorDialog)
            {
                MessageBox.Show(
                    $"无法修改开机启动设置。\n\n{errorMessage}",
                    "DesktopOrganizer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void PanelExpanderButton_Click(object sender, RoutedEventArgs e)
        {
            RecoverStaleInteractionState();
            e.Handled = true;
            if (_panelToggleInProgress)
            {
                return;
            }

            _panelToggleInProgress = true;
            try
            {
                SetCommandsExpanded(!_commandsExpanded);
            }
            finally
            {
                _panelToggleInProgress = false;

                // 无激活桌面窗口在布局尺寸改变的同一轮输入中，极少数情况下会让
                // Button 保留鼠标捕获。显式释放可避免后续整块面板继续路由到“整理”。
                if (ReferenceEquals(Mouse.Captured, PanelExpanderButton))
                {
                    Mouse.Capture(null);
                }
            }
        }

        private void SetCommandsExpanded(bool expanded)
        {
            if (_commandsExpanded == expanded &&
                ExpandedCommands.Visibility == (expanded ? Visibility.Visible : Visibility.Collapsed))
            {
                return;
            }

            _commandsExpanded = expanded;
            ExpandedCommands.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            PanelExpanderButton.Content = expanded ? "▴  收起" : "☰  整理";
            PanelExpanderButton.ToolTip = expanded ? "收起整理命令" : "展开整理命令";

            // 展开状态不属于持久化布局，不能在每次点击时排队写 layout.json。
            // 仅在布局完成后低优先级修正面板边界，而且同一轮最多排队一次。
            ScheduleControlPanelClamp(DispatcherPriority.ContextIdle);
            if (!expanded)
            {
                _controlPanelAutoCollapseTimer.Stop();
            }
            else
            {
                ScheduleControlPanelAutoCollapse();
            }
        }

        private void ControlPanel_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _controlPanelAutoCollapseTimer.Stop();
        }

        private void ControlPanel_MouseEnter(object sender, MouseEventArgs e)
        {
            _controlPanelAutoCollapseTimer.Stop();
        }

        private void ControlPanel_MouseLeave(object sender, MouseEventArgs e)
        {
            ScheduleControlPanelAutoCollapse();
        }

        private void AutoCollapsePanelToggle_Click(object sender, RoutedEventArgs e)
        {
            _appLayout.AutoCollapseControlPanel = AutoCollapsePanelToggle.IsChecked == true;
            if (_appLayout.AutoCollapseControlPanel)
            {
                ScheduleControlPanelAutoCollapse();
                StatusText.Text = "面板自动收起已开启";
            }
            else
            {
                _controlPanelAutoCollapseTimer.Stop();
                StatusText.Text = "面板自动收起已关闭";
            }

            SaveLayout();
        }

        private void CompactGroupLayoutToggle_Click(object sender, RoutedEventArgs e)
        {
            _appLayout.CompactGroupLayout = CompactGroupLayoutToggle.IsChecked == true;
            foreach (GroupInfo group in _appLayout.Groups.Where(group => !group.IsSizeLocked))
            {
                AutoFitGroup(group, clampPosition: false);
            }

            foreach (GroupInfo group in _appLayout.Groups)
            {
                ClampGroupToCanvas(group);
            }

            bool rearranged = GroupLayoutCollisionDetector.HasCollision(
                _appLayout.Groups.Select(GetGroupBounds).ToList(),
                GetRecycleBinWidgetObstacle());
            int collapsed = rearranged ? ArrangeGroupsSmartly() : 0;
            RebuildDesktopIconsAndSaveLayout();
            string modeMessage = _appLayout.CompactGroupLayout
                ? "紧凑分类框已开启；大型分类可使用四列图标"
                : "已恢复舒展分类框尺寸";
            StatusText.Text = rearranged
                ? collapsed > 0
                    ? $"{modeMessage}；已重新排列并收起 {collapsed} 个自动分类以避免重叠"
                    : $"{modeMessage}；已重新排列以避免重叠"
                : modeMessage;
        }

        private void ReserveWorkspaceToggle_Click(object sender, RoutedEventArgs e)
        {
            _appLayout.ReserveTemporaryWorkspace = ReserveWorkspaceToggle.IsChecked == true;
            SaveLayout();
            StatusText.Text = _appLayout.ReserveTemporaryWorkspace
                ? "智能布局会优先保留主屏底部约三分之一临时区域"
                : "智能布局可使用整个桌面工作区";
        }

        private void AutoFitAllGroupsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_appLayout.Groups.Count == 0)
            {
                StatusText.Text = "当前没有分类框";
                return;
            }

            _lastSmartLayoutSnapshot = CaptureGroupLayoutSnapshot();
            UndoSmartLayoutButton.IsEnabled = true;
            foreach (GroupInfo group in _appLayout.Groups)
            {
                group.IsSizeLocked = false;
            }

            int collapsed = ArrangeGroupsSmartly();
            RebuildDesktopIconsAndSaveLayout();
            StatusText.Text = collapsed > 0
                ? $"已适应全部分类框，并收起 {collapsed} 个自动分类以避免拥挤"
                : $"已适应并重新排列 {_appLayout.Groups.Count} 个分类框";
        }

        private void ScheduleControlPanelAutoCollapse()
        {
            _controlPanelAutoCollapseTimer.Stop();
            if (_appLayout.AutoCollapseControlPanel &&
                _commandsExpanded &&
                ControlPanel.Visibility == Visibility.Visible &&
                !ControlPanel.IsMouseOver &&
                !_isControlPanelDragging)
            {
                _controlPanelAutoCollapseTimer.Start();
            }
        }

        private void ControlPanelAutoCollapseTimer_Tick(object? sender, EventArgs e)
        {
            _controlPanelAutoCollapseTimer.Stop();
            if (_appLayout.AutoCollapseControlPanel &&
                _commandsExpanded &&
                ControlPanel.Visibility == Visibility.Visible &&
                !ControlPanel.IsMouseOver &&
                !_isControlPanelDragging)
            {
                SetCommandsExpanded(false);
                StatusText.Text = "命令面板已自动收起";
            }
        }

        private void HidePanelButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            HideControlPanel(showRestoreButton: true);
        }

        private void ControlPanelRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            ShowControlPanel("总面板已恢复");
        }

        private void HideControlPanel(bool showRestoreButton = true)
        {
            RecoverStaleInteractionState();
            _controlPanelAutoCollapseTimer.Stop();
            SetCommandsExpanded(false);
            ControlPanel.Visibility = Visibility.Collapsed;
            if (showRestoreButton)
            {
                PositionControlPanelRestoreButton();
            }
            ControlPanelRestoreButton.Visibility = showRestoreButton
                ? Visibility.Visible
                : Visibility.Collapsed;

            // 控件在隐藏过程中若仍持有捕获，会让恢复按钮也无法点击。
            if (ReferenceEquals(Mouse.Captured, ControlPanel) ||
                ReferenceEquals(Mouse.Captured, ControlPanelDragHandle) ||
                ReferenceEquals(Mouse.Captured, PanelExpanderButton))
            {
                Mouse.Capture(null);
            }
        }

        private void ToggleControlPanel()
        {
            if (ControlPanel.Visibility == Visibility.Visible)
            {
                HideControlPanel(showRestoreButton: true);
            }
            else
            {
                ShowControlPanel();
            }
        }

        private void ShowControlPanel(string? statusMessage = null, bool expandCommands = false)
        {
            ControlPanelRestoreButton.Visibility = Visibility.Collapsed;
            ControlPanel.Visibility = Visibility.Visible;
            ApplyControlPanelPosition();
            if (expandCommands)
            {
                SetCommandsExpanded(true);
            }

            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                StatusText.Text = statusMessage;
            }

            ScheduleControlPanelClamp(DispatcherPriority.Loaded);

            // 从托盘或再次运行 EXE 唤出面板时，顺便校正一次桌面 Z 序。
            // 使用 ContextIdle 避免在当前按钮 MouseUp 路由中直接调整窗口层级。
            _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (_isClosing || IsUserInteractionActive())
                {
                    return;
                }

                IntPtr currentHandle = new WindowInteropHelper(this).Handle;
                if (_isAttachedToDesktop && _desktopHostHandle != IntPtr.Zero)
                {
                    _isAttachedToDesktop = NativeMethods.EnsureWindowInDesktopLayer(
                        currentHandle,
                        _desktopHostHandle);
                }
            }));
        }

        public void ShowControlPanelFromExternalLaunch()
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(new Action(ShowControlPanelFromExternalLaunch));
                return;
            }

            ShowControlPanel("程序已在运行", expandCommands: true);
        }

    }
}
