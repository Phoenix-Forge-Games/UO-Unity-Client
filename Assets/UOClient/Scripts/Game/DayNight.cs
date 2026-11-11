using System;
using UnityEngine;
using UnityEngine.Rendering;
using UOGame.Content;

namespace UOGame
{
    /// <summary>
    /// Client: the sky and the weather (a small sky-and-weather system of our own). Time of day follows UO's clock where
    /// you stand (<see cref="WorldRules"/>); the weather follows World.json's schedule, which every client works out
    /// from the clock alone, so everyone sees the same skies with nothing sent.
    /// <list type="bullet">
    /// <item>The sky (Shaders/Sky.shader): a day gradient with dawn and dusk glow, the sun, at night stars, a faint
    /// galaxy and the moon in its phase (UO's eight-day cycle), and clouds drifting on the wind.</item>
    /// <item>Light: the sun rises in the east, crosses the southern sky and sets in the west (World.json Sun), amber
    /// while it's low; at night a dim blue moon rides opposite it. Ambient light follows the sky's colours,
    /// so nights are dark and dungeons darker; clouds dim the sun. Night Sight lights you as by day.</item>
    /// <item>Weather: cloud cover and darkness, fog, rain and snow around you, and lightning in storms.</item>
    /// </list>
    /// </summary>
    public sealed class DayNight : MonoBehaviour
    {
        private Light _sun;
        private Material _sky;
        private bool _ourSky;
        private float _sunIntensity = 1f, _exposure = 1.3f;
        private Color _sunColor = new Color(1f, 0.957f, 0.839f);
        private static readonly Color MoonColor = new Color(0.55f, 0.65f, 0.95f);

        // Ambient light (sky, horizon, ground) by day and by night.
        private static readonly Color DaySky = new Color(0.58f, 0.66f, 0.78f), DayEquator = new Color(0.47f, 0.49f, 0.50f), DayGround = new Color(0.26f, 0.24f, 0.21f);
        private static readonly Color NightSky = new Color(0.035f, 0.045f, 0.085f), NightEquator = new Color(0.022f, 0.027f, 0.045f), NightGround = new Color(0.010f, 0.011f, 0.016f);
        private static readonly Color Amber = new Color(1f, 0.58f, 0.24f), WarmSky = new Color(0.78f, 0.58f, 0.38f), WarmEquator = new Color(0.88f, 0.58f, 0.30f);
        private static readonly Color DayHorizon = new Color(0.70f, 0.80f, 0.90f), NightHorizon = new Color(0.025f, 0.035f, 0.07f);

        private ParticleSystem _rain, _snow;
        private Vector2 _cloudOffset;
        private float _flash, _nextFlash;
        private readonly System.Random _random = new System.Random();

        /// <summary>The hour, light level and weather now where you stand (for a clock, the stats page, admin tools).</summary>
        public static double Hour { get; private set; }
        public static float Level { get; private set; }
        /// <summary>0..1: daylight (0 night or underground, 1 day): carried lights glow by it.</summary>
        public static float Daylight { get; private set; } = 1f;
        /// <summary>0..1: how amber the light is (the sun near the horizon).</summary>
        public static float Golden { get; private set; }
        public static WorldRules.Weather Weather { get; private set; }

        private void Start()
        {
            _sun = RenderSettings.sun;
            if (_sun == null)
            {
                foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l.type == LightType.Directional)
                    {
                        _sun = l;
                        break;
                    }
                }
            }

            if (_sun != null)
            {
                _sunIntensity = _sun.intensity;
                _sunColor = _sun.color;
            }

            // Our sky when the build has it (Resources/Sky/Sky.mat), else the scene's own, dimmed at night.
            var ours = Resources.Load<Material>("Sky/Sky");
            Material source = ours != null ? ours : RenderSettings.skybox;
            if (source != null)
            {
                _sky = new Material(source); // a copy: the asset itself stays as it is
                _ourSky = _sky.shader != null && _sky.shader.name == "UOGame/Sky";
                RenderSettings.skybox = _sky;
                if (!_ourSky && _sky.HasProperty("_Exposure"))
                {
                    _exposure = _sky.GetFloat("_Exposure");
                }
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            _rain = MakeWeather("Sky/Rain", rain: true);
            _snow = MakeWeather("Sky/Snow", rain: false);
        }

        private void Update()
        {
            PlayerMotor me = PlayerMotor.Local;
            Vector2Int tile = me != null ? UOClient.WorldSpace.ToTile(me.transform.position) : new Vector2Int(1600, 1600);
            const int facet = 1; // Trammel: the world that's baked
            DateTime now = DateTime.UtcNow;
            Hour = WorldRules.HourAt(now, facet, tile.x);
            bool dungeon = HousingRules.InDungeon(facet, tile.x, tile.y);
            bool nightSight = me != null && me.GetComponent<PlayerIdentity>() is PlayerIdentity id && id.NightSight.Value;
            WorldRules.Weather weather = dungeon ? new WorldRules.Weather { Name = "Underground", Light = 1f } : WorldRules.WeatherAt(now);
            Weather = weather;
            float dark = dungeon && !nightSight ? 1f : 0f;

            // The sun on its path (World.json Sun): up in the east, across the south, down in the west, then under the
            // world; the moon rides opposite it. A light at rotation (e, b) shines down e degrees toward bearing b, so a
            // body at bearing B is lit from B + 180.
            (float sunElevation, float sunBearing) = WorldRules.SunAt(Hour);
            float moonElevation = -sunElevation * 0.9f;
            Quaternion sunRot = Quaternion.Euler(sunElevation, sunBearing + 180f, 0f);
            Quaternion moonRot = Quaternion.Euler(moonElevation, sunBearing, 0f);
            float day = nightSight ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-6f, 6f, sunElevation)); // the sun's share
            // Amber while the sun is low: the hour round sunrise and sunset.
            float golden = Mathf.InverseLerp(-6f, 1f, sunElevation) * (1f - Mathf.InverseLerp(3f, 20f, sunElevation));
            Golden = golden;
            Daylight = dark > 0 ? 0f : day;
            Level = dark > 0 ? WorldRules.DungeonLevel : Mathf.Lerp(WorldRules.NightLevel, WorldRules.DayLevel, day);
            float sunLight = weather.Light; // clouds let less sun through

            // Lightning: in storms, now and then a flash lights everything for a moment.
            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 6f);
            if (weather.Lightning > 0.01f && !dungeon && Time.time >= _nextFlash)
            {
                if (_nextFlash > 0f)
                {
                    _flash = 1f;
                }

                _nextFlash = Time.time + Mathf.Lerp(25f, 4f, weather.Lightning) * (0.4f + (float)_random.NextDouble() * 1.2f);
            }

            if (_sun != null)
            {
                // One light: the sun while it's up, the moon once it's down (they hand over in the dusk, when both are dim).
                bool sunUp = sunElevation > -2f;
                float sunI = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-2f, 12f, sunElevation)) * Mathf.Lerp(0.55f, 1f, sunLight) * (1f - golden * 0.2f);
                float moonI = 0.13f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-2f, 15f, moonElevation)) * Mathf.Lerp(0.35f, 1f, sunLight);
                if (nightSight)
                {
                    sunI = Mathf.Max(sunI, 0.75f);
                    moonI = Mathf.Max(moonI, 0.75f);
                }

                _sun.transform.rotation = sunUp || nightSight && sunElevation > -20f ? sunRot : moonRot;
                _sun.color = sunUp ? Color.Lerp(_sunColor, Amber, golden) : nightSight ? _sunColor : MoonColor;
                _sun.intensity = _sunIntensity * (sunUp ? sunI : moonI) * (dark > 0 ? 0.15f : 1f) + _flash * 1.5f;
            }

            // Ambient light from the sky's own colours: dark at night, darker in dungeons, greyer under heavy cloud, warm
            // at sunrise and sunset.
            float lit = Mathf.Lerp(0.5f, 1f, sunLight) * (dark > 0 ? 0.35f : 1f);
            Color grey = new Color(0.42f, 0.44f, 0.47f);
            Color daySky = Color.Lerp(DaySky, grey, weather.CloudDark * weather.Clouds);
            Color sky = Color.Lerp(Color.Lerp(NightSky, daySky, day), WarmSky, golden * 0.6f);
            Color equator = Color.Lerp(Color.Lerp(NightEquator, DayEquator, day), WarmEquator, golden * 0.7f);
            RenderSettings.ambientSkyColor = sky * lit + Color.white * _flash * 0.4f;
            RenderSettings.ambientEquatorColor = equator * lit + Color.white * _flash * 0.3f;
            RenderSettings.ambientGroundColor = Color.Lerp(NightGround, DayGround, day) * lit;

            UpdateSky(weather, day, dark, golden, sunRot, moonRot, now);
            UpdateReflections(weather, day, dark, sunLight);
            UpdateFog(weather, day, dark);
            UpdatePrecipitation(weather, dungeon);
        }

        private void UpdateSky(WorldRules.Weather weather, float day, float dark, float golden, Quaternion sunRot, Quaternion moonRot, DateTime now)
        {
            if (_sky == null)
            {
                return;
            }

            if (!_ourSky)
            {
                if (_sky.HasProperty("_Exposure"))
                {
                    _sky.SetFloat("_Exposure", _exposure * Mathf.Lerp(0.08f, 1f, dark > 0 ? 0f : day));
                }

                return;
            }

            // The wind carries the clouds (east-north-east, faster in storms).
            _cloudOffset += new Vector2(0.92f, 0.38f) * (0.004f + weather.Wind * 0.03f) * Time.deltaTime;
            _sky.SetFloat("_Day", day);
            _sky.SetFloat("_Dark", dark);
            _sky.SetFloat("_Golden", golden);
            _sky.SetVector("_SunDir", sunRot * Vector3.back); // toward the sun: the light shines along its forward
            _sky.SetVector("_MoonDir", moonRot * Vector3.back);
            _sky.SetFloat("_MoonPhase", WorldRules.MoonPhase(now));
            _sky.SetFloat("_Clouds", weather.Clouds);
            _sky.SetFloat("_CloudDark", weather.CloudDark);
            _sky.SetVector("_CloudOffset", _cloudOffset);
            _sky.SetFloat("_Flash", _flash);
            _sky.SetFloat("_Stars", Mathf.Lerp(1.6f, 0f, weather.Clouds * 0.5f));
        }

        private float _reflectDay = -1f, _reflectClouds = -1f, _nextReflect;

        /// <summary>
        /// What shiny surfaces (roofs, leaves, glass) reflect: the sky as it is now, not the bright daytime sky the scene
        /// was made with (that lit roofs and leaves white at night, shadows or not). The reflection is re-made from our
        /// sky when it has changed enough, and it's weaker at night, in dungeons and under cloud.
        /// </summary>
        private void UpdateReflections(WorldRules.Weather weather, float day, float dark, float sunLight)
        {
            RenderSettings.reflectionIntensity = Mathf.Lerp(0.06f, 0.5f, day) * Mathf.Lerp(0.6f, 1f, sunLight) * (dark > 0 ? 0.2f : 1f);
            if (!_ourSky || Time.time < _nextReflect)
            {
                return;
            }

            if (Mathf.Abs(day - _reflectDay) > 0.03f || Mathf.Abs(weather.Clouds - _reflectClouds) > 0.05f)
            {
                _reflectDay = day;
                _reflectClouds = weather.Clouds;
                _nextReflect = Time.time + 3f;
                DynamicGI.UpdateEnvironment();
            }
        }

        private void UpdateFog(WorldRules.Weather weather, float day, float dark)
        {
            float fog = Mathf.Max(weather.Fog, dark * 0.5f);
            RenderSettings.fog = fog > 0.01f;
            if (!RenderSettings.fog)
            {
                return;
            }

            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = Mathf.Lerp(0.002f, 0.02f, fog);
            Color c = Color.Lerp(NightHorizon, Color.Lerp(DayHorizon, new Color(0.55f, 0.57f, 0.6f), weather.CloudDark), day);
            RenderSettings.fogColor = c * (dark > 0 ? 0.2f : 1f);
        }

        // ------------------------------------------------------------------ rain and snow

        /// <summary>Rain or snow falling round the camera (a particle system that follows it).</summary>
        private ParticleSystem MakeWeather(string material, bool rain)
        {
            var mat = Resources.Load<Material>(material);
            if (mat == null)
            {
                return null;
            }

            var go = new GameObject(rain ? "Rain" : "Snow");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = rain ? 1.1f : 7f;
            main.startSpeed = 0f;
            main.startSize = rain ? 0.03f : 0.09f;
            main.maxParticles = rain ? 6000 : 3000;
            main.startColor = rain ? new Color(0.75f, 0.8f, 0.9f, 0.45f) : new Color(1f, 1f, 1f, 0.9f);
            main.gravityModifier = 0f;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(44f, 1f, 44f);
            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(rain ? 0f : -0.4f, rain ? 0f : 0.4f);
            velocity.y = new ParticleSystem.MinMaxCurve(rain ? -26f : -1.6f, rain ? -22f : -1.1f);
            velocity.z = new ParticleSystem.MinMaxCurve(rain ? 0f : -0.4f, rain ? 0f : 0.4f);
            if (!rain)
            {
                ParticleSystem.NoiseModule noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.6f;
                noise.frequency = 0.4f;
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.renderMode = rain ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.velocityScale = rain ? 0.045f : 0f;
            renderer.lengthScale = rain ? 1f : 2f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        private void UpdatePrecipitation(WorldRules.Weather weather, bool dungeon)
        {
            Camera cam = Camera.main;
            Vector3 above = cam != null ? cam.transform.position + Vector3.up * 18f : transform.position;
            Place(_rain, dungeon ? 0f : weather.Rain * 5000f, above, weather.Wind);
            Place(_snow, dungeon ? 0f : weather.Snow * 900f, above, weather.Wind);
        }

        private static void Place(ParticleSystem ps, float rate, Vector3 at, float wind)
        {
            if (ps == null)
            {
                return;
            }

            ps.transform.position = at;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;
            ParticleSystem.ForceOverLifetimeModule force = ps.forceOverLifetime;
            force.enabled = wind > 0.05f;
            force.x = new ParticleSystem.MinMaxCurve(wind * 5f);
            force.z = new ParticleSystem.MinMaxCurve(wind * 2f);
        }
    }
}
