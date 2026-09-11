namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private static readonly Brush LightDesktopSurfaceBrush =
            CreateFrozenBrush(Color.FromArgb(248, 240, 246, 246));
        private readonly Dictionary<string, Border> _lightDesktopEntries = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<FrameworkElement> _lightDesktopDecorations = new();
        private Border? _joinedLightDesktopProject;
        private string? _lightDesktopDrawerId;
        private long _lightDesktopDrawerGeneration;

        private void RefreshLightDesktopEntries()
        {
            foreach (FrameworkElement visual in _lightDesktopEntries.Values)
                IconCanvas.Children.Remove(visual);
            _lightDesktopEntries.Clear();
            var entries = _appLayout.Groups.Where(IsLightDesktopEntry).ToList();
            if (!entries.Any(group => group.Id == _lightDesktopDrawerId))
                _lightDesktopDrawerId = null;
            foreach (GroupInfo group in entries)
            {
                if (!_groupPeekVisuals.TryGetValue(group.Id, out GroupPeekVisualRegistration? registration)) continue;
                var content = new Grid { Margin = new Thickness(10, 0, 9, 0) };
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3),
                    Background = CreateFrozenBrush(GetGroupAccentColor(group)), Margin = new Thickness(0, 0, 7, 0) };
                var title = new TextBlock { Text = group.Name, FontSize = 12, FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(title, 1);
                var count = new TextBlock { Text = group.ItemNames.Count.ToString(), FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 8, 0),
                    Foreground = WarmPaperTheme.SecondaryTextBrush };
                Grid.SetColumn(count, 2);
                var arrow = new TextBlock { Text = "›", FontSize = 17, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(arrow, 3);
                content.Children.Add(dot); content.Children.Add(title); content.Children.Add(count); content.Children.Add(arrow);
                var button = new Button { Content = content, Background = MediaBrushes.Transparent,
                    Foreground = WarmPaperTheme.PrimaryTextBrush, BorderThickness = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, Cursor = Cursors.Hand,
                    ToolTip = $"{group.Name} · {group.ItemNames.Count} 项\n点击展开，再次点击或按 Esc 关闭；右键打开分组菜单" };
                // 保留键盘焦点边框和鼠标反馈，常态没有按钮底色。
                var chrome = new FrameworkElementFactory(typeof(Border));
                chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
                chrome.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
                var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
                presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                chrome.AppendChild(presenter);
                button.Template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
                button.Click += (_, _) => ToggleLightDesktopDrawer(group);
                button.MouseEnter += (_, _) => button.Background = new SolidColorBrush(Color.FromArgb(85, 255, 255, 255));
                button.MouseLeave += (_, _) => button.Background = MediaBrushes.Transparent;
                button.IsKeyboardFocusedChanged += (_, _) => button.Background = button.IsKeyboardFocused
                    ? WarmPaperTheme.SageSoftBrush : MediaBrushes.Transparent;
                var anchor = new Border { Width = GetGroupDisplayWidth(group), Height = GroupHeaderHeight,
                    Child = button, Tag = group, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7),
                    BorderBrush = MediaBrushes.Transparent, Background = MediaBrushes.Transparent,
                    ContextMenu = registration.HeaderMenu };
                if (_appLayout.IsEditMode)
                {
                    var editGrid = new Grid();
                    editGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                    editGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var handle = new Border { Name = "LightDesktopEntryDragHandle", Tag = group,
                        Background = MediaBrushes.Transparent, Cursor = Cursors.SizeAll, ToolTip = "拖动分类入口",
                        Child = new TextBlock { Text = "⠿", VerticalAlignment = VerticalAlignment.Center,
                            Foreground = WarmPaperTheme.SecondaryTextBrush } };
                    handle.PreviewMouseLeftButtonDown += GroupHeader_MouseLeftButtonDown;
                    handle.PreviewMouseMove += GroupHeader_MouseMove;
                    handle.PreviewMouseLeftButtonUp += GroupHeader_MouseLeftButtonUp;
                    handle.LostMouseCapture += GroupHeader_LostMouseCapture;
                    anchor.Child = null;
                    Grid.SetColumn(button, 1);
                    editGrid.Children.Add(handle); editGrid.Children.Add(button); anchor.Child = editGrid;
                }
                _lightDesktopEntries[group.Id] = anchor;
                Canvas.SetLeft(anchor, group.X); Canvas.SetTop(anchor, group.Y);
                Panel.SetZIndex(anchor, GroupNormalZIndex);
                IconCanvas.Children.Add(anchor);
            }
            RefreshLightDesktopSection();
            if (_lightDesktopDrawerId is string openId && _groupPeekVisuals.ContainsKey(openId))
            {
                _activeGroupPeekId = openId;
                ApplyGroupPeekVisualState(openId);
            }
        }

        private void RefreshLightDesktopSection()
        {
            if (_joinedLightDesktopProject != null)
            {
                _joinedLightDesktopProject.CornerRadius = new CornerRadius(11);
                _joinedLightDesktopProject = null;
            }
            foreach (FrameworkElement visual in _lightDesktopDecorations)
                IconCanvas.Children.Remove(visual);
            _lightDesktopDecorations.Clear();
            var entries = _appLayout.Groups.Where(IsLightDesktopEntry).ToList();
            if (entries.Count > 0)
            {
                double left = entries.Min(group => group.X), top = entries.Min(group => group.Y);
                double right = entries.Max(group => group.X + GetGroupDisplayWidth(group));
                double bottom = entries.Max(group => group.Y + GroupHeaderHeight);
                // 用户手动分散入口后不再铺一张巨大的底板。
                if (right - left <= LightDesktopLayoutPolicy.ColumnWidth + 1)
                {
                    double sectionTop = top - 24;
                    double sectionWidth = right - left;
                    Thickness sectionBorder = new(0);
                    Brush? sectionBorderBrush = null;
                    bool draggingEntry = _groupDragMoved && _draggedGroup != null && entries.Contains(_draggedGroup);
                    GroupInfo? project = _appLayout.Groups.FirstOrDefault(group =>
                        group.DesktopRole == DesktopZoneRole.Projects && !group.IsCollapsed && !group.IsSizeLocked &&
                        !draggingEntry && !(_groupDragMoved && _draggedGroup == group) &&
                        Math.Abs(group.X - left) < 0.5 && Math.Abs(group.Width - sectionWidth) < 0.5 &&
                        Math.Abs(group.Y + group.Height - sectionTop) < 0.5);
                    bool joined = project != null && _groupPeekVisuals.ContainsKey(project.Id);
                    if (joined)
                    {
                        GroupPeekVisualRegistration registration = _groupPeekVisuals[project!.Id];
                        _joinedLightDesktopProject = registration.Container;
                        _joinedLightDesktopProject.CornerRadius = new CornerRadius(11, 11, 0, 0);
                        // Outer 的固定尺寸外还有边框；只调整底板，保持图标和入口坐标不变。
                        Thickness border = registration.Container.BorderThickness;
                        sectionBorder = new Thickness(border.Left, 0, border.Right, border.Bottom);
                        sectionBorderBrush = registration.Container.BorderBrush;
                        // 底板略托入底部留白，避免缩放取整后透明边框透出壁纸。
                        sectionTop = project.Y + registration.Outer.Height;
                    }
                    var section = new Border { Width = sectionWidth, Height = bottom - sectionTop,
                        Background = LightDesktopSurfaceBrush,
                        BorderThickness = sectionBorder, BorderBrush = sectionBorderBrush,
                        CornerRadius = joined ? new CornerRadius(0, 0, 11, 11) : new CornerRadius(11),
                        IsHitTestVisible = false, SnapsToDevicePixels = true };
                    if (joined)
                    {
                        // ActualWidth 包含 DPI 取整后的边框，不能用两个名义上的 1 DIP 推算。
                        section.SetBinding(WidthProperty, new System.Windows.Data.Binding(nameof(ActualWidth))
                            { Source = _joinedLightDesktopProject });
                        section.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding(nameof(Border.BorderBrush))
                            { Source = _joinedLightDesktopProject });
                    }
                    var heading = new TextBlock { Text = "其他分类", FontSize = 10,
                        Foreground = WarmPaperTheme.SecondaryTextBrush,
                        Margin = new Thickness(11 - sectionBorder.Left, 5, 0, 0) };
                    section.Child = heading;
                    Canvas.SetLeft(section, left); Canvas.SetTop(section, sectionTop);
                    Panel.SetZIndex(section, GroupNormalZIndex - 1);
                    _lightDesktopDecorations.Add(section);
                    IconCanvas.Children.Add(section);
                }
            }
        }

        private void ToggleLightDesktopDrawer(GroupInfo group)
        {
            if (_lightDesktopDrawerId == group.Id) StopGroupPeek();
            else OpenLightDesktopDrawer(group);
        }

        private void OpenLightDesktopDrawer(GroupInfo group)
        {
            StopGroupPeek();
            if (_organizerPaused || !_groupPeekVisuals.ContainsKey(group.Id)) return;
            _lightDesktopDrawerId = group.Id;
            _lightDesktopDrawerGeneration++;
            _activeGroupPeekId = group.Id;
            ApplyGroupPeekVisualState(group.Id);
            StatusText.Text = $"已展开“{group.Name}”；入口位置不变，点击入口或按 Esc 关闭";
        }

        private void ApplyLightDesktopDrawerPresentation(GroupPeekVisualRegistration registration, bool isOpen)
        {
            GroupInfo group = registration.Group;
            registration.Container.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            if (!isOpen) return;
            Rect work = GetMonitorForItemRect(GetGroupBounds(group)).WorkArea;
            double height = Math.Min(group.Height, work.Height - 16);
            double right = _appLayout.Groups.Where(candidate => candidate.DesktopRole != DesktopZoneRole.None &&
                work.Contains(new Point(candidate.X, candidate.Y))).Max(candidate => GetGroupBounds(candidate).Right);
            double x = Math.Clamp(right + 12, work.Left + 8, Math.Max(work.Left + 8, work.Right - group.Width - 8));
            double y = Math.Clamp(group.Y, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - height - 8));
            registration.Outer.Width = group.Width;
            registration.Outer.Height = height;
            registration.Body.Visibility = Visibility.Visible;
            registration.Header.CornerRadius = new CornerRadius(11, 11, 0, 0);
            registration.Container.Background = LightDesktopSurfaceBrush;
            Canvas.SetLeft(registration.Container, x);
            Canvas.SetTop(registration.Container, y);
            Panel.SetZIndex(registration.Container, GroupPeekZIndex);
        }

        private void DismissLightDesktopDrawerFromPointer(DependencyObject? source)
        {
            if (_lightDesktopDrawerId is not string id || source == null) return;
            if (_groupPeekVisuals.TryGetValue(id, out GroupPeekVisualRegistration? registration) &&
                IsDescendantOf(source, registration.Container)) return;
            if (_lightDesktopEntries.TryGetValue(id, out Border? anchor) && IsDescendantOf(source, anchor)) return;
            StopGroupPeek();
        }

        private bool IsCoveredByLightDesktopDrawer(FrameworkElement visual, Point canvasPoint) =>
            _lightDesktopDrawerId is string id &&
            _groupPeekVisuals.TryGetValue(id, out GroupPeekVisualRegistration? registration) &&
            registration.Container.IsVisible &&
            TryGetElementBoundsOnCanvas(registration.Container, out Rect bounds) && bounds.Contains(canvasPoint) &&
            !IsDescendantOf(visual, registration.Container);

        private void DismissLightDesktopDrawerFromNativePointer(IntPtr clickedWindow)
        {
            if (_lightDesktopDrawerId is not string id || clickedWindow == _hwndSource?.Handle ||
                _groupPeekChildMenuGroupId != null) return;
            if (_groupPeekVisuals.TryGetValue(id, out GroupPeekVisualRegistration? registration) &&
                IsGroupMenuWindow(registration.HeaderMenu, clickedWindow,
                    visual => (PresentationSource.FromVisual(visual) as HwndSource)?.Handle ?? IntPtr.Zero)) return;
            long generation = _lightDesktopDrawerGeneration;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (!_isClosing && _lightDesktopDrawerId == id && generation == _lightDesktopDrawerGeneration)
                    StopGroupPeek();
            }));
        }
    }
}
