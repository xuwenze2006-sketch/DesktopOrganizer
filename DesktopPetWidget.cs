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
        internal static BitmapSource CatPreview => Frames.Value[0].Image;
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
        private static readonly int[] FallMilliseconds = [32, 24, 24, 24, 24, 24];
        private VPetAnimation.Frame? _fallFrame;

        internal string CharacterId => _characterId;
        internal string CurrentClip => UsesClips ? _clipName : ReferenceEquals(_sequence, Sleep) ? "sleepLoop"
            : ReferenceEquals(_sequence, React) ? "touchLoop" : "idle";
        private bool UsesClips => _characterId != CatCharacterId;
        private bool IsSunsetDuo => _characterId == DesktopPetCatalog.SunsetDuoId;
        internal int CurrentFrame => UsesClips ? _step : _sequence[_step].Frame;
        internal bool AnimationRunning => _timer.IsEnabled;
        internal bool SceneEnabled { get; private set; }
        internal VPetAnimation.Frame CurrentVPetFrame => _fallFrame ?? VPetAnimation.GetClip(_characterId, _clipName).Frames[_step];
        internal Matrix CharacterMotionTransform
        {
            get
            {
                Matrix motion = Matrix.Identity;
                if (_fallFrame is null) return motion;
                Rect bounds = CharacterBounds;
                double progress = (double)_step / (FallMilliseconds.Length - 1);
                double distance = progress * progress;
                bool imported = DesktopPetCatalog.IsImported(_characterId);
                if (!imported) motion.RotateAt(12 * distance, bounds.X + bounds.Width * .5, bounds.Y + bounds.Height * .6);
                motion.Translate(0, bounds.Height * (imported ? .125 : .14) * distance);
                return motion;
            }
        }
        internal static string NormalizeCharacterId(string? id) => DesktopPetCatalog.Find(id).Id;
        internal static double GetCharacterSize(string? id) => NormalizeCharacterId(id) == DesktopPetCatalog.SunsetDuoId
            ? DesktopPetScene.Size : NormalizeCharacterId(id) != CatCharacterId ? 168 : WidgetSize;
        internal static double GetWidgetSize(string? id, bool scene) => scene ? DesktopPetScene.Size : GetCharacterSize(id);
        internal static Vector GetCharacterOffset(string? id, bool scene) => !scene || NormalizeCharacterId(id) == DesktopPetCatalog.SunsetDuoId ? new Vector()
            : NormalizeCharacterId(id) != CatCharacterId ? new Vector(168, 137) : new Vector(196, 192);
        internal Rect CharacterBounds
        {
            get
            {
                Vector offset = GetCharacterOffset(_characterId, SceneEnabled);
                double size = GetCharacterSize(_characterId);
                return new Rect(offset.X * RenderSize.Width / Width, offset.Y * RenderSize.Height / Height,
                    size * RenderSize.Width / Width, size * RenderSize.Height / Height);
            }
        }

        internal void SetSceneEnabled(bool enabled)
        {
            if (SceneEnabled == enabled) return;
            SceneEnabled = enabled;
            Width = Height = GetWidgetSize(_characterId, enabled);
            InvalidateVisual();
        }

        internal void SetCharacter(string? id)
        {
            string normalized = NormalizeCharacterId(id);
            if (_characterId == normalized) return;
            _characterId = normalized;
            Width = Height = GetWidgetSize(normalized, SceneEnabled);
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
            _fallFrame = null;
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
            if (UsesClips && _active)
            {
                // 双人作为一个整体移动，不套用单人悬挂和摔落动作，保持牵手与脚底衔接。
                if (IsSunsetDuo) StartClip(dragging ? "raisedStart" : "raisedEnd");
                else if (dragging) StartClip("raisedStart");
                else
                {
                    // 从松手时真正显示的悬挂帧开始，避免直接瞬移成趴地姿势。
                    _fallFrame = CurrentVPetFrame;
                    _clipName = "fall";
                    _step = 0;
                    InvalidateVisual();
                    UpdateTimer();
                }
            }
            else ResetIdle();
        }

        internal void ReactToTouch()
        {
            if (!_active || _dragging) return;
            if (UsesClips)
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
            if (!_active || !IsVisible || (_dragging && !UsesClips)) return;
            if (UsesClips)
            {
                if (_fallFrame is not null)
                {
                    if (++_step >= FallMilliseconds.Length) StartClip("raisedEnd");
                    else
                    {
                        InvalidateVisual();
                        UpdateTimer();
                    }
                    return;
                }
                VPetAnimation.Clip clip = VPetAnimation.GetClip(_characterId, _clipName);
                if (_clipName == "idle") _idleMilliseconds += clip.Milliseconds[_step];
                if (++_step >= clip.Frames.Length)
                {
                    string next = _clipName switch
                    {
                        "idle" when _idleMilliseconds >= 30000 => "sleepStart",
                        "touchStart" => "touchLoop",
                        "touchLoop" => "touchEnd",
                        "touchEnd" or "raisedEnd" or "wave" => "idle",
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
            if (UsesClips) StartClip("sleepStart");
            else
            {
                _sequence = Sleep;
                _step = 0;
                InvalidateVisual();
                UpdateTimer();
            }
        }

        internal void Wave()
        {
            if (!_active || _dragging) return;
            if (!DesktopPetCatalog.IsImported(_characterId)) { ReactToTouch(); return; }
            if (_clipName.StartsWith("sleep", StringComparison.Ordinal))
            {
                _afterWake = "wave";
                StartClip("sleepEnd");
            }
            else StartClip("wave");
        }

        internal void WakeUp()
        {
            if (!_active || _dragging) return;
            _afterWake = "idle";
            if (UsesClips && _clipName.StartsWith("sleep", StringComparison.Ordinal)) StartClip("sleepEnd");
            else ResetIdle();
        }

        private void StartClip(string name)
        {
            _fallFrame = null;
            _clipName = name;
            _step = 0;
            if (name == "idle") _idleMilliseconds = 0;
            InvalidateVisual();
            UpdateTimer();
        }

        private void UpdateTimer()
        {
            _timer.Stop();
            if (_active && IsVisible && (!_dragging || UsesClips))
            {
                _timer.Interval = UsesClips
                    ? TimeSpan.FromMilliseconds(_fallFrame is not null ? FallMilliseconds[_step]
                        : VPetAnimation.GetClip(_characterId, _clipName).Milliseconds[_step])
                    : TimeSpan.FromSeconds(_sequence[_step].Seconds);
                _timer.Start();
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (!_active) return;
            if (SceneEnabled)
            {
                drawingContext.DrawImage(DesktopPetScene.BackgroundFor(_characterId).Image, new Rect(RenderSize));
                if (!IsSunsetDuo)
                {
                    double xScale = RenderSize.Width / DesktopPetScene.Size;
                    double yScale = RenderSize.Height / DesktopPetScene.Size;
                    bool resting = CurrentClip.StartsWith("sleep", StringComparison.Ordinal) || CurrentClip == "raisedEnd";
                    drawingContext.DrawEllipse(SceneShadow, null, new Point(253.5 * xScale, 301 * yScale),
                        (resting ? 72.5 : 33) * xScale, 5.5 * yScale);
                }
            }
            if (_fallFrame is not null) drawingContext.PushTransform(new MatrixTransform(CharacterMotionTransform));
            drawingContext.DrawImage(UsesClips ? CurrentVPetFrame.Image
                : Frames.Value[CurrentFrame].Image, CharacterBounds);
            if (_fallFrame is not null) drawingContext.Pop();
            if (SceneEnabled && !IsSunsetDuo) drawingContext.DrawImage(DesktopPetScene.Foreground.Image, new Rect(RenderSize));
        }

        private static readonly Brush SceneShadow = CreateSceneShadow();
        private static Brush CreateSceneShadow()
        {
            var brush = new SolidColorBrush(Color.FromArgb(32, 53, 72, 59));
            brush.Freeze();
            return brush;
        }

        internal bool ContainsOpaquePoint(Point point) =>
            ContainsCharacterPoint(point) || (_active && IsVisible && SceneEnabled &&
                DesktopPetScene.ContainsOpaquePoint(point, RenderSize, _characterId));

        internal bool ContainsCharacterPoint(Point point)
        {
            if (!_active || !IsVisible || ActualWidth <= 0 || ActualHeight <= 0 ||
                point.X < 0 || point.Y < 0 || point.X >= ActualWidth || point.Y >= ActualHeight)
                return false;

            Rect bounds = CharacterBounds;
            if (_fallFrame is not null)
            {
                Matrix inverse = CharacterMotionTransform;
                inverse.Invert();
                point = inverse.Transform(point);
            }
            if (point.X < bounds.Left || point.Y < bounds.Top || point.X >= bounds.Right || point.Y >= bounds.Bottom) return false;
            BitmapSource bitmap = UsesClips ? CurrentVPetFrame.Image : Frames.Value[CurrentFrame].Image;
            byte[] alpha = UsesClips ? CurrentVPetFrame.Alpha : Frames.Value[CurrentFrame].Alpha;
            int x = (int)((point.X - bounds.X) * bitmap.PixelWidth / bounds.Width);
            int y = (int)((point.Y - bounds.Y) * bitmap.PixelHeight / bounds.Height);
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
