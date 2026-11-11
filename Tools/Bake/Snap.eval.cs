// Renders the main camera to a PNG (path below) and reports loaded chunks and doors.
string PATH = @"G:\Ultima Online\UnityClient\Docs\bake-view.png";
var cam = UnityEngine.Camera.main;
var rt = new UnityEngine.RenderTexture(1600, 900, 24);
cam.targetTexture = rt; cam.Render();
UnityEngine.RenderTexture.active = rt;
var tex = new UnityEngine.Texture2D(1600, 900, UnityEngine.TextureFormat.RGB24, false);
tex.ReadPixels(new UnityEngine.Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
cam.targetTexture = null; UnityEngine.RenderTexture.active = null;
System.IO.File.WriteAllBytes(PATH, tex.EncodeToPNG());
UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(tex);
var view = UnityEngine.Object.FindFirstObjectByType<UOClient.Bake.BakedWorldView>();
return "saved; chunks loaded " + (view != null ? view.LoadedCount : -1) + ", doors " + UOClient.Buildings.BuildingDoor.All.Count;
