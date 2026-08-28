// 网格对齐与布局规范化
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private void AlignFreeIconsToGrid(bool showFeedback)
        {
            (int alignedCount, int skippedCount) = ApplyGridAlignmentToFreeIcons();
            RebuildDesktopIconsAndSaveLayout();

            if (showFeedback)
            {
                StatusText.Text = (alignedCount, skippedCount) switch
                {
                    (> 0, 0) => $"已对齐 {alignedCount} 个自由图标",
                    (> 0, > 0) => $"已对齐 {alignedCount} 个自由图标；{skippedCount} 个因没有可用网格保持原位",
                    (0, > 0) => $"没有可用网格，{skippedCount} 个自由图标保持原位",
                    _ => "没有可对齐的自由图标"
                };
            }
        }

        private (int AlignedCount, int SkippedCount) ApplyGridAlignmentToFreeIcons()
        {
            NormalizeLayout();
            var existing = new Dictionary<string, string>(_desktopItems, StringComparer.OrdinalIgnoreCase);
            var groupedNames = new HashSet<string>(
                _appLayout.Groups.SelectMany(group => group.ItemNames),
                StringComparer.OrdinalIgnoreCase);

            List<string> iconNames = _appLayout.FreeIcons
                .Where(pair => existing.ContainsKey(pair.Key) && !groupedNames.Contains(pair.Key))
                .OrderBy(pair => SafeCanvasCoordinate(pair.Value.Y))
                .ThenBy(pair => SafeCanvasCoordinate(pair.Value.X))
                .ThenBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(pair => pair.Key)
                .ToList();

            if (iconNames.Count == 0)
            {
                return (0, 0);
            }

            Dictionary<string, IconPosition>? plan = TryPlanAlignedIconPositions(
                iconNames.Select(name => (name, _appLayout.FreeIcons[name])),
                new HashSet<(int Column, int Row)>());
            if (plan == null)
            {
                // 整批保持原位，避免先移动的图标占用后续失败图标的旧位置。
                return (0, iconNames.Count);
            }

            foreach ((string name, IconPosition position) in plan)
            {
                _appLayout.FreeIcons[name] = position;
            }

            return (plan.Count, 0);
        }

        private void AlignSingleIconToGrid(string name)
        {
            if (!_appLayout.FreeIcons.TryGetValue(name, out IconPosition? position) || position == null)
            {
                return;
            }

            IconPosition? alignedPosition = FindAlignedIconPosition(name, position);
            if (alignedPosition == null)
            {
                StatusText.Text = $"没有可用网格，“{name}”保持原位";
                return;
            }

            _appLayout.FreeIcons[name] = alignedPosition;
            RebuildDesktopIconsAndSaveLayout();
            StatusText.Text = $"“{name}”已对齐到网格";
        }

        private IconPosition? FindAlignedIconPosition(
            string name,
            IconPosition requestedPosition,
            IReadOnlyDictionary<string, Rect>? groupBoundsOverrides = null)
        {
            (int preferredColumn, int preferredRow) = GetNearestGridCell(
                requestedPosition.X,
                requestedPosition.Y);

            var excludedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
            HashSet<(int Column, int Row)> occupied = GetOccupiedFreeGridCells(excludedNames);
            GridCell? available = FindNearestAvailableGridCell(
                preferredColumn,
                preferredRow,
                occupied,
                groupBoundsOverrides: groupBoundsOverrides);
            return available.HasValue
                ? GridCellToPosition(available.Value.Column, available.Value.Row)
                : null;
        }

        private (int Column, int Row) GetNearestGridCell(double x, double y)
        {
            int maxColumn = GetGridColumnCount() - 1;
            int maxRow = GetGridRowCount() - 1;
            int column = (int)Math.Round((SafeCanvasCoordinate(x) - GridOriginX) / IconCellWidth);
            int row = (int)Math.Round((SafeCanvasCoordinate(y) - GridOriginY) / IconCellHeight);
            return (Math.Clamp(column, 0, maxColumn), Math.Clamp(row, 0, maxRow));
        }

        private GridCell? FindNearestAvailableGridCell(
            int preferredColumn,
            int preferredRow,
            IReadOnlySet<(int Column, int Row)> occupied,
            IReadOnlySet<string>? ignoredGroupIds = null,
            IReadOnlyDictionary<string, Rect>? groupBoundsOverrides = null)
        {
            int columns = GetGridColumnCount();
            int rows = GetGridRowCount();
            return GridCellSearch.FindNearest(
                columns,
                rows,
                preferredColumn,
                preferredRow,
                (column, row) =>
                    !occupied.Contains((column, row)) &&
                    !GridCellIntersectsGroup(
                        column,
                        row,
                        ignoredGroupIds,
                        groupBoundsOverrides) &&
                    GridCellFitsUsableDesktop(column, row));
        }

        private HashSet<(int Column, int Row)> GetOccupiedFreeGridCells(
            IReadOnlySet<string>? excludedNames = null)
        {
            var groupedNames = new HashSet<string>(
                _appLayout.Groups.SelectMany(group => group.ItemNames),
                StringComparer.OrdinalIgnoreCase);
            var occupied = new HashSet<(int Column, int Row)>();
            foreach ((string name, IconPosition position) in _appLayout.FreeIcons)
            {
                if ((excludedNames?.Contains(name) ?? false) ||
                    groupedNames.Contains(name) ||
                    position == null)
                {
                    continue;
                }

                occupied.Add(GetNearestGridCell(position.X, position.Y));
            }

            return occupied;
        }

        private Dictionary<string, IconPosition>? TryPlanAlignedIconPositions(
            IEnumerable<(string Name, IconPosition Requested)> requests,
            IReadOnlySet<(int Column, int Row)> occupied,
            IReadOnlySet<string>? ignoredGroupIds = null,
            IReadOnlyDictionary<string, Rect>? groupBoundsOverrides = null)
        {
            List<GridPlacementRequest> gridRequests = requests
                .Select(request =>
                {
                    (int column, int row) = GetNearestGridCell(
                        request.Requested.X,
                        request.Requested.Y);
                    return new GridPlacementRequest(request.Name, column, row);
                })
                .ToList();

            Dictionary<string, GridCell>? plan = GridPlacementPlanner.TryPlan(
                GetGridColumnCount(),
                GetGridRowCount(),
                gridRequests,
                occupied.Select(cell => new GridCell(cell.Column, cell.Row)),
                (column, row) =>
                    !GridCellIntersectsGroup(
                        column,
                        row,
                        ignoredGroupIds,
                        groupBoundsOverrides) &&
                    GridCellFitsUsableDesktop(column, row),
                StringComparer.OrdinalIgnoreCase);
            return plan?.ToDictionary(
                pair => pair.Key,
                pair => GridCellToPosition(pair.Value.Column, pair.Value.Row),
                StringComparer.OrdinalIgnoreCase);
        }

        private Dictionary<string, Rect> BuildGroupBoundsAfterRemovingItems(
            IEnumerable<string> removedItemNames)
        {
            var removedNames = new HashSet<string>(
                removedItemNames,
                StringComparer.OrdinalIgnoreCase);
            var overrides = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
            foreach (GroupInfo group in _appLayout.Groups)
            {
                if (group.IsSizeLocked ||
                    !group.ItemNames.Any(removedNames.Contains))
                {
                    continue;
                }

                var plannedGroup = new GroupInfo
                {
                    Id = group.Id,
                    Name = group.Name,
                    X = group.X,
                    Y = group.Y,
                    Width = group.Width,
                    Height = group.Height,
                    ItemNames = group.ItemNames
                        .Where(name => !removedNames.Contains(name))
                        .ToList(),
                    IsCollapsed = group.IsCollapsed,
                    IsAutoCategory = group.IsAutoCategory,
                    AutoCategoryKey = group.AutoCategoryKey,
                    IsSizeLocked = false,
                    SortMode = group.SortMode
                };
                AutoFitGroup(plannedGroup, clampPosition: false);
                overrides[group.Id] = GetGroupBounds(plannedGroup);
            }

            return overrides;
        }

        private bool GridCellIntersectsGroup(
            int column,
            int row,
            IReadOnlySet<string>? ignoredGroupIds = null,
            IReadOnlyDictionary<string, Rect>? groupBoundsOverrides = null)
        {
            IconPosition position = GridCellToPosition(column, row);
            var iconBounds = new Rect(position.X, position.Y, IconCellWidth, IconCellHeight);

            if (_appLayout.Groups.Any(group =>
                {
                    if (ignoredGroupIds?.Contains(group.Id) == true)
                    {
                        return false;
                    }

                    Rect groupBounds = groupBoundsOverrides != null &&
                                       groupBoundsOverrides.TryGetValue(group.Id, out Rect overrideBounds)
                        ? overrideBounds
                        : GetGroupBounds(group);
                    return iconBounds.IntersectsWith(groupBounds);
                }))
            {
                return true;
            }

            Rect? recycleObstacle = GetRecycleBinWidgetObstacle();
            return recycleObstacle.HasValue && iconBounds.IntersectsWith(recycleObstacle.Value);
        }

        private bool GridCellFitsUsableDesktop(int column, int row)
        {
            double x = GridOriginX + column * IconCellWidth;
            double y = GridOriginY + row * IconCellHeight;
            return IsRectInsideUsableDesktop(new Rect(x, y, IconCellWidth, IconCellHeight));
        }

        private IconPosition GridCellToPosition(int column, int row)
        {
            var position = new IconPosition
            {
                X = GridOriginX + column * IconCellWidth,
                Y = GridOriginY + row * IconCellHeight
            };
            ClampIconPosition(position);
            return position;
        }

        private IconPosition CreateTemporaryGridOverflowPosition(int itemIndex, int overflowIndex)
        {
            int columns = GetGridColumnCount();
            int preferredRow = itemIndex / columns;
            int preferredColumn = itemIndex % columns;
            var position = new IconPosition
            {
                X = GridOriginX + preferredColumn * IconCellWidth,
                Y = GridOriginY + preferredRow * IconCellHeight
            };

            // 这是仅用于当前视觉刷新的临时级联位置，不会写入布局。空出有效网格后，
            // 下一次刷新会重新尝试正式吸附。
            double cascadeOffset = 12 * ((overflowIndex % 5) + 1);
            position.X += cascadeOffset;
            position.Y += cascadeOffset;
            ClampIconPosition(position);
            return position;
        }

        private int GetGridColumnCount()
        {
            return Math.Max(1, (int)Math.Floor(
                (GetCanvasWidth() - GridOriginX - IconCellWidth) / IconCellWidth) + 1);
        }

        private int GetGridRowCount()
        {
            return Math.Max(1, (int)Math.Floor(
                (GetCanvasHeight() - GridOriginY - IconCellHeight) / IconCellHeight) + 1);
        }

        // ==================== 坐标与布局持久化 ====================

        private double GetCanvasWidth() => Math.Max(1, _desktopGeometry.CanvasBounds.Width);
        private double GetCanvasHeight() => Math.Max(1, _desktopGeometry.CanvasBounds.Height);

        private void ClampIconCoordinates(ref double x, ref double y)
        {
            Point clamped = ClampRectToUsableDesktop(
                x,
                y,
                IconCellWidth,
                IconCellHeight);
            x = clamped.X;
            y = clamped.Y;
        }

        private void ClampIconPosition(IconPosition position)
        {
            double x = SafeCanvasCoordinate(position.X);
            double y = SafeCanvasCoordinate(position.Y);
            ClampIconCoordinates(ref x, ref y);
            position.X = x;
            position.Y = y;
        }

        private void ClampGroupCoordinates(GroupInfo group, ref double x, ref double y)
        {
            Point clamped = ClampRectToUsableDesktop(
                x,
                y,
                group.Width,
                GetGroupDisplayHeight(group));
            x = clamped.X;
            y = clamped.Y;
        }

        private void ClampGroupToCanvas(GroupInfo group)
        {
            var requestedBounds = new Rect(
                SafeCanvasCoordinate(group.X),
                SafeCanvasCoordinate(group.Y),
                Math.Max(GroupMinWidth, SafeCanvasCoordinate(group.Width)),
                Math.Max(GroupMinHeight, SafeCanvasCoordinate(group.Height)));
            DesktopMonitorRegion monitor = GetMonitorForItemRect(requestedBounds);
            group.Width = Math.Clamp(
                group.Width,
                GroupMinWidth,
                Math.Max(GroupMinWidth, monitor.WorkArea.Width));
            group.Height = Math.Clamp(
                group.Height,
                GroupMinHeight,
                Math.Max(GroupMinHeight, monitor.WorkArea.Height));

            double x = SafeCanvasCoordinate(group.X);
            double y = SafeCanvasCoordinate(group.Y);
            ClampGroupCoordinates(group, ref x, ref y);
            group.X = x;
            group.Y = y;
        }

        private static double SafeCanvasCoordinate(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 0 : value;
        }

        private void NormalizeLayout()
        {
            int loadedVersion = _appLayout.Version;
            _appLayout.Version = 17;
            _appLayout.FreeIcons ??= new Dictionary<string, IconPosition>();
            _appLayout.Groups ??= new List<GroupInfo>();
            _appLayout.DesktopTopology ??= new List<DesktopMonitorLayoutInfo>();
            _appLayout.ItemIdentities ??= new Dictionary<string, DesktopItemIdentityInfo>();
            _appLayout.RecycleBinWidget ??= new RecycleBinWidgetLayoutInfo();
            _appLayout.AutoClassificationOriginalPositions ??= new Dictionary<string, IconPosition>();
            _appLayout.InboxItems ??= new Dictionary<string, InboxItemInfo>();
            _appLayout.ItemTags ??= new Dictionary<string, List<string>>();
            _appLayout.ItemFirstSeenUtcTicks ??= new Dictionary<string, long>();
            _appLayout.ItemLastMovedUtcTicks ??= new Dictionary<string, long>();
            WorkspaceLayoutManager.Normalize(_appLayout);

            // JSON 反序列化不会保留 Dictionary 的比较器，这里重建为 Windows 友好的大小写不敏感字典。
            var normalizedIcons = new Dictionary<string, IconPosition>(StringComparer.OrdinalIgnoreCase);
            foreach ((string key, IconPosition value) in _appLayout.FreeIcons)
            {
                if (!string.IsNullOrWhiteSpace(key) && value != null)
                {
                    normalizedIcons[key] = value;
                }
            }
            _appLayout.FreeIcons = normalizedIcons;

            var normalizedIdentities = new Dictionary<string, DesktopItemIdentityInfo>(StringComparer.OrdinalIgnoreCase);
            foreach ((string key, DesktopItemIdentityInfo value) in _appLayout.ItemIdentities)
            {
                if (string.IsNullOrWhiteSpace(key) || value == null)
                {
                    continue;
                }

                if (!Enum.IsDefined(typeof(DesktopItemKind), value.Kind))
                {
                    value.Kind = DesktopItemKind.FileSystem;
                }

                value.LastKnownPath = NormalizePersistedPath(value.LastKnownPath);
                value.FileId = string.IsNullOrWhiteSpace(value.FileId) ? null : value.FileId.Trim();
                value.ShellParsingName = string.IsNullOrWhiteSpace(value.ShellParsingName)
                    ? null
                    : value.ShellParsingName.Trim();
                if (value.Kind == DesktopItemKind.ShellNamespace && value.ShellParsingName == null &&
                    ShellItemLocation.TryDecode(value.LastKnownPath, out string parsingName, out bool isFolder))
                {
                    value.ShellParsingName = parsingName;
                    value.IsDirectory = isFolder;
                }
                normalizedIdentities[key] = value;
            }
            _appLayout.ItemIdentities = normalizedIdentities;

            var normalizedOriginalPositions = new Dictionary<string, IconPosition>(StringComparer.OrdinalIgnoreCase);
            foreach ((string key, IconPosition value) in _appLayout.AutoClassificationOriginalPositions)
            {
                if (!string.IsNullOrWhiteSpace(key) && value != null)
                {
                    normalizedOriginalPositions[key] = value;
                }
            }
            _appLayout.AutoClassificationOriginalPositions = normalizedOriginalPositions;
            _appLayout.InboxItems = NormalizeInboxItems(_appLayout.InboxItems);
            _appLayout.ItemTags = NormalizeItemTags(_appLayout.ItemTags);
            _appLayout.ItemFirstSeenUtcTicks = NormalizeItemTimes(_appLayout.ItemFirstSeenUtcTicks);
            _appLayout.ItemLastMovedUtcTicks = NormalizeItemTimes(_appLayout.ItemLastMovedUtcTicks);
            if (loadedVersion < 17 && _appLayout.ItemIdentities.Count > 0)
            {
                _appLayout.InboxBaselineEstablished = true;
            }
            _appLayout.DesktopTopology = _appLayout.DesktopTopology
                .OfType<DesktopMonitorLayoutInfo>()
                .Where(info => !string.IsNullOrWhiteSpace(info.DeviceName))
                .GroupBy(info => info.DeviceName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            _appLayout.Groups = _appLayout.Groups.OfType<GroupInfo>().ToList();
            var groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (GroupInfo group in _appLayout.Groups)
            {
                if (string.IsNullOrWhiteSpace(group.Id) || !groupIds.Add(group.Id))
                {
                    do
                    {
                        group.Id = Guid.NewGuid().ToString("N");
                    }
                    while (!groupIds.Add(group.Id));
                }
                group.Name = string.IsNullOrWhiteSpace(group.Name) ? "未命名分组" : group.Name;
                if (group.IsAutoCategory && string.IsNullOrWhiteSpace(group.AutoCategoryKey))
                {
                    group.AutoCategoryKey = "legacy-" + group.Id;
                }

                group.ItemNames ??= new List<string>();
                group.ItemNames = group.ItemNames
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (!Enum.IsDefined(typeof(GroupSortMode), group.SortMode))
                {
                    group.SortMode = GroupSortMode.Custom;
                }
            }


            if (loadedVersion < 15)
            {
                // v15 将回收站从普通图标/分组模型迁移为独立小组件。通过稳定 CLSID
                // 查找旧条目，不依赖当前系统语言中的“回收站”显示名称。
                List<string> legacyRecycleBinNames = _appLayout.ItemIdentities
                    .Where(pair =>
                        pair.Value.Kind == DesktopItemKind.ShellNamespace &&
                        ShellDesktopItemPolicy.IsRecycleBin(pair.Value.ShellParsingName))
                    .Select(pair => pair.Key)
                    .ToList();
                foreach (string name in legacyRecycleBinNames)
                {
                    if (!_appLayout.RecycleBinWidget.X.HasValue &&
                        !_appLayout.RecycleBinWidget.Y.HasValue &&
                        _appLayout.FreeIcons.TryGetValue(name, out IconPosition? oldPosition) &&
                        oldPosition != null)
                    {
                        _appLayout.RecycleBinWidget.X = oldPosition.X;
                        _appLayout.RecycleBinWidget.Y = oldPosition.Y;
                    }

                    _appLayout.FreeIcons.Remove(name);
                    _appLayout.AutoClassificationOriginalPositions.Remove(name);
                    _appLayout.ItemIdentities.Remove(name);
                    foreach (GroupInfo group in _appLayout.Groups)
                    {
                        group.ItemNames.RemoveAll(item =>
                            item.Equals(name, StringComparison.OrdinalIgnoreCase));
                    }
                }

                _appLayout.Groups.RemoveAll(group =>
                    group.IsAutoCategory && group.ItemNames.Count == 0);
            }

            if (loadedVersion < 11)
            {
                _appLayout.CompactGroupLayout = true;
                _appLayout.ReserveTemporaryWorkspace = true;
            }

            if (loadedVersion < 10)
            {
                foreach (GroupInfo group in _appLayout.Groups.Where(group => group.IsAutoCategory))
                {
                    group.SortMode = GroupSortMode.Name;
                }
            }

            if (loadedVersion < 9)
            {
                // v1.9 首次升级默认进入安全的日常模式，并让旧分组按内容重新适应一次。
                _appLayout.IsEditMode = false;
                _appLayout.AutoCollapseControlPanel = true;
                foreach (GroupInfo group in _appLayout.Groups)
                {
                    group.IsSizeLocked = false;
                }
            }

            // v1.6 首次升级：自动分类较多时默认收起，解决旧版一启动就铺满屏幕的问题。
            if (loadedVersion < 6 && _appLayout.Groups.Count(group => group.IsAutoCategory) >= 5)
            {
                foreach (GroupInfo group in _appLayout.Groups.Where(group => group.IsAutoCategory))
                {
                    group.IsCollapsed = true;
                }
            }
        }

        private static Dictionary<string, InboxItemInfo> NormalizeInboxItems(
            IEnumerable<KeyValuePair<string, InboxItemInfo>> source)
        {
            var result = new Dictionary<string, InboxItemInfo>(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, InboxItemInfo? item) in source)
            {
                if (string.IsNullOrWhiteSpace(name) || item?.Identity == null)
                {
                    continue;
                }

                if (!Enum.IsDefined(item.Reliability))
                {
                    item.Reliability = ClassificationReliability.Conservative;
                }
                if (!Enum.IsDefined(item.ReviewState))
                {
                    item.ReviewState = InboxReviewState.Pending;
                }
                if (!Enum.IsDefined(item.Identity.Kind))
                {
                    item.Identity.Kind = DesktopItemKind.FileSystem;
                }
                item.Identity.LastKnownPath = NormalizePersistedPath(item.Identity.LastKnownPath);
                item.Identity.FileId = string.IsNullOrWhiteSpace(item.Identity.FileId)
                    ? null
                    : item.Identity.FileId.Trim();
                item.Identity.ShellParsingName = string.IsNullOrWhiteSpace(item.Identity.ShellParsingName)
                    ? null
                    : item.Identity.ShellParsingName.Trim();
                item.SuggestedCategoryKey = item.SuggestedCategoryKey?.Trim() ?? string.Empty;
                item.SuggestedCategoryName = item.SuggestedCategoryName?.Trim() ?? string.Empty;
                item.MatchReason = item.MatchReason?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(item.SuggestedCategoryKey))
                {
                    item.Reliability = ClassificationReliability.Conservative;
                }
                result[name] = item;
            }
            return result;
        }

        private static Dictionary<string, List<string>> NormalizeItemTags(
            IEnumerable<KeyValuePair<string, List<string>>> source)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, List<string>? tags) in source)
            {
                if (string.IsNullOrWhiteSpace(name) || tags == null)
                {
                    continue;
                }
                List<string> normalized = tags
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(tag => tag.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                if (normalized.Count > 0)
                {
                    result[name] = normalized;
                }
            }
            return result;
        }

        private static Dictionary<string, long> NormalizeItemTimes(
            IEnumerable<KeyValuePair<string, long>> source)
        {
            var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, long ticks) in source)
            {
                if (!string.IsNullOrWhiteSpace(name) && ticks >= DateTime.MinValue.Ticks &&
                    ticks <= DateTime.MaxValue.Ticks)
                {
                    result[name] = ticks;
                }
            }
            return result;
        }

    }
}
