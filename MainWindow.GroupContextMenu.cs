namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private ContextMenu? _openGroupContextMenu;
        private long _groupContextMenuGeneration;

        private void TrackGroupContextMenu(ContextMenu menu)
        {
            menu.Opened += (_, _) =>
            {
                _openGroupContextMenu = menu;
                _groupContextMenuGeneration++;
            };
            menu.Closed += (_, _) =>
            {
                if (ReferenceEquals(_openGroupContextMenu, menu))
                {
                    _openGroupContextMenu = null;
                    _groupContextMenuGeneration++;
                }
            };
        }

        private void DismissGroupMenuFromNativePointer(IntPtr clickedWindow)
        {
            DismissLightDesktopDrawerFromNativePointer(clickedWindow);
            // 现有低级鼠标钩子在安装它的 UI 线程回调。不能依赖透明桌面的
            // WPF MouseDown 或 Deactivated；也不能在原生钩子里同步拆除 Popup。
            ContextMenu? menu = _openGroupContextMenu;
            if (_isClosing || menu == null ||
                IsGroupMenuWindow(menu, clickedWindow,
                    visual => (PresentationSource.FromVisual(visual) as HwndSource)?.Handle ?? IntPtr.Zero))
            {
                return;
            }

            long generation = _groupContextMenuGeneration;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                // 原点击之后可能已经切换或重新打开菜单，旧回调不能关闭新菜单。
                if (!_isClosing && ReferenceEquals(_openGroupContextMenu, menu) &&
                    generation == _groupContextMenuGeneration)
                {
                    menu.SetCurrentValue(ContextMenu.IsOpenProperty, false);
                }
            }));
        }

        internal static bool IsGroupMenuWindow(
            ItemsControl menu, IntPtr clickedWindow, Func<Visual, IntPtr> getWindow)
        {
            if (clickedWindow == IntPtr.Zero)
            {
                return false;
            }
            if (getWindow(menu) == clickedWindow)
            {
                return true;
            }

            // 子菜单有独立 HWND；仅检查顶层菜单会误把“排序”子菜单当成外部点击。
            for (int index = 0; index < menu.Items.Count; index++)
            {
                if ((menu.ItemContainerGenerator.ContainerFromIndex(index) ?? menu.Items[index])
                    is not MenuItem item)
                {
                    continue;
                }
                if (IsGroupMenuWindow(item, clickedWindow, getWindow))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
