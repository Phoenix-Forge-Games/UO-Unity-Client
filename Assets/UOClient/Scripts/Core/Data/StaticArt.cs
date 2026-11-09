using System;
using System.Collections.Generic;
using System.IO;

namespace UOClient.Data
{
    /// <summary>A static's image as an opacity mask (true = drawn pixel), row-major from the top.</summary>
    public sealed class ArtMask
    {
        public int Width;
        public int Height;
        public bool[] Opaque;
        /// <summary>Share of drawn pixels that are dark (timber beams, shadowed planks), 0-1.</summary>
        public float DarkShare;

        public bool this[int x, int y] => x >= 0 && y >= 0 && x < Width && y < Height && Opaque[y * Width + x];
    }

    /// <summary>
    /// Reads static art from artLegacyMUL.uop. Only shapes are needed (wall orientation), so
    /// pixels decode to an opacity mask. Results are cached; not thread-safe.
    /// </summary>
    public sealed class StaticArt : IDisposable
    {
        private const int LandEntries = 0x4000;
        private const int StaticEntries = 0x10000;
        private const int MaxDimension = 1024;
        private const float DarkLuma = 0.3f;

        private readonly FileStream _file;
        private readonly UopIndex _index;
        private readonly Dictionary<ushort, ArtMask> _cache = new Dictionary<ushort, ArtMask>();

        public StaticArt(string dataFolder)
        {
            string path = Path.Combine(dataFolder, "artLegacyMUL.uop");
            _index = new UopIndex(path, "build/artlegacymul/{0:D8}.tga", LandEntries + StaticEntries);
            _file = File.OpenRead(path);
        }

        /// <summary>The mask for a static graphic, or null if it has no art.</summary>
        public ArtMask Get(ushort graphic)
        {
            if (_cache.TryGetValue(graphic, out ArtMask cached))
            {
                return cached;
            }

            ArtMask mask = Load(LandEntries + graphic);
            _cache[graphic] = mask;
            return mask;
        }

        public void Dispose() => _file.Dispose();

        private ArtMask Load(int entry)
        {
            long offset = _index.Offsets[entry];
            int length = _index.Lengths[entry];
            if (offset < 0 || length < 12)
            {
                return null;
            }

            byte[] data = new byte[length];
            _file.Seek(offset, SeekOrigin.Begin);
            int read = 0;
            while (read < length)
            {
                int n = _file.Read(data, read, length - read);
                if (n <= 0)
                {
                    return null;
                }

                read += n;
            }

            // Header: flags (4), width (2), height (2); then one row-offset word per row, then the runs.
            int width = BitConverter.ToInt16(data, 4);
            int height = BitConverter.ToInt16(data, 6);
            if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension)
            {
                return null;
            }

            int lookup = 8;
            int dataStart = lookup + height * 2;
            var mask = new ArtMask { Width = width, Height = height, Opaque = new bool[width * height] };
            int drawn = 0, dark = 0;

            for (int y = 0; y < height; y++)
            {
                int at = dataStart + BitConverter.ToUInt16(data, lookup + y * 2) * 2;
                int x = 0;
                while (at + 4 <= data.Length)
                {
                    int skip = BitConverter.ToUInt16(data, at);
                    int run = BitConverter.ToUInt16(data, at + 2);
                    at += 4;
                    if (skip + run == 0)
                    {
                        break;
                    }

                    x += skip;
                    for (int i = 0; i < run && at + 1 < data.Length; i++, at += 2)
                    {
                        int px = x + i;
                        ushort color = BitConverter.ToUInt16(data, at);
                        if (px < width && color != 0)
                        {
                            mask.Opaque[y * width + px] = true;
                            drawn++;
                            // 1555 colour: 5 bits each of red, green, blue.
                            float luma = (((color >> 10) & 31) * 0.3f + ((color >> 5) & 31) * 0.59f + (color & 31) * 0.11f) / 31f;
                            if (luma < DarkLuma)
                            {
                                dark++;
                            }
                        }
                    }

                    x += run;
                }
            }

            mask.DarkShare = drawn > 0 ? (float)dark / drawn : 0f;
            return mask;
        }
    }
}
