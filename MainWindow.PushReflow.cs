// 挤压排列预览与提交
// 本文件由 v1.12 完整功能重构拆分；行为逻辑保持自 v1.11.6 不变。
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private void BeginPushPreviewSession(FrameworkElement draggedElement)
        {
            CancelPushPreview(restoreVisuals: true);
            EndPushPreviewSession();

            if (!IsPushReflowActive ||
                draggedElement.Tag is not IconTag tag ||
                tag.Group != null)
            {
                return;
            }

            string draggedName = tag.DisplayName;
            _pushPreviewDraggedName = draggedName;
            _pushPreviewOriginalPositions = new Dictionary<string, IconPosition>(StringComparer.OrdinalIgnoreCase);

            foreach ((string name, IconPosition position) in _appLayout.FreeIcons)
            {
                if (position == null)
                {
                    continue;
                }

                _pushPreviewOriginalPositions[name] = ClonePosition(position);
            }

            // 极端情况下布局中尚无该图标（例如刷新与拖动同时发生），以当前视觉坐标补齐快照。
            if (!_pushPreviewOriginalPositions.ContainsKey(draggedName))
            {
                var current = new IconPosition
                {
                    X = SafeCanvasCoordinate(Canvas.GetLeft(draggedElement)),
                    Y = SafeCanvasCoordinate(Canvas.GetTop(draggedElement))
                };
                ClampIconPosition(current);
                _pushPreviewOriginalPositions[draggedName] = current;
            }
        }

        private void UpdatePushPreview(double draggedLeft, double draggedTop)
        {
            if (_pushPreviewOriginalPositions == null ||
                string.IsNullOrWhiteSpace(_pushPreviewDraggedName) ||
                !IsPushReflowActive)
            {
                return;
            }

            if (!TryFindPushTarget(
                    draggedLeft,
                    draggedTop,
                    out string targetName,
                    out int targetColumn,
                    out int targetRow))
            {
                if (_pushPreviewPositions != null)
                {
                    RestorePushPreviewVisuals(animate: true);
                    _pushPreviewPositions = null;
                    _pushPreviewTargetName = null;
                    StatusText.Text = "已离开插入位置，原排列已恢复";
                }

                return;
            }

            // 鼠标仍停留在同一目标上时不重复计算和启动动画，避免视觉抖动。
            if (_pushPreviewPositions != null &&
                targetName.Equals(_pushPreviewTargetName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Dictionary<string, IconPosition>? preview = BuildPushPreviewLayout(
                _pushPreviewDraggedName,
                targetName,
                targetColumn,
                targetRow,
                _pushPreviewOriginalPositions);

            if (preview == null)
            {
                if (_pushPreviewPositions != null)
                {
                    RestorePushPreviewVisuals(animate: true);
                }

                _pushPreviewPositions = null;
                _pushPreviewTargetName = null;
                StatusText.Text = "目标后方没有可用网格，未执行挤压";
                return;
            }

            Dictionary<string, IconPosition>? previousPreview = _pushPreviewPositions;
            _pushPreviewPositions = preview;
            _pushPreviewTargetName = targetName;

            int shiftedCount = CountShiftedIcons(preview, _pushPreviewOriginalPositions, _pushPreviewDraggedName);
            bool animate = shiftedCount <= 12 && SystemParameters.ClientAreaAnimation;
            ApplyPushPreviewVisuals(preview, previousPreview, animate);
            StatusText.Text = $"松开可插入到“{targetName}”前，预计后移 {shiftedCount} 个图标；拖离可恢复";
        }

        private bool TryFindPushTarget(
            double draggedLeft,
            double draggedTop,
            out string targetName,
            out int targetColumn,
            out int targetRow)
        {
            targetName = string.Empty;
            targetColumn = 0;
            targetRow = 0;

            if (_pushPreviewOriginalPositions == null || string.IsNullOrWhiteSpace(_pushPreviewDraggedName))
            {
                return false;
            }

            // 使用拖动图标中心点进行命中，避免抓住图标不同部位时触发位置不一致。
            var draggedCenter = new Point(
                draggedLeft + IconCellWidth / 2,
                draggedTop + IconCellHeight / 2);

            // 分组区域优先作为“拖入分组”目标，不触发自由图标挤压。
            if (_appLayout.Groups.Any(group => GetGroupBounds(group).Contains(draggedCenter)))
            {
                return false;
            }

            string? bestName = null;
            double bestDistanceSquared = double.MaxValue;
            int bestColumn = 0;
            int bestRow = 0;

            foreach ((string name, IconPosition position) in _pushPreviewOriginalPositions)
            {
                if (name.Equals(_pushPreviewDraggedName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var bounds = new Rect(position.X, position.Y, IconCellWidth, IconCellHeight);
                if (!bounds.Contains(draggedCenter))
                {
                    continue;
                }

                double centerX = position.X + IconCellWidth / 2;
                double centerY = position.Y + IconCellHeight / 2;
                double dx = draggedCenter.X - centerX;
                double dy = draggedCenter.Y - centerY;
                double distanceSquared = dx * dx + dy * dy;

                if (distanceSquared >= bestDistanceSquared)
                {
                    continue;
                }

                (int column, int row) = GetNearestGridCell(position.X, position.Y);
                if (GridCellIntersectsGroup(column, row))
                {
                    continue;
                }

                bestName = name;
                bestDistanceSquared = distanceSquared;
                bestColumn = column;
                bestRow = row;
            }

            if (string.IsNullOrWhiteSpace(bestName))
            {
                return false;
            }

            targetName = bestName;
            targetColumn = bestColumn;
            targetRow = bestRow;
            return true;
        }

        private Dictionary<string, IconPosition>? BuildPushPreviewLayout(
            string draggedName,
            string targetName,
            int targetColumn,
            int targetRow,
            Dictionary<string, IconPosition> originalPositions)
        {
            List<(int Column, int Row)> usableCells = GetUsableGridCellsInDisplayOrder();
            int targetIndex = usableCells.FindIndex(cell =>
                cell.Column == targetColumn && cell.Row == targetRow);
            if (targetIndex < 0)
            {
                return null;
            }

            var cellIndexes = usableCells
                .Select((cell, index) => (cell, index))
                .ToDictionary(item => item.cell, item => item.index);

            var occupiedByIndex = new Dictionary<int, string>();
            foreach ((string name, IconPosition position) in originalPositions)
            {
                if (name.Equals(draggedName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                (int column, int row) = GetNearestGridCell(position.X, position.Y);
                if (!cellIndexes.TryGetValue((column, row), out int index) || occupiedByIndex.ContainsKey(index))
                {
                    continue;
                }

                occupiedByIndex[index] = name;
            }

            if (!occupiedByIndex.TryGetValue(targetIndex, out string? actualTarget) ||
                !actualTarget.Equals(targetName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var preview = originalPositions.ToDictionary(
                pair => pair.Key,
                pair => ClonePosition(pair.Value),
                StringComparer.OrdinalIgnoreCase);
            preview[draggedName] = GridCellToPosition(targetColumn, targetRow);

            string? carriedName = targetName;
            int destinationIndex = targetIndex + 1;

            while (carriedName != null)
            {
                if (destinationIndex >= usableCells.Count)
                {
                    // 最后一个可用网格后没有空间时不显示错误预览，维持原布局。
                    return null;
                }

                (int column, int row) = usableCells[destinationIndex];
                preview[carriedName] = GridCellToPosition(column, row);

                occupiedByIndex.TryGetValue(destinationIndex, out string? nextCarriedName);
                carriedName = nextCarriedName;
                destinationIndex++;
            }

            return preview;
        }

        private List<(int Column, int Row)> GetUsableGridCellsInDisplayOrder()
        {
            int columns = GetGridColumnCount();
            int rows = GetGridRowCount();
            var cells = new List<(int Column, int Row)>(columns * rows);

            // 与初始排列保持一致：先从左到右，再换到下一行。
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    if (!GridCellIntersectsGroup(column, row))
                    {
                        cells.Add((column, row));
                    }
                }
            }

            return cells;
        }

        private void ApplyPushPreviewVisuals(
            Dictionary<string, IconPosition> preview,
            Dictionary<string, IconPosition>? previousPreview,
            bool animate)
        {
            Dictionary<string, IconPosition>? baseline = previousPreview ?? _pushPreviewOriginalPositions;

            foreach ((string name, IconPosition position) in preview)
            {
                if (name.Equals(_pushPreviewDraggedName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (baseline != null &&
                    baseline.TryGetValue(name, out IconPosition? previous) &&
                    PositionsEqual(previous, position))
                {
                    continue;
                }

                FrameworkElement? visual = FindFreeIconVisual(name);
                if (visual != null)
                {
                    MoveFreeIconVisual(visual, position, animate);
                }
            }
        }

        private void RestorePushPreviewVisuals(bool animate = false)
        {
            if (_pushPreviewOriginalPositions == null || _pushPreviewPositions == null)
            {
                return;
            }

            int movedCount = CountShiftedIcons(
                _pushPreviewPositions,
                _pushPreviewOriginalPositions,
                _pushPreviewDraggedName ?? string.Empty);
            bool shouldAnimate = animate && movedCount <= 12 && SystemParameters.ClientAreaAnimation;

            foreach ((string name, IconPosition currentPreviewPosition) in _pushPreviewPositions)
            {
                if (name.Equals(_pushPreviewDraggedName, StringComparison.OrdinalIgnoreCase) ||
                    !_pushPreviewOriginalPositions.TryGetValue(name, out IconPosition? originalPosition) ||
                    PositionsEqual(currentPreviewPosition, originalPosition))
                {
                    continue;
                }

                FrameworkElement? visual = FindFreeIconVisual(name);
                if (visual != null)
                {
                    MoveFreeIconVisual(visual, originalPosition, shouldAnimate);
                }
            }
        }

        private static bool PositionsEqual(IconPosition first, IconPosition second)
        {
            const double epsilon = 0.1;
            return Math.Abs(first.X - second.X) <= epsilon &&
                   Math.Abs(first.Y - second.Y) <= epsilon;
        }

        private void MoveFreeIconVisual(
            FrameworkElement visual,
            IconPosition target,
            bool animate)
        {
            double currentX = SafeCanvasCoordinate(Canvas.GetLeft(visual));
            double currentY = SafeCanvasCoordinate(Canvas.GetTop(visual));

            // 清除旧动画并把最终值写入基础属性，动画结束后不会回跳。
            visual.BeginAnimation(Canvas.LeftProperty, null);
            visual.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(visual, target.X);
            Canvas.SetTop(visual, target.Y);

            if (_isSafeModeActive || !animate ||
                (Math.Abs(currentX - target.X) < 0.1 && Math.Abs(currentY - target.Y) < 0.1))
            {
                return;
            }

            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var xAnimation = new DoubleAnimation(currentX, target.X, PushAnimationDuration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            };
            var yAnimation = new DoubleAnimation(currentY, target.Y, PushAnimationDuration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            };

            visual.BeginAnimation(Canvas.LeftProperty, xAnimation, HandoffBehavior.SnapshotAndReplace);
            visual.BeginAnimation(Canvas.TopProperty, yAnimation, HandoffBehavior.SnapshotAndReplace);
        }

        private FrameworkElement? FindFreeIconVisual(string name)
        {
            return _freeIconVisuals.TryGetValue(name, out FrameworkElement? visual)
                ? visual
                : null;
        }

        private bool TryCommitPushPreview(string draggedName, UIElement dragged, out int shiftedCount)
        {
            shiftedCount = 0;
            if (_pushPreviewPositions == null ||
                _pushPreviewOriginalPositions == null ||
                string.IsNullOrWhiteSpace(_pushPreviewDraggedName) ||
                !_pushPreviewDraggedName.Equals(draggedName, StringComparison.OrdinalIgnoreCase) ||
                !_pushPreviewPositions.TryGetValue(draggedName, out IconPosition? draggedPosition))
            {
                return false;
            }

            shiftedCount = CountShiftedIcons(
                _pushPreviewPositions,
                _pushPreviewOriginalPositions,
                draggedName);

            foreach ((string name, IconPosition position) in _pushPreviewPositions)
            {
                _appLayout.FreeIcons[name] = ClonePosition(position);
            }

            Canvas.SetLeft(dragged, draggedPosition.X);
            Canvas.SetTop(dragged, draggedPosition.Y);
            return true;
        }

        private static int CountShiftedIcons(
            Dictionary<string, IconPosition> preview,
            Dictionary<string, IconPosition> original,
            string draggedName)
        {
            const double epsilon = 0.1;
            int count = 0;
            foreach ((string name, IconPosition previewPosition) in preview)
            {
                if (name.Equals(draggedName, StringComparison.OrdinalIgnoreCase) ||
                    !original.TryGetValue(name, out IconPosition? originalPosition))
                {
                    continue;
                }

                if (Math.Abs(previewPosition.X - originalPosition.X) > epsilon ||
                    Math.Abs(previewPosition.Y - originalPosition.Y) > epsilon)
                {
                    count++;
                }
            }

            return count;
        }

        private void CancelPushPreview(bool restoreVisuals)
        {
            if (restoreVisuals)
            {
                RestorePushPreviewVisuals();
            }

            _pushPreviewPositions = null;
            _pushPreviewTargetName = null;
        }

        private void EndPushPreviewSession()
        {
            _pushPreviewOriginalPositions = null;
            _pushPreviewPositions = null;
            _pushPreviewDraggedName = null;
            _pushPreviewTargetName = null;
        }

        private static IconPosition ClonePosition(IconPosition position) => new()
        {
            X = position.X,
            Y = position.Y
        };

    }
}
