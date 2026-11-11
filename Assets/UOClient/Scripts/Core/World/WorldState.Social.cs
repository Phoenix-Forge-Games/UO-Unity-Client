using System;
using System.Collections.Generic;
using UOClient.Data;
using UOClient.Net;

namespace UOClient.World
{
    /// <summary>UO speech / message types (the type byte of 0x1C, 0xAE, 0xC1, 0xAD).</summary>
    public enum MessageType : byte
    {
        Regular = 0,
        System = 1,
        Emote = 2,
        Label = 6,
        Focus = 7,
        Whisper = 8,
        Yell = 9,
        Spell = 10,
        Guild = 13,
        Alliance = 14,
        Command = 15,
    }

    /// <summary>Something said or shown: speech over a mobile, a system message, an item's name label.</summary>
    public sealed class SpeechMessage
    {
        /// <summary>Who or what it's from; 0 or 0xFFFFFFFF for the system.</summary>
        public uint Serial;
        public string Name = string.Empty;
        public string Text = string.Empty;
        public ushort Hue;
        public MessageType Type;

        public bool IsSystem => Serial == 0 || Serial == 0xFFFFFFFF || Type == MessageType.System;
    }

    /// <summary>The player's own numbers beyond hits/mana/stamina (from the full 0x11 status).</summary>
    public sealed class PlayerStatus
    {
        public int Strength, Dexterity, Intelligence, Gold, Armor, Weight, MaxWeight, Followers, MaxFollowers;
        public bool Female;
        public int Luck, MinDamage, MaxDamage, Tithing, StatCap;
        public int ResistPhysical, ResistFire, ResistCold, ResistPoison, ResistEnergy;
        /// <summary>
        /// The extended status (type 6) values the classic client gets, by index: 0-4 max physical / fire / cold /
        /// poison / energy resist, 5 defense chance, 6 defense chance cap, 7 hit chance, 8 swing speed, 9 damage
        /// increase, 10 lower reagent cost, 11 spell damage, 12 cast recovery, 13 casting speed, 14 lower mana cost.
        /// </summary>
        public readonly int[] Extended = new int[29];
        public bool HasExtended;

        /// <summary>Str, dex, int locks (0xBF 0x19 / 0x1A).</summary>
        public readonly SkillLock[] StatLocks = new SkillLock[3];
    }

    /// <summary>Chat, names, health and combat: the parts of the world state the HUD shows.</summary>
    public sealed partial class WorldState
    {
        private enum StatKind { Hits, Mana, Stamina }

        /// <summary>Speech, system messages and labels, translated to text.</summary>
        public event Action<SpeechMessage> Speech;
        /// <summary>A mobile's hits, mana, stamina or name changed.</summary>
        public event Action<Mobile> StatsChanged;
        /// <summary>Someone took damage: serial and amount.</summary>
        public event Action<uint, int> Damaged;
        /// <summary>The server's view of whom the player is fighting changed (0: nobody).</summary>
        public event Action<uint> CombatantChanged;
        public event Action<bool> WarModeChanged;

        /// <summary>Turns cliloc numbers into text; without it they show as numbers.</summary>
        public Cliloc Cliloc { get; set; }
        /// <summary>Keyword IDs for outgoing speech, so NPCs hear "bank", "vendor buy" and the like.</summary>
        public SpeechKeywords Keywords { get; set; }

        public PlayerStatus Status { get; } = new PlayerStatus();
        public uint Combatant { get; private set; }
        public bool WarMode => Player != null && Player.WarMode;

        private readonly Dictionary<uint, int> _namesAsked = new Dictionary<uint, int>();
        private const int NameRetryMilliseconds = 3000;

        private void ResetSocial()
        {
            Combatant = 0;
            _namesAsked.Clear();
        }

        // ---- Outgoing ----

        /// <summary>Says something (0xAD, unicode, with speech.mul keywords).</summary>
        public void Say(string text, MessageType type = MessageType.Regular, ushort hue = 0x02B2)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            List<ushort> keywords = Keywords?.Find(text);
            bool encoded = keywords != null && keywords.Count > 0;
            var packet = new PacketWriter(0xAD);
            packet.WriteByte((byte)((byte)type | (encoded ? 0xC0 : 0)));
            packet.WriteUInt16(hue);
            packet.WriteUInt16(3); // font
            packet.WriteAscii("ENU", 4);
            if (encoded)
            {
                // Keyword count (12 bits) then each 12-bit ID, packed; then the text as UTF-8.
                var codes = new List<byte>();
                int count = keywords.Count;
                codes.Add((byte)(count >> 4));
                int pending = count & 15;
                bool flag = false;
                foreach (ushort id in keywords)
                {
                    if (flag)
                    {
                        codes.Add((byte)(id >> 4));
                        pending = id & 15;
                    }
                    else
                    {
                        codes.Add((byte)((pending << 4) | ((id >> 8) & 15)));
                        codes.Add((byte)id);
                    }

                    flag = !flag;
                }

                if (!flag)
                {
                    codes.Add((byte)(pending << 4));
                }

                foreach (byte b in codes)
                {
                    packet.WriteByte(b);
                }

                foreach (byte b in System.Text.Encoding.UTF8.GetBytes(text))
                {
                    packet.WriteByte(b);
                }

                packet.WriteByte(0);
            }
            else
            {
                packet.WriteUnicodeNullTerminated(text);
            }

            _send(packet);
        }

        /// <summary>Single-clicks something (0x09): the server answers with its name as a label.</summary>
        public void Look(uint serial)
        {
            var packet = new PacketWriter(0x09);
            packet.WriteUInt32(serial);
            _send(packet);
        }

        /// <summary>Asks for a mobile's status (0x34 type 4): name and health, as a 0x11.</summary>
        public void RequestStatus(uint serial)
        {
            var packet = new PacketWriter(0x34);
            packet.WriteUInt32(0xEDEDEDED);
            packet.WriteByte(4);
            packet.WriteUInt32(serial);
            _send(packet);
        }

        /// <summary>
        /// Asks for a mobile's name (0x98), for name plates. The server ignores requests for mobiles at the edge of
        /// its update range, so an unanswered request is repeated every few seconds while the name is still missing.
        /// </summary>
        public void RequestName(uint serial)
        {
            int now = Environment.TickCount;
            if (_namesAsked.TryGetValue(serial, out int askedAt) && unchecked(now - askedAt) < NameRetryMilliseconds)
            {
                return;
            }

            _namesAsked[serial] = now;

            var packet = new PacketWriter(0x98);
            packet.WriteUInt32(serial);
            _send(packet);
        }

        public void SetWarMode(bool on)
        {
            var packet = new PacketWriter(0x72);
            packet.WriteBool(on);
            packet.WriteByte(0);
            packet.WriteByte(0x32);
            packet.WriteByte(0);
            _send(packet);
        }

        /// <summary>Attacks a mobile (0x05). The server puts the player in war mode if needed.</summary>
        public void Attack(uint serial)
        {
            var packet = new PacketWriter(0x05);
            packet.WriteUInt32(serial);
            _send(packet);
        }

        // ---- Incoming ----

        private void HandleAsciiMessage(PacketReader packet)
        {
            var message = new SpeechMessage { Serial = packet.ReadUInt32() };
            packet.ReadUInt16(); // body
            message.Type = (MessageType)packet.ReadByte();
            message.Hue = packet.ReadUInt16();
            packet.ReadUInt16(); // font
            message.Name = packet.ReadAscii(30);
            message.Text = packet.ReadAsciiNullTerminated();
            Raise(message);
        }

        private void HandleUnicodeMessage(PacketReader packet)
        {
            var message = new SpeechMessage { Serial = packet.ReadUInt32() };
            packet.ReadUInt16(); // body
            message.Type = (MessageType)packet.ReadByte();
            message.Hue = packet.ReadUInt16();
            packet.ReadUInt16(); // font
            packet.ReadAscii(4); // language
            message.Name = packet.ReadAscii(30);
            message.Text = packet.ReadUnicodeNullTerminated();
            Raise(message);
        }

        private void HandleClilocMessage(PacketReader packet, bool affixed)
        {
            var message = new SpeechMessage { Serial = packet.ReadUInt32() };
            packet.ReadUInt16(); // body
            message.Type = (MessageType)packet.ReadByte();
            message.Hue = packet.ReadUInt16();
            packet.ReadUInt16(); // font
            int number = (int)packet.ReadUInt32();
            byte affixType = 0;
            if (affixed)
            {
                affixType = packet.ReadByte();
            }

            message.Name = packet.ReadAscii(30);
            string affix = affixed ? packet.ReadAsciiNullTerminated() : string.Empty;
            string args = affixed ? packet.ReadUnicodeNullTerminated() : packet.ReadUnicodeLittleEndianNullTerminated();
            string text = Cliloc != null ? Cliloc.Format(number, args) : $"[{number}] {args}";
            if (affixed)
            {
                text = (affixType & 0x01) != 0 ? affix + text : text + affix;
            }

            message.Text = text;
            Raise(message);
        }

        private void Raise(SpeechMessage message)
        {
            // A label for a mobile is its name (single-click answer).
            if (message.Type == MessageType.Label && Mobiles.TryGetValue(message.Serial, out Mobile mobile) &&
                string.IsNullOrEmpty(mobile.Name) && !string.IsNullOrEmpty(message.Name))
            {
                mobile.Name = message.Name;
                StatsChanged?.Invoke(mobile);
            }

            Speech?.Invoke(message);
        }

        private void HandleName(PacketReader packet)
        {
            if (Mobiles.TryGetValue(packet.ReadUInt32(), out Mobile mobile))
            {
                mobile.Name = packet.ReadAscii(30);
                StatsChanged?.Invoke(mobile);
            }
        }

        private void HandleStatus(PacketReader packet)
        {
            if (!Mobiles.TryGetValue(packet.ReadUInt32(), out Mobile mobile))
            {
                return;
            }

            mobile.Name = packet.ReadAscii(30);
            mobile.Hits = packet.ReadUInt16();
            mobile.HitsMax = packet.ReadUInt16();
            packet.ReadByte(); // can rename
            byte type = packet.ReadByte();
            if (type > 0 && mobile == Player)
            {
                Status.Female = packet.ReadByte() != 0;
                Status.Strength = packet.ReadUInt16();
                Status.Dexterity = packet.ReadUInt16();
                Status.Intelligence = packet.ReadUInt16();
                mobile.Stamina = packet.ReadUInt16();
                mobile.StaminaMax = packet.ReadUInt16();
                mobile.Mana = packet.ReadUInt16();
                mobile.ManaMax = packet.ReadUInt16();
                Status.Gold = (int)packet.ReadUInt32();
                Status.Armor = packet.ReadUInt16();
                Status.Weight = packet.ReadUInt16();
                if (type >= 5)
                {
                    Status.MaxWeight = packet.ReadUInt16();
                    packet.ReadByte(); // race
                }

                if (type >= 3)
                {
                    Status.StatCap = packet.ReadUInt16();
                    Status.Followers = packet.ReadByte();
                    Status.MaxFollowers = packet.ReadByte();
                }

                if (type >= 4)
                {
                    Status.ResistFire = packet.ReadUInt16();
                    Status.ResistCold = packet.ReadUInt16();
                    Status.ResistPoison = packet.ReadUInt16();
                    Status.ResistEnergy = packet.ReadUInt16();
                    Status.Luck = packet.ReadUInt16();
                    Status.MinDamage = packet.ReadUInt16();
                    Status.MaxDamage = packet.ReadUInt16();
                    Status.Tithing = (int)packet.ReadUInt32();
                    Status.ResistPhysical = Status.Armor;
                }

                if (type >= 6)
                {
                    for (int i = 0; i < Status.Extended.Length && packet.Remaining >= 2; i++)
                    {
                        Status.Extended[i] = packet.ReadInt16();
                    }

                    Status.HasExtended = true;
                }
            }

            StatsChanged?.Invoke(mobile);
            MobileUpdated?.Invoke(mobile);
        }

        private void HandleStat(PacketReader packet, StatKind kind)
        {
            if (!Mobiles.TryGetValue(packet.ReadUInt32(), out Mobile mobile))
            {
                return;
            }

            int max = packet.ReadUInt16();
            int current = packet.ReadUInt16();
            switch (kind)
            {
                case StatKind.Hits: mobile.HitsMax = max; mobile.Hits = current; break;
                case StatKind.Mana: mobile.ManaMax = max; mobile.Mana = current; break;
                case StatKind.Stamina: mobile.StaminaMax = max; mobile.Stamina = current; break;
            }

            StatsChanged?.Invoke(mobile);
        }

        private void HandleAttributes(PacketReader packet)
        {
            if (!Mobiles.TryGetValue(packet.ReadUInt32(), out Mobile mobile))
            {
                return;
            }

            mobile.HitsMax = packet.ReadUInt16();
            mobile.Hits = packet.ReadUInt16();
            mobile.ManaMax = packet.ReadUInt16();
            mobile.Mana = packet.ReadUInt16();
            mobile.StaminaMax = packet.ReadUInt16();
            mobile.Stamina = packet.ReadUInt16();
            StatsChanged?.Invoke(mobile);
        }

        private void HandleWarMode(PacketReader packet)
        {
            bool on = packet.ReadBool();
            if (Player == null)
            {
                return;
            }

            Player.Flags = (byte)(on ? Player.Flags | 0x40 : Player.Flags & ~0x40);
            WarModeChanged?.Invoke(on);
            MobileUpdated?.Invoke(Player);
        }

        private void HandleCombatant(PacketReader packet)
        {
            Combatant = packet.ReadUInt32();
            CombatantChanged?.Invoke(Combatant);
        }

        private void HandleDamage(PacketReader packet)
        {
            uint serial = packet.ReadUInt32();
            int amount = packet.ReadUInt16();
            Damaged?.Invoke(serial, amount);
        }
    }
}
