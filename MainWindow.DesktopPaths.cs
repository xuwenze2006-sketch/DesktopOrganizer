namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private string[] GetDesktopWatcherPaths()
        {
            if (_desktopPaths is null)
                return [];

            return new[] { _desktopPaths.UserDesktop, _desktopPaths.CommonDesktop }
                .Where(entry => entry.IsAvailable && !string.IsNullOrWhiteSpace(entry.Path))
                .Select(entry => entry.Path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        // 所有调用均在 UI 线程；迟到的旧扫描不能覆盖新位置或重绑回旧监听器。
        private bool ApplyDesktopPaths(DesktopPathSnapshot paths)
        {
            if (paths.Revision < _desktopPathResolver.Current.Revision ||
                (_desktopPaths is not null && paths.Revision < _desktopPaths.Revision))
                return false;

            bool changed = _desktopPaths?.Revision != paths.Revision;
            _desktopPaths = paths;
            if (!paths.IsAvailable)
                _desktopScanUnavailable = true;
            string[] wanted = GetDesktopWatcherPaths();
            bool watchersMatch = wanted.Length == _watchers.Count &&
                _watchers.All(watcher => watcher.EnableRaisingEvents &&
                    wanted.Contains(watcher.Path, StringComparer.OrdinalIgnoreCase));
            if (changed || !watchersMatch)
            {
                StartDesktopWatchers();
                StatusText.ToolTip = GetDesktopPathDescription();
            }
            return true;
        }

        private string GetDesktopPathDescription()
        {
            if (_desktopPaths is null)
                return "正在向 Windows 查询桌面位置…";

            string Describe(DesktopPathState entry)
            {
                string path = entry.Path ?? "未能确定位置";
                if (entry.IsAvailable)
                    return path;
                string previous = entry.LastKnownGoodPath is { Length: > 0 }
                    ? $"\n上次可用位置：{entry.LastKnownGoodPath}" : "";
                return $"{path}（{entry.ErrorMessage}）{previous}";
            }
            return $"用户桌面：{Describe(_desktopPaths.UserDesktop)}\n" +
                $"公共桌面：{Describe(_desktopPaths.CommonDesktop)}\n" +
                "位置由 Windows 提供；按 F5 可重新检查。";
        }

        private async Task CheckDesktopPathChangesAsync()
        {
            if (Interlocked.CompareExchange(ref _desktopPathCheckRunning, 1, 0) != 0)
                return;
            try
            {
                DesktopPathSnapshot paths = await Task.Run(
                    _desktopPathResolver.Refresh, _lifetimeCts.Token).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_isClosing || _isSafeModeActive)
                        return;
                    bool changed = _desktopPaths?.Revision != paths.Revision;
                    if (ApplyDesktopPaths(paths) && changed)
                        RequestDesktopRefresh(clearIconCache: false, statusMessage: null);
                }, DispatcherPriority.Background, _lifetimeCts.Token);
            }
            catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
            {
                // 正常退出。
            }
            catch (Exception exception)
            {
                _diagnostics.Log($"DESKTOP_PATH check failed: {exception.GetType().Name}");
            }
            finally
            {
                Interlocked.Exchange(ref _desktopPathCheckRunning, 0);
            }
        }
    }
}
