using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Buildings;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// MMO-style targeting on top of UO's rules (Docs/GameplayDesign.md): left-click selects a mobile or item (and
    /// shows its name), double-click uses it, Tab cycles hostiles in view, Esc clears, R attacks the target (war mode
    /// on first), ` toggles war mode, E uses the nearest usable thing within reach.
    /// </summary>
    public sealed class Targeting : MonoBehaviour
    {
        private const float DoubleClickSeconds = 0.35f;
        private const float ClickMaxPixels = 6f;
        private const int TabRangeTiles = 14;
        private const int UseRangeTiles = 2;

        /// <summary>The selected mobile or item (0: none).</summary>
        public uint Current { get; private set; }

        public event Action<uint> Changed;

        /// <summary>E on a mobile: the HUD opens the dialogue (NPC context entries) for it.</summary>
        public event Action<uint> TalkTo;

        private const int TalkRangeTiles = 4;

        private Vector2 _pressPosition;
        private bool _pressed;
        private uint _lastClicked;
        private float _lastClickTime;

        private static WorldView View => WorldView.Current;

        private void Update()
        {
            WorldView view = View;
            if (view == null || view.World?.Player == null || GameInput.Typing)
            {
                return;
            }

            UOControls.GameplayActions input = GameInput.Gameplay;
            Vector2 pointer = input.Point.ReadValue<Vector2>();
            if (input.Select.WasPressedThisFrame())
            {
                _pressed = !GameInput.IsPointerOverUI(pointer);
                _pressPosition = pointer;
            }

            if (input.Select.WasReleasedThisFrame() && _pressed)
            {
                _pressed = false;
                if ((pointer - _pressPosition).sqrMagnitude <= ClickMaxPixels * ClickMaxPixels)
                {
                    Click(pointer);
                }
            }

            if (input.CycleTarget.WasPressedThisFrame())
            {
                Cycle(input.Run.IsPressed() ? -1 : 1);
            }

            if (input.Use.WasPressedThisFrame())
            {
                Interact(pointer);
            }

            if (input.Attack.WasPressedThisFrame())
            {
                AttackTarget();
            }

            if (input.ToggleWarMode.WasPressedThisFrame())
            {
                view.World.SetWarMode(!view.World.WarMode);
            }

            if (Current != 0 && !view.World.Mobiles.ContainsKey(Current) && !view.World.Items.ContainsKey(Current))
            {
                Set(0); // it's gone
            }
        }

        public void Set(uint serial)
        {
            if (serial == Current)
            {
                return;
            }

            Current = serial;
            WorldState world = View?.World;
            if (serial != 0 && world != null && world.Mobiles.ContainsKey(serial))
            {
                world.RequestStatus(serial); // name and health for the target frame
            }

            Changed?.Invoke(serial);
        }

        private void Click(Vector2 pointer)
        {
            uint serial = Pick(pointer);
            WorldState world = View.World;
            if (serial == 0)
            {
                return; // clicking empty ground keeps the target, as in most MMOs
            }

            bool doubleClick = serial == _lastClicked && Time.unscaledTime - _lastClickTime < DoubleClickSeconds;
            _lastClicked = serial;
            _lastClickTime = Time.unscaledTime;
            if (doubleClick)
            {
                world.Use(serial);
                _lastClicked = 0;
                return;
            }

            Set(serial);
            world.Look(serial);
        }

        /// <summary>
        /// The mobile or item under the pointer: a raycast against the views' fitted colliders (<see cref="Pickable"/>).
        /// Solid world geometry (terrain, buildings) in front hides what's behind it.
        /// </summary>
        public uint Pick(Vector2 pointer)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return 0;
            }

            Ray ray = camera.ScreenPointToRay(pointer);
            int count = Physics.RaycastNonAlloc(ray, Hits, 250f, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(Hits, 0, count, HitDistance);
            for (int i = 0; i < count; i++)
            {
                Collider hit = Hits[i].collider;
                Pickable pickable = hit.GetComponentInParent<Pickable>();
                if (pickable != null)
                {
                    return pickable.Serial;
                }

                if (!hit.isTrigger)
                {
                    return 0; // a wall or the ground is in the way
                }
            }

            return 0;
        }

        private static readonly RaycastHit[] Hits = new RaycastHit[64];
        private static readonly IComparer<RaycastHit> HitDistance =
            Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        private static bool IsHostile(Mobile mobile) =>
            mobile.Notoriety != Notoriety.Innocent && mobile.Notoriety != Notoriety.Ally && mobile.Notoriety != Notoriety.Invulnerable;

        /// <summary>Tab: the next hostile on screen within range, nearest first.</summary>
        private void Cycle(int step)
        {
            Camera camera = Camera.main;
            WorldState world = View.World;
            Mobile player = world.Player;
            var candidates = new List<(uint Serial, int Distance)>();
            foreach (Mobile mobile in world.Mobiles.Values)
            {
                if (mobile == player || !IsHostile(mobile) || !MobileView.IsVisible(mobile))
                {
                    continue;
                }

                int distance = Math.Max(Math.Abs(mobile.X - player.X), Math.Abs(mobile.Y - player.Y));
                MobileView mv = View.Entities.Get(mobile.Serial);
                if (distance > TabRangeTiles || mv == null)
                {
                    continue;
                }

                if (camera != null)
                {
                    Vector3 vp = camera.WorldToViewportPoint(mv.transform.position + Vector3.up);
                    if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f)
                    {
                        continue;
                    }
                }

                candidates.Add((mobile.Serial, distance));
            }

            if (candidates.Count == 0)
            {
                return;
            }

            candidates.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Serial.CompareTo(b.Serial));
            int index = candidates.FindIndex(c => c.Serial == Current);
            index = index < 0 ? (step > 0 ? 0 : candidates.Count - 1) : (index + step + candidates.Count) % candidates.Count;
            Set(candidates[index].Serial);
        }

        /// <summary>
        /// E: interacts with what's under the cursor (raycast): a mobile within reach opens its dialogue, an item
        /// within reach is used. With nothing there, the nearest usable item in front of you is used instead.
        /// </summary>
        private void Interact(Vector2 pointer)
        {
            WorldState world = View.World;
            Mobile player = world.Player;
            uint picked = GameInput.IsPointerOverUI(pointer) ? 0 : Pick(pointer);
            if (picked != 0 && picked != player.Serial)
            {
                if (world.Mobiles.TryGetValue(picked, out Mobile mobile))
                {
                    if (Math.Max(Math.Abs(mobile.X - player.X), Math.Abs(mobile.Y - player.Y)) <= TalkRangeTiles)
                    {
                        TalkTo?.Invoke(picked);
                        return;
                    }
                }
                else if (world.Items.TryGetValue(picked, out WorldItem item) &&
                         Math.Max(Math.Abs(item.X - player.X), Math.Abs(item.Y - player.Y)) <= UseRangeTiles)
                {
                    world.Use(picked);
                    return;
                }
            }

            if (TryUseDoor(pointer))
            {
                return;
            }

            UseNearest();
        }

        /// <summary>How far (metres) from the player a door may be and still be opened with E.</summary>
        private const float DoorReach = 4f;

        /// <summary>How far (metres) a door's middle may be off the line under the cursor.</summary>
        private const float DoorAim = 1.6f;

        /// <summary>How long a door stands open before it shuts itself.</summary>
        private const float DoorOpenSeconds = 4f;

        private readonly Dictionary<BuildingDoor, Vector3> _doorMiddles = new Dictionary<BuildingDoor, Vector3>();

        /// <summary>
        /// E on a door: the door nearest the line under the cursor, within reach, opens, and shuts itself a moment
        /// later. Picked by geometry, not colliders (house pieces don't carry any).
        /// </summary>
        private bool TryUseDoor(Vector2 pointer)
        {
            Camera camera = Camera.main;
            Transform player = View.Entities.Get(View.World.Player.Serial)?.transform;
            if (camera == null || player == null || GameInput.IsPointerOverUI(pointer))
            {
                return false;
            }

            Ray ray = camera.ScreenPointToRay(pointer);
            BuildingDoor best = null;
            float bestOff = DoorAim;
            foreach (BuildingDoor door in BuildingDoor.All)
            {
                if (door == null || door.LeafCount == 0)
                {
                    continue;
                }

                if (!_doorMiddles.TryGetValue(door, out Vector3 middle))
                {
                    middle = door.transform.position + Vector3.up;
                    var bounds = new Bounds();
                    bool any = false;
                    foreach (Renderer r in door.GetComponentsInChildren<Renderer>())
                    {
                        if (!any) { bounds = r.bounds; any = true; } else { bounds.Encapsulate(r.bounds); }
                    }

                    if (any)
                    {
                        middle = bounds.center;
                    }

                    _doorMiddles[door] = middle;
                }

                Vector3 toDoor = middle - ray.origin;
                if (Vector3.Dot(toDoor, ray.direction) <= 0f || (middle - player.position).magnitude > DoorReach)
                {
                    continue;
                }

                float off = Vector3.Cross(ray.direction, toDoor).magnitude;
                if (off < bestOff)
                {
                    bestOff = off;
                    best = door;
                }
            }

            if (best == null)
            {
                return false;
            }

            // The door's own logic for now; with the new server this sends the open trigger instead.
            best.OpenFor(DoorOpenSeconds);
            return true;
        }

        /// <summary>The nearest item within reach (door, corpse, chest...), else a target standing beside you.</summary>
        private void UseNearest()
        {
            WorldState world = View.World;
            Mobile player = world.Player;
            var facing = new Vector2Int(DirectionUtil.Dx(player.Facing), DirectionUtil.Dy(player.Facing));
            uint best = 0;
            float bestScore = float.MaxValue;
            foreach (WorldItem item in world.Items.Values)
            {
                if (item.IsMulti)
                {
                    continue;
                }

                int dx = item.X - player.X, dy = item.Y - player.Y;
                int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (distance > UseRangeTiles || Math.Abs(item.Z - player.Z) > 16)
                {
                    continue;
                }

                // Prefer what's in front: distance, plus a penalty for being off to the side or behind.
                float ahead = dx * facing.x + dy * facing.y;
                float score = distance - 0.5f * Mathf.Clamp(ahead, -1f, 1f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = item.Serial;
                }
            }

            if (best == 0 && Current != 0 && world.Mobiles.TryGetValue(Current, out Mobile target) &&
                Math.Max(Math.Abs(target.X - player.X), Math.Abs(target.Y - player.Y)) <= UseRangeTiles)
            {
                best = Current;
            }

            if (best != 0 && world.Mobiles.ContainsKey(best))
            {
                TalkTo?.Invoke(best);
            }
            else if (best != 0)
            {
                world.Use(best);
            }
        }

        private void AttackTarget()
        {
            WorldState world = View.World;
            if (Current == 0 || Current == world.Player.Serial || !world.Mobiles.ContainsKey(Current))
            {
                Cycle(1); // nothing to attack yet: pick the nearest hostile first
                if (Current == 0 || !world.Mobiles.ContainsKey(Current))
                {
                    return;
                }
            }

            if (!world.WarMode)
            {
                world.SetWarMode(true);
            }

            world.Attack(Current);
        }
    }
}
