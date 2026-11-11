using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UOClient.Bake
{
    /// <summary>
    /// Runs world bakes in play mode, facet after facet, giving each frame a time budget so the editor (or the
    /// import screen) stays responsive. <see cref="Report"/> says how far it has got.
    /// </summary>
    public sealed class WorldBakeRunner : MonoBehaviour
    {
        public WorldContent Content;
        public WorldPalette Palette;
        public string DataFolder;
        public string OutputFolder;
        public List<int> Facets = new List<int>();
        /// <summary>Optional chunk area (same for every facet); empty for whole facets.</summary>
        public RectInt Area;
        public bool Overwrite;
        public float BudgetMilliseconds = 150f;
        /// <summary>Load each chunk back after writing it and compare it with what was generated.</summary>
        public bool Verify;
        /// <summary>Non-zero: bake chunks in a random order from this seed (determinism test).</summary>
        public int ShuffleSeed;

        /// <summary>Totals over finished facets: what the writer left out or wrote inline, and verify differences.</summary>
        public readonly SortedDictionary<string, int> Totals = new SortedDictionary<string, int>();
        public readonly List<string> Examples = new List<string>();

        /// <summary>Called after each chunk is written (the editor saves the palette when it grows).</summary>
        public static event Action<WorldBakeRunner> ChunkWritten;

        private WorldBaker _baker;
        private int _facetIndex;
        private readonly StringBuilder _log = new StringBuilder();
        private int _lastBaked;

        public bool Finished { get; private set; }
        public string Error { get; private set; }
        public WorldBaker Baker => _baker;

        private void Update()
        {
            if (Finished)
            {
                return;
            }

            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (clock.Elapsed.TotalMilliseconds < BudgetMilliseconds)
                {
                    if (_baker == null)
                    {
                        if (_facetIndex >= Facets.Count)
                        {
                            Finished = true;
                            Debug.Log("[Bake] Finished.\n" + _log);
                            return;
                        }

                        RectInt? area = Area.width > 0 && Area.height > 0 ? Area : (RectInt?)null;
                        _baker = new WorldBaker(Content, Palette, DataFolder, Facets[_facetIndex], OutputFolder, area, Overwrite)
                        {
                            Verifier = Verify ? new ChunkVerifier() : null,
                        };
                        if (ShuffleSeed != 0)
                        {
                            _baker.Shuffle(ShuffleSeed);
                        }
                        _lastBaked = 0;
                    }

                    _baker.Step();
                    if (_baker.Baked != _lastBaked)
                    {
                        _lastBaked = _baker.Baked;
                        ChunkWritten?.Invoke(this);
                    }

                    if (_baker.Done)
                    {
                        _log.AppendLine(Line(_baker));
                        Merge(_baker);
                        _baker.Dispose();
                        _baker = null;
                        _facetIndex++;
                    }
                }
            }
            catch (Exception e)
            {
                Error = $"{e.GetType().Name}: {e.Message} (facet {(_baker != null ? _baker.Facet : -1)}, chunk {_baker?.Current})\n{e.StackTrace}";
                Debug.LogError("[Bake] " + Error);
                Finished = true;
            }
        }

        private void Merge(WorldBaker b)
        {
            foreach (KeyValuePair<string, int> stat in b.Writer.Stats)
            {
                Add(stat.Key, stat.Value);
            }

            if (b.Verifier != null)
            {
                Add("verified objects", b.Verifier.Compared);
                foreach (KeyValuePair<string, int> d in b.Verifier.Differences)
                {
                    Add("DIFFERS: " + d.Key, d.Value);
                }

                Examples.AddRange(b.Verifier.Examples);
            }
        }

        private void Add(string key, int n)
        {
            Totals.TryGetValue(key, out int have);
            Totals[key] = have + n;
        }

        private static string Line(WorldBaker b) =>
            $"facet {b.Facet}: {b.Baked}/{b.Total} chunks, {b.Bytes / 1048576.0:0.0} MB, {b.Seconds:0}s";

        /// <summary>Progress, totals and what the writer left out.</summary>
        public string Report()
        {
            var sb = new StringBuilder(_log.ToString());
            if (_baker != null)
            {
                double rate = _baker.Baked / Math.Max(1.0, _baker.Seconds);
                double left = (_baker.Total - _baker.Baked) / Math.Max(0.001, rate);
                sb.AppendLine(Line(_baker) + $" (now {_baker.Current}, {rate:0.00}/s, about {left / 60:0} min left on this facet)");
                foreach (KeyValuePair<string, int> stat in _baker.Writer.Stats)
                {
                    sb.AppendLine($"  {stat.Key}: {stat.Value}");
                }

                if (_baker.Verifier != null)
                {
                    sb.AppendLine($"  verified objects: {_baker.Verifier.Compared}");
                    foreach (KeyValuePair<string, int> d in _baker.Verifier.Differences)
                    {
                        sb.AppendLine($"  DIFFERS: {d.Key}: {d.Value}");
                    }
                }
            }

            foreach (KeyValuePair<string, int> total in Totals)
            {
                sb.AppendLine($"  total {total.Key}: {total.Value}");
            }

            for (int i = 0; i < Examples.Count && i < 10; i++)
            {
                sb.AppendLine("  e.g. " + Examples[i]);
            }

            if (Error != null)
            {
                sb.AppendLine("ERROR " + Error);
            }

            sb.AppendLine(Finished ? "finished" : $"running (facet {(_facetIndex < Facets.Count ? Facets[_facetIndex] : -1)})");
            return sb.ToString();
        }

        private void OnDestroy()
        {
            _baker?.Dispose();
        }
    }
}
