namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private bool _isRecycleBinDropTarget;
        private Brush? _recycleDropOriginalBackground;
        private Brush? _recycleDropOriginalBorder;

        private bool UpdateRecycleBinDropPreview(Point canvasPoint, FrameworkElement? dragged)
        {
            if (_organizerPaused || !_appLayout.RecycleBinWidget.IsVisible ||
                RecycleBinWidget.Visibility != Visibility.Visible ||
                !RecycleBinWidget.IsHitTestVisible || dragged?.Tag is not IconTag tag)
            {
                ClearRecycleBinDropPreview();
                return false;
            }

            Point rootPoint = IconCanvas.TranslatePoint(canvasPoint, RootGrid);
            if (!GetRecycleBinWidgetBounds().Contains(rootPoint) ||
                RootGrid.InputHitTest(rootPoint) is not DependencyObject hit ||
                !IsDescendantOf(hit, RecycleBinWidget))
            {
                // 只有光标真的落在小组件上才接收；上层控制面板遮住时不能穿透投放。
                ClearRecycleBinDropPreview();
                return false;
            }

            // 小组件在分类框之上；回收站命中时不再插入、归组或挤压其它图标。
            ClearGroupDropPreview();
            ClearPhysicalFolderDropPreview();
            CancelPushPreview(restoreVisuals: true);
            if (!_isRecycleBinDropTarget)
            {
                _recycleDropOriginalBackground = RecycleBinWidget.Background;
                _recycleDropOriginalBorder = RecycleBinWidget.BorderBrush;
                _isRecycleBinDropTarget = true;
            }

            string? rejection = GetRecycleBinDropRejection(tag);
            RecycleBinWidget.Background = rejection == null
                ? FolderDropHighlightBrush : WarmPaperTheme.WarmHoverBrush;
            RecycleBinWidget.BorderBrush = rejection == null
                ? FolderDropBorderBrush : _recycleDropOriginalBorder;
            StatusText.Text = rejection ?? $"松开可将“{tag.DisplayName}”移到回收站（需确认）";
            // 不可删除的系统项目也必须拦住，不能退化为投放到桌面空白处。
            return true;
        }

        private string? GetRecycleBinDropRejection(IconTag tag)
        {
            if (tag.Kind == DesktopItemKind.ShellNamespace ||
                ShellItemLocation.TryDecode(tag.FullPath, out _, out _))
            {
                return $"“{tag.DisplayName}”是 Windows 系统项目，不能放入回收站";
            }
            return IsFileOperationPending(tag.FullPath)
                ? $"“{tag.DisplayName}”已有文件操作正在进行，暂不能放入回收站"
                : null;
        }

        private void ClearRecycleBinDropPreview()
        {
            if (!_isRecycleBinDropTarget)
            {
                return;
            }
            RecycleBinWidget.Background = _recycleDropOriginalBackground;
            RecycleBinWidget.BorderBrush = _recycleDropOriginalBorder;
            _recycleDropOriginalBackground = null;
            _recycleDropOriginalBorder = null;
            _isRecycleBinDropTarget = false;
        }

        private bool TryCompleteRecycleBinIconDrop(
            Point canvasPoint, FrameworkElement dragged, Action<string, string>? recycle = null)
        {
            if (!UpdateRecycleBinDropPreview(canvasPoint, dragged))
            {
                return false;
            }
            CompleteRecycleBinIconDrop(dragged, recycle);
            return true;
        }

        private void CompleteRecycleBinIconDrop(
            FrameworkElement dragged, Action<string, string>? recycle = null)
        {
            IconTag? tag = dragged.Tag as IconTag;
            string? rejection = tag == null ? "未识别到可回收的桌面项目" : GetRecycleBinDropRejection(tag);

            // 先恢复自由图标/原分类并彻底释放拖动捕获，再弹确认框。
            // 取消、身份校验失败或排队失败时，原布局均不改变。
            ResetAllInteractionState(restoreDraggedVisual: true);
            if (rejection != null || tag == null)
            {
                StatusText.Text = rejection;
                return;
            }
            // 与现有单图标拖动一致，只处理被拖项目，不扩展到其它选中项。
            (recycle ?? MoveItemToRecycleBin)(tag.FullPath, tag.DisplayName);
        }
    }
}
