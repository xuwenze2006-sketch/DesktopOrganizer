namespace DesktopOrganizer
{
    internal readonly record struct FreeIconPlacementRequest(
        string Key,
        double PreferredX,
        double PreferredY);

    internal static class FreeIconPlacementPlanner
    {
        public static Dictionary<string, Point>? TryPlan(
            IEnumerable<FreeIconPlacementRequest> requests,
            IEnumerable<Rect> usableAreas,
            IEnumerable<Rect> obstacles,
            double cellWidth,
            double cellHeight,
            IEqualityComparer<string>? keyComparer = null)
        {
            ArgumentNullException.ThrowIfNull(requests);
            ArgumentNullException.ThrowIfNull(usableAreas);
            ArgumentNullException.ThrowIfNull(obstacles);
            if (!double.IsFinite(cellWidth) || cellWidth <= 0 ||
                !double.IsFinite(cellHeight) || cellHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellWidth));
            }

            List<Rect> areas = usableAreas
                .Where(area =>
                    IsFiniteRect(area) &&
                    area.Width >= cellWidth &&
                    area.Height >= cellHeight)
                .ToList();
            if (areas.Count == 0)
            {
                return null;
            }

            List<Rect> occupied = obstacles
                .Where(IsFiniteRect)
                .ToList();
            var placements = new Dictionary<string, Point>(
                keyComparer ?? EqualityComparer<string>.Default);

            foreach (FreeIconPlacementRequest request in requests)
            {
                if (string.IsNullOrWhiteSpace(request.Key) ||
                    !double.IsFinite(request.PreferredX) ||
                    !double.IsFinite(request.PreferredY) ||
                    placements.ContainsKey(request.Key))
                {
                    return null;
                }

                Point? selected = null;
                IEnumerable<Rect> orderedAreas = areas
                    .Select((area, index) => new
                    {
                        Area = area,
                        Index = index,
                        Distance = DistanceSquaredToArea(
                            request.PreferredX,
                            request.PreferredY,
                            area,
                            cellWidth,
                            cellHeight)
                    })
                    .OrderBy(candidate => candidate.Distance)
                    .ThenBy(candidate => candidate.Index)
                    .Select(candidate => candidate.Area);

                foreach (Rect area in orderedAreas)
                {
                    foreach (Point candidate in EnumerateCandidates(
                                 request,
                                 area,
                                 cellWidth,
                                 cellHeight))
                    {
                        var bounds = new Rect(
                            candidate.X,
                            candidate.Y,
                            cellWidth,
                            cellHeight);
                        if (occupied.Any(obstacle => HasPositiveAreaOverlap(bounds, obstacle)))
                        {
                            continue;
                        }

                        selected = candidate;
                        occupied.Add(bounds);
                        break;
                    }

                    if (selected.HasValue)
                    {
                        break;
                    }
                }

                if (!selected.HasValue)
                {
                    return null;
                }

                placements.Add(request.Key, selected.Value);
            }

            return placements;
        }

        private static IEnumerable<Point> EnumerateCandidates(
            FreeIconPlacementRequest request,
            Rect area,
            double cellWidth,
            double cellHeight)
        {
            var seen = new HashSet<(double X, double Y)>();
            double clampedX = Math.Clamp(
                request.PreferredX,
                area.Left,
                area.Right - cellWidth);
            double clampedY = Math.Clamp(
                request.PreferredY,
                area.Top,
                area.Bottom - cellHeight);
            if (seen.Add((clampedX, clampedY)))
            {
                yield return new Point(clampedX, clampedY);
            }

            foreach (Point candidate in EnumerateAnchoredGrid(
                         request.PreferredX,
                         request.PreferredY,
                         area,
                         cellWidth,
                         cellHeight))
            {
                if (seen.Add((candidate.X, candidate.Y)))
                {
                    yield return candidate;
                }
            }

            foreach (Point candidate in EnumerateAnchoredGrid(
                         clampedX,
                         clampedY,
                         area,
                         cellWidth,
                         cellHeight))
            {
                if (seen.Add((candidate.X, candidate.Y)))
                {
                    yield return candidate;
                }
            }
        }

        private static IEnumerable<Point> EnumerateAnchoredGrid(
            double anchorX,
            double anchorY,
            Rect area,
            double cellWidth,
            double cellHeight)
        {
            int minColumnOffset = (int)Math.Ceiling((area.Left - anchorX) / cellWidth) - 1;
            int maxColumnOffset =
                (int)Math.Floor((area.Right - cellWidth - anchorX) / cellWidth) + 1;
            int minRowOffset = (int)Math.Ceiling((area.Top - anchorY) / cellHeight) - 1;
            int maxRowOffset =
                (int)Math.Floor((area.Bottom - cellHeight - anchorY) / cellHeight) + 1;

            // 批量移出原本沿分组右侧纵向排布：先在同列寻找完整单元，
            // 同列用尽后再扩展到相邻列，避免边界夹取把多项压成一叠。
            foreach (int columnOffset in OrderOffsets(
                         minColumnOffset,
                         maxColumnOffset,
                         preferNegative: false))
            {
                foreach (int rowOffset in OrderOffsets(
                             minRowOffset,
                             maxRowOffset,
                             preferNegative: true))
                {
                    yield return new Point(
                        Math.Clamp(
                            anchorX + columnOffset * cellWidth,
                            area.Left,
                            area.Right - cellWidth),
                        Math.Clamp(
                            anchorY + rowOffset * cellHeight,
                            area.Top,
                            area.Bottom - cellHeight));
                }
            }
        }

        private static IEnumerable<int> OrderOffsets(
            int minimum,
            int maximum,
            bool preferNegative)
        {
            if (maximum < minimum)
            {
                return [];
            }

            return Enumerable.Range(minimum, maximum - minimum + 1)
                .OrderBy(offset => Math.Abs((long)offset))
                .ThenBy(offset => offset == 0
                    ? 0
                    : preferNegative
                        ? offset < 0 ? 1 : 2
                        : offset > 0 ? 1 : 2);
        }

        private static double DistanceSquaredToArea(
            double x,
            double y,
            Rect area,
            double cellWidth,
            double cellHeight)
        {
            double clampedX = Math.Clamp(x, area.Left, area.Right - cellWidth);
            double clampedY = Math.Clamp(y, area.Top, area.Bottom - cellHeight);
            double deltaX = clampedX - x;
            double deltaY = clampedY - y;
            return deltaX * deltaX + deltaY * deltaY;
        }

        private static bool HasPositiveAreaOverlap(Rect first, Rect second) =>
            first.Left < second.Right &&
            first.Right > second.Left &&
            first.Top < second.Bottom &&
            first.Bottom > second.Top;

        private static bool IsFiniteRect(Rect rect) =>
            !rect.IsEmpty &&
            double.IsFinite(rect.X) &&
            double.IsFinite(rect.Y) &&
            double.IsFinite(rect.Width) &&
            double.IsFinite(rect.Height) &&
            rect.Width > 0 &&
            rect.Height > 0;
    }
}
