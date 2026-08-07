using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Runs BloomRush.unity. What makes it a level you can open and play rather than a set the round
    /// teleports into.
    ///
    /// Two ways in, and it does not care which:
    ///   * From a round — GameDirector has loaded this scene alongside Main and is waiting on
    ///     <see cref="Finished"/>. The standings go back through <see cref="TurfHandoff"/>.
    ///   * On its own — pressed play with the scene open. Nothing is waiting and nothing is loaded
    ///     afterwards; it just runs, which is the entire review loop for the mode. Every feel
    ///     adjustment in Bloom Rush is checkable in about six seconds because of this, and a mode
    ///     whose central mechanic is a render texture needs to be checked constantly.
    ///
    /// It creates an <see cref="InputReader"/> only if there is not one, which is the standalone
    /// case. Entering from a round, Main's is still alive and still the singleton — so the player
    /// paints with the identical input pipeline they mowed with, smoothing and all.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public class TurfBootstrap : MonoBehaviour
    {
        [Tooltip("The match. Found in this scene if left empty.")]
        public TurfDirector director;
        [Tooltip("Extra seconds held on the finished arena before the round is told it can move on.")]
        public float exitHold = 1.4f;

        public bool Finished { get; private set; }
        /// <summary>True when a round is waiting on this rather than it being a standalone review run.</summary>
        public bool FromRound { get; private set; }

        float _hold;

        void Awake()
        {
            if (director == null) director = FindFirstObjectByType<TurfDirector>();

            // Standalone only. Entering from a round, Main's reader is alive and this finds it.
            if (InputReader.Instance == null)
            {
                var go = new GameObject("~ InputReader (standalone)");
                go.transform.SetParent(transform, false);
                go.AddComponent<InputReader>();
            }
        }

        void Start()
        {
            if (director == null)
            {
                Debug.LogError("[Bloom] BloomRush has no TurfDirector; nothing to run. Rebuild with " +
                               "'Duck/4 · Build bloom rush scene'.");
                Finished = true;
                return;
            }

            if (TurfMask.Instance == null)
                Debug.LogError("[Bloom] there is no TurfMask in this scene. Nothing can be claimed " +
                               "and every percentage will read zero. Rebuild the scene.");

            FromRound = TurfHandoff.Active;
            Debug.Log(FromRound
                ? $"[Bloom] round {TurfHandoff.RoundNumber}."
                : "[Bloom] standalone review run — no round is waiting on this.");

            if (InputReader.Instance != null)
            {
                // Driving is deliberately NOT switched on here. The director holds the wheel
                // through the count-in and hands it back on the horn; enabling it at start-up would
                // undo that every time the stage is entered, which is the kind of fight between two
                // systems that presents as an intermittent bug.
                //
                // The round's own verbs are switched off for the duration. Entering from a round
                // the player keeps Main's input pipeline — that is the point, so the machine
                // handles identically here — and inherits every key bound to the round with it.
                // There is no horn to sound in this arena and no picture to check from the air, and
                // a key that silently does something in stage one and nothing in stage three is a
                // bug the player has to discover rather than a control they can learn.
                InputReader.Instance.RoundActionsEnabled = false;
            }

            // The clock comes from the round, so a session's stages all run to the same length and
            // there is one number to tune rather than three that drift apart. Falls back to the
            // director's own value in a standalone review run, where there is no round to ask.
            var round = GameDirector.Instance;
            if (round != null && round.roundDuration > 1f)
                director.matchSeconds = round.roundDuration;

            // The music, when nobody else is going to start it.
            //
            // Entering from a round, Main's AudioDirector picks the cue off the GameState.Bloom
            // transition and this must not fight it — that director is the one scoring the arena,
            // because "~ Audio" is one of the three Main roots TurfStage deliberately leaves awake.
            //
            // Standalone there is no GameDirector, no state change, and therefore no music: the
            // stage played in silence in exactly the mode it is reviewed in. This scene builds its
            // own AudioDirector with the cue already wired, so all that is missing is somebody to
            // ask for it.
            if (!FromRound)
            {
                var audio = AudioDirector.Instance;
                if (audio != null && audio.musicBloom != null) audio.PlayStageMusic(audio.musicBloom);
                else Debug.LogWarning("[Bloom] no Bloom Rush music is wired; the arena runs silent. " +
                                      "Rebuild the scene so WireAudioClips can find it.");
            }

            director.Begin();
        }

        void Update()
        {
            if (director == null || Finished) return;
            if (!director.Finished) return;

            // The director posts the standings the moment the reveal resolves, so by the time this
            // is reached they are already banked. All that is left is to hold on the overhead shot
            // long enough for the last petals to come down.
            _hold += Time.unscaledDeltaTime;
            if (_hold < exitHold) return;
            Finished = true;

            if (!FromRound)
                Debug.Log("[Bloom] standalone run complete — staying put so the arena can be inspected.");
        }

        /// <summary>Wind the match up early, for a retry or a trip back to the menu mid-match.</summary>
        public void Abort()
        {
            director?.Abort();
            Finished = true;
        }

        /// <summary>
        /// Hand the round's verbs back on the way out.
        ///
        /// In OnDestroy rather than beside the exit test, because every route out of this scene ends
        /// here and only some of them go through Abort — a retry, Escape, the stage finishing
        /// normally, and the editor leaving play mode are four different paths. Missing one leaves
        /// the player back on the lawn with the horn and the overhead check dead, which is a far
        /// worse bug than the one this was fixing.
        /// </summary>
        void OnDestroy()
        {
            if (InputReader.Instance == null) return;
            InputReader.Instance.RoundActionsEnabled = true;
            // And the wheel. The director takes driving away for the count-in and gives it back on
            // the horn — but a stage abandoned DURING the count-in never reaches the horn, so a
            // retry or an Escape in those three seconds would drop the player back onto the lawn
            // unable to move. Whoever took it away is not always the one who gets to give it back.
            InputReader.Instance.DrivingEnabled = true;
        }
    }
}
