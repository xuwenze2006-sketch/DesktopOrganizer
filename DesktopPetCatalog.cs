namespace DesktopOrganizer
{
    /// <summary>已随程序导入的角色目录，选择界面与布局校验共用同一身份列表。</summary>
    internal static class DesktopPetCatalog
    {
        internal const string SunsetDuoId = "sunset-duo";
        internal sealed record Character(string Id, string Name, string Description, string? AssetFolder);
        internal static IReadOnlyList<Character> Characters { get; } = Array.AsReadOnly(new[]
        {
            new Character("pixelpaws", "小黑猫", "PixelPaws · 轻点互动与打盹", null),
            new Character("vpet", "萝莉斯", "VPet · 摸头、提起与睡眠", "VPet"),
            new Character("black-haired", "黑发伙伴", "红瞳短发 · 挥手与轻巧落地", "BlackHaired"),
            new Character("pink-haired", "粉发伙伴", "蓝瞳长发 · 挥手与轻巧落地", "PinkHaired"),
            new Character(SunsetDuoId, "夕阳双人", "牵手组合 · 眨眼、点头与相伴休息", "SunsetDuo")
        });

        internal static Character Find(string? id) => Characters.FirstOrDefault(character => character.Id == id) ?? Characters[0];
        internal static bool Contains(string id) => Characters.Any(character => character.Id == id);
        internal static bool IsImported(string id) => id is "black-haired" or "pink-haired" or SunsetDuoId;
        internal static BitmapSource Preview(string id) => id == SunsetDuoId ? DesktopPetScene.SunsetPreview.Image : id == "pixelpaws"
            ? DesktopPetWidget.CatPreview : VPetAnimation.GetClip(id, "idle").Frames[0].Image;
    }
}
