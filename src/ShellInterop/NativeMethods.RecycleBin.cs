// Windows 回收站状态查询与清空
namespace DesktopOrganizer
{
    internal static partial class NativeMethods
    {
        private const uint SherbNoConfirmation = 0x00000001;
        private const uint SherbNoProgressUi = 0x00000002;
        private const uint SherbNoSound = 0x00000004;

        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO
        {
            public uint cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHQueryRecycleBinW(
            string? pszRootPath,
            ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHEmptyRecycleBinW(
            IntPtr hwnd,
            string? pszRootPath,
            uint dwFlags);

        internal static RecycleBinStatus QueryRecycleBinStatus()
        {
            var info = new SHQUERYRBINFO
            {
                cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>()
            };
            int result = SHQueryRecycleBinW(null, ref info);
            return result >= 0
                ? new RecycleBinStatus(true, Math.Max(0, info.i64NumItems), Math.Max(0, info.i64Size), 0)
                : new RecycleBinStatus(false, 0, 0, result);
        }

        internal static int EmptyRecycleBin(IntPtr owner) =>
            SHEmptyRecycleBinW(
                owner,
                null,
                SherbNoConfirmation | SherbNoProgressUi | SherbNoSound);
    }

    internal readonly record struct RecycleBinStatus(
        bool IsAvailable,
        long ItemCount,
        long SizeBytes,
        int ErrorCode);
}
