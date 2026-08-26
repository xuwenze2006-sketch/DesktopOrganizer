using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class RefreshCancellationEpochTests
{
    [TestMethod]
    public void Advance_CancelsExistingLinkedToken()
    {
        using var epoch = new RefreshCancellationEpoch();
        using CancellationTokenSource linked = epoch.CreateLinkedTokenSource(CancellationToken.None);

        epoch.Advance();

        Assert.IsTrue(linked.IsCancellationRequested);
    }

    [TestMethod]
    public void Advance_DoesNotCancelNewLinkedToken()
    {
        using var epoch = new RefreshCancellationEpoch();
        using CancellationTokenSource previous = epoch.CreateLinkedTokenSource(CancellationToken.None);

        epoch.Advance();
        using CancellationTokenSource current = epoch.CreateLinkedTokenSource(CancellationToken.None);

        Assert.IsTrue(previous.IsCancellationRequested);
        Assert.IsFalse(current.IsCancellationRequested);
    }

    [TestMethod]
    public void LifetimeCancellation_StillCancelsCurrentEpoch()
    {
        using var epoch = new RefreshCancellationEpoch();
        using var lifetime = new CancellationTokenSource();
        using CancellationTokenSource linked = epoch.CreateLinkedTokenSource(lifetime.Token);

        lifetime.Cancel();

        Assert.IsTrue(linked.IsCancellationRequested);
    }
}
