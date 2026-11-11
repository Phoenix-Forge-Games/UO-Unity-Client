using System.Collections.Generic;
using UnityEngine;

namespace UOClient
{
    /// <summary>
    /// Something that pushes grass aside as it moves through it (players, NPCs, creatures). Registers itself while
    /// enabled; <see cref="GrassPushMap"/> paints every pusher near the camera into the push map each frame.
    /// </summary>
    public sealed class GrassPusher : MonoBehaviour
    {
        [Tooltip("How far (metres) around this object the grass bends away.")]
        public float Radius = 0.7f;

        internal static readonly List<GrassPusher> All = new List<GrassPusher>();

        private void OnEnable() => All.Add(this);

        private void OnDisable() => All.Remove(this);
    }

    /// <summary>
    /// A small texture around the camera (<see cref="Size"/> metres across) recording which way, and how hard, the
    /// grass is pushed at each spot: rg = push direction (x, z) mapped to 0..1, b = strength. Pushes are remembered
    /// and spring back over about <see cref="RecoverSeconds"/>, so a walk through grass leaves a trail that stands
    /// back up behind you. The map is anchored to the world (its centre snaps to whole cells and its contents shift
    /// with it as the camera moves), so trails stay where they were made. Grass chunks read it relative to
    /// <see cref="Centre"/>.
    /// </summary>
    public static class GrassPushMap
    {
        public const int Resolution = 64;
        public const float Size = 32f;
        private const float Cell = Size / Resolution;
        private const float RecoverSeconds = 1.2f;

        private static Texture2D _map;
        private static Color32[] _pixels;
        private static float[] _strength, _dirX, _dirZ;
        private static float[] _scratch;
        private static int _frame = -1;
        private static int _originX = int.MinValue, _originZ = int.MinValue;

        /// <summary>The map's centre in world space this frame.</summary>
        public static Vector3 Centre { get; private set; }

        /// <summary>The push map for this frame (updated on the first call each frame).</summary>
        public static Texture2D Current(Vector3 camera)
        {
            if (_map == null)
            {
                _map = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, true)
                {
                    name = "Grass PushMap",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                int n = Resolution * Resolution;
                _pixels = new Color32[n];
                _strength = new float[n];
                _dirX = new float[n];
                _dirZ = new float[n];
                _scratch = new float[n];
            }

            if (_frame == Time.frameCount)
            {
                return _map;
            }

            _frame = Time.frameCount;
            Follow(camera);
            Recover(Time.deltaTime);
            foreach (GrassPusher pusher in GrassPusher.All)
            {
                Paint(pusher.transform.position - Centre, pusher.Radius);
            }

            for (int k = 0; k < _pixels.Length; k++)
            {
                float s = _strength[k];
                _pixels[k] = s <= 0.002f
                    ? new Color32(128, 128, 0, 0)
                    : new Color32(
                        (byte)Mathf.RoundToInt((_dirX[k] * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((_dirZ[k] * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt(s * 255f),
                        255);
            }

            _map.SetPixels32(_pixels);
            _map.Apply(false, false);
            return _map;
        }

        /// <summary>Snaps the map's centre to whole cells near the camera, shifting what it remembers along with it.</summary>
        private static void Follow(Vector3 camera)
        {
            int ox = Mathf.FloorToInt(camera.x / Cell), oz = Mathf.FloorToInt(camera.z / Cell);
            if (_originX != int.MinValue && (ox != _originX || oz != _originZ))
            {
                int sx = ox - _originX, sz = oz - _originZ;
                Shift(_strength, sx, sz);
                Shift(_dirX, sx, sz);
                Shift(_dirZ, sx, sz);
            }

            _originX = ox;
            _originZ = oz;
            Centre = new Vector3(ox * Cell, camera.y, oz * Cell);
        }

        private static void Shift(float[] values, int sx, int sz)
        {
            for (int j = 0; j < Resolution; j++)
            {
                for (int i = 0; i < Resolution; i++)
                {
                    int fi = i + sx, fj = j + sz;
                    _scratch[j * Resolution + i] = fi >= 0 && fj >= 0 && fi < Resolution && fj < Resolution
                        ? values[fj * Resolution + fi]
                        : 0f;
                }
            }

            System.Array.Copy(_scratch, values, values.Length);
        }

        private static void Recover(float dt)
        {
            float keep = Mathf.Exp(-dt * 3f / RecoverSeconds); // about 95% recovered after RecoverSeconds
            for (int k = 0; k < _strength.Length; k++)
            {
                _strength[k] *= keep;
            }
        }

        private static void Paint(Vector3 offset, float r)
        {
            if (Mathf.Abs(offset.x) > Size * 0.5f || Mathf.Abs(offset.z) > Size * 0.5f)
            {
                return;
            }

            // Pixel (i, j) covers x = -Size/2 + (i + 0.5) * Cell (and likewise z); the edge row stays still so the
            // clamped border never pushes.
            int i0 = Mathf.Max(1, Mathf.FloorToInt((offset.x - r + Size * 0.5f) / Cell));
            int i1 = Mathf.Min(Resolution - 2, Mathf.CeilToInt((offset.x + r + Size * 0.5f) / Cell));
            int j0 = Mathf.Max(1, Mathf.FloorToInt((offset.z - r + Size * 0.5f) / Cell));
            int j1 = Mathf.Min(Resolution - 2, Mathf.CeilToInt((offset.z + r + Size * 0.5f) / Cell));
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    float dx = -Size * 0.5f + (i + 0.5f) * Cell - offset.x;
                    float dz = -Size * 0.5f + (j + 0.5f) * Cell - offset.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    float s = 1f - d / r;
                    int k = j * Resolution + i;
                    if (s <= _strength[k])
                    {
                        continue;
                    }

                    _strength[k] = s;
                    _dirX[k] = d > 0.001f ? dx / d : 0f;
                    _dirZ[k] = d > 0.001f ? dz / d : 0f;
                }
            }
        }
    }
}
