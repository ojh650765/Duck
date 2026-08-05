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
        /// <summary>
        /// Where the shipped player is written.
        ///
        /// Inside the repository rather than in the ignored Build folder, because this is what
        /// GitHub Pages publishes — the deploy workflow uploads exactly this directory. It is
        /// deliberately not called "docs": Pages only serves a branch root or /docs by itself, but
        /// the Actions deployment used here can publish any path, so the folder can say what it is.
        ///
        /// Resolved from the project rather than written out as C:\Duck\Web, which is what it was.
        /// That literal is correct on exactly one machine, and the GitHub Actions runner that now
        /// runs this same method is a Linux container with no C: drive at all — the build did not
        /// fail there, it wrote a directory called "C:\Duck\Web" into the working folder and
        /// published nothing.
        /// </summary>
        public static string OutputDir =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Web")
                .Replace('\\', Path.DirectorySeparatorChar);

        [MenuItem("Duck/6 · Build WebGL", priority = 5)]
        public static void BuildWebGL() => BuildWebGLCore();

        /// <summary>
        /// The same build, run from a headless editor by the GitHub Actions workflow.
        ///
        /// Separate entry point for one reason: a CI job that "passes" while having produced nothing
        /// is worse than no CI job. BuildPipeline reports failure through a return value, and a
        /// -executeMethod that returns normally is a success as far as Unity's exit code is
        /// concerned, so the failure has to be turned into an exit code by hand.
        /// </summary>
        public static void BuildWebGLForCI()
        {
            bool ok = BuildWebGLCore();
            // Exit(0) as well as Exit(1): -executeMethod leaves the editor running afterwards, and
            // a headless editor that never quits is a job that hangs until the runner times out.
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool BuildWebGLCore()
        {
            // The menu first, then the game. This array — not the build settings window — is what the
            // player actually ships, so shipping the menu means listing it here; it was a one-element
            // array of the game scene, which is why the browser opened on a round.
            var scenes = DuckMenuBuilder.PlayerScenes();

            // Make sure the platform is actually switched, or BuildPlayer silently no-ops.
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                Debug.Log("[Duck] Switching active build target to WebGL...");
                bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.WebGL, BuildTarget.WebGL);
                if (!ok)
                {
                    Debug.LogError("[Duck] Could not switch to WebGL. Is the module installed?");
                    return false;
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
                return false;
            }
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                Debug.LogError("[Duck] WebGL build target is not supported by this editor install.");
                return false;
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

            return s.result == BuildResult.Succeeded;
        }

        [MenuItem("Duck/6 · Build WebGL (development)", priority = 6)]
        public static void BuildWebGLDev()
        {
            var scenes = DuckMenuBuilder.PlayerScenes();
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
