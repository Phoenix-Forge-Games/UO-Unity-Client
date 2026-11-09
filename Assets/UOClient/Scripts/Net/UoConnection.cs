using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace UOClient.Net
{
    /// <summary>
    /// One TCP connection to a UO login or game server. A background thread reads the socket,
    /// optionally Huffman-decodes it, and splits the stream into whole packets. Callers drain
    /// packets with <see cref="TryDequeue"/> from their own thread (Unity's main thread), so no
    /// game code ever runs on the socket thread. Sending is thread-safe.
    /// </summary>
    public sealed class UoConnection : IDisposable
    {
        private const int ReceiveBufferSize = 64 * 1024;
        private const int ConnectTimeoutMs = 5000;

        private readonly ConcurrentQueue<byte[]> _packets = new ConcurrentQueue<byte[]>();
        private readonly object _sendLock = new object();
        private readonly HuffmanDecoder _huffman = new HuffmanDecoder();
        private readonly List<byte> _stream = new List<byte>(ReceiveBufferSize);

        private TcpClient _client;
        private NetworkStream _network;
        private Thread _readThread;
        private volatile bool _compressed;
        private volatile bool _closing;

        /// <summary>Set once the socket closes; holds the reason (null for a normal close).</summary>
        public string DisconnectReason { get; private set; }
        public bool IsDisconnected { get; private set; }
        public bool IsConnected => _client != null && _client.Connected && !IsDisconnected;

        /// <summary>Our local IPv4 address as the server sees it in seeds and 0x5D, packed big-endian.</summary>
        public uint LocalAddress { get; private set; }

        /// <summary>Connects synchronously. Throws <see cref="SocketException"/> on failure.</summary>
        public void Connect(string host, int port)
        {
            _client = new TcpClient { NoDelay = true };
            var connect = _client.ConnectAsync(host, port);
            if (!connect.Wait(ConnectTimeoutMs))
            {
                _client.Close();
                throw new SocketException((int)SocketError.TimedOut);
            }

            if (connect.IsFaulted && connect.Exception != null)
            {
                throw connect.Exception.GetBaseException();
            }

            _network = _client.GetStream();

            if (_client.Client.LocalEndPoint is IPEndPoint local)
            {
                byte[] bytes = local.Address.MapToIPv4().GetAddressBytes();
                LocalAddress = (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
            }

            _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "UO socket reader" };
            _readThread.Start();
        }

        /// <summary>Game servers compress everything they send once the relay login starts.</summary>
        public void EnableCompression() => _compressed = true;

        public bool TryDequeue(out byte[] packet) => _packets.TryDequeue(out packet);

        public void Send(PacketWriter packet) => SendRaw(packet.ToArray());

        public void SendRaw(byte[] data)
        {
            if (_network == null || IsDisconnected)
            {
                return;
            }

            try
            {
                lock (_sendLock)
                {
                    _network.Write(data, 0, data.Length);
                }
            }
            catch (Exception ex)
            {
                MarkDisconnected(ex.Message);
            }
        }

        public void Dispose()
        {
            _closing = true;
            try
            {
                _client?.Close();
            }
            catch
            {
                // already closed
            }

            IsDisconnected = true;
        }

        private void ReadLoop()
        {
            byte[] buffer = new byte[ReceiveBufferSize];
            try
            {
                while (!_closing)
                {
                    int read = _network.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        MarkDisconnected(null);
                        return;
                    }

                    if (_compressed)
                    {
                        _huffman.Decode(buffer, read, _stream);
                    }
                    else
                    {
                        for (int i = 0; i < read; i++)
                        {
                            _stream.Add(buffer[i]);
                        }
                    }

                    ExtractPackets();
                }
            }
            catch (Exception ex)
            {
                MarkDisconnected(_closing ? null : ex.Message);
            }
        }

        private void ExtractPackets()
        {
            int offset = 0;
            while (offset < _stream.Count)
            {
                byte id = _stream[offset];
                int length = PacketLengths.Get(id);

                if (length == PacketLengths.Variable)
                {
                    if (_stream.Count - offset < 3)
                    {
                        break;
                    }

                    length = (_stream[offset + 1] << 8) | _stream[offset + 2];
                }

                if (length <= 0)
                {
                    MarkDisconnected($"Malformed packet 0x{id:X2} (length {length}).");
                    _closing = true;
                    return;
                }

                if (_stream.Count - offset < length)
                {
                    break;
                }

                byte[] packet = new byte[length];
                _stream.CopyTo(offset, packet, 0, length);
                _packets.Enqueue(packet);
                offset += length;
            }

            if (offset > 0)
            {
                _stream.RemoveRange(0, offset);
            }
        }

        private void MarkDisconnected(string reason)
        {
            if (IsDisconnected)
            {
                return;
            }

            DisconnectReason = reason;
            IsDisconnected = true;
        }
    }
}
