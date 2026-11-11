using System;
using System.Text;

namespace UOClient.Net
{
    /// <summary>
    /// Sequential big-endian reader over one complete packet. Reads past the end return zero
    /// instead of throwing, matching how lenient UO clients treat short packets.
    /// </summary>
    public sealed class PacketReader
    {
        private readonly byte[] _data;

        public PacketReader(byte[] data)
        {
            _data = data;
            Position = 1; // skip the packet ID
            if (data.Length > 0 && PacketLengths.Get(data[0]) == PacketLengths.Variable)
            {
                Position = 3; // skip the length field too
            }
        }

        public byte Id => _data[0];
        public int Length => _data.Length;
        public int Position { get; set; }
        public int Remaining => Math.Max(0, _data.Length - Position);

        public byte ReadByte() => Position < _data.Length ? _data[Position++] : (byte)0;

        public sbyte ReadSByte() => (sbyte)ReadByte();

        public bool ReadBool() => ReadByte() != 0;

        public ushort ReadUInt16()
        {
            int high = ReadByte();
            return (ushort)((high << 8) | ReadByte());
        }

        public short ReadInt16() => (short)ReadUInt16();

        public uint ReadUInt32()
        {
            uint value = 0;
            for (int i = 0; i < 4; i++)
            {
                value = (value << 8) | ReadByte();
            }

            return value;
        }

        public int ReadInt32() => (int)ReadUInt32();

        /// <summary>Reads a fixed-size, NUL-padded ASCII field.</summary>
        public string ReadAscii(int fixedLength)
        {
            int start = Position;
            int available = Math.Min(fixedLength, Remaining);
            Position += fixedLength;

            int end = start;
            while (end < start + available && _data[end] != 0)
            {
                end++;
            }

            return Encoding.ASCII.GetString(_data, start, end - start);
        }

        /// <summary>Reads a NUL-terminated ASCII string.</summary>
        public string ReadAsciiNullTerminated()
        {
            int start = Position;
            while (Position < _data.Length && _data[Position] != 0)
            {
                Position++;
            }

            string value = Encoding.ASCII.GetString(_data, start, Position - start);
            if (Position < _data.Length)
            {
                Position++; // consume the terminator
            }

            return value;
        }

        /// <summary>Reads a NUL-terminated big-endian UTF-16 string.</summary>
        public string ReadUnicodeNullTerminated()
        {
            var builder = new StringBuilder();
            while (Remaining >= 2)
            {
                char c = (char)ReadUInt16();
                if (c == '\0')
                {
                    break;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        /// <summary>Reads a NUL-terminated little-endian UTF-16 string (cliloc arguments in 0xC1).</summary>
        public string ReadUnicodeLittleEndianNullTerminated()
        {
            var builder = new StringBuilder();
            while (Remaining >= 2)
            {
                int low = ReadByte();
                char c = (char)(low | (ReadByte() << 8));
                if (c == '\0')
                {
                    break;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        public void Skip(int count) => Position += count;
    }
}
