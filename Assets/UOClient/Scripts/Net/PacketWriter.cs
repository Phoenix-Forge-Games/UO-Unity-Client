using System;
using System.IO;
using System.Text;

namespace UOClient.Net
{
    /// <summary>
    /// Builds one outgoing packet. Fixed-length packets are zero-padded to their table length;
    /// variable-length packets get their length field patched in <see cref="ToArray"/>.
    /// </summary>
    public sealed class PacketWriter
    {
        private readonly MemoryStream _stream = new MemoryStream(64);
        private readonly int _fixedLength;

        public PacketWriter(byte id)
        {
            _fixedLength = PacketLengths.Get(id);
            WriteByte(id);
            if (_fixedLength == PacketLengths.Variable)
            {
                WriteUInt16(0); // patched later
            }
        }

        public void WriteByte(byte value) => _stream.WriteByte(value);

        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        public void WriteUInt16(ushort value)
        {
            WriteByte((byte)(value >> 8));
            WriteByte((byte)value);
        }

        public void WriteUInt32(uint value)
        {
            WriteByte((byte)(value >> 24));
            WriteByte((byte)(value >> 16));
            WriteByte((byte)(value >> 8));
            WriteByte((byte)value);
        }

        /// <summary>Writes ASCII text truncated or NUL-padded to exactly <paramref name="fixedLength"/> bytes.</summary>
        public void WriteAscii(string value, int fixedLength)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
            int count = Math.Min(bytes.Length, fixedLength);
            _stream.Write(bytes, 0, count);
            WriteZero(fixedLength - count);
        }

        /// <summary>Writes NUL-terminated ASCII text.</summary>
        public void WriteAsciiNullTerminated(string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
            _stream.Write(bytes, 0, bytes.Length);
            WriteByte(0);
        }

        /// <summary>Writes NUL-terminated big-endian UTF-16 text.</summary>
        public void WriteUnicodeNullTerminated(string value)
        {
            foreach (char c in value ?? string.Empty)
            {
                WriteUInt16(c);
            }

            WriteUInt16(0);
        }

        public void WriteZero(int count)
        {
            for (int i = 0; i < count; i++)
            {
                WriteByte(0);
            }
        }

        public byte[] ToArray()
        {
            if (_fixedLength > 0 && _stream.Length < _fixedLength)
            {
                WriteZero(_fixedLength - (int)_stream.Length);
            }

            byte[] data = _stream.ToArray();
            if (_fixedLength == PacketLengths.Variable)
            {
                data[1] = (byte)(data.Length >> 8);
                data[2] = (byte)data.Length;
            }

            return data;
        }
    }
}
