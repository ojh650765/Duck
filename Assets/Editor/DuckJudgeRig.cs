using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Import settings and animator controllers for the three skinned judges.
    ///
    /// The judges used to ship as a transform hierarchy — Body, Head, Arm_L, Arm_R as separate
    /// meshes — and every movement they made was generated at runtime from Perlin noise. That
    /// reads as drifting rather than as behaving: noise has no anticipation, no accent and no
    /// settle, and a curve that deliberately holds still for a beat is something it cannot
    /// produce at any amplitude.
    ///
    /// They now arrive from Blender as one skinned mesh per judge over a nine-bone rig, with four
    /// authored clips each. This file is the bridge: it tells Unity how to import them and builds
    /// the state machine that plays them.
    ///
    /// One file per judge, not one shared Judges.fbx. Blender's FBX exporter writes every
    /// animation take onto every armature in the file, so three rigs in one file produced
    /// thirty-six clips of which twenty-four were one judge's performance driving another's
    /// skeleton — and because all three rigs share bone names, those imported without complaint.
    /// </summary>
    public static class DuckJudgeRig
    {
        public static readonly string[] Names = { "Mildred", "Boris", "Priscilla" };

        const string ModelDir = "Assets/Art/Models";
        const string ControllerDir = "Assets/Settings/Animators";

        /// <summary>The idle clip's suffix differs per judge — it carries their temperament.</summary>
        static readonly Dictionary<string, string> IdleSuffix = new Dictionary<string, string>
        {
            { "Mildred", "Idle_Severe" },
            { "Boris", "Idle_Boisterous" },
            { "Priscilla", "Idle_Aloof" },
        };

        public static string ModelPath(string judge) => $"{ModelDir}/Judge_{judge}.fbx";

        /// <summary>
        /// Force the rig and clip settings the controllers depend on.
        ///
        /// Asserted rather than assumed: an FBX that lands with animationType None imports its
        /// clips as nothing at all, and the failure is silent — the prefab builds, the judge
        /// appears, and simply never moves. That is a very expensive thing to debug from the
        /// symptom.
        /// </summary>
        public static void ConfigureImport(string judge)
        {
            string path = ModelPath(judge);
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null)
            {
                Debug.LogWarning($"[Duck] Judge model missing: {path}");
                return;
            }

            imp.animationType = ModelImporterAnimationType.Generic;
            imp.importAnimation = true;
            imp.optimizeGameObjects = false;   // the card is parented to a bone by name
            imp.importNormals = ModelImporterNormals.Import;
            imp.importBlendShapes = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.meshCompression = ModelImporterMeshCompression.Off;

            // Only the idle loops. Lean holds its end pose, and the two gestures play once and
            // hand back — looping either of those turns a judge into a machine.
            string idle = IdleSuffix[judge];
            var clips = imp.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                bool isIdle = clips[i].name.EndsWith(idle);
                clips[i].loopTime = isIdle;
                clips[i].loop = isIdle;
            }
            imp.clipAnimations = clips;

            imp.SaveAndReimport();
        }

        /// <summary>Every animation clip inside one judge's model file.</summary>
        static Dictionary<string, AnimationClip> Clips(string judge)
        {
            var found = new Dictionary<string, AnimationClip>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ModelPath(judge)))
            {
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    found[clip.name] = clip;
            }
            return found;
        }

        static AnimationClip Match(Dictionary<string, AnimationClip> clips, string suffix, string judge)
        {
            foreach (var kv in clips)
                if (kv.Key.EndsWith(suffix)) return kv.Value;
            Debug.LogWarning($"[Duck] {judge}: no clip ending '{suffix}'. Found: " +
                             string.Join(", ", clips.Keys));
            return null;
        }

        /// <summary>
        /// The state machine. Deliberately small.
        ///
        /// Posture is a one-dimensional blend between the judge's idle and their lean, driven by
        /// the Attention float the panel already sets while a judge is studying the picture — so
        /// leaning in is a continuous blend rather than a state change, and can be interrupted at
        /// any point. The two gestures are one-shot states that return on exit time.
        /// </summary>
        public static AnimatorController BuildController(string judge)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                AssetDatabase.CreateFolder("Assets", "Settings");
            if (!AssetDatabase.IsValidFolder(ControllerDir))
                AssetDatabase.CreateFolder("Assets/Settings", "Animators");

            var clips = Clips(judge);
            if (clips.Count == 0)
            {
                Debug.LogWarning($"[Duck] {judge}: model has no animation clips; " +
                                 "the judge will be built without an animator.");
                return null;
            }

            string path = $"{ControllerDir}/AC_Judge_{judge}.controller";
            AssetDatabase.DeleteAsset(path);      // rebuilt from scratch, never patched
            var ac = AnimatorController.CreateAnimatorControllerAtPath(path);

            ac.AddParameter("Attention", AnimatorControllerParameterType.Float);
            ac.AddParameter("Applaud", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Shake", AnimatorControllerParameterType.Trigger);

            var sm = ac.layers[0].stateMachine;

            var idle = Match(clips, IdleSuffix[judge], judge);
            var lean = Match(clips, "Lean", judge);
            var applaud = Match(clips, "Applaud", judge);
            var shake = Match(clips, "Shake", judge);

            var tree = new BlendTree
            {
                name = "Posture",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Attention",
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(tree, ac);
            if (idle != null) tree.AddChild(idle, 0f);
            if (lean != null) tree.AddChild(lean, 1f);

            var posture = sm.AddState("Posture");
            posture.motion = tree;
            posture.writeDefaultValues = false;
            sm.defaultState = posture;

            AddOneShot(sm, posture, applaud, "Applaud");
            AddOneShot(sm, posture, shake, "Shake");

            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssets();
            return ac;
        }

        static void AddOneShot(AnimatorStateMachine sm, AnimatorState back, AnimationClip clip, string trigger)
        {
            if (clip == null) return;

            var state = sm.AddState(trigger);
            state.motion = clip;
            state.writeDefaultValues = false;

            var into = back.AddTransition(state);
            into.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            into.hasExitTime = false;
            into.duration = 0.12f;

            var outOf = state.AddTransition(back);
            outOf.hasExitTime = true;
            outOf.exitTime = 0.85f;
            outOf.duration = 0.22f;
        }

        [MenuItem("Duck/4 · Rebuild judge rigs and animators", priority = 3)]
        public static void RebuildAll()
        {
            foreach (var n in Names)
            {
                ConfigureImport(n);
                BuildController(n);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Duck] Judge rigs imported and animator controllers built.");
        }
    }
}
