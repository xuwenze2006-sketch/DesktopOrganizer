using System.IO;

namespace DesktopOrganizer.Tests;

internal static class TestProjectFiles
{
    internal static string Root { get; } = FindRoot();

    /// <summary>
    /// 契约测试读取的源码文件所在目录。源码按目录整理后不再平铺在仓库根目录，
    /// 这里集中登记位置，避免各测试重复拼接路径。未登记的文件按仓库根目录处理。
    /// </summary>
    private static readonly Dictionary<string, string> SourceDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["MainWindow.xaml"] = "src/App",
            ["MainWindow.DesktopHost.cs"] = "src/App",
            ["InboxWindow.xaml"] = "src/Inbox",
            ["OperationCenterWindow.xaml"] = "src/App",
            ["RuleManagerWindow.xaml"] = "src/App",
            ["RulePreviewWindow.xaml"] = "src/App",
            ["SimpleInputDialog.xaml"] = "src/App",
            ["WorkspaceManagerWindow.xaml"] = "src/App",
            ["DesktopSearchWindow.xaml"] = "src/Desktop",
            ["NativeMethods.cs"] = "src/ShellInterop",
            ["NativeMethods.DesktopLayer.cs"] = "src/ShellInterop",
            ["NativeMethods.WindowEvents.cs"] = "src/ShellInterop",
        };

    /// <summary>返回源码文件路径，供契约测试读取。</summary>
    internal static string SourcePath(string fileName)
    {
        string directory = SourceDirectories.TryGetValue(fileName, out string? known)
            ? Path.Combine(Root, known.Replace('/', Path.DirectorySeparatorChar))
            : Root;
        return Path.Combine(directory, fileName);
    }

    /// <summary>定位表中登记的全部源码文件名。</summary>
    internal static IReadOnlyCollection<string> RegisteredFiles => SourceDirectories.Keys;

    internal static string ReadSource(string fileName) => File.ReadAllText(SourcePath(fileName));

    private static string FindRoot()
    {
        // CI 的确定性构建会把 CallerFilePath 映射到 /_/；从实际测试输出位置找源码。
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DesktopOrganizer.slnx")) &&
                File.Exists(Path.Combine(directory.FullName, "DesktopOrganizer.csproj")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Run source contract tests from a DesktopOrganizer checkout.");
    }
}
