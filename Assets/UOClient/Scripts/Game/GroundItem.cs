using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace UOGame
{
    /// <summary>
    /// An item lying on the ground: dropped by a player, waiting to be picked up (E) or to decay. One of the few
    /// things that are their own network objects (the map's own objects never are). Clients show a pouch with the
    /// item's name over it; what it is comes from the item kind, from the client's own catalog.
    /// </summary>
    public sealed class GroundItem : NetworkBehaviour, INamed
    {
        public readonly SyncVar<ushort> Kind = new SyncVar<ushort>();
        public readonly SyncVar<int> Amount = new SyncVar<int>();
        public readonly SyncVar<ushort> Hue = new SyncVar<ushort>();

        [SerializeField] private GameObject _pouch;
        [SerializeField] private Transform _graphic;

        /// <summary>Server: the item itself (null once picked up).</summary>
        public Server.AccountStore.Item Item { get; set; }

        private float _decayAt = float.PositiveInfinity;
        private GameObject _model;

        /// <summary>Client side: the ground items this client sees.</summary>
        public static readonly List<GroundItem> Seen = new List<GroundItem>();

        public void SetServer(Server.AccountStore.Item item, float seconds)
        {
            Item = item;
            Kind.Value = Server.ItemService.Catalog.Find(item.Type)?.Kind ?? 0;
            Amount.Value = item.Amount;
            Hue.Value = (ushort)item.Hue;
            _decayAt = Time.time + seconds;
        }

        private void Update()
        {
            if (IsServerStarted && Time.time >= _decayAt)
            {
                _decayAt = float.PositiveInfinity;
                Item = null;
                ServerManager.Despawn(NetworkObject);
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Seen.Add(this);
            Nameplates.Seen.Add(this);
            _pouch = global::UOClient.Content.SecureArt.Resolve(_pouch, global::UOClient.Content.SecureArtAddresses.GroundPouch); // paid art by address (Docs/SecureArt.md)
            if (_model == null && _pouch != null && _graphic != null)
            {
                _model = Instantiate(_pouch, _graphic, false);
                foreach (Collider c in _model.GetComponentsInChildren<Collider>(true))
                {
                    Destroy(c);
                }
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            Seen.Remove(this);
            Nameplates.Seen.Remove(this);
        }

        // ------------------------------------------------------------------ name over it

        public string DisplayName => UI.ItemNames.Of(new Net.ItemView { Kind = Kind.Value, Amount = Amount.Value });
        public Transform Body => _graphic != null ? _graphic : transform;
        public float NameHeight => 0.5f;
        public Color NameColour => new Color(0.95f, 0.88f, 0.65f);
    }
}
