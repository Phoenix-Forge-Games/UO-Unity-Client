// Exports generated castles as editable prefabs (Assets/UOClient/Models/Castles/Castle_<x>_<y>.prefab), which the
// house builder then places instead of generating them (HouseBuilder.CastleOverride). Run in PLAY MODE in an empty
// scene. Set ANCHORS to the castles to export; WALLWALK_AREA (x0, y0, x1, y1), when non-empty, also exports the
// castle in that area that owns the most outworks (the wall-walk owner). An existing prefab is never overwritten.
// Props standing on or beside a castle's outworks (a signpost by the gate) go in its "Absorbed" group, named
// UOStatic_<graphic hex>_<x>_<y>_<z>, and the map stops drawing them.
var ANCHORS = new System.Collections.Generic.List<(int, int)> { (1484, 1544) };
var WALLWALK_AREA = new UnityEngine.RectInt(1400, 1610, 120, 40);
int FACET = 1;

var folder = UOClient.ClientSettings.DataFolder;
var content = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.Bake.WorldContent>("Assets/UOClient/Data/WorldContent.asset");
var tiles = new UOClient.Data.TileData(System.IO.Path.Combine(folder, "tiledata.mul"));
var art = new UOClient.Data.StaticArt(folder);
var map = new UOClient.Data.MapFacet(folder, FACET);
var walls = new UOClient.Data.WallShapes(art);
var index = new UOClient.HouseIndex(map, tiles, art, walls);
var placer = new UOClient.PropPlacer(content.Props, tiles, walls) { Map = map };
var log = new System.Text.StringBuilder();

UOClient.Data.HouseLayout Find(int ax, int ay)
{
    foreach (var h in index.Scan(ax >> 3, ay >> 3)) if (h.Anchor == (ax, ay)) return h;
    return null;
}

// The wall-walk owner: every castle anchored in the area, after the outworks are shared out.
if (WALLWALK_AREA.width > 0)
{
    UOClient.Data.HouseLayout best = null;
    for (int by = WALLWALK_AREA.yMin >> 3; by <= WALLWALK_AREA.yMax >> 3; by++)
    for (int bx = WALLWALK_AREA.xMin >> 3; bx <= WALLWALK_AREA.xMax >> 3; bx++)
        foreach (var h in index.Scan(bx, by))
        {
            if (!h.Castle) continue;
            index.ResolveOutworks(h);
            log.Append($"castle {h.Anchor} outworks {h.Outworks.Count}\n");
            if (best == null || h.Outworks.Count > best.Outworks.Count) best = h;
        }
    if (best != null && !ANCHORS.Contains(best.Anchor)) ANCHORS.Add(best.Anchor);
}

if (!UnityEditor.AssetDatabase.IsValidFolder(UOClient.HouseBuilder.CastleOverrideFolder))
    UnityEditor.AssetDatabase.CreateFolder("Assets/UOClient/Models", "Castles");

foreach (var (ax, ay) in ANCHORS)
{
    string path = $"{UOClient.HouseBuilder.CastleOverrideFolder}/Castle_{ax}_{ay}.prefab";
    if (System.IO.File.Exists(path)) { log.Append($"{path} exists, left alone\n"); continue; }
    var house = Find(ax, ay);
    if (house == null || !house.Castle) { log.Append($"no castle anchored at {ax},{ay}\n"); continue; }
    index.ResolveOutworks(house);
    var root = UOClient.HouseBuilder.Build(house);
    if (root == null) { log.Append($"castle {ax},{ay} failed to build\n"); continue; }

    // Props on and beside the outworks (not inside the keep, not water, floors or castle masonry).
    var absorbed = new UnityEngine.GameObject("Absorbed").transform;
    absorbed.SetParent(root.transform, false);
    var near = new System.Collections.Generic.HashSet<(int, int)>();
    foreach (var (x, y) in house.Outworks)
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            if (!house.Ground.Contains((x + dx, y + dy))) near.Add((x + dx, y + dy));
    foreach (var (x, y) in near)
    {
        var block = map.GetBlock(x >> 3, y >> 3);
        if (block == null) continue;
        foreach (var s in block.Statics)
        {
            if (s.X != (x & 7) || s.Y != (y & 7) || UOClient.Data.HouseLayout.IsCastleWall(s.Graphic)) continue;
            var info = tiles.GetStatic(s.Graphic);
            string name = info.Name ?? "";
            if (info.Is(UOClient.Data.TileFlag.Surface) || info.Is(UOClient.Data.TileFlag.Roof) || name.Contains("water")) continue;
            var entry = placer.EntryFor(s.Graphic);
            if (entry == null) continue;
            var go = placer.Place(absorbed, entry, s.Graphic, x, y, x, y, s.Z);
            if (go == null) continue;
            go.name = $"UOStatic_{s.Graphic:X4}_{x}_{y}_{s.Z}";
            log.Append($"  absorbed {name} {go.name}\n");
        }
    }

    root.transform.position = UnityEngine.Vector3.zero; // the builder places the prefab at the castle's RootPosition
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
    UnityEngine.Object.Destroy(root);
    log.Append($"saved {path} ({house.Outworks.Count} outwork tiles)\n");
}

map.Dispose(); art.Dispose();
return log.ToString();
