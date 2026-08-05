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
        public float total;
        /// <summary>
        /// The bench's verdict on the goose defence, roughly -3..+8. Zero for every rival — they never
        /// face the geese, so the defence is the player's own opportunity and their own risk. Counted by
        /// the championship and by the round's placing; see <see cref="RoundScore"/>.
        /// </summary>
        public int defenceAward;
        /// <summary>The round score that actually decides anything: the picture plus the defence.</summary>
        public float RoundScore => total + defenceAward;
        public string rank;        // S..D, on the picture alone
        public bool isPlayer;
        public Vector2 plotCentre;
        public Color livery;
    }

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
        [Tooltip("Rounds in a championship. Three is the shortest arc in which a bad round can " +
                 "still be answered, and short enough that a browser session reaches the end of one.")]
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
        /// Klaxon. Everyone downs tools, every plot is marked, and the board is built.
        /// The player's score arrives already computed by their own bench, so the two paths meet
        /// here and nowhere else.
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

            // Highest total wins; ties break toward the tidier outline, then alphabetically, so the
            // order is stable and never depends on which contestant happened to be listed first.
            _standings.Sort((a, b) =>
            {
                // Ordered on the ROUND SCORE, so the defence counts toward where a contestant placed
                // rather than only toward the championship total. A round saved by the horn should be a
                // round you won.
                int byTotal = b.RoundScore.CompareTo(a.RoundScore);
                if (byTotal != 0) return byTotal;
                return string.CompareOrdinal(a.name, b.name);
            });

            // Competition ranking, so a tie reads 1st / 2nd / 2nd / 4th — and so the outro card and
            // the board standing next to it can never print two different numbers for the same
            // contestant. Taking the place from the row index would have done exactly that.
            var places = Scoring.CompetitionPlaces(_standings);
            PlayerPlace = 1;
            for (int i = 0; i < _standings.Count; i++)
                if (_standings[i].isPlayer) { PlayerPlace = places[i]; break; }
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
