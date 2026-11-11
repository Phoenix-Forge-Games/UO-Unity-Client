// Quests: the scene's QuestService with Content/Quests.json and Content/Regions.json (slay and escort regions). Run on
// the open Game scene after AddBulkOrders; MakeGameScene lists it among the follow-ups.
var net = UnityEngine.GameObject.Find("Network");
var manager = net.GetComponent<FishNet.Managing.NetworkManager>();
var service = net.GetComponent<UOGame.Server.QuestService>() ?? net.AddComponent<UOGame.Server.QuestService>();
var sso = new UnityEditor.SerializedObject(service);
sso.FindProperty("_network").objectReferenceValue = manager;
sso.FindProperty("_quests").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Quests.json");
sso.FindProperty("_regions").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Regions.json");
sso.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(net.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(net.scene);
return "QuestService " + (sso.FindProperty("_quests").objectReferenceValue != null);
