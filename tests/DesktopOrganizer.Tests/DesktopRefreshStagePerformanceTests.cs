using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopRefreshStagePerformanceTests
{
    private static readonly string[] Extensions = [".txt", ".md", ".csv", ".pdf"];
    private static readonly string[] Scenarios = ["unchanged", "single-change", "batch-rename"];
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Recorder_SeparatesStagesAndCountsSynchronousAllocations()
    {
        var recorder = new DesktopRefreshPerformanceRecorder();
        var scope = recorder.Start("first");
        byte[] first = new byte[4096];
        scope.Next("second");
        byte[] second = new byte[8192];
        scope.Dispose();
        scope.Dispose();
        GC.KeepAlive(first);
        GC.KeepAlive(second);

        Assert.HasCount(2, recorder.Samples);
        Assert.AreEqual("first", recorder.Samples[0].Stage);
        Assert.IsGreaterThanOrEqualTo(4096L, recorder.Samples[0].AllocatedBytes!.Value);
        Assert.IsGreaterThanOrEqualTo(8192L, recorder.Samples[1].AllocatedBytes!.Value);
    }

    [TestMethod]
    public void Recorder_CrossThreadTotalsDoNotReportAllocationDeltas()
    {
        var recorder = new DesktopRefreshPerformanceRecorder();
        var scope = recorder.Start("total", measureAllocations: false);
        Assert.IsNull(DisposeOnDedicatedThread(scope));
        Assert.IsNull(recorder.Samples.Single().AllocatedBytes);
    }

    [TestMethod]
    public void Recorder_RejectsCrossThreadAllocationMeasurement()
    {
        var recorder = new DesktopRefreshPerformanceRecorder();
        using var scope = recorder.Start("sync-only");
        Assert.IsInstanceOfType<InvalidOperationException>(DisposeOnDedicatedThread(scope));
        Assert.HasCount(0, recorder.Samples);
    }

    private static Exception? DisposeOnDedicatedThread(DesktopRefreshPerformanceRecorder.Scope scope)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { scope.Dispose(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "测量线程未及时退出。");
        return failure;
    }

    [STATestMethod]
    public void IncompleteScan_IsNotMarkedAsCompletedMeasurement()
    {
        using var fixture = new DesktopPathRefreshTests.Fixture();
        fixture.UserPath = string.Empty;
        var recorder = new DesktopRefreshPerformanceRecorder();
        fixture.Window.RefreshPerformanceRecorder = recorder;
        fixture.Refresh();
        Assert.IsFalse(recorder.Completed);
        Assert.IsTrue(fixture.Field<bool>("_desktopScanUnavailable"));
        Assert.IsFalse(recorder.Samples.Any(stage => stage.Stage == "compare"));
    }

    [STATestMethod]
    public void ActualRefresh_RecordsThreeScenariosWithoutChangingIdentityOrReviewState()
    {
        string? outputDirectory = Environment.GetEnvironmentVariable("DESKTOPORGANIZER_REFRESH_PROFILE_DIR");
        bool export = !string.IsNullOrWhiteSpace(outputDirectory);
        // 常规回归只验证小夹具；显式性能运行覆盖规模并输出全部原始样本，不设机器相关耗时门禁。
        foreach (int count in export ? new[] { 50, 200, 1000 } : new[] { 12 })
        {
            List<RefreshSample> samples = Scenarios.SelectMany(scenario =>
                RunScenario(count, scenario, warmups: export ? 3 : 1, iterations: export ? 9 : 1)).ToList();
            if (export)
            {
                Directory.CreateDirectory(outputDirectory!);
                File.WriteAllText(Path.Combine(outputDirectory!, $"refresh-{count}.json"), JsonSerializer.Serialize(new
                {
                    MeasuredUtc = DateTime.UtcNow,
                    Runtime = Environment.Version.ToString(),
                    Environment.ProcessorCount,
                    Profile = "separate fixture per scenario; temporary local files; four extensions; free icons; 25% deferred inbox; safe mode; no watcher events; native icon extraction disabled; fixed 1920x1080 geometry; no Show/Loaded; deferred persistence excluded",
                    WarmupsPerScenario = 3,
                    Samples = samples
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            foreach (var group in samples.GroupBy(sample => sample.Scenario))
            {
                double total = Median(group.Select(sample => Stage(sample, "refresh-total").Milliseconds));
                double ui = Median(group.Select(sample => Stage(sample, "ui-total").Milliseconds));
                TestContext.WriteLine($"{count} items / {group.Key}: total median {total:F3} ms, UI median {ui:F3} ms; created {group.First().Created}, reused {group.First().Reused}");
            }
        }
    }

    private static List<RefreshSample> RunScenario(int count, string scenario, int warmups, int iterations)
    {
        using var fixture = new DesktopPathRefreshTests.Fixture();
        Assert.IsNull(fixture.Window.RefreshPerformanceRecorder);
        // 拒绝本次夹具的所有加载请求；保留服务生命周期和缓存全清路径，结束时再正常释放。
        fixture.Field<ShellIconLoadService>("_shellIconLoadService").AdvanceGeneration(int.MaxValue);
        fixture.Field<DispatcherTimer>("_layoutSaveTimer").Interval = TimeSpan.FromHours(1);
        typeof(MainWindow).GetField("_desktopGeometry", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(fixture.Window, new DesktopGeometry([new DesktopMonitorRegion
            {
                DeviceName = "PERFORMANCE-TEST", IsPrimary = true,
                Bounds = new Rect(0, 0, 1920, 1080), WorkArea = new Rect(0, 0, 1920, 1080)
            }]));
        fixture.Window.IconCanvas.Measure(new Size(1920, 1080));
        fixture.Window.IconCanvas.Arrange(new Rect(0, 0, 1920, 1080));
        for (int index = 0; index < count; index++)
            File.WriteAllText(Path.Combine(fixture.UserA, $"A{index:D4}{Extensions[index % Extensions.Length]}"), "fixture");
        foreach (string extension in Extensions)
            fixture.Field<Dictionary<string, BitmapSource?>>("_iconCache")["ext:" + extension] = null;
        fixture.Refresh();
        Assert.HasCount(count, fixture.Items);
        Assert.IsTrue(fixture.Layout.ItemIdentities.Values.All(identity => !string.IsNullOrWhiteSpace(identity.FileId)),
            "批量改名测量依赖临时磁盘提供稳定 FileId。");

        foreach (var pair in fixture.Layout.ItemIdentities.Take(count / 4))
        {
            DesktopCategoryDefinition category = fixture.Field<Dictionary<string, DesktopCategoryDefinition>>("_desktopCategories")[pair.Key];
            fixture.Layout.InboxItems.Add(pair.Key, new InboxItemInfo
            {
                Identity = pair.Value,
                DetectedUtc = DateTime.UnixEpoch,
                UpdatedUtc = DateTime.UnixEpoch,
                SuggestedCategoryKey = category.Key,
                SuggestedCategoryName = category.DisplayName,
                SuggestedCategoryOrder = category.Order,
                MatchReason = "扩展名 " + Path.GetExtension(pair.Key),
                Reliability = ClassificationReliability.Reliable,
                ReviewState = InboxReviewState.Deferred
            });
        }

        var samples = new List<RefreshSample>();
        int changeSequence = 0;
        for (int round = -warmups; round < iterations; round++)
        {
            Dictionary<string, FrameworkElement> before = new(fixture.Field<Dictionary<string, FrameworkElement>>("_freeIconVisuals"));
            int cacheVersionCountBefore = fixture.Field<Dictionary<string, int>>("_iconCacheVersions").Count;
            int generationBefore = fixture.Field<int>("_iconVisualGeneration");
            HashSet<string?> fileIds = fixture.Layout.ItemIdentities.Values.Select(identity => identity.FileId).ToHashSet();
            if (scenario == "single-change")
            {
                string path = fixture.Items.OrderBy(pair => pair.Key, StringComparer.Ordinal).First().Value;
                File.AppendAllText(path, ".");
                File.SetLastWriteTimeUtc(path, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(++changeSequence));
            }
            else if (scenario == "batch-rename")
            {
                foreach (var pair in fixture.Items)
                {
                    string name = (pair.Key[0] == 'A' ? "B" : "A") + pair.Key[1..];
                    File.Move(pair.Value, Path.Combine(fixture.UserA, name));
                }
            }

            var recorder = new DesktopRefreshPerformanceRecorder();
            fixture.Window.RefreshPerformanceRecorder = recorder;
            fixture.Refresh();
            fixture.Window.RefreshPerformanceRecorder = null;
            fixture.Field<DispatcherTimer>("_layoutSaveTimer").Stop();
            IReadOnlyList<DesktopRefreshStageSample> stages = recorder.Samples;
            Assert.IsTrue(recorder.Completed, $"{count} items / {scenario} / round {round}: 刷新未正常完成，拒绝使用提前退出的样本。");
            Assert.HasCount(count, fixture.Items);
            Assert.IsTrue(fileIds.SetEquals(fixture.Layout.ItemIdentities.Values.Select(identity => identity.FileId)));
            Assert.HasCount(count / 4, fixture.Layout.InboxItems);
            Assert.IsTrue(fixture.Layout.InboxItems.Values.All(entry => entry.ReviewState == InboxReviewState.Deferred && entry.DetectedUtc == DateTime.UnixEpoch));
            Assert.IsTrue(stages.All(stage => stage.Milliseconds >= 0 && (stage.AllocatedBytes ?? 0) >= 0));
            Assert.HasCount(1, stages.Where(stage => stage.Stage == "scan").ToArray());
            Assert.HasCount(1, stages.Where(stage => stage.Stage == "refresh-total").ToArray());
            Assert.HasCount(1, stages.Where(stage => stage.Stage == "ui-total").ToArray());
            Assert.HasCount(1, stages.Where(stage => stage.Stage == "compare").ToArray());
            Assert.AreEqual(scenario != "unchanged", stages.Any(stage => stage.Stage == "visuals"),
                $"{count} items / {scenario} / round {round}: 无变化必须提前返回；变更场景必须完整完成同步刷新，不能把异常提前退出计为加速。Stages: {string.Join(",", stages.Select(stage => stage.Stage))}");
            if (scenario != "unchanged")
            {
                Assert.IsTrue(stages.Any(stage => stage.Stage == "save-request"));
                foreach (string name in new[] { "targets", "prune", "materialize", "finalize" })
                    Assert.HasCount(1, stages.Where(stage => stage.Stage == "visuals/" + name).ToArray());
            }

            Dictionary<string, FrameworkElement> after = fixture.Field<Dictionary<string, FrameworkElement>>("_freeIconVisuals");
            var previousVisuals = before.Values.ToHashSet();
            int reused = after.Values.Count(previousVisuals.Contains);
            if (scenario == "unchanged") Assert.AreEqual(count, reused);
            if (round >= 0)
                samples.Add(new RefreshSample(count, scenario, round, after.Count - reused, reused, before.Count - reused,
                    cacheVersionCountBefore, fixture.Field<int>("_iconVisualGeneration") != generationBefore,
                    count - fixture.Layout.FreeIcons.Count, stages));
        }
        return samples;
    }

    private static DesktopRefreshStageSample Stage(RefreshSample sample, string name) => sample.Stages.Single(stage => stage.Stage == name);
    private static double Median(IEnumerable<double> values)
    {
        double[] sorted = values.Order().ToArray();
        return sorted[sorted.Length / 2];
    }

    private sealed record RefreshSample(int Count, string Scenario, int Iteration, int Created, int Reused, int Removed,
        int CacheVersionCountBefore, bool ResetAllIconCache, int TemporaryOverflowPositions,
        IReadOnlyList<DesktopRefreshStageSample> Stages);
}
