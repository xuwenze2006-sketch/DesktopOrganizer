namespace DesktopOrganizer
{
    /// <summary>角色配套静态图层；冻结位图和命中缓存跨控件复用。</summary>
    internal static class DesktopPetScene
    {
        internal const double Size = 360;
        internal sealed record Layer(BitmapSource Image, byte[] Alpha);
        private static readonly Lazy<Layer> Back = new(() => Load("background.png"));
        private static readonly Lazy<Layer> Front = new(() => Load("foreground.png"));
        private static readonly Lazy<Layer> Sunset = new(() => Load("Assets/Scenes/SunsetDuo/background.png", fullPath: true));
        private static readonly Lazy<Layer> SunsetCard = new(() => Load("Assets/Pets/SunsetDuo/preview.png", fullPath: true));
        private static readonly Lazy<Layer> Tiananmen = new(() => Load("Assets/Scenes/Tiananmen/background.png", fullPath: true));
        private static readonly Lazy<Layer> PinkCard = new(() => Load("Assets/Pets/PinkHaired/preview.png", fullPath: true));
        internal static Layer Background => Back.Value;
        internal static Layer Foreground => Front.Value;
        internal static Layer SunsetPreview => SunsetCard.Value;
        internal static Layer PinkPreview => PinkCard.Value;
        internal static bool HasForeground(string characterId) => characterId is not ("pink-haired" or DesktopPetCatalog.SunsetDuoId);
        internal static Layer BackgroundFor(string characterId) => characterId switch
        {
            "pink-haired" => Tiananmen.Value,
            DesktopPetCatalog.SunsetDuoId => Sunset.Value,
            _ => Background
        };
        internal static string NameFor(string characterId) => characterId switch
        {
            "pink-haired" => "透明夕阳城楼",
            DesktopPetCatalog.SunsetDuoId => "夕阳城楼",
            _ => "树与草地"
        };

        internal static bool ContainsOpaquePoint(Point point, Size size, string characterId = DesktopPetWidget.CatCharacterId)
        {
            if (size.Width <= 0 || size.Height <= 0 || point.X < 0 || point.Y < 0 ||
                point.X >= size.Width || point.Y >= size.Height) return false;
            Layer background = BackgroundFor(characterId);
            int x = (int)(point.X * background.Image.PixelWidth / size.Width);
            int y = (int)(point.Y * background.Image.PixelHeight / size.Height);
            int index = y * background.Image.PixelWidth + x;
            int back = background.Alpha[index];
            if (!HasForeground(characterId)) return back > 32;
            return back + Foreground.Alpha[index] * (255 - back) / 255 > 32;
        }

        private static Layer Load(string name, bool fullPath = false)
        {
            using Stream stream = VPetAnimation.OpenResource(fullPath ? name : "Assets/Scenes/TreeGrass/" + name);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            converted.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var alpha = new byte[bitmap.PixelWidth * bitmap.PixelHeight];
            for (int i = 0; i < alpha.Length; i++) alpha[i] = pixels[i * 4 + 3];
            return new Layer(bitmap, alpha);
        }
    }
}
