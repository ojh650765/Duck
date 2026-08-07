using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Where in the whole evening the player is, and how loud the next seam should be.
    ///
    /// Deliberately NOT a second copy of anything that already crosses a boundary. The competitors,
    /// their species and their liveries are in <see cref="Venue"/>, which is static and authored and
    /// readable from every scene in the project. The points are in <see cref="Championship"/>. The
    /// garden damage, the beds lost, the turf shares and the defence award are in
    /// <see cref="RallyHandoff"/>, <see cref="TurfHandoff"/> and <see cref="DefenceHandoff"/>, each
    /// narrowed to exactly what the stage that owns it earned. Duplicating any of that here would
    /// give the game two answers to the same question and no rule about which one wins.
    ///
    /// What is left over — and it is genuinely not held anywhere else — is the JOURNEY:
    ///   * how far through the running order the player has got, which is what decides whether a
    ///     transition is a quiet leaf sweep or the full brass-and-confetti one;
    ///   * the pose the outgoing camera was in when the frame went dark, so the next shot can be
    ///     picked to continue the move rather than to contradict it;
    ///   * whether the scene about to open was arrived at through a curtain that is still down and
    ///     is waiting to be raised, which is the one fact a freshly loaded scene cannot work out for
    ///     itself and the one that stops it playing its opening beat behind an opaque screen.
    ///
    /// Static, for the reason set out at length in <see cref="RallyHandoff"/>: a scene load destroys
    /// everything else, and a DontDestroyOnLoad carrier is a component that exists in two scenes at
    /// once, which is how a retry ends up with two of them. The single exception in this system is
    /// <see cref="Curtain"/>, which has to draw and therefore cannot be a static — and it carries no
    /// state that matters if it is lost.
    /// </summary>
    public static class MatchState
    {
        // ------------------------------------------------------------------ the journey

        /// <summary>
        /// How many boundaries the player has crossed since the front page.
        ///
        /// Not the round number and not the stage index: a session that plays three rounds with two
        /// stages each crosses far more seams than it plays rounds, and it is the SEAMS that are
        /// being escalated. Reset by <see cref="BeginSession"/>.
        /// </summary>
        public static int Crossings { get; private set; }

        /// <summary>Round the player is on, mirrored here so a stage scene can read it without a
        /// Tournament. Set by the round on its way out; 1 until told otherwise.</summary>
        public static int Round = 1;

        /// <summary>How many rounds the championship runs to. Mirrored for the same reason.</summary>
        public static int RoundsTotal = 3;

        /// <summary>True once the run has reached the last round. The point the pacing changes.</summary>
        public static bool FinalRound => Round >= RoundsTotal;

        /// <summary>
        /// How hard the next transition should hit, 0..1.
        ///
        /// Two terms, and they answer different questions. The championship term rises with the
        /// round, so the evening gets louder overall. The crossing term rises within a round, so the
        /// third seam of a round is bigger than its first — which is what stops round three's first
        /// transition landing softer than round one's last.
        /// </summary>
        public static float Escalation
        {
            get
            {
                float byRound = RoundsTotal > 1
                    ? Mathf.Clamp01((Round - 1f) / (RoundsTotal - 1f))
                    : 0.5f;
                float byCrossing = Mathf.Clamp01(Crossings / 9f);
                return Mathf.Clamp01(0.16f + 0.58f * byRound + 0.26f * byCrossing);
            }
        }

        /// <summary>
        /// Wipe the journey and start counting again. Called from the front page, so a second run
        /// through the game does not open on the escalation the first one ended at.
        /// </summary>
        /// <summary>
        /// Wipe the journey the moment the game starts, before any scene has run.
        ///
        /// Statics are not reset by entering play mode when domain reloading is off, and this
        /// project's whole cross-scene mechanism is statics — see RallyHandoff.Clear, which is
        /// called on the way IN for exactly this reason. Without this, pressing play a second time
        /// in the editor opens the front page holding the escalation, the round number and, worst of
        /// all, a CurtainPending flag from the previous session, which makes the first scene sit
        /// waiting to raise a curtain that is not there.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ResetForSession()
        {
            BeginSession();
            CurtainPending = false;
            PendingHeadline = null;
            PendingKicker = null;
            StageSeam.Forget();
        }

        public static void BeginSession()
        {
            Crossings = 0;
            Round = 1;
            RoundsTotal = 3;
            ClearHandover();
        }

        /// <summary>Called by whatever is about to cross a boundary, once per crossing.</summary>
        public static void Crossed() => Crossings++;

        // ------------------------------------------------------------------ the camera handover

        /// <summary>True when <see cref="HandoverPosition"/> and friends describe a real last frame.</summary>
        public static bool HasHandover { get; private set; }

        /// <summary>Where the outgoing camera was when the curtain covered it.</summary>
        public static Vector3 HandoverPosition { get; private set; }

        /// <summary>Which way it was looking. What an incoming establishing shot should agree with.</summary>
        public static Vector3 HandoverForward { get; private set; } = Vector3.forward;

        /// <summary>How fast it was travelling, in metres a second. Zero for a static shot.</summary>
        public static float HandoverSpeed { get; private set; }

        /// <summary>The lens it was on, so a cut between two very different focal lengths is known about.</summary>
        public static float HandoverFov { get; private set; } = 46f;

        /// <summary>
        /// Which way the curtain swept, in screen terms, so the incoming beat can carry the same
        /// direction. A wipe that leaves to the left wants the next shot moving left too.
        /// </summary>
        public static Vector2 HandoverSweep { get; private set; } = Vector2.left;

        public static void RecordHandover(Camera cam, float speed, Vector2 sweep)
        {
            if (cam == null) return;
            HandoverPosition = cam.transform.position;
            HandoverForward = cam.transform.forward;
            HandoverFov = cam.fieldOfView;
            HandoverSpeed = speed;
            HandoverSweep = sweep;
            HasHandover = true;
        }

        public static void ClearHandover() => HasHandover = false;

        // ------------------------------------------------------------------ the pending seam

        /// <summary>
        /// Set by whoever closed the curtain before a SINGLE-scene load, and read by the scene that
        /// comes up the other side.
        ///
        /// Additive stage loads do not need this — the coroutine that closed the curtain is still
        /// alive on the other side and opens it itself. A single load is different: the object that
        /// closed the curtain has been destroyed, so the incoming scene has to be told that the
        /// frame it is rendering into is currently opaque and that raising it is now its job.
        /// </summary>
        public static bool CurtainPending;

        /// <summary>What the sign should say when the incoming scene raises the curtain, if anything.</summary>
        public static string PendingHeadline;
        public static string PendingKicker;

        /// <summary>Take the pending flag once, so a second scene cannot also think it owns the raise.</summary>
        public static bool TakeCurtainPending()
        {
            bool p = CurtainPending;
            CurtainPending = false;
            return p;
        }

        // ------------------------------------------------------------------ dressing a seam

        /// <summary>
        /// The curtain that belongs to a given destination, at the current point in the evening.
        ///
        /// One place, so the running order's look is a decision rather than five scattered ones, and
        /// so "the finale should feel like a finale" is enforced by a rule instead of by remembering
        /// to pass a louder argument at the right call site.
        /// </summary>
        public static CurtainStyle StyleFor(Seam seam)
        {
            switch (seam)
            {
                case Seam.MenuToRound:
                    // Into the venue: the grass comes up over the frame and you are standing in it.
                    return CurtainStyle.GrassRise;
                case Seam.RoundToRally:
                    return FinalRound ? CurtainStyle.BannerWipe : CurtainStyle.LeafSweep;
                case Seam.RoundToBloom:
                    return CurtainStyle.PetalCurtain;
                case Seam.StageToRound:
                    // Coming back out of an arena and onto the finished picture. Foliage, because
                    // what is on the other side is a lawn.
                    return FinalRound ? CurtainStyle.PetalCurtain : CurtainStyle.LeafSweep;
                case Seam.IntoJudging:
                    return CurtainStyle.PetalCurtain;
                case Seam.IntoFinale:
                    return CurtainStyle.Fanfare;
                default:
                    return CurtainStyle.LeafSweep;
            }
        }

        /// <summary>How loud a given seam is, before the escalation is folded in.</summary>
        public static float IntensityFor(Seam seam)
        {
            float e = Escalation;
            switch (seam)
            {
                // The front page is the quietest thing in the game and should stay that way.
                case Seam.MenuToRound: return 0.30f;
                case Seam.StageToRound: return Mathf.Clamp01(e * 0.8f);
                case Seam.IntoJudging: return Mathf.Clamp01(0.35f + e * 0.5f);
                // The last stage entrance and the ceremony are the two moments allowed to shout.
                case Seam.IntoFinale: return 1f;
                default: return Mathf.Clamp01(0.2f + e * 0.75f);
            }
        }

        /// <summary>The boundaries the game actually has. Named, so a call site reads as a decision.</summary>
        public enum Seam
        {
            MenuToRound,
            RoundToRally,
            RoundToBloom,
            StageToRound,
            IntoJudging,
            IntoFinale,
            RoundToMenu
        }
    }
}
