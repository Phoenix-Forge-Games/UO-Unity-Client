// Magic items: the scene's ItemService reads Content/Magic.json (random loot properties, slayers, tooltips; migrated by
// Tools/Data/export_servuo_magic.py). Run on the open Game scene after AddActiveSkills; MakeGameScene lists it.
var net = UnityEngine.GameObject.Find("Network");
var items = net.GetComponent<UOGame.Server.ItemService>();
var so = new UnityEditor.SerializedObject(items);
so.FindProperty("_magic").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Magic.json");
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(net.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(net.scene);
return "ItemService reads Magic.json";
