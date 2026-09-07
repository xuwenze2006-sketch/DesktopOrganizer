using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopIconVisibilityTests
{
    [TestMethod]
    public void HideAndRestore_PreserveOtherFolderSettings()
    {
        // 自动排列、贴齐网格、桌面视图等位不能被图标隐藏/恢复覆盖。
        const uint original = 0x621;
        uint current = original;
        bool Set(uint mask, uint value)
        {
            Assert.AreEqual(0x1000u, mask);
            current = (current & ~mask) | (value & mask);
            return true;
        }

        Assert.IsTrue(DesktopIconVisibility.TrySet(false, () => current, Set, out bool hidden));
        Assert.IsTrue(hidden);
        Assert.AreEqual(original | 0x1000u, current);
        Assert.IsTrue(DesktopIconVisibility.TrySet(true, () => current, Set, out bool restored));
        Assert.IsTrue(restored);
        Assert.AreEqual(original, current);
    }

    [TestMethod]
    public void AlreadyHidden_DoesNotClaimResponsibilityForUsersSetting()
    {
        Assert.IsTrue(DesktopIconVisibility.TrySet(false, () => 0x1621u,
            (_, _) => throw new AssertFailedException("Must not rewrite an unchanged setting."),
            out bool changed));
        Assert.IsFalse(changed);
    }

    [TestMethod]
    public void Guard_DoesNotRepeatShellWritesUntilIconsBecomeVisibleAgain()
    {
        uint current = 0x621;
        int writes = 0;
        bool Set(uint mask, uint value)
        {
            writes++;
            current = (current & ~mask) | value;
            return true;
        }

        Assert.IsTrue(DesktopIconVisibility.TrySet(false, () => current, Set, out _));
        Assert.IsTrue(DesktopIconVisibility.TrySet(false, () => current, Set, out bool unchanged));
        Assert.IsFalse(unchanged);
        Assert.AreEqual(1, writes);
        current &= ~0x1000u; // Explorer 重建或用户重新显示图标。
        Assert.IsTrue(DesktopIconVisibility.TrySet(false, () => current, Set, out bool hiddenAgain));
        Assert.IsTrue(hiddenAgain);
        Assert.AreEqual(2, writes);
    }

    [TestMethod]
    public void ReadFailure_DoesNotWriteOrClaimRecoveryResponsibility()
    {
        Assert.IsFalse(DesktopIconVisibility.TrySet(false, () => null,
            (_, _) => throw new AssertFailedException("Unknown Shell state must remain untouched."),
            out bool changed));
        Assert.IsFalse(changed);
    }

    [TestMethod]
    public void WriteFailure_DoesNotClaimRecoveryResponsibility()
    {
        Assert.IsFalse(DesktopIconVisibility.TrySet(false, () => 0x621u,
            (_, _) => false, out bool changed));
        Assert.IsFalse(changed);
    }
}
