using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopFileClipboardTests
{
    [STATestMethod]
    public void CopyData_UsesExplorerFileDropFormat_AndExplicitCopyEffect()
    {
        string[] paths = [@"D:\桌面\报告.txt", @"D:\桌面\文件夹", @"C:\Users\Public\Desktop\应用.lnk"];
        var data = DesktopFileClipboard.CreateCopyData(paths);
        CollectionAssert.AreEqual(paths, data.GetFileDropList().Cast<string>().ToArray());
        Assert.IsFalse(data.ContainsText(), "复制的是文件，而非路径文本。");
        using var effect = data.GetData("Preferred DropEffect") as MemoryStream;
        Assert.IsNotNull(effect);
        Assert.AreEqual(1, BitConverter.ToInt32(effect.ToArray()));
    }

    [STATestMethod]
    public void FreeAndGroupedMenus_CopyAllSelectedFiles_AndRightClickReplacesUnselectedTarget()
    {
        using var f = new Fixture();
        f.Items["A"] = f.Source;
        f.Items["B"] = f.Folder;
        f.Selected.UnionWith(["A", "B"]);
        foreach (GroupInfo? group in new GroupInfo?[] { null, new() { Name = "资料" } })
        {
            var menu = f.Window.CreateIconContextMenu(f.Source, "A", group);
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            var copy = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, "copy-files"));
            Assert.IsTrue(copy.IsEnabled);
            Assert.AreEqual("Ctrl+C", copy.InputGestureText);
            copy.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            CollectionAssert.AreEquivalent(new[] { f.Source, f.Folder }, f.Clipboard.Copied!);
        }
        f.Selected.Clear(); f.Selected.Add("B");
        var target = f.Window.CreateIconContextMenu(f.Source, "A", null);
        target.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        target.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, "copy-files"))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        CollectionAssert.AreEqual(new[] { f.Source }, f.Clipboard.Copied!);
        var folderMenu = f.Window.CreateIconContextMenu(f.Folder, "B", null);
        Assert.AreEqual("粘贴到此文件夹", folderMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, "paste-files")).Header);
    }

    [STATestMethod]
    public void MissingOrBusySource_DoesNotReplaceClipboard()
    {
        using var f = new Fixture();
        f.Items["A"] = f.Source; f.Selected.Add("A");
        f.Field<HashSet<string>>("_pendingFileOperationPaths").Add(f.Source);
        Assert.IsFalse(f.Window.CopySelectedDesktopFiles());
        Assert.IsNull(f.Clipboard.Copied);
        f.Field<HashSet<string>>("_pendingFileOperationPaths").Clear();
        File.Delete(f.Source);
        f.Window.CopySelectedDesktopFiles();
        Assert.IsNull(f.Clipboard.Copied);
        StringAssert.Contains(f.Window.StatusText.Text, "复制失败");
    }

    [STATestMethod]
    public void Paste_UsesCurrentUserDesktop_AndRunsShellOffUiOnSta()
    {
        using var f = new Fixture();
        f.Paste();
        Assert.AreEqual(f.Target, f.Clipboard.PastedTo);
        Assert.AreEqual(77u, f.Clipboard.ExpectedSequence);
        Assert.AreEqual(ApartmentState.STA, f.Clipboard.Apartment);
        Assert.AreNotEqual(Environment.CurrentManagedThreadId, f.Clipboard.ThreadId);
        Assert.AreEqual(0, f.Field<int>("_fileOperationCount"));
        Assert.IsEmpty(f.Field<HashSet<string>>("_pendingFileOperationPaths"));
        f.Paste(f.Folder);
        Assert.AreEqual(f.Folder, f.Clipboard.PastedTo);
    }

    [STATestMethod]
    public void Paste_FailureReleasesReservation_AndUnavailableDesktopNeverFallsBackToCommon()
    {
        using var f = new Fixture();
        f.Clipboard.Fail = true;
        f.Paste();
        Assert.AreEqual(0, f.Field<int>("_fileOperationCount"));
        Assert.IsEmpty(f.Field<HashSet<string>>("_pendingFileOperationPaths"));
        StringAssert.Contains(f.Window.StatusText.Text, "粘贴未完成");
        f.Clipboard.PastedTo = null;
        f.Set("_desktopPaths", new DesktopPathSnapshot(
            new(null, f.Target, false, "missing"), new(f.Folder, f.Folder, true, null), 2));
        f.Paste();
        Assert.IsNull(f.Clipboard.PastedTo);
    }

    [STATestMethod]
    public void Paste_DuplicatePendingCommandIsIgnored_AndTextEditorsKeepTheirShortcuts()
    {
        using var f = new Fixture();
        f.Set("_fileOperationCount", 1);
        f.Paste();
        Assert.IsNull(f.Clipboard.PastedTo);
        Assert.AreEqual(1, f.Field<int>("_fileOperationCount"));
        Assert.IsTrue(MainWindow.IsTextClipboardTarget(new TextBox()));
        Assert.IsTrue(MainWindow.IsTextClipboardTarget(new PasswordBox()));
        Assert.IsTrue(MainWindow.IsTextClipboardTarget(new ComboBox()));
        Assert.IsFalse(MainWindow.IsTextClipboardTarget(new Border()));
    }

    private sealed class FakeClipboard : IDesktopFileClipboard
    {
        public bool ContainsFiles => true;
        public uint Sequence => 77;
        public string[] Sources = [];
        public string[]? Copied;
        public string? PastedTo;
        public bool Fail;
        public uint ExpectedSequence;
        public int ThreadId;
        public ApartmentState Apartment;
        public string[] ReadFiles() => Sources;
        public void CopyFiles(string[] paths) => Copied = paths;
        public void Paste(string targetFolder, IntPtr owner, uint expectedSequence)
        {
            PastedTo = targetFolder; ExpectedSequence = expectedSequence;
            ThreadId = Environment.CurrentManagedThreadId; Apartment = Thread.CurrentThread.GetApartmentState();
            if (Fail) throw new IOException("模拟 Shell 失败");
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "DesktopClipboardTests", Guid.NewGuid().ToString("N"));
        public string Source { get; }
        public string Folder { get; }
        public string Target { get; }
        public MainWindow Window { get; }
        public FakeClipboard Clipboard { get; } = new();
        public Dictionary<string, string> Items => Field<Dictionary<string, string>>("_desktopItems");
        public HashSet<string> Selected => Field<HashSet<string>>("_selectedItemNames");
        public Fixture()
        {
            Directory.CreateDirectory(_root);
            Source = Path.Combine(_root, "source.txt"); File.WriteAllText(Source, "clipboard test");
            Folder = Directory.CreateDirectory(Path.Combine(_root, "folder")).FullName;
            Target = Directory.CreateDirectory(Path.Combine(_root, "redirected-desktop")).FullName;
            Clipboard.Sources = [Source, Folder];
            Window = new MainWindow(false, desktopFileClipboard: Clipboard);
            Set("_isSafeModeActive", true); // 不触发真实桌面扫描。
            Set("_desktopPaths", new DesktopPathSnapshot(new(Target, Target, true, null), new(Folder, Folder, true, null), 1));
        }
        public T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Window)!;
        public void Set(string name, object value) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Window, value);
        public void Paste(string? path = null)
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Window.Dispatcher));
            try
            {
                Task task = Window.PasteDesktopFilesAsync(path);
                var frame = new DispatcherFrame();
                var timeout = DateTime.UtcNow.AddSeconds(5);
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow > timeout) frame.Continue = false; };
                timer.Start(); try { if (!task.IsCompleted) Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
                Assert.IsTrue(task.IsCompleted); task.GetAwaiter().GetResult();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
        public void Dispose()
        {
            Set("_isClosing", true);
            Field<FileOperationService>("_fileOperationService").Dispose();
            Field<CancellationTokenSource>("_lifetimeCts").Cancel();
            // 仅删除当前测试创建的 GUID 临时目录。
            Directory.Delete(_root, true);
        }
    }
}
