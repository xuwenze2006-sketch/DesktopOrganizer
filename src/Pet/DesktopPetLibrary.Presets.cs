namespace DesktopOrganizer;

internal sealed partial class DesktopPetLibrary
{
    // 搭配只描述作坊里的组合；桌面坐标、显示开关仍由当前布局管理。
    internal sealed record Composition(string CharacterId, string? SceneId, bool ShowScene, double Scale, double X, double Y);
    internal sealed record Preset(string Id, string Name, Composition Composition);
    internal IReadOnlyList<Preset> Presets => ReadSnapshot().Presets;

    private static bool ValidComposition(Composition? value) => value is not null &&
        (DesktopPetCatalog.BuiltInCharacters.Any(c => c.Id == value.CharacterId) || ValidId(value.CharacterId)) &&
        (value.SceneId is null ? !value.ShowScene : DesktopPetScene.BuiltInScenes.Any(s => s.Id == value.SceneId) || ValidId(value.SceneId)) &&
        double.IsFinite(value.Scale) && value.Scale is >= .5 and <= 1.6 &&
        double.IsFinite(value.X) && value.X is >= -240 and <= 240 &&
        double.IsFinite(value.Y) && value.Y is >= -240 and <= 240;

    private static bool ValidPresets(List<Preset>? presets) => presets is not null &&
        presets.All(p => p is not null && Guid.TryParseExact(p.Id, "N", out _) && ValidDisplayName(p.Name) && ValidComposition(p.Composition)) &&
        presets.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == presets.Count &&
        presets.Select(p => p.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == presets.Count;

    internal string? GetCompositionProblem(Composition composition)
    {
        if (!ValidComposition(composition)) return "搭配参数无效。";
        if (!DesktopPetCatalog.BuiltInCharacters.Any(c => c.Id == composition.CharacterId) && FindAsset(composition.CharacterId)?.Kind != "character")
            return "搭配中的角色素材缺失；请恢复原素材后再载入。";
        if (composition.SceneId is string scene && !DesktopPetScene.BuiltInScenes.Any(s => s.Id == scene) && FindAsset(scene)?.Kind != "scene")
            return "搭配中的背景素材缺失；请恢复原素材后再载入。";
        return null;
    }

    private string PresetName(string name, string? exceptId = null)
    {
        if (!ValidDisplayName(name)) throw new ArgumentException("搭配名称需为 1–40 个字符，不能包含换行或控制字符。");
        string trimmed = name.Trim();
        if (_index.Presets.Any(p => p.Id != exceptId && string.Equals(p.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("已有同名搭配，请换个名字，或使用「更新所选搭配」。");
        return trimmed;
    }

    internal Preset AddPreset(string name, Composition composition)
    {
        lock (_mutationGate)
        {
            EnsureWritable();
            if (GetCompositionProblem(composition) is string problem) throw new ArgumentException(problem);
            var preset = new Preset(Guid.NewGuid().ToString("N"), PresetName(name), composition);
            var next = CopyIndex(); next.Presets.Add(preset); Commit(next);
            return preset;
        }
    }

    private int PresetIndex(string id)
    {
        int index = _index.Presets.FindIndex(p => p.Id == id);
        return index >= 0 ? index : throw new ArgumentException("这个搭配已不存在，请重新选择。");
    }

    internal void UpdatePreset(string id, Composition composition)
    {
        lock (_mutationGate)
        {
            EnsureWritable();
            int index = PresetIndex(id);
            if (GetCompositionProblem(composition) is string problem) throw new ArgumentException(problem);
            if (_index.Presets[index].Composition == composition) return;
            var next = CopyIndex(); next.Presets[index] = next.Presets[index] with { Composition = composition }; Commit(next);
        }
    }

    internal void RenamePreset(string id, string name)
    {
        lock (_mutationGate)
        {
            EnsureWritable();
            int index = PresetIndex(id);
            string trimmed = PresetName(name, id);
            if (_index.Presets[index].Name == trimmed) return;
            var next = CopyIndex(); next.Presets[index] = next.Presets[index] with { Name = trimmed }; Commit(next);
        }
    }

    internal void DeletePreset(string id)
    {
        lock (_mutationGate)
        {
            EnsureWritable();
            int index = PresetIndex(id);
            var next = CopyIndex(); next.Presets.RemoveAt(index); Commit(next);
        }
    }
}
