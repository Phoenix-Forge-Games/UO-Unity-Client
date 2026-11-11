using System;
using System.Collections.Generic;
using UOClient.Data;
using UOClient.Net;

namespace UOClient.World
{
    /// <summary>
    /// Player-designed (custom) houses. The server announces each house's design revision (0xBF 0x1D); the client
    /// asks for a design it doesn't have (0xBF 0x1E) and gets it as zlib-compressed planes of tiles (0xD8). The
    /// design is handed on as plain tiles relative to the house's foundation; the world view lays them over the map
    /// for the house generator. Plane layout as in ClassicUO / TazUO (CustomHouse, HouseHelpers).
    /// </summary>
    public sealed partial class WorldState
    {
        /// <summary>A custom house's design arrived: the foundation's serial and its tiles (offsets from the foundation).</summary>
        public event Action<uint, List<MultiTile>> HouseDesignReceived;

        /// <summary>A multi's full extent by ID (set by the world view from multi.mul); mode-2 planes need it.</summary>
        public Func<int, (int MinX, int MinY, int MaxX, int MaxY)> MultiBounds { get; set; }

        private readonly Dictionary<uint, uint> _designRevisions = new Dictionary<uint, uint>();

        /// <summary>The latest design of every custom house seen, for views that start after it arrived.</summary>
        public readonly Dictionary<uint, List<MultiTile>> HouseDesigns = new Dictionary<uint, List<MultiTile>>();

        private void HandleDesignRevision(PacketReader packet)
        {
            uint serial = packet.ReadUInt32();
            uint revision = packet.ReadUInt32();
            if (_designRevisions.TryGetValue(serial, out uint known) && known == revision)
            {
                return;
            }

            RequestHouseDesign(serial);
        }

        /// <summary>Asks for a house's design (0xBF 0x1E); the answer is a 0xD8 for custom houses, nothing for others.</summary>
        public void RequestHouseDesign(uint serial)
        {
            var request = new PacketWriter(0xBF);
            request.WriteUInt16(0x1E);
            request.WriteUInt32(serial);
            _send(request);
        }

        private void HandleHouseDesign(PacketReader packet)
        {
            packet.ReadByte(); // 0x03: compressed
            packet.ReadBool(); // enable response
            uint serial = packet.ReadUInt32();
            uint revision = packet.ReadUInt32();
            if (!Items.TryGetValue(serial, out WorldItem foundation) || !foundation.IsMulti || MultiBounds == null)
            {
                return;
            }

            (int minX, int minY, int _, int maxY) = MultiBounds(foundation.Graphic & 0x3FFF);
            packet.Skip(4);
            int planes = packet.ReadByte();
            var tiles = new List<MultiTile>();
            for (int plane = 0; plane < planes; plane++)
            {
                uint header = packet.ReadUInt32();
                int dlen = (int)(((header & 0xFF0000) >> 16) | ((header & 0xF0) << 4));
                int clen = (int)(((header & 0xFF00) >> 8) | ((header & 0x0F) << 8));
                int planeZ = (int)((header & 0x0F000000) >> 24);
                int mode = (int)((header & 0xF0000000) >> 28);
                if (clen <= 0 || clen > packet.Remaining)
                {
                    break;
                }

                byte[] data = InflateRaw(packet, clen, dlen);
                if (data != null)
                {
                    ReadPlane(data, planeZ, mode, minX, minY, maxY, tiles);
                }
            }

            _designRevisions[serial] = revision;
            HouseDesigns[serial] = tiles;
            HouseDesignReceived?.Invoke(serial, tiles);
        }

        private static void ReadPlane(byte[] d, int planeZ, int mode, int minX, int minY, int maxY, List<MultiTile> into)
        {
            int z = planeZ > 0 ? (planeZ - 1) % 4 * 20 + 7 : 0;
            switch (mode)
            {
                case 0: // id, x, y, z
                    for (int i = 0; i + 5 <= d.Length; i += 5)
                    {
                        ushort id = (ushort)((d[i] << 8) | d[i + 1]);
                        if (id != 0)
                        {
                            into.Add(new MultiTile(id, (sbyte)d[i + 2], (sbyte)d[i + 3], (sbyte)d[i + 4]));
                        }
                    }

                    break;
                case 1: // id, x, y at the plane's height
                    for (int i = 0; i + 4 <= d.Length; i += 4)
                    {
                        ushort id = (ushort)((d[i] << 8) | d[i + 1]);
                        if (id != 0)
                        {
                            into.Add(new MultiTile(id, (sbyte)d[i + 2], (sbyte)d[i + 3], (short)z));
                        }
                    }

                    break;
                case 2: // ids only, column by column over the house's extent
                    int offX, offY, height;
                    if (planeZ <= 0)
                    {
                        offX = minX;
                        offY = minY;
                        height = maxY - minY + 2;
                    }
                    else if (planeZ <= 4)
                    {
                        offX = minX + 1;
                        offY = minY + 1;
                        height = maxY - minY;
                    }
                    else
                    {
                        offX = minX;
                        offY = minY;
                        height = maxY - minY + 1;
                    }

                    if (height <= 0)
                    {
                        break;
                    }

                    for (int i = 0; i * 2 + 2 <= d.Length; i++)
                    {
                        ushort id = (ushort)((d[i * 2] << 8) | d[i * 2 + 1]);
                        if (id != 0)
                        {
                            into.Add(new MultiTile(id, (short)(i / height + offX), (short)(i % height + offY), (short)z));
                        }
                    }

                    break;
            }
        }

        /// <summary>A zlib block of known compressed / uncompressed size (house design planes).</summary>
        private static byte[] InflateRaw(PacketReader packet, int compressed, int size)
        {
            var data = new byte[compressed];
            for (int i = 0; i < compressed; i++)
            {
                data[i] = packet.ReadByte();
            }

            if (compressed <= 2 || size <= 0)
            {
                return null;
            }

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
    }
}
