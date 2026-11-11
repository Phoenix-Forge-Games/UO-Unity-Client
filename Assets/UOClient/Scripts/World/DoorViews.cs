using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Buildings;
using UOClient.Data;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// Shows server door items. Each is bound to the nearest door of a generated house and swings it open
    /// or shut as the server's graphic says; a door item with no generated door near it (UO's interior
    /// doors, gates, dungeon doors) gets a kit door leaf hung on the wall edge its art stands on.
    /// </summary>
    public sealed class DoorViews
    {
        /// <summary>How far (metres, across the ground) a door item may be from the generated door it drives.</summary>
        private const float BindReach = 2.6f * WorldSpace.TileScale; // about two tiles
        private const float BindHeight = 2.5f;

        /// <summary>
        /// Kit leaf hung in UO doorways: a plain rectangular door (1 m by 1.97 m, hinge at its pivot) stretched
        /// to fill the 1.25 m by 2.5 m opening exactly. Arched leaves left the opening's corners see-through.
        /// </summary>
        private const string LeafPrefab = "SM_Bld_Base_Door_01";
        private const float LeafHeight = 2.5f;

        private readonly DoorTracker _tracker;
        private readonly WallShapes _walls;
        private readonly Transform _root;
        private readonly Dictionary<uint, DoorLeafSwing> _leaves = new Dictionary<uint, DoorLeafSwing>();
        private readonly Dictionary<(int X, int Y), DoorLeafSwing> _doorways = new Dictionary<(int X, int Y), DoorLeafSwing>();
        private readonly Dictionary<uint, (int X, int Y, int Z, ushort Graphic)> _closed = new Dictionary<uint, (int, int, int, ushort)>();
        private GameObject _leafPrefab;
        private readonly Dictionary<uint, WorldItem> _items = new Dictionary<uint, WorldItem>();
        private readonly Dictionary<uint, BuildingDoor> _bound = new Dictionary<uint, BuildingDoor>();
        private readonly List<BuildingDoor> _doors = new List<BuildingDoor>();

        public DoorViews(DoorTracker tracker, WallShapes walls, Transform root)
        {
            _tracker = tracker;
            _walls = walls;
            _root = root;
        }

        /// <summary>Raised when a door item becomes bound after the fact, so its placeholder can go.</summary>
        public event Action<uint> ItemBound;

        /// <summary>
        /// Takes a door item update. Returns true when a generated door shows it (no placeholder needed).
        /// </summary>
        public bool Handle(WorldItem item)
        {
            if (!_tracker.IsDoor(item.Graphic))
            {
                return false;
            }

            _items[item.Serial] = item;
            if (!_tracker.IsOpen(item.Graphic))
            {
                _closed[item.Serial] = (item.X, item.Y, item.Z, item.Graphic);
            }

            bool wasBound = _bound.TryGetValue(item.Serial, out BuildingDoor before) && before != null;
            BuildingDoor door = DoorFor(item);
            if (door == null)
            {
                return ShowLeaf(item);
            }

            Apply(door);
            if (!wasBound)
            {
                BindPartners(item, door);
            }

            return true;
        }

        /// <summary>
        /// The door item left the client's update range: keep its leaf, shut, so the doorway isn't left
        /// empty in the distance. A later update for the same serial takes it over again.
        /// </summary>
        public void KeepClosed(uint serial)
        {
            if (_leaves.TryGetValue(serial, out DoorLeafSwing leaf) && leaf != null)
            {
                leaf.SetOpen(false);
            }

            if (_bound.TryGetValue(serial, out BuildingDoor door) && door != null && _items.TryGetValue(serial, out WorldItem item))
            {
                _items.Remove(serial);
                Apply(door);
                _items[serial] = item;
            }
        }

        public void Remove(uint serial)
        {
            _items.Remove(serial);
            _closed.Remove(serial);
            DropLeaf(serial);
            if (_bound.TryGetValue(serial, out BuildingDoor door))
            {
                _bound.Remove(serial);
                if (door != null)
                {
                    Apply(door);
                }
            }
        }

        /// <summary>
        /// Hangs a shut leaf in each of a generated house's doorways (the wall is left open there), so a
        /// doorway the server hasn't sent the door for yet (it only sends items within about 18 tiles)
        /// isn't an empty hole; the door item takes its leaf over when it arrives.
        /// </summary>
        public void AddDoorways(GameObject house, HouseLayout layout)
        {
            var tiles = new List<(int X, int Y, bool East)>();
            foreach (HouseOpening door in layout.Doors)
            {
                switch (door.Side)
                {
                    case TileSide.East: tiles.Add((door.X, door.Y, true)); break;
                    case TileSide.West: tiles.Add((door.X - 1, door.Y, true)); break;
                    case TileSide.South: tiles.Add((door.X, door.Y, false)); break;
                    default: tiles.Add((door.X, door.Y - 1, false)); break;
                }
            }

            foreach ((int x, int y, bool east) in tiles)
            {
                if (_doorways.ContainsKey((x, y)) || LeafAt(x, y))
                {
                    continue;
                }

                bool pair = tiles.Contains(east ? (x, y + 1, true) : (x + 1, y, false));
                DoorLeafSwing leaf = HangLeaf(x, y, layout.FloorZ, east, pair, 0, house.transform);
                if (leaf != null)
                {
                    leaf.SetOpen(false);
                    _doorways[(x, y)] = leaf;
                }
            }
        }

        private bool LeafAt(int x, int y)
        {
            foreach (DoorLeafSwing leaf in _leaves.Values)
            {
                if (leaf != null && leaf.Tile.X == x && leaf.Tile.Y == y)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Takes the doors of a newly built house and binds any door items waiting near them.</summary>
        public void AddHouse(GameObject house)
        {
            _doors.RemoveAll(d => d == null);
            foreach (BuildingDoor door in house.GetComponentsInChildren<BuildingDoor>(true))
            {
                _doors.Add(door);
            }

            foreach (WorldItem item in _items.Values)
            {
                if (_bound.TryGetValue(item.Serial, out BuildingDoor bound) && bound != null)
                {
                    continue;
                }

                BuildingDoor door = DoorFor(item);
                if (door != null)
                {
                    Apply(door);
                    ItemBound?.Invoke(item.Serial);
                }
            }
        }

        /// <summary>Is this door item shown by a generated door?</summary>
        public bool IsBound(uint serial) => _bound.TryGetValue(serial, out BuildingDoor door) && door != null;

        public void Clear()
        {
            foreach (DoorLeafSwing leaf in _leaves.Values)
            {
                if (leaf != null)
                {
                    UnityEngine.Object.Destroy(leaf.gameObject);
                }
            }

            _leaves.Clear();
            _closed.Clear();
            _items.Clear();
            _bound.Clear();
            _doors.Clear();
        }

        private BuildingDoor DoorFor(WorldItem item)
        {
            if (_bound.TryGetValue(item.Serial, out BuildingDoor bound) && bound != null)
            {
                return bound;
            }

            // An open door has been nudged a tile aside, so measure from where it stands either way.
            Vector3 at = WorldSpace.TileCenter(item.X, item.Y, item.Z);
            BuildingDoor best = null;
            float bestDistance = BindReach;
            foreach (BuildingDoor door in _doors)
            {
                if (door == null)
                {
                    continue;
                }

                Vector3 p = door.transform.position;
                float across = new Vector2(p.x - at.x, p.z - at.z).magnitude;
                if (across < bestDistance && Mathf.Abs(p.y - at.y) < BindHeight)
                {
                    bestDistance = across;
                    best = door;
                }
            }

            if (best == null)
            {
                best = PartnerDoor(item);
            }

            if (best != null)
            {
                _bound[item.Serial] = best;
                DropLeaf(item.Serial);
            }

            return best;
        }

        /// <summary>
        /// Hangs (or updates) a kit leaf for a door no generated house shows. The leaf stays where the door
        /// stands closed (the server nudges an open door's item aside) and swings a quarter turn when open.
        /// </summary>
        private bool ShowLeaf(WorldItem item)
        {
            if (!_leaves.TryGetValue(item.Serial, out DoorLeafSwing leaf) || leaf == null)
            {
                (int x, int y, int z, ushort graphic) = _closed.TryGetValue(item.Serial, out var c)
                    ? c
                    : (item.X, item.Y, item.Z, (ushort)(item.Graphic - 1)); // first seen open: its closed art
                if (_doorways.TryGetValue((x, y), out leaf) && leaf != null)
                {
                    // The house hung this doorway's leaf before the server sent the door; take it over.
                    _doorways.Remove((x, y));
                    leaf.Serial = item.Serial;
                }
                else
                {
                    leaf = CreateLeaf(x, y, z, graphic, item.Serial);
                    if (leaf == null)
                    {
                        return false;
                    }
                }

                _leaves[item.Serial] = leaf;
                RehangPartner(leaf);
            }

            leaf.SetOpen(_tracker.IsOpen(item.Graphic));
            return true;
        }

        private DoorLeafSwing CreateLeaf(int x, int y, int z, ushort closedGraphic, uint serial)
        {
            if (_leafPrefab == null)
            {
                _leafPrefab = Buildings.Synty.SyntyAssets.Prefab(LeafPrefab);
            }

            WallEdges edges = _walls != null ? _walls.Get(closedGraphic) : WallEdges.None;
            if (_leafPrefab == null || (edges & (WallEdges.South | WallEdges.East)) == 0)
            {
                return null;
            }

            bool onEast = (edges & WallEdges.East) != 0 && (edges & WallEdges.South) == 0;
            bool pair = onEast ? DoorClosedAt(x, y + 1, z, serial) : DoorClosedAt(x + 1, y, z, serial);
            return HangLeaf(x, y, z, onEast, pair, serial, _root);
        }

        /// <summary>Hangs a kit leaf on the east or south edge of tile (x, y), hinged as described below.</summary>
        private DoorLeafSwing HangLeaf(int x, int y, int z, bool east, bool mirrored, uint serial, Transform parent)
        {
            if (_leafPrefab == null)
            {
                _leafPrefab = Buildings.Synty.SyntyAssets.Prefab(LeafPrefab);
                if (_leafPrefab == null)
                {
                    return null;
                }
            }

            // A single leaf hinges at the tile's south-east corner, lying along its south edge (yaw 0, leaf
            // toward -x) or its east edge (yaw 90, leaf toward +z, north). The first leaf of a double door
            // (the west or north one, with its partner on the next tile along the wall) is mirrored to hinge
            // at its outer end, so the pair opens from the middle; both swing to the same side of the wall.
            var hinge = new GameObject($"Door leaf {x},{y}");
            hinge.transform.SetParent(parent, false);
            float hx = WorldSpace.X(mirrored && !east ? x : x + 1);
            float hz = WorldSpace.Z(mirrored && east ? y : y + 1);
            hinge.transform.position = new Vector3(hx, z * WorldSpace.ZScale, hz);
            GameObject leaf = UnityEngine.Object.Instantiate(_leafPrefab, hinge.transform, false);
            Bounds lb = LeafBounds();
            leaf.transform.localScale = new Vector3(
                WorldSpace.TileSize / Mathf.Max(lb.size.x, 0.01f),
                LeafHeight / Mathf.Max(lb.size.y, 0.01f),
                1f);
            foreach (Collider collider in leaf.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.Destroy(collider);
            }

            int layer = LayerMask.NameToLayer(HouseBuilder.LayerName);
            if (layer >= 0)
            {
                foreach (Transform t in hinge.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = layer;
                }
            }

            var swing = hinge.AddComponent<DoorLeafSwing>();
            swing.ClosedYaw = (east ? 90f : 0f) + (mirrored ? 180f : 0f);
            swing.OpenTurn = mirrored ? 90f : -90f;
            swing.Mirrored = mirrored;
            swing.Serial = serial;
            swing.Tile = (x, y, z, east);
            return swing;
        }

        /// <summary>Is a (different) door item standing closed on this tile?</summary>
        private bool DoorClosedAt(int x, int y, int z, uint except)
        {
            foreach (KeyValuePair<uint, (int X, int Y, int Z, ushort Graphic)> pair in _closed)
            {
                if (pair.Key != except && pair.Value.X == x && pair.Value.Y == y && Mathf.Abs(pair.Value.Z - z) <= 5)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A new leaf may be the second half of a double door whose first half was hung before it was
        /// known to be one; hang that first half again, mirrored.
        /// </summary>
        private void RehangPartner(DoorLeafSwing leaf)
        {
            (int x, int y, int z, bool east) = leaf.Tile;
            int px = east ? x : x - 1, py = east ? y - 1 : y;
            foreach (KeyValuePair<uint, DoorLeafSwing> pair in _leaves)
            {
                DoorLeafSwing other = pair.Value;
                if (other == null || other == leaf || other.Mirrored || other.Tile.X != px || other.Tile.Y != py ||
                    other.Tile.East != east || !_closed.TryGetValue(pair.Key, out var closed) || !_items.TryGetValue(pair.Key, out WorldItem item))
                {
                    continue;
                }

                // Not while enumerating _leaves: the rehung leaf replaces this entry.
                UnityEngine.Object.Destroy(other.gameObject);
                uint serial = pair.Key;
                DoorLeafSwing rehung = CreateLeaf(closed.X, closed.Y, closed.Z, closed.Graphic, serial);
                bool open = _tracker.IsOpen(item.Graphic);
                _pendingRehang = (serial, rehung, open);
                break;
            }

            if (_pendingRehang.Leaf != null || _pendingRehang.Serial != 0)
            {
                _leaves[_pendingRehang.Serial] = _pendingRehang.Leaf;
                _pendingRehang.Leaf?.SetOpen(_pendingRehang.Open);
                _pendingRehang = default;
            }
        }

        private (uint Serial, DoorLeafSwing Leaf, bool Open) _pendingRehang;

        private Bounds? _leafBounds;

        /// <summary>The leaf prefab's mesh bounds in its own space.</summary>
        private Bounds LeafBounds()
        {
            if (_leafBounds.HasValue)
            {
                return _leafBounds.Value;
            }

            Bounds b = new Bounds(new Vector3(-0.5f, 1f, 0f), new Vector3(1f, 2f, 0.1f));
            bool any = false;
            Matrix4x4 toRoot = _leafPrefab.transform.worldToLocalMatrix;
            foreach (MeshFilter filter in _leafPrefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                Matrix4x4 m = toRoot * filter.transform.localToWorldMatrix;
                Bounds mb = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = m.MultiplyPoint3x4(new Vector3(
                        (i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z));
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

            _leafBounds = b;
            return b;
        }

        private void DropLeaf(uint serial)
        {
            if (_leaves.TryGetValue(serial, out DoorLeafSwing leaf))
            {
                if (leaf != null)
                {
                    UnityEngine.Object.Destroy(leaf.gameObject);
                }

                _leaves.Remove(serial);
            }
        }

        /// <summary>
        /// The other leaf of a double door stands a tile beside its partner (two tiles once both have
        /// swung aside), which can put it beyond <see cref="BindReach"/> of the generated door; it drives
        /// the same door as its partner.
        /// </summary>
        private BuildingDoor PartnerDoor(WorldItem item)
        {
            foreach (KeyValuePair<uint, BuildingDoor> pair in _bound)
            {
                if (pair.Value == null || pair.Key == item.Serial || !_items.TryGetValue(pair.Key, out WorldItem other))
                {
                    continue;
                }

                if (Mathf.Abs(other.Z - item.Z) <= 5 && Mathf.Abs(other.X - item.X) + Mathf.Abs(other.Y - item.Y) <= 3)
                {
                    return pair.Value;
                }
            }

            return null;
        }

        /// <summary>Binds door items waiting beside a door that just found its generated door.</summary>
        private void BindPartners(WorldItem item, BuildingDoor door)
        {
            var waiting = new List<uint>();
            foreach (WorldItem other in _items.Values)
            {
                if (other.Serial == item.Serial || (_bound.TryGetValue(other.Serial, out BuildingDoor b) && b != null))
                {
                    continue;
                }

                if (Mathf.Abs(other.Z - item.Z) <= 5 && Mathf.Abs(other.X - item.X) + Mathf.Abs(other.Y - item.Y) <= 3)
                {
                    waiting.Add(other.Serial);
                }
            }

            foreach (uint serial in waiting)
            {
                _bound[serial] = door;
                ItemBound?.Invoke(serial);
            }

            if (waiting.Count > 0)
            {
                Apply(door);
            }
        }

        /// <summary>A generated door stands open while any door item bound to it is open (double doors).</summary>
        private void Apply(BuildingDoor door)
        {
            bool open = false;
            foreach (KeyValuePair<uint, BuildingDoor> pair in _bound)
            {
                if (pair.Value == door && _items.TryGetValue(pair.Key, out WorldItem item) && _tracker.IsOpen(item.Graphic))
                {
                    open = true;
                    break;
                }
            }

            door.Lock = null; // the server decides who gets in
            door.SetOpen(open);
        }
    }

    /// <summary>Swings a kit door leaf a quarter turn about its hinge (this transform) to open it.</summary>
    public sealed class DoorLeafSwing : MonoBehaviour
    {
        private const float SwingSeconds = 0.6f;

        public float ClosedYaw;
        /// <summary>Quarter turn that opens the leaf: -90 for a plain leaf, +90 for a mirrored one.</summary>
        public float OpenTurn = -90f;
        public bool Mirrored;
        public uint Serial;
        public (int X, int Y, int Z, bool East) Tile;
        private float _from, _to, _t = 1f;
        private bool _open;
        private bool _placed;

        public void SetOpen(bool open)
        {
            float target = ClosedYaw + (open ? OpenTurn : 0f);
            if (!_placed)
            {
                _placed = true;
                _open = open;
                _to = target;
                transform.rotation = Quaternion.Euler(0f, target, 0f);
                return;
            }

            if (open == _open)
            {
                return;
            }

            _open = open;
            _from = transform.eulerAngles.y;
            _to = target;
            _t = 0f;
            enabled = true;
        }

        private void Update()
        {
            if (_t >= 1f)
            {
                enabled = false;
                return;
            }

            _t = Mathf.Min(1f, _t + Time.deltaTime / SwingSeconds);
            float e = 1f - (1f - _t) * (1f - _t);
            transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(_from, _to, e), 0f);
        }
    }
}
