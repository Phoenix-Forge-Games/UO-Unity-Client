using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient;
using UOGame.Items;
using UOGame.Net;
using Object = UnityEngine.Object;

namespace UOGame.UI
{
    /// <summary>
    /// Equipment icons drawn from the gear itself: the item is put on a mannequin by the same dresser that dresses
    /// every character (<see cref="GameLook.Dresser"/>, so the same outfit parts, colour map, hue and metal-only armour
    /// tint as when it is worn), everything a bare mannequin also has is hidden, and what is left is filmed in front
    /// of a black background into a small texture. A robe dyed red is a red robe; dyed blue it is another icon.
    ///
    /// Renders are cached per item kind and hue (and per the look they are worn on: the local player's sex, race and
    /// colour scheme), made one per frame, and kept for the session. Items that are not worn on the body, or that
    /// show nothing on it (rings, earrings), have no picture: callers keep their name.
    ///
    /// Items with a chosen picture (Content/Icons.json "item": gold, ores, ingots, wood, reagents, gems, potions, food,
    /// tools, bags...) show that instead (<see cref="Show"/>, <see cref="ArtAddress"/>); spell scrolls show their spell.
    ///
    /// Stage: far below the world on <see cref="PaperdollStage.Layer"/>, away from the paperdoll; the sun, fog and
    /// ambient light are swapped out only for the instant of each render, so icons look the same day or night.
    /// </summary>
    public sealed class ItemIcons : MonoBehaviour
    {
        public const int Size = 128;
        private static readonly Vector3 StagePosition = new Vector3(3000f, -2000f, 0f);
        private static readonly Quaternion ViewAngle = Quaternion.Euler(12f, 205f, 0f); // from the front, a little above and to the side

        private static ItemIcons _instance;
        private static readonly Dictionary<string, Texture> Cache = new Dictionary<string, Texture>();
        private static readonly HashSet<string> Nothing = new HashSet<string>();
        private static readonly Dictionary<string, List<Action<Texture>>> Waiting = new Dictionary<string, List<Action<Texture>>>();
        private static readonly Queue<(string Key, ushort Kind, int Hue, Look Wearer)> Jobs = new Queue<(string, ushort, int, Look)>();

        // The meshes a bare mannequin has, per wearer (sex, race, face seed): those are hidden on a dressed one.
        private static readonly Dictionary<string, HashSet<Mesh>> BareMeshes = new Dictionary<string, HashSet<Mesh>>();

        private Camera _camera;
        private Light _key, _fill;

        // ---------------------------------------------------------------- the API

        /// <summary>Whether an item kind could have a rendered picture (it is worn on the body).</summary>
        public static bool CanRender(ushort kind)
        {
            ItemDef def = Server.ItemService.Catalog?.Get(kind);
            return def != null && def.Wearable;
        }

        /// <summary>
        /// The picture of an item kind in a hue, now if it's cached, else when it has been rendered. <paramref name="done"/>
        /// gets null when there is no picture (not worn on the body, nothing visible, or no character art in this build).
        /// </summary>
        public static void Get(ushort kind, int hue, Action<Texture> done)
        {
            if (done == null)
            {
                return;
            }

            if (!CanRender(kind))
            {
                done(null);
                return;
            }

            Look wearer = Wearer();
            string key = $"{kind}:{hue}:{WearerKey(wearer)}";
            if (Cache.TryGetValue(key, out Texture cached))
            {
                done(cached);
                return;
            }

            if (Nothing.Contains(key) || GameLook.Dresser == null)
            {
                done(null); // no dresser: not remembered, so it is tried again once the art is there
                return;
            }

            if (Waiting.TryGetValue(key, out List<Action<Texture>> list))
            {
                list.Add(done);
                return;
            }

            Waiting[key] = new List<Action<Texture>> { done };
            Jobs.Enqueue((key, kind, hue, wearer));
            EnsureInstance();
        }

        /// <summary>
        /// The chosen picture for an item kind, when it could have one: icon/item/&lt;Items.json Type&gt; (the art picked in
        /// Content/Icons.json "item": gold, ores and ingots, wood, reagents, gems, potions, food, tools...), or a spell
        /// scroll's spell icon. Null for an unknown kind. The address may still have no art in this build.
        /// </summary>
        public static string ArtAddress(ushort kind)
        {
            ItemDef def = Server.ItemService.Catalog?.Get(kind);
            if (def == null || string.IsNullOrEmpty(def.Type))
            {
                return null;
            }

            if (def.Category == "Scroll")
            {
                Server.SpellService.SpellDef spell = Server.SpellService.Spells.Find(s => s.Scroll == def.Type);
                if (spell != null)
                {
                    return GameIcons.Spell(spell);
                }
            }

            return global::UOClient.Content.SecureArtAddresses.ItemIcon(def.Type);
        }

        /// <summary>
        /// Shows an item on a tile: its chosen picture (<see cref="ArtAddress"/>) when there is one, else the rendered
        /// picture of its model, as <paramref name="icon"/>'s background; its name (<paramref name="fallback"/>) when it
        /// has neither. The latest call on an element wins.
        /// </summary>
        public static void Show(VisualElement icon, ItemView item, VisualElement fallback)
        {
            if (icon == null)
            {
                return;
            }

            string token = $"{item.Kind}:{item.Hue}";
            icon.userData = token;
            string art = ArtAddress(item.Kind);
            if (art == null)
            {
                ShowRendered(icon, item, fallback, token);
                return;
            }

            global::UOClient.Content.SecureArt.Load<Texture2D>(art, texture =>
            {
                if (!Equals(icon.userData, token))
                {
                    return; // given another item meanwhile
                }

                if (texture == null)
                {
                    ShowRendered(icon, item, fallback, token);
                    return;
                }

                icon.style.backgroundImage = new StyleBackground(texture);
                icon.style.display = DisplayStyle.Flex;
                if (fallback != null)
                {
                    fallback.style.display = DisplayStyle.None;
                }
            });
        }

        private static void ShowRendered(VisualElement icon, ItemView item, VisualElement fallback, string token)
        {
            if (!CanRender(item.Kind))
            {
                icon.style.display = DisplayStyle.None;
                if (fallback != null)
                {
                    fallback.style.display = DisplayStyle.Flex;
                }

                return;
            }

            Get(item.Kind, item.Hue, texture =>
            {
                if (!Equals(icon.userData, token))
                {
                    return;
                }

                bool have = texture != null;
                icon.style.backgroundImage = have ? Background.FromRenderTexture(texture as RenderTexture) : new StyleBackground(StyleKeyword.None);
                icon.style.display = have ? DisplayStyle.Flex : DisplayStyle.None;
                if (fallback != null)
                {
                    fallback.style.display = have ? DisplayStyle.None : DisplayStyle.Flex;
                }
            });
        }

        // ---------------------------------------------------------------- who wears it

        /// <summary>The local player's look without hair or gear (icons show the item as it would look on you).</summary>
        private static Look Wearer()
        {
            Look look = default;
            PlayerMotor me = PlayerMotor.Local;
            PlayerIdentity identity = me != null ? me.GetComponent<PlayerIdentity>() : null;
            if (identity != null)
            {
                look = identity.Look.Value;
            }

            return new Look
            {
                Seed = look.Seed != 0 ? look.Seed : 1,
                Female = look.Female,
                Gargoyle = look.Gargoyle,
                Skin = look.Skin,
                Gear = Array.Empty<Gear>(),
            };
        }

        private static string WearerKey(Look w) => $"{(w.Female ? 'f' : 'm')}{(w.Gargoyle ? 'g' : 'h')}{w.Seed}";

        // ---------------------------------------------------------------- rendering

        private static void EnsureInstance()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("Item Icons");
            go.transform.position = StagePosition;
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ItemIcons>();
        }

        private void Awake()
        {
            var cameraObject = new GameObject("Item Icon Camera");
            cameraObject.transform.SetParent(transform, false);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.orthographic = true;
            _camera.cullingMask = 1 << PaperdollStage.Layer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 20f;
            _camera.allowHDR = false;

            _key = MakeLight("Item Icon Key", LightType.Directional, Quaternion.Euler(35f, 160f, 0f), 1.25f);
            _fill = MakeLight("Item Icon Fill", LightType.Directional, Quaternion.Euler(10f, 300f, 0f), 0.45f);
        }

        private Light MakeLight(string name, LightType type, Quaternion rotation, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.rotation = rotation;
            Light light = go.AddComponent<Light>();
            light.type = type;
            light.intensity = intensity;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << PaperdollStage.Layer;
            light.enabled = false; // only during a render: it would light the paperdoll too
            return light;
        }

        private void LateUpdate()
        {
            if (Jobs.Count == 0)
            {
                return;
            }

            (string key, ushort kind, int hue, Look wearer) = Jobs.Dequeue();
            Texture result = null;
            try
            {
                result = Render(kind, hue, wearer);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            if (result != null)
            {
                Cache[key] = result;
            }
            else if (GameLook.Dresser != null)
            {
                Nothing.Add(key); // really nothing to show; don't try again
            }

            if (Waiting.TryGetValue(key, out List<Action<Texture>> callbacks))
            {
                Waiting.Remove(key);
                foreach (Action<Texture> callback in callbacks)
                {
                    try
                    {
                        callback(result);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
        }

        private Texture Render(ushort kind, int hue, Look wearer)
        {
            SidekickDresser dresser = GameLook.Dresser;
            if (dresser == null)
            {
                return null;
            }

            HashSet<Mesh> bare = Bare(dresser, wearer);
            Look dressed = wearer;
            dressed.Gear = new[] { new Gear { Item = kind, Hue = (ushort)Mathf.Clamp(hue, 0, ushort.MaxValue) } };
            GameObject model = dresser.Build(Looks.ToMobile(dressed, Server.ItemService.Catalog), transform);
            try
            {
                Prepare(model);
                var shown = new List<Renderer>();
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = MeshOf(r);
                    bool part = mesh != null && !bare.Contains(mesh);
                    r.enabled = part;
                    if (part)
                    {
                        shown.Add(r);
                    }
                }

                if (shown.Count == 0 || !Frame(shown))
                {
                    return null;
                }

                var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32)
                {
                    name = $"Item icon {kind}/{hue}",
                    antiAliasing = 4,
                };
                texture.Create();
                Film(texture);
                return texture;
            }
            finally
            {
                Discard(model);
            }
        }

        /// <summary>The meshes of a bare mannequin for this wearer (built once, then thrown away).</summary>
        private HashSet<Mesh> Bare(SidekickDresser dresser, Look wearer)
        {
            string key = WearerKey(wearer);
            if (BareMeshes.TryGetValue(key, out HashSet<Mesh> set))
            {
                return set;
            }

            set = new HashSet<Mesh>();
            GameObject bare = dresser.Build(Looks.ToMobile(wearer, Server.ItemService.Catalog), transform);
            foreach (Renderer r in bare.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = MeshOf(r);
                if (mesh != null)
                {
                    set.Add(mesh);
                }
            }

            Discard(bare);
            BareMeshes[key] = set;
            return set;
        }

        private static Mesh MeshOf(Renderer r)
        {
            if (r is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            MeshFilter filter = r.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        /// <summary>On the icon layer, no colliders, standing in its idle pose.</summary>
        private static void Prepare(GameObject model)
        {
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = PaperdollStage.Layer;
            }

            foreach (Collider c in model.GetComponentsInChildren<Collider>(true))
            {
                c.enabled = false;
                Destroy(c);
            }

            foreach (Animator animator in model.GetComponentsInChildren<Animator>(true))
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.enabled = true;
                foreach (AnimatorControllerParameter p in animator.parameters)
                {
                    if (p.name == "Speed" && p.type == AnimatorControllerParameterType.Float)
                    {
                        animator.SetFloat("Speed", 0f);
                    }
                }

                // Settle into the idle pose (arms down) instead of the bind pose.
                animator.Update(0f);
                animator.Update(0.4f);
            }
        }

        /// <summary>Points the camera at what is shown, filling the square with a little room around it.</summary>
        private bool Frame(List<Renderer> shown)
        {
            Quaternion inverse = Quaternion.Inverse(ViewAngle);
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            var baked = new Mesh();
            var vertices = new List<Vector3>();
            foreach (Renderer r in shown)
            {
                Mesh mesh;
                Matrix4x4 toWorld;
                if (r is SkinnedMeshRenderer skinned)
                {
                    skinned.BakeMesh(baked, false);
                    mesh = baked;
                    toWorld = skinned.transform.localToWorldMatrix;
                }
                else
                {
                    mesh = MeshOf(r);
                    toWorld = r.transform.localToWorldMatrix;
                }

                if (mesh == null || !mesh.isReadable && !(r is SkinnedMeshRenderer))
                {
                    // An unreadable prop: its renderer bounds will do.
                    Bounds b = r.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        Vector3 v = inverse * corner;
                        min = Vector3.Min(min, v);
                        max = Vector3.Max(max, v);
                    }

                    continue;
                }

                mesh.GetVertices(vertices);
                int step = Mathf.Max(1, vertices.Count / 4000);
                for (int i = 0; i < vertices.Count; i += step)
                {
                    Vector3 v = inverse * toWorld.MultiplyPoint3x4(vertices[i]);
                    min = Vector3.Min(min, v);
                    max = Vector3.Max(max, v);
                }
            }

            Destroy(baked);
            if (float.IsInfinity(min.x) || max.x - min.x < 1e-4f && max.y - min.y < 1e-4f)
            {
                return false;
            }

            Vector3 centre = ViewAngle * ((min + max) * 0.5f);
            float half = Mathf.Max(max.x - min.x, max.y - min.y) * 0.5f;
            _camera.orthographicSize = half * 1.12f; // a little room at the edges
            _camera.transform.rotation = ViewAngle;
            _camera.transform.position = centre - ViewAngle * Vector3.forward * (max.z - min.z + 5f);
            _camera.farClipPlane = (max.z - min.z) * 2f + 10f;
            return true;
        }

        /// <summary>One render into <paramref name="target"/>, with the world's sun, fog and ambient light swapped out.</summary>
        private void Film(RenderTexture target)
        {
            var others = new List<Light>();
            foreach (Light light in FindObjectsByType<Light>())
            {
                if (light != _key && light != _fill && light.enabled && light.type == LightType.Directional)
                {
                    others.Add(light);
                    light.enabled = false;
                }
            }

            bool fog = RenderSettings.fog;
            UnityEngine.Rendering.AmbientMode ambientMode = RenderSettings.ambientMode;
            Color ambient = RenderSettings.ambientLight;
            Light sun = RenderSettings.sun;
            try
            {
                RenderSettings.fog = false;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.42f, 0.42f, 0.45f);
                RenderSettings.sun = _key;
                _key.enabled = true;
                _fill.enabled = true;
                _camera.targetTexture = target;
                _camera.Render();
            }
            finally
            {
                _camera.targetTexture = null;
                _key.enabled = false;
                _fill.enabled = false;
                RenderSettings.sun = sun;
                RenderSettings.ambientLight = ambient;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.fog = fog;
                foreach (Light light in others)
                {
                    light.enabled = true;
                }
            }
        }

        /// <summary>Destroys a mannequin with the material and colour map the dresser made for it.</summary>
        private static void Discard(GameObject model)
        {
            if (model == null)
            {
                return;
            }

            // The dresser's body parts are the model root's direct children and share the one material it made for
            // this build (SidekickDresser.Assemble), with its colour map. Held props keep their asset materials.
            var made = new HashSet<Object>();
            foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Material m = r.transform.parent == model.transform ? r.sharedMaterial : null;
                if (m != null && made.Add(m) && m.HasProperty("_ColorMap") && m.GetTexture("_ColorMap") is Texture2D map)
                {
                    made.Add(map);
                }
            }

            model.SetActive(false);
            Destroy(model);
            foreach (Object o in made)
            {
                Destroy(o);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlayMode()
        {
            _instance = null;
            Cache.Clear();
            Nothing.Clear();
            Waiting.Clear();
            Jobs.Clear();
            BareMeshes.Clear();
        }
    }
}
