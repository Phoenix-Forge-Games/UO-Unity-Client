using System;
using System.Collections.Generic;
using System.IO;

namespace UOClient.Data
{
    /// <summary>One tile of a multi (house, boat, tent): a static graphic at an offset from the multi's centre.</summary>
    public readonly struct MultiTile
    {
        public readonly ushort Graphic;
        public readonly short X, Y, Z;

        public MultiTile(ushort graphic, short x, short y, short z)
        {
            Graphic = graphic;
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// multi.idx / multi.mul: the tiles of every multi by ID (UO data, read for layout only; nothing of UO's art is
    /// drawn). Entries are 16 bytes from client 7.0.9 on (graphic, x, y, z, flags, 4 more bytes); tiles with
    /// flags 0 are invisible markers and are skipped. Layout as in ClassicUO's MultiLoader.
    /// </summary>
    public sealed class MultiData : IDisposable
    {
        private const int EntryBytes = 16;

        private readonly FileStream _index;
        private readonly FileStream _data;
        private readonly Dictionary<int, MultiTile[]> _cache = new Dictionary<int, MultiTile[]>();

        public MultiData(string dataFolder)
        {
            _index = File.OpenRead(Path.Combine(dataFolder, "multi.idx"));
            _data = File.OpenRead(Path.Combine(dataFolder, "multi.mul"));
        }

        private readonly Dictionary<int, (int MinX, int MinY, int MaxX, int MaxY)> _bounds = new Dictionary<int, (int, int, int, int)>();

        /// <summary>A multi's extent over all its tiles, invisible markers included (custom house designs use it).</summary>
        public (int MinX, int MinY, int MaxX, int MaxY) Bounds(int id)
        {
            if (!_bounds.ContainsKey(id))
            {
                Read(id);
            }

            return _bounds.TryGetValue(id, out var b) ? b : (0, 0, 0, 0);
        }

        public MultiTile[] Get(int id)
        {
            if (_cache.TryGetValue(id, out MultiTile[] tiles))
            {
                return tiles;
            }

            tiles = Read(id);
            _cache[id] = tiles;
            return tiles;
        }

        private MultiTile[] Read(int id)
        {
            var entry = new byte[12];
            if ((long)id * 12 + 12 > _index.Length)
            {
                return Array.Empty<MultiTile>();
            }

            _index.Seek((long)id * 12, SeekOrigin.Begin);
            _index.Read(entry, 0, 12);
            int offset = BitConverter.ToInt32(entry, 0);
            int length = BitConverter.ToInt32(entry, 4);
            if (offset < 0 || length <= 0 || offset + length > _data.Length)
            {
                return Array.Empty<MultiTile>();
            }

            var data = new byte[length];
            _data.Seek(offset, SeekOrigin.Begin);
            _data.Read(data, 0, length);
            var list = new List<MultiTile>(length / EntryBytes);
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int p = 0; p + EntryBytes <= length; p += EntryBytes)
            {
                ushort graphic = BitConverter.ToUInt16(data, p);
                short x = BitConverter.ToInt16(data, p + 2);
                short y = BitConverter.ToInt16(data, p + 4);
                short z = BitConverter.ToInt16(data, p + 6);
                uint flags = BitConverter.ToUInt32(data, p + 8);
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                if (flags != 0)
                {
                    list.Add(new MultiTile(graphic, x, y, z));
                }
            }

            if (minX != int.MaxValue)
            {
                _bounds[id] = (minX, minY, maxX, maxY);
            }

            return list.ToArray();
        }

        public void Dispose()
        {
            _index.Dispose();
            _data.Dispose();
        }
    }
}
