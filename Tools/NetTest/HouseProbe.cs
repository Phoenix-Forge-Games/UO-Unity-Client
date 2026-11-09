using System;
using System.Collections.Generic;
using UOClient.Data;

// NetTest house <dataFolder> <facet> <x0> <y0> <x1> <y1>: ASCII plan of walls, floors and roofs.
// Each tile prints as 3 chars: [wall edges][floor][roof]. Walls: '_' south edge, '|' east edge,
// 'L' both, '+' post, '#' full; floor 'f'; roof 'r'. Only ground-storey walls (z within 0..19 of the land).
internal static class HouseProbe
{
    public static int Run(string[] args)
    {
        var tiles = new TileData(System.IO.Path.Combine(args[1], "tiledata.mul"));
        int x0 = int.Parse(args[3]), y0 = int.Parse(args[4]), x1 = int.Parse(args[5]), y1 = int.Parse(args[6]);
        using (var art = new StaticArt(args[1]))
        using (var map = new MapFacet(args[1], int.Parse(args[2])))
        {
            var shapes = new WallShapes(art);
            Console.Write("      ");
            for (int x = x0; x <= x1; x++) Console.Write($"{x % 100,3}");
            Console.WriteLine();
            for (int y = y0; y <= y1; y++)
            {
                Console.Write($"{y} ");
                for (int x = x0; x <= x1; x++)
                {
                    int land = map.GetLand(x, y).Z;
                    char wall = ' ', floor = ' ', roof = ' ';
                    MapBlock block = map.GetBlock(x >> 3, y >> 3);
                    foreach (StaticTile s in block.Statics)
                    {
                        if (s.X != (x & 7) || s.Y != (y & 7)) continue;
                        StaticTileInfo info = tiles.GetStatic(s.Graphic);
                        if (info.Is(TileFlag.Roof)) { roof = 'r'; continue; }
                        if ((info.Is(TileFlag.Wall) || info.Is(TileFlag.Window) || info.Is(TileFlag.Door)) && s.Z - land >= -2 && s.Z - land < 20)
                        {
                            WallEdges e = shapes.Get(s.Graphic);
                            char c = (e & WallEdges.Full) != 0 ? '#' : (e & WallEdges.South) != 0 && (e & WallEdges.East) != 0 ? 'L'
                                   : (e & WallEdges.South) != 0 ? '_' : (e & WallEdges.East) != 0 ? '|' : (e & WallEdges.Post) != 0 ? '+' : '?';
                            if (info.Is(TileFlag.Window)) c = c == '_' ? 'w' : c == '|' ? 'W' : c;
                            wall = wall == ' ' ? c : (wall == c ? c : 'L');
                        }
                        else if (info.Is(TileFlag.Surface) && info.Height <= 1) floor = 'f';
                    }
                    Console.Write($"{wall}{floor}{roof}");
                }
                Console.WriteLine();
            }
        }
        return 0;
    }
}
