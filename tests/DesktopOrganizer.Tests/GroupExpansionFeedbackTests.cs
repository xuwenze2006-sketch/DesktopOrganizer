using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class GroupExpansionFeedbackTests
{
    [STATestMethod]
    public void FailureTooltip_DoesNotRetainPreviousFailureAfterStatusChanges()
    {
        var window=new MainWindow(startQuietly:false);
        typeof(MainWindow).GetMethod("ReportGroupExpansionNoSpace",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,["documents"]);
        window.StatusText.Text="已完成后续操作";
        Assert.AreEqual(window.StatusText.Text,window.StatusText.ToolTip);
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
        type.GetMethod("ReportGroupExpansionNoSpace",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,["documents-long-name"]);
        string? tooltip=window.StatusText.ToolTip as string;
        Assert.IsNotNull(tooltip);
        Assert.AreEqual(window.StatusText.Text,tooltip);
        Assert.IsTrue(tooltip.Contains("documents-long-name"));
        Assert.IsTrue(tooltip.Contains("空间不足"));
        Assert.IsTrue(tooltip.Contains("退出编辑模式"));
        Assert.IsTrue(tooltip.Contains("未暂停"));
        Assert.IsTrue(tooltip.Contains("悬停"));
    }
}
