using System;
using System.IO;
using System.Security.Cryptography;

namespace UOClient.Content
{
    /// <summary>
    /// Read-only, seekable AES-256-CTR view over a slice of another stream. CTR lets any byte be
    /// decrypted on its own, which <c>AssetBundle.LoadFromStream</c> needs: it seeks around the
    /// bundle and reads blocks on demand, so nothing decrypted is ever written to disk.
    /// The counter block is the 8-byte nonce followed by the 64-bit big-endian block index.
    /// </summary>
    public sealed class CtrCipherStream : Stream
    {
        public const int NonceSize = 8;
        private const int BlockSize = 16;
        private const int ChunkSize = 64 * 1024;

        private readonly Stream _inner;
        private readonly long _origin;
        private readonly long _length;
        private readonly byte[] _nonce;
        private readonly ICryptoTransform _ecb;
        private readonly byte[] _counters = new byte[ChunkSize + BlockSize];
        private readonly byte[] _keystream = new byte[ChunkSize + BlockSize];
        private long _position;
        private long _innerPosition = -1;

        public CtrCipherStream(Stream inner, long origin, long length, byte[] key, byte[] nonce)
        {
            if (nonce == null || nonce.Length != NonceSize) throw new ArgumentException("nonce must be 8 bytes");
            _inner = inner;
            _origin = origin;
            _length = length;
            _nonce = (byte[])nonce.Clone();
            _ecb = CreateEcb(key);
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long remaining = _length - _position;
            if (remaining <= 0 || count <= 0) return 0;
            if (count > remaining) count = (int)remaining;

            int total = 0;
            while (total < count)
            {
                int want = Math.Min(ChunkSize, count - total);
                long at = _origin + _position;
                if (_innerPosition != at)
                {
                    _inner.Position = at;
                }

                int got = _inner.Read(buffer, offset + total, want);
                if (got <= 0) break;
                _innerPosition = at + got;
                Xor(_ecb, _nonce, _position, buffer, offset + total, got, _counters, _keystream);
                _position += got;
                total += got;
            }

            return total;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? _position + offset : _length + offset;
            if (target < 0) throw new IOException("seek before start");
            _position = target;
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _ecb.Dispose();
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public static ICryptoTransform CreateEcb(byte[] key)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.Key = key;
                return aes.CreateEncryptor();
            }
        }

        /// <summary>
        /// XORs <paramref name="count"/> bytes (at most one chunk) with the keystream starting at stream
        /// position <paramref name="position"/>. Encrypting and decrypting are the same operation.
        /// </summary>
        public static void Xor(ICryptoTransform ecb, byte[] nonce, long position, byte[] data, int offset, int count,
            byte[] counters, byte[] keystream)
        {
            long firstBlock = position / BlockSize;
            int skip = (int)(position - firstBlock * BlockSize);
            int blocks = (skip + count + BlockSize - 1) / BlockSize;
            for (int b = 0; b < blocks; b++)
            {
                int o = b * BlockSize;
                Buffer.BlockCopy(nonce, 0, counters, o, NonceSize);
                ulong index = (ulong)(firstBlock + b);
                for (int i = 0; i < 8; i++)
                {
                    counters[o + NonceSize + i] = (byte)(index >> (56 - 8 * i));
                }
            }

            ecb.TransformBlock(counters, 0, blocks * BlockSize, keystream, 0);
            for (int i = 0; i < count; i++)
            {
                data[offset + i] ^= keystream[skip + i];
            }
        }
    }
}
