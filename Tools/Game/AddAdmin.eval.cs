// Admin tools: the scene's AdminService (admin mode, the Admin window's tools, live content, and the admin API on
// 127.0.0.1:7771 for AI agents; Docs/AdminTools.md). Run on the open Game scene after AddQuests; MakeGameScene lists it
// among the follow-ups.
var net = UnityEngine.GameObject.Find("Network");
var manager = net.GetComponent<FishNet.Managing.NetworkManager>();
var service = net.GetComponent<UOGame.Server.AdminService>() ?? net.AddComponent<UOGame.Server.AdminService>();
var sso = new UnityEditor.SerializedObject(service);
sso.FindProperty("_network").objectReferenceValue = manager;
sso.FindProperty("_apiPort").intValue = 7771;
sso.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(net.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(net.scene);
return "AdminService on " + net.scene.name;
