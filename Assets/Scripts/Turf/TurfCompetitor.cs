using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// One of the four gardeners. Owns a mower and turns driving into ground.
    ///
    /// There is deliberately no player subclass and no bot subclass, exactly as in
    /// <see cref="RallyCompetitor"/>. The player differs from the three opponents in one field:
    /// <see cref="brain"/> is null and the controls come from the keyboard instead. Roller width,
    /// the crown bonus, the coverage rule, the boost paid for a steal and the collision reaction
    /// are one code path, so "the NPCs paint faster" is not something anyone can suspect without
    /// also suspecting it of the player.
    ///
    /// It paints from the roller's LAST position to its position NOW, every fixed step, rather than
    /// stamping a disc where it happens to be. A mower at full boost covers 40 cm per fixed step and
    /// a browser tab that has dropped to thirty covers 80 — stamping discs leaves a dotted line
    /// through the fastest, most committed driving in the mode, which is precisely the driving that
    /// should look best.
    /// </summary>
    [DefaultExecutionOrder(-5)]
    public class TurfCompetitor : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Which spoke this gardener starts on. Indexes TurfArena.")]
        public int slot;
        [Tooltip("True for the one machine the keyboard drives.")]
        public bool isPlayer;

        [Header("Wiring")]
        public MowerController mower;
        [Tooltip("The bot driving this mower. Null on the player's, and that is the only difference.")]
        public TurfBrain brain;

        [Header("Roller")]
        [Tooltip("Width of the ground the roller claims, in metres. Wider than the mower's cutting " +
                 "deck on purpose — a 1.2 m trail on a 92 m arena is a pencil line, and the mode " +
                 "would be about lap count rather than about route.")]
        public float rollerWidth = 3.3f;
        [Tooltip("Multiplier on the roller while this gardener holds the hub. The crown's whole " +
                 "reward, and the reason the middle is worth fighting for.")]
        public float crownWidthBonus = 1.5f;
        [Tooltip("Below this speed the roller lifts. Stops a parked mower boring a hole in the map.")]
        public float minPaintSpeed = 0.5f;

        [Header("Reward for stealing")]
        [Tooltip("Boost fuel paid per square metre taken off an opponent. Claiming empty ground " +
                 "pays nothing — that asymmetry is the mode's whole risk curve.")]
        public float boostPerStolenMetre = 0.010f;
        [Tooltip("Boost fuel paid per square metre of empty ground. Small, but not nothing, or the " +
                 "opening thirty seconds have no economy at all.")]
        public float boostPerNeutralMetre = 0.0016f;

        [Header("Collisions")]
        [Tooltip("How hard a mower-to-mower shunt kicks this machine, 0..1, through MowerController.Bonk.")]
        [Range(0f, 1f)] public float shuntRecoil = 0.85f;
        [Tooltip("Seconds between shunts registering, so a scrape along another machine is one " +
                 "event rather than forty.")]
        public float shuntCooldown = 0.32f;
        [Tooltip("Metres per second of straight shove away from the other machine, on top of the " +
                 "chassis reaction. This is the bumper-car half: Bonk rocks the body on its " +
                 "springs, which looks right and moves nobody, and a mode where contact does not " +
                 "MOVE you is a mode where contact is decoration.")]
        public float shuntKnockback = 5.2f;
        [Tooltip("Closing speed, in m/s, at which contact counts as a hit at all.")]
        public float shuntThreshold = 1.8f;

        [Header("Staying on the pitch")]
        [Tooltip("Metres INSIDE the touchline at which the arena starts pushing back. Generous on " +
                 "purpose — a mower that has reached the fence has already lost several seconds, " +
                 "and this is a hand on the shoulder well before it is a wall.")]
        public float recoverMargin = 2.5f;
        [Tooltip("How hard the push is, per metre over the line.")]
        public float recoverPush = 26f;

        // ---- running totals, read by the HUD and by the result carried out of the match ----
        /// <summary>Square metres claimed off nobody.</summary>
        public float MetresClaimed { get; private set; }
        /// <summary>Square metres taken off opponents. The offensive score.</summary>
        public float MetresStolen { get; private set; }
        /// <summary>Square metres this gardener has lost to others.</summary>
        public float MetresLost { get; private set; }
        /// <summary>Distance driven inside the hub. Reported at the end as "time in the middle".</summary>
        public float PlazaMetres { get; private set; }
        public int Shunts { get; private set; }

        /// <summary>Square metres per second being taken off opponents, smoothed. Drives the FX.</summary>
        public float StealRate { get; private set; }
        /// <summary>Seconds since this gardener last took ground off somebody.</summary>
        public float SinceSteal { get; private set; } = 99f;
        /// <summary>Who they took it off, or -1.</summary>
        public int LastVictim { get; private set; } = -1;

        /// <summary>True while this gardener holds the most of the hub. Set by the director.</summary>
        public bool HasCrown { get; set; }

        public TurfArena.Slot Slot => TurfArena.Get(slot);
        public string Name => Slot.contestant;
        public Color Livery => Slot.livery;
        public Vector3 Position => mower != null ? mower.transform.position : Slot.SpawnPosition;
        public Vector3 Heading => mower != null ? mower.transform.forward : Slot.inward;
        public float Speed => mower != null ? Mathf.Abs(mower.ForwardSpeed) : 0f;

        /// <summary>Share of the arena this gardener holds right now, 0..1.</summary>
        public float Share => TurfMask.Instance != null ? TurfMask.Instance.Share(slot) : 0f;

        /// <summary>Where the roller is: across the back of the machine, behind the axle.</summary>
        public Vector3 RollerPosition
            => mower != null
             ? mower.transform.TransformPoint(new Vector3(0f, 0f, -0.28f))
             : Slot.SpawnPosition;

        /// <summary>The roller's working width right now, crown included.</summary>
        public float ActiveWidth => rollerWidth * (HasCrown ? crownWidthBonus : 1f);

        /// <summary>Raised the moment a swath takes ground off somebody. (victim, square metres, where).</summary>
        public System.Action<int, float, Vector3> OnSteal;
        /// <summary>Raised for every swath that took any ground at all. (square metres, where).</summary>
        public System.Action<float, Vector3> OnClaim;
        /// <summary>Raised when this machine shunts another. (the other gardener, strength 0..1).</summary>
        public System.Action<TurfCompetitor, float> OnShunt;

        Rigidbody _rb;
        Vector3 _lastRoller;
        bool _rollerValid;
        bool _painting;
        float _shuntTimer;

        void Awake()
        {
            if (mower == null) mower = GetComponentInChildren<MowerController>();
            if (mower != null) _rb = mower.GetComponent<Rigidbody>();
            if (brain != null && mower != null) mower.inputSource = brain;
        }

        public void ResetForMatch()
        {
            MetresClaimed = MetresStolen = MetresLost = PlazaMetres = 0f;
            Shunts = 0;
            StealRate = 0f;
            SinceSteal = 99f;
            LastVictim = -1;
            HasCrown = false;
            _rollerValid = false;
            _painting = false;
            _shuntTimer = 0f;

            var s = Slot;
            mower?.ResetTo(s.SpawnPosition, s.SpawnRotation);
            // The blade is locked and the wheels are not. The player keeps the machine and the
            // controls they have spent the game with — this is the same mower, doing a different
            // job — but there is no grass here to cut, only ground to take.
            if (mower != null) mower.BladeLocked = true;
            brain?.ResetForMatch();
        }

        /// <summary>Let the roller down. Called by the director on the horn.</summary>
        public void BeginPainting()
        {
            _painting = true;
            _rollerValid = false;      // so the first swath is a point, not a line from the spawn
        }

        /// <summary>Lift the roller. Called the instant the match clock hits zero.</summary>
        public void StopPainting() => _painting = false;

        void Update()
        {
            SinceSteal += Time.deltaTime;
            StealRate = Mathf.Lerp(StealRate, 0f, 1f - Mathf.Exp(-3.2f * Time.deltaTime));
            if (_shuntTimer > 0f) _shuntTimer -= Time.deltaTime;
        }

        void FixedUpdate()
        {
            Paint(Time.fixedDeltaTime);
            Recover();
        }

        // ------------------------------------------------------------------ painting

        void Paint(float dt)
        {
            var mask = TurfMask.Instance;
            if (mask == null || mower == null) return;

            Vector3 roller = RollerPosition;
            roller.y = 0f;

            if (!_rollerValid)
            {
                _lastRoller = roller;
                _rollerValid = true;
                return;
            }

            float travel = Vector3.Distance(_lastRoller, roller);

            if (!_painting || Speed < minPaintSpeed || !mower.IsGrounded)
            {
                // The roller is up, but the history still advances. Leaving it stale means the next
                // swath is a straight line from wherever the machine last painted to wherever it is
                // now — which after a spin-out is a stripe drawn clean through a hedge.
                _lastRoller = roller;
                return;
            }

            var result = mask.Claim(_lastRoller, roller, ActiveWidth, slot);
            _lastRoller = roller;

            if (TurfArena.InsidePlaza(roller)) PlazaMetres += travel;
            if (result.Total <= 1e-4f) return;

            MetresClaimed += result.neutral;
            OnClaim?.Invoke(result.Total, roller);

            // Boost is paid on the ground, not on the driving. A gardener who spends the match on
            // empty outfield ends it with an empty tank; one who has been living in somebody else's
            // garden has been boosting the whole time. The economy IS the strategy advice.
            mower.AddBoostFuel(result.neutral * boostPerNeutralMetre
                             + result.Stolen * boostPerStolenMetre);

            if (result.Stolen <= 1e-4f) return;

            MetresStolen += result.Stolen;
            StealRate += result.Stolen / Mathf.Max(dt, 1e-4f) * 0.35f;
            SinceSteal = 0f;
            LastVictim = result.BiggestVictim;

            for (int i = 0; i < TurfArena.Count; i++)
            {
                float lost = result.From(i);
                if (lost <= 1e-4f) continue;
                TurfDirector.NotifyLoss(i, lost);
                OnSteal?.Invoke(i, lost, roller);
            }
        }

        /// <summary>Book ground taken off this gardener by somebody else.</summary>
        public void RegisterLoss(float squareMetres)
        {
            MetresLost += squareMetres;
            brain?.NotifyRobbed(squareMetres);
        }

        // ------------------------------------------------------------------ staying on the map

        /// <summary>
        /// Nudge a machine that has ended up somewhere the arena does not exist.
        ///
        /// Hedges are scenery with colliders, and the outfield has a barrier, so this almost never
        /// fires. It is here for the almost: a mower launched off a ramp into the top of a hedge, or
        /// squeezed through a choke by two others at once, would otherwise sit inside geometry
        /// unable to paint while the clock ran. A shove back toward open ground, proportional and
        /// applied to everyone including the player — a bot that can be rescued and a player who
        /// cannot are two different games on the same pitch.
        /// </summary>
        void Recover()
        {
            if (_rb == null) return;
            Vector3 p = _rb.position;
            p.y = 0f;

            Vector3 dir;
            float over;

            // OUTSIDE THE TOUCHLINE: always pushed straight back INWARD, and the push starts before
            // the line rather than at it.
            //
            // Inward specifically, not "toward the nearest playable point". Out here the nearest
            // playable point is the touchline itself, so a machine sliding along the outside gets a
            // shove perpendicular to its own travel and skates the rim for as long as it likes.
            // Aiming at the middle of the arena instead means the correction always has a component
            // against whatever took it out there.
            //
            // The margin is generous on purpose: a mower that reaches the fence has already lost
            // several seconds, and letting it drift a metre further before anything happens only
            // adds to that. It is a hand on the shoulder well before it is a wall.
            float r = new Vector2(p.x, p.z).magnitude;
            float softEdge = TurfArena.ArenaRadius - recoverMargin;
            float coreEdge = TurfArena.CoreRadius + recoverMargin;
            if (r < coreEdge)
            {
                // INSIDE THE CORE: pushed straight back OUT, and again before the wall rather than
                // at it. The bench is in there and there is a stone wall around it, so a machine
                // that has found its way in has clipped through something — the right correction is
                // the one that gets it back onto turf without waiting for the collider to argue.
                dir = r > 1e-3f ? p / r : Vector3.forward;
                over = coreEdge - r;
            }
            else if (r > softEdge)
            {
                dir = r > 1e-3f ? -p / r : Vector3.forward;
                over = r - softEdge;
            }
            else
            {
                // INSIDE A HEDGE: the nearest way out really is the nearest way out, because a
                // machine wedged in a wall could be a metre from a gap in any direction.
                if (TurfArena.IsPlayable(p)) return;
                Vector3 back = TurfArena.NearestPlayable(p) - p;
                back.y = 0f;
                over = back.magnitude;
                if (over < 1e-3f) return;
                dir = back / over;
            }

            _rb.AddForce(dir * (recoverPush * Mathf.Min(over, 3f)), ForceMode.Acceleration);

            // Cancel the part of the velocity still heading the wrong way, or the shove spends its
            // whole budget fighting momentum and the machine travels a long way past the line first.
            float outward = Vector3.Dot(_rb.linearVelocity, -dir);
            if (outward > 0f) _rb.linearVelocity += dir * outward;
        }

        // ------------------------------------------------------------------ contact

        /// <summary>
        /// Two mowers meeting. Called by this machine's own <see cref="TurfContact"/>, which is
        /// where Unity actually delivers the collision.
        ///
        /// Both machines are told, by their own contact, and both recoil. Neither is stunned: the
        /// mode is about where you are, so taking the controls away is taking the game away.
        /// <see cref="MowerController.Bonk"/> shoves the chassis with torque and lets the four
        /// suspension raycasts compress and settle, so the recoil carries the machine's real mass
        /// rather than an animation of it, and the driver keeps the wheel throughout.
        /// </summary>
        public void Shunted(TurfCompetitor other, Collision c)
        {
            if (_shuntTimer > 0f || other == null || other == this) return;

            // Closing speed, not contact count. Two machines sitting against each other in a choke
            // are touching sixty times a second and should read as a scrape, not as sixty impacts.
            float closing = c.relativeVelocity.magnitude;
            if (closing < shuntThreshold) return;

            _shuntTimer = shuntCooldown;
            Shunts++;

            Vector3 point = c.contactCount > 0 ? c.GetContact(0).point : Position;
            float strength = Mathf.Clamp01(closing / 12f) * shuntRecoil;
            mower?.Bonk(point, strength);

            // The shove. Away from the other machine, flat, and applied as a velocity change so it
            // lands whatever the two were doing beforehand. Both parties run this on their own
            // rigidbody from their own contact, so a hit throws BOTH cars apart — which is what
            // makes a three-way pile-up in a choke resolve itself instead of grinding.
            if (_rb != null)
            {
                Vector3 away = Position - other.Position;
                away.y = 0f;
                if (away.sqrMagnitude > 1e-4f)
                    _rb.AddForce(away.normalized * (shuntKnockback * (0.45f + strength)),
                                 ForceMode.VelocityChange);
            }

            OnShunt?.Invoke(other, strength);
        }
    }
}
