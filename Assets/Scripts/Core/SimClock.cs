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

        public static void Reset()
        {
            Scripted = false;
            ElapsedScripted = 0f;
        }
    }
}
