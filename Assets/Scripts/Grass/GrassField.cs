using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Builds and maintains the lawn: one ground mesh plus a grid of blade chunks.
    ///
    /// Every chunk shares the same baked blade mesh (one per LOD) and relies on the shader
    /// hashing the chunk's world position to make each one look different. That keeps grass
    /// memory at a couple of megabytes rather than a couple of hundred, which is the only way
    /// this runs in a browser.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class GrassField : MonoBehaviour
    {
        [Header("Materials")]
        public Material groundMaterial;
        public Material bladeMaterial;

        [Tooltip("Build the flat ground plane and its collider along with the blades.\n\n" +
                 "True everywhere in the mowing round, where this IS the lawn. False for a scene " +
                 "that already owns its own floor — Bloom Rush has a radial mesh with ramps in it " +
                 "and a mesh collider to match, and a flat box laid over the top would be the " +
                 "surface the mower's suspension actually found, flattening every climb in the " +
                 "arena. The blade layer is the part that is worth reusing; the ground is not.")]
        public bool buildGround = true;

        [Header("Extent")]
        [Tooltip("Side length of the field in metres. Zero means the venue's own Field.Size, which " +
                 "is what every lawn in the mowing round wants. " +
                 "Exists because the goose arena is ninety metres across and the mowing field is " +
                 "sixty-four. The blade layer is the right way to draw a lot of grass — it bakes " +
                 "chunked meshes with real LOD instead of one GameObject per plant — but it was " +
                 "welded to a constant, so anything that is not a competition lawn could not use it " +
                 "and had to scatter objects by hand instead. One field, two sizes.")]
        public float fieldSize;

        [Tooltip("Anything implementing IBareGround here has blades kept off it — the goose arena's " +
                 "dirt strips and gardens. Left empty, the whole field is grassed, which is what " +
                 "every lawn in the mowing round wants.")]
        public MonoBehaviour bareGround;

        /// <summary>The side this field actually covers.</summary>
        public float Size => fieldSize > 0f ? fieldSize : Field.Size;
        /// <summary>Half of it. Roots are laid out in [-Half, +Half].</summary>
        public float HalfSize => Size * 0.5f;

        [Header("Chunking")]
        public int chunksPerSide = 8;
        [Tooltip("Blades per square metre at full detail.")]
        public float density0 = 105f;
        [Tooltip("Blades per square metre at reduced detail.")]
        public float density1 = 30f;

        [Header("Blade shape")]
        public float bladeHeight = 0.30f;
        public float bladeWidth = 0.042f;
        [Tooltip("How far the blade tip curves away from vertical, in metres.")]
        public float bladeCurve = 0.085f;

        [Header("LOD (metres from camera to chunk centre)")]
        [Tooltip("Swap to the far blade mesh here. Must be past the point where the shader has " +
                 "already thinned away every blade the far mesh drops, or the swap is visible.")]
        public float lod0Distance = 34f;
        [Tooltip("Stop drawing blades entirely. Past the shader's thinning range, so nothing is lost.")]
        public float lod1Distance = 48f;
        [Tooltip("Recompute LOD this often. Grass does not need it every frame.")]
        public float lodInterval = 0.15f;

        [Header("Wind")]
        public float windStrength = 0.055f;
        public float windSpeed = 1.9f;
        public float windGustScale = 0.035f;
        public float windGustSpeed = 2.6f;
        public Vector2 windDirection = new Vector2(0.82f, 0.57f);

        [Header("Ground mesh")]
        public int groundSubdivisions = 32;

        Mesh _groundMesh, _bladeMeshL0, _bladeMeshL1;
        Transform[] _chunks;
        MeshFilter[] _chunkFilters;
        MeshRenderer[] _chunkRenderers;
        int[] _chunkLod;
        float _lodTimer;
        Camera _cam;

        static readonly int IdWindParams = Shader.PropertyToID("_WindParams");
        static readonly int IdWindDirection = Shader.PropertyToID("_WindDirection");

        void Awake()
        {
            if (buildGround) BuildGround();
            BuildBladeMeshes();
            BuildChunks();
            ApplyWind();
        }

        void OnDestroy()
        {
            if (_groundMesh) DestroyImmediate(_groundMesh);
            if (_bladeMeshL0) DestroyImmediate(_bladeMeshL0);
            if (_bladeMeshL1) DestroyImmediate(_bladeMeshL1);
        }

        public void ApplyWind()
        {
            Shader.SetGlobalVector(IdWindParams, new Vector4(windStrength, windSpeed, windGustScale, windGustSpeed));
            Vector2 d = windDirection.sqrMagnitude > 1e-5f ? windDirection.normalized : Vector2.right;
            Shader.SetGlobalVector(IdWindDirection, new Vector4(d.x, d.y, 0, 0));
        }

        // ------------------------------------------------------------------ ground

        void BuildGround()
        {
            int n = Mathf.Max(2, groundSubdivisions);
            var verts = new Vector3[(n + 1) * (n + 1)];
            var norms = new Vector3[verts.Length];
            var tris = new int[n * n * 6];

            for (int z = 0; z <= n; z++)
            {
                for (int x = 0; x <= n; x++)
                {
                    int i = z * (n + 1) + x;
                    verts[i] = new Vector3(x / (float)n * Size - HalfSize, 0f,
                                           z / (float)n * Size - HalfSize);
                    norms[i] = Vector3.up;
                }
            }

            int t = 0;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int i = z * (n + 1) + x;
                    tris[t++] = i; tris[t++] = i + n + 1; tris[t++] = i + n + 2;
                    tris[t++] = i; tris[t++] = i + n + 2; tris[t++] = i + 1;
                }
            }

            _groundMesh = new Mesh { name = "LawnGround" };
            _groundMesh.SetVertices(verts);
            _groundMesh.SetNormals(norms);
            _groundMesh.SetTriangles(tris, 0);
            _groundMesh.RecalculateBounds();

            var go = new GameObject("LawnGround");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _groundMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = groundMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;

            // A single flat collider for the whole lawn — the mower's suspension rays hit this.
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.5f, 0f);
            box.size = new Vector3(Size, 1f, Size);
        }

        // ------------------------------------------------------------------ blades

        void BuildBladeMeshes()
        {
            // Both LODs are baked from the SAME seed and the same grid, with the far one simply
            // dropping every blade whose id is past the cutoff. They were baked from different
            // seeds, which meant that at the swap distance every blade in the chunk jumped to a new
            // position at once — an eight-metre square of grass visibly reshuffling as you drove
            // past it, which is the grid pattern moving with the player.
            //
            // A strict subset means the swap only removes blades, and the shader has already faded
            // exactly those blades to nothing by the time it happens, so there is nothing to see.
            float chunkSize = Size / chunksPerSide;
            _bladeMeshL0 = BakeBladeMesh("GrassBladesL0", chunkSize, density0, BladeSeed, 1f,
                                         bladeHeight, bladeWidth, bladeCurve);
            _bladeMeshL1 = BakeBladeMesh("GrassBladesL1", chunkSize, density0, BladeSeed, BladeIdCutoff,
                                         bladeHeight, bladeWidth, bladeCurve);
            _bare = bareGround as IBareGround;
        }

        /// <summary>Blades with an id at or above this are dropped from the far LOD.</summary>
        public const float BladeIdCutoff = 0.5f;

        /// <summary>
        /// The seed both LODs are baked from. Named rather than inlined because the rival plots
        /// bake the same patch: two different seeds would give the venue two different lawns.
        /// </summary>
        public const int BladeSeed = 1234;

        /// <summary>
        /// Bakes a chunk-sized patch of blades on a jittered grid. Roots are laid out in
        /// [-chunkSize/2, +chunkSize/2] so the mesh is centred on its chunk transform.
        ///
        /// Static and public because <see cref="RivalBlades"/> bakes the rival plots' patches from
        /// it. A second copy of the blade profile is the one thing that must not exist here: the
        /// venue tour cuts straight from the player's lawn to a rival's, and any drift in height,
        /// width, curve or vertex layout between the two shows up as a different species of grass.
        /// </summary>
        /// <param name="keepAt">
        /// Optional test, in CHUNK-LOCAL space, for whether a blade may root at a point. Used to cut
        /// the goose arena's dirt strips and gardens out of the lawn — grass growing through a bed
        /// of soil is the sort of thing that reads as the level being unfinished. Null keeps
        /// everything, which is every lawn in the mowing round.
        /// </param>
        public static Mesh BakeBladeMesh(string name, float chunkSize, float density, int seed,
                                         float idCutoff, float bladeHeight, float bladeWidth,
                                         float bladeCurve,
                                         System.Func<Vector3, bool> keepAt = null)
        {
            int perSide = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(density * chunkSize * chunkSize)));
            int gridCount = perSide * perSide;

            // Draw the whole grid either way so the random stream — and therefore every blade's
            // position and id — is identical between LODs; only the keep test differs.
            var keep = new bool[gridCount];
            var ids = new float[gridCount];
            var jitters = new Vector2[gridCount];
            var rngPre = new System.Random(seed);
            int kept = 0;
            for (int i = 0; i < gridCount; i++)
            {
                jitters[i] = new Vector2((float)rngPre.NextDouble() - 0.5f, (float)rngPre.NextDouble() - 0.5f);
                ids[i] = (float)rngPre.NextDouble();
                keep[i] = ids[i] < idCutoff;
                if (keep[i]) kept++;
            }
            int bladeCount = Mathf.Max(1, kept);

            const int vertsPerBlade = 5;
            const int trisPerBlade = 3;

            var verts = new Vector3[bladeCount * vertsPerBlade];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            var data = new Vector4[verts.Length];
            var tris = new int[bladeCount * trisPerBlade * 3];

            float cell = chunkSize / perSide;
            float half = chunkSize * 0.5f;
            int vi = 0, ti = 0, blade = 0;

            for (int gz = 0; gz < perSide; gz++)
            {
                for (int gx = 0; gx < perSide; gx++)
                {
                    int cellIndex = gz * perSide + gx;
                    if (!keep[cellIndex]) continue;

                    float jx = jitters[cellIndex].x;
                    float jz = jitters[cellIndex].y;
                    float rx = -half + (gx + 0.5f + jx * 0.9f) * cell;
                    float rz = -half + (gz + 0.5f + jz * 0.9f) * cell;

                    // Tested at the JITTERED root, not at the grid cell, so the edge of a dirt strip
                    // is a ragged line of grass rather than a staircase.
                    if (keepAt != null && !keepAt(new Vector3(rx, 0f, rz))) continue;

                    float bladeId = ids[cellIndex];
                    blade++;
                    float h = bladeHeight;
                    float w = bladeWidth;
                    var root = new Vector3(rx, 0f, rz);
                    var d = new Vector4(rx, rz, bladeId, h);

                    // Blade profile: wide base, narrower waist, single tip, curving forward in +Z.
                    float midH = h * 0.55f;
                    float midCurve = bladeCurve * 0.30f;
                    float tipCurve = bladeCurve;

                    verts[vi + 0] = root + new Vector3(-w * 0.5f, 0f, 0f);
                    verts[vi + 1] = root + new Vector3(w * 0.5f, 0f, 0f);
                    verts[vi + 2] = root + new Vector3(-w * 0.30f, midH, midCurve);
                    verts[vi + 3] = root + new Vector3(w * 0.30f, midH, midCurve);
                    verts[vi + 4] = root + new Vector3(0f, h, tipCurve);

                    uvs[vi + 0] = new Vector2(0f, -1f);
                    uvs[vi + 1] = new Vector2(0f, 1f);
                    uvs[vi + 2] = new Vector2(0.55f, -1f);
                    uvs[vi + 3] = new Vector2(0.55f, 1f);
                    uvs[vi + 4] = new Vector2(1f, 0f);

                    // Tilted slightly back so the lit side faces up-ish and the grass never
                    // reads as a wall of vertical cards.
                    var n = new Vector3(0f, 0.34f, -0.94f).normalized;
                    for (int k = 0; k < vertsPerBlade; k++) { norms[vi + k] = n; data[vi + k] = d; }

                    tris[ti++] = vi + 0; tris[ti++] = vi + 2; tris[ti++] = vi + 3;
                    tris[ti++] = vi + 0; tris[ti++] = vi + 3; tris[ti++] = vi + 1;
                    tris[ti++] = vi + 2; tris[ti++] = vi + 4; tris[ti++] = vi + 3;

                    vi += vertsPerBlade;
                }
            }

            // Trimmed to what was actually emitted.
            //
            // The arrays are sized from the pre-count, and a keep test culls blades AFTER that — so a
            // chunk with a dirt strip through it would otherwise upload a few thousand degenerate
            // blades collapsed at the origin, and a fan of degenerate triangles with it. Invisible,
            // but paid for on every draw.
            if (blade < bladeCount)
            {
                System.Array.Resize(ref verts, blade * vertsPerBlade);
                System.Array.Resize(ref norms, blade * vertsPerBlade);
                System.Array.Resize(ref uvs, blade * vertsPerBlade);
                System.Array.Resize(ref data, blade * vertsPerBlade);
                System.Array.Resize(ref tris, blade * trisPerBlade * 3);
            }

            var mesh = new Mesh { name = name };
            mesh.indexFormat = verts.Length > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, data);
            mesh.SetTriangles(tris, 0);
            // Padded for wind, layover and root jitter so nothing is culled at a chunk edge.
            mesh.bounds = new Bounds(Vector3.zero,
                new Vector3(chunkSize + 1f, bladeHeight * 2.5f, chunkSize + 1f));
            mesh.UploadMeshData(true);
            return mesh;
        }

        void BuildChunks()
        {
            int count = chunksPerSide * chunksPerSide;
            _chunks = new Transform[count];
            _chunkFilters = new MeshFilter[count];
            _chunkRenderers = new MeshRenderer[count];
            _chunkLod = new int[count];
            _chunkOverride = new Mesh[count];

            float chunkSize = Size / chunksPerSide;
            var root = new GameObject("BladeChunks").transform;
            root.SetParent(transform, false);

            for (int z = 0; z < chunksPerSide; z++)
            {
                for (int x = 0; x < chunksPerSide; x++)
                {
                    int i = z * chunksPerSide + x;
                    var go = new GameObject($"Chunk_{x}_{z}");
                    go.transform.SetParent(root, false);
                    go.transform.position = new Vector3(
                        -HalfSize + (x + 0.5f) * chunkSize, 0f,
                        -HalfSize + (z + 0.5f) * chunkSize);

                    // A chunk that lands on bare ground gets its OWN mesh with those blades removed.
                    //
                    // Only the chunks that actually straddle a dirt strip or a garden pay for this;
                    // everything else keeps sharing the two common meshes, which is the whole reason
                    // the blade layer is affordable. In the arena that is about a third of them.
                    if (_bare != null)
                        _chunkOverride[i] = BakeChunkOverride(chunkSize, go.transform.position);

                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = _chunkOverride[i] != null ? _chunkOverride[i] : _bladeMeshL0;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = bladeMaterial;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = true;
                    mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                    _chunks[i] = go.transform;
                    _chunkFilters[i] = mf;
                    _chunkRenderers[i] = mr;
                    _chunkLod[i] = -1;
                }
            }
        }

        IBareGround _bare;
        Mesh[] _chunkOverride;

        /// <summary>
        /// The stand-in for "this chunk has no grass on it". Shared, empty, never uploaded with
        /// vertices — a sentinel rather than geometry, so a fully bare chunk costs one reference.
        /// </summary>
        static Mesh _empty;
        static Mesh EmptyMesh => _empty != null ? _empty
            : (_empty = new Mesh { name = "GrassBladesNone", hideFlags = HideFlags.DontSave });

        /// <summary>
        /// Bake a chunk-specific blade mesh with the bare ground cut out of it, or null if this
        /// chunk is entirely grass.
        ///
        /// Returning null for the common case is the whole design. Baking every chunk its own mesh
        /// would multiply the blade layer's memory by the chunk count for no benefit — the arena is
        /// mostly lawn, and only the handful of chunks that overlap a defending strip or a garden
        /// need to differ from the shared one.
        /// </summary>
        Mesh BakeChunkOverride(float chunkSize, Vector3 chunkWorld)
        {
            if (_bare == null) return null;

            // Classify the chunk first: all grass, all bare, or mixed.
            //
            // The three cases have wildly different costs and the middle one is the important one.
            // A chunk lying ENTIRELY on a dirt strip needs no mesh at all — the first version baked
            // it a full unique blade mesh and then culled every blade out of it, which is thousands
            // of vertices of empty geometry per chunk, uploaded and paid for. Across an arena with
            // four strips and four gardens that was most of the memory and most of the build hitch
            // that showed up as lag in the browser.
            int bare = 0, total = 0;
            const int probe = 5;
            for (int pz = 0; pz <= probe; pz++)
                for (int px = 0; px <= probe; px++)
                {
                    var p = chunkWorld + new Vector3((px / (float)probe - 0.5f) * chunkSize, 0f,
                                                     (pz / (float)probe - 0.5f) * chunkSize);
                    total++;
                    if (_bare.IsBare(p)) bare++;
                }

            if (bare == 0) return null;              // all grass: share the common mesh
            if (bare == total) return EmptyMesh;     // all bare: draw nothing at all

            return BakeBladeMesh("GrassBladesCut", chunkSize, density0, BladeSeed, 1f,
                                 bladeHeight, bladeWidth, bladeCurve,
                                 root => !_bare.IsBare(chunkWorld + root));
        }

        void LateUpdate()
        {
            if (SimClock.Scripted) return;
            TickLod(Time.deltaTime);
        }

        public void TickLod(float dt)
        {
            _lodTimer -= dt;
            if (_lodTimer > 0f) return;
            _lodTimer = lodInterval;

            if (_cam == null) _cam = Camera.main;
            if (_cam == null || _chunks == null) return;

            Vector3 camPos = _cam.transform.position;
            for (int i = 0; i < _chunks.Length; i++)
            {
                Vector3 c = _chunks[i].position;
                float dx = c.x - camPos.x, dz = c.z - camPos.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);

                int lod = dist < lod0Distance ? 0 : (dist < lod1Distance ? 1 : 2);
                if (lod == _chunkLod[i]) continue;
                _chunkLod[i] = lod;

                if (lod == 2)
                {
                    _chunkRenderers[i].enabled = false;
                }
                else
                {
                    _chunkRenderers[i].enabled = true;
                    // A chunk with its own cut mesh keeps it at BOTH detail levels.
                    //
                    // This is what defeated the first attempt at keeping grass off the arena's dirt:
                    // the override was assigned once at build and then overwritten by the very next
                    // LOD tick, which swapped in the shared mesh and put every culled blade back.
                    // The bare ground has to survive a distance change, so the swap has to know
                    // about it.
                    var cut = _chunkOverride != null ? _chunkOverride[i] : null;
                    if (cut == EmptyMesh)
                    {
                        // Entirely bare ground. Nothing to draw, so do not draw anything.
                        _chunkRenderers[i].enabled = false;
                        continue;
                    }
                    _chunkFilters[i].sharedMesh = cut != null ? cut
                                                : (lod == 0 ? _bladeMeshL0 : _bladeMeshL1);
                }
            }
        }

        void OnValidate()
        {
            if (Application.isPlaying) ApplyWind();
        }
    }
}
