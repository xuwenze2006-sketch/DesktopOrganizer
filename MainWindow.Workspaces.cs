// 命名工作区与纯视觉布局快照
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private void WorkspaceManagerButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WorkspaceManagerWindow(this);
            if (_isAttachedToDesktop)
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            else
            {
                dialog.Owner = this;
            }
            dialog.ShowDialog();
        }

        internal IReadOnlyList<WorkspacePreview> GetWorkspacePreviews() =>
            WorkspaceLayoutManager.GetPreviews(_appLayout);

        internal string? GetActiveWorkspaceId() => _appLayout.ActiveWorkspaceId;

        internal bool TryCreateWorkspace(string name, out string error)
        {
            error = string.Empty;
            try
            {
                PrepareLayoutForPersistence();
                WorkspaceProfileInfo workspace = WorkspaceLayoutManager.CreateAndActivate(
                    _appLayout,
                    name,
                    DateTime.UtcNow);
                SaveLayout();
                StatusText.Text = $"已创建工作区“{workspace.Name}”";
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException)
            {
                error = exception.Message;
                return false;
            }
        }

        internal bool TryRenameWorkspace(string workspaceId, string name, out string error)
        {
            error = string.Empty;
            try
            {
                if (!WorkspaceLayoutManager.Rename(_appLayout, workspaceId, name))
                {
                    error = "工作区已不存在。";
                    return false;
                }
                SaveLayout();
                StatusText.Text = "工作区已重命名";
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException)
            {
                error = exception.Message;
                return false;
            }
        }

        internal bool TryOverwriteWorkspace(string workspaceId, out string error)
        {
            error = string.Empty;
            PrepareLayoutForPersistence();
            if (!WorkspaceLayoutManager.Overwrite(_appLayout, workspaceId, DateTime.UtcNow))
            {
                error = "工作区已不存在。";
                return false;
            }

            SaveLayout();
            StatusText.Text = "工作区快照已更新";
            return true;
        }

        internal bool TryDeleteWorkspace(string workspaceId, out string error)
        {
            error = string.Empty;
            if (!WorkspaceLayoutManager.Delete(_appLayout, workspaceId))
            {
                error = "工作区已不存在。";
                return false;
            }

            SaveLayout();
            StatusText.Text = "工作区已删除；当前桌面布局保持不变";
            return true;
        }

        internal bool TryActivateWorkspace(string workspaceId, out string error)
        {
            error = string.Empty;
            if (HasPendingFileOperations)
            {
                error = "真实文件任务正在进行，完成后才能切换工作区。";
                return false;
            }
            if (_autoClassifyInProgress)
            {
                error = "自动分类正在计算，完成后才能切换工作区。";
                return false;
            }
            if (_draggedElement != null)
            {
                error = "请先结束当前拖动，再切换工作区。";
                return false;
            }

            WorkspaceProfileInfo? target = _appLayout.Workspaces.FirstOrDefault(workspace =>
                workspace.Id.Equals(workspaceId, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                error = "工作区已不存在。";
                return false;
            }

            if (string.Equals(_appLayout.ActiveWorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = $"“{target.Name}”已经是当前工作区";
                return true;
            }

            CancelScheduledDesktopRefresh();
            CancelActiveDesktopRefresh();
            _pendingAutoClassificationCandidates.Clear();
            ResetAllInteractionState(restoreDraggedVisual: true);
            PrepareLayoutForPersistence();
            if (!WorkspaceLayoutManager.TryActivate(_appLayout, workspaceId, DateTime.UtcNow))
            {
                error = "工作区已不存在。";
                return false;
            }

            DesktopGeometry? savedGeometry = CreateGeometryFromPersistedTopology(
                _appLayout.DesktopTopology);
            if (savedGeometry != null && !savedGeometry.IsEquivalentTo(_desktopGeometry))
            {
                _ = RemapLayoutBetweenGeometries(savedGeometry, _desktopGeometry);
            }

            CaptureCurrentDesktopTopology();
            NormalizeLayout();
            _selectedItemNames.Clear();
            _lastSmartLayoutSnapshot = null;
            UndoSmartLayoutButton.IsEnabled = false;
            RebuildDesktopIcons();
            ApplyControlPanelPosition();
            RecycleBinWidgetToggle.IsChecked = _appLayout.RecycleBinWidget.IsVisible;
            ApplyRecycleBinWidgetPosition();
            UpdateRecycleBinWidgetVisibility();
            SaveLayout();
            StatusText.Text = $"已切换到工作区“{target.Name}”；未修改真实文件";
            RequestDesktopRefresh(clearIconCache: false, statusMessage: null);
            return true;
        }
    }
}
