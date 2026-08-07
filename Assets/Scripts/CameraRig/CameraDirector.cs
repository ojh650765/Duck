using UnityEngine;

namespace DuckMow
{
    public enum CameraMode
    {
        Chase, Briefing, Reveal, Judges, Verdict, VenueTour, Scoreboard,
        /// <summary>High over the whole venue while the picture is studied, before the count-in.</summary>
        VenuePreview,
        /// <summary>The one-per-round lift, so the player can see what they have actually cut.</summary>
        Aerial,
        /// <summary>
        /// A push in on the trophy by the judges' bench, for the championship ceremony. Appended at
        /// the end rather than filed next to Verdict where it belongs in the running order, because
        /// the shot list is referenced by value by the capture tools.
        /// </summary>
        Trophy
    }

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
        [Tooltip("Metres the aim point is pushed down the machine's heading, REGARDLESS of speed.\n\n" +
                 "Zero on the lawn, where the subject is the mower and the picture under it. The rally " +
                 "needs it: aiming at a stationary mower puts the mower in the middle of frame and " +
                 "fills the bottom half with the dirt between the lens and it, while the entire match " +
                 "happens in the top half. Leading the aim drops the machine onto the lower third and " +
                 "hands the rest of the screen to the pitch. Distinct from lookAhead, which is " +
                 "velocity-scaled and therefore does nothing at the exact moment a defender is sat " +
                 "still waiting for a bird.")]
        public float lookForward = 0f;

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
        [Tooltip("Camera height ABOVE the aim point, which is already 0.86 m up the machine. " +
                 "Small values put the lens near the duck's own eye line.")]
        // Dropped from 1.32.
        //
        // At 1.32 the lens sat 2.18 m up and looked down at the duck, which is the angle you shoot
        // a small object on a table from — it made the closing portrait read as an inventory
        // screenshot rather than as a character who has just been judged. Near eye level the duck
        // gets the sky behind it and holds the frame.
        //
        // Not lower than this: the uncut blade layer stands about half a metre off the lawn and
        // the mower parks on open grass, so a lens under roughly 1.2 m world height starts taking
        // grass across the bottom of the shot and eventually sits inside it.
        public float verdictHeight = 0.55f;
        [Tooltip("Yaw around the mower, so we see the duck three-quarter rather than head on.")]
        public float verdictYaw = 34f;
        [Tooltip("Push the subject to screen right, leaving the left clear for the scores.")]
        public float verdictScreenOffset = 1.15f;
        public float verdictFov = 32f;

        // ------------------------------------------------------------------ defence framing
        //
        // Deliberately NOT a camera mode. The defence phase has to read as a continuation of driving
        // the same mower, and a mode of its own would have given it its own framing, its own blend
        // and its own feel — which is exactly the detached-minigame camera the phase must not have.
        // So it is a set of modifiers layered onto Chase: the boom lengthens, the lens widens, the
        // aim is BIASED toward the goose without ever being handed to it, and a hit punches the
        // lens. Turn the subject off and Chase is bit-for-bit what it was.

        [Header("Defence framing")]
        [Tooltip("Metres the boom lengthens at full rally energy, so an escalating rally has more " +
                 "room in frame without the shot changing character.")]
        public float defencePullBack = 2.6f;
        [Tooltip("Metres the camera rises at full rally energy.")]
        public float defenceLift = 1.1f;
        [Tooltip("Degrees the lens widens at full rally energy.")]
        public float defenceFovWiden = 7f;
        [Tooltip("How far the aim point is allowed to slide from the mower toward the goose, 0..1. " +
                 "Bounded on purpose: past about a third the player stops being able to steer by " +
                 "what is on screen, which is taking control away rather than helping them aim.")]
        [Range(0f, 0.6f)] public float defenceAimBias = 0.32f;
        [Tooltip("Extra bias while the goose is close, where the player actually needs to see it.")]
        public float defenceAimNearDistance = 9f;
        [Tooltip("Metres the camera dollies in on a hit punch, and how much the lens narrows with it.")]
        public float punchDolly = 1.5f;
        public float punchFov = 9f;
        [Tooltip("How fast a punch decays. High: a punch has to be over before the player's next " +
                 "input, or it reads as the camera being broken rather than as an impact.")]
        public float punchDecay = 7f;

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
                case CameraMode.VenuePreview: ComputeVenuePreview(out p, out r, out f); break;
                case CameraMode.Aerial: ComputeAerial(out p, out r, out f); break;
                case CameraMode.Trophy: ComputeTrophy(out p, out r, out f); break;
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
            // When this shot started, for the shots that dolly rather than settle. Taken from the
            // rig's own clock so it survives the scripted stepping the capture tools drive.
            _modeEnteredAt = _clock;
        }

        float _modeEnteredAt;

        float _lookBack;

        public void AddShake(float amount) => _shake = Mathf.Min(1.6f, _shake + amount);

        /// <summary>
        /// Tell the rig its subject has just been moved rather than driven there.
        ///
        /// Only the mower-following shots care, and they care a lot: the round starts by putting
        /// the machine back on its mark, and if the camera is on it at that instant — the verdict
        /// portrait, or a chase left over from the intro — the subject vanishes out of frame and
        /// the rig lurches after it across the field. That is the "teleport" the player sees, and
        /// it was fixed once at the Briefing transition, which only covered one of the several
        /// routes into a new round. Handling it here covers all of them, including the ones added
        /// later by whoever reads this.
        /// </summary>
        public void NotifyTargetTeleported()
        {
            if (Mode != CameraMode.Chase && Mode != CameraMode.Verdict) return;
            SnapToCurrent();
        }

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
                case CameraMode.VenuePreview: ComputeVenuePreview(out wantPos, out wantRot, out wantFov); break;
                case CameraMode.Aerial: ComputeAerial(out wantPos, out wantRot, out wantFov); break;
                case CameraMode.Trophy: ComputeTrophy(out wantPos, out wantRot, out wantFov); break;
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
            // Decayed on SCALED time, deliberately. A hit stop drops the timescale to near zero, so
            // the punch and the shake hold for the duration of the freeze and release as the world
            // starts moving again — which is the whole effect. Decaying on unscaled time would spend
            // the punch during the frames nothing else is moving.
            _punch = Mathf.Max(0f, _punch - punchDecay * dt);
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

            // Look over the shoulder, while held.
            //
            // Swung rather than snapped, and swung through the SIDE: at half travel the shot is
            // side-on to the machine, which is a camera move a viewer can follow. A hard cut to the
            // reverse angle on the frame the key goes down is indistinguishable from a bug, and in a
            // mode where things are thrown at you from behind, losing your bearings for a beat is
            // worse than not having the button.
            //
            // Held rather than toggled: a glance that ends when you let go can never leave the
            // player driving forwards while facing backwards.
            bool wantBack = InputReader.Instance != null && InputReader.Instance.LookBack;
            _lookBack = Mathf.MoveTowards(_lookBack, wantBack ? 1f : 0f, dt * 4.5f);
            if (_lookBack > 0.001f)
                heading = Quaternion.AngleAxis(180f * _lookBack, Vector3.up) * heading;

            float dist = Mathf.Lerp(chaseDistance, chaseDistanceAtSpeed, speedFrac);
            float height = Mathf.Lerp(chaseHeight, chaseHeightAtSpeed, speedFrac);

            // A rally winds the shot up: further back, a little higher, a little wider. All three
            // ride the same energy value so the escalation reads as one gesture rather than as three
            // settings drifting apart.
            //
            // Energy is NOT gated on there being a subject. It was, and that is wrong for a four-way:
            // the board being busy is exactly when the shot needs to open up, and the frames where
            // nothing is inbound at the player are the frames three geese are loose somewhere else.
            // Tying the pull-back to the bias meant the camera tightened up the moment the player was
            // no longer the target, which is the opposite of what a wide free-for-all needs.
            _energySmoothed = Mathf.Lerp(_energySmoothed, _rallyEnergy,
                                         1f - Mathf.Exp(-2.2f * dt));
            dist += defencePullBack * _energySmoothed;
            height += defenceLift * _energySmoothed;

            Vector3 pivot = target.position;
            Vector3 desired = pivot - heading * dist + Vector3.up * height;

            // Never let the camera sink into the lawn or clip a fence post.
            desired = ResolveCollision(pivot + Vector3.up * lookHeight, desired);

            _smoothPos = Vector3.Lerp(_smoothPos, desired, 1f - Mathf.Exp(-dt / Mathf.Max(positionLag, 1e-3f)));

            Vector3 lookTarget = pivot + Vector3.up * lookHeight + flatVel * lookAhead
                               + heading * lookForward;

            // Bias the aim toward the goose, and only toward it. The mower stays the subject of the
            // shot — this slides the aim point a bounded fraction of the way, weighted up while the
            // bird is close enough to matter. Framing the goose outright was the obvious version and
            // it is wrong: the player steers by what is centred, so handing the centre to something
            // else is taking the controls away at the exact moment they need them.
            if (_defenceSubject != null)
            {
                Vector3 toGoose = _defenceSubject.position - lookTarget;
                float near = 1f - Mathf.Clamp01(toGoose.magnitude / Mathf.Max(defenceAimNearDistance, 0.1f));
                float bias = defenceAimBias * (0.45f + 0.55f * near) * _defenceBiasScale;
                lookTarget += toGoose * bias;
            }

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
            wantFov += defenceFovWiden * _energySmoothed;
            _smoothFov = Mathf.Lerp(_smoothFov, wantFov, 1f - Mathf.Exp(-fovLag * dt));

            pos = _smoothPos; rot = _smoothRot; fov = _smoothFov;

            // The punch is applied to the OUTPUT and never written back into the smoothed state, so
            // it is crisp and self-cancelling. Feeding it through positionLag and fovLag instead
            // smeared a 140 ms impact into half a second of drift, which reads as the camera sagging
            // rather than as a hit.
            if (_punch > 0.001f)
            {
                pos += rot * Vector3.forward * (punchDolly * _punch);
                fov -= punchFov * _punch;
            }
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

        /// <summary>
        /// Give the chase camera something to make room for, without giving it something to follow.
        ///
        /// <paramref name="energy01"/> is how far into a rally the phase is: it lengthens the boom,
        /// lifts a little and widens the lens, so a long rally visibly winds up while remaining the
        /// same shot. Pass null to put Chase back exactly as it was.
        /// </summary>
        /// <param name="biasScale">
        /// Scales <see cref="defenceAimBias"/> for this subject. Exists because the phase has two
        /// kinds of subject with different claims on the frame: a goose in flight is the thing being
        /// timed and wants the full bias, while the opponent's plot across the way only needs to be
        /// KEPT IN SHOT between exchanges. Giving the far plot the full bias pitched the camera so far
        /// up the field that the duck sat on the bottom edge of frame.
        /// </param>
        public void SetDefenceSubject(Transform subject, float energy01, float biasScale = 1f)
        {
            _defenceSubject = subject;
            _defenceEnergy = Mathf.Clamp01(energy01);
            _rallyEnergy = _defenceEnergy;
            _defenceBiasScale = Mathf.Clamp(biasScale, 0f, 2f);
        }

        /// <summary>
        /// How busy the board is, 0..1, independent of whether anything is aimed at the player.
        ///
        /// The only thing the rally is allowed to say about framing besides offering a bias subject.
        /// It opens the shot — boom, height, lens — and cannot rotate the camera, cannot pick a
        /// target and cannot reach the pose. That asymmetry is the whole camera contract: the rig
        /// owns where the lens is and what it is pointed at, and the mode owns nothing but how much
        /// room to leave.
        /// </summary>
        public void SetRallyEnergy(float energy01) => _rallyEnergy = Mathf.Clamp01(energy01);

        public void ClearDefenceSubject()
        {
            _defenceSubject = null;
            _defenceEnergy = 0f;
            _rallyEnergy = 0f;
            _punch = 0f;
        }

        /// <summary>
        /// A short dolly-in and lens narrow, for an impact. Separate from <see cref="AddShake"/>:
        /// shake says "something rattled the camera", a punch says "that connected". Stacking them
        /// on the same hit is what makes a parry land.
        /// </summary>
        public void AddPunch(float amount) => _punch = Mathf.Min(1.4f, _punch + amount);

        /// <summary>
        /// The live punch envelope, 0..1.4. Exposed for measurement only.
        ///
        /// A perfect parry adds 1.0 and punchDolly is 1.5 m, so the camera should dolly about 1.4 m in on
        /// the frame after contact. A frame-by-frame capture measured 0.05 m. Reading this alongside the
        /// distance separates the two possible causes — the envelope never arriving, or arriving and not
        /// being applied — which is the difference between a wiring fault and a maths fault.
        /// </summary>
        public float PunchAmount => _punch;

        Transform _defenceSubject;
        float _defenceEnergy;
        float _rallyEnergy;
        /// <summary>Energy, eased. Stepping it raw made a goose being served pop the boom out 2.6 m
        /// in one frame, which reads as a cut rather than as the shot making room.</summary>
        float _energySmoothed;
        float _defenceBiasScale = 1f;
        float _punch;

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

        // ------------------------------------------------------------------ the memory beat

        [Header("Venue preview (the study beat)")]
        [Tooltip("Height over the venue for the pre-round study shot. Must fit all four plots.")]
        public float previewHeight = 186f;
        [Tooltip("How far south of the venue centre the preview sits, so the shot has some rake.")]
        public float previewBack = 46f;
        public float previewFov = 46f;
        [Tooltip("Degrees per second the preview orbits, so the study beat is not a still frame.")]
        public float previewDrift = 2.2f;

        [Header("Aerial check")]
        [Tooltip("Height of the one-per-round lift over the player's own lawn.")]
        public float aerialHeight = 74f;
        public float aerialBack = 16f;
        public float aerialFov = 46f;

        /// <summary>
        /// The whole championship ground from above, which is the only time it is ever seen as one
        /// thing. Derived from <see cref="Venue"/> rather than hand-placed, so adding a fifth plot
        /// reframes the shot instead of cropping it.
        /// </summary>
        void ComputeVenuePreview(out Vector3 pos, out Quaternion rot, out float fov)
        {
            Vector2 lo = Venue.Plots[0].centre, hi = lo;
            foreach (var p in Venue.Plots)
            {
                lo = Vector2.Min(lo, p.centre - Vector2.one * p.Half);
                hi = Vector2.Max(hi, p.centre + Vector2.one * p.Half);
            }
            Vector2 c = (lo + hi) * 0.5f;
            Vector3 centre = new Vector3(c.x, 0f, c.y);

            // A slow orbit rather than a locked overhead. Four seconds on a static top-down frame
            // reads as a loading screen; a drifting one reads as a helicopter shot and gives the
            // picture a moment of parallax to be read against.
            float ang = _clock * previewDrift * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Sin(ang) * previewBack, previewHeight,
                                         -Mathf.Cos(ang) * previewBack);
            pos = centre + offset;
            rot = Quaternion.LookRotation((centre - pos).normalized, Vector3.up);
            fov = previewFov;
            _smoothPos = pos; _smoothRot = rot; _smoothFov = fov;
        }

        /// <summary>
        /// Straight up over the player's own lawn. Centred on the field rather than on the mower,
        /// because the point of the lift is to see the whole of what you have drawn — and the mower
        /// is in shot anyway, which is what tells you where you are standing in it.
        /// </summary>
        void ComputeAerial(out Vector3 pos, out Quaternion rot, out float fov)
        {
            Vector3 centre = Vector3.zero;
            pos = centre + new Vector3(0f, aerialHeight, -aerialBack);
            rot = Quaternion.LookRotation((centre - pos).normalized, Vector3.up);
            fov = aerialFov;
            _smoothPos = pos; _smoothRot = rot; _smoothFov = fov;
        }

        // ------------------------------------------------------------------ the ceremony

        [Header("Trophy (the championship ceremony)")]
        [Tooltip("Centre of the trophy on its plinth. Matches where the environment builder stands it.")]
        public Vector3 trophySubject = new Vector3(-6.2f, 1.22f, -37.5f);
        [Tooltip("Where the shot starts, relative to the trophy. Positive z is the lawn side, so the " +
                 "judges' bench sits behind the prize rather than out of frame.")]
        public Vector3 trophyOffset = new Vector3(0.72f, 0.44f, 2.55f);
        [Tooltip("Metres the shot closes over the beat. A prize is dollied in on, never held static.")]
        public float trophyPushIn = 0.8f;
        public float trophyPushSeconds = 3.6f;
        public float trophyFov = 27f;

        /// <summary>
        /// The trophy, close, with the panel behind it.
        ///
        /// Framed off the prop's authored position rather than off a transform found in the scene:
        /// the trophy is baked into a combined clutter mesh, so there is nothing in the hierarchy to
        /// point a camera at. Both numbers therefore have to agree with DuckEnvironmentBuilder, and
        /// that is the one thing to check if this shot ever comes back looking at bare grass.
        /// </summary>
        void ComputeTrophy(out Vector3 pos, out Quaternion rot, out float fov)
        {
            float t = Mathf.Clamp01((_clock - _modeEnteredAt) / Mathf.Max(trophyPushSeconds, 0.01f));
            Vector3 offset = trophyOffset;
            offset.z = Mathf.Max(0.6f, offset.z - trophyPushIn * SmoothStep(t));

            pos = trophySubject + offset;
            rot = Quaternion.LookRotation((trophySubject - pos).normalized, Vector3.up);
            fov = trophyFov;
            _smoothPos = pos; _smoothRot = rot; _smoothFov = fov;
        }
    }
}
