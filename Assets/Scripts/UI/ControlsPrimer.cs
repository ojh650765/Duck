using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DuckMow.UI
{
    /// <summary>
    /// THE CARD THAT GOES UP AS A STAGE HANDS THE PLAYER THE WHEEL: the world dims, and the player
    /// is told what they are about to be asked to do and which keys do it.
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

        /// <summary>Unscaled seconds spent actively waiting. See <see cref="GiveUpAfter"/>.</summary>
        static float _age;

        /// <summary>
        /// True once the watcher has SEEN the incoming stage take the wheel off the player.
        ///
        /// This is what tells the primer the stage is assembled and under a director's control
        /// rather than half-built, and it is stated as an observation rather than as a timer because
        /// every stage already announces it the same way. <see cref="StageSeam.Begin"/> takes driving
        /// away the instant a transition starts and pointedly does not give it back;
        /// <see cref="TurfDirector"/> holds it through the count-in and releases it on the horn;
        /// <see cref="RallyDirector"/> re-asserts it every frame from its own phase;
        /// <see cref="GameDirector"/> sets it per state and it is false through briefing, preview and
        /// countdown.
        ///
        /// It also gives the watcher a clean way to be LATE: once this is true, driving going true
        /// means the stage has handed over and the moment for a primer has passed. A card that
        /// dropped in after the horn would freeze a live match, which is worse than never showing.
        /// </summary>
        static bool _sawHeld;

        /// <summary>
        /// True once the rig has been seen framing this stage's ESTABLISHING SHOT.
        ///
        /// The other half of the timing decision, and the half that moved. See <see cref="Beat"/>.
        /// Latched rather than tested at the moment of the push, because a stage's opening move can
        /// happen behind a curtain that has not lifted yet and an edge missed while the frame was
        /// covered would be an edge missed for good.
        /// </summary>
        static bool _sawEstablishing;

        /// <summary>
        /// True once the rig has LEFT the establishing shot for the shot the stage is played in.
        ///
        /// Also latched, for the reason above: this is the moment the card is due, and it is allowed
        /// to arrive while the frame is still covered.
        /// </summary>
        static bool _handover;

        /// <summary>
        /// The camera rig of the stage currently being watched, found on demand and dropped whenever
        /// a new stage arms.
        ///
        /// Re-found per stage rather than cached for the session, and that is load bearing in a
        /// championship: an arena is loaded ADDITIVELY on top of a sleeping Main, so there are two
        /// CameraDirectors in memory and the one that matters is whichever scene is awake.
        /// FindFirstObjectByType skips inactive GameObjects by default and Main's roots are
        /// deactivated by RallyStage.Sleep, so the search answers correctly — but only if it is asked
        /// again once the arena is up, which is what <see cref="Arm"/> guarantees.
        /// </summary>
        static CameraDirector _rig;

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

        // ==================================================================================
        // HAS THIS PLAYER PLAYED BEFORE
        // ==================================================================================

        /// <summary>
        /// PlayerPrefs key, namespaced with the project prefix for the reason MasterAudio spells out
        /// and Haptics repeats: on WebGL, PlayerPrefs is one IndexedDB store keyed off the build
        /// path, which on a shared domain is genuinely a bucket other builds may have written into.
        /// </summary>
        const string KeyTaught = "DuckMow.Primer.Taught";

        /// <summary>
        /// Whether this SESSION teaches. Read from storage once, at <see cref="Install"/>, and then
        /// never re-read.
        ///
        /// ---- why a session latch rather than reading the flag each time ----
        ///
        /// The requirement is "show it once, ever", and the naive version of that — write the flag as
        /// the first card closes, and test the flag before every card — is a different feature. There
        /// are THREE cards, one per stage, because there are three verb sets: the horn is the whole
        /// of stage two and stage three switches both it and the air check off. A player who dismissed
        /// the LAWN ART card and then, twenty minutes later in the same first run, was handed the
        /// goose rally in silence would never be told about the horn at all.
        ///
        /// So the decision is taken once for the whole run: a player who has never played is taught
        /// every stage of the run they are in, and a player who has is taught none of them. That is
        /// what "has this player played before" actually means, and it is the reading that makes the
        /// three cards a course rather than three copies of one notice.
        ///
        /// ---- and why the flag is written on the way OUT ----
        ///
        /// It is set when a card is DISMISSED, never when one is pushed — see <see cref="OnClosed"/>.
        /// A player who shuts the tab, or stops play mode, while the card is on screen has not read
        /// it, and a flag written at the push would deny them it for ever with no way back short of
        /// clearing browser storage. Nothing writes it during the frames the card is up.
        ///
        /// The way back, for a player who HAS been taught and wants the list again, is the pause
        /// board — see <see cref="ForPause"/>. That door is what makes it safe for this to be a
        /// one-way flag at all.
        /// </summary>
        static bool _teachThisSession;

        /// <summary>
        /// Remember that this player has been through the primer. Idempotent, and flushed at once.
        ///
        /// Set/Save split as MasterAudio and Haptics both use it: SetInt touches an in-memory
        /// dictionary and is free, and PlayerPrefs.Save() is the one that goes to storage — in WebGL
        /// an asynchronous FS.syncfs against IndexedDB. Here it is flushed IMMEDIATELY rather than
        /// debounced, because unlike a volume slider this is written once in a player's life and the
        /// very next thing that happens is a stage starting, which is exactly the sort of moment a
        /// browser tab gets closed during.
        /// </summary>
        static void MarkTaught()
        {
            if (PlayerPrefs.GetInt(KeyTaught, 0) != 0) return;
            PlayerPrefs.SetInt(KeyTaught, 1);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Forget that this player has played, so the next run teaches again.
        ///
        /// Nothing in the game calls this. It exists for the settings board to hang a row off if one
        /// is ever wanted, and — more usefully today — so that reviewing the primer does not mean
        /// hand-editing the registry or clearing a browser's site data. A one-way flag with no stated
        /// way back is a feature that can be looked at exactly once per machine.
        /// </summary>
        public static void ForgetPlayer()
        {
            PlayerPrefs.SetInt(KeyTaught, 0);
            PlayerPrefs.Save();
            _teachThisSession = true;
        }

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

            // Whether this whole run teaches, decided here and not asked again — see the note on
            // the field. Read even when the answer is "no", because the watcher below still has to
            // be installed: the pause board can ask for a card at any moment and IsStage is the only
            // thing that knows whether there is one to give.
            _teachThisSession = PlayerPrefs.GetInt(KeyTaught, 0) == 0;

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

        /// <summary>
        /// Start watching a scene, if it is a stage, this player is being taught, and it has not
        /// already had its card.
        ///
        /// The rig is dropped rather than kept: this is the one moment the watcher knows a different
        /// stage has arrived, and in a championship the arena that just loaded has a camera director
        /// of its own standing in front of the lawn's. See <see cref="_rig"/>.
        /// </summary>
        static void Arm(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            if (!_teachThisSession) return;
            if (!IsStage(scene.name)) return;
            if (_primed.Contains(scene.name)) return;

            _pending = scene;
            _age = 0f;
            _sawHeld = false;
            _sawEstablishing = false;
            _handover = false;
            _rig = null;
        }

        static void Disarm()
        {
            _pending = default;
            _age = 0f;
            _sawHeld = false;
            _sawEstablishing = false;
            _handover = false;
            _rig = null;
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
        /// ---- what this used to do, and the hang it caused ----
        ///
        /// It waited for the wheel to be taken off the player, dwelled half a second on the first
        /// uncovered frame, and pushed. Both halves of that were inferences and the first one was
        /// simply false: DRIVING IS DISABLED FOR THE WHOLE OF THE OPENING STORY. So on a PLAY from
        /// the front page the card would push itself over the cutscene about half a second in — and
        /// because the popup stack holds Time.timeScale at zero while a popup wants time stopped, and
        /// because ComicSequence was stepping itself on the SCALED clock, the story stopped dead on
        /// its fade-in frame. Its own failsafe went with it: GameDirector's Intro budget was also
        /// counting scaled seconds, so the deadline that exists to rescue exactly this could never
        /// arrive. The game hung, permanently, on the first thing a new player is ever shown.
        ///
        /// It was a RACE, which is why it did not always reproduce — anything that pushed the dwell
        /// past the end of the story hid it — and the card was NOT invisible while it happened: it
        /// sorts at 24900 against the comic canvas's 200. What the player described as a frozen
        /// cutscene was the frozen art behind a card that had every right to be there.
        ///
        /// Two other files were changed so that this class can never do that to anything again — the
        /// story and the ending run unscaled now, and their budgets do too — but the fix that matters
        /// is here: THE TRIGGER IS NO LONGER AN INFERENCE. "Driving is off, therefore a stage is
        /// about to start" was never true, and no dwell makes a false premise safe.
        ///
        /// ---- the beat it hangs off now ----
        ///
        /// All three stages open the same way, and they say so out loud through the camera rig:
        ///
        ///   ESTABLISH   a shot of the place. GameDirector goes Briefing then VenuePreview over the
        ///               whole venue; RallyDirector and TurfDirector both snap to CameraMode.Reveal,
        ///               looking straight down at the arena.
        ///   HAND OVER   the rig moves to CameraMode.Chase, behind the machine the player is about to
        ///               drive. GameDirector does it entering Countdown, TurfDirector at the end of
        ///               its establish, RallyDirector as the intro's last beat.
        ///   COUNT       three, two, one — and only then is the wheel handed over.
        ///
        /// So the card goes up on the frame the rig LEAVES the establishing shot for the play shot.
        /// That is exactly what was asked for: the player has been shown where they are, and they are
        /// told which buttons to press immediately before they are given any. It is one observation,
        /// identical in all three stages, and it is a real published fact rather than a delay — the
        /// front page never enters Chase from an establishing shot, and neither does a cutscene,
        /// because neither has a CameraDirector doing this at all.
        ///
        /// It also lands BEFORE each stage's count-in rather than on the horn, which is the one thing
        /// the previous version of this comment got right and is worth keeping: freezing on "GO" reads
        /// the controls to a player who has already been told to drive.
        ///
        /// Both halves are LATCHED rather than tested at the moment of the push, because a stage's
        /// opening move is allowed to happen while the curtain is still down and an edge missed
        /// behind a covered frame would be an edge missed for good.
        ///
        /// ---- this trigger was accused of a fault it did not have; do not rewrite it for that ----
        ///
        /// It was reported as never firing on the lawn, with the watchdog below giving up every
        /// single run, and a replacement was written that deleted the whole reading and had the
        /// three directors call StageHandingOver() instead. It was not the trigger. It was not even
        /// this file. Time.timeScale was ZERO — a play
        /// session had been stopped with a popup holding the clock, and Unity had written that zero
        /// into ProjectSettings/TimeManager.asset, where it sat and started every later session with
        /// the world stopped. The story and the curtain still ran, because both are unscaled;
        /// GameDirector never left the venue preview, because Briefing, Preview and Countdown are
        /// all timed on the scaled clock. So the rig really did stay in its establishing shot, and
        /// the watchdog was telling the truth.
        ///
        /// The reading below was re-checked against all three directors afterwards and is correct in
        /// all three: GameDirector enters Briefing, then VenuePreview, and reaches CameraMode.Chase
        /// only on the countdown; RallyDirector snaps to Reveal and drops to Chase as the last beat
        /// of its intro; TurfDirector holds Reveal and swoops to Chase before its 3-2-1. The lawn's
        /// own numbers leave 4.6 s of slack — briefingDuration 3.4 plus previewDuration 4 puts Chase
        /// 7.4 s after the budget starts, against the twelve below.
        ///
        /// PopupStack now refuses to begin a session on a stopped clock and hands the clock back on
        /// the way out of play mode, so the fault cannot be laid again; and the watchdog prints the
        /// clock, so the next person to read it is not offered a shortlist that excludes the answer.
        ///
        /// Each gate below says NOT YET rather than NEVER, and the difference matters:
        ///
        ///   SimClock.Scripted    never, and this one really is never. The capture harness steps the
        ///                        game by hand and owns the clock; the popup stack refuses to touch
        ///                        Time.timeScale under it, so a primer pushed here would sit on
        ///                        screen unpaused for ever and put a controls card in every frame
        ///                        sheet the review process produces.
        ///   scene gone           never. A stage aborted before it started has no controls to teach.
        ///   a popup is up        not yet. Something already has the player's attention and a
        ///                        controls card is not entitled to land on top of it.
        ///   the opening story    NOT YET, and this is now belt and braces rather than the only
        ///                        thing standing between the player and a hang. The story is not a
        ///                        stage handing over a wheel, so the trigger above cannot fire during
        ///                        it — the rig is in no mode at all while the page is up. The gate
        ///                        stays because a second reason to be right about the single worst
        ///                        failure this file has ever produced is worth four lines.
        ///   curtain or seam      not yet. The frame is covered and a card pushed behind an opaque
        ///                        screen is a card nobody can see, stopping the clock, with the only
        ///                        way out a key they were never shown.
        ///   no InputReader       not yet. The stage's input pipeline is not up, so there is nothing
        ///                        to say anything true about.
        ///   driving already live too late. The stage has handed over and the moment has passed; a
        ///                        card dropped in after the horn would freeze a live match.
        /// </summary>
        static void Beat()
        {
            // Once a frame, whatever drives this.
            if (Time.frameCount == _lastBeatFrame) return;
            _lastBeatFrame = Time.frameCount;

            if (!_pending.IsValid()) return;
            if (SimClock.Scripted) { Disarm(); return; }
            if (!_pending.isLoaded) { Disarm(); return; }

            // The two HOLDS, which stand aside for something else and spend no budget doing it. A
            // player can leave a pause menu up for an hour and the opening story is minutes long;
            // neither is the watcher waiting on a stage, and charging them to the budget below would
            // make a patient player the reason the primer vanished.
            if (PopupStack.Any) return;

            var director = GameDirector.Instance;
            if (director != null && director.State == GameState.Intro) return;

            // Clamped for the reason the curtain and the popup stack both clamp: a browser tab
            // regaining focus hands over a quarter-second delta.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            // From here down the watcher IS waiting on the stage, so this is where the budget is
            // spent — including while the curtain is still down, which is deliberate. A seam that
            // never ends is exactly the sort of fault that would otherwise turn this feature off
            // for ever with nothing in the console, and it cannot be a false positive: the curtain
            // runs a self-raising watchdog of its own at ten seconds, so a frame still covered at
            // twelve is already a bug somebody wants to hear about.
            _age += dt;
            if (_age > GiveUpAfter)
            {
                ReportGivingUp();
                Disarm();
                return;
            }

            // ---- the observation, latched, whether or not the frame is covered -----------------

            var input = InputReader.Instance;
            if (input == null) return;

            if (!input.DrivingEnabled) _sawHeld = true;
            else if (_sawHeld) { Disarm(); return; }
            if (!_sawHeld) return;

            // ---- whose camera are we watching -------------------------------------------------
            //
            // Re-resolved whenever the cached rig is gone or ASLEEP, and the second half of that is
            // the part that had to be got right. In a championship an arena is loaded ADDITIVELY on
            // top of Main and Main is only put to sleep AFTER the load completes — so the frame this
            // watcher arms on, there are two CameraDirectors and the awake one is still the LAWN'S.
            // Caching that one would leave the watcher staring at a rig whose scene is about to stop
            // being ticked at all: its mode would never change again, the hand-over would never be
            // seen, and the arena's card would be dropped twelve seconds later with a warning
            // blaming the arena. isActiveAndEnabled is what notices, because RallyStage.Sleep
            // deactivates Main's roots rather than destroying them.
            var rig = _rig != null && _rig.isActiveAndEnabled
                ? _rig : Object.FindFirstObjectByType<CameraDirector>();

            // A DIFFERENT rig means a different stage's opening, so the observation starts over.
            //
            // Without this the swap would produce a false positive rather than a miss, which is
            // worse: CameraDirector's Mode field INITIALISES to Chase and is only moved to an
            // establishing shot when its director begins. So a watcher that had already seen the
            // lawn's rig in some non-Chase mode would meet the arena's brand-new rig reading Chase,
            // conclude the hand-over had happened, and put the card up over the arena's establishing
            // shot — the exact beat this trigger exists to protect.
            if (!ReferenceEquals(rig, _rig))
            {
                _rig = rig;
                _sawEstablishing = false;
                _handover = false;
            }

            // Null is NOT NEVER: the arena's rig may simply not be awake yet on the frame its scene
            // finished loading. The budget above is what stops that being forever.
            if (_rig == null) return;

            if (_rig.Mode != CameraMode.Chase) _sawEstablishing = true;
            else if (_sawEstablishing) _handover = true;
            if (!_handover) return;

            // ---- and the push, which does wait for a frame the player can see -------------------

            var curtain = Curtain.Live;
            if (StageSeam.InProgress || (curtain != null && curtain.Busy)) return;

            // Recorded and disarmed BEFORE the push, not after. OnPush builds a canvas and the stack
            // calls it from inside Push, so anything that ran during it would find the watcher still
            // armed and still one frame away from pushing a second card. Cheap to make impossible.
            string scene = _pending.name;
            _primed.Add(scene);
            Disarm();

            PopupStack.Push(new ControlsPrimer(scene, Shown.Opening));
        }

        /// <summary>
        /// Say what this watcher was actually waiting for when it ran out of budget.
        ///
        /// ---- why this is worth twenty lines ----
        ///
        /// The message this replaces offered two candidate causes — a seam that never lifted, or a
        /// camera that never left its establishing shot — and printed the seam's state to let the
        /// reader choose between them. The real cause was NEITHER, and the shortlist is what made it
        /// expensive: TIME.TIMESCALE WAS ZERO. Unity writes the play-mode value of that global back
        /// into ProjectSettings/TimeManager.asset, so a session stopped with a popup open leaves a
        /// zero on disk and every session after it starts with the world stopped. The story still
        /// plays and the curtain still lifts, because both run unscaled; GameDirector never advances
        /// out of the venue preview, because every beat it owns is timed on the scaled clock. So the
        /// camera genuinely never reached the chase, and this watcher said so, accurately, and two
        /// people in a row read it as an accusation against the trigger and rewrote the trigger.
        ///
        /// The budget is spent on the UNSCALED clock, which is right — a watchdog that stops when the
        /// world stops cannot report a stopped world — and is exactly why this can fire while nothing
        /// else is moving. That makes naming the clock the single most useful thing it can print.
        ///
        /// So this reports the OBSERVATIONS rather than a shortlist of conclusions: which of the two
        /// latches is still down, what the rig's mode actually is, what state the director is in, and
        /// whether the world is moving at all. A watchdog that cannot say what it was waiting for
        /// makes its own failure harder to fix than silence would.
        /// </summary>
        static void ReportGivingUp()
        {
            string mode = _rig == null ? "no rig found" : _rig.Mode.ToString();
            var director = GameDirector.Instance;
            string state = director == null ? "no director" : director.State.ToString();
            bool curtain = Curtain.Live != null && Curtain.Live.Busy;

            Debug.LogWarning(
                $"[Primer] gave up on '{_pending.name}' after {GiveUpAfter:0}s of waiting: no " +
                $"controls card was shown for it. wheel held: {_sawHeld}, establishing shot seen: " +
                $"{_sawEstablishing}, hand-over seen: {_handover}, camera: {mode}, director: {state}, " +
                $"curtain busy: {curtain}, seam: {StageSeam.InProgress}, timeScale: {Time.timeScale:0.###}. " +
                "A timeScale of zero is the answer whenever it appears here and nothing else has " +
                "moved: this budget runs unscaled while the round does not, so the stage really did " +
                "stay in its establishing shot — because the whole world did. See PopupStack.");
        }

        // ==================================================================================
        // THE CARD, ASKED FOR
        // ==================================================================================

        /// <summary>
        /// Why a card is up, which changes two words on it and one side effect.
        ///
        /// The distinction is not cosmetic. An OPENING card is the game teaching an untaught player,
        /// so its plate starts the stage and dismissing it is what records that they have now been
        /// taught. A REFERENCE card was asked for from the pause board by somebody who already knows
        /// — it goes BACK to the board it came from and says nothing about whether anyone was ever
        /// taught anything. A single card that did both would have to guess which, and it would guess
        /// by looking at the popup stack, which is exactly the sort of inference that produced the
        /// hang described in <see cref="Beat"/>.
        /// </summary>
        public enum Shown { Opening, Reference }

        /// <summary>
        /// Which stage the player is standing in, or null when they are not standing in one.
        ///
        /// The ACTIVE scene first, because that is the fact the flow already maintains: RallyStage
        /// and TurfStage both call SetActiveScene on the arena as they load it, so during rounds two
        /// and three the active scene is the arena and Main is asleep behind it. Falling back to any
        /// loaded stage covers a standalone arena opened straight from the editor, where the active
        /// scene is the arena anyway, and the lawn, where it is Main.
        /// </summary>
        static string CurrentStageScene()
        {
            var active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isLoaded && IsStage(active.name)) return active.name;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.IsValid() && s.isLoaded && IsStage(s.name)) return s.name;
            }
            return null;
        }

        /// <summary>
        /// True when there is a controls card that could honestly be shown right now.
        ///
        /// Read by the pause board to decide whether to offer the row at all. It is deliberately a
        /// question about the WORLD rather than about whether the player has been taught: a card the
        /// player has already seen is exactly the card they are asking for when they open the board,
        /// and one they have never seen is if anything more welcome. What it refuses is a row on the
        /// FRONT PAGE's pause board — there is no stage there, so there are no controls to list, and
        /// a plate that opened a card headed LAWN ART over the menu would be the board describing a
        /// place the player is not in.
        /// </summary>
        public static bool HasCardHere => CurrentStageScene() != null;

        /// <summary>
        /// The card for wherever the player is, to be pushed on top of the pause board.
        ///
        /// The whole of requirement four, and it costs one method because the card was already a
        /// popup: the stack was built for a screen opening on top of another screen — that is the
        /// case PausePopup's QUIT confirmation exists to prove — so this is a push, not a second
        /// overlay with its own canvas, its own input and its own idea of what Escape means.
        ///
        /// The CONTENT is not duplicated anywhere. There is one list of what each stage honours, in
        /// the constructor below, and both the opening card and this one are built from it — so the
        /// controls a returning player looks up cannot drift from the controls a new player was
        /// taught. That was the alternative worth refusing: a second control list on the pause board
        /// would be a second place to update the day a key changes, and the two would disagree
        /// silently, in the one screen a confused player goes to.
        ///
        /// Null when there is no stage. Callers must check <see cref="HasCardHere"/> first rather
        /// than pushing this blind — PopupStack.Push(null) is a silent no-op, which would present as
        /// a plate that does nothing.
        /// </summary>
        public static ControlsPrimer ForPause()
        {
            string scene = CurrentStageScene();
            return scene == null ? null : new ControlsPrimer(scene, Shown.Reference);
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
        readonly Shown _reason;

        float _itemsCentreY;
        RectTransform _card;

        ControlsPrimer(string sceneName, Shown reason)
        {
            _reason = reason;

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
            //   R, start over   RetryPressed is not gated by anything, but its only consumers are
            //                   GameDirector's Scoreboard, Ceremony and Ending cases — the Verdict
            //                   read has gone with the same-picture retry, so R no longer means
            //                   anything at the end of a round, only at the standings, where it
            //                   throws the whole championship away. There is nothing mid-round to
            //                   advertise; the boards own that key and prompt for it themselves.
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
            //
            // The WORD on it names what pressing it does, which is not the same thing on both routes
            // in. On the opening card the stage is on the other side of it and is waiting to be
            // started. Opened from the pause board there is a pause board on the other side of it and
            // nothing has been started yet — a plate reading START there would promise to resume a
            // round that the board behind it is still holding.
            AddItem(_reason == Shown.Opening ? "START" : "BACK", RequestClose);

            // What dismisses this, spelled out, because the card has no other way to end and a
            // player waiting for it to time out would wait for ever. It deliberately does NOT
            // auto-dismiss: the whole reason to stop the world is that the player starts when they
            // are ready, and a card that leaves on a timer is a card that leaves mid-sentence.
            //
            // ESC is named on both, and it means different things by design — the stack pops ONE
            // popup, so on the opening card it starts the stage and on the reference card it puts
            // the pause board back. Both are "go back to what you were doing", which is the only
            // thing Escape means anywhere in this game.
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
        /// Nothing to put back, and one thing to write down.
        ///
        /// NOTHING TO PUT BACK is worth an explicit note rather than an absent override, because the
        /// sibling popup DOES do something here — <see cref="PausePopup"/> ducks the master volume,
        /// since an engine drone whose pitch is driven by a frozen road speed holds one flat tone
        /// under the menu. This card is up before any of that has started: the machine is idling on a
        /// start line under a director that has not released it, the crowd and the arena's ambience
        /// are the establishing shot's own soundtrack, and quietening them would make the stage feel
        /// like it had been interrupted rather than introduced. So the sound is left exactly alone,
        /// and the only thing this popup ever borrowed — the clock and the wheel — is the stack's to
        /// return.
        ///
        /// THE ONE THING TO WRITE DOWN is that this player has now been taught, and the timing of
        /// that write is the whole of the requirement. It happens HERE, as the card leaves, and never
        /// when one is pushed: a player who closes the tab or stops play mode while the card is on
        /// screen has not read it, and OnPop simply never runs for them, so the next session teaches
        /// again. A flag set on the way IN would have spent a player's one and only primer on a card
        /// they never finished looking at, permanently, with no way back short of clearing browser
        /// storage.
        ///
        /// Only the OPENING card writes it. A card the player went and fetched from the pause board
        /// says nothing about whether anybody ever taught them anything — and on the first run they
        /// may well fetch one BEFORE the stage that was going to teach them has got there.
        ///
        /// It does not change what the rest of THIS run does: the decision to teach is latched once
        /// per session in <see cref="Install"/>, so the two arenas still get their cards after the
        /// lawn's has been dismissed. See <see cref="_teachThisSession"/>.
        /// </summary>
        protected override void OnClosed()
        {
            if (_reason == Shown.Opening) MarkTaught();
        }
    }
}
