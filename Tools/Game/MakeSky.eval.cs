// Sky and weather assets for DayNight (loaded by name from Resources/Sky): the sky material (Shaders/Sky.shader) and the
// rain and snow particle materials with their small generated textures (a soft streak and a soft flake). Run once on
// the open project; it overwrites only these files.
string dir = "Assets/UOClient/Resources/Sky";
System.IO.Directory.CreateDirectory(dir);

UnityEngine.Texture2D Make(int w, int h, System.Func<float, float, float> alpha)
{
    var tex = new UnityEngine.Texture2D(w, h, UnityEngine.TextureFormat.RGBA32, false);
    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
            tex.SetPixel(x, y, new UnityEngine.Color(1f, 1f, 1f, UnityEngine.Mathf.Clamp01(alpha(u, v))));
        }
    }

    return tex;
}

string SaveTex(string name, UnityEngine.Texture2D tex)
{
    string path = $"{dir}/{name}.png";
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
    UnityEditor.AssetDatabase.ImportAsset(path);
    var importer = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
    importer.alphaIsTransparency = true;
    importer.mipmapEnabled = false;
    importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
    importer.SaveAndReimport();
    return path;
}

string streak = SaveTex("RainStreak", Make(8, 64, (u, v) => (1f - u * u) * (1f - UnityEngine.Mathf.Abs(v)) * 1.2f));
string flake = SaveTex("SnowFlake", Make(32, 32, (u, v) => UnityEngine.Mathf.Pow(UnityEngine.Mathf.Clamp01(1f - UnityEngine.Mathf.Sqrt(u * u + v * v)), 1.5f)));

UnityEngine.Material Particle(string name, string texPath)
{
    string path = $"{dir}/{name}.mat";
    var shader = UnityEngine.Shader.Find("Universal Render Pipeline/Particles/Unlit");
    var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
    if (mat == null)
    {
        mat = new UnityEngine.Material(shader);
        UnityEditor.AssetDatabase.CreateAsset(mat, path);
    }

    mat.shader = shader;
    mat.SetTexture("_BaseMap", UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(texPath));
    mat.SetColor("_BaseColor", UnityEngine.Color.white);
    // Transparent, alpha blended, no depth writes.
    mat.SetFloat("_Surface", 1f);
    mat.SetFloat("_Blend", 0f);
    mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
    mat.SetFloat("_ZWrite", 0f);
    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    UnityEditor.EditorUtility.SetDirty(mat);
    return mat;
}

Particle("Rain", streak);
Particle("Snow", flake);

string skyPath = $"{dir}/Sky.mat";
var skyShader = UnityEngine.Shader.Find("UOGame/Sky");
var sky = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(skyPath);
if (sky == null)
{
    sky = new UnityEngine.Material(skyShader);
    UnityEditor.AssetDatabase.CreateAsset(sky, skyPath);
}

sky.shader = skyShader;
UnityEditor.EditorUtility.SetDirty(sky);
UnityEditor.AssetDatabase.SaveAssets();
return $"sky shader {(skyShader != null)}, particle shader {(UnityEngine.Shader.Find("Universal Render Pipeline/Particles/Unlit") != null)}, assets in {dir}";
