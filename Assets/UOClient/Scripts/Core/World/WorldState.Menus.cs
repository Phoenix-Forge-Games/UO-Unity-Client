using System;
using System.Collections.Generic;
using UOClient.Net;

namespace UOClient.World
{
    /// <summary>A server gump as data: who sent it, its type ID, its layout commands and text lines.</summary>
    public sealed class ServerGump
    {
        public uint Serial;
        public uint GumpId;
        public string Layout = string.Empty;
        public string[] Lines = new string[0];

        /// <summary>The cliloc numbers the layout shows (xmfhtmlgump / xmfhtmlgumpcolor / tooltip...).</summary>
        public IEnumerable<int> Clilocs()
        {
            foreach (string command in Layout.Split('{'))
            {
                string[] parts = command.Trim(' ', '}').Split(' ');
                if (parts.Length > 0 && parts[0].StartsWith("xmfhtml"))
                {
                    if (parts.Length > 5 && int.TryParse(parts[5], out int number))
                    {
                        yield return number;
                    }
                }
            }
        }
    }

    /// <summary>One entry of a server context menu ("Open Paperdoll", "Buy", "Talk", "Bank"...).</summary>
    public sealed class ContextEntry
    {
        public ushort Index;
        public string Text;
        public bool Disabled;
    }

    /// <summary>
    /// Server context menus (0xBF 0x13 ask, 0x14 entries, 0x15 choose). The client shows them two ways: an NPC's
    /// as a dialogue window (E on the NPC), an item's as a right-click menu in containers.
    /// </summary>
    public sealed partial class WorldState
    {
        /// <summary>The server sent the context menu for an object: its serial and entries.</summary>
        public event Action<uint, IReadOnlyList<ContextEntry>> ContextMenuReceived;

        public void RequestContextMenu(uint serial)
        {
            var packet = new PacketWriter(0xBF);
            packet.WriteUInt16(0x13);
            packet.WriteUInt32(serial);
            _send(packet);
        }

        public void ChooseContextEntry(uint serial, ushort index)
        {
            var packet = new PacketWriter(0xBF);
            packet.WriteUInt16(0x15);
            packet.WriteUInt32(serial);
            packet.WriteUInt16(index);
            _send(packet);
        }

        /// <summary>A spellbook's contents arrived (0xBF 0x1B): its serial.</summary>
        public event Action<uint> SpellbookChanged;

        /// <summary>Known spellbooks: serial → (first spell number, spells it holds).</summary>
        public readonly Dictionary<uint, (int Offset, ulong Spells)> Spellbooks = new Dictionary<uint, (int, ulong)>();

        private void HandleSpellbookContent(PacketReader packet)
        {
            packet.ReadUInt16(); // 1
            uint serial = packet.ReadUInt32();
            packet.ReadUInt16(); // graphic
            int offset = packet.ReadUInt16();
            ulong bits = 0;
            for (int i = 0; i < 8; i++)
            {
                bits |= (ulong)packet.ReadByte() << (i * 8);
            }

            Spellbooks[serial] = (offset, bits);
            SpellbookChanged?.Invoke(serial);
        }

        /// <summary>Spell names by school, from the school's first spell number.</summary>
        public static readonly Dictionary<int, (string School, string[] Spells)> SpellSchools = new Dictionary<int, (string, string[])>
        {
            [1] = ("Magery", new[]
            {
                "Clumsy", "Create Food", "Feeblemind", "Heal", "Magic Arrow", "Night Sight", "Reactive Armor", "Weaken",
                "Agility", "Cunning", "Cure", "Harm", "Magic Trap", "Magic Untrap", "Protection", "Strength",
                "Bless", "Fireball", "Magic Lock", "Poison", "Telekinesis", "Teleport", "Unlock", "Wall of Stone",
                "Arch Cure", "Arch Protection", "Curse", "Fire Field", "Greater Heal", "Lightning", "Mana Drain", "Recall",
                "Blade Spirits", "Dispel Field", "Incognito", "Magic Reflection", "Mind Blast", "Paralyze", "Poison Field", "Summon Creature",
                "Dispel", "Energy Bolt", "Explosion", "Invisibility", "Mark", "Mass Curse", "Paralyze Field", "Reveal",
                "Chain Lightning", "Energy Field", "Flamestrike", "Gate Travel", "Mana Vampire", "Mass Dispel", "Meteor Swarm", "Polymorph",
                "Earthquake", "Energy Vortex", "Resurrection", "Air Elemental", "Summon Daemon", "Earth Elemental", "Fire Elemental", "Water Elemental",
            }),
            [101] = ("Necromancy", new[]
            {
                "Animate Dead", "Blood Oath", "Corpse Skin", "Curse Weapon", "Evil Omen", "Horrific Beast", "Lich Form", "Mind Rot",
                "Pain Spike", "Poison Strike", "Strangle", "Summon Familiar", "Vampiric Embrace", "Vengeful Spirit", "Wither", "Wraith Form", "Exorcism",
            }),
            [201] = ("Chivalry", new[]
            {
                "Cleanse by Fire", "Close Wounds", "Consecrate Weapon", "Dispel Evil", "Divine Fury", "Enemy of One", "Holy Light",
                "Noble Sacrifice", "Remove Curse", "Sacred Journey",
            }),
            [401] = ("Bushido", new[] { "Honorable Execution", "Confidence", "Evasion", "Counter Attack", "Lightning Strike", "Momentum Strike" }),
            [501] = ("Ninjitsu", new[] { "Focus Attack", "Death Strike", "Animal Form", "Ki Attack", "Surprise Attack", "Backstab", "Shadowjump", "Mirror Image" }),
            [601] = ("Spellweaving", new[]
            {
                "Arcane Circle", "Gift of Renewal", "Immolating Weapon", "Attune Weapon", "Thunderstorm", "Nature's Fury", "Summon Fey", "Summon Fiend",
                "Reaper Form", "Wildfire", "Essence of Wind", "Dryad Allure", "Ethereal Voyage", "Word of Death", "Gift of Life", "Arcane Empowerment",
            }),
            [678] = ("Mysticism", new[]
            {
                "Nether Bolt", "Healing Stone", "Purge Magic", "Enchant", "Sleep", "Eagle Strike", "Animated Weapon", "Stone Form",
                "Spell Trigger", "Mass Sleep", "Cleansing Winds", "Bombard", "Spell Plague", "Hail Storm", "Nether Cyclone", "Rising Colossus",
            }),
        };

        /// <summary>Spellbook item graphics.</summary>
        public static bool IsSpellbook(ushort graphic) =>
            graphic == 0x0EFA || graphic == 0x2252 || graphic == 0x2253 || graphic == 0x238C || graphic == 0x23A0 ||
            graphic == 0x2D50 || graphic == 0x2D9D;

        /// <summary>
        /// The server opened a gump. The client never draws server gumps (Docs/GameplayDesign.md). It parses them so
        /// the few that matter (resurrection) can be answered from the client's own windows; the rest it leaves be.
        /// (Answering "closed" makes some gumps re-open themselves in a loop.)
        /// </summary>
        public event Action<ServerGump> ServerGumpReceived;

        /// <summary>Gumps that arrived before anything listened (e.g. at login, before the HUD exists).</summary>
        public readonly List<ServerGump> UnclaimedGumps = new List<ServerGump>();

        public int ActiveAbility { get; private set; }
        public event Action<int> ActiveAbilityChanged;

        /// <summary>Readies (or, used again, un-readies) a weapon special move (0xD7 0x19).</summary>
        public void UseWeaponAbility(int ability)
        {
            int next = ActiveAbility == ability ? 0 : ability;
            var packet = new PacketWriter(0xD7);
            packet.WriteUInt32(Player.Serial);
            packet.WriteUInt16(0x19);
            packet.WriteUInt32(0);
            packet.WriteByte((byte)next);
            packet.WriteByte(0x0A);
            _send(packet);
            ActiveAbility = next;
            ActiveAbilityChanged?.Invoke(next);
        }

        /// <summary>0xBF 0x21: the readied ability was used up or cancelled.</summary>
        private void HandleAbilityCleared()
        {
            ActiveAbility = 0;
            ActiveAbilityChanged?.Invoke(0);
        }

        private void HandleGump(PacketReader packet)
        {
            var gump = new ServerGump { Serial = packet.ReadUInt32(), GumpId = packet.ReadUInt32() };
            packet.ReadUInt32(); // x
            packet.ReadUInt32(); // y
            if (packet.Id == 0xB0)
            {
                gump.Layout = packet.ReadAscii(packet.ReadUInt16());
                int count = packet.ReadUInt16();
                var lines = new string[count];
                for (int i = 0; i < count; i++)
                {
                    lines[i] = ReadBigEndianUnicode(packet, packet.ReadUInt16());
                }

                gump.Lines = lines;
            }
            else
            {
                byte[] layout = Inflate(packet);
                gump.Layout = layout != null ? System.Text.Encoding.UTF8.GetString(layout).TrimEnd(' ') : string.Empty;
                int count = (int)packet.ReadUInt32();
                gump.Lines = new string[0];
                if (count > 0)
                {
                    byte[] text = Inflate(packet);
                    if (text != null)
                    {
                        var lines = new List<string>(count);
                        int pos = 0;
                        for (int i = 0; i < count && pos + 2 <= text.Length; i++)
                        {
                            int length = (text[pos] << 8) | text[pos + 1];
                            pos += 2;
                            var chars = new char[length];
                            for (int c = 0; c < length && pos + 1 < text.Length; c++, pos += 2)
                            {
                                chars[c] = (char)((text[pos] << 8) | text[pos + 1]);
                            }

                            lines.Add(new string(chars));
                        }

                        gump.Lines = lines.ToArray();
                    }
                }
            }

            if (ServerGumpReceived != null)
            {
                ServerGumpReceived(gump);
            }
            else
            {
                UnclaimedGumps.Add(gump);
            }
        }

        private static string ReadBigEndianUnicode(PacketReader packet, int length)
        {
            var chars = new char[length];
            for (int i = 0; i < length; i++)
            {
                chars[i] = (char)packet.ReadUInt16();
            }

            return new string(chars);
        }

        /// <summary>A zlib block in a 0xDD packet: compressed length + 4, decompressed length, then the data.</summary>
        private static byte[] Inflate(PacketReader packet)
        {
            int compressed = (int)packet.ReadUInt32() - 4;
            int size = (int)packet.ReadUInt32();
            if (compressed <= 2 || size <= 0 || compressed > packet.Remaining)
            {
                return null;
            }

            var data = new byte[compressed];
            for (int i = 0; i < compressed; i++)
            {
                data[i] = packet.ReadByte();
            }

            // zlib = 2-byte header + deflate data (+ checksum, which DeflateStream ignores).
            using (var input = new System.IO.MemoryStream(data, 2, compressed - 2))
            using (var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress))
            {
                var output = new byte[size];
                int read = 0, n;
                while (read < size && (n = deflate.Read(output, read, size - read)) > 0)
                {
                    read += n;
                }

                return output;
            }
        }

        /// <summary>Answers a gump as if its button `button` had been pressed (0 closes it), with these switches (checkboxes, radios) on.</summary>
        public void ReplyGump(ServerGump gump, int button, params int[] switches)
        {
            var reply = new PacketWriter(0xB1);
            reply.WriteUInt32(gump.Serial);
            reply.WriteUInt32(gump.GumpId);
            reply.WriteUInt32((uint)button);
            reply.WriteUInt32((uint)switches.Length);
            foreach (int s in switches)
            {
                reply.WriteUInt32((uint)s);
            }

            reply.WriteUInt32(0); // no text entries
            _send(reply);
        }

        private void HandleContextMenu(PacketReader packet)
        {
            ushort mode = packet.ReadUInt16();
            bool clilocIds = mode >= 2;
            uint serial = packet.ReadUInt32();
            int count = packet.ReadByte();
            var entries = new List<ContextEntry>(count);
            for (int i = 0; i < count; i++)
            {
                int number;
                ushort index, flags;
                if (clilocIds)
                {
                    number = (int)packet.ReadUInt32();
                    index = packet.ReadUInt16();
                    flags = packet.ReadUInt16();
                }
                else
                {
                    index = packet.ReadUInt16();
                    number = packet.ReadUInt16() + 3000000;
                    flags = packet.ReadUInt16();
                    if ((flags & 0x84) != 0)
                    {
                        packet.Skip(2);
                    }

                    if ((flags & 0x40) != 0)
                    {
                        packet.Skip(2);
                    }

                    if ((flags & 0x20) != 0)
                    {
                        packet.Skip(2);
                    }
                }

                entries.Add(new ContextEntry
                {
                    Index = index,
                    Text = Cliloc?.Get(number) ?? $"[{number}]",
                    Disabled = (flags & 0x01) != 0,
                });
            }

            ContextMenuReceived?.Invoke(serial, entries);
        }
    }
}
