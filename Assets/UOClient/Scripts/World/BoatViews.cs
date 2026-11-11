using System.Collections.Generic;
using UnityEngine;
using UOClient.Data;

namespace UOClient
{
    /// <summary>
    /// Boats: a ship model per boat multi (BoatCatalog), scaled to the boat's footprint, turned to its heading,
    /// sitting in the water at the boat's z, gliding to its new position when it sails. The boat's own tiles aren't
    /// drawn (no UO art); they stay as walkable deck surfaces (<see cref="TilesAt"/>) for the player's movement.
    /// </summary>
    public sealed class BoatViews
    {
        private const float GlideMetresPerSecond = 6f;

        private sealed class Boat
        {
            public GameObject Model;
            public Vector3 Target;
            public Quaternion Facing;
            public readonly List<(int X, int Y, ushort Graphic, int Z)> Deck = new List<(int, int, ushort, int)>();
        }

        private readonly Transform _root;
        private readonly BoatCatalog _catalog;
        private readonly Dictionary<uint, Boat> _boats = new Dictionary<uint, Boat>();
        private readonly Dictionary<(int X, int Y), List<(ushort Graphic, int Z)>> _deck = new Dictionary<(int, int), List<(ushort, int)>>();
        private readonly Dictionary<GameObject, float> _lengths = new Dictionary<GameObject, float>();

        public BoatViews(Transform root, BoatCatalog catalog)
        {
            _root = root;
            _catalog = catalog;
        }

        public bool IsBoat(int multiId) => _catalog != null && _catalog.Find(multiId, out _) != null;

        /// <summary>A boat appeared, turned or moved.</summary>
        public void Place(uint serial, int multiId, int x, int y, int z, MultiTile[] tiles)
        {
            BoatCatalog.Entry entry = _catalog.Find(multiId, out int heading);
            if (entry == null)
            {
                return;
            }

            if (!_boats.TryGetValue(serial, out Boat boat))
            {
                boat = new Boat { Model = Object.Instantiate(entry.Prefab, _root) };
                boat.Model.name = $"Boat 0x{serial:X8} ({entry.Name})";
                foreach (Collider c in boat.Model.GetComponentsInChildren<Collider>(true))
                {
                    Object.Destroy(c);
                }

                _boats[serial] = boat;
            }

            // Footprint along and across the heading, in tiles.
            int minX = 0, maxX = 0, minY = 0, maxY = 0;
            foreach (MultiTile t in tiles)
            {
                minX = Mathf.Min(minX, t.X);
                maxX = Mathf.Max(maxX, t.X);
                minY = Mathf.Min(minY, t.Y);
                maxY = Mathf.Max(maxY, t.Y);
            }

            bool northSouth = heading == 0 || heading == 2;
            float length = ((northSouth ? maxY - minY : maxX - minX) + 1) * WorldSpace.TileSize;
            float modelLength = LengthOf(entry.Prefab);
            float scale = modelLength > 0.1f ? length / modelLength : 1f;
            boat.Model.transform.localScale = Vector3.one * scale;

            // Centre of the footprint; north is +Z, so headings turn by 90° (bow along the model's +Z).
            float cx = x + (minX + maxX) * 0.5f, cy = y + (minY + maxY) * 0.5f;
            float water = z * WorldSpace.ZScale;
            boat.Target = new Vector3(WorldSpace.X(cx + 0.5f), water - entry.Draft * length, WorldSpace.Z(cy + 0.5f));
            boat.Facing = Quaternion.Euler(0f, heading * 90f, 0f);
            if (boat.Model.transform.position == Vector3.zero || (boat.Model.transform.position - boat.Target).sqrMagnitude > 400f)
            {
                boat.Model.transform.SetPositionAndRotation(boat.Target, boat.Facing);
            }

            // Walkable deck: the boat's tiles at their place in the world.
            ClearDeck(boat);
            foreach (MultiTile t in tiles)
            {
                boat.Deck.Add((x + t.X, y + t.Y, t.Graphic, z + t.Z));
            }

            foreach ((int dx, int dy, ushort g, int dz) in boat.Deck)
            {
                if (!_deck.TryGetValue((dx, dy), out List<(ushort, int)> list))
                {
                    _deck[(dx, dy)] = list = new List<(ushort, int)>();
                }

                list.Add((g, dz));
            }
        }

        public bool Remove(uint serial)
        {
            if (!_boats.TryGetValue(serial, out Boat boat))
            {
                return false;
            }

            ClearDeck(boat);
            Object.Destroy(boat.Model);
            _boats.Remove(serial);
            return true;
        }

        /// <summary>Deck tiles at a map tile, for the walker's surfaces.</summary>
        public IEnumerable<(ushort Graphic, int Z)> TilesAt(int x, int y) =>
            _deck.TryGetValue((x, y), out List<(ushort, int)> list) ? list : (IEnumerable<(ushort, int)>)System.Array.Empty<(ushort, int)>();

        public void Update()
        {
            float step = GlideMetresPerSecond * Time.deltaTime;
            foreach (Boat boat in _boats.Values)
            {
                Transform t = boat.Model.transform;
                t.SetPositionAndRotation(Vector3.MoveTowards(t.position, boat.Target, step),
                    Quaternion.RotateTowards(t.rotation, boat.Facing, 45f * Time.deltaTime));
            }
        }

        public void Clear()
        {
            foreach (Boat boat in _boats.Values)
            {
                Object.Destroy(boat.Model);
            }

            _boats.Clear();
            _deck.Clear();
        }

        private void ClearDeck(Boat boat)
        {
            foreach ((int x, int y, ushort g, int z) in boat.Deck)
            {
                if (_deck.TryGetValue((x, y), out List<(ushort, int)> list))
                {
                    list.Remove((g, z));
                    if (list.Count == 0)
                    {
                        _deck.Remove((x, y));
                    }
                }
            }

            boat.Deck.Clear();
        }

        /// <summary>A ship prefab's length (its meshes along +Z), measured once.</summary>
        private float LengthOf(GameObject prefab)
        {
            if (_lengths.TryGetValue(prefab, out float length))
            {
                return length;
            }

            float min = float.MaxValue, max = float.MinValue;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float z = prefab.transform.InverseTransformPoint(filter.transform.TransformPoint(corner)).z;
                    min = Mathf.Min(min, z);
                    max = Mathf.Max(max, z);
                }
            }

            length = max > min ? max - min : 0f;
            _lengths[prefab] = length;
            return length;
        }
    }
}
