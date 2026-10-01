namespace DesktopOrganizer;

internal static class GroupExpansionPlacementPolicy
{
    public static bool TryFind(Rect expanded, IEnumerable<Rect> workAreas, IReadOnlyList<Rect> obstacles, out Point position)
    {
        Rect[] areas=workAreas.ToArray();
        position=expanded.Location;
        if(areas.Any(area=>area.Contains(expanded)) && !obstacles.Any(obstacle => { Rect overlap=Rect.Intersect(expanded,obstacle); return !overlap.IsEmpty && overlap.Width>0.5 && overlap.Height>0.5; })) return true;
        const double gap=8.01;
        double bestDistance=double.PositiveInfinity;
        foreach(Rect area in areas)
        {
            if(expanded.Width>area.Width || expanded.Height>area.Height) continue;
            Rect nearby=area; nearby.Inflate(gap,gap);
            Rect[] local=obstacles.Where(nearby.IntersectsWith).ToArray();
            double[] xs=local.Select(o=>o.Left-expanded.Width-gap).Concat(local.Select(o=>o.Right+gap))
                .Concat(new[]{expanded.X,area.Left,area.Right-expanded.Width})
                .Where(x=>x>=area.Left && x+expanded.Width<=area.Right).Distinct().ToArray();
            double[] ys=local.Select(o=>o.Top-expanded.Height-gap).Concat(local.Select(o=>o.Bottom+gap))
                .Concat(new[]{expanded.Y,area.Top,area.Bottom-expanded.Height})
                .Where(y=>y>=area.Top && y+expanded.Height<=area.Bottom).Distinct().ToArray();
            foreach(double y in ys)
                foreach(double x in xs)
                {
                    double distance=(x-expanded.X)*(x-expanded.X)+(y-expanded.Y)*(y-expanded.Y);
                    if(distance>=bestDistance) continue;
                    var candidate=new Rect(x,y,expanded.Width,expanded.Height);
                    candidate.Inflate(8,8);
                    if(local.Any(candidate.IntersectsWith)) continue;
                    position=new Point(x,y);bestDistance=distance;
                }
        }
        return double.IsFinite(bestDistance);
    }
}
