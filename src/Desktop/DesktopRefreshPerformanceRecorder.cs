using System.Collections.Concurrent;
using System.Diagnostics;

namespace DesktopOrganizer;

internal sealed record DesktopRefreshStageSample(string Stage, double Milliseconds, long? AllocatedBytes);

/// <summary>显式启用的刷新测量；同步阶段统计当前线程分配，跨线程总时长只计时。</summary>
internal sealed class DesktopRefreshPerformanceRecorder
{
    private readonly ConcurrentQueue<DesktopRefreshStageSample> _samples = new();
    public IReadOnlyList<DesktopRefreshStageSample> Samples => _samples.ToArray();
    public bool Completed { get; private set; }

    public void Complete() => Completed = true;

    public Scope Start(string stage, bool measureAllocations = true) => new(this, stage, measureAllocations);

    internal sealed class Scope : IDisposable
    {
        private readonly DesktopRefreshPerformanceRecorder _owner;
        private readonly bool _measureAllocations;
        private readonly int _threadId;
        private string _stage;
        private long _started;
        private long _allocated;
        private bool _disposed;

        internal Scope(DesktopRefreshPerformanceRecorder owner, string stage, bool measureAllocations)
        {
            _owner = owner;
            _stage = stage;
            _measureAllocations = measureAllocations;
            _threadId = Environment.CurrentManagedThreadId;
            Restart();
        }

        public void Next(string stage)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Record();
            _stage = stage;
            Restart();
        }

        private void Restart()
        {
            _allocated = _measureAllocations ? GC.GetAllocatedBytesForCurrentThread() : 0;
            _started = Stopwatch.GetTimestamp();
        }

        private void Record()
        {
            double milliseconds = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
            if (_measureAllocations && Environment.CurrentManagedThreadId != _threadId)
                throw new InvalidOperationException("分配测量不能跨线程或跨 await 使用。");
            long? bytes = _measureAllocations ? GC.GetAllocatedBytesForCurrentThread() - _allocated : null;
            _owner._samples.Enqueue(new DesktopRefreshStageSample(_stage, milliseconds, bytes));
        }

        public void Dispose()
        {
            if (_disposed) return;
            Record();
            _disposed = true;
        }
    }
}
