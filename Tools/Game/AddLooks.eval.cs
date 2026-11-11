// Looks and creation: the scene's GameLook (Sidekick wardrobe and humanoid animator for dressing characters) and the outfit
// templates for SpawnService. Run on the open Game scene after Tools/Game/AddItems.eval.cs.
var net = UnityEngine.GameObject.Find("Network");
var look = net.GetComponent<UOGame.GameLook>() ?? net.AddComponent<UOGame.GameLook>();
var lso = new UnityEditor.SerializedObject(look);
lso.FindProperty("_wardrobe").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.SidekickWardrobe>("Assets/UOClient/Data/SidekickWardrobe.asset");
lso.FindProperty("_humanoidAnimator").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.RuntimeAnimatorController>(UnityEditor.AssetDatabase.GUIDToAssetPath("a560562956f76674faee24a4ab460578"));
lso.ApplyModifiedPropertiesWithoutUndo();

var spawns = net.GetComponent<UOGame.Server.SpawnService>();
var sso = new UnityEditor.SerializedObject(spawns);
sso.FindProperty("_outfits").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Outfits.json");
sso.FindProperty("_creation").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Creation.json");
sso.FindProperty("_skills").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Skills.json");
sso.ApplyModifiedPropertiesWithoutUndo();

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(net.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(net.scene);
return $"GameLook (wardrobe {lso.FindProperty("_wardrobe").objectReferenceValue != null}, animator {lso.FindProperty("_humanoidAnimator").objectReferenceValue != null}); outfits {sso.FindProperty("_outfits").objectReferenceValue != null}";
