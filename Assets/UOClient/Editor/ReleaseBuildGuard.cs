using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;
using UOClient.Content;

namespace UOClient.EditorTools
{
    /// <summary>
    /// Keeps paid art out of the player's readable data files. Any player build that isn't the release
    /// pipeline fails, and during a release build every <see cref="WorldView"/> has its asset fields
    /// cleared, so the scene drags none of the Synty/BK content into sharedassets; the player gets it
    /// from the encrypted content pack instead.
    /// </summary>
    /// <summary>
    /// Runs after every other pre-build hook. Synty's DatabaseBuildSync (order 0) copies the Sidekick
    /// database back into Resources/Database before each build, which would ship it in resources.assets;
    /// during a release build any Resources folder under the paid packs is such a regenerated copy (the
    /// originals are parked aside), so delete it.
    /// </summary>
    public sealed class ReleaseResourcesSweeper : IPreprocessBuildWithReport
    {
        public int callbackOrder => 10000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!ReleaseBuilder.Active) return;
            foreach (string root in ReleaseBuilder.ProtectedRoots)
            {
                string dir = root.TrimEnd('/');
                if (!System.IO.Directory.Exists(dir)) continue;
                foreach (string found in System.IO.Directory.GetDirectories(dir, "Resources", System.IO.SearchOption.AllDirectories))
                {
                    string path = found.Replace('\\', '/');
                    UnityEngine.Debug.Log($"[Release] Removing {path}, recreated by another pre-build hook.");
                    if (!AssetDatabase.DeleteAsset(path)) throw new BuildFailedException($"Couldn't remove {path}.");
                }
            }
        }
    }

    public sealed class ReleaseBuildGuard : IPreprocessBuildWithReport, IProcessSceneWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            // A dedicated server build is never handed to players (it runs on our own server), so it may carry the
            // art it needs for world collision as-is. Client builds still have to go through the release pipeline.
            if (report.summary.GetSubtarget<StandaloneBuildSubtarget>() == StandaloneBuildSubtarget.Server)
            {
                UnityEngine.Debug.Log("[Release] Dedicated server build: not for distribution; the release guard doesn't apply.");
                return;
            }

            if (PlaytestBuilder.Active)
            {
                UnityEngine.Debug.LogWarning("[Release] Playtest build: the paid art is in ordinary Unity data files (not encrypted). " +
                                             "For friends' playtests from the private patch host only; never publish it.");
                return;
            }

            if (!ReleaseBuilder.Active)
            {
                throw new BuildFailedException(
                    "Build the client with UO Client > Release > Build. A plain Unity build would ship the paid " +
                    "Synty/BK art in readable data files.");
            }
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null || !BuildPipeline.isBuildingPlayer)
            {
                return; // entering play mode, not building
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (WorldView view in root.GetComponentsInChildren<WorldView>(true))
                {
                    var so = new SerializedObject(view);
                    foreach (string field in ContentPack.WorldViewFields)
                    {
                        SerializedProperty p = so.FindProperty(field);
                        if (p == null)
                        {
                            throw new BuildFailedException($"WorldView has no field {field}; update ContentPack.WorldViewFields.");
                        }

                        if (p.isArray)
                        {
                            p.arraySize = 0;
                        }
                        else
                        {
                            p.objectReferenceValue = null;
                        }
                    }

                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }
    }
}
