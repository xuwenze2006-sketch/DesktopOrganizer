namespace DesktopOrganizer
{
    /// <summary>
    /// 共用一个计时器的桌面角色；素材与授权见 ThirdParty。
    /// </summary>
    public sealed class DesktopPetWidget : FrameworkElement
    {
        internal const double WidgetSize = 112;
        internal const string CatCharacterId = "pixelpaws";
        internal const string VPetCharacterId = "vpet";
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
        private string _characterId = CatCharacterId;
        private string _clipName = "idle";
        private double _idleMilliseconds;
        private string _afterWake = "idle";

        internal string CharacterId => _characterId;
        internal string CurrentClip => IsVPet ? _clipName : ReferenceEquals(_sequence, Sleep) ? "sleepLoop"
            : ReferenceEquals(_sequence, React) ? "touchLoop" : "idle";
        private bool IsVPet => _characterId == VPetCharacterId;
        internal int CurrentFrame => IsVPet ? _step : _sequence[_step].Frame;
        internal bool AnimationRunning => _timer.IsEnabled;
        internal static string NormalizeCharacterId(string? id) => id == VPetCharacterId ? VPetCharacterId : CatCharacterId;
        internal static double GetCharacterSize(string? id) => id == VPetCharacterId ? 168 : WidgetSize;

        internal void SetCharacter(string? id)
        {
            string normalized = NormalizeCharacterId(id);
            if (_characterId == normalized) return;
            _characterId = normalized;
            Width = Height = GetCharacterSize(normalized);
            _dragging = false;
            ResetIdle();
        }

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
            ResetIdle();
        }

        private void ResetIdle()
        {
            _idleMilliseconds = 0;
            _afterWake = "idle";
            _clipName = "idle";
            _sequence = Idle;
            _step = 0;
            InvalidateVisual();
            UpdateTimer();
        }

        internal void SetDragging(bool dragging)
        {
            if (_dragging == dragging) return;
            _dragging = dragging;
            if (IsVPet && _active) StartClip(dragging ? "raisedStart" : "raisedEnd");
            else ResetIdle();
        }

        internal void ReactToTouch()
        {
            if (!_active || _dragging) return;
            if (IsVPet)
            {
                if (_clipName.StartsWith("sleep", StringComparison.Ordinal))
                {
                    _afterWake = "touchStart";
                    StartClip("sleepEnd");
                }
                else StartClip("touchStart");
                return;
            }
            _sequence = React;
            _step = 0;
            InvalidateVisual();
            UpdateTimer();
        }

        internal void AdvanceAnimation()
        {
            if (!_active || !IsVisible || (_dragging && !IsVPet)) return;
            if (IsVPet)
            {
                VPetAnimation.Clip clip = VPetAnimation.GetClip(_clipName);
                if (_clipName == "idle") _idleMilliseconds += clip.Milliseconds[_step];
                if (++_step >= clip.Frames.Length)
                {
                    string next = _clipName switch
                    {
                        "idle" when _idleMilliseconds >= 30000 => "sleepStart",
                        "touchStart" => "touchLoop",
                        "touchLoop" => "touchEnd",
                        "touchEnd" or "raisedEnd" => "idle",
                        "raisedStart" => "raisedLoop",
                        "sleepStart" => "sleepLoop",
                        "sleepEnd" => _afterWake,
                        _ => _clipName
                    };
                    if (next == _clipName)
                    {
                        _step = 0;
                        InvalidateVisual();
                        UpdateTimer();
                    }
                    else StartClip(next);
                    return;
                }
                InvalidateVisual();
                UpdateTimer();
                return;
            }
            if (++_step >= _sequence.Length)
            {
                _sequence = ReferenceEquals(_sequence, React) ? Idle : Sleep;
                _step = 0;
            }
            InvalidateVisual();
            UpdateTimer();
        }

        internal void Rest()
        {
            if (!_active || _dragging) return;
            if (IsVPet) StartClip("sleepStart");
            else
            {
                _sequence = Sleep;
                _step = 0;
                InvalidateVisual();
                UpdateTimer();
            }
        }

        internal void WakeUp()
        {
            if (!_active || _dragging) return;
            _afterWake = "idle";
            if (IsVPet && _clipName.StartsWith("sleep", StringComparison.Ordinal)) StartClip("sleepEnd");
            else ResetIdle();
        }

        private void StartClip(string name)
        {
            _clipName = name;
            _step = 0;
            if (name == "idle") _idleMilliseconds = 0;
            InvalidateVisual();
            UpdateTimer();
        }

        private void UpdateTimer()
        {
            _timer.Stop();
            if (_active && IsVisible && (!_dragging || IsVPet))
            {
                _timer.Interval = IsVPet
                    ? TimeSpan.FromMilliseconds(VPetAnimation.GetClip(_clipName).Milliseconds[_step])
                    : TimeSpan.FromSeconds(_sequence[_step].Seconds);
                _timer.Start();
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (_active)
                drawingContext.DrawImage(IsVPet ? VPetAnimation.GetClip(_clipName).Frames[_step].Image
                    : Frames.Value[CurrentFrame].Image, new Rect(RenderSize));
        }

        internal bool ContainsOpaquePoint(Point point)
        {
            if (!_active || !IsVisible || ActualWidth <= 0 || ActualHeight <= 0 ||
                point.X < 0 || point.Y < 0 || point.X >= ActualWidth || point.Y >= ActualHeight)
                return false;

            BitmapSource bitmap = IsVPet ? VPetAnimation.GetClip(_clipName).Frames[_step].Image : Frames.Value[CurrentFrame].Image;
            byte[] alpha = IsVPet ? VPetAnimation.GetClip(_clipName).Frames[_step].Alpha : Frames.Value[CurrentFrame].Alpha;
            int x = (int)(point.X * bitmap.PixelWidth / ActualWidth);
            int y = (int)(point.Y * bitmap.PixelHeight / ActualHeight);
            return alpha[y * bitmap.PixelWidth + x] > 32;
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
