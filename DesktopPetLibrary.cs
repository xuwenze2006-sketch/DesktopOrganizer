namespace DesktopOrganizer;

/// <summary>用户素材只复制到应用自己的素材目录；移除不删除原图、动画或许可。</summary>
internal sealed class DesktopPetLibrary
{
    internal static DesktopPetLibrary Shared { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopOrganizer", "PetLibrary"));
    internal sealed record Asset(string Id, string Name, string Kind, bool Animated);
    private sealed class Index
    {
        public List<Asset> Assets { get; set; } = [];
        public HashSet<string> Removed { get; set; } = [];
    }
    private readonly string _root;
    private Index _index = new();
    private readonly Dictionary<string, Dictionary<string, VPetAnimation.Clip>> _clips = new();
    private readonly Dictionary<string, DesktopPetScene.Layer> _images = new();
    private bool _readFailed;
    internal string? LoadWarning { get; private set; }
    internal IReadOnlyList<Asset> Assets => _index.Assets;
    internal bool IsRemoved(string id) => _index.Removed.Contains(id);

    internal DesktopPetLibrary(string root)
    {
        _root = Path.GetFullPath(root);
        string path = Path.Combine(_root, "library.json");
        if (!File.Exists(path)) return;
        try
        {
            _index = JsonSerializer.Deserialize<Index>(File.ReadAllText(path)) ?? throw new InvalidDataException();
            if (_index.Assets is null || _index.Removed is null || _index.Assets.Any(a =>
                a is null || !ValidId(a.Id) || string.IsNullOrWhiteSpace(a.Name) || a.Kind is not ("character" or "scene")) ||
                _index.Assets.Select(a => a.Id).Distinct().Count() != _index.Assets.Count) throw new InvalidDataException();
            foreach (Asset asset in _index.Assets.ToArray())
            {
                try { if (asset.Kind == "scene") GetImage(asset.Id); else GetClip(asset.Id, "idle"); }
                catch (Exception ex) when (IsAssetError(ex))
                {
                    _index.Assets.Remove(asset);
                    LoadWarning = "部分素材缺失或损坏，已暂时跳过。请检查素材目录后重新打开程序。";
                    _readFailed = true;
                }
            }
        }
        catch (Exception ex) when (IsAssetError(ex))
        {
            _index = new();
            _readFailed = true;
            LoadWarning = "素材库无法读取。为保护原记录，暂时不能更改素材库；内置角色仍可使用。";
        }
    }

    private static bool ValidId(string? id) => id is not null && id.StartsWith("user-", StringComparison.Ordinal) &&
        Guid.TryParseExact(id[5..], "N", out _);
    internal static bool IsAssetError(Exception ex) => ex is IOException or UnauthorizedAccessException or
        JsonException or ArgumentException or NotSupportedException or FileFormatException or OverflowException or
        COMException or InvalidOperationException;
    private void EnsureWritable()
    {
        if (_readFailed) throw new IOException(LoadWarning);
    }
    private void Save()
    {
        EnsureWritable();
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "library.json");
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_index, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
    internal void SetRemoved(string id, bool removed)
    {
        EnsureWritable();
        bool previous = IsRemoved(id);
        if (removed) _index.Removed.Add(id); else _index.Removed.Remove(id);
        try { Save(); }
        catch { if (previous) _index.Removed.Add(id); else _index.Removed.Remove(id); throw; }
    }
    internal void RestoreAll()
    {
        EnsureWritable();
        var previous = _index.Removed;
        _index.Removed = [];
        try { Save(); } catch { _index.Removed = previous; throw; }
    }

    internal Asset Import(string source, string kind)
    {
        EnsureWritable();
        if (kind is not ("character" or "scene")) throw new ArgumentException("未知素材类型。");
        bool animated = kind == "character" && Path.GetExtension(source).Equals(".json", StringComparison.OrdinalIgnoreCase);
        if (!animated && !Path.GetExtension(source).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请选择 PNG 图片，或角色动画的 manifest.json。");
        // 全部验证完成后才写入素材库，损坏或越界的动画不会成为可选择角色。
        if (animated) _ = ReadAnimation(source);
        BitmapSource? bitmap = animated ? null : ReadBitmap(source);
        string id = "user-" + Guid.NewGuid().ToString("N");
        string name = animated ? new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(source))!).Name : Path.GetFileNameWithoutExtension(source);
        var asset = new Asset(id, name, kind, animated);
        string target = Path.Combine(_root, id);
        Directory.CreateDirectory(target);
        if (animated)
        {
            File.Copy(source, Path.Combine(target, "manifest.json"));
            var manifest = ReadManifest(source);
            foreach (string sheet in manifest.Clips.Values.Select(c => c.Sheet).Distinct())
                File.Copy(SheetPath(source, sheet), Path.Combine(target, sheet));
            _clips[id] = ReadAnimation(Path.Combine(target, "manifest.json"));
        }
        else
        {
            // 等比放入统一透明画布，避免宽图被强行拉成正方形。
            BitmapSource square = FitSquare(bitmap!, kind == "scene" ? 1080 : 512);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(square));
            using (var output = File.Create(Path.Combine(target, "image.png"))) encoder.Save(output);
        }
        string sourceFolder = Path.GetDirectoryName(Path.GetFullPath(source))!;
        foreach (string license in Directory.EnumerateFiles(sourceFolder, "*.txt").Where(p =>
            Path.GetFileName(p).Contains("LICENSE", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(p).Contains("NOTICE", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(p).Contains("SOURCES", StringComparison.OrdinalIgnoreCase)))
        {
            if (new FileInfo(license).Length <= 1024 * 1024)
                File.Copy(license, Path.Combine(target, Path.GetFileName(license)), false);
        }
        _index.Assets.Add(asset);
        try { Save(); } catch { _index.Assets.Remove(asset); _clips.Remove(id); throw; }
        return asset;
    }

    internal DesktopPetScene.Layer GetImage(string id)
    {
        if (_images.TryGetValue(id, out var image)) return image;
        if (!ValidId(id)) throw new InvalidDataException("无效素材编号。");
        var bitmap = ReadBitmap(Path.Combine(_root, id, "image.png"));
        image = new(bitmap, Alpha(bitmap));
        _images.Add(id, image);
        return image;
    }
    internal VPetAnimation.Clip GetClip(string id, string name)
    {
        if (!_clips.TryGetValue(id, out var clips))
        {
            Asset asset = Assets.First(a => a.Id == id);
            if (asset.Animated) clips = ReadAnimation(Path.Combine(_root, id, "manifest.json"));
            else
            {
                var layer = GetImage(id);
                clips = new() { ["idle"] = new([new(layer.Image, layer.Alpha)], [1000]) };
            }
            _clips.Add(id, clips);
        }
        return clips.GetValueOrDefault(name) ?? clips["idle"];
    }
    internal static BitmapSource FitSquare(BitmapSource bitmap, int size)
    {
        double scale = Math.Min((double)size / bitmap.PixelWidth, (double)size / bitmap.PixelHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawImage(bitmap, new Rect((size - bitmap.PixelWidth * scale) / 2,
                size - bitmap.PixelHeight * scale, bitmap.PixelWidth * scale, bitmap.PixelHeight * scale));
        var result = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze(); return result;
    }
    private static BitmapSource ReadBitmap(string path)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("单张图片不能超过 32 MB。");
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        if (frame.PixelWidth > 4096 || frame.PixelHeight > 4096) throw new InvalidDataException("图片宽高不能超过 4096 像素。");
        var bitmap = new WriteableBitmap(frame); bitmap.Freeze(); return bitmap;
    }
    private static byte[] Alpha(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return Enumerable.Range(0, pixels.Length / 4).Select(i => pixels[i * 4 + 3]).ToArray();
    }
    private sealed class Manifest
    {
        public int CellSize { get; set; }
        public int Columns { get; set; }
        public Dictionary<string, AnimationClip> Clips { get; set; } = [];
    }
    private sealed class AnimationClip
    {
        public string Sheet { get; set; } = "";
        public List<AnimationFrame> Frames { get; set; } = [];
    }
    private sealed class AnimationFrame
    {
        public int Index { get; set; }
        public int Milliseconds { get; set; }
    }
    private static Manifest ReadManifest(string source)
    {
        if (new FileInfo(source).Length > 1024 * 1024) throw new InvalidDataException("动画清单过大。");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(source),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest is null || manifest.CellSize is < 16 or > 1024 || manifest.Columns is < 1 or > 256 ||
            manifest.Clips is null || !manifest.Clips.ContainsKey("idle") || manifest.Clips.Count > 32 ||
            manifest.Clips.Values.Any(c => c is null || c.Frames is null || c.Frames.Count is < 1 or > 256 ||
                c.Frames.Any(f => f is null || f.Index < 0 || f.Milliseconds is < 16 or > 30000)))
            throw new InvalidDataException("动画清单无效：需要 idle 动作、有效帧号和 16–30000 ms 的帧时长。");
        return manifest;
    }
    private static string SheetPath(string manifest, string sheet)
    {
        if (string.IsNullOrWhiteSpace(sheet) || sheet != Path.GetFileName(sheet) || sheet.Contains(':') ||
            !sheet.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("动画图片必须是清单同目录下的 PNG 文件。");
        string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifest))!, sheet);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("动画图片不能是链接。");
        return path;
    }
    private static Dictionary<string, VPetAnimation.Clip> ReadAnimation(string source)
    {
        Manifest manifest = ReadManifest(source);
        var frames = new Dictionary<(string, int), VPetAnimation.Frame>();
        var result = new Dictionary<string, VPetAnimation.Clip>();
        long pixelBudget = 0;
        foreach (var group in manifest.Clips.GroupBy(c => c.Value.Sheet))
        {
            BitmapSource sheet = ReadBitmap(SheetPath(source, group.Key));
            pixelBudget += (long)sheet.PixelWidth * sheet.PixelHeight;
            if (pixelBudget > 32 * 1024 * 1024 || sheet.PixelWidth != manifest.Columns * manifest.CellSize ||
                sheet.PixelHeight % manifest.CellSize != 0) throw new InvalidDataException("动画图尺寸与清单不匹配，或素材总像素过多。");
            foreach (int index in group.SelectMany(c => c.Value.Frames).Select(f => f.Index).Distinct())
            {
                if (index >= (long)manifest.Columns * (sheet.PixelHeight / manifest.CellSize)) throw new InvalidDataException("动画帧号超出图片范围。");
                var crop = new CroppedBitmap(sheet, new Int32Rect(index % manifest.Columns * manifest.CellSize,
                    index / manifest.Columns * manifest.CellSize, manifest.CellSize, manifest.CellSize));
                crop.Freeze(); frames[(group.Key, index)] = new(crop, Alpha(crop));
            }
            foreach (var clip in group)
                result[clip.Key] = new(clip.Value.Frames.Select(f => frames[(group.Key, f.Index)]).ToArray(),
                    clip.Value.Frames.Select(f => f.Milliseconds).ToArray());
        }
        return result;
    }
}
