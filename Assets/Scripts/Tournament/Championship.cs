using System.Collections.Generic;
using UnityEngine;

namespace DuckMow
{
    /// <summary>One contestant's running record across the championship.</summary>
    public struct ChampionshipEntry
    {
        public string name;
        public string species;
        public Color livery;
        public bool isPlayer;
        /// <summary>
        /// Cumulative round score across the championship — every mark from every round, added up.
        ///
        /// This used to be championship POINTS awarded by placing, 5/3/2/1. The rule is now the sum of
        /// the actual scores, on the player's instruction: three rounds of marks gives a total out of
        /// ninety, so how well a round went carries into the title rather than only where it placed.
        /// The field keeps its old name because the editor diagnostics print it and one rename would
        /// have been three files of churn for no gain.
        /// </summary>
        public int points;
        public int wins;
        /// <summary>
        /// Cumulative marks for the PICTURES alone, with every defence award excluded. Not redundant
        /// with <see cref="points"/> now that points is the full score: this is the tiebreak, and it is
        /// the right one — level on total, the title goes to whoever actually drew the better lawns.
        /// </summary>
        public float marks;
    }

    /// <summary>
    /// The championship arc: three rounds against the same three neighbours, every mark from every
    /// round added up, highest total out of ninety takes the title.
    ///
    /// It used to award placement points, 5/3/2/1 by where you finished each round, and the argument for
    /// that was real — placing points make winning a round matter in itself. The player has overruled it
    /// in favour of the sum, and the sum has a virtue placing does not: a single outstanding round can
    /// answer a bad one on its own merits rather than only by moving you up one rung. The defence award
    /// rides in on the same total, which is why it had to be a score rather than a fifth artistry axis.
    ///
    /// This exists because the game had no stated goal. A round ended, the board sorted itself, and
    /// the next round began from nothing — so coming first meant exactly as much as coming fourth,
    /// and there was nothing for the player to be trying to do. Points that carry between rounds
    /// are the cheapest structure that makes one round matter beyond itself, and the only one that
    /// lets the game say out loud, before the player has mown a single metre, what winning will
    /// take.
    ///
    /// That sentence is what <see cref="GoalLine"/> is for, and on the final round it is computed
    /// as a GUARANTEE rather than as encouragement: "FINISH 2ND OR BETTER" means the title cannot
    /// be lost by finishing second, however the three rivals arrange themselves behind you. Getting
    /// that wrong would be worse than saying nothing — a stated condition the game then fails to
    /// honour reads as the game cheating.
    ///
    /// A plain class rather than a MonoBehaviour, owned by <see cref="Tournament"/>. Nothing here
    /// needs a transform, and the scene is assembled by an editor script — one more component to
    /// wire is one more component that can be missing from a built scene.
    /// </summary>
    public class Championship
    {
        /// <summary>
        /// The most one round can be worth: thirty artistry marks plus the defence award's ceiling.
        ///
        /// The championship is decided on the SUM of round scores, so this is what the final-round
        /// guarantee has to reason about — the largest total any contestant can still add. Rivals never
        /// receive a defence award, so a rival's best remaining round is <see cref="RivalRoundMax"/>
        /// and only the player can reach this.
        /// </summary>
        public const int PlayerRoundMax = 38;
        /// <summary>A rival's best possible round: the artistry marks and nothing else.</summary>
        public const int RivalRoundMax = 30;

        /// <summary>Rounds in a championship. Set from <see cref="Tournament"/>.</summary>
        public int RoundsTotal { get; set; } = 3;

        public int RoundsRecorded { get; private set; }
        /// <summary>The round about to be mown, 1-based. Clamped so a finished championship reads sanely.</summary>
        public int RoundNumber => Mathf.Clamp(RoundsRecorded + 1, 1, Mathf.Max(RoundsTotal, 1));
        public bool IsComplete => RoundsRecorded >= RoundsTotal;
        public bool IsFinalRound => RoundsRecorded == RoundsTotal - 1;
        /// <summary>False until a round has actually been banked, so the card knows not to print places.</summary>
        public bool HasResults => RoundsRecorded > 0;

        public IReadOnlyList<ChampionshipEntry> Table => _entries;
        readonly List<ChampionshipEntry> _entries = new List<ChampionshipEntry>(4);

        public ChampionshipEntry Leader => _entries.Count > 0 ? _entries[0] : default;
        public bool PlayerIsChampion => IsComplete && _entries.Count > 0 && _entries[0].isPlayer;

        /// <summary>
        /// The player's place in the championship table, 1-based.
        ///
        /// Straight off the row index rather than through <see cref="Scoring.CompetitionPlaces"/>,
        /// because the sort below already breaks every tie it can be given — two contestants only
        /// share a row here if they have the same points, the same round wins and the same total
        /// marks to a thousandth, at which point there is nothing left to separate them with.
        /// </summary>
        public int PlayerPlace
        {
            get
            {
                for (int i = 0; i < _entries.Count; i++)
                    if (_entries[i].isPlayer) return i + 1;
                return 1;
            }
        }

        public int PlayerPoints
        {
            get
            {
                foreach (var e in _entries) if (e.isPlayer) return e.points;
                return 0;
            }
        }

        /// <summary>The strongest rival's points. What the player's own total has to beat.</summary>
        public int TopRivalPoints
        {
            get
            {
                int best = 0;
                foreach (var e in _entries) if (!e.isPlayer && e.points > best) best = e.points;
                return best;
            }
        }

        public string TopRivalName
        {
            get
            {
                string name = "THE FIELD";
                int best = -1;
                foreach (var e in _entries)
                    if (!e.isPlayer && e.points > best) { best = e.points; name = e.name; }
                return name;
            }
        }

        /// <summary>
        /// "ROUND 2 OF 3", or the closing heading once every round is in.
        ///
        /// DORMANT — see the block above <see cref="GoalLine"/>. Nothing calls this: the game shows no
        /// round counter anywhere. Do not wire it back into the HUD without asking.
        /// </summary>
        public string RoundLabel => IsComplete
            ? "FINAL STANDINGS"
            : $"ROUND {RoundNumber} OF {Mathf.Max(RoundsTotal, 1)}";

        // ------------------------------------------------------------------ roster

        /// <summary>
        /// Put a contestant on the table at zero. Called for all four before the first round so the
        /// briefing card can show the field the player is up against rather than four blank rows.
        /// </summary>
        public void Seed(string name, string species, Color livery, bool isPlayer)
        {
            if (string.IsNullOrEmpty(name) || IndexOf(name) >= 0) return;
            _entries.Add(new ChampionshipEntry
            {
                name = name, species = species, livery = livery, isPlayer = isPlayer
            });
        }

        /// <summary>Wipe every score and start a fresh championship. The roster is re-seeded after.</summary>
        public void Reset()
        {
            _entries.Clear();
            RoundsRecorded = 0;
        }

        int IndexOf(string name)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (string.Equals(_entries[i].name, name)) return i;
            return -1;
        }

        // ------------------------------------------------------------------ banking a round

        /// <summary>
        /// Commit one round's standings to the championship.
        ///
        /// Every contestant's round score is added to their running total — that sum is the title. Round
        /// wins are still counted, because the competition ranking is still what decides them and a win
        /// is still the tiebreak the ceremony can talk about, but they no longer award anything.
        ///
        /// Places still come from <see cref="Scoring.CompetitionPlaces"/> so a tie is read the same way
        /// here as on the board — 1st / 2nd / 2nd / 4th, never two different numbers for one contestant.
        /// </summary>
        public void Record(IReadOnlyList<Standing> roundStandings)
        {
            if (roundStandings == null || roundStandings.Count == 0) return;

            var places = Scoring.CompetitionPlaces(roundStandings);
            for (int i = 0; i < roundStandings.Count; i++)
            {
                var s = roundStandings[i];
                int idx = IndexOf(s.name);
                if (idx < 0)
                {
                    Seed(s.name, s.species, s.livery, s.isPlayer);
                    idx = _entries.Count - 1;
                }

                var e = _entries[idx];
                // Rounded rather than carried as a float: every mark is already a whole number out of
                // ten (see Scoring.Mark) and so is the defence award, so this loses nothing and keeps
                // the running total something the board can print without a decimal point.
                e.points += Mathf.RoundToInt(s.RoundScore);
                e.marks += s.total;
                if (places[i] == 1) e.wins++;
                _entries[idx] = e;
            }

            RoundsRecorded++;
            Sort();
        }

        /// <summary>
        /// Rank the table. Called only once a round has been banked — never on <see cref="Seed"/>.
        /// Before the first round the table is still in roster order, player first, and printing
        /// "1ST" against whichever of four contestants on nought points happened to sort to the top
        /// would announce a leader who does not exist.
        /// </summary>
        void Sort()
        {
            _entries.Sort((a, b) =>
            {
                int byPoints = b.points.CompareTo(a.points);
                if (byPoints != 0) return byPoints;
                // Level on points goes to whoever won more rounds, then to whoever the judges liked
                // more across the whole championship. Both are things the player did on the lawn;
                // falling through to alphabetical order decides a title on somebody's name.
                int byWins = b.wins.CompareTo(a.wins);
                if (byWins != 0) return byWins;
                int byMarks = b.marks.CompareTo(a.marks);
                if (byMarks != 0) return byMarks;
                return string.CompareOrdinal(a.name, b.name);
            });
        }

        // ------------------------------------------------------------------ the stated goal
        //
        // This copy is read exactly once per round, by the briefing banner, and it leaves with that
        // banner. That placement is deliberate and was arrived at the hard way: the same lines first
        // lived on a permanent panel in the top-left corner alongside the points table and a
        // "ROUND 2 OF 3" heading, and the whole card was cut after a playthrough — it read as chrome
        // rather than as a goal, and the player looking at it still had to ask what the format was.
        //
        // So: no counter, no standing table, nothing persistent. One line, at the moment the player is
        // deciding how to mow, then gone.

        /// <summary>
        /// What the player has to do, in one line, before they mow.
        ///
        /// Three different sentences for three different situations, and the last round's is the one
        /// that matters: it names the exact placing that cannot lose. See
        /// <see cref="FinalRoundRequirement"/> for why it is stated as a guarantee.
        /// </summary>
        public string GoalLine()
        {
            if (_entries.Count == 0) return "";
            // Nothing left to ask for, so ask nothing. This used to name the champion here, which made
            // sense while the caller was a standing card that stayed on screen after the last round;
            // the caller is now the briefing plate for the round about to be mown, and announcing a
            // champion over a picture the player is about to draw would be nonsense. The ceremony card
            // is what reports the result.
            if (IsComplete) return "";
            if (!HasResults)
                return $"HIGHEST TOTAL AFTER {Mathf.Max(RoundsTotal, 1)} ROUNDS WINS";
            return IsFinalRound ? FinalRoundRequirement() : StandingLine();
        }

        /// <summary>
        /// The scoring rule, shown under the goal on the first round only.
        ///
        /// Spelled out rather than given as "5 · 3 · 2 · 1", which is only legible to somebody who
        /// already knows what it means — and the one round this line appears on is the round where
        /// nobody does.
        /// </summary>
        public static string PointsRule()
            => $"EVERY MARK COUNTS — {RivalRoundMax} A ROUND, PLUS WHAT THE GEESE COST YOU";

        /// <summary>
        /// The exact placing that guarantees the title, worked out rather than guessed at.
        ///
        /// The player's total after finishing in place p is fixed. The best any rival can manage is
        /// their current points plus the best place still going — and the rival who can reach the
        /// highest total is always the one already on the most points, taking the best remaining
        /// place, so the worst case is a single comparison rather than a search over who finishes
        /// where. Guarantees use a strict greater-than: level on points falls through to round wins
        /// and total marks, and this line has no business promising a title on a tiebreak it cannot
        /// see the outcome of yet.
        ///
        /// Requirements get easier as the placing gets worse, so walking down from last place and
        /// returning the first one that holds gives the most generous true statement.
        /// </summary>
        string FinalRoundRequirement()
        {
            int mine = PlayerPoints;
            int theirs = TopRivalPoints;
            string rival = TopRivalName;

            // Under the summed rule the requirement is a SCORE rather than a placing, which is both
            // easier to state and easier to act on — the player can look at a number and know what kind
            // of round it asks for. The worst case is the strongest rival taking a perfect round, and it
            // is still a single comparison rather than a search, because the rival who can reach the
            // highest total is always the one already on the most.
            //
            // Strictly greater, as before: level on total falls through to round wins and then to
            // artistry marks, and this line has no business promising a title on a tiebreak whose
            // outcome it cannot see yet.
            int needed = theirs + RivalRoundMax - mine + 1;

            if (needed <= 0) return "THE TITLE IS ALREADY YOURS";
            if (needed <= PlayerRoundMax)
                return $"SCORE {needed} THIS ROUND TO TAKE THE TITLE";

            // Nothing the player can do alone is enough. Say what the title still needs from the rival,
            // which is a real instruction — it tells the player to watch the other lawns rather than to
            // assume the round is already lost. Expressed as the most that rival can be allowed to
            // score, which is the same shape of statement as the line above.
            int allowance = mine + PlayerRoundMax - theirs - 1;
            if (allowance >= 0)
                return $"SCORE EVERYTHING, AND {rival} MUST STAY UNDER {allowance + 1}";

            // A perfect round cannot even draw level. Saying so is the honest reading, and unlike the
            // old placing rule there is no tiebreak left to hope for once the arithmetic is this far
            // apart.
            return $"{rival} CANNOT BE CAUGHT — SCORE AS HIGH AS YOU CAN";
        }

        /// <summary>Where the player stands, for the rounds between the first and the last.</summary>
        string StandingLine()
        {
            int mine = PlayerPoints;
            int theirs = TopRivalPoints;
            if (PlayerPlace == 1)
                return mine > theirs
                    ? $"YOU LEAD BY {Points(mine - theirs)}"
                    : $"LEVEL ON POINTS WITH {TopRivalName}";
            return $"{Ordinal(PlayerPlace)} — {Points(theirs - mine)} BEHIND {TopRivalName}";
        }

        static string Points(int n) => n == 1 ? "1 POINT" : $"{n} POINTS";

        // ---- hooks used by the editor capture tools, so the ceremony can be photographed ----
        //
        // The victory sequence is the hardest thing in the game to reach: it needs three rounds and
        // for the autopilot to actually win one. Reviewing it would otherwise mean playing for the
        // outcome and hoping, which is not a review process. These put the table into a stated
        // position so the last round can be mown into a known result.

        public void DebugSetPoints(string name, int points, int wins = 0)
        {
            int idx = IndexOf(name);
            if (idx < 0) return;
            var e = _entries[idx];
            e.points = Mathf.Max(0, points);
            e.wins = Mathf.Max(0, wins);
            _entries[idx] = e;
            Sort();
        }

        public void DebugSetRoundsRecorded(int rounds)
            => RoundsRecorded = Mathf.Clamp(rounds, 0, Mathf.Max(RoundsTotal, 1));

        /// <summary>Placing as caps, matching everything else printed on this game's UI.</summary>
        public static string Ordinal(int n) => n switch
        {
            1 => "1ST",
            2 => "2ND",
            3 => "3RD",
            _ => $"{n}TH"
        };
    }
}
