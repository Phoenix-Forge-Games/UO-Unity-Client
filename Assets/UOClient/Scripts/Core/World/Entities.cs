using System.Collections.Generic;

namespace UOClient.World
{
    /// <summary>Server-side notoriety, which colors names and health bars.</summary>
    public enum Notoriety : byte
    {
        Unknown = 0,
        Innocent = 1,
        Ally = 2,
        Gray = 3,
        Criminal = 4,
        Enemy = 5,
        Murderer = 6,
        Invulnerable = 7,
    }

    public sealed class EquippedItem
    {
        public uint Serial;
        public ushort Graphic;
        public byte Layer;
        public ushort Hue;
    }

    public sealed class Mobile
    {
        public uint Serial;
        public ushort Body;
        public ushort Hue;
        public byte Flags;
        public Notoriety Notoriety;
        public string Name = string.Empty;
        public int X, Y, Z;
        public Direction Facing;
        public bool Running;
        public readonly Dictionary<byte, EquippedItem> Equipment = new Dictionary<byte, EquippedItem>();

        /// <summary>Human, elf and gargoyle bodies, which get a Sidekick model.</summary>
        public bool IsHumanoid =>
            Body == 400 || Body == 401 || Body == 402 || Body == 403 ||
            Body == 605 || Body == 606 || Body == 607 || Body == 608 ||
            Body == 666 || Body == 667 || Body == 694 || Body == 695 ||
            Body == 0x3DB || Body == 0x3DF;

        public bool IsFemale => Body == 401 || Body == 403 || Body == 606 || Body == 608 || Body == 667 || Body == 695;
    }

    /// <summary>An item lying in the world (not in a container or worn).</summary>
    public sealed class WorldItem
    {
        public uint Serial;
        public ushort Graphic;
        public ushort Amount;
        public ushort Hue;
        public int X, Y, Z;
        public bool IsMulti;
    }
}
