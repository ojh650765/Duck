using System.Collections.Generic;
using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Head-and-shoulders portraits of every contestant, rendered from the actual models.
    ///
    /// The characters are modelled in detail but the game almost never gets close to them — a
    /// rival is a forty-pixel shape across a fence, and the tour looks at their lawn from sixty
    /// metres up. Rendering each one into a small texture and putting it on the results card is
    /// what makes the modelling visible at all.
    ///
    /// They are rendered once, at startup, from a studio parked far below the world: a subject, a
    /// camera two metres in front of it, and a far clip short enough that nothing else in the scene
    /// can wander into frame. That avoids needing a dedicated culling layer, and it costs four
    /// small renders for the whole game rather than anything per frame.
    /// </summary>
    public class ContestantPortraits : MonoBehaviour
    {
        [System.Serializable]
        public class Subject
        {
            public string contestant;
            public Mesh mesh;
            public Material material;
            [Tooltip("Nudge to frame the head rather than the whole body.")]
            public Vector3 lookOffset = new Vector3(0f, 0.45f, 0f);
            [Tooltip("Half-height of the orthographic view, in metres.")]
            public float framing = 0.34f;
            [Tooltip("Degrees turned off dead-on, so the portrait is a three-quarter view.")]
            public float yaw = 28f;
        }

        public Subject[] subjects = new Subject[0];
        public int resolution = 192;
        [Tooltip("Where the studio sits. Far enough below the world that nothing else is in range.")]
        public Vector3 studioOrigin = new Vector3(0f, -600f, 0f);
        public Color background = new Color(0.42f, 0.31f, 0.22f, 1f);

        readonly Dictionary<string, RenderTexture> _portraits = new Dictionary<string, RenderTexture>();
        bool _rendered;

        public static ContestantPortraits Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            // Deliberately NOT rendering here. See RenderWhenReady.
            StartCoroutine(RenderWhenReady());
        }

        /// <summary>
        /// Render the portraits once the pipeline is actually up.
        ///
        /// These used to be rendered in Awake, which works in the editor — the render pipeline is
        /// already warm there — and produces garbage in a build, where Awake runs before the first
        /// frame has been drawn and URP has not finished setting itself up. The symptom is the one
        /// that showed up in the shipped player: every portrait came out as an uninitialised
        /// texture rather than a character.
        ///
        /// Waiting for the end of the first rendered frame costs nothing anybody can perceive —
        /// the portraits are not needed until the tour, a minute and a half later — and it is the
        /// difference between a face and a pattern.
        /// </summary>
        System.Collections.IEnumerator RenderWhenReady()
        {
            yield return null;                        // let every Awake and Start finish
            yield return new WaitForEndOfFrame();     // and let one real frame be drawn
            Render();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var rt in _portraits.Values)
                if (rt != null) { rt.Release(); Destroy(rt); }
            _portraits.Clear();
        }

        public Texture Get(string contestant)
        {
            // If something asks earlier than expected, render on the spot rather than hand back
            // nothing — a late portrait is better than a missing one.
            if (!_rendered) Render();
            return contestant != null && _portraits.TryGetValue(contestant, out var rt) ? rt : null;
        }

        /// <summary>Render every portrait. Safe to call again; it simply redoes them.</summary>
        public void Render()
        {
            if (_rendered || subjects == null || subjects.Length == 0) return;
            _rendered = true;

            var camGO = new GameObject("~ PortraitCamera");
            camGO.transform.SetParent(transform, false);
            var cam = camGO.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.orthographic = true;
            cam.nearClipPlane = 0.05f;
            // Short enough that the rest of the scene cannot possibly be behind the subject.
            cam.farClipPlane = 6f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.cullingMask = ~0;
            // URP wants its own camera data; without it a camera created from script can render
            // through a partially configured path.
            if (camGO.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() == null)
                camGO.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();

            var holder = new GameObject("~ PortraitSubject");
            holder.transform.SetParent(transform, false);
            var mf = holder.AddComponent<MeshFilter>();
            var mr = holder.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            for (int i = 0; i < subjects.Length; i++)
            {
                var s = subjects[i];
                if (s == null || s.mesh == null || string.IsNullOrEmpty(s.contestant)) continue;

                // Each subject gets its own patch of the studio, so a stray large mesh cannot
                // overlap the next one.
                Vector3 spot = studioOrigin + new Vector3(i * 40f, 0f, 0f);

                mf.sharedMesh = s.mesh;
                mr.sharedMaterial = s.material;
                holder.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, 180f + s.yaw, 0f));

                var rt = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.ARGB32,
                                           RenderTextureReadWrite.sRGB)
                {
                    name = $"Portrait_{s.contestant}",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 1
                };
                rt.Create();

                // Clear it explicitly. An uninitialised render texture is exactly what the broken
                // build was showing, and a target that is cleared cannot show it even if a render
                // is somehow skipped.
                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                GL.Clear(true, true, background);
                RenderTexture.active = prevActive;

                Vector3 look = spot + s.lookOffset;
                cam.orthographicSize = Mathf.Max(0.05f, s.framing);

                // View from the side the sun is coming from, so a portrait is never a silhouette.
                // The venue's sun crosses from the south-east, which means a camera parked on the
                // +Z axis — the obvious choice — looks straight at every character's shaded half.
                Vector3 fromSun = RenderSettings.sun != null
                    ? -RenderSettings.sun.transform.forward
                    : new Vector3(0.4f, 0.3f, -0.5f);
                fromSun.y = Mathf.Max(fromSun.y * 0.35f, 0.08f);   // keep it near eye level
                Vector3 eye = look + fromSun.normalized * 2.2f;

                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((look - eye).normalized, Vector3.up));
                // Turn the subject to face the camera, keeping the authored three-quarter offset.
                float faceYaw = Mathf.Atan2(eye.x - spot.x, eye.z - spot.z) * Mathf.Rad2Deg;
                holder.transform.rotation = Quaternion.Euler(0f, faceYaw + s.yaw, 0f);
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;

                _portraits[s.contestant] = rt;
            }

            mf.sharedMesh = null;
            holder.SetActive(false);
        }
    }
}
