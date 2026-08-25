// 多显示器、工作区与 DPI 信息
namespace DesktopOrganizer
{
    internal static partial class NativeMethods
    {
        internal readonly record struct DesktopMonitorNativeInfo(
            string DeviceName,
            int MonitorLeft,
            int MonitorTop,
            int MonitorRight,
            int MonitorBottom,
            int WorkLeft,
            int WorkTop,
            int WorkRight,
            int WorkBottom,
            bool IsPrimary,
            uint DpiX,
            uint DpiY);

        private delegate bool MonitorEnumProc(
            IntPtr monitor,
            IntPtr monitorDc,
            ref NativeRect monitorRect,
            IntPtr data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfoEx
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayMonitors(
            IntPtr hdc,
            IntPtr clipRect,
            MonitorEnumProc callback,
            IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(
            IntPtr monitor,
            ref MonitorInfoEx monitorInfo);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(
            IntPtr monitor,
            int dpiType,
            out uint dpiX,
            out uint dpiY);

        private const uint MonitorInfoPrimary = 0x00000001;
        private const int MonitorDpiTypeEffective = 0;
        private const uint SwpNoZOrderForDisplayBounds = 0x0004;

        public static IReadOnlyList<DesktopMonitorNativeInfo> GetDesktopMonitors()
        {
            var monitors = new List<DesktopMonitorNativeInfo>();
            MonitorEnumProc callback = delegate(
                IntPtr monitor,
                IntPtr monitorDc,
                ref NativeRect monitorRect,
                IntPtr data)
            {
                var info = new MonitorInfoEx
                {
                    Size = Marshal.SizeOf<MonitorInfoEx>(),
                    DeviceName = string.Empty
                };

                if (!GetMonitorInfo(monitor, ref info))
                {
                    return true;
                }

                uint dpiX = 96;
                uint dpiY = 96;
                try
                {
                    if (GetDpiForMonitor(
                            monitor,
                            MonitorDpiTypeEffective,
                            out uint detectedX,
                            out uint detectedY) >= 0)
                    {
                        dpiX = detectedX == 0 ? 96u : detectedX;
                        dpiY = detectedY == 0 ? 96u : detectedY;
                    }
                }
                catch (DllNotFoundException)
                {
                    // Windows 8.1 之前不存在 shcore；项目目标系统会使用 96 DPI 回退。
                }
                catch (EntryPointNotFoundException)
                {
                    // 精简系统缺失入口点时不影响多显示器枚举。
                }

                monitors.Add(new DesktopMonitorNativeInfo(
                    string.IsNullOrWhiteSpace(info.DeviceName)
                        ? $"MONITOR-{monitors.Count + 1}"
                        : info.DeviceName,
                    info.Monitor.Left,
                    info.Monitor.Top,
                    info.Monitor.Right,
                    info.Monitor.Bottom,
                    info.Work.Left,
                    info.Work.Top,
                    info.Work.Right,
                    info.Work.Bottom,
                    (info.Flags & MonitorInfoPrimary) != 0,
                    dpiX,
                    dpiY));
                return true;
            };

            _ = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            return monitors;
        }

        public static bool SetDesktopWindowBounds(
            IntPtr windowHandle,
            int left,
            int top,
            int width,
            int height)
        {
            if (windowHandle == IntPtr.Zero || width <= 0 || height <= 0)
            {
                return false;
            }

            return SetWindowPos(
                windowHandle,
                IntPtr.Zero,
                left,
                top,
                width,
                height,
                SwpNoZOrderForDisplayBounds |
                SWP_NOACTIVATE |
                SWP_NOOWNERZORDER);
        }
    }
}
