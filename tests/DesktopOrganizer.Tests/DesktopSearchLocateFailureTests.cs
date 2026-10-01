using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopSearchLocateFailureTests
{
    [STATestMethod]
    [DataRow("group")]
    [DataRow("free")]
    [DataRow("entry")]
    public void PausedLocate_DoesNotChangeDesktopState(string kind)
    {
        var (main,layout,group)=Fixture();
        if(kind=="free") { group.ItemNames.Clear(); layout.FreeIcons["target.txt"]=new IconPosition {X=600,Y=100}; }
        if(kind=="entry") { group.DesktopRole=DesktopZoneRole.Other; group.IsSizeLocked=false; }
        Set(main,"_organizerPaused",true);
        main.IconCanvas.Visibility=Visibility.Collapsed;
        var selected=Field<HashSet<string>>(main,"_selectedItemNames"); selected.Add("previous.txt");
        var anchor=new GroupRangeSelectionAnchor("previous","previous.txt"); Set(main,"_groupRangeSelectionAnchor",anchor);
        layout.GroupScrollOffsets[group.Id]=160;
        var timer=Field<DispatcherTimer>(main,"_layoutSaveTimer"); timer.Stop();
        object? result=Invoke(main,"LocateDesktopSearchResult","target.txt");
        Assert.AreEqual(false,result);
        Assert.IsTrue(Field<bool>(main,"_organizerPaused"));
        Assert.AreEqual(Visibility.Collapsed,main.IconCanvas.Visibility);
        Assert.IsTrue(group.IsCollapsed); Assert.AreEqual(20d,group.X); Assert.AreEqual(20d,group.Y);
        Assert.IsTrue(selected.SetEquals(["previous.txt"]));
        Assert.AreSame(anchor,Field<GroupRangeSelectionAnchor>(main,"_groupRangeSelectionAnchor"));
        Assert.AreEqual(160d,layout.GroupScrollOffsets[group.Id]);
        Assert.IsFalse(timer.IsEnabled); Assert.AreEqual(0,Field<Dictionary<string,VirtualizingGroupPanel>>(main,"_groupItemPanels").Count);
    }

    [STATestMethod]
    [DataRow("button")]
    [DataRow("enter")]
    [DataRow("doubleClick")]
    public void PausedSearch_AllLocateRoutesPreserveResultsAndExplainRecovery(string route)
    {
        var (main,_,group)=Fixture(); Set(main,"_organizerPaused",true);
        var search=new DesktopSearchWindow(main); search.QueryBox.Text="target";
        object? selected=search.ResultsList.SelectedItem; bool closed=false; search.Closed+=(_,_)=>closed=true;
        ExecuteRoute(search,route);
        Assert.IsFalse(closed); Assert.AreEqual("target",search.QueryBox.Text); Assert.AreSame(selected,search.ResultsList.SelectedItem);
        Assert.IsTrue(group.IsCollapsed); Assert.IsTrue(Field<bool>(main,"_organizerPaused"));
        var feedback=(TextBlock)search.FindName("LocateFeedbackText");
        var card=(Border)search.FindName("LocateFeedbackCard");
        Assert.IsNotNull(feedback); Assert.AreEqual(Visibility.Visible,card.Visibility);
        Assert.IsTrue(feedback.Text.Contains("暂停")); Assert.IsTrue(feedback.Text.Contains("继续"));
        Assert.AreEqual(TextWrapping.Wrap,feedback.TextWrapping); Assert.IsTrue(card.Padding.Top>=10);
        Assert.IsTrue(search.ResultActionPanel.IsEnabled);
    }

    [STATestMethod]
    [DataRow("space")]
    [DataRow("missing")]
    public void FailedLocate_PreservesQueryAndSelection(string reason)
    {
        var (main,layout,_)=Fixture();
        var search=new DesktopSearchWindow(main); search.QueryBox.Text="target";
        object? selected=search.ResultsList.SelectedItem;
        if(reason=="space") layout.Groups.Add(new GroupInfo {Id="obstacle",X=0,Y=0,Width=800,Height=600,IsSizeLocked=true});
        else Field<Dictionary<string,string>>(main,"_desktopItems").Clear();
        bool closed=false; search.Closed+=(_,_)=>closed=true;
        ExecuteRoute(search,"button");
        Assert.IsFalse(closed); Assert.AreEqual("target",search.QueryBox.Text); Assert.AreSame(selected,search.ResultsList.SelectedItem);
        var feedback=(TextBlock)search.FindName("LocateFeedbackText"); Assert.IsNotNull(feedback);
        Assert.IsTrue(feedback.Text.Contains(reason=="space"?"空间不足":"已不在"));
    }

    [STATestMethod]
    [DataRow("button")]
    [DataRow("enter")]
    [DataRow("doubleClick")]
    public void LocateAfterPauseClears_SucceedsAndClosesSearch(string route)
    {
        var (main,_,group)=Fixture(); Set(main,"_organizerPaused",true);
        var search=new DesktopSearchWindow(main); search.QueryBox.Text="target";
        bool closed=false; search.Closed+=(_,_)=>closed=true;
        ExecuteRoute(search,"button"); Assert.IsFalse(closed);
        // Only change isolated test state; do not invoke native desktop pause/resume.
        Set(main,"_organizerPaused",false);
        ExecuteRoute(search,route);
        Assert.IsTrue(closed); Assert.IsFalse(group.IsCollapsed);
        Assert.IsTrue(Field<HashSet<string>>(main,"_selectedItemNames").SetEquals(["target.txt"]));
        Field<DispatcherTimer>(main,"_layoutSaveTimer").Stop();
    }

    [STATestMethod]
    public void ChangingQuery_ClearsObsoleteFailureFeedback()
    {
        var (main,_,_)=Fixture(); Set(main,"_organizerPaused",true);
        var search=new DesktopSearchWindow(main); ExecuteRoute(search,"button");
        var card=(Border)search.FindName("LocateFeedbackCard"); Assert.IsNotNull(card);
        Assert.AreEqual(Visibility.Visible,card.Visibility);
        search.QueryBox.Text="missing";
        Assert.AreEqual(Visibility.Collapsed,card.Visibility);
    }

    [STATestMethod]
    [DataRow(760d,520d,1d)]
    [DataRow(620d,420d,1d)]
    [DataRow(620d,420d,2d)]
    public void PausedFeedback_WrapsWithoutHidingResultsOrActions(double width,double height,double scale)
    {
        var (main,_,_)=Fixture(); Set(main,"_organizerPaused",true);
        var search=new DesktopSearchWindow(main); ExecuteRoute(search,"button");
        var root=(Grid)search.Content; search.Content=null; root.Background=Brushes.White;
        root.Measure(new Size(width-32,height-32)); root.Arrange(new Rect(0,0,width-32,height-32)); root.UpdateLayout();
        var card=(Border)search.FindName("LocateFeedbackCard");
        var feedback=(TextBlock)search.FindName("LocateFeedbackText");
        Assert.IsTrue(feedback.ActualHeight>=38,"Recovery instructions must wrap to readable lines.");
        Assert.IsTrue(card.ActualWidth<=width-32);
        Assert.IsTrue(search.ResultsList.ActualHeight>=80,"Keep a usable search result area at minimum size.");
        Assert.IsTrue(search.ResultActionPanel.ActualHeight>0);
        Assert.IsTrue(search.ResultActionPanel.TranslatePoint(new Point(0,search.ResultActionPanel.ActualHeight),root).Y<=root.ActualHeight);
        if(Environment.GetEnvironmentVariable("DESKTOPORGANIZER_RENDER_SEARCH_QA")=="1")
        {
            string directory=Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","artifacts","search-locate"); Directory.CreateDirectory(directory);
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling((root.ActualWidth+root.Margin.Left+root.Margin.Right)*scale),(int)Math.Ceiling((root.ActualHeight+root.Margin.Top+root.Margin.Bottom)*scale),96*scale,96*scale,PixelFormats.Pbgra32); bitmap.Render(root);
            var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream=File.Create(Path.Combine(directory,$"search-paused-{width}-{scale}.png")); encoder.Save(stream);
        }
    }
    private static void ExecuteRoute(DesktopSearchWindow search,string route)
    {
        if(route=="button") Invoke(search,"Locate_Click",search,new RoutedEventArgs());
        else if(route=="enter")
        {
            var e=new KeyEventArgs(Keyboard.PrimaryDevice,new TestPresentationSource(),0,Key.Enter) {RoutedEvent=Keyboard.PreviewKeyDownEvent};
            Invoke(search,"ResultsList_PreviewKeyDown",search.ResultsList,e); Assert.IsTrue(e.Handled);
        }
        else
        {
            ((FrameworkElement)search.Content).Measure(new Size(760,520));
            ((FrameworkElement)search.Content).Arrange(new Rect(0,0,760,520)); search.ResultsList.UpdateLayout();
            var item=(ListBoxItem)search.ResultsList.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.IsNotNull(item);
            var e=new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left) {RoutedEvent=Control.MouseDoubleClickEvent,Source=item};
            Invoke(search,"ResultsList_MouseDoubleClick",search.ResultsList,e); Assert.IsTrue(e.Handled);
        }
    }
    private sealed class TestPresentationSource:PresentationSource
    {
        public override Visual RootVisual {get;set;}=new DrawingVisual();
        public override bool IsDisposed=>false;
        protected override CompositionTarget GetCompositionTargetCore()=>null!;
    }
    private static (MainWindow,AppLayoutData,GroupInfo) Fixture()
    {
        var main=new MainWindow(startQuietly:false); Set(main,"_layoutWriteProtected",true);
        var items=Field<Dictionary<string,string>>(main,"_desktopItems"); items.Clear(); items["target.txt"]=@"C:\Test\target.txt";
        var layout=Field<AppLayoutData>(main,"_appLayout"); layout.Groups.Clear(); layout.FreeIcons.Clear(); layout.FolderPortals.Clear(); layout.RecycleBinWidget.IsVisible=false;
        main.ControlPanel.Visibility=Visibility.Collapsed;
        Set(main,"_desktopGeometry",new DesktopGeometry([new DesktopMonitorRegion {DeviceName="TEST",IsPrimary=true,Bounds=new Rect(0,0,800,600),WorkArea=new Rect(0,0,800,600)}]));
        var group=new GroupInfo {Id="group",Name="开发样例",X=20,Y=20,Width=300,Height=240,IsCollapsed=true,IsSizeLocked=true,ItemNames=["target.txt"]}; layout.Groups.Add(group);
        return(main,layout,group);
    }
    private static T Field<T>(object obj,string name)=>(T)obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(obj)!;
    private static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(obj,value);
    private static object? Invoke(object obj,string name,params object[] args)=>obj.GetType().GetMethods(BindingFlags.NonPublic|BindingFlags.Instance).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(obj,args);
}
