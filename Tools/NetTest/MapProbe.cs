using System;
using System.Linq;
using UOClient.Data;

// NetTest map <dataFolder> <facet> <x> <y>: prints land and statics around a point to check the readers.
internal static class MapProbe
{
    public static int Run(string[] args)
    {
        string folder = args[1];
        int facet = int.Parse(args[2]), x = int.Parse(args[3]), y = int.Parse(args[4]);
        var tiles = new TileData(System.IO.Path.Combine(folder, "tiledata.mul"));
        Console.WriteLine($"tiledata: {tiles.Land.Length} land, {tiles.Statics.Length} statics");
        using (var map = new MapFacet(folder, facet))
        {
            Console.WriteLine($"facet {facet}: {map.Width}x{map.Height}");
            for (int dy = -3; dy <= 3; dy++)
            {
                Console.WriteLine(string.Join(" ", Enumerable.Range(-6, 13).Select(dx =>
                {
                    LandCell c = map.GetLand(x + dx, y + dy);
                    return $"{tiles.Land[c.TileId].Name,-6}{c.Z,4}".Substring(0, 10);
                })));
            }

            MapBlock block = map.GetBlock(x >> 3, y >> 3);
            Console.WriteLine($"block ({x >> 3},{y >> 3}) statics: {block.Statics.Length}");
            foreach (StaticTile s in block.Statics.Take(25))
            {
                StaticTileInfo info = tiles.GetStatic(s.Graphic);
                Console.WriteLine($"  0x{s.Graphic:X4} {info.Name,-20} at {(x & ~7) + s.X},{(y & ~7) + s.Y},{s.Z} h{info.Height} {info.Flags}");
            }
        }

        return 0;
    }
}
