using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;
using UOClient.Data;

namespace UOClient
{
    /// <summary>
    /// Grass grown by a VFX Graph (`Grass.vfx`) on each terrain chunk near the player. The chunk bakes two small
    /// textures the graph reads per blade:
    ///  - NormalHeight: the terrain's normal (xyz) and height above the chunk origin (w), at every heightmap corner,
    ///    so each blade stands on the ground as drawn and leans with it;
    ///  - ColorMask: the ground's colour (rgb, the terrain layers' average colours blended by the splatmap) and how
    ///    grassy it is (a, the grassy layers' splat weight), so blades thin out as the ground blends to paving, rock
    ///    or dirt and take the colour of the ground they grow from (browner on dry ground, darker in forest).
    /// </summary>
    public sealed class GrassField
    {
        /// <summary>How grassy each land kind's terrain layer is (0: no grass grows there). Only grass textures grow grass.</summary>
        private static float Grassiness(LandKind kind)
        {
            switch (kind)
            {
                case LandKind.Grass: return 1f;
                case LandKind.Jungle: return 1f;
                case LandKind.Forest: return 0.85f;
                case LandKind.Swamp: return 0.8f;
                default: return 0f; // dirt, mud, furrows, sand, rock, paving: bare

            }
        }

        private readonly VisualEffectAsset _asset;
        private readonly Dictionary<TerrainLayer, Color> _layerColours = new Dictionary<TerrainLayer, Color>();

        public GrassField(VisualEffectAsset asset)
        {
            _asset = asset;
        }

        /// <summary>
        /// Adds the grass effect to a freshly built terrain chunk. `splat` is the chunk's alphamap ([row, col, layer],
        /// rows along local Z), `used` the land kind of each of its layers, `bare` per tile (with a one-tile border,
        /// [ty + 1, tx + 1]) where nothing should grow: under houses, floors and roads.
        /// </summary>
        public GameObject Add(GameObject terrainObject, TerrainData data, float[,,] splat, List<LandKind> used, bool[,] bare, int chunkTiles)
        {
            if (_asset == null)
            {
                return null;
            }

            int splatRes = splat.GetLength(0);
            var grassy = new float[used.Count];
            var colours = new Color[used.Count];
            for (int i = 0; i < used.Count; i++)
            {
                grassy[i] = Grassiness(used[i]);
                colours[i] = AverageColour(data.terrainLayers[i]);
            }

            var colourMask = new Texture2D(splatRes, splatRes, TextureFormat.RGBA32, false, false)
            {
                name = "Grass ColorMask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[splatRes * splatRes];
            for (int row = 0; row < splatRes; row++)
            {
                // The tile under this texel; rows run up local Z, i.e. UO Y counted from the chunk's south edge.
                int ty = chunkTiles - 1 - row * chunkTiles / splatRes;
                for (int col = 0; col < splatRes; col++)
                {
                    int tx = col * chunkTiles / splatRes;
                    // Colour from the grassy layers only (weighted by how much of each is here), so a blade on the
                    // edge of a grass patch is the grass's colour, not a blend with the mud or paving beside it.
                    Color colour = Color.black;
                    float grass = 0f;
                    for (int i = 0; i < used.Count; i++)
                    {
                        float w = splat[row, col, i] * grassy[i];
                        colour += colours[i] * w;
                        grass += w;
                    }

                    colour = grass > 0.001f ? colour / grass : Color.black;

                    if (bare != null && bare[ty + 1, tx + 1])
                    {
                        grass = 0f;
                    }

                    colour.a = Mathf.Clamp01(grass);
                    pixels[row * splatRes + col] = colour;
                }
            }

            colourMask.SetPixels32(pixels);
            colourMask.Apply(false, true);

            int heightRes = data.heightmapResolution;
            var normalHeight = new Texture2D(heightRes, heightRes, TextureFormat.RGBAHalf, false, true)
            {
                name = "Grass NormalHeight",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            float[,] heights = data.GetHeights(0, 0, heightRes, heightRes);
            var nh = new Color[heightRes * heightRes];
            for (int row = 0; row < heightRes; row++)
            {
                for (int col = 0; col < heightRes; col++)
                {
                    Vector3 n = data.GetInterpolatedNormal(col / (float)(heightRes - 1), row / (float)(heightRes - 1));
                    nh[row * heightRes + col] = new Color(n.x, n.y, n.z, heights[row, col] * data.size.y);
                }
            }

            normalHeight.SetPixels(nh);
            normalHeight.Apply(false, true);

            var go = new GameObject("Grass");
            go.transform.SetParent(terrainObject.transform, false);
            var effect = go.AddComponent<VisualEffect>();
            effect.visualEffectAsset = _asset;
            effect.SetTexture("ColorMask", colourMask);
            effect.SetTexture("NormalHeight", normalHeight);
            effect.SetVector3("ChunkSize", new Vector3(data.size.x, data.size.y, data.size.z));
            go.AddComponent<GrassTextures>().Own(colourMask, normalHeight);
            go.AddComponent<GrassViewer>();
            return go;
        }

        /// <summary>A terrain layer's overall colour: its diffuse texture averaged (read back through the GPU).</summary>
        private Color AverageColour(TerrainLayer layer)
        {
            if (layer == null || layer.diffuseTexture == null)
            {
                return new Color(0.35f, 0.45f, 0.2f);
            }

            if (_layerColours.TryGetValue(layer, out Color cached))
            {
                return cached;
            }

            RenderTexture rt = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(layer.diffuseTexture, rt);
            RenderTexture.active = rt;
            var read = new Texture2D(8, 8, TextureFormat.RGBA32, false, false);
            read.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            Color sum = Color.black;
            foreach (Color c in read.GetPixels())
            {
                sum += c;
            }

            Object.Destroy(read);
            Color average = sum / 64f;
            average.a = 1f;
            // The layer's tint (Unity multiplies the diffuse by diffuseRemapMax).
            Vector4 tint = layer.diffuseRemapMax;
            if (tint.x > 0f || tint.y > 0f || tint.z > 0f)
            {
                average = new Color(average.r * tint.x, average.g * tint.y, average.b * tint.z, 1f);
            }

            _layerColours[layer] = average;
            return average;
        }
    }

    /// <summary>Destroys a chunk's baked grass textures with it.</summary>
    public sealed class GrassTextures : MonoBehaviour
    {
        private Texture2D _a, _b;

        public void Own(Texture2D a, Texture2D b)
        {
            _a = a;
            _b = b;
        }

        private void OnDestroy()
        {
            if (_a != null) Destroy(_a);
            if (_b != null) Destroy(_b);
        }
    }

    /// <summary>Tells a chunk's grass where the camera is (in the chunk's space) each frame, for the distance falloff.</summary>
    public sealed class GrassViewer : MonoBehaviour
    {
        private VisualEffect _effect;

        private void Awake() => _effect = GetComponent<VisualEffect>();

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera != null && _effect != null)
            {
                _effect.SetVector3("Viewer", transform.InverseTransformPoint(camera.transform.position));
                _effect.SetTexture("PushMap", GrassPushMap.Current(camera.transform.position));
                _effect.SetVector3("PushCentre", transform.InverseTransformPoint(GrassPushMap.Centre));
            }
        }
    }
}
