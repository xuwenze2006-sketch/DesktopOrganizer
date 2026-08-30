// 分组视觉、自适应尺寸与排序
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private int GetDesiredGroupColumnCount(GroupInfo group)
        {
            if (_appLayout.CompactGroupLayout && group.Width >= 330)
            {
                return 4;
            }

            if (group.Width >= 260)
            {
                return 3;
            }

            return group.Width >= 215 ? 2 : 1;
        }

        private double GetGroupedIconRowHeight() =>
            _appLayout.CompactGroupLayout ? 78 : GroupedIconRowHeight;

        private byte GetGroupNormalBorderAlpha(GroupInfo group) =>
            _appLayout.IsEditMode
                ? (group.IsAutoCategory ? (byte)176 : (byte)148)
                : (byte)104;

        private bool AutoFitGroup(GroupInfo group, bool clampPosition = true)
        {
            if (group.IsSizeLocked)
            {
                return false;
            }

            int itemCount = Math.Max(1, group.ItemNames.Count);
            int columns;
            double desiredWidth;
            if (_appLayout.CompactGroupLayout)
            {
                (columns, desiredWidth) = itemCount switch
                {
                    1 => (1, 176),
                    2 => (2, 220),
                    <= 6 => (3, 280),
                    <= 12 => (3, 304),
                    _ => (4, 352)
                };
            }
            else
            {
                (columns, desiredWidth) = itemCount switch
                {
                    1 => (1, 190),
                    2 => (2, 220),
                    _ => (3, GroupPreferredWidth)
                };
            }

            int rows = Math.Clamp(
                (int)Math.Ceiling(itemCount / (double)columns),
                1,
                GroupMaxAutoRows);
            double desiredHeight = Math.Max(
                GroupMinHeight,
                GroupHeaderHeight + 14 + rows * GetGroupedIconRowHeight());

            DesktopMonitorRegion monitor = GetMonitorForItemRect(GetGroupBounds(group));
            desiredWidth = Math.Clamp(
                desiredWidth,
                GroupMinWidth,
                Math.Max(GroupMinWidth, Math.Min(GroupMaxAutoWidth, monitor.WorkArea.Width - 28)));
            desiredHeight = Math.Clamp(
                desiredHeight,
                GroupMinHeight,
                Math.Max(GroupMinHeight, monitor.WorkArea.Height - 28));

            bool changed = Math.Abs(group.Width - desiredWidth) > 0.5 ||
                           Math.Abs(group.Height - desiredHeight) > 0.5;
            group.Width = desiredWidth;
            group.Height = desiredHeight;
            if (clampPosition)
            {
                ClampGroupToCanvas(group);
            }

            return changed;
        }

        private void AutoFitGroupAndUnlock(GroupInfo group)
        {
            group.IsSizeLocked = false;
            AutoFitGroup(group);
            RebuildDesktopIconsAndSaveLayout();
            StatusText.Text = $"“{group.Name}”已按内容自动适应尺寸";
        }

        private IEnumerable<string> GetSortedGroupItemNames(
            GroupInfo group,
            IReadOnlyDictionary<string, string> existing)
        {
            IEnumerable<string> items = group.ItemNames.Where(existing.ContainsKey);
            DateTime SafeFileTime(string path, bool creation)
            {
                if (ShellItemLocation.TryDecode(path, out _, out _))
                {
                    return DateTime.MinValue;
                }

                try
                {
                    return creation ? File.GetCreationTimeUtc(path) : File.GetLastWriteTimeUtc(path);
                }
                catch
                {
                    return DateTime.MinValue;
                }
            }

            return group.SortMode switch
            {
                GroupSortMode.Name => items.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase),
                GroupSortMode.Type => items
                    .OrderBy(name => GetDesktopItemTypeSortKey(existing[name]), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(name => name, StringComparer.CurrentCultureIgnoreCase),
                GroupSortMode.ModifiedTime => items
                    .OrderByDescending(name => SafeFileTime(existing[name], creation: false))
                    .ThenBy(name => name, StringComparer.CurrentCultureIgnoreCase),
                GroupSortMode.CreatedTime => items
                    .OrderByDescending(name => SafeFileTime(existing[name], creation: true))
                    .ThenBy(name => name, StringComparer.CurrentCultureIgnoreCase),
                GroupSortMode.FoldersFirst => items
                    .OrderByDescending(name => IsDesktopItemFolderLike(existing[name]))
                    .ThenBy(name => name, StringComparer.CurrentCultureIgnoreCase),
                _ => items
            };
        }

        private static string GetDesktopItemTypeSortKey(string location)
        {
            if (ShellItemLocation.TryDecode(location, out _, out bool shellFolder))
            {
                return shellFolder ? string.Empty : ".shell";
            }

            return Directory.Exists(location) ? string.Empty : Path.GetExtension(location);
        }

        private static bool IsDesktopItemFolderLike(string location)
        {
            return ShellItemLocation.TryDecode(location, out _, out bool shellFolder)
                ? shellFolder
                : Directory.Exists(location);
        }

        private void SetGroupSortMode(GroupInfo group, GroupSortMode mode)
        {
            group.SortMode = mode;
            RebuildDesktopIconsAndSaveLayout();
            StatusText.Text = mode == GroupSortMode.Custom
                ? $"“{group.Name}”已恢复自定义顺序"
                : $"“{group.Name}”已按{GetGroupSortModeDisplayName(mode)}排序";
        }

        private static string GetGroupSortModeDisplayName(GroupSortMode mode) => mode switch
        {
            GroupSortMode.Name => "名称",
            GroupSortMode.Type => "类型",
            GroupSortMode.ModifiedTime => "修改时间",
            GroupSortMode.CreatedTime => "创建时间",
            GroupSortMode.FoldersFirst => "文件夹优先",
            _ => "自定义顺序"
        };

        private List<GroupVirtualItem> BuildGroupVirtualItems(
            GroupInfo group,
            IReadOnlyDictionary<string, string> existing)
        {
            var items = new List<GroupVirtualItem>(group.ItemNames.Count);
            foreach (string name in GetSortedGroupItemNames(group, existing))
            {
                if (existing.TryGetValue(name, out string? fullPath))
                {
                    items.Add(new GroupVirtualItem(name, fullPath));
                }
            }

            return items;
        }

        private FrameworkElement CreateGroupVisual(GroupInfo group, Dictionary<string, string> existing)
        {
            double displayHeight = GetGroupDisplayHeight(group);
            Color accentColor = GetGroupAccentColor(group);
            Color paperSurface = WarmPaperTheme.PanelSurfaceColor;
            Color headerStart = WithAlpha(BlendColor(accentColor, paperSurface, 0.84), 250);
            Color headerEnd = WithAlpha(BlendColor(accentColor, paperSurface, 0.92), 250);
            Color hoverHeaderStart = WithAlpha(BlendColor(accentColor, paperSurface, 0.76), 250);
            Color hoverHeaderEnd = WithAlpha(BlendColor(accentColor, paperSurface, 0.86), 250);
            Color bodyStart = WithAlpha(
                BlendColor(accentColor, WarmPaperTheme.SoftSurfaceColor, 0.96),
                248);
            Color bodyEnd = WarmPaperTheme.SoftSurfaceColor;

            Brush accentBrush = CreateFrozenBrush(accentColor);
            Brush headerBrush = CreateFrozenGradientBrush(headerStart, headerEnd);
            Brush hoverHeaderBrush = CreateFrozenGradientBrush(hoverHeaderStart, hoverHeaderEnd);
            Brush bodyBrush = CreateFrozenGradientBrush(bodyStart, bodyEnd);
            byte normalBorderAlpha = GetGroupNormalBorderAlpha(group);
            Brush normalBorderBrush = CreateFrozenBrush(WithAlpha(accentColor, normalBorderAlpha));
            Brush hoverBorderBrush = CreateFrozenBrush(WithAlpha(accentColor, 220));
            Style? headerButtonStyle = TryFindResource("GroupHeaderIconButtonStyle") as Style;

            var header = new Border
            {
                Height = GroupHeaderHeight,
                Background = headerBrush,
                CornerRadius = group.IsCollapsed
                    ? new CornerRadius(11)
                    : new CornerRadius(11, 11, 0, 0),
                Cursor = _appLayout.IsEditMode ? Cursors.SizeAll : Cursors.Arrow,
                Tag = group
            };

            var headerLayer = new Grid();
            headerLayer.Children.Add(new Border
            {
                Width = 2,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 8),
                CornerRadius = new CornerRadius(0, 2, 2, 0),
                Background = accentBrush,
                Opacity = 0.72,
                IsHitTestVisible = false
            });
            headerLayer.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(WithAlpha(WarmPaperTheme.BorderColor, 112)),
                IsHitTestVisible = false
            });

            var headerGrid = new Grid { Margin = new Thickness(4, 2, 3, 2) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var collapseButton = new Button
            {
                Content = group.IsCollapsed ? "▸" : "▾",
                ToolTip = group.IsCollapsed ? "展开分组" : "收起分组",
                Tag = group,
                Style = headerButtonStyle
            };
            collapseButton.Click += (_, _) => ToggleGroupCollapsed(group);
            Grid.SetColumn(collapseButton, 0);

            var titlePanel = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 6, 0)
            };
            titlePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titlePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var titleDot = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(4),
                Background = accentBrush,
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            Grid.SetColumn(titleDot, 0);
            var titleText = new TextBlock
            {
                Text = group.Name,
                ToolTip = group.IsAutoCategory
                    ? "自动识别分类；手工拖动和重命名会被保留"
                    : !string.IsNullOrWhiteSpace(group.UserRuleId)
                        ? "用户规则创建的虚拟分组；真实文件未移动"
                        : group.Name,
                Foreground = WarmPaperTheme.PrimaryTextBrush,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(titleText, 1);
            titlePanel.Children.Add(titleDot);
            titlePanel.Children.Add(titleText);
            Grid.SetColumn(titlePanel, 1);

            var countBadge = new Border
            {
                MinWidth = 34,
                Height = 22,
                Padding = new Thickness(7, 0, 7, 0),
                Margin = new Thickness(2, 0, 3, 0),
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromArgb(214, 255, 253, 249)),
                BorderBrush = new SolidColorBrush(WithAlpha(accentColor, 92)),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = $"{group.ItemNames.Count} 项",
                    Foreground = WarmPaperTheme.SecondaryTextBrush,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            };
            Grid.SetColumn(countBadge, 2);

            Border? autoBadge = null;
            if (group.Width >= 250)
            {
                autoBadge = new Border
                {
                    Height = 22,
                    Padding = new Thickness(7, 0, 7, 0),
                    Margin = new Thickness(0, 0, 3, 0),
                    CornerRadius = new CornerRadius(11),
                    Background = new SolidColorBrush(WithAlpha(
                        BlendColor(accentColor, paperSurface, 0.88),
                        244)),
                    BorderBrush = new SolidColorBrush(WithAlpha(accentColor, 116)),
                    BorderThickness = new Thickness(1),
                    Visibility = _appLayout.IsEditMode ? Visibility.Visible : Visibility.Collapsed,
                    Child = new TextBlock
                    {
                        Text = group.IsAutoCategory
                            ? "自动分类"
                            : !string.IsNullOrWhiteSpace(group.UserRuleId)
                                ? "用户规则"
                                : "分类",
                        ToolTip = group.IsAutoCategory
                            ? "虚拟自动分类：拖入只改变分类，不移动真实文件"
                            : !string.IsNullOrWhiteSpace(group.UserRuleId)
                                ? "用户规则虚拟分组：规则只改变本地布局，不移动真实文件"
                                : "虚拟分类：拖入只改变分类，不移动真实文件",
                        Foreground = WarmPaperTheme.PrimaryTextBrush,
                        FontSize = 10.5,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                };
                Grid.SetColumn(autoBadge, 3);
            }

            var menuButton = new Button
            {
                Content = "⋯",
                ToolTip = "分组菜单",
                Tag = group,
                Style = headerButtonStyle,
                Visibility = _appLayout.IsEditMode ? Visibility.Visible : Visibility.Collapsed
            };
            var groupMenu = new ContextMenu();
            var renameItem = new MenuItem { Header = "重命名", IsEnabled = _appLayout.IsEditMode };
            renameItem.Click += (_, _) => RenameGroup(group);
            var collapseItem = new MenuItem { Header = group.IsCollapsed ? "展开" : "收起" };
            collapseItem.Click += (_, _) => ToggleGroupCollapsed(group);
            var autoFitItem = new MenuItem
            {
                Header = group.IsSizeLocked ? "自动适应尺寸" : "重新适应内容"
            };
            autoFitItem.Click += (_, _) => AutoFitGroupAndUnlock(group);
            var sortMenu = new MenuItem { Header = "排序" };
            foreach (GroupSortMode mode in Enum.GetValues<GroupSortMode>())
            {
                var sortItem = new MenuItem
                {
                    Header = GetGroupSortModeDisplayName(mode),
                    IsCheckable = true,
                    IsChecked = group.SortMode == mode,
                    Tag = mode
                };
                sortItem.Click += (_, _) => SetGroupSortMode(group, mode);
                sortMenu.Items.Add(sortItem);
            }
            var selectionSeparator = new Separator();
            var toggleGroupSelectionItem = new MenuItem
            {
                Header = "选择此分组的项目",
                ToolTip = "只更新当前会话选择，不修改布局或真实文件"
            };
            toggleGroupSelectionItem.Click += (_, _) => ToggleGroupItemSelection(group);
            var moveSelectedItem = new MenuItem
            {
                Header = "将所选项目移入此分组",
                ToolTip = "只改变虚拟分类，不移动真实文件",
                Visibility = Visibility.Collapsed
            };
            moveSelectedItem.Click += (_, _) => MoveSelectedItemsToGroup(group);
            var deleteItem = new MenuItem { Header = "删除分组", IsEnabled = _appLayout.IsEditMode };
            deleteItem.Click += (_, args) => DeleteGroup_Click(menuButton, args);
            groupMenu.Items.Add(renameItem);
            groupMenu.Items.Add(collapseItem);
            groupMenu.Items.Add(autoFitItem);
            groupMenu.Items.Add(sortMenu);
            groupMenu.Items.Add(selectionSeparator);
            groupMenu.Items.Add(toggleGroupSelectionItem);
            groupMenu.Items.Add(moveSelectedItem);
            groupMenu.Items.Add(new Separator());
            groupMenu.Items.Add(deleteItem);
            groupMenu.Opened += (_, _) =>
            {
                List<string> selectedNames = _selectedItemNames
                    .Where(_desktopItems.ContainsKey)
                    .ToList();
                int movableCount = selectedNames.Count(name =>
                    !group.ItemNames.Contains(name, StringComparer.OrdinalIgnoreCase));
                bool hasPendingPhysicalItem = selectedNames.Any(name =>
                    _desktopItems.TryGetValue(name, out string? location) &&
                    !ShellItemLocation.TryDecode(location, out _, out _) &&
                    IsFileOperationPending(location));
                GroupItemSelectionPlan selectionPlan = GroupItemSelectionPolicy.CreatePlan(
                    group.ItemNames,
                    _desktopItems.Keys,
                    _selectedItemNames);
                toggleGroupSelectionItem.IsEnabled = selectionPlan.ItemCount > 0;
                toggleGroupSelectionItem.Header = selectionPlan.ItemCount == 0
                    ? "此分组没有可选项目"
                    : selectionPlan.Select
                        ? $"选择此分组的 {selectionPlan.ItemCount} 项"
                        : $"取消选择此分组的 {selectionPlan.ItemCount} 项";
                moveSelectedItem.Visibility = selectedNames.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                moveSelectedItem.IsEnabled = movableCount > 0 && !hasPendingPhysicalItem;
                moveSelectedItem.Header = movableCount == 0
                    ? "所选项目已在此分组"
                    : $"将所选 {movableCount} 项移入此分组";
                moveSelectedItem.ToolTip = hasPendingPhysicalItem
                    ? "所选项目中有真实文件操作正在进行，暂不能修改分类"
                    : "只改变虚拟分类，不移动真实文件";
            };
            menuButton.Click += (_, _) =>
            {
                groupMenu.PlacementTarget = menuButton;
                groupMenu.Placement = PlacementMode.Bottom;
                groupMenu.IsOpen = true;
            };
            Grid.SetColumn(menuButton, 4);

            var closeButton = new Button
            {
                Content = "×",
                ToolTip = "删除分组",
                Tag = group,
                Style = headerButtonStyle,
                Visibility = Visibility.Collapsed
            };
            closeButton.Click += DeleteGroup_Click;
            Grid.SetColumn(closeButton, 5);

            headerGrid.Children.Add(collapseButton);
            headerGrid.Children.Add(titlePanel);
            headerGrid.Children.Add(countBadge);
            if (autoBadge != null)
            {
                headerGrid.Children.Add(autoBadge);
            }
            headerGrid.Children.Add(menuButton);
            headerGrid.Children.Add(closeButton);
            headerLayer.Children.Add(headerGrid);
            header.Child = headerLayer;

            // 使用预览事件并由标题栏自身捕获鼠标。旧实现把鼠标捕获交给外层容器，
            // 光标一离开标题栏后 Move/Up 就不再回到这些处理器，导致分组看起来无法移动。
            header.PreviewMouseLeftButtonDown += GroupHeader_MouseLeftButtonDown;
            header.PreviewMouseMove += GroupHeader_MouseMove;
            header.PreviewMouseLeftButtonUp += GroupHeader_MouseLeftButtonUp;
            header.LostMouseCapture += GroupHeader_LostMouseCapture;
            header.ContextMenu = groupMenu;

            List<GroupVirtualItem> virtualItems = BuildGroupVirtualItems(group, existing);
            double groupedTileWidth = GetGroupedIconTileWidth(group);
            double groupedTileMargin = _appLayout.CompactGroupLayout ? 1.5 : 2;
            int groupedColumns = GetDesiredGroupColumnCount(group);
            double groupedSlotWidth = groupedTileWidth + groupedTileMargin * 2;
            double initialScrollOffset = _groupScrollOffsets.TryGetValue(group.Id, out double savedOffset)
                ? savedOffset
                : 0;
            var itemsPanel = new VirtualizingGroupPanel(
                virtualItems,
                item => CreateIconVisual(item.FullPath, item.DisplayName, group),
                visual => UnregisterIconVisualTree(visual),
                groupedColumns,
                groupedSlotWidth,
                GetGroupedIconRowHeight(),
                initialScrollOffset,
                offset => _groupScrollOffsets[group.Id] = offset)
            {
                Margin = _appLayout.CompactGroupLayout
                    ? new Thickness(6, 6, 6, 6)
                    : new Thickness(8, 7, 8, 8),
                Width = Math.Max(120, group.Width - 20),
                ClipToBounds = true,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true
            };

            var scrollViewer = new ScrollViewer
            {
                Content = itemsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(1),
                Focusable = false,
                CanContentScroll = true,
                PanningMode = PanningMode.VerticalOnly,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
                Style = TryFindResource("GroupScrollViewerStyle") as Style
            };
            itemsPanel.ScrollOwner = scrollViewer;

            var bodyLayer = new Grid();
            bodyLayer.Children.Add(scrollViewer);
            var bodyDivider = new Border
            {
                Height = 1,
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Background = accentBrush,
                Opacity = 0.30,
                IsHitTestVisible = false
            };
            bodyLayer.Children.Add(bodyDivider);

            var body = new Border
            {
                Visibility = group.IsCollapsed ? Visibility.Collapsed : Visibility.Visible,
                Background = bodyBrush,
                CornerRadius = new CornerRadius(0, 0, 11, 11),
                Child = bodyLayer
            };

            var resizeThumb = new Thumb
            {
                Visibility = group.IsCollapsed || !_appLayout.IsEditMode
                    ? Visibility.Collapsed
                    : Visibility.Visible,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                BorderBrush = new SolidColorBrush(WithAlpha(accentColor, 215)),
                Tag = group,
                Style = TryFindResource("GroupResizeThumbStyle") as Style
            };
            resizeThumb.DragDelta += ResizeThumb_DragDelta;
            resizeThumb.DragCompleted += (_, _) => RebuildDesktopIconsAndSaveLayout();

            var outer = new Grid
            {
                Width = group.Width,
                Height = displayHeight,
                Tag = group
            };
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(header, 0);
            Grid.SetRow(body, 1);
            Grid.SetRow(resizeThumb, 0);
            Grid.SetRowSpan(resizeThumb, 2);

            outer.Children.Add(header);
            outer.Children.Add(body);
            outer.Children.Add(resizeThumb);

            var container = new Border
            {
                Child = outer,
                Background = MediaBrushes.Transparent,
                BorderBrush = normalBorderBrush,
                BorderThickness = new Thickness(1.25),
                CornerRadius = new CornerRadius(11),
                ClipToBounds = true,
                Tag = group,
                Opacity = 1.0,
                SnapsToDevicePixels = true
            };

            void SetHoverActionsVisible(bool visible)
            {
                if (_appLayout.IsEditMode)
                {
                    menuButton.Visibility = Visibility.Visible;
                    if (autoBadge != null)
                    {
                        autoBadge.Visibility = Visibility.Visible;
                    }
                    return;
                }

                menuButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (autoBadge != null)
                {
                    autoBadge.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            container.MouseEnter += (_, _) =>
            {
                container.BorderBrush = hoverBorderBrush;
                header.Background = hoverHeaderBrush;
                SetHoverActionsVisible(true);
                RequestGroupPeek(group.Id);
            };
            container.MouseLeave += (_, _) =>
            {
                container.BorderBrush = normalBorderBrush;
                header.Background = headerBrush;
                if (!groupMenu.IsOpen)
                {
                    SetHoverActionsVisible(false);
                }
                ScheduleGroupPeekClose(group.Id);
            };
            groupMenu.Closed += (_, _) =>
            {
                if (!container.IsMouseOver)
                {
                    SetHoverActionsVisible(false);
                    ScheduleGroupPeekClose(group.Id);
                }
            };
            _groupItemPanels[group.Id] = itemsPanel;
            _groupDropTargets[group.Id] = container;
            RegisterGroupPeekVisual(
                group,
                container,
                outer,
                header,
                body,
                bodyDivider,
                itemsPanel,
                groupMenu);
            return container;
        }

        private static string GetIconCacheKey(string path)
        {
            if (ShellItemLocation.TryDecode(path, out string parsingName, out _))
            {
                return "shell:" + parsingName;
            }

            if (Directory.Exists(path))
            {
                return "folder:" + path;
            }

            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension is ".exe" or ".lnk" or ".url" or ".ico" || string.IsNullOrEmpty(extension)
                ? "path:" + path
                : "ext:" + extension;
        }

        // ==================== 自由图标拖拽 ====================

    }
}
