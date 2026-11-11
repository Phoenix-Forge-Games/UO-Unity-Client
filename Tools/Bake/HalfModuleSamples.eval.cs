// Play mode: builds a row of synthetic houses on the 1.25 m plan grid (HalfModulePlan.md) far from the map, for
// close inspection of the cases central Britain may not contain: an odd x odd gable (centred-overhang fallback),
// half roof bays, single and double doors, a two-storey stair, an L with crossing ranges, and every wall set.
// Leaves them in the scene under "HalfModule Samples"; returns a line per house.
using UOClient.Buildings.Synty;
var old = UnityEngine.GameObject.Find("HalfModule Samples");
if (old != null) UnityEngine.Object.Destroy(old);
var root = new UnityEngine.GameObject("HalfModule Samples").transform;
root.position = new UnityEngine.Vector3(0f, 500f, 0f);
var sb = new System.Text.StringBuilder();
float x = 0f;

SyntyBuilding Spec(string name, SyntyLook look, RoofKind roof)
{
    return new SyntyBuilding { Name = name, Plinth = false, Dress = false, Sign = false, Dormers = false, Chimney = false,
        IntersectWings = true, NoPartitions = true, Look = look, Roof = roof, EntryRoom = "R0" };
}

void Make(SyntyBuilding s)
{
    SyntyBuildingGenerator.DressingEnabled = false;
    SyntyBuildingGenerator.ResetDoors();
    var go = SyntyBuildingGenerator.Build(s);
    go.transform.SetParent(root, false);
    go.transform.localPosition = new UnityEngine.Vector3(x, 0f, 0f);
    int w = 0;
    foreach (var r in s.Rooms) w = UnityEngine.Mathf.Max(w, r.X2);
    x += w * SyntyKit.Unit + 8f;
    int doors = go.GetComponentsInChildren<UOClient.Buildings.BuildingDoor>(true).Length;
    sb.Append(s.Name + ": doors " + doors + ", failures " + SyntyBuildingGenerator.DoorFailures.Count + "\n");
}

// 1. 5 x 7, odd by odd: centred-overhang gable; one single UO door on the south face.
var a = Spec("Odd5x7", SyntyLook.Plaster, RoofKind.GableTile);
a.Rooms.Add(new SyntyRoom("R0", 0, 0, 5, 7));
a.EntryPoints.Add(new UnityEngine.Vector2(2.5f * SyntyKit.Unit, 0f)); a.EntryWidths.Add(1);
Make(a);

// 2. 6 x 9, two storeys, thatch: even span, odd run (half bay); double door; a stair.
var b = Spec("TwoStorey6x9", SyntyLook.PlasterBeams, RoofKind.GableThatch);
b.Rooms.Add(new SyntyRoom("R0", 0, 0, 6, 9));
b.Above.Add(new System.Collections.Generic.List<SyntyRoom> { new SyntyRoom("S1R0", 0, 0, 6, 9) });
b.EntryPoints.Add(new UnityEngine.Vector2(3f * SyntyKit.Unit, 0f)); b.EntryWidths.Add(2);
Make(b);

// 3. An L: 9 x 5 plus a 4 x 4 wing on its north-west, crossing ranges.
var c = Spec("L9x5", SyntyLook.Wooden, RoofKind.GableTile);
c.Rooms.Add(new SyntyRoom("R0", 0, 0, 9, 5));
c.Rooms.Add(new SyntyRoom("R1", 0, 5, 4, 4));
c.RoofWings.Add(new SyntyRoofWing(0, 0, 9, 5, 1));
c.RoofWings.Add(new SyntyRoofWing(0, 2, 4, 7, 1));
c.EntryPoints.Add(new UnityEngine.Vector2(6.5f * SyntyKit.Unit, 0f)); c.EntryWidths.Add(1);
c.RoofPitch = 35f;
Make(c);

// 4. 7 x 4 wooden, two storeys: an odd wall each way, a door on the east face.
var d = Spec("Wooden7x4", SyntyLook.Wooden, RoofKind.GableTile);
d.Rooms.Add(new SyntyRoom("R0", 0, 0, 7, 6));
d.Above.Add(new System.Collections.Generic.List<SyntyRoom> { new SyntyRoom("S1R0", 0, 0, 7, 6) });
d.EntryPoints.Add(new UnityEngine.Vector2(7f * SyntyKit.Unit, 2.5f * SyntyKit.Unit)); d.EntryWidths.Add(1);
Make(d);

// 5. 7 x 5 brick, flat roof with parapet.
var e = Spec("Flat7x5", SyntyLook.BrickGrey, RoofKind.Flat);
e.Rooms.Add(new SyntyRoom("R0", 0, 0, 7, 5));
e.EntryPoints.Add(new UnityEngine.Vector2(3.5f * SyntyKit.Unit, 0f)); e.EntryWidths.Add(1);
Make(e);

// 6. A custom-house plinth, 5 x 3 tiles.
var tiles = new System.Collections.Generic.HashSet<UnityEngine.Vector2Int>();
for (int tz = 0; tz < 3; tz++) for (int tx = 0; tx < 5; tx++) tiles.Add(new UnityEngine.Vector2Int(tx, tz));
var pr = new UnityEngine.GameObject("Plinth5x3").transform; pr.SetParent(root, false); pr.localPosition = new UnityEngine.Vector3(x, 0f, 0f);
SyntyBuildingGenerator.BuildPlinth(tiles, pr);
sb.Append("plinth built\n");
return sb.ToString();
