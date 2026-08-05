using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The whole contract between the round and the arena: three scalars and an enum.
    ///
    /// Static, and deliberately so. A scene load destroys everything else, and the alternative — a
    /// DontDestroyOnLoad carrier object — means a component that exists in two scenes at once, which is
    /// how you end up with two of them after a retry. Statics survive the load and nothing else has to
    /// know they did.
    ///
    /// The narrowness is the point. An earlier shape had the arena grow its garden FROM the player's cut
    /// mask, which meant the boundary carried a texture and the arena could not be authored — the level
    /// was whatever had been mown. The garden is authored now, so the only things that cross are what
    /// the round earned on its way in and what the bench awarded on the way out. That is why the arena
    /// can be a saved scene at all.
    ///
    /// Reset by <see cref="Clear"/> on the way in rather than on the way out, because a domain reload
    /// between the two scenes leaves whatever a previous session left here.
    /// </summary>
    public static class DefenceHandoff
    {
        /// <summary>True while a round is out at the arena. False means the arena is being played on
        /// its own, which is a legitimate thing to do — see ArenaBootstrap.</summary>
        public static bool Active;

        /// <summary>Which round this is, so the arena can say so and the award lands on the right one.</summary>
        public static int RoundNumber;

        /// <summary>The picture's marks, carried only so the arena can show the split it is adding to.</summary>
        public static float PictureScore;

        /// <summary>Which shape was mown. The arena shows it on the board, and nothing else reads it.</summary>
        public static ShapeId Shape;

        /// <summary>What the bench made of the defence. Written by the arena, read once by the round.</summary>
        public static int Award;

        /// <summary>Set when the arena has finished and the award is real rather than left over.</summary>
        public static bool AwardReady;

        public static void Clear()
        {
            Active = false;
            RoundNumber = 0;
            PictureScore = 0f;
            Award = 0;
            AwardReady = false;
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

        /// <summary>Called by the arena when the bench has marked the defence.</summary>
        public static void Return(int award)
        {
            Award = award;
            AwardReady = true;
            Active = false;
        }

        /// <summary>Read the award once and forget it, so a retry cannot be paid twice.</summary>
        public static int TakeAward()
        {
            int a = AwardReady ? Award : 0;
            Award = 0;
            AwardReady = false;
            return a;
        }
    }
}
