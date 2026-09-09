using System.IO;

namespace DesktopOrganizer.Tests;

internal static class TestProjectFiles
{
    internal static string Root { get; } = FindRoot();

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
