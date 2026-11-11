using System.Collections.Generic;
using UnityEngine;
using UOClient.Data;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// Places <see cref="PropCatalog"/> props for UO tiles: sized from the prefab's own (real-world) size,
    /// stretched along a row of tiles for grouped furniture, laid along a tile edge for fences, and turned
    /// with its back to a wall for wall furniture.
    /// </summary>
    public sealed class PropPlacer
    {
        /// <summary>A natural-size prop may overhang its tile by this much before it is shrunk.</summary>
        private const float Overhang = 1.15f;
        private const float MaxStretch = 2.5f;

        private readonly PropCatalog _catalog;
        private readonly TileData _tiles;
        private readonly WallShapes _walls;
        private readonly Dictionary<ushort, PropCatalog.Entry> _entries = new Dictionary<ushort, PropCatalog.Entry>();
        private readonly Dictionary<GameObject, Bounds> _bounds = new Dictionary<GameObject, Bounds>();
        private readonly int _layer = LayerMask.NameToLayer(HouseBuilder.LayerName);

        public PropPlacer(PropCatalog catalog, TileData tiles, WallShapes walls)
        {
            _catalog = catalog;
            _tiles = tiles;
            _walls = walls;
        }

        /// <summary>The facet whose walls wall furniture turns away from.</summary>
        public MapFacet Map { get; set; }

        /// <summary>
        /// Optional: the terrain surface (world Y) at a point in UO tile coordinates. Props lying on the
        /// ground are seated on it, not at the UO tile's Z, which is one corner's height on a slope.
        /// </summary>
        public System.Func<float, float, float> GroundY { get; set; }

        /// <summary>
        /// Optional: is this tile inside a generated house? The terrain is lowered under houses, so things there
        /// stand on the house floor (their UO Z), never on the terrain.
        /// </summary>
        public System.Func<int, int, bool> InHouse { get; set; }

        /// <summary>A prop this close (Z) to its tile's land lies on the ground.</summary>
        private const int OnGroundZ = 4;

        public PropCatalog.Entry EntryFor(ushort graphic)
        {
            if (_catalog == null || graphic >= _tiles.Statics.Length)
            {
                return null;
            }

            if (!_entries.TryGetValue(graphic, out PropCatalog.Entry entry))
            {
                StaticTileInfo info = _tiles.GetStatic(graphic);
                entry = _catalog.Match(info.Name, info.Height);
                _entries[graphic] = entry;
            }

            return entry;
        }

        /// <summary>
        /// Places the prop for tiles (x0, y0)-(x1, y1) (inclusive; one tile unless grouped) standing at z.
        /// <paramref name="serverItem"/>: a server item, not a map static (tables among them are remembered for seats;
        /// map tables are read from the map, so the result never depends on which block was built first).
        /// </summary>
        public GameObject Place(Transform parent, PropCatalog.Entry entry, ushort graphic, int x0, int y0, int x1, int y1, int z,
            bool serverItem = false)
        {
            uint hash = Hash(x0, y0, graphic);
            GameObject prefab = entry.Prefabs[(int)(hash % (uint)entry.Prefabs.Length)];
            if (prefab == null)
            {
                return null;
            }

            Bounds b = LocalBounds(prefab);
            float width = (x1 - x0 + 1) * WorldSpace.TileSize;  // along x
            float depth = (y1 - y0 + 1) * WorldSpace.TileSize;  // along z
            Vector3 centre = new Vector3(WorldSpace.X((x0 + x1 + 1) * 0.5f), z * WorldSpace.ZScale, WorldSpace.Z((y0 + y1 + 1) * 0.5f));
            float yaw;
            Vector3 scale = Vector3.one;

            switch (entry.Fit)
            {
                case PropFit.Edge:
                {
                    WallEdges edges = _walls != null ? _walls.Get(graphic) : WallEdges.None;
                    bool east = (edges & WallEdges.East) != 0 && (edges & WallEdges.South) == 0;
                    bool prefabAlongX = b.size.x >= b.size.z;
                    yaw = (east ? 90f : 0f) + (prefabAlongX ? 0f : 90f);
                    float along = Mathf.Max(b.size.x, b.size.z);
                    scale = Stretch(along > 0.01f ? WorldSpace.TileSize / along : 1f, prefabAlongX);
                    centre = east
                        ? new Vector3(WorldSpace.X(x0 + 1), centre.y, WorldSpace.Z(y0 + 0.5f))
                        : new Vector3(WorldSpace.X(x0 + 0.5f), centre.y, WorldSpace.Z(y0 + 1));
                    break;
                }

                case PropFit.Stair:
                {
                    // A lone step: Synty flights climb toward their local -x; turn it toward the higher neighbour.
                    int rise = Mathf.Max(_tiles.GetStatic(graphic).Height, 1);
                    yaw = ClimbYaw(x0, y0, z + rise) + 180f;
                    scale = new Vector3(
                        WorldSpace.TileSize / Mathf.Max(b.size.x, 0.01f),
                        rise * WorldSpace.ZScale / Mathf.Max(b.size.y, 0.01f),
                        WorldSpace.TileSize / Mathf.Max(b.size.z, 0.01f));
                    break;
                }

                case PropFit.LowWall:
                {
                    WallEdges edges = _walls != null ? _walls.Get(graphic) : WallEdges.None;
                    bool east = (edges & WallEdges.East) != 0 && (edges & WallEdges.South) == 0;
                    bool prefabAlongX = b.size.x >= b.size.z;
                    yaw = (east ? 90f : 0f) + (prefabAlongX ? 0f : 90f);
                    float along = prefabAlongX ? b.size.x : b.size.z, thick = prefabAlongX ? b.size.z : b.size.x;
                    // Down to whatever it stands on: UO hangs planter fronts and garden walls above the soil.
                    int topZ = z + Mathf.Max(_tiles.GetStatic(graphic).Height, 1);
                    int bottomZ = Mathf.Min(z, GroundBelow(x0, y0, z));

                    // A wall UO stacks on a planter (brick front over the soil) would stand twice its own
                    // height once grounded; cap it just above the planter's top, where the flowers start.
                    if (z - bottomZ > PlanterLift)
                    {
                        topZ = z + PlanterLift;
                    }
                    float height = (topZ - bottomZ) * WorldSpace.ZScale;
                    centre.y = bottomZ * WorldSpace.ZScale;
                    float a = WorldSpace.TileSize / Mathf.Max(along, 0.01f), t = entry.Footprint;
                    scale = prefabAlongX
                        ? new Vector3(a, height / Mathf.Max(b.size.y, 0.01f), t)
                        : new Vector3(t, height / Mathf.Max(b.size.y, 0.01f), a);
                    centre = east
                        ? new Vector3(WorldSpace.X(x0 + 1), centre.y, WorldSpace.Z(y0 + 0.5f))
                        : new Vector3(WorldSpace.X(x0 + 0.5f), centre.y, WorldSpace.Z(y0 + 1));
                    break;
                }

                case PropFit.Hang:
                {
                    // Flat against the wall beside the tile, facing away from it, bottom at the tile's Z.
                    (bool found, float wallYaw, Vector3 onWall) = WallBeside(x0, y0, z);
                    if (!found)
                    {
                        goto default;
                    }

                    bool thinAlongX = b.size.x < b.size.z;
                    yaw = wallYaw + (thinAlongX ? 90f : 0f);

                    // From just under the top of the wall it hangs on, never lower than 1.2 m off the ground.
                    float ground = GroundBelow(x0, y0, z) * WorldSpace.ZScale;
                    float top = (WallTopBeside(x0, y0, z) ?? z + 20) * WorldSpace.ZScale - HangBelowEaves;
                    float room = Mathf.Max(top - (ground + HangClearance), 0.3f);
                    float wide = Mathf.Max(b.size.x, b.size.z);
                    float s = Mathf.Min(1f, Mathf.Min(WorldSpace.TileSize * 0.9f / Mathf.Max(wide, 0.01f), room / Mathf.Max(b.size.y, 0.01f)));
                    scale = new Vector3(s, s, s);
                    centre = new Vector3(onWall.x, top - b.size.y * s, onWall.z);
                    break;
                }

                case PropFit.Bracket:
                {
                    (bool found, float wallYaw, Vector3 onWall) = WallBeside(x0, y0, z);
                    if (!found)
                    {
                        goto default;
                    }

                    // wallYaw turns a prop's +z away from the wall; a quarter turn more points the arm (-x) out.
                    yaw = wallYaw + 90f;
                    Vector3 outward = Quaternion.Euler(0f, wallYaw, 0f) * Vector3.forward;
                    float top = (WallTopBeside(x0, y0, z) ?? z + 20) * WorldSpace.ZScale - HangBelowEaves;
                    scale = Vector3.one;
                    centre = new Vector3(onWall.x, top - b.size.y, onWall.z) + outward * (b.size.x * 0.5f);
                    break;
                }

                case PropFit.Block:
                {
                    // Exactly the tile's footprint and UO height, so walkable blocks and steps match the server,
                    // but no higher than the next surface stacked on the tile (UO overlaps support blocks).
                    int rise = Mathf.Max(_tiles.GetStatic(graphic).Height, 1);
                    int? above = SurfaceAbove(x0, y0, z, z + rise);
                    if (above.HasValue)
                    {
                        rise = Mathf.Max(above.Value - z, 1);
                    }

                    float height = rise * WorldSpace.ZScale;
                    float side = WorldSpace.TileSize * entry.Footprint;
                    yaw = 0f;
                    scale = new Vector3(
                        side / Mathf.Max(b.size.x, 0.01f),
                        height / Mathf.Max(b.size.y, 0.01f),
                        side / Mathf.Max(b.size.z, 0.01f));
                    break;
                }

                case PropFit.Group when x1 > x0 || y1 > y0:
                {
                    bool rowAlongX = width >= depth;
                    bool prefabAlongX = b.size.x >= b.size.z;
                    yaw = rowAlongX == prefabAlongX ? 0f : 90f;
                    float prefabLong = Mathf.Max(b.size.x, b.size.z), prefabShort = Mathf.Min(b.size.x, b.size.z);
                    float rowLong = Mathf.Max(width, depth), rowShort = Mathf.Min(width, depth);
                    // Cover the whole UO footprint (a two-tile-wide table is two tiles wide), height as authored.
                    float across = Mathf.Clamp(rowShort / Mathf.Max(prefabShort, 0.01f), 0.5f, MaxStretch);
                    float stretch = Mathf.Clamp(rowLong / Mathf.Max(prefabLong, 0.01f), 0.5f, MaxStretch);
                    scale = prefabAlongX ? new Vector3(stretch, 1f, across) : new Vector3(across, 1f, stretch);

                    if (hash % 2 == 1)
                    {
                        yaw += 180f;
                    }

                    break;
                }

                default:
                {
                    float? toTable = entry.FaceTable ? FaceTableYaw(x0, y0, z) : null;
                    yaw = toTable ?? (entry.AgainstWall || entry.FaceTable ? FaceAwayFromWall(x0, y0, z, hash) : ((hash >> 3) % 4) * 90f);
                    float largest = Mathf.Max(b.size.x, b.size.z);
                    float s = largest > WorldSpace.TileSize * Overhang ? WorldSpace.TileSize * Overhang / largest : 1f;
                    scale = new Vector3(s, s, s);
                    break;
                }
            }

            if (entry.Fit == PropFit.Natural || entry.Fit == PropFit.Group)
            {
                float? ground = GroundUnder(x0, y0, x1, y1, z);
                if (ground.HasValue)
                {
                    centre.y = ground.Value;
                }
            }

            GameObject placed = Spawn(parent, prefab, b, Quaternion.Euler(0f, yaw, 0f), scale, centre);
            StaticTileInfo placedInfo = _tiles.GetStatic(graphic);
            if (serverItem && IsTable(placedInfo))
            {
                for (int ty = y0; ty <= y1; ty++)
                {
                    for (int tx = x0; tx <= x1; tx++)
                    {
                        _tableTiles.Add((tx, ty));
                    }
                }
            }
            if (entry.Fit != PropFit.Hang && entry.Fit != PropFit.Edge && entry.Fit != PropFit.LowWall)
            {
                Stack(placed, x0, y0, x1, y1, z);
            }

            return placed;
        }

        // ------------------------------------------------------------------ stacking

        /// <summary>One object in the per-tile stacks: where UO put it, and where it was first placed.</summary>
        private sealed class Stacked
        {
            public GameObject Go;
            public int X0, Y0, X1, Y1, Z;
        }

        private readonly Dictionary<(int X, int Y), List<Stacked>> _stacks = new Dictionary<(int X, int Y), List<Stacked>>();

        /// <summary>
        /// Records an object standing on tiles (x0, y0)-(x1, y1) at UO height z, and drops it (and anything
        /// already recorded above it) onto the real top of whatever stands beneath it. UO heights only give
        /// the order things are stacked in; the props' own sizes decide the heights, so a cup sits on the
        /// Synty table it was on in UO however tall that table is.
        /// </summary>
        public void Stack(GameObject go, int x0, int y0, int x1, int y1, int z)
        {
            if (go == null)
            {
                return;
            }

            var entry = new Stacked { Go = go, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Z = z };
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (!_stacks.TryGetValue((x, y), out List<Stacked> list))
                    {
                        list = new List<Stacked>();
                        _stacks[(x, y)] = list;
                    }

                    list.RemoveAll(o => o.Go == null || o.Go == go);
                    list.Add(entry);
                }
            }

            Restack(x0, y0, x1, y1);
        }

        /// <summary>Re-seats everything on these tiles, lowest first.</summary>
        private void Restack(int x0, int y0, int x1, int y1)
        {
            var affected = new List<Stacked>();
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (_stacks.TryGetValue((x, y), out List<Stacked> list))
                    {
                        list.RemoveAll(o => o.Go == null);
                        foreach (Stacked o in list)
                        {
                            if (!affected.Contains(o))
                            {
                                affected.Add(o);
                            }
                        }
                    }
                }
            }

            affected.Sort((a, b) => a.Z.CompareTo(b.Z));
            foreach (Stacked o in affected)
            {
                // The highest top among things UO put under it (lower Z) on any of its tiles.
                float? support = null;
                for (int y = o.Y0; y <= o.Y1; y++)
                {
                    for (int x = o.X0; x <= o.X1; x++)
                    {
                        if (!_stacks.TryGetValue((x, y), out List<Stacked> list))
                        {
                            continue;
                        }

                        foreach (Stacked under in list)
                        {
                            if (under != o && under.Go != null && under.Z < o.Z && o.Z - under.Z <= 20)
                            {
                                float top = TopOf(under.Go);
                                support = support.HasValue ? Mathf.Max(support.Value, top) : top;
                            }
                        }
                    }
                }

                if (support.HasValue)
                {
                    float bottom = BottomOf(o.Go);
                    o.Go.transform.position += Vector3.up * (support.Value - bottom);
                }
            }
        }

        private static float TopOf(GameObject go)
        {
            float top = float.MinValue;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                top = Mathf.Max(top, r.bounds.max.y);
            }

            return top == float.MinValue ? go.transform.position.y : top;
        }

        private static float BottomOf(GameObject go)
        {
            float bottom = float.MaxValue;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                bottom = Mathf.Min(bottom, r.bounds.min.y);
            }

            return bottom == float.MaxValue ? go.transform.position.y : bottom;
        }

        /// <summary>
        /// Yaw that turns a flight (climbing along local +x) toward the neighbouring tile whose walkable top
        /// is nearest the stair's own top: the next step or the landing it leads to.
        /// </summary>
        private float ClimbYaw(int x, int y, int top)
        {
            // East (+x) yaw 0; north (UO y-1, Unity +z) yaw -90; west 180; south (Unity -z) 90.
            (int dx, int dy) = ClimbDirection(x, y, top, Mathf.Max(top - StepRise(x, y, top), top - 5));
            return dx > 0 ? 0f : dx < 0 ? 180f : dy < 0 ? -90f : 90f;
        }

        /// <summary>
        /// Which way a stair step on (x, y) climbs: toward the neighbour whose walkable top meets the step's
        /// top (the landing or the next step), with the low ground on the opposite side. A stair beside it at
        /// the same height is its twin in a wide flight, never the way up.
        /// </summary>
        public (int Dx, int Dy) ClimbDirection(int x, int y, int top, int bottom)
        {
            var dirs = new (int Dx, int Dy)[] { (0, -1), (1, 0), (0, 1), (-1, 0) };
            (int, int) best = (0, -1);
            int bestScore = int.MaxValue;
            foreach ((int dx, int dy) in dirs)
            {
                int? up = TopNear(x + dx, y + dy, top);
                int? down = TopNear(x - dx, y - dy, bottom);
                if (up == null)
                {
                    continue;
                }

                int score = Mathf.Abs(up.Value - top);
                if (up.Value < top - 2) score += 20;                              // leads nowhere higher
                if (down.HasValue && down.Value > bottom + 2) score += 10;          // the foot isn't low ground
                if (StairTwinAt(x + dx, y + dy, top)) score += 30;                 // its twin in a wide flight
                if (score < bestScore)
                {
                    bestScore = score;
                    best = (dx, dy);
                }
            }

            return best;
        }

        private int StepRise(int x, int y, int top) => 5;

        /// <summary>Is there a stair step on this tile topping out at the same height?</summary>
        private bool StairTwinAt(int x, int y, int top)
        {
            MapBlock block = Map?.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return false;
            }

            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X != (x & 7) || tile.Y != (y & 7))
                {
                    continue;
                }

                PropCatalog.Entry entry = EntryFor(tile.Graphic);
                if (entry != null && entry.Fit == PropFit.Stair && Mathf.Abs(tile.Z + _tiles.GetStatic(tile.Graphic).Height - top) <= 1)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Pitch of a stair flight (degrees) where the run is long enough for it.</summary>
        private const float StairPitch = 35f;

        /// <summary>
        /// Places one flight for a run of UO stair tiles (x0, y0)-(x1, y1), from the lowest step's base
        /// <paramref name="bottomZ"/> up to <paramref name="topZ"/>, climbing toward <paramref name="climb"/>
        /// (UO tile direction: (1,0) east, (0,-1) north...).
        /// </summary>
        public GameObject PlaceFlight(Transform parent, PropCatalog.Entry entry, ushort graphic,
            int x0, int y0, int x1, int y1, int bottomZ, int topZ, (int Dx, int Dy) climb)
        {
            GameObject prefab = entry.Prefabs[0];
            if (prefab == null)
            {
                return null;
            }

            Bounds b = LocalBounds(prefab);
            bool alongX = climb.Dx != 0;
            float run = (alongX ? x1 - x0 + 1 : y1 - y0 + 1) * WorldSpace.TileSize;
            float width = (alongX ? y1 - y0 + 1 : x1 - x0 + 1) * WorldSpace.TileSize;
            float rise = Mathf.Max(topZ - bottomZ, 1) * WorldSpace.ZScale;
            // UO climbs about 5 Z per tile, which on 2.5 m tiles is a 14 degree ramp; a flight takes a natural pitch
            // instead, as long as the run allows, standing against the top step so it still meets the landing (the
            // run's lower tiles are floor).
            float length = Mathf.Clamp(rise / Mathf.Tan(StairPitch * Mathf.Deg2Rad), WorldSpace.TileSize * 0.5f, run);
            float half = length * 0.5f / WorldSpace.TileSize; // in tiles
            // Synty flights climb toward their local -x: east 180, west 0, north (Unity +z) 90, south -90.
            float yaw = climb.Dx > 0 ? 180f : climb.Dx < 0 ? 0f : climb.Dy < 0 ? 90f : -90f;
            var scale = new Vector3(
                length / Mathf.Max(b.size.x, 0.01f),
                rise / Mathf.Max(b.size.y, 0.01f),
                width / Mathf.Max(b.size.z, 0.01f));
            float cx = (x0 + x1 + 1) * 0.5f, cy = (y0 + y1 + 1) * 0.5f;
            if (alongX)
            {
                cx = (climb.Dx > 0 ? x1 + 1 : x0) - climb.Dx * half; // back from the high end
            }
            else
            {
                cy = (climb.Dy < 0 ? y0 : y1 + 1) - climb.Dy * half;
            }

            var centre = new Vector3(WorldSpace.X(cx), bottomZ * WorldSpace.ZScale, WorldSpace.Z(cy));

            return Spawn(parent, prefab, b, Quaternion.Euler(0f, yaw, 0f), scale, centre);
        }

        /// <summary>
        /// Puts the prefab in a holder that carries the turn and the fit scale, the prefab keeping its own
        /// root rotation and scale inside it (some presets are authored rotated), with the footprint centred
        /// on <paramref name="centre"/> and the base standing at its Y.
        /// </summary>
        private GameObject Spawn(Transform parent, GameObject prefab, Bounds b, Quaternion rotation, Vector3 scale, Vector3 centre)
        {
            var holder = new GameObject(prefab.name);
            holder.transform.SetParent(parent, false);
            GameObject instance = Object.Instantiate(prefab, holder.transform, false);
            instance.transform.localPosition = Vector3.zero;
            holder.transform.localScale = scale;
            holder.transform.rotation = rotation;
            Vector3 offset = rotation * Vector3.Scale(b.center, scale);
            holder.transform.position = new Vector3(centre.x - offset.x, centre.y - b.min.y * scale.y, centre.z - offset.z);
            Finish(holder);
            return holder;
        }

        /// <summary>
        /// The terrain surface under a footprint's centre, when the prop lies on the ground (its Z is near the
        /// land's); null for props standing on floors, tables or anything else.
        /// </summary>
        private float? GroundUnder(int x0, int y0, int x1, int y1, int z)
        {
            if (GroundY == null || Map == null || Mathf.Abs(Map.GetLand(x0, y0).Z - z) > OnGroundZ ||
                (InHouse != null && InHouse(x0, y0)))
            {
                return null;
            }

            return GroundY((x0 + x1 + 1) * 0.5f, (y0 + y1 + 1) * 0.5f);
        }

        private void Finish(GameObject instance)
        {
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider); // now, not at frame end: the world bake records this frame
            }

            if (_layer >= 0)
            {
                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = _layer;
                }
            }
        }

        /// <summary>A low wall floating more than this above the ground is a planter front; it stops this far above its base.</summary>
        private const int PlanterLift = 2;

        /// <summary>Gap between a hanging's top and the top of its wall.</summary>
        private const float HangBelowEaves = 0.25f;
        /// <summary>A hanging's bottom stays at least this far off the ground.</summary>
        private const float HangClearance = 1.2f;

        /// <summary>The top Z of the wall a hanging on this tile would hang on (the wall pieces stacked on that edge).</summary>
        private int? WallTopBeside(int x, int y, int z)
        {
            foreach ((int wx, int wy, WallEdges edge) in new[] { (x, y - 1, WallEdges.South), (x - 1, y, WallEdges.East), (x, y, WallEdges.South), (x, y, WallEdges.East) })
            {
                if (!WallOn(wx, wy, edge, z))
                {
                    continue;
                }

                MapBlock block = Map.GetBlock(wx >> 3, wy >> 3);
                int top = int.MinValue;
                foreach (StaticTile tile in block.Statics)
                {
                    StaticTileInfo info = _tiles.GetStatic(tile.Graphic);
                    if (tile.X == (wx & 7) && tile.Y == (wy & 7) && info.Is(TileFlag.Wall) && tile.Z >= z - 25 && tile.Z <= z + 20 &&
                        (_walls.Get(tile.Graphic) & (edge | WallEdges.Full)) != 0)
                    {
                        top = Mathf.Max(top, tile.Z + info.Height);
                    }
                }

                return top == int.MinValue ? (int?)null : top;
            }

            return null;
        }

        /// <summary>The highest land or surface top on a tile at or below <paramref name="z"/>.</summary>
        private int GroundBelow(int x, int y, int z)
        {
            MapBlock block = Map?.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return z;
            }

            int best = block.GetLand(x & 7, y & 7).Z;
            if (best > z)
            {
                best = int.MinValue;
            }

            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X != (x & 7) || tile.Y != (y & 7))
                {
                    continue;
                }

                StaticTileInfo info = _tiles.GetStatic(tile.Graphic);
                int top = tile.Z + info.Height;
                if ((info.Is(TileFlag.Surface) || info.Is(TileFlag.Bridge)) && top <= z && top > best)
                {
                    best = top;
                }
            }

            return best == int.MinValue ? z : best;
        }

        /// <summary>
        /// The wall beside a tile (north, west, south or east edge, standing around Z) for a hanging: the yaw
        /// that faces away from it and the point a hand's width off its face, centred on the tile.
        /// </summary>
        private (bool Found, float Yaw, Vector3 Point) WallBeside(int x, int y, int z)
        {
            const float Off = 0.2f;
            float cx = WorldSpace.X(x + 0.5f), cz = WorldSpace.Z(y + 0.5f);
            if (Map == null || _walls == null)
            {
                return (false, 0f, default);
            }

            if (WallOn(x, y - 1, WallEdges.South, z)) return (true, 180f, new Vector3(cx, 0f, WorldSpace.Z(y) - Off));
            if (WallOn(x - 1, y, WallEdges.East, z)) return (true, 90f, new Vector3(WorldSpace.X(x) + Off, 0f, cz));
            if (WallOn(x, y, WallEdges.South, z)) return (true, 0f, new Vector3(cx, 0f, WorldSpace.Z(y + 1) + Off));
            if (WallOn(x, y, WallEdges.East, z)) return (true, -90f, new Vector3(WorldSpace.X(x + 1) - Off, 0f, cz));
            return (false, 0f, default);
        }

        /// <summary>The lowest surface static on a tile starting above <paramref name="z"/> and below <paramref name="top"/>.</summary>
        private int? SurfaceAbove(int x, int y, int z, int top)
        {
            MapBlock block = Map?.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return null;
            }

            int? lowest = null;
            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X != (x & 7) || tile.Y != (y & 7) || tile.Z <= z || tile.Z >= top)
                {
                    continue;
                }

                StaticTileInfo info = _tiles.GetStatic(tile.Graphic);
                if ((info.Is(TileFlag.Surface) || info.Is(TileFlag.Bridge)) && (!lowest.HasValue || tile.Z < lowest.Value))
                {
                    lowest = tile.Z;
                }
            }

            return lowest;
        }

        /// <summary>The walkable top (static surface, or land) on a tile nearest the given Z.</summary>
        private int? TopNear(int x, int y, int z)
        {
            if (Map == null)
            {
                return null;
            }

            MapBlock block = Map.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return null;
            }

            int best = block.GetLand(x & 7, y & 7).Z;
            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X != (x & 7) || tile.Y != (y & 7))
                {
                    continue;
                }

                StaticTileInfo info = _tiles.GetStatic(tile.Graphic);
                if (!info.Is(TileFlag.Surface) && !info.Is(TileFlag.Bridge))
                {
                    continue;
                }

                int top = tile.Z + info.Height;
                if (Mathf.Abs(top - z) < Mathf.Abs(best - z))
                {
                    best = top;
                }
            }

            return best;
        }

        private static Vector3 Stretch(float factor, bool alongX) =>
            alongX ? new Vector3(factor, 1f, 1f) : new Vector3(1f, 1f, factor);

        private readonly HashSet<(int X, int Y)> _tableTiles = new HashSet<(int X, int Y)>();

        /// <summary>
        /// Yaw that turns a seat to face a table or counter on a neighbouring tile (map static or server
        /// item already placed), or null when there is none. Synty props face +z (UO north).
        /// </summary>
        private float? FaceTableYaw(int x, int y, int z)
        {
            foreach ((int dx, int dy, float yaw) in new[] { (0, -1, 0f), (1, 0, 90f), (0, 1, 180f), (-1, 0, -90f) })
            {
                if (_tableTiles.Contains((x + dx, y + dy)) || TableStaticAt(x + dx, y + dy, z))
                {
                    return yaw;
                }
            }

            return null;
        }

        /// <summary>Something to sit at: a surface you can't walk through (tables, counters, desks).</summary>
        private static bool IsTable(StaticTileInfo info) =>
            info.Is(TileFlag.Surface) && info.Is(TileFlag.Impassable) && !info.Is(TileFlag.Bridge);

        private bool TableStaticAt(int x, int y, int z)
        {
            MapBlock block = Map?.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return false;
            }

            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X != (x & 7) || tile.Y != (y & 7) || Mathf.Abs(tile.Z - z) > 4)
                {
                    continue;
                }

                if (IsTable(_tiles.GetStatic(tile.Graphic)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Yaw that puts the prop's back (local -z; Synty props face +z) to a wall beside the tile, or a
        /// stable random quarter turn when there is none.
        /// </summary>
        private float FaceAwayFromWall(int x, int y, int z, uint hash)
        {
            if (Map != null && _walls != null)
            {
                if (WallOn(x, y - 1, WallEdges.South, z)) return 180f; // wall to the north: face south
                if (WallOn(x, y, WallEdges.South, z)) return 0f;       // wall to the south: face north
                if (WallOn(x - 1, y, WallEdges.East, z)) return 90f;   // wall to the west: face east
                if (WallOn(x, y, WallEdges.East, z)) return -90f;      // wall to the east: face west
            }

            return ((hash >> 3) % 4) * 90f;
        }

        private bool WallOn(int x, int y, WallEdges edge, int z)
        {
            MapBlock block = Map.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return false;
            }

            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X != (x & 7) || tile.Y != (y & 7) || tile.Z < z - 4 || tile.Z > z + 15)
                {
                    continue;
                }

                if (_tiles.GetStatic(tile.Graphic).Is(TileFlag.Wall) && (_walls.Get(tile.Graphic) & (edge | WallEdges.Full)) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The prefab's mesh bounds in its own space (root unrotated, unscaled).</summary>
        private Bounds LocalBounds(GameObject prefab)
        {
            if (_bounds.TryGetValue(prefab, out Bounds cached))
            {
                return cached;
            }

            Bounds b = default;
            bool any = false;
            Matrix4x4 toRoot = Matrix4x4.TRS(Vector3.zero, prefab.transform.localRotation, prefab.transform.localScale) *
                               prefab.transform.worldToLocalMatrix;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                Matrix4x4 m = toRoot * filter.transform.localToWorldMatrix;
                Bounds mb = filter.sharedMesh.bounds;
                foreach (Vector3 corner in Corners(mb))
                {
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any)
                    {
                        b = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        b.Encapsulate(p);
                    }
                }
            }

            if (!any)
            {
                b = new Bounds(Vector3.up * 0.5f, Vector3.one);
            }

            _bounds[prefab] = b;
            return b;
        }

        private static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++)
            {
                yield return new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
            }
        }

        private static uint Hash(int x, int y, ushort graphic)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(graphic * 83492791);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                return h ^ (h >> 15);
            }
        }
    }
}
