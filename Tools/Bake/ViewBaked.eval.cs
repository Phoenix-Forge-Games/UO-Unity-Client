// Play mode: loads baked chunks around a UO tile with BakedWorldView and points the main camera at it.
// Set FACET / TILE_X / TILE_Y / Z below. Take pictures with Tools/Bake/Snap.eval.cs once chunks have loaded.
int FACET = 1, TILE_X = 1475, TILE_Y = 1645, Z = 0;
var target = new UnityEngine.GameObject("Bake view target").transform;
target.position = UOClient.WorldSpace.TileCenter(TILE_X, TILE_Y, Z);
var go = new UnityEngine.GameObject("Baked World");
var view = go.AddComponent<UOClient.Bake.BakedWorldView>();
view.Content = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.Bake.WorldContent>("Assets/UOClient/Data/WorldContent.asset");
view.Palette = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.Bake.WorldPalette>("Assets/UOClient/Data/WorldPalette.asset");
view.Folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "WorldData"));
view.Facet = FACET;
view.Target = target;
view.RadiusChunks = 1;
var cam = UnityEngine.Camera.main;
cam.farClipPlane = 2000f;
cam.transform.position = target.position + new UnityEngine.Vector3(-60f, 70f, -60f);
cam.transform.LookAt(target.position);
return "viewing facet " + FACET + " around " + TILE_X + "," + TILE_Y;
