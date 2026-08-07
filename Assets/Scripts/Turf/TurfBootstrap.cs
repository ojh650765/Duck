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
    ///   * On its own — picked off the front page's class list, or pressed play with the scene
    ///     open. Nothing is waiting and nothing is loaded afterwards; it just runs, which is the
    ///     entire review loop for the mode. Every feel adjustment in Bloom Rush is checkable in
    ///     about six seconds because of this, and a mode whose central mechanic is a render texture
    ///     needs to be checked constantly.
    ///
    /// The two ways in are the same match and differ in exactly one thing, which is where the
    /// player goes when it is over. See <see cref="Update"/>: SPACE CARRIES ON, to the round when
    /// there is a round and to the front page when there is not.
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
        [Tooltip("Extra seconds held on the finished arena before the round is told it can move on.\n\n" +
                 "A confirm cuts this short — see the exit prompt below. It is a floor on how long " +
                 "the finished board is left standing, not a wait the player has to sit through.")]
        public float exitHold = 1.4f;

        public bool Finished { get; private set; }
        /// <summary>True when a round is waiting on this rather than it being a standalone review run.</summary>
        public bool FromRound { get; private set; }

        float _hold;

        // ------------------------------------------------------------------ the way out

        /// <summary>
        /// What the prompt says when a round is waiting on the other side of this stage.
        ///
        /// It does not name the destination, and that is deliberate. Bloom Rush is spliced in at
        /// <c>bloomOnRound</c> and what follows it is whatever the chain has left — the judges, the
        /// lawn, another stage — so a prompt that promised "TO THE JUDGES" would be wrong on any
        /// running order but the one it was written against, and wrong silently. "CARRY ON" is true
        /// of every one of them.
        ///
        /// Bracketed key, two spaces, capitals: the shape every prompt in this game already has —
        /// see HUD's outro and verdict lines, which is where this was copied from rather than
        /// invented next to.
        /// </summary>
        public const string RoundPrompt = "[SPACE]  CARRY ON";

        /// <summary>
        /// And what it says when nothing is waiting, because the stage was picked off the front
        /// page's class list and played on its own.
        ///
        /// "BACK TO THE GATE" rather than "BACK TO MENU" because that is what the venue calls its
        /// own front door — StageLauncher's curtain says it on the way there and GameDirector's
        /// says it on the way out. The pause board's row is the one place that says MENU, and it
        /// is a MENU row rather than a sentence spoken by the game.
        /// </summary>
        public const string MenuPrompt = "[SPACE]  BACK TO THE GATE";

        /// <summary>
        /// True while the HUD should be offering the player the way out — see <see cref="TurfHud"/>,
        /// which is the only thing that reads it.
        ///
        /// Gated on the DIRECTOR being finished rather than on a timer, which in this stage means
        /// <see cref="TurfDirector.Phase.Done"/>: the overhead has counted the percentages up, the
        /// bench has raised its cards, the result card has been read off the player's own machine
        /// and the camera has swept to the board. All four of those are the payoff of the stage and
        /// none of them is skippable from here. The prompt appears when the presentation has
        /// genuinely finished and not one beat before it.
        /// </summary>
        public bool ExitPromptUp { get; private set; }

        /// <summary>Which of the two lines the prompt is. Reads correctly before the match ends too.</summary>
        public string ExitPrompt => FromRound ? RoundPrompt : MenuPrompt;

        /// <summary>
        /// Set the instant the exit is taken, so it can only be taken once.
        ///
        /// <see cref="Flow.StageLauncher.ReturnToMenu"/> is asynchronous — it closes a curtain,
        /// loads a scene under it and lifts again, all on the curtain's own coroutine — so this
        /// component is alive and ticking for the whole of the transition it just started. Without
        /// a latch, every frame of that transition is another frame in which a confirm can ask for
        /// the same trip. StageLauncher guards itself with <c>Busy</c> and would only log, but a
        /// caller that relies on the callee's guard is a caller that breaks the day the guard moves.
        /// </summary>
        bool _left;

        /// <summary>The frame this component last saw anything in the popup stack. See UpdateConfirmGate.</summary>
        int _popupFrame = -99;

        /// <summary>The confirm edge with the popup-dismissing one taken out of it.</summary>
        bool _confirm;

        /// <summary>
        /// How many frames a confirm stays swallowed after the popup stack empties. Two, for the
        /// arithmetic GameDirector.UpdateConfirmGate sets out at length.
        /// </summary>
        const int ConfirmBleedFrames = 2;

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

        /// <summary>
        /// The confirm edge, with the keypress that dismissed a popup taken out of it.
        ///
        /// A copy of GameDirector.UpdateConfirmGate, and a deliberate one rather than a shared
        /// helper, because the two live in different assemblies of responsibility and because the
        /// bug it prevents is a property of WHO IS READING the key rather than of the key itself.
        /// The frame-by-frame case is written out in full over there and is not worth repeating;
        /// the short version is that PopupStack runs at the very top of the player loop and
        /// InputReader latches a frame later, so the Space that closed the pause board arrives here
        /// looking exactly like a fresh press.
        ///
        /// This stage is one of the two places in the game where that would be worst. Standalone,
        /// the pause board is the ONLY other thing the player can open at the results — so without
        /// this, opening it and pressing ENTER on RESUME would resume the arena straight back to
        /// the front page, which is the precise opposite of what they asked for.
        ///
        /// The stack being non-empty is also, separately, a reason to do nothing at all: whatever
        /// is in front of the game owns the key while it is up. That test is in <see cref="Update"/>
        /// rather than folded in here, because a popup being open should also take the PROMPT down,
        /// and a gate that only produces a bool cannot say so.
        /// </summary>
        void UpdateConfirmGate()
        {
            var input = InputReader.Instance;
            // Fully qualified: this file has `using UnityEngine;`, which puts UnityEngine.UI in
            // scope under the same short name and would make a bare `UI.PopupStack` ambiguous.
            if (DuckMow.UI.PopupStack.Any) _popupFrame = Time.frameCount;

            _confirm = input != null && input.AnyConfirmPressed &&
                       Time.frameCount - _popupFrame > ConfirmBleedFrames;
        }

        /// <summary>
        /// The exit, and the one rule the whole game now follows at a results screen: SPACE CARRIES
        /// ON — to whatever is next when there is a next, and to the front page when there is not.
        ///
        /// The two cases are told apart by <see cref="FromRound"/> and by nothing else. It is set
        /// once in <see cref="Start"/> from <see cref="TurfHandoff.Active"/>, which is true only
        /// when a round spliced this stage in and is sitting in <c>GameState.Bloom</c> waiting on
        /// <see cref="Finished"/>. False means the player picked BLOOM RUSH off the front page's
        /// class list, or opened the scene and pressed play, and there is nothing on the other side
        /// of this arena at all.
        ///
        /// ---- from a round ----
        ///
        /// The hold still runs, and the confirm ENDS it rather than replacing it. That distinction
        /// is the whole design of this branch. <see cref="TurfStage.Run"/> is parked on
        /// <c>while (!_boot.Finished)</c> and everything that has to happen on the way out — the
        /// seam, the unload, waking Main's sleeping roots, banking the stage result — happens after
        /// it and only after it. So the exit here is the same single line it always was, reached
        /// either by waiting or by asking; there is no second way out of this scene for the round
        /// to fall out of step with.
        ///
        /// ---- on its own ----
        ///
        /// Nothing is waiting, so nothing takes the player anywhere unless they say so. This used
        /// to set <see cref="Finished"/> and log that it was staying put, which was honest about
        /// what it did and left the player stranded on a finished arena with no way off it but the
        /// pause board. Now the prompt stands and Space takes it, through
        /// <see cref="Flow.StageLauncher.ReturnToMenu"/> — which is the route the pause board's own
        /// BACK TO MENU row takes, works with or without a GameDirector, and is the only thing in
        /// the project that knows a standalone arena has to raise its own curtain on the far side.
        /// A hand-rolled LoadSceneAsync here would be a second answer to that, free to drift, and
        /// the drift would present as "leaving Bloom Rush sometimes leaves the screen black".
        ///
        /// <see cref="Finished"/> is still set on that branch, a beat before the transition starts.
        /// Nothing in a standalone run reads it — that is what standalone means — but it is this
        /// component's own statement that the arena is over, and leaving it false while the curtain
        /// comes down would make the flag mean "the match ended" on one path and "the match ended
        /// and nobody left" on the other.
        /// </summary>
        void Update()
        {
            if (director == null || Finished) return;

            // Before the early return below, so the gate keeps watching the stack through the whole
            // match. A pause board opened during the match and closed on the last frame of it would
            // otherwise be invisible to a gate that only started looking at the results.
            UpdateConfirmGate();

            if (!director.Finished) { ExitPromptUp = false; return; }

            // The director posts the standings the moment the reveal resolves, and Phase.Done is
            // reached only after the bench, the card and the sweep to the board have all had their
            // turn — so by the time this is reached the stage has finished saying everything it had
            // to say. Down again while a popup is up: the pause board is drawn over this and the key
            // belongs to it, and a prompt still advertising SPACE underneath a menu that also
            // answers to SPACE is a promise the game is about to break.
            ExitPromptUp = !_left && !DuckMow.UI.PopupStack.Any;

            bool take = _confirm && !DuckMow.UI.PopupStack.Any && !_left;

            if (!FromRound)
            {
                if (!take) return;
                _left = true;
                ExitPromptUp = false;
                Finished = true;
                Debug.Log("[Bloom] standalone run complete — back to the front page.");
                DuckMow.Flow.StageLauncher.ReturnToMenu();
                return;
            }

            // All that is left is to hold on the finished board long enough for the last petals to
            // come down — unless the player has seen enough, which is what the confirm says.
            _hold += Time.unscaledDeltaTime;
            if (_hold < exitHold && !take) return;
            _left = true;
            ExitPromptUp = false;
            Finished = true;
        }

        /// <summary>Wind the match up early, for a retry or a trip back to the menu mid-match.</summary>
        public void Abort()
        {
            director?.Abort();
            // The exit is spent as well as the match. An abort is somebody ELSE already taking the
            // player somewhere — the pause board's BACK TO MENU, or the round tearing the stage
            // down for a retry — and a latch left open here would let a confirm arriving during
            // that transition start a second one on top of it. Finished already stops Update, so
            // this is belt and braces; it is here because the day somebody makes Abort recoverable
            // is the day the belt matters.
            _left = true;
            ExitPromptUp = false;
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
