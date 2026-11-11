// Builds WorldData/<facet>/layers.bin: per-tile layers the baked chunks don't keep. One per harvest kind
// ("Mining.Ore", "Lumberjacking.Logs", "Fishing.Fish", from Content/Harvest.json tiles), one per crafting station
// ("Station.Anvil", "Station.Forge"... from Content/Crafting.json Rules.Stations, when present), and the housing layers
// (Content/Housing.json Layers: "Road" by land id, "Blocked" for anything impassable by tiledata flags, minus trees). Reads the UO map from
// ClientSettings.DataFolder and the decorations in WorldSource/Decoration. Edit FACETS to choose facets; takes about a minute per facet.
var facets = new[] { 1 };
string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, ".."));
var rules = new System.Collections.Generic.List<UOClient.Bake.TileLayers.Rule>();
void AddIds(System.Collections.Generic.HashSet<int> set, Newtonsoft.Json.Linq.JToken list)
{
    foreach (var t in list as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray())
    {
        if (t is Newtonsoft.Json.Linq.JArray pair && pair.Count == 2) { for (int i = (int)pair[0]; i <= (int)pair[1]; i++) set.Add(i); }
        else if (t.Type == Newtonsoft.Json.Linq.JTokenType.Integer) set.Add((int)t);
    }
}
var harvest = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(root, "Assets/UOClient/Content/Harvest.json")));
foreach (var system in ((Newtonsoft.Json.Linq.JObject)harvest["Systems"]).Properties())
{
    foreach (var d in (Newtonsoft.Json.Linq.JArray)system.Value["Definitions"])
    {
        var rule = new UOClient.Bake.TileLayers.Rule { Name = UOGame.Server.HarvestService.LayerName(system.Name, (string)d["Name"]) };
        AddIds(rule.Land, d["Tiles"]?["Land"]);
        AddIds(rule.Static, d["Tiles"]?["Static"]);
        rules.Add(rule);
    }
}
string craftingPath = System.IO.Path.Combine(root, "Assets/UOClient/Content/Crafting.json");
if (System.IO.File.Exists(craftingPath))
{
    var crafting = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(craftingPath));
    foreach (var station in ((crafting["Rules"]?["Stations"] as Newtonsoft.Json.Linq.JObject) ?? new Newtonsoft.Json.Linq.JObject()).Properties())
    {
        var rule = new UOClient.Bake.TileLayers.Rule { Name = "Station." + station.Name };
        AddIds(rule.Static, station.Value["Static"]);
        AddIds(rule.Land, station.Value["Land"]);
        rules.Add(rule);
    }
}
string housingPath = System.IO.Path.Combine(root, "Assets/UOClient/Content/Housing.json");
if (System.IO.File.Exists(housingPath))
{
    var housingLayers = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(housingPath))["Layers"] as Newtonsoft.Json.Linq.JObject;
    var tiles = new UOClient.Data.TileData(System.IO.Path.Combine(UOClient.ClientSettings.DataFolder, "tiledata.mul"));
    foreach (var layer in (housingLayers ?? new Newtonsoft.Json.Linq.JObject()).Properties())
    {
        var rule = new UOClient.Bake.TileLayers.Rule { Name = layer.Name };
        AddIds(rule.Land, layer.Value["Land"]);
        AddIds(rule.Static, layer.Value["Static"]);
        ulong landFlags = 0, staticFlags = 0;
        foreach (var f in layer.Value["LandFlags"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray()) landFlags |= (ulong)System.Enum.Parse(typeof(UOClient.Data.TileFlag), (string)f);
        foreach (var f in layer.Value["StaticFlags"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray()) staticFlags |= (ulong)System.Enum.Parse(typeof(UOClient.Data.TileFlag), (string)f);
        if (landFlags != 0) for (int i = 0; i < tiles.Land.Length; i++) if (((ulong)tiles.Land[i].Flags & landFlags) != 0) rule.Land.Add(i);
        if (staticFlags != 0) for (int i = 0; i < tiles.Statics.Length; i++) if (((ulong)tiles.Statics[i].Flags & staticFlags) != 0) rule.Static.Add(i);
        // Statics of other layers left out (trees: cleared with the plot instead of blocking it).
        foreach (var except in layer.Value["ExceptLayers"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray())
        {
            var other = rules.Find(r => r.Name == (string)except);
            if (other != null) rule.Static.ExceptWith(other.Static);
        }
        rules.Add(rule);
    }
}
var report = new System.Text.StringBuilder();
foreach (int facet in facets)
{
    // The world's decorations (WorldSource/Decoration, migrated from the shard): Britannia's set covers both
    // Felucca (0) and Trammel (1); each facet also has its own folder.
    string deco = System.IO.Path.Combine(root, "WorldSource", "Decoration");
    string[] names = { "Felucca", "Trammel", "Ilshenar", "Malas", "Tokuno", "TerMur" };
    var folders = new System.Collections.Generic.List<string>();
    if (facet <= 1) folders.Add(System.IO.Path.Combine(deco, "Britannia"));
    if (facet < names.Length) folders.Add(System.IO.Path.Combine(deco, names[facet]));
    var counts = UOClient.Bake.TileLayers.Build(UOClient.ClientSettings.DataFolder, facet, rules, System.IO.Path.Combine(root, "WorldData", facet.ToString()), folders);
    report.Append($"facet {facet}: ");
    foreach (var c in counts) report.Append($"{c.Key} {c.Value:N0}; ");
}
return report.ToString();
