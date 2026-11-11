// Turns on Read/Write for every model the world palette uses, so chunk collision can merge their meshes (in the
// editor and in builds). Edit mode only; reimports the changed models.
var palette = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.Bake.WorldPalette>("Assets/UOClient/Data/WorldPalette.asset");
var paths = new System.Collections.Generic.HashSet<string>();
foreach (var mesh in palette.Meshes) { if (mesh == null) continue; var p = UnityEditor.AssetDatabase.GetAssetPath(mesh); if (!string.IsNullOrEmpty(p)) paths.Add(p); }
int changed = 0, other = 0, already = 0;
UnityEditor.AssetDatabase.StartAssetEditing();
try {
  foreach (var p in paths) {
    var importer = UnityEditor.AssetImporter.GetAtPath(p) as UnityEditor.ModelImporter;
    if (importer == null) { other++; continue; }
    if (importer.isReadable) { already++; continue; }
    importer.isReadable = true; importer.SaveAndReimport(); changed++;
  }
} finally { UnityEditor.AssetDatabase.StopAssetEditing(); }
return $"{paths.Count} model files: {changed} made readable, {already} already readable, {other} not models";
