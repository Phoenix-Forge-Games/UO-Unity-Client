// Active skills: the scene's SkillUseService (Hiding, Stealth, Meditation, Detect Hidden, Anatomy, Evaluating
// Intelligence, Arms Lore, Item Identification, Begging, Stealing, Parry; rules in Content/Skills.json "Uses").
// Run on the open Game scene after AddSpellObjects; MakeGameScene lists it among the follow-ups.
var net = UnityEngine.GameObject.Find("Network");
var uses = net.GetComponent<UOGame.Server.SkillUseService>() ?? net.AddComponent<UOGame.Server.SkillUseService>();
var so = new UnityEditor.SerializedObject(uses);
so.FindProperty("_network").objectReferenceValue = net.GetComponent<FishNet.Managing.NetworkManager>();
so.FindProperty("_skills").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Skills.json");
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(net.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(net.scene);
return "SkillUseService wired";
