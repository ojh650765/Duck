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
        [Tooltip("How hard the boundary pushes a machine back onto its ground over the last few " +
                 "centimetres. A shove first, so leaning on the line is soft rather than a clang — " +
                 "but only for those few centimetres. See Confine().")]
        public float bandPush = 26f;

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

        void Awake()
        {
            if (mower == null) mower = GetComponentInChildren<MowerController>();
            if (mower != null) _rb = mower.GetComponent<Rigidbody>();
            if (brain != null && mower != null) mower.inputSource = brain;
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
        /// Keep the machine on its own ground.
        ///
        /// A shove for the first few centimetres and a wall after that, and the wall is the part
        /// that was missing. A pure spring cannot hold a line: it is an acceleration, so a machine
        /// arriving at speed keeps going while the shove spends its budget on the momentum, and how
        /// far past it gets is a function of how fast it was travelling. That is why the boundary
        /// could be driven through — not because the number was too small, but because "push back
        /// proportionally" has no maximum overshoot at all, and the faster you go the further out
        /// you end up. A marked area you can leave by going quickly is not a marked area.
        ///
        /// So: the outward velocity is scrubbed first, the spring handles the shallow contact so
        /// brushing the edge stays soft, and past <c>Give</c> the position is simply put back. The
        /// hard part is never felt, because by the time it applies the machine has already been
        /// stopped from travelling outward — it catches the frame where a big step would otherwise
        /// jump the line, which is exactly the case the spring is worst at.
        ///
        /// Applied to everyone including the player: a bot that can be shoved off its ground and a
        /// player who cannot are two different games being played on the same pitch.
        /// </summary>
        void Confine(float dt)
        {
            const float Give = 0.12f;      // how far past the line the machine may ever be seen

            var s = Slot;
            Vector3 p = _rb.position;
            Vector3 limit = RallyArena.ClampToDrive(s, p);
            Vector3 back = limit - p;
            back.y = 0f;
            float over = back.magnitude;
            if (over < 1e-4f) return;

            Vector3 dir = back / over;

            // Cancel the part of the velocity still heading out first, or everything below is
            // fighting momentum that is about to undo it.
            float outward = Vector3.Dot(_rb.linearVelocity, -dir);
            if (outward > 0f) _rb.linearVelocity += dir * outward;

            if (over > Give) _rb.position = p + dir * (over - Give);
            else _rb.AddForce(dir * (bandPush * over), ForceMode.Acceleration);
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
