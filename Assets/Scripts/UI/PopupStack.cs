using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;

namespace DuckMow.UI
{
    /// <summary>
    /// The one place that knows what is in front of the game and who Escape belongs to.
    ///
    /// A STACK rather than a single "current popup", because the first thing anybody wants after a
    /// pause menu is a confirmation on top of it — "leave the round?" — and a single slot forces
    /// that into one of two bad shapes: the pause menu grows a second mode and starts drawing two
    /// different screens out of one set of fields, or the confirmation replaces the pause menu and
    /// cancelling it has to rebuild the thing it destroyed. With a stack, Escape closes the top of
    /// the pile, one popup at a time, all the way back to the game — which is what every player
    /// already expects Escape to do, and it costs a List.
    ///
    /// A STATIC, for the reason set out at length in <see cref="RallyHandoff"/> and
    /// <see cref="MatchState"/>: this fact has to be readable from four scenes, two of which are
    /// opened and played on their own, and a DontDestroyOnLoad carrier is a component alive in two
    /// scenes at once, which is how a retry ends up with two of them. Nothing here draws, so nothing
    /// here needs to survive a load as an object — the popups themselves are scene objects and are
    /// SUPPOSED to die with their scene, which is exactly what the pruning below expects.
    ///
    /// What it owns, and what it will fight you for:
    ///   * <see cref="Time.timeScale"/>, while any popup in the stack wants time stopped.
    ///   * <see cref="InputReader.DrivingEnabled"/>, while any popup wants the mower held.
    ///   * The Escape key, whenever the stack is not empty.
    /// Both of the first two are BORROWED, not taken. They are read back every frame while held and
    /// handed back exactly as found — see <see cref="ApplyGates"/>, which is the most careful code
    /// in this file and had to be.
    ///
    /// What it deliberately does NOT own: drawing, layout, sound, and any opinion about what a
    /// popup looks like. It also does not own the pause menu itself — see
    /// <see cref="PauseFactory"/> for why that is a registration and not a reference.
    /// </summary>
    public static class PopupStack
    {
        // ------------------------------------------------------------------ the stack

        static readonly List<IPopup> _stack = new List<IPopup>(4);

        /// <summary>How many popups are open. Zero when the game is in front of the player.</summary>
        public static int Depth => _stack.Count;

        /// <summary>True while anything at all is in front of the game.</summary>
        public static bool Any => _stack.Count > 0;

        /// <summary>The popup the player is actually looking at, or null when the stack is empty.</summary>
        public static IPopup Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        /// <summary>
        /// True for the rest of a frame on which the stack has already answered the player's input —
        /// which today means Escape, and means it whether the key closed something or was swallowed
        /// by a popup that refuses to close on it.
        ///
        /// This exists because of one specific frame and it is worth being blunt about which. The
        /// stack polls Escape from the same latch <see cref="GameDirector"/> polls it from, a beat
        /// earlier in the player loop. On the frame that closes the LAST popup, the stack is empty
        /// again by the time the director looks — so the director sees a live Escape edge, an empty
        /// stack, and cheerfully reopens the pause menu the player just shut. Every close would be
        /// invisible. This flag is how the director knows the edge is spent.
        ///
        /// Cleared at the top of every <see cref="Tick"/>, before anything else, so it can never
        /// latch on and quietly kill the key for good.
        /// </summary>
        public static bool Consumed { get; private set; }

        /// <summary>
        /// How the game asks for its pause menu without knowing what a pause menu is.
        ///
        /// <see cref="GameDirector"/> has to be able to open the pause popup, and the pause popup has
        /// to be able to call back into GameDirector to leave for the front page. Naming each other
        /// directly makes that a cycle: neither file can be written, moved or replaced without the
        /// other. So the seam is here — the popup registers a factory on startup (a
        /// RuntimeInitializeOnLoadMethod on its own side), and the director calls it without ever
        /// learning the type.
        ///
        /// Null is a legal, silent state, and it means Escape does NOTHING. That is on purpose: a
        /// dead key is recoverable and obvious in one line of code, whereas a hard reference that
        /// fails to compile takes the whole game down with it. If Escape stops opening anything, the
        /// registration on the popup's side is what to check first.
        /// </summary>
        public static Func<IPopup> PauseFactory;

        // ------------------------------------------------------------------ the borrowed globals

        /// <summary>True while this stack is the reason Time.timeScale is zero.</summary>
        static bool _timeHeld;

        /// <summary>What the clock goes back to. Kept current while held — see ApplyGates.</summary>
        static float _timeScaleOnHold = 1f;

        static bool _drivingHeld;
        static bool _drivingOnHold = true;

        /// <summary>The frame a popup was last pushed on, so it cannot be closed by its own keypress.</summary>
        static int _pushedFrame = -1;

        // ------------------------------------------------------------------ push and pop

        /// <summary>
        /// Put a popup in front of everything, including any popup already there.
        ///
        /// The one below is told it has been <see cref="IPopup.OnCovered"/> BEFORE the newcomer is
        /// told to build itself, so on the single frame both are visible the one underneath has
        /// already dimmed. The other order is one frame of two fully lit menus stacked on each
        /// other, which is a frame long and looks like a bug for exactly as long as it lasts.
        /// </summary>
        public static void Push(IPopup popup)
        {
            if (popup == null) return;

            EnsureBeat();
            Prune();

            // Pushing the same instance twice would give it two OnPops and leave the stack holding a
            // popup that has already torn itself down. Cheap to check at this depth, and the caller
            // that does it is a double click, not a bug worth crashing over.
            if (_stack.Contains(popup)) return;

            var below = Top;
            _stack.Add(popup);
            _pushedFrame = Time.frameCount;

            if (below != null) Dispatch(below, PopupCall.Covered);
            Dispatch(popup, PopupCall.Push);

            ApplyGates();
        }

        /// <summary>
        /// Close the top popup and reveal whatever was underneath it.
        ///
        /// Safe on an empty stack, and safe to call from anywhere EXCEPT the inside of a popup's own
        /// <see cref="IPopup.Tick"/> — that is what returning true from Tick is for.
        /// </summary>
        public static void Pop()
        {
            Prune();
            if (_stack.Count == 0) { ApplyGates(); return; }

            var going = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);

            Dispatch(going, PopupCall.Pop);
            var revealed = Top;
            if (revealed != null) Dispatch(revealed, PopupCall.Revealed);

            // After the removal, so the locks are recomputed against the stack that is actually left.
            ApplyGates();
        }

        /// <summary>
        /// Empty the stack, top first, telling every popup in it that it is over.
        ///
        /// Nothing is ever told it has been <see cref="IPopup.OnRevealed"/> on the way out — being
        /// uncovered for the one instant before you are also closed is not a reveal, and a popup
        /// that used the call to start an animation would be starting it into its own teardown.
        ///
        /// This is what a popup must call before it does anything that destroys the scene it lives
        /// in. GameDirector.BackToMenu loads a new scene, and a stack still holding a popup across
        /// that load is a stack still holding the clock at zero.
        /// </summary>
        public static void PopAll()
        {
            // Taken one at a time off the end rather than walked, because OnPop is somebody else's
            // code and is entitled to touch the stack. Bounded, so a popup that pushes a replacement
            // from its own teardown is a logged mess rather than a hung editor.
            int guard = 64;
            while (_stack.Count > 0 && guard-- > 0)
            {
                int i = _stack.Count - 1;
                var p = _stack[i];
                _stack.RemoveAt(i);
                Dispatch(p, PopupCall.Pop);
            }
            if (_stack.Count > 0)
            {
                Debug.LogError("[PopupStack] PopAll did not reach the bottom — something is pushing " +
                               "popups from inside OnPop. Clearing the rest without telling them.");
                _stack.Clear();
            }
            ApplyGates();
        }

        // ------------------------------------------------------------------ the frame

        /// <summary>
        /// One frame of the whole subsystem: consume Escape, then step the top popup.
        ///
        /// Public because the capture harness steps the game by hand (see <see cref="SimClock"/>) and
        /// because a test has to be able to drive this without a player loop. In an ordinary session
        /// it is called by <see cref="EarlyTick"/>, once, from inside the player loop.
        ///
        /// Only the TOP popup is stepped. See <see cref="IPopup.Tick"/>.
        /// </summary>
        public static void Tick(float unscaledDt)
        {
            // First thing, unconditionally, including on frames where the stack is empty. If this
            // ever fails to run the flag latches on and Escape is dead for the rest of the session.
            Consumed = false;

            Prune();
            if (_stack.Count == 0) return;

            // Not on the frame the popup arrived. The key that opened it is still latched for this
            // frame in every source we read, so without this a pause menu would open and shut inside
            // one frame and look like a key that does nothing.
            if (Time.frameCount != _pushedFrame && EscapePressed())
            {
                Consumed = true;
                var top = Top;
                if (top != null && top.ClosesOnEscape) Pop();
                // Nothing else this frame. The popup underneath has just been revealed and stepping
                // it now would hand it the same frame of input that closed the thing above it.
                return;
            }

            var stepping = Top;
            if (stepping == null) return;

            bool wantsOut;
            try
            {
                wantsOut = stepping.Tick(unscaledDt);
            }
            catch (Exception e)
            {
                // The only try/catch in this subsystem, and it earns its place. Everywhere else in
                // this project a throw spoils a frame and the game carries on. Here the game is
                // stopped at timeScale zero with the controls locked BY THIS CLASS, so a popup that
                // throws every frame does not spoil a frame — it strands the player in a frozen
                // world with no way to press anything. Closing the broken popup is the only outcome
                // that leaves them with a game.
                Debug.LogException(e);
                Debug.LogError($"[PopupStack] popup '{SafeId(stepping)}' threw during Tick and has " +
                               "been closed. The stack will not hold the clock for a popup it cannot step.");
                wantsOut = true;
            }

            if (!wantsOut) return;

            // Pushing from inside Tick is not just allowed, it is the point — a confirmation opening
            // over the pause menu is exactly a popup pushing another one from its own frame. But if
            // a popup does that AND asks to close in the same breath, Pop would take the newcomer
            // off instead of the one that asked, which is the sort of bug that only shows up as "the
            // confirmation flickers". So the close only happens if the popup that asked for it is
            // still the one on top.
            if (ReferenceEquals(Top, stepping)) { Pop(); return; }

            Debug.LogWarning($"[PopupStack] popup '{SafeId(stepping)}' asked to close on the same " +
                             "frame it pushed something on top of itself. Ignoring the close — pop it " +
                             "when the popup above it has gone.");
        }

        /// <summary>
        /// The Escape edge, from the same place the rest of the game reads it.
        ///
        /// <see cref="InputReader"/> first, and this is not a preference. GameDirector reads
        /// <see cref="InputReader.MenuPressed"/>, which is a LATCH refreshed once a frame — and
        /// because the reader ticks at execution order 0 and the director at -10, the director is
        /// always reading the value from the previous frame's poll. Reading the raw device here
        /// instead would put this class a frame AHEAD of the director, which is precisely the
        /// desynchronisation <see cref="Consumed"/> exists to prevent: the stack would close a popup
        /// on frame N and the director would see the edge on frame N+1, with the flag already
        /// cleared, and reopen it.
        ///
        /// The device is the fallback for scenes that have no InputReader at all. In those there is
        /// no director either, so there is nothing to stay in step with.
        /// </summary>
        static bool EscapePressed()
        {
            var input = InputReader.Instance;
            if (input != null) return input.MenuPressed;

            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
        }

        // ------------------------------------------------------------------ the borrowed globals, held

        /// <summary>
        /// Take, hold and hand back the two globals the stack needs, once a frame, LAST.
        ///
        /// Both locks are ORed across the WHOLE stack rather than read off the top, because a
        /// confirmation pushed on top of the pause menu has no opinion about the clock and asking
        /// only the top one would restart the round underneath it.
        ///
        /// The clock is the part that had to be got exactly right, and a snapshot is not enough.
        /// GameDirector runs the end-of-round hit stop by writing Time.timeScale EVERY FRAME on the
        /// unscaled clock (klaxonHitStopScale is 0.24 in the scene), and it stops writing the moment
        /// the stop expires. So a stack that photographed 0.24 at the push and restored 0.24 at the
        /// pop would hand back a value that nothing is coming along to correct, and the whole game
        /// would run at a quarter speed for ever with no way out but a restart. Instead the value to
        /// restore is RE-READ every frame while the lock is held: this runs after every Update and
        /// LateUpdate in the frame, so whatever the world last wrote is sitting there to be
        /// collected before it gets stomped back to zero. A hit stop that expires behind a pause
        /// menu therefore leaves 1 behind, and 1 is what the player gets back.
        ///
        /// Zero is the one value that is never collected, because zero is what this class writes and
        /// collecting it would mean handing back a permanently stopped game.
        ///
        /// Nothing in the project sets Time.timeScale from LateUpdate, so running here wins every
        /// contest. Driving is a real contest and not a theoretical one: RallyDirector re-asserts
        /// DrivingEnabled from its own tick every single frame, so a popup that only wrote it once
        /// on push would be overwritten before the player saw the menu.
        ///
        /// Left alone entirely under <see cref="SimClock.Scripted"/>. The capture harness owns the
        /// clock and steps the game by hand; a timeScale written from under it makes every frame
        /// sheet it produces a lie, which is the same rule GooseDefence.UpdateClock follows.
        /// </summary>
        static void ApplyGates()
        {
            bool wantPause = false, wantBlock = false;
            for (int i = 0; i < _stack.Count; i++)
            {
                var p = _stack[i];
                if (p == null) continue;
                if (p.PausesTime) wantPause = true;
                if (p.BlocksDriving) wantBlock = true;
            }

            if (!SimClock.Scripted)
            {
                if (wantPause)
                {
                    if (!_timeHeld) { _timeScaleOnHold = Time.timeScale; _timeHeld = true; }
                    else if (Time.timeScale > 0f) _timeScaleOnHold = Time.timeScale;
                    Time.timeScale = 0f;
                }
                else if (_timeHeld)
                {
                    Time.timeScale = _timeScaleOnHold;
                    _timeHeld = false;
                }
            }

            if (wantBlock)
            {
                // Resolved here rather than cached: InputReader is a scene object and the stack
                // outlives scenes, so a reference kept from one round is a destroyed component in
                // the next. The property does a Find only when its own cache is empty.
                var input = InputReader.Instance;
                if (input != null)
                {
                    if (!_drivingHeld) { _drivingOnHold = input.DrivingEnabled; _drivingHeld = true; }
                    // Same collection rule as the clock, and the same blind spot: a system that
                    // turns driving ON behind a popup is noticed, one that turns it OFF cannot be,
                    // because false is indistinguishable from this class's own write. That is the
                    // safe way round — every director in the game re-asserts DrivingEnabled on its
                    // next state change, and a mower that is briefly drivable recovers, whereas a
                    // mower stuck on false is a round the player can only watch.
                    else if (input.DrivingEnabled) _drivingOnHold = true;
                    input.DrivingEnabled = false;
                }
            }
            else if (_drivingHeld)
            {
                var input = InputReader.Instance;
                if (input != null) input.DrivingEnabled = _drivingOnHold;
                _drivingHeld = false;
            }
        }

        // ------------------------------------------------------------------ housekeeping

        /// <summary>
        /// Drop popups whose scene has been pulled out from under them.
        ///
        /// A popup is very likely a MonoBehaviour in a scene, and scenes are unloaded by the flow
        /// without asking this class first. A destroyed MonoBehaviour is still a live managed object
        /// holding a live interface reference, so the stack would happily keep it — keep holding the
        /// clock at zero for it — and throw the first time it touched its transform. Unity's null
        /// operator is the only thing that can tell the difference, and this is the one place that
        /// asks. Called before every decision the stack makes.
        /// </summary>
        static void Prune()
        {
            bool removed = false;
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (Alive(_stack[i])) continue;
                _stack.RemoveAt(i);
                removed = true;
            }
            // No OnPop. There is nothing left to call it on.
            if (removed) ApplyGates();
        }

        static bool Alive(IPopup p)
        {
            if (p == null) return false;
            // The managed reference survives Destroy; the overloaded operator is what notices.
            if (p is UnityEngine.Object o) return o != null;
            return true;
        }

        static string SafeId(IPopup p)
        {
            if (p == null) return "<null>";
            try { return p.Id ?? "<no id>"; } catch { return "<threw>"; }
        }

        enum PopupCall { Push, Pop, Covered, Revealed }

        /// <summary>
        /// Make one lifecycle call without letting it take the subsystem down with it.
        ///
        /// Same reasoning as the catch around Tick, and the same stakes. OnPop in particular MUST
        /// complete the pop: it is called after the popup has already been taken out of the list, so
        /// a throw that escaped here would skip <see cref="ApplyGates"/> and leave the clock at zero
        /// with nothing left in the stack to explain why.
        /// </summary>
        static void Dispatch(IPopup p, PopupCall call)
        {
            if (p == null) return;
            try
            {
                switch (call)
                {
                    case PopupCall.Push:     p.OnPush();     break;
                    case PopupCall.Pop:      p.OnPop();      break;
                    case PopupCall.Covered:  p.OnCovered();  break;
                    case PopupCall.Revealed: p.OnRevealed(); break;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"[PopupStack] popup '{SafeId(p)}' threw from {call}. " +
                               "The stack has carried on regardless; the popup is in an unknown state.");
            }
        }

        /// <summary>
        /// Let go of everything without ceremony, because the world the stack was describing has
        /// been replaced.
        ///
        /// No OnPop: at both call sites the popups either no longer exist or belong to a play
        /// session that has ended, and calling into a destroyed MonoBehaviour is how a reset throws.
        ///
        /// The clock goes back to 1 and driving to true, and this is the ONE place in this file
        /// where hardcoding those is right rather than lazy. Everywhere else the stack restores what
        /// it borrowed, because there is a world on the other side that had an opinion. Here there
        /// is not — a 0.24 collected from a hit stop in a scene that has been unloaded describes
        /// nothing, and carrying it across the boundary would open the next scene at a quarter
        /// speed for no reason anybody could ever find.
        /// </summary>
        static void Abandon()
        {
            _stack.Clear();

            if (_timeHeld)
            {
                _timeHeld = false;
                if (!SimClock.Scripted) Time.timeScale = 1f;
            }
            if (_drivingHeld)
            {
                _drivingHeld = false;
                var input = InputReader.Instance;
                if (input != null) input.DrivingEnabled = true;
            }

            _timeScaleOnHold = 1f;
            _drivingOnHold = true;
            Consumed = false;
            _pushedFrame = -1;
        }

        /// <summary>
        /// Hand the clock back before the application ends, if this class is the reason it is
        /// stopped.
        ///
        /// ---- the fault this closes, which shipped as far as a committed project setting ----
        ///
        /// This class is the ONLY thing in the runtime that ever writes Time.timeScale = 0; the
        /// klaxon hit stop bottoms out at 0.05 and the defence phase at 0.02. It borrows the clock
        /// on push and hands it back on pop, and that contract is complete for every popup that is
        /// closed. A play session STOPPED while a popup is open never pops.
        ///
        /// In the editor that is not a lost frame, it is a lost setting: Time.timeScale is one of
        /// the few globals whose play-mode value Unity WRITES BACK to ProjectSettings, so a session
        /// killed with the pause board up leaves m_TimeScale: 0 in TimeManager.asset. It is a real
        /// file on disk, it survives branch switches and domain reloads, it is easy to commit by
        /// accident, and every later session starts with the world stopped — the story and the
        /// curtain still play, because they run unscaled, while the game behind them never advances
        /// a single state. This project lost days to that: a frozen world was diagnosed twice as a
        /// broken controls-card trigger, because the trigger's watchdog reported truthfully that the
        /// stage's camera never left its establishing shot, which it never did, because nothing was
        /// entering any state at all.
        ///
        /// Application.quitting is the right hook and not a convenience: it is raised on the way out
        /// of play mode in the editor as well as on a real quit, which is precisely the moment the
        /// value is about to be written down. It cannot cover the editor being killed outright — a
        /// crash or an End Task takes the process with it — which is what ReclaimAStoppedClock is
        /// for on the way back in.
        /// </summary>
        static void HandBackTheClock()
        {
            if (!_timeHeld) return;
            _timeHeld = false;
            if (SimClock.Scripted) return;
            // Never a zero, whatever was collected: this runs to make sure the value left behind is
            // one a game can start on.
            Time.timeScale = _timeScaleOnHold > 0f ? _timeScaleOnHold : 1f;
        }

        /// <summary>
        /// Refuse to begin a session on a stopped clock.
        ///
        /// The second half of <see cref="HandBackTheClock"/>, and the half that repairs the damage
        /// already done rather than preventing more: a TimeManager.asset that has ALREADY been
        /// written with a zero — by a session that was killed rather than quit, by the editor's own
        /// pause menu, by <c>DuckPlayTools.Pause</c>, or by a colleague's commit — starts every
        /// session with the world stopped and nothing on screen to say so.
        ///
        /// Unconditional, because at this point nothing has borrowed anything: the stack is empty,
        /// no popup exists, no scene has loaded, and a zero here cannot be anybody's deliberate
        /// state. It is stated loudly rather than repaired in silence, because the setting is on
        /// disk and will do this again on the next machine until somebody saves a 1 over it.
        ///
        /// Left alone under the capture harness, which owns the clock and steps the game by hand —
        /// the same rule ApplyGates, StageLauncher and GooseDefence.UpdateClock all follow.
        /// </summary>
        static void ReclaimAStoppedClock()
        {
            if (SimClock.Scripted) return;
            if (Time.timeScale > 0f) return;

            Debug.LogWarning("[PopupStack] this session started with Time.timeScale at zero, before " +
                             "anything had borrowed it. That is a stopped world: the opening story " +
                             "and the curtain would still play, because they run unscaled, but no " +
                             "round would ever start. It is almost certainly ProjectSettings/" +
                             "TimeManager.asset carrying a zero left behind by a session that was " +
                             "stopped with a popup open. Setting it to 1 — save the project settings " +
                             "with the clock at 1 to stop this recurring.");
            Time.timeScale = 1f;
        }

        /// <summary>
        /// The safety net under a popup that leaves its own scene.
        ///
        /// The polite path is PopAll before the load, and the pause menu's "back to the gate" button
        /// is expected to take it. This is what happens when something forgets: a stack still
        /// holding the clock at zero as a new scene opens is a game that never starts, and it is
        /// worth one event subscription to make that impossible rather than merely unlikely.
        /// </summary>
        static void OnActiveSceneChanged(Scene from, Scene to) => Abandon();

        /// <summary>
        /// Wipe the subsystem before the first scene of a session, for the reason
        /// <see cref="MatchState"/> gives at its own reset: statics are not reset by entering play
        /// mode when domain reloading is off, and this project's whole cross-scene mechanism is
        /// statics. Without this, a session that was stopped with the pause menu open would leave
        /// the NEXT one holding a stale popup, a locked mower and a stopped clock, on the front page,
        /// with nothing on screen to say why.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ResetForSession()
        {
            ReclaimAStoppedClock();
            Abandon();

            // The clock is handed back when the application ends, and this is not tidiness — it is
            // the one hole through which a transient pause became a permanent project setting. See
            // HandBackTheClock. Unsubscribed before subscribing for the same reason the scene event
            // below is: with domain reload off this is the same static delegate the last session
            // left behind.
            Application.quitting -= HandBackTheClock;
            Application.quitting += HandBackTheClock;

            // <see cref="PauseFactory"/> is deliberately NOT cleared here, and that is the single
            // most important line in this method. The pause popup registers itself from a
            // RuntimeInitializeOnLoadMethod of its own, and Unity gives no ordering guarantee
            // between two of those — clearing the factory here would, on whichever runs of the game
            // happen to order them the other way round, silently unregister the pause menu and leave
            // Escape doing nothing at all, intermittently, which is the worst kind of nothing.
            // Nothing is lost by keeping it: a delegate pointing at a static method describes no
            // world and cannot go stale the way a scene reference does.

            // Unsubscribe first. With domain reload off this delegate is the same static field the
            // last session left behind, and += without -= is how a handler ends up running four
            // times on the fourth play.
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;

            _beatInstalled = false;
            EnsureBeat();
        }

        // ------------------------------------------------------------------ the heartbeat

        // Two marker types, one per beat. The player loop identifies a system by its Type and shows
        // that name in the profiler, so these are named for what they do rather than left anonymous.
        struct PopupStackEarly { }
        struct PopupStackLate { }

        static bool _beatInstalled;

        /// <summary>
        /// Give the stack a heartbeat, by putting two entries into Unity's player loop.
        ///
        /// This is deliberately NOT a hidden DontDestroyOnLoad driver object, and the reason is
        /// ordering rather than tidiness. The stack needs to run in two different places in the same
        /// frame and no single execution order can provide both:
        ///
        ///   * BEFORE every Update, so it consumes Escape before GameDirector reads the same latch
        ///     and can tell it the edge is spent. GameDirector sits at -10.
        ///   * AFTER every LateUpdate, so its write to Time.timeScale and DrivingEnabled is the last
        ///     one of the frame. GameDirector's hit stop writes the clock from Update and
        ///     RallyDirector writes DrivingEnabled from its tick every frame; a lock applied before
        ///     them is a lock that does not hold.
        ///
        /// The player loop gives both exactly, by construction, with no attribute to get wrong. It
        /// also cannot be duplicated, cannot be destroyed by a scene load, and runs at
        /// Time.timeScale zero — which a stack whose entire job is to stop the clock rather depends
        /// on. And it needs no MonoBehaviour, which matters here for a dull practical reason: a
        /// MonoBehaviour has to live in a file named after itself, and this subsystem is two files.
        ///
        /// Idempotent. Called from Push as well as from the session reset, so a stack that is
        /// somehow pushed to before the reset runs still ticks.
        /// </summary>
        static void EnsureBeat()
        {
            if (_beatInstalled) return;

            var root = PlayerLoop.GetCurrentPlayerLoop();

            // Guard against the flag and the loop disagreeing — an editor session that reset the
            // loop without resetting the statics, mostly.
            if (Contains(root, typeof(PopupStackEarly)) && Contains(root, typeof(PopupStackLate)))
            {
                _beatInstalled = true;
                return;
            }

            var early = new PlayerLoopSystem
            {
                type = typeof(PopupStackEarly),
                updateDelegate = EarlyTick
            };
            var late = new PlayerLoopSystem
            {
                type = typeof(PopupStackLate),
                updateDelegate = LateTick
            };

            // First inside Update, which puts it ahead of ScriptRunBehaviourUpdate and therefore
            // ahead of every MonoBehaviour in the game. First inside PostLateUpdate, which is after
            // ScriptRunBehaviourLateUpdate and therefore after all of them.
            bool a = Insert(ref root, typeof(UnityEngine.PlayerLoop.Update), early, true);
            bool b = Insert(ref root, typeof(UnityEngine.PlayerLoop.PostLateUpdate), late, true);

            if (!a || !b)
            {
                Debug.LogError("[PopupStack] could not install its player-loop beat " +
                               $"(update:{a} postLate:{b}). Escape will not open the pause menu and " +
                               "any popup already open will never be stepped.");
                return;
            }

            PlayerLoop.SetPlayerLoop(root);
            _beatInstalled = true;
        }

        static void EarlyTick()
        {
            // Clamped for the same reason Curtain clamps: a browser tab regaining focus hands over a
            // quarter-second delta, and a popup animating through one of those arrives already over.
            Tick(Mathf.Min(Time.unscaledDeltaTime, 0.05f));
        }

        static void LateTick() => ApplyGates();

        static bool Contains(in PlayerLoopSystem system, Type type)
        {
            var kids = system.subSystemList;
            if (kids == null) return false;
            for (int i = 0; i < kids.Length; i++)
            {
                if (kids[i].type == type) return true;
                if (Contains(kids[i], type)) return true;
            }
            return false;
        }

        /// <summary>
        /// Put <paramref name="beat"/> inside the subsystem list of <paramref name="anchor"/>,
        /// wherever in the tree that anchor happens to be. Returns false if the anchor is not there,
        /// which would mean Unity's loop is not shaped the way this expects and is worth an error
        /// rather than a silent nothing.
        /// </summary>
        static bool Insert(ref PlayerLoopSystem parent, Type anchor, PlayerLoopSystem beat, bool atStart)
        {
            var kids = parent.subSystemList;
            if (kids == null) return false;

            for (int i = 0; i < kids.Length; i++)
            {
                if (kids[i].type == anchor)
                {
                    var grandkids = kids[i].subSystemList;
                    var list = grandkids == null
                        ? new List<PlayerLoopSystem>()
                        : new List<PlayerLoopSystem>(grandkids);
                    if (atStart) list.Insert(0, beat); else list.Add(beat);
                    kids[i].subSystemList = list.ToArray();
                    return true;
                }

                // Array elements are variables, so this recurses into the real struct rather than a
                // copy and the write above lands where it was meant to.
                if (Insert(ref kids[i], anchor, beat, atStart)) return true;
            }
            return false;
        }
    }
}
