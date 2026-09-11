namespace DesktopOrganizer;

public partial class MainWindow
{
    private readonly IDesktopFileClipboard _desktopFileClipboard;

    private bool CanRouteFileClipboardShortcut() => !_isClosing && !_organizerPaused && !_isSafeModeActive &&
        !HasPendingFileOperations &&
        !_folderPortalRuntimeStates.Values.Any(state => state.CurrentList?.IsKeyboardFocusWithin == true) &&
        !IsTextClipboardTarget(Keyboard.FocusedElement as DependencyObject);

    internal static bool IsTextClipboardTarget(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is TextBoxBase or PasswordBox or ComboBox) return true;
            element = element is Visual || element is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    private bool CanCopySelectedFiles() => _selectedItemNames.Count > 0 &&
        _selectedItemNames.All(name => _desktopItems.TryGetValue(name, out string? path) &&
            !ShellItemLocation.TryDecode(path, out _, out _) && !IsFileOperationPending(path));

    internal bool CopySelectedDesktopFiles()
    {
        if (_isClosing || !CanCopySelectedFiles()) return false;
        try
        {
            string[] paths = _selectedItemNames.Select(name => _desktopItems[name])
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (paths.Any(path => !File.Exists(path) && !Directory.Exists(path)))
                throw new IOException("所选文件已移动或删除，请刷新后重试。");
            _desktopFileClipboard.CopyFiles(paths);
            StatusText.Text = $"已复制 {paths.Length} 个项目，可粘贴到桌面或资源管理器";
        }
        catch (Exception exception)
        {
            StatusText.Text = "复制失败：" + exception.Message;
        }
        return true;
    }

    internal static string? GetClipboardDesktopTarget(DesktopPathSnapshot? paths) =>
        paths?.UserDesktop is { IsAvailable: true, Path: { Length: > 0 } path } ? path : null;

    internal async Task PasteDesktopFilesAsync(string? targetFolder = null)
    {
        if (_isClosing || HasPendingFileOperations) return;
        List<string>? reservation = null;
        try
        {
            if (!_desktopFileClipboard.ContainsFiles)
            {
                StatusText.Text = "剪贴板中没有可粘贴的文件";
                return;
            }
            string? target = targetFolder ?? GetClipboardDesktopTarget(_desktopPaths);
            if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
                throw new IOException("当前桌面或目标文件夹不可用，请刷新后重试。");
            uint sequence = _desktopFileClipboard.Sequence;
            string[] sources = _desktopFileClipboard.ReadFiles();
            if (sources.Length == 0 || sources.Any(path => !File.Exists(path) && !Directory.Exists(path)))
                throw new IOException("剪贴板中的文件已不可用，请重新复制。");
            if (!TryReserveFileOperation(sources.Append(target), "相关文件正在操作中，请稍后重试。", out var keys)) return;
            reservation = keys;
            IntPtr owner = new WindowInteropHelper(this).Handle;
            StatusText.Text = "正在调用 Windows 粘贴文件…";
            await _fileOperationService.Enqueue(token =>
            {
                token.ThrowIfCancellationRequested();
                _desktopFileClipboard.Paste(target, owner, sequence);
                return true;
            }, _lifetimeCts.Token);
            if (!_isClosing)
            {
                StatusText.Text = "已交由 Windows 处理粘贴；重名或权限问题请按系统提示选择";
                RequestDesktopRefresh(clearIconCache: false, statusMessage: null);
            }
        }
        catch (OperationCanceledException) when (_isClosing) { }
        catch (Exception exception)
        {
            if (!_isClosing) StatusText.Text = "粘贴未完成：" + exception.Message;
        }
        finally
        {
            if (reservation is { Count: > 0 }) ReleaseFileOperation(reservation);
        }
    }

    private bool PasteFilesFromKeyboard()
    {
        _ = PasteDesktopFilesAsync();
        return true;
    }

    private MenuItem CreatePasteFilesMenuItem(string? targetFolder = null)
    {
        var paste = new MenuItem
        {
            Header = targetFolder is null ? "粘贴到桌面" : "粘贴到此文件夹",
            InputGestureText = targetFolder is null ? "Ctrl+V" : "",
            Tag = "paste-files"
        };
        paste.Click += async (_, _) => await PasteDesktopFilesAsync(targetFolder);
        return paste;
    }
}
