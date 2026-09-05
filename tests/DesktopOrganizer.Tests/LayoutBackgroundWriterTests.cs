using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class LayoutBackgroundWriterTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [STATestMethod]
    public void SlowWrite_AllowsDispatcherToRespondBeforeWriteCompletes()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        int dispatcherThread = Environment.CurrentManagedThreadId;
        int writerThread = 0;
        using var fixture = new Fixture(_ =>
        {
            writerThread = Environment.CurrentManagedThreadId;
            entered.Set();
            release.Wait(Timeout);
            completed.Set();
        });

        try
        {
            fixture.QueueWrite();
            Assert.IsTrue(entered.Wait(Timeout), "模拟磁盘写入应已开始。");
            Assert.AreNotEqual(dispatcherThread, writerThread, "普通布局写入不能占用界面线程。");

            bool respondedWhileWriting = false;
            var frame = new DispatcherFrame();
            fixture.Window.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                respondedWhileWriting = !completed.IsSet;
                frame.Continue = false;
            }));
            Dispatcher.PushFrame(frame);

            Assert.IsTrue(respondedWhileWriting, "慢写入未结束时，界面应能处理输入优先级回调。");
        }
        finally
        {
            release.Set();
            fixture.WaitUntilIdle();
        }
    }

    [STATestMethod]
    public void ChangesDuringWrite_CoalesceToLatestSnapshot_WithoutOverlappingWrites()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var generations = new ConcurrentQueue<long>();
        int activeWrites = 0;
        int overlappingWrites = 0;
        using var fixture = new Fixture(json =>
        {
            if (Interlocked.Increment(ref activeWrites) > 1)
                Interlocked.Increment(ref overlappingWrites);
            try
            {
                using JsonDocument snapshot = JsonDocument.Parse(json);
                generations.Enqueue(snapshot.RootElement.GetProperty("SaveGeneration").GetInt64());
                if (generations.Count == 1)
                {
                    entered.Set();
                    release.Wait(Timeout);
                }
            }
            finally
            {
                Interlocked.Decrement(ref activeWrites);
            }
        });

        try
        {
            fixture.QueueWrite();
            Assert.IsTrue(entered.Wait(Timeout));
            fixture.QueueWrite();
            fixture.QueueWrite();
            release.Set();
            fixture.WaitUntilIdle();

            CollectionAssert.AreEqual(new long[] { 1, 3 }, generations.ToArray(),
                "首个快照写入期间的连续变化应合并为最新快照。");
            Assert.AreEqual(0, overlappingWrites, "布局文件只允许一个写入者。");
        }
        finally
        {
            release.Set();
            fixture.WaitUntilIdle();
        }
    }

    [STATestMethod]
    public void CloseWhileWaitingForGate_DiscardsCapturedSnapshot_AndReleasesGate()
    {
        int writes = 0;
        using var fixture = new Fixture(_ => Interlocked.Increment(ref writes));
        SemaphoreSlim gate = fixture.Field<SemaphoreSlim>("_layoutWriteGate");
        gate.Wait();
        bool gateHeld = true;
        try
        {
            fixture.QueueWrite();
            Assert.IsTrue(SpinWait.SpinUntil(() => fixture.PendingSnapshotConsumed, Timeout),
                "后台写入者应已取走旧快照并等待写锁。");

            // 不取消令牌，单独验证取得写锁后的关闭检查。
            fixture.SetClosing();
            gate.Release();
            gateHeld = false;
            fixture.WaitUntilIdle();

            Assert.AreEqual(0, writes, "关闭后不能再写入已取走的旧快照。");
            Assert.AreEqual(1, gate.CurrentCount, "放弃旧快照也必须释放写锁。");
        }
        finally
        {
            if (gateHeld) gate.Release();
            fixture.WaitUntilIdle();
        }
    }

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        public MainWindow Window { get; }

        public Fixture(Action<string> writer)
        {
            // 不显示窗口、不加载用户布局；所有普通保存均由内存委托接收。
            Window = new MainWindow(startQuietly: false, backgroundLayoutWriter: writer);
        }

        public T Field<T>(string name) =>
            (T)typeof(MainWindow).GetField(name, PrivateInstance)!.GetValue(Window)!;

        public void QueueWrite() =>
            typeof(MainWindow).GetMethod("QueueLayoutWrite", PrivateInstance)!.Invoke(Window, null);

        public bool PendingSnapshotConsumed
        {
            get
            {
                lock (Field<object>("_layoutWriteLock"))
                    return Field<string?>("_pendingLayoutJson") == null;
            }
        }

        public void SetClosing() =>
            typeof(MainWindow).GetField("_isClosing", PrivateInstance)!.SetValue(Window, true);

        public void WaitUntilIdle() => Assert.IsTrue(SpinWait.SpinUntil(
            () => Field<int>("_layoutWriterRunning") == 0, Timeout), "后台保存应已收尾。");

        public void Dispose()
        {
            Field<DispatcherTimer>("_layoutSaveTimer").Stop();
            SetClosing();
            Field<CancellationTokenSource>("_lifetimeCts").Cancel();
            WaitUntilIdle();
            Field<FileOperationService>("_fileOperationService").Dispose();
            Field<FolderPortalWatcherCoordinator>("_folderPortalWatcherCoordinator").Dispose();
            Field<CancellationTokenSource>("_lifetimeCts").Dispose();
            Field<SemaphoreSlim>("_layoutWriteGate").Dispose();
        }
    }
}
