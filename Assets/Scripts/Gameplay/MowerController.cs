using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Arcade mower physics. Suspension is real forces (so the machine rides bumps, leans into
    /// turns and dives under braking), but drive, steering and grip are velocity-level so the
    /// handling stays predictable and tunable.
    ///
    /// The design tension the whole game rests on: yaw rate falls off hard with speed, and boost
    /// cuts it further. Going fast is always the wrong choice for accuracy, and that has to be
    /// felt in the hands rather than explained in the UI.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class MowerController : MonoBehaviour
    {
        [Header("Speed")]
        public float maxSpeed = 10.0f;
        public float reverseSpeed = 4.2f;
        public float accelRate = 14f;
        public float brakeRate = 26f;
        public float coastRate = 4.5f;
        public float overspeedBleed = 6f;

        [Header("Steering")]
        [Tooltip("Degrees per second of yaw available at a standstill.")]
        public float yawRateLow = 165f;
        [Tooltip("Degrees per second of yaw available at top speed. The accuracy tax.")]
        public float yawRateHigh = 88f;
        [Tooltip("How fast the mower can change its yaw rate. Lower feels heavier.")]
        public float yawAcceleration = 16f;
        [Tooltip("Speed below which steering has no authority, in m/s.")]
        public float steerCutIn = 0.8f;

        [Header("Grip")]
        [Range(0f, 1f)] public float grip = 0.34f;
        [Range(0f, 1f)] public float driftGrip = 0.035f;
        [Tooltip("Extra yaw authority while drifting.")]
        public float driftYawBonus = 1.45f;
        [Tooltip("Speed cost per second of drifting.")]
        public float driftDrag = 2.6f;

        [Header("Boost")]
        public float boostSpeedMultiplier = 1.42f;
        public float boostAccelMultiplier = 1.9f;
        [Range(0.2f, 1f)] public float boostYawMultiplier = 0.68f;
        public float boostDrainPerSecond = 0.42f;
        [Tooltip("Boost gained per second when mowing a full swath of uncut grass at speed.")]
        public float boostRefillRate = 0.30f;
        public float boostMinimumToStart = 0.12f;

        [Header("Suspension")]
        public float suspensionRest = 0.30f;
        public float suspensionTravel = 0.16f;
        public float suspensionStiffness = 24000f;
        public float suspensionDamping = 2400f;
        public Vector3 wheelbase = new Vector3(0.40f, 0f, 0.52f);
        public LayerMask groundMask = ~0;

        [Header("Cutting")]
        [Tooltip("Local offset of the cutting deck centre from the mower origin.")]
        public Vector3 deckOffset = new Vector3(0f, 0.02f, 0.62f);
        public float deckWidth = 1.20f;
        public float minCuttingSpeed = 0.35f;
        public float trackWidth = 0.20f;

        [Header("Body")]
        public float gravityScale = 2.1f;
        public float maxAirborneTime = 1.5f;
        [Tooltip("Below this height the mower is considered lost and is put back on the lawn.")]
        public float fallResetHeight = -3f;

        // ---- live state, read by visuals, audio, camera, HUD ----
        public float ForwardSpeed { get; private set; }
        public float SpeedFraction => Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / maxSpeed);
        public float SlipAngle { get; private set; }
        public bool IsGrounded { get; private set; }
        public bool IsDrifting { get; private set; }
        public bool IsBoosting { get; private set; }
        public float BoostFuel { get; private set; } = 1f;
        /// <summary>0..1, how much uncut grass the blade removed on the last physics step.</summary>
        public float CutLoad { get; private set; }
        public float SmoothedCutLoad { get; private set; }
        public bool BladeEngaged { get; private set; }
        public float EngineRpm01 { get; private set; }
        public Vector3 DeckWorldPosition => transform.TransformPoint(deckOffset);
        public float LastImpactStrength { get; private set; }
        public float DriftMetres { get; private set; }
        public float BoostMetres { get; private set; }
        public float DistanceTravelled { get; private set; }

        public System.Action<float, Vector3> OnImpact;

        Rigidbody _rb;
        InputReader _input;
        Vector3 _lastDeckPos;
        Vector3 _lastTrackL, _lastTrackR;
        bool _deckHistoryValid;
        float _airborneTimer;
        float _yawRate;
        readonly RaycastHit[] _hitBuffer = new RaycastHit[4];
        Vector3 _spawnPos;
        Quaternion _spawnRot;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.constraints = RigidbodyConstraints.None;
            _rb.useGravity = false;               // we apply scaled gravity ourselves
            _rb.centerOfMass = new Vector3(0f, -0.10f, 0.02f);
            _spawnPos = transform.position;
            _spawnRot = transform.rotation;
        }

        void Start() => _input = InputReader.Instance;

        /// <summary>Full reset for the instant-retry path. No scene reload, no allocation.</summary>
        public void ResetTo(Vector3 position, Quaternion rotation)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            Place(position, rotation);
            _yawRate = 0f;
            BoostFuel = 1f;
            CutLoad = 0f; SmoothedCutLoad = 0f;
            DriftMetres = 0f; BoostMetres = 0f; DistanceTravelled = 0f;
            _deckHistoryValid = false;
            _airborneTimer = 0f;
            IsBoosting = false; IsDrifting = false;
            LastImpactStrength = 0f;
        }

        public void ResetToSpawn() => ResetTo(_spawnPos, _spawnRot);

        /// <summary>
        /// Places the mower and stops it dead, leaving the round's statistics alone.
        ///
        /// Used to stage the closing portrait. The verdict camera frames the mower wherever it
        /// happens to have stopped, which meant a run that ended near the judges' bench put the
        /// camera inside the bench and the tent — the duck was behind a wall of brown. Parking on
        /// a known patch of open lawn makes the last shot of the round the same clean portrait
        /// every time, with nothing between the lens and the duck.
        /// </summary>
        public void ParkAt(Vector3 position, Quaternion rotation)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            Place(position, rotation);
            _yawRate = 0f;
            IsBoosting = false;
            IsDrifting = false;
            _airborneTimer = 0f;
        }

        /// <summary>
        /// Move the mower, and move the RIGIDBODY with it.
        ///
        /// Setting only the transform does not survive: this body is physics-driven and
        /// interpolated, so the next step restores the pose the rigidbody still believes in and
        /// the mower snaps back to wherever it was. Every round therefore began at the mower's
        /// original scene position instead of on the picture — outside every shape, no outline
        /// anywhere near, nothing scoring for the whole round.
        ///
        /// This hid for a long time because the capture harness steps physics by hand
        /// (Physics.simulationMode = Script), so nothing was there to overwrite the transform and
        /// every screenshot showed the mower exactly where it was asked to be. The bug only
        /// existed when the game was actually being played.
        /// </summary>
        void Place(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            _rb.position = position;
            _rb.rotation = rotation;
            // Interpolation keeps a history of previous poses to blend between; without this the
            // mower visibly streaks across the field from its old position to the new one.
            var interp = _rb.interpolation;
            _rb.interpolation = RigidbodyInterpolation.None;
            Physics.SyncTransforms();
            _rb.interpolation = interp;
        }

        void FixedUpdate()
        {
            if (SimClock.Scripted) return;
            TickPhysics(Time.fixedDeltaTime);
        }

        public void TickPhysics(float dt)
        {
            if (_input == null) _input = InputReader.Instance;

            float steer = _input != null ? _input.Steer : 0f;
            float throttle = _input != null ? _input.Throttle : 0f;
            bool handbrake = _input != null && _input.Handbrake;
            bool wantBoost = _input != null && _input.Boost;

            ApplySuspension(dt);
            _rb.AddForce(Physics.gravity * gravityScale, ForceMode.Acceleration);

            Vector3 fwd = transform.forward;
            Vector3 right = transform.right;
            Vector3 vel = _rb.linearVelocity;

            ForwardSpeed = Vector3.Dot(vel, fwd);
            float lateral = Vector3.Dot(vel, right);
            Vector3 flatVel = new Vector3(vel.x, 0f, vel.z);
            SlipAngle = flatVel.magnitude > 0.5f
                ? Vector3.SignedAngle(fwd, flatVel.normalized, Vector3.up)
                : 0f;

            HandleBoost(wantBoost, throttle, dt);

            if (IsGrounded)
            {
                ApplyDrive(throttle, dt);
                ApplyGrip(handbrake, right, lateral, dt);
                ApplySteering(steer, handbrake, dt);
            }
            else
            {
                // A little air control so a jump off the ramp is not a coin flip.
                _yawRate = Mathf.MoveTowards(_yawRate, steer * 40f * Mathf.Deg2Rad, 3f * dt);
                var av = _rb.angularVelocity; av.y = _yawRate; _rb.angularVelocity = av;
            }

            UpdateCutting(dt);
            UpdateTelemetry(dt);
        }

        // ------------------------------------------------------------------ suspension

        void ApplySuspension(float dt)
        {
            bool anyGrounded = false;
            for (int i = 0; i < 4; i++)
            {
                float sx = (i == 0 || i == 2) ? -1f : 1f;
                float sz = (i < 2) ? 1f : -1f;
                Vector3 localPos = new Vector3(wheelbase.x * sx, wheelbase.y, wheelbase.z * sz);
                Vector3 worldPos = transform.TransformPoint(localPos);
                Vector3 up = transform.up;

                float maxLen = suspensionRest + suspensionTravel;
                if (Physics.Raycast(worldPos, -up, out RaycastHit hit, maxLen, groundMask, QueryTriggerInteraction.Ignore))
                {
                    anyGrounded = true;
                    float compression = 1f - Mathf.Clamp01(hit.distance / maxLen);
                    Vector3 pointVel = _rb.GetPointVelocity(worldPos);
                    float upVel = Vector3.Dot(pointVel, up);

                    float force = suspensionStiffness * compression - suspensionDamping * upVel;
                    force = Mathf.Max(force, 0f);
                    _rb.AddForceAtPosition(up * force, worldPos);
                }
            }

            IsGrounded = anyGrounded;
            _airborneTimer = anyGrounded ? 0f : _airborneTimer + dt;

            // Absolute floor. Any hole in the ground collision — a missing collider, a gap
            // between two meshes — used to mean falling forever with no way back. Catch it and
            // put the mower down on the lawn instead.
            if (transform.position.y < fallResetHeight)
            {
                Vector3 safe = Field.ClampToPlayfield(transform.position, 4f);
                safe.y = 1.2f;
                transform.position = safe;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _airborneTimer = 0f;
                _deckHistoryValid = false;
                return;
            }

            // Safety net: if we somehow end up airborne or inverted for too long, right the mower.
            if (_airborneTimer > maxAirborneTime || Vector3.Dot(transform.up, Vector3.up) < -0.2f)
            {
                Vector3 p = transform.position; p.y = Mathf.Max(p.y, 0.45f);
                Vector3 flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
                transform.SetPositionAndRotation(p, Quaternion.LookRotation(flat.normalized, Vector3.up));
                _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z) * 0.4f;
                _rb.angularVelocity = Vector3.zero;
                _airborneTimer = 0f;
            }
        }

        // ------------------------------------------------------------------ drive

        void ApplyDrive(float throttle, float dt)
        {
            float topSpeed = maxSpeed * (IsBoosting ? boostSpeedMultiplier : 1f);
            float targetSpeed;
            float rate;

            if (throttle > 0.01f)
            {
                targetSpeed = topSpeed * throttle;
                rate = ForwardSpeed < targetSpeed
                    ? accelRate * (IsBoosting ? boostAccelMultiplier : 1f)
                    : overspeedBleed;
            }
            else if (throttle < -0.01f)
            {
                // Same key brakes then reverses, which is what everyone expects from a driving game.
                if (ForwardSpeed > 0.4f) { targetSpeed = 0f; rate = brakeRate; }
                else { targetSpeed = -reverseSpeed * -throttle; rate = accelRate * 0.55f; }
            }
            else
            {
                targetSpeed = 0f;
                rate = coastRate;
            }

            float newForward = Mathf.MoveTowards(ForwardSpeed, targetSpeed, rate * dt);
            _rb.linearVelocity += transform.forward * (newForward - ForwardSpeed);
        }

        void ApplyGrip(bool handbrake, Vector3 right, float lateral, float dt)
        {
            IsDrifting = handbrake && Mathf.Abs(ForwardSpeed) > 2.5f;

            float g = IsDrifting ? driftGrip : grip;
            // Grip eases off with speed so fast cornering already feels loose before you drift.
            g *= Mathf.Lerp(1f, 0.72f, SpeedFraction);

            float removed = lateral * (1f - Mathf.Pow(1f - g, dt * 60f));
            _rb.linearVelocity -= right * removed;

            if (IsDrifting)
            {
                float bleed = Mathf.Min(driftDrag * dt, Mathf.Abs(ForwardSpeed));
                _rb.linearVelocity -= transform.forward * (bleed * Mathf.Sign(ForwardSpeed));
            }
        }

        void ApplySteering(float steer, bool handbrake, float dt)
        {
            float speedFrac = Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / maxSpeed);
            float yawRateDeg = Mathf.Lerp(yawRateLow, yawRateHigh, speedFrac * speedFrac);

            if (IsBoosting) yawRateDeg *= boostYawMultiplier;
            if (IsDrifting) yawRateDeg *= driftYawBonus;

            float authority = Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / steerCutIn);
            float dirSign = ForwardSpeed < -0.2f ? -1f : 1f;

            float target = steer * yawRateDeg * Mathf.Deg2Rad * authority * dirSign;

            // While drifting, nudge the yaw target toward the direction we are actually sliding.
            // Without this a drift is a spin; with it, it is a tool.
            if (IsDrifting)
            {
                float slipRad = SlipAngle * Mathf.Deg2Rad;
                target += Mathf.Clamp(slipRad, -0.6f, 0.6f) * 0.9f;
            }

            _yawRate = Mathf.MoveTowards(_yawRate, target, yawAcceleration * dt);
            Vector3 av = _rb.angularVelocity;
            av.y = _yawRate;
            _rb.angularVelocity = av;
        }

        void HandleBoost(bool wantBoost, float throttle, float dt)
        {
            bool canBoost = wantBoost && throttle > 0.05f && BoostFuel > 0.001f
                            && (IsBoosting || BoostFuel > boostMinimumToStart);

            IsBoosting = canBoost;
            if (IsBoosting)
            {
                BoostFuel = Mathf.Max(0f, BoostFuel - boostDrainPerSecond * dt);
                BoostMetres += Mathf.Abs(ForwardSpeed) * dt;
            }
        }

        // ------------------------------------------------------------------ cutting

        void UpdateCutting(float dt)
        {
            var mask = CutMask.Instance;
            Vector3 deck = DeckWorldPosition;
            deck.y = 0f;

            Vector3 halfAxle = transform.right * (wheelbase.x);
            Vector3 rear = transform.TransformPoint(new Vector3(0f, 0f, -wheelbase.z));
            rear.y = 0f;
            Vector3 trackL = rear - halfAxle;
            Vector3 trackR = rear + halfAxle;

            BladeEngaged = Mathf.Abs(ForwardSpeed) > minCuttingSpeed && IsGrounded;

            if (!_deckHistoryValid)
            {
                _lastDeckPos = deck; _lastTrackL = trackL; _lastTrackR = trackR;
                _deckHistoryValid = true;
                CutLoad = 0f;
                return;
            }

            float fresh = 0f;
            if (mask != null && BladeEngaged)
            {
                fresh = mask.CutSwath(_lastDeckPos, deck, deckWidth);
                mask.PressTrack(_lastTrackL, trackL, trackWidth);
                mask.PressTrack(_lastTrackR, trackR, trackWidth);
            }

            // Scale by how far we actually moved: creeping forward should not spray clippings.
            float travel = Vector3.Distance(_lastDeckPos, deck);
            CutLoad = fresh * Mathf.Clamp01(travel / (Mathf.Max(maxSpeed, 1f) * dt));
            SmoothedCutLoad = Mathf.Lerp(SmoothedCutLoad, CutLoad, 1f - Mathf.Exp(-9f * dt));

            if (CutLoad > 0.02f)
                BoostFuel = Mathf.Min(1f, BoostFuel + CutLoad * boostRefillRate * dt);

            _lastDeckPos = deck; _lastTrackL = trackL; _lastTrackR = trackR;
            DistanceTravelled += travel;
            if (IsDrifting) DriftMetres += travel;
        }

        void UpdateTelemetry(float dt)
        {
            float load = 0.20f + SpeedFraction * 0.55f + SmoothedCutLoad * 0.30f;
            if (IsBoosting) load += 0.22f;
            if (!IsGrounded) load += 0.12f;
            EngineRpm01 = Mathf.Lerp(EngineRpm01, Mathf.Clamp01(load), 1f - Mathf.Exp(-7f * dt));
        }

        void OnCollisionEnter(Collision c)
        {
            float impulse = c.impulse.magnitude / Mathf.Max(_rb.mass, 1f);
            if (impulse < 0.35f) return;
            LastImpactStrength = Mathf.Clamp01(impulse / 6f);
            OnImpact?.Invoke(LastImpactStrength, c.GetContact(0).point);
        }

        /// <summary>Called by the game director so the mower coasts to a stop when time runs out.</summary>
        public void CutEngine()
        {
            BladeEngaged = false;
            IsBoosting = false;
        }
    }
}
