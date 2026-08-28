namespace DesktopOrganizer
{
    /// <summary>
    /// 主桌面层使用的暖白纸面调色板。颜色保持集中定义，动态创建的分类框、
    /// 文件夹入口和分组内图标可以与 XAML 控制面板使用同一套视觉语义。
    /// </summary>
    internal static class WarmPaperTheme
    {
        public static Color PanelSurfaceColor => Color.FromArgb(250, 250, 247, 240);
        public static Color SoftSurfaceColor => Color.FromArgb(246, 250, 248, 243);
        public static Color HeaderSurfaceColor => Color.FromArgb(248, 244, 239, 231);
        public static Color BorderColor => Color.FromRgb(216, 207, 194);
        public static Color PrimaryTextColor => Color.FromRgb(47, 43, 38);
        public static Color SecondaryTextColor => Color.FromRgb(98, 91, 82);
        public static Color MutedTextColor => Color.FromRgb(117, 109, 100);
        public static Color SageAccentColor => Color.FromRgb(111, 150, 116);
        public static Color SageSoftColor => Color.FromRgb(229, 237, 228);
        public static Color WarmHoverColor => Color.FromRgb(243, 238, 230);
        public static Color WarmPressedColor => Color.FromRgb(232, 222, 209);
        public static Color GroupedLabelSurfaceColor => Color.FromArgb(210, 250, 248, 242);

        public static Brush PanelSurfaceBrush { get; } = CreateFrozenBrush(PanelSurfaceColor);
        public static Brush SoftSurfaceBrush { get; } = CreateFrozenBrush(SoftSurfaceColor);
        public static Brush HeaderSurfaceBrush { get; } = CreateFrozenBrush(HeaderSurfaceColor);
        public static Brush BorderBrush { get; } = CreateFrozenBrush(BorderColor);
        public static Brush PrimaryTextBrush { get; } = CreateFrozenBrush(PrimaryTextColor);
        public static Brush SecondaryTextBrush { get; } = CreateFrozenBrush(SecondaryTextColor);
        public static Brush MutedTextBrush { get; } = CreateFrozenBrush(MutedTextColor);
        public static Brush SageAccentBrush { get; } = CreateFrozenBrush(SageAccentColor);
        public static Brush SageSoftBrush { get; } = CreateFrozenBrush(SageSoftColor);
        public static Brush WarmHoverBrush { get; } = CreateFrozenBrush(WarmHoverColor);
        public static Brush WarmPressedBrush { get; } = CreateFrozenBrush(WarmPressedColor);
        public static Brush GroupedLabelSurfaceBrush { get; } = CreateFrozenBrush(GroupedLabelSurfaceColor);

        private static Brush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
