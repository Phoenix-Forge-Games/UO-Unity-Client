using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using UnityEngine;
using UOClient;

namespace UOGame
{
    /// <summary>
    /// Free movement, decided by the server. The owning client sends its input every tick and moves at once with the
    /// same code (prediction); the server runs that input against the same map collision and sends back where the
    /// character really is, which the client adopts and replays its newer input on top of (reconcile). Other players
    /// are smoothed between the server's states. Runs by default; hold Run (Shift) to walk; Jump to jump.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : TickNetworkBehaviour
    {
        public struct MoveInput : IReplicateData
        {
            /// <summary>World-space move direction (x east, y = Unity +z north), length up to 1.</summary>
            public Vector2 Move;
            /// <summary>Which way the character faces (degrees about Y).</summary>
            public float Yaw;
            public bool Walk;
            public bool Jump;
            /// <summary>Flying only: -1 down (Descend), 0 level, +1 up (Jump held).</summary>
            public float Climb;
            private uint _tick;

            public MoveInput(Vector2 move, float yaw, bool walk, bool jump, float climb)
            {
                Move = move;
                Yaw = yaw;
                Walk = walk;
                Jump = jump;
                Climb = climb;
                _tick = 0;
            }

            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        public struct MoveState : IReconcileData
        {
            public Vector3 Position;
            public float Yaw;
            public float VerticalVelocity;
            private uint _tick;

            public MoveState(Vector3 position, float yaw, float verticalVelocity)
            {
                Position = position;
                Yaw = yaw;
                VerticalVelocity = verticalVelocity;
                _tick = 0;
            }

            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        public const float RunSpeed = 7f;
        public const float WalkSpeed = 3f;
        private const float JumpSpeed = 5.5f;
        private const float Gravity = -20f;
        private const float MaxFallSpeed = -45f;
        /// <summary>Flying (gargoyles): speeds in m/s, and how fast the vertical speed follows the input.</summary>
        public const float FlySpeed = 9f, FlyWalkSpeed = 4f;
        private const float ClimbSpeed = 5f, DescendSpeed = 6f, ClimbAcceleration = 25f;
        /// <summary>Fly hovers this far above the ground; past HighMargin more it counts as airborne. The ceiling is metres above the ground below.</summary>
        /// <summary>Seconds crouching before the spring up, and the spring itself (the gargoyle leaves the ground at the end).</summary>
        public const float TakeoffCrouch = 0.30f, TakeoffSpring = 0.22f;
        public const float HoverHeight = 0.6f, HighMargin = 1.2f, FlightCeiling = 40f;

        /// <summary>Server: falls longer than the safe height hurt when you land (World.json Fall): a share of your
        /// most hits for each metre beyond it. Flying, swimming and landing in water don't count.</summary>
        private bool _safeLanding;
        private float _standAsked;

        /// <summary>Server: the next landing doesn't hurt (an admin teleport drops someone onto ground still loading).</summary>
        public void LandSafely()
        {
            _fallTop = float.NaN;
            _safeLanding = true;
        }

        private void Falling(bool grounded, bool afloat)
        {
            float y = transform.position.y;
            if (afloat)
            {
                _fallTop = float.NaN;
                return;
            }

            if (!grounded)
            {
                _fallTop = float.IsNaN(_fallTop) ? y : Mathf.Max(_fallTop, y);
                return;
            }

            if (!float.IsNaN(_fallTop))
            {
                float drop = _safeLanding ? 0f : _fallTop - y;
                _fallTop = float.NaN;
                _safeLanding = false;
                if (IsServerStarted && drop > Content.WorldRules.FallSafe && _combat != null && !_combat.Dead.Value)
                {
                    int damage = Mathf.CeilToInt(_combat.HitsMax.Value * (drop - Content.WorldRules.FallSafe) * Content.WorldRules.FallPercentPerMetre / 100f);
                    _combat.TakeDamage(null, damage); // nothing resists the ground
                    Server.ItemService.Instance?.Tell(Owner, $"You fall {drop:0} metres and take {damage} damage.");
                }
            }
        }

        /// <summary>The local player's motor, once it's spawned.</summary>
        public static PlayerMotor Local { get; private set; }

        private CharacterController _controller;
        private UOGame.Combat.Combatant _combat;
        private PlayerIdentity _identity;
        private float _verticalVelocity;
        private bool _jumpQueued;
        private float _yaw;
        private MoveInput _lastTicked;
        private bool _wasFlying;
        private bool _flyHigh;
        private float _flyTime;
        /// <summary>Server: the highest point of the fall now under way (NaN while on the ground).</summary>
        private float _fallTop = float.NaN;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _combat = GetComponent<UOGame.Combat.Combatant>();
            _identity = GetComponent<PlayerIdentity>();
            SetTickCallbacks(TickCallback.Tick);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            GameWorld.Follow(transform); // the server keeps ground under every player
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            GameWorld.Unfollow(transform);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner)
            {
                Local = this;
                GameWorld.Follow(transform);
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (Local == this)
            {
                Local = null;
                GameWorld.Unfollow(transform);
            }
        }

        private void Update()
        {
            if (IsOwner && !GameInput.Typing && GameInput.Controls.Gameplay.Jump.WasPressedThisFrame())
            {
                _jumpQueued = true;
            }

            if (IsServerStarted)
            {
                CatchFall();
            }

            if (IsServerStarted || IsOwner)
            {
                GhostDoors(); // both sides move the body, so both must agree it passes the door
            }
        }

        // ------------------------------------------------------------------ ghosts pass through doors

        private readonly System.Collections.Generic.List<Collider> _ghostIgnored = new System.Collections.Generic.List<Collider>();
        private float _nextGhostDoors;

        /// <summary>
        /// A ghost (dead) walks through closed doors, as in UO, so they can always reach a healer or shrine: the doors
        /// near them stop blocking their body. Alive again, every door blocks again.
        /// </summary>
        private void GhostDoors()
        {
            bool ghost = _combat != null && _combat.Dead.Value;
            if (!ghost)
            {
                if (_ghostIgnored.Count > 0)
                {
                    foreach (Collider c in _ghostIgnored)
                    {
                        if (c != null && _controller != null)
                        {
                            Physics.IgnoreCollision(_controller, c, false);
                        }
                    }

                    _ghostIgnored.Clear();
                }

                return;
            }

            if (Time.time < _nextGhostDoors || _controller == null)
            {
                return;
            }

            _nextGhostDoors = Time.time + 0.3f;
            foreach (UOClient.Buildings.BuildingDoor door in UOClient.Buildings.BuildingDoor.All)
            {
                if (door == null || (door.transform.position - transform.position).sqrMagnitude > 10f * 10f)
                {
                    continue;
                }

                foreach (Collider c in door.GetComponentsInChildren<Collider>(true))
                {
                    if (!_ghostIgnored.Contains(c))
                    {
                        Physics.IgnoreCollision(_controller, c, true);
                        _ghostIgnored.Add(c);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ the safety net

        /// <summary>
        /// Below the lowest ground the world can have (UO's lowest Z, -128, is -16 m) with room to spare: anyone down
        /// here has fallen out of the world.
        /// </summary>
        public const float WorldFloor = -40f;

        private Vector3? _lastSafe;
        private float _nextSafe, _nextRescue;

        /// <summary>
        /// Server: remember where they last stood on solid ground; if they fall out of the world (a hole, a gap in a
        /// floor), put them back there, or on the ground above where they are when there's nowhere remembered (a
        /// character saved under the world).
        /// </summary>
        private void CatchFall()
        {
            Vector3 at = transform.position;
            bool swimming = _identity != null && _identity.Swimming.Value;
            bool flying = _identity != null && _identity.Flying.Value;
            if (_controller != null && _controller.isGrounded && !swimming && !flying && at.y > WorldFloor &&
                Time.time >= _nextSafe && GameWorld.IsReadyAt(at))
            {
                _lastSafe = at;
                _nextSafe = Time.time + 0.5f;
            }

            if (at.y >= WorldFloor || Time.time < _nextRescue || _combat == null || Server.AdminService.Instance == null)
            {
                return;
            }

            _nextRescue = Time.time + 3f;
            Debug.Log($"[Game] {name} fell out of the world at {at:0}; putting them back {(_lastSafe.HasValue ? "where they last stood" : "on the ground above")}");
            Server.AdminService.Instance.Rescue(_combat, _lastSafe ?? new Vector3(at.x, 0f, at.z), exact: _lastSafe.HasValue);
        }

        protected override void TimeManager_OnTick()
        {
            PerformReplicate(BuildInput());
            CreateReconcile();
        }

        private MoveInput BuildInput()
        {
            if (!IsOwner)
            {
                return default;
            }

            Vector2 raw = GameInput.Typing ? Vector2.zero : GameInput.Controls.Gameplay.Move.ReadValue<Vector2>();
            // Camera-relative: forward is where the camera looks.
            float cameraYaw = GameCamera.Yaw;
            Vector3 world = Quaternion.Euler(0f, cameraYaw, 0f) * new Vector3(raw.x, 0f, raw.y);
            var move = new Vector2(world.x, world.z);
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            if (move.sqrMagnitude > 0.0001f)
            {
                _yaw = Mathf.Atan2(move.x, move.y) * Mathf.Rad2Deg;
            }

            bool walk = !GameInput.Typing && GameInput.Controls.Gameplay.Run.IsPressed();
            float climb = GameInput.Typing ? 0f : (GameInput.Controls.Gameplay.Jump.IsPressed() ? 1f : 0f) - (GameInput.Controls.Gameplay.Descend.IsPressed() ? 1f : 0f);
            var input = new MoveInput(move, _yaw, walk, _jumpQueued, climb);
            _jumpQueued = false;
            return input;
        }

        [Replicate]
        private void PerformReplicate(MoveInput input, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
            if (!IsServerStarted && !IsOwner)
            {
                // Someone else's character on this client: replay their last known input one tick ahead at most.
                if (state.ContainsTicked())
                {
                    _lastTicked = input;
                }
                else if (state.IsFuture())
                {
                    input = _lastTicked;
                    input.Jump = false;
                }
            }

            float delta = (float)TimeManager.TickDelta;
            if (!GameWorld.IsReadyAt(transform.position))
            {
                // The ground here isn't loaded yet: wait rather than fall through it.
                _verticalVelocity = 0f;
                _fallTop = float.NaN;
                return;
            }

            if (_combat != null && _combat.Paralyzed.Value)
            {
                input.Move = Vector2.zero; // held fast by paralysis
                input.Jump = false;
            }

            bool grounded = _controller.isGrounded;
            bool flying = _identity != null && _identity.Flying.Value;
            Boat deck = flying ? null : Boat.Under(transform.position);
            byte pose = _identity != null ? _identity.Pose.Value : PlayerIdentity.Standing;
            if (IsServerStarted && (pose == PlayerIdentity.Sitting || pose == PlayerIdentity.Steering) &&
                (deck == null || pose == PlayerIdentity.Steering && deck.Helm.Value != _identity.CharacterId + 1))
            {
                _identity.Pose.Value = pose = PlayerIdentity.Standing; // off the ship, or no longer at the helm
            }

            if (pose == PlayerIdentity.Sitting)
            {
                // Sitting: held still; a movement key asks to stand up (and you move once you're up).
                if (IsOwner && deck != null && input.Move.sqrMagnitude > 0.04f && Time.unscaledTime >= _standAsked)
                {
                    _standAsked = Time.unscaledTime + 0.5f;
                    ClientManager.Broadcast(new Net.BoatCommand { Boat = deck.ObjectId, Order = (byte)Server.BoatService.Order.Stand });
                }

                input.Move = Vector2.zero;
                input.Jump = false;
            }

            if (deck != null && _identity != null && deck.Helm.Value == _identity.CharacterId + 1)
            {
                // At the helm: the movement keys steer the ship, not you.
                if (IsServerStarted)
                {
                    Server.BoatService.Instance?.HelmInput(deck, input.Move);
                }

                input.Move = Vector2.zero;
                input.Jump = false;
            }
            // Deep water: more than the swim depth of water over your feet.
            float surface = 0f;
            bool swimming = !flying && UOClient.WaterMap.TryGet(transform.position, out surface) &&
                            surface - transform.position.y > Content.WorldRules.SwimDepth - 0.05f;
            if (IsServerStarted && _identity != null && _identity.Swimming.Value != swimming)
            {
                _identity.Swimming.Value = swimming;
            }
            if (flying && IsServerStarted && (!_identity.IsGargoyle || _identity.Mounted || (_combat != null && _combat.Dead.Value)))
            {
                _identity.Flying.Value = flying = false; // only living, unmounted gargoyles stay in the air
                _identity.Landing.Value = false;
            }

            if (flying && !_wasFlying)
            {
                _flyHigh = false;
                _flyTime = 0f;
            }

            _wasFlying = flying;
            if (flying)
            {
                // Take-off: crouch on the spot, then spring up (the pose in GargoyleFlightPose follows these times).
                _flyTime += delta;
                if (_flyTime < TakeoffCrouch)
                {
                    input.Move = Vector2.zero;
                    input.Climb = 0f;
                }

                bool landing = _identity.Landing.Value;
                if (landing)
                {
                    input.Climb = 0f;
                }

                float hoverNow = HoverHeight * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TakeoffCrouch, TakeoffCrouch + TakeoffSpring, _flyTime));
                // Fly hovers just above the ground as if walking. Climbing leaves the ground; coming back down to it ends flight.
                float height = HeightAboveGround();
                if (height > HoverHeight + HighMargin)
                {
                    _flyHigh = true;
                }

                float wanted;
                if (input.Climb > 0f)
                {
                    wanted = height >= FlightCeiling ? 0f : ClimbSpeed * Mathf.Clamp01((FlightCeiling - height) / 3f + 0.05f);
                }
                else if (landing)
                {
                    wanted = -Mathf.Clamp(height * 1.1f, 0.7f, 4f); // slows as the ground nears
                }
                else if (input.Climb < 0f)
                {
                    wanted = -DescendSpeed;
                }
                else if (height <= HoverHeight + HighMargin)
                {
                    wanted = Mathf.Clamp((hoverNow - height) * 6f, -DescendSpeed, ClimbSpeed); // hold the hover height over uneven ground
                }
                else
                {
                    wanted = height > FlightCeiling ? -DescendSpeed : 0f; // hang in the air
                }

                if (height >= FlightCeiling + 20f && wanted < 0f)
                {
                    wanted = 0f; // nothing under you (open water has no ground to land on): stay up rather than fall out of the world
                }

                _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, wanted, ClimbAcceleration * delta);
                if ((_flyHigh || landing) && height <= HoverHeight + 0.15f && IsServerStarted)
                {
                    _identity.Flying.Value = false; // came back to the ground
                    _identity.Landing.Value = false;
                }
            }
            else if (swimming)
            {
                // Afloat: held with your head out of the water, whatever the bed does below.
                float wantedY = surface - Content.WorldRules.SwimDepth;
                _verticalVelocity = Mathf.Clamp((wantedY - transform.position.y) * 6f, -Content.WorldRules.SwimRise, Content.WorldRules.SwimRise);
                _fallTop = float.NaN; // water breaks a fall
            }
            else
            {
                if (grounded && _verticalVelocity < 0f)
                {
                    _verticalVelocity = -2f; // keeps the controller pressed to slopes and steps
                }

                if (input.Jump && grounded)
                {
                    _verticalVelocity = JumpSpeed;
                }

                _verticalVelocity = Mathf.Max(_verticalVelocity + Gravity * delta, MaxFallSpeed);
            }

            Falling(grounded, flying || swimming);

            // Riding is faster (Pets.json Mount; UO doubles both paces).
            bool mounted = _identity != null && (_identity.Mounted || _identity.Swift.Value);
            // Leaving the ground it builds up speed; coming in to land it sheds it.
            float flyFactor = Mathf.Lerp(0.3f, 1f, Mathf.InverseLerp(TakeoffCrouch, TakeoffCrouch + 0.8f, _flyTime));
            if (flying && _identity.Landing.Value)
            {
                flyFactor *= Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(HeightAboveGround() / 5f));
            }

            float speed = flying ? (input.Walk ? FlyWalkSpeed : FlySpeed) * flyFactor
                : swimming ? (input.Walk ? Content.WorldRules.SwimWalk : Content.WorldRules.SwimRun)
                : input.Walk ? WalkSpeed * (mounted ? Content.PetRules.MountWalk : 1f) : RunSpeed * (mounted ? Content.PetRules.MountRun : 1f);
            Vector3 horizontal = new Vector3(input.Move.x, 0f, input.Move.y) * speed;
            if (deck != null)
            {
                // Riding a ship, as if parented to it: the deck's own move this tick, turning with it too.
                double now = deck.Now;
                (Vector3 carried, float turned) = deck.Carry(transform.position, now - 1, now);
                horizontal += (carried - transform.position) / delta;
                if (Mathf.Abs(turned) > 0.0001f)
                {
                    transform.Rotate(0f, turned, 0f);
                }
            }
            _controller.Move((horizontal + Vector3.up * _verticalVelocity) * delta);
            if (input.Move.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.Euler(0f, input.Yaw, 0f);
                // (on a ship you face where you walk; standing still you turn with it)
            }
        }

        /// <summary>How far the feet are above whatever is under them (up to a bit past the ceiling).</summary>
        private float HeightAboveGround()
        {
            Vector3 from = transform.position + Vector3.up * 0.1f;
            return Physics.Raycast(from, Vector3.down, out RaycastHit hit, FlightCeiling + 20f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.distance - 0.1f
                : FlightCeiling + 20f;
        }

        public override void CreateReconcile()
        {
            PerformReconcile(new MoveState(transform.position, transform.eulerAngles.y, _verticalVelocity));
        }

        [Reconcile]
        private void PerformReconcile(MoveState state, Channel channel = Channel.Unreliable)
        {
            _verticalVelocity = state.VerticalVelocity;
            // The controller has to be off while it's moved, or its physics stays where it was until the next move.
            _controller.enabled = false;
            transform.position = state.Position;
            transform.rotation = Quaternion.Euler(0f, state.Yaw, 0f);
            _controller.enabled = true;
        }
    }
}
