namespace DesktopOrganizer
{
    internal readonly record struct GridCell(int Column, int Row);

    internal readonly record struct GridPlacementRequest(
        string Key,
        int PreferredColumn,
        int PreferredRow);

    internal static class GridCellSearch
    {
        public static GridCell? FindNearest(
            int columns,
            int rows,
            int preferredColumn,
            int preferredRow,
            Func<int, int, bool> isAvailable)
        {
            ArgumentNullException.ThrowIfNull(isAvailable);
            if (columns <= 0 || rows <= 0)
            {
                return null;
            }

            preferredColumn = Math.Clamp(preferredColumn, 0, columns - 1);
            preferredRow = Math.Clamp(preferredRow, 0, rows - 1);

            GridCell? nearest = null;
            long nearestDistance = long.MaxValue;
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    if (!isAvailable(column, row))
                    {
                        continue;
                    }

                    long columnDistance = column - preferredColumn;
                    long rowDistance = row - preferredRow;
                    long distance = columnDistance * columnDistance + rowDistance * rowDistance;
                    if (distance < nearestDistance)
                    {
                        nearest = new GridCell(column, row);
                        nearestDistance = distance;
                    }
                }
            }

            return nearest;
        }
    }

    internal static class GridPlacementPlanner
    {
        public static Dictionary<string, GridCell>? TryPlan(
            int columns,
            int rows,
            IEnumerable<GridPlacementRequest> requests,
            IEnumerable<GridCell> occupied,
            Func<int, int, bool> isAvailable,
            IEqualityComparer<string>? keyComparer = null)
        {
            ArgumentNullException.ThrowIfNull(requests);
            ArgumentNullException.ThrowIfNull(occupied);
            ArgumentNullException.ThrowIfNull(isAvailable);

            var reserved = new HashSet<GridCell>(occupied);
            var placements = new Dictionary<string, GridCell>(
                keyComparer ?? EqualityComparer<string>.Default);

            foreach (GridPlacementRequest request in requests)
            {
                if (placements.ContainsKey(request.Key))
                {
                    return null;
                }

                GridCell? available = GridCellSearch.FindNearest(
                    columns,
                    rows,
                    request.PreferredColumn,
                    request.PreferredRow,
                    (column, row) =>
                        !reserved.Contains(new GridCell(column, row)) &&
                        isAvailable(column, row));
                if (!available.HasValue)
                {
                    return null;
                }

                placements.Add(request.Key, available.Value);
                reserved.Add(available.Value);
            }

            return placements;
        }
    }

    internal static class GridRefreshPlacementPlanner
    {
        public static Dictionary<string, GridCell?> Plan(
            int columns,
            int rows,
            IEnumerable<GridPlacementRequest> newItemRequests,
            IEnumerable<GridCell> persistedCells,
            Func<int, int, bool> isAvailable,
            IEqualityComparer<string>? keyComparer = null)
        {
            ArgumentNullException.ThrowIfNull(newItemRequests);
            ArgumentNullException.ThrowIfNull(persistedCells);
            ArgumentNullException.ThrowIfNull(isAvailable);

            var reserved = new HashSet<GridCell>(persistedCells);
            var placements = new Dictionary<string, GridCell?>(
                keyComparer ?? EqualityComparer<string>.Default);
            foreach (GridPlacementRequest request in newItemRequests)
            {
                GridCell? available = GridCellSearch.FindNearest(
                    columns,
                    rows,
                    request.PreferredColumn,
                    request.PreferredRow,
                    (column, row) =>
                        !reserved.Contains(new GridCell(column, row)) &&
                        isAvailable(column, row));
                if (!placements.TryAdd(request.Key, available))
                {
                    throw new ArgumentException(
                        $"Duplicate grid placement key: {request.Key}",
                        nameof(newItemRequests));
                }

                if (available.HasValue)
                {
                    reserved.Add(available.Value);
                }
            }

            return placements;
        }
    }
}
