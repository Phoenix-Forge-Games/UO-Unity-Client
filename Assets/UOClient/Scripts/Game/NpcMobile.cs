using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UOGame.Combat;
using UOGame.Content;

namespace UOGame
{
    /// <summary>
    /// A creature or NPC, run by the server. Only its kind (its place in our creature content) and seed cross the
    /// network: server and clients hold the same content, and turn kind and seed into the same name, body and outfit.
    /// The server also knows where it lives and what it's doing. The server moves it with its character controller against the map's collision; clients see
    /// it smoothed (NetworkTransform). Hits and fighting are its <see cref="Combatant"/>.
    /// <para>
    /// AI: it wanders near home. A hostile creature attacks a player who comes within its perception, chases them into
    /// reach and fights; if the fight drags it past its leash (too far from home), it gives up, walks home and heals.
    /// A dead creature leaves its corpse and is removed (its spawn point refills it later).
    /// </para>
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(Combatant))]
    public sealed class NpcMobile : NetworkBehaviour, INamed
    {
        /// <summary>Its kind: the creature's number in our content.</summary>
        public readonly SyncVar<ushort> Kind = new SyncVar<ushort>();
        /// <summary>Which one of its kind: picks its name, body and outfit (the same everywhere).</summary>
        public readonly SyncVar<int> Seed = new SyncVar<int>();
        /// <summary>Mirror Image: the caster's look and name (else its kind's).</summary>
        public readonly SyncVar<Look> Mirror = new SyncVar<Look>();
        public readonly SyncVar<string> MirrorName = new SyncVar<string>();

        /// <summary>Server: look and be named like this player (Mirror Image).</summary>
        public void SetMirror(Look look, string name)
        {
            MirrorName.Value = name;
            Mirror.Value = look;
            Resolve();
        }

        // ------------------------------------------------------------------ as a pet (Pets.json)

        public enum PetOrder : byte { Follow, Stay, Guard, Attack, Stop }

        /// <summary>The object id of its master's body plus one (a body can have object id 0), 0 when wild.</summary>
        public readonly SyncVar<int> MasterId = new SyncVar<int>();
        /// <summary>The name its owner gave it ("" for its own).</summary>
        public readonly SyncVar<string> PetName = new SyncVar<string>();
        public readonly SyncVar<byte> Order = new SyncVar<byte>();
        public readonly SyncVar<byte> Loyalty = new SyncVar<byte>();
        public readonly SyncVar<bool> Bonded = new SyncVar<bool>();

        public bool IsPet => MasterId.Value != 0;

        /// <summary>Is this the pet of the body with this object id?</summary>
        public bool IsPetOf(int objectId) => MasterId.Value == objectId + 1;

        /// <summary>Server: a revenant's quarry (Vengeful Spirit): it fights only this one and fades when it's gone.</summary>
        public Combatant Hunts { get; private set; }

        public void Hunt(Combatant quarry)
        {
            Hunts = quarry;
            _combat.Engage(quarry);
        }

        /// <summary>Server: follower slots it takes (-1: its kind's); raised dead take none.</summary>
        public int Slots = -1;

        /// <summary>Server: raised dead heal 1 + 1% of their hits this often (seconds; 0 none).</summary>
        public float RegenEvery;
        private float _nextRegen;

        /// <summary>Server: what it carries, rolled from its loot packs the first time anyone looks (a thief) or when it
        /// dies (its corpse gets what's left).</summary>
        public System.Collections.Generic.List<Server.AccountStore.Item> Carried { get; private set; }

        public System.Collections.Generic.List<Server.AccountStore.Item> EnsureCarried(int luck = 0)
        {
            if (Carried == null)
            {
                Carried = Server.ItemService.Instance?.RollLoot(Def, luck) ?? new System.Collections.Generic.List<Server.AccountStore.Item>();
            }

            return Carried;
        }

        /// <summary>Server: gold a townsperson has on them for beggars (-1 until first asked).</summary>
        public int Pocket { get; set; } = -1;
        /// <summary>Server: a spellcaster (a mage AI with Magery): it casts at what it fights (SpellService.CreatureThink).</summary>
        public bool Casts { get; private set; }
        /// <summary>Server: called by a spell; it vanishes when its time is up (and can be dispelled).</summary>
        public bool Summoned { get; private set; }
        private float _summonedUntil = float.PositiveInfinity;

        /// <summary>Server: a summoned creature, here for so long: on its own (blade spirits, vortexes) it fights the
        /// nearest enemy of its summoner's side; controlled ones are pets until they vanish.</summary>
        public void MakeSummoned(float seconds)
        {
            Summoned = true;
            _summonedUntil = Time.time + seconds;
            _combat.SetTeam(Combatant.Side.Player);
            _combat.Protected = false;
        }

        /// <summary>Server: gone at once (dispelled, released, or its time ran out).</summary>
        public void Unsummon()
        {
            if (IsPet)
            {
                Server.PetService.Instance?.Lost(this);
            }

            _summonedUntil = float.PositiveInfinity;
            Summoned = false;
            if (IsSpawned)
            {
                ServerManager.Despawn(NetworkObject);
            }
        }
        /// <summary>Server: its master's body.</summary>
        public Combatant Master { get; private set; }
        /// <summary>Server: the saved character that owns it now.</summary>
        public int OwnerCharacter { get; private set; }
        /// <summary>Server: characters (ids) who have owned it (UO: at most five; retaming your own always works).</summary>
        public readonly System.Collections.Generic.HashSet<int> Owners = new System.Collections.Generic.HashSet<int>();
        /// <summary>Server: when the owner first fed it (bonding starts), or MinValue.</summary>
        public System.DateTime BondingBegin = System.DateTime.MinValue;
        /// <summary>Server: its skills' share of the creature's own (taming scales them down).</summary>
        public float SkillScale = 1f;
        /// <summary>Server: a pack animal's pack.</summary>
        public readonly System.Collections.Generic.List<Server.AccountStore.Item> Pack = new System.Collections.Generic.List<Server.AccountStore.Item>();

        /// <summary>Server: it accepts a master (taming, buying, claiming from a stable).</summary>
        public void MakePet(Combatant master, int ownerCharacterId)
        {
            Master = master;
            MasterId.Value = master.ObjectId + 1;
            OwnerCharacter = ownerCharacterId;
            Owners.Add(ownerCharacterId);
            Order.Value = (byte)PetOrder.Follow;
            _goal = null;
            _returning = false;
            _removeAt = float.PositiveInfinity;
            _combat.SetTeam(Combatant.Side.Player);
            _combat.Protected = false;
        }

        /// <summary>Server: its master gives it up (or it ran off): wild again, here, and it fades out after a while.</summary>
        public void GoWild(float fadeSeconds)
        {
            Master = null;
            MasterId.Value = 0;
            OwnerCharacter = 0;
            PetName.Value = string.Empty;
            Bonded.Value = false;
            Home = transform.position;
            _combat.SetTeam(Def.Hostile ? Combatant.Side.Monster : Combatant.Side.Neutral);
            _removeAt = Time.time + fadeSeconds;
        }

        /// <summary>Server: the body it follows (after its master logs back in, a new body).</summary>
        public void Rebind(Combatant master)
        {
            Master = master;
            MasterId.Value = master != null ? master.ObjectId + 1 : 0;
        }

        /// <summary>Server: an order from its master (already past the control check).</summary>
        public void Command(PetOrder order)
        {
            Order.Value = (byte)order;
            switch (order)
            {
                case PetOrder.Attack:
                    if (Master != null && Master.Target != null && _combat.IsEnemyOf(Master.Target))
                    {
                        _combat.Engage(Master.Target);
                    }
                    else
                    {
                        Order.Value = (byte)PetOrder.Follow;
                    }

                    break;
                case PetOrder.Stop:
                case PetOrder.Stay:
                    _combat.Evade();
                    _goal = null;
                    Order.Value = (byte)PetOrder.Stay;
                    break;
                default:
                    _combat.Evade();
                    break;
            }
        }

        /// <summary>Server: put it somewhere at once (beside its master: claimed from a stable, or left far behind).</summary>
        public void PlaceNear(Vector3 at)
        {
            _controller.enabled = false;
            transform.position = at;
            _controller.enabled = true;
            _goal = null;
        }

        private const float FollowSpeed = 7.5f;
        private const float FollowGap = 2.2f;

        // ------------------------------------------------------------------ who it is (from kind and seed)

        public CreatureDef Def { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public int BodyId { get; private set; }
        public int Hue => Def?.Hue ?? 0;
        public bool Hostile => Def != null && Def.Hostile;
        public bool IsVendor => Def != null && Def.Vendor;
        /// <summary>A human's sex, face, hair and what they wear.</summary>
        public Look Look { get; private set; }

        /// <summary>Raised on clients once kind and seed are known (and whenever they change).</summary>
        public event System.Action Resolved;

        private void Resolve()
        {
            CreatureDef def = Server.SpawnService.Content?.Kind(Kind.Value);
            if (def == null)
            {
                return;
            }

            GameContent.Variant variant = Server.SpawnService.Content.Resolve(def, Seed.Value);
            Def = def;
            Name = variant.Name;
            BodyId = variant.Body;
            Look = variant.Look;
            if (Mirror.Value.IsSet)
            {
                Look = Mirror.Value;
                BodyId = Look.Gargoyle ? (Look.Female ? 667 : 666) : Look.Female ? 401 : 400;
                Name = MirrorName.Value ?? Name;
            }

            Resolved?.Invoke();
        }

        private const float WalkSpeed = 2.2f;
        private const float ChaseSpeed = 5.5f;
        private const float Gravity = -20f;
        private const float ThinkSeconds = 0.25f;
        /// <summary>How far from where a fight began it may drag a creature before it gives up (metres).</summary>
        private const float Leash = 35f;
        /// <summary>The longest a creature walks back evading before it's whole and fair game again, stuck or not.</summary>
        private const float EvadeSeconds = 20f;
        /// <summary>Where the creature was when its current fight began (it leashes from here and goes back here).</summary>
        private Vector3? _fightStart;
        private float _evadeUntil;
        /// <summary>Seconds chasing without getting any closer (can't reach them) before it gives up.</summary>
        private const float CantReachSeconds = 8f;
        private float _closest = float.MaxValue, _closingSince;

        [SerializeField] private Transform _graphic;

        // ------------------------------------------------------------------ server state

        public Vector3 Home { get; private set; }
        public float HomeRange { get; private set; }

        private CharacterController _controller;
        private Combatant _combat;
        private Vector3? _goal;
        /// <summary>Server: the NavMesh agent that finds its way (made once there's navmesh under it).</summary>
        private UnityEngine.AI.NavMeshAgent _agent;
        private Vector3 _agentGoal;
        private float _agentRetry;
        private float _nextDoorCheck;

        /// <summary>Server: a closed door just ahead gets opened (creatures go through doors, as in UO).</summary>
        private void OpenDoorAhead(Vector3 dir)
        {
            Vector3 eye = transform.position + Vector3.up * 1.1f;
            if (Physics.SphereCast(eye, 0.3f, dir, out RaycastHit hit, 1.4f + _controller.radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var door = hit.collider.GetComponentInParent<UOClient.Buildings.BuildingDoor>();
                if (door != null && door.State != UOClient.Buildings.DoorState.Open)
                {
                    Server.WorldServices.Instance?.CreatureOpens(door);
                }
            }
        }
        private float _goalSpeed;
        private float _nextThink;
        private float _idleUntil;
        private float _verticalVelocity;
        private bool _returning;
        private float _removeAt = float.PositiveInfinity;
        private System.Random _random;

        /// <summary>Server, before spawning.</summary>
        public void SetServer(CreatureDef def, Vector3 home, float homeRange, int seed)
        {
            // Pooled: forget any earlier life as someone's pet.
            Master = null;
            MasterId.Value = 0;
            OwnerCharacter = 0;
            PetName.Value = string.Empty;
            Order.Value = 0;
            Loyalty.Value = 0;
            Bonded.Value = false;
            Owners.Clear();
            Pack.Clear();
            Summoned = false;
            _summonedUntil = float.PositiveInfinity;
            SkillScale = 1f;
            BondingBegin = System.DateTime.MinValue;
            _removeAt = float.PositiveInfinity;
            Home = home;
            HomeRange = homeRange;
            Kind.Value = def.Kind;
            Seed.Value = seed;
            Mirror.Value = default;
            MirrorName.Value = null;
            Resolve();
            _random = new System.Random(seed ^ 0x5EED);

            int str = Roll(def.Str, 50);
            int dex = Roll(def.Dex, 50);
            int hits = Roll(def.Hits, 0);
            if (hits <= 0)
            {
                hits = Mathf.Max(1, str); // UO: a creature with no hits set has its strength in hits
            }

            float skill = def.WeaponSkill.y > 0f ? Mathf.Lerp(def.WeaponSkill.x, def.WeaponSkill.y, (float)_random.NextDouble()) : 30f;
            float tactics = def.Tactics.y > 0f ? Mathf.Lerp(def.Tactics.x, def.Tactics.y, (float)_random.NextDouble()) : 30f;
            float reach = Mathf.Max(1, def.RangeFight) * UOClient.WorldSpace.TileSize + 1f;
            var combat = GetComponent<Combatant>();
            combat.SetServer(def.Hostile ? Combatant.Side.Monster : Combatant.Side.Neutral, str, dex, hits, skill, tactics, def.Damage, reach);
            combat.Protected = def.Protected;
            var resists = new int[5];
            for (int e = 0; e < 5; e++)
            {
                resists[e] = def.Resists[e].y > 0f ? Mathf.RoundToInt(Mathf.Lerp(def.Resists[e].x, def.Resists[e].y, (float)_random.NextDouble())) : 0;
            }

            combat.SetBaseResists(resists);
            combat.SetDamageMix(def.DamageMix);
            int intel = Roll(def.Int, 10);
            combat.SetPools(intel, Roll(def.Mana, intel), dex); // UO: mana is its intelligence unless set
            combat.Casting = false;
            Hunts = null;
            Slots = -1;
            RegenEvery = 0f;
            Carried = null;
            Pocket = -1;
            Server.SpellService.Instance?.ResetCreature(combat);
            Casts = Server.SpellService.CastsSpells(def);
        }

        private int Roll(Vector2 range, int fallback) =>
            range.y > 0f ? Mathf.RoundToInt(Mathf.Lerp(range.x, range.y, (float)_random.NextDouble())) : fallback;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _combat = GetComponent<Combatant>();
            Kind.OnChange += (a, b, asServer) => { if (!asServer && !IsServerStarted) Resolve(); };
            Seed.OnChange += (a, b, asServer) => { if (!asServer && !IsServerStarted) Resolve(); };
            Mirror.OnChange += (a, b, asServer) => { if (!asServer && !IsServerStarted) Resolve(); };
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _combat.Died += (self, killer) =>
            {
                if (IsPet && Bonded.Value)
                {
                    return; // a bonded pet stays, a ghost following its master, until brought back
                }

                if (IsPet)
                {
                    Server.PetService.Instance?.Lost(this);
                    // A pet leaves only what it carried (a pack animal's pack), not loot.
                    Server.DeathService.Instance?.LeaveCreatureCorpse(this, new System.Collections.Generic.List<Server.AccountStore.Item>(Pack));
                    Pack.Clear();
                    _removeAt = Time.time;
                    return;
                }

                Server.SpellService.Instance?.AwardKill(killer, Def); // fame and karma
                Server.QuestService.Instance?.OnKill(killer, this); // slay objectives
                Server.DeathService.Instance?.LeaveCreatureCorpse(this, EnsureCarried(killer != null ? killer.Prop(21) : 0)); // the killer's luck
                _removeAt = Time.time; // the corpse takes its place
            };
        }

        // ------------------------------------------------------------------ names (clients)

        public string DisplayName => !string.IsNullOrEmpty(PetName.Value) ? PetName.Value : Name;
        public Transform Body => _graphic != null ? _graphic : transform;
        public float NameHeight => _height;
        public Color NameColour => IsPet ? new Color(0.55f, 0.95f, 0.55f) : Hostile ? new Color(1f, 0.45f, 0.4f) : IsVendor ? new Color(0.6f, 0.85f, 1f) : new Color(0.85f, 0.85f, 0.85f);

        private float _height = 2.1f;

        /// <summary>Set by the appearance once the model is built, so the name clears tall and short creatures.</summary>
        public void SetModelHeight(float height) => _height = Mathf.Max(1f, height) + 0.3f;

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerStarted)
            {
                Resolve();
            }

            Nameplates.Seen.Add(this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            Nameplates.Seen.Remove(this);
        }

        // ------------------------------------------------------------------ AI (server)

        private void Update()
        {
            if (!IsServerStarted || Def == null)
            {
                return;
            }

            if (_combat.Dead.Value && !(IsPet && Bonded.Value))
            {
                if (Time.time >= _removeAt)
                {
                    _removeAt = float.PositiveInfinity;
                    ServerManager.Despawn(NetworkObject);
                }

                return;
            }

            if (Summoned && Time.time >= _summonedUntil)
            {
                Unsummon();
                return;
            }

            if (Hunts != null && (Hunts.Dead.Value || !Hunts.IsSpawned))
            {
                Unsummon(); // its quarry is gone: the revenant fades
                return;
            }

            if (RegenEvery > 0f && Time.time >= _nextRegen)
            {
                _nextRegen = Time.time + RegenEvery;
                _combat.Heal(1 + _combat.HitsMax.Value / 100);
            }

            if (Time.time >= _nextThink)
            {
                _nextThink = Time.time + ThinkSeconds;
                Think();
            }

            if (Time.time >= _removeAt && !IsPet)
            {
                _removeAt = float.PositiveInfinity;
                ServerManager.Despawn(NetworkObject); // a released pet fades out
                return;
            }

            Move(Time.deltaTime);
        }

        private void Think()
        {
            if (IsPet)
            {
                ThinkAsPet();
                return;
            }

            if (_returning)
            {
                // Evading: walk back to where the fight began, untouchable, until it's there; one that can't get
                // there (no way back) is put there when its time is up.
                Vector3 start = _fightStart ?? Home;
                Vector3 away = transform.position - start;
                away.y = 0f;
                bool back = away.magnitude < 1.5f;
                if (back || Time.time >= _evadeUntil)
                {
                    if (!back)
                    {
                        PlaceNear(start);
                    }

                    // Back where the fight began: whole again, and fair game.
                    _returning = false;
                    _fightStart = null;
                    _combat.EndEvade();
                }
                else if (_goal == null)
                {
                    _goal = start; // keep heading back
                    _goalSpeed = ChaseSpeed;
                }

                return;
            }

            Combatant target = _combat.Target;
            if (target == null && Summoned)
            {
                target = NearestFoe(12f * UOClient.WorldSpace.TileSize); // on its own: whatever its side may fight
                if (target != null)
                {
                    _combat.Engage(target);
                }
            }
            else if (target == null && Hostile)
            {
                target = NearestEnemy(Def.RangePerception * UOClient.WorldSpace.TileSize);
                if (target != null)
                {
                    _combat.Engage(target); // aggro: the player is now in combat
                }
            }

            if (target != null && _combat.AutoAttack && Casts && Server.SpellService.Instance != null && Server.SpellService.Instance.CreatureThink(this, _combat))
            {
                _goal = null; // standing to cast
                Face(target);
                return;
            }

            if (target != null && _combat.AutoAttack)
            {
                if (_fightStart == null)
                {
                    _fightStart = transform.position;
                    _closest = float.MaxValue;
                    _closingSince = Time.time;
                }

                // Getting closer (or in reach) counts as a chase; no headway at all for a while means it can't reach
                // them (blocked, no path), and it gives up the same as past the leash.
                float apart = (target.transform.position - transform.position).magnitude;
                if (apart < _closest - 0.5f || _combat.InReach(target))
                {
                    _closest = Mathf.Min(_closest, apart);
                    _closingSince = Time.time;
                }

                Vector3 offset = transform.position - _fightStart.Value;
                offset.y = 0f;
                if (offset.magnitude > Leash || Time.time - _closingSince > CantReachSeconds)
                {
                    // Chased too far from where the fight began: drop aggro and walk back, untouchable, then heal
                    // (the fight ends for the player too, unless something else wants them dead).
                    _combat.BeginEvade();
                    _goal = _fightStart.Value;
                    _goalSpeed = ChaseSpeed;
                    _evadeUntil = Time.time + EvadeSeconds;
                    _returning = true;
                    return;
                }

                Vector3 toward = target.transform.position - transform.position;
                toward.y = 0f;
                _goal = _combat.InReach(target) ? (Vector3?)null : target.transform.position - toward.normalized * (_combat.Reach * 0.7f);
                _goalSpeed = ChaseSpeed;
                if (toward.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Quaternion.LookRotation(toward.normalized);
                }

                return;
            }

            _fightStart = null; // no fight (won, or the foe is gone): the next one starts from wherever it is then

            if (Casts)
            {
                Server.SpellService.Instance?.CreatureThink(this, _combat); // mana comes back
            }

            // Wander: rest a while, then stroll to somewhere near home.
            if (_goal.HasValue || Time.time < _idleUntil)
            {
                return;
            }

            float radius = IsVendor ? Mathf.Min(HomeRange, 3f) : HomeRange;
            float angle = (float)_random.NextDouble() * Mathf.PI * 2f;
            float distance = (float)_random.NextDouble() * radius;
            Vector3 stroll = Home + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
            _idleUntil = Time.time + 3f + (float)_random.NextDouble() * 8f;
            if (HousePlot.Cleared(UOClient.WorldSpace.ToTile(stroll)))
            {
                return; // creatures don't stroll onto or near someone's land (they may still chase you there)
            }

            // On the navmesh: only somewhere it can walk to by a short way (a shopkeeper stays in the shop, nobody
            // strolls into a wall or up onto a roof).
            if (Server.NavMeshService.Active && Server.NavMeshService.Snap(transform.position, 1f, out Vector3 here))
            {
                if (!Server.NavMeshService.Snap(stroll, 1.5f, out Vector3 there) || Mathf.Abs(there.y - here.y) > 2.5f)
                {
                    return;
                }

                var path = new UnityEngine.AI.NavMeshPath();
                if (!UnityEngine.AI.NavMesh.CalculatePath(here, there, UnityEngine.AI.NavMesh.AllAreas, path) ||
                    path.status != UnityEngine.AI.NavMeshPathStatus.PathComplete || path.corners.Length > 4)
                {
                    return;
                }

                stroll = there;
            }

            _goal = stroll;
            _goalSpeed = WalkSpeed;
            _idleUntil = Time.time + 3f + (float)_random.NextDouble() * 8f;
        }

        /// <summary>A pet does as it was told: follows its master (guarding them if told), stays, or fights its target.</summary>
        private void ThinkAsPet()
        {
            if (Master == null || !Master.IsSpawned)
            {
                _goal = null;
                return;
            }

            var order = (PetOrder)Order.Value;
            bool ghost = _combat.Dead.Value;
            if (order == PetOrder.Guard && !_combat.AutoAttack && !ghost)
            {
                foreach (Combatant attacker in Master.Attackers)
                {
                    if (_combat.IsEnemyOf(attacker))
                    {
                        _combat.Engage(attacker);
                        break;
                    }
                }
            }

            Combatant target = _combat.Target;
            if (target != null && _combat.AutoAttack && !ghost && Casts && Server.SpellService.Instance != null && Server.SpellService.Instance.CreatureThink(this, _combat))
            {
                _goal = null; // standing to cast
                Face(target);
                return;
            }

            if (target != null && _combat.AutoAttack && !ghost)
            {
                Vector3 toward = target.transform.position - transform.position;
                toward.y = 0f;
                _goal = _combat.InReach(target) ? (Vector3?)null : target.transform.position - toward.normalized * (_combat.Reach * 0.7f);
                _goalSpeed = FollowSpeed;
                if (toward.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Quaternion.LookRotation(toward.normalized);
                }

                return;
            }

            if (order == PetOrder.Attack)
            {
                Order.Value = (byte)PetOrder.Follow; // its fight is over: back to its master
            }

            if (order == PetOrder.Stay && !ghost)
            {
                _goal = null;
                return;
            }

            Vector3 offset = Master.transform.position - transform.position;
            offset.y = 0f;
            if (offset.magnitude > 60f)
            {
                PlaceNear(Master.transform.position - Master.transform.forward * FollowGap); // left far behind
                return;
            }

            _goal = offset.magnitude > FollowGap ? Master.transform.position - offset.normalized * FollowGap : (Vector3?)null;
            _goalSpeed = offset.magnitude > 5f ? FollowSpeed : WalkSpeed * 1.4f;
        }

        private void Face(Combatant target)
        {
            Vector3 toward = target.transform.position - transform.position;
            toward.y = 0f;
            if (toward.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(toward.normalized);
            }
        }

        private void Move(float delta)
        {
            if (!GameWorld.IsReadyAt(transform.position) || _combat.Paralyzed.Value)
            {
                return;
            }

            Vector3 horizontal = Vector3.zero;
            if (_goal.HasValue)
            {
                Vector3 toward = _goal.Value - transform.position;
                toward.y = 0f;
                if (toward.magnitude < 0.3f)
                {
                    _goal = null;
                }
                else
                {
                    // On the navmesh the agent finds the way round walls and buildings (and round other creatures);
                    // elsewhere, straight at it.
                    Vector3 way = Steer(_goal.Value, out bool arrived);
                    if (arrived)
                    {
                        _goal = null;
                    }
                    else
                    {
                        Vector3 dir = way.sqrMagnitude > 0.0001f ? way : toward.normalized;
                        horizontal = dir * _goalSpeed;
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 1f - Mathf.Exp(-10f * delta));
                    }
                }
            }

            if (horizontal.sqrMagnitude > 0.01f && Time.time >= _nextDoorCheck)
            {
                _nextDoorCheck = Time.time + 0.25f;
                OpenDoorAhead(horizontal.normalized);
            }

            _verticalVelocity = _controller.isGrounded ? -2f : Mathf.Max(_verticalVelocity + Gravity * delta, -45f);
            Vector3 before = transform.position;
            _controller.Move((horizontal + Vector3.up * _verticalVelocity) * delta);
            if (_goal.HasValue && !_combat.AutoAttack && horizontal.sqrMagnitude > 0f && (transform.position - before).sqrMagnitude < 0.0001f * delta)
            {
                _goal = null; // blocked on a stroll: give up on it
            }
        }

        /// <summary>
        /// Server: which way to go toward a goal along the navmesh (a unit vector), or zero to walk straight (no navmesh
        /// here yet, or the path still being worked out). The agent only plans: the character controller does the moving,
        /// and the agent is kept where the body is.
        /// </summary>
        private Vector3 Steer(Vector3 goal, out bool arrived)
        {
            arrived = false;
            if (!Server.NavMeshService.Active)
            {
                return Vector3.zero;
            }

            if (_agent == null)
            {
                if (Time.time < _agentRetry)
                {
                    return Vector3.zero;
                }

                _agentRetry = Time.time + 2f;
                if (!Server.NavMeshService.Snap(transform.position, 1.5f, out Vector3 on))
                {
                    return Vector3.zero;
                }

                _agent = gameObject.AddComponent<UnityEngine.AI.NavMeshAgent>();
                _agent.updatePosition = false;
                _agent.updateRotation = false;
                _agent.autoBraking = false;
                _agent.acceleration = 60f;
                _agent.angularSpeed = 720f;
                _agent.stoppingDistance = 0.25f;
                _agent.radius = Mathf.Clamp(_controller.radius, 0.25f, 1.2f);
                _agent.height = Mathf.Max(1f, _controller.height);
                _agent.obstacleAvoidanceType = UnityEngine.AI.ObstacleAvoidanceType.LowQualityObstacleAvoidance;
                _agent.autoTraverseOffMeshLink = false; // doorways: the body walks the link itself (opening the door)
                _agent.Warp(on);
            }

            if (!_agent.isOnNavMesh)
            {
                if (Server.NavMeshService.Snap(transform.position, 1.5f, out Vector3 back))
                {
                    _agent.Warp(back);
                }
                else
                {
                    return Vector3.zero; // off the mesh (its chunk unloaded, or somewhere it can't map)
                }
            }

            if (_agent.isOnOffMeshLink)
            {
                // Through a doorway: straight to the far side, then back on the navmesh.
                Vector3 end = _agent.currentOffMeshLinkData.endPos;
                Vector3 to = end - transform.position;
                to.y = 0f;
                if (to.magnitude > 0.35f)
                {
                    return to.normalized;
                }

                _agent.CompleteOffMeshLink();
            }

            _agent.nextPosition = transform.position;
            _agent.speed = Mathf.Max(0.5f, _goalSpeed);
            if ((goal - _agentGoal).sqrMagnitude > 0.36f || !_agent.hasPath && !_agent.pathPending)
            {
                _agentGoal = goal;
                _agent.SetDestination(goal);
            }

            if (_agent.pathPending)
            {
                return Vector3.zero;
            }

            if (_agent.hasPath && _agent.remainingDistance <= _agent.stoppingDistance + 0.05f &&
                _agent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathComplete)
            {
                arrived = true;
                return Vector3.zero;
            }

            Vector3 v = _agent.desiredVelocity;
            v.y = 0f;
            return v.sqrMagnitude > 0.01f ? v.normalized : Vector3.zero;
        }

        private Combatant NearestFoe(float range)
        {
            Combatant best = null;
            float bestDistance = range * range;
            foreach (Combatant c in Combatant.All)
            {
                if (c == null || !_combat.IsEnemyOf(c) || c.Team != Combatant.Side.Monster)
                {
                    continue;
                }

                float d = (c.transform.position - transform.position).sqrMagnitude;
                if (d < bestDistance && Combatant.LineOfSight(transform.position, c.transform.position))
                {
                    bestDistance = d;
                    best = c;
                }
            }

            return best;
        }

        private Combatant NearestEnemy(float range)
        {
            Combatant best = null;
            float bestDistance = range * range;
            foreach (FishNet.Connection.NetworkConnection connection in NetworkManager.ServerManager.Clients.Values)
            {
                NetworkObject player = connection.FirstObject;
                Combatant c = player != null ? player.GetComponent<Combatant>() : null;
                if (c == null || !_combat.IsEnemyOf(c))
                {
                    continue;
                }

                float d = (player.transform.position - transform.position).sqrMagnitude;
                if (d < bestDistance && Combatant.LineOfSight(transform.position, player.transform.position))
                {
                    bestDistance = d;
                    best = c;
                }
            }

            return best;
        }
    }
}
