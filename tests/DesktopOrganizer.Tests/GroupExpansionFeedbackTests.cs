using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class GroupExpansionFeedbackTests
{
    [STATestMethod]
    public void FailureTooltip_DoesNotRetainPreviousFailureAfterStatusChanges()
    {
        var window=new MainWindow(startQuietly:false);
        Report(window,"documents");
        ToolTip tip=Tip(window);
        Grid content=(Grid)tip.Content;
        window.StatusText.Text="已完成后续操作";
        content.Measure(new Size(292,double.PositiveInfinity));
        Assert.AreEqual(Visibility.Collapsed,Details(tip).Visibility);
        TextBlock fallback=content.Children.OfType<TextBlock>().Single();
        Assert.AreEqual(Visibility.Visible,fallback.Visibility);
        Assert.AreEqual(window.StatusText.Text,fallback.Text);
    }

    [STATestMethod]
    [DataRow(false,false)]
    [DataRow(true,false)]
    [DataRow(true,true)]
    public void FailureTooltip_ContainsCompleteCauseAndPreviewConditions(bool editing,bool paused)
    {
        var window=new MainWindow(startQuietly:false);
        var type=typeof(MainWindow);
        var layout=(AppLayoutData)type.GetField("_appLayout",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
        layout.IsEditMode=editing;
        type.GetField("_organizerPaused",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,paused);
        Report(window,"documents-long-name");
        ToolTip tip=Tip(window);
        string text=string.Join("\n",Details(tip).Children.OfType<TextBlock>().Select(block=>block.Text));
        Assert.IsTrue(window.StatusText.Text.Length<=14,"Compact status must remain a short summary.");
        Assert.IsTrue(text.Contains("documents-long-name"));
        Assert.IsTrue(text.Contains("空间不足"));
        Assert.IsTrue(text.Contains("退出编辑模式"));
        Assert.IsTrue(text.Contains("未暂停"));
        Assert.IsTrue(text.Contains("悬停"));
        Assert.AreSame(window.ControlPanelDragHandle,tip.PlacementTarget);
        Assert.AreEqual(PlacementMode.Bottom,tip.Placement);
    }

    [STATestMethod]
    [DataRow(1d)]
    [DataRow(1.5d)]
    [DataRow(2d)]
    public void DetailCard_WrapsLongCauseAndKeepsReadableHierarchy(double scale)
    {
        var window=new MainWindow(startQuietly:false);
        Report(window,new string('长',80));
        ToolTip tip=Tip(window);
        var details=Details(tip);
        var blocks=details.Children.OfType<TextBlock>().ToArray();
        Assert.IsTrue(tip.Width<=320);
        Assert.IsTrue(blocks[0].FontSize>blocks[1].FontSize);
        Assert.AreEqual(TextWrapping.Wrap,blocks[1].TextWrapping);
        Grid content=(Grid)tip.Content;
        tip.Content=null;
        var frame=new Border {Width=tip.Width,Padding=new Thickness(14),CornerRadius=new CornerRadius(10),
            Background=(Brush)window.FindResource("PanelSurfaceBrush"),Child=content};
        frame.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty,new FontFamily("Microsoft YaHei UI"));
        frame.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,window.FindResource("PrimaryTextBrush"));
        frame.Measure(new Size(tip.Width,double.PositiveInfinity));
        frame.Arrange(new Rect(new Point(),frame.DesiredSize));
        Assert.IsTrue(blocks[1].ActualHeight>40,"Long causes should use multiple lines.");
        Assert.IsTrue(frame.ActualWidth<=320);
        if(Environment.GetEnvironmentVariable("DESKTOPORGANIZER_RENDER_STATUS_QA")=="1")
        {
            string directory=Path.Combine(TestProjectFiles.Root,"artifacts/status-ui");
            Directory.CreateDirectory(directory);
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(frame.ActualWidth*scale),(int)Math.Ceiling(frame.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);
            bitmap.Render(frame);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream=File.Create(Path.Combine(directory,$"detail-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png"));
            encoder.Save(stream);
        }
    }

    [STATestMethod]
    public void DetailCard_RechecksWidthWhenOpenedOnAnotherMonitor()
    {
        var window=new MainWindow(startQuietly:false);
        Report(window,"documents");
        ToolTip tip=Tip(window);
        typeof(MainWindow).GetField("_desktopGeometry",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,
            new DesktopGeometry([new DesktopMonitorRegion {DeviceName="narrow",IsPrimary=true,Bounds=new Rect(0,0,240,800),WorkArea=new Rect(0,0,240,800)}]));
        tip.RaiseEvent(new RoutedEventArgs(ToolTip.OpenedEvent));
        Assert.IsTrue(tip.Width<=216,"Details must be limited by the monitor used when opening, not an old monitor.");
    }

    [STATestMethod]
    public void CompactToolbar_LeavesRoomForStatusAndKeyActionsWithoutShowingDesktop()
    {
        var window=new MainWindow(startQuietly:false);
        window.Content=null;
        Report(window,"documents");
        var panel=window.ControlPanel;
        panel.Visibility=Visibility.Visible;
        panel.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        panel.Arrange(new Rect(new Point(),panel.DesiredSize));
        Assert.IsTrue(panel.ActualWidth<=340);
        Assert.IsTrue(window.StatusText.FontSize>=12.5);
        Assert.AreEqual(Visibility.Visible,window.QuickPauseButton.Visibility);
        Assert.AreEqual(Visibility.Visible,window.PanelExpanderButton.Visibility);
        if(Environment.GetEnvironmentVariable("DESKTOPORGANIZER_RENDER_STATUS_QA")=="1")
        {
            string directory=Path.Combine(TestProjectFiles.Root,"artifacts/status-ui");Directory.CreateDirectory(directory);
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth),(int)Math.Ceiling(panel.ActualHeight),96,96,PixelFormats.Pbgra32);
            bitmap.Render(panel);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream=File.Create(Path.Combine(directory,"compact-toolbar.png"));encoder.Save(stream);
        }
    }
    private static void Report(MainWindow window,string name)=>typeof(MainWindow)
        .GetMethod("ReportGroupExpansionNoSpace",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,[name]);
    private static ToolTip Tip(MainWindow window)
    {
        Assert.IsInstanceOfType<ToolTip>(window.StatusText.ToolTip);
        return (ToolTip)window.StatusText.ToolTip;
    }
    private static StackPanel Details(ToolTip tip)=>((Grid)tip.Content).Children.OfType<StackPanel>().Single();
}
