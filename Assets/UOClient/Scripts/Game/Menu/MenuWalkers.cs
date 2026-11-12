using System.Collections.Generic;
using UnityEngine;

namespace UOGame.Menu
{
    /// <summary>
    /// Townsfolk going about the menu's town (this machine only), keeping to the streets: the cobbled ground
    /// (the terrain's street layer) round this object, under open sky with nothing standing on it. Each picks a spot
    /// down the street, finds a way there along the cobbles, walks it, pauses, and goes on. They appear once the
    /// town is in.
    /// </summary>
    public sealed class MenuWalkers : MonoBehaviour
    {
        [Tooltip("How many people walk the streets.")]
        [SerializeField] private int _count = 6;
        [Tooltip("How far round this object (metres) the streets are mapped.")]
        [SerializeField] private float _radius = 40f;
        [Tooltip("Terrain layers that are street (any whose name contains one of these).")]
        [SerializeField] private string[] _streetLayers = { "Cobble" };

        private const float Cell = 1.25f; // a UO tile

        private sealed class Walker
        {
            public MenuActor Actor;
            public readonly Queue<Vector3> Route = new Queue<Vector3>();
            public float WaitUntil;
        }

        private readonly List<Walker> _walkers = new List<Walker>();
        private bool[,] _street;
        private float[,] _height;
        private int _size;
        private Vector3 _origin;
        private readonly List<Vector2Int> _streetCells = new List<Vector2Int>();
        private Transform _people;
        private bool _spawned;

        private void OnDestroy()
        {
            if (_people != null)
            {
                Destroy(_people.gameObject);
            }
        }

        private void Update()
        {
            if (!_spawned)
            {
                TrySpawn();
                return;
            }

            foreach (Walker w in _walkers)
            {
                if (w.Actor == null || !w.Actor.Arrived || Time.time < w.WaitUntil)
                {
                    continue;
                }

                if (w.Route.Count > 0 && !w.Actor.WasBlocked)
                {
                    w.Actor.WalkTo(w.Route.Dequeue(), w.Actor.Pace);
                    continue;
                }

                // There (or something in the way): a moment's pause, then somewhere else down the street.
                w.Route.Clear();
                if (w.WaitUntil <= 0f)
                {
                    w.WaitUntil = Time.time + Random.Range(1f, 4f);
                    continue;
                }

                w.WaitUntil = 0f;
                PlanRoute(w);
            }
        }

        // ------------------------------------------------------------------ the streets

        private void TrySpawn()
        {
            var view = FindFirstObjectByType<UOClient.Bake.BakedWorldView>();
            if (view == null || !view.IsLoadedAt(transform.position) || view.Progress < 0.95f)
            {
                return; // the town isn't in yet
            }

            _spawned = true;
            MapStreets();
            if (_streetCells.Count == 0)
            {
                Debug.LogWarning("[Menu] No streets round the menu's walkers: nobody walks.");
                return;
            }

            var people = new GameObject("Townsfolk");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(people, gameObject.scene);
            _people = people.transform;
            int seed = System.Environment.TickCount;
            for (int i = 0; i < _count; i++)
            {
                Vector2Int start = _streetCells[Random.Range(0, _streetCells.Count)];
                MenuActor actor = MenuActor.Make("Townsperson", MenuActor.Townsperson(seed++), CellPoint(start), _people);
                actor.Pace = Random.Range(1.1f, 1.6f);
                actor.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                _walkers.Add(new Walker { Actor = actor, WaitUntil = Time.time + Random.Range(0f, 3f) });
            }
        }

        /// <summary>Which tiles round here are street: cobbles, open sky, nothing standing there.</summary>
        private void MapStreets()
        {
            _size = Mathf.CeilToInt(_radius * 2f / Cell);
            _origin = transform.position - new Vector3(_radius, 0f, _radius);
            _street = new bool[_size, _size];
            _height = new float[_size, _size];
            _streetCells.Clear();
            for (int x = 0; x < _size; x++)
            {
                for (int z = 0; z < _size; z++)
                {
                    Vector3 p = _origin + new Vector3((x + 0.5f) * Cell, 0f, (z + 0.5f) * Cell);
                    if (!Physics.Raycast(new Vector3(p.x, p.y + 60f, p.z), Vector3.down, out RaycastHit hit, 120f, ~0, QueryTriggerInteraction.Ignore) ||
                        hit.collider is not TerrainCollider tc || !IsStreet(tc.GetComponent<Terrain>(), hit.point) ||
                        Physics.CheckSphere(hit.point + Vector3.up * 1.0f, 0.45f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        continue; // a roof, a wall, a stall, or not cobbles
                    }

                    _street[x, z] = true;
                    _height[x, z] = hit.point.y;
                    _streetCells.Add(new Vector2Int(x, z));
                }
            }
        }

        private bool IsStreet(Terrain terrain, Vector3 point)
        {
            if (terrain == null || terrain.terrainData == null)
            {
                return false;
            }

            TerrainData data = terrain.terrainData;
            Vector3 local = point - terrain.transform.position;
            int ax = Mathf.Clamp((int)(local.x / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int az = Mathf.Clamp((int)(local.z / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
            float[,,] weights = data.GetAlphamaps(ax, az, 1, 1);
            float street = 0f;
            for (int i = 0; i < data.alphamapLayers; i++)
            {
                string name = data.terrainLayers[i] != null ? data.terrainLayers[i].name : string.Empty;
                foreach (string s in _streetLayers)
                {
                    if (name.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        street += weights[0, 0, i];
                        break;
                    }
                }
            }

            return street >= 0.5f;
        }

        private Vector3 CellPoint(Vector2Int c) =>
            _origin + new Vector3((c.x + 0.5f) * Cell, _height[c.x, c.y], (c.y + 0.5f) * Cell);

        // ------------------------------------------------------------------ getting about

        /// <summary>Somewhere 10-35 m down the street, and the way there along the cobbles.</summary>
        private void PlanRoute(Walker w)
        {
            Vector2Int from = CellOf(w.Actor.transform.position);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector2Int to = _streetCells[Random.Range(0, _streetCells.Count)];
                float d = Vector2Int.Distance(from, to) * Cell;
                if (d < 10f || d > 35f)
                {
                    continue;
                }

                List<Vector2Int> path = FindPath(from, to);
                if (path == null)
                {
                    continue;
                }

                // Every few tiles is enough to follow the street (and reads as walking, not stepping).
                for (int i = 2; i < path.Count; i += 2)
                {
                    w.Route.Enqueue(Jitter(CellPoint(path[i])));
                }

                w.Route.Enqueue(CellPoint(path[path.Count - 1]));
                return;
            }
        }

        private Vector2Int CellOf(Vector3 p)
        {
            var c = new Vector2Int(Mathf.FloorToInt((p.x - _origin.x) / Cell), Mathf.FloorToInt((p.z - _origin.z) / Cell));
            if (InStreet(c))
            {
                return c;
            }

            // Off the cobbles (a turn cut a corner): the nearest street tile.
            Vector2Int best = c;
            float bestD = float.MaxValue;
            foreach (Vector2Int s in _streetCells)
            {
                float d = (s - c).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = s;
                }
            }

            return best;
        }

        private bool InStreet(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < _size && c.y < _size && _street[c.x, c.y];

        private static readonly Vector2Int[] Steps =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
        };

        /// <summary>A* over the street tiles (diagonals only where both sides are street, so no corner is cut).</summary>
        private List<Vector2Int> FindPath(Vector2Int from, Vector2Int to)
        {
            var open = new List<Vector2Int> { from };
            var came = new Dictionary<Vector2Int, Vector2Int>();
            var cost = new Dictionary<Vector2Int, float> { [from] = 0f };
            var closed = new HashSet<Vector2Int>();
            while (open.Count > 0 && closed.Count < 4000)
            {
                int bestIndex = 0;
                float bestF = float.MaxValue;
                for (int i = 0; i < open.Count; i++)
                {
                    float f = cost[open[i]] + Vector2Int.Distance(open[i], to);
                    if (f < bestF)
                    {
                        bestF = f;
                        bestIndex = i;
                    }
                }

                Vector2Int cur = open[bestIndex];
                open.RemoveAt(bestIndex);
                if (cur == to)
                {
                    var path = new List<Vector2Int> { cur };
                    while (came.TryGetValue(cur, out Vector2Int prev))
                    {
                        cur = prev;
                        path.Add(cur);
                    }

                    path.Reverse();
                    return path;
                }

                closed.Add(cur);
                foreach (Vector2Int step in Steps)
                {
                    Vector2Int next = cur + step;
                    if (!InStreet(next) || closed.Contains(next) ||
                        step.x != 0 && step.y != 0 && (!InStreet(new Vector2Int(cur.x + step.x, cur.y)) || !InStreet(new Vector2Int(cur.x, cur.y + step.y))) ||
                        Mathf.Abs(_height[next.x, next.y] - _height[cur.x, cur.y]) > 0.45f)
                    {
                        continue;
                    }

                    float c = cost[cur] + (step.x != 0 && step.y != 0 ? 1.414f : 1f);
                    if (!cost.TryGetValue(next, out float old) || c < old)
                    {
                        cost[next] = c;
                        came[next] = cur;
                        if (!open.Contains(next))
                        {
                            open.Add(next);
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>A little off the tile's middle, so they don't all walk one line.</summary>
        private static Vector3 Jitter(Vector3 p) => p + new Vector3(Random.Range(-0.35f, 0.35f), 0f, Random.Range(-0.35f, 0.35f));
    }
}
