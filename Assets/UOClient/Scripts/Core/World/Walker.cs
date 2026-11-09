using System;
using System.Collections.Generic;
using UOClient.Net;

namespace UOClient.World
{
    /// <summary>
    /// Client side of UO movement. Steps are predicted locally and sent as 0x02 with a rolling
    /// sequence number; the server acknowledges each (0x22) or refuses one (0x21), which snaps the
    /// player back and resets the sequence. A step in a new direction only turns the player.
    /// Pacing (how often to call <see cref="TryStep"/>) is the caller's job.
    /// </summary>
    public sealed class Walker
    {
        /// <summary>Unacknowledged steps allowed in flight, matching the classic client.</summary>
        public const int MaxPendingSteps = 5;

        private readonly WorldState _world;
        private readonly Action<PacketWriter> _send;
        private readonly Queue<byte> _pending = new Queue<byte>();
        private readonly Queue<uint> _fastWalkKeys = new Queue<uint>();
        private byte _sequence;

        internal Walker(WorldState world, Action<PacketWriter> send)
        {
            _world = world;
            _send = send;
        }

        /// <summary>
        /// Resolves the Z the player would stand at on a tile, or null if the tile is blocked.
        /// Arguments are target X, target Y and the current Z.
        /// </summary>
        public Func<int, int, int, int?> StandingZ { get; set; }

        public int PendingSteps => _pending.Count;

        /// <summary>Raised with a tile the server refused a step onto (for diagnostics).</summary>
        public event Action<int, int> Refused;

        /// <summary>How long a refused tile is avoided before the walker tries it again.</summary>
        private const int RefusedMs = 20000;
        private readonly Dictionary<(int X, int Y), int> _refused = new Dictionary<(int X, int Y), int>();
        private (int X, int Y)? _lastTarget;

        private bool WasRefused(int x, int y) =>
            _refused.TryGetValue((x, y), out int at) && unchecked(Environment.TickCount - at) < RefusedMs;

        /// <summary>
        /// When set, walking into a closed door uses it (0x06) so the server opens it, like UO's "open doors
        /// automatically". The step itself is tried again once the door has swung.
        /// </summary>
        public DoorTracker Doors { get; set; }

        private const int DoorRetryMs = 1000;
        private uint _lastDoor;
        private int _lastDoorTime;

        /// <summary>Tries to step (or turn) in a direction. Returns false if nothing was sent.</summary>
        public bool TryStep(Direction direction, bool run)
        {
            Mobile player = _world.Player;
            if (player == null || _pending.Count >= MaxPendingSteps)
            {
                return false;
            }

            if (player.Facing != direction)
            {
                Send(direction, run);
                player.Facing = direction;
                _world.RaiseMobileUpdated(player);
                return true;
            }

            int dx = DirectionUtil.Dx(direction), dy = DirectionUtil.Dy(direction);
            int x = player.X + dx;
            int y = player.Y + dy;
            WorldItem door = Doors?.ClosedDoorAt(_world, x, y, player.Z);
            if (door != null)
            {
                if (door.Serial != _lastDoor || unchecked(Environment.TickCount - _lastDoorTime) > DoorRetryMs)
                {
                    Use(door.Serial);
                    _lastDoor = door.Serial;
                    _lastDoorTime = Environment.TickCount;
                }

                return true; // paced like a step; the walk goes on once the door is open
            }

            int? z = StandingZ != null ? StandingZ(x, y, player.Z) : player.Z;
            if (z == null || WasRefused(x, y))
            {
                return false;
            }

            // The server refuses a diagonal step unless both tiles beside it are passable too.
            if (dx != 0 && dy != 0 && StandingZ != null &&
                (StandingZ(player.X + dx, player.Y, player.Z) == null || StandingZ(player.X, player.Y + dy, player.Z) == null))
            {
                return false;
            }

            Send(direction, run);
            _lastTarget = (x, y);
            player.X = x;
            player.Y = y;
            player.Z = z.Value;
            player.Running = run;
            _world.RaiseMobileUpdated(player);
            return true;
        }

        public void PushFastWalkKey(uint key) => _fastWalkKeys.Enqueue(key);

        internal void Reset()
        {
            _pending.Clear();
            _sequence = 0;
        }

        internal void OnConfirmed(PacketReader packet)
        {
            byte sequence = packet.ReadByte();
            // Acks arrive in order; drop everything up to and including this one.
            while (_pending.Count > 0)
            {
                if (_pending.Dequeue() == sequence)
                {
                    break;
                }
            }
        }

        internal void OnDenied(PacketReader packet)
        {
            packet.ReadByte(); // sequence
            Mobile player = _world.Player;
            if (player == null)
            {
                return;
            }

            // The tile we predicted onto is where the server said no: remember it for a while.
            if (_lastTarget.HasValue)
            {
                _refused[_lastTarget.Value] = Environment.TickCount;
                Refused?.Invoke(_lastTarget.Value.X, _lastTarget.Value.Y);
            }

            player.X = packet.ReadUInt16();
            player.Y = packet.ReadUInt16();
            player.Facing = (Direction)(packet.ReadByte() & DirectionUtil.Mask);
            player.Z = packet.ReadSByte();
            Reset();
            _world.RaisePlayerTeleported();
            _world.RaiseMobileUpdated(player);
        }

        /// <summary>Uses (double-clicks) an object: 0x06 with its serial.</summary>
        public void Use(uint serial)
        {
            var packet = new PacketWriter(0x06);
            packet.WriteUInt32(serial);
            _send(packet);
        }

        private void Send(Direction direction, bool run)
        {
            var packet = new PacketWriter(0x02);
            packet.WriteByte((byte)((byte)direction | (run ? DirectionUtil.RunningFlag : 0)));
            packet.WriteByte(_sequence);
            packet.WriteUInt32(_fastWalkKeys.Count > 0 ? _fastWalkKeys.Dequeue() : 0);
            _send(packet);

            _pending.Enqueue(_sequence);
            // The sequence wraps from 255 back to 1; 0 only follows a reset.
            _sequence = _sequence == 255 ? (byte)1 : (byte)(_sequence + 1);
        }
    }
}
