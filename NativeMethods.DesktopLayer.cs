// 桌面宿主、Z 序与窗口样式
// 本文件由 v1.12 Win32 互操作模块拆分；P/Invoke 与行为保持自 v1.11.6 不变。
namespace DesktopOrganizer
{
    internal static partial class NativeMethods
    {
        public static bool IsDesktopShellMenuWindow(IntPtr menuWindow, IntPtr desktopHost)
        {
            if (desktopHost == IntPtr.Zero || !IsWindow(desktopHost))
            {
                return false;
            }

            IntPtr foreground = GetForegroundWindow();
            IntPtr foregroundRoot = NormalizeRootWindow(foreground);
            if (foregroundRoot == desktopHost)
            {
                return true;
            }

            if (menuWindow == IntPtr.Zero || !IsWindow(menuWindow))
            {
                return false;
            }

            // Explorer 的所有文件管理器窗口与桌面通常在同一进程中，不能仅凭 PID
            // 判断“桌面菜单”，否则在资源管理器中右键也会暂停桌面层维护。
            IntPtr current = menuWindow;
            for (int depth = 0; depth < 12 && current != IntPtr.Zero; depth++)
            {
                if (current == desktopHost)
                {
                    return true;
                }

                IntPtr parent = GetParent(current);
                if (parent == IntPtr.Zero)
                {
                    parent = GetWindowLongPtr(current, GWL_HWNDPARENT);
                }
                current = parent;
            }

            return false;
        }

        /// <summary>
        /// Shell 菜单、提示框和 XAML 弹出宿主只用于短暂交互，不应触发全屏整理层
        /// 的 Z 序重排。过滤它们可避免桌面右键时整层重绘闪烁。
        /// </summary>
        public static bool IsTransientShellUiWindow(IntPtr windowHandle, IntPtr desktopHost)
        {
            IntPtr root = NormalizeRootWindow(windowHandle);
            if (root == IntPtr.Zero || !IsWindow(root))
            {
                return false;
            }

            var classNameBuilder = new StringBuilder(160);
            if (GetClassName(root, classNameBuilder, classNameBuilder.Capacity) <= 0)
            {
                return false;
            }

            string className = classNameBuilder.ToString();
            if (string.Equals(className, "#32768", StringComparison.Ordinal) ||
                className.Contains("PopupWindowSiteBridge", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("PopupWindow", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("tooltips", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("Tooltip", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("ForegroundStaging", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (desktopHost == IntPtr.Zero || !IsWindow(desktopHost))
            {
                return false;
            }

            _ = GetWindowThreadProcessId(root, out uint rootProcessId);
            _ = GetWindowThreadProcessId(desktopHost, out uint shellProcessId);
            if (rootProcessId == 0 || rootProcessId != shellProcessId)
            {
                return false;
            }

            return className.Contains("XamlExplorerHostIslandWindow", StringComparison.OrdinalIgnoreCase) ||
                   className.Contains("TopLevelWindowForOverflowXamlIsland", StringComparison.OrdinalIgnoreCase) ||
                   className.Contains("DesktopChildSiteBridge", StringComparison.OrdinalIgnoreCase);
        }

        public static IntPtr GetForegroundRootWindow()
        {
            return NormalizeRootWindow(GetForegroundWindow());
        }

        /// <summary>使主窗口不进入 Alt+Tab，也不会在点击时抢占前台焦点。</summary>
        public static void ApplyDesktopWindowStyles(IntPtr hWnd)
        {
            long style = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            style |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(style));
        }

        /// <summary>
        /// 将 WPF 主窗口放到顶级窗口 Z 序中 Progman 的正上方。
        /// 这是 Windows 11 上比 SetParent(Progman/WorkerW) 更稳定的桌面小组件模式：
        /// 窗口保持为正常顶级 layered window，因此 WPF 透明渲染不会因变成子窗口而消失；
        /// 同时它位于 Progman 之上、所有普通应用窗口之下。
        /// </summary>
        public static bool TryAttachWindowToDesktop(IntPtr windowHandle, out IntPtr desktopHost)
        {
            desktopHost = FindDesktopHostWindow();
            if (windowHandle == IntPtr.Zero || desktopHost == IntPtr.Zero || !IsWindow(windowHandle))
            {
                return false;
            }

            // WPF 在 ShowInTaskbar=false 时可能自动创建隐藏 owner。owner 会限制 Z 序，
            // 必须清除后才能精确插入到 Progman 正上方。
            _ = SetParent(windowHandle, IntPtr.Zero);
            SetWindowLongPtr(windowHandle, GWL_HWNDPARENT, IntPtr.Zero);

            long originalStyle = GetWindowLongPtr(windowHandle, GWL_STYLE).ToInt64();
            long topLevelStyle = (originalStyle |
                                  WS_POPUP) &
                                 ~WS_CHILD &
                                 ~WS_CLIPSIBLINGS &
                                 ~WS_CLIPCHILDREN;
            SetWindowLongPtr(windowHandle, GWL_STYLE, new IntPtr(topLevelStyle));

            if (!EnsureWindowInDesktopLayer(windowHandle, desktopHost, frameChanged: true))
            {
                SendWindowToBottom(windowHandle);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 确保用户刚启动/激活的普通应用窗口位于全屏整理层之上。
        /// Windows Terminal 可能复用已有进程，并在 Shell 菜单命令后被插到整理层下方；
        /// 单纯监听前台事件无法覆盖这一时序，因此这里同时验证真实顶级 Z 序。
        /// </summary>
        public static bool IsExternalWindowAboveOrganizer(
            IntPtr externalWindow,
            IntPtr organizerWindow,
            IntPtr desktopHost)
        {
            IntPtr root = NormalizeRootWindow(externalWindow);
            return IsMeaningfulExternalApplicationWindow(root, organizerWindow, desktopHost) &&
                   IsWindowAbove(root, organizerWindow);
        }

        public static bool EnsureExternalWindowAboveOrganizer(
            IntPtr externalWindow,
            IntPtr organizerWindow,
            IntPtr desktopHost,
            bool requestForeground)
        {
            IntPtr root = NormalizeRootWindow(externalWindow);
            if (!IsMeaningfulExternalApplicationWindow(root, organizerWindow, desktopHost))
            {
                return false;
            }

            if (IsWindowAbove(root, organizerWindow))
            {
                return true;
            }

            // 不再为了修复单个外部窗口而移动全屏整理层。SetWindowPos 整个
            // layered window 会迫使 WPF 重绘全部图标；若外部窗口确实位于其下，
            // 只提升该外部窗口即可。
            if (!IsWindowAbove(root, organizerWindow))
            {
                // 某些 WinUI/Terminal 宿主处在独立 Z-order band，移动整理层不一定能跨过。
                // 对已经可见的普通应用仅提升其 Z 序，不改变大小和位置。
                _ = SetWindowPos(
                    root,
                    HWND_TOP,
                    0,
                    0,
                    0,
                    0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE |
                    SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS);
            }

            if (requestForeground && IsWindowVisible(root))
            {
                _ = SetForegroundWindow(root);
            }

            return IsWindowAbove(root, organizerWindow);
        }

        private static IntPtr NormalizeRootWindow(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
            {
                return IntPtr.Zero;
            }

            IntPtr root = GetAncestor(windowHandle, GA_ROOT);
            return root != IntPtr.Zero ? root : windowHandle;
        }

        private static bool IsMeaningfulExternalApplicationWindow(
            IntPtr windowHandle,
            IntPtr organizerWindow,
            IntPtr desktopHost)
        {
            if (windowHandle == IntPtr.Zero ||
                windowHandle == organizerWindow ||
                windowHandle == desktopHost ||
                !IsWindow(windowHandle) ||
                !IsWindowVisible(windowHandle) ||
                IsIconic(windowHandle) ||
                GetParent(windowHandle) != IntPtr.Zero)
            {
                return false;
            }

            _ = GetWindowThreadProcessId(windowHandle, out uint processId);
            if (processId == 0 || processId == (uint)Environment.ProcessId)
            {
                return false;
            }

            var classNameBuilder = new StringBuilder(128);
            if (GetClassName(windowHandle, classNameBuilder, classNameBuilder.Capacity) <= 0)
            {
                return false;
            }

            string className = classNameBuilder.ToString();
            if (string.Equals(className, "Progman", StringComparison.Ordinal) ||
                string.Equals(className, "WorkerW", StringComparison.Ordinal) ||
                string.Equals(className, "Shell_TrayWnd", StringComparison.Ordinal) ||
                string.Equals(className, "#32768", StringComparison.Ordinal) ||
                className.Contains("Tooltip", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("tooltips", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("PopupWindow", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("InputSite", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("IME", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("Notification", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("ForegroundStaging", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!GetWindowRect(windowHandle, out NativeRect rect))
            {
                return false;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            return width >= 120 && height >= 80;
        }

        private static bool IsWindowAbove(IntPtr upperWindow, IntPtr lowerWindow)
        {
            if (upperWindow == IntPtr.Zero || lowerWindow == IntPtr.Zero || upperWindow == lowerWindow)
            {
                return false;
            }

            IntPtr current = GetWindow(lowerWindow, GW_HWNDPREV);
            for (int index = 0; index < 4096 && current != IntPtr.Zero; index++)
            {
                if (current == upperWindow)
                {
                    return true;
                }
                current = GetWindow(current, GW_HWNDPREV);
            }

            return false;
        }

        public static bool IsWindowAttachedToDesktop(IntPtr windowHandle, IntPtr desktopHost)
        {
            if (windowHandle == IntPtr.Zero ||
                desktopHost == IntPtr.Zero ||
                !IsWindow(windowHandle) ||
                !IsReliableDesktopHost(desktopHost))
            {
                return false;
            }

            // 运行期只要求整理窗口仍是顶级窗口并位于 Progman 之上。Explorer、
            // Spotlight、桌面搜索和通知宿主可能临时插入二者之间；把“必须紧邻”
            // 作为健康条件会反复 SetWindowPos 全屏 layered window，造成整层闪烁。
            // 初次附着仍由 EnsureWindowInDesktopLayer 精确放到 Progman 正上方。
            return GetParent(windowHandle) == IntPtr.Zero &&
                   IsWindowAbove(windowHandle, desktopHost);
        }

        /// <summary>
        /// 确保整理窗口位于桌面 Shell 窗口正上方，而不越过任何普通应用窗口。
        /// </summary>
        public static bool EnsureWindowInDesktopLayer(
            IntPtr windowHandle,
            IntPtr desktopHost,
            bool frameChanged = false)
        {
            if (windowHandle == IntPtr.Zero ||
                desktopHost == IntPtr.Zero ||
                !IsWindow(windowHandle) ||
                !IsReliableDesktopHost(desktopHost))
            {
                return false;
            }

            _ = SetParent(windowHandle, IntPtr.Zero);
            SetWindowLongPtr(windowHandle, GWL_HWNDPARENT, IntPtr.Zero);

            // hWndInsertAfter 表示“放在该窗口之后（其下方）”。取 Progman 上方的
            // 顶级窗口作为插入点，就能把整理窗口精确放在它与 Progman 之间。
            IntPtr windowAboveDesktop = GetWindow(desktopHost, GW_HWNDPREV);
            if (windowAboveDesktop == windowHandle)
            {
                return true;
            }

            IntPtr insertAfter = windowAboveDesktop == IntPtr.Zero
                ? HWND_TOP
                : windowAboveDesktop;

            uint flags = SWP_NOMOVE |
                         SWP_NOSIZE |
                         SWP_NOACTIVATE |
                         SWP_NOOWNERZORDER;
            if (!IsWindowVisible(windowHandle))
            {
                flags |= SWP_SHOWWINDOW;
            }
            if (frameChanged)
            {
                flags |= SWP_FRAMECHANGED;
            }

            if (!SetWindowPos(windowHandle, insertAfter, 0, 0, 0, 0, flags))
            {
                return false;
            }

            // 成功后，整理窗口的下一个顶级窗口应当就是 Progman。
            return GetWindow(windowHandle, GW_HWNDNEXT) == desktopHost ||
                   GetWindow(desktopHost, GW_HWNDPREV) == windowHandle;
        }

        private static bool IsReliableDesktopHost(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle) || !IsWindowVisible(windowHandle))
            {
                return false;
            }

            var className = new StringBuilder(64);
            if (GetClassName(windowHandle, className, className.Capacity) <= 0)
            {
                return false;
            }

            // 诊断报告确认当前系统的真实 ShellWindow 就是 Progman。WorkerW 数量很多，
            // 且多数属于隐藏系统组件，因此不再把任意 WorkerW 当作桌面宿主。
            if (!string.Equals(className.ToString(), "Progman", StringComparison.Ordinal))
            {
                return false;
            }

            return TryGetClientSize(windowHandle, out int width, out int height) &&
                   width >= 200 &&
                   height >= 200;
        }

        private static IntPtr FindDesktopHostWindow()
        {
            IntPtr shellWindow = GetShellWindow();
            if (IsReliableDesktopHost(shellWindow))
            {
                return shellWindow;
            }

            IntPtr progman = FindWindow("Progman", null);
            return IsReliableDesktopHost(progman) ? progman : IntPtr.Zero;
        }

        public static bool TryGetClientSize(IntPtr windowHandle, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (windowHandle == IntPtr.Zero || !GetClientRect(windowHandle, out NativeRect rect))
            {
                return false;
            }

            width = Math.Max(1, rect.Right - rect.Left);
            height = Math.Max(1, rect.Bottom - rect.Top);
            return true;
        }

        public static void SendWindowToBottom(IntPtr windowHandle)
        {
            uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER;
            if (!IsWindowVisible(windowHandle))
            {
                flags |= SWP_SHOWWINDOW;
            }

            _ = SetWindowPos(
                windowHandle,
                HWND_BOTTOM,
                0,
                0,
                0,
                0,
                flags);
        }

    }
}
