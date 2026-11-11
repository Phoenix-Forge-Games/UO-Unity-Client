// Loads two baked chunk files and lists where their object trees first differ (name, position, scale, mesh size).
// Set A and B below; run with "script eval" (edit or play mode).
string A = @"G:\Ultima Online\UnityClient\WorldData\1\23_25.chunk";
string B = @"C:\path\to\other\23_25.chunk";
var pal = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.Bake.WorldPalette>("Assets/UOClient/Data/WorldPalette.asset");
var con = UnityEditor.AssetDatabase.LoadAssetAtPath<UOClient.Bake.WorldContent>("Assets/UOClient/Data/WorldContent.asset");
var rd = new UOClient.Bake.ChunkReader(pal, con);
var a = rd.Load(System.IO.File.ReadAllBytes(A), null);
var b = rd.Load(System.IO.File.ReadAllBytes(B), null);
var la = new System.Collections.Generic.List<string>(); var lb = new System.Collections.Generic.List<string>();
System.Action<UnityEngine.Transform, System.Collections.Generic.List<string>, string> walk = null;
walk = (t, l, d) => { var mf = t.GetComponent<UnityEngine.MeshFilter>(); l.Add(d + t.name + " " + t.position.ToString("F3") + " " + t.lossyScale.ToString("F3") + (mf != null && mf.sharedMesh != null ? " v" + mf.sharedMesh.vertexCount : "")); for (int i = 0; i < t.childCount; i++) walk(t.GetChild(i), l, d + " "); };
walk(a.Root.transform, la, ""); walk(b.Root.transform, lb, "");
var sb = new System.Text.StringBuilder("A " + la.Count + " objects, B " + lb.Count + "\n"); int shown = 0;
for (int i = 0; i < System.Math.Max(la.Count, lb.Count) && shown < 12; i++) { string x = i < la.Count ? la[i] : "-", y = i < lb.Count ? lb[i] : "-"; if (x != y) { sb.Append("#" + i + "\n A " + x + "\n B " + y + "\n"); shown++; } }
if (a.Terrain != null && b.Terrain != null) { var ha = a.Terrain.GetComponent<UnityEngine.Terrain>().terrainData; var hb = b.Terrain.GetComponent<UnityEngine.Terrain>().terrainData; int r = ha.heightmapResolution; var x1 = ha.GetHeights(0,0,r,r); var x2 = hb.GetHeights(0,0,r,r); int d = 0; for (int y=0;y<r;y++) for (int x=0;x<r;x++) if (x1[y,x]!=x2[y,x]) d++; sb.Append("terrain heights differing: " + d + "\n"); }
a.Dispose(); b.Dispose(); return sb.ToString();
