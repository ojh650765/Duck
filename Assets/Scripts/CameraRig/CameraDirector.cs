using UnityEngine;

namespace DuckMow
{
    public enum CameraMode { Chase, Briefing, Reveal, Judges, Verdict, VenueTour, Scoreboard }

    /// <summary>
    /// The single camera. Every shot in the game is a mode on this rig, and mode changes are
    /// blended rather than cut, so the round reads as one continuous piece of coverage:
    /// chase → lift → overhead reveal → push in on the judges.
    ///
    /// Written by hand rather than with Cinemachine so the chase behaviour can be tuned against
    /// the mower's own velocity and drift state, which is where most of the feel lives.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class CameraDirector : MonoBehaviour
    {
        [Header("Targets")]
        public Transform target;                 // the mower
        public MowerController mower;
        public Transform judgesLookAt;
        public Transform revealLookAt;

        [Header("Chase")]
        public float chaseDistance = 5.2f;
        public float chaseHeight = 2.9f;
        public float chaseHeightAtSpeed = 3.6f;
        public float chaseDistanceAtSpeed = 6.3f;
        [Tooltip("Seconds of positional lag. Higher feels heavier and reads speed better.")]
        public float positionLag = 0.13f;
        public float rotationLag = 0.16f;
        [Tooltip("How far ahead of the mower the camera looks, in seconds of travel.")]
        public float lookAhead = 0.42f;
        public float lookHeight = 0.75f;

        [Header("Lens")]
        public float fovBase = 46f;
        public float fovAtSpeed = 54f;
        public float fovBoostKick = 6f;
        public float fovLag = 6f;

        [Header("Character")]
        [Tooltip("Degrees of roll into a turn.")]
        public float rollAmount = 3.2f;
        [Tooltip("How much the camera swings toward the direction of travel while drifting.")]
        public float driftSwing = 0.45f;
        public float groundClearance = 0.9f;
        [Tooltip("Spherecast the boom against world geometry. Off by default; it lurches.")]
        public bool avoidGeometry = false;
        public LayerMask collisionMask = 0;

        [Header("Reveal")]
        public float revealHeight = 92f;
        public float revealTilt = 6f;
        public float revealFov = 42f;

        [Header("Judges")]
        public float judgesLookHeight = 1.26f;
        public float judgesCameraHeight = 1.74f;
        public float judgesCameraDistance = 7.6f;
        public float judgesFov = 31f;
        [Tooltip("Metres the aim is pushed sideways off the bench centre. Zero centres the trio.")]
        public float judgesFrameOffset = 0f;
        [Tooltip("Framing when pushed in on the judge currently deliberating.")]
        public float judgeCloseDistance = 3.3f;
        public float judgeCloseFov = 36f;
        public float judgeCloseHeight = 0.80f;

        [Header("Verdict portrait")]
        [Tooltip("How far in front of the mower the verdict camera sits.")]
        public float verdictDistance = 4.3f;
        public float verdictHeight = 1.32f;
        [Tooltip("Yaw around the mower, so we see the duck three-quarter rather than head on.")]
        public float verdictYaw = 34f;
        [Tooltip("Push the subject to screen right, leaving the left clear for the scores.")]
        public float verdictScreenOffset = 1.15f;
        public float verdictFov = 32f;

        [Header("Briefing / Verdict")]
        public Vector3 briefingPosition = new Vector3(0f, 4.2f, -47f);
        public Vector3 briefingLookAt = new Vector3(0f, 2.2f, -56f);
        public float briefingFov = 44f;

        public CameraMode Mode { get; private set; } = CameraMode.Chase;

        Camera _cam;
        Vector3 _smoothPos;
        Quaternion _smoothRot;
        float _smoothFov;
        float _roll;

        // Blend between the previous framing and the new one on a mode change.
        float _blend = 1f, _blendDuration = 1f;
        Vector3 _blendFromPos;
        Quaternion _blendFromRot;
        float _blendFromFov;

        // Shake accumulator: one number, decayed, sampled with noise.
        float _shake;
        float _shakeDecay = 3.6f;
        float _clock;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null) _cam = gameObject.AddComponent<Camera>();
            _smoothFov = fovBase;
            _smoothPos = transform.position;
            _smoothRot = transform.rotation;
        }

        void Start()
        {
            // The game director runs first and may already have put us in Briefing, so snapping
            // to chase unconditionally here would silently throw that shot away.
            SnapToCurrent();
        }

        /// <summary>Apply the active mode's framing immediately, with no blend.</summary>
        public void SnapToCurrent()
        {
            _blend = 1f;
            Vector3 p; Quaternion r; float f;
            switch (Mode)
            {
                case CameraMode.Reveal: ComputeReveal(out p, out r, out f); break;
                case CameraMode.VenueTour: ComputeVenueTour(out p, out r, out f); break;
                case CameraMode.Scoreboard: ComputeScoreboard(out p, out r, out f); break;
                case CameraMode.Judges: ComputeJudges(out p, out r, out f); break;
                case CameraMode.Verdict: ComputeVerdict(out p, out r, out f); break;
                case CameraMode.Briefing: ComputeBriefing(out p, out r, out f); break;
                default: ComputeChase(out p, out r, out f); break;
            }
            _smoothPos = p; _smoothRot = r; _smoothFov = f;
            transform.SetPositionAndRotation(p, r);
            _cam.fieldOfView = f;
        }

        public void SetMode(CameraMode mode, float blendDuration = 1.0f)
        {
            if (Mode == mode) return;
            Mode = mode;
            _blendFromPos = transform.position;
            _blendFromRot = transform.rotation;
            _blendFromFov = _cam.fieldOfView;
            _blendDuration = Mathf.Max(0.001f, blendDuration);
            _blend = 0f;
        }

        public void AddShake(float amount) => _shake = Mathf.Min(1.6f, _shake + amount);

        public void SnapToChase()
        {
            Mode = CameraMode.Chase;
            _blend = 1f;
            ComputeChase(out Vector3 p, out Quaternion r, out float f);
            _smoothPos = p; _smoothRot = r; _smoothFov = f;
            transform.SetPositionAndRotation(p, r);
            _cam.fieldOfView = f;
        }

        void LateUpdate()
        {
            if (SimClock.Scripted) return;
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// The step the Compute* helpers smooth against.
        ///
        /// They used to read Time.deltaTime directly, which is right during normal play and wrong
        /// everywhere else: under the scripted clock the editor is not advancing frames, so
        /// deltaTime is effectively zero and the push-in on a judge never arrives — the close-ups
        /// came back pointed at open sky. Carrying the caller's step through fixes the harness and
        /// makes the camera honest about what "per second" means.
        /// </summary>
        float _dt = 1f / 60f;

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            _dt = dt;
            _clock += dt;

            Vector3 wantPos; Quaternion wantRot; float wantFov;

            switch (Mode)
            {
                case CameraMode.Reveal: ComputeReveal(out wantPos, out wantRot, out wantFov); break;
                case CameraMode.VenueTour: ComputeVenueTour(out wantPos, out wantRot, out wantFov); break;
                case CameraMode.Scoreboard: ComputeScoreboard(out wantPos, out wantRot, out wantFov); break;
                case CameraMode.Judges: ComputeJudges(out wantPos, out wantRot, out wantFov); break;
                case CameraMode.Verdict: ComputeVerdict(out wantPos, out wantRot, out wantFov); break;
                case CameraMode.Briefing: ComputeBriefing(out wantPos, out wantRot, out wantFov); break;
                default: ComputeChase(out wantPos, out wantRot, out wantFov); break;
            }

            if (_blend < 1f)
            {
                _blend = Mathf.Min(1f, _blend + dt / _blendDuration);
                float e = SmoothStep(_blend);
                wantPos = Vector3.Lerp(_blendFromPos, wantPos, e);
                wantRot = Quaternion.Slerp(_blendFromRot, wantRot, e);
                wantFov = Mathf.Lerp(_blendFromFov, wantFov, e);
                _smoothPos = wantPos; _smoothRot = wantRot; _smoothFov = wantFov;
            }

            _shake = Mathf.Max(0f, _shake - _shakeDecay * dt);
            Vector3 shakeOffset = Vector3.zero;
            if (_shake > 0.001f)
            {
                float t = _clock * 27f;
                float s = _shake * _shake * 0.22f;
                shakeOffset = new Vector3(
                    (Mathf.PerlinNoise(t, 0.31f) - 0.5f),
                    (Mathf.PerlinNoise(0.73f, t) - 0.5f),
                    (Mathf.PerlinNoise(t * 0.7f, t * 0.4f) - 0.5f)) * s;
            }

            transform.SetPositionAndRotation(_smoothPos + shakeOffset, _smoothRot);
            _cam.fieldOfView = _smoothFov;
        }

        static float SmoothStep(float t) => t * t * (3f - 2f * t);

        // ------------------------------------------------------------------ shots

        void ComputeChase(out Vector3 pos, out Quaternion rot, out float fov)
        {
            if (target == null)
            {
                pos = _smoothPos; rot = _smoothRot; fov = _smoothFov;
                return;
            }

            float dt = Mathf.Max(_dt, 1e-4f);
            float speedFrac = mower != null ? mower.SpeedFraction : 0f;
            Vector3 vel = Vector3.zero;
            if (mower != null)
            {
                var rb = mower.GetComponent<Rigidbody>();
                if (rb != null) vel = rb.linearVelocity;
            }
            Vector3 flatVel = new Vector3(vel.x, 0f, vel.z);

            // Facing: the mower's own heading, swung toward the travel direction while drifting
            // so a slide shows you where you are actually going.
            Vector3 heading = target.forward;
            if (flatVel.sqrMagnitude > 4f && mower != null && mower.IsDrifting)
                heading = Vector3.Slerp(heading, flatVel.normalized, driftSwing);
            heading = Vector3.ProjectOnPlane(heading, Vector3.up);
            if (heading.sqrMagnitude < 1e-4f) heading = Vector3.forward;
            heading.Normalize();

            float dist = Mathf.Lerp(chaseDistance, chaseDistanceAtSpeed, speedFrac);
            float height = Mathf.Lerp(chaseHeight, chaseHeightAtSpeed, speedFrac);

            Vector3 pivot = target.position;
            Vector3 desired = pivot - heading * dist + Vector3.up * height;

            // Never let the camera sink into the lawn or clip a fence post.
            desired = ResolveCollision(pivot + Vector3.up * lookHeight, desired);

            _smoothPos = Vector3.Lerp(_smoothPos, desired, 1f - Mathf.Exp(-dt / Mathf.Max(positionLag, 1e-3f)));

            Vector3 lookTarget = pivot + Vector3.up * lookHeight + flatVel * lookAhead;
            Vector3 toTarget = lookTarget - _smoothPos;
            if (toTarget.sqrMagnitude < 1e-4f) toTarget = heading;

            float targetRoll = 0f;
            if (mower != null)
            {
                var rb = mower.GetComponent<Rigidbody>();
                float yaw = rb != null ? rb.angularVelocity.y : 0f;
                targetRoll = -Mathf.Clamp(yaw, -2f, 2f) * rollAmount * (0.35f + speedFrac * 0.65f);
            }
            _roll = Mathf.Lerp(_roll, targetRoll, 1f - Mathf.Exp(-4.5f * dt));

            Quaternion want = Quaternion.LookRotation(toTarget.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, _roll);
            _smoothRot = Quaternion.Slerp(_smoothRot, want, 1f - Mathf.Exp(-dt / Mathf.Max(rotationLag, 1e-3f)));

            float wantFov = Mathf.Lerp(fovBase, fovAtSpeed, speedFrac);
            if (mower != null && mower.IsBoosting) wantFov += fovBoostKick;
            _smoothFov = Mathf.Lerp(_smoothFov, wantFov, 1f - Mathf.Exp(-fovLag * dt));

            pos = _smoothPos; rot = _smoothRot; fov = _smoothFov;
        }

        Vector3 ResolveCollision(Vector3 from, Vector3 to)
        {
            // Collision avoidance is off by default: pulling the camera in whenever a fence post
            // clips the boom reads as a lurch, and the arena has no geometry the chase camera
            // genuinely needs to dodge. The floor clamp below is kept.
            if (!avoidGeometry || collisionMask.value == 0)
            {
                to.y = Mathf.Max(to.y, groundClearance);
                return to;
            }

            Vector3 dir = to - from;
            float len = dir.magnitude;
            if (len > 1e-3f &&
                Physics.SphereCast(from, 0.35f, dir / len, out RaycastHit hit, len, collisionMask, QueryTriggerInteraction.Ignore))
            {
                to = from + dir / len * Mathf.Max(hit.distance - 0.15f, 1.2f);
            }
            to.y = Mathf.Max(to.y, groundClearance);
            return to;
        }

        void ComputeReveal(out Vector3 pos, out Quaternion rot, out float fov)
        {
            Vector3 centre = revealLookAt != null ? revealLookAt.position : Vector3.zero;
            // A few degrees of tilt keeps the world three-dimensional instead of a flat map.
            float tiltRad = revealTilt * Mathf.Deg2Rad;
            pos = centre + new Vector3(0f, revealHeight, -Mathf.Tan(tiltRad) * revealHeight);
            rot = Quaternion.LookRotation((centre - pos).normalized, Vector3.forward);
            fov = revealFov;
            _smoothPos = pos; _smoothRot = rot; _smoothFov = fov;
        }

        /// <summary>
        /// Push in on one judge while they deliberate. Three marks delivered in one static wide
        /// is a scoreboard; three close-ups with a beat between them is a performance.
        /// </summary>
        public void SetJudgeFocus(Transform judge) => _judgeFocus = judge;

        Transform _judgeFocus;

        void ComputeJudges(out Vector3 pos, out Quaternion rot, out float fov)
        {
            if (_judgeFocus != null)
            {
                Vector3 head = _judgeFocus.position + new Vector3(0f, judgeCloseHeight, 0f);
                // Slightly off-axis so the shot has a nose-room side rather than being a mugshot.
                float side = Mathf.Sign(_judgeFocus.position.x - (judgesLookAt != null ? judgesLookAt.position.x : 0f));
                if (Mathf.Abs(side) < 0.01f) side = 1f;
                // Sit above the bench and off to one side, then aim between the face and the card
                // standing on the desk rather than at the face. Aiming at the head put the card
                // below the bottom of frame and let the tent canopy into the top of it, so the
                // beat that exists to show a number showed everything except the number.
                pos = head + new Vector3(side * 0.55f, 0.30f, judgeCloseDistance);
                Vector3 aim = _judgeFocus.position + new Vector3(0f, judgeCloseHeight * 0.72f, 0.30f);
                rot = Quaternion.LookRotation((aim - pos).normalized, Vector3.up);
                fov = judgeCloseFov;
                _smoothPos = Vector3.Lerp(_smoothPos, pos, 1f - Mathf.Exp(-6f * Mathf.Max(_dt, 1e-3f)));
                _smoothRot = Quaternion.Slerp(_smoothRot, rot, 1f - Mathf.Exp(-6f * Mathf.Max(_dt, 1e-3f)));
                _smoothFov = fov;
                pos = _smoothPos; rot = _smoothRot;
                return;
            }

            Vector3 bench = judgesLookAt != null ? judgesLookAt.position : new Vector3(0f, 0f, -39.5f);
            // Frame their heads and shoulders, not the furniture: shoot from above head height and
            // look down, which keeps the desk from swallowing the bottom of the frame and the tent
            // canopy from hanging into the top of it.
            Vector3 look = bench + new Vector3(-judgesFrameOffset, judgesLookHeight, 0f);
            float creep = Mathf.PingPong(_clock * 0.16f, 1f);
            pos = bench + new Vector3(0.45f, judgesCameraHeight, judgesCameraDistance - creep * 0.45f);
            rot = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
            fov = judgesFov;
            _smoothPos = pos; _smoothRot = rot; _smoothFov = fov;
        }

        /// <summary>
        /// The closing shot: a portrait of the duck who just did that, framed to screen right so
        /// the scores and the judges' remarks have the left half of the screen to themselves.
        /// </summary>
        void ComputeVerdict(out Vector3 pos, out Quaternion rot, out float fov)
        {
            if (target == null) { ComputeBriefing(out pos, out rot, out fov); return; }

            Vector3 subject = target.position + Vector3.up * 0.86f;
            Vector3 dir = Quaternion.Euler(0f, verdictYaw, 0f) * target.forward;
            pos = subject + dir * verdictDistance + Vector3.up * verdictHeight;

            // Aim left of the duck so the duck itself sits on the right of frame.
            Vector3 flatDir = Vector3.ProjectOnPlane(subject - pos, Vector3.up).normalized;
            Vector3 camRight = Vector3.Cross(Vector3.up, flatDir).normalized;
            Vector3 aim = subject - camRight * verdictScreenOffset;

            // A slow drift so the last frame of the round is not a still photograph.
            float drift = Mathf.Sin(_clock * 0.35f) * 0.12f;
            pos += camRight * drift;

            rot = Quaternion.LookRotation((aim - pos).normalized, Vector3.up);
            fov = verdictFov;
            _smoothPos = Vector3.Lerp(_smoothPos, pos, 1f - Mathf.Exp(-4f * Mathf.Max(_dt, 1e-3f)));
            _smoothRot = Quaternion.Slerp(_smoothRot, rot, 1f - Mathf.Exp(-4f * Mathf.Max(_dt, 1e-3f)));
            _smoothFov = fov;
            pos = _smoothPos; rot = _smoothRot;
        }

        // ------------------------------------------------------------------ venue tour

        [Header("Venue tour")]
        [Tooltip("Height the tour flies over each plot to show off the finished artwork.")]
        public float tourHeight = 62f;
        [Tooltip("How far south of the plot the tour camera sits, so the work is seen at an angle.")]
        public float tourBack = 30f;
        public float tourFov = 44f;
        [Tooltip("Metres per second the camera travels between plots. Slow enough to read as a move, not a cut.")]
        public float tourSpeed = 46f;

        Vector3 _tourTargetCentre;
        float _tourPlotSize = 48f;

        /// <summary>
        /// Point the tour at a plot. The camera does not jump — it keeps its current position and
        /// flies, which is what makes four separate lawns read as one venue.
        /// </summary>
        public void SetTourTarget(Vector3 plotCentre, float plotSize)
        {
            _tourTargetCentre = plotCentre;
            _tourPlotSize = Mathf.Max(plotSize, 1f);
            if (Mode != CameraMode.VenueTour) return;
        }

        /// <summary>True once the tour camera has essentially arrived over the current plot.</summary>
        public bool TourArrived
        {
            get
            {
                TourFraming(out Vector3 want, out _, out _);
                return (transform.position - want).sqrMagnitude < 9f;
            }
        }

        void TourFraming(out Vector3 pos, out Quaternion rot, out float fov)
        {
            // Height scales with the plot so a 48 m rival lawn fills the same share of frame as
            // the player's 64 m one. The artworks are being compared; they must be shown alike.
            float h = tourHeight * (_tourPlotSize / Field.Size);
            pos = _tourTargetCentre + new Vector3(0f, h, -tourBack * (_tourPlotSize / Field.Size));
            rot = Quaternion.LookRotation((_tourTargetCentre - pos).normalized, Vector3.up);
            fov = tourFov;
        }

        void ComputeVenueTour(out Vector3 pos, out Quaternion rot, out float fov)
        {
            TourFraming(out Vector3 want, out Quaternion wantRot, out fov);

            // Constant-speed travel rather than an exponential ease, so a long leg across the venue
            // and a short hop to the next plot both read as the same deliberate camera move.
            float step = tourSpeed * Mathf.Max(_dt, 1e-4f);
            _smoothPos = Vector3.MoveTowards(_smoothPos, want, step);
            _smoothRot = Quaternion.Slerp(_smoothRot, wantRot, 1f - Mathf.Exp(-2.6f * Mathf.Max(_dt, 1e-3f)));
            _smoothFov = Mathf.Lerp(_smoothFov, fov, 1f - Mathf.Exp(-2.6f * Mathf.Max(_dt, 1e-3f)));

            pos = _smoothPos;
            rot = _smoothRot;
            fov = _smoothFov;
        }

        // ------------------------------------------------------------------ scoreboard

        [Header("Scoreboard")]
        public Transform scoreboardAnchor;
        public float scoreboardDistance = 16f;
        public float scoreboardHeight = 7.2f;
        public float scoreboardFov = 36f;

        void ComputeScoreboard(out Vector3 pos, out Quaternion rot, out float fov)
        {
            Vector3 boardPos = scoreboardAnchor != null
                ? scoreboardAnchor.position
                : Venue.PlazaCentre + new Vector3(0f, 6f, Venue.PlazaRadius - 3.5f);
            Vector3 facing = scoreboardAnchor != null ? scoreboardAnchor.forward : Vector3.back;

            Vector3 look = boardPos + Vector3.up * 5.6f;
            Vector3 want = look + facing * scoreboardDistance + Vector3.up * (scoreboardHeight - 5.6f);

            // A slow creep in, so the final beat is still moving while the rows sort themselves.
            float creep = Mathf.PingPong(_clock * 0.10f, 1f) * 1.4f;
            want -= facing * creep;

            _smoothPos = Vector3.MoveTowards(_smoothPos, want, tourSpeed * Mathf.Max(_dt, 1e-4f));
            Quaternion wantRot = Quaternion.LookRotation((look - _smoothPos).normalized, Vector3.up);
            _smoothRot = Quaternion.Slerp(_smoothRot, wantRot, 1f - Mathf.Exp(-3f * Mathf.Max(_dt, 1e-3f)));
            _smoothFov = Mathf.Lerp(_smoothFov, scoreboardFov, 1f - Mathf.Exp(-3f * Mathf.Max(_dt, 1e-3f)));

            pos = _smoothPos; rot = _smoothRot; fov = _smoothFov;
        }

        /// <summary>True once the scoreboard shot has settled.</summary>
        public bool ScoreboardArrived
        {
            get
            {
                Vector3 boardPos = scoreboardAnchor != null
                    ? scoreboardAnchor.position
                    : Venue.PlazaCentre + new Vector3(0f, 6f, Venue.PlazaRadius - 3.5f);
                Vector3 facing = scoreboardAnchor != null ? scoreboardAnchor.forward : Vector3.back;
                Vector3 want = boardPos + Vector3.up * 5.6f + facing * scoreboardDistance
                             + Vector3.up * (scoreboardHeight - 5.6f);
                return (transform.position - want).sqrMagnitude < 16f;
            }
        }

        void ComputeBriefing(out Vector3 pos, out Quaternion rot, out float fov)
        {
            pos = briefingPosition;
            rot = Quaternion.LookRotation((briefingLookAt - briefingPosition).normalized, Vector3.up);
            fov = briefingFov;
            _smoothPos = pos; _smoothRot = rot; _smoothFov = fov;
        }
    }
}
