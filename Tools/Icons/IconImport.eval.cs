// Icon import settings: every texture under Assets/Secure/Icons/<category>/ (placed there by
// Tools/Icons/place_icons.py from Content/Icons.json) is imported as a plain UI texture: no mipmaps, clamped,
// alpha kept, capped at 256 px (the key/mouse prompts at 128, they are shown small), normal-quality compression.
// Only changes importers whose settings differ, so it is quick to run again. Touches no scene, prefab or other asset.
//
// Run through the Unity CLI eval after placing icons, then Tools/SecureArt/SetupSecureArt.eval.cs to (re)make the
// Protected_Icons Addressables entries.
const string Folder = "Assets/Secure/Icons";
if (!UnityEditor.AssetDatabase.IsValidFolder(Folder))
{
    return "No " + Folder + " folder: run Tools/Icons/place_icons.py first.";
}

int changed = 0, total = 0;
UnityEditor.AssetDatabase.StartAssetEditing();
try
{
    foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { Folder }))
    {
        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
        var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
        if (importer == null)
        {
            continue;
        }

        total++;
        bool prompt = path.Contains("/prompt/");
        int max = prompt ? 128 : 256;
        bool same = importer.textureType == UnityEditor.TextureImporterType.Default
                    && !importer.mipmapEnabled
                    && importer.wrapMode == UnityEngine.TextureWrapMode.Clamp
                    && importer.alphaIsTransparency
                    && importer.sRGBTexture
                    && importer.maxTextureSize == max
                    && importer.textureCompression == UnityEditor.TextureImporterCompression.Compressed
                    && !importer.isReadable;
        if (same)
        {
            continue;
        }

        importer.textureType = UnityEditor.TextureImporterType.Default;
        importer.mipmapEnabled = false;
        importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        importer.alphaIsTransparency = true;
        importer.sRGBTexture = true;
        importer.maxTextureSize = max;
        importer.textureCompression = UnityEditor.TextureImporterCompression.Compressed;
        importer.isReadable = false;
        importer.SaveAndReimport();
        changed++;
    }
}
finally
{
    UnityEditor.AssetDatabase.StopAssetEditing();
}

return $"Icons: {total} textures under {Folder}, {changed} re-imported with UI settings.";
