using System;
using System.Collections.Generic;
using System.IO;

namespace UOClient.Data
{
    public struct LandCell
    {
        public ushort TileId;
        public sbyte Z;
    }

    public struct StaticTile
    {
        public ushort Graphic;
        public byte X, Y; // offset inside the block, 0-7
        public sbyte Z;
        public ushort Hue;
    }

    /// <summary>One loaded 8x8 map block: its land cells plus the statics placed on it.</summary>
    public sealed class MapBlock
    {
        public const int Size = 8;

        public readonly LandCell[] Land = new LandCell[Size * Size];
        public StaticTile[] Statics = Array.Empty<StaticTile>();

        public LandCell GetLand(int localX, int localY) => Land[(localY << 3) + localX];
    }

    /// <summary>
    /// Read-only access to one facet's land (mapNLegacyMUL.uop) and statics (staidxN/staticsN.mul).
    /// Blocks are read lazily and cached. Not thread-safe: use from one thread.
    /// </summary>
    public sealed class MapFacet : IDisposable
    {
        private const int BlocksPerUopFile = 4096;
        private const int LandBlockBytes = 4 + MapBlock.Size * MapBlock.Size * 3;
        private const int StaticIndexBytes = 12;
        private const int StaticBytes = 7;

        private static readonly (int Width, int Height)[] FacetSizes =
        {
            (7168, 4096), (7168, 4096), (2304, 1600), (2560, 2048), (1448, 1448), (1280, 4096),
        };

        private readonly FileStream _land;
        private readonly FileStream _staticIndex;
        private readonly FileStream _statics;
        private readonly long[] _uopOffsets;
        private readonly Dictionary<int, MapBlock> _cache = new Dictionary<int, MapBlock>();
        private readonly byte[] _landBuffer = new byte[LandBlockBytes];
        private readonly byte[] _indexBuffer = new byte[StaticIndexBytes];

        public MapFacet(string dataFolder, int facet)
        {
            Index = facet;
            (Width, Height) = FacetSizes[facet];
            BlocksWide = Width / MapBlock.Size;
            BlocksHigh = Height / MapBlock.Size;

            string uopPath = Path.Combine(dataFolder, $"map{facet}LegacyMUL.uop");
            int uopFiles = (BlocksWide * BlocksHigh + BlocksPerUopFile - 1) / BlocksPerUopFile;
            _uopOffsets = new UopIndex(uopPath, $"build/map{facet}legacymul/{{0:D8}}.dat", uopFiles).Offsets;

            _land = File.OpenRead(uopPath);
            _staticIndex = File.OpenRead(Path.Combine(dataFolder, $"staidx{facet}.mul"));
            _statics = File.OpenRead(Path.Combine(dataFolder, $"statics{facet}.mul"));
        }

        public int Index { get; }
        public int Width { get; }
        public int Height { get; }
        public int BlocksWide { get; }
        public int BlocksHigh { get; }

        /// <summary>Returns the block, or null outside the facet.</summary>
        public MapBlock GetBlock(int blockX, int blockY)
        {
            if (blockX < 0 || blockY < 0 || blockX >= BlocksWide || blockY >= BlocksHigh)
            {
                return null;
            }

            int blockNumber = blockX * BlocksHigh + blockY;
            if (_cache.TryGetValue(blockNumber, out MapBlock cached))
            {
                return cached;
            }

            var block = new MapBlock();
            ReadLand(blockNumber, block);
            ReadStatics(blockNumber, block);
            AddOverlays(blockNumber, block);
            _cache[blockNumber] = block;
            return block;
        }

        // ---- Multis laid over the map ----

        /// <summary>Tiles of server multis (houses, boats), by block number, so they read like map statics.</summary>
        private readonly Dictionary<int, List<(uint Owner, StaticTile Tile)>> _overlays = new Dictionary<int, List<(uint, StaticTile)>>();

        /// <summary>
        /// Lays a multi's tiles (at its centre x, y, z) over the map. Replaces any earlier tiles of the same owner.
        /// Returns the blocks whose contents changed.
        /// </summary>
        public HashSet<(int BlockX, int BlockY)> SetOverlay(uint owner, int x, int y, int z, IEnumerable<MultiTile> tiles)
        {
            HashSet<(int, int)> changed = RemoveOverlay(owner);
            foreach (MultiTile tile in tiles)
            {
                int tx = x + tile.X, ty = y + tile.Y;
                int bx = tx >> 3, by = ty >> 3;
                if (tx < 0 || ty < 0 || bx >= BlocksWide || by >= BlocksHigh)
                {
                    continue;
                }

                int number = bx * BlocksHigh + by;
                if (!_overlays.TryGetValue(number, out List<(uint, StaticTile)> list))
                {
                    _overlays[number] = list = new List<(uint, StaticTile)>();
                }

                list.Add((owner, new StaticTile { Graphic = tile.Graphic, X = (byte)(tx & 7), Y = (byte)(ty & 7), Z = (sbyte)Math.Max(sbyte.MinValue, Math.Min(sbyte.MaxValue, z + tile.Z)) }));
                changed.Add((bx, by));
            }

            foreach ((int bx, int by) in changed)
            {
                _cache.Remove(bx * BlocksHigh + by);
            }

            return changed;
        }

        /// <summary>Takes a multi's tiles off the map. Returns the blocks whose contents changed.</summary>
        public HashSet<(int BlockX, int BlockY)> RemoveOverlay(uint owner)
        {
            var changed = new HashSet<(int, int)>();
            foreach (KeyValuePair<int, List<(uint Owner, StaticTile Tile)>> pair in _overlays)
            {
                if (pair.Value.RemoveAll(t => t.Owner == owner) > 0)
                {
                    changed.Add((pair.Key / BlocksHigh, pair.Key % BlocksHigh));
                }
            }

            foreach ((int bx, int by) in changed)
            {
                _cache.Remove(bx * BlocksHigh + by);
            }

            return changed;
        }

        private void AddOverlays(int blockNumber, MapBlock block)
        {
            if (!_overlays.TryGetValue(blockNumber, out List<(uint Owner, StaticTile Tile)> list) || list.Count == 0)
            {
                return;
            }

            var statics = new StaticTile[block.Statics.Length + list.Count];
            Array.Copy(block.Statics, statics, block.Statics.Length);
            for (int i = 0; i < list.Count; i++)
            {
                statics[block.Statics.Length + i] = list[i].Tile;
            }

            block.Statics = statics;
        }

        public LandCell GetLand(int x, int y)
        {
            MapBlock block = GetBlock(x >> 3, y >> 3);
            return block?.GetLand(x & 7, y & 7) ?? default;
        }

        /// <summary>Drops cached blocks farther than <paramref name="keepRadius"/> blocks from a point.</summary>
        public void Trim(int centerBlockX, int centerBlockY, int keepRadius)
        {
            var remove = new List<int>();
            foreach (int key in _cache.Keys)
            {
                int bx = key / BlocksHigh;
                int by = key % BlocksHigh;
                if (Math.Abs(bx - centerBlockX) > keepRadius || Math.Abs(by - centerBlockY) > keepRadius)
                {
                    remove.Add(key);
                }
            }

            foreach (int key in remove)
            {
                _cache.Remove(key);
            }
        }

        public void Dispose()
        {
            _land.Dispose();
            _staticIndex.Dispose();
            _statics.Dispose();
        }

        private void ReadLand(int blockNumber, MapBlock block)
        {
            long fileOffset = _uopOffsets[blockNumber / BlocksPerUopFile];
            if (fileOffset < 0)
            {
                return;
            }

            _land.Seek(fileOffset + (long)(blockNumber % BlocksPerUopFile) * LandBlockBytes, SeekOrigin.Begin);
            ReadFully(_land, _landBuffer);

            for (int i = 0; i < block.Land.Length; i++)
            {
                int at = 4 + i * 3;
                block.Land[i] = new LandCell
                {
                    TileId = (ushort)(_landBuffer[at] | (_landBuffer[at + 1] << 8)),
                    Z = (sbyte)_landBuffer[at + 2],
                };
            }
        }

        private void ReadStatics(int blockNumber, MapBlock block)
        {
            long indexAt = (long)blockNumber * StaticIndexBytes;
            if (indexAt + StaticIndexBytes > _staticIndex.Length)
            {
                return;
            }

            _staticIndex.Seek(indexAt, SeekOrigin.Begin);
            ReadFully(_staticIndex, _indexBuffer);
            uint position = BitConverter.ToUInt32(_indexBuffer, 0);
            int length = BitConverter.ToInt32(_indexBuffer, 4);
            if (position == 0xFFFFFFFF || length <= 0)
            {
                return;
            }

            byte[] data = new byte[length];
            _statics.Seek(position, SeekOrigin.Begin);
            ReadFully(_statics, data);

            int count = length / StaticBytes;
            block.Statics = new StaticTile[count];
            for (int i = 0; i < count; i++)
            {
                int at = i * StaticBytes;
                block.Statics[i] = new StaticTile
                {
                    Graphic = (ushort)(data[at] | (data[at + 1] << 8)),
                    X = data[at + 2],
                    Y = data[at + 3],
                    Z = (sbyte)data[at + 4],
                    Hue = (ushort)(data[at + 5] | (data[at + 6] << 8)),
                };
            }
        }

        private static void ReadFully(Stream stream, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0)
                {
                    throw new EndOfStreamException();
                }

                read += n;
            }
        }
    }
}
