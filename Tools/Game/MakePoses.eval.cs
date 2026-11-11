// Held poses for the humanoid animator, made from muscle values (no art pack needed), saved to Resources/Poses and
// played by PlayerAppearance through a playable while a player holds the pose:
//   Sit  - sitting on the deck (or ground): legs out in front, knees a little bent, hands resting on the thighs.
// Run once on the open project; edit the numbers and run again to adjust (it overwrites only these clips).
string dir = "Assets/UOClient/Resources/Poses";
System.IO.Directory.CreateDirectory(dir);

UnityEngine.AnimationClip Pose(string name, System.Collections.Generic.Dictionary<string, float> values)
{
    var clip = new UnityEngine.AnimationClip { name = name, frameRate = 30 };
    foreach (var kv in values)
    {
        var binding = UnityEditor.EditorCurveBinding.FloatCurve("", typeof(UnityEngine.Animator), kv.Key);
        UnityEditor.AnimationUtility.SetEditorCurve(clip, binding, UnityEngine.AnimationCurve.Constant(0f, 1f, kv.Value));
    }

    var settings = UnityEditor.AnimationUtility.GetAnimationClipSettings(clip);
    settings.loopTime = true;
    UnityEditor.AnimationUtility.SetAnimationClipSettings(clip, settings);
    string path = $"{dir}/{name}.anim";
    UnityEditor.AssetDatabase.DeleteAsset(path);
    UnityEditor.AssetDatabase.CreateAsset(clip, path);
    return clip;
}

var sit = new System.Collections.Generic.Dictionary<string, float>
{
    ["RootT.x"] = 0f, ["RootT.y"] = 0.42f, ["RootT.z"] = 0f,
    ["RootQ.x"] = 0f, ["RootQ.y"] = 0f, ["RootQ.z"] = 0f, ["RootQ.w"] = 1f,
    ["Spine Front-Back"] = 0.15f, ["Chest Front-Back"] = 0.05f,
    ["Left Upper Leg Front-Back"] = 0.95f, ["Right Upper Leg Front-Back"] = 0.95f,
    ["Left Upper Leg In-Out"] = 0.1f, ["Right Upper Leg In-Out"] = 0.1f,
    ["Left Lower Leg Stretch"] = 0.55f, ["Right Lower Leg Stretch"] = 0.55f,
    ["Left Foot Up-Down"] = 0.2f, ["Right Foot Up-Down"] = 0.2f,
    ["Left Arm Down-Up"] = -0.55f, ["Right Arm Down-Up"] = -0.55f,
    ["Left Arm Front-Back"] = 0.35f, ["Right Arm Front-Back"] = 0.35f,
    ["Left Forearm Stretch"] = 0.55f, ["Right Forearm Stretch"] = 0.55f,
    ["Left Shoulder Down-Up"] = 0f, ["Right Shoulder Down-Up"] = 0f,
};

Pose("Sit", sit);
UnityEditor.AssetDatabase.SaveAssets();
return "poses saved in " + dir;
