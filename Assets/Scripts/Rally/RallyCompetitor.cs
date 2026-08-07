using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// One of the four. Owns a mower, a garden and a strip of dirt, and knows how to swing at a
    /// goose — the same way whoever is driving.
    ///
    /// There is deliberately no player subclass and no bot subclass. The player differs from the
    /// three opponents in exactly one field: <see cref="brain"/> is null and the controls come from
    /// the keyboard instead. Parry geometry, timing tiers, recoil, redirect sectors, confinement and
    /// garden damage are all the same code path, so "the NPCs cheat" is not a thing anyone can
    /// suspect without also suspecting it of the player.
    /// </summary>
    [DefaultExecutionOrder(-5)]
    public class RallyCompetitor : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Which corner of the arena this competitor defends. Indexes RallyArena.")]
        public int slot;
        [Tooltip("True for the one machine the keyboard drives.")]
        public bool isPlayer;

        [Header("Wiring")]
        public MowerController mower;
        public RallyGarden garden;
        [Tooltip("The bot driving this mower. Null on the player's, and that is the only difference.")]
        public RallyBrain brain;

        [Header("Parry geometry")]
        [Tooltip("Furthest a goose can be from the deck and still be struck.")]
        public float parryRadius = 3.4f;
        [Tooltip("Half-angle of the arc in front of the machine a goose has to be inside, degrees. " +
                 "Wide, because a mower that can only hit what is dead ahead makes the whole mode " +
                 "about lining up rather than about reading the bird.")]
        [Range(30f, 150f)] public float parryHalfAngle = 78f;
        [Tooltip("Inside this, the strike is Good.")]
        public float goodRadius = 2.1f;
        [Tooltip("Inside this, the strike is Perfect: hardest launch, biggest reward, deepest hit stop.")]
        public float perfectRadius = 1.15f;
        [Tooltip("Metres in front of the machine the ideal contact point sits. Measured from the deck, " +
                 "so the sweet spot is where the blade housing actually is rather than at the axle.")]
        public float sweetSpotAhead = 1.25f;

        [Header("Confinement")]
        [Tooltip("Extra inward acceleration once the hull is past the line, on top of the return " +
                 "velocity below. Belt to that braces: it is what gets a machine that was knocked " +
                 "well outside moving again in the first place. See Confine().")]
        public float bandPush = 26f;
        [Tooltip("Metres per second squared the boundary may take off the part of the velocity " +
                 "heading OUT. Sets how far back from the line the machine starts being eased: a " +
                 "machine leaving at v is braked over v*v/(2*this) metres, so at 60 a full-speed " +
                 "run at the edge is smoothed over the last 0.8 m and arrives stopped rather than " +
                 "stopping. Nothing across the line is touched at any point.")]
        public float boundaryBrake = 60f;
        [Tooltip("How much of what the boundary absorbs is handed back as speed ALONG it, 0..1. " +
                 "0 is a wall that arrests you; 1 redirects everything it takes. Scaled by how " +
                 "glancing the machine's NOSE is against the face, so a head-on run still stops " +
                 "dead and a shallow scrape costs nothing.")]
        [Range(0f, 1f)] public float slideCarry = 0.85f;
        [Tooltip("Ceiling on that slide, m/s^2 — about twice the machine's own drive. Bounding it " +
                 "as an acceleration rather than as a share of a velocity is what makes the " +
                 "boundary feel the same at 30 fps as at 144.")]
        public float slideAccel = 30f;
        [Tooltip("Once the hull IS past the line, how fast per metre of overshoot the boundary " +
                 "insists the machine is already travelling back in, 1/s. This is what actually " +
                 "holds the line — see Confine() on why a boundary that merely forbids leaving can " +
                 "still be walked through by an engine at full throttle.")]
        public float bandReturn = 6f;
        [Tooltip("Ceiling on that return, m/s. Stops a machine flung well outside from being " +
                 "fired back across its own ground faster than it could drive.")]
        public float bandReturnMax = 2.5f;
        [Tooltip("Extra clearance past the chassis box, metres. Keeps the bodywork off the stones " +
                 "that mark the line rather than resting exactly on them.")]
        public float hullPadding = 0.05f;

        [Header("Feel")]
        [Tooltip("How hard a strike kicks the machine back through MowerController.Bonk, 0..1.")]
        [Range(0f, 1f)] public float recoilNormal = 0.34f;
        [Range(0f, 1f)] public float recoilGood = 0.58f;
        [Range(0f, 1f)] public float recoilPerfect = 0.9f;

        // ---- running totals, read by the HUD and by the result carried into judging ----
        public int Parries { get; private set; }
        public int Perfects { get; private set; }
        public int Goods { get; private set; }
        public int Normals { get; private set; }
        public int Knockouts { get; private set; }
        public int Conceded { get; private set; }
        /// <summary>Successful redirects that ended in somebody else's garden. The offensive score.</summary>
        public int Landed { get; private set; }

        public RallyArena.Slot Slot => RallyArena.Get(slot);
        public string Name => Slot.contestant;
        public Color Livery => Slot.livery;
        public bool IsPlayer => Slot.isPlayer;
        public float Integrity => garden != null ? garden.Integrity : 1f;
        public Vector3 Position => mower != null ? mower.transform.position : Slot.SpawnPosition;
        public Vector3 Heading => mower != null ? mower.transform.forward : Slot.inward;

        /// <summary>Where the strike lands if it lands: a little ahead of the machine, at goose height.</summary>
        public Vector3 SweetSpot => Position + Heading * sweetSpotAhead + Vector3.up * 0.45f;

        /// <summary>Seconds since this competitor last connected. Drives the mower's own reaction.</summary>
        public float SinceStrike { get; private set; } = 99f;
        public RallyStrike.Tier LastTier { get; private set; } = RallyStrike.Tier.Miss;

        Rigidbody _rb;

        /// <summary>
        /// Half the chassis box, x across and y along the machine. Read off the collider the solver
        /// actually uses rather than typed in, so a mower that is re-modelled cannot quietly leave
        /// the confinement enforcing the old silhouette. The fallback is the current prefab's box.
        /// </summary>
        Vector2 _hullHalf = new Vector2(0.46f, 0.725f);

        void Awake()
        {
            if (mower == null) mower = GetComponentInChildren<MowerController>();
            if (mower != null) _rb = mower.GetComponent<Rigidbody>();
            if (brain != null && mower != null) mower.inputSource = brain;

            // The chassis collider sits on the same GameObject as the body — that is the one the
            // contact solver resolves against, so it is the one the boundary has to agree with.
            if (_rb != null && _rb.TryGetComponent(out BoxCollider box))
            {
                Vector3 ls = _rb.transform.lossyScale;
                _hullHalf = new Vector2(Mathf.Abs(box.size.x * ls.x) * 0.5f,
                                        Mathf.Abs(box.size.z * ls.z) * 0.5f);
            }
        }

        public void ResetForMatch()
        {
            Parries = Perfects = Goods = Normals = Knockouts = Conceded = Landed = 0;
            SinceStrike = 99f;
            LastTier = RallyStrike.Tier.Miss;
            garden?.Bind();

            var s = Slot;
            mower?.ResetTo(s.SpawnPosition, s.SpawnRotation);
            // Blades ON. This used to lock them on the grounds that "nothing here is grass to be
            // cut" — which stopped being true the moment the dirt strips were replaced by the round
            // one lawn. Everything a competitor drives on is grass now, and a mower that drives over
            // a lawn for seventy-eight seconds and leaves no mark is a mower that is not really
            // there. The mark is also information: the shape of a defender's box at the end of a
            // match IS the record of how much ground they had to cover.
            if (mower != null) mower.BladeLocked = false;
            brain?.ResetForMatch();
        }

        void Update()
        {
            SinceStrike += Time.deltaTime;
            LayTracks();
        }

        Vector3 _lastTrackAt;
        bool _trackStarted;

        /// <summary>
        /// Write a pair of tyre marks under the back wheels, every so many metres of travel.
        ///
        /// Spaced by DISTANCE rather than by time, so a machine inching into position and one at
        /// full speed leave the same track. That is the whole point of it: the mark records where
        /// this competitor went, not how long they took to get there — after a minute of defending,
        /// a strip carries the shape of every scramble its driver made.
        /// </summary>
        void LayTracks()
        {
            var tracks = RallyTracks.Instance;
            if (tracks == null || mower == null) return;

            Vector3 at = Position;
            if (!_trackStarted)
            {
                _trackStarted = true;
                _lastTrackAt = at;
                return;
            }

            Vector3 step = at - _lastTrackAt; step.y = 0f;
            if (step.sqrMagnitude < tracks.spacing * tracks.spacing) return;

            // Lay along the direction of TRAVEL, not the machine's heading.
            //
            // A mower turning on the spot barely moves while its heading swings through ninety
            // degrees, so heading-aligned segments fan out from one point like a hand of cards —
            // which is the unnatural spray on every turn. A tyre lays its print along the line it is
            // actually rolling down, and when the machine is sliding sideways that line is the
            // slide, not the nose.
            Vector3 travel = step.normalized;
            _lastTrackAt = at;

            // Only on the dirt. Grass is somebody else's ground and a track across it would claim a
            // machine had been somewhere the confinement never lets it go.
            var s = Slot;
            if (!RallyArena.InsideBand(s, at, 0.6f)) return;

            // The wheels are still where the CHASSIS says they are — the machine has an axle even
            // when it is sliding — so the pair is offset across the heading and the print itself is
            // turned down the travel.
            Vector3 fwd = Heading;
            Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized;
            foreach (float w in new[] { -0.42f, 0.42f })
                tracks.Lay(at + side * w - fwd * 0.35f, travel, 0f);
        }

        void FixedUpdate()
        {
            if (_rb == null) return;
            Confine(Time.fixedDeltaTime);
        }

        /// <summary>
        /// Keep the machine on its own ground — by DEFLECTING it, not by arresting it.
        ///
        /// The old version held the line and felt terrible, and the two facts were the same fact.
        /// It clamped the machine's position to the nearest point inside the box, took the
        /// direction back to that point as the wall normal, cancelled the velocity along it and,
        /// past twelve centimetres, wrote <c>Rigidbody.position</c> directly. Every one of those
        /// four steps is wrong in a way that costs the player their momentum:
        ///
        ///   • "Direction to the nearest inside point" is only the wall normal in the MIDDLE of a
        ///     face. Approach a corner and both axes clamp at once, the direction swings diagonal,
        ///     and the velocity cancelled along that diagonal takes the along-the-wall component
        ///     with it. Corners are exactly where "I was turning and it stopped" was reported, and
        ///     that is not a coincidence, it is the arithmetic.
        ///
        ///   • Cancelling the outward component is right, but doing it as a hard step function the
        ///     instant the line is touched means the machine goes from full speed to nothing in one
        ///     physics step. There is no wall in any driving game that does that. A wall lets you
        ///     arrive.
        ///
        ///   • Even on a flat face, where the cancellation IS tangent-preserving, the tangent does
        ///     not survive. MowerController.ApplyGrip removes the part of the velocity that is
        ///     sideways relative to the NOSE, and a machine held against a wall it is pointing into
        ///     has a velocity that is entirely sideways relative to its nose. So the boundary hands
        ///     the mower a clean tangential slide and the mower's own grip model destroys it within
        ///     about a tenth of a second. Step the loop out and a machine holding FULL THROTTLE at
        ///     forty-five degrees into the line settles at two thirds of a metre per second, out of
        ///     a top speed of ten — and a machine holding a full-lock turn, which the box is too
        ///     small to fit, spends the match pinned at 0.4. That is the bug the player is
        ///     describing, and no amount of tuning the old numbers reaches it, because the old code
        ///     was not the thing spending the speed.
        ///
        ///   • Writing <c>Rigidbody.position</c> every physics step is a teleport. The mower runs
        ///     with interpolation on, and a direct position write resets the interpolation history —
        ///     MowerController.Place says so in its own comment and disables interpolation around
        ///     the one legitimate case. Doing it fifty times a second along an edge does not read as
        ///     a wall, it reads as the game dropping frames.
        ///
        /// So this is rebuilt as three things, in band coordinates so that each face can be
        /// resolved against its OWN normal:
        ///
        ///   1. A brake curve instead of a cliff. The boundary caps the outward component of the
        ///      velocity at sqrt(2 * boundaryBrake * distance-still-to-go), which is exactly the
        ///      speed something can still be brought to rest in the room remaining. Far from the
        ///      line the cap is above any speed the machine has and the boundary is not there at
        ///      all; near it, the outward part is eased to nothing and reaches the line at zero.
        ///      Nothing along the line is ever touched, so brushing the edge at a shallow angle
        ///      costs nothing, which is the "grace" the mower already gives you around props.
        ///
        ///   2. A slide. Whatever the boundary absorbs is partly handed back as speed ALONG the
        ///      face, scaled by how glancing the machine's NOSE is against it — a shallow scrape
        ///      keeps everything, a head-on run keeps nothing and stops, as it should. This is what
        ///      answers the grip model above: the mower bleeds tangential speed every step, so the
        ///      boundary has to be topping it up or the machine grinds to a halt with the engine at
        ///      full throttle. Simulating the whole step loop against MowerController's real drive,
        ///      grip and steering numbers, the speed a machine settles at while HOLDING full
        ///      throttle into the face goes:
        ///
        ///          nose into the face   20     25     30     40     45     55     60     90 deg
        ///          before             4.38   2.55   1.68   0.87   0.66   0.39   0.31   0.00 m/s
        ///          after              9.80   9.54   7.19   3.57   2.37   0.94   0.65   0.00 m/s
        ///
        ///      — out of a top speed of ten. The shape is the point, not the numbers: it is now
        ///      monotone in the angle, so the boundary answers how badly you hit it instead of
        ///      collapsing to a stop everywhere past about twenty degrees. Nothing exceeds what the
        ///      throttle could be delivering at that instant, so the wall can redirect a machine
        ///      and can never make one faster than driving.
        ///
        ///   3. A return, and the teleport demoted to a shunt-catcher. Past the line the permitted
        ///      outward velocity goes negative rather than to zero, so the machine is required to
        ///      already be coming back — see ResolveFace, which explains why merely forbidding
        ///      departure is not enough to hold a line when the drive runs after this does. That
        ///      settles a machine leaning on the edge between four and twenty-five centimetres past
        ///      it depending on the angle, and holds it there indefinitely at any speed, with no
        ///      position write at all. Twenty-five is more than the twelve the old code snapped to,
        ///      and it is still well inside the stones that draw the line — a boundary that is a
        ///      few centimetres soft and never stutters beats one that is exact fifty times a
        ///      second. Anything found more than half a metre out did not drive there, it was hit
        ///      there — a recoil, a collision with another competitor — and only that is snapped.
        ///
        /// The limits are inset by the machine's own hull, resolved onto each axis at its current
        /// heading, so the marked line is where the BODYWORK stops rather than where the axle does.
        /// See RallyArena.DriveLimits for why that matters here specifically.
        ///
        /// Applied to everyone including the player: a bot that can be shoved off its ground and a
        /// player who cannot are two different games being played on the same pitch. Note also that
        /// this runs at execution order -5, ahead of MowerController — deliberately, because the
        /// slide has to be in the velocity BEFORE the mower measures its own forward speed off it,
        /// or the drive model spends the step fighting a correction it cannot see.
        /// </summary>
        void Confine(float dt)
        {
            var s = Slot;
            Vector3 p = _rb.position;
            Vector3 v = _rb.linearVelocity;

            Vector2 pos = RallyArena.ToBand(s, p);
            Vector2 vel = RallyArena.DirToBand(s, v);

            // The machine's extent along each band axis at its CURRENT heading. A box meeting a
            // wall square needs its half-depth; the same box at forty-five degrees needs most of
            // its diagonal. Treating that as a constant would either let the bodywork through the
            // marking when turned or hold it half a metre short of it when square.
            Vector3 fwd = Heading; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = s.inward;
            fwd.Normalize();
            Vector2 f = RallyArena.DirToBand(s, fwd);
            Vector2 hull = new Vector2(
                Mathf.Abs(f.y) * _hullHalf.x + Mathf.Abs(f.x) * _hullHalf.y + hullPadding,
                Mathf.Abs(f.x) * _hullHalf.x + Mathf.Abs(f.y) * _hullHalf.y + hullPadding);

            Vector2 lim = RallyArena.DriveLimits(hull);

            // What the throttle could be giving the machine at this instant. The ceiling on the
            // slide, and the only thing standing between "a wall you can scrape along" and "a wall
            // you can farm for speed you could never reach on open ground".
            float ceiling = mower != null
                ? mower.maxSpeed * (mower.IsBoosting ? mower.boostSpeedMultiplier : 1f)
                : 10f;

            // Each face's tangent is the other axis, so each is handed the other axis' everything —
            // including the slice of the NOSE that lies along it, which is what decides whether
            // contact here is a scrape or a crash.
            var across = ResolveFace(pos.x, vel.x, lim.x, pos.y, vel.y, lim.y, f.y, ceiling, dt);
            var along = ResolveFace(pos.y, vel.y, lim.y, pos.x, vel.x, lim.x, f.x, ceiling, dt);

            // Each face's slide runs along the OTHER axis, which is why they are crossed here.
            float newX = across.normalVel + along.slide;
            float newY = along.normalVel + across.slide;

            if (newX != vel.x || newY != vel.y)
                _rb.linearVelocity = v + RallyArena.FromBand(s, new Vector2(newX - vel.x, newY - vel.y));

            if (across.push != 0f || along.push != 0f)
                _rb.AddForce(RallyArena.FromBand(s, new Vector2(across.push, along.push)),
                             ForceMode.Acceleration);

            if (across.snap != 0f || along.snap != 0f)
                _rb.position = p + RallyArena.FromBand(s, new Vector2(across.snap, along.snap));
        }

        /// <summary>What one face of the boundary wants doing, in band coordinates.</summary>
        struct Face
        {
            /// <summary>This axis' velocity after the face is finished with it.</summary>
            public float normalVel;
            /// <summary>Speed handed to the OTHER axis: the slide along this face.</summary>
            public float slide;
            /// <summary>Inward acceleration along this axis. Only ever non-zero past the line.</summary>
            public float push;
            /// <summary>Metres the position must be moved along this axis. The last resort.</summary>
            public float snap;
        }

        /// <summary>
        /// Resolve one face of the box, against its own axis-aligned normal.
        ///
        /// Everything here is signed by which side of the band centre the machine is on, so the
        /// four faces are one piece of arithmetic rather than four cases. <paramref name="tanPos"/>
        /// and <paramref name="tanLimit"/> are only read to answer the corner question.
        /// </summary>
        Face ResolveFace(float pos, float vel, float limit,
                         float tanPos, float tanVel, float tanLimit,
                         float noseTan, float ceiling, float dt)
        {
            const float Give = 0.10f;      // where a shunted machine is put back to, past the line
            const float Shunt = 0.55f;     // past this, it did not drive here — something hit it

            var face = new Face { normalVel = vel };

            float side = pos >= 0f ? 1f : -1f;
            float depth = side * pos - limit;   // > 0 once the hull is through the marking
            float outward = side * vel;         // > 0 while still travelling out through this face

            // The velocity the boundary will permit through this face, signed outward.
            //
            // Inside the line it is the brake curve: the room still left, converted into the
            // fastest departure that can still be brought to rest inside it. Far from the edge that
            // is above anything the machine can do and the boundary is not there at all.
            //
            // Past the line it goes NEGATIVE, and that sign is the whole difference between a
            // boundary and a suggestion. Forbidding outward motion is not enough to hold a line,
            // and the reason is the execution order this runs in: the confinement speaks at -5 and
            // MowerController drives at 0, so every step the drive puts accelRate * dt back into
            // the velocity AFTER the ceiling has been applied and the machine leaves at a fifth of
            // a metre per second — twenty-eight centimetres of ground per second, for as long as
            // the player leans on it. The old code met that with a per-frame position write, which
            // is why it teleported. Requiring inward motion proportional to the overshoot meets it
            // with arithmetic instead: the engine's fifth of a metre per second is answered at
            // roughly five centimetres out and the machine simply sits there.
            float allowed = depth <= 0f
                ? Mathf.Sqrt(2f * boundaryBrake * -depth)
                : -Mathf.Min(depth * bandReturn, bandReturnMax);

            if (outward > allowed)
            {
                face.normalVel = side * allowed;

                // Everything the boundary just took out of this axis, INCLUDING the escort back in.
                //
                // Counting only the part that was heading out sounds more principled and is in fact
                // useless, and it took a simulation of the whole step loop to see why: a machine
                // resting against the line is at equilibrium, so its outward velocity at this
                // moment is zero by definition — the drive's push into the wall has already been
                // absorbed by last step's return. Measure the slide off "how fast is it leaving"
                // and the answer while leaning on a wall is always nothing, and the slide never
                // fires at all when it is needed most. What the wall is actually absorbing is the
                // whole correction, so that is what may be redirected.
                float excess = outward - allowed;

                // How glancing is it? Taken off the NOSE rather than off the velocity, and that is
                // not a detail. At equilibrium the velocity is nearly all tangential whatever the
                // machine is pointing at, so a velocity-derived angle says "glancing" even when the
                // player is driving flat into the wall — the head-on case then creeps sideways at a
                // fifth of a metre per second forever, which is a boundary moving the machine for
                // them. The nose is what the player is holding and it is what decides whether this
                // is a scrape or a crash: dead ahead into the face is exactly zero, and the wall
                // stops you, as it should.
                float glance = Mathf.Abs(noseTan);
                float carry = excess * slideCarry * glance;

                // A wall is not a rocket. Bounding the slide as an ACCELERATION rather than as a
                // fraction of a velocity is what keeps it the same at any frame rate, and 30 m/s^2
                // is about twice what the machine's own drive can do — enough to beat the grip
                // model's appetite for a sideways velocity, not enough to feel like being thrown.
                carry = Mathf.Min(carry, slideAccel * dt);

                // Which way along the face. The machine's own travel decides it whenever there is
                // any, so the slide only amplifies a direction it already has; below a crawl there
                // is no travel to read and the nose decides instead, which is what lets a machine
                // that has been stopped against the line get going again by turning rather than by
                // reversing out.
                float tanSide = Mathf.Abs(tanVel) > 0.15f
                    ? (tanVel >= 0f ? 1f : -1f)
                    : (noseTan >= 0f ? 1f : -1f);

                // The corner, explicitly. If the face we would slide along is itself breached on
                // the side we are heading, sliding is just burrowing further into the corner and
                // the other face will spend the next step undoing it. So a corner does stop you —
                // it is a corner — but a machine clipping one while running down a face is still
                // free to keep running down that face, because only the outward half is suppressed.
                if (tanSide * tanPos > tanLimit) carry = 0f;

                // Never past what the machine could be doing on open ground under its own power.
                carry = Mathf.Min(carry, Mathf.Max(ceiling - Mathf.Abs(tanVel), 0f));

                face.slide = tanSide * carry;
            }

            if (depth > 0f)
            {
                face.push = -side * bandPush * Mathf.Min(depth, 1f);
                if (depth > Shunt) face.snap = -side * (depth - Give);
            }

            return face;
        }

        // ------------------------------------------------------------------ striking

        /// <summary>
        /// Would a strike connect on this goose right now, and how well?
        ///
        /// Geometry only — no timing button, no input check. The machine is the racket: get it to
        /// the bird, pointed the right way, and the contact happens. Which is the same contract a
        /// car-soccer game makes and the reason this reads as driving rather than as a QTE.
        /// </summary>
        /// <param name="from">Where the goose was last frame.</param>
        /// <param name="to">Where it is now.</param>
        public RallyStrike.Tier Evaluate(Vector3 from, Vector3 to, out Vector3 contact)
        {
            contact = to;
            if (mower == null) return RallyStrike.Tier.Miss;

            // Tested against the SEGMENT the bird flew, not against where it happens to be this
            // frame. A perfect launch travels 25 m/s, which is 0.42 m per frame at sixty and 8 m per
            // frame in a browser tab that has just been dropped to three — and at that step a goose
            // crosses the whole 3.4 m strike radius between two tests and connects with nothing. The
            // symptom is the worst kind: a parry the player watched happen, that did not.
            Vector3 goosePos = ClosestOnSegment(from, to, Position);

            Vector3 flat = goosePos - Position; flat.y = 0f;
            float dist = flat.magnitude;
            if (dist > parryRadius) return RallyStrike.Tier.Miss;

            Vector3 fwd = Heading; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) return RallyStrike.Tier.Miss;
            fwd.Normalize();

            if (dist > 1e-3f)
            {
                float ang = Vector3.Angle(fwd, flat / dist);
                if (ang > parryHalfAngle) return RallyStrike.Tier.Miss;
            }

            contact = Vector3.Lerp(Position + Vector3.up * 0.45f, goosePos, 0.72f);

            // Tier off the distance to the SWEET SPOT rather than to the machine's centre, so
            // catching the bird on the front of the deck beats scraping it along the side. That is
            // the difference between a strike the player aimed and one that happened to them.
            Vector3 toSweet = goosePos - SweetSpot; toSweet.y = 0f;
            float off = toSweet.magnitude;

            if (off <= perfectRadius) return RallyStrike.Tier.Perfect;
            if (off <= goodRadius) return RallyStrike.Tier.Good;
            return RallyStrike.Tier.Normal;
        }

        /// <summary>Nearest point on a segment to a world position, flattened. Ignores height.</summary>
        static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a; ab.y = 0f;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 1e-6f) return b;
            Vector3 ap = p - a; ap.y = 0f;
            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / lenSq);
            Vector3 on = a + (b - a) * t;
            return on;
        }

        /// <summary>
        /// Where this competitor is sending it.
        ///
        /// Facing first, contact angle second. The machine's heading is what the player is holding
        /// and can see, so it has to dominate; the incoming line contributes the glance, which is
        /// what makes a bird caught on the corner of the deck spray off sideways instead of leaving
        /// on rails. Then the result is snapped to whichever opponent's broad sector it falls in, so
        /// a rough aim still names somebody.
        /// </summary>
        public int ChooseTarget(Vector3 gooseVelocity, Vector3 contact, out Vector3 launchDir)
        {
            Vector3 fwd = Heading; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Slot.inward;
            fwd.Normalize();

            Vector3 incoming = gooseVelocity; incoming.y = 0f;
            Vector3 glance = fwd;
            if (incoming.sqrMagnitude > 1f)
                glance = Vector3.Reflect(incoming.normalized, fwd);

            // Where on the deck it landed. Catching it off-centre pushes the launch that way, which
            // is how a player who cannot get square on still steers the return.
            Vector3 off = contact - Position; off.y = 0f;
            float lateral = Vector3.Dot(off, Vector3.Cross(Vector3.up, fwd));

            launchDir = (fwd * 1.0f + glance * 0.34f + Vector3.Cross(Vector3.up, fwd) * (lateral * 0.22f));
            launchDir.y = 0f;
            if (launchDir.sqrMagnitude < 1e-4f) launchDir = fwd;
            launchDir.Normalize();

            int target = RallyArena.SectorTarget(slot, launchDir);
            if (target < 0) target = RallyArena.NearestOpponent(slot, launchDir);

            // Point the launch AT the garden it was assigned to, mostly. Leaving it on the raw
            // heading meant a return aimed between two gardens sailed into the corner and the goose
            // spent five seconds walking back in, which is the deadest the arena ever looked.
            // Blended rather than snapped so the player's aim is still visibly theirs.
            Vector3 toGarden = RallyArena.Get(target).gardenCentre - contact;
            toGarden.y = 0f;
            if (toGarden.sqrMagnitude > 1e-4f)
                launchDir = Vector3.Slerp(launchDir, toGarden.normalized, 0.55f).normalized;

            return target;
        }

        /// <summary>Book a connection: statistics, recoil, and the machine's own reaction.</summary>
        public void RegisterStrike(RallyStrike.Tier tier, Vector3 contact, bool knockout)
        {
            Parries++;
            SinceStrike = 0f;
            LastTier = tier;
            switch (tier)
            {
                case RallyStrike.Tier.Perfect: Perfects++; break;
                case RallyStrike.Tier.Good: Goods++; break;
                default: Normals++; break;
            }
            if (knockout) Knockouts++;

            float recoil = tier switch
            {
                RallyStrike.Tier.Perfect => recoilPerfect,
                RallyStrike.Tier.Good => recoilGood,
                _ => recoilNormal
            };
            // Reuse the machine's own impact reaction rather than scripting a lean. Bonk shoves the
            // chassis with torque and lets the four suspension raycasts compress and settle, so the
            // recoil carries the mower's real mass and damping instead of an animation of it.
            mower?.Bonk(contact, recoil);
            // The machine's own half of the impact: tyre scuffs and dirt off the back wheels. Every
            // competitor leaves them, not just the player — a parry across the arena has to read as
            // a parry from here, and marks on the ground are the only part of it that carries.
            if (mower != null)
                RallyFX.Instance?.MowerRecoil(Position, Heading, mower.transform.right, recoil);
            brain?.NotifyStruck(tier);
        }

        public void RegisterConceded() { Conceded++; brain?.NotifyConceded(); }
        public void RegisterLanded() => Landed++;
    }

    /// <summary>How well a strike connected. Ordered worst to best; the numbers are compared.</summary>
    public static class RallyStrike
    {
        public enum Tier { Miss = 0, Normal = 1, Good = 2, Perfect = 3 }

        public static string Label(Tier t) => t switch
        {
            Tier.Perfect => "PERFECT!",
            Tier.Good => "GOOD",
            Tier.Normal => "PARRY",
            _ => ""
        };
    }
}
