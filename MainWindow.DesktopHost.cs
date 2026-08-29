// 桌面宿主、窗口层级与生命周期
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        /// <summary>
        /// ShowActivated=false 的窗口不能在首次 Show 时处于 Maximized 状态。
        /// 句柄建立前先使用 WPF 虚拟屏幕范围；句柄建立后再按 Win32 物理像素
        /// 覆盖全部显示器，并把各显示器工作区转换为统一的 WPF 客户区坐标。
        /// </summary>
        private bool ConfigureDesktopBounds()
        {
            double targetLeft = SystemParameters.VirtualScreenLeft;
            double targetTop = SystemParameters.VirtualScreenTop;
            double targetWidth = Math.Max(1, SystemParameters.VirtualScreenWidth);
            double targetHeight = Math.Max(1, SystemParameters.VirtualScreenHeight);
            bool changed = WindowState != WindowState.Normal ||
                           !double.IsFinite(Left) ||
                           !double.IsFinite(Top) ||
                           !double.IsFinite(Width) ||
                           !double.IsFinite(Height) ||
                           Math.Abs(Left - targetLeft) > 0.25 ||
                           Math.Abs(Top - targetTop) > 0.25 ||
                           Math.Abs(Width - targetWidth) > 0.25 ||
                           Math.Abs(Height - targetHeight) > 0.25;
            if (!changed)
            {
                return false;
            }

            WindowState = WindowState.Normal;
            Left = targetLeft;
            Top = targetTop;
            Width = targetWidth;
            Height = targetHeight;
            return true;
        }

        private bool UpdateDesktopHostBounds(IntPtr hwnd, bool remapExistingLayout = false)
        {
            DesktopGeometry previousGeometry = _desktopGeometry;
            bool windowBoundsChanged = ConfigureDesktopBounds();
            RefreshDesktopGeometry(hwnd);
            bool geometryChanged = !previousGeometry.IsEquivalentTo(_desktopGeometry);

            if (remapExistingLayout && _desktopSnapshotInitialized && geometryChanged)
            {
                _ = RemapLayoutForGeometryChange(previousGeometry);
            }

            if (_isAttachedToDesktop &&
                _desktopHostHandle != IntPtr.Zero &&
                !NativeMethods.IsWindowAttachedToDesktop(hwnd, _desktopHostHandle))
            {
                _isAttachedToDesktop = NativeMethods.EnsureWindowInDesktopLayer(
                    hwnd,
                    _desktopHostHandle);
            }

            return windowBoundsChanged || geometryChanged;
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            _desktopWindowHandle = hwnd;
            NativeMethods.ApplyDesktopWindowStyles(hwnd);

            // 启动阶段立即插入到 Progman 正上方：在壁纸/桌面之上、普通应用之下。
            _isAttachedToDesktop = NativeMethods.TryAttachWindowToDesktop(hwnd, out _desktopHostHandle);

            // 显示器几何与 Progman 是否可用无关。即使首次桌面层附着失败，也必须在
            // 加载布局前建立完整虚拟桌面坐标，避免稍后附着成功时发生坐标系跳变。
            UpdateDesktopHostBounds(hwnd);
            if (!_isAttachedToDesktop)
            {
                // 极少数精简版 Explorer 或第三方 Shell 不提供 Progman，退回 HWND_BOTTOM。
                NativeMethods.SendWindowToBottom(hwnd);
            }

            // 窗口在完成桌面 Z 序定位或底层回退之前保持透明，避免启动瞬间闪到应用上方。
            Opacity = 1;

            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(WindowProc);

            // 主窗口保持 WS_EX_NOACTIVATE，不能依赖 WPF 键盘焦点。输入监听只观察
            // Ctrl+Z/Esc；仅在命令可执行时消费该次按键，其他输入继续传给系统。
            _desktopKeyboardMonitor = NativeMethods.WatchDesktopKeyboardCommands(
                hwnd,
                desktopHostProvider: () => _desktopHostHandle,
                DesktopKeyboardCommandReceived);
            if (_desktopKeyboardMonitor == null)
            {
                _diagnostics.Log("KEYBOARD_COMMAND monitor unavailable; buttons remain available");
            }

            // 事件驱动地修复外部应用启动后的 Z 序。普通窗口 SHOW 事件数量很大，
            // 只使用前台切换和桌面菜单生命周期；低频巡检仅作为 Explorer 重启等
            // 异常场景的兜底，避免反复移动全屏整理层。
            _externalWindowMonitor = NativeMethods.WatchExternalWindowEvents(
                ExternalWindowEventReceived);

            _trayIcon = new TrayIconService(
                hwnd,
                isControlPanelVisible: () => ControlPanel.Visibility == Visibility.Visible,
                isOrganizerPaused: () => _organizerPaused,
                isAutoStartEnabled: StartupManager.IsEnabledForCurrentExecutable,
                toggleControlPanel: ToggleControlPanel,
                togglePause: () => SetOrganizerPaused(!_organizerPaused),
                refresh: RefreshDesktop,
                toggleAutoStart: ToggleAutoStartFromTray,
                exit: RequestExit,
                registrationFailed: HandleTrayIconRegistrationFailure);
            _trayIcon.Initialize();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RecoverNativeIconsAfterPreviousCrash();
            LoadLayout();
            InitializeFileOperationJournal();
            SnapToGridToggle.IsChecked = _appLayout.SnapToGrid;
            PushReflowToggle.IsChecked = _appLayout.PushReflowEnabled;
            AutoClassifyNewItemsToggle.IsChecked = _appLayout.AutoClassifyNewItems;
            EditModeToggle.IsChecked = _appLayout.IsEditMode;
            EditModeToggle.Content = _appLayout.IsEditMode ? "完成编辑" : "编辑布局";
            AutoCollapsePanelToggle.IsChecked = _appLayout.AutoCollapseControlPanel;
            AutoStartToggle.IsChecked = StartupManager.IsEnabledForCurrentExecutable();
            SafeModeToggle.IsChecked = false;
            CompactGroupLayoutToggle.IsChecked = _appLayout.CompactGroupLayout;
            ReserveWorkspaceToggle.IsChecked = _appLayout.ReserveTemporaryWorkspace;
            ApplySafeModeState(showStatus: false, updateWatchers: false);
            UpdatePushReflowAvailability();
            UpdateAutoClassificationControls();
            UpdateInboxButton();
            InitializeRecycleBinWidget();
            await RefreshDesktopSnapshotAsync(clearIconCache: false, statusMessage: null);
            if (_isClosing || _lifetimeCts.IsCancellationRequested || Dispatcher.HasShutdownStarted)
            {
                return;
            }

            ApplyControlPanelPosition();
            if (!_startQuietly)
            {
                ControlPanel.Visibility = Visibility.Visible;
            }
            ScheduleControlPanelClamp(DispatcherPriority.Loaded);
            if (!_isSafeModeActive)
            {
                StartDesktopWatchers();
            }

            // Explorer 在登录后的早期阶段可能尚未建立最终 Progman Z 序。Loaded 时再验证一次，
            // 只有真正定位到可靠桌面层后才隐藏原生图标，避免定位失败时
            // 留下“只有壁纸、没有任何图标或整理面板”的空白桌面。
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (!_isAttachedToDesktop ||
                !NativeMethods.IsWindowAttachedToDesktop(hwnd, _desktopHostHandle))
            {
                _isAttachedToDesktop = NativeMethods.TryAttachWindowToDesktop(hwnd, out _desktopHostHandle);
                if (_isAttachedToDesktop)
                {
                    UpdateDesktopHostBounds(hwnd, remapExistingLayout: true);
                }
            }

            _nativeIconsWereVisible = _isAttachedToDesktop && NativeMethods.HideNativeDesktopIcons();
            if (_nativeIconsWereVisible)
            {
                WriteSessionMarker();
            }

            if (_isAttachedToDesktop)
            {
                // 隐藏 SysListView32 或加载 Spotlight/桌面搜索组件，都可能让 Explorer
                // 异步重排顶级 Z 序。无论用户原本是否显示系统图标，都要再次校正。
                _isAttachedToDesktop = NativeMethods.EnsureWindowInDesktopLayer(
                    hwnd,
                    _desktopHostHandle,
                    frameChanged: true);
                if (_isAttachedToDesktop)
                {
                    _ = EnsureDesktopLayerAfterShellSettlesAsync();
                }
                else
                {
                    // 只有本程序实际隐藏过系统图标时才恢复，避免改变用户原本的
                    // “显示桌面图标”设置。
                    if (_nativeIconsWereVisible)
                    {
                        NativeMethods.RestoreNativeDesktopIcons();
                        _nativeIconsWereVisible = false;
                        TryDeleteSessionMarker();
                    }
                    StatusText.Text = "桌面层定位失败，已保留系统桌面状态";
                }
            }
            else
            {
                StatusText.Text = "桌面宿主尚未就绪，保留系统图标并将在后台重试";
            }

            _nativeIconGuardTimer.Start();
            _lastHealthTickUtc = DateTime.UtcNow;
            _healthMonitorTimer.Start();
            _diagnostics.Log($"START version=1.12.15, safeMode={_isSafeModeActive}, monitors={_desktopGeometry.Monitors.Count}, mixedDpi={_desktopGeometry.HasMixedDpi}");
            _trayIcon?.RefreshToolTip();

            if (QuietStartupVisibilityPolicy.ShouldHideControlPanel(
                    _startQuietly,
                    _trayIcon?.IsRegistered == true))
            {
                HideControlPanel(showRestoreButton: false);
            }
            else if (_startQuietly && ControlPanel.Visibility != Visibility.Visible)
            {
                HandleTrayIconRegistrationFailure();
            }
        }

        private void HandleTrayIconRegistrationFailure()
        {
            _diagnostics.Log("TRAY registration failed; control panel restored");
            ShowControlPanel("通知区域图标不可用，已显示控制面板");
        }

        private void ExternalWindowEventReceived(NativeMethods.ExternalWindowEvent windowEvent)
        {
            if (_isClosing || Dispatcher.HasShutdownStarted)
            {
                return;
            }

            if (windowEvent.Kind == NativeMethods.ExternalWindowEventKind.DesktopShellMenuOpened)
            {
                Volatile.Write(ref _desktopShellMenuActive, 1);
                Interlocked.Increment(ref _externalLayerGeneration);
                Interlocked.Increment(ref _shellMenuCloseGeneration);
                return;
            }

            if (windowEvent.Kind == NativeMethods.ExternalWindowEventKind.DesktopShellMenuClosed)
            {
                Volatile.Write(ref _desktopShellMenuActive, 0);
                int closeGeneration = Interlocked.Increment(ref _shellMenuCloseGeneration);
                Interlocked.Increment(ref _externalLayerGeneration);
                _ = RecheckDesktopLayerAfterShellMenuClosedAsync(closeGeneration);
                return;
            }

            if (windowEvent.Kind == NativeMethods.ExternalWindowEventKind.Shown)
            {
                if (NativeMethods.IsDesktopCompanionShowCandidate(
                        windowEvent.WindowHandle,
                        _desktopWindowHandle,
                        _desktopHostHandle))
                {
                    QueueDesktopCompanionLayerCorrection(windowEvent.WindowHandle);
                }
                return;
            }

            if (windowEvent.Kind != NativeMethods.ExternalWindowEventKind.Foreground)
            {
                return;
            }

            Interlocked.Increment(ref _externalLayerGeneration);
            QueueExternalLayerCorrection(windowEvent.WindowHandle, shouldActivate: true);
        }

        private void QueueDesktopCompanionLayerCorrection(IntPtr companionWindow)
        {
            if (_isClosing ||
                companionWindow == IntPtr.Zero ||
                Dispatcher.HasShutdownStarted)
            {
                return;
            }

            lock (_externalEventLock)
            {
                _pendingDesktopCompanionWindows.Add(companionWindow);
            }

            if (Interlocked.CompareExchange(
                    ref _desktopCompanionLayerCorrectionQueued,
                    1,
                    0) != 0)
            {
                return;
            }

            _ = ProcessDesktopCompanionLayerCorrectionsAfterShownAsync();
        }

        private async Task ProcessDesktopCompanionLayerCorrectionsAfterShownAsync()
        {
            try
            {
                // SHOW 通知可能早于窗口最终扩展样式和矩形。整个批次只建立一个
                // 延迟链：先在样式通常稳定后检查，再给应用一次最终 Z 序提交机会。
                await Task.Delay(80, _lifetimeCts.Token);
                await Task.Run(
                    () => ProcessPendingDesktopCompanionLayerCorrections(clear: false),
                    _lifetimeCts.Token);

                await Task.Delay(420, _lifetimeCts.Token);
                await Task.Run(
                    () => ProcessPendingDesktopCompanionLayerCorrections(clear: true),
                    _lifetimeCts.Token);
            }
            catch (OperationCanceledException)
            {
                // 正常退出。
            }
            finally
            {
                Interlocked.Exchange(ref _desktopCompanionLayerCorrectionQueued, 0);

                bool hasPending;
                lock (_externalEventLock)
                {
                    hasPending = _pendingDesktopCompanionWindows.Count > 0;
                }

                if (hasPending &&
                    !_isClosing &&
                    Interlocked.CompareExchange(
                        ref _desktopCompanionLayerCorrectionQueued,
                        1,
                        0) == 0)
                {
                    _ = ProcessDesktopCompanionLayerCorrectionsAfterShownAsync();
                }
            }
        }

        private void ProcessPendingDesktopCompanionLayerCorrections(bool clear)
        {
            IntPtr[] pendingWindows;
            lock (_externalEventLock)
            {
                pendingWindows = _pendingDesktopCompanionWindows.ToArray();
                if (clear)
                {
                    _pendingDesktopCompanionWindows.Clear();
                }
            }

            var orderedCandidates = pendingWindows
                .Select(window => new
                {
                    Window = window,
                    Depth = NativeMethods.GetDesktopBandDepth(
                        window,
                        _desktopWindowHandle,
                        _desktopHostHandle)
                })
                .Where(candidate => candidate.Depth >= 0)
                .OrderBy(candidate => candidate.Depth);

            IntPtr[] orderedWindows = orderedCandidates
                .Select(candidate => candidate.Window)
                .ToArray();
            if (orderedWindows.Length == 0 ||
                _isClosing ||
                _desktopWindowHandle == IntPtr.Zero)
            {
                return;
            }

            int placedCount = NativeMethods.TryPlaceDesktopCompanionsAboveOrganizer(
                orderedWindows,
                _desktopWindowHandle,
                _desktopHostHandle);
            if (placedCount > 0)
            {
                _diagnostics.Log(
                    $"COMPANION_LAYER requested windows={placedCount}");
            }
        }

        private async Task RecheckDesktopLayerAfterShellMenuClosedAsync(int closeGeneration)
        {
            try
            {
                // 等待 Shell 销毁菜单 Popup 并自然恢复 Z 序。菜单仍显示时绝不移动
                // 全屏整理层；多数情况下无需任何 SetWindowPos，因此不会闪烁。
                await Task.Delay(240, _lifetimeCts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_isClosing ||
                Dispatcher.HasShutdownStarted ||
                closeGeneration != Volatile.Read(ref _shellMenuCloseGeneration) ||
                Volatile.Read(ref _desktopShellMenuActive) != 0)
            {
                return;
            }

            IntPtr foregroundWindow = NativeMethods.GetForegroundRootWindow();
            QueueExternalLayerCorrection(foregroundWindow, shouldActivate: false);
        }

        private void QueueExternalLayerCorrection(IntPtr externalWindow, bool shouldActivate)
        {
            if (_isClosing || Dispatcher.HasShutdownStarted)
            {
                return;
            }

            lock (_externalEventLock)
            {
                if (externalWindow != IntPtr.Zero)
                {
                    _pendingExternalWindowHandle = externalWindow;
                }
                _pendingExternalWindowShouldActivate |= shouldActivate;
            }

            if (Volatile.Read(ref _desktopShellMenuActive) != 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _externalLayerCorrectionQueued, 1, 0) != 0)
            {
                return;
            }

            ScheduleExternalLayerCorrectionWorker();
        }

        private void ScheduleExternalLayerCorrectionWorker()
        {
            try
            {
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    new Action(ProcessPendingExternalLayerCorrection));
            }
            catch (InvalidOperationException)
            {
                Interlocked.Exchange(ref _externalLayerCorrectionQueued, 0);
            }
        }

        private void ProcessPendingExternalLayerCorrection()
        {
            if (Volatile.Read(ref _desktopShellMenuActive) != 0)
            {
                Interlocked.Exchange(ref _externalLayerCorrectionQueued, 0);
                return;
            }

            IntPtr pendingWindow;
            bool requestActivation;
            lock (_externalEventLock)
            {
                pendingWindow = _pendingExternalWindowHandle;
                requestActivation = _pendingExternalWindowShouldActivate;
                _pendingExternalWindowHandle = IntPtr.Zero;
                _pendingExternalWindowShouldActivate = false;
            }

            int generation = Volatile.Read(ref _externalLayerGeneration);
            try
            {
                CorrectDesktopLayerAfterExternalWindow(pendingWindow, requestActivation);
                _ = RecheckDesktopLayerAfterExternalWindowAsync(
                    generation,
                    pendingWindow,
                    requestActivation);
            }
            finally
            {
                Interlocked.Exchange(ref _externalLayerCorrectionQueued, 0);

                bool hasPending;
                lock (_externalEventLock)
                {
                    hasPending = _pendingExternalWindowHandle != IntPtr.Zero ||
                                 _pendingExternalWindowShouldActivate;
                }

                if (hasPending && !_isClosing &&
                    Interlocked.CompareExchange(ref _externalLayerCorrectionQueued, 1, 0) == 0)
                {
                    ScheduleExternalLayerCorrectionWorker();
                }
            }
        }

        private void CorrectDesktopLayerAfterExternalWindow(
            IntPtr externalWindow,
            bool requestExternalActivation = false)
        {
            if (_isClosing ||
                IsUserInteractionActive() ||
                Volatile.Read(ref _desktopShellMenuActive) != 0 ||
                NativeMethods.IsTransientShellUiWindow(externalWindow, _desktopHostHandle))
            {
                return;
            }

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            bool desktopWasCorrect = _isAttachedToDesktop &&
                                     NativeMethods.IsWindowAttachedToDesktop(
                                         hwnd,
                                         _desktopHostHandle);
            bool externalWasAbove = NativeMethods.IsExternalWindowAboveOrganizer(
                externalWindow,
                hwnd,
                _desktopHostHandle);

            // 只要整理层仍位于 Progman 上方就不再调用 SetWindowPos。右键菜单和
            // Spotlight 等 Shell 窗口可以短暂插在中间，无需强制整层重绘。
            bool repaired = desktopWasCorrect;
            if (!desktopWasCorrect)
            {
                if (_isAttachedToDesktop && _desktopHostHandle != IntPtr.Zero)
                {
                    repaired = NativeMethods.EnsureWindowInDesktopLayer(hwnd, _desktopHostHandle);
                }
                else
                {
                    repaired = NativeMethods.TryAttachWindowToDesktop(hwnd, out _desktopHostHandle);
                }
            }

            _isAttachedToDesktop = repaired;
            bool externalVisible = externalWindow != IntPtr.Zero &&
                                   NativeMethods.EnsureExternalWindowAboveOrganizer(
                                       externalWindow,
                                       hwnd,
                                       _desktopHostHandle,
                                       requestExternalActivation);

            // EVENT_OBJECT_SHOW 非常频繁。只有真正改变过 Z 序时才写日志，避免在 UI 线程
            // 上为每个系统弹窗同步追加文件，长期运行时造成磁盘 I/O 和 Dispatcher 延迟。
            bool layerChanged = !desktopWasCorrect && repaired;
            bool externalChanged = !externalWasAbove && externalVisible;
            if (layerChanged || externalChanged)
            {
                _diagnostics.Log(
                    $"LAYER corrected external=0x{externalWindow.ToInt64():X}, " +
                    $"desktopChanged={layerChanged}, externalChanged={externalChanged}");
            }
        }

        private async Task RecheckDesktopLayerAfterExternalWindowAsync(
            int generation,
            IntPtr externalWindow,
            bool requestExternalActivation)
        {
            // WinUI 应用成为前台后仍可能异步完成最终 Z 序，因此对前台事件执行
            // 短延迟复查，但不再对所有 SHOW 事件启动多轮任务。
            int[] delays = [80, 260, 650];
            foreach (int delay in delays)
            {
                try
                {
                    await Task.Delay(delay, _lifetimeCts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (_isClosing ||
                    generation != Volatile.Read(ref _externalLayerGeneration) ||
                    Volatile.Read(ref _desktopShellMenuActive) != 0 ||
                    Dispatcher.HasShutdownStarted)
                {
                    return;
                }

                try
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!IsUserInteractionActive())
                        {
                            CorrectDesktopLayerAfterExternalWindow(
                                externalWindow,
                                requestExternalActivation);
                        }
                    }, DispatcherPriority.ContextIdle, _lifetimeCts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted)
                {
                    return;
                }
            }
        }

        private async Task EnsureDesktopLayerAfterShellSettlesAsync()
        {
            // Explorer/Spotlight/桌面搜索组件会在登录和隐藏原生图标后异步调整 Z 序。
            // 只在启动后的短窗口内复查，不使用高频永久定时器，避免干扰鼠标操作。
            int[] delays = [120, 450, 1200];
            foreach (int delay in delays)
            {
                try
                {
                    await Task.Delay(delay, _lifetimeCts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (_isClosing || Dispatcher.HasShutdownStarted)
                {
                    return;
                }

                try
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (Volatile.Read(ref _desktopShellMenuActive) != 0 ||
                            IsUserInteractionActive())
                        {
                            return;
                        }

                        IntPtr currentHandle = new WindowInteropHelper(this).Handle;
                        if (_isAttachedToDesktop && _desktopHostHandle != IntPtr.Zero)
                        {
                            if (!NativeMethods.IsWindowAttachedToDesktop(
                                    currentHandle,
                                    _desktopHostHandle))
                            {
                                _isAttachedToDesktop = NativeMethods.EnsureWindowInDesktopLayer(
                                    currentHandle,
                                    _desktopHostHandle);
                            }
                        }
                        else
                        {
                            _isAttachedToDesktop = NativeMethods.TryAttachWindowToDesktop(
                                currentHandle,
                                out _desktopHostHandle);
                        }
                    }, DispatcherPriority.ContextIdle, _lifetimeCts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted)
                {
                    return;
                }
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _isClosing = true;
            CancelAllPortalReads();
            _lifetimeCts.Cancel();
            lock (_refreshDebounceLock)
            {
                _refreshDebounceCts?.Cancel();
                _refreshDebounceCts?.Dispose();
                _refreshDebounceCts = null;
            }

            _pendingPanelClampOperation?.Abort();
            _pendingPanelClampOperation = null;
            _layoutSaveTimer.Stop();
            _nativeIconGuardTimer.Stop();
            _controlPanelAutoCollapseTimer.Stop();
            _healthMonitorTimer.Stop();
            _recycleBinStatusTimer.Stop();
            StopDesktopWatchers();
            _refreshCancellationEpoch.Dispose();
            StopAsyncIconLoading();
            _fileOperationService.Dispose();
            _diagnostics.Log("STOP application closing");
            ResetAllInteractionState(restoreDraggedVisual: true);
            SaveLayoutNow();
            RestoreDesktopState();

            lock (_externalEventLock)
            {
                _pendingExternalWindowHandle = IntPtr.Zero;
                _pendingExternalWindowShouldActivate = false;
                _pendingDesktopCompanionWindows.Clear();
            }
            Interlocked.Exchange(ref _desktopCompanionLayerCorrectionQueued, 0);
            _desktopWindowHandle = IntPtr.Zero;
            _externalWindowMonitor?.Dispose();
            _externalWindowMonitor = null;

            _desktopKeyboardMonitor?.Dispose();
            _desktopKeyboardMonitor = null;

            _trayIcon?.Dispose();
            _trayIcon = null;

            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WindowProc);
                _hwndSource = null;
            }
        }

        internal void LogFatalException(Exception exception)
        {
            _diagnostics.Log($"FATAL {exception}");
        }

        public void RestoreDesktopState()
        {
            if (Interlocked.Exchange(ref _desktopStateRestoreStarted, 1) == 1)
            {
                return;
            }

            if (_nativeIconsWereVisible)
            {
                NativeMethods.RestoreNativeDesktopIcons();
            }

            TryDeleteSessionMarker();
        }

        private async Task ProcessDisplayGeometryUpdateAsync()
        {
            try
            {
                // Windows 会为一次分辨率、缩放或任务栏变化连续广播多条消息。
                // 等 Shell 稳定后只提交一次，避免全屏透明窗口反复调整尺寸。
                await Task.Delay(180, _lifetimeCts.Token).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_isClosing)
                    {
                        return;
                    }

                    if (IsUserInteractionActive())
                    {
                        ResetAllInteractionState(restoreDraggedVisual: true);
                    }

                    IntPtr currentHandle = new WindowInteropHelper(this).Handle;
                    bool geometryChanged = UpdateDesktopHostBounds(
                        currentHandle,
                        remapExistingLayout: true);
                    if (geometryChanged)
                    {
                        // 显示器变化只影响坐标和工作区，不需要重新扫描文件系统或
                        // 清空图标缓存。直接复用现有视觉并更新位置。
                        RebuildDesktopIcons();
                    }

                    ScheduleControlPanelClamp(DispatcherPriority.ContextIdle);
                    ClampRecycleBinWidgetToDesktop(updateLayout: true);
                }, DispatcherPriority.Background, _lifetimeCts.Token);
            }
            catch (OperationCanceledException)
            {
                // 正常退出或后续生命周期取消。
            }
            catch (Exception exception)
            {
                _diagnostics.Log($"DISPLAY update failed: {exception}");
                if (!_isClosing && !Dispatcher.HasShutdownStarted)
                {
                    _ = Dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(() => StatusText.Text = "显示配置更新失败，可点击刷新重试"));
                }
            }
            finally
            {
                Interlocked.Exchange(ref _displayUpdateQueued, 0);
            }
        }

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_trayIcon?.HandleWindowMessage(msg, lParam) == true)
            {
                handled = true;
                return IntPtr.Zero;
            }

            if (DesktopWindowMessagePolicy.IsGeometryChangeMessage(msg, wParam))
            {
                if (Interlocked.CompareExchange(ref _displayUpdateQueued, 1, 0) == 0)
                {
                    _ = ProcessDisplayGeometryUpdateAsync();
                }
                return IntPtr.Zero;
            }

            // 保持桌面整理层不抢前台焦点，同时明确保留本次鼠标消息。
            // MA_NOACTIVATE 与 MA_NOACTIVATEANDEAT 的区别是后者会吞掉点击。
            if (msg == WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(MA_NOACTIVATE);
            }

            if (msg != WM_NCHITTEST)
            {
                return IntPtr.Zero;
            }

            long raw = lParam.ToInt64();
            int screenX = unchecked((short)(raw & 0xFFFF));
            int screenY = unchecked((short)((raw >> 16) & 0xFFFF));
            Point clientPoint = PointFromScreen(new Point(screenX, screenY));

            // 不再在每次鼠标移动时执行 InputHitTest 深度遍历。全屏透明 WPF 窗口中，
            // 深度命中会遍历图标、文本、滚动条和特效，是卡顿的主要来源。
            handled = true;
            return new IntPtr(IsInteractiveClientPoint(clientPoint) ? HTCLIENT : HTTRANSPARENT);
        }

        private bool IsInteractiveClientPoint(Point point)
        {
            if (ControlPanelRestoreButton.Visibility == Visibility.Visible)
            {
                double buttonWidth = GetRenderedLength(
                    ControlPanelRestoreButton.ActualWidth,
                    ControlPanelRestoreButton.Width,
                    ControlPanelRestoreButton.DesiredSize.Width);
                double buttonHeight = GetRenderedLength(
                    ControlPanelRestoreButton.ActualHeight,
                    ControlPanelRestoreButton.Height,
                    ControlPanelRestoreButton.DesiredSize.Height);
                double left = Math.Max(0, ControlPanelRestoreButton.Margin.Left);
                double top = Math.Max(0, ControlPanelRestoreButton.Margin.Top);

                if (buttonWidth > 0 && buttonHeight > 0 &&
                    new Rect(left, top, buttonWidth, buttonHeight).Contains(point))
                {
                    return true;
                }
            }

            if (RecycleBinWidget.Visibility == Visibility.Visible)
            {
                Rect widgetBounds = GetRecycleBinWidgetBounds();
                if (widgetBounds.Width > 0 && widgetBounds.Height > 0 && widgetBounds.Contains(point))
                {
                    return true;
                }
            }

            if (ControlPanel.Visibility == Visibility.Visible)
            {
                Point panelPosition = GetControlPanelPosition();
                double panelWidth = GetRenderedLength(
                    ControlPanel.ActualWidth,
                    ControlPanel.Width,
                    ControlPanel.DesiredSize.Width);
                double panelHeight = GetRenderedLength(
                    ControlPanel.ActualHeight,
                    ControlPanel.Height,
                    ControlPanel.DesiredSize.Height);

                if (panelWidth > 0 && panelHeight > 0 &&
                    new Rect(panelPosition.X, panelPosition.Y, panelWidth, panelHeight).Contains(point))
                {
                    return true;
                }
            }

            // IconCanvas 的直接子项只有“自由图标”和“分组容器”。
            // 分组内部的按钮、滚动条和图标全部包含在分组矩形内，无需逐层遍历视觉树。
            for (int index = IconCanvas.Children.Count - 1; index >= 0; index--)
            {
                if (IconCanvas.Children[index] is not FrameworkElement element ||
                    element.Visibility != Visibility.Visible ||
                    !element.IsHitTestVisible)
                {
                    continue;
                }

                double left = SafeCanvasCoordinate(Canvas.GetLeft(element));
                double top = SafeCanvasCoordinate(Canvas.GetTop(element));
                double width = GetRenderedLength(element.ActualWidth, element.Width, element.DesiredSize.Width);
                double height = GetRenderedLength(element.ActualHeight, element.Height, element.DesiredSize.Height);

                if (width > 0 && height > 0 && new Rect(left, top, width, height).Contains(point))
                {
                    return true;
                }
            }

            return false;
        }

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static double GetRenderedLength(double actual, double explicitLength, double desired)
        {
            if (double.IsFinite(actual) && actual > 0)
            {
                return actual;
            }

            if (double.IsFinite(explicitLength) && explicitLength > 0)
            {
                return explicitLength;
            }

            return double.IsFinite(desired) && desired > 0 ? desired : 0;
        }

    }
}
