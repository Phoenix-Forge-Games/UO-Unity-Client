import json, re, sys
src = sys.argv[1]; dst = sys.argv[2]; blade = sys.argv[3]; mulsrc = sys.argv[4]
s = open(src, encoding="utf-8").read()
objs = [json.loads(o) for o in re.split(r'\n\n(?=\{)', s) if o.strip()]
by = {o["m_ObjectId"]: o for o in objs}
g = objs[0]
def typ(i): return by[i]["m_Type"].split(".")[-1]
# Nodes to keep: block nodes, the Color property node, the sample that feeds Alpha.
alpha_sample = None
for e in g["m_Edges"]:
    i = e["m_InputSlot"]["m_Node"]["m_Id"]
    if by[i].get("m_Name") == "SurfaceDescription.Alpha":
        alpha_sample = e["m_OutputSlot"]["m_Node"]["m_Id"]
colour_prop_node = None
for n in g["m_Nodes"]:
    o = by[n["m_Id"]]
    if typ(n["m_Id"]) == "PropertyNode" and by[o["m_Property"]["m_Id"]]["m_Type"].endswith("ColorShaderProperty"):
        colour_prop_node = n["m_Id"]
keep_nodes = [n for n in g["m_Nodes"] if typ(n["m_Id"]) == "BlockNode" or n["m_Id"] in (alpha_sample, colour_prop_node)]
keep_ids = {n["m_Id"] for n in keep_nodes}
g["m_Nodes"] = keep_nodes
g["m_Edges"] = [e for e in g["m_Edges"] if e["m_OutputSlot"]["m_Node"]["m_Id"] in keep_ids and e["m_InputSlot"]["m_Node"]["m_Id"] in keep_ids]
g["m_GroupDatas"] = []
g["m_Path"] = "UOClient"
# Properties: only Color.
g["m_Properties"] = [p for p in g["m_Properties"] if by[p["m_Id"]]["m_Type"].endswith("ColorShaderProperty")]
for p in g["m_Properties"]:
    by[p["m_Id"]]["m_Value"] = {"r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0}
for c in g["m_CategoryData"]:
    by[c["m_Id"]]["m_ChildObjectList"] = [{"m_Id": p["m_Id"]} for p in g["m_Properties"]]
# Targets: URP only.
g["m_ActiveTargets"] = [t for t in g["m_ActiveTargets"] if by[t["m_Id"]]["m_Type"].endswith("UniversalTarget")]
for n in keep_ids:
    by[n]["m_Group"] = {"m_Id": ""}
# The alpha sample reads the blade texture with plain UVs.
for sl in by[alpha_sample]["m_Slots"]:
    so = by[sl["m_Id"]]
    if so["m_Type"].endswith("Texture2DInputMaterialSlot"):
        so["m_Texture"]["m_SerializedTexture"] = json.dumps({"texture": {"fileID": 2800000, "guid": blade, "type": 3}}, separators=(",", ":"))
        so["m_DefaultType"] = 0
# BaseColor = Color property x texture RGB (the blade's texture detail, tinted by the ground colour the VFX passes).
import uuid
ms = open(mulsrc, encoding="utf-8").read()
mobjs = [json.loads(o) for o in re.split(r'\n\n(?=\{)', ms) if o.strip()]
mby = {o["m_ObjectId"]: o for o in mobjs}
mul = next(o for o in mobjs if o["m_Type"].endswith("MultiplyNode"))
new = {}
for sl in mul["m_Slots"]:
    so = dict(mby[sl["m_Id"]]); so["m_ObjectId"] = uuid.uuid4().hex; new[sl["m_Id"]] = so["m_ObjectId"]; objs.append(so); by[so["m_ObjectId"]] = so
mul = dict(mul); mul["m_ObjectId"] = uuid.uuid4().hex; mul["m_Slots"] = [{"m_Id": new[s_["m_Id"]]} for s_ in mul["m_Slots"]]
objs.append(mul); by[mul["m_ObjectId"]] = mul
g["m_Nodes"].append({"m_Id": mul["m_ObjectId"]})
base_block = next(n for n in keep_ids if by[n].get("m_Name") == "SurfaceDescription.BaseColor")
g["m_Edges"] = [e for e in g["m_Edges"] if e["m_InputSlot"]["m_Node"]["m_Id"] != base_block]
def edge(on, os_, inn, is_): g["m_Edges"].append({"m_OutputSlot": {"m_Node": {"m_Id": on}, "m_SlotId": os_}, "m_InputSlot": {"m_Node": {"m_Id": inn}, "m_SlotId": is_}})
edge(colour_prop_node, 0, mul["m_ObjectId"], 0)
edge(alpha_sample, 0, mul["m_ObjectId"], 1)   # RGBA output is slot 0
edge(mul["m_ObjectId"], 2, base_block, 0)

# Matte: smoothness low.
for n in keep_ids:
    o = by[n]
    if o.get("m_Name") == "SurfaceDescription.Smoothness":
        by[o["m_Slots"][0]["m_Id"]]["m_Value"] = 0.1
# Keep only objects reachable from the graph.
def refs(o):
    out = set()
    def walk(v):
        if isinstance(v, dict):
            if set(v.keys()) == {"m_Id"} and isinstance(v["m_Id"], str): out.add(v["m_Id"])
            else:
                for x in v.values(): walk(x)
        elif isinstance(v, list):
            for x in v: walk(x)
    walk(o)
    return out
reach = {g["m_ObjectId"]}; stack = [g]
while stack:
    o = stack.pop()
    for r in refs(o):
        if r in by and r not in reach:
            reach.add(r); stack.append(by[r])
out = [o for o in objs if o["m_ObjectId"] in reach]
open(dst, "w", encoding="utf-8").write("\n\n".join(json.dumps(o, indent=4) for o in out) + "\n")
print(len(objs), "->", len(out), "alpha sample", alpha_sample[:8], "colour node", (colour_prop_node or "")[:8])
