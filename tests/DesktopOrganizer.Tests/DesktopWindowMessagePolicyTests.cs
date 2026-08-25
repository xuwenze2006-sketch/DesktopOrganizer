namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopWindowMessagePolicyTests
{
    [TestMethod]
    [DataRow(DesktopWindowMessagePolicy.WmDisplayChange, 0L, true)]
    [DataRow(DesktopWindowMessagePolicy.WmDpiChanged, 0L, true)]
    [DataRow(DesktopWindowMessagePolicy.WmSettingChange, DesktopWindowMessagePolicy.SpiSetWorkArea, true)]
    [DataRow(DesktopWindowMessagePolicy.WmSettingChange, 0x0014L, false)]
    [DataRow(DesktopWindowMessagePolicy.WmSettingChange, 0L, false)]
    [DataRow(0x000F, 0L, false)]
    public void IsGeometryChangeMessage_FiltersUnrelatedSystemBroadcasts(
        int message,
        long wParam,
        bool expected)
    {
        bool actual = DesktopWindowMessagePolicy.IsGeometryChangeMessage(
            message,
            new IntPtr(wParam));

        Assert.AreEqual(expected, actual);
    }
}
