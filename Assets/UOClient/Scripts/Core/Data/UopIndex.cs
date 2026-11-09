using System;
using System.Collections.Generic;
using System.IO;

namespace UOClient.Data
{
    /// <summary>
    /// Index of a UOP container: maps logical file numbers (from a name pattern such as
    /// "build/map0legacymul/{0:D8}.dat") to byte ranges inside the .uop. Only uncompressed
    /// entries are supported, which covers the map files.
    /// </summary>
    public sealed class UopIndex
    {
        private const uint Magic = 0x50594D; // "MYP"

        private readonly Dictionary<ulong, (long Offset, int Length)> _byHash =
            new Dictionary<ulong, (long, int)>();

        public UopIndex(string path, string pattern, int maxFiles)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                if (reader.ReadUInt32() != Magic)
                {
                    throw new InvalidDataException($"{path} is not a UOP file.");
                }

                reader.ReadUInt32(); // version
                reader.ReadUInt32(); // timestamp
                long nextBlock = reader.ReadInt64();

                while (nextBlock != 0)
                {
                    reader.BaseStream.Seek(nextBlock, SeekOrigin.Begin);
                    int count = reader.ReadInt32();
                    nextBlock = reader.ReadInt64();

                    for (int i = 0; i < count; i++)
                    {
                        long offset = reader.ReadInt64();
                        int headerLength = reader.ReadInt32();
                        int compressedLength = reader.ReadInt32();
                        int decompressedLength = reader.ReadInt32();
                        ulong hash = reader.ReadUInt64();
                        reader.ReadUInt32(); // data hash
                        short flag = reader.ReadInt16();

                        if (offset == 0)
                        {
                            continue;
                        }

                        int length = flag == 0 ? decompressedLength : compressedLength;
                        _byHash[hash] = (offset + headerLength, length);
                    }
                }
            }

            Offsets = new long[maxFiles];
            Lengths = new int[maxFiles];
            for (int i = 0; i < maxFiles; i++)
            {
                bool found = _byHash.TryGetValue(CreateHash(string.Format(pattern, i)), out var entry);
                Offsets[i] = found ? entry.Offset : -1;
                Lengths[i] = found ? entry.Length : 0;
            }
        }

        /// <summary>Byte offset of each logical file, or -1 when missing.</summary>
        public long[] Offsets { get; }

        /// <summary>Byte length of each logical file, 0 when missing.</summary>
        public int[] Lengths { get; }

        /// <summary>The Jenkins lookup3-style hash UOP uses for entry names.</summary>
        public static ulong CreateHash(string s)
        {
            uint eax, ecx, edx, ebx, esi, edi;
            eax = ecx = edx = 0;
            ebx = edi = esi = (uint)s.Length + 0xDEADBEEF;
            int i = 0;

            for (i = 0; i + 12 < s.Length; i += 12)
            {
                edi = (uint)((s[i + 7] << 24) | (s[i + 6] << 16) | (s[i + 5] << 8) | s[i + 4]) + edi;
                esi = (uint)((s[i + 11] << 24) | (s[i + 10] << 16) | (s[i + 9] << 8) | s[i + 8]) + esi;
                edx = (uint)((s[i + 3] << 24) | (s[i + 2] << 16) | (s[i + 1] << 8) | s[i]) - esi;
                edx = (edx + ebx) ^ (esi >> 28) ^ (esi << 4);
                esi += edi;
                edi = (edi - edx) ^ (edx >> 26) ^ (edx << 6);
                edx += esi;
                esi = (esi - edi) ^ (edi >> 24) ^ (edi << 8);
                edi += edx;
                ebx = (edx - esi) ^ (esi >> 16) ^ (esi << 16);
                esi += edi;
                edi = (edi - ebx) ^ (ebx >> 13) ^ (ebx << 19);
                ebx += esi;
                esi = (esi - edi) ^ (edi >> 28) ^ (edi << 4);
                edi += ebx;
            }

            int remaining = s.Length - i;
            if (remaining <= 0)
            {
                return ((ulong)esi << 32) | eax;
            }

            // Deliberate fall-through: each case adds one more byte of the tail.
            if (remaining >= 12) esi += (uint)s[i + 11] << 24;
            if (remaining >= 11) esi += (uint)s[i + 10] << 16;
            if (remaining >= 10) esi += (uint)s[i + 9] << 8;
            if (remaining >= 9) esi += s[i + 8];
            if (remaining >= 8) edi += (uint)s[i + 7] << 24;
            if (remaining >= 7) edi += (uint)s[i + 6] << 16;
            if (remaining >= 6) edi += (uint)s[i + 5] << 8;
            if (remaining >= 5) edi += s[i + 4];
            if (remaining >= 4) ebx += (uint)s[i + 3] << 24;
            if (remaining >= 3) ebx += (uint)s[i + 2] << 16;
            if (remaining >= 2) ebx += (uint)s[i + 1] << 8;
            ebx += s[i];

            esi = (esi ^ edi) - ((edi >> 18) ^ (edi << 14));
            ecx = (esi ^ ebx) - ((esi >> 21) ^ (esi << 11));
            edi = (edi ^ ecx) - ((ecx >> 7) ^ (ecx << 25));
            esi = (esi ^ edi) - ((edi >> 16) ^ (edi << 16));
            edx = (esi ^ ecx) - ((esi >> 28) ^ (esi << 4));
            edi = (edi ^ edx) - ((edx >> 18) ^ (edx << 14));
            eax = (esi ^ edi) - ((edi >> 8) ^ (edi << 24));

            return ((ulong)edi << 32) | eax;
        }
    }
}
