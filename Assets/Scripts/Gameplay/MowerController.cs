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
        // Defaulted from MowerContact, not typed here. The environment builder has to know how high
        // this machine rides before one exists — that ride height is what decides which props can be
        // hit at all — so the numbers live in one place and this end takes them from there. See the
        // note at the top of MowerContact.cs.
        public float suspensionRest = MowerContact.SuspensionRest;
        public float suspensionTravel = MowerContact.SuspensionTravel;
        public float suspensionStiffness = MowerContact.SuspensionStiffness;
        public float suspensionDamping = 2400f;
        [Tooltip("Angular velocity added about the machine's lateral axis when it is struck, so the nose " +
                 "rocks up and the springs compress. Zero means an impact the chassis does not feel.")]
        [Range(0f, 6f)] public float bonkPitchKick = 2.6f;
        [Tooltip("The same about its long axis, for a blow that lands off-centre.")]
        [Range(0f, 6f)] public float bonkRollKick = 1.9f;
        public Vector3 wheelbase = new Vector3(0.40f, 0f, 0.52f);
        public LayerMask groundMask = ~0;

        [Header("Cutting")]
        [Tooltip("Local offset of the cutting deck centre from the mower origin.")]
        public Vector3 deckOffset = new Vector3(0f, 0.02f, 0.62f);
        public float deckWidth = 1.20f;
        public float minCuttingSpeed = 0.35f;
        public float trackWidth = 0.20f;

        [Header("Impacts")]
        [Tooltip("Seconds the drive is cut after a solid hit. Long enough to feel, short enough " +
                 "that it never reads as the controls having stopped working.")]
        public float staggerDuration = 0.30f;
        [Tooltip("Fraction of forward speed kept through a solid hit.")]
        [Range(0f, 1f)] public float staggerSpeedKeep = 0.18f;
        [Tooltip("Metres per second of shove away from whatever was hit.")]
        public float staggerRebound = 1.8f;

        [Header("Body")]
        public float gravityScale = MowerContact.GravityScale;
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

        /// <summary>
        /// Deck up: the machine drives but cuts nothing.
        ///
        /// For the beats where the player is driving for a reason other than mowing. Distinct from
        /// <see cref="CutEngine"/>, which stops the engine and is recomputed by the next physics step
        /// anyway — this is a standing instruction that survives until it is lifted. Without it a
        /// player driving after the klaxon spins a blade, throws clippings and runs the shredding
        /// audio layer while doing no work, which reads as the machine being broken.
        /// </summary>
        public bool BladeLocked { get; set; }

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
        float _staggerTimer;
        readonly RaycastHit[] _hitBuffer = new RaycastHit[4];
        bool _rideVerified;
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
            _staggerTimer = 0f;
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

            _staggerTimer = Mathf.Max(0f, _staggerTimer - dt);

            if (IsGrounded)
            {
                // Grip and steering stay live through a stagger — only the engine is out. Freezing
                // all three turned a bonk into a third of a second of dead controls, which reads as
                // a hitch in the game rather than as a hit, and the machine slid on unaffected
                // because nothing was scrubbing the lateral velocity either.
                if (_staggerTimer <= 0f) ApplyDrive(throttle, dt);
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
            float rideSum = 0f;
            int rideRays = 0;
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
                    rideSum += hit.distance;
                    rideRays++;
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

            VerifyRideHeight(rideSum, rideRays);

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

        /// <summary>
        /// Confirm, once, that the machine rides as high as MowerContact predicts.
        ///
        /// The contact band that decides which props can be hit at all is derived from the spring
        /// balance in MowerContact, at edit time, with no mower in existence. If anyone retunes the
        /// stiffness, the mass or the travel in the Inspector — or if a scene ships with an older
        /// serialised value — the band silently stops describing this mower and the builder's
        /// obstacle checks start certifying props against a machine that is not there. That is
        /// exactly how the "some obstacles are ignored" bug got in three times, so the loop is
        /// closed here rather than trusted: four springs settling on flat ground measure the ride
        /// height directly, and a disagreement is a console error naming both numbers.
        /// </summary>
        void VerifyRideHeight(float rideSum, int rideRays)
        {
            if (_rideVerified || rideRays < 4) return;
            // Only on a level, settled machine: a lean, a bump or a landing all read as a
            // disagreement that is not one.
            if (Mathf.Abs(_rb.linearVelocity.y) > 0.05f) return;
            if (Vector3.Dot(transform.up, Vector3.up) < 0.999f) return;

            _rideVerified = true;
            float measured = rideSum / rideRays;
            float predicted = MowerContact.RideHeight;
            if (Mathf.Abs(measured - predicted) > 0.03f)
                Debug.LogError(
                    $"[Duck] Mower ride height is {measured:0.000} m but MowerContact predicts " +
                    $"{predicted:0.000} m. Every obstacle in the venue was checked against " +
                    $"{MowerContact.Describe()}, so those checks are now wrong. Reconcile " +
                    $"MowerContact with this mower's mass and suspension, then rebuild the scene.");
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

            BladeEngaged = !BladeLocked && Mathf.Abs(ForwardSpeed) > minCuttingSpeed && IsGrounded;

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
            // Props that stage their own hit call Bonk directly and must not also be reported
            // here, or the same collision counts twice toward the bonk tally and fires two camera
            // shakes on the same frame.
            if (c.collider.GetComponentInParent<Gnome>() != null) return;

            float impulse = c.impulse.magnitude / Mathf.Max(_rb.mass, 1f);
            if (impulse < 0.35f) return;
            LastImpactStrength = Mathf.Clamp01(impulse / 6f);
            OnImpact?.Invoke(LastImpactStrength, c.GetContact(0).point);
        }

        /// <summary>
        /// Take a solid hit from a prop: shed speed, get shoved back, and lose the drive for a beat.
        ///
        /// Explicit rather than left to the solver, because the solver could not be relied on. A
        /// gnome wakes from kinematic to dynamic inside its own collision callback, and a body that
        /// turns dynamic mid-contact absorbs the exchange instead of resisting it — so depending on
        /// where in the step the two callbacks landed, the mower either got stopped or drove
        /// straight through as though nothing was there. That is the "sometimes it doesn't stop"
        /// report, and no amount of tuning masses or materials fixes it, because the contact the
        /// tuning would apply to has already been dissolved.
        ///
        /// So the reaction is applied here, from the prop's own callback, and cannot be skipped.
        /// </summary>
        public void Bonk(Vector3 impactPoint, float strength)
        {
            strength = Mathf.Clamp01(strength);
            _staggerTimer = Mathf.Max(_staggerTimer, staggerDuration * (0.6f + strength * 0.7f));

            Vector3 vel = _rb.linearVelocity;
            Vector3 fwd = transform.forward;

            // Scrub the component of travel along the machine's own heading. Scaling the whole
            // velocity instead would leave a fast sideways drift running, which reads as sliding
            // past the gnome rather than hitting it.
            vel -= fwd * (Vector3.Dot(vel, fwd) * (1f - staggerSpeedKeep));

            Vector3 away = transform.position - impactPoint;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = -fwd;
            vel += away.normalized * (staggerRebound * (0.7f + strength));

            _rb.linearVelocity = vel;

            // A twist as well, so a glancing blow knocks the machine off line. Without it a bonk
            // only costs time, and the interesting cost is the scar it leaves across the picture.
            float spin = (UnityEngine.Random.value < 0.5f ? -1f : 1f) * (1.1f + strength * 2.0f);
            _rb.AddTorque(Vector3.up * spin, ForceMode.VelocityChange);

            // Rock the chassis, and do it with TORQUE rather than by animating a visual offset.
            //
            // This was missing entirely, and a frame-by-frame capture is what found it: twelve
            // consecutive frames across a parry had the machine's pitch and roll pinned at +0.1 and 0.0
            // the whole way through. The camera kicked, the goose squashed, the debris flew — and the
            // mower itself did not so much as nod. "Suspension recoil and tire compression" had nothing
            // behind it at all.
            //
            // Torque rather than a scripted lean because the suspension here is four real spring
            // raycasts: shove the body and the springs compress, fight back and settle on their own, so
            // the recoil has the machine's actual mass and damping in it. A scripted lean would have to
            // reproduce all of that and would fight the springs while doing it.
            Vector3 from = -away.normalized;
            // Head-on lifts the nose; a side-swipe rolls it away from the blow. Both fall out of where
            // the hit came from, so a glancing goose reads differently from one straight up the front.
            float head = Mathf.Clamp01(Vector3.Dot(from, fwd));
            float lateral = Vector3.Dot(from, transform.right);

            _rb.AddTorque(-transform.right * (bonkPitchKick * strength * (0.35f + head)),
                          ForceMode.VelocityChange);
            _rb.AddTorque(transform.forward * (bonkRollKick * strength * lateral),
                          ForceMode.VelocityChange);

            LastImpactStrength = strength;
            OnImpact?.Invoke(strength, impactPoint);
        }

        /// <summary>Called by the game director so the mower coasts to a stop when time runs out.</summary>
        public void CutEngine()
        {
            BladeEngaged = false;
            IsBoosting = false;
        }
    }
}
