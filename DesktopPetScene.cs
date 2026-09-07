namespace DesktopOrganizer
{
    /// <summary>树草静态图层；只加载打包素材，冻结位图和命中缓存跨控件复用。</summary>
    internal static class DesktopPetScene
    {
        internal const double Size = 360;
        internal sealed record Layer(BitmapSource Image, byte[] Alpha);
        private static readonly Lazy<Layer> Back = new(() => Load("background.png"));
        private static readonly Lazy<Layer> Front = new(() => Load("foreground.png"));
        internal static Layer Background => Back.Value;
        internal static Layer Foreground => Front.Value;

        internal static bool ContainsOpaquePoint(Point point, Size size)
        {
            if (size.Width <= 0 || size.Height <= 0 || point.X < 0 || point.Y < 0 ||
                point.X >= size.Width || point.Y >= size.Height) return false;
            int x = (int)(point.X * Background.Image.PixelWidth / size.Width);
            int y = (int)(point.Y * Background.Image.PixelHeight / size.Height);
            int index = y * Background.Image.PixelWidth + x;
            int back = Background.Alpha[index];
            return back + Foreground.Alpha[index] * (255 - back) / 255 > 32;
        }

        private static Layer Load(string name)
        {
            using Stream stream = VPetAnimation.OpenResource("Assets/Scenes/TreeGrass/" + name);
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
