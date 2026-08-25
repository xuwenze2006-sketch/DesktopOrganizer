namespace DesktopOrganizer
{
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = @"Local\DesktopOrganizer.SingleInstance";
        private const string ShowControlPanelEventName = @"Local\DesktopOrganizer.ShowControlPanel";

        private Mutex? _singleInstanceMutex;
        private EventWaitHandle? _showControlPanelEvent;
        private RegisteredWaitHandle? _showControlPanelRegistration;
        private int _showControlPanelRequestQueued;
        private int _fatalErrorHandling;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            bool quietStartup = e.Args.Any(argument =>
                argument.Equals("--startup", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("--quiet", StringComparison.OrdinalIgnoreCase));

            _showControlPanelEvent = new EventWaitHandle(
                initialState: false,
                mode: EventResetMode.AutoReset,
                name: ShowControlPanelEventName);

            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: SingleInstanceMutexName,
                createdNew: out bool createdNew);

            if (!createdNew)
            {
                // 再次双击 EXE 时不弹“已运行”提示，而是唤出已有实例的控制栏。
                try
                {
                    _showControlPanelEvent.Set();
                }
                catch
                {
                    // 已有实例正在退出时，信号发送失败不需要再显示错误窗口。
                }

                Shutdown();
                return;
            }

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            var window = new MainWindow(quietStartup);
            MainWindow = window;

            _showControlPanelRegistration = ThreadPool.RegisterWaitForSingleObject(
                _showControlPanelEvent,
                (_, timedOut) =>
                {
                    if (timedOut || Dispatcher.HasShutdownStarted)
                    {
                        return;
                    }

                    // 连续双击 EXE 只排队一次唤出请求，避免 Dispatcher 队列被重复信号淹没。
                    if (Interlocked.Exchange(ref _showControlPanelRequestQueued, 1) == 1)
                    {
                        return;
                    }

                    try
                    {
                        _ = Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (MainWindow is DesktopOrganizer.MainWindow existingWindow)
                                {
                                    existingWindow.ShowControlPanelFromExternalLaunch();
                                }
                            }
                            finally
                            {
                                Interlocked.Exchange(ref _showControlPanelRequestQueued, 0);
                            }
                        }));
                    }
                    catch (InvalidOperationException)
                    {
                        // Dispatcher 已开始退出；允许清理流程继续完成。
                        Interlocked.Exchange(ref _showControlPanelRequestQueued, 0);
                    }
                },
                state: null,
                millisecondsTimeOutInterval: Timeout.Infinite,
                executeOnlyOnce: false);

            window.Show();
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // 不让发生未处理异常后的半损坏窗口继续运行，否则可能表现为“按钮全失效”。
            e.Handled = true;
            if (Interlocked.Exchange(ref _fatalErrorHandling, 1) == 1)
            {
                Shutdown(-1);
                return;
            }

            if (MainWindow is DesktopOrganizer.MainWindow window)
            {
                window.LogFatalException(e.Exception);
            }
            RestoreDesktopState();
            try
            {
                MessageBox.Show(
                    $"程序发生未处理错误，系统桌面图标已尝试恢复。\n\n{e.Exception.Message}",
                    "DesktopOrganizer 错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Shutdown(-1);
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (MainWindow is DesktopOrganizer.MainWindow window && e.ExceptionObject is Exception exception)
            {
                window.LogFatalException(exception);
            }
            RestoreDesktopState();
        }

        private void RestoreDesktopState()
        {
            if (MainWindow is DesktopOrganizer.MainWindow window)
            {
                window.RestoreDesktopState();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            RestoreDesktopState();

            _showControlPanelRegistration?.Unregister(null);
            _showControlPanelRegistration = null;

            _showControlPanelEvent?.Dispose();
            _showControlPanelEvent = null;

            if (_singleInstanceMutex != null)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // 当前进程并未持有互斥锁，无需处理。
                }

                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }

            base.OnExit(e);
        }
    }
}
