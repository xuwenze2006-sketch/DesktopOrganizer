namespace DesktopOrganizer
{
    internal sealed record WorkspacePreview(
        string Id,
        string Name,
        int GroupCount,
        int FreeIconCount,
        int MonitorCount,
        DateTime UpdatedUtc);

    /// <summary>
    /// 命名工作区的纯模型操作。所有方法只复制或替换 AppLayoutData 中的视觉字段，
    /// 不接触文件系统，也不调用真实文件任务服务。
    /// </summary>
    internal static class WorkspaceLayoutManager
    {
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
                    workspace.Layout.DesktopTopology.Count,
                    workspace.UpdatedUtc))
                .ToList();

        public static WorkspaceLayoutState Capture(AppLayoutData layout) => new()
        {
            Version = 1,
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
                StringComparer.OrdinalIgnoreCase)
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
            snapshot.Version = 1;
            snapshot.RecycleBinWidget ??= new RecycleBinWidgetLayoutInfo();
            snapshot.FreeIcons ??= new Dictionary<string, IconPosition>();
            snapshot.Groups ??= new List<GroupInfo>();
            snapshot.DesktopTopology ??= new List<DesktopMonitorLayoutInfo>();
            snapshot.AutoClassificationOriginalPositions ??= new Dictionary<string, IconPosition>();
            snapshot.FreeIcons = NormalizePositions(snapshot.FreeIcons);
            snapshot.AutoClassificationOriginalPositions = NormalizePositions(
                snapshot.AutoClassificationOriginalPositions);

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

        private static GroupInfo Clone(GroupInfo group) => new()
        {
            Id = group.Id,
            Name = group.Name,
            X = group.X,
            Y = group.Y,
            Width = group.Width,
            Height = group.Height,
            ItemNames = (group.ItemNames ?? new List<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            IsCollapsed = group.IsCollapsed,
            IsAutoCategory = group.IsAutoCategory,
            AutoCategoryKey = group.AutoCategoryKey,
            IsSizeLocked = group.IsSizeLocked,
            SortMode = group.SortMode
        };

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
