using System.Collections.Generic;
using UnityEngine;
using UOGame.Content;

namespace UOGame
{
    /// <summary>
    /// Client: lamps, torches, candles and braziers in the baked world light up at night (World.json Lights). As each
    /// chunk loads, its props whose names match get a warm point light: at the middle of their flames when the
    /// <see cref="RoomLightKit"/> knows the prop (with a flame effect on each wick or lamp), else near their top. Every
    /// room of every building that has none gets a light source of its own: a candle or lantern on its table, or a
    /// standing candelabra in a free corner. Lamps the camera can see (in view, and a clear line to the lamp or the pool of
    /// light under it: a flier over the roofs sees many more than someone in the street) are lit nearest first, up to the
    /// player's Lamp lights setting, fading in and out; the nearest few of those (Lamp shadows) cast soft shadows, the rest
    /// none. A lantern's glass glows whenever it burns, lit or not, so far-off streets still twinkle. Flames burn within
    /// FlameDistance; in fog the nearest lamps glow in the air. Outdoor lamps go out by day, room lights dim to
    /// IndoorDayShare.
    /// </summary>
    public sealed class WorldLights : MonoBehaviour
    {
        private sealed class Lamp
        {
            public Light Light;
            public WorldRules.PropLight Kind;
            public float Seed;
            public bool Indoor;
            public GameObject Flames; // its flame effects (null: a lamp whose flames aren't known)
            public GameObject Glow;   // its volumetric halo (null without the pack, and indoors)
            public Renderer[] Glass;  // a lantern's panes, glowing when it burns (none for open flames)
            public LampFixture Fixture; // a whole fixture prefab (null: a lamp prop lit by name)
            public float GlassOn = -1f;
            public bool Seen;         // a clear line from the camera to it (or its pool of light), last looked
            public bool Wanted;       // within the lit budget
            public float Fade;        // 0..1, eases toward Wanted
        }

        /// <summary>Lamps farther than this (metres) are never lit; their glass still glows.</summary>
        private const float SeeDistance = 150f;

        /// <summary>Lines of sight checked per frame (round the lamps in turn).</summary>
        private const int LooksPerFrame = 24;

        /// <summary>How fast a light fades in or out (per second).</summary>
        private const float FadeSpeed = 2.5f;

        /// <summary>Lamps nearer than this cast shadows when their turn comes (metres).</summary>
        private const float ShadowReach = 30f;

        /// <summary>How brightly a lantern's glass glows, relative to its light's colour.</summary>
        private const float GlassBright = 2.2f;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private MaterialPropertyBlock _block; // made on first use (not allowed while the component is constructed)
        private readonly Plane[] _planes = new Plane[6];
        private readonly List<Lamp> _shining = new List<Lamp>();
        private int _lookAt;

        /// <summary>How many lamps may light the world at once, and how many of them cast shadows (Options).</summary>
        private static int LitBudget => new[] { 24, 48, 96 }[Mathf.Clamp(UOClient.GameSettings.Current.LampLights, 0, 2)];
        private static int ShadowBudget => new[] { 0, 2, 4, 8 }[Mathf.Clamp(UOClient.GameSettings.Current.LampShadows, 0, 3)];

        /// <summary>Flames burn on lamps this near the camera (metres); further off nobody sees them flicker.</summary>
        private const float FlameDistance = 45f;

        /// <summary>The nearest few lit outdoor lamps glow in the air (volumetric, costly per lamp).</summary>
        private const int GlowCount = 6;
        private const float GlowDistance = 35f;
        private const float MinGlowFog = 0.05f;

        private readonly List<Lamp> _lamps = new List<Lamp>();
        private readonly List<Lamp> _byDistance = new List<Lamp>();
        private float _nextSort;
        private static readonly Color Warm = new Color(1f, 0.7f, 0.4f);
        private RoomLightKit _kit;
        private readonly Collider[] _overlap = new Collider[16];

        /// <summary>The one in the game world (null before it loads).</summary>
        public static WorldLights Instance { get; private set; }

        /// <summary>The fixtures kit (Resources/World/RoomLights).</summary>
        public RoomLightKit Kit => _kit;

        private static readonly List<LampFixture> Waiting = new List<LampFixture>();

        private void Awake()
        {
            _kit = Resources.Load<RoomLightKit>("World/RoomLights");
            Instance = this;
            foreach (LampFixture f in Waiting)
            {
                if (f != null)
                {
                    Add(f);
                }
            }

            Waiting.Clear();
        }

        /// <summary>A light fixture came into the world (see <see cref="LampFixture"/>).</summary>
        public static void Register(LampFixture fixture)
        {
            if (Instance != null)
            {
                Instance.Add(fixture);
            }
            else if (!Waiting.Contains(fixture))
            {
                Waiting.Add(fixture);
            }
        }

        /// <summary>A light fixture left (its chunk unloaded, or it was switched off).</summary>
        public static void Unregister(LampFixture fixture)
        {
            Waiting.Remove(fixture);
            if (Instance != null)
            {
                Instance._lamps.RemoveAll(l => l.Fixture == fixture);
                Instance._shining.RemoveAll(l => l.Fixture == fixture);
            }
        }

        private void Add(LampFixture f)
        {
            if (f.Light == null || _lamps.Exists(l => l.Fixture == f))
            {
                return;
            }

            f.Light.enabled = false;
            f.Light.shadows = LightShadows.None;
            if (f.Flames != null)
            {
                f.Flames.SetActive(false);
            }

            _lamps.Add(new Lamp
            {
                Light = f.Light,
                Kind = new WorldRules.PropLight { Match = f.name, Range = f.Light.range, Intensity = f.Intensity, Flicker = f.Flicker },
                Seed = Random.value * 100f,
                Indoor = f.Indoor,
                Flames = f.Flames,
                Glow = f.Indoor || !f.Glows ? null : LightFixtures.Glow(f.Light),
                Glass = f.Glass != null && f.Glass.Length > 0 ? f.Glass : null,
                Fixture = f,
            });
            _nextSort = 0f;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Lights a lamp prop that other code placed like the world's own: its World.json kind, its flames, on when seen at
        /// night. False if it's no known lamp.
        /// </summary>
        public bool Light(Transform prop)
        {
            string name = prop.name;
            WorldRules.PropLight kind = WorldRules.PropLights.Find(k => k.Match.Length > 0 && name.StartsWith(k.Match, System.StringComparison.Ordinal));
            RoomLightKit.Fixture fixture = _kit != null ? _kit.Find(name) : null;
            if (kind == null || fixture == null)
            {
                return false;
            }

            GameObject flames = LightFixtures.Flames(prop, fixture, _kit, out Vector3 at);
            Add(prop, at, kind, kind.Range, indoor: false, fixture, flames);
            _nextSort = 0f; // shine now
            return true;
        }

        // After the chunk's collision is built: furnishing rooms looks for free floor with it.
        private void OnEnable() => UOClient.Bake.BakedWorldView.ChunkReady += OnChunk;
        private void OnDisable() => UOClient.Bake.BakedWorldView.ChunkReady -= OnChunk;

        private void OnChunk(UOClient.Bake.LoadedChunk chunk)
        {
            if (chunk?.Root == null)
            {
                return;
            }

            var propLamps = new List<Vector3>();
            var tables = new List<Bounds>();
            foreach (LampFixture f in chunk.Root.GetComponentsInChildren<LampFixture>(true))
            {
                if (f.Light != null)
                {
                    propLamps.Add(f.Light.transform.position); // a room with a fixture in it keeps just that
                }
            }

            foreach (Transform t in chunk.Root.GetComponentsInChildren<Transform>(true))
            {
                // The placed instance (a holder of the same name sits above it).
                if (!t.name.EndsWith("(Clone)", System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (t.name.StartsWith("SM_Prop_Table", System.StringComparison.Ordinal) && RendererBounds(t, out Bounds table))
                {
                    tables.Add(table);
                    continue;
                }

                WorldRules.PropLight kind = WorldRules.PropLights.Find(k => k.Match.Length > 0 && t.name.StartsWith(k.Match, System.StringComparison.Ordinal));
                if (kind == null || t.GetComponentInParent<LampFixture>(true) != null)
                {
                    continue; // not a lamp, or part of a whole fixture (which lights itself)
                }

                // A known prop shines from its flames; any other lamp from just under its top.
                RoomLightKit.Fixture fixture = _kit != null ? _kit.Find(t.name) : null;
                GameObject flames = null;
                Vector3 at;
                if (fixture != null)
                {
                    flames = LightFixtures.Flames(t, fixture, _kit, out at);
                }
                else if (Top(t, out Vector3 top))
                {
                    at = top - Vector3.up * kind.Top;
                }
                else
                {
                    continue;
                }

                Add(t, at, kind, kind.Range, indoor: false, fixture, flames);
                propLamps.Add(at);
            }

            LightRooms(chunk.Root.transform, propLamps, tables);
        }

        private Lamp Add(Transform parent, Vector3 at, WorldRules.PropLight kind, float range, bool indoor, RoomLightKit.Fixture fixture, GameObject flames)
        {
            var go = new GameObject(indoor ? "Room light" : "Lamp light");
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.color = fixture != null ? LightFixtures.ColourOf(fixture.Kind) : Warm;
            light.range = range;
            light.intensity = 0f;
            light.enabled = false;
            var lamp = new Lamp
            {
                Light = light,
                Kind = kind,
                Seed = Random.value * 100f,
                Indoor = indoor,
                Flames = flames,
                Glow = indoor ? null : LightFixtures.Glow(light),
                Glass = GlassOf(parent),
            };
            _lamps.Add(lamp);
            return lamp;
        }

        // ------------------------------------------------------------------ rooms

        /// <summary>
        /// A light source in each room of each building in the chunk (Block */House_*) that has none: the rooms are the
        /// buildings' reflection probes (one per room per storey, the room's box); a building baked without them counts
        /// each floor as one room.
        /// </summary>
        private void LightRooms(Transform root, List<Vector3> propLamps, List<Bounds> tables)
        {
            WorldRules.PropLight kind = WorldRules.IndoorLight;
            foreach (Transform block in root)
            {
                foreach (Transform house in block)
                {
                    if (!house.name.StartsWith("House_", System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Transform interior = house.Find("Interior");
                    if (interior == null)
                    {
                        continue;
                    }

                    var doors = new List<Vector3>();
                    foreach (UOClient.Buildings.BuildingDoor door in house.GetComponentsInChildren<UOClient.Buildings.BuildingDoor>(true))
                    {
                        doors.Add(door.transform.position);
                    }

                    foreach (Bounds room in Rooms(interior))
                    {
                        // A room already lit by a candle or lamp prop keeps just that.
                        Bounds near = room;
                        near.Expand(0.5f);
                        if (propLamps.Exists(near.Contains))
                        {
                            continue;
                        }

                        float floor = room.min.y;
                        float reach = Mathf.Clamp(Mathf.Max(room.size.x, room.size.z) * 0.8f, 4f, kind.Range);
                        Vector3 light;
                        GameObject source = null;
                        GameObject flames = null;
                        RoomLightKit.Fixture fixture = null;
                        if (_kit != null && _kit.TableLights.Length > 0 && TableIn(room, tables, out Bounds table))
                        {
                            // Which of the table lights: fixed per table, so a room looks the same each visit.
                            var at = new Vector3(table.center.x, table.max.y, table.center.z);
                            int pick = Mathf.Abs(Mathf.RoundToInt(at.x * 7.3f + at.z * 3.1f)) % _kit.TableLights.Length;
                            source = Place(_kit.TableLights[pick], at, house);
                            light = at + Vector3.up * 0.4f;
                        }
                        else if (_kit != null && _kit.CornerLight != null && FreeCorner(room, doors, out Vector3 corner))
                        {
                            source = Place(_kit.CornerLight, corner, house);
                            light = corner + Vector3.up * 1.4f;
                        }
                        else
                        {
                            // Nowhere to stand one: the room is lit all the same.
                            light = new Vector3(room.center.x, floor + Mathf.Min(kind.Top, room.size.y - 0.3f), room.center.z);
                        }

                        if (source != null && source.GetComponent<LampFixture>() != null)
                        {
                            continue; // a whole fixture: it registered itself
                        }

                        if (source != null && (fixture = _kit.Find(source.name)) != null)
                        {
                            flames = LightFixtures.Flames(source.transform, fixture, _kit, out light);
                        }

                        Add(house, light, kind, reach, indoor: true, fixture, flames);
                    }
                }
            }
        }

        private static GameObject Place(GameObject prefab, Vector3 at, Transform house)
        {
            GameObject go = Instantiate(prefab, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), house);
            go.name = prefab.name; // the kit knows its flames by name
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
            {
                Destroy(c); // decoration only: the chunk's own collision is what you bump into
            }

            return go;
        }

        /// <summary>The table standing in the room nearest its middle (on this floor), if any.</summary>
        private static bool TableIn(Bounds room, List<Bounds> tables, out Bounds best)
        {
            best = default;
            float bestDistance = float.MaxValue;
            foreach (Bounds t in tables)
            {
                bool inRoom = t.center.x > room.min.x && t.center.x < room.max.x && t.center.z > room.min.z && t.center.z < room.max.z;
                if (!inRoom || t.min.y < room.min.y - 0.3f || t.min.y > room.min.y + 0.5f || t.max.y > room.max.y - 0.6f)
                {
                    continue;
                }

                float d = new Vector2(t.center.x - room.center.x, t.center.z - room.center.z).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = t;
                }
            }

            return bestDistance < float.MaxValue;
        }

        /// <summary>
        /// A corner of the room with clear floor for a lantern: nothing standing there, no wall in the way, and not by a
        /// door.
        /// </summary>
        private bool FreeCorner(Bounds room, List<Vector3> doors, out Vector3 corner)
        {
            const float Inset = 0.45f;
            float floor = room.min.y;
            float x0 = room.min.x + Inset, x1 = room.max.x - Inset, z0 = room.min.z + Inset, z1 = room.max.z - Inset;
            if (x1 > x0 && z1 > z0)
            {
                foreach (Vector3 c in new[] { new Vector3(x0, floor, z0), new Vector3(x1, floor, z1), new Vector3(x1, floor, z0), new Vector3(x0, floor, z1) })
                {
                    if (doors.Exists(d => new Vector2(d.x - c.x, d.z - c.z).sqrMagnitude < 1.6f * 1.6f))
                    {
                        continue;
                    }

                    // The floor here (and nothing standing on it) ...
                    if (!Physics.Raycast(c + Vector3.up * 1.6f, Vector3.down, out RaycastHit hit, 2.2f, ~0, QueryTriggerInteraction.Ignore) ||
                        Mathf.Abs(hit.point.y - floor) > 0.2f)
                    {
                        continue;
                    }

                    // ... and room to stand: no wall or furniture (creatures move on).
                    int n = Physics.OverlapCapsuleNonAlloc(hit.point + Vector3.up * 0.4f, hit.point + Vector3.up * 1.2f, 0.22f, _overlap, ~0, QueryTriggerInteraction.Ignore);
                    bool blocked = false;
                    for (int i = 0; i < n && !blocked; i++)
                    {
                        blocked = _overlap[i].GetComponentInParent<Combat.Combatant>() == null;
                    }

                    if (!blocked)
                    {
                        corner = hit.point;
                        return true;
                    }
                }
            }

            corner = default;
            return false;
        }

        /// <summary>The building's rooms as boxes from floor to ceiling (world space).</summary>
        private static List<Bounds> Rooms(Transform interior)
        {
            var rooms = new List<Bounds>();
            foreach (ReflectionProbe probe in interior.GetComponentsInChildren<ReflectionProbe>(true))
            {
                if (!RoomProbes.IsRoom(probe, out _))
                {
                    continue; // a whole floor's probe, not a room
                }

                Vector3 size = probe.transform.TransformVector(probe.size);
                rooms.Add(new Bounds(probe.transform.position + probe.center, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z))));
            }

            if (rooms.Count > 0)
            {
                return rooms;
            }

            // No room probes: one room per floor (Interior/Floor_N), from the top of its slabs.
            foreach (Transform floor in interior)
            {
                if (!floor.name.StartsWith("Floor_", System.StringComparison.Ordinal) || !RendererBounds(floor, out Bounds b))
                {
                    continue;
                }

                rooms.Add(new Bounds(new Vector3(b.center.x, b.max.y + 1.2f, b.center.z), new Vector3(b.size.x, 2.4f, b.size.z)));
            }

            return rooms;
        }

        private static bool RendererBounds(Transform t, out Bounds b)
        {
            bool any = false;
            b = new Bounds();
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

            return any;
        }

        /// <summary>The top centre of a prop (its renderers' bounds).</summary>
        private static bool Top(Transform t, out Vector3 top)
        {
            bool any = RendererBounds(t, out Bounds b);
            top = any ? new Vector3(b.center.x, b.max.y, b.center.z) : t.position;
            return any;
        }

        // ------------------------------------------------------------------ which shine

        private static void Show(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on)
            {
                go.SetActive(on);
            }
        }

        /// <summary>A lit lantern prop's glass panes (its "Glass" child: see the UOLit prefabs), if it has them.</summary>
        private static Renderer[] GlassOf(Transform prop)
        {
            Transform glass = prop.Find("Glass");
            return glass != null ? glass.GetComponentsInChildren<Renderer>(true) : null;
        }

        private void Update()
        {
            float dark = 1f - DayNight.Daylight;
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            // Lamps outdoors burn only when it's dark enough to matter; room lights always (dimmer by day).
            bool night = dark > 0.05f;
            Vector3 eye = cam.transform.position;
            Look(eye);

            // Now and then: which lamps the camera sees, nearest first, are lit; the nearest of those cast shadows.
            if (Time.time >= _nextSort)
            {
                _nextSort = Time.time + 0.25f;
                _lamps.RemoveAll(l => l.Light == null); // their chunk unloaded
                _shining.RemoveAll(l => l.Light == null);
                GeometryUtility.CalculateFrustumPlanes(cam, _planes);
                _byDistance.Clear();
                foreach (Lamp l in _lamps)
                {
                    bool burning = l.Indoor || night;
                    l.Wanted = false;
                    SetGlass(l, burning ? (l.Indoor ? Mathf.Max(dark, WorldRules.IndoorDayShare) : dark) : 0f);
                    Vector3 at = l.Light.transform.position;
                    float distance = (at - eye).magnitude;
                    Show(l.Flames, burning && distance < FlameDistance);
                    if (!burning)
                    {
                        Show(l.Glow, false);
                        continue;
                    }

                    if (distance < SeeDistance && l.Seen &&
                        GeometryUtility.TestPlanesAABB(_planes, new Bounds(at, Vector3.one * (l.Light.range * 2f))))
                    {
                        _byDistance.Add(l);
                    }
                    else
                    {
                        Show(l.Glow, false);
                    }
                }

                _byDistance.Sort((a, b) => (a.Light.transform.position - eye).sqrMagnitude.CompareTo((b.Light.transform.position - eye).sqrMagnitude));
                int lit = Mathf.Min(LitBudget, _byDistance.Count), shadows = ShadowBudget, glows = 0;
                for (int i = 0; i < _byDistance.Count; i++)
                {
                    Lamp l = _byDistance[i];
                    l.Wanted = i < lit;
                    if (l.Wanted && !_shining.Contains(l))
                    {
                        _shining.Add(l);
                    }

                    float distance = (l.Light.transform.position - eye).magnitude;
                    bool shadow = l.Wanted && shadows > 0 && distance < ShadowReach;
                    shadows -= shadow ? 1 : 0;
                    l.Light.shadows = shadow ? LightShadows.Soft : LightShadows.None;

                    // Glow needs something in the air to light: none in clear weather, wider as fog comes in.
                    bool glow = l.Glow != null && l.Wanted && DayNight.Fog > MinGlowFog && distance < GlowDistance && glows < GlowCount;
                    glows += glow ? 1 : 0;
                    Show(l.Glow, glow);
                    if (glow)
                    {
                        LightFixtures.SizeGlow(l.Glow, l.Light, DayNight.Fog);
                    }
                }
            }

            // The lit ones (and those still fading out): brightness, flicker, fade.
            for (int i = _shining.Count - 1; i >= 0; i--)
            {
                Lamp l = _shining[i];
                l.Fade = Mathf.MoveTowards(l.Fade, l.Wanted ? 1f : 0f, Time.deltaTime * FadeSpeed);
                if (l.Fade <= 0f)
                {
                    l.Light.enabled = false;
                    l.Light.shadows = LightShadows.None;
                    _shining.RemoveAt(i);
                    continue;
                }

                float flicker = 1f + (Mathf.PerlinNoise(Time.time * 6f, l.Seed) - 0.5f) * 0.4f * l.Kind.Flicker;
                float share = l.Indoor ? Mathf.Max(dark, WorldRules.IndoorDayShare) : dark;
                l.Light.enabled = true;
                l.Light.intensity = l.Kind.Intensity * share * flicker * l.Fade;
            }
        }

        /// <summary>
        /// A few lamps a frame, in turn: can the camera see the lamp, or the pool of light on the ground under it? (A clear
        /// line to either; the camera standing in its light counts too.) What's behind a wall stays dark.
        /// </summary>
        private void Look(Vector3 eye)
        {
            int count = _lamps.Count;
            for (int n = 0; n < LooksPerFrame && count > 0; n++)
            {
                _lookAt = (_lookAt + 1) % count;
                Lamp l = _lamps[_lookAt];
                if (l.Light == null)
                {
                    continue;
                }

                Vector3 at = l.Light.transform.position;
                float distance = (at - eye).magnitude;
                l.Seen = distance < SeeDistance &&
                         (distance < l.Light.range * 0.5f || Clear(eye, at) || Clear(eye, at + Vector3.down * 1.8f));
            }
        }

        /// <summary>Nothing between two points (stopping short of the far one: a lamp's own body doesn't hide it).</summary>
        private static bool Clear(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float total = d.magnitude;
            float length = total - 0.6f;
            return length <= 0f || !Physics.Raycast(from, d / total, length, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>A lantern's glass glows with its light's colour when it burns (0 dark, 1 full).</summary>
        private void SetGlass(Lamp l, float on)
        {
            if (l.Glass == null || Mathf.Abs(on - l.GlassOn) < 0.01f)
            {
                return;
            }

            l.GlassOn = on;
            _block ??= new MaterialPropertyBlock();
            _block.SetColor(EmissionColor, l.Light.color * (GlassBright * on));
            foreach (Renderer r in l.Glass)
            {
                if (r != null)
                {
                    r.SetPropertyBlock(_block);
                }
            }
        }
    }
}
