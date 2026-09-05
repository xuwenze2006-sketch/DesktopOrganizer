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

        private static readonly Lazy<Dictionary<string, Lazy<Clip>>> Clips = new(LoadManifest);

        internal static IEnumerable<string> ClipNames => Clips.Value.Keys;
        internal static Clip GetClip(string name) => Clips.Value[name].Value;

        internal static Stream OpenResource(string path) => Application.GetResourceStream(new Uri(
            "pack://application:,,,/DesktopOrganizer;component/" + path))!.Stream;

        private static Dictionary<string, Lazy<Clip>> LoadManifest()
        {
            using Stream stream = OpenResource("Assets/Pets/VPet/manifest.json");
            Manifest manifest = JsonSerializer.Deserialize<Manifest>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            return manifest.Clips.ToDictionary(pair => pair.Key,
                pair => new Lazy<Clip>(() => LoadClip(pair.Value, manifest.CellSize, manifest.Columns)));
        }

        private static Clip LoadClip(ClipInfo info, int cellSize, int columns)
        {
            using Stream stream = OpenResource("Assets/Pets/VPet/" + info.Sheet);
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
