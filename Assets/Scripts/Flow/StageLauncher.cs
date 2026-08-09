using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DuckMow.Flow
{
    /// <summary>The three things in this game that can be played, named so a menu row is a value.</summary>
    ///
    /// <remarks>
    /// Deliberately NOT <see cref="GameState"/>, and not an index into anything. GameState describes
    /// where a ROUND is — Intro, Mowing, Rally, Bloom, Judging — which is a different question with
    /// different members, and borrowing it here would mean the front page and the round's state
    /// machine sharing one enum that neither of them can extend without breaking the other. This is
    /// a menu's vocabulary: three destinations a player can choose between from cold.
    /// </remarks>
    public enum StageId
    {
        /// <summary>The lawn. Main.unity, and therefore the championship — see StageLauncher's note.</summary>
        LawnArt,
        /// <summary>GooseRally.unity, run standalone.</summary>
        GooseRally,
        /// <summary>BloomRush.unity, run standalone.</summary>
        BloomRush
    }

    /// <summary>
    /// Play one stage, chosen from the front page, without the championship in the way.
    ///
    /// ---- why this is not a call into GameDirector ----
    ///
    /// The game already has a way to reach the two arenas, and it is the running order:
    /// GameDirector plays Goose Rally AS round <c>rallyOnRound</c> and Bloom Rush AS round
    /// <c>bloomOnRound</c>, RallyStage and TurfStage load them ADDITIVELY beside Main.unity, put the
    /// lawn to sleep, and unload them again on the way back. Every part of that machinery exists to
    /// serve one thing — a continuous evening — and every part of it is wrong for a stage select:
    /// it needs a Main.unity underneath it, a Tournament to belong to, a round number to decorate
    /// the board with, and somewhere to go when the horn sounds. A menu that wanted stage three
    /// would have to fake all four, and the fake would be a second answer to "what round is this"
    /// living next to the real one.
    ///
    /// So this takes the other door, which was already there and already tested. Both arenas ask
    /// <c>&lt;Rally|Turf&gt;Handoff.Active</c> on their first frame and, when it is false, run
    /// STANDALONE: RallyBootstrap and TurfBootstrap build an <see cref="InputReader"/> of their own
    /// if the scene has none, start their own music because no round's AudioDirector is going to,
    /// and simply STOP when the match ends rather than handing results back to a round that is not
    /// there. That path exists so the modes can be opened and played on their own during
    /// development, and it is exactly, precisely the dependency-free launch a stage select wants.
    /// Nothing here had to be built for it — this class only has to load one scene with the
    /// handoffs false, and be honest about the curtain.
    ///
    /// ---- the one correctness point that matters ----
    ///
    /// <see cref="RallyHandoff.Active"/>, <see cref="TurfHandoff.Active"/> and
    /// <see cref="DefenceHandoff.Active"/> are STATICS, which is the whole reason they survive a
    /// scene load and the whole reason they can be left behind. A session that played a round and
    /// came back to the menu can leave one of them true — <c>Send</c> sets it and only <c>Post</c>
    /// or <c>Clear</c> puts it back — and a stage-select launch made on top of that would light up
    /// <c>FromRound</c> in the bootstrap, which suppresses the standalone InputReader, suppresses
    /// the standalone music, and makes the stage sit at the end waiting to hand results to a round
    /// that does not exist. It would look like the stage select "sometimes" ships a silent,
    /// unplayable arena. So all three are cleared before every load here, unconditionally, and that
    /// is the single line in this file that is not allowed to be wrong.
    ///
    /// ---- and the curtain ----
    ///
    /// See <see cref="Run"/>. The short version: this class outlives the load, so it raises its own
    /// curtain rather than leaving one down for a scene that may have nobody in it to notice.
    ///
    /// ---- what StageId.LawnArt launches, and why it needed a flag ----
    ///
    /// The other two stages are scenes. LawnArt is not: it is Main.unity, which is the whole
    /// CHAMPIONSHIP — a GameDirector holding <c>playIntro</c> and a three-round running order in
    /// which <c>rallyOnRound: 2</c> and <c>bloomOnRound: 3</c> name rounds two and three as the
    /// other two stages. So for as long as this class only loaded a scene, picking stage one off a
    /// stage select gave the player the opening story followed by all three stages, while picking
    /// stage two or three gave them one match and a horn. Two rows behaved one way and the third
    /// behaved like the entire game, and a board that offers three equal-looking plates and means
    /// something different by one of them is lying to the person clicking it.
    ///
    /// The fix is <see cref="SoloRound"/>: an INTENT, set here and honoured by GameDirector, rather
    /// than a reconfiguration performed here. That distinction is the whole of the design and it is
    /// worth being explicit about, because the obvious alternative — reach into the live
    /// GameDirector and set <c>rallyOnRound = 0</c> — is genuinely worse in two ways. It is a
    /// launcher editing the thing it launches, which is how a class ends up owning half of somebody
    /// else's state machine. And those fields are SERIALIZED: writing them at runtime in the editor
    /// is one accidental "Apply" away from a championship that no longer has a round two in it,
    /// permanently, in a scene file, with nothing in the diff to say why. A bool the director reads
    /// once and forgets cannot do that to anybody.
    /// </summary>
    public static class StageLauncher
    {
        // ------------------------------------------------------------------ the scenes

        /// <summary>
        /// The front page, and the one scene name in this file that is a literal.
        ///
        /// There is no constant for it anywhere in the project — GameDirector.LeaveToMenu spells it
        /// out too. It is written again here rather than reached for through GameDirector because
        /// <see cref="ReturnToMenu"/> has to work when there is no GameDirector at all, which is its
        /// entire reason to exist; a launcher that needs the director to know where the menu is
        /// would be dead on precisely the path it was written for.
        /// </summary>
        const string MenuScene = "Menu";

        /// <summary>
        /// The lawn, when the front page cannot be asked. See <see cref="SceneNameFor"/>.
        /// </summary>
        const string LawnArtSceneFallback = "Main";

        /// <summary>
        /// Which scene a stage lives in.
        ///
        /// The two arenas answer from the stages themselves — <see cref="RallyStage.SceneName"/> and
        /// <see cref="TurfStage.SceneName"/> are the constants the round chain loads them by, so a
        /// stage select and a spliced round can never disagree about which file a mode is in.
        ///
        /// The lawn has no such constant: the front page holds it as a serialized field
        /// (MainMenu.playScene) so it can be repointed in the inspector, which means the authority
        /// is an OBJECT rather than a symbol. Asked of the live MainMenu when there is one, which is
        /// the case that matters because that is where a stage select is clicked, and falls back to
        /// the same default the field ships with. Do not "simplify" this to a bare literal: the day
        /// somebody repoints playScene at a test scene, PLAY and STAGE 1 would go to two different
        /// places and only one of them would be reported as broken.
        /// </summary>
        public static string SceneNameFor(StageId stage)
        {
            switch (stage)
            {
                case StageId.GooseRally: return RallyStage.SceneName;
                case StageId.BloomRush:  return TurfStage.SceneName;
                default:
                    var menu = Object.FindFirstObjectByType<MainMenu>(FindObjectsInactive.Include);
                    return menu != null && !string.IsNullOrEmpty(menu.playScene)
                        ? menu.playScene
                        : LawnArtSceneFallback;
            }
        }

        /// <summary>
        /// What the curtain's board says the player is going to, in the venue's own voice.
        ///
        /// Public because the menu wants the same words on the row that the sign shows a moment
        /// later — a stage select whose labels and whose transition disagree reads as two different
        /// games, and this is one string rather than two that can drift.
        /// </summary>
        public static string TitleFor(StageId stage)
        {
            switch (stage)
            {
                case StageId.GooseRally: return "GOOSE RALLY";
                case StageId.BloomRush:  return "BLOOM RUSH";
                default:                 return "LAWN ART";
            }
        }

        /// <summary>
        /// The small line above the name on the sign.
        ///
        /// Plain ASCII capitals with no punctuation, which is not a style preference: every kicker
        /// this game still shows is that shape — "ROUND 1 OF 3", "FINAL ROUND" — and the curtain's
        /// board is rendered in a font atlas that has never been asked
        /// for a middle dot or an em dash. A glyph the atlas does not carry is a blank rectangle in
        /// the middle of the loudest thing on screen.
        ///
        /// Each one names the VERB rather than the stage number. The player has just read the stage
        /// number off the row they clicked; what the sign can usefully add in the second before the
        /// world arrives is what they are about to be asked to do.
        /// </summary>
        public static string KickerFor(StageId stage)
        {
            switch (stage)
            {
                case StageId.GooseRally: return "HOLD THE GARDEN";
                case StageId.BloomRush:  return "CLAIM THE GROUND";
                default:                 return "MOW THE PICTURE";
            }
        }

        /// <summary>Which boundary a given launch is, so the curtain dresses itself correctly.</summary>
        static MatchState.Seam SeamFor(StageId stage)
        {
            switch (stage)
            {
                // The same two the round chain uses for these destinations, so entering Bloom Rush
                // from a menu row and entering it from round three are the same wipe. Petals for the
                // flowers, leaves for the geese; see MatchState.StyleFor.
                case StageId.GooseRally: return MatchState.Seam.RoundToRally;
                case StageId.BloomRush:  return MatchState.Seam.RoundToBloom;
                // Grass rising over the frame, exactly as MainMenu.LeaveToGame asks for, because
                // this IS that journey — off the front page and onto the lawn.
                default:                 return MatchState.Seam.MenuToRound;
            }
        }

        /// <summary>
        /// Extra covered time before the curtain lifts on a given destination.
        ///
        /// The arenas get more than the lawn. Both of them build something expensive on their first
        /// frames that the establishing shot should not be caught halfway through — Bloom Rush
        /// clears a render texture and lays out a nav graph, the rally stands up its competitors —
        /// and <see cref="StageSeam.End"/>'s two settle frames are a floor on "has anything drawn",
        /// not a promise that what drew is finished.
        /// </summary>
        static float HoldFor(string scene)
            => scene == RallyStage.SceneName || scene == TurfStage.SceneName ? 0.25f : 0.15f;

        /// <summary>
        /// True for the destinations that have a component of their own waiting to take
        /// <see cref="MatchState.CurtainPending"/> on their first frame.
        ///
        /// Main.unity has GameDirector.Start and Menu.unity has MainMenu.Start. The two arena scenes
        /// have NOTHING that reads the flag — nobody ever needed them to, because the round chain
        /// reaches them additively with the coroutine that closed the curtain still alive on the
        /// other side. Setting it for them anyway would leave a true lying around for whichever
        /// scene loaded next to pick up and misread as its own handover, and the scene that would
        /// pick it up is the front page, where the flag does more than schedule a raise: it is also
        /// what tells MainMenu to skip its fade-up from black. A front page that fades up from black
        /// behind a closed curtain shows a grey step the instant that curtain lifts.
        /// </summary>
        static bool ClaimsHandover(string scene)
            => scene == MenuScene || scene == SceneNameFor(StageId.LawnArt);

        // ------------------------------------------------------------------ the gate

        static bool _busy;

        /// <summary>
        /// True while this class is running a transition of its own.
        ///
        /// The menu should read it and refuse to act on a second click. It is a plain latch rather
        /// than anything cleverer because the failure it prevents is specific and cheap to describe:
        /// LoadSceneAsync is not idempotent, a plate under a mouse is clicked twice by roughly every
        /// player who has ever met a slow browser, and two loads queued against one curtain is a
        /// transition that closes, opens onto the destination, and then loads it a second time.
        /// GameDirector.BackToMenu guards itself the same way and for the same reason.
        ///
        /// Cleared from a finally, so no exit — a guard, a failed load, a throw from inside the seam
        /// — can leave the menu permanently dead. That is the whole reason the body below is one
        /// coroutine wrapped in try/finally rather than several tidy ones.
        /// </summary>
        public static bool Busy => _busy;

        // ------------------------------------------------------------------ the solo intent

        static bool _soloRound;

        /// <summary>
        /// True when the Main.unity about to open was asked for by a STAGE SELECT, and should
        /// therefore play one round on its own rather than a three-round championship.
        ///
        /// ---- why a static, and why here ----
        ///
        /// A static because it has to survive a scene load, which is the same reason
        /// <see cref="MatchState.CurtainPending"/>, <see cref="RallyHandoff.Active"/> and every
        /// other fact that crosses a boundary in this project is one. The alternative — a
        /// DontDestroyOnLoad carrier — is a component that exists in two scenes at once, which is
        /// how a retry ends up with two of them; RallyHandoff's note argues that at length and this
        /// is not a new case.
        ///
        /// Here rather than in MatchState because this class is the only thing in the project that
        /// ever sets it. MatchState holds the JOURNEY — how far through the evening the player is,
        /// how loud the next seam should be — and is read by half a dozen files; a menu's private
        /// intent parked in the middle of that is a field every one of them can see and none of them
        /// has any business reading. The setter and the resets live together, in the file whose
        /// behaviour they describe.
        ///
        /// ---- and why it is TAKEN rather than read ----
        ///
        /// This is the one part that has to be right, because the failure is silent. PLAY on the
        /// front page does not come through this class at all — MainMenu.LeaveToGame calls
        /// SceneManager.LoadSceneAsync(playScene) directly — so PLAY can never SET this flag. What
        /// PLAY absolutely can do is INHERIT one: play stage one from the stage select, come back to
        /// the front page, press PLAY, and a flag left true would hand the player a one-round
        /// championship with no opening and no stages, with nothing on screen to explain why. So the
        /// director takes it once and clears it — <see cref="TakeSoloRound"/>, exactly the contract
        /// <see cref="MatchState.TakeCurtainPending"/> is under and for exactly the same reason — and
        /// it is cleared again on every other route out of this class besides.
        /// </summary>
        public static bool SoloRoundPending => _soloRound;

        /// <summary>
        /// Take the solo intent once, so the round that opens is the only round that can act on it.
        ///
        /// Called by GameDirector.Start, which latches the answer into a field of its own for the
        /// life of the round — the flag has to be spent immediately, but the BEHAVIOUR has to last
        /// until the player leaves, and those are two different lifetimes.
        /// </summary>
        public static bool TakeSoloRound()
        {
            bool solo = _soloRound;
            _soloRound = false;
            return solo;
        }

        // ------------------------------------------------------------------ the verbs

        /// <summary>
        /// Play a stage. Fire and forget; the transition runs itself from here.
        /// </summary>
        public static void Launch(StageId stage)
        {
            if (_busy)
            {
                Debug.Log("[StageLauncher] a transition is already running; ignoring the launch.");
                return;
            }

            string scene = SceneNameFor(stage);
            if (!CanLoad(scene, $"stage '{stage}'")) return;

            var host = Host();
            if (host == null) return;

            // Assigned, not set — the else branch is load bearing.
            //
            // Writing `if (LawnArt) _soloRound = true;` would leave a stale true standing whenever
            // the lawn was launched and, for whatever reason, nobody took it: a Main.unity whose
            // GameDirector is disabled, a load that the browser never finished, a session stopped in
            // the editor between the two. The next Goose Rally launch would carry it, and the PLAY
            // after that would spend it. An unconditional assignment has no stale state to leave —
            // every launch states what it is, including the two that are not the lawn.
            //
            // After the guards, so a launch this method is about to refuse cannot change what the
            // NEXT one means.
            _soloRound = stage == StageId.LawnArt;

            _busy = true;
            host.StartCoroutine(Run(scene, SeamFor(stage), TitleFor(stage), KickerFor(stage)));
        }

        /// <summary>
        /// Back to the front page, from wherever the player is — including from a scene that has no
        /// GameDirector in it at all.
        ///
        /// That last clause is the whole point of this method. Today the pause menu OMITS its BACK
        /// TO MENU row when <c>GameDirector.Instance</c> is null, because the only exit it knew was
        /// <c>director.BackToMenu()</c> and there was nothing honest for the row to do without one.
        /// In a stage-select launch of Goose Rally or Bloom Rush there is no GameDirector by
        /// construction — that is what standalone MEANS — so the player would reach the horn and
        /// have no way out of the arena short of reloading the tab.
        ///
        /// ---- why it delegates when it can ----
        ///
        /// When a GameDirector IS alive this hands straight over to it rather than doing the work
        /// again. GameDirector.LeaveToMenu is not merely "cover, clear, load": it drops its cached
        /// stage handles, and it aborts the defence phase BEFORE the load because that phase holds
        /// Time.timeScale down to two percent during a hit stop and the load is asynchronous, so the
        /// old scene keeps ticking. Both of those reach private state that only the director can
        /// see. A copy here would be a second teardown for the same round, free to drift from the
        /// first, and the drift would present as "going back to the menu from a rally sometimes
        /// leaves the game in slow motion" — which is exactly the bug that comment was written
        /// after. One caller, two implementations, and the right one chosen by whether the thing
        /// that owns the round is there to do it.
        /// </summary>
        public static void ReturnToMenu()
        {
            if (_busy)
            {
                Debug.Log("[StageLauncher] a transition is already running; ignoring the exit.");
                return;
            }

            // The stack is emptied on BOTH branches and before either of them, because it holds
            // Time.timeScale at zero and InputReader.DrivingEnabled at false for as long as anything
            // is in it — see PopupStack.ApplyGates — and the caller here is, by construction, a
            // popup that is about to destroy its own scene. PopupStack has an activeSceneChanged net
            // under this, but a net is what catches the fall rather than a reason not to step
            // carefully. Harmless if the caller already did it: PopAll on an empty stack is a no-op.
            DuckMow.UI.PopupStack.PopAll();

            // The solo round is over the moment the player heads for the front page, whichever
            // branch below takes them there. Belt and braces with the take in GameDirector.Start —
            // by the time a solo round can be left, its director has already spent the flag — but
            // the branch that matters is the OTHER one: a player who abandoned a stage-select launch
            // of the lawn before Main.unity ever came up (a load the browser stalled on, a Bloom
            // Rush launched from the pause board) is the one case where nobody took it, and this is
            // the last chance to put it back before the front page offers PLAY again.
            _soloRound = false;

            var director = GameDirector.Instance;
            if (director != null)
            {
                // Busy is deliberately NOT claimed on this branch. It describes transitions THIS
                // class is running, and there is no coroutine here to clear it — a flag set on a
                // path with nothing to reset it is a menu that is dead for the rest of the session.
                // The double-click this would have guarded is guarded on the far side instead, by
                // GameDirector's own _leavingToMenu, which exists for the identical reason.
                director.BackToMenu();
                return;
            }

            if (!CanLoad(MenuScene, "the front page")) return;

            var host = Host();
            if (host == null) return;

            _busy = true;
            // WORDLESS, like the round's own way out. GameDirector's leave-to-menu dropped its
            // sign when the copy pass went through, on the argument that the curtain already
            // carries the tournament's identity and a headline over it is the part that reads as
            // UI pasted onto the world — and that "you are going where you just asked to go" is
            // not information worth a board. This is the same journey taken from a standalone
            // arena instead of from the lawn, so it has to make the same argument or the game
            // shows a sign on one route out and not the other. Empty is a supported first-class
            // case in Curtain.Close; the wipe plays in full and no plate is ever positioned.
            host.StartCoroutine(Run(MenuScene, MatchState.Seam.RoundToMenu, null, null));
        }

        // ------------------------------------------------------------------ the transition

        /// <summary>
        /// One boundary, end to end: cover, wipe the statics, load, and lift.
        ///
        /// ---- who raises the curtain, and why it cannot stick ----
        ///
        /// This is the part a standalone launch had to answer differently from everything else in
        /// the project, so it is worth being explicit rather than leaving it to be inferred.
        ///
        /// Every existing single-scene load in this game hands the raise to the DESTINATION.
        /// MainMenu.LeaveToGame closes the curtain, sets <see cref="MatchState.CurtainPending"/> and
        /// loads Main — and it has to, because the load destroys the MainMenu component and the
        /// coroutine running on it, so there is nobody left on this side to lift anything.
        /// GameDirector.Start takes the flag and lifts. LeaveToMenu does the mirror image, and
        /// MainMenu.Start takes it.
        ///
        /// That pattern cannot be copied here, and the reason is the whole premise of the feature: a
        /// standalone arena has NO GameDirector. BloomRush.unity and GooseRally.unity contain a
        /// bootstrap, a director and an arena, and not one line anywhere in either scene reads
        /// CurtainPending. Left down for them, the curtain would be raised by nothing at all — and
        /// the only thing that would eventually clear the frame is Curtain's ten-second watchdog,
        /// which logs an error and exists to survive a bug rather than to implement a feature. Ten
        /// opaque seconds at the start of every Bloom Rush is not a transition.
        ///
        /// So the raise is kept on THIS side, and it can be, because unlike MainMenu this class is
        /// not a scene object. Its coroutine runs on the CURTAIN (see <see cref="Host"/>), which is
        /// DontDestroyOnLoad and is alive on both sides of the swap by design — that is the one
        /// thing the curtain was built for. So it closes the curtain, loads, waits for the load to
        /// genuinely be done, and lifts the curtain it closed, all from one coroutine on one object
        /// that never went away. There is no destination to be told anything, and therefore no
        /// destination that can fail to have been told.
        ///
        /// CurtainPending is still set for the two scenes that have somebody waiting for it, and
        /// that is belt and braces rather than a second mechanism: StageSeam.End latches on the
        /// first caller and every later one falls through, which is precisely the case its
        /// <c>_raising</c> flag was written for. Whichever of GameDirector.Start and this coroutine
        /// gets there first wins, and RaiseSeam is a one-line wrapper around the same End, so it
        /// genuinely does not matter which.
        /// </summary>
        static IEnumerator Run(string scene, MatchState.Seam seam, string headline, string kicker)
        {
            try
            {
                // Before anything is covered or loaded, and before MatchState is touched. See the
                // class note: a handoff left Active by a previous session is the one fault here that
                // produces a stage which looks built rather than a stage which looks broken.
                RallyHandoff.Clear();
                TurfHandoff.Clear();
                DefenceHandoff.Clear();

                // A fresh evening. The escalation starts from quiet whatever the last run ended on,
                // which is what stops a stage picked after a full championship arriving on the
                // finale's brass. It also has to happen BEFORE the seam below, because
                // StageSeam.Begin reads MatchState.Escalation to decide how loud to be.
                MatchState.BeginSession();

                // The stack, for the same reason ReturnToMenu empties it: a stage select is very
                // likely a popup, and a popup still in the stack across a load is a clock still held
                // at zero. Belt and braces with ReturnToMenu's own call; PopAll is idempotent.
                DuckMow.UI.PopupStack.PopAll();

                // ---- outro and cover ----
                yield return StageSeam.Begin(seam, headline, kicker);

                // ---- the load ----
                //
                // Only the two scenes that read it. See ClaimsHandover.
                MatchState.CurtainPending = ClaimsHandover(scene);
                MatchState.PendingHeadline = null;
                MatchState.PendingKicker = null;

                var load = SceneManager.LoadSceneAsync(scene);
                if (load == null)
                {
                    // CanLoad already vetted this before a single frame was covered, so reaching
                    // here means the scene became unloadable between the check and the call, which
                    // is not a thing that happens — but a curtain left down over a load that never
                    // started is the one failure in this file with no way back, so it gets a branch.
                    Debug.LogError($"[StageLauncher] '{scene}' would not start loading. The frame is " +
                                   "covered with nothing behind it; opening the curtain again on " +
                                   "whatever was there before.");
                    MatchState.CurtainPending = false;
                    yield return StageSeam.End();
                    yield break;
                }

                yield return load;
                // Belt and braces on a browser, where a load can report done from an operation that
                // is still settling. Costs nothing when isDone is already true.
                while (!load.isDone) yield return null;

                // ---- the clock ----
                //
                // Here, and not before the cover, and that ordering is the point. Two things in this
                // game warp Time.timeScale — GameDirector's end-of-round hit stop and the defence
                // phase's — and both write it EVERY FRAME from the scene being left, which keeps
                // ticking right through an asynchronous load. A one is only safe to write once
                // there is nobody left alive to overwrite it, and that moment is now. Left alone
                // under the capture harness, which owns the clock and steps the game by hand; the
                // same rule PopupStack.ApplyGates and GooseDefence.UpdateClock follow.
                if (!SimClock.Scripted) Time.timeScale = 1f;

                // ---- the lift ----
                //
                // Whether or not the destination also thinks it is doing this. StageSeam.End takes
                // the first caller and drops the rest.
                yield return StageSeam.End(HoldFor(scene));
            }
            finally
            {
                // Every exit, including the ones an exception took. Unity logs a throw from a
                // coroutine and drops the rest of it on the floor without unwinding to any caller,
                // so this is the only construct that can promise the menu is still clickable
                // afterwards.
                _busy = false;
            }
        }

        // ------------------------------------------------------------------ housekeeping

        /// <summary>
        /// Refuse a load that cannot work, BEFORE anything on screen has changed.
        ///
        /// The order matters more than the check. RallyStage and TurfStage discover a missing scene
        /// after they have already closed the curtain, and have to unwind — they have no choice,
        /// because they are inside a round that has to continue. This one is entered from a menu
        /// with nothing at stake, so the honest answer to "that scene is not in the build" is to say
        /// so loudly and leave the player looking at the page they clicked on, rather than to play a
        /// full wipe and then a full unwipe at them for no reason.
        ///
        /// Names the scene, because the fault is always the same one and is always fixed the same
        /// way: the scene is missing from EditorBuildSettings, where DuckMenuBuilder.RegisterBuildScenes
        /// and each stage builder are what put it. It fails silently otherwise — LoadSceneAsync
        /// returns null and logs nothing anybody is reading.
        /// </summary>
        static bool CanLoad(string scene, string what)
        {
            if (string.IsNullOrEmpty(scene))
            {
                Debug.LogError($"[StageLauncher] {what} has no scene name. Nothing to load.");
                return false;
            }
            if (Application.CanStreamedLevelBeLoaded(scene)) return true;

            Debug.LogError($"[StageLauncher] cannot launch {what}: scene '{scene}' is not in the " +
                           "build settings, so it can never be loaded at runtime. Rebuild the scene " +
                           "from the Duck menu — each builder registers its own — or run " +
                           "'Duck/4 · Build Menu Scene', which registers the rest.");
            return false;
        }

        /// <summary>
        /// The object the transition's coroutine runs on: the curtain itself.
        ///
        /// A coroutine has to live on a MonoBehaviour, and this one has to live on a MonoBehaviour
        /// that SURVIVES the scene load happening in the middle of it — see <see cref="Run"/>, where
        /// that is the entire mechanism. The project already contains exactly one object built to do
        /// that, says so at length at the top of <see cref="Curtain"/>, and calls it "the only object
        /// in the project that is alive in two scenes at once on purpose". Making a second one to
        /// stand beside it would be a second answer to a question that already has a good one, and
        /// two DontDestroyOnLoad objects with overlapping jobs is how one of them gets duplicated on
        /// a retry — which is the specific failure RallyHandoff's note warns about and the reason
        /// everything else that crosses a scene here is a static.
        ///
        /// It also ties the coroutine's life to the right thing rather than to an arbitrary thing.
        /// The only work in <see cref="Run"/> is closing a curtain, loading under it and raising it
        /// again; if the curtain is gone there is nothing left for the coroutine to be doing, and
        /// having it die with the curtain is more correct than having it outlive it and try to raise
        /// something that is not there. <see cref="Curtain.Ensure"/> is idempotent, and StageSeam
        /// would have called it a few lines later anyway, so nothing is built early.
        ///
        /// Not the player-loop beat PopupStack installs, and the difference is worth stating because
        /// that is the other precedent in the project and it is the wrong one here. PopupStack needs
        /// to run at two exact points in EVERY frame for ever, which no execution order can give it.
        /// This needs to run a SEQUENCE — cover, load, wait, lift — whose every step is already
        /// written as an IEnumerator by StageSeam and Curtain, and consuming an IEnumerator needs
        /// StartCoroutine. Rewriting StageSeam.Begin as a hand-rolled state machine driven off a
        /// player-loop callback, purely to avoid touching a MonoBehaviour, would be a second copy of
        /// the transition system.
        /// </summary>
        static Curtain Host()
        {
            var host = Curtain.Ensure();
            if (host == null)
            {
                Debug.LogError("[StageLauncher] there is no curtain and one could not be built, so " +
                               "there is nothing to run the transition on. The menu has done nothing.");
                return null;
            }

            // Ensure will happily adopt an INACTIVE curtain — it searches with
            // FindObjectsInactive.Include, on purpose, because an editor script that left one behind
            // is exactly the duplicate it is trying not to make. StartCoroutine on a component of an
            // inactive GameObject throws rather than returning null, which would take the launch
            // down with it, so the one case Ensure tolerates is the one case that is fixed here.
            if (!host.gameObject.activeSelf) host.gameObject.SetActive(true);
            return host;
        }

        /// <summary>
        /// Wipe the latch before the first scene of a session, for the reason
        /// <see cref="MatchState"/> and PopupStack both give at their own resets: statics are not
        /// reset by entering play mode when domain reloading is off.
        ///
        /// <see cref="_busy"/> is the older of the two and the easier to describe. A session stopped
        /// in the editor mid-launch leaves it true, and the next session's front page would then be
        /// a stage select where nothing happens when you click — silently, with no error, for ever.
        ///
        /// <see cref="_soloRound"/> is the newer one and fails the other way round: a session
        /// stopped in the editor between a stage-select launch of the lawn and GameDirector.Start
        /// taking the flag leaves it TRUE, and the next session's PLAY would then open a
        /// championship that quietly turns out to be one round with no opening story in front of it.
        /// The player's first act in a new session is the one thing that must never inherit anything
        /// from the last one.
        ///
        /// There is deliberately nothing else to reset: the coroutine host is not cached here, it is
        /// asked of <see cref="Curtain.Ensure"/> at the moment it is needed, which is the same rule
        /// PopupStack.ApplyGates follows for InputReader and for the same reason — a reference kept
        /// across a scene load is a destroyed component in the next one.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ResetForSession()
        {
            _busy = false;
            _soloRound = false;
        }
    }
}
