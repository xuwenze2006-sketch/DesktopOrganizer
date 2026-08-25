namespace DesktopOrganizer
{
    /// <summary>
    /// 控制哪些 Windows Shell 桌面命名空间项目由整理层呈现。
    /// 使用稳定 CLSID，而不是本地化显示名称，保证不同系统语言下行为一致。
    /// </summary>
    internal static class ShellDesktopItemPolicy
    {
        internal const string RecycleBinParsingName =
            "::{645FF040-5081-101B-9F08-00AA002F954E}";

        private static readonly Guid RecycleBinClassId =
            new("645FF040-5081-101B-9F08-00AA002F954E");

        public static bool ShouldShowOnDesktop(ShellNamespaceItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            return IsRecycleBin(item.ParsingName);
        }

        public static bool IsRecycleBin(string? parsingName)
        {
            if (string.IsNullOrWhiteSpace(parsingName))
            {
                return false;
            }

            // Desktop-absolute parsing name 可能带 shell::: 前缀，或包含多级 CLSID。
            // 逐个解析花括号中的 GUID，避免依赖显示名称或固定字符串前缀。
            int searchIndex = 0;
            while (searchIndex < parsingName.Length)
            {
                int openBrace = parsingName.IndexOf('{', searchIndex);
                if (openBrace < 0)
                {
                    break;
                }

                int closeBrace = parsingName.IndexOf('}', openBrace + 1);
                if (closeBrace < 0)
                {
                    break;
                }

                ReadOnlySpan<char> candidate = parsingName.AsSpan(
                    openBrace,
                    closeBrace - openBrace + 1);
                if (Guid.TryParse(candidate, out Guid classId) &&
                    classId == RecycleBinClassId)
                {
                    return true;
                }

                searchIndex = closeBrace + 1;
            }

            return false;
        }
    }
}
