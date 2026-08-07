using UnityEngine;
using UnityEngine.Rendering;

namespace DuckMow
{
    /// <summary>
    /// Who owns every square metre of the Bloom Rush arena.
    ///
    /// Held twice, in two representations written by the same calls so they cannot drift apart:
    ///
    ///   * a GPU render texture (<see cref="MaskRes"/>^2, RGBA32, POINT sampled) that the turf
    ///     shader reads to draw the ground, and
    ///   * a CPU byte grid (<see cref="GridRes"/>^2) which is what the score is actually counted
    ///     from, plus running per-owner totals so a percentage is a division rather than a scan.
    ///
    /// Nothing is ever read back off the GPU. WebGL has no AsyncGPUReadback and a blocking readback
    /// would stall the frame, so the CPU grid is not a cache of the texture — it is the record, and
    /// the texture is the picture of it. Both apply the identical rule to the identical swath: a
    /// cell changes hands when the swath covers at least half of it. That is why the map you can
    /// see and the percentages on the board agree.
    ///
    /// The three failure modes a territory mask has, and what stops each here:
    ///
    ///   * BLEEDING — a filtered tap between two owner codes decodes to a third competitor. Killed
    ///     by point sampling and by the stamp writing ownership through a hard clip; see
    ///     TurfStamp.shader.
    ///   * DOUBLE COUNTING — a cell credited to two owners at once, so the percentages sum past
    ///     100. Impossible by construction: a cell holds ONE byte, and the totals are adjusted by
    ///     the same code that overwrites it.
    ///   * FLICKER — two surfaces fighting over the same ground. There is only one ground mesh and
    ///     one mask; ownership is a colour inside a single opaque surface, never a decal on top of
    ///     one, so there is nothing to z-fight with.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class TurfMask : MonoBehaviour
    {
        static TurfMask _instance;

        /// <summary>
        /// Resolved lazily, because an editor domain reload clears statics without re-running
        /// Awake and the mode would come back up unable to claim anything, silently.
        /// </summary>
        public static TurfMask Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<TurfMask>();
                return _instance;
            }
            private set => _instance = value;
        }

        /// <summary>Metres covered by the mask, edge to edge. Comfortably past the barrier ring.</summary>
        public const float Size = 96f;
        public const float Half = Size * 0.5f;

        /// <summary>Texels across the render texture. 9.4 cm each.</summary>
        public const int MaskRes = 1024;
        /// <summary>
        /// Cells across the scoring grid. 18.8 cm each — deliberately within a texel of the picture,
        /// so no border on screen can be more than one cell out from the number beside it.
        /// </summary>
        public const int GridRes = 512;
        public const float MetresPerCell = Size / GridRes;

        /// <summary>Coarse sectors the bots read the map through. See <see cref="SectorOwned"/>.</summary>
        public const int SectorRes = 16;

        [Tooltip("Duck/TurfStamp")]
        public Shader stampShader;

        [Header("Feathering")]
        [Tooltip("Soft edge width in metres, for the bloom and steal channels. Ownership itself is " +
                 "always hard edged whatever this says.")]
        public float feather = 0.10f;

        [Header("Decay")]
        [Tooltip("Seconds for the claim wave to fade out of freshly planted ground.")]
        public float claimFade = 0.85f;
        [Tooltip("Seconds for the steal burn to cool. Deliberately shorter than the claim fade — " +
                 "a steal has to read as a flash that becomes a garden.")]
        public float stealFade = 0.32f;

        /// <summary>Cell value for ground nobody has taken.</summary>
        public const byte Neutral = 0;
        /// <summary>Cell value for ground that is not part of the arena at all.</summary>
        public const byte Blocked = 255;

        /// <summary>Owner per cell: 0 neutral, 1..4 competitor+1, 255 unplayable. Row-major.</summary>
        public byte[] Owner { get; private set; }

        /// <summary>Cells belonging to each competitor. Kept live; never rescanned.</summary>
        public int[] Owned { get; } = new int[TurfArena.Count];
        /// <summary>Cells that can be owned at all. The denominator of every percentage.</summary>
        public int PlayableCells { get; private set; }
        public int NeutralCells => PlayableCells - Owned[0] - Owned[1] - Owned[2] - Owned[3];

        /// <summary>True once the horn has gone and ownership is locked for the reveal.</summary>
        public bool Frozen { get; private set; }

        int[] _sector;          // SectorRes^2 * Count, cells owned per competitor per sector
        int[] _sectorTotal;     // playable cells per sector
        int[] _plaza;           // cells of the hub owned by each competitor
        int _plazaCells;

        RenderTexture _rt, _prev;
        Material _stampMat;
        Mesh _mesh, _quad;
        CommandBuffer _cb;
        Matrix4x4 _proj;

        readonly System.Collections.Generic.List<Vector3> _verts = new(512);
        readonly System.Collections.Generic.List<Vector4> _seg = new(512);
        readonly System.Collections.Generic.List<Vector4> _param = new(512);
        readonly System.Collections.Generic.List<int> _claimTris = new(768);
        bool _dirty;

        static readonly int IdPrev = Shader.PropertyToID("_TurfPrev");
        static readonly int IdDecay = Shader.PropertyToID("_TurfDecay");

        /// <summary>What one call to <see cref="Claim"/> actually did, in square metres.</summary>
        public struct Result
        {
            /// <summary>Ground taken off nobody.</summary>
            public float neutral;
            /// <summary>Ground taken off each competitor. Indexed by slot; the claimer's is always 0.</summary>
            public float from0, from1, from2, from3;

            public float Stolen => from0 + from1 + from2 + from3;
            public float Total => neutral + Stolen;

            public float From(int slot) => slot switch
            {
                0 => from0, 1 => from1, 2 => from2, _ => from3
            };

            /// <summary>Whoever lost the most to this swath, or -1 if it was all empty ground.</summary>
            public int BiggestVictim
            {
                get
                {
                    int best = -1; float bestArea = 1e-4f;
                    for (int i = 0; i < TurfArena.Count; i++)
                        if (From(i) > bestArea) { bestArea = From(i); best = i; }
                    return best;
                }
            }
        }

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            Instance = this;
            BuildGrid();

            if (stampShader == null) stampShader = Shader.Find("Duck/TurfStamp");
            _stampMat = new Material(stampShader) { hideFlags = HideFlags.HideAndDontSave };

            _rt = NewTarget("TurfMaskRT");
            _prev = NewTarget("TurfMaskRT (previous)");

            _mesh = new Mesh { name = "TurfStampMesh", indexFormat = IndexFormat.UInt16 };
            _mesh.MarkDynamic();
            _quad = BuildFullscreenQuad();
            _cb = new CommandBuffer { name = "TurfMask.Stamp" };

            _proj = GL.GetGPUProjectionMatrix(
                Matrix4x4.Ortho(-Half, Half, -Half, Half, -10f, 10f), true);

            PushConstants();
            ClearAll();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Release(ref _rt);
            Release(ref _prev);
            if (_mesh != null) Destroy(_mesh);
            if (_quad != null) Destroy(_quad);
            if (_stampMat != null) Destroy(_stampMat);
            _cb?.Release();
        }

        static void Release(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Destroy(rt);
            rt = null;
        }

        RenderTexture NewTarget(string name)
        {
            var rt = new RenderTexture(MaskRes, MaskRes, 0, RenderTextureFormat.ARGB32,
                                       RenderTextureReadWrite.Linear)
            {
                name = name,
                // Point, and it has to be. See the class comment: a filtered owner code is a
                // competitor who does not exist.
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1
            };
            rt.Create();
            return rt;
        }

        /// <summary>
        /// Precompute which cells are part of the arena, once.
        ///
        /// Asking <see cref="TurfArena.IsPlayable"/> per cell per frame would be sixteen trig calls
        /// on a quarter of a million cells. Asking it once at startup costs a few milliseconds on a
        /// loading screen and turns every later validity test into an array read.
        /// </summary>
        void BuildGrid()
        {
            Owner = new byte[GridRes * GridRes];
            _sector = new int[SectorRes * SectorRes * TurfArena.Count];
            _sectorTotal = new int[SectorRes * SectorRes];
            _plaza = new int[TurfArena.Count];

            PlayableCells = 0;
            _plazaCells = 0;

            for (int gz = 0; gz < GridRes; gz++)
            {
                float wz = CellCentre(gz);
                int row = gz * GridRes;
                for (int gx = 0; gx < GridRes; gx++)
                {
                    float wx = CellCentre(gx);
                    if (!TurfArena.IsPlayable(wx, wz))
                    {
                        Owner[row + gx] = Blocked;
                        continue;
                    }
                    Owner[row + gx] = Neutral;
                    PlayableCells++;
                    _sectorTotal[SectorIndex(gx, gz)]++;
                    if (wx * wx + wz * wz <= TurfArena.PlazaRadius * TurfArena.PlazaRadius)
                        _plazaCells++;
                }
            }
        }

        static float CellCentre(int g) => (g + 0.5f) * MetresPerCell - Half;
        static int SectorIndex(int gx, int gz)
            => (gz * SectorRes / GridRes) * SectorRes + (gx * SectorRes / GridRes);

        /// <summary>
        /// Push the arena's shape, palette and the live ownership texture to the shaders.
        ///
        /// The shape and palette half is shared with <see cref="TurfPalette"/>, which is what keeps
        /// the saved scene renderable outside play mode. The only thing this adds is the mask
        /// itself, which is the one constant that cannot exist until something has allocated it.
        /// </summary>
        public void PushConstants()
        {
            TurfPalette.Push();
            Shader.SetGlobalTexture(TurfPalette.IdMask, _rt);
            Shader.SetGlobalVector(TurfPalette.IdPulse, new Vector4(0f, -1f, 0f, 0f));
        }

        /// <summary>How hard the arena is glowing, 0..1, and who holds the hub. Set by the director.</summary>
        public void SetPulse(float urgency, int crownHolder, float crownPulse)
            => Shader.SetGlobalVector(TurfPalette.IdPulse,
                                      new Vector4(urgency, crownHolder, crownPulse, 0f));

        /// <summary>Wipe every claim. The retry path, and the start of a match.</summary>
        public void ClearAll()
        {
            Frozen = false;

            for (int i = 0; i < Owner.Length; i++)
                if (Owner[i] != Blocked) Owner[i] = Neutral;

            System.Array.Clear(Owned, 0, Owned.Length);
            System.Array.Clear(_sector, 0, _sector.Length);
            System.Array.Clear(_plaza, 0, _plaza.Length);

            var active = RenderTexture.active;
            RenderTexture.active = _rt; GL.Clear(false, true, Color.clear);
            RenderTexture.active = _prev; GL.Clear(false, true, Color.clear);
            RenderTexture.active = active;

            _verts.Clear(); _seg.Clear(); _param.Clear(); _claimTris.Clear();
            _dirty = false;
        }

        /// <summary>Lock ownership for the reveal. Later claims are ignored, not merely unscored.</summary>
        public void Freeze() => Frozen = true;

        // ------------------------------------------------------------------ claiming

        /// <summary>
        /// Claim the ground under a swath from <paramref name="from"/> to <paramref name="to"/>.
        ///
        /// The one entry point. Every competitor, player and bot alike, takes ground exactly and
        /// only through this, at the same width, under the same coverage rule — so "the NPCs paint
        /// faster" is not something anybody can suspect without also suspecting it of the player.
        ///
        /// Returns what the swath actually did, in square metres, split by who lost it. That split
        /// is what the whole reward layer is built on: a swath that took two square metres off
        /// MARGOT is a different event from one that took two square metres off nobody, and the
        /// game has to be able to tell before it can make the player feel it.
        /// </summary>
        public Result Claim(Vector3 from, Vector3 to, float width, int owner)
        {
            var result = new Result();
            if (Frozen || owner < 0 || owner >= TurfArena.Count) return result;

            float radius = width * 0.5f;
            byte code = (byte)(owner + 1);
            float cellArea = MetresPerCell * MetresPerCell;

            int stolen = 0;
            ForEachCell(from, to, radius, (idx, cov) =>
            {
                if (cov < 0.5f) return;                    // the same rule the shader clips at
                byte prev = Owner[idx];
                if (prev == Blocked || prev == code) return;

                if (prev != Neutral)
                {
                    int victim = prev - 1;
                    Owned[victim]--;
                    _sector[SectorSlot(idx, victim)]--;
                    if (InPlaza(idx)) _plaza[victim]--;
                    switch (victim)
                    {
                        case 0: result.from0 += cellArea; break;
                        case 1: result.from1 += cellArea; break;
                        case 2: result.from2 += cellArea; break;
                        default: result.from3 += cellArea; break;
                    }
                    stolen++;
                }
                else result.neutral += cellArea;

                Owner[idx] = code;
                Owned[owner]++;
                _sector[SectorSlot(idx, owner)]++;
                if (InPlaza(idx)) _plaza[owner]++;
            });

            // The steal heat handed to the shader is the FRACTION of this swath that was taken off
            // somebody, not a flag. Clipping the corner of an opponent's turf glows faintly; driving
            // the length of their best route lights the whole trail. The feedback is proportional to
            // the theft because the reward is.
            float steal = result.Total > 1e-4f ? Mathf.Clamp01(result.Stolen / result.Total) : 0f;
            AddQuad(from, to, radius, (owner + 1) / 8f, steal);

            return result;
        }

        int SectorSlot(int idx, int owner)
        {
            int gx = idx % GridRes, gz = idx / GridRes;
            return SectorIndex(gx, gz) * TurfArena.Count + owner;
        }

        bool InPlaza(int idx)
        {
            float wx = CellCentre(idx % GridRes), wz = CellCentre(idx / GridRes);
            return wx * wx + wz * wz <= TurfArena.PlazaRadius * TurfArena.PlazaRadius;
        }

        void AddQuad(Vector3 from, Vector3 to, float radius, float ownerCode, float steal)
        {
            Vector2 a = new(from.x, from.z);
            Vector2 b = new(to.x, to.z);

            Vector2 axis = b - a;
            float len = axis.magnitude;
            axis = len > 1e-4f ? axis / len : Vector2.right;
            Vector2 perp = new(-axis.y, axis.x);

            // Oversized by the radius at both ends; the fragment shader carves the real capsule out
            // of it, which is what gives round caps and no gaps however fast the mower is going.
            float pad = radius + feather + 0.05f;
            Vector2 a0 = a - axis * pad, b0 = b + axis * pad;

            int baseIndex = _verts.Count;
            _verts.Add(new Vector3(a0.x - perp.x * pad, a0.y - perp.y * pad, 0f));
            _verts.Add(new Vector3(a0.x + perp.x * pad, a0.y + perp.y * pad, 0f));
            _verts.Add(new Vector3(b0.x + perp.x * pad, b0.y + perp.y * pad, 0f));
            _verts.Add(new Vector3(b0.x - perp.x * pad, b0.y - perp.y * pad, 0f));

            var segData = new Vector4(a.x, a.y, b.x, b.y);
            var paramData = new Vector4(radius, ownerCode, steal, feather);
            for (int i = 0; i < 4; i++) { _seg.Add(segData); _param.Add(paramData); }

            _claimTris.Add(baseIndex + 0); _claimTris.Add(baseIndex + 1); _claimTris.Add(baseIndex + 2);
            _claimTris.Add(baseIndex + 0); _claimTris.Add(baseIndex + 2); _claimTris.Add(baseIndex + 3);
            _dirty = true;
        }

        void LateUpdate()
        {
            if (SimClock.Scripted) return;
            Flush(Time.deltaTime);
        }

        /// <summary>
        /// Decay last frame's effect channels and lay this frame's swaths on top.
        ///
        /// One full-screen copy and one mesh draw, every frame, into a target that is not the one
        /// being read. The copy is what lets the claim wave and the steal burn fade by themselves
        /// across the whole arena — including ground claimed by an opponent forty metres away and
        /// off camera — without any per-claim bookkeeping on the CPU.
        ///
        /// It cannot be done in place: a shader cannot sample the target it is writing to, and the
        /// version that tried produced a mask that smeared a texel per frame until, two minutes in,
        /// there were no borders in the arena at all.
        /// </summary>
        public void Flush(float dt)
        {
            if (_stampMat == null || _rt == null || _prev == null) return;

            // A recompile in play mode reloads the domain, restores the serialisable half of this
            // component and drops the rest — _dirty comes back true, _cb comes back null. Cannot
            // happen in a player; hides real exceptions during exactly the loop this is developed in.
            if (_cb == null) _cb = new CommandBuffer { name = "TurfMask.Stamp" };
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "TurfStampMesh", indexFormat = IndexFormat.UInt16 };
                _mesh.MarkDynamic();
            }
            if (_quad == null) _quad = BuildFullscreenQuad();

            (_rt, _prev) = (_prev, _rt);

            _cb.Clear();
            _cb.SetRenderTarget(_rt);
            _cb.SetViewProjectionMatrices(Matrix4x4.identity, _proj);

            _cb.SetGlobalTexture(IdPrev, _prev);
            _cb.SetGlobalVector(IdDecay, new Vector4(
                Mathf.Exp(-dt / Mathf.Max(claimFade, 0.01f)),
                Mathf.Exp(-dt / Mathf.Max(stealFade, 0.01f)), 0f, 0f));
            _cb.DrawMesh(_quad, Matrix4x4.identity, _stampMat, 0, 2);

            if (_dirty && _verts.Count > 0)
            {
                _mesh.Clear();
                _mesh.SetVertices(_verts);
                _mesh.SetUVs(0, _seg);
                _mesh.SetUVs(1, _param);
                _mesh.SetTriangles(_claimTris, 0, false);
                _mesh.bounds = new Bounds(Vector3.zero, new Vector3(Size, Size, 1f));

                _cb.DrawMesh(_mesh, Matrix4x4.identity, _stampMat, 0, 0);   // ownership, hard
                _cb.DrawMesh(_mesh, Matrix4x4.identity, _stampMat, 0, 1);   // the look, feathered
            }

            Graphics.ExecuteCommandBuffer(_cb);
            Shader.SetGlobalTexture(TurfPalette.IdMask, _rt);

            _verts.Clear(); _seg.Clear(); _param.Clear(); _claimTris.Clear();
            _dirty = false;
        }

        static Mesh BuildFullscreenQuad()
        {
            // Laid out in the same world-XZ space as the stamp quads and carrying the UVs the turf
            // shader would derive from those same positions, so the decay copy is exactly identity
            // on every platform without anybody having to reason about which way up render textures
            // are stored today.
            bool flip = SystemInfo.graphicsUVStartsAtTop;
            var m = new Mesh { name = "TurfDecayQuad" };
            m.SetVertices(new[]
            {
                new Vector3(-Half, -Half, 0f), new Vector3(-Half, Half, 0f),
                new Vector3( Half,  Half, 0f), new Vector3( Half, -Half, 0f)
            });
            float v0 = flip ? 1f : 0f, v1 = flip ? 0f : 1f;
            m.SetUVs(0, new[]
            {
                new Vector2(0f, v0), new Vector2(0f, v1),
                new Vector2(1f, v1), new Vector2(1f, v0)
            });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            m.bounds = new Bounds(Vector3.zero, new Vector3(Size, Size, 1f));
            return m;
        }

        /// <summary>Walk every grid cell touched by the capsule, passing index and coverage 0..1.</summary>
        void ForEachCell(Vector3 from, Vector3 to, float radius, System.Action<int, float> body)
        {
            Vector2 a = new(from.x, from.z);
            Vector2 b = new(to.x, to.z);
            Vector2 ba = b - a;
            float denom = Mathf.Max(Vector2.Dot(ba, ba), 1e-6f);

            float minX = Mathf.Min(a.x, b.x) - radius, maxX = Mathf.Max(a.x, b.x) + radius;
            float minZ = Mathf.Min(a.y, b.y) - radius, maxZ = Mathf.Max(a.y, b.y) + radius;

            int gx0 = Mathf.Max(0, Mathf.FloorToInt((minX + Half) / MetresPerCell));
            int gx1 = Mathf.Min(GridRes - 1, Mathf.CeilToInt((maxX + Half) / MetresPerCell));
            int gz0 = Mathf.Max(0, Mathf.FloorToInt((minZ + Half) / MetresPerCell));
            int gz1 = Mathf.Min(GridRes - 1, Mathf.CeilToInt((maxZ + Half) / MetresPerCell));

            float soft = MetresPerCell * 0.7071f;

            for (int gz = gz0; gz <= gz1; gz++)
            {
                float wz = CellCentre(gz);
                int row = gz * GridRes;
                for (int gx = gx0; gx <= gx1; gx++)
                {
                    float wx = CellCentre(gx);
                    Vector2 pa = new(wx - a.x, wz - a.y);
                    float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / denom);
                    float d = (pa - ba * h).magnitude;
                    if (d > radius + soft) continue;
                    float cov = soft > 1e-5f ? Mathf.Clamp01((radius + soft - d) / (soft * 2f)) : 1f;
                    body(row + gx, cov);
                }
            }
        }

        // ------------------------------------------------------------------ reading

        /// <summary>Share of the arena a competitor holds, 0..1.</summary>
        public float Share(int slot)
            => PlayableCells > 0 ? (float)Owned[slot] / PlayableCells : 0f;

        /// <summary>Share of the arena nobody has taken, 0..1.</summary>
        public float NeutralShare
            => PlayableCells > 0 ? (float)NeutralCells / PlayableCells : 0f;

        /// <summary>Share of the hub a competitor holds, 0..1. What the crown is decided on.</summary>
        public float PlazaShare(int slot)
            => _plazaCells > 0 ? (float)_plaza[slot] / _plazaCells : 0f;

        /// <summary>Who is winning. Ties break toward the lower slot, which the director spells out.</summary>
        public int Leader
        {
            get
            {
                int best = 0;
                for (int i = 1; i < TurfArena.Count; i++)
                    if (Owned[i] > Owned[best]) best = i;
                return best;
            }
        }

        /// <summary>Who owns the ground at a world position: -1 neutral, -2 unplayable, else the slot.</summary>
        public int OwnerAt(Vector3 world)
        {
            int gx = Mathf.FloorToInt((world.x + Half) / MetresPerCell);
            int gz = Mathf.FloorToInt((world.z + Half) / MetresPerCell);
            if (gx < 0 || gz < 0 || gx >= GridRes || gz >= GridRes) return -2;
            byte b = Owner[gz * GridRes + gx];
            if (b == Blocked) return -2;
            return b == Neutral ? -1 : b - 1;
        }

        // ---- the coarse map, which is all a bot is allowed to see -------------------------------

        /// <summary>Centre of a coarse sector in world space.</summary>
        public static Vector3 SectorCentre(int sx, int sz)
        {
            float step = Size / SectorRes;
            return new Vector3(sx * step - Half + step * 0.5f, 0f, sz * step - Half + step * 0.5f);
        }

        /// <summary>Playable cells in a sector. Zero means the sector is entirely hedge or outfield.</summary>
        public int SectorPlayable(int sx, int sz) => _sectorTotal[sz * SectorRes + sx];

        /// <summary>Cells of a sector owned by a competitor.</summary>
        public int SectorOwned(int sx, int sz, int slot)
            => _sector[(sz * SectorRes + sx) * TurfArena.Count + slot];

        /// <summary>
        /// Cells of a sector owned by anyone other than <paramref name="slot"/>. What a raider hunts
        /// for, and the reason bots do not need to see the fine grid to behave as though they can
        /// read the map.
        /// </summary>
        public int SectorEnemy(int sx, int sz, int slot)
        {
            int at = (sz * SectorRes + sx) * TurfArena.Count;
            int total = 0;
            for (int i = 0; i < TurfArena.Count; i++)
                if (i != slot) total += _sector[at + i];
            return total;
        }

        public int SectorNeutral(int sx, int sz)
        {
            int at = (sz * SectorRes + sx) * TurfArena.Count;
            int taken = 0;
            for (int i = 0; i < TurfArena.Count; i++) taken += _sector[at + i];
            return _sectorTotal[sz * SectorRes + sx] - taken;
        }

        public RenderTexture MaskTexture => _rt;
    }
}
