using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class FileOperationServiceTests
{
    [TestMethod]
    public async Task Enqueue_ExecutesOperationsInFifoOrder()
    {
        using var service = new FileOperationService();
        using var releaseFirst = new ManualResetEventSlim(initialState: false);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<int>();
        var gate = new object();

        Task<int> first = service.Enqueue(_ =>
        {
            firstStarted.TrySetResult();
            if (!releaseFirst.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("测试未能及时释放第一个文件操作。");
            }
            lock (gate)
            {
                order.Add(1);
            }
            return 1;
        });
        Task<int> second = service.Enqueue(_ =>
        {
            lock (gate)
            {
                order.Add(2);
            }
            return 2;
        });

        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(second.IsCompleted, "第二个任务不应越过仍在执行的第一个任务。");

        releaseFirst.Set();
        int[] results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        CollectionAssert.AreEqual(new[] { 1, 2 }, results);
        lock (gate)
        {
            CollectionAssert.AreEqual(new[] { 1, 2 }, order);
        }
    }

    [TestMethod]
    public async Task Enqueue_RunsDelegateOnStaWorkerThread()
    {
        using var service = new FileOperationService();

        ApartmentState apartment = await service
            .Enqueue(_ => Thread.CurrentThread.GetApartmentState())
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(ApartmentState.STA, apartment);
    }

    [TestMethod]
    public async Task Enqueue_PropagatesExceptionAndContinuesWithNextOperation()
    {
        using var service = new FileOperationService();

        Task<int> failed = service.Enqueue<int>(_ =>
            throw new InvalidOperationException("expected"));
        Task<int> succeeded = service.Enqueue(_ => 42);

        await AssertTaskThrowsAsync<InvalidOperationException>(failed);
        int result = await succeeded.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(42, result);
    }

    [TestMethod]
    public async Task Enqueue_WithAlreadyCanceledToken_DoesNotInvokeDelegate()
    {
        using var service = new FileOperationService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool invoked = false;

        Task<int> task = service.Enqueue(
            _ =>
            {
                invoked = true;
                return 1;
            },
            cancellation.Token);

        await AssertTaskCanceledAsync(task);
        Assert.IsFalse(invoked);
    }

    private static async Task AssertTaskThrowsAsync<TException>(Task task)
        where TException : Exception
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Fail($"预期抛出 {typeof(TException).Name}，但任务成功完成。");
        }
        catch (TException)
        {
            // 预期异常。
        }
    }

    private static async Task AssertTaskCanceledAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Fail("预期任务被取消，但任务成功完成。");
        }
        catch (OperationCanceledException)
        {
            // TaskCanceledException 继承自 OperationCanceledException。
        }
    }
}
