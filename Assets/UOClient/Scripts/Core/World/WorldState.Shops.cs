using System;
using System.Collections.Generic;
using UOClient.Net;

namespace UOClient.World
{
    /// <summary>One thing a vendor sells (or will buy from you).</summary>
    public sealed class ShopEntry
    {
        public uint Serial;
        public ushort Graphic;
        public ushort Hue;
        /// <summary>The server's name for it: text, or a cliloc number as text ("1023834"). Prefer its property name.</summary>
        public string Name;
        public int Price;
        /// <summary>How many the vendor has (buying) or you have (selling).</summary>
        public int Available;
    }

    /// <summary>A shop as plain data: who, buying or selling, and the entries. The shop window only sees this.</summary>
    public sealed class ShopOffer
    {
        public uint Vendor;
        public bool Selling;
        public readonly List<ShopEntry> Entries = new List<ShopEntry>();
    }

    /// <summary>
    /// Vendors in the UO protocol. Buying: the vendor's stock arrives as container contents (0x3C), then prices
    /// (0x74) that pair up with those items in reverse arrival order, then a "show the shop" open (0x24 gump 0x30).
    /// Selling: one list of your sellable items with prices (0x9E). Purchases go back as 0x3B, sales as 0x9F.
    /// </summary>
    public sealed partial class WorldState
    {
        private const byte ShopBuyLayer = 0x1A;
        private const byte ShopResaleLayer = 0x1B;

        /// <summary>A shop is ready to show.</summary>
        public event Action<ShopOffer> ShopOpened;

        private ShopOffer _pendingBuy;

        public void Buy(uint vendor, IEnumerable<(uint Serial, int Amount)> items)
        {
            var packet = new PacketWriter(0x3B);
            packet.WriteUInt32(vendor);
            packet.WriteByte(0x02);
            foreach ((uint serial, int amount) in items)
            {
                packet.WriteByte(ShopBuyLayer);
                packet.WriteUInt32(serial);
                packet.WriteUInt16((ushort)Math.Max(1, Math.Min(amount, ushort.MaxValue)));
            }

            _send(packet);
        }

        public void Sell(uint vendor, IList<(uint Serial, int Amount)> items)
        {
            var packet = new PacketWriter(0x9F);
            packet.WriteUInt32(vendor);
            packet.WriteUInt16((ushort)items.Count);
            foreach ((uint serial, int amount) in items)
            {
                packet.WriteUInt32(serial);
                packet.WriteUInt16((ushort)Math.Max(1, Math.Min(amount, ushort.MaxValue)));
            }

            _send(packet);
        }

        /// <summary>0x74: prices for one of the vendor's stock containers (there can be two: stock and resale).</summary>
        private void HandleBuyList(PacketReader packet)
        {
            uint container = packet.ReadUInt32();
            int count = packet.ReadByte();
            uint vendor = VendorWearing(container, out ushort containerGraphic);
            if (vendor == 0)
            {
                return;
            }

            var items = new List<ContainedItem>();
            foreach (ContainedItem item in Contained.Values)
            {
                if (item.Container == container)
                {
                    items.Add(item);
                }
            }

            // The original client pairs prices with the container's items newest-first, except in this one
            // container graphic, where it sorts them by x.
            if (containerGraphic == 0x2AF8)
            {
                items.Sort((a, b) => a.X.CompareTo(b.X));
            }
            else
            {
                items.Sort((a, b) => b.Order.CompareTo(a.Order));
            }

            if (_pendingBuy == null || _pendingBuy.Vendor != vendor)
            {
                _pendingBuy = new ShopOffer { Vendor = vendor, Selling = false };
            }

            for (int i = 0; i < count && i < items.Count; i++)
            {
                int price = (int)packet.ReadUInt32();
                int nameLength = packet.ReadByte();
                string name = packet.ReadAscii(nameLength);
                _pendingBuy.Entries.Add(new ShopEntry
                {
                    Serial = items[i].Serial,
                    Graphic = items[i].Graphic,
                    Hue = items[i].Hue,
                    Name = name,
                    Price = price,
                    Available = items[i].Amount,
                });
            }
        }

        /// <summary>The 0x24 with gump 0x30 that follows the buy lists: show the shop.</summary>
        private bool TryOpenShop(uint serial, ushort gump)
        {
            if (gump != 0x30)
            {
                return false;
            }

            ShopOffer offer = _pendingBuy != null && _pendingBuy.Vendor == serial ? _pendingBuy : new ShopOffer { Vendor = serial };
            _pendingBuy = null;
            ShopOpened?.Invoke(offer);
            return true;
        }

        private void HandleSellList(PacketReader packet)
        {
            var offer = new ShopOffer { Vendor = packet.ReadUInt32(), Selling = true };
            int count = packet.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                var entry = new ShopEntry
                {
                    Serial = packet.ReadUInt32(),
                    Graphic = packet.ReadUInt16(),
                    Hue = packet.ReadUInt16(),
                    Available = packet.ReadUInt16(),
                    Price = packet.ReadUInt16(),
                };
                entry.Name = packet.ReadAscii(packet.ReadUInt16());
                offer.Entries.Add(entry);
            }

            ShopOpened?.Invoke(offer);
        }

        /// <summary>The vendor wearing a shop container (layer 0x1A / 0x1B), and that container's graphic.</summary>
        private uint VendorWearing(uint container, out ushort graphic)
        {
            foreach (Mobile mobile in Mobiles.Values)
            {
                foreach (KeyValuePair<byte, EquippedItem> pair in mobile.Equipment)
                {
                    if (pair.Value.Serial == container && (pair.Key == ShopBuyLayer || pair.Key == ShopResaleLayer))
                    {
                        graphic = pair.Value.Graphic;
                        return mobile.Serial;
                    }
                }
            }

            graphic = 0;
            return 0;
        }

        /// <summary>A display name for a shop entry: its property name, else the server's name (a cliloc number or text).</summary>
        public string ShopName(ShopEntry entry)
        {
            string[] lines = PropertiesOf(entry.Serial);
            if (lines != null && lines.Length > 0)
            {
                return lines[0];
            }

            if (int.TryParse(entry.Name, out int number) && Cliloc != null)
            {
                string text = Cliloc.Get(number);
                if (!string.IsNullOrEmpty(text))
                {
                    return text.Replace("~1_NAME~", string.Empty).Replace("~1~", string.Empty).Trim();
                }
            }

            return entry.Name;
        }
    }
}
