using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Generated meshes and the temporary stand-in rigs for the mower, duck and judges.
    ///
    /// The stand-ins exist so the game is playable and tunable before the Blender assets land.
    /// They use the same transform names and pivots as the real models, so replacing them is a
    /// matter of parenting the imported mesh under the same node — no rewiring.
    /// </summary>
    public static class DuckMeshLibrary
    {
        const string MeshDir = "Assets/Meshes";
        const string MatDir = "Assets/Materials";

        static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>
        /// Persists a generated mesh and hands back the asset.
        ///
        /// Anything that ends up inside a saved prefab has to go through here. A Mesh created in
        /// memory is silently dropped when the prefab is written — the MeshFilter survives with a
        /// null mesh, so the object is simply invisible in the build with no error anywhere.
        /// </summary>
        public static Mesh Persist(Mesh mesh, string name) => SaveMesh(mesh, name);

        static Mesh SaveMesh(Mesh mesh, string name)
        {
            EnsureFolder(MeshDir);
            string path = $"{MeshDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                existing.indexFormat = mesh.indexFormat;
                existing.SetVertices(mesh.vertices);
                existing.SetNormals(mesh.normals);
                existing.SetUVs(0, new List<Vector2>(mesh.uv));
                // Colours have to come across too. The prop shader uses the vertex colour as the
                // albedo, so a mesh rebuilt without its colour stream renders whatever happens to
                // be in that register — which showed up as a rainbow wash over the score cards the
                // moment a second object reused the same generated mesh.
                var colours = mesh.colors32;
                if (colours != null && colours.Length == mesh.vertexCount) existing.SetColors(colours);
                existing.SetTriangles(mesh.triangles, 0);
                existing.RecalculateBounds();
                // Keep the asset's name matching its filename, or Unity warns on every rebuild.
                existing.name = name;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(mesh);
                return existing;
            }
            mesh.name = name;
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ------------------------------------------------------------------ primitives

        /// <summary>A flat XZ quad, subdivided so vertex fog interpolates sensibly.</summary>
        public static Mesh Quad(float sizeX, float sizeZ, int subdiv)
        {
            string key = $"Quad_{sizeX:0}x{sizeZ:0}_{subdiv}";
            var cached = AssetDatabase.LoadAssetAtPath<Mesh>($"{MeshDir}/{key}.asset");
            if (cached != null) return cached;

            int n = Mathf.Max(1, subdiv);
            var verts = new Vector3[(n + 1) * (n + 1)];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            var tris = new int[n * n * 6];

            for (int z = 0; z <= n; z++)
                for (int x = 0; x <= n; x++)
                {
                    int i = z * (n + 1) + x;
                    float fx = x / (float)n, fz = z / (float)n;
                    verts[i] = new Vector3((fx - 0.5f) * sizeX, 0f, (fz - 0.5f) * sizeZ);
                    norms[i] = Vector3.up;
                    uvs[i] = new Vector2(fx, fz);
                }

            int t = 0;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int i = z * (n + 1) + x;
                    tris[t++] = i; tris[t++] = i + n + 1; tris[t++] = i + n + 2;
                    tris[t++] = i; tris[t++] = i + n + 2; tris[t++] = i + 1;
                }

            var mesh = new Mesh();
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return SaveMesh(mesh, key);
        }

        /// <summary>An annulus in the XZ plane — used for the apron ring and the pond rim.</summary>
        public static Mesh Ring(float innerRadius, float outerRadius, int segments, string key)
        {
            var cached = AssetDatabase.LoadAssetAtPath<Mesh>($"{MeshDir}/{key}.asset");
            if (cached != null) return cached;

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts.Add(dir * innerRadius); norms.Add(Vector3.up); uvs.Add(new Vector2(i / (float)segments, 0f));
                verts.Add(dir * outerRadius); norms.Add(Vector3.up); uvs.Add(new Vector2(i / (float)segments, 1f));
            }
            // Wound to face UP, which is the only direction a ring lying on the ground is ever seen
            // from. It was wound the other way and every ring built with it was invisible: the
            // vertices ran anticlockwise as seen from above, so the whole annulus was back-facing and
            // silently culled while the hierarchy showed a renderer with a perfectly good mesh on it.
            // Nothing else in the project had ever called this, so the fault had never had to show.
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                tris.Add(a); tris.Add(a + 3); tris.Add(a + 1);
                tris.Add(a); tris.Add(a + 2); tris.Add(a + 3);
            }

            var mesh = new Mesh();
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return SaveMesh(mesh, key);
        }

        // ------------------------------------------------------------------ stand-in rigs

        public class MowerProxy
        {
            public Transform wheelFL, wheelFR, wheelRL, wheelRR;
            public Transform steering, blade, exhaust, deck, body;
            public Transform duckRoot, duckHead, duckWingL, duckWingR;
        }

        /// <summary>
        /// One drawn box, with collision if — and only if — the mower can hit it.
        ///
        /// This helper builds most of the venue's furniture: the judges' bench, the marker boards,
        /// the rivals' stations and stands, the scoreboard's frame. It used to destroy the collider
        /// that CreatePrimitive hands it, every time, without looking at where the box stood. Two of
        /// those boxes stand inside the fence — the six-metre judges' bench and the plot's marker
        /// board — so the mower drove straight through them, which is the "some obstacles are
        /// ignored" report in its third and fourth incarnations.
        ///
        /// Stripping it wholesale was not laziness, it was a reasonable default: hundreds of these
        /// boxes are dressing, standing far outside the fence, and colliders on them would be pure
        /// cost. What was missing is that the default was applied to the two boxes it was wrong for
        /// with no way for anyone to notice.
        ///
        /// So the decision is made from the geometry instead of by hand. The primitive's own collider
        /// IS its mesh — a unit cube under the same transform — so keeping it cannot disagree with
        /// what is drawn, at any scale or rotation. DuckSolidity owns the question; see the note at
        /// the top of that file and of MowerContact.
        /// </summary>
        static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat,
                              Vector3? euler = null, PrimitiveType type = PrimitiveType.Cube,
                              bool castShadow = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (euler.HasValue) go.transform.localEulerAngles = euler.Value;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;

            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                var mf = go.GetComponent<MeshFilter>();
                Bounds world = mf != null && mf.sharedMesh != null
                    ? DuckSolidity.WorldBounds(go.transform, mf.sharedMesh)
                    : new Bounds(go.transform.position, Vector3.zero);

                // A box belonging to a rigidbody keeps nothing: the mower's stand-in chassis is built
                // from these, and extra colliders there join the mower's own compound shape and grind
                // along the ground.
                bool ownedByABody = go.GetComponentInParent<Rigidbody>() != null;

                // A box too short to be hit is NOT reported here. One box knows nothing about its
                // neighbours, and most short pieces are the top slab of something whose front panel
                // will stop the mower a moment earlier. Judging that needs the whole scene, so it is
                // DuckSolidity.Enforce at the end of the build that reports it.
                if (ownedByABody || !DuckSolidity.MustBeSolid(world))
                    Object.DestroyImmediate(col);
            }
            return go;
        }

        public static MowerProxy BuildMowerProxy(Transform pivot)
        {
            var p = new MowerProxy();
            var red = Mat("M_MowerRed");
            var cream = Mat("M_MowerCream");
            var grey = Mat("M_EngineGrey");
            var brass = Mat("M_Brass");
            var tyre = Mat("M_Tyre");
            var duckMat = Mat("M_DuckCream");
            var billMat = Mat("M_Bill");

            var visual = new GameObject("Visual").transform;
            visual.SetParent(pivot, false);

            p.body = Box(visual, "Body", new Vector3(0f, 0.16f, -0.06f), new Vector3(0.86f, 0.34f, 1.16f), red).transform;
            Box(visual, "Bonnet", new Vector3(0f, 0.30f, 0.34f), new Vector3(0.72f, 0.24f, 0.52f), red);
            Box(visual, "Trim", new Vector3(0f, 0.05f, -0.06f), new Vector3(0.90f, 0.10f, 1.18f), cream);
            Box(visual, "Engine", new Vector3(0f, 0.44f, 0.30f), new Vector3(0.44f, 0.22f, 0.36f), grey);
            Box(visual, "Seat", new Vector3(0f, 0.42f, -0.30f), new Vector3(0.46f, 0.10f, 0.42f), cream);
            Box(visual, "SeatBack", new Vector3(0f, 0.60f, -0.50f), new Vector3(0.46f, 0.34f, 0.10f), cream);

            p.exhaust = Box(visual, "Exhaust", new Vector3(0.24f, 0.62f, 0.30f), new Vector3(0.08f, 0.34f, 0.08f), grey,
                            null, PrimitiveType.Cylinder).transform;
            Box(visual, "Headlight", new Vector3(0f, 0.36f, 0.60f), new Vector3(0.18f, 0.18f, 0.10f), brass, null, PrimitiveType.Sphere);

            // Deck and blade, forward-mounted at the swath the cut system actually uses.
            var deck = new GameObject("Deck").transform;
            deck.SetParent(visual, false);
            deck.localPosition = new Vector3(0f, 0.10f, 0.62f);
            p.deck = deck;
            Box(deck, "DeckShell", new Vector3(0f, 0.02f, 0f), new Vector3(1.20f, 0.16f, 0.44f), red);

            var blade = new GameObject("Blade").transform;
            blade.SetParent(deck, false);
            blade.localPosition = new Vector3(0f, -0.05f, 0f);
            p.blade = blade;
            Box(blade, "BladeBar", Vector3.zero, new Vector3(1.02f, 0.02f, 0.07f), brass);

            var steering = new GameObject("Steering").transform;
            steering.SetParent(visual, false);
            steering.localPosition = new Vector3(0f, 0.50f, 0.16f);
            steering.localEulerAngles = new Vector3(-22f, 0f, 0f);
            p.steering = steering;
            Box(steering, "Column", new Vector3(0f, -0.08f, 0f), new Vector3(0.05f, 0.16f, 0.05f), grey, null, PrimitiveType.Cylinder);
            Box(steering, "Wheel", new Vector3(0f, 0.10f, 0f), new Vector3(0.30f, 0.03f, 0.30f), grey, new Vector3(90f, 0f, 0f), PrimitiveType.Cylinder);

            p.wheelFL = MakeWheel(visual, "Wheel_FL", new Vector3(-0.40f, -0.15f, 0.52f), 0.15f, tyre);
            p.wheelFR = MakeWheel(visual, "Wheel_FR", new Vector3(0.40f, -0.15f, 0.52f), 0.15f, tyre);
            p.wheelRL = MakeWheel(visual, "Wheel_RL", new Vector3(-0.40f, -0.08f, -0.52f), 0.22f, tyre);
            p.wheelRR = MakeWheel(visual, "Wheel_RR", new Vector3(0.40f, -0.08f, -0.52f), 0.22f, tyre);

            // Duck stand-in, seated, sized to the art bible.
            var duck = new GameObject("Duck").transform;
            duck.SetParent(visual, false);
            duck.localPosition = new Vector3(0f, 0.47f, -0.16f);
            p.duckRoot = duck;

            Box(duck, "Body", new Vector3(0f, 0.11f, 0f), new Vector3(0.26f, 0.24f, 0.30f), duckMat, null, PrimitiveType.Sphere);

            var head = new GameObject("Head").transform;
            head.SetParent(duck, false);
            head.localPosition = new Vector3(0f, 0.26f, 0.04f);
            p.duckHead = head;
            Box(head, "Skull", Vector3.zero, new Vector3(0.17f, 0.17f, 0.17f), duckMat, null, PrimitiveType.Sphere);
            Box(head, "Bill", new Vector3(0f, -0.02f, 0.11f), new Vector3(0.09f, 0.045f, 0.13f), billMat);

            p.duckWingL = MakeWing(duck, "Wing_L", new Vector3(-0.14f, 0.11f, 0.02f), duckMat);
            p.duckWingR = MakeWing(duck, "Wing_R", new Vector3(0.14f, 0.11f, 0.02f), duckMat);

            return p;
        }

        static Transform MakeWheel(Transform parent, string name, Vector3 pos, float radius, Material mat)
        {
            var hub = new GameObject(name).transform;
            hub.SetParent(parent, false);
            hub.localPosition = pos;
            var go = Box(hub, "Tyre", Vector3.zero, new Vector3(radius * 2f, 0.09f, radius * 2f), mat,
                         new Vector3(0f, 0f, 90f), PrimitiveType.Cylinder);
            return hub;
        }

        static Transform MakeWing(Transform parent, string name, Vector3 pos, Material mat)
        {
            var wing = new GameObject(name).transform;
            wing.SetParent(parent, false);
            wing.localPosition = pos;
            Box(wing, "Feather", new Vector3(0f, -0.02f, 0.06f), new Vector3(0.06f, 0.12f, 0.18f), mat);
            return wing;
        }

        // ------------------------------------------------------------------ judges

        /// <summary>Just the furniture, for when the authored judges are supplying the characters.</summary>
        public static void BuildJudgeBenchOnly(Transform root)
        {
            var wood = Mat("M_WoodWarm");
            var woodDark = Mat("M_WoodDark");
            // Legs take M_WoodPost, not M_WoodDark. Both are the same brown; the difference is that
            // the wood shader runs its grain along the mesh's own long axis, and a leg is scaled long
            // on Y while every other piece here is scaled long on X. On M_WoodDark the legs would get
            // grain banding running across a vertical timber.
            var woodPost = Mat("M_WoodPost");
            // The bench casts again. It was switched off because the top overhang threw a hard
            // shadow down the front panel — the largest surface in every judging shot — and the
            // panel read as a black wall under the judges.
            //
            // The shadow was never what made it black. Two lighting bugs were: the volume profile
            // held no post-processing at all, so nothing was ever tonemapped, and the ambient probe
            // was a near-pure green that multiplied a warm brown's red and blue channels away. A
            // shadowed face had nothing to fall back on, so any shadow on it went to black.
            //
            // With both fixed, the shadow is the thing that puts the bench on the ground and the
            // judges behind it. Switching the casters off was a workaround that cost the whole
            // judging beat its depth, which is what "그림자가 없어서" was describing.
            Box(root, "BenchTop", new Vector3(0f, 0.78f, 0.42f), new Vector3(6.0f, 0.10f, 0.78f), wood);
            Box(root, "BenchFront", new Vector3(0f, 0.40f, 0.79f), new Vector3(6.0f, 0.66f, 0.08f), wood);
            Box(root, "BenchLegL", new Vector3(-2.7f, 0.38f, 0.15f), new Vector3(0.13f, 0.76f, 0.13f), woodPost);
            Box(root, "BenchLegR", new Vector3(2.7f, 0.38f, 0.15f), new Vector3(0.13f, 0.76f, 0.13f), woodPost);
        }

        public static void BuildJudgeProxies(Transform root, JudgePanel panel)
        {
            var wood = Mat("M_WoodWarm");
            var woodDark = Mat("M_WoodDark");
            // Same reason as BuildJudgeBenchOnly: grain follows the mesh's long axis, and these legs
            // are the only pieces here scaled long on Y.
            var woodPost = Mat("M_WoodPost");
            var cream = Mat("M_TentCream");

            // Bench they sit behind.
            Box(root, "BenchTop", new Vector3(0f, 0.92f, 0.35f), new Vector3(6.4f, 0.14f, 0.9f), wood);
            Box(root, "BenchFront", new Vector3(0f, 0.45f, 0.75f), new Vector3(6.4f, 0.9f, 0.12f), woodDark);
            Box(root, "BenchLegL", new Vector3(-2.9f, 0.45f, 0f), new Vector3(0.16f, 0.9f, 0.16f), woodPost);
            Box(root, "BenchLegR", new Vector3(2.9f, 0.45f, 0f), new Vector3(0.16f, 0.9f, 0.16f), woodPost);

            string[] names = { "Mildred", "Boris", "Priscilla" };
            string[] mats = { "M_TentCream", "M_FenceWhite", "M_TentCream" };
            float[] xs = { -2.1f, 0f, 2.1f };
            float[] heights = { 1.18f, 0.98f, 1.34f };
            float[] widths = { 0.42f, 0.62f, 0.34f };

            for (int i = 0; i < 3; i++)
            {
                var jg = new GameObject($"Judge_{names[i]}").transform;
                jg.SetParent(root, false);
                jg.localPosition = new Vector3(xs[i], 0f, -0.15f);

                var body = new GameObject("Body").transform;
                body.SetParent(jg, false);
                body.localPosition = new Vector3(0f, 0.55f, 0f);
                Box(body, "Torso", new Vector3(0f, heights[i] * 0.25f, 0f),
                    new Vector3(widths[i], heights[i] * 0.62f, widths[i] * 0.8f), Mat(mats[i]));

                var head = new GameObject("Head").transform;
                head.SetParent(body, false);
                head.localPosition = new Vector3(0f, heights[i] * 0.62f, 0f);
                Box(head, "Skull", Vector3.zero,
                    new Vector3(widths[i] * 0.8f, widths[i] * 0.8f, widths[i] * 0.9f), Mat(mats[i]), null, PrimitiveType.Sphere);

                var armL = new GameObject("Arm_L").transform;
                armL.SetParent(body, false);
                armL.localPosition = new Vector3(-widths[i] * 0.6f, heights[i] * 0.32f, 0f);
                Box(armL, "Limb", new Vector3(0f, -0.14f, 0.06f), new Vector3(0.11f, 0.30f, 0.11f), Mat(mats[i]));

                var armR = new GameObject("Arm_R").transform;
                armR.SetParent(body, false);
                armR.localPosition = new Vector3(widths[i] * 0.6f, heights[i] * 0.32f, 0f);
                Box(armR, "Limb", new Vector3(0f, -0.14f, 0.06f), new Vector3(0.11f, 0.30f, 0.11f), Mat(mats[i]));

                var card = new GameObject("Card").transform;
                card.SetParent(armR, false);
                card.localPosition = new Vector3(0f, -0.26f, 0.16f);
                var cardGO = Box(card, "CardFace", Vector3.zero, new Vector3(0.34f, 0.44f, 0.02f), cream);

                // The proxies keep only what a box stack can honour.
                //
                // JudgeCharacter is animator-driven now, and there is no rig here to drive — so
                // this fallback gets a head to look with and a card to raise, and no animator at
                // all. The component handles that: a null animator means the posture blend and the
                // gestures are skipped, while the card and the look-at carry on working. These
                // stand-ins exist so the scene still builds when the authored judges are missing,
                // and a silent NullReferenceException in that case would defeat the point of
                // having them.
                var jc = jg.gameObject.AddComponent<JudgeCharacter>();
                jc.animator = null;
                jc.head = head;
                jc.card = card;
                jc.cardRenderer = cardGO.GetComponent<MeshRenderer>();
                jc.idleSpeed = i == 1 ? 1.35f : (i == 2 ? 0.6f : 1f);
                jc.temperament = i == 1 ? JudgeTemperament.Boisterous
                               : (i == 2 ? JudgeTemperament.Aloof : JudgeTemperament.Severe);

                if (panel.judges[i] != null) panel.judges[i].character = jc;
            }
        }
    }
}
