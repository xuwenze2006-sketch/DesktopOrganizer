namespace DesktopOrganizer
{
    /// <summary>桌面项目来源。文件系统项目支持真实文件操作；ShellNamespace 只参与虚拟桌面呈现。</summary>
    internal enum DesktopItemKind
    {
        FileSystem,
        ShellNamespace
    }

    /// <summary>从 Windows Shell 桌面根命名空间枚举得到的可见项目。</summary>
    internal sealed record ShellNamespaceItem(
        string DisplayName,
        string ParsingName,
        string? FileSystemPath,
        bool IsFolder);

    /// <summary>
    /// 在现有 name -> location 字典中保存 Shell parsing name 的无歧义封装。
    /// 使用 Base64 避免把 ::{CLSID} 一类 parsing name 误交给 Path/File API。
    /// </summary>
    internal static class ShellItemLocation
    {
        private const string Prefix = "shell-namespace:";

        public static string Encode(string parsingName, bool isFolder)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);
            string kind = isFolder ? "folder:" : "item:";
            return Prefix + kind + Convert.ToBase64String(Encoding.UTF8.GetBytes(parsingName));
        }

        public static bool TryDecode(string? location, out string parsingName, out bool isFolder)
        {
            parsingName = string.Empty;
            isFolder = false;
            if (string.IsNullOrWhiteSpace(location) ||
                !location.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            string payload = location[Prefix.Length..];
            bool decodedIsFolder;
            if (payload.StartsWith("folder:", StringComparison.Ordinal))
            {
                decodedIsFolder = true;
                payload = payload["folder:".Length..];
            }
            else if (payload.StartsWith("item:", StringComparison.Ordinal))
            {
                decodedIsFolder = false;
                payload = payload["item:".Length..];
            }
            else
            {
                return false;
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(payload);
                string decodedParsingName = Encoding.UTF8.GetString(bytes);
                if (string.IsNullOrWhiteSpace(decodedParsingName))
                {
                    return false;
                }

                parsingName = decodedParsingName;
                isFolder = decodedIsFolder;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static bool AreEquivalent(string? first, string? second)
        {
            bool firstIsShell = TryDecode(first, out string firstParsingName, out bool firstIsFolder);
            bool secondIsShell = TryDecode(second, out string secondParsingName, out bool secondIsFolder);
            if (firstIsShell || secondIsShell)
            {
                return firstIsShell &&
                       secondIsShell &&
                       firstIsFolder == secondIsFolder &&
                       string.Equals(
                           firstParsingName,
                           secondParsingName,
                           StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }
}
