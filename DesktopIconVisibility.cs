namespace DesktopOrganizer;

internal static class DesktopIconVisibility
{
    internal const uint NoIcons = 0x1000; // FWF_NOICONS

    // 返回值表示操作成功；changed 只表示本次确实改变了图标状态，供调用方记录恢复责任。
    internal static bool TrySet(
        bool visible,
        Func<uint?> readFlags,
        Func<uint, uint, bool> setFlags,
        out bool changed)
    {
        changed = false;
        uint? flags = readFlags();
        if (flags == null)
            return false;

        bool currentlyVisible = (flags.Value & NoIcons) == 0;
        if (currentlyVisible == visible)
            return true;

        // 只修改 NOICONS，不能覆盖排列、对齐等用户设置。
        changed = setFlags(NoIcons, visible ? 0 : NoIcons);
        return changed;
    }
}
