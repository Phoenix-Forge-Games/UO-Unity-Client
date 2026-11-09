namespace UOClient.World
{
    /// <summary>UO's eight facings. North is -Y in UO coordinates.</summary>
    public enum Direction : byte
    {
        North = 0,
        NorthEast = 1,
        East = 2,
        SouthEast = 3,
        South = 4,
        SouthWest = 5,
        West = 6,
        NorthWest = 7,
    }

    public static class DirectionUtil
    {
        public const byte RunningFlag = 0x80;
        public const byte Mask = 0x07;

        private static readonly int[] OffsetX = { 0, 1, 1, 1, 0, -1, -1, -1 };
        private static readonly int[] OffsetY = { -1, -1, 0, 1, 1, 1, 0, -1 };

        public static int Dx(Direction direction) => OffsetX[(int)direction & Mask];
        public static int Dy(Direction direction) => OffsetY[(int)direction & Mask];

        /// <summary>Maps a UO-space delta (each component -1, 0 or 1) to a facing.</summary>
        public static Direction FromDelta(int dx, int dy)
        {
            for (int i = 0; i < 8; i++)
            {
                if (OffsetX[i] == dx && OffsetY[i] == dy)
                {
                    return (Direction)i;
                }
            }

            return Direction.South;
        }

        /// <summary>Yaw in degrees for a Unity world where +Z is UO north.</summary>
        public static float Yaw(Direction direction) => ((int)direction & Mask) * 45f;
    }
}
