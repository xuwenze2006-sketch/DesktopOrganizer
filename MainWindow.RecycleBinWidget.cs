// 回收站专属小组件：状态、拖动、打开、清空与持久化
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private sealed record JournaledRecycleBinEmptyCompletion(
            int Result,
            bool Executed,
            bool JournalPersisted,
            string? ErrorMessage);

        private static readonly string RecycleBinWidgetLocation = ShellItemLocation.Encode(
            ShellDesktopItemPolicy.RecycleBinParsingName,
            isFolder: true);

        private void InitializeRecycleBinWidget()
        {
            EnsureRecycleBinWidgetIcon();

            RecycleBinWidgetToggle.IsChecked = _appLayout.RecycleBinWidget.IsVisible;
            ApplyRecycleBinWidgetPosition();
            UpdateRecycleBinWidgetVisibility();
            ApplyRecycleBinStatus(new RecycleBinStatus(false, 0, 0, 0));
            RequestRecycleBinStatusRefresh();
            _recycleBinStatusTimer.Start();
        }

        private void EnsureRecycleBinWidgetIcon(bool forceReload = false)
        {
            if (forceReload)
            {
                RecycleBinWidgetIconHost.Content = null;
            }

            if (RecycleBinWidgetIconHost.Content == null)
            {
                RecycleBinWidgetIconHost.Content = CreateAsyncIconElement(
                    RecycleBinWidgetLocation,
                    "回收站",
                    parentGroup: null,
                    iconSize: 56,
                    shellPlaceholder: "♻");
            }
        }

        private void RecycleBinStatusTimer_Tick(object? sender, EventArgs e)
        {
            if (!_organizerPaused && _appLayout.RecycleBinWidget.IsVisible)
            {
                RequestRecycleBinStatusRefresh();
            }
        }

        private void RequestRecycleBinStatusRefresh()
        {
            if (_isClosing ||
                _lifetimeCts.IsCancellationRequested ||
                Interlocked.CompareExchange(ref _recycleBinStatusRefreshRunning, 1, 0) != 0)
            {
                return;
            }

            _ = RefreshRecycleBinStatusAsync();
        }

        private async Task RefreshRecycleBinStatusAsync()
        {
            try
            {
                RecycleBinStatus status = await Task.Run(
                    NativeMethods.QueryRecycleBinStatus,
                    _lifetimeCts.Token).ConfigureAwait(false);
                if (_isClosing || Dispatcher.HasShutdownStarted)
                {
                    return;
                }

                await Dispatcher.InvokeAsync(
                    () => ApplyRecycleBinStatus(status),
                    DispatcherPriority.Background,
                    _lifetimeCts.Token);
            }
            catch (OperationCanceledException) when (
                _isClosing || _lifetimeCts.IsCancellationRequested)
            {
                // 正常退出。
            }
            catch (Exception exception)
            {
                _diagnostics.Log($"RECYCLE_WIDGET query failed: {exception.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _recycleBinStatusRefreshRunning, 0);
            }
        }

        private void ApplyRecycleBinStatus(RecycleBinStatus status)
        {
            if (_isClosing)
            {
                return;
            }

            bool previousStatusWasAvailable = _recycleBinStatus.IsAvailable;
            bool previousStatusWasEmpty = previousStatusWasAvailable &&
                _recycleBinStatus.ItemCount == 0;
            _recycleBinStatus = status;
            if (!status.IsAvailable)
            {
                RecycleBinWidgetCountText.Text = "状态暂不可用";
                RecycleBinWidgetSizeText.Text = "仍可打开回收站";
                RecycleBinWidgetStatusDot.Background = RecycleBinUnavailableBrush;
                RecycleBinEmptyButton.IsEnabled = false;
                RecycleBinContextEmptyMenuItem.IsEnabled = false;
                return;
            }

            bool isEmpty = status.ItemCount == 0;
            if (!previousStatusWasAvailable || previousStatusWasEmpty != isEmpty)
            {
                // Explorer 的回收站图标会区分空/非空。只在状态跨越空与非空时
                // 失效这一项缓存，避免四秒轮询不断重新提取 Shell 图标。
                InvalidateIconCacheLocations([RecycleBinWidgetLocation]);
                EnsureRecycleBinWidgetIcon(forceReload: true);
            }

            RecycleBinWidgetCountText.Text = isEmpty
                ? "回收站为空"
                : $"{status.ItemCount:N0} 项待清理";
            RecycleBinWidgetSizeText.Text = isEmpty
                ? "桌面保持清爽"
                : $"占用 {FormatRecycleBinSize(status.SizeBytes)}";
            RecycleBinWidgetStatusDot.Background = isEmpty
                ? RecycleBinEmptyBrush
                : RecycleBinOccupiedBrush;
            bool canEmpty = !isEmpty && !_isRecycleBinEmptying;
            RecycleBinEmptyButton.IsEnabled = canEmpty;
            RecycleBinContextEmptyMenuItem.IsEnabled = canEmpty;
        }

        private static string FormatRecycleBinSize(long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }

            string[] units = ["KB", "MB", "GB", "TB"];
            double value = bytes;
            int unitIndex = -1;
            do
            {
                value /= 1024;
                unitIndex++;
            }
            while (value >= 1024 && unitIndex < units.Length - 1);

            return value >= 100
                ? $"{value:0} {units[unitIndex]}"
                : $"{value:0.#} {units[unitIndex]}";
        }

        private void RecycleBinOpenButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            OpenDesktopItem(RecycleBinWidgetLocation);
        }

        private void RecycleBinWidget_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left ||
                e.ClickCount < 2 ||
                IsPointerOverButton(e.OriginalSource as DependencyObject))
            {
                return;
            }

            e.Handled = true;
            OpenDesktopItem(RecycleBinWidgetLocation);
        }

        private async void RecycleBinEmptyButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (_isRecycleBinEmptying ||
                !_recycleBinStatus.IsAvailable ||
                _recycleBinStatus.ItemCount == 0)
            {
                return;
            }

            if (!EnsureFileOperationJournalAvailable("清空回收站"))
            {
                return;
            }

            MessageBoxResult confirmation = MessageBox.Show(
                $"确定永久删除回收站中的 {_recycleBinStatus.ItemCount:N0} 个项目吗？\n\n此操作无法通过 DesktopOrganizer 撤销。",
                "清空回收站",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            if (!TryReserveFileOperation(
                    [RecycleBinWidgetLocation],
                    "已有真实文件操作正在进行，请完成后再清空回收站",
                    out List<string> reservedKeys))
            {
                return;
            }

            var journalEntry = new FileOperationJournalEntry
            {
                Kind = FileOperationJournalKind.EmptyRecycleBin,
                DisplayName = $"回收站中的 {_recycleBinStatus.ItemCount:N0} 个项目",
                IsAggregate = true
            };
            if (!TryPersistQueuedJournalEntries([journalEntry], out string queueError))
            {
                ReleaseFileOperation(reservedKeys);
                StatusText.Text = "未排队清空回收站：操作账本无法安全写入";
                _diagnostics.Log($"RECYCLE_WIDGET queue journal failed error={queueError}");
                return;
            }

            _isRecycleBinEmptying = true;
            RecycleBinEmptyButton.IsEnabled = false;
            RecycleBinContextEmptyMenuItem.IsEnabled = false;
            RecycleBinEmptyButton.Content = "清理中…";
            RecycleBinWidgetCountText.Text = "正在清空回收站";
            StatusText.Text = "回收站清理任务已排队";
            JournaledRecycleBinEmptyCompletion completion;
            try
            {
                IntPtr owner = new WindowInteropHelper(this).Handle;
                completion = await _fileOperationService.Enqueue(
                    token =>
                    {
                        int shellResult = unchecked((int)0x80004005);
                        FileOperationDispatchResult dispatch = DispatchJournaledOperation(
                            journalEntry,
                            () =>
                            {
                                token.ThrowIfCancellationRequested();
                                shellResult = NativeMethods.EmptyRecycleBin(owner);
                                if (shellResult >= 0)
                                {
                                    return FileOperationExecutionOutcome.Success();
                                }

                                Exception? shellError = Marshal.GetExceptionForHR(shellResult);
                                return FileOperationExecutionOutcome.Failure(
                                    $"HRESULT_0x{shellResult:X8}",
                                    shellError?.Message ?? $"Windows Shell 返回错误 0x{shellResult:X8}。");
                            });
                        return new JournaledRecycleBinEmptyCompletion(
                            shellResult,
                            dispatch.Executed,
                            dispatch.JournalPersisted,
                            dispatch.ErrorMessage);
                    },
                    _lifetimeCts.Token);
            }
            catch (OperationCanceledException)
            {
                completion = new JournaledRecycleBinEmptyCompletion(
                    unchecked((int)0x800704C7),
                    Executed: false,
                    JournalPersisted: false,
                    ErrorMessage: "操作已取消。");
            }
            catch (Exception exception)
            {
                _diagnostics.Log($"RECYCLE_WIDGET empty failed: {exception}");
                completion = new JournaledRecycleBinEmptyCompletion(
                    exception.HResult,
                    Executed: false,
                    JournalPersisted: false,
                    ErrorMessage: exception.Message);
            }
            finally
            {
                _isRecycleBinEmptying = false;
                RecycleBinEmptyButton.Content = "清空";
            }

            ReleaseFileOperation(reservedKeys);

            if (_isClosing || Dispatcher.HasShutdownStarted)
            {
                return;
            }

            if (completion.Executed && completion.Result >= 0)
            {
                ApplyRecycleBinStatus(new RecycleBinStatus(true, 0, 0, 0));
                StatusText.Text = completion.JournalPersisted
                    ? "回收站已清空；该操作不可撤销，结果已记入操作中心"
                    : "回收站已清空，但账本终态未能保存；请人工核对";
                _diagnostics.Log($"RECYCLE_WIDGET empty success journalPersisted={completion.JournalPersisted}");
            }
            else
            {
                ApplyRecycleBinStatus(_recycleBinStatus);
                Exception? error = Marshal.GetExceptionForHR(completion.Result);
                StatusText.Text = "回收站未能清空";
                MessageBox.Show(
                    completion.ErrorMessage ?? error?.Message ??
                    $"Windows Shell 返回错误 0x{completion.Result:X8}。",
                    "清空回收站失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            RequestRecycleBinStatusRefresh();
        }

        private void RecycleBinWidgetToggle_Click(object sender, RoutedEventArgs e)
        {
            _appLayout.RecycleBinWidget.IsVisible = RecycleBinWidgetToggle.IsChecked == true;
            UpdateRecycleBinWidgetVisibility();
            SaveLayout();
            StatusText.Text = _appLayout.RecycleBinWidget.IsVisible
                ? "回收站组件已显示"
                : "回收站组件已隐藏；可在整理面板中恢复";
        }

        private void HideRecycleBinWidgetMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _appLayout.RecycleBinWidget.IsVisible = false;
            RecycleBinWidgetToggle.IsChecked = false;
            UpdateRecycleBinWidgetVisibility();
            SaveLayout();
            StatusText.Text = "回收站组件已隐藏；可在整理面板中恢复";
        }

        private void UpdateRecycleBinWidgetVisibility()
        {
            ClearRecycleBinDropPreview();
            RecycleBinWidget.Visibility =
                !_organizerPaused && _appLayout.RecycleBinWidget.IsVisible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            if (RecycleBinWidget.Visibility == Visibility.Visible)
            {
                ClampRecycleBinWidgetToDesktop(updateLayout: true);
                RequestRecycleBinStatusRefresh();
            }
        }

        private void ApplyRecycleBinWidgetPosition()
        {
            RecycleBinWidget.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Rect primary = GetPrimaryWorkArea();
            double x = _appLayout.RecycleBinWidget.X ??
                Math.Max(primary.Left + 14, primary.Right - RecycleBinWidgetWidth - 18);
            double y = _appLayout.RecycleBinWidget.Y ??
                Math.Max(primary.Top + 14, primary.Bottom - RecycleBinWidgetHeight - 18);
            SetRecycleBinWidgetPosition(x, y, updateLayout: true);
        }

        private Point GetRecycleBinWidgetPosition()
        {
            Thickness margin = RecycleBinWidget.Margin;
            return new Point(SafeCanvasCoordinate(margin.Left), SafeCanvasCoordinate(margin.Top));
        }

        private Rect GetRecycleBinWidgetBounds()
        {
            Point position = GetRecycleBinWidgetPosition();
            double width = GetRenderedLength(
                RecycleBinWidget.ActualWidth,
                RecycleBinWidget.Width,
                RecycleBinWidget.DesiredSize.Width);
            double height = GetRenderedLength(
                RecycleBinWidget.ActualHeight,
                RecycleBinWidget.Height,
                RecycleBinWidget.DesiredSize.Height);
            return new Rect(position.X, position.Y, width, height);
        }

        private Rect? GetRecycleBinWidgetObstacle()
        {
            if (_organizerPaused ||
                !_appLayout.RecycleBinWidget.IsVisible ||
                RecycleBinWidget.Visibility != Visibility.Visible)
            {
                return null;
            }

            Rect bounds = GetRecycleBinWidgetBounds();
            bounds.Inflate(8, 8);
            return bounds;
        }

        private void ClampRecycleBinWidgetToDesktop(bool updateLayout = true)
        {
            Point position = GetRecycleBinWidgetPosition();
            SetRecycleBinWidgetPosition(position.X, position.Y, updateLayout);
        }

        private void SetRecycleBinWidgetPosition(double x, double y, bool updateLayout)
        {
            Point clamped = ClampRectToUsableDesktop(
                x,
                y,
                RecycleBinWidgetWidth,
                RecycleBinWidgetHeight);
            RecycleBinWidget.Margin = new Thickness(clamped.X, clamped.Y, 0, 0);
            if (updateLayout)
            {
                _appLayout.RecycleBinWidget.X = clamped.X;
                _appLayout.RecycleBinWidget.Y = clamped.Y;
            }
        }

        private void RecycleBinWidgetDragHandle_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left ||
                IsPointerOverButton(e.OriginalSource as DependencyObject))
            {
                return;
            }

            RecoverStaleInteractionState();
            _isRecycleBinWidgetDragging = true;
            _recycleBinWidgetDragMoved = false;
            _recycleBinWidgetDragStartMousePoint = e.GetPosition(RootGrid);
            _recycleBinWidgetDragStartPosition = GetRecycleBinWidgetPosition();
            if (!Mouse.Capture(RecycleBinWidgetDragHandle, CaptureMode.Element))
            {
                _isRecycleBinWidgetDragging = false;
                return;
            }

            e.Handled = true;
        }

        private void RecycleBinWidgetDragHandle_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isRecycleBinWidgetDragging || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point current = e.GetPosition(RootGrid);
            Vector delta = current - _recycleBinWidgetDragStartMousePoint;
            if (!_recycleBinWidgetDragMoved && delta.Length < 3)
            {
                return;
            }

            _recycleBinWidgetDragMoved = true;
            SetRecycleBinWidgetPosition(
                _recycleBinWidgetDragStartPosition.X + delta.X,
                _recycleBinWidgetDragStartPosition.Y + delta.Y,
                updateLayout: false);
            e.Handled = true;
        }

        private void RecycleBinWidgetDragHandle_PreviewMouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            bool moved = _recycleBinWidgetDragMoved;
            CompleteRecycleBinWidgetDrag();
            e.Handled = moved;
        }

        private void RecycleBinWidgetDragHandle_LostMouseCapture(object sender, MouseEventArgs e)
        {
            CompleteRecycleBinWidgetDrag();
        }

        private void CompleteRecycleBinWidgetDrag()
        {
            if (!_isRecycleBinWidgetDragging)
            {
                return;
            }

            bool moved = _recycleBinWidgetDragMoved;
            _isRecycleBinWidgetDragging = false;
            _recycleBinWidgetDragMoved = false;
            if (ReferenceEquals(Mouse.Captured, RecycleBinWidgetDragHandle))
            {
                Mouse.Capture(null);
            }

            if (moved)
            {
                ClampRecycleBinWidgetToDesktop(updateLayout: true);
                SaveLayout();
                StatusText.Text = "回收站组件位置已保存";
            }
        }
    }
}
