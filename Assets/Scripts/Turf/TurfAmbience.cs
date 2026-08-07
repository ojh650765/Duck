using System.Collections.Generic;
using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The arena's own motion: pennants, drifting leaves, and the wind coming up at the finish.
    ///
    /// Bloom Rush has a problem no other mode in the game has. It is a wheel of flat green with a
    /// stone fountain in the middle, and once the four mowers are somewhere else NOTHING in frame
    /// moves. The lawn has grass that bends; the rally has three geese in the air at once. Here, a
    /// player driving an empty stretch of the outer loop is looking at a still photograph, and a
    /// still photograph is what makes ninety seconds feel like three minutes.
    ///
    /// So: forty pennants that actually flutter, a slow drift of leaves across the pitch, and a
    /// wind that rises through the closing seconds until the whole ring is snapping. All of it is
    /// driven from one phase and one gust value, so the flags on the far side of the arena are in
    /// the same weather as the ones behind the player — which is the difference between an arena
    /// with wind in it and forty objects that each wobble on their own.
    ///
    /// Everything here is transform work on a fixed set of objects. Nothing is spawned, nothing is
    /// destroyed, and the leaves are a pooled ring that wraps rather than a particle system.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class TurfAmbience : MonoBehaviour
    {
        [Tooltip("The match, so the wind knows how close the finish is. Found in the scene if empty.")]
        public TurfDirector director;

        [Header("Wind")]
        [Tooltip("Base flutter, before the finish starts pushing it.")]
        [Range(0f, 1f)] public float calm = 0.35f;
        [Tooltip("Flutter in the closing seconds.")]
        [Range(0f, 2f)] public float gale = 1.25f;
        public float gustSpeed = 0.55f;
        [Tooltip("Degrees the wind swings back and forth through.")]
        public float swing = 34f;

        [Header("Pennants")]
        [Tooltip("Degrees a pennant lifts at full wind.")]
        public float pennantLift = 62f;
        [Tooltip("Degrees a pennant snaps through as it flutters.")]
        public float pennantSnap = 26f;

        [Header("Leaves")]
        public int leafCount = 42;
        [Tooltip("Metres per second the drift carries a leaf across the arena.")]
        public float leafSpeed = 4.2f;
        public Shader glowShader;

        readonly List<Transform> _pennants = new(48);
        readonly List<float> _phase = new(48);
        readonly List<Quaternion> _rest = new(48);

        Transform[] _leaves;
        Vector3[] _leafDrift;
        float[] _leafSpin;
        Material _leafMat;
        Mesh _leafMesh;

        float _windPhase;

        void Awake()
        {
            if (director == null) director = FindFirstObjectByType<TurfDirector>();
            Collect();
            BuildLeaves();
        }

        void OnDestroy()
        {
            if (_leafMat != null) Destroy(_leafMat);
            if (_leafMesh != null) Destroy(_leafMesh);
        }

        /// <summary>
        /// Find the pennants the builder laid down, by name.
        ///
        /// By name rather than by a serialised list because the builder places forty of them in a
        /// loop and a forty-entry array in the inspector is a thing nobody can check and everybody
        /// breaks by reordering. The name is the contract, it is written in one place, and a
        /// mismatch shows up as a still arena rather than as an exception — so it is logged.
        /// </summary>
        void Collect()
        {
            foreach (Transform child in transform)
            {
                if (!child.name.StartsWith("Pennant")) continue;
                _pennants.Add(child);
                _rest.Add(child.localRotation);
                // Seeded off the position rather than off the index, so neighbouring flags differ
                // and the ring does not travel in a visible wave the same way every time.
                _phase.Add(Mathf.Repeat(child.position.x * 0.37f + child.position.z * 0.71f, 1f));
            }

            if (_pennants.Count == 0)
                Debug.LogWarning("[Bloom] TurfAmbience found no pennants to move. Rebuild the scene " +
                                 "with 'Duck/4 · Build bloom rush scene'.");
        }

        void BuildLeaves()
        {
            if (glowShader == null) glowShader = Shader.Find("Duck/TurfGlow");
            if (glowShader == null) { _leaves = System.Array.Empty<Transform>(); return; }

            _leafMat = new Material(glowShader) { hideFlags = HideFlags.HideAndDontSave };
            _leafMat.SetColor("_Color", new Color(0.55f, 0.62f, 0.28f));
            _leafMat.SetFloat("_Fade", 0.5f);
            _leafMesh = BuildLeafMesh();

            var bin = new GameObject("Leaves").transform;
            bin.SetParent(transform, false);

            _leaves = new Transform[leafCount];
            _leafDrift = new Vector3[leafCount];
            _leafSpin = new float[leafCount];

            for (int i = 0; i < leafCount; i++)
            {
                var go = new GameObject("Leaf");
                go.transform.SetParent(bin, false);
                go.AddComponent<MeshFilter>().sharedMesh = _leafMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _leafMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                _leaves[i] = go.transform;
                _leafSpin[i] = Random.Range(-90f, 90f);
                Respawn(i, initial: true);
            }
        }

        /// <summary>
        /// Put a leaf back at the upwind edge.
        ///
        /// Called when one leaves the arena, so the same forty-two objects circulate for the whole
        /// match. Height is biased low — a leaf at eye level from a chase camera reads as weather,
        /// one at fifteen metres reads as debris falling out of the sky.
        /// </summary>
        void Respawn(int i, bool initial = false)
        {
            float r = TurfArena.ArenaRadius;
            Vector3 from = initial
                ? new Vector3(Random.Range(-r, r), 0f, Random.Range(-r, r))
                : -WindDirection * (r + 4f) + Vector3.Cross(Vector3.up, WindDirection)
                  * Random.Range(-r, r);

            _leaves[i].position = new Vector3(from.x, Random.Range(0.4f, 3.2f), from.z);
            _leaves[i].rotation = Random.rotationUniform;
            _leaves[i].localScale = Vector3.one * Random.Range(0.14f, 0.26f);
            _leafDrift[i] = new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(-0.5f, 0.35f),
                                        Random.Range(-0.4f, 0.4f));
        }

        Vector3 WindDirection { get; set; } = Vector3.forward;

        void Update()
        {
            float dt = Time.deltaTime;
            float urgency = director != null ? director.Urgency : 0f;

            // One phase and one strength for the whole arena, so every flag is in the same weather.
            _windPhase += dt * (1.6f + urgency * 2.4f);
            float strength = Mathf.Lerp(calm, gale, urgency);
            float bearing = Mathf.Sin(Time.time * gustSpeed) * swing;
            WindDirection = TurfArena.Bearing(bearing);

            TickPennants(strength, bearing);
            TickLeaves(dt, strength);
        }

        void TickPennants(float strength, float bearing)
        {
            for (int i = 0; i < _pennants.Count; i++)
            {
                var t = _pennants[i];
                if (t == null) continue;

                float p = _phase[i];
                // Lift plus snap: the flag rises toward horizontal as the wind gets up, and cracks
                // back and forth about that. Two terms rather than one sine, because a flag that
                // only oscillates about rest looks like it is being waved rather than blown.
                float gust = 0.6f + 0.4f * Mathf.Sin(_windPhase * 0.7f + p * 6.28f);
                float lift = pennantLift * strength * gust;
                float snap = pennantSnap * strength * Mathf.Sin(_windPhase * 3.1f + p * 12.9f);

                t.localRotation = _rest[i]
                                * Quaternion.Euler(0f, bearing * 0.35f + snap, -lift);
            }
        }

        void TickLeaves(float dt, float strength)
        {
            if (_leaves == null) return;
            float r = TurfArena.ArenaRadius + 6f;
            Vector3 wind = WindDirection * (leafSpeed * (0.5f + strength));

            for (int i = 0; i < _leaves.Length; i++)
            {
                var t = _leaves[i];
                Vector3 p = t.position + (wind + _leafDrift[i]) * dt;

                // Bob on the gust rather than falling. These are leaves being carried, not dropped.
                p.y += Mathf.Sin(_windPhase * 1.7f + i) * 0.35f * dt;
                p.y = Mathf.Clamp(p.y, 0.3f, 4.2f);
                t.position = p;
                t.Rotate(Vector3.up * (_leafSpin[i] * dt * (0.5f + strength)), Space.Self);
                t.Rotate(Vector3.right * (_leafSpin[i] * 0.6f * dt), Space.Self);

                if (p.x * p.x + p.z * p.z > r * r) Respawn(i);
            }
        }

        static Mesh BuildLeafMesh()
        {
            var m = new Mesh { name = "TurfLeaf" };
            m.SetVertices(new[]
            {
                new Vector3(0f, 0f, -0.6f), new Vector3(-0.3f, 0f, 0f),
                new Vector3(0f, 0f,  0.6f), new Vector3( 0.3f, 0f, 0f)
            });
            m.SetColors(new[]
            {
                new Color(1f, 1f, 1f, 0.35f), new Color(1f, 1f, 1f, 0.8f),
                new Color(1f, 1f, 1f, 0.5f), new Color(1f, 1f, 1f, 0.8f)
            });
            // Wound both ways: a leaf tumbling on its own axis is edge-on and then back-facing
            // twice a second, and a single-sided one flickers out of existence as it turns.
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0 }, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one);
            return m;
        }
    }
}
