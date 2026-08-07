using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Tyre tracks, laid down as the machines drive.
    ///
    /// Not baked into the level, and the difference matters. Pre-placed ruts say "somebody has
    /// driven here at some point", which is scenery — it is true before the match starts and it is
    /// exactly as true at the end. Tracks written while driving say "YOU went there, just now", and
    /// after a minute of defending, a competitor's strip carries the record of every scramble they
    /// made. That is the same idea as the cut mask the mowing round is built on: the ground
    /// remembering what the player did to it is most of why that round feels like it has weight.
    ///
    /// This is also NOT the ground-decal layer that was removed. Those were momentary marks that
    /// flashed up at an impact and then sat there for the rest of the match still claiming it had
    /// just happened. A track has no moment to be wrong about; it is a history, and histories are
    /// allowed to accumulate.
    ///
    /// Pooled, opaque and flat, like everything else here. When the pool wraps, the oldest segment
    /// is reused — so the ground holds a bounded, rolling record rather than growing without limit
    /// on a platform that cannot afford it.
    /// </summary>
    [DefaultExecutionOrder(-13)]
    public class RallyTracks : MonoBehaviour
    {
        public static RallyTracks Instance { get; private set; }

        [Tooltip("Segments in the pool, shared by all four competitors. At 0.34 m apart this is " +
                 "about thirty metres of track each, which is three lengths of a defending strip — " +
                 "enough to read as a history, bounded enough to stay affordable in a browser.")]
        public int poolSize = 360;
        [Tooltip("Metres between segments. Shorter looks continuous and costs more.")]
        public float spacing = 0.34f;
        [Tooltip("Width of one track, metres. Roughly a mower tyre.")]
        public float trackWidth = 0.26f;
        [Tooltip("Height above the ground the segments sit at. Clear of the dirt strip's own top " +
                 "face and of the scuff patches, or the whole strip flickers.")]
        public float height = 0.068f;
        [Tooltip("How much darker than bare earth a fresh track is.\n\n" +
                 "Only a shade. A track is EARTH THAT HAS BEEN PRESSED, not paint — the first tone " +
                 "was dark enough to read as a stripe drawn on the ground, and a stripe drawn on the " +
                 "ground is what made the whole effect look cheap. Close to the strip's own colour, " +
                 "so it registers as a change in the surface rather than as a mark on top of it.")]
        public Color tint = new Color(0.46f, 0.36f, 0.24f);

        Transform[] _slots;
        int _next;
        Mesh _segment;
        Material _material;

        void Awake()
        {
            Instance = this;
            _segment = BuildSegment(trackWidth);
            _material = Flat(tint, "FXTrack");

            _slots = new Transform[Mathf.Max(8, poolSize)];
            for (int i = 0; i < _slots.Length; i++)
            {
                var go = new GameObject("Track");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _segment;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = true;
                go.SetActive(false);
                _slots[i] = go.transform;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_segment != null) Destroy(_segment);
            if (_material != null) Destroy(_material);
        }

        /// <summary>Wipe the ground clean. Called at the start of a match.</summary>
        public void Clear()
        {
            if (_slots == null) return;
            foreach (var t in _slots) if (t != null) t.gameObject.SetActive(false);
            _next = 0;
        }

        /// <summary>
        /// Put one segment down under a wheel.
        ///
        /// The caller decides WHEN — spacing is measured in distance travelled rather than in
        /// seconds, so a machine crawling and a machine at full speed lay the same track and the
        /// mark records where it went rather than how long it took.
        /// </summary>
        public void Lay(Vector3 position, Vector3 forward, float groundY)
        {
            if (_slots == null || _slots.Length == 0) return;
            var t = _slots[_next];
            _next = (_next + 1) % _slots.Length;
            if (t == null) return;

            Vector3 flat = new Vector3(forward.x, 0f, forward.z);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;

            t.gameObject.SetActive(true);
            t.SetPositionAndRotation(new Vector3(position.x, groundY + height, position.z),
                                     Quaternion.LookRotation(flat.normalized, Vector3.up));
        }

        // ------------------------------------------------------------------ build

        /// <summary>
        /// One segment of tread: two shoulder lines and a pair of angled blocks between them.
        ///
        /// The blocks are what make it read as a TYRE rather than as a smear. A solid ribbon has no
        /// repeat, and the repeat is the whole cue — it is what gives the eye something to measure
        /// the machine's travel against, which on a flat brown strip it otherwise has nothing of.
        /// Local +Z is the direction of travel.
        /// </summary>
        static Mesh BuildSegment(float width)
        {
            var verts = new System.Collections.Generic.List<Vector3>(48);
            var tris = new System.Collections.Generic.List<int>(72);
            var norms = new System.Collections.Generic.List<Vector3>(48);

            void Quad(Vector3 c, float sx, float sz, float rotDeg)
            {
                var rot = Quaternion.Euler(0f, rotDeg, 0f);
                int b = verts.Count;
                verts.Add(c + rot * new Vector3(-sx, 0f, -sz));
                verts.Add(c + rot * new Vector3(-sx, 0f, sz));
                verts.Add(c + rot * new Vector3(sx, 0f, sz));
                verts.Add(c + rot * new Vector3(sx, 0f, -sz));
                for (int i = 0; i < 4; i++) norms.Add(Vector3.up);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }

            float half = width * 0.5f;

            // ONE pressed band, with a fine rib in it. Not two rails and a pair of big leaning
            // chevrons — that version read as a cartoon arrow stamped into the ground, which is the
            // "tacky" everyone saw immediately. The mistake was treating a tyre print as a DIAGRAM
            // of a tyre: at the size a track is actually seen, from four metres up and moving, a
            // real one reads as a slightly darker compressed strip with a fine texture in it, and
            // the individual blocks are far below the threshold where they are legible as shapes.
            //
            // So: one full-width band doing the work, and six shallow ribs across it that only ever
            // register as grain.
            Quad(Vector3.zero, half, 0.19f, 0f);

            const int ribs = 6;
            for (int i = 0; i < ribs; i++)
            {
                float z = (i / (ribs - 1f) - 0.5f) * 0.34f;
                Quad(new Vector3(0f, 0.001f, z), half - 0.018f, 0.011f, 0f);
            }

            var mesh = new Mesh { name = "TrackSegment", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Material Flat(Color c, string name)
        {
            var sh = Shader.Find("Duck/Prop")
                  ?? Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = name, hideFlags = HideFlags.DontSave };
            m.SetColor("_BaseColor", c);
            // Duck/Prop multiplies albedo by `_InstanceColor`, which is an instanced uniform with no
            // entry in the shader's Properties block — so a `new Material(shader)` has no value for
            // it and the instancing path enabled below reads zero, i.e. black. Same fix as
            // RallyWorldFX.Flat and the same one DuckVenueBuilder applies to the rival liveries.
            m.SetColor("_InstanceColor", Color.white);
            if (m.HasProperty("_VertexColorAmount")) m.SetFloat("_VertexColorAmount", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            m.enableInstancing = true;
            return m;
        }
    }
}
