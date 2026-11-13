using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UOClient.EditorTools
{
    /// <summary>
    /// The dedicated server for Linux (the hosted shard's container, Deploy/Shard): the Game scene as a headless
    /// StandaloneLinux64 Server build. Mono, so the Windows build machine needs only Unity's Linux Build Support module
    /// (IL2CPP for Linux would also need the Linux toolchain package). The baked world isn't in the build: the shard
    /// reads it from <c>BR_WORLD_DIR</c>. The Windows server build is still Tools/Game/BuildServer.eval.cs.
    /// <para>Batch mode: <c>-executeMethod UOClient.EditorTools.ServerBuilder.BuildLinuxFromCommandLine [-buildOutput dir]</c>
    /// (best started with <c>-buildTarget Linux64 -standaloneBuildSubtarget Server</c>, so the editor opens on the right
    /// target and doesn't switch).</para>
    /// </summary>
    public static class ServerBuilder
    {
        public const string ExeName = "UOGameServer.x86_64";
        public const string DefaultLinuxOutput = "Builds/ServerLinux/" + ExeName;
        private const string GameScene = "Assets/UOClient/Scenes/Game.unity";

        /// <summary>Batch-mode entry. <c>-buildOutput</c> is a folder (the exe goes in it) or the exe's path. Exits 1 on failure.</summary>
        public static void BuildLinuxFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-buildOutput");
            string output = DefaultLinuxOutput;
            if (i >= 0 && i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]))
            {
                output = args[i + 1].EndsWith(".x86_64", StringComparison.OrdinalIgnoreCase) ? args[i + 1] : Path.Combine(args[i + 1], ExeName);
            }

            bool ok;
            try
            {
                ok = BuildLinux(output);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Server] Linux build FAILED: {e.Message}\n{e}");
                ok = false;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(ok ? 0 : 1);
            }
        }

        /// <summary>Builds the Linux server to <paramref name="exePath"/>. True when it succeeded.</summary>
        public static bool BuildLinux(string exePath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[Server] Leave play mode first.");
                return false;
            }

            exePath = Path.GetFullPath(exePath);
            string folder = Path.GetDirectoryName(exePath);
            Directory.CreateDirectory(folder);

            NamedBuildTarget server = NamedBuildTarget.Server;
            ScriptingImplementation backend = PlayerSettings.GetScriptingBackend(server);
            BuildReport report;
            try
            {
                PlayerSettings.SetScriptingBackend(server, ScriptingImplementation.Mono2x);
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { GameScene },
                    locationPathName = exePath,
                    target = BuildTarget.StandaloneLinux64,
                    targetGroup = BuildTargetGroup.Standalone,
                    subtarget = (int)StandaloneBuildSubtarget.Server,
                    options = BuildOptions.None,
                });
            }
            finally
            {
                if (PlayerSettings.GetScriptingBackend(server) != backend)
                {
                    PlayerSettings.SetScriptingBackend(server, backend);
                }
            }

            BuildSummary s = report.summary;
            if (s.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[Server] Linux build {s.result}: {s.totalErrors} errors, {s.totalWarnings} warnings.");
                return false;
            }

            // Never ship generated sources or debug symbols (none with Mono, but Burst can leave its own).
            foreach (string dir in Directory.GetDirectories(folder, "*DoNotShip*"))
            {
                Directory.Delete(dir, true);
            }

            foreach (string dir in Directory.GetDirectories(folder, "*DontShip*"))
            {
                Directory.Delete(dir, true);
            }

            Debug.Log($"[Server] Linux build Succeeded: {s.totalSize / 1048576.0:0} MB in {s.totalTime.TotalSeconds:0}s, {s.totalWarnings} warnings -> {s.outputPath}");
            return true;
        }
    }
}
