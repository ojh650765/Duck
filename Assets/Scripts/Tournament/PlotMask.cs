using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuckMow
{
    /// <summary>
    /// A rival plot's cut state: a small render texture the plot's grass samples, plus the CPU
    /// grid its station scores from.
    ///
    /// This is a deliberately reduced <see cref="CutMask"/>. The player's lawn needs 16 px/m
    /// because the camera sits two metres above it; a rival's lawn is judged from ninety metres
    /// away on the tour and glimpsed over a hedge during play, so 256² over 48 m — about 5 px/m —
    /// is indistinguishable and costs a sixteenth of the memory. There is no wheel-track pass for
    /// the same reason: nobody will ever be close enough to see one.
    /// </summary>
    public class PlotMask
    {
        public const int MaskRes = 256;
        public const int GridRes = 128;

        readonly Vector2 _centre;
        readonly float _size;
        readonly float _half;
        readonly float _metresPerCell;

        readonly RenderTexture _rt;
        readonly Material _stampMat;
        readonly Mesh _mesh;
        readonly CommandBuffer _cb;

        readonly List<Vector3> _verts = new List<Vector3>(256);
        readonly List<Vector4> _seg = new List<Vector4>(256);
        readonly List<Vector4> _param = new List<Vector4>(256);
        readonly List<int> _tris = new List<int>(384);
        bool _dirty;

        const byte CutThreshold = 128;
        const float Feather = 0.10f;

        public byte[] Cut { get; }
        public int CutCellCount { get; private set; }
        public RenderTexture Texture => _rt;
        public float CellArea => _metresPerCell * _metresPerCell;

        public PlotMask(Vector2 centre, float size, Shader stampShader)
        {
            _centre = centre;
            _size = size;
            _half = size * 0.5f;
            _metresPerCell = size / GridRes;

            Cut = new byte[GridRes * GridRes];

            _stampMat = new Material(stampShader != null ? stampShader : Shader.Find("Duck/CutStamp"))
            { hideFlags = HideFlags.HideAndDontSave };

            _rt = new RenderTexture(MaskRes, MaskRes, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = $"PlotMask_{centre.x:0}_{centre.y:0}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1
            };
            _rt.Create();

            _mesh = new Mesh { name = "PlotStampMesh", indexFormat = IndexFormat.UInt16 };
            _mesh.MarkDynamic();
            _cb = new CommandBuffer { name = "PlotMask.Stamp" };

            Clear();
        }

        public void Dispose()
        {
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
            if (_mesh != null) Object.Destroy(_mesh);
            if (_stampMat != null) Object.Destroy(_stampMat);
            _cb?.Release();
        }

        public void Clear()
        {
            var active = RenderTexture.active;
            RenderTexture.active = _rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = active;

            System.Array.Clear(Cut, 0, Cut.Length);
            CutCellCount = 0;
            _verts.Clear(); _seg.Clear(); _param.Clear(); _tris.Clear();
            _dirty = false;
        }

        /// <summary>Cut a swath in world space. Positions outside the plot are simply clipped.</summary>
        public void CutSwath(Vector3 from, Vector3 to, float width, float strength = 1f)
        {
            float radius = width * 0.5f;
            float ang = Mathf.Atan2(to.z - from.z, to.x - from.x);
            float dir01 = Mathf.Repeat(ang / (Mathf.PI * 2f), 1f);

            AddQuad(from, to, radius, dir01, strength);
            RasterizeCPU(from, to, radius, strength);
        }

        void AddQuad(Vector3 from, Vector3 to, float radius, float dir01, float strength)
        {
            // The stamp shader works in world XZ, and the plot's projection is centred on the plot,
            // so world coordinates go straight in.
            Vector2 a = new Vector2(from.x, from.z);
            Vector2 b = new Vector2(to.x, to.z);

            Vector2 axis = b - a;
            float len = axis.magnitude;
            axis = len > 1e-4f ? axis / len : Vector2.right;
            Vector2 perp = new Vector2(-axis.y, axis.x);

            float pad = radius + Feather + 0.02f;
            Vector2 a0 = a - axis * pad, b0 = b + axis * pad;

            int baseIndex = _verts.Count;
            _verts.Add(new Vector3(a0.x - perp.x * pad, a0.y - perp.y * pad, 0f));
            _verts.Add(new Vector3(a0.x + perp.x * pad, a0.y + perp.y * pad, 0f));
            _verts.Add(new Vector3(b0.x + perp.x * pad, b0.y + perp.y * pad, 0f));
            _verts.Add(new Vector3(b0.x - perp.x * pad, b0.y - perp.y * pad, 0f));

            var segData = new Vector4(a.x, a.y, b.x, b.y);
            var paramData = new Vector4(radius, dir01, strength, Feather);
            for (int i = 0; i < 4; i++) { _seg.Add(segData); _param.Add(paramData); }

            _tris.Add(baseIndex + 0); _tris.Add(baseIndex + 1); _tris.Add(baseIndex + 2);
            _tris.Add(baseIndex + 0); _tris.Add(baseIndex + 2); _tris.Add(baseIndex + 3);
            _dirty = true;
        }

        public void Flush()
        {
            if (!_dirty || _verts.Count == 0) return;

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _seg);
            _mesh.SetUVs(1, _param);
            _mesh.subMeshCount = 1;
            _mesh.SetTriangles(_tris, 0, false);
            _mesh.bounds = new Bounds(new Vector3(_centre.x, _centre.y, 0f),
                                      new Vector3(_size, _size, 1f));

            var proj = GL.GetGPUProjectionMatrix(
                Matrix4x4.Ortho(_centre.x - _half, _centre.x + _half,
                                _centre.y - _half, _centre.y + _half, -10f, 10f), true);

            _cb.Clear();
            _cb.SetRenderTarget(_rt);
            _cb.SetViewProjectionMatrices(Matrix4x4.identity, proj);
            _cb.DrawMesh(_mesh, Matrix4x4.identity, _stampMat, 0, 0);
            Graphics.ExecuteCommandBuffer(_cb);

            _verts.Clear(); _seg.Clear(); _param.Clear(); _tris.Clear();
            _dirty = false;
        }

        void RasterizeCPU(Vector3 from, Vector3 to, float radius, float strength)
        {
            Vector2 a = new Vector2(from.x - _centre.x, from.z - _centre.y);
            Vector2 b = new Vector2(to.x - _centre.x, to.z - _centre.y);
            Vector2 ba = b - a;
            float denom = Mathf.Max(Vector2.Dot(ba, ba), 1e-6f);

            float minX = Mathf.Min(a.x, b.x) - radius, maxX = Mathf.Max(a.x, b.x) + radius;
            float minZ = Mathf.Min(a.y, b.y) - radius, maxZ = Mathf.Max(a.y, b.y) + radius;

            int gx0 = Mathf.Max(0, Mathf.FloorToInt((minX + _half) / _size * GridRes));
            int gx1 = Mathf.Min(GridRes - 1, Mathf.CeilToInt((maxX + _half) / _size * GridRes));
            int gz0 = Mathf.Max(0, Mathf.FloorToInt((minZ + _half) / _size * GridRes));
            int gz1 = Mathf.Min(GridRes - 1, Mathf.CeilToInt((maxZ + _half) / _size * GridRes));

            float soft = _metresPerCell * 0.7071f;

            for (int gz = gz0; gz <= gz1; gz++)
            {
                float wz = (gz + 0.5f) / GridRes * _size - _half;
                int row = gz * GridRes;
                for (int gx = gx0; gx <= gx1; gx++)
                {
                    float wx = (gx + 0.5f) / GridRes * _size - _half;
                    Vector2 pa = new Vector2(wx - a.x, wz - a.y);
                    float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / denom);
                    float d = (pa - ba * h).magnitude;
                    if (d > radius + soft) continue;

                    float cov = soft > 1e-5f ? Mathf.Clamp01((radius + soft - d) / (soft * 2f)) : 1f;
                    int idx = row + gx;
                    byte prev = Cut[idx];
                    byte add = (byte)Mathf.Clamp(Mathf.RoundToInt(cov * strength * 255f), 0, 255);
                    if (add <= prev) continue;
                    if (prev < CutThreshold && add >= CutThreshold) CutCellCount++;
                    Cut[idx] = add;
                }
            }
        }
    }
}
