using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Turns the Blender FBX exports into game-ready prefabs.
    ///
    /// The models arrive as a plain transform hierarchy with vertex colours and no rig, which is
    /// deliberate: animating named transforms is cheaper than skinning in WebGL and survives a
    /// re-export without rebinding anything. This script's job is to apply consistent import
    /// settings, put one material on each family, assemble the mower with its driver, and wire
    /// the animation component to the real node names.
    /// </summary>
    public static class DuckModelIntegration
    {
        const string ModelDir = "Assets/Art/Models";
        const string PrefabDir = "Assets/Prefabs";
        const string MatDir = "Assets/Materials";

        [MenuItem("Duck/4 · Integrate Blender Models", priority = 3)]
        public static void IntegrateAll()
        {
            EnsureFolder(PrefabDir);
            ConfigureImporters();
            BuildModelMaterials();
            BuildMowerPrefab();
            BuildJudgePrefabs();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Duck] Blender models integrated.");
        }

        // ------------------------------------------------------------------ import settings

        static void ConfigureImporters()
        {
            foreach (string path in ModelPaths())
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;

                bool dirty = false;
                if (importer.globalScale != 1f) { importer.globalScale = 1f; dirty = true; }
                if (!importer.useFileScale) { importer.useFileScale = true; dirty = true; }
                if (importer.animationType != ModelImporterAnimationType.None)
                { importer.animationType = ModelImporterAnimationType.None; dirty = true; }
                if (importer.importBlendShapes) { importer.importBlendShapes = false; dirty = true; }
                if (importer.importVisibility) { importer.importVisibility = false; dirty = true; }
                if (importer.importCameras) { importer.importCameras = false; dirty = true; }
                if (importer.importLights) { importer.importLights = false; dirty = true; }
                if (importer.meshCompression != ModelImporterMeshCompression.Off)
                { importer.meshCompression = ModelImporterMeshCompression.Off; dirty = true; }
                if (importer.isReadable) { importer.isReadable = false; dirty = true; }
                if (!importer.optimizeMeshPolygons) { importer.optimizeMeshPolygons = true; dirty = true; }
                if (!importer.optimizeMeshVertices) { importer.optimizeMeshVertices = true; dirty = true; }
                if (importer.importNormals != ModelImporterNormals.Import)
                { importer.importNormals = ModelImporterNormals.Import; dirty = true; }
                if (importer.generateSecondaryUV) { importer.generateSecondaryUV = false; dirty = true; }
                // We assign our own materials on the prefab, so do not spawn per-file ones.
                if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
                { importer.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }

                if (dirty)
                {
                    importer.SaveAndReimport();
                    Debug.Log($"[Duck] Reimported {path}");
                }
            }
        }

        // ------------------------------------------------------------------ materials

        public static Material EnsureModelMaterial(string name, float smoothness, float metallic)
        {
            var m = DuckSceneBuilder.EnsureMaterial(name, "Duck/Prop");
            if (m == null) return null;
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_VertexColorAmount", 1f);
            // Blender writes sRGB vertex colours and Unity imports the bytes untouched.
            m.SetFloat("_VertexColorSRGB", 1f);
            m.EnableKeyword("_VCOL_SRGB");
            m.SetColor("_ShadowTint", DuckSceneBuilder.HexL("#8CA8D6"));
            m.SetColor("_RimColor", DuckSceneBuilder.HexL("#FFF3D8"));
            m.SetFloat("_RimStrength", 0.16f);
            // Same reason as the props, and more so: the characters are the things the camera
            // pushes in on, and the judges are lit from behind in every shot they appear in.
            m.SetFloat("_Wrap", 0.62f);
            m.SetColor("_InstanceColor", Color.white);
            // The winding audit reports every authored mesh closed, manifold and consistently
            // wound, so back-face culling is safe again and we get the fill rate back.
            m.SetFloat("_Cull", 2f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void BuildModelMaterials()
        {
            EnsureModelMaterial("M_Duck", 0.22f, 0f);
            EnsureModelMaterial("M_Mower", 0.50f, 0.06f);
            EnsureModelMaterial("M_Judges", 0.20f, 0f);
            EnsureModelMaterial("M_Spectators", 0.16f, 0f);
            EnsureModelMaterial("M_Rivals", 0.22f, 0f);
            EnsureModelMaterial("M_StationJudges", 0.20f, 0f);
            EnsureModelMaterial("M_PropsAuthored", 0.24f, 0f);
        }

        // ------------------------------------------------------------------ helpers

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        static void ApplyMaterial(GameObject root, Material mat, bool castShadows = true)
        {
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveShadows = true;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>
        /// Every FBX under the model folder. "t:Model" is a Project-window filter, not a real
        /// type name, so FindAssets silently returns nothing for it — enumerate GameObjects and
        /// keep the ones that came from a mesh file.
        /// </summary>
        static List<string> ModelPaths()
        {
            var list = new List<string>();
            if (!AssetDatabase.IsValidFolder(ModelDir)) return list;
            foreach (string full in System.IO.Directory.GetFiles(ModelDir, "*.*", System.IO.SearchOption.AllDirectories))
            {
                string ext = System.IO.Path.GetExtension(full).ToLowerInvariant();
                if (ext != ".fbx" && ext != ".obj" && ext != ".glb" && ext != ".gltf") continue;
                string path = full.Replace(System.IO.Path.DirectorySeparatorChar, '/');
                if (!list.Contains(path)) list.Add(path);
            }
            list.Sort();
            return list;
        }

        static GameObject LoadModel(string file)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{file}");
            if (go == null) Debug.LogWarning($"[Duck] Model not found: {ModelDir}/{file}");
            return go;
        }

        // ------------------------------------------------------------------ mower + duck

        public static GameObject BuildMowerPrefab()
        {
            var mowerModel = LoadModel("Mower.fbx");
            var duckModel = LoadModel("Duck.fbx");
            if (mowerModel == null) return null;

            int mowerLayer = LayerMask.NameToLayer("Mower");

            var root = new GameObject("Mower");
            root.layer = mowerLayer;
            root.tag = "Mower";

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 180f;
            rb.linearDamping = 0f;
            rb.angularDamping = 2.2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(0.92f, 0.52f, 1.45f);
            box.center = new Vector3(0f, 0.06f, 0f);

            var ctrl = root.AddComponent<MowerController>();
            ctrl.groundMask = ~(1 << mowerLayer);

            var pivot = new GameObject("VisualPivot").transform;
            pivot.SetParent(root.transform, false);

            var mowerGO = (GameObject)PrefabUtility.InstantiatePrefab(mowerModel);
            mowerGO.name = "MowerModel";
            mowerGO.transform.SetParent(pivot, false);
            PrefabUtility.UnpackPrefabInstance(mowerGO, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            ApplyMaterial(mowerGO, AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Mower.mat"));

            // The duck rides on the seat. Parenting under the visual pivot rather than the body
            // means it inherits the chassis lean without inheriting the wheel spin.
            Transform duckRoot = null, duckHead = null, duckWingL = null, duckWingR = null, duckTail = null;
            if (duckModel != null)
            {
                var duckGO = (GameObject)PrefabUtility.InstantiatePrefab(duckModel);
                duckGO.name = "Duck";
                duckGO.transform.SetParent(pivot, false);
                PrefabUtility.UnpackPrefabInstance(duckGO, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                ApplyMaterial(duckGO, AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Duck.mat"));

                // The duck and the mower were modelled together in one scene and verified as a
                // rig, so the authored offset is the truth. Measuring the fit from bounding boxes
                // put the duck a head too high, standing on the steering wheel.
                duckGO.transform.localPosition = DuckSeatOffset;

                duckRoot = duckGO.transform;
                duckHead = FindDeep(duckGO.transform, "Duck_Head");
                duckWingL = FindDeep(duckGO.transform, "Duck_Wing_L");
                duckWingR = FindDeep(duckGO.transform, "Duck_Wing_R");
                duckTail = FindDeep(duckGO.transform, "Duck_Tail");
            }

            var visuals = root.AddComponent<MowerVisuals>();
            visuals.mower = ctrl;
            visuals.visualPivot = pivot;
            visuals.wheelFL = FindDeep(mowerGO.transform, "Mower_Wheel_FL");
            visuals.wheelFR = FindDeep(mowerGO.transform, "Mower_Wheel_FR");
            visuals.wheelRL = FindDeep(mowerGO.transform, "Mower_Wheel_RL");
            visuals.wheelRR = FindDeep(mowerGO.transform, "Mower_Wheel_RR");
            visuals.steeringColumn = FindDeep(mowerGO.transform, "Mower_Steering");
            visuals.bladeSpinner = FindDeep(mowerGO.transform, "Mower_Blade");
            visuals.exhaust = FindDeep(mowerGO.transform, "Mower_Exhaust");
            visuals.catcherBag = FindDeep(mowerGO.transform, "Mower_CatcherBag");
            visuals.duckRoot = duckRoot;
            visuals.duckHead = duckHead;
            visuals.duckWingL = duckWingL;
            visuals.duckWingR = duckWingR;
            visuals.duckTail = duckTail;

            SetLayerRecursive(root.transform, mowerLayer);

            string path = $"{PrefabDir}/Mower.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log($"[Duck] Mower prefab written to {path}");
            return prefab;
        }

        /// <summary>
        /// Seat contact in mower-root local space, measured in Blender by the modeller with both
        /// assets in one scene. Do not derive this from bounds — the duck's origin sits near its
        /// body centre, not its underside.
        /// </summary>
        public static readonly Vector3 DuckSeatOffset = new Vector3(0f, 0.42f, -0.10f);

        /// <summary>Bounds-based fallback, kept for assets that have no authored seat node.</summary>
        static void SeatDuck(GameObject duckGO, GameObject mowerGO)
        {
            var seat = FindDeep(mowerGO.transform, "Mower_Seat");
            var steering = FindDeep(mowerGO.transform, "Mower_Steering");

            Bounds seatB;
            if (seat != null && TryBounds(seat.gameObject, out seatB)) { }
            else { seatB = new Bounds(new Vector3(0f, 0.42f, -0.10f), Vector3.one * 0.3f); }

            if (!TryBounds(duckGO, out Bounds duckB)) return;

            // Sink slightly into the cushion so the duck reads as sitting, not perching.
            const float sink = 0.035f;
            float dy = (seatB.max.y - sink) - duckB.min.y;
            float dz = seatB.center.z - duckB.center.z;
            float dx = seatB.center.x - duckB.center.x;

            duckGO.transform.localPosition += new Vector3(dx, dy, dz);

            // Nudge forward so the wings reach the wheel rather than stopping short of it.
            if (steering != null && TryBounds(steering.gameObject, out Bounds steerB) &&
                TryBounds(duckGO, out Bounds duckAfter))
            {
                float reach = steerB.center.z - duckAfter.center.z;
                float nudge = Mathf.Clamp(reach * 0.35f, -0.12f, 0.16f);
                duckGO.transform.localPosition += new Vector3(0f, 0f, nudge);
            }

            Debug.Log($"[Duck] Seated duck at {duckGO.transform.localPosition} " +
                      $"(seat top y={seatB.max.y:0.###}, duck underside was y={duckB.min.y:0.###})");
        }

        static bool TryBounds(GameObject go, out Bounds b)
        {
            b = default;
            bool first = true;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            return !first;
        }

        // ------------------------------------------------------------------ judges

        public static void BuildJudgePrefabs()
        {
            var judgesModel = LoadModel("Judges.fbx");
            if (judgesModel == null) return;

            EnsureFolder($"{PrefabDir}/Judges");
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Judges.mat");

            string[] names = { "Mildred", "Boris", "Priscilla" };
            float[] idleSpeed = { 1.0f, 1.15f, 0.55f };
            float[] idleAmount = { 1.0f, 1.05f, 0.5f };
            float[] lean = { 10f, 12f, 5f };
            var temperaments = new[]
            {
                JudgeTemperament.Severe, JudgeTemperament.Boisterous, JudgeTemperament.Aloof
            };
            float[] gestureGap = { 3.4f, 3.6f, 4.8f };

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(judgesModel);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            // Judges.fbx puts the conversion rotation on each judge's own root rather than on the
            // file root, so read it from the first one we find.
            Quaternion rootRotation = Quaternion.identity;
            var probe = FindDeep(instance.transform, "Mildred_Root") ?? FindDeep(instance.transform, "Boris_Root");
            if (probe != null) rootRotation = probe.localRotation;
            else rootRotation = instance.transform.localRotation;

            for (int i = 0; i < names.Length; i++)
            {
                var found = FindDeep(instance.transform, $"{names[i]}_Root");
                if (found == null)
                {
                    Debug.LogWarning($"[Duck] Judge root not found: {names[i]}_Root");
                    continue;
                }

                var go = new GameObject($"Judge_{names[i]}");
                found.SetParent(go.transform, false);
                found.localPosition = Vector3.zero;
                // Keep the FBX root's axis-conversion rotation. Resetting it to identity leaves
                // the model in Blender's Z-up space, which is why the judges were face down.
                found.localRotation = rootRotation;
                ApplyMaterial(go, mat);

                var jc = go.AddComponent<JudgeCharacter>();
                jc.body = FindDeep(found, $"{names[i]}_Body");
                jc.head = FindDeep(found, $"{names[i]}_Head");
                jc.armL = FindDeep(found, $"{names[i]}_Arm_L");
                jc.armR = FindDeep(found, $"{names[i]}_Arm_R");
                jc.card = BuildDeskCard(FindDeep(found, $"{names[i]}_Card"), go.transform,
                                        out var cardRend, out var cardText, out var cardVis);
                jc.cardRenderer = cardRend;
                jc.cardNumber = cardText;
                jc.cardVisual = cardVis;
                jc.idleSpeed = idleSpeed[i];
                jc.idleAmount = idleAmount[i];
                jc.leanAngle = lean[i];
                jc.temperament = temperaments[i];
                jc.gestureInterval = gestureGap[i];
                jc.gestureIntervalJitter = gestureGap[i] * 0.6f;

                PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/Judges/Judge_{names[i]}.prefab");
                Object.DestroyImmediate(go);
            }

            Object.DestroyImmediate(instance);
            Debug.Log("[Duck] Judge prefabs written.");
        }

        /// <summary>
        /// Turns the judge's held scorecard into a sign that lies face-down on the bench and tips
        /// up to stand.
        ///
        /// Held cards inherit every tremor of the character's idle animation, which read as the
        /// judge's hands shaking. Standing the card on the desk decouples the number from the
        /// performance entirely: the character can be as lively as it likes and the score still
        /// lands rock steady, with a knock.
        /// </summary>
        static Transform BuildDeskCard(Transform authoredCard, Transform judgeRoot,
                                       out MeshRenderer renderer, out TMPro.TextMeshPro text,
                                       out GameObject visual)
        {
            renderer = null;
            text = null;
            visual = null;
            if (judgeRoot == null) return null;

            // Hinge sits on the bench top, in front of the judge, pivoting on the card's bottom edge.
            var hinge = new GameObject("CardHinge").transform;
            hinge.SetParent(judgeRoot, false);
            hinge.localPosition = DeskCardPosition;
            hinge.localRotation = Quaternion.identity;

            // The authored card comes out of the FBX in Blender's Z-up space, and reparenting it
            // here strips the conversion rotation that made it stand up — so it lay flat and there
            // was effectively no plate behind the number at all. Build the plate here instead,
            // where its facing is known: the judges look north, so the readable side is +Z.
            if (authoredCard != null) Object.DestroyImmediate(authoredCard.gameObject);

            visual = new GameObject("CardVisual");
            visual.transform.SetParent(hinge, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            var back = Plate("Backing", visual.transform,
                             new Vector3(0f, DeskCardHeight * 0.5f, 0f),
                             new Vector3(DeskCardWidth * 0.5f, DeskCardHeight * 0.5f, 0.011f),
                             Mat("M_WoodDark"));
            renderer = Plate("Face", visual.transform,
                             new Vector3(0f, DeskCardHeight * 0.5f, 0.013f),
                             new Vector3(DeskCardWidth * 0.5f - 0.022f, DeskCardHeight * 0.5f - 0.022f, 0.006f),
                             Mat("M_TentCream"));
            if (back == null) renderer = null;

            // The number, drawn on the face of the card so it reads the instant the card stands.
            // Turned to face +Z: with an identity rotation TMP reads correctly only from behind
            // the judge, which is why the scores appeared mirrored from the player's seat.
            var textGO = new GameObject("CardNumber");
            textGO.transform.SetParent(visual.transform, false);
            textGO.transform.localPosition = new Vector3(0f, DeskCardNumberHeight, DeskCardNumberDepth);
            textGO.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            text = textGO.AddComponent<TMPro.TextMeshPro>();
            text.text = "0";
            text.fontSize = 3.6f;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = new Color(0.16f, 0.12f, 0.09f);
            text.rectTransform.sizeDelta = new Vector2(DeskCardWidth, DeskCardHeight);
            text.fontStyle = TMPro.FontStyles.Bold;

            return hinge;
        }

        /// <summary>A flat slab standing in the XY plane, readable from +Z.</summary>
        static MeshRenderer Plate(string name, Transform parent, Vector3 localPos, Vector3 half, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            // Persisted, not just generated: the judge is saved as a prefab, and a mesh that only
            // exists in memory does not survive that write. The card rendered as nothing at all.
            string key = $"ScoreCard_{half.x:0.000}x{half.y:0.000}x{half.z:0.000}";
            go.AddComponent<MeshFilter>().sharedMesh =
                DuckMeshLibrary.Persist(DuckPrimitives.ChamferBox(half, 0.008f), key);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return mr;
        }

        static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");

        /// <summary>Where the scorecard stands, relative to the judge root.</summary>
        /// <summary>
        /// Where the scorecard stands, relative to the judge root. Offset to one side: dead centre
        /// put a 40 cm plate directly across the judge's face at exactly the moment the close-up
        /// exists to show their reaction.
        /// </summary>
        public static readonly Vector3 DeskCardPosition = new Vector3(0.30f, 0.34f, 0.54f);
        const float DeskCardWidth = 0.32f;
        const float DeskCardHeight = 0.36f;
        const float DeskCardNumberHeight = 0.18f;
        const float DeskCardNumberDepth = 0.021f;

        static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }

        // ------------------------------------------------------------------ inspection

        [MenuItem("Duck/Diagnose · Imported models", priority = 43)]
        public static void ReportModels()
        {
            var sb = new System.Text.StringBuilder("[Duck] MODEL REPORT\n");
            foreach (string path in ModelPaths())
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(go);
                var bounds = new Bounds();
                bool first = true;
                int tris = 0, colored = 0, meshes = 0;
                foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        meshes++;
                        tris += mf.sharedMesh.triangles.Length / 3;
                        if (mf.sharedMesh.colors32 != null && mf.sharedMesh.colors32.Length > 0) colored++;
                    }
                }

                sb.AppendLine($"  {System.IO.Path.GetFileName(path)}: meshes={meshes} tris={tris} withVertexColours={colored}");
                sb.AppendLine($"     bounds centre={bounds.center} size={bounds.size}");
                var names = new List<string>();
                CollectNames(inst.transform, names, 0);
                sb.AppendLine("     hierarchy: " + string.Join(", ", names));
                Object.DestroyImmediate(inst);
            }
            Debug.Log(sb.ToString());
        }

        static void CollectNames(Transform t, List<string> into, int depth)
        {
            if (depth > 3 || into.Count > 60) return;
            for (int i = 0; i < t.childCount; i++)
            {
                into.Add(t.GetChild(i).name);
                CollectNames(t.GetChild(i), into, depth + 1);
            }
        }
    }
}
