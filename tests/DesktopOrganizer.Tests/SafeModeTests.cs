using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class SafeModeTests
{
    [STATestMethod]
    public void BasicSync_AddRenameDeleteUpdatesVisibleItemsWithoutAutomation()
    {
        using var f = new DesktopPathRefreshTests.Fixture();
        f.Layout.AutoClassifyNewItems = true;
        f.Layout.PushReflowEnabled = true;
        f.Refresh();
        File.WriteAllText(Path.Combine(f.UserA, "new.txt"), "new");
        Directory.CreateDirectory(Path.Combine(f.UserA, "folder"));

        f.Synchronize();

        CollectionAssert.AreEquivalent(new[] { "new.txt", "folder" }, f.Items.Keys.ToArray());
        var visuals = f.Field<Dictionary<string, FrameworkElement>>("_freeIconVisuals");
        Assert.IsTrue(visuals.ContainsKey("new.txt"));
        Assert.IsTrue(visuals.ContainsKey("folder"));
        Assert.IsEmpty(f.Watchers);
        Assert.IsEmpty(f.Layout.Groups);
        Assert.IsTrue(f.Layout.AutoClassifyNewItems);
        Assert.IsTrue(f.Layout.PushReflowEnabled);

        File.Move(Path.Combine(f.UserA, "new.txt"), Path.Combine(f.UserA, "renamed.txt"));
        Directory.Delete(Path.Combine(f.UserA, "folder"));
        f.Synchronize();

        CollectionAssert.AreEqual(new[] { "renamed.txt" }, f.Items.Keys.ToArray());
        CollectionAssert.AreEqual(new[] { "renamed.txt" }, visuals.Keys.ToArray());
    }

    [STATestMethod]
    public void BasicSync_UnavailableDirectoryPreservesSnapshotThenRecovers()
    {
        using var f = new DesktopPathRefreshTests.Fixture();
        File.WriteAllText(Path.Combine(f.UserA, "old.txt"), "old");
        f.Refresh();
        File.WriteAllText(Path.Combine(f.UserA, "new.txt"), "new");
        f.CommonPath = Path.Combine(f.Root, "missing");

        f.Synchronize();
        CollectionAssert.AreEqual(new[] { "old.txt" }, f.Items.Keys.ToArray());
        Assert.IsTrue(f.Field<bool>("_desktopScanUnavailable"));

        f.CommonPath = f.Common;
        f.Synchronize();
        Assert.IsTrue(f.Items.ContainsKey("new.txt"));
        Assert.IsFalse(f.Field<bool>("_desktopScanUnavailable"));
    }

    [STATestMethod]
    public void BasicSync_InteractionDefersUntilNextProbeWithoutDebounceLoop()
    {
        using var f = new DesktopPathRefreshTests.Fixture();
        f.Refresh();
        File.WriteAllText(Path.Combine(f.UserA, "new.txt"), "new");
        f.Set("_isControlPanelDragging", true);

        f.Synchronize();
        Assert.IsFalse(f.Items.ContainsKey("new.txt"));
        Assert.IsNull(f.Field<CancellationTokenSource?>("_refreshDebounceCts"));

        f.Set("_isControlPanelDragging", false);
        f.Synchronize();
        Assert.IsTrue(f.Items.ContainsKey("new.txt"));
    }

    [STATestMethod]
    public void BasicSync_MenuOpenedDuringScanDefersApplyUntilNextProbe()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int blockNextRead = 0;
        using var f = new DesktopPathRefreshTests.Fixture(beforePathRead: () =>
        {
            if (Interlocked.Exchange(ref blockNextRead, 0) == 0) return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("测试未及时释放桌面扫描。");
        });
        f.Refresh();
        File.WriteAllText(Path.Combine(f.UserA, "during-menu.txt"), "new");
        Interlocked.Exchange(ref blockNextRead, 1);
        f.Invoke("CheckDesktopSynchronizationAsync");
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)), "扫描未开始。");
            f.Set("_desktopShellMenuActive", 1);
        }
        finally { release.Set(); }

        f.WaitForRefreshIdle();

        Assert.IsFalse(f.Items.ContainsKey("during-menu.txt"), "菜单打开时不应替换桌面图标。");
        Assert.IsNull(f.Field<CancellationTokenSource?>("_refreshDebounceCts"));
        f.Set("_desktopShellMenuActive", 0);
        f.Synchronize();
        Assert.IsTrue(f.Items.ContainsKey("during-menu.txt"));
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void BasicSync_ShutdownOrCancellationDoesNotQueue(bool cancelLifetime)
    {
        using var f = new DesktopPathRefreshTests.Fixture();
        if (cancelLifetime) f.Field<CancellationTokenSource>("_lifetimeCts").Cancel();
        else f.Set("_isClosing", true);
        File.WriteAllText(Path.Combine(f.UserA, "new.txt"), "new");

        f.Synchronize();

        Assert.IsEmpty(f.Items);
        Assert.AreEqual(0, f.Field<int>("_refreshWorkerRunning"));
        Assert.AreEqual(0, f.Field<int>("_refreshRequested"));
    }

    [STATestMethod]
    public void BasicSync_PausedOrganizerStaysHiddenWhileSnapshotUpdates()
    {
        using var f = new DesktopPathRefreshTests.Fixture();
        f.Set("_organizerPaused", true);
        f.Window.IconCanvas.Visibility = Visibility.Collapsed;
        File.WriteAllText(Path.Combine(f.UserA, "new.txt"), "new");

        f.Synchronize();

        Assert.IsTrue(f.Items.ContainsKey("new.txt"));
        Assert.AreEqual(Visibility.Collapsed, f.Window.IconCanvas.Visibility);
        Assert.IsTrue(f.Field<bool>("_organizerPaused"));
    }

    [STATestMethod]
    public void BasicSync_NormalModeKeepsWatcherAndMigrationBehavior()
    {
        using var f = new DesktopPathRefreshTests.Fixture(safeMode: false);
        f.Refresh();
        File.WriteAllText(Path.Combine(f.UserB, "migrated.txt"), "new");
        f.UserPath = f.UserB;

        f.Synchronize();

        Assert.IsTrue(f.Items.ContainsKey("migrated.txt"));
        Assert.IsTrue(f.Watchers.Any(w => w.Path == f.UserB));
        File.WriteAllText(Path.Combine(f.UserB, "watcher.txt"), "event");
        f.WaitForItem("watcher.txt");
    }

    [STATestMethod]
    public void Status_SurvivesSelectionFeedbackAndOffersRecoveryWithoutChangingPreferences()
    {
        using var f = new DesktopPathRefreshTests.Fixture();
        f.Layout.AutoClassifyNewItems = true;
        f.Layout.PushReflowEnabled = true;
        f.Invoke("ApplySafeModeState", true, false);
        var badge = f.Window.FindName("SafeModeStatusButton") as Button;
        Assert.IsNotNull(badge);
        Assert.AreEqual(Visibility.Visible, badge.Visibility);
        StringAssert.Contains(badge.Content.ToString()!, "安全模式");
        f.Field<HashSet<string>>("_selectedItemNames").Add("selected.txt");
        f.Invoke("ClearItemSelection");
        Assert.AreEqual("已清除选择", f.Window.StatusText.Text);
        Assert.AreEqual(Visibility.Visible, badge.Visibility);
        Assert.IsFalse(IsDescendantOf(badge, f.Window.ExpandedCommands));
        Assert.IsFalse(IsDescendantOf(badge, f.Window.ControlPanelDragHandle));
        StringAssert.Contains(TooltipText(f.Window), "安全模式");

        badge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.IsFalse(f.Field<bool>("_isSafeModeActive"));
        Assert.AreEqual(Visibility.Collapsed, badge.Visibility);
        StringAssert.Contains(TooltipText(f.Window), "实时模式");
        Assert.IsTrue(f.Layout.AutoClassifyNewItems);
        Assert.IsTrue(f.Layout.PushReflowEnabled);
        f.Synchronize();
        Assert.AreEqual(2, f.Watchers.Count);
        File.WriteAllText(Path.Combine(f.UserA, "after-resume.txt"), "new");
        f.WaitForItem("after-resume.txt");
    }

    [STATestMethod]
    public void Status_UnchangedPollsPreserveModeAndRecoveryDetails()
    {
        using var f = new DesktopPathRefreshTests.Fixture();
        f.Refresh();
        f.Invoke("ApplySafeModeState", true, false);

        f.Synchronize();
        f.Synchronize();

        string details = TooltipText(f.Window);
        StringAssert.Contains(details, "安全模式");
        StringAssert.Contains(details, "15秒");
        StringAssert.Contains(details, "恢复实时");
        StringAssert.Contains(details, f.UserA);
    }

    [STATestMethod]
    public void Status_AppearingAtBottomKeepsWholePanelInsideWorkArea()
    {
        using var f = new DesktopPathRefreshTests.Fixture();
        var workArea = new Rect(0, 0, 1000, 700);
        f.Set("_desktopGeometry", new DesktopGeometry([new DesktopMonitorRegion
        {
            DeviceName = "TEST", IsPrimary = true, Bounds = workArea, WorkArea = workArea
        }]));
        var panel = f.Window.ControlPanel;
        panel.Visibility = Visibility.Visible;
        double initialHeight = ((Size)f.Invoke("MeasureControlPanelContentSize")!).Height;
        panel.Margin = new Thickness(100, workArea.Bottom - initialHeight - 14, 0, 0);
        f.Window.RootGrid.Measure(workArea.Size);
        f.Window.RootGrid.Arrange(workArea);

        f.Invoke("ApplySafeModeState", true, false);
        f.Window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

        double newHeight = ((Size)f.Invoke("MeasureControlPanelContentSize")!).Height;
        Assert.IsTrue(newHeight > initialHeight, "测试应覆盖状态条导致的高度增长。");
        Assert.IsTrue(panel.Margin.Top + newHeight <= workArea.Bottom - 14 + 0.1,
            "安全模式状态条显示后，整个控制条应留在工作区内。");
    }

    [STATestMethod]
    public void Health_SingleDelayDoesNotDowngradeAndHealthySampleResetsWarnings()
    {
        using var f = new DesktopPathRefreshTests.Fixture(safeMode: false);
        var delayed = new ProcessHealthSnapshot(TimeSpan.FromSeconds(5), 0, 0, 0, 0);
        Assert.IsFalse(delayed.IsCritical, "单次延迟不应直接触发资源临界保护。");
        f.Invoke("ApplyHealthSnapshot", delayed);
        Assert.IsFalse(f.Field<bool>("_isSafeModeActive"));
        f.Invoke("ApplyHealthSnapshot", default(ProcessHealthSnapshot));
        f.Invoke("ApplyHealthSnapshot", delayed);
        f.Invoke("ApplyHealthSnapshot", delayed);
        Assert.IsFalse(f.Field<bool>("_isSafeModeActive"));

        f.Invoke("ApplyHealthSnapshot", delayed);

        Assert.IsTrue(f.Field<bool>("_isSafeModeActive"));
        Assert.IsTrue(f.Field<bool>("_safeModeTriggeredThisSession"));
        Assert.AreEqual(Visibility.Visible, ((Button)f.Window.FindName("SafeModeStatusButton")).Visibility);
    }

    [STATestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Health_CriticalResourcesStillDowngradeImmediately(bool memory)
    {
        using var f = new DesktopPathRefreshTests.Fixture(safeMode: false);
        var health = new ProcessHealthSnapshot(TimeSpan.Zero,
            memory ? 1_200L * 1024 * 1024 : 0, 0, memory ? 0 : 12_000, 0);
        Assert.IsTrue(health.IsCritical);

        f.Invoke("ApplyHealthSnapshot", health);

        Assert.IsTrue(f.Field<bool>("_isSafeModeActive"));
    }

    private static string TooltipText(MainWindow window) => string.Join("\n",
        ((StackPanel)((ToolTip)window.StatusText.ToolTip).Content).Children.OfType<TextBlock>().Select(b => b.Text));

    private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
    {
        for (DependencyObject? current = child; current != null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }
}
