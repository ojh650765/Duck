using System.Collections.Generic;
using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Takes the venue to night and puts fireworks over it, for the last beat of the championship.
    ///
    /// Built and driven at runtime rather than authored into the scene: the venue is a day scene in
    /// every other state, and a second set of lights and a sky that only matter for eight seconds
    /// should not be something every round pays to load, light and cull.
    ///
    /// Nothing here is undone. The championship ends on this shot, and the only ways out are the
    /// menu or a fresh championship, both of which rebuild or reload the state that matters.
    /// </summary>
    public class CeremonyNight : MonoBehaviour
    {
        [Header("Night")]
        public float fadeSeconds = 2.6f;
        public Color nightAmbient = new Color(0.13f, 0.16f, 0.26f);
        public Color nightFog = new Color(0.07f, 0.09f, 0.17f);
        public Color moonColour = new Color(0.62f, 0.72f, 1f);
        public float moonIntensity = 0.42f;
        public Vector3 moonAngles = new Vector3(34f, 205f, 0f);

        [Header("Fireworks")]
        public float burstEvery = 0.72f;
        public float burstHeight = 34f;
        public float burstSpread = 46f;
        public int shellsPerBurst = 2;

        static readonly Color[] ShellColours =
        {
            new Color(1f, 0.86f, 0.42f), new Color(1f, 0.42f, 0.38f),
            new Color(0.55f, 0.82f, 1f), new Color(0.72f, 1f, 0.62f),
            new Color(1f, 0.68f, 0.92f)
        };

        float _t;
        bool _running;
        float _burstTimer;
        int _shell;

        Light _sun;
        float _sunIntensity;
        Color _sunColour;
        Quaternion _sunRotation;
        Color _ambient, _fog;
        bool _fogWas;
        float _fogDensity;
        bool _captured;

        readonly List<ParticleSystem> _pool = new List<ParticleSystem>();
        Transform _burstRoot;

        public bool Running => _running;
        /// <summary>0..1 of the way into night. Read by anything that wants to dim with the sky.</summary>
        public float Night01 => Mathf.Clamp01(_t / Mathf.Max(fadeSeconds, 0.01f));

        /// <summary>
        /// Put a scene into night immediately, with no blend and no component left behind.
        ///
        /// For a level that IS night rather than one that turns into it: a builder calls this once
        /// after the lights exist and the scene is saved dark. Kept here so there is exactly one
        /// description of what night looks like in this game — a second set of numbers in a builder
        /// is how the ceremony's dusk and a night level end up being two different nights.
        /// </summary>
        public static void ApplyNightNow(Color ambient, Color fog, Color moon,
                                         float intensity, Vector3 angles, float fogDensity = 0.0035f)
        {
            Light sun = null;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                if (sun == null || l.intensity > sun.intensity) sun = l;
            }
            if (sun != null)
            {
                sun.intensity = intensity;
                sun.color = moon;
                sun.transform.rotation = Quaternion.Euler(angles);
                sun.shadowStrength = Mathf.Min(sun.shadowStrength, 0.55f);
            }
            RenderSettings.ambientLight = ambient;
            RenderSettings.fog = true;
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = fogDensity;
        }

        /// <summary>The same night the ceremony ends on, applied at once. One call, no arguments.</summary>
        public static void ApplyNightNow()
        {
            var d = new GameObject("~ NightDefaults").AddComponent<CeremonyNight>();
            ApplyNightNow(d.nightAmbient, d.nightFog, d.moonColour, d.moonIntensity, d.moonAngles);
            DestroyImmediate(d.gameObject);
        }

        public static CeremonyNight Ensure()
        {
            var existing = FindFirstObjectByType<CeremonyNight>();
            if (existing != null) return existing;
            var go = new GameObject("~ CeremonyNight");
            return go.AddComponent<CeremonyNight>();
        }

        public void Begin()
        {
            if (_running) return;
            Capture();
            _running = true;
            _t = 0f;
            _burstTimer = 0.35f;
        }

        void Capture()
        {
            if (_captured) return;
            _captured = true;

            // The brightest directional light in the scene is the sun by definition; there is no
            // tag for it and hunting by name breaks the moment the builder renames anything.
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                if (_sun == null || l.intensity > _sun.intensity) _sun = l;
            }
            if (_sun != null)
            {
                _sunIntensity = _sun.intensity;
                _sunColour = _sun.color;
                _sunRotation = _sun.transform.rotation;
            }
            _ambient = RenderSettings.ambientLight;
            _fogWas = RenderSettings.fog;
            _fog = RenderSettings.fogColor;
            _fogDensity = RenderSettings.fogDensity;

            var root = new GameObject("Bursts");
            root.transform.SetParent(transform, false);
            _burstRoot = root.transform;
        }

        /// <summary>Stepped by the ceremony, not by Update, so the capture harness can photograph it.</summary>
        public void Tick(float dt)
        {
            if (!_running) return;
            _t += dt;
            float k = Night01;
            float e = k * k * (3f - 2f * k);

            if (_sun != null)
            {
                _sun.intensity = Mathf.Lerp(_sunIntensity, moonIntensity, e);
                _sun.color = Color.Lerp(_sunColour, moonColour, e);
                _sun.transform.rotation = Quaternion.Slerp(_sunRotation,
                                                           Quaternion.Euler(moonAngles), e);
            }
            RenderSettings.ambientLight = Color.Lerp(_ambient, nightAmbient, e);
            RenderSettings.fog = true;
            RenderSettings.fogColor = Color.Lerp(_fogWas ? _fog : _ambient, nightFog, e);
            RenderSettings.fogDensity = Mathf.Lerp(_fogWas ? _fogDensity : 0f, 0.0035f, e);

            // Nothing goes up until the sky is dark enough to see it against.
            if (e < 0.45f) return;
            _burstTimer -= dt;
            if (_burstTimer > 0f) return;
            _burstTimer = burstEvery * Random.Range(0.7f, 1.35f);
            for (int i = 0; i < shellsPerBurst; i++) Fire();
        }

        void Fire()
        {
            Vector3 centre = Venue.PlazaCentre;
            Vector3 at = centre + new Vector3(Random.Range(-burstSpread, burstSpread),
                                              burstHeight + Random.Range(-7f, 9f),
                                              Random.Range(-burstSpread * 0.5f, burstSpread));
            var ps = Take();
            ps.transform.position = at;

            var main = ps.main;
            var col = ShellColours[_shell++ % ShellColours.Length];
            main.startColor = col;
            ps.Clear();
            ps.Play();

            AudioDirector.Instance?.CrowdCheer(0.35f, applaud: true);
        }

        ParticleSystem Take()
        {
            foreach (var p in _pool)
                if (p != null && !p.IsAlive(true)) return p;
            var made = Build();
            _pool.Add(made);
            return made;
        }

        ParticleSystem Build()
        {
            var go = new GameObject("Shell");
            go.transform.SetParent(_burstRoot, false);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.duration = 1.6f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 2.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 17f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.gravityModifier = 0.55f;
            main.maxParticles = 260;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 120) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f),
                        new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(grad);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));

            var trails = ps.trails;
            trails.enabled = true;
            trails.lifetime = new ParticleSystem.MinMaxCurve(0.22f);
            trails.dieWithParticles = true;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(0.22f);

            var r = ps.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = "M_Firework" };
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 1f);
            mat.renderQueue = 3000;
            r.material = mat;
            r.trailMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.alignment = ParticleSystemRenderSpace.View;
            return ps;
        }
    }
}
