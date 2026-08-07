using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DuckMow.UI
{
    /// <summary>
    /// THE CARD THAT GOES UP BEFORE A STAGE DOES: the world dims, and the player is told what they
    /// are about to be asked to do and which keys do it.
    ///
    /// The problem it solves is not "the game has no tutorial". It is that this game has THREE
    /// stages with three different verb sets, played back to back inside one championship, and the
    /// player is handed each of them mid-flight with nothing on screen to say what changed. Stage
    /// one has a one-per-round look from the air. Stage two has a horn that is the entire mode.
    /// Stage three deliberately turns BOTH of those off — see <see cref="TurfBootstrap"/>, which is
    /// blunt about why: "a key that silently does something in stage one and nothing in stage three
    /// is a bug the player has to discover rather than a control they can learn". That reasoning
    /// only pays off if somebody actually tells them, and until this file existed nobody did.
    ///
    /// ---- what this file is allowed to touch ----
    ///
    /// NOTHING. This is the whole feature, in one file, wired in by nobody: no scene edit, no
    /// prefab, no serialized reference, no line added to a director. It installs its own watcher
    /// from a <see cref="RuntimeInitializeOnLoadMethod"/> and pushes itself onto the existing
    /// <see cref="PopupStack"/>, exactly as <see cref="PausePopup"/> registers itself with the
    /// stack's factory. That is not a stunt — four other agents are editing the directors and the
    /// scenes concurrently, and a feature that needs a line in <see cref="GameDirector"/> is a
    /// feature that arrives as a merge conflict. Everything below is reachable from the outside
    /// because the subsystems it reads were already written to be read from the outside.
    ///
    /// ---- what it inherits ----
    ///
    /// <see cref="PopupView"/>, and that is most of the file's correctness rather than a saving on
    /// typing. The base already owns the canvas construction, the painted-signboard palette taken
    /// as NUMBERS from the rest of the game, the scrim in hedge green rather than black, the board's
    /// ease-out-back landing, the unscaled clock, and — the part that is genuinely hard — the
    /// deafening rule that stops the key which opened a popup from also being the key that answers
    /// it. A primer written from the interface up would have had to reproduce all of that and would
    /// have got the last one wrong.
    ///
    /// The stack's contract is honoured exactly: <see cref="PausesTime"/>, <see cref="BlocksDriving"/>
    /// and <see cref="ClosesOnEscape"/> are all true, and NEITHER Time.timeScale NOR
    /// InputReader.DrivingEnabled is ever written by this file. The stack borrows both, re-reads
    /// them every frame while it holds them, and hands back what it collected. A primer that set
    /// either by hand would be fighting the thing whose entire job is to give them back.
    /// </summary>
    public sealed class ControlsPrimer : PopupView
    {
        public override string Id => "controls";
        public override bool PausesTime => true;
        public override bool BlocksDriving => true;
        public override bool ClosesOnEscape => true;

        /// <summary>
        /// A hundred under the pause board's 25000, and the hundred is deliberate.
        ///
        /// The popup range is documented in <see cref="PopupView.BuildCanvas"/>: clear of every HUD,
        /// five thousand below the curtain. Sitting exactly ON the pause board's number would be
        /// fine today, because Escape dismisses this primer rather than opening a pause menu over
        /// it, so the two are never on screen together. But two ScreenSpaceOverlay canvases with the
        /// SAME sorting order resolve against each other by an order Unity does not document, and
        /// the day somebody makes the primer refuse Escape, the pause board has to be able to land
        /// on top of it. Costing a hundred to make that unambiguous is cheaper than finding out.
        /// </summary>
        protected override int SortingOrder => 24900;

        // ==================================================================================
        // THE WATCHER
        // ==================================================================================
        //
        // Everything from here to the next banner is static, has no scene presence, and exists to
        // answer one question every frame: is a stage sitting there waiting to be played, with a
        // player who has not yet been told how to play it.

        /// <summary>
        /// The mowing round's scene, which is the ONE name here with no shared constant to consume.
        ///
        /// <see cref="RallyStage.SceneName"/> and <see cref="TurfStage.SceneName"/> are consts and
        /// are used below as consts, so those two names exist once in the project and this file
        /// cannot drift from them. Stage one has no equivalent: the name lives as
        /// <c>MainMenu.playScene</c> and again as <c>ArenaBootstrap.returnScene</c>, both of which
        /// are SERIALIZED INSTANCE FIELDS — reading either would mean finding a live component in a
        /// scene that has, by definition, only just loaded. So it is written out here, once, with
        /// this comment attached to it, which is the honest version of the problem rather than a
        /// silent third copy.
        /// </summary>
        const string LawnArtScene = "Main";

        /// <summary>
        /// Unscaled seconds of clear, curtain-free stage the player gets to look at before the card
        /// lands on top of it.
        ///
        /// Not zero, and the reason is the whole timing decision below. Every stage in this game
        /// opens on an authored establishing shot — the arena from directly overhead, the lawn from
        /// the start line — and the curtain is lifted ONTO it deliberately, gated on frames actually
        /// rendered rather than on a load handle. Dimming that on the frame it appears would throw
        /// away the shot the seam went to some trouble to deliver. Half a second is long enough to
        /// read as "here is the place, now here is how you play it" and short enough that nobody
        /// waiting to start feels held.
        /// </summary>
        const float Settle = 0.55f;

        /// <summary>
        /// How long the watcher will wait for a stage to reach the state described below before it
        /// gives up on that scene and says so.
        ///
        /// Budgeted rather than trusted, which is the rule this project applies to the opening
        /// story, the scoreboard and the ending for the same reason: a wait with no ceiling is a
        /// wait that, when the world changes underneath it, becomes a feature that silently stopped
        /// working. This clock only advances on frames the watcher is genuinely WAITING on the stage
        /// — never while the opening story is running or a popup is up, since neither of those is
        /// the stage's fault — so twelve seconds is a very long time and reaching it means something
        /// is actually wrong. See <see cref="Beat"/> for the one gate that IS charged to it.
        /// </summary>
        const float GiveUpAfter = 12f;

        /// <summary>The stage scene the watcher is holding a card for. Invalid means nothing is due.</summary>
        static Scene _pending;

        /// <summary>Unscaled seconds of uncovered stage seen so far. Reset by anything covering it.</summary>
        static float _dwell;

        /// <summary>Unscaled seconds spent actively waiting. See <see cref="GiveUpAfter"/>.</summary>
        static float _age;

        /// <summary>
        /// True once the watcher has SEEN the incoming stage take the wheel off the player.
        ///
        /// This is the gate that makes "before the stage starts" mean something, and it is stated as
        /// an observation rather than as a timer because every stage already announces it the same
        /// way. <see cref="StageSeam.Begin"/> takes driving away the instant a transition starts and
        /// pointedly does not give it back; <see cref="TurfDirector"/> holds it through the count-in
        /// and releases it on the horn; <see cref="RallyDirector"/> re-asserts it every frame from
        /// its own phase; <see cref="GameDirector"/> sets it per state and it is false through
        /// briefing, preview and countdown. So "the stage is holding the wheel" is a fact the game
        /// already publishes, and waiting for it is how the primer knows the arena is assembled and
        /// under a director's control rather than half-built.
        ///
        /// It also gives the watcher a clean way to be LATE: once this is true, driving going true
        /// means the stage has handed over and the moment for a primer has passed. A card that
        /// dropped in after the horn would freeze a live match, which is worse than never showing.
        /// </summary>
        static bool _sawHeld;

        /// <summary>The frame the beat last ran on, so it cannot be double-stepped. See Beat.</summary>
        static int _lastBeatFrame = -1;

        /// <summary>
        /// Stages already primed in this visit to the championship, by scene name.
        ///
        /// NAMES rather than scene handles, and it is worth saying why the weaker key is the right
        /// one. Each stage is entered at most once per championship — <see cref="GameDirector"/>
        /// marks Rally and Bloom as played and will not run them twice, and a retry re-mows inside
        /// the scene it is already in rather than reloading it — so a name IS unique per run. And
        /// where it is not, the weaker key errs the way this feature should err: a stage somehow
        /// entered twice teaches its controls once, because the second time the player already
        /// knows. Cleared on any single-mode scene load below, which is what puts the primers back
        /// for the next championship without this file having to know the front page's name.
        /// </summary>
        static readonly HashSet<string> _primed = new HashSet<string>();

        /// <summary>
        /// Install the watcher, once per session, and pick up the scene that is already open.
        ///
        /// AFTER scene load rather than before, which is the opposite of the choice
        /// <see cref="PausePopup.RegisterWithStack"/> makes, and for the opposite reason. The pause
        /// factory has to exist before the first frame because Escape must work on the first frame.
        /// Nothing here is waiting on a key. What this DOES need is to see the scene that Unity has
        /// already opened, because a stage scene opened directly in the editor for a standalone
        /// review run — which is the entire review loop for both arenas, see
        /// <see cref="TurfBootstrap"/> — never raises a <see cref="SceneManager.sceneLoaded"/> a
        /// handler registered this late would catch. So the already-loaded scenes are swept once,
        /// here, and the event covers every load after it.
        ///
        /// The statics are wiped first for the reason <see cref="PopupStack.ResetForSession"/> and
        /// <see cref="MatchState"/> both give at length: with domain reloading off, entering play
        /// mode does not reset a static, and this project's cross-scene machinery is all statics.
        /// Without the wipe, a session stopped mid-stage would leave the next one holding a pending
        /// scene that no longer exists and a set of names that says the primers have already been
        /// shown.
        ///
        /// Both subscriptions are removed before they are added. With domain reload off these are
        /// the same delegate fields the last session left behind, and += without -= is how a handler
        /// ends up running four times on the fourth play.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            Disarm();
            _lastBeatFrame = -1;
            _primed.Clear();

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            // THE HEARTBEAT, and the one piece of engine surface this file leans on.
            //
            // A watcher needs a frame while NOTHING is pushed, so the popup stack's own tick cannot
            // supply it — the stack only steps the popup on top of it. The two alternatives were a
            // hidden MonoBehaviour, which this project rejects for exactly this shape of job (see
            // PopupStack.EnsureBeat: "a MonoBehaviour has to live in a file named after itself"),
            // and a second surgery on Unity's player loop, which would mean a second file mutating
            // a structure the stack already owns and reinstalls at session reset.
            //
            // Application.onBeforeRender is neither. It is a plain static event on the engine, it
            // fires once a frame from the BeforeRender stage, it fires at Time.timeScale zero, it
            // cannot be duplicated by a scene load, and it costs one line. It also fires BEFORE the
            // frame is drawn, which is a small bonus: the card built during it is composited into
            // the same frame that dimmed, rather than the game showing one more undimmed frame.
            //
            // If the primer ever stops appearing anywhere at all, this subscription is the first
            // thing to check — a heartbeat that does not beat is a silent, total failure, and it is
            // the only single point of failure in this file.
            Application.onBeforeRender -= Beat;
            Application.onBeforeRender += Beat;

            for (int i = 0; i < SceneManager.sceneCount; i++) Arm(SceneManager.GetSceneAt(i));
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // A single-mode load has replaced the world, so whatever the player was taught belonged
            // to a championship that no longer exists. This is how going back to the front page and
            // starting again gets the primers back, without this file naming the front page.
            //
            // The stages load ADDITIVELY on top of a sleeping Main — see RallyStage and TurfStage,
            // which do that so the cut mask survives — so this deliberately does not fire for them
            // and stage one's primer stays remembered for the whole run.
            if (mode == LoadSceneMode.Single) _primed.Clear();
            Arm(scene);
        }

        /// <summary>Start watching a scene, if it is a stage and has not already had its card.</summary>
        static void Arm(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            if (!IsStage(scene.name)) return;
            if (_primed.Contains(scene.name)) return;

            _pending = scene;
            _dwell = 0f;
            _age = 0f;
            _sawHeld = false;
        }

        static void Disarm()
        {
            _pending = default;
            _dwell = 0f;
            _age = 0f;
            _sawHeld = false;
        }

        /// <summary>
        /// The three stages, and nothing else. The front page is not one, which is the point of
        /// checking by name at all rather than by "has a mower in it".
        /// </summary>
        static bool IsStage(string sceneName)
            => sceneName == LawnArtScene
            || sceneName == RallyStage.SceneName
            || sceneName == TurfStage.SceneName;

        /// <summary>
        /// One frame of the watcher, and the whole of the WHEN decision.
        ///
        /// The brief for this file asked when the card should appear and wanted the answer argued
        /// rather than picked, because there are three plausible ones and two of them are wrong in
        /// ways that only show up in a build.
        ///
        ///   ON SCENE LOAD is wrong, and badly. All three stage scenes are loaded UNDER A CLOSED
        ///   CURTAIN: the seam shuts the frame before LoadSceneAsync is called and lifts it only
        ///   after the arena has genuinely rendered. The curtain sits at sorting order 30000 and
        ///   this card at 24900, so a primer pushed on load is a card nobody can see, stopping the
        ///   clock, behind an opaque frame — and the player's only way out of it is to press a key
        ///   they were never shown, on a screen that looks like the game has hung mid-transition.
        ///
        ///   ON THE HANDOVER — the first frame the stage would give the player the wheel — is
        ///   defensible and still wrong. Bloom Rush and the Goose Rally both open on an overhead
        ///   establishing shot, count down over it, and release on a horn. Freezing on the horn
        ///   means the game shouts GROW and then immediately stops, and the player reads their
        ///   controls having already been told to go. Controls belong BEFORE the count-in, not
        ///   after it.
        ///
        ///   ON THE FIRST CLEAR, SETTLED FRAME OF THE STAGE is what this does. The stage is fully
        ///   assembled, the establishing shot is up and has been up long enough to register, the
        ///   director is still holding the wheel through its own intro, and every one of those
        ///   intros is stepped on Time.deltaTime — TurfDirector, RallyDirector and GameDirector all
        ///   take the scaled clock — so stopping it freezes the count-in cleanly and lets it resume
        ///   from where it was. The primer does not fight the director's intro; it stands inside it.
        ///
        /// Each gate below therefore says NOT YET rather than NEVER, and the difference matters:
        ///
        ///   SimClock.Scripted    never, and this one really is never. The capture harness steps the
        ///                        game by hand and owns the clock; the popup stack refuses to touch
        ///                        Time.timeScale under it, so a primer pushed here would sit on
        ///                        screen unpaused for ever and put a controls card in every frame
        ///                        sheet the review process produces.
        ///   scene gone           never. A stage aborted before it started has no controls to teach.
        ///   a popup is up        not yet. Something already has the player's attention and a
        ///                        controls card is not entitled to land on top of it.
        ///   curtain or seam      not yet, and this is the gate that makes the whole thing work.
        ///   the opening story    not yet. GameDirector ignores Escape during it and the sequence
        ///                        reads its own keys; dimming a story page to explain the handbrake
        ///                        is the wrong beat in every possible sense.
        ///   no InputReader       not yet. The stage's input pipeline is not up, so there is nothing
        ///                        to say anything true about.
        ///   driving already live too late, once the wheel has been seen held. See _sawHeld.
        /// </summary>
        static void Beat()
        {
            // Once a frame, whatever drives this. Cheap, and it means the dwell below measures
            // frames of stage rather than invocations of a delegate.
            if (Time.frameCount == _lastBeatFrame) return;
            _lastBeatFrame = Time.frameCount;

            if (!_pending.IsValid()) return;
            if (SimClock.Scripted) { Disarm(); return; }
            if (!_pending.isLoaded) { Disarm(); return; }

            // Clamped for the reason the curtain and the popup stack both clamp: a browser tab
            // regaining focus hands over a quarter-second delta, and a settle measured through one
            // of those is a settle that never happened.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            // The two HOLDS, which stand aside for something else and spend no budget doing it. A
            // player can leave a pause menu up for an hour and the opening story is minutes long;
            // neither is the watcher waiting on a stage, and charging them to the budget below would
            // make a patient player the reason the primer vanished.
            if (PopupStack.Any) return;

            var director = GameDirector.Instance;
            if (director != null && director.State == GameState.Intro) { _dwell = 0f; return; }

            // From here down the watcher IS waiting on the stage, so this is where the budget is
            // spent — including while the curtain is still down, which is deliberate. A seam that
            // never ends is exactly the sort of fault that would otherwise turn this feature off
            // for ever with nothing in the console, and it cannot be a false positive: the curtain
            // runs a self-raising watchdog of its own at ten seconds, so a frame still covered at
            // twelve is already a bug somebody wants to hear about.
            _age += dt;
            if (_age > GiveUpAfter)
            {
                Debug.LogWarning($"[Primer] gave up on '{_pending.name}' after {GiveUpAfter:0}s: no " +
                                 "controls card was shown for it. Either the seam never lifted " +
                                 $"(curtain busy: {Curtain.Live != null && Curtain.Live.Busy}, seam: " +
                                 $"{StageSeam.InProgress}) or the stage never took the wheel off the " +
                                 "player the way its director is supposed to.");
                Disarm();
                return;
            }

            var curtain = Curtain.Live;
            if (StageSeam.InProgress || (curtain != null && curtain.Busy)) { _dwell = 0f; return; }

            var input = InputReader.Instance;
            if (input == null) { _dwell = 0f; return; }

            if (!input.DrivingEnabled) _sawHeld = true;
            else if (_sawHeld) { Disarm(); return; }
            if (!_sawHeld) { _dwell = 0f; return; }

            _dwell += dt;
            if (_dwell < Settle) return;

            // Recorded and disarmed BEFORE the push, not after. OnPush builds a canvas and the stack
            // calls it from inside Push, so anything that ran during it would find the watcher still
            // armed and still one frame away from pushing a second card. Cheap to make impossible.
            string scene = _pending.name;
            _primed.Add(scene);
            Disarm();

            PopupStack.Push(new ControlsPrimer(scene));
        }

        // ==================================================================================
        // THE CARD
        // ==================================================================================

        /// <summary>One line of the control list: the key, and what it does. Nothing else.</summary>
        readonly struct Line
        {
            public readonly string Key;
            public readonly string Verb;
            public Line(string key, string verb) { Key = key; Verb = verb; }
        }

        readonly string _kicker;
        readonly string _title;
        readonly string _objective;
        readonly string _footnote;
        readonly Line[] _lines;

        float _itemsCentreY;
        RectTransform _card;

        ControlsPrimer(string sceneName)
        {
            // ---- what each stage actually honours -------------------------------------------
            //
            // Every line below was READ OFF THE CONSUMER, not off a design document, because the
            // two have already disagreed in this project and the player is the one who finds out.
            // InputReader polls eleven things; what follows is which of them do anything where.
            //
            // Three verbs are gated together on InputReader.RoundActionsEnabled — the horn on E, the
            // overhead check on F, and skip on N — and TurfBootstrap switches that flag OFF for the
            // whole of Bloom Rush, on the stated grounds that a key which works in one stage and not
            // the next is a bug the player has to discover. This card is the other half of that
            // argument: the flag stops the key doing anything, and the card stops the player
            // expecting it to.
            //
            // Deliberately absent from EVERY stage, and each for a checked reason:
            //
            //   R, retry        RetryPressed is not gated by anything, but its only consumers are
            //                   GameDirector's Verdict, Scoreboard, Ceremony and Ending cases. There
            //                   is no mid-round retry to advertise; the result cards own that key
            //                   and prompt for it themselves.
            //   N, skip         same shape. Only ever read on a result card.
            //   the horn in stage one
            //                   GooseDefence is the only thing in the lawn scene that reads the
            //                   horn, and it only runs in GameState.Defence, which Main.unity ships
            //                   with defencePhaseEnabled set to 0. E does nothing on the lawn today.
            //                   If the defence phase is ever switched back on, the beat has a HUD of
            //                   its own to teach it in context, which is the better place for it.
            //   the air check in stage two
            //                   RoundActionsEnabled is still true in the rally, so F genuinely
            //                   raises AerialPressed there — but GameDirector.UpdateAerial is only
            //                   called from the Mowing case, so nothing consumes it. An ungated key
            //                   with no consumer is still a key that does nothing, and the rule is
            //                   about what the player experiences, not about which flag stopped it.
            //
            // Look-back on C is offered everywhere, and that is correct rather than lazy:
            // InputReader keeps it out of the round-verb gate on purpose ("looking over your
            // shoulder is a camera control, not a game action"), and CameraDirector honours it in
            // every scene.

            switch (sceneName)
            {
                case RallyStage.SceneName:
                    _kicker = "STAGE TWO";
                    _title = "GOOSE RALLY";
                    _objective = "One flock, four gardens. Sound the horn at a goose " +
                                 "before it reaches yours.";
                    _footnote = null;
                    _lines = new[]
                    {
                        new Line("W A S D / ARROWS", "DRIVE AND STEER"),
                        new Line("SPACE", "HANDBRAKE"),
                        new Line("SHIFT", "BOOST"),
                        new Line("E", "HORN " + Dot + " TURN A GOOSE"),
                        new Line("C", "GLANCE BEHIND"),
                        new Line("ESC", "PAUSE"),
                    };
                    break;

                case TurfStage.SceneName:
                    _kicker = "STAGE THREE";
                    _title = "BLOOM RUSH";
                    _objective = "Paint ground and hold it. The middle is worth the most " +
                                 "and it is the hardest to keep.";
                    // Said out loud, because two keys the player has been using all evening stop
                    // working here and silence would leave them pressing a dead button and
                    // concluding the game had dropped the input.
                    _footnote = "NO HORN AND NO AIR CHECK IN THE ARENA.";
                    _lines = new[]
                    {
                        new Line("W A S D / ARROWS", "DRIVE AND STEER"),
                        // The mini-turbo is switched on for this mode ALONE — BloomRush.unity sets
                        // driftBoost on all four machines, and TurfArena explains why: every way
                        // into the middle is a ninety degree turn out of a tangent, so the arena's
                        // one manoeuvre is a handbrake slide through a gateway. Worth a word.
                        new Line("SPACE", "HANDBRAKE " + Dot + " DRIFT TURBO"),
                        new Line("SHIFT", "BOOST"),
                        new Line("C", "GLANCE BEHIND"),
                        new Line("ESC", "PAUSE"),
                    };
                    break;

                default:
                    _kicker = "STAGE ONE";
                    _title = "LAWN ART";
                    _objective = "One look at the shape, then mow it from memory. " +
                                 "The judges only ever see it from the air.";
                    _footnote = null;
                    _lines = new[]
                    {
                        new Line("W A S D / ARROWS", "DRIVE AND STEER"),
                        new Line("SPACE", "HANDBRAKE"),
                        new Line("SHIFT", "BOOST"),
                        new Line("F", "CHECK FROM THE AIR " + Dot + " ONCE"),
                        new Line("C", "GLANCE BEHIND"),
                        new Line("ESC", "PAUSE"),
                    };
                    break;
            }
        }

        /// <summary>
        /// The only ornament on this card, and the only one it is allowed.
        ///
        /// The project's TMP font asset is LiberationSans SDF in STATIC atlas mode with 250 glyphs
        /// baked, so anything outside ASCII and a handful of punctuation renders as the missing
        /// glyph box on every platform — <see cref="PausePopup"/> found this the hard way and
        /// records that U+00B7 is one of the ones that IS in the atlas. Arrows, em dashes and
        /// diamonds are not, which is why the drive row spells out the arrow keys in words.
        /// </summary>
        const string Dot = "·";

        // ------------------------------------------------------------------ layout

        const float BoardWidth = 980f;
        /// <summary>Right edge of the key caps. They are right-aligned into the gutter at the middle.</summary>
        const float CapRight = -95f;
        const float CapHeight = 46f;
        const float VerbWidth = 470f;
        /// <summary>Centre of the verb column, whose left edge sits forty pixels off the gutter.</summary>
        const float VerbCentre = 180f;
        const float RowStep = 58f;
        /// <summary>Distance from the top of the board to the first control row.</summary>
        const float RowsTop = 306f;

        protected override float ItemWidth => 400f;
        protected override float ItemHeight => 78f;
        protected override float ItemStep => 78f;
        protected override float ItemsCentreY => _itemsCentreY;

        /// <summary>
        /// Build the board, sized to its own contents.
        ///
        /// Measured from the TOP downward into a cursor and turned into a height at the end, rather
        /// than laid out against a fixed board, because the row count and the footnote both vary by
        /// stage — Bloom Rush has one fewer control and one more line of prose than the lawn. The
        /// alternative is a fixed height with a hole in it on two of the three stages, which is the
        /// specific fault <see cref="PausePopup.Compose"/> sizes itself to avoid.
        /// </summary>
        protected override void Compose()
        {
            // Lighter than the pause board's 0.78, on purpose. The pause menu is asking the player
            // to make a decision about a round and the round behind it is noise; this card is
            // introducing the very place it is dimming, and the establishing shot underneath is half
            // of what it is saying. Dark enough that cream type on it is never in question, light
            // enough that the arena still reads as an arena.
            BuildScrim(0.62f);

            int n = _lines.Length;
            float cursor = RowsTop + (n - 1) * RowStep + CapHeight * 0.5f;

            float footY = 0f;
            if (_footnote != null) { footY = cursor + 30f; cursor = footY + 16f; }

            float itemY = cursor + 34f + ItemHeight * 0.5f;
            cursor = itemY + ItemHeight * 0.5f;

            float hintY = cursor + 34f;
            float height = hintY + 40f;

            _card = BuildBoard(BoardWidth, height);
            float half = height * 0.5f;

            // The column of choices is dealt out by the base AFTER Compose returns, against this
            // property. It has to be a real number by then and there is no layout to read it from
            // any earlier, which is exactly the split OnComposed exists to describe.
            _itemsCentreY = half - itemY;

            BuildText("Kicker", _kicker, 26f, half - 54f,
                      new Vector2(700f, 38f), Gold, false, 0.20f, 14f);
            BuildText("Title", _title, 74f, half - 122f,
                      new Vector2(880f, 96f), Cream, false, 0.13f, 9f)
                .fontStyle = FontStyles.Bold;
            BuildRule(half - 178f, 820f);

            // The one sentence of prose on the card. Wrapped, because it is a sentence and not a
            // label, and kept to a single line of intent — the brief for this file was explicit that
            // it is not a manual, and a stage that needs a paragraph to explain is a stage with a
            // design problem no card can paper over.
            BuildText("Objective", _objective, 27f, half - 228f,
                      new Vector2(830f, 70f), new Color(Cream.r, Cream.g, Cream.b, 0.88f),
                      true, 0.14f, 2f);

            for (int i = 0; i < n; i++)
                Row(i, _lines[i], half - (RowsTop + i * RowStep));

            if (_footnote != null)
                BuildText("Footnote", _footnote, 21f, half - footY,
                          new Vector2(820f, 30f),
                          new Color(Gold.r, Gold.g, Gold.b, 0.80f), false, 0.14f, 8f);

            // ONE choice, and it is doing more work than it looks like.
            //
            // The base's input handling is built around a column of items: it hit-tests the mouse
            // against their rects, and its confirm branch activates the selected one. A card with
            // NO items would therefore be dismissable by Escape and by nothing else — no Enter, no
            // Space, no click — which is not what anybody means by "press anything to start". A
            // single plate makes every one of those work through machinery that is already correct,
            // and it gives the card the same painted button the front page and the pause board use,
            // so starting a stage feels like every other decision in the game.
            //
            // The click lands ON THE PLATE rather than anywhere on screen, and that is the better
            // contract in a browser rather than a compromise. The first click into a WebGL canvas is
            // a FOCUS click — a player alt-tabbing back to the tab, or clicking in for the first
            // time, would spend it dismissing a card they had not read. A plate has to be aimed at.
            AddItem("START", RequestClose);

            // What dismisses this, spelled out, because the card has no other way to end and a
            // player waiting for it to time out would wait for ever. It deliberately does NOT
            // auto-dismiss: the whole reason to stop the world is that the player starts when they
            // are ready, and a card that leaves on a timer is a card that leaves mid-sentence.
            BuildText("Hint",
                      "ENTER   " + Dot + "   SPACE   " + Dot + "   ESC   " + Dot + "   OR CLICK THE PLATE",
                      20f, half - hintY, new Vector2(820f, 30f),
                      new Color(BoardEdge.r, BoardEdge.g, BoardEdge.b, 0.62f), false, 0.14f, 8f);
        }

        /// <summary>
        /// One control: a cream key cap with the key on it, and the verb beside it.
        ///
        /// A CAP rather than two columns of type, because a list of keys set as plain text is a list
        /// the eye has to parse, and a key on a plate is a key. It is the same generated rounded
        /// plank every plate in this subsystem is cut from — <see cref="PopupView.RoundedSprite"/> —
        /// at the board's own cream, so the caps read as more of the venue's painted signage rather
        /// than as a web page's <c>&lt;kbd&gt;</c>.
        ///
        /// The width is picked from the key's LENGTH rather than measured from the finished text,
        /// and that is deliberate. TMP will only give a preferred width once it has a rect to lay
        /// out into, which during construction it does not, and a measurement taken at the wrong
        /// moment silently returns something plausible and wrong. There are exactly two shapes of
        /// key on these cards — a single token like SPACE or ESC, and the one long "W A S D /
        /// ARROWS" — so two widths describe every case, and the label auto-sizes down inside
        /// whichever it gets. A cap that is slightly too roomy is invisible; one that clips is not.
        /// </summary>
        void Row(int index, Line line, float y)
        {
            float capWidth = line.Key.Length > 8 ? 320f : 148f;

            var capGo = new GameObject($"Key {index}", typeof(RectTransform), typeof(Image));
            capGo.transform.SetParent(_card, false);
            var cap = (RectTransform)capGo.transform;
            cap.anchorMin = cap.anchorMax = new Vector2(0.5f, 0.5f);
            // Pivoted on its RIGHT edge, so caps of two different widths still line up down the
            // gutter instead of each being centred on its own middle.
            cap.pivot = new Vector2(1f, 0.5f);
            cap.sizeDelta = new Vector2(capWidth, CapHeight);
            cap.anchoredPosition = new Vector2(CapRight, y);

            var img = capGo.GetComponent<Image>();
            img.sprite = RoundedSprite();
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            img.color = BoardEdge;

            // Built through the base's text builder — which owns the font fallback, the wrap-before-
            // autosize ordering and the outline-via-material-instance rule that a hand-rolled
            // TextMeshProUGUI here would get wrong — and then reparented onto the cap. No outline:
            // dark ink on a cream plate needs no help, and a contour there would read as a sticker,
            // which is the same call AddItem makes for the same reason.
            var key = BuildText($"KeyLabel {index}", line.Key, 24f, y,
                                new Vector2(capWidth, CapHeight), Ink, false, 0f, 6f);
            key.fontStyle = FontStyles.Bold;
            key.rectTransform.SetParent(cap, false);
            Stretch(key.rectTransform);

            var verb = BuildText($"Verb {index}", line.Verb, 26f, y,
                                 new Vector2(VerbWidth, CapHeight), Cream, false, 0.12f, 5f);
            // Left, so the verbs form an edge down the card. Centred verbs against right-aligned
            // caps would leave a ragged channel between the two columns and nothing to read down.
            verb.alignment = TextAlignmentOptions.Left;
            verb.rectTransform.anchoredPosition = new Vector2(VerbCentre, y);
        }

        /// <summary>
        /// Nothing to put back.
        ///
        /// Worth an explicit note rather than an absent override, because the sibling popup DOES do
        /// something here — <see cref="PausePopup"/> ducks AudioListener.volume, since an engine
        /// drone whose pitch is driven by a frozen road speed holds one flat tone under the menu.
        /// This card is up before any of that has started: the machine is idling on a start line
        /// under a director that has not released it, the crowd and the arena's ambience are the
        /// establishing shot's own soundtrack, and quietening them would make the stage feel like it
        /// had been interrupted rather than introduced. So the sound is left exactly alone, and the
        /// only thing this popup ever borrowed — the clock and the wheel — is the stack's to return.
        /// </summary>
        protected override void OnClosed() { }
    }
}
