using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Places the world around the playfield.
    ///
    /// The map is built as concentric rings — lawn, apron, spectator ring, landscape — so the
    /// player always has something at three depths in frame: the grass they are cutting, the
    /// crowd watching them do it, and a horizon that says where they are. Placement is
    /// deterministic from a fixed seed, and props are combined per material so the whole fair
    /// costs a couple of dozen draw calls.
    /// </summary>
    public static class DuckEnvironmentBuilder
    {
        const string MatDir = "Assets/Materials";
        const string MeshDir = "Assets/Meshes/Generated";

        static Material Mat(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");

        /// <summary>Authored mesh from the Blender exports; null if that object is not present.</summary>
        static Mesh Authored(string file, string root, string save, string exclude = null)
            => DuckAssetLibrary.GetCombined(file, root, save, exclude);

        /// <summary>The one material every authored prop uses (vertex colours carry the paint).</summary>
        static Material AuthoredMat() => Mat("M_PropsAuthored");

        // Ring radii, in metres from the centre of the lawn.
        const float Lawn = Field.Half;      // 32
        const float ApronOuter = 46f;
        const float FenceRadius = 40f;
        // Past the hills, but no further than the fog can see. The ground used to stop at 300 and
        // leave the far side of the skyline standing in nothing; 640 fixed that but drew a lot of
        // triangles the camera can never resolve, since haze is solid by 400 m. 470 covers the
        // whole hill ring and stops there.
        const float MeadowOuter = 470f;
        /// <summary>How far the ground stays level for the venue to stand on.</summary>
        const float VenueFlatRadius = 150f;

        // ------------------------------------------------------------------ shared placements
        //
        // Positions more than one pass has to agree about live here rather than inside the builder
        // that draws them.
        //
        // The scenery scatter has to know where the tents, the stands, the barn and the judging
        // backdrop are so it does not plant a tree through them — and BuildFoliage runs BEFORE the
        // landmarks and the backdrop are built, so a "register as you place" list would be empty for
        // exactly the props that were being hit. Declaring the positions once, and reading them from
        // both the builder and RegisterKeepOuts, is what stops a keep-out drifting away from the
        // prop it is supposed to protect.

        /// <summary>Supporters' tents on the outer ring: where, which way round, and how wide.</summary>
        static readonly Vector3[] TentSpots =
        {
            new Vector3(-52f, 0f, 44f), new Vector3(-40f, 0f, 53f),
            new Vector3( 49f, 0f, -38f), new Vector3(58f, 0f, 12f)
        };
        static readonly float[] TentYaws = { 22f, -14f, 40f, -62f };
        static readonly float[] TentSizes = { 5.2f, 4.2f, 6.0f, 4.6f };

        /// <summary>
        /// The barn: the player's east bearing, and the venue's tightest squeeze.
        ///
        /// It has to fit between the east spectator stand, whose seating reaches x = 48.1, and
        /// HORACE's fence line at x = 68.8 — 20.7 m for a building 16.7 m across. At (58, -6) turned
        /// -74 degrees the authored barn measured x 48.2 to 66.9: SIX CENTIMETRES off the stand, two
        /// solid masses touching. Squaring it up to -87 turns its long axis (16.1 m, along the mesh's
        /// local Z) fully east-west, which is what buys the clearance — 2.5 m off the stand and 1.4 m
        /// off the neighbour's rails — and still leaves it three degrees off square so it reads as a
        /// building rather than as a marker.
        /// </summary>
        static readonly Vector3 BarnCentre = new Vector3(59.5f, 0f, -6f);
        const float BarnYaw = -87f;

        static readonly Vector3 WindmillCentre = new Vector3(-102f, 0f, -38f);

        /// <summary>The north bearing board, well clear of the fence and of the plaza's own board.</summary>
        const float NorthBoardZ = 58f;

        /// <summary>Where the spectator stands stand, measured out from the fence on both touchlines.</summary>
        const float StandOffset = FenceRadius + 4.6f;

        /// <summary>The judges' bench, on the player plot's south edge. The backdrop is all south of it.</summary>
        const float BenchZ = -39.5f;

        // ---- the judging backdrop, which shares its ground with the entrance avenue ----
        //
        // The gate hedges and the dirt lane run down the middle of exactly the same patch (the lane
        // is 5.2 m wide at x = 0, the hedges flank it at x = ±5.2, both from z = -48 to z = -72), and
        // the backdrop props were laid out as if that ground were empty. Two of the four hay stacks
        // were standing IN the hedge — 2.3 m of bale inside a hedge block, measured — and one of them
        // was also parked across the road everybody arrives on.
        //
        // So the corridor belongs to the avenue: nothing in the backdrop comes inside |x| = 8. That
        // reads better than the overlap did, too — from the bench the avenue recedes between its
        // hedges with the marquee, the hay and the flags stacked up either side of it.
        const float AvenueHalfWidth = 8f;

        static readonly Vector3 MarqueeCentre = new Vector3(-13.2f, 0f, BenchZ - 21f);

        /// <summary>
        /// The backdrop is built as CLUSTERS, because the previous version was a row.
        ///
        /// It was four hay stacks at hardcoded positions and seven flag poles marching along
        /// x = -26 + i * 8.5 with a metre of jitter on top, and the player's verdict on it was that
        /// it looks strange and placed. They are right, and the jitter is why: the eye locks onto
        /// constant spacing long before it notices a metre of noise, so noise on a march reads as a
        /// march that someone tried to hide. A fairground does not space things out, it CLUSTERS
        /// them — around whatever they serve — and leaves the ground between clusters genuinely
        /// empty.
        ///
        /// So there are six anchors here, each one a group with a reason to exist, at six different
        /// distances so their silhouettes overlap from the bench's eyeline. Depth is what makes a
        /// backdrop read as deep, and overlapping silhouettes at different ranges cost nothing.
        ///
        /// Every anchor is constrained three ways, and all three are checkable arithmetic:
        ///   - outboard of the entrance avenue (nothing east of x = -8 on the west side or west of
        ///     x = +8 on the east), because the lane and the gate hedges own that corridor;
        ///   - inside the near tree ring's southern gap, 270 ± 32 degrees, with five degrees spare
        ///     for the widest canopy in the tree set;
        ///   - clear of each other by more than the sum of their radii.
        /// </summary>
        static readonly (Vector2 at, float radius)[] BackdropClusters =
        {
            (new Vector2(-13.2f, BenchZ - 21f), 8f),     // the supporters' marquee and its gear
            (new Vector2(12f, BenchZ - 16.5f), 6f),       // the hay pile
            (new Vector2(-21f, BenchZ - 31.5f), 6f),      // the second pile, crates, a barrow
            (new Vector2(23f, BenchZ - 38.5f), 7f),       // the far tent and its flags
            (new Vector2(-25.5f, BenchZ - 18.5f), 3f),    // west banner pole
            (new Vector2(16f, BenchZ - 26.5f), 3f),       // east banner pole
        };

        /// <summary>
        /// Which way the wind is blowing, in degrees. Every piece of cloth in the venue agrees.
        ///
        /// The seven old flags each took a random yaw, which is the single loudest thing wrong with
        /// them: seven flags pointing seven ways is not cloth, it is seven signs. Cloth on a windy
        /// day all leans the same way, and a shared direction with a few degrees of variation per
        /// piece is what turns a row of quads into a fairground in a breeze.
        /// </summary>
        const float WindYaw = 118f;

        /// <summary>
        /// Everywhere the scenery pass must not plant, as circles in the XZ plane.
        ///
        /// BlockedForScenery used to know about the lawns, the plaza, the pond and the hills, and
        /// that was the whole list — so the scatter was free to grow a tree through anything else,
        /// and it did: an oak inside the tent at (49, -38), a specimen through the tent at (-40, 53),
        /// and grove 1 reaching the west spectator stand at x = -46.
        /// </summary>
        static readonly List<(Vector3 centre, float radius)> _keepOut = new List<(Vector3, float)>();

        static void RegisterKeepOuts()
        {
            _keepOut.Clear();

            // Radii are the prop's own footprint. BlockedForScenery adds the canopy margin, so these
            // stay readable as "how big is this thing".
            for (int i = 0; i < TentSpots.Length; i++)
                _keepOut.Add((TentSpots[i], TentSizes[i] * 0.85f));

            // Three stand sections a side, each 6 m along the touchline and 3.4 m deep, sitting
            // outboard of the fence.
            for (int side = -1; side <= 1; side += 2)
                for (int k = -1; k <= 1; k++)
                    _keepOut.Add((new Vector3(side * (StandOffset + 1.8f), 0f, k * 11f), 3.8f));

            _keepOut.Add((BarnCentre, 10.5f));
            _keepOut.Add((WindmillCentre, 6f));                              // tower plus sail sweep
            _keepOut.Add((new Vector3(0f, 0f, NorthBoardZ), 7f));            // 13 m across

            foreach (var (at, radius) in BackdropClusters)
                _keepOut.Add((new Vector3(at.x, 0f, at.y), radius));

            // The entrance avenue: lane, hedges and the gap between them, from the fence to the
            // south end of the hedge line. A tree standing in the road is as wrong as one standing
            // in a tent, and this is the ground the round both starts and finishes on.
            for (float z = -48f; z >= -76f; z -= 7f)
                _keepOut.Add((new Vector3(0f, 0f, z), AvenueHalfWidth));
        }

        static System.Random _rng;
        static float Rand => (float)_rng.NextDouble();
        static float Range(float a, float b) => a + (b - a) * Rand;

        /// <summary>
        /// A sideways offset for a companion prop: a random bearing, and a distance with a FLOOR.
        ///
        /// Everything that clumps in this file used to offset by Range(-n, n) on x and z
        /// independently, which is a square with the parent sitting in the middle of it — and the
        /// middle of that square is zero. So "a tree and its companion" included "two trees in one
        /// hole", and it drew the same numbers every rebuild whether it looked right or not. Polar
        /// with a minimum radius cannot produce that.
        /// </summary>
        static Vector3 Companion(float minRadius, float maxRadius)
        {
            float a = Range(0f, Mathf.PI * 2f);
            float r = Range(minRadius, maxRadius);
            return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }

        public static void Build()
        {
            _rng = new System.Random(20260804);
            RegisterKeepOuts();
            var root = new GameObject("~ World").transform;

            BuildGround(root);
            BuildHills(root);
            BuildFence(root);
            BuildBunting(root);
            BuildStands(root);
            BuildAwningAndScoreboard(root);
            BuildTents(root);
            BuildPond(root);
            BuildFoliage(root);
            BuildLandmarks(root);
            BuildCardinalLandmarks(root);
            BuildJudgeBackdrop(root);
            BuildFieldProps(root);
            BuildCrowd(root);

            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ helpers

        static GameObject Spawn(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos,
                                Quaternion rot, Vector3 scale, bool castShadow = true, int layer = 0)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            go.layer = layer;
            return go;
        }

        /// <summary>
        /// A copy of MESH turned upside down, with its winding put back the right way round.
        ///
        /// Needed because a negative scale is not a safe way to flip a solid. Mirroring a
        /// transform on one axis reverses the effective triangle winding, so every face renders
        /// as a backface and gets culled — the object turns inside out and you see through it.
        /// The pond basin was built exactly that way, a Hill mound spawned at scale (1, -1, 1) to
        /// make a bowl, and it had been rendering inverted ever since.
        ///
        /// Neither winding audit could have caught it, and that is worth knowing rather than
        /// fixing quietly: both of them inspect MESH DATA, where the winding is perfectly correct.
        /// The inversion lives in the transform. "0 inverted meshes over 101 scene meshes" was a
        /// true answer to a question that could not see the bug.
        ///
        /// Flipping the geometry once, here, keeps the object at a positive scale, so what the
        /// renderer sees agrees with what the audit checks.
        /// </summary>
        /// <remarks>
        /// No longer called: the pond's sunken bowl was measured to be permanently invisible (see
        /// BuildPond) and the bank is now built above the ground instead. Kept because the trap it
        /// documents is not specific to that bowl — the next person to reach for a negative scale to
        /// flip a solid needs this, and the fix belongs in a mesh copy rather than a transform.
        /// </remarks>
        static Mesh MirrorY(Mesh src)
        {
            var m = Object.Instantiate(src);
            var verts = m.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i].y = -verts[i].y;
            m.vertices = verts;

            var normals = m.normals;
            if (normals != null && normals.Length == verts.Length)
            {
                for (int i = 0; i < normals.Length; i++) normals[i].y = -normals[i].y;
                m.normals = normals;
            }

            // Reverse each triangle, which undoes the winding flip the mirror introduced.
            for (int s = 0; s < m.subMeshCount; s++)
            {
                var tris = m.GetTriangles(s);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int t = tris[i + 1];
                    tris[i + 1] = tris[i + 2];
                    tris[i + 2] = t;
                }
                m.SetTriangles(tris, s, false);
            }

            m.RecalculateBounds();
            return m;
        }

        static Mesh Save(Mesh m, string name)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Meshes"))
                AssetDatabase.CreateFolder("Assets", "Meshes");
            if (!AssetDatabase.IsValidFolder(MeshDir))
                AssetDatabase.CreateFolder("Assets/Meshes", "Generated");

            string path = $"{MeshDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(m, existing);
                // CopySerialized copies the source mesh's NAME across as well, which for a
                // generated mesh is whatever the primitive was called — "Hill", "SquareRing", or
                // nothing at all for a combined one. Unity then warns that the main object's name
                // does not match the file it lives in, once per mesh, on every single rebuild.
                // Renaming after the copy is the whole fix.
                existing.name = name;
                Object.DestroyImmediate(m);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            m.name = name;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>
        /// Accumulates many small props and emits one combined mesh per material.
        /// A picket fence of 140 posts becomes a single draw call instead of 140.
        /// </summary>
        class Combiner
        {
            struct Item
            {
                public CombineInstance instance;
                public bool solid;
            }

            readonly Dictionary<Material, List<Item>> _buckets = new();

            public void Add(Mesh mesh, Matrix4x4 trs, Material mat)
            {
                if (mesh == null || mat == null) return;
                if (!_buckets.TryGetValue(mat, out var list))
                {
                    list = new List<Item>();
                    _buckets[mat] = list;
                }
                list.Add(new Item
                {
                    instance = new CombineInstance { mesh = mesh, transform = trs, subMeshIndex = 0 },
                    // Decided here, while the mesh and its placement are both still in hand. Once
                    // CombineMeshes has run there is no instance left to ask about.
                    solid = DuckSolidity.MustBeSolid(mesh, trs)
                });
            }

            /// <summary>
            /// Emit the batch, and give it collision the mower can actually use.
            ///
            /// <paramref name="addCollider"/> now means "this whole batch needs a collider for its own
            /// reasons" — the surround is the ground, the stands are what the crowd's seating rays are
            /// fired at. It is no longer the only way a batch becomes solid, because that is what the
            /// "some obstacles are ignored" bug kept being: a per-batch flag set by hand while the
            /// question is per-prop. A batch may hold a hay bale standing where the mower drives and a
            /// tent forty metres outside the fence, and one flag cannot be right for both.
            ///
            /// So every instance is measured. Any that stand where the mower can reach them and fill
            /// enough of its contact band are baked into a SECOND mesh, from the same meshes and the
            /// same matrices as the visual, and that is what the collider gets — so the collision can
            /// never disagree with what is drawn, and nothing hittable can be left out by forgetting a
            /// flag. Instances the mower could drive at but could never hit are errors, not silence.
            /// </summary>
            public void Emit(Transform parent, string name, bool castShadow = true, bool addCollider = false)
            {
                int index = 0;
                foreach (var kv in _buckets)
                {
                    var all = new CombineInstance[kv.Value.Count];
                    var solid = new List<CombineInstance>();
                    for (int i = 0; i < kv.Value.Count; i++)
                    {
                        var item = kv.Value[i];
                        all[i] = item.instance;
                        // Instances too short to be hit are not reported from here: a prop is usually
                        // several instances, and its own solid parts shield the short ones. Judging
                        // that needs the finished scene, so DuckSolidity.Enforce does it.
                        if (item.solid) solid.Add(item.instance);
                    }

                    var combined = new Mesh { indexFormat = IndexFormat.UInt32 };
                    combined.CombineMeshes(all, true, true);
                    combined.RecalculateNormals();
                    combined.RecalculateBounds();
                    var saved = Save(combined, $"{name}_{index}");

                    var go = Spawn(parent, $"{name}_{kv.Key.name}", saved, kv.Key,
                                   Vector3.zero, Quaternion.identity, Vector3.one, castShadow);
                    go.isStatic = true;

                    Mesh collision = null;
                    if (addCollider) collision = saved;
                    else if (solid.Count > 0)
                    {
                        var hittable = new Mesh { indexFormat = IndexFormat.UInt32 };
                        hittable.CombineMeshes(solid.ToArray(), true, true);
                        hittable.RecalculateBounds();
                        collision = Save(hittable, $"{name}_{index}_Solid");
                    }
                    if (collision != null)
                    {
                        var mc = go.AddComponent<MeshCollider>();
                        mc.sharedMesh = collision;
                    }
                    index++;
                }
            }
        }

        // ------------------------------------------------------------------ ground

        static void BuildGround(Transform root)
        {
            var g = new GameObject("Ground").transform;
            g.SetParent(root, false);

            // One continuous surround, from the lawn's edge all the way to the horizon.
            //
            // This used to be two rings with two different materials, which made the 64 m
            // playfield read as a rug thrown onto a differently-coloured field — the dominant
            // shape in the overhead reveal was a rectangle rather than the picture. One mesh with
            // one material means the boundary is a mow line, not a material change. It also
            // closes the hole that used to exist between the two rings, which had no collider at
            // all and dropped the mower into empty space.
            // Flat across the whole championship ground, rolling only past it. The venue reaches
            // roughly 130 m from the origin at Bramble's far corner, so the level part runs to 150
            // and every plot, stand and station sits on true ground instead of on a wave.
            var surround = Save(DuckPrimitives.SquareRing(Lawn, MeadowOuter, 44, 4.5f, 23, VenueFlatRadius),
                                "Surround");
            var surroundGO = Spawn(g, "Surround", surround, Mat("M_Apron"),
                                   Vector3.zero, Quaternion.identity, Vector3.one, false);
            var mc = surroundGO.AddComponent<MeshCollider>();
            mc.sharedMesh = surround;
            surroundGO.layer = LayerMask.NameToLayer("Ground");

            // Push the collider into the physics scene now, because everything placed after this
            // point seats itself by raycasting against it. Unity's autoSyncTransforms is off in this
            // project (ProjectSettings/DynamicsManager), so a collider added from a script is not
            // queryable until something syncs — BuildCrowd already learned this the hard way when it
            // dropped rays onto the stands.
            Physics.SyncTransforms();

            BuildLane(g);
        }

        /// <summary>
        /// The worn lane in from the south gate — the way everyone arrives.
        ///
        /// It used to be ONE extruded box: 5.2 m wide, 52 m long, dead straight, one flat colour,
        /// with two perfectly parallel edges ruled across the grass. That is a large area in every
        /// wide shot and in the whole judging sequence, and a ruled rectangle of flat colour is most
        /// of why the venue read as a demo scene. The material half of that is fixed elsewhere
        /// (M_Earth carries the meadow's multi-scale variation); this is the geometry half, and it is
        /// three separate problems:
        ///
        /// SHAPE. A track worn by traffic is not straight and not a constant width. It is built as
        /// eleven overlapping segments that wander either side of the centre line and breathe between
        /// 4.2 and 6.4 m wide, which also means the surface is made of many quads at slightly
        /// different angles rather than one, so the light across it is not uniform.
        ///
        /// EDGES. The straight boundary is the other half of the demo look. Each segment is turned a
        /// few degrees off the run, so the outline is a ragged zigzag rather than two parallel lines,
        /// and the join is then broken up further by scallops of earth overlapping the boundary at
        /// irregular intervals. Nothing here is a straight line longer than about 5 m.
        ///
        /// WIDTH. It has to stay inside the entrance avenue: the gate hedges reach x = ±6.5 and the
        /// fence gate is 6.4 m wide, so the widest segment at 6.4 m plus its wander is the most the
        /// corridor takes.
        ///
        /// The whole thing is one combined mesh per material, so this costs two draw calls, the same
        /// as the single box did.
        /// </summary>
        static void BuildLane(Transform g)
        {
            var comb = new Combiner();
            var earth = Mat("M_Earth") ?? Mat("M_Dirt");

            // From the gate at z = -40 out past the hedge line to where the fog takes over.
            const float from = -40f, to = -94f;
            const int segments = 11;
            float step = (to - from) / segments;

            float wander = 0f;
            for (int i = 0; i < segments; i++)
            {
                float z0 = from + i * step;
                // A slow drift rather than per-segment noise: consecutive segments have to agree
                // about where the track is going or it reads as a zigzag of separate slabs.
                wander = Mathf.Clamp(wander + Range(-0.55f, 0.55f), -1.5f, 1.5f);
                float width = Range(4.2f, 6.4f);
                // Overlap each segment into the next by a fifth, so there is no seam to find.
                float len = Mathf.Abs(step) * 1.2f;

                var slab = Save(DuckPrimitives.ChamferBox(new Vector3(width * 0.5f, 0.02f, len * 0.5f), 0.5f),
                                $"LaneSeg_{i}");
                comb.Add(slab, Matrix4x4.TRS(new Vector3(wander, 0.012f, z0 + step * 0.5f),
                         Quaternion.Euler(0f, Range(-5f, 5f), 0f), Vector3.one), earth);

                // Scalloped edges: flat earth lobes spilling out of each side at irregular
                // intervals, so the boundary stops being a line.
                for (int e = -1; e <= 1; e += 2)
                {
                    int lobes = 2 + (Rand < 0.5f ? 1 : 0);
                    for (int k = 0; k < lobes; k++)
                    {
                        float lz = z0 + Range(0.2f, 0.8f) * step;
                        float lobe = Range(0.7f, 1.9f);
                        var patch = Save(DuckPrimitives.Hill(lobe, 0.03f, 2, 7, 1200 + i * 8 + k + (e > 0 ? 4 : 0)),
                                         $"LaneEdge_{i}_{k}_{(e > 0 ? "R" : "L")}");
                        comb.Add(patch, Matrix4x4.TRS(
                                 new Vector3(wander + e * (width * 0.5f - Range(0.1f, 0.5f)), 0.010f, lz),
                                 Quaternion.Euler(0f, Range(0f, 360f), 0f), Vector3.one), earth);
                    }
                }
            }

            // NO GRASS TUFTS. Ninety of them used to stand here and they were the worst thing in the
            // frame, so this is a deliberate absence rather than an omission.
            //
            // They were 14 cm chamfered boxes in M_Hedge, and every part of that was wrong. M_Hedge
            // is #2F6B45, a dark blue-green picked so the hedge RING would separate from the meadow
            // by hue at sixty metres; against pale tan earth the same colour is the darkest value in
            // the shot, so each tuft read as a hole punched in the ground. At that size a box does
            // not read as grass either — it reads as dropped crates — and a third of them were
            // scattered down the middle of the ruts, which is the one place a worn track has no
            // grass, because being worn is what makes it a track.
            //
            // The scallops and the ragged segment edges above already do the job the tufts were
            // added for: they stop the lane reading as a decal painted on the field. If real
            // planting is wanted at the margins later, use the actual blade geometry
            // (GrassField.BakeBladeMesh is public static, and RivalBlades shows how to instance it)
            // in the meadow's own colours, at the boundary only — not a box in the hedge colour.

            comb.Emit(g, "Lane", false);
        }

        /// <summary>
        /// The ground's true height at a point, read off the surround's own collider.
        ///
        /// Placing props at a hardcoded y = 0 is right for the venue and wrong for everything past
        /// it, because the surround is only level out to VenueFlatRadius (150 m) and rolls by up to
        /// 4.5 m beyond that — and the planting reaches 155 m from the quad centre, which is nearly
        /// 200 m from the origin at the corners. Trees out there were standing at y = 0 on ground
        /// that is not at y = 0, so they hovered or sank by metres.
        ///
        /// Measured, not derived: the surround's undulation is a sine field with a seeded phase
        /// inside SquareRing, and re-implementing that formula here would be a second copy to keep
        /// in step. A ray against the mesh cannot disagree with the mesh.
        ///
        /// Inside the lawn there is no collider to hit at build time — the player's LawnGround
        /// collider is created by GrassField at runtime — and the fallback of 0 is exactly right
        /// there, because that lawn is a flat quad at y = 0 by construction.
        /// </summary>
        static float GroundY(float x, float z)
        {
            int groundMask = 1 << LayerMask.NameToLayer("Ground");
            if (Physics.Raycast(new Vector3(x, 400f, z), Vector3.down, out RaycastHit hit, 800f,
                                groundMask, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return 0f;
        }

        /// <summary>The same point, seated on the ground.</summary>
        static Vector3 OnGround(Vector3 p)
        {
            p.y = GroundY(p.x, p.z);
            return p;
        }

        /// <summary>
        /// The lowest point of MESH once SCALE and ROT are applied — that is, how far below its own
        /// pivot the thing actually reaches.
        /// </summary>
        static float BottomOf(Mesh mesh, Quaternion rot, Vector3 scale)
        {
            if (mesh == null) return 0f;
            Bounds b = mesh.bounds;
            float min = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                         (i & 2) == 0 ? b.min.y : b.max.y,
                                         (i & 4) == 0 ? b.min.z : b.max.z);
                min = Mathf.Min(min, (rot * Vector3.Scale(scale, corner)).y);
            }
            return min;
        }

        /// <summary>
        /// A transform that STANDS a mesh on the ground, whatever its pivot and however it is turned.
        ///
        /// This exists because a hand-typed y is wrong in two independent ways at once, and the venue
        /// had both everywhere:
        ///
        /// THE PIVOT. Half the props in this file are written as `authored ?? primitive`, and the two
        /// do not agree about where their origin is: DuckPrimitives centres its boxes and cylinders
        /// on themselves, while the Blender exports stand on their own base. So every one of those
        /// pairs carried a lift that had been measured for ONE of the two branches — the apron hay
        /// bales are the clearest case, `spot + up * 0.55` for the primitive and `spot` for the
        /// authored mesh, with only the primitive's number ever checked. And 0.55 was wrong even for
        /// the primitive, because it is a 1.1 m cylinder laid on its SIDE, so what holds it off the
        /// ground is its 0.62 radius. Every one of those bales was 7 cm into the dirt.
        ///
        /// THE GROUND. y = 0 is the ground inside VenueFlatRadius and nowhere else. See GroundY.
        ///
        /// Both are measurements, so both are taken rather than typed: the mesh's own bounds through
        /// its own rotation for the first, a ray against the ground collider for the second. Anything
        /// that STANDS on something goes through here. Anything that HANGS — bunting, pennants,
        /// scallops, flags, fence rails — deliberately does not: those are positioned against the
        /// thing that holds them up, and seating them on the ground would drop them all to the grass.
        /// </summary>
        static Matrix4x4 Standing(Mesh mesh, Vector3 at, Quaternion rot, Vector3 scale)
        {
            float lift = GroundY(at.x, at.z) - BottomOf(mesh, rot, scale) + at.y;
            return Matrix4x4.TRS(new Vector3(at.x, lift, at.z), rot, scale);
        }

        static Matrix4x4 Standing(Mesh mesh, Vector3 at, Quaternion rot) => Standing(mesh, at, rot, Vector3.one);

        /// <summary>
        /// A length of cord between two points, as one thin bar turned to lie along them.
        ///
        /// This exists because the venue's bunting was hanging on NOTHING. Five hundred and sixty
        /// pennants around the fence and seventeen behind the judges, each one placed on a catenary
        /// curve and none of them attached to anything the eye can see — the two posts at the ends of
        /// a run are metres away from the flags in the middle of it, so what the player reported as
        /// "floating in the air with no post" was exactly and literally true. A pennant is not a prop,
        /// it is cloth pegged to a line, and if the line is not drawn the cloth is an artefact.
        ///
        /// Kept to a three-sided cylinder (24 triangles). At 3 cm across, the cross-section is far
        /// below what a pixel can resolve at any distance the bunting is seen from, and a run of them
        /// is the whole difference between a string of flags and a swarm.
        /// </summary>
        static void Cord(Combiner comb, Mesh cord, Vector3 a, Vector3 b, Material mat)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            comb.Add(cord, Matrix4x4.TRS((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d / len),
                                         new Vector3(1f, len, 1f)), mat);
        }

        /// <summary>Where the hills are, so nothing gets planted on their slopes.</summary>
        static readonly List<(Vector3 centre, float radius)> _hills = new List<(Vector3, float)>();

        static void BuildHills(Transform root)
        {
            var h = new GameObject("Hills").transform;
            h.SetParent(root, false);
            var mat = Mat("M_Hills");
            _hills.Clear();

            // A staggered ring of mounds. Varying radius, height and distance stops them
            // reading as a repeated element on the skyline.
            for (int i = 0; i < 13; i++)
            {
                float a = i / 13f * Mathf.PI * 2f + Range(-0.18f, 0.18f);
                float dist = Range(255f, 380f);
                float radius = Range(70f, 150f);
                // Deliberately flat. Tall mounds at this distance read as domes or bubbles,
                // not as landscape; a low, wide, overlapping ridge line reads as hills.
                float height = Range(9f, 21f);
                var mesh = Save(DuckPrimitives.Hill(radius, height, 4, 20, 100 + i), $"Hill_{i}");
                var pos = new Vector3(Mathf.Cos(a) * dist, Range(-6f, -2f), Mathf.Sin(a) * dist);
                Spawn(h, $"Hill_{i}", mesh, mat, pos,
                      Quaternion.Euler(0f, Range(0f, 360f), 0f), Vector3.one, false);
                _hills.Add((pos, radius));
            }
        }

        // ------------------------------------------------------------------ fence & bunting

        static void BuildFence(Transform root)
        {
            var f = new GameObject("Fence").transform;
            f.SetParent(root, false);

            var authoredPost = Authored("Props.fbx", "FencePost", "FencePost");
            var authoredRail = Authored("Props.fbx", "FenceRail", "FenceRail");
            var white = authoredPost != null ? AuthoredMat() : Mat("M_FenceWhite");

            var post = authoredPost ?? DuckPrimitives.ChamferBox(new Vector3(0.055f, 0.525f, 0.045f), 0.018f);
            var cap = authoredPost != null ? null : DuckPrimitives.ChamferBox(new Vector3(0.075f, 0.05f, 0.065f), 0.022f);
            var rail = authoredRail ?? DuckPrimitives.ChamferBox(new Vector3(1.1f, 0.05f, 0.03f), 0.014f);
            // The authored post already stands on its own base; the procedural one is centred.
            float postLift = authoredPost != null ? 0f : 0.525f;

            var comb = new Combiner();
            const float spacing = 2.2f;
            int perSide = Mathf.RoundToInt(FenceRadius * 2f / spacing);

            for (int side = 0; side < 4; side++)
            {
                for (int i = 0; i <= perSide; i++)
                {
                    float t = -FenceRadius + i * spacing;
                    // Leave a gate on the south side where the lane arrives.
                    if (side == 1 && Mathf.Abs(t) < 3.2f) continue;

                    Vector3 p = side switch
                    {
                        0 => new Vector3(t, 0f, FenceRadius),
                        1 => new Vector3(t, 0f, -FenceRadius),
                        2 => new Vector3(FenceRadius, 0f, t),
                        _ => new Vector3(-FenceRadius, 0f, t),
                    };
                    float yaw = side < 2 ? 0f : 90f;
                    // Every post leans a little; a perfectly straight fence reads as CAD.
                    var rot = Quaternion.Euler(Range(-2.5f, 2.5f), yaw + Range(-3f, 3f), Range(-2.5f, 2.5f));

                    comb.Add(post, Matrix4x4.TRS(p + Vector3.up * postLift, rot, Vector3.one), white);
                    if (cap != null) comb.Add(cap, Matrix4x4.TRS(p + Vector3.up * 1.075f, rot, Vector3.one), white);

                    if (i < perSide && !(side == 1 && Mathf.Abs(t + spacing * 0.5f) < 3.6f))
                    {
                        Vector3 mid = p + (side < 2 ? Vector3.right : Vector3.forward) * (spacing * 0.5f);
                        var rrot = Quaternion.Euler(0f, yaw, Range(-1.5f, 1.5f));
                        comb.Add(rail, Matrix4x4.TRS(mid + Vector3.up * 0.78f, rrot, Vector3.one), white);
                        comb.Add(rail, Matrix4x4.TRS(mid + Vector3.up * 0.40f, rrot, Vector3.one), white);
                    }
                }
            }

            comb.Emit(f, "Fence");

            // An invisible wall just inside the fence so the mower cannot escape the fair.
            //
            // Built out from MowerContact.ReachRadius rather than in from FenceRadius, because this
            // wall's inner face IS the definition of "where the mower can drive" — and that is what
            // decides which props in the venue have to be solid. Written as two independent numbers
            // it was only a matter of time before a fence tweak moved the wall and left every
            // obstacle check certifying the wrong area. Same 39.6 m centre as before.
            const float wallThickness = 0.6f;
            for (int side = 0; side < 4; side++)
            {
                var go = new GameObject($"Bound_{side}");
                go.transform.SetParent(f, false);
                var box = go.AddComponent<BoxCollider>();
                float r = MowerContact.ReachRadius + wallThickness * 0.5f;
                if (side < 2)
                {
                    go.transform.position = new Vector3(0f, 1.2f, side == 0 ? r : -r);
                    box.size = new Vector3(r * 2f + 2f, 2.4f, wallThickness);
                }
                else
                {
                    go.transform.position = new Vector3(side == 2 ? r : -r, 1.2f, 0f);
                    box.size = new Vector3(wallThickness, 2.4f, r * 2f + 2f);
                }
                go.layer = LayerMask.NameToLayer("Prop");
            }
        }

        static void BuildBunting(Transform root)
        {
            var b = new GameObject("Bunting").transform;
            b.SetParent(root, false);

            var red = Mat("M_TentRed");
            var cream = Mat("M_TentCream");
            var flag = DuckPrimitives.Prism(0.26f, 0.30f, 0.012f);
            var cordMesh = DuckPrimitives.Cylinder(0.016f, 0.016f, 1f, 3, 0.004f);
            var cordMat = Mat("M_WoodDark");

            // The line is strung from POST TOP to POST TOP, measured off the post.
            //
            // It used to run at a typed 1.28, which is 15 cm above the top of the 1.05 m fence post it
            // is nailed to — so even the two ends of each run were attached to thin air. The post mesh
            // is fetched the same way BuildFence fetches it so the two cannot disagree; the procedural
            // fallback is a 1.05 m post with a cap centred at 1.075, hence 1.125.
            var authoredPost = Authored("Props.fbx", "FencePost", "FencePost");
            float postTop = authoredPost != null ? authoredPost.bounds.size.y : 1.125f;

            var comb = new Combiner();
            const float spacing = 2.2f;
            int perSide = Mathf.RoundToInt(FenceRadius * 2f / spacing);

            for (int side = 0; side < 4; side++)
            {
                for (int i = 0; i < perSide; i++)
                {
                    float t0 = -FenceRadius + i * spacing;
                    if (side == 1 && Mathf.Abs(t0) < 4f) continue;

                    // Where a point at fraction u along this span sits, on the sagging line.
                    Vector3 OnLine(float u)
                    {
                        float t = t0 + u * spacing;
                        Vector3 q = side switch
                        {
                            0 => new Vector3(t, 0f, FenceRadius),
                            1 => new Vector3(t, 0f, -FenceRadius),
                            2 => new Vector3(FenceRadius, 0f, t),
                            _ => new Vector3(-FenceRadius, 0f, t),
                        };
                        q.y = postTop - Mathf.Sin(u * Mathf.PI) * 0.28f;
                        return q;
                    }

                    // The cord itself, as two straight lengths through the bottom of the sag. Two is
                    // enough at 2.2 m: the pennants hang within 5 cm of the chord either side of the
                    // middle, which no camera in the game can resolve, and a dozen segments per span
                    // would cost more triangles than the whole fence.
                    Cord(comb, cordMesh, OnLine(0f), OnLine(0.5f), cordMat);
                    Cord(comb, cordMesh, OnLine(0.5f), OnLine(1f), cordMat);

                    // Flags hang from that line, so they read as pegged to it.
                    for (int k = 1; k <= 4; k++)
                    {
                        Vector3 p = OnLine(k / 5f);
                        var rot = Quaternion.Euler(180f, side < 2 ? 0f : 90f, Range(-9f, 9f));
                        comb.Add(flag, Matrix4x4.TRS(p, rot, Vector3.one),
                                 ((i * 4 + k) % 2 == 0) ? red : cream);
                    }
                }
            }

            comb.Emit(b, "Bunting", false);
        }

        // ------------------------------------------------------------------ spectator ring

        static void BuildStands(Transform root)
        {
            var s = new GameObject("Stands").transform;
            s.SetParent(root, false);

            var wood = Mat("M_WoodWarm");
            var woodDark = Mat("M_WoodDark");
            var comb = new Combiner();

            var authoredStand = Authored("Landmarks.fbx", "Stands", "Stands");
            if (authoredStand != null)
            {
                // One authored section repeated along each touchline, turned to face the field.
                for (int sideIndex = 0; sideIndex < 2; sideIndex++)
                {
                    float sxa = sideIndex == 0 ? 1f : -1f;
                    float baseXa = sxa * StandOffset;
                    for (int k = -1; k <= 1; k++)
                    {
                        var rot = Quaternion.Euler(0f, sxa > 0f ? -90f : 90f, 0f);
                        comb.Add(authoredStand,
                                 Matrix4x4.TRS(new Vector3(baseXa, 0f, k * 11f), rot, Vector3.one),
                                 AuthoredMat());
                    }
                }
                comb.Emit(s, "Stands", true, addCollider: true);
                return;
            }

            // Two tiered stands facing each other across the field.
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                float sx = sideIndex == 0 ? 1f : -1f;
                float baseX = sx * (FenceRadius + 3.4f);

                for (int tier = 0; tier < 4; tier++)
                {
                    float y = 0.42f + tier * 0.52f;
                    float x = baseX + sx * tier * 0.95f;
                    var plank = DuckPrimitives.ChamferBox(new Vector3(0.42f, 0.07f, 15f), 0.03f);
                    var riser = DuckPrimitives.ChamferBox(new Vector3(0.45f, y * 0.5f, 15f), 0.03f);

                    comb.Add(plank, Matrix4x4.TRS(new Vector3(x, y, Range(-0.4f, 0.4f)), Quaternion.identity, Vector3.one), Mat("M_WoodWarmZ"));
                    comb.Add(riser, Matrix4x4.TRS(new Vector3(x + sx * 0.42f, y * 0.5f, 0f), Quaternion.identity, Vector3.one), Mat("M_WoodDarkZ"));
                }

                // End frames so the stand has a structure rather than floating planks.
                for (int e = -1; e <= 1; e += 2)
                {
                    var frame = DuckPrimitives.ChamferBox(new Vector3(2.4f, 0.09f, 0.14f), 0.04f);
                    comb.Add(frame, Matrix4x4.TRS(new Vector3(baseX + sx * 1.4f, 1.35f, e * 15f),
                                                  Quaternion.Euler(0f, 0f, sx * 26f), Vector3.one), woodDark);
                    var leg = DuckPrimitives.ChamferBox(new Vector3(0.11f, 1.3f, 0.11f), 0.035f);
                    comb.Add(leg, Matrix4x4.TRS(new Vector3(baseX + sx * 2.8f, 1.3f, e * 15f), Quaternion.identity, Vector3.one), Mat("M_WoodPost"));
                    comb.Add(leg, Matrix4x4.TRS(new Vector3(baseX, 1.3f, e * 15f), Quaternion.identity, Vector3.one), Mat("M_WoodPost"));
                }
            }

            comb.Emit(s, "Stands", true, addCollider: true);
        }

        static void BuildAwningAndScoreboard(Transform root)
        {
            var a = new GameObject("JudgeStand").transform;
            a.SetParent(root, false);

            var red = Mat("M_TentRed");
            var cream = Mat("M_TentCream");
            var wood = Mat("M_WoodDark");
            var comb = new Combiner();

            // Striped awning over the judges' bench on the south edge.
            float z = -(Field.Half + 7.5f);
            var post = DuckPrimitives.ChamferBox(new Vector3(0.09f, 1.4f, 0.09f), 0.03f);
            for (int i = -1; i <= 1; i += 2)
                for (int k = -1; k <= 1; k += 2)
                    comb.Add(post, Matrix4x4.TRS(new Vector3(i * 3.9f, 1.4f, z + k * 1.5f), Quaternion.identity, Vector3.one), Mat("M_WoodPost"));

            // Prism builds its ridge along Z, so the 90-degree turn is what puts the ridge along
            // the bench — but the width and depth have to be swapped to match, or the turn leaves a
            // 3.6 m awning pointing 9 m out into the field. The scallops below are separate pieces
            // in the same combiner, which is why turning the finished object in the editor took
            // the bunting with it: the fix belongs here, not on the transform.
            var canopy = DuckPrimitives.Prism(3.6f, 1.05f, 9.0f);
            comb.Add(canopy, Matrix4x4.TRS(new Vector3(0f, 2.8f, z), Quaternion.Euler(0f, 90f, 0f), Vector3.one), red);

            // Scallop of alternating stripes along the awning edge.
            //
            // Hung from 2.80, the height of the canopy's eave, and not 2.62. The canopy is a prism
            // whose slope reaches zero height at its edge, so its lip is at its own base plane of
            // 2.8 — and a valance turned upside down hangs DOWNWARD from wherever it is placed, so
            // 2.62 left an 18 cm strip of daylight between the awning and the frill nailed to it.
            var scallop = DuckPrimitives.Prism(0.55f, 0.34f, 0.03f);
            for (int i = 0; i < 16; i++)
            {
                float x = -4.2f + i * 0.56f;
                comb.Add(scallop, Matrix4x4.TRS(new Vector3(x, 2.80f, z + 1.80f), Quaternion.Euler(180f, 0f, 0f), Vector3.one),
                         (i % 2 == 0) ? cream : red);
            }

            // The awning casts again, and this is the third place the same misdiagnosis was made.
            //
            // It was switched off because it dropped the whole bench — faces, scorecards, desk —
            // into a flat murky green. That was read as "the lid is too big", but the murk came
            // from the volume profile containing no post-processing at all and an ambient probe so
            // saturated it dyed surfaces instead of filling them. Under those conditions ANY shade
            // went murky, so every caster over the bench got turned off in turn: this awning, then
            // the bench itself, then the backdrop behind it. Between them they removed every
            // shadow from the one set the game pushes in on.
            //
            // A striped awning throwing real shade over three animals at a desk is the shot. It is
            // only worth having now that a shaded face keeps its colour.
            comb.Emit(a, "JudgeStand", castShadow: true);

            // No board on the north edge. There were three boards on this axis and this was two of
            // them, standing in the same half metre of ground:
            //
            //   - a procedural panel-on-two-legs at (0, 3.4, 42.2), 6.2 m wide, and
            //   - the AUTHORED ScoreboardProp from Props.fbx at (0, 0, 42.4), 1.8 m wide,
            //
            // which is the small blackboard the player found underneath the big one. Measured, the
            // authored prop's top 0.78 m sat inside the procedural panel's volume. This is the
            // "built twice, once procedurally and once from the FBX, neither aware of the other"
            // shape, and it is a guaranteed overlap every time it happens: the procedural version
            // was written first, the authored prop was added later by a pass that placed it at the
            // same landmark, and nothing joins the two up. Both are gone.
            //
            // Nothing is lost. The board that CARRIES the standings is the plaza one built by
            // DuckVenueBuilder.BuildScoreboard, which is where the Scoreboard component lives and
            // where the closing shot is composed; and the north BEARING is NorthBoard at z = 58,
            // which the tree ring reserves a 26-degree gap for and which the player navigates by
            // once the chalk guide dissolves. Neither of the two removed here was referenced by
            // name from any script.
        }

        static void BuildTents(Transform root)
        {
            var t = new GameObject("Tents").transform;
            t.SetParent(root, false);
            var comb = new Combiner();

            // Positions are shared with the keep-out registry, because three of these four tents sit
            // inside the near tree ring's radius band and two of them are inside a grove as well.
            var positions = TentSpots;
            var yaws = TentYaws;
            var sizes = TentSizes;

            var tentA = Authored("Landmarks.fbx", "Tent_A", "Tent_A");
            var tentB = Authored("Landmarks.fbx", "Tent_B", "Tent_B");

            for (int i = 0; i < positions.Length; i++)
            {
                float w = sizes[i];

                if (tentA != null || tentB != null)
                {
                    var authoredTent = (i % 2 == 0 ? tentA : tentB) ?? tentA ?? tentB;
                    comb.Add(authoredTent,
                             Matrix4x4.TRS(OnGround(positions[i]), Quaternion.Euler(0f, yaws[i], 0f),
                                           Vector3.one * (w / 5.2f)),
                             AuthoredMat());
                    continue;
                }
                var body = DuckPrimitives.ChamferBox(new Vector3(w * 0.5f, 1.05f, w * 0.42f), 0.06f);
                var roof = DuckPrimitives.Prism(w * 1.12f, w * 0.42f, w * 0.92f);
                var rot = Quaternion.Euler(0f, yaws[i], 0f);
                Vector3 p = positions[i];

                comb.Add(body, Matrix4x4.TRS(p + Vector3.up * 1.05f, rot, Vector3.one), Mat("M_TentCream"));
                comb.Add(roof, Matrix4x4.TRS(p + Vector3.up * 2.1f, rot, Vector3.one), Mat("M_TentRed"));

                var pole = DuckPrimitives.Cylinder(0.06f, 0.05f, 3.4f, 8);
                for (int k = 0; k < 4; k++)
                {
                    float sx = (k % 2 == 0) ? -1f : 1f;
                    float sz = (k < 2) ? -1f : 1f;
                    Vector3 off = rot * new Vector3(sx * w * 0.55f, 0f, sz * w * 0.46f);
                    comb.Add(pole, Matrix4x4.TRS(p + off + Vector3.up * 1.7f, rot, Vector3.one), Mat("M_WoodWarm"));
                }
            }

            comb.Emit(t, "Tents");
        }

        // ------------------------------------------------------------------ landscape

        static void BuildPond(Transform root)
        {
            var p = new GameObject("Pond").transform;
            p.SetParent(root, false);
            // West of the venue, clear of every plot and of the scoreboard plaza. It used to sit
            // at (64,56), which is now the middle of the championship square — the water was
            // rendering straight through the plaza floor. The position lives in one place so the
            // planting can keep out of it.
            Vector3 centre = PondCentre;

            // ---- why there is no basin bowl any more, from the measurements ----
            //
            // The sunken bowl could not be seen, and could never have been seen. Three numbers
            // settle it:
            //
            //   the ground here is Surround, one continuous SquareRing from 32 m to 470 m with NO
            //   hole in it, held dead level at y = 0 everywhere inside VenueFlatRadius (150 m);
            //   M_Water and M_Dirt are both Duck/Prop, an opaque "Queue"="Geometry" material with
            //   ZTest LEqual — so nothing below y = 0 draws at all here;
            //   the bowl's highest point is its own rim, and it was placed 2.4 m DOWN.
            //
            // So the basin sat 2.4 m under an opaque sheet, and the water 0.55 m under it. The player
            // was right that the pond is buried, and right that the basin is not needed: its winding
            // fix was real and correct, but the mesh it fixed is invisible geometry and no camera in
            // the game can reach a position that sees it. Cutting a hole in the ring instead is not a
            // local change — every plot apron in the venue is built by SquareRing.
            //
            // What replaces it is a bank that is ABOVE the ground rather than below it: a ring of low
            // mud mounds around the rim, with the water lying just inside them. A Hill dome falls to
            // zero height at its own rim, so a dome placed at y = 0 meets the flat ground flush —
            // there is no exposed open edge to see through and nothing coplanar to z-fight, which is
            // exactly why this shape can be used above ground and the bowl cannot.
            var bankComb = new Combiner();
            var comb = new Combiner();
            // M_Earth, not M_Dirt: the bank is the one piece of bare ground the player gets close to,
            // and a flat single-colour mud ring would undo the whole point of building it.
            var earth = Mat("M_Earth") ?? Mat("M_Dirt");

            // Water first, so the bank can be built to enclose it. It clears the grass by 0.06,
            // which is five times the dirt lane's own 0.012 lift and far too small to read as a step
            // at the sixty metres this is ever seen from — but it is the difference between the west
            // bearing being a bright shape on the map and not being there.
            var water = Save(DuckPrimitives.Hill(16.2f, 0.05f, 2, 22, 78), "PondWater");
            Spawn(p, "Water", water, Mat("M_Water"), centre + Vector3.up * 0.06f, Quaternion.identity, Vector3.one, false);

            // The bank: eighteen overlapping mud mounds around the rim, no two the same size.
            //
            // Overlapping is the point. Eighteen separate domes at even spacing would read as a ring
            // of blobs; overlapping them by roughly a third turns them into one continuous shoulder
            // with an irregular top line, which is what a dug-out farm pond's spoil bank looks like.
            // The rim radius is 17.8 against the water's nominal 16.2, so the bank sits just outside
            // the water on every bearing even though Hill wobbles both by up to ±22%.
            for (int i = 0; i < 18; i++)
            {
                float a = i / 18f * Mathf.PI * 2f + Range(-0.06f, 0.06f);
                float r = 17.8f + Range(-0.7f, 0.7f);
                float width = Range(2.6f, 4.2f);
                var mound = Save(DuckPrimitives.Hill(width, Range(0.26f, 0.52f), 3, 9, 900 + i), $"PondBankMound_{i}");
                bankComb.Add(mound, Matrix4x4.TRS(centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r),
                             Quaternion.Euler(0f, Range(0f, 360f), 0f), Vector3.one), earth);
            }
            bankComb.Emit(p, "PondBank", false);

            // Reed clumps around the near bank only — that is the side the player ever sees.
            // Pulled in to 15.2-17.6 so they stand between the water's edge and the top of the bank,
            // which is where reeds actually grow. At 14.5 the innermost of them were out in open
            // water with the surface now visible under them.
            var reed = DuckPrimitives.ChamferBox(new Vector3(0.05f, 0.65f, 0.05f), 0.02f);
            for (int i = 0; i < 90; i++)
            {
                float a = Range(2.2f, 5.6f);
                float r = Range(15.2f, 17.6f);
                Vector3 pos = centre + new Vector3(Mathf.Cos(a) * r, Range(0.1f, 0.5f), Mathf.Sin(a) * r);
                comb.Add(reed, Matrix4x4.TRS(pos, Quaternion.Euler(Range(-14f, 14f), Range(0f, 360f), Range(-14f, 14f)),
                                             new Vector3(1f, Range(0.7f, 1.5f), 1f)), Mat("M_Hedge"));
            }
            comb.Emit(p, "Reeds", false);
        }

        /// <summary>Trunks already in the ground, so nothing is planted on top of one.</summary>
        static readonly List<(Vector2 at, float radius)> _planted = new List<(Vector2, float)>();
        static int _rejected;

        static void BuildFoliage(Transform root)
        {
            var f = new GameObject("Foliage").transform;
            f.SetParent(root, false);
            var comb = new Combiner();
            _planted.Clear();
            _rejected = 0;

            var wood = Mat("M_WoodDark");
            var hedge = Mat("M_Hedge");
            var canopy = Mat("M_Canopy");
            var propMat = AuthoredMat();

            // Three authored tree species, placed with varied scale and rotation. Three distinct
            // silhouettes beat one silhouette repeated, which is what the stand-ins looked like.
            var treeMeshes = new[]
            {
                Authored("Foliage.fbx", "Tree_Oak", "Tree_Oak"),
                Authored("Foliage.fbx", "Tree_Poplar", "Tree_Poplar"),
                Authored("Foliage.fbx", "Tree_Apple", "Tree_Apple")
            };
            bool haveTrees = treeMeshes[0] != null || treeMeshes[1] != null || treeMeshes[2] != null;
            var trunk = DuckPrimitives.Cylinder(0.34f, 0.24f, 3.2f, 8, 0.05f);

            // Canopies are three offset blobs rather than one sphere, so the silhouette has
            // lobes and reads as a tree from any angle.
            Mesh Canopy(int seed, float scale)
            {
                var m = DuckPrimitives.Hill(3.2f * scale, 3.6f * scale, 4, 14, seed);
                return Save(m, $"Canopy_{seed}");
            }

            void Tree(Vector3 pos, float scale, int seed)
            {
                // Never on somebody's competition lawn or its apron, and never on the venue's own
                // furniture. The scatter was written when there was one plot at the origin; the
                // orchard to the north and the random ring both now fall across rival ground, and a
                // lawn-art plot with an oak growing out of it is not a lawn-art plot. The keep-out
                // list covers everything else that got hit — see RegisterKeepOuts.
                if (BlockedForScenery(pos)) { _rejected++; return; }

                // No two trunks in the same place.
                //
                // Canopies are supposed to interleave — that is what a wood looks like from
                // underneath, and refusing it would turn every grove into an orchard. Trunks are
                // different: the clumping passes offset a companion by up to 6 m on each axis
                // INDEPENDENTLY, which includes offsetting it by almost nothing, and two trees
                // sharing a trunk is not a clump, it is one tree with a rendering fault.
                float trunkKeep = 0.55f * scale + 0.4f;
                foreach (var (at, r) in _planted)
                {
                    float dx = pos.x - at.x, dz = pos.z - at.y;
                    float keep = trunkKeep + r;
                    if (dx * dx + dz * dz < keep * keep) { _rejected++; return; }
                }
                _planted.Add((new Vector2(pos.x, pos.z), trunkKeep));

                // Stand it on the ground it is actually standing on.
                //
                // Every caller here passes y = 0, which is true only inside VenueFlatRadius. The
                // groves reach 155 m from the quad centre at (48, 48) — nearly 200 m from the origin
                // at the corners — and out there the surround rolls by up to 4.5 m, so a tree placed
                // at y = 0 was hanging in the air over a dip or buried to the knee in a rise. This is
                // also the single hardest kind of fault to see in a screenshot, because a floating
                // tree at 180 m in haze just looks like a tree.
                pos.y = GroundY(pos.x, pos.z);

                var rot = Quaternion.Euler(0f, Range(0f, 360f), 0f);

                if (haveTrees)
                {
                    // Bias the mix by location seed so orchards read as orchards and hedgerows
                    // read as hedgerows, rather than a random scatter of species.
                    var m = treeMeshes[seed % 3] ?? treeMeshes[0] ?? treeMeshes[1] ?? treeMeshes[2];
                    if (m != null)
                    {
                        comb.Add(m, Matrix4x4.TRS(pos, rot,
                            new Vector3(scale * Range(0.92f, 1.1f), scale * Range(0.9f, 1.18f), scale * Range(0.92f, 1.1f))),
                            propMat);
                        return;
                    }
                }

                comb.Add(trunk, Matrix4x4.TRS(pos + Vector3.up * 1.6f * scale, rot,
                                              new Vector3(scale, scale, scale)), wood);
                var c = Canopy(seed, scale);
                comb.Add(c, Matrix4x4.TRS(pos + Vector3.up * 3.1f * scale, rot, Vector3.one), canopy);
                comb.Add(c, Matrix4x4.TRS(pos + new Vector3(1.1f * scale, 2.4f * scale, -0.7f * scale),
                                          Quaternion.Euler(0f, Range(0f, 360f), 0f), Vector3.one * 0.66f), canopy);
                comb.Add(c, Matrix4x4.TRS(pos + new Vector3(-0.9f * scale, 2.2f * scale, 0.9f * scale),
                                          Quaternion.Euler(0f, Range(0f, 360f), 0f), Vector3.one * 0.58f), canopy);
            }

            // Planting stops well short of the skyline, and it clumps.
            //
            // Two mistakes in the previous pass. It scattered trees evenly out to 235 m, which put
            // them inside the hazed band where the hills live — and those hills are deliberately
            // washed out with atmosphere to read as distance, so sharp green trees standing in
            // front of them destroyed the depth they were doing all the work to create. And an even
            // scatter is not how trees grow: uniform spacing reads as procedural placement no
            // matter how good the individual tree is.
            //
            // So: everything lives inside TreeLimit, comfortably clear of the haze, and it is
            // planted as woods and parkland — a few dense groves with a soft edge, a thin parkland
            // scatter near the venue, and specimen trees on the ground the tour flies over.
            var quadCentre = new Vector3(Venue.Spacing * 0.5f, 0f, Venue.Spacing * 0.5f);
            // Pulled in toward the venue. Woods sitting out near the limit read as a distant tree
            // line with a bare gap between them and the championship ground; brought closer they
            // enclose it, and the plots feel like they are in a park rather than on a plain.
            const float TreeLimit = 155f;

            // A grove: dense in the middle, thinning out, with no hard edge.
            void Grove(Vector3 centre, float radius, int count, int seed, float minScale, float maxScale)
            {
                for (int i = 0; i < count; i++)
                {
                    // sqrt keeps the density even per unit area rather than piling up at the middle;
                    // the extra bias term is what actually makes it a grove rather than a disc.
                    float r = radius * Mathf.Sqrt(Rand) * Mathf.Lerp(0.55f, 1f, Rand);
                    float a = Range(0f, Mathf.PI * 2f);
                    Vector3 p = centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    if ((p - quadCentre).sqrMagnitude > TreeLimit * TreeLimit) continue;
                    // Smaller toward the edge of the grove, so it feathers out instead of stopping.
                    float edge = 1f - Mathf.Clamp01(r / Mathf.Max(radius, 0.01f));
                    Tree(p, Mathf.Lerp(minScale, maxScale, edge * 0.7f + Rand * 0.3f), seed + i);
                }
            }

            // Four woods, placed on the quarters the venue does not use and kept off the skyline.
            Grove(quadCentre + new Vector3(-118f, 0f, -30f), 32f, 26, 200, 0.85f, 1.45f);
            Grove(quadCentre + new Vector3(-86f, 0f, 96f), 28f, 20, 240, 0.80f, 1.35f);
            Grove(quadCentre + new Vector3(94f, 0f, -104f), 30f, 22, 270, 0.80f, 1.40f);
            Grove(quadCentre + new Vector3(112f, 0f, 74f), 26f, 18, 300, 0.75f, 1.30f);

            // An orchard: the one place regular spacing is right, because someone planted it.
            for (int gz = 0; gz < 4; gz++)
                for (int gx = 0; gx < 6; gx++)
                    Tree(quadCentre + new Vector3(-124f + gx * 11f + Range(-1.5f, 1.5f), 0f,
                                                  30f + gz * 11f + Range(-1.5f, 1.5f)),
                         Range(0.72f, 0.95f), 340 + gz * 6 + gx);

            // Parkland: singles and pairs on the open ground between the plots and the woods, thin
            // enough that the venue still reads as the subject.
            for (int i = 0; i < 20; i++)
            {
                float a = Range(0f, Mathf.PI * 2f);
                float r = Range(70f, 118f);
                Vector3 p = quadCentre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                Tree(p, Range(0.9f, 1.45f), 400 + i);
                // Half of them get a companion, because parkland trees come in twos and threes.
                // Offset in POLAR terms with a floor on the distance: a box offset of ±7 on each
                // axis independently includes (0.1, 0.2), and a companion planted a handspan from
                // its parent is two trunks in one hole rather than a pair of trees.
                if (Rand < 0.5f)
                    Tree(p + Companion(4.5f, 8f), Range(0.7f, 1.1f), 440 + i);
            }

            // A near ring around the PLAYER's lawn.
            //
            // Everything above is laid out around the quad centre at (48, 48), which is right for
            // the tour but leaves the one view the player actually spends the round inside — the
            // chase camera, looking out over their own fence — staring across sixty metres of bare
            // meadow at a thin line of trees on the horizon. That gap is what makes the map read as
            // empty however much planting there is elsewhere.
            //
            // So: a band from 54 to 74 m about the origin, dense enough to close the horizon in
            // every direction the chase camera can point. Four gaps are left in it, because the
            // compass landmarks have to stay clear — a barn with a wood in front of it is not a
            // bearing. The gaps also stop the ring reading as a hedge maze wall.
            {
                // Angles are measured with x = cos, z = sin, so 0 is east and 90 is north.
                //
                // The gaps are no longer all the same width, because the four things they protect
                // are not the same size. Three of them are single objects about 13 m across, which
                // 26 degrees clears comfortably at this radius. The judging stand is not: its
                // backdrop is a set — marquee, four hay stacks, seven flag poles — spread from
                // x = -25 to x = +26 between 50 and 78 m out, and the widest of those pieces sits
                // 23 degrees off south. At 26 degrees the ring was free to plant trees among the
                // flag poles at both ends of the one shot the whole round builds toward.
                //
                // 32 leaves five degrees of margin over that 23, which is what a full-grown oak
                // subtends at this distance (2.9 m of leaf, scaled up to 1.7, at 55 m).
                var cardinalGaps = new[]
                {
                    (bearing: 0f, halfWidth: 26f),     // barn
                    (bearing: 90f, halfWidth: 26f),    // display board
                    (bearing: 180f, halfWidth: 26f),   // pond
                    (bearing: 270f, halfWidth: 32f),   // judging stand and its backdrop
                };

                for (int i = 0; i < 54; i++)
                {
                    float deg = Range(0f, 360f);

                    bool blocked = false;
                    foreach (var (bearing, halfWidth) in cardinalGaps)
                        if (Mathf.Abs(Mathf.DeltaAngle(deg, bearing)) < halfWidth) { blocked = true; break; }
                    if (blocked) continue;

                    float a = deg * Mathf.Deg2Rad;
                    // Clumped rather than evenly spaced: pairs and threes at a shared bearing.
                    float r = Range(54f, 74f);
                    Vector3 p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    Tree(p, Range(0.95f, 1.55f), 600 + i);

                    if (Rand < 0.55f)
                        Tree(p + Companion(4f, 7f), Range(0.7f, 1.15f), 660 + i);
                }
            }

            // Specimen trees close in, on the ground the tour camera crosses between plots.
            //
            // Two of these six were planted through a tent, which is half of what the player was
            // reporting when they said the trees and the benches overlap. Both were placed before
            // the tents existed at those positions and nothing was checking:
            //
            //   (48, -34)  a 2.7 m apple against the 6 m tent at (49, -38), which spans x 45.2 to
            //              52.9 and z -41.8 to -34.2: a 5.4 x 2.5 m intersection with no jitter at
            //              all, and up to 7.5 x 4.5 m with it. Moved south-west to (44, -52), which
            //              is still on the run between the player's plot and HORACE's.
            //   (-34, 46)  a 4.5 m oak reaching the tent at (-40, 53) whenever the ±2 m jitter went
            //              its way. Moved to (-27, 52), ten metres clear, and still beside the
            //              tour's run north to MARGOT.
            //
            // (-30, -46) moved three metres further out as well: its canopy crossed the fence line
            // at z = -40 on a positive jitter.
            var specimens = new[]
            {
                new Vector3(44f, 0f, -52f), new Vector3(-27f, 0f, 52f), new Vector3(140f, 0f, 44f),
                new Vector3(46f, 0f, 142f), new Vector3(-30f, 0f, -49f), new Vector3(142f, 0f, -30f),
            };
            for (int i = 0; i < specimens.Length; i++)
                Tree(specimens[i] + new Vector3(Range(-2f, 2f), 0f, Range(-2f, 2f)),
                     Range(1.0f, 1.4f), 500 + i);

            // Clipped hedge blocks framing the approach to the gate.
            var authoredHedge = Authored("Foliage.fbx", "Hedge_Straight", "Hedge_Straight");
            var block = authoredHedge ?? DuckPrimitives.ChamferBox(new Vector3(1.5f, 0.85f, 0.75f), 0.28f);
            var hedgeMat = authoredHedge != null ? propMat : hedge;
            float hedgeLift = authoredHedge != null ? 0f : 0.85f;
            for (int i = 0; i < 8; i++)
            {
                float z = -48f - i * 3.4f;
                foreach (float sx in new[] { -5.2f, 5.2f })
                {
                    float x = sx + Range(-0.2f, 0.2f);
                    comb.Add(block, Matrix4x4.TRS(new Vector3(x, GroundY(x, z) + hedgeLift, z),
                                                  Quaternion.Euler(0f, Range(-5f, 5f), 0f),
                                                  new Vector3(Range(0.92f, 1.08f), Range(0.9f, 1.1f), 1f)), hedgeMat);
                }
            }

            comb.Emit(f, "Foliage");

            // Say how much the keep-outs threw away. A rejection is silent by design — a tree that
            // would have grown through a tent simply is not planted — but silent means nobody notices
            // when a change starts rejecting most of the ring, which would thin the horizon back out
            // to the bare meadow this planting exists to close.
            Debug.Log($"[Duck] Foliage: {_planted.Count} trees planted, {_rejected} placements rejected " +
                      $"(competition ground, pond, hills, venue structures, or another trunk).");
        }

        /// <summary>
        /// Every seat that physically exists on one stand.
        ///
        /// Candidates are spaced at roughly one spectator per 0.8 m across the collider's footprint
        /// and dropped from above. A hit on this collider above knee height is a bench; a hit on
        /// anything else — the ground, the apron — is discarded rather than moved, because a
        /// spectator standing in a field is exactly the artefact this is here to prevent.
        /// </summary>
        static List<SpectatorCrowd.Seat> SeatsOn(MeshCollider stand)
        {
            var seats = new List<SpectatorCrowd.Seat>();
            Bounds b = stand.bounds;

            const float step = 0.8f;
            int nx = Mathf.Max(2, Mathf.RoundToInt(b.size.x / step));
            int nz = Mathf.Max(2, Mathf.RoundToInt(b.size.z / step));

            // Face the middle of the arena: the stands ring the player's plot, so the crowd looks
            // inward at whichever side they are on.
            Vector3 arena = Vector3.zero;

            for (int ix = 0; ix < nx; ix++)
            {
                for (int iz = 0; iz < nz; iz++)
                {
                    float x = b.min.x + (ix + 0.5f) / nx * b.size.x + Range(-0.16f, 0.16f);
                    float z = b.min.z + (iz + 0.5f) / nz * b.size.z + Range(-0.16f, 0.16f);

                    Vector3 from = new Vector3(x, b.max.y + 2f, z);
                    if (!stand.Raycast(new Ray(from, Vector3.down), out RaycastHit hit, b.size.y + 4f))
                        continue;
                    // Skip the ground-level apron under the structure; benches are up on the tiers.
                    if (hit.point.y < b.min.y + 0.30f) continue;
                    // Only near-horizontal surfaces are seating; the sloped front is not.
                    if (Vector3.Dot(hit.normal, Vector3.up) < 0.72f) continue;

                    Vector3 pos = new Vector3(x, hit.point.y + 0.02f, z);
                    Vector3 toArena = arena - pos; toArena.y = 0f;
                    float yaw = toArena.sqrMagnitude > 0.01f
                        ? Mathf.Atan2(toArena.x, toArena.z) * Mathf.Rad2Deg
                        : 0f;

                    // Thin the grid only slightly, so the rows are not a perfect lattice.
                    //
                    // This was 0.28, which quietly removed better than a quarter of every stand.
                    // Combined with a scale that had already been raised once, the stands still
                    // read as almost empty — and a half-empty stand does not say "a few people
                    // came", it says "nobody is watching", which is the opposite of what a
                    // championship needs. A packed bench is also the single cheapest way to make
                    // the venue feel busy, since these are instanced and cost almost nothing.
                    if (Rand < 0.10f) continue;

                    seats.Add(new SpectatorCrowd.Seat
                    {
                        position = pos,
                        yaw = yaw + Range(-16f, 16f),
                        // Bigger again. At 1.25–1.62 a spectator was still shorter than the duck
                        // on the field, so a full stand read as a row of pebbles from the one
                        // distance the player ever sees it from.
                        scale = Range(1.45f, 1.90f),
                        species = _rng.Next(0, 8),
                        phase = Rand
                    });
                }
            }
            return seats;
        }

        /// <summary>Where the pond is, so nothing gets planted in the water.</summary>
        /// <summary>
        /// Due west of the player's lawn, and close enough to be a bearing rather than scenery.
        ///
        /// It used to sit at (-74, 58) — north-west, 93 m out, and behind the tree line from most
        /// of the field. That was fine when a minimap told the player which way they were facing.
        /// The minimap is gone, deliberately: it drew the target outline and the cut mask together
        /// and handed back everything the dissolving guide takes away. Orientation now has to come
        /// from the world, so each compass point carries one unmistakable silhouette and this is
        /// west. See BuildCardinalLandmarks.
        /// </summary>
        /// <remarks>
        /// Moved out from x = -70. The pond's meshes are Hill mounds, and Hill wobbles its rim by up
        /// to ±22% per direction, so a nominally 16.2 m water surface can reach 19.8 m — which put
        /// the pond's east shore about two metres from the west spectator stand's seating at
        /// x = -48.1. Six metres further west is still due west and still a bearing.
        /// </remarks>
        public static readonly Vector3 PondCentre = new Vector3(-76f, 0f, 0f);
        /// <summary>
        /// Keep-out radius, not the water's radius. 21 rather than 18.5 because of that same rim
        /// wobble: at 18.5 the outer fifth of the water was fair game for planting.
        /// </summary>
        public const float PondRadius = 21f;

        /// <summary>
        /// Everywhere scenery must not go: a contestant's lawn or apron, the scoreboard plaza, the
        /// pond, a hill, or any of the venue's own structures. The pond check is not optional — the
        /// westward hedgerow ran straight through the water and planted four oaks in it.
        /// </summary>
        static bool BlockedForScenery(Vector3 p, float margin = 9f)
        {
            Vector3 toPond = p - PondCentre; toPond.y = 0f;
            if (toPond.sqrMagnitude < PondRadius * PondRadius) return true;

            // Off the venue's own furniture: tents, stands, the barn, the mill, the boards, the
            // judging backdrop and the entrance avenue. See RegisterKeepOuts.
            foreach (var (centre, radius) in _keepOut)
            {
                // The extra five metres is the widest canopy in the tree set. A full-grown oak
                // measures 2.9 m of leaf either side of its trunk and the near ring scales it up to
                // 1.7, so a trunk placed five metres from a tent wall still has branches inside it.
                float keep = radius + 5f;
                float kx = p.x - centre.x, kz = p.z - centre.z;
                if (kx * kx + kz * kz < keep * keep) return true;
            }

            // Off the hills. They are 70 to 150 m across and the nearest of them reaches to within
            // about 105 m of the origin, so they overlap the planting range even though they read
            // as far-off skyline. A tree standing on a hazed hill is the thing that breaks the
            // distance those hills exist to create — and it also floats, because the hill is
            // rounded and the tree is placed at y = 0.
            foreach (var (centre, radius) in _hills)
            {
                float dx = p.x - centre.x, dz = p.z - centre.z;
                float keepOut = radius + 6f;
                if (dx * dx + dz * dz < keepOut * keepOut) return true;
            }

            return OnCompetitionGround(p, margin);
        }

        /// <summary>
        /// True on any contestant's lawn or the apron around it. Scenery keeps off all of it.
        /// </summary>
        static bool OnCompetitionGround(Vector3 p, float margin = 9f)
        {
            foreach (var spec in Venue.Plots)
            {
                if (Mathf.Abs(p.x - spec.centre.x) <= spec.Half + margin &&
                    Mathf.Abs(p.z - spec.centre.y) <= spec.Half + margin)
                    return true;
            }
            // And off the plaza, which is the one piece of ground the closing shot is composed on.
            Vector3 d = p - Venue.PlazaCentre; d.y = 0f;
            return d.sqrMagnitude < (Venue.PlazaRadius + 4f) * (Venue.PlazaRadius + 4f);
        }

        /// <summary>
        /// One unmistakable silhouette on each compass point, just outside the player's fence.
        ///
        /// This exists because the corner minimap was removed. That map drew the target outline,
        /// the cut mask, the spill and the mower's own heading in one frame — the complete answer
        /// key — and with the ground guide now dissolving a third of the way into the round it
        /// would have handed straight back everything the round is built on taking away.
        ///
        /// But the map was also carrying the compass, and a player who has lost the outline AND
        /// cannot tell which way they are pointing is not being asked to remember a picture, they
        /// are being asked to survive being lost. So orientation moves into the world:
        ///
        ///     west   the pond          (flat, bright, unmistakable from the air)
        ///     east   the barn          (the tallest solid mass on the ring)
        ///     north  the display board (vertical, lettered, faces the lawn)
        ///     south  the judging stand (already there, and the round's anchor)
        ///
        /// Everything sits between the apron at 46 m and the rival plots' fences at 68.8 m. Each
        /// is a different shape class on purpose — water, roof, board, awning — because at sixty
        /// metres and in silhouette, colour and detail are gone and only the outline survives.
        ///
        /// South is deliberately not built here: the player's judging station, its red awning and
        /// its crowd stand are put up by the arena pass and are already the strongest mass on the
        /// ring. Adding to it would only crowd the shot the judging beat has to hold.
        /// </summary>
        static void BuildCardinalLandmarks(Transform root)
        {
            var c = new GameObject("CardinalLandmarks").transform;
            c.SetParent(root, false);

            BuildNorthBoard(c);
        }

        /// <summary>
        /// The north bearing: a big lettered board on two legs, facing back down the lawn.
        ///
        /// Sized to be legible in silhouette from the far (south) edge of the field, which is 92 m
        /// away — hence a 13 m span rather than something that would look right stood next to.
        /// It is NOT the plaza scoreboard: that one lives on the shared corner at (48, 48), faces
        /// the quad, and carries live results. This is signage, and carrying results on it would
        /// put the standings in the corner of every chase shot.
        /// </summary>
        static void BuildNorthBoard(Transform parent)
        {
            var b = new GameObject("NorthBoard").transform;
            b.SetParent(parent, false);
            b.position = new Vector3(0f, 0f, NorthBoardZ);
            // Faces south, back down the player's lawn.
            b.rotation = Quaternion.Euler(0f, 180f, 0f);

            var woodDark = Mat("M_WoodDark");
            var woodWarm = Mat("M_WoodWarm");
            var cream = Mat("M_TentCream");
            var red = Mat("M_TentRed");

            var leg = Save(DuckPrimitives.ChamferBox(new Vector3(0.34f, 4.1f, 0.34f), 0.08f), "NorthBoardLeg");
            Spawn(b, "LegL", leg, woodDark, b.position + b.rotation * new Vector3(-5.6f, 4.1f, 0f), b.rotation, Vector3.one);
            Spawn(b, "LegR", leg, woodDark, b.position + b.rotation * new Vector3(5.6f, 4.1f, 0f), b.rotation, Vector3.one);

            var face = Save(DuckPrimitives.ChamferBox(new Vector3(6.5f, 2.3f, 0.28f), 0.16f), "NorthBoardFace");
            Spawn(b, "Face", face, woodDark, b.position + b.rotation * new Vector3(0f, 9.4f, 0f), b.rotation, Vector3.one);

            var inlay = Save(DuckPrimitives.ChamferBox(new Vector3(6.05f, 1.9f, 0.08f), 0.10f), "NorthBoardInlay");
            Spawn(b, "Inlay", inlay, cream, b.position + b.rotation * new Vector3(0f, 9.4f, 0.34f), b.rotation, Vector3.one, false);

            // A pitched crest, so the silhouette has a top edge that is not another rectangle. At
            // sixty metres this is most of what tells the board apart from the barn.
            // No 90-degree turn on the crest.
            //
            // Copied from the plaza scoreboard, where the turn swaps the prism's width and depth.
            // On a 6.8 m wide, 0.5 m deep prism that leaves the ridge running front-to-back —
            // a fin poking out of the top of the board instead of a gable spanning it.
            //
            // 13.4, not 6.8, and that number came from the same units trap as everything else here:
            // ChamferBox takes HALF-extents and Prism takes FULL sizes, so the face's 6.5 is 13 m of
            // board while the crest's 6.8 was 6.8 m of gable — a stub sitting in the middle of a
            // board twice its width, with the crest's own base plane hanging over open air on both
            // sides. 13.4 spans the face with a 20 cm eave.
            var crest = Save(DuckPrimitives.Prism(13.4f, 1.3f, 0.62f), "NorthBoardCrest");
            Spawn(b, "Crest", crest, red, b.position + b.rotation * new Vector3(0f, 11.7f, 0f),
                  b.rotation, Vector3.one);

            // Parented and turned 180 degrees about Y, which is the house convention for every
            // piece of world text in this project — the judges' desk cards and the plaza board
            // both do it, and DuckModelIntegration records why: at an identity rotation TMP reads
            // correctly only from BEHIND the surface it is sitting on, so a card left unturned
            // shows the audience a mirror image. This board faces south down the lawn, so its
            // readable side has to be its local +Z, and that needs the turn.
            //
            // The 180 is applied on top of the board's own rotation rather than instead of it, so
            // the text stays glued to the face however the board is aimed.
            var textGO = new GameObject("Title");
            textGO.transform.SetParent(b, false);
            textGO.transform.localPosition = new Vector3(0f, 9.4f, 0.44f);
            textGO.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var tm = textGO.AddComponent<TMPro.TextMeshPro>();
            // The show, not the discipline. This board is the north cardinal landmark the player
            // navigates by once the chalk guide dissolves, so it is read from the field constantly
            // — which makes it the single best place to state what the competition actually is.
            // "Lawn art" survives as the class name, on the flyer and the plaza board.
            tm.text = "GARDENER\nOF THE YEAR";
            // World units, not points: a TextMeshPro in 3D sizes at roughly eleven times the cap
            // height in metres, so a 3.8 m tall panel wants a font size near ten.
            tm.fontSize = 9.5f;
            tm.alignment = TMPro.TextAlignmentOptions.Center;
            tm.color = new Color(0.36f, 0.17f, 0.12f);
            tm.fontStyle = TMPro.FontStyles.Bold;
            tm.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            tm.rectTransform.sizeDelta = new Vector2(12f, 3.6f);
        }

        /// <summary>
        /// What stands behind the judges.
        ///
        /// The judging beat shoots from just north of the bench looking south, so everything the
        /// camera frames behind three animals is whatever happens to be at z &lt; -40. That was
        /// nothing at all: flat meadow to the fog line and a plain sky. The verdict — the payoff
        /// the whole round builds to — was being delivered against an empty backdrop, and it read
        /// as a placeholder set rather than as a county show.
        ///
        /// The fix is depth, not props. Flatness here is a DEPTH problem: with nothing between the
        /// bench and the horizon there is no scale reference and no overlap, so the eye reads the
        /// whole frame as one distance. Three bands, deliberately at three ranges:
        ///
        ///     near  (2-9 m)    marquee posts and bunting, framing the top and sides
        ///     mid   (14-40 m)  a supporters' marquee, hay stacks, flag poles, parked machines —
        ///                      this is the band that BREAKS THE HORIZON, and it is the one that
        ///                      was missing
        ///     far   (60 m+)    the tree ring and hills, already there
        ///
        /// Kept inside a cone behind the bench rather than scattered, because everything here
        /// exists for one camera and paying to dress ground it never sees is how a scene ends up
        /// too heavy to run.
        /// </summary>
        static void BuildJudgeBackdrop(Transform root)
        {
            var b = new GameObject("JudgeBackdrop").transform;
            b.SetParent(root, false);

            var comb = new Combiner();
            var wood = Mat("M_WoodDark");
            var woodWarm = Mat("M_WoodWarm");
            var cream = Mat("M_TentCream");
            var red = Mat("M_TentRed");
            var hay = Mat("M_ApronProp") ?? woodWarm;

            // ---- mid band: a supporters' marquee, off to one side so it does not sit dead centre
            {
                // 3.3, which is the top of the posts below, and not 4.4.
                //
                // The two primitives measure height differently and mixing them up left the
                // canopy hanging in mid-air with a 1.1 m gap over its own posts. ChamferBox takes
                // HALF-extents, so a post given 1.65 and placed at y = 1.65 stands from the ground
                // to 3.3. Prism builds from its BASE upward, so a canopy placed at y = 4.4 starts
                // there and rides to 6.6 — nothing of it is at 4.4 or below.
                //
                // It showed up as a 52 m wide, 33 m deep sheet of red floating between y 4.4 and
                // 6.65 with open sky underneath, which is what made the whole backdrop read as
                // misplaced. The JudgeStand awning above already gets this right (posts 1.4 to a
                // top of 2.8, canopy base at 2.8) and is the pattern to copy.
                // Moved out from x = -9.5, which straddled the entrance avenue: the canopy's east
                // edge reached x = -3.5, inside the hedge line at -3.9, and its two east posts came
                // within 22 cm of a hedge block in z — close enough that the hedges' own ±0.2 m
                // jitter and ±5 degrees of yaw could put a post inside one. At -13.2 the whole
                // marquee sits west of the avenue.
                var marqueeRot = Quaternion.Euler(0f, 8f, 0f);
                var canopy = Save(DuckPrimitives.Prism(11f, 2.2f, 7.5f), "BackdropMarquee");
                comb.Add(canopy, Matrix4x4.TRS(MarqueeCentre + Vector3.up * 3.3f, marqueeRot, Vector3.one), red);

                // The posts turn WITH the canopy.
                //
                // They were placed on a world-axis-aligned rectangle while the canopy was turned 8
                // degrees, so the corner posts ended up 5.62 m out in the canopy's own frame against
                // a roof that slopes to nothing at 5.5 — each one poking 12 cm past the edge of the
                // roof it is supposed to be holding up. Building the post ring in the marquee's
                // frame makes the geometry impossible to get wrong however the tent is aimed.
                var post = Save(DuckPrimitives.ChamferBox(new Vector3(0.13f, 1.65f, 0.13f), 0.04f), "BackdropPost");
                for (int i = -1; i <= 1; i += 2)
                    for (int k = -1; k <= 1; k += 2)
                        comb.Add(post, Matrix4x4.TRS(
                                 MarqueeCentre + marqueeRot * new Vector3(i * 5.2f, 1.65f, k * 3.4f),
                                 marqueeRot, Vector3.one), wood);
            }

            // ---- the pieces every cluster is built from ----
            //
            // Two sizes of bale on purpose. The big block is the procedural one this backdrop has
            // always used, and it is the only thing here that reads as MASS at 30 m; the small one is
            // the authored HayBale from Props.fbx, 0.94 x 0.54 x 0.60, which is a hand-carried square
            // bale. A real yard has both, and mixing them is most of the difference between "a stack
            // of identical boxes" and "somebody stacked these".
            var bigBale = Save(DuckPrimitives.ChamferBox(new Vector3(1.05f, 0.72f, 0.72f), 0.22f), "BackdropBale");
            var smallBale = Authored("Props.fbx", "HayBale", "HayBale");
            var smallBaleMat = smallBale != null ? AuthoredMat() : hay;
            var crate = Save(DuckPrimitives.ChamferBox(new Vector3(0.42f, 0.34f, 0.36f), 0.06f), "BackdropCrate");
            var plank = Save(DuckPrimitives.ChamferBox(new Vector3(0.16f, 1.55f, 0.035f), 0.03f), "BackdropPlank");

            // A stack of the big bales, built as a pyramid with the top course short.
            void Stack(Vector2 at, int high, float yaw)
            {
                var rot = Quaternion.Euler(0f, yaw, 0f);
                // Off the mesh, not typed. The course spacing IS the bale's height, and the old
                // 0.72/1.44 pair was the same number written twice in two different units.
                float baleH = bigBale.bounds.size.y;
                float baleW = bigBale.bounds.size.x;
                for (int y = 0; y < high; y++)
                {
                    int across = high - y;
                    for (int i = 0; i < across; i++)
                    {
                        // Each course is offset a little from the one under it, and each bale is
                        // turned a few degrees, because a hand-built stack is not a brick wall.
                        var baleRot = rot * Quaternion.Euler(0f, Range(-11f, 11f), 0f);
                        Vector3 local = new Vector3((i - (across - 1) * 0.5f) * (baleW + 0.1f) + Range(-0.16f, 0.16f),
                                                    0f, Range(-0.2f, 0.2f));
                        Vector3 world = new Vector3(at.x, y * baleH, at.y) + rot * local;
                        comb.Add(bigBale, Standing(bigBale, world, baleRot), hay);
                    }
                }
            }

            // A few small bales dropped on the ground nearby, some of them lying over.
            void LooseBales(Vector2 at, int count, float spread)
            {
                if (smallBale == null) return;
                for (int i = 0; i < count; i++)
                {
                    Vector3 p = new Vector3(at.x, 0f, at.y) + Companion(1.6f, spread);
                    // One in three has been tipped onto its side, which is the cheapest way to say
                    // somebody was working here rather than arranging things.
                    bool tipped = Rand < 0.34f;
                    var rot = tipped
                        ? Quaternion.Euler(90f, Range(0f, 360f), Range(-12f, 12f))
                        : Quaternion.Euler(Range(-4f, 4f), Range(0f, 360f), Range(-4f, 4f));
                    // Standing, not a typed lift. A tipped bale is turned 90 degrees, so what holds
                    // it off the ground is its 0.60 m DEPTH rather than its 0.54 m height — the
                    // hand-guessed 0.47 left every tipped bale hovering 17 cm up.
                    comb.Add(smallBale, Standing(smallBale, p, rot), smallBaleMat);
                }
            }

            // Crates, stacked two or three high and never squarely.
            void Crates(Vector2 at, int count)
            {
                float baseYaw = Range(0f, 360f);
                // The bottom crate stands on the ground; the ones above it stand on the crate below,
                // so only the first goes through Standing and the rest step up by a crate's height.
                float crateH = crate.bounds.size.y;
                for (int i = 0; i < count; i++)
                    comb.Add(crate, Standing(crate,
                             new Vector3(at.x + Range(-0.22f, 0.22f), i * crateH, at.y + Range(-0.22f, 0.22f)),
                             Quaternion.Euler(0f, baseYaw + Range(-24f, 24f), 0f)), woodWarm);
            }

            // A pole with cloth on it. Height, cloth kind and colour all vary; the WIND does not.
            void FlagPole(Vector2 at, float height, int kind, Material cloth)
            {
                var poleMesh = Save(DuckPrimitives.ChamferBox(new Vector3(0.07f, height * 0.5f, 0.07f), 0.02f),
                                    $"BackdropPole_{Mathf.RoundToInt(height * 10f)}");
                comb.Add(poleMesh, Matrix4x4.TRS(new Vector3(at.x, height * 0.5f, at.y),
                         Quaternion.Euler(Range(-1.5f, 1.5f), 0f, Range(-1.5f, 1.5f)), Vector3.one), wood);

                // Cloth hangs off the pole's downwind side, so which way it reaches is derived from
                // the wind rather than chosen per flag.
                Quaternion windRot = Quaternion.Euler(0f, WindYaw + Range(-7f, 7f), 0f);
                Vector3 downwind = windRot * Vector3.right;

                switch (kind)
                {
                    case 0:
                        // A long narrow banner, drooping: the roll is what makes it read as heavy
                        // cloth rather than as a board bolted to a post.
                        var banner = Save(DuckPrimitives.ChamferBox(new Vector3(0.55f, 1.35f, 0.02f), 0.02f),
                                          "BackdropBanner");
                        comb.Add(banner, Matrix4x4.TRS(
                                 new Vector3(at.x, height - 1.5f, at.y) + downwind * 0.6f,
                                 windRot * Quaternion.Euler(0f, 0f, Range(-16f, -6f)), Vector3.one), cloth);
                        break;
                    case 1:
                        // A triangular pennant streaming from the top.
                        var pennant = Save(DuckPrimitives.Prism(0.62f, 1.9f, 0.02f), "BackdropStreamer");
                        comb.Add(pennant, Matrix4x4.TRS(
                                 new Vector3(at.x, height - 0.25f, at.y) + downwind * 0.15f,
                                 windRot * Quaternion.Euler(0f, 0f, -78f), Vector3.one), cloth);
                        break;
                    default:
                        // A small square flag, well below the top so the poles do not all end at the
                        // same height in silhouette.
                        var square = Save(DuckPrimitives.ChamferBox(new Vector3(0.62f, 0.42f, 0.02f), 0.02f),
                                          "BackdropFlag");
                        comb.Add(square, Matrix4x4.TRS(
                                 new Vector3(at.x, height - Range(0.5f, 1.1f), at.y) + downwind * 0.66f,
                                 windRot * Quaternion.Euler(0f, 0f, Range(-9f, 3f)), Vector3.one), cloth);
                        break;
                }
            }

            // ---- cluster: the marquee's gear, tucked against its west and south sides ----
            //
            // Nothing here goes east of the marquee, because east of the marquee is the entrance
            // avenue. Bales beside the tent that needed bales; a barrow left where the last one was
            // carried from.
            {
                Vector2 tent = new Vector2(MarqueeCentre.x, MarqueeCentre.z);
                Stack(tent + new Vector2(-6.2f, -1.4f), 2, -14f);
                LooseBales(tent + new Vector2(-5.4f, 2.6f), 3, 2.6f);
                Crates(tent + new Vector2(-7.4f, 3.6f), 2);

                var barrow = Authored("Props.fbx", "Wheelbarrow", "Wheelbarrow");
                if (barrow != null)
                    comb.Add(barrow, Matrix4x4.TRS(
                             new Vector3(tent.x - 4.1f, 0f, tent.y + 4.4f),
                             Quaternion.Euler(0f, 128f, 0f), Vector3.one), AuthoredMat());

                // A plank leaning on the corner post at the angle a plank leans at.
                // A plank leaning at 24 degrees is 2.83 m tall rather than 3.10, and its bottom end is
                // nowhere near its own pivot — placed at a typed 0.76 it drove 0.66 m into the
                // ground, which is measurable in the emitted mesh (JudgeBackdrop's WoodWarm bounds
                // started at y = -0.714). Standing() takes the rotation into account.
                var leanA = Quaternion.Euler(0f, 22f, 24f);
                comb.Add(plank, Standing(plank, new Vector3(tent.x - 5.9f, 0f, tent.y - 3.9f), leanA), woodWarm);
            }

            // ---- cluster: the hay pile ----
            {
                Vector2 at = BackdropClusters[1].at;
                Stack(at, 3, 9f);
                Stack(at + new Vector2(3.9f, -2.2f), 2, -21f);
                LooseBales(at + new Vector2(-2.6f, 2.4f), 4, 3.2f);
                Crates(at + new Vector2(4.6f, 2.2f), 3);
            }

            // ---- cluster: the second pile, further out and smaller, so the two do not rhyme ----
            {
                Vector2 at = BackdropClusters[2].at;
                Stack(at, 2, 31f);
                LooseBales(at + new Vector2(2.9f, 1.6f), 3, 2.8f);
                Crates(at + new Vector2(-3.2f, -1.1f), 2);
                var leanB = Quaternion.Euler(0f, -38f, 31f);
                comb.Add(plank, Standing(plank, new Vector3(at.x + 1.2f, 0f, at.y - 2.4f), leanB), woodWarm);
            }

            // ---- cluster: the far tent, and the only place flags are grouped ----
            //
            // Flags mark something. Three of them clustered beside a tent 40 m out reads as that
            // tent's pitch; seven in a line across the whole horizon read as signage, which is what
            // the player was objecting to. Heights are 4.1, 5.6 and 6.5 so their tops do not agree,
            // and the three carry three different kinds of cloth.
            {
                Vector2 at = BackdropClusters[3].at;
                var tentB = Authored("Landmarks.fbx", "Tent_B", "Tent_B");
                if (tentB != null)
                {
                    comb.Add(tentB, Matrix4x4.TRS(new Vector3(at.x, 0f, at.y),
                             Quaternion.Euler(0f, -24f, 0f), Vector3.one * 1.25f), AuthoredMat());
                }
                else
                {
                    // Same shape from primitives, so the far silhouette survives a missing FBX.
                    comb.Add(DuckPrimitives.ChamferBox(new Vector3(2.7f, 1.1f, 2.7f), 0.08f),
                             Matrix4x4.TRS(new Vector3(at.x, 1.1f, at.y), Quaternion.Euler(0f, -24f, 0f), Vector3.one), cream);
                    comb.Add(DuckPrimitives.Prism(6.2f, 2.3f, 6.0f),
                             Matrix4x4.TRS(new Vector3(at.x, 2.2f, at.y), Quaternion.Euler(0f, -24f, 0f), Vector3.one), red);
                }

                FlagPole(at + new Vector2(-3.4f, 1.9f), 6.5f, 1, red);
                FlagPole(at + new Vector2(-2.1f, 3.4f), 4.1f, 2, cream);
                FlagPole(at + new Vector2(-4.6f, 3.9f), 5.6f, 0, red);
                Crates(at + new Vector2(3.1f, 1.4f), 2);
                LooseBales(at + new Vector2(3.4f, -1.8f), 2, 2.2f);
            }

            // ---- two lone poles, one either side, at two different depths ----
            //
            // These are the horizon-breakers the old row of seven was trying to be. Two is enough:
            // what cuts a horizon is a vertical at a DIFFERENT distance from everything else in
            // frame, not more verticals at the same distance.
            FlagPole(BackdropClusters[4].at, 7.2f, 0, cream);
            FlagPole(BackdropClusters[5].at, 5.9f, 1, red);

            // ---- near band: bunting strung across the top of frame, plus its two poles.
            {
                var tall = Save(DuckPrimitives.ChamferBox(new Vector3(0.09f, 3.1f, 0.09f), 0.03f), "BackdropBuntingPost");
                comb.Add(tall, Matrix4x4.TRS(new Vector3(-7.4f, 3.1f, BenchZ - 4.2f), Quaternion.identity, Vector3.one), woodWarm);
                comb.Add(tall, Matrix4x4.TRS(new Vector3(7.4f, 3.1f, BenchZ - 4.2f), Quaternion.identity, Vector3.one), woodWarm);

                // The line, drawn. These seventeen pennants used to hang across a 14.8 m gap with
                // nothing between them and the two posts at its ends — which is what the player was
                // looking at when they said the cream pieces are floating with no post. They were
                // right: at the middle of the run the nearest wood was seven metres away.
                //
                // Both the cord and the pennants now come off ONE curve, and it starts at the post
                // tops (6.2, from the post's own half-extent) rather than at a remembered 6.05.
                float buntingTop = 3.1f * 2f;
                Vector3 Line(float t) => new Vector3(Mathf.Lerp(-7.4f, 7.4f, t),
                                                     buntingTop - Mathf.Sin(t * Mathf.PI) * 0.85f,
                                                     BenchZ - 4.2f);

                var cord = DuckPrimitives.Cylinder(0.02f, 0.02f, 1f, 3, 0.004f);
                const int cordSegments = 8;
                for (int i = 0; i < cordSegments; i++)
                    Cord(comb, cord, Line(i / (float)cordSegments), Line((i + 1) / (float)cordSegments), wood);

                var pennant = Save(DuckPrimitives.Prism(0.34f, 0.42f, 0.02f), "BackdropPennant");
                const int flags = 17;
                for (int i = 0; i < flags; i++)
                {
                    // Pegged along the same curve the cord follows, so every one of them touches it.
                    float t = i / (float)(flags - 1);
                    comb.Add(pennant, Matrix4x4.TRS(Line(t),
                             Quaternion.Euler(180f, 0f, Range(-7f, 7f)), Vector3.one),
                             (i % 3 == 0) ? red : ((i % 3 == 1) ? cream : woodWarm));
                }
            }

            // Shadows back ON, and the reason they were ever off is worth recording.
            //
            // They were disabled to fix a judging close-up in which the bench, the cards and the
            // lower half of every judge sank into a flat murky green. The diagnosis was that this
            // backdrop's marquee was casting over the bench. It was wrong. The bench was never in
            // a cast shadow — the ground immediately behind it was fully lit in the same frame,
            // which should have settled it — and the real cause was two lighting bugs: the volume
            // profile had no post-processing in it at all, and the ambient probe was a near-pure
            // green that multiplied a warm brown bench's red and blue channels away.
            //
            // With those fixed, switching the casters off is a workaround for a problem that no
            // longer exists, and it was costing the venue every ground shadow it had. A set with
            // no shadows reads as flat and unplaced no matter how well lit it is.
            comb.Emit(b, "JudgeBackdrop", true);
        }

        static void BuildLandmarks(Transform root)
        {
            var l = new GameObject("Landmarks").transform;
            l.SetParent(root, false);
            var comb = new Combiner();

            var propMat = AuthoredMat();

            // The barn: the one silhouette on the skyline that says "this is a farm", and the
            // player's east bearing.
            //
            // Moved from (-86, 74). It was north-west, which put it on the same side as the pond
            // and the windmill — three landmarks in one quadrant and nothing at all on the other
            // three, which is no use for orientation. Turned a few degrees off square so it reads
            // as a building rather than as a marker.
            //
            // Neither the position nor the yaw is arbitrary: both are pinned by the 20.7 m of ground
            // between the east stand and HORACE's fence. See BarnCentre for the measurements.
            Vector3 barn = BarnCentre;
            var barnRot = Quaternion.Euler(0f, BarnYaw, 0f);
            var barnMesh = Authored("Landmarks.fbx", "Barn", "Barn");
            if (barnMesh != null)
            {
                comb.Add(barnMesh, Matrix4x4.TRS(barn, barnRot, Vector3.one), propMat);
            }
            else
            {
                comb.Add(DuckPrimitives.ChamferBox(new Vector3(9f, 3.4f, 6f), 0.18f),
                         Matrix4x4.TRS(barn + Vector3.up * 3.4f, barnRot, Vector3.one), Mat("M_TentRed"));
                // Width and depth swapped for the 90-degree turn, which is the trap this file keeps
                // walking into. Prism's ridge runs along its own Z, so the turn is what lays the
                // ridge along the barn's 18 m length — but it also swaps which of the prism's two
                // horizontal sizes ends up where. As written it was Prism(18.6, 4.4, 12.4), which
                // after the turn put an 18.6 m span across a body only 12 m deep and left the roof
                // 5.6 m SHORT of both gable ends. The body is 18 x 12, so the roof is 13.2 across
                // (12 plus an eave) by 18.6 along.
                comb.Add(DuckPrimitives.Prism(13.2f, 4.4f, 18.6f),
                         Matrix4x4.TRS(barn + Vector3.up * 6.8f, barnRot * Quaternion.Euler(0f, 90f, 0f), Vector3.one), Mat("M_WoodDark"));
            }
            comb.Emit(l, "Barn");

            // The windmill turns, so it gets its own object with a live component.
            var mill = new GameObject("Windmill").transform;
            mill.SetParent(root, false);
            // West of everything. At (92,-34) it was standing on top of Horace's judging station:
            // that spot was open ground when there was one plot, and is now inside the venue.
            mill.position = WindmillCentre;
            mill.rotation = Quaternion.Euler(0f, 62f, 0f);

            var millTower = Authored("Landmarks.fbx", "Windmill", "WindmillTower", "Windmill_Blades");
            var millSails = Authored("Landmarks.fbx", "Windmill_Blades", "WindmillSails");

            Transform hub;
            if (millTower != null && millSails != null)
            {
                Spawn(mill, "Tower", millTower, AuthoredMat(), mill.position, mill.rotation, Vector3.one);

                hub = new GameObject("Windmill_Blades").transform;
                hub.SetParent(mill, false);

                // Where the modeller actually put the sails on the tower, read out of the FBX.
                // The previous version took the height from the combined sail mesh's own bounds
                // and no horizontal offset at all — but GetCombined recentres each object on its
                // own pivot, so those bounds are centred on zero and the height collapsed to a
                // hardcoded guess with the sails buried in the middle of the tower.
                if (!DuckAssetLibrary.TryGetLocalOffset("Landmarks.fbx", "Windmill", "Windmill_Blades",
                                                        out Vector3 hubOffset) ||
                    hubOffset.sqrMagnitude < 0.01f)
                    hubOffset = new Vector3(0f, 10.6f, 2.6f);

                hub.localPosition = hubOffset;
                hub.localRotation = Quaternion.identity;

                // The sails sit on the hub's own origin: the mesh is already centred on its pivot,
                // and the pivot is now in the right place.
                var sailGO = Spawn(hub, "Sails", millSails, AuthoredMat(),
                                   hub.position, hub.rotation, Vector3.one);
                sailGO.transform.localPosition = Vector3.zero;
                // Stand the wheel up. The combined mesh arrives lying in the ground plane, which
                // spins about the vertical and reads as a helicopter rotor rather than a windmill.
                sailGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            else
            {
                var tower = Save(DuckPrimitives.Cylinder(3.4f, 2.1f, 11f, 12, 0.15f), "MillTower");
                Spawn(mill, "Tower", tower, Mat("M_FenceWhite"), mill.position + Vector3.up * 5.5f, mill.rotation, Vector3.one);
                var capMesh = Save(DuckPrimitives.Hill(2.5f, 2.4f, 3, 12, 5), "MillCap");
                Spawn(mill, "Cap", capMesh, Mat("M_WoodDark"), mill.position + Vector3.up * 11f, mill.rotation, Vector3.one);

                hub = new GameObject("Windmill_Blades").transform;
                hub.SetParent(mill, false);
                hub.position = mill.position + mill.rotation * new Vector3(0f, 10.6f, 2.6f);
                hub.rotation = mill.rotation;
                var sailMesh = Save(DuckPrimitives.ChamferBox(new Vector3(0.34f, 5.4f, 0.12f), 0.06f), "MillSail");
                for (int i = 0; i < 4; i++)
                    Spawn(hub, $"Sail_{i}", sailMesh, Mat("M_WoodWarm"),
                          hub.position + hub.rotation * (Quaternion.Euler(0f, 0f, i * 90f) * new Vector3(0f, 5.4f, 0f)),
                          hub.rotation * Quaternion.Euler(0f, 0f, i * 90f), Vector3.one);
            }

            var spin = mill.gameObject.AddComponent<Windmill>();
            spin.blades = hub;
            spin.degreesPerSecond = 22f;
        }

        // ------------------------------------------------------------------ field props

        static void BuildFieldProps(Transform root)
        {
            var p = new GameObject("Props").transform;
            p.SetParent(root, false);

            // Corner marker stakes: the only thing standing on the lawn itself, so the player
            // always knows where the judged area ends.
            var stakeComb = new Combiner();
            var authoredStake = Authored("Props.fbx", "MarkerStake", "MarkerStake");
            var stake = authoredStake ?? DuckPrimitives.ChamferBox(new Vector3(0.05f, 0.55f, 0.05f), 0.018f);
            var stakeMat = authoredStake != null ? AuthoredMat() : Mat("M_FenceWhite");
            var flagMesh = DuckPrimitives.Prism(0.34f, 0.26f, 0.012f);
            for (int i = 0; i < 4; i++)
            {
                float sx = (i % 2 == 0) ? -1f : 1f;
                float sz = (i < 2) ? -1f : 1f;
                var pos = new Vector3(sx * (Field.Half - 0.5f), 0f, sz * (Field.Half - 0.5f));
                // No authored-versus-procedural lift to keep in step: Standing measures whichever
                // mesh it was handed, through the lean it was given.
                var stakeRot = Quaternion.Euler(Range(-4f, 4f), Range(0f, 360f), Range(-4f, 4f));
                stakeComb.Add(stake, Standing(stake, pos, stakeRot), stakeMat);
                if (authoredStake == null)
                    // The pennant hangs from just under the stake's top rather than from a
                    // remembered height, so it follows the stake if the stake ever changes.
                    stakeComb.Add(flagMesh, Matrix4x4.TRS(
                                  pos + Vector3.up * (GroundY(pos.x, pos.z) + stake.bounds.size.y - 0.1f),
                                  Quaternion.Euler(180f, Range(0f, 360f), 0f), Vector3.one), Mat("M_TentRed"));
            }
            // The stakes stand on the lawn itself, so they get colliders too — they mark the
            // judged area and clipping through the corner marker is worse than being stopped by it.
            stakeComb.Emit(p, "MarkerStakes", true, addCollider: true);

            // Hay bales, wheelbarrows and clutter on the apron — lived-in, not scattered.
            var clutter = new Combiner();
            var authoredBale = Authored("Props.fbx", "HayBale", "HayBale");
            var bale = authoredBale ?? DuckPrimitives.Cylinder(0.62f, 0.62f, 1.1f, 10, 0.08f);
            var baleMat = authoredBale != null ? AuthoredMat() : Mat("M_Dirt");
            var baleSpots = new[]
            {
                new Vector3(-38f, 0f, 24f), new Vector3(-38.4f, 0f, 25.6f), new Vector3(-37.2f, 0f, 26.3f),
                new Vector3(36f, 0f, -22f), new Vector3(37.4f, 0f, -22.8f),
                new Vector3(-20f, 0f, 41f), new Vector3(-18.6f, 0f, 41.6f)
            };
            foreach (var spot in baleSpots)
            {
                // One branch, not two. This was the clearest case of the pivot bug in the file: the
                // authored bale was placed with NO lift and the procedural one with a typed 0.55,
                // only one of those was ever measured, and 0.55 was wrong anyway because a 1.1 m
                // cylinder turned on its side is held up by its 0.62 m RADIUS. Every procedural bale
                // was 7 cm underground. Standing measures the mesh it is actually given.
                var baleRot = authoredBale != null
                    ? Quaternion.Euler(0f, Range(0f, 360f), 0f)
                    : Quaternion.Euler(90f, Range(0f, 360f), 0f);
                clutter.Add(bale, Standing(bale, spot, baleRot), baleMat);
            }

            // A trophy on a plinth by the judges, a wheelbarrow and a bicycle left where someone
            // finished with them. Environmental storytelling, not scatter.
            //
            // pos.y is a height ABOVE whatever this prop stands on, not a world y: 0 means "on the
            // ground" and anything else means "on top of the thing named in the comment". Standing
            // adds the ground height and the mesh's own bottom, so neither the terrain nor the
            // exporter's choice of pivot can put these in the air or under it.
            void PlaceProp(string objName, string save, Vector3 pos, float yaw)
            {
                var mesh = Authored("Props.fbx", objName, save);
                if (mesh != null)
                    clutter.Add(mesh, Standing(mesh, pos, Quaternion.Euler(0f, yaw, 0f)), AuthoredMat());
            }

            var plinthMesh = Authored("Props.fbx", "TrophyPlinth", "TrophyPlinth");
            PlaceProp("TrophyPlinth", "TrophyPlinth", new Vector3(-6.2f, 0f, -37.5f), 14f);
            // On the plinth, at the plinth's real height. It was at a typed 1.02 against a plinth
            // that is 0.76 m tall, so the trophy floated a quarter of a metre over its own base.
            PlaceProp("Trophy", "Trophy",
                      new Vector3(-6.2f, plinthMesh != null ? plinthMesh.bounds.size.y : 0.76f, -37.5f), 26f);
            PlaceProp("Wheelbarrow", "Wheelbarrow", new Vector3(23f, 0f, 38.5f), -62f);
            PlaceProp("Bicycle", "Bicycle", new Vector3(-30f, 0f, -35f), 104f);
            PlaceProp("Thermos", "Thermos", new Vector3(1.1f, 0.83f, -38.9f), 20f);
            // The sprinkler stands OUTSIDE the rail, and it has to.
            //
            // At (-34, 12) it stood on the apron, well inside the fence, and it is 0.26 m tall — so it
            // reached 2 cm into the band the mower's chassis sweeps (y 0.24..0.76) and the machine went
            // straight over it. That is not a collider fault: the sprinkler is in the Clutter batch and
            // has an exact mesh collider. It is simply too short for this mower to touch, and no
            // configuration of the builder can make a 26 cm prop hittable — see MowerContact.
            //
            // So the rule is the other way round: a prop that cannot be solid must not stand where the
            // mower can drive. This spot is 1.3 m past the mower's reach and still inside the radius
            // that keeps trees out, tucked between two fence posts under the bottom rail — visible
            // dressing from the lawn and from the reveal, and impossible to drive at. DuckSolidity's
            // audit fails the build if anyone puts a prop like this back inside the rail.
            PlaceProp("Sprinkler", "Sprinkler", new Vector3(-(MowerContact.ReachRadius + 1.3f), 0f, 24.9f), 0f);
            // No ScoreboardProp here. It was the second of two boards in the same spot — see the
            // note at the end of BuildAwningAndScoreboard for the measurement and for why removing
            // both is safe.
            // Solid. Hay bales, a wheelbarrow and a trophy plinth that the mower drives straight
            // through are scenery; ones it can hit are part of the game — bonks feed the style
            // score, and a bale you have to steer around is a reason to look where you are going.
            clutter.Emit(p, "Clutter", true, addCollider: true);

            // Garden gnomes: knockable, comedic, and the only real obstacle on the lawn.
            //
            // ---- WHY SOME OBSTACLES ARE IGNORED AND OTHERS ARE NOT ----
            //
            // The arithmetic that used to be written out here now lives in MowerContact, as code, and
            // is checked against every prop in the venue by DuckSolidity. Read those two if you are
            // chasing an obstacle that does not collide; this comment is only the summary.
            //
            // The mower has exactly ONE collider, a box 0.52 m tall held about 0.44 m off the ground by
            // its suspension, so:
            //
            //     THE MOWER CAN ONLY TOUCH THINGS BETWEEN y 0.24 AND y 0.76.
            //     Nothing below 0.24 m can be hit, whatever collider it has.
            //
            // Which makes it a per-PROP fact, and the measured heights sort the venue's dressing into
            // hittable and not (measured by the solidity audit, not by hand):
            //
            //     Sprinkler      0.26 m  ->  0.02 m of overlap   never hittable, so it lives outside the rail
            //     Thermos        0.27 m, on the bench at 0.83    entirely above the band; nothing to drive through
            //     Wheelbarrow    0.52 m  ->  0.22 m              hittable
            //     HayBale        0.54 m  ->  0.29 m              hittable
            //     gnome, OLD     0.60 m  ->  0.36 m              hittable, thin — glancing hits slipped
            //     TrophyPlinth   0.76 m  ->  0.52 m              full contact
            //     MarkerStake    0.86 m  ->  0.52 m              full contact
            //     Bicycle        1.10 m  ->  0.52 m              full contact
            //
            // A thin window is what "sometimes it does not collide" is: a glancing contact needs the
            // solver to resolve an overlap of a few centimetres between two moving boxes in one
            // timestep, and near the edge of the window it does not always find one. The gnome now
            // spans 0 to 1.2, which covers the mower's ENTIRE collider height, so any contact at all
            // registers. Below about 1.0 m overall the thin-window problem comes back, so 1.2 is a
            // floor with margin and not a look-and-see number.
            //
            // Two things this is NOT, both checked rather than assumed:
            //
            //   NOT the terrain. Ground height would produce exactly this symptom — a gnome pinned to
            //   y = 0.22 on a +0.5 m rise tops out at 0.60 while the mower's box sits at 0.74..1.26,
            //   so the obstacle is silently ignored AND buried so it never looked hittable. But there
            //   is no rise: the gnomes stand on GrassField's LawnGround box collider, whose top face
            //   is dead flat at y = 0, and every clutter prop is inside VenueFlatRadius where the
            //   surround's undulation is multiplied by zero. The mechanism is real and would bite the
            //   moment anything moved outward, which is why everything here is now ground-relative
            //   (see Standing and GroundY) — it just is not the current cause.
            //
            //   NOT the layers. Prop is layer 10 and exists, so NameToLayer does not return -1, and
            //   ProjectSettings/DynamicsManager has Mower(8) x Prop(10) enabled in both directions.
            //   The only layer disabled against anything is Grass, deliberately.
            var gnomeRoot = new GameObject("Gnomes").transform;
            gnomeRoot.SetParent(p, false);
            var gnomePositions = new[]
            {
                new Vector3(-17f, 0f, 9f), new Vector3(14f, 0f, -13f), new Vector3(21f, 0f, 18f),
                new Vector3(-9f, 0f, -21f), new Vector3(4f, 0f, 25f), new Vector3(-25f, 0f, -6f)
            };
            var authoredGnome = Authored("Props.fbx", "Gnome", "Gnome");
            var gnomeBody = authoredGnome ?? Save(DuckPrimitives.Cylinder(0.20f, 0.06f, 0.44f, 10, 0.03f), "GnomeBody");
            var gnomeHat = authoredGnome != null ? null : Save(DuckPrimitives.Cylinder(0.14f, 0.005f, 0.26f, 8, 0.01f), "GnomeHat");
            var gnomeMat = authoredGnome != null ? AuthoredMat() : Mat("M_TentCream");

            // ---- one size number, and everything else measured off the mesh ----
            //
            // The gnome used to be 0.485 m tall standing in grass that stands about 0.5 m, which is
            // most of why it was reported as not colliding: you cannot avoid, aim at, or even see an
            // obstacle that is shorter than the lawn it is hiding in, and from the chase camera it was
            // a bump in the grass. Doubling it puts the whole body and its hat clear of the blade
            // line — 1.2 m of ornament against 0.5 m of grass — and it puts far more of it inside the
            // 0.24-to-0.76 m band the mower's chassis can actually touch.
            //
            // GnomeTargetHeight is the only hand-chosen number here. The scale factor comes from the
            // mesh's real height, and the capsule's radius, height and centre all come from the mesh
            // bounds AFTER that scale. Previously those were four independent typed values — a mesh
            // scale, a capsule radius, a capsule height, a capsule centre and a transform lift — that
            // all had to agree about one shape, and the first person to change any of them would have
            // desynchronised the rest silently.
            const float GnomeTargetHeight = 1.2f;
            Bounds gb = gnomeBody.bounds;
            float gnomeScale = gb.size.y > 0.01f ? GnomeTargetHeight / gb.size.y : 1f;
            float gnomeTall = gb.size.y * gnomeScale;
            float gnomeWide = Mathf.Max(gb.size.x, gb.size.z) * gnomeScale;

            foreach (var gp in gnomePositions)
            {
                var go = new GameObject("Gnome");
                go.transform.SetParent(gnomeRoot, false);
                // The gnome's origin IS the ground now. Nothing is lifted, so there is no lift to get
                // wrong: the collider's centre sits at half its own height and the visual is placed
                // from the mesh's own bottom, so both stand on this point by construction.
                //
                // Seated against the ground rather than against y = 0. On the lawn those are the same
                // number and the raycast falls through to 0 by design (GrassField builds the lawn
                // collider at runtime, and that lawn is a flat quad at y = 0) — but the gnome
                // positions are only ever going to drift outward, and past the lawn the surround is
                // real geometry with real height. See GroundY.
                Vector3 seated = OnGround(gp);
                go.transform.position = seated;
                go.transform.rotation = Quaternion.Euler(0f, Range(0f, 360f), 0f);
                go.layer = LayerMask.NameToLayer("Prop");
                go.tag = "Gnome";

                var visual = new GameObject("Visual").transform;
                visual.SetParent(go.transform, false);
                var gnomeScaleV = Vector3.one * gnomeScale;
                // The authored gnome stands on its own origin and the procedural cylinder is centred
                // on its middle; subtracting the mesh's own scaled bottom handles either without
                // asking which one this is.
                Vector3 bodyAt = seated - Vector3.up * BottomOf(gnomeBody, go.transform.rotation, gnomeScaleV);
                Spawn(visual, "Body", gnomeBody, gnomeMat, bodyAt, go.transform.rotation, gnomeScaleV);
                if (gnomeHat != null)
                {
                    // On top of the body, not at a remembered height: the hat's own centre has to
                    // clear the body's top by half the hat.
                    float bodyTop = bodyAt.y + gnomeBody.bounds.max.y * gnomeScale;
                    float hatHalf = gnomeHat.bounds.extents.y * gnomeScale;
                    Spawn(visual, "Hat", gnomeHat, Mat("M_TentRed"),
                          new Vector3(seated.x, bodyTop + hatHalf - 0.02f * gnomeScale, seated.z),
                          go.transform.rotation, gnomeScaleV);
                }

                var col = go.AddComponent<CapsuleCollider>();
                col.height = gnomeTall;
                // A capsule cannot be shorter than twice its radius without collapsing into a
                // sphere, and a sphere would let the mower ride up over the gnome instead of hitting
                // it, so the radius is clamped to keep it a capsule.
                col.radius = Mathf.Min(gnomeWide * 0.5f, gnomeTall * 0.45f);
                col.center = new Vector3(0f, gnomeTall * 0.5f, 0f);

                var rb = go.AddComponent<Rigidbody>();
                // Mass follows the size: this is now a 1.2 m ornament rather than a 0.5 m one, and at
                // 6 kg it would have shoved the mower around. It does not change the launch, which
                // Gnome.cs sets as a VELOCITY, but it does decide how the gnome behaves once it is
                // loose among the other bodies. See the report for the launch values that want
                // revisiting in Gnome.cs, which is not this file's to edit.
                rb.mass = 22f;
                rb.isKinematic = false;
                go.AddComponent<Gnome>();
            }
        }

        static void BuildCrowd(Transform root)
        {
            var c = new GameObject("Crowd");
            c.transform.SetParent(root, false);
            var crowd = c.AddComponent<SpectatorCrowd>();

            // Seats are found on the seating, not typed in.
            //
            // Every previous version wrote out a grid of positions from remembered tier heights and
            // spacings. That grid was authored against the procedural stands; the arena has used
            // the authored model for a long time now, and the two do not agree about where a bench
            // is — so spectators sat in the air, beside the stand, and out on open grass. Nudging
            // the numbers only ever moved the problem.
            //
            // Instead: sweep a fine grid of candidate points across the stands' own bounds, drop a
            // ray on each one, and keep it only if it lands on the stand collider. A spectator can
            // then only exist where there is genuinely something to sit on, and the crowd follows
            // the model automatically if the stands are ever remodelled.
            var spots = new List<SpectatorCrowd.Seat>();
            Physics.SyncTransforms();

            foreach (var stand in root.GetComponentsInChildren<MeshCollider>(true))
            {
                if (!stand.name.StartsWith("Stands")) continue;
                spots.AddRange(SeatsOn(stand));
            }

            if (spots.Count == 0)
                Debug.LogWarning("[Duck] No stand colliders found — the crowd has nowhere to sit.");

            crowd.seats = spots.ToArray();

            // Authored spectators if we have them; the runtime falls back to generated blobs.
            var species = new List<Mesh>();
            foreach (var name in new[] { "Rabbit_Root", "Sheep_Root", "Pig_Root", "Fox_Root", "Tortoise_Root" })
            {
                var m = Authored("Spectators.fbx", name, name.Replace("_Root", ""));
                if (m != null) species.Add(m);
            }
            // Three more species for the championship crowd. Five repeated across four plots'
            // worth of stands reads as wallpaper; eight is enough that no two neighbours match.
            foreach (var name in new[] { "Goose_Root", "Hedgehog_Root", "Squirrel_Root" })
            {
                var m = Authored("CrowdExtra.fbx", name, name.Replace("_Root", ""));
                if (m != null) species.Add(m);
            }
            if (species.Count > 0)
            {
                crowd.speciesMeshes = species.ToArray();
                crowd.crowdMaterial = Mat("M_Spectators");
                for (int i = 0; i < spots.Count; i++)
                {
                    var seat = spots[i];
                    seat.species = seat.species % species.Count;
                    spots[i] = seat;
                }
                crowd.seats = spots.ToArray();
            }
            else
            {
                crowd.crowdMaterial = Mat("M_Crowd");
            }
            // A crowd that barely moves is scenery. These are instanced and animated on the CPU
            // from one loop, so motion is close to free — and motion is what the eye reads as
            // "attended" from sixty metres, long before it can make out an individual animal.
            crowd.idleBobAmplitude = 0.055f;   // was 0.026 — a twitch, invisible at any real distance
            crowd.idleBobSpeed = 1.8f;
            crowd.cheerBobAmplitude = 0.30f;   // was 0.16 — a cheer should be a stand erupting
            crowd.cheerBobSpeed = 8.5f;
            crowd.trackingDegrees = 34f;       // heads follow the mower further round

            var mowerGO = GameObject.Find("Mower");
            if (mowerGO != null) crowd.trackTarget = mowerGO.transform;
            crowd.EnsurePlaceholderMeshes();
        }
    }
}
