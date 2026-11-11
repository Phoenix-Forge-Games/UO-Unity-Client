using System;
using System.Collections.Generic;
using UOClient.Net;

namespace UOClient.World
{
    /// <summary>An item inside a container (backpack, bank, corpse, chest).</summary>
    public sealed class ContainedItem
    {
        public uint Serial;
        public ushort Graphic;
        public ushort Amount;
        public ushort Hue;
        /// <summary>The container it's in.</summary>
        public uint Container;
        /// <summary>Where the server keeps it in the container (classic gump layout; the client doesn't show it).</summary>
        public int X, Y;
        /// <summary>Arrival order within its container (vendor price lists pair up with items by it).</summary>
        public int Order;
    }

    public enum SkillLock : byte { Up = 0, Down = 1, Locked = 2 }

    public sealed class Skill
    {
        public int Id;
        /// <summary>Tenths: 1000 = 100.0.</summary>
        public int Value, Base, Cap;
        public SkillLock Lock;
    }

    /// <summary>Containers, item properties, dragging items, skills and logging out.</summary>
    public sealed partial class WorldState
    {
        public const byte BackpackLayer = 0x15;
        public const byte BankLayer = 0x1D;

        /// <summary>A container gump was opened by the server (serial, gump graphic: 0x09 corpse, 0x30 vendor, 0xFFFF spellbook).</summary>
        public event Action<uint, ushort> ContainerOpened;
        /// <summary>A container's contents changed (its serial).</summary>
        public event Action<uint> ContainerChanged;
        /// <summary>An object's property list (name and tooltip lines) arrived.</summary>
        public event Action<uint> PropertiesChanged;
        public event Action SkillsChanged;
        /// <summary>The server refused a lift or drop (reason code).</summary>
        public event Action<byte> DragRejected;

        public readonly Dictionary<uint, ContainedItem> Contained = new Dictionary<uint, ContainedItem>();
        private int _nextOrder = 1 << 20;
        public readonly Skill[] Skills = new Skill[SkillNames.Length];

        private readonly Dictionary<uint, string[]> _properties = new Dictionary<uint, string[]>();
        private readonly Dictionary<uint, uint> _propertyRevisions = new Dictionary<uint, uint>();
        private readonly HashSet<uint> _propertiesAsked = new HashSet<uint>();

        /// <summary>The player's backpack serial, or 0.</summary>
        public uint Backpack => Player != null && Player.Equipment.TryGetValue(BackpackLayer, out EquippedItem pack) ? pack.Serial : 0;

        /// <summary>The items directly inside a container.</summary>
        public IEnumerable<ContainedItem> ContentsOf(uint container)
        {
            foreach (ContainedItem item in Contained.Values)
            {
                if (item.Container == container)
                {
                    yield return item;
                }
            }
        }

        /// <summary>An object's property lines (first is its name), or null if not known yet (asks the server).</summary>
        public string[] PropertiesOf(uint serial)
        {
            if (_properties.TryGetValue(serial, out string[] lines))
            {
                return lines;
            }

            RequestProperties(serial);
            return null;
        }

        private void ResetItems()
        {
            Contained.Clear();
            _properties.Clear();
            _propertyRevisions.Clear();
            _propertiesAsked.Clear();
            Spellbooks.Clear();
            Buffs.Clear();
            for (int i = 0; i < Skills.Length; i++)
            {
                Skills[i] = new Skill { Id = i };
            }
        }

        // ---- Outgoing ----

        /// <summary>Opens a container (or uses an item): double-click.</summary>
        public void Open(uint serial) => Use(serial);

        /// <summary>Picks an item up (0x07). Follow with a drop or equip.</summary>
        public void Lift(uint serial, int amount)
        {
            var packet = new PacketWriter(0x07);
            packet.WriteUInt32(serial);
            packet.WriteUInt16((ushort)Math.Max(1, Math.Min(amount, ushort.MaxValue)));
            _send(packet);
        }

        /// <summary>Drops the lifted item into a container (0x08; the server picks the spot).</summary>
        public void DropInto(uint serial, uint container)
        {
            var packet = new PacketWriter(0x08);
            packet.WriteUInt32(serial);
            packet.WriteUInt16(0xFFFF); // x: let the server place it
            packet.WriteUInt16(0xFFFF); // y
            packet.WriteByte(0);        // z
            packet.WriteByte(0);        // grid index
            packet.WriteUInt32(container);
            _send(packet);
        }

        /// <summary>Drops the lifted item on the ground at a tile.</summary>
        public void DropOnGround(uint serial, int x, int y, int z)
        {
            var packet = new PacketWriter(0x08);
            packet.WriteUInt32(serial);
            packet.WriteUInt16((ushort)x);
            packet.WriteUInt16((ushort)y);
            packet.WriteByte((byte)(sbyte)z);
            packet.WriteByte(0);
            packet.WriteUInt32(0xFFFFFFFF);
            _send(packet);
        }

        /// <summary>Wears the lifted item (0x13) on a mobile (the player, usually) at its layer.</summary>
        public void Equip(uint serial, byte layer, uint mobile)
        {
            var packet = new PacketWriter(0x13);
            packet.WriteUInt32(serial);
            packet.WriteByte(layer);
            packet.WriteUInt32(mobile);
            _send(packet);
        }

        /// <summary>Asks for property lists (0xD6), batched by the caller's frame.</summary>
        public void RequestProperties(uint serial)
        {
            if (!_propertiesAsked.Add(serial))
            {
                return;
            }

            var packet = new PacketWriter(0xD6);
            packet.WriteUInt32(serial);
            _send(packet);
        }

        /// <summary>Asks for the whole skill list (0x34 type 5).</summary>
        public void RequestSkills()
        {
            if (Player == null)
            {
                return;
            }

            var packet = new PacketWriter(0x34);
            packet.WriteUInt32(0xEDEDEDED);
            packet.WriteByte(5);
            packet.WriteUInt32(Player.Serial);
            _send(packet);
        }

        /// <summary>Uses a skill (0x12 type 0x24, "id 0").</summary>
        public void UseSkill(int id)
        {
            NoteSkill(id);
            var packet = new PacketWriter(0x12);
            packet.WriteByte(0x24);
            packet.WriteAsciiNullTerminated($"{id} 0");
            _send(packet);
        }

        public void SetSkillLock(int id, SkillLock state)
        {
            var packet = new PacketWriter(0x3A);
            packet.WriteUInt16((ushort)id);
            packet.WriteByte((byte)state);
            _send(packet);
            if (id >= 0 && id < Skills.Length)
            {
                Skills[id].Lock = state;
                SkillsChanged?.Invoke();
            }
        }

        /// <summary>Sets a stat's lock (0xBF 0x1A): stat 0 str, 1 dex, 2 int.</summary>
        public void SetStatLock(int stat, SkillLock state)
        {
            var packet = new PacketWriter(0xBF);
            packet.WriteUInt16(0x1A);
            packet.WriteByte((byte)stat);
            packet.WriteByte((byte)state);
            _send(packet);
            Status.StatLocks[stat] = state;
        }

        /// <summary>Casts a spell by number (0xBF 0x1C): 1-64 magery, 101+ necromancy, and so on.</summary>
        public void CastSpell(int spell)
        {
            NoteSpell(spell);
            var packet = new PacketWriter(0xBF);
            packet.WriteUInt16(0x1C);
            packet.WriteUInt16(1);
            packet.WriteUInt16((ushort)spell);
            _send(packet);
        }

        /// <summary>Tells the server the player is logging out (0x01). The caller then closes the connection.</summary>
        public void Logout()
        {
            var packet = new PacketWriter(0x01);
            packet.WriteUInt32(0xFFFFFFFF);
            _send(packet);
        }

        // ---- Incoming ----

        private void HandleOpenContainer(PacketReader packet)
        {
            uint serial = packet.ReadUInt32();
            ushort gump = packet.ReadUInt16();
            if (TryOpenShop(serial, gump))
            {
                return;
            }

            ContainerOpened?.Invoke(serial, gump);
        }

        private void HandleContainerContents(PacketReader packet)
        {
            int count = packet.ReadUInt16();
            var touched = new HashSet<uint>();
            var items = new List<ContainedItem>(count);
            for (int i = 0; i < count; i++)
            {
                var item = new ContainedItem { Serial = packet.ReadUInt32(), Order = i };
                item.Graphic = (ushort)(packet.ReadUInt16() + packet.ReadByte());
                item.Amount = Math.Max((ushort)1, packet.ReadUInt16());
                item.X = packet.ReadUInt16();
                item.Y = packet.ReadUInt16();
                packet.ReadByte(); // grid index
                item.Container = packet.ReadUInt32();
                item.Hue = packet.ReadUInt16();
                items.Add(item);
                touched.Add(item.Container);
            }

            // A full listing replaces what we knew of those containers.
            var stale = new List<uint>();
            foreach (ContainedItem old in Contained.Values)
            {
                if (touched.Contains(old.Container))
                {
                    stale.Add(old.Serial);
                }
            }

            foreach (uint serial in stale)
            {
                Contained.Remove(serial);
            }

            foreach (ContainedItem item in items)
            {
                Contained[item.Serial] = item;
                Items.Remove(item.Serial);
            }

            foreach (uint container in touched)
            {
                ContainerChanged?.Invoke(container);
            }
        }

        private void HandleContainedItem(PacketReader packet)
        {
            var item = new ContainedItem { Serial = packet.ReadUInt32() };
            item.Graphic = (ushort)(packet.ReadUInt16() + packet.ReadByte());
            item.Amount = Math.Max((ushort)1, packet.ReadUInt16());
            item.X = packet.ReadUInt16();
            item.Y = packet.ReadUInt16();
            packet.ReadByte(); // grid index
            item.Container = packet.ReadUInt32();
            item.Hue = packet.ReadUInt16();

            uint previous = Contained.TryGetValue(item.Serial, out ContainedItem old) ? old.Container : 0;
            item.Order = old != null && previous == item.Container ? old.Order : _nextOrder++;
            Contained[item.Serial] = item;
            if (Items.Remove(item.Serial))
            {
                ItemRemoved?.Invoke(item.Serial); // picked up off the ground into a container
            }

            RemoveWorn(item.Serial);
            if (previous != 0 && previous != item.Container)
            {
                ContainerChanged?.Invoke(previous);
            }

            ContainerChanged?.Invoke(item.Container);
        }

        /// <summary>Called on delete (0x1D): drops a contained item. True if it was one.</summary>
        private bool RemoveContained(uint serial)
        {
            if (!Contained.TryGetValue(serial, out ContainedItem item))
            {
                return false;
            }

            Contained.Remove(serial);
            ContainerChanged?.Invoke(item.Container);
            return true;
        }

        /// <summary>An item put on (0x2E) leaves whatever container it was in.</summary>
        private void OnEquipped(uint serial)
        {
            RemoveContained(serial);
        }

        private void RemoveWorn(uint serial)
        {
            foreach (Mobile mobile in Mobiles.Values)
            {
                foreach (KeyValuePair<byte, EquippedItem> pair in mobile.Equipment)
                {
                    if (pair.Value.Serial == serial)
                    {
                        mobile.Equipment.Remove(pair.Key);
                        MobileUpdated?.Invoke(mobile);
                        return;
                    }
                }
            }
        }

        private void HandleDragRejected(PacketReader packet)
        {
            DragRejected?.Invoke(packet.ReadByte());
        }

        private void HandlePropertyRevision(PacketReader packet)
        {
            uint serial = packet.ReadUInt32();
            uint revision = packet.ReadUInt32();
            if (_propertyRevisions.TryGetValue(serial, out uint known) && known == revision)
            {
                return;
            }

            // Out of date: forget it so the next look asks again.
            _propertiesAsked.Remove(serial);
            if (_properties.ContainsKey(serial))
            {
                RequestProperties(serial);
            }
        }

        private void HandleProperties(PacketReader packet)
        {
            packet.ReadUInt16(); // 1
            uint serial = packet.ReadUInt32();
            packet.ReadUInt16(); // 0
            uint revision = packet.ReadUInt32();
            var lines = new List<string>();
            while (packet.Remaining >= 4)
            {
                int number = (int)packet.ReadUInt32();
                if (number == 0)
                {
                    break;
                }

                int length = packet.ReadUInt16();
                string args = string.Empty;
                if (length > 0)
                {
                    var chars = new char[length / 2];
                    for (int i = 0; i < chars.Length; i++)
                    {
                        int low = packet.ReadByte();
                        chars[i] = (char)(low | (packet.ReadByte() << 8));
                    }

                    args = new string(chars);
                }

                string text = Cliloc != null ? Cliloc.Format(number, args) : $"[{number}] {args}";
                lines.Add(StripMarkup(text));
            }

            _properties[serial] = lines.ToArray();
            _propertyRevisions[serial] = revision;
            PropertiesChanged?.Invoke(serial);
        }

        /// <summary>Property text carries simple HTML (&lt;BASEFONT&gt;, &lt;br&gt;); the HUD shows plain text.</summary>
        private static string StripMarkup(string text)
        {
            if (text.IndexOf('<') < 0)
            {
                return text.Trim();
            }

            var sb = new System.Text.StringBuilder(text.Length);
            bool inTag = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '<')
                {
                    inTag = true;
                    if (string.Compare(text, i, "<br>", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        sb.Append('\n');
                    }
                }
                else if (c == '>' && inTag)
                {
                    inTag = false;
                }
                else if (!inTag)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString().Trim();
        }

        private void HandleSkills(PacketReader packet)
        {
            byte type = packet.ReadByte();
            if (type == 0xFE)
            {
                return; // skill name list (we use our own)
            }

            bool hasCap = type == 0x02 || type == 0xDF;
            bool single = type == 0xDF || type == 0xFF;
            while (packet.Remaining >= 7)
            {
                int id = packet.ReadUInt16();
                if (id == 0 && !single)
                {
                    break;
                }

                if (!single)
                {
                    id--; // full lists are 1-based
                }

                var skill = new Skill
                {
                    Id = id,
                    Value = packet.ReadUInt16(),
                    Base = packet.ReadUInt16(),
                    Lock = (SkillLock)packet.ReadByte(),
                    Cap = hasCap ? packet.ReadUInt16() : 1000,
                };
                if (id >= 0 && id < Skills.Length)
                {
                    Skills[id] = skill;
                }

                if (single)
                {
                    break;
                }
            }

            SkillsChanged?.Invoke();
        }

        /// <summary>UO's skills by ID (client 7.0, 58 skills).</summary>
        public static readonly string[] SkillNames =
        {
            "Alchemy", "Anatomy", "Animal Lore", "Item Identification", "Arms Lore", "Parrying", "Begging", "Blacksmithy",
            "Bowcraft/Fletching", "Peacemaking", "Camping", "Carpentry", "Cartography", "Cooking", "Detecting Hidden",
            "Discordance", "Evaluating Intelligence", "Healing", "Fishing", "Forensic Evaluation", "Herding", "Hiding",
            "Provocation", "Inscription", "Lockpicking", "Magery", "Resisting Spells", "Tactics", "Snooping",
            "Musicianship", "Poisoning", "Archery", "Spirit Speak", "Stealing", "Tailoring", "Animal Taming",
            "Taste Identification", "Tinkering", "Tracking", "Veterinary", "Swordsmanship", "Mace Fighting", "Fencing",
            "Wrestling", "Lumberjacking", "Mining", "Meditation", "Stealth", "Remove Trap", "Necromancy", "Focus",
            "Chivalry", "Bushido", "Ninjitsu", "Spellweaving", "Mysticism", "Imbuing", "Throwing",
        };

        /// <summary>Skills with a "use" action (the client's blue gem).</summary>
        public static bool IsUsableSkill(int id)
        {
            switch (id)
            {
                case 1: case 2: case 3: case 4: case 6: case 9: case 12: case 14: case 15: case 16: case 19: case 21:
                case 22: case 23: case 30: case 32: case 33: case 35: case 36: case 38: case 46: case 47: case 48: case 56:
                    return true;
                default:
                    return false;
            }
        }
    }
}
