using Microsoft.Win32;

namespace DesktopOrganizer
{
    /// <summary>
    /// 使用当前用户的 Windows Run 注册表项管理开机启动。
    /// 注册命令带 --startup，开机登录时只启动桌面整理层，不主动显示控制栏。
    /// </summary>
    internal static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DesktopOrganizer";

        public static bool IsEnabledForCurrentExecutable()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                string? registeredCommand = key?.GetValue(ValueName) as string;
                return !string.IsNullOrWhiteSpace(registeredCommand) &&
                       string.Equals(
                           NormalizeCommand(registeredCommand),
                           NormalizeCommand(BuildStartupCommand()),
                           StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool TrySetEnabled(bool enabled, out string? errorMessage)
        {
            errorMessage = null;

            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                if (enabled)
                {
                    key.SetValue(ValueName, BuildStartupCommand(), RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static string BuildStartupCommand()
        {
            string processPath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("无法确定当前程序路径。");

            bool hostedByDotNet = Path.GetFileNameWithoutExtension(processPath)
                .Equals("dotnet", StringComparison.OrdinalIgnoreCase);

            if (hostedByDotNet)
            {
                string entryAssemblyPath = Path.Combine(AppContext.BaseDirectory, "DesktopOrganizer.dll");
                if (File.Exists(entryAssemblyPath))
                {
                    return $"\"{processPath}\" \"{entryAssemblyPath}\" --startup";
                }
            }

            return $"\"{processPath}\" --startup";
        }

        private static string NormalizeCommand(string command)
        {
            return command.Trim().Replace('/', '\\');
        }
    }
}
