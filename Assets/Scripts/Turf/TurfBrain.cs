using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// One opponent gardener's hands on the wheel.
    ///
    /// It produces a steer value and a throttle value and nothing else — see <see cref="IMowerInput"/>.
    /// It cannot move a transform, cannot claim ground that is not under its own roller, and cannot
    /// see anything the player could not work out by looking at the arena. Everything it knows about
    /// the map comes through <see cref="TurfMask"/>'s sixteen-by-sixteen COARSE sectors, which is
    /// roughly "that quarter over there is mostly blue" — the read a person gets from a chase camera,
    /// not the quarter-million-cell grid the score is counted from.
    ///
    /// Three ideas do the work:
    ///
    /// It picks a SECTOR, not a point. A bot steering at a moving opponent looks like a homing
    /// missile; a bot that decides "there is a lot of HORACE in the north-east and I am going to go
    /// and mow through it" drives a route, arrives, works the area, and reads as somebody with a
    /// plan. The plan is re-chosen a few times a minute, so it can also be overtaken by events.
    ///
    /// It ROUTES through the hedge rather than into it. The arena is a wheel with eight openings and
    /// the bot goes to a gap mouth, through it, and only then at its objective. That is
    /// the single thing that stops the opponents grinding along the outside of a hedge for the whole
    /// match, and it is also what puts them in the chokes where the player is.
    ///
    /// And it is allowed to be wrong. It commits late, aims a little off the middle of a gap, keeps
    /// working a sector somebody else has already taken back, and occasionally forgets to boost. Not
    /// difficulty settings bolted on afterwards: an opponent that is never wrong is a metronome, and
    /// the player stops believing there is anybody over there.
    /// </summary>
    public class TurfBrain : MonoBehaviour, IMowerInput
    {
        /// <summary>How this gardener decides what to do with the next few seconds.</summary>
        public enum Plan
        {
            /// <summary>Works the outer loop, taking empty ground and rarely picking a fight.</summary>
            Expander,
            /// <summary>Hunts whoever owns the most and drives through the middle of it.</summary>
            Raider,
            /// <summary>Lives in the hub and defends the crown.</summary>
            Warlord
        }

        [Header("Who")]
        public TurfCompetitor competitor;
        public Plan plan = Plan.Expander;
        [Tooltip("0 is hopeless, 1 is machine-perfect. Set from the contestant's mowing grade.")]
        [Range(0f, 1f)] public float skill = 0.6f;

        [Header("Deciding")]
        [Tooltip("Seconds between plan reviews for a sharp gardener.")]
        public float thinkFast = 1.5f;
        [Tooltip("Seconds for a slow one. Interpolated by skill.")]
        public float thinkSlow = 3.6f;
        [Tooltip("Chance per review of keeping a plan that has stopped being any good.")]
        [Range(0f, 0.6f)] public float stubbornness = 0.3f;

        [Header("Error")]
        [Tooltip("Metres a poor gardener misjudges the middle of a gap by. This is what makes them " +
                 "clip a choke instead of threading it, which is most of their visible character.")]
        public float gapError = 2.4f;
        [Tooltip("Metres a poor gardener's objective is off by.")]
        public float aimError = 7f;

        [Header("Driving")]
        [Tooltip("Degrees of heading error that maps to full lock.")]
        public float steerFullLock = 40f;
        [Tooltip("Inside this many metres of a waypoint it counts as reached.")]
        public float arriveRadius = 3.2f;
        [Tooltip("How close is close enough to count a route leg as done. Generous: waypoints pick " +
                 "a heading, they are not places anybody has to visit, and a machine that doubles " +
                 "back to touch one exactly has stopped painting to satisfy its own bookkeeping.")]
        public float legRadius = 5.5f;

        [Header("Barging")]
        [Tooltip("Metres inside which a rival is worth driving AT rather than driving past. The " +
                 "whole route is abandoned while one is lined up, which is the point: a shunt has " +
                 "to look like a decision somebody made.")]
        public float bargeRange = 15f;
        [Tooltip("Seconds a barge stays committed once started, so it reads as a charge rather " +
                 "than as a twitch toward whoever happened to be nearest this frame.")]
        public float bargeCommit = 1.8f;
        [Tooltip("Seconds before another barge may start. Without it two bots find each other and " +
                 "spend the whole match shoving in one corner.")]
        public float bargeCooldown = 2.6f;
        [Tooltip("Chance a review goes looking for somebody to hit at all.")]
        [Range(0f, 1f)] public float bargeAppetite = 0.6f;

        public float Steer { get; private set; }
        public float Throttle { get; private set; }
        public bool Handbrake { get; private set; }
        public bool Boost { get; private set; }

        /// <summary>
        /// False until the horn. Hands off the wheel during the count-in.
        ///
        /// A countdown a machine can drive through is not a countdown — it is three seconds of
        /// everybody quietly taking the good ground before the match has started, which is both
        /// unfair and unreadable. The player is held by InputReader.DrivingEnabled over the same
        /// beat, and it matters that these are two different mechanisms doing the same thing at the
        /// same moment: the bots do not read the player's input gate, so the only way for all four
        /// to be still is for both to be set.
        /// </summary>
        public bool Released { get; set; }

        /// <summary>Where this gardener is currently trying to get to. Drawn by the debug overlay.</summary>
        public Vector3 Objective { get; private set; }
        /// <summary>The gap it is threading right now, or -1 when it is driving in the open.</summary>
        public int RoutingVia { get; private set; } = -1;

        System.Random _rng;
        float _thinkTimer;
        Vector3 _waypoint;
        Vector3 _aimJitter;
        float _gapJitter;
        float _stuckTimer;
        float _reverseTimer;
        TurfCompetitor _barging;
        float _bargeTimer;
        float _bargeRest;

        readonly System.Collections.Generic.List<Vector3> _path = new(24);
        int _leg;
        float _repathTimer;

        float Think => Mathf.Lerp(thinkSlow, thinkFast, skill);

        public void Bind(int seed)
        {
            _rng = new System.Random(seed);
            ResetForMatch();
        }

        public void ResetForMatch()
        {
            _rng ??= new System.Random(competitor != null ? competitor.slot * 7919 + 13 : 1);
            _thinkTimer = 0f;
            _stuckTimer = _reverseTimer = 0f;
            _barging = null;
            _bargeTimer = _bargeRest = 0f;
            _path.Clear();
            _leg = 0;
            _repathTimer = 0f;
            Released = false;
            Steer = Throttle = 0f;
            Handbrake = Boost = false;
            RoutingVia = -1;
            Objective = _waypoint = competitor != null ? competitor.Slot.SpawnPosition : Vector3.zero;
            Reroll();
        }

        void Reroll()
        {
            float e = 1f - skill;
            _aimJitter = new Vector3(Range(-1f, 1f), 0f, Range(-1f, 1f)) * (aimError * e);
            _gapJitter = Range(-1f, 1f) * gapError * e;
        }

        float Range(float a, float b) => a + (float)(_rng?.NextDouble() ?? 0.5) * (b - a);

        void Update()
        {
            if (competitor == null || _rng == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (!Released)
            {
                // Still on the line. Zeroed every frame rather than simply not updated, or whatever
                // the wheel happened to be holding when the match reset carries through the count-in
                // and the machine creeps off its mark.
                Steer = Throttle = 0f;
                Handbrake = Boost = false;
                return;
            }

            _thinkTimer -= dt;
            if (_thinkTimer <= 0f)
            {
                _thinkTimer = Think * (0.7f + (float)_rng.NextDouble() * 0.6f);
                if (_rng.NextDouble() >= stubbornness * (1f - skill * 0.6f))
                {
                    Reroll();
                    ChooseObjective();
                }
                ConsiderBarge();
            }

            // A barge owns the wheel outright while it lasts. Blending it with the route was the
            // first attempt and it produced a machine that drifted vaguely toward a rival while
            // still trying to reach a sector, which connects with nothing and reads as indecision.
            if (TickBarge(dt)) { Drive(dt); return; }

            Route();
            Drive(dt);
        }

        // ------------------------------------------------------------------ what to do

        void ChooseObjective()
        {
            var mask = TurfMask.Instance;
            if (mask == null) { Objective = Vector3.zero; return; }

            Vector3 me = competitor.Position;
            int slot = competitor.slot;

            float bestScore = float.NegativeInfinity;
            Vector3 best = Objective;

            for (int sz = 0; sz < TurfMask.SectorRes; sz++)
            for (int sx = 0; sx < TurfMask.SectorRes; sx++)
            {
                int playable = mask.SectorPlayable(sx, sz);
                // A sector that is mostly hedge is not worth crossing the arena for even if the
                // sliver of it that exists belongs to somebody else.
                if (playable < 40) continue;

                Vector3 centre = TurfMask.SectorCentre(sx, sz);
                float radius = new Vector2(centre.x, centre.z).magnitude;

                float mine = mask.SectorOwned(sx, sz, slot) / (float)playable;
                float enemy = mask.SectorEnemy(sx, sz, slot) / (float)playable;
                float empty = mask.SectorNeutral(sx, sz) / (float)playable;

                // Ground worth having, before anybody's plan gets an opinion about it.
                //
                // Two terms, and the first match played without them showed exactly why both are
                // needed. Every gardener spent the whole ninety seconds on the outer loop and the
                // hub was never entered once — the middle of the arena, the thing the entire layout
                // is pointed at, finished a match completely untouched.
                //
                // INWARD, because ground gets scarcer as you go in: the loop is 1600 square metres
                // of easy grass and the plaza is 530 of contested, so a metre of plaza is worth
                // several of rim and nothing was saying so.
                //
                // MINE, because the sector a gardener is standing in is the one they have just
                // painted, and without a penalty for it the highest-scoring place to go is always
                // where you already are. That is what pinned MARGOT to one patch for a whole match
                // on one percent of the arena.
                float inward = radius < TurfArena.PlazaRadius ? 1.0f
                             : radius < TurfArena.HedgeInner ? 0.55f
                             : 0f;

                float score = plan switch
                {
                    Plan.Expander =>
                        // Empty ground, and a strong preference for the outfield: the loop is fast,
                        // wide and nobody defends it, which is exactly the bargain this gardener has
                        // decided to take.
                        empty * 2.4f + enemy * 0.35f
                        + (radius > TurfArena.HedgeOuter ? 1.1f : -0.5f),

                    Plan.Raider =>
                        // Whoever owns the most, wherever they own it. Weighted toward the leader so
                        // the raider visibly turns on whoever is winning, which is both good play and
                        // the thing that keeps a four-way match from running away from the player.
                        enemy * 3.0f + LeaderBonus(mask, sx, sz, slot) * 1.6f + empty * 0.25f,

                    _ =>
                        // The hub, and the mouths of the hub. Everything else is a distraction.
                        //
                        // Scaled by how much of it is NOT already hers, which is the fix for a
                        // warlord who reached the middle and then sat on her own ground for the
                        // rest of the match: a flat bonus for being in the plaza made the plaza the
                        // best place to be even once there was nothing left there to take.
                        (radius < TurfArena.PlazaRadius ? 2.6f * (1f - mine) : 0f)
                        + (radius < TurfArena.HedgeInner ? 1.2f * (1f - mine) : 0f)
                        + enemy * 1.4f
                };

                score += inward;
                // Never worth crossing the arena to repaint what is already yours.
                score -= mine * 1.7f;

                // Everyone would rather work near where they already are than cross the arena for a
                // marginally better sector. Without this the bots pinball and never finish anything.
                score -= Vector3.Distance(me, centre) * 0.026f;

                if (score <= bestScore) continue;
                bestScore = score;
                best = centre;
            }

            Objective = TurfArena.NearestPlayable(best + _aimJitter);
            Repath();
        }

        /// <summary>Extra pull toward ground held by whoever is currently top of the board.</summary>
        static float LeaderBonus(TurfMask mask, int sx, int sz, int slot)
        {
            int leader = mask.Leader;
            if (leader == slot) return 0f;
            int playable = mask.SectorPlayable(sx, sz);
            return playable > 0 ? mask.SectorOwned(sx, sz, leader) / (float)playable : 0f;
        }

        // ------------------------------------------------------------------ barging

        /// <summary>
        /// Pick somebody to drive into.
        ///
        /// Four identical machines with no weapons: the only thing one gardener can physically do to
        /// another is get in the way, and a mode where nobody ever does reads as four vehicles
        /// politely sharing a car park. Bumper cars is the right register — go for the hit, miss as
        /// often as not, come away pointing the wrong direction.
        ///
        /// Targets are ranked by what the hit is WORTH rather than by distance alone. Somebody
        /// standing in the plaza is worth knocking off the crown; somebody with a big share is worth
        /// interrupting. So the barging is strategy rather than noise, and a player who gets hit can
        /// see why they were the one hit.
        /// </summary>
        void ConsiderBarge()
        {
            if (_bargeRest > 0f || competitor == null) return;
            if (_rng.NextDouble() > bargeAppetite) return;

            var mask = TurfMask.Instance;
            var all = Object.FindObjectsByType<TurfCompetitor>(FindObjectsSortMode.None);
            Vector3 me = competitor.Position;

            TurfCompetitor best = null;
            float bestScore = 0.3f;
            foreach (var c in all)
            {
                if (c == null || c == competitor || c.mower == null) continue;
                float d = Vector3.Distance(me, c.Position);
                if (d > bargeRange) continue;

                float score = 1f - d / bargeRange;
                if (TurfArena.InsidePlaza(c.Position)) score += 0.55f;
                if (c.HasCrown) score += 0.7f;
                if (mask != null) score += mask.Share(c.slot) * 1.2f;
                // A raider goes looking for contact; an expander would rather be left alone.
                score *= plan == Plan.Raider ? 1.4f : plan == Plan.Warlord ? 1.1f : 0.75f;

                if (score <= bestScore) continue;
                bestScore = score; best = c;
            }

            if (best == null) return;
            _barging = best;
            _bargeTimer = bargeCommit;
        }

        /// <summary>
        /// Drive the barge. Returns true while it owns the wheel.
        ///
        /// It aims at where the target is GOING rather than where it is, which is what turns a shove
        /// into an interception — and, when the lead is misjudged, into a near miss that leaves both
        /// machines sideways. The lead is scaled by skill, so the sharp ones connect and the clumsy
        /// ones clatter through the space a rival has just left. That miss is worth as much as the
        /// hit: a bot that never whiffs is a bot nobody believes is driving.
        /// </summary>
        bool TickBarge(float dt)
        {
            if (_bargeRest > 0f) _bargeRest -= dt;
            if (_barging == null) return false;

            _bargeTimer -= dt;
            Vector3 me = competitor.Position;
            float dist = Vector3.Distance(me, _barging.Position);

            if (_bargeTimer <= 0f || dist > bargeRange * 1.6f || _barging.mower == null)
            {
                _barging = null;
                _bargeRest = bargeCooldown * (0.7f + (float)_rng.NextDouble() * 0.6f);
                return false;
            }

            float lead = Mathf.Lerp(0.10f, 0.40f, skill) * dist / Mathf.Max(competitor.Speed, 4f);
            _waypoint = _barging.Position + _barging.Heading * (_barging.Speed * lead);
            RoutingVia = -1;
            return true;
        }

        // ------------------------------------------------------------------ how to get there

        /// <summary>
        /// Ask the navigation graph for a route to the objective.
        ///
        /// Rate limited, because A* over fifty nodes is cheap but not free and four gardeners
        /// re-planning every frame is four times a cost nobody needs to pay. A route stays valid
        /// until the objective changes or the machine falls off it.
        /// </summary>
        void Repath()
        {
            if (competitor == null) return;
            _repathTimer = 0.6f;
            _leg = 0;
            if (!TurfNavGraph.FindPath(competitor.Position, Objective, _path))
            {
                // Nowhere to go. Keep whatever route was in hand rather than inventing a straight
                // line, which on this map means a straight line into a hedge.
                _path.Clear();
                _waypoint = Objective;
            }
        }

        /// <summary>
        /// Walk the route, and decide what the next thing to steer at is.
        ///
        /// This used to be a greedy one-step guess: find the opening whose bearing best matches the
        /// objective, aim at its mouth, aim through it. That is enough to cross the wall once and
        /// wrong about everything else — it cannot go out through one gate and in through another,
        /// it cannot tell that the near gate opens onto the wrong side of the hub, and when it
        /// failed to line up it simply drove into the hedge again. See TurfNavGraph.
        ///
        /// A leg counts as reached generously. Waypoints are a device for choosing a heading, not
        /// places anybody has to visit, and a machine that doubles back to touch one exactly is a
        /// machine that has stopped painting to satisfy its own bookkeeping.
        /// </summary>
        void Route()
        {
            _repathTimer -= Time.deltaTime;

            if (_path.Count == 0)
            {
                if (_repathTimer <= 0f) Repath();
                if (_path.Count == 0) { _waypoint = Objective; RoutingVia = -1; return; }
            }

            Vector3 me = competitor.Position;

            // Advance past every leg already behind us, not just one. A boost down a spoke can
            // clear two or three waypoints between frames, and stepping one at a time would leave
            // the machine steering back at a point it passed half a second ago.
            while (_leg < _path.Count - 1)
            {
                Vector3 leg = _path[_leg];
                leg.y = me.y;
                if (Vector3.Distance(me, leg) > legRadius) break;
                _leg++;
            }

            _waypoint = _path[Mathf.Min(_leg, _path.Count - 1)];

            // Off the route entirely — shunted, spun, or shoved through a gap by somebody else.
            if (Vector3.Distance(me, _waypoint) > 30f && _repathTimer <= 0f) Repath();

            // Reported for the trace and for the boost test below: is this leg threading a wall?
            float r = new Vector2(_waypoint.x, _waypoint.z).magnitude;
            RoutingVia = r > TurfArena.HedgeInner - 5f && r < TurfArena.HedgeOuter + 5f ? 0 : -1;
        }

        // ------------------------------------------------------------------ driving

        void Drive(float dt)
        {
            Vector3 pos = competitor.Position;
            Vector3 to = _waypoint - pos; to.y = 0f;
            float dist = to.magnitude;

            Vector3 fwd = competitor.Heading; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) return;
            fwd.Normalize();

            // Wedged. Measured as "not getting closer to the waypoint while asking for throttle",
            // which catches a nose in a hedge and a shoving match against another machine alike.
            if (Throttle > 0.25f && competitor.Speed < 1.2f) _stuckTimer += dt;
            else _stuckTimer = Mathf.Max(0f, _stuckTimer - dt * 2f);
            if (_stuckTimer > 0.9f) { _reverseTimer = 0.8f; _stuckTimer = 0f; }

            if (_reverseTimer > 0f)
            {
                _reverseTimer -= dt;
                Throttle = -0.85f;
                Steer = Mathf.Sign(Vector3.SignedAngle(fwd, to.sqrMagnitude > 1e-4f ? to : fwd, Vector3.up)) * -0.7f;
                Handbrake = Boost = false;
                return;
            }

            if (dist < arriveRadius && _barging == null)
            {
                // Arrived and still moving. Keep the throttle on and pick the next thing NOW rather
                // than coasting until the review timer happens to come round — a gardener circling
                // its own finished objective for two seconds is repainting ground it already owns,
                // which scores nothing and looks like it has forgotten what it was doing.
                _thinkTimer = 0f;
                Throttle = Mathf.MoveTowards(Throttle, 0.55f, dt * 5f);
                Steer *= 0.7f;
                Handbrake = Boost = false;
                    return;
            }

            Vector3 dir = to / dist;
            float signed = Vector3.SignedAngle(fwd, dir, Vector3.up);
            bool behind = Mathf.Abs(signed) > 118f;

            if (behind && dist < 7f)
            {
                Throttle = Mathf.MoveTowards(Throttle, -0.8f, dt * 5f);
                Steer = Mathf.Clamp(-signed / steerFullLock, -1f, 1f) * -1f;
                Handbrake = Boost = false;
                return;
            }

            Steer = Mathf.Clamp(signed / steerFullLock, -1f, 1f);

            float align = Mathf.Clamp01(1f - Mathf.Abs(signed) / 90f);
            Throttle = Mathf.MoveTowards(Throttle, Mathf.Lerp(0.42f, 1f, align), dt * 6f);

            // Boost when it is pointed the right way, has room, and is not about to thread a gap it
            // will need to place the machine into. A bot on the boost whenever it can be reads as
            // rubber-banding even when it is not.
            // Boost when the road ahead is actually straight.
            //
            // The old test was "far enough away and pointed at it", which on a route made of legs
            // meant boosting into a waypoint that turned ninety degrees the moment it arrived. Now
            // it looks at the leg AFTER this one: if both point the same way there is a real
            // straight to spend it on, and a spoke crossing the court or a long arc of the loop
            // both qualify. Threading a gateway never does — that is the one place on the map where
            // the answer is to place the machine, not to arrive fast.
            bool straightAhead = align > 0.8f && dist > 8f && RoutingVia < 0 && LegsAlign();
            Boost = _barging != null
                ? align > 0.68f && dist > 4f
                : straightAhead && skill > 0.35f && competitor.Speed > 3f;

            // A handbrake flick for a corner that is genuinely too tight to steer round. Sparingly:
            // the drift model is the player's toy and a bot using it constantly makes the arena look
            // like it is on ice.
            Handbrake = !behind && Mathf.Abs(signed) > 82f && dist > 5f && competitor.Speed > 6f;
        }

        /// <summary>
        /// Does the leg after this one carry on in roughly the same direction?
        ///
        /// What turns "there is distance ahead" into "there is a STRAIGHT ahead", which is the only
        /// thing worth spending boost on. True at the end of a route as well: the last leg has
        /// nothing after it to disagree with, and arriving at the objective quickly is fine.
        /// </summary>
        bool LegsAlign()
        {
            if (_path.Count == 0) return true;
            int a = Mathf.Min(_leg, _path.Count - 1);
            if (a + 1 >= _path.Count) return true;

            Vector3 here = competitor.Position;
            Vector3 first = _path[a] - here;
            Vector3 next = _path[a + 1] - _path[a];
            first.y = next.y = 0f;
            if (first.sqrMagnitude < 1e-3f || next.sqrMagnitude < 1e-3f) return true;
            return Vector3.Dot(first.normalized, next.normalized) > 0.72f;
        }

        // ------------------------------------------------------------------ reacting

        /// <summary>Somebody just took ground off this gardener.</summary>
        public void NotifyRobbed(float squareMetres)
        {
            if (squareMetres < 3f) return;
            // Look up. Not a scripted revenge — the next review simply happens sooner, and a raider
            // scoring off this gardener has usually just made their own sector the richest enemy
            // sector on the board, so the review tends to send them straight back at each other.
            _thinkTimer = Mathf.Min(_thinkTimer, 0.35f);
        }
    }
}
