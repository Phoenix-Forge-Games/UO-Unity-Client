using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UOClient.Data;
using UOClient.World;
using Object = UnityEngine.Object;

namespace UOClient
{
    /// <summary>
    /// Builds a Sidekick character from a UO mobile: a naked human (male or female by body), its hair and
    /// beard, and for each worn item the matching parts of a Sidekick outfit made of the same stuff
    /// (platemail → a plate outfit's torso, shoulders...). Outfits are picked per item graphic, so every
    /// "platemail tunic" looks alike. Each character gets its own 32×32 colour map, starting from a
    /// ready-made palette and recoloured with the UO skin, hair and dye hues.
    /// </summary>
    public sealed class SidekickDresser
    {
        // Sidekick part types: the two digits before the part code in a part's name (…_10TORS_HU01).
        private const int Head = 1, Hair = 2, BrowL = 3, BrowR = 4, EarL = 7, EarR = 8, Beard = 9;
        private const int Torso = 10, Hips = 17, AttHead = 22, Nose = 35;

        /// <summary>Body areas a UO item can cover, and the Sidekick part types that make each one.</summary>
        private enum Region
        {
            Torso,
            Arms,
            Hands,
            Hips,
            Legs,
            Feet,
            Head,
            Back,
            Shoulders,
            Elbows,
            Knees,
            HipFront,
        }

        private static readonly int[][] RegionTypes =
        {
            new[] { 10 },
            new[] { 11, 12, 13, 14 },
            new[] { 15, 16 },
            new[] { 17 },
            new[] { 18, 19 },
            new[] { 20, 21 },
            new[] { 22 },
            new[] { 24 },
            new[] { 29, 30 },
            new[] { 31, 32 },
            new[] { 33, 34 },
            new[] { 25 },
        };

        // Where each region's five outfit colours sit in the 16×16 colour grid (column, top row; rows go down).
        private static readonly (int U, int V)[] RegionCells =
        {
            (0, 15), (1, 15), (3, 15), (0, 10), (1, 10), (3, 10), (5, 10), (7, 10), (5, 15), (7, 15), (9, 15), (11, 15),
        };

        // Arms, hands, legs and feet are left/right pairs with a column each.
        private static readonly int[] RegionColumns = { 1, 2, 2, 1, 2, 2, 1, 1, 2, 2, 2, 1 };

        // UO layers.
        private const byte Shoes = 0x03, Pants = 0x04, Shirt = 0x05, Helm = 0x06, Gloves = 0x07, HairLayer = 0x0B;
        private const byte Waist = 0x0C, InnerTorso = 0x0D, BeardLayer = 0x10, MiddleTorso = 0x11, Arms = 0x13;
        private const byte Cloak = 0x14, OuterTorso = 0x16, OuterLegs = 0x17, InnerLegs = 0x18;

        private readonly SidekickWardrobe _wardrobe;
        private readonly TileData _tiles;
        private readonly Hues _hues;
        private readonly RuntimeAnimatorController _controller;
        private readonly Transform _rigSource;
        private readonly Avatar _avatar;
        private readonly Dictionary<int, GameObject> _body = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, List<GameObject>> _faces = new Dictionary<int, List<GameObject>>();
        private readonly Dictionary<GameObject, SkinnedMeshRenderer> _renderers = new Dictionary<GameObject, SkinnedMeshRenderer>();
        private readonly Dictionary<GameObject, int> _types = new Dictionary<GameObject, int>();

        public SidekickDresser(SidekickWardrobe wardrobe, TileData tiles, Hues hues, RuntimeAnimatorController controller)
        {
            _wardrobe = wardrobe;
            _tiles = tiles;
            _hues = hues;
            _controller = controller;

            SkinnedMeshRenderer baseRenderer = wardrobe.BaseModel.GetComponentInChildren<SkinnedMeshRenderer>(true);
            _rigSource = baseRenderer.rootBone;
            while (_rigSource.parent != null && _rigSource.parent != wardrobe.BaseModel.transform)
            {
                _rigSource = _rigSource.parent;
            }

            _avatar = wardrobe.BaseModel.GetComponentInChildren<Animator>(true)?.avatar;

            foreach (GameObject part in wardrobe.Body)
            {
                if (part != null)
                {
                    _body[TypeOf(part)] = part;
                }
            }

            foreach (GameObject part in wardrobe.Faces)
            {
                if (part == null)
                {
                    continue;
                }

                int type = TypeOf(part);
                if (!_faces.TryGetValue(type, out List<GameObject> list))
                {
                    _faces[type] = list = new List<GameObject>();
                }

                list.Add(part);
            }
        }

        /// <summary>A string that changes whenever the model would: body, skin, and every worn item.</summary>
        public static string Signature(Mobile mobile)
        {
            var sb = new StringBuilder();
            sb.Append(mobile.Body).Append(':').Append(mobile.Hue);
            foreach (KeyValuePair<byte, EquippedItem> pair in mobile.Equipment)
            {
                if (pair.Key == 0x19 || pair.Key == 0x15 || pair.Key == 0x1D)
                {
                    continue; // mount, backpack, bank box: not drawn on the body
                }

                sb.Append('|').Append(pair.Key).Append('=').Append(pair.Value.Graphic).Append('/').Append(pair.Value.Hue);
            }

            return sb.ToString();
        }

        /// <summary>Builds the dressed model (rig, parts and Animator) under `parent`.</summary>
        public GameObject Build(Mobile mobile, Transform parent)
        {
            var parts = new List<GameObject>();
            var regionHues = new ushort[RegionTypes.Length];
            var dressed = new bool[RegionTypes.Length];
            uint seed = mobile.Serial * 2654435761u;

            Dress(mobile, parts, regionHues, dressed);

            // Naked where nothing covers: body parts for torso, arms, hands, hips, legs and feet.
            for (int r = 0; r <= (int)Region.Feet; r++)
            {
                if (!dressed[r])
                {
                    foreach (int type in RegionTypes[r])
                    {
                        Add(parts, _body, type);
                    }
                }
            }

            // Face: head, eyes, teeth and tongue from the body; nose, ears and brows vary per character.
            foreach (int type in new[] { Head, 5, 6, 36, 37 })
            {
                Add(parts, _body, type);
            }

            int face = (int)(seed >> 8);
            foreach ((int left, int right) in new[] { (EarL, EarR), (BrowL, BrowR), (Nose, Nose) })
            {
                GameObject l = Pick(_faces, left, face);
                parts.Add(l ?? (_body.TryGetValue(left, out GameObject bl) ? bl : null));
                if (right != left)
                {
                    // The matching side from the same head variant.
                    GameObject r = l != null ? Twin(_faces, l, right) : null;
                    parts.Add(r ?? (_body.TryGetValue(right, out GameObject br) ? br : null));
                }
            }

            bool hideHair = false;
            if (mobile.Equipment.TryGetValue(Helm, out EquippedItem helm))
            {
                HeadKind kind = HeadOf(NameOf(helm));
                hideHair = kind == HeadKind.ClosedHelm || kind == HeadKind.Hood;
            }

            if (!hideHair && mobile.Equipment.TryGetValue(HairLayer, out EquippedItem hair))
            {
                AddStyle(parts, _wardrobe.Hair, hair.Graphic);
            }

            if (mobile.Equipment.TryGetValue(BeardLayer, out EquippedItem beard))
            {
                AddStyle(parts, _wardrobe.Beards, beard.Graphic);
            }

            parts.RemoveAll(p => p == null);
            Texture2D colours = ColourMap(mobile, regionHues, seed);
            return Assemble(parts, colours, mobile.IsFemale, parent, $"Sidekick {mobile.Serial:X8}");
        }

        // ------------------------------------------------------------------ equipment → outfits

        /// <summary>
        /// For each region, the outermost worn item that covers it picks a matching outfit and contributes
        /// that outfit's parts for the region.
        /// </summary>
        private void Dress(Mobile mobile, List<GameObject> parts, ushort[] regionHues, bool[] dressed)
        {
            var cover = new (Region Region, byte[] Layers)[]
            {
                (Region.Torso, new[] { OuterTorso, MiddleTorso, InnerTorso, Shirt }),
                (Region.Arms, new[] { OuterTorso, Arms, Shirt, InnerTorso }),
                (Region.Hands, new[] { Gloves }),
                (Region.Hips, new[] { OuterTorso, OuterLegs, InnerLegs, Pants }),
                (Region.Legs, new[] { OuterTorso, OuterLegs, InnerLegs, Pants }),
                (Region.Feet, new[] { Shoes }),
                (Region.Head, new[] { Helm }),
                (Region.Back, new[] { Cloak }),
                (Region.Shoulders, new[] { Arms, InnerTorso }),
                (Region.Elbows, new[] { Arms }),
                (Region.Knees, new[] { InnerLegs, Pants }),
                (Region.HipFront, new[] { MiddleTorso, Waist }),
            };

            foreach ((Region region, byte[] layers) in cover)
            {
                foreach (byte layer in layers)
                {
                    if (!mobile.Equipment.TryGetValue(layer, out EquippedItem item) || !Covers(layer, item, region))
                    {
                        continue;
                    }

                    SidekickWardrobe.Outfit outfit = OutfitFor(item, layer, region);
                    if (outfit == null)
                    {
                        continue;
                    }

                    bool any = false;
                    foreach (int type in RegionTypes[(int)region])
                    {
                        GameObject part = PartOf(outfit, type);
                        if (part != null)
                        {
                            parts.Add(part);
                            any = true;
                        }
                    }

                    if (any)
                    {
                        dressed[(int)region] = true;
                        regionHues[(int)region] = item.Hue;
                        break;
                    }
                }
            }
        }

        /// <summary>Whether a worn item reaches this region (a shirt has sleeves; a kilt doesn't reach the legs).</summary>
        private bool Covers(byte layer, EquippedItem item, Region region)
        {
            string name = NameOf(item);
            OutfitKind kind = KindOf(name);
            bool armour = kind != OutfitKind.Cloth && kind != OutfitKind.Fine && kind != OutfitKind.Robe;
            switch (layer)
            {
                case InnerTorso:
                    // Mail shirts have sleeves; plate and leather chests leave the arms to the arms piece.
                    return region == Region.Torso ||
                           (region == Region.Arms && (kind == OutfitKind.Chain || kind == OutfitKind.Ring)) ||
                           (region == Region.Shoulders && armour);
                case Arms:
                    return region == Region.Arms || region == Region.Elbows || (region == Region.Shoulders && armour);
                case OuterLegs:
                    // Skirts hang to the ankles; kilts stop at the knee.
                    return region == Region.Hips || (region == Region.Legs && !name.Contains("kilt"));
                case InnerLegs:
                case Pants:
                    // UO puts leg armour on the pants layer too.
                    return region == Region.Hips || region == Region.Legs || (region == Region.Knees && armour);
                case MiddleTorso:
                    return region == Region.Torso ||
                           (region == Region.HipFront && (name.Contains("surcoat") || name.Contains("tabard")));
                case Waist:
                    return region == Region.HipFront && name.Contains("apron");
                default:
                    return true;
            }
        }

        /// <summary>An outfit made of the item's stuff with a part for this region, the same one for every copy of the item.</summary>
        private SidekickWardrobe.Outfit OutfitFor(EquippedItem item, byte layer, Region region)
        {
            string name = NameOf(item);
            OutfitKind kind = layer == OuterTorso && !name.Contains("robe") && !name.Contains("shroud")
                ? OutfitKind.Fine // dresses, jester suits
                : KindOf(name);
            HeadKind head = region == Region.Head ? HeadOf(name) : HeadKind.None;
            int primary = RegionTypes[(int)region][0];

            if (region == Region.Back)
            {
                // Cloaks match any cape, whatever it's made of.
                var capes = new List<SidekickWardrobe.Outfit>();
                foreach (SidekickWardrobe.Outfit outfit in _wardrobe.Outfits)
                {
                    if (outfit.Cape && PartOf(outfit, primary) != null)
                    {
                        capes.Add(outfit);
                    }
                }

                return capes.Count > 0 ? capes[(int)((item.Graphic * 2654435761u) >> 8) % capes.Count] : null;
            }

            // Same stuff first, then close substitutes, then (for a hat or a long skirt) anything with the right shape.
            foreach (OutfitKind? candidate in Candidates(kind))
            {
                var matches = new List<SidekickWardrobe.Outfit>();
                foreach (SidekickWardrobe.Outfit outfit in _wardrobe.Outfits)
                {
                    if ((candidate.HasValue && outfit.Kind != candidate.Value) || PartOf(outfit, primary) == null)
                    {
                        continue;
                    }

                    if (region == Region.Head && outfit.Head != head)
                    {
                        continue;
                    }

                    // Robes and dresses want long hips; trousers and kilts want short ones.
                    if (region == Region.Hips && outfit.LongHips != (layer == OuterTorso || name.Contains("skirt")))
                    {
                        continue;
                    }

                    matches.Add(outfit);
                }

                if (matches.Count > 0)
                {
                    return matches[(int)((item.Graphic * 2654435761u) >> 8) % matches.Count];
                }
            }

            return null;
        }

        private static IEnumerable<OutfitKind?> Candidates(OutfitKind kind)
        {
            foreach (OutfitKind fallback in Fallbacks(kind))
            {
                yield return fallback;
            }

            yield return null;
        }

        private static IEnumerable<OutfitKind> Fallbacks(OutfitKind kind)
        {
            yield return kind;
            switch (kind)
            {
                case OutfitKind.Ring:
                    yield return OutfitKind.Chain;
                    break;
                case OutfitKind.Chain:
                    yield return OutfitKind.Ring;
                    break;
                case OutfitKind.Studded:
                    yield return OutfitKind.Leather;
                    break;
                case OutfitKind.Bone:
                    yield return OutfitKind.Plate;
                    break;
                case OutfitKind.Robe:
                    yield return OutfitKind.Fine;
                    break;
                case OutfitKind.Fine:
                    yield return OutfitKind.Cloth;
                    break;
            }

            if (kind != OutfitKind.Cloth)
            {
                yield return kind == OutfitKind.Plate || kind == OutfitKind.Chain || kind == OutfitKind.Ring
                    ? OutfitKind.Plate
                    : OutfitKind.Leather;
            }

            yield return OutfitKind.Cloth;
        }

        private string NameOf(EquippedItem item) =>
            _tiles != null && item.Graphic < _tiles.Statics.Length ? (_tiles.GetStatic(item.Graphic).Name ?? "").ToLowerInvariant() : "";

        /// <summary>What a UO item is made of, from its tiledata name.</summary>
        public static OutfitKind KindOf(string name)
        {
            if (name.Contains("plate") || name.Contains("helmet") || name.Contains("bascinet") || name.Contains("norse"))
            {
                return OutfitKind.Plate;
            }

            if (name.Contains("chain"))
            {
                return OutfitKind.Chain;
            }

            if (name.Contains("ringmail"))
            {
                return OutfitKind.Ring;
            }

            if (name.Contains("studded"))
            {
                return OutfitKind.Studded;
            }

            if (name.Contains("bone"))
            {
                return OutfitKind.Bone;
            }

            if (name.Contains("leather") || name.Contains("boots"))
            {
                return OutfitKind.Leather;
            }

            if (name.Contains("robe") || name.Contains("shroud"))
            {
                return OutfitKind.Robe;
            }

            if (name.Contains("fancy") || name.Contains("doublet") || name.Contains("surcoat") || name.Contains("jester") ||
                name.Contains("dress") || name.Contains("kimono") || name.Contains("elven"))
            {
                return OutfitKind.Fine;
            }

            return OutfitKind.Cloth;
        }

        /// <summary>What a UO helmet or hat is, from its tiledata name.</summary>
        public static HeadKind HeadOf(string name)
        {
            if (name.Contains("close helm") || name.Contains("plate helm") || name.Contains("bascinet") || name.Contains("great helm"))
            {
                return HeadKind.ClosedHelm;
            }

            if (name.Contains("helm") || name.Contains("coif") || name.Contains("norse"))
            {
                return HeadKind.OpenHelm;
            }

            if (name.Contains("hood") || name.Contains("cowl"))
            {
                return HeadKind.Hood;
            }

            if (name.Contains("crown") || name.Contains("circlet") || name.Contains("tiara"))
            {
                return HeadKind.Crown;
            }

            if (name.Contains("cap") || name.Contains("bandana"))
            {
                return HeadKind.Cap;
            }

            return HeadKind.Hat;
        }

        // ------------------------------------------------------------------ parts

        /// <summary>The Sidekick part type from a part's name (SK_FANT_KNGT_01_10TORS_HU01 → 10).</summary>
        public static int TypeOf(GameObject part)
        {
            string[] bits = part.name.Split('_');
            return bits.Length >= 6 && bits[4].Length >= 2 && int.TryParse(bits[4].Substring(0, 2), out int type) ? type : -1;
        }

        private int CachedType(GameObject part)
        {
            if (!_types.TryGetValue(part, out int type))
            {
                _types[part] = type = TypeOf(part);
            }

            return type;
        }

        private GameObject PartOf(SidekickWardrobe.Outfit outfit, int type)
        {
            foreach (GameObject part in outfit.Parts)
            {
                if (part != null && CachedType(part) == type)
                {
                    return part;
                }
            }

            return null;
        }

        private static void Add(List<GameObject> parts, Dictionary<int, GameObject> source, int type)
        {
            if (source.TryGetValue(type, out GameObject part))
            {
                parts.Add(part);
            }
        }

        private static GameObject Pick(Dictionary<int, List<GameObject>> source, int type, int hash)
        {
            return source.TryGetValue(type, out List<GameObject> list) && list.Count > 0 ? list[(hash & 0x7FFFFFFF) % list.Count] : null;
        }

        /// <summary>The other side's part from the same variant (…_03EBRL… → …_04EBRR…).</summary>
        private static GameObject Twin(Dictionary<int, List<GameObject>> source, GameObject part, int type)
        {
            string prefix = part.name.Substring(0, part.name.LastIndexOf('_', part.name.LastIndexOf('_') - 1));
            if (source.TryGetValue(type, out List<GameObject> list))
            {
                foreach (GameObject other in list)
                {
                    if (other.name.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        return other;
                    }
                }
            }

            return null;
        }

        private static void AddStyle(List<GameObject> parts, SidekickWardrobe.Style[] styles, ushort graphic)
        {
            if (styles == null)
            {
                return;
            }

            foreach (SidekickWardrobe.Style style in styles)
            {
                if (style.Graphic == graphic)
                {
                    parts.AddRange(style.Parts);
                    return;
                }
            }

            // An unknown style still gets some hair.
            if (styles.Length > 0)
            {
                parts.AddRange(styles[graphic % styles.Length].Parts);
            }
        }

        // ------------------------------------------------------------------ colours

        /// <summary>
        /// A copy of one of the ready-made palettes with the skin, hair and dyed regions replaced by UO hues.
        /// A hue keeps each cell's brightness, the way UO maps grey art onto a hue's ramp.
        /// </summary>
        private Texture2D ColourMap(Mobile mobile, ushort[] regionHues, uint seed)
        {
            Texture2D scheme = _wardrobe.Schemes != null && _wardrobe.Schemes.Length > 0
                ? _wardrobe.Schemes[(int)(seed >> 16) % _wardrobe.Schemes.Length]
                : null;
            Texture2D map = scheme != null ? CloneReadable(scheme) : Grey();
            if (_hues != null)
            {
                // Skin, lips, ears, eyelids, nails, nose.
                ushort skin = (ushort)(mobile.Hue & 0x7FFF);
                foreach ((int u, int v) in new[] { (0, 5), (0, 4), (0, 3), (0, 1), (1, 5), (2, 5), (1, 4), (2, 4), (1, 3), (2, 3), (1, 2), (2, 2), (1, 1) })
                {
                    Rehue(map, u, v, skin);
                }

                if (mobile.Equipment.TryGetValue(HairLayer, out EquippedItem hair))
                {
                    foreach ((int u, int v) in new[] { (6, 4), (6, 3), (6, 2), (6, 5), (7, 5) })
                    {
                        Rehue(map, u, v, hair.Hue);
                    }
                }

                if (mobile.Equipment.TryGetValue(BeardLayer, out EquippedItem beard))
                {
                    foreach ((int u, int v) in new[] { (7, 4), (7, 3), (7, 2) })
                    {
                        Rehue(map, u, v, beard.Hue);
                    }
                }

                for (int r = 0; r < regionHues.Length; r++)
                {
                    if (regionHues[r] == 0)
                    {
                        continue;
                    }

                    (int u0, int v0) = RegionCells[r];
                    for (int column = 0; column < RegionColumns[r]; column++)
                    {
                        for (int slot = 0; slot < 5; slot++)
                        {
                            Rehue(map, u0 + column, v0 - slot, regionHues[r]);
                        }
                    }
                }
            }

            map.Apply(false, true);
            return map;
        }

        /// <summary>
        /// A writable copy of a palette. The shipped colour maps aren't CPU-readable, so this copies through the GPU
        /// (as Genesis Survivors' ColorService does): blit to an sRGB target and read it back, point-filtered so each
        /// 2×2 cell stays one flat colour.
        /// </summary>
        private static Texture2D CloneReadable(Texture2D source)
        {
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false)
            {
                name = source.name + " (dressed)",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        private static Texture2D Grey()
        {
            var map = new Texture2D(32, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var grey = new Color32[32 * 32];
            for (int i = 0; i < grey.Length; i++)
            {
                grey[i] = new Color32(160, 160, 160, 255);
            }

            map.SetPixels32(grey);
            return map;
        }

        /// <summary>Recolours one 2×2 cell of the grid with a hue, keeping its brightness.</summary>
        private void Rehue(Texture2D map, int u, int v, ushort hue)
        {
            Color current = map.GetPixel(u * 2, v * 2);
            float lum = current.r * 0.3f + current.g * 0.59f + current.b * 0.11f;
            // Shades 8..30 keep dark cells from going black and light ones from washing out.
            int shade = 8 + Mathf.RoundToInt(lum * 22f);
            if (!_hues.TryShade(hue, shade, out byte r, out byte g, out byte b))
            {
                return;
            }

            var c = new Color32(r, g, b, 255);
            for (int dy = 0; dy < 2; dy++)
            {
                for (int dx = 0; dx < 2; dx++)
                {
                    map.SetPixel(u * 2 + dx, v * 2 + dy, c);
                }
            }
        }

        // ------------------------------------------------------------------ assembly

        /// <summary>
        /// One rig from the base model and a renderer per part bound to it by bone name (meshes shared
        /// between characters), all with one material carrying this character's colour map.
        /// </summary>
        public GameObject Assemble(List<GameObject> parts, Texture2D colours, bool female, Transform parent, string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            GameObject rig = Object.Instantiate(_rigSource.gameObject, root.transform, false);
            rig.name = _rigSource.name;
            Dictionary<string, Transform> bones = BoneMap(rig.transform);


            var material = new Material(_wardrobe.Material);
            if (colours != null)
            {
                material.SetTexture("_ColorMap", colours);
            }

            // Every bone first: adding bones under a rig that renderers are already bound to leaves those
            // renderers skinning against stale bone indices (holes and missing limbs).
            var sources = new List<(GameObject Part, SkinnedMeshRenderer Renderer, Transform[] Bones, Transform Root)>();
            foreach (GameObject part in parts)
            {
                SkinnedMeshRenderer source = RendererOf(part);
                if (source == null || source.sharedMesh == null)
                {
                    continue;
                }

                Transform[] sourceBones = source.bones;
                var mapped = new Transform[sourceBones.Length];
                for (int i = 0; i < sourceBones.Length; i++)
                {
                    mapped[i] = (sourceBones[i] != null ? Bone(bones, sourceBones[i]) : null) ?? rig.transform;
                }

                Transform rootBone = (source.rootBone != null ? Bone(bones, source.rootBone) : null) ?? rig.transform;
                sources.Add((part, source, mapped, rootBone));
            }

            foreach ((GameObject part, SkinnedMeshRenderer source, Transform[] mapped, Transform rootBone) in sources)
            {
                var go = new GameObject(part.name);
                go.transform.SetParent(root.transform, false);
                var renderer = go.AddComponent<SkinnedMeshRenderer>();
                renderer.bones = mapped;
                renderer.rootBone = rootBone;
                renderer.sharedMesh = MeshFor(source.sharedMesh, female);
                renderer.localBounds = source.localBounds;
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            // Last, so it finds the renderers: with CullUpdateTransforms an Animator that saw none never poses the rig.
            Animator animator = root.AddComponent<Animator>();
            animator.avatar = _avatar;
            animator.runtimeAnimatorController = _controller;
            animator.applyRootMotion = false; // the server owns position
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            return root;
        }

        private readonly Dictionary<(Mesh, bool), Mesh> _meshes = new Dictionary<(Mesh, bool), Mesh>();

        /// <summary>
        /// A copy of a part's mesh, shared by every character that wears it, with the body shape baked in
        /// (female: the masculineFeminine blend shape at full), so no character pays for blend shapes.
        /// </summary>
        private Mesh MeshFor(Mesh source, bool female)
        {
            if (_meshes.TryGetValue((source, female), out Mesh mesh))
            {
                return mesh;
            }

            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Vector4[] tangents = source.tangents;
            int shape = female ? FindBlendShape(source, "masculineFeminine") : -1;
            if (shape >= 0)
            {
                int frame = source.GetBlendShapeFrameCount(shape) - 1;
                var dv = new Vector3[vertices.Length];
                var dn = new Vector3[vertices.Length];
                var dt = new Vector3[vertices.Length];
                source.GetBlendShapeFrameVertices(shape, frame, dv, dn, dt);
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] += dv[i];
                    if (normals.Length == vertices.Length)
                    {
                        normals[i] = (normals[i] + dn[i]).normalized;
                    }

                    if (tangents.Length == vertices.Length)
                    {
                        Vector3 t = (Vector3)tangents[i] + dt[i];
                        tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w);
                    }
                }
            }

            mesh = new Mesh
            {
                name = source.name + (female ? " (female)" : ""),
                indexFormat = source.indexFormat,
            };
            mesh.vertices = vertices;
            mesh.triangles = source.triangles;
            mesh.uv = source.uv;
            mesh.uv2 = source.uv2;
            mesh.uv3 = source.uv3;
            mesh.uv4 = source.uv4;
            mesh.normals = normals;
            mesh.colors = source.colors;
            mesh.tangents = tangents;
            mesh.boneWeights = source.boneWeights;
            mesh.bindposes = source.bindposes;
            _meshes[(source, female)] = mesh;
            return mesh;
        }

        private static int FindBlendShape(Mesh mesh, string name)
        {
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                if (mesh.GetBlendShapeName(i).Contains(name))
                {
                    return i;
                }
            }

            return -1;
        }

        private SkinnedMeshRenderer RendererOf(GameObject part)
        {
            if (!_renderers.TryGetValue(part, out SkinnedMeshRenderer renderer))
            {
                _renderers[part] = renderer = part.GetComponentInChildren<SkinnedMeshRenderer>(true);
            }

            return renderer;
        }

        private static Dictionary<string, Transform> BoneMap(Transform rig)
        {
            var map = new Dictionary<string, Transform>();
            foreach (Transform bone in rig.GetComponentsInChildren<Transform>(true))
            {
                if (!map.ContainsKey(bone.name))
                {
                    map[bone.name] = bone;
                }
            }

            return map;
        }

        /// <summary>
        /// The rig's bone with this name; bones only some parts have (cloth and hair dynamics) are copied
        /// in under their parent the first time a part needs them.
        /// </summary>
        private static Transform Bone(Dictionary<string, Transform> bones, Transform source)
        {
            if (bones.TryGetValue(source.name, out Transform bone))
            {
                return bone;
            }

            Transform parent = source.parent != null ? Bone(bones, source.parent) : null;
            if (parent == null)
            {
                return null;
            }

            var copy = new GameObject(source.name).transform;
            copy.SetParent(parent, false);
            copy.localPosition = source.localPosition;
            copy.localRotation = source.localRotation;
            copy.localScale = source.localScale;
            bones[source.name] = copy;
            return copy;
        }
    }
}
