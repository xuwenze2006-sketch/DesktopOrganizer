// 虚拟桌面、多显示器工作区与布局坐标迁移
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private void RefreshDesktopGeometry(IntPtr windowHandle)
        {
            IReadOnlyList<NativeMethods.DesktopMonitorNativeInfo> nativeMonitors =
                NativeMethods.GetDesktopMonitors();
            if (nativeMonitors.Count == 0 || windowHandle == IntPtr.Zero)
            {
                _desktopGeometry = new DesktopGeometry([]);
                return;
            }

            int virtualLeft = nativeMonitors.Min(monitor => monitor.MonitorLeft);
            int virtualTop = nativeMonitors.Min(monitor => monitor.MonitorTop);
            int virtualRight = nativeMonitors.Max(monitor => monitor.MonitorRight);
            int virtualBottom = nativeMonitors.Max(monitor => monitor.MonitorBottom);
            _ = NativeMethods.SetDesktopWindowBounds(
                windowHandle,
                virtualLeft,
                virtualTop,
                Math.Max(1, virtualRight - virtualLeft),
                Math.Max(1, virtualBottom - virtualTop));

            var rawRegions = new List<DesktopMonitorRegion>(nativeMonitors.Count);
            foreach (NativeMethods.DesktopMonitorNativeInfo monitor in nativeMonitors)
            {
                Rect bounds = ScreenPixelRectToClientDipRect(
                    monitor.MonitorLeft,
                    monitor.MonitorTop,
                    monitor.MonitorRight,
                    monitor.MonitorBottom);
                Rect workArea = ScreenPixelRectToClientDipRect(
                    monitor.WorkLeft,
                    monitor.WorkTop,
                    monitor.WorkRight,
                    monitor.WorkBottom);

                if (!IsUsableRect(bounds) || !IsUsableRect(workArea))
                {
                    continue;
                }

                rawRegions.Add(new DesktopMonitorRegion
                {
                    DeviceName = monitor.DeviceName,
                    Bounds = bounds,
                    WorkArea = workArea,
                    IsPrimary = monitor.IsPrimary,
                    DpiX = monitor.DpiX,
                    DpiY = monitor.DpiY
                });
            }

            if (rawRegions.Count == 0)
            {
                _desktopGeometry = new DesktopGeometry([]);
                return;
            }

            // PointFromScreen 理论上已返回客户区坐标；仍统一归一化到 (0,0)，
            // 消除负虚拟屏幕原点和 DPI 舍入造成的亚像素偏差。
            double originX = rawRegions.Min(region => region.Bounds.Left);
            double originY = rawRegions.Min(region => region.Bounds.Top);
            List<DesktopMonitorRegion> normalized = rawRegions
                .Select(region => new DesktopMonitorRegion
                {
                    DeviceName = region.DeviceName,
                    Bounds = OffsetRect(region.Bounds, -originX, -originY),
                    WorkArea = OffsetRect(region.WorkArea, -originX, -originY),
                    IsPrimary = region.IsPrimary,
                    DpiX = region.DpiX,
                    DpiY = region.DpiY
                })
                .ToList();

            _desktopGeometry = new DesktopGeometry(normalized);
            _diagnostics.Log(
                $"DISPLAY monitors={normalized.Count}, canvas={_desktopGeometry.CanvasBounds.Width:0.#}x{_desktopGeometry.CanvasBounds.Height:0.#}, mixedDpi={_desktopGeometry.HasMixedDpi}");
        }

        private Rect ScreenPixelRectToClientDipRect(int left, int top, int right, int bottom)
        {
            Point topLeft = PointFromScreen(new Point(left, top));
            Point bottomRight = PointFromScreen(new Point(right, bottom));
            return new Rect(
                Math.Min(topLeft.X, bottomRight.X),
                Math.Min(topLeft.Y, bottomRight.Y),
                Math.Abs(bottomRight.X - topLeft.X),
                Math.Abs(bottomRight.Y - topLeft.Y));
        }

        private static Rect OffsetRect(Rect rect, double offsetX, double offsetY) =>
            new(rect.X + offsetX, rect.Y + offsetY, rect.Width, rect.Height);

        private static bool IsUsableRect(Rect rect) =>
            !rect.IsEmpty &&
            double.IsFinite(rect.X) &&
            double.IsFinite(rect.Y) &&
            double.IsFinite(rect.Width) &&
            double.IsFinite(rect.Height) &&
            rect.Width > 0 &&
            rect.Height > 0;

        private Rect GetPrimaryWorkArea() => _desktopGeometry.PrimaryMonitor.WorkArea;

        private DesktopMonitorRegion GetMonitorForItemRect(Rect rect) =>
            _desktopGeometry.FindMonitorForRect(rect);

        private bool IsRectInsideUsableDesktop(Rect rect)
        {
            const double tolerance = 0.75;
            return _desktopGeometry.Monitors.Any(monitor =>
                rect.Left >= monitor.WorkArea.Left - tolerance &&
                rect.Top >= monitor.WorkArea.Top - tolerance &&
                rect.Right <= monitor.WorkArea.Right + tolerance &&
                rect.Bottom <= monitor.WorkArea.Bottom + tolerance);
        }

        private Point ClampRectToUsableDesktop(double x, double y, double width, double height)
        {
            width = Math.Max(0, SafeCanvasCoordinate(width));
            height = Math.Max(0, SafeCanvasCoordinate(height));
            var requested = new Rect(
                SafeCanvasCoordinate(x),
                SafeCanvasCoordinate(y),
                width,
                height);
            DesktopMonitorRegion monitor = GetMonitorForItemRect(requested);
            Rect workArea = monitor.WorkArea;

            double clampedWidth = Math.Min(width, workArea.Width);
            double clampedHeight = Math.Min(height, workArea.Height);
            double clampedX = Math.Clamp(
                requested.X,
                workArea.Left,
                Math.Max(workArea.Left, workArea.Right - clampedWidth));
            double clampedY = Math.Clamp(
                requested.Y,
                workArea.Top,
                Math.Max(workArea.Top, workArea.Bottom - clampedHeight));
            return new Point(clampedX, clampedY);
        }

        private bool ReconcileLoadedLayoutCoordinateSpace(int serializedVersion)
        {
            bool changed;
            if (serializedVersion < 13)
            {
                changed = MigrateLegacyPrimaryWorkAreaCoordinates();
            }
            else
            {
                DesktopGeometry? savedGeometry = CreateGeometryFromPersistedTopology(
                    _appLayout.DesktopTopology);
                changed = savedGeometry != null &&
                          !savedGeometry.IsEquivalentTo(_desktopGeometry) &&
                          RemapLayoutBetweenGeometries(savedGeometry, _desktopGeometry);
            }

            _appLayout.Version = 16;
            CaptureCurrentDesktopTopology();
            return changed || serializedVersion < 16;
        }

        private bool MigrateLegacyPrimaryWorkAreaCoordinates()
        {
            Rect primaryWorkArea = GetPrimaryWorkArea();
            double offsetX = primaryWorkArea.Left;
            double offsetY = primaryWorkArea.Top;
            if (Math.Abs(offsetX) < 0.01 && Math.Abs(offsetY) < 0.01)
            {
                return false;
            }

            foreach (IconPosition position in _appLayout.FreeIcons.Values)
            {
                position.X = SafeCanvasCoordinate(position.X) + offsetX;
                position.Y = SafeCanvasCoordinate(position.Y) + offsetY;
            }

            foreach (IconPosition position in _appLayout.AutoClassificationOriginalPositions.Values)
            {
                position.X = SafeCanvasCoordinate(position.X) + offsetX;
                position.Y = SafeCanvasCoordinate(position.Y) + offsetY;
            }

            foreach (GroupInfo group in _appLayout.Groups)
            {
                group.X = SafeCanvasCoordinate(group.X) + offsetX;
                group.Y = SafeCanvasCoordinate(group.Y) + offsetY;
            }

            if (_appLayout.ControlPanelX.HasValue)
            {
                _appLayout.ControlPanelX = SafeCanvasCoordinate(_appLayout.ControlPanelX.Value) + offsetX;
            }
            if (_appLayout.ControlPanelY.HasValue)
            {
                _appLayout.ControlPanelY = SafeCanvasCoordinate(_appLayout.ControlPanelY.Value) + offsetY;
            }

            return true;
        }

        private bool RemapLayoutForGeometryChange(DesktopGeometry previousGeometry)
        {
            if (previousGeometry.IsEquivalentTo(_desktopGeometry))
            {
                return false;
            }

            bool changed = RemapLayoutBetweenGeometries(previousGeometry, _desktopGeometry);
            CaptureCurrentDesktopTopology();
            _lastSmartLayoutSnapshot = null;
            UndoSmartLayoutButton.IsEnabled = false;
            SaveLayout();
            return changed;
        }

        private bool RemapLayoutBetweenGeometries(
            DesktopGeometry sourceGeometry,
            DesktopGeometry targetGeometry)
        {
            bool changed = false;
            foreach (IconPosition position in _appLayout.FreeIcons.Values)
            {
                Point mapped = MapItemPosition(
                    position.X,
                    position.Y,
                    IconCellWidth,
                    IconCellHeight,
                    sourceGeometry,
                    targetGeometry);
                changed |= SetPositionIfChanged(position, mapped);
            }

            foreach (IconPosition position in _appLayout.AutoClassificationOriginalPositions.Values)
            {
                Point mapped = MapItemPosition(
                    position.X,
                    position.Y,
                    IconCellWidth,
                    IconCellHeight,
                    sourceGeometry,
                    targetGeometry);
                changed |= SetPositionIfChanged(position, mapped);
            }

            foreach (GroupInfo group in _appLayout.Groups)
            {
                Point mapped = MapItemPosition(
                    group.X,
                    group.Y,
                    group.Width,
                    GetGroupDisplayHeight(group),
                    sourceGeometry,
                    targetGeometry);
                if (Math.Abs(group.X - mapped.X) > 0.01 ||
                    Math.Abs(group.Y - mapped.Y) > 0.01)
                {
                    group.X = mapped.X;
                    group.Y = mapped.Y;
                    changed = true;
                }
                ClampGroupToCanvas(group);
            }


            if (_appLayout.RecycleBinWidget.X.HasValue &&
                _appLayout.RecycleBinWidget.Y.HasValue)
            {
                Point mapped = MapItemPosition(
                    _appLayout.RecycleBinWidget.X.Value,
                    _appLayout.RecycleBinWidget.Y.Value,
                    RecycleBinWidgetWidth,
                    RecycleBinWidgetHeight,
                    sourceGeometry,
                    targetGeometry);
                if (Math.Abs(_appLayout.RecycleBinWidget.X.Value - mapped.X) > 0.01 ||
                    Math.Abs(_appLayout.RecycleBinWidget.Y.Value - mapped.Y) > 0.01)
                {
                    _appLayout.RecycleBinWidget.X = mapped.X;
                    _appLayout.RecycleBinWidget.Y = mapped.Y;
                    changed = true;
                }
            }

            if (_appLayout.ControlPanelX.HasValue && _appLayout.ControlPanelY.HasValue)
            {
                Point mapped = MapItemPosition(
                    _appLayout.ControlPanelX.Value,
                    _appLayout.ControlPanelY.Value,
                    0,
                    0,
                    sourceGeometry,
                    targetGeometry);
                if (Math.Abs(_appLayout.ControlPanelX.Value - mapped.X) > 0.01 ||
                    Math.Abs(_appLayout.ControlPanelY.Value - mapped.Y) > 0.01)
                {
                    _appLayout.ControlPanelX = mapped.X;
                    _appLayout.ControlPanelY = mapped.Y;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool SetPositionIfChanged(IconPosition position, Point mapped)
        {
            if (Math.Abs(position.X - mapped.X) <= 0.01 &&
                Math.Abs(position.Y - mapped.Y) <= 0.01)
            {
                return false;
            }

            position.X = mapped.X;
            position.Y = mapped.Y;
            return true;
        }

        private static Point MapItemPosition(
            double x,
            double y,
            double itemWidth,
            double itemHeight,
            DesktopGeometry sourceGeometry,
            DesktopGeometry targetGeometry)
        {
            var sourceRect = new Rect(
                double.IsFinite(x) ? x : 0,
                double.IsFinite(y) ? y : 0,
                Math.Max(0, double.IsFinite(itemWidth) ? itemWidth : 0),
                Math.Max(0, double.IsFinite(itemHeight) ? itemHeight : 0));
            DesktopMonitorRegion sourceMonitor = sourceGeometry.FindMonitorForRect(sourceRect);
            DesktopMonitorRegion targetMonitor = targetGeometry.Monitors.FirstOrDefault(candidate =>
                candidate.DeviceName.Equals(
                    sourceMonitor.DeviceName,
                    StringComparison.OrdinalIgnoreCase)) ?? targetGeometry.PrimaryMonitor;

            double mappedX = MapAxis(
                sourceRect.X,
                sourceRect.Width,
                sourceMonitor.WorkArea.Left,
                sourceMonitor.WorkArea.Width,
                targetMonitor.WorkArea.Left,
                targetMonitor.WorkArea.Width);
            double mappedY = MapAxis(
                sourceRect.Y,
                sourceRect.Height,
                sourceMonitor.WorkArea.Top,
                sourceMonitor.WorkArea.Height,
                targetMonitor.WorkArea.Top,
                targetMonitor.WorkArea.Height);
            return new Point(mappedX, mappedY);
        }

        private static double MapAxis(
            double value,
            double itemLength,
            double sourceStart,
            double sourceLength,
            double targetStart,
            double targetLength)
        {
            double sourceRange = Math.Max(1, sourceLength - itemLength);
            double targetRange = Math.Max(0, targetLength - Math.Min(itemLength, targetLength));
            double ratio = Math.Clamp((value - sourceStart) / sourceRange, 0, 1);
            return targetStart + ratio * targetRange;
        }

        private DesktopGeometry? CreateGeometryFromPersistedTopology(
            IReadOnlyList<DesktopMonitorLayoutInfo>? topology)
        {
            if (topology == null || topology.Count == 0)
            {
                return null;
            }

            List<DesktopMonitorRegion> monitors = topology
                .Where(info =>
                    !string.IsNullOrWhiteSpace(info.DeviceName) &&
                    info.BoundsWidth > 0 &&
                    info.BoundsHeight > 0 &&
                    info.WorkWidth > 0 &&
                    info.WorkHeight > 0)
                .Select(info => new DesktopMonitorRegion
                {
                    DeviceName = info.DeviceName,
                    Bounds = new Rect(
                        info.BoundsX,
                        info.BoundsY,
                        info.BoundsWidth,
                        info.BoundsHeight),
                    WorkArea = new Rect(
                        info.WorkX,
                        info.WorkY,
                        info.WorkWidth,
                        info.WorkHeight),
                    IsPrimary = info.IsPrimary,
                    DpiX = info.DpiX == 0 ? 96u : info.DpiX,
                    DpiY = info.DpiY == 0 ? 96u : info.DpiY
                })
                .ToList();

            return monitors.Count == 0 ? null : new DesktopGeometry(monitors);
        }

        private void CaptureCurrentDesktopTopology()
        {
            _appLayout.DesktopTopology = _desktopGeometry.Monitors
                .Select(monitor => new DesktopMonitorLayoutInfo
                {
                    DeviceName = monitor.DeviceName,
                    BoundsX = monitor.Bounds.X,
                    BoundsY = monitor.Bounds.Y,
                    BoundsWidth = monitor.Bounds.Width,
                    BoundsHeight = monitor.Bounds.Height,
                    WorkX = monitor.WorkArea.X,
                    WorkY = monitor.WorkArea.Y,
                    WorkWidth = monitor.WorkArea.Width,
                    WorkHeight = monitor.WorkArea.Height,
                    IsPrimary = monitor.IsPrimary,
                    DpiX = monitor.DpiX,
                    DpiY = monitor.DpiY
                })
                .ToList();
        }

        private void PrepareLayoutForPersistence()
        {
            NormalizeLayout();
            CaptureCurrentDesktopTopology();
            WorkspaceLayoutManager.UpdateActiveSnapshot(_appLayout, DateTime.UtcNow);
        }
    }
}
