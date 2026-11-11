using System;
using System.Collections.Generic;
using UOClient.Net;

namespace UOClient.World
{
    /// <summary>A buff or debuff on the player (Bless, Poison, Evasion...).</summary>
    public sealed class Buff
    {
        public ushort Icon;
        public string Title = string.Empty;
        public string Description = string.Empty;
        /// <summary>When it runs out (Environment.TickCount ms), or 0 if it has no timer.</summary>
        public int ExpiresAt;
    }

    /// <summary>Buffs and debuffs (0xDF).</summary>
    public sealed partial class WorldState
    {
        public readonly Dictionary<ushort, Buff> Buffs = new Dictionary<ushort, Buff>();
        public event Action BuffsChanged;

        private void HandleBuff(PacketReader packet)
        {
            uint serial = packet.ReadUInt32();
            ushort icon = packet.ReadUInt16();
            int count = packet.ReadUInt16();
            if (Player == null || serial != Player.Serial)
            {
                return;
            }

            if (count == 0)
            {
                Buffs.Remove(icon);
                BuffsChanged?.Invoke();
                return;
            }

            for (int i = 0; i < count; i++)
            {
                packet.ReadUInt16(); // source type
                packet.Skip(2);
                packet.ReadUInt16(); // icon again
                packet.ReadUInt16(); // queue index
                packet.Skip(4);
                int seconds = packet.ReadUInt16();
                packet.Skip(3);
                int title = (int)packet.ReadUInt32();
                int description = (int)packet.ReadUInt32();
                int extra = (int)packet.ReadUInt32();
                packet.ReadUInt16();
                string titleArgs = packet.ReadUnicodeLittleEndianNullTerminated();
                packet.ReadUInt16();
                string descriptionArgs = packet.ReadUnicodeLittleEndianNullTerminated();
                packet.ReadUInt16();
                string extraArgs = packet.ReadUnicodeLittleEndianNullTerminated();

                string Text(int number, string args) => number == 0 ? string.Empty : Cliloc != null ? Cliloc.Format(number, args) : $"[{number}]";
                string desc = Text(description, descriptionArgs);
                string more = Text(extra, extraArgs);
                Buffs[icon] = new Buff
                {
                    Icon = icon,
                    Title = Text(title, titleArgs),
                    Description = more.Length > 0 ? (desc + "\n" + more).Trim() : desc,
                    ExpiresAt = seconds > 0 ? Environment.TickCount + seconds * 1000 : 0,
                };
            }

            BuffsChanged?.Invoke();
        }
    }
}
