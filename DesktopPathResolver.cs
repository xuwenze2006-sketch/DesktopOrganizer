namespace DesktopOrganizer;

/// <summary>一次系统桌面路径探测；旧的有效路径仅供说明，不代表当前可安全读取。</summary>
internal sealed record DesktopPathState(
    string? Path,
    string? LastKnownGoodPath,
    bool IsAvailable,
    string? ErrorMessage);

internal sealed record DesktopPathSnapshot(
    DesktopPathState UserDesktop,
    DesktopPathState CommonDesktop,
    long Revision)
{
    public bool IsAvailable => UserDesktop.IsAvailable && CommonDesktop.IsAvailable;
}

/// <summary>
/// 每次探测都重新询问 Windows，不推测盘符或 OneDrive 目录，也不创建目录。
/// Refresh 应在后台调用；Current 始终返回已完成的快照，不等待正在进行的路径查询。
/// 目录存在不等于枚举成功，调用方仍须检查实际扫描是否完整。
/// </summary>
internal sealed class DesktopPathResolver
{
    private readonly object _gate = new();
    private readonly Func<Environment.SpecialFolder, string> _getFolderPath;
    private readonly Func<string, bool> _directoryExists;
    private DesktopPathSnapshot _current = new(
        new DesktopPathState(null, null, false, "尚未检测用户桌面路径。"),
        new DesktopPathState(null, null, false, "尚未检测公共桌面路径。"),
        0);

    public DesktopPathResolver(
        Func<Environment.SpecialFolder, string>? getFolderPath = null,
        Func<string, bool>? directoryExists = null)
    {
        _getFolderPath = getFolderPath ?? (folder => Environment.GetFolderPath(
            folder,
            Environment.SpecialFolderOption.DoNotVerify));
        _directoryExists = directoryExists ?? Directory.Exists;
    }

    public DesktopPathSnapshot Current => Volatile.Read(ref _current);

    public DesktopPathSnapshot Refresh()
    {
        lock (_gate)
        {
            DesktopPathSnapshot previous = _current;
            DesktopPathState user = Resolve(
                Environment.SpecialFolder.DesktopDirectory,
                previous.UserDesktop);
            DesktopPathState common = Resolve(
                Environment.SpecialFolder.CommonDesktopDirectory,
                previous.CommonDesktop);
            bool changed = previous.Revision == 0 ||
                HasChanged(previous.UserDesktop, user) ||
                HasChanged(previous.CommonDesktop, common);
            var current = new DesktopPathSnapshot(
                user,
                common,
                changed ? previous.Revision + 1 : previous.Revision);
            Volatile.Write(ref _current, current);
            return current;
        }
    }

    private DesktopPathState Resolve(
        Environment.SpecialFolder folder,
        DesktopPathState previous)
    {
        string? path = null;
        try
        {
            string reportedPath = _getFolderPath(folder);
            if (string.IsNullOrWhiteSpace(reportedPath))
            {
                return Unavailable(null, previous, "Windows 未返回桌面路径。");
            }
            if (!System.IO.Path.IsPathFullyQualified(reportedPath))
            {
                return Unavailable(null, previous, "Windows 返回的桌面路径不是完整绝对路径。");
            }

            path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(reportedPath));
            if (!_directoryExists(path))
            {
                return Unavailable(path, previous, "桌面目录不存在或暂时无法访问。");
            }

            return new DesktopPathState(path, path, true, null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return Unavailable(path, previous, $"无法读取桌面路径：{exception.Message}");
        }
    }

    private static DesktopPathState Unavailable(
        string? path,
        DesktopPathState previous,
        string errorMessage) => new(path, previous.LastKnownGoodPath, false, errorMessage);

    private static bool HasChanged(DesktopPathState previous, DesktopPathState current) =>
        previous.IsAvailable != current.IsAvailable ||
        !string.Equals(previous.Path, current.Path, StringComparison.OrdinalIgnoreCase);
}
