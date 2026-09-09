namespace DesktopOrganizer
{
    /// <summary>
    /// 为每个只读 Portal 监听当前目录的直属变化，并把短时间内的事件合并为一次刷新请求。
    /// 该协调器只发出通知，不读取目录，也不执行任何文件系统写操作。
    /// </summary>
    internal sealed class FolderPortalWatcherCoordinator : IDisposable
    {
        private sealed class Registration : IDisposable
        {
            public Registration(string directoryPath, FileSystemWatcher watcher)
            {
                DirectoryPath = directoryPath;
                Watcher = watcher;
            }

            public string DirectoryPath { get; }

            public FileSystemWatcher Watcher { get; }

            public Timer? DebounceTimer { get; set; }

            public long NotificationVersion { get; set; }

            public bool NeedsRebind { get; set; }

            public void Dispose()
            {
                Watcher.EnableRaisingEvents = false;
                Watcher.Dispose();
                DebounceTimer?.Dispose();
            }
        }

        private readonly object _gate = new();
        private readonly Dictionary<string, Registration> _registrations =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Action<string, long> _refreshRequested;
        private readonly TimeSpan _debounceDelay;
        private long _nextNotificationVersion;
        private bool _disposed;

        public FolderPortalWatcherCoordinator(
            Action<string, long> refreshRequested,
            TimeSpan? debounceDelay = null)
        {
            _refreshRequested = refreshRequested ??
                throw new ArgumentNullException(nameof(refreshRequested));
            _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(550);
            if (_debounceDelay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(debounceDelay));
            }
        }

        public bool TryBind(string portalId, string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(portalId) || string.IsNullOrWhiteSpace(directoryPath))
            {
                return false;
            }

            string normalizedPath;
            try
            {
                normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }

            if (!Directory.Exists(normalizedPath))
            {
                return false;
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    return false;
                }

                if (_registrations.TryGetValue(portalId, out Registration? existing) &&
                    existing.DirectoryPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                    !existing.NeedsRebind)
                {
                    return true;
                }
            }

            Unbind(portalId);
            FileSystemWatcher? watcher = null;
            try
            {
                var createdWatcher = new FileSystemWatcher(normalizedPath)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName |
                                   NotifyFilters.DirectoryName
                };
                watcher = createdWatcher;
                createdWatcher.Created += (_, _) => NotifyChanged(portalId, createdWatcher);
                createdWatcher.Deleted += (_, _) => NotifyChanged(portalId, createdWatcher);
                createdWatcher.Renamed += (_, _) => NotifyChanged(portalId, createdWatcher);
                createdWatcher.Error += (_, _) => NotifyError(portalId, createdWatcher);
            }
            catch (Exception exception) when (
                exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                watcher?.Dispose();
                return false;
            }

            var registration = new Registration(normalizedPath, watcher);
            registration.DebounceTimer = new Timer(
                _ => PublishRefresh(portalId, registration),
                null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);

            lock (_gate)
            {
                if (_disposed)
                {
                    registration.Dispose();
                    return false;
                }
                _registrations[portalId] = registration;
            }

            try
            {
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception exception) when (
                exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                Unbind(portalId);
                return false;
            }
            return true;
        }

        internal bool NotifyChanged(string portalId)
        {
            lock (_gate)
            {
                if (_disposed ||
                    !_registrations.TryGetValue(portalId, out Registration? registration))
                {
                    return false;
                }

                registration.NotificationVersion = NextNotificationVersion();
                registration.DebounceTimer?.Change(
                    _debounceDelay,
                    Timeout.InfiniteTimeSpan);
                return true;
            }
        }

        public bool IsBound(string portalId)
        {
            lock (_gate)
            {
                return !_disposed && _registrations.ContainsKey(portalId);
            }
        }

        internal bool IsCurrentNotification(string portalId, long notificationVersion)
        {
            lock (_gate)
            {
                return !_disposed &&
                       _registrations.TryGetValue(portalId, out Registration? registration) &&
                       registration.NotificationVersion == notificationVersion;
            }
        }

        public void CancelPending(string portalId)
        {
            lock (_gate)
            {
                if (!_disposed &&
                    _registrations.TryGetValue(portalId, out Registration? registration))
                {
                    registration.NotificationVersion = NextNotificationVersion();
                    registration.DebounceTimer?.Change(
                        Timeout.InfiniteTimeSpan,
                        Timeout.InfiniteTimeSpan);
                }
            }
        }

        internal string? GetBoundPath(string portalId)
        {
            lock (_gate)
            {
                return !_disposed &&
                       _registrations.TryGetValue(portalId, out Registration? registration)
                    ? registration.DirectoryPath
                    : null;
            }
        }

        public void Unbind(string portalId)
        {
            Registration? registration;
            lock (_gate)
            {
                _registrations.Remove(portalId, out registration);
            }
            registration?.Dispose();
        }

        public void UnbindAll()
        {
            List<Registration> registrations;
            lock (_gate)
            {
                registrations = _registrations.Values.ToList();
                _registrations.Clear();
            }
            foreach (Registration registration in registrations)
            {
                registration.Dispose();
            }
        }

        public void Dispose()
        {
            List<Registration> registrations;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                registrations = _registrations.Values.ToList();
                _registrations.Clear();
            }
            foreach (Registration registration in registrations)
            {
                registration.Dispose();
            }
        }

        private void PublishRefresh(string portalId, Registration registration)
        {
            long notificationVersion;
            lock (_gate)
            {
                if (_disposed ||
                    !_registrations.TryGetValue(portalId, out Registration? current) ||
                    !ReferenceEquals(current, registration))
                {
                    return;
                }
                notificationVersion = registration.NotificationVersion;
            }

            try
            {
                _refreshRequested(portalId, notificationVersion);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Folder Portal watcher callback failed: {exception}");
            }
        }

        private void NotifyChanged(string portalId, FileSystemWatcher sourceWatcher)
        {
            lock (_gate)
            {
                if (_disposed ||
                    !_registrations.TryGetValue(portalId, out Registration? registration) ||
                    !ReferenceEquals(registration.Watcher, sourceWatcher))
                {
                    return;
                }

                registration.NotificationVersion = NextNotificationVersion();
                registration.DebounceTimer?.Change(
                    _debounceDelay,
                    Timeout.InfiniteTimeSpan);
            }
        }

        private void NotifyError(string portalId, FileSystemWatcher sourceWatcher)
        {
            lock (_gate)
            {
                if (_disposed || !_registrations.TryGetValue(portalId, out Registration? current) ||
                    !ReferenceEquals(current.Watcher, sourceWatcher))
                    return;

                // 保留通知身份：UI 会丢弃已解绑注册的刷新通知。
                // 成功重读后 TryBind 重新建立监听，不启动无界重试循环。
                current.NeedsRebind = true;
                current.NotificationVersion = NextNotificationVersion();
                current.DebounceTimer?.Change(_debounceDelay, Timeout.InfiniteTimeSpan);
            }
        }

        private long NextNotificationVersion()
        {
            _nextNotificationVersion++;
            if (_nextNotificationVersion == 0)
            {
                _nextNotificationVersion++;
            }
            return _nextNotificationVersion;
        }
    }
}
