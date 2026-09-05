using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class GroupContextMenuTests
{
    [STATestMethod]
    public void ActualGroupHeaderMenu_RegistersAndClearsDismissalTarget()
    {
        var window = new MainWindow(startQuietly: false);
        var group = new GroupInfo { Width = 280, Height = 200 };
        var container = (Border)Invoke(window, "CreateGroupVisual", group,
            new Dictionary<string, string>())!;
        var header = (Border)((Grid)container.Child).Children[0];
        ContextMenu menu = header.ContextMenu;

        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        Assert.AreSame(menu, GetOpenMenu(window));
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        Assert.IsNull(GetOpenMenu(window));
    }

    [STATestMethod]
    [DataRow(10, true)] // 主菜单
    [DataRow(20, true)] // 排序子菜单
    [DataRow(30, true)] // 更深一级子菜单
    [DataRow(99, false)] // Explorer 桌面或其他窗口
    [DataRow(0, false)] // 未命中窗口
    public void MenuWindowDetection_IncludesSubmenus(int target, bool expected)
    {
        var menu = new ContextMenu();
        var sort = new MenuItem();
        var nested = new MenuItem();
        var command = new MenuItem();
        menu.Items.Add(sort);
        menu.Items.Add(new Separator());
        sort.Items.Add(nested);
        nested.Items.Add(command);
        var windows = new Dictionary<Visual, IntPtr>
        {
            [menu] = new(10), [sort] = new(10),
            [nested] = new(20), [command] = new(30)
        };

        Assert.AreEqual(expected, MainWindow.IsGroupMenuWindow(
            menu, new IntPtr(target), visual => windows.GetValueOrDefault(visual)));
    }

    [STATestMethod]
    public void OutsideClick_QueuesClose_WithoutOpeningAnyNativeWindow()
    {
        var window = new MainWindow(startQuietly: false);
        var menu = new NonOpeningMenu();
        Invoke(window, "TrackGroupContextMenu", menu);
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        int before = menu.CloseRequests;

        Invoke(window, "DismissGroupMenuFromNativePointer", new IntPtr(99));
        Assert.AreEqual(before, menu.CloseRequests, "不能在原生钩子回调内同步关闭菜单。");
        DrainInput(window.Dispatcher);

        Assert.AreEqual(before + 1, menu.CloseRequests);
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        Assert.IsNull(GetOpenMenu(window));
    }

    [STATestMethod]
    public void QueuedOldClick_DoesNotCloseReopenedMenu()
    {
        var window = new MainWindow(startQuietly: false);
        var menu = new NonOpeningMenu();
        Invoke(window, "TrackGroupContextMenu", menu);
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        Invoke(window, "DismissGroupMenuFromNativePointer", new IntPtr(99));
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        int before = menu.CloseRequests;

        DrainInput(window.Dispatcher);

        Assert.AreEqual(before, menu.CloseRequests);
        Assert.AreSame(menu, GetOpenMenu(window));
    }

    [STATestMethod]
    public void ClosingOldMenu_DoesNotClearNewMenuRegistration()
    {
        var window = new MainWindow(startQuietly: false);
        var oldMenu = new ContextMenu();
        var newMenu = new ContextMenu();
        Invoke(window, "TrackGroupContextMenu", oldMenu);
        Invoke(window, "TrackGroupContextMenu", newMenu);
        oldMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        newMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        oldMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));

        Assert.AreSame(newMenu, GetOpenMenu(window));
    }

    private static void DrainInput(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    // 记录真实 IsOpen 属性的关闭请求，但强制保持关闭；测试绝不创建 Popup 窗口。
    private sealed class NonOpeningMenu : ContextMenu
    {
        public int CloseRequests { get; private set; }
        static NonOpeningMenu()
        {
            IsOpenProperty.OverrideMetadata(typeof(NonOpeningMenu),
                new FrameworkPropertyMetadata(false, null, (owner, value) =>
                {
                    if (!(bool)value)
                    {
                        ((NonOpeningMenu)owner).CloseRequests++;
                    }
                    return false;
                }));
        }
    }

    private static object? GetOpenMenu(MainWindow window) => typeof(MainWindow)
        .GetField("_openGroupContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);

    private static object? Invoke(MainWindow window, string method, params object[] arguments) => typeof(MainWindow)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
}
