// Secure art: makes (or brings up to date) the Addressables setup that keeps the paid art out of the player's
// readable files and out of the public repository. See Docs/SecureArt.md. Safe to run again at any time: it
// creates what's missing, updates settings, and re-syncs the auto-managed groups; it never touches scenes or prefabs.
//
// Needs the Addressables package installed and compiled (the UOClient.SecureArt.* assemblies only exist then).
// Run through the Unity CLI eval, like Tools/Game/Add*.eval.cs.
//
// What it does:
//  1. Addressables settings + the "Patch" profile (Remote.BuildPath = AddressableContent/[BuildTarget],
//     Remote.LoadPath = {UOClient.Patching.PatchPaths.ContentRoot}/[BuildTarget]), remote catalog named
//     catalog_content, no unique bundle ids, not built with the player.
//  2. Protected_Catalogs: our catalogs that point into the packs, each at its SecureArtAddresses address, label "preload".
//  3. Protected_Pack_<Pack>: every paid asset those catalogs (recursively) use, one group per source pack; entries the
//     catalogs no longer use are removed.
//  4. Protected_Icons: every texture under Assets/Secure/Icons/<category>/, as icon/<category>/<name>, labels
//     "icons" and "icons-<category>"; icons whose file is gone are removed.
//  5. Every Protected_* group: Remote paths, LZ4, no bundle cache, no CRC, hash-appended names, and the
//     EncryptedAssetBundleProvider. A "Data" group (plain) is the default group.

const string Profile = "Patch";
const string BuildPathVar = "Remote.BuildPath";
const string LoadPathVar = "Remote.LoadPath";
var log = new System.Text.StringBuilder();

var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.GetSettings(true);

// ---- 1. Profile and settings
var profiles = settings.profileSettings;
string profileId = profiles.GetProfileId(Profile);
if (string.IsNullOrEmpty(profileId))
{
    profileId = profiles.AddProfile(Profile, settings.activeProfileId);
    log.AppendLine("Created profile " + Profile);
}
profiles.SetValue(profileId, BuildPathVar, UOClient.Patching.PatchPaths.EditorBuildFolder + "/[BuildTarget]");
profiles.SetValue(profileId, LoadPathVar, "{UOClient.Patching.PatchPaths.ContentRoot}/[BuildTarget]");
settings.activeProfileId = profileId;
settings.BuildRemoteCatalog = true;
settings.RemoteCatalogBuildPath.SetVariableByName(settings, BuildPathVar);
settings.RemoteCatalogLoadPath.SetVariableByName(settings, LoadPathVar);
settings.OverridePlayerVersion = "content";
settings.UniqueBundleIds = false;
settings.DisableCatalogUpdateOnStartup = false;
settings.BuildAddressablesWithPlayerBuild = UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;

// ---- Groups
var providerField = typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema)
    .GetField("m_AssetBundleProviderType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
if (providerField == null) return "BundledAssetGroupSchema has no m_AssetBundleProviderType field in this Addressables version; set the provider by hand (Docs/SecureArt.md).";

System.Func<string, bool, UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema.BundlePackingMode, UnityEditor.AddressableAssets.Settings.AddressableAssetGroup> ensureGroup =
    (name, isProtected, packing) =>
    {
        var group = settings.FindGroup(name);
        if (group == null)
        {
            group = settings.CreateGroup(name, false, false, true, null,
                typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema),
                typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.ContentUpdateGroupSchema));
            log.AppendLine("Created group " + name);
        }

        var schema = group.GetSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema>()
                     ?? group.AddSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema>();
        schema.BuildPath.SetVariableByName(settings, BuildPathVar);
        schema.LoadPath.SetVariableByName(settings, LoadPathVar);
        schema.Compression = UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema.BundleCompressionMode.LZ4;
        schema.UseAssetBundleCache = false;
        schema.UseAssetBundleCrc = false;
        schema.UseAssetBundleCrcForCachedBundles = false;
        schema.BundleNaming = UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema.BundleNamingStyle.AppendHash;
        schema.BundleMode = packing;
        schema.IncludeInBuild = true;
        if (isProtected)
        {
            providerField.SetValue(schema, new UnityEngine.ResourceManagement.Util.SerializedType { Value = typeof(UOClient.Content.EncryptedAssetBundleProvider) });
        }

        UnityEditor.EditorUtility.SetDirty(schema);
        return group;
    };

var together = UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema.BundlePackingMode.PackTogether;
var byLabel = UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel;

var data = ensureGroup("Data", false, together);
settings.DefaultGroup = data;

// ---- 2. Catalogs
var catalogs = new System.Collections.Generic.Dictionary<string, string>
{
    { UOClient.Content.SecureArtAddresses.CreatureCatalog, "Assets/UOClient/Data/CreatureCatalog.asset" },
    { UOClient.Content.SecureArtAddresses.SidekickWardrobe, "Assets/UOClient/Data/SidekickWardrobe.asset" },
    { UOClient.Content.SecureArtAddresses.BoatCatalog, "Assets/UOClient/Data/BoatCatalog.asset" },
    { UOClient.Content.SecureArtAddresses.HouseKit, "Assets/UOClient/Game/HouseKit.asset" },
    { UOClient.Content.SecureArtAddresses.PropCatalog, "Assets/UOClient/Data/PropCatalog.asset" },
    { UOClient.Content.SecureArtAddresses.VegetationCatalog, "Assets/UOClient/Data/VegetationCatalog.asset" },
    { UOClient.Content.SecureArtAddresses.WallKit, "Assets/UOClient/Data/WallKit.asset" },
    { UOClient.Content.SecureArtAddresses.WorldContent, "Assets/UOClient/Data/WorldContent.asset" },
    { UOClient.Content.SecureArtAddresses.WorldPalette, "Assets/UOClient/Data/WorldPalette.asset" },
};

// The fixed human (model + animator) as the Player prefab holds them today.
var playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/UOClient/Game/Player.prefab");
var appearance = playerPrefab != null ? playerPrefab.GetComponent<UOGame.PlayerAppearance>() : null;
if (appearance != null)
{
    var so = new UnityEditor.SerializedObject(appearance);
    var model = so.FindProperty("_model")?.objectReferenceValue;
    var animator = so.FindProperty("_animator")?.objectReferenceValue;
    if (model != null) catalogs[UOClient.Content.SecureArtAddresses.HumanModel] = UnityEditor.AssetDatabase.GetAssetPath(model);
    if (animator != null) catalogs[UOClient.Content.SecureArtAddresses.HumanAnimator] = UnityEditor.AssetDatabase.GetAssetPath(animator);
}
// The ground-item pouch, as GroundItem.prefab holds it today.
var groundPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/UOClient/Game/GroundItem.prefab");
var groundItem = groundPrefab != null ? groundPrefab.GetComponent<UOGame.GroundItem>() : null;
if (groundItem != null)
{
    var pouch = new UnityEditor.SerializedObject(groundItem).FindProperty("_pouch")?.objectReferenceValue;
    if (pouch != null) catalogs[UOClient.Content.SecureArtAddresses.GroundPouch] = UnityEditor.AssetDatabase.GetAssetPath(pouch);
}

var catalogGroup = ensureGroup(UOClient.Content.SecureArtAddresses.CatalogGroup, true, together);

// After ClearDirectArtRefs the prefabs no longer hold them: keep what the entries already say.
foreach (var entry in catalogGroup.entries)
{
    if ((entry.address == UOClient.Content.SecureArtAddresses.HumanModel || entry.address == UOClient.Content.SecureArtAddresses.HumanAnimator
         || entry.address == UOClient.Content.SecureArtAddresses.GroundPouch)
        && !catalogs.ContainsKey(entry.address))
    {
        catalogs[entry.address] = entry.AssetPath;
    }
}
if (!catalogs.ContainsKey(UOClient.Content.SecureArtAddresses.HumanModel)) log.AppendLine("WARNING: Player.prefab has no PlayerAppearance._model; art/human/model not set.");
var explicitGuids = new System.Collections.Generic.HashSet<string>();
var roots = new System.Collections.Generic.List<string>();
foreach (var pair in catalogs)
{
    string guid = UnityEditor.AssetDatabase.AssetPathToGUID(pair.Value);
    if (string.IsNullOrEmpty(guid))
    {
        log.AppendLine("WARNING: missing " + pair.Value + " (" + pair.Key + ")");
        continue;
    }

    var entry = settings.CreateOrMoveEntry(guid, catalogGroup, false, false);
    entry.address = pair.Key;
    entry.SetLabel(UOClient.Content.SecureArtAddresses.PreloadLabel, true, true, false);
    explicitGuids.Add(guid);
    roots.Add(pair.Value);
}

// ---- 3. Pack groups from the catalogs' dependencies
// Which pack a path belongs to, or null when it isn't paid art. Mirrors ReleaseBuilder.ProtectedRoots.
System.Func<string, string> packOf = path =>
{
    string[] parts = path.Split('/');
    if (path.StartsWith("Assets/Synty/") && parts.Length > 3) return parts[2];
    if (path.StartsWith("Assets/Secure/") && parts.Length > 3 && parts[2] != "Icons") return parts[2];
    if (path.StartsWith("Assets/UOClient/Materials/SyntyWalls/")) return "SyntyWalls";
    return null;
};
System.Func<string, string> groupNameOf = pack =>
{
    var sb = new System.Text.StringBuilder(UOClient.Content.SecureArtAddresses.PackGroupPrefix);
    foreach (char c in pack) if (char.IsLetterOrDigit(c)) sb.Append(c);
    return sb.ToString();
};

var wanted = new System.Collections.Generic.Dictionary<string, string>(); // guid -> group name
foreach (string dep in UnityEditor.AssetDatabase.GetDependencies(roots.ToArray(), true))
{
    if (dep.EndsWith(".cs") || dep.EndsWith(".dll") || dep.EndsWith(".asmdef")) continue;
    string pack = packOf(dep);
    if (pack == null) continue;
    string guid = UnityEditor.AssetDatabase.AssetPathToGUID(dep);
    if (explicitGuids.Contains(guid)) continue;
    wanted[guid] = groupNameOf(pack);
}

var packGroups = new System.Collections.Generic.Dictionary<string, UnityEditor.AddressableAssets.Settings.AddressableAssetGroup>();
foreach (string name in new System.Collections.Generic.HashSet<string>(wanted.Values))
{
    packGroups[name] = ensureGroup(name, true, together);
}

int moved = 0;
foreach (var pair in wanted)
{
    var group = packGroups[pair.Value];
    var existing = settings.FindAssetEntry(pair.Key);
    if (existing != null && existing.parentGroup == group) continue;
    settings.CreateOrMoveEntry(pair.Key, group, false, false);
    moved++;
}

int removed = 0;
foreach (var group in new System.Collections.Generic.List<UnityEditor.AddressableAssets.Settings.AddressableAssetGroup>(settings.groups))
{
    if (group == null || !group.Name.StartsWith(UOClient.Content.SecureArtAddresses.PackGroupPrefix)) continue;
    foreach (var entry in new System.Collections.Generic.List<UnityEditor.AddressableAssets.Settings.AddressableAssetEntry>(group.entries))
    {
        if (!wanted.ContainsKey(entry.guid))
        {
            settings.RemoveAssetEntry(entry.guid, false);
            removed++;
        }
    }

    if (group.entries.Count == 0)
    {
        settings.RemoveGroup(group);
        log.AppendLine("Removed empty group " + group.Name);
    }
}

// ---- 4. Icons
int icons = 0, iconsRemoved = 0;
var iconGroup = ensureGroup(UOClient.Content.SecureArtAddresses.IconGroup, true, byLabel);
var iconGuids = new System.Collections.Generic.HashSet<string>();
if (UnityEditor.AssetDatabase.IsValidFolder(UOClient.Content.SecureArtAddresses.IconFolder))
{
    foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { UOClient.Content.SecureArtAddresses.IconFolder }))
    {
        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
        string category = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
        if (System.IO.Path.GetDirectoryName(path).Replace('\\', '/') == UOClient.Content.SecureArtAddresses.IconFolder)
        {
            log.AppendLine("WARNING: icon outside a category folder, skipped: " + path);
            continue;
        }

        var entry = settings.CreateOrMoveEntry(guid, iconGroup, false, false);
        entry.address = UOClient.Content.SecureArtAddresses.Icon(category, System.IO.Path.GetFileNameWithoutExtension(path));
        entry.SetLabel(UOClient.Content.SecureArtAddresses.IconLabel, true, true, false);
        entry.SetLabel(UOClient.Content.SecureArtAddresses.IconCategoryLabel(category), true, true, false);
        iconGuids.Add(guid);
        icons++;
    }
}
foreach (var entry in new System.Collections.Generic.List<UnityEditor.AddressableAssets.Settings.AddressableAssetEntry>(iconGroup.entries))
{
    if (!iconGuids.Contains(entry.guid))
    {
        settings.RemoveAssetEntry(entry.guid, false);
        iconsRemoved++;
    }
}

settings.SetDirty(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
UnityEditor.AssetDatabase.SaveAssets();

log.AppendLine($"Catalogs: {explicitGuids.Count} in {UOClient.Content.SecureArtAddresses.CatalogGroup}");
log.AppendLine($"Pack groups: {packGroups.Count} ({string.Join(", ", packGroups.Keys)}); {wanted.Count} paid assets, {moved} added/moved, {removed} removed");
log.AppendLine($"Icons: {icons} in {UOClient.Content.SecureArtAddresses.IconGroup}, {iconsRemoved} removed");
log.AppendLine("Active profile: " + Profile);
return log.ToString();
