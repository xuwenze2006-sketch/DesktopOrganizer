// 不激活桌面窗口的键盘命令监听
namespace DesktopOrganizer
{
    internal static partial class NativeMethods
    {
        internal enum DesktopKeyboardCommand
        {
            UndoFileMove,
            ClearSelection,
            SelectAllItems,
            OpenDesktopSearch,
            RefreshDesktop
        }

        /// <summary>
        /// 监听桌面整理层需要的少量键盘命令。主窗口使用 WS_EX_NOACTIVATE，
        /// 无法依赖 WPF KeyDown；低级钩子只在
        /// 当前前台属于桌面，或用户最后一次鼠标按下发生在整理层时发布命令。
        /// 只有命令可执行时才消费对应按键，其他输入原样传递。
        /// </summary>
        public static IDisposable? WatchDesktopKeyboardCommands(
            IntPtr organizerWindow,
            Func<IntPtr> desktopHostProvider,
            Func<DesktopKeyboardCommand, bool> callback,
            Action<IntPtr>? mouseButtonDown = null)
        {
            if (organizerWindow == IntPtr.Zero || !IsWindow(organizerWindow))
            {
                return null;
            }

            ArgumentNullException.ThrowIfNull(desktopHostProvider);
            ArgumentNullException.ThrowIfNull(callback);

            var subscription = new DesktopInputSubscription(
                organizerWindow,
                desktopHostProvider,
                callback,
                mouseButtonDown);
            if (subscription.IsActive)
            {
                return subscription;
            }

            subscription.Dispose();
            return null;
        }

        /// <summary>
        /// 判断键盘输入当前是否属于桌面语境。不能只比较 Explorer 进程，
        /// 因为普通文件管理器窗口与桌面共用 explorer.exe。
        /// </summary>
        public static bool IsDesktopKeyboardContext(
            IntPtr organizerWindow,
            IntPtr desktopHost)
        {
            IntPtr foregroundRoot = NormalizeRootWindow(GetForegroundWindow());
            if (foregroundRoot == IntPtr.Zero)
            {
                return false;
            }

            if (foregroundRoot == organizerWindow)
            {
                return true;
            }

            IntPtr normalizedHost = NormalizeRootWindow(desktopHost);
            if (normalizedHost != IntPtr.Zero && foregroundRoot == normalizedHost)
            {
                return true;
            }

            IntPtr shellRoot = NormalizeRootWindow(GetShellWindow());
            if (shellRoot != IntPtr.Zero && foregroundRoot == shellRoot)
            {
                return true;
            }

            IntPtr desktopListView = FindDesktopListView();
            IntPtr desktopListRoot = NormalizeRootWindow(desktopListView);
            return desktopListRoot != IntPtr.Zero && foregroundRoot == desktopListRoot;
        }

        internal static DesktopKeyboardCommand? ResolveDesktopKeyboardCommand(
            uint virtualKey,
            bool controlDown,
            bool shiftDown,
            bool altDown,
            bool windowsDown)
        {
            if (shiftDown || altDown || windowsDown)
            {
                return null;
            }
            if (controlDown && virtualKey == VK_Z)
            {
                return DesktopKeyboardCommand.UndoFileMove;
            }
            if (controlDown && virtualKey == VK_A)
            {
                return DesktopKeyboardCommand.SelectAllItems;
            }
            if (controlDown && virtualKey == VK_F)
            {
                return DesktopKeyboardCommand.OpenDesktopSearch;
            }
            if (!controlDown && virtualKey == VK_ESCAPE)
            {
                return DesktopKeyboardCommand.ClearSelection;
            }
            if (!controlDown && virtualKey == VK_F5)
            {
                return DesktopKeyboardCommand.RefreshDesktop;
            }
            return null;
        }

        private sealed class DesktopInputSubscription : IDisposable
        {
            private readonly IntPtr _organizerWindow;
            private readonly Func<IntPtr> _desktopHostProvider;
            private readonly Func<DesktopKeyboardCommand, bool> _callback;
            private readonly Action<IntPtr>? _mouseButtonDown;
            private readonly HookProc _keyboardCallback;
            private readonly HookProc _mouseCallback;
            private readonly WinEventDelegate _foregroundCallback;
            private readonly object _pressedKeysLock = new();
            private readonly HashSet<uint> _pressedKeys = new();
            private readonly HashSet<uint> _suppressedKeys = new();
            private IntPtr _keyboardHook;
            private IntPtr _mouseHook;
            private IntPtr _foregroundHook;
            private int _organizerPointerContext;
            private int _disposed;

            public DesktopInputSubscription(
                IntPtr organizerWindow,
                Func<IntPtr> desktopHostProvider,
                Func<DesktopKeyboardCommand, bool> callback,
                Action<IntPtr>? mouseButtonDown)
            {
                _organizerWindow = organizerWindow;
                _desktopHostProvider = desktopHostProvider;
                _callback = callback;
                _mouseButtonDown = mouseButtonDown;
                _keyboardCallback = OnKeyboardEvent;
                _mouseCallback = OnMouseEvent;
                _foregroundCallback = OnForegroundEvent;

                IntPtr moduleHandle = GetModuleHandle(null);
                _keyboardHook = SetWindowsHookEx(
                    WH_KEYBOARD_LL,
                    _keyboardCallback,
                    moduleHandle,
                    0);
                _mouseHook = SetWindowsHookEx(
                    WH_MOUSE_LL,
                    _mouseCallback,
                    moduleHandle,
                    0);
                _foregroundHook = SetWinEventHook(
                    EVENT_SYSTEM_FOREGROUND,
                    EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    _foregroundCallback,
                    0,
                    0,
                    WINEVENT_OUTOFCONTEXT);
            }

            // 键盘钩子是核心；鼠标和前台监听失败时仍可在 Explorer 桌面前台工作。
            public bool IsActive => _keyboardHook != IntPtr.Zero;

            private IntPtr OnKeyboardEvent(
                int nCode,
                IntPtr wParam,
                IntPtr lParam)
            {
                try
                {
                    if (nCode >= 0 && Volatile.Read(ref _disposed) == 0)
                    {
                        int message = unchecked((int)wParam.ToInt64());
                        KbdLlHookStruct data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                        bool isKeyDown = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
                        bool isKeyUp = message == WM_KEYUP || message == WM_SYSKEYUP;

                        if (isKeyUp)
                        {
                            if (ReleaseKey(data.VirtualKey))
                            {
                                return new IntPtr(1);
                            }
                        }
                        else if (isKeyDown && (data.Flags & LLKHF_INJECTED) == 0)
                        {
                            bool firstKeyDown = TryMarkFirstKeyDown(data.VirtualKey);
                            if (!firstKeyDown)
                            {
                                if (IsSuppressedKey(data.VirtualKey))
                                {
                                    return new IntPtr(1);
                                }
                            }
                            else if (IsCommandContextActive() &&
                                     TryPublishCommand(data.VirtualKey))
                            {
                                MarkSuppressedKey(data.VirtualKey);
                                return new IntPtr(1);
                            }
                        }
                    }
                }
                catch
                {
                    // 低级钩子不能让托管异常越过原生边界。
                }

                return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            }

            private bool TryMarkFirstKeyDown(uint virtualKey)
            {
                lock (_pressedKeysLock)
                {
                    return _pressedKeys.Add(virtualKey);
                }
            }

            private void MarkSuppressedKey(uint virtualKey)
            {
                lock (_pressedKeysLock)
                {
                    _suppressedKeys.Add(virtualKey);
                }
            }

            private bool IsSuppressedKey(uint virtualKey)
            {
                lock (_pressedKeysLock)
                {
                    return _suppressedKeys.Contains(virtualKey);
                }
            }

            private bool ReleaseKey(uint virtualKey)
            {
                lock (_pressedKeysLock)
                {
                    _pressedKeys.Remove(virtualKey);
                    return _suppressedKeys.Remove(virtualKey);
                }
            }

            private bool TryPublishCommand(uint virtualKey)
            {
                bool controlDown = IsVirtualKeyDown(VK_CONTROL);
                bool shiftDown = IsVirtualKeyDown(VK_SHIFT);
                bool altDown = IsVirtualKeyDown(VK_MENU);
                bool windowsDown = IsVirtualKeyDown(VK_LWIN) || IsVirtualKeyDown(VK_RWIN);

                DesktopKeyboardCommand? command = ResolveDesktopKeyboardCommand(
                    virtualKey,
                    controlDown,
                    shiftDown,
                    altDown,
                    windowsDown);

                if (command == null)
                {
                    return false;
                }

                try
                {
                    // 只有调用方确认命令可执行并已排队时才消费按键，避免
                    // 不激活窗口与原前台应用同时处理同一个 Ctrl+A/Ctrl+F/Ctrl+Z/Esc/F5。
                    return _callback(command.Value);
                }
                catch
                {
                    // 回调只负责排队到 Dispatcher；异常时继续把按键交给系统。
                    return false;
                }
            }

            private IntPtr OnMouseEvent(
                int nCode,
                IntPtr wParam,
                IntPtr lParam)
            {
                try
                {
                    if (nCode >= 0 &&
                        Volatile.Read(ref _disposed) == 0 &&
                        IsMouseButtonDownMessage(unchecked((int)wParam.ToInt64())))
                    {
                        MouseLlHookStruct data = Marshal.PtrToStructure<MouseLlHookStruct>(lParam);
                        IntPtr target = WindowFromPoint(data.Point);
                        IntPtr root = NormalizeRootWindow(target);
                        Volatile.Write(
                            ref _organizerPointerContext,
                            root == _organizerWindow ? 1 : 0);
                        // 只通知，不消费点击；让透明空白区域仍由 Windows 桌面处理。
                        _mouseButtonDown?.Invoke(root);
                    }
                }
                catch
                {
                    Volatile.Write(ref _organizerPointerContext, 0);
                }

                return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            }

            private void OnForegroundEvent(
                IntPtr hWinEventHook,
                uint eventType,
                IntPtr hWnd,
                int idObject,
                int idChild,
                uint idEventThread,
                uint eventTime)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                // Alt+Tab/Win+Tab 等不产生鼠标按下，因此在前台切换时主动清除
                // “最后点击了整理层”的临时语境。切到桌面则由实时前台判断接管。
                if (!IsDesktopKeyboardContext(_organizerWindow, SafeGetDesktopHost()))
                {
                    Volatile.Write(ref _organizerPointerContext, 0);
                }
            }

            private bool IsCommandContextActive()
            {
                IntPtr foregroundRoot = NormalizeRootWindow(GetForegroundWindow());

                // 主窗口极少数情况下被显式激活时，由 WPF PreviewKeyDown 处理，
                // 避免本地事件与低级钩子对同一次按键执行两遍。
                if (foregroundRoot == _organizerWindow)
                {
                    return false;
                }

                return Volatile.Read(ref _organizerPointerContext) != 0 ||
                       IsDesktopKeyboardContext(_organizerWindow, SafeGetDesktopHost());
            }

            private IntPtr SafeGetDesktopHost()
            {
                try
                {
                    return _desktopHostProvider();
                }
                catch
                {
                    return IntPtr.Zero;
                }
            }

            private static bool IsVirtualKeyDown(int virtualKey)
            {
                return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
            }

            private static bool IsMouseButtonDownMessage(int message)
            {
                return message == WM_LBUTTONDOWN ||
                       message == WM_RBUTTONDOWN ||
                       message == WM_MBUTTONDOWN ||
                       message == WM_XBUTTONDOWN ||
                       message == WM_NCLBUTTONDOWN ||
                       message == WM_NCRBUTTONDOWN ||
                       message == WM_NCMBUTTONDOWN ||
                       message == WM_NCXBUTTONDOWN;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                UnhookWindows(ref _keyboardHook);
                UnhookWindows(ref _mouseHook);

                IntPtr foregroundHook = Interlocked.Exchange(ref _foregroundHook, IntPtr.Zero);
                if (foregroundHook != IntPtr.Zero)
                {
                    _ = UnhookWinEvent(foregroundHook);
                }

                lock (_pressedKeysLock)
                {
                    _pressedKeys.Clear();
                    _suppressedKeys.Clear();
                }
                Volatile.Write(ref _organizerPointerContext, 0);

                GC.KeepAlive(_keyboardCallback);
                GC.KeepAlive(_mouseCallback);
                GC.KeepAlive(_foregroundCallback);
            }

            private static void UnhookWindows(ref IntPtr hookField)
            {
                IntPtr hook = Interlocked.Exchange(ref hookField, IntPtr.Zero);
                if (hook != IntPtr.Zero)
                {
                    _ = UnhookWindowsHookEx(hook);
                }
            }
        }

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct KbdLlHookStruct
        {
            public readonly uint VirtualKey;
            public readonly uint ScanCode;
            public readonly uint Flags;
            public readonly uint Time;
            public readonly UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct MouseLlHookStruct
        {
            public readonly NativePoint Point;
            public readonly uint MouseData;
            public readonly uint Flags;
            public readonly uint Time;
            public readonly UIntPtr ExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int idHook,
            HookProc callback,
            IntPtr moduleHandle,
            uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hookHandle,
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(NativePoint point);

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCRBUTTONDOWN = 0x00A4;
        private const int WM_NCMBUTTONDOWN = 0x00A7;
        private const int WM_NCXBUTTONDOWN = 0x00AB;
        private const uint LLKHF_INJECTED = 0x00000010;
        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;
        private const int VK_MENU = 0x12;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_A = 0x41;
        private const int VK_F = 0x46;
        private const int VK_F5 = 0x74;
        private const int VK_Z = 0x5A;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
    }
}
