using UnityEngine;
using UnityEngine.Rendering;

namespace DuckMow
{
    /// <summary>
    /// The blade layer for the plots the player does not own.
    ///
    /// The rival lawns were a flat quad with the ground shader on it and nothing standing up, so
    /// the moment the tour camera dropped over a neighbour the venue changed material: the player's
    /// plot has grass, theirs had paint. This puts the same blades on all three, and it does it by
    /// baking <see cref="GrassField.BakeBladeMesh"/> — the player's own patch — rather than by
    /// growing a second grass system that would drift away from it.
    ///
    /// One component for the whole venue rather than one per plot. All three plots share a single
    /// baked mesh pair (a chunk is a chunk wherever it stands), a single LOD tick and a single
    /// place where the field parameters are bound, which is the part that is easy to get wrong.
    ///
    /// Cost is self-limiting and deliberately not tuned separately: every distance here is copied
    /// from the player's GrassField at build time, so a rival chunk appears, thins, swaps LOD and
    /// is culled at exactly the ranges the player's chunks do. That is what makes the two lawns
    /// match, and it also means the tour — the only shot that gets close to a rival — pays for one
    /// plot's worth of grass while the player's field is entirely out of range and drawing nothing.
    /// From the chase camera the rival lawns are 40 m past the player's fence, past the fade, so
    /// gameplay pays almost nothing.
    /// </summary>
    [DefaultExecutionOrder(-39)]
    public class RivalBlades : MonoBehaviour
    {
        /// <summary>One plot's grass: where it is and who mows it.</summary>
        [System.Serializable]
        public struct Plot
        {
            public string contestant;
            public Vector2 centre;
            public float size;
            [Tooltip("The contestant who owns this plot. Their mask is what the blades read.")]
            public RivalContestant owner;
        }

        public Plot[] plots = new Plot[0];
        public Material bladeMaterial;

        [Header("Copied from the player's GrassField when the venue is built")]
        [Tooltip("Must be the player's chunk size: the baked patch is that wide, and a plot laid " +
                 "out in a different stride would leave bare strips or overhang the apron.")]
        public float chunkSize = 8f;
        public float density = 105f;
        public float bladeHeight = 0.30f;
        public float bladeWidth = 0.042f;
        public float bladeCurve = 0.085f;
        public float lod0Distance = 34f;
        public float lod1Distance = 48f;
        public float lodInterval = 0.15f;

        [Header("Plot surface")]
        [Tooltip("Height of a rival lawn quad. Roots sit on it rather than at y=0 so they neither " +
                 "float above the surface nor sink under it.")]
        public float rootHeight = 0.005f;

        // RivalLawn's ground palette is deliberately left alone. It lifts these plots' base colours
        // toward the tip colours to stand in for the blade layer they used to lack, which now double
        // counts — but only where the blades are thin enough to see the soil through, and out there
        // the lift is still doing the job it was tuned for. It is also what keeps the mown picture
        // readable in the 186 m study beat, which no blade layer reaches.

        Mesh _meshL0, _meshL1;
        Transform[] _chunks;
        MeshFilter[] _filters;
        MeshRenderer[] _renderers;
        int[] _chunkPlot;
        int[] _chunkLod;
        // One block per plot: every chunk on a plot binds the same mask, extent and origin, and
        // SetPropertyBlock copies what it is given, so one block can serve all of them.
        MaterialPropertyBlock[] _blocks;
        bool[] _bound;
        int _unbound;
        float _lodTimer;
        Camera _cam;

        static readonly int IdFieldOrigin = Shader.PropertyToID("_FieldOrigin");

        void Awake()
        {
            if (bladeMaterial == null || plots.Length == 0) return;

            _meshL0 = GrassField.BakeBladeMesh("RivalBladesL0", chunkSize, density, GrassField.BladeSeed,
                                               1f, bladeHeight, bladeWidth, bladeCurve);
            _meshL1 = GrassField.BakeBladeMesh("RivalBladesL1", chunkSize, density, GrassField.BladeSeed,
                                               GrassField.BladeIdCutoff, bladeHeight, bladeWidth, bladeCurve);

            BuildChunks();
        }

        void OnDestroy()
        {
            if (_meshL0) DestroyImmediate(_meshL0);
            if (_meshL1) DestroyImmediate(_meshL1);
        }

        // ------------------------------------------------------------------ build

        void BuildChunks()
        {
            var chunks = new System.Collections.Generic.List<Transform>();
            var filters = new System.Collections.Generic.List<MeshFilter>();
            var renderers = new System.Collections.Generic.List<MeshRenderer>();
            var owners = new System.Collections.Generic.List<int>();

            _blocks = new MaterialPropertyBlock[plots.Length];
            _bound = new bool[plots.Length];
            _unbound = plots.Length;

            for (int p = 0; p < plots.Length; p++)
            {
                var plot = plots[p];

                // The field parameters go through a property block, NEVER through a material.
                //
                // _FieldSize, _FieldHalf and _FieldOrigin are declared in GrassCommon.hlsl outside
                // CBUFFER_START(UnityPerMaterial), which makes them global uniforms; with the SRP
                // Batcher on, a per-material write to one of them is silently dropped. RivalLawn
                // found this the hard way — its ground quads sampled their own masks through the
                // PLAYER's origin and extent, and the mown lines slid about as the tour flew past.
                // Blades read the same mask in their vertex shader, so they would fail the same way,
                // except worse: the height of every blade on the plot would be driven by whatever
                // the player had just mown.
                var block = new MaterialPropertyBlock();
                block.SetFloat(Field.IdFieldSize, plot.size);
                block.SetFloat(Field.IdFieldHalf, plot.size * 0.5f);
                block.SetVector(IdFieldOrigin, new Vector4(plot.centre.x, plot.centre.y, 0f, 0f));
                // No mask until the round starts. White would read as fully cut and the plot would
                // open with every blade already crushed flat.
                block.SetTexture(Field.IdCutMask, Texture2D.blackTexture);
                _blocks[p] = block;

                int perSide = Mathf.Max(1, Mathf.RoundToInt(plot.size / Mathf.Max(chunkSize, 0.01f)));
                if (Mathf.Abs(perSide * chunkSize - plot.size) > 0.01f)
                    Debug.LogWarning($"[Duck] Plot {plot.contestant} is {plot.size} m, which is not a " +
                                     $"whole number of {chunkSize} m grass chunks; its blade layer " +
                                     "will not line up with its lawn.");

                var root = new GameObject($"Blades_{plot.contestant}").transform;
                root.SetParent(transform, false);

                for (int z = 0; z < perSide; z++)
                {
                    for (int x = 0; x < perSide; x++)
                    {
                        var go = new GameObject($"Chunk_{x}_{z}");
                        go.transform.SetParent(root, false);
                        go.transform.position = new Vector3(
                            plot.centre.x + (x + 0.5f - perSide * 0.5f) * chunkSize, rootHeight,
                            plot.centre.y + (z + 0.5f - perSide * 0.5f) * chunkSize);

                        var mf = go.AddComponent<MeshFilter>();
                        mf.sharedMesh = _meshL0;
                        var mr = go.AddComponent<MeshRenderer>();
                        mr.sharedMaterial = bladeMaterial;
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        mr.receiveShadows = true;
                        mr.lightProbeUsage = LightProbeUsage.Off;
                        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                        mr.SetPropertyBlock(block);

                        chunks.Add(go.transform);
                        filters.Add(mf);
                        renderers.Add(mr);
                        owners.Add(p);
                    }
                }
            }

            _chunks = chunks.ToArray();
            _filters = filters.ToArray();
            _renderers = renderers.ToArray();
            _chunkPlot = owners.ToArray();
            _chunkLod = new int[_chunks.Length];
            for (int i = 0; i < _chunkLod.Length; i++) _chunkLod[i] = -1;
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            if (SimClock.Scripted) return;
            Tick(Time.deltaTime);
        }

        /// <summary>Public so a scripted pass can drive the venue's grass the way it drives the player's.</summary>
        public void Tick(float dt)
        {
            BindMasks();

            _lodTimer -= dt;
            if (_lodTimer > 0f) return;
            _lodTimer = lodInterval;
            TickLod();
        }

        /// <summary>
        /// Hand each plot its own cut mask once its contestant has one.
        ///
        /// Polled rather than pushed: RivalContestant.Begin binds the lawn quad and the chalk it
        /// holds references to, and it holds none to this. A plot's mask is created once and reused
        /// for every round, so this stops looking at a plot the moment it is bound and does nothing
        /// at all after that.
        /// </summary>
        void BindMasks()
        {
            if (_unbound == 0 || _blocks == null) return;

            for (int p = 0; p < plots.Length; p++)
            {
                if (_bound[p]) continue;
                var mask = plots[p].owner != null ? plots[p].owner.Mask : null;
                if (mask == null || mask.Texture == null) continue;

                _blocks[p].SetTexture(Field.IdCutMask, mask.Texture);
                for (int i = 0; i < _renderers.Length; i++)
                    if (_chunkPlot[i] == p) _renderers[i].SetPropertyBlock(_blocks[p]);

                _bound[p] = true;
                _unbound--;
            }
        }

        /// <summary>
        /// The same LOD rule the player's field runs, on the same distances, measured the same way.
        ///
        /// XZ only, so camera height does not count. That is what keeps the blades alive under the
        /// tour and the overhead reveal: those cameras are 60 m up but only a few metres out, and a
        /// true 3D distance would cull the whole venue's grass in exactly the shots this layer was
        /// added for. The blade shader's fade uses the same flat distance, so the two agree.
        /// </summary>
        void TickLod()
        {
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
                    _renderers[i].enabled = false;
                }
                else
                {
                    _renderers[i].enabled = true;
                    _filters[i].sharedMesh = lod == 0 ? _meshL0 : _meshL1;
                }
            }
        }
    }
}
