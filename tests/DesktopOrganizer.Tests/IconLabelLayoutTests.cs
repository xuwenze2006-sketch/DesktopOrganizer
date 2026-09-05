using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class IconLabelLayoutTests
{
    [STATestMethod]
    [DataRow(true, 352, 1.0)]
    [DataRow(true, 352, 1.25)]
    [DataRow(true, 352, 1.5)]
    [DataRow(true, 352, 2.0)]
    [DataRow(true, 280, 1.5)]
    [DataRow(false, 280, 1.0)]
    [DataRow(false, 280, 1.25)]
    [DataRow(false, 280, 1.5)]
    [DataRow(false, 280, 2.0)]
    [DataRow(false, 0, 1.0)]
    [DataRow(false, 0, 1.25)]
    [DataRow(false, 0, 1.5)]
    [DataRow(false, 0, 2.0)]
    public void TwoLines_FitInsideTileAndRowWithSelectionOrDropBorder(
        bool compact, int groupWidth, double scale)
    {
        var window = new MainWindow(startQuietly: false);
        try
        {
            Field<AppLayoutData>(window, "_appLayout").CompactGroupLayout = compact;
            GroupInfo? group = groupWidth == 0 ? null : new GroupInfo { Width = groupWidth };
            var cache = Field<Dictionary<string, BitmapSource?>>(window, "_iconCache");
            BitmapSource icon = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 210, 140, 60, 255 }, 4);
            icon.Freeze();
            cache["ext:.txt"] = icon; // 使用缓存占位，禁止测试触发 Shell 图标加载。
            double rowHeight = group == null ? 90 : (double)Invoke(window, "GetGroupedIconRowHeight")!;
            var canvas = new Canvas { Background = Brushes.White, UseLayoutRounding = true };
            VisualTreeHelper.SetRootDpi(canvas, new DpiScale(scale, scale));
            int index = 0;
            foreach (string font in new[] { "Segoe UI", "Microsoft YaHei UI" })
            {
                foreach (double borderWidth in new[] { 0.0, 1.5, 2.0 })
                {
                    string name = $"sample-{index}.txt";
                    var tile = (Border)Invoke(window, "CreateIconVisual", @"C:\Test\" + name, name, group)!;
                    tile.BorderThickness = new Thickness(borderWidth);
                    tile.BorderBrush = Brushes.SeaGreen;
                    var content = (StackPanel)tile.Child;
                    var labelBackground = (Border)content.Children[1];
                    var label = (TextBlock)labelBackground.Child;
                    label.FontFamily = new FontFamily(font);
                    label.Text = "数学物理\n学习之旅";
                    Canvas.SetLeft(tile, index * 100);
                    canvas.Children.Add(tile);
                    index++;
                }
            }
            canvas.Measure(new Size(600, rowHeight));
            canvas.Arrange(new Rect(0, 0, 600, rowHeight));

            foreach (Border tile in canvas.Children)
            {
                var content = (StackPanel)tile.Child;
                var labelBackground = (Border)content.Children[1];
                var label = (TextBlock)labelBackground.Child;
                var reference = new TextBlock
                {
                    Text = label.Text, FontFamily = label.FontFamily, FontSize = label.FontSize,
                    LineHeight = label.LineHeight, LineStackingStrategy = label.LineStackingStrategy
                };
                VisualTreeHelper.SetRootDpi(reference, new DpiScale(scale, scale));
                TextOptions.SetTextFormattingMode(reference, TextFormattingMode.Display);
                reference.Measure(new Size(label.ActualWidth, double.PositiveInfinity));
                double bottom = label.TranslatePoint(new Point(0, label.ActualHeight), tile).Y;
                double availableBottom = tile.ActualHeight - tile.Padding.Bottom - tile.BorderThickness.Bottom;
                Assert.IsTrue(bottom <= availableBottom + 0.1,
                    $"两行文字越出图标格：bottom={bottom}, available={availableBottom}, dpi={scale}, border={tile.BorderThickness}");
                Assert.IsTrue(label.ActualHeight + 0.1 >= reference.DesiredSize.Height,
                    $"两行文字被自身高度裁剪：actual={label.ActualHeight}, required={reference.DesiredSize.Height}");
                Assert.IsTrue(tile.Height + tile.Margin.Top + tile.Margin.Bottom <= rowHeight,
                    "图标格不能覆盖下一行。");
                Assert.AreEqual(42, ((FrameworkElement)content.Children[0]).Height);
                Assert.AreEqual(11, label.FontSize);
            }

            if (Environment.GetEnvironmentVariable("DESKTOPORGANIZER_RENDER_LABEL_QA") == "1")
            {
                string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
                string output = Path.Combine(root, "artifacts", "icon-label-layout");
                Directory.CreateDirectory(output);
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(600 * scale),
                    (int)Math.Ceiling(rowHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(canvas);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(output, $"labels-{compact}-{groupWidth}-{scale}.png"));
                encoder.Save(stream);
            }
        }
        finally
        {
            Field<DispatcherTimer>(window, "_layoutSaveTimer").Stop();
        }
    }

    private static T Field<T>(MainWindow window, string name) =>
        (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static object? Invoke(MainWindow window, string name, params object?[] arguments) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
}
