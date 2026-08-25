// 原生通知区域图标与菜单
// 本文件由 v1.12 Win32 互操作模块拆分；P/Invoke 与行为保持自 v1.11.6 不变。
namespace DesktopOrganizer
{
    internal static partial class NativeMethods
    {
        // ==================== 通知区域图标与原生菜单 ====================

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;

            public uint dwState;
            public uint dwStateMask;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;

            public uint uTimeoutOrVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;

            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint TrackPopupMenu(
            IntPtr hMenu,
            uint uFlags,
            int x,
            int y,
            int nReserved,
            IntPtr hWnd,
            IntPtr prcRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint NimAdd = 0x00000000;
        private const uint NimModify = 0x00000001;
        private const uint NimDelete = 0x00000002;
        private const uint NifMessage = 0x00000001;
        private const uint NifIcon = 0x00000002;
        private const uint NifTip = 0x00000004;
        private const int IdiApplication = 32512;

        private const uint MfString = 0x00000000;
        private const uint MfSeparator = 0x00000800;
        private const uint MfChecked = 0x00000008;
        private const uint TpmRightButton = 0x0002;
        private const uint TpmReturnCommand = 0x0100;
        private const uint WmNull = 0x0000;

        internal const int TrayCommandTogglePanel = 1001;
        internal const int TrayCommandPauseResume = 1002;
        internal const int TrayCommandRefresh = 1003;
        internal const int TrayCommandToggleAutoStart = 1004;
        internal const int TrayCommandExit = 1099;

        internal static uint GetTaskbarCreatedMessage()
        {
            return RegisterWindowMessage("TaskbarCreated");
        }

        internal static bool AddTrayIcon(IntPtr windowHandle, uint iconId, uint callbackMessage, string toolTip)
        {
            NotifyIconData data = CreateNotifyIconData(windowHandle, iconId, callbackMessage, toolTip);
            return Shell_NotifyIcon(NimAdd, ref data);
        }

        internal static bool UpdateTrayIcon(IntPtr windowHandle, uint iconId, uint callbackMessage, string toolTip)
        {
            NotifyIconData data = CreateNotifyIconData(windowHandle, iconId, callbackMessage, toolTip);
            return Shell_NotifyIcon(NimModify, ref data);
        }

        internal static void RemoveTrayIcon(IntPtr windowHandle, uint iconId)
        {
            var data = new NotifyIconData
            {
                cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
                hWnd = windowHandle,
                uID = iconId,
                szTip = string.Empty,
                szInfo = string.Empty,
                szInfoTitle = string.Empty
            };

            _ = Shell_NotifyIcon(NimDelete, ref data);
        }

        private static NotifyIconData CreateNotifyIconData(
            IntPtr windowHandle,
            uint iconId,
            uint callbackMessage,
            string toolTip)
        {
            return new NotifyIconData
            {
                cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
                hWnd = windowHandle,
                uID = iconId,
                uFlags = NifMessage | NifIcon | NifTip,
                uCallbackMessage = callbackMessage,
                hIcon = GetApplicationIconHandle(),
                szTip = Truncate(toolTip, 127),
                szInfo = string.Empty,
                szInfoTitle = string.Empty
            };
        }

        private static IntPtr GetApplicationIconHandle()
        {
            IntPtr moduleHandle = GetModuleHandle(null);
            IntPtr iconHandle = moduleHandle != IntPtr.Zero
                ? LoadIcon(moduleHandle, new IntPtr(IdiApplication))
                : IntPtr.Zero;

            return iconHandle != IntPtr.Zero
                ? iconHandle
                : LoadIcon(IntPtr.Zero, new IntPtr(IdiApplication));
        }

        internal static int ShowTrayContextMenu(
            IntPtr ownerWindow,
            bool controlPanelVisible,
            bool organizerPaused,
            bool autoStartEnabled)
        {
            IntPtr menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                return 0;
            }

            try
            {
                string panelText = controlPanelVisible ? "隐藏控制栏" : "显示控制栏";
                string pauseText = organizerPaused ? "继续整理" : "暂停整理";

                _ = AppendMenu(menu, MfString, new UIntPtr((uint)TrayCommandTogglePanel), panelText);
                _ = AppendMenu(menu, MfString, new UIntPtr((uint)TrayCommandPauseResume), pauseText);
                _ = AppendMenu(menu, MfString, new UIntPtr((uint)TrayCommandRefresh), "刷新桌面");
                _ = AppendMenu(menu, MfSeparator, UIntPtr.Zero, null);
                _ = AppendMenu(
                    menu,
                    MfString | (autoStartEnabled ? MfChecked : 0),
                    new UIntPtr((uint)TrayCommandToggleAutoStart),
                    "开机自动启动");
                _ = AppendMenu(menu, MfSeparator, UIntPtr.Zero, null);
                _ = AppendMenu(menu, MfString, new UIntPtr((uint)TrayCommandExit), "退出 DesktopOrganizer");

                if (!GetCursorPos(out NativePoint cursor))
                {
                    return 0;
                }

                _ = SetForegroundWindow(ownerWindow);
                uint command = TrackPopupMenu(
                    menu,
                    TpmRightButton | TpmReturnCommand,
                    cursor.X,
                    cursor.Y,
                    0,
                    ownerWindow,
                    IntPtr.Zero);

                // 让通知区域正确关闭菜单；这是 TrackPopupMenu 的标准配套处理。
                _ = PostMessage(ownerWindow, WmNull, IntPtr.Zero, IntPtr.Zero);
                return unchecked((int)command);
            }
            finally
            {
                _ = DestroyMenu(menu);
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value[..maxLength];
        }

    }
}
