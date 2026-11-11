using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UOClient.EditorTools
{
    /// <summary>
    /// A playtest client for friends (Docs/Playtest.md): the Game scene as an ordinary Windows player, with Trammel's baked
    /// world beside it. Its paid art is in Unity's normal data files, NOT encrypted: until the secure-art pipeline
    /// (Docs/SecureArt.md) is finished, this build goes only to the private patch host on phoenix-server and never to a
    /// public place. <see cref="ReleaseBuildGuard"/> lets it through only while <see cref="Active"/>.
    /// </summary>
    public static class PlaytestBuilder
    {
        public const string Output = "Builds/Playtest/UOGame";
        public const string ExeName = "UOGame.exe";
        private const string GameScene = "Assets/UOClient/Scenes/Game.unity";
        private const int Facet = 1; // Trammel: the world that's baked and served

        public static bool Active { get; private set; }

        [MenuItem("UO Client/Release/Build Playtest (friends only, art not encrypted)")]
        public static void BuildMenu() => Build();

        public static string Build()
        {
            string output = Path.GetFullPath(Output);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return "Leave play mode first.";
            }

            if (Directory.Exists(output))
            {
                Directory.Delete(output, true);
            }

            Directory.CreateDirectory(output);
            BuildReport report;
            Active = true;
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { GameScene },
                    locationPathName = Path.Combine(output, ExeName),
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    subtarget = (int)StandaloneBuildSubtarget.Player, // a game with a screen, not the headless server
                    options = BuildOptions.CompressWithLz4HC, // the art packs' data, compressed (several GB otherwise)
                });
            }
            finally
            {
                Active = false;
            }

            if (report.summary.result != BuildResult.Succeeded)
            {
                return $"Playtest build {report.summary.result} ({report.summary.totalErrors} errors).";
            }

            // Never ship IL2CPP's generated sources or debug symbols.
            foreach (string dir in Directory.GetDirectories(output, "*DoNotShip*"))
            {
                Directory.Delete(dir, true);
            }

            foreach (string dir in Directory.GetDirectories(output, "*DontShip*"))
            {
                Directory.Delete(dir, true);
            }

            // The baked world the client loads (GameWorld.WorldFolder: WorldData beside the exe).
            string world = Path.GetFullPath(Path.Combine("WorldData", Facet.ToString()));
            string target = Path.Combine(output, "WorldData", Facet.ToString());
            CopyFolder(world, target);
            long bytes = 0;
            foreach (string f in Directory.GetFiles(output, "*", SearchOption.AllDirectories))
            {
                bytes += new FileInfo(f).Length;
            }

            string done = $"Playtest build OK: {output} ({bytes / (1024 * 1024)} MB with the world).";
            Debug.Log("[Release] " + done);
            return done;
        }

        private static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            }

            foreach (string dir in Directory.GetDirectories(from))
            {
                CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
            }
        }
    }
}
