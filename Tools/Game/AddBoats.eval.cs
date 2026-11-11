// Boats: the Boat prefab (registered with FishNet; seen from 300 m like houses) and the scene's BoatService with
// Content/Boats.json and the ship models (Data/BoatCatalog.asset). Run on the open Game scene after AddHousing;
// MakeGameScene lists it among the follow-ups.
const string BoatPath = "Assets/UOClient/Game/Boat.prefab";
var range = UnityEditor.AssetDatabase.LoadAssetAtPath<FishNet.Component.Observing.DistanceCondition>("Assets/UOClient/Game/HousesInRange.asset");
var go = new UnityEngine.GameObject("Boat");
go.AddComponent<FishNet.Object.NetworkObject>();
go.AddComponent<UOGame.Boat>();
var observer = go.AddComponent<FishNet.Observing.NetworkObserver>();
var oso = new UnityEditor.SerializedObject(observer);
oso.FindProperty("_overrideType").intValue = (int)FishNet.Observing.NetworkObserver.ConditionOverrideType.IgnoreManager;
var conditions = oso.FindProperty("_observerConditions");
conditions.ClearArray();
conditions.InsertArrayElementAtIndex(0);
conditions.GetArrayElementAtIndex(0).objectReferenceValue = range;
oso.ApplyModifiedPropertiesWithoutUndo();
var boatPrefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, BoatPath);
UnityEngine.Object.DestroyImmediate(go);
var gen = System.Type.GetType("FishNet.Editing.PrefabCollectionGenerator.Generator, FishNet.Runtime");
gen?.GetMethod("GenerateFull", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.Invoke(null, new object[] { null, true, true });
UnityEditor.AssetDatabase.SaveAssets();

var net = UnityEngine.GameObject.Find("Network");
var manager = net.GetComponent<FishNet.Managing.NetworkManager>();
var service = net.GetComponent<UOGame.Server.BoatService>() ?? net.AddComponent<UOGame.Server.BoatService>();
var sso = new UnityEditor.SerializedObject(service);
sso.FindProperty("_network").objectReferenceValue = manager;
sso.FindProperty("_boats").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Boats.json");
sso.FindProperty("_boatPrefab").objectReferenceValue = boatPrefab.GetComponent<FishNet.Object.NetworkObject>();
sso.FindProperty("_catalog").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.BoatCatalog>("Assets/UOClient/Data/BoatCatalog.asset");
sso.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(net.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(net.scene);

var objects = UnityEditor.AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.PrefabObjects>("Assets/DefaultPrefabObjects.asset");
bool registered = false;
for (int i = 0; i < objects.GetObjectCount(); i++)
{
    if (objects.GetObject(true, i) != null && objects.GetObject(true, i).name == "Boat") registered = true;
}
return $"Boat registered: {registered}; service catalog {(sso.FindProperty("_catalog").objectReferenceValue != null)}";
