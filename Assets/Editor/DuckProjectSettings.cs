using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Project-wide settings the game depends on, applied from code so a fresh clone or a
    /// settings reset cannot silently change how the game runs or builds.
    /// </summary>
    public static class DuckProjectSettings
    {
        [MenuItem("Duck/0 · Configure project settings", priority = -1)]
        public static void Configure()
        {
            PlayerSettings.productName = "Duck Mow";
            PlayerSettings.companyName = "Duck Mow";

            // Without this the editor stops ticking play mode whenever it loses focus, which
            // makes every unattended capture and timing test silently wrong.
            PlayerSettings.runInBackground = true;

            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

            // Fixed 60 Hz simulation: the mower's handling is tuned in fixed steps and must not
            // change character with frame rate.
            Time.fixedDeltaTime = 1f / 60f;
            var timeAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset");
            foreach (var a in timeAsset)
            {
                var so = new SerializedObject(a);
                var fixedTs = so.FindProperty("Fixed Timestep");
                if (fixedTs != null) fixedTs.floatValue = 1f / 60f;
                var maxTs = so.FindProperty("Maximum Allowed Timestep");
                if (maxTs != null) maxTs.floatValue = 0.1f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            ConfigureWebGL();
            ConfigurePhysics();

            AssetDatabase.SaveAssets();
            Debug.Log("[Duck] Project settings configured.");
        }

        static void ConfigureWebGL()
        {
            var wgl = NamedBuildTarget.WebGL;

            PlayerSettings.SetScriptingBackend(wgl, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(wgl, Il2CppCompilerConfiguration.Master);
            PlayerSettings.SetManagedStrippingLevel(wgl, ManagedStrippingLevel.High);

            // Brotli is much smaller but needs server support; gzip is the safe default for a
            // build that will be opened straight off disk or from a simple static host.
            // Gzipped, with the JavaScript decompression fallback below.
            //
            // A static host like GitHub Pages cannot add the Content-Encoding header that a
            // pre-compressed build needs, and serving one without it hangs the loader. The
            // fallback exists for exactly that case: the loader decompresses in JavaScript
            // instead. Turning compression off altogether also works and was the first thing
            // tried, but it took the player from 24 MB to 61 MB — a much worse deal for someone
            // opening a link than a couple of seconds of decompression.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
            PlayerSettings.WebGL.memorySize = 512;
            // Our own page rather than Unity's default. The default ships no translation hints,
            // so a browser set to any language but English puts a translate dialog over the canvas
            // the moment the page loads, and it routes every runtime warning to a coloured bar
            // across the game. Both are developer defaults showing through to the player.
            PlayerSettings.WebGL.template = "PROJECT:DuckMow";
            PlayerSettings.WebGL.powerPreference = WebGLPowerPreference.HighPerformance;

            // 60 fps needs vsync off in the player and the frame rate uncapped.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.stripUnusedMeshComponents = true;
        }

        static void ConfigurePhysics()
        {
            Physics.defaultSolverIterations = 8;
            Physics.defaultSolverVelocityIterations = 2;
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
            Physics.queriesHitBackfaces = false;

            // The mower must not collide with itself; the grass must not collide with anything.
            int mower = LayerMask.NameToLayer("Mower");
            int grass = LayerMask.NameToLayer("Grass");
            if (mower >= 0) Physics.IgnoreLayerCollision(mower, mower, true);
            if (grass >= 0)
                for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(grass, i, true);
        }
    }
}
