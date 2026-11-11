// Bake progress; also saves the palette asset if the bake added to it.
var runner = UnityEngine.Object.FindFirstObjectByType<UOClient.Bake.WorldBakeRunner>();
if (runner == null) return "no bake running";
if (runner.Palette.Grew) { UnityEditor.EditorUtility.SetDirty(runner.Palette); UnityEditor.AssetDatabase.SaveAssets(); runner.Palette.Grew = false; }
return runner.Report() + $"palette: {runner.Palette.Meshes.Count} meshes, {runner.Palette.Materials.Count} materials";
