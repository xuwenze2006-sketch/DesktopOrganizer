namespace DesktopOrganizer
{
    /// <summary>桌面项目识别结果。Key 用于稳定匹配自动分组，DisplayName 用于界面显示。</summary>
    internal sealed record DesktopCategoryDefinition(string Key, string DisplayName, int Order);

    /// <summary>
    /// 单次分类结果。目录标记读取失败时仍提供保守分类，但 IsReliable 为 false，
    /// 调用方不得据此把已经归类的项目迁往其他自动分类框。
    /// </summary>
    internal sealed record DesktopCategoryClassification(
        DesktopCategoryDefinition Category,
        bool IsReliable);

    /// <summary>
    /// 纯本地、无网络的桌面项目分类器。
    /// 文件按扩展名分类；文件夹只检查顶层项目标记，不递归扫描内容。
    /// </summary>
    internal static class DesktopCategoryClassifier
    {
        private static readonly DesktopCategoryDefinition SystemItems = new("system-items", "系统项目", 5);
        private static readonly DesktopCategoryDefinition Shortcuts = new("shortcuts", "快捷方式", 10);
        private static readonly DesktopCategoryDefinition DevelopmentProjects = new("development-projects", "开发项目", 20);
        private static readonly DesktopCategoryDefinition Folders = new("folders", "文件夹", 30);
        private static readonly DesktopCategoryDefinition Documents = new("documents", "文档", 40);
        private static readonly DesktopCategoryDefinition Spreadsheets = new("spreadsheets", "表格", 50);
        private static readonly DesktopCategoryDefinition Presentations = new("presentations", "演示文稿", 60);
        private static readonly DesktopCategoryDefinition Images = new("images", "图片", 70);
        private static readonly DesktopCategoryDefinition Videos = new("videos", "视频", 80);
        private static readonly DesktopCategoryDefinition Audio = new("audio", "音频", 90);
        private static readonly DesktopCategoryDefinition Archives = new("archives", "压缩包", 100);
        private static readonly DesktopCategoryDefinition Applications = new("applications", "程序与安装包", 110);
        private static readonly DesktopCategoryDefinition Code = new("code", "代码与脚本", 120);
        private static readonly DesktopCategoryDefinition Data = new("data", "数据文件", 130);
        private static readonly DesktopCategoryDefinition Design = new("design", "设计与模型", 140);
        private static readonly DesktopCategoryDefinition Ebooks = new("ebooks", "电子书", 150);
        private static readonly DesktopCategoryDefinition Fonts = new("fonts", "字体", 160);
        private static readonly DesktopCategoryDefinition DiskImages = new("disk-images", "磁盘镜像", 170);
        private static readonly DesktopCategoryDefinition Other = new("other", "其他", 999);

        private static readonly Dictionary<string, DesktopCategoryDefinition> ExtensionMap = BuildExtensionMap();

        private static readonly string[] ProjectMarkerFiles =
        {
            "package.json", "pnpm-workspace.yaml", "yarn.lock", "package-lock.json",
            "pyproject.toml", "Pipfile", "poetry.lock", "requirements.txt",
            "Cargo.toml", "go.mod", "CMakeLists.txt", "meson.build",
            "pom.xml", "build.gradle", "build.gradle.kts", "settings.gradle",
            "composer.json", "Gemfile", "pubspec.yaml", "Dockerfile"
        };

        private static readonly string[] ProjectMarkerPatterns =
        {
            "*.sln", "*.slnx", "*.csproj", "*.fsproj", "*.vbproj",
            "*.vcxproj", "*.xcodeproj", "*.code-workspace"
        };

        public static DesktopCategoryDefinition ClassifyShellNamespace() => SystemItems;

        public static DesktopCategoryDefinition Classify(string fullPath) =>
            ClassifyWithReliability(fullPath).Category;

        public static DesktopCategoryClassification ClassifyWithReliability(string fullPath)
        {
            if (Directory.Exists(fullPath))
            {
                return ClassifyDirectory(
                    fullPath,
                    pattern => Directory.EnumerateFileSystemEntries(
                        fullPath,
                        pattern,
                        SearchOption.TopDirectoryOnly));
            }

            string extension = Path.GetExtension(fullPath).ToLowerInvariant();
            DesktopCategoryDefinition category =
                ExtensionMap.TryGetValue(extension, out DesktopCategoryDefinition? mappedCategory)
                    ? mappedCategory
                    : Other;
            // 扫描与分类之间项目可能被删除，Directory.Exists 也会在无权读取时返回 false。
            // 此时仍返回保守的扩展名分类供显示，但禁止据此迁移已有自动分类成员。
            return new DesktopCategoryClassification(category, IsReliable: File.Exists(fullPath));
        }

        internal static DesktopCategoryClassification ClassifyDirectory(
            string path,
            Func<string, IEnumerable<string>> enumerateEntries)
        {
            try
            {
                if (Directory.Exists(Path.Combine(path, ".git")) ||
                    Directory.Exists(Path.Combine(path, ".svn")))
                {
                    return new DesktopCategoryClassification(DevelopmentProjects, IsReliable: true);
                }

                if (ProjectMarkerFiles.Any(marker => File.Exists(Path.Combine(path, marker))))
                {
                    return new DesktopCategoryClassification(DevelopmentProjects, IsReliable: true);
                }

                foreach (string pattern in ProjectMarkerPatterns)
                {
                    if (enumerateEntries(pattern).Any())
                    {
                        return new DesktopCategoryClassification(DevelopmentProjects, IsReliable: true);
                    }
                }
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or
                IOException or
                NotSupportedException or
                System.Security.SecurityException)
            {
                // 保守显示为普通文件夹，但不允许本轮结果触发自动分类迁移。
                return new DesktopCategoryClassification(Folders, IsReliable: false);
            }

            return new DesktopCategoryClassification(Folders, IsReliable: true);
        }

        private static Dictionary<string, DesktopCategoryDefinition> BuildExtensionMap()
        {
            var map = new Dictionary<string, DesktopCategoryDefinition>(StringComparer.OrdinalIgnoreCase);

            Add(map, Shortcuts, ".lnk", ".url", ".appref-ms");
            Add(map, Documents, ".txt", ".rtf", ".doc", ".docx", ".odt", ".pdf", ".xps", ".md", ".tex", ".pages");
            Add(map, Spreadsheets, ".xls", ".xlsx", ".xlsm", ".ods", ".csv", ".tsv", ".numbers");
            Add(map, Presentations, ".ppt", ".pptx", ".pptm", ".odp", ".key");
            Add(map, Images, ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico", ".heic", ".heif", ".tif", ".tiff", ".raw", ".avif");
            Add(map, Videos, ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".3gp", ".mpeg", ".mpg");
            Add(map, Audio, ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma", ".mid", ".midi", ".opus", ".ape");
            Add(map, Archives, ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".tgz", ".cab", ".zst");
            Add(map, Applications, ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".com");
            Add(map, Code,
                ".cs", ".csx", ".xaml", ".fs", ".vb", ".c", ".cc", ".cpp", ".h", ".hpp",
                ".java", ".kt", ".kts", ".go", ".rs", ".swift", ".m", ".mm",
                ".js", ".jsx", ".ts", ".tsx", ".vue", ".svelte", ".html", ".htm", ".css", ".scss", ".sass", ".less",
                ".py", ".pyw", ".rb", ".php", ".lua", ".r", ".dart", ".sql",
                ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".config",
                ".ps1", ".psm1", ".bat", ".cmd", ".sh", ".zsh", ".fish");
            Add(map, Data, ".db", ".sqlite", ".sqlite3", ".mdb", ".accdb", ".parquet", ".feather", ".pkl", ".sav", ".dta");
            Add(map, Design, ".psd", ".ai", ".xd", ".fig", ".sketch", ".dwg", ".dxf", ".blend", ".fbx", ".obj", ".stl", ".3ds", ".step", ".stp");
            Add(map, Ebooks, ".epub", ".mobi", ".azw", ".azw3", ".djvu", ".fb2");
            Add(map, Fonts, ".ttf", ".otf", ".woff", ".woff2", ".eot");
            Add(map, DiskImages, ".iso", ".img", ".vhd", ".vhdx", ".vmdk", ".dmg");

            return map;
        }

        private static void Add(
            Dictionary<string, DesktopCategoryDefinition> map,
            DesktopCategoryDefinition category,
            params string[] extensions)
        {
            foreach (string extension in extensions)
            {
                map[extension] = category;
            }
        }
    }
}
