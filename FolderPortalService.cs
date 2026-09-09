namespace DesktopOrganizer
{
    /// <summary>只读文件夹入口的持久化配置。目录内容始终只存在于运行时内存中。</summary>
    internal sealed class FolderPortalInfo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "文件夹入口";
        public string RootPath { get; set; } = string.Empty;
        public string RootIdentity { get; set; } = string.Empty;
        public string CurrentRelativePath { get; set; } = string.Empty;
        public double X { get; set; } = 40;
        public double Y { get; set; } = 40;
        public double Width { get; set; } = 340;
        public double Height { get; set; } = 280;
        public bool IsCollapsed { get; set; }
    }

    /// <summary>Portal 顶层枚举返回的单个文件系统项目。</summary>
    internal sealed record PortalDirectoryEntry(
        string Name,
        string FullPath,
        bool IsDirectory,
        bool IsReparsePoint,
        DateTime LastWriteTimeUtc)
    {
        /// <summary>
        /// 重解析点可以显示和交给资源管理器打开，但 Portal 不得继续进入，
        /// 避免 junction/symlink 把枚举边界带到用户指定的根目录之外。
        /// </summary>
        public bool CanNavigate => IsDirectory && !IsReparsePoint;
    }

    /// <summary>Portal 的一次只读页面查询结果。</summary>
    internal sealed record PortalReadResult(
        bool Success,
        string RootPath,
        string CurrentPath,
        string CurrentRelativePath,
        IReadOnlyList<PortalDirectoryEntry> Entries,
        bool IsTruncated,
        string? ErrorMessage);

    internal delegate bool PortalIdentityReader(string path, out string identity);

    internal static class FolderPortalLayoutPolicy
    {
        public static List<FolderPortalInfo> Normalize(IEnumerable<FolderPortalInfo>? portals)
        {
            var result = new List<FolderPortalInfo>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rootIdentities = new HashSet<string>(StringComparer.Ordinal);
            foreach (FolderPortalInfo portal in (portals ?? Array.Empty<FolderPortalInfo>())
                         .OfType<FolderPortalInfo>())
            {
                if (!TryNormalizeRoot(portal.RootPath, out string root) ||
                    string.IsNullOrWhiteSpace(portal.RootIdentity))
                {
                    continue;
                }

                string normalizedIdentity = portal.RootIdentity.Trim();
                if (!roots.Add(root) || !rootIdentities.Add(normalizedIdentity))
                {
                    continue;
                }

                portal.Id = portal.Id?.Trim() ?? string.Empty;
                while (string.IsNullOrWhiteSpace(portal.Id) || !ids.Add(portal.Id))
                {
                    portal.Id = Guid.NewGuid().ToString("N");
                }
                portal.Name = string.IsNullOrWhiteSpace(portal.Name)
                    ? Path.GetFileName(Path.TrimEndingDirectorySeparator(root))
                    : portal.Name.Trim();
                if (string.IsNullOrWhiteSpace(portal.Name))
                {
                    portal.Name = root;
                }
                portal.RootPath = root;
                portal.RootIdentity = normalizedIdentity;
                if (!FolderPortalService.TryResolveRelativePath(
                        root,
                        portal.CurrentRelativePath,
                        out _,
                        out string relative,
                        out _))
                {
                    relative = string.Empty;
                }
                portal.CurrentRelativePath = relative;
                portal.X = double.IsFinite(portal.X) ? portal.X : 40;
                portal.Y = double.IsFinite(portal.Y) ? portal.Y : 40;
                portal.Width = Math.Clamp(
                    double.IsFinite(portal.Width) ? portal.Width : 340,
                    300,
                    720);
                portal.Height = Math.Clamp(
                    double.IsFinite(portal.Height) ? portal.Height : 280,
                    220,
                    680);
                result.Add(portal);
            }
            return result;
        }

        public static FolderPortalInfo Clone(FolderPortalInfo portal) => new()
        {
            Id = portal.Id,
            Name = portal.Name,
            RootPath = portal.RootPath,
            RootIdentity = portal.RootIdentity,
            CurrentRelativePath = portal.CurrentRelativePath,
            X = portal.X,
            Y = portal.Y,
            Width = portal.Width,
            Height = portal.Height,
            IsCollapsed = portal.IsCollapsed
        };

        private static bool TryNormalizeRoot(string? rootPath, out string root)
        {
            root = string.Empty;
            if (string.IsNullOrWhiteSpace(rootPath) || !Path.IsPathFullyQualified(rootPath))
            {
                return false;
            }
            try
            {
                root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
                return !string.IsNullOrWhiteSpace(root);
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 只读文件夹 Portal 的边界检查与顶层枚举。
    /// 服务不创建 watcher、不递归、不读取文件正文，也不执行任何文件系统写操作。
    /// </summary>
    internal sealed class FolderPortalService
    {
        public const int MaximumEntryCount = 500;

        private readonly PortalIdentityReader _tryReadIdentity;
        private readonly Func<string, bool> _directoryExists;
        private readonly Func<string, IEnumerable<string>> _enumerateTopLevelEntries;
        private readonly Func<string, FileAttributes> _getAttributes;
        private readonly Func<string, DateTime> _getLastWriteTimeUtc;

        public FolderPortalService()
            : this(
                FileOperationIdentityGuard.TryCapture,
                Directory.Exists,
                path => Directory.EnumerateFileSystemEntries(
                    path,
                    "*",
                    SearchOption.TopDirectoryOnly),
                File.GetAttributes,
                File.GetLastWriteTimeUtc)
        {
        }

        internal FolderPortalService(
            PortalIdentityReader tryReadIdentity,
            Func<string, bool> directoryExists,
            Func<string, IEnumerable<string>> enumerateTopLevelEntries,
            Func<string, FileAttributes> getAttributes,
            Func<string, DateTime> getLastWriteTimeUtc)
        {
            _tryReadIdentity = tryReadIdentity ?? throw new ArgumentNullException(nameof(tryReadIdentity));
            _directoryExists = directoryExists ?? throw new ArgumentNullException(nameof(directoryExists));
            _enumerateTopLevelEntries = enumerateTopLevelEntries ??
                throw new ArgumentNullException(nameof(enumerateTopLevelEntries));
            _getAttributes = getAttributes ?? throw new ArgumentNullException(nameof(getAttributes));
            _getLastWriteTimeUtc = getLastWriteTimeUtc ??
                throw new ArgumentNullException(nameof(getLastWriteTimeUtc));
        }

        public PortalReadResult Read(
            FolderPortalInfo portal,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(portal);
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryNormalizeRootPath(portal.RootPath, out string rootPath, out string rootError))
            {
                return Failure(portal, string.Empty, string.Empty, rootError);
            }

            if (string.IsNullOrWhiteSpace(portal.RootIdentity))
            {
                return Failure(portal, rootPath, string.Empty, "文件夹入口没有可验证的根目录身份。");
            }

            if (!_directoryExists(rootPath))
            {
                return Failure(portal, rootPath, string.Empty, "指定的根目录已不存在。");
            }

            if (!_tryReadIdentity(rootPath, out string currentRootIdentity) ||
                string.IsNullOrWhiteSpace(currentRootIdentity))
            {
                return Failure(portal, rootPath, string.Empty, "无法验证指定根目录的身份。");
            }

            if (!string.Equals(
                    portal.RootIdentity,
                    currentRootIdentity,
                    StringComparison.Ordinal))
            {
                // 必须在调用枚举委托之前失败。同路径被替换时不得扫描新目录。
                return Failure(portal, rootPath, string.Empty, "根目录身份已改变，为避免读取错误目录已停止。");
            }

            if (!TryResolveRelativePath(
                    rootPath,
                    portal.CurrentRelativePath,
                    out string currentPath,
                    out string normalizedRelativePath,
                    out string resolveError))
            {
                return Failure(portal, rootPath, string.Empty, resolveError);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!TryValidateNavigablePath(
                    rootPath,
                    normalizedRelativePath,
                    out string navigationError))
            {
                return Failure(portal, rootPath, currentPath, navigationError);
            }

            // 路径验证可能触发慢速的网络或 Shell 文件系统查询。真正枚举前再核验一次，
            // 避免在验证窗口内同路径被替换后读取到新目录。
            cancellationToken.ThrowIfCancellationRequested();
            if (!_tryReadIdentity(rootPath, out string revalidatedRootIdentity) ||
                !string.Equals(
                    portal.RootIdentity,
                    revalidatedRootIdentity,
                    StringComparison.Ordinal))
            {
                return Failure(portal, rootPath, currentPath, "根目录身份在读取前已改变，未执行枚举。");
            }

            try
            {
                var entries = new List<PortalDirectoryEntry>(MaximumEntryCount);
                bool isTruncated = false;
                using IEnumerator<string> enumerator = _enumerateTopLevelEntries(currentPath).GetEnumerator();
                while (enumerator.MoveNext())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entries.Count >= MaximumEntryCount)
                    {
                        isTruncated = true;
                        break;
                    }

                    string rawPath = enumerator.Current;
                    string fullPath = Path.GetFullPath(rawPath);
                    if (!IsDirectChild(currentPath, fullPath) ||
                        !IsPathWithinRoot(rootPath, fullPath))
                    {
                        throw new InvalidDataException("Portal 枚举器返回了指定目录之外的项目。");
                    }

                    FileAttributes attributes;
                    DateTime lastWriteTimeUtc;
                    try
                    {
                        attributes = _getAttributes(fullPath);
                        lastWriteTimeUtc = _getLastWriteTimeUtc(fullPath);
                    }
                    catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
                    {
                        if (!_directoryExists(currentPath))
                            throw;
                        // 枚举后单项可能刚被删除；其它条目仍有效。
                        // 根目录/越界/权限和枚举器本身的失败仍由外层报告。
                        continue;
                    }
                    bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                    bool isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
                    string itemName = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
                    if (string.IsNullOrWhiteSpace(itemName))
                    {
                        throw new InvalidDataException("Portal 枚举器返回了没有名称的项目。");
                    }

                    entries.Add(new PortalDirectoryEntry(
                        itemName,
                        fullPath,
                        isDirectory,
                        isReparsePoint,
                        lastWriteTimeUtc));
                }

                IReadOnlyList<PortalDirectoryEntry> ordered = entries
                    .OrderByDescending(entry => entry.IsDirectory)
                    .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                return new PortalReadResult(
                    Success: true,
                    RootPath: rootPath,
                    CurrentPath: currentPath,
                    CurrentRelativePath: normalizedRelativePath,
                    Entries: ordered,
                    IsTruncated: isTruncated,
                    ErrorMessage: null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Failure(portal, rootPath, currentPath, exception.Message);
            }
        }

        internal static bool TryResolveRelativePath(
            string rootPath,
            string? relativePath,
            out string resolvedPath,
            out string normalizedRelativePath,
            out string errorMessage)
        {
            resolvedPath = string.Empty;
            normalizedRelativePath = string.Empty;
            errorMessage = string.Empty;

            if (!TryNormalizeRootPath(rootPath, out string normalizedRoot, out errorMessage))
            {
                return false;
            }

            string requested = relativePath?.Trim() ?? string.Empty;
            if (requested.Length == 0)
            {
                resolvedPath = normalizedRoot;
                return true;
            }

            if (Path.IsPathRooted(requested) ||
                requested.Contains(Path.VolumeSeparatorChar, StringComparison.Ordinal))
            {
                errorMessage = "Portal 当前路径必须是根目录下的相对路径。";
                return false;
            }

            string[] segments = requested.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0 ||
                segments.Any(segment =>
                    segment.Length == 0 ||
                    segment.Equals(".", StringComparison.Ordinal) ||
                    segment.Equals("..", StringComparison.Ordinal)))
            {
                errorMessage = "Portal 相对路径不得包含 . 或 .. 段。";
                return false;
            }

            try
            {
                normalizedRelativePath = string.Join(Path.DirectorySeparatorChar, segments);
                string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, normalizedRelativePath));
                if (!IsPathWithinRoot(normalizedRoot, candidate))
                {
                    normalizedRelativePath = string.Empty;
                    errorMessage = "Portal 当前路径超出了指定根目录。";
                    return false;
                }

                resolvedPath = candidate;
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                normalizedRelativePath = string.Empty;
                errorMessage = $"Portal 相对路径无效：{exception.Message}";
                return false;
            }
        }

        internal static bool IsPathWithinRoot(string rootPath, string candidatePath)
        {
            try
            {
                string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
                string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
                if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string rootWithSeparator = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
                    ? normalizedRoot
                    : normalizedRoot + Path.DirectorySeparatorChar;
                return normalizedCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }

        private bool TryValidateNavigablePath(
            string rootPath,
            string normalizedRelativePath,
            out string errorMessage)
        {
            errorMessage = string.Empty;
            if (normalizedRelativePath.Length == 0)
            {
                return true;
            }

            string current = rootPath;
            foreach (string segment in normalizedRelativePath.Split(Path.DirectorySeparatorChar))
            {
                current = Path.Combine(current, segment);
                if (!_directoryExists(current))
                {
                    errorMessage = "Portal 当前子目录已不存在。";
                    return false;
                }

                FileAttributes attributes;
                try
                {
                    attributes = _getAttributes(current);
                }
                catch (Exception exception)
                {
                    errorMessage = exception.Message;
                    return false;
                }

                if ((attributes & FileAttributes.Directory) == 0)
                {
                    errorMessage = "Portal 当前相对路径不是文件夹。";
                    return false;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    errorMessage = "Portal 不会进入可能指向根目录外部的链接或重解析点。";
                    return false;
                }
            }

            return true;
        }

        private static bool TryNormalizeRootPath(
            string? rootPath,
            out string normalizedRootPath,
            out string errorMessage)
        {
            normalizedRootPath = string.Empty;
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(rootPath) || !Path.IsPathFullyQualified(rootPath))
            {
                errorMessage = "Portal 根目录必须是完整的绝对路径。";
                return false;
            }

            try
            {
                normalizedRootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errorMessage = $"Portal 根目录路径无效：{exception.Message}";
                return false;
            }
        }

        private static bool IsDirectChild(string parentPath, string candidatePath)
        {
            string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
            string? candidateParent = Path.GetDirectoryName(normalizedCandidate);
            return candidateParent != null &&
                   string.Equals(
                       Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentPath)),
                       Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidateParent)),
                       StringComparison.OrdinalIgnoreCase);
        }

        private static PortalReadResult Failure(
            FolderPortalInfo portal,
            string rootPath,
            string currentPath,
            string errorMessage) => new(
                Success: false,
                RootPath: rootPath,
                CurrentPath: currentPath,
                CurrentRelativePath: portal.CurrentRelativePath ?? string.Empty,
                Entries: Array.Empty<PortalDirectoryEntry>(),
                IsTruncated: false,
                ErrorMessage: errorMessage);
    }
}
