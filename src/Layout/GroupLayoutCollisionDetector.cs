using System.Windows;

namespace DesktopOrganizer
{
    internal static class GroupLayoutCollisionDetector
    {
        public static bool HasCollision(
            IReadOnlyList<Rect> groupBounds,
            Rect? obstacle = null)
        {
            for (int index = 0; index < groupBounds.Count; index++)
            {
                Rect current = groupBounds[index];
                if (obstacle.HasValue && current.IntersectsWith(obstacle.Value))
                {
                    return true;
                }

                for (int otherIndex = index + 1; otherIndex < groupBounds.Count; otherIndex++)
                {
                    if (current.IntersectsWith(groupBounds[otherIndex]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
