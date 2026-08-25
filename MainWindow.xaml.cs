namespace DesktopOrganizer
{
    public partial class MainWindow : Window
    {
        private const double IconCellWidth = 90;
        private const double IconCellHeight = 90;
        private const double GridOriginX = 20;
        private const double GridOriginY = 70;
        private const double GroupMinWidth = 180;
        private const double GroupMinHeight = 130;
        private const double GroupHeaderHeight = 38;
        private const double GroupPreferredWidth = 280;
        private const double GroupMaxAutoWidth = 360;
        private const double GroupedIconRowHeight = 82;
        private const int GroupMaxAutoRows = 3;
        private const double RecycleBinWidgetWidth = 252;
        private const double RecycleBinWidgetHeight = 122;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int HTTRANSPARENT = -1;
        private const int HTCLIENT = 1;
        private const int MA_NOACTIVATE = 3;
        private static readonly Duration PushAnimationDuration = new(TimeSpan.FromMilliseconds(120));
        private static readonly Brush IconHoverBrush = CreateFrozenBrush(Color.FromArgb(38, 255, 255, 255));
        private static readonly Brush IconLabelBackgroundBrush = CreateFrozenBrush(Color.FromArgb(105, 0, 0, 0));
        private static readonly Brush FolderDropHighlightBrush = CreateFrozenBrush(Color.FromArgb(72, 67, 214, 135));
        private static readonly Brush FolderDropBorderBrush = CreateFrozenBrush(Color.FromArgb(245, 83, 224, 151));
        private static readonly Brush GroupDropHighlightBrush = CreateFrozenBrush(Color.FromArgb(58, 72, 160, 255));
        private static readonly Brush GroupDropBorderBrush = CreateFrozenBrush(Color.FromArgb(238, 105, 190, 255));
        private static readonly Brush SelectedIconBackgroundBrush = CreateFrozenBrush(Color.FromArgb(78, 83, 170, 255));
        private static readonly Brush SelectedIconBorderBrush = CreateFrozenBrush(Color.FromArgb(238, 132, 208, 255));
        private static readonly Brush RecycleBinUnavailableBrush = CreateFrozenBrush(Color.FromRgb(148, 163, 184));
        private static readonly Brush RecycleBinEmptyBrush = CreateFrozenBrush(Color.FromRgb(94, 234, 212));
        private static readonly Brush RecycleBinOccupiedBrush = CreateFrozenBrush(Color.FromRgb(251, 191, 36));

        private readonly string _userDesktopPath =
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        private readonly string _commonDesktopPath =
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

        private readonly string _layoutFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesktopOrganizer",
            "layout.json");

        private readonly string _layoutExitRecoveryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesktopOrganizer",
            "layout.pending-exit.json");

        private readonly string _sessionMarkerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesktopOrganizer",
            "restore-native-icons.flag");

        private readonly DispatcherTimer _layoutSaveTimer;
        private readonly DispatcherTimer _nativeIconGuardTimer;
        private readonly DispatcherTimer _controlPanelAutoCollapseTimer;
        private readonly DispatcherTimer _healthMonitorTimer;
        private readonly DispatcherTimer _recycleBinStatusTimer;
        private readonly AppDiagnostics _diagnostics = new();
        private readonly CancellationTokenSource _lifetimeCts = new();
        private readonly FileOperationService _fileOperationService;
        private readonly object _refreshDebounceLock = new();
        private readonly object _desktopRenameLock = new();
        private readonly object _externalEventLock = new();
        private readonly object _layoutWriteLock = new();
        private readonly SemaphoreSlim _layoutWriteGate = new(1, 1);
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly List<DesktopRenameOperation> _pendingDesktopRenames = new();
        private readonly Dictionary<string, BitmapSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FrameworkElement> _freeIconVisuals = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FrameworkElement> _allIconVisuals = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _selectedItemNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FrameworkElement> _groupDropTargets = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FrameworkElement> _groupVisuals = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, VirtualizingGroupPanel> _groupItemPanels = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _groupVisualFingerprints = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _groupScrollOffsets = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FreeIconVisualState> _freeIconVisualStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<FileMoveUndoRecord> _fileMoveHistory = new();
        private readonly HashSet<string> _pendingFileOperationPaths = new(StringComparer.OrdinalIgnoreCase);
        private DesktopGeometry _desktopGeometry = new([]);
        private Dictionary<string, string> _desktopItems = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, DesktopCategoryDefinition> _desktopCategories = new(StringComparer.OrdinalIgnoreCase);
        private bool _desktopSnapshotInitialized;
        private CancellationTokenSource? _refreshDebounceCts;
        private string? _pendingRefreshStatus;
        private int _refreshWorkerRunning;
        private int _refreshRequested;
        private int _clearIconCacheRequested;
        private bool _layoutDirty;
        private string? _pendingLayoutJson;
        private int _layoutWriterRunning;
        private bool _isRebuildingVisualTree;
        private bool _rebuildRequested;
        private int _visualRebuildQueued;
        private int _fileOperationCount;
        private int _iconVisualGeneration;
        private bool _commandsExpanded;
        private bool _panelToggleInProgress;
        private bool _autoClassifyInProgress;
        private int _displayUpdateQueued;
        private int _desktopLayerGuardFailures;
        private int _watcherRestartQueued;
        private int _incompleteScanRetryQueued;
        private int _consecutiveIncompleteScans;
        private int _staleCaptureRecoveryQueued;
        private DispatcherOperation? _pendingPanelClampOperation;
        private int _recycleBinStatusRefreshRunning;
        private RecycleBinStatus _recycleBinStatus;
        private bool _isRecycleBinEmptying;
        private bool _isRecycleBinWidgetDragging;
        private bool _recycleBinWidgetDragMoved;
        private Point _recycleBinWidgetDragStartMousePoint;
        private Point _recycleBinWidgetDragStartPosition;

        private AppLayoutData _appLayout = new();
        private UIElement? _draggedElement;
        private FrameworkElement? _pendingIconDragElement;
        private Point _pendingIconMouseDownCanvasPoint;
        private Point _dragStartOffset;
        private bool _draggedIsGroup;
        private GroupInfo? _draggedGroup;
        private FrameworkElement? _groupDragCaptureElement;
        private Point _groupDragStartPosition;
        private Point _groupDragMouseDownCanvasPoint;
        private bool _groupDragMoved;
        private bool _isCompletingGroupDrag;
        private bool _nativeIconsWereVisible;
        private int _desktopStateRestoreStarted;
        private bool _organizerPaused;
        private volatile bool _isClosing;
        private bool _isCompletingIconDrop;
        private GroupInfo? _groupedIconDragSourceGroup;
        private IconPosition? _dragOriginalPosition;
        private bool _dragAllowsLayoutMove;
        private readonly Dictionary<string, FrameworkElement> _physicalFolderDropTargets = new(StringComparer.OrdinalIgnoreCase);
        private FrameworkElement? _activePhysicalFolderDropVisual;
        private string? _activePhysicalFolderDropPath;
        private FrameworkElement? _activeGroupDropVisual;
        private GroupInfo? _activeGroupDropTarget;
        private DateTime _lastHealthTickUtc = DateTime.UtcNow;
        private int _consecutiveHealthWarnings;
        private bool _safeModeTriggeredThisSession;
        // 安全模式只属于当前进程，不写入 layout.json，也不覆盖用户偏好。
        // volatile 确保 FileSystemWatcher 的后台回调能及时看到状态变化。
        private volatile bool _isSafeModeActive;
        private bool IsPushReflowActive =>
            !_isSafeModeActive && _appLayout.SnapToGrid && _appLayout.PushReflowEnabled;
        private bool IsAutoClassificationActive =>
            !_isSafeModeActive && _appLayout.AutoClassifyNewItems;
        private bool _isControlPanelDragging;
        private bool _controlPanelDragMoved;
        private Point _controlPanelDragStartMousePoint;
        private Point _controlPanelDragStartPosition;
        private readonly bool _startQuietly;
        private HwndSource? _hwndSource;
        private TrayIconService? _trayIcon;
        private IDisposable? _externalWindowMonitor;
        private IDisposable? _desktopKeyboardMonitor;
        private int _externalLayerCorrectionQueued;
        private int _externalLayerGeneration;
        private int _desktopShellMenuActive;
        private int _shellMenuCloseGeneration;
        // 合并外部窗口 SHOW/FOREGROUND 事件；Shell 菜单打开期间暂停 Z 序校正，
        // 避免全屏 layered window 因 SetWindowPos 重绘而闪烁。
        private IntPtr _pendingExternalWindowHandle;
        private bool _pendingExternalWindowShouldActivate;
        private IntPtr _desktopHostHandle;
        private bool _isAttachedToDesktop;
        private Dictionary<string, GroupLayoutSnapshot>? _lastSmartLayoutSnapshot;
        private bool _lastSmartLayoutPreservedWorkspace;

        // 挤压排列仅在一次拖动会话中保存预览快照；未松开鼠标前不会写入布局。
        private Dictionary<string, IconPosition>? _pushPreviewOriginalPositions;
        private Dictionary<string, IconPosition>? _pushPreviewPositions;
        private string? _pushPreviewDraggedName;
        private string? _pushPreviewTargetName;

        private sealed record DesktopScanSnapshot(
            Dictionary<string, string> Items,
            Dictionary<string, DesktopCategoryDefinition> Categories,
            Dictionary<string, DesktopItemIdentityInfo> Identities,
            bool PhysicalScanComplete,
            bool ShellScanComplete);

        private sealed record DesktopRenameOperation(
            string OldFullPath,
            string NewFullPath);

        private sealed record GroupLayoutSnapshot(
            double X,
            double Y,
            double Width,
            double Height,
            bool IsCollapsed,
            bool IsSizeLocked);

        private readonly record struct FreeIconVisualState(
            string VisualKind,
            bool IsEditMode,
            bool FileOperationPending,
            int IconGeneration);

        private sealed record FreeIconVisualTarget(
            string FullPath,
            IconPosition Position,
            FreeIconVisualState State);

        private sealed record FileMoveUndoRecord(
            string DisplayName,
            string SourcePath,
            string DestinationPath,
            string? SourceGroupId,
            GroupInfo? SourceGroupSnapshot,
            int SourceGroupItemIndex,
            IconPosition? FreePosition,
            bool HadAutoClassificationPosition,
            DateTime CreatedUtc);

        private enum PhysicalFolderMoveResult
        {
            NotHandled,
            Queued,
            Rejected
        }

        public MainWindow() : this(startQuietly: false)
        {
        }

        internal MainWindow(bool startQuietly)
        {
            _startQuietly = startQuietly;
            InitializeComponent();
            _fileOperationService = new FileOperationService();
            if (_startQuietly)
            {
                ControlPanel.Visibility = Visibility.Collapsed;
            }

            ConfigureDesktopBounds();

            _layoutSaveTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                // 合并拖动、收起、排列等连续保存，避免在 UI 线程上反复写 layout.json。
                Interval = TimeSpan.FromMilliseconds(650)
            };
            _layoutSaveTimer.Tick += LayoutSaveTimer_Tick;

            _nativeIconGuardTimer = new DispatcherTimer(DispatcherPriority.ContextIdle)
            {
                // 桌面宿主和原生图标只需低频巡检。频繁调整 HWND Z 序会打断鼠标交互。
                Interval = TimeSpan.FromSeconds(15)
            };
            _nativeIconGuardTimer.Tick += NativeIconGuardTimer_Tick;

            _controlPanelAutoCollapseTimer = new DispatcherTimer(DispatcherPriority.ContextIdle)
            {
                Interval = TimeSpan.FromSeconds(6)
            };
            _controlPanelAutoCollapseTimer.Tick += ControlPanelAutoCollapseTimer_Tick;

            _healthMonitorTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _healthMonitorTimer.Tick += HealthMonitorTimer_Tick;

            _recycleBinStatusTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            _recycleBinStatusTimer.Tick += RecycleBinStatusTimer_Tick;

            SourceInitialized += MainWindow_SourceInitialized;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }
    }
}
