using UnityEngine;

namespace DuckMow
{
    public enum JudgeTemperament { Severe, Boisterous, Aloof }

    /// <summary>
    /// Procedural performance for one judge. The Blender characters ship as a transform hierarchy
    /// rather than a skinned rig, so everything they do is generated here.
    ///
    /// The important part is that they are never still and never doing the same thing twice.
    /// A character holding one pose with a sine wave on it reads as a broken animation, so this
    /// layers three things: a continuous idle built from decorrelated noise, a stream of short
    /// gestures fired on a random schedule, and a temperament that decides which gestures they
    /// pick and how hard they play them. Mildred chews and glares, Boris bounces and applauds,
    /// Priscilla holds unnervingly still and then snaps her head round.
    /// </summary>
    public class JudgeCharacter : MonoBehaviour
    {
        [Header("Rig")]
        public Transform body;
        public Transform head;
        public Transform armL;
        public Transform armR;
        public Transform card;

        [Header("Scorecard on the desk")]
        [Tooltip("Degrees the card is tipped back when lying flat on the bench.")]
        public float cardFlatAngle = -88f;
        [Tooltip("Degrees past upright the card overshoots before settling, for the slam.")]
        public float cardOvershoot = 9f;
        [Tooltip("How hard the card lands. Higher rings longer.")]
        public float cardSlamRing = 15f;

        [Header("Personality")]
        public JudgeTemperament temperament = JudgeTemperament.Severe;
        [Tooltip("Overall speed of the idle. Fast reads as fussy, slow as aloof.")]
        public float idleSpeed = 1f;
        [Tooltip("Overall size of the idle motion.")]
        public float idleAmount = 1f;
        [Tooltip("How far this judge leans in when studying the picture, in degrees.")]
        public float leanAngle = 9f;
        public float cardRaise = 0.55f;
        public float cardTiltDegrees = 12f;

        [Header("Gestures")]
        [Tooltip("Average seconds between spontaneous gestures.")]
        public float gestureInterval = 3.2f;
        public float gestureIntervalJitter = 1.8f;

        [Header("Card")]
        public MeshRenderer cardRenderer;
        public TMPro.TextMeshPro cardNumber;
        [Tooltip("Everything drawn on the card — plate, face and number — hidden as one.")]
        public GameObject cardVisual;

        /// <summary>0 = settled back, 1 = leaning in and studying the picture.</summary>
        public float Attention { get; set; }
        /// <summary>0 = card down, 1 = card fully raised.</summary>
        public float CardUp { get; set; }
        /// <summary>Positive = pleased bounce, negative = disapproving shake.</summary>
        public float Reaction { get; set; }
        /// <summary>Something for them to look at — usually the mower.</summary>
        public Transform lookTarget;

        enum Gesture { None, GlanceAside, LookAtField, ShiftWeight, Chew, Applaud, SlowBlink, Preen }

        Vector3 _bodyPos0, _headPos0, _cardPos0, _armLPos0, _armRPos0;
        Quaternion _bodyRot0, _headRot0, _cardRot0, _armLRot0, _armRRot0;

        float _clock, _phase, _reactionTimer;
        float _cardLandTimer = 99f;
        bool _cardWasUp;
        float _gestureTimer, _gestureDuration, _gestureNext;
        Gesture _gesture = Gesture.None;
        float _noiseSeedA, _noiseSeedB, _noiseSeedC;
        System.Random _rng;

        void Awake()
        {
            _phase = Random.value * 10f;
            _noiseSeedA = Random.value * 100f;
            _noiseSeedB = Random.value * 100f;
            _noiseSeedC = Random.value * 100f;
            _rng = new System.Random(Random.Range(0, int.MaxValue));
            _gestureNext = 1f + (float)_rng.NextDouble() * gestureInterval;

            Capture(body, ref _bodyPos0, ref _bodyRot0);
            Capture(head, ref _headPos0, ref _headRot0);
            Capture(card, ref _cardPos0, ref _cardRot0);
            Capture(armL, ref _armLPos0, ref _armLRot0);
            Capture(armR, ref _armRPos0, ref _armRRot0);
        }

        static void Capture(Transform t, ref Vector3 p, ref Quaternion r)
        {
            if (t == null) return;
            p = t.localPosition;
            r = t.localRotation;
        }

        public void SetCardNumber(int value)
        {
            if (cardNumber != null) cardNumber.text = value.ToString();
        }

        public void Punch(float amount)
        {
            Reaction = amount;
            _reactionTimer = 0f;
            // A big reaction interrupts whatever they were doing.
            StartGesture(amount > 0.4f ? Gesture.Applaud : Gesture.GlanceAside, 1.1f);
        }

        void LateUpdate()
        {
            if (SimClock.Scripted) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            _clock += dt;
            _reactionTimer += dt;

            // Start the ring the instant the card reaches upright.
            bool up = CardUp >= 1f;
            if (up && !_cardWasUp) _cardLandTimer = 0f;
            else if (up) _cardLandTimer += dt;
            _cardWasUp = up;
            float t = _clock * idleSpeed + _phase;

            UpdateGestureSchedule(dt);

            // Damped spring reaction, so a good score gives several diminishing bounces.
            float react = Reaction * Mathf.Exp(-_reactionTimer * 4.6f) * Mathf.Cos(_reactionTimer * 9f) * 0.55f;

            // Two decorrelated noise channels per axis. Sine alone reads as machinery; noise of
            // two different frequencies reads as a living thing shifting its weight.
            float swayX = Noise(_noiseSeedA, t * 0.23f) * 2.4f + Noise(_noiseSeedA + 5f, t * 0.9f) * 0.9f;
            float swayZ = Noise(_noiseSeedB, t * 0.19f) * 2.0f + Noise(_noiseSeedB + 5f, t * 0.7f) * 0.8f;
            float headYaw = Noise(_noiseSeedC, t * 0.17f) * 9f;
            float headPitch = Noise(_noiseSeedC + 3f, t * 0.31f) * 4f;

            GestureOffsets(out float gBodyPitch, out float gBodyRoll, out float gHeadYaw,
                           out float gHeadPitch, out float gLift, out float gArmL, out float gArmR);

            // Looking at the mower overrides the idle head drift while it is on the field.
            float lookYaw = 0f, lookPitch = 0f;
            if (lookTarget != null && head != null && Attention < 0.5f)
            {
                Vector3 to = lookTarget.position - head.position;
                if (to.sqrMagnitude > 0.5f)
                {
                    Vector3 local = transform.InverseTransformDirection(to.normalized);
                    lookYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -55f, 55f) * 0.55f;
                    lookPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg, -25f, 25f) * 0.5f;
                }
            }

            if (body != null)
            {
                float breathe = (Mathf.Sin(t * 1.45f) * 0.5f + Noise(_noiseSeedA + 11f, t * 0.6f)) * 0.016f * idleAmount;
                body.localPosition = _bodyPos0 + new Vector3(0f, breathe + react * 0.09f + gLift, 0f);
                body.localRotation = _bodyRot0 * Quaternion.Euler(
                    leanAngle * Attention + gBodyPitch,
                    swayX * 0.5f * idleAmount,
                    swayZ * idleAmount + gBodyRoll);
            }

            if (head != null)
            {
                head.localPosition = _headPos0 + new Vector3(0f, react * 0.05f, 0f);
                head.localRotation = _headRot0 * Quaternion.Euler(
                    headPitch * idleAmount + leanAngle * 0.6f * Attention + react * 9f + gHeadPitch + lookPitch,
                    (headYaw * idleAmount) * (1f - Attention) + gHeadYaw + lookYaw,
                    react * 4f + swayZ * 0.4f);
            }

            if (card != null)
            {
                // The card lies face-down on the bench and tips up to stand, rather than being
                // held. A held card inherits every tremor of the character's idle, which read as
                // the judge shaking; standing it on the desk makes the number rock solid and the
                // moment it lands far more emphatic.
                float raise = Mathf.SmoothStep(0f, 1f, CardUp);

                // Overshoot and ring down, so it arrives with a knock instead of easing in.
                float settle = 0f;
                if (CardUp > 0.001f && CardUp < 1f)
                    settle = 0f;
                else if (CardUp >= 1f)
                    settle = Mathf.Exp(-_cardLandTimer * 6.5f) * Mathf.Cos(_cardLandTimer * cardSlamRing) * cardOvershoot;

                float angle = Mathf.Lerp(cardFlatAngle, 0f, raise) + settle;
                card.localPosition = _cardPos0;
                card.localRotation = _cardRot0 * Quaternion.Euler(angle, 0f, 0f);

                // Hide the plate and the number together. Toggling only the renderer used to leave
                // the number floating above an empty bench before the card came up.
                bool show = raise > 0.01f;
                if (cardVisual != null)
                {
                    if (cardVisual.activeSelf != show) cardVisual.SetActive(show);
                }
                else if (cardRenderer != null && cardRenderer.enabled != show)
                {
                    cardRenderer.enabled = show;
                }
            }

            // Arms stay in their idle now that nothing is being held up.
            if (armR != null)
                armR.localRotation = _armRRot0 * Quaternion.Euler(
                    Noise(_noiseSeedB + 2f, t * 0.7f) * 4f * idleAmount + gArmR, 0f, 0f);
            if (armL != null)
                armL.localRotation = _armLRot0 * Quaternion.Euler(
                    Noise(_noiseSeedA + 2f, t * 0.8f) * 4f * idleAmount + react * 14f + gArmL, 0f, 0f);
        }

        /// <summary>Signed smooth noise in roughly [-1, 1].</summary>
        static float Noise(float seed, float t) => Mathf.PerlinNoise(seed, t) * 2f - 1f;

        // ------------------------------------------------------------------ gestures

        void UpdateGestureSchedule(float dt)
        {
            if (_gesture != Gesture.None)
            {
                _gestureTimer += dt;
                if (_gestureTimer >= _gestureDuration) _gesture = Gesture.None;
                return;
            }

            _gestureNext -= dt;
            if (_gestureNext > 0f) return;

            _gestureNext = gestureInterval + (float)_rng.NextDouble() * gestureIntervalJitter;
            StartGesture(PickGesture(), 0f);
        }

        void StartGesture(Gesture g, float durationOverride)
        {
            _gesture = g;
            _gestureTimer = 0f;
            _gestureDuration = durationOverride > 0f ? durationOverride : DurationOf(g);
        }

        Gesture PickGesture()
        {
            double roll = _rng.NextDouble();
            switch (temperament)
            {
                case JudgeTemperament.Boisterous:
                    if (roll < 0.34) return Gesture.Applaud;
                    if (roll < 0.60) return Gesture.ShiftWeight;
                    if (roll < 0.82) return Gesture.LookAtField;
                    return Gesture.GlanceAside;

                case JudgeTemperament.Aloof:
                    if (roll < 0.45) return Gesture.SlowBlink;
                    if (roll < 0.70) return Gesture.Preen;
                    if (roll < 0.88) return Gesture.LookAtField;
                    return Gesture.GlanceAside;

                default: // Severe
                    if (roll < 0.38) return Gesture.Chew;
                    if (roll < 0.62) return Gesture.GlanceAside;
                    if (roll < 0.84) return Gesture.LookAtField;
                    return Gesture.ShiftWeight;
            }
        }

        static float DurationOf(Gesture g) => g switch
        {
            Gesture.GlanceAside => 1.4f,
            Gesture.LookAtField => 2.1f,
            Gesture.ShiftWeight => 1.6f,
            Gesture.Chew => 1.9f,
            Gesture.Applaud => 1.5f,
            Gesture.SlowBlink => 1.2f,
            Gesture.Preen => 2.4f,
            _ => 1f
        };

        void GestureOffsets(out float bodyPitch, out float bodyRoll, out float headYaw,
                            out float headPitch, out float lift, out float armL, out float armR)
        {
            bodyPitch = bodyRoll = headYaw = headPitch = lift = armL = armR = 0f;
            if (_gesture == Gesture.None) return;

            // Ease in and out so nothing snaps at the boundaries of a gesture.
            float u = Mathf.Clamp01(_gestureTimer / Mathf.Max(_gestureDuration, 1e-3f));
            float env = Mathf.Sin(u * Mathf.PI);

            switch (_gesture)
            {
                case Gesture.GlanceAside:
                    // A look at the judge next to them, with a little disapproving tilt.
                    headYaw = env * 26f;
                    headPitch = env * -4f;
                    break;

                case Gesture.LookAtField:
                    headYaw = env * -14f;
                    headPitch = env * 12f;
                    bodyPitch = env * 4f;
                    break;

                case Gesture.ShiftWeight:
                    bodyRoll = Mathf.Sin(u * Mathf.PI * 1.0f) * 4.5f;
                    lift = env * 0.02f;
                    break;

                case Gesture.Chew:
                    // Small fast jaw-ish nod on top of a slow lean; reads as chewing from 6 m.
                    headPitch = env * 3f + Mathf.Sin(_gestureTimer * 17f) * 2.6f * env;
                    headYaw = Mathf.Sin(_gestureTimer * 5f) * 2f * env;
                    break;

                case Gesture.Applaud:
                    // Both arms clap. The clap rate used to be 11 Hz with the body bouncing on
                    // every beat, which did not read as applause — it read as one judge shaking.
                    // Slower, arms only, with just a hint of body.
                    float clap = Mathf.Abs(Mathf.Sin(_gestureTimer * 5.0f)) * env;
                    armL = -28f * clap;
                    armR = -25f * clap;
                    lift = clap * 0.015f;
                    bodyPitch = -clap * 1.6f;
                    break;

                case Gesture.SlowBlink:
                    // No blink geometry, so it is played as a slow deliberate head dip.
                    headPitch = env * 7f;
                    break;

                case Gesture.Preen:
                    headYaw = Mathf.Sin(u * Mathf.PI * 2f) * 30f;
                    headPitch = env * 16f;
                    armL = -env * 12f;
                    break;
            }
        }
    }
}
