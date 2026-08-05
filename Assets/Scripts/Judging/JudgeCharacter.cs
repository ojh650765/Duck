using UnityEngine;

namespace DuckMow
{
    public enum JudgeTemperament { Severe, Boisterous, Aloof }

    /// <summary>
    /// One judge's performance, played from authored animation.
    ///
    /// This used to generate everything at runtime: two octaves of Perlin noise per axis for the
    /// idle, plus a gesture picked off a random schedule, applied to four separate meshes. It was
    /// never still and never repeated, and that was exactly the problem — noise has no
    /// anticipation, no accent and no settle, so the judges read as drifting rather than as
    /// behaving. A curve that deliberately holds for a beat and then snaps is the whole substance
    /// of a performance, and it is the one thing noise cannot produce at any amplitude.
    ///
    /// They are now skinned to a nine-bone rig with four hand-keyed clips each, blended by an
    /// animator. What survives from the old version, deliberately:
    ///
    ///   - The CARD. It is a prop on the desk, not part of the character, and it is animated here
    ///     rather than in a clip. A card held in an animated hand inherits every tremor of the
    ///     performance, which read as the judge's hands shaking; standing it on the bench lets the
    ///     character be as lively as it likes while the number stays rock steady.
    ///   - The LOOK-AT. Applied after the animator has written its pose, so the head tracks the
    ///     mower on top of whatever it is playing. This is a constraint, not procedural animation
    ///     — and it rotates the head bone about the pivot the geometry was built around, which is
    ///     the one operation that provably cannot break the neck joint.
    ///
    /// The public surface is unchanged, because JudgePanel and the capture harness drive it.
    /// </summary>
    public class JudgeCharacter : MonoBehaviour
    {
        [Header("Rig")]
        public Animator animator;
        [Tooltip("The Head bone. Its origin sits on the pivot the head geometry was built around.")]
        public Transform head;
        [Tooltip("The scorecard prop standing on the bench. Not part of the skinned character.")]
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
        [Tooltip("Playback rate of this judge's idle. Fast reads as fussy, slow as aloof.")]
        public float idleSpeed = 1f;

        [Header("Look")]
        [Tooltip("How far the head turns to follow the mower, in degrees.")]
        public float lookYawLimit = 46f;
        public float lookPitchLimit = 22f;
        [Tooltip("How quickly the head catches up. Low is watchful, high is twitchy.")]
        public float lookLag = 5f;
        [Tooltip("Scales the look off as the judge leans in to study the picture instead.")]
        [Range(0f, 1f)] public float lookFadeOnAttention = 1f;

        /// <summary>0 = settled back, 1 = leaning in and studying the picture.</summary>
        public float Attention { get; set; }
        /// <summary>0 = card down, 1 = card fully raised.</summary>
        public float CardUp { get; set; }
        /// <summary>
        /// Positive = pleased, negative = disapproving. Set by <see cref="Punch"/>, and cleared
        /// directly by the panel when it resets — hence a public setter rather than a private one.
        /// </summary>
        public float Reaction { get; set; }
        /// <summary>Something for them to look at — usually the mower.</summary>
        public Transform lookTarget;

        [Header("Card contents")]
        public MeshRenderer cardRenderer;
        public TMPro.TextMeshPro cardNumber;
        [Tooltip("Everything drawn on the card — plate, face and number — hidden as one.")]
        public GameObject cardVisual;

        static readonly int IdAttention = Animator.StringToHash("Attention");
        static readonly int IdApplaud = Animator.StringToHash("Applaud");
        static readonly int IdShake = Animator.StringToHash("Shake");

        Vector3 _cardPos0;
        Quaternion _cardRot0;
        float _cardLandTimer = 99f;
        bool _cardWasUp;
        float _smoothAttention;
        float _lookYaw, _lookPitch;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.speed = Mathf.Max(0.05f, idleSpeed);
                // Offset every judge into its own part of the loop, or three animals on one bench
                // breathe in perfect unison and the whole panel reads as one puppet.
                animator.Play(0, 0, Random.value);
            }

            if (card != null)
            {
                _cardPos0 = card.localPosition;
                _cardRot0 = card.localRotation;
            }
        }

        public void SetCardNumber(int value)
        {
            if (cardNumber != null) cardNumber.text = value.ToString();
        }

        /// <summary>React to a mark. Positive applauds, negative is a disapproving head shake.</summary>
        public void Punch(float amount)
        {
            Reaction = amount;
            if (animator == null) return;
            animator.SetTrigger(amount > 0.4f ? IdApplaud : IdShake);
        }

        void LateUpdate()
        {
            if (SimClock.Scripted) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            // Attention is smoothed here rather than in the transition, so the panel can snap it
            // to zero on reset without the judge visibly whipping upright.
            _smoothAttention = Mathf.MoveTowards(_smoothAttention, Mathf.Clamp01(Attention), dt * 2.2f);
            if (animator != null) animator.SetFloat(IdAttention, _smoothAttention);

            UpdateCard(dt);
            ApplyLook(dt);
        }

        /// <summary>
        /// The card lies face-down on the bench and tips up to stand, rather than being held.
        /// It overshoots and rings down so it arrives with a knock instead of easing into place.
        /// </summary>
        void UpdateCard(float dt)
        {
            if (card == null) return;

            bool up = CardUp >= 1f;
            if (up && !_cardWasUp) _cardLandTimer = 0f;
            else if (up) _cardLandTimer += dt;
            _cardWasUp = up;

            float raise = Mathf.SmoothStep(0f, 1f, CardUp);
            float settle = up
                ? Mathf.Exp(-_cardLandTimer * 6.5f) * Mathf.Cos(_cardLandTimer * cardSlamRing) * cardOvershoot
                : 0f;

            card.localPosition = _cardPos0;
            card.localRotation = _cardRot0 * Quaternion.Euler(Mathf.Lerp(cardFlatAngle, 0f, raise) + settle, 0f, 0f);

            // Hide the plate and the number together. Toggling only the renderer used to leave the
            // number floating above an empty bench before the card came up.
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

        /// <summary>
        /// Turn the head toward the mower, on top of whatever the animator is playing.
        ///
        /// Runs in LateUpdate after the animator has written the pose, which is the only place a
        /// bone override survives. It fades out as the judge leans in, because a judge studying
        /// the lawn in front of them should not also be tracking a machine over their shoulder.
        /// </summary>
        void ApplyLook(float dt)
        {
            if (head == null) return;

            float wantYaw = 0f, wantPitch = 0f;
            if (lookTarget != null)
            {
                Vector3 to = lookTarget.position - head.position;
                if (to.sqrMagnitude > 0.5f)
                {
                    Vector3 local = transform.InverseTransformDirection(to.normalized);
                    wantYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -lookYawLimit, lookYawLimit);
                    wantPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg,
                                            -lookPitchLimit, lookPitchLimit);
                }
            }

            float fade = 1f - _smoothAttention * lookFadeOnAttention;
            float k = 1f - Mathf.Exp(-lookLag * Mathf.Max(dt, 1e-4f));
            _lookYaw = Mathf.Lerp(_lookYaw, wantYaw * fade, k);
            _lookPitch = Mathf.Lerp(_lookPitch, wantPitch * fade, k);

            // About the judge's own axes, not the bone's. The rig comes through the FBX importer's
            // axis conversion, so a bone's local X is not the axis a neck actually pitches on —
            // driving Euler(x) directly is what used to roll their heads sideways.
            head.rotation = Quaternion.AngleAxis(_lookYaw, transform.up)
                          * Quaternion.AngleAxis(_lookPitch, transform.right)
                          * head.rotation;
        }
    }
}
