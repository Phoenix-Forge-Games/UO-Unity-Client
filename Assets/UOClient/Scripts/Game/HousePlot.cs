using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UOClient;
using UOClient.Buildings;
using UOGame.Content;
using UOGame.Server;

namespace UOGame
{
    /// <summary>One piece of a house on the wire: which piece (Housing.json order), where on the plot, how turned.</summary>
    public struct HousePiece
    {
        public ushort Uid;
        public ushort Piece;
        public byte X, Y, Level, Rot;
    }

    /// <summary>Marks a built piece's objects so build mode can tell which piece the cursor is on.</summary>
    public sealed class HousePieceTag : MonoBehaviour
    {
        public HousePlot House;
        public ushort Uid;
    }

    /// <summary>
    /// A claimed plot and the house on it, networked: where it is, who owns it, its styles and its pieces. Server and
    /// clients build the same pieces from the kit prefabs (their own colliders make the walls solid on both sides), so a
    /// change to the piece list is all that travels. Doors get world IDs of their own and open through the world's door
    /// service. Trees on the plot are cleared when it appears (as SeriousFun's TreeClearing did).
    /// </summary>
    public sealed class HousePlot : NetworkBehaviour
    {
        public readonly SyncVar<int> HouseId = new SyncVar<int>();
        public readonly SyncVar<int> OriginX = new SyncVar<int>();
        public readonly SyncVar<int> OriginY = new SyncVar<int>();
        public readonly SyncVar<int> Size = new SyncVar<int>();
        public readonly SyncVar<float> BaseY = new SyncVar<float>();
        public readonly SyncVar<string> OwnerName = new SyncVar<string>();
        public readonly SyncVar<string> HouseName = new SyncVar<string>();
        public readonly SyncVar<byte> WallStyle = new SyncVar<byte>();
        public readonly SyncVar<byte> RoofStyle = new SyncVar<byte>();
        public readonly SyncVar<bool> Public = new SyncVar<bool>();
        public readonly SyncList<HousePiece> Pieces = new SyncList<HousePiece>();
        /// <summary>Characters (by name) who may open the doors and keep the house from decaying.</summary>
        public readonly SyncList<string> Friends = new SyncList<string>();

        /// <summary>Every plot this process knows (server: all; client: those in view).</summary>
        public static readonly List<HousePlot> All = new List<HousePlot>();

        private readonly Dictionary<ushort, GameObject> _built = new Dictionary<ushort, GameObject>();
        private Transform _root;
        private bool _started;

        public Vector2Int Origin => new Vector2Int(OriginX.Value, OriginY.Value);
        public RectInt Area => new RectInt(OriginX.Value, OriginY.Value, Size.Value, Size.Value);

        /// <summary>A house door's world ID: the top bit marks houses, then the house and the piece.</summary>
        public static ulong DoorId(int house, ushort uid) => 0x8000_0000_0000_0000UL | ((ulong)(uint)house << 16) | uid;

        public static bool IsHouseDoor(ulong id) => (id & 0x8000_0000_0000_0000UL) != 0;

        public static HousePlot ForDoor(ulong id) => IsHouseDoor(id) ? All.Find(h => h != null && h.HouseId.Value == (int)((id >> 16) & 0xFFFFFFFF)) : null;

        /// <summary>The plot covering a tile, if any.</summary>
        public static HousePlot At(Vector2Int tile) => All.Find(h => h != null && h.Area.Contains(tile));

        /// <summary>Is a tile on a plot or within <see cref="HousingRules.SpawnClearance"/> of one (no spawning or wandering there)?</summary>
        public static bool Cleared(Vector2Int tile)
        {
            int m = HousingRules.SpawnClearance;
            foreach (HousePlot h in All)
            {
                if (h != null && new RectInt(h.OriginX.Value - m, h.OriginY.Value - m, h.Size.Value + m * 2, h.Size.Value + m * 2).Contains(tile))
                {
                    return true;
                }
            }

            return false;
        }

        private void Awake()
        {
            Pieces.OnChange += OnPiecesChanged;
            WallStyle.OnChange += (_, __, ___) => Repaint();
            RoofStyle.OnChange += (_, __, ___) => Repaint();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            Begin();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Begin();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            End();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            End();
        }

        /// <summary>First start on this process (a host starts as both server and client: build once).</summary>
        private void Begin()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            All.Add(this);
            _root = new GameObject($"House {HouseId.Value}").transform;
            _root.SetParent(transform, true);
            _root.position = Vector3.zero;
            _root.rotation = Quaternion.identity;
            if (HousingRules.ClearTrees)
            {
                PlotClearing.Add(Area);
            }

            RebuildAll();
        }

        private void End()
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            All.Remove(this);
            PlotClearing.Remove(Area);
            foreach (GameObject go in _built.Values)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            _built.Clear();
        }

        private void OnPiecesChanged(SyncListOperation op, int index, HousePiece previous, HousePiece next, bool asServer)
        {
            if (!_started || (!asServer && IsServerStarted))
            {
                return; // a host builds on the server's call only
            }

            switch (op)
            {
                case SyncListOperation.Add:
                case SyncListOperation.Insert:
                    Build(next);
                    break;
                case SyncListOperation.RemoveAt:
                    Unbuild(previous.Uid);
                    break;
                case SyncListOperation.Set:
                    Unbuild(previous.Uid);
                    Build(next);
                    break;
                case SyncListOperation.Clear:
                case SyncListOperation.Complete:
                    RebuildAll();
                    break;
            }
        }

        private void RebuildAll()
        {
            if (_root == null)
            {
                return;
            }

            foreach (GameObject go in _built.Values)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            _built.Clear();
            foreach (HousePiece piece in Pieces)
            {
                Build(piece);
            }
        }

        private void Unbuild(ushort uid)
        {
            if (_built.TryGetValue(uid, out GameObject go))
            {
                _built.Remove(uid);
                if (go != null)
                {
                    Destroy(go);
                }
            }
        }

        private void Build(HousePiece piece)
        {
            HousingRules.PieceDef def = HousingRules.Piece(piece.Piece);
            if (def == null || _root == null)
            {
                return;
            }

            Unbuild(piece.Uid);
            GameObject go = Instantiate(def, Origin, BaseY.Value, piece.X, piece.Y, piece.Level, piece.Rot, _root);
            go.name = $"{def.Id} {piece.Uid}";
            var tag = go.AddComponent<HousePieceTag>();
            tag.House = this;
            tag.Uid = piece.Uid;
            Paint(go, def);
            if (def.Door)
            {
                foreach (Transform part in go.transform)
                {
                    var door = part.gameObject.AddComponent<BuildingDoor>();
                    BuildingDoorBake.Report report = BuildingDoorBake.Bake(door, part);
                    if (!report.Ok)
                    {
                        Destroy(door);
                        continue;
                    }

                    door.WorldId = DoorId(HouseId.Value, piece.Uid);
                    break; // one door per piece
                }
            }

            _built[piece.Uid] = go;
        }

        /// <summary>
        /// Lay out a piece's kit parts in the world (server, clients, and build mode's ghost all use this). Parts that
        /// the kit doesn't have in this build are skipped.
        /// </summary>
        public static GameObject Instantiate(HousingRules.PieceDef def, Vector2Int origin, float baseY, int x, int y, int level, int rot, Transform parent)
        {
            (Vector3 at, Quaternion turn) = HousingRules.Frame(origin, baseY, def, x, y, level, rot);
            var go = new GameObject(def.Id);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(at, turn);
            foreach (HousingRules.Part part in def.Parts)
            {
                GameObject prefab = HousingService.Prefab(part.Prefab);
                if (prefab == null)
                {
                    continue;
                }

                GameObject p = Object.Instantiate(prefab, go.transform, false);
                p.name = prefab.name;
                p.transform.localPosition = part.At;
                p.transform.localRotation = Quaternion.Euler(0f, part.Yaw, 0f);
            }

            return go;
        }

        private void Repaint()
        {
            foreach (KeyValuePair<ushort, GameObject> pair in _built)
            {
                HousePiece? piece = null;
                foreach (HousePiece p in Pieces)
                {
                    if (p.Uid == pair.Key)
                    {
                        piece = p;
                        break;
                    }
                }

                if (piece != null && pair.Value != null)
                {
                    Paint(pair.Value, HousingRules.Piece(piece.Value.Piece));
                }
            }
        }

        private void Paint(GameObject go, HousingRules.PieceDef def)
        {
            if (def?.Paint == null)
            {
                return;
            }

            List<HousingRules.Style> styles = def.Paint == "Roof" ? HousingRules.RoofStyles : HousingRules.Styles;
            int index = def.Paint == "Roof" ? RoofStyle.Value : WallStyle.Value;
            Material material = index < styles.Count ? HousingService.Material(styles[index].Material) : null;
            if (material == null)
            {
                return;
            }

            foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool touched = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || mats[i].name.StartsWith("Glass") || mats[i] == material)
                    {
                        continue; // glazing keeps its own transparent material
                    }

                    mats[i] = material;
                    touched = true;
                }

                if (touched)
                {
                    r.sharedMaterials = mats;
                }
            }
        }

        /// <summary>The slots every piece of this house takes (for the overlap check).</summary>
        public HashSet<long> TakenSlots(ushort except = 0)
        {
            var taken = new HashSet<long>();
            foreach (HousePiece p in Pieces)
            {
                HousingRules.PieceDef def = HousingRules.Piece(p.Piece);
                if (def == null || p.Uid == except)
                {
                    continue;
                }

                foreach (long slot in HousingRules.Slots(def, p.X, p.Y, p.Level, p.Rot))
                {
                    taken.Add(slot);
                }
            }

            return taken;
        }

        /// <summary>May this character build here (the owner) or open the doors (owner, friends, anyone when public)?</summary>
        public bool OwnedBy(string character) => !string.IsNullOrEmpty(character) && string.Equals(character, OwnerName.Value, System.StringComparison.OrdinalIgnoreCase);

        public bool IsFriend(string character)
        {
            foreach (string f in Friends)
            {
                if (string.Equals(f, character, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Land cleared for plots: trees standing on a claimed plot are hidden, and their trunks lose their colliders, in
    /// every loaded chunk and in chunks as they load (server and clients alike).
    /// </summary>
    public static class PlotClearing
    {
        private static readonly List<RectInt> Areas = new List<RectInt>();
        private static int _vegetationLayer = -2;
        private static bool _hooked;

        public static void Add(RectInt area)
        {
            Hook();
            Areas.Add(area);
            foreach (UOClient.Bake.BakedWorldView view in Object.FindObjectsByType<UOClient.Bake.BakedWorldView>())
            {
                foreach (UOClient.Bake.LoadedChunk chunk in view.Chunks)
                {
                    Clear(chunk.Root, area, true);
                }
            }
        }

        public static void Remove(RectInt area) => Areas.Remove(area); // trees come back as their chunks reload

        private static void Hook()
        {
            if (_hooked)
            {
                return;
            }

            _hooked = true;
            UOClient.Bake.BakedWorldView.ChunkLoading += chunk =>
            {
                foreach (RectInt area in Areas)
                {
                    Clear(chunk.Root, area, false);
                }
            };
        }

        /// <summary>
        /// Clear the area's tiles: hide every plant, tree, log and bush standing on them (chunk root / block / object),
        /// and drop the trunk colliders of trees there (direct children of the root, at each tree's base).
        /// </summary>
        private static void Clear(GameObject root, RectInt area, bool trunks)
        {
            if (root == null)
            {
                return;
            }

            if (_vegetationLayer == -2)
            {
                _vegetationLayer = LayerMask.NameToLayer(MapRenderer.VegetationLayerName);
            }

            Transform top = root.transform;
            for (int i = top.childCount - 1; i >= 0; i--)
            {
                Transform child = top.GetChild(i);
                if (child.name == "Trunk")
                {
                    if (trunks && area.Contains(WorldSpace.ToTile(child.position)))
                    {
                        Object.Destroy(child.gameObject);
                    }

                    continue;
                }

                for (int j = 0; j < child.childCount; j++)
                {
                    Transform thing = child.GetChild(j);
                    if ((thing.gameObject.layer == _vegetationLayer || HasVegetation(thing)) && area.Contains(WorldSpace.ToTile(thing.position)))
                    {
                        thing.gameObject.SetActive(false);
                    }
                }
            }
        }

        private static bool HasVegetation(Transform t)
        {
            Renderer r = t.GetComponentInChildren<Renderer>();
            return r != null && r.gameObject.layer == _vegetationLayer;
        }

    }
}
