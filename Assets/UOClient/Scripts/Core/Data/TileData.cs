using System;
using System.IO;
using System.Text;

namespace UOClient.Data
{
    [Flags]
    public enum TileFlag : ulong
    {
        None = 0,
        Background = 0x1,
        Weapon = 0x2,
        Transparent = 0x4,
        Translucent = 0x8,
        Wall = 0x10,
        Damaging = 0x20,
        Impassable = 0x40,
        Wet = 0x80,
        Surface = 0x200,
        Bridge = 0x400,
        Stackable = 0x800,
        Window = 0x1000,
        NoShoot = 0x2000,
        Internal = 0x10000,
        Foliage = 0x20000,
        PartialHue = 0x40000,
        Map = 0x100000,
        Container = 0x200000,
        Wearable = 0x400000,
        LightSource = 0x800000,
        Animation = 0x1000000,
        NoDiagonal = 0x4000000,
        Armor = 0x8000000,
        Roof = 0x10000000,
        Door = 0x20000000,
        StairBack = 0x40000000,
        StairRight = 0x80000000,
    }

    public struct LandTileInfo
    {
        public TileFlag Flags;
        public ushort TextureId;
        public string Name;

        public bool IsImpassable => (Flags & TileFlag.Impassable) != 0;
        public bool IsWet => (Flags & TileFlag.Wet) != 0;
    }

    public struct StaticTileInfo
    {
        public TileFlag Flags;
        public byte Height;
        public byte Layer;
        public ushort AnimationId;
        public string Name;

        public bool Is(TileFlag flag) => (Flags & flag) != 0;
    }

    /// <summary>tiledata.mul in the 7.0.9+ layout (64-bit flags). Read once, immutable after.</summary>
    public sealed class TileData
    {
        private const int LandCount = 0x4000;
        private const int GroupSize = 32;
        private const int LandGroupBytes = 4 + GroupSize * (8 + 2 + 20);
        private const int StaticGroupBytes = 4 + GroupSize * (8 + 1 + 1 + 4 + 2 + 2 + 2 + 1 + 20);

        public TileData(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using (var reader = new BinaryReader(new MemoryStream(bytes)))
            {
                Land = new LandTileInfo[LandCount];
                for (int i = 0; i < LandCount; i++)
                {
                    if (i % GroupSize == 0)
                    {
                        reader.ReadUInt32(); // group header
                    }

                    Land[i] = new LandTileInfo
                    {
                        Flags = (TileFlag)reader.ReadUInt64(),
                        TextureId = reader.ReadUInt16(),
                        Name = ReadName(reader),
                    };
                }

                int staticGroups = (bytes.Length - LandCount / GroupSize * LandGroupBytes) / StaticGroupBytes;
                Statics = new StaticTileInfo[staticGroups * GroupSize];
                for (int i = 0; i < Statics.Length; i++)
                {
                    if (i % GroupSize == 0)
                    {
                        reader.ReadUInt32();
                    }

                    var info = new StaticTileInfo { Flags = (TileFlag)reader.ReadUInt64() };
                    reader.ReadByte(); // weight
                    info.Layer = reader.ReadByte();
                    reader.ReadInt32(); // count
                    info.AnimationId = reader.ReadUInt16();
                    reader.ReadUInt16(); // hue
                    reader.ReadUInt16(); // light index
                    info.Height = reader.ReadByte();
                    info.Name = ReadName(reader);
                    Statics[i] = info;
                }
            }
        }

        public LandTileInfo[] Land { get; }
        public StaticTileInfo[] Statics { get; }

        public StaticTileInfo GetStatic(ushort graphic) =>
            graphic < Statics.Length ? Statics[graphic] : default;

        private static string ReadName(BinaryReader reader)
        {
            byte[] raw = reader.ReadBytes(20);
            int end = Array.IndexOf(raw, (byte)0);
            return string.Intern(Encoding.ASCII.GetString(raw, 0, end < 0 ? raw.Length : end));
        }
    }
}
