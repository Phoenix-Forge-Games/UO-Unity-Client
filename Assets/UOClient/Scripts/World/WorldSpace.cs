using UnityEngine;

namespace UOClient
{
    /// <summary>
    /// Mapping between UO tile coordinates and Unity world space. One tile is 1.25 m and one Z unit 12.5 cm, so a
    /// 20-Z storey is exactly one 2.5 m Synty wall and a 16-Z human comes out at 2 m. Houses are built from whole
    /// 2.5 m Synty modules stretched a few percent to fit their footprint (HouseSpecBuilder), never squashed halves.
    /// (On 2026-11-09 a tile was briefly 2.5 m; the user preferred the map at this size.) UO +Y (south) is Unity -Z,
    /// so UO north faces Unity forward. Constants tuned at <see cref="ClassicTileSize"/> scale by <see cref="TileScale"/>.
    /// </summary>
    public static class WorldSpace
    {
        public const float TileSize = 1.25f;

        /// <summary>The tile size much of the world generation was tuned at.</summary>
        public const float ClassicTileSize = 1.25f;

        /// <summary>How much bigger a tile is than it was when the generation was tuned (2).</summary>
        public const float TileScale = TileSize / ClassicTileSize;
        public const float ZScale = 0.125f;

        /// <summary>Center of a tile, standing at height z.</summary>
        public static Vector3 TileCenter(int x, int y, float z) =>
            new Vector3((x + 0.5f) * TileSize, z * ZScale, -(y + 0.5f) * TileSize);

        /// <summary>Unity X of a (possibly fractional) UO X.</summary>
        public static float X(float uoX) => uoX * TileSize;

        /// <summary>Unity Z of a (possibly fractional) UO Y.</summary>
        public static float Z(float uoY) => -uoY * TileSize;

        /// <summary>The tile a world position stands on.</summary>
        public static Vector2Int ToTile(Vector3 position) =>
            new Vector2Int(Mathf.FloorToInt(position.x / TileSize), Mathf.FloorToInt(-position.z / TileSize));

        /// <summary>A tile corner (land mesh vertex).</summary>
        public static Vector3 Corner(int x, int y, float z) =>
            new Vector3(x * TileSize, z * ZScale, -y * TileSize);
    }
}
