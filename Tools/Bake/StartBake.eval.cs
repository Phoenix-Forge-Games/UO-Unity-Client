// Starts a WorldBakeRunner in play mode (run after entering play mode in an empty scene).
// Edit FACETS / AREA below; AREA width 0 bakes whole facets. Output: <project>/WorldData/<facet>/.
var FACETS = new System.Collections.Generic.List<int> { 1 };
var AREA = new UnityEngine.RectInt(22, 24, 3, 3); // Britain (Trammel)
bool OVERWRITE = true;
bool VERIFY = true;
UOClient.Bake.WorldPalette.CanAdd = o => UnityEditor.EditorUtility.IsPersistent(o);
UOClient.Bake.WorldPalette.FindEffect = name => { foreach (var guid in UnityEditor.AssetDatabase.FindAssets(name + " t:Prefab")) { var p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid); if (System.IO.Path.GetFileNameWithoutExtension(p) == name) return UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(p); } return null; };
var go = new UnityEngine.GameObject("World Bake");
var runner = go.AddComponent<UOClient.Bake.WorldBakeRunner>();
runner.Content = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.Bake.WorldContent>("Assets/UOClient/Data/WorldContent.asset");
runner.Palette = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.Bake.WorldPalette>("Assets/UOClient/Data/WorldPalette.asset");
runner.DataFolder = UOClient.ClientSettings.DataFolder;
runner.OutputFolder = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "WorldData"));
runner.Facets = FACETS;
runner.Area = AREA;
runner.Overwrite = OVERWRITE;
runner.Verify = VERIFY;
return "baking " + string.Join(",", FACETS) + " from " + runner.DataFolder + " to " + runner.OutputFolder;
