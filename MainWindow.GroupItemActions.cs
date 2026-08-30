// 分组图标拖拽与文件操作
// 本文件由 v1.12 完整功能重构拆分；行为逻辑保持自 v1.11.6 不变。
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private void GroupedIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                ClearPendingIconDrag();
                OpenItem(element);
                e.Handled = true;
                return;
            }

            if (HandleControlClickSelection(element, e))
            {
                return;
            }

            string clickedName = GetIconDisplayName(element);
            if (_selectedItemNames.Count > 0 && !_selectedItemNames.Contains(clickedName))
            {
                ClearItemSelection();
            }

            _pendingIconDragElement = element;
            _pendingIconMouseDownCanvasPoint = e.GetPosition(IconCanvas);
            _dragStartOffset = e.GetPosition(element);
            e.Handled = true;
        }

        private void GroupedIcon_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            if (_draggedElement == null)
            {
                if (_pendingIconDragElement == null ||
                    !ReferenceEquals(element, _pendingIconDragElement) ||
                    e.LeftButton != MouseButtonState.Pressed)
                {
                    return;
                }

                Point current = e.GetPosition(IconCanvas);
                if (Math.Abs(current.X - _pendingIconMouseDownCanvasPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(current.Y - _pendingIconMouseDownCanvasPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                if (!StartGroupedIconDrag(element))
                {
                    ClearPendingIconDrag();
                    return;
                }
            }

            if (!ReferenceEquals(_draggedElement, element) || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point canvasPosition = e.GetPosition(IconCanvas);
            double left = canvasPosition.X - _dragStartOffset.X;
            double top = canvasPosition.Y - _dragStartOffset.Y;
            ClampIconCoordinates(ref left, ref top);
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
            if (IsPointOverFolderPortal(canvasPosition) ||
                IntersectsFolderPortal(new Rect(left, top, IconCellWidth, IconCellHeight)))
            {
                ClearPhysicalFolderDropPreview();
                ClearGroupDropPreview();
                StatusText.Text = "真实文件夹入口为只读，不接收拖放";
                e.Handled = true;
                return;
            }
            UpdateGroupDropPreview(canvasPosition, element);
            UpdatePhysicalFolderDropPreview(canvasPosition, element);
            e.Handled = true;
        }

        private bool StartGroupedIconDrag(FrameworkElement element)
        {
            if (element.Tag is not IconTag tag || tag.Group == null ||
                VisualTreeHelper.GetParent(element) is not Panel sourcePanel)
            {
                return false;
            }

            Point topLeft;
            try
            {
                topLeft = element.TranslatePoint(new Point(0, 0), IconCanvas);
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            _groupedIconDragSourceGroup = tag.Group;
            bool detached;
            if (sourcePanel is VirtualizingGroupPanel virtualizingPanel)
            {
                detached = virtualizingPanel.DetachForDrag(element);
            }
            else if (sourcePanel.Children.Contains(element))
            {
                sourcePanel.Children.Remove(element);
                detached = true;
            }
            else
            {
                detached = false;
            }

            if (!detached)
            {
                _groupedIconDragSourceGroup = null;
                return false;
            }

            IconCanvas.Children.Add(element);
            Canvas.SetLeft(element, topLeft.X);
            Canvas.SetTop(element, topLeft.Y);
            Panel.SetZIndex(element, 700);
            _draggedElement = element;
            _draggedIsGroup = false;
            _draggedGroup = null;
            _pendingIconDragElement = null;
            _dragAllowsLayoutMove = true;
            _dragOriginalPosition = null;
            if (!Mouse.Capture(element, CaptureMode.Element))
            {
                ClearPhysicalFolderDropPreview();
                ClearGroupDropPreview();
                _draggedElement = null;
                _groupedIconDragSourceGroup = null;
                _dragOriginalPosition = null;
                _dragAllowsLayoutMove = false;
                RebuildDesktopIcons();
                return false;
            }

            StatusText.Text = $"可拖入其它分类框或在当前框排序；拖到空白处可移出“{tag.Group.Name}”；按住 Shift 可移入真实文件夹";
            return true;
        }

        private void GroupedIcon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggedElement == null)
            {
                if (ReferenceEquals(sender, _pendingIconDragElement))
                {
                    ClearPendingIconDrag();
                    e.Handled = true;
                }
                return;
            }

            if (ReferenceEquals(sender, _draggedElement))
            {
                Icon_MouseLeftButtonUp(sender, e);
            }
        }

        private void GroupedIcon_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isCompletingIconDrop ||
                _draggedElement == null ||
                !ReferenceEquals(sender, _draggedElement) ||
                _groupedIconDragSourceGroup == null)
            {
                return;
            }

            ClearPhysicalFolderDropPreview();
            ClearGroupDropPreview();
            _draggedElement = null;
            _groupedIconDragSourceGroup = null;
            _dragOriginalPosition = null;
            _dragAllowsLayoutMove = false;
            ClearPendingIconDrag();
            RebuildDesktopIcons();
            StatusText.Text = "拖动已取消，图标已回到原分类";
        }

        private void RemoveFromGroup(GroupInfo group, string name)
        {
            if (_desktopItems.TryGetValue(name, out string? location) &&
                !ShellItemLocation.TryDecode(location, out _, out _) &&
                IsFileOperationPending(location))
            {
                StatusText.Text = $"“{name}”的真实文件操作正在进行，暂不能修改分类";
                return;
            }

            var position = new IconPosition
            {
                X = group.X + group.Width + 12,
                Y = group.Y
            };
            ClampIconPosition(position);
            if (_appLayout.SnapToGrid)
            {
                IconPosition? alignedPosition = FindAlignedIconPosition(
                    name,
                    position,
                    BuildGroupBoundsAfterRemovingItems([name]));
                if (alignedPosition == null)
                {
                    StatusText.Text = $"没有可用网格，“{name}”仍保留在“{group.Name}”";
                    return;
                }

                position = alignedPosition;
            }

            group.ItemNames.RemoveAll(item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
            group.ManuallyAssignedItemNames.RemoveAll(item =>
                item.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (group.IsAutoCategory)
            {
                // 用户明确移出自动分组后，不再让“取消分类”覆盖该项目的新位置。
                _appLayout.AutoClassificationOriginalPositions.Remove(name);
            }
            _appLayout.FreeIcons[name] = position;

            RebuildDesktopIconsAndSaveLayout();
            StatusText.Text = $"已将“{name}”移出“{group.Name}”，真实文件未改变";
        }

        private void MoveItemToRecycleBin(string fullPath, string displayName)
        {
            if (ShellItemLocation.TryDecode(fullPath, out _, out _))
            {
                StatusText.Text = $"“{displayName}”是 Windows 系统项目，不能作为真实文件删除";
                return;
            }

            bool isDirectory = Directory.Exists(fullPath);
            bool isFile = File.Exists(fullPath);
            if (!isDirectory && !isFile)
            {
                StatusText.Text = $"“{displayName}”已不存在，正在刷新桌面";
                RequestDesktopRefresh(clearIconCache: false, statusMessage: null);
                return;
            }

            if (IsFileOperationPending(fullPath))
            {
                StatusText.Text = $"“{displayName}”已有文件操作正在进行";
                return;
            }

            if (!FileOperationIdentityGuard.TryCapture(fullPath, out string expectedIdentity))
            {
                StatusText.Text = $"无法确认“{displayName}”仍是当前项目，未排队删除";
                return;
            }

            string itemType = isDirectory ? "文件夹" : "文件";
            MessageBoxResult result = MessageBox.Show(
                $"确定把这个{itemType}移到 Windows 回收站吗？\n\n{displayName}\n{fullPath}\n\n这会删除真实桌面项目，但通常可以从回收站恢复。",
                "删除到回收站",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            QueueRecycleOperation([new RecycleRequest(displayName, fullPath, expectedIdentity)]);
        }

        private void OpenItem(object sender)
        {
            if (sender is FrameworkElement fe && fe.Tag is IconTag tag)
            {
                OpenDesktopItem(tag.FullPath);
            }
        }

        private void OpenDesktopItem(string location)
        {
            if (ShellItemLocation.TryDecode(location, out string parsingName, out _))
            {
                if (!NativeMethods.ExecuteShellNamespaceItem(
                        parsingName,
                        verb: null,
                        new WindowInteropHelper(this).Handle,
                        out int errorCode))
                {
                    _diagnostics.Log($"SHELL open failed parsingName={parsingName}, error={errorCode}");
                    MessageBox.Show(
                        errorCode == 0
                            ? "Windows Shell 无法打开该系统项目。"
                            : $"Windows Shell 无法打开该系统项目（错误 {errorCode}）。",
                        "打开失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                return;
            }

            OpenPath(location);
        }

        private void OpenShellItemProperties(string parsingName, string displayName)
        {
            if (NativeMethods.ExecuteShellNamespaceItem(
                    parsingName,
                    "properties",
                    new WindowInteropHelper(this).Handle,
                    out int errorCode))
            {
                return;
            }

            _diagnostics.Log($"SHELL properties failed parsingName={parsingName}, error={errorCode}");
            MessageBox.Show(
                errorCode == 0
                    ? $"Windows Shell 没有为“{displayName}”提供可打开的属性页。"
                    : $"无法打开“{displayName}”的属性（错误 {errorCode}）。",
                "属性打开失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private static void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开：{ex.Message}", "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void RevealInExplorer(string path)
        {
            try
            {
                string arguments = Directory.Exists(path)
                    ? $"\"{path}\""
                    : $"/select,\"{path}\"";

                Process.Start(new ProcessStartInfo("explorer.exe", arguments)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开资源管理器：{ex.Message}", "操作失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

    }
}
