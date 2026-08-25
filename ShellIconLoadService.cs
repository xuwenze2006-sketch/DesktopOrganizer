namespace DesktopOrganizer
{
    /// <summary>
    /// 在单独的后台 STA 线程中串行提取 Shell 图标。
    /// Shell 扩展可能执行磁盘或 COM 工作，因此绝不能阻塞 WPF Dispatcher。
    /// </summary>
    internal sealed class ShellIconLoadService : IDisposable
    {
        private readonly object _gate = new();
        private readonly PriorityQueue<QueuedIconLoad, (int Priority, long Sequence)> _queue = new();
        private readonly Dictionary<IconLoadRequestKey, QueuedIconLoad> _pending = new();
        private readonly HashSet<IconLoadRequestKey> _inFlight = new();
        private readonly AutoResetEvent _workAvailable = new(false);
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Thread _workerThread;
        private long _nextSequence;
        private int _minimumGeneration;
        private int _disposeStarted;

        public ShellIconLoadService()
        {
            _workerThread = new Thread(WorkerMain)
            {
                IsBackground = true,
                Name = "DesktopOrganizer.ShellIconLoader",
                Priority = ThreadPriority.BelowNormal
            };
            _workerThread.SetApartmentState(ApartmentState.STA);
            _workerThread.Start();
        }

        public bool Enqueue(
            IconLoadRequestKey requestKey,
            string itemLocation,
            int priority,
            Action<IconLoadResult> completion)
        {
            ArgumentNullException.ThrowIfNull(completion);
            if (Volatile.Read(ref _disposeStarted) != 0 ||
                requestKey.Generation < Volatile.Read(ref _minimumGeneration))
            {
                return false;
            }

            lock (_gate)
            {
                if (_disposeStarted != 0 || requestKey.Generation < _minimumGeneration)
                {
                    return false;
                }

                if (_inFlight.Contains(requestKey))
                {
                    return true;
                }

                // 同一个缓存键可能先在收起的分组中排队，随后又成为自由图标。
                // 只在优先级提高时追加一个新队列项；旧项出队时会因序列号失效而跳过。
                if (_pending.TryGetValue(requestKey, out QueuedIconLoad? current) &&
                    current.Priority <= priority)
                {
                    return true;
                }

                long sequence = unchecked(++_nextSequence);
                var work = new QueuedIconLoad(
                    requestKey,
                    itemLocation,
                    Math.Max(0, priority),
                    sequence,
                    completion);
                _pending[requestKey] = work;
                _queue.Enqueue(work, (work.Priority, work.Sequence));
            }

            _workAvailable.Set();
            return true;
        }

        public void AdvanceGeneration(int generation)
        {
            if (generation <= Volatile.Read(ref _minimumGeneration))
            {
                return;
            }

            lock (_gate)
            {
                if (generation <= _minimumGeneration)
                {
                    return;
                }

                _minimumGeneration = generation;
                foreach (IconLoadRequestKey key in _pending.Keys
                             .Where(key => key.Generation < generation)
                             .ToList())
                {
                    _pending.Remove(key);
                }
            }

            _workAvailable.Set();
        }

        private void WorkerMain()
        {
            bool shouldUninitialize = false;
            try
            {
                if (!NativeMethods.TryInitializeShellWorkerApartment(out shouldUninitialize))
                {
                    return;
                }

                while (!_shutdown.IsCancellationRequested)
                {
                    if (!TryTakeNext(out QueuedIconLoad work))
                    {
                        _workAvailable.WaitOne(TimeSpan.FromSeconds(2));
                        continue;
                    }

                    try
                    {
                        if (work.RequestKey.Generation < Volatile.Read(ref _minimumGeneration))
                        {
                            continue;
                        }

                        Stopwatch stopwatch = Stopwatch.StartNew();
                        BitmapSource? source = LoadFrozenIcon(
                            work.ItemLocation,
                            work.RequestKey.CacheKey);
                        stopwatch.Stop();

                        try
                        {
                            work.Completion(new IconLoadResult(
                                work.RequestKey,
                                source,
                                stopwatch.Elapsed));
                        }
                        catch
                        {
                            // 完成回调不得终止长期运行的图标线程。
                        }
                    }
                    finally
                    {
                        lock (_gate)
                        {
                            _inFlight.Remove(work.RequestKey);
                        }
                    }
                }
            }
            catch
            {
                // 后台图标加载失败时，界面仍保留占位图，不影响主功能。
            }
            finally
            {
                if (shouldUninitialize)
                {
                    NativeMethods.UninitializeShellWorkerApartment();
                }
            }
        }

        private bool TryTakeNext(out QueuedIconLoad work)
        {
            lock (_gate)
            {
                while (_queue.TryDequeue(out QueuedIconLoad? candidate, out _))
                {
                    if (candidate is null)
                    {
                        continue;
                    }

                    if (!_pending.TryGetValue(candidate.RequestKey, out QueuedIconLoad? current) ||
                        current.Sequence != candidate.Sequence)
                    {
                        continue;
                    }

                    _pending.Remove(candidate.RequestKey);
                    _inFlight.Add(candidate.RequestKey);
                    work = candidate;
                    return true;
                }
            }

            work = null!;
            return false;
        }

        private static BitmapSource? LoadFrozenIcon(
            string itemLocation,
            string requestCacheKey)
        {
            IntPtr iconHandle = IntPtr.Zero;
            try
            {
                iconHandle = ShellItemLocation.TryDecode(itemLocation, out string parsingName, out _)
                    ? NativeMethods.GetLargeShellNamespaceIconHandle(parsingName)
                    : NativeMethods.GetLargeShellIconHandle(
                        itemLocation,
                        useFileAttributes: requestCacheKey.StartsWith(
                            "ext:",
                            StringComparison.OrdinalIgnoreCase));
                if (iconHandle == IntPtr.Zero)
                {
                    return null;
                }

                BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                    iconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(48, 48));
                if (!source.CanFreeze)
                {
                    return null;
                }

                source.Freeze();
                return source;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (iconHandle != IntPtr.Zero)
                {
                    NativeMethods.DestroyIcon(iconHandle);
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            {
                return;
            }

            _shutdown.Cancel();
            lock (_gate)
            {
                _pending.Clear();
                _queue.Clear();
            }

            _workAvailable.Set();

            // Shell 扩展可能暂时阻塞原生调用；不要在 UI 关闭流程中无限等待。
            if (Thread.CurrentThread != _workerThread && _workerThread.Join(TimeSpan.FromMilliseconds(300)))
            {
                _workAvailable.Dispose();
                _shutdown.Dispose();
            }
        }

        private sealed record QueuedIconLoad(
            IconLoadRequestKey RequestKey,
            string ItemLocation,
            int Priority,
            long Sequence,
            Action<IconLoadResult> Completion);
    }

    internal readonly struct IconLoadRequestKey : IEquatable<IconLoadRequestKey>
    {
        public IconLoadRequestKey(string cacheKey, int generation)
        {
            CacheKey = cacheKey;
            Generation = generation;
        }

        public string CacheKey { get; }

        public int Generation { get; }

        public bool Equals(IconLoadRequestKey other) =>
            Generation == other.Generation &&
            StringComparer.OrdinalIgnoreCase.Equals(CacheKey, other.CacheKey);

        public override bool Equals(object? obj) =>
            obj is IconLoadRequestKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(CacheKey),
                Generation);

        public static bool operator ==(IconLoadRequestKey left, IconLoadRequestKey right) =>
            left.Equals(right);

        public static bool operator !=(IconLoadRequestKey left, IconLoadRequestKey right) =>
            !left.Equals(right);
    }

    internal readonly record struct IconLoadResult(
        IconLoadRequestKey RequestKey,
        BitmapSource? Source,
        TimeSpan Elapsed);
}
