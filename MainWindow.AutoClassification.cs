// 自动分类
// 本文件由 v1.12 完整功能重构拆分；行为逻辑保持自 v1.11.6 不变。
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private void NewGroupButton_Click(object sender, RoutedEventArgs e)
        {
            SimpleInputDialog dialog = CreateInputDialog("请输入分组名称：", "新分组");
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ResultText))
            {
                return;
            }

            AddManualGroup(dialog.ResultText.Trim());
        }

        private void AddManualGroup(string name)
        {
            _appLayout.IsEditMode = true;
            EditModeToggle.IsChecked = true;
            EditModeToggle.Content = "完成编辑";
            UpdatePushReflowAvailability();

            var group = new GroupInfo
            {
                Name = name,
                IsSizeLocked = false
            };
            AutoFitGroup(group, clampPosition: false);

            Point position = FindAvailableAutoGroupPosition(group.Width, GetGroupDisplayHeight(group));
            group.X = position.X;
            group.Y = position.Y;
            ClampGroupToCanvas(group);
            _appLayout.Groups.Add(group);
            _lastSmartLayoutSnapshot = null;
            UndoSmartLayoutButton.IsEnabled = false;
            RebuildDesktopIconsAndSaveLayout();
        }

        // ==================== 自动识别分类 ====================

        private async void AutoClassifyButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            RecoverStaleInteractionState();
            if (_autoClassifyInProgress)
            {
                StatusText.Text = "正在识别分类，请稍候";
                return;
            }

            if (_draggedElement != null)
            {
                StatusText.Text = "请先结束当前拖动，再执行自动分类";
                return;
            }

            _autoClassifyInProgress = true;
            AutoClassifyButton.IsEnabled = false;
            StatusText.Text = "正在后台识别桌面项目…";
            try
            {
                DesktopScanSnapshot snapshot = await Task.Run(
                    () => ScanDesktopSnapshot(_lifetimeCts.Token),
                    _lifetimeCts.Token);

                if (_isClosing)
                {
                    return;
                }

                if (!snapshot.PhysicalScanComplete || !snapshot.ShellScanComplete)
                {
                    StatusText.Text = "桌面扫描暂时不完整，未修改现有布局，请稍后重试";
                    return;
                }

                Dictionary<string, string> existing = snapshot.Items;
                var manualGroupedNames = new HashSet<string>(
                    _appLayout.Groups
                        .SelectMany(group => group.IsAutoCategory
                            ? group.ManuallyAssignedItemNames
                            : group.ItemNames),
                    StringComparer.OrdinalIgnoreCase);

                List<string> candidateNames = existing.Keys
                    .Where(name => !manualGroupedNames.Contains(name))
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                if (candidateNames.Count == 0)
                {
                    StatusText.Text = "没有可自动分类的项目；手工分组中的项目不会被改动";
                    return;
                }

                Dictionary<string, DesktopCategoryDefinition> effectiveCategories =
                    AutoClassificationCategoryPolicy.BuildEffectiveLookup(
                        snapshot.Categories,
                        snapshot.ReliableCategoryNames,
                        _appLayout.Groups);
                Dictionary<DesktopCategoryDefinition, List<string>> plan = BuildAutoClassificationPlan(
                    existing,
                    candidateNames,
                    effectiveCategories);

                var preview = new StringBuilder();
                preview.AppendLine($"识别到 {candidateNames.Count} 个可分类项目：");
                preview.AppendLine();
                foreach ((DesktopCategoryDefinition category, List<string> names) in plan
                             .OrderBy(pair => pair.Key.Order))
                {
                    preview.AppendLine($"• {category.DisplayName}：{names.Count} 项");
                }

                preview.AppendLine();
                preview.AppendLine("只会创建虚拟分组和改变图标布局，不会移动、重命名或删除真实文件。");
                preview.AppendLine("现有手工分组保持不变。是否应用？");

                MessageBoxResult result = MessageBox.Show(
                    preview.ToString(),
                    "自动识别分类预览",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    StatusText.Text = "已取消自动分类";
                    return;
                }

                _desktopItems = snapshot.Items;
                _desktopCategories = snapshot.Categories;
                _reliableDesktopCategoryNames = snapshot.ReliableCategoryNames;
                _desktopSnapshotInitialized = true;
                ApplyAutoClassification(plan, existing);
            }
            catch (OperationCanceledException) when (_isClosing || _lifetimeCts.IsCancellationRequested)
            {
                // 正常退出。
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Auto classification failed: {exception}");
                StatusText.Text = "自动分类失败，可稍后重试";
            }
            finally
            {
                _autoClassifyInProgress = false;
                if (!_isClosing)
                {
                    AutoClassifyButton.IsEnabled = true;
                }
            }
        }

        private void AutoClassifyNewItemsToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_isSafeModeActive)
            {
                AutoClassifyNewItemsToggle.IsChecked = _appLayout.AutoClassifyNewItems;
                StatusText.Text = "安全模式下新项目自动归类已暂停，原设置保持不变";
                return;
            }

            _appLayout.AutoClassifyNewItems = AutoClassifyNewItemsToggle.IsChecked == true;
            SaveLayout();
            StatusText.Text = _appLayout.AutoClassifyNewItems
                ? "新项目自动归类已开启：仅处理之后出现在桌面上的新项目"
                : "新项目自动归类已关闭";
        }

        private void ClearAutoClassificationButton_Click(object sender, RoutedEventArgs e)
        {
            if (HasPendingFileOperations)
            {
                StatusText.Text = "后台真实文件任务完成后再取消分类";
                return;
            }

            List<GroupInfo> autoGroups = _appLayout.Groups
                .Where(group => group.IsAutoCategory)
                .ToList();

            if (autoGroups.Count == 0)
            {
                StatusText.Text = "当前没有自动分类分组";
                return;
            }

            int itemCount = autoGroups
                .SelectMany(group => group.ItemNames)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            MessageBoxResult result = MessageBox.Show(
                $"取消 {autoGroups.Count} 个自动分类，并把其中 {itemCount} 个项目恢复为自由图标？\n\n" +
                "手工分组不会被改动，真实文件也不会移动。",
                "取消自动分类",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            ClearAutoClassificationAfterConfirmation(autoGroups);
        }

        private void ClearAutoClassificationAfterConfirmation(
            IReadOnlyCollection<GroupInfo> autoGroups)
        {
            var existing = new Dictionary<string, string>(_desktopItems, StringComparer.OrdinalIgnoreCase);
            var manualGroupedNames = new HashSet<string>(
                _appLayout.Groups
                    .Where(group => !group.IsAutoCategory)
                    .SelectMany(group => group.ItemNames),
                StringComparer.OrdinalIgnoreCase);

            var preferredPositions = new Dictionary<string, IconPosition>(StringComparer.OrdinalIgnoreCase);
            foreach (GroupInfo group in autoGroups)
            {
                for (int i = 0; i < group.ItemNames.Count; i++)
                {
                    string name = group.ItemNames[i];
                    if (_appLayout.AutoClassificationOriginalPositions.TryGetValue(name, out IconPosition? saved) &&
                        saved != null)
                    {
                        preferredPositions[name] = ClonePosition(saved);
                    }
                    else
                    {
                        preferredPositions[name] = new IconPosition
                        {
                            X = group.X + (i % 3) * IconCellWidth,
                            Y = group.Y + 36 + (i / 3) * IconCellHeight
                        };
                    }
                }
            }

            var restoreRequests = new List<(string Name, IconPosition Requested)>();
            foreach ((string name, IconPosition requested) in preferredPositions
                         .OrderBy(pair => SafeCanvasCoordinate(pair.Value.Y))
                         .ThenBy(pair => SafeCanvasCoordinate(pair.Value.X))
                         .ThenBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!existing.ContainsKey(name) || manualGroupedNames.Contains(name))
                {
                    continue;
                }

                restoreRequests.Add((name, ClonePosition(requested)));
            }

            var restoringNames = new HashSet<string>(
                restoreRequests.Select(request => request.Name),
                StringComparer.OrdinalIgnoreCase);
            var ignoredGroupIds = new HashSet<string>(
                autoGroups.Select(group => group.Id),
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, IconPosition>? plan;
            if (restoreRequests.Count == 0)
            {
                plan = new Dictionary<string, IconPosition>(StringComparer.OrdinalIgnoreCase);
            }
            else if (_appLayout.SnapToGrid)
            {
                List<(string Name, IconPosition Requested)> alignedRequests = restoreRequests
                    .Select(request =>
                    {
                        IconPosition requested = ClonePosition(request.Requested);
                        ClampIconPosition(requested);
                        return (request.Name, requested);
                    })
                    .ToList();
                plan = TryPlanAlignedIconPositions(
                    alignedRequests,
                    GetOccupiedFreeGridCells(restoringNames),
                    ignoredGroupIds);
            }
            else
            {
                plan = TryPlanFreeIconPositions(
                    restoreRequests,
                    restoringNames,
                    ignoredGroupIds: ignoredGroupIds);
            }

            if (plan == null || plan.Count != restoreRequests.Count)
            {
                StatusText.Text = _appLayout.SnapToGrid
                    ? "没有足够的可用网格，自动分类保持不变"
                    : "没有足够的可用位置，自动分类保持不变";
                return;
            }

            CommitClearAutoClassification(autoGroups, plan);
        }

        private void CommitClearAutoClassification(
            IReadOnlyCollection<GroupInfo> autoGroups,
            IReadOnlyDictionary<string, IconPosition> restoredPositions)
        {
            _canceledAutoCategoryGroupIds.UnionWith(autoGroups.Select(group => group.Id));
            _canceledAutoCategoryGroupIds.UnionWith(_fileMoveHistory
                .Where(record => record.SourceGroupSnapshot?.IsAutoCategory == true)
                .Select(record => record.SourceGroupId)
                .OfType<string>());
            _appLayout.Groups.RemoveAll(group => group.IsAutoCategory);
            foreach ((string name, IconPosition position) in restoredPositions)
            {
                _appLayout.FreeIcons[name] = position;
            }

            _appLayout.AutoClassificationOriginalPositions.Clear();
            _lastSmartLayoutSnapshot = null;
            UndoSmartLayoutButton.IsEnabled = false;
            RebuildDesktopIconsAndSaveLayout();
            StatusText.Text = $"已取消自动分类，恢复 {restoredPositions.Count} 个自由图标";
        }

        private Dictionary<DesktopCategoryDefinition, List<string>> BuildAutoClassificationPlan(
            Dictionary<string, string> existing,
            IEnumerable<string> names,
            IReadOnlyDictionary<string, DesktopCategoryDefinition>? categoryLookup = null)
        {
            var plan = new Dictionary<DesktopCategoryDefinition, List<string>>();
            foreach (string name in names)
            {
                if (!existing.TryGetValue(name, out string? fullPath))
                {
                    continue;
                }

                DesktopCategoryDefinition category =
                    categoryLookup != null &&
                    categoryLookup.TryGetValue(name, out DesktopCategoryDefinition? cachedCategory) &&
                    cachedCategory != null
                        ? cachedCategory
                        : DesktopCategoryClassifier.Classify(fullPath);
                if (!plan.TryGetValue(category, out List<string>? categoryItems))
                {
                    categoryItems = new List<string>();
                    plan[category] = categoryItems;
                }

                categoryItems.Add(name);
            }

            foreach (List<string> categoryItems in plan.Values)
            {
                categoryItems.Sort(StringComparer.CurrentCultureIgnoreCase);
            }

            return plan;
        }

        private void ApplyAutoClassification(
            Dictionary<DesktopCategoryDefinition, List<string>> plan,
            Dictionary<string, string> existing)
        {
            WorkspaceLayoutState groupStateBefore = WorkspaceLayoutManager.Capture(_appLayout);
            List<GroupInfo> previousGroups = _appLayout.Groups
                .Where(group => group.IsAutoCategory && !string.IsNullOrWhiteSpace(group.AutoCategoryKey))
                .ToList();
            var previousAutoGroups = previousGroups
                .GroupBy(group => group.AutoCategoryKey!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
            var manualAssignmentsByCategory = previousGroups
                .GroupBy(group => group.AutoCategoryKey!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    grouping => grouping.Key,
                    grouping => grouping
                        .SelectMany(group => group.ItemNames.Where(name =>
                            group.ManuallyAssignedItemNames.Contains(
                                name,
                                StringComparer.OrdinalIgnoreCase)))
                        .Where(existing.ContainsKey)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var candidateNames = new HashSet<string>(
                plan.SelectMany(pair => pair.Value),
                StringComparer.OrdinalIgnoreCase);

            foreach (string name in candidateNames)
            {
                if (_appLayout.FreeIcons.TryGetValue(name, out IconPosition? current) &&
                    current != null &&
                    !_appLayout.AutoClassificationOriginalPositions.ContainsKey(name))
                {
                    _appLayout.AutoClassificationOriginalPositions[name] = ClonePosition(current);
                }
            }

            _appLayout.Groups.RemoveAll(group => group.IsAutoCategory);
            foreach (string name in candidateNames)
            {
                _appLayout.FreeIcons.Remove(name);
            }

            var rebuiltCategoryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach ((DesktopCategoryDefinition category, List<string> names) in plan
                         .OrderBy(pair => pair.Key.Order))
            {
                previousAutoGroups.TryGetValue(category.Key, out GroupInfo? previous);
                List<string> manualAssignments = manualAssignmentsByCategory.TryGetValue(
                    category.Key,
                    out List<string>? preservedManualAssignments)
                    ? preservedManualAssignments
                    : new List<string>();
                List<string> plannedNames = names
                    .Where(existing.ContainsKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (plannedNames.Count == 0 && manualAssignments.Count == 0)
                {
                    continue;
                }

                GroupInfo group;
                if (previous != null)
                {
                    var retainedNames = new HashSet<string>(
                        plannedNames.Concat(manualAssignments),
                        StringComparer.OrdinalIgnoreCase);
                    List<string> combinedNames = previous.SortMode == GroupSortMode.Custom
                        ? previous.ItemNames
                            .Where(retainedNames.Contains)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList()
                        : retainedNames
                            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                            .ToList();
                    foreach (string name in manualAssignments.Concat(plannedNames))
                    {
                        if (!combinedNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        {
                            combinedNames.Add(name);
                        }
                    }

                    group = new GroupInfo
                    {
                        Id = previous.Id,
                        Name = string.IsNullOrWhiteSpace(previous.Name) ? category.DisplayName : previous.Name,
                        X = previous.X,
                        Y = previous.Y,
                        Width = previous.Width,
                        Height = previous.Height,
                        IsCollapsed = previous.IsCollapsed,
                        IsSizeLocked = previous.IsSizeLocked,
                        UseUniformTrackWidth = previous.UseUniformTrackWidth,
                        DesktopRole = previous.DesktopRole,
                        SortMode = previous.SortMode,
                        IsAutoCategory = true,
                        AutoCategoryKey = category.Key,
                        ItemNames = combinedNames,
                        ManuallyAssignedItemNames = manualAssignments
                    };
                    ClampGroupToCanvas(group);
                }
                else
                {
                    group = CreateAutoCategoryGroup(category, plannedNames);
                }

                UpdateAutoCategoryGroupSize(group);
                _appLayout.Groups.Add(group);
                rebuiltCategoryKeys.Add(category.Key);
            }

            foreach ((string categoryKey, GroupInfo previous) in previousAutoGroups)
            {
                if (rebuiltCategoryKeys.Contains(categoryKey) ||
                    !manualAssignmentsByCategory.TryGetValue(
                        categoryKey,
                        out List<string>? manualAssignments) ||
                    manualAssignments.Count == 0)
                {
                    continue;
                }

                var manualSet = new HashSet<string>(
                    manualAssignments,
                    StringComparer.OrdinalIgnoreCase);
                previous.ItemNames = previous.ItemNames
                    .Where(manualSet.Contains)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (string name in manualAssignments)
                {
                    if (!previous.ItemNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        previous.ItemNames.Add(name);
                    }
                }
                previous.ManuallyAssignedItemNames = manualAssignments;
                UpdateAutoCategoryGroupSize(previous);
                _appLayout.Groups.Add(previous);
            }

            RebuildDesktopIconsAndSaveLayout();
            if (_lastSmartLayoutSnapshot != null &&
                HasGroupStateChanged(groupStateBefore, _appLayout.Groups))
            {
                _lastSmartLayoutSnapshot = null;
                UndoSmartLayoutButton.IsEnabled = false;
            }
            StatusText.Text = $"已自动分类 {candidateNames.Count} 个项目，共 {plan.Count} 类；真实文件未移动";
        }

        private bool AutoClassifyNewDesktopItems(
            Dictionary<string, string> existing,
            IReadOnlyDictionary<string, DesktopCategoryDefinition> categories,
            IReadOnlySet<string> newItemCandidates)
        {
            if (!IsAutoClassificationActive)
            {
                return false;
            }

            var groupedNames = new HashSet<string>(
                _appLayout.Groups.SelectMany(group => group.ItemNames),
                StringComparer.OrdinalIgnoreCase);

            List<string> newNames = newItemCandidates
                .Where(name => existing.ContainsKey(name))
                .Where(name => !groupedNames.Contains(name) && !_appLayout.FreeIcons.ContainsKey(name))
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (newNames.Count == 0)
            {
                return false;
            }

            Dictionary<DesktopCategoryDefinition, List<string>> plan = BuildAutoClassificationPlan(existing, newNames, categories);
            foreach ((DesktopCategoryDefinition category, List<string> names) in plan
                         .OrderBy(pair => pair.Key.Order))
            {
                GroupInfo? group = _appLayout.Groups.LastOrDefault(candidate =>
                    candidate.IsAutoCategory &&
                    string.Equals(candidate.AutoCategoryKey, category.Key, StringComparison.OrdinalIgnoreCase));

                if (group == null)
                {
                    group = CreateAutoCategoryGroup(category, Array.Empty<string>());
                    _appLayout.Groups.Add(group);
                }

                foreach (string name in names)
                {
                    if (!group.ItemNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        group.ItemNames.Add(name);
                    }
                }

                IEnumerable<string> normalizedNames = group.ItemNames
                    .Where(existing.ContainsKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                group.ItemNames = group.SortMode == GroupSortMode.Custom
                    ? normalizedNames.ToList()
                    : normalizedNames
                        .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                UpdateAutoCategoryGroupSize(group);
            }

            return true;
        }

        private bool ReconcileAutoCategoryMembership(
            Dictionary<string, string> existing,
            IReadOnlyDictionary<string, DesktopCategoryDefinition> categories,
            IReadOnlySet<string> reliableCategoryNames)
        {
            IReadOnlyList<AutoCategoryMembershipMove> moves =
                AutoCategoryMembershipPlanner.Plan(
                    _appLayout.Groups,
                    categories,
                    reliableCategoryNames,
                    paused: _isSafeModeActive);
            if (moves.Count == 0)
            {
                return false;
            }

            AutoCategoryMembershipUpdateResult update = AutoCategoryMembershipPlanner.Apply(
                _appLayout.Groups,
                moves,
                existing.ContainsKey,
                category => CreateAutoCategoryGroup(category, Array.Empty<string>()));
            foreach (GroupInfo group in update.AffectedGroups)
            {
                UpdateAutoCategoryGroupSize(group);
            }

            return update.Changed;
        }

        private GroupInfo CreateAutoCategoryGroup(
            DesktopCategoryDefinition category,
            IEnumerable<string> itemNames)
        {
            var group = new GroupInfo
            {
                Name = category.DisplayName,
                Width = 280,
                Height = 200,
                IsAutoCategory = true,
                AutoCategoryKey = category.Key,
                SortMode = GroupSortMode.Name,
                ItemNames = itemNames
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
            };

            // 自动分类数量较多时默认收起，避免首次分类后立即铺满整个桌面。
            group.IsCollapsed = group.ItemNames.Count > 4;
            UpdateAutoCategoryGroupSize(group);
            Point position = FindAvailableAutoGroupPosition(group.Width, GetGroupDisplayHeight(group));
            group.X = position.X;
            group.Y = position.Y;
            ClampGroupToCanvas(group);
            return group;
        }

        private void UpdateAutoCategoryGroupSize(GroupInfo group)
        {
            if (!group.IsSizeLocked)
            {
                AutoFitGroup(group);
            }
            else
            {
                ClampGroupToCanvas(group);
            }
        }

        private Point FindAvailableAutoGroupPosition(double width, double height)
        {
            const double margin = 14;
            const double step = 36;

            foreach (DesktopMonitorRegion monitor in _desktopGeometry.Monitors
                         .OrderByDescending(item => item.IsPrimary))
            {
                Rect workArea = monitor.WorkArea;
                double startX = workArea.Left + 20;
                double startY = workArea.Top + (monitor.IsPrimary ? 112 : 20);
                for (double y = startY; y + height <= workArea.Bottom; y += step)
                {
                    for (double x = startX; x + width <= workArea.Right; x += step)
                    {
                        var candidate = new Rect(
                            x - margin,
                            y - margin,
                            width + margin * 2,
                            height + margin * 2);
                        bool overlaps = _appLayout.Groups.Any(group =>
                            candidate.IntersectsWith(GetGroupBounds(group)));
                        overlaps |= IntersectsFolderPortal(candidate);
                        Rect? recycleObstacle = GetRecycleBinWidgetObstacle();
                        overlaps |= recycleObstacle.HasValue && candidate.IntersectsWith(recycleObstacle.Value);
                        if (!overlaps && IsRectInsideUsableDesktop(new Rect(x, y, width, height)))
                        {
                            return new Point(x, y);
                        }
                    }
                }
            }

            int index = _appLayout.Groups.Count(group => group.IsAutoCategory);
            Rect primary = GetPrimaryWorkArea();
            Point fallback = new(
                primary.Left + 20 + (index % 8) * 24,
                primary.Top + 112 + (index % 8) * 24);
            return ClampRectToUsableDesktop(fallback.X, fallback.Y, width, height);
        }

        private void UpdateAutoClassificationControls()
        {
            AutoClassifyNewItemsToggle.IsChecked = _appLayout.AutoClassifyNewItems;
            AutoClassifyNewItemsToggle.IsEnabled = !_isSafeModeActive;
            AutoClassifyNewItemsToggle.ToolTip = _isSafeModeActive
                ? "安全模式下已暂停；退出后恢复原设置"
                : "之后出现的新桌面项目自动进入对应分类";
            bool hasAutoGroups = _appLayout.Groups.Any(group => group.IsAutoCategory);
            ClearAutoClassificationButton.IsEnabled = hasAutoGroups && !HasPendingFileOperations;
            ClearAutoClassificationButton.ToolTip = !hasAutoGroups
                ? "当前没有自动分类分组"
                : HasPendingFileOperations
                    ? "后台真实文件任务完成后才能取消分类"
                    : "解散全部自动分类并尽量恢复分类前的图标位置；手工分组不受影响";
        }

        // ==================== 桌面扫描与刷新 ====================

        private Dictionary<string, string> ScanDesktopItems(
            CancellationToken cancellationToken,
            out bool physicalScanComplete,
            out bool shellScanComplete)
        {
            var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // 公共桌面先加入；同名时用户桌面覆盖公共桌面，更符合 Explorer 的显示逻辑。
            // 每个目录必须完整枚举成功后才提交，防止瞬时 I/O 异常产生半份快照。
            bool commonDesktopComplete = AddDesktopPathItems(
                _commonDesktopPath,
                existing,
                cancellationToken);
            bool userDesktopComplete = AddDesktopPathItems(
                _userDesktopPath,
                existing,
                cancellationToken);
            physicalScanComplete = commonDesktopComplete && userDesktopComplete;

            // v15 起系统回收站由独立小组件负责，不再进入普通图标、分组、
            // 自动分类和挤压排列链路。其他 Shell 虚拟项目也不显示，因此无需
            // 每轮刷新枚举整个桌面 Shell 命名空间。
            shellScanComplete = true;

            return existing;
        }



    }
}
