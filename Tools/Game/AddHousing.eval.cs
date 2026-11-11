// Housing: the kit registry (every prefab the pieces in Content/Housing.json use, and the style materials, so player
// builds carry them), the HousePlot prefab (registered with FishNet; seen from 300 m, further than mobiles), the scene's
// HousingService and BuildMode. Run on the open Game scene after AddMagic; MakeGameScene lists it among the follow-ups.
// Re-run after adding pieces or styles to Housing.json.
const string KitPath = "Assets/UOClient/Game/HouseKit.asset";
const string PlotPath = "Assets/UOClient/Game/HousePlot.prefab";
const string RangePath = "Assets/UOClient/Game/HousesInRange.asset";
string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, ".."));
var housing = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(root, "Assets/UOClient/Content/Housing.json")));

// The kit.
var prefabs = new System.Collections.Generic.List<UnityEngine.GameObject>();
var missing = new System.Collections.Generic.List<string>();
foreach (var piece in (Newtonsoft.Json.Linq.JArray)housing["Pieces"])
{
    foreach (var part in (Newtonsoft.Json.Linq.JArray)piece["Parts"])
    {
        string name = (string)part["Prefab"];
        var prefab = UOClient.Buildings.Synty.SyntyAssets.Prefab(name);
        if (prefab == null) missing.Add(name);
        else if (!prefabs.Contains(prefab)) prefabs.Add(prefab);
    }
}
var materials = new System.Collections.Generic.List<UnityEngine.Material>();
foreach (var list in new[] { "Styles", "RoofStyles" })
{
    foreach (var style in (Newtonsoft.Json.Linq.JArray)housing[list])
    {
        var m = UOClient.Buildings.Synty.SyntyAssets.Material((string)style["Material"]);
        if (m == null) missing.Add((string)style["Material"]);
        else if (!materials.Contains(m)) materials.Add(m);
    }
}
var kit = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.Buildings.Synty.SyntyAssetRegistry>(KitPath);
if (kit == null)
{
    kit = UnityEngine.ScriptableObject.CreateInstance<UOClient.Buildings.Synty.SyntyAssetRegistry>();
    UnityEditor.AssetDatabase.CreateAsset(kit, KitPath);
}
kit.Prefabs = prefabs.ToArray();
kit.Materials = materials.ToArray();
UnityEditor.EditorUtility.SetDirty(kit);

// Houses are big: seen from further away than mobiles.
var range = UnityEditor.AssetDatabase.LoadAssetAtPath<FishNet.Component.Observing.DistanceCondition>(RangePath);
if (range == null)
{
    range = UnityEngine.ScriptableObject.CreateInstance<FishNet.Component.Observing.DistanceCondition>();
    UnityEditor.AssetDatabase.CreateAsset(range, RangePath);
}
var rso = new UnityEditor.SerializedObject(range);
rso.FindProperty("_maximumDistance").floatValue = 300f;
rso.FindProperty("_hideDistancePercent").floatValue = 0.1f;
rso.ApplyModifiedPropertiesWithoutUndo();

// The plot prefab.
var go = new UnityEngine.GameObject("HousePlot");
go.AddComponent<FishNet.Object.NetworkObject>();
go.AddComponent<UOGame.HousePlot>();
var observer = go.AddComponent<FishNet.Observing.NetworkObserver>();
var oso = new UnityEditor.SerializedObject(observer);
oso.FindProperty("_overrideType").intValue = (int)FishNet.Observing.NetworkObserver.ConditionOverrideType.IgnoreManager; // the value (3), not the declaration index
var conditions = oso.FindProperty("_observerConditions");
conditions.ClearArray();
conditions.InsertArrayElementAtIndex(0);
conditions.GetArrayElementAtIndex(0).objectReferenceValue = range;
oso.ApplyModifiedPropertiesWithoutUndo();
var plotPrefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, PlotPath);
UnityEngine.Object.DestroyImmediate(go);

var gen = System.Type.GetType("FishNet.Editing.PrefabCollectionGenerator.Generator, FishNet.Runtime");
gen?.GetMethod("GenerateFull", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.Invoke(null, new object[] { null, true, true });
UnityEditor.AssetDatabase.SaveAssets();

// The scene's services.
var net = UnityEngine.GameObject.Find("Network");
var manager = net.GetComponent<FishNet.Managing.NetworkManager>();
var service = net.GetComponent<UOGame.Server.HousingService>() ?? net.AddComponent<UOGame.Server.HousingService>();
var sso = new UnityEditor.SerializedObject(service);
sso.FindProperty("_network").objectReferenceValue = manager;
sso.FindProperty("_housing").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Housing.json");
sso.FindProperty("_regions").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Regions.json");
sso.FindProperty("_plotPrefab").objectReferenceValue = plotPrefab.GetComponent<FishNet.Object.NetworkObject>();
sso.FindProperty("_kit").objectReferenceValue = kit;
sso.ApplyModifiedPropertiesWithoutUndo();
var build = net.GetComponent<UOGame.UI.BuildMode>() ?? net.AddComponent<UOGame.UI.BuildMode>();
var bso = new UnityEditor.SerializedObject(build);
bso.FindProperty("_network").objectReferenceValue = manager;
bso.FindProperty("_ghost").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/UOClient/Game/Ghost.mat");
bso.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(net.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(net.scene);

var objects = UnityEditor.AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.PrefabObjects>("Assets/DefaultPrefabObjects.asset");
bool registered = false;
for (int i = 0; i < objects.GetObjectCount(); i++)
{
    if (objects.GetObject(true, i) != null && objects.GetObject(true, i).name == "HousePlot") registered = true;
}
return $"kit: {prefabs.Count} prefabs, {materials.Count} materials; missing: {string.Join(", ", missing)}; HousePlot registered: {registered}";
