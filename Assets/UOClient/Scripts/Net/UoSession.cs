using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UOClient.Net
{
    /// <summary>
    /// Drives the UO login handshake and owns the live game connection afterwards.
    /// Call <see cref="Poll"/> once per frame from a single thread; every event fires from inside
    /// Poll, so handlers can touch Unity objects directly. Packets the login flow does not consume
    /// (everything once in the world) are forwarded through <see cref="PacketReceived"/>.
    /// </summary>
    /// <remarks>
    /// Flow: login server [seed 0xEF, 0x80] → 0xA8 server list → 0xA0 select → 0x8C relay;
    /// game server [4-byte key, 0x91] (Huffman from here on) → 0xA9 characters → 0x5D play → 0x1B.
    /// </remarks>
    public sealed class UoSession : IDisposable
    {
        private UoConnection _connection;
        private Task _pendingConnect;
        private Action _afterConnect;
        private string _host;
        private int _port;
        private string _account;
        private string _password;
        private uint _relayKey;

        public event Action<SessionState> StateChanged;
        public event Action<IReadOnlyList<ServerInfo>> ServerListReceived;
        public event Action<IReadOnlyList<CharacterSlot>> CharacterListReceived;
        public event Action<string> Failed;
        public event Action<LoginConfirm> EnteredWorld;
        public event Action<PacketReader> PacketReceived;

        public SessionState State { get; private set; } = SessionState.Idle;
        public string LastError { get; private set; }
        public IReadOnlyList<ServerInfo> Servers { get; private set; } = Array.Empty<ServerInfo>();
        public IReadOnlyList<CharacterSlot> Characters { get; private set; } = Array.Empty<CharacterSlot>();
        public IReadOnlyList<StartingCity> Cities { get; private set; } = Array.Empty<StartingCity>();

        /// <summary>Use the address typed at login instead of the one the relay packet names.</summary>
        public bool IgnoreRelayAddress { get; set; }

        /// <summary>Starts a login. The password lives only in memory for the relay step.</summary>
        public void Login(string host, int port, string account, string password)
        {
            CloseConnection();
            _host = host;
            _port = port;
            _account = account;
            _password = password;
            LastError = null;

            SetState(SessionState.Connecting);
            BeginConnect(host, port, OnLoginServerConnected);
        }

        public void SelectServer(ServerInfo server)
        {
            if (State != SessionState.ServerSelection)
            {
                return;
            }

            var packet = new PacketWriter(0xA0);
            packet.WriteUInt16(server.Index);
            _connection.Send(packet);
            SetState(SessionState.ConnectingToGameServer);
        }

        public void SelectCharacter(CharacterSlot character)
        {
            if (State != SessionState.CharacterSelection)
            {
                return;
            }

            var packet = new PacketWriter(0x5D);
            packet.WriteUInt32(0xEDEDEDED);
            packet.WriteAscii(character.Name, 30);
            packet.WriteZero(2);
            packet.WriteUInt32(0); // client feature flags
            packet.WriteZero(24);
            packet.WriteUInt32((uint)character.Slot);
            packet.WriteUInt32(_connection.LocalAddress);
            _connection.Send(packet);
            SetState(SessionState.EnteringWorld);
        }

        /// <summary>Creates a character in the first free slot; the server then logs it straight in.</summary>
        public void CreateCharacter(NewCharacter character)
        {
            if (State != SessionState.CharacterSelection)
            {
                return;
            }

            int slot = 0;
            while (Characters.Any(c => c.Slot == slot))
            {
                slot++;
            }

            const byte humanRace = 1;
            var packet = new PacketWriter(0xF8);
            packet.WriteUInt32(0xEDEDEDED);
            packet.WriteUInt32(0xFFFFFFFF);
            packet.WriteByte(0);
            packet.WriteAscii(character.Name, 30);
            packet.WriteZero(2);
            packet.WriteUInt32(0); // client feature flags
            packet.WriteUInt32(1);
            packet.WriteUInt32(0);
            packet.WriteByte(0); // profession: custom skills
            packet.WriteZero(15);
            packet.WriteByte((byte)(humanRace * 2 + (character.Female ? 1 : 0)));
            packet.WriteByte(character.Strength);
            packet.WriteByte(character.Dexterity);
            packet.WriteByte(character.Intelligence);
            for (int i = 0; i < 4; i++)
            {
                packet.WriteByte(character.SkillIds[i]);
                packet.WriteByte(character.SkillValues[i]);
            }

            packet.WriteUInt16(character.SkinHue);
            packet.WriteUInt16(character.HairStyle);
            packet.WriteUInt16(character.HairHue);
            packet.WriteUInt16(character.BeardStyle);
            packet.WriteUInt16(character.BeardHue);
            packet.WriteUInt16((ushort)character.CityIndex);
            packet.WriteZero(2);
            packet.WriteUInt16((ushort)slot);
            packet.WriteUInt32(_connection.LocalAddress);
            packet.WriteUInt16(character.ShirtHue);
            packet.WriteUInt16(character.PantsHue);
            _connection.Send(packet);
            SetState(SessionState.EnteringWorld);
        }

        /// <summary>Sends a packet on the current connection (no-op when disconnected).</summary>
        public void Send(PacketWriter packet) => _connection?.Send(packet);

        /// <summary>Processes connection results and queued packets. Call every frame.</summary>
        public void Poll()
        {
            if (_pendingConnect != null)
            {
                if (!_pendingConnect.IsCompleted)
                {
                    return;
                }

                Task finished = _pendingConnect;
                _pendingConnect = null;

                if (finished.IsFaulted)
                {
                    Exception error = finished.Exception?.GetBaseException();
                    Fail($"Could not connect to {_host}:{_port}. {error?.Message}");
                    return;
                }

                _afterConnect?.Invoke();
            }

            if (_connection == null)
            {
                return;
            }

            UoConnection current = _connection;
            while (current == _connection && current.TryDequeue(out byte[] data))
            {
                Handle(new PacketReader(data));
            }

            // The login server hangs up after the relay packet; that close is expected.
            if (current == _connection && current.IsDisconnected && State != SessionState.Failed &&
                State != SessionState.Idle)
            {
                string reason = current.DisconnectReason;
                Fail(string.IsNullOrEmpty(reason) ? "The server closed the connection." : $"Disconnected: {reason}");
            }
        }

        public void Disconnect()
        {
            CloseConnection();
            SetState(SessionState.Idle);
        }

        public void Dispose()
        {
            CloseConnection();
            _password = null;
        }

        private void BeginConnect(string host, int port, Action onConnected)
        {
            var connection = new UoConnection();
            _connection = connection;
            _afterConnect = onConnected;
            // Connecting blocks for up to the timeout, so keep it off the caller's thread.
            _pendingConnect = Task.Run(() => connection.Connect(host, port));
        }

        private void OnLoginServerConnected()
        {
            SetState(SessionState.VerifyingAccount);

            var seed = new PacketWriter(0xEF);
            seed.WriteUInt32(_connection.LocalAddress);
            seed.WriteUInt32(ClientIdentity.Major);
            seed.WriteUInt32(ClientIdentity.Minor);
            seed.WriteUInt32(ClientIdentity.Build);
            seed.WriteUInt32(ClientIdentity.Revision);
            _connection.Send(seed);

            var login = new PacketWriter(0x80);
            login.WriteAscii(_account, 30);
            login.WriteAscii(_password, 30);
            login.WriteByte(0xFF);
            _connection.Send(login);
        }

        private void OnGameServerConnected()
        {
            _connection.EnableCompression();
            _connection.SendRaw(new[]
            {
                (byte)(_relayKey >> 24), (byte)(_relayKey >> 16), (byte)(_relayKey >> 8), (byte)_relayKey,
            });

            var login = new PacketWriter(0x91);
            login.WriteUInt32(_relayKey);
            login.WriteAscii(_account, 30);
            login.WriteAscii(_password, 30);
            _connection.Send(login);
        }

        private void Handle(PacketReader packet)
        {
            switch (packet.Id)
            {
                case 0x82: // login rejected
                    Fail(LoginMessages.ForLoginReject(packet.ReadByte()));
                    return;
                case 0x53: // popup error during character login
                    Fail(LoginMessages.ForPopup(packet.ReadByte()));
                    return;
                case 0xA8:
                    HandleServerList(packet);
                    return;
                case 0x8C:
                    HandleRelay(packet);
                    return;
                case 0xA9:
                    HandleCharacterList(packet, true);
                    return;
                case 0x86: // character list refresh after delete
                    HandleCharacterList(packet, false);
                    return;
                case 0xBD: // client version request
                    var version = new PacketWriter(0xBD);
                    version.WriteAsciiNullTerminated(ClientIdentity.VersionString);
                    _connection.Send(version);
                    return;
                case 0x1B:
                    HandleLoginConfirm(packet);
                    return;
                default:
                    PacketReceived?.Invoke(packet);
                    return;
            }
        }

        private void HandleServerList(PacketReader packet)
        {
            packet.ReadByte(); // system info flags
            int count = packet.ReadUInt16();
            var servers = new List<ServerInfo>(count);
            for (int i = 0; i < count; i++)
            {
                var server = new ServerInfo { Index = packet.ReadUInt16(), Name = packet.ReadAscii(32) };
                server.PercentFull = packet.ReadByte();
                packet.ReadByte(); // timezone
                packet.ReadUInt32(); // address, unused: the relay packet gives the real one
                servers.Add(server);
            }

            Servers = servers;
            SetState(SessionState.ServerSelection);
            ServerListReceived?.Invoke(servers);
        }

        private void HandleRelay(PacketReader packet)
        {
            byte[] ip = { packet.ReadByte(), packet.ReadByte(), packet.ReadByte(), packet.ReadByte() };
            int port = packet.ReadUInt16();
            _relayKey = packet.ReadUInt32();

            string host = $"{ip[0]}.{ip[1]}.{ip[2]}.{ip[3]}";
            if (IgnoreRelayAddress || (ip[0] | ip[1] | ip[2] | ip[3]) == 0)
            {
                host = _host;
                port = _port;
            }

            CloseConnection();
            SetState(SessionState.ConnectingToGameServer);
            BeginConnect(host, port, OnGameServerConnected);
        }

        private void HandleCharacterList(PacketReader packet, bool includesCities)
        {
            int count = packet.ReadByte();
            var characters = new List<CharacterSlot>(count);
            for (int i = 0; i < count; i++)
            {
                string name = packet.ReadAscii(30);
                packet.Skip(30); // password field, always blank
                if (!string.IsNullOrWhiteSpace(name))
                {
                    characters.Add(new CharacterSlot { Slot = i, Name = name.Trim() });
                }
            }

            if (includesCities)
            {
                Cities = ReadCities(packet);
            }

            Characters = characters;
            SetState(SessionState.CharacterSelection);
            CharacterListReceived?.Invoke(characters);
        }

        private static List<StartingCity> ReadCities(PacketReader packet)
        {
            // 7.0.13+ layout; older clients use a shorter record.
            int count = packet.ReadByte();
            var cities = new List<StartingCity>(count);
            for (int i = 0; i < count; i++)
            {
                var city = new StartingCity
                {
                    Index = packet.ReadByte(),
                    City = packet.ReadAscii(32),
                    Building = packet.ReadAscii(32),
                    X = (ushort)packet.ReadUInt32(),
                    Y = (ushort)packet.ReadUInt32(),
                    Z = (sbyte)packet.ReadUInt32(),
                    Map = packet.ReadUInt32(),
                };
                packet.Skip(8); // cliloc description, padding
                cities.Add(city);
            }

            return cities;
        }

        private void HandleLoginConfirm(PacketReader packet)
        {
            var confirm = new LoginConfirm { Serial = packet.ReadUInt32() };
            packet.ReadUInt32(); // unused
            confirm.Body = packet.ReadUInt16();
            confirm.X = packet.ReadUInt16();
            confirm.Y = packet.ReadUInt16();
            confirm.Z = packet.ReadInt16();
            confirm.Direction = packet.ReadByte();
            packet.ReadByte();
            packet.ReadUInt32();
            packet.ReadUInt32();
            confirm.MapWidth = packet.ReadUInt16();
            confirm.MapHeight = packet.ReadUInt16();

            _password = null; // no longer needed once in the world
            SetState(SessionState.InWorld);
            EnteredWorld?.Invoke(confirm);
        }

        private void Fail(string message)
        {
            LastError = message;
            CloseConnection();
            SetState(SessionState.Failed);
            Failed?.Invoke(message);
        }

        private void CloseConnection()
        {
            _pendingConnect = null;
            _afterConnect = null;
            _connection?.Dispose();
            _connection = null;
        }

        private void SetState(SessionState state)
        {
            if (State == state)
            {
                return;
            }

            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
