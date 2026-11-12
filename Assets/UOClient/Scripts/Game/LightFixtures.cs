using System;
using System.Reflection;
using UnityEngine;

namespace UOGame
{
    /// <summary>
    /// Lights a lamp prop the way it would really shine (see <see cref="RoomLightKit"/>): a flame effect at each of its
    /// flames, in place of any the prop came with, and the point light in their middle. Also the optional volumetric glow
    /// around a lamp, from Kronnect's Volumetric Lights when the project has it (a paid pack kept out of the public repo,
    /// so it is reached by name and the game builds without it).
    /// </summary>
    public static class LightFixtures
    {
        /// <summary>Colour of the light by what burns: candles warmest, lanterns behind glass a touch whiter.</summary>
        public static Color ColourOf(RoomLightKit.FlameKind kind) => kind switch
        {
            RoomLightKit.FlameKind.Candle => new Color(1f, 0.62f, 0.3f),
            RoomLightKit.FlameKind.Torch => new Color(1f, 0.56f, 0.24f),
            _ => new Color(1f, 0.68f, 0.36f),
        };

        /// <summary>
        /// Puts the fixture's flames on a placed prop (off until it is lit: the returned root) and gives where its light
        /// belongs: the middle of the flames.
        /// </summary>
        public static GameObject Flames(Transform prop, RoomLightKit.Fixture fixture, RoomLightKit kit, out Vector3 lightAt)
        {
            // The prop's own particle flames (Synty presets) sit wherever the pack put them: ours replace them.
            foreach (ParticleSystem own in prop.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (own.transform == prop)
                {
                    own.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ParticleSystem.EmissionModule emission = own.emission;
                    emission.enabled = false;
                    if (own.TryGetComponent(out ParticleSystemRenderer r))
                    {
                        r.enabled = false;
                    }
                }
                else
                {
                    own.gameObject.SetActive(false);
                }
            }

            var root = new GameObject("Flames");
            root.transform.SetParent(prop, false);
            root.SetActive(false);
            GameObject effect = fixture.Kind == RoomLightKit.FlameKind.Torch ? kit.TorchFlame : kit.CandleFlame;
            float scale = fixture.FlameScale * (fixture.Kind == RoomLightKit.FlameKind.Lantern ? 1.4f : 1f);
            Vector3 sum = Vector3.zero;
            foreach (Vector3 at in fixture.Flames)
            {
                sum += prop.TransformPoint(at);
                if (effect == null)
                {
                    continue;
                }

                GameObject flame = UnityEngine.Object.Instantiate(effect, root.transform, false);
                flame.transform.localPosition = at;
                flame.transform.localScale = Vector3.one * scale;
                foreach (ParticleSystem ps in flame.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ParticleSystem.MainModule main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy; // sized with the prop and the flame scale
                }
            }

            lightAt = fixture.Flames.Length > 0 ? sum / fixture.Flames.Length : prop.position;
            return root;
        }

        // ------------------------------------------------------------------ volumetric glow (optional)

        private static Type _volumetric;
        private static bool _looked;

        /// <summary>How far a lamp's glow in the air reaches (metres), at most: a halo round the flame, not the whole lit area.</summary>
        public const float GlowRadius = 3f;

        /// <summary>
        /// A volumetric halo round the light (off: the caller switches the returned object on near the camera); null
        /// without the pack. The halo is its own small, all-but-dark light at the same spot: the volume follows its
        /// light's range, so on the lamp's own light it would fill the whole lit street with haze.
        /// </summary>
        public static GameObject Glow(Light light, float density = 0.06f, float brightness = 0.6f)
        {
            if (!_looked)
            {
                _looked = true;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    _volumetric = a.GetType("VolumetricLights.VolumetricLight");
                    if (_volumetric != null)
                    {
                        break;
                    }
                }
            }

            if (_volumetric == null || light == null)
            {
                return null;
            }

            // Made switched off, so it sets up once, with our settings, when it first shows.
            var go = new GameObject("Glow");
            go.SetActive(false);
            go.transform.SetParent(light.transform, false);
            var halo = go.AddComponent<Light>();
            halo.type = LightType.Point;
            halo.range = Mathf.Min(GlowRadius, light.range * 0.35f);
            halo.color = light.color;
            halo.intensity = 0.01f; // drives the halo only; the lamp's own light does the lighting
            halo.shadows = LightShadows.None;
            var glow = (Behaviour)go.AddComponent(_volumetric);
            Set(glow, "profileSync", false);
            Set(glow, "brightnessMode", "Absolute");
            Set(glow, "density", density);
            Set(glow, "brightness", brightness);
            Set(glow, "useNoise", true);
            Set(glow, "noiseStrength", 0.6f);
            Set(glow, "noiseScale", 3f);
            Set(glow, "mediumAlbedo", Color.white);
            Set(glow, "distanceStartDimming", 25f);
            Set(glow, "distanceDeactivation", 40f);
            return go;
        }

        /// <summary>
        /// Sizes a lamp's halo to the weather: a tight GlowRadius in clear air, swelling to the lamp's whole reach in
        /// thick fog (the volume follows its light's range).
        /// </summary>
        public static void SizeGlow(GameObject glow, Light lamp, float fog)
        {
            if (glow != null && lamp != null && glow.TryGetComponent(out Light halo))
            {
                float clear = Mathf.Min(GlowRadius, lamp.range * 0.35f);
                halo.range = Mathf.Lerp(clear, lamp.range, Mathf.Clamp01(fog));
                halo.color = lamp.color;
            }
        }

        private static void Set(object target, string name, object value)
        {
            FieldInfo f = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f == null)
            {
                return;
            }

            if (f.FieldType.IsEnum && value is string member && Enum.IsDefined(f.FieldType, member))
            {
                f.SetValue(target, Enum.Parse(f.FieldType, member));
            }
            else if (f.FieldType.IsInstanceOfType(value))
            {
                f.SetValue(target, value);
            }
        }
    }
}
