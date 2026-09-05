namespace DesktopOrganizer
{
    internal sealed record WorkspacePreview(
        string Id,
        string Name,
        int GroupCount,
        int FreeIconCount,
        int PortalCount,
        int MonitorCount,
        DateTime UpdatedUtc);

    internal sealed record WorkspaceSwitchImpact(
        int ChangedGroupCount,
        int ChangedFreeIconCoordinateCount,
        int ChangedPortalCount,
        bool ControlPanelChanged,
        bool RecycleBinWidgetChanged,
        bool DesktopTopologyChanged)
    {
        public bool HasVisibleChanges =>
            ChangedGroupCount > 0 ||
            ChangedFreeIconCoordinateCount > 0 ||
            ChangedPortalCount > 0 ||
            ControlPanelChanged ||
            RecycleBinWidgetChanged ||
            DesktopTopologyChanged;
    }

    /// <summary>
    /// 命名工作区的纯模型操作。所有方法只复制或替换 AppLayoutData 中的视觉字段，
    /// 不接触文件系统，也不调用真实文件任务服务。
    /// </summary>
    internal static class WorkspaceLayoutManager
    {
        private const double CoordinateComparisonTolerance = 0.01;

        public static WorkspaceProfileInfo CreateAndActivate(
            AppLayoutData layout,
            string name,
            DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(layout);
            string normalizedName = NormalizeName(name);
            if (layout.Workspaces.Any(workspace =>
                    workspace.Name.Equals(normalizedName, StringComparison.CurrentCultureIgnoreCase)))
            {
                throw new InvalidOperationException("已经存在同名工作区。");
            }

            UpdateActiveSnapshot(layout, utcNow);
            var workspace = new WorkspaceProfileInfo
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = normalizedName,
                CreatedUtc = utcNow,
                UpdatedUtc = utcNow,
                Layout = Capture(layout)
            };
            layout.Workspaces.Add(workspace);
            layout.ActiveWorkspaceId = workspace.Id;
            return workspace;
        }

        public static bool TryActivate(AppLayoutData layout, string workspaceId, DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(layout);
            WorkspaceProfileInfo? target = Find(layout, workspaceId);
            if (target == null)
            {
                return false;
            }

            if (string.Equals(layout.ActiveWorkspaceId, target.Id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            UpdateActiveSnapshot(layout, utcNow);
            Apply(layout, target.Layout);
            layout.ActiveWorkspaceId = target.Id;
            return true;
        }

        public static bool UpdateActiveSnapshot(AppLayoutData layout, DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(layout);
            WorkspaceProfileInfo? active = Find(layout, layout.ActiveWorkspaceId);
            if (active == null)
            {
                return false;
            }

            active.Layout = Capture(layout);
            active.UpdatedUtc = utcNow;
            return true;
        }

        public static bool Overwrite(AppLayoutData layout, string workspaceId, DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(layout);
            WorkspaceProfileInfo? workspace = Find(layout, workspaceId);
            if (workspace == null)
            {
                return false;
            }

            workspace.Layout = Capture(layout);
            workspace.UpdatedUtc = utcNow;
            return true;
        }

        public static WorkspaceProfileInfo? Duplicate(
            AppLayoutData layout,
            string sourceWorkspaceId,
            string name,
            DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(layout);
            WorkspaceProfileInfo? source = Find(layout, sourceWorkspaceId);
            if (source == null)
            {
                return null;
            }

            string normalizedName = NormalizeName(name);
            if (layout.Workspaces.Any(workspace =>
                    workspace.Name.Equals(
                        normalizedName,
                        StringComparison.CurrentCultureIgnoreCase)))
            {
                throw new InvalidOperationException("已经存在同名工作区。");
            }

            var duplicate = new WorkspaceProfileInfo
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = normalizedName,
                CreatedUtc = utcNow,
                UpdatedUtc = utcNow,
                Layout = Clone(source.Layout)
            };
            layout.Workspaces.Add(duplicate);
            return duplicate;
        }

        public static bool Rename(AppLayoutData layout, string workspaceId, string name)
        {
            ArgumentNullException.ThrowIfNull(layout);
            WorkspaceProfileInfo? workspace = Find(layout, workspaceId);
            if (workspace == null)
            {
                return false;
            }

            string normalizedName = NormalizeName(name);
            if (layout.Workspaces.Any(candidate =>
                    !candidate.Id.Equals(workspace.Id, StringComparison.OrdinalIgnoreCase) &&
                    candidate.Name.Equals(normalizedName, StringComparison.CurrentCultureIgnoreCase)))
            {
                throw new InvalidOperationException("已经存在同名工作区。");
            }

            workspace.Name = normalizedName;
            return true;
        }

        public static bool Delete(AppLayoutData layout, string workspaceId)
        {
            ArgumentNullException.ThrowIfNull(layout);
            int removed = layout.Workspaces.RemoveAll(workspace =>
                workspace.Id.Equals(workspaceId, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                return false;
            }

            if (string.Equals(layout.ActiveWorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
            {
                layout.ActiveWorkspaceId = null;
            }
            return true;
        }

        public static void RemoveItemFromSnapshots(AppLayoutData layout, string displayName)
        {
            ArgumentNullException.ThrowIfNull(layout);
            foreach (WorkspaceProfileInfo workspace in layout.Workspaces)
            {
                foreach (GroupInfo group in workspace.Layout.Groups)
                {
                    group.ItemNames.RemoveAll(item =>
                        item.Equals(displayName, StringComparison.OrdinalIgnoreCase));
                    group.ManuallyAssignedItemNames.RemoveAll(item =>
                        item.Equals(displayName, StringComparison.OrdinalIgnoreCase));
                }
                workspace.Layout.FreeIcons.Remove(displayName);
                workspace.Layout.AutoClassificationOriginalPositions.Remove(displayName);
            }
        }

        public static IReadOnlyList<WorkspacePreview> GetPreviews(AppLayoutData layout) =>
            layout.Workspaces
                .OrderBy(workspace => workspace.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(workspace => new WorkspacePreview(
                    workspace.Id,
                    workspace.Name,
                    workspace.Layout.Groups.Count,
                    workspace.Layout.FreeIcons.Count,
                    workspace.Layout.FolderPortals.Count,
                    workspace.Layout.DesktopTopology.Count,
                    workspace.UpdatedUtc))
                .ToList();

        public static WorkspaceSwitchImpact? GetSwitchImpact(
            AppLayoutData layout,
            string workspaceId)
        {
            ArgumentNullException.ThrowIfNull(layout);
            WorkspaceProfileInfo? target = Find(layout, workspaceId);
            if (target == null)
            {
                return null;
            }

            WorkspaceLayoutState snapshot = target.Layout;
            return new WorkspaceSwitchImpact(
                CountChangedEntries(
                    layout.Groups,
                    snapshot.Groups,
                    group => group.Id,
                    GroupsEquivalent),
                CountChangedFreeIconCoordinates(layout.FreeIcons, snapshot.FreeIcons),
                CountChangedEntries(
                    layout.FolderPortals,
                    snapshot.FolderPortals,
                    portal => portal.Id,
                    PortalsEquivalent),
                !NullableCoordinatesEquivalent(
                    layout.ControlPanelX,
                    layout.ControlPanelY,
                    snapshot.ControlPanelX,
                    snapshot.ControlPanelY),
                !RecycleBinWidgetsEquivalent(
                    layout.RecycleBinWidget,
                    snapshot.RecycleBinWidget),
                CountChangedEntries(
                    layout.DesktopTopology,
                    snapshot.DesktopTopology,
                    monitor => monitor.DeviceName,
                    DesktopMonitorsEquivalent) > 0);
        }

        public static WorkspaceLayoutState Capture(AppLayoutData layout) => new()
        {
            Version = 3,
            ControlPanelX = layout.ControlPanelX,
            ControlPanelY = layout.ControlPanelY,
            RecycleBinWidget = Clone(layout.RecycleBinWidget),
            FreeIcons = layout.FreeIcons.ToDictionary(
                pair => pair.Key,
                pair => Clone(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            Groups = layout.Groups.Select(Clone).ToList(),
            DesktopTopology = layout.DesktopTopology.Select(Clone).ToList(),
            AutoClassificationOriginalPositions = layout.AutoClassificationOriginalPositions.ToDictionary(
                pair => pair.Key,
                pair => Clone(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            FolderPortals = layout.FolderPortals
                .Select(FolderPortalLayoutPolicy.Clone)
                .ToList()
        };

        public static void Apply(AppLayoutData layout, WorkspaceLayoutState snapshot)
        {
            ArgumentNullException.ThrowIfNull(layout);
            ArgumentNullException.ThrowIfNull(snapshot);
            layout.ControlPanelX = snapshot.ControlPanelX;
            layout.ControlPanelY = snapshot.ControlPanelY;
            layout.RecycleBinWidget = Clone(snapshot.RecycleBinWidget);
            layout.FreeIcons = snapshot.FreeIcons.ToDictionary(
                pair => pair.Key,
                pair => Clone(pair.Value),
                StringComparer.OrdinalIgnoreCase);
            layout.Groups = snapshot.Groups.Select(Clone).ToList();
            layout.DesktopTopology = snapshot.DesktopTopology.Select(Clone).ToList();
            layout.AutoClassificationOriginalPositions = snapshot.AutoClassificationOriginalPositions.ToDictionary(
                pair => pair.Key,
                pair => Clone(pair.Value),
                StringComparer.OrdinalIgnoreCase);
            layout.FolderPortals = snapshot.FolderPortals
                .Select(FolderPortalLayoutPolicy.Clone)
                .ToList();
        }

        public static void Normalize(AppLayoutData layout)
        {
            ArgumentNullException.ThrowIfNull(layout);
            layout.Workspaces ??= new List<WorkspaceProfileInfo>();
            var normalized = new List<WorkspaceProfileInfo>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (WorkspaceProfileInfo workspace in layout.Workspaces.OfType<WorkspaceProfileInfo>())
            {
                workspace.Id = string.IsNullOrWhiteSpace(workspace.Id)
                    ? Guid.NewGuid().ToString("N")
                    : workspace.Id.Trim();
                while (!ids.Add(workspace.Id))
                {
                    workspace.Id = Guid.NewGuid().ToString("N");
                }

                string baseName = string.IsNullOrWhiteSpace(workspace.Name)
                    ? "未命名工作区"
                    : workspace.Name.Trim();
                string uniqueName = baseName;
                int suffix = 2;
                while (!names.Add(uniqueName))
                {
                    uniqueName = $"{baseName} ({suffix++})";
                }
                workspace.Name = uniqueName;
                workspace.Layout ??= new WorkspaceLayoutState();
                Normalize(workspace.Layout);
                if (workspace.CreatedUtc == default)
                {
                    workspace.CreatedUtc = workspace.UpdatedUtc == default
                        ? DateTime.UnixEpoch
                        : workspace.UpdatedUtc;
                }
                if (workspace.UpdatedUtc == default)
                {
                    workspace.UpdatedUtc = workspace.CreatedUtc;
                }
                normalized.Add(workspace);
            }

            layout.Workspaces = normalized;
            if (Find(layout, layout.ActiveWorkspaceId) == null)
            {
                layout.ActiveWorkspaceId = null;
            }
        }

        private static void Normalize(WorkspaceLayoutState snapshot)
        {
            snapshot.Version = 3;
            snapshot.RecycleBinWidget ??= new RecycleBinWidgetLayoutInfo();
            snapshot.FreeIcons ??= new Dictionary<string, IconPosition>();
            snapshot.Groups ??= new List<GroupInfo>();
            snapshot.DesktopTopology ??= new List<DesktopMonitorLayoutInfo>();
            snapshot.AutoClassificationOriginalPositions ??= new Dictionary<string, IconPosition>();
            snapshot.FolderPortals ??= new List<FolderPortalInfo>();
            snapshot.FreeIcons = NormalizePositions(snapshot.FreeIcons);
            snapshot.AutoClassificationOriginalPositions = NormalizePositions(
                snapshot.AutoClassificationOriginalPositions);
            snapshot.FolderPortals = FolderPortalLayoutPolicy.Normalize(
                snapshot.FolderPortals.Select(FolderPortalLayoutPolicy.Clone));

            var groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groups = new List<GroupInfo>();
            foreach (GroupInfo source in snapshot.Groups.OfType<GroupInfo>())
            {
                GroupInfo group = Clone(source);
                group.Id = group.Id?.Trim() ?? string.Empty;
                while (string.IsNullOrWhiteSpace(group.Id) || !groupIds.Add(group.Id))
                {
                    group.Id = Guid.NewGuid().ToString("N");
                }
                group.Name = string.IsNullOrWhiteSpace(group.Name)
                    ? "未命名分组"
                    : group.Name.Trim();
                if (!Enum.IsDefined(group.SortMode))
                {
                    group.SortMode = GroupSortMode.Custom;
                }
                groups.Add(group);
            }
            snapshot.Groups = groups;

            var monitorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            snapshot.DesktopTopology = snapshot.DesktopTopology
                .OfType<DesktopMonitorLayoutInfo>()
                .Where(monitor =>
                    !string.IsNullOrWhiteSpace(monitor.DeviceName) &&
                    monitorNames.Add(monitor.DeviceName.Trim()))
                .Select(Clone)
                .ToList();
        }

        private static Dictionary<string, IconPosition> NormalizePositions(
            IEnumerable<KeyValuePair<string, IconPosition>> positions)
        {
            var normalized = new Dictionary<string, IconPosition>(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, IconPosition? position) in positions)
            {
                if (!string.IsNullOrWhiteSpace(name) && position != null)
                {
                    normalized[name] = Clone(position);
                }
            }
            return normalized;
        }

        private static int CountChangedFreeIconCoordinates(
            IReadOnlyDictionary<string, IconPosition> current,
            IReadOnlyDictionary<string, IconPosition> target)
        {
            Dictionary<string, IconPosition> currentByName = current.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, IconPosition> targetByName = target.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(currentByName.Keys, StringComparer.OrdinalIgnoreCase);
            names.UnionWith(targetByName.Keys);
            return names.Count(name =>
                !currentByName.TryGetValue(name, out IconPosition? currentPosition) ||
                !targetByName.TryGetValue(name, out IconPosition? targetPosition) ||
                !PositionsEquivalent(currentPosition, targetPosition));
        }

        private static int CountChangedEntries<T>(
            IEnumerable<T> current,
            IEnumerable<T> target,
            Func<T, string> keySelector,
            Func<T, T, bool> equivalent)
            where T : class
        {
            Dictionary<string, T> currentById = current.ToDictionary(
                keySelector,
                item => item,
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, T> targetById = target.ToDictionary(
                keySelector,
                item => item,
                StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>(currentById.Keys, StringComparer.OrdinalIgnoreCase);
            ids.UnionWith(targetById.Keys);
            return ids.Count(id =>
                !currentById.TryGetValue(id, out T? currentItem) ||
                !targetById.TryGetValue(id, out T? targetItem) ||
                !equivalent(currentItem, targetItem));
        }

        private static bool GroupsEquivalent(GroupInfo current, GroupInfo target) =>
            string.Equals(current.Name, target.Name, StringComparison.Ordinal) &&
            CoordinatesEquivalent(current.X, current.Y, target.X, target.Y) &&
            CoordinatesEquivalent(current.Width, current.Height, target.Width, target.Height) &&
            (current.ItemNames ?? new List<string>()).SequenceEqual(
                target.ItemNames ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase) &&
            (current.ManuallyAssignedItemNames ?? new List<string>()).SequenceEqual(
                target.ManuallyAssignedItemNames ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase) &&
            current.IsCollapsed == target.IsCollapsed &&
            current.IsAutoCategory == target.IsAutoCategory &&
            string.Equals(
                current.AutoCategoryKey,
                target.AutoCategoryKey,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                current.UserRuleId,
                target.UserRuleId,
                StringComparison.OrdinalIgnoreCase) &&
            current.IsSizeLocked == target.IsSizeLocked &&
            current.UseUniformTrackWidth == target.UseUniformTrackWidth &&
            current.DesktopRole == target.DesktopRole &&
            current.SortMode == target.SortMode;

        private static bool PortalsEquivalent(FolderPortalInfo current, FolderPortalInfo target) =>
            string.Equals(current.Name, target.Name, StringComparison.Ordinal) &&
            string.Equals(current.RootPath, target.RootPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(current.RootIdentity, target.RootIdentity, StringComparison.Ordinal) &&
            string.Equals(
                current.CurrentRelativePath,
                target.CurrentRelativePath,
                StringComparison.OrdinalIgnoreCase) &&
            CoordinatesEquivalent(current.X, current.Y, target.X, target.Y) &&
            CoordinatesEquivalent(current.Width, current.Height, target.Width, target.Height) &&
            current.IsCollapsed == target.IsCollapsed;

        private static bool RecycleBinWidgetsEquivalent(
            RecycleBinWidgetLayoutInfo current,
            RecycleBinWidgetLayoutInfo target) =>
            NullableCoordinatesEquivalent(current.X, current.Y, target.X, target.Y) &&
            current.IsVisible == target.IsVisible;

        private static bool DesktopMonitorsEquivalent(
            DesktopMonitorLayoutInfo current,
            DesktopMonitorLayoutInfo target) =>
            CoordinatesEquivalent(
                current.BoundsX,
                current.BoundsY,
                target.BoundsX,
                target.BoundsY) &&
            CoordinatesEquivalent(
                current.BoundsWidth,
                current.BoundsHeight,
                target.BoundsWidth,
                target.BoundsHeight) &&
            CoordinatesEquivalent(
                current.WorkX,
                current.WorkY,
                target.WorkX,
                target.WorkY) &&
            CoordinatesEquivalent(
                current.WorkWidth,
                current.WorkHeight,
                target.WorkWidth,
                target.WorkHeight) &&
            current.IsPrimary == target.IsPrimary &&
            current.DpiX == target.DpiX &&
            current.DpiY == target.DpiY;

        private static bool PositionsEquivalent(IconPosition current, IconPosition target) =>
            CoordinatesEquivalent(current.X, current.Y, target.X, target.Y);

        private static bool CoordinatesEquivalent(
            double currentX,
            double currentY,
            double targetX,
            double targetY) =>
            Math.Abs(currentX - targetX) <= CoordinateComparisonTolerance &&
            Math.Abs(currentY - targetY) <= CoordinateComparisonTolerance;

        private static bool NullableCoordinatesEquivalent(
            double? currentX,
            double? currentY,
            double? targetX,
            double? targetY) =>
            NullableCoordinateEquivalent(currentX, targetX) &&
            NullableCoordinateEquivalent(currentY, targetY);

        private static bool NullableCoordinateEquivalent(double? current, double? target) =>
            current.HasValue == target.HasValue &&
            (!current.HasValue ||
             Math.Abs(current.Value - target!.Value) <= CoordinateComparisonTolerance);

        private static WorkspaceProfileInfo? Find(AppLayoutData layout, string? workspaceId) =>
            string.IsNullOrWhiteSpace(workspaceId)
                ? null
                : layout.Workspaces.FirstOrDefault(workspace =>
                    workspace.Id.Equals(workspaceId, StringComparison.OrdinalIgnoreCase));

        private static string NormalizeName(string name)
        {
            string normalized = name?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(normalized)
                ? throw new ArgumentException("工作区名称不能为空。", nameof(name))
                : normalized;
        }

        private static IconPosition Clone(IconPosition position) => new()
        {
            X = position.X,
            Y = position.Y
        };

        private static RecycleBinWidgetLayoutInfo Clone(RecycleBinWidgetLayoutInfo widget) => new()
        {
            X = widget.X,
            Y = widget.Y,
            IsVisible = widget.IsVisible
        };

        private static WorkspaceLayoutState Clone(WorkspaceLayoutState snapshot) => new()
        {
            Version = snapshot.Version,
            ControlPanelX = snapshot.ControlPanelX,
            ControlPanelY = snapshot.ControlPanelY,
            RecycleBinWidget = Clone(snapshot.RecycleBinWidget),
            FreeIcons = snapshot.FreeIcons.ToDictionary(
                pair => pair.Key,
                pair => Clone(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            Groups = snapshot.Groups.Select(Clone).ToList(),
            DesktopTopology = snapshot.DesktopTopology.Select(Clone).ToList(),
            AutoClassificationOriginalPositions =
                snapshot.AutoClassificationOriginalPositions.ToDictionary(
                    pair => pair.Key,
                    pair => Clone(pair.Value),
                    StringComparer.OrdinalIgnoreCase),
            FolderPortals = snapshot.FolderPortals
                .Select(FolderPortalLayoutPolicy.Clone)
                .ToList()
        };

        private static GroupInfo Clone(GroupInfo group)
        {
            List<string> itemNames = (group.ItemNames ?? new List<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var canonicalItemNames = itemNames.ToDictionary(
                item => item,
                item => item,
                StringComparer.OrdinalIgnoreCase);
            var seenManualAssignments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> manuallyAssignedItemNames =
                (group.ManuallyAssignedItemNames ?? new List<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => canonicalItemNames.TryGetValue(item, out string? canonicalItem)
                    ? canonicalItem
                    : null)
                .Where(item => item != null && seenManualAssignments.Add(item))
                .Select(item => item!)
                .ToList();

            return new GroupInfo
            {
                Id = group.Id,
                Name = group.Name,
                X = group.X,
                Y = group.Y,
                Width = group.Width,
                Height = group.Height,
                ItemNames = itemNames,
                ManuallyAssignedItemNames = manuallyAssignedItemNames,
                IsCollapsed = group.IsCollapsed,
                IsAutoCategory = group.IsAutoCategory,
                AutoCategoryKey = group.AutoCategoryKey,
                UserRuleId = group.UserRuleId,
                IsSizeLocked = group.IsSizeLocked,
                UseUniformTrackWidth = group.UseUniformTrackWidth,
                DesktopRole = group.DesktopRole,
                SortMode = group.SortMode
            };
        }

        private static DesktopMonitorLayoutInfo Clone(DesktopMonitorLayoutInfo monitor) => new()
        {
            DeviceName = monitor.DeviceName,
            BoundsX = monitor.BoundsX,
            BoundsY = monitor.BoundsY,
            BoundsWidth = monitor.BoundsWidth,
            BoundsHeight = monitor.BoundsHeight,
            WorkX = monitor.WorkX,
            WorkY = monitor.WorkY,
            WorkWidth = monitor.WorkWidth,
            WorkHeight = monitor.WorkHeight,
            IsPrimary = monitor.IsPrimary,
            DpiX = monitor.DpiX,
            DpiY = monitor.DpiY
        };
    }
}
