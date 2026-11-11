// Builds Assets/UOClient/VFX/Grass.vfx by reflection over VFX Graph's internal model (see UnityClient/Docs/VFX_Authoring.md).
// Exposed: ColorMask (rgb ground colour, a grass weight), NormalHeight (xyz normal, w height), ChunkSize (x, y, z metres).
var BF = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy;
var log = new System.Text.StringBuilder();
System.Type T(string full)
{
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
    {
        var t = a.GetType(full, false);
        if (t != null) return t;
    }
    throw new System.Exception("type not found " + full);
}
object Call(object target, string name, params object[] args)
{
    var type = target as System.Type ?? target.GetType();
    foreach (var m in type.GetMethods(BF))
    {
        if (m.Name != name || m.GetParameters().Length != args.Length) continue;
        return m.Invoke(target is System.Type ? null : target, args);
    }
    throw new System.Exception("method not found " + type.Name + "." + name + "/" + args.Length);
}
object Get(object target, string name)
{
    var t = target.GetType();
    // The most-derived declaration wins (several VFX types redeclare `children` and friends).
    for (var k = t; k != null; k = k.BaseType)
    {
        var dp = k.GetProperty(name, BF | System.Reflection.BindingFlags.DeclaredOnly);
        if (dp != null) return dp.GetValue(target);
        var df = k.GetField(name, BF | System.Reflection.BindingFlags.DeclaredOnly);
        if (df != null) return df.GetValue(target);
    }
    var p = (System.Reflection.PropertyInfo)null;
    var f = t.GetField(name, BF);
    if (f != null) return f.GetValue(target);
    throw new System.Exception("member not found " + t.Name + "." + name);
}
UnityEngine.ScriptableObject New(string type) => UnityEngine.ScriptableObject.CreateInstance(T(type));
void Add(object parent, object child) => Call(parent, "AddChild", child, -1, false);
void Setting(object model, string name, object value)
{
    var f = model.GetType().GetField(name, BF);
    object v = value;
    if (f != null && f.FieldType.IsEnum && value is string s) v = System.Enum.Parse(f.FieldType, s);
    Call(model, "SetSettingValue", name, v);
}
object In(object model, string name)
{
    foreach (var s in (System.Collections.IEnumerable)Get(model, "inputSlots"))
        if ((string)Get(s, "name") == name) return s;
    var names = new System.Text.StringBuilder();
    foreach (var s in (System.Collections.IEnumerable)Get(model, "inputSlots")) names.Append(Get(s, "name") + ",");
    throw new System.Exception("no input slot " + name + " on " + model.GetType().Name + " (" + names + ")");
}
object Out(object model, int i) => Call(model, "GetOutputSlot", i);
// Links and checks: a link VFX refuses (incompatible types) is otherwise silent. Tries the slot, then its child.
void Link(object to, object from)
{
    Call(to, "Link", from, true);
    if ((bool)Call(to, "HasLink", false)) return;
    foreach (var c in (System.Collections.IEnumerable)Get(to, "children"))
    {
        Call(c, "Link", from, true);
        if ((bool)Call(c, "HasLink", false)) return;
    }
    throw new System.Exception("link refused into " + Get(to, "name"));
}
void Value(object slot, object value)
{
    System.Reflection.PropertyInfo p = null;
    for (var k = slot.GetType(); k != null && p == null; k = k.BaseType)
        p = k.GetProperty("value", BF | System.Reflection.BindingFlags.DeclaredOnly);
    object current = p.GetValue(slot);
    if (value is UnityEngine.Vector3 v3 && current != null && !(current is UnityEngine.Vector3))
    {
        object wrapped = System.Activator.CreateInstance(current.GetType());
        foreach (var f in current.GetType().GetFields(BF))
            if (f.FieldType == typeof(UnityEngine.Vector3)) { f.SetValue(wrapped, v3); break; }
        value = wrapped;
    }
    p.SetValue(slot, value);
}
// The slot itself, or its Vector3 child when it is a composite (Position, Vector, DirectionType).
object Vec(object slot)
{
    var children = (System.Collections.IEnumerable)Get(slot, "children");
    foreach (var c in children) return c;
    return slot;
}

const string path = "Assets/UOClient/VFX/Grass.vfx";
UnityEditor.AssetDatabase.DeleteAsset(path);
Call(T("UnityEditor.VisualEffectAssetEditorUtility"), "CreateNewAsset", path);
var resource = Call(T("UnityEditor.VFX.VisualEffectResource"), "GetResourceAtPath", path);
var ext = T("UnityEditor.VFX.VisualEffectResourceExtensions");
var graph = Call(ext, "GetGraph", resource) ?? Call(ext, "CreateGraph", resource);

// ---------------------------------------------------------------- exposed parameters
object Param(System.Type type, string name, object value)
{
    var p = New("UnityEditor.VFX.VFXParameter");
    Call(p, "Init", type);
    Add(graph, p);
    Setting(p, "m_ExposedName", name);
    Setting(p, "m_Exposed", true);
    if (value != null) Value(Out(p, 0), value);
    return p;
}
var colorMask = Param(typeof(UnityEngine.Texture2D), "ColorMask", null);
var normalHeight = Param(typeof(UnityEngine.Texture2D), "NormalHeight", null);
var chunkSize = Param(typeof(UnityEngine.Vector3), "ChunkSize", new UnityEngine.Vector3(80f, 32f, 80f));
// The camera in the chunk's local space, set every frame by GrassViewer: drives the distance falloff.
var viewer = Param(typeof(UnityEngine.Vector3), "Viewer", new UnityEngine.Vector3(40f, 0f, 40f));
const float FullDensityTo = 14f, FadeOutBy = 60f;
// Where characters push the grass aside: a camera-centred map (GrassPushMap, 32 m across), rg = direction, b = strength.
var pushMap = Param(typeof(UnityEngine.Texture2D), "PushMap", null);
var pushCentre = Param(typeof(UnityEngine.Vector3), "PushCentre", new UnityEngine.Vector3(40f, 0f, 40f));
const float PushMapSize = 32f, PushBend = 1.8f;

object Op(string type) { var o = New(type); Add(graph, o); return o; }
object Swz(object fromSlot, string mask) { var s = Op("UnityEditor.VFX.Operator.Swizzle"); Setting(s, "mask", mask); Link(In(s, "x"), fromSlot); return s; }
object Times(object fromSlot, float k)
{
    var m = Op("UnityEditor.VFX.Operator.Multiply");
    Link(Call(m, "GetInputSlot", 0), fromSlot);
    Value(Call(m, "GetInputSlot", 1), k);
    return m;
}
object Vec3Op(string type, object a, object b)
{
    var m = Op("UnityEditor.VFX.Operator." + type);
    Call(m, "SetOperandType", 0, typeof(UnityEngine.Vector3));
    Call(m, "SetOperandType", 1, typeof(UnityEngine.Vector3));
    if (a is UnityEngine.Vector3 va) Value(Call(m, "GetInputSlot", 0), va); else Link(Call(m, "GetInputSlot", 0), a);
    if (b is UnityEngine.Vector3 vb) Value(Call(m, "GetInputSlot", 1), vb); else Link(Call(m, "GetInputSlot", 1), b);
    return m;
}
UnityEngine.Object VfxShader(string path)
{
    foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path)) if (o != null && o.GetType().Name == "ShaderGraphVfxAsset") return o;
    throw new System.Exception("no VFX shader graph asset in " + path);
}
UnityEngine.Mesh MeshOf(string prefab)
{
    if (prefab.StartsWith("Assets/")) return UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(prefab);
    foreach (var g in UnityEditor.AssetDatabase.FindAssets(prefab + " t:Prefab", new[] { "Assets/BK" }))
    {
        var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
        if (System.IO.Path.GetFileNameWithoutExtension(p) != prefab) continue;
        return UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(p).GetComponentInChildren<UnityEngine.MeshFilter>().sharedMesh;
    }
    throw new System.Exception("no prefab " + prefab);
}
// A texture's average colour over its visible (alpha) texels, read back through the GPU.
UnityEngine.Vector3 AverageOf(string textureName)
{
    UnityEngine.Texture2D tex = textureName.StartsWith("Assets/") ? UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(textureName) : null;
    foreach (var g in tex != null ? new string[0] : UnityEditor.AssetDatabase.FindAssets(textureName + " t:Texture2D", new[] { "Assets/BK" }))
    {
        var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
        if (System.IO.Path.GetFileNameWithoutExtension(p) == textureName) { tex = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(p); break; }
    }
    var rt = UnityEngine.RenderTexture.GetTemporary(64, 64, 0, UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.sRGB);
    UnityEngine.Graphics.Blit(tex, rt);
    var prev = UnityEngine.RenderTexture.active; UnityEngine.RenderTexture.active = rt;
    var read = new UnityEngine.Texture2D(64, 64, UnityEngine.TextureFormat.RGBA32, false);
    read.ReadPixels(new UnityEngine.Rect(0, 0, 64, 64), 0, 0);
    UnityEngine.RenderTexture.active = prev; UnityEngine.RenderTexture.ReleaseTemporary(rt);
    UnityEngine.Vector3 sum = UnityEngine.Vector3.zero; float w = 0f;
    foreach (var c in read.GetPixels()) { if (c.a < 0.5f) continue; sum += new UnityEngine.Vector3(c.r, c.g, c.b); w += 1f; }
    return w > 0f ? sum / w : new UnityEngine.Vector3(0.5f, 0.5f, 0.5f);
}

// One grass system: blades scattered over the chunk, kept by the ground's grassiness, seated on the drawn ground,
// standing straight up with a fixed facing, swaying in the wind, drawn as BK grass meshes (one of `meshes`, picked
// per blade) through a lit shader that multiplies the BK texture by the ground colour. Casts and receives shadows.
void GrassSystem(int count, string shaderGraphPath, string[] meshes, string atlas, float minScale, float maxScale, float keepBias)
{
    var spawner = New("UnityEditor.VFX.VFXBasicSpawner");
    var init = New("UnityEditor.VFX.VFXBasicInitialize");
    var update = New("UnityEditor.VFX.VFXBasicUpdate");
    // Shader Graphs on the current integration render only through the composed output (topology + shading).
    var output = New("UnityEditor.VFX.VFXComposedParticleOutput");
    Add(graph, spawner); Add(graph, init); Add(graph, update); Add(graph, output);
    Call(spawner, "LinkTo", init, 0, 0);
    Call(init, "LinkTo", update, 0, 0);
    Call(update, "LinkTo", output, 0, 0);
    Setting(update, "integration", "None");
    Setting(update, "angularIntegration", "None");
    Setting(update, "ageParticles", false);
    Setting(update, "reapParticles", false);

    var burst = New("UnityEditor.VFX.VFXSpawnerBurst");
    Add(spawner, burst);
    Value(In(burst, "Count"), (float)count);
    var data = Call(init, "GetData");
    Setting(data, "capacity", (uint)count);
    Setting(data, "boundsMode", "Manual");
    var boundsSlot = In(init, "bounds");
    var box = System.Activator.CreateInstance(Get(boundsSlot, "value").GetType());
    box.GetType().GetField("center").SetValue(box, new UnityEngine.Vector3(40f, 32f, 40f));
    box.GetType().GetField("size").SetValue(box, new UnityEngine.Vector3(84f, 160f, 84f));
    Value(boundsSlot, box);

    object SetAttr(string attribute, string random = "Off")
    {
        var b = New("UnityEditor.VFX.Block.SetAttribute");
        Setting(b, "attribute", attribute);
        Setting(b, "Random", random);
        Add(init, b);
        return b;
    }

    // Where: a per-blade random UV over the chunk (a Vector4 random; see the notes on Swizzle).
    var r4 = Op("UnityEditor.VFX.Operator.Random");
    Call(r4, "SetOperandType", typeof(UnityEngine.Vector4));
    var uv = Swz(Out(r4, 0), "xy");
    var sampleColour = Op("UnityEditor.VFX.Operator.SampleTexture2D");
    Link(In(sampleColour, "texture"), Out(colorMask, 0));
    Link(In(sampleColour, "UV"), Out(uv, 0));
    var sampleGround = Op("UnityEditor.VFX.Operator.SampleTexture2D");
    Link(In(sampleGround, "texture"), Out(normalHeight, 0));
    Link(In(sampleGround, "UV"), Out(uv, 0));

    var where = Op("UnityEditor.VFX.Operator.AppendVector");
    Call(where, "AddOperand", typeof(float));
    Link(Call(where, "GetInputSlot", 0), Out(Times(Out(Swz(Out(r4, 0), "x"), 0), 80f), 0));
    // The height sampled bilinearly from the corners rides a little above the drawn triangles on uneven ground:
    // sink the base 6 cm so every blade grows out of the ground.
    var sunk = Op("UnityEditor.VFX.Operator.Add");
    Link(Call(sunk, "GetInputSlot", 0), Out(Swz(Out(sampleGround, 0), "w"), 0));
    Value(Call(sunk, "GetInputSlot", 1), -0.06f);
    Link(Call(where, "GetInputSlot", 1), Out(sunk, 0));
    Link(Call(where, "GetInputSlot", 2), Out(Times(Out(Swz(Out(r4, 0), "y"), 0), 80f), 0));
    Link(Call(SetAttr("position"), "GetInputSlot", 0), Out(where, 0));

    // Kept as much as the ground is grassy (z of the random), and which mesh (w of the random).
    var keep = Op("UnityEditor.VFX.Operator.Condition");
    Setting(keep, "condition", "Less");
    Link(In(keep, "left"), Out(Times(Out(Swz(Out(r4, 0), "z"), 0), keepBias), 0));
    Link(In(keep, "right"), Out(Swz(Out(sampleColour, 0), "w"), 0));
    Link(Call(SetAttr("alive"), "GetInputSlot", 0), Out(keep, 0));
    if (meshes.Length > 1)
    {
        // w squared: earlier meshes (the common styles) far more often than the last (the rare one).
        var w = Swz(Out(r4, 0), "w");
        var w2 = Op("UnityEditor.VFX.Operator.Multiply");
        Link(Call(w2, "GetInputSlot", 0), Out(w, 0));
        Link(Call(w2, "GetInputSlot", 1), Out(w, 0));
        Link(Call(SetAttr("meshIndex"), "GetInputSlot", 0), Out(Times(Out(w2, 0), meshes.Length - 0.01f), 0));
    }

    // Colour: the ground colour divided by the BK texture's own average, so texture x colour is the ground
    // colour with the texture's detail.
    var avg = AverageOf(atlas);
    var gain = new UnityEngine.Vector3(1f / UnityEngine.Mathf.Max(avg.x, 0.05f), 1f / UnityEngine.Mathf.Max(avg.y, 0.05f), 1f / UnityEngine.Mathf.Max(avg.z, 0.05f));
    // ...and a slight per-blade shade (0.85-1.1) so a field isn't one flat colour.
    var shadeRand = Op("UnityEditor.VFX.Operator.Random");
    // Separate Random operators repeat each other's sequence unless their seeds differ.
    Value(Call(shadeRand, "GetInputSlot", 2), 7919u);
    Value(Call(shadeRand, "GetInputSlot", 0), 0.85f);
    Value(Call(shadeRand, "GetInputSlot", 1), 1.1f);
    var shaded = Vec3Op("Multiply", Out(Swz(Out(sampleColour, 0), "xyz"), 0), Out(Swz(Out(shadeRand, 0), "xxx"), 0));
    var tint = Vec3Op("Multiply", Out(shaded, 0), gain);
    Link(Call(SetAttr("color"), "GetInputSlot", 0), Out(tint, 0));

    // A fixed random facing, kept in `velocity` (the Update stage moves nothing).
    var facing = Op("UnityEditor.VFX.Operator.Random");
    Call(facing, "SetOperandType", typeof(UnityEngine.Vector3));
    Value(Call(facing, "GetInputSlot", 0), new UnityEngine.Vector3(-1f, 0f, -1f));
    Value(Call(facing, "GetInputSlot", 1), new UnityEngine.Vector3(1f, 0f, 1f));
    Link(Call(SetAttr("velocity"), "GetInputSlot", 0), Out(facing, 0));

    // A fixed random number per blade (kept in `lifetime`, which nothing ages): its place in the distance fade.
    var fadeRand = Op("UnityEditor.VFX.Operator.Random");
    Value(Call(fadeRand, "GetInputSlot", 2), 104729u);
    Link(Call(SetAttr("lifetime"), "GetInputSlot", 0), Out(fadeRand, 0));

    var sz = SetAttr("size", "Uniform");
    Value(Call(sz, "GetInputSlot", 0), minScale);
    Value(Call(sz, "GetInputSlot", 1), maxScale);

    // ---- output: BK meshes through the lit grass shader, casting shadows.
    var topology = System.Activator.CreateInstance(T("UnityEditor.VFX.ParticleTopologyMesh"), true);
    topology.GetType().GetField("MeshCount", BF).SetValue(topology, (uint)meshes.Length);
    var shading = System.Activator.CreateInstance(T("UnityEditor.VFX.ParticleShadingShaderGraph"), true);
    shading.GetType().GetField("shaderGraph", BF).SetValue(shading, VfxShader(shaderGraphPath));
    Call(output, "SetSettingValue", "m_Topology", topology);
    Call(output, "SetSettingValue", "m_Shading", shading);
    Setting(output, "castShadows", true);
    for (int i = 0; i < meshes.Length; i++) Value(In(output, meshes.Length > 1 ? "mesh" + i : "mesh"), MeshOf(meshes[i]));
    var getColour = Op("UnityEditor.VFX.VFXAttributeParameter");
    Setting(getColour, "attribute", "color");
    Link(In(output, "_Color"), Out(getColour, 0));

    // Distance falloff: full density within FullDensityTo of the viewer, thinning to none by FadeOutBy. A blade
    // draws while its fixed random number is under the fade, so the same blades stay as you walk (no flicker).
    var getPosF = Op("UnityEditor.VFX.VFXAttributeParameter");
    Setting(getPosF, "attribute", "position");
    var dist = Op("UnityEditor.VFX.Operator.Distance");
    Call(dist, "SetOperandType", typeof(UnityEngine.Vector3));
    Link(Call(dist, "GetInputSlot", 0), Out(getPosF, 0));
    Link(Call(dist, "GetInputSlot", 1), Out(viewer, 0));
    var fadeAdd = Op("UnityEditor.VFX.Operator.Add");
    Link(Call(fadeAdd, "GetInputSlot", 0), Out(Times(Out(dist, 0), -1f / (FadeOutBy - FullDensityTo)), 0));
    Value(Call(fadeAdd, "GetInputSlot", 1), FadeOutBy / (FadeOutBy - FullDensityTo));
    var fade = Op("UnityEditor.VFX.Operator.Saturate");
    Link(Call(fade, "GetInputSlot", 0), Out(fadeAdd, 0));
    var getRoll = Op("UnityEditor.VFX.VFXAttributeParameter");
    Setting(getRoll, "attribute", "lifetime");
    var shown = Op("UnityEditor.VFX.Operator.Condition");
    Setting(shown, "condition", "Less");
    Link(In(shown, "left"), Out(getRoll, 0));
    Link(In(shown, "right"), Out(fade, 0));
    var show = New("UnityEditor.VFX.Block.SetAttribute");
    Setting(show, "attribute", "alive");
    Add(output, show);
    Link(Call(show, "GetInputSlot", 0), Out(shown, 0));

    // Straight up, swaying with the wind: up + wind * sin(time + a phase from the blade position).
    var time = Op("UnityEditor.VFX.VFXDynamicBuiltInParameter");
    Setting(time, "m_BuiltInParameters", "VfxTotalTime");
    var getPos = Op("UnityEditor.VFX.VFXAttributeParameter");
    Setting(getPos, "attribute", "position");
    var phase = Op("UnityEditor.VFX.Operator.Add");
    Call(phase, "AddOperand", typeof(float));
    Link(Call(phase, "GetInputSlot", 0), Out(Times(Out(time, 0), 1.6f), 0));
    Link(Call(phase, "GetInputSlot", 1), Out(Times(Out(Swz(Out(getPos, 0), "x"), 0), 0.35f), 0));
    Link(Call(phase, "GetInputSlot", 2), Out(Times(Out(Swz(Out(getPos, 0), "z"), 0), 0.27f), 0));
    var sine = Op("UnityEditor.VFX.Operator.Sine");
    Link(Call(sine, "GetInputSlot", 0), Out(phase, 0));
    var gust = Vec3Op("Multiply", Out(Swz(Out(sine, 0), "xxx"), 0), new UnityEngine.Vector3(0.12f, 0f, 0.07f));
    // Pushed aside: sample the push map under the blade (relative to the viewer) and bend away from whoever stands there.
    object Vec2Op(string type, object a, object b)
    {
        var m = Op("UnityEditor.VFX.Operator." + type);
        Call(m, "SetOperandType", 0, typeof(UnityEngine.Vector2));
        Call(m, "SetOperandType", 1, typeof(UnityEngine.Vector2));
        if (a is UnityEngine.Vector2 va) Value(Call(m, "GetInputSlot", 0), va); else Link(Call(m, "GetInputSlot", 0), a);
        if (b is UnityEngine.Vector2 vb) Value(Call(m, "GetInputSlot", 1), vb); else Link(Call(m, "GetInputSlot", 1), b);
        return m;
    }
    // Swizzle reads a Vector4 and a Vector3 fed into it loses its z (as a Vector2 loses its y), so pad to 4 first.
    object XZ(object v3)
    {
        var pad = Op("UnityEditor.VFX.Operator.AppendVector");
        Call(pad, "SetOperandType", 0, typeof(UnityEngine.Vector3));
        Link(Call(pad, "GetInputSlot", 0), v3);
        Value(Call(pad, "GetInputSlot", 1), 0f);
        return Out(Swz(Out(pad, 0), "xz"), 0);
    }
    var rel = Vec2Op("Subtract", XZ(Out(getPos, 0)), XZ(Out(pushCentre, 0)));
    var pushUv = Vec2Op("Add", Out(Vec2Op("Multiply", Out(rel, 0), new UnityEngine.Vector2(1f / PushMapSize, 1f / PushMapSize)), 0), new UnityEngine.Vector2(0.5f, 0.5f));
    var samplePush = Op("UnityEditor.VFX.Operator.SampleTexture2D");
    Link(In(samplePush, "texture"), Out(pushMap, 0));
    Link(In(samplePush, "UV"), Out(pushUv, 0));
    // direction (r, g) from 0..1 back to -1..1, as (x, 0, z), times strength b times the bend.
    var pushDir = Op("UnityEditor.VFX.Operator.AppendVector");
    Call(pushDir, "AddOperand", typeof(float));
    var px = Op("UnityEditor.VFX.Operator.Add");
    Link(Call(px, "GetInputSlot", 0), Out(Times(Out(Swz(Out(samplePush, 0), "x"), 0), 2f), 0));
    Value(Call(px, "GetInputSlot", 1), -1f);
    var pz = Op("UnityEditor.VFX.Operator.Add");
    Link(Call(pz, "GetInputSlot", 0), Out(Times(Out(Swz(Out(samplePush, 0), "y"), 0), 2f), 0));
    Value(Call(pz, "GetInputSlot", 1), -1f);
    Link(Call(pushDir, "GetInputSlot", 0), Out(px, 0));
    Value(Call(pushDir, "GetInputSlot", 1), 0f);
    Link(Call(pushDir, "GetInputSlot", 2), Out(pz, 0));
    var pushAmount = Times(Out(Swz(Out(samplePush, 0), "z"), 0), PushBend);
    var push = Vec3Op("Multiply", Out(pushDir, 0), Out(Swz(Out(pushAmount, 0), "xxx"), 0));

    var swayUp = Vec3Op("Add", new UnityEngine.Vector3(0f, 1f, 0f), Out(gust, 0));
    var up = Vec3Op("Add", Out(swayUp, 0), Out(push, 0));
    var getFacing = Op("UnityEditor.VFX.VFXAttributeParameter");
    Setting(getFacing, "attribute", "velocity");
    var orient = New("UnityEditor.VFX.Block.Orient");
    Setting(orient, "mode", "Advanced");
    Setting(orient, "axes", "YZ");
    Add(output, orient);
    Link(Call(orient, "GetInputSlot", 0), Out(up, 0));
    Link(Call(orient, "GetInputSlot", 1), Out(getFacing, 0));
}

// Individual blades: four generated cluster styles (Tools/VFX/GrassBladeMeshes.eval.cs), fine ones most common.
GrassSystem(440000, "Assets/UOClient/VFX/GrassBlades.shadergraph",
    new[] { "Assets/UOClient/VFX/GrassBladesFine.asset", "Assets/UOClient/VFX/GrassBladesBroad.asset", "Assets/UOClient/VFX/GrassBladesWispy.asset", "Assets/UOClient/VFX/GrassBladesSeed.asset" },
    "Assets/UOClient/VFX/GrassBladeShade.png", 0.55f, 0.8f, 1f);

// ---------------------------------------------------------------- save
Call(graph, "SetExpressionGraphDirty", true);
Call(ext, "WriteAssetWithSubAssets", resource);
UnityEditor.EditorUtility.SetDirty((UnityEngine.Object)resource);
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceSynchronousImport | UnityEditor.ImportAssetOptions.ForceUpdate);
return "saved " + path;
