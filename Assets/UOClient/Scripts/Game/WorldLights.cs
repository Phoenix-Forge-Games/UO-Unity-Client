using System.Collections.Generic;
using UnityEngine;
using UOGame.Content;

namespace UOGame
{
    /// <summary>
    /// Client: lamps, torches, candles and braziers in the baked world light up at night (World.json Lights). As each
    /// chunk loads, its props whose names match get a warm point light near their top; only the nearest few to the camera
    /// are on at once (MaxLit), so a town full of wall torches stays affordable; all of them fade out by day.
    /// </summary>
    public sealed class WorldLights : MonoBehaviour
    {
        private sealed class Lamp
        {
            public Light Light;
            public WorldRules.PropLight Kind;
            public float Seed;
        }

        private readonly List<Lamp> _lamps = new List<Lamp>();
        private readonly List<Lamp> _byDistance = new List<Lamp>();
        private float _nextSort;
        private static readonly Color Warm = new Color(1f, 0.7f, 0.4f);

        private void OnEnable() => UOClient.Bake.BakedWorldView.ChunkLoading += OnChunk;
        private void OnDisable() => UOClient.Bake.BakedWorldView.ChunkLoading -= OnChunk;

        private void OnChunk(UOClient.Bake.LoadedChunk chunk)
        {
            if (chunk?.Root == null || WorldRules.PropLights.Count == 0)
            {
                return;
            }

            foreach (Transform t in chunk.Root.GetComponentsInChildren<Transform>(true))
            {
                // The placed instance (a holder of the same name sits above it).
                if (!t.name.EndsWith("(Clone)", System.StringComparison.Ordinal))
                {
                    continue;
                }

                WorldRules.PropLight kind = WorldRules.PropLights.Find(k => k.Match.Length > 0 && t.name.StartsWith(k.Match, System.StringComparison.Ordinal));
                if (kind == null || !Top(t, out Vector3 top))
                {
                    continue;
                }

                var go = new GameObject("Lamp light");
                go.transform.SetParent(t, false);
                go.transform.position = top - Vector3.up * kind.Top;
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.color = Warm;
                light.range = kind.Range;
                light.intensity = 0f;
                light.enabled = false;
                _lamps.Add(new Lamp { Light = light, Kind = kind, Seed = Random.value * 100f });
            }
        }

        /// <summary>The top centre of a prop (its renderers' bounds).</summary>
        private static bool Top(Transform t, out Vector3 top)
        {
            bool any = false;
            var b = new Bounds();
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer)
                {
                    continue;
                }

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

            top = any ? new Vector3(b.center.x, b.max.y, b.center.z) : t.position;
            return any;
        }

        private void Update()
        {
            float dark = 1f - DayNight.Daylight;
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            // Now and then: the nearest MaxLit lamps are the ones that shine (when it's dark enough to matter).
            if (Time.time >= _nextSort)
            {
                _nextSort = Time.time + 0.5f;
                _lamps.RemoveAll(l => l.Light == null); // their chunk unloaded
                _byDistance.Clear();
                _byDistance.AddRange(_lamps);
                Vector3 at = cam.transform.position;
                _byDistance.Sort((a, b) => (a.Light.transform.position - at).sqrMagnitude.CompareTo((b.Light.transform.position - at).sqrMagnitude));
                for (int i = 0; i < _byDistance.Count; i++)
                {
                    _byDistance[i].Light.enabled = dark > 0.05f && i < WorldRules.MaxLitProps;
                }
            }

            if (dark <= 0.05f)
            {
                return;
            }

            for (int i = 0; i < _byDistance.Count && i < WorldRules.MaxLitProps; i++)
            {
                Lamp l = _byDistance[i];
                if (l.Light == null)
                {
                    continue;
                }

                float flicker = 1f + (Mathf.PerlinNoise(Time.time * 6f, l.Seed) - 0.5f) * 0.4f * l.Kind.Flicker;
                l.Light.intensity = l.Kind.Intensity * dark * flicker;
            }
        }
    }
}
