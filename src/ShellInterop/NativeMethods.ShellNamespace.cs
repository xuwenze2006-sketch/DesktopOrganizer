// Windows Shell 桌面命名空间枚举与虚拟项目图标
namespace DesktopOrganizer
{
    internal static partial class NativeMethods
    {
        [Flags]
        private enum ShellContentFlags : uint
        {
            Folders = 0x20,
            NonFolders = 0x40
        }

        [ComImport]
        [Guid("000214E6-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellFolder
        {
            [PreserveSig]
            int ParseDisplayName(
                IntPtr hwnd,
                IntPtr bindContext,
                [MarshalAs(UnmanagedType.LPWStr)] string displayName,
                out uint eaten,
                out IntPtr childPidl,
                ref uint attributes);

            [PreserveSig]
            int EnumObjects(
                IntPtr hwnd,
                ShellContentFlags flags,
                [MarshalAs(UnmanagedType.Interface)] out IEnumIDList? enumerator);

            [PreserveSig]
            int BindToObject(IntPtr pidl, IntPtr bindContext, ref Guid interfaceId, out IntPtr result);

            [PreserveSig]
            int BindToStorage(IntPtr pidl, IntPtr bindContext, ref Guid interfaceId, out IntPtr result);

            [PreserveSig]
            int CompareIDs(IntPtr parameter, IntPtr firstPidl, IntPtr secondPidl);

            [PreserveSig]
            int CreateViewObject(IntPtr owner, ref Guid interfaceId, out IntPtr result);

            [PreserveSig]
            int GetAttributesOf(
                uint count,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] pidls,
                ref uint attributes);

            [PreserveSig]
            int GetUIObjectOf(
                IntPtr owner,
                uint count,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] pidls,
                ref Guid interfaceId,
                IntPtr reserved,
                out IntPtr result);

            [PreserveSig]
            int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr name);

            [PreserveSig]
            int SetNameOf(
                IntPtr owner,
                IntPtr pidl,
                [MarshalAs(UnmanagedType.LPWStr)] string name,
                uint flags,
                out IntPtr newPidl);
        }

        [ComImport]
        [Guid("000214F2-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumIDList
        {
            [PreserveSig]
            int Next(uint count, out IntPtr pidl, out uint fetched);

            [PreserveSig]
            int Skip(uint count);

            [PreserveSig]
            int Reset();

            [PreserveSig]
            int Clone([MarshalAs(UnmanagedType.Interface)] out IEnumIDList clone);
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetDesktopFolder(
            [MarshalAs(UnmanagedType.Interface)] out IShellFolder desktopFolder);

        [DllImport("shell32.dll")]
        private static extern IntPtr ILCombine(IntPtr firstPidl, IntPtr secondPidl);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetNameFromIDList(
            IntPtr pidl,
            uint nameType,
            out IntPtr name);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(
            string name,
            IntPtr bindContext,
            out IntPtr pidl,
            uint attributesIn,
            out uint attributesOut);

        [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfoFromPidl(
            IntPtr pidl,
            uint fileAttributes,
            out SHFILEINFO info,
            uint infoSize,
            uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIconOrMonitor;
            public IntPtr hProcess;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO executeInfo);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr reserved, uint apartmentModel);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        private const uint CoinitMultithreaded = 0x0;
        private const uint CoinitApartmentThreaded = 0x2;
        private const uint CoinitDisableOle1Dde = 0x4;
        private const int RpcEChangedMode = unchecked((int)0x80010106);
        private const uint SigdnNormalDisplay = 0x00000000;
        private const uint SigdnDesktopAbsoluteParsing = 0x80028000;
        private const uint SigdnFileSystemPath = 0x80058000;
        private const uint SfgaoHidden = 0x00080000;
        private const uint SfgaoNonEnumerated = 0x00100000;
        private const uint SfgaoFolder = 0x20000000;
        private const uint SfgaoFileSystem = 0x40000000;
        private const uint ShgfiPidl = 0x000000008;
        private const uint SeeMaskInvokeIdList = 0x0000000C;
        private const int SwShowNormal = 1;

        public static bool TryInitializeShellWorkerApartment(out bool shouldUninitialize)
        {
            int initializeResult = CoInitializeEx(
                IntPtr.Zero,
                CoinitApartmentThreaded | CoinitDisableOle1Dde);
            shouldUninitialize = initializeResult is 0 or 1;
            return initializeResult >= 0 || initializeResult == RpcEChangedMode;
        }

        public static void UninitializeShellWorkerApartment() => CoUninitialize();

        /// <summary>
        /// 枚举 Explorer 桌面根命名空间中的可见项目。
        /// 调用方根据 FileSystemPath 与已扫描的用户/公共桌面路径去重，
        /// 从而既避免重复真实文件，也保留“用户文件”等文件系统支持的 Shell 特殊项。
        /// </summary>
        public static IReadOnlyList<ShellNamespaceItem> EnumerateShellDesktopItems()
        {
            return TryEnumerateShellDesktopItems(out IReadOnlyList<ShellNamespaceItem> items)
                ? items
                : Array.Empty<ShellNamespaceItem>();
        }

        /// <summary>
        /// 尝试枚举桌面 Shell 命名空间。返回 false 表示本轮 COM/Shell 枚举不完整，
        /// 调用方应保留上一份快照，而不是把空集合当成“所有系统项目均已删除”。
        /// </summary>
        public static bool TryEnumerateShellDesktopItems(
            out IReadOnlyList<ShellNamespaceItem> resultItems)
        {
            resultItems = Array.Empty<ShellNamespaceItem>();
            int initializeResult = CoInitializeEx(IntPtr.Zero, CoinitMultithreaded);
            bool shouldUninitialize = initializeResult is 0 or 1;
            if (initializeResult < 0 && initializeResult != RpcEChangedMode)
            {
                return false;
            }

            IShellFolder? desktopFolder = null;
            IEnumIDList? enumerator = null;
            try
            {
                if (SHGetDesktopFolder(out desktopFolder) < 0 ||
                    desktopFolder.EnumObjects(
                        IntPtr.Zero,
                        ShellContentFlags.Folders | ShellContentFlags.NonFolders,
                        out enumerator) < 0 ||
                    enumerator == null)
                {
                    return false;
                }

                var items = new List<ShellNamespaceItem>();
                while (true)
                {
                    int nextResult = enumerator.Next(1, out IntPtr childPidl, out uint fetched);
                    if (nextResult < 0)
                    {
                        return false;
                    }

                    if (fetched == 0 || childPidl == IntPtr.Zero)
                    {
                        resultItems = items;
                        return true;
                    }

                    try
                    {
                        uint attributes = SfgaoFileSystem | SfgaoFolder | SfgaoHidden | SfgaoNonEnumerated;
                        int attributeResult = desktopFolder.GetAttributesOf(
                            1,
                            new[] { childPidl },
                            ref attributes);
                        if (attributeResult < 0 ||
                            (attributes & (SfgaoHidden | SfgaoNonEnumerated)) != 0)
                        {
                            continue;
                        }

                        IntPtr absolutePidl = ILCombine(IntPtr.Zero, childPidl);
                        if (absolutePidl == IntPtr.Zero)
                        {
                            continue;
                        }

                        try
                        {
                            string? displayName = GetNameFromPidl(absolutePidl, SigdnNormalDisplay);
                            string? parsingName = GetNameFromPidl(absolutePidl, SigdnDesktopAbsoluteParsing);
                            if (string.IsNullOrWhiteSpace(displayName) ||
                                string.IsNullOrWhiteSpace(parsingName))
                            {
                                continue;
                            }

                            string? fileSystemPath = (attributes & SfgaoFileSystem) != 0
                                ? GetNameFromPidl(absolutePidl, SigdnFileSystemPath)
                                : null;
                            items.Add(new ShellNamespaceItem(
                                displayName.Trim(),
                                parsingName.Trim(),
                                string.IsNullOrWhiteSpace(fileSystemPath) ? null : fileSystemPath.Trim(),
                                (attributes & SfgaoFolder) != 0));
                        }
                        finally
                        {
                            ILFree(absolutePidl);
                        }
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(childPidl);
                    }

                    if (nextResult != 0)
                    {
                        resultItems = items;
                        return true;
                    }
                }
            }
            catch (Exception exception) when (
                exception is COMException or InvalidCastException or ArgumentException)
            {
                resultItems = Array.Empty<ShellNamespaceItem>();
                return false;
            }
            finally
            {
                ReleaseComObject(enumerator);
                ReleaseComObject(desktopFolder);
                if (shouldUninitialize)
                {
                    CoUninitialize();
                }
            }
        }

        /// <summary>返回调用方负责 DestroyIcon 的 Shell 命名空间项目图标。</summary>
        public static IntPtr GetLargeShellNamespaceIconHandle(string parsingName)
        {
            if (string.IsNullOrWhiteSpace(parsingName) ||
                SHParseDisplayName(parsingName, IntPtr.Zero, out IntPtr pidl, 0, out _) < 0 ||
                pidl == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            try
            {
                IntPtr result = SHGetFileInfoFromPidl(
                    pidl,
                    0,
                    out SHFILEINFO info,
                    (uint)Marshal.SizeOf<SHFILEINFO>(),
                    ShgfiPidl | SHGFI_ICON | SHGFI_LARGEICON);
                return result == IntPtr.Zero ? IntPtr.Zero : info.hIcon;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }

        /// <summary>
        /// 通过 Shell PIDL 执行默认动作或指定动词；可打开非文件系统项目及其属性页。
        /// </summary>
        public static bool ExecuteShellNamespaceItem(
            string parsingName,
            string? verb,
            IntPtr owner,
            out int errorCode)
        {
            errorCode = 0;
            if (string.IsNullOrWhiteSpace(parsingName))
            {
                return false;
            }

            int parseResult = SHParseDisplayName(
                parsingName,
                IntPtr.Zero,
                out IntPtr pidl,
                0,
                out _);
            if (parseResult < 0 || pidl == IntPtr.Zero)
            {
                errorCode = parseResult;
                return false;
            }

            try
            {
                var executeInfo = new SHELLEXECUTEINFO
                {
                    cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                    fMask = SeeMaskInvokeIdList,
                    hwnd = owner,
                    lpVerb = string.IsNullOrWhiteSpace(verb) ? null : verb,
                    nShow = SwShowNormal,
                    lpIDList = pidl
                };

                bool succeeded = ShellExecuteEx(ref executeInfo);
                if (!succeeded)
                {
                    errorCode = Marshal.GetLastWin32Error();
                }

                return succeeded;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }

        private static string? GetNameFromPidl(IntPtr pidl, uint nameType)
        {
            if (SHGetNameFromIDList(pidl, nameType, out IntPtr namePointer) < 0 ||
                namePointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(namePointer);
            }
            finally
            {
                Marshal.FreeCoTaskMem(namePointer);
            }
        }

        private static void ReleaseComObject(object? value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                _ = Marshal.FinalReleaseComObject(value);
            }
        }
    }
}
