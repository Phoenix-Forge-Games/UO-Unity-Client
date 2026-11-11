// Skills: the scene's SkillService (gains, locks, bandages). Run on the open Game scene, in edit mode, after
// Tools/Game/AddTrade.eval.cs.
if (UnityEngine.Application.isPlaying) return "Stop play mode first: changes made in play mode are lost.";
var net = UnityEngine.GameObject.Find("Network");
var skills = net.GetComponent<UOGame.Server.SkillService>() ?? net.AddComponent<UOGame.Server.SkillService>();
var so = new UnityEditor.SerializedObject(skills);
so.FindProperty("_network").objectReferenceValue = net.GetComponent<FishNet.Managing.NetworkManager>();
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(net.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(net.scene);
return $"SkillService {net.GetComponents<UOGame.Server.SkillService>().Length}; missing scripts on Network: {UnityEditor.GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(net)}";
