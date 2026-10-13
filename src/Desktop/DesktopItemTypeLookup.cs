namespace DesktopOrganizer
{
    /// <summary>
    /// UI 线程共用已接受扫描中的文件类型。未知路径仍查询磁盘，回退结果不跨扫描缓存。
    /// 只保存本次成功读取属性的路径，不从持久化身份中的默认值推断类型。
    /// </summary>
    internal sealed class DesktopItemTypeLookup
    {
        private readonly Func<string, bool> _directoryExists;
        private readonly Func<string, bool> _fileExists;
        private Dictionary<string, bool> _knownTypes = new(StringComparer.OrdinalIgnoreCase);

        public DesktopItemTypeLookup(
            Func<string, bool>? directoryExists = null,
            Func<string, bool>? fileExists = null)
        {
            _directoryExists = directoryExists ?? Directory.Exists;
            _fileExists = fileExists ?? File.Exists;
        }

        public void ReplaceSnapshot(IReadOnlyDictionary<string, bool> knownTypes) =>
            _knownTypes = new Dictionary<string, bool>(knownTypes, StringComparer.OrdinalIgnoreCase);

        public bool IsFolderLike(string location)
        {
            if (ShellItemLocation.TryDecode(location, out _, out bool shellFolder)) return shellFolder;
            return _knownTypes.TryGetValue(location, out bool isDirectory)
                ? isDirectory
                : _directoryExists(location);
        }

        public string GetVisualKind(string location)
        {
            if (ShellItemLocation.TryDecode(location, out _, out bool shellFolder))
                return shellFolder ? "shell-folder" : "shell-item";
            if (_knownTypes.TryGetValue(location, out bool isDirectory))
                return isDirectory ? "file-system-folder" : "file-system-file";
            return _directoryExists(location) ? "file-system-folder"
                : _fileExists(location) ? "file-system-file" : "missing";
        }

        public string GetTypeSortKey(string location)
        {
            if (ShellItemLocation.TryDecode(location, out _, out bool shellFolder))
                return shellFolder ? string.Empty : ".shell";
            return IsFolderLike(location) ? string.Empty : Path.GetExtension(location);
        }

        public string GetIconCacheKey(string location)
        {
            if (ShellItemLocation.TryDecode(location, out string parsingName, out _))
                return "shell:" + parsingName;
            if (IsFolderLike(location)) return "folder:" + location;

            string extension = Path.GetExtension(location).ToLowerInvariant();
            return UsesSharedExtensionIcon(extension)
                ? "ext:" + extension
                : "path:" + location;
        }

        internal static bool UsesSharedExtensionIcon(string extension) =>
            !string.IsNullOrEmpty(extension) &&
            !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".url", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".ico", StringComparison.OrdinalIgnoreCase);
    }
}
