// Bulk orders: the scene's BulkOrderService with Content/BulkOrders.json. Run on the open Game scene after AddBoats;
// MakeGameScene lists it among the follow-ups.
var net = UnityEngine.GameObject.Find("Network");
var manager = net.GetComponent<FishNet.Managing.NetworkManager>();
var service = net.GetComponent<UOGame.Server.BulkOrderService>() ?? net.AddComponent<UOGame.Server.BulkOrderService>();
var sso = new UnityEditor.SerializedObject(service);
sso.FindProperty("_network").objectReferenceValue = manager;
sso.FindProperty("_bulkOrders").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/BulkOrders.json");
sso.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(net.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(net.scene);
return "BulkOrderService " + (sso.FindProperty("_bulkOrders").objectReferenceValue != null);
