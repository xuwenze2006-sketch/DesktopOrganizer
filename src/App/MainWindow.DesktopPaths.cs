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
                SetStatusDetails(GetDesktopPathDescription());
            }
            return true;
        }

        private string GetDesktopPathDescription()
        {
            // 路径巡检、失败恢复和未变化快照也会更新详情，必须保留当前降级说明。
            string modeDescription = _isSafeModeActive
                ? "安全模式：文件变化约15秒同步，拖动或菜单结束后补同步；F5可立即刷新。\n" +
                  "自动归类、规则和挤压预览暂停，原设置保持。\n点击安全模式状态条可恢复实时模式。\n\n"
                : string.Empty;
            if (_desktopPaths is null)
                return modeDescription + "正在向 Windows 查询桌面位置…";

            string Describe(DesktopPathState entry)
            {
                string path = entry.Path ?? "未能确定位置";
                if (entry.IsAvailable)
                    return path;
                string previous = entry.LastKnownGoodPath is { Length: > 0 }
                    ? $"\n上次可用位置：{entry.LastKnownGoodPath}" : "";
                return $"{path}（{entry.ErrorMessage}）{previous}";
            }
            return modeDescription + $"用户桌面：{Describe(_desktopPaths.UserDesktop)}\n" +
                $"公共桌面：{Describe(_desktopPaths.CommonDesktop)}\n" +
                "位置由 Windows 提供；按 F5 可重新检查。";
        }

        // 由现有 15 秒巡检调用。安全模式仍同步基本文件集合，昂贵自动行为由各自门禁暂停。
        private Task CheckDesktopSynchronizationAsync()
        {
            if (_isClosing || _lifetimeCts.IsCancellationRequested || Dispatcher.HasShutdownStarted)
                return Task.CompletedTask;

            if (!_isSafeModeActive)
                return CheckDesktopPathChangesAsync();

            // 拖动/菜单期间等下一轮巡检，避免低频同步退化成 850ms 的扫描循环。
            if (!IsUserInteractionActive() && Volatile.Read(ref _desktopShellMenuActive) == 0)
                RequestDesktopRefresh(clearIconCache: false, statusMessage: null);
            return Task.CompletedTask;
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
