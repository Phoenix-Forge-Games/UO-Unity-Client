using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UOGame.Combat;

namespace UOGame
{
    /// <summary>
    /// Something a spell leaves standing in the world for a while, one tile each: a wall (Wall of Stone, Energy Field:
    /// solid on server and clients alike), a field (fire, poison, paralysis: hurts whoever stands in it), or a moongate
    /// (Gate Travel: walking into it takes you to the other end). The server runs it and removes it when it expires;
    /// clients only draw it. The look is a stand-in shape and colour per kind until it gets its own art.
    /// </summary>
    public sealed class SpellObject : NetworkBehaviour
    {
        public enum Kinds : byte { StoneWall, EnergyWall, Fire, Poison, Paralyze, Gate }

        public readonly SyncVar<byte> Kind = new SyncVar<byte>();

        [SerializeField] private Material _look;

        /// <summary>Server: every spell object standing (Dispel Field picks from these).</summary>
        public static readonly List<SpellObject> All = new List<SpellObject>();

        // Server state.
        public Combatant Caster { get; private set; }
        private float _until, _nextTick;
        private float _interval;
        private System.Action<Combatant> _touch;
        private readonly HashSet<Combatant> _inside = new HashSet<Combatant>();
        private bool _hitOnEnter;
        private Vector3 _destination;
        private readonly Dictionary<Combatant, float> _gateCooldown = new Dictionary<Combatant, float>();
        private bool _built;

        public Kinds What => (Kinds)Kind.Value;
        public bool IsWall => What == Kinds.StoneWall || What == Kinds.EnergyWall;

        /// <summary>Server, before spawning: what it is, who made it, how long it lasts, and what touching it does.</summary>
        public void SetServer(Kinds kind, Combatant caster, float seconds, float interval, bool hitOnEnter, System.Action<Combatant> touch)
        {
            Kind.Value = (byte)kind;
            Caster = caster;
            _until = Time.time + seconds;
            _interval = interval;
            _hitOnEnter = hitOnEnter;
            _touch = touch;
            _nextTick = Time.time + 0.5f; // the first tick comes half a second in
            _inside.Clear();
            _gateCooldown.Clear();
        }

        /// <summary>Server: a gate's far end.</summary>
        public void SetDestination(Vector3 to) => _destination = to;

        public override void OnStartServer()
        {
            base.OnStartServer();
            All.Add(this);
            Build();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Build();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            All.Remove(this);
        }

        /// <summary>Its shape: a solid block for walls (with a collider), a glowing slab for fields, a tall oval for gates.</summary>
        private void Build()
        {
            if (_built)
            {
                return;
            }

            _built = true;
            float tile = UOClient.WorldSpace.TileSize;
            if (IsWall)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(tile, 2.5f, 0.4f);
                box.center = new Vector3(0f, 1.25f, 0f);
            }

            if (IsServerInitialized && !IsClientInitialized && Application.isBatchMode)
            {
                return; // a headless server draws nothing
            }

            GameObject shape = GameObject.CreatePrimitive(What == Kinds.Gate ? PrimitiveType.Sphere : PrimitiveType.Cube);
            Destroy(shape.GetComponent<Collider>());
            shape.transform.SetParent(transform, false);
            switch (What)
            {
                case Kinds.Gate:
                    shape.transform.localScale = new Vector3(1.3f, 2.6f, 0.3f);
                    shape.transform.localPosition = new Vector3(0f, 1.35f, 0f);
                    break;
                case Kinds.StoneWall:
                case Kinds.EnergyWall:
                    shape.transform.localScale = new Vector3(tile, 2.5f, 0.4f);
                    shape.transform.localPosition = new Vector3(0f, 1.25f, 0f);
                    break;
                default:
                    shape.transform.localScale = new Vector3(tile, 1.2f, 0.5f);
                    shape.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                    break;
            }

            var renderer = shape.GetComponent<Renderer>();
            if (_look != null)
            {
                renderer.sharedMaterial = _look;
            }

            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Color colour;
            switch (What)
            {
                case Kinds.StoneWall: colour = new Color(0.55f, 0.53f, 0.5f, 0.95f); break;
                case Kinds.EnergyWall: colour = new Color(0.45f, 0.55f, 1f, 0.55f); break;
                case Kinds.Fire: colour = new Color(1f, 0.45f, 0.1f, 0.55f); break;
                case Kinds.Poison: colour = new Color(0.35f, 0.9f, 0.25f, 0.5f); break;
                case Kinds.Paralyze: colour = new Color(0.95f, 0.9f, 0.45f, 0.5f); break;
                default: colour = new Color(0.35f, 0.6f, 1f, 0.6f); break;
            }

            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", colour);
            block.SetColor("_EmissionColor", colour * 0.6f);
            renderer.SetPropertyBlock(block);
        }

        private void Update()
        {
            if (!IsServerInitialized)
            {
                return;
            }

            if (Time.time >= _until)
            {
                Despawn();
                return;
            }

            if (What == Kinds.Gate)
            {
                Pass();
                return;
            }

            if (_touch == null)
            {
                return;
            }

            bool tick = _interval > 0f && Time.time >= _nextTick;
            if (tick)
            {
                _nextTick = Time.time + _interval;
            }

            var now = new HashSet<Combatant>();
            foreach (Combatant c in Combatant.All)
            {
                if (c == null || c.Dead.Value || c == Caster || !Inside(c.transform.position))
                {
                    continue;
                }

                now.Add(c);
                bool entered = !_inside.Contains(c);
                if (tick || entered && _hitOnEnter)
                {
                    _touch(c);
                }
            }

            _inside.Clear();
            _inside.UnionWith(now);
        }

        /// <summary>Is a body's position on this tile (its 1-tile width along the line, a little either side, about its height)?</summary>
        private bool Inside(Vector3 position)
        {
            Vector3 local = transform.InverseTransformPoint(position);
            return Mathf.Abs(local.x) <= UOClient.WorldSpace.TileSize * 0.5f && Mathf.Abs(local.z) <= 0.75f && local.y > -1.5f && local.y < 2.5f;
        }

        /// <summary>A gate: players stepping into it (and pets following them) come out at the other end.</summary>
        private void Pass()
        {
            foreach (Combatant c in Combatant.All)
            {
                if (c == null || c.Dead.Value || c.GetComponent<PlayerIdentity>() == null)
                {
                    continue;
                }

                Vector3 d = c.transform.position - transform.position;
                d.y = 0f;
                if (d.magnitude > 0.9f || _gateCooldown.TryGetValue(c, out float until) && Time.time < until)
                {
                    continue;
                }

                Vector3 to = _destination + Vector3.back * 1.6f; // just beside the far gate
                foreach (SpellObject other in All)
                {
                    if (other != this && other.What == Kinds.Gate && (other.transform.position - _destination).sqrMagnitude < 1f)
                    {
                        other._gateCooldown[c] = Time.time + 3f; // don't bounce straight back
                    }
                }

                Server.SpellService.MoveBody(c, to);
            }
        }

        private void Despawn()
        {
            _until = float.PositiveInfinity;
            if (IsSpawned)
            {
                ServerManager.Despawn(NetworkObject);
            }
        }

        /// <summary>Server: gone at once (Dispel Field).</summary>
        public void Dispel() => Despawn();
    }
}
