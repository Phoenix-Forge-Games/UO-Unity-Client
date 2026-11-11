// Gathering and crafting: the scene's HarvestService (Harvest.json) and CraftService (Crafting.json, when it exists).
// Run on the open Game scene, in edit mode, after Tools/Game/AddSkills.eval.cs. Build the tile layers first with
// Tools/Bake/MakeTileLayers.eval.cs.
if (UnityEngine.Application.isPlaying) return "Stop play mode first: changes made in play mode are lost.";
var net = UnityEngine.GameObject.Find("Network");
var manager = net.GetComponent<FishNet.Managing.NetworkManager>();
var harvest = net.GetComponent<UOGame.Server.HarvestService>() ?? net.AddComponent<UOGame.Server.HarvestService>();
var hso = new UnityEditor.SerializedObject(harvest);
hso.FindProperty("_network").objectReferenceValue = manager;
hso.FindProperty("_harvest").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Harvest.json");
hso.ApplyModifiedPropertiesWithoutUndo();
var craftType = System.Type.GetType("UOGame.Server.CraftService, UOGame");
string craft = "not yet";
if (craftType != null)
{
    var c = net.GetComponent(craftType) ?? net.AddComponent(craftType);
    var cso = new UnityEditor.SerializedObject(c);
    cso.FindProperty("_network").objectReferenceValue = manager;
    cso.FindProperty("_crafting").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Crafting.json");
    cso.ApplyModifiedPropertiesWithoutUndo();
    craft = cso.FindProperty("_crafting").objectReferenceValue != null ? "with Crafting.json" : "without data";
}
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(net.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(net.scene);
return $"HarvestService (data {hso.FindProperty("_harvest").objectReferenceValue != null}); CraftService {craft}; missing scripts on Network: {UnityEditor.GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(net)}";
