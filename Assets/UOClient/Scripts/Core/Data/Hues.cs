using System;
using System.IO;

namespace UOClient.Data
{
    /// <summary>
    /// hues.mul: the dye and skin colours the server refers to by number. Hues come in groups of 8
    /// (a 4-byte header, then 8 × [32 colours, table start, table end, 20-char name]); each hue is a
    /// ramp of 32 shades from dark to light in 1-5-5-5 RGB, which UO maps grey art onto.
    /// </summary>
    public sealed class Hues
    {
        private const int EntrySize = 32 * 2 + 2 + 2 + 20;
        private const int GroupSize = 4 + 8 * EntrySize;

        private readonly ushort[][] _ramps;

        public Hues(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            int groups = data.Length / GroupSize;
            _ramps = new ushort[groups * 8][];
            for (int g = 0; g < groups; g++)
            {
                int offset = g * GroupSize + 4;
                for (int e = 0; e < 8; e++, offset += EntrySize)
                {
                    var ramp = new ushort[32];
                    for (int i = 0; i < 32; i++)
                    {
                        ramp[i] = BitConverter.ToUInt16(data, offset + i * 2);
                    }

                    _ramps[g * 8 + e] = ramp;
                }
            }
        }

        public int Count => _ramps.Length;

        /// <summary>
        /// The one shade of a hue the game shows: what a colour swatch shows, and exactly what hair, skin and dyed
        /// things are drawn in (no mapping through UO's ramp by the texture's brightness, which turned blonde brown).
        /// </summary>
        public const int SwatchShade = 22;

        /// <summary>
        /// Shade 0 (dark) to 31 (light) of a hue as the server sends it (1-based; the top bits are flags),
        /// as 8-bit RGB; false for hue 0 (the art's own colours) or an unknown hue.
        /// </summary>
        public bool TryShade(ushort hue, int shade, out byte r, out byte g, out byte b)
        {
            int index = (hue & 0x3FFF) - 1;
            if (index < 0 || index >= _ramps.Length)
            {
                r = g = b = 0;
                return false;
            }

            ushort c = _ramps[index][Math.Max(0, Math.Min(31, shade))];
            r = (byte)(((c >> 10) & 31) * 255 / 31);
            g = (byte)(((c >> 5) & 31) * 255 / 31);
            b = (byte)((c & 31) * 255 / 31);
            return true;
        }
    }
}
