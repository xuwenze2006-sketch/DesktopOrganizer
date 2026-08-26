namespace DesktopOrganizer
{
    /// <summary>
    /// 为桌面刷新提供可前进的取消代次。进入安全模式时取消旧代次，
    /// 之后的手工刷新会自动使用新代次，不受旧取消信号影响。
    /// </summary>
    internal sealed class RefreshCancellationEpoch : IDisposable
    {
        private readonly object _sync = new();
        private CancellationTokenSource _current = new();
        private bool _disposed;

        public CancellationTokenSource CreateLinkedTokenSource(CancellationToken lifetimeToken)
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return CancellationTokenSource.CreateLinkedTokenSource(
                    lifetimeToken,
                    _current.Token);
            }
        }

        public void Advance()
        {
            CancellationTokenSource previous;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                previous = _current;
                _current = new CancellationTokenSource();
            }

            previous.Cancel();
            previous.Dispose();
        }

        public void Dispose()
        {
            CancellationTokenSource current;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                current = _current;
            }

            current.Cancel();
            current.Dispose();
        }
    }
}
