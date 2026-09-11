namespace DesktopOrganizer
{
    /// <summary>
    /// 过滤会真正改变虚拟桌面几何的窗口消息。WM_SETTINGCHANGE 还会用于主题、
    /// 壁纸、辅助功能和大量 Shell 状态；只有 SPI_SETWORKAREA 需要重算布局。
    /// </summary>
    internal static class DesktopWindowMessagePolicy
    {
        internal const int WmSettingChange = 0x001A;
        internal const int WmDisplayChange = 0x007E;
        internal const int WmDpiChanged = 0x02E0;
        internal const int SpiSetWorkArea = 0x002F;

        public static bool IsGeometryChangeMessage(int message, IntPtr wParam)
        {
            return message == WmDisplayChange ||
                   message == WmDpiChanged ||
                   (message == WmSettingChange && wParam.ToInt64() == SpiSetWorkArea);
        }
    }
}
