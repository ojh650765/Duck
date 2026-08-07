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

        /// <summary>
        /// Clip names as authored, and whether each one loops.
        ///
        /// The three continuous ones loop; the reactions are one-shots. The rally drives these by
        /// name — see <see cref="RallyGoose"/> — so this table is also the contract with the model:
        /// a clip missing from Goose.fbx is reported loudly at import rather than discovered later as
        /// a bird that slides across the grass without moving its legs.
        /// </summary>
        static readonly Dictionary<string, bool> Loops = new()
        {
            { "Fly", true },
            { "Glide", true },
            { "Charge", true },
            { "Land", false },
            { "Brace", false },
            { "Struck", false },
            { "Recover", false },
            { "KO", false },
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
        /// One state per clip, flat, with no transitions between them.
        ///
        /// This replaces a blend tree and a trigger layer, and the change is not a simplification for
        /// its own sake — it is what the rally's goose actually needs. That bird has EIGHT distinct
        /// things it does and the code already knows which one it is doing, because the state machine
        /// that decides is the same one that flies it: fly in, land badly, charge, brace, get struck,
        /// tumble, recover, get knocked out. Encoding those transitions a second time in an animator
        /// graph means two state machines that have to agree, and the one in the graph has no way to
        /// know why it is being asked to move.
        ///
        /// So the graph holds poses and nothing else, and <see cref="RallyGoose"/> cross-fades to them
        /// by name. Every parameter-driven transition is gone; the deformation that used to need a
        /// continuous blend — the wing beat, the neck, the squash — is now driven procedurally in
        /// LateUpdate on top of whichever clip is playing, which is both cheaper and able to react to
        /// a hit that arrives mid-stride.
        ///
        /// The two old parameters are still declared. They cost nothing, and the previous phase still
        /// writes them; a Set on a parameter that does not exist is a console error per frame.
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
            // Read by nothing in the graph; written by the rally so a later blend tree has it ready.
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);

            var root = ctrl.layers[0].stateMachine;
            AnimatorState first = null;
            var made = new List<string>();

            // Ordered by the table rather than by whatever the FBX enumerates, so the graph opens in
            // a sensible reading order and the default state is always Fly.
            foreach (var name in Loops.Keys)
            {
                if (!clips.TryGetValue(name, out var clip) || clip == null) continue;
                var state = root.AddState(name);
                state.motion = clip;
                // Nothing leaves a state on its own. The bird is cross-faded out of it the moment its
                // own state machine moves on, and a one-shot that ran off the end into a transition
                // would fight that.
                state.writeDefaultValues = true;
                if (first == null || name == "Fly") first = state;
                made.Add(name);
            }

            if (first != null) root.defaultState = first;
            else Debug.LogError("[Duck] the goose controller has no states — Goose.fbx carries no clips.");

            AssetDatabase.SaveAssets();
            Debug.Log($"[Duck] goose controller: states [{string.Join(", ", made)}].");
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
                // Fixed, generous bounds — NOT updateWhenOffscreen.
                //
                // The bird is squashed, stretched, whipped and thrown fifty metres, so the bind-pose
                // box does not describe where it actually is and a goose in flight would pop out of
                // existence the moment that stale box left the frustum. The obvious answer is
                // updateWhenOffscreen, and it was what was here, and it is the wrong one twice over:
                // it re-skins the mesh on the CPU every frame purely to measure it — on a platform
                // where that is the most expensive thing in the loop — and it is the same code path
                // that has been failing to draw these birds at all in WebGL.
                //
                // A box three metres each way costs nothing, is correct for every pose this bird can
                // reach including a full launch stretch, and the only thing it gives up is culling a
                // goose slightly later than strictly necessary. There are at most nine of them.
                smr.updateWhenOffscreen = false;
                smr.localBounds = new Bounds(new Vector3(0f, 0.18f, 0.14f), new Vector3(6f, 6f, 6f));
            }

            // ------------------------------------------------------------------ standing it up
            //
            // Three nodes, and each one exists because the other two cannot do its job.
            //
            //   Goose   the gameplay root. RallyGoose rotates this to face travel, and nothing else
            //           ever touches it.
            //   Body    squash and stretch, and the walk's roll and sway. Its axes must AGREE with
            //           the root's, because the squash stretches along local +Z meaning "down the
            //           direction of travel" — put the mesh's own rotation here and a stretched
            //           goose flattens sideways.
            //   Model   the imported hierarchy, pitched back ninety degrees so the bird stands up
            //           and holds its head up. The mesh comes out of the FBX lying on its face; the
            //           correction has to be BELOW everything that reasons about direction, or every
            //           piece of that reasoning has to know about it.
            //
            // The Animator stays on the imported node. Its clips address bones by path relative to
            // the GameObject it sits on, so it can be re-parented freely but never separated from
            // the hierarchy it animates — moving it up to the new root would silently break every
            // clip in the controller.
            inst.name = "Model";
            inst.transform.localRotation = Quaternion.Euler(ModelPitchFix, 0f, 0f);

            var root = new GameObject("Goose");
            var bodyNode = new GameObject("Body");
            bodyNode.transform.SetParent(root.transform, false);
            inst.transform.SetParent(bodyNode.transform, true);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.Euler(ModelPitchFix, 0f, 0f);
            inst.transform.localScale = Vector3.one;

            FitForRally(root, anim, bodyNode.transform);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[Duck] wrote {PrefabPath}.");
        }

        /// <summary>
        /// Degrees of pitch that turn the imported mesh into a goose that is standing up.
        ///
        /// Not a taste call and not a guess: the model arrives nose-down, and at zero the bird runs
        /// at the gardens staring into the lawn. Held here as one named number rather than typed
        /// into the prefab by hand, so re-exporting the FBX cannot quietly lose it.
        /// </summary>
        const float ModelPitchFix = -55f;

        /// <summary>
        /// Make the bird playable in the four-way rally.
        ///
        /// The rig's procedural half — squash and stretch down travel, the neck whip, the wing beat —
        /// is driven off bones found BY NAME rather than off serialized references picked in the
        /// inspector, because this prefab is regenerated from the FBX whenever the model changes and
        /// hand-picked references do not survive that. Missing a bone is not an error: every one of
        /// these is optional in <see cref="RallyGoose"/> and the bird simply deforms less.
        ///
        /// Body deliberately is NOT the prefab root. RallyGoose writes a non-uniform localScale for
        /// the squash, and the root is what the director positions and rotates — squashing it would
        /// scale the collider-free bird along world axes rather than along its own travel, so a
        /// stretched goose would flatten sideways whenever it turned.
        /// </summary>
        static void FitForRally(GameObject inst, Animator anim, Transform bodyNode)
        {
            var goose = inst.GetComponent<RallyGoose>();
            if (goose == null) goose = inst.AddComponent<RallyGoose>();
            goose.animator = anim;

            // The dedicated node, not a bone. Squash, roll and sway are all expressed in the frame
            // the machine reasons in — "along travel", "onto the planted foot" — and a bone found by
            // name sits in the mesh's own frame, which is pitched. Deforming there means every one
            // of those has to be re-derived through the correction, and the first time the model is
            // re-exported at a different rest angle they all silently mean something else.
            goose.body = bodyNode;
            goose.neck = FindBone(inst.transform, "neck", "head");
            goose.wingLeft = FindBone(inst.transform, "wing_l", "wingl", "wing.l", "leftwing");
            goose.wingRight = FindBone(inst.transform, "wing_r", "wingr", "wing.r", "rightwing");

            if (goose.body == inst.transform) goose.body = null;

            Debug.Log($"[Duck] goose fitted for the rally: body={Name(goose.body)}, " +
                      $"neck={Name(goose.neck)}, wings={Name(goose.wingLeft)}/{Name(goose.wingRight)}.");
        }

        static string Name(Transform t) => t != null ? t.name : "—";

        /// <summary>First transform whose lowercased name contains any of the fragments, breadth-ish.</summary>
        static Transform FindBone(Transform root, params string[] fragments)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                string n = t.name.ToLowerInvariant();
                foreach (var f in fragments)
                    if (n.Contains(f)) return t;
            }
            return null;
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
