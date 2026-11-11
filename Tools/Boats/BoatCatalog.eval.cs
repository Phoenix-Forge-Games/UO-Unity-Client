// Builds Assets/UOClient/Data/BoatCatalog.asset (run with the Unity CLI `script eval --code`): UO boat multi IDs →
// Synty POLYGON Pirate ships. Boat IDs from the shard's scripts (Multis/Boats, Services/Expansions/High Seas).
const string P = "Assets/Synty/PolygonPirates/Prefabs/Vehicles/";
UnityEngine.GameObject G(string n) => UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(P + n + ".prefab");
var entries = new System.Collections.Generic.List<UOClient.BoatCatalog.Entry>();
void Add(string name, int north, string prefab, float draft) { var g = G(prefab); if (g != null) entries.Add(new UOClient.BoatCatalog.Entry { Name = name, NorthId = north, Prefab = g, Draft = draft }); }
Add("small boat", 0x00, "SM_Veh_Boat_Small_01_Hull_Attachments", 0.05f);
Add("small dragon boat", 0x04, "SM_Veh_Boat_Small_01_Hull_Pirate_Attachments", 0.05f);
Add("medium boat", 0x08, "SM_Veh_Boat_Medium_01_Hull_Attachments", 0.06f);
Add("medium dragon boat", 0x0C, "SM_Veh_Boat_Medium_01_Hull_Pirate_Attachments", 0.06f);
Add("large boat", 0x10, "SM_Veh_Veh_Boat_Large_01_Hull_Attachments", 0.06f);
Add("large dragon boat", 0x14, "SM_Veh_Veh_Boat_Large_01_Hull_Pirate_Attachments", 0.06f);
Add("orcish galleon", 0x18, "SM_Veh_Boat_Warship_01_Hull_Pirate_Attachments", 0.07f);
Add("gargish galleon", 0x24, "SM_Veh_Boat_Warship_01_Hull_Attachments", 0.07f);
Add("tokuno galleon", 0x30, "SM_Veh_Boat_Warship_01_Hull", 0.07f);
Add("rowboat", 0x3C, "SM_Veh_Boat_Rowing_01_Hull_Attachments", 0.08f);
Add("britannian ship", 0x40, "SM_Veh_Boat_Warship_01_Hull_Upsized", 0.07f);
Add("pumpkin rowboat", 0x50, "SM_Veh_Boat_Rowing_01_Hull_Pirate_Attachments", 0.08f);
string path = "Assets/UOClient/Data/BoatCatalog.asset";
var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.BoatCatalog>(path);
if (catalog == null) { catalog = UnityEngine.ScriptableObject.CreateInstance<UOClient.BoatCatalog>(); UnityEditor.AssetDatabase.CreateAsset(catalog, path); }
catalog.Entries = entries.ToArray();
UnityEditor.EditorUtility.SetDirty(catalog);
UnityEditor.AssetDatabase.SaveAssets();
return entries.Count + " boats";
