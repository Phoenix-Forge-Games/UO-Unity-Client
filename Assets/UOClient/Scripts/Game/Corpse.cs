using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UOClient;

namespace UOGame
{
    /// <summary>
    /// What's left where something died: a creature's body, or a player's while their ghost goes for a healer. It lies
    /// there until it decays (it will hold the loot and the dead player's belongings once items exist).
    /// <para>
    /// The server places it and removes it when it decays; clients build the body's model, still, and lay it down
    /// (humans on their back, beasts on their side), falling over if they saw it happen.
    /// </para>
    /// </summary>
    public sealed class Corpse : NetworkBehaviour
    {
        /// <summary>A creature's corpse: its kind and seed (the body, name and outfit follow from our content).</summary>
        public readonly SyncVar<int> Kind = new SyncVar<int>(-1);
        public readonly SyncVar<int> Seed = new SyncVar<int>();
        /// <summary>A player's corpse: whose it is, and how they looked (players aren't in the content).</summary>
        public readonly SyncVar<string> Owner = new SyncVar<string>();
        public readonly SyncVar<Look> PlayerLook = new SyncVar<Look>();
        public readonly SyncVar<uint> DiedTick = new SyncVar<uint>();

        /// <summary>Whose body this was.</summary>
        public string Name
        {
            get
            {
                if (!string.IsNullOrEmpty(Owner.Value))
                {
                    return Owner.Value;
                }

                Content.CreatureDef def = Server.SpawnService.Content?.Kind(Kind.Value);
                return def != null ? Server.SpawnService.Content.Resolve(def, Seed.Value).Name : "something";
            }
        }

        [SerializeField] private CreatureCatalog _catalog;
        [SerializeField] private GameObject _human;
        [SerializeField] private RuntimeAnimatorController _humanAnimator;
        [SerializeField] private Transform _graphic;

        private const float FallSeconds = 0.6f;

        /// <summary>Server: what's in it (a creature's loot).</summary>
        public readonly System.Collections.Generic.List<Server.AccountStore.Item> Loot = new System.Collections.Generic.List<Server.AccountStore.Item>();

        private float _decayAt = float.PositiveInfinity;
        private Quaternion _lying;
        private float _fallStart = -1f;

        /// <summary>Server, before spawning: a creature's body.</summary>
        public void SetCreature(NpcMobile creature, float decaySeconds)
        {
            Kind.Value = creature.Kind.Value;
            Seed.Value = creature.Seed.Value;
            Owner.Value = string.Empty;
            PlayerLook.Value = default;
            Loot.Clear();
            _decayAt = Time.time + decaySeconds;
        }

        /// <summary>Server, before spawning: a player's body.</summary>
        public void SetPlayer(string name, Look look, float decaySeconds)
        {
            Kind.Value = -1;
            Seed.Value = 0;
            Owner.Value = name;
            PlayerLook.Value = look;
            Loot.Clear();
            _decayAt = Time.time + decaySeconds;
        }

        /// <summary>The body to build, and how it's dressed.</summary>
        private (int Body, Look Look) Appearance()
        {
            if (Kind.Value < 0)
            {
                Look look = PlayerLook.Value;
                return (look.Female ? 401 : 400, look);
            }

            Content.CreatureDef def = Server.SpawnService.Content?.Kind(Kind.Value);
            if (def == null)
            {
                return (0, default);
            }

            Content.GameContent.Variant variant = Server.SpawnService.Content.Resolve(def, Seed.Value);
            return (variant.Body, variant.Look);
        }

        /// <summary>Server: every corpse lying in the world (Animate Dead looks through them).</summary>
        public static readonly System.Collections.Generic.List<Corpse> All = new System.Collections.Generic.List<Corpse>();

        /// <summary>Server: already raised by Animate Dead.</summary>
        public bool Animated { get; set; }

        public override void OnStartServer()
        {
            base.OnStartServer();
            DiedTick.Value = TimeManager.Tick;
            Animated = false;
            All.Add(this);
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            All.Remove(this);
        }

        private void Update()
        {
            if (IsServerStarted && Time.time >= _decayAt)
            {
                _decayAt = float.PositiveInfinity;
                ServerManager.Despawn(NetworkObject);
                return;
            }

            if (_fallStart >= 0f)
            {
                float t = Mathf.Clamp01((Time.time - _fallStart) / FallSeconds);
                _graphic.localRotation = Quaternion.Slerp(Quaternion.identity, _lying, t * t); // speeds up as it falls
                if (t >= 1f)
                {
                    _fallStart = -1f;
                }
            }
        }

        /// <summary>Client side: the corpses this client sees.</summary>
        public static readonly System.Collections.Generic.List<Corpse> Seen = new System.Collections.Generic.List<Corpse>();

        public override void OnStopClient()
        {
            base.OnStopClient();
            Seen.Remove(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Seen.Add(this);
            (int body, Look look) = Appearance();
            if (_graphic == null || body <= 0)
            {
                return;
            }

            _catalog = global::UOClient.Content.SecureArt.Resolve(_catalog, global::UOClient.Content.SecureArtAddresses.CreatureCatalog); // paid art by address when the prefab holds none (Docs/SecureArt.md)
            _human = global::UOClient.Content.SecureArt.Resolve(_human, global::UOClient.Content.SecureArtAddresses.HumanModel);
            _humanAnimator = global::UOClient.Content.SecureArt.Resolve(_humanAnimator, global::UOClient.Content.SecureArtAddresses.HumanAnimator);
            GameObject model = ActorModel.Build(body, _graphic, _catalog, _human, _humanAnimator, out Animator animator, out bool human, look);
            if (animator != null)
            {
                // Hold the standing pose (idle's first frame), then stop animating for good.
                animator.Update(0f);
                animator.enabled = false;
                Destroy(animator);
            }

            // Humans fall on their back (resting on the back, about 0.12 m deep); beasts roll onto their side.
            _lying = human ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.Euler(0f, 0f, 90f);
            _graphic.localPosition = Vector3.up * (human ? 0.12f : ActorModel.HalfWidth(model) * 0.8f);
            _graphic.localRotation = _lying;

            bool sawItHappen = TimeManager.TimePassed(DiedTick.Value) < 1.5;
            if (sawItHappen)
            {
                _graphic.localRotation = Quaternion.identity;
                _fallStart = Time.time;
            }
        }
    }
}
