namespace DesktopOrganizer
{
    /// <summary>仅加载随程序打包的选定动作；冻结图像和透明度缓存跨控件复用。</summary>
    internal static class VPetAnimation
    {
        internal sealed record Frame(BitmapSource Image, byte[] Alpha);
        internal sealed record Clip(Frame[] Frames, int[] Milliseconds);

        private sealed class Manifest
        {
            public int CellSize { get; set; }
            public int Columns { get; set; }
            public Dictionary<string, ClipInfo> Clips { get; set; } = new();
        }

        private sealed class ClipInfo
        {
            public string Sheet { get; set; } = "";
            public List<FrameInfo> Frames { get; set; } = new();
        }

        private sealed class FrameInfo
        {
            public int Index { get; set; }
            public int Milliseconds { get; set; }
        }

        private static readonly Lazy<Dictionary<string, Lazy<Clip>>> Clips = new(() => LoadManifest("VPet"));
        private static readonly Dictionary<string, Lazy<Dictionary<string, Lazy<Clip>>>> ImportedClips =
            DesktopPetCatalog.BuiltInCharacters.Where(character => DesktopPetCatalog.IsImported(character.Id))
                .ToDictionary(character => character.Id,
                    character => new Lazy<Dictionary<string, Lazy<Clip>>>(() => LoadManifest(character.AssetFolder!)));

        internal static IEnumerable<string> ClipNames => Clips.Value.Keys;
        internal static Clip GetClip(string name) => Clips.Value[name].Value;
        internal static Clip GetClip(string characterId, string name) => characterId.StartsWith("user-", StringComparison.Ordinal)
            ? DesktopPetLibrary.Shared.GetClip(characterId, name) : characterId == "vpet"
            ? GetClip(name) : ImportedClips[characterId].Value[name].Value;

        internal static Stream OpenResource(string path) => Application.GetResourceStream(new Uri(
            "pack://application:,,,/DesktopOrganizer;component/" + path))!.Stream;

        private static Dictionary<string, Lazy<Clip>> LoadManifest(string folder)
        {
            using Stream stream = OpenResource($"Assets/Pets/{folder}/manifest.json");
            Manifest manifest = JsonSerializer.Deserialize<Manifest>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            if (folder != "VPet")
            {
                // 新角色共用一张动作图：每张图与每个透明度掩码只解码一次。
                var sheets = manifest.Clips.Values.GroupBy(info => info.Sheet).ToDictionary(group => group.Key,
                    group => new Lazy<Clip>(() => LoadClip(new ClipInfo
                    {
                        Sheet = group.Key,
                        Frames = Enumerable.Range(0, group.SelectMany(info => info.Frames).Max(frame => frame.Index) + 1)
                            .Select(index => new FrameInfo { Index = index }).ToList()
                    }, manifest.CellSize, manifest.Columns, folder)));
                return manifest.Clips.ToDictionary(pair => pair.Key, pair => new Lazy<Clip>(() => new Clip(
                    pair.Value.Frames.Select(frame => sheets[pair.Value.Sheet].Value.Frames[frame.Index]).ToArray(),
                    pair.Value.Frames.Select(frame => frame.Milliseconds).ToArray())));
            }
            return manifest.Clips.ToDictionary(pair => pair.Key,
                pair => new Lazy<Clip>(() => LoadClip(pair.Value, manifest.CellSize, manifest.Columns, folder)));
        }

        private static Clip LoadClip(ClipInfo info, int cellSize, int columns, string folder)
        {
            using Stream stream = OpenResource($"Assets/Pets/{folder}/" + info.Sheet);
            var sheet = new BitmapImage();
            sheet.BeginInit();
            sheet.CacheOption = BitmapCacheOption.OnLoad;
            sheet.StreamSource = stream;
            sheet.EndInit();
            sheet.Freeze();
            var frames = new Frame[info.Frames.Count];
            for (int index = 0; index < frames.Length; index++)
            {
                int cell = info.Frames[index].Index;
                var crop = new CroppedBitmap(sheet,
                    new Int32Rect(cell % columns * cellSize, cell / columns * cellSize, cellSize, cellSize));
                crop.Freeze();
                var pixels = new byte[cellSize * cellSize * 4];
                var bitmap = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
                bitmap.CopyPixels(pixels, cellSize * 4, 0);
                var alpha = new byte[cellSize * cellSize];
                for (int pixel = 0; pixel < alpha.Length; pixel++) alpha[pixel] = pixels[pixel * 4 + 3];
                frames[index] = new Frame(crop, alpha);
            }
            return new Clip(frames, info.Frames.Select(frame => frame.Milliseconds).ToArray());
        }
    }
}
