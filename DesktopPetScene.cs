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
        internal sealed record Scene(string Id, string Name);
        private static readonly Scene[] BuiltInScenes = [new("tree-grass", "树与草地"), new("tiananmen", "透明夕阳城楼"), new("sunset", "夕阳城楼")];
        private sealed record SceneSnapshot(IReadOnlyList<DesktopPetLibrary.Asset> Source, IReadOnlyList<Scene> Scenes);
        private static SceneSnapshot? _sceneSnapshot;
        internal static IReadOnlyList<Scene> Scenes
        {
            get
            {
                var source = DesktopPetLibrary.Shared.Assets;
                var cached = Volatile.Read(ref _sceneSnapshot);
                if (cached is not null && ReferenceEquals(cached.Source, source)) return cached.Scenes;
                var next = new SceneSnapshot(source, Array.AsReadOnly(BuiltInScenes.Concat(
                    source.Where(a => a.Kind == "scene").Select(a => new Scene(a.Id, a.Name))).ToArray()));
                Volatile.Write(ref _sceneSnapshot, next);
                return next.Scenes;
            }
        }
        internal static string DefaultId(string characterId) => characterId switch
        { "pink-haired" => "tiananmen", DesktopPetCatalog.SunsetDuoId => "sunset", _ => "tree-grass" };
        internal static string ResolveId(string? sceneId, string characterId) => sceneId is "tree-grass" or "tiananmen" or "sunset"
            ? sceneId : sceneId is not null && DesktopPetLibrary.Shared.FindAsset(sceneId)?.Kind == "scene"
                ? sceneId : DefaultId(characterId);
        internal static Layer GetBackground(string sceneId) => sceneId switch
        { "tree-grass" => Background, "tiananmen" => Tiananmen.Value, "sunset" => Sunset.Value,
            _ => DesktopPetLibrary.Shared.GetImage(sceneId) };
        internal static bool ContainsScenePoint(Point point, Size size, string sceneId)
        {
            if (size.Width <= 0 || size.Height <= 0 || point.X < 0 || point.Y < 0 || point.X >= size.Width || point.Y >= size.Height) return false;
            Layer back = GetBackground(sceneId);
            int x = (int)(point.X * back.Image.PixelWidth / size.Width), y = (int)(point.Y * back.Image.PixelHeight / size.Height);
            int alpha = back.Alpha[y * back.Image.PixelWidth + x];
            if (sceneId != "tree-grass") return alpha > 32;
            int fx = (int)(point.X * Foreground.Image.PixelWidth / size.Width), fy = (int)(point.Y * Foreground.Image.PixelHeight / size.Height);
            return alpha + Foreground.Alpha[fy * Foreground.Image.PixelWidth + fx] * (255 - alpha) / 255 > 32;
        }
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
