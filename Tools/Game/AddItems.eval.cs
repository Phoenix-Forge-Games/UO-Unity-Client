// Items: the scene's ItemService (item kinds and loot packs) and the item HUD (backpack, equipment, loot windows)
// beside the combat HUD. Run on the open Game scene after Tools/Game/AddDeath.eval.cs.
var net = UnityEngine.GameObject.Find("Network");
var manager = net.GetComponent<FishNet.Managing.NetworkManager>();
var items = net.GetComponent<UOGame.Server.ItemService>() ?? net.AddComponent<UOGame.Server.ItemService>();
var iso = new UnityEditor.SerializedObject(items);
iso.FindProperty("_network").objectReferenceValue = manager;
iso.FindProperty("_items").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Items.json");
iso.FindProperty("_lootPacks").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/LootPacks.json");
iso.ApplyModifiedPropertiesWithoutUndo();

var combatHud = UnityEngine.Object.FindAnyObjectByType<UOGame.UI.CombatHud>(UnityEngine.FindObjectsInactive.Include);
var hud = combatHud.gameObject.GetComponent<UOGame.UI.ItemHud>() ?? combatHud.gameObject.AddComponent<UOGame.UI.ItemHud>();
var hso = new UnityEditor.SerializedObject(hud);
hso.FindProperty("_network").objectReferenceValue = manager;
hso.FindProperty("_hudStyles").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.StyleSheet>("Assets/UOClient/UI/Hud.uss");
hso.ApplyModifiedPropertiesWithoutUndo();

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(net.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(net.scene);
return $"ItemService on {net.name} (items: {iso.FindProperty("_items").objectReferenceValue != null}, loot: {iso.FindProperty("_lootPacks").objectReferenceValue != null}); ItemHud on {combatHud.gameObject.name}";
