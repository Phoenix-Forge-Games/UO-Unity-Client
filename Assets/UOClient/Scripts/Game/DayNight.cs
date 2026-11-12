using System;
using System.Collections.Generic;
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
        private float _sunIntensity = 1f, _exposure = 1.3f, _shadowStrength = 1f;
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
        /// <summary>0..1: how fully the sky is clouded over (rain or snow closes it from horizon to horizon).</summary>
        public static float Overcast { get; private set; }
        /// <summary>0..1: how foggy the weather is (lamps glow wider in it).</summary>
        public static float Fog { get; private set; }
        /// <summary>In a dungeon (always dark, no sky or weather).</summary>
        public static bool Underground { get; private set; }
        /// <summary>The town or place you're in (its named region), or null out in the wilds.</summary>
        public static string PlaceName { get; private set; }
        /// <summary>A lightning flash lit the sky (the thunder follows it).</summary>
        public static event Action Lightning;

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
                _shadowStrength = _sun.shadowStrength;
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
            // Where you stand; before you're in the world (the menu), where the camera is.
            Camera view = Camera.main;
            Vector2Int tile = me != null ? UOClient.WorldSpace.ToTile(me.transform.position)
                : view != null ? UOClient.WorldSpace.ToTile(view.transform.position) : new Vector2Int(1600, 1600);
            const int facet = 1; // Trammel: the world that's baked
            DateTime now = DateTime.UtcNow;
            Hour = WorldRules.HourAt(now, facet, tile.x);
            bool dungeon = HousingRules.InDungeon(facet, tile.x, tile.y);
            Underground = dungeon;
            if (Time.frameCount % 15 == 0)
            {
                PlaceName = HousingRules.PlaceName(facet, tile.x, tile.y);
            }
            bool nightSight = me != null && me.GetComponent<PlayerIdentity>() is PlayerIdentity id && id.NightSight.Value;
            WorldRules.Weather weather = dungeon ? new WorldRules.Weather { Name = "Underground", Light = 1f } : WorldRules.WeatherAt(now);
            Weather = weather;
            Overcast = dungeon ? 0f : Mathf.Clamp01(Mathf.Max(weather.Rain, weather.Snow) * 2.5f);
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
                    Lightning?.Invoke();
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
                // Under a closed sky the light is diffuse: the sun much weaker, its shadows faint.
                _sun.intensity = _sunIntensity * (sunUp ? sunI : moonI) * (dark > 0 ? 0.15f : 1f) * Mathf.Lerp(1f, 0.4f, Overcast) + _flash * 1.5f;
                _sun.shadowStrength = _shadowStrength * Mathf.Lerp(1f, 0.3f, Overcast);
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
            // Rain or snow means a fully overcast sky: cloud from horizon to horizon, no sun, moon or stars.
            _sky.SetFloat("_Overcast", Overcast);
            _sky.SetFloat("_Stars", Mathf.Lerp(1.6f, 0f, weather.Clouds * 0.5f) * (1f - Overcast));
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
            Fog = weather.Fog;
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
            UpdateCover(cam);
            KeepOut(_rain);
            KeepOut(_snow);
        }

        // ------------------------------------------------------------------ roofs keep the weather out

        // What's overhead round the camera, half a metre square at a time: the height of the first solid thing a drop
        // would land on (a roof, a floor, the ground). A drop goes as it reaches the highest cover of its square and the
        // squares beside it, so none slip in where a roof's edge crosses a square, and a frame early, so none show for a
        // frame under the roof. Plants and creatures don't count. Unmeasured squares come first, a few hundred a frame;
        // the rest are measured again in turn (keeping what they had meanwhile), so doors, ships and houses are noticed.
        private const float CoverCell = 0.5f;
        private const int CoverSpan = 160;          // squares across the map: 80 m, past the rain's 44 m and its drift
        private const int CoverPerFrame = 500;
        private readonly float[] _coverHeight = new float[CoverSpan * CoverSpan];
        private readonly int[] _coverKey = new int[CoverSpan * CoverSpan];   // which square each slot holds (0: none)
        private readonly Queue<Vector2Int> _coverWanted = new Queue<Vector2Int>();
        private readonly HashSet<Vector2Int> _coverQueued = new HashSet<Vector2Int>();
        private readonly RaycastHit[] _coverHits = new RaycastHit[16];
        private ParticleSystem.Particle[] _drops = new ParticleSystem.Particle[0];
        private int _coverSweep;
        private int _coverMask = -1;
        private float _coverTop;
        private Vector2Int _coverCentre;

        private static int Key(int x, int z) => (x & 0xFFFF) << 16 | z & 0xFFFF | 1 << 31; // never 0

        private static int Slot(int x, int z) => ((x % CoverSpan + CoverSpan) % CoverSpan) * CoverSpan + (z % CoverSpan + CoverSpan) % CoverSpan;

        private void UpdateCover(Camera cam)
        {
            if (cam == null || (_rain == null || _rain.particleCount == 0) && (_snow == null || _snow.particleCount == 0))
            {
                return;
            }

            if (_coverMask == -1)
            {
                int plants = LayerMask.NameToLayer(UOClient.MapRenderer.VegetationLayerName);
                _coverMask = plants >= 0 ? ~(1 << plants) : ~0;
            }

            Vector3 at = cam.transform.position;
            _coverTop = at.y + 40f;
            _coverCentre = new Vector2Int(Mathf.FloorToInt(at.x / CoverCell), Mathf.FloorToInt(at.z / CoverCell));
            int budget = CoverPerFrame;
            for (; budget > 0 && _coverWanted.Count > 0; budget--)
            {
                Vector2Int cell = _coverWanted.Dequeue();
                _coverQueued.Remove(cell);
                Measure(cell.x, cell.y);
            }

            // Then the known squares again, in turn, round the camera.
            const int Half = 48; // 24 m each way: where the rain falls
            for (; budget > 0; budget--)
            {
                _coverSweep = (_coverSweep + 1) % (Half * 2 * Half * 2);
                int x = _coverCentre.x - Half + _coverSweep / (Half * 2), z = _coverCentre.y - Half + _coverSweep % (Half * 2);
                if (_coverKey[Slot(x, z)] == Key(x, z))
                {
                    Measure(x, z);
                }
            }
        }

        /// <summary>The top of the highest solid thing over a square (not plants or creatures), or far below.</summary>
        private void Measure(int x, int z)
        {
            var from = new Vector3((x + 0.5f) * CoverCell, _coverTop, (z + 0.5f) * CoverCell);
            int n = Physics.RaycastNonAlloc(from, Vector3.down, _coverHits, 120f, _coverMask, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _coverHits[i];
                if (h.point.y > best && h.collider.GetComponentInParent<Combat.Combatant>() == null)
                {
                    best = h.point.y;
                }
            }

            int slot = Slot(x, z);
            _coverKey[slot] = Key(x, z);
            _coverHeight[slot] = best;
        }

        /// <summary>The cover over a square, or false (and it's asked for) when it hasn't been measured.</summary>
        private bool Cover(int x, int z, out float top)
        {
            int slot = Slot(x, z);
            if (_coverKey[slot] == Key(x, z))
            {
                top = _coverHeight[slot];
                return true;
            }

            var cell = new Vector2Int(x, z);
            if (_coverQueued.Add(cell))
            {
                _coverWanted.Enqueue(cell);
            }

            top = float.NegativeInfinity;
            return false;
        }

        /// <summary>Drops that have reached what's overhead (or will this frame) go.</summary>
        private void KeepOut(ParticleSystem ps)
        {
            if (ps == null || ps.particleCount == 0)
            {
                return;
            }

            if (_drops.Length < ps.main.maxParticles)
            {
                _drops = new ParticleSystem.Particle[ps.main.maxParticles];
            }

            int count = ps.GetParticles(_drops);
            float ahead = 30f * Time.deltaTime * 1.5f + 0.05f; // how far the fastest drop falls before it's next drawn
            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = _drops[i].position;
                int x = Mathf.FloorToInt(p.x / CoverCell), z = Mathf.FloorToInt(p.z / CoverCell);
                if (!Cover(x, z, out float top))
                {
                    continue;
                }

                // The highest cover here and beside it.
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if ((dx != 0 || dz != 0) && Cover(x + dx, z + dz, out float side) && side > top)
                        {
                            top = side;
                        }
                    }
                }

                if (p.y <= top + ahead)
                {
                    _drops[i].remainingLifetime = 0f;
                    changed = true;
                }
            }

            if (changed)
            {
                ps.SetParticles(_drops, count);
            }
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
