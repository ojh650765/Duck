using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Lets an external driver step the game instead of Unity's player loop.
    ///
    /// The whole project is built and reviewed through the editor from outside, and an unfocused
    /// Unity editor advances play mode roughly once every five seconds — which makes the round
    /// clock, the autopilot and every capture meaningless. So every system that would normally
    /// tick in Update/FixedUpdate exposes an explicit Tick, and when <see cref="Scripted"/> is
    /// set the Unity callbacks stand down and a deterministic driver runs the loop instead.
    ///
    /// The side benefit is that a full 90-second round can be simulated in about a second, which
    /// is what makes automated capture and review practical at all.
    /// </summary>
    public static class SimClock
    {
        /// <summary>True while an external driver is stepping the game.</summary>
        public static bool Scripted;

        /// <summary>Total simulated seconds since the driver took over. Diagnostics only.</summary>
        public static float ElapsedScripted;

        /// <summary>
        /// Put the clock back in Unity's hands at the start of every play session.
        ///
        /// This class was the one that missed the memo. Domain reload is off in this project, so a
        /// static is NOT cleared by entering play mode — the trap MatchState, PopupStack and Haptics
        /// all document at length and all guard with a boot reset of their own. SimClock had a Reset
        /// method that nothing in the project ever called, and no boot hook, so <see cref="Scripted"/>
        /// was simply whatever the last thing to touch it had left behind.
        ///
        /// What left it behind was the review tooling. DuckSimulator.BeginScripted sets Scripted true
        /// and hands physics over too; EndScripted puts both back — unless the harness throws, or the
        /// editor exits, or somebody stops play mode part way through a capture. Any one of those
        /// leaks a true, and from then on EVERY play session in that editor is scripted with no
        /// driver stepping it. Systems stand down and wait for a Tick that is never coming.
        ///
        /// The visible symptom was the opening cutscene freezing on its fade-in frame and never
        /// advancing, because ComicSequence.Update is `if (SimClock.Scripted) return;`. It did not
        /// look like a leaked static — it looked like a broken cutscene, in a build where nothing
        /// about the cutscene had changed. That is exactly the cost these boot resets buy off.
        ///
        /// Physics comes back with it. A leaked SimulationMode.Script is the same bug wearing
        /// different clothes: nothing moves, because nothing is stepping the simulation either.
        /// Restored here rather than left to the harness for the same reason as above — the harness
        /// is precisely what does not get to run its cleanup when this goes wrong.
        ///
        /// Runs BeforeSceneLoad, so it lands before any Awake and well before DuckSimulator takes
        /// over on purpose. A harness that wants the scripted clock still gets it; it just has to ask
        /// each session, which is the correct relationship.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            Reset();
            Physics.simulationMode = SimulationMode.FixedUpdate;
        }

        public static void Reset()
        {
            Scripted = false;
            ElapsedScripted = 0f;
        }
    }
}
