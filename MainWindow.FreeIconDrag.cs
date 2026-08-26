// 自由图标拖拽与真实文件夹投放
// 本文件由 v1.12 完整功能重构拆分；行为逻辑保持自 v1.11.6 不变。
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
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

            // 锁定布局时仍允许把项目拖入真实文件夹或虚拟分类框；
            // 只有投放到空白处时才禁止改变自由图标坐标。
            // 单击只进入“可能拖动”状态。只有移动超过系统拖动阈值后才捕获鼠标，
            // 避免普通点击也启动挤压快照、动画和布局保存。
            _pendingIconDragElement = element;
            _pendingIconMouseDownCanvasPoint = e.GetPosition(IconCanvas);
            _dragStartOffset = e.GetPosition(element);
            e.Handled = true;
        }

        private void Icon_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggedElement == null)
            {
                if (_pendingIconDragElement == null ||
                    !ReferenceEquals(sender, _pendingIconDragElement) ||
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

                if (!StartIconDrag(_pendingIconDragElement))
                {
                    ClearPendingIconDrag();
                    return;
                }
            }

            if (_draggedElement == null || _draggedIsGroup || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point canvasPosition = e.GetPosition(IconCanvas);
            double left = canvasPosition.X - _dragStartOffset.X;
            double top = canvasPosition.Y - _dragStartOffset.Y;
            ClampIconCoordinates(ref left, ref top);
            Canvas.SetLeft(_draggedElement, left);
            Canvas.SetTop(_draggedElement, top);

            UpdatePhysicalFolderDropPreview(canvasPosition, _draggedElement as FrameworkElement);
            UpdateGroupDropPreview(canvasPosition, _draggedElement as FrameworkElement);
            if (_activePhysicalFolderDropPath != null)
            {
                // 真实文件夹投放具有最高优先级。进入文件夹目标后必须立即撤销
                // 挤压预览，否则目标文件夹本身会被挤走，状态提示也会被
                // “插入到图标前”覆盖，最终动作可能随动画时机发生变化。
                CancelPushPreview(restoreVisuals: true);
            }
            else if (_dragAllowsLayoutMove)
            {
                UpdatePushPreview(left, top);
            }
        }

        private bool StartIconDrag(FrameworkElement element)
        {
            _draggedElement = element;
            _draggedIsGroup = false;
            _draggedGroup = null;
            _pendingIconDragElement = null;
            _dragAllowsLayoutMove = _appLayout.IsEditMode;
            _dragOriginalPosition = new IconPosition
            {
                X = SafeCanvasCoordinate(Canvas.GetLeft(element)),
                Y = SafeCanvasCoordinate(Canvas.GetTop(element))
            };

            if (_dragAllowsLayoutMove)
            {
                BeginPushPreviewSession(element);
            }
            else
            {
                CancelPushPreview(restoreVisuals: true);
                EndPushPreviewSession();
            }

            Panel.SetZIndex(element, 700);

            if (!Mouse.Capture(element, CaptureMode.Element))
            {
                Panel.SetZIndex(element, 200);
                CancelPushPreview(restoreVisuals: true);
                EndPushPreviewSession();
                ClearPhysicalFolderDropPreview();
                ClearGroupDropPreview();
                _draggedElement = null;
                _dragOriginalPosition = null;
                _dragAllowsLayoutMove = false;
                return false;
            }

            return true;
        }

        private void ClearPendingIconDrag()
        {
            _pendingIconDragElement = null;
        }

        private void UpdatePhysicalFolderDropPreview(Point canvasPoint, FrameworkElement? draggedElement)
        {
            if (draggedElement?.Tag is IconTag { Kind: DesktopItemKind.ShellNamespace })
            {
                ClearPhysicalFolderDropPreview();
                return;
            }

            FrameworkElement? nextVisual = null;
            string? nextPath = null;

            foreach ((string folderPath, FrameworkElement visual) in _physicalFolderDropTargets)
            {
                if (ReferenceEquals(visual, draggedElement) ||
                    !visual.IsVisible ||
                    draggedElement?.Tag is IconTag draggedTag &&
                    PhysicalPathsEqual(draggedTag.FullPath, folderPath))
                {
                    continue;
                }

                if (TryGetElementBoundsOnCanvas(visual, out Rect bounds) && bounds.Contains(canvasPoint))
                {
                    nextVisual = visual;
                    nextPath = folderPath;
                }
            }

            if (ReferenceEquals(nextVisual, _activePhysicalFolderDropVisual) &&
                string.Equals(nextPath, _activePhysicalFolderDropPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ClearPhysicalFolderDropPreview();
            if (nextVisual is not Border targetBorder || string.IsNullOrWhiteSpace(nextPath))
            {
                return;
            }

            _activePhysicalFolderDropVisual = targetBorder;
            _activePhysicalFolderDropPath = nextPath;
            targetBorder.BorderBrush = FolderDropBorderBrush;
            targetBorder.BorderThickness = new Thickness(2);
            targetBorder.Background = FolderDropHighlightBrush;
            StatusText.Text = $"松开可移入真实文件夹“{Path.GetFileName(nextPath)}”";
        }

        private void ClearPhysicalFolderDropPreview()
        {
            if (_activePhysicalFolderDropVisual is Border previousBorder)
            {
                string name = GetIconDisplayName(previousBorder);
                ApplyIconSelectionVisual(name, previousBorder, previousBorder.IsMouseOver);
            }

            _activePhysicalFolderDropVisual = null;
            _activePhysicalFolderDropPath = null;
        }

        private void UpdateGroupDropPreview(Point canvasPoint, FrameworkElement? draggedElement)
        {
            // 真实文件夹目标优先，避免文件夹图标位于分类框内部时被虚拟分类截获。
            if (_activePhysicalFolderDropPath != null)
            {
                ClearGroupDropPreview();
                return;
            }

            GroupInfo? sourceGroup = draggedElement?.Tag is IconTag tag
                ? tag.Group ?? _groupedIconDragSourceGroup
                : null;
            FrameworkElement? nextVisual = null;
            GroupInfo? nextGroup = null;
            foreach (GroupInfo group in _appLayout.Groups)
            {
                if (ReferenceEquals(group, sourceGroup) ||
                    !_groupDropTargets.TryGetValue(group.Id, out FrameworkElement? visual) ||
                    !visual.IsVisible)
                {
                    continue;
                }

                if (TryGetElementBoundsOnCanvas(visual, out Rect bounds) && bounds.Contains(canvasPoint))
                {
                    nextVisual = visual;
                    nextGroup = group;
                }
            }

            if (ReferenceEquals(nextVisual, _activeGroupDropVisual) &&
                ReferenceEquals(nextGroup, _activeGroupDropTarget))
            {
                return;
            }

            ClearGroupDropPreview();
            if (nextVisual is not Border targetBorder || nextGroup == null)
            {
                return;
            }

            _activeGroupDropVisual = targetBorder;
            _activeGroupDropTarget = nextGroup;
            targetBorder.BorderBrush = GroupDropBorderBrush;
            targetBorder.BorderThickness = new Thickness(2);
            targetBorder.Background = GroupDropHighlightBrush;
            StatusText.Text = $"松开可加入虚拟分类“{nextGroup.Name}”（不会移动真实文件）";
        }

        private void ClearGroupDropPreview()
        {
            if (_activeGroupDropVisual is Border previousBorder &&
                _activeGroupDropTarget is GroupInfo previousGroup)
            {
                Color accent = GetGroupAccentColor(previousGroup);
                byte alpha = _appLayout.IsEditMode
                    ? (previousGroup.IsAutoCategory ? (byte)176 : (byte)138)
                    : (byte)82;
                previousBorder.BorderBrush = CreateFrozenBrush(WithAlpha(accent, alpha));
                previousBorder.BorderThickness = new Thickness(1.25);
                previousBorder.Background = new SolidColorBrush(Color.FromArgb(20, 0, 0, 0));
            }

            _activeGroupDropVisual = null;
            _activeGroupDropTarget = null;
        }

        private bool TryGetElementBoundsOnCanvas(FrameworkElement element, out Rect bounds)
        {
            bounds = Rect.Empty;
            if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            {
                return false;
            }

            try
            {
                GeneralTransform transform = element.TransformToAncestor(IconCanvas);
                Point topLeft = transform.Transform(new Point(0, 0));
                bounds = new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private PhysicalFolderMoveResult MoveDesktopItemIntoPhysicalFolder(
            string sourcePath,
            string displayName,
            string targetFolderPath)
        {
            if (ShellItemLocation.TryDecode(sourcePath, out _, out _))
            {
                StatusText.Text = $"“{displayName}”是 Windows 系统项目，只能调整布局或加入虚拟分类";
                return PhysicalFolderMoveResult.Rejected;
            }

            if (IsFileOperationPending(sourcePath))
            {
                StatusText.Text = $"“{displayName}”已有文件操作正在进行";
                return PhysicalFolderMoveResult.Rejected;
            }

            if (!Directory.Exists(targetFolderPath))
            {
                StatusText.Text = "目标文件夹已不存在，正在刷新桌面";
                RequestDesktopRefresh(clearIconCache: false, statusMessage: null);
                return PhysicalFolderMoveResult.Rejected;
            }

            bool sourceIsDirectory = Directory.Exists(sourcePath);
            bool sourceIsFile = File.Exists(sourcePath);
            if (!sourceIsDirectory && !sourceIsFile)
            {
                StatusText.Text = $"“{displayName}”已不存在，正在刷新桌面";
                RequestDesktopRefresh(clearIconCache: false, statusMessage: null);
                return PhysicalFolderMoveResult.Rejected;
            }

            string normalizedSource;
            string normalizedTargetFolder;
            try
            {
                normalizedSource = NormalizePathForComparison(sourcePath);
                normalizedTargetFolder = NormalizePathForComparison(targetFolderPath);
            }
            catch (Exception exception)
            {
                StatusText.Text = $"无法验证移动路径：{exception.Message}";
                return PhysicalFolderMoveResult.Rejected;
            }

            if (PhysicalPathsEqual(normalizedSource, normalizedTargetFolder))
            {
                StatusText.Text = "不能把文件夹拖入它自己";
                return PhysicalFolderMoveResult.Rejected;
            }

            if (sourceIsDirectory && IsSameOrDescendantPath(normalizedTargetFolder, normalizedSource))
            {
                MessageBox.Show(
                    "不能把文件夹移动到它自己或它的子文件夹中。",
                    "无法移动",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return PhysicalFolderMoveResult.Rejected;
            }

            string? sourceRoot = Path.GetPathRoot(normalizedSource);
            string? targetRoot = Path.GetPathRoot(normalizedTargetFolder);
            if (!string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "为避免大文件跨磁盘移动长时间占用后台队列，本程序只处理同一磁盘内的拖入。\n\n请使用资源管理器完成跨磁盘复制或移动。",
                    "跨磁盘移动未执行",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return PhysicalFolderMoveResult.Rejected;
            }

            if (!FileOperationIdentityGuard.TryCapture(sourcePath, out string sourceIdentity))
            {
                StatusText.Text = $"无法确认“{displayName}”仍是当前项目，未排队移动";
                return PhysicalFolderMoveResult.Rejected;
            }

            if (!FileOperationIdentityGuard.TryCapture(
                    targetFolderPath,
                    out string targetFolderIdentity))
            {
                StatusText.Text = "无法确认目标文件夹身份，未排队移动";
                return PhysicalFolderMoveResult.Rejected;
            }

            string sourceName = Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath));
            string destinationPath = Path.Combine(targetFolderPath, sourceName);
            bool allowAutoRename = false;
            if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
            {
                MessageBoxResult conflictResult = MessageBox.Show(
                    $"目标文件夹中已经存在同名项目：\n\n{sourceName}\n\n是否自动生成不重复的名称后继续移动？",
                    "存在同名项目",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No);
                if (conflictResult != MessageBoxResult.Yes)
                {
                    StatusText.Text = $"已取消移动“{displayName}”";
                    return PhysicalFolderMoveResult.Rejected;
                }

                allowAutoRename = true;
            }

            GroupInfo? originalGroup = _appLayout.Groups.FirstOrDefault(group =>
                group.ItemNames.Contains(displayName, StringComparer.OrdinalIgnoreCase));
            int originalGroupItemIndex = originalGroup?.ItemNames.FindIndex(item =>
                item.Equals(displayName, StringComparison.OrdinalIgnoreCase)) ?? -1;
            IconPosition? originalFreePosition = _appLayout.FreeIcons.TryGetValue(displayName, out IconPosition? savedPosition) && savedPosition != null
                ? ClonePosition(savedPosition)
                : null;
            IconPosition? autoClassificationOriginalPosition =
                _appLayout.AutoClassificationOriginalPositions.TryGetValue(
                    displayName,
                    out IconPosition? savedAutoPosition) &&
                savedAutoPosition != null
                    ? ClonePosition(savedAutoPosition)
                    : null;

            return QueuePhysicalFolderMove(new PendingPhysicalMove(
                displayName,
                sourcePath,
                targetFolderPath,
                allowAutoRename,
                originalGroup?.Id,
                CreateUndoGroupSnapshot(originalGroup),
                originalGroupItemIndex,
                originalFreePosition,
                autoClassificationOriginalPosition,
                sourceIdentity,
                targetFolderIdentity));
        }

        private static string GetUniqueDestinationPath(
            string targetFolderPath,
            string sourceName,
            bool isDirectory)
        {
            string baseName = isDirectory
                ? sourceName
                : Path.GetFileNameWithoutExtension(sourceName);
            string extension = isDirectory ? string.Empty : Path.GetExtension(sourceName);
            for (int index = 2; index < 10_000; index++)
            {
                string candidateName = $"{baseName} ({index}){extension}";
                string candidatePath = Path.Combine(targetFolderPath, candidateName);
                if (!File.Exists(candidatePath) && !Directory.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            return Path.Combine(
                targetFolderPath,
                $"{baseName}-{DateTime.Now:yyyyMMdd-HHmmss}{extension}");
        }

        private void RestoreDraggedIconAfterRejectedDrop(UIElement dragged, GroupInfo? sourceGroup)
        {
            if (sourceGroup != null)
            {
                RebuildDesktopIcons();
                return;
            }

            RestoreFreeIconToDragOrigin(dragged);
        }

        private void RestoreFreeIconToDragOrigin(UIElement dragged)
        {
            if (_dragOriginalPosition == null)
            {
                RebuildDesktopIcons();
                return;
            }

            Canvas.SetLeft(dragged, _dragOriginalPosition.X);
            Canvas.SetTop(dragged, _dragOriginalPosition.Y);
            Panel.SetZIndex(dragged, 200);
        }

        private static string NormalizePathForComparison(string path) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        private static bool PhysicalPathsEqual(string first, string second) =>
            string.Equals(
                NormalizePathForComparison(first),
                NormalizePathForComparison(second),
                StringComparison.OrdinalIgnoreCase);

        private static bool IsSameOrDescendantPath(string candidatePath, string parentPath)
        {
            string candidate = NormalizePathForComparison(candidatePath);
            string parent = NormalizePathForComparison(parentPath);
            if (string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string parentWithSeparator = parent + Path.DirectorySeparatorChar;
            return candidate.StartsWith(parentWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        private void Icon_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isCompletingIconDrop ||
                _draggedElement == null ||
                _draggedIsGroup ||
                !ReferenceEquals(sender, _draggedElement))
            {
                return;
            }

            // 例如 Alt+Tab、系统弹窗等导致捕获意外丢失时，取消本次拖动并恢复快照。
            CancelPushPreview(restoreVisuals: true);

            if (sender is FrameworkElement element && element.Tag is IconTag tag)
            {
                if (_pushPreviewOriginalPositions != null &&
                    _pushPreviewOriginalPositions.TryGetValue(
                        tag.DisplayName,
                        out IconPosition? originalPosition))
                {
                    Canvas.SetLeft(element, originalPosition.X);
                    Canvas.SetTop(element, originalPosition.Y);
                    Panel.SetZIndex(element, 200);
                }
                else if (_dragOriginalPosition != null)
                {
                    Canvas.SetLeft(element, _dragOriginalPosition.X);
                    Canvas.SetTop(element, _dragOriginalPosition.Y);
                    Panel.SetZIndex(element, 200);
                }
            }

            ClearPhysicalFolderDropPreview();
            ClearGroupDropPreview();
            _draggedElement = null;
            _dragOriginalPosition = null;
            _dragAllowsLayoutMove = false;
            ClearPendingIconDrag();
            EndPushPreviewSession();
            StatusText.Text = "拖动已取消，图标排列已恢复";
        }

        private void Icon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
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

            if (_draggedIsGroup)
            {
                return;
            }

            UIElement dragged = _draggedElement;
            if (dragged is FrameworkElement draggedFrameworkElement)
            {
                // MouseUp 前先恢复旧预览，再按最终光标位置更新拖动视觉和全部目标，
                // 避免快速移动时沿用上一个 MouseMove 的坐标或挤压结果。
                Point finalPoint = e.GetPosition(IconCanvas);
                double finalLeft = finalPoint.X - _dragStartOffset.X;
                double finalTop = finalPoint.Y - _dragStartOffset.Y;
                ClampIconCoordinates(ref finalLeft, ref finalTop);
                Canvas.SetLeft(draggedFrameworkElement, finalLeft);
                Canvas.SetTop(draggedFrameworkElement, finalTop);

                CancelPushPreview(restoreVisuals: true);
                UpdatePhysicalFolderDropPreview(finalPoint, draggedFrameworkElement);
                UpdateGroupDropPreview(finalPoint, draggedFrameworkElement);
                if (_activePhysicalFolderDropPath == null &&
                    _activeGroupDropTarget == null &&
                    _dragAllowsLayoutMove)
                {
                    UpdatePushPreview(finalLeft, finalTop);
                }
            }

            string? physicalFolderTargetPath = _activePhysicalFolderDropPath;
            GroupInfo? previewedGroupTarget = _activeGroupDropTarget;
            ClearPhysicalFolderDropPreview();
            ClearGroupDropPreview();

            _isCompletingIconDrop = true;
            try
            {
                dragged.ReleaseMouseCapture();
            }
            finally
            {
                _isCompletingIconDrop = false;
            }

            if (dragged is FrameworkElement fe && fe.Tag is IconTag tag)
            {
                double left = SafeCanvasCoordinate(Canvas.GetLeft(dragged));
                double top = SafeCanvasCoordinate(Canvas.GetTop(dragged));
                string name = tag.DisplayName;
                GroupInfo? sourceGroup = tag.Group ?? _groupedIconDragSourceGroup;
                var center = new Point(left + IconCellWidth / 2, top + IconCellHeight / 2);
                bool dropHandled = false;

                // 真实文件夹投放优先于虚拟分类框。这样即使文件夹图标位于分类框内部，
                // 把项目准确拖到该文件夹图标上仍会执行真实文件移动。
                if (!string.IsNullOrWhiteSpace(physicalFolderTargetPath))
                {
                    CancelPushPreview(restoreVisuals: true);
                    PhysicalFolderMoveResult moveResult = MoveDesktopItemIntoPhysicalFolder(
                        tag.FullPath,
                        name,
                        physicalFolderTargetPath);
                    if (moveResult != PhysicalFolderMoveResult.NotHandled)
                    {
                        dropHandled = true;
                        if (moveResult == PhysicalFolderMoveResult.Rejected)
                        {
                            RestoreDraggedIconAfterRejectedDrop(dragged, sourceGroup);
                        }
                    }
                }

                if (!dropHandled)
                {
                    GroupInfo? targetGroup = previewedGroupTarget ?? _appLayout.Groups.LastOrDefault(group =>
                        GetGroupBounds(group).Contains(center));

                    if (targetGroup != null)
                    {
                        CancelPushPreview(restoreVisuals: true);
                        bool movedBetweenGroups = sourceGroup != null && !ReferenceEquals(sourceGroup, targetGroup);
                        if (ReferenceEquals(sourceGroup, targetGroup))
                        {
                            targetGroup.SortMode = GroupSortMode.Custom;
                        }

                        if (sourceGroup?.IsAutoCategory == true && movedBetweenGroups)
                        {
                            _appLayout.AutoClassificationOriginalPositions.Remove(name);
                        }

                        foreach (GroupInfo group in _appLayout.Groups)
                        {
                            group.ItemNames.RemoveAll(item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
                        }

                        if (!targetGroup.ItemNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        {
                            targetGroup.ItemNames.Add(name);
                        }

                        _appLayout.FreeIcons.Remove(name);
                        RebuildDesktopIcons();
                        StatusText.Text = sourceGroup == null
                            ? $"已将“{name}”加入虚拟分类“{targetGroup.Name}”；真实文件未移动"
                            : movedBetweenGroups
                                ? $"已将“{name}”移动到虚拟分类“{targetGroup.Name}”；真实文件未移动"
                                : $"已调整“{name}”在“{targetGroup.Name}”中的自定义顺序";
                    }
                    else if (_dragAllowsLayoutMove && TryCommitPushPreview(name, dragged, out int shiftedCount))
                    {
                        StatusText.Text = shiftedCount > 0
                            ? $"已插入“{name}”，向后挤压 {shiftedCount} 个图标"
                            : $"已插入“{name}”";
                    }
                    else if (!_dragAllowsLayoutMove && sourceGroup == null)
                    {
                        // 锁定模式允许拖入文件夹/分类框，但不允许借此改变自由图标位置。
                        CancelPushPreview(restoreVisuals: true);
                        RestoreFreeIconToDragOrigin(dragged);
                        StatusText.Text = "布局已锁定；可拖入真实文件夹或分类框，空白处不会改变位置";
                    }
                    else
                    {
                        CancelPushPreview(restoreVisuals: true);
                        IconPosition? position = new IconPosition { X = left, Y = top };
                        if (_appLayout.SnapToGrid)
                        {
                            position = FindAlignedIconPosition(
                                name,
                                position,
                                sourceGroup == null
                                    ? null
                                    : BuildGroupBoundsAfterRemovingItems([name]));
                        }

                        if (position == null)
                        {
                            RestoreDraggedIconAfterRejectedDrop(dragged, sourceGroup);
                            StatusText.Text = sourceGroup == null
                                ? $"没有可用网格，“{name}”已回到原位"
                                : $"没有可用网格，“{name}”仍保留在原分类中";
                        }
                        else
                        {
                            if (_appLayout.SnapToGrid)
                            {
                                Canvas.SetLeft(dragged, position.X);
                                Canvas.SetTop(dragged, position.Y);
                            }

                            if (sourceGroup != null)
                            {
                                foreach (GroupInfo group in _appLayout.Groups)
                                {
                                    group.ItemNames.RemoveAll(item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
                                }

                                if (sourceGroup.IsAutoCategory)
                                {
                                    _appLayout.AutoClassificationOriginalPositions.Remove(name);
                                }
                            }

                            _appLayout.FreeIcons[name] = position;
                            if (sourceGroup != null)
                            {
                                RebuildDesktopIcons();
                                StatusText.Text = $"已将“{name}”移出分类，可在桌面自由摆放";
                            }
                        }
                    }
                }
            }

            Panel.SetZIndex(dragged, 200);
            _draggedElement = null;
            _groupedIconDragSourceGroup = null;
            _dragOriginalPosition = null;
            _dragAllowsLayoutMove = false;
            ClearPendingIconDrag();
            EndPushPreviewSession();
            SaveLayout();
            e.Handled = true;
        }

        // ==================== 图标挤压排列预览 ====================

    }
}
