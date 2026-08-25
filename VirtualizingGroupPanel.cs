namespace DesktopOrganizer
{
    /// <summary>
    /// 针对分类框固定尺寸图标的轻量虚拟化面板。
    /// 只创建当前可视行及少量缓冲行中的图标控件，并通过 IScrollInfo
    /// 让 ScrollViewer 使用面板自身的像素滚动范围。
    /// </summary>
    internal sealed class VirtualizingGroupPanel : Panel, IScrollInfo
    {
        private const int CacheRowsBeforeViewport = 1;
        private const int CacheRowsAfterViewport = 2;

        private readonly IReadOnlyList<GroupVirtualItem> _items;
        private readonly Func<GroupVirtualItem, FrameworkElement> _createContainer;
        private readonly Action<FrameworkElement> _recycleContainer;
        private readonly Action<double>? _verticalOffsetChanged;
        private readonly Dictionary<int, FrameworkElement> _realized = new();
        private readonly HashSet<int> _suppressedIndices = new();
        private readonly int _columnCount;
        private readonly double _slotWidth;
        private readonly double _rowHeight;
        private double _extentWidth;
        private double _extentHeight;
        private double _viewportWidth;
        private double _viewportHeight;
        private double _verticalOffset;
        private bool _isStable = true;

        public VirtualizingGroupPanel(
            IReadOnlyList<GroupVirtualItem> items,
            Func<GroupVirtualItem, FrameworkElement> createContainer,
            Action<FrameworkElement> recycleContainer,
            int columnCount,
            double slotWidth,
            double rowHeight,
            double initialVerticalOffset,
            Action<double>? verticalOffsetChanged = null)
        {
            _items = items ?? throw new ArgumentNullException(nameof(items));
            _createContainer = createContainer ?? throw new ArgumentNullException(nameof(createContainer));
            _recycleContainer = recycleContainer ?? throw new ArgumentNullException(nameof(recycleContainer));
            _columnCount = Math.Max(1, columnCount);
            _slotWidth = Math.Max(1, slotWidth);
            _rowHeight = Math.Max(1, rowHeight);
            _verticalOffset = Math.Max(0, initialVerticalOffset);
            _verticalOffsetChanged = verticalOffsetChanged;

            ClipToBounds = true;
            Focusable = false;
            CanHorizontallyScroll = false;
            CanVerticallyScroll = true;
        }

        public bool IsStable => _isStable && _suppressedIndices.Count == 0;

        public bool MatchesItems(IReadOnlyList<GroupVirtualItem> expected)
        {
            if (expected.Count != _items.Count)
            {
                return false;
            }

            for (int index = 0; index < expected.Count; index++)
            {
                GroupVirtualItem actual = _items[index];
                GroupVirtualItem target = expected[index];
                if (!actual.DisplayName.Equals(target.DisplayName, StringComparison.Ordinal) ||
                    !ShellItemLocation.AreEquivalent(actual.FullPath, target.FullPath))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 将已实现的图标从虚拟化面板中取出用于拖动，并阻止同一索引在拖动结束前被重新创建。
        /// </summary>
        public bool DetachForDrag(FrameworkElement element)
        {
            if (!TryGetRealizedIndex(element, out int index))
            {
                return false;
            }

            _realized.Remove(index);
            _suppressedIndices.Add(index);
            _isStable = false;
            Children.Remove(element);
            InvalidateMeasure();
            return true;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            int rowCount = _items.Count == 0
                ? 0
                : (int)Math.Ceiling(_items.Count / (double)_columnCount);
            double naturalViewportHeight = Math.Max(
                _rowHeight,
                Math.Min(Math.Max(1, rowCount), 3) * _rowHeight);
            double availableWidth = NormalizeViewportLength(availableSize.Width, ActualWidth, Width);
            double availableHeight = double.IsFinite(availableSize.Height)
                ? Math.Max(0, availableSize.Height)
                : NormalizeViewportLength(
                    ScrollOwner?.ViewportHeight ?? double.NaN,
                    ActualHeight,
                    naturalViewportHeight);
            double targetExtentWidth = Math.Max(availableWidth, _columnCount * _slotWidth);
            double targetExtentHeight = rowCount * _rowHeight;

            UpdateScrollInfo(
                targetExtentWidth,
                targetExtentHeight,
                availableWidth,
                availableHeight);
            RealizeVisibleRange();

            foreach (FrameworkElement child in _realized.Values)
            {
                child.Measure(new Size(_slotWidth, _rowHeight));
            }

            return new Size(availableWidth, availableHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach ((int index, FrameworkElement child) in _realized)
            {
                int row = index / _columnCount;
                int column = index % _columnCount;
                double x = column * _slotWidth;
                double y = row * _rowHeight - _verticalOffset;
                child.Arrange(new Rect(x, y, _slotWidth, _rowHeight));
            }

            return finalSize;
        }

        private void RealizeVisibleRange()
        {
            if (_items.Count == 0)
            {
                RecycleOutsideRange(0, -1);
                return;
            }

            double effectiveViewportHeight = _viewportHeight > 0
                ? _viewportHeight
                : _rowHeight;
            int firstVisibleRow = Math.Max(
                0,
                (int)Math.Floor(_verticalOffset / _rowHeight));
            int lastVisibleRow = Math.Max(
                firstVisibleRow,
                (int)Math.Floor(
                    Math.Max(0, _verticalOffset + effectiveViewportHeight - 0.01) /
                    _rowHeight));
            int firstRealizedRow = Math.Max(0, firstVisibleRow - CacheRowsBeforeViewport);
            int totalRows = (int)Math.Ceiling(_items.Count / (double)_columnCount);
            int lastRealizedRow = Math.Min(
                Math.Max(0, totalRows - 1),
                lastVisibleRow + CacheRowsAfterViewport);
            int firstIndex = firstRealizedRow * _columnCount;
            int lastIndex = Math.Min(
                _items.Count - 1,
                ((lastRealizedRow + 1) * _columnCount) - 1);

            RecycleOutsideRange(firstIndex, lastIndex);
            for (int index = firstIndex; index <= lastIndex; index++)
            {
                if (_suppressedIndices.Contains(index) || _realized.ContainsKey(index))
                {
                    continue;
                }

                FrameworkElement child = _createContainer(_items[index]);
                _realized[index] = child;
                Children.Add(child);
            }
        }

        private void RecycleOutsideRange(int firstIndex, int lastIndex)
        {
            foreach (int index in _realized.Keys
                         .Where(index => index < firstIndex || index > lastIndex)
                         .ToList())
            {
                FrameworkElement child = _realized[index];
                _realized.Remove(index);
                _recycleContainer(child);
                Children.Remove(child);
            }
        }

        private void UpdateScrollInfo(
            double extentWidth,
            double extentHeight,
            double viewportWidth,
            double viewportHeight)
        {
            extentWidth = Math.Max(0, extentWidth);
            extentHeight = Math.Max(0, extentHeight);
            viewportWidth = Math.Max(0, viewportWidth);
            viewportHeight = Math.Max(0, viewportHeight);

            bool changed = !AreClose(_extentWidth, extentWidth) ||
                           !AreClose(_extentHeight, extentHeight) ||
                           !AreClose(_viewportWidth, viewportWidth) ||
                           !AreClose(_viewportHeight, viewportHeight);
            _extentWidth = extentWidth;
            _extentHeight = extentHeight;
            _viewportWidth = viewportWidth;
            _viewportHeight = viewportHeight;

            double clampedOffset = CoerceVerticalOffset(_verticalOffset);
            if (!AreClose(clampedOffset, _verticalOffset))
            {
                _verticalOffset = clampedOffset;
                _verticalOffsetChanged?.Invoke(_verticalOffset);
                changed = true;
            }

            if (changed)
            {
                ScrollOwner?.InvalidateScrollInfo();
            }
        }

        private static double NormalizeViewportLength(
            double available,
            double preferred,
            double fallback)
        {
            if (double.IsFinite(available))
            {
                return Math.Max(0, available);
            }

            if (double.IsFinite(preferred) && preferred > 0)
            {
                return preferred;
            }

            return double.IsFinite(fallback) && fallback > 0 ? fallback : 0;
        }

        private double CoerceVerticalOffset(double offset)
        {
            if (double.IsNaN(offset) || offset < 0)
            {
                return 0;
            }

            return Math.Min(offset, Math.Max(0, _extentHeight - _viewportHeight));
        }

        private static bool AreClose(double left, double right) =>
            Math.Abs(left - right) < 0.1;

        public bool CanHorizontallyScroll { get; set; }

        public bool CanVerticallyScroll { get; set; }

        public double ExtentWidth => _extentWidth;

        public double ExtentHeight => _extentHeight;

        public double ViewportWidth => _viewportWidth;

        public double ViewportHeight => _viewportHeight;

        public double HorizontalOffset => 0;

        public double VerticalOffset => _verticalOffset;

        public ScrollViewer? ScrollOwner { get; set; }

        public void LineUp() => SetVerticalOffset(_verticalOffset - _rowHeight);

        public void LineDown() => SetVerticalOffset(_verticalOffset + _rowHeight);

        public void LineLeft()
        {
        }

        public void LineRight()
        {
        }

        public void MouseWheelUp()
        {
            int lines = Math.Max(1, SystemParameters.WheelScrollLines);
            SetVerticalOffset(_verticalOffset - lines * _rowHeight);
        }

        public void MouseWheelDown()
        {
            int lines = Math.Max(1, SystemParameters.WheelScrollLines);
            SetVerticalOffset(_verticalOffset + lines * _rowHeight);
        }

        public void MouseWheelLeft()
        {
        }

        public void MouseWheelRight()
        {
        }

        public void PageUp() => SetVerticalOffset(_verticalOffset - _viewportHeight);

        public void PageDown() => SetVerticalOffset(_verticalOffset + _viewportHeight);

        public void PageLeft()
        {
        }

        public void PageRight()
        {
        }

        public void SetHorizontalOffset(double offset)
        {
        }

        public void SetVerticalOffset(double offset)
        {
            double clamped = CoerceVerticalOffset(offset);
            if (AreClose(clamped, _verticalOffset))
            {
                return;
            }

            _verticalOffset = clamped;
            _verticalOffsetChanged?.Invoke(_verticalOffset);
            ScrollOwner?.InvalidateScrollInfo();
            InvalidateMeasure();
            InvalidateArrange();
        }

        private bool TryGetRealizedIndex(FrameworkElement element, out int index)
        {
            foreach ((int candidateIndex, FrameworkElement candidate) in _realized)
            {
                if (ReferenceEquals(candidate, element))
                {
                    index = candidateIndex;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            DependencyObject? current = visual;
            while (current != null && !ReferenceEquals(current, this))
            {
                if (current is FrameworkElement element &&
                    TryGetRealizedIndex(element, out int realizedIndex))
                {
                    return MakeRealizedIndexVisible(realizedIndex);
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return Rect.Empty;
        }

        private Rect MakeRealizedIndexVisible(int index)
        {
            int row = index / _columnCount;
            double itemTop = row * _rowHeight;
            double itemBottom = itemTop + _rowHeight;
            if (itemTop < _verticalOffset)
            {
                SetVerticalOffset(itemTop);
            }
            else if (itemBottom > _verticalOffset + _viewportHeight)
            {
                SetVerticalOffset(itemBottom - _viewportHeight);
            }

            return new Rect(
                0,
                Math.Max(0, itemTop - _verticalOffset),
                _slotWidth,
                _rowHeight);
        }
    }

    internal sealed record GroupVirtualItem(string DisplayName, string FullPath);
}
