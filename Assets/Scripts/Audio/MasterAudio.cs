using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The one and only owner of <see cref="AudioListener.volume"/>.
    ///
    /// READ THIS BEFORE YOU WRITE AudioListener.volume ANYWHERE ELSE: don't. There is exactly one
    /// assignment to that field in the entire project and it lives in <see cref="Apply"/>. Every
    /// other system that wants the game quieter says so by moving one of the two inputs below —
    /// <see cref="Master"/> for "the player asked for this" and <see cref="Duck"/> for "something is
    /// temporarily holding the game down" — and this class decides what the listener ends up at.
    ///
    /// The reason for the rule is the bug it prevents, and that bug was already latent here. A
    /// component that ducks by CAPTURING AudioListener.volume, scaling it, and restoring the capture
    /// later is correct only for as long as it is the only writer. The moment a settings slider
    /// exists, the capture becomes a time machine: the player pauses at 100 %, slides the master
    /// down to 20 % on the pause board, unpauses, and the popup faithfully restores the 100 % it
    /// remembered from before they touched anything. The setting appears to have been ignored, or
    /// worse, to work and then silently revert. Two writers of one field cannot be made safe by
    /// being careful; they can only be made safe by there being one writer.
    ///
    /// So the split is:
    ///     AudioListener.volume = Master * Duck        (and flat 0 while Muted)
    /// where Master is persistent and belongs to the player, and Duck is transient and belongs to
    /// whatever is currently on top of the game. They multiply because they are answers to
    /// independent questions — a pause board ducking to 30 % means 30 % of whatever the player chose,
    /// not 30 % of full, and a player on 20 % who opens the pause menu should hear 6 %, not 30 %.
    ///
    /// WHY NOT AN AudioMixer. Because there isn't one. There is no .mixer asset anywhere in this
    /// project and nothing references UnityEngine.Audio at all; every "bus" on
    /// <see cref="AudioDirector"/> (masterEngine, masterBlade, masterAmbience, masterMusic,
    /// masterSfx, crowdMurmur) is a plain float multiplied into AudioSource.volume by hand, once a
    /// frame. AUDIO_SPEC.md §5 sketches the mixer tree that was meant to exist and §4 even budgets
    /// the +10.5 dB its master was supposed to add back — none of it was built. With no mixer there
    /// is no AudioExposedParameter to SetFloat, and AudioListener.volume is the only global lever the
    /// engine actually gives you. That is a limitation, not a design; see the note on
    /// <see cref="DefaultMaster"/> for what it costs us.
    ///
    /// Static, and deliberately not a MonoBehaviour, for the same reason as
    /// <see cref="MatchState"/>: a scene load destroys components, and the master volume must
    /// survive the front page becoming a round becoming a stage becoming a retry. A
    /// DontDestroyOnLoad carrier would be a component alive in two scenes at once, which is how you
    /// end up with two of them disagreeing about how loud the game is.
    /// </summary>
    public static class MasterAudio
    {
        // ------------------------------------------------------------------ the stored settings

        /// <summary>
        /// Where the master sits when the player has never touched it: all the way up.
        ///
        /// This is not laziness about picking a polite default, it is the only honest number
        /// available. AUDIO_SPEC.md §4 shifted every single clip's authored level down by a uniform
        /// −10.5 dB so that no AudioSource would ever need a volume above 1.0, on the explicit
        /// understanding that a mixer master would put those 10.5 dB back. That mixer was never
        /// built (see the class comment), so the game as it ships is already running about 10.5 dB
        /// under its intended absolute loudness. There is no headroom left to spend on a courteous
        /// 0.8 default — starting below 1.0 would just mean a game most players find too quiet and
        /// have to go and fix, which is exactly the friction a default is supposed to remove.
        ///
        /// If the mixer ever gets built and the 10.5 dB comes back, revisit this number, because at
        /// that point 1.0 stops being "as loud as intended" and starts being "as loud as possible".
        /// </summary>
        public const float DefaultMaster = 1f;

        /// <summary>
        /// PlayerPrefs keys. Namespaced with the project prefix on purpose.
        ///
        /// PlayerPrefs is a flat global bucket shared by everything in the player — on Windows it is
        /// a registry key per company/product, and in WebGL it is one IndexedDB store per build path,
        /// which on a shared domain can genuinely be a bucket several unrelated builds have written
        /// into. A bare key called "master" or "volume" is a collision waiting for someone else's
        /// build, and a collision here reads as "the game randomly forgot my settings".
        ///
        /// These are also the first two PlayerPrefs entries this project has ever written. If a save
        /// system arrives later, it should keep this prefix convention rather than inventing a
        /// second one.
        /// </summary>
        const string KeyMaster = "DuckMow.Audio.Master";
        const string KeyMuted = "DuckMow.Audio.Muted";

        /// <summary>
        /// Smallest master change worth reacting to.
        ///
        /// Exists so that a UI which assigns <see cref="Master"/> every frame from a slider — which
        /// is how every slider in every engine is written — does not mark the settings dirty sixty
        /// times a second while the player's finger is still on it. Below this the assignment is
        /// treated as "the same value again" and costs nothing. It is small enough that no step a
        /// human can perceive is ever swallowed by it.
        /// </summary>
        const float Epsilon = 1e-4f;

        /// <summary>
        /// Seconds between actual disk flushes while a value is being dragged around.
        ///
        /// See <see cref="MarkDirty"/> for why this is a rate limit rather than a trailing debounce.
        /// </summary>
        const float FlushInterval = 1f;

        /// <summary>Below this the master counts as silence for the purposes of the mute toggle.</summary>
        const float SilenceFloor = 0.001f;

        static float _master = DefaultMaster;
        static float _duck = 1f;
        static bool _muted;

        /// <summary>
        /// The level the mute toggle will hand back if the player has since dragged the master to
        /// zero. Only used to stop the toggle becoming a button that visibly does nothing — see the
        /// <see cref="Muted"/> setter.
        /// </summary>
        static float _beforeMute = DefaultMaster;

        /// <summary>True when PlayerPrefs holds a value we have not yet asked it to flush.</summary>
        static bool _dirty;

        /// <summary>Unscaled clock reading of the last real flush. Starts far enough in the past
        /// that the first change the player makes is written through immediately.</summary>
        static float _lastFlush = float.NegativeInfinity;

        // ------------------------------------------------------------------ the public dials

        /// <summary>
        /// What the player asked for, 0..1. Persisted; survives scene loads and sessions.
        ///
        /// Assigning re-applies immediately, so a slider can simply write to this every frame and
        /// the game gets quieter under the player's hand with no separate Apply call to forget.
        ///
        /// Raising this above zero while muted also clears the mute. Dragging a volume slider upward
        /// is an unambiguous request to hear something, and a slider that moves while the game stays
        /// silent is indistinguishable from a broken slider.
        /// </summary>
        public static float Master
        {
            get => _master;
            set
            {
                float v = Sane(value, DefaultMaster);
                bool unmuting = _muted && v > SilenceFloor;
                bool moved = Mathf.Abs(v - _master) > Epsilon;
                if (!unmuting && !moved) return;

                if (moved)
                {
                    _master = v;
                    PlayerPrefs.SetFloat(KeyMaster, _master);
                }
                if (unmuting)
                {
                    _muted = false;
                    PlayerPrefs.SetInt(KeyMuted, 0);
                }

                MarkDirty();
                Apply();
            }
        }

        /// <summary>
        /// A transient multiplier on top of the player's setting, 0..1. NOT persisted, by design.
        ///
        /// This is where the pause board, a cutscene, a wipe or anything else that wants the world
        /// held down for a moment expresses itself. It is deliberately a plain multiplier with no
        /// stack and no reference counting: if two things ever need to duck at once, the second one
        /// to release will stomp the first, and the fix at that point is a proper stack here — not a
        /// second system that also writes AudioListener.volume behind this one's back.
        ///
        /// It is not persisted because a ducked value is never a fact about the player; it is a fact
        /// about a menu that is currently open. Saving it would mean a game that crashed with the
        /// pause board up reopens at 30 % volume forever, with no UI anywhere that admits why.
        /// </summary>
        public static float Duck
        {
            get => _duck;
            set
            {
                float v = Sane(value, 1f);
                if (Mathf.Abs(v - _duck) <= Epsilon) return;
                _duck = v;
                Apply();
            }
        }

        /// <summary>
        /// Silence, without throwing away the level to come back to.
        ///
        /// Mute is a flag rather than "set Master to 0" precisely so that unmuting is possible at
        /// all. Storing the mute as a zero would make the toggle destructive: press twice and the
        /// player is left at silence with no memory of where they were, which is the single most
        /// common way this control is got wrong.
        ///
        /// The one awkward case is a player who mutes and THEN drags the master to zero. Unmuting
        /// would then be a no-op — a button that lights up and changes nothing — so this hands back
        /// the level the mute was taken at, or the default if that was silence too. A toggle must
        /// always audibly do something in at least one of its two directions.
        /// </summary>
        public static bool Muted
        {
            get => _muted;
            set
            {
                if (value == _muted) return;

                if (value)
                {
                    if (_master > SilenceFloor) _beforeMute = _master;
                    _muted = true;
                }
                else
                {
                    _muted = false;
                    if (_master <= SilenceFloor)
                    {
                        _master = _beforeMute > SilenceFloor ? _beforeMute : DefaultMaster;
                        PlayerPrefs.SetFloat(KeyMaster, _master);
                    }
                }

                PlayerPrefs.SetInt(KeyMuted, _muted ? 1 : 0);
                MarkDirty();
                Apply();
            }
        }

        /// <summary>
        /// What is actually reaching the listener right now. For UI readout and for tests.
        ///
        /// Exposed because a settings screen that wants to show a percentage should show the number
        /// the game is genuinely at, not re-derive it from Master and get it wrong the first time
        /// something ducks. This is the same expression <see cref="Apply"/> writes, and there is only
        /// one copy of it for that reason.
        /// </summary>
        public static float Effective => _muted ? 0f : Mathf.Clamp01(_master * _duck);

        // ------------------------------------------------------------------ doing it

        /// <summary>
        /// The single assignment to AudioListener.volume in this project. Keep it that way.
        ///
        /// Called automatically by every setter above, so ordinary callers never need it. It stays
        /// public for the one case that does: something that has just restored a whole pile of state
        /// at once and wants to be certain the listener agrees with it.
        /// </summary>
        public static void Apply()
        {
            AudioListener.volume = Effective;
        }

        /// <summary>
        /// Step the master by delta and clamp, for menus driven by arrow keys.
        ///
        /// Every menu in this project is polled keyboard-and-mouse rather than built out of UI
        /// events, so a volume row is going to be nudged by ±0.05 or ±0.1 a keypress rather than
        /// dragged. Repeated float addition of 0.1 does not land on 0.5 or on 1.0 — it lands a few
        /// ulps off and stays there, and a settings screen that reads "99 %" after being taken all
        /// the way up looks broken in a way that is very hard to argue with. So the result is snapped
        /// to a thousandth, which is finer than any step a menu will ever use and coarse enough to
        /// swallow the drift.
        /// </summary>
        public static void Nudge(float delta)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta == 0f) return;
            Master = Mathf.Round(Mathf.Clamp01(_master + delta) * 1000f) / 1000f;
        }

        // ------------------------------------------------------------------ the control's-eye view

        /// <summary>
        /// The power between where a volume control SITS and the amplitude underneath it.
        ///
        /// Loudness is not linear in amplitude. A control that maps its travel straight onto
        /// AudioListener.volume spends its top half doing almost nothing audible and its bottom
        /// eighth doing everything, which is a control that only works at the bottom. A power of 2.5
        /// puts the perceived change roughly where the hand is.
        ///
        /// ---- and why it lives HERE rather than on a menu ----
        ///
        /// Because there are two menus now. This was private to MainMenu while the front page was
        /// the only place a player could touch the volume; the pause board has a settings page too,
        /// and two screens with two curves would print two different percentages for the same
        /// amplitude — the player would set 70% on one board, open the other, and be told they were
        /// at 50%. Neither reading would be wrong and the game would still be lying.
        ///
        /// The screens keep their own POSITION and their own drift guard, which are genuinely
        /// theirs: a bar has to remember where it was put so a press near the bottom of a 2.5-power
        /// curve does not make the readout jump. What they share is the mapping, which is the only
        /// part that has to agree.
        /// </summary>
        public const float ControlCurve = 2.5f;

        /// <summary>
        /// How far one press of a direction moves a volume control, in POSITION.
        ///
        /// A twentieth, so a full sweep is twenty presses and every stop is a round number on
        /// screen. Shared for the same reason the curve is: two boards stepping by different amounts
        /// would disagree about what "one press" means the moment anybody counted.
        /// </summary>
        public const float ControlStep = 0.05f;

        /// <summary>The amplitude a control sitting at <paramref name="position"/> asks for.</summary>
        public static float AmplitudeAt(float position)
            => Mathf.Pow(Mathf.Clamp01(position), ControlCurve);

        /// <summary>Where a control has to sit to be asking for <paramref name="amplitude"/>.</summary>
        public static float PositionAt(float amplitude)
            => Mathf.Pow(Mathf.Clamp01(amplitude), 1f / ControlCurve);

        // ------------------------------------------------------------------ persistence

        /// <summary>
        /// Read the stored setting. Called for you before the first scene loads; safe to call again.
        ///
        /// Also resets <see cref="Duck"/> to 1, which matters more than it looks. Statics are not
        /// cleared by entering play mode when domain reloading is off — the same trap MatchState
        /// documents at length — so without this, an editor session that was stopped with the pause
        /// board up would start the NEXT session ducked to 30 %, with no popup on screen and nothing
        /// that will ever put it back. A transient value must be re-zeroed at the point the session
        /// begins, not merely at the point it is released.
        /// </summary>
        public static void Load()
        {
            _master = Sane(PlayerPrefs.GetFloat(KeyMaster, DefaultMaster), DefaultMaster);
            _muted = PlayerPrefs.GetInt(KeyMuted, 0) != 0;
            _beforeMute = _master > SilenceFloor ? _master : DefaultMaster;
            _duck = 1f;
            _dirty = false;
        }

        /// <summary>
        /// Flush the setting to disk, if there is anything to flush.
        ///
        /// The split that makes this cheap: PlayerPrefs.SetFloat/SetInt only touch an in-memory
        /// dictionary and are free to call on every keypress, while PlayerPrefs.Save() is the call
        /// that actually goes to storage. The setters above therefore always Set (so the values are
        /// correct the instant they change, and any later flush by anyone persists them) and only
        /// sometimes Save.
        ///
        /// IN WEBGL, WHICH IS THIS GAME'S SHIPPING TARGET, this matters more than on desktop.
        /// PlayerPrefs there is backed by IndexedDB through Emscripten's virtual filesystem, and
        /// PlayerPrefs.Save() triggers an asynchronous FS.syncfs — the call returns before the data
        /// is anywhere durable, and calling it on every frame of a drag queues sync after sync
        /// against a browser database for no benefit. Hence the rate limit in
        /// <see cref="MarkDirty"/> and the belt-and-braces flush on focus loss below: a browser tab
        /// is closed far more often than a desktop game is quit, and Application.quitting is not
        /// something you can rely on there.
        ///
        /// A no-op when nothing changed. Call it freely — on closing a settings screen, for instance.
        /// </summary>
        public static void Save()
        {
            if (!_dirty) return;
            PlayerPrefs.Save();
            _dirty = false;
            _lastFlush = Now;
        }

        /// <summary>
        /// Note that PlayerPrefs is ahead of storage, and flush if we have not flushed recently.
        ///
        /// A rate limit rather than a trailing debounce, because a trailing debounce needs something
        /// to tick it and this class has no Update — it is a static with no GameObject, on purpose.
        /// The trade is that the FIRST change in a burst is written immediately and the last one may
        /// wait; the tail is covered by the focus/quit hooks and by anyone calling
        /// <see cref="Save"/> when a settings screen closes. Since the value is already correct in
        /// PlayerPrefs' memory the whole time, nothing is at risk except the very last edit before
        /// an abrupt kill.
        ///
        /// Time.realtimeSinceStartup, NOT Time.time, and this is not a stylistic preference. The
        /// volume control will live on the pause board, and the pause board sets Time.timeScale to
        /// zero — Time.time stops advancing entirely while it is up, so a timeScale-based limiter
        /// would let through exactly one flush and then never allow another for as long as the
        /// player stayed in the menu they were adjusting.
        /// </summary>
        static void MarkDirty()
        {
            _dirty = true;
            if (Now - _lastFlush >= FlushInterval) Save();
        }

        static float Now => Application.isPlaying ? Time.realtimeSinceStartup : 0f;

        // ------------------------------------------------------------------ getting there first

        /// <summary>
        /// Load and apply before a single scene has opened, let alone played anything.
        ///
        /// BeforeSceneLoad rather than AfterSceneLoad because the bug this exists to prevent is a
        /// timing bug: a player who set the game to 20 % because they are listening to something
        /// else, opening the tab and being hit with a half second of full-volume brass before the
        /// setting is applied. Once is enough for them to never trust the control again. Awake on
        /// the first AudioDirector is already too late — its ambience beds start in its own Awake,
        /// and menu music can start on the first frame.
        ///
        /// The focus and quit hooks are subscribed here too, and subscribed with a defensive
        /// unsubscribe first: with domain reloading disabled these static event lists and our own
        /// statics do not necessarily survive or die together, and double-subscribing would mean
        /// double-flushing on every tab switch for the rest of the session.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            Load();
            Apply();

            Application.focusChanged -= OnFocusChanged;
            Application.focusChanged += OnFocusChanged;
            Application.quitting -= Save;
            Application.quitting += Save;
        }

        /// <summary>Losing focus is the closest thing a browser tab gives us to a warning.</summary>
        static void OnFocusChanged(bool focused)
        {
            if (!focused) Save();
        }

        // ------------------------------------------------------------------ arithmetic hygiene

        /// <summary>
        /// Clamp to 0..1 and refuse to let a NaN through.
        ///
        /// Mathf.Clamp01 does NOT handle this on its own, and the way it fails is quiet. It is
        /// written as a pair of comparisons — if (v &lt; 0) return 0; if (v &gt; 1) return 1; return
        /// v — and every comparison against NaN is false, so Clamp01(NaN) returns NaN. Assign that
        /// to AudioListener.volume and the entire game goes silent with no error, no warning and
        /// nothing in the console, and the only symptom is that sound stopped. A single division by
        /// a zero-width slider track upstream is all it takes to get here, so the guard lives at the
        /// boundary where the number arrives rather than at the place it would eventually bite.
        /// </summary>
        static float Sane(float v, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return fallback;
            if (v < 0f) return 0f;
            return v > 1f ? 1f : v;
        }
    }
}
