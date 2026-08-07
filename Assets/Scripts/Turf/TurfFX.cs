using System.Collections.Generic;
using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Everything Bloom Rush throws in the air, in one pooled place.
    ///
    /// The ground shader is already carrying most of the feedback — the bloom growing in, the claim
    /// wave, the steal burning off — and that is deliberate, because it is per-texel, it works for
    /// all four competitors at once and it costs one extra channel rather than an effect per event.
    /// What is left for this layer is the part the ground cannot say: HEIGHT, and DIRECTION.
    ///
    /// A claim that only happens on the floor reads as the floor changing colour. The same claim
    /// with a low ring leaving the roller, a spray of soil thrown up behind it and petals lifting
    /// off it reads as a machine doing something to the ground. That is the entire brief here.
    ///
    /// Everything is pooled, opaque-cheap and additive, on shared meshes with the colour riding on a
    /// property block — same discipline as <see cref="DefenceImpactFX"/>, whose debris and skids this
    /// borrows outright rather than reimplementing. Nothing here allocates after Awake, nothing here
    /// casts a shadow, and nothing here survives being switched off.
    /// </summary>
    [DefaultExecutionOrder(-15)]
    public class TurfFX : MonoBehaviour
    {
        public static TurfFX Instance { get; private set; }

        [Header("Wiring")]
        [Tooltip("Pooled soil, skids and shards. Made here if left empty.")]
        public DefenceImpactFX debris;
        public Shader glowShader;

        [Header("Budget")]
        [Tooltip("Most pieces alive at once. Past this the oldest is recycled, so a four-way pile-up " +
                 "in a choke cannot cost more than a quiet lap of the loop.")]
        public int poolSize = 72;

        [Header("Claiming")]
        [Tooltip("Square metres of claim between one ring leaving the roller and the next.")]
        public float ringEveryMetres = 9f;
        [Tooltip("Radius a claim ring grows to.")]
        public float claimRingRadius = 2.4f;
        [Tooltip("Seconds a claim ring lives.")]
        public float claimRingLife = 0.55f;

        [Header("Stealing")]
        [Tooltip("Radius a steal burst grows to, per square metre taken, up to the cap below.")]
        public float stealRingPerMetre = 0.24f;
        public float stealRingMax = 11f;
        public float stealRingLife = 0.95f;
        [Tooltip("Petals thrown per square metre stolen.")]
        public float petalsPerMetre = 0.5f;
        public int petalsMax = 14;

        readonly List<Piece> _pool = new();
        readonly float[] _claimCredit = new float[TurfArena.Count];
        Material _glow;
        Mesh _ringMesh, _petalMesh, _shaftMesh;
        MaterialPropertyBlock _mpb;
        System.Random _rng;
        Transform _bin;
        int _next;

        static readonly int IdColor = Shader.PropertyToID("_Color");
        static readonly int IdFade = Shader.PropertyToID("_Fade");

        enum Kind { Ring, Petal, Shaft }

        class Piece
        {
            public Transform tr;
            public MeshRenderer mr;
            /// <summary>Cached, because a piece changes mesh every time it is taken out of the pool
            /// and GetComponent on the spawn path is the one place in this file it would be hot.</summary>
            public MeshFilter mf;
            public Kind kind;
            public float age, life;
            public float startRadius, endRadius;
            public Vector3 velocity, spin;
            public Color colour;
            public bool live;
        }

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            Instance = this;
            _rng = new System.Random(60451);
            _mpb = new MaterialPropertyBlock();

            if (glowShader == null) glowShader = Shader.Find("Duck/TurfGlow");
            _glow = new Material(glowShader) { hideFlags = HideFlags.HideAndDontSave };

            _ringMesh = BuildRing(48);
            _petalMesh = BuildPetal();
            _shaftMesh = BuildShaft();

            if (debris == null)
            {
                var go = new GameObject("~ Bloom debris");
                go.transform.SetParent(transform, false);
                debris = go.AddComponent<DefenceImpactFX>();
            }
            debris.Setup(_rng, 70f);

            _bin = new GameObject("~ Bloom FX").transform;
            _bin.SetParent(transform, false);
            for (int i = 0; i < poolSize; i++) _pool.Add(NewPiece());
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_glow != null) Destroy(_glow);
            if (_ringMesh != null) Destroy(_ringMesh);
            if (_petalMesh != null) Destroy(_petalMesh);
            if (_shaftMesh != null) Destroy(_shaftMesh);
        }

        Piece NewPiece()
        {
            var go = new GameObject("fx");
            go.transform.SetParent(_bin, false);
            go.SetActive(false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _ringMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _glow;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return new Piece { tr = go.transform, mr = mr, mf = mf };
        }

        Piece Take()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                var p = _pool[(_next + i) % _pool.Count];
                if (p.live) continue;
                _next = (_next + i + 1) % _pool.Count;
                return p;
            }
            // Full. Recycle the oldest rather than growing, so a pile-up has a ceiling.
            Piece oldest = _pool[0];
            foreach (var p in _pool) if (p.age > oldest.age) oldest = p;
            return oldest;
        }

        public void Clear()
        {
            foreach (var p in _pool) { p.live = false; p.tr.gameObject.SetActive(false); }
            debris?.Clear();
            System.Array.Clear(_claimCredit, 0, _claimCredit.Length);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            debris?.Tick(dt);

            foreach (var p in _pool)
            {
                if (!p.live) continue;
                p.age += dt;
                if (p.age >= p.life)
                {
                    p.live = false;
                    p.tr.gameObject.SetActive(false);
                    continue;
                }

                float t = p.age / p.life;
                float fade;

                switch (p.kind)
                {
                    case Kind.Ring:
                        // Expand and thin. Eased out so the ring leaves the roller quickly and then
                        // holds, which is what makes it read as a wave rather than as a growing circle.
                        float r = Mathf.Lerp(p.startRadius, p.endRadius, 1f - (1f - t) * (1f - t));
                        p.tr.localScale = new Vector3(r, 1f, r);
                        fade = (1f - t) * (1f - t);
                        break;

                    case Kind.Petal:
                        p.velocity += Vector3.down * (5.4f * dt);
                        p.tr.position += p.velocity * dt;
                        p.tr.Rotate(p.spin * dt, Space.Self);
                        // Petals settle rather than vanish: they slow as they near the ground so the
                        // last third of their life is a drift, which is the part that looks alive.
                        if (p.tr.position.y < 0.06f)
                        {
                            p.velocity.y = 0f;
                            p.velocity *= 1f - Mathf.Min(1f, 4f * dt);
                        }
                        fade = Mathf.Min(1f, (1f - t) * 2.4f);
                        break;

                    default:
                        p.tr.localScale = new Vector3(p.startRadius,
                                                      Mathf.Lerp(p.endRadius, p.endRadius * 0.2f, t),
                                                      p.startRadius);
                        fade = (1f - t) * (1f - t) * (1f - t);
                        break;
                }

                p.mr.GetPropertyBlock(_mpb);
                _mpb.SetColor(IdColor, p.colour);
                _mpb.SetFloat(IdFade, fade);
                p.mr.SetPropertyBlock(_mpb);
            }
        }

        // ------------------------------------------------------------------ the events

        /// <summary>
        /// Ground taken. Called for every steal, however small — the size decides what happens.
        ///
        /// Small nibbles get soil and nothing else, because a full burst on every clipped corner
        /// would leave the arena permanently strobing and the real steals would have nothing left to
        /// be. Past the threshold the effect scales with the theft: a ring in the thief's colour, a
        /// counter-ring in the victim's so the player can see WHOSE ground just changed hands, a
        /// shaft of light at the point of contact, and petals in the victim's colour thrown up as
        /// their planting is turned over.
        /// </summary>
        public void Steal(TurfCompetitor thief, TurfCompetitor victim, float squareMetres, Vector3 where)
        {
            where.y = 0.06f;
            Color thiefCol = thief != null ? thief.Livery : Color.white;
            Color victimCol = victim != null ? victim.Livery : Color.grey;

            debris?.Dirt(where, Mathf.Clamp01(squareMetres / 18f) * 0.8f + 0.2f);

            // Soil for a nibble, petals for a real bite, and a ring only for a theft big enough to
            // be worth interrupting the screen for.
            //
            // The threshold used to be four square metres, which a mower crosses in a fifth of a
            // second — so "a steal" fired constantly and the arena was never not ringing. Taking a
            // corner off somebody is not an event; driving the length of their best route is.
            if (squareMetres < 4f) return;

            int petals = Mathf.Min(petalsMax, Mathf.RoundToInt(squareMetres * petalsPerMetre));
            for (int i = 0; i < petals; i++) Petal(where, victimCol, 1f);

            if (squareMetres < 16f) return;

            // ONE ring, in the thief's colour. The second ring in the victim's colour was there to
            // say whose ground it had been — but the ground itself has just changed species and
            // colour underfoot, which says it better and does not put two concentric circles on the
            // floor for every exchange.
            float radius = Mathf.Min(stealRingMax, squareMetres * stealRingPerMetre + 2f);
            Ring(where, radius, stealRingLife, thiefCol * 1.5f, 0.6f);
            Shaft(where, Mathf.Clamp(squareMetres * 0.16f, 1.4f, 6f), thiefCol);
        }

        /// <summary>
        /// Ground claimed, of any kind. Metered rather than fired per call.
        ///
        /// A roller claims something every fixed step, so an effect per claim would be fifty a
        /// second per machine. Credit accumulates in square metres instead and something leaves the
        /// roller each time enough has been taken — so the rhythm spaces itself by AREA, and a mower
        /// at full boost across empty ground throws more than one nosing through a choke.
        ///
        /// What it throws is PETALS, and no longer a ring on the floor.
        ///
        /// The expanding ring was written before the planting layer existed, when the ground was
        /// the only thing that could show a claim and it needed help. It does not need help now:
        /// Duck/TurfBlades grows real grass in the owner's colour behind the roller, rising over the
        /// second after it passes. With four machines painting continuously, a ring every nine
        /// square metres on top of that is four permanent trails of expanding circles across the
        /// floor — and the honest description of it is clutter. The claim is already legible; the
        /// rings were decorating something that was already working.
        /// </summary>
        public void Claim(TurfCompetitor who, float squareMetres, Vector3 where)
        {
            if (who == null) return;
            int slot = Mathf.Clamp(who.slot, 0, TurfArena.Count - 1);
            _claimCredit[slot] += squareMetres;
            if (_claimCredit[slot] < ringEveryMetres) return;
            _claimCredit[slot] = 0f;

            where.y = 0.05f;
            // One petal, lifting off the new planting. It reads as garden debris rather than as a
            // piece of interface, which is the whole difference.
            Petal(where, who.Livery, 0.55f);
        }

        /// <summary>Two machines meeting. Soil, a skid and a short shock ring at the contact point.</summary>
        public void Shunt(Vector3 where, float strength)
        {
            where.y = 0.1f;
            debris?.Impact(where, Vector3.up, strength);
            Ring(where, 1.4f + strength * 3f, 0.35f, new Color(1f, 0.94f, 0.8f), 0.4f);
        }

        /// <summary>The crown changing hands. A wave off the plaza kerb, big enough to see from the loop.</summary>
        public void CrownTaken(int slot)
        {
            if (slot < 0) return;
            Color c = TurfArena.Livery(slot);
            Ring(new Vector3(0f, 0.08f, 0f), TurfArena.PlazaRadius + 5f, 1.35f, c * 1.7f,
                 TurfArena.PlazaRadius * 0.55f);
            Shaft(new Vector3(0f, 0.05f, 0f), 9f, c);
        }

        /// <summary>The horn. One wave out across the whole arena, in white, ahead of the reveal.</summary>
        public void Horn()
        {
            Ring(new Vector3(0f, 0.09f, 0f), TurfArena.ArenaRadius + 4f, 1.8f,
                 new Color(1f, 0.98f, 0.86f), 1f);
        }

        /// <summary>The reveal. A ring in the winner's colour, held long enough to read at height.</summary>
        public void Reveal(int winner)
        {
            if (winner < 0) return;
            Color c = TurfArena.Livery(winner);
            Ring(new Vector3(0f, 0.1f, 0f), TurfArena.ArenaRadius, 2.6f, c * 2f, 2f);
            for (int i = 0; i < TurfArena.Count; i++)
            {
                var s = TurfArena.Get(i);
                Shaft(s.outward * TurfArena.LoopMid, i == winner ? 14f : 5f, TurfArena.Livery(i));
            }
        }

        // ------------------------------------------------------------------ pieces

        void Ring(Vector3 at, float radius, float life, Color colour, float startRadius)
        {
            var p = Take();
            p.kind = Kind.Ring;
            p.live = true;
            p.age = 0f;
            p.life = life;
            p.startRadius = Mathf.Max(startRadius, 0.05f);
            p.endRadius = radius;
            p.colour = colour;
            p.tr.SetPositionAndRotation(at, Quaternion.identity);
            p.tr.localScale = new Vector3(p.startRadius, 1f, p.startRadius);
            p.mf.sharedMesh = _ringMesh;
            p.tr.gameObject.SetActive(true);
        }

        void Shaft(Vector3 at, float height, Color colour)
        {
            var p = Take();
            p.kind = Kind.Shaft;
            p.live = true;
            p.age = 0f;
            p.life = 0.7f;
            p.startRadius = Mathf.Clamp(height * 0.14f, 0.4f, 2.2f);
            p.endRadius = height;
            p.colour = colour * 1.4f;
            p.tr.SetPositionAndRotation(at, Quaternion.identity);
            p.mf.sharedMesh = _shaftMesh;
            p.tr.gameObject.SetActive(true);
        }

        void Petal(Vector3 at, Color colour, float force)
        {
            var p = Take();
            p.kind = Kind.Petal;
            p.live = true;
            p.age = 0f;
            p.life = 1.1f + (float)_rng.NextDouble() * 0.8f;
            p.colour = colour * 1.6f;

            float ang = (float)_rng.NextDouble() * Mathf.PI * 2f;
            float spread = 2.4f + (float)_rng.NextDouble() * 3.4f;
            p.velocity = new Vector3(Mathf.Sin(ang), 2.6f + (float)_rng.NextDouble() * 2.2f,
                                     Mathf.Cos(ang)) * (spread * 0.5f * force);
            p.spin = new Vector3(Range(-420f, 420f), Range(-420f, 420f), Range(-420f, 420f));

            p.tr.SetPositionAndRotation(at + Vector3.up * 0.25f, Random.rotationUniform);
            p.tr.localScale = Vector3.one * (0.22f + (float)_rng.NextDouble() * 0.16f);
            p.mf.sharedMesh = _petalMesh;
            p.tr.gameObject.SetActive(true);
        }

        float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        // ------------------------------------------------------------------ meshes

        /// <summary>
        /// A flat annulus of unit outer radius, bright at the outer edge and transparent at the
        /// inner one. Scaled on X and Z to grow, which is why it is authored at unit size.
        /// </summary>
        static Mesh BuildRing(int segments)
        {
            var verts = new Vector3[segments * 2];
            var cols = new Color[segments * 2];
            var tris = new int[segments * 6];

            const float inner = 0.72f;
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                verts[i * 2] = dir * inner;
                verts[i * 2 + 1] = dir;
                cols[i * 2] = new Color(1f, 1f, 1f, 0f);
                cols[i * 2 + 1] = Color.white;

                int n = (i + 1) % segments;
                int t = i * 6;
                tris[t] = i * 2; tris[t + 1] = i * 2 + 1; tris[t + 2] = n * 2 + 1;
                tris[t + 3] = i * 2; tris[t + 4] = n * 2 + 1; tris[t + 5] = n * 2;
            }

            var m = new Mesh { name = "TurfGlowRing" };
            m.SetVertices(verts);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.bounds = new Bounds(Vector3.zero, new Vector3(2f, 0.2f, 2f));
            return m;
        }

        /// <summary>A single petal: a four-sided blade, bright at the tip, cheap and always facing something.</summary>
        static Mesh BuildPetal()
        {
            var m = new Mesh { name = "TurfPetal" };
            m.SetVertices(new[]
            {
                new Vector3( 0f,    0f, -0.5f), new Vector3(-0.34f, 0f, 0f),
                new Vector3( 0f,    0f,  0.6f), new Vector3( 0.34f, 0f, 0f)
            });
            m.SetColors(new[]
            {
                new Color(1f, 1f, 1f, 0.55f), new Color(1f, 1f, 1f, 0.95f),
                Color.white,                  new Color(1f, 1f, 1f, 0.95f)
            });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0 }, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one);
            return m;
        }

        /// <summary>
        /// A vertical flare: two crossed quads, bright at the base and gone at the top.
        ///
        /// Crossed rather than billboarded because a billboard needs the camera every frame and this
        /// is the effect that fires while the camera is mid-blend into the overhead reveal, where
        /// "face the camera" resolves to a quad seen edge-on and therefore to nothing at all.
        /// </summary>
        static Mesh BuildShaft()
        {
            var verts = new List<Vector3>(8);
            var cols = new List<Color>(8);
            var tris = new List<int>(12);

            for (int q = 0; q < 2; q++)
            {
                Vector3 across = q == 0 ? Vector3.right : Vector3.forward;
                int b = verts.Count;
                verts.Add(-across); verts.Add(across);
                verts.Add(across + Vector3.up); verts.Add(-across + Vector3.up);
                cols.Add(Color.white); cols.Add(Color.white);
                cols.Add(new Color(1f, 1f, 1f, 0f)); cols.Add(new Color(1f, 1f, 1f, 0f));
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }

            var m = new Mesh { name = "TurfShaft" };
            m.SetVertices(verts);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.bounds = new Bounds(Vector3.up * 0.5f, new Vector3(2f, 1f, 2f));
            return m;
        }
    }
}
