using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Runs GooseRally.unity. What makes it a level you can open and play rather than a set the
    /// round teleports into.
    ///
    /// Two ways in, and it does not care which:
    ///   * From a round — GameDirector has loaded this scene alongside Main and is waiting on
    ///     <see cref="Finished"/>. The results go back through <see cref="RallyHandoff"/>.
    ///   * On its own — picked off the front page's class list, or pressed play with the scene
    ///     open. Nothing is waiting and nothing is loaded afterwards; it just runs, which is the
    ///     entire review loop for the mode. Every feel adjustment in the rally is checkable in
    ///     about four seconds because of this.
    ///
    /// The two ways in are the same match and differ in exactly one thing, which is where the
    /// player goes when it is over. See <see cref="Update"/>: SPACE CARRIES ON, to the round when
    /// there is a round and to the front page when there is not.
    ///
    /// It creates an <see cref="InputReader"/> only if there isn't one, which is the standalone case.
    /// Entering from a round, Main's is still alive and still the singleton — so the player drives
    /// the rally with the identical input pipeline they drove the round with, smoothing and all.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public class RallyBootstrap : MonoBehaviour
    {
        [Tooltip("The match. Found in this scene if left empty.")]
        public RallyDirector director;
        [Tooltip("Extra seconds held on the finished arena before the round is told it can move on.\n\n" +
                 "A confirm cuts this short — see the exit prompt below. It is a floor on how long " +
                 "the bench is left standing, not a wait the player has to sit through.")]
        public float exitHold = 1.6f;
        [Tooltip("The bench that names the winner. Null is fine — the match simply ends without one.")]
        public RallyVerdict verdict;

        public bool Finished { get; private set; }
        /// <summary>True when a round is waiting on this rather than it being a standalone review run.</summary>
        public bool FromRound { get; private set; }

        float _hold;

        // ------------------------------------------------------------------ the way out

        /// <summary>
        /// What the prompt says when a round is waiting on the other side of this stage.
        ///
        /// It does not name the destination. Goose Rally is spliced in at <c>rallyOnRound</c> and
        /// what follows it is whatever the chain has left — the lawn, Bloom Rush, the reveal — so a
        /// prompt that promised any one of them would be wrong on every running order but the one
        /// it was written against, and wrong silently. "CARRY ON" is true of all of them.
        ///
        /// Bracketed key, two spaces, capitals: the shape every prompt in this game already has —
        /// see HUD's outro and verdict lines. Word for word the same as
        /// <see cref="TurfBootstrap.RoundPrompt"/>, on purpose. Two stages that end the same way
        /// have to say it the same way, or the player learns the sentence rather than the rule.
        /// </summary>
        public const string RoundPrompt = "[SPACE]  CARRY ON";

        /// <summary>
        /// And what it says when nothing is waiting, because the arena was picked off the front
        /// page's class list and played on its own.
        ///
        /// "BACK TO THE GATE" rather than "BACK TO MENU" because that is what the venue calls its
        /// own front door — StageLauncher's curtain says it on the way there and GameDirector's
        /// says it on the way out.
        /// </summary>
        public const string MenuPrompt = "[SPACE]  BACK TO THE GATE";

        /// <summary>
        /// True while the HUD should be offering the player the way out — see <see cref="RallyHud"/>,
        /// which is the only thing that reads it.
        ///
        /// Gated on the real completion signals rather than on a timer, and in this stage there are
        /// TWO of them because the ending is two things. <see cref="RallyDirector.Finished"/> is
        /// Phase.Done, which is reached after the settle beat during which the result card counts
        /// its numbers up. <see cref="RallyVerdict.Finished"/> is the bench that follows it —
        /// started from <see cref="Update"/> below, on the cut to the judges, and running through
        /// its lean-in, its raised cards, its hold and its board. Both have to be over. Gating on
        /// the director alone would put the prompt on screen the instant the horn's dust settled,
        /// underneath three animals who had not yet said who won.
        /// </summary>
        public bool ExitPromptUp { get; private set; }

        /// <summary>Which of the two lines the prompt is. Reads correctly before the match ends too.</summary>
        public string ExitPrompt => FromRound ? RoundPrompt : MenuPrompt;

        /// <summary>
        /// Set the instant the exit is taken, so it can only be taken once.
        ///
        /// <see cref="Flow.StageLauncher.ReturnToMenu"/> is asynchronous — it closes a curtain,
        /// loads under it and lifts on the far side, all on the curtain's own coroutine — so this
        /// component is alive and ticking for the whole of the transition it just started. Without
        /// a latch, every frame of that transition is another frame in which a confirm can ask for
        /// the same trip.
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

        [Tooltip("This scene's own audio bus. Shut down when a round is already running one, so the " +
                 "arena never plays a second crowd over the top of the venue's.")]
        public AudioDirector localAudio;

        void Awake()
        {
            if (director == null) director = FindFirstObjectByType<RallyDirector>();

            // Standalone only. Entering from a round, Main's reader is alive and this finds it.
            if (InputReader.Instance == null)
            {
                var go = new GameObject("~ InputReader (standalone)");
                go.transform.SetParent(transform, false);
                go.AddComponent<InputReader>();
            }

            // One audio bus, always.
            //
            // The arena carries its own so it can be opened and played on its own — without it every
            // honk, thud and cheer the match fires does nothing at all, silently. But entering from a
            // round, Main's bus is still alive behind us: two directors means two ambience beds, two
            // crowd loops and two engines mixed on top of each other, and it is also how one of them
            // ends up being ticked through a singleton before Unity has run its Awake. So the arena's
            // own stands down whenever it is the second one in the room.
            if (localAudio == null) localAudio = GetComponentInChildren<AudioDirector>(true);
            if (localAudio == null)
                foreach (var a in FindObjectsByType<AudioDirector>(FindObjectsSortMode.None))
                    if (a.gameObject.scene == gameObject.scene) { localAudio = a; break; }

            if (localAudio == null) return;
            foreach (var other in FindObjectsByType<AudioDirector>(FindObjectsSortMode.None))
            {
                if (other == localAudio) continue;
                localAudio.gameObject.SetActive(false);
                Debug.Log("[Rally] a round is already running the audio bus; the arena's own is off.");
                break;
            }
        }

        void Start()
        {
            if (director == null)
            {
                Debug.LogError("[Rally] GooseRally has no RallyDirector; nothing to run. Rebuild with " +
                               "'Duck/3 · Build goose rally scene'.");
                Finished = true;
                return;
            }

            FromRound = RallyHandoff.Active;
            Debug.Log(FromRound
                ? $"[Rally] round {RallyHandoff.RoundNumber}, picture {RallyHandoff.PictureScore:0.0}."
                : "[Rally] standalone review run — no round is waiting on this.");

            if (InputReader.Instance != null) InputReader.Instance.DrivingEnabled = true;
            director.Begin();
        }

        /// <summary>
        /// The confirm edge, with the keypress that dismissed a popup taken out of it.
        ///
        /// A copy of GameDirector.UpdateConfirmGate, and a deliberate one rather than a shared
        /// helper, because the bug it prevents is a property of WHO IS READING the key rather than
        /// of the key itself. The frame-by-frame case is written out in full over there; the short
        /// version is that PopupStack runs at the very top of the player loop and InputReader
        /// latches a frame later, so the Space that closed the pause board arrives here looking
        /// exactly like a fresh press.
        ///
        /// Standalone, the pause board is the ONLY other thing the player can open at the results
        /// — so without this, opening it and pressing ENTER on RESUME would resume the arena
        /// straight back to the front page, which is the precise opposite of what they asked for.
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
        /// The ending, and then the exit: SPACE CARRIES ON — to whatever is next when there is a
        /// next, and to the front page when there is not.
        ///
        /// The two cases are told apart by <see cref="FromRound"/> and by nothing else. It is set
        /// once in <see cref="Start"/> from <see cref="RallyHandoff.Active"/>, which is true only
        /// when a round spliced this stage in and is sitting in <c>GameState.Rally</c> waiting on
        /// <see cref="Finished"/>. False means the player picked GOOSE RALLY off the front page's
        /// class list, or opened the scene and pressed play, and there is nothing on the other side
        /// of this arena at all.
        ///
        /// ---- from a round ----
        ///
        /// The hold still runs, and the confirm ENDS it rather than replacing it. That distinction
        /// is the whole design of this branch. <see cref="RallyStage.Run"/> is parked on
        /// <c>while (!_boot.Finished)</c> and everything that has to happen on the way out — the
        /// seam, the unload, waking Main's sleeping roots, banking the stage result — happens after
        /// it and only after it. So the exit here is the same single line it always was, reached
        /// either by waiting or by asking; there is no second way out of this scene for the round
        /// to fall out of step with. Nothing before the hold is skippable: the bench below is
        /// waited on unconditionally, because three animals raising the winner's face is the payoff
        /// of the stage rather than a delay in front of it.
        ///
        /// ---- on its own ----
        ///
        /// Nothing is waiting, so nothing takes the player anywhere unless they say so. This used
        /// to set <see cref="Finished"/> and log that it was staying put, which was honest about
        /// what it did and left the player stranded on a finished arena with no way off it but the
        /// pause board. Now the prompt stands and Space takes it, through
        /// <see cref="Flow.StageLauncher.ReturnToMenu"/> — the route the pause board's own BACK TO
        /// MENU row takes, which works with or without a GameDirector and is the only thing in the
        /// project that knows a standalone arena has to raise its own curtain on the far side.
        /// </summary>
        void Update()
        {
            if (director == null || Finished) return;

            // Before the early return below, so the gate keeps watching the stack through the whole
            // match. A pause board opened during the match and closed on the last frame of it would
            // otherwise be invisible to a gate that only started looking at the results.
            UpdateConfirmGate();

            if (!director.Finished) { ExitPromptUp = false; return; }

            // The bench, once the match is over and the results are banked.
            //
            // Started here rather than by the director because it is not part of the match — the
            // match is decided the moment the horn goes, and this is the ceremony that follows.
            // Keeping the two apart means a match aborted mid-flight never has to unwind a
            // half-delivered verdict.
            if (verdict != null && verdict.State == RallyVerdict.Step.Idle)
            {
                var winner = TopContestant(out Color livery);
                verdict.Begin(winner, livery);
                if (director.cameraDirector != null)
                {
                    // Cut to the bench, the same way the round's own judging beat does — see
                    // GameDirector's Judging case on why this is a cut and not a blend.
                    director.cameraDirector.SetMode(CameraMode.Judges, 0f);
                    director.cameraDirector.SnapToCurrent();
                }
            }
            if (verdict != null && !verdict.Finished) { ExitPromptUp = false; return; }

            // Both signals are in: the director is Done and the bench has finished. Down again while
            // a popup is up — the pause board is drawn over this and the key belongs to it, and a
            // prompt still advertising SPACE underneath a menu that also answers to SPACE is a
            // promise the game is about to break.
            ExitPromptUp = !_left && !DuckMow.UI.PopupStack.Any;

            bool take = _confirm && !DuckMow.UI.PopupStack.Any && !_left;

            if (!FromRound)
            {
                if (!take) return;
                _left = true;
                ExitPromptUp = false;
                Finished = true;
                Debug.Log("[Rally] standalone run complete — back to the front page.");
                DuckMow.Flow.StageLauncher.ReturnToMenu();
                return;
            }

            // The director posts the results the moment its settle beat ends, so by the time this is
            // reached they are already banked. All that is left is to hold on the finished arena
            // long enough for the last feathers to come down — unless the player has seen enough,
            // which is what the confirm says.
            _hold += Time.unscaledDeltaTime;
            if (_hold < exitHold && !take) return;
            _left = true;
            ExitPromptUp = false;
            Finished = true;
        }

        /// <summary>Wind the match up early, for a retry or a trip back to the menu mid-rally.</summary>
        public void Abort()
        {
            director?.Abort();
            verdict?.Abort();
            // The exit is spent as well as the match. An abort is somebody ELSE already taking the
            // player somewhere — the pause board's BACK TO MENU, or the round tearing the stage
            // down for a retry — and a latch left open here would let a confirm arriving during
            // that transition start a second one on top of it.
            _left = true;
            ExitPromptUp = false;
            Finished = true;
        }

        /// <summary>
        /// Whoever ends with the most garden. Ties break on knockouts, then on parries — the
        /// contestant who did the most about it, rather than the one nobody happened to attack.
        /// </summary>
        string TopContestant(out Color livery)
        {
            RallyCompetitor best = null;
            for (int i = 0; i < RallyArena.Count; i++)
            {
                var c = director.CompetitorAt(i);
                if (c == null) continue;
                if (best == null ||
                    c.Integrity > best.Integrity ||
                    (Mathf.Approximately(c.Integrity, best.Integrity) &&
                     (c.Knockouts > best.Knockouts ||
                      (c.Knockouts == best.Knockouts && c.Parries > best.Parries))))
                    best = c;
            }
            livery = best != null ? best.Livery : Color.white;
            return best != null ? best.Name : Venue.Player.contestant;
        }
    }
}
