using System;
using System.Collections.Generic;
using FishNet.Managing;
using UnityEngine;
using UOClient;
using UOGame.Content;
using UOGame.Net;
using UOGame.Server;

namespace UOGame.UI
{
    /// <summary>
    /// Claiming land and building on it, client side (Docs/BuildingDesign.md). No targeting cursor anywhere:
    ///
    /// - Double-clicking a plot deed shows the plot in front of you, turning and moving with you, green where it may be
    ///   claimed and red (with the reason) where not. Click to claim, Escape to put the deed away.
    /// - H on your own land opens build mode: pick a piece in the palette, and a ghost of it follows the pointer on the
    ///   current level, snapped to the tile grid. Click places it (its materials leave your backpack), Q turns it,
    ///   Page Up/Down (or ] and [) change level, X takes down the piece under the pointer. H or Escape leaves.
    ///
    /// The checks are the server's own (<see cref="HousingService.CheckPiece"/>, <see cref="HousingService.CheckClaim"/>),
    /// so green here means the server will agree.
    /// </summary>
    public sealed partial class BuildMode : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        /// <summary>Translucent material for the ghost and the plot preview (tinted green or red).</summary>
        [SerializeField] private Material _ghost;

        public static BuildMode Instance { get; private set; }

        /// <summary>Building on <see cref="House"/> now.</summary>
        public static bool Building { get; private set; }

        /// <summary>The frame Esc was used to stop building or placing (nothing else should act on that press).</summary>
        public static int BackHandledFrame { get; private set; } = -1;
        /// <summary>Choosing where a plot deed's land goes.</summary>
        public static bool Claiming => Instance != null && Instance._deed != 0;
        public static HousePlot House { get; private set; }
        public static int Selected { get; private set; } = -1;
        public static int Level { get; private set; }
        public static int Rotation { get; private set; }
        /// <summary>What the preview says: why it's red, or what a click will do.</summary>
        public static string Status { get; private set; } = string.Empty;
        public static bool Valid { get; private set; }
        public static HousingRules.PlotTier ClaimTier { get; private set; }
        /// <summary>Raised when the mode, piece, level or rotation changes (the palette redraws).</summary>
        public static event Action Changed;

        private static readonly Color Green = new Color(0.35f, 1f, 0.45f, 0.45f);
        private static readonly Color Red = new Color(1f, 0.3f, 0.25f, 0.45f);
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private uint _deed;
        private GameObject _ghostPiece, _plotPreview, _outline;
        private int _ghostFor = -1, _ghostRot = -1;
        private Vector2Int _claimAt;
        private Vector2Int? _anchor;
        private MaterialPropertyBlock _block;

        private void Awake()
        {
            Instance = this;
            _block = new MaterialPropertyBlock();
        }

        private void OnDisable() => Stop();

        // ------------------------------------------------------------------ entering and leaving

        /// <summary>A double-clicked item: a plot deed starts claiming land. True when it was one.</summary>
        public static bool TryUseDeed(ItemView item)
        {
            if (Instance != null && Instance.TryShipDeed(item))
            {
                return true; // a ship: placed with a ghost on the water
            }

            HousingRules.PlotTier tier = HousingRules.TierForDeed(ItemService.Catalog?.Get(item.Kind)?.Type);
            if (tier == null || Instance == null)
            {
                return false;
            }

            Instance.Stop();
            Instance._deed = item.Id;
            ClaimTier = tier;
            Instance.SetBuildInput(true);
            Changed?.Invoke();
            return true;
        }

        public static void Select(int piece)
        {
            Selected = piece;
            Changed?.Invoke();
        }

        public static void Exit() => Instance?.Stop();

        private void Stop()
        {
            bool was = Building || _deed != 0;
            Building = false;
            _deed = 0;
            ClaimTier = null;
            StopShip();
            House = null;
            Status = string.Empty;
            ClearGhosts();
            if (_outline != null)
            {
                Destroy(_outline);
                _outline = null;
            }
            if (was)
            {
                SetBuildInput(false);
                Changed?.Invoke();
            }
        }

        private void ToggleBuilding()
        {
            if (Building)
            {
                Stop();
                return;
            }

            PlayerMotor me = PlayerMotor.Local;
            string name = me != null ? me.GetComponent<PlayerIdentity>()?.Name.Value : null;
            HousePlot mine = null;
            float best = float.MaxValue;
            foreach (HousePlot plot in HousePlot.All)
            {
                if (plot == null || !plot.OwnedBy(name))
                {
                    continue;
                }

                float d = (plot.transform.position - me.transform.position).magnitude;
                if (d < best)
                {
                    best = d;
                    mine = plot;
                }
            }

            if (mine == null)
            {
                ItemService.ShowNotice("You have no house nearby. A plot deed from an architect claims land to build on.");
                return;
            }

            Stop();
            Building = true;
            House = mine;
            Level = Mathf.Clamp(Mathf.FloorToInt((me.transform.position.y - mine.BaseY.Value + 0.5f) / HousingRules.StoreyHeight), 0, HousingRules.TopLevel);
            if (Selected < 0 && HousingRules.Pieces.Count > 0)
            {
                Selected = 0;
            }

            SetBuildInput(true);
            Changed?.Invoke();
        }

        /// <summary>The Build actions take over the click (Gameplay's Select would pick targets).</summary>
        private void SetBuildInput(bool on)
        {
            if (on)
            {
                GameInput.Controls.Build.Enable();
                GameInput.Controls.Gameplay.Select.Disable();
            }
            else
            {
                GameInput.Controls.Build.Disable();
                GameInput.Controls.Gameplay.Select.Enable();
            }
        }

        // ------------------------------------------------------------------ each frame

        private void Update()
        {
            PlayerMotor me = PlayerMotor.Local;
            if (me == null || !HousingRules.Loaded)
            {
                Stop();
                return;
            }

            if (!GameInput.Typing && GameInput.Controls.Gameplay.BuildMode.WasPressedThisFrame())
            {
                ToggleBuilding();
            }

            if (!Building && _deed == 0)
            {
                return;
            }

            if (!GameInput.Typing && GameInput.Controls.Gameplay.Back.WasPressedThisFrame())
            {
                BackHandledFrame = Time.frameCount;
                Stop();
                return;
            }

            if (_deed != 0 && _ship != null)
            {
                UpdateShip(me);
            }
            else if (_deed != 0)
            {
                UpdateClaim(me);
            }
            else
            {
                UpdateBuilding(me);
            }
        }

        private bool Clicked(UOControls.BuildActions build) =>
            !GameInput.Typing && build.Place.WasPressedThisFrame() && !GameInput.IsPointerOverUI(GameInput.Controls.Gameplay.Point.ReadValue<Vector2>());

        // ------------------------------------------------------------------ claiming

        private void UpdateClaim(PlayerMotor me)
        {
            if (Array.Find(ItemService.Mine, i => i.Id == _deed).Id == 0)
            {
                Stop(); // the deed is gone (used, dropped)
                return;
            }

            int size = ClaimTier.Size;
            Vector2Int at = WorldSpace.ToTile(me.transform.position);
            Vector3 f = me.transform.forward;
            Vector2Int origin;
            if (Mathf.Abs(f.x) >= Mathf.Abs(f.z))
            {
                origin = f.x > 0 ? new Vector2Int(at.x + 2, at.y - size / 2) : new Vector2Int(at.x - 1 - size, at.y - size / 2);
            }
            else
            {
                origin = f.z < 0 ? new Vector2Int(at.x - size / 2, at.y + 2) : new Vector2Int(at.x - size / 2, at.y - 1 - size);
            }

            if (origin != _claimAt || _plotPreview == null || Time.frameCount % 15 == 0)
            {
                _claimAt = origin;
                string why = HousingService.CheckClaim(1, origin.x, origin.y, size, me.transform.position, out float baseY);
                Valid = why == null;
                Status = why ?? $"An empty {ClaimTier.Name.ToLowerInvariant()} plot, {size} x {size} tiles: you build the house on it yourself ({{BuildMode}} once it's yours). {{Select}} to claim, {{Back}} to cancel.";
                (float Low, float High)? ground = HousingRules.Ground(origin.x, origin.y, size);
                ShowPlot(origin, size, ground?.High ?? me.transform.position.y, Valid);
                Changed?.Invoke();
            }

            if (Clicked(GameInput.Controls.Build))
            {
                if (Valid)
                {
                    _network.ClientManager.Broadcast(new ClaimPlot { Deed = _deed, X = origin.x, Y = origin.y });
                    Stop();
                }
                else
                {
                    ItemService.ShowNotice(Status);
                }
            }
        }

        private void ShowPlot(Vector2Int origin, int size, float y, bool valid)
        {
            if (_plotPreview == null)
            {
                _plotPreview = new GameObject("Plot preview");
                GameObject slab = Ghostly(GameObject.CreatePrimitive(PrimitiveType.Cube));
                slab.transform.SetParent(_plotPreview.transform, false);
                for (int i = 0; i < 4; i++)
                {
                    GameObject post = Ghostly(GameObject.CreatePrimitive(PrimitiveType.Cube));
                    post.transform.SetParent(_plotPreview.transform, false);
                }
            }

            float metres = size * WorldSpace.TileSize;
            Vector3 corner = WorldSpace.Corner(origin.x, origin.y, 0f);
            _plotPreview.transform.position = new Vector3(corner.x + metres * 0.5f, y, corner.z - metres * 0.5f);
            Transform t = _plotPreview.transform;
            t.GetChild(0).localScale = new Vector3(metres, 0.08f, metres);
            t.GetChild(0).localPosition = new Vector3(0f, 0.05f, 0f);
            for (int i = 0; i < 4; i++)
            {
                Transform post = t.GetChild(i + 1);
                post.localScale = new Vector3(0.25f, 3f, 0.25f);
                post.localPosition = new Vector3((i % 2 == 0 ? -0.5f : 0.5f) * metres, 1.5f, (i < 2 ? -0.5f : 0.5f) * metres);
            }

            Tint(_plotPreview, valid);
        }

        /// <summary>While building: the plot's edges as low rails and corner posts, so you can see where your land ends.</summary>
        private void ShowOutline(HousePlot house)
        {
            if (_outline == null)
            {
                _outline = new GameObject("Plot outline");
                for (int i = 0; i < 8; i++)
                {
                    Ghostly(GameObject.CreatePrimitive(PrimitiveType.Cube)).transform.SetParent(_outline.transform, false);
                }

                Tint(_outline, true);
            }

            float metres = house.Size.Value * WorldSpace.TileSize;
            Vector3 corner = WorldSpace.Corner(house.Origin.x, house.Origin.y, 0f);
            _outline.transform.position = new Vector3(corner.x + metres * 0.5f, house.BaseY.Value, corner.z - metres * 0.5f);
            Transform t = _outline.transform;
            for (int i = 0; i < 4; i++)
            {
                bool alongX = i < 2;
                float side = (i % 2 == 0 ? -0.5f : 0.5f) * metres;
                Transform rail = t.GetChild(i);
                rail.localScale = alongX ? new Vector3(metres, 0.12f, 0.12f) : new Vector3(0.12f, 0.12f, metres);
                rail.localPosition = alongX ? new Vector3(0f, 0.3f, side) : new Vector3(side, 0.3f, 0f);
                Transform post = t.GetChild(i + 4);
                post.localScale = new Vector3(0.2f, 3f, 0.2f);
                post.localPosition = new Vector3((i % 2 == 0 ? -0.5f : 0.5f) * metres, 1.5f, (i < 2 ? -0.5f : 0.5f) * metres);
            }
        }

        // ------------------------------------------------------------------ building

        private void UpdateBuilding(PlayerMotor me)
        {
            if (House == null || !House.IsSpawned)
            {
                Stop();
                return;
            }

            ShowOutline(House);
            UOControls.BuildActions build = GameInput.Controls.Build;
            if (!GameInput.Typing)
            {
                if (build.Rotate.WasPressedThisFrame())
                {
                    Rotation = (Rotation + 1) % 4;
                    Changed?.Invoke();
                }

                if (build.LevelUp.WasPressedThisFrame())
                {
                    Level = Mathf.Min(Level + 1, HousingRules.TopLevel);
                    Changed?.Invoke();
                }

                if (build.LevelDown.WasPressedThisFrame())
                {
                    Level = Mathf.Max(Level - 1, 0);
                    Changed?.Invoke();
                }

                if (build.Remove.WasPressedThisFrame())
                {
                    RemoveUnderPointer();
                }
            }

            HousingRules.PieceDef def = HousingRules.Piece(Selected);
            Camera camera = Camera.main;
            if (def == null || camera == null)
            {
                HideGhost();
                return;
            }

            Ray ray = camera.ScreenPointToRay(GameInput.Controls.Gameplay.Point.ReadValue<Vector2>());
            float planeY = House.BaseY.Value + Level * HousingRules.StoreyHeight;
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            if (GameInput.IsPointerOverUI(GameInput.Controls.Gameplay.Point.ReadValue<Vector2>()) || !plane.Raycast(ray, out float distance))
            {
                HideGhost();
                return;
            }

            Vector2 onPlot = HousingRules.PlotTileAt(House.Origin, ray.GetPoint(distance));
            Vector2Int anchor = HousingRules.Anchor(def, Rotation, onPlot);
            if (_ghostFor != Selected || _ghostRot != Rotation || _ghostPiece == null)
            {
                ClearGhosts();
                _ghostPiece = HousePlot.Instantiate(def, House.Origin, House.BaseY.Value, 0, 0, Level, Rotation, null);
                Ghostly(_ghostPiece);
                _ghostFor = Selected;
                _ghostRot = Rotation;
            }

            (Vector3 at, Quaternion turn) = HousingRules.Frame(House.Origin, House.BaseY.Value, def, anchor.x, anchor.y, Level, Rotation);
            _ghostPiece.transform.SetPositionAndRotation(at, turn);
            _ghostPiece.SetActive(true);
            string why = HousingService.CheckPiece(House, def, anchor.x, anchor.y, Level, Rotation, me.transform.position) ?? MissingMaterials(def);
            Valid = why == null;
            string status = why ?? $"{def.Name}: click to build.";
            if (status != Status || !Equals(_anchor, (Vector2Int?)anchor))
            {
                Status = status;
                _anchor = anchor;
                Changed?.Invoke();
            }

            Tint(_ghostPiece, Valid);
            if (Clicked(build))
            {
                if (Valid)
                {
                    _network.ClientManager.Broadcast(new PlacePiece
                    {
                        House = House.HouseId.Value,
                        Piece = (ushort)def.Index,
                        X = (byte)anchor.x,
                        Y = (byte)anchor.y,
                        Level = (byte)Level,
                        Rot = (byte)Rotation,
                    });
                }
                else
                {
                    ItemService.ShowNotice(Status);
                }
            }
        }

        /// <summary>What's lacking to pay for a piece from the backpack, or null.</summary>
        public static string MissingMaterials(HousingRules.PieceDef def)
        {
            foreach ((string item, int amount) in def.Cost)
            {
                int have = Have(item);
                if (have < amount)
                {
                    return $"{def.Name}: needs {amount} {Spaced(item).ToLowerInvariant()} (you have {have}).";
                }
            }

            return null;
        }

        /// <summary>How much of a kind (or what pays for it: logs for boards) the backpack holds.</summary>
        public static int Have(string type)
        {
            int have = 0;
            foreach (string accepted in CraftService.Accepts(type))
            {
                foreach (ItemView v in ItemService.Mine)
                {
                    if (v.Slot == 0 && string.Equals(ItemService.Catalog?.Get(v.Kind)?.Type, accepted, StringComparison.OrdinalIgnoreCase))
                    {
                        have += v.Amount;
                    }
                }
            }

            return have;
        }

        public static string Spaced(string type) => System.Text.RegularExpressions.Regex.Replace(type ?? string.Empty, "(?<=[a-z])(?=[A-Z])", " ");

        private void RemoveUnderPointer()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Ray ray = camera.ScreenPointToRay(GameInput.Controls.Gameplay.Point.ReadValue<Vector2>());
            RaycastHit[] hits = Physics.RaycastAll(ray, 120f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                HousePieceTag tag = hit.collider.GetComponentInParent<HousePieceTag>();
                if (tag != null && tag.House == House)
                {
                    _network.ClientManager.Broadcast(new RemovePiece { House = House.HouseId.Value, Uid = tag.Uid });
                    return;
                }

                if (hit.collider.GetComponentInParent<PlayerMotor>() == null)
                {
                    break; // something else is in the way
                }
            }

            ItemService.ShowNotice("Point at a piece of your house to take it down.");
        }

        // ------------------------------------------------------------------ ghosts

        /// <summary>Make an object a see-through preview: no colliders, ignored by raycasts, ghost material.</summary>
        private GameObject Ghostly(GameObject go)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
            {
                Destroy(c);
            }

            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = 2; // Ignore Raycast
            }

            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    mats[i] = _ghost;
                }

                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            return go;
        }

        private void Tint(GameObject go, bool valid)
        {
            _block.SetColor(BaseColor, valid ? Green : Red);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.SetPropertyBlock(_block);
            }
        }

        private void HideGhost()
        {
            if (_ghostPiece != null)
            {
                _ghostPiece.SetActive(false);
            }
        }

        private void ClearGhosts()
        {
            if (_ghostPiece != null)
            {
                Destroy(_ghostPiece);
            }

            if (_plotPreview != null)
            {
                Destroy(_plotPreview);
            }

            _ghostPiece = null;
            _plotPreview = null;
            _ghostFor = -1;
            _ghostRot = -1;
        }
    }
}
