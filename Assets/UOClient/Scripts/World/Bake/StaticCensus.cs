using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UOClient.Data;

namespace UOClient.Bake
{
    /// <summary>
    /// What every map static became during a bake (see <see cref="MapRenderer.Census"/>), counted once per static:
    /// a block's statics are counted the first time the block is built and ignored when it's built again. Writes a
    /// report of everything drawn as a placeholder box or not drawn at all, which the world's collision would
    /// otherwise silently lose (the new server collides with what's drawn, nothing else).
    /// </summary>
    public sealed class StaticCensus
    {
        private sealed class Row
        {
            public readonly SortedDictionary<string, int> Outcomes = new SortedDictionary<string, int>();
            public readonly List<(int X, int Y, int Z, string Outcome)> Samples = new List<(int, int, int, string)>();
        }

        private readonly Dictionary<ushort, Row> _rows = new Dictionary<ushort, Row>();
        private readonly HashSet<long> _blocksSeen = new HashSet<long>();
        private long _counting = long.MinValue;
        private readonly TileData _tiles;
        private readonly int _facet;
        public readonly List<string> HouseFailures = new List<string>();

        public StaticCensus(TileData tiles, int facet)
        {
            _tiles = tiles;
            _facet = facet;
        }

        public void Record(ushort graphic, int x, int y, int z, string outcome)
        {
            long block = ((long)(x >> 3) << 32) | (uint)(y >> 3);
            if (block != _counting)
            {
                if (!_blocksSeen.Add(block))
                {
                    return; // built before: already counted
                }

                _counting = block;
            }

            if (!_rows.TryGetValue(graphic, out Row row))
            {
                row = new Row();
                _rows[graphic] = row;
            }

            row.Outcomes.TryGetValue(outcome, out int n);
            row.Outcomes[outcome] = n + 1;
            if (IsProblem(outcome) && row.Samples.Count < 6)
            {
                row.Samples.Add((x, y, z, outcome));
            }
        }

        public static bool IsProblem(string outcome) => outcome.StartsWith("NOT DRAWN") || outcome.StartsWith("PLACEHOLDER");

        private const TileFlag Interesting = TileFlag.Impassable | TileFlag.Surface | TileFlag.Wall | TileFlag.Roof |
                                             TileFlag.Bridge | TileFlag.Foliage | TileFlag.Wet | TileFlag.Door |
                                             TileFlag.Window | TileFlag.Internal | TileFlag.Background | TileFlag.NoShoot;

        /// <summary>Writes census-{facet}.md (problems first, by count) and census-{facet}.csv (every graphic and outcome).</summary>
        public void Write(string folder, Func<int, int, int, string> placeName)
        {
            var problems = new List<(ushort Graphic, Row Row, int Count)>();
            var csv = new StringBuilder("graphic,name,flags,height,outcome,count\n");
            var totals = new SortedDictionary<string, int>();
            foreach (KeyValuePair<ushort, Row> pair in _rows)
            {
                StaticTileInfo info = _tiles.GetStatic(pair.Key);
                int bad = 0;
                foreach (KeyValuePair<string, int> o in pair.Value.Outcomes)
                {
                    csv.Append($"0x{pair.Key:X4},\"{info.Name}\",\"{info.Flags & Interesting}\",{info.Height},\"{o.Key}\",{o.Value}\n");
                    totals.TryGetValue(o.Key, out int t);
                    totals[o.Key] = t + o.Value;
                    if (IsProblem(o.Key))
                    {
                        bad += o.Value;
                    }
                }

                if (bad > 0)
                {
                    problems.Add((pair.Key, pair.Value, bad));
                }
            }

            problems.Sort((a, b) => b.Count.CompareTo(a.Count));
            var md = new StringBuilder($"# Static census, facet {_facet}\n\n");
            md.AppendLine("What every map static became in the bake. Placeholder boxes and statics that are not drawn are listed in the table below.\n");
            md.AppendLine("| Outcome | Statics |\n|---|---|");
            foreach (KeyValuePair<string, int> t in totals)
            {
                md.AppendLine($"| {t.Key} | {t.Value:N0} |");
            }

            md.AppendLine($"\n## Problem graphics ({problems.Count})\n");
            md.AppendLine("| Graphic | Name | Flags | H | Count | Outcomes | Examples |\n|---|---|---|---|---|---|---|");
            foreach ((ushort graphic, Row row, int count) in problems)
            {
                StaticTileInfo info = _tiles.GetStatic(graphic);
                var outcomes = new StringBuilder();
                foreach (KeyValuePair<string, int> o in row.Outcomes)
                {
                    outcomes.Append(outcomes.Length > 0 ? "; " : "").Append($"{o.Key} {o.Value}");
                }

                var samples = new StringBuilder();
                foreach ((int x, int y, int z, string _) in row.Samples)
                {
                    string place = placeName?.Invoke(_facet, x, y);
                    samples.Append(samples.Length > 0 ? "; " : "").Append($"{x},{y},{z}").Append(place != null ? $" ({place})" : "");
                }

                md.AppendLine($"| 0x{graphic:X4} | {info.Name} | {info.Flags & Interesting} | {info.Height} | {count:N0} | {outcomes} | {samples} |");
            }

            if (HouseFailures.Count > 0)
            {
                md.AppendLine($"\n## Houses that failed to generate ({HouseFailures.Count})\n");
                foreach (string h in HouseFailures)
                {
                    md.AppendLine("- " + h);
                }
            }

            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, $"census-{_facet}.md"), md.ToString());
            File.WriteAllText(Path.Combine(folder, $"census-{_facet}.csv"), csv.ToString());
        }

        /// <summary>The nearest named place (Resources/Places.json) within reach, for the examples.</summary>
        public static Func<int, int, int, string> PlaceNames()
        {
            TextAsset text = Resources.Load<TextAsset>("Places");
            if (text == null)
            {
                return null;
            }

            PlaceList list = JsonUtility.FromJson<PlaceList>(text.text);
            return (facet, x, y) =>
            {
                string best = null;
                int bestDistance = 200 * 200;
                foreach (Place p in list.Places)
                {
                    int dx = p.X - x, dy = p.Y - y;
                    if (p.Facet == facet && dx * dx + dy * dy < bestDistance)
                    {
                        bestDistance = dx * dx + dy * dy;
                        best = p.Name;
                    }
                }

                return best != null ? $"near {best}" : null;
            };
        }

        [Serializable] private sealed class Place { public string Name; public string Kind; public int Facet; public int X; public int Y; }
        [Serializable] private sealed class PlaceList { public Place[] Places; }
    }
}
