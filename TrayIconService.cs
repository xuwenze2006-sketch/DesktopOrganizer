namespace DesktopOrganizer
{
    /// <summary>
    /// 轻量级原生通知区域图标，不引入第三方 NuGet，也不依赖 WinForms 消息循环。
    /// </summary>
    internal sealed class TrayIconService : IDisposable
    {
        internal const int CallbackMessage = 0x8001;
        private const uint IconId = 1;
        private const int WmLeftButtonDoubleClick = 0x0203;
        private const int WmRightButtonUp = 0x0205;
        private const int WmContextMenu = 0x007B;

        private readonly IntPtr _windowHandle;
        private readonly Func<bool> _isControlPanelVisible;
        private readonly Func<bool> _isOrganizerPaused;
        private readonly Func<bool> _isAutoStartEnabled;
        private readonly Action _toggleControlPanel;
        private readonly Action _togglePause;
        private readonly Action _refresh;
        private readonly Action _toggleAutoStart;
        private readonly Action _exit;
        private readonly Action _registrationFailed;
        private bool _disposed;

        public TrayIconService(
            IntPtr windowHandle,
            Func<bool> isControlPanelVisible,
            Func<bool> isOrganizerPaused,
            Func<bool> isAutoStartEnabled,
            Action toggleControlPanel,
            Action togglePause,
            Action refresh,
            Action toggleAutoStart,
            Action exit,
            Action registrationFailed)
        {
            _windowHandle = windowHandle;
            _isControlPanelVisible = isControlPanelVisible;
            _isOrganizerPaused = isOrganizerPaused;
            _isAutoStartEnabled = isAutoStartEnabled;
            _toggleControlPanel = toggleControlPanel;
            _togglePause = togglePause;
            _refresh = refresh;
            _toggleAutoStart = toggleAutoStart;
            _exit = exit;
            _registrationFailed = registrationFailed;
            TaskbarCreatedMessage = NativeMethods.GetTaskbarCreatedMessage();
        }

        public uint TaskbarCreatedMessage { get; }
        public bool IsRegistered { get; private set; }

        public bool Initialize(bool notifyOnFailure = false)
        {
            if (_disposed)
            {
                return false;
            }

            IsRegistered = NativeMethods.AddTrayIcon(
                _windowHandle,
                IconId,
                CallbackMessage,
                BuildToolTip());
            if (!IsRegistered && notifyOnFailure)
            {
                _registrationFailed();
            }

            return IsRegistered;
        }

        public void RefreshToolTip()
        {
            if (_disposed)
            {
                return;
            }

            if (!IsRegistered)
            {
                return;
            }

            IsRegistered = NativeMethods.UpdateTrayIcon(
                _windowHandle,
                IconId,
                CallbackMessage,
                BuildToolTip());
            if (!IsRegistered)
            {
                _registrationFailed();
            }
        }

        public bool HandleWindowMessage(int message, IntPtr lParam)
        {
            if (_disposed)
            {
                return false;
            }

            if ((uint)message == TaskbarCreatedMessage)
            {
                // Explorer 重启后通知区域会被清空，此时自动重新注册图标。
                Initialize(notifyOnFailure: true);
                return true;
            }

            if (message != CallbackMessage)
            {
                return false;
            }

            int mouseMessage = unchecked((int)lParam.ToInt64());
            switch (mouseMessage)
            {
                case WmLeftButtonDoubleClick:
                    _toggleControlPanel();
                    break;

                case WmRightButtonUp:
                case WmContextMenu:
                    ShowContextMenu();
                    break;
            }

            return true;
        }

        private void ShowContextMenu()
        {
            int command = NativeMethods.ShowTrayContextMenu(
                _windowHandle,
                _isControlPanelVisible(),
                _isOrganizerPaused(),
                _isAutoStartEnabled());

            switch (command)
            {
                case NativeMethods.TrayCommandTogglePanel:
                    _toggleControlPanel();
                    break;
                case NativeMethods.TrayCommandPauseResume:
                    _togglePause();
                    break;
                case NativeMethods.TrayCommandRefresh:
                    _refresh();
                    break;
                case NativeMethods.TrayCommandToggleAutoStart:
                    _toggleAutoStart();
                    break;
                case NativeMethods.TrayCommandExit:
                    _exit();
                    break;
            }
        }

        private string BuildToolTip()
        {
            return _isOrganizerPaused()
                ? "DesktopOrganizer（已暂停）"
                : "DesktopOrganizer（正在整理桌面）";
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (IsRegistered)
            {
                NativeMethods.RemoveTrayIcon(_windowHandle, IconId);
                IsRegistered = false;
            }
        }
    }
}
