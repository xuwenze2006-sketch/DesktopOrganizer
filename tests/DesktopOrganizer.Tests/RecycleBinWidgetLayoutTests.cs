using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class RecycleBinWidgetLayoutTests
{
    [TestMethod]
    public void MissingWidgetField_UsesVisibleDefault()
    {
        const string json = "{\"Version\":14,\"FreeIcons\":{},\"Groups\":[]}";
        AppLayoutData layout = JsonSerializer.Deserialize<AppLayoutData>(json)
            ?? throw new AssertFailedException("布局 JSON 反序列化返回 null。");

        Assert.IsNotNull(layout.RecycleBinWidget);
        Assert.IsTrue(layout.RecycleBinWidget.IsVisible);
        Assert.IsFalse(layout.RecycleBinWidget.X.HasValue);
        Assert.IsFalse(layout.RecycleBinWidget.Y.HasValue);
    }

    [TestMethod]
    public void WidgetLayout_RoundTripsPositionAndVisibility()
    {
        var source = new AppLayoutData
        {
            RecycleBinWidget = new RecycleBinWidgetLayoutInfo
            {
                X = -240.5,
                Y = 720.25,
                IsVisible = false
            }
        };

        string json = JsonSerializer.Serialize(source);
        AppLayoutData restored = JsonSerializer.Deserialize<AppLayoutData>(json)
            ?? throw new AssertFailedException("布局 JSON 反序列化返回 null。");

        Assert.AreEqual(-240.5, restored.RecycleBinWidget.X.GetValueOrDefault());
        Assert.AreEqual(720.25, restored.RecycleBinWidget.Y.GetValueOrDefault());
        Assert.IsFalse(restored.RecycleBinWidget.IsVisible);
    }
}
