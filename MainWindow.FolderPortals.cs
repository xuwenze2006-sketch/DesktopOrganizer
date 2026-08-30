// 只读文件夹入口：运行时内容、独立视觉与交互边界
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private const double FolderPortalCollapsedHeight = 112;
        private const int FolderPortalNormalZIndex = 120;

        private readonly FolderPortalService _folderPortalService = new();
        private readonly Dictionary<string, FrameworkElement> _folderPortalVisuals =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _folderPortalVisualFingerprints =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FolderPortalRuntimeState> _folderPortalRuntimeStates =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CancellationTokenSource> _folderPortalReadCts =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _folderPortalRefreshAfterRead =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly FolderPortalWatcherCoordinator _folderPortalWatcherCoordinator;

        private FolderPortalInfo? _draggedFolderPortal;
        private FrameworkElement? _draggedFolderPortalVisual;
        private FrameworkElement? _folderPortalDragCaptureElement;
        private Point _folderPortalDragStartMousePoint;
        private Point _folderPortalDragStartPosition;
        private Point _folderPortalDragOffset;
        private bool _folderPortalDragMoved;
        private bool _isCompletingFolderPortalDrag;

        private sealed class FolderPortalRuntimeState
        {
            public string RootSignature { get; set; } = string.Empty;
            public int Generation { get; set; }
            public bool IsLoading { get; set; }
            public bool IsStale { get; set; }
            public string? ErrorMessage { get; set; }
            public PortalReadResult? LastSuccessfulResult { get; set; }
            public int VisualRevision { get; set; }
        }

        /// <summary>
        /// 供主视觉重建流程调用。Portal 卡片是 IconCanvas 的独立顶层元素，既不会注册为
        /// 普通桌面图标，也不会注册为真实文件夹拖放目标。
        /// </summary>
        private void EnsureFolderPortalVisuals()
        {
            var activeIds = new HashSet<string>(
                _appLayout.FolderPortals.Select(portal => portal.Id),
                StringComparer.OrdinalIgnoreCase);

            foreach (string portalId in _folderPortalVisuals.Keys
                         .Where(id => !activeIds.Contains(id))
                         .ToList())
            {
                RemoveFolderPortalVisual(portalId, cancelRead: true, removeRuntimeState: true);
            }

            foreach (FolderPortalInfo portal in _appLayout.FolderPortals)
            {
                string rootSignature = GetFolderPortalRootSignature(portal);
                if (_folderPortalRuntimeStates.TryGetValue(
                        portal.Id,
                        out FolderPortalRuntimeState? previousState) &&
                    !string.Equals(
                        previousState.RootSignature,
                        rootSignature,
                        StringComparison.Ordinal))
                {
                    CancelFolderPortalRead(portal.Id);
                    _folderPortalWatcherCoordinator.Unbind(portal.Id);
                    _folderPortalRefreshAfterRead.Remove(portal.Id);
                    _folderPortalRuntimeStates.Remove(portal.Id);
                }

                if (!_folderPortalRuntimeStates.TryGetValue(
                        portal.Id,
                        out FolderPortalRuntimeState? state))
                {
                    state = new FolderPortalRuntimeState
                    {
                        RootSignature = rootSignature
                    };
                    _folderPortalRuntimeStates[portal.Id] = state;
                    QueueFolderPortalRead(portal, portal.CurrentRelativePath);
                }

                string fingerprint = GetFolderPortalVisualFingerprint(portal, state);
                bool reusable =
                    _folderPortalVisuals.TryGetValue(portal.Id, out FrameworkElement? visual) &&
                    _folderPortalVisualFingerprints.TryGetValue(portal.Id, out string? oldFingerprint) &&
                    string.Equals(oldFingerprint, fingerprint, StringComparison.Ordinal) &&
                    IconCanvas.Children.Contains(visual);

                if (!reusable)
                {
                    RemoveFolderPortalVisual(
                        portal.Id,
                        cancelRead: false,
                        removeRuntimeState: false);
                    visual = CreateFolderPortalVisual(portal, state);
                    _folderPortalVisuals[portal.Id] = visual;
                    _folderPortalVisualFingerprints[portal.Id] = fingerprint;
                    IconCanvas.Children.Add(visual);
                }

                Canvas.SetLeft(visual, portal.X);
                Canvas.SetTop(visual, portal.Y);
                Panel.SetZIndex(visual, FolderPortalNormalZIndex);
            }
        }

        /// <summary>供主视觉孤儿清理集合合并使用。</summary>
        private IEnumerable<FrameworkElement> GetFolderPortalVisuals() =>
            _folderPortalVisuals.Values;

        private FrameworkElement CreateFolderPortalVisual(
            FolderPortalInfo portal,
            FolderPortalRuntimeState state)
        {
            double displayHeight = GetFolderPortalDisplayHeight(portal);
            var card = new Border
            {
                Width = portal.Width,
                Height = displayHeight,
                Background = WarmPaperTheme.PanelSurfaceBrush,
                BorderBrush = WarmPaperTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(0),
                SnapsToDevicePixels = true,
                ClipToBounds = true,
                AllowDrop = false,
                Tag = portal,
                ToolTip = $"真实文件夹入口（只读）\n{portal.RootPath}"
            };
            card.PreviewDragEnter += FolderPortal_BlockDrop;
            card.PreviewDragOver += FolderPortal_BlockDrop;
            card.PreviewDrop += FolderPortal_BlockDrop;

            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            card.Child = rootGrid;

            Grid header = CreateFolderPortalHeader(portal);
            Grid.SetRow(header, 0);
            rootGrid.Children.Add(header);

            var pathPanel = new StackPanel
            {
                Margin = new Thickness(12, 2, 12, 8)
            };
            var rootPathText = new TextBlock
            {
                Text = portal.RootPath,
                Foreground = WarmPaperTheme.MutedTextBrush,
                FontSize = 10.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = portal.RootPath
            };
            pathPanel.Children.Add(rootPathText);

            string breadcrumbRelative = state.LastSuccessfulResult?.CurrentRelativePath ??
                                        portal.CurrentRelativePath;
            var breadcrumb = new TextBlock
            {
                Text = FormatFolderPortalBreadcrumb(breadcrumbRelative),
                Foreground = WarmPaperTheme.SecondaryTextBrush,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = string.IsNullOrWhiteSpace(breadcrumbRelative)
                    ? portal.RootPath
                    : Path.Combine(portal.RootPath, breadcrumbRelative)
            };
            pathPanel.Children.Add(breadcrumb);
            Grid.SetRow(pathPanel, 1);
            rootGrid.Children.Add(pathPanel);

            if (!portal.IsCollapsed)
            {
                FrameworkElement body = CreateFolderPortalBody(portal, state);
                Grid.SetRow(body, 2);
                rootGrid.Children.Add(body);
            }

            card.ContextMenu = CreateFolderPortalContextMenu(portal);
            return card;
        }

        private Grid CreateFolderPortalHeader(FolderPortalInfo portal)
        {
            var header = new Grid
            {
                Height = 44,
                Background = WarmPaperTheme.HeaderSurfaceBrush,
                Cursor = _appLayout.IsEditMode ? Cursors.SizeAll : Cursors.Arrow,
                Tag = portal
            };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titlePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 6, 0)
            };
            titlePanel.Children.Add(new TextBlock
            {
                Text = portal.Name,
                Foreground = WarmPaperTheme.PrimaryTextBrush,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                MaxWidth = Math.Max(70, portal.Width - 230),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });
            titlePanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(229, 239, 232)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(167, 197, 177)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(7, 2, 7, 2),
                Child = new TextBlock
                {
                    Text = "真实文件夹 · 只读",
                    Foreground = new SolidColorBrush(Color.FromRgb(63, 111, 82)),
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold
                }
            });
            Grid.SetColumn(titlePanel, 0);
            header.Children.Add(titlePanel);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            buttons.Children.Add(CreateFolderPortalHeaderButton(
                "←",
                "返回上一级（只读）",
                (_, _) => NavigateFolderPortalUp(portal)));
            buttons.Children.Add(CreateFolderPortalHeaderButton(
                "⌂",
                "返回入口根目录（只读）",
                (_, _) => QueueFolderPortalRead(portal, string.Empty)));
            buttons.Children.Add(CreateFolderPortalHeaderButton(
                "↻",
                "手动刷新；失败不会自动重试",
                (_, _) => QueueFolderPortalRead(portal, portal.CurrentRelativePath)));
            buttons.Children.Add(CreateFolderPortalHeaderButton(
                portal.IsCollapsed ? "▾" : "▴",
                portal.IsCollapsed ? "展开入口" : "收起入口",
                (_, _) => ToggleFolderPortalCollapsed(portal)));
            Grid.SetColumn(buttons, 1);
            header.Children.Add(buttons);

            header.PreviewMouseLeftButtonDown += FolderPortalHeader_MouseLeftButtonDown;
            header.PreviewMouseMove += FolderPortalHeader_MouseMove;
            header.PreviewMouseLeftButtonUp += FolderPortalHeader_MouseLeftButtonUp;
            header.LostMouseCapture += FolderPortalHeader_LostMouseCapture;
            return header;
        }

        private static Button CreateFolderPortalHeaderButton(
            string content,
            string toolTip,
            RoutedEventHandler click)
        {
            var button = new Button
            {
                Content = content,
                Width = 25,
                Height = 25,
                Margin = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(0),
                Foreground = WarmPaperTheme.PrimaryTextBrush,
                Background = WarmPaperTheme.SoftSurfaceBrush,
                BorderBrush = WarmPaperTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                ToolTip = toolTip,
                Focusable = false
            };
            button.Click += click;
            return button;
        }

        private FrameworkElement CreateFolderPortalBody(
            FolderPortalInfo portal,
            FolderPortalRuntimeState state)
        {
            var body = new Grid
            {
                Margin = new Thickness(10, 0, 10, 10)
            };
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            string? statusMessage = null;
            Brush statusForeground = WarmPaperTheme.MutedTextBrush;
            if (state.IsLoading)
            {
                statusMessage = state.LastSuccessfulResult == null
                    ? "正在读取文件夹内容…"
                    : "正在手动刷新；当前仍显示上次成功内容…";
            }
            else if (!string.IsNullOrWhiteSpace(state.ErrorMessage))
            {
                statusMessage = state.IsStale
                    ? $"读取失败，以下为上次成功内容（已过期）：{state.ErrorMessage}"
                    : $"读取失败：{state.ErrorMessage}";
                statusForeground = new SolidColorBrush(Color.FromRgb(151, 90, 32));
            }

            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                var status = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(255, 241, 214)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(7, 4, 7, 4),
                    Margin = new Thickness(0, 0, 0, 6),
                    Child = new TextBlock
                    {
                        Text = statusMessage,
                        Foreground = statusForeground,
                        FontSize = 10.5,
                        TextWrapping = TextWrapping.Wrap,
                        MaxHeight = 34,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        ToolTip = statusMessage
                    }
                };
                Grid.SetRow(status, 0);
                body.Children.Add(status);
            }

            var list = new ListBox
            {
                Background = MediaBrushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                AllowDrop = false,
                Focusable = true,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = "方向键选择；Enter 打开或进入"
            };
            ScrollViewer.SetCanContentScroll(list, true);
            VirtualizingPanel.SetIsVirtualizing(list, true);
            VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            list.PreviewDragEnter += FolderPortal_BlockDrop;
            list.PreviewDragOver += FolderPortal_BlockDrop;
            list.PreviewDrop += FolderPortal_BlockDrop;
            list.PreviewKeyDown += (_, eventArgs) =>
            {
                PortalDirectoryEntry? entry =
                    (list.SelectedItem as FrameworkElement)?.Tag as PortalDirectoryEntry;
                if (!ShouldOpenFolderPortalEntryFromKeyboard(
                        eventArgs.Key,
                        Keyboard.Modifiers,
                        eventArgs.IsRepeat,
                        entry != null))
                {
                    return;
                }

                eventArgs.Handled = true;
                OpenFolderPortalEntry(portal, entry!);
            };

            IReadOnlyList<PortalDirectoryEntry> entries =
                state.LastSuccessfulResult?.Entries ?? Array.Empty<PortalDirectoryEntry>();
            foreach (PortalDirectoryEntry entry in entries)
            {
                list.Items.Add(CreateFolderPortalEntry(portal, entry));
            }

            if (entries.Count == 0 && !state.IsLoading && string.IsNullOrWhiteSpace(state.ErrorMessage))
            {
                list.Items.Add(new ListBoxItem
                {
                    IsHitTestVisible = false,
                    Content = new TextBlock
                    {
                        Text = "此文件夹为空",
                        Foreground = WarmPaperTheme.MutedTextBrush,
                        Margin = new Thickness(8, 12, 8, 12),
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                });
            }
            Grid.SetRow(list, 1);
            body.Children.Add(list);

            string footerText = state.LastSuccessfulResult == null
                ? "未读取 · 不监控 · 不自动重试"
                : $"{entries.Count} 项" +
                  (state.LastSuccessfulResult.IsTruncated ? "（仅显示前 500 项）" : string.Empty) +
                  " · 不监控 · 不自动重试";
            var footer = new TextBlock
            {
                Text = footerText,
                Foreground = WarmPaperTheme.MutedTextBrush,
                FontSize = 9.5,
                Margin = new Thickness(2, 5, 0, 0)
            };
            Grid.SetRow(footer, 2);
            body.Children.Add(footer);
            return body;
        }

        private ListBoxItem CreateFolderPortalEntry(
            FolderPortalInfo portal,
            PortalDirectoryEntry entry)
        {
            var row = new ListBoxItem
            {
                Tag = entry,
                ToolTip = entry.FullPath,
                Padding = new Thickness(6, 5, 6, 5),
                Margin = new Thickness(0, 1, 0, 1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = MediaBrushes.Transparent,
                Foreground = WarmPaperTheme.PrimaryTextBrush,
                Style = TryFindResource("FolderPortalListItemStyle") as Style,
                AllowDrop = false
            };
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.Children.Add(new TextBlock
            {
                Text = entry.IsDirectory ? (entry.IsReparsePoint ? "↗" : "📁") : "📄",
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            });
            var name = new TextBlock
            {
                Text = entry.Name,
                Foreground = WarmPaperTheme.PrimaryTextBrush,
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(name, 1);
            content.Children.Add(name);
            if (entry.IsReparsePoint)
            {
                var linkBadge = new TextBlock
                {
                    Text = "链接 · 外部打开",
                    Foreground = new SolidColorBrush(Color.FromRgb(60, 113, 135)),
                    FontSize = 9.5,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(linkBadge, 2);
                content.Children.Add(linkBadge);
            }
            row.Content = content;
            row.MouseDoubleClick += (_, eventArgs) =>
            {
                OpenFolderPortalEntry(portal, entry);
                eventArgs.Handled = true;
            };
            row.ContextMenu = CreateFolderPortalEntryContextMenu(portal, entry);
            row.PreviewDragEnter += FolderPortal_BlockDrop;
            row.PreviewDragOver += FolderPortal_BlockDrop;
            row.PreviewDrop += FolderPortal_BlockDrop;
            return row;
        }

        private ContextMenu CreateFolderPortalEntryContextMenu(
            FolderPortalInfo portal,
            PortalDirectoryEntry entry)
        {
            var menu = new ContextMenu();
            var open = new MenuItem { Header = "打开" };
            open.Click += (_, _) => OpenFolderPortalEntry(portal, entry);
            menu.Items.Add(open);

            var reveal = new MenuItem { Header = "在资源管理器中显示" };
            reveal.Click += (_, _) =>
            {
                if (CanUseFolderPortalPath(portal, entry.FullPath))
                {
                    RevealInExplorer(entry.FullPath);
                }
            };
            menu.Items.Add(reveal);

            var copy = new MenuItem { Header = "复制路径" };
            copy.Click += (_, _) => CopyFolderPortalPath(portal, entry.FullPath);
            menu.Items.Add(copy);
            return menu;
        }

        private ContextMenu CreateFolderPortalContextMenu(FolderPortalInfo portal)
        {
            var menu = new ContextMenu();
            var readonlyHeader = new MenuItem
            {
                Header = "真实文件夹入口 · 只读",
                IsEnabled = false,
                FontWeight = FontWeights.SemiBold
            };
            menu.Items.Add(readonlyHeader);
            menu.Items.Add(new Separator());

            var openRoot = new MenuItem { Header = "在资源管理器中打开根目录" };
            openRoot.Click += (_, _) =>
            {
                if (CanUseFolderPortalPath(portal, portal.RootPath))
                {
                    OpenPath(portal.RootPath);
                }
            };
            menu.Items.Add(openRoot);

            var refresh = new MenuItem { Header = "手动刷新（失败不自动重试）" };
            refresh.Click += (_, _) => QueueFolderPortalRead(portal, portal.CurrentRelativePath);
            menu.Items.Add(refresh);
            menu.Items.Add(new Separator());

            var rename = new MenuItem
            {
                Header = "重命名入口标题（仅修改本地配置）",
                IsEnabled = _appLayout.IsEditMode
            };
            rename.Click += (_, _) => RenameFolderPortalTitle(portal);
            menu.Items.Add(rename);

            var sizes = new MenuItem
            {
                Header = "入口卡片尺寸（仅布局）",
                IsEnabled = _appLayout.IsEditMode
            };
            sizes.Items.Add(CreateFolderPortalSizeMenuItem(portal, "紧凑 300 × 240", 300, 240));
            sizes.Items.Add(CreateFolderPortalSizeMenuItem(portal, "标准 340 × 280", 340, 280));
            sizes.Items.Add(CreateFolderPortalSizeMenuItem(portal, "宽大 480 × 400", 480, 400));
            menu.Items.Add(sizes);

            var collapse = new MenuItem
            {
                Header = portal.IsCollapsed ? "展开入口" : "收起入口"
            };
            collapse.Click += (_, _) => ToggleFolderPortalCollapsed(portal);
            menu.Items.Add(collapse);
            menu.Items.Add(new Separator());

            var remove = new MenuItem
            {
                Header = "移除此入口配置（不会删除文件夹）",
                IsEnabled = _appLayout.IsEditMode,
                Foreground = new SolidColorBrush(Color.FromRgb(178, 75, 67))
            };
            remove.Click += (_, _) => RemoveFolderPortalConfiguration(portal);
            menu.Items.Add(remove);
            return menu;
        }

        private MenuItem CreateFolderPortalSizeMenuItem(
            FolderPortalInfo portal,
            string header,
            double width,
            double height)
        {
            var item = new MenuItem
            {
                Header = header,
                IsCheckable = true,
                IsChecked = Math.Abs(portal.Width - width) < 0.5 &&
                            Math.Abs(portal.Height - height) < 0.5
            };
            item.Click += (_, _) => ResizeFolderPortal(portal, width, height);
            return item;
        }

        private void FolderPortalButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择要显示的真实文件夹（Portal 始终只读）",
                Multiselect = false
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string selectedPath;
            try
            {
                selectedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dialog.FolderName));
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"所选文件夹路径无效：{exception.Message}",
                    "无法创建入口",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(selectedPath) ||
                !FileOperationIdentityGuard.TryCapture(selectedPath, out string rootIdentity))
            {
                MessageBox.Show(
                    "无法确认所选文件夹仍是当前目录，未创建入口。",
                    "无法创建入口",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_appLayout.FolderPortals.Any(portal =>
                    string.Equals(portal.RootPath, selectedPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(portal.RootIdentity, rootIdentity, StringComparison.Ordinal)))
            {
                StatusText.Text = "该真实文件夹已经有一个只读入口";
                return;
            }

            string name = Path.GetFileName(selectedPath);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = selectedPath;
            }

            var portal = new FolderPortalInfo
            {
                Name = name,
                RootPath = selectedPath,
                RootIdentity = rootIdentity,
                Width = 340,
                Height = 280
            };
            Point position = FindInitialFolderPortalPosition(portal.Width, portal.Height);
            portal.X = position.X;
            portal.Y = position.Y;
            _appLayout.FolderPortals.Add(portal);
            EnsureFolderPortalVisuals();
            SaveLayout();
            StatusText.Text = $"已创建“{portal.Name}”只读入口；不会移动或修改真实文件";
        }

        private void QueueFolderPortalRead(
            FolderPortalInfo portal,
            string? requestedRelativePath)
        {
            if (!_appLayout.FolderPortals.Any(current => ReferenceEquals(current, portal)))
            {
                _folderPortalWatcherCoordinator.Unbind(portal.Id);
                _folderPortalRefreshAfterRead.Remove(portal.Id);
                return;
            }

            CancelFolderPortalRead(portal.Id);
            _folderPortalWatcherCoordinator.CancelPending(portal.Id);
            _folderPortalRefreshAfterRead.Remove(portal.Id);
            if (!_folderPortalRuntimeStates.TryGetValue(
                    portal.Id,
                    out FolderPortalRuntimeState? state))
            {
                state = new FolderPortalRuntimeState
                {
                    RootSignature = GetFolderPortalRootSignature(portal)
                };
                _folderPortalRuntimeStates[portal.Id] = state;
            }

            int generation = ++state.Generation;
            state.IsLoading = true;
            state.ErrorMessage = null;
            state.VisualRevision++;

            var snapshot = FolderPortalLayoutPolicy.Clone(portal);
            snapshot.CurrentRelativePath = requestedRelativePath?.Trim() ?? string.Empty;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            _folderPortalReadCts[portal.Id] = cts;
            RefreshFolderPortalVisual(portal);
            _ = ReadFolderPortalAsync(portal, snapshot, state, generation, cts);
        }

        private async Task ReadFolderPortalAsync(
            FolderPortalInfo portal,
            FolderPortalInfo snapshot,
            FolderPortalRuntimeState state,
            int generation,
            CancellationTokenSource cts)
        {
            PortalReadResult result;
            try
            {
                result = await Task.Run(
                    () => _folderPortalService.Read(snapshot, cts.Token),
                    cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                result = new PortalReadResult(
                    Success: false,
                    RootPath: snapshot.RootPath,
                    CurrentPath: string.Empty,
                    CurrentRelativePath: snapshot.CurrentRelativePath,
                    Entries: Array.Empty<PortalDirectoryEntry>(),
                    IsTruncated: false,
                    ErrorMessage: exception.Message);
            }
            finally
            {
                if (_folderPortalReadCts.TryGetValue(
                        portal.Id,
                        out CancellationTokenSource? currentCts) &&
                    ReferenceEquals(currentCts, cts))
                {
                    _folderPortalReadCts.Remove(portal.Id);
                }
                cts.Dispose();
            }

            if (!_folderPortalRuntimeStates.TryGetValue(portal.Id, out FolderPortalRuntimeState? currentState) ||
                !ReferenceEquals(currentState, state) ||
                currentState.Generation != generation ||
                !_appLayout.FolderPortals.Any(current => ReferenceEquals(current, portal)))
            {
                return;
            }

            bool refreshAfterRead = _folderPortalRefreshAfterRead.Remove(portal.Id);
            bool refreshAfterWatcherBind = false;
            state.IsLoading = false;
            if (result.Success)
            {
                bool pathChanged = !string.Equals(
                    portal.CurrentRelativePath,
                    result.CurrentRelativePath,
                    StringComparison.OrdinalIgnoreCase);
                portal.CurrentRelativePath = result.CurrentRelativePath;
                state.LastSuccessfulResult = result;
                state.IsStale = false;
                state.ErrorMessage = null;
                if (pathChanged)
                {
                    SaveLayout();
                }

                if (!_isSafeModeActive && !_isClosing)
                {
                    string? previousWatchedPath =
                        _folderPortalWatcherCoordinator.GetBoundPath(portal.Id);
                    if (_folderPortalWatcherCoordinator.TryBind(portal.Id, result.CurrentPath))
                    {
                        string? currentWatchedPath =
                            _folderPortalWatcherCoordinator.GetBoundPath(portal.Id);
                        refreshAfterWatcherBind = currentWatchedPath != null &&
                            !string.Equals(
                                previousWatchedPath,
                                currentWatchedPath,
                                StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            else
            {
                _folderPortalWatcherCoordinator.CancelPending(portal.Id);
                state.IsStale = state.LastSuccessfulResult != null;
                state.ErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "未能读取文件夹内容。"
                    : result.ErrorMessage;
            }
            state.VisualRevision++;
            RefreshFolderPortalVisual(portal);

            if (result.Success &&
                (refreshAfterRead || refreshAfterWatcherBind) &&
                !_isSafeModeActive &&
                !_isClosing)
            {
                QueueFolderPortalRead(portal, portal.CurrentRelativePath);
            }
        }

        private void NavigateFolderPortalUp(FolderPortalInfo portal)
        {
            string current = portal.CurrentRelativePath?.Trim() ?? string.Empty;
            if (current.Length == 0)
            {
                return;
            }

            string? parent = Path.GetDirectoryName(current);
            QueueFolderPortalRead(portal, parent ?? string.Empty);
        }

        private void OpenFolderPortalEntry(
            FolderPortalInfo portal,
            PortalDirectoryEntry entry)
        {
            if (!CanUseFolderPortalPath(portal, entry.FullPath))
            {
                return;
            }

            if (entry.CanNavigate)
            {
                string relativePath = Path.GetRelativePath(portal.RootPath, entry.FullPath);
                QueueFolderPortalRead(portal, relativePath);
                return;
            }

            // 文件和重解析点只交给 Shell 外部打开；重解析目录绝不进入 Portal 内部。
            OpenPath(entry.FullPath);
        }

        internal static bool ShouldOpenFolderPortalEntryFromKeyboard(
            Key key,
            ModifierKeys modifiers,
            bool isRepeat,
            bool hasEntry) =>
            key == Key.Enter &&
            modifiers == ModifierKeys.None &&
            !isRepeat &&
            hasEntry;

        private bool CanUseFolderPortalPath(FolderPortalInfo portal, string path)
        {
            if (!_appLayout.FolderPortals.Any(current => ReferenceEquals(current, portal)) ||
                !FolderPortalService.IsPathWithinRoot(portal.RootPath, path))
            {
                StatusText.Text = "入口项目已不属于当前真实文件夹，未执行操作";
                return false;
            }

            if (!FileOperationIdentityGuard.Matches(portal.RootPath, portal.RootIdentity))
            {
                StatusText.Text = "入口根目录身份已改变；为避免打开错误目录，操作已停止";
                return false;
            }

            return true;
        }

        private void CopyFolderPortalPath(FolderPortalInfo portal, string path)
        {
            if (!CanUseFolderPortalPath(portal, path))
            {
                return;
            }

            try
            {
                Clipboard.SetText(path);
                StatusText.Text = "已复制真实路径";
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"无法复制路径：{exception.Message}",
                    "复制失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void RenameFolderPortalTitle(FolderPortalInfo portal)
        {
            if (!_appLayout.IsEditMode)
            {
                StatusText.Text = "请先开启编辑布局";
                return;
            }

            SimpleInputDialog dialog = CreateInputDialog(
                "重命名入口标题（只改本地显示名，不重命名真实文件夹）：",
                portal.Name);
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ResultText))
            {
                return;
            }

            portal.Name = dialog.ResultText.Trim();
            RefreshFolderPortalVisual(portal);
            SaveLayout();
            StatusText.Text = "入口标题已更新；真实文件夹名称未改变";
        }

        private void RemoveFolderPortalConfiguration(FolderPortalInfo portal)
        {
            if (!_appLayout.IsEditMode)
            {
                StatusText.Text = "请先开启编辑布局";
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                $"从桌面移除只读入口“{portal.Name}”？\n\n" +
                "这只会删除 DesktopOrganizer 的入口配置；不会删除、移动或重命名真实文件夹及其中任何内容。",
                "移除入口配置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            _appLayout.FolderPortals.Remove(portal);
            RemoveFolderPortalVisual(portal.Id, cancelRead: true, removeRuntimeState: true);
            SaveLayout();
            StatusText.Text = $"已移除入口“{portal.Name}”；真实文件夹未改变";
        }

        private void ResizeFolderPortal(FolderPortalInfo portal, double width, double height)
        {
            if (!_appLayout.IsEditMode)
            {
                return;
            }

            portal.Width = Math.Clamp(width, 300, 720);
            portal.Height = Math.Clamp(height, 220, 680);
            Point clamped = ClampRectToUsableDesktop(
                portal.X,
                portal.Y,
                portal.Width,
                GetFolderPortalDisplayHeight(portal));
            portal.X = clamped.X;
            portal.Y = clamped.Y;
            RefreshFolderPortalVisual(portal);
            SaveLayout();
            StatusText.Text = "只读入口卡片尺寸已保存";
        }

        private void ToggleFolderPortalCollapsed(FolderPortalInfo portal)
        {
            portal.IsCollapsed = !portal.IsCollapsed;
            Point clamped = ClampRectToUsableDesktop(
                portal.X,
                portal.Y,
                portal.Width,
                GetFolderPortalDisplayHeight(portal));
            portal.X = clamped.X;
            portal.Y = clamped.Y;
            RefreshFolderPortalVisual(portal);
            SaveLayout();
            StatusText.Text = portal.IsCollapsed ? "只读入口已收起" : "只读入口已展开";
        }

        private void RefreshFolderPortalVisual(FolderPortalInfo portal)
        {
            if (!_folderPortalRuntimeStates.TryGetValue(
                    portal.Id,
                    out FolderPortalRuntimeState? state))
            {
                return;
            }

            RemoveFolderPortalVisual(portal.Id, cancelRead: false, removeRuntimeState: false);
            FrameworkElement visual = CreateFolderPortalVisual(portal, state);
            _folderPortalVisuals[portal.Id] = visual;
            _folderPortalVisualFingerprints[portal.Id] =
                GetFolderPortalVisualFingerprint(portal, state);
            IconCanvas.Children.Add(visual);
            Canvas.SetLeft(visual, portal.X);
            Canvas.SetTop(visual, portal.Y);
            Panel.SetZIndex(visual, FolderPortalNormalZIndex);
        }

        private void RemoveFolderPortalVisual(
            string portalId,
            bool cancelRead,
            bool removeRuntimeState)
        {
            if (cancelRead)
            {
                CancelFolderPortalRead(portalId);
            }

            if (_folderPortalVisuals.Remove(portalId, out FrameworkElement? visual) &&
                IconCanvas.Children.Contains(visual))
            {
                IconCanvas.Children.Remove(visual);
            }
            _folderPortalVisualFingerprints.Remove(portalId);
            if (removeRuntimeState)
            {
                _folderPortalWatcherCoordinator.Unbind(portalId);
                _folderPortalRefreshAfterRead.Remove(portalId);
                _folderPortalRuntimeStates.Remove(portalId);
            }
        }

        private void CancelFolderPortalRead(string portalId)
        {
            if (_folderPortalReadCts.Remove(portalId, out CancellationTokenSource? cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        /// <summary>
        /// 工作区切换和窗口关闭前调用。取消只读枚举并丢弃运行时列表；不会修改布局或文件。
        /// </summary>
        private void CancelAllPortalReads(bool clearRuntimeState = true)
        {
            _folderPortalWatcherCoordinator.UnbindAll();
            _folderPortalRefreshAfterRead.Clear();
            foreach (CancellationTokenSource cts in _folderPortalReadCts.Values.ToList())
            {
                cts.Cancel();
                cts.Dispose();
            }
            _folderPortalReadCts.Clear();
            if (clearRuntimeState)
            {
                _folderPortalRuntimeStates.Clear();
            }
            CancelFolderPortalDrag(commit: false);
        }

        private void FolderPortalWatcherRefreshRequested(
            string portalId,
            long notificationVersion)
        {
            if (_isClosing || _isSafeModeActive || Dispatcher.HasShutdownStarted)
            {
                return;
            }

            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_isClosing ||
                    _isSafeModeActive ||
                    !_folderPortalWatcherCoordinator.IsCurrentNotification(
                        portalId,
                        notificationVersion))
                {
                    return;
                }

                FolderPortalInfo? portal = _appLayout.FolderPortals.FirstOrDefault(candidate =>
                    candidate.Id.Equals(portalId, StringComparison.OrdinalIgnoreCase));
                if (portal != null)
                {
                    if (_folderPortalRuntimeStates.TryGetValue(
                            portalId,
                            out FolderPortalRuntimeState? state) &&
                        state.IsLoading)
                    {
                        _folderPortalRefreshAfterRead.Add(portalId);
                        return;
                    }
                    QueueFolderPortalRead(portal, portal.CurrentRelativePath);
                }
            }));
        }

        private void StopFolderPortalWatchers()
        {
            _folderPortalWatcherCoordinator.UnbindAll();
            _folderPortalRefreshAfterRead.Clear();
        }

        private void ResumeFolderPortalWatchers()
        {
            if (_isClosing || _isSafeModeActive)
            {
                return;
            }

            foreach (FolderPortalInfo portal in _appLayout.FolderPortals.ToList())
            {
                if (_folderPortalRuntimeStates.TryGetValue(
                        portal.Id,
                        out FolderPortalRuntimeState? state))
                {
                    if (state.LastSuccessfulResult is PortalReadResult result)
                    {
                        _folderPortalWatcherCoordinator.TryBind(portal.Id, result.CurrentPath);
                    }

                    if (state.IsLoading)
                    {
                        _folderPortalRefreshAfterRead.Add(portal.Id);
                        continue;
                    }
                }

                QueueFolderPortalRead(portal, portal.CurrentRelativePath);
            }
        }

        private void FolderPortal_BlockDrop(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void FolderPortalHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_appLayout.IsEditMode ||
                e.ChangedButton != MouseButton.Left ||
                sender is not FrameworkElement header ||
                header.Tag is not FolderPortalInfo portal ||
                _draggedElement != null ||
                _draggedFolderPortal != null ||
                IsFolderPortalButtonSource(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (!_folderPortalVisuals.TryGetValue(portal.Id, out FrameworkElement? visual))
            {
                return;
            }

            _draggedFolderPortal = portal;
            _draggedFolderPortalVisual = visual;
            _folderPortalDragCaptureElement = header;
            _folderPortalDragStartMousePoint = e.GetPosition(IconCanvas);
            _folderPortalDragStartPosition = new Point(portal.X, portal.Y);
            _folderPortalDragOffset = e.GetPosition(visual);
            _folderPortalDragMoved = false;
            if (!Mouse.Capture(header, CaptureMode.Element))
            {
                ResetFolderPortalDragState();
                return;
            }

            e.Handled = true;
        }

        private void FolderPortalHeader_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggedFolderPortal == null ||
                _draggedFolderPortalVisual == null ||
                _folderPortalDragCaptureElement == null ||
                e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point current = e.GetPosition(IconCanvas);
            if (!_folderPortalDragMoved)
            {
                Vector delta = current - _folderPortalDragStartMousePoint;
                if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }
                _folderPortalDragMoved = true;
                Panel.SetZIndex(_draggedFolderPortalVisual, 600);
            }

            Point clamped = ClampRectToUsableDesktop(
                current.X - _folderPortalDragOffset.X,
                current.Y - _folderPortalDragOffset.Y,
                _draggedFolderPortal.Width,
                GetFolderPortalDisplayHeight(_draggedFolderPortal));
            Canvas.SetLeft(_draggedFolderPortalVisual, clamped.X);
            Canvas.SetTop(_draggedFolderPortalVisual, clamped.Y);
            e.Handled = true;
        }

        private void FolderPortalHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggedFolderPortal == null)
            {
                return;
            }

            CancelFolderPortalDrag(commit: _folderPortalDragMoved);
            e.Handled = true;
        }

        private void FolderPortalHeader_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isCompletingFolderPortalDrag ||
                _draggedFolderPortal == null ||
                !ReferenceEquals(sender, _folderPortalDragCaptureElement))
            {
                return;
            }
            CancelFolderPortalDrag(commit: false);
        }

        private void CancelFolderPortalDrag(bool commit)
        {
            if (_draggedFolderPortal == null || _draggedFolderPortalVisual == null)
            {
                ResetFolderPortalDragState();
                return;
            }

            FolderPortalInfo portal = _draggedFolderPortal;
            FrameworkElement visual = _draggedFolderPortalVisual;
            bool moved = _folderPortalDragMoved;
            _isCompletingFolderPortalDrag = true;
            try
            {
                if (_folderPortalDragCaptureElement != null &&
                    ReferenceEquals(Mouse.Captured, _folderPortalDragCaptureElement))
                {
                    Mouse.Capture(null);
                }
            }
            finally
            {
                _isCompletingFolderPortalDrag = false;
            }

            bool saved = false;
            if (commit && moved)
            {
                double x = SafeCanvasCoordinate(Canvas.GetLeft(visual));
                double y = SafeCanvasCoordinate(Canvas.GetTop(visual));
                var candidate = new Rect(
                    x,
                    y,
                    portal.Width,
                    GetFolderPortalDisplayHeight(portal));
                if (IsFolderPortalPlacementAvailable(candidate, portal.Id))
                {
                    portal.X = x;
                    portal.Y = y;
                    SaveLayout();
                    StatusText.Text = $"已移动只读入口“{portal.Name}”";
                    saved = true;
                }
                else
                {
                    StatusText.Text = "入口与现有桌面项目重叠，已恢复原位置";
                }
            }

            if (!saved)
            {
                portal.X = _folderPortalDragStartPosition.X;
                portal.Y = _folderPortalDragStartPosition.Y;
            }
            Canvas.SetLeft(visual, portal.X);
            Canvas.SetTop(visual, portal.Y);
            Panel.SetZIndex(visual, FolderPortalNormalZIndex);
            ResetFolderPortalDragState();
        }

        private void ResetFolderPortalDragState()
        {
            _draggedFolderPortal = null;
            _draggedFolderPortalVisual = null;
            _folderPortalDragCaptureElement = null;
            _folderPortalDragMoved = false;
        }

        private static bool IsFolderPortalButtonSource(DependencyObject? source)
        {
            DependencyObject? current = source;
            while (current != null)
            {
                if (current is ButtonBase)
                {
                    return true;
                }
                current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
            }
            return false;
        }

        private Point FindInitialFolderPortalPosition(double width, double height)
        {
            foreach (DesktopMonitorRegion monitor in _desktopGeometry.Monitors
                         .OrderByDescending(item => item.IsPrimary))
            {
                Rect workArea = monitor.WorkArea;
                for (double y = workArea.Top + 70;
                     y + height <= workArea.Bottom;
                     y += 44)
                {
                    for (double x = workArea.Left + 24;
                         x + width <= workArea.Right;
                         x += 44)
                    {
                        var candidate = new Rect(x, y, width, height);
                        if (IsFolderPortalPlacementAvailable(candidate, excludedPortalId: null))
                        {
                            return new Point(x, y);
                        }
                    }
                }
            }

            Rect primary = GetPrimaryWorkArea();
            return ClampRectToUsableDesktop(
                primary.Left + 32,
                primary.Top + 80,
                width,
                height);
        }

        private bool IsFolderPortalPlacementAvailable(Rect candidate, string? excludedPortalId)
        {
            Rect padded = candidate;
            padded.Inflate(7, 7);
            if (_appLayout.Groups.Any(group => padded.IntersectsWith(GetGroupBounds(group))))
            {
                return false;
            }

            if (_appLayout.FreeIcons.Values.Any(position =>
                    padded.IntersectsWith(new Rect(
                        position.X,
                        position.Y,
                        IconCellWidth,
                        IconCellHeight))))
            {
                return false;
            }

            foreach (FolderPortalInfo portal in _appLayout.FolderPortals)
            {
                if (!string.IsNullOrWhiteSpace(excludedPortalId) &&
                    portal.Id.Equals(excludedPortalId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (padded.IntersectsWith(GetFolderPortalBounds(portal)))
                {
                    return false;
                }
            }

            Rect? recycleObstacle = GetRecycleBinWidgetObstacle();
            if (recycleObstacle.HasValue && padded.IntersectsWith(recycleObstacle.Value))
            {
                return false;
            }

            if (ControlPanel.Visibility == Visibility.Visible)
            {
                Point panelPosition = GetControlPanelPosition();
                double panelWidth = GetRenderedLength(
                    ControlPanel.ActualWidth,
                    ControlPanel.Width,
                    ControlPanel.DesiredSize.Width);
                double panelHeight = GetRenderedLength(
                    ControlPanel.ActualHeight,
                    ControlPanel.Height,
                    ControlPanel.DesiredSize.Height);
                if (panelWidth > 0 && panelHeight > 0 &&
                    padded.IntersectsWith(new Rect(
                        panelPosition.X,
                        panelPosition.Y,
                        panelWidth,
                        panelHeight)))
                {
                    return false;
                }
            }
            return true;
        }

        private Rect GetFolderPortalBounds(FolderPortalInfo portal) => new(
            portal.X,
            portal.Y,
            portal.Width,
            GetFolderPortalDisplayHeight(portal));

        private void ClampFolderPortalToCanvas(FolderPortalInfo portal)
        {
            Point clamped = ClampRectToUsableDesktop(
                portal.X,
                portal.Y,
                portal.Width,
                GetFolderPortalDisplayHeight(portal));
            portal.X = clamped.X;
            portal.Y = clamped.Y;
        }

        private IEnumerable<Rect> GetFolderPortalObstacles() =>
            _appLayout.FolderPortals.Select(GetFolderPortalBounds);

        private bool IntersectsFolderPortal(Rect bounds, string? excludedPortalId = null)
        {
            foreach (FolderPortalInfo portal in _appLayout.FolderPortals)
            {
                if (!string.IsNullOrWhiteSpace(excludedPortalId) &&
                    portal.Id.Equals(excludedPortalId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (bounds.IntersectsWith(GetFolderPortalBounds(portal)))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsPointOverFolderPortal(Point point) =>
            _appLayout.FolderPortals.Any(portal => GetFolderPortalBounds(portal).Contains(point));

        private static double GetFolderPortalDisplayHeight(FolderPortalInfo portal) =>
            portal.IsCollapsed ? FolderPortalCollapsedHeight : portal.Height;

        private static string FormatFolderPortalBreadcrumb(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return "根目录";
            }

            string[] segments = relativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return "根目录  ›  " + string.Join("  ›  ", segments);
        }

        private static string GetFolderPortalRootSignature(FolderPortalInfo portal) =>
            $"{portal.RootPath}\0{portal.RootIdentity}";

        private string GetFolderPortalVisualFingerprint(
            FolderPortalInfo portal,
            FolderPortalRuntimeState state) =>
            string.Join(
                '|',
                portal.Name,
                portal.RootPath,
                portal.CurrentRelativePath,
                portal.Width.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                portal.Height.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                portal.IsCollapsed ? "1" : "0",
                _appLayout.IsEditMode ? "1" : "0",
                state.VisualRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
