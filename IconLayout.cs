namespace DesktopOrganizer
{
    /// <summary>单个自由摆放图标的坐标。</summary>
    internal sealed class IconPosition
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    /// <summary>
    /// 桌面项目的持久化身份。FileId 由卷序列号与 128 位文件 ID 组成，
    /// 用于在文件改名或程序关闭期间发生重命名后找回原布局。
    /// </summary>
    internal sealed class DesktopItemIdentityInfo
    {
        public DesktopItemKind Kind { get; set; } = DesktopItemKind.FileSystem;
        public string LastKnownPath { get; set; } = string.Empty;
        public string? FileId { get; set; }
        public string? ShellParsingName { get; set; }
        public long? CreationTimeUtcTicks { get; set; }
        public bool IsDirectory { get; set; }
    }

    /// <summary>分类框内部项目的排序方式。</summary>
    internal enum GroupSortMode
    {
        Custom,
        Name,
        Type,
        ModifiedTime,
        CreatedTime,
        FoldersFirst
    }

    /// <summary>分组卡片信息：位置、大小以及包含的桌面项名称。</summary>
    internal sealed class GroupInfo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "新分组";
        public double X { get; set; } = 40;
        public double Y { get; set; } = 40;
        public double Width { get; set; } = 280;
        public double Height { get; set; } = 200;
        public List<string> ItemNames { get; set; } = new();

        /// <summary>收起时只显示标题栏，减少桌面遮挡；Height 始终保存展开高度。</summary>
        public bool IsCollapsed { get; set; }

        /// <summary>由自动识别创建的虚拟分类分组；手工分组始终具有更高优先级。</summary>
        public bool IsAutoCategory { get; set; }

        /// <summary>稳定的分类键，允许分类名称被用户重命名后仍可继续归类。</summary>
        public string? AutoCategoryKey { get; set; }

        /// <summary>用户手动缩放后锁定尺寸；为 false 时根据图标数量自动适应。</summary>
        public bool IsSizeLocked { get; set; }

        /// <summary>分类框内部项目排序；Custom 保留用户拖放顺序。</summary>
        public GroupSortMode SortMode { get; set; } = GroupSortMode.Custom;
    }

    /// <summary>上次保存时的显示器工作区，用于拓扑或缩放变化后的坐标迁移。</summary>
    internal sealed class DesktopMonitorLayoutInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public double BoundsX { get; set; }
        public double BoundsY { get; set; }
        public double BoundsWidth { get; set; }
        public double BoundsHeight { get; set; }
        public double WorkX { get; set; }
        public double WorkY { get; set; }
        public double WorkWidth { get; set; }
        public double WorkHeight { get; set; }
        public bool IsPrimary { get; set; }
        public uint DpiX { get; set; } = 96;
        public uint DpiY { get; set; } = 96;
    }

    /// <summary>回收站专属小组件的位置与显示偏好。</summary>
    internal sealed class RecycleBinWidgetLayoutInfo
    {
        public double? X { get; set; }
        public double? Y { get; set; }
        public bool IsVisible { get; set; } = true;
    }

    /// <summary>
    /// 一个命名工作区保存的纯视觉布局状态。它不保存文件操作、桌面文件身份或自动化规则，
    /// 因此恢复工作区只会改变本程序的布局模型，不会移动、重命名或删除真实文件。
    /// </summary>
    internal sealed class WorkspaceLayoutState
    {
        public int Version { get; set; } = 1;
        public double? ControlPanelX { get; set; }
        public double? ControlPanelY { get; set; }
        public RecycleBinWidgetLayoutInfo RecycleBinWidget { get; set; } = new();
        public Dictionary<string, IconPosition> FreeIcons { get; set; } = new();
        public List<GroupInfo> Groups { get; set; } = new();
        public List<DesktopMonitorLayoutInfo> DesktopTopology { get; set; } = new();
        public Dictionary<string, IconPosition> AutoClassificationOriginalPositions { get; set; } = new();
    }

    /// <summary>用户命名的本地工作区及其最近一次保存的视觉快照。</summary>
    internal sealed class WorkspaceProfileInfo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "新工作区";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        public WorkspaceLayoutState Layout { get; set; } = new();
    }

    /// <summary>布局持久化根对象。</summary>
    internal sealed class AppLayoutData
    {
        public int Version { get; set; } = 17;
        public long SaveGeneration { get; set; }
        public bool SnapToGrid { get; set; } = true;
        public bool PushReflowEnabled { get; set; } = true;

        /// <summary>开启后，仅把刷新时发现的新桌面项目自动加入对应自动分类。</summary>
        public bool AutoClassifyNewItems { get; set; }

        /// <summary>编辑模式下才允许拖动、缩放、删除分组以及移动图标。</summary>
        public bool IsEditMode { get; set; }

        /// <summary>展开后的总面板在鼠标离开一段时间后自动收起。</summary>
        public bool AutoCollapseControlPanel { get; set; } = true;

        /// <summary>使用更紧凑的分类框尺寸，并允许大型分类使用四列图标。</summary>
        public bool CompactGroupLayout { get; set; } = true;

        /// <summary>智能布局优先保留主屏底部约三分之一，作为临时文件和壁纸留白区域。</summary>
        public bool ReserveTemporaryWorkspace { get; set; } = true;

        /// <summary>总控制面板左上角坐标；null 表示首次启动时停靠在右上角。</summary>
        public double? ControlPanelX { get; set; }
        public double? ControlPanelY { get; set; }

        /// <summary>v15 起回收站从普通桌面图标体系中独立出来。</summary>
        public RecycleBinWidgetLayoutInfo RecycleBinWidget { get; set; } = new();

        public Dictionary<string, IconPosition> FreeIcons { get; set; } = new();
        public List<GroupInfo> Groups { get; set; } = new();

        /// <summary>v13 起保存虚拟桌面中的显示器工作区与 DPI 快照。</summary>
        public List<DesktopMonitorLayoutInfo> DesktopTopology { get; set; } = new();

        /// <summary>
        /// 以当前显示名称为索引保存最近一次扫描到的稳定身份；v14 起也保存 Shell parsing name。
        /// 旧版本没有该字段时会在首次扫描后自动补齐。
        /// </summary>
        public Dictionary<string, DesktopItemIdentityInfo> ItemIdentities { get; set; } = new();

        /// <summary>
        /// 首次自动分类前的自由图标位置，用于“取消分类”时尽量恢复。
        /// 这里只保存布局坐标，不移动、修改或删除真实文件。
        /// </summary>
        public Dictionary<string, IconPosition> AutoClassificationOriginalPositions { get; set; } = new();

        /// <summary>v16 起保存的命名工作区；当前根布局始终表示正在显示的工作区。</summary>
        public List<WorkspaceProfileInfo> Workspaces { get; set; } = new();

        /// <summary>当前工作区 ID；null 表示仍使用兼容的未命名根布局。</summary>
        public string? ActiveWorkspaceId { get; set; }

        /// <summary>首次完整桌面扫描只建立基线，避免升级后把已有项目全部加入收件箱。</summary>
        public bool InboxBaselineEstablished { get; set; }

        /// <summary>待用户确认的桌面新项目；只保存本地建议和身份，不改变真实文件。</summary>
        public Dictionary<string, InboxItemInfo> InboxItems { get; set; } = new();

        /// <summary>本地标签；不改文件名，也不在文件旁创建 sidecar。</summary>
        public Dictionary<string, List<string>> ItemTags { get; set; } = new();

        /// <summary>项目首次作为“新项目”被完整扫描发现的 UTC ticks。</summary>
        public Dictionary<string, long> ItemFirstSeenUtcTicks { get; set; } = new();

        /// <summary>最近一次虚拟归组或标签规则操作的 UTC ticks。</summary>
        public Dictionary<string, long> ItemLastMovedUtcTicks { get; set; } = new();
    }

    /// <summary>挂在图标控件 Tag 上的数据。</summary>
    internal sealed class IconTag
    {
        public string DisplayName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public DesktopItemKind Kind { get; set; } = DesktopItemKind.FileSystem;
        public GroupInfo? Group { get; set; }
    }
}
