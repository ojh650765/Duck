using UnityEngine;
using UnityEngine.SceneManagement;

namespace DuckMow
{
    /// <summary>
    /// Runs the arena. The thing that makes Arena.unity a level you can open and play rather than a
    /// set the round happens to teleport into.
    ///
    /// This exists because the phase used to be driven entirely by GameDirector, which lives in Main —
    /// so the arena could only ever be entered by being dragged there mid-round, and that is exactly
    /// what read as an abduction. Owning its own clock means the arena is playable on its own, which is
    /// what makes every remaining feel adjustment checkable: open the scene, press play, watch the
    /// thing you just changed.
    ///
    /// Two ways in, and it does not care which:
    ///   * From a round — DefenceHandoff.Active is set, and the award is returned before Main reloads.
    ///   * On its own — pressed play with the scene open. Nothing is returned and nothing is loaded
    ///     afterwards; it just runs, which is the whole review loop.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class ArenaBootstrap : MonoBehaviour
    {
        [Tooltip("The raid. Found in this scene if left empty.")]
        public GooseDefence defence;

        [Tooltip("Seconds held after the award lands before Main is reloaded. Only used when the arena " +
                 "was entered from a round.")]
        public float exitHold = 1.2f;

        [Tooltip("Scene to return to. Only used when the arena was entered from a round.")]
        public string returnScene = "Main";

        bool _returning;
        float _hold;
        /// <summary>
        /// Captured at Start rather than read live, because Return() clears DefenceHandoff.Active — so
        /// asking "did a round send me here?" after the award has landed gets the wrong answer, and the
        /// arena would decide whether to reload Main based on a flag it had just cleared itself.
        /// </summary>
        bool _fromRound;

        void Awake()
        {
            if (defence == null) defence = FindFirstObjectByType<GooseDefence>();
        }

        void Start()
        {
            if (defence == null)
            {
                Debug.LogError("[Duck] Arena has no GooseDefence; nothing to run. Rebuild the scene " +
                               "with 'Duck/2 · Build defence arena scene'.");
                return;
            }

            _fromRound = DefenceHandoff.Active;

            Debug.Log(_fromRound
                ? $"[Duck] arena: round {DefenceHandoff.RoundNumber}, picture {DefenceHandoff.PictureScore:0.0}."
                : "[Duck] arena: standalone review run — no round is waiting on this.");

            defence.Begin();
        }

        void Update()
        {
            if (defence == null) return;

            // The phase is stepped from here on the SCALED clock, exactly as GameDirector stepped it, so
            // its own hit stop still costs it nothing off the phase timer. Anything that reads
            // Time.deltaTime inside the phase behaves identically whichever scene is driving it — which
            // is what stops the arena from feeling like a different game with the same assets.
            if (!defence.Finished) defence.Tick(Time.deltaTime);

            if (!defence.Finished || _returning) return;

            // The award goes back the instant the phase reports itself done, and the hold is spent
            // afterwards. Returning it on a timer would lose it if the player left during the hold.
            if (_fromRound) DefenceHandoff.Return(defence.Award);

            _returning = true;
            _hold = 0f;
            Debug.Log($"[Duck] arena done: award {defence.Award:+0;-0;0}.");
        }

        void LateUpdate()
        {
            if (!_returning) return;

            // Standalone runs stop here. There is nothing to go back to, and reloading Main from a
            // review run would throw away the scene the reviewer is working in.
            if (!_fromRound) return;

            _hold += Time.unscaledDeltaTime;
            if (_hold < exitHold) return;

            _returning = false;
            SceneManager.LoadSceneAsync(returnScene);
        }
    }
}
