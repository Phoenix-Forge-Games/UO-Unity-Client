// Spell objects: the SpellObject prefab (walls, fields and moongates left by spells; registered with FishNet) and the
// scene SpellService's reference to it. Run on the open Game scene after AddPets; MakeGameScene lists it among the follow-ups.
const string PrefabPath = "Assets/UOClient/Game/SpellObject.prefab";
var go = new UnityEngine.GameObject("SpellObject");
go.AddComponent<FishNet.Object.NetworkObject>();
var o = go.AddComponent<UOGame.SpellObject>();
var oso = new UnityEditor.SerializedObject(o);
oso.FindProperty("_look").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/UOClient/Game/Ghost.mat");
oso.ApplyModifiedPropertiesWithoutUndo();
var prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
UnityEngine.Object.DestroyImmediate(go);
var gen = System.Type.GetType("FishNet.Editing.PrefabCollectionGenerator.Generator, FishNet.Runtime");
gen?.GetMethod("GenerateFull", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.Invoke(null, new object[] { null, true, true });
UnityEditor.AssetDatabase.SaveAssets();

var net = UnityEngine.GameObject.Find("Network");
var spells = net.GetComponent<UOGame.Server.SpellService>();
var sso = new UnityEditor.SerializedObject(spells);
sso.FindProperty("_spellObject").objectReferenceValue = prefab.GetComponent<FishNet.Object.NetworkObject>();
sso.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(net.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(net.scene);
var objects = UnityEditor.AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.PrefabObjects>("Assets/DefaultPrefabObjects.asset");
bool registered = false;
for (int i = 0; i < objects.GetObjectCount(); i++)
{
    if (objects.GetObject(true, i) != null && objects.GetObject(true, i).name == "SpellObject") registered = true;
}
return $"SpellObject prefab registered: {registered}";
