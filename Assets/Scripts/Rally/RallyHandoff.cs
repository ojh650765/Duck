using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// What crosses between the mowing scene and the rally scene, and back out into judging.
    ///
    /// Static, like <see cref="DefenceHandoff"/> and for the same reason: a scene change destroys
    /// everything else, and a DontDestroyOnLoad carrier is a component that exists in two scenes at
    /// once — which is how you end up with two of them after a retry.
    ///
    /// It is narrow on purpose. The rally does not need the cut mask, the chalk, the picture or the
    /// judges; it needs to know whose garden is whose, and the round afterwards needs to know what
    /// state four gardens ended in. Anything wider and the rally would stop being a level that can be
    /// opened and played on its own, which is the property that makes every feel adjustment cheap to
    /// check.
    /// </summary>
    public static class RallyHandoff
    {
        public struct Result
        {
            public int slot;
            public string contestant;
            public bool isPlayer;
            /// <summary>1 for untouched, 0 for ruined. What the judging reads.</summary>
            public float integrity;
            public int bedsLost, bedsTotal;
            public int parries, perfects, knockouts;
            /// <summary>Geese this competitor sent into somebody else's flowers, plus knockouts.</summary>
            public int landed;
            public int conceded;

            /// <summary>Beds saved as a fraction, the headline number the reveal shows.</summary>
            public int Percent => Mathf.RoundToInt(integrity * 100f);
        }

        /// <summary>True while the round is out at the rally.</summary>
        public static bool Active;
        /// <summary>Which round this is, so the arena can say so.</summary>
        public static int RoundNumber;
        /// <summary>The picture's marks so far. Shown on the arena board; nothing else reads it.</summary>
        public static float PictureScore;
        public static ShapeId Shape;

        /// <summary>Set once the match has ended and the results below are real rather than left over.</summary>
        public static bool ResultsReady;
        public static Result[] Results = System.Array.Empty<Result>();

        public static void Clear()
        {
            Active = false;
            RoundNumber = 0;
            PictureScore = 0f;
            ResultsReady = false;
            Results = System.Array.Empty<Result>();
        }

        /// <summary>Called by the round on its way out to the arena.</summary>
        public static void Send(int roundNumber, float pictureScore, ShapeId shape)
        {
            Clear();
            Active = true;
            RoundNumber = roundNumber;
            PictureScore = pictureScore;
            Shape = shape;
        }

        /// <summary>Called by the arena the moment the horn's settle beat is over.</summary>
        public static void Post(Result[] results)
        {
            Results = results ?? System.Array.Empty<Result>();
            ResultsReady = true;
            Active = false;
        }

        public static Result PlayerResult
        {
            get
            {
                foreach (var r in Results) if (r.isPlayer) return r;
                return new Result { integrity = 1f, contestant = "DUCK", isPlayer = true };
            }
        }

        public static Result Of(string contestant)
        {
            foreach (var r in Results)
                if (string.Equals(r.contestant, contestant)) return r;
            return new Result { integrity = 1f, contestant = contestant };
        }

        /// <summary>Slots ordered best garden first. The ranking carried into the final judging.</summary>
        public static Result[] Ranked()
        {
            var copy = (Result[])Results.Clone();
            System.Array.Sort(copy, (a, b) =>
            {
                int byIntegrity = b.integrity.CompareTo(a.integrity);
                if (byIntegrity != 0) return byIntegrity;
                // A tie on flowers is broken by who did the most about it, which rewards the
                // competitor who was actually playing over the one nobody happened to attack.
                int byKo = b.knockouts.CompareTo(a.knockouts);
                return byKo != 0 ? byKo : b.parries.CompareTo(a.parries);
            });
            return copy;
        }

        /// <summary>
        /// Read the results once and forget them, so a retry cannot be paid for the same match twice.
        /// </summary>
        public static Result[] TakeResults()
        {
            var r = ResultsReady ? Results : System.Array.Empty<Result>();
            ResultsReady = false;
            Results = System.Array.Empty<Result>();
            return r;
        }
    }
}
