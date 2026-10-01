using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Windows;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class GroupExpansionTests
{
    [STATestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Expand_MovesOnlyCollidingGroup_AndRepeatedToggleKeepsNewPosition(bool push)
    {
        var (window,layout,docs,work)=Fixture();
        layout.PushReflowEnabled=push;
        Invoke(window,"ToggleGroupCollapsed",docs);
        Assert.IsFalse(docs.IsCollapsed);
        Assert.IsFalse(Bounds(docs).IntersectsWith(Bounds(work)));
        Assert.AreEqual(new Point(781,301),new Point(work.X,work.Y));
        Point moved=new(docs.X,docs.Y);
        Invoke(window,"ToggleGroupCollapsed",docs);
        Invoke(window,"ToggleGroupCollapsed",docs);
        Assert.AreEqual(moved,new Point(docs.X,docs.Y));
    }

    [STATestMethod]
    public void ExpandAll_UsesSameCollisionProtection()
    {
        var (window,_,docs,work)=Fixture();
        work.IsCollapsed=true;
        Invoke(window,"ToggleAllGroupsCollapsed");
        Assert.IsFalse(docs.IsCollapsed); Assert.IsFalse(work.IsCollapsed);
        Assert.IsFalse(Bounds(docs).IntersectsWith(Bounds(work)));
    }

    [STATestMethod]
    public void SearchExpansion_UsesSameCollisionProtection()
    {
        var (window,_,docs,work)=Fixture();
        docs.ItemNames.Add("test.txt");
        Field<Dictionary<string,string>>(window,"_desktopItems")["test.txt"]=@"C:\Test\test.txt";
        window.LocateDesktopSearchResult("test.txt");
        Assert.IsFalse(docs.IsCollapsed);
        Assert.IsFalse(Bounds(docs).IntersectsWith(Bounds(work)));
    }

    [STATestMethod]
    public void NoSpace_KeepsCollapsedAndCoordinates_AndReportsFailure()
    {
        var (window,layout,docs,work)=Fixture();
        work.X=0; work.Y=0; work.Width=1200; work.Height=800;
        Point original=new(docs.X,docs.Y);
        Invoke(window,"ToggleGroupCollapsed",docs);
        Assert.IsTrue(docs.IsCollapsed);
        Assert.AreEqual(original,new Point(docs.X,docs.Y));
        Assert.IsTrue(window.StatusText.Text.Contains("空间不足"));
        layout.GroupScrollOffsets[docs.Id]=160;
        docs.ItemNames.Add("test.txt");
        Field<Dictionary<string,string>>(window,"_desktopItems")["test.txt"]=@"C:\Test\test.txt";
        window.LocateDesktopSearchResult("test.txt");
        Assert.IsTrue(docs.IsCollapsed);
        Assert.IsTrue(window.StatusText.Text.Contains("空间不足"));
        Assert.AreEqual(160,layout.GroupScrollOffsets[docs.Id]);
    }

    [STATestMethod]
    public void SafeOriginalPosition_StaysUnchanged()
    {
        var (window,_,docs,work)=Fixture(); work.X=20; work.Y=20;
        Invoke(window,"ToggleGroupCollapsed",docs);
        Assert.AreEqual(new Point(704,184),new Point(docs.X,docs.Y));
    }

    [STATestMethod]
    public void Expansion_AvoidsPortalAndFreeIconLabels_WithinScreen()
    {
        var (window,layout,docs,work)=Fixture();
        layout.FolderPortals.Add(new FolderPortalInfo {X=500,Y=180,Width=180,Height=350});
        layout.FreeIcons["free.txt"]=new IconPosition {X=704,Y=80};
        Field<Dictionary<string,string>>(window,"_desktopItems")["free.txt"]=@"C:\Test\free.txt";
        Invoke(window,"ToggleGroupCollapsed",docs);
        Assert.IsFalse(Bounds(docs).IntersectsWith(new Rect(500,180,180,350)));
        Assert.IsFalse(Bounds(docs).IntersectsWith(new Rect(704,80,90,90)));
        Assert.IsFalse(Bounds(docs).IntersectsWith(Bounds(work)));
        Assert.IsTrue(new Rect(0,0,1200,800).Contains(Bounds(docs)));
        Assert.AreEqual(704,layout.FreeIcons["free.txt"].X);
    }

    [TestMethod]
    public void Placement_UsesEdgeSpace_AndRejectsOversizedRectangle()
    {
        Rect area=new(0,0,304,316);
        Assert.IsTrue(GroupExpansionPlacementPolicy.TryFind(new Rect(200,250,304,316),[area],[],out Point position));
        Assert.AreEqual(new Point(0,0),position);
        Assert.IsFalse(GroupExpansionPlacementPolicy.TryFind(new Rect(0,0,305,316),[area],[],out _));
    }

    [TestMethod]
    public void Placement_AvoidsActualOtherHostHeader_ButAllowsJoinedBoundary()
    {
        Rect host=new(704,180,352,150);
        Assert.IsTrue(GroupExpansionPlacementPolicy.TryFind(new Rect(704,184,304,316),[new Rect(0,0,1200,800)],[host],out Point position));
        Assert.IsFalse(new Rect(position,new Size(304,316)).IntersectsWith(host));
        var project=new Rect(704,0,352,180);
        Assert.IsTrue(GroupExpansionPlacementPolicy.TryFind(project,[new Rect(0,0,1200,800)],[host],out position));
        Assert.AreEqual(project.Location,position);
    }
    [STATestMethod]
    public void UnlockedGroup_MovingFromSmallToLargeMonitor_ValidatesFinalAutoFitSize()
    {
        var (window,layout,docs,work)=Fixture();
        docs.IsSizeLocked=false;
        docs.X=20;docs.Y=40;docs.Height=272;
        for(int i=0;i<9;i++) docs.ItemNames.Add($"test-{i}.txt");
        work.X=0;work.Y=0;work.Width=400;work.Height=300;
        var other=new GroupInfo {X=500,Y=300,Width=400,Height=140,IsSizeLocked=true};
        layout.Groups.Add(other);
        Set(window,"_desktopGeometry",new DesktopGeometry([
            new DesktopMonitorRegion {DeviceName="small",IsPrimary=true,Bounds=new Rect(0,0,400,300),WorkArea=new Rect(0,0,400,300)},
            new DesktopMonitorRegion {DeviceName="large",Bounds=new Rect(500,0,400,800),WorkArea=new Rect(500,0,400,800)}]));
        Assert.AreEqual(true,Invoke(window,"TryExpandGroupWithoutOverlap",docs));
        Invoke(window,"AutoFitGroup",docs,false);
        Assert.IsFalse(Bounds(docs).IntersectsWith(Bounds(other)), "Rebuild on the larger monitor must not grow into an obstacle.");
        Assert.IsTrue(new Rect(500,0,400,800).Contains(Bounds(docs)));
    }
    private static (MainWindow,AppLayoutData,GroupInfo,GroupInfo) Fixture()
    {
        var window=new MainWindow(startQuietly:false);
        Set(window,"_layoutWriteProtected",true);
        Set(window,"_desktopSnapshotInitialized",true);
        var layout=Field<AppLayoutData>(window,"_appLayout");
        layout.Groups.Clear();layout.FreeIcons.Clear();layout.FolderPortals.Clear();layout.RecycleBinWidget.IsVisible=false;
        window.ControlPanel.Visibility=Visibility.Collapsed;window.RecycleBinWidget.Visibility=Visibility.Collapsed;
        Set(window,"_desktopGeometry",new DesktopGeometry([new DesktopMonitorRegion {DeviceName="TEST",IsPrimary=true,Bounds=new Rect(0,0,1200,800),WorkArea=new Rect(0,0,1200,800)}]));
        var docs=new GroupInfo {Id="docs",Name="documents",X=704,Y=184,Width=304,Height=316,IsCollapsed=true,IsSizeLocked=true};
        var work=new GroupInfo {Id="work",Name="work",X=781,Y=301,Width=220,Height=140,IsSizeLocked=true};
        layout.Groups.Add(docs);layout.Groups.Add(work);
        return(window,layout,docs,work);
    }
    private static Rect Bounds(GroupInfo group)=>new(group.X,group.Y,group.Width,group.IsCollapsed?32:group.Height);
    private static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
    private static void Set(MainWindow window,string name,object value)=>typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,value);
    private static object? Invoke(MainWindow window,string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,args);
}
