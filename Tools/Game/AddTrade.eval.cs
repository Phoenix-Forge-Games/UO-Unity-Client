// Trade: the GroundItem prefab (a pouch with the item's name over it, registered with FishNet), ItemService's
// ground prefab, and the scene's TradeService (Shops.json). Run on the open Game scene, in edit mode, after
// Tools/Game/AddLooks.eval.cs.
if (UnityEngine.Application.isPlaying) return "Stop play mode first: changes made in play mode are lost.";
const string GroundPath = "Assets/UOClient/Game/GroundItem.prefab";
var groundGo = new UnityEngine.GameObject("GroundItem");
var graphic = new UnityEngine.GameObject("Graphic");
graphic.transform.SetParent(groundGo.transform, false);
groundGo.AddComponent<FishNet.Object.NetworkObject>();
var ground = groundGo.AddComponent<UOGame.GroundItem>();
var gso = new UnityEditor.SerializedObject(ground);
gso.FindProperty("_pouch").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Synty/PolygonFantasyKingdom/Prefabs/Items/SM_Item_Pouch_01.prefab");
gso.FindProperty("_graphic").objectReferenceValue = graphic.transform;
gso.ApplyModifiedPropertiesWithoutUndo();
var groundPrefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(groundGo, GroundPath);
UnityEngine.Object.DestroyImmediate(groundGo);
var gen = System.Type.GetType("FishNet.Editing.PrefabCollectionGenerator.Generator, FishNet.Runtime");
gen?.GetMethod("GenerateFull", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.Invoke(null, new object[] { null, true, true });
UnityEditor.AssetDatabase.SaveAssets();

var net = UnityEngine.GameObject.Find("Network");
var manager = net.GetComponent<FishNet.Managing.NetworkManager>();
var items = net.GetComponent<UOGame.Server.ItemService>();
var iso = new UnityEditor.SerializedObject(items);
iso.FindProperty("_groundPrefab").objectReferenceValue = groundPrefab.GetComponent<FishNet.Object.NetworkObject>();
iso.ApplyModifiedPropertiesWithoutUndo();

var trade = net.GetComponent<UOGame.Server.TradeService>() ?? net.AddComponent<UOGame.Server.TradeService>();
var tso = new UnityEditor.SerializedObject(trade);
tso.FindProperty("_network").objectReferenceValue = manager;
tso.FindProperty("_shops").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Shops.json");
tso.ApplyModifiedPropertiesWithoutUndo();

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(net.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(net.scene);
int missing = UnityEditor.GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(net);
return $"GroundItem prefab saved; TradeService {net.GetComponents<UOGame.Server.TradeService>().Length} (shops: {tso.FindProperty("_shops").objectReferenceValue != null}); missing scripts on Network: {missing}";
