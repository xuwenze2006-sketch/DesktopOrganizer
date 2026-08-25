// Explorer 桌面图标与 Shell 图标
// 本文件由 v1.12 Win32 互操作模块拆分；P/Invoke 与行为保持自 v1.11.6 不变。
namespace DesktopOrganizer
{
    internal static partial class NativeMethods
    {
        private static IntPtr FindDesktopListView()
        {
            // SysListView32 在 Explorer 生命周期内保持稳定。缓存句柄可避免每 15 秒
            // 在 UI 线程上重复 EnumWindows；Explorer 重启后 IsWindow 会使缓存自动失效。
            if (_cachedDesktopListView != IntPtr.Zero && IsWindow(_cachedDesktopListView))
            {
                return _cachedDesktopListView;
            }

            _cachedDesktopListView = IntPtr.Zero;
            IntPtr result = IntPtr.Zero;

            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero)
                {
                    result = FindDesktopListViewUnder(defView);
                }
            }

            if (result != IntPtr.Zero)
            {
                _cachedDesktopListView = result;
                return result;
            }

            EnumWindows((topWindow, _) =>
            {
                IntPtr defView = FindWindowEx(topWindow, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView == IntPtr.Zero)
                {
                    return true;
                }

                IntPtr listView = FindDesktopListViewUnder(defView);
                if (listView == IntPtr.Zero)
                {
                    return true;
                }

                result = listView;
                return false;
            }, IntPtr.Zero);

            _cachedDesktopListView = result;
            return result;
        }


        private static IntPtr FindDesktopListViewUnder(IntPtr defView)
        {
            IntPtr listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
            return listView != IntPtr.Zero
                ? listView
                : FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        }

        /// <summary>隐藏原生桌面图标，并返回调用前是否可见。</summary>
        public static bool HideNativeDesktopIcons()
        {
            IntPtr listView = FindDesktopListView();
            if (listView == IntPtr.Zero)
            {
                return false;
            }

            bool wasVisible = IsWindowVisible(listView);
            if (wasVisible)
            {
                _ = ShowWindow(listView, SW_HIDE);
            }

            return wasVisible;
        }

        public static bool EnsureNativeDesktopIconsHidden()
        {
            IntPtr listView = FindDesktopListView();
            if (listView == IntPtr.Zero || !IsWindowVisible(listView))
            {
                return false;
            }

            _ = ShowWindow(listView, SW_HIDE);
            return true;
        }

        public static void RestoreNativeDesktopIcons()
        {
            IntPtr listView = FindDesktopListView();
            if (listView != IntPtr.Zero && !IsWindowVisible(listView))
            {
                _ = ShowWindow(listView, SW_SHOW);
            }
        }

        /// <summary>返回调用方负责 DestroyIcon 的大尺寸 Shell 图标句柄。</summary>
        public static IntPtr GetLargeShellIconHandle(string path, bool useFileAttributes = false)
        {
            const uint fileAttributeNormal = 0x00000080;
            const uint shgfiUseFileAttributes = 0x000000010;
            uint flags = SHGFI_ICON | SHGFI_LARGEICON;
            uint attributes = 0;
            if (useFileAttributes)
            {
                flags |= shgfiUseFileAttributes;
                attributes = fileAttributeNormal;
            }

            IntPtr result = SHGetFileInfo(
                path,
                attributes,
                out SHFILEINFO info,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                flags);

            return result == IntPtr.Zero ? IntPtr.Zero : info.hIcon;
        }

    }
}
