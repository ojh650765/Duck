using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// One opponent gardener's hands on the wheel.
    ///
    /// It produces a steer value and a throttle value and nothing else — see <see cref="IMowerInput"/>.
    /// It cannot move a transform, cannot claim ground that is not under its own roller, and cannot
    /// see anything the player could not work out by looking at the arena and the clock. Everything
    /// it knows about the map comes through <see cref="TurfMask"/>'s sixteen-by-sixteen COARSE
    /// sectors, which is roughly "that quarter over there is mostly blue" — the read a person gets
    /// from a chase camera, not the quarter-million-cell grid the score is counted from. It gets no
    /// extra speed, no wider roller and no free fuel. Everything below is a better decision, not a
    /// bigger number.
    ///
    /// ---------------------------------------------------------------------------------------------
    /// WHY THIS WAS REWRITTEN
    ///
    /// The first version drove around and painted things and was not a contest. A capture with the
    /// player parked showed three gardeners fifteen seconds into a match holding 12.5%, 10.3% and
    /// 8.7% with two thirds of the board still untouched and the crown unclaimed — and, worse, one
    /// of them cruising at 0.42 throttle. Four faults, all of them structural:
    ///
    /// IT COULD NOT BOOST WHERE BOOSTING MATTERS. The old code marked a leg as "threading a wall"
    /// whenever its waypoint radius fell between 25 and 38 m, and the navigation graph's whole outer
    /// ring sits at <see cref="TurfArena.LoopMid"/> — 37.5. So boost was forbidden on the outer loop:
    /// the fastest, straightest, widest part of the arena, and since the disc grew to 45.5 m the
    /// largest, at 3083 m² against a 5900 m² board. Every gardener drove the biggest half of the map
    /// at walking pace by accident. Threading a gateway is now tested as what it actually is — a leg
    /// that crosses the hedge annulus — and boost is available everywhere else.
    ///
    /// IT SCORED SECTORS AS FRACTIONS. Every term was a ratio of the sector's own playable cells, so
    /// a fat 1024-cell wedge of open loop and a 45-cell scrap wedged behind a hedge looked identical
    /// if both were empty. There was no notion of how much ground was actually on offer and no notion
    /// of what it costs to go and get it. Objectives are valued in SQUARE METRES PER SECOND now:
    /// real area, divided by an honest estimate of the drive — distance, plus the cost of turning
    /// round, plus the time it takes to work the place once you arrive.
    ///
    /// IT NEVER WENT FOR THE CROWN. The crown is a 1.5× roller — by a distance the largest multiplier
    /// in the mode — and it is won by holding 30% of the hub. Only the Warlord went near the plaza at
    /// all, and her incentive was scaled by how much of each SECTOR was already hers, so it switched
    /// itself off exactly as she started to succeed. She was engineered to stop just short of the
    /// threshold. The pull is driven by hub-wide <see cref="TurfMask.PlazaShare"/> now, so it does
    /// not relax until the crown is genuinely taken, and it comes back the moment it is lost.
    ///
    /// IT DID NOT KNOW WHAT KIND OF MATCH IT WAS IN. Four mowers at 3.3 m and 10 m/s lay down about
    /// 13,000 m² of roller over a hundred seconds onto a 5900 m² board. Neutral ground is GONE by
    /// mid-match and the ending is a pure steal war — and two of the three archetypes were still
    /// hunting empty grass that no longer existed, on an economy that pays 6.25× as much boost fuel
    /// for a stolen metre as for an empty one. Every gardener now reads how much of the board is
    /// still unclaimed, how far behind the leader it is, and how much clock is left, and plays
    /// differently for each.
    /// ---------------------------------------------------------------------------------------------
    ///
    /// What survives from the first version, because it was right:
    ///
    /// It picks a SECTOR, not a point. A bot steering at a moving opponent looks like a homing
    /// missile; a bot that decides "there is a lot of HORACE in the north-east and I am going to go
    /// and mow through it" drives a route, arrives, works the area, and reads as somebody with a
    /// plan.
    ///
    /// It ROUTES through the hedge rather than into it, via <see cref="TurfNavGraph"/>.
    ///
    /// And it is allowed to be wrong. It misjudges the middle of a gap, mis-values a sector, commits
    /// to a barge it will not land. An opponent that is never wrong is a metronome and the player
    /// stops believing there is anybody over there. The wrongness is a DIAL now rather than an
    /// accident — see <see cref="Difficulty"/> — and turning it up buys competence, never advantage.
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
            /// <summary>Lives in the hub, takes the crown and holds the line.</summary>
            Warlord
        }

        // =============================================================================================
        //  THE ONE DIAL
        // =============================================================================================

        /// <summary>
        /// How good the opposition is, 0..1. THIS IS THE ONLY PLACE TO TUNE THE DIFFICULTY.
        ///
        /// Deliberately a static field and not a serialized one. Every public field on this component
        /// is already baked into BloomRush.unity with the value it had the day the scene was built,
        /// so changing a field initialiser here would do precisely nothing at runtime — the scene
        /// would keep overriding it and the change would look like it had failed. A static is immune
        /// to that: it is read from this line, always, by all four gardeners at once.
        ///
        /// It buys COMPETENCE and nothing else. Turning it up makes them re-plan sooner, mis-value
        /// sectors less, carry more speed through a corner, lead a barge more accurately and commit
        /// to the crown harder. It does not touch speed, roller width, boost fuel, or what they are
        /// allowed to see. A gardener at 1.0 beats the player by driving better, which is the only
        /// kind of losing that teaches anybody anything.
        ///
        ///   0.0  hopeless — dithers, mis-reads the board, never boosts
        ///   0.5  a nuisance
        ///   0.85 a real contest: they will punish a lazy lap and take the hub if you leave it
        ///   1.0  they will beat an average player most of the time
        ///
        /// Combined with each contestant's own <see cref="skill"/>, so raising it lifts the whole
        /// field WITHOUT flattening HORACE, MARGOT and BRAMBLE into one another. See <see cref="Edge"/>.
        /// </summary>
        public static float Difficulty = 0.85f;

        // =============================================================================================
        //  SERIALIZED — every one of these is already baked into BloomRush.unity.
        //  Changing a default here will NOT change the running game. Tune the consts below instead.
        // =============================================================================================

        [Header("Who")]
        public TurfCompetitor competitor;
        public Plan plan = Plan.Expander;
        [Tooltip("0 is hopeless, 1 is machine-perfect. Set from the contestant's mowing grade. This " +
                 "is the CHARACTER's ability and it is what keeps the three of them apart; the " +
                 "difficulty of the match as a whole lives in TurfBrain.Difficulty.")]
        [Range(0f, 1f)] public float skill = 0.6f;

        [Header("Deciding")]
        [Tooltip("Seconds between plan reviews for a sharp gardener, BEFORE the difficulty scale.")]
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
        [Tooltip("Baseline chance a review goes looking for somebody to hit. Scaled by archetype " +
                 "and by how far behind this gardener is.")]
        [Range(0f, 1f)] public float bargeAppetite = 0.6f;

        // =============================================================================================
        //  TUNING — consts on purpose.
        //
        //  Unity serializes public fields, and this component was serialized into BloomRush.unity
        //  before any of this existed. A public field added here would be fine TODAY (an absent
        //  field deserializes to its initialiser) and would silently freeze at whatever value the
        //  scene happened to be saved with tomorrow. Everything the new behaviour rests on is
        //  therefore a const: it cannot be overridden by a stale scene, it cannot drift between the
        //  editor and a WebGL build, and there is exactly one copy of each number.
        // =============================================================================================

        /// <summary>Square metres in one mask cell. All prizes are counted in real area, not in cells.</summary>
        const float CellArea = TurfMask.MetresPerCell * TurfMask.MetresPerCell;

        /// <summary>Sectors with less playable ground than this are not worth crossing a map for.</summary>
        const int MinSectorCells = 24;

        /// <summary>
        /// Metres per second a gardener assumes it can average on the way to an objective.
        ///
        /// Deliberately below the mower's 10 m/s top speed and well below the 14.2 it reaches on
        /// boost. This is an ESTIMATE the bot makes about its own driving, not a fact it is given,
        /// and pitching it low is what stops "there is a huge prize on the far rim" beating "there
        /// is a decent prize right here" every single time.
        /// </summary>
        const float CruiseEstimate = 8.5f;

        /// <summary>
        /// Seconds a gardener expects to spend actually working a sector once it gets there.
        ///
        /// The whole reason objectives are valued per SECOND rather than per metre travelled. Without
        /// it the nearest sector always wins by dividing by nearly zero, and the machine pinballs
        /// between its own feet. Six metres of sector at 3.3 m of roller is two or three passes, so
        /// this is close to honest as well as useful.
        /// </summary>
        const float WorkSeconds = 2.6f;

        /// <summary>
        /// Seconds charged for a full about-turn, scaled by how far round the objective is.
        ///
        /// A mower does not turn round for free: 165°/s of yaw at a standstill and 88 at speed means
        /// reversing direction costs a slow-down, an arc and an acceleration, and it is the single
        /// most expensive thing a gardener can do with its time. Pricing it here is what makes the
        /// bots SWEEP — the sector they are already pointed at wins ties, so they work a region and
        /// move on rather than criss-crossing the arena repainting their own stripes.
        /// </summary>
        const float TurnSeconds = 2.4f;

        /// <summary>
        /// The band of neutral share over which the match turns from a land grab into a steal war.
        ///
        /// Not a clock. The board filling is something the player can SEE, and it is the honest
        /// trigger: while there is loose grass, taking it is cheap; once it is scarce, the only way
        /// to gain a metre is to take it off somebody, and it is worth double because they lose it.
        ///
        /// THESE TWO NUMBERS WERE MEASURED, and the first attempt at them was dead code. It asked
        /// for neutral to fall below 0.34 before anything changed, on the arithmetic that four
        /// mowers lay ~13,000 m2 of roller over a hundred seconds onto a 5900 m2 board and must
        /// therefore fill it twice over. The board does not do that, for two reasons the arithmetic
        /// did not know: a match is seventy-five seconds and not a hundred (matchSeconds is 75 in
        /// BloomRush.unity), and the roller spends a great deal of its time on ground somebody
        /// already owns. A full run traced at four second intervals goes
        ///
        ///     t75 100%   t63 90%   t51 81%   t39 77%   t23 71%   t03 56.5%
        ///
        /// and stops there. Neutral NEVER APPROACHED 0.34, so the ramp never left zero, enemy ground
        /// stayed at its opening weight for the whole match, and the one thing this class was given
        /// to make a gardener play the ending differently from the opening did nothing at all.
        ///
        /// Pitched across the second half of the real curve instead: still a land grab while the
        /// grass is loose, fully a steal war by the closing seconds. A ratio against a single
        /// threshold cannot express that — it needs both ends, or the transition lands outside the
        /// range the board actually visits.
        /// </summary>
        const float RipeFrom = 0.88f, RipeTo = 0.50f;

        /// <summary>Share behind the leader that counts as fully behind. Ten points is a lot on this board.</summary>
        const float DeficitSpan = 0.10f;

        /// <summary>
        /// Hub share a gardener drives for. The director takes the crown at 0.30 and defends it with
        /// 0.045 of hysteresis, so 0.35 is "enough to take it off somebody", not "enough to tie".
        /// </summary>
        const float CrownTarget = 0.35f;

        /// <summary>
        /// What the crown is worth, in the same square-metres-per-second the sector scores use.
        ///
        /// A crowned roller is 4.95 m instead of 3.3, which at 10 m/s is 16 m²/s of extra ground for
        /// as long as it is held. Six is a deliberate discount on that: the hub is small, contested
        /// and slow to cross, and a gardener who values the crown at its theoretical maximum abandons
        /// a good outfield position to go and lose a wrestling match in the middle.
        /// </summary>
        const float CrownWorth = 6f;

        /// <summary>Extra pull toward an UNCLAIMED crown, for everyone. A vacant hub should draw a crowd.</summary>
        const float VacantCrownPull = 0.45f;

        /// <summary>Metres of arc a corner is allowed before it stops costing any speed at all.</summary>
        const float CornerRoom = 15f;
        /// <summary>Degrees of heading error that counts as a full corner.</summary>
        const float CornerHard = 70f;
        /// <summary>How much of a corner's speed cost is forgiven by having room to arc through it.</summary>
        const float RoomRelief = 0.75f;
        /// <summary>Throttle a hopeless driver falls to in a hairpin, and a sharp one.</summary>
        const float ThrottleFloorSoft = 0.38f, ThrottleFloorSharp = 0.58f;
        /// <summary>Throttle units per second. Fast enough to be decisive, slow enough not to stutter.</summary>
        const float ThrottleRate = 6f;
        /// <summary>Throttle held while an objective is being swapped out underneath the machine.</summary>
        const float ArriveThrottle = 0.9f;

        /// <summary>Dot product between consecutive legs that counts as a straight worth boosting.</summary>
        const float StraightDot = 0.62f;

        /// <summary>The band of the outer loop a gardener will run a lane in.</summary>
        const float LaneInner = 35.2f, LaneOuter = 43.0f;

        /// <summary>
        /// Radius an objective is pulled in to, so no gardener ever drives AT the touchline.
        ///
        /// Sector centres go out to 45.2 m and the barrier is at 45.5, so a rim sector's centre is a
        /// point three tenths of a metre short of a wall. A machine sent to one arrives nose-first
        /// into the fence and grinds along it: the trace has HORACE at r 45 doing 0.1 m/s on full
        /// lock with his objective three metres away, which is a gardener spending seconds achieving
        /// nothing at the one place on the board where the recovery push and the barrier collider are
        /// both arguing with him. The ground out there is still worth painting — the roller is 3.3 m
        /// wide, so a pass at 43.1 covers to within a whisker of the line — it just is not worth
        /// AIMING at. Same reasoning as the lane's outer limit, and deliberately the same radius.
        /// </summary>
        const float FenceStandoff = TurfArena.ArenaRadius - 2.4f;
        /// <summary>Metres of lane drift per metre driven out on the loop.</summary>
        const float LaneDrift = 0.022f;

        /// <summary>Radius outside which a point is unambiguously in the outer loop, squared.</summary>
        const float LoopGuardSq = (TurfArena.HedgeOuter + 1f) * (TurfArena.HedgeOuter + 1f);
        /// <summary>Radius inside which a point is unambiguously in the court or the hub, squared.</summary>
        const float CourtGuardSq = (TurfArena.HedgeInner - 1f) * (TurfArena.HedgeInner - 1f);

        /// <summary>Seconds between the cheap "has somebody beaten me to it" checks.</summary>
        const float ReviewEvery = 0.45f;
        /// <summary>Fraction of an objective's original prize below which it is abandoned.</summary>
        const float PrizeCollapse = 0.55f;

        /// <summary>Barge worth, in arbitrary units, below which the hit is not worth the detour.</summary>
        const float BargeFloor = 0.55f;

        // =============================================================================================

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
        /// <summary>Square metres per second the current objective was worth when it was chosen.</summary>
        public float ObjectiveValue { get; private set; }

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

        // ---- the situation, refreshed once per review rather than once per frame ----
        float _ripe;        // 0 while the board is still mostly empty, 1 once it is used up
        float _deficit;     // 0 level or ahead, 1 ten points behind the leader
        float _edge;        // competence: this contestant's skill blended with the difficulty dial
        float _plazaNeed;   // 0 crown comfortably held, 1 nowhere near it
        int _leader = -1;

        // ---- the lane this gardener runs out on the loop ----
        float _lane;
        float _laneDir = 1f;

        // ---- objective bookkeeping, so a stale objective can be noticed and dropped ----
        float _reviewTimer;
        int _objSx = -1, _objSz = -1;
        int _objPrizeCells;

        /// <summary>
        /// This gardener's competence, 0..1: their own grade, lifted by the difficulty dial.
        ///
        /// Weighted toward <see cref="skill"/> so that raising <see cref="Difficulty"/> lifts the
        /// whole field without closing the gap between them. BRAMBLE at 0.665 and HORACE at 0.753
        /// stay 0.088 apart at every setting, which is the point: the difficulty of a match and the
        /// character of the people in it are two different things and a dial that flattens the
        /// second while raising the first has made the cast worse, not the game harder.
        /// </summary>
        float Edge => Mathf.Clamp01(skill * 0.55f + Mathf.Clamp01(Difficulty) * 0.45f);

        /// <summary>
        /// Seconds between plan reviews. Faster gardeners think oftener, and so does a harder match.
        ///
        /// The old interval ran to three seconds at the slow end, which on a board where the lead
        /// changes hands every few passes is a gardener acting on a picture of the arena that has
        /// stopped being true. At the default difficulty this is about a second and a half.
        /// </summary>
        float Think => Mathf.Lerp(thinkSlow, thinkFast, skill)
                     * Mathf.Lerp(1.3f, 0.62f, Mathf.Clamp01(Difficulty));

        public void Bind(int seed)
        {
            _rng = new System.Random(seed);
            ResetForMatch();
        }

        public void ResetForMatch()
        {
            _rng ??= new System.Random(competitor != null ? competitor.slot * 7919 + 13 : 1);
            _thinkTimer = 0f;
            _reviewTimer = 0f;
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
            ObjectiveValue = 0f;
            _objSx = _objSz = -1;
            _objPrizeCells = 0;
            _ripe = _deficit = _plazaNeed = 0f;
            _edge = Edge;
            _leader = -1;

            // A lane of one's own. Three gardeners that all run the outer loop at the graph's own
            // 37.5 m ring grind the same 3.3 m stripe for a hundred seconds and leave the twelve
            // metre band it sits in almost untouched — which since the arena grew to 45.5 m is over
            // half the board. Starting each of them somewhere different in the band, drifting in a
            // different direction, is worth more coverage than any amount of planning and it also
            // reads as three drivers with three lines.
            _lane = Range(LaneInner, LaneOuter);
            _laneDir = _rng.NextDouble() < 0.5 ? -1f : 1f;

            // Competitors are looked up fresh each match: the previous match's array may be full of
            // destroyed objects, and this is the one moment it is free to find out.
            _cast = null;
            _director = null;

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
                Assess();
                if (_rng.NextDouble() >= stubbornness * (1f - skill * 0.6f))
                {
                    Reroll();
                    ChooseObjective();
                }
                ConsiderBarge();
            }
            else
            {
                // Between reviews, one cheap question: is the thing I am driving at still there?
                //
                // Somebody else reaching an objective first used to cost a gardener the entire
                // remainder of its think interval — it would arrive at a sector another machine had
                // just finished, find nothing to take, and only then look up. An O(1) sector read
                // three times a second is a much better deal than a two-second drive to nowhere, and
                // it is the single cheapest piece of attentiveness in this class.
                _reviewTimer -= dt;
                if (_reviewTimer <= 0f)
                {
                    _reviewTimer = ReviewEvery;
                    if (ObjectiveSpent()) _thinkTimer = 0f;
                }
            }

            TickLane(dt);

            // A barge owns the wheel outright while it lasts. Blending it with the route was the
            // first attempt and it produced a machine that drifted vaguely toward a rival while
            // still trying to reach a sector, which connects with nothing and reads as indecision.
            if (TickBarge(dt)) { Drive(dt); return; }

            Route();
            Drive(dt);
        }

        // ------------------------------------------------------------------ reading the room

        /// <summary>
        /// What kind of match is this, right now?
        ///
        /// Four numbers, read once per review rather than once per frame, and every decision below
        /// leans on them. None of it is privileged: the fullness of the board, who is winning, who
        /// holds the hub and how long is left are all things the player can see from the chase
        /// camera and the HUD. The bots simply used not to look.
        /// </summary>
        void Assess()
        {
            _edge = Edge;

            var mask = TurfMask.Instance;
            if (mask == null) return;

            int slot = competitor.slot;
            _leader = mask.Leader;

            // HOW FULL IS THE BOARD. Loose grass gets scarcer all match, and the cheapest metre
            // available changes with it: empty ground while there is plenty, and once there is not,
            // ground somebody else is standing on — which is worth double, because they lose it.
            // Ramped across the share the board really visits rather than one it never reaches; see
            // the traced curve beside RipeFrom.
            _ripe = Mathf.Clamp01((RipeFrom - mask.NeutralShare) / (RipeFrom - RipeTo));

            // AM I LOSING. A gardener behind the leader should not play the same match as one ahead
            // of them, and until now they did.
            float mine = mask.Share(slot);
            _deficit = _leader == slot ? 0f
                     : Mathf.Clamp01((mask.Share(_leader) - mine) / DeficitSpan);

            // THE CROWN. Driven by hub-wide share, NOT by how much of any one sector is already
            // painted — that was the bug that had MARGOT relax the instant each plaza sector went
            // her colour and stop dead short of the 30% the crown actually costs.
            float hub = mask.PlazaShare(slot);
            _plazaNeed = Mathf.Clamp01((CrownTarget - hub) / CrownTarget);
            // Holding it is not a reason to leave. A crowned roller is half again as wide and it is
            // lost by wandering off, so keep a floor under the pull while it is ours.
            if (competitor.HasCrown) _plazaNeed = Mathf.Max(_plazaNeed, 0.25f);
        }

        /// <summary>
        /// Seconds of match left, or a large number when there is no director to ask.
        ///
        /// The clock is on the HUD. A gardener that starts a six-second drive with four seconds left
        /// has thrown the end of the match away, and that is the sort of mistake a person watching
        /// notices immediately.
        /// </summary>
        float SecondsLeft()
        {
            if (_director == null) _director = Object.FindFirstObjectByType<TurfDirector>();
            return _director != null ? _director.TimeRemaining : 999f;
        }

        // ------------------------------------------------------------------ what to do

        /// <summary>
        /// Choose a sector to go and work, valued in SQUARE METRES PER SECOND.
        ///
        /// The old version compared fractions — what proportion of each sector was empty, enemy or
        /// mine — which cannot tell a fat wedge of open loop from a scrap of grass behind a hedge,
        /// and subtracted a flat 0.026 per metre of travel, which is not a cost anybody can reason
        /// about. This one counts the actual prize in square metres and divides by an honest guess at
        /// the seconds it will take to collect: the drive out, the turn to face it, and the time
        /// spent working it once there. That single change does most of the work in this file. It
        /// makes distant riches beat nearby scraps only when they really are worth the trip, it
        /// makes a gardener finish a region before leaving it, and — because turning round is priced
        /// — it makes them sweep instead of pinball.
        ///
        /// Two hundred and fifty six sectors, a handful of integer reads each, about once a second
        /// per gardener. It allocates nothing.
        /// </summary>
        void ChooseObjective()
        {
            var mask = TurfMask.Instance;
            if (mask == null) { Objective = Vector3.zero; return; }

            BuildSectorTable();

            Vector3 me = competitor.Position;
            Vector3 fwd = competitor.Heading; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; else fwd.Normalize();

            int slot = competitor.slot;
            // Treating "no leader yet" as "I am the leader" is what keeps the leader premium out of
            // the sector loop entirely before the first Assess has run. It also keeps a -1 out of
            // SectorOwned, which indexes an array and would happily read backwards off the front of it.
            bool iLead = _leader < 0 || _leader >= TurfArena.Count || _leader == slot;
            float left = SecondsLeft();

            // ---- what a metre of each kind of ground is worth to THIS gardener, right now ----
            //
            // Base rates first. Empty ground is the unit. Enemy ground is worth more than empty even
            // early — it is a two point swing and it pays 6.25 times as much boost fuel — and its
            // value climbs as the loose grass runs out, until by the end of the match it is the only
            // ground there is. The leader's holdings carry an extra premium that grows with how far
            // behind we are, which is what keeps a four-way match from running away from anybody.
            float wFree = 1f;
            float wEnemy = 0.9f + 1.5f * _ripe;
            float wLead = 0.4f + 1.1f * _deficit;
            // Repainting your own ground is not neutral, it is a waste of the only scarce resource a
            // gardener has: seconds with the roller down.
            float wMine = 0.9f;
            // The front line — sectors held partly by us and partly by somebody else. Worth most
            // when we are AHEAD, because a leader's job is to stop the erosion rather than to go
            // looking for more, and that is the defensive instinct this cast had none of.
            float wFront = 0.35f + 0.8f * (1f - _deficit);

            float bandLoop = 1f, bandIn = 1f, crownDrive;

            switch (plan)
            {
                case Plan.Expander:
                    // The loop is fast, wide, undefended and — since the disc grew to 45.5 m —
                    // fully half the board. That bargain is BRAMBLE's whole personality and it is a
                    // good one. What it is not any more is an excuse to never steal: the old weights
                    // put enemy ground at 0.35 against empty at 2.4, which on an economy that pays
                    // out for steals meant an expander drove the entire match on an empty tank while
                    // everybody else ran on boost. Below, the ratio inverts by itself once the board
                    // fills up, so the character survives the whole match instead of only the first
                    // thirty seconds of it.
                    wFree *= 1.55f; wEnemy *= 0.75f; wLead *= 0.7f; wFront *= 0.7f;
                    bandLoop = 1.35f; bandIn = 0.85f;
                    crownDrive = 0.30f;
                    break;

                case Plan.Raider:
                    // Whoever owns the most, wherever they own it.
                    wFree *= 0.7f; wEnemy *= 1.5f; wLead *= 1.8f; wFront *= 0.8f;
                    crownDrive = 0.55f;
                    break;

                default:
                    // The hub, and holding what she has taken. The crown is the prize and the front
                    // line is the job.
                    wFree *= 0.9f; wEnemy *= 1.15f; wLead *= 0.9f; wFront *= 1.5f;
                    bandLoop = 0.9f; bandIn = 1.3f;
                    crownDrive = 1f;
                    break;
            }

            // How hard this gardener leans on the hub this review. Scaled by competence — reading a
            // threshold mechanic off a HUD is a skill — and by desperation, because a machine that
            // is losing should be reaching for the biggest multiplier on the board.
            float crownPull = crownDrive * _plazaNeed * CrownWorth * (0.45f + 0.55f * _edge)
                            * (1f + _deficit * 0.5f);
            // An unclaimed crown is an open goal and everybody should notice one.
            if (CrownHolder() < 0) crownPull += CrownWorth * VacantCrownPull * _edge;

            // How badly this gardener mis-reads the board. The ONLY thing the difficulty dial does
            // to the planner: at 1.0 it values sectors correctly, at 0 it may talk itself into
            // almost anything. Wrongness with a shape, rather than wrongness by omission.
            float haze = (1f - _edge) * 0.6f;

            // Zero and not negative infinity: a sector whose prize is negative is one this gardener
            // would be REPAINTING, and driving to the least bad of those is not a plan. If nothing on
            // the board scores at all the old objective simply stands.
            float bestValue = 0f;
            Vector3 best = Objective;
            int bestSx = -1, bestSz = -1, bestPrizeCells = 0;

            for (int sz = 0; sz < TurfMask.SectorRes; sz++)
            for (int sx = 0; sx < TurfMask.SectorRes; sx++)
            {
                int playable = mask.SectorPlayable(sx, sz);
                if (playable < MinSectorCells) continue;

                int mineC = mask.SectorOwned(sx, sz, slot);
                int enemyC = mask.SectorEnemy(sx, sz, slot);
                int freeC = playable - mineC - enemyC;
                int leadC = iLead ? 0 : mask.SectorOwned(sx, sz, _leader);

                // The prize, in square metres of ground actually worth putting a roller over.
                float prize = (freeC * wFree
                             + enemyC * wEnemy
                             + leadC * wLead
                             + Mathf.Min(mineC, enemyC) * wFront
                             - mineC * wMine) * CellArea;

                int idx = sz * TurfMask.SectorRes + sx;
                float radius = _sectorRadius[idx];
                prize *= radius > TurfArena.HedgeOuter ? bandLoop
                       : radius < TurfArena.HedgeInner ? bandIn : 1f;

                // The drive. Distance, plus the price of turning round to face it, plus the time it
                // takes to work the place once there.
                Vector3 centre = _sectorCentre[idx];
                float dx = centre.x - me.x, dz = centre.z - me.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                float inv = 1f / Mathf.Max(dist, 0.5f);
                float ahead = (dx * fwd.x + dz * fwd.z) * inv;          // 1 straight on, -1 behind
                float seconds = WorkSeconds + dist / CruiseEstimate + (1f - ahead) * 0.5f * TurnSeconds;

                float value = prize / seconds;

                // The hub. Note this rides on hub-wide need, not on this sector's own paint, so it
                // does not switch itself off halfway to the threshold; the per-sector "how much of
                // it is not already mine" only decides WHICH part of the hub to go and take.
                if (crownPull > 0f && radius < TurfArena.PlazaRadius)
                    value += crownPull * (1f - mineC / (float)playable);

                // Everything below scales the value, so a negative one has to leave first — a
                // discount applied to a loss is an improvement, and a gardener that reasons its way
                // to "the least unattractive patch of my own lawn" has stopped playing.
                if (value <= 0f) continue;

                // Do not start something there is no time to finish.
                if (seconds > left) value *= Mathf.Clamp01(left / seconds) * 0.5f;

                value *= 1f + ((float)_rng.NextDouble() * 2f - 1f) * haze;

                if (value <= bestValue) continue;
                bestValue = value;
                best = centre;
                bestSx = sx; bestSz = sz;
                bestPrizeCells = freeC + enemyC;
            }

            ObjectiveValue = bestValue;
            _objSx = bestSx; _objSz = bestSz;
            _objPrizeCells = bestPrizeCells;
            _reviewTimer = ReviewEvery;

            Objective = TurfArena.NearestPlayable(HoldOffTheFence(best + _aimJitter));
            Repath();
        }

        /// <summary>
        /// Has somebody else finished the sector we set off for?
        ///
        /// Compares the takeable ground there against what was on offer when it was chosen. Two
        /// integer reads. It is the difference between a gardener that arrives at an empty patch and
        /// looks around, and one that changes its mind halfway across the arena because it can see
        /// the north-east has gone purple while it was driving.
        /// </summary>
        bool ObjectiveSpent()
        {
            if (_objSx < 0 || _objPrizeCells <= 0) return false;
            var mask = TurfMask.Instance;
            if (mask == null) return false;

            // Everything in that sector that is not already ours is still worth a roller — free
            // ground and enemy ground alike. Which is simply "playable minus mine", and it is the
            // same quantity that was banked as the prize when the objective was chosen.
            int takeable = mask.SectorPlayable(_objSx, _objSz)
                         - mask.SectorOwned(_objSx, _objSz, competitor.slot);
            return takeable < _objPrizeCells * PrizeCollapse;
        }

        /// <summary>Slide an objective radially in to <see cref="FenceStandoff"/> if it is outside it.</summary>
        static Vector3 HoldOffTheFence(Vector3 p)
        {
            float r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
            if (r <= FenceStandoff || r < 1e-3f) return p;
            float k = FenceStandoff / r;
            p.x *= k;
            p.z *= k;
            return p;
        }

        /// <summary>Who is wearing the crown, or -1. Read off the competitors the director set it on.</summary>
        int CrownHolder()
        {
            var cast = Cast;
            if (cast == null) return -1;
            for (int i = 0; i < cast.Length; i++)
                if (cast[i] != null && cast[i].HasCrown) return cast[i].slot;
            return -1;
        }

        // ------------------------------------------------------------------ the cast, and the clock

        // Shared by all four gardeners and rebuilt only at a match reset. The old code called
        // Object.FindObjectsByType inside every barge review — a scene-wide type scan AND a fresh
        // array, several times a second, on a build whose whole performance budget is a WebGL tab at
        // sixty frames. Four competitors do not appear or vanish mid-match, so looking for them more
        // than once per match was never anything but waste.
        static TurfCompetitor[] _cast;
        static TurfDirector _director;

        static TurfCompetitor[] Cast
        {
            get
            {
                if (_cast == null || _cast.Length == 0)
                {
                    _cast = Object.FindObjectsByType<TurfCompetitor>(FindObjectsSortMode.None);
                    return _cast;
                }
                // A destroyed entry means the scene changed under us — a retry, a stage change —
                // and the list is worth rebuilding once rather than dereferencing forever.
                for (int i = 0; i < _cast.Length; i++)
                    if (_cast[i] == null)
                    {
                        _cast = Object.FindObjectsByType<TurfCompetitor>(FindObjectsSortMode.None);
                        break;
                    }
                return _cast;
            }
        }

        // The coarse sector geometry never changes, so working it out inside a 256-iteration loop
        // four times a second was pure arithmetic nobody needed. Built once for the life of the
        // process; two small arrays, and the last allocation this class ever makes.
        static Vector3[] _sectorCentre;
        static float[] _sectorRadius;

        static void BuildSectorTable()
        {
            if (_sectorCentre != null) return;
            int n = TurfMask.SectorRes * TurfMask.SectorRes;
            _sectorCentre = new Vector3[n];
            _sectorRadius = new float[n];
            for (int sz = 0; sz < TurfMask.SectorRes; sz++)
            for (int sx = 0; sx < TurfMask.SectorRes; sx++)
            {
                int i = sz * TurfMask.SectorRes + sx;
                Vector3 c = TurfMask.SectorCentre(sx, sz);
                _sectorCentre[i] = c;
                _sectorRadius[i] = Mathf.Sqrt(c.x * c.x + c.z * c.z);
            }
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
        /// What changed is that a barge now has to be WORTH IT. The old ranking was dominated by how
        /// near the target happened to be, so a gardener would abandon a good line to go and shove
        /// whoever was in last place, and then have to turn round. A shunt is five metres a second of
        /// displacement plus a lost couple of seconds; spent on the leader or on the machine wearing
        /// the crown that is one of the strongest plays available, and spent on somebody who is
        /// already losing it is a gift to the player. So the score is built from what interrupting
        /// this particular rival buys — are they ahead of me, do they hold the hub, are they the one
        /// running away with it — and then discounted by how far off my line they are. A gardener
        /// that is behind goes looking for contact more often; one that is ahead has better things
        /// to do than start fights.
        /// </summary>
        void ConsiderBarge()
        {
            if (_bargeRest > 0f || competitor == null) return;

            var cast = Cast;
            if (cast == null) return;

            float urge = plan == Plan.Raider ? 1.15f : plan == Plan.Warlord ? 1f : 0.55f;
            float appetite = Mathf.Clamp01(bargeAppetite * urge * (0.75f + 0.55f * _edge)
                                           + _deficit * 0.35f);
            if (_rng.NextDouble() > appetite) return;

            var mask = TurfMask.Instance;
            Vector3 me = competitor.Position;
            Vector3 fwd = competitor.Heading; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; else fwd.Normalize();
            float myShare = mask != null ? mask.Share(competitor.slot) : 0f;

            TurfCompetitor best = null;
            float bestScore = BargeFloor;

            for (int i = 0; i < cast.Length; i++)
            {
                var c = cast[i];
                if (c == null || c == competitor || c.mower == null) continue;

                float dx = c.Position.x - me.x, dz = c.Position.z - me.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > bargeRange || d < 1e-3f) continue;

                // What is knocking THIS one sideways actually worth?
                float worth = 0.35f;
                if (c.slot == _leader) worth += 0.9f;
                if (c.HasCrown) worth += 0.8f;
                if (mask != null) worth += Mathf.Max(0f, mask.Share(c.slot) - myShare) * 3f;
                if (TurfArena.InsidePlaza(c.Position)) worth += 0.35f;

                // Near enough to reach, and roughly where we were going anyway. A hit taken on the
                // way somewhere is nearly free; a U-turn to land one costs more than it gains, and
                // this is the term that stops them doing it.
                worth *= Mathf.Lerp(0.35f, 1f, 1f - d / bargeRange);
                float ahead = (dx * fwd.x + dz * fwd.z) / d;
                worth *= Mathf.Lerp(0.3f, 1.15f, (ahead + 1f) * 0.5f);

                if (worth <= bestScore) continue;
                bestScore = worth; best = c;
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
        /// machines sideways. The lead is scaled by competence, so a sharp gardener in a hard match
        /// connects and a clumsy one clatters through the space a rival has just left. That miss is
        /// worth as much as the hit: a bot that never whiffs is a bot nobody believes is driving.
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

            float lead = Mathf.Lerp(0.10f, 0.45f, _edge) * dist / Mathf.Max(competitor.Speed, 4f);
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
        /// Walk the route, decide what the next thing to steer at is, and pick a lane while doing it.
        ///
        /// A leg counts as reached generously. Waypoints are a device for choosing a heading, not
        /// places anybody has to visit, and a machine that doubles back to touch one exactly is a
        /// machine that has stopped painting to satisfy its own bookkeeping.
        ///
        /// Two things happen here that did not before. The route's loop legs get pulled onto this
        /// gardener's own LANE, and the "am I threading a gateway" test finally means what it says.
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

            // THE LANE.
            //
            // The navigation graph's outer ring is twenty nodes at exactly TurfArena.LoopMid, 37.5 m.
            // Follow it faithfully and a gardener repaints one 3.3 m stripe for the whole match while
            // the twelve metre band it sits in — from the hedge at 33 out to the touchline at 45.5,
            // 3083 m², more than half the playable board since the arena grew — goes almost untouched.
            // Nothing was wrong with the routing; the ring is a road, not a lawn. So a transit leg out
            // on the loop is slid radially onto whichever lane this gardener is currently running,
            // and the lane crawls across the band as they drive (see TickLane). Three bots on three
            // lanes drifting in two directions cover the outfield properly AND stop looking like a
            // train.
            //
            // Only for TRANSIT, and only when the next two legs are also out on the loop: a route
            // from one side of the loop to the other goes through the middle when that is shorter,
            // and its first legs are gate mouths at 36.5 m which must be hit on their centreline or
            // the machine puts its nose in a hedge. And never the final leg — that one is the
            // objective itself, a real place that was chosen for a reason.
            //
            // The lane has to be applied to the LEG TEST as well as to the steering target, and this
            // is not a detail. A lane sits up to 5.5 m off the ring, which is exactly legRadius: a
            // gardener that drove perfectly to a lane point would arrive 5.5 m from the node the
            // route is measured against, fail to count the leg as reached, fall into the arrival
            // branch instead and re-plan the whole objective — every leg, all the way round. The
            // route is being walked in lane coordinates, so it has to be MEASURED in them too.
            bool lane = LaneHere();

            // Advance past every leg already behind us, not just one. A boost down a spoke can
            // clear two or three waypoints between frames, and stepping one at a time would leave
            // the machine steering back at a point it passed half a second ago.
            while (_leg < _path.Count - 1)
            {
                Vector3 leg = LaneAdjust(_path[_leg], lane);
                leg.y = me.y;
                if (Vector3.Distance(me, leg) > legRadius) break;
                _leg++;
                lane = LaneHere();
            }

            _waypoint = LaneAdjust(_path[Mathf.Min(_leg, _path.Count - 1)], lane);

            // Off the route entirely — shunted, spun, or shoved through a gap by somebody else.
            if (Vector3.Distance(me, _waypoint) > 30f && _repathTimer <= 0f) Repath();

            // AM I THREADING A GATEWAY? Reported for the trace, and the gate on boost below.
            //
            // This used to be "is the waypoint's radius between 25 and 38 metres", which sounds like
            // a description of the hedge band and is not one. The hedge is at 30..33; the outer loop
            // ring the whole cast drives is at 37.5; 37.5 is inside 25..38. So the test declared the
            // entire outer loop to be a gateway and boost was structurally forbidden on the biggest,
            // straightest, most open half of the arena for every gardener in every match. That one
            // off-by-a-band is most of why they looked slow.
            //
            // The honest question is whether the LEG crosses the wall, and the honest answer is to
            // look at both ends: both outside the hedge is a loop run, both inside is the court or
            // the hub, and anything else is a gateway and wants placing rather than arriving fast.
            bool clear = (Outside(me) && Outside(_waypoint)) || (Inside(me) && Inside(_waypoint));
            RoutingVia = clear ? -1 : 0;

            // Threading a gateway, so aim a little off the middle of it — by however much THIS
            // gardener habitually misjudges one.
            //
            // Which is the entire stated purpose of gapError, and the previous version rolled the
            // number every single review and then never applied it to anything at all. The tooltip
            // beside the field still claims it is "what makes them clip a choke instead of threading
            // it, which is most of their visible character", and it was doing none of that. Tangent
            // to the wall, so it slides along the opening rather than into or out of it; a fraction
            // under a metre at its worst, against gaps 7.2 and 10 m wide. Character, not sabotage —
            // BRAMBLE at 0.665 skill wanders 0.8 m off the centreline and occasionally pays for it,
            // which is exactly the sort of mistake the player should be able to watch happen.
            float rw = Mathf.Sqrt(_waypoint.x * _waypoint.x + _waypoint.z * _waypoint.z);
            if (!clear && rw > 1e-3f)
            {
                float tx = -_waypoint.z / rw, tz = _waypoint.x / rw;
                _waypoint.x += tx * _gapJitter;
                _waypoint.z += tz * _gapJitter;
            }
        }

        /// <summary>Unambiguously out in the loop, clear of the hedge.</summary>
        static bool Outside(Vector3 p) => p.x * p.x + p.z * p.z > LoopGuardSq;
        /// <summary>Unambiguously inside the wall, in the court or the hub.</summary>
        static bool Inside(Vector3 p) => p.x * p.x + p.z * p.z < CourtGuardSq;

        /// <summary>
        /// Is the machine on a stretch of route it may run its own lane on?
        ///
        /// Out on the loop itself, not on the final leg, and with this leg and the next two all
        /// clear of the hedge. That last clause is what keeps the lane off a gateway approach: a
        /// route across the loop takes the short way through the middle when there is one, and its
        /// first legs are gate mouths at 36.5 m that have to be met on their centreline.
        /// </summary>
        bool LaneHere()
        {
            if (_leg >= _path.Count - 1) return false;
            if (!Outside(competitor.Position)) return false;
            for (int i = _leg; i <= _leg + 2 && i < _path.Count; i++)
                if (!Outside(_path[i])) return false;
            return true;
        }

        /// <summary>Slide a point radially onto this gardener's lane, keeping its bearing.</summary>
        Vector3 LaneAdjust(Vector3 p, bool apply)
        {
            if (!apply) return p;
            float r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
            if (r < 1e-3f) return p;
            float k = _lane / r;
            p.x *= k;
            p.z *= k;
            return p;
        }

        /// <summary>
        /// Crawl the lane across the outfield band as the gardener drives it.
        ///
        /// Tied to distance travelled rather than to the clock, so a machine parked in a choke does
        /// not silently drift its line across twelve metres of arena while standing still. At the
        /// rate here a gardener sweeps the band about twice over a hundred second match, which with a
        /// 3.3 m roller is roughly the coverage the outfield deserves, and it bounces off both edges
        /// rather than wrapping so the sweep is continuous.
        /// </summary>
        void TickLane(float dt)
        {
            if (competitor == null || !Outside(competitor.Position)) return;
            _lane += _laneDir * competitor.Speed * dt * LaneDrift;
            if (_lane >= LaneOuter) { _lane = LaneOuter; _laneDir = -1f; }
            else if (_lane <= LaneInner) { _lane = LaneInner; _laneDir = 1f; }
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
                // Arrived, and the next objective is one frame away. Keep the throttle UP through
                // the handover: this used to drop to 0.55 and coast, which on a board where the
                // roller is the only scarce resource is a gardener choosing to paint a third less
                // ground for no reason at all, several times a minute, for the whole match.
                _thinkTimer = 0f;
                Throttle = Mathf.MoveTowards(Throttle, ArriveThrottle, dt * ThrottleRate);
                Steer *= 0.85f;
                Handbrake = Boost = false;
                return;
            }

            Vector3 dir = to / dist;
            float signed = Vector3.SignedAngle(fwd, dir, Vector3.up);
            float need = Mathf.Abs(signed);
            bool behind = need > 118f;

            if (behind && dist < 7f)
            {
                Throttle = Mathf.MoveTowards(Throttle, -0.8f, dt * 5f);
                Steer = Mathf.Clamp(-signed / steerFullLock, -1f, 1f) * -1f;
                Handbrake = Boost = false;
                return;
            }

            Steer = Mathf.Clamp(signed / steerFullLock, -1f, 1f);

            // THROTTLE.
            //
            // The old rule was a straight interpolation from 0.42 to 1 on heading error alone, and
            // it is the reason the trace caught a gardener cruising at 0.42 with the arena in front
            // of it. Heading error on its own is not a corner: a waypoint thirty metres away and
            // sixty degrees off is not a corner at all, it is an arc you take flat, and the old rule
            // slowed down for it — while the mower's own physics punish that twice over, because
            // yaw authority is 165°/s at a standstill and 88 at speed and it never got to either.
            //
            // A corner costs speed only when it has to be taken NOW. So: how far round is it, minus
            // how much room there is to arc through it. Out on the loop, where consecutive legs turn
            // about eighteen degrees eleven metres apart, this sits at essentially full throttle
            // where the old rule sat at 0.89 and could not boost. Into a hairpin at four metres it
            // still lifts, and how far it lifts is what competence buys: a sharp gardener carries
            // more speed through the same corner than a clumsy one, which is exactly the difference
            // between two drivers and not a difference in their machines.
            float room = Mathf.Clamp01(dist / CornerRoom);
            float tight = Mathf.Clamp01(need / CornerHard) * (1f - room * RoomRelief);
            float floorThr = Mathf.Lerp(ThrottleFloorSoft, ThrottleFloorSharp, _edge);
            Throttle = Mathf.MoveTowards(Throttle, Mathf.Lerp(1f, floorThr, tight), dt * ThrottleRate);

            // BOOST, on a straight worth spending it on: pointed roughly the right way, with room,
            // not threading a gateway, and with a leg after this one that carries on the same way.
            // The gateway test is the one fixed in Route — until it was, this line was dead on the
            // entire outer loop, which is over half the board.
            float align = Mathf.Clamp01(1f - need / 90f);
            bool straightAhead = align > 0.72f && dist > 7f && RoutingVia < 0 && LegsAlign();
            Boost = _barging != null
                ? align > 0.66f && dist > 4f
                : straightAhead && _edge > 0.3f && competitor.Speed > 3f;

            // A handbrake flick for a corner that is genuinely too tight to steer round, and close
            // enough that steering round it is not an option. Sparingly: the drift model is the
            // player's toy, it bleeds 2.6 m/s per second, and a bot using it constantly makes the
            // arena look like it is on ice.
            Handbrake = !behind && need > 88f && dist > 4f && dist < 16f && competitor.Speed > 7f;
        }

        /// <summary>
        /// Does the leg after this one carry on in roughly the same direction?
        ///
        /// What turns "there is distance ahead" into "there is a STRAIGHT ahead", which is the only
        /// thing worth spending boost on. True at the end of a route as well: the last leg has
        /// nothing after it to disagree with, and arriving at the objective quickly is fine.
        ///
        /// The threshold is loose enough to admit the outer loop, whose consecutive ring legs turn
        /// eighteen degrees against each other. A long arc IS a straight as far as a boost is
        /// concerned — the mower has 60°/s of yaw even while boosting and the loop needs 22.
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
            return Vector3.Dot(first.normalized, next.normalized) > StraightDot;
        }

        // ------------------------------------------------------------------ reacting

        /// <summary>
        /// Somebody just took ground off this gardener.
        ///
        /// Not a scripted revenge — the next review simply happens sooner, and with the front-line
        /// term in the objective score a gardener who is ahead now genuinely values the contested
        /// sector it is being robbed in, so the review tends to send it back to defend rather than
        /// merely to look up. A big enough theft cancels a barge outright: whatever we were driving
        /// at, the machine currently eating our lawn is a better use of the next two seconds.
        /// </summary>
        public void NotifyRobbed(float squareMetres)
        {
            if (squareMetres < 3f) return;
            _thinkTimer = Mathf.Min(_thinkTimer, 0.35f);
            if (squareMetres > 25f && _barging != null)
            {
                _barging = null;
                _bargeRest = 0f;
                _thinkTimer = 0f;
            }
        }
    }
}
