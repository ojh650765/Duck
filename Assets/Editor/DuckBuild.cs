using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Builds the WebGL player and reports exactly what happened.
    ///
    /// Driving BuildPipeline directly rather than through a wrapper, because a wrapper that
    /// reports "build succeeded, 47 MB" while writing nothing to disk costs far more time than
    /// it saves. Everything here is logged from the BuildReport itself: the resolved output path,
    /// the result enum, the total size, and every error step.
    /// </summary>
    public static class DuckBuild
    {
        public const string OutputDir = @"C:\Duck\Build\WebGL";

        [MenuItem("Duck/6 · Build WebGL", priority = 5)]
        public static void BuildWebGL()
        {
            var scenes = new[] { DuckSceneBuilder.ScenePath };

            // Make sure the platform is actually switched, or BuildPlayer silently no-ops.
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                Debug.Log("[Duck] Switching active build target to WebGL...");
                bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.WebGL, BuildTarget.WebGL);
                if (!ok)
                {
                    Debug.LogError("[Duck] Could not switch to WebGL. Is the module installed?");
                    return;
                }
            }

            Directory.CreateDirectory(OutputDir);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputDir,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None
            };

            Debug.Log($"[Duck] Building WebGL to {OutputDir} ... " +
                      $"activeTarget={EditorUserBuildSettings.activeBuildTarget} " +
                      $"compiling={EditorApplication.isCompiling} " +
                      $"updating={EditorApplication.isUpdating} " +
                      $"playing={EditorApplication.isPlayingOrWillChangePlaymode} " +
                      $"webglSupported={BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL)}");

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Debug.LogError("[Duck] Editor is busy compiling or importing; BuildPlayer would " +
                               "return Unknown without doing anything. Try again when idle.");
                return;
            }
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                Debug.LogError("[Duck] WebGL build target is not supported by this editor install.");
                return;
            }
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;

            var sb = new StringBuilder("[Duck] WEBGL BUILD REPORT\n");
            sb.AppendLine($"  result       = {s.result}");
            sb.AppendLine($"  outputPath   = {s.outputPath}");
            sb.AppendLine($"  totalSize    = {s.totalSize / (1024f * 1024f):0.0} MB");
            sb.AppendLine($"  totalTime    = {s.totalTime}");
            sb.AppendLine($"  errors       = {s.totalErrors}, warnings = {s.totalWarnings}");
            sb.AppendLine($"  exists on disk = {Directory.Exists(s.outputPath)}");

            if (Directory.Exists(s.outputPath))
            {
                long bytes = 0;
                int files = 0;
                foreach (var f in Directory.GetFiles(s.outputPath, "*", SearchOption.AllDirectories))
                {
                    bytes += new FileInfo(f).Length;
                    files++;
                }
                sb.AppendLine($"  on disk      = {files} files, {bytes / (1024f * 1024f):0.0} MB");
                foreach (var f in Directory.GetFiles(s.outputPath, "*", SearchOption.TopDirectoryOnly))
                    sb.AppendLine($"    {Path.GetFileName(f)}");
            }

            foreach (var step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        sb.AppendLine($"  [{step.name}] {msg.type}: {msg.content}");
                }
            }

            if (s.result == BuildResult.Succeeded) Debug.Log(sb.ToString());
            else Debug.LogError(sb.ToString());
        }

        [MenuItem("Duck/6 · Build WebGL (development)", priority = 6)]
        public static void BuildWebGLDev()
        {
            var scenes = new[] { DuckSceneBuilder.ScenePath };
            Directory.CreateDirectory(OutputDir + "_Dev");
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputDir + "_Dev",
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.Development
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[Duck] dev build {report.summary.result} -> {report.summary.outputPath}");
        }
    }
}
