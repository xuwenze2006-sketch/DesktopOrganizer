namespace DesktopOrganizer
{
    public partial class WorkspaceManagerWindow : Window
    {
        private readonly MainWindow _mainWindow;

        private sealed record WorkspaceListItem(
            string Id,
            string Name,
            bool IsActive,
            int GroupCount,
            int FreeIconCount,
            int PortalCount,
            int MonitorCount,
            DateTime UpdatedUtc)
        {
            public string DisplayName => IsActive ? $"● {Name}" : Name;
        }

        internal WorkspaceManagerWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            InitializeComponent();
            RefreshList();
        }

        private WorkspaceListItem? Selected => WorkspaceList.SelectedItem as WorkspaceListItem;

        private void RefreshList(string? selectedId = null)
        {
            string? activeId = _mainWindow.GetActiveWorkspaceId();
            List<WorkspaceListItem> items = _mainWindow.GetWorkspacePreviews()
                .Select(preview => new WorkspaceListItem(
                    preview.Id,
                    preview.Name,
                    string.Equals(preview.Id, activeId, StringComparison.OrdinalIgnoreCase),
                    preview.GroupCount,
                    preview.FreeIconCount,
                    preview.PortalCount,
                    preview.MonitorCount,
                    preview.UpdatedUtc))
                .ToList();
            WorkspaceList.ItemsSource = items;
            WorkspaceList.SelectedItem = items.FirstOrDefault(item =>
                item.Id.Equals(selectedId ?? activeId, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
            UpdatePreview();
        }

        private void WorkspaceList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            UpdatePreview();

        private void WorkspaceList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left ||
                e.OriginalSource is not DependencyObject source ||
                ItemsControl.ContainerFromElement(WorkspaceList, source) is not ListBoxItem)
            {
                return;
            }

            e.Handled = true;
            Activate_Click(sender, e);
        }

        private void WorkspaceList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            WorkspaceListItem? item = Selected;
            if (ShouldRenameFromKeyboard(
                    e.Key,
                    Keyboard.Modifiers,
                    e.IsRepeat,
                    item != null))
            {
                e.Handled = true;
                Rename_Click(sender, e);
                return;
            }

            if (!ShouldActivateFromKeyboard(
                    e.Key,
                    Keyboard.Modifiers,
                    item != null,
                    item?.IsActive == true))
            {
                return;
            }

            e.Handled = true;
            Activate_Click(sender, e);
        }

        internal static bool ShouldActivateFromKeyboard(
            Key key,
            ModifierKeys modifiers,
            bool hasSelection,
            bool isActive) =>
            key == Key.Enter &&
            modifiers == ModifierKeys.None &&
            hasSelection &&
            !isActive;

        internal static bool ShouldRenameFromKeyboard(
            Key key,
            ModifierKeys modifiers,
            bool isRepeat,
            bool hasSelection) =>
            key == Key.F2 &&
            modifiers == ModifierKeys.None &&
            !isRepeat &&
            hasSelection;

        private void UpdatePreview()
        {
            WorkspaceListItem? item = Selected;
            string impactText = item?.IsActive == true
                ? "当前工作区；快照会在布局保存时更新。"
                : $"{FormatSwitchImpact(item == null
                    ? null
                    : _mainWindow.GetWorkspaceSwitchImpact(item.Id))}\n" +
                  "切换时仍会按当前显示器和现存桌面项目校正。";
            PreviewText.Text = item == null
                ? "尚未创建命名工作区。当前兼容布局仍会照常保存。"
                : $"名称：{item.Name}\n" +
                  $"状态：{(item.IsActive ? "当前工作区" : "可恢复快照")}\n" +
                  $"分组：{item.GroupCount}\n" +
                  $"自由图标坐标：{item.FreeIconCount}\n" +
                  $"只读文件夹入口：{item.PortalCount}\n" +
                  $"显示器拓扑：{item.MonitorCount} 个显示器\n" +
                  $"最近更新：{item.UpdatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n\n" +
                  impactText;
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            var input = new SimpleInputDialog("请输入工作区名称：", "工作") { Owner = this };
            if (input.ShowDialog() != true)
            {
                return;
            }

            if (!_mainWindow.TryCreateWorkspace(input.ResultText, out string error))
            {
                ShowError(error);
                return;
            }
            RefreshList(_mainWindow.GetActiveWorkspaceId());
        }

        private void Activate_Click(object sender, RoutedEventArgs e)
        {
            WorkspaceListItem? item = RequireSelection();
            if (item == null || item.IsActive)
            {
                return;
            }

            WorkspaceSwitchImpact? impact = _mainWindow.GetWorkspaceSwitchImpact(item.Id);
            if (impact == null)
            {
                ShowError("工作区已不存在。");
                RefreshList();
                return;
            }

            MessageBoxResult confirmation = MessageBox.Show(
                $"将恢复工作区“{item.Name}”的视觉布局。\n\n" +
                $"分组 {item.GroupCount} 个，自由图标坐标 {item.FreeIconCount} 个，" +
                $"只读文件夹入口 {item.PortalCount} 个，保存时显示器 {item.MonitorCount} 个。\n\n" +
                $"{FormatSwitchImpact(impact)}\n" +
                "切换时仍会按当前显示器和现存桌面项目校正。\n\n" +
                "此操作不会移动、重命名或删除任何真实文件。",
                "切换工作区",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);
            if (confirmation != MessageBoxResult.OK)
            {
                return;
            }

            if (!_mainWindow.TryActivateWorkspace(item.Id, out string error))
            {
                ShowError(error);
                return;
            }
            RefreshList(item.Id);
        }

        private void Duplicate_Click(object sender, RoutedEventArgs e)
        {
            WorkspaceListItem? item = RequireSelection();
            if (item == null)
            {
                return;
            }

            var input = new SimpleInputDialog(
                "请输入副本名称：",
                GetSuggestedDuplicateName(item.Name))
            {
                Owner = this
            };
            if (input.ShowDialog() != true)
            {
                return;
            }

            if (!_mainWindow.TryDuplicateWorkspace(
                    item.Id,
                    input.ResultText,
                    out string duplicateId,
                    out string error))
            {
                ShowError(error);
                return;
            }
            RefreshList(duplicateId);
        }

        private void Overwrite_Click(object sender, RoutedEventArgs e)
        {
            WorkspaceListItem? item = RequireSelection();
            if (item == null)
            {
                return;
            }

            WorkspaceSwitchImpact? impact = _mainWindow.GetWorkspaceSwitchImpact(item.Id);
            if (impact == null)
            {
                ShowError("工作区已不存在。");
                RefreshList();
                return;
            }

            if (MessageBox.Show(
                    $"使用当前屏幕上的视觉布局覆盖“{item.Name}”的已有快照？\n\n" +
                    $"{FormatSwitchImpact(impact)}\n\n不会修改真实文件。",
                    "覆盖工作区快照",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return;
            }

            if (!_mainWindow.TryOverwriteWorkspace(item.Id, out string error))
            {
                ShowError(error);
                return;
            }
            RefreshList(item.Id);
        }

        internal static string FormatSwitchImpact(WorkspaceSwitchImpact? impact)
        {
            if (impact == null)
            {
                return "无法读取该工作区的可见布局差异。";
            }
            if (!impact.HasVisibleChanges)
            {
                return "已保存的可见布局字段与当前布局一致。";
            }

            var parts = new List<string>();
            if (impact.ChangedGroupCount > 0)
            {
                parts.Add($"分组配置 {impact.ChangedGroupCount} 项");
            }
            if (impact.ChangedFreeIconCoordinateCount > 0)
            {
                parts.Add($"自由图标坐标 {impact.ChangedFreeIconCoordinateCount} 项");
            }
            if (impact.ChangedPortalCount > 0)
            {
                parts.Add($"只读文件夹入口 {impact.ChangedPortalCount} 项");
            }
            if (impact.ControlPanelChanged)
            {
                parts.Add("控制面板位置");
            }
            if (impact.RecycleBinWidgetChanged)
            {
                parts.Add("回收站组件");
            }
            if (impact.DesktopTopologyChanged)
            {
                parts.Add("显示器快照");
            }
            return $"与当前布局相比：{string.Join("、", parts)}不同。";
        }

        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            WorkspaceListItem? item = RequireSelection();
            if (item == null)
            {
                return;
            }
            var input = new SimpleInputDialog("请输入新的工作区名称：", item.Name) { Owner = this };
            if (input.ShowDialog() != true)
            {
                return;
            }
            if (!_mainWindow.TryRenameWorkspace(item.Id, input.ResultText, out string error))
            {
                ShowError(error);
                return;
            }
            RefreshList(item.Id);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            WorkspaceListItem? item = RequireSelection();
            if (item == null)
            {
                return;
            }
            if (MessageBox.Show(
                    $"删除工作区快照“{item.Name}”？\n\n当前屏幕布局和真实文件都不会改变。",
                    "删除工作区",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return;
            }
            if (!_mainWindow.TryDeleteWorkspace(item.Id, out string error))
            {
                ShowError(error);
                return;
            }
            RefreshList();
        }

        private WorkspaceListItem? RequireSelection()
        {
            WorkspaceListItem? item = Selected;
            if (item == null)
            {
                ShowError("请先选择一个工作区。");
            }
            return item;
        }

        private string GetSuggestedDuplicateName(string sourceName)
        {
            var existingNames = new HashSet<string>(
                _mainWindow.GetWorkspacePreviews().Select(preview => preview.Name),
                StringComparer.CurrentCultureIgnoreCase);
            string baseName = $"{sourceName} 副本";
            string candidate = baseName;
            int suffix = 2;
            while (existingNames.Contains(candidate))
            {
                candidate = $"{baseName} ({suffix++})";
            }
            return candidate;
        }

        private void ShowError(string message) => MessageBox.Show(
            this,
            message,
            "工作区操作未完成",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
