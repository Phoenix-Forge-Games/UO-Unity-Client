using System.Collections.Generic;
using UnityEngine;
using UOClient.Buildings;

namespace UOGame
{
    /// <summary>
    /// Client: one reflection probe per floor of each building, the size of the building on that floor (its rooms
    /// together, up to the ceiling, or the roof over a tall hall). The baked per-room probes stay switched off: they
    /// only say where the rooms are (<see cref="WorldLights"/> uses them). A probe is off until the camera comes within
    /// <see cref="Near"/> metres; then it switches on and renders once, and again each hour of the day (when the sky and
    /// light change), never every frame. Nearest first, a couple at a time (each spreads its faces over several
    /// frames); probes farther off wait until you come closer.
    /// </summary>
    public sealed class RoomProbes : MonoBehaviour
    {
        private sealed class Probe
        {
            public ReflectionProbe Box;
            public int RenderedHour = int.MinValue;
            public int Rendering = -1;
        }

        private const float Near = 60f;
        private const int AtOnce = 2;
        private const string Suffix = " Floor Reflection Probe";
        private readonly List<Probe> _probes = new List<Probe>();
        private readonly List<Probe> _busy = new List<Probe>();
        private float _nextPick;

        /// <summary>The live instance (client only).</summary>
        public static RoomProbes Instance { get; private set; }

        /// <summary>
        /// Probes near the camera still to render this hour (and those rendering now): a loading screen waits for 0.
        /// </summary>
        public int PendingNear
        {
            get
            {
                Camera cam = Camera.main;
                if (cam == null)
                {
                    return 0;
                }

                int hour = Hour, n = _busy.Count;
                Vector3 at = cam.transform.position;
                foreach (Probe p in _probes)
                {
                    if (p.Box != null && p.RenderedHour != hour && p.Box.gameObject.activeInHierarchy &&
                        new Bounds(p.Box.transform.position + p.Box.center, p.Box.size).SqrDistance(at) < Near * Near)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        private void Awake() => Instance = this;

        /// <summary>The hour the probes show (UO's clock where you stand).</summary>
        private static int Hour => Mathf.FloorToInt((float)DayNight.Hour);

        /// <summary>Is this a per-room probe from the bake ("S{storey}R{room}_..."), and on which storey?</summary>
        public static bool IsRoom(ReflectionProbe p, out int storey)
        {
            string n = p.name;
            int r = n.IndexOf('R');
            storey = -1;
            return n.Length > 1 && n[0] == 'S' && r > 1 && int.TryParse(n.Substring(1, r - 1), out storey);
        }

        private void OnEnable() => UOClient.Bake.BakedWorldView.ChunkReady += OnChunk;
        private void OnDisable() => UOClient.Bake.BakedWorldView.ChunkReady -= OnChunk;

        private void OnChunk(UOClient.Bake.LoadedChunk chunk)
        {
            if (chunk?.Root == null)
            {
                return;
            }

            foreach (Transform block in chunk.Root.transform)
            {
                foreach (Transform house in block)
                {
                    if (house.name.StartsWith("House_", System.StringComparison.Ordinal) && house.Find("Interior") is Transform interior)
                    {
                        foreach (ReflectionProbe p in FloorProbes(interior))
                        {
                            p.enabled = false; // until the camera comes near
                            _probes.Add(new Probe { Box = p });
                        }
                    }
                }
            }
        }

        /// <summary>A probe per floor, the building's size there; the per-room probes are switched off.</summary>
        private static List<ReflectionProbe> FloorProbes(Transform interior)
        {
            var floors = new SortedDictionary<int, Bounds>();
            foreach (ReflectionProbe p in interior.GetComponentsInChildren<ReflectionProbe>(true))
            {
                if (!IsRoom(p, out int storey))
                {
                    continue;
                }

                p.enabled = false;
                Vector3 size = p.transform.TransformVector(p.size);
                var room = new Bounds(p.transform.position + p.center, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
                if (floors.TryGetValue(storey, out Bounds all))
                {
                    all.Encapsulate(room);
                    floors[storey] = all;
                }
                else
                {
                    floors[storey] = room;
                }
            }

            // Floors baked without room probes: the floor's own extent, a storey high.
            foreach (Transform floor in interior)
            {
                if (!floor.name.StartsWith("Floor_", System.StringComparison.Ordinal) ||
                    !int.TryParse(floor.name.Substring(6), out int storey) || floors.ContainsKey(storey) || !Extent(floor, out Bounds b))
                {
                    continue;
                }

                const float Headroom = 2.35f;
                floors[storey] = new Bounds(new Vector3(b.center.x, b.max.y + Headroom * 0.5f, b.center.z), new Vector3(b.size.x, Headroom, b.size.z));
            }

            var probes = new List<ReflectionProbe>();
            Transform group = BuildingRoomProbes.Group(interior);
            foreach (KeyValuePair<int, Bounds> floor in floors)
            {
                ReflectionProbe probe = BuildingRoomProbes.Add(group, $"S{floor.Key}", group.InverseTransformPoint(floor.Value.center), floor.Value.size);
                probe.name = $"S{floor.Key}{Suffix}";
                probes.Add(probe);
            }

            return probes;
        }

        private static bool Extent(Transform t, out Bounds b)
        {
            bool any = false;
            b = new Bounds();
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (!any)
                {
                    b = r.bounds;
                    any = true;
                }
                else
                {
                    b.Encapsulate(r.bounds);
                }
            }

            return any;
        }

        private void Update()
        {
            // Finished renders free their slot.
            for (int i = _busy.Count - 1; i >= 0; i--)
            {
                Probe p = _busy[i];
                if (p.Box == null || p.Box.IsFinishedRendering(p.Rendering))
                {
                    p.Rendering = -1;
                    _busy.RemoveAt(i);
                }
            }

            Camera cam = Camera.main;
            if (cam == null || _busy.Count >= AtOnce || Time.time < _nextPick)
            {
                return;
            }

            _nextPick = Time.time + 0.1f;
            _probes.RemoveAll(p => p.Box == null); // their chunk unloaded
            int hour = Hour;
            Vector3 at = cam.transform.position;
            Probe best = null;
            float bestDistance = Near * Near;
            foreach (Probe p in _probes)
            {
                if (p.RenderedHour == hour || p.Rendering >= 0 || !p.Box.gameObject.activeInHierarchy)
                {
                    continue;
                }

                // Nearest point of the probe's box: a big building counts as near once you reach its walls' reach.
                Bounds box = new Bounds(p.Box.transform.position + p.Box.center, p.Box.size);
                float d = box.SqrDistance(at);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = p;
                }
            }

            if (best != null)
            {
                best.Box.enabled = true; // on from its first render
                best.RenderedHour = hour;
                best.Rendering = best.Box.RenderProbe();
                _busy.Add(best);
            }
        }
    }
}
