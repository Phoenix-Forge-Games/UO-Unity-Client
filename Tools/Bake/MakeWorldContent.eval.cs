// Creates Data/WorldContent.asset from the World scene's WorldView fields, and an empty Data/WorldPalette.asset.
// Run with: unity.exe command --project-path ... "script eval" --code "$(cat Tools/Bake/MakeWorldContent.eval.cs)"
var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/UOClient/Scenes/World.unity");
var view = UnityEngine.Object.FindFirstObjectByType<UOClient.WorldView>();
var so = new UnityEditor.SerializedObject(view);
const string contentPath = "Assets/UOClient/Data/WorldContent.asset";
var content = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.Bake.WorldContent>(contentPath);
if (content == null) { content = UnityEngine.ScriptableObject.CreateInstance<UOClient.Bake.WorldContent>(); UnityEditor.AssetDatabase.CreateAsset(content, contentPath); }
content.WorldMaterial = (UnityEngine.Material)so.FindProperty("_worldMaterial").objectReferenceValue;
content.TerrainMaterial = (UnityEngine.Material)so.FindProperty("_terrainMaterial").objectReferenceValue;
content.WaterMaterial = (UnityEngine.Material)so.FindProperty("_waterMaterial").objectReferenceValue;
var layers = so.FindProperty("_landLayers");
content.LandLayers = new UnityEngine.TerrainLayer[layers.arraySize];
for (int i = 0; i < layers.arraySize; i++) content.LandLayers[i] = (UnityEngine.TerrainLayer)layers.GetArrayElementAtIndex(i).objectReferenceValue;
content.Vegetation = (UOClient.VegetationCatalog)so.FindProperty("_vegetation").objectReferenceValue;
content.WallKit = (UOClient.WallKit)so.FindProperty("_wallKit").objectReferenceValue;
content.Props = (UOClient.PropCatalog)so.FindProperty("_props").objectReferenceValue;
content.Grass = (UnityEngine.VFX.VisualEffectAsset)so.FindProperty("_grass").objectReferenceValue;
content.GenerateHouses = so.FindProperty("_generateHouses").boolValue;
UnityEditor.EditorUtility.SetDirty(content);
const string palettePath = "Assets/UOClient/Data/WorldPalette.asset";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.Bake.WorldPalette>(palettePath) == null)
    UnityEditor.AssetDatabase.CreateAsset(UnityEngine.ScriptableObject.CreateInstance<UOClient.Bake.WorldPalette>(), palettePath);
UnityEditor.AssetDatabase.SaveAssets();
return $"content: world {content.WorldMaterial?.name}, terrain {content.TerrainMaterial?.name}, water {content.WaterMaterial?.name}, layers {content.LandLayers.Length}, veg {content.Vegetation?.name}, wallkit {content.WallKit?.name}, props {content.Props?.name}, grass {content.Grass?.name}, houses {content.GenerateHouses}";
