// Builds the headless dedicated server (Windows, Server subtarget) to Builds/Server/UOGameServer.exe.
// The baked world isn't part of the build: copy WorldData/<facet> next to the exe (see Docs/ServerRoadmap.md).
var options = new UnityEditor.BuildPlayerOptions
{
    scenes = new[] { "Assets/UOClient/Scenes/Game.unity" },
    locationPathName = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "Builds", "Server", "UOGameServer.exe")),
    target = UnityEditor.BuildTarget.StandaloneWindows64,
    subtarget = (int)UnityEditor.StandaloneBuildSubtarget.Server,
    options = UnityEditor.BuildOptions.None,
};
var report = UnityEditor.BuildPipeline.BuildPlayer(options);
var s = report.summary;
return $"{s.result}: {s.totalSize / 1048576.0:0} MB in {s.totalTime.TotalSeconds:0}s, {s.totalErrors} errors, {s.totalWarnings} warnings -> {s.outputPath}";
