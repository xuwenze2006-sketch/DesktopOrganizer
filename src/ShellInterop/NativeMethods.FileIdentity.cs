namespace DesktopOrganizer
{
    internal static partial class NativeMethods
    {
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint FileShareDelete = 0x00000004;
        private const uint OpenExisting = 3;
        private const uint FileFlagBackupSemantics = 0x02000000;

        private enum FileInfoByHandleClass
        {
            FileIdInfo = 18
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileId128
        {
            public ulong Part0;
            public ulong Part1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileIdInfo
        {
            public ulong VolumeSerialNumber;
            public FileId128 FileId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            Microsoft.Win32.SafeHandles.SafeFileHandle hFile,
            FileInfoByHandleClass fileInformationClass,
            out FileIdInfo fileInformation,
            uint bufferSize);

        internal static bool TryGetFileIdentity(string fullPath, out string? identity)
        {
            identity = null;
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return false;
            }

            try
            {
                using Microsoft.Win32.SafeHandles.SafeFileHandle handle = CreateFileW(
                    fullPath,
                    dwDesiredAccess: 0,
                    FileShareRead | FileShareWrite | FileShareDelete,
                    IntPtr.Zero,
                    OpenExisting,
                    FileFlagBackupSemantics,
                    IntPtr.Zero);

                if (handle.IsInvalid || !GetFileInformationByHandleEx(
                        handle,
                        FileInfoByHandleClass.FileIdInfo,
                        out FileIdInfo info,
                        (uint)Marshal.SizeOf<FileIdInfo>()))
                {
                    return false;
                }

                identity = $"{info.VolumeSerialNumber:X16}:{info.FileId.Part1:X16}{info.FileId.Part0:X16}";
                return true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return false;
            }
        }
    }
}
