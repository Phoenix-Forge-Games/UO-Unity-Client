using System;
using System.Collections;
using System.Text;
using UnityEngine;

namespace UOGame
{
    /// <summary>
    /// Test switches for a built game (nothing happens without them), to see what a player build does where the
    /// editor can't look:
    ///   -diagTerrain            log every terrain's state a few times (active, layer, material, shader support)
    ///   -shot &lt;file&gt; &lt;seconds&gt;  save the game's own frame as a PNG after that many seconds
    ///   -noTerrainInstancing    draw terrains without GPU instancing (now the default: ChunkReader.DrawInstanced)
    /// </summary>
    public sealed class BuildDiagnostics : MonoBehaviour
    {
        public static bool NoTerrainInstancing { get; private set; }

        private static string _shotPath;
        private static float _shotAfter;
        private static bool _diagTerrain;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Begin()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-diagTerrain") _diagTerrain = true;
                if (args[i] == "-noTerrainInstancing") { NoTerrainInstancing = true; UOClient.Bake.ChunkReader.DrawInstanced = false; }
                if (args[i] == "-shot" && i + 2 < args.Length)
                {
                    _shotPath = args[i + 1];
                    float.TryParse(args[i + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _shotAfter);
                }
            }

            if (_diagTerrain || _shotPath != null)
            {
                var go = new GameObject("Build diagnostics");
                DontDestroyOnLoad(go);
                go.AddComponent<BuildDiagnostics>();
            }
        }

        private IEnumerator Start()
        {
            float start = Time.realtimeSinceStartup;
            float[] reports = { 8f, 15f, 25f };
            int next = 0;
            bool shot = _shotPath == null;
            while (next < reports.Length || !shot)
            {
                float t = Time.realtimeSinceStartup - start;
                if (_diagTerrain && next < reports.Length && t >= reports[next])
                {
                    Debug.Log(TerrainReport(t));
                    next++;
                }
                else if (!_diagTerrain)
                {
                    next = reports.Length;
                }

                if (!shot && t >= _shotAfter)
                {
                    shot = true;
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(_shotPath);
                    Debug.Log($"[Diag] Screenshot to {_shotPath}");
                }

                yield return null;
            }
        }

        private static string TerrainReport(float t)
        {
            var s = new StringBuilder($"[Diag] {t:0}s: {Terrain.activeTerrains.Length} active terrains");
            Camera cam = Camera.main;
            if (cam != null)
            {
                s.Append($"; camera {cam.name} at {cam.transform.position:0} mask {cam.cullingMask} far {cam.farClipPlane}");
            }

            int shown = 0;
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                if (shown++ >= 3) break;
                TerrainData d = terrain.terrainData;
                Material m = terrain.materialTemplate;
                s.Append($"\n  {terrain.name} at {terrain.transform.position:0} active {terrain.isActiveAndEnabled} layer {terrain.gameObject.layer} " +
                         $"drawHeightmap {terrain.drawHeightmap} instanced {terrain.drawInstanced} " +
                         $"material {(m != null ? m.name : "none")} shader {(m != null && m.shader != null ? m.shader.name + " supported " + m.shader.isSupported + " passes " + m.shader.passCount : "none")} " +
                         $"keywords [{(m != null ? string.Join(" ", m.shaderKeywords) : "")}] " +
                         $"data {(d != null ? $"res {d.heightmapResolution} size {d.size} layers {d.terrainLayers.Length} splat {d.alphamapResolution}" : "none")}");
            }

            return s.ToString();
        }
    }
}
