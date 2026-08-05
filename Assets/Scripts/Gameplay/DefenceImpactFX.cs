using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The debris half of a parry: feathers, dirt, grass and the impact ring.
    ///
    /// Built by hand rather than with ParticleSystem, and the reason is the frame budget rather than
    /// taste. A ParticleSystem per effect brings its own module evaluation, its own transparent
    /// material and its own draw call, and the effects here fire in bursts during the exact frames the
    /// hit stop has already made expensive. Twenty pre-allocated boxes on two shared meshes cost four
    /// draw calls, no allocation after the first burst, and no garbage — which is what makes them
    /// affordable in a rally rather than only in a screenshot.
    ///
    /// THREE decisions worth keeping:
    ///
    /// EVERYTHING IS OPAQUE. Feathers shrink to nothing and the ring collapses its own thickness, so
    /// nothing here blends. Transparency would mean sort order, overdraw and a depth-write decision on
    /// every quad, all of it landing on the impact frames. A shrinking opaque box reads as a vanishing
    /// fleck perfectly well at the size these are actually seen at.
    ///
    /// EVERYTHING IS A BOX. A flat quad is the obvious cheaper feather and it is invisible edge-on —
    /// which, for something tumbling, means each fleck blinks out at random. Twelve triangles buys
    /// visibility from every angle, and twenty of them is 240 triangles against a 41k scene.
    ///
    /// THE CLOCK IS THE SCALED ONE. Driven from GooseDefence.Tick with the same dt as everything else,
    /// so a hit stop freezes the burst mid-air along with the world. That is deliberate: the debris
    /// hangs at the contact point for the whole 70–110 ms and then bursts outward as the clock
    /// releases. Running these on real time would spend the entire burst during the frames nothing
    /// else is moving, and the freeze would be the one moment the impact looked empty.
    /// </summary>
    public class DefenceImpactFX : MonoBehaviour
    {
        const int FeatherCount = 20;
        const int GritCount    = 14;
        const int SkidCount    = 16;
        const int RingCount    = 3;

        struct Bit
        {
            public Transform t;
            public Vector3 vel;
            public float life, maxLife;
            public float gravity;
            public Vector3 spinAxis;
            public float spin;
            public float size;
        }

        readonly Bit[] _feathers = new Bit[FeatherCount];
        readonly Bit[] _grit     = new Bit[GritCount];
        readonly Bit[] _skid     = new Bit[SkidCount];

        struct Ring
        {
            public Transform t;
            public float life, maxLife;
            public float radius0, radius1, thickness;
        }
        readonly Ring[] _rings = new Ring[RingCount];

        int _nextFeather, _nextGrit, _nextRing, _nextSkid;

        Mesh _box, _ring;
        Material _matFeather, _matDirt, _matGrass, _matRing, _matSkid;
        System.Random _rng;

        // ------------------------------------------------------------------ build

        public void Setup(System.Random rng)
        {
            _rng = rng ?? new System.Random(7);

            _box  = BuildBox();
            _ring = BuildRing(28);

            // Cream feathers against a dark goose and pale sage ground; the ring takes the same hot
            // orange the struck goose flares to, so the two halves of the impact read as one event.
            _matFeather = Flat(new Color(0.96f, 0.94f, 0.88f), "FXFeather");
            _matDirt    = Flat(new Color(0.28f, 0.19f, 0.13f), "FXDirt");
            _matGrass   = Flat(new Color(0.34f, 0.58f, 0.24f), "FXGrass");
            _matRing    = Flat(new Color(1.00f, 0.55f, 0.13f), "FXRing");
            // Scuffed earth, not rubber: this is a lawn, and a black tyre stripe on grass reads as a
            // road. Darker than the soil so it registers, warm so it belongs to the ground.
            _matSkid    = Flat(new Color(0.22f, 0.17f, 0.11f), "FXSkid");

            for (int i = 0; i < FeatherCount; i++) _feathers[i].t = MakeBit("Feather", _matFeather);
            for (int i = 0; i < GritCount; i++)
                _grit[i].t = MakeBit("Grit", i % 3 == 0 ? _matGrass : _matDirt);
            for (int i = 0; i < SkidCount; i++) _skid[i].t = MakeBit("Skid", _matSkid);
            for (int i = 0; i < RingCount; i++) _rings[i].t = MakeRing();
        }

        Transform MakeBit(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _box;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return go.transform;
        }

        Transform MakeRing()
        {
            var go = new GameObject("ImpactRing");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _ring;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _matRing;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return go.transform;
        }

        // ------------------------------------------------------------------ emit

        /// <summary>
        /// A parry. <paramref name="strength"/> is 0..1 across Normal/Good/Perfect, and it scales the
        /// count as well as the speed — a perfect hit should throw MORE debris, not the same debris
        /// faster, because count is what reads at a glance and speed is what reads in motion.
        /// </summary>
        public void Impact(Vector3 pos, Vector3 dir, float strength)
        {
            strength = Mathf.Clamp01(strength);
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();

            int feathers = Mathf.RoundToInt(6f + 10f * strength);
            for (int i = 0; i < feathers; i++)
            {
                // Biased along the launch heading and upward, with enough spread that the frozen frame
                // reads as a burst rather than as a line. A cone this wide is what makes the contact
                // point legible while everything is held still.
                Vector3 v = (dir * Rand(0.35f, 1.25f)
                          + Vector3.up * Rand(0.55f, 1.5f)
                          + Random.onUnitSphere * 0.55f).normalized * Rand(2.6f, 6.2f + 2.5f * strength);
                Spawn(ref _feathers[_nextFeather], pos + Random.insideUnitSphere * 0.35f, v,
                      life: Rand(0.55f, 0.95f), gravity: 7.5f, size: Rand(0.07f, 0.13f), flat: true);
                _nextFeather = (_nextFeather + 1) % FeatherCount;
            }

            int grit = Mathf.RoundToInt(4f + 6f * strength);
            for (int i = 0; i < grit; i++)
            {
                Vector3 v = (dir * Rand(0.2f, 0.9f)
                          + Vector3.up * Rand(0.7f, 1.6f)
                          + Random.onUnitSphere * 0.4f).normalized * Rand(2.2f, 5.4f);
                Spawn(ref _grit[_nextGrit], pos + Random.insideUnitSphere * 0.25f, v,
                      life: Rand(0.38f, 0.68f), gravity: 15f, size: Rand(0.05f, 0.10f), flat: false);
                _nextGrit = (_nextGrit + 1) % GritCount;
            }

            EmitRing(pos, 0.45f, 1.9f + 2.6f * strength, 0.22f + 0.12f * strength, 0.26f);
        }

        /// <summary>
        /// A miss that ended in the flowers: earth and leaves, no feathers, no ring. The goose is not
        /// struck here, so borrowing the parry's vocabulary would say it was.
        /// </summary>
        public void Dirt(Vector3 pos, float strength)
        {
            strength = Mathf.Clamp01(strength);
            int grit = Mathf.RoundToInt(6f + 8f * strength);
            for (int i = 0; i < grit; i++)
            {
                Vector3 v = (Vector3.up * Rand(0.8f, 1.7f) + Random.onUnitSphere * 0.7f).normalized
                          * Rand(2.4f, 5.8f);
                Spawn(ref _grit[_nextGrit], pos + Random.insideUnitSphere * 0.4f, v,
                      life: Rand(0.4f, 0.75f), gravity: 15f, size: Rand(0.06f, 0.12f), flat: false);
                _nextGrit = (_nextGrit + 1) % GritCount;
            }
        }

        /// <summary>
        /// One fleck shed by a launched goose, called along its arc. The goal asks for "a clean
        /// directional trail" on a perfect strike, and a sparse line of feathers is the version of that
        /// which shows the trajectory without becoming the visual noise the same goal warns against.
        /// </summary>
        public void TrailPuff(Vector3 pos)
        {
            Spawn(ref _feathers[_nextFeather], pos, Random.onUnitSphere * 0.6f,
                  life: 0.4f, gravity: 2.5f, size: 0.075f, flat: true);
            _nextFeather = (_nextFeather + 1) % FeatherCount;
        }

        /// <summary>
        /// The timing cue: a ring around the mower that CONTRACTS as the goose closes.
        ///
        /// The parry had no tell at all. Everything about a hit was communicated after it happened —
        /// freeze, punch, debris, a word on screen — and nothing before, so the window was found by trial
        /// and never by reading. "A clear anticipation pose and readable timing cue" is first on the
        /// goal's own list for a reason: juice that only fires on success teaches nothing.
        ///
        /// Contracting rather than expanding, and that is deliberately the opposite of the impact ring.
        /// An expanding ring says "this happened"; a ring closing on the machine says "this is arriving",
        /// and the two can never be confused because they move in opposite directions. When it reaches
        /// the mower, the goose is in range.
        ///
        /// Held rather than emitted: called every frame with a fresh radius while the bird is inbound, so
        /// it tracks the machine instead of being left behind at the point it was spawned.
        /// </summary>
        public void TimingRing(Vector3 centre, float radius, float strength)
        {
            if (_rings.Length == 0) return;

            // Slot zero is reserved for the cue so an impact burst cannot recycle it out from under a
            // goose that is still inbound.
            ref var r = ref _rings[0];
            r.life = 0f;                        // never ticked; this one is driven from outside
            r.t.position = new Vector3(centre.x, 0.07f, centre.z);
            r.t.rotation = Quaternion.identity;
            r.t.localScale = new Vector3(radius, Mathf.Lerp(0.06f, 0.20f, strength), radius);
            r.t.gameObject.SetActive(true);
        }

        /// <summary>Take the cue down — the exchange is over, one way or the other.</summary>
        public void ClearTimingRing()
        {
            if (_rings.Length > 0 && _rings[0].t != null) _rings[0].t.gameObject.SetActive(false);
        }

        /// <summary>
        /// A short scuff of torn turf where the wheels broke traction.
        ///
        /// The last thing on the goal's parry list with nothing behind it. The machine already sheds
        /// speed, gets shoved and spins on a hit — but it did all of that leaving the ground untouched,
        /// so the skid existed in the physics and nowhere the player could see it.
        ///
        /// Laid as a short row of flat marks along the direction of travel rather than as a continuous
        /// decal or a TrailRenderer. A trail would need a renderer living on the mower for the whole
        /// round to produce a mark that lasts a second and a half, and a decal needs a projector; a
        /// handful of pooled quads costs nothing and is indistinguishable at the size it is seen.
        ///
        /// They SHRINK away rather than fading, like everything else here — see the class note on why
        /// nothing in this file is transparent.
        /// </summary>
        public void Skid(Vector3 pos, Vector3 travel, float halfWidth, float strength)
        {
            strength = Mathf.Clamp01(strength);
            travel.y = 0f;
            if (travel.sqrMagnitude < 1e-4f) return;
            travel.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, travel);

            int marks = Mathf.RoundToInt(3f + 3f * strength);
            for (int lane = -1; lane <= 1; lane += 2)
            {
                for (int i = 0; i < marks; i++)
                {
                    float back = 0.25f + i * 0.42f;
                    Vector3 p = pos - travel * back + side * (halfWidth * lane);
                    p.y = 0.015f;

                    ref var b = ref _skid[_nextSkid];
                    _nextSkid = (_nextSkid + 1) % SkidCount;

                    b.t.position = p;
                    b.t.rotation = Quaternion.LookRotation(travel, Vector3.up);
                    b.t.localScale = new Vector3(0.17f, 0.02f, Rand(0.30f, 0.46f));
                    b.vel = Vector3.zero;
                    // Long enough to be noticed after the eye comes back from the impact, short enough
                    // that a rally does not tile the pitch with them.
                    b.life = b.maxLife = Rand(1.1f, 1.6f);
                    b.gravity = 0f;
                    b.spin = 0f;
                    b.spinAxis = Vector3.up;
                    b.size = 0.02f;
                    b.t.gameObject.SetActive(true);
                }
            }
        }

        void Spawn(ref Bit b, Vector3 pos, Vector3 vel, float life, float gravity, float size, bool flat)
        {
            b.t.position = pos;
            b.t.rotation = Random.rotationUniform;
            // A feather is a flattened, elongated box; grit is chunky. Same mesh, different scale — the
            // silhouette difference is what separates plumage from soil at a glance.
            b.t.localScale = flat ? new Vector3(size * 1.7f, size * 0.35f, size)
                                  : new Vector3(size, size * 0.8f, size * 0.9f);
            b.vel = vel;
            b.life = b.maxLife = life;
            b.gravity = gravity;
            b.spinAxis = Random.onUnitSphere;
            b.spin = Rand(180f, 620f) * (Rand(0f, 1f) > 0.5f ? 1f : -1f);
            b.size = size;
            b.t.gameObject.SetActive(true);
        }

        void EmitRing(Vector3 pos, float r0, float r1, float thickness, float life)
        {
            // Slots 1..N only: slot zero belongs to the timing cue, which is driven from outside and
            // must not be recycled away while a goose is still inbound.
            if (_nextRing < 1) _nextRing = 1;
            ref var r = ref _rings[_nextRing];
            _nextRing = _nextRing + 1 >= RingCount ? 1 : _nextRing + 1;

            // Flat on the ground rather than facing the lens. From a low chase camera a horizontal ring
            // reads as a shockwave leaving the point of contact and stays anchored to the pitch; a
            // camera-facing disc reads as a decal stuck to the screen.
            r.t.position = new Vector3(pos.x, Mathf.Max(pos.y, 0.06f), pos.z);
            r.t.rotation = Quaternion.identity;
            r.radius0 = r0; r.radius1 = r1; r.thickness = thickness;
            r.life = r.maxLife = life;
            r.t.localScale = new Vector3(r0, 1f, r0);
            r.t.gameObject.SetActive(true);
        }

        // ------------------------------------------------------------------ tick

        public void Tick(float dt)
        {
            if (dt <= 0f) return;

            for (int i = 0; i < _feathers.Length; i++) Step(ref _feathers[i], dt, drag: 2.2f);
            for (int i = 0; i < _grit.Length; i++)     Step(ref _grit[i], dt, drag: 1.1f);
            // Marks do not move; they only run down their clock and shrink out.
            for (int i = 0; i < _skid.Length; i++) StepStatic(ref _skid[i], dt);

            // From 1: slot zero is the timing cue and has no lifetime to tick.
            for (int i = 1; i < _rings.Length; i++)
            {
                ref var r = ref _rings[i];
                if (r.life <= 0f) continue;
                r.life -= dt;
                float k = 1f - Mathf.Clamp01(r.life / r.maxLife);
                // Fast out, slow to a stop: the shape of an impact rather than of a balloon.
                float e = 1f - (1f - k) * (1f - k);
                float radius = Mathf.Lerp(r.radius0, r.radius1, e);
                // Thickness collapses to nothing, which is the fade. See the class note on why nothing
                // here is transparent.
                float thick = r.thickness * (1f - k);
                r.t.localScale = new Vector3(radius, Mathf.Max(thick, 0.0001f), radius);
                if (r.life <= 0f) r.t.gameObject.SetActive(false);
            }
        }

        static void StepStatic(ref Bit b, float dt)
        {
            if (b.life <= 0f) return;
            b.life -= dt;
            if (b.life <= 0f) { b.t.gameObject.SetActive(false); return; }
            float k = b.life / b.maxLife;
            if (k < 0.4f)
            {
                var s = b.t.localScale;
                b.t.localScale = new Vector3(s.x * 0.94f, s.y, s.z * 0.94f);
            }
        }

        void Step(ref Bit b, float dt, float drag)
        {
            if (b.life <= 0f) return;
            b.life -= dt;
            if (b.life <= 0f) { b.t.gameObject.SetActive(false); return; }

            b.vel += Vector3.down * (b.gravity * dt);
            b.vel -= b.vel * Mathf.Min(drag * dt, 0.9f);
            Vector3 p = b.t.position + b.vel * dt;

            // Settle on the pitch instead of sinking through it, and stop tumbling once down. Debris
            // that keeps spinning on the ground reads as a physics bug in exactly the frames the
            // player is looking at the ground.
            if (p.y < b.size * 0.5f)
            {
                p.y = b.size * 0.5f;
                b.vel.x *= 0.4f; b.vel.z *= 0.4f;
                b.vel.y = -b.vel.y * 0.22f;
                b.spin *= 0.3f;
            }
            b.t.position = p;
            b.t.Rotate(b.spinAxis, b.spin * dt, Space.World);

            // The last third of life is spent shrinking away. Cheaper than a fade and, at the size
            // these are actually seen at, indistinguishable from one.
            float k = b.life / b.maxLife;
            if (k < 0.34f)
            {
                float s = Mathf.Clamp01(k / 0.34f);
                b.t.localScale *= Mathf.Lerp(0.86f, 1f, s);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _feathers.Length; i++)
            { _feathers[i].life = 0f; if (_feathers[i].t != null) _feathers[i].t.gameObject.SetActive(false); }
            for (int i = 0; i < _grit.Length; i++)
            { _grit[i].life = 0f; if (_grit[i].t != null) _grit[i].t.gameObject.SetActive(false); }
            for (int i = 0; i < _skid.Length; i++)
            { _skid[i].life = 0f; if (_skid[i].t != null) _skid[i].t.gameObject.SetActive(false); }
            for (int i = 0; i < _rings.Length; i++)
            { _rings[i].life = 0f; if (_rings[i].t != null) _rings[i].t.gameObject.SetActive(false); }
        }

        // ------------------------------------------------------------------ plumbing

        float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        Material Flat(Color c, string name)
        {
            var sh = Shader.Find("Duck/Prop")
                  ?? Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Sprites/Default");
            if (sh == null) return null;

            var m = new Material(sh) { name = name, hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            // Same trap as the greybox materials: the workhorse shader multiplies by vertex colour and
            // these meshes carry none, which comes out black rather than coloured.
            if (m.HasProperty("_VertexColorAmount")) m.SetFloat("_VertexColorAmount", 0f);
            return m;
        }

        static Mesh BuildBox()
        {
            var m = new Mesh { name = "FXBox", hideFlags = HideFlags.DontSave };
            Vector3[] v =
            {
                new(-0.5f,-0.5f,-0.5f), new(0.5f,-0.5f,-0.5f), new(0.5f,0.5f,-0.5f), new(-0.5f,0.5f,-0.5f),
                new(-0.5f,-0.5f, 0.5f), new(0.5f,-0.5f, 0.5f), new(0.5f,0.5f, 0.5f), new(-0.5f,0.5f, 0.5f)
            };
            int[] t =
            {
                0,2,1, 0,3,2,   5,6,4, 4,6,7,   4,7,0, 0,7,3,
                1,2,5, 5,2,6,   3,7,2, 2,7,6,   0,1,4, 4,1,5
            };
            m.vertices = v; m.triangles = t; m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// A flat annulus in the XZ plane, unit outer radius. Scaled non-uniformly at runtime: X and Z
        /// carry the radius, Y carries the thickness that collapses as it fades.
        /// </summary>
        static Mesh BuildRing(int segments)
        {
            var m = new Mesh { name = "FXRing", hideFlags = HideFlags.DontSave };
            const float inner = 0.72f;
            var verts = new Vector3[segments * 2];
            var tris = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                verts[i * 2] = new Vector3(ca * inner, 0f, sa * inner);
                verts[i * 2 + 1] = new Vector3(ca, 0f, sa);

                int n = (i + 1) % segments;
                int o = i * 6;
                tris[o] = i * 2; tris[o + 1] = i * 2 + 1; tris[o + 2] = n * 2 + 1;
                tris[o + 3] = i * 2; tris[o + 4] = n * 2 + 1; tris[o + 5] = n * 2;
            }
            m.vertices = verts; m.triangles = tris; m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        void OnDestroy()
        {
            if (_box != null) DestroyImmediate(_box);
            if (_ring != null) DestroyImmediate(_ring);
            foreach (var mat in new[] { _matFeather, _matDirt, _matGrass, _matRing, _matSkid })
                if (mat != null) DestroyImmediate(mat);
        }
    }
}
