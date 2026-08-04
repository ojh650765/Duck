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
