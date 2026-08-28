// 桌面扫描、Watcher 与刷新
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private DesktopScanSnapshot ScanDesktopSnapshot(CancellationToken cancellationToken)
        {
            Dictionary<string, string> items = ScanDesktopItems(
                cancellationToken,
                out bool physicalScanComplete,
                out bool shellScanComplete);
            var categories = new Dictionary<string, DesktopCategoryDefinition>(StringComparer.OrdinalIgnoreCase);
            var reliableCategoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var identities = new Dictionary<string, DesktopItemIdentityInfo>(StringComparer.OrdinalIgnoreCase);

            foreach ((string name, string fullPath) in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ShellItemLocation.TryDecode(fullPath, out string parsingName, out bool isFolder))
                {
                    categories[name] = DesktopCategoryClassifier.ClassifyShellNamespace();
                    reliableCategoryNames.Add(name);
                    identities[name] = CreateShellDesktopItemIdentity(fullPath, parsingName, isFolder);
                }
                else
                {
                    DesktopCategoryClassification classification =
                        DesktopCategoryClassifier.ClassifyWithReliability(fullPath);
                    categories[name] = classification.Category;
                    if (classification.IsReliable)
                    {
                        reliableCategoryNames.Add(name);
                    }

                    identities[name] = CreateDesktopItemIdentity(fullPath);
                }
            }

            return new DesktopScanSnapshot(
                items,
                categories,
                reliableCategoryNames,
                identities,
                physicalScanComplete,
                shellScanComplete);
        }

        private static bool DesktopSnapshotMatches(
            IReadOnlyDictionary<string, string> currentItems,
            IReadOnlyDictionary<string, DesktopCategoryDefinition> currentCategories,
            IReadOnlySet<string> currentReliableCategoryNames,
            DesktopScanSnapshot next)
        {
            if (currentItems.Count != next.Items.Count ||
                currentCategories.Count != next.Categories.Count ||
                currentReliableCategoryNames.Count != next.ReliableCategoryNames.Count)
            {
                return false;
            }

            foreach ((string name, string path) in next.Items)
            {
                string? currentActualName = currentItems.Keys.FirstOrDefault(currentName =>
                    currentName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (currentActualName == null ||
                    !string.Equals(currentActualName, name, StringComparison.Ordinal) ||
                    !currentItems.TryGetValue(name, out string? currentPath) ||
                    !ShellItemLocation.AreEquivalent(currentPath, path))
                {
                    return false;
                }

                if (!next.Categories.TryGetValue(name, out DesktopCategoryDefinition? nextCategory) ||
                    !currentCategories.TryGetValue(name, out DesktopCategoryDefinition? currentCategory) ||
                    !string.Equals(currentCategory.Key, nextCategory.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (currentReliableCategoryNames.Contains(name) !=
                    next.ReliableCategoryNames.Contains(name))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AddDesktopPathItems(
            string desktopPath,
            Dictionary<string, string> target,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(desktopPath))
            {
                return true;
            }

            if (!Directory.Exists(desktopPath))
            {
                return false;
            }

            // 先完整枚举到临时集合，再一次性提交。Directory.EnumerateFileSystemEntries
            // 可能在迭代中途抛出异常；旧实现会把半份目录内容当成有效快照，随后
            // 删除大量视觉，下一次成功扫描又全部加回，表现为整桌面持续闪烁。
            var stagedItems = new List<(string Name, string Path)>();
            try
            {
                foreach (string path in Directory.EnumerateFileSystemEntries(desktopPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string name = Path.GetFileName(path);
                    if (string.IsNullOrWhiteSpace(name) ||
                        name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    stagedItems.Add((name, path));
                }
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException)
            {
                // 任何目录读取失败都不能被当作完整空目录，否则对应项目会
                // 整批消失后重新出现。调用方保留上一份完整快照并稍后重试。
                return false;
            }

            foreach ((string name, string path) in stagedItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                target[name] = path;
            }

            return true;
        }

        private void RebuildDesktopIconsAndSaveLayout()
        {
            RebuildDesktopIcons();
            SaveLayout();
        }

        private void RebuildDesktopIcons(IReadOnlySet<string>? newItemCandidates = null)
        {
            if (_isClosing)
            {
                return;
            }

            if (newItemCandidates != null)
            {
                _pendingAutoClassificationCandidates.UnionWith(newItemCandidates);
            }

            if (_isRebuildingVisualTree)
            {
                _rebuildRequested = true;
                return;
            }

            _isRebuildingVisualTree = true;
            Stopwatch stopwatch = Stopwatch.StartNew();
            int passCount = 0;
            try
            {
                do
                {
                    _rebuildRequested = false;
                    var candidatesForPass = new HashSet<string>(
                        _pendingAutoClassificationCandidates,
                        StringComparer.OrdinalIgnoreCase);
                    _pendingAutoClassificationCandidates.Clear();
                    RebuildDesktopIconsCore(candidatesForPass);
                    passCount++;
                }
                while (_rebuildRequested && !_isClosing && passCount < 2);
            }
            finally
            {
                _isRebuildingVisualTree = false;
                stopwatch.Stop();
                _diagnostics.LogSlowOperation(
                    "visual-rebuild",
                    stopwatch.Elapsed,
                    _desktopItems.Count);
                if (stopwatch.ElapsedMilliseconds >= 500)
                {
                    Debug.WriteLine($"Desktop visual rebuild took {stopwatch.ElapsedMilliseconds} ms in {passCount} pass(es).");
                }
            }

            // 防止意外的属性/事件重入形成无界 do/while。超过两轮时把剩余工作
            // 合并为一个低优先级 Dispatcher 项，让输入消息先得到处理。
            if (_rebuildRequested && !_isClosing &&
                Interlocked.Exchange(ref _visualRebuildQueued, 1) == 0)
            {
                _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                {
                    Interlocked.Exchange(ref _visualRebuildQueued, 0);
                    if (!_isClosing)
                    {
                        RebuildDesktopIcons();
                    }
                }));
            }
        }

        private void RebuildDesktopIconsCore(IReadOnlySet<string> newItemCandidates)
        {
            if (_isClosing)
            {
                return;
            }

            ClearPhysicalFolderDropPreview();
            ClearGroupDropPreview();
            NormalizeLayout();

            var existing = new Dictionary<string, string>(_desktopItems, StringComparer.OrdinalIgnoreCase);
            _selectedItemNames.RemoveWhere(name => !existing.ContainsKey(name));
            bool layoutChanged = RemoveMissingAndDuplicateGroupItems(existing);
            layoutChanged |= ReconcileAutoCategoryMembership(
                existing,
                _desktopCategories,
                _reliableDesktopCategoryNames);
            layoutChanged |= AutoClassifyNewDesktopItems(
                existing,
                _desktopCategories,
                newItemCandidates);

            var groupedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var targetGroupFingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (GroupInfo group in _appLayout.Groups)
            {
                foreach (string itemName in group.ItemNames)
                {
                    groupedNames.Add(itemName);
                }

                if (!group.IsSizeLocked)
                {
                    layoutChanged |= AutoFitGroup(group, clampPosition: false);
                }

                ClampGroupToCanvas(group);
                targetGroupFingerprints[group.Id] = BuildGroupVisualFingerprint(group, existing);
            }

            foreach (FolderPortalInfo portal in _appLayout.FolderPortals)
            {
                double oldX = portal.X;
                double oldY = portal.Y;
                ClampFolderPortalToCanvas(portal);
                layoutChanged |= Math.Abs(oldX - portal.X) > 0.01 ||
                                 Math.Abs(oldY - portal.Y) > 0.01;
            }

            int columns = GetGridColumnCount();
            var occupiedGridCells = new HashSet<(int Column, int Row)>();
            var freeTargets = new Dictionary<string, FreeIconVisualTarget>(StringComparer.OrdinalIgnoreCase);
            List<(string Name, string FullPath)> orderedFreeItems = existing
                .Where(pair => !groupedNames.Contains(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(pair => (pair.Key, pair.Value))
                .ToList();
            int index = 0;
            int gridOverflowCount = 0;

            // 必须先登记所有已有位置，再给新项目找空位；否则名称排序靠前的新项目可能
            // 在名称靠后的已有图标登记前抢占其网格并把重叠结果持久化。
            foreach ((string name, _) in orderedFreeItems)
            {
                if (!_appLayout.FreeIcons.TryGetValue(name, out IconPosition? savedPosition) ||
                    savedPosition == null)
                {
                    continue;
                }

                ClampIconPosition(savedPosition);
                occupiedGridCells.Add(GetNearestGridCell(savedPosition.X, savedPosition.Y));
            }

            var newItemRequests = new List<GridPlacementRequest>();
            for (int itemIndex = 0; itemIndex < orderedFreeItems.Count; itemIndex++)
            {
                string name = orderedFreeItems[itemIndex].Name;
                if (_appLayout.FreeIcons.TryGetValue(name, out IconPosition? savedPosition) &&
                    savedPosition != null)
                {
                    continue;
                }

                newItemRequests.Add(new GridPlacementRequest(
                    name,
                    itemIndex % columns,
                    itemIndex / columns));
            }

            Dictionary<string, GridCell?> newItemPlacements = GridRefreshPlacementPlanner.Plan(
                columns,
                GetGridRowCount(),
                newItemRequests,
                occupiedGridCells.Select(cell => new GridCell(cell.Column, cell.Row)),
                (column, row) =>
                    !GridCellIntersectsGroup(column, row) &&
                    GridCellFitsUsableDesktop(column, row),
                StringComparer.OrdinalIgnoreCase);

            foreach ((string name, string fullPath) in orderedFreeItems)
            {
                IconPosition position;
                if (_appLayout.FreeIcons.TryGetValue(name, out IconPosition? savedPosition) && savedPosition != null)
                {
                    position = savedPosition;
                    ClampIconPosition(position);
                }
                else
                {
                    GridCell? available = newItemPlacements[name];
                    if (available.HasValue)
                    {
                        position = GridCellToPosition(
                            available.Value.Column,
                            available.Value.Row);
                        _appLayout.FreeIcons[name] = position;
                        layoutChanged = true;
                    }
                    else
                    {
                        // 网格已满时仍显示真实桌面项目，但不把已占用或无效网格冒充为空位持久化。
                        position = CreateTemporaryGridOverflowPosition(index, gridOverflowCount);
                        gridOverflowCount++;
                    }
                }

                ClampIconPosition(position);
                freeTargets[name] = new FreeIconVisualTarget(
                    fullPath,
                    position,
                    BuildFreeIconVisualState(name, fullPath));
                index++;
            }

            foreach (string key in _appLayout.FreeIcons.Keys.Where(k => !existing.ContainsKey(k)).ToList())
            {
                _appLayout.FreeIcons.Remove(key);
                layoutChanged = true;
            }

            foreach (string key in _appLayout.AutoClassificationOriginalPositions.Keys
                         .Where(k => !existing.ContainsKey(k))
                         .ToList())
            {
                _appLayout.AutoClassificationOriginalPositions.Remove(key);
                layoutChanged = true;
            }

            int removedVisuals = 0;
            foreach (string name in _freeIconVisuals.Keys.ToList())
            {
                bool reusable = freeTargets.TryGetValue(name, out FreeIconVisualTarget? target) &&
                    target is not null &&
                    _freeIconVisualStates.TryGetValue(name, out FreeIconVisualState currentState) &&
                    currentState == target.State &&
                    IsReusableFreeIconVisual(name, target.FullPath, _freeIconVisuals[name]);
                if (!reusable)
                {
                    RemoveFreeIconVisual(name);
                    removedVisuals++;
                }
            }

            var activeGroupIds = new HashSet<string>(
                _appLayout.Groups.Select(group => group.Id),
                StringComparer.OrdinalIgnoreCase);
            foreach (string groupId in _groupScrollOffsets.Keys
                         .Where(groupId => !activeGroupIds.Contains(groupId))
                         .ToList())
            {
                _groupScrollOffsets.Remove(groupId);
            }

            foreach (string groupId in _groupVisuals.Keys.ToList())
            {
                GroupInfo? targetGroup = _appLayout.Groups.FirstOrDefault(group =>
                    group.Id.Equals(groupId, StringComparison.OrdinalIgnoreCase));
                bool reusable = targetGroup != null &&
                    targetGroupFingerprints.TryGetValue(groupId, out string? targetFingerprint) &&
                    _groupVisualFingerprints.TryGetValue(groupId, out string? currentFingerprint) &&
                    string.Equals(currentFingerprint, targetFingerprint, StringComparison.Ordinal) &&
                    IsReusableGroupVisual(targetGroup, existing, _groupVisuals[groupId]);
                if (!reusable)
                {
                    RemoveGroupVisual(groupId);
                    removedVisuals++;
                }
            }

            EnsureFolderPortalVisuals();

            // 拖拽中断时，分组图标可能已经临时提升到 IconCanvas 顶层；常规刷新只应
            // 保留缓存字典登记的自由图标、分组卡片和只读 Portal，其余孤立视觉全部移除。
            var registeredTopLevelVisuals = new HashSet<FrameworkElement>(
                _freeIconVisuals.Values
                    .Concat(_groupVisuals.Values)
                    .Concat(GetFolderPortalVisuals()));
            foreach (FrameworkElement child in IconCanvas.Children.OfType<FrameworkElement>().ToList())
            {
                if (registeredTopLevelVisuals.Contains(child))
                {
                    continue;
                }

                IconCanvas.Children.Remove(child);
                UnregisterIconVisualTree(child);
                removedVisuals++;
            }

            int reusedGroups = 0;
            int createdGroups = 0;
            foreach (GroupInfo group in _appLayout.Groups)
            {
                FrameworkElement groupVisual;
                if (_groupVisuals.TryGetValue(group.Id, out FrameworkElement? cachedGroupVisual))
                {
                    groupVisual = cachedGroupVisual;
                    reusedGroups++;
                }
                else
                {
                    groupVisual = CreateGroupVisual(group, existing);
                    _groupVisuals[group.Id] = groupVisual;
                    _groupVisualFingerprints[group.Id] = targetGroupFingerprints[group.Id];
                    IconCanvas.Children.Add(groupVisual);
                    createdGroups++;
                }

                Canvas.SetLeft(groupVisual, group.X);
                Canvas.SetTop(groupVisual, group.Y);
                Panel.SetZIndex(groupVisual, 100);
            }

            int reusedFreeIcons = 0;
            int createdFreeIcons = 0;
            foreach ((string name, FreeIconVisualTarget target) in freeTargets)
            {
                FrameworkElement icon;
                if (_freeIconVisuals.TryGetValue(name, out FrameworkElement? cachedIcon))
                {
                    icon = cachedIcon;
                    reusedFreeIcons++;
                }
                else
                {
                    icon = CreateIconVisual(target.FullPath, name, parentGroup: null);
                    _freeIconVisuals[name] = icon;
                    _freeIconVisualStates[name] = target.State;
                    IconCanvas.Children.Add(icon);
                    createdFreeIcons++;
                }

                Canvas.SetLeft(icon, target.Position.X);
                Canvas.SetTop(icon, target.Position.Y);
                Panel.SetZIndex(icon, 200);
            }

            RefreshItemSelectionVisuals();
            string snapStatus = _appLayout.SnapToGrid ? "网格吸附开" : "网格吸附关";
            string pushStatus = !_appLayout.PushReflowEnabled || !_appLayout.SnapToGrid
                ? "挤压关"
                : _isSafeModeActive || !_appLayout.IsEditMode ? "挤压暂停" : "挤压开";
            int autoGroupCount = _appLayout.Groups.Count(group => group.IsAutoCategory);
            string autoStatus = !_appLayout.AutoClassifyNewItems
                ? "新项目归类关"
                : _isSafeModeActive ? "新项目归类暂停" : "新项目归类开";
            string desktopMode = _isAttachedToDesktop ? "Progman 底层模式" : "兼容底层模式";
            string editStatus = _appLayout.IsEditMode ? "编辑布局" : "布局已锁定";
            string safeStatus = _isSafeModeActive ? "安全模式" : "实时模式";
            string fileOperationStatus = HasPendingFileOperations
                ? $" · {_fileOperationCount} 个文件任务"
                : string.Empty;
            string gridOverflowStatus = gridOverflowCount > 0
                ? $" · {gridOverflowCount} 个项目等待空网格"
                : string.Empty;
            StatusText.Text =
                $"{existing.Count} 项 · {_appLayout.Groups.Count} 组 · {_appLayout.FolderPortals.Count} 个只读入口 · {editStatus}{fileOperationStatus}{gridOverflowStatus}";
            StatusText.ToolTip =
                $"{existing.Count} 个桌面项目；{_appLayout.Groups.Count} 个分组（自动 {autoGroupCount}）；" +
                $"{_appLayout.FolderPortals.Count} 个真实文件夹只读入口；" +
                $"{editStatus}；{safeStatus}；{snapStatus}；{pushStatus}；{autoStatus}；{desktopMode}；" +
                $"后台真实文件任务 {_fileOperationCount} 个；" +
                (gridOverflowCount > 0
                    ? $"网格已满，{gridOverflowCount} 个新增项目使用未持久化的临时位置；"
                    : string.Empty) +
                $"本次增量刷新复用 {reusedFreeIcons} 个自由图标和 {reusedGroups} 个分组，" +
                $"新建 {createdFreeIcons} 个自由图标和 {createdGroups} 个分组，移除 {removedVisuals} 个旧视觉";
            UpdateAutoClassificationControls();
            UpdateCollapseGroupsButton();

            if (layoutChanged)
            {
                SaveLayout();
            }
        }

        private FreeIconVisualState BuildFreeIconVisualState(string displayName, string fullPath)
        {
            string cacheKey = GetIconCacheKey(fullPath);
            return new FreeIconVisualState(
                GetDesktopItemVisualKind(displayName, fullPath),
                _appLayout.IsEditMode,
                !ShellItemLocation.TryDecode(fullPath, out _, out _) && IsFileOperationPending(fullPath),
                _iconVisualGeneration,
                GetIconCacheVersion(cacheKey));
        }

        private string BuildGroupVisualFingerprint(
            GroupInfo group,
            Dictionary<string, string> existing)
        {
            var builder = new StringBuilder(256 + group.ItemNames.Count * 80);
            AppendFingerprintPart(builder, group.Id);
            AppendFingerprintPart(builder, group.Name);
            AppendFingerprintPart(builder, group.AutoCategoryKey ?? string.Empty);
            builder.Append('|').Append(group.Width.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            builder.Append('|').Append(group.Height.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            builder.Append('|').Append(group.IsCollapsed ? '1' : '0');
            builder.Append('|').Append(group.IsAutoCategory ? '1' : '0');
            builder.Append('|').Append(group.IsSizeLocked ? '1' : '0');
            builder.Append('|').Append((int)group.SortMode);
            builder.Append('|').Append(_appLayout.IsEditMode ? '1' : '0');
            builder.Append('|').Append(_appLayout.CompactGroupLayout ? '1' : '0');
            builder.Append('|').Append(_iconVisualGeneration);

            foreach (string itemName in GetSortedGroupItemNames(group, existing))
            {
                AppendFingerprintPart(builder, itemName);
                string fullPath = existing.TryGetValue(itemName, out string? itemPath)
                    ? itemPath
                    : string.Empty;
                AppendFingerprintPart(builder, fullPath);
                AppendFingerprintPart(builder, GetDesktopItemVisualKind(itemName, fullPath));
                builder.Append('|').Append(GetIconCacheVersion(GetIconCacheKey(fullPath)));
                builder.Append('|').Append(
                    !ShellItemLocation.TryDecode(fullPath, out _, out _) && IsFileOperationPending(fullPath)
                        ? '1'
                        : '0');
            }

            return builder.ToString();
        }

        private string GetDesktopItemVisualKind(string displayName, string fullPath)
        {
            if (ShellItemLocation.TryDecode(fullPath, out _, out bool shellFolder))
            {
                return shellFolder ? "shell-folder" : "shell-item";
            }

            if (_appLayout.ItemIdentities.TryGetValue(displayName, out DesktopItemIdentityInfo? identity) &&
                identity.Kind == DesktopItemKind.FileSystem)
            {
                return identity.IsDirectory ? "file-system-folder" : "file-system-file";
            }

            return Directory.Exists(fullPath)
                ? "file-system-folder"
                : File.Exists(fullPath) ? "file-system-file" : "missing";
        }

        private static void AppendFingerprintPart(StringBuilder builder, string value)
        {
            builder.Append('|').Append(value.Length).Append(':').Append(value);
        }

        private bool IsReusableFreeIconVisual(
            string displayName,
            string fullPath,
            FrameworkElement visual)
        {
            return IconCanvas.Children.Contains(visual) &&
                visual.Tag is IconTag tag &&
                tag.Group == null &&
                tag.DisplayName.Equals(displayName, StringComparison.Ordinal) &&
                ShellItemLocation.AreEquivalent(tag.FullPath, fullPath);
        }

        private bool IsReusableGroupVisual(
            GroupInfo group,
            IReadOnlyDictionary<string, string> existing,
            FrameworkElement visual)
        {
            if (!IconCanvas.Children.Contains(visual) ||
                visual.Tag is not GroupInfo visualGroup ||
                !ReferenceEquals(visualGroup, group))
            {
                return false;
            }

            if (!_groupItemPanels.TryGetValue(group.Id, out VirtualizingGroupPanel? itemsPanel) ||
                !itemsPanel.IsStable)
            {
                return false;
            }

            List<GroupVirtualItem> expectedItems = BuildGroupVirtualItems(group, existing);
            return itemsPanel.MatchesItems(expectedItems);
        }

        private void RemoveFreeIconVisual(string displayName)
        {
            if (_freeIconVisuals.Remove(displayName, out FrameworkElement? visual))
            {
                IconCanvas.Children.Remove(visual);
                UnregisterIconVisualTree(visual);
            }

            _freeIconVisualStates.Remove(displayName);
        }

        private void RemoveGroupVisual(string groupId)
        {
            if (_groupVisuals.Remove(groupId, out FrameworkElement? visual))
            {
                IconCanvas.Children.Remove(visual);
                UnregisterIconVisualTree(visual);
                if (_groupDropTargets.TryGetValue(groupId, out FrameworkElement? target) &&
                    ReferenceEquals(target, visual))
                {
                    _groupDropTargets.Remove(groupId);
                }
            }

            _groupItemPanels.Remove(groupId);
            _groupVisualFingerprints.Remove(groupId);
        }

        private void UnregisterIconVisualTree(DependencyObject root)
        {
            if (root is FrameworkElement { Tag: IconTag tag } visual)
            {
                if (_allIconVisuals.TryGetValue(tag.DisplayName, out FrameworkElement? registered) &&
                    ReferenceEquals(registered, visual))
                {
                    _allIconVisuals.Remove(tag.DisplayName);
                }

                if (_physicalFolderDropTargets.TryGetValue(tag.FullPath, out FrameworkElement? folderTarget) &&
                    ReferenceEquals(folderTarget, visual))
                {
                    _physicalFolderDropTargets.Remove(tag.FullPath);
                }
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childCount; index++)
            {
                UnregisterIconVisualTree(VisualTreeHelper.GetChild(root, index));
            }
        }

        private bool RemoveMissingAndDuplicateGroupItems(Dictionary<string, string> existing)
        {
            bool changed = false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 手工分组优先：即使布局文件中出现重复项，也先保留手工分组中的项目。
            foreach (GroupInfo group in _appLayout.Groups.OrderBy(group => group.IsAutoCategory ? 1 : 0))
            {
                for (int i = group.ItemNames.Count - 1; i >= 0; i--)
                {
                    string name = group.ItemNames[i];
                    if (!existing.ContainsKey(name) || !seen.Add(name))
                    {
                        group.ItemNames.RemoveAt(i);
                        changed = true;
                    }
                }
            }

            // 自动分类没有项目时直接移除，避免留下截图中那种空白大框；手工空分组保留。
            int removedEmptyAutoGroups = _appLayout.Groups.RemoveAll(group =>
                group.IsAutoCategory && group.ItemNames.Count == 0);
            changed |= removedEmptyAutoGroups > 0;

            return changed;
        }

        private void StartDesktopWatchers()
        {
            StopDesktopWatchers();
            if (_isSafeModeActive || _isClosing)
            {
                return;
            }

            foreach (string path in new[] { _commonDesktopPath, _userDesktopPath }
                         .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var watcher = new FileSystemWatcher(path)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                    };

                    watcher.Created += DesktopWatcher_Changed;
                    watcher.Deleted += DesktopWatcher_Changed;
                    watcher.Renamed += DesktopWatcher_Renamed;
                    watcher.Error += DesktopWatcher_Error;
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                }
                catch (IOException)
                {
                    // 无法监听时仍可使用手动刷新。
                }
                catch (UnauthorizedAccessException)
                {
                    // 公共桌面可能受策略限制，忽略该监听器。
                }
            }
        }

        private void StopDesktopWatchers()
        {
            foreach (FileSystemWatcher watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Created -= DesktopWatcher_Changed;
                watcher.Deleted -= DesktopWatcher_Changed;
                watcher.Renamed -= DesktopWatcher_Renamed;
                watcher.Error -= DesktopWatcher_Error;
                watcher.Dispose();
            }

            _watchers.Clear();
        }

        private void DesktopWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (!_isSafeModeActive)
            {
                ScheduleDesktopRefresh();
            }
        }

        private void DesktopWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            if (_isSafeModeActive || _isClosing)
            {
                return;
            }

            QueueDesktopRename(e.OldFullPath, e.FullPath);
            ScheduleDesktopRefresh();
        }

        private void DesktopWatcher_Error(object sender, ErrorEventArgs e)
        {
            if (_isSafeModeActive)
            {
                return;
            }

            ScheduleDesktopRefresh();
            if (Interlocked.Exchange(ref _watcherRestartQueued, 1) == 0)
            {
                _ = RestartDesktopWatchersAfterErrorAsync();
            }
        }

        private async Task RestartDesktopWatchersAfterErrorAsync()
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), _lifetimeCts.Token).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (!_isClosing && !_isSafeModeActive)
                    {
                        StartDesktopWatchers();
                    }
                }, DispatcherPriority.Background, _lifetimeCts.Token);
            }
            catch (OperationCanceledException) when (_isClosing || _lifetimeCts.IsCancellationRequested)
            {
                // 正常退出。
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Desktop watcher restart failed: {exception}");
            }
            finally
            {
                Interlocked.Exchange(ref _watcherRestartQueued, 0);
            }
        }

        private void CancelScheduledDesktopRefresh()
        {
            lock (_refreshDebounceLock)
            {
                _refreshDebounceCts?.Cancel();
                _refreshDebounceCts?.Dispose();
                _refreshDebounceCts = null;
                _pendingRefreshStatus = null;
            }
        }

        private void CancelActiveDesktopRefresh()
        {
            _refreshCancellationEpoch.Advance();
            Interlocked.Exchange(ref _refreshRequested, 0);
            Interlocked.Exchange(ref _clearIconCacheRequested, 0);
            lock (_refreshDebounceLock)
            {
                _pendingRefreshStatus = null;
            }
        }

        private void ScheduleDesktopRefresh(bool allowInSafeMode = false)
        {
            if (_isClosing || (_isSafeModeActive && !allowInSafeMode))
            {
                return;
            }

            CancellationTokenSource debounceSource;
            lock (_refreshDebounceLock)
            {
                // Watcher 回调可能在安全模式切换前通过第一次检查、随后才取得锁；
                // 在临界区内再次确认，避免切换瞬间重新排队。
                if (_isClosing || (_isSafeModeActive && !allowInSafeMode))
                {
                    return;
                }

                _refreshDebounceCts?.Cancel();
                _refreshDebounceCts?.Dispose();
                _refreshDebounceCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
                debounceSource = _refreshDebounceCts;
            }

            _ = DebounceDesktopRefreshAsync(debounceSource, allowInSafeMode);
        }

        private async Task DebounceDesktopRefreshAsync(
            CancellationTokenSource debounceSource,
            bool allowInSafeMode)
        {
            try
            {
                // FileSystemWatcher 会为一次复制/重命名产生多条事件；只保留最后一次。
                await Task.Delay(TimeSpan.FromMilliseconds(850), debounceSource.Token).ConfigureAwait(false);
                if (_isSafeModeActive && !allowInSafeMode)
                {
                    return;
                }

                RequestDesktopRefresh(clearIconCache: false, statusMessage: null);
            }
            catch (OperationCanceledException)
            {
                // 后续事件取代了本次刷新，属于正常防抖流程。
            }
            finally
            {
                lock (_refreshDebounceLock)
                {
                    if (ReferenceEquals(_refreshDebounceCts, debounceSource))
                    {
                        _refreshDebounceCts.Dispose();
                        _refreshDebounceCts = null;
                    }
                }
            }
        }

        private void RequestDesktopRefresh(bool clearIconCache, string? statusMessage)
        {
            if (_isClosing)
            {
                return;
            }

            if (clearIconCache)
            {
                Interlocked.Exchange(ref _clearIconCacheRequested, 1);
            }

            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                lock (_refreshDebounceLock)
                {
                    _pendingRefreshStatus = statusMessage;
                }
            }

            Interlocked.Exchange(ref _refreshRequested, 1);
            if (Interlocked.CompareExchange(ref _refreshWorkerRunning, 1, 0) == 0)
            {
                _ = RunRefreshWorkerAsync();
            }
        }

        private async Task RunRefreshWorkerAsync()
        {
            try
            {
                while (!_isClosing && Interlocked.Exchange(ref _refreshRequested, 0) == 1)
                {
                    bool clearIconCache = Interlocked.Exchange(ref _clearIconCacheRequested, 0) == 1;
                    string? statusMessage;
                    lock (_refreshDebounceLock)
                    {
                        statusMessage = _pendingRefreshStatus;
                        _pendingRefreshStatus = null;
                    }

                    await RefreshDesktopSnapshotAsync(clearIconCache, statusMessage).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_isClosing || _lifetimeCts.IsCancellationRequested)
            {
                // 正常退出。
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Desktop refresh failed: {exception}");
                if (!_isClosing)
                {
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                        StatusText.Text = "桌面刷新失败，可稍后点击刷新重试"));
                }
            }
            finally
            {
                Interlocked.Exchange(ref _refreshWorkerRunning, 0);
                if (!_isClosing &&
                    Interlocked.CompareExchange(ref _refreshRequested, 0, 0) == 1 &&
                    Interlocked.CompareExchange(ref _refreshWorkerRunning, 1, 0) == 0)
                {
                    _ = RunRefreshWorkerAsync();
                }
            }
        }

        private void PreserveKnownShellItemsForIncompleteScan(DesktopScanSnapshot snapshot)
        {
            if (snapshot.ShellScanComplete)
            {
                return;
            }

            foreach ((string displayName, string location) in _desktopItems)
            {
                if (!ShellItemLocation.TryDecode(location, out string parsingName, out bool isFolder) ||
                    !ShellDesktopItemPolicy.IsRecycleBin(parsingName) ||
                    snapshot.Items.ContainsKey(displayName))
                {
                    continue;
                }

                snapshot.Items[displayName] = location;
                if (_desktopCategories.TryGetValue(
                        displayName,
                        out DesktopCategoryDefinition? currentCategory) &&
                    currentCategory is not null)
                {
                    snapshot.Categories[displayName] = currentCategory;
                }
                else
                {
                    snapshot.Categories[displayName] =
                        DesktopCategoryClassifier.ClassifyShellNamespace();
                }

                snapshot.ReliableCategoryNames.Add(displayName);

                if (_appLayout.ItemIdentities.TryGetValue(
                        displayName,
                        out DesktopItemIdentityInfo? currentIdentity) &&
                    currentIdentity is not null)
                {
                    snapshot.Identities[displayName] = currentIdentity;
                }
                else
                {
                    snapshot.Identities[displayName] =
                        CreateShellDesktopItemIdentity(location, parsingName, isFolder);
                }
            }
        }

        private void RejectIncompleteDesktopSnapshot(DesktopScanSnapshot snapshot)
        {
            int failureCount = Interlocked.Increment(ref _consecutiveIncompleteScans);
            _diagnostics.Log(
                $"SCAN incomplete physical={snapshot.PhysicalScanComplete}, " +
                $"shell={snapshot.ShellScanComplete}, consecutive={failureCount}");

            if (_isSafeModeActive)
            {
                return;
            }

            if (failureCount >= 3 && !_isClosing && !Dispatcher.HasShutdownStarted)
            {
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() =>
                    {
                        if (!_isClosing)
                        {
                            StatusText.Text = "桌面读取暂时不稳定，已保留现有图标并在后台重试";
                        }
                    }));
            }

            if (Interlocked.CompareExchange(ref _incompleteScanRetryQueued, 1, 0) == 0)
            {
                _ = RetryIncompleteDesktopScanAsync(failureCount);
            }
        }

        private async Task RetryIncompleteDesktopScanAsync(int failureCount)
        {
            using CancellationTokenSource refreshCancellation =
                _refreshCancellationEpoch.CreateLinkedTokenSource(_lifetimeCts.Token);
            try
            {
                int delayMilliseconds = Math.Min(5000, 500 + failureCount * 400);
                await Task.Delay(delayMilliseconds, refreshCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                Interlocked.Exchange(ref _incompleteScanRetryQueued, 0);
            }

            if (!_isClosing &&
                !_isSafeModeActive &&
                !_lifetimeCts.IsCancellationRequested &&
                !refreshCancellation.IsCancellationRequested)
            {
                RequestDesktopRefresh(clearIconCache: false, statusMessage: null);
            }
        }

        private async Task RefreshDesktopSnapshotAsync(bool clearIconCache, string? statusMessage)
        {
            using CancellationTokenSource refreshCancellation =
                _refreshCancellationEpoch.CreateLinkedTokenSource(_lifetimeCts.Token);
            try
            {
                CancellationToken cancellationToken = refreshCancellation.Token;
                Stopwatch scanStopwatch = Stopwatch.StartNew();
                DesktopScanSnapshot snapshot = await Task.Run(
                    () => ScanDesktopSnapshot(cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                scanStopwatch.Stop();
                _diagnostics.LogSlowOperation("desktop-scan", scanStopwatch.Elapsed, snapshot.Items.Count);

                if (!snapshot.PhysicalScanComplete)
                {
                    RejectIncompleteDesktopSnapshot(snapshot);
                    return;
                }

                if (!snapshot.ShellScanComplete)
                {
                    RejectIncompleteDesktopSnapshot(snapshot);
                }
                else
                {
                    Interlocked.Exchange(ref _consecutiveIncompleteScans, 0);
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    if (_isClosing)
                    {
                        return;
                    }

                    PreserveKnownShellItemsForIncompleteScan(snapshot);
                    RecoverStaleInteractionState();

                    // 不在任何鼠标捕获或拖动过程中替换整棵视觉树。
                    if (IsUserInteractionActive())
                    {
                        ScheduleDesktopRefresh();
                        return;
                    }

                    bool snapshotChanged = !_desktopSnapshotInitialized ||
                        !DesktopSnapshotMatches(
                            _desktopItems,
                            _desktopCategories,
                            _reliableDesktopCategoryNames,
                            snapshot) ||
                        !DesktopIdentityMapsEqual(_appLayout.ItemIdentities, snapshot.Identities);

                    // 某些 Shell 操作会产生“内容未变化”的通知。没有变化时不要清空并重建
                    // 整棵视觉树，否则持续的无效通知仍会周期性占用 UI 线程。
                    if (!snapshotChanged && !clearIconCache)
                    {
                        if (!string.IsNullOrWhiteSpace(statusMessage))
                        {
                            StatusText.Text = statusMessage;
                        }
                        return;
                    }

                    bool resetAllIconCache = clearIconCache ||
                        _iconCache.Count > 512 ||
                        _iconCacheVersions.Count > 1024;
                    if (resetAllIconCache)
                    {
                        _iconCache.Clear();
                        _iconCacheVersions.Clear();
                        unchecked
                        {
                            _iconVisualGeneration++;
                        }

                        AdvanceIconCacheGeneration();
                        EnsureRecycleBinWidgetIcon(forceReload: true);
                    }
                    else
                    {
                        HashSet<string> changedIconLocations = IconCacheInvalidation.FindChangedLocations(
                            _desktopItems,
                            _appLayout.ItemIdentities,
                            snapshot.Items,
                            snapshot.Identities);
                        int invalidatedKeyCount = InvalidateIconCacheLocations(changedIconLocations);
                        if (invalidatedKeyCount > 0)
                        {
                            _diagnostics.Log(
                                $"ICON_CACHE invalidatedLocations={changedIconLocations.Count}, keys={invalidatedKeyCount}");
                        }
                    }

                    var previousIdentities = new Dictionary<string, DesktopItemIdentityInfo>(
                        _appLayout.ItemIdentities,
                        StringComparer.OrdinalIgnoreCase);
                    bool inboxBaselineWasEstablished = _appLayout.InboxBaselineEstablished;
                    HashSet<string> newItemNames = DesktopNewItemDetector.FindNewItemNames(
                        previousIdentities,
                        snapshot.Identities);
                    bool identityLayoutChanged = ReconcileDesktopItemIdentities(snapshot);
                    _desktopItems = snapshot.Items;
                    _desktopCategories = snapshot.Categories;
                    _reliableDesktopCategoryNames = snapshot.ReliableCategoryNames;
                    _desktopSnapshotInitialized = true;

                    DateTime utcNow = DateTime.UtcNow;
                    InboxReconcileResult inboxResult = InboxQueueManager.Reconcile(
                        _appLayout.InboxItems,
                        _appLayout.InboxBaselineEstablished,
                        completeScan: snapshot.PhysicalScanComplete && snapshot.ShellScanComplete,
                        previousIdentities,
                        snapshot.Identities,
                        BuildInboxSuggestions(snapshot),
                        utcNow);
                    _appLayout.InboxBaselineEstablished = inboxResult.BaselineEstablished;
                    bool organizationMetadataChanged = ReconcileDesktopOrganizationMetadata(
                        snapshot,
                        newItemNames,
                        inboxBaselineWasEstablished,
                        utcNow);
                    bool inboxAutoApplied = ApplyAutomaticInboxAcceptances();
                    bool userRulesChanged = snapshot.PhysicalScanComplete && snapshot.ShellScanComplete &&
                        ApplyEnabledOrganizationRules();
                    UpdateInboxButton();

                    // 新项目先经过收件箱；只有用户已明确开启“新项目归类”且建议可靠时，
                    // 上面的收件箱接受计划才会把它加入虚拟分类。这里不再绕过审阅队列。
                    RebuildDesktopIcons(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    if (identityLayoutChanged || inboxResult.Changed ||
                        inboxResult.BaselineChanged || organizationMetadataChanged ||
                        inboxAutoApplied || userRulesChanged)
                    {
                        SaveLayout();
                    }
                    if (!string.IsNullOrWhiteSpace(statusMessage))
                    {
                        StatusText.Text = statusMessage;
                    }
                }, DispatcherPriority.Background, cancellationToken);
            }
            catch (OperationCanceledException) when (
                _isClosing ||
                _lifetimeCts.IsCancellationRequested ||
                refreshCancellation.IsCancellationRequested)
            {
                // 正常退出或安全模式取消了旧刷新代次。
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Desktop snapshot failed: {exception}");
                if (!_isClosing && !Dispatcher.HasShutdownStarted)
                {
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                        StatusText.Text = "桌面扫描失败，可稍后点击刷新重试"));
                }
            }
        }

        private void NativeIconGuardTimer_Tick(object? sender, EventArgs e)
        {
            RecoverStaleInteractionState();
            if (Volatile.Read(ref _desktopShellMenuActive) != 0 || IsUserInteractionActive())
            {
                return;
            }

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (!_isAttachedToDesktop ||
                !NativeMethods.IsWindowAttachedToDesktop(hwnd, _desktopHostHandle))
            {
                _isAttachedToDesktop = NativeMethods.TryAttachWindowToDesktop(hwnd, out _desktopHostHandle);
                if (_isAttachedToDesktop)
                {
                    Interlocked.Exchange(ref _desktopLayerGuardFailures, 0);
                    UpdateDesktopHostBounds(hwnd, remapExistingLayout: true);
                }
                else
                {
                    // 保留当前窗口位置，等待下次低频巡检确认。单次 Shell 查询失败时
                    // 立即送到底层会让整个整理层瞬间消失。
                    ConfigureDesktopBounds();
                }
            }

            if (!_isAttachedToDesktop)
            {
                int failures = Interlocked.Increment(ref _desktopLayerGuardFailures);
                // Shell 的 Z 序查询可能在 Explorer/Spotlight 切换窗口时短暂失败。
                // 单次失败就显示原生图标、下一轮又隐藏，会让整桌面图标交替闪现。
                // 连续三次（约 45 秒）确认失败后才执行安全回退。
                if (failures >= 3 && _nativeIconsWereVisible)
                {
                    NativeMethods.RestoreNativeDesktopIcons();
                    NativeMethods.SendWindowToBottom(hwnd);
                    _nativeIconsWereVisible = false;
                    TryDeleteSessionMarker();
                    _diagnostics.Log("DESKTOP_LAYER guard failed repeatedly; native icons restored");
                }
                return;
            }

            Interlocked.Exchange(ref _desktopLayerGuardFailures, 0);

            if (_organizerPaused)
            {
                return;
            }

            bool newlyHidden = NativeMethods.EnsureNativeDesktopIconsHidden();
            if (newlyHidden && !_nativeIconsWereVisible)
            {
                // Explorer 可能在启动时尚未准备好；稍后第一次成功隐藏时补记恢复状态。
                _nativeIconsWereVisible = true;
                WriteSessionMarker();
            }
        }

        // ==================== 图标与分组可视化 ====================

    }
}
