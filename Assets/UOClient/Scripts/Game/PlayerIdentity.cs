using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace UOGame
{
    /// <summary>Who a player is: the character's name for everyone, its saved-character ID on the server.</summary>
    public sealed class PlayerIdentity : NetworkBehaviour, INamed
    {
        public readonly SyncVar<string> Name = new SyncVar<string>();
        /// <summary>Sex, face, hair and the gear worn: everyone dresses this character from it.</summary>
        public readonly SyncVar<Look> Look = new SyncVar<Look>();
        /// <summary>The creature being ridden: its kind + 1 (0 = on foot) and seed, so everyone draws the same mount.</summary>
        public readonly SyncVar<ushort> MountKind = new SyncVar<ushort>();
        public readonly SyncVar<int> MountSeed = new SyncVar<int>();

        public bool Mounted => MountKind.Value != 0;
        /// <summary>In the air (gargoyles only; the Fly ability toggles it, the server decides). Everyone sees the wings open.</summary>
        public readonly SyncVar<bool> Flying = new SyncVar<bool>();
        /// <summary>Flying and coming down under control: slowing, flaring, then touching down (ends Flying).</summary>
        public readonly SyncVar<bool> Landing = new SyncVar<bool>();
        /// <summary>Gargoyles can fly and can't ride.</summary>
        public bool IsGargoyle => Look.Value.Gargoyle;
        /// <summary>A body worn instead of their own (Polymorph): a UO body number, 0 for their own.</summary>
        public readonly SyncVar<ushort> BodyOverride = new SyncVar<ushort>();
        /// <summary>Animal Form as a swift beast (kirin, unicorn, wolf, ostard...): mount speed on foot.</summary>
        public readonly SyncVar<bool> Swift = new SyncVar<bool>();
        /// <summary>Night Sight: you see as by day, whatever the hour or the dungeon.</summary>
        public readonly SyncVar<bool> NightSight = new SyncVar<bool>();
        /// <summary>In deep water, swimming (for the pose).</summary>
        public readonly SyncVar<bool> Swimming = new SyncVar<bool>();
        /// <summary>A held pose (the server sets it): standing, sitting on a deck, or steering at a ship's helm.</summary>
        public readonly SyncVar<byte> Pose = new SyncVar<byte>();
        public const byte Standing = 0, Sitting = 1, Steering = 2;
        /// <summary>At work (everyone sees it): gathering and crafting set these while they last.</summary>
        public const byte Mining = 3, Chopping = 4, Fishing = 5, Smithing = 6, Crafting = 7, Brewing = 8;

        /// <summary>Is this pose one of the working ones?</summary>
        public static bool IsWork(byte pose) => pose >= Mining && pose <= Brewing;

        /// <summary>Client side: every player this client can see.</summary>
        public static readonly System.Collections.Generic.List<PlayerIdentity> Seen = new System.Collections.Generic.List<PlayerIdentity>();

        public string DisplayName => Name.Value;
        public Transform Body => NetworkObject.GetGraphicalObject() ?? transform;
        public float NameHeight => 2.15f;
        public Color NameColour => new Color(0.92f, 0.88f, 0.78f);

        public override void OnStartClient()
        {
            base.OnStartClient();
            Seen.Add(this);
            Nameplates.Seen.Add(this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            Seen.Remove(this);
            Nameplates.Seen.Remove(this);
        }

        /// <summary>The character this player is (synced: the server still checks every claim against it).</summary>
        public readonly SyncVar<int> Character = new SyncVar<int>();
        public int CharacterId => Character.Value;

        /// <summary>Server, before spawning.</summary>
        public void SetServer(int characterId, string name)
        {
            Character.Value = characterId;
            Name.Value = name;
        }
    }
}
