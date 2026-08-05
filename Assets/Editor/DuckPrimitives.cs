using System.Collections.Generic;
using UnityEngine;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Procedural mesh primitives with chamfered edges.
    ///
    /// Unity's built-in cube has razor edges, which is exactly what the art bible bans: a
    /// chamfer of a centimetre or two catches a highlight and is most of the difference between
    /// "toy set" and "grey box". Everything the environment builder places is made from these.
    /// </summary>
    public static class DuckPrimitives
    {
        /// <summary>
        /// A box whose 12 edges and 8 corners are cut back by <paramref name="chamfer"/>.
        /// 24 vertices, 26 faces, flat shaded.
        /// </summary>
        public static Mesh ChamferBox(Vector3 half, float chamfer)
        {
            chamfer = Mathf.Min(chamfer, Mathf.Min(half.x, Mathf.Min(half.y, half.z)) * 0.49f);
            Vector3 inner = half - Vector3.one * chamfer;

            var verts = new List<Vector3>(24);
            var tris = new List<int>(3 * 26 * 2);

            // Three vertices per corner, one pushed out along each axis.
            // Index layout: corner c in [0,8), axis a in [0,3) -> c * 3 + a
            for (int c = 0; c < 8; c++)
            {
                float sx = (c & 1) == 0 ? -1f : 1f;
                float sy = (c & 2) == 0 ? -1f : 1f;
                float sz = (c & 4) == 0 ? -1f : 1f;
                verts.Add(new Vector3(sx * half.x, sy * inner.y, sz * inner.z));  // axis X
                verts.Add(new Vector3(sx * inner.x, sy * half.y, sz * inner.z));  // axis Y
                verts.Add(new Vector3(sx * inner.x, sy * inner.y, sz * half.z));  // axis Z
            }

            int V(int corner, int axis) => corner * 3 + axis;

            // ---- the six flat faces ----
            AddQuad(tris, V(1, 0), V(5, 0), V(7, 0), V(3, 0));   // +X
            AddQuad(tris, V(0, 0), V(2, 0), V(6, 0), V(4, 0));   // -X
            AddQuad(tris, V(2, 1), V(3, 1), V(7, 1), V(6, 1));   // +Y
            AddQuad(tris, V(0, 1), V(4, 1), V(5, 1), V(1, 1));   // -Y
            AddQuad(tris, V(4, 2), V(6, 2), V(7, 2), V(5, 2));   // +Z
            AddQuad(tris, V(0, 2), V(1, 2), V(3, 2), V(2, 2));   // -Z

            // ---- the twelve edge chamfers ----
            // Edges along Z (corners differing in bit 4), joining the X and Y faces.
            AddEdge(tris, 0, 4, 0, 1); AddEdge(tris, 5, 1, 0, 1);
            AddEdge(tris, 3, 7, 0, 1); AddEdge(tris, 6, 2, 0, 1);
            // Edges along Y (bit 2), joining X and Z faces.
            AddEdge(tris, 4, 6, 0, 2); AddEdge(tris, 2, 0, 0, 2);
            AddEdge(tris, 1, 3, 0, 2); AddEdge(tris, 7, 5, 0, 2);
            // Edges along X (bit 1), joining Y and Z faces.
            AddEdge(tris, 0, 1, 1, 2); AddEdge(tris, 3, 2, 1, 2);
            AddEdge(tris, 6, 7, 1, 2); AddEdge(tris, 5, 4, 1, 2);

            // ---- the eight corner triangles ----
            for (int c = 0; c < 8; c++)
            {
                bool flip = ((c & 1) != 0) ^ ((c & 2) != 0) ^ ((c & 4) != 0);
                if (flip) { tris.Add(V(c, 0)); tris.Add(V(c, 1)); tris.Add(V(c, 2)); }
                else { tris.Add(V(c, 0)); tris.Add(V(c, 2)); tris.Add(V(c, 1)); }
            }

            return Finish(verts, tris, "ChamferBox");
        }

        static void AddEdge(List<int> tris, int cA, int cB, int axisA, int axisB)
        {
            int a0 = cA * 3 + axisA, a1 = cA * 3 + axisB;
            int b0 = cB * 3 + axisA, b1 = cB * 3 + axisB;
            AddQuad(tris, a0, b0, b1, a1);
        }

        static void AddQuad(List<int> tris, int a, int b, int c, int d)
        {
            tris.Add(a); tris.Add(b); tris.Add(c);
            tris.Add(a); tris.Add(c); tris.Add(d);
        }

        /// <summary>A capped cylinder along Y, optionally tapered, with chamfered rims.</summary>
        public static Mesh Cylinder(float radiusBottom, float radiusTop, float height, int segments, float chamfer = 0.01f)
        {
            segments = Mathf.Max(4, segments);
            var verts = new List<Vector3>();
            var tris = new List<int>();

            float hh = height * 0.5f;
            float rbIn = Mathf.Max(radiusBottom - chamfer, 0.001f);
            float rtIn = Mathf.Max(radiusTop - chamfer, 0.001f);
            float yb = -hh, ybc = -hh + chamfer, ytc = hh - chamfer, yt = hh;

            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts.Add(new Vector3(d.x * rbIn, yb, d.z * rbIn));       // 0 bottom cap rim
                verts.Add(new Vector3(d.x * radiusBottom, ybc, d.z * radiusBottom)); // 1 lower wall
                verts.Add(new Vector3(d.x * radiusTop, ytc, d.z * radiusTop));       // 2 upper wall
                verts.Add(new Vector3(d.x * rtIn, yt, d.z * rtIn));       // 3 top cap rim
            }

            int centreBottom = verts.Count; verts.Add(new Vector3(0f, yb, 0f));
            int centreTop = verts.Count; verts.Add(new Vector3(0f, yt, 0f));

            for (int i = 0; i < segments; i++)
            {
                int c = i * 4, n = ((i + 1) % segments) * 4;
                AddQuad(tris, c + 0, n + 0, n + 1, c + 1);   // bottom chamfer
                AddQuad(tris, c + 1, n + 1, n + 2, c + 2);   // wall
                AddQuad(tris, c + 2, n + 2, n + 3, c + 3);   // top chamfer
                tris.Add(centreBottom); tris.Add(n + 0); tris.Add(c + 0);
                tris.Add(centreTop); tris.Add(c + 3); tris.Add(n + 3);
            }

            return Finish(verts, tris, "Cylinder");
        }

        /// <summary>A gable roof / tent prism: ridge along Z, sloping down to ±X.</summary>
        public static Mesh Prism(float width, float height, float depth)
        {
            float hw = width * 0.5f, hd = depth * 0.5f;
            var verts = new List<Vector3>
            {
                new Vector3(-hw, 0f, -hd), new Vector3( hw, 0f, -hd),
                new Vector3( hw, 0f,  hd), new Vector3(-hw, 0f,  hd),
                new Vector3(0f, height, -hd), new Vector3(0f, height, hd)
            };
            var tris = new List<int>();
            AddQuad(tris, 0, 1, 2, 3);          // floor (wound down)
            AddQuad(tris, 1, 4, 5, 2);          // +X slope
            AddQuad(tris, 3, 5, 4, 0);          // -X slope
            tris.Add(0); tris.Add(4); tris.Add(1);   // -Z gable
            tris.Add(3); tris.Add(2); tris.Add(5);   // +Z gable
            return Finish(verts, tris, "Prism");
        }

        /// <summary>
        /// A low, irregular ground mound. Used for the distant hills, which need to read as
        /// landscape rather than as spheres poking through the horizon.
        /// </summary>
        public static Mesh Hill(float radius, float height, int rings, int segments, int seed)
        {
            var rng = new System.Random(seed);
            var verts = new List<Vector3>();
            var tris = new List<int>();

            // Per-direction radius wobble, smoothed around the loop so the base is not a circle.
            var wob = new float[segments];
            for (int i = 0; i < segments; i++) wob[i] = 0.78f + (float)rng.NextDouble() * 0.44f;
            for (int pass = 0; pass < 3; pass++)
            {
                var tmp = new float[segments];
                for (int i = 0; i < segments; i++)
                    tmp[i] = (wob[(i - 1 + segments) % segments] + wob[i] * 2f + wob[(i + 1) % segments]) * 0.25f;
                wob = tmp;
            }

            verts.Add(new Vector3(0f, height, 0f));
            for (int r = 1; r <= rings; r++)
            {
                float t = r / (float)rings;
                float y = height * Mathf.Cos(t * Mathf.PI * 0.5f);
                float rad = radius * Mathf.Sin(t * Mathf.PI * 0.5f);
                for (int s = 0; s < segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    float w = Mathf.Lerp(1f, wob[s], t);
                    verts.Add(new Vector3(Mathf.Cos(a) * rad * w, y, Mathf.Sin(a) * rad * w));
                }
            }

            for (int s = 0; s < segments; s++)
            {
                int a = 1 + s, b = 1 + (s + 1) % segments;
                tris.Add(0); tris.Add(b); tris.Add(a);
            }
            for (int r = 1; r < rings; r++)
            {
                int rowA = 1 + (r - 1) * segments;
                int rowB = 1 + r * segments;
                for (int s = 0; s < segments; s++)
                {
                    int s2 = (s + 1) % segments;
                    AddQuad(tris, rowA + s, rowA + s2, rowB + s2, rowB + s);
                }
            }

            return Finish(verts, tris, "Hill");
        }

        /// <summary>
        /// A square annulus in the XZ plane, coplanar with the lawn so there is nothing to
        /// z-fight against. Optional outward-increasing undulation.
        /// </summary>
        /// <summary>
        /// A flat square annulus, optionally rolling toward its outer edge.
        ///
        /// <paramref name="flatRadius"/> is how far out the surface stays dead level before the
        /// undulation is allowed to start. The championship ground stands on this mesh, and every
        /// plot, station, stand and fence in it is placed at y=0 — so any roll underneath them
        /// leaves lawns floating over dips and buried in rises. Keeping the venue's whole footprint
        /// flat and rolling the landscape only beyond it is the fix; a separate flat slab laid over
        /// the top would only add a seam and z-fighting.
        /// </summary>
        public static Mesh SquareRing(float inner, float outer, int subdiv, float undulation, int seed,
                                      float flatRadius = 0f)
        {
            var rng = new System.Random(seed);
            float phase = (float)rng.NextDouble() * 100f;

            var verts = new List<Vector3>();
            var tris = new List<int>();
            int n = Mathf.Max(1, subdiv);

            // Four trapezoidal strips, one per side, so the ring stays a clean quad grid.
            for (int side = 0; side < 4; side++)
            {
                int baseIndex = verts.Count;
                for (int j = 0; j <= n; j++)
                {
                    float t = j / (float)n;
                    float rIn = Mathf.Lerp(-inner, inner, t);
                    float rOut = Mathf.Lerp(-outer, outer, t);
                    for (int k = 0; k <= n; k++)
                    {
                        float u = k / (float)n;
                        float across = Mathf.Lerp(inner, outer, u);
                        float along = Mathf.Lerp(rIn, rOut, u);
                        Vector3 p = side switch
                        {
                            0 => new Vector3(along, 0f, across),
                            1 => new Vector3(along, 0f, -across),
                            2 => new Vector3(across, 0f, along),
                            _ => new Vector3(-across, 0f, along),
                        };
                        if (undulation > 0f)
                        {
                            float d = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z));
                            float start = Mathf.Max(inner, flatRadius);
                            float fall = d <= start ? 0f : Mathf.InverseLerp(start, outer, d);
                            p.y = (Mathf.Sin(p.x * 0.06f + phase) * Mathf.Cos(p.z * 0.052f - phase) * 0.5f
                                   + Mathf.Sin((p.x + p.z) * 0.021f) * 0.5f) * undulation * fall;
                        }
                        verts.Add(p);
                    }
                }
                for (int j = 0; j < n; j++)
                    for (int k = 0; k < n; k++)
                    {
                        int i0 = baseIndex + j * (n + 1) + k;
                        int i1 = i0 + 1;
                        int i2 = i0 + (n + 1);
                        int i3 = i2 + 1;
                        if (side == 0 || side == 3) AddQuad(tris, i0, i2, i3, i1);
                        else AddQuad(tris, i0, i1, i3, i2);
                    }
            }

            // A flat ring's winding differs per side, and RecalculateNormals happily points half
            // of them at the ground — which renders the apron and meadow black. The surface is
            // near-horizontal by construction, so just assert the normal.
            var mesh = Finish(verts, tris, "SquareRing");
            var up = new Vector3[mesh.vertexCount];
            for (int i = 0; i < up.Length; i++) up[i] = Vector3.up;
            mesh.SetNormals(up);
            return mesh;
        }

        /// <summary>
        /// Force every triangle in a shell to agree, then face them outward.
        ///
        /// Hand-derived winding is where this kind of geometry goes wrong, and it goes wrong
        /// invisibly: an inverted solid still draws (you are seeing its inside), and because
        /// RecalculateNormals derives normals from the winding, its normals point inward too — so
        /// the lit side of every prop was the side facing away from the sun. That was measured, not
        /// guessed: Unity's own cube audits at +1 signed volume with zero inconsistent edges, and
        /// ChamferBox audited at -0.94 with 24, Cylinder at -0.77.
        ///
        /// Rather than fix twelve chamfer strips by hand and hope, this makes correctness a
        /// property of the exit path: flood-fill across shared edges so neighbours agree, then, for
        /// a closed shell, flip the whole thing if it encloses negative volume. Every primitive
        /// that goes through here is correct by construction, including ones written later.
        /// </summary>
        static void MakeOutwardFacing(List<Vector3> verts, List<int> tris)
        {
            int triCount = tris.Count / 3;
            if (triCount == 0) return;

            // Weld by position: flat-shaded builders duplicate vertices, and unwelded seams would
            // look like the mesh is made of disconnected islands.
            var weld = new Dictionary<Vector3Int, int>(verts.Count);
            var remap = new int[verts.Count];
            const float q = 1e4f;
            for (int i = 0; i < verts.Count; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(verts[i].x * q),
                                         Mathf.RoundToInt(verts[i].y * q),
                                         Mathf.RoundToInt(verts[i].z * q));
                if (!weld.TryGetValue(key, out int idx)) { idx = weld.Count; weld[key] = idx; }
                remap[i] = idx;
            }

            // Undirected edge -> the triangles touching it.
            var edgeTris = new Dictionary<long, List<int>>(triCount * 3);
            long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

            void Register(int t)
            {
                int a = remap[tris[t * 3]], b = remap[tris[t * 3 + 1]], c = remap[tris[t * 3 + 2]];
                foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
                {
                    long k = Key(u, v);
                    if (!edgeTris.TryGetValue(k, out var list)) edgeTris[k] = list = new List<int>(2);
                    list.Add(t);
                }
            }
            for (int t = 0; t < triCount; t++) Register(t);

            // True if triangle t traverses the edge u->v in that direction.
            bool Traverses(int t, int u, int v)
            {
                int a = remap[tris[t * 3]], b = remap[tris[t * 3 + 1]], c = remap[tris[t * 3 + 2]];
                return (a == u && b == v) || (b == u && c == v) || (c == u && a == v);
            }

            void Flip(int t)
            {
                (tris[t * 3 + 1], tris[t * 3 + 2]) = (tris[t * 3 + 2], tris[t * 3 + 1]);
            }

            // Flood fill: a neighbour that traverses the shared edge the same way as us is wound
            // the opposite way round and has to be turned over.
            var visited = new bool[triCount];
            var queue = new Queue<int>();
            for (int seed = 0; seed < triCount; seed++)
            {
                if (visited[seed]) continue;
                visited[seed] = true;
                queue.Enqueue(seed);

                while (queue.Count > 0)
                {
                    int t = queue.Dequeue();
                    int a = remap[tris[t * 3]], b = remap[tris[t * 3 + 1]], c = remap[tris[t * 3 + 2]];

                    foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
                    {
                        if (!edgeTris.TryGetValue(Key(u, v), out var list)) continue;
                        foreach (int n in list)
                        {
                            if (n == t || visited[n]) continue;
                            if (Traverses(n, u, v)) Flip(n);
                            visited[n] = true;
                            queue.Enqueue(n);
                        }
                    }
                }
            }

            bool closed = true;
            foreach (var kv in edgeTris) if (kv.Value.Count != 2) { closed = false; break; }

            if (closed)
            {
                // A closed shell has an inside, so turn it outward.
                float volume = 0f;
                for (int t = 0; t < triCount; t++)
                {
                    Vector3 p0 = verts[tris[t * 3]], p1 = verts[tris[t * 3 + 1]], p2 = verts[tris[t * 3 + 2]];
                    volume += Vector3.Dot(p0, Vector3.Cross(p1, p2)) / 6f;
                }
                if (volume < 0f) for (int t = 0; t < triCount; t++) Flip(t);
                return;
            }

            // An open shell has no inside — but a horizontal one still has a right way up, and
            // making the winding merely CONSISTENT leaves which way arbitrary: it depends on where
            // the flood fill happened to start. That is not a theoretical worry. It face-planted
            // the rival plot aprons — every one of their two thousand triangles ended up pointing
            // at the ground, and under back-face culling an apron you cannot see is a hole in the
            // world. The main meadow, built by the same function, happened to come out the right
            // way up, which is exactly how this hid.
            //
            // So: measure which way the surface faces, weighted by area so a few edge slivers
            // cannot outvote the field, and turn it over if it is looking down.
            float upward = 0f;
            for (int t = 0; t < triCount; t++)
            {
                Vector3 p0 = verts[tris[t * 3]], p1 = verts[tris[t * 3 + 1]], p2 = verts[tris[t * 3 + 2]];
                Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);   // length is twice the area
                upward += cross.y;
            }

            // Near zero means the surface is vertical — a wall or a fence panel — and has no
            // up to be wrong about. Leave those exactly as built.
            float scale = 0f;
            for (int t = 0; t < triCount; t++)
            {
                Vector3 p0 = verts[tris[t * 3]], p1 = verts[tris[t * 3 + 1]], p2 = verts[tris[t * 3 + 2]];
                scale += Vector3.Cross(p1 - p0, p2 - p0).magnitude;
            }
            if (scale < 1e-6f || Mathf.Abs(upward) < scale * 0.25f) return;

            if (upward < 0f) for (int t = 0; t < triCount; t++) Flip(t);
        }

        static Mesh Finish(List<Vector3> verts, List<int> tris, string name)
        {
            MakeOutwardFacing(verts, tris);

            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            // Every generated mesh carries a white colour stream so it can share the vertex
            // colour prop material with the Blender assets without a special case.
            var white = new Color32(255, 255, 255, 255);
            var cols = new Color32[verts.Count];
            for (int i = 0; i < cols.Length; i++) cols[i] = white;
            mesh.SetColors(cols);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
