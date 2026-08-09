using System.Collections.Generic;
using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// What crosses between the mowing round and Bloom Rush, and back out again.
    ///
    /// Static, like <see cref="RallyHandoff"/> and <see cref="DefenceHandoff"/>, and for the same
    /// reason: a scene change destroys everything else, and a DontDestroyOnLoad carrier is a
    /// component that exists in two scenes at once — which is how a retry ends up with two of them.
    ///
    /// Narrow on purpose. Bloom Rush does not need the cut mask, the chalk, the picture or the
    /// judges; it needs to know which round it is decorating. Anything wider and the stage would
    /// stop being a level that can be opened and played on its own, which is the property that makes
    /// every feel adjustment in it cheap to check.
    /// </summary>
    public static class TurfHandoff
    {
        public struct Result
        {
            public int slot;
            public string contestant;
            public bool isPlayer;
            /// <summary>Share of valid playable ground held at the horn, 0..1.</summary>
            public float share;
            /// <summary>Cells held at the horn. The raw number the share was divided out of.</summary>
            public int cells;
            /// <summary>Where they finished, 1 for the winner.</summary>
            public int place;

            public int Percent => Mathf.RoundToInt(share * 1000f) / 10;
        }

        /// <summary>True while the round is out at Bloom Rush.</summary>
        public static bool Active;
        /// <summary>Which round this is, so the stage can say so on its board.</summary>
        public static int RoundNumber;

        /// <summary>Set once the match has ended and the results below are real rather than left over.</summary>
        public static bool ResultsReady;

        /// <summary>
        /// True once this stage has raised the CLOSING board — the evening's table, shown in the
        /// arena because this was the last round and there is nowhere after it but the ending.
        ///
        /// A fact reported by the stage that did it, not a rule the venue re-derives. The venue has
        /// to stay silent exactly when this board has spoken, and the tempting way to arrange that
        /// is for both sides to test Championship.IsFinalRound independently. That is one edit away
        /// from a round with NO board at all: the rally's verdict stands aside from its own board
        /// unconditionally, so if the running order ever made the rally the last round, both sides
        /// would decide the other one was handling it. Reporting what actually happened cannot get
        /// out of step with what actually happened.
        ///
        /// Cleared by <see cref="Clear"/>, which <see cref="Send"/> calls on the way into the stage.
        /// </summary>
        public static bool ClosingBoardShown;
        public static Result[] Results = System.Array.Empty<Result>();
        /// <summary>Share of the arena nobody claimed. Shown on the reveal; the honest remainder.</summary>
        public static float NeutralShare;

        public static void Clear()
        {
            Active = false;
            RoundNumber = 0;
            ResultsReady = false;
            ClosingBoardShown = false;
            NeutralShare = 0f;
            Results = System.Array.Empty<Result>();
        }

        /// <summary>Called by the round on its way out to the arena.</summary>
        public static void Send(int roundNumber)
        {
            Clear();
            Active = true;
            RoundNumber = roundNumber;
        }

        /// <summary>Called by the director the instant the standings are settled.</summary>
        public static void Post(TurfMask mask, IReadOnlyList<int> standings, int playerSlot)
        {
            if (mask == null || standings == null) { ResultsReady = true; Active = false; return; }

            var results = new Result[standings.Count];
            for (int i = 0; i < standings.Count; i++)
            {
                int slot = standings[i];
                var s = TurfArena.Get(slot);
                results[i] = new Result
                {
                    slot = slot,
                    contestant = s.contestant,
                    isPlayer = slot == playerSlot,
                    share = mask.Share(slot),
                    cells = mask.Owned[slot],
                    place = i + 1
                };
            }

            Results = results;
            NeutralShare = mask.NeutralShare;
            ResultsReady = true;
            Active = false;
        }

        public static Result PlayerResult
        {
            get
            {
                foreach (var r in Results) if (r.isPlayer) return r;
                return new Result { contestant = "DUCK", isPlayer = true, place = 4 };
            }
        }

        public static Result Of(string contestant)
        {
            foreach (var r in Results)
                if (string.Equals(r.contestant, contestant)) return r;
            return new Result { contestant = contestant, place = 4 };
        }

        /// <summary>Read the results once and forget them, so a retry cannot be paid for twice.</summary>
        public static Result[] TakeResults()
        {
            var r = ResultsReady ? Results : System.Array.Empty<Result>();
            ResultsReady = false;
            Results = System.Array.Empty<Result>();
            return r;
        }
    }
}
