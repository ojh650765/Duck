using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Imports the authored goose and builds the animator that drives it.
    ///
    /// The greybox this replaces was a capsule with two cubes for wings, rotated in code. The player's
    /// verdict was "거위 날개짓이 너무 사각형팔 날개짓임. 나는 스키닝을 하나의 스킨 메시로 만드는 걸
    /// 원했는데" — and they were right twice over. Rotating separate boxes is a hinge no amount of
    /// easing hides, and the fix is not a better curve but a continuous membrane whose weights hand over
    /// along a SWEPT line, so the trailing edge follows the outer bone before the leading edge does and
    /// the wing bends on a diagonal.
    ///
    /// Deliberately mirrors <see cref="DuckJudgeRig"/> rather than inventing an import path. That file
    /// records, from experience, the one failure worth fearing here: an FBX that lands with
    /// animationType None imports its clips as nothing at all, silently — the prefab builds, the animal
    /// appears, and never moves.
    ///
    /// NOT usable through DuckAssetLibrary. That walks MeshFilters and this is a SkinnedMeshRenderer, so
    /// GetPieces returns an empty list and the caller falls back to the greybox without saying why.
    /// </summary>
    public static class DuckGooseRig
    {
        const string ModelDir = "Assets/Art/Models";
        public const string ModelPath = ModelDir + "/Goose.fbx";
        public const string PrefabPath = "Assets/Prefabs/Goose.prefab";
        const string ControllerPath = "Assets/Animations/GooseController.controller";

        /// <summary>Clip names as authored. Only the two continuous ones loop.</summary>
        static readonly Dictionary<string, bool> Loops = new()
        {
            { "Fly", true },
            { "Glide", true },
            { "Struck", false },
        };

        [MenuItem("Duck/4 · Import + rig the goose", priority = 4)]
        public static void Build()
        {
            if (!ConfigureImport()) return;
            var controller = BuildController();
            BuildPrefab(controller);
            AssetDatabase.SaveAssets();
        }

        static bool ConfigureImport()
        {
            var imp = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (imp == null)
            {
                Debug.LogError($"[Duck] Goose model missing: {ModelPath}. Run Art/Blender/build_goose.py.");
                return false;
            }

            imp.animationType = ModelImporterAnimationType.Generic;
            imp.importAnimation = true;
            // The phase reaches into the hierarchy, so the bones have to survive as GameObjects.
            imp.optimizeGameObjects = false;
            // The auto-smooth split normals are baked in the file; recalculating flattens the breast.
            imp.importNormals = ModelImporterNormals.Import;
            imp.importBlendShapes = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.meshCompression = ModelImporterMeshCompression.Off;

            var clips = imp.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                bool loop = Loops.TryGetValue(clips[i].name, out bool l) && l;
                clips[i].loopTime = loop;
                clips[i].loop = loop;
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();

            var found = new List<string>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__")) found.Add(c.name);

            // Asserted, for the reason in DuckJudgeRig's own comment: a rig with no clips is a rig that
            // silently never moves, and that is the most expensive symptom to debug backwards.
            foreach (var want in Loops.Keys)
                if (!found.Contains(want))
                    Debug.LogError($"[Duck] Goose.fbx has no clip named '{want}'. Found: " +
                                   (found.Count > 0 ? string.Join(", ", found) : "none"));

            Debug.Log($"[Duck] goose imported: clips [{string.Join(", ", found)}].");
            return true;
        }

        /// <summary>
        /// Fly and Glide on a blend, Struck as a one-shot over the top.
        ///
        /// A blend rather than two states, because the anticipation pose the phase already computes is a
        /// CONTINUOUS value: the wings flare and the beat stills as the bird commits, and a hard switch
        /// between flapping and gliding would step through that instead of easing across it. `Struck` sits
        /// on its own layer so a hit reads over whatever the wings were doing.
        /// </summary>
        static AnimatorController BuildController()
        {
            EnsureFolder("Assets/Animations");
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__")) clips[c.name] = c;

            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ctrl != null) AssetDatabase.DeleteAsset(ControllerPath);
            ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            ctrl.AddParameter("Settle", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Struck", AnimatorControllerParameterType.Trigger);

            // ---- layer 0: the wingbeat, blended against the held glide ----
            var tree = new BlendTree { name = "Air", blendParameter = "Settle", hideFlags = HideFlags.None };
            AssetDatabase.AddObjectToAsset(tree, ctrl);
            if (clips.TryGetValue("Fly", out var fly)) tree.AddChild(fly, 0f);
            if (clips.TryGetValue("Glide", out var glide)) tree.AddChild(glide, 1f);

            var root = ctrl.layers[0].stateMachine;
            var air = root.AddState("Air");
            air.motion = tree;
            root.defaultState = air;

            // ---- layer 1: the strike, over the top ----
            if (clips.TryGetValue("Struck", out var struckClip))
            {
                ctrl.AddLayer("Strike");
                var layers = ctrl.layers;
                var strike = layers[1];
                strike.defaultWeight = 1f;
                strike.blendingMode = AnimatorLayerBlendingMode.Override;
                ctrl.layers = layers;

                var sm = ctrl.layers[1].stateMachine;
                var idle = sm.AddState("None");
                var hit = sm.AddState("Struck");
                hit.motion = struckClip;
                sm.defaultState = idle;

                var go = idle.AddTransition(hit);
                go.AddCondition(AnimatorConditionMode.If, 0f, "Struck");
                go.hasExitTime = false;
                go.duration = 0.04f;

                var back = hit.AddTransition(idle);
                back.hasExitTime = true;
                back.exitTime = 0.95f;
                back.duration = 0.18f;
            }

            AssetDatabase.SaveAssets();
            return ctrl;
        }

        static void BuildPrefab(AnimatorController ctrl)
        {
            EnsureFolder("Assets/Prefabs");
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (src == null) { Debug.LogError("[Duck] goose model would not load."); return; }

            // A plain root with the model UNPACKED under it.
            //
            // Instantiating the FBX as a prefab instance and adding an Animator to it does not stick: the
            // instance's component set is governed by the imported asset, so the add is refused and every
            // later line runs against null — which is what "no 'Animator' attached to the Goose game
            // object" was reporting. Unpacking makes it an ordinary hierarchy this builder owns.
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
            inst.name = "Goose";

            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            if (anim == null)
            {
                Debug.LogError("[Duck] could not attach an Animator to the goose; prefab not written.");
                Object.DestroyImmediate(inst);
                return;
            }
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            // Always animate: the bird spends most of its life off screen behind the player, and a
            // culled animator would hand the phase a frozen goose the moment the camera turned away.
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Two slots by design — the bill is its own submesh so a strike flash can recolour the body
            // and leave the one bright edge that states the bird's heading untouched.
            var body = CharacterMat("M_Goose", "#726C69");
            var bill = CharacterMat("M_Goose_Bill", "#E8873A");
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = i == 0 ? body : bill;
                smr.sharedMaterials = mats;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                smr.receiveShadows = true;
                // The bird is thrown fifty metres and the bounds are authored around the bind pose;
                // without this a launched goose vanishes when its bind-pose box leaves the frustum.
                smr.updateWhenOffscreen = true;
            }

            PrefabUtility.SaveAsPrefabAsset(inst, PrefabPath);
            Object.DestroyImmediate(inst);
            Debug.Log($"[Duck] wrote {PrefabPath}.");
        }

        /// <summary>
        /// The authored-character material: white base, vertex colours on and read as sRGB.
        ///
        /// Matching DuckModelIntegration rather than EnsureLit, because the paint lives in the MESH for
        /// every authored asset in this project. Tinting the base here would multiply every vertex colour
        /// by that tint and flatten the whole bird to one shade.
        /// </summary>
        static Material CharacterMat(string name, string fallbackHex)
        {
            string path = $"Assets/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var sh = Shader.Find("Duck/Prop") ?? Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) return null;
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_VertexColorAmount")) m.SetFloat("_VertexColorAmount", 1f);
            if (m.HasProperty("_VertexColorSRGB")) m.SetFloat("_VertexColorSRGB", 1f);
            m.EnableKeyword("_VCOL_SRGB");
            EditorUtility.SetDirty(m);
            return m;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int cut = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, cut), path.Substring(cut + 1));
        }
    }
}
