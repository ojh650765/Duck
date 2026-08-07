using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// One opponent's hands on the wheel.
    ///
    /// It produces a steer value and a throttle value and nothing else — see <see cref="IMowerInput"/>.
    /// There is no path to the goose that does not go through the same suspension, grip and drift
    /// model the player's machine uses, so a bot that gets there first got there by driving.
    ///
    /// Two ideas do most of the work:
    ///
    /// The bot drives to a STAND POINT rather than to the goose. The stand point sits a couple of
    /// metres back along the line it wants to hit down, so arriving there leaves the machine already
    /// pointing at the opponent it chose. That is exactly what a good player does, it needs no
    /// separate aiming code, and it is why the bots visibly send geese to DIFFERENT opponents rather
    /// than batting everything to whoever is nearest.
    ///
    /// And it is allowed to be wrong. It reacts late, its aim is off by a few degrees, and it
    /// sometimes commits to an intercept the bird has already left. Those are not difficulty
    /// settings bolted on afterwards — a defender who is never wrong is a wall, and the player
    /// stops believing there is anybody over there.
    /// </summary>
    public class RallyBrain : MonoBehaviour, IMowerInput
    {
        [Header("Who")]
        public RallyCompetitor competitor;
        [Tooltip("0 is hopeless, 1 is machine-perfect. Set from the contestant's mowing grade.")]
        [Range(0f, 1f)] public float skill = 0.6f;

        [Header("Reaction")]
        [Tooltip("Seconds before a good defender notices a new threat.")]
        public float reactionFast = 0.22f;
        [Tooltip("Seconds a poor one takes. Interpolated by skill.")]
        public float reactionSlow = 0.78f;

        [Header("Error")]
        [Tooltip("Degrees a poor defender's aim is off by. Scaled down as skill rises, never to zero " +
                 "— a bot that is never off is a bot the player can predict perfectly.")]
        public float aimErrorDegrees = 26f;
        [Tooltip("Metres a poor defender misjudges the intercept by, across the line.")]
        public float interceptError = 3.2f;
        [Tooltip("Chance per decision of simply not committing this time.")]
        [Range(0f, 0.5f)] public float blunderChance = 0.16f;

        [Header("Driving")]
        [Tooltip("Degrees of heading error that maps to full lock.")]
        public float steerFullLock = 42f;
        [Tooltip("Inside this many metres of the stand point the bot eases off.")]
        public float arriveRadius = 1.6f;
        [Tooltip("Below this, hold station rather than shuffling. Stops the idle twitch that reads " +
                 "as a machine with a fault rather than a driver waiting.")]
        public float holdRadius = 0.85f;

        public float Steer { get; private set; }
        public float Throttle { get; private set; }
        public bool Handbrake { get; private set; }
        public bool Boost { get; private set; }

        RallyDirector _director;
        System.Random _rng;
        float _decisionTimer;
        Vector3 _standPoint;
        bool _committed;
        int _preferredTarget = -1;
        float _aimOffset;
        float _lateralOffset;
        RallyGoose _tracked;

        public void Bind(RallyDirector director, int seed)
        {
            _director = director;
            _rng = new System.Random(seed);
            ResetForMatch();
        }

        public void ResetForMatch()
        {
            _decisionTimer = 0f;
            _committed = false;
            _tracked = null;
            _preferredTarget = -1;
            Steer = Throttle = 0f;
            Handbrake = Boost = false;
            _standPoint = competitor != null ? competitor.Slot.bandCentre : Vector3.zero;
        }

        float Reaction => Mathf.Lerp(reactionSlow, reactionFast, skill);

        void Update()
        {
            if (competitor == null) return;

            // Re-acquire rather than give up.
            //
            // These are set by Bind at the start of a match, and if either is ever null afterwards
            // this bot stops driving — permanently, silently, holding whatever steer and throttle it
            // last had. That is precisely the failure that is hardest to read from the outside: the
            // machine looks parked, the logs look healthy, and nothing says a defender has left the
            // match. So recover, and say so once, because the recovery is not the interesting part —
            // the fact that it was needed is.
            if (_director == null || _rng == null)
            {
                var d = FindFirstObjectByType<RallyDirector>();
                if (d == null) return;
                Debug.LogWarning($"[Rally] {competitor.Name}'s bot lost its bindings and re-acquired " +
                                 "them. Something cleared them mid-match — worth finding out what.");
                Bind(d, competitor.slot * 7919 + 13);
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Hands off the wheel until the match is actually running.
            //
            // A bot with no goose to chase still drives to a stand point, and it was doing that from
            // the moment the scene loaded — so the opening shot of an untouched arena had three
            // machines already milling about in it. Zeroed rather than merely not updated, because
            // MowerController keeps the last input it was given and a stale throttle carries on
            // pushing.
            if (_director != null && !_director.Running)
            {
                Throttle = 0f;
                Steer = 0f;
                Handbrake = true;
                Boost = false;
                return;
            }

            // Advanced every frame, not per decision: the shuffle has to be continuous or the
            // machine teleports between stances at the reaction interval.
            _idlePhase += dt * 0.9f;

            _decisionTimer -= dt;
            if (_decisionTimer <= 0f)
            {
                _decisionTimer = Reaction * (0.7f + (float)_rng.NextDouble() * 0.6f);
                Decide();
            }

            Drive(dt);
        }

        // ------------------------------------------------------------------ deciding

        void Decide()
        {
            var slot = competitor.Slot;
            _tracked = _director.NearestThreatTo(competitor.slot);

            if (_tracked == null)
            {
                // Nothing aimed at them — which is not the same as nothing to do.
                //
                // This used to park the machine on the middle of its own box, which IS where it
                // starts, so a bot with no goose assigned never moved a centimetre. Three of the
                // four competitors spent most of the match stationary, and a defender standing
                // perfectly still while geese cross the arena in front of them reads as a bot that
                // has been switched off.
                //
                // A person waiting watches the whole pitch and shifts their weight. So: track the
                // nearest bird in play whoever it is going for, lean toward the side of the box it
                // would arrive from, step forward off the back line, and never stop entirely.
                _committed = false;
                _standPoint = ReadyPoint(slot);
                return;
            }

            if (!_committed && _rng.NextDouble() < blunderChance * (1f - skill * 0.7f))
            {
                // Caught flat-footed this beat. Not a permanent decision — the next one will notice.
                _committed = false;
                return;
            }

            _preferredTarget = PickTarget();
            Vector3 desired = (RallyArena.Get(_preferredTarget).gardenCentre - slot.bandCentre);
            desired.y = 0f;
            _aimOffset = (float)(_rng.NextDouble() * 2.0 - 1.0) * aimErrorDegrees * (1f - skill * 0.8f);
            desired = Quaternion.Euler(0f, _aimOffset, 0f) * desired.normalized;

            Vector3 intercept = Intercept(_tracked);
            _lateralOffset = (float)(_rng.NextDouble() * 2.0 - 1.0) * interceptError * (1f - skill * 0.75f);
            intercept += slot.right * _lateralOffset;

            // Stand back along the line it wants to hit down, so arriving points the machine at the
            // opponent it picked. The aiming is the positioning.
            _standPoint = RallyArena.ClampToBand(slot, intercept - desired * 2.2f, 1.0f);
            _standPoint.y = slot.bandCentre.y;
            _committed = true;
        }

        float _idlePhase;

        /// <summary>The stance to hold when nothing is coming. Never the same spot twice running.</summary>
        Vector3 ReadyPoint(in RallyArena.Slot slot)
        {
            RallyGoose watch = null;
            float best = float.MaxValue;
            var flock = _director != null ? _director.Flock : null;
            if (flock != null)
                foreach (var g in flock)
                {
                    if (g == null || !g.Active) continue;
                    float d = (g.transform.position - slot.bandCentre).sqrMagnitude;
                    if (d >= best) continue;
                    best = d; watch = g;
                }

            Vector3 p = slot.bandCentre;
            if (watch != null)
            {
                Vector3 to = watch.transform.position - slot.bandCentre; to.y = 0f;
                float across = Vector3.Dot(to, slot.right);
                p += slot.right * Mathf.Clamp(across * 0.4f,
                                              -RallyArena.BandHalfWidth * 0.6f,
                                               RallyArena.BandHalfWidth * 0.6f);
                // Forward of centre: closer to where a bird crosses, and it means the machine is
                // already pointed up the pitch when one is assigned.
                p -= slot.outward * 1.8f;
            }

            // A slow shuffle across the frontage. Wider than the brain's hold radius on purpose —
            // inside it the machine simply stops, which is the thing this exists to prevent.
            p += slot.right * Mathf.Sin(_idlePhase) * (holdRadius * 2.4f + 1.2f);

            return RallyArena.ClampToBand(slot, p, 1.2f);
        }

        /// <summary>
        /// Where the bird will cross this competitor's ground.
        ///
        /// A straight-line lead, which is right for a charging goose and wrong for one arcing in on a
        /// bounce — and being wrong about the arcing ones is fine, because the player is wrong about
        /// them too. Perfect prediction here would make the airborne redirect, which is the hardest
        /// thing in the mode, the one the bots never miss.
        /// </summary>
        Vector3 Intercept(RallyGoose g)
        {
            var slot = competitor.Slot;
            Vector3 gp = g.transform.position;
            Vector3 gv = g.Velocity; gv.y = 0f;

            Vector3 toBand = slot.bandCentre - gp;
            float closing = Vector3.Dot(gv, -slot.outward);
            float eta = closing > 0.5f ? Vector3.Dot(toBand, -slot.outward) / closing : 0.6f;
            eta = Mathf.Clamp(eta, 0f, 3.5f);

            Vector3 predicted = gp + gv * eta;
            predicted.y = slot.bandCentre.y;
            return RallyArena.ClampToBand(slot, predicted, 0.8f);
        }

        /// <summary>
        /// Who to send it at.
        ///
        /// Health is a NUDGE here, not the rule. It used to be the whole score — attack whoever is
        /// winning — and the effect on the player was the opposite of what that sounds like: the
        /// moment a human takes a couple of hits they stop being the healthiest, three bots quietly
        /// agree to leave them alone, and the player spends the rest of the match watching a game
        /// they are no longer in. The same fault as the serve weighting, in the other half of the
        /// system, and fixing one without the other fixes nothing.
        ///
        /// Now: mostly a coin toss between the three opponents, with health worth about a third of
        /// the spread and a grudge worth a little more. That still reads as "gang up on the leader"
        /// from the stands, because over a match the leader does take more, but no single decision
        /// is a foregone conclusion and nobody can be written out.
        /// </summary>
        int PickTarget()
        {
            float best = -1f;
            int pick = (competitor.slot + 2) % RallyArena.Count;
            for (int i = 0; i < RallyArena.Count; i++)
            {
                if (i == competitor.slot) continue;
                var c = _director.CompetitorAt(i);
                float integrity = c != null ? c.Integrity : 1f;
                float score = integrity * 0.35f + (float)_rng.NextDouble();
                // Mild grudge: bots that just got hit hit back, which is legible from the stands.
                if (_tracked != null && _tracked.LastStriker == i) score += 0.3f;
                if (score <= best) continue;
                best = score; pick = i;
            }
            return pick;
        }

        // ------------------------------------------------------------------ driving

        void Drive(float dt)
        {
            Vector3 pos = competitor.Position;
            Vector3 to = _standPoint - pos; to.y = 0f;
            float dist = to.magnitude;

            if (dist < holdRadius)
            {
                Throttle = Mathf.MoveTowards(Throttle, 0f, dt * 4f);
                // Still turn to face where it wants to hit, so it is ready rather than merely parked.
                AimOnly(dt);
                return;
            }

            Vector3 fwd = competitor.Heading; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) return;
            fwd.Normalize();
            Vector3 dir = to / dist;

            float signed = Vector3.SignedAngle(fwd, dir, Vector3.up);
            bool behind = Mathf.Abs(signed) > 115f;

            if (behind && dist < 6f)
            {
                // Back up rather than swinging all the way round. A three-point turn in a 9 m strip
                // is how a defender misses a goose it was standing next to.
                Throttle = Mathf.MoveTowards(Throttle, -0.8f, dt * 5f);
                Steer = Mathf.Clamp(-signed / steerFullLock, -1f, 1f) * -1f;
                Handbrake = false;
                Boost = false;
                return;
            }

            Steer = Mathf.Clamp(signed / steerFullLock, -1f, 1f);

            float ease = Mathf.Clamp01(dist / Mathf.Max(arriveRadius, 0.1f));
            float align = Mathf.Clamp01(1f - Mathf.Abs(signed) / 90f);
            float want = Mathf.Lerp(0.35f, 1f, align) * ease;
            Throttle = Mathf.MoveTowards(Throttle, want, dt * 6f);

            // Boost only when it is genuinely behind and pointed the right way. A bot on the boost
            // whenever it can be reads as rubber-banding even when it isn't.
            Boost = _committed && dist > 7f && align > 0.85f && skill > 0.45f;
            Handbrake = !behind && Mathf.Abs(signed) > 78f && dist > 4f;
        }

        void AimOnly(float dt)
        {
            int want = _preferredTarget >= 0 ? _preferredTarget : (competitor.slot + 2) % RallyArena.Count;
            Vector3 desired = RallyArena.Get(want).gardenCentre - competitor.Position;
            desired.y = 0f;
            if (desired.sqrMagnitude < 1e-4f) { Steer = 0f; return; }
            desired = Quaternion.Euler(0f, _aimOffset, 0f) * desired.normalized;

            Vector3 fwd = competitor.Heading; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) { Steer = 0f; return; }
            float signed = Vector3.SignedAngle(fwd.normalized, desired, Vector3.up);
            Steer = Mathf.Clamp(signed / steerFullLock, -1f, 1f);
            // A dab of throttle, because a stationary mower cannot turn — the yaw model needs some
            // speed under it. Without this the bots sat pointing the wrong way and never came round.
            Throttle = Mathf.Abs(signed) > 12f ? 0.22f : 0f;
            Handbrake = false;
            Boost = false;
        }

        public void NotifyStruck(RallyStrike.Tier tier)
        {
            // Committed shot taken; look for the next one immediately rather than finishing the beat
            // standing still with a goose already on its way back.
            _committed = false;
            _decisionTimer = Mathf.Min(_decisionTimer, 0.12f);
        }

        public void NotifyConceded()
        {
            // Rattled. A defender who has just been scored on hesitates, and it is the clearest
            // signal the player gets that the pressure they applied actually landed.
            _decisionTimer = Reaction * 2.2f;
            _committed = false;
        }
    }
}
