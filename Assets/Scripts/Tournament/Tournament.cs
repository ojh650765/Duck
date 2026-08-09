using System;
using System.Collections.Generic;
using UnityEngine;

namespace DuckMow
{
    /// <summary>One line of the final standings.</summary>
    public struct Standing
    {
        public string name;
        public string species;
        /// <summary>
        /// The picture's marks, out of 30 — and still out of 30, deliberately.
        ///
        /// The defence award is kept OUT of this and carried separately, even though the championship
        /// counts their sum. Six places print this as "N / 30" — the scoreboard row, the tour card, the
        /// results panel, the simulator's log — and folding the award in here would have made every one
        /// of them state a denominator the number can exceed. A visible "+N" belongs BESIDE those, not
        /// silently inside them.
        /// </summary>
        /// <summary>
        /// This round's marks, out of 30.
        ///
        /// The name still says "total" and the denominator is still thirty, but WHAT is being marked
        /// now depends on which round it is. A round is a STAGE — round one is the picture, round two
        /// is the goose rally, round three is Bloom Rush — and every one of them is scored onto this
        /// same thirty, by <see cref="CloseRound"/> for a lawn and by <see cref="CloseMatchRound"/>
        /// for an arena. That is the whole reason the arenas were converted onto this scale rather
        /// than carried in <see cref="defenceAward"/>: six places print this as "N / 30" and the
        /// championship adds three of them up to a table out of ninety, and a round worth a third of
        /// the others would have made the title a referendum on round one.
        /// </summary>
        public float total;
        /// <summary>
        /// The bench's verdict on the GREYBOX goose defence phase, roughly -6..+10.
        ///
        /// Only <see cref="GameDirector.defencePhaseEnabled"/> feeds this, and that phase is off by
        /// default. It is NOT how the goose rally reaches the competition any more: the rally is a
        /// round in its own right and is marked out of thirty like every other round — see
        /// <see cref="RallyMarks"/>. Kept because the defence phase still exists and still awards,
        /// and because a round score that can be added to is the honest shape for a bonus beat.
        /// </summary>
        public int defenceAward;
        /// <summary>
        /// True when this row's number is NOT out of thirty, so the board prints it bare.
        ///
        /// A display switch rather than a fact about the rally, which is what it used to be. The one
        /// caller left is Bloom Rush's own board, which prints the CUMULATIVE championship — three
        /// rounds do not fit in one round's denominator, and a board reading "71 / 30" is a board
        /// nobody trusts again. See Scoreboard.Label.
        /// </summary>
        public bool rallyMarked;
        /// <summary>The round score that actually decides anything: the round's marks plus any
        /// defence award on top of them.</summary>
        public float RoundScore => total + defenceAward;
        public string rank;        // S..D, on this round's marks
        public bool isPlayer;
        public Vector2 plotCentre;
        public Color livery;
    }

    /// <summary>
    /// Which arena a MATCH round was played in. See <see cref="Tournament.CloseMatchRound"/>.
    ///
    /// Deliberately not <see cref="GameState"/>, which is where a round IS rather than what it was:
    /// the director hands this over once the arena has already unloaded and its state has moved on,
    /// so borrowing the enum that describes the live beat would mean passing a value that is by then
    /// untrue. Two members, because there are two arenas.
    /// </summary>
    public enum MatchStage { Rally, Bloom }

    /// <summary>
    /// The championship: the player and every rival, running the same round at the same time on
    /// their own plots, judged by the same rules, ranked on one board at the end.
    ///
    /// It owns nothing about how a contestant mows — the player has a physical mower and the
    /// rivals have routes — only that they all start together, stop together, and are compared
    /// on the same numbers.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class Tournament : MonoBehaviour
    {
        public static Tournament Instance { get; private set; }

        public RivalContestant[] rivals = Array.Empty<RivalContestant>();

        [Header("Player entry")]
        public string playerName = "DUCK";
        public string playerSpecies = "duck";
        public Color playerLivery = new Color(0.78f, 0.24f, 0.20f);

        [Header("Championship")]
        [Tooltip("Rounds in a championship. Three, and it is no longer a free number: a round IS a " +
                 "stage, so three rounds is Lawn Art, Goose Rally, Bloom Rush. Raising it adds " +
                 "rounds of Lawn Art on the end and lowering it drops stages off it — see " +
                 "GameDirector.rallyOnRound and bloomOnRound, which name which round each arena is.")]
        public int roundsPerChampionship = 3;

        /// <summary>
        /// The points table the rounds add up to. Not serialized and not a component: it is created
        /// and seeded here so that a scene built before it existed still comes up with a working
        /// championship instead of a null reference.
        /// </summary>
        public Championship Championship => _championship;
        readonly Championship _championship = new Championship();

        /// <summary>
        /// True once this round's standings have been added to the championship.
        ///
        /// The guard is not paranoia. The director drives states, the capture tools force them, and
        /// a round banked twice awards its points twice — which is invisible on the round board and
        /// only shows up as a championship the player cannot account for.
        /// </summary>
        bool _banked;

        [Header("Variety")]
        [Tooltip("Rivals mow different pictures from the player. Off by default — see below.")]
        // Off, and it matters.
        //
        // This was on so the venue would not look like a photocopier, which was the right call when
        // the guide stayed on the ground all round. It is the wrong call now: the round turns on
        // everybody losing the same picture at the same moment, and the tour's payoff is watching
        // three neighbours misremember the outline you were also working from. If each rival is
        // drawing something else you cannot tell a mistake from a different brief — HORACE's lopsided
        // star just reads as HORACE's star, and the one shot that is supposed to say "they struggled
        // too" says nothing at all.
        //
        // The variety has to come from how they fail instead. See RivalContestant's memory profile.
        public bool rivalsGetOwnShapes = false;

        /// <summary>Fired for anything a neighbour does that the player might notice.</summary>
        public event Action<RivalEvent> OnRivalEvent;

        public IReadOnlyList<Standing> Standings => _standings;
        readonly List<Standing> _standings = new List<Standing>(4);

        public Standing Winner => _standings.Count > 0 ? _standings[0] : default;
        public int PlayerPlace { get; private set; } = 1;

        System.Random _rng;
        bool _running;

        void Awake()
        {
            Instance = this;
            _rng = new System.Random(Environment.TickCount ^ 0x5EED);
            foreach (var r in rivals)
            {
                if (r == null) continue;
                var captured = r;
                captured.OnEvent += e => OnRivalEvent?.Invoke(e);
            }
            ResetChampionship();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>
        /// Wipe the points and put the same four contestants back on the table at nought.
        ///
        /// Seeded in roster order — player first — because the briefing card shows this table before
        /// a single round has been mown, and the player is entitled to see who they are up against.
        /// </summary>
        public void ResetChampionship()
        {
            _championship.RoundsTotal = Mathf.Max(1, roundsPerChampionship);
            _championship.Reset();
            _championship.Seed(playerName, playerSpecies, playerLivery, true);
            foreach (var r in rivals)
            {
                if (r == null) continue;
                _championship.Seed(r.displayName, r.species, r.liveryColour, false);
            }
            _banked = false;
        }

        /// <summary>
        /// Add this round's finished standings to the championship. Called once, from the board.
        ///
        /// Deliberately NOT called from <see cref="CloseRound"/>, even though that is where the marks
        /// become final: the verdict beat still accepts input, so a round banked there could be
        /// re-mown and banked again. The board is the first beat of the round with no way out except
        /// forward, which makes it the only safe place to commit points.
        /// </summary>
        public void BankRound()
        {
            if (_banked || _standings.Count == 0) return;
            _banked = true;
            _championship.Record(_standings);
        }

        /// <summary>
        /// Everyone starts at the klaxon, on the same picture, under the same rule.
        ///
        /// <paramref name="guideLostFraction"/> is how far into the round the chalk finishes
        /// dissolving. It is handed down from the director rather than configured here so there is
        /// exactly one schedule in the game: change the player's fade and every rival's recall
        /// moves with it, which is the only way the tour can honestly claim they all had the same
        /// amount of time to look.
        /// </summary>
        public void BeginRound(ShapeId playerShape, float guideLostFraction = 0.24f)
        {
            _standings.Clear();
            _running = true;
            _banked = false;

            var all = TargetShapes.All;
            foreach (var r in rivals)
            {
                if (r == null) continue;
                ShapeId s = playerShape;
                if (rivalsGetOwnShapes)
                {
                    int guard = 0;
                    do { s = all[_rng.Next(all.Length)]; } while (s == playerShape && ++guard < 12);
                }
                r.guideLostFraction = Mathf.Clamp01(guideLostFraction);
                r.Begin(s);
            }
        }

        /// <summary>
        /// Step every rival. <paramref name="progress01"/> is the round's own progress, so the
        /// whole venue finishes together however long the player's picture happened to grant.
        /// </summary>
        public void Tick(float dt, float progress01, float guideVisibility = 0f)
        {
            if (!_running) return;
            foreach (var r in rivals)
            {
                if (r == null) continue;
                r.Tick(dt, progress01, guideVisibility);
            }
        }

        /// <summary>Flush every rival's mask. Called before anything is going to look at a lawn.</summary>
        public void FlushMasks()
        {
            foreach (var r in rivals) r?.FlushMask();
        }

        /// <summary>
        /// Klaxon for a LAWN round. Everyone downs tools, every plot is marked, and the board is
        /// built. The player's score arrives already computed by their own bench, so the two paths
        /// meet here and nowhere else.
        ///
        /// Only round one takes this route now. A round is a stage, and the other two stages are
        /// matches with no lawn in them at all — see <see cref="CloseMatchRound"/>, which is the
        /// sibling that closes those. Calling this one for a match round would re-read three rivals'
        /// finished pictures and bank round one's artwork a second time.
        /// </summary>
        /// <param name="defenceAward">
        /// What the bench made of the goose defence, added straight onto the player's round score.
        /// Zero when the phase did not run, which keeps a round without it scoring exactly as it did.
        /// Rivals never receive one — they do not face the geese, so the defence is the player's own
        /// opportunity and their own risk.
        /// </param>
        public void CloseRound(float playerArtistry, string playerRank, Vector2 playerPlot,
                               int defenceAward = 0)
        {
            _running = false;

            foreach (var r in rivals) r?.Finish();

            _standings.Clear();
            _standings.Add(new Standing
            {
                name = playerName,
                species = playerSpecies,
                total = playerArtistry,
                defenceAward = defenceAward,
                rank = playerRank,
                isPlayer = true,
                plotCentre = playerPlot,
                livery = playerLivery
            });

            foreach (var r in rivals)
            {
                if (r == null) continue;
                _standings.Add(new Standing
                {
                    name = r.displayName,
                    species = r.species,
                    total = r.Total,
                    defenceAward = 0,
                    rank = r.Rank,
                    isPlayer = false,
                    plotCentre = r.plotCentre,
                    livery = r.liveryColour
                });
            }

            RankStandings();
        }

        /// <summary>
        /// Klaxon for a MATCH round: the goose rally, or Bloom Rush, played as a round in its own
        /// right rather than as a beat inside somebody else's.
        ///
        /// ---- why this is a separate verb from <see cref="CloseRound"/> ----
        ///
        /// A lawn round is closed by reading four lawns: the player's marks arrive from their own
        /// bench and each rival's come off <c>RivalContestant.Total</c>. There is no lawn in a match
        /// round. The rivals did not mow, were never begun and were never ticked, so asking them for
        /// a total would hand back whatever their last PICTURE scored — and the championship would
        /// bank round one's artwork three times over.
        ///
        /// So a match round is closed off the arena's own handoff instead, which is the only thing
        /// that saw it. Every contestant, not just the player: all four defend a garden and all four
        /// paint the plaza, and a round in which only the duck was marked would be a round nobody
        /// could lose.
        ///
        /// Opens and closes in one call, deliberately. <see cref="BeginRound"/> exists because a lawn
        /// round has seventy-five seconds of rivals to step; a match round has nothing here to step,
        /// because the arena is a loaded scene running its own clock. The one thing that beginning
        /// did which still matters is clearing <see cref="_banked"/>, so this does that itself and
        /// stays safe to call twice — which it is, because both the arena's own board and the
        /// director ask for it (see RallyVerdict.BeginBoard).
        /// </summary>
        public void CloseMatchRound(MatchStage stage, Vector2 playerPlot)
        {
            _running = false;
            _banked = false;
            _standings.Clear();

            AddMatchStanding(stage, playerName, playerSpecies, playerLivery, true, playerPlot);
            foreach (var r in rivals)
            {
                if (r == null) continue;
                AddMatchStanding(stage, r.displayName, r.species, r.liveryColour, false, r.plotCentre);
            }

            RankStandings();
        }

        void AddMatchStanding(MatchStage stage, string name, string species, Color livery,
                              bool isPlayer, Vector2 plot)
        {
            float marks = MatchMarks(stage, name);
            _standings.Add(new Standing
            {
                name = name,
                species = species,
                total = marks,
                defenceAward = 0,
                // False, so the board prints "N / 30" — which is the truth about a match round now
                // that it is marked on the same thirty a picture is. See Standing.rallyMarked.
                rallyMarked = false,
                rank = Scoring.Rank(marks),
                isPlayer = isPlayer,
                plotCentre = plot,
                livery = livery
            });
        }

        /// <summary>
        /// Sort the round's standings and work out everybody's place.
        ///
        /// Highest round score wins; a tie breaks alphabetically, so the order is stable and never
        /// depends on which contestant happened to be listed first.
        ///
        /// Competition ranking, so a tie reads 1st / 2nd / 2nd / 4th — and so the outro card and the
        /// board standing next to it can never print two different numbers for the same contestant.
        /// Taking the place from the row index would have done exactly that.
        ///
        /// One method rather than a copy at the end of every close, which is what it was: there were
        /// three of them, and the day one of them gained a tiebreak the other two did not was the day
        /// two boards started disagreeing about who came second.
        /// </summary>
        void RankStandings()
        {
            _standings.Sort((a, b) =>
            {
                // Ordered on the ROUND SCORE, so a defence award counts toward where a contestant
                // placed rather than only toward the championship total.
                int byTotal = b.RoundScore.CompareTo(a.RoundScore);
                if (byTotal != 0) return byTotal;
                return string.CompareOrdinal(a.name, b.name);
            });

            var places = Scoring.CompetitionPlaces(_standings);
            PlayerPlace = 1;
            for (int i = 0; i < _standings.Count; i++)
                if (_standings[i].isPlayer) { PlayerPlace = places[i]; break; }
        }

        // ------------------------------------------------------------------ an arena, in marks
        //
        // Everything below is "what was that match worth", and it is static so that the ARENA'S OWN
        // board and the championship cannot come up with two different answers about the same match.
        // Both arenas print a number to the player before the round is banked; both print this one.
        //
        // The two arenas measure completely different things — a garden that survived, a share of
        // ground held — and they meet at MarksFromPerformance, which is the only place that decides
        // what a match round is worth out of thirty. That single meeting point is the point: two
        // stages marked on the same denominator have to be marked on the same band, or the
        // championship is decided by which round a contestant happened to be good at rather than by
        // how well they played it.

        /// <summary>
        /// The band a MATCH round is marked inside, and the premium for winning it.
        ///
        /// ---- why these three numbers exist, and what they were measured against ----
        ///
        /// A round is a stage, so an arena is marked on the same thirty a picture is, and the only
        /// honest way to pick the band is to look at what the bench actually prints. It prints less
        /// than thirty and more than nothing: PRISCILLA's ceiling is nine, so the panel's arithmetic
        /// maximum is 29, and a competent lawn lands between 18 and 26 with a near-perfect one at 28.
        /// At the other end an UNTOUCHED lawn scores 9 — nothing mown means no spill, so neatness is
        /// a perfect 1 and the outline band matches everywhere outside the shape — and a run that
        /// tries and fails scores about 13. The lawn round is therefore a 9..28 instrument whose
        /// working range is 13..26, and four contestants on it usually finish within five or six
        /// marks of each other.
        ///
        /// So an arena is given the same shape. <see cref="MatchMarksFloor"/> is what turning up is
        /// worth, set level with the empty lawn's 9 rather than at nought, because a flattened garden
        /// is a bad round and not an absence of one. <see cref="MatchMarksSpan"/> is what performance
        /// adds on top, and floor plus span plus the winner's premium comes to exactly thirty — so a
        /// perfect arena round is reachable and has to be a maximum performance AND a win, which is
        /// the same bargain the lawn's 28 drives.
        ///
        /// <see cref="PlacingMarks"/> is deliberately the small part. Placing used to be the whole of
        /// the rally's score and that is the defect this replaces; four marks between first and last
        /// makes winning worth something in a ninety-mark championship without letting it decide one.
        /// </summary>
        public const int MatchMarksFloor = 10;
        /// <summary>What performance is worth on top of <see cref="MatchMarksFloor"/>.</summary>
        public const int MatchMarksSpan = 16;
        /// <summary>What first, second, third and last are worth on top of what they earned.</summary>
        public static readonly int[] PlacingMarks = { 4, 2, 1, 0 };

        /// <summary>
        /// One contestant's marks for a match round: what they did, plus a little for where it put them.
        ///
        /// <paramref name="performance01"/> is the arena's own measure of how well the match was
        /// played, already normalised — the rally's award re-based off its -6..+10 scale, Bloom Rush's
        /// share of the ground against an even split. Every arena reaches the championship through
        /// this one function so that two stages marked out of the same thirty cannot quietly be worth
        /// different amounts.
        ///
        /// <paramref name="place"/> below one means UNPLACED, and gets no premium. That is not
        /// defensive tidying: <see cref="TurfHandoff.Result.place"/> is a plain field that defaults to
        /// zero, and a zero silently read as "first" would have paid the winner's premium to every
        /// contestant a caller forgot to place.
        /// </summary>
        public static float MarksFromPerformance(float performance01, int place)
        {
            int merit = Mathf.RoundToInt(MatchMarksFloor +
                                         Mathf.Clamp01(performance01) * MatchMarksSpan);
            int placed = place < 1
                ? 0
                : PlacingMarks[Mathf.Clamp(place - 1, 0, PlacingMarks.Length - 1)];
            return Mathf.Clamp(merit + placed, 0, Championship.RivalRoundMax);
        }

        /// <summary>
        /// What a bench award alone is worth in marks, with no placing in it.
        ///
        /// Exists so that anything wanting to say "this is what a good rally looks like" on the
        /// round's own scale can convert a figure from the award scale instead of typing a mark and
        /// hoping. The rally HUD's card colours are the caller this is for.
        /// </summary>
        public static int MarksForAward(int award)
            => Mathf.RoundToInt(MatchMarksFloor +
                   Mathf.Clamp01(Mathf.InverseLerp(AwardFloor, AwardCeiling, award)) * MatchMarksSpan);

        /// <summary>
        /// What a garden was worth, as marks out of thirty.
        ///
        /// Built on <see cref="RallyAward"/>, because that curve is where the mode's actual scoring
        /// lives — break-even at 60 percent intact, knockouts and geese landed in somebody else's
        /// flowers paying on top. Marking integrity alone would delete the offensive half of the mode
        /// from the championship.
        ///
        /// ---- what was wrong here, since the fix is the whole point of the function ----
        ///
        /// This used to go through a RallyAwardForPlace that returned <c>max(earned, placed)</c>
        /// against a ladder of 10 / 6 / 3 / 0 — the award's own ceiling for the winner. The argument
        /// was that a bench which picks a winner must pay them the most, and it was sound while the
        /// rally was a bonus of at most ten points bolted onto a picture. As a whole round it meant
        /// WINNING THE RALLY WAS AN AUTOMATIC PERFECT THIRTY, with a fixed 22 / 17 / 11 behind it,
        /// whatever the four gardens actually looked like. A rally in which every contestant was
        /// flattened paid its winner exactly what a rally nobody was scratched in paid theirs, and no
        /// lawn round can pay thirty at all — the bench's arithmetic maximum is 29. Round two was the
        /// cheapest round in the championship and it was cheap by construction.
        ///
        /// So placing is a premium now instead of a floor: four marks for the win, and the rest of
        /// the round earned. One thing is knowingly given up for that. The bench still picks its
        /// winner on integrity (see RallyHandoff.Ranked) while the marks count the offensive half
        /// too, so a contestant who saved slightly less and fought far harder can out-score the
        /// picked winner. That is intended — a bad first place SHOULD score below a brilliant second
        /// — and the premium is what keeps it from being cheap: second has to make up the winner's
        /// defensive margin plus two whole marks, which is about a knockout and a half of clear
        /// daylight, before the table disagrees with the ceremony.
        /// </summary>
        public static float RallyMarks(RallyHandoff.Result r, int place, int of)
            => MarksFromPerformance(Mathf.InverseLerp(AwardFloor, AwardCeiling, RallyAward(r)),
                                    // Nobody to be placed against, so no premium — the same case the
                                    // old curve handled by returning the bare award.
                                    of <= 1 ? 0 : place);

        /// <summary>
        /// What a share of the plaza was worth, as marks out of thirty.
        ///
        /// Normalised against an EVEN FOUR-WAY SPLIT rather than against the whole arena: a quarter
        /// is par, and taking half the ground is a rout that saturates. That normalisation is the
        /// tuned part and is unchanged — straight <c>share * 30</c> would have made a perfectly
        /// respectable four-way scrap score seven marks out of thirty.
        ///
        /// What changed is where it lands. Bloom Rush never had the rally's placing floor and so
        /// never had its defect — a share is earned by driving, nothing is handed to the leader, and
        /// thirty already required half the arena. It had the opposite fault, which only shows up
        /// once the two arenas are compared with the lawn: shares are ZERO SUM. Four contestants
        /// divide one arena between them, so their marks used to divide sixty between them however
        /// hard they all played, and a scrap with thirty percent of the ground left neutral paid the
        /// whole field 42 marks where a lawn round pays about 85. Every Bloom Rush round quietly cost
        /// all four contestants marks that round one had given them. Through the shared band a par
        /// quarter is 18 rather than 15, the field takes about 75, and the round stops being the one
        /// nobody can score in.
        /// </summary>
        public static float BloomMarks(TurfHandoff.Result r)
            => MarksFromPerformance(Mathf.InverseLerp(0f, 0.5f, r.share), r.place);

        /// <summary>
        /// One contestant's marks for a match round, looked up by name in the arena's handoff.
        ///
        /// Zero when the arena never posted anything — a scene that failed to load, a match aborted
        /// before the horn. That branch is load bearing for the rally and not merely defensive:
        /// <see cref="RallyHandoff.Of"/> answers an unknown name with an UNTOUCHED garden, which is
        /// the right default for a lookup and completely the wrong one for a score. Without the
        /// guard, a rally that never happened would award all four contestants a perfect round.
        /// </summary>
        static float MatchMarks(MatchStage stage, string contestant)
        {
            if (stage == MatchStage.Bloom)
                return TurfHandoff.Results.Length == 0
                    ? 0f : BloomMarks(TurfHandoff.Of(contestant));

            if (RallyHandoff.Results.Length == 0) return 0f;

            // Placed first, then marked. The place is only a premium now rather than the score
            // itself — see RallyMarks — but it still has to be worked out here, because the handoff
            // stores four gardens and not the ladder they came in.
            var ranked = RallyHandoff.Ranked();
            int place = 1;
            for (int p = 0; p < ranked.Length; p++)
                if (ranked[p].contestant == contestant) { place = p + 1; break; }
            return RallyMarks(RallyHandoff.Of(contestant), place, Mathf.Max(ranked.Length, 1));
        }

        /// <summary>
        /// The ends of the award scale. Named because <see cref="RallyMarks"/> re-bases the award
        /// onto the round's thirty and has to know where the scale starts and stops — a second copy
        /// of "-6" and "10" in that expression would silently mis-scale the whole mode the first
        /// time either bound was retuned here.
        /// </summary>
        public const int AwardFloor = -6;
        public const int AwardCeiling = 10;

        /// <summary>
        /// What a garden is worth, on the rally's own scale.
        ///
        /// Built around a break-even at 60 percent intact, so an average rally is worth about
        /// nothing and the mark is genuinely earned in both directions. The offensive half is
        /// deliberately the smaller one: knockouts and geese landed in somebody else's flowers are a
        /// bonus on top of defending, not a way to win the round while your own beds are flattened.
        ///
        /// This is a MERIT CURVE and not a round score. It was both, back when the rally was a beat
        /// spliced into a mowing round and the bounds were chosen to sit "well inside the picture's
        /// thirty marks" so a third of a round could not overturn one that was mown properly. The
        /// rally is a whole round now, so that argument no longer applies to it — but the curve's
        /// SHAPE was always the good part, and it is kept exactly as it was and converted to marks
        /// by <see cref="RallyMarks"/>. Reaching the ceiling takes an untouched garden AND about
        /// three geese put down, which is what makes a perfect round two something a contestant does
        /// rather than something the ladder hands them.
        /// </summary>
        public static int RallyAward(RallyHandoff.Result r)
        {
            float defence = (r.integrity - 0.6f) * 14f;
            float offence = r.knockouts * 1.2f + r.landed * 0.45f + r.perfects * 0.25f;
            return Mathf.Clamp(Mathf.RoundToInt(defence + offence), AwardFloor, AwardCeiling);
        }

        /// <summary>Find the rival working a given plot, for the tour.</summary>
        public RivalContestant RivalAt(Vector2 plotCentre)
        {
            foreach (var r in rivals)
                if (r != null && (r.plotCentre - plotCentre).sqrMagnitude < 1f) return r;
            return null;
        }
    }
}
