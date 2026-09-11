namespace DesktopOrganizer
{
    internal static class LightDesktopLayoutPolicy
    {
        public const double ColumnWidth = 352;
        public const double Gap = 12;
        public const double EntryGap = 4;
        public const double EntryWidth = (ColumnWidth - EntryGap) / 2;

        public static DesktopZoneRole ResolveRole(GroupInfo group)
        {
            if (group.IsSizeLocked) return DesktopZoneRole.None;
            if (group.DesktopRole != DesktopZoneRole.None) return group.DesktopRole;
            return (group.AutoCategoryKey ?? group.Name) switch
            {
                "shortcuts" or "快捷方式" => DesktopZoneRole.Shortcuts,
                "development-projects" or "开发项目" => DesktopZoneRole.Projects,
                "科研软件" => DesktopZoneRole.Research,
                "documents" or "文档" or "folders" or "文件夹" or
                "images" or "图片" or "archives" or "压缩包" or "科学上网" => DesktopZoneRole.Other,
                _ => DesktopZoneRole.None
            };
        }

        public static int Columns(DesktopZoneRole role) => role == DesktopZoneRole.Shortcuts ? 4 : 3;
        public static int Rows(DesktopZoneRole role) => role switch
        {
            DesktopZoneRole.Research => 2,
            DesktopZoneRole.Projects => 4,
            _ => 3
        };

        public static int EntryOrder(GroupInfo group) => (group.AutoCategoryKey ?? group.Name) switch
        {
            "documents" or "文档" => 0,
            "folders" or "文件夹" => 1,
            "科学上网" => 2,
            "images" or "图片" => 3,
            "archives" or "压缩包" => 4,
            _ => 5
        };
    }
}
