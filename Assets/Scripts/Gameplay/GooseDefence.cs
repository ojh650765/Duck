using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuckMow
{
    /// <summary>
    /// GREYBOX — the goose rally. Capsules and coloured boxes on purpose.
    ///
    /// The round ends, the duck drives into a small arena beside its own flower garden, and a goose
    /// comes at the beds. Hit it and it goes back over the way it came — and comes back faster. The
    /// rally escalates until somebody misses, and a miss flattens flowerbeds that do not grow back.
    ///
    /// THE DESIGN, in the order the decisions matter:
    ///
    ///   * It runs on the SAME chase camera as the mowing, driving the SAME mower. No camera of its
    ///     own, no control scheme of its own — the phase is a continuation of the round rather than a
    ///     minigame. The framing work lives in CameraDirector as modifiers on Chase; see
    ///     SetDefenceSubject there.
    ///   * The three tiers fall out of ONE geometric rule instead of three hand-tuned timers: how
    ///     close the goose is when you honk. See <see cref="Tier"/>.
    ///   * Escalation is the goose getting faster. Because the tiers are radii, a faster goose spends
    ///     less time inside each of them, so every window tightens by itself as the rally grows.
    ///     Nothing schedules the difficulty — the rally IS the difficulty.
    ///   * What is defended is a garden planted along the outline the player actually mowed, so the
    ///     stake is their own work rather than a garden. See DefenceArena.PlantFromPicture.
    ///
    /// On probation, and built to be cut cheaply: two files, everything created at runtime, no
    /// prefab, no material asset, no mesh asset, no scene reference, no new scene, and it does not
    /// run at all unless <see cref="GameDirector.defencePhaseEnabled"/> is ticked.
    /// </summary>
    public class GooseDefence : MonoBehaviour
    {
        public enum Phase { Idle, Brief, Live, Settle, Awarding, Done }

        /// <summary>
        /// How well the goose was met, decided by how far away it was when the horn went.
        ///
        /// One radius set, three tiers, and the timing windows are a CONSEQUENCE rather than a setting:
        /// a goose closing at speed v is inside radius r for 2r/v seconds.
        ///
        /// The radii grew with the pitch, and the reason is the best thing in this phase. Closing speed
        /// is the goose PLUS the mower's own approach, so on open ground the player sets their own
        /// difficulty. Standing off and letting it come, at the opening dive speed, the windows are
        /// 0.94 / 0.54 / 0.28 s — generous. Charging it head-on at 10 m/s they collapse to
        /// 0.59 / 0.34 / 0.18 s. Driving at the goose is faster and more aggressive AND cuts your own
        /// window by a third. Nothing implements that trade; it falls out of measuring the tier as a
        /// distance on a pitch you are free to move around, which is exactly why the small box had to go.
        ///
        /// Escalation rides on top: every exchange raises v, so by the fifth the head-on windows are
        /// 0.38 / 0.22 / 0.11 s. Perfect becomes nearly impossible while Normal stays reachable, which
        /// is why a long rally ends in a scramble or a miss rather than in a wall.
        /// </summary>
        public enum Tier { Miss, Normal, Good, Perfect }

        enum GooseState { Gone, Diving, Launched, Returning, Crashing }

        // ------------------------------------------------------------------ tuning

        [Header("Shape of the phase")]
        public float briefSeconds = 1.4f;
        /// <summary>
        /// THE BUDGET, NOT THE LENGTH. The raid ends when a garden falls; this only stops it running
        /// forever if nothing ever does.
        ///
        /// It replaces `liveSeconds = 12f`, which was the single worst thing in this phase. Twelve
        /// seconds is about three exchanges at the tuned dive speeds, so the contest stopped mid-argument
        /// for no reason the player could see — the player's own verdict was "왜 거위는 3번만 오고 끝남?
        /// 게임스럽지 않아": why do only three geese come and then it ends, that is not game-like. They
        /// were right, and a stopwatch is simply the wrong shape for this beat. A raid on a clock has no
        /// win, no loss, and no reason for any single exchange to matter; nothing the player does can
        /// bring it closer to an end, so every exchange is worth exactly the same as every other one.
        ///
        /// So the clock stops being the condition and becomes the ceiling — the same rule every other
        /// beat here is under, and it is generous on purpose: a minute holds fourteen or fifteen
        /// exchanges, four times what the old timer allowed, and reaching it at all means neither garden
        /// was breaking. When it IS reached the raid is decided on beds and said so plainly in the log,
        /// so a budget finish is never mistaken for a real result.
        /// </summary>
        [Tooltip("Ceiling on the whole contest, in seconds. NOT the length — the raid normally ends when " +
                 "a garden reaches its damage cap. This only stops it running forever if the geese stop " +
                 "arriving or the player parks.")]
        public float raidBudgetSeconds = 60f;
        [Tooltip("Ceiling on the tail, so a goose mid-crash when the clock runs out still finishes. " +
                 "A cap rather than a duration: cutting a crash short would make the damage depend " +
                 "on when the timer happened to land.")]
        public float settleMaxSeconds = 3.5f;

        [Header("The parry")]
        [Tooltip("Metres. Outside this the horn does nothing.")]
        public float parryRadius = 8f;
        [Tooltip("Half-angle of the strike cone, in degrees, measured from the machine's nose on the " +
                 "horizontal. Direction decides whether the horn connects at all; distance decides the " +
                 "grade. 60 is forgiving enough to be fun and tight enough that facing the wrong way is " +
                 "a real mistake — a full 180 would be the old bubble with extra steps.")]
        [Range(20f, 120f)] public float parryHalfAngle = 60f;
        [Tooltip("Metres for a GOOD hit — honours the aim, so it can reach the opponent.")]
        public float goodRadius = 4.6f;
        [Tooltip("Metres for a PERFECT hit. Slow motion, the hardest launch, the loudest crowd.")]
        public float perfectRadius = 2.4f;
        [Tooltip("Seconds the horn is dead after honking at nothing. The anti-mash rule: without it " +
                 "the window is not a window, it is a key you can hold down.")]
        public float whiffRecovery = 0.4f;

        [Header("The rally")]
        [Tooltip("Metres per second the goose dives at on the first exchange.")]
        public float diveSpeed = 17f;
        [Tooltip("Added per successful parry. This is the escalation, and because the tiers are radii " +
                 "it tightens all three windows at once.")]
        public float diveSpeedPerRally = 3f;
        /// <summary>
        /// Added per exchange the RAID has seen, and never given back.
        ///
        /// The rally bonus above is the only escalation there was, and it resets to zero the moment a
        /// goose gets through. Over three exchanges that is invisible; over a match of fifteen it is
        /// backwards — the player who is struggling, and who has therefore missed, is handed the OPENING
        /// dive speed again and again, while the player who is clean reaches <see cref="diveSpeedMax"/>
        /// by the sixth parry and spends the rest of the contest on a plateau. One curve cannot be both
        /// the combo pressure and the difficulty of the match.
        ///
        /// So they are separated. This term is the match ramp: slow, monotonic, and indifferent to how the
        /// last exchange went, so a raid always gets harder the longer it lasts. The rally bonus stays
        /// what it was — fast, sharp, and lost with the combo — which is what makes a run of clean parries
        /// feel like a run.
        /// </summary>
        [Tooltip("Added per exchange the raid has seen. Never reset by a miss — this is the MATCH ramp, " +
                 "where diveSpeedPerRally is the combo. See the summary.")]
        public float diveSpeedPerExchange = 0.55f;
        [Tooltip("Metres per second the dive is capped at. Raised from 34 to give a fifteen-exchange " +
                 "match somewhere to climb to: at 40 the flight is still 1.30 s over the 52 m approach, " +
                 "which is the side of a second the parry has to stay on to remain a timing test.")]
        public float diveSpeedMax = 40f;
        /// <summary>
        /// Exchanges at which the raid is running at full pace.
        ///
        /// Speed CANNOT be the whole escalation, and this is the field that admits it. The parry windows
        /// are 2r/v, so at the cap the outer one is 0.40 s standing still and 0.26 s driving into the
        /// bird — push v any further and the timing test becomes a coin flip, which is exactly the line
        /// approachDistance's note draws. A ceiling on v is therefore not a plateau to be fixed but a
        /// fairness bound to be respected.
        ///
        /// What escalates past it is PACE and READABILITY: the gap between geese closes, the turnaround
        /// after a return shortens, and the entry bearing spreads wider so the player cannot pre-aim.
        /// None of the three touches the window itself, so a late exchange is relentless rather than
        /// unfair, and the contest still tightens after the bird has stopped getting faster.
        /// </summary>
        [Tooltip("Exchanges at which the gaps between geese are at their shortest and the entry bearing " +
                 "at its widest. The escalation that continues after diveSpeedMax binds.")]
        public int heatFullExchanges = 12;
        [Tooltip("Metres out the goose turns in from. Sized to the pitch: at 52 m it enters from over " +
                 "the OPPONENT'S end and flies the length of the ground, so the player always sees it " +
                 "coming and always has room to get back. Flight is 3.06 s on the first exchange down " +
                 "to 1.53 s at the speed cap — never under a second, or the parry stops being a timing " +
                 "test and becomes a coin flip.")]
        public float approachDistance = 52f;
        [Tooltip("Degrees above the horizon it comes in at. Shallow enough to read as a charge " +
                 "rather than as a bombing run.")]
        public float diveElevation = 20f;
        /// <summary>
        /// Metres per second a SCRAMBLED goose is thrown at. Carries about 16 m on the arc below.
        ///
        /// Dropped from 22, and this is the change that makes a rally possible at all. At 22 a scramble
        /// flew nearly the whole way to the far garden, so every exchange cost a long outbound flight
        /// plus the return — a cycle near 5 s, which in a twelve-second phase is two exchanges and a
        /// bit. The escalation could not be seen because it never got to happen: rallyEnergyFull was
        /// six and the arithmetic could not reach three.
        ///
        /// So the two throws are now genuinely different shots. A scramble is a short pop that comes
        /// straight back, which is what keeps a rally rallying; a PLACED hit is the big clearance that
        /// reaches their flowers and costs most of a second of extra airtime to do it. That turns "keep
        /// the rally going" versus "cash it in on their garden" into a real trade with a real price,
        /// which it was not when both throws went the same distance.
        /// </summary>
        [Tooltip("Metres per second a scrambled goose is thrown at. See the summary — this number is " +
                 "what decides whether a rally can build.")]
        public float launchSpeed = 14f;
        [Tooltip("Downward acceleration on a thrown goose, m/s². Well under real gravity: full " +
                 "gravity put the arc down at 27 m, short of the far garden, and a bird punted by a " +
                 "mower wants to hang long enough to be watched anyway.")]
        public float launchGravity = 6.86f;
        [Tooltip("Seconds a PLACED throw spends in the air. Only used when the hit earned placement. " +
                 "1.8 s over the pitch's 50 m gives a 2.8 m arc — a clearance rather than a bunt, and " +
                 "still cheap enough that cashing a rally in does not eat the whole window.")]
        public float launchFlightSeconds = 1.8f;
        [Tooltip("Degrees of scatter on a scrambled throw. What makes a normal hit unreliable in " +
                 "DIRECTION rather than merely quieter than a good one.")]
        public float scrambleScatter = 14f;
        [Tooltip("Exchanges at which the camera is fully wound up.")]
        public int rallyEnergyFull = 6;
        [Tooltip("Seconds of breathing space after a crash before the next goose comes in.")]
        public float regroupSeconds = 1.1f;
        [Tooltip("Degrees off the mower's heading within which a launch genuinely reaches the " +
                 "opponent's garden. Aim the machine, not a cursor.")]
        public float redirectCone = 34f;

        [Header("The damage")]
        [Tooltip("Metres around a crash in which beds are flattened.")]
        public float crashRadius = 2.6f;
        /// <summary>
        /// Most beds one crash can flatten, before the cap is considered.
        ///
        /// Two rather than three, worked out against the cap rather than picked. Against the current
        /// five-bed cap that is THREE geese through and the garden is finished — the third one only has
        /// one bed left to take. Three is the right number of chances: two makes a bad opening minute
        /// fatal, four makes the first two concessions free.
        /// </summary>
        public int bedsPerCrash = 2;
        /// <summary>
        /// THE MATCH. Whichever garden loses this fraction of its beds first has lost the raid.
        ///
        /// It was a CEILING — a limiter that stopped damage accumulating — and it is now the win and the
        /// loss condition, which is the whole reshaping of this phase. As a ceiling it was never the thing
        /// the player was playing toward, so the raid needed a clock to stop it, and a clock is what made
        /// three geese arrive and the contest halt mid-argument. As a condition it gives the raid a shape:
        /// defend yours, wreck theirs, and every single exchange moves one of the two numbers.
        ///
        /// Raised from 0.18 because 0.18 no longer meant what its own note claimed. That note reasoned
        /// against "roughly 26 beds", where the widened garden actually holds SEVENTEEN a side — so the
        /// intended five-bed ceiling had quietly become three, and three at two beds a crash is a match
        /// decided by the second concession. 0.30 of seventeen is five, which is the number that comment
        /// was aiming at all along.
        ///
        /// Counted in beds rather than in cut-mask cells, and that is the payoff of defending a garden
        /// instead of the mown picture: "two beds left" is a number the player reads off the ground and
        /// off the HUD, where "the cut mask gained 229 cells" was invisible.
        /// </summary>
        [Range(0f, 0.5f)] public float damageCapFraction = 0.30f;

        /// <summary>
        /// How good the rival is in goal, as the two numbers that decide a save.
        ///
        /// The rival needs an attempt of their own because the player asked for one — "핑퐁된 거위는 두번째
        /// 상대가 튕기거나 튕겨내지 못할때 소멸": a ponged goose is finished when the second party either
        /// returns it or fails to. Without that, a struck goose simply arrived and scored, so the far end of
        /// the pitch was a target rather than an opponent and there was nobody over there to beat.
        ///
        /// Not a bare dice roll and not a bare radius. The player's own parry is a CONE out of the machine's
        /// nose — direction decides contact, distance decides grade — and the keeper's analogue of that is
        /// REACH: they are one capsule standing in a sixteen-metre goalmouth, so a bird put down beside
        /// them is theirs and a bird put down at the far end of their garden is not. That is a thing the
        /// player can aim AT, learn, and beat on purpose, which a percentage behind the scenes never is.
        /// </summary>
        [Header("The rival in goal")]
        [Tooltip("Chance the rival returns a goose that comes down within reach of them. Under 1 on " +
                 "purpose: even a well-placed bird is sometimes saved, so beating them reads as beating " +
                 "somebody rather than as passing a check.")]
        [Range(0f, 1f)] public float keeperSkill = 0.72f;
        /// <summary>
        /// Metres either side of the rival they can cover against a ball skipped in from the far end.
        ///
        /// Four and a half, not six and a half, and the number has to be argued against the GOALMOUTH
        /// rather than picked for feel. Their garden is sixteen metres across — <c>gardenHalfWidth</c> is
        /// eight — so a 6.5 m reach covers about four fifths of it and the claim above, that the gap between
        /// their reach and their frontage is the thing the player aims at, was very nearly false: there was
        /// hardly any gap to aim into. At 4.5 they hold the middle and the two ends are open, which is a
        /// shape the player can see from their own garden and play toward.
        ///
        /// It applies ONLY to a ball the player punted in. A goose diving at their own beds they watched
        /// cross the whole pitch, and reach does not enter into it — see <see cref="KeeperAttempt"/>.
        /// </summary>
        [Tooltip("Metres either side of the rival they can cover against the player's throws. Well under " +
                 "their garden's 8 m half-frontage on purpose — that difference IS what the player aims at.")]
        public float keeperReach = 4.5f;

        [Header("Impact")]
        [Tooltip("Seconds of near-frozen time on a normal hit. Below the 70-110 ms band on purpose — " +
                 "that band is for STRONG impacts, and a normal hit that stops as hard as a perfect " +
                 "one leaves the tiers indistinguishable.")]
        public float hitStopNormal = 0.045f;
        public float hitStopGood = 0.075f;
        public float hitStopPerfect = 0.11f;
        [Range(0.05f, 1f)] public float slowMotionScale = 0.34f;
        [Tooltip("Seconds of REAL time the slow-motion beat lasts. Short: it is punctuation, and a " +
                 "long one eats the reaction time the next dive needs.")]
        public float slowMotionSeconds = 0.22f;
        [Tooltip("Strength handed to MowerController.Bonk. Reuses the gnome-impact model, which " +
                 "recoils and spins the machine while deliberately leaving grip and steering live — " +
                 "recoil without taking control away, which is exactly the brief.")]
        [Range(0f, 1f)] public float bonkNormal = 0.35f;
        [Range(0f, 1f)] public float bonkGood = 0.6f;
        [Range(0f, 1f)] public float bonkPerfect = 0.9f;

        [Header("Greybox")]
        public bool showReadout = true;
        [Tooltip("Fixed so two runs can be compared. 0 draws a fresh rally every time.")]
        public int seed = 1337;

        // ------------------------------------------------------------------ result

        /// <summary>
        /// How the raid ended. The thing a timer could not give it.
        ///
        /// Four values rather than two, because a budget finish is a different animal from a garden
        /// falling and the award is entitled to know which: a KNOCKOUT is a garden reduced to its cap in
        /// front of the player, and a DECISION is the budget expiring with both gardens still standing, in
        /// which case the raid goes to whichever side has lost fewer beds. Sport has both and they are not
        /// worth the same, so <see cref="ComputeAward"/> pays them differently.
        /// </summary>
        public enum MatchResult { None, Won, Lost, Draw }

        public struct Outcome
        {
            public int parries, perfects, goods, normals;
            public int crashes, whiffs, longestRally;
            /// <summary>Throws that came down on the opponent's beds. The point of playing well.</summary>
            public int hitsOnOpponent;
            /// <summary>Throws that came down on the player's OWN beds. The cost of a scrappy hit.</summary>
            public int ownGoals;
            /// <summary>Geese the rival returned. How often the far end actually held.</summary>
            public int keeperSaves;
            /// <summary>
            /// Geese that came down in the rival's beds with no help from the player, because a raid
            /// attacks both ends. Counted apart from <see cref="hitsOnOpponent"/> on purpose: it moves the
            /// match toward a win but it is not the player's doing, so the award must not pay for it.
            /// </summary>
            public int rivalConceded;
            public int myBedsLost, myBedsTotal;
            public int theirBedsLost, theirBedsTotal;
            /// <summary>Who won, and by what. Set once, in ConcludeRaid.</summary>
            public MatchResult result;
            /// <summary>True when the budget stopped the raid rather than a garden falling.</summary>
            public bool onBudget;
            public string opponent;
        }

        public Phase State { get; private set; } = Phase.Idle;
        public bool Finished => State == Phase.Done;
        public Outcome Result => _outcome;

        // ---- the match, for the HUD ----
        //
        // Read every frame by DefenceHud, which is the only thing on screen able to answer "how am I
        // doing" — and the readout the old phase was missing entirely. A timer told the player how long
        // they had left; these tell them what they are about to win or lose, which is the only number
        // worth a corner of the frame.

        /// <summary>Who is winning, or None while the raid is still open.</summary>
        public MatchResult Match => _outcome.result;
        /// <summary>Beds the player's garden can still lose before the raid is lost. Zero means gone.</summary>
        public int MyBedsToSpare => Mathf.Max(0, _myCap - (_arena != null ? _arena.PlayerBedsLost : 0));
        /// <summary>Beds the rival's garden can still lose before the raid is won.</summary>
        public int TheirBedsToSpare => Mathf.Max(0, _theirCap - (_arena != null ? _arena.OpponentBedsLost : 0));
        /// <summary>Beds either garden may lose in total. What the two numbers above started at.</summary>
        public int BedsAtStake => _myCap;
        public string OpponentName => string.IsNullOrEmpty(_outcome.opponent) ? "THE FIELD" : _outcome.opponent;
        /// <summary>
        /// Longest the whole phase can possibly take, for a host that budgets it from OUTSIDE.
        ///
        /// GameDirector guards the defence state with a wall-clock of its own, and that guard was a
        /// hard-coded 30 s sized against a twelve-second raid. A minute-long budget walks straight through
        /// it, and the failure would have looked like the phase abandoning itself. Derived rather than
        /// re-typed, so the two can never drift again.
        /// </summary>
        public float WorstCaseSeconds =>
            briefSeconds + raidBudgetSeconds + settleMaxSeconds + awardHoldSeconds + 2f;

        /// <summary>
        /// The marks the bench adds to the round for how the defence went. Survives until the next
        /// <see cref="Begin"/>, because the verdict beat asks for it well after the phase has shut down.
        /// Zero if the phase never finished, so an aborted or skipped raid awards nothing.
        /// </summary>
        public int Award { get; private set; }

        [Header("The award")]
        [Tooltip("Most the defence can add to a round out of 30. Eight is a fifth of the artistry " +
                 "ceiling: enough that playing the phase well is worth a grade, not enough that it " +
                 "beats drawing the picture properly.")]
        public int awardCeiling = 8;
        [Tooltip("Worst the defence can cost. Negative on purpose — if the floor were zero, ignoring " +
                 "the geese would be exactly as good as never having faced them, and the phase would " +
                 "be a free bonus rather than something at stake.")]
        public int awardFloor = -3;

        /// <summary>
        /// What the bench makes of the defence, as one small integer.
        ///
        /// REWEIGHTED FOR A MATCH, and the old weights are worth recording because they were reasoned
        /// correctly for a phase that no longer exists. They were: two for a perfect, ONE FOR EVERY GOOD
        /// PARRY, one for a goose put on the leader's flowers, one for a rally of three, less one per
        /// crash and per own goal. Sound arithmetic over three exchanges — and arithmetic that pins the
        /// award to the +8 ceiling every single time over fifteen. A raid now contains ten or twelve good
        /// parries because parrying is how you survive it, so paying a mark for each one pays a mark for
        /// breathing, and once the clamp saturates the award stops distinguishing anything at all.
        ///
        /// So the parry count comes out and the CONSEQUENCE goes in. Under the new loop a parried goose
        /// travels to a real opponent who tries to keep it out, which means a good parry is already paid
        /// for — by reaching them. Every remaining term is scarce and none of them is available by
        /// surviving:
        ///
        ///   * the RESULT, which is the whole point of there being one: ±3 for a garden actually broken,
        ///     ±1 when the budget expired and the raid went on beds. Nothing else in the formula can say
        ///     "you won", and a match with a winner has to say so.
        ///   * TWO for every goose put down in the rival's beds past their keeper. The decisive act of the
        ///     phase, capped at three or four by the damage cap, and the only term the player can build
        ///     a plan around.
        ///   * ONE for a perfect strike, down from two. There can be a dozen now.
        ///   * one for a rally of four or more (showmanship), raised from three — three is a Tuesday in
        ///     a match this long.
        ///   * less one per goose that got through, and less TWO per own goal. Punting a bird into your
        ///     own flowers is a worse thing than being beaten by one, and it was priced the same.
        ///
        /// Then two gates the sum cannot express, which are the honest half of having a result at all: a
        /// win always pays at least a mark, and a LOSS CAN NEVER PAY. A defence that ended with the duck's
        /// garden flat is not a positive contribution to the round however many perfects were in it, and
        /// before this it very comfortably could be.
        ///
        /// Static and pure so the formula is one readable place rather than something assembled across
        /// a method — this number goes on a scorecard the player is entitled to be able to predict.
        /// </summary>
        public static int ComputeAward(Outcome o, int floor, int ceiling)
        {
            int result = o.result switch
            {
                MatchResult.Won => o.onBudget ? 1 : 3,
                MatchResult.Lost => o.onBudget ? -1 : -3,
                _ => 0
            };

            int raw = result
                    + o.hitsOnOpponent * 2
                    + o.perfects
                    + RallyMark(o.longestRally)
                    - o.crashes
                    - o.ownGoals * 2;

            int award = Mathf.Clamp(raw, floor, ceiling);
            // The gates. Applied after the clamp and then re-clamped, so they can never push the award
            // outside the range the bench is allowed to hand out.
            if (o.result == MatchResult.Won) award = Mathf.Max(award, 1);
            else if (o.result == MatchResult.Lost) award = Mathf.Min(award, -1);
            return Mathf.Clamp(award, floor, ceiling);
        }

        /// <summary>
        /// What a sustained rally is worth, as a ladder rather than a flag.
        ///
        /// This is where DEFENDING WELL is paid, and it has to be a ladder because the alternative failed
        /// both ways. Paying a mark per good parry saturated the +8 clamp inside ten exchanges, so the award
        /// stopped distinguishing a great raid from an adequate one; paying a single mark for "a rally of
        /// three or more" was the opposite failure — three is a Tuesday in a match of fifteen exchanges, so
        /// the term was a participation prize that everybody collected once and nobody could improve on.
        ///
        /// A ladder is volume-insensitive: it cannot inflate with the length of the raid, because it counts
        /// the LONGEST unbroken run rather than the total. Four parries in a row is a mark, six is two,
        /// nine is three — and a run of nine means nine consecutive geese met at climbing dive speeds
        /// without one getting through, which is the best thing a defender can do and is worth saying so.
        /// </summary>
        static int RallyMark(int longest) => longest >= 9 ? 3
                                          : longest >= 6 ? 2
                                          : longest >= 4 ? 1 : 0;

        /// <summary>
        /// The result as the bench would say it out loud. One place, because the HUD, the console log and
        /// the readout must never disagree about who won.
        /// </summary>
        public static string DescribeResult(Outcome o) => o.result switch
        {
            MatchResult.Won => o.onBudget ? "WON ON BEDS" : "GARDEN TAKEN",
            MatchResult.Lost => o.onBudget ? "LOST ON BEDS" : "GARDEN LOST",
            MatchResult.Draw => "HELD",
            _ => "OPEN"
        };
        /// <summary>Successful parries in the rally currently running. The combo.</summary>
        public int Rally { get; private set; }

        // ---- state the capture tooling triggers on ----
        //
        // Exposed because a hit stop is 45-110 ms and the slow-motion beat is 220 ms, and the review
        // loop drives Unity over RPC calls that cost one to three seconds each. No sequence of external
        // commands can land a screenshot inside a window that short, so the capture has to be driven
        // from INSIDE the frame loop and it needs to be able to see these. Read-only, and nothing in the
        // phase itself consults them.

        /// <summary>Increments on every successful strike. A capture watches it for a change.</summary>
        public int StrikeCount { get; private set; }
        public Tier LastStrikeTier { get; private set; } = Tier.Miss;
        public bool HitStopActive => _hitStop > 0f;
        public bool SlowMotionActive => _slowMo > 0f;
        public bool GooseInPlay => _gooseState == GooseState.Diving || _gooseState == GooseState.Launched;
        /// <summary>
        /// True while the bird in the air is one the player can be beaten by. Half the geese now dive at
        /// the RIVAL'S garden, and the tell, the timing ring and every capture tool exist to watch the
        /// other half — so this is what tells them apart.
        /// </summary>
        public bool GooseComingAtMe => _gooseState == GooseState.Diving && _diveAtPlayer;
        /// <summary>
        /// Metres from the mower to a goose diving AT THE PLAYER, or -1 when there is none.
        ///
        /// Deliberately blind to a goose on its way to the rival's end. Every caller means "is there
        /// something I have to deal with, and how long have I got" — the tell, the anticipation pose, and
        /// the four capture bursts that wait on it — and reporting a bird attacking somebody else would
        /// wind all of them up for an exchange the player is not in.
        /// </summary>
        public float GooseRange => GooseComingAtMe && _mower != null
            ? Vector3.Distance(_goosePos, _mower.transform.position) : -1f;

        // ------------------------------------------------------------------ state

        Outcome _outcome;
        float _stateTime, _recovery, _regroup;

        DefenceArena _arena;
        RivalContestant _opponent;
        Vector3 _toOpponent = Vector3.forward;
        int _myCap, _theirCap;

        // ---- the stream ----
        //
        // "계속 나랑 상대방 방향으로 번갈아가면서 새로운 거위가 나한테 날라오고" — new geese keep coming,
        // alternating between my direction and the opponent's. That is what turns three throws into a
        // raid: the pitch has two ends under attack, the rival's garden falls whether or not the player
        // touches it, and no exchange is the last one because another bird is always on its way.

        /// <summary>Which garden the goose in play is diving at. Flips every fresh bird.</summary>
        bool _diveAtPlayer = true;
        /// <summary>Dives sent so far this raid. The match ramp and the pace ramp both read it.</summary>
        int _exchanges;
        /// <summary>
        /// Returns the bird in play has left, and the reason it cannot ping-pong forever.
        ///
        /// One per goose. A bird struck at the rival gets ONE keeper attempt; if they return it, it comes
        /// back at the player, and whatever happens to it next is final — struck again it lands in their
        /// beds unopposed, missed it comes down in the player's. So an exchange is at most two of the
        /// player's touches and always terminates, which is exactly the "소멸" the player asked for: the
        /// ponged goose is finished once the second party has had its go.
        /// </summary>
        int _keeperAttemptsLeft;

        System.Random _rng;
        MowerController _mower;
        CameraDirector _camera;
        SpectatorCrowd _crowd;
        JudgePanel _judgePanel;
        float _npcKick;
        Quaternion _npcRest;
        DefenceImpactFX _fx;

        Vector3 _mowerHomePos;
        Quaternion _mowerHomeRot;
        bool _mowerParked;

        Transform _bench;
        Vector3 _benchHomePos;
        Quaternion _benchHomeRot;
        bool _benchMoved;

        [Header("The award beat")]
        [Tooltip("Seconds held on the duck and its garden while the bench delivers the award. Long " +
                 "enough to read the number and see which beds survived, short enough that it is a " +
                 "beat rather than a screen.")]
        public float awardHoldSeconds = 2.8f;

        float _timeScaleOnEntry = 1f;
        float _hitStop, _slowMo;
        bool _clockHeld;

        Transform _goose;
        GooseState _gooseState = GooseState.Gone;
        Vector3 _goosePos, _goosePrev, _gooseVel, _diveTarget;
        float _gooseTimer;
        Material _matGoose, _matGooseHot, _matGooseBill;
        Transform _gooseBody;
        Vector3 _gooseBodyScale = Vector3.one;
        readonly List<MeshRenderer> _gooseSkin = new List<MeshRenderer>(6);
        /// <summary>Impact squash, 1 on contact decaying to 0. Real time, so a hit stop holds it.</summary>
        float _squash;
        Transform _neck, _wingL, _wingR;
        Quaternion _neckRest, _wingRestL, _wingRestR;
        /// <summary>Counts up while the bird is in the air, so the wingbeat has a phase of its own.</summary>
        float _flapPhase;
        /// <summary>0 while the bird is far, 1 the instant it is in range. Drives the tell.</summary>
        float _tell;
        /// <summary>1 at contact, decaying. Drives the neck whip and the wings snapping back.</summary>
        float _whip;
        /// <summary>Metres flown since the last trail mark, so spacing tracks speed.</summary>
        float _trailRun;
        /// <summary>Ground touches since launch. A ball is allowed a few skips before it is dead.</summary>
        int _gooseBounces;
        /// <summary>Where the bird's own centre sits when it is resting on the pitch.</summary>
        const float GroundY = 0.32f;

        AudioSource _voiceHonk, _voiceLow, _voiceRally;

        // ------------------------------------------------------------------ lifecycle

        void OnDisable() => Abort();

        void OnDestroy()
        {
            Abort();
            if (_goose != null) Destroy(_goose.gameObject);
            if (_matGoose != null) Destroy(_matGoose);
            if (_matGooseHot != null) Destroy(_matGooseHot);
            if (_matGooseBill != null) Destroy(_matGooseBill);
        }

        /// <summary>
        /// Raise the arena, move the machine into it and start the first dive.
        ///
        /// Everything that depends on the round — which outline the garden is planted along, who the
        /// opponent is, how many beds may be lost — is resolved here, because none of it exists until
        /// the picture has been mown.
        /// </summary>
        public void Begin()
        {
            Abort();

            // Asked of the director first and FOUND IN THE SCENE otherwise, because the arena is a level
            // that can be opened and played on its own and there is no GameDirector in it.
            //
            // This was not cosmetic. GooseRange returns -1 unless the mower is known, so with a null
            // mower the phase ran, dived and swooped while every tool watching it — and every capture —
            // was told no goose had arrived. Judge() reads the same reference, so no parry could have
            // been scored either. One null reference made the whole phase unobservable and unplayable
            // in the only scene it actually lives in.
            var director = GameDirector.Instance;
            _mower = director != null && director.mower != null
                   ? director.mower : FindFirstObjectByType<MowerController>();
            _camera = director != null && director.cameraDirector != null
                    ? director.cameraDirector : FindFirstObjectByType<CameraDirector>();
            if (_crowd == null) _crowd = FindFirstObjectByType<SpectatorCrowd>();

            if (_mower == null)
            {
                Debug.LogError("[Duck] no MowerController in the scene; the phase cannot judge a parry " +
                               "without one. Rebuild the arena with 'Duck/2 · Build defence arena scene'.");
                State = Phase.Done;
                return;
            }

            _rng = new System.Random(seed != 0 ? seed : System.Environment.TickCount);
            _outcome = default;
            // Cleared here and set only in EndPhase, so a raid that was aborted or never reached the
            // end awards nothing rather than paying out the last one's marks a second time.
            Award = 0;
            Rally = 0;
            _recovery = 0f;
            _regroup = 0f;
            _exchanges = 0;
            // The first goose is the player's. A raid that opened on the rival's end would spend its first
            // four seconds showing the player something happening to somebody else.
            _diveAtPlayer = true;
            // One life for the first bird too — Brief starts it directly rather than through NextInStream,
            // and a zero here would have quietly denied the rival any attempt on the opening exchange.
            _keeperAttemptsLeft = 1;

            _opponent = PickOpponent();
            _outcome.opponent = _opponent != null ? _opponent.displayName : "THE FIELD";

            DebugClearArming();
            // FOUND, not created. The arena is a saved scene authored by DuckArenaBuilder, so if this
            // comes back null the scene has not been built rather than the phase having a bug — say so
            // plainly instead of silently conjuring geometry nobody can inspect.
            if (_arena == null) _arena = FindFirstObjectByType<DefenceArena>();
            if (_arena == null)
            {
                Debug.LogWarning("[Duck] no DefenceArena in the scene; run " +
                                 "'Duck/2 · Build defence arena scene'. The phase will not run.");
                State = Phase.Done;
                return;
            }
            _arena.Bind();
            _toOpponent = _arena.Toward;

            _outcome.myBedsTotal = _arena.PlayerBeds.Count;
            _outcome.theirBedsTotal = _arena.OpponentBeds.Count;
            _myCap = Mathf.Max(1, Mathf.RoundToInt(_outcome.myBedsTotal * damageCapFraction));
            _theirCap = Mathf.Max(1, Mathf.RoundToInt(_outcome.theirBedsTotal * damageCapFraction));

            // The clock is only ever bent from here, so remember what it WAS rather than assuming 1.
            // The review tools park the game at 6x, and restoring a hard 1 would silently undo a
            // reviewer's fast-forward in the middle of their own capture.
            _timeScaleOnEntry = Time.timeScale;
            _hitStop = 0f;
            _slowMo = 0f;
            _clockHeld = false;

            MoveMowerIn();
            BuildGoose();
            BuildVoices();
            EnsureFX();

            // Assert the chase shot. The klaxon abandons an aerial lift without putting the camera
            // back, so a player who was ninety metres up when time ran out would otherwise open the
            // phase looking straight down at a lawn 420 m from the arena.
            _camera?.SetMode(CameraMode.Chase, 0.6f);

            SetPhase(Phase.Brief);

            Debug.Log($"[Duck] raid ON: first to break a garden wins. " +
                      $"Mine {_outcome.myBedsTotal} beds, {_myCap} may fall; " +
                      $"{_outcome.opponent} {_outcome.theirBedsTotal} beds, {_theirCap} may fall. " +
                      $"{bedsPerCrash} beds a goose, so about {Mathf.CeilToInt(_myCap / (float)Mathf.Max(bedsPerCrash, 1))} " +
                      $"through either end decides it. Budget {raidBudgetSeconds:0} s, " +
                      $"goals {_arena.GardenGap:0} m apart.");
        }

        /// <summary>
        /// Shut the phase down, give the clock back, and put the machine where it was.
        ///
        /// Safe from the state change, from OnDisable and from OnDestroy, because the debug jumps and
        /// the capture tools all change state from underneath this. Restoring the timescale is the one
        /// that must never be missed: a phase aborted during a hit stop would otherwise leave the
        /// whole game running at two percent speed with nothing on screen to explain it.
        /// </summary>
        public void Abort()
        {
            ReleaseClock();
            // Abort is the ABNORMAL exit — a retry, Escape to the menu, a debug jump — and those do need
            // the machine back on the lawn. A normal finish clears _mowerParked without moving anything
            // (see EndPhase), so this is a no-op on that path and the duck stays on the pitch for its
            // award.
            MoveMowerOut();
            RestoreBench();
            // Belt and braces on top of MoveMowerOut, which only lifts the lock if it was the thing
            // that set it. A BladeLocked left standing is the worst failure this file can cause: the
            // mower drives for the rest of the session and cuts nothing, so the player mows a whole
            // round and scores zero with no way to tell why.
            if (_mower != null) _mower.BladeLocked = false;
            _camera?.ClearDefenceSubject();
            if (_goose != null) _goose.gameObject.SetActive(false);
            _gooseState = GooseState.Gone;
            if (_voiceRally != null) _voiceRally.Stop();
            State = Phase.Idle;
        }

        void SetPhase(Phase p)
        {
            State = p;
            _stateTime = 0f;
        }

        void MoveMowerIn()
        {
            if (_mower == null || _arena == null) return;

            _mowerHomePos = _mower.transform.position;
            _mowerHomeRot = _mower.transform.rotation;
            _mowerParked = true;

            MoveBenchIn();
            _mower.ParkAt(_arena.SpawnPoint, _arena.SpawnRotation);
            // The deck comes up. The blade does nothing out here anyway — the cut mask is centred on
            // the playfield and clips everything at this range — but a spinning blade and a shredding
            // audio layer while the duck is defending is a machine doing the wrong job.
            _mower.BladeLocked = true;
            _camera?.NotifyTargetTeleported();
        }

        /// <summary>
        /// Bring the judges' bench onto the pitch, by MOVING THE REAL ONE.
        ///
        /// The bench and the three rigged judges are authored by editor script — `BuildJudgeBenchOnly`
        /// plus the `Judge_*` prefabs — and none of that can be called at runtime, so a second bench
        /// here would have meant re-authoring one in code and maintaining two. Instead the existing
        /// assembly is relocated for the duration and put back afterwards.
        ///
        /// It works because the whole thing hangs off one transform: `JudgePanel`'s own. The bench mesh,
        /// all three judges, their rigs and their animators are children of it, and CameraDirector's
        /// `judgesLookAt` points at that very transform — so the judging camera follows the move for
        /// free rather than having to be told about it. One SetPositionAndRotation relocates the lot.
        ///
        /// RestoreBench is therefore not optional housekeeping: the judging beat happens back at the
        /// venue a few seconds later, and a bench left out here would hold that entire beat 420 m from
        /// where the camera is looking.
        /// </summary>
        void MoveBenchIn()
        {
            var panel = GameDirector.Instance != null ? GameDirector.Instance.judges : null;
            if (panel == null || _arena == null || _benchMoved) return;

            _bench = panel.transform;
            _benchHomePos = _bench.position;
            _benchHomeRot = _bench.rotation;
            _benchMoved = true;
            _bench.SetPositionAndRotation(_arena.BenchPosition, _arena.BenchRotation);
        }

        void RestoreBench()
        {
            if (!_benchMoved || _bench == null) return;
            _benchMoved = false;
            _bench.SetPositionAndRotation(_benchHomePos, _benchHomeRot);
        }

        void MoveMowerOut()
        {
            if (!_mowerParked || _mower == null) return;
            _mowerParked = false;
            _mower.ParkAt(_mowerHomePos, _mowerHomeRot);
            _mower.BladeLocked = false;
            _camera?.NotifyTargetTeleported();
        }

        // ------------------------------------------------------------------ tick

        // No Update. The director drives this from its own Tick, which is the entry point the capture
        // harness also calls, so the phase steps identically under Unity's loop and under SimClock.
        public void Tick(float dt)
        {
            if (State == Phase.Idle || State == Phase.Done) return;

            // The clock is bent on UNSCALED time, because the whole point of a hit stop is that
            // scaled time has stopped moving. Everything else runs on the scaled dt the director
            // handed down, which is what makes a hit stop cost the player nothing off the phase timer.
            UpdateClock();

            // Debris runs on the SCALED dt, so a hit stop freezes the burst in mid-air with everything
            // else and it bursts outward as the clock releases. See DefenceImpactFX's class note: on
            // real time the whole burst would be spent during the frames nothing else is moving.
            _fx?.Tick(dt);

            _stateTime += dt;

            switch (State)
            {
                case Phase.Brief:
                    if (_stateTime >= briefSeconds) { SetPhase(Phase.Live); StartDive(); }
                    break;

                case Phase.Live:
                    // Input before the goose moves, so the frame it arrives is still a frame it can be
                    // met on. The other order quietly spends the last frame of every window.
                    ResolveInput(dt);
                    StepGoose(dt);
                    UpdateCameraEnergy(dt);
                    UpdateTell();

                    // The CONDITION first, the ceiling second, and the order is the whole point of the
                    // rewrite: a raid ends because a garden fell, and only falls back on the clock if
                    // nothing did.
                    {
                        MatchResult verdict = TestGardens();
                        if (verdict != MatchResult.None) ConcludeRaid(verdict, onBudget: false);
                        else if (_stateTime >= raidBudgetSeconds) ConcludeRaid(DecideOnBeds(), onBudget: true);
                    }
                    break;

                case Phase.Settle:
                    ResolveInput(dt);
                    StepGoose(dt);
                    UpdateCameraEnergy(dt);
                    UpdateTell();
                    if (_gooseState == GooseState.Gone || _stateTime >= settleMaxSeconds) BeginAward();
                    break;

                case Phase.Awarding:
                    // Nothing moves and nobody is moved. The bench marks the defence here, on this
                    // pitch, with the flattened and surviving beds still on the ground in shot — which
                    // is the entire point of the beat: the evidence and the verdict in one frame.
                    if (_stateTime >= awardHoldSeconds) EndPhase();
                    break;
            }
        }

        // ------------------------------------------------------------------ the match

        /// <summary>
        /// Has a garden fallen? The condition that replaced the twelve-second clock.
        ///
        /// The player's loss is tested FIRST. Both caps can only be reached in the same frame if one of
        /// them was already reached and had somehow not been noticed, so the order is very nearly academic
        /// — but if it ever is not, the raid the player lost is the raid that happened. A phase that
        /// awarded a win on the frame the duck's own garden went flat would be lying about the ground
        /// everyone can see.
        /// </summary>
        MatchResult TestGardens()
        {
            if (_arena == null) return MatchResult.None;
            if (_arena.PlayerBedsLost >= _myCap) return MatchResult.Lost;
            if (_arena.OpponentBedsLost >= _theirCap) return MatchResult.Won;
            return MatchResult.None;
        }

        /// <summary>
        /// Who was ahead when the budget ran out. A raid that goes the distance is decided on damage.
        ///
        /// The alternative was to call a budget finish "no result", and that is unfair in a way worth
        /// spelling out: a player four beds up with the rival's garden one goose from collapse would have
        /// been given exactly the same nothing as a player who parked in a corner. Sport decides on points
        /// when nobody is knocked out; so does this. <see cref="ComputeAward"/> pays a decision less than
        /// a knockout, which is the honest gap between the two.
        /// </summary>
        MatchResult DecideOnBeds()
        {
            int mine = _arena != null ? _arena.PlayerBedsLost : 0;
            int theirs = _arena != null ? _arena.OpponentBedsLost : 0;
            if (theirs > mine) return MatchResult.Won;
            if (mine > theirs) return MatchResult.Lost;
            return MatchResult.Draw;
        }

        /// <summary>
        /// Call the raid, on the frame it is decided, and make the call legible where it happens.
        ///
        /// The result is announced HERE rather than at the award, and that gap is deliberate. A garden
        /// falling is the dramatic event; the award is the paperwork two seconds later. If the only place
        /// the player were told were the scorecard, the moment they actually won would pass in silence with
        /// a goose still bouncing across the grass.
        ///
        /// It does not stop the phase dead either. Settle already exists to let a bird mid-flight finish
        /// what it was doing, and cutting a crash short would make the final damage depend on which frame
        /// the condition happened to land in — so the raid is CALLED here and comes to rest on its own.
        /// </summary>
        void ConcludeRaid(MatchResult verdict, bool onBudget)
        {
            _outcome.result = verdict;
            _outcome.onBudget = onBudget;
            _outcome.myBedsLost = _arena != null ? _arena.PlayerBedsLost : 0;
            _outcome.theirBedsLost = _arena != null ? _arena.OpponentBedsLost : 0;

            bool won = verdict == MatchResult.Won;

            // The budget ending a raid is a finding, not a result, so it says so at warning level exactly
            // as GameDirector does when a beat overruns. Anybody reading a console and seeing this knows
            // the contest did not resolve itself and that the numbers below are a points decision.
            if (onBudget)
                Debug.LogWarning($"[Duck] raid hit its {raidBudgetSeconds:0} s BUDGET with both gardens " +
                                 $"still standing — the condition did not end it, the ceiling did. " +
                                 $"Decided on beds: mine {_outcome.myBedsLost}/{_myCap}, theirs " +
                                 $"{_outcome.theirBedsLost}/{_theirCap} over {_exchanges} exchanges " +
                                 $"→ {DescribeResult(_outcome)}.");
            else
                Debug.Log($"[Duck] raid decided on exchange {_exchanges} at {_stateTime:0.0} s: " +
                          $"{DescribeResult(_outcome)} — mine {_outcome.myBedsLost}/{_myCap} flat, " +
                          $"{_outcome.opponent} {_outcome.theirBedsLost}/{_theirCap} flat.");

            // Sold with the vocabulary the phase already owns rather than with anything new: the camera
            // punch and shake that a perfect strike gets, the crowd, the bench, and the rival's own body
            // language. A win draws the rival up short; a loss is their good news.
            _camera?.AddPunch(won ? 1f : 0.5f);
            _camera?.AddShake(won ? 0.9f : 0.7f);
            // A beat of slow motion on a knockout, and none on a decision. The clock bending is this
            // phase's strongest punctuation and it is reserved for the moment a garden actually goes.
            if (!onBudget) _slowMo = slowMotionSeconds * 1.6f;

            var audio = AudioDirector.Instance;
            if (audio != null)
            {
                if (won)
                {
                    audio.CrowdCheer(1f, applaud: true);
                    audio.PlayOne(audio.quackProud, 0.8f);
                }
                else if (verdict == MatchResult.Lost)
                {
                    audio.CrowdGroan(0.9f);
                    audio.PlayOne(audio.quackPanic, 0.7f);
                }
            }
            _crowd?.Excite(won ? 1f : 0.3f);
            PunchJudges(won ? 0.9f : -0.8f);
            ReactOpponent(1f, inTheirFavour: !won);

            // Let it come to rest. Settle sends no fresh geese — see the Returning and Crashing cases in
            // StepGoose, both of which only re-arm while Live — so the stream stops here without the stream
            // needing to know about the match at all.
            SetPhase(Phase.Settle);
        }

        /// <summary>
        /// The award beat, delivered here on the pitch rather than after the player has been moved.
        ///
        /// The old shape put the whole outcome into EndPhase, which also parked the mower back in the
        /// venue — so the instant the rally ended the chase camera was yanked 420 m across the map and
        /// the judging happened somewhere the player had never been. From the seat that read as the
        /// level snapping back, and it was the reason the flow felt wrong. Nothing is moved until the
        /// award has landed.
        /// </summary>
        void BeginAward()
        {
            _outcome.myBedsLost = _arena != null ? _arena.PlayerBedsLost : 0;
            _outcome.theirBedsLost = _arena != null ? _arena.OpponentBedsLost : 0;
            // Belt and braces. Settle is only ever entered from ConcludeRaid, so the result is already
            // set — but an unmarked raid would silently score as a draw, and an award that quietly says
            // nothing about who won is the exact failure this rewrite exists to remove.
            if (_outcome.result == MatchResult.None)
            {
                _outcome.result = DecideOnBeds();
                _outcome.onBudget = true;
                Debug.LogWarning("[Duck] the raid reached its award with no verdict set; decided on beds. " +
                                 "Something reached Settle without going through ConcludeRaid.");
            }
            Award = ComputeAward(_outcome, awardFloor, awardCeiling);

            // Boris is the one who enjoys a rally — he marks on coverage and style and applauds
            // everything — so a positive award goes to him by name rather than to whichever judge the
            // random happened to pick. Mildred withholds, so she gets the disappointments.
            var panel = GameDirector.Instance != null ? GameDirector.Instance.judges : null;
            if (panel != null && panel.judges != null && panel.judges.Length >= 2)
                panel.judges[Award >= 0 ? 1 : 0]?.character?.Punch(Award >= 0 ? 0.85f : -0.7f);

            var audio = AudioDirector.Instance;
            if (audio != null)
            {
                audio.PlayOne(audio.cardRaise, 0.6f);
                audio.PlayOne(audio.stamp, 0.7f);
                if (Award > 0) audio.CrowdCheer(Mathf.InverseLerp(0f, awardCeiling, Award), applaud: true);
                else if (Award < 0) audio.CrowdGroan(0.6f);
                audio.PlayOne(Award >= 3 ? audio.quackProud : Award < 0 ? audio.quackAnnoyed : null, 0.6f);
            }
            _crowd?.Excite(Award > 0 ? 0.8f : 0.2f);

            // Hold the shot on the duck and its garden. The goose is gone, so without this the camera's
            // defence subject would still be the opponent's board at the far end and the award would be
            // delivered over an empty pitch.
            _camera?.ClearDefenceSubject();

            Debug.Log($"[Duck] {DescribeResult(_outcome)} — defence award {Award:+0;-0;0} " +
                      $"({_outcome.perfects}P {_outcome.goods}G {_outcome.normals}N, " +
                      $"longest x{_outcome.longestRally}, {_outcome.hitsOnOpponent} past their keeper, " +
                      $"{_outcome.keeperSaves} saved by them, {_outcome.rivalConceded} they lost on their " +
                      $"own, {_outcome.crashes} through, {_outcome.ownGoals} own goals).");

            SetPhase(Phase.Awarding);
        }

        void EndPhase()
        {

            Debug.Log($"[Duck] raid over — {DescribeResult(_outcome)} in {_exchanges} exchanges" +
                      $"{(_outcome.onBudget ? " (on the budget)" : "")}: {_outcome.parries} parries " +
                      $"({_outcome.perfects}P {_outcome.goods}G {_outcome.normals}N), longest " +
                      $"x{_outcome.longestRally}, {_outcome.crashes} crashes, {_outcome.whiffs} wasted " +
                      $"honks, {_outcome.hitsOnOpponent} past their keeper ({_outcome.keeperSaves} saved), " +
                      $"{_outcome.rivalConceded} they lost unaided, {_outcome.ownGoals} own goals. " +
                      $"My beds {_outcome.myBedsLost}/{_outcome.myBedsTotal} flat (of {_myCap} allowed), " +
                      $"theirs {_outcome.theirBedsLost}/{_outcome.theirBedsTotal} (of {_theirCap}). " +
                      $"AWARD {Award:+0;-0;0}.");

            var audio = AudioDirector.Instance;
            // The fanfare follows the RESULT rather than a bed comparison. The old test was
            // "myBedsLost <= theirBedsLost", which handed out the good fanfare for a nil-nil raid in which
            // nothing happened at all — and, worse, for a raid the player had just lost on the budget by
            // an equal number of beds.
            audio?.PlayOne(_outcome.result == MatchResult.Lost ? audio.fanfareBad : audio.fanfareGood, 0.7f);

            ReleaseClock();

            // The machine is deliberately LEFT on the pitch. Driving it back here is what produced the
            // 420 m snap under a live chase camera, and nothing needs it moved: the reveal is an
            // overhead of the lawn, where an absent mower is if anything a cleaner shot, and the
            // verdict's StageForPortrait parks it on its mark before any camera looks at it again. The
            // one thing that must go with this is the reveal's camera BLEND — see the Reveal case in
            // GameDirector, which now cuts rather than sailing across the map.
            //
            // The flag is cleared without moving anything so that Abort, which runs on the way out of
            // the state, finds nothing left to put back.
            _mowerParked = false;
            if (_mower != null) _mower.BladeLocked = false;

            RestoreBench();
            _camera?.ClearDefenceSubject();
            if (_goose != null) _goose.gameObject.SetActive(false);
            if (_voiceRally != null) _voiceRally.Stop();
            State = Phase.Done;
        }

        // ------------------------------------------------------------------ the clock

        /// <summary>
        /// Hit stop and slow motion, on Time.timeScale.
        ///
        /// Two things make this safe to do to a force-driven rigidbody. Time.fixedDeltaTime is NOT
        /// scaled by timeScale, so the suspension springs integrate with exactly the step they always
        /// had — a dip changes how MANY steps happen per real second, never how large one is, and
        /// fewer identical steps cannot destabilise a spring. And every dt-dependent expression in
        /// MowerController is already written dt-invariantly (MoveTowards for drive,
        /// 1-pow(1-g, dt*60) for grip), so the handling in game-time is unchanged. Interpolation is on,
        /// so the render stays smooth at fifteen steps a second.
        ///
        /// Skipped entirely under SimClock.Scripted: the harness owns the clock and steps physics by
        /// hand, so a timescale here would do nothing to the simulation while corrupting the frame
        /// sheet's timings — a hit stop would be captured as a stall.
        /// </summary>
        void UpdateClock()
        {
            if (SimClock.Scripted) return;

            float real = Time.unscaledDeltaTime;
            // Real time, so the squash HOLDS through a hit stop and releases as the world starts moving
            // again. Decaying it on scaled time would spend the whole deformation during the frames
            // nothing else is moving, which is the one moment it exists to be seen in.
            _squash = Mathf.Max(0f, _squash - real * 5.5f);
            // Whip decays slower than the squash: a neck snapping back is a longer gesture than a body
            // compressing, and matching them makes the whole bird read as one rubber blob.
            _whip = Mathf.Max(0f, _whip - real * 3.4f);
            StepOpponent(real);

            if (_hitStop > 0f)
            {
                _hitStop -= real;
                if (_hitStop > 0f) { Time.timeScale = 0.02f; _clockHeld = true; return; }
            }
            if (_slowMo > 0f)
            {
                _slowMo -= real;
                if (_slowMo > 0f)
                {
                    Time.timeScale = _timeScaleOnEntry * slowMotionScale;
                    _clockHeld = true;
                    return;
                }
            }
            if (_clockHeld) { Time.timeScale = _timeScaleOnEntry; _clockHeld = false; }
        }

        void ReleaseClock()
        {
            _hitStop = 0f;
            _slowMo = 0f;
            if (_clockHeld && !SimClock.Scripted) Time.timeScale = _timeScaleOnEntry;
            _clockHeld = false;
        }

        /// <summary>
        /// The debris rig, made once and kept. Parented to this component rather than to the goose, so
        /// a burst stays where the impact happened instead of being dragged along by the bird that has
        /// just been launched out of it.
        /// </summary>
        void EnsureFX()
        {
            if (_fx == null)
            {
                _fx = GetComponent<DefenceImpactFX>() ?? gameObject.AddComponent<DefenceImpactFX>();
                _fx.Setup(_rng, parryHalfAngle);
            }
            _fx.Clear();
        }

        // ------------------------------------------------------------------ the parry

        void ResolveInput(float dt)
        {
            if (_recovery > 0f) _recovery = Mathf.Max(0f, _recovery - dt);

            var input = InputReader.Instance;
            if (input == null || !input.HornPressed) return;

            Honk();
            // The shockwave goes out on EVERY press, before the hit test — including a honk during
            // recovery, which is the one the player most needs to see land. The horn was the phase's
            // only verb and it had no output of its own: a mistimed press produced nothing at all and
            // read as a dropped input rather than as a miss. Weaker while recovering, so a mashed horn
            // still says "not yet" rather than rewarding the mash.
            // Turned with the nose, so the wave shows the cone the horn actually reaches through.
            Vector3 nose = Vector3.ProjectOnPlane(_mower.transform.forward, Vector3.up);
            _fx?.Shock(_mower.transform.position,
                       Quaternion.LookRotation(nose.sqrMagnitude > 1e-4f ? nose.normalized : _toOpponent,
                                               Vector3.up),
                       _recovery > 0f ? 0.35f : 1f);

            if (_recovery > 0f) return;

            Tier tier = Judge();
            if (tier == Tier.Miss)
            {
                _recovery = whiffRecovery;
                _outcome.whiffs++;
                return;
            }
            Strike(tier);
        }

        /// <summary>
        /// How well the goose was met: purely how far away it was when the horn went.
        ///
        /// Only a DIVING goose can be met. A launched or returning one already belongs to the player,
        /// and letting the horn touch it would let them keep the same bird in the air forever without
        /// ever having to face it coming back.
        /// </summary>
        Tier Judge()
        {
            // And only a goose diving AT THE DUCK. Half of them are attacking the rival's garden now, and
            // a bird crossing the player's end on its way there is somebody else's problem — letting the
            // horn touch it would let the player defend their opponent's flowers, which is nonsense, and
            // would credit them a parry for a bird that was never a threat.
            if (_gooseState != GooseState.Diving || !_diveAtPlayer || _mower == null) return Tier.Miss;

            // A CONE out of the machine's nose, not a bubble around it.
            //
            // The test used to be distance alone, so the horn connected with a goose directly behind the
            // mower as readily as one in front of it. Aim changed only how accurately the bird was
            // returned — never whether it was hit — which is why the phase read as swatting rather than
            // as striking: there was no wrong way to be facing.
            //
            // Now direction decides contact and distance decides grade. Pointing at the bird is the
            // skill; being close to it is the reward.
            //
            // Measured on the HORIZONTAL only. A diving goose arrives from twenty metres up, so a true
            // 3D cone from a ground-level forward vector would exclude the very bird the player is
            // plainly aiming at — the player reads this as "am I facing it", and facing is a compass
            // question, not a spherical one.
            Vector3 toGoose = _goosePos - _mower.transform.position;
            float d = toGoose.magnitude;
            if (d > parryRadius) return Tier.Miss;

            Vector3 flatTo = toGoose; flatTo.y = 0f;
            Vector3 nose = _mower.transform.forward; nose.y = 0f;
            // Directly overhead: no meaningful bearing, so the cone cannot reject it. Treat a bird on
            // top of the machine as in front of it rather than as a miss nobody could explain.
            bool overhead = flatTo.sqrMagnitude < 0.36f;
            float bearing = overhead ? 0f : Vector3.Angle(nose, flatTo);
            if (bearing > parryHalfAngle) return Tier.Miss;

            // Grade on distance as before, but tightened by how square the hit was: a bird at the edge
            // of the cone cannot be a PERFECT even from point blank. That is what stops the widened cone
            // from being strictly more generous than the bubble it replaced.
            float square = 1f - Mathf.Clamp01(bearing / Mathf.Max(parryHalfAngle, 1f));
            if (d <= perfectRadius && square > 0.55f) return Tier.Perfect;
            if (d <= goodRadius && square > 0.25f) return Tier.Good;
            return Tier.Normal;
        }

        void Strike(Tier tier)
        {
            Rally++;
            StrikeCount++;
            LastStrikeTier = tier;
            _outcome.parries++;
            _outcome.longestRally = Mathf.Max(_outcome.longestRally, Rally);
            if (tier == Tier.Perfect) _outcome.perfects++;
            else if (tier == Tier.Good) _outcome.goods++;
            else _outcome.normals++;

            // The sharpest thing in the haptic vocabulary, and it belongs here: this is the one event
            // in the rally the player is actively trying to cause, arriving in a moment when the
            // screen is busy and the crowd is loud. No tier scaling — the amplitude is the point, and
            // a NORMAL parry that feels like a weak PERFECT would read as a worse parry than it was.
            // Unconditional because _mower is the player's own machine (bound from director.mower at
            // line 617), so there is no rival that can reach this method.
            Haptics.GooseStrike();

            // Where the machine is pointing is where the goose goes. The most direct reading of the
            // arcade-car-soccer reference, and it makes aiming a driving problem rather than a second
            // control to learn.
            Vector3 heading = _mower.transform.forward;
            heading.y = 0f;
            if (heading.sqrMagnitude < 1e-4f) heading = _toOpponent;
            heading.Normalize();

            // A parried goose ALWAYS goes downfield. Tier decides how ACCURATELY, never whether.
            //
            // It used to decide whether: a Normal parry was excluded outright and Good/Perfect only
            // counted if the machine happened to be pointing within 34 degrees of the far goal. So most
            // successful parries flung the bird sideways or back over the player's own beds, and the one
            // thing the exchange is about — sending it to the opponent — was the exception rather than
            // the rule. From the seat that reads as the goose charging at you and being swatted away,
            // not as a rally.
            //
            // Now the launch is aimed at the opponent's garden and the player's heading only BENDS it.
            // A perfect hit with good aim lands on a bed; a normal hit sprays wide and may fall short in
            // the open. Skill still decides the outcome — LandLaunched reads it off where the bird
            // actually comes down, exactly as before — but the SILHOUETTE of the exchange is now always
            // "it went that way", which is what makes the pitch read as having two ends.
            Vector3 target = _arena != null
                ? (_arena.AnyAlive(_rng, playerSide: false)?.position ?? _arena.OpponentGardenCentre)
                : _goosePos + _toOpponent * 40f;   // no arena bound: still throw downfield

            // How much the player's aim is honoured. A perfect strike places it; a normal one keeps the
            // direction and loses the address.
            float placement = tier == Tier.Perfect ? 1f : tier == Tier.Good ? 0.72f : 0.35f;
            float offAim = Vector3.Angle(heading, _toOpponent) / Mathf.Max(redirectCone, 1f);
            // Aim outside the cone costs accuracy rather than cancelling the throw.
            placement *= Mathf.Clamp01(1.2f - offAim * 0.55f);

            // Scatter shrinks as placement rises, and is applied about the DOWNFIELD axis so even the
            // worst parry still travels toward the far end.
            float spread = scrambleScatter * (1f - placement);
            float wobble = ((float)_rng.NextDouble() * 2f - 1f) * spread;

            // AIMED AT MID-PITCH, not at the far goal.
            //
            // "E로 튕겼을 때 상대방쪽으로 날라가서 사라지는게 아니라. 한번 중앙 바닥에 튕겨서 상대방한테
            // 가는거지." — it should touch down once in the middle and travel on from there, not sail the
            // whole way and vanish. That is the difference between a clearance and a ball: one leaves the
            // pitch, the other uses it.
            //
            // So the arc is thrown roughly HALF the distance and the bounces carry the rest. The skip
            // retains 86% of its horizontal speed per touch (see the Launched case), so a well-struck
            // bird reaches the far beds on its second or third contact while a weak one dies in midfield
            // — which is a far better read of how well it was hit than the landing point of a single arc.
            Vector3 flat = target - _goosePos; flat.y = 0f;
            // Carry, measured rather than guessed. A diagnostic run over four Good strikes put the ball
            // down at 40.1 / 41.4 / 41.4 / 41.7 m and a Perfect at 46.6 m — against a garden whose near
            // edge is at 42 m. So a GOOD strike stopped two metres short of the target every single time,
            // which made hitsOnOpponent a Perfect-only event and quietly deleted the middle rung of the
            // tier ladder: the difference between Normal and Good was "short" versus "slightly less
            // short", both landing on open grass.
            //
            // Raised so the ladder reads the way it is scored — Normal falls short in the open, Good
            // REACHES the beds, Perfect goes deep enough to test their keeper's reach rather than their
            // reflex. Tuned at the low end as well as the high one, because the gap between the two ends
            // is what separates the tiers and the first pass had it far too narrow.
            float reach = Mathf.Max(flat.magnitude, 6f) * Mathf.Lerp(0.40f, 0.58f, placement);
            Vector3 aimed = Quaternion.Euler(0f, wobble, 0f) * (flat.sqrMagnitude > 1e-4f
                                                                ? flat.normalized : _toOpponent);
            Vector3 land = _goosePos + aimed * reach;
            land.y = 0.3f;

            _gooseVel = ArcTo(_goosePos, land, launchFlightSeconds, launchGravity);

            _gooseState = GooseState.Launched;
            _gooseTimer = 0f;
            _trailRun = 1.1f;   // first mark on the frame it leaves, not a metre later
            SetGooseHot(true);

            // Feel, reused wholesale rather than rebuilt. Bonk was written for gnome impacts and is
            // already the mower half of this brief: it sheds speed, shoves the machine back, spins it,
            // and deliberately keeps grip and steering live so only the drive is cut.
            _mower.Bonk(_goosePos, tier == Tier.Perfect ? bonkPerfect
                                 : tier == Tier.Good ? bonkGood : bonkNormal);

            _squash = tier == Tier.Perfect ? 1f : tier == Tier.Good ? 0.75f : 0.5f;
            // The neck whip and the wing snap ride the same envelope as the squash, so the whole bird
            // deforms as one event rather than as three effects that happen to coincide.
            _whip = _squash;
            _hitStop = tier == Tier.Perfect ? hitStopPerfect
                     : tier == Tier.Good ? hitStopGood : hitStopNormal;
            if (tier == Tier.Perfect) _slowMo = slowMotionSeconds;

            _camera?.AddPunch(tier == Tier.Perfect ? 1.0f : tier == Tier.Good ? 0.62f : 0.34f);
            _camera?.AddShake(tier == Tier.Perfect ? 0.9f : tier == Tier.Good ? 0.55f : 0.3f);

            PlayStrikeAudio(tier);
            ReactCrowd(tier);
            // Struck well: the rival draws up, because this one is coming at them.
            ReactOpponent(tier == Tier.Perfect ? 1f : tier == Tier.Good ? 0.6f : 0.35f,
                          inTheirFavour: false);

            // Debris last, and thrown along the LAUNCH heading rather than along the goose's incoming
            // velocity. The burst is the clearest read of where the bird has been sent — it points at
            // the answer during the frames the hit stop is holding everything still.
            _fx?.Impact(_goosePos, heading,
                        tier == Tier.Perfect ? 1f : tier == Tier.Good ? 0.62f : 0.3f);

            // And the marks where the wheels let go. Laid along the machine's own travel rather than the
            // launch heading: the skid is where the mower WAS going when it was checked, which is what
            // makes it read as the machine having been stopped rather than as the goose having been sent.
            Vector3 roll = _mower.transform.forward;
            _fx?.Skid(_mower.transform.position, roll, 0.46f,
                      tier == Tier.Perfect ? 1f : tier == Tier.Good ? 0.6f : 0.35f);
        }

        // ------------------------------------------------------------------ the goose

        /// <summary>
        /// The goose, as a legible greybox rather than a capsule.
        ///
        /// The capsule failed on measurement, not on taste, and both failures are worth recording
        /// because they are easy to repeat.
        ///
        /// COLOUR. It was pale cream, and the frame that matters is the contact frame — which is
        /// always near the ground, over pale sage. A pale bird on pale ground at 30 px had to be
        /// HUNTED FOR in a still. Worse, the contact frame shows the STRUCK material, so the paler of
        /// the two was the one guarding the only moment the mechanic lives in. It is now dark: a dark
        /// value is the one choice that separates from pale sky AND pale ground at once, where any hue
        /// only separates from one of them. The struck state flares to saturated orange, which is both
        /// unmistakable and reads as an event rather than a state.
        ///
        /// HEADING. A capsule is an egg — it has no front, so neither its trajectory nor where it came
        /// from could be read at all, and "readable trajectories" is on the goal's own list. Neck, bill,
        /// tail and wings cost four boxes and give the silhouette a direction from any angle. The bill
        /// stays bright while the body is dark, so the sharpest value edge on the object points exactly
        /// where it is going.
        ///
        /// Built as a hierarchy with the parts pre-oriented so the ROOT's +Z is forward. That means
        /// placement is one LookRotation with no correction Euler, and the body can be squashed on
        /// impact independently of the heading.
        /// </summary>
        void BuildGoose()
        {
            _matGoose ??= Flat(new Color(0.17f, 0.15f, 0.14f));
            _matGooseHot ??= Flat(new Color(0.98f, 0.34f, 0.06f));
            _matGooseBill ??= Flat(new Color(1f, 0.58f, 0.06f));

            if (_goose != null) { _goose.gameObject.SetActive(false); return; }

            var root = new GameObject("~ DefenceGoose (greybox)");
            _goose = root.transform;
            _gooseSkin.Clear();

            // A capsule's long axis is Y, so the 90-degree turn lives here, once, rather than in every
            // placement. Body is 1.24 m long and 0.42 m across — a big bird.
            _gooseBody = Part(PrimitiveType.Capsule, "Body", _matGoose,
                              new Vector3(0.42f, 0.62f, 0.42f), Vector3.zero,
                              Quaternion.Euler(90f, 0f, 0f));
            _gooseBodyScale = _gooseBody.localScale;

            _neck = Part(PrimitiveType.Capsule, "Neck", _matGoose,
                 new Vector3(0.17f, 0.30f, 0.17f), new Vector3(0f, 0.17f, 0.50f),
                 Quaternion.Euler(58f, 0f, 0f));
            _neckRest = _neck.localRotation;
            Part(PrimitiveType.Cube, "Tail", _matGoose,
                 new Vector3(0.11f, 0.24f, 0.30f), new Vector3(0f, 0.10f, -0.60f),
                 Quaternion.Euler(-18f, 0f, 0f));
            _wingL = Part(PrimitiveType.Cube, "WingL", _matGoose,
                 new Vector3(0.60f, 0.07f, 0.34f), new Vector3(-0.40f, 0.06f, 0.02f),
                 Quaternion.Euler(0f, 0f, 13f));
            _wingR = Part(PrimitiveType.Cube, "WingR", _matGoose,
                 new Vector3(0.60f, 0.07f, 0.34f), new Vector3(0.40f, 0.06f, 0.02f),
                 Quaternion.Euler(0f, 0f, -13f));
            _wingRestL = _wingL.localRotation;
            _wingRestR = _wingR.localRotation;

            // Deliberately NOT added to the skin list: the bill keeps its bright colour through a
            // strike, so the object never loses the one edge that states its heading.
            Part(PrimitiveType.Cube, "Bill", _matGooseBill,
                 new Vector3(0.13f, 0.10f, 0.32f), new Vector3(0f, 0.31f, 0.78f),
                 Quaternion.identity, skin: false);

            _goose.gameObject.SetActive(false);
        }

        Transform Part(PrimitiveType type, string name, Material mat, Vector3 scale,
                       Vector3 localPos, Quaternion localRot, bool skin = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.SetParent(_goose, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = scale;

            var r = go.GetComponent<MeshRenderer>();
            if (r != null)
            {
                r.sharedMaterial = mat;
                // The bird casts. Its shadow crossing the beds is most of how the player reads where it
                // is going to land, and on a low chase camera it is often the clearer of the two reads.
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                if (skin) _gooseSkin.Add(r);
            }
            return go.transform;
        }

        void SetGooseHot(bool hot)
        {
            var m = hot ? _matGooseHot : _matGoose;
            foreach (var r in _gooseSkin) if (r != null) r.sharedMaterial = m;
        }

        /// <summary>
        /// Send the next goose in — and it goes at whichever garden is next in the stream.
        ///
        /// ALTERNATING is the change that turns three throws into a raid. The player asked for it in as
        /// many words: "계속 나랑 상대방 방향으로 번갈아가면서 새로운 거위가 나한테 날라오고" — geese keep
        /// coming, alternating between my direction and the opponent's. Before this the flock only ever
        /// attacked the duck, so the far end of the pitch was a wall to aim at and the rival was a capsule
        /// with nothing at stake; the only way their beds could ever be touched was by the player putting a
        /// bird there personally. Now both gardens are under attack from the start, both counters move
        /// whether or not the player does anything, and the raid is a contest the player is IN rather than
        /// a target range they are shooting at.
        ///
        /// The two sides pick their bed by different rules, and the difference is fairness in both
        /// directions:
        ///
        ///   * At the PLAYER, the nearest living bed to the mower. The phase is a timing test, so the bird
        ///     has to be reachable and visible from where the player already is; a random bed would turn
        ///     every dive into a drive across the garden and beds would be lost to travel time rather than
        ///     to bad timing. The 16 m frontage is for CHOOSING where to stand between exchanges.
        ///   * At the RIVAL, a living bed at random. They are one capsule who does not move, so which bed
        ///     is picked is exactly what decides whether they can reach it — see
        ///     <see cref="KeeperAttempt"/>. Picking the bed nearest them would make their end a formality;
        ///     picking at random makes their end a genuine second front the player can watch and count.
        /// </summary>
        void StartDive() => StartDive(_diveAtPlayer);

        void StartDive(bool atPlayer)
        {
            if (_arena == null || _mower == null) { _gooseState = GooseState.Gone; return; }

            _diveAtPlayer = atPlayer;
            _exchanges++;

            if (atPlayer)
            {
                var bed = _arena.NearestAlive(_mower.transform.position, playerSide: true);
                // Nothing left to defend. Aim at the middle so the phase still plays out rather than
                // stopping dead on a technicality.
                _diveTarget = bed != null ? bed.position : _arena.PlayerGardenCentre;
            }
            else
            {
                var bed = _arena.AnyAlive(_rng, playerSide: false);
                _diveTarget = bed != null ? bed.position : _arena.OpponentGardenCentre;
            }

            // Comes in over the OTHER garden's side, give or take, so "back the way it came" is a
            // direction the player can see rather than a rule to learn — and so a bird attacking the
            // rival passes over the player's head on its way, which is what makes the second front
            // something the player watches rather than something they are told about.
            //
            // The spread WIDENS with the raid: pre-aiming a bearing that is always within 25 degrees of
            // downfield is most of what makes a late exchange easy, and widening it costs the player
            // nothing in reaction time — the window is still 2r/v — while making a fifteenth goose a
            // genuinely harder read than a second one. This is the escalation channel that keeps working
            // after diveSpeedMax has bound.
            float arc = Mathf.Lerp(25f, 42f, MatchHeat);
            float spread = ((float)_rng.NextDouble() * 2f - 1f) * arc;
            Vector3 from = atPlayer ? _toOpponent : -_toOpponent;
            Vector3 azimuth = Quaternion.Euler(0f, spread, 0f) * from;
            Vector3 dir = (azimuth + Vector3.up * Mathf.Tan(diveElevation * Mathf.Deg2Rad)).normalized;

            _goosePos = _diveTarget + dir * approachDistance;
            _goosePrev = _goosePos;
            _gooseVel = -dir * CurrentDiveSpeed();
            _gooseState = GooseState.Diving;
            _gooseTimer = 0f;
            _gooseBounces = 0;

            SetGooseHot(false);
            _goose.gameObject.SetActive(true);
            PlaceGoose();
        }

        /// <summary>
        /// Hand the stream on to the other end and send the next bird.
        ///
        /// One place, because "whose turn is it" is the kind of bookkeeping that goes wrong quietly: a
        /// flip missed on one of the four paths that end an exchange would leave the raid attacking the
        /// same garden forever, and the symptom would be a contest that looks fine and is one-sided.
        /// </summary>
        void NextInStream()
        {
            _keeperAttemptsLeft = 1;
            StartDive(!_diveAtPlayer);
        }

        /// <summary>
        /// How far into the raid we are, 0 to 1. Everything that escalates past the speed cap reads this.
        /// </summary>
        public float MatchHeat => Mathf.Clamp01(_exchanges / (float)Mathf.Max(heatFullExchanges, 1));

        /// <summary>
        /// Dive speed: the match ramp plus the combo, capped.
        ///
        /// Two terms rather than one. <see cref="diveSpeedPerExchange"/> never goes backwards, so the raid
        /// gets harder the longer it runs whoever is winning; <see cref="diveSpeedPerRally"/> is lost with
        /// the combo, so a clean run of parries feels like one. Before this the rally term was the only
        /// escalation there was, which meant a missed goose handed the player the opening speed back — the
        /// difficulty curve ran backwards for exactly the player who was struggling.
        /// </summary>
        public float CurrentDiveSpeed() => Mathf.Min(diveSpeedMax,
            diveSpeed + diveSpeedPerExchange * Mathf.Max(0, _exchanges - 1) + diveSpeedPerRally * Rally);

        /// <summary>
        /// Seconds of dead air between exchanges, shrinking as the raid heats up.
        ///
        /// The other half of the escalation the speed cap cannot provide. A shorter gap does not narrow the
        /// parry window by a millisecond — it removes the time to breathe, reposition and look at the far
        /// end between windows, which is the thing that actually makes a late exchange feel relentless.
        /// Floored at 45% so the raid never becomes a continuous stream with no pulse to it.
        /// </summary>
        float PacedGap(float baseSeconds) => baseSeconds * Mathf.Lerp(1f, 0.45f, MatchHeat);

        /// <summary>
        /// The rival's one attempt on a goose arriving at their garden, and the answer to "who is over
        /// there".
        ///
        /// True if they keep it out. What decides it is REACH, not a hidden dice roll: they are a single
        /// capsule planted in a sixteen-metre goalmouth, so a bird that comes down beside them is theirs
        /// and one put down at the far end of their garden is past them before they have moved. That makes
        /// their end a thing the player can read off the pitch and beat ON PURPOSE — aim away from the
        /// rival — which is the same shape as the player's own cone, where direction decides contact.
        /// <see cref="keeperSkill"/> then keeps even a reachable bird from being an automatic save, so
        /// beating them reads as beating somebody rather than as clearing a threshold.
        ///
        /// Their body language runs on both branches. They stood in their garden through the entire old
        /// phase without moving, and a keeper who does not visibly react to a save is not a keeper.
        /// </summary>
        bool KeeperAttempt(Vector3 where, bool watchedItComeIn)
        {
            if (_keeperAttemptsLeft <= 0) return false;
            _keeperAttemptsLeft--;

            // Two situations, and they are NOT the same save. The first measured raid proved it: with reach
            // applied to everything, the rival conceded both of their own dives and a player who never
            // touched the horn came within one bed of winning. Their end has to be defended by somebody,
            // or the alternating stream is a coin flip about whose garden collapses first.
            //
            //   * A goose DIVING at their own garden, they watched cross the whole pitch — the same three
            //     seconds of warning the player gets, and they are free to walk to it. Reach cannot bind,
            //     so the save is their skill and nothing else. That is why a passive player now loses: the
            //     far end holds itself.
            //   * A bird the PLAYER punted in arrives low, fast and skipping off the turf from fifty
            //     metres, with a fraction of that warning. Here reach binds hard, and where the ball came
            //     to rest relative to where they are standing is what decides it. That is the shot the
            //     player is being asked to make, and it is the only way their garden can be broken quickly.
            float chance = keeperSkill;
            float d = 0f;
            if (!watchedItComeIn)
            {
                Vector3 post = _arena != null && _arena.opponentNpc != null
                    ? _arena.opponentNpc.position
                    : (_arena != null ? _arena.OpponentGardenCentre : where);
                Vector3 flat = where - post; flat.y = 0f;
                d = flat.magnitude;
                chance *= 1f - Mathf.Clamp01(d / Mathf.Max(keeperReach, 0.5f));
            }

            bool saved = chance > 0f && _rng.NextDouble() < chance;

            // Drawn up and forward on a save, tipped back on a concession. Same call the phase already
            // used for their reaction to a strike, so the vocabulary stays one vocabulary.
            ReactOpponent(saved ? 0.9f : 0.7f, inTheirFavour: saved);

            // Gated on the greybox flag, like the IMGUI readout: while this phase is being tuned, "what did
            // their keeper do and why" is the single hardest thing to see from a frame, because it happens
            // fifty metres away and resolves in one tick.
            if (showReadout)
                Debug.Log($"[Duck] keeper: {(watchedItComeIn ? "dive at their beds" : $"my ball {d:0.0} m from them")}" +
                          $" — {(saved ? "SAVED" : "beaten")} (chance {chance:0.00})");
            return saved;
        }

        /// <summary>
        /// The rival returns it: the same bird comes straight back at the duck.
        ///
        /// Sent as a DIVE rather than as a thrown ball, and that asymmetry is deliberate. Everything that
        /// comes at the player is a dive so it arrives with the whole readable apparatus — the anticipation
        /// pose, the timing ring, the cone, the tiers — while everything the player hits is a launched ball
        /// that arcs and bounces. From the seat, geese attack and struck geese travel; there is never a
        /// thing coming at you that the horn does not know how to meet.
        ///
        /// It does NOT reset the rally. The bird being alive is the rally, and the exchange the player is
        /// in the middle of is the one thing here that should feel continuous.
        /// </summary>
        void KeeperReturn()
        {
            _outcome.keeperSaves++;
            AudioDirector.Instance?.CrowdCheer(0.4f);
            LowThud(0.35f);
            StartDive(atPlayer: true);
        }

        // ------------------------------------------------------------------ scripted parries
        //
        // THE REVIEW LOOP CANNOT PRESS A KEY. Unity is driven here through MCP, which can build, play,
        // run menu items and capture frames but cannot send keystrokes — so the horn is unreachable and
        // every captured frame of this phase was a miss. `0 parries (0P 0G 0N), longest x0`. That makes
        // the entire impact layer — tiers, hit stop, the perfect slow-motion, the camera punch, the
        // launch arc, an escalating rally, a goose landing on the opponent — invisible to the only
        // process that can look at it, and it is most of what the phase is.
        //
        // So the honk gets a second way in. These arm an automatic strike at a chosen tier and are
        // otherwise inert: no new gameplay, no change to what a real press does, and E is untouched.

        Tier _armedTier = Tier.Miss;
        int _armedRemaining;

        /// <summary>How many scripted parries are still queued. Shown in the readout.</summary>
        public int DebugArmedRemaining => _armedRemaining;

        /// <summary>
        /// Flatten one garden to its cap, so the WIN and the LOSS beats can be reached on demand.
        ///
        /// The same argument as the scripted parries, one level up. A result now needs three geese through
        /// one end of a live contest, which is minutes of driving per captured frame, and the review loop
        /// cannot drive — so the two most important frames the phase can produce would have had no capture
        /// path at all, exactly as the tiers had none before DebugArmParries existed.
        ///
        /// It does NOT set the result itself, and that distinction is the whole reason this is safe: it does
        /// the damage through the arena's ordinary Damage call and then lets the raid notice, so the
        /// condition fires through precisely the code path a real raid takes. A capture taken this way is
        /// evidence about the real rule rather than about a shortcut.
        /// </summary>
        public void DebugBreakGarden(bool playerSide)
        {
            if (_arena == null || State != Phase.Live)
            {
                Debug.LogWarning("[Duck] no live raid to decide. Press play in Assets/Scenes/Arena.unity.");
                return;
            }

            int cap = playerSide ? _myCap : _theirCap;
            int already = playerSide ? _arena.PlayerBedsLost : _arena.OpponentBedsLost;
            Vector3 centre = playerSide ? _arena.PlayerGardenCentre : _arena.OpponentGardenCentre;

            // One sweep of the whole garden, limited to what the cap still allows.
            int killed = _arena.Damage(centre, _arena.gardenHalfWidth * 3f, playerSide,
                                       cap - already, cap - already);
            _fx?.Dirt(centre, 0.9f);
            Debug.Log($"[Duck] DEBUG flattened {killed} {(playerSide ? "player" : "opponent")} beds to the " +
                      $"cap of {cap}. The raid should call itself on the next frame.");
        }

        /// <summary>
        /// Queue <paramref name="count"/> automatic parries at <paramref name="tier"/>.
        ///
        /// Each one fires the moment the diving goose is inside that tier's radius. If it never gets
        /// that close — a Perfect needs the machine parked on the threatened bed, and a scripted run
        /// does not drive — the strike fires on the frame the bird would otherwise have landed, so the
        /// requested tier is always what gets demonstrated. That is the right behaviour for an
        /// instrument whose job is to show what a tier looks like, and the wrong behaviour for
        /// gameplay, which is why nothing but the editor tools calls it.
        /// </summary>
        public void DebugArmParries(Tier tier, int count)
        {
            _armedTier = tier;
            _armedRemaining = Mathf.Max(0, count);
        }

        public void DebugClearArming()
        {
            _armedTier = Tier.Miss;
            _armedRemaining = 0;
        }

        float RadiusFor(Tier tier) => tier switch
        {
            Tier.Perfect => perfectRadius,
            Tier.Good => goodRadius,
            _ => parryRadius
        };

        /// <summary>
        /// Fire a queued scripted parry if this is its moment. True if it struck, so the caller knows
        /// not to also run the crash test on the same frame.
        /// </summary>
        bool TryArmedParry(bool aboutToLand)
        {
            // Same rule as Judge: a scripted parry waits for a goose that is actually coming at the duck.
            // Without this the arm was spent on the frame a rival-bound bird reached the far garden, and
            // every burst would have photographed a strike fifty metres off camera.
            if (_armedRemaining <= 0 || _armedTier == Tier.Miss || _mower == null || !_diveAtPlayer)
                return false;

            float d = Vector3.Distance(_goosePos, _mower.transform.position);
            if (d > RadiusFor(_armedTier) && !aboutToLand) return false;

            _armedRemaining--;
            Honk();
            Strike(_armedTier);
            return true;
        }

        void StepGoose(float dt)
        {
            if (_regroup > 0f)
            {
                _regroup -= dt;
                if (_regroup <= 0f && State == Phase.Live) NextInStream();
                return;
            }

            switch (_gooseState)
            {
                case GooseState.Gone:
                    return;

                case GooseState.Diving:
                {
                    Advance(dt);
                    // Judged on having PASSED the target rather than on proximity to it, so a fast
                    // goose on a long frame cannot tunnel through and orbit forever.
                    bool landing = Vector3.Dot(_goosePos - _diveTarget, _gooseVel) >= 0f ||
                                   _goosePos.y <= 0.3f;

                    // A goose diving at the RIVAL'S garden is not the player's problem, and the horn
                    // cannot reach it — it crosses the player's end at eighteen metres up. Their keeper
                    // meets it instead, and the raid's second front resolves without the player being
                    // involved at all. That is the point of it: their counter moves whether or not the
                    // duck does anything, so there are two gardens falling rather than one target range.
                    if (!_diveAtPlayer)
                    {
                        if (landing)
                        {
                            if (KeeperAttempt(_diveTarget, watchedItComeIn: true)) KeeperReturn();
                            else RivalConcedes();
                        }
                        break;
                    }

                    if (TryArmedParry(landing)) break;
                    if (landing) Crash();
                    break;
                }

                case GooseState.Launched:
                    _gooseVel += Vector3.down * launchGravity * dt;
                    Advance(dt);
                    _gooseTimer += dt;
                    // The directional trail, and PERFECT ONLY — that is the whole point of it. The goal
                    // asks for it under "perfect parries should feel dramatically stronger", so putting
                    // it on every launch would spend the one signal that distinguishes the best hit.
                    //
                    // Shed at a fixed distance rather than on a timer, so the spacing reads as SPEED: a
                    // fast throw lays the same marks further apart, and the trail says how hard it was
                    // hit as well as where it went.
                    if (LastStrikeTier == Tier.Perfect)
                    {
                        _trailRun += (_goosePos - _goosePrev).magnitude;
                        if (_trailRun >= 1.1f)
                        {
                            _trailRun = 0f;
                            _fx?.TrailPuff(_goosePos);
                        }
                    }
                    // A minimum airtime before it can be called down. Without it, a goose met at ankle
                    // height was already under the landing line on the frame it was struck, so it
                    // "landed" instantly beside the player and got credited to whichever garden was
                    // nearest — damage from a throw that never travelled.
                    // IT BOUNCES. The player asked for "공처럼 튕기는 걸" — like a ball — and the old
                    // behaviour was the opposite of that: the first time it touched grass it stopped dead,
                    // scored, and the exchange was over. One touch, one outcome, nothing to chase.
                    //
                    // Now it rebounds with restitution and keeps its downfield momentum, so a good hit
                    // SKIPS toward the far goal and a weak one dribbles to a halt short of it. That single
                    // change is what makes the pitch read as a ball game rather than as a throw: the bird
                    // is live between bounces, it can still be reached, and where it finally settles is
                    // the result of how hard it was struck rather than of one instant of contact.
                    if (_goosePos.y <= GroundY && _gooseVel.y < 0f)
                    {
                        _gooseBounces++;
                        _goosePos.y = GroundY;

                        // Vertical energy comes back at 52%; horizontal is scrubbed only lightly, so the
                        // skip carries downfield instead of stalling on the spot.
                        _gooseVel.y = -_gooseVel.y * 0.52f;
                        _gooseVel.x *= 0.86f;
                        _gooseVel.z *= 0.86f;

                        // Each touch is an event: dirt, a thud, and a squash that shrinks as it settles.
                        float hard = Mathf.Clamp01(_gooseVel.magnitude / Mathf.Max(launchSpeed, 1f));
                        _fx?.Dirt(_goosePos, hard * 0.5f);
                        _squash = Mathf.Max(_squash, hard * 0.5f);
                        LowThud(hard * 0.45f);
                        if (_gooseBounces == 1) Honk(0.5f);

                        // Settled: too slow to skip again, out of bounces, or out of patience. THEN it
                        // scores, and it scores where it actually came to rest.
                        if (_gooseVel.magnitude < 3.2f || _gooseBounces >= 4 || _gooseTimer > 6f)
                            SettleLaunched();
                    }
                    else if (_gooseTimer > 6f)
                    {
                        // Budgeted, not trusted — the same rule every other beat here is under.
                        SettleLaunched();
                    }
                    break;

                case GooseState.Returning:
                    // This bird is SPENT. It had its one extra life, somebody has had the last touch on
                    // it, and the stream hands over to the other garden — "핑퐁된 거위는 두번째 상대가
                    // 튕기거나 튕겨내지 못할때 소멸". A short pause so the raid has a pulse rather than
                    // being a continuous stream, and the pause SHRINKS as the raid heats up, which is
                    // escalation the speed cap cannot supply.
                    _gooseTimer += dt;
                    if (_gooseTimer >= PacedGap(0.35f))
                    {
                        _goose.gameObject.SetActive(false);
                        if (State == Phase.Live) NextInStream();
                        else _gooseState = GooseState.Gone;
                    }
                    break;

                case GooseState.Crashing:
                    _gooseTimer += dt;
                    if (_gooseTimer >= 0.5f)
                    {
                        _goose.gameObject.SetActive(false);
                        _gooseState = GooseState.Gone;
                        if (State == Phase.Live) _regroup = PacedGap(regroupSeconds);
                    }
                    break;
            }
        }

        void Advance(float dt)
        {
            _goosePrev = _goosePos;
            _goosePos += _gooseVel * dt;
            PlaceGoose();
        }

        void PlaceGoose()
        {
            if (_goose == null) return;

            Vector3 travel = _goosePos - _goosePrev;
            Quaternion rot = travel.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(travel.normalized, Vector3.up)
                : _goose.rotation;
            // No correction Euler: the parts are pre-oriented in BuildGoose so the root's +Z is forward.
            // That is what lets the body be squashed below without also rolling the heading.
            _goose.SetPositionAndRotation(_goosePos, rot);

            if (_gooseBody == null) return;
            // Squash along the direction of travel and bulge across it — the classic impact read, and
            // the cheapest thing on the whole juice list. The body's own local Y runs along the root's
            // +Z (see the capsule turn in BuildGoose), so travel is Y here and the two widths are X and Z.
            float s = _squash;
            _gooseBody.localScale = new Vector3(_gooseBodyScale.x * (1f + s * 0.45f),
                                               _gooseBodyScale.y * (1f - s * 0.40f),
                                               _gooseBodyScale.z * (1f + s * 0.45f));

            // Time.deltaTime rather than a threaded-through dt: PlaceGoose is called from several places
            // and only one of them has one, and this IS the dt the phase is driven with — the host passes
            // Time.deltaTime into Tick. Reading it here keeps the flap frozen during a hit stop for the
            // same reason, since timeScale is what the freeze bends.
            AnimateLimbs(Time.deltaTime);
        }

        /// <summary>
        /// Wingbeat while it flies, and a neck whip when it is hit.
        ///
        /// The neck, tail and wings were built and then never moved by anything: the bird crossed fifty
        /// metres as a rigid ornament and took a mower to the chest without so much as a twitch. Two of
        /// the goal's own words — "neck whip" and "wing flapping" — had geometry and no motion, which is
        /// the same trap as the crowd that was never in the scene and the audio with no director. Present
        /// in the build, absent from the game.
        ///
        /// The FLAP is a plain sine on the wings' rest pose, faster while diving than while sailing,
        /// because a bird beating hard is a bird committed to arriving. The WHIP shares the strike
        /// envelope: the neck snaps past its rest angle and the wings fold back, so the hit reads as
        /// having gone THROUGH the animal rather than having stopped at its outline.
        ///
        /// Driven by the scaled dt like everything else, so the flap freezes with the world during a hit
        /// stop instead of carrying on and giving away that the freeze is only skin deep.
        /// </summary>
        /// <summary>
        /// The tell: how close the bird is to being hittable, as a number the pose and the cue both read.
        ///
        /// The parry had NO anticipation at all. Every signal fired after contact — freeze, punch, debris,
        /// a word on screen — so the window could only ever be found by trial, never read. Juice that only
        /// happens on success teaches nothing, and "a clear anticipation pose and readable timing cue" is
        /// first on the goal's list for that reason.
        ///
        /// Measured in TIME rather than in distance, because the dive speed climbs with the rally: at
        /// 20 m/s the outer window is 0.4 s wide and at 32 m/s it is 0.25 s, so a fixed-distance tell would
        /// give less and less warning exactly as the exchange got harder. A time-based one gives the same
        /// warning at every speed, which is what makes a long rally hard because it is FAST rather than
        /// because it became unreadable.
        /// </summary>
        void UpdateTell()
        {
            float range = GooseRange;
            if (range < 0f)
            {
                _tell = 0f;
                _fx?.ClearTimingRing();
                return;
            }

            float speed = Mathf.Max(_gooseVel.magnitude, 1f);
            float secondsOut = Mathf.Max(range - parryRadius, 0f) / speed;
            // Full tell from a third of a second out. Less than that and it arrives with the bird; much
            // more and it is on screen so long it stops meaning "now".
            _tell = 1f - Mathf.Clamp01(secondsOut / 0.34f);

            if (_mower != null && _tell > 0.02f)
                _fx?.TimingRing(_mower.transform.position,
                                Mathf.Lerp(parryRadius * 2.2f, parryRadius, _tell), _tell,
                                Quaternion.LookRotation(
                                    Vector3.ProjectOnPlane(_mower.transform.forward, Vector3.up)
                                        .sqrMagnitude > 1e-4f
                                    ? Vector3.ProjectOnPlane(_mower.transform.forward, Vector3.up).normalized
                                    : _toOpponent, Vector3.up));
            else
                _fx?.ClearTimingRing();
        }

        void AnimateLimbs(float dt)
        {
            bool flying = _gooseState == GooseState.Diving || _gooseState == GooseState.Launched;
            // Beat harder on the way in; a launched bird is tumbling, not flying.
            float rate = _gooseState == GooseState.Diving ? 13f : 7f;
            if (flying) _flapPhase += dt * rate;

            float beat = flying ? Mathf.Sin(_flapPhase) : 0f;
            // Struck wings fold back and stop beating, which is what makes the flap's ABSENCE readable.
            float fold = _whip * 62f;
            // The ANTICIPATION POSE: wings flare wide and the beat stills as the bird commits. A held
            // pose reads as a wind-up where a faster flap would only read as more of the same motion,
            // and stillness against a beating silhouette is the clearest change a shape this size can
            // make. Suppressed once struck, so the tell cannot fight the impact.
            float tell = _tell * (1f - _whip);
            float flare = tell * 38f;
            float amp = Mathf.Lerp(34f, 6f, Mathf.Max(_whip, tell * 0.8f));

            if (_wingL != null)
                _wingL.localRotation = _wingRestL * Quaternion.Euler(0f, 0f, beat * amp - fold + flare);
            if (_wingR != null)
                _wingR.localRotation = _wingRestR * Quaternion.Euler(0f, 0f, -beat * amp + fold - flare);

            if (_neck != null)
            {
                // Past the rest angle rather than toward it: a whip overshoots, and the overshoot is the
                // whole reason it reads as a whip and not as a nod.
                // Drawn BACK on the tell and thrown forward on the whip, so the two read as one
                // gesture: wind up, then snap through.
                float lash = -_whip * 54f + tell * 22f + beat * (flying ? 5f : 0f);
                _neck.localRotation = _neckRest * Quaternion.Euler(lash, 0f, 0f);
            }
        }

        /// <summary>
        /// The goose gets through: it comes down in the garden and flattens beds.
        ///
        /// This is the whole cost of the phase, and it is deliberately unrecoverable — a flattened bed
        /// stays flat, exactly as a knocked gnome now stays knocked, so the damage is on the ground to
        /// be seen rather than a number on a card claiming it.
        /// </summary>
        void Crash()
        {
            _outcome.crashes++;
            Rally = 0;

            int remaining = _myCap - (_arena != null ? _arena.PlayerBedsLost : 0);
            int killed = _arena != null
                ? _arena.Damage(_diveTarget, crashRadius, true, bedsPerCrash, remaining) : 0;

            _goosePos = new Vector3(_diveTarget.x, 0.35f, _diveTarget.z);
            PlaceGoose();
            _gooseState = GooseState.Crashing;
            _gooseTimer = 0f;

            // The machine feels it even though nothing hit it — a goose coming down beside the duck at
            // speed. Scaled by how close, so a crash across the garden is not a shove.
            if (_mower != null)
            {
                float near = 1f - Mathf.Clamp01(
                    Vector3.Distance(_diveTarget, _mower.transform.position) / 12f);
                if (near > 0.08f) _mower.Bonk(_diveTarget, near * 0.45f);
            }

            _camera?.AddShake(0.7f);
            _hitStop = 0.05f;

            var audio = AudioDirector.Instance;
            if (audio != null)
            {
                audio.PlayOne(Pick(audio.bonks), 0.55f);
                audio.PlayOne(Pick(audio.debrisPings), 0.5f);
                audio.PlayOne(audio.quackPanic, 0.6f);
                audio.CrowdGroan(killed > 0 ? 0.7f : 0.4f);
            }
            LowThud(0.7f);
            Honk(0.9f);
            PunchJudges(-0.6f);
            // A goose in the player's beds is the rival's good news.
            ReactOpponent(0.8f, inTheirFavour: true);

            // Earth and leaves, no feathers and no ring. The goose was not struck here — borrowing the
            // parry's vocabulary would tell the player they had hit something when they had not.
            _fx?.Dirt(_diveTarget, killed > 0 ? 0.95f : 0.55f);

            // And the fence it came through.
            //
            // The segment has to CROSS THE PERIMETER, which is the whole point and which _goosePrev alone
            // cannot give: that is one frame back, a third of a metre, so the segment was a dot at the
            // crater. The fence stands 5 m out and the beds are 1.5–3.4 m in, so nothing was ever within
            // reach and the first version toppled exactly zero boards while reporting success.
            //
            // Extended backwards along the flight direction instead, far enough to reach the fence line
            // from anywhere in the garden.
            if (_arena != null)
            {
                Vector3 travel = _diveTarget - _goosePrev;
                travel.y = 0f;
                if (travel.sqrMagnitude < 1e-4f) travel = _toOpponent;
                Vector3 entry = _diveTarget - travel.normalized * (_arena.gardenHalfWidth + 6f);

                int boards = _arena.SmashFence(entry, _diveTarget, 2.6f, 3);
                if (boards > 0) _fx?.Dirt(_diveTarget, 0.4f);
            }
        }

        /// <summary>
        /// A struck goose has come to rest. Resolve it, then either hand the stream on or send it back.
        ///
        /// One place for the end of a launch because there are two ways into it — settled after its
        /// bounces, or out of airtime — and they used to duplicate four lines each. The bird ALWAYS leaves
        /// play from here unless the rival returned it, which is the rule that stops the ping-pong: a
        /// goose gets one keeper attempt, and after that the exchange is over however it went.
        /// </summary>
        void SettleLaunched()
        {
            if (LandLaunched()) return;   // returned by their keeper: it is already diving at the duck

            _gooseState = GooseState.Returning;
            _gooseTimer = 0f;
            _gooseBounces = 0;
        }

        /// <summary>
        /// Work out what a thrown goose hit, from where it came down. True if the rival returned it.
        ///
        /// By position, never by intention. A throw that falls short lands in the open ground between
        /// the gardens and harms nobody; one sent into the player's own flowers harms the player. That
        /// second case is the real cost of a scrappy hit, and it is why the tiers matter beyond the
        /// noise they make — only a good or perfect strike gets to choose where the bird goes.
        ///
        /// THE RIVAL NOW STANDS IN THE WAY. A bird that reaches their garden used to score on arrival, so
        /// the far end was a wall with a hit-box; now it gets one attempt from whoever is defending it, and
        /// how well the player placed the bird is what decides whether that attempt has a chance. Reaching
        /// their garden is no longer the same thing as beating them, and the difference between the two is
        /// the skill the phase is actually about.
        /// </summary>
        bool LandLaunched()
        {
            if (_arena == null) return false;
            Vector3 flat = new Vector3(_goosePos.x, 0f, _goosePos.z);

            // WHERE IT ACTUALLY CAME DOWN, said out loud. Gated on the greybox flag like the readout.
            //
            // This exists because the first measured raid reported five good parries and zero geese on the
            // rival's beds, and nothing on screen or in the log could say why: a throw that lands on open
            // pitch resolves to no damage, no sound and no counter moving, so it is indistinguishable from
            // a throw that scored and from one that never happened. The one number that explains it — how
            // far up the fifty metres the bird got — was the one number nobody could see.
            if (showReadout)
            {
                float up = Vector3.Dot(flat - _arena.PlayerGardenCentre, _toOpponent);
                Debug.Log($"[Duck] ball down {up:0.0} m upfield of {_arena.GardenGap:0} " +
                          $"({(_arena.IsInsideGarden(flat, false) ? "IN THEIR GARDEN" : _arena.IsInsideGarden(flat, true) ? "in my own" : "open pitch")})" +
                          $" after a {LastStrikeTier} strike.");
            }

            if (_arena.IsInsideGarden(flat, playerSide: false))
            {
                // Their attempt, before any damage. Saved and the bird is on its way back at the duck with
                // nothing broken; beaten and it comes down in their flowers.
                if (KeeperAttempt(flat, watchedItComeIn: false))
                {
                    KeeperReturn();
                    return true;
                }

                var bed = _arena.NearestAlive(flat, false);
                int killed = _arena.Damage(bed != null ? bed.position : flat, crashRadius, false,
                                           bedsPerCrash, _theirCap - _arena.OpponentBedsLost);
                if (killed <= 0) return false;

                _outcome.hitsOnOpponent++;
                AudioDirector.Instance?.CrowdCheer(0.75f);
                _crowd?.Excite(0.7f);
                PunchJudges(0.7f);
                _fx?.Dirt(bed != null ? bed.position : flat, 0.8f);
                ReactOpponent(0.9f, inTheirFavour: false);
                return false;
            }

            if (!_arena.IsInsideGarden(flat, playerSide: true)) return false;

            var mine = _arena.NearestAlive(flat, true);
            int lost = _arena.Damage(mine != null ? mine.position : flat, crashRadius, true,
                                     bedsPerCrash, _myCap - _arena.PlayerBedsLost);
            if (lost <= 0) return false;

            _outcome.ownGoals++;
            var audio = AudioDirector.Instance;
            audio?.CrowdGroan(0.6f);
            audio?.PlayOne(audio.quackAnnoyed, 0.6f);
            PunchJudges(-0.5f);
            return false;
        }

        /// <summary>
        /// A goose the RIVAL failed to keep out. Their beds, their fault, and none of the player's doing.
        ///
        /// Counted apart from <see cref="Outcome.hitsOnOpponent"/> and deliberately quieter than one the
        /// player put there: it still moves the match toward a win, because the rule is simply whichever
        /// garden falls first, but the award must not pay a mark for something that happened at the far end
        /// while the duck was parked. The crowd notices; the bench does not credit it.
        /// </summary>
        void RivalConcedes()
        {
            int killed = _arena != null
                ? _arena.Damage(_diveTarget, crashRadius, false, bedsPerCrash,
                                _theirCap - _arena.OpponentBedsLost) : 0;

            _outcome.rivalConceded += killed > 0 ? 1 : 0;

            _goosePos = new Vector3(_diveTarget.x, 0.35f, _diveTarget.z);
            PlaceGoose();
            _gooseState = GooseState.Crashing;
            _gooseTimer = 0f;

            // No hit stop, no bonk, no shake. Fifty metres away and nothing touched the machine — freezing
            // the clock for it would give a far-end event the punctuation reserved for contact.
            _fx?.Dirt(_diveTarget, killed > 0 ? 0.8f : 0.45f);
            LowThud(0.3f);

            var audio = AudioDirector.Instance;
            if (audio != null)
            {
                audio.PlayOne(Pick(audio.bonks), 0.3f);
                if (killed > 0) audio.CrowdCheer(0.5f);
            }
            _crowd?.Excite(killed > 0 ? 0.45f : 0.2f);
        }

        /// <summary>
        /// The launch velocity that carries a ballistic arc from one point to another in a given time.
        ///
        /// Standard projectile solve. Used only where the strike earned placement — everywhere else the
        /// goose is thrown along the heading and lands wherever the arc drops it.
        /// </summary>
        static Vector3 ArcTo(Vector3 from, Vector3 to, float seconds, float gravity)
        {
            float t = Mathf.Max(seconds, 0.05f);
            Vector3 d = to - from;
            return new Vector3(d.x / t, d.y / t + 0.5f * gravity * t, d.z / t);
        }

        // ------------------------------------------------------------------ camera & music energy

        void UpdateCameraEnergy(float dt)
        {
            float energy = Mathf.Clamp01(Rally / (float)Mathf.Max(rallyEnergyFull, 1));

            if (_camera != null)
            {
                // The camera does NOT chase the incoming bird.
                //
                // This overrides the brief I was working to, which asked for the aim to bias toward the
                // goose while it closed. On the player's own verdict — "날라올때 카메라가 그걸 안봤으면
                // 좋겠고" — that framing is unwanted, and they are the authority on how it feels. It also
                // has a reason behind it: the lens drifting off the machine while a bird converges on you
                // takes the frame away from the thing you are steering at exactly the moment steering
                // matters, so the camera fights the player for the last third of a second.
                //
                // A LAUNCHED goose still gets followed, because that is the ball travelling and it costs
                // the player nothing — they are not aiming at anything while it is in the air.
                if (_gooseState == GooseState.Launched)
                {
                    _camera.SetDefenceSubject(_goose, energy);
                }
                else if (_gooseState == GooseState.Diving)
                {
                    // Held on the far goal, as it is between exchanges. The bird is readable in the frame
                    // because it is skylined against blue, not because the camera turned to look at it.
                    _camera.SetDefenceSubject(_arena != null ? _arena.OpponentMarker : null,
                                              energy, biasScale: 0.4f);
                }
                else
                {
                    // Between exchanges the camera holds the OPPONENT'S board instead of snapping back
                    // to the mower's tail. Without this the chase camera looks wherever the duck is
                    // pointing, which is usually at its own feet, and the far garden — the thing every
                    // throw is aimed at — was never in shot at all. At a third of the bias so the duck
                    // keeps the foreground: full bias pitched the lens so far up the field that the
                    // machine sat on the bottom edge.
                    _camera.SetDefenceSubject(_arena != null ? _arena.OpponentMarker : null,
                                              energy, biasScale: 0.55f);
                }
            }

            if (_voiceRally != null && _voiceRally.clip != null)
            {
                // The urgent layer is an authored overlay for the round loop, and a rally is what it
                // was made for. Faded rather than switched, so the escalation is felt before it is
                // noticed.
                float want = Mathf.Clamp01(Rally / 3f) * 0.55f;
                _voiceRally.volume = Mathf.MoveTowards(_voiceRally.volume, want, dt * 0.9f);
                // And the PITCH climbs with it. The volume ramp alone made a long rally louder without
                // making it more urgent, and "escalate through audio pitch" was the one escalation
                // channel on the goal's list with nothing behind it — the pitch was set once when the
                // voice was made and never touched again. A fifth up across a full rally is enough to
                // feel as tightening rather than to hear as a tape speeding up.
                _voiceRally.pitch = Mathf.Lerp(1f, 1.34f, energy);
                if (want > 0.01f && !_voiceRally.isPlaying) _voiceRally.Play();
            }
        }

        // ------------------------------------------------------------------ audio

        /// <summary>
        /// Three voices this phase owns, because each needs a pitch of its own and AudioDirector's
        /// one-shot sources are shared.
        ///
        /// Two sounds the brief asks for are not in the bank, and neither needs new art: a comic goose
        /// honk is the duck's annoyed quack pitched well down — both are waterfowl calls, and the
        /// register lands where the brief wants it — and the heavy low end under an impact is a second
        /// voice of the same bonk clip pitched down hard, which is the standard way to manufacture
        /// weight. Everything else routes through AudioDirector so the mix stays in one place.
        /// </summary>
        void BuildVoices()
        {
            var audio = AudioDirector.Instance;
            if (audio == null) return;

            _voiceHonk ??= MakeVoice("DefenceHonk", audio.quackAnnoyed, 0.55f, false);
            _voiceLow ??= MakeVoice("DefenceLowEnd", Pick(audio.bonks), 0.5f, false);
            _voiceRally ??= MakeVoice("DefenceRallyLayer", audio.musicRoundUrgent, 1f, true);
        }

        AudioSource MakeVoice(string name, AudioClip clip, float pitch, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = loop;
            src.playOnAwake = false;
            src.pitch = pitch;
            // 2D, matching AudioDirector: the camera moves a great deal and a spatialised mix swims.
            src.spatialBlend = 0f;
            src.volume = 0f;
            return src;
        }

        /// <summary>The honk itself — the parry input, whether or not it connects.</summary>
        void Honk(float volume = 0.7f)
        {
            AudioDirector.Instance?.PlayHorn();
            if (_voiceHonk == null || _voiceHonk.clip == null) return;
            _voiceHonk.pitch = 0.5f + (float)_rng.NextDouble() * 0.1f;
            _voiceHonk.volume = volume;
            _voiceHonk.Play();
        }

        void LowThud(float volume)
        {
            if (_voiceLow == null || _voiceLow.clip == null) return;
            _voiceLow.pitch = 0.45f + (float)_rng.NextDouble() * 0.1f;
            _voiceLow.volume = volume;
            _voiceLow.Play();
        }

        /// <summary>
        /// Graded by tier rather than fired wholesale, so the player can HEAR which tier they got
        /// without reading anything. That is also the cheapest answer to "do not confuse juice with
        /// noise": the loudest layers only exist on the hits that earned them.
        /// </summary>
        void PlayStrikeAudio(Tier tier)
        {
            var audio = AudioDirector.Instance;
            if (audio == null) return;

            audio.PlayOne(Pick(audio.bonks), tier == Tier.Perfect ? 0.85f : 0.6f);
            audio.PlayOne(Pick(audio.suspensionBumps), 0.4f);
            if (tier == Tier.Normal) return;

            audio.PlayOne(Pick(audio.debrisPings), 0.45f);
            LowThud(tier == Tier.Perfect ? 0.9f : 0.6f);
            if (tier != Tier.Perfect) return;

            audio.PlayOne(audio.quackProud, 0.7f);
        }

        /// <summary>
        /// The rival reacting — the other half of "a distinct character reaction from BOTH competitors".
        ///
        /// They stood in their own garden for this entire phase and never moved: not when a goose came
        /// down in their flowers, not when the player put one there on purpose. Eighth instance of the
        /// same fault in this phase, and the most conspicuous, because the rival is the thing every throw
        /// is aimed AT.
        ///
        /// A lean and a hop rather than an animation: the NPC is a greybox capsule and giving it a rig
        /// would be building an asset to solve a timing problem. Sign tells which way it read the event —
        /// recoiling backward when it is their beds, drawing up when it is yours.
        /// </summary>
        void ReactOpponent(float amount, bool inTheirFavour)
        {
            var npc = _arena != null ? _arena.opponentNpc : null;
            if (npc == null) return;
            _npcKick = Mathf.Clamp(amount * (inTheirFavour ? 1f : -1f), -1.2f, 1.2f);
            _npcRest = _npcRest == default ? npc.localRotation : _npcRest;
        }

        /// <summary>Runs the rival's lean down, on real time so a hit stop holds the pose.</summary>
        void StepOpponent(float real)
        {
            var npc = _arena != null ? _arena.opponentNpc : null;
            if (npc == null || Mathf.Abs(_npcKick) < 0.001f) return;

            if (_npcRest == default) _npcRest = npc.localRotation;
            _npcKick = Mathf.MoveTowards(_npcKick, 0f, real * 2.2f);

            // Tipped back on bad news, drawn upright and forward on good. Both about its own lateral
            // axis, so from the player's end of the pitch it reads as body language rather than as spin.
            npc.localRotation = _npcRest * Quaternion.Euler(_npcKick * 26f, 0f, _npcKick * 9f);
        }

        void ReactCrowd(Tier tier)
        {
            float amount = tier == Tier.Perfect ? 0.9f : tier == Tier.Good ? 0.55f : 0.3f;
            AudioDirector.Instance?.CrowdCheer(amount);
            _crowd?.Excite(amount);
            PunchJudges(amount);
        }

        /// <summary>The bench reacts. Already built for scoring a mark; a rally is the same gesture.</summary>
        void PunchJudges(float amount)
        {
            // Asked of the director first and FOUND IN THE SCENE otherwise — the seventh time this
            // exact fault has turned up in this phase. The arena has no GameDirector, so this returned
            // immediately and the bench never reacted to anything: three judges sitting in frame,
            // visibly present, watching a rally without moving. Cached because it is hit on every
            // parry and every crash.
            var panel = GameDirector.Instance != null ? GameDirector.Instance.judges : null;
            if (panel == null) panel = _judgePanel ??= FindFirstObjectByType<JudgePanel>();
            if (panel == null || panel.judges == null || panel.judges.Length == 0) return;

            // One judge, not all three. Three animals applauding in unison on every hit is a chorus
            // line; one reacting is a crowd of individuals.
            panel.judges[_rng.Next(panel.judges.Length)]?.character?.Punch(amount);
        }

        AudioClip Pick(AudioClip[] clips)
            => clips == null || clips.Length == 0 ? null : clips[_rng.Next(clips.Length)];

        // ------------------------------------------------------------------ opponent

        /// <summary>
        /// Whose garden is across the way: whoever leads the championship.
        ///
        /// Points first, because that is the contestant who can still beat the player, with live
        /// coverage as the tiebreak — which is what decides it in round one, when nobody has points
        /// and "the leader" can only mean whoever is ahead on this lawn right now.
        /// </summary>
        RivalContestant PickOpponent()
        {
            var t = Tournament.Instance;
            if (t == null || t.rivals == null) return null;

            var table = t.Championship != null ? t.Championship.Table : null;
            RivalContestant best = null;
            int bestPoints = int.MinValue;
            float bestCoverage = -1f;

            foreach (var r in t.rivals)
            {
                if (r == null) continue;
                int points = 0;
                if (table != null)
                    foreach (var e in table)
                        if (string.Equals(e.name, r.displayName)) { points = e.points; break; }

                if (points < bestPoints) continue;
                if (points == bestPoints && r.Coverage <= bestCoverage) continue;
                best = r; bestPoints = points; bestCoverage = r.Coverage;
            }
            return best;
        }

        Material Flat(Color c)
        {
            var sh = Shader.Find("Duck/Prop")
                  ?? Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Sprites/Default");
            if (sh == null) return null;

            var m = new Material(sh) { name = "DefenceGreybox", hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            // Unity's primitives carry no vertex colours and the workhorse shader multiplies by them,
            // which comes out black rather than coloured.
            if (m.HasProperty("_VertexColorAmount")) m.SetFloat("_VertexColorAmount", 0f);
            return m;
        }

        // ------------------------------------------------------------------ readout

        /// <summary>
        /// The review numbers, in the crudest form that works. IMGUI rather than the HUD because this
        /// is a measuring instrument, not presentation, and it leaves with the greybox. A real rally
        /// counter on screen is on the list for the presentation pass.
        /// </summary>
        void OnGUI()
        {
            if (!showReadout || State == Phase.Idle || State == Phase.Done) return;

            if (_readout == null)
            {
                _readout = new GUIStyle(GUI.skin.label) { fontSize = 16 };
                _readout.normal.textColor = Color.white;
            }

            // Budget REMAINING, labelled as a budget. It used to be the phase's whole clock and it is now
            // only the ceiling, so the readout says which it is — a reviewer watching this number tick down
            // needs to know that reaching zero is a diagnostic, not the design.
            float budgetLeft = State == Phase.Live ? Mathf.Max(0f, raidBudgetSeconds - _stateTime) : 0f;
            Tier now = Judge();
            float dist = GooseComingAtMe && _mower != null
                ? Vector3.Distance(_goosePos, _mower.transform.position) : -1f;
            float v = CurrentDiveSpeed();

            GUI.Box(new Rect(12f, 12f, 430f, 290f), GUIContent.none);
            Line(0, $"RAID (greybox)   {State}   budget {budgetLeft:0.0} s left");
            Line(1, $"WASD drive · E honk    goose {_gooseState} " +
                    $"→ {(_diveAtPlayer ? "AT ME" : "AT THEM")}   exchange {_exchanges}");
            Line(2, $"rally x{Rally}   dive {v:0.0} m/s   heat {MatchHeat:0.00}   " +
                    (_recovery > 0f ? "RECOVERING" : now != Tier.Miss ? now.ToString().ToUpper() : "—") +
                    (_armedRemaining > 0 ? $"   [armed {_armedTier} x{_armedRemaining}]" : ""));
            Line(3, $"range {(dist >= 0f ? dist.ToString("0.0") + " m" : "—")}   " +
                    $"P<{perfectRadius:0.0} G<{goodRadius:0.0} N<{parryRadius:0.0}   " +
                    $"their keeper {(_keeperAttemptsLeft > 0 ? "has a go left" : "spent")}");
            // The windows are not settings, they are 2r/v — so print them, because they are the thing
            // being judged and they change every exchange.
            Line(4, $"windows  P {2f * perfectRadius / v:0.00}s  G {2f * goodRadius / v:0.00}s  " +
                    $"N {2f * parryRadius / v:0.00}s");
            Line(5, $"parries {_outcome.parries} ({_outcome.perfects}P {_outcome.goods}G " +
                    $"{_outcome.normals}N)   best x{_outcome.longestRally}");
            // THE MATCH, and the two lines the phase is actually played on. Beds TO SPARE rather than beds
            // lost: what the player needs is how close each garden is to falling, and "2 left" is a number
            // that means something where "3/17 flat" needs arithmetic doing on it first.
            Line(6, $"MY GARDEN   {MyBedsToSpare} of {_myCap} beds to spare" +
                    $"   ({(_arena != null ? _arena.PlayerBedsLost : 0)}/{_outcome.myBedsTotal} flat)");
            Line(7, $"{_outcome.opponent}   {TheirBedsToSpare} of {_theirCap} to spare" +
                    $"   ({(_arena != null ? _arena.OpponentBedsLost : 0)}/{_outcome.theirBedsTotal} flat)");
            Line(8, $"crashes {_outcome.crashes}   wasted honks {_outcome.whiffs}   " +
                    $"past keeper {_outcome.hitsOnOpponent}   they saved {_outcome.keeperSaves}   " +
                    $"they conceded {_outcome.rivalConceded}   own goals {_outcome.ownGoals}");
            Line(9, _outcome.result != MatchResult.None
                        ? $"RESULT  {DescribeResult(_outcome)}"
                        : "result  — still open —");
            Line(10, State == Phase.Awarding
                        ? $"AWARD  {Award:+0;-0;0}   (added to this round's score)"
                        : $"award so far  {ComputeAward(_outcome, awardFloor, awardCeiling):+0;-0;0}");
        }

        void Line(int row, string text)
            => GUI.Label(new Rect(22f, 18f + row * 23f, 384f, 22f), text, _readout);

        GUIStyle _readout;
    }
}
