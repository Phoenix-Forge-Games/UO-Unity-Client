using UnityEngine;

namespace UOClient
{
    /// <summary>
    /// Mapping between UO tile coordinates and Unity world space. One tile is 1.25 m and one Z unit
    /// 12.5 cm, so a UO wall tile is exactly one Fantasy Kingdom half-wall (1.25 m) and a 20-Z storey
    /// is exactly one 2.5 m wall; a 16-Z human comes out at 2 m. UO +Y (south) is Unity -Z, so UO
    /// north faces Unity forward.
    /// </summary>
    public static class WorldSpace
    {
        public const float TileSize = 1.25f;
        public const float ZScale = 0.125f;

        /// <summary>Center of a tile, standing at height z.</summary>
        public static Vector3 TileCenter(int x, int y, float z) =>
            new Vector3((x + 0.5f) * TileSize, z * ZScale, -(y + 0.5f) * TileSize);

        /// <summary>Unity X of a (possibly fractional) UO X.</summary>
        public static float X(float uoX) => uoX * TileSize;

        /// <summary>Unity Z of a (possibly fractional) UO Y.</summary>
        public static float Z(float uoY) => -uoY * TileSize;

        /// <summary>A tile corner (land mesh vertex).</summary>
        public static Vector3 Corner(int x, int y, float z) =>
            new Vector3(x * TileSize, z * ZScale, -y * TileSize);
    }
}
