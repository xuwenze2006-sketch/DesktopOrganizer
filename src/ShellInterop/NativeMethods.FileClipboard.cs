namespace DesktopOrganizer;

internal static partial class NativeMethods
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")]
    internal static extern uint GetClipboardSequenceNumber();
    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr reserved);
    [DllImport("ole32.dll")]
    private static extern void OleUninitialize();
    internal static bool HasClipboardFiles() => IsClipboardFormatAvailable(15); // CF_HDROP

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileClipboardContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref ClipboardCommandInfo info);
        [PreserveSig] int GetCommandString(UIntPtr command, uint flags, IntPtr reserved, IntPtr text, uint length);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ClipboardCommandInfo
    {
        public int Size;
        public uint Mask;
        public IntPtr Owner;
        public IntPtr Verb;
        public IntPtr Parameters;
        public IntPtr Directory;
        public int Show;
        public uint HotKey;
        public IntPtr Icon;
    }

    internal static void PasteClipboardFiles(string targetFolder, IntPtr owner, uint expectedSequence)
    {
        if (!Path.IsPathFullyQualified(targetFolder) || !System.IO.Directory.Exists(targetFolder))
            throw new IOException("粘贴目标文件夹已不可用。");
        Marshal.ThrowExceptionForHR(OleInitialize(IntPtr.Zero));
        IShellFolder? desktop = null;
        IShellFolder? folder = null;
        IFileClipboardContextMenu? context = null;
        IntPtr pidl = IntPtr.Zero, folderPointer = IntPtr.Zero, contextPointer = IntPtr.Zero;
        IntPtr menu = IntPtr.Zero, verb = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(SHGetDesktopFolder(out desktop));
            Marshal.ThrowExceptionForHR(SHParseDisplayName(targetFolder, IntPtr.Zero, out pidl, 0, out _));
            Guid folderId = typeof(IShellFolder).GUID;
            Marshal.ThrowExceptionForHR(desktop.BindToObject(pidl, IntPtr.Zero, ref folderId, out folderPointer));
            folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPointer);
            Guid contextId = typeof(IFileClipboardContextMenu).GUID;
            Marshal.ThrowExceptionForHR(folder.CreateViewObject(owner, ref contextId, out contextPointer));
            context = (IFileClipboardContextMenu)Marshal.GetObjectForIUnknown(contextPointer);
            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Marshal.ThrowExceptionForHR(context.QueryContextMenu(menu, 0, 1, 0x7fff, 0));
            if (GetClipboardSequenceNumber() != expectedSequence || !HasClipboardFiles())
                throw new InvalidOperationException("剪贴板已变化，请重新执行粘贴。");
            verb = Marshal.StringToCoTaskMemAnsi("paste");
            var command = new ClipboardCommandInfo
            {
                Size = Marshal.SizeOf<ClipboardCommandInfo>(), Mask = 0x100, // CMIC_MASK_NOASYNC
                Owner = owner, Verb = verb, Show = 1
            };
            Marshal.ThrowExceptionForHR(context.InvokeCommand(ref command));
        }
        finally
        {
            if (verb != IntPtr.Zero) Marshal.FreeCoTaskMem(verb);
            if (menu != IntPtr.Zero) DestroyMenu(menu);
            ReleaseComObject(context);
            if (contextPointer != IntPtr.Zero) Marshal.Release(contextPointer);
            ReleaseComObject(folder);
            if (folderPointer != IntPtr.Zero) Marshal.Release(folderPointer);
            if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
            ReleaseComObject(desktop);
            OleUninitialize();
        }
    }
}
