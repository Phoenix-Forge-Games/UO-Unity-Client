using System;
using UOClient.Data;

// NetTest art <dataFolder> <graphic hex>...: prints each static's opacity mask as ASCII, 2x2 pixels per char.
internal static class ArtProbe
{
    public static int Run(string[] args)
    {
        var tiles = new TileData(System.IO.Path.Combine(args[1], "tiledata.mul"));
        using (var art = new StaticArt(args[1]))
        {
            for (int a = 2; a < args.Length; a++)
            {
                ushort graphic = Convert.ToUInt16(args[a], 16);
                ArtMask mask = art.Get(graphic);
                StaticTileInfo info = tiles.GetStatic(graphic);
                Console.WriteLine($"== 0x{graphic:X4} {info.Name} h{info.Height} {mask?.Width}x{mask?.Height} -> {WallShapes.Classify(mask)} dark={mask?.DarkShare:0.00}");
                if (mask == null || Environment.GetEnvironmentVariable("ART_QUIET") == "1") continue;
                for (int y = 0; y < mask.Height; y += 2)
                {
                    var line = new char[(mask.Width + 1) / 2];
                    for (int x = 0; x < mask.Width; x += 2)
                        line[x / 2] = mask[x, y] || mask[x + 1, y] || mask[x, y + 1] || mask[x + 1, y + 1] ? '#' : '.';
                    Console.WriteLine(new string(line));
                }
            }
        }
        return 0;
    }
}
