using System.Collections.Generic;
using UnityEngine;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// Creates, updates and removes the GameObjects for mobiles and ground items as the
    /// <see cref="WorldState"/> changes. Humanoids get one of the Sidekick prefabs (picked
    /// stably per serial); everything else gets a notoriety-colored capsule for now.
    /// </summary>
    public sealed class EntityViews
    {
        private readonly Transform _root;
        private readonly GameObject[] _humanoidPrefabs;
        private readonly RuntimeAnimatorController _humanoidAnimator;
        private readonly Material _material;
        private readonly Dictionary<uint, MobileView> _mobiles = new Dictionary<uint, MobileView>();
        private readonly Dictionary<uint, GameObject> _items = new Dictionary<uint, GameObject>();

        public EntityViews(
            Transform root,
            GameObject[] humanoidPrefabs,
            RuntimeAnimatorController humanoidAnimator,
            Material material
        )
        {
            _root = root;
            _humanoidPrefabs = humanoidPrefabs;
            _humanoidAnimator = humanoidAnimator;
            _material = material;
        }

        /// <summary>Optional: doors shown by generated houses instead of placeholders.</summary>
        public DoorViews Doors
        {
            get => _doors;
            set
            {
                if (_doors != null)
                {
                    _doors.ItemBound -= DropItemView;
                }

                _doors = value;
                if (_doors != null)
                {
                    _doors.ItemBound += DropItemView;
                }
            }
        }

        private DoorViews _doors;
        private readonly HashSet<uint> _propItems = new HashSet<uint>();

        private void DropItemView(uint serial)
        {
            if (_items.TryGetValue(serial, out GameObject go))
            {
                Object.Destroy(go);
                _items.Remove(serial);
            }

            _propItems.Remove(serial);
        }

        /// <summary>Optional: Synty props for furniture and clutter items instead of placeholder cubes.</summary>
        public PropPlacer Props { get; set; }

        /// <summary>Optional: tile heights for sizing placeholder cubes.</summary>
        public UOClient.Data.TileData Tiles { get; set; }

        private float PlaceholderSize(ushort graphic)
        {
            int h = Tiles != null && graphic < Tiles.Statics.Length ? Tiles.GetStatic(graphic).Height : 2;
            return Mathf.Clamp(h * WorldSpace.ZScale, 0.12f, 0.6f);
        }

        /// <summary>Optional: models for creatures and animals by UO body; bodies it lacks stay capsules.</summary>
        public CreatureCatalog Creatures { get; set; }

        /// <summary>Optional: dresses humanoids in Sidekick parts from their equipment, instead of fixed prefabs.</summary>
        public SidekickDresser Dresser { get; set; }

        private readonly Dictionary<uint, string> _outfits = new Dictionary<uint, string>();

        public MobileView Get(uint serial) => _mobiles.TryGetValue(serial, out MobileView view) ? view : null;

        /// <summary>Every mobile's view, for picking and name plates.</summary>
        public IEnumerable<KeyValuePair<uint, MobileView>> Mobiles => _mobiles;

        /// <summary>Every ground item's view (placeholders and props; doors drawn by houses aren't here).</summary>
        public IEnumerable<KeyValuePair<uint, GameObject>> Items => _items;

        public void OnMobileUpdated(Mobile mobile)
        {
            if (!_mobiles.TryGetValue(mobile.Serial, out MobileView view))
            {
                view = Create(mobile, out Animator animator);
                _mobiles[mobile.Serial] = view;
                view.Bind(mobile, animator);
                return;
            }

            if (Dresser != null && mobile.IsHumanoid && _outfits.TryGetValue(mobile.Serial, out string worn) &&
                worn != SidekickDresser.Signature(mobile))
            {
                view.SetAnimator(Dress(mobile, view.transform));
            }

            view.Refresh();
        }

        /// <summary>(Re)builds a humanoid's dressed model under its view.</summary>
        private Animator Dress(Mobile mobile, Transform view)
        {
            for (int i = view.childCount - 1; i >= 0; i--)
            {
                GameObject old = view.GetChild(i).gameObject;
                if (old.name.StartsWith("Sidekick ", System.StringComparison.Ordinal))
                {
                    Object.Destroy(old); // only the old model; anything else riding on the view stays
                }
            }

            GameObject model = Dresser.Build(mobile, view);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            _outfits[mobile.Serial] = SidekickDresser.Signature(mobile);
            return model.GetComponent<Animator>();
        }

        public void OnMobileRemoved(uint serial)
        {
            if (_mobiles.TryGetValue(serial, out MobileView view))
            {
                Object.Destroy(view.gameObject);
                _mobiles.Remove(serial);
            }

            _outfits.Remove(serial);
        }

        public void OnItemUpdated(WorldItem item)
        {
            if (item.IsMulti)
            {
                return; // houses and boats come with the art-mapping milestone
            }

            _ghosts.Remove(item.Serial);
            _itemTiles[item.Serial] = (item.X, item.Y);

            if (_doors != null && _doors.Handle(item))
            {
                DropItemView(item.Serial); // a generated house's door shows it
                return;
            }

            PropCatalog.Entry prop = Props?.EntryFor(item.Graphic);
            if (prop != null)
            {
                // Rebuilt when the item changes graphic or moves; furniture rarely does.
                string key = $"Item 0x{item.Serial:X8} (0x{item.Graphic:X4}) {item.X},{item.Y},{item.Z}";
                if (_items.TryGetValue(item.Serial, out GameObject existing) && existing != null && existing.name == key)
                {
                    return;
                }

                DropItemView(item.Serial);
                GameObject placed = Props.Place(_root, prop, item.Graphic, item.X, item.Y, item.X, item.Y, item.Z, serverItem: true);
                if (placed != null)
                {
                    placed.name = key;
                    placed.AddComponent<Pickable>().Serial = item.Serial;
                    string tileName = Tiles != null && item.Graphic < Tiles.Statics.Length ? (Tiles.GetStatic(item.Graphic).Name ?? string.Empty).ToLowerInvariant() : string.Empty;
                    if (tileName.Contains("post") || tileName.Contains("pole"))
                    {
                        placed.AddComponent<GroundedPole>(); // never floats over lower ground
                    }
                    _items[item.Serial] = placed;
                    _propItems.Add(item.Serial);
                    return;
                }
            }

            if (!_items.TryGetValue(item.Serial, out GameObject go) || _propItems.Contains(item.Serial))
            {
                DropItemView(item.Serial);
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(go.GetComponent<Collider>());
                go.name = $"Item 0x{item.Serial:X8} (0x{item.Graphic:X4})";
                go.transform.SetParent(_root, false);
                // Sized to the item (UO height, kept small) so a placeholder reads as a small object, not a crate.
                go.transform.localScale = Vector3.one * PlaceholderSize(item.Graphic);
                go.GetComponent<Renderer>().sharedMaterial = _material;
                SetColor(go, new Color(0.85f, 0.7f, 0.25f));
                go.AddComponent<Pickable>().Serial = item.Serial;
                _items[item.Serial] = go;
            }

            // Standing on its surface, not floating above it.
            go.transform.position = WorldSpace.TileCenter(item.X, item.Y, item.Z) + Vector3.up * (go.transform.localScale.y * 0.5f);
            Props?.Stack(go, item.X, item.Y, item.X, item.Y, item.Z);
        }

        /// <summary>
        /// The player's tile. When set, items removed far from the player are kept as ghosts: UO servers drop
        /// items beyond about 18 tiles, but the 3D view reaches much further, and doorways and furniture
        /// shouldn't empty out in the distance.
        /// </summary>
        public System.Func<(int X, int Y)> PlayerTile { get; set; }

        /// <summary>Items closer than this when removed were really deleted (or picked up).</summary>
        private const int GhostRange = 15;
        /// <summary>A ghost the player is back near, which the server hasn't re-sent by now, is gone for real.</summary>
        private const float GhostGrace = 4f;
        private const int GhostForget = 120;

        private readonly Dictionary<uint, (int X, int Y)> _itemTiles = new Dictionary<uint, (int X, int Y)>();
        private readonly Dictionary<uint, float> _ghosts = new Dictionary<uint, float>();
        private readonly List<uint> _expired = new List<uint>();

        public void OnItemRemoved(uint serial)
        {
            if (PlayerTile != null && _itemTiles.TryGetValue(serial, out (int X, int Y) at) && _items.ContainsKey(serial))
            {
                (int px, int py) = PlayerTile();
                if (Mathf.Max(Mathf.Abs(at.X - px), Mathf.Abs(at.Y - py)) >= GhostRange)
                {
                    _ghosts[serial] = float.MaxValue; // not near yet
                    _doors?.KeepClosed(serial);
                    return;
                }
            }

            ForgetItem(serial);
        }

        private void ForgetItem(uint serial)
        {
            _ghosts.Remove(serial);
            _itemTiles.Remove(serial);
            _doors?.Remove(serial);
            if (_items.TryGetValue(serial, out GameObject go))
            {
                Object.Destroy(go);
                _items.Remove(serial);
            }

            _propItems.Remove(serial);
        }

        /// <summary>
        /// Drops ghosts that are far behind, and ghosts the player has been back near for a while without
        /// the server sending them again (they were deleted while out of range). Call every frame.
        /// </summary>
        public void PruneGhosts()
        {
            if (PlayerTile == null || _ghosts.Count == 0)
            {
                return;
            }

            (int px, int py) = PlayerTile();
            float now = Time.time;
            _expired.Clear();
            var near = new List<uint>();
            foreach (KeyValuePair<uint, float> ghost in _ghosts)
            {
                if (!_itemTiles.TryGetValue(ghost.Key, out (int X, int Y) at))
                {
                    _expired.Add(ghost.Key);
                    continue;
                }

                int d = Mathf.Max(Mathf.Abs(at.X - px), Mathf.Abs(at.Y - py));
                if (d > GhostForget || (d < GhostRange - 3 && ghost.Value != float.MaxValue && now - ghost.Value > GhostGrace))
                {
                    _expired.Add(ghost.Key);
                }
                else if (d < GhostRange - 3 && ghost.Value == float.MaxValue)
                {
                    near.Add(ghost.Key);
                }
            }

            foreach (uint serial in near)
            {
                _ghosts[serial] = now;
            }

            foreach (uint serial in _expired)
            {
                ForgetItem(serial);
            }
        }

        public void Clear()
        {
            // On scene teardown these may already be destroyed; Unity's null check covers that.
            foreach (MobileView view in _mobiles.Values)
            {
                if (view != null)
                {
                    Object.Destroy(view.gameObject);
                }
            }

            foreach (GameObject go in _items.Values)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }

            _mobiles.Clear();
            _items.Clear();
        }

        private MobileView Create(Mobile mobile, out Animator animator)
        {
            GameObject go;
            animator = null;
            if (mobile.IsHumanoid && Dresser != null)
            {
                go = new GameObject();
                go.transform.SetParent(_root, false);
                animator = Dress(mobile, go.transform);
            }
            else if (mobile.IsHumanoid && _humanoidPrefabs != null && _humanoidPrefabs.Length > 0)
            {
                GameObject prefab = _humanoidPrefabs[(int)(mobile.Serial % (uint)_humanoidPrefabs.Length)];
                go = Object.Instantiate(prefab, _root);
                animator = go.GetComponentInChildren<Animator>();
                if (animator != null && _humanoidAnimator != null)
                {
                    animator.runtimeAnimatorController = _humanoidAnimator;
                    animator.applyRootMotion = false; // the server owns position
                    animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                }
            }
            else if (!mobile.IsHumanoid && Creatures != null && Creatures.Find(mobile.Body) is CreatureCatalog.Entry creature)
            {
                go = new GameObject();
                go.transform.SetParent(_root, false);
                CreatureCatalog.Spawn(creature, go.transform, out animator);
            }
            else
            {
                go = new GameObject();
                go.transform.SetParent(_root, false);
                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.Destroy(body.GetComponent<Collider>());
                body.transform.SetParent(go.transform, false);
                body.transform.localPosition = Vector3.up * 0.8f;
                body.transform.localScale = new Vector3(0.7f, 0.8f, 0.7f);
                body.GetComponent<Renderer>().sharedMaterial = _material;
                SetColor(body, NotorietyColor(mobile.Notoriety));
            }

            go.name = $"Mobile 0x{mobile.Serial:X8} (body {mobile.Body})";
            go.AddComponent<Pickable>().Serial = mobile.Serial;
            return go.AddComponent<MobileView>();
        }

        /// <summary>Tints a vertex-colored primitive by rewriting its mesh colors.</summary>
        private static void SetColor(GameObject go, Color color)
        {
            var filter = go.GetComponent<MeshFilter>();
            Mesh mesh = filter.mesh; // per-instance copy
            var colors = new Color[mesh.vertexCount];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = color;
            }

            mesh.colors = colors;
        }

        private static Color NotorietyColor(Notoriety notoriety)
        {
            switch (notoriety)
            {
                case Notoriety.Innocent: return new Color(0.3f, 0.55f, 0.95f);
                case Notoriety.Ally: return new Color(0.3f, 0.85f, 0.35f);
                case Notoriety.Criminal:
                case Notoriety.Gray: return new Color(0.6f, 0.6f, 0.6f);
                case Notoriety.Enemy: return new Color(0.95f, 0.55f, 0.2f);
                case Notoriety.Murderer: return new Color(0.9f, 0.2f, 0.2f);
                case Notoriety.Invulnerable: return new Color(0.95f, 0.85f, 0.2f);
                default: return new Color(0.8f, 0.8f, 0.8f);
            }
        }
    }
}
