namespace DesktopOrganizer
{
    /// <summary>
    /// 在单独的后台 STA 线程中串行执行真实文件系统操作。
    /// Shell 回收站操作要求 STA；串行队列也避免同一批桌面项目并发移动、删除或撤销。
    /// </summary>
    internal sealed class FileOperationService : IDisposable
    {
        private readonly object _gate = new();
        private readonly Queue<IQueuedOperation> _queue = new();
        private readonly AutoResetEvent _workAvailable = new(false);
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Thread _workerThread;
        private int _disposeStarted;
        private int _resourcesDisposed;

        public FileOperationService()
        {
            _workerThread = new Thread(WorkerMain)
            {
                IsBackground = true,
                Name = "DesktopOrganizer.FileOperations",
                Priority = ThreadPriority.BelowNormal
            };
            _workerThread.SetApartmentState(ApartmentState.STA);
            _workerThread.Start();
        }

        public Task<T> Enqueue<T>(
            Func<CancellationToken, T> operation,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(operation);
            if (Volatile.Read(ref _disposeStarted) != 0)
            {
                return Task.FromCanceled<T>(new CancellationToken(canceled: true));
            }

            var queued = new QueuedOperation<T>(operation, cancellationToken);
            lock (_gate)
            {
                if (_disposeStarted != 0)
                {
                    queued.CancelPending();
                    return queued.Task;
                }

                _queue.Enqueue(queued);
            }

            _workAvailable.Set();
            return queued.Task;
        }

        private void WorkerMain()
        {
            while (!_shutdown.IsCancellationRequested)
            {
                IQueuedOperation? operation = null;
                lock (_gate)
                {
                    if (_queue.Count > 0)
                    {
                        operation = _queue.Dequeue();
                    }
                }

                if (operation == null)
                {
                    _workAvailable.WaitOne(TimeSpan.FromSeconds(2));
                    continue;
                }

                operation.Execute(_shutdown.Token);
            }

            CancelPendingOperations();
        }

        private void CancelPendingOperations()
        {
            List<IQueuedOperation> pending;
            lock (_gate)
            {
                pending = _queue.ToList();
                _queue.Clear();
            }

            foreach (IQueuedOperation operation in pending)
            {
                operation.CancelPending();
            }
        }

        public void Stop(TimeSpan waitTimeout)
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            {
                return;
            }

            _shutdown.Cancel();
            CancelPendingOperations();
            _workAvailable.Set();
            if (Thread.CurrentThread != _workerThread)
            {
                _workerThread.Join(waitTimeout);
            }
        }

        public void Dispose()
        {
            Stop(TimeSpan.FromMilliseconds(500));
            // 正在执行的原生文件操作无法安全强制终止。若 500 ms 内尚未返回，
            // 保留同步句柄直到进程退出，避免后台线程在收尾时访问已释放对象。
            if (!_workerThread.IsAlive &&
                Interlocked.Exchange(ref _resourcesDisposed, 1) == 0)
            {
                _shutdown.Dispose();
                _workAvailable.Dispose();
            }
        }

        private interface IQueuedOperation
        {
            void Execute(CancellationToken shutdownToken);
            void CancelPending();
        }

        private sealed class QueuedOperation<T> : IQueuedOperation
        {
            private readonly Func<CancellationToken, T> _operation;
            private readonly CancellationToken _requestToken;
            private readonly TaskCompletionSource<T> _completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            public QueuedOperation(
                Func<CancellationToken, T> operation,
                CancellationToken requestToken)
            {
                _operation = operation;
                _requestToken = requestToken;
            }

            public Task<T> Task => _completion.Task;

            public void Execute(CancellationToken shutdownToken)
            {
                if (_requestToken.IsCancellationRequested || shutdownToken.IsCancellationRequested)
                {
                    CancelPending();
                    return;
                }

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    _requestToken,
                    shutdownToken);
                try
                {
                    _completion.TrySetResult(_operation(linked.Token));
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    _completion.TrySetCanceled(linked.Token);
                }
                catch (Exception exception)
                {
                    _completion.TrySetException(exception);
                }
            }

            public void CancelPending()
            {
                CancellationToken token = _requestToken.IsCancellationRequested
                    ? _requestToken
                    : new CancellationToken(canceled: true);
                _completion.TrySetCanceled(token);
            }
        }
    }
}
