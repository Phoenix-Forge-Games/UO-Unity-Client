using System;
using System.Collections.Generic;
using UOClient.Net;

namespace UOClient.World
{
    /// <summary>
    /// The client's view of the world, built from server packets: the player, nearby mobiles and
    /// ground items, and the current facet. Fed by <see cref="UoSession.PacketReceived"/>, so it is
    /// only touched from the thread that polls the session. Events fire synchronously from
    /// <see cref="Handle"/>.
    /// </summary>
    public sealed partial class WorldState
    {
        public event Action<Mobile> MobileUpdated;
        public event Action<uint> MobileRemoved;
        public event Action<WorldItem> ItemUpdated;
        public event Action<uint> ItemRemoved;
        public event Action<int> FacetChanged;
        /// <summary>The server moved the player directly (login, teleport, or a refused step).</summary>
        public event Action<Mobile> PlayerTeleported;

        public readonly Dictionary<uint, Mobile> Mobiles = new Dictionary<uint, Mobile>();
        public readonly Dictionary<uint, WorldItem> Items = new Dictionary<uint, WorldItem>();

        public WorldState(Action<PacketWriter> send)
        {
            Walker = new Walker(this, send);
            _send = send;
        }

        private readonly Action<PacketWriter> _send;

        public Mobile Player { get; private set; }
        public int Facet { get; private set; } = 1;
        public Walker Walker { get; }

        /// <summary>Resets everything for a fresh login.</summary>
        public void Begin(LoginConfirm confirm)
        {
            Mobiles.Clear();
            Items.Clear();
            ResetSocial();
            ResetItems();
            Player = new Mobile
            {
                Serial = confirm.Serial,
                Body = confirm.Body,
                X = confirm.X,
                Y = confirm.Y,
                Z = confirm.Z,
                Facing = (Direction)(confirm.Direction & DirectionUtil.Mask),
            };
            Mobiles[Player.Serial] = Player;
            Walker.Reset();
            PlayerTeleported?.Invoke(Player);
            MobileUpdated?.Invoke(Player);
        }

        public void Handle(PacketReader packet)
        {
            switch (packet.Id)
            {
                case 0x20: HandlePlayerUpdate(packet); break;
                case 0x78: HandleMobileIncoming(packet); break;
                case 0x77: HandleMobileMoving(packet); break;
                case 0x1D: HandleDelete(packet); break;
                case 0xF3: HandleWorldItem(packet); break;
                case 0x2E: HandleEquip(packet); break;
                case 0x11: HandleStatus(packet); break;
                case 0x1C: HandleAsciiMessage(packet); break;
                case 0xAE: HandleUnicodeMessage(packet); break;
                case 0xC1: HandleClilocMessage(packet, false); break;
                case 0xCC: HandleClilocMessage(packet, true); break;
                case 0xA1: HandleStat(packet, StatKind.Hits); break;
                case 0xA2: HandleStat(packet, StatKind.Mana); break;
                case 0xA3: HandleStat(packet, StatKind.Stamina); break;
                case 0x2D: HandleAttributes(packet); break;
                case 0x72: HandleWarMode(packet); break;
                case 0xAA: HandleCombatant(packet); break;
                case 0x0B: HandleDamage(packet); break;
                case 0x98: HandleName(packet); break;
                case 0x24: HandleOpenContainer(packet); break;
                case 0x3C: HandleContainerContents(packet); break;
                case 0x25: HandleContainedItem(packet); break;
                case 0x27: HandleDragRejected(packet); break;
                case 0xDC: HandlePropertyRevision(packet); break;
                case 0xD6: HandleProperties(packet); break;
                case 0x3A: HandleSkills(packet); break;
                case 0x74: HandleBuyList(packet); break;
                case 0x6C: HandleTargetRequest(packet); break;
                case 0x99: HandleMultiPlacement(packet); break;
                case 0xD8: HandleHouseDesign(packet); break;
                case 0xB0: HandleGump(packet); break;
                case 0xDF: HandleBuff(packet); break;
                case 0xDD: HandleGump(packet); break;
                case 0x9E: HandleSellList(packet); break;
                case 0x21: Walker.OnDenied(packet); break;
                case 0x22: Walker.OnConfirmed(packet); break;
                case 0xBF: HandleExtended(packet); break;
            }
        }

        /// <summary>Called by the walker when the server refuses a step and snaps the player back.</summary>
        internal void RaisePlayerTeleported() => PlayerTeleported?.Invoke(Player);

        internal void RaiseMobileUpdated(Mobile mobile) => MobileUpdated?.Invoke(mobile);

        private void HandlePlayerUpdate(PacketReader packet)
        {
            uint serial = packet.ReadUInt32();
            if (Player == null || serial != Player.Serial)
            {
                return;
            }

            Player.Body = packet.ReadUInt16();
            packet.ReadByte();
            Player.Hue = packet.ReadUInt16();
            Player.Flags = packet.ReadByte();
            Player.X = packet.ReadUInt16();
            Player.Y = packet.ReadUInt16();
            packet.ReadUInt16();
            byte direction = packet.ReadByte();
            Player.Facing = (Direction)(direction & DirectionUtil.Mask);
            Player.Z = packet.ReadSByte();

            Walker.Reset();
            PlayerTeleported?.Invoke(Player);
            MobileUpdated?.Invoke(Player);
        }

        private void HandleMobileIncoming(PacketReader packet)
        {
            Mobile mobile = GetOrCreateMobile(packet.ReadUInt32());
            mobile.Body = packet.ReadUInt16();
            int x = packet.ReadUInt16();
            int y = packet.ReadUInt16();
            int z = packet.ReadSByte();
            byte direction = packet.ReadByte();
            mobile.Hue = packet.ReadUInt16();
            mobile.Flags = packet.ReadByte();
            mobile.Notoriety = (Notoriety)packet.ReadByte();

            // The player's position is owned by the walker once in the world.
            if (mobile != Player)
            {
                SetPosition(mobile, x, y, z, direction);
            }

            mobile.Equipment.Clear();
            uint itemSerial = packet.ReadUInt32();
            while (itemSerial != 0 && packet.Remaining > 0)
            {
                var item = new EquippedItem
                {
                    Serial = itemSerial,
                    Graphic = packet.ReadUInt16(),
                    Layer = packet.ReadByte(),
                    Hue = packet.ReadUInt16(),
                };
                mobile.Equipment[item.Layer] = item;
                itemSerial = packet.ReadUInt32();
            }

            MobileUpdated?.Invoke(mobile);
        }

        private void HandleMobileMoving(PacketReader packet)
        {
            uint serial = packet.ReadUInt32();
            if (Player != null && serial == Player.Serial)
            {
                return;
            }

            Mobile mobile = GetOrCreateMobile(serial);
            mobile.Body = packet.ReadUInt16();
            int x = packet.ReadUInt16();
            int y = packet.ReadUInt16();
            int z = packet.ReadSByte();
            byte direction = packet.ReadByte();
            mobile.Hue = packet.ReadUInt16();
            mobile.Flags = packet.ReadByte();
            mobile.Notoriety = (Notoriety)packet.ReadByte();
            SetPosition(mobile, x, y, z, direction);
            MobileUpdated?.Invoke(mobile);
        }

        private void HandleDelete(PacketReader packet)
        {
            uint serial = packet.ReadUInt32();
            if (Player != null && serial == Player.Serial)
            {
                return;
            }

            if (Mobiles.Remove(serial))
            {
                MobileRemoved?.Invoke(serial);
            }
            else if (Items.Remove(serial))
            {
                ItemRemoved?.Invoke(serial);
            }
            else if (RemoveContained(serial))
            {
            }
            else
            {
                // A worn item taken off: the server deletes it from the wearer.
                foreach (Mobile mobile in Mobiles.Values)
                {
                    byte? layer = null;
                    foreach (KeyValuePair<byte, EquippedItem> pair in mobile.Equipment)
                    {
                        if (pair.Value.Serial == serial)
                        {
                            layer = pair.Key;
                            break;
                        }
                    }

                    if (layer.HasValue)
                    {
                        mobile.Equipment.Remove(layer.Value);
                        MobileUpdated?.Invoke(mobile);
                        break;
                    }
                }
            }
        }

        private void HandleWorldItem(PacketReader packet)
        {
            packet.ReadUInt16(); // always 1
            byte type = packet.ReadByte();
            var item = new WorldItem { Serial = packet.ReadUInt32() };
            item.Graphic = (ushort)(packet.ReadUInt16() + packet.ReadByte());
            item.Amount = packet.ReadUInt16();
            packet.ReadUInt16();
            item.X = packet.ReadUInt16();
            item.Y = packet.ReadUInt16();
            item.Z = packet.ReadSByte();
            packet.ReadByte(); // light level / direction
            item.Hue = packet.ReadUInt16();
            item.IsMulti = type == 2;

            Items[item.Serial] = item;
            RemoveContained(item.Serial); // dropped out of a container onto the ground
            ItemUpdated?.Invoke(item);
        }

        private void HandleEquip(PacketReader packet)
        {
            var item = new EquippedItem { Serial = packet.ReadUInt32() };
            item.Graphic = (ushort)(packet.ReadUInt16() + packet.ReadSByte());
            item.Layer = packet.ReadByte();
            uint owner = packet.ReadUInt32();
            item.Hue = packet.ReadUInt16();

            OnEquipped(item.Serial);
            if (Mobiles.TryGetValue(owner, out Mobile mobile))
            {
                mobile.Equipment[item.Layer] = item;
                MobileUpdated?.Invoke(mobile);
            }
        }

        private void HandleExtended(PacketReader packet)
        {
            ushort command = packet.ReadUInt16();
            switch (command)
            {
                case 0x01: // fast-walk key stack
                    for (int i = 0; i < 6; i++)
                    {
                        Walker.PushFastWalkKey(packet.ReadUInt32());
                    }

                    break;
                case 0x02:
                    Walker.PushFastWalkKey(packet.ReadUInt32());
                    break;
                case 0x08:
                    Facet = packet.ReadByte();
                    FacetChanged?.Invoke(Facet);
                    break;
                case 0x14:
                    HandleContextMenu(packet);
                    break;
                case 0x1B:
                    HandleSpellbookContent(packet);
                    break;
                case 0x21:
                    HandleAbilityCleared();
                    break;
                case 0x1D:
                    HandleDesignRevision(packet);
                    break;
            }
        }

        private Mobile GetOrCreateMobile(uint serial)
        {
            if (!Mobiles.TryGetValue(serial, out Mobile mobile))
            {
                mobile = new Mobile { Serial = serial };
                Mobiles[serial] = mobile;
            }

            return mobile;
        }

        private static void SetPosition(Mobile mobile, int x, int y, int z, byte direction)
        {
            mobile.X = x;
            mobile.Y = y;
            mobile.Z = z;
            mobile.Facing = (Direction)(direction & DirectionUtil.Mask);
            mobile.Running = (direction & DirectionUtil.RunningFlag) != 0;
        }
    }
}
