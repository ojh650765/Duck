using System.Collections;
using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Every join between two stages in the game, in one place.
    ///
    /// The rule this enforces, and the reason it is a single class rather than a habit each caller
    /// is trusted to follow: a stage boundary is an OUTRO, a TRANSITION and an INTRO, and the middle
    /// one is not allowed to be visible. Before this existed there were five places that changed
    /// what was on screen — the menu's load, the rally's additive swap, Bloom Rush's, the trip back
    /// to the lawn, and Escape — and each had invented its own answer, which is why the game read as
    /// a set of modes rather than as one evening.
    ///
    /// What a seam does, in order:
    ///   1. OUTRO — the outgoing camera is pushed forward into the frame rather than abandoned, and
    ///      its pose and the direction the player was travelling are recorded.
    ///   2. TRANSITION — the curtain closes, sweeping the way the player was already moving, with
    ///      the destination's name on a board that slides through the frame. All the loading,
    ///      unloading, sleeping, re-lighting and teleporting happens under it.
    ///   3. INTRO — the curtain waits for the incoming scene to have actually RENDERED a frame, then
    ///      lifts onto its establishing shot.
    ///
    /// Step three is the one that is easy to get wrong and impossible to spot in the editor, where
    /// scenes load in a frame. A build on a cold browser cache does not, and a curtain that lifts on
    /// the frame the load returns shows the new scene's first frame with its lighting unbuilt and
    /// its camera still at the origin. So the lift is gated on frames rendered, not on the load
    /// operation reporting done.
    /// </summary>
    public static class StageSeam
    {
        /// <summary>True between <see cref="Begin"/> and <see cref="End"/>. Checked by the callers
        /// that can be entered either as part of a seam or on their own — see RallyStage.Leave.</summary>
        public static bool InProgress { get; private set; }

        /// <summary>
        /// True once somebody has started raising the curtain.
        ///
        /// There is deliberately more than one thing that can decide the raise is its job — the
        /// scene that came up from a single load, the state that landed under a stage's curtain, the
        /// stage coroutine itself — because relying on exactly one of them is how a build ends up
        /// with a route nobody covered and a player looking at leaves for ever. The cost of that
        /// belt and braces is that two of them can fire on the same frame, and two raises means two
        /// holds, two opens, and a curtain that stutters on its way up. So the first one wins and
        /// the rest fall through.
        /// </summary>
        static bool _raising;

        /// <summary>Frames the incoming scene must render before the curtain is allowed to lift.</summary>
        const int SettleFrames = 2;

        /// <summary>
        /// Close the curtain over whatever is on screen, and do not return until the frame is opaque.
        ///
        /// After this yields, the caller may load, unload, sleep, wake, re-light and teleport freely.
        /// </summary>
        public static IEnumerator Begin(MatchState.Seam seam, string headline = null,
                                        string kicker = null)
        {
            InProgress = true;
            _raising = false;
            MatchState.Crossed();

            var style = MatchState.StyleFor(seam);
            float intensity = MatchState.IntensityFor(seam);

            // ---- outro --------------------------------------------------------------------
            //
            // A push, not a cut. The lens dollies in and narrows over the last half second of the
            // outgoing shot, so the frame is ACCELERATING when the leaves arrive rather than sitting
            // still waiting to be replaced. It is the same punch a parry uses, which is deliberate:
            // the game already has a vocabulary for "this moment matters" and a transition should
            // speak it rather than invent a second one.
            var director = GameDirector.Instance;
            director?.cameraDirector?.AddPunch(Mathf.Lerp(0.7f, 1.25f, intensity));

            // The wheel comes off the player the instant the transition starts, and is NOT handed
            // back here — whatever is on the other side owns that, and every destination in the game
            // already decides it: the round's state machine sets it per state, and both arenas hold
            // it through their own count-in and release it on the horn.
            //
            // Without this the player keeps driving for the half second the curtain is closing, on a
            // lawn they can no longer see, and arrives at the arena having steered into a hedge in
            // the dark. It is the exact shape of "input active too early", running backwards.
            if (InputReader.Instance != null) InputReader.Instance.DrivingEnabled = false;

            // The stands go up on the LAST frame they are visible, not after the wipe has covered
            // them. That ordering is the whole value of it: the transition then reads as something
            // the crowd reacted to, rather than as an effect that happened to a static set. They are
            // excited harder as the championship escalates, which is the cheapest honest version of
            // "the tournament gets louder" — the same animals, reacting more.
            foreach (var crowd in Object.FindObjectsByType<SpectatorCrowd>(FindObjectsSortMode.None))
                crowd.Excite(Mathf.Lerp(0.45f, 1f, intensity));
            AudioDirector.Instance?.CrowdCheer(Mathf.Lerp(0.4f, 0.95f, intensity),
                                               applaud: intensity > 0.7f);

            var cam = Camera.main;
            Vector2 sweep = SweepDirection(cam, director);
            MatchState.RecordHandover(cam, director != null && director.mower != null
                                           ? Mathf.Abs(director.mower.ForwardSpeed) : 0f, sweep);

            // ---- transition ---------------------------------------------------------------
            var curtain = Curtain.Ensure();
            yield return curtain.Close(style, intensity, headline, kicker, sweep);
        }

        /// <summary>
        /// Lift the curtain onto the incoming scene, once that scene has genuinely drawn itself.
        ///
        /// <paramref name="extraHold"/> buys additional covered time for a caller that knows its
        /// destination needs a moment — the ceremony, mostly, where the crowd has to be in place
        /// before the frame clears.
        /// </summary>
        public static IEnumerator End(float extraHold = 0f)
        {
            if (_raising) yield break;
            _raising = true;

            var curtain = Curtain.Live;
            if (curtain == null) { InProgress = false; _raising = false; yield break; }

            // Frames, not seconds and not the load handle. See the class note.
            for (int i = 0; i < SettleFrames; i++) yield return new WaitForEndOfFrame();

            yield return curtain.Open(extraHold);
            InProgress = false;
            _raising = false;
        }

        /// <summary>
        /// Slam the curtain down with no animation, for the routes that cannot wait a beat: Escape
        /// held through a match, a retry pressed mid-stage, an editor script forcing a state.
        ///
        /// The alternative in every one of those cases is showing the seam — a half-unloaded arena,
        /// a mower being teleported, a camera swinging four hundred metres — so an instant cover is
        /// the honest edit even though it is an abrupt one. It is also the only place in this system
        /// that skips the outro, because by definition there is nothing to play out of.
        /// </summary>
        public static void CoverNow(MatchState.Seam seam)
        {
            InProgress = true;
            _raising = false;
            var curtain = Curtain.Ensure();
            curtain.CloseInstant(MatchState.StyleFor(seam), MatchState.IntensityFor(seam));
        }

        /// <summary>Give up on a seam without raising anything. For teardown, and for the menu.</summary>
        public static void Forget()
        {
            InProgress = false;
            _raising = false;
        }

        /// <summary>
        /// Which way across the screen the player was travelling, so the curtain sweeps WITH them.
        ///
        /// This is the whole of "preserve visual momentum" in one function. A wipe that comes in
        /// against the direction of travel reads as an interruption; one that comes in with it reads
        /// as the same movement continuing past the camera. Falls back to a right-to-left sweep,
        /// which is the direction the venue's own signage and bunting hang.
        /// </summary>
        static Vector2 SweepDirection(Camera cam, GameDirector director)
        {
            if (cam == null || director == null || director.mower == null) return Vector2.left;

            var mower = director.mower;
            if (Mathf.Abs(mower.ForwardSpeed) < 1.2f) return Vector2.left;

            Vector3 world = mower.transform.forward * Mathf.Sign(mower.ForwardSpeed);
            var t = cam.transform;
            var screen = new Vector2(Vector3.Dot(world, t.right), Vector3.Dot(world, t.up));
            return screen.sqrMagnitude < 0.02f ? Vector2.left : screen.normalized;
        }

        /// <summary>The small line above a stage's name on the sign. One phrasing, everywhere.</summary>
        public static string RoundKicker(int round)
        {
            int total = Mathf.Max(MatchState.RoundsTotal, 1);
            if (round >= total) return "FINAL ROUND";
            return $"ROUND {round} OF {total}";
        }
    }
}
