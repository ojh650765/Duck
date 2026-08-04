using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The particle feedback that makes cutting feel like cutting.
    ///
    /// One rule does most of the work: clippings are emitted in proportion to how much *uncut*
    /// grass the blade just removed. Mowing fresh grass throws a fountain of cuttings; driving
    /// back over ground you already mowed throws nothing. That single asymmetry is what stops the
    /// blade reading as a decal brush and starts it reading as a machine doing work.
    ///
    /// Everything is pooled inside Unity particle systems — no runtime instantiation during a
    /// round, which matters for a WebGL frame budget.
    /// </summary>
    public class MowerVFX : MonoBehaviour
    {
        [Header("Sources")]
        public MowerController mower;
        public Transform deckPoint;
        public Transform exhaustPoint;

        [Header("Systems")]
        public ParticleSystem clippings;
        public ParticleSystem dust;
        public ParticleSystem exhaust;
        public ParticleSystem boostTrail;

        [Header("Tuning")]
        [Tooltip("Clippings per second at full blade load and full speed.")]
        public float clippingRate = 420f;
        public float dustRateDrift = 90f;
        public float exhaustRateIdle = 4f;
        public float exhaustRateFull = 26f;

        float _clipAccum, _dustAccum, _exhaustAccum;
        ParticleSystem.EmitParams _emit;

        void Awake()
        {
            if (mower == null) mower = GetComponentInParent<MowerController>();
            if (mower != null) mower.OnImpact += OnImpact;
        }

        void OnDestroy()
        {
            if (mower != null) mower.OnImpact -= OnImpact;
        }

        void OnImpact(float strength, Vector3 point)
        {
            if (dust == null || strength < 0.15f) return;
            _emit = default;
            _emit.position = point;
            _emit.applyShapeToPosition = false;
            dust.Emit(_emit, Mathf.RoundToInt(6f + strength * 22f));
        }

        void Update()
        {
            if (SimClock.Scripted) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            if (mower == null || dt <= 0f) return;

            EmitClippings(dt);
            EmitDust(dt);
            EmitExhaust(dt);
            UpdateBoost();

            // Under scripted stepping the particle systems do not advance on their own.
            if (SimClock.Scripted)
            {
                Step(clippings, dt);
                Step(dust, dt);
                Step(exhaust, dt);
                Step(boostTrail, dt);
            }
        }

        static void Step(ParticleSystem ps, float dt)
        {
            if (ps != null) ps.Simulate(dt, true, false, true);
        }

        void EmitClippings(float dt)
        {
            if (clippings == null || deckPoint == null) return;
            if (!mower.BladeEngaged) return;

            // Proportional to fresh grass removed AND to speed, so creeping forward on uncut
            // lawn still throws a little and a fast pass throws a lot.
            float load = mower.SmoothedCutLoad;
            if (load < 0.02f) return;

            float rate = clippingRate * load * (0.35f + mower.SpeedFraction * 0.9f);
            _clipAccum += rate * dt;
            int count = Mathf.FloorToInt(_clipAccum);
            if (count <= 0) return;
            _clipAccum -= count;

            Vector3 fwd = mower.transform.forward;
            Vector3 right = mower.transform.right;
            Vector3 basePos = deckPoint.position;

            for (int i = 0; i < count && i < 40; i++)
            {
                _emit = default;
                // Spread across the blade width and throw back and up, like a real discharge.
                float across = Random.Range(-0.55f, 0.55f);
                _emit.position = basePos + right * across + Vector3.up * 0.06f;
                _emit.velocity =
                    -fwd * Random.Range(0.6f, 2.4f) +
                    right * (across * Random.Range(1.2f, 3.0f)) +
                    Vector3.up * Random.Range(1.4f, 3.4f);
                _emit.startSize = Random.Range(0.10f, 0.20f);
                _emit.startLifetime = Random.Range(0.45f, 1.0f);
                _emit.rotation3D = new Vector3(Random.value * 360f, Random.value * 360f, Random.value * 360f);
                _emit.applyShapeToPosition = false;
                clippings.Emit(_emit, 1);
            }
        }

        void EmitDust(float dt)
        {
            if (dust == null) return;
            if (!mower.IsDrifting || !mower.IsGrounded) return;

            _dustAccum += dustRateDrift * mower.SpeedFraction * dt;
            int count = Mathf.FloorToInt(_dustAccum);
            if (count <= 0) return;
            _dustAccum -= count;

            Vector3 rear = mower.transform.position - mower.transform.forward * 0.6f;
            for (int i = 0; i < count && i < 12; i++)
            {
                _emit = default;
                _emit.position = rear + mower.transform.right * Random.Range(-0.5f, 0.5f);
                _emit.velocity = Vector3.up * Random.Range(0.3f, 0.9f) +
                                 mower.transform.right * Random.Range(-0.8f, 0.8f);
                _emit.startSize = Random.Range(0.28f, 0.6f);
                _emit.startLifetime = Random.Range(0.5f, 1.1f);
                _emit.applyShapeToPosition = false;
                dust.Emit(_emit, 1);
            }
        }

        void EmitExhaust(float dt)
        {
            if (exhaust == null || exhaustPoint == null) return;

            float rate = Mathf.Lerp(exhaustRateIdle, exhaustRateFull, mower.EngineRpm01);
            _exhaustAccum += rate * dt;
            int count = Mathf.FloorToInt(_exhaustAccum);
            if (count <= 0) return;
            _exhaustAccum -= count;

            for (int i = 0; i < count && i < 6; i++)
            {
                _emit = default;
                _emit.position = exhaustPoint.position;
                _emit.velocity = Vector3.up * Random.Range(0.5f, 1.1f) +
                                 Random.insideUnitSphere * 0.18f;
                _emit.startSize = Random.Range(0.10f, 0.20f);
                _emit.startLifetime = Random.Range(0.5f, 1.0f);
                _emit.applyShapeToPosition = false;
                exhaust.Emit(_emit, 1);
            }
        }

        void UpdateBoost()
        {
            if (boostTrail == null) return;
            var emission = boostTrail.emission;
            emission.enabled = mower.IsBoosting;
        }
    }
}
