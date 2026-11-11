// Death: the ghost material, the Corpse prefab (registered with FishNet), the player's ghost look and the scene's
// DeathService. Run on the open Game scene; MakeGameScene does the same when it builds the scene.
const string GhostPath = "Assets/UOClient/Game/Ghost.mat";
const string CorpsePath = "Assets/UOClient/Game/Corpse.prefab";

var ghost = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(GhostPath);
if (ghost == null)
{
    ghost = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit"));
    UnityEditor.AssetDatabase.CreateAsset(ghost, GhostPath);
}

// Transparent, pale and faintly lit from within.
ghost.SetFloat("_Surface", 1f);
ghost.SetFloat("_Blend", 0f);
ghost.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
ghost.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
ghost.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
ghost.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
ghost.SetFloat("_ZWrite", 0f);
ghost.SetFloat("_Smoothness", 0.2f);
ghost.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
ghost.SetOverrideTag("RenderType", "Transparent");
ghost.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
ghost.SetColor("_BaseColor", new UnityEngine.Color(0.78f, 0.84f, 0.95f, 0.38f));
ghost.EnableKeyword("_EMISSION");
ghost.SetColor("_EmissionColor", new UnityEngine.Color(0.18f, 0.22f, 0.32f));
ghost.globalIlluminationFlags = UnityEngine.MaterialGlobalIlluminationFlags.None;
UnityEditor.EditorUtility.SetDirty(ghost);

// The corpse prefab looks bodies up the same way living mobiles do.
var mobilePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/UOClient/Game/Mobile.prefab");
var look = new UnityEditor.SerializedObject(mobilePrefab.GetComponent<UOGame.NpcAppearance>());
var corpseGo = new UnityEngine.GameObject("Corpse");
var corpseGraphic = new UnityEngine.GameObject("Graphic");
corpseGraphic.transform.SetParent(corpseGo.transform, false);
corpseGo.AddComponent<FishNet.Object.NetworkObject>();
var corpse = corpseGo.AddComponent<UOGame.Corpse>();
var cso = new UnityEditor.SerializedObject(corpse);
cso.FindProperty("_catalog").objectReferenceValue = look.FindProperty("_catalog").objectReferenceValue;
cso.FindProperty("_human").objectReferenceValue = look.FindProperty("_human").objectReferenceValue;
cso.FindProperty("_humanAnimator").objectReferenceValue = look.FindProperty("_humanAnimator").objectReferenceValue;
cso.FindProperty("_graphic").objectReferenceValue = corpseGraphic.transform;
cso.ApplyModifiedPropertiesWithoutUndo();
var corpsePrefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(corpseGo, CorpsePath);
UnityEngine.Object.DestroyImmediate(corpseGo);

// Players wear the ghost material while dead.
var playerPath = "Assets/UOClient/Game/Player.prefab";
var playerRoot = UnityEditor.PrefabUtility.LoadPrefabContents(playerPath);
var pso = new UnityEditor.SerializedObject(playerRoot.GetComponent<UOGame.PlayerAppearance>());
pso.FindProperty("_ghost").objectReferenceValue = ghost;
pso.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.PrefabUtility.SaveAsPrefabAsset(playerRoot, playerPath);
UnityEditor.PrefabUtility.UnloadPrefabContents(playerRoot);

var gen = System.Type.GetType("FishNet.Editing.PrefabCollectionGenerator.Generator, FishNet.Runtime");
gen?.GetMethod("GenerateFull", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.Invoke(null, new object[] { null, true, true });
UnityEditor.AssetDatabase.SaveAssets();

var net = UnityEngine.GameObject.Find("Network");
var death = net.GetComponent<UOGame.Server.DeathService>() ?? net.AddComponent<UOGame.Server.DeathService>();
var dso = new UnityEditor.SerializedObject(death);
dso.FindProperty("_network").objectReferenceValue = net.GetComponent<FishNet.Managing.NetworkManager>();
dso.FindProperty("_corpsePrefab").objectReferenceValue = corpsePrefab.GetComponent<FishNet.Object.NetworkObject>();
dso.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(net.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(net.scene);

var prefabs = UnityEditor.AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.PrefabObjects>("Assets/DefaultPrefabObjects.asset");
bool registered = false;
for (int i = 0; i < prefabs.GetObjectCount(); i++)
{
    if (prefabs.GetObject(true, i) != null && prefabs.GetObject(true, i).name == "Corpse")
    {
        registered = true;
    }
}

return $"ghost {ghost.shader.name}, corpse prefab saved, registered with FishNet: {registered}, DeathService on {net.name}";
