using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuckMow
{
    /// <summary>
    /// Owns the lawn's cut state, in two parallel representations:
    ///
    ///   * a GPU render texture (<see cref="Field.MaskRes"/>^2, RGBA32) that every lawn shader
    ///     samples to drive blade height, colour, stripes and wheel tracks, and
    ///   * a CPU byte grid (<see cref="Field.GridRes"/>^2) used for scoring and for asking
    ///     "is there uncut grass under the blade right now?".
    ///
    /// Both are written by the same swath calls, so they never disagree. Nothing is ever read
    /// back from the GPU — WebGL has no AsyncGPUReadback and a blocking readback would stall.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class CutMask : MonoBehaviour
    {
        static CutMask _instance;

        /// <summary>
        /// Resolved lazily. An editor domain reload clears statics without re-running Awake,
        /// which used to leave the mower silently unable to cut anything.
        /// </summary>
        public static CutMask Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<CutMask>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Tooltip("Duck/CutStamp")]
        public Shader stampShader;

        [Header("Feathering")]
        [Tooltip("Soft edge width in metres. Roughly one blade width reads best.")]
        public float feather = 0.06f;

        RenderTexture _rt;
        Material _stampMat;
        Mesh _mesh;
        CommandBuffer _cb;

        // Per-frame quad accumulation. Index 0 = cut pass, 1 = track pass.
        readonly List<Vector3> _verts = new List<Vector3>(512);
        readonly List<Vector4> _seg = new List<Vector4>(512);
        readonly List<Vector4> _param = new List<Vector4>(512);
        readonly List<int> _cutTris = new List<int>(768);
        readonly List<int> _trackTris = new List<int>(768);
        bool _dirty;

        /// <summary>Cut amount per cell, 0..255. Row-major, index = gz * GridRes + gx.</summary>
        public byte[] Cut { get; private set; }

        /// <summary>Number of cells that are at least half cut. Cheap live coverage read.</summary>
        public int CutCellCount { get; private set; }

        const byte CutThreshold = 128;
        static readonly int IdCutMaskFlipV = Shader.PropertyToID("_CutMaskFlipV");

        void Awake()
        {
            Instance = this;
            Cut = new byte[Field.GridRes * Field.GridRes];

            if (stampShader == null) stampShader = Shader.Find("Duck/CutStamp");
            _stampMat = new Material(stampShader) { hideFlags = HideFlags.HideAndDontSave };

            _rt = new RenderTexture(Field.MaskRes, Field.MaskRes, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "CutMaskRT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1
            };
            _rt.Create();

            _mesh = new Mesh { name = "CutStampMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
            _mesh.MarkDynamic();
            _mesh.subMeshCount = 2;

            _cb = new CommandBuffer { name = "CutMask.Stamp" };

            // Tell every lawn shader which way up this platform stores render textures.
            Shader.SetGlobalFloat(IdCutMaskFlipV, SystemInfo.graphicsUVStartsAtTop ? 1f : 0f);
            Shader.SetGlobalFloat(Field.IdFieldSize, Field.Size);
            Shader.SetGlobalFloat(Field.IdFieldHalf, Field.Half);
            Shader.SetGlobalTexture(Field.IdCutMask, _rt);

            ClearAll();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            if (_mesh != null) Destroy(_mesh);
            if (_stampMat != null) Destroy(_stampMat);
            _cb?.Release();
        }

        /// <summary>Wipe the lawn back to fully uncut. Used by the instant retry path.</summary>
        public void ClearAll()
        {
            var active = RenderTexture.active;
            RenderTexture.active = _rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = active;

            System.Array.Clear(Cut, 0, Cut.Length);
            CutCellCount = 0;
            _verts.Clear(); _seg.Clear(); _param.Clear();
            _cutTris.Clear(); _trackTris.Clear();
            _dirty = false;
        }

        // ------------------------------------------------------------------ painting

        /// <summary>
        /// Cut a swath from <paramref name="from"/> to <paramref name="to"/>.
        /// Returns the amount of previously-uncut grass that this swath removed, expressed as
        /// a 0..1 fraction of the swath's own area — the number that drives clipping particles,
        /// the shredding audio layer and boost refill.
        /// </summary>
        public float CutSwath(Vector3 from, Vector3 to, float width, float strength = 1f)
        {
            float radius = width * 0.5f;
            float freshness = MeasureUncut(from, to, radius);

            float ang = Mathf.Atan2(to.z - from.z, to.x - from.x);      // -pi..pi
            float dir01 = Mathf.Repeat(ang / (Mathf.PI * 2f), 1f);       // full 360 so opposite passes differ

            AddQuad(from, to, radius, dir01, strength, _cutTris);
            RasterizeCPU(from, to, radius, strength);
            return freshness;
        }

        /// <summary>Press a wheel track. Does not cut.</summary>
        public void PressTrack(Vector3 from, Vector3 to, float width, float strength = 0.8f)
        {
            AddQuad(from, to, width * 0.5f, 0f, strength, _trackTris);
        }

        void AddQuad(Vector3 from, Vector3 to, float radius, float dir01, float strength, List<int> tris)
        {
            Vector2 a = new Vector2(from.x, from.z);
            Vector2 b = new Vector2(to.x, to.z);

            Vector2 axis = b - a;
            float len = axis.magnitude;
            axis = len > 1e-4f ? axis / len : Vector2.right;
            Vector2 perp = new Vector2(-axis.y, axis.x);

            // Oversize by the radius at both ends; the fragment shader carves the real capsule.
            float pad = radius + feather + 0.02f;
            Vector2 a0 = a - axis * pad, b0 = b + axis * pad;

            int baseIndex = _verts.Count;
            _verts.Add(new Vector3(a0.x - perp.x * pad, a0.y - perp.y * pad, 0f));
            _verts.Add(new Vector3(a0.x + perp.x * pad, a0.y + perp.y * pad, 0f));
            _verts.Add(new Vector3(b0.x + perp.x * pad, b0.y + perp.y * pad, 0f));
            _verts.Add(new Vector3(b0.x - perp.x * pad, b0.y - perp.y * pad, 0f));

            var segData = new Vector4(a.x, a.y, b.x, b.y);
            var paramData = new Vector4(radius, dir01, strength, feather);
            for (int i = 0; i < 4; i++) { _seg.Add(segData); _param.Add(paramData); }

            tris.Add(baseIndex + 0); tris.Add(baseIndex + 1); tris.Add(baseIndex + 2);
            tris.Add(baseIndex + 0); tris.Add(baseIndex + 2); tris.Add(baseIndex + 3);
            _dirty = true;
        }

        void LateUpdate()
        {
            if (SimClock.Scripted) return;
            Flush();
        }

        /// <summary>Uploads this frame's accumulated swaths to the mask render texture.</summary>
        public void Flush()
        {
            if (!_dirty || _verts.Count == 0) return;

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _seg);
            _mesh.SetUVs(1, _param);
            _mesh.subMeshCount = 2;
            _mesh.SetTriangles(_cutTris, 0, false);
            _mesh.SetTriangles(_trackTris, 1, false);
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(Field.Size, Field.Size, 1f));

            var proj = GL.GetGPUProjectionMatrix(
                Matrix4x4.Ortho(-Field.Half, Field.Half, -Field.Half, Field.Half, -10f, 10f), true);

            _cb.Clear();
            _cb.SetRenderTarget(_rt);
            _cb.SetViewProjectionMatrices(Matrix4x4.identity, proj);
            if (_cutTris.Count > 0) _cb.DrawMesh(_mesh, Matrix4x4.identity, _stampMat, 0, 0);
            if (_trackTris.Count > 0) _cb.DrawMesh(_mesh, Matrix4x4.identity, _stampMat, 1, 1);
            Graphics.ExecuteCommandBuffer(_cb);

            _verts.Clear(); _seg.Clear(); _param.Clear();
            _cutTris.Clear(); _trackTris.Clear();
            _dirty = false;
        }

        // ------------------------------------------------------------------ CPU grid

        /// <summary>
        /// How much of this swath is currently uncut, 0..1. Sampled before the swath is applied
        /// so it answers "how much work did that pass actually do".
        /// </summary>
        float MeasureUncut(Vector3 from, Vector3 to, float radius)
        {
            int total = 0, uncut = 0;
            ForEachCell(from, to, radius, (idx, cov) =>
            {
                total++;
                if (Cut[idx] < CutThreshold) uncut++;
            });
            return total > 0 ? (float)uncut / total : 0f;
        }

        void RasterizeCPU(Vector3 from, Vector3 to, float radius, float strength)
        {
            ForEachCell(from, to, radius, (idx, cov) =>
            {
                byte prev = Cut[idx];
                byte add = (byte)Mathf.Clamp(Mathf.RoundToInt(cov * strength * 255f), 0, 255);
                if (add <= prev) return;
                if (prev < CutThreshold && add >= CutThreshold) CutCellCount++;
                Cut[idx] = add;
            });
        }

        /// <summary>Walk every grid cell touched by the capsule, passing index and coverage 0..1.</summary>
        void ForEachCell(Vector3 from, Vector3 to, float radius, System.Action<int, float> body)
        {
            Vector2 a = new Vector2(from.x, from.z);
            Vector2 b = new Vector2(to.x, to.z);
            Vector2 ba = b - a;
            float denom = Mathf.Max(Vector2.Dot(ba, ba), 1e-6f);

            float minX = Mathf.Min(a.x, b.x) - radius, maxX = Mathf.Max(a.x, b.x) + radius;
            float minZ = Mathf.Min(a.y, b.y) - radius, maxZ = Mathf.Max(a.y, b.y) + radius;

            int gx0 = Mathf.Max(0, Mathf.FloorToInt((minX + Field.Half) / Field.Size * Field.GridRes));
            int gx1 = Mathf.Min(Field.GridRes - 1, Mathf.CeilToInt((maxX + Field.Half) / Field.Size * Field.GridRes));
            int gz0 = Mathf.Max(0, Mathf.FloorToInt((minZ + Field.Half) / Field.Size * Field.GridRes));
            int gz1 = Mathf.Min(Field.GridRes - 1, Mathf.CeilToInt((maxZ + Field.Half) / Field.Size * Field.GridRes));

            // Half a cell diagonal of softness so cell coverage approximates the shader's feather.
            float soft = Field.MetresPerCell * 0.7071f;

            for (int gz = gz0; gz <= gz1; gz++)
            {
                float wz = (gz + 0.5f) / Field.GridRes * Field.Size - Field.Half;
                int row = gz * Field.GridRes;
                for (int gx = gx0; gx <= gx1; gx++)
                {
                    float wx = (gx + 0.5f) / Field.GridRes * Field.Size - Field.Half;
                    Vector2 pa = new Vector2(wx - a.x, wz - a.y);
                    float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / denom);
                    float d = (pa - ba * h).magnitude;
                    if (d > radius + soft) continue;
                    float cov = soft > 1e-5f ? Mathf.Clamp01((radius + soft - d) / (soft * 2f)) : 1f;
                    body(row + gx, cov);
                }
            }
        }

        /// <summary>Cut amount 0..1 at a world position, from the CPU grid.</summary>
        public float SampleCut(Vector3 world)
        {
            int gx = Mathf.FloorToInt((world.x + Field.Half) / Field.Size * Field.GridRes);
            int gz = Mathf.FloorToInt((world.z + Field.Half) / Field.Size * Field.GridRes);
            if (gx < 0 || gz < 0 || gx >= Field.GridRes || gz >= Field.GridRes) return 0f;
            return Cut[gz * Field.GridRes + gx] / 255f;
        }

        public RenderTexture MaskTexture => _rt;
    }
}
