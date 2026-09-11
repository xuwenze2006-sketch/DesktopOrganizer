namespace DesktopOrganizer
{
    internal sealed class DesktopMonitorRegion
    {
        public string DeviceName { get; init; } = string.Empty;
        public Rect Bounds { get; init; }
        public Rect WorkArea { get; init; }
        public bool IsPrimary { get; init; }
        public uint DpiX { get; init; } = 96;
        public uint DpiY { get; init; } = 96;
    }

    internal sealed class DesktopGeometry
    {
        private const double GeometryTolerance = 0.75;

        public DesktopGeometry(IReadOnlyList<DesktopMonitorRegion> monitors)
        {
            Monitors = monitors.Count > 0
                ? monitors
                : [CreateFallbackMonitor()];

            double left = Monitors.Min(monitor => monitor.Bounds.Left);
            double top = Monitors.Min(monitor => monitor.Bounds.Top);
            double right = Monitors.Max(monitor => monitor.Bounds.Right);
            double bottom = Monitors.Max(monitor => monitor.Bounds.Bottom);
            CanvasBounds = new Rect(
                0,
                0,
                Math.Max(1, right - left),
                Math.Max(1, bottom - top));
        }

        public IReadOnlyList<DesktopMonitorRegion> Monitors { get; }
        public Rect CanvasBounds { get; }

        public DesktopMonitorRegion PrimaryMonitor =>
            Monitors.FirstOrDefault(monitor => monitor.IsPrimary) ?? Monitors[0];

        public bool HasMixedDpi => Monitors
            .Select(monitor => (monitor.DpiX, monitor.DpiY))
            .Distinct()
            .Skip(1)
            .Any();

        public DesktopMonitorRegion FindMonitorForPoint(Point point)
        {
            DesktopMonitorRegion? containing = Monitors.FirstOrDefault(
                monitor => monitor.Bounds.Contains(point));
            if (containing != null)
            {
                return containing;
            }

            return Monitors
                .OrderBy(monitor => DistanceSquared(point, monitor.Bounds))
                .First();
        }

        public DesktopMonitorRegion FindMonitorForRect(Rect rect)
        {
            DesktopMonitorRegion? overlapping = Monitors
                .Select(monitor => new
                {
                    Monitor = monitor,
                    Area = IntersectionArea(rect, monitor.WorkArea)
                })
                .Where(result => result.Area > 0)
                .OrderByDescending(result => result.Area)
                .Select(result => result.Monitor)
                .FirstOrDefault();

            return overlapping ?? Monitors
                .OrderBy(monitor => DistanceSquared(rect, monitor.Bounds))
                .First();
        }

        public bool IsEquivalentTo(DesktopGeometry other)
        {
            if (Monitors.Count != other.Monitors.Count)
            {
                return false;
            }

            foreach (DesktopMonitorRegion monitor in Monitors)
            {
                DesktopMonitorRegion? matching = other.Monitors.FirstOrDefault(candidate =>
                    candidate.DeviceName.Equals(monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
                if (matching == null ||
                    !RectApproximatelyEquals(monitor.Bounds, matching.Bounds) ||
                    !RectApproximatelyEquals(monitor.WorkArea, matching.WorkArea) ||
                    monitor.DpiX != matching.DpiX ||
                    monitor.DpiY != matching.DpiY)
                {
                    return false;
                }
            }

            return true;
        }

        private static DesktopMonitorRegion CreateFallbackMonitor()
        {
            Rect workArea = SystemParameters.WorkArea;
            return new DesktopMonitorRegion
            {
                DeviceName = "PRIMARY",
                Bounds = new Rect(0, 0, Math.Max(1, workArea.Width), Math.Max(1, workArea.Height)),
                WorkArea = new Rect(0, 0, Math.Max(1, workArea.Width), Math.Max(1, workArea.Height)),
                IsPrimary = true
            };
        }

        private static double DistanceSquared(Point point, Rect rect)
        {
            double dx = DistanceToInterval(point.X, rect.Left, rect.Right);
            double dy = DistanceToInterval(point.Y, rect.Top, rect.Bottom);
            return dx * dx + dy * dy;
        }

        private static double DistanceSquared(Rect first, Rect second)
        {
            double dx = IntervalDistance(
                first.Left,
                first.Right,
                second.Left,
                second.Right);
            double dy = IntervalDistance(
                first.Top,
                first.Bottom,
                second.Top,
                second.Bottom);
            return dx * dx + dy * dy;
        }

        private static double DistanceToInterval(double value, double start, double end) =>
            value < start ? start - value : value > end ? value - end : 0;

        private static double IntervalDistance(
            double firstStart,
            double firstEnd,
            double secondStart,
            double secondEnd)
        {
            if (firstEnd < secondStart)
            {
                return secondStart - firstEnd;
            }

            return secondEnd < firstStart ? firstStart - secondEnd : 0;
        }

        private static double IntersectionArea(Rect first, Rect second)
        {
            Rect intersection = Rect.Intersect(first, second);
            return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
        }

        private static bool RectApproximatelyEquals(Rect first, Rect second)
        {
            return Math.Abs(first.Left - second.Left) <= GeometryTolerance &&
                   Math.Abs(first.Top - second.Top) <= GeometryTolerance &&
                   Math.Abs(first.Width - second.Width) <= GeometryTolerance &&
                   Math.Abs(first.Height - second.Height) <= GeometryTolerance;
        }
    }
}
