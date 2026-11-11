// Generates the grass blade assets for Grass.vfx (run with the Unity CLI `script eval --code`):
//  - Assets/UOClient/VFX/GrassBlades{Fine,Wispy,Broad,Seed}.asset: four styles of small blade clusters (tapered,
//    gently curved strips, base at the origin), normals bent toward straight up so they light like the ground.
//    Fine: short, dense, thin. Wispy: taller, sparse, more bent. Broad: wider leafy blades. Seed: tall thin stalks
//    with a small head. Variety inspired by the Pure Nature grass, built from scratch;
//  - Assets/UOClient/VFX/GrassBladeShade.png: a root-to-tip shade (dark at the root, light at the tip), sampled
//    by uv.y, multiplied by the ground colour in the grass shader.
UnityEngine.Mesh Cluster(int seed, int blades, float radius, float minH, float maxH, float minW, float maxW, float maxBend, bool seedHead)
{
    var rng = new System.Random(seed);
    float R() => (float)rng.NextDouble();
    var v = new System.Collections.Generic.List<UnityEngine.Vector3>();
    var n = new System.Collections.Generic.List<UnityEngine.Vector3>();
    var uv = new System.Collections.Generic.List<UnityEngine.Vector2>();
    var tri = new System.Collections.Generic.List<int>();
    const int Segments = 4;
    for (int b = 0; b < blades; b++)
    {
        float a = R() * UnityEngine.Mathf.PI * 2f, d = UnityEngine.Mathf.Sqrt(R()) * radius;
        var root = new UnityEngine.Vector3(UnityEngine.Mathf.Cos(a) * d, 0f, UnityEngine.Mathf.Sin(a) * d);
        float height = UnityEngine.Mathf.Lerp(minH, maxH, R());
        float width = UnityEngine.Mathf.Lerp(minW, maxW, R());
        float facing = R() * UnityEngine.Mathf.PI * 2f;
        var across = new UnityEngine.Vector3(UnityEngine.Mathf.Cos(facing), 0f, UnityEngine.Mathf.Sin(facing));
        var bendDir = new UnityEngine.Vector3(-across.z, 0f, across.x) * (R() < 0.5f ? -1f : 1f);
        float bend = UnityEngine.Mathf.Lerp(0.05f, maxBend, R()) * height;
        int start = v.Count;
        for (int s = 0; s <= Segments; s++)
        {
            float t = s / (float)Segments;
            var centre = root + UnityEngine.Vector3.up * (height * t) + bendDir * (bend * t * t);
            float half = width * 0.5f * (seedHead ? (t > 0.8f ? 2.2f : 0.35f) : (1f - t * 0.92f));
            var faceN = UnityEngine.Vector3.Cross(across, UnityEngine.Vector3.up).normalized;
            var normal = UnityEngine.Vector3.Lerp(faceN, UnityEngine.Vector3.up, 0.75f).normalized;
            v.Add(centre - across * half); n.Add(normal); uv.Add(new UnityEngine.Vector2(0f, t));
            v.Add(centre + across * half); n.Add(normal); uv.Add(new UnityEngine.Vector2(1f, t));
            if (s > 0)
            {
                int i = start + (s - 1) * 2;
                tri.Add(i); tri.Add(i + 2); tri.Add(i + 1);
                tri.Add(i + 1); tri.Add(i + 2); tri.Add(i + 3);
            }
        }
    }
    var m = new UnityEngine.Mesh();
    m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
    m.RecalculateBounds(); m.RecalculateTangents();
    return m;
}
System.IO.Directory.CreateDirectory("Assets/UOClient/VFX");
foreach (var old in new[] { "GrassBladesA", "GrassBladesB" }) UnityEditor.AssetDatabase.DeleteAsset("Assets/UOClient/VFX/" + old + ".asset");
var styles = new (string Name, int Seed, int Blades, float Radius, float MinH, float MaxH, float MinW, float MaxW, float Bend, bool Head)[]
{
    ("GrassBladesFine", 11, 9, 0.12f, 0.18f, 0.30f, 0.018f, 0.028f, 0.18f, false),
    ("GrassBladesWispy", 23, 5, 0.10f, 0.32f, 0.50f, 0.015f, 0.022f, 0.35f, false),
    ("GrassBladesBroad", 37, 4, 0.08f, 0.20f, 0.34f, 0.040f, 0.060f, 0.22f, false),
    ("GrassBladesSeed", 51, 3, 0.06f, 0.40f, 0.60f, 0.012f, 0.016f, 0.10f, true),
};
foreach (var (name, seed, count, radius, lo, hi, wlo, whi, bendMax, head) in styles)
{
    var path = "Assets/UOClient/VFX/" + name + ".asset";
    UnityEditor.AssetDatabase.DeleteAsset(path);
    var mesh = Cluster(seed, count, radius, lo, hi, wlo, whi, bendMax, head);
    mesh.name = name;
    UnityEditor.AssetDatabase.CreateAsset(mesh, path);
}

// Root-to-tip shade: 4 x 64, opaque, dark at the root, brightening toward the tip.
var tex = new UnityEngine.Texture2D(4, 64, UnityEngine.TextureFormat.RGBA32, false);
for (int y = 0; y < 64; y++)
{
    float t = y / 63f;
    float shade = UnityEngine.Mathf.Lerp(0.45f, 1f, UnityEngine.Mathf.SmoothStep(0f, 1f, t));
    for (int x = 0; x < 4; x++) tex.SetPixel(x, y, new UnityEngine.Color(shade, shade, shade, 1f));
}
tex.Apply();
System.IO.File.WriteAllBytes("Assets/UOClient/VFX/GrassBladeShade.png", tex.EncodeToPNG());
UnityEditor.AssetDatabase.ImportAsset("Assets/UOClient/VFX/GrassBladeShade.png", UnityEditor.ImportAssetOptions.ForceSynchronousImport);
var imp = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath("Assets/UOClient/VFX/GrassBladeShade.png");
imp.wrapMode = UnityEngine.TextureWrapMode.Clamp; imp.mipmapEnabled = false; imp.SaveAndReimport();
UnityEditor.AssetDatabase.SaveAssets();
return UnityEditor.AssetDatabase.AssetPathToGUID("Assets/UOClient/VFX/GrassBladeShade.png");
