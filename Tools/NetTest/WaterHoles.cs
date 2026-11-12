using System;
using UOClient.Data;

// NetTest waterholes <dataFolder> <facet>: counts water tiles (water statics, as TerrainStreamer sees them) standing
// on void land ("NoName", NODRAW...), which the terrain used to cut out as holes, leaving nothing under the water.
internal static class WaterHoles
{
    private static bool IsVoid(ushort id, string name) =>
        SurfaceQuery.IsVoid(id) || string.IsNullOrEmpty(name) || name == "NoName" ||
        name.StartsWith("VOID", StringComparison.OrdinalIgnoreCase) || name.Equals("NODRAW", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        var tiles = new TileData(System.IO.Path.Combine(args[1], "tiledata.mul"));
        int water = 0, holes = 0;
        var chunks = new System.Collections.Generic.HashSet<(int, int)>();
        using (var map = new MapFacet(args[1], int.Parse(args[2])))
        {
            for (int by = 0; by < map.Height >> 3; by++)
            {
                for (int bx = 0; bx < map.Width >> 3; bx++)
                {
                    MapBlock block = map.GetBlock(bx, by);
                    if (block == null) continue;
                    var seen = new bool[64];
                    foreach (StaticTile s in block.Statics)
                    {
                        StaticTileInfo info = tiles.GetStatic(s.Graphic);
                        if (!info.Is(TileFlag.Wet) || info.Name == null || info.Name.IndexOf("water", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (seen[s.Y * 8 + s.X]) continue;
                        seen[s.Y * 8 + s.X] = true;
                        water++;
                        int x = bx * 8 + s.X, y = by * 8 + s.Y;
                        ushort land = map.GetLand(x, y).TileId;
                        if (IsVoid(land, tiles.Land[land].Name)) { holes++; chunks.Add((x >> 6, y >> 6)); }
                    }
                }

                if ((by & 7) == 0) map.Trim(0, by, 1);
            }
        }

        Console.WriteLine($"facet {args[2]}: {water} water-static tiles, {holes} of them on void land (were terrain holes) in {chunks.Count} chunks");
        return 0;
    }
}
