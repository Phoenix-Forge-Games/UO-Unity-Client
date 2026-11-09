using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UOClient.Buildings.Synty;
using UOClient.Content;

namespace UOClient.EditorTools
{
    /// <summary>
    /// Builds a Windows release of the client whose paid art (Synty, BK Pure Nature) ships only inside an
    /// AES-encrypted AssetBundle:
    ///
    /// 1. Gathers WorldView's asset fields plus the house-kit registry into a temporary <see cref="ContentPack"/>.
    /// 2. Builds that (and everything it depends on) into one AssetBundle, without type trees.
    /// 3. Encrypts the bundle into StreamingAssets/content.pak with the compiled-in content key.
    /// 4. Moves the packs' Resources folders aside so Unity doesn't ship them wholesale.
    /// 5. Builds an IL2CPP player; <see cref="ReleaseBuildGuard"/> clears WorldView's fields in the scene.
    /// 6. Moves IL2CPP's do-not-ship folder (generated C++, which contains the key) out of the build.
    /// 7. Scans the player's data files for any protected asset name and fails if one turns up.
    /// </summary>
    public static class ReleaseBuilder
    {
        public const string DefaultOutput = "Builds/Release/UOClient";
        public const string ExeName = "UOClient.exe";
        private const string WorldScenePath = "Assets/UOClient/Scenes/World.unity";
        private const string GeneratedFolder = "Assets/UOClient/Generated";
        private const string PackAssetPath = GeneratedFolder + "/ContentPack.asset";
        private const string BundleName = "content";
        private const string ExcludedSuffix = "_ReleaseExcluded";
        private const BuildTarget Target = BuildTarget.StandaloneWindows64;

        /// <summary>Folders whose contents may only ship inside the encrypted pack.</summary>
        public static readonly string[] ProtectedRoots =
        {
            "Assets/Synty/", "Assets/BK/", "Assets/UOClient/Materials/SyntyWalls/",
        };

        /// <summary>Where the house generator's by-name lookups resolve from, later folders winning.</summary>
        private static readonly string[] KitPrefabFolders =
        {
            "Assets/Synty/PolygonGeneric/Prefabs", "Assets/Synty/PolygonFantasyKingdom/Prefabs",
        };

        private static readonly string[] KitMaterialFolders =
        {
            "Assets/Synty/PolygonGeneric/Materials", "Assets/Synty/PolygonFantasyKingdom/Materials", "Assets/UOClient/Materials",
        };

        /// <summary>True only while this pipeline is building; the guard fails any other player build.</summary>
        public static bool Active { get; private set; }

        [MenuItem("UO Client/Release/Build")]
        public static void BuildMenu() => Build(DefaultOutput);

        /// <summary>Batch-mode entry: <c>-executeMethod UOClient.EditorTools.ReleaseBuilder.BuildFromCommandLine [-releaseOutput dir]</c>.</summary>
        public static void BuildFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-releaseOutput");
            string output = i >= 0 && i + 1 < args.Length ? args[i + 1] : DefaultOutput;
            bool ok = Build(output);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool Build(string outputDir)
        {
            try
            {
                Run(Path.GetFullPath(outputDir));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Release] FAILED: {e.Message}\n{e}");
                return false;
            }
        }

        private static void Run(string outputDir)
        {
            byte[] key = ContentKey.Get();
            if (key == null)
            {
                throw new InvalidOperationException("No content key. Run UO Client > Release > Generate Content Key, let scripts recompile, then build again.");
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Leave play mode first.");
            if (EditorApplication.isCompiling) throw new InvalidOperationException("Scripts are compiling; build again once they finish.");
            foreach (Scene s in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt))
            {
                if (s.isDirty) throw new InvalidOperationException($"Save scene '{s.name}' first.");
            }

            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
            Directory.CreateDirectory(outputDir);
            string bundleDir = Path.GetFullPath("Temp/ReleaseContent");
            if (Directory.Exists(bundleDir)) Directory.Delete(bundleDir, true);
            Directory.CreateDirectory(bundleDir);

            var moved = new List<(string from, string to)>();
            string[] protectedNames;
            BuildReport report;
            string stamp = outputDir + ".verified";
            if (File.Exists(stamp)) File.Delete(stamp);
            Active = true;
            try
            {
                Step("Gathering content");
                CreatePack();
                protectedNames = ProtectedNames();

                Step("Building content bundle");
                AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(bundleDir,
                    new[] { new AssetBundleBuild { assetBundleName = BundleName, assetNames = new[] { PackAssetPath } } },
                    BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.DisableWriteTypeTree
                    | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                    Target);
                if (manifest == null) throw new BuildFailedException("AssetBundle build failed (see the console).");

                Step("Moving pack Resources folders aside");
                moved = ExcludeResourcesFolders();

                Step("Building IL2CPP player");
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.Minimal);
                var options = new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    locationPathName = Path.Combine(outputDir, ExeName),
                    target = Target,
                    targetGroup = BuildTargetGroup.Standalone,
                    // Lets stripping see the types the bundle uses.
                    assetBundleManifestPath = Path.Combine(bundleDir, Path.GetFileName(bundleDir) + ".manifest"),
                    options = BuildOptions.None,
                };
                report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new BuildFailedException($"Player build {report.summary.result} ({report.summary.totalErrors} errors).");
                }
            }
            finally
            {
                Active = false;
                RestoreResourcesFolders(moved);
                AssetDatabase.DeleteAsset(GeneratedFolder);
            }

            string dataDir = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(ExeName) + "_Data");
            Step("Encrypting content pack");
            string pak = Path.Combine(dataDir, "StreamingAssets", ProtectedContent.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(pak));
            EncryptBundle(Path.Combine(bundleDir, BundleName), pak, key);

            Step("Removing do-not-ship folders");
            string symbols = Path.Combine(Path.GetDirectoryName(outputDir), Path.GetFileName(outputDir) + "_DoNotShip");
            if (Directory.Exists(symbols)) Directory.Delete(symbols, true);
            Directory.CreateDirectory(symbols);
            foreach (string dir in Directory.GetDirectories(outputDir, "*DoNotShip*").Concat(Directory.GetDirectories(outputDir, "*DontShip*")))
            {
                Directory.Move(dir, Path.Combine(symbols, Path.GetFileName(dir)));
            }

            Step("Verifying");
            Verify(outputDir, dataDir, pak, key, protectedNames);
            VerifyPackedAssets(report);
            // publish-release.ps1 only ships a build with this stamp, so a failed or unverified build can't go out.
            File.WriteAllText(stamp, File.GetLastWriteTimeUtc(Path.Combine(outputDir, ExeName)).ToString("o"));
            EditorUtility.ClearProgressBar();
            Debug.Log($"[Release] OK: {outputDir} (content pack {new FileInfo(pak).Length / (1024 * 1024)} MB). " +
                      $"Ship that folder only; {symbols} holds IL2CPP's generated sources and must never be published.");
        }

        private static void Step(string what)
        {
            Debug.Log($"[Release] {what}");
            if (!Application.isBatchMode) EditorUtility.DisplayProgressBar("UO Client release", what, 0.5f);
        }

        /// <summary>Copies WorldView's asset fields and the kit registry into a temporary ContentPack asset.</summary>
        private static void CreatePack()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedFolder)) AssetDatabase.CreateFolder("Assets/UOClient", "Generated");

            Scene world = SceneManager.GetSceneByPath(WorldScenePath);
            bool opened = !world.isLoaded;
            if (opened) world = EditorSceneManager.OpenScene(WorldScenePath, OpenSceneMode.Additive);
            try
            {
                WorldView view = world.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WorldView>(true)).FirstOrDefault();
                if (view == null) throw new BuildFailedException($"No WorldView in {WorldScenePath}.");

                var pack = ScriptableObject.CreateInstance<ContentPack>();
                pack.name = "ContentPack";
                var kit = ScriptableObject.CreateInstance<SyntyAssetRegistry>();
                kit.name = "Kit";
                kit.Prefabs = LoadAll<GameObject>("t:Prefab", KitPrefabFolders);
                kit.Materials = LoadAll<Material>("t:Material", KitMaterialFolders);
                AssetDatabase.CreateAsset(pack, PackAssetPath);
                AssetDatabase.AddObjectToAsset(kit, pack);

                var src = new SerializedObject(view);
                var dst = new SerializedObject(pack);
                foreach (string field in ContentPack.WorldViewFields)
                {
                    SerializedProperty p = src.FindProperty(field);
                    if (p == null) throw new BuildFailedException($"WorldView has no field {field}.");
                    dst.CopyFromSerializedProperty(p);
                }

                dst.FindProperty("_kit").objectReferenceValue = kit;
                dst.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                Debug.Log($"[Release] Content pack: {kit.Prefabs.Length} kit prefabs, {kit.Materials.Length} kit materials.");
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(world, true);
            }
        }

        private static T[] LoadAll<T>(string filter, string[] folders) where T : UnityEngine.Object
        {
            var byName = new Dictionary<string, T>();
            foreach (string folder in folders.Where(AssetDatabase.IsValidFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets(filter, new[] { folder }))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                    if (asset != null) byName[asset.name] = asset;
                }
            }

            return byName.Values.ToArray();
        }

        /// <summary>File names of every protected asset the pack pulls in: none may appear in the player's own data.</summary>
        private static string[] ProtectedNames()
        {
            return AssetDatabase.GetDependencies(PackAssetPath, true)
                .Where(IsProtected)
                .Where(p => !p.EndsWith(".cs") && !p.EndsWith(".dll") && !p.EndsWith(".asmdef"))
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => n.Length >= 8)
                .Distinct()
                .ToArray();
        }

        public static bool IsProtected(string assetPath) =>
            ProtectedRoots.Any(r => assetPath.StartsWith(r, StringComparison.OrdinalIgnoreCase));

        /// <summary>Renames every Resources folder under the protected roots, so the player doesn't include them.</summary>
        private static List<(string from, string to)> ExcludeResourcesFolders()
        {
            var moved = new List<(string, string)>();
            foreach (string root in ProtectedRoots)
            {
                string dir = root.TrimEnd('/');
                if (!Directory.Exists(dir)) continue;
                foreach (string found in Directory.GetDirectories(dir, "Resources", SearchOption.AllDirectories))
                {
                    string from = found.Replace('\\', '/');
                    string to = from + ExcludedSuffix;
                    string error = AssetDatabase.MoveAsset(from, to);
                    if (!string.IsNullOrEmpty(error)) throw new BuildFailedException($"Couldn't move {from} aside: {error}");
                    moved.Add((from, to));
                }
            }

            return moved;
        }

        private static void RestoreResourcesFolders(List<(string from, string to)> moved)
        {
            foreach ((string from, string to) in moved)
            {
                MergeRecreated(from, to);
                string error = AssetDatabase.MoveAsset(to, from);
                if (!string.IsNullOrEmpty(error)) Debug.LogError($"[Release] Couldn't restore {from}: {error}. Rename {to} back by hand.");
            }
        }

        /// <summary>
        /// A pre-build hook can recreate a Resources folder while it's moved aside (Sidekick's DatabaseBuildSync
        /// writes Resources/Database/Side_Kick_Data.bytes before every build). Copy what it wrote into the parked
        /// folder, keeping the parked files' .meta (GUIDs), then delete the new folder so the original can move back.
        /// </summary>
        private static void MergeRecreated(string from, string parked)
        {
            if (!AssetDatabase.IsValidFolder(from) && !Directory.Exists(from)) return;

            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string relative = file.Substring(from.Length).TrimStart('/', '\\');
                string target = Path.Combine(parked, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
                if (!File.Exists(target + ".meta") && File.Exists(file + ".meta")) File.Copy(file + ".meta", target + ".meta");
            }

            if (!AssetDatabase.DeleteAsset(from))
            {
                Directory.Delete(from, true);
                if (File.Exists(from + ".meta")) File.Delete(from + ".meta");
            }

            AssetDatabase.Refresh();
            Debug.Log($"[Release] Merged a {from} recreated during the build into the original before restoring it.");
        }

        /// <summary>Puts back Resources folders a crashed release build left renamed.</summary>
        [InitializeOnLoadMethod]
        private static void RecoverAfterCrash()
        {
            EditorApplication.delayCall += () =>
            {
                foreach (string root in ProtectedRoots)
                {
                    string dir = root.TrimEnd('/');
                    if (!Directory.Exists(dir)) continue;
                    foreach (string found in Directory.GetDirectories(dir, "Resources" + ExcludedSuffix, SearchOption.AllDirectories))
                    {
                        string to = found.Replace('\\', '/');
                        string from = to.Substring(0, to.Length - ExcludedSuffix.Length);
                        Debug.LogWarning($"[Release] Restoring {from} left over from an interrupted release build.");
                        MergeRecreated(from, to);
                        string error = AssetDatabase.MoveAsset(to, from);
                        if (!string.IsNullOrEmpty(error)) Debug.LogError($"[Release] Couldn't restore {from}: {error}. Rename {to} back by hand.");
                    }
                }
            };
        }

        private static void EncryptBundle(string bundlePath, string pakPath, byte[] key)
        {
            var nonce = new byte[CtrCipherStream.NonceSize];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(nonce);

            using (ICryptoTransform ecb = CtrCipherStream.CreateEcb(key))
            using (var input = new FileStream(bundlePath, FileMode.Open, FileAccess.Read))
            using (var output = new FileStream(pakPath, FileMode.Create, FileAccess.Write))
            {
                var header = new byte[ProtectedContent.HeaderSize];
                Buffer.BlockCopy(ProtectedContent.Magic, 0, header, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(ProtectedContent.FormatVersion), 0, header, 4, 4);
                Buffer.BlockCopy(nonce, 0, header, 8, nonce.Length);
                output.Write(header, 0, header.Length);

                const int chunk = 64 * 1024;
                var buffer = new byte[chunk];
                var counters = new byte[chunk + 16];
                var keystream = new byte[chunk + 16];
                long position = 0;
                int n;
                while ((n = input.Read(buffer, 0, chunk)) > 0)
                {
                    CtrCipherStream.Xor(ecb, nonce, position, buffer, 0, n, counters, keystream);
                    output.Write(buffer, 0, n);
                    position += n;
                }
            }
        }

        private static void Verify(string outputDir, string dataDir, string pak, byte[] key, string[] protectedNames)
        {
            // 1. The pack decrypts back to an AssetBundle.
            using (var file = new FileStream(pak, FileMode.Open, FileAccess.Read))
            {
                var header = new byte[ProtectedContent.HeaderSize];
                file.Read(header, 0, header.Length);
                var nonce = new byte[CtrCipherStream.NonceSize];
                Buffer.BlockCopy(header, 8, nonce, 0, nonce.Length);
                using (var stream = new CtrCipherStream(file, header.Length, file.Length - header.Length, key, nonce))
                {
                    var sig = new byte[7];
                    stream.Read(sig, 0, sig.Length);
                    if (Encoding.ASCII.GetString(sig) != "UnityFS") throw new BuildFailedException("Content pack doesn't decrypt to an AssetBundle.");
                }
            }

            // 2. Nothing that shouldn't ship is in the build folder.
            foreach (string dir in Directory.GetDirectories(outputDir, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(dir);
                if (name.IndexOf("DoNotShip", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("DontShip", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new BuildFailedException($"{dir} must not ship.");
                }
            }

            // 3. No protected asset name appears in the player's serialized data files.
            var names = new HashSet<string>(protectedNames, StringComparer.Ordinal);
            var hits = new List<string>();
            foreach (string path in Directory.GetFiles(dataDir, "*", SearchOption.TopDirectoryOnly))
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".json" || ext == ".config" || ext == ".info" || ext == ".dll") continue;
                foreach (string run in PrintableRuns(File.ReadAllBytes(path), 8))
                {
                    if (names.Contains(run) || (run.Length > 8 && names.Contains(run.Substring(1))))
                    {
                        hits.Add($"{Path.GetFileName(path)}: {run}");
                    }
                }
            }

            if (hits.Count > 0)
            {
                throw new BuildFailedException($"{hits.Count} protected asset(s) leaked into the player data, e.g.\n" +
                                               string.Join("\n", hits.Take(20)));
            }

            Debug.Log($"[Release] Verified: pack decrypts, no do-not-ship folders, none of {names.Count} protected asset names in player data.");
        }

        /// <summary>
        /// The authoritative check: Unity's own list of every source asset packed into the player's data
        /// files. Any asset from a paid pack there means it shipped readable.
        /// </summary>
        private static void VerifyPackedAssets(BuildReport report)
        {
            var leaked = report.packedAssets
                .SelectMany(file => file.contents.Select(c => (file: Path.GetFileName(file.shortPath), source: c.sourceAssetPath)))
                .Where(x => !string.IsNullOrEmpty(x.source) && IsProtected(x.source))
                .Distinct()
                .ToList();
            if (leaked.Count > 0)
            {
                throw new BuildFailedException($"{leaked.Count} paid asset(s) were packed into the player's readable data, e.g.\n" +
                                               string.Join("\n", leaked.Take(20).Select(x => $"{x.file}: {x.source}")));
            }

            Debug.Log("[Release] Verified: the build report lists no paid-pack assets in the player's data files.");
        }

        private static IEnumerable<string> PrintableRuns(byte[] data, int minLength)
        {
            int start = -1;
            for (int i = 0; i <= data.Length; i++)
            {
                bool printable = i < data.Length && data[i] >= 0x20 && data[i] < 0x7F;
                if (printable)
                {
                    if (start < 0) start = i;
                }
                else if (start >= 0)
                {
                    if (i - start >= minLength) yield return Encoding.ASCII.GetString(data, start, i - start);
                    start = -1;
                }
            }
        }
    }
}
