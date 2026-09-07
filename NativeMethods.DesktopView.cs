// 通过 Explorer 的桌面视图控制图标显示，保留 Shell 菜单的文件夹上下文。
namespace DesktopOrganizer;

internal static partial class NativeMethods
{
    private static bool TrySetShellDesktopIconsVisible(bool visible, out bool changed)
    {
        changed = false;
        object? shellWindows = null;
        object? desktop = null;
        IShellBrowser? browser = null;
        object? view = null;
        try
        {
            // SWC_DESKTOP + SWFO_NEEDDISPATCH：只取系统桌面，不取前台资源管理器窗口。
            Type shellWindowsType = Type.GetTypeFromCLSID(
                new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), throwOnError: true)!;
            shellWindows = Activator.CreateInstance(shellWindowsType)!;
            object location = 0;
            object root = 0;
            int desktopHandle;
            desktop = ((dynamic)shellWindows).FindWindowSW(
                ref location, ref root, 8, out desktopHandle, 1);
            if (desktop is not IShellServiceProvider provider)
                return false;

            Guid browserService = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
            Guid browserId = typeof(IShellBrowser).GUID;
            if (provider.QueryService(ref browserService, ref browserId, out browser) < 0 ||
                browser == null || browser.QueryActiveShellView(out view) < 0 ||
                view is not IDesktopFolderView2 folderView)
                return false;

            return DesktopIconVisibility.TrySet(
                visible,
                () => folderView.GetCurrentFolderFlags(out uint flags) >= 0 ? flags : null,
                (mask, flags) => folderView.SetCurrentFolderFlags(mask, flags) >= 0,
                out changed);
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or
            Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            // Explorer 尚未就绪或刚刚重启：保留图标，让现有低频巡检重试。
            // 不能回退到 ShowWindow(SW_HIDE)，否则会重新破坏桌面菜单上下文。
            return false;
        }
        finally
        {
            ReleaseComObject(view);
            ReleaseComObject(browser);
            ReleaseComObject(desktop);
            ReleaseComObject(shellWindows);
        }
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out IShellBrowser? browser);
    }

    // 按 Windows SDK 的继承顺序声明至需要的方法；未使用的方法仍必须保留槽位。
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        [PreserveSig] int GetWindow(out IntPtr window);
        [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enterMode);
        [PreserveSig] int InsertMenusSB(IntPtr menu, IntPtr widths);
        [PreserveSig] int SetMenuSB(IntPtr menu, IntPtr reserved, IntPtr activeObject);
        [PreserveSig] int RemoveMenusSB(IntPtr menu);
        [PreserveSig] int SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int EnableModelessSB([MarshalAs(UnmanagedType.Bool)] bool enable);
        [PreserveSig] int TranslateAcceleratorSB(IntPtr message, ushort id);
        [PreserveSig] int BrowseObject(IntPtr pidl, uint flags);
        [PreserveSig] int GetViewStateStream(uint mode, out IntPtr stream);
        [PreserveSig] int GetControlWindow(uint id, out IntPtr window);
        [PreserveSig] int SendControlMsg(uint id, uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
        [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out object? view);
    }

    [ComImport, Guid("1AF3A467-214F-4298-908E-06B03E0B39F9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopFolderView2
    {
        // IFolderView
        [PreserveSig] int GetCurrentViewMode(out uint mode);
        [PreserveSig] int SetCurrentViewMode(uint mode);
        [PreserveSig] int GetFolder(ref Guid interfaceId, out IntPtr folder);
        [PreserveSig] int Item(int index, out IntPtr pidl);
        [PreserveSig] int ItemCount(uint flags, out int count);
        [PreserveSig] int Items(uint flags, ref Guid interfaceId, out IntPtr items);
        [PreserveSig] int GetSelectionMarkedItem(out int index);
        [PreserveSig] int GetFocusedItem(out int index);
        [PreserveSig] int GetItemPosition(IntPtr pidl, IntPtr point);
        [PreserveSig] int GetSpacing(IntPtr point);
        [PreserveSig] int GetDefaultSpacing(IntPtr point);
        [PreserveSig] int GetAutoArrange();
        [PreserveSig] int SelectItem(int index, uint flags);
        [PreserveSig] int SelectAndPositionItems(uint count, IntPtr pidls, IntPtr points, uint flags);
        // IFolderView2
        [PreserveSig] int SetGroupBy(IntPtr key, [MarshalAs(UnmanagedType.Bool)] bool ascending);
        [PreserveSig] int GetGroupBy(IntPtr key, IntPtr ascending);
        [PreserveSig] int SetViewProperty(IntPtr pidl, IntPtr key, IntPtr value);
        [PreserveSig] int GetViewProperty(IntPtr pidl, IntPtr key, IntPtr value);
        [PreserveSig] int SetTileViewProperties(IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string properties);
        [PreserveSig] int SetExtendedTileViewProperties(IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string properties);
        [PreserveSig] int SetText(int type, [MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int SetCurrentFolderFlags(uint mask, uint flags);
        [PreserveSig] int GetCurrentFolderFlags(out uint flags);
    }
}
