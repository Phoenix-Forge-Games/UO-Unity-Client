// Magic: the scene's SpellService (Spells.json). Run on the open Game scene, in edit mode, after
// Tools/Game/AddCrafting.eval.cs.
if (UnityEngine.Application.isPlaying) return "Stop play mode first: changes made in play mode are lost.";
var net = UnityEngine.GameObject.Find("Network");
var spells = net.GetComponent<UOGame.Server.SpellService>() ?? net.AddComponent<UOGame.Server.SpellService>();
var so = new UnityEditor.SerializedObject(spells);
so.FindProperty("_network").objectReferenceValue = net.GetComponent<FishNet.Managing.NetworkManager>();
so.FindProperty("_spells").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Spells.json");
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(net.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(net.scene);
return $"SpellService (data {so.FindProperty("_spells").objectReferenceValue != null}); missing scripts on Network: {UnityEditor.GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(net)}";
