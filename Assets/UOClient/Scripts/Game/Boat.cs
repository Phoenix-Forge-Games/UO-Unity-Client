using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UOClient;
using UOGame.Content;
using UOGame.Net;

namespace UOGame
{
    /// <summary>
    /// A ship on the water. Its course (where it started, its yaw, speed and turn rate, the server tick it started at)
    /// is synced as one value, and server and clients both place the hull from it every frame (an arc while turning),
    /// so it glides without lag. Those aboard ride it as if parented: <see cref="PlayerMotor"/> moves and turns them by
    /// the deck's own move each tick. Its helmsman steers it freely with the movement keys (BoatService). The hull is
    /// the Synty ship model for its multi (BoatCatalog), scaled to the UO footprint and floating with its deck just
    /// above the water; its colliders are the deck.
    /// </summary>
    public sealed class Boat : NetworkBehaviour
    {
        public readonly SyncVar<int> Kind = new SyncVar<int>(-1);
        public readonly SyncVar<int> BoatId = new SyncVar<int>();
        public readonly SyncVar<int> OwnerId = new SyncVar<int>();
        public readonly SyncVar<string> ShipName = new SyncVar<string>();
        public readonly SyncVar<string> OwnerName = new SyncVar<string>();
        public readonly SyncVar<BoatCourse> Course = new SyncVar<BoatCourse>();
        /// <summary>Who is at the helm: their character id + 1 (0: nobody).</summary>
        public readonly SyncVar<int> Helm = new SyncVar<int>();

        /// <summary>Every ship spawned here (server and client).</summary>
        public static readonly List<Boat> All = new List<Boat>();

        /// <summary>The ship models (set by BoatService on both sides).</summary>
        public static BoatCatalog Catalog;

        private GameObject _model;
        private int _builtKind = -1;

        public BoatRules.BoatDef Def => BoatRules.Get(Kind.Value);
        /// <summary>The nearest quarter the bow points to (0 north, 1 east, 2 south, 3 west).</summary>
        public int Heading => ((Mathf.RoundToInt(Yaw / 90f) % 4) + 4) % 4;
        public float Yaw => PoseAt(Now).Yaw;
        public bool Moving => Course.Value.Speed != 0f || Course.Value.Turn != 0f;
        /// <summary>The ship's own velocity now (m/s, along its bow).</summary>
        public Vector3 Velocity => Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward * Course.Value.Speed;

        /// <summary>The server tick now, fractional (clients: their estimate of the server's).</summary>
        public double Now => TimeManager == null ? 0 : TimeManager.Tick + 0.0;

        /// <summary>Where the ship is and how it's turned at a (fractional) server tick, by its course: straight
        /// along the bow, or an arc while turning. The same sum on server and client.</summary>
        public (Vector3 Position, float Yaw) PoseAt(double tick)
        {
            BoatCourse c = Course.Value;
            if ((c.Speed == 0f && c.Turn == 0f) || TimeManager == null)
            {
                return (c.Origin, c.Yaw);
            }

            // Before the course began too (extrapolated back), so a tick's carry across a fresh course is still a whole tick.
            double t = (tick - c.Tick) * TimeManager.TickDelta;
            float yaw = c.Yaw + c.Turn * (float)t;
            double y0 = c.Yaw * Mathf.Deg2Rad, w = c.Turn * Mathf.Deg2Rad;
            Vector3 p;
            if (Math.Abs(w) < 1e-5)
            {
                p = c.Origin + new Vector3((float)Math.Sin(y0), 0f, (float)Math.Cos(y0)) * (float)(c.Speed * t);
            }
            else
            {
                double r = c.Speed / w, y1 = y0 + w * t;
                p = c.Origin + new Vector3((float)(r * (Math.Cos(y0) - Math.Cos(y1))), 0f, (float)(r * (Math.Sin(y1) - Math.Sin(y0))));
            }

            return (p, yaw);
        }

        /// <summary>Where the ship is now by its course.</summary>
        public Vector3 CoursePosition() => PoseAt(Now).Position;

        /// <summary>Moves a point riding the deck from one tick to the next (as if parented to the ship): where it is
        /// now and how much it turned.</summary>
        public (Vector3 Position, float Turned) Carry(Vector3 point, double fromTick, double toTick)
        {
            (Vector3 p0, float y0) = PoseAt(fromTick);
            (Vector3 p1, float y1) = PoseAt(toTick);
            Vector3 local = Quaternion.Euler(0f, -y0, 0f) * (point - p0);
            return (p1 + Quaternion.Euler(0f, y1, 0f) * local, y1 - y0);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            All.Add(this);
            Build();
            Place();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            All.Remove(this);
        }

        private void Update()
        {
            if (_builtKind != Kind.Value)
            {
                Build();
            }

            Place();
        }

        private void Place()
        {
            (Vector3 p, float yaw) = PoseAt(Now);
            transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>The hull: the catalog's ship for this kind, scaled so its length is the footprint's, centred on the
        /// footprint, sitting its draft into the water. Its colliders are the deck (a kinematic body moves them).</summary>
        private void Build()
        {
            BoatRules.BoatDef def = Def;
            if (def == null || Catalog == null)
            {
                return;
            }

            BoatCatalog.Entry entry = Catalog.Find(def.NorthId, out _);
            if (entry == null)
            {
                return;
            }

            if (_model != null)
            {
                Destroy(_model);
            }

            _builtKind = Kind.Value;
            _model = MakeHull(def, transform);
            if (!TryGetComponent(out Rigidbody body))
            {
                body = gameObject.AddComponent<Rigidbody>();
            }

            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
        }

        /// <summary>How far the deck sits above the water.</summary>
        public const float FloatAbove = 0.15f;

        /// <summary>A ship's hull model under a parent (the ship, or a placement ghost): the catalog's model, scaled so its
        /// length is the footprint's, centred on the footprint, its draft under the parent's height (the water).</summary>
        public static GameObject MakeHull(BoatRules.BoatDef def, Transform parent)
        {
            BoatCatalog.Entry entry = Catalog != null && def != null ? Catalog.Find(def.NorthId, out _) : null;
            if (entry == null)
            {
                return null;
            }

            GameObject model = Instantiate(entry.Prefab, parent);
            model.name = "Hull";
            RectInt north = def.Bounds[0];
            float length = def.Length * WorldSpace.TileSize;
            float modelLength = LengthOf(model);
            model.transform.localScale = Vector3.one * (modelLength > 0.1f ? length / modelLength : 1f);
            float cx = (north.xMin + north.xMax) * 0.5f, cy = (north.yMin + north.yMax) * 0.5f;
            model.transform.localPosition = new Vector3(cx * WorldSpace.TileSize, 0f, -cy * WorldSpace.TileSize);
            model.transform.localRotation = Quaternion.identity;

            // Float: the inside floor (the lowest surface at the middle, seen from above) just above the water, so no
            // water shows inside the boat.
            Physics.SyncTransforms();
            Vector3 middle = model.transform.TransformPoint(Vector3.zero);
            float floor = float.NaN;
            foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(parent.position.x, parent.position.y + 60f, parent.position.z) +
                                                          (middle - parent.position).x * Vector3.right + (middle - parent.position).z * Vector3.forward,
                         Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(model.transform) && (float.IsNaN(floor) || hit.point.y < floor))
                {
                    floor = hit.point.y; // the lowest surface seen from above at the middle: the inside floor (not a bench)
                }
            }

            float lift = float.IsNaN(floor) ? -entry.Draft * length : FloatAbove - (floor - parent.position.y);
            model.transform.localPosition += Vector3.up * lift;
            return model;
        }

        // ------------------------------------------------------------------ the water (the same checks on server and client)

        /// <summary>Is the footprint at this centre tile and heading all water, clear of other ships?</summary>
        public static bool Fits(BoatRules.BoatDef def, Vector2Int centre, int heading, Boat ignore)
        {
            if (def == null)
            {
                return false;
            }

            RectInt r = def.Bounds[((heading % 4) + 4) % 4];
            for (int x = r.xMin; x <= r.xMax; x++)
            {
                for (int y = r.yMin; y <= r.yMax; y++)
                {
                    if (!WaterMap.TryGet(WorldSpace.TileCenter(centre.x + x, centre.y + y, 0), out _))
                    {
                        return false;
                    }

                    foreach (Boat other in All)
                    {
                        if (other != null && other != ignore && other.Covers(centre.x + x, centre.y + y))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>Does this ship's hull stand on that tile now (any angle)?</summary>
        public bool Covers(int x, int y) => Covers(WorldSpace.TileCenter(x, y, 0), CoursePosition(), Yaw);

        private bool Covers(Vector3 point, Vector3 at, float yaw)
        {
            BoatRules.BoatDef def = Def;
            if (def == null)
            {
                return false;
            }

            Vector3 local = Quaternion.Euler(0f, -yaw, 0f) * (point - at);
            float tx = local.x / WorldSpace.TileSize, ty = -local.z / WorldSpace.TileSize;
            RectInt r = def.Bounds[0];
            return tx >= r.xMin - 0.5f && tx <= r.xMax + 0.5f && ty >= r.yMin - 0.5f && ty <= r.yMax + 0.5f;
        }

        /// <summary>Server: is the hull all on water, clear of other ships, at this place and yaw? (Samples across the
        /// hull a tile apart.)</summary>
        public bool HullClear(Vector3 at, float yaw)
        {
            BoatRules.BoatDef def = Def;
            if (def == null)
            {
                return false;
            }

            RectInt r = def.Bounds[0];
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            for (int x = r.xMin; x <= r.xMax; x++)
            {
                for (int y = r.yMin; y <= r.yMax; y++)
                {
                    Vector3 p = at + turn * new Vector3(x * WorldSpace.TileSize, 0f, -y * WorldSpace.TileSize);
                    if (!WaterMap.TryGet(p, out _))
                    {
                        return false;
                    }

                    foreach (Boat other in All)
                    {
                        if (other != null && other != this && other.Covers(p, other.CoursePosition(), other.Yaw))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>Where a new ship may go (the placement ghost and the server): all on water, dry land within
        /// CoastReach tiles of the hull (near the coast), its centre within PlaceReach tiles of you. Null if it may.</summary>
        public static string CheckPlace(BoatRules.BoatDef def, Vector2Int centre, int heading, Vector3 placer)
        {
            if (def == null)
            {
                return "That isn't a ship.";
            }

            Vector2Int me = WorldSpace.ToTile(placer);
            if (Mathf.Max(Mathf.Abs(me.x - centre.x), Mathf.Abs(me.y - centre.y)) > BoatRules.PlaceReach)
            {
                return "That is too far away.";
            }

            if (!Fits(def, centre, heading, null))
            {
                return "The ship needs open water all around it.";
            }

            RectInt r = def.Bounds[((heading % 4) + 4) % 4];
            int reach = BoatRules.CoastReach;
            for (int x = r.xMin - reach; x <= r.xMax + reach; x++)
            {
                for (int y = r.yMin - reach; y <= r.yMax + reach; y++)
                {
                    if (!WaterMap.TryGet(WorldSpace.TileCenter(centre.x + x, centre.y + y, 0), out _))
                    {
                        return null; // dry land close by: near the coast
                    }
                }
            }

            return "The ship must be placed near the coast.";
        }

        private static float LengthOf(GameObject model)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float z = model.transform.InverseTransformPoint(filter.transform.TransformPoint(corner)).z;
                    min = Mathf.Min(min, z);
                    max = Mathf.Max(max, z);
                }
            }

            return max > min ? max - min : 0f;
        }

        /// <summary>The ship whose deck is under this point (within a couple of metres), or null.</summary>
        public static Boat Under(Vector3 position)
        {
            if (All.Count == 0)
            {
                return null;
            }

            foreach (RaycastHit hit in Physics.RaycastAll(position + Vector3.up * 0.6f, Vector3.down, 2.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                Boat boat = hit.collider.GetComponentInParent<Boat>();
                if (boat != null)
                {
                    return boat;
                }
            }

            return null;
        }

        /// <summary>The nearest ship within a distance of a point, or null.</summary>
        public static Boat Near(Vector3 position, float metres)
        {
            Boat best = null;
            float bestDistance = float.MaxValue;
            foreach (Boat b in All)
            {
                if (b == null || b.Def == null)
                {
                    continue;
                }

                // Distance to the hull's footprint, not its centre.
                Vector3 local = b.transform.InverseTransformPoint(position);
                RectInt r = b.Def.Bounds[0];
                float dx = Mathf.Max(0f, Mathf.Abs(local.x / WorldSpace.TileSize - (r.xMin + r.xMax) * 0.5f) - r.width * 0.5f);
                float dz = Mathf.Max(0f, Mathf.Abs(-local.z / WorldSpace.TileSize - (r.yMin + r.yMax) * 0.5f) - r.height * 0.5f);
                float d = new Vector2(dx, dz).magnitude * WorldSpace.TileSize;
                if (d <= metres && d < bestDistance)
                {
                    best = b;
                    bestDistance = d;
                }
            }

            return best;
        }
    }
}
