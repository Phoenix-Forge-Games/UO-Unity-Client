using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UOClient.Data;

namespace UOClient.Bake
{
    /// <summary>
    /// Per-tile facts about a facet that the baked chunks don't keep, baked alongside them and shipped with the world:
    /// which tiles can be mined, chopped or fished, and where the anvils, forges, ovens, mills and water are. One bit
    /// per tile per layer, deflated (`layers.bin` in the facet's world folder). Server and clients load the same file.
    /// </summary>
    public sealed class TileLayers
    {
        public const string FileName = "layers.bin";
        private const uint Magic = 0x4C49544C; // "LTIL"
        private const int Version = 1;

        public int Width { get; private set; }
        public int Height { get; private set; }

        private readonly Dictionary<string, byte[]> _bits = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> Names => _bits.Keys;

        public bool Has(string layer) => _bits.ContainsKey(layer);

        /// <summary>Is tile (x, y) in the layer?</summary>
        public bool Get(string layer, int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || !_bits.TryGetValue(layer, out byte[] bits))
            {
                return false;
            }

            int i = y * Width + x;
            return (bits[i >> 3] & (1 << (i & 7))) != 0;
        }

        public static TileLayers Load(string folder)
        {
            string path = Path.Combine(folder, FileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var layers = new TileLayers();
            using (var file = File.OpenRead(path))
            using (var reader = new BinaryReader(file, Encoding.UTF8))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                {
                    return null;
                }

                layers.Width = reader.ReadInt32();
                layers.Height = reader.ReadInt32();
                int count = reader.ReadInt32();
                for (int n = 0; n < count; n++)
                {
                    string name = reader.ReadString();
                    int packed = reader.ReadInt32();
                    byte[] data = reader.ReadBytes(packed);
                    var bits = new byte[(layers.Width * layers.Height + 7) / 8];
                    using (var input = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress))
                    {
                        int read = 0;
                        while (read < bits.Length)
                        {
                            int got = input.Read(bits, read, bits.Length - read);
                            if (got <= 0)
                            {
                                break;
                            }

                            read += got;
                        }
                    }

                    layers._bits[name] = bits;
                }
            }

            return layers;
        }

        // ------------------------------------------------------------------ building (editor)

        /// <summary>
        /// What makes a tile part of a layer: its land tile id, or any static on it with one of these graphics.
        /// </summary>
        public sealed class Rule
        {
            public string Name;
            public readonly HashSet<int> Land = new HashSet<int>();
            public readonly HashSet<int> Static = new HashSet<int>();
        }

        /// <summary>
        /// Read the facet's land and statics (and the world's decorations, when given) and write each rule's layer.
        /// Decoration files are blocks of "Class 0xGRAPHIC [(properties)]" followed by "x y z" lines; a decoration
        /// counts as a static of its graphic. Returns tiles marked per layer.
        /// </summary>
        public static Dictionary<string, int> Build(string dataFolder, int facet, IList<Rule> rules, string outFolder,
            IEnumerable<string> decorationFolders = null)
        {
            var counts = new Dictionary<string, int>();
            using (var map = new MapFacet(dataFolder, facet))
            {
                int width = map.Width, height = map.Height;
                var bits = new Dictionary<string, byte[]>();
                foreach (Rule rule in rules)
                {
                    bits[rule.Name] = new byte[(width * height + 7) / 8];
                    counts[rule.Name] = 0;
                }

                for (int by = 0; by < map.BlocksHigh; by++)
                {
                    for (int bx = 0; bx < map.BlocksWide; bx++)
                    {
                        MapBlock block = map.GetBlock(bx, by);
                        if (block == null)
                        {
                            continue;
                        }

                        for (int ly = 0; ly < MapBlock.Size; ly++)
                        {
                            for (int lx = 0; lx < MapBlock.Size; lx++)
                            {
                                int land = block.GetLand(lx, ly).TileId;
                                foreach (Rule rule in rules)
                                {
                                    if (rule.Land.Contains(land))
                                    {
                                        Mark(bits[rule.Name], width, bx * 8 + lx, by * 8 + ly, counts, rule.Name);
                                    }
                                }
                            }
                        }

                        foreach (StaticTile s in block.Statics)
                        {
                            foreach (Rule rule in rules)
                            {
                                if (rule.Static.Contains(s.Graphic))
                                {
                                    Mark(bits[rule.Name], width, bx * 8 + s.X, by * 8 + s.Y, counts, rule.Name);
                                }
                            }
                        }
                    }

                    map.Trim(0, by, 0); // keep memory flat while sweeping the whole facet
                }

                foreach (string folder in decorationFolders ?? Array.Empty<string>())
                {
                    if (!Directory.Exists(folder))
                    {
                        continue;
                    }

                    foreach (string file in Directory.GetFiles(folder, "*.cfg"))
                    {
                        int graphic = -1;
                        foreach (string raw in File.ReadAllLines(file))
                        {
                            string line = raw.Trim();
                            if (line.Length == 0 || line.StartsWith("#"))
                            {
                                continue;
                            }

                            string[] parts = line.Split(new[] { ' ', '	' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && parts[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                            {
                                // "0x0FAF", sometimes run into its properties ("0x0FAF(Hue=1)"): the hex digits only.
                                string hex = new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.TakeWhile(parts[1].Substring(2), Uri.IsHexDigit)));
                                graphic = hex.Length > 0 ? Convert.ToInt32(hex, 16) : -1;
                                continue;
                            }

                            if (graphic > 0 && parts.Length >= 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y) &&
                                x >= 0 && y >= 0 && x < width && y < height)
                            {
                                foreach (Rule rule in rules)
                                {
                                    if (rule.Static.Contains(graphic))
                                    {
                                        Mark(bits[rule.Name], width, x, y, counts, rule.Name);
                                    }
                                }
                            }
                        }
                    }
                }

                Directory.CreateDirectory(outFolder);
                string path = Path.Combine(outFolder, FileName);
                string temp = path + ".tmp";
                using (var file = File.Create(temp))
                using (var writer = new BinaryWriter(file, Encoding.UTF8))
                {
                    writer.Write(Magic);
                    writer.Write(Version);
                    writer.Write(width);
                    writer.Write(height);
                    writer.Write(rules.Count);
                    foreach (Rule rule in rules)
                    {
                        var packed = new MemoryStream();
                        using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, true))
                        {
                            deflate.Write(bits[rule.Name], 0, bits[rule.Name].Length);
                        }

                        writer.Write(rule.Name);
                        writer.Write((int)packed.Length);
                        writer.Write(packed.ToArray());
                    }
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temp, path);
            }

            return counts;
        }

        private static void Mark(byte[] bits, int width, int x, int y, Dictionary<string, int> counts, string name)
        {
            int i = y * width + x;
            if ((bits[i >> 3] & (1 << (i & 7))) == 0)
            {
                bits[i >> 3] |= (byte)(1 << (i & 7));
                counts[name]++;
            }
        }
    }
}
