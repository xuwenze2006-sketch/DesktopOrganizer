using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class GroupScrollPersistenceTests
{
    [TestMethod]
    public void Layout_RoundTripsOffsetsByStableId_AndOldConfigDefaultsEmpty()
    {
        PropertyInfo? property = typeof(AppLayoutData).GetProperty("GroupScrollOffsets");
        Assert.IsNotNull(property, "Restart recovery requires persisted offsets.");
        var layout = new AppLayoutData();
        var offsets = (Dictionary<string,double>)property.GetValue(layout)!;
        offsets["stable-group"] = 160;
        var restored = LayoutJsonSerializer.Deserialize(JsonSerializer.Serialize(layout)).Layout;
        Assert.AreEqual(160, ((Dictionary<string,double>)property.GetValue(restored)!)["stable-group"]);
        var old = LayoutJsonSerializer.Deserialize("{\"Version\":19,\"FreeIcons\":{},\"Groups\":[]}").Layout;
        Assert.AreEqual(0, ((Dictionary<string,double>)property.GetValue(old)!).Count);
        WorkspaceLayoutManager.Apply(restored, new WorkspaceLayoutState());
        Assert.AreEqual(160, ((Dictionary<string,double>)property.GetValue(restored)!)["stable-group"],
            "Workspace snapshots must retain the existing global per-ID scroll semantics.");
    }

    [STATestMethod]
    public void Restore_IgnoresPreMeasureZeroAndCollapsedViewport()
    {
        var changes = new List<double>();
        var panel = Panel(30, 160, changes.Add);
        panel.SetVerticalOffset(0); // ScrollViewer initialization before valid geometry.
        panel.Measure(new Size(300,0));
        Assert.AreEqual(160, panel.VerticalOffset);
        Assert.AreEqual(0, changes.Count);
        panel.Measure(new Size(300,160));
        panel.Arrange(new Rect(0,0,300,160));
        Assert.AreEqual(160, panel.VerticalOffset);
        panel.Measure(new Size(300,0));
        panel.SetVerticalOffset(0);
        panel.Measure(new Size(300,160));
        Assert.AreEqual(160, panel.VerticalOffset, "Collapsing must preserve the remembered position.");
    }

    [STATestMethod]
    [DataRow(6,160d,0d)]
    [DataRow(30,700d,100d)]
    public void Restore_ClampsToActualContentAndViewport(int count,double height,double expected)
    {
        var changes = new List<double>();
        var panel = Panel(count,160,changes.Add);
        panel.Measure(new Size(300,height));
        panel.Arrange(new Rect(0,0,300,height));
        Assert.AreEqual(expected,panel.VerticalOffset);
        Assert.AreEqual(expected,changes.Last());
    }

    [STATestMethod]
    public void RememberOffset_UpdatesPersistedStateAndSchedulesDebouncedSave()
    {
        var window = new MainWindow(startQuietly:false);
        var type=typeof(MainWindow);
        type.GetField("_layoutWriteProtected",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,true);
        var method=type.GetMethod("RememberGroupScrollOffset",BindingFlags.Instance|BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        method.Invoke(window,["stable-group",160d]);
        var layout=(AppLayoutData)type.GetField("_appLayout",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        var offsets=(Dictionary<string,double>)typeof(AppLayoutData).GetProperty("GroupScrollOffsets")!.GetValue(layout)!;
        Assert.AreEqual(160,offsets["stable-group"]);
        Assert.AreEqual(true,type.GetField("_layoutDirty",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window));
        method.Invoke(window,["stable-group",double.NaN]);
        Assert.AreEqual(160,offsets["stable-group"]);
    }

    [STATestMethod]
    public void ScrollToEnd_AcceptsWpfInfinityRequestAndReportsFiniteOffset()
    {
        var changes=new List<double>();
        var panel=Panel(30,0,changes.Add);
        panel.Measure(new Size(300,160));
        panel.SetVerticalOffset(double.PositiveInfinity);
        Assert.AreEqual(640,panel.VerticalOffset);
        Assert.AreEqual(640,changes.Last());
    }
    private static VirtualizingGroupPanel Panel(int count,double initial,Action<double> changed) => new(
        Enumerable.Range(0,count).Select(i=>new GroupVirtualItem($"test-{i}",$"C:\\Test\\test-{i}")).ToArray(),
        _=>new Border(),_=>{},3,100,80,initial,changed);
}
