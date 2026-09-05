namespace DesktopOrganizer
{
    /// <summary>
    /// 小尺寸、低频帧动画。图集裁帧/冻结方式参考 PixelPaws 的 SpriteAnimator；
    /// 素材来源与 MIT 许可见 ThirdParty/PixelPaws-LICENSE.txt。
    /// </summary>
    public sealed class DesktopPetWidget : FrameworkElement
    {
        internal const double WidgetSize = 112;
        private const int CellSize = 200;
        private sealed record Frame(BitmapSource Image, byte[] Alpha);
        private readonly record struct Step(int Frame, double Seconds);
        private static readonly Lazy<Frame[]> Frames = new(LoadFrames);
        private static readonly Step[] Idle = [new(0, 12), new(1, .18), new(0, 10), new(1, .18), new(0, 8)];
        private static readonly Step[] Sleep = [new(2, 3), new(3, 3)];
        private static readonly Step[] React = [new(4, .2), new(5, .2), new(4, .2), new(5, .2)];
        private readonly DispatcherTimer _timer;
        private Step[] _sequence = Idle;
        private int _step;
        private bool _active;
        private bool _dragging;

        internal int CurrentFrame => _sequence[_step].Frame;
        internal bool AnimationRunning => _timer.IsEnabled;

        public DesktopPetWidget()
        {
            Width = Height = WidgetSize;
            Focusable = false;
            _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            _timer.Tick += (_, _) => AdvanceAnimation();
            IsVisibleChanged += (_, _) => UpdateTimer();
        }

        internal void SetActive(bool active)
        {
            if (_active == active) return;
            _active = active;
            _dragging = false;
            _sequence = Idle;
            _step = 0;
            InvalidateVisual();
            UpdateTimer();
        }

        internal void SetDragging(bool dragging)
        {
            _dragging = dragging;
            _sequence = Idle;
            _step = 0;
            InvalidateVisual();
            UpdateTimer();
        }

        internal void ReactToTouch()
        {
            if (!_active || _dragging) return;
            _sequence = React;
            _step = 0;
            InvalidateVisual();
            UpdateTimer();
        }

        internal void AdvanceAnimation()
        {
            if (!_active || !IsVisible || _dragging) return;
            if (++_step >= _sequence.Length)
            {
                _sequence = ReferenceEquals(_sequence, React) ? Idle : Sleep;
                _step = 0;
            }
            InvalidateVisual();
            UpdateTimer();
        }

        private void UpdateTimer()
        {
            _timer.Stop();
            if (_active && IsVisible && !_dragging)
            {
                _timer.Interval = TimeSpan.FromSeconds(_sequence[_step].Seconds);
                _timer.Start();
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (_active)
                drawingContext.DrawImage(Frames.Value[CurrentFrame].Image, new Rect(RenderSize));
        }

        internal bool ContainsOpaquePoint(Point point)
        {
            if (!_active || !IsVisible || ActualWidth <= 0 || ActualHeight <= 0 ||
                point.X < 0 || point.Y < 0 || point.X >= ActualWidth || point.Y >= ActualHeight)
                return false;

            int x = (int)(point.X * CellSize / ActualWidth);
            int y = (int)(point.Y * CellSize / ActualHeight);
            return Frames.Value[CurrentFrame].Alpha[y * CellSize + x] > 32;
        }

        protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
            ContainsOpaquePoint(hitTestParameters.HitPoint)
                ? new PointHitTestResult(this, hitTestParameters.HitPoint)
                : null;

        private static Frame[] LoadFrames()
        {
            var sheet = new BitmapImage(new Uri(
                "pack://application:,,,/DesktopOrganizer;component/Assets/Pets/PixelPawsCat.png"));
            sheet.Freeze();
            var frames = new Frame[6];
            for (int index = 0; index < frames.Length; index++)
            {
                var crop = new CroppedBitmap(sheet,
                    new Int32Rect(index % 3 * CellSize, index / 3 * CellSize, CellSize, CellSize));
                crop.Freeze();
                var pixels = new byte[CellSize * CellSize * 4];
                var bitmap = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
                bitmap.CopyPixels(pixels, CellSize * 4, 0);
                var alpha = new byte[CellSize * CellSize];
                for (int pixel = 0; pixel < alpha.Length; pixel++)
                    alpha[pixel] = pixels[pixel * 4 + 3];
                frames[index] = new Frame(crop, alpha);
            }
            return frames;
        }
    }
}
