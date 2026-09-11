using System.Windows;

namespace DesktopOrganizer
{
    internal static class SmartLayoutObstaclePolicy
    {
        public static List<Rect> Create(
            IEnumerable<Rect> folderPortalObstacles,
            Rect? recycleBinObstacle)
        {
            ArgumentNullException.ThrowIfNull(folderPortalObstacles);

            var obstacles = new List<Rect>(folderPortalObstacles);
            if (recycleBinObstacle.HasValue)
            {
                obstacles.Add(recycleBinObstacle.Value);
            }

            return obstacles;
        }
    }
}
