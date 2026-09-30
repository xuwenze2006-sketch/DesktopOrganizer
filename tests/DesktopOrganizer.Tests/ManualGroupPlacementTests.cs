using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class ManualGroupPlacementTests
{
    [STATestMethod]
    [DataRow(96u)]
    [DataRow(144u)]
    [DataRow(192u)]
    public void Placement_AvoidsExistingFreeIconLabelWithoutMovingIcon(uint dpi)
    {
        var window = CreateWindow(new Rect(0, 0, 800, 600), dpi);
        AppLayoutData layout = Field<AppLayoutData>(window, "_appLayout");
        layout.FreeIcons["DOT_TEST_Project"] = new IconPosition { X = 110, Y = 70 };
        Field<Dictionary<string, string>>(window, "_desktopItems")["DOT_TEST_Project"] = @"C:\Test\DOT_TEST_Project";
        Point position = (Point)Invoke(window, "FindAvailableAutoGroupPosition", 180d, 140d)!;
        var group = new Rect(position, new Size(180, 140));
        Assert.IsFalse(group.IntersectsWith(new Rect(110,70,90,90)),
            "The label below the icon must not cover the new group's header.");
        Assert.AreEqual(110, layout.FreeIcons["DOT_TEST_Project"].X);
        Assert.AreEqual(70, layout.FreeIcons["DOT_TEST_Project"].Y);
        Assert.IsTrue(new Rect(0,0,800,600).Contains(group));
    }

    [STATestMethod]
    public void FullDesktop_ReturnsNoPlacementInsteadOfOverlappingFallback()
    {
        var window = CreateWindow(new Rect(0,0,240,280), 96);
        var layout = Field<AppLayoutData>(window,"_appLayout");
        layout.Groups.Add(new GroupInfo { X=0,Y=0,Width=240,Height=280,IsSizeLocked=true });
        MethodInfo? method = typeof(MainWindow).GetMethod("TryFindAvailableAutoGroupPosition",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, "Manual creation needs a failure result when there is no free space.");
        object[] args = [180d,140d,new Point()];
        Assert.AreEqual(false, method.Invoke(window,args));
        Assert.AreEqual(1,layout.Groups.Count);
    }

    [STATestMethod]
    public void NarrowEdgeGap_IsUsedWithoutCrossingMonitorBoundary()
    {
        var window = CreateWindow(new Rect(0,0,400,252),96);
        Field<AppLayoutData>(window,"_appLayout").Groups.Add(new GroupInfo {
            X=0,Y=112,Width=195,Height=140,IsSizeLocked=true });
        Point position = (Point)Invoke(window,"FindAvailableAutoGroupPosition",180d,140d)!;
        Assert.IsTrue(new Rect(0,0,400,252).Contains(new Rect(position,new Size(180,140))));
        Assert.IsTrue(position.X > 209, "The fixed-step grid skips the only gap at the right edge.");
    }

    [STATestMethod]
    public void FailedManualCreation_DoesNotChangeEditModeOrAddGroup()
    {
        var window=CreateWindow(new Rect(0,0,240,280),96);
        var layout=Field<AppLayoutData>(window,"_appLayout");
        layout.IsEditMode=false;
        layout.Groups.Add(new GroupInfo { X=0,Y=0,Width=240,Height=280,IsSizeLocked=true });
        Invoke(window,"AddManualGroup","no-room");
        Assert.AreEqual(1,layout.Groups.Count);
        Assert.IsFalse(layout.IsEditMode, "Rejected creation must leave the existing interaction mode unchanged.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(window.StatusText.Text));
    }

    [STATestMethod]
    public void Placement_UsesSecondaryMonitorWhenPrimaryIsFull()
    {
        var window=CreateWindow(new Rect(0,0,400,300),144);
        Field<AppLayoutData>(window,"_appLayout").Groups.Add(new GroupInfo {
            X=0,Y=0,Width=400,Height=300,IsSizeLocked=true });
        typeof(MainWindow).GetField("_desktopGeometry",BindingFlags.Instance|BindingFlags.NonPublic)!
            .SetValue(window,new DesktopGeometry([
                new DesktopMonitorRegion { DeviceName="primary",Bounds=new Rect(0,0,400,300),
                    WorkArea=new Rect(0,0,400,300),IsPrimary=true,DpiX=144,DpiY=144 },
                new DesktopMonitorRegion { DeviceName="secondary",Bounds=new Rect(400,0,400,300),
                    WorkArea=new Rect(400,0,400,300),DpiX=96,DpiY=96 }]));
        Point position=(Point)Invoke(window,"FindAvailableAutoGroupPosition",180d,140d)!;
        Assert.IsTrue(new Rect(400,0,400,300).Contains(new Rect(position,new Size(180,140))));
    }

    [STATestMethod]
    public void OversizedGroup_ReturnsNoPlacement()
    {
        var window=CreateWindow(new Rect(0,0,400,300),96);
        object[] args=[500d,140d,new Point()];
        Assert.AreEqual(false,Invoke(window,"TryFindAvailableAutoGroupPosition",args));
    }

    [STATestMethod]
    public void SmallDesktop_CanUseTopSpaceWhenNoControlPanelOccupiesIt()
    {
        var window=CreateWindow(new Rect(0,0,400,160),96);
        object[] args=[180d,140d,new Point()];
        Assert.AreEqual(true,Invoke(window,"TryFindAvailableAutoGroupPosition",args),
            "A hidden toolbar must not permanently reserve the top 112 DIPs.");
        Assert.IsTrue(new Rect(0,0,400,160).Contains(new Rect((Point)args[2],new Size(180,140))));
    }

    private static MainWindow CreateWindow(Rect workArea,uint dpi)
    {
        var window = new MainWindow(startQuietly:false);
        var layout=Field<AppLayoutData>(window,"_appLayout");
        layout.Groups.Clear(); layout.FreeIcons.Clear(); layout.FolderPortals.Clear();
        layout.RecycleBinWidget.IsVisible=false;
        window.RecycleBinWidget.Visibility=Visibility.Collapsed;
        Field<Dictionary<string,string>>(window,"_desktopItems").Clear();
        typeof(MainWindow).GetField("_desktopGeometry",BindingFlags.Instance|BindingFlags.NonPublic)!
            .SetValue(window,new DesktopGeometry([new DesktopMonitorRegion {
                DeviceName="TEST",Bounds=workArea,WorkArea=workArea,IsPrimary=true,DpiX=dpi,DpiY=dpi }]));
        return window;
    }
    private static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow)
        .GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window,string name,params object[] args)=>typeof(MainWindow)
        .GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,args);
}
