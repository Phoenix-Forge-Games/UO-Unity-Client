using System;
using UOClient.Net;

namespace UOClient.World
{
    public enum TargetIntent : byte { Neutral = 0, Harmful = 1, Beneficial = 2, Cancel = 3 }

    /// <summary>What the player last did that can make the server ask for a target.</summary>
    public enum ActionKind { None, Spell, Skill, UseItem }

    /// <summary>The server asking which object or tile an action applies to (0x6C).</summary>
    public sealed class TargetRequest
    {
        public uint CursorId;
        /// <summary>True: a location is wanted (ground, tree, rock); false: an object.</summary>
        public bool Ground;
        public TargetIntent Intent;
        /// <summary>What led to it (the player's last action).</summary>
        public ActionKind Action;
        /// <summary>Spell number, skill ID, or the used item's serial.</summary>
        public int ActionArg;
        public ushort ActionGraphic;
        /// <summary>Placing a multi (a house from a deed, 0x99): which one; 0 for an ordinary target.</summary>
        public ushort MultiId;
    }

    /// <summary>
    /// Target requests. The game has no targeting cursor (Docs/GameplayDesign.md): the client answers every request
    /// itself, from the target chosen beforehand or what's in front of the player. This part only carries the
    /// request (with the action that caused it) and the answers.
    /// </summary>
    public sealed partial class WorldState
    {
        public event Action<TargetRequest> TargetRequested;

        public ActionKind LastAction { get; private set; }
        public int LastActionArg { get; private set; }
        public ushort LastActionGraphic { get; private set; }

        /// <summary>
        /// Asked before an item is used: return true to take over (get it ready first, e.g. put an axe in hand) and
        /// call <see cref="UseNow"/> when done.
        /// </summary>
        public Func<uint, bool> PrepareUse { get; set; }

        /// <summary>Uses (double-clicks) something, remembering it in case the server then asks for a target.</summary>
        public void Use(uint serial)
        {
            if (PrepareUse != null && PrepareUse(serial))
            {
                return;
            }

            UseNow(serial);
        }

        /// <summary>Uses something right away (no preparation).</summary>
        public void UseNow(uint serial)
        {
            LastAction = ActionKind.UseItem;
            LastActionArg = (int)serial;
            LastActionGraphic = Contained.TryGetValue(serial, out ContainedItem c) ? c.Graphic
                : Items.TryGetValue(serial, out WorldItem w) ? w.Graphic
                : WornGraphic(serial);
            Walker.Use(serial);
        }

        private ushort WornGraphic(uint serial)
        {
            if (Player != null)
            {
                foreach (EquippedItem item in Player.Equipment.Values)
                {
                    if (item.Serial == serial)
                    {
                        return item.Graphic;
                    }
                }
            }

            return 0;
        }

        private void NoteSpell(int spell)
        {
            LastAction = ActionKind.Spell;
            LastActionArg = spell;
            LastActionGraphic = 0;
        }

        private void NoteSkill(int skill)
        {
            LastAction = ActionKind.Skill;
            LastActionArg = skill;
            LastActionGraphic = 0;
        }

        private void HandleTargetRequest(PacketReader packet)
        {
            var request = new TargetRequest
            {
                Ground = packet.ReadByte() == 1,
                CursorId = packet.ReadUInt32(),
                Intent = (TargetIntent)packet.ReadByte(),
                Action = LastAction,
                ActionArg = LastActionArg,
                ActionGraphic = LastActionGraphic,
            };

            if (request.Intent == TargetIntent.Cancel)
            {
                return; // the server withdrew its request
            }

            TargetRequested?.Invoke(request);
        }

        /// <summary>0x99: place a multi (a house from a deed); answered like a ground target.</summary>
        private void HandleMultiPlacement(PacketReader packet)
        {
            packet.ReadBool(); // allow ground
            var request = new TargetRequest
            {
                Ground = true,
                CursorId = packet.ReadUInt32(),
                Intent = (TargetIntent)(packet.ReadByte() & 0x03),
                Action = LastAction,
                ActionArg = LastActionArg,
                ActionGraphic = LastActionGraphic,
            };
            packet.Position = 18;
            request.MultiId = packet.ReadUInt16();
            TargetRequested?.Invoke(request);
        }

        /// <summary>Answers a request with an object.</summary>
        public void AnswerTarget(TargetRequest request, uint serial)
        {
            ushort graphic = 0;
            int x = 0, y = 0, z = 0;
            if (Mobiles.TryGetValue(serial, out Mobile m))
            {
                graphic = m.Body;
                x = m.X; y = m.Y; z = m.Z;
            }
            else if (Items.TryGetValue(serial, out WorldItem item))
            {
                graphic = item.Graphic;
                x = item.X; y = item.Y; z = item.Z;
            }
            else if (Contained.TryGetValue(serial, out ContainedItem contained))
            {
                graphic = contained.Graphic;
                x = contained.X; y = contained.Y;
            }

            SendTarget(request, 0, serial, x, y, z, graphic);
        }

        /// <summary>Answers a request with a map location: a static (tree, forge) when `graphic` is set, else the land.</summary>
        public void AnswerTarget(TargetRequest request, int x, int y, int z, ushort graphic) =>
            SendTarget(request, 1, 0, x, y, z, graphic);

        /// <summary>Declines a request (as if the player pressed Esc on a cursor).</summary>
        public void CancelTarget(TargetRequest request) =>
            SendTarget(request, 0, 0, 0xFFFF, 0xFFFF, 0, 0, cancel: true);

        private void SendTarget(TargetRequest request, byte type, uint serial, int x, int y, int z, ushort graphic, bool cancel = false)
        {
            var packet = new PacketWriter(0x6C);
            packet.WriteByte(type);
            packet.WriteUInt32(request.CursorId);
            packet.WriteByte(cancel ? (byte)TargetIntent.Cancel : (byte)request.Intent);
            packet.WriteUInt32(serial);
            packet.WriteUInt16((ushort)x);
            packet.WriteUInt16((ushort)y);
            packet.WriteByte(0);
            packet.WriteByte((byte)(sbyte)z);
            packet.WriteUInt16(graphic);
            _send(packet);
        }
    }
}
