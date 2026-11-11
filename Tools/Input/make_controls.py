"""Writes Assets/UOClient/Input/UOControls.inputactions (and its .meta with C# wrapper generation on).
Edit the tables below and re-run; IDs are derived from names so they stay stable."""
import json, uuid, os
NS = uuid.UUID("6f1d2b1e-3c55-4b7a-9d0a-5a7e0c1b2d3f")
def gid(*parts): return str(uuid.uuid5(NS, "/".join(parts)))

# map -> [(action, type, controlType, [bindings])]; a binding is a path or ("2DVector", {part: path})
MAPS = {
    "Gameplay": [
        ("Move", "Value", "Vector2", [("2DVector", {"up": "<Keyboard>/w", "down": "<Keyboard>/s", "left": "<Keyboard>/a", "right": "<Keyboard>/d"}),
                                      ("2DVector", {"up": "<Keyboard>/upArrow", "down": "<Keyboard>/downArrow", "left": "<Keyboard>/leftArrow", "right": "<Keyboard>/rightArrow"}),
                                      "<Gamepad>/leftStick"]),
        ("Run", "Button", "Button", ["<Keyboard>/leftShift", "<Gamepad>/leftStickPress"]),
        ("Jump", "Button", "Button", ["<Keyboard>/space", "<Gamepad>/buttonNorth"]),
        ("Look", "Value", "Vector2", ["<Mouse>/delta", "<Gamepad>/rightStick"]),
        ("LookHold", "Button", "Button", ["<Mouse>/rightButton"]),
        ("Zoom", "Value", "Axis", ["<Mouse>/scroll/y"]),
        ("Point", "PassThrough", "Vector2", ["<Mouse>/position"]),
        ("Select", "Button", "Button", ["<Mouse>/leftButton"]),
        ("Use", "Button", "Button", ["<Keyboard>/e", "<Gamepad>/buttonSouth"]),
        ("CycleTarget", "Button", "Button", ["<Keyboard>/tab", "<Gamepad>/rightShoulder"]),
        ("Back", "Button", "Button", ["<Keyboard>/escape", "<Gamepad>/buttonEast"]),
        ("Backpack", "Button", "Button", ["<Keyboard>/b"]),
        ("Character", "Button", "Button", ["<Keyboard>/c"]),
        ("Minimap", "Button", "Button", ["<Keyboard>/m"]),
        ("Attack", "Button", "Button", ["<Keyboard>/r", "<Gamepad>/rightTrigger"]),
        ("ToggleWarMode", "Button", "Button", ["<Keyboard>/backquote"]),
        ("Chat", "Button", "Button", ["<Keyboard>/enter", "<Keyboard>/numpadEnter"]),
        ("SplitStack", "Button", "Button", ["<Keyboard>/leftShift", "<Keyboard>/rightShift"]),  # held while dropping a stack: ask how many
        ("BuildMode", "Button", "Button", ["<Keyboard>/h"]),
        ("Pets", "Button", "Button", ["<Keyboard>/p"]),  # the pets window  # house building on your own plot
    ] + [("Hotbar%d" % (i + 1), "Button", "Button", ["<Keyboard>/%s" % ("0" if i == 9 else str(i + 1))]) for i in range(10)] + [
    ],
    # Enabled only while building (BuildMode): Place takes the click from Gameplay's Select.
    "Build": [
        ("Place", "Button", "Button", ["<Mouse>/leftButton"]),
        ("Remove", "Button", "Button", ["<Keyboard>/x", "<Keyboard>/delete"]),
        ("Rotate", "Button", "Button", ["<Keyboard>/q"]),
        ("LevelUp", "Button", "Button", ["<Keyboard>/pageUp", "<Keyboard>/rightBracket"]),
        ("LevelDown", "Button", "Button", ["<Keyboard>/pageDown", "<Keyboard>/leftBracket"]),
    ],
    "UI": [
        ("Submit", "Button", "Button", ["<Keyboard>/enter", "<Keyboard>/numpadEnter"]),
        ("Cancel", "Button", "Button", ["<Keyboard>/escape"]),
    ],
}

maps = []
for mname, actions in MAPS.items():
    acts, binds = [], []
    for aname, atype, ctype, bl in actions:
        acts.append({"name": aname, "type": atype, "id": gid(mname, aname), "expectedControlType": ctype,
                     "processors": "", "interactions": "", "initialStateCheck": atype != "Button"})
        for i, b in enumerate(bl):
            if isinstance(b, tuple):
                kind, parts = b
                binds.append({"name": kind, "id": gid(mname, aname, str(i)), "path": kind, "interactions": "", "processors": "",
                              "groups": "", "action": aname, "isComposite": True, "isPartOfComposite": False})
                for part, path in parts.items():
                    binds.append({"name": part, "id": gid(mname, aname, str(i), part), "path": path, "interactions": "", "processors": "",
                                  "groups": "", "action": aname, "isComposite": False, "isPartOfComposite": True})
            else:
                binds.append({"name": "", "id": gid(mname, aname, str(i)), "path": b, "interactions": "", "processors": "",
                              "groups": "", "action": aname, "isComposite": False, "isPartOfComposite": False})
    maps.append({"name": mname, "id": gid(mname), "actions": acts, "bindings": binds})

out = "G:/Ultima Online/UnityClient/Assets/UOClient/Input/UOControls.inputactions"
with open(out, "w", encoding="utf-8") as f:
    json.dump({"version": 1, "name": "UOControls", "maps": maps, "controlSchemes": []}, f, indent=4)
meta = out + ".meta"
if not os.path.exists(meta):
    with open(meta, "w", encoding="utf-8") as f:
        f.write(f"""fileFormatVersion: 2
guid: {uuid.uuid5(NS, 'UOControls.asset').hex}
ScriptedImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 2
  userData: 
  assetBundleName: 
  assetBundleVariant: 
  script: {{fileID: 11500000, guid: 8404be70184654265930450def6a9037, type: 3}}
  generateWrapperCode: 1
  wrapperCodePath: Assets/UOClient/Scripts/Input/UOControls.cs
  wrapperClassName: UOControls
  wrapperCodeNamespace: UOClient
""")
print("written")
