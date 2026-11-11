using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace UOClient.Bake
{
    /// <summary>
    /// Draws the baked world around its targets: loads chunk files near any of them (read off the main thread, built
    /// one per frame, with their collision) and drops chunks far from all of them. A client follows its own player;
    /// the server follows every player, so there is ground under each. Replaces generating the world at runtime.
    /// </summary>
    public sealed class BakedWorldView : MonoBehaviour
    {
        public WorldContent Content;
        public WorldPalette Palette;
        /// <summary>The bake output folder (chunks are in a sub-folder per facet).</summary>
        public string Folder;
        public int Facet;
        /// <summary>Kept for a single target; added to <see cref="Targets"/>.</summary>
        public Transform Target;
        public readonly List<Transform> Targets = new List<Transform>();
        public int RadiusChunks = 2;

        private ChunkReader _reader;
        private FarLod _farLod;
        private readonly Dictionary<Vector2Int, LoadedChunk> _loaded = new Dictionary<Vector2Int, LoadedChunk>();
        private readonly Dictionary<Vector2Int, Task<byte[]>> _reading = new Dictionary<Vector2Int, Task<byte[]>>();
        private readonly HashSet<Vector2Int> _missing = new HashSet<Vector2Int>();
        private readonly List<Vector2Int> _drop = new List<Vector2Int>();

        public int LoadedCount => _loaded.Count;

        /// <summary>The chunks loaded now.</summary>
        public IEnumerable<LoadedChunk> Chunks => _loaded.Values;

        /// <summary>A chunk has been read and is about to get its collision (the game hides what it clears, e.g. trees on a plot).</summary>
        public static event System.Action<LoadedChunk> ChunkLoading;

        /// <summary>A chunk is in, with its collision (the server builds its navmesh from it).</summary>
        public static event System.Action<LoadedChunk> ChunkReady;

        /// <summary>A chunk is about to go (out of everyone's range).</summary>
        public static event System.Action<LoadedChunk> ChunkDropped;

        /// <summary>Is the chunk under this world position loaded (with its collision)?</summary>
        public bool IsLoadedAt(Vector3 position) => _loaded.ContainsKey(ChunkAt(position));

        private readonly List<Vector2Int> _centres = new List<Vector2Int>();

        private void Start()
        {
            // Paid art by address when the scene holds none (Docs/SecureArt.md).
            Content = global::UOClient.Content.SecureArt.Resolve(Content, global::UOClient.Content.SecureArtAddresses.WorldContent);
            Palette = global::UOClient.Content.SecureArt.Resolve(Palette, global::UOClient.Content.SecureArtAddresses.WorldPalette);
            _reader = new ChunkReader(Palette, Content) { Grass = Content.Grass != null ? new GrassField(Content.Grass) : null };
            if (!Application.isBatchMode)
            {
                _farLod = gameObject.AddComponent<FarLod>(); // buildings get a cheap far version, so a long view stays affordable
            }
        }

        /// <summary>The chunk under a world position.</summary>
        public static Vector2Int ChunkAt(Vector3 position) =>
            new Vector2Int(
                Mathf.FloorToInt(position.x / WorldSpace.TileSize / ChunkFormat.ChunkTiles),
                Mathf.FloorToInt(-position.z / WorldSpace.TileSize / ChunkFormat.ChunkTiles));

        private void Update()
        {
            if (_reader == null)
            {
                return;
            }

            _centres.Clear();
            if (Target != null)
            {
                _centres.Add(ChunkAt(Target.position));
            }

            Targets.RemoveAll(t => t == null);
            foreach (Transform t in Targets)
            {
                _centres.Add(ChunkAt(t.position));
            }

            if (_centres.Count == 0)
            {
                return;
            }

            _drop.Clear();
            foreach (Vector2Int key in _loaded.Keys)
            {
                if (!Near(key, RadiusChunks + 1))
                {
                    _drop.Add(key);
                }
            }

            foreach (Vector2Int key in _drop)
            {
                ChunkDropped?.Invoke(_loaded[key]);
                _loaded[key].Dispose();
                _loaded.Remove(key);
            }

            string folder = Path.Combine(Folder, Facet.ToString());
            Vector2Int? nearest = null;
            int nearestDistance = int.MaxValue;
            foreach (Vector2Int centre in _centres)
            {
                for (int dy = -RadiusChunks; dy <= RadiusChunks; dy++)
                {
                    for (int dx = -RadiusChunks; dx <= RadiusChunks; dx++)
                    {
                        var key = new Vector2Int(centre.x + dx, centre.y + dy);
                        if (_loaded.ContainsKey(key) || _missing.Contains(key))
                        {
                            continue;
                        }

                        if (!_reading.ContainsKey(key))
                        {
                            string path = Path.Combine(folder, ChunkFormat.FileName(key.x, key.y));
                            if (!File.Exists(path))
                            {
                                _missing.Add(key);
                                continue;
                            }

                            _reading[key] = Task.Run(() => File.ReadAllBytes(path));
                        }

                        int distance = dx * dx + dy * dy;
                        if (_reading[key].IsCompleted && distance < nearestDistance)
                        {
                            nearest = key;
                            nearestDistance = distance;
                        }
                    }
                }
            }

            if (nearest.HasValue)
            {
                Task<byte[]> read = _reading[nearest.Value];
                _reading.Remove(nearest.Value);
                if (read.Status == TaskStatus.RanToCompletion)
                {
                    LoadedChunk chunk = _reader.Load(read.Result, transform);
                    ChunkLoading?.Invoke(chunk);
                    ChunkCollision.Build(chunk);
                    _loaded[nearest.Value] = chunk;
                    ChunkReady?.Invoke(chunk);
                    _farLod?.Enqueue(chunk);
                }
                else
                {
                    Debug.LogWarning($"[Bake] Couldn't read chunk {nearest.Value}: {read.Exception?.GetBaseException().Message}");
                    _missing.Add(nearest.Value);
                }
            }
        }

        private bool Near(Vector2Int key, int radius)
        {
            foreach (Vector2Int centre in _centres)
            {
                if (Mathf.Abs(key.x - centre.x) <= radius && Mathf.Abs(key.y - centre.y) <= radius)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnDestroy()
        {
            foreach (LoadedChunk chunk in _loaded.Values)
            {
                chunk.Dispose();
            }

            _loaded.Clear();
        }
    }
}
