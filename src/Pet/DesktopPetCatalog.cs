namespace DesktopOrganizer
{
    /// <summary>已随程序导入的角色目录，选择界面与布局校验共用同一身份列表。</summary>
    internal static class DesktopPetCatalog
    {
        internal const string SunsetDuoId = "sunset-duo";
        internal sealed record Character(string Id, string Name, string Description, string? AssetFolder);
        internal static IReadOnlyList<Character> BuiltInCharacters { get; } = Array.AsReadOnly(new[]
        {
            new Character("pixelpaws", "小黑猫", "PixelPaws · 轻点互动与打盹", null),
            new Character("vpet", "萝莉斯", "VPet · 摸头、提起与睡眠", "VPet"),
            new Character("black-haired", "黑发伙伴", "红瞳短发 · 挥手与轻巧落地", "BlackHaired"),
            new Character("pink-haired", "粉发伙伴", "蓝瞳长发 · 挥手、摸头与休息", "PinkHaired"),
            new Character(SunsetDuoId, "夕阳双人", "牵手组合 · 挥手、相视、轻摆与依偎", "SunsetDuo")
        });
        private static readonly System.Collections.Frozen.FrozenDictionary<string, Character> BuiltInById =
            System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(BuiltInCharacters, c => c.Id, StringComparer.Ordinal);
        private sealed record CatalogSnapshot(DesktopPetLibrary.Snapshot Source, IReadOnlyList<Character> Characters,
            System.Collections.Frozen.FrozenDictionary<string, Character> ById);
        private static CatalogSnapshot? _snapshot;
        private static CatalogSnapshot Current
        {
            get
            {
                var source = DesktopPetLibrary.Shared.ReadSnapshot();
                var cached = Volatile.Read(ref _snapshot);
                if (cached is not null && ReferenceEquals(cached.Source, source)) return cached;
                var next = CreateCatalog(source);
                Volatile.Write(ref _snapshot, next);
                return next;
            }
        }
        private static CatalogSnapshot CreateCatalog(DesktopPetLibrary.Snapshot source)
        {
            var characters = Array.AsReadOnly(BuiltInCharacters.Concat(source.Assets.Where(a => a.Kind == "character").Select(a =>
                new Character(a.Id, a.Name, a.Animated ? "导入动画 · 缺少的动作使用待机帧" : "导入图片 · 静态角色", null)))
                .Select(c => c with { Name = source.DisplayName(c.Id, c.Name) }).ToArray());
            return new CatalogSnapshot(source, characters,
                System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(characters, c => c.Id, StringComparer.Ordinal));
        }
        internal static IReadOnlyList<Character> Characters => Current.Characters;

        internal static Character Find(string? id)
        {
            var current = Current;
            return current.ById.GetValueOrDefault(id ?? "pixelpaws") ?? current.Characters[0];
        }
        internal static bool Contains(string id) => BuiltInById.ContainsKey(id) || Current.ById.ContainsKey(id);
        internal static bool IsImported(string id) => id is "black-haired" or "pink-haired" or SunsetDuoId || id.StartsWith("user-", StringComparison.Ordinal);
        internal static BitmapSource Preview(string id) => id == "pixelpaws"
            ? DesktopPetWidget.CatPreview : VPetAnimation.GetClip(id, "idle").Frames[0].Image;
    }
}
