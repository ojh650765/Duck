using System.Collections.Generic;
using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The animal crowd. Ninety-odd spectators drawn with GPU instancing in a handful of calls,
    /// each bobbing on its own phase and reacting together when something happens.
    ///
    /// A still crowd makes a world feel like a diorama; a crowd that leans in when you nail a
    /// corner and groans when you flatten a gnome is most of what makes the fair feel attended.
    /// </summary>
    public class SpectatorCrowd : MonoBehaviour
    {
        [System.Serializable]
        public struct Seat
        {
            public Vector3 position;
            public float yaw;
            public float scale;
            public int species;
            public float phase;
        }

        public Seat[] seats = new Seat[0];

        [Tooltip("One mesh per species. Left empty, simple stand-ins are generated at runtime.")]
        public Mesh[] speciesMeshes = new Mesh[0];
        public Material crowdMaterial;

        [Header("Motion")]
        public float idleBobAmplitude = 0.026f;
        public float idleBobSpeed = 1.6f;
        public float cheerBobAmplitude = 0.16f;
        public float cheerBobSpeed = 7.5f;
        public float cheerDecay = 0.9f;
        [Tooltip("How far spectators turn to track the mower, in degrees.")]
        public float trackingDegrees = 26f;
        public Transform trackTarget;

        [Header("Colour variety")]
        public Color[] tints =
        {
            new Color(0.96f, 0.92f, 0.84f), new Color(0.86f, 0.74f, 0.60f),
            new Color(0.72f, 0.60f, 0.52f), new Color(0.94f, 0.80f, 0.66f),
            new Color(0.62f, 0.66f, 0.72f), new Color(0.88f, 0.62f, 0.46f),
            new Color(0.78f, 0.84f, 0.72f), new Color(0.98f, 0.86f, 0.62f)
        };

        /// <summary>0..1 excitement, driven by the game director.</summary>
        public float Cheer { get; private set; }

        const int BatchSize = 500;
        List<Matrix4x4>[] _matrices;
        List<Vector4>[] _colours;
        MaterialPropertyBlock _mpb;
        float _clock;
        Mesh[] _meshes;
        bool _ready;

        static readonly int IdInstanceColor = Shader.PropertyToID("_InstanceColor");

        void Awake() => Prepare();

        void Prepare()
        {
            if (crowdMaterial == null) { _ready = false; return; }

            int speciesCount = Mathf.Max(1, speciesMeshes != null ? speciesMeshes.Length : 0);
            bool needStandIns = speciesMeshes == null || speciesMeshes.Length == 0;
            if (needStandIns)
            {
                speciesCount = 5;
                _meshes = new Mesh[speciesCount];
                for (int i = 0; i < speciesCount; i++) _meshes[i] = BuildStandIn(i);
            }
            else
            {
                _meshes = speciesMeshes;
            }

            _matrices = new List<Matrix4x4>[speciesCount];
            _colours = new List<Vector4>[speciesCount];
            for (int i = 0; i < speciesCount; i++)
            {
                _matrices[i] = new List<Matrix4x4>(64);
                _colours[i] = new List<Vector4>(64);
            }
            _mpb = new MaterialPropertyBlock();
            _ready = _meshes.Length > 0;
        }

        void OnDestroy()
        {
            if (_meshes == null || speciesMeshes != null && speciesMeshes.Length > 0) return;
            foreach (var m in _meshes) if (m != null) Destroy(m);
        }

        /// <summary>Editor hook — placement runs before any mesh exists, so this only validates.</summary>
        public void EnsurePlaceholderMeshes()
        {
            if (crowdMaterial == null)
            {
#if UNITY_EDITOR
                crowdMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Crowd.mat");
#endif
            }
        }

        public void Excite(float amount) => Cheer = Mathf.Clamp01(Cheer + amount);

        void Update()
        {
            if (SimClock.Scripted) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            // _matrices is checked as well as _ready, and the two can genuinely disagree.
            //
            // A script recompile while the editor is in play mode reloads the domain, and Unity
            // restores what it can of a component across that: _ready is a bool and comes back
            // true, while _matrices is an array of Lists and comes back null. The guard then
            // passed on a half-restored component and the next line dereferenced null. It threw
            // every frame for the rest of the session, from a line that cannot fail in a player.
            //
            // Harmless in a WebGL build, which never reloads a domain — but the whole team's
            // workflow is edit-while-playing, and a console full of phantom exceptions is how a
            // real one gets missed.
            if (!_ready || _matrices == null) { Prepare(); if (!_ready) return; }

            Cheer = Mathf.Max(0f, Cheer - cheerDecay * dt);
            _clock += dt;
            float t = _clock;

            for (int i = 0; i < _matrices.Length; i++) { _matrices[i].Clear(); _colours[i].Clear(); }

            Vector3 track = trackTarget != null ? trackTarget.position : Vector3.zero;
            float bobAmp = Mathf.Lerp(idleBobAmplitude, cheerBobAmplitude, Cheer);
            float bobSpeed = Mathf.Lerp(idleBobSpeed, cheerBobSpeed, Cheer);

            for (int i = 0; i < seats.Length; i++)
            {
                var s = seats[i];
                int sp = Mathf.Clamp(s.species, 0, _matrices.Length - 1);

                float bob = Mathf.Abs(Mathf.Sin(t * bobSpeed + s.phase * 6.2831f)) * bobAmp;
                // Individual spectators lag the crowd slightly, so a cheer ripples instead of
                // everyone popping on the same frame.
                float lag = 1f - s.phase * 0.35f;
                Vector3 pos = s.position + Vector3.up * bob * lag;

                float yaw = s.yaw;
                if (trackTarget != null && trackingDegrees > 0.01f)
                {
                    Vector3 to = track - s.position;
                    if (to.sqrMagnitude > 1f)
                    {
                        float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                        yaw = Mathf.MoveTowardsAngle(s.yaw, want, trackingDegrees);
                    }
                }

                float squash = 1f + bob * 0.5f;
                var trs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f),
                                        new Vector3(s.scale, s.scale * squash, s.scale));
                _matrices[sp].Add(trs);

                Color tint = tints.Length > 0 ? tints[(int)(s.phase * 977f) % tints.Length] : Color.white;
                _colours[sp].Add(tint);
            }

            for (int sp = 0; sp < _matrices.Length; sp++)
            {
                var mesh = _meshes[Mathf.Min(sp, _meshes.Length - 1)];
                if (mesh == null || _matrices[sp].Count == 0) continue;

                int count = _matrices[sp].Count;
                for (int start = 0; start < count; start += BatchSize)
                {
                    int len = Mathf.Min(BatchSize, count - start);
                    var mats = _matrices[sp].GetRange(start, len).ToArray();
                    var cols = _colours[sp].GetRange(start, len).ToArray();
                    _mpb.Clear();
                    _mpb.SetVectorArray(IdInstanceColor, cols);
                    Graphics.DrawMeshInstanced(mesh, 0, crowdMaterial, mats, len, _mpb,
                        UnityEngine.Rendering.ShadowCastingMode.On, true, gameObject.layer);
                }
            }
        }

        /// <summary>
        /// A rough animal silhouette: body, head, ears. Replaced by the authored spectators,
        /// but shaped enough that the stands never read as a row of cubes.
        /// </summary>
        static Mesh BuildStandIn(int species)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var cols = new List<Color32>();

            float bodyH = 0.42f + species * 0.05f;
            float bodyR = 0.19f - species * 0.012f;
            float headR = 0.135f + species * 0.006f;
            float earH = species switch { 0 => 0.20f, 1 => 0.05f, 2 => 0.06f, 3 => 0.11f, _ => 0.03f };

            AddCapsule(verts, tris, cols, new Vector3(0f, bodyH * 0.5f, 0f), bodyR, bodyH, 8, 0.78f);
            AddCapsule(verts, tris, cols, new Vector3(0f, bodyH + headR * 0.75f, 0.02f), headR, headR * 1.5f, 8, 1.0f);
            if (earH > 0.04f)
            {
                AddCapsule(verts, tris, cols, new Vector3(-headR * 0.5f, bodyH + headR * 1.6f, 0f), 0.035f, earH, 5, 0.92f);
                AddCapsule(verts, tris, cols, new Vector3(headR * 0.5f, bodyH + headR * 1.6f, 0f), 0.035f, earH, 5, 0.92f);
            }
            // A snout so the crowd has a facing direction from any distance.
            AddCapsule(verts, tris, cols, new Vector3(0f, bodyH + headR * 0.6f, headR * 0.9f), headR * 0.45f, headR * 0.7f, 6, 1.0f);

            var mesh = new Mesh { name = $"SpectatorStandIn_{species}" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetColors(cols);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddCapsule(List<Vector3> verts, List<int> tris, List<Color32> cols,
                               Vector3 centre, float radius, float height, int segments, float shade)
        {
            int baseIndex = verts.Count;
            int rings = 5;
            for (int r = 0; r <= rings; r++)
            {
                float v = r / (float)rings;
                float ang = v * Mathf.PI;
                float y = Mathf.Cos(ang) * height * 0.5f;
                float rr = Mathf.Sin(ang) * radius;
                for (int s = 0; s < segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    verts.Add(centre + new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr));
                    // Fake ambient occlusion: darker toward the base of every mass.
                    float ao = Mathf.Lerp(0.62f, 1f, 1f - v) * shade;
                    byte c = (byte)Mathf.Clamp(Mathf.RoundToInt(ao * 255f), 0, 255);
                    cols.Add(new Color32(c, c, c, 255));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int s2 = (s + 1) % segments;
                    int a = baseIndex + r * segments + s;
                    int b = baseIndex + r * segments + s2;
                    int c = baseIndex + (r + 1) * segments + s2;
                    int d = baseIndex + (r + 1) * segments + s;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(a); tris.Add(c); tris.Add(d);
                }
        }
    }
}
