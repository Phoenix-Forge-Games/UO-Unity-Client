using System;
using System.Collections.Generic;
using System.Linq;
using UOClient.Data;

// NetTest landcensus <dataFolder> <facet> [step]: land tile names across a whole facet, sampled.
internal static class LandCensus
{
    public static int Run(string[] args)
    {
        var tiles = new TileData(System.IO.Path.Combine(args[1], "tiledata.mul"));
        int step = args.Length > 3 ? int.Parse(args[3]) : 4;
        var counts = new Dictionary<string, int>();
        int total = 0;
        using (var map = new MapFacet(args[1], int.Parse(args[2])))
        {
            for (int y = 0; y < map.Height; y += step)
            {
                for (int x = 0; x < map.Width; x += step)
                {
                    string name = tiles.Land[map.GetLand(x, y).TileId].Name;
                    counts.TryGetValue(name, out int n);
                    counts[name] = n + 1;
                    total++;
                }

                if ((y & 63) == 0) map.Trim(0, y >> 3, 1);
            }
        }

        foreach (var pair in counts.OrderByDescending(p => p.Value))
            Console.WriteLine($"{pair.Value,8} {100.0 * pair.Value / total,6:0.00}%  {pair.Key}");
        return 0;
    }
}
