// 图标视觉、菜单与多选
// 本文件由 v1.12 完整功能重构拆分；行为逻辑保持自 v1.11.6 不变。
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private FrameworkElement CreateIconVisual(string fullPath, string displayName, GroupInfo? parentGroup)
        {
            bool isGroupedIcon = parentGroup != null;
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
                Foreground = isGroupedIcon
                    ? WarmPaperTheme.PrimaryTextBrush
                    : MediaBrushes.White,
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
                Background = isGroupedIcon
                    ? WarmPaperTheme.GroupedLabelSurfaceBrush
                    : IconLabelBackgroundBrush,
                CornerRadius = new CornerRadius(isGroupedIcon ? 4 : 3),
                Padding = isGroupedIcon ? new Thickness(2, 1, 2, 1) : new Thickness(1),
                Child = label
            };

            content.Children.Add(iconElement);
            content.Children.Add(labelBackground);

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
                        ? $"Windows Shell 系统项目；单击选择；双击打开，可拖入虚拟分类\nCtrl+单击可多选\n{shellParsingName}"
                        : $"Windows Shell 系统项目；单击选择；双击打开，拖到分类框外可变为自由图标\nCtrl+单击可多选；Shift+单击可连续选择\n{shellParsingName}"
                    : Directory.Exists(fullPath)
                        ? parentGroup == null
                            ? $"真实文件夹：拖入此处会移动真实文件\n单击选择；双击打开；Ctrl+单击可多选\n{fullPath}"
                            : $"真实文件夹：拖入此处会移动真实文件\n单击选择；双击打开；Ctrl+单击可多选；Shift+单击可连续选择\n{fullPath}"
                        : parentGroup == null
                            ? (_appLayout.IsEditMode
                                ? $"单击选择；双击打开；拖动可调整位置或移入真实文件夹/虚拟分类\nCtrl+单击可多选\n{fullPath}"
                                : $"单击选择；双击打开；可拖入真实文件夹或虚拟分类\nCtrl+单击可多选\n{fullPath}")
                            : $"单击选择；双击打开；拖到分类框外可变为自由图标\nCtrl+单击可多选；Shift+单击可连续选择；右键可批量操作\n{fullPath}",
                SnapsToDevicePixels = true
            };

            _allIconVisuals[displayName] = hitTarget;
            ApplyIconSelectionVisual(displayName, hitTarget, isMouseOver: false);
            hitTarget.MouseEnter += (_, _) =>
                ApplyIconSelectionVisual(displayName, hitTarget, isMouseOver: true);
            hitTarget.MouseLeave += (_, _) =>
                ApplyIconSelectionVisual(displayName, hitTarget, isMouseOver: false);
            ContextMenu iconMenu = CreateIconContextMenu(fullPath, displayName, parentGroup);
            if (parentGroup != null)
            {
                iconMenu.Opened += (_, _) => NotifyGroupPeekChildMenuOpened(parentGroup.Id);
                iconMenu.Closed += (_, _) => NotifyGroupPeekChildMenuClosed(parentGroup.Id);
            }
            hitTarget.ContextMenu = iconMenu;

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

        internal ContextMenu CreateIconContextMenu(string fullPath, string displayName, GroupInfo? parentGroup)
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
            var tagSummary = new MenuItem
            {
                Header = FormatDesktopItemTagSummary(displayName),
                IsEnabled = false
            };
            var editTags = new MenuItem { Header = "编辑此项目的本地标签…" };
            editTags.Click += (_, _) => EditDesktopItemTags(displayName);
            var batchAddTags = new MenuItem
            {
                Header = "为所选项目添加本地标签…",
                Visibility = Visibility.Collapsed
            };
            batchAddTags.Click += (_, _) => EditSelectedItemTags(remove: false);
            var batchRemoveTags = new MenuItem
            {
                Header = "从所选项目移除本地标签…",
                Visibility = Visibility.Collapsed
            };
            batchRemoveTags.Click += (_, _) => EditSelectedItemTags(remove: true);
            menu.Items.Add(tagSummary);
            menu.Items.Add(editTags);
            menu.Items.Add(batchAddTags);
            menu.Items.Add(batchRemoveTags);
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
                    _groupRangeSelectionAnchor = parentGroup == null
                        ? null
                        : new GroupRangeSelectionAnchor(parentGroup.Id, displayName);
                    RefreshItemSelectionVisuals();
                }

                tagSummary.Header = FormatDesktopItemTagSummary(displayName);
                select.Header = _selectedItemNames.Contains(displayName) ? "取消选择" : "选择此项目";
                int selectedCount = _selectedItemNames.Count;
                int selectedPhysicalCount = _selectedItemNames.Count(name =>
                    _desktopItems.TryGetValue(name, out string? location) &&
                    !ShellItemLocation.TryDecode(location, out _, out _));
                bool selectedHasPendingFileOperation = _selectedItemNames.Any(name =>
                    _desktopItems.TryGetValue(name, out string? location) &&
                    !ShellItemLocation.TryDecode(location, out _, out _) &&
                    IsFileOperationPending(location));
                batchAddTags.Visibility = selectedCount > 1 ? Visibility.Visible : Visibility.Collapsed;
                batchRemoveTags.Visibility = selectedCount > 1 ? Visibility.Visible : Visibility.Collapsed;
                batchRemove.Visibility = selectedCount > 1 ? Visibility.Visible : Visibility.Collapsed;
                batchDelete.Visibility = selectedPhysicalCount > 1 ? Visibility.Visible : Visibility.Collapsed;
                batchAddTags.IsEnabled = !selectedHasPendingFileOperation;
                batchRemoveTags.IsEnabled = !selectedHasPendingFileOperation;
                batchRemove.IsEnabled = !selectedHasPendingFileOperation;
                batchDelete.IsEnabled = !selectedHasPendingFileOperation;
                batchAddTags.Header = $"为所选 {selectedCount} 项添加本地标签…";
                batchRemoveTags.Header = $"从所选 {selectedCount} 项移除本地标签…";
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

        private string FormatDesktopItemTagSummary(string displayName)
        {
            string formatted = ItemTagPolicy.FormatEditorText(
                _appLayout.ItemTags.TryGetValue(displayName, out List<string>? tags)
                    ? tags
                    : null);
            return string.IsNullOrWhiteSpace(formatted)
                ? "本地标签：无"
                : $"本地标签：{formatted}";
        }

        private void EditDesktopItemTags(string displayName)
        {
            if (!_desktopItems.ContainsKey(displayName))
            {
                StatusText.Text = $"“{displayName}”已不在当前桌面，请刷新后重试";
                return;
            }

            string current = ItemTagPolicy.FormatEditorText(
                _appLayout.ItemTags.TryGetValue(displayName, out List<string>? tags)
                    ? tags
                    : null);
            SimpleInputDialog dialog = CreateInputDialog(
                "用逗号或分号分隔标签；标签内部可含空格，留空将清除全部标签：",
                current);
            dialog.Title = "编辑本地标签";
            dialog.Width = 480;
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            List<string> normalized = ItemTagPolicy.ParseEditorText(dialog.ResultText);
            bool changed = ItemTagPolicy.SetTags(
                _appLayout.ItemTags,
                displayName,
                normalized);
            if (changed)
            {
                SaveLayout();
            }

            StatusText.Text = !changed
                ? $"“{displayName}”的本地标签未变化"
                : normalized.Count == 0
                    ? $"已清除“{displayName}”的本地标签"
                    : $"已保存“{displayName}”的本地标签：{string.Join("、", normalized)}";
        }

        private void EditSelectedItemTags(bool remove)
        {
            if (_selectedItemNames.Count(_desktopItems.ContainsKey) < 2)
            {
                StatusText.Text = "请先选择至少两个桌面项目";
                return;
            }

            SimpleInputDialog dialog = CreateInputDialog(
                remove
                    ? "输入要从所选项目移除的标签，用逗号或分号分隔："
                    : "输入要为所选项目添加的标签，用逗号或分号分隔：",
                string.Empty);
            dialog.Title = remove ? "批量移除本地标签" : "批量添加本地标签";
            dialog.Width = 460;
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            List<string> tags = ItemTagPolicy.ParseEditorText(dialog.ResultText);
            if (tags.Count == 0)
            {
                StatusText.Text = "未输入有效标签，本次没有修改";
                return;
            }

            List<string> selectedNames = _selectedItemNames
                .Where(_desktopItems.ContainsKey)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (selectedNames.Count < 2)
            {
                StatusText.Text = "所选项目已变化，当前不足两项，本次没有修改";
                return;
            }

            int changedCount = 0;
            foreach (string name in selectedNames)
            {
                bool changed = remove
                    ? ItemTagPolicy.RemoveTags(_appLayout.ItemTags, name, tags)
                    : ItemTagPolicy.AddTags(_appLayout.ItemTags, name, tags);
                if (changed)
                {
                    changedCount++;
                }
            }
            if (changedCount > 0)
            {
                SaveLayout();
            }

            string formatted = string.Join("、", tags);
            StatusText.Text = changedCount == 0
                ? remove
                    ? $"所选 {selectedNames.Count} 项都不包含标签：{formatted}"
                    : $"所选 {selectedNames.Count} 项都已包含标签：{formatted}"
                : remove
                    ? $"已从所选项目移除标签：{formatted}；实际更新 {changedCount}/{selectedNames.Count} 项"
                    : $"已为所选项目添加标签：{formatted}；实际更新 {changedCount}/{selectedNames.Count} 项";
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

        private void SelectSingleItem(FrameworkElement element)
        {
            string displayName = GetIconDisplayName(element);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return;
            }

            ReplaceSelectionWithSingleItem(_selectedItemNames, displayName);
            _groupRangeSelectionAnchor = element.Tag is IconTag { Group: not null } tag
                ? new GroupRangeSelectionAnchor(tag.Group.Id, displayName)
                : null;
            RefreshItemSelectionVisuals();
            StatusText.Text = $"已选择“{displayName}”；Ctrl+单击可多选，右键可操作";
        }

        internal static bool ShouldBeginSingleItemSelection(
            ModifierKeys modifiers,
            int clickCount) =>
            modifiers == ModifierKeys.None && clickCount == 1;

        internal static bool ReplaceSelectionWithSingleItem(
            ISet<string> selectedItemNames,
            string displayName)
        {
            ArgumentNullException.ThrowIfNull(selectedItemNames);
            if (string.IsNullOrWhiteSpace(displayName) ||
                (selectedItemNames.Count == 1 && selectedItemNames.Contains(displayName)))
            {
                return false;
            }

            selectedItemNames.Clear();
            selectedItemNames.Add(displayName);
            return true;
        }

        private void ToggleGroupItemSelection(GroupInfo group)
        {
            GroupItemSelectionPlan plan = GroupItemSelectionPolicy.CreatePlan(
                group.ItemNames,
                _desktopItems.Keys,
                _selectedItemNames);
            if (plan.ItemCount == 0)
            {
                StatusText.Text = $"“{group.Name}”中没有可选择的桌面项目";
                return;
            }

            _ = GroupItemSelectionPolicy.Apply(_selectedItemNames, plan);
            RefreshItemSelectionVisuals();
            string currentSelection = _selectedItemNames.Count == 0
                ? "当前没有选择"
                : $"当前共选择 {_selectedItemNames.Count} 项";
            StatusText.Text = plan.Select
                ? $"已选择“{group.Name}”中的 {plan.ItemCount} 项；{currentSelection}"
                : $"已取消选择“{group.Name}”中的 {plan.ItemCount} 项；{currentSelection}";
        }

        private void ApplyGroupedRangeSelection(FrameworkElement endpointElement)
        {
            if (endpointElement.Tag is not IconTag { Group: not null } tag)
            {
                return;
            }

            GroupInfo? currentGroup = _appLayout.Groups.FirstOrDefault(group =>
                group.Id.Equals(tag.Group.Id, StringComparison.OrdinalIgnoreCase));
            if (currentGroup == null)
            {
                return;
            }

            IReadOnlyList<string> orderedNames =
                _groupItemPanels.TryGetValue(currentGroup.Id, out VirtualizingGroupPanel? panel)
                    ? panel.GetItemNamesSnapshot()
                        .Where(_desktopItems.ContainsKey)
                        .ToList()
                    : GetSortedGroupItemNames(currentGroup, _desktopItems).ToList();
            GroupRangeSelectionPlan plan = GroupRangeSelectionPolicy.CreatePlan(
                currentGroup.Id,
                orderedNames,
                _selectedItemNames,
                _groupRangeSelectionAnchor,
                tag.DisplayName);
            if (plan.ItemCount == 0)
            {
                return;
            }

            int changedCount = GroupRangeSelectionPolicy.Apply(_selectedItemNames, plan);
            if (!plan.UsedAnchor)
            {
                _groupRangeSelectionAnchor = new GroupRangeSelectionAnchor(
                    currentGroup.Id,
                    plan.ItemNames[0]);
            }

            RefreshItemSelectionVisuals();
            StatusText.Text = plan.UsedAnchor
                ? $"已按“{currentGroup.Name}”当前顺序连续选择 {plan.ItemCount} 项；新增 {changedCount} 项，当前共选择 {_selectedItemNames.Count} 项"
                : $"已选择“{plan.ItemNames[0]}”作为“{currentGroup.Name}”的连续选择起点；当前共选择 {_selectedItemNames.Count} 项";
        }

        private void ClearItemSelection()
        {
            _groupRangeSelectionAnchor = null;
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
            bool isGroupedIcon = border.Tag is IconTag { Group: not null };
            border.Background = selected
                ? SelectedIconBackgroundBrush
                : isMouseOver
                    ? isGroupedIcon ? WarmPaperTheme.WarmHoverBrush : IconHoverBrush
                    : MediaBrushes.Transparent;
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
            _groupRangeSelectionAnchor = element.Tag is IconTag { Group: not null } tag &&
                _selectedItemNames.Contains(name)
                    ? new GroupRangeSelectionAnchor(tag.Group.Id, name)
                    : null;
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

            var removalRequests = new List<(string Name, GroupInfo Group, IconPosition Requested)>();
            foreach (string name in _selectedItemNames.OrderBy(
                         value => value,
                         StringComparer.CurrentCultureIgnoreCase))
            {
                GroupInfo? group = _appLayout.Groups.FirstOrDefault(candidate =>
                    candidate.ItemNames.Contains(name, StringComparer.OrdinalIgnoreCase));
                if (group == null)
                {
                    continue;
                }

                var position = new IconPosition
                {
                    X = group.X + group.Width + 12,
                    Y = group.Y + removalRequests.Count * 12
                };
                ClampIconPosition(position);
                removalRequests.Add((name, group, position));
            }

            if (removalRequests.Count == 0)
            {
                StatusText.Text = "所选项目不在分类框中";
                return;
            }

            Dictionary<string, IconPosition> plannedPositions;
            if (_appLayout.SnapToGrid)
            {
                var movingNames = new HashSet<string>(
                    removalRequests.Select(request => request.Name),
                    StringComparer.OrdinalIgnoreCase);
                plannedPositions = TryPlanAlignedIconPositions(
                    removalRequests.Select(request => (request.Name, request.Requested)),
                    GetOccupiedFreeGridCells(movingNames),
                    groupBoundsOverrides: BuildGroupBoundsAfterRemovingItems(movingNames))
                    ?? [];
                if (plannedPositions.Count != removalRequests.Count)
                {
                    StatusText.Text = "没有足够的可用网格，所选项目仍保留在原分类框中";
                    return;
                }
            }
            else
            {
                plannedPositions = removalRequests.ToDictionary(
                    request => request.Name,
                    request => request.Requested,
                    StringComparer.OrdinalIgnoreCase);
            }

            foreach ((string name, GroupInfo group, _) in removalRequests)
            {
                group.ItemNames.RemoveAll(item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
                group.ManuallyAssignedItemNames.RemoveAll(item =>
                    item.Equals(name, StringComparison.OrdinalIgnoreCase));
                _appLayout.FreeIcons[name] = plannedPositions[name];
                if (group.IsAutoCategory)
                {
                    _appLayout.AutoClassificationOriginalPositions.Remove(name);
                }
            }

            _selectedItemNames.Clear();
            RebuildDesktopIconsAndSaveLayout();
            StatusText.Text = $"已将 {removalRequests.Count} 个项目移出分类框";
        }

        private void MoveSelectedItemsToRecycleBin()
        {
            List<RecycleRequest> selected = _selectedItemNames
                .Where(name => _desktopItems.TryGetValue(name, out string? location) &&
                               !ShellItemLocation.TryDecode(location, out _, out _))
                .Select(name =>
                {
                    string path = _desktopItems[name];
                    return FileOperationIdentityGuard.TryCapture(path, out string identity)
                        ? new RecycleRequest(name, path, identity)
                        : null;
                })
                .Where(request => request != null)
                .Select(request => request!)
                .ToList();
            int selectedPhysicalCount = _selectedItemNames.Count(name =>
                _desktopItems.TryGetValue(name, out string? location) &&
                !ShellItemLocation.TryDecode(location, out _, out _));
            if (selected.Count != selectedPhysicalCount)
            {
                StatusText.Text = "无法确认部分所选项目的身份，未排队删除";
                return;
            }

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
