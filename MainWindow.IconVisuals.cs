// 图标视觉、菜单与多选
// 本文件由 v1.12 完整功能重构拆分；行为逻辑保持自 v1.11.6 不变。
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private FrameworkElement CreateIconVisual(string fullPath, string displayName, GroupInfo? parentGroup)
        {
            bool isShellNamespace = ShellItemLocation.TryDecode(
                fullPath,
                out string shellParsingName,
                out _);
            var content = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            FrameworkElement iconElement = CreateAsyncIconElement(fullPath, displayName, parentGroup);

            var label = new TextBlock
            {
                Text = displayName,
                Foreground = MediaBrushes.White,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 34,
                FontSize = 11,
                Margin = new Thickness(2, 0, 2, 0)
            };
            TextOptions.SetTextFormattingMode(label, TextFormattingMode.Display);

            // 用轻量半透明底代替每个文字的 DropShadowEffect。大量阴影处于全屏透明窗口中
            // 会在鼠标移动和拖动时触发昂贵的离屏渲染。
            var labelBackground = new Border
            {
                Background = IconLabelBackgroundBrush,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(1),
                Child = label
            };

            content.Children.Add(iconElement);
            content.Children.Add(labelBackground);

            bool isGroupedIcon = parentGroup != null;
            double tileWidth = isGroupedIcon
                ? GetGroupedIconTileWidth(parentGroup!)
                : IconCellWidth - 10;
            double tileHeight = isGroupedIcon
                ? (_appLayout.CompactGroupLayout ? 74 : 78)
                : IconCellHeight - 10;
            double tileMargin = isGroupedIcon
                ? (_appLayout.CompactGroupLayout ? 1.5 : 2)
                : 3;

            bool fileOperationPending = !isShellNamespace && IsFileOperationPending(fullPath);
            var hitTarget = new Border
            {
                Width = tileWidth,
                Height = tileHeight,
                Margin = new Thickness(tileMargin),
                Padding = new Thickness(2),
                Background = MediaBrushes.Transparent,
                BorderBrush = MediaBrushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Cursor = fileOperationPending ? Cursors.Wait : Cursors.Hand,
                Opacity = fileOperationPending ? 0.52 : 1.0,
                IsHitTestVisible = !fileOperationPending,
                Tag = new IconTag
                {
                    DisplayName = displayName,
                    FullPath = fullPath,
                    Kind = isShellNamespace ? DesktopItemKind.ShellNamespace : DesktopItemKind.FileSystem,
                    Group = parentGroup
                },
                Child = content,
                ToolTip = isShellNamespace
                    ? parentGroup == null
                        ? $"Windows Shell 系统项目；双击打开，可拖入虚拟分类\nCtrl+单击可多选\n{shellParsingName}"
                        : $"Windows Shell 系统项目；双击打开，拖到分类框外可变为自由图标\nCtrl+单击可多选\n{shellParsingName}"
                    : Directory.Exists(fullPath)
                        ? $"真实文件夹：拖入此处会移动真实文件\n双击打开\n{fullPath}"
                        : parentGroup == null
                            ? (_appLayout.IsEditMode
                                ? $"双击打开；拖动可调整位置或移入真实文件夹/虚拟分类\nCtrl+单击可多选\n{fullPath}"
                                : $"双击打开；可拖入真实文件夹或虚拟分类\nCtrl+单击可多选\n{fullPath}")
                            : $"双击打开；拖到分类框外可变为自由图标\nCtrl+单击可多选；右键可批量操作\n{fullPath}",
                SnapsToDevicePixels = true
            };

            _allIconVisuals[displayName] = hitTarget;
            ApplyIconSelectionVisual(displayName, hitTarget, isMouseOver: false);
            hitTarget.MouseEnter += (_, _) =>
                ApplyIconSelectionVisual(displayName, hitTarget, isMouseOver: true);
            hitTarget.MouseLeave += (_, _) =>
                ApplyIconSelectionVisual(displayName, hitTarget, isMouseOver: false);
            hitTarget.ContextMenu = CreateIconContextMenu(fullPath, displayName, parentGroup);

            if (Directory.Exists(fullPath) && !fileOperationPending)
            {
                _physicalFolderDropTargets[fullPath] = hitTarget;
            }

            if (parentGroup == null)
            {
                hitTarget.PreviewMouseLeftButtonDown += Icon_MouseLeftButtonDown;
                hitTarget.MouseLeftButtonUp += Icon_MouseLeftButtonUp;
                hitTarget.MouseMove += Icon_MouseMove;
                hitTarget.LostMouseCapture += Icon_LostMouseCapture;
            }
            else
            {
                hitTarget.PreviewMouseLeftButtonDown += GroupedIcon_MouseLeftButtonDown;
                hitTarget.PreviewMouseMove += GroupedIcon_MouseMove;
                hitTarget.PreviewMouseLeftButtonUp += GroupedIcon_MouseLeftButtonUp;
                hitTarget.LostMouseCapture += GroupedIcon_LostMouseCapture;
            }

            return hitTarget;
        }

        private double GetGroupedIconTileWidth(GroupInfo group)
        {
            // 为内容边距、边框、ScrollViewer 的垂直滚动条预留空间。
            // 默认 280 DIP 宽的分组应稳定容纳 3 列，而不是因滚动条出现退化为 2 列。
            double reservedChromeWidth = _appLayout.CompactGroupLayout ? 32 : 38;
            double horizontalTileMargins = _appLayout.CompactGroupLayout ? 3 : 4;
            int desiredColumns = GetDesiredGroupColumnCount(group);
            double availableWidth = Math.Max(120, group.Width - reservedChromeWidth);
            double widthPerColumn = Math.Floor(availableWidth / desiredColumns);
            return Math.Clamp(
                widthPerColumn - horizontalTileMargins,
                66,
                IconCellWidth - 10);
        }

        private ContextMenu CreateIconContextMenu(string fullPath, string displayName, GroupInfo? parentGroup)
        {
            bool isShellNamespace = ShellItemLocation.TryDecode(fullPath, out string shellParsingName, out _);
            var menu = new ContextMenu();

            var open = new MenuItem { Header = "打开" };
            open.Click += (_, _) => OpenDesktopItem(fullPath);
            menu.Items.Add(open);

            if (isShellNamespace)
            {
                var properties = new MenuItem { Header = "属性" };
                properties.Click += (_, _) => OpenShellItemProperties(shellParsingName, displayName);
                menu.Items.Add(properties);
            }
            else
            {
                var reveal = new MenuItem { Header = "在资源管理器中显示" };
                reveal.Click += (_, _) => RevealInExplorer(fullPath);
                menu.Items.Add(reveal);
            }

            menu.Items.Add(new Separator());
            var select = new MenuItem();
            select.Click += (_, _) => ToggleItemSelection(displayName);
            menu.Items.Add(select);

            if (parentGroup == null && _appLayout.IsEditMode)
            {
                var align = new MenuItem { Header = "对齐到网格" };
                align.Click += (_, _) => AlignSingleIconToGrid(displayName);
                menu.Items.Add(align);
            }

            if (parentGroup != null)
            {
                var remove = new MenuItem
                {
                    Header = "移出分类框（不删除项目）",
                    ToolTip = "把项目变为桌面自由图标，不会移动或删除真实文件，也不会修改系统项目"
                };
                remove.Click += (_, _) => RemoveFromGroup(parentGroup, displayName);
                menu.Items.Add(remove);
            }

            var batchRemove = new MenuItem
            {
                Header = "将所选项目移出分类框",
                ToolTip = "只解除虚拟分类，不移动真实文件或修改系统项目"
            };
            batchRemove.Click += (_, _) => RemoveSelectedItemsFromGroups();
            menu.Items.Add(batchRemove);

            MenuItem? delete = null;
            if (!isShellNamespace)
            {
                menu.Items.Add(new Separator());
                delete = new MenuItem
                {
                    Header = "删除到回收站…",
                    ToolTip = "删除真实桌面文件或文件夹，可从 Windows 回收站恢复"
                };
                delete.Click += (_, _) => MoveItemToRecycleBin(fullPath, displayName);
                menu.Items.Add(delete);
            }

            var batchDelete = new MenuItem
            {
                Header = "将所选真实项目删除到回收站…"
            };
            batchDelete.Click += (_, _) => MoveSelectedItemsToRecycleBin();
            menu.Items.Add(batchDelete);

            menu.Opened += (_, _) =>
            {
                if (!_selectedItemNames.Contains(displayName))
                {
                    _selectedItemNames.Clear();
                    _selectedItemNames.Add(displayName);
                    RefreshItemSelectionVisuals();
                }

                select.Header = _selectedItemNames.Contains(displayName) ? "取消选择" : "选择此项目";
                int selectedCount = _selectedItemNames.Count;
                int selectedPhysicalCount = _selectedItemNames.Count(name =>
                    _desktopItems.TryGetValue(name, out string? location) &&
                    !ShellItemLocation.TryDecode(location, out _, out _));
                bool selectedHasPendingFileOperation = _selectedItemNames.Any(name =>
                    _desktopItems.TryGetValue(name, out string? location) &&
                    !ShellItemLocation.TryDecode(location, out _, out _) &&
                    IsFileOperationPending(location));
                batchRemove.Visibility = selectedCount > 1 ? Visibility.Visible : Visibility.Collapsed;
                batchDelete.Visibility = selectedPhysicalCount > 1 ? Visibility.Visible : Visibility.Collapsed;
                batchRemove.IsEnabled = !selectedHasPendingFileOperation;
                batchDelete.IsEnabled = !selectedHasPendingFileOperation;
                batchRemove.Header = $"将所选 {selectedCount} 项移出分类框";
                batchDelete.Header = $"将所选 {selectedPhysicalCount} 个真实项目删除到回收站…";
                if (delete != null)
                {
                    delete.IsEnabled = !IsFileOperationPending(fullPath) &&
                        (File.Exists(fullPath) || Directory.Exists(fullPath));
                }
            };

            return menu;
        }

        private static string GetIconDisplayName(FrameworkElement element) =>
            element.Tag is IconTag tag ? tag.DisplayName : string.Empty;

        private void ToggleItemSelection(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return;
            }

            if (!_selectedItemNames.Add(displayName))
            {
                _selectedItemNames.Remove(displayName);
            }

            RefreshItemSelectionVisuals();
            StatusText.Text = _selectedItemNames.Count == 0
                ? "已清除选择"
                : $"已选择 {_selectedItemNames.Count} 项；右键可批量操作，Esc 清除选择";
        }

        private void ClearItemSelection()
        {
            if (_selectedItemNames.Count == 0)
            {
                return;
            }

            _selectedItemNames.Clear();
            RefreshItemSelectionVisuals();
            StatusText.Text = "已清除选择";
        }

        private void RefreshItemSelectionVisuals()
        {
            foreach ((string name, FrameworkElement visual) in _allIconVisuals)
            {
                ApplyIconSelectionVisual(name, visual, visual.IsMouseOver);
            }
        }

        private void ApplyIconSelectionVisual(string displayName, FrameworkElement visual, bool isMouseOver)
        {
            if (visual is not Border border)
            {
                return;
            }

            bool selected = _selectedItemNames.Contains(displayName);
            border.Background = selected
                ? SelectedIconBackgroundBrush
                : isMouseOver ? IconHoverBrush : MediaBrushes.Transparent;
            border.BorderBrush = selected ? SelectedIconBorderBrush : MediaBrushes.Transparent;
            border.BorderThickness = selected ? new Thickness(1.5) : new Thickness(0);
        }

        private bool HandleControlClickSelection(FrameworkElement element, MouseButtonEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || e.ClickCount != 1)
            {
                return false;
            }

            string name = GetIconDisplayName(element);
            ToggleItemSelection(name);
            ClearPendingIconDrag();
            e.Handled = true;
            return true;
        }

        private void RemoveSelectedItemsFromGroups()
        {
            if (_selectedItemNames.Count == 0)
            {
                return;
            }

            bool hasPendingPhysicalItem = _selectedItemNames.Any(name =>
                _desktopItems.TryGetValue(name, out string? location) &&
                !ShellItemLocation.TryDecode(location, out _, out _) &&
                IsFileOperationPending(location));
            if (hasPendingPhysicalItem)
            {
                StatusText.Text = "所选项目中有真实文件操作正在进行，暂不能修改分类";
                return;
            }

            int removed = 0;
            foreach (string name in _selectedItemNames.ToList())
            {
                GroupInfo? group = _appLayout.Groups.FirstOrDefault(candidate =>
                    candidate.ItemNames.Contains(name, StringComparer.OrdinalIgnoreCase));
                if (group == null)
                {
                    continue;
                }

                group.ItemNames.RemoveAll(item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
                var position = new IconPosition
                {
                    X = group.X + group.Width + 12,
                    Y = group.Y + removed * 12
                };
                ClampIconPosition(position);
                if (_appLayout.SnapToGrid)
                {
                    position = FindAlignedIconPosition(name, position);
                }
                _appLayout.FreeIcons[name] = position;
                if (group.IsAutoCategory)
                {
                    _appLayout.AutoClassificationOriginalPositions.Remove(name);
                }
                removed++;
            }

            _selectedItemNames.Clear();
            RebuildDesktopIconsAndSaveLayout();
            StatusText.Text = removed > 0 ? $"已将 {removed} 个项目移出分类框" : "所选项目不在分类框中";
        }

        private void MoveSelectedItemsToRecycleBin()
        {
            List<RecycleRequest> selected = _selectedItemNames
                .Where(name => _desktopItems.TryGetValue(name, out string? location) &&
                               !ShellItemLocation.TryDecode(location, out _, out _))
                .Select(name => new RecycleRequest(name, _desktopItems[name]))
                .ToList();
            if (selected.Count == 0)
            {
                return;
            }

            if (selected.Any(item => IsFileOperationPending(item.FullPath)))
            {
                StatusText.Text = "所选项目中已有文件操作正在进行";
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                $"确定把所选 {selected.Count} 个真实桌面项目移到 Windows 回收站吗？\n\nShell 系统项目不会被删除；此操作通常可以从回收站恢复。",
                "批量删除到回收站",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            QueueRecycleOperation(selected);
        }

        private static Color GetGroupAccentColor(GroupInfo group)
        {
            if (!group.IsAutoCategory)
            {
                return Color.FromRgb(121, 166, 214);
            }

            return group.AutoCategoryKey?.ToLowerInvariant() switch
            {
                "system-items" => Color.FromRgb(104, 178, 255),
                "shortcuts" => Color.FromRgb(77, 184, 255),
                "development-projects" => Color.FromRgb(92, 211, 160),
                "folders" => Color.FromRgb(244, 190, 74),
                "documents" => Color.FromRgb(94, 145, 255),
                "spreadsheets" => Color.FromRgb(61, 196, 130),
                "presentations" => Color.FromRgb(242, 132, 75),
                "images" => Color.FromRgb(221, 103, 211),
                "videos" => Color.FromRgb(151, 111, 242),
                "audio" => Color.FromRgb(61, 205, 215),
                "archives" => Color.FromRgb(234, 151, 71),
                "applications" => Color.FromRgb(77, 177, 240),
                "code" => Color.FromRgb(88, 210, 151),
                "data" => Color.FromRgb(83, 191, 184),
                "design" => Color.FromRgb(237, 106, 153),
                "ebooks" => Color.FromRgb(190, 139, 88),
                "fonts" => Color.FromRgb(155, 135, 232),
                "disk-images" => Color.FromRgb(126, 155, 188),
                _ => Color.FromRgb(118, 159, 207)
            };
        }

        private static Color WithAlpha(Color color, byte alpha) =>
            Color.FromArgb(alpha, color.R, color.G, color.B);

        private static Color BlendColor(Color first, Color second, double secondAmount)
        {
            double amount = Math.Clamp(secondAmount, 0, 1);
            byte Mix(byte a, byte b) => (byte)Math.Round(a + ((b - a) * amount));
            return Color.FromArgb(
                Mix(first.A, second.A),
                Mix(first.R, second.R),
                Mix(first.G, second.G),
                Mix(first.B, second.B));
        }

        private static LinearGradientBrush CreateFrozenGradientBrush(Color start, Color end)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(start, 0),
                    new GradientStop(end, 1)
                }
            };
            brush.Freeze();
            return brush;
        }

    }
}
