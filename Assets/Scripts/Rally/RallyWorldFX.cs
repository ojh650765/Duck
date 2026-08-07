using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The loud things, in the world rather than on the glass.
    ///
    /// Two effects live here and they exist for the same reason: a callout printed across the middle
    /// of the screen tells the player something happened but not WHERE, and in a four-cornered arena
    /// where is most of the information. Screen-space text also sits in front of everything, so the
    /// biggest, most attention-grabbing element in the frame is systematically covering the pitch at
    /// the exact moment there is something on it worth seeing.
    ///
    /// So the punctuation is staged in the world, at the place it is about:
    ///
    ///   BURST — a star that pops where a goose was knocked out or a garden was breached. It has a
    ///   position, so it says who; it has parallax and a size that falls off with distance, so it
    ///   says how far; and it is behind the geese in depth order, so it never hides one.
    ///
    ///   WAVE — nested arcs opening along a direction, like the bars of a wifi glyph, thrown out
    ///   when the horn sounds. A horn is a directional noise and this draws it as one.
    ///
    /// Built like <see cref="DefenceImpactFX"/> and for the same reasons: pooled, opaque, no
    /// transparency and no sort order, shrinking rather than fading, and stepped on the SCALED clock
    /// so a hit stop holds them in the air along with everything else.
    /// </summary>
    [DefaultExecutionOrder(-14)]
    public class RallyWorldFX : MonoBehaviour
    {
        public static RallyWorldFX Instance { get; private set; }

        const int BurstCount = 6;
        const int SparkCount = 18;
        const int WaveCount = 9;          // three pulses of three arcs

        [Header("Burst")]
        [Tooltip("How big a knockout star gets, in metres across.")]
        public float burstSize = 1.9f;
        [Tooltip("Seconds a star lives. Short — it is punctuation, not a monument.")]
        public float burstLife = 0.85f;
        [Tooltip("Degrees per second a star turns while it opens.")]
        public float burstSpin = 90f;

        [Header("Wave")]
        [Tooltip("Metres the horn's arcs travel before they die.")]
        public float waveReach = 11f;
        public float waveLife = 0.55f;
        [Tooltip("Half-width of the arc, degrees. Wide enough to read as a broadcast rather than a beam.")]
        [Range(20f, 110f)] public float waveHalfAngle = 42f;
        [Tooltip("Where the foot of each arc sits. The arcs STAND UP off the ground rather than " +
                 "lying on it — a flat annulus floating at chest height is edge-on to the chase " +
                 "camera, which is why the horn appeared to do nothing at all. A curved wall sweeping " +
                 "outward reads from any angle, including the one the player actually has.")]
        public float waveHeight = 0.06f;

        struct StarPop
        {
            public Transform t;
            public float life, maxLife;
            public float size;
            public float spin;
        }

        struct Spark
        {
            public Transform t;
            public Vector3 vel;
            public float life, maxLife;
            public float size;
        }

        struct Wave
        {
            public Transform t;
            public MeshFilter filter;
            public Mesh mesh;
            public float life, maxLife;
            public float delay;          // staggers the three arcs of one pulse
            public float reach;
        }

        readonly StarPop[] _bursts = new StarPop[BurstCount];
        readonly Spark[] _sparks = new Spark[SparkCount];
        readonly Wave[] _waves = new Wave[WaveCount];
        int _nextBurst, _nextSpark, _nextWave;

        Mesh _star, _pip;
        Material _matStar, _matSpark, _matWave;
        Camera _view;

        void Awake()
        {
            Instance = this;

            _star = BuildStar(5, 1f, 0.44f);
            _pip = BuildStar(4, 1f, 0.32f);

            _matStar = Flat(new Color(1.00f, 0.86f, 0.28f), "FXStar");
            _matSpark = Flat(new Color(1.00f, 0.97f, 0.86f), "FXSpark");
            _matWave = Flat(new Color(0.86f, 0.95f, 1.00f), "FXWave");

            for (int i = 0; i < BurstCount; i++) _bursts[i].t = MakeQuad("Burst", _star, _matStar);
            for (int i = 0; i < SparkCount; i++) _sparks[i].t = MakeQuad("Spark", _pip, _matSpark);
            for (int i = 0; i < WaveCount; i++)
            {
                var go = new GameObject("HornArc");
                go.transform.SetParent(transform, false);
                var mesh = new Mesh { name = "HornArc", hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
                _waves[i].mesh = mesh;
                _waves[i].filter = go.AddComponent<MeshFilter>();
                _waves[i].filter.sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _matWave;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.SetActive(false);
                _waves[i].t = go.transform;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var w in _waves) if (w.mesh != null) Destroy(w.mesh);
            if (_star != null) Destroy(_star);
            if (_pip != null) Destroy(_pip);
            if (_matStar != null) Destroy(_matStar);
            if (_matSpark != null) Destroy(_matSpark);
            if (_matWave != null) Destroy(_matWave);
        }

        // ------------------------------------------------------------------ emit

        /// <summary>
        /// A star where something was decided. The knockout beat, and the garden-breach beat.
        ///
        /// <paramref name="scale"/> multiplies the base size, so a knockout and a breach are the same
        /// gesture at two different weights rather than two effects to tell apart.
        /// </summary>
        public void Burst(Vector3 world, Color colour, float scale = 1f, int sparks = 8)
        {
            ref var b = ref _bursts[_nextBurst = (_nextBurst + 1) % BurstCount];
            if (b.t == null) return;

            // PUSHED AWAY FROM THE CAMERA, so it frames the moment instead of covering it.
            //
            // The class note above claims the star sits behind the geese in depth order and can
            // never hide one. It could, and it did: an opaque billboard at the CONTACT POINT is at
            // exactly the same depth as the thing that was hit, and at three and a half metres
            // across it is more than twice the size of the goose. A knockout close-up showed a wall
            // of blue with the bird somewhere behind it — the one beat whose entire job is to say
            // "that was an elimination", and the elimination was the part you could not see.
            //
            // Two and a half metres of separation costs nothing and puts the subject in front of
            // its own punctuation, which is the way round it was always meant to be.
            Vector3 away = _view != null ? (world - _view.transform.position) : Vector3.forward;
            away.Normalize();

            b.t.gameObject.SetActive(true);
            b.t.position = world + away * 2.5f;
            b.t.localScale = Vector3.zero;
            b.life = b.maxLife = burstLife;
            b.size = burstSize * Mathf.Max(scale, 0.1f);
            b.spin = burstSpin * (Random.value < 0.5f ? -1f : 1f);
            // Toward white, for the same reason the horn's arcs are: the owner stays readable in
            // the tint, and the shape stops out-shouting the play it is punctuating.
            SetColour(b.t, Color.Lerp(colour, Color.white, 0.45f));

            // Small stars flung outward. One big shape alone reads as a stamp; a shape plus a spray
            // reads as something bursting.
            for (int i = 0; i < sparks; i++)
            {
                ref var s = ref _sparks[_nextSpark = (_nextSpark + 1) % SparkCount];
                if (s.t == null) continue;
                s.t.gameObject.SetActive(true);
                s.t.position = world;
                float a = (i / (float)Mathf.Max(sparks, 1)) * Mathf.PI * 2f + Random.value * 0.6f;
                s.vel = new Vector3(Mathf.Cos(a), Random.Range(0.5f, 1.5f), Mathf.Sin(a))
                      * Random.Range(3.4f, 7.2f) * Mathf.Max(scale, 0.5f);
                s.life = s.maxLife = burstLife * Random.Range(0.6f, 1.0f);
                s.size = 0.34f * Mathf.Max(scale, 0.5f);
                SetColour(s.t, Color.Lerp(colour, Color.white, 0.45f));
            }
        }

        /// <summary>
        /// The horn: three arcs opening along <paramref name="direction"/>, one behind the other.
        ///
        /// Staggered rather than simultaneous, because a wifi glyph is read as bars at increasing
        /// distance and a single expanding ring is read as a shockwave. The difference is what makes
        /// this say "sound" instead of "impact" — and the mode already has impacts.
        /// </summary>
        public void HornWave(Vector3 origin, Vector3 direction, Color colour)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();

            Quaternion facing = Quaternion.LookRotation(flat, Vector3.up);
            Vector3 at = new Vector3(origin.x, waveHeight, origin.z);

            for (int i = 0; i < 3; i++)
            {
                ref var w = ref _waves[_nextWave = (_nextWave + 1) % WaveCount];
                if (w.t == null) continue;
                w.t.gameObject.SetActive(true);
                w.t.SetPositionAndRotation(at, facing);
                w.life = w.maxLife = waveLife;
                // The outer bars start later and travel further, which is what stacks them into a
                // glyph instead of a single thick band.
                w.delay = i * 0.075f;
                w.reach = waveReach * (0.55f + i * 0.28f);
                // Pulled most of the way to white.
                //
                // At full livery these arcs were the loudest thing in the frame by a wide margin —
                // three saturated bands sweeping the pitch while the actual event, a goose being
                // struck, happened underneath them. The horn is punctuation. It has to be legible
                // and it must not out-shout the play, and the way to have both is to keep the shape
                // and give up the saturation: the owner is still readable in the tint.
                SetColour(w.t, Color.Lerp(colour, Color.white, 0.6f));
                Rebuild(ref w, 0f);
            }

            Burst(at + flat * 1.6f + Vector3.up * 1.5f, colour, 0.6f, 4);

            var a = AudioDirector.Instance;
            if (a != null) a.PlayHorn();
        }

        // ------------------------------------------------------------------ step

        void Update()
        {
            // Scaled, so a hit stop holds these in the air with the rest of the world.
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (_view == null) _view = Camera.main;

            StepBursts(dt);
            StepSparks(dt);
            StepWaves(dt);
        }

        void StepBursts(float dt)
        {
            for (int i = 0; i < _bursts.Length; i++)
            {
                ref var b = ref _bursts[i];
                if (b.t == null || b.life <= 0f) continue;
                b.life -= dt;
                if (b.life <= 0f) { b.t.gameObject.SetActive(false); continue; }

                float k = 1f - b.life / Mathf.Max(b.maxLife, 1e-4f);       // 0 at birth, 1 at death
                // Opens fast with an overshoot, then collapses to nothing. Shrinking IS the fade —
                // nothing here is transparent, for the reasons in DefenceImpactFX's note.
                float open = k < 0.25f ? Mathf.SmoothStep(0f, 1.18f, k / 0.25f)
                                       : Mathf.Lerp(1.18f, 0f, Mathf.SmoothStep(0f, 1f, (k - 0.25f) / 0.75f));
                b.t.localScale = Vector3.one * (b.size * open);
                Face(b.t);
                b.t.Rotate(0f, 0f, b.spin * dt, Space.Self);
            }
        }

        void StepSparks(float dt)
        {
            for (int i = 0; i < _sparks.Length; i++)
            {
                ref var s = ref _sparks[i];
                if (s.t == null || s.life <= 0f) continue;
                s.life -= dt;
                if (s.life <= 0f) { s.t.gameObject.SetActive(false); continue; }

                s.vel += Vector3.down * (11f * dt);
                s.vel *= 1f - 1.6f * dt;
                s.t.position += s.vel * dt;
                float k = s.life / Mathf.Max(s.maxLife, 1e-4f);
                s.t.localScale = Vector3.one * (s.size * k);
                Face(s.t);
                s.t.Rotate(0f, 0f, 420f * dt, Space.Self);
            }
        }

        void StepWaves(float dt)
        {
            for (int i = 0; i < _waves.Length; i++)
            {
                ref var w = ref _waves[i];
                if (w.t == null || w.life <= 0f) continue;

                if (w.delay > 0f) { w.delay -= dt; continue; }

                w.life -= dt;
                if (w.life <= 0f) { w.t.gameObject.SetActive(false); continue; }
                Rebuild(ref w, 1f - w.life / Mathf.Max(w.maxLife, 1e-4f));
            }
        }

        /// <summary>
        /// Re-cut one arc at the radius and thickness its age asks for.
        ///
        /// Rebuilt rather than scaled, because an annulus cannot be thinned by scaling an axis — its
        /// thickness is radial. Sixteen segments times nine arcs is nothing, and it buys the one
        /// property that matters: the bar collapses to nothing instead of vanishing at full width.
        /// </summary>
        void Rebuild(ref Wave w, float k)
        {
            const int seg = 20;
            float ease = Mathf.SmoothStep(0f, 1f, k);
            float radius = Mathf.Lerp(0.6f, w.reach, ease);
            float height = Mathf.Lerp(0.15f, 0.95f, Mathf.Min(k * 3.5f, 1f)) * (1f - k * k);

            var verts = new Vector3[(seg + 1) * 2];
            var tris = new int[seg * 12];
            float half = waveHalfAngle * Mathf.Deg2Rad;

            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(-half, half, i / (float)seg);
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                verts[i * 2] = dir * radius;
                verts[i * 2 + 1] = dir * radius + Vector3.up * height;
            }
            for (int i = 0; i < seg; i++)
            {
                int v = i * 2, t = i * 12;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 3;
                tris[t + 3] = v; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
                tris[t + 6] = v; tris[t + 7] = v + 3; tris[t + 8] = v + 1;
                tris[t + 9] = v; tris[t + 10] = v + 2; tris[t + 11] = v + 3;
            }

            // Normals written by hand, pointing UP, and this is the whole reason the arcs came out
            // BLACK.
            //
            // The band is drawn twice with opposite winding so it is visible from either side — the
            // wave expands away from the machine, so the player is always looking at its inside
            // face. RecalculateNormals then averaged the two opposing faces at every shared vertex
            // and produced normals of length zero, which normalise to nothing, which the shader
            // reads as a surface facing no light at all.
            //
            // Straight up is the right answer rather than merely a working one. This is a glyph, not
            // a piece of the world: it wants to be evenly lit from any angle at any moment of its
            // half second, and a band with real side-facing normals would flicker from bright to
            // dark as it swept past the sun's azimuth.
            var normals = new Vector3[verts.Length];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;

            w.mesh.Clear();
            w.mesh.vertices = verts;
            w.mesh.normals = normals;
            w.mesh.triangles = tris;
            w.mesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------ build

        /// <summary>Turn a flat shape to face the camera. Billboarded so a star never edges out.</summary>
        void Face(Transform t)
        {
            if (_view == null) return;
            Vector3 to = t.position - _view.transform.position;
            if (to.sqrMagnitude < 1e-4f) return;
            t.rotation = Quaternion.LookRotation(to.normalized, _view.transform.up);
        }

        Transform MakeQuad(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return go.transform;
        }

        /// <summary>
        /// Per-instance colour without a per-instance material.
        ///
        /// The prop shader takes its albedo from the vertex colours, so the tint is written into the
        /// MESH's colour stream — except the mesh is shared, so it goes through a property block
        /// instead. Four contestants and three event weights would otherwise be a dozen materials.
        /// </summary>
        void SetColour(Transform t, Color c)
        {
            var mr = t.GetComponent<MeshRenderer>();
            if (mr == null) return;
            _block ??= new MaterialPropertyBlock();
            mr.GetPropertyBlock(_block);
            _block.SetColor(IdBaseColor, c);
            mr.SetPropertyBlock(_block);
        }

        MaterialPropertyBlock _block;
        static readonly int IdBaseColor = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// A flat unlit-ish material in the project's own prop shader.
        ///
        /// Vertex colour is dialled OUT: these meshes are generated and carry no painted colours, and
        /// at full vertex-colour amount the shader would multiply the tint by whatever happens to be
        /// in that register, which for a mesh with no colour stream is not white on every platform.
        /// </summary>
        static Material Flat(Color c, string name)
        {
            var sh = Shader.Find("Duck/Prop")
                  ?? Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = name, hideFlags = HideFlags.DontSave };
            m.SetColor(IdBaseColor, c);
            // WHY THE HORN CAME OUT BLACK.
            //
            // Duck/Prop's albedo is `_BaseColor * vertexColour * _InstanceColor`, and
            // `_InstanceColor` is NOT declared in the shader's Properties block — it only exists as
            // an instanced uniform. A material built with `new Material(shader)` therefore has no
            // value for it at all, and once `enableInstancing` is on below, the instancing path
            // reads zero and multiplies the whole tint away: the arcs drew, lit correctly, at
            // colour (0,0,0). The vertex-colour trap on the line above is the same bug one term to
            // the left, and it was already handled; this is the term that was missed.
            //
            // DuckModelIntegration and DuckVenueBuilder both set this to white for exactly this
            // reason. Doing it unconditionally rather than behind HasProperty, as they do, because
            // a uniform outside the Properties block does not answer HasProperty.
            m.SetColor("_InstanceColor", Color.white);
            if (m.HasProperty("_VertexColorAmount")) m.SetFloat("_VertexColorAmount", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", 0.5f);
            m.enableInstancing = true;
            return m;
        }

        /// <summary>A flat star in the XY plane, <paramref name="points"/> spikes, ready to billboard.</summary>
        static Mesh BuildStar(int points, float outer, float inner)
        {
            int n = points * 2;
            var verts = new Vector3[n + 1];
            var tris = new int[n * 3];
            verts[n] = Vector3.zero;                       // hub

            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f + Mathf.PI * 0.5f;
                float r = (i % 2 == 0) ? outer : inner;
                verts[i] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
            }
            for (int i = 0; i < n; i++)
            {
                int t = i * 3;
                tris[t] = n;
                tris[t + 1] = i;
                tris[t + 2] = (i + 1) % n;
            }

            var mesh = new Mesh { name = $"Star{points}", hideFlags = HideFlags.DontSave };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
