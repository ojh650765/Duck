using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Everything the mower does that is not physics: wheels turning, the chassis leaning and
    /// diving, the blade spinning up, the duck reacting, and a squash-and-stretch punch when
    /// something gets hit.
    ///
    /// All of it is driven off the controller's live state and applied to a visual pivot, so it
    /// can be as exaggerated as it likes without ever lying about where the collider is.
    /// </summary>
    public class MowerVisuals : MonoBehaviour
    {
        [Header("Source")]
        public MowerController mower;

        [Header("Rig")]
        [Tooltip("Child that carries all the lean and squash. Never the collider's transform.")]
        public Transform visualPivot;
        public Transform wheelFL, wheelFR, wheelRL, wheelRR;
        public Transform steeringColumn;
        public Transform bladeSpinner;
        public Transform exhaust;
        public Transform catcherBag;

        [Header("Duck")]
        public Transform duckRoot;
        public Transform duckHead;
        public Transform duckWingL, duckWingR;
        public Transform duckTail;

        [Header("Wheels")]
        public float frontWheelRadius = 0.15f;
        public float rearWheelRadius = 0.22f;
        public float maxVisualSteerAngle = 26f;

        [Header("Chassis")]
        public float rollPerYawRate = 7.5f;
        public float pitchPerAccel = 1.1f;
        public float maxRoll = 11f;
        public float maxPitch = 7f;
        public float chassisResponse = 9f;
        public float engineShakeAmount = 0.006f;

        [Header("Blade")]
        public float bladeSpinSpeed = 2600f;

        [Header("Duck motion")]
        public float duckLeanIntoTurn = 13f;
        public float duckBobAmount = 0.016f;
        public float duckLookAhead = 18f;

        float _clock;
        float _wheelSpinF, _wheelSpinR;
        float _bladeAngle;
        float _roll, _pitch, _squash;
        float _lastSpeed;
        Vector3 _pivotBase;
        Quaternion _steerBase, _duckBase, _headBase, _wingLBase, _wingRBase, _tailBase, _bagBase;
        Vector3 _duckPosBase;

        void Awake()
        {
            if (mower == null) mower = GetComponentInParent<MowerController>();
            if (visualPivot != null)
            {
                _pivotBase = visualPivot.localPosition;
                _pivotBase.y += GroundOffset();
                visualPivot.localPosition = _pivotBase;
            }
            if (steeringColumn != null) _steerBase = steeringColumn.localRotation;
            if (duckRoot != null) { _duckBase = duckRoot.localRotation; _duckPosBase = duckRoot.localPosition; }
            if (duckHead != null) _headBase = duckHead.localRotation;
            if (duckWingL != null) _wingLBase = duckWingL.localRotation;
            if (duckWingR != null) _wingRBase = duckWingR.localRotation;
            if (duckTail != null) _tailBase = duckTail.localRotation;
            if (catcherBag != null) _bagBase = catcherBag.localRotation;

            if (mower != null) mower.OnImpact += OnImpact;
        }

        void OnDestroy()
        {
            if (mower != null) mower.OnImpact -= OnImpact;
        }

        /// <summary>
        /// How far to drop the model so its wheels meet the lawn.
        ///
        /// The rigidbody's origin is not on the ground — it hangs at the suspension's resting
        /// ride height, which is where the wheel raycasts start. The authored mower, by contrast,
        /// has its wheels touching its own origin. Without this the machine floats a hand's width
        /// above the grass it is supposedly cutting, and the cut appears somewhere the mower
        /// visibly is not.
        /// </summary>
        float GroundOffset()
        {
            if (mower == null) return 0f;
            var rb = mower.GetComponent<Rigidbody>();
            float mass = rb != null ? rb.mass : 180f;
            float maxLen = mower.suspensionRest + mower.suspensionTravel;

            // Static compression, as the fraction of travel the springs give up under gravity.
            float load = mass * Mathf.Abs(Physics.gravity.y) * mower.gravityScale;
            float compression = Mathf.Clamp01(load / Mathf.Max(4f * mower.suspensionStiffness, 1e-3f));
            return -maxLen * (1f - compression);
        }

        void OnImpact(float strength, Vector3 point) => _squash = Mathf.Max(_squash, strength);

        void Update()
        {
            if (SimClock.Scripted) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            if (mower == null) return;
            if (dt <= 0f) return;
            _clock += dt;

            float speed = mower.ForwardSpeed;
            float accel = (speed - _lastSpeed) / dt;
            _lastSpeed = speed;

            var rb = mower.GetComponent<Rigidbody>();
            float yawRate = rb != null ? rb.angularVelocity.y : 0f;
            // This machine's own controls, not the keyboard's.
            //
            // It read InputReader directly, which is right for the one mower the player is sitting
            // on and wrong the moment there are four on a pitch: three opponents' wheels, steering
            // columns and drivers all leaned whichever way the PLAYER was steering, in perfect
            // unison, while the machines underneath them drove somewhere else entirely.
            float steerInput = mower != null ? mower.VisualSteer : 0f;

            UpdateWheels(speed, steerInput, dt);
            UpdateChassis(yawRate, accel, dt);
            UpdateBlade(dt);
            UpdateDuck(steerInput, speed, yawRate, dt);
            UpdateBag(accel, yawRate, dt);
        }

        void UpdateWheels(float speed, float steerInput, float dt)
        {
            _wheelSpinF += speed / Mathf.Max(frontWheelRadius, 0.01f) * Mathf.Rad2Deg * dt;
            _wheelSpinR += speed / Mathf.Max(rearWheelRadius, 0.01f) * Mathf.Rad2Deg * dt;

            float steerAngle = steerInput * maxVisualSteerAngle;
            SetWheel(wheelFL, _wheelSpinF, steerAngle);
            SetWheel(wheelFR, _wheelSpinF, steerAngle);
            SetWheel(wheelRL, _wheelSpinR, 0f);
            SetWheel(wheelRR, _wheelSpinR, 0f);

            if (steeringColumn != null)
            {
                // The column is raked back, so the wheel turns about its OWN shaft rather than a
                // world axis. The shaft comes from the REST pose, and it has to — this used to read
                // `steeringColumn.up`, which is the axis of the pose written on the previous frame.
                //
                // That is a feedback loop. Turn the wheel about its shaft, the shaft moves; next
                // frame the new shaft is resolved from the already-rotated pose and the rotation
                // compounds on top of itself. Hold any steering input and the wheel accelerates into
                // a full 360 and keeps going — the "propeller". Deriving the axis from _steerBase
                // makes it a fixed property of how the column is mounted, which is what it is.
                //
                // Positive steer turns the wheel to the RIGHT. It was negated, which had been
                // invisible for as long as the column was spinning — you cannot tell which way a
                // propeller is wrong. With the rotation stable the reversal is the first thing you
                // see, and a steering wheel that turns away from the direction the machine goes is
                // worse than one that does not move at all.
                Vector3 shaft = _steerBase * Vector3.up;
                steeringColumn.localRotation = Quaternion.AngleAxis(steerInput * 105f, shaft) * _steerBase;
            }
        }

        static void SetWheel(Transform wheel, float spin, float steer)
        {
            if (wheel == null) return;
            wheel.localRotation = Quaternion.Euler(0f, steer, 0f) * Quaternion.Euler(spin, 0f, 0f);
        }

        void UpdateChassis(float yawRate, float accel, float dt)
        {
            if (visualPivot == null) return;

            float targetRoll = Mathf.Clamp(-yawRate * rollPerYawRate, -maxRoll, maxRoll);
            // Drifting exaggerates the lean; that is most of what sells a slide.
            if (mower.IsDrifting) targetRoll *= 1.5f;

            float targetPitch = Mathf.Clamp(-accel * pitchPerAccel, -maxPitch, maxPitch);
            if (mower.IsBoosting) targetPitch -= 2.2f;

            float k = 1f - Mathf.Exp(-chassisResponse * dt);
            _roll = Mathf.Lerp(_roll, targetRoll, k);
            _pitch = Mathf.Lerp(_pitch, targetPitch, k);
            _squash = Mathf.Max(0f, _squash - dt * 2.6f);

            // Engine idle buzz: tiny, high frequency, scales with revs. Reads as a running motor.
            float buzz = Mathf.Sin(_clock * 62f) * engineShakeAmount * (0.35f + mower.EngineRpm01);
            float bump = mower.IsGrounded ? 0f : 0.02f;

            visualPivot.localPosition = _pivotBase + new Vector3(0f, buzz + bump, 0f);
            visualPivot.localRotation = Quaternion.Euler(_pitch, 0f, _roll);

            float s = 1f + _squash * 0.16f;
            float f = 1f - _squash * 0.12f;
            visualPivot.localScale = new Vector3(s, f, s);
        }

        void UpdateBlade(float dt)
        {
            if (bladeSpinner == null) return;
            float target = mower.BladeEngaged ? bladeSpinSpeed : 0f;
            _bladeAngle += target * dt;
            bladeSpinner.localRotation = Quaternion.Euler(0f, _bladeAngle, 0f);
        }

        /// <summary>
        /// Rotation about a world axis, expressed in a child transform's parent space.
        ///
        /// The imported models keep their children in Blender's coordinate space with the
        /// axis conversion on the root, so a child's local X/Y/Z are not Unity's right/up/forward.
        /// Writing Quaternion.Euler(pitch, yaw, roll) onto one of those nodes puts the yaw on
        /// whatever axis happens to be second — which is how the duck ended up turning its head
        /// the wrong way when steering. Resolving the axis through the parent makes the code
        /// independent of whatever convention the asset arrived in.
        /// </summary>
        static Quaternion AboutWorldAxis(Transform node, Vector3 worldAxis, float degrees)
        {
            if (node == null || node.parent == null) return Quaternion.AngleAxis(degrees, worldAxis);
            Vector3 local = node.parent.InverseTransformDirection(worldAxis).normalized;
            return Quaternion.AngleAxis(degrees, local);
        }

        void UpdateDuck(float steerInput, float speed, float yawRate, float dt)
        {
            float t = _clock;
            Vector3 up = transform.up;
            Vector3 right = transform.right;
            Vector3 forward = transform.forward;

            if (duckRoot != null)
            {
                float lean = Mathf.Clamp(-yawRate, -2.2f, 2.2f) * duckLeanIntoTurn;
                float bob = Mathf.Sin(t * (5f + mower.EngineRpm01 * 22f)) * duckBobAmount * (0.4f + mower.EngineRpm01);

                // Lean is a roll about the direction of travel.
                var target = AboutWorldAxis(duckRoot, forward, lean) * _duckBase;
                duckRoot.localRotation = Quaternion.Slerp(duckRoot.localRotation, target, 1f - Mathf.Exp(-8f * dt));

                // Offset from the seated pose captured at startup. This used to add the bob to
                // the duck's CURRENT height every frame, which integrated into a slow upward
                // drift — after a while the duck was sitting a long way above the mower.
                duckRoot.localPosition = _duckPosBase + new Vector3(0f, bob, 0f);
            }

            if (duckHead != null)
            {
                // The duck looks where it is steering, and cranes forward at speed. Earnest.
                float look = steerInput * duckLookAhead;
                float crane = mower.SpeedFraction * 7f;
                float jolt = mower.IsGrounded ? 0f : 4f;

                var target = AboutWorldAxis(duckHead, up, look)
                           * AboutWorldAxis(duckHead, right, crane + jolt)
                           * _headBase;
                duckHead.localRotation = Quaternion.Slerp(duckHead.localRotation, target,
                                                          1f - Mathf.Exp(-11f * dt));
            }

            // Wings grip the wheel and shuffle with the steering input.
            float wingSwing = steerInput * 16f;
            if (duckWingL != null)
            {
                var target = AboutWorldAxis(duckWingL, right, wingSwing) * _wingLBase;
                duckWingL.localRotation = Quaternion.Slerp(duckWingL.localRotation, target, 1f - Mathf.Exp(-12f * dt));
            }
            if (duckWingR != null)
            {
                var target = AboutWorldAxis(duckWingR, right, -wingSwing) * _wingRBase;
                duckWingR.localRotation = Quaternion.Slerp(duckWingR.localRotation, target, 1f - Mathf.Exp(-12f * dt));
            }

            if (duckTail != null)
            {
                float flick = Mathf.Sin(t * 3.4f) * 4f + (mower.IsDrifting ? Mathf.Sin(t * 19f) * 9f : 0f);
                duckTail.localRotation = AboutWorldAxis(duckTail, right, flick) * _tailBase;
            }
        }

        void UpdateBag(float accel, float yawRate, float dt)
        {
            if (catcherBag == null) return;
            float swingX = Mathf.Clamp(accel * 0.8f, -14f, 14f);
            float swingZ = Mathf.Clamp(-yawRate * 6f, -12f, 12f);
            catcherBag.localRotation = Quaternion.Slerp(catcherBag.localRotation,
                _bagBase * Quaternion.Euler(swingX, 0f, swingZ), 1f - Mathf.Exp(-6f * dt));
        }
    }
}
