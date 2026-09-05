using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class PhysicalFolderDropTests
{
    [TestMethod]
    [DataRow(10, true, false, false)]
    [DataRow(29.9, true, false, false)]
    [DataRow(30, true, false, true)]
    [DataRow(50, true, false, true)]
    [DataRow(69.9, true, false, true)]
    [DataRow(70, true, false, false)]
    [DataRow(89, true, false, false)]
    [DataRow(10, true, true, true)]
    [DataRow(89, true, true, true)]
    [DataRow(10, false, false, true)]
    [DataRow(89, false, false, true)]
    [DataRow(91, false, true, false)]
    public void HitTest_SeparatesFolderCenterFromInsertionEdges(
        double x, bool insideGroup, bool shift, bool expected)
    {
        Assert.AreEqual(expected, MainWindow.IsPhysicalFolderDropHit(
            new Rect(10, 20, 80, 70), new Point(x, 40), insideGroup, shift));
    }

    [STATestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Preview_CenterEdgesAndRepeatedUpdatesRemainMutuallyExclusive(bool sameGroup)
    {
        using var fixture = new PreviewFixture();
        fixture.Source.Tag = new IconTag
        {
            DisplayName = "source.txt", FullPath = @"C:\Test\source.txt",
            Group = sameGroup ? fixture.Group : new GroupInfo { Id = "other" }
        };

        fixture.Preview(new Point(50, 30));
        fixture.AssertFolderSelected();
        fixture.Preview(new Point(50, 30));
        fixture.AssertFolderSelected();

        fixture.Preview(new Point(5, 30));
        fixture.AssertInsertionSelected();
        fixture.Preview(new Point(95, 30));
        fixture.AssertInsertionSelected();
        fixture.Preview(new Point(150, 30)); // 普通文件中央仍是插入。
        fixture.AssertInsertionSelected();
        fixture.Preview(new Point(50, 30));
        fixture.AssertFolderSelected();

        fixture.Preview(new Point(350, 230));
        Assert.IsNull(fixture.Field("_activePhysicalFolderDropPath"));
        Assert.IsNull(fixture.Field("_activeGroupDropTarget"));
    }

    [STATestMethod]
    public void Preview_ClippedFolderCannotReceiveDropOutsideViewport()
    {
        using var fixture = new PreviewFixture();
        // 模拟缓存行位于裁剪视口以外，但其控件仍已实现。
        fixture.Panel.Height = 20;
        fixture.Panel.Measure(new Size(300, 20));
        fixture.Panel.Arrange(new Rect(0, 0, 300, 20));
        Assert.AreEqual(20, fixture.Panel.ActualHeight);
        fixture.Preview(new Point(50, 50));
        Assert.IsNull(fixture.Field("_activePhysicalFolderDropPath"));
    }

    [STATestMethod]
    public void Preview_SourceFolderAndShellItemsCannotBecomePhysicalMoves()
    {
        using var fixture = new PreviewFixture();
        fixture.Source.Tag = new IconTag { FullPath = PreviewFixture.FolderPath };
        fixture.Preview(new Point(50, 30));
        fixture.AssertInsertionSelected();
        fixture.Source.Tag = new IconTag { Kind = DesktopItemKind.ShellNamespace };
        fixture.Preview(new Point(50, 30));
        fixture.AssertInsertionSelected();
    }

    private sealed class PreviewFixture : IDisposable
    {
        public const string FolderPath = @"C:\Test\folder";
        private readonly MainWindow _window = new(startQuietly: false);
        private readonly OffscreenSource _source;
        public GroupInfo Group { get; } = new() { Id = "group", Name = "测试" };
        public Border Source { get; } = new() { Tag = new IconTag { FullPath = @"C:\Test\source.txt" } };
        public VirtualizingGroupPanel Panel { get; }

        public PreviewFixture()
        {
            // 仅离屏测量测试控件，不显示窗口、不访问或移动真实文件。
            _window.IconCanvas = new Canvas();
            _source = new OffscreenSource(_window.IconCanvas);
            Panel = new VirtualizingGroupPanel(
                [new("folder", FolderPath), new("file.txt", @"C:\Test\file.txt")],
                item =>
                {
                    var visual = new Border
                    {
                        Tag = new IconTag { FullPath = item.FullPath, DisplayName = item.DisplayName, Group = Group }
                    };
                    if (item.FullPath == FolderPath)
                    {
                        ((Dictionary<string, FrameworkElement>)Field("_physicalFolderDropTargets")!)[FolderPath] = visual;
                    }
                    return visual;
                }, _ => { }, 3, 100, 80, 0)
            { Width = 300, Height = 160, VerticalAlignment = VerticalAlignment.Top };
            var target = new Border { Width = 300, Height = 200, Child = Panel };
            _window.IconCanvas.Children.Add(target);
            ((AppLayoutData)Field("_appLayout")!).Groups.Add(Group);
            ((Dictionary<string, FrameworkElement>)Field("_groupDropTargets")!)[Group.Id] = target;
            ((Dictionary<string, VirtualizingGroupPanel>)Field("_groupItemPanels")!)[Group.Id] = Panel;
            Layout();
            Assert.IsTrue(target.IsVisible,
                $"离屏目标可见性：canvas={_window.IconCanvas.IsVisible}，target={target.IsVisible}，尺寸={target.RenderSize}，面板={Panel.RenderSize}");
            object[] boundsArguments = [target, Rect.Empty];
            bool hasBounds = (bool)typeof(MainWindow)
                .GetMethod("TryGetElementBoundsOnCanvas", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(_window, boundsArguments)!;
            Assert.IsTrue(hasBounds && ((Rect)boundsArguments[1]).Contains(new Point(50, 30)),
                $"目标边界：{boundsArguments[1]}，尺寸={target.RenderSize}，位置={target.TranslatePoint(new Point(), _window.IconCanvas)}");
        }

        public void Layout()
        {
            _window.IconCanvas.Measure(new Size(400, 300));
            _window.IconCanvas.Arrange(new Rect(0, 0, 400, 300));
        }

        public object? Field(string name) => typeof(MainWindow)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_window);

        public void Preview(Point point)
        {
            foreach (string method in new[] { "UpdateGroupDropPreview", "UpdatePhysicalFolderDropPreview" })
            {
                typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_window, [point, Source]);
            }
        }

        public void AssertFolderSelected()
        {
            Assert.AreEqual(FolderPath, Field("_activePhysicalFolderDropPath"));
            Assert.IsNull(Field("_activeGroupDropTarget"));
            Assert.IsFalse(Panel.Children.OfType<Border>().Any(child => !child.IsHitTestVisible));
            StringAssert.Contains(_window.StatusText.Text, "松开可移入真实文件夹");
        }

        public void AssertInsertionSelected()
        {
            Assert.IsNull(Field("_activePhysicalFolderDropPath"));
            Assert.AreSame(Group, Field("_activeGroupDropTarget"));
            Assert.IsTrue(Panel.Children.OfType<Border>().Any(child => !child.IsHitTestVisible));
        }

        public void Dispose()
        {
            ((DispatcherTimer)Field("_layoutSaveTimer")!).Stop();
            _source.Dispose();
        }
    }

    private sealed class OffscreenSource : PresentationSource, IDisposable
    {
        private readonly Visual _root;
        private bool _disposed;
        public OffscreenSource(Visual root)
        {
            _root = root;
            AddSource();
            RootChanged(null, root);
        }

        public override Visual RootVisual { get => _root; set => throw new NotSupportedException(); }
        public override bool IsDisposed => _disposed;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
        public void Dispose()
        {
            RootChanged(_root, null);
            RemoveSource();
            _disposed = true;
        }
    }
}
