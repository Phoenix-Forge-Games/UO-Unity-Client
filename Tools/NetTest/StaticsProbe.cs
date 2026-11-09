using System;
using UOClient.Data;

// NetTest statics <dataFolder> <facet> <x0> <y0> <x1> <y1> <minZ>: every static in a rectangle at or above minZ.
internal static class StaticsProbe
{
    public static int Run(string[] args)
    {
        var tiles = new TileData(System.IO.Path.Combine(args[1], "tiledata.mul"));
        int x0 = int.Parse(args[3]), y0 = int.Parse(args[4]), x1 = int.Parse(args[5]), y1 = int.Parse(args[6]), minZ = int.Parse(args[7]);
        using (var art = new StaticArt(args[1]))
        using (var map = new MapFacet(args[1], int.Parse(args[2])))
        {
            var shapes = new WallShapes(art);
            for (int by = y0 >> 3; by <= y1 >> 3; by++)
            for (int bx = x0 >> 3; bx <= x1 >> 3; bx++)
            {
                MapBlock block = map.GetBlock(bx, by);
                foreach (StaticTile s in block.Statics)
                {
                    int x = (bx << 3) + s.X, y = (by << 3) + s.Y;
                    if (x < x0 || x > x1 || y < y0 || y > y1 || s.Z < minZ) continue;
                    StaticTileInfo info = tiles.GetStatic(s.Graphic);
                    if (Environment.GetEnvironmentVariable("ROOFGRID") == "1")
                    {
                        if (!info.Is(TileFlag.Roof)) continue;
                        Console.WriteLine($"R	{x}	{y}	{s.Z}");
                        continue;
                    }

                    if (info.Is(TileFlag.Roof)) continue;
                    Console.WriteLine($"{x},{y},{s.Z}\t0x{s.Graphic:X4}\t{info.Name}\th{info.Height}\t{shapes.Get(s.Graphic)}\t{info.Flags}");
                }
            }
        }
        return 0;
    }
}
