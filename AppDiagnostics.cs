namespace DesktopOrganizer
{
    /// <summary>
    /// 轻量运行诊断。只在异常或慢操作时写日志，不持续高频采样。
    /// </summary>
    internal sealed class AppDiagnostics
    {
        private readonly object _sync = new();
        private readonly string _logDirectory;
        private readonly string _logPath;

        public AppDiagnostics()
        {
            _logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DesktopOrganizer",
                "Logs");
            _logPath = Path.Combine(_logDirectory, $"desktoporganizer-{DateTime.Now:yyyyMMdd}.log");
        }

        public string LogDirectory => _logDirectory;

        public void Log(string message)
        {
            try
            {
                lock (_sync)
                {
                    Directory.CreateDirectory(_logDirectory);
                    File.AppendAllText(
                        _logPath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}",
                        new UTF8Encoding(false));
                }
            }
            catch
            {
                // 诊断日志不得影响主程序。
            }
        }

        public void LogSlowOperation(string name, TimeSpan elapsed, int itemCount = -1)
        {
            if (elapsed < TimeSpan.FromMilliseconds(750))
            {
                return;
            }

            string countText = itemCount >= 0 ? $", items={itemCount}" : string.Empty;
            Log($"SLOW operation={name}, elapsedMs={elapsed.TotalMilliseconds:F0}{countText}");
        }

        public ProcessHealthSnapshot CaptureHealth(TimeSpan dispatcherDelay)
        {
            try
            {
                using Process process = Process.GetCurrentProcess();
                return new ProcessHealthSnapshot(
                    dispatcherDelay,
                    process.WorkingSet64,
                    process.PrivateMemorySize64,
                    process.HandleCount,
                    GC.GetTotalMemory(forceFullCollection: false));
            }
            catch
            {
                return new ProcessHealthSnapshot(dispatcherDelay, 0, 0, 0, 0);
            }
        }
    }

    internal readonly record struct ProcessHealthSnapshot(
        TimeSpan DispatcherDelay,
        long WorkingSetBytes,
        long PrivateBytes,
        int HandleCount,
        long ManagedBytes)
    {
        public bool IsCritical =>
            DispatcherDelay >= TimeSpan.FromSeconds(4) ||
            WorkingSetBytes >= 1_200L * 1024 * 1024 ||
            HandleCount >= 12_000;

        public override string ToString() =>
            $"dispatcherDelayMs={DispatcherDelay.TotalMilliseconds:F0}, " +
            $"workingSetMB={WorkingSetBytes / 1024d / 1024d:F1}, " +
            $"privateMB={PrivateBytes / 1024d / 1024d:F1}, " +
            $"managedMB={ManagedBytes / 1024d / 1024d:F1}, handles={HandleCount}";
    }
}
