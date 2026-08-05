using UnityEditor;
using UnityEngine;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// One entry point that takes the project from source assets to a shipped WebGL build.
    ///
    /// Exists so the whole thing can be driven headlessly:
    ///
    ///   Unity.exe -batchmode -quit -projectPath C:\Duck ^
    ///             -executeMethod DuckMow.EditorTools.DuckPipeline.FullBuild ^
    ///             -logFile C:\Duck\Logs\pipeline.log
    ///
    /// Batch mode turned out to be far more dependable than driving the editor interactively:
    /// it cannot be in play mode, cannot be mid-compile, and cannot silently refuse a build.
    /// </summary>
    public static class DuckPipeline
    {
        [MenuItem("Duck/7 · Full pipeline (assets, scene, WebGL)", priority = 7)]
        public static void FullBuild()
        {
            Debug.Log("[Duck] ===== PIPELINE START =====");

            DuckProjectSettings.Configure();
            DuckModelIntegration.IntegrateAll();
            DuckSceneBuilder.BuildMaterials();
            DuckUIBuilder.ImportSprites();
            // The menu first and the game scene second, so the editor is left on the game scene —
            // that is the one anybody who runs this then wants to look at.
            DuckMenuBuilder.BuildMenuScene();
            DuckSceneBuilder.BuildScene();
            AssetDatabase.SaveAssets();

            DuckBuild.BuildWebGL();

            Debug.Log("[Duck] ===== PIPELINE END =====");
        }

        /// <summary>Everything except the player build, for a quick content refresh.</summary>
        [MenuItem("Duck/7 · Rebuild content only", priority = 8)]
        public static void ContentOnly()
        {
            DuckModelIntegration.IntegrateAll();
            DuckSceneBuilder.BuildMaterials();
            DuckUIBuilder.ImportSprites();
            DuckMenuBuilder.BuildMenuScene();
            DuckSceneBuilder.BuildScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[Duck] Content rebuilt.");
        }
    }
}
