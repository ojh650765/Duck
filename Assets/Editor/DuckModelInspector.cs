using System.Text;
using System.IO;
using UnityEditor;
using UnityEngine;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// In-engine inspection of the imported Blender assets: node transforms, mirrored scales,
    /// triangle winding, and rendered turnarounds from the actual game shaders.
    ///
    /// A model can look perfect in Blender and arrive inside-out in Unity, because the FBX axis
    /// conversion can leave a negative scale on the root — which flips triangle winding, so back
    /// face culling hides the surfaces you are meant to see and the object reads as transparent.
    /// This is the tool that tells you that has happened.
    /// </summary>
    public static class DuckModelInspector
    {
        const string OutDir = "Captures/Models";

        [MenuItem("Duck/Diagnose · Model transforms", priority = 44)]
        public static void ReportTransforms()
        {
            string[] files = { "Duck.fbx", "Mower.fbx", "Judges.fbx", "Spectators.fbx", "Props.fbx", "Landmarks.fbx", "Foliage.fbx" };
            var sb = new StringBuilder("[Duck] MODEL TRANSFORM REPORT\n");

            foreach (var f in files)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Art/Models/{f}");
                if (asset == null) { sb.AppendLine($"  {f}: MISSING"); continue; }

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                sb.AppendLine($"  --- {f} ---");
                Walk(inst.transform, sb, 0);
                Object.DestroyImmediate(inst);
            }
            Debug.Log(sb.ToString());
        }

        static void Walk(Transform t, StringBuilder sb, int depth)
        {
            if (depth > 2) return;
            string pad = new string(' ', 4 + depth * 2);

            Vector3 ls = t.localScale;
            bool mirrored = ls.x * ls.y * ls.z < 0f;
            var mf = t.GetComponent<MeshFilter>();
            string meshInfo = "";
            if (mf != null && mf.sharedMesh != null)
            {
                var m = mf.sharedMesh;
                meshInfo = $" mesh[v={m.vertexCount} t={m.triangles.Length / 3} colors={m.colors32.Length > 0} " +
                           $"normals={m.normals.Length > 0} bounds={m.bounds.size}]";
            }

            sb.AppendLine($"{pad}{t.name} pos={V(t.localPosition)} rot={V(t.localEulerAngles)} scale={V(ls)}" +
                          (mirrored ? "  <<< MIRRORED SCALE" : "") + meshInfo);

            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), sb, depth + 1);
        }

        static string V(Vector3 v) => $"({v.x:0.###},{v.y:0.###},{v.z:0.###})";

        /// <summary>
        /// Renders a turnaround of a prefab or model with the real game material and lighting,
        /// so what we judge is what the player sees rather than a Blender viewport.
        /// </summary>
        [MenuItem("Duck/Diagnose · Render model turnarounds", priority = 45)]
        public static void RenderTurnarounds()
        {
            Directory.CreateDirectory(OutDir);

            // Stage in a scratch scene: rendering into the live world put the game's lawn at the
            // same height as the stage floor and the two z-fought into stripes.
            var prev = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            var stageScene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = DuckSceneBuilder.Hex("#8CC0E8");
            RenderSettings.ambientEquatorColor = DuckSceneBuilder.Hex("#A9C79A");
            RenderSettings.ambientGroundColor = DuckSceneBuilder.Hex("#4C6B44");
            RenderSettings.fog = false;

            RenderOne(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mower.prefab"), "mower_rig");
            RenderOne(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Models/Duck.fbx"), "duck", "M_Duck");
            RenderOne(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Models/Mower.fbx"), "mower", "M_Mower");
            RenderOne(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Models/Judges.fbx"), "judges", "M_Judges");

            Debug.Log($"[Duck] Turnarounds written to {OutDir}/");

            if (!string.IsNullOrEmpty(prev))
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(prev);
        }

        static void RenderOne(GameObject asset, string name, string materialName = null)
        {
            if (asset == null) { Debug.LogWarning($"[Duck] turnaround: {name} asset missing"); return; }

            var stage = new GameObject($"__stage_{name}");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            inst.transform.SetParent(stage.transform, false);
            inst.transform.localPosition = Vector3.zero;

            if (materialName != null)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/{materialName}.mat");
                if (mat != null)
                    foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                        for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                        r.sharedMaterials = mats;
                    }
            }

            // Ground plane so the model is not floating in a void when we judge its footing.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.SetParent(stage.transform, false);
            ground.transform.localScale = Vector3.one * 2f;
            var groundMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Apron.mat");
            if (groundMat != null) ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;

            var bounds = new Bounds(Vector3.zero, Vector3.one);
            bool first = true;
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
            }
            float radius = Mathf.Max(bounds.extents.magnitude, 0.4f);

            var lightGO = new GameObject("__stageLight");
            lightGO.transform.SetParent(stage.transform, false);
            lightGO.transform.rotation = Quaternion.Euler(46f, -38f, 0f);
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = DuckSceneBuilder.Hex("#FFF1CE");
            light.intensity = 1.6f;
            light.shadows = LightShadows.Soft;

            var camGO = new GameObject("__stageCam");
            camGO.transform.SetParent(stage.transform, false);
            var cam = camGO.AddComponent<Camera>();
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = DuckSceneBuilder.Hex("#8FB6CE");

            // The models face +Z, so a "front" view puts the camera on the +Z side looking back.
            float[] yaws = { 0f, 40f, 90f, 180f };
            float[] pitches = { 8f, 18f, 8f, 18f };
            string[] tags = { "front", "q34", "side", "back" };
            for (int i = 0; i < yaws.Length; i++)
            {
                float dist = radius / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.35f;
                var dir = Quaternion.Euler(pitches[i], yaws[i], 0f) * Vector3.forward;
                camGO.transform.position = bounds.center + dir * dist;
                camGO.transform.rotation = Quaternion.LookRotation(bounds.center - camGO.transform.position, Vector3.up);
                Capture(cam, $"{name}_{tags[i]}", 900, 900);
            }

            // A tight shot of the driving position: is the duck on the seat, are its wings on the
            // wheel, are its eyes readable. This is the frame that answers the actual complaints.
            var seatFocus = bounds.center + new Vector3(0f, bounds.extents.y * 0.55f, bounds.extents.z * 0.15f);
            var closeDir = Quaternion.Euler(12f, 28f, 0f) * Vector3.forward;
            camGO.transform.position = seatFocus + closeDir * (radius * 1.25f);
            camGO.transform.rotation = Quaternion.LookRotation(seatFocus - camGO.transform.position, Vector3.up);
            cam.fieldOfView = 22f;
            Capture(cam, $"{name}_driver", 900, 900);
            cam.fieldOfView = 38f;

            Object.DestroyImmediate(stage);
        }

        static void Capture(Camera cam, string name, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply(false);
            cam.targetTexture = null;
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        /// <summary>Dumps the live cut mask render texture so we can see what is actually landing in it.</summary>
        [MenuItem("Duck/Diagnose · Cut mask", priority = 46)]
        public static void DumpCutMask()
        {
            var mask = Object.FindFirstObjectByType<CutMask>();
            if (mask == null) { Debug.LogWarning("[Duck] No CutMask (enter play mode)."); return; }

            Debug.Log($"[Duck] CutMask CPU grid: {mask.CutCellCount} / {Field.GridRes * Field.GridRes} cells cut " +
                      $"({100f * mask.CutCellCount / (Field.GridRes * Field.GridRes):0.0}%)");

            var rt = mask.MaskTexture;
            if (rt == null) { Debug.LogWarning("[Duck] mask RT is null"); return; }

            Directory.CreateDirectory(OutDir);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;

            File.WriteAllBytes(Path.Combine(OutDir, "cutmask.png"), tex.EncodeToPNG());

            // Count non-zero red so we know whether the GPU path wrote anything at all.
            var px = tex.GetPixels32();
            int lit = 0;
            for (int i = 0; i < px.Length; i += 7) if (px[i].r > 40) lit++;
            Debug.Log($"[Duck] CutMask GPU: sampled {px.Length / 7} texels, {lit} have cut > 0.15. Written to {OutDir}/cutmask.png");
            Object.DestroyImmediate(tex);
        }
    }
}
