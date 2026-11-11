using FishNet.Object;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace UOGame
{
    /// <summary>
    /// What a player looks like, on clients only: the humanoid model under the graphical object, animated from how
    /// fast it actually moves (so other players walk and run the same way as your own). Speeds map onto the
    /// humanoid animator's Speed parameter: 0 idle, 1 walk, 2 run.
    /// </summary>
    public sealed class PlayerAppearance : NetworkBehaviour
    {
        [SerializeField] private GameObject _model;
        [SerializeField] private RuntimeAnimatorController _animator;
        [SerializeField] private Transform _graphic;

        /// <summary>Client: what holds the character's model (the paperdoll films a copy of it).</summary>
        public Transform Graphic => _graphic;
        /// <summary>Worn while dead: the player is a ghost until a healer brings them back.</summary>
        [SerializeField] private Material _ghost;
        /// <summary>Creature models, for the mount a rider sits on.</summary>
        [SerializeField] private UOClient.CreatureCatalog _catalog;

        private GameObject _mount;
        private Animator _mountAnim;
        /// <summary>How high the rider sits: the mount's back.</summary>
        private float _saddle;

        private Renderer[] _renderers;
        private Material[][] _living;

        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private Animator _anim;
        private Vector3 _lastPosition;
        private float _speed;

        private GameObject _built;
        private bool _hooked;
        private UOClient.GargoyleWings _wings;
        private PlayerIdentity _identity;
        private float _climbRate, _lean, _accel, _brake;
        private UOClient.GargoyleFlightPose _pose;

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (_graphic == null)
            {
                return;
            }

            // Paid art by address when the prefab holds none (Docs/SecureArt.md).
            _model = global::UOClient.Content.SecureArt.Resolve(_model, global::UOClient.Content.SecureArtAddresses.HumanModel);
            _animator = global::UOClient.Content.SecureArt.Resolve(_animator, global::UOClient.Content.SecureArtAddresses.HumanAnimator);
            _catalog = global::UOClient.Content.SecureArt.Resolve(_catalog, global::UOClient.Content.SecureArtAddresses.CreatureCatalog);

            var identity = GetComponent<PlayerIdentity>();
            var combat = GetComponent<UOGame.Combat.Combatant>();
            if (!_hooked)
            {
                _hooked = true;
                identity.Look.OnChange += (previous, next, asServer) =>
                {
                    // A host's own client may only hear the server-side change, so rebuild there too (once: the second
                    // call sees the same look).
                    if ((!asServer || IsClientStarted) && !Looks.Same(previous, next) && !Looks.Same(_builtLook, next))
                    {
                        Build(next);
                    }
                };

                identity.MountKind.OnChange += (previous, next, asServer) => BuildMount();
                identity.BodyOverride.OnChange += (previous, next, asServer) => Build(identity.Look.Value);
                identity.MountSeed.OnChange += (previous, next, asServer) => BuildMount();
                if (combat != null)
                {
                    combat.Dead.OnChange += (previous, next, asServer) =>
                    {
                        if (!asServer)
                        {
                            ShowGhost(next);
                        }
                    };
                }
            }

            Build(identity.Look.Value);
            BuildMount();
        }

        /// <summary>The creature being ridden, under the character, who sits on its back (raised by its height).</summary>
        private void BuildMount()
        {
            var identity = GetComponent<PlayerIdentity>();
            if (_mount != null)
            {
                Destroy(_mount);
                _mount = null;
                _mountAnim = null;
            }

            _saddle = 0f;
            Content.CreatureDef def = identity.Mounted ? Server.SpawnService.Content?.Kind(identity.MountKind.Value - 1) : null;
            if (def != null && _graphic != null)
            {
                Content.GameContent.Variant variant = Server.SpawnService.Content.Resolve(def, identity.MountSeed.Value);
                var holder = new GameObject("Mount").transform;
                holder.SetParent(_graphic, false);
                _mount = holder.gameObject;
                ActorModel.Build(variant.Body, holder, _catalog, null, null, variant.Look, out _mountAnim);
                // The rider sits a little below the top of the mount's body (its back, not its head).
                _saddle = ActorModel.Bounds(_mount, out Bounds b) ? Mathf.Max(0.4f, (b.max.y - _graphic.position.y) * 0.62f) : 1f;
            }

            if (_built != null)
            {
                _built.transform.localPosition = new Vector3(0f, _saddle, 0f);
            }
        }

        /// <summary>The character as they look: dressed from their look, or the plain human until it arrives.</summary>
        private Look _builtLook;

        private void Build(Look look)
        {
            _builtLook = look;
            if (_built != null)
            {
                Destroy(_built);
            }

            ushort borrowed = GetComponent<PlayerIdentity>().BodyOverride.Value;
            _built = borrowed != 0 && borrowed != 400 && borrowed != 401
                ? ActorModel.Build(borrowed, _graphic, _catalog, _model, _animator, default(Look), out _anim)
                : ActorModel.Build(borrowed != 0 ? borrowed : look.Female ? 401 : 400, _graphic, null, _model, _animator, look, out _anim);
            if (_anim == null)
            {
                _anim = _built.AddComponent<Animator>();
                _anim.runtimeAnimatorController = _animator;
            }

            _anim.applyRootMotion = false;
            _wings = _built.GetComponent<UOClient.GargoyleWings>();
            BuildCarriedLight(look);
            _built.transform.localPosition = new Vector3(0f, _saddle, 0f);
            _lastPosition = _graphic.position;
            _renderers = _built.GetComponentsInChildren<Renderer>(true);
            _living = new Material[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++)
            {
                _living[i] = _renderers[i].sharedMaterials;
            }

            var combat = GetComponent<UOGame.Combat.Combatant>();
            ShowGhost(combat != null && combat.Dead.Value);
        }

        private void ShowGhost(bool ghost)
        {
            if (_renderers == null || _ghost == null)
            {
                return;
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null)
                {
                    continue;
                }

                if (ghost)
                {
                    var ghostly = new Material[_living[i].Length];
                    for (int m = 0; m < ghostly.Length; m++)
                    {
                        ghostly[m] = _ghost;
                    }

                    _renderers[i].sharedMaterials = ghostly;
                }
                else
                {
                    _renderers[i].sharedMaterials = _living[i];
                }
            }
        }

        private bool _shownHidden;

        /// <summary>Unseen (Invisibility): others don't draw them; the player sees themself as a ghost.</summary>
        private void ShowHidden()
        {
            var combat = GetComponent<UOGame.Combat.Combatant>();
            bool hidden = combat != null && (combat.Hidden.Value || combat.AdminMode.Value && (IsOwner || !Server.AdminService.IsAdmin)); // admins see each other at work
            if (hidden == _shownHidden || _renderers == null)
            {
                return;
            }

            _shownHidden = hidden;
            if (IsOwner)
            {
                ShowGhost(hidden || combat.Dead.Value);
                return;
            }

            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = !hidden;
            }
        }

        private void LateUpdate()
        {
            ShowHidden();
            if (_anim == null || Time.deltaTime <= 0f)
            {
                return;
            }

            Vector3 moved = _graphic.position - _lastPosition;
            _lastPosition = _graphic.position;
            _climbRate = Mathf.Lerp(_climbRate, moved.y / Time.deltaTime, 1f - Mathf.Exp(-8f * Time.deltaTime));
            moved.y = 0f;
            Boat deck = Boat.Under(_graphic.position);
            bool onMovingDeck = deck != null && deck.Moving;
            if (onMovingDeck)
            {
                double now = deck.Now, frame = Time.deltaTime / Mathf.Max(0.0001f, (float)deck.TimeManager.TickDelta);
                moved -= deck.Carry(_graphic.position, now - frame, now).Position - _graphic.position; // the ship's move isn't walking
            }

            float speed = moved.magnitude / Time.deltaTime;
            float previousSpeed = _speed;
            _speed = Mathf.Lerp(_speed, speed, 1f - Mathf.Exp(-12f * Time.deltaTime));
            _accel = Mathf.Lerp(_accel, (_speed - previousSpeed) / Time.deltaTime, 1f - Mathf.Exp(-6f * Time.deltaTime));
            float parameter = _speed < 0.2f ? 0f
                : _speed <= PlayerMotor.WalkSpeed ? _speed / PlayerMotor.WalkSpeed
                : 1f + Mathf.Clamp01((_speed - PlayerMotor.WalkSpeed) / (PlayerMotor.RunSpeed - PlayerMotor.WalkSpeed));
            if (_mount != null)
            {
                // Riding: the mount walks and runs (its own clips' scale: 3 m/s a walk), the rider sits still.
                float mounted = _speed < 0.2f ? 0f : _speed <= 6f ? _speed / 6f : 1f + Mathf.Clamp01((_speed - 6f) / 8f);
                _mountAnim?.SetFloat(SpeedParameter, mounted);
                parameter = 0f;
            }

            // Held poses: sitting plays the seated pose; at the helm the legs stay still; on a moving deck small
            // drifts (the ship's motion not quite cancelled) don't start the walk.
            _identity = _identity != null ? _identity : GetComponent<PlayerIdentity>();
            byte pose = _identity != null ? _identity.Pose.Value : PlayerIdentity.Standing;
            HoldPose(pose == PlayerIdentity.Sitting ? SitClip : null);
            if (pose == PlayerIdentity.Steering || onMovingDeck && _speed < 0.9f)
            {
                parameter = 0f;
            }

            bool airborne = _wings != null && _identity != null && _identity.Flying.Value;
            _anim.SetFloat(SpeedParameter, airborne ? 0f : parameter);
            UpdateCarriedLight();
            FlyingLook();
        }

        // ------------------------------------------------------------------ a carried light (torch, lantern, candle)

        private Light _carried;
        private float _carriedRange, _carriedFlicker;
        private readonly float _flickerSeed = (float)(new System.Random().NextDouble() * 100.0);

        /// <summary>A torch, lantern or candle in hand (or a lit helm) lights the dark round its bearer, for everyone.</summary>
        private void BuildCarriedLight(Look look)
        {
            int radius = 0;
            bool flame = false;
            foreach (Gear g in look.Gear ?? System.Array.Empty<Gear>())
            {
                Items.ItemDef def = Server.ItemService.Catalog?.Get(g.Item);
                if (def != null && def.LightRadius > radius)
                {
                    radius = def.LightRadius;
                    flame = def.Type.IndexOf("Lantern", System.StringComparison.OrdinalIgnoreCase) < 0; // a lantern's glass steadies it
                }
            }

            if (radius == 0)
            {
                if (_carried != null)
                {
                    Destroy(_carried.gameObject);
                    _carried = null;
                }

                return;
            }

            if (_carried == null)
            {
                var go = new GameObject("Carried light");
                _carried = go.AddComponent<Light>();
                _carried.type = LightType.Point;
                _carried.shadows = LightShadows.None;
                _carried.color = new Color(1f, 0.72f, 0.42f);
            }

            // Held at the left hand (the off hand), or at the chest when the body has no such bone.
            Transform hand = _anim != null && _anim.isHuman ? _anim.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            _carried.transform.SetParent(hand != null ? hand : _graphic, false);
            _carried.transform.localPosition = hand != null ? Vector3.zero : new Vector3(-0.3f, 1.3f, 0.25f);
            _carriedRange = radius / 30f; // UO's 150/225/300: 5, 7.5 and 10 m
            _carriedFlicker = flame ? 1f : 0.15f;
        }

        /// <summary>Its glow: strong in the dark, faint by day; a flame flickers.</summary>
        private void UpdateCarriedLight()
        {
            if (_carried == null)
            {
                return;
            }

            float dark = 1f - DayNight.Daylight * 0.85f;
            float flicker = 1f + (Mathf.PerlinNoise(Time.time * 7f, _flickerSeed) - 0.5f) * 0.35f * _carriedFlicker;
            _carried.range = _carriedRange * (1f + (flicker - 1f) * 0.3f);
            _carried.intensity = 2.2f * dark * flicker;
            _carried.enabled = _built != null && _built.activeInHierarchy;
        }

        private static AnimationClip _sitClip;
        private static bool _sitLoaded;

        /// <summary>The seated pose (Resources/Poses/Sit, made by Tools/Game/MakePoses.eval.cs).</summary>
        private static AnimationClip SitClip
        {
            get
            {
                if (!_sitLoaded)
                {
                    _sitLoaded = true;
                    _sitClip = Resources.Load<AnimationClip>("Poses/Sit");
                }

                return _sitClip;
            }
        }

        private PlayableGraph _poseGraph;
        private AnimationClip _poseClip;
        private Animator _poseAnimator;

        /// <summary>Play a held pose over the walk and idle (null: back to them).</summary>
        private void HoldPose(AnimationClip clip)
        {
            if (clip == _poseClip && _poseAnimator == _anim)
            {
                return;
            }

            if (_poseGraph.IsValid())
            {
                _poseGraph.Destroy();
            }

            _poseClip = clip;
            _poseAnimator = _anim;
            if (clip != null && _anim != null)
            {
                AnimationPlayableUtilities.PlayClip(_anim, clip, out _poseGraph);
            }
        }

        private void OnDisable()
        {
            if (_poseGraph.IsValid())
            {
                _poseGraph.Destroy();
            }

            _poseClip = null;
        }

        /// <summary>
        /// A flying gargoyle's wings open and beat when hovering or climbing and hold level when cruising; the body leans into the flight.
        /// </summary>
        private void FlyingLook()
        {
            if (_wings == null)
            {
                return;
            }

            _identity = _identity != null ? _identity : GetComponent<PlayerIdentity>();
            bool flying = _identity != null && _identity.Flying.Value;
            bool landing = flying && _identity.Landing.Value;
            // Folds a moment after touchdown, once the legs have taken the weight.
            _wings.Spread = flying || (_pose != null && _pose.LandingTime < 0.3f);
            // Climbing beats hard; dropping straight down beats to brake; moving forward only ticks over; a dive at an angle glides.
            bool moving = _speed >= 2f;
            bool climbing = _climbRate > 0.5f;
            bool falling = _climbRate < -1f;
            bool glide = falling && moving;
            bool launching = _pose != null && _pose.TakeoffTime < 0.30f; // wings held raised while the legs load
            bool accelerating = flying && _accel > 2f;
            bool braking = flying && ((_accel < -3f && _speed > 1.5f) || landing);
            bool fast = _speed > 7.5f && !accelerating;
            _brake = Mathf.MoveTowards(_brake, braking ? 1f : 0f, Time.deltaTime * 3f);
            // At full cruise the beat eases off into stretches of gliding.
            bool cruiseGlide = fast && Mathf.Repeat(Time.time, 3.2f) > 2.0f;
            _wings.Flap = flying && !glide && !launching && !cruiseGlide;
            _wings.Brake = _brake;
            _wings.Hover = !moving && !climbing && !falling && !braking && !landing;
            _wings.Rate = !flying ? 1f : (_pose != null && _pose.TakeoffTime < 0.9f) ? 1.8f : braking ? 1.1f : accelerating ? 1.4f : climbing ? 1.5f : falling ? 1.2f : fast ? 0.8f : moving ? 1f : 1f;
            // A flying gargoyle pitches forward with speed (prone at a cruise), more still in a dive, and hangs upright when hovering.
            float lean = flying ? Mathf.Clamp(40f + _speed * 1.5f, 0f, 55f) - Mathf.Clamp(_climbRate * 2f, 0f, 14f) + (landing ? -22f : falling && !moving ? -14f : Mathf.Clamp(-_climbRate * 4f, 0f, 22f)) + Mathf.Clamp(_accel * 1.5f, -12f, 10f) : 0f;
            _lean = Mathf.Lerp(_lean, lean, 1f - Mathf.Exp(-6f * Time.deltaTime));
            _wings.Lean = _lean;
            _pose = _pose != null ? _pose : _built.GetComponent<UOClient.GargoyleFlightPose>();
            float CrouchDrop = _pose != null ? 0.34f * _pose.Crouch : 0f;
            // Pitched about the pelvis, not the feet.
            Quaternion target = Quaternion.Euler(_lean, 0f, 0f);
            _built.transform.localRotation = target;
            _built.transform.localPosition = UOClient.GargoyleFlightPose.Pivot - target * UOClient.GargoyleFlightPose.Pivot
                + Vector3.up * ((flying && _wings.Beating ? 0.07f * Mathf.Sin(2f * Mathf.PI * _wings.Phase) : 0f) - CrouchDrop); // up on the downstroke, down on the upstroke
            if (_pose != null)
            {
                _pose.Fly = flying ? 1f : 0f;
                _pose.Cruise = Mathf.Clamp01(_speed / 8f);
                _pose.Phase = _wings.Phase;
                _pose.Beating = _wings.Beating;
                _pose.Brake = Mathf.Max(_brake, falling && !moving ? 1f : 0f);
            }
        }
    }
}
