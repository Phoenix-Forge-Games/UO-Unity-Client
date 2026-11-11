// Builds or refreshes Assets/UOClient/Data/CreatureCatalog.asset and the creature animator controllers. Run it with
// the Unity CLI `script eval --code`. Entries are matched by Name, so running it again updates entries in place.
// Packs that aren't imported yet are skipped. Mapping source: Docs/CreatureMapping.md.
const string Data = "Assets/UOClient/Data";
const string Ctrl = "Assets/UOClient/Data/Creatures";
System.IO.Directory.CreateDirectory(Ctrl);
var log = new System.Text.StringBuilder();

UnityEngine.AnimationClip Clip(string fbx)
{
    foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(fbx))
        if (o is UnityEngine.AnimationClip c && !c.name.StartsWith("__preview__")) return c;
    return null;
}

void Loop(string fbx)
{
    var imp = UnityEditor.AssetImporter.GetAtPath(fbx) as UnityEditor.ModelImporter;
    if (imp == null) return;
    var clips = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
    bool changed = false;
    foreach (var c in clips) if (!c.loopTime) { c.loopTime = true; changed = true; }
    if (changed) { imp.clipAnimations = clips; imp.SaveAndReimport(); }
}

// A locomotion controller: one blend tree on Speed (0 idle, 1 walk, 2 run), the parameter MobileView drives.
UnityEditor.Animations.AnimatorController Locomotion(string name, string idle, string walk, string run)
{
    foreach (var f in new[] { idle, walk, run }) Loop(f);
    UnityEngine.AnimationClip i = Clip(idle), w = Clip(walk), r = Clip(run);
    if (i == null || w == null || r == null) { log.AppendLine("missing clips for " + name); return null; }
    string path = Ctrl + "/" + name + ".controller";
    UnityEditor.AssetDatabase.DeleteAsset(path);
    var ac = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);
    ac.AddParameter("Speed", UnityEngine.AnimatorControllerParameterType.Float);
    var tree = new UnityEditor.Animations.BlendTree { name = "Locomotion", blendParameter = "Speed", useAutomaticThresholds = false };
    UnityEditor.AssetDatabase.AddObjectToAsset(tree, ac);
    tree.AddChild(i, 0f);
    tree.AddChild(w, 1f);
    tree.AddChild(r, 2f);
    var sm = ac.layers[0].stateMachine;
    var state = sm.AddState("Locomotion");
    state.motion = tree;
    sm.defaultState = state;
    return ac;
}

UnityEngine.GameObject P(string path)
{
    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
    if (go == null) log.AppendLine("not imported: " + path);
    return go;
}

string catalogPath = Data + "/CreatureCatalog.asset";
var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.CreatureCatalog>(catalogPath);
if (catalog == null)
{
    catalog = UnityEngine.ScriptableObject.CreateInstance<UOClient.CreatureCatalog>();
    UnityEditor.AssetDatabase.CreateAsset(catalog, catalogPath);
}

var entries = new System.Collections.Generic.List<UOClient.CreatureCatalog.Entry>(catalog.Entries ?? new UOClient.CreatureCatalog.Entry[0]);
void Add(string name, ushort[] bodies, UnityEngine.GameObject prefab, float height, UnityEngine.Color tint, UnityEngine.RuntimeAnimatorController animator, params string[] hide)
{
    if (prefab == null) return;
    entries.RemoveAll(e => e.Name == name);
    entries.Add(new UOClient.CreatureCatalog.Entry { Name = name, Bodies = bodies, Prefab = prefab, Height = height, Tint = tint, Animator = animator, Hide = hide });
}

UnityEngine.Color C(float r, float g, float b) => new UnityEngine.Color(r, g, b, 1f);
var white = UnityEngine.Color.white;

// --- POLYGON Dog Pack ---
const string Dog = "Assets/Synty/PolygonDog";
if (UnityEditor.AssetDatabase.IsValidFolder(Dog))
{
    var dogAnim = Locomotion("DogLocomotion",
        Dog + "/Animations/Locomotion/_POLYGON_Dog_Locomotion_Standing.fbx",
        Dog + "/Animations/Locomotion/_POLYGON_Dog_Locomotion_Walking.fbx",
        Dog + "/Animations/Locomotion/_POLYGON_Dog_Locomotion_Running.fbx");
    string D(string n) => Dog + "/Prefabs/Dogs/SK_Dog_" + n + "_01.prefab";
    Add("timber wolves", new ushort[] { 225 }, P(D("Wolf")), 0.95f, C(0.85f, 0.78f, 0.68f), dogAnim);
    Add("grey wolves", new ushort[] { 25 }, P(D("Wolf")), 0.9f, white, dogAnim);
    Add("dire wolves", new ushort[] { 23 }, P(D("Wolf")), 1.15f, C(0.5f, 0.45f, 0.45f), dogAnim);
    Add("white wolves", new ushort[] { 34 }, P(D("Husky")), 0.95f, white, dogAnim);
    Add("tsuki wolves", new ushort[] { 250 }, P(D("Wolf")), 1.1f, C(0.55f, 0.55f, 0.75f), dogAnim);
    Add("dogs", new ushort[] { 217 }, P(D("Labrador")), 0.75f, white, dogAnim);
    Add("hellhounds", new ushort[] { 98 }, P(D("Hellhound")), 1.1f, white, dogAnim);
}

// --- Humanoid-rigged POLYGON creatures: they share the humanoids' locomotion controller (Mecanim retargets it). ---
var humanoid = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.RuntimeAnimatorController>(
    UnityEditor.AssetDatabase.GUIDToAssetPath("a560562956f76674faee24a4ab460578"));
const string S = "Assets/Synty/";
UnityEngine.GameObject Q(string pack, string name) => P(S + pack + "/Prefabs/Characters/" + name + ".prefab");
void H(string name, ushort[] bodies, UnityEngine.GameObject prefab, float height, UnityEngine.Color tint, params string[] hide) => Add(name, bodies, prefab, height, tint, humanoid, hide);
ushort[] B(params int[] b) => System.Array.ConvertAll(b, v => (ushort)v);

// Orcs and ogres (Fantasy Rivals, Dungeon goblins)
H("orcs", B(17), Q("PolygonFantasyRivals", "SM_Chr_BR_BigOrk_01"), 1.95f, white);
H("orc captains", B(7), Q("PolygonFantasyRivals", "SM_Chr_BR_BigOrk_01"), 2.05f, C(0.85f, 0.8f, 0.8f));
H("orcish lords", B(138), Q("PolygonFantasyRivals", "SM_Chr_BR_BigOrk_01"), 2.15f, C(0.75f, 0.7f, 0.7f));
H("orc brutes", B(189), Q("PolygonFantasyRivals", "SM_Chr_BR_BigOrk_01"), 2.6f, C(0.7f, 0.6f, 0.55f));
H("orcish mages", B(140), Q("PolygonDungeon", "SM_Chr_Goblin_Shaman_01"), 1.8f, white);
H("orc scouts and bombers", B(181, 182), Q("PolygonDungeon", "SM_Chr_Goblin_Warrior_Male_01"), 1.7f, white);
H("goblins", B(723), Q("PolygonDungeon", "SM_Chr_Goblin_Male_01"), 1.35f, white);
H("ogres", B(1), Q("PolygonFantasyRivals", "SM_Chr_BR_BigOrk_01"), 2.9f, C(0.8f, 0.75f, 0.6f));
H("ogre lords", B(83), Q("PolygonFantasyRivals", "SM_Chr_BR_BigOrk_01"), 3.2f, C(0.7f, 0.6f, 0.5f));
H("arctic ogre lords", B(135), Q("PolygonFantasyRivals", "SM_Chr_BR_BigOrk_01"), 3.2f, C(0.75f, 0.85f, 1f));
H("ettins", B(18), Q("PolygonFantasyRivals", "SM_Chr_BR_BarbarianGiant_01"), 3.0f, white);
H("cyclopes", B(75), Q("PolygonDarkFortress", "SM_Chr_Behemoth_01"), 3.4f, white);
H("titans", B(76), Q("PolygonDarkFortress", "SM_Chr_Colossus_01"), 3.4f, white);
// Trolls
H("trolls", B(53, 54), Q("PolygonFantasyRivals", "SM_Chr_BR_Troll_01"), 2.5f, white);
H("frost trolls", B(55), Q("PolygonGoblinWarCamp", "SM_Chr_Troll_01"), 2.6f, C(0.75f, 0.85f, 1f));
// Daemons
H("daemons", B(9), Q("PolygonFantasyRivals", "SM_Chr_BR_RedDemon_01"), 2.8f, white);
H("balrons", B(40), Q("PolygonDungeonRealms", "Chr_BR_Demon_01"), 3.6f, white);
H("chaos daemons", B(792), Q("PolygonDarkFantasy", "SM_Chr_Demon_01"), 2.4f, white);
H("imps", B(74), Q("PolygonDarkFantasy", "SM_Chr_Demon_Male_01"), 1.1f, white);
H("succubi", B(149), Q("PolygonFantasyRivals", "SM_Chr_SpiritDemon_01"), 2.2f, white);
H("ice fiends", B(43), Q("PolygonKaiju", "SM_Chr_Kaiju_01"), 3.0f, white);
H("molochs", B(785), Q("PolygonKaiju", "SM_Chr_Kaiju_02"), 3.0f, white);
// Elementals and golems
H("earth elementals", B(14), Q("PolygonDungeon", "SM_Chr_Rock_Golem_01"), 2.3f, white);
H("golems", B(752), Q("PolygonFantasyRivals", "SM_Chr_BR_MechanicalGolem_01"), 2.4f, white);
var elemental = Q("PolygonFantasyRivals", "SM_Chr_BR_ElementalGolem_01");
H("water elementals", B(16), elemental, 2.4f, C(0.45f, 0.65f, 1f));
H("air elementals", B(13), elemental, 2.4f, C(0.85f, 0.92f, 1f));
H("fire elementals", B(15), elemental, 2.4f, C(1f, 0.55f, 0.3f));
H("poison elementals", B(158, 162), elemental, 2.4f, C(0.55f, 0.9f, 0.4f));
H("blood elementals", B(159), elemental, 2.4f, C(0.85f, 0.25f, 0.25f));
H("ice and snow elementals", B(161, 163), elemental, 2.4f, C(0.9f, 0.95f, 1f));
H("crystal elementals", B(300), elemental, 2.4f, C(0.8f, 0.6f, 1f));
var fort = Q("PolygonFantasyRivals", "SM_Chr_BR_FortGolem_01");
H("dull copper elementals", B(110), fort, 2.3f, C(0.8f, 0.6f, 0.5f));
H("copper elementals", B(109), fort, 2.3f, C(0.9f, 0.55f, 0.35f));
H("bronze elementals", B(108), fort, 2.3f, C(0.85f, 0.6f, 0.3f));
H("shadow iron elementals", B(111), fort, 2.3f, C(0.45f, 0.45f, 0.5f));
H("gold elementals", B(166), fort, 2.3f, C(1f, 0.85f, 0.35f));
H("agapite elementals", B(107), fort, 2.3f, C(0.85f, 0.45f, 0.45f));
H("verite elementals", B(113), fort, 2.3f, C(0.5f, 0.8f, 0.5f));
H("valorite elementals", B(112), fort, 2.3f, C(0.45f, 0.6f, 1f));
// Gargoyles
H("gargoyles", B(4), Q("PolygonDarkFantasy", "SM_Chr_Gargoyle_01"), 2.2f, white);
H("stone gargoyles", B(67), Q("PolygonDarkFantasy", "SM_Chr_Gargoyle_01"), 2.6f, C(0.7f, 0.7f, 0.7f));
H("fire gargoyles", B(130), Q("PolygonDarkFantasy", "SM_Chr_Gargoyle_01"), 2.3f, C(1f, 0.6f, 0.4f));
// Undead
H("skeletons", B(50), Q("PolygonDarkFantasy", "SM_Chr_Skeleton_01"), 1.8f, white);
H("skeletal knights", B(147), Q("PolygonDungeon", "SM_Chr_Skeleton_Knight_01"), 2.0f, white);
H("bone knights", B(57), Q("PolygonDarkFantasy", "SM_Chr_Skeleton_HeavyArmor_01"), 2.0f, white);
H("skeletal mages", B(148), Q("PolygonDungeonRealms", "Chr_Skeleton_02"), 1.8f, white);
H("patchwork skeletons", B(309), Q("PolygonDarkFantasy", "SM_Chr_Skeleton_Flesh_01"), 1.9f, white);
H("zombies", B(3), Q("PolygonDarkFortress", "SM_Chr_Undead_01"), 1.8f, white);
H("ghouls", B(153), Q("PolygonDarkFortress", "SM_Chr_Undead_02"), 1.8f, white);
H("rotting corpses", B(155), Q("PolygonDarkFortress", "SM_Chr_Corpse_01"), 1.9f, white);
H("spectres and wraiths", B(26), Q("PolygonDarkFortress", "SM_Chr_Wraith_01"), 2.0f, white);
H("headless ones", B(31), Q("PolygonDungeon", "SM_Chr_Tormented_Soul_01"), 1.6f, white);
H("restless souls", B(970), Q("PolygonDungeon", "SM_Chr_Ghost_01"), 1.8f, white);
H("spectral armour", B(637), Q("PolygonDungeonRealms", "Chr_Undead_Knight_01"), 2.0f, white);
H("liches", B(24), Q("PolygonDarkFortress", "SM_Chr_Necromancer_01"), 1.9f, C(0.8f, 0.85f, 0.8f));
H("lich lords", B(79), Q("PolygonDarkFortress", "SM_Chr_Plague_Lord_01"), 2.2f, white);
H("ancient liches", B(78), Q("PolygonDarkFortress", "SM_Chr_Plague_Lord_01"), 2.6f, C(0.7f, 0.75f, 0.9f));
H("evil mages", B(124, 125), Q("PolygonDarkFortress", "SM_Chr_Necromancer_01"), 1.8f, white);
H("mummies", B(154), Q("PolygonAncientEgypt", "SM_Chr_Mummy_01"), 1.9f, white);
H("flesh golems", B(304), P(S + "PolygonBossZombies/Prefabs/SM_Chr_ZombieBoss_Brute_01.prefab"), 2.4f, white);
H("plague beasts", B(775), P(S + "PolygonBossZombies/Prefabs/SM_Chr_ZombieBoss_Blobber_01.prefab"), 2.4f, white);
H("boglings", B(779), P(S + "PolygonBossZombies/Prefabs/SM_Chr_ZombieBoss_Wretch_01.prefab"), 1.5f, white);
H("bog things", B(780), P(S + "PolygonBossZombies/Prefabs/SM_Chr_ZombieBoss_Slobber_01.prefab"), 2.6f, white);
H("devourers", B(303), Q("PolygonDarkFortress", "SM_Chr_Plague_01"), 2.6f, white);
// Others
H("reapers", B(47), Q("PolygonFantasyRivals", "SM_Chr_ForestGuardian_01"), 3.0f, white);
H("lizardmen", B(35), Q("PolygonKaiju", "SM_Chr_Kaiju_03"), 1.9f, white);
H("gorillas", B(29), Q("PolygonKaiju", "SM_Chr_Kaiju_04"), 1.6f, white, "SM_Chr_Kaiju_04_Tail_01");
H("savages", B(184), Q("PolygonGoblinWarCamp", "SM_Chr_Warrior_Male_01"), 1.8f, white);
H("savage shamans", B(186), Q("PolygonGoblinWarCamp", "SM_Chr_Shaman_01"), 1.8f, white);

catalog.Entries = entries.ToArray();
UnityEditor.EditorUtility.SetDirty(catalog);
UnityEditor.AssetDatabase.SaveAssets();
log.Insert(0, catalog.Entries.Length + " entries\n");
return log.ToString();
