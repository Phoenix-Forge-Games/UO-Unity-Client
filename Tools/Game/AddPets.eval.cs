// Pets: the scene's PetService (Content/Pets.json, the animal trainers' stock from Content/Shops.json, the mobile prefab
// pets are spawned from). Run on the open Game scene after AddHousing; MakeGameScene lists it among the follow-ups.
var net = UnityEngine.GameObject.Find("Network");
var spawns = net.GetComponent<UOGame.Server.SpawnService>();
var mobile = new UnityEditor.SerializedObject(spawns).FindProperty("_mobilePrefab").objectReferenceValue;
var pets = net.GetComponent<UOGame.Server.PetService>() ?? net.AddComponent<UOGame.Server.PetService>();
var so = new UnityEditor.SerializedObject(pets);
so.FindProperty("_network").objectReferenceValue = net.GetComponent<FishNet.Managing.NetworkManager>();
so.FindProperty("_mobilePrefab").objectReferenceValue = mobile;
so.FindProperty("_pets").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Pets.json");
so.FindProperty("_shops").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Shops.json");
so.ApplyModifiedPropertiesWithoutUndo();
// Riders draw their mount from the same creature catalog as mobiles; bonded pets that die wear the ghost material.
var mobilePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/UOClient/Game/Mobile.prefab");
var catalog = new UnityEditor.SerializedObject(mobilePrefab.GetComponent<UOGame.NpcAppearance>()).FindProperty("_catalog").objectReferenceValue;
var ghost = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/UOClient/Game/Ghost.mat");
var mobileRoot = UnityEditor.PrefabUtility.LoadPrefabContents("Assets/UOClient/Game/Mobile.prefab");
var nso = new UnityEditor.SerializedObject(mobileRoot.GetComponent<UOGame.NpcAppearance>());
nso.FindProperty("_ghost").objectReferenceValue = ghost;
nso.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.PrefabUtility.SaveAsPrefabAsset(mobileRoot, "Assets/UOClient/Game/Mobile.prefab");
UnityEditor.PrefabUtility.UnloadPrefabContents(mobileRoot);
var playerRoot = UnityEditor.PrefabUtility.LoadPrefabContents("Assets/UOClient/Game/Player.prefab");
var pso = new UnityEditor.SerializedObject(playerRoot.GetComponent<UOGame.PlayerAppearance>());
pso.FindProperty("_catalog").objectReferenceValue = catalog;
pso.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.PrefabUtility.SaveAsPrefabAsset(playerRoot, "Assets/UOClient/Game/Player.prefab");
UnityEditor.PrefabUtility.UnloadPrefabContents(playerRoot);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(net.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(net.scene);
return $"PetService on {net.name}, mobile prefab {mobile?.name}, rider catalog {catalog?.name}, ghost {ghost?.name}";
