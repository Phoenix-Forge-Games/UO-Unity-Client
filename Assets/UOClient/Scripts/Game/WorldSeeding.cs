using System.Collections.Generic;
using UnityEngine;
using UOClient;
using UOClient.Bake;
using UOClient.Buildings;
using UOClient.Data;
using UOGame.Content;

namespace UOGame
{
    /// <summary>
    /// What the bake seeds into the world as it writes each chunk (<see cref="WorldBaker.SeedChunk"/>), from the
    /// <see cref="RoomLightKit"/>: a lantern each side of every outside door (<see cref="DoorLights"/>), and street lamps
    /// along the roads, a lamp in every CitySpacing tiles square in cities, VillageSpacing in villages and WildSpacing out
    /// in the wilds, plus the kit's anchors placed exactly. UO's own lamps (DropProps) are left out. Deterministic: the
    /// same map makes the same lamps, whatever order the chunks bake in. The lamps light up at runtime through
    /// <see cref="WorldLights"/> by their names.
    /// </summary>
    public static class WorldSeeding
    {
        private const string StreetLampsName = "Street lamps";

        /// <summary>How far round the chunk the bake's other buildings get looked at (metres).</summary>
        private const float LookMargin = 16f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register() => WorldBaker.SeedChunk = Seed;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterInEditor() => WorldBaker.SeedChunk = Seed;
#endif

        public static void Seed(WorldBaker.ChunkSeed chunk)
        {
            RoomLightKit kit = Resources.Load<RoomLightKit>("World/RoomLights");
            if (kit == null || chunk.Blocks == null || chunk.Blocks.Count == 0)
            {
                return;
            }

            if (!HousingRules.Loaded && kit.Housing != null && kit.Regions != null)
            {
                HousingRules.Load(kit.Housing, kit.Regions);
            }

            Drop(chunk, kit);

            // The bake draws without collision; looking for walls, roofs and clear ground needs it for a moment.
            List<Collider> temporary = TemporaryColliders(chunk);
            Physics.SyncTransforms();
            try
            {
                var doors = new List<Vector3>();
                foreach (GameObject block in chunk.Blocks)
                {
                    foreach (BuildingDoor door in block.GetComponentsInChildren<BuildingDoor>(true))
                    {
                        doors.Add(door.OpeningCentre);
                        DoorLights.HangLanterns(door, kit);
                    }
                }

                StreetLamps(chunk, kit, doors);
            }
            finally
            {
                foreach (Collider c in temporary)
                {
                    Object.DestroyImmediate(c);
                }
            }
        }

        // ------------------------------------------------------------------ UO's own lamps

        private static void Drop(WorldBaker.ChunkSeed chunk, RoomLightKit kit)
        {
            if (kit.DropProps == null || kit.DropProps.Length == 0)
            {
                return;
            }

            var gone = new List<GameObject>();
            foreach (GameObject block in chunk.Blocks)
            {
                foreach (Transform t in block.GetComponentsInChildren<Transform>(true))
                {
                    foreach (string prefix in kit.DropProps)
                    {
                        if (!string.IsNullOrEmpty(prefix) && t.name.StartsWith(prefix, System.StringComparison.Ordinal))
                        {
                            // The holder of the same name above a placed instance goes with it.
                            Transform top = t.parent != null && t.parent.name.StartsWith(prefix, System.StringComparison.Ordinal) ? t.parent : t;
                            if (!gone.Contains(top.gameObject))
                            {
                                gone.Add(top.gameObject);
                            }

                            break;
                        }
                    }
                }
            }

            foreach (GameObject go in gone)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }
        }

        // ------------------------------------------------------------------ street lamps

        private static void StreetLamps(WorldBaker.ChunkSeed chunk, RoomLightKit kit, List<Vector3> doors)
        {
            if (kit.StreetLamp == null || chunk.Map == null)
            {
                return;
            }

            int x0 = chunk.Chunk.x * ChunkFormat.ChunkTiles, y0 = chunk.Chunk.y * ChunkFormat.ChunkTiles;
            var taken = new List<RectInt>(); // squares that already have their lamp (the anchors')
            Transform holder = null;
            Transform Holder() => holder ??= MakeHolder(chunk.Blocks[0].transform);

            foreach (RoomLightKit.Anchor anchor in kit.Anchors)
            {
                Vector2Int tile = WorldSpace.ToTile(anchor.Position);
                if (tile.x < x0 || tile.y < y0 || tile.x >= x0 + ChunkFormat.ChunkTiles || tile.y >= y0 + ChunkFormat.ChunkTiles)
                {
                    continue;
                }

                Vector3 foot = anchor.Position;
                foot.y = GroundAt(chunk, foot, foot.y);
                Place(kit.StreetLamp, foot, Quaternion.Euler(0f, anchor.Yaw, 0f), Holder());
                int s = SpacingAt(chunk.Facet, tile.x, tile.y, kit);
                taken.Add(new RectInt(tile.x - tile.x % s, tile.y - tile.y % s, s, s));
            }

            foreach (int spacing in new[] { kit.CitySpacing, kit.VillageSpacing, kit.WildSpacing })
            {
                if (spacing <= 0 || ChunkFormat.ChunkTiles % spacing != 0)
                {
                    continue;
                }

                for (int cy = y0; cy < y0 + ChunkFormat.ChunkTiles; cy += spacing)
                {
                    for (int cx = x0; cx < x0 + ChunkFormat.ChunkTiles; cx += spacing)
                    {
                        // This square's lamp is set by the kind of place at its middle.
                        if (SpacingAt(chunk.Facet, cx + spacing / 2, cy + spacing / 2, kit) != spacing ||
                            taken.Exists(r => r.Contains(new Vector2Int(cx + spacing / 2, cy + spacing / 2))))
                        {
                            continue;
                        }

                        if (Pick(chunk, cx, cy, spacing, doors, out Vector3 foot, out Quaternion facing))
                        {
                            Place(kit.StreetLamp, foot, facing, Holder());
                        }
                    }
                }
            }
        }

        /// <summary>The lamp spacing (tiles) where a tile lies: a city's, a village's (any other named town), or the wilds'.</summary>
        private static int SpacingAt(int facet, int x, int y, RoomLightKit kit)
        {
            string place = HousingRules.PlaceName(facet, x, y);
            if (place == null)
            {
                return kit.WildSpacing;
            }

            return System.Array.IndexOf(kit.Cities, place) >= 0 ? kit.CitySpacing : kit.VillageSpacing;
        }

        /// <summary>
        /// The square's lamp: on a road tile at the road's edge, standing at the verge with its lantern out over the road;
        /// clear ground, open sky, off doorways. Of the tiles that will do, the one the map's hash ranks first, kept away
        /// from the square's border (so neighbouring squares' lamps are never side by side).
        /// </summary>
        private static bool Pick(WorldBaker.ChunkSeed chunk, int cx, int cy, int spacing, List<Vector3> doors, out Vector3 foot, out Quaternion facing)
        {
            foot = default;
            facing = default;
            int margin = Mathf.Max(1, spacing / 4);
            uint best = uint.MaxValue;
            for (int y = cy + margin; y < cy + spacing - margin; y++)
            {
                for (int x = cx + margin; x < cx + spacing - margin; x++)
                {
                    if (!Road(chunk.Map, x, y) || !Verge(chunk.Map, x, y, out Vector2Int off))
                    {
                        continue;
                    }

                    uint h = Hash(chunk.Facet, x, y);
                    if (h >= best)
                    {
                        continue;
                    }

                    // At the edge of the road, toward the verge; its arm turned back toward the road, the lantern over it.
                    Vector3 centre = WorldSpace.TileCenter(x, y, 0f);
                    var outward = new Vector3(off.x, 0f, -off.y); // tile +y is world -z
                    Vector3 at = centre + outward * (WorldSpace.TileSize * 0.4f);
                    at.y = GroundAt(chunk, at, float.NaN);
                    if (float.IsNaN(at.y) || !Clear(at, -outward, doors))
                    {
                        continue;
                    }

                    best = h;
                    foot = at;
                    facing = Quaternion.LookRotation(-outward);
                }
            }

            return best != uint.MaxValue;
        }

        private static bool Road(MapFacet map, int x, int y) => HousingRules.IsRoad(map.GetLand(x, y).TileId);

        /// <summary>A road tile beside land that isn't road: which way the verge lies (tile steps).</summary>
        private static bool Verge(MapFacet map, int x, int y, out Vector2Int off)
        {
            foreach (Vector2Int d in new[] { new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0) })
            {
                if (!Road(map, x + d.x, y + d.y))
                {
                    off = d;
                    return true;
                }
            }

            off = default;
            return false;
        }

        /// <summary>Room for a lamp post: nothing standing there or where its lantern hangs, open sky, off doorways.</summary>
        private static bool Clear(Vector3 foot, Vector3 overRoad, List<Vector3> doors)
        {
            foreach (Vector3 d in doors)
            {
                if (new Vector2(d.x - foot.x, d.z - foot.z).sqrMagnitude < 3f * 3f)
                {
                    return false;
                }
            }

            return !Physics.CheckCapsule(foot + Vector3.up * 0.4f, foot + Vector3.up * 3.4f, 0.3f, ~0, QueryTriggerInteraction.Ignore) &&
                   !Physics.CheckSphere(foot + overRoad * 0.82f + Vector3.up * 2.5f, 0.35f, ~0, QueryTriggerInteraction.Ignore) &&
                   !Physics.Raycast(foot + Vector3.up * 4f, Vector3.up, 40f, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>The ground (the chunk's terrain, never a roof or a canopy) under a point, or the fallback off it.</summary>
        private static float GroundAt(WorldBaker.ChunkSeed chunk, Vector3 at, float fallback) =>
            chunk.Terrain != null && chunk.Terrain.TryGetComponent(out TerrainCollider ground) &&
            ground.Raycast(new Ray(new Vector3(at.x, 1000f, at.z), Vector3.down), out RaycastHit hit, 2000f)
                ? hit.point.y
                : fallback;

        private static uint Hash(int facet, int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(facet * 83492791);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return h;
            }
        }

        private static Transform MakeHolder(Transform block)
        {
            var go = new GameObject(StreetLampsName);
            go.transform.SetParent(block, false);
            return go.transform;
        }

        private static void Place(GameObject prefab, Vector3 at, Quaternion rotation, Transform parent)
        {
            GameObject go = Object.Instantiate(prefab, at, rotation, parent); // named "...(Clone)": WorldLights lights it by that
            DoorLights.StripColliders(go);
        }

        // ------------------------------------------------------------------ looking about

        /// <summary>Mesh colliders on what's drawn round the chunk that has none (removed again after seeding).</summary>
        private static List<Collider> TemporaryColliders(WorldBaker.ChunkSeed chunk)
        {
            float size = ChunkFormat.ChunkTiles * WorldSpace.TileSize;
            var area = new Bounds(
                new Vector3((chunk.Chunk.x + 0.5f) * size, 0f, -(chunk.Chunk.y + 0.5f) * size),
                new Vector3(size + LookMargin * 2f, 2000f, size + LookMargin * 2f));
            var added = new List<Collider>();
            Transform world = chunk.World != null ? chunk.World : chunk.Blocks[0].transform.root;
            foreach (MeshFilter mf in world.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null || !mf.TryGetComponent(out MeshRenderer r) ||
                    !r.bounds.Intersects(area))
                {
                    continue;
                }

                MeshCollider c = mf.gameObject.AddComponent<MeshCollider>();
                c.sharedMesh = mf.sharedMesh;
                added.Add(c);
            }

            return added;
        }
    }
}
