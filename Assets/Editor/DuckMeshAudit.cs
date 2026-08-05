using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Audits every imported mesh for the geometry faults that show up in engine as holes,
    /// black patches or z-fighting — inconsistent triangle winding above all.
    ///
    /// A triangle wound the wrong way is invisible under back-face culling, so the model reads as
    /// transparent in exactly one direction. That is very hard to spot in a Blender viewport and
    /// very obvious in the game, so it needs measuring rather than eyeballing.
    ///
    /// Per mesh it reports:
    ///   boundary edges   — edges with a single triangle. Non-zero means the surface is open.
    ///   non-manifold     — edges shared by more than two triangles.
    ///   bad winding      — shared edges traversed in the SAME direction by both triangles,
    ///                      which is the definition of two neighbours wound inconsistently.
    ///   signed volume    — negative means the whole mesh is inside-out.
    /// </summary>
    public static class DuckMeshAudit
    {
        [MenuItem("Duck/Diagnose · Audit mesh winding (all models)", priority = 48)]
        public static void AuditAll()
        {
            var sb = new StringBuilder("[Duck] MESH WINDING AUDIT\n");
            int totalBad = 0, totalOpen = 0, totalInverted = 0, meshCount = 0;

            foreach (string path in ModelFiles())
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;

                sb.AppendLine($"  --- {System.IO.Path.GetFileName(path)} ---");
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);

                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh;
                    if (mesh == null) continue;
                    meshCount++;

                    var report = Audit(mesh);
                    bool bad = report.badWinding > 0 || report.nonManifold > 0 || report.signedVolume < 0f;
                    totalBad += report.badWinding;
                    totalOpen += report.boundary;
                    if (report.signedVolume < 0f) totalInverted++;

                    if (bad || report.boundary > 0)
                    {
                        sb.AppendLine($"    {(bad ? "FAIL" : "open")} {mf.name}: tris={report.tris} " +
                                      $"boundary={report.boundary} nonManifold={report.nonManifold} " +
                                      $"badWinding={report.badWinding} volume={report.signedVolume:0.####}");
                    }
                }

                Object.DestroyImmediate(inst);
            }

            sb.AppendLine($"  TOTALS over {meshCount} meshes: badWinding={totalBad} " +
                          $"boundaryEdges={totalOpen} invertedMeshes={totalInverted}");
            if (totalBad == 0 && totalInverted == 0)
                sb.AppendLine("  No inconsistently wound or inside-out meshes. Back-face culling is safe to re-enable.");
            else
                sb.AppendLine("  Inconsistent winding present — keep character materials double-sided until fixed at source.");

            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Audit every mesh actually placed in the open scene, not just the imported models.
        ///
        /// The venue is generated geometry — hedges, stations, stands, the plaza, the scoreboard,
        /// the rival mowers — none of which passes through an FBX importer, so the model audit
        /// cannot see any of it. A lofted or mirrored primitive is exactly where inverted winding
        /// creeps back in, and with back-face culling on an inverted mesh simply vanishes from one
        /// side, which is the hardest kind of art bug to spot in a screenshot.
        ///
        /// Flat pieces (lawn quads, path slabs, banners) are open by construction: they report
        /// boundary edges and no volume, and that is correct, so they are listed separately rather
        /// than counted as failures.
        /// </summary>
        [MenuItem("Duck/Diagnose · Audit mesh winding (open scene)", priority = 49)]
        public static void AuditScene()
        {
            var sb = new StringBuilder("[Duck] SCENE MESH WINDING AUDIT\n");
            int meshCount = 0, totalBad = 0, totalInverted = 0, totalNonManifold = 0, flatCount = 0;
            var seen = new System.Collections.Generic.HashSet<Mesh>();

            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || !seen.Add(mesh)) continue;
                meshCount++;

                var report = Audit(mesh);

                // Only a closed shell has an inside, so only a closed shell can be inside-out.
                // Aprons, ground rings and cards are open by construction: their signed volume is
                // an artefact of where the origin happens to sit, not a verdict on their winding.
                bool open = report.boundary > 0 || Mathf.Abs(report.signedVolume) < 1e-4f;
                if (open) { flatCount++; continue; }

                bool bad = report.badWinding > 0 || report.nonManifold > 0 || report.signedVolume < 0f;
                totalBad += report.badWinding;
                totalNonManifold += report.nonManifold;
                if (report.signedVolume < 0f) totalInverted++;

                if (bad)
                {
                    sb.AppendLine($"    FAIL {mesh.name} (on {mf.name}): tris={report.tris} " +
                                  $"boundary={report.boundary} nonManifold={report.nonManifold} " +
                                  $"badWinding={report.badWinding} volume={report.signedVolume:0.####}");
                }
            }

            sb.AppendLine($"  TOTALS over {meshCount} scene meshes ({flatCount} open shells, not solids): " +
                          $"badWinding={totalBad} nonManifold={totalNonManifold} invertedMeshes={totalInverted}");
            if (totalBad == 0 && totalInverted == 0 && totalNonManifold == 0)
                sb.AppendLine("  Every solid in the scene is closed, manifold and consistently wound.");
            else
                sb.AppendLine("  Inverted or inconsistent solids present — fix at the generator, not with double-sided materials.");

            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Check that every flat, open surface in the scene actually faces the sky.
        ///
        /// The solid audit deliberately skips open shells, because signed volume says nothing
        /// about a mesh with no inside — but that let the biggest surfaces in the game go
        /// unchecked. The ground, the aprons and the paths are all open rings and planes, they are
        /// all drawn with back-face culling, and a triangle wound the wrong way on one of them is
        /// simply a hole you can see the sky through. That is far worse on the ground than on a
        /// prop, and it is invisible in a wireframe.
        ///
        /// Winding is checked geometrically — the cross product of the triangle's own edges — not
        /// from the stored normals, because the ring builder asserts its normals upward regardless
        /// of how the triangle is wound. The normals being right is exactly what hides this.
        /// </summary>
        [MenuItem("Duck/Diagnose · Audit ground facing (open scene)", priority = 51)]
        public static void AuditGroundFacing()
        {
            var sb = new StringBuilder("[Duck] GROUND FACING AUDIT\n");
            int checkedMeshes = 0, badMeshes = 0, totalDown = 0;
            var seen = new HashSet<Mesh>();

            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || !seen.Add(mesh)) continue;

                var verts = mesh.vertices;
                var tris = mesh.triangles;
                if (tris.Length == 0) continue;

                // Only judge surfaces that are broadly horizontal — a fence panel is vertical by
                // design and has nothing to say here.
                int up = 0, down = 0, steep = 0;
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    if (n.sqrMagnitude < 1e-12f) continue;
                    n.Normalize();
                    // Unity is left-handed: a clockwise-from-the-front triangle faces the viewer,
                    // which makes this cross product point along the face normal.
                    if (n.y > 0.5f) up++;
                    else if (n.y < -0.5f) down++;
                    else steep++;
                }

                // Solids are not the subject here. A closed box is supposed to have a floor, and
                // reporting every crate and post as "face-down" buries the one finding that
                // matters: an open surface that should be ground and is pointing at the sky's
                // opposite. The solid audit already covers closed meshes properly.
                var r = Audit(mesh);
                if (r.boundary == 0) continue;

                int flat = up + down;
                if (flat < tris.Length / 3 * 0.6f) continue;   // not a ground-like surface
                checkedMeshes++;

                if (down > 0)
                {
                    badMeshes++;
                    totalDown += down;
                    sb.AppendLine($"    FACE-DOWN {mesh.name} (on {mf.name}): up={up} down={down} steep={steep}");
                }
            }

            sb.AppendLine($"  {checkedMeshes} horizontal surfaces checked, {badMeshes} with downward faces, " +
                          $"{totalDown} triangles facing the ground.");
            if (badMeshes == 0)
                sb.AppendLine("  Every horizontal surface faces the sky. Nothing is culled from above.");
            else
                sb.AppendLine("  Downward-facing ground triangles are holes under back-face culling — fix the generator.");

            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Anything standing inside a hill.
        ///
        /// The hills are 70 to 150 m across and the nearest of them reaches to within about 105 m
        /// of the origin, so despite reading as far-off skyline they overlap ground the venue and
        /// its scenery actually use. Anything placed there is either buried in the slope or
        /// floating off it, because everything else is positioned at y = 0 and the hill is a dome.
        /// </summary>
        [MenuItem("Duck/Diagnose · Audit hill overlaps (open scene)", priority = 52)]
        public static void AuditHillOverlaps()
        {
            var sb = new StringBuilder("[Duck] HILL OVERLAP AUDIT\n");

            var hills = new List<(string name, Vector3 pos, float radius)>();
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!mf.name.StartsWith("Hill_")) continue;
                var b = mf.GetComponent<MeshRenderer>() != null ? mf.GetComponent<MeshRenderer>().bounds : default;
                hills.Add((mf.name, mf.transform.position, Mathf.Max(b.extents.x, b.extents.z)));
            }
            if (hills.Count == 0) { Debug.Log(sb.Append("  No hills found.").ToString()); return; }

            int hits = 0;
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string n = mr.name;
                if (n.StartsWith("Hill_") || n == "Surround" || n.StartsWith("Foliage")) continue;

                Vector3 p = mr.bounds.center;
                foreach (var h in hills)
                {
                    float dx = p.x - h.pos.x, dz = p.z - h.pos.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    // A hill is a dome that falls to nothing at its rim, and it is sunk a few
                    // metres besides — so an object near the outer edge of the bounding radius is
                    // standing on flat ground, not on a slope. Only the raised inner part counts,
                    // or the audit reports every fence post within sight of the skyline.
                    if (d >= h.radius * 0.7f) continue;
                    hits++;
                    sb.AppendLine($"    {n} is {d:0} m into {h.name} (radius {h.radius:0}) at " +
                                  $"({p.x:0}, {p.z:0})");
                    break;
                }
            }

            sb.AppendLine(hits == 0
                ? "  Nothing is standing inside a hill."
                : $"  {hits} objects overlap a hill.");
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Every pair of props that visibly grow through each other, plus anything hanging in the
        /// air or buried under the ground.
        ///
        /// Written because three separate playthroughs reported intersecting dressing and each one
        /// was then chased by reading the placement code and doing the arithmetic by hand. That
        /// works — the marquee floating 1.1 m over its own posts was found that way — but it only
        /// ever finds the collision you happened to look for, and the venue has a few hundred props
        /// in it. This measures all of them.
        ///
        /// Two things it has to get right or it lies:
        ///
        /// TRANSFORMS ARE MEANINGLESS. Almost every venue prop is emitted by the environment
        /// builder's Combiner: one combined mesh per material, left at the origin, with world-space
        /// vertices baked in. So transform.position is (0,0,0) for the fence, the bunting, the
        /// hedges and the hay, and only renderer/mesh BOUNDS say where anything is.
        ///
        /// GAMEOBJECT BOUNDS ARE TOO COARSE. The same fact means one GameObject's bounds span every
        /// instance of that material at once — the fence's bounds are the whole 80 m ring — so
        /// comparing two combined meshes to each other answers nothing. Every mesh is therefore
        /// split back into its individual props first, by flood-filling triangles across shared
        /// vertices: the Combiner never welds its instances together, so one connected component is
        /// one placed prop. A 1 mm weld tolerance is deliberate — props that INTERPENETRATE do not
        /// share vertices, so they stay separate pieces and remain comparable, while a flat-shaded
        /// box's duplicated corners collapse as they should.
        ///
        /// Bounds overlap is not the same as visible intersection, so the report is filtered:
        ///
        ///   - the smallest of the three axis overlaps must exceed <see cref="OverlapTolerance"/>.
        ///     Legitimate joinery TOUCHES: a post's top is exactly the canopy's base, a rail meets
        ///     a post, a scallop hangs off an eave. Those measure at or near zero and drop out.
        ///   - the overlap must also swallow at least 15% of the smaller piece's bounds. This is
        ///     what separates a leg mounted 0.6 m into a signboard from a hay bale standing inside
        ///     a hedge.
        ///   - pieces of the SAME renderer are never compared. Every combined mesh is one authored
        ///     assembly whose parts are meant to touch — a fence's posts and rails, a stack's own
        ///     bales, a tree's trunk and canopy — and reporting those buries everything else.
        ///     Pairs within one assembly but from different renderers are still reported, tagged
        ///     [same assembly], because that is where the hay-versus-marquee kind of fault lives.
        ///   - ground surfaces (the surround, lawns, aprons, paths, chalk, the pond) are skipped
        ///     entirely: everything on the map legitimately overlaps the ground.
        ///   - characters and moving parts are skipped by name. A seated judge intersects the bench
        ///     it is sitting on, and the windmill's wheel is mounted through its cap.
        ///
        /// The floating check does not use a whitelist of things allowed to be off the ground.
        /// Instead a piece is only reported if NOTHING is underneath it — no other piece within
        /// 1.5 m horizontally reaching up to within 0.3 m of its base. That is what makes the
        /// original marquee bug a finding rather than a judgement call: posts topping out at 3.3
        /// under a canopy starting at 4.4 is a 1.1 m gap with nothing in it. Bunting is the one
        /// genuine exception — it hangs in mid-span by design — so it is named.
        /// </summary>
        [MenuItem("Duck/Diagnose · Audit prop overlaps (open scene)", priority = 54)]
        public static void AuditPropOverlaps()
        {
            var sb = new StringBuilder("[Duck] PROP OVERLAP AUDIT\n");

            var pieces = CollectPieces(sb);
            if (pieces.Count == 0)
            {
                Debug.Log(sb.Append("  No props found — open Main and build the world first.").ToString());
                return;
            }

            // Sorted by minimum x so the pair sweep can stop early instead of testing n².
            pieces.Sort((a, b) => a.bounds.min.x.CompareTo(b.bounds.min.x));

            int hits = 0, sameAssembly = 0;
            var lines = new List<string>();
            var supported = new bool[pieces.Count];

            for (int i = 0; i < pieces.Count; i++)
            {
                var a = pieces[i];
                for (int j = i + 1; j < pieces.Count; j++)
                {
                    var b = pieces[j];
                    // Sorted on min.x: once a candidate starts beyond our right edge, so does
                    // everything after it. The 1.5 m slack is the support test's reach — a flag hangs
                    // off the side of its pole and a scallop off the edge of its eave, so a supporter
                    // does not have to overlap in x to be what a thing is resting on.
                    if (b.bounds.min.x > a.bounds.max.x + 1.5f) break;

                    if (Supports(a.bounds, b.bounds)) supported[j] = true;
                    if (Supports(b.bounds, a.bounds)) supported[i] = true;

                    if (a.renderer == b.renderer) continue;
                    if (Excused(a.owner, b.owner)) continue;

                    Vector3 pen = Penetration(a.bounds, b.bounds);
                    if (pen.x <= 0f || pen.y <= 0f || pen.z <= 0f) continue;

                    float depth = Mathf.Min(pen.x, Mathf.Min(pen.y, pen.z));
                    if (depth < OverlapTolerance) continue;

                    float shared = pen.x * pen.y * pen.z;
                    float smaller = Mathf.Min(Volume(a.bounds), Volume(b.bounds));
                    if (smaller < 1e-5f || shared < smaller * 0.15f) continue;

                    bool same = a.assembly == b.assembly;
                    if (same) sameAssembly++;
                    hits++;
                    Vector3 c = a.bounds.center;
                    lines.Add($"    {(same ? "[same assembly] " : "")}{a.owner} x {b.owner}: " +
                              $"{depth:0.00} m deep ({pen.x:0.00} x {pen.y:0.00} x {pen.z:0.00}), " +
                              $"{Mathf.Min(1f, shared / smaller) * 100f:0}% of the smaller piece, " +
                              $"near ({c.x:0.0}, {c.z:0.0})");
                }
            }

            // ---- off the ground, either way ----
            //
            // Reported FIRST, ahead of the intersections, because it is the fault this venue actually
            // had: almost every prop was positioned at a hand-typed y, and where an authored FBX later
            // replaced a procedural primitive nobody re-measured the pivot, so the two branches of the
            // same placement disagreed about where the object's bottom was. Floating and sinking are
            // also far cheaper to judge from a log than an intersection is — a number either is or is
            // not zero.
            sb.AppendLine("  --- off the ground ---");
            int buried = 0, floating = 0;
            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                // Only where the ground is known to be dead level. Past the venue's flat radius the
                // surround rolls by several metres, so "y = 0" is not the ground out there and the
                // hills and the far planting would all report as floating or buried.
                Vector3 c = p.bounds.center;
                if (Mathf.Max(Mathf.Abs(c.x), Mathf.Abs(c.z)) > 150f) continue;

                if (p.bounds.max.y < -0.05f)
                {
                    buried++;
                    sb.AppendLine($"    BURIED {p.owner}: top at y={p.bounds.max.y:0.00} " +
                                  $"near ({c.x:0.0}, {c.z:0.0})");
                }
                else if (p.bounds.min.y > 0.25f && !supported[i] && !Aloft(p.owner))
                {
                    floating++;
                    sb.AppendLine($"    FLOATING {p.owner}: base at y={p.bounds.min.y:0.00} with " +
                                  $"nothing under it, near ({c.x:0.0}, {c.z:0.0})");
                }
            }
            if (buried == 0 && floating == 0)
                sb.AppendLine("    Everything stands on the ground or on something else.");

            // ---- intersections ----
            // Longest penetration first: that is the order they need fixing in.
            lines.Sort((x, y) => y.CompareTo(x));
            if (lines.Count > 120)
            {
                sb.AppendLine($"  {lines.Count} intersecting pairs; listing the first 120.");
                lines.RemoveRange(120, lines.Count - 120);
            }
            sb.AppendLine($"  --- intersecting pairs ({hits}, of which {sameAssembly} inside one assembly) ---");
            foreach (string l in lines) sb.AppendLine(l);
            if (hits == 0) sb.AppendLine("    Nothing interpenetrates by more than the tolerance.");

            sb.AppendLine($"  {pieces.Count} props measured. tolerance={OverlapTolerance:0.00} m, " +
                          $"intersections={hits}, buried={buried}, floating={floating}");
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// How deep two props have to grow into each other before it is a fault and not joinery.
        /// A rail meeting a post, a canopy sitting on its posts and a valance hanging off an eave
        /// all measure within a couple of centimetres of zero.
        /// </summary>
        const float OverlapTolerance = 0.25f;

        struct PropPiece
        {
            public string owner;        // reported name, e.g. "JudgeBackdrop_M_ApronProp#7"
            public string assembly;     // the parent object, so pieces of one structure can be tagged
            public MeshRenderer renderer;
            public Bounds bounds;
        }

        /// <summary>Ground-like surfaces. Everything on the map overlaps these by design.</summary>
        static readonly string[] NotAProp =
        {
            "Surround", "Ground", "Lawn", "Apron", "Chalk", "Path", "Lane", "Blade", "Grass",
            "Hill_", "PlazaDisc", "Plaza_", "Basin", "Water", "PondBank", "Guide", "Stamp", "Cut",
        };

        /// <summary>
        /// Pairs that are supposed to interpenetrate. An empty second name means "with anything".
        /// Every entry is a real assembly detail, not a way of quietening a finding:
        /// characters sit on furniture, text quads sit proud of the boards they label, inlays are
        /// laid into panels, and the windmill's wheel is mounted through the cap.
        /// </summary>
        static readonly (string a, string b)[] OverlapExcused =
        {
            ("Sails", ""), ("Windmill", ""),
            ("Judge", ""), ("Mower", ""), ("Rival", ""), ("Duck", ""), ("Spectator", ""),
            ("Gnome", ""), ("Hat", "Body"),
            ("Title", ""), ("Name", ""), ("Card", ""), ("Text", ""),
            ("Inlay", ""), ("Bunting", ""), ("Pennant", ""),
        };

        /// <summary>Things that hang, so having nothing underneath them is correct.</summary>
        static bool Aloft(string name)
            => name.Contains("Bunting") || name.Contains("Pennant") || name.Contains("Title")
            || name.Contains("Name") || name.Contains("Card") || name.Contains("Text");

        static bool Excused(string a, string b)
        {
            foreach (var (x, y) in OverlapExcused)
            {
                if (y.Length == 0) { if (a.Contains(x) || b.Contains(x)) return true; }
                else if ((a.Contains(x) && b.Contains(y)) || (b.Contains(x) && a.Contains(y))) return true;
            }
            return false;
        }

        static Vector3 Penetration(Bounds a, Bounds b) => new Vector3(
            Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x),
            Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y),
            Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z));

        static float Volume(Bounds b)
            // Clamped: a flag, a banner or a bunting pennant is millimetres thick, and a zero on one
            // axis would make every one of them "100% swallowed" by whatever it is near.
            => Mathf.Max(b.size.x, 0.05f) * Mathf.Max(b.size.y, 0.05f) * Mathf.Max(b.size.z, 0.05f);

        /// <summary>
        /// True if <paramref name="under"/> is plausibly what <paramref name="thing"/> is standing
        /// or hanging on: close enough horizontally, and reaching up to at least its base.
        /// </summary>
        static bool Supports(Bounds under, Bounds thing)
        {
            if (under.max.y < thing.min.y - 0.3f) return false;      // too short to be holding it up
            if (under.min.y > thing.min.y + 0.6f) return false;      // starts above us, so it is not below us
            return under.max.x > thing.min.x - 1.5f && under.min.x < thing.max.x + 1.5f
                && under.max.z > thing.min.z - 1.5f && under.min.z < thing.max.z + 1.5f;
        }

        /// <summary>
        /// Every prop in the scene as its own bounding box, combined meshes split back into the
        /// individual props that were baked into them.
        /// </summary>
        static List<PropPiece> CollectPieces(StringBuilder sb)
        {
            var pieces = new List<PropPiece>();
            int skipped = 0, splitMeshes = 0;

            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,
                                                                     FindObjectsSortMode.None))
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                bool ground = false;
                foreach (string n in NotAProp)
                    if (mr.name.Contains(n)) { ground = true; break; }
                if (ground) { skipped++; continue; }

                var mesh = mf.sharedMesh;
                // 200k triangles is a combined blade or grass layer, not dressing. Splitting one
                // costs seconds and reports nothing useful.
                if (mesh.triangles.Length > 600000)
                {
                    skipped++;
                    sb.AppendLine($"  (skipped {mr.name}: {mesh.triangles.Length / 3} triangles)");
                    continue;
                }

                var clusters = MeshClusters(mesh, mr.localToWorldMatrix);
                if (clusters.Count > 1) splitMeshes++;
                string assembly = mr.transform.parent != null ? mr.transform.parent.name : mr.name;

                for (int i = 0; i < clusters.Count; i++)
                {
                    pieces.Add(new PropPiece
                    {
                        owner = clusters.Count > 1 ? $"{mr.name}#{i}" : mr.name,
                        assembly = assembly,
                        renderer = mr,
                        bounds = clusters[i]
                    });
                }
            }

            sb.AppendLine($"  {pieces.Count} props from {splitMeshes} combined meshes; " +
                          $"{skipped} ground or oversized meshes skipped.");
            return pieces;
        }

        /// <summary>
        /// One bounding box per connected run of triangles, in world space.
        ///
        /// The Combiner bakes many props into one mesh and never welds them together, so a
        /// connected component is exactly one placed prop. Welding at 1 mm is the point: props that
        /// merely interpenetrate share no vertices and stay separate, so they can still be compared,
        /// while a flat-shaded box's duplicated corners collapse into one point as they should.
        /// </summary>
        static List<Bounds> MeshClusters(Mesh mesh, Matrix4x4 toWorld)
        {
            var result = new List<Bounds>();
            var verts = mesh.vertices;
            var tris = mesh.triangles;
            if (tris.Length == 0 || verts.Length == 0) return result;

            var weld = new Dictionary<Vector3Int, int>(verts.Length);
            var remap = new int[verts.Length];
            const float q = 1000f;   // 1 mm buckets
            for (int i = 0; i < verts.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(verts[i].x * q),
                                         Mathf.RoundToInt(verts[i].y * q),
                                         Mathf.RoundToInt(verts[i].z * q));
                if (!weld.TryGetValue(key, out int idx)) { idx = weld.Count; weld[key] = idx; }
                remap[i] = idx;
            }

            var parent = new int[weld.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int x, int y) { int rx = Find(x), ry = Find(y); if (rx != ry) parent[rx] = ry; }

            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = remap[tris[t]], b = remap[tris[t + 1]], c = remap[tris[t + 2]];
                Union(a, b);
                Union(b, c);
            }

            var boxes = new Dictionary<int, Bounds>();
            for (int i = 0; i < verts.Length; i++)
            {
                int root = Find(remap[i]);
                Vector3 w = toWorld.MultiplyPoint3x4(verts[i]);
                if (boxes.TryGetValue(root, out var box)) { box.Encapsulate(w); boxes[root] = box; }
                else boxes[root] = new Bounds(w, Vector3.zero);
            }

            foreach (var kv in boxes) result.Add(kv.Value);
            return result;
        }

        /// <summary>
        /// Check every picture's start pose is genuinely inside it, with room to move.
        ///
        /// This is the sort of thing that looks fine on the one shape you happen to test and is
        /// broken on three others. The previous spawn logic passed on a heart and walked the
        /// player straight out of a duckling, because it picked a column on one row and then
        /// stepped north without rechecking. So: measure all of them, report the clearance in
        /// metres, and say plainly which ones fail.
        /// </summary>
        [MenuItem("Duck/Diagnose · Audit round start poses", priority = 53)]
        public static void AuditStartPoses()
        {
            var target = Object.FindFirstObjectByType<RoundTarget>();
            if (target == null)
            {
                Debug.LogError("[Duck] No RoundTarget in the scene — open Main and try again.");
                return;
            }

            var sb = new StringBuilder("[Duck] START POSE AUDIT\n");
            int bad = 0;

            foreach (var shape in TargetShapes.All)
            {
                target.Build(shape);
                target.GetStartPose(out Vector3 pos, out Quaternion rot);

                var sp = new Vector2(pos.x / target.shapeRadius, pos.z / target.shapeRadius);
                float d = TargetShapes.Sdf(shape, sp);
                // The SDF is in shape units; convert back to metres for a number that means something.
                float clearance = -d * target.shapeRadius;

                // How far the mower can drive before leaving the picture, along its start heading.
                float run = 0f;
                for (float t = 0.5f; t < target.shapeRadius * 2f; t += 0.5f)
                {
                    Vector3 s2 = pos + rot * Vector3.forward * t;
                    if (TargetShapes.Sdf(shape, new Vector2(s2.x / target.shapeRadius, s2.z / target.shapeRadius)) >= 0f) break;
                    run = t;
                }

                bool ok = d < 0f && clearance >= 1.0f;
                if (!ok) bad++;
                sb.AppendLine($"    {(ok ? "ok  " : "FAIL")} {shape,-9} at ({pos.x,6:0.0}, {pos.z,6:0.0})  " +
                              $"clearance {clearance,5:0.00} m   run ahead {run,5:0.0} m");
            }

            sb.AppendLine(bad == 0
                ? "  Every picture starts the mower inside itself with at least a metre of clearance."
                : $"  {bad} pictures start the mower outside or against an edge.");
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Calibrate the audit against meshes that are known-good.
        ///
        /// Signed volume has a handedness convention, and Unity is left-handed with clockwise
        /// front faces — so "negative volume means inside-out" is only true if the formula's
        /// convention matches Unity's, and getting that backwards would condemn every correct mesh
        /// in the project. Rather than reason about it, measure Unity's own primitives: whatever
        /// sign a built-in cube reports is the sign a correctly wound solid has.
        /// </summary>
        [MenuItem("Duck/Diagnose · Calibrate winding audit", priority = 50)]
        public static void Calibrate()
        {
            var sb = new StringBuilder("[Duck] WINDING AUDIT CALIBRATION\n");
            foreach (PrimitiveType t in new[] { PrimitiveType.Cube, PrimitiveType.Sphere, PrimitiveType.Cylinder })
            {
                var go = GameObject.CreatePrimitive(t);
                var m = go.GetComponent<MeshFilter>().sharedMesh;
                var r = Audit(m);
                sb.AppendLine($"  Unity {t}: tris={r.tris} boundary={r.boundary} " +
                              $"nonManifold={r.nonManifold} badWinding={r.badWinding} volume={r.signedVolume:0.#####}");
                Object.DestroyImmediate(go);
            }

            var box = DuckPrimitives.ChamferBox(new Vector3(0.5f, 0.5f, 0.5f), 0.08f);
            var br = Audit(box);
            sb.AppendLine($"  Duck ChamferBox: tris={br.tris} boundary={br.boundary} " +
                          $"nonManifold={br.nonManifold} badWinding={br.badWinding} volume={br.signedVolume:0.#####}");
            Object.DestroyImmediate(box);

            void Check(string label, Mesh m)
            {
                var rr = Audit(m);
                sb.AppendLine($"  Duck {label}: tris={rr.tris} boundary={rr.boundary} " +
                              $"nonManifold={rr.nonManifold} badWinding={rr.badWinding} volume={rr.signedVolume:0.#####}");
                Object.DestroyImmediate(m);
            }

            Check("Cylinder", DuckPrimitives.Cylinder(0.5f, 0.5f, 1f, 16));
            Check("Prism", DuckPrimitives.Prism(1f, 1f, 1f));
            Check("Hill", DuckPrimitives.Hill(1f, 0.5f, 3, 12, 1));
            Check("SquareRing", DuckPrimitives.SquareRing(1f, 2f, 4, 0f, 1));

            Debug.Log(sb.ToString());
        }

        struct Report
        {
            public int tris, boundary, nonManifold, badWinding;
            public float signedVolume;
        }

        static Report Audit(Mesh mesh)
        {
            var r = new Report();
            var verts = mesh.vertices;
            var tris = mesh.triangles;
            r.tris = tris.Length / 3;

            // Weld by position so seams between duplicated vertices do not read as holes; the
            // exporter splits vertices for flat shading and those are not real boundaries.
            var weld = new Dictionary<Vector3Int, int>(verts.Length);
            var remap = new int[verts.Length];
            const float q = 1e4f;   // 0.1 mm buckets
            for (int i = 0; i < verts.Length; i++)
            {
                var key = new Vector3Int(
                    Mathf.RoundToInt(verts[i].x * q),
                    Mathf.RoundToInt(verts[i].y * q),
                    Mathf.RoundToInt(verts[i].z * q));
                if (!weld.TryGetValue(key, out int idx)) { idx = weld.Count; weld[key] = idx; }
                remap[i] = idx;
            }

            // Directed edge counts. A well-formed closed surface uses every undirected edge twice,
            // once in each direction.
            var directed = new Dictionary<long, int>(tris.Length);
            long Key(int a, int b) => ((long)a << 32) | (uint)b;

            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = remap[tris[t]], b = remap[tris[t + 1]], c = remap[tris[t + 2]];
                if (a == b || b == c || a == c) continue;   // degenerate

                foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
                {
                    long k = Key(u, v);
                    directed.TryGetValue(k, out int n);
                    directed[k] = n + 1;
                }

                // Signed volume of the tetrahedron to the origin; sums to the enclosed volume.
                Vector3 p0 = verts[tris[t]], p1 = verts[tris[t + 1]], p2 = verts[tris[t + 2]];
                r.signedVolume += Vector3.Dot(p0, Vector3.Cross(p1, p2)) / 6f;
            }

            var seen = new HashSet<long>();
            foreach (var kv in directed)
            {
                int u = (int)(kv.Key >> 32);
                int v = (int)(kv.Key & 0xFFFFFFFF);
                long undirected = u < v ? Key(u, v) : Key(v, u);
                if (!seen.Add(undirected)) continue;

                directed.TryGetValue(Key(u, v), out int fwd);
                directed.TryGetValue(Key(v, u), out int rev);

                int total = fwd + rev;
                if (total == 1) r.boundary++;
                else if (total > 2) r.nonManifold++;
                // Two triangles sharing an edge in the SAME direction are wound inconsistently.
                if (fwd > 1 || rev > 1) r.badWinding++;
            }

            return r;
        }

        static List<string> ModelFiles()
        {
            var list = new List<string>();
            const string dir = "Assets/Art/Models";
            if (!AssetDatabase.IsValidFolder(dir)) return list;
            foreach (string full in System.IO.Directory.GetFiles(dir, "*.*", System.IO.SearchOption.AllDirectories))
            {
                string ext = System.IO.Path.GetExtension(full).ToLowerInvariant();
                if (ext == ".fbx" || ext == ".obj" || ext == ".glb" || ext == ".gltf")
                    list.Add(full.Replace(System.IO.Path.DirectorySeparatorChar, '/'));
            }
            list.Sort();
            return list;
        }
    }
}
