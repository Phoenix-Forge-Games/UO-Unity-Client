// Secure art, LAST step (Docs/SecureArt.md, "Order of work"): removes the direct references to paid art from our
// prefabs and from the open scene, so neither the player's readable data nor the public repository points at the
// packs. The code then gets the same assets by address (SecureArt.Resolve), which in the editor's default
// "Use Asset Database" play mode reads them straight from the project.
//
// Run it only after SetupSecureArt.eval.cs and after play-testing with Addressables installed. Pass dryRun = true
// first (edit the constant) to see what it would clear. Open the Game scene before running it; other scenes holding
// these components need the same run with them open. Undo: re-assign the fields (the old values are in the log).

const bool dryRun = true;

// Component type -> serialized fields that hold paid art and are resolved by address in code.
var fields = new System.Collections.Generic.Dictionary<string, string[]>
{
    { "UOGame.NpcAppearance", new[] { "_catalog", "_human", "_humanAnimator" } },
    { "UOGame.PlayerAppearance", new[] { "_model", "_animator", "_catalog" } },
    { "UOGame.Corpse", new[] { "_catalog", "_human", "_humanAnimator" } },
    { "UOGame.GameLook", new[] { "_wardrobe", "_humanoidAnimator" } },
    { "UOGame.GroundItem", new[] { "_pouch" } },
    { "UOGame.Server.BoatService", new[] { "_catalog" } },
    { "UOGame.Server.HousingService", new[] { "_kit" } },
    { "UOGame.Net.GameAuthenticator", new[] { "_palette" } },
    { "UOClient.Bake.BakedWorldView", new[] { "Content", "Palette" } },
};

var log = new System.Text.StringBuilder();
int cleared = 0;
System.Func<UnityEngine.Component, string, bool> clear = (component, where) =>
{
    if (component == null || !fields.TryGetValue(component.GetType().FullName, out string[] names)) return false;
    var so = new UnityEditor.SerializedObject(component);
    bool changed = false;
    foreach (string name in names)
    {
        var p = so.FindProperty(name);
        if (p == null)
        {
            log.AppendLine($"WARNING: {component.GetType().Name} has no field {name}");
            continue;
        }

        if (p.objectReferenceValue == null) continue;
        log.AppendLine($"{where}: {component.GetType().Name}.{name} = {UnityEditor.AssetDatabase.GetAssetPath(p.objectReferenceValue)}");
        if (!dryRun) p.objectReferenceValue = null;
        changed = true;
        cleared++;
    }

    if (changed && !dryRun) so.ApplyModifiedPropertiesWithoutUndo();
    return changed;
};

// Prefabs under our own folder.
foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/UOClient" }))
{
    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
    var root = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
    bool changed = false;
    foreach (var c in root.GetComponentsInChildren<UnityEngine.Component>(true))
    {
        changed |= clear(c, path);
    }

    if (changed && !dryRun) UnityEditor.EditorUtility.SetDirty(root);
}

// The open scenes.
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
{
    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
    if (!scene.isLoaded) continue;
    bool changed = false;
    foreach (var go in scene.GetRootGameObjects())
    {
        foreach (var c in go.GetComponentsInChildren<UnityEngine.Component>(true))
        {
            changed |= clear(c, scene.path);
        }
    }

    if (changed && !dryRun)
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
    }
}

if (!dryRun) UnityEditor.AssetDatabase.SaveAssets();
log.Insert(0, (dryRun ? "DRY RUN, would clear " : "Cleared ") + cleared + " reference(s):\n");
return log.ToString();
