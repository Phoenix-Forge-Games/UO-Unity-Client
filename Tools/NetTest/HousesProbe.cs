using System;
using System.Collections.Generic;
using System.Linq;
using UOClient.Data;

// NetTest houses <dataFolder> <facet> <x0> <y0> <x1> <y1> [plan|misses]: detects houses in a rectangle;
// 'misses' lists clusters of roof tiles that no detected house claims (footprint + 2 tiles).
internal static class HousesProbe
{
    public static int Run(string[] args)
    {
        var tiles = new TileData(System.IO.Path.Combine(args[1], "tiledata.mul"));
        LandClassifier.Initialize(tiles);
        int x0 = int.Parse(args[3]), y0 = int.Parse(args[4]), x1 = int.Parse(args[5]), y1 = int.Parse(args[6]);
        bool plan = args.Length > 7 && args[7] == "plan";
        using (var art = new StaticArt(args[1]))
        using (var map = new MapFacet(args[1], int.Parse(args[2])))
        {
            var detector = new HouseDetector(map, tiles, art);
            if (Environment.GetEnvironmentVariable("HOUSETRACE") == "1") detector.Trace = Console.WriteLine;
            var visited = new HashSet<(int, int)>();
            var claimed = new HashSet<(int, int)>();
            bool misses = args.Length > 7 && args[7] == "misses";
            int count = 0;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (visited.Contains((x, y))) continue;
                HouseLayout h = detector.Detect(x, y, visited);
                if (h == null) continue;
                count++;
                foreach (var (gx, gy) in h.Ground)
                    for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++) claimed.Add((gx + dx, gy + dy));
                foreach (var (gx, gy) in h.Porch)
                    for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++) claimed.Add((gx + dx, gy + dy));
                if (h.Porch.Count > 0) Console.WriteLine($"   porch {h.Porch.Count} tiles at {h.Porch.Min(t => t.Item1)},{h.Porch.Min(t => t.Item2)}-{h.Porch.Max(t => t.Item1)},{h.Porch.Max(t => t.Item2)}");
                if (misses) continue;
                string storeys = string.Join("/", h.Storeys.Select(s => s.Count));
                string doors = string.Join(" ", h.Doors.Select(d => $"{d.X},{d.Y}{d.Side.ToString()[0]}"));
                Console.WriteLine($"house #{count} at {h.MinX},{h.MinY}-{h.MaxX},{h.MaxY} ({h.MaxX - h.MinX + 1}x{h.MaxY - h.MinY + 1}) floorZ={h.FloorZ} tiles/storey={storeys} walls={h.Walls} thatch={h.ThatchRoof} doors[{h.Doors.Count}]: {doors} windows={h.Windows.Count}");
                if (!plan) continue;
                for (int ty = h.MinY; ty <= h.MaxY; ty++)
                {
                    var line = new char[h.MaxX - h.MinX + 1];
                    for (int tx = h.MinX; tx <= h.MaxX; tx++)
                    {
                        char c = h.Ground.Contains((tx, ty)) ? (h.Storeys.Count > 1 && h.Storeys[1].Contains((tx, ty)) ? '2' : '1') : '.';
                        if (h.Doors.Any(d => d.X == tx && d.Y == ty)) c = 'D';
                        line[tx - h.MinX] = c;
                    }
                    Console.WriteLine("   " + new string(line));
                }
            }
            Console.WriteLine($"{count} houses");
            if (misses)
            {
                var roofs = new HashSet<(int, int)>();
                for (int by = y0 >> 3; by <= y1 >> 3; by++)
                for (int bx = x0 >> 3; bx <= x1 >> 3; bx++)
                    foreach (StaticTile st in map.GetBlock(bx, by).Statics)
                    {
                        int sx = (bx << 3) + st.X, sy = (by << 3) + st.Y;
                        if (sx < x0 || sy < y0 || sx > x1 || sy > y1 || claimed.Contains((sx, sy))) continue;
                        if (tiles.GetStatic(st.Graphic).Is(TileFlag.Roof)) roofs.Add((sx, sy));
                    }
                var seen = new HashSet<(int, int)>();
                var clusters = new List<(int n, int ax, int ay, int bx2, int by2)>();
                foreach (var start in roofs)
                {
                    if (!seen.Add(start)) continue;
                    var q = new Queue<(int, int)>(); q.Enqueue(start);
                    int n = 0, ax = start.Item1, ay = start.Item2, bx2 = ax, by2 = ay;
                    while (q.Count > 0)
                    {
                        var (cx, cy) = q.Dequeue(); n++;
                        ax = Math.Min(ax, cx); ay = Math.Min(ay, cy); bx2 = Math.Max(bx2, cx); by2 = Math.Max(by2, cy);
                        for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (roofs.Contains((cx + dx, cy + dy)) && seen.Add((cx + dx, cy + dy))) q.Enqueue((cx + dx, cy + dy));
                    }
                    clusters.Add((n, ax, ay, bx2, by2));
                }
                foreach (var c in clusters.OrderByDescending(c => c.n))
                    if (c.n >= 6) Console.WriteLine($"miss {c.n,4} roof tiles at {c.ax},{c.ay}-{c.bx2},{c.by2}");
            }
        }
        return 0;
    }
}
