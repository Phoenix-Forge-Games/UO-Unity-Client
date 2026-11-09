using System;
using System.Collections.Generic;
using System.Linq;
using UOClient.Data;

// NetTest census <dataFolder> <facet> <x0> <y0> <x1> <y1> [top]: counts static graphics in a rectangle.
internal static class StaticCensus
{
    public static int Run(string[] args)
    {
        string folder = args[1];
        int facet = int.Parse(args[2]);
        int x0 = int.Parse(args[3]), y0 = int.Parse(args[4]), x1 = int.Parse(args[5]), y1 = int.Parse(args[6]);
        int top = args.Length > 7 ? int.Parse(args[7]) : 150;
        var tiles = new TileData(System.IO.Path.Combine(folder, "tiledata.mul"));
        var counts = new Dictionary<ushort, int>();
        int total = 0;
        using (var map = new MapFacet(folder, facet))
        {
            for (int by = y0 >> 3; by <= y1 >> 3; by++)
            for (int bx = x0 >> 3; bx <= x1 >> 3; bx++)
            {
                MapBlock block = map.GetBlock(bx, by);
                if (block == null) continue;
                foreach (StaticTile s in block.Statics)
                {
                    counts.TryGetValue(s.Graphic, out int n);
                    counts[s.Graphic] = n + 1;
                    total++;
                }
            }
        }

        Console.WriteLine($"{total} statics, {counts.Count} distinct graphics");
        int running = 0;
        foreach (var pair in counts.OrderByDescending(p => p.Value).Take(top))
        {
            running += pair.Value;
            StaticTileInfo info = tiles.GetStatic(pair.Key);
            Console.WriteLine($"0x{pair.Key:X4}\t{pair.Value}\t{100.0 * running / total:0.0}%\t{info.Name}\th{info.Height}\t{info.Flags}");
        }

        return 0;
    }
}
