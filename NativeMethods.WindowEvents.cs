// 外部窗口与 Shell 菜单事件监听
// 本文件由 v1.12 Win32 互操作模块拆分；P/Invoke 与行为保持自 v1.11.6 不变。
namespace DesktopOrganizer
{
    internal static partial class NativeMethods
    {

        /// <summary>
        /// 监听外部进程的前台切换、顶级窗口显示以及系统菜单生命周期。
        /// 仅监听前台窗口并不足以覆盖 Windows Terminal：由桌面右键菜单启动时，
        /// Terminal 可能先显示但暂时不成为前台窗口。EVENT_OBJECT_SHOW 能更早发现它。
        /// </summary>
        public static IDisposable? WatchExternalWindowEvents(Action<ExternalWindowEvent> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var subscription = new ExternalWindowEventSubscription(callback);
            return subscription.IsActive ? subscription : null;
        }

        private sealed class ExternalWindowEventSubscription : IDisposable
        {
            private readonly Action<ExternalWindowEvent> _callback;
            private readonly WinEventDelegate _foregroundCallback;
            private readonly WinEventDelegate _visibilityCallback;
            private readonly WinEventDelegate _menuCallback;
            private IntPtr _foregroundHook;
            private IntPtr _visibilityHook;
            private IntPtr _menuHook;
            private readonly object _desktopMenuLock = new();
            private readonly HashSet<IntPtr> _desktopMenuWindows = new();
            private int _disposed;

            public ExternalWindowEventSubscription(Action<ExternalWindowEvent> callback)
            {
                _callback = callback;
                _foregroundCallback = OnForegroundEvent;
                _visibilityCallback = OnVisibilityEvent;
                _menuCallback = OnMenuEvent;

                uint flags = WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS;
                _foregroundHook = SetWinEventHook(
                    EVENT_SYSTEM_FOREGROUND,
                    EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    _foregroundCallback,
                    0,
                    0,
                    flags);
                _visibilityHook = SetWinEventHook(
                    EVENT_OBJECT_SHOW,
                    EVENT_OBJECT_HIDE,
                    IntPtr.Zero,
                    _visibilityCallback,
                    0,
                    0,
                    flags);
                _menuHook = SetWinEventHook(
                    EVENT_SYSTEM_MENUPOPUPSTART,
                    EVENT_SYSTEM_MENUPOPUPEND,
                    IntPtr.Zero,
                    _menuCallback,
                    0,
                    0,
                    flags);
            }

            public bool IsActive =>
                _foregroundHook != IntPtr.Zero ||
                _visibilityHook != IntPtr.Zero ||
                _menuHook != IntPtr.Zero;

            private void OnForegroundEvent(
                IntPtr hWinEventHook,
                uint eventType,
                IntPtr hWnd,
                int idObject,
                int idChild,
                uint idEventThread,
                uint eventTime)
            {
                IntPtr desktopHost = FindDesktopHostWindow();
                if (IsTransientShellUiWindow(hWnd, desktopHost))
                {
                    return;
                }

                PublishWindowEvent(ExternalWindowEventKind.Foreground, hWnd);
            }

            private void OnVisibilityEvent(
                IntPtr hWinEventHook,
                uint eventType,
                IntPtr hWnd,
                int idObject,
                int idChild,
                uint idEventThread,
                uint eventTime)
            {
                if (idObject != OBJID_WINDOW || idChild != 0)
                {
                    return;
                }

                // 桌面右键菜单是临时 Shell UI，不是外部应用窗口。旧版在这里既发布
                // “菜单打开”又发布“窗口显示”，随后重插全屏 layered window，导致整屏闪烁。
                if (LooksLikePopupMenuWindow(hWnd))
                {
                    IntPtr desktopHost = FindDesktopHostWindow();
                    if (eventType == EVENT_OBJECT_SHOW &&
                        IsDesktopShellMenuWindow(hWnd, desktopHost))
                    {
                        MarkDesktopMenuOpened(hWnd);
                    }
                    else if (eventType == EVENT_OBJECT_HIDE)
                    {
                        MarkDesktopMenuClosed(hWnd);
                    }
                    return;
                }

                // 普通顶级窗口的 EVENT_OBJECT_SHOW 数量非常大，通知中心、浏览器、
                // WinUI 宿主和后台组件都会触发。仅凭 SHOW 就多轮重排全屏整理层会形成
                // Z 序反馈循环。普通应用只在真正成为前台窗口时再校正；此监听保留用于
                // 识别 Windows 11 的桌面菜单 Popup 生命周期。
            }

            private void OnMenuEvent(
                IntPtr hWinEventHook,
                uint eventType,
                IntPtr hWnd,
                int idObject,
                int idChild,
                uint idEventThread,
                uint eventTime)
            {
                if (eventType == EVENT_SYSTEM_MENUPOPUPSTART)
                {
                    if (IsDesktopShellMenuWindow(hWnd, FindDesktopHostWindow()))
                    {
                        MarkDesktopMenuOpened(hWnd);
                    }
                    return;
                }

                MarkDesktopMenuClosed(hWnd);
            }

            private void MarkDesktopMenuOpened(IntPtr hWnd)
            {
                if (hWnd == IntPtr.Zero)
                {
                    return;
                }

                bool publishOpened = false;
                lock (_desktopMenuLock)
                {
                    int previousCount = _desktopMenuWindows.Count;
                    _desktopMenuWindows.Add(hWnd);
                    publishOpened = previousCount == 0 && _desktopMenuWindows.Count > 0;
                }

                if (publishOpened)
                {
                    Publish(new ExternalWindowEvent(
                        ExternalWindowEventKind.DesktopShellMenuOpened,
                        hWnd));
                }
            }

            private void MarkDesktopMenuClosed(IntPtr hWnd)
            {
                bool publishClosed = false;
                lock (_desktopMenuLock)
                {
                    if (_desktopMenuWindows.Count == 0)
                    {
                        return;
                    }

                    if (hWnd == IntPtr.Zero)
                    {
                        _desktopMenuWindows.Clear();
                    }
                    else
                    {
                        _desktopMenuWindows.Remove(hWnd);
                    }

                    publishClosed = _desktopMenuWindows.Count == 0;
                }

                if (publishClosed)
                {
                    Publish(new ExternalWindowEvent(
                        ExternalWindowEventKind.DesktopShellMenuClosed,
                        hWnd));
                }
            }

            private static bool LooksLikePopupMenuWindow(IntPtr hWnd)
            {
                var className = new StringBuilder(128);
                if (GetClassName(hWnd, className, className.Capacity) <= 0)
                {
                    return false;
                }

                string value = className.ToString();
                return string.Equals(value, "#32768", StringComparison.Ordinal) ||
                       value.Contains("PopupWindowSiteBridge", StringComparison.OrdinalIgnoreCase) ||
                       value.Contains("PopupWindow", StringComparison.OrdinalIgnoreCase);
            }

            private void PublishWindowEvent(ExternalWindowEventKind kind, IntPtr hWnd)
            {
                if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
                {
                    return;
                }

                IntPtr root = GetAncestor(hWnd, GA_ROOT);
                if (root == IntPtr.Zero)
                {
                    root = hWnd;
                }

                if (GetParent(root) != IntPtr.Zero ||
                    IsTransientShellUiWindow(root, FindDesktopHostWindow()))
                {
                    return;
                }

                Publish(new ExternalWindowEvent(kind, root));
            }

            private void Publish(ExternalWindowEvent windowEvent)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                try
                {
                    _callback(windowEvent);
                }
                catch
                {
                    // WinEvent 回调运行在系统线程上，异常不能越过原生边界。
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                Unhook(ref _foregroundHook);
                Unhook(ref _visibilityHook);
                Unhook(ref _menuHook);

                GC.KeepAlive(_foregroundCallback);
                GC.KeepAlive(_visibilityCallback);
                GC.KeepAlive(_menuCallback);
            }

            private static void Unhook(ref IntPtr hookField)
            {
                IntPtr hook = Interlocked.Exchange(ref hookField, IntPtr.Zero);
                if (hook != IntPtr.Zero)
                {
                    _ = UnhookWinEvent(hook);
                }
            }
        }

        /// <summary>
        /// 判断 WinEvent 菜单通知是否属于桌面 Shell。Windows 11 的右键菜单可能
        /// 使用传统 #32768 窗口，也可能使用 Explorer 的 XAML 弹出窗口，因此以
        /// Shell 进程和当前前台根窗口为主，而不依赖单一窗口类名。
        /// </summary>
    }
}
