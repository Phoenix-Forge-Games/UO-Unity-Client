// After building the scene, run Tools/Game/AddDeath.eval.cs (ghost material, Corpse prefab, DeathService), then Tools/Game/AddItems.eval.cs. Then Tools/Game/AddLooks.eval.cs. Then Tools/Game/AddTrade.eval.cs. Then Tools/Game/AddSkills.eval.cs. Then Tools/Bake/MakeTileLayers.eval.cs and Tools/Game/AddCrafting.eval.cs. Then Tools/Game/AddMagic.eval.cs. Then Tools/Game/AddHousing.eval.cs. Then Tools/Game/AddPets.eval.cs. Then Tools/Game/AddSpellObjects.eval.cs. Then Tools/Game/AddActiveSkills.eval.cs. Then Tools/Game/AddMagicItems.eval.cs. Then Tools/Game/AddBoats.eval.cs. Then Tools/Game/AddBulkOrders.eval.cs. Then Tools/Game/AddQuests.eval.cs. Then Tools/Game/AddAdmin.eval.cs.
// Builds Assets/UOClient/Game/Player.prefab and Assets/UOClient/Scenes/Game.unity (the networked game scene) and adds
// the scene to the build. Re-run after changing what the prefab or scene should hold. Edit mode only.
const string PrefabPath = "Assets/UOClient/Game/Player.prefab";
const string ScenePath = "Assets/UOClient/Scenes/Game.unity";
// Britain, outside the West bank; spawned a little above the ground and settles once the chunk loads.
var spawnAt = UOClient.WorldSpace.TileCenter(1475, 1645, 40);

// The humanoid model and animator the World scene's view uses.
var world = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/UOClient/Scenes/World.unity");
var wv = new UnityEditor.SerializedObject(UnityEngine.Object.FindFirstObjectByType<UOClient.WorldView>());
var humanoid = (UnityEngine.GameObject)wv.FindProperty("_humanoidPrefabs").GetArrayElementAtIndex(0).objectReferenceValue;
var animator = (UnityEngine.RuntimeAnimatorController)wv.FindProperty("_humanoidAnimator").objectReferenceValue;

// ---- player prefab
var player = new UnityEngine.GameObject("Player");
var graphic = new UnityEngine.GameObject("Graphic"); graphic.transform.SetParent(player.transform, false);
var cc = player.AddComponent<UnityEngine.CharacterController>();
cc.height = 1.8f; cc.radius = 0.35f; cc.center = new UnityEngine.Vector3(0f, 0.9f, 0f);
cc.stepOffset = 0.45f; cc.slopeLimit = 50f; cc.skinWidth = 0.05f; cc.minMoveDistance = 0f;
var nob = player.AddComponent<FishNet.Object.NetworkObject>();
player.AddComponent<UOGame.PlayerMotor>();
player.AddComponent<UOGame.PlayerIdentity>();
player.AddComponent<UOGame.Combat.Combatant>();
var look = player.AddComponent<UOGame.PlayerAppearance>();
var lso = new UnityEditor.SerializedObject(look);
lso.FindProperty("_model").objectReferenceValue = humanoid;
lso.FindProperty("_animator").objectReferenceValue = animator;
lso.FindProperty("_graphic").objectReferenceValue = graphic.transform;
lso.ApplyModifiedPropertiesWithoutUndo();
var nso = new UnityEditor.SerializedObject(nob);
nso.FindProperty("_enablePrediction").boolValue = true;
nso.FindProperty("_predictionType").intValue = 0; // Other (character controller)
nso.FindProperty("_graphicalObject").objectReferenceValue = graphic.transform;
nso.ApplyModifiedPropertiesWithoutUndo();
var prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(player, PrefabPath);
UnityEngine.Object.DestroyImmediate(player);
// ---- mobile (creature / NPC) prefab
const string MobilePath = "Assets/UOClient/Game/Mobile.prefab";
var mobileGo = new UnityEngine.GameObject("Mobile");
var mobileGraphic = new UnityEngine.GameObject("Graphic"); mobileGraphic.transform.SetParent(mobileGo.transform, false);
var mcc = mobileGo.AddComponent<UnityEngine.CharacterController>();
mcc.height = 1.8f; mcc.radius = 0.4f; mcc.center = new UnityEngine.Vector3(0f, 0.9f, 0f); mcc.stepOffset = 0.45f; mcc.slopeLimit = 50f; mcc.skinWidth = 0.05f;
mobileGo.AddComponent<FishNet.Object.NetworkObject>();
mobileGo.AddComponent<FishNet.Component.Transforming.NetworkTransform>();
mobileGo.AddComponent<UOGame.Combat.Combatant>();
var npc = mobileGo.AddComponent<UOGame.NpcMobile>();
var npcso = new UnityEditor.SerializedObject(npc); npcso.FindProperty("_graphic").objectReferenceValue = mobileGraphic.transform; npcso.ApplyModifiedPropertiesWithoutUndo();
var npcLook = mobileGo.AddComponent<UOGame.NpcAppearance>();
var nlso = new UnityEditor.SerializedObject(npcLook);
nlso.FindProperty("_catalog").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.CreatureCatalog>("Assets/UOClient/Data/CreatureCatalog.asset");
nlso.FindProperty("_human").objectReferenceValue = humanoid;
nlso.FindProperty("_humanAnimator").objectReferenceValue = animator;
nlso.FindProperty("_graphic").objectReferenceValue = mobileGraphic.transform;
nlso.ApplyModifiedPropertiesWithoutUndo();
var mobilePrefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(mobileGo, MobilePath);
UnityEngine.Object.DestroyImmediate(mobileGo);

// Register it with FishNet's spawnable prefabs (normally done by its asset postprocessor).
var gen = System.Type.GetType("FishNet.Editing.PrefabCollectionGenerator.Generator, FishNet.Runtime");
gen?.GetMethod("GenerateFull", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.Invoke(null, new object[] { null, true, true });

// ---- scene
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects, UnityEditor.SceneManagement.NewSceneMode.Single);
var camera = UnityEngine.Camera.main; camera.farClipPlane = 1500f; camera.gameObject.AddComponent<UOGame.GameCamera>();
camera.transform.position = spawnAt + new UnityEngine.Vector3(0f, 8f, -10f);

var spawn = new UnityEngine.GameObject("Spawn (Britain)"); spawn.transform.position = spawnAt;
var net = new UnityEngine.GameObject("Network");
var manager = net.AddComponent<FishNet.Managing.NetworkManager>();
var tugboat = net.AddComponent<FishNet.Transporting.Tugboat.Tugboat>();
var nmso = new UnityEditor.SerializedObject(manager);
nmso.FindProperty("_spawnablePrefabs").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.PrefabObjects>("Assets/DefaultPrefabObjects.asset");
nmso.ApplyModifiedPropertiesWithoutUndo();
var transportManager = net.GetComponent<FishNet.Managing.Transporting.TransportManager>() ?? net.AddComponent<FishNet.Managing.Transporting.TransportManager>();
var tmso = new UnityEditor.SerializedObject(transportManager); tmso.FindProperty("Transport").objectReferenceValue = tugboat; tmso.ApplyModifiedPropertiesWithoutUndo();
var palette = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.Bake.WorldPalette>("Assets/UOClient/Data/WorldPalette.asset");
var auth = net.AddComponent<UOGame.Net.GameAuthenticator>();
var aso = new UnityEditor.SerializedObject(auth); aso.FindProperty("_palette").objectReferenceValue = palette; aso.ApplyModifiedPropertiesWithoutUndo();
var serverManager = net.GetComponent<FishNet.Managing.Server.ServerManager>() ?? net.AddComponent<FishNet.Managing.Server.ServerManager>();
var smso = new UnityEditor.SerializedObject(serverManager); smso.FindProperty("_authenticator").objectReferenceValue = auth; smso.ApplyModifiedPropertiesWithoutUndo();
// Interest management (see Tools/Game/AddInterest.eval.cs): clients get only objects within 100 m.
var observers = net.AddComponent<FishNet.Managing.Observing.ObserverManager>();
var oso = new UnityEditor.SerializedObject(observers);
oso.FindProperty("_defaultConditions").InsertArrayElementAtIndex(0);
oso.FindProperty("_defaultConditions").GetArrayElementAtIndex(0).objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<FishNet.Component.Observing.DistanceCondition>("Assets/UOClient/Game/ActorsInRange.asset");
oso.FindProperty("_maximumTimedObserversDuration").floatValue = 1f;
oso.ApplyModifiedPropertiesWithoutUndo();
var characters = net.AddComponent<UOGame.Server.CharacterService>();
var cso = new UnityEditor.SerializedObject(characters);
cso.FindProperty("_network").objectReferenceValue = manager;
cso.FindProperty("_playerPrefab").objectReferenceValue = prefab.GetComponent<FishNet.Object.NetworkObject>();
cso.FindProperty("_start").objectReferenceValue = spawn.transform;
cso.ApplyModifiedPropertiesWithoutUndo();
var spawnService = net.AddComponent<UOGame.Server.SpawnService>();
var ssso = new UnityEditor.SerializedObject(spawnService);
ssso.FindProperty("_network").objectReferenceValue = manager;
ssso.FindProperty("_mobilePrefab").objectReferenceValue = mobilePrefab.GetComponent<FishNet.Object.NetworkObject>();
ssso.FindProperty("_creatures").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Creatures.json");
ssso.FindProperty("_spawns").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Spawns.json");
ssso.FindProperty("_names").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/Names.json");
ssso.FindProperty("_facet").intValue = 1;
ssso.ApplyModifiedPropertiesWithoutUndo();
var combatService = net.AddComponent<UOGame.Server.CombatService>();
var csso = new UnityEditor.SerializedObject(combatService); csso.FindProperty("_network").objectReferenceValue = manager; csso.ApplyModifiedPropertiesWithoutUndo();
var services = net.AddComponent<UOGame.Server.WorldServices>();
var wso = new UnityEditor.SerializedObject(services); wso.FindProperty("_network").objectReferenceValue = manager; wso.ApplyModifiedPropertiesWithoutUndo();

var worldGo = new UnityEngine.GameObject("World");
var view = worldGo.AddComponent<UOClient.Bake.BakedWorldView>();
view.Content = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.Bake.WorldContent>("Assets/UOClient/Data/WorldContent.asset");
view.Palette = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.Bake.WorldPalette>("Assets/UOClient/Data/WorldPalette.asset");
view.Facet = 1; view.RadiusChunks = 2;
var gw = worldGo.AddComponent<UOGame.GameWorld>();
var gwso = new UnityEditor.SerializedObject(gw); gwso.FindProperty("_view").objectReferenceValue = view;
gwso.FindProperty("_worldRules").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/UOClient/Content/World.json"); gwso.ApplyModifiedPropertiesWithoutUndo();

var hud = new UnityEngine.GameObject("HUD");
var hudPanel = hud.AddComponent<UnityEngine.UIElements.PanelRenderer>();
var hpso = new UnityEditor.SerializedObject(hudPanel);
hpso.FindProperty("m_PanelSettings").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>("Assets/UOClient/UI/UOClientPanelSettings.asset");
hpso.FindProperty("sourceAsset").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>("Assets/UOClient/UI/GameHud.uxml");
hpso.ApplyModifiedPropertiesWithoutUndo();
hudPanel.enabled = false; // GameBoot turns UI on for clients only
var combatHud = hud.AddComponent<UOGame.UI.CombatHud>();
var chso = new UnityEditor.SerializedObject(combatHud); chso.FindProperty("_network").objectReferenceValue = manager; chso.ApplyModifiedPropertiesWithoutUndo();
var hudScript = hud.AddComponent<UOGame.UI.GameHud>();
var hso = new UnityEditor.SerializedObject(hudScript); hso.FindProperty("_network").objectReferenceValue = manager; hso.ApplyModifiedPropertiesWithoutUndo();

var menu = new UnityEngine.GameObject("Menu");
var panel = menu.AddComponent<UnityEngine.UIElements.PanelRenderer>();
var pso = new UnityEditor.SerializedObject(panel);
pso.FindProperty("m_PanelSettings").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>("Assets/UOClient/UI/UOClientPanelSettings.asset");
pso.FindProperty("sourceAsset").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>("Assets/UOClient/UI/GameMenu.uxml");
pso.ApplyModifiedPropertiesWithoutUndo();
panel.enabled = false;
var boot = menu.AddComponent<UOGame.GameBoot>();
var bso = new UnityEditor.SerializedObject(boot); bso.FindProperty("_network").objectReferenceValue = manager; bso.ApplyModifiedPropertiesWithoutUndo();

UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, ScenePath);
var scenes = new System.Collections.Generic.List<UnityEditor.EditorBuildSettingsScene>(UnityEditor.EditorBuildSettings.scenes);
if (!scenes.Exists(s => s.path == ScenePath)) { scenes.Insert(0, new UnityEditor.EditorBuildSettingsScene(ScenePath, true)); UnityEditor.EditorBuildSettings.scenes = scenes.ToArray(); }
return "prefab " + (prefab != null) + ", model " + (humanoid != null ? humanoid.name : "none") + ", animator " + (animator != null ? animator.name : "none") + ", generator " + (gen != null) + ", scene saved";
