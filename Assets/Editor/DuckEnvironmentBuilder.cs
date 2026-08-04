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

        static System.Random _rng;
        static float Rand => (float)_rng.NextDouble();
        static float Range(float a, float b) => a + (b - a) * Rand;

        public static void Build()
        {
            _rng = new System.Random(20260804);
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
            readonly Dictionary<Material, List<CombineInstance>> _buckets = new();

            public void Add(Mesh mesh, Matrix4x4 trs, Material mat)
            {
                if (mesh == null || mat == null) return;
                if (!_buckets.TryGetValue(mat, out var list))
                {
                    list = new List<CombineInstance>();
                    _buckets[mat] = list;
                }
                list.Add(new CombineInstance { mesh = mesh, transform = trs, subMeshIndex = 0 });
            }

            public void Emit(Transform parent, string name, bool castShadow = true, bool addCollider = false)
            {
                int index = 0;
                foreach (var kv in _buckets)
                {
                    var combined = new Mesh { indexFormat = IndexFormat.UInt32 };
                    combined.CombineMeshes(kv.Value.ToArray(), true, true);
                    combined.RecalculateNormals();
                    combined.RecalculateBounds();
                    var saved = Save(combined, $"{name}_{index}");

                    var go = Spawn(parent, $"{name}_{kv.Key.name}", saved, kv.Key,
                                   Vector3.zero, Quaternion.identity, Vector3.one, castShadow);
                    go.isStatic = true;
                    if (addCollider)
                    {
                        var mc = go.AddComponent<MeshCollider>();
                        mc.sharedMesh = saved;
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

            // A worn dirt lane leading in from the south gate — the way everyone arrives.
            var lane = Save(DuckPrimitives.ChamferBox(new Vector3(2.6f, 0.02f, 26f), 0.4f), "Lane");
            Spawn(g, "Lane", lane, Mat("M_Dirt"), new Vector3(0f, 0.012f, -66f), Quaternion.identity, Vector3.one, false);
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
            for (int side = 0; side < 4; side++)
            {
                var go = new GameObject($"Bound_{side}");
                go.transform.SetParent(f, false);
                var box = go.AddComponent<BoxCollider>();
                float r = FenceRadius - 0.4f;
                if (side < 2)
                {
                    go.transform.position = new Vector3(0f, 1.2f, side == 0 ? r : -r);
                    box.size = new Vector3(r * 2f + 2f, 2.4f, 0.6f);
                }
                else
                {
                    go.transform.position = new Vector3(side == 2 ? r : -r, 1.2f, 0f);
                    box.size = new Vector3(0.6f, 2.4f, r * 2f + 2f);
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

            var comb = new Combiner();
            const float spacing = 2.2f;
            int perSide = Mathf.RoundToInt(FenceRadius * 2f / spacing);

            for (int side = 0; side < 4; side++)
            {
                for (int i = 0; i < perSide; i++)
                {
                    float t0 = -FenceRadius + i * spacing;
                    if (side == 1 && Mathf.Abs(t0) < 4f) continue;

                    // Flags hang along a catenary between posts, so the line sags.
                    for (int k = 1; k <= 4; k++)
                    {
                        float u = k / 5f;
                        float sag = Mathf.Sin(u * Mathf.PI) * 0.28f;
                        float t = t0 + u * spacing;
                        Vector3 p = side switch
                        {
                            0 => new Vector3(t, 0f, FenceRadius),
                            1 => new Vector3(t, 0f, -FenceRadius),
                            2 => new Vector3(FenceRadius, 0f, t),
                            _ => new Vector3(-FenceRadius, 0f, t),
                        };
                        p.y = 1.28f - sag;
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
                    float baseXa = sxa * (FenceRadius + 4.6f);
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

                    comb.Add(plank, Matrix4x4.TRS(new Vector3(x, y, Range(-0.4f, 0.4f)), Quaternion.identity, Vector3.one), wood);
                    comb.Add(riser, Matrix4x4.TRS(new Vector3(x + sx * 0.42f, y * 0.5f, 0f), Quaternion.identity, Vector3.one), woodDark);
                }

                // End frames so the stand has a structure rather than floating planks.
                for (int e = -1; e <= 1; e += 2)
                {
                    var frame = DuckPrimitives.ChamferBox(new Vector3(2.4f, 0.09f, 0.14f), 0.04f);
                    comb.Add(frame, Matrix4x4.TRS(new Vector3(baseX + sx * 1.4f, 1.35f, e * 15f),
                                                  Quaternion.Euler(0f, 0f, sx * 26f), Vector3.one), woodDark);
                    var leg = DuckPrimitives.ChamferBox(new Vector3(0.11f, 1.3f, 0.11f), 0.035f);
                    comb.Add(leg, Matrix4x4.TRS(new Vector3(baseX + sx * 2.8f, 1.3f, e * 15f), Quaternion.identity, Vector3.one), woodDark);
                    comb.Add(leg, Matrix4x4.TRS(new Vector3(baseX, 1.3f, e * 15f), Quaternion.identity, Vector3.one), woodDark);
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
                    comb.Add(post, Matrix4x4.TRS(new Vector3(i * 3.9f, 1.4f, z + k * 1.5f), Quaternion.identity, Vector3.one), wood);

            // Prism builds its ridge along Z, so the 90-degree turn is what puts the ridge along
            // the bench — but the width and depth have to be swapped to match, or the turn leaves a
            // 3.6 m awning pointing 9 m out into the field. The scallops below are separate pieces
            // in the same combiner, which is why turning the finished object in the editor took
            // the bunting with it: the fix belongs here, not on the transform.
            var canopy = DuckPrimitives.Prism(3.6f, 1.05f, 9.0f);
            comb.Add(canopy, Matrix4x4.TRS(new Vector3(0f, 2.8f, z), Quaternion.Euler(0f, 90f, 0f), Vector3.one), red);

            // Scallop of alternating stripes along the awning edge.
            var scallop = DuckPrimitives.Prism(0.55f, 0.34f, 0.03f);
            for (int i = 0; i < 16; i++)
            {
                float x = -4.2f + i * 0.56f;
                comb.Add(scallop, Matrix4x4.TRS(new Vector3(x, 2.62f, z + 1.82f), Quaternion.Euler(180f, 0f, 0f), Vector3.one),
                         (i % 2 == 0) ? cream : red);
            }

            // No shadow from the awning. It is a three-metre lid directly over the only three
            // characters the game ever pushes in on, and it dropped the whole bench — faces,
            // scorecards, desk — into a flat murky green that read as broken lighting rather than
            // as shade. The stand still reads as a stand; it just stops eating the performance.
            comb.Emit(a, "JudgeStand", castShadow: false);

            // Scoreboard on the north edge, angled toward the crowd.
            var board = new GameObject("Scoreboard").transform;
            board.SetParent(root, false);
            var bComb = new Combiner();
            var panel = DuckPrimitives.ChamferBox(new Vector3(3.1f, 1.9f, 0.16f), 0.08f);
            var legs = DuckPrimitives.ChamferBox(new Vector3(0.12f, 1.5f, 0.12f), 0.04f);
            bComb.Add(panel, Matrix4x4.TRS(new Vector3(0f, 3.4f, FenceRadius + 2.2f), Quaternion.Euler(-8f, 180f, 0f), Vector3.one), Mat("M_WoodWarm"));
            bComb.Add(legs, Matrix4x4.TRS(new Vector3(-2.4f, 1.5f, FenceRadius + 2.5f), Quaternion.identity, Vector3.one), Mat("M_WoodDark"));
            bComb.Add(legs, Matrix4x4.TRS(new Vector3(2.4f, 1.5f, FenceRadius + 2.5f), Quaternion.identity, Vector3.one), Mat("M_WoodDark"));
            bComb.Emit(board, "Scoreboard");
        }

        static void BuildTents(Transform root)
        {
            var t = new GameObject("Tents").transform;
            t.SetParent(root, false);
            var comb = new Combiner();

            var positions = new[]
            {
                new Vector3(-52f, 0f, 44f), new Vector3(-40f, 0f, 53f),
                new Vector3( 49f, 0f, -38f), new Vector3(58f, 0f, 12f)
            };
            var yaws = new[] { 22f, -14f, 40f, -62f };
            var sizes = new[] { 5.2f, 4.2f, 6.0f, 4.6f };

            var tentA = Authored("Landmarks.fbx", "Tent_A", "Tent_A");
            var tentB = Authored("Landmarks.fbx", "Tent_B", "Tent_B");

            for (int i = 0; i < positions.Length; i++)
            {
                float w = sizes[i];

                if (tentA != null || tentB != null)
                {
                    var authoredTent = (i % 2 == 0 ? tentA : tentB) ?? tentA ?? tentB;
                    comb.Add(authoredTent,
                             Matrix4x4.TRS(positions[i], Quaternion.Euler(0f, yaws[i], 0f),
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

            // Basin, then a water surface just below the rim so there is a real bank.
            var basin = Save(DuckPrimitives.Hill(17f, 2.2f, 4, 22, 77), "PondBasin");
            var basinGO = Spawn(p, "Basin", basin, Mat("M_Dirt"),
                                centre + Vector3.down * 2.4f, Quaternion.identity, new Vector3(1f, -1f, 1f), false);
            basinGO.GetComponent<MeshRenderer>().sharedMaterial = Mat("M_Dirt");

            var water = Save(DuckPrimitives.Hill(16.2f, 0.05f, 2, 22, 78), "PondWater");
            Spawn(p, "Water", water, Mat("M_Water"), centre + Vector3.down * 0.55f, Quaternion.identity, Vector3.one, false);

            // Reed clumps around the near bank only — that is the side the player ever sees.
            var reed = DuckPrimitives.ChamferBox(new Vector3(0.05f, 0.65f, 0.05f), 0.02f);
            var comb = new Combiner();
            for (int i = 0; i < 90; i++)
            {
                float a = Range(2.2f, 5.6f);
                float r = Range(14.5f, 17.5f);
                Vector3 pos = centre + new Vector3(Mathf.Cos(a) * r, Range(0.1f, 0.5f), Mathf.Sin(a) * r);
                comb.Add(reed, Matrix4x4.TRS(pos, Quaternion.Euler(Range(-14f, 14f), Range(0f, 360f), Range(-14f, 14f)),
                                             new Vector3(1f, Range(0.7f, 1.5f), 1f)), Mat("M_Hedge"));
            }
            comb.Emit(p, "Reeds", false);
        }

        static void BuildFoliage(Transform root)
        {
            var f = new GameObject("Foliage").transform;
            f.SetParent(root, false);
            var comb = new Combiner();

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
                // Never on somebody's competition lawn or its apron. The scatter was written when
                // there was one plot at the origin; the orchard to the north and the random ring
                // both now fall across rival ground, and a lawn-art plot with an oak growing out
                // of it is not a lawn-art plot.
                if (BlockedForScenery(pos)) return;

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
            const float TreeLimit = 190f;

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
            Grove(quadCentre + new Vector3(-168f, 0f, -40f), 34f, 26, 200, 0.85f, 1.45f);
            Grove(quadCentre + new Vector3(-120f, 0f, 118f), 30f, 20, 240, 0.80f, 1.35f);
            Grove(quadCentre + new Vector3(126f, 0f, -128f), 32f, 22, 270, 0.80f, 1.40f);
            Grove(quadCentre + new Vector3(150f, 0f, 92f), 28f, 18, 300, 0.75f, 1.30f);

            // An orchard: the one place regular spacing is right, because someone planted it.
            for (int gz = 0; gz < 4; gz++)
                for (int gx = 0; gx < 6; gx++)
                    Tree(quadCentre + new Vector3(-158f + gx * 12f + Range(-1.5f, 1.5f), 0f,
                                                  46f + gz * 12f + Range(-1.5f, 1.5f)),
                         Range(0.72f, 0.95f), 340 + gz * 6 + gx);

            // Parkland: singles and pairs on the open ground between the plots and the woods, thin
            // enough that the venue still reads as the subject.
            for (int i = 0; i < 20; i++)
            {
                float a = Range(0f, Mathf.PI * 2f);
                float r = Range(92f, 150f);
                Vector3 p = quadCentre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                Tree(p, Range(0.9f, 1.45f), 400 + i);
                // Half of them get a companion, because parkland trees come in twos and threes.
                if (Rand < 0.5f)
                    Tree(p + new Vector3(Range(-7f, 7f), 0f, Range(-7f, 7f)), Range(0.7f, 1.1f), 440 + i);
            }

            // Specimen trees close in, on the ground the tour camera crosses between plots.
            var specimens = new[]
            {
                new Vector3(48f, 0f, -34f), new Vector3(-34f, 0f, 46f), new Vector3(140f, 0f, 44f),
                new Vector3(46f, 0f, 142f), new Vector3(-30f, 0f, -46f), new Vector3(142f, 0f, -30f),
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
                    comb.Add(block, Matrix4x4.TRS(new Vector3(sx + Range(-0.2f, 0.2f), hedgeLift, z),
                                                  Quaternion.Euler(0f, Range(-5f, 5f), 0f),
                                                  new Vector3(Range(0.92f, 1.08f), Range(0.9f, 1.1f), 1f)), hedgeMat);
            }

            comb.Emit(f, "Foliage");
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

                    // Thin the grid a little so the rows are not a perfect lattice of animals.
                    if (Rand < 0.28f) continue;

                    seats.Add(new SpectatorCrowd.Seat
                    {
                        position = pos,
                        yaw = yaw + Range(-16f, 16f),
                        scale = Range(0.85f, 1.18f),
                        species = _rng.Next(0, 8),
                        phase = Rand
                    });
                }
            }
            return seats;
        }

        /// <summary>Where the pond is, so nothing gets planted in the water.</summary>
        public static readonly Vector3 PondCentre = new Vector3(-74f, 0f, 58f);
        public const float PondRadius = 18.5f;

        /// <summary>
        /// Everywhere scenery must not go: a contestant's lawn or apron, the scoreboard plaza, or
        /// the pond. The pond check is not optional — the westward hedgerow ran straight through
        /// the water and planted four oaks in it.
        /// </summary>
        static bool BlockedForScenery(Vector3 p, float margin = 9f)
        {
            Vector3 toPond = p - PondCentre; toPond.y = 0f;
            if (toPond.sqrMagnitude < PondRadius * PondRadius) return true;

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

        static void BuildLandmarks(Transform root)
        {
            var l = new GameObject("Landmarks").transform;
            l.SetParent(root, false);
            var comb = new Combiner();

            var propMat = AuthoredMat();

            // The barn: the one silhouette on the skyline that says "this is a farm".
            Vector3 barn = new Vector3(-86f, 0f, 74f);
            var barnRot = Quaternion.Euler(0f, 28f, 0f);
            var barnMesh = Authored("Landmarks.fbx", "Barn", "Barn");
            if (barnMesh != null)
            {
                comb.Add(barnMesh, Matrix4x4.TRS(barn, barnRot, Vector3.one), propMat);
            }
            else
            {
                comb.Add(DuckPrimitives.ChamferBox(new Vector3(9f, 3.4f, 6f), 0.18f),
                         Matrix4x4.TRS(barn + Vector3.up * 3.4f, barnRot, Vector3.one), Mat("M_TentRed"));
                comb.Add(DuckPrimitives.Prism(18.6f, 4.4f, 12.4f),
                         Matrix4x4.TRS(barn + Vector3.up * 6.8f, barnRot * Quaternion.Euler(0f, 90f, 0f), Vector3.one), Mat("M_WoodDark"));
            }
            comb.Emit(l, "Barn");

            // The windmill turns, so it gets its own object with a live component.
            var mill = new GameObject("Windmill").transform;
            mill.SetParent(root, false);
            // West of everything. At (92,-34) it was standing on top of Horace's judging station:
            // that spot was open ground when there was one plot, and is now inside the venue.
            mill.position = new Vector3(-102f, 0f, -38f);
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
            float stakeLift = authoredStake != null ? 0f : 0.55f;
            var flagMesh = DuckPrimitives.Prism(0.34f, 0.26f, 0.012f);
            for (int i = 0; i < 4; i++)
            {
                float sx = (i % 2 == 0) ? -1f : 1f;
                float sz = (i < 2) ? -1f : 1f;
                var pos = new Vector3(sx * (Field.Half - 0.5f), 0f, sz * (Field.Half - 0.5f));
                stakeComb.Add(stake, Matrix4x4.TRS(pos + Vector3.up * stakeLift,
                              Quaternion.Euler(Range(-4f, 4f), Range(0f, 360f), Range(-4f, 4f)), Vector3.one), stakeMat);
                if (authoredStake == null)
                    stakeComb.Add(flagMesh, Matrix4x4.TRS(pos + Vector3.up * 1.0f, Quaternion.Euler(180f, Range(0f, 360f), 0f), Vector3.one), Mat("M_TentRed"));
            }
            stakeComb.Emit(p, "MarkerStakes", true);

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
                var m = authoredBale != null
                    ? Matrix4x4.TRS(spot, Quaternion.Euler(0f, Range(0f, 360f), 0f), Vector3.one)
                    : Matrix4x4.TRS(spot + Vector3.up * 0.55f, Quaternion.Euler(90f, Range(0f, 360f), 0f), Vector3.one);
                clutter.Add(bale, m, baleMat);
            }

            // A trophy on a plinth by the judges, a wheelbarrow and a bicycle left where someone
            // finished with them. Environmental storytelling, not scatter.
            void PlaceProp(string objName, string save, Vector3 pos, float yaw)
            {
                var mesh = Authored("Props.fbx", objName, save);
                if (mesh != null)
                    clutter.Add(mesh, Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), Vector3.one), AuthoredMat());
            }

            PlaceProp("TrophyPlinth", "TrophyPlinth", new Vector3(-6.2f, 0f, -37.5f), 14f);
            PlaceProp("Trophy", "Trophy", new Vector3(-6.2f, 1.02f, -37.5f), 26f);
            PlaceProp("Wheelbarrow", "Wheelbarrow", new Vector3(23f, 0f, 38.5f), -62f);
            PlaceProp("Bicycle", "Bicycle", new Vector3(-30f, 0f, -35f), 104f);
            PlaceProp("Thermos", "Thermos", new Vector3(1.1f, 0.83f, -38.9f), 20f);
            PlaceProp("Sprinkler", "Sprinkler", new Vector3(-34f, 0f, 12f), 0f);
            PlaceProp("Scoreboard", "ScoreboardProp", new Vector3(0f, 0f, FenceRadius + 2.4f), 180f);
            clutter.Emit(p, "Clutter");

            // Garden gnomes: knockable, comedic, and the only real obstacle on the lawn.
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
            foreach (var gp in gnomePositions)
            {
                var go = new GameObject("Gnome");
                go.transform.SetParent(gnomeRoot, false);
                go.transform.position = gp + Vector3.up * 0.22f;
                go.transform.rotation = Quaternion.Euler(0f, Range(0f, 360f), 0f);
                go.layer = LayerMask.NameToLayer("Prop");
                go.tag = "Gnome";

                var visual = new GameObject("Visual").transform;
                visual.SetParent(go.transform, false);
                Spawn(visual, "Body", gnomeBody, gnomeMat,
                      go.transform.position + (authoredGnome != null ? Vector3.down * 0.22f : Vector3.zero),
                      go.transform.rotation, Vector3.one);
                if (gnomeHat != null)
                    Spawn(visual, "Hat", gnomeHat, Mat("M_TentRed"), go.transform.position + Vector3.up * 0.32f, go.transform.rotation, Vector3.one);

                var col = go.AddComponent<CapsuleCollider>();
                col.height = 0.6f; col.radius = 0.2f; col.center = new Vector3(0f, 0.08f, 0f);
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 6f;
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
            var mowerGO = GameObject.Find("Mower");
            if (mowerGO != null) crowd.trackTarget = mowerGO.transform;
            crowd.EnsurePlaceholderMeshes();
        }
    }
}
