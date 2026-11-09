using System;
using System.Collections.Generic;
using System.Linq;
using UOClient.Data;

// NetTest tilenames <dataFolder> <regex>: distinct static names matching a pattern, with ID counts and ranges.
internal static class TileNames
{
    public static int Run(string[] args)
    {
        var tiles = new TileData(System.IO.Path.Combine(args[1], "tiledata.mul"));
        var regex = new System.Text.RegularExpressions.Regex(args[2], System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var groups = new SortedDictionary<string, List<int>>();
        for (int i = 0; i < tiles.Statics.Length; i++)
        {
            string name = tiles.Statics[i].Name;
            if (string.IsNullOrEmpty(name) || !regex.IsMatch(name)) continue;
            if (!groups.TryGetValue(name, out var ids)) groups[name] = ids = new List<int>();
            ids.Add(i);
        }

        foreach (var g in groups)
            Console.WriteLine($"{g.Value.Count,4}  {g.Key,-22} 0x{g.Value.First():X4}-0x{g.Value.Last():X4}  h{tiles.Statics[g.Value[0]].Height} {tiles.Statics[g.Value[0]].Flags}");
        return 0;
    }
}
