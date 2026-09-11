namespace DesktopOrganizer;

/// <summary>用户素材只复制到应用自己的素材目录；移除不删除原图、动画或许可。</summary>
internal sealed class DesktopPetLibrary
{
    private static readonly object SharedGate = new();
    private static DesktopPetLibrary? _shared;
    internal static DesktopPetLibrary Shared
    {
        get
        {
            var current = Volatile.Read(ref _shared);
            if (current is not null) return current;
            lock (SharedGate)
            {
                current = _shared;
                if (current is null)
                {
                    current = new DesktopPetLibrary(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "DesktopOrganizer", "PetLibrary"));
                    Volatile.Write(ref _shared, current);
                }
                return current;
            }
        }
    }
    // 宿主可在首次读取前指定存储实例；初始化后不可更换，避免运行中串库。
    internal static void InitializeShared(DesktopPetLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        lock (SharedGate)
        {
            if (_shared is not null) throw new InvalidOperationException("素材库已初始化。");
            Volatile.Write(ref _shared, library);
        }
    }
    internal sealed record Asset(string Id, string Name, string Kind, bool Animated);
    private sealed class Index
    {
        public List<Asset> Assets { get; set; } = [];
        public HashSet<string> Removed { get; set; } = [];
    }
    private sealed record Snapshot(IReadOnlyList<Asset> Assets,
        System.Collections.Frozen.FrozenDictionary<string, Asset> ById,
        System.Collections.Frozen.FrozenSet<string> Removed);
    private readonly string _root;
    private readonly object _mutationGate = new();
    private Index _index = new();
    private Snapshot _snapshot = CreateSnapshot(new Index());
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Dictionary<string, VPetAnimation.Clip>>> _clips = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<DesktopPetScene.Layer>> _images = new();
    private bool _readFailed;
    internal string? LoadWarning { get; private set; }
    internal IReadOnlyList<Asset> Assets => Volatile.Read(ref _snapshot).Assets;
    internal Asset? FindAsset(string id) => Volatile.Read(ref _snapshot).ById.GetValueOrDefault(id);
    internal bool IsRemoved(string id) => Volatile.Read(ref _snapshot).Removed.Contains(id);
    private const long MaxFileBytes = 32 * 1024 * 1024;
    private const long MaxImportBytes = 128 * 1024 * 1024;

    private static Snapshot CreateSnapshot(Index index) => new(Array.AsReadOnly(index.Assets.ToArray()),
        System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(index.Assets, a => a.Id, StringComparer.Ordinal),
        System.Collections.Frozen.FrozenSet.ToFrozenSet(index.Removed, StringComparer.Ordinal));

    internal DesktopPetLibrary(string root)
    {
        _root = Path.GetFullPath(root);
        string path = Path.Combine(_root, "library.json");
        if (!File.Exists(path)) return;
        try
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("素材索引过大。");
            _index = JsonSerializer.Deserialize<Index>(File.ReadAllText(path)) ?? throw new InvalidDataException();
            if (_index.Assets is null || _index.Removed is null || _index.Assets.Any(a =>
                a is null || !ValidId(a.Id) || string.IsNullOrWhiteSpace(a.Name) || a.Kind is not ("character" or "scene")) ||
                _index.Assets.Select(a => a.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _index.Assets.Count)
                throw new InvalidDataException();
            _snapshot = CreateSnapshot(_index);
            foreach (Asset asset in _index.Assets.ToArray())
            {
                try { if (asset.Kind == "scene") GetImage(asset.Id); else GetClip(asset.Id, "idle"); }
                catch (Exception ex) when (IsAssetError(ex))
                {
                    _index.Assets.Remove(asset);
                    _clips.TryRemove(asset.Id, out _);
                    _images.TryRemove(asset.Id, out _);
                    LoadWarning = "部分素材缺失或损坏，已暂时跳过。请检查素材目录后重新打开程序。";
                    _readFailed = true;
                }
            }
            _snapshot = CreateSnapshot(_index);
        }
        catch (Exception ex) when (IsAssetError(ex))
        {
            _index = new();
            _snapshot = CreateSnapshot(_index);
            _clips.Clear(); _images.Clear();
            _readFailed = true;
            LoadWarning = "素材库无法读取。为保护原记录，暂时不能更改素材库；内置角色仍可使用。";
        }
    }

    private static bool ValidId(string? id) => id is not null && id.StartsWith("user-", StringComparison.Ordinal) &&
        Guid.TryParseExact(id[5..], "N", out _);
    internal static bool IsAssetError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or
        JsonException or ArgumentException or NotSupportedException or FileFormatException or OverflowException or
        COMException or InvalidOperationException;
    private void EnsureWritable()
    {
        if (_readFailed) throw new IOException(LoadWarning);
    }
    private void Save(Index next)
    {
        EnsureWritable();
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "library.json");
        string temp = Path.Combine(_root, "library-" + Guid.NewGuid().ToString("N") + ".tmp");
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(next, new JsonSerializerOptions { WriteIndented = true });
        if (data.Length > 1024 * 1024) throw new InvalidDataException("素材索引过大，无法继续添加。");
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(data); file.Flush(flushToDisk: true); }
            File.Move(temp, path, true);
        }
        finally
        {
            try { File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    private void Commit(Index next)
    {
        // 先准备快照；磁盘成功后才对渲染线程发布，读者永远看到完整的旧或新目录。
        Snapshot snapshot = CreateSnapshot(next);
        Save(next);
        _index = next;
        Volatile.Write(ref _snapshot, snapshot);
    }
    internal void SetRemoved(string id, bool removed)
    {
        lock (_mutationGate)
        {
            EnsureWritable();
            if (IsRemoved(id) == removed) return;
            var next = new Index { Assets = _index.Assets, Removed = new(_index.Removed) };
            if (removed) next.Removed.Add(id); else next.Removed.Remove(id);
            Commit(next);
        }
    }
    internal void RestoreAll()
    {
        lock (_mutationGate)
        {
            EnsureWritable();
            if (_index.Removed.Count == 0) return;
            Commit(new Index { Assets = _index.Assets, Removed = [] });
        }
    }

    internal async Task<Asset> ImportAsync(string source, string kind, CancellationToken cancellationToken = default)
    {
        // 图像整理需要 STA；复用现有的串行后台执行器，不占用桌面界面线程。
        using var worker = new FileOperationService();
        return await worker.Enqueue(token => Import(source, kind, token), cancellationToken).ConfigureAwait(false);
    }

    internal Asset Import(string source, string kind, CancellationToken cancellationToken = default)
    {
        lock (_mutationGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureWritable();
            if (kind is not ("character" or "scene")) throw new ArgumentException("未知素材类型。");
            source = Path.GetFullPath(source);
            bool animated = kind == "character" && Path.GetExtension(source).Equals(".json", StringComparison.OrdinalIgnoreCase);
            if (!animated && !Path.GetExtension(source).Equals(".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("请选择 PNG 图片，或角色动画的 manifest.json。");
            string id = "user-" + Guid.NewGuid().ToString("N");
            string name = (animated ? new DirectoryInfo(Path.GetDirectoryName(source)!).Name : Path.GetFileNameWithoutExtension(source)).Trim();
            if (name.Length == 0) name = kind == "scene" ? "导入背景" : "导入角色";
            var asset = new Asset(id, name, kind, animated);
            string stage = Path.Combine(_root, ".import-" + Guid.NewGuid().ToString("N"));
            string target = Path.Combine(_root, id);
            string owned = stage;
            bool committed = false;
            long copiedBytes = 0;
            Dictionary<string, VPetAnimation.Clip>? clips = null;
            DesktopPetScene.Layer? image = null;
            try
            {
                Directory.CreateDirectory(stage);
                if (animated)
                {
                    string manifestPath = Path.Combine(stage, "manifest.json");
                    CopyImportFile(source, manifestPath, 1024 * 1024, ref copiedBytes, cancellationToken);
                    Manifest manifest = ReadManifest(manifestPath);
                    foreach (string sheet in manifest.Clips.Values.Select(c => c.Sheet).Distinct(StringComparer.OrdinalIgnoreCase))
                        CopyImportFile(SheetPath(source, sheet), Path.Combine(stage, sheet), MaxFileBytes, ref copiedBytes, cancellationToken);
                    // 仅解码已复制的快照一次；用户在复制期间更改原图也不会绕过验证。
                    clips = ReadAnimation(manifestPath, manifest, cancellationToken);
                }
                else
                {
                    string copied = Path.Combine(stage, "source.png");
                    CopyImportFile(source, copied, MaxFileBytes, ref copiedBytes, cancellationToken);
                    BitmapSource square = FitSquare(ReadBitmap(copied), kind == "scene" ? 1080 : 512);
                    cancellationToken.ThrowIfCancellationRequested();
                    image = new(square, Alpha(square));
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(square));
                    using (var output = File.Create(Path.Combine(stage, "image.png"))) encoder.Save(output);
                    File.Delete(copied);
                    if (kind == "character") clips = new() { ["idle"] = new([new(image.Image, image.Alpha)], [1000]) };
                }
                foreach (string license in Directory.EnumerateFiles(Path.GetDirectoryName(source)!, "*.txt").Where(p =>
                    Path.GetFileName(p).Contains("LICENSE", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(p).Contains("NOTICE", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(p).Contains("SOURCES", StringComparison.OrdinalIgnoreCase)))
                {
                    if (new FileInfo(license).Length <= 1024 * 1024)
                        CopyImportFile(license, Path.Combine(stage, Path.GetFileName(license)), 1024 * 1024, ref copiedBytes, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(stage, target);
                owned = target;
                if (clips is not null) _clips[id] = new Lazy<Dictionary<string, VPetAnimation.Clip>>(clips);
                if (image is not null) _images[id] = new Lazy<DesktopPetScene.Layer>(image);
                Commit(new Index { Assets = [.. _index.Assets, asset], Removed = _index.Removed });
                committed = true;
                return asset;
            }
            finally
            {
                if (!committed)
                {
                    _clips.TryRemove(id, out _); _images.TryRemove(id, out _);
                    DeleteImportDirectory(owned);
                }
            }
        }
    }

    private static void CopyImportFile(string source, string target, long maximum, ref long copiedBytes, CancellationToken token)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("素材文件不能是链接。");
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > maximum || copiedBytes + input.Length > MaxImportBytes) throw new InvalidDataException("素材文件或导入总大小超出限制。");
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long length = 0;
        int read;
        while ((read = input.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested();
            length += read; copiedBytes += read;
            if (length > maximum || copiedBytes > MaxImportBytes) throw new InvalidDataException("素材文件或导入总大小超出限制。");
            output.Write(buffer, 0, read);
        }
    }
    private void DeleteImportDirectory(string path)
    {
        // 只清理本次创建、位于素材根目录正下方的目录，不接触用户原始文件。
        string fullPath = Path.GetFullPath(path);
        string name = Path.GetFileName(fullPath);
        if (!string.Equals(Path.GetDirectoryName(fullPath), _root, StringComparison.OrdinalIgnoreCase) ||
            !(ValidId(name) || name.StartsWith(".import-", StringComparison.Ordinal) && Guid.TryParseExact(name[8..], "N", out _)))
            throw new InvalidOperationException("导入清理路径不属于素材库。");
        try
        {
            if (!Directory.Exists(fullPath)) return;
            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0) return;
            // 导入只允许同级文件；遇到意外子目录时保留现场，不做递归删除。
            foreach (string file in Directory.EnumerateFiles(fullPath)) File.Delete(file);
            Directory.Delete(fullPath, recursive: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal DesktopPetScene.Layer GetImage(string id)
    {
        if (!_images.TryGetValue(id, out var image))
        {
            if (!ValidId(id) || FindAsset(id) is null) throw new InvalidDataException("无效素材编号。");
            image = _images.GetOrAdd(id, new Lazy<DesktopPetScene.Layer>(() =>
            {
                var bitmap = ReadBitmap(Path.Combine(_root, id, "image.png"));
                return new(bitmap, Alpha(bitmap));
            }));
        }
        return image.Value;
    }
    internal VPetAnimation.Clip GetClip(string id, string name)
    {
        if (!_clips.TryGetValue(id, out var cached))
        {
            Asset asset = FindAsset(id) ?? throw new InvalidDataException("无效角色编号。");
            cached = _clips.GetOrAdd(id, new Lazy<Dictionary<string, VPetAnimation.Clip>>(() =>
            {
                if (asset.Animated) return ReadAnimation(Path.Combine(_root, id, "manifest.json"));
                var layer = GetImage(id);
                return new() { ["idle"] = new([new(layer.Image, layer.Alpha)], [1000]) };
            }));
        }
        var clips = cached.Value;
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
    private static BitmapSource ReadBitmap(string path, long maximumPixels = 32 * 1024 * 1024)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("单张图片不能超过 32 MB。");
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        if (frame.PixelWidth > 4096 || frame.PixelHeight > 4096 || (long)frame.PixelWidth * frame.PixelHeight > maximumPixels) throw new InvalidDataException("图片宽高不能超过 4096 像素，且总像素不能超出限制。");
        var bitmap = new WriteableBitmap(frame); bitmap.Freeze(); return bitmap;
    }
    private static byte[] Alpha(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var alpha = new byte[pixels.Length / 4];
        for (int i = 0; i < alpha.Length; i++) alpha[i] = pixels[i * 4 + 3];
        return alpha;
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
    private static Dictionary<string, VPetAnimation.Clip> ReadAnimation(string source, Manifest? manifest = null, CancellationToken token = default)
    {
        manifest ??= ReadManifest(source);
        var frames = new Dictionary<(string, int), VPetAnimation.Frame>();
        var result = new Dictionary<string, VPetAnimation.Clip>();
        long pixelBudget = 0;
        foreach (var group in manifest.Clips.GroupBy(c => c.Value.Sheet))
        {
            token.ThrowIfCancellationRequested();
            BitmapSource sheet = ReadBitmap(SheetPath(source, group.Key), 32 * 1024 * 1024 - pixelBudget);
            pixelBudget += (long)sheet.PixelWidth * sheet.PixelHeight;
            if (pixelBudget > 32 * 1024 * 1024 || sheet.PixelWidth != manifest.Columns * manifest.CellSize ||
                sheet.PixelHeight % manifest.CellSize != 0) throw new InvalidDataException("动画图尺寸与清单不匹配，或素材总像素过多。");
            foreach (int index in group.SelectMany(c => c.Value.Frames).Select(f => f.Index).Distinct())
            {
                token.ThrowIfCancellationRequested();
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
