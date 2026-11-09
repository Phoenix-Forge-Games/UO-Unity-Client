using UnityEngine;
using UOClient.Data;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// Turns UO's stepped roof tiles into sloped roof pieces. Each roof tile slopes down toward its
    /// lowest roof neighbour; a tile with lower neighbours on both sides of an axis is a ridge. UO
    /// roofs climb only 3 Z per tile (about 17 degrees), which reads as flat in 3D, so heights are
    /// steepened by <see cref="PitchScale"/> measured from each roof's own eave, keeping eaves on
    /// top of the walls.
    /// </summary>
    public sealed class RoofPlanner
    {
        public const float PitchScale = 2.2f;
        private const int NeighbourZRange = 12;
        private const int DefaultStep = 3;
        private const int MaxEaveSearch = 24;
        private const int GableZRange = 6;

        private static readonly Direction[] Cardinals =
            { Direction.North, Direction.East, Direction.South, Direction.West };

        private readonly MapFacet _map;
        private readonly TileData _tiles;

        public RoofPlanner(MapFacet map, TileData tiles)
        {
            _map = map;
            _tiles = tiles;
        }

        public enum Shape
        {
            Slope,
            Ridge,
            Flat,
        }

        public struct Piece
        {
            public Shape Shape;
            /// <summary>Downhill direction for a slope; for a ridge, the ridge runs perpendicular to it.</summary>
            public Direction Downhill;
            /// <summary>Unity height of the roof surface at the tile's centre (its low edge sits on the tile's Z).</summary>
            public float CentreHeight;
            /// <summary>Height lost across the tile in the downhill direction, in metres.</summary>
            public float Drop;
        }

        public static bool IsRoof(StaticTileInfo info) => info.Is(TileFlag.Roof);

        public Piece Plan(int x, int y, int z)
        {
            Piece piece = PlanShape(x, y, z);
            // A slope spans its tile from the tile's own (steepened) Z at the low edge up by Drop, so
            // the eave row's low edge rests exactly on the wall tops instead of dipping below them.
            piece.CentreHeight += piece.Drop * 0.5f;
            return piece;
        }

        public enum WallFit
        {
            /// <summary>Not part of a roof: draw at its real height.</summary>
            Normal,
            /// <summary>A gable wall: draw at the returned steepened heights.</summary>
            Steepened,
            /// <summary>A cap along the eave or ridge, already covered by the roof: don't draw.</summary>
            Hidden,
        }

        /// <summary>
        /// Fits a wall to an adjoining steepened roof. Walls at or above the roof's eave that run the
        /// way the roof slopes are gables and rise with it; ones running across the slope sit under
        /// the roof's edge and are hidden. Walls below the eave are left alone.
        /// </summary>
        public WallFit FitWall(int x, int y, int z, int height, WallEdges edges, out float bottom, out float top)
        {
            bottom = top = 0f;
            int? roofZ = null;
            int roofX = x, roofY = y;
            for (int i = -1; i < Cardinals.Length; i++)
            {
                int cx = i < 0 ? x : x + DirectionUtil.Dx(Cardinals[i]);
                int cy = i < 0 ? y : y + DirectionUtil.Dy(Cardinals[i]);
                int? candidate = RoofZ(cx, cy, z);
                if (candidate.HasValue && Mathf.Abs(candidate.Value - z) <= GableZRange &&
                    (!roofZ.HasValue || Mathf.Abs(candidate.Value - z) < Mathf.Abs(roofZ.Value - z)))
                {
                    roofZ = candidate;
                    roofX = cx;
                    roofY = cy;
                }
            }

            if (!roofZ.HasValue)
            {
                return WallFit.Normal;
            }

            int eave = FindEave(roofX, roofY, roofZ.Value);
            if (z < eave)
            {
                return WallFit.Normal; // the storey below the roof keeps its real height
            }

            // Which way does the roof fall here? North/south slopes rise along Y, east/west along X.
            Piece roof = PlanShape(roofX, roofY, roofZ.Value);
            bool slopeAlongY = roof.Downhill == Direction.North || roof.Downhill == Direction.South;
            bool runsAlongX = (edges & WallEdges.South) != 0 && (edges & WallEdges.East) == 0;
            bool runsAlongY = (edges & WallEdges.East) != 0 && (edges & WallEdges.South) == 0;
            bool acrossSlope = roof.Shape != Shape.Flat && ((slopeAlongY && runsAlongX) || (!slopeAlongY && runsAlongY));
            if (acrossSlope)
            {
                return WallFit.Hidden;
            }

            bottom = Steepen(z, eave) * WorldSpace.ZScale;
            top = Steepen(z + Mathf.Max(height, 1), eave) * WorldSpace.ZScale;
            return WallFit.Steepened;
        }

        private Piece PlanShape(int x, int y, int z)
        {
            int eave = FindEave(x, y, z);
            float zEff = Steepen(z, eave);
            var piece = new Piece { Shape = Shape.Flat, CentreHeight = zEff * WorldSpace.ZScale };

            // Lowest neighbour decides the slope; lower neighbours on opposite sides make a ridge.
            int bestDrop = 0;
            Direction best = Direction.North;
            bool lowerNorth = false, lowerSouth = false, lowerEast = false, lowerWest = false;
            foreach (Direction direction in Cardinals)
            {
                int? nz = RoofZ(x + DirectionUtil.Dx(direction), y + DirectionUtil.Dy(direction), z);
                int drop = nz.HasValue ? z - nz.Value : 0;
                if (drop > 0)
                {
                    switch (direction)
                    {
                        case Direction.North: lowerNorth = true; break;
                        case Direction.South: lowerSouth = true; break;
                        case Direction.East: lowerEast = true; break;
                        default: lowerWest = true; break;
                    }

                    if (drop > bestDrop)
                    {
                        bestDrop = drop;
                        best = direction;
                    }
                }
            }

            if ((lowerNorth && lowerSouth) || (lowerEast && lowerWest))
            {
                piece.Shape = Shape.Ridge;
                piece.Downhill = lowerNorth && lowerSouth ? Direction.North : Direction.East;
                piece.Drop = Mathf.Max(bestDrop, DefaultStep) * PitchScale * WorldSpace.ZScale;
                return piece;
            }

            if (bestDrop > 0)
            {
                piece.Shape = Shape.Slope;
                piece.Downhill = best;
                piece.Drop = bestDrop * PitchScale * WorldSpace.ZScale;
                return piece;
            }

            // An eave tile: nothing lower around it, but something higher behind it.
            foreach (Direction direction in Cardinals)
            {
                int? nz = RoofZ(x + DirectionUtil.Dx(direction), y + DirectionUtil.Dy(direction), z);
                if (nz.HasValue && nz.Value > z)
                {
                    piece.Shape = Shape.Slope;
                    piece.Downhill = Opposite(direction);
                    piece.Drop = Mathf.Min(nz.Value - z, 10) * PitchScale * WorldSpace.ZScale;
                    return piece;
                }
            }

            return piece;
        }

        private float Steepen(int z, int eave) => eave + (z - eave) * PitchScale;

        /// <summary>Follows the roof downhill to its lowest tile.</summary>
        private int FindEave(int x, int y, int z)
        {
            for (int step = 0; step < MaxEaveSearch; step++)
            {
                int lowest = z, nx = x, ny = y;
                foreach (Direction direction in Cardinals)
                {
                    int cx = x + DirectionUtil.Dx(direction), cy = y + DirectionUtil.Dy(direction);
                    int? nz = RoofZ(cx, cy, z);
                    if (nz.HasValue && nz.Value < lowest)
                    {
                        lowest = nz.Value;
                        nx = cx;
                        ny = cy;
                    }
                }

                if (lowest == z)
                {
                    return z;
                }

                x = nx;
                y = ny;
                z = lowest;
            }

            return z;
        }

        /// <summary>Z of the roof tile at (x, y) closest to <paramref name="nearZ"/>, or null.</summary>
        private int? RoofZ(int x, int y, int nearZ)
        {
            MapBlock block = _map.GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return null;
            }

            int lx = x & 7, ly = y & 7;
            int? best = null;
            foreach (StaticTile tile in block.Statics)
            {
                if (tile.X != lx || tile.Y != ly || !IsRoof(_tiles.GetStatic(tile.Graphic)))
                {
                    continue;
                }

                int distance = Mathf.Abs(tile.Z - nearZ);
                if (distance <= NeighbourZRange && (!best.HasValue || distance < Mathf.Abs(best.Value - nearZ)))
                {
                    best = tile.Z;
                }
            }

            return best;
        }

        private static Direction Opposite(Direction direction) => (Direction)(((int)direction + 4) & 7);
    }
}
