// Fills Assets/UOClient/Animation/AC_UOHumanoid.controller in place (players, humanoid NPCs and creatures share it, by
// GUID), with the actions ActionAnimator fires. Run with the Unity CLI `script eval --file`; running it again rebuilds
// the same controller.
//   Base layer: Locomotion on Speed in metres a second, each Synty clip at the ground speed it shows (its root motion:
//   idle 0, walk 1.54, run 2.74, sprint 7.69), so the feet keep up with the ground at any pace; Swim (Swimming), Die.
//   Action layer (upper body): Attack by Weapon and Variant, Hit (Variant), and casting (Casting, CastRelease, Fizzle).
// Clips: Kevin Iglesias Human Animations (male), ExplosiveLLC for maces and daggers. Assets/Secure (paid, not in git).
string KI = "Assets/Secure/Kevin Iglesias/Human Animations/Animations/Male/";
string EX = "Assets/Secure/ExplosiveLLC/RPG Character Mecanim Animation Pack/Animations/";
string mask = "Assets/Secure/Kevin Iglesias/Human Animations/Models/Avatar Masks/Human Body Upper Mask.mask";
string path = UnityEditor.AssetDatabase.GUIDToAssetPath("a560562956f76674faee24a4ab460578");
var ac = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
var log = new System.Text.StringBuilder();

UnityEngine.AnimationClip Clip(string fbx)
{
    foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(fbx))
        if (o is UnityEngine.AnimationClip c && !c.name.StartsWith("__preview__")) return c;
    log.AppendLine("missing " + fbx);
    return null;
}

// The locomotion clips it has now (Synty's, made for the Sidekick rig), kept.
var sm = ac.layers[0].stateMachine;
UnityEngine.Motion idle = null, walk = null, run = null;
foreach (var s in sm.states)
{
    if (s.state.name == "Idle") idle = s.state.motion;
    if (s.state.name == "Walk") walk = s.state.motion;
    if (s.state.name == "Run") run = s.state.motion;
    if (s.state.motion is UnityEditor.Animations.BlendTree bt && s.state.name == "Locomotion")
    {
        idle = bt.children[0].motion; walk = bt.children[1].motion; run = bt.children[2].motion;
    }
}

if (idle == null || walk == null || run == null)
{
    return "couldn't find the locomotion clips in " + path;
}

// The ground speed each clip shows, from its root-motion twin (A_MOD_BL_*_RM_Masc: RootT.z over a cycle).
const float WalkPace = 1.54f, RunPace = 2.74f, SprintPace = 7.69f;
var sprint = Clip("Assets/Synty/AnimationBaseLocomotion/Animations/Sidekick/Masculine/Locomotion/Sprint/A_MOD_BL_Sprint_F_Masc.fbx");

// Clear it out (layers, states, parameters, old blend trees), keeping the asset and its GUID.
while (ac.layers.Length > 1) ac.RemoveLayer(ac.layers.Length - 1);
foreach (var s in sm.states) sm.RemoveState(s.state);
foreach (var t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);
foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
    if (o is UnityEditor.Animations.BlendTree) UnityEngine.Object.DestroyImmediate(o, true);
ac.parameters = new UnityEngine.AnimatorControllerParameter[0];

void P(string name, UnityEngine.AnimatorControllerParameterType type, int def = 0) =>
    ac.AddParameter(new UnityEngine.AnimatorControllerParameter { name = name, type = type, defaultInt = def });
P("Speed", UnityEngine.AnimatorControllerParameterType.Float);
P("Swimming", UnityEngine.AnimatorControllerParameterType.Bool);
P("InCombat", UnityEngine.AnimatorControllerParameterType.Bool);
P("Work", UnityEngine.AnimatorControllerParameterType.Int);
P("Casting", UnityEngine.AnimatorControllerParameterType.Bool);
P("Attack", UnityEngine.AnimatorControllerParameterType.Trigger);
P("Hit", UnityEngine.AnimatorControllerParameterType.Trigger);
P("Die", UnityEngine.AnimatorControllerParameterType.Trigger);
P("CastRelease", UnityEngine.AnimatorControllerParameterType.Trigger);
P("Fizzle", UnityEngine.AnimatorControllerParameterType.Trigger);
P("Variant", UnityEngine.AnimatorControllerParameterType.Int);
P("Weapon", UnityEngine.AnimatorControllerParameterType.Int);
P("AttackCount", UnityEngine.AnimatorControllerParameterType.Int, 3);
P("HitCount", UnityEngine.AnimatorControllerParameterType.Int, 3);

UnityEditor.Animations.BlendTree Tree(string name, params (UnityEngine.Motion Motion, float At)[] children)
{
    var tree = new UnityEditor.Animations.BlendTree { name = name, blendParameter = "Speed", useAutomaticThresholds = false, hideFlags = UnityEngine.HideFlags.HideInHierarchy };
    UnityEditor.AssetDatabase.AddObjectToAsset(tree, ac);
    foreach (var (motion, at) in children)
    {
        if (motion != null) tree.AddChild(motion, at);
    }

    return tree;
}

UnityEditor.Animations.BlendTree Moving(string name, UnityEngine.Motion standing) =>
    Tree(name, (standing, 0f), (walk, WalkPace), (run, RunPace), (sprint, SprintPace));

// ---- Base layer: moving, swimming, dying.
var loco = sm.AddState("Locomotion", new UnityEngine.Vector3(300, 0));
loco.motion = Moving("Locomotion", idle);
sm.defaultState = loco;

var swimIdle = Clip(KI + "Movement/Swim/HumanM@SwimIdle01.fbx");
var swimFwd = Clip(KI + "Movement/Swim/SwimMovement/HumanM@Swim01_Forward.fbx");
var swim = sm.AddState("Swim", new UnityEngine.Vector3(300, 120));
swim.motion = Tree("Swim", (swimIdle, 0f), (swimFwd, 1.5f));
var toSwim = loco.AddTransition(swim);
toSwim.hasExitTime = false; toSwim.duration = 0.3f;
toSwim.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, "Swimming");
var fromSwim = swim.AddTransition(loco);
fromSwim.hasExitTime = false; fromSwim.duration = 0.3f;
fromSwim.AddCondition(UnityEditor.Animations.AnimatorConditionMode.IfNot, 0, "Swimming");

// In combat (InCombat, from the server's Combatant.InCombat): the fighting stance for what you hold, moving as usual.
// One state per stance; Weapon (ActionAnimator.WeaponKind) picks it: unarmed, one-handed (sword, mace, fencing),
// two-handed, polearm, ranged (bow, crossbow).
string[] stances =
{
    KI + "Combat/HumanM@CombatIdle01.fbx", KI + "Combat/1H/HumanM@CombatIdle1H01.fbx", KI + "Combat/2H/HumanM@CombatIdle2H01.fbx",
    KI + "Combat/Polearm/HumanM@CombatIdlePolearm01.fbx", KI + "Combat/Bow/HumanM@BowIdle01.fbx",
};
int[][] stanceWeapons = { new[] { 0 }, new[] { 1, 2, 3 }, new[] { 4 }, new[] { 5 }, new[] { 6, 7 } };
for (int k = 0; k < stances.Length; k++)
{
    var stance = sm.AddState("Combat" + k, new UnityEngine.Vector3(0, -120 - k * 60));
    stance.motion = Moving("Combat" + k, Clip(stances[k]));
    foreach (int w in stanceWeapons[k])
    {
        var t = loco.AddTransition(stance);
        t.hasExitTime = false; t.duration = 0.25f;
        t.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, "InCombat");
        t.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Equals, w, "Weapon");
    }

    var leave = stance.AddTransition(loco);
    leave.hasExitTime = false; leave.duration = 0.4f;
    leave.AddCondition(UnityEditor.Animations.AnimatorConditionMode.IfNot, 0, "InCombat");
    // Changing weapons mid-fight: back through Locomotion into the right stance.
    foreach (int w in new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
    {
        if (System.Array.IndexOf(stanceWeapons[k], w) >= 0) continue;
        var swap = stance.AddTransition(loco);
        swap.hasExitTime = false; swap.duration = 0.2f;
        swap.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Equals, w, "Weapon");
    }

    var swimFromStance = stance.AddTransition(swim);
    swimFromStance.hasExitTime = false; swimFromStance.duration = 0.3f;
    swimFromStance.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, "Swimming");
}

// At work (Work, from PlayerIdentity.Pose): 1 mining, 2 chopping, 3 fishing, 4 smithing at an anvil, 5 at a bench
// (tinkering, carpentry, tailoring...), 6 stirring a pot (alchemy, cooking). Each loops until Work is 0 again.
string WH = "Assets/Secure/Witch_Hag_Animations/Art/Animations/";
string[] works =
{
    KI + "Work/Mining/HumanM@Mining01 - Loop Ground.fbx", KI + "Work/Chopping/HumanM@TreeChopping01 - Loop.fbx",
    KI + "Work/Fishing/HumanM@Fishing01 - Loop.fbx", KI + "Work/Hammering/HumanM@HammeringAnvil01_R - Loop.fbx",
    KI + "Work/Hammering/HumanM@HammeringTable01_R - Loop.fbx", WH + "Cauldron/WitchHag_Interact_CauldronMix_Loop.fbx",
};
for (int k = 0; k < works.Length; k++)
{
    var job = sm.AddState("Work" + (k + 1), new UnityEngine.Vector3(-300, k * 60));
    job.motion = Clip(works[k]);
    var t = loco.AddTransition(job);
    t.hasExitTime = false; t.duration = 0.25f;
    t.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Equals, k + 1, "Work");
    var done = job.AddTransition(loco);
    done.hasExitTime = false; done.duration = 0.3f;
    done.AddCondition(UnityEditor.Animations.AnimatorConditionMode.NotEqual, k + 1, "Work");
}

string[] deaths = { KI + "Combat/HumanM@Death01.fbx", KI + "Combat/HumanM@Death02.fbx", KI + "Combat/HumanM@CombatDeath01.fbx" };
for (int k = 0; k < deaths.Length; k++)
{
    var d = sm.AddState("Die" + (k + 1), new UnityEngine.Vector3(600, k * 60));
    d.motion = Clip(deaths[k]);
    var t = sm.AddAnyStateTransition(d);
    t.canTransitionToSelf = false; t.duration = 0.15f;
    t.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, "Die");
    t.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Equals, k, "Variant");
}

// ---- Action layer: upper body only, so swings and casts play while walking.
ac.AddLayer("Action");
var layers = ac.layers;
layers[1].defaultWeight = 0f; // off until an action plays (ActionAnimator.Drive raises it): models nobody drives (the menu's) stand normally
layers[1].blendingMode = UnityEditor.Animations.AnimatorLayerBlendingMode.Override;
layers[1].avatarMask = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AvatarMask>(mask);
if (layers[1].avatarMask == null) log.AppendLine("missing mask " + mask);
ac.layers = layers;
var am = ac.layers[1].stateMachine;
var none = am.AddState("None", new UnityEngine.Vector3(300, 0));
none.writeDefaultValues = false;
am.defaultState = none;

void Back(UnityEditor.Animations.AnimatorState s, float exit = 0.9f)
{
    var t = s.AddTransition(none);
    t.hasExitTime = true; t.exitTime = exit; t.duration = 0.2f;
}

UnityEditor.Animations.AnimatorState Act(string name, UnityEngine.Motion clip, UnityEngine.Vector3 at)
{
    var s = am.AddState(name, at);
    s.motion = clip;
    s.writeDefaultValues = false;
    return s;
}

// Attacks: three per weapon group (ActionAnimator.WeaponKind order).
string[][] attacks =
{
    new[] { KI + "Combat/Unarmed/HumanM@AttackPunch01_R.fbx", KI + "Combat/Unarmed/HumanM@AttackPunch02_L.fbx", KI + "Combat/Unarmed/HumanM@AttackPunch03_R.fbx" },
    new[] { KI + "Combat/1H/HumanM@Attack1H01_R.fbx", KI + "Combat/1H/HumanM@Attack1H02_R.fbx", KI + "Combat/1H/HumanM@Attack1H03_R.fbx" },
    new[] { EX + "1Hand-Mace/RPG-Character@Mace-Attack-R1.FBX", EX + "1Hand-Mace/RPG-Character@Mace-Attack-R2.FBX", EX + "1Hand-Mace/RPG-Character@Mace-Attack-R3.FBX" },
    new[] { EX + "1Hand-Dagger/RPG-Character@Dagger-Attack-R1.FBX", EX + "1Hand-Dagger/RPG-Character@Dagger-Attack-R2.FBX", EX + "1Hand-Dagger/RPG-Character@Dagger-Attack-R3.FBX" },
    new[] { KI + "Combat/2H/HumanM@Attack2H01.fbx", KI + "Combat/2H/HumanM@Attack2H02.fbx", KI + "Combat/2H/HumanM@Attack2H03.fbx" },
    new[] { KI + "Combat/Polearm/HumanM@AttackPolearm01.fbx", KI + "Combat/Polearm/HumanM@AttackPolearm02.fbx", KI + "Combat/Polearm/HumanM@AttackPolearm03.fbx" },
    new[] { KI + "Combat/Bow/HumanM@BowShot01 - Release.fbx", KI + "Combat/Bow/HumanM@BowShot01_Up - Release.fbx", KI + "Combat/Bow/HumanM@BowShot01_Down - Release.fbx" },
    new[] { KI + "Combat/Bow/HumanM@BowShot01 - Release.fbx", KI + "Combat/Bow/HumanM@BowShot01_Up - Release.fbx", KI + "Combat/Bow/HumanM@BowShot01_Down - Release.fbx" }, // crossbow: the bow's shot for now
};
for (int w = 0; w < attacks.Length; w++)
{
    for (int v = 0; v < attacks[w].Length; v++)
    {
        var s = Act($"Attack{w}_{v}", Clip(attacks[w][v]), new UnityEngine.Vector3(600 + v * 220, w * 50));
        var t = am.AddAnyStateTransition(s);
        t.canTransitionToSelf = false; t.duration = 0.08f;
        t.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, "Attack");
        t.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Equals, w, "Weapon");
        t.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Equals, v, "Variant");
        Back(s);
    }
}

string[] hits = { KI + "Combat/HumanM@Damage01.fbx", KI + "Combat/HumanM@Damage02.fbx", KI + "Combat/HumanM@Damage03.fbx" };
for (int v = 0; v < hits.Length; v++)
{
    var s = Act("Hit" + v, Clip(hits[v]), new UnityEngine.Vector3(0, 200 + v * 50));
    var t = am.AddAnyStateTransition(s);
    t.canTransitionToSelf = false; t.duration = 0.08f;
    t.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, "Hit");
    t.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Equals, v, "Variant");
    Back(s, 0.85f);
}

// Casting: charging while the spell is spoken, then the release, or a fizzle.
string magic = KI + "Combat/Spellcasting/MagicAttacks/Directional/HumanM@MagicAttackDirect1H01_R";
var charge = Act("CastCharge", Clip(magic + " - Load.fbx"), new UnityEngine.Vector3(0, -150));
var release = Act("CastRelease", Clip(magic + " - Cast.fbx"), new UnityEngine.Vector3(250, -150));
var fizzle = Act("CastFizzle", Clip(KI + "Combat/Spellcasting/HumanM@CastingExit01.fbx"), new UnityEngine.Vector3(250, -220));
var toCharge = none.AddTransition(charge);
toCharge.hasExitTime = false; toCharge.duration = 0.15f;
toCharge.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, "Casting");
var toRelease = charge.AddTransition(release);
toRelease.hasExitTime = false; toRelease.duration = 0.08f;
toRelease.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, "CastRelease");
var toFizzle = charge.AddTransition(fizzle);
toFizzle.hasExitTime = false; toFizzle.duration = 0.1f;
toFizzle.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, "Fizzle");
var stopCharge = charge.AddTransition(none);
stopCharge.hasExitTime = false; stopCharge.duration = 0.2f;
stopCharge.AddCondition(UnityEditor.Animations.AnimatorConditionMode.IfNot, 0, "Casting");
Back(release);
Back(fizzle);

UnityEditor.EditorUtility.SetDirty(ac);
UnityEditor.AssetDatabase.SaveAssets();
return $"{path}: {ac.parameters.Length} parameters, {sm.states.Length} base states, {am.states.Length} action states" + (log.Length > 0 ? "\n" + log : "");
