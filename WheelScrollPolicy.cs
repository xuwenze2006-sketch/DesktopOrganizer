namespace DesktopOrganizer
{
    internal static class WheelScrollPolicy
    {
        // Win32 WHEEL_PAGESCROLL (UINT_MAX) 通过 WPF 的有符号 int 属性表现为 -1。
        public const int PageScroll = unchecked((int)0xFFFFFFFFu);

        public static double GetVerticalDistance(
            int wheelScrollLines,
            double rowHeight,
            double viewportHeight)
        {
            if (wheelScrollLines == 0)
            {
                return 0;
            }

            if (wheelScrollLines == PageScroll)
            {
                return double.IsFinite(viewportHeight) && viewportHeight > 0
                    ? viewportHeight
                    : 0;
            }

            if (wheelScrollLines < 0 ||
                !double.IsFinite(rowHeight) ||
                rowHeight <= 0)
            {
                return 0;
            }

            return wheelScrollLines * rowHeight;
        }
    }
}
