using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using DuckMow.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;

namespace DuckMow
{
    /// <summary>
    /// THE ONLY THING IN THIS PROJECT ALLOWED TO TURN A CONTROLLER'S MOTORS ON.
    ///
    /// READ THIS BEFORE YOU CALL Gamepad.SetMotorSpeeds ANYWHERE ELSE: don't. There is exactly one
    /// call to that method in the whole game and it lives in <see cref="Drive"/>. The rule is the
    /// same one <see cref="MasterAudio"/> lays down about AudioListener.volume, and it is here for a
    /// worse reason than tidiness.
    ///
    /// A rumble is a LATCH, not a sound. AudioSource.Play ends by itself; SetMotorSpeeds(0.7f, 0.4f)
    /// spins the motors and leaves them spinning until somebody says otherwise. So every caller that
    /// starts one has taken on a promise to stop it, on every path out — including the paths it did
    /// not think about: the pause board, a scene load, the player alt-tabbing to another tab, the
    /// round ending mid-effect, the editor leaving play mode. Miss one and the player is left holding
    /// a pad that buzzes at a menu, or at the desktop. That is the single worst bug this feature can
    /// produce, it is not recoverable from inside the game, and it is the direct consequence of
    /// scattering SetMotorSpeeds calls around. With one writer it is one method's problem, and this
    /// file solves it once — see <see cref="StopAll"/> and the four hooks in <see cref="Boot"/>.
    ///
    /// ---- the shape ----
    ///
    /// Callers do not describe motor speeds. They name an EVENT — a bonk, a goose turned, a boost
    /// lit, a klaxon, a verdict — and this class decides what that feels like. That indirection is
    /// the whole point of having the file: it is the only way the effects stay a coherent vocabulary
    /// instead of nine different people's idea of "a bit of rumble", and it is the only place the
    /// relative weights can be tuned against each other rather than in isolation.
    ///
    /// Under the vocabulary is a tiny mixer. Each named effect queues one or two SHOTS — an
    /// amplitude on each motor, a delay, a duration and an envelope — and once a frame the live
    /// shots are combined, capped, and written to the pad. Layering is why a bonk and a klaxon feel
    /// like different things rather than like the same buzz at two lengths.
    ///
    /// ---- what it refuses to do ----
    ///
    /// It will not throw. Not on a missing pad, not on a NaN, not on a browser that has never heard
    /// of haptics. A rumble is garnish; a rumble that takes a frame down with it is worse than no
    /// rumble at all, so every entry point is total.
    ///
    /// It will not buzz forever. Shots are duration-capped, the sustained channel expires by itself
    /// a tenth of a second after the last call that fed it, the number of concurrent shots is
    /// bounded, and on top of all three there is a watchdog that forces silence if output somehow
    /// stays up past <see cref="MaxContinuous"/>. Four independent brakes, because the failure they
    /// prevent is the one the player cannot do anything about.
    ///
    /// It will not rumble a player who asked it not to. <see cref="Enabled"/> is persisted through
    /// PlayerPrefs on exactly the pattern MasterAudio established, debounced flush and all.
    ///
    /// ---- and the part that is genuinely awkward: WEBGL ----
    ///
    /// This game ships to a browser, and UNITY'S INPUT SYSTEM CANNOT RUMBLE IN A BROWSER. That is
    /// not a suspicion, it is documented in the exact package version this project resolves
    /// (com.unity.inputsystem@1.18.0, Documentation~/SupportedDevices.md: "WebGL currently doesn't
    /// support rumble.") and it is visible in the source: WebGLGamepad.cs declares a layout and an
    /// empty class body, with no ExecuteCommand override and nothing that answers a
    /// DualMotorRumbleCommand. Unity's WebGL gamepad support goes through emscripten's
    /// emscripten_get_gamepad_status, which is a READ of axes, buttons and mapping — emscripten
    /// exposes no vibration API at all, and Unity's WebGL runtime JavaScript contains no reference
    /// to vibrationActuator anywhere. So SetMotorSpeeds in a browser build sends a device command
    /// that nothing handles, gets a generic failure back, and does nothing. Silently. With no
    /// warning in the console. It works perfectly in the editor, which is exactly how a feature like
    /// this ships broken.
    ///
    /// The browser DOES have haptics; Unity just does not reach them. The Gamepad API's
    /// gamepad.vibrationActuator.playEffect("dual-rumble", ...) is real and good, so the WebGL build
    /// goes around Unity to it through Assets/Plugins/WebGL/DuckHaptics.jslib. Everything above this
    /// line is unchanged; only <see cref="Drive"/> forks. See <see cref="DriveWeb"/> for the two
    /// things that fork is obliged to do differently, both of which are consequences of playEffect
    /// being a promise-returning call with a duration rather than a level you hold.
    ///
    /// WHICH BROWSERS ACTUALLY BUZZ, plainly, so nobody is surprised: Chrome, Edge and other
    /// Chromium browsers on desktop do. Firefox and Safari historically do not implement
    /// vibrationActuator at all, and there the whole path is a checked no-op — see
    /// <see cref="Available"/>, which is exposed precisely so a settings screen can say so rather
    /// than offering a toggle that changes nothing.
    /// </summary>
    public static class Haptics
    {
        // ------------------------------------------------------------------ the vocabulary
        //
        // Every method here is a named EVENT, not a level. The numbers are tuned against each other
        // and are only meaningful as a set; changing one in isolation is how a game ends up where
        // the smallest event is the one you feel most.
        //
        // On a dual-motor pad the LOW motor is the big off-centre weight — a body blow, felt in the
        // palms — and the HIGH motor is the small one, a buzz felt in the fingers. Impacts are
        // mostly low with a high transient on the front; anything mechanical or electrical is
        // mostly high. That split is what makes six effects distinguishable through a glove.

        /// <summary>
        /// Something solid was hit. <paramref name="strength"/> is GameDirector's own impact
        /// figure — the same number it feeds to the camera shake — so a nudge and a full-speed
        /// collision with a gnome are not the same event wearing one amplitude.
        ///
        /// Two shots, and the order is the whole character of it: a two-frame high tick that arrives
        /// on the same frame as the sound, and a low thump under it that outlives the tick. A single
        /// low shot reads as mush, and a single high one reads as a phone notification.
        /// </summary>
        public static void Bonk(float strength)
        {
            float s = Clamp01(strength);
            if (s <= 0.02f) return;
            Queue(0f, 0f, 0.35f + 0.55f * s, 0.055f, 0f);
            Queue(0.30f + 0.60f * s, 0f, 0f, 0.15f + 0.10f * s, 0.005f);
        }

        /// <summary>
        /// A goose met the horn and turned. Deliberately the sharpest thing on the pad: it is the
        /// one event in the rally the player is trying to cause, and it has to be legible in a
        /// moment when the screen is busy and the crowd is loud.
        /// </summary>
        public static void GooseStrike()
        {
            Queue(0f, 0f, 0.90f, 0.05f, 0f);
            Queue(0.55f, 0f, 0.18f, 0.22f, 0.04f);
        }

        /// <summary>
        /// The boost has lit. A swell rather than a hit — it rises into the palm over a third of a
        /// second, because the thing it is describing is not an impact but a machine winding up.
        /// Nothing marks the END of the boost: a rumble that stops is not a feeling, and the engine
        /// note already covers it.
        /// </summary>
        public static void BoostStart()
        {
            Queue(0f, 0f, 0.40f, 0.07f, 0f);
            Queue(0.55f, 0.20f, 0.10f, 0.30f, 0.01f);
        }

        /// <summary>
        /// HELD, not an edge, and the only method here that expects to be called every frame.
        ///
        /// A drift is a state rather than an event, so it feeds a sustained channel instead of the
        /// shot queue: pass how hard the machine is sliding, once a frame, for as long as it slides.
        /// The channel expires <see cref="SustainHold"/> after the last call, which means a caller
        /// that stops calling — because the drift ended, or because the object was destroyed, or
        /// because the round did — cannot leave anything running. There is deliberately no
        /// StopDrift; a sustained rumble whose shutdown depends on somebody remembering a second
        /// call is the buzzing-pad bug with extra steps.
        /// </summary>
        public static void Drift(float amount)
        {
            float a = Clamp01(amount);
            if (a <= 0.05f) return;
            _sustainLow = Mathf.Max(_sustainLow, 0.10f + 0.16f * a);
            _sustainHigh = Mathf.Max(_sustainHigh, 0.05f * a);
            _sustainAge = 0f;
        }

        /// <summary>The drift paid off. A snap, so it is felt as a release rather than as more drift.</summary>
        public static void MiniTurbo()
        {
            Queue(0f, 0f, 0.75f, 0.06f, 0f);
            Queue(0.48f, 0f, 0.22f, 0.20f, 0.02f);
        }

        /// <summary>
        /// The klaxon: time is up. The longest and flattest thing in the vocabulary, because it is
        /// the only one describing something that is not a collision — it is a horn sounding across
        /// a showground, and it wants to feel like an announcement rather than like being hit.
        /// </summary>
        public static void Klaxon()
        {
            Queue(0.45f, 0.30f, 0.28f, 0.45f, 0f);
        }

        /// <summary>The player's own horn. Small — they pressed it, they know it happened.</summary>
        public static void Horn()
        {
            Queue(0f, 0f, 0.32f, 0.09f, 0f);
        }

        /// <summary>
        /// The judges have spoken. Two shapes, because the verdict is the one moment in the round
        /// where the player is being told something rather than doing something, and a good result
        /// and a bad one that feel identical waste the only chance the pad gets to editorialise.
        ///
        /// Favourable is two rising taps — an applause figure. Unfavourable is one long, low, flat
        /// press, which is the same gesture a disappointed room makes.
        /// </summary>
        public static void Verdict(bool favourable)
        {
            if (favourable)
            {
                Queue(0.30f, 0f, 0.35f, 0.09f, 0f);
                Queue(0.55f, 0f, 0.60f, 0.16f, 0.16f);
            }
            else
            {
                Queue(0.42f, 0.30f, 0.06f, 0.55f, 0f);
            }
        }

        // ------------------------------------------------------------------ the player's setting

        /// <summary>
        /// PlayerPrefs key, namespaced with the project prefix for the reason MasterAudio spells
        /// out: on WebGL, PlayerPrefs is one IndexedDB store keyed off the build path, which on a
        /// shared domain is genuinely a bucket other builds may have written into.
        /// </summary>
        const string KeyEnabled = "DuckMow.Haptics.Enabled";

        /// <summary>Seconds between real flushes. See <see cref="MarkDirty"/>; MasterAudio's rule.</summary>
        const float FlushInterval = 1f;

        static bool _enabled = true;
        static bool _dirty;
        static float _lastFlush = float.NegativeInfinity;

        /// <summary>
        /// Whether the pad is allowed to move at all. Persisted; survives scene loads and sessions.
        ///
        /// Defaults ON, and that is a considered default rather than a lazy one. Rumble is on by
        /// default on every console this game's controls are modelled after, the effects here are
        /// short and none of them is loud, and a feature that ships off is a feature almost nobody
        /// discovers. The players who genuinely cannot have it — a pad on a desk that rattles, a
        /// shared room, motion sensitivity — are the ones who go looking in settings, and the row
        /// is there for them.
        ///
        /// Turning it OFF stops the motors on the same frame rather than waiting for the current
        /// effect to run out. A toggle whose effect you have to wait for is a toggle the player
        /// presses twice.
        /// </summary>
        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (value == _enabled) return;
                _enabled = value;
                PlayerPrefs.SetInt(KeyEnabled, _enabled ? 1 : 0);
                MarkDirty();
                if (!_enabled) StopAll();
            }
        }

        /// <summary>
        /// Whether rumbling is physically possible right now: a pad is connected AND this platform
        /// can actually drive its motors.
        ///
        /// Exposed for the settings screen, and it earns its place there. On Firefox and Safari the
        /// browser has no vibrationActuator, so the honest thing for the board to show is a dulled
        /// row reading UNAVAILABLE rather than a switch the player can flip that will never do
        /// anything. A control that lies about what it controls is worse than a missing control —
        /// the player concludes the game is broken, and they are not entirely wrong.
        ///
        /// Re-checked periodically rather than answered once: a pad can be plugged in at any moment,
        /// and in a browser it does not even appear in navigator.getGamepads() until the player has
        /// pressed something on it.
        /// </summary>
        public static bool Available
        {
            get
            {
                if (Gamepad.current == null) return false;
#if UNITY_WEBGL && !UNITY_EDITOR
                return WebSupported;
#else
                return true;
#endif
            }
        }

        /// <summary>Read the stored setting. Called before the first scene loads; safe to call again.</summary>
        public static void Load()
        {
            _enabled = PlayerPrefs.GetInt(KeyEnabled, 1) != 0;
            _dirty = false;
        }

        /// <summary>
        /// Flush to storage, if there is anything to flush. A no-op otherwise, so it is free to call
        /// whenever a settings screen closes.
        ///
        /// The Set/Save split is MasterAudio's and the reasoning transfers unchanged: SetInt touches
        /// an in-memory dictionary and is free, PlayerPrefs.Save() is what goes to storage, and in
        /// WebGL that Save is an asynchronous FS.syncfs against IndexedDB which nobody wants queued
        /// once per keypress.
        /// </summary>
        public static void Save()
        {
            if (!_dirty) return;
            PlayerPrefs.Save();
            _dirty = false;
            _lastFlush = Now;
        }

        static void MarkDirty()
        {
            _dirty = true;
            if (Now - _lastFlush >= FlushInterval) Save();
        }

        /// <summary>
        /// Time.realtimeSinceStartup, NOT Time.time, and for MasterAudio's exact reason: this
        /// setting lives on a board that may be opened from the pause popup, which holds
        /// Time.timeScale at zero. A timeScale-based limiter would let one flush through and then
        /// never allow another for as long as the player stayed on the screen they were adjusting.
        /// </summary>
        static float Now => Application.isPlaying ? Time.realtimeSinceStartup : 0f;

        // ------------------------------------------------------------------ the mixer

        /// <summary>
        /// One queued effect component: a delay, a length, and an amplitude on each motor.
        ///
        /// A struct in a plain List rather than anything cleverer. There are never more than
        /// <see cref="MaxShots"/> of these alive and they are walked once a frame; a pool, an object
        /// or an event system here would be more machinery than the thing it drives.
        /// </summary>
        struct Shot
        {
            public float low, high;
            public float delay;
            public float duration;
            public float age;
        }

        /// <summary>
        /// How many shots may be alive at once.
        ///
        /// Small on purpose. Beyond about four overlapping effects a dual-motor pad has nothing left
        /// to say — the amplitudes saturate and every event feels like the same wall of buzz — so a
        /// bound here is not merely a safety valve, it is what keeps the vocabulary legible when
        /// three things happen at once. The oldest is dropped rather than the newest refused,
        /// because the newest is the one describing what just happened.
        /// </summary>
        const int MaxShots = 6;

        /// <summary>Nothing may ask for a single shot longer than this. See the class comment.</summary>
        const float MaxShotDuration = 1.2f;

        /// <summary>How long the sustained channel survives after the last call that fed it.</summary>
        const float SustainHold = 0.12f;

        /// <summary>
        /// The ceiling on everything, applied after mixing.
        ///
        /// Not 1.0, and not out of politeness. Full amplitude on both motors is unpleasant on every
        /// pad and it also flattens the dynamic range to nothing — if the ordinary bonk is already
        /// at the maximum, the big one has nowhere to go. Reserving the top fifteen percent means
        /// the loudest event in the game is still louder than the second loudest.
        /// </summary>
        const float Ceiling = 0.85f;

        /// <summary>
        /// The watchdog. If output has been continuously non-zero for this long, something is stuck
        /// and the motors are forced off.
        ///
        /// Every other brake in this file is a bound on a thing this file controls; this one is the
        /// bound on the bugs it does not know about yet — a caller feeding <see cref="Drift"/> from
        /// a loop that never ends, a shot list corrupted by something re-entering the mixer. Eight
        /// seconds is far longer than any legitimate effect and far shorter than the point at which
        /// a player would put the pad down and go looking for the power switch.
        /// </summary>
        const float MaxContinuous = 8f;

        static readonly List<Shot> _shots = new List<Shot>(MaxShots);

        static float _sustainLow, _sustainHigh, _sustainAge = SustainHold;

        static float _lastLow = -1f, _lastHigh = -1f;
        static float _liveFor;
        static bool _watchdogTripped;
        static bool _warned;

        /// <summary>
        /// Add one component of an effect. The only way anything gets into the mixer.
        ///
        /// Everything is clamped at the door rather than at the point it would bite. A NaN reaching
        /// SetMotorSpeeds is not the quiet nothing it is in most APIs — Mathf.Clamp passes NaN
        /// straight through, exactly as MasterAudio.Sane documents for Clamp01, and what arrives at
        /// the driver is undefined. Guarding here means every number downstream of this line is
        /// known finite, which is what lets the rest of the mixer be arithmetic rather than
        /// arithmetic plus checks.
        /// </summary>
        static void Queue(float low, float high, float highAlt, float duration, float delay)
        {
            // The three-amplitude signature is a small ugliness worth explaining: the vocabulary
            // above wants to write "a low thump" and "a high tick" without repeating a zero, so the
            // high motor is spelled either as an explicit second argument or as the alternate,
            // whichever the effect found readable. They are summed, so only one is ever non-zero.
            float l = Sane(low), h = Sane(high) + Sane(highAlt);
            if (l <= 0f && h <= 0f) return;

            float dur = Mathf.Clamp(Sane(duration), 0.01f, MaxShotDuration);
            float del = Mathf.Clamp(Sane(delay), 0f, MaxShotDuration);

            if (_shots.Count >= MaxShots) _shots.RemoveAt(0);
            _shots.Add(new Shot
            {
                low = Mathf.Min(l, 1f),
                high = Mathf.Min(h, 1f),
                delay = del,
                duration = dur,
                age = 0f
            });

            // A queued shot means the game is asking for something, so the watchdog's patience is
            // reset. Without this a player who drifts for nine seconds and then hits a gnome would
            // feel nothing, because the watchdog would still be holding the motors down.
            _watchdogTripped = false;
        }

        /// <summary>
        /// One frame of the mixer. Runs LAST in the frame — see <see cref="EnsureBeat"/>.
        ///
        /// Unscaled time throughout, without exception, and this is not a preference. The klaxon
        /// runs a hit stop at Time.timeScale 0.24 and the pause board holds it at zero; an effect
        /// integrated on the scaled clock would stretch to four times its length under the klaxon —
        /// which is precisely the moment the round is trying to punctuate — and would freeze
        /// mid-buzz behind a menu, which is the buzzing-pad bug arriving by a different door.
        /// </summary>
        static void Tick()
        {
            // Clamped for Curtain's and PopupStack's reason: a browser tab regaining focus hands
            // over a delta of a quarter second or more, and an effect integrated through one of
            // those arrives already finished — or, worse, a sustained channel ages past its hold in
            // a single step and stutters.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            float low = 0f, high = 0f;

            // ---- the shot queue ----
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                var s = _shots[i];
                s.age += dt;

                if (s.age >= s.delay + s.duration) { _shots.RemoveAt(i); continue; }
                _shots[i] = s;

                if (s.age < s.delay) continue;

                float t = (s.age - s.delay) / s.duration;
                float env = Envelope(t);
                // Combined as a soft OR rather than a sum. Two shots at 0.6 summed give 1.2, which
                // clamps to the ceiling and makes every pair of overlapping effects feel identical;
                // combined this way they give 0.84, which still leaves room above for a third.
                low = low + s.low * env - low * s.low * env;
                high = high + s.high * env - high * s.high * env;
            }

            // ---- the sustained channel ----
            _sustainAge += dt;
            if (_sustainAge >= SustainHold)
            {
                _sustainLow = 0f;
                _sustainHigh = 0f;
            }
            else
            {
                // Faded out across the hold rather than cut at the end of it, so a drift that ends
                // between two calls releases instead of stopping dead.
                float k = 1f - _sustainAge / SustainHold;
                low = low + _sustainLow * k - low * _sustainLow * k;
                high = high + _sustainHigh * k - high * _sustainHigh * k;
            }
            // Consumed. The channel is re-fed every frame by whoever is still drifting; anything
            // that stops calling stops rumbling, which is the whole contract of Drift.
            _sustainLow = 0f;
            _sustainHigh = 0f;

            low = Mathf.Clamp(low, 0f, 1f) * Ceiling;
            high = Mathf.Clamp(high, 0f, 1f) * Ceiling;

            // ---- the gates ----
            //
            // Ordered cheapest first, and every one of them means "write zero", not "skip the
            // write". Skipping would leave the last non-zero value spinning, which is the bug.
            //
            // PopupStack.Any is how this file knows the game is paused. It is deliberately not
            // Time.timeScale == 0 — the klaxon hit stop drops the clock to 0.24 and a curtain may
            // hold it lower still, and neither of those is a pause. The stack being non-empty is
            // the project's actual definition of "something is in front of the game", and it is the
            // same fact GameDirector and the mower are gated on.
            if (!_enabled || SimClock.Scripted || PopupStack.Any || !Application.isFocused)
            {
                low = 0f;
                high = 0f;
            }

            // ---- the watchdog ----
            if (low > 0.02f || high > 0.02f)
            {
                _liveFor += dt;
                if (_liveFor > MaxContinuous)
                {
                    if (!_watchdogTripped)
                    {
                        _watchdogTripped = true;
                        if (!_warned)
                        {
                            _warned = true;
                            Debug.LogWarning("[Haptics] the motors have been running continuously " +
                                             $"for over {MaxContinuous} s and have been forced off. " +
                                             "Something is feeding Haptics.Drift from a loop that " +
                                             "never ends, or queueing an effect every frame.");
                        }
                    }
                }
            }
            else
            {
                _liveFor = 0f;
                _watchdogTripped = false;
            }

            if (_watchdogTripped) { low = 0f; high = 0f; }

            Drive(low, high);
        }

        /// <summary>
        /// The shape of a shot over its life: a fast attack and a curved decay.
        ///
        /// A rectangular envelope — full amplitude for the duration, then nothing — is what a naive
        /// SetMotorSpeeds-plus-coroutine gives you, and it reads as a switch rather than as an
        /// impact, because a real impact has a front and a tail and no back edge at all. The attack
        /// is deliberately about two frames: motors have their own mechanical rise time and asking
        /// for a slower one just loses the transient entirely.
        /// </summary>
        static float Envelope(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 0f;
            const float attack = 0.12f;
            if (t < attack) return t / attack;
            float d = (t - attack) / (1f - attack);
            return (1f - d) * (1f - d);
        }

        // ------------------------------------------------------------------ the one writer

        /// <summary>
        /// Put the mixed level on the pad. THE ONLY PLACE IN THIS PROJECT THAT DOES.
        ///
        /// Deduplicated: a level within a hundredth of the last one written is not written again.
        /// On the native path that saves a device command per frame, which is small; on the browser
        /// path it is the difference between a smooth rumble and a stutter, because playEffect is a
        /// promise-returning call that CANCELS whatever is playing, and issuing one every frame
        /// means restarting the effect sixty times a second. Zero is always written through, because
        /// silence is the one value that must never be optimised away.
        /// </summary>
        static void Drive(float low, float high)
        {
            bool silent = low <= 0.001f && high <= 0.001f;
            if (!silent && Mathf.Abs(low - _lastLow) < 0.01f && Mathf.Abs(high - _lastHigh) < 0.01f)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                // The browser path still has to refresh, because what it issued was an effect with
                // an END, not a level being held. See DriveWeb.
                DriveWeb(low, high, false);
#endif
                return;
            }

            if (silent && _lastLow <= 0.001f && _lastHigh <= 0.001f) return;

            _lastLow = low;
            _lastHigh = high;

#if UNITY_WEBGL && !UNITY_EDITOR
            DriveWeb(low, high, true);
#else
            try
            {
                var pad = Gamepad.current;
                if (pad == null) return;
                pad.SetMotorSpeeds(low, high);
            }
            catch (Exception e)
            {
                // A pad that disconnects between the null check and the command throws from inside
                // the Input System, and the only correct response to a failed rumble is to carry on
                // without one. Logged once, because a pad in that state will do it every frame.
                if (!_warned) { _warned = true; Debug.LogWarning("[Haptics] " + e.Message); }
            }
#endif
        }

        /// <summary>
        /// Stop everything, at once, unconditionally. The method every hook in
        /// <see cref="Boot"/> calls, and the one worth reading if you are here because a pad is
        /// stuck buzzing.
        ///
        /// It clears the queue AND writes zero, in that order, and it writes zero even when the
        /// bookkeeping says the motors are already off. That last part is the whole point: this is
        /// the recovery path, so it may not trust the state that got the game into the situation it
        /// is recovering from.
        ///
        /// Safe to call from anywhere, at any time, including before the first scene has loaded and
        /// after the last one has gone.
        /// </summary>
        public static void StopAll()
        {
            _shots.Clear();
            _sustainLow = 0f;
            _sustainHigh = 0f;
            _sustainAge = SustainHold;
            _liveFor = 0f;
            _watchdogTripped = false;
            _lastLow = 0f;
            _lastHigh = 0f;

#if UNITY_WEBGL && !UNITY_EDITOR
            try { DuckHapticsStop(); } catch { }
            _webIssued = 0f;
            _webLow = -1f;
            _webHigh = -1f;
#else
            try
            {
                var pad = Gamepad.current;
                if (pad != null) pad.SetMotorSpeeds(0f, 0f);
            }
            catch { /* a pad that has already gone cannot be told to stop, and does not need to be */ }
#endif
        }

        // ------------------------------------------------------------------ the browser

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int DuckHapticsSupported();
        [DllImport("__Internal")] static extern void DuckHapticsPlay(float low, float high, int durationMs);
        [DllImport("__Internal")] static extern void DuckHapticsStop();

        /// <summary>
        /// How long each issued effect asks to run for, and how much of that is allowed to elapse
        /// before it is re-issued.
        ///
        /// These two numbers are the entire browser path and they exist because playEffect is not
        /// SetMotorSpeeds. SetMotorSpeeds is a LEVEL: you set it and it stays. playEffect is a
        /// PLAYBACK: it takes a duration, it ends by itself, and calling it again cancels whatever
        /// was running and starts over. So holding a continuous rumble in a browser means issuing
        /// overlapping effects on a timer — long enough that the promise churn stays low, and
        /// re-issued far enough before the end that there is no audible gap at the seam.
        ///
        /// Two hundred milliseconds re-issued at a hundred and twenty gives about eight calls a
        /// second instead of sixty, with an eighty millisecond overlap absorbing any scheduling
        /// jitter. It also means a build that crashes mid-effect leaves a pad buzzing for a fifth of
        /// a second rather than for ever, which is a genuine advantage of the playback model and
        /// worth saying out loud.
        /// </summary>
        const int WebEffectMs = 200;
        const float WebRefresh = 0.12f;

        static float _webIssued;
        static float _webLow = -1f, _webHigh = -1f;
        static float _webProbe = float.NegativeInfinity;
        static bool _webSupported;

        /// <summary>
        /// Whether this browser and this pad can actually do it, re-probed once a second.
        ///
        /// Cached rather than asked every frame because the answer requires
        /// navigator.getGamepads(), which allocates a fresh array on every call in Chromium. Not
        /// cached for ever because a pad can be plugged in at any moment — and in a browser it does
        /// not even appear in that array until the player has pressed a button on it, so the first
        /// probe of a session is very likely to say no on a machine that will shortly say yes.
        /// </summary>
        static bool WebSupported
        {
            get
            {
                float now = Application.isPlaying ? Time.realtimeSinceStartup : 0f;
                if (now - _webProbe < 1f) return _webSupported;
                _webProbe = now;
                try { _webSupported = DuckHapticsSupported() != 0; }
                catch { _webSupported = false; }
                return _webSupported;
            }
        }

        /// <summary>
        /// Issue or refresh the browser effect. <paramref name="changed"/> is true when the mixed
        /// level actually moved, which forces an immediate re-issue so a transient is not delayed by
        /// up to <see cref="WebRefresh"/> — the one place where the timer must lose.
        /// </summary>
        static void DriveWeb(float low, float high, bool changed)
        {
            if (!WebSupported) return;

            float now = Application.isPlaying ? Time.realtimeSinceStartup : 0f;

            if (low <= 0.001f && high <= 0.001f)
            {
                if (_webLow <= 0.001f && _webHigh <= 0.001f) return;
                try { DuckHapticsStop(); } catch { }
                _webLow = 0f; _webHigh = 0f; _webIssued = 0f;
                return;
            }

            if (!changed && now - _webIssued < WebRefresh) return;

            try { DuckHapticsPlay(low, high, WebEffectMs); } catch { }
            _webLow = low; _webHigh = high; _webIssued = now;
        }
#endif

        // ------------------------------------------------------------------ arithmetic hygiene

        static float Sane(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return v < 0f ? 0f : v;
        }

        static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            if (v < 0f) return 0f;
            return v > 1f ? 1f : v;
        }

        // ------------------------------------------------------------------ getting there first

        /// <summary>
        /// Load the setting, install the heartbeat, and — the part that matters — subscribe the
        /// three hooks that stop the motors when the game stops being in front of the player.
        ///
        /// Every one of these is a path on which a naive implementation leaves a pad buzzing:
        ///
        ///   FOCUS LOSS   the browser tab goes to the background, or the player alt-tabs. The game
        ///                keeps running at a reduced rate in most browsers, so nothing else would
        ///                ever notice, and the player is now looking at a different window holding a
        ///                pad that will not stop. The mixer also gates on Application.isFocused
        ///                every frame; this hook is what makes the stop IMMEDIATE rather than one
        ///                frame later, which on a backgrounded tab can be a second away.
        ///   SCENE CHANGE a round ends mid-effect and the next scene loads. Nothing in the new scene
        ///                knows an effect was running, and the shot that was in flight belongs to a
        ///                world that no longer exists.
        ///   QUIT         the honest one. Unity does not reset motor speeds on shutdown on every
        ///                platform, and a pad left spinning as the process exits stays spinning.
        ///
        /// Every subscription does a defensive unsubscribe first, for PopupStack's and MasterAudio's
        /// reason: with domain reloading disabled these static delegate lists survive between play
        /// sessions and += without -= is how a handler ends up running four times on the fourth play.
        ///
        /// BeforeSceneLoad rather than AfterSceneLoad so the setting is read before anything can
        /// queue an effect. Cheap: it reads one PlayerPrefs int and touches no scene object.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            // Statics are not cleared by entering play mode with domain reloading off — the trap
            // MatchState and PopupStack both document — so a session stopped mid-effect would leave
            // the next one starting with shots in the queue and a watchdog already counting.
            StopAll();
            _warned = false;
            Load();

            Application.focusChanged -= OnFocusChanged;
            Application.focusChanged += OnFocusChanged;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
            SceneManager.activeSceneChanged -= OnSceneChanged;
            SceneManager.activeSceneChanged += OnSceneChanged;

            _beatInstalled = false;
            EnsureBeat();
        }

        static void OnFocusChanged(bool focused)
        {
            if (focused) return;
            StopAll();
            // The setting rides along on the same event MasterAudio flushes on, and for the same
            // reason: a browser tab is closed far more often than a desktop game is quit, and
            // Application.quitting is not something a page can rely on.
            Save();
        }

        static void OnQuitting()
        {
            StopAll();
            Save();
        }

        static void OnSceneChanged(Scene from, Scene to) => StopAll();

        // ------------------------------------------------------------------ the heartbeat

        /// <summary>Marker type, so the profiler names this entry rather than showing it blank.</summary>
        struct HapticsBeat { }

        static bool _beatInstalled;

        /// <summary>
        /// Give the mixer a frame, by putting one entry at the END of Unity's PostLateUpdate.
        ///
        /// The player loop rather than a hidden DontDestroyOnLoad driver, and the reasoning is
        /// PopupStack's, which this borrows wholesale — it cannot be duplicated, cannot be destroyed
        /// by a scene load, and RUNS AT Time.timeScale ZERO, which a subsystem whose job includes
        /// going quiet behind a pause board rather depends on. A coroutine, which is the obvious
        /// alternative for something with a duration, does none of those three.
        ///
        /// LAST in PostLateUpdate specifically, which puts it after PopupStack's own late entry —
        /// that one is inserted at the FRONT of the same list. That ordering is load-bearing: the
        /// stack applies its pause gate there, and a mixer that read PopupStack.Any before the stack
        /// had settled would be answering last frame's question about whether the game is paused.
        ///
        /// The insertion helpers below are a deliberate duplication of PopupStack's. They are
        /// private there, this is thirty lines, and a shared utility file would have to be owned by
        /// somebody — the same trade PopupView documents when it copies MainMenu's spring.
        /// </summary>
        static void EnsureBeat()
        {
            if (_beatInstalled) return;

            var root = PlayerLoop.GetCurrentPlayerLoop();

            if (Contains(root, typeof(HapticsBeat)))
            {
                _beatInstalled = true;
                return;
            }

            var beat = new PlayerLoopSystem
            {
                type = typeof(HapticsBeat),
                updateDelegate = Beat
            };

            if (!Insert(ref root, typeof(UnityEngine.PlayerLoop.PostLateUpdate), beat))
            {
                // Not fatal — the game plays fine without haptics, which is exactly why this has to
                // be said out loud. A silent failure here is a feature that simply never happens and
                // nothing anywhere to say why.
                Debug.LogError("[Haptics] could not install its player-loop beat. Rumble is off for " +
                               "this session; nothing else is affected.");
                return;
            }

            PlayerLoop.SetPlayerLoop(root);
            _beatInstalled = true;
        }

        /// <summary>
        /// The frame, wrapped. Nothing about a rumble is worth a thrown exception in the player
        /// loop — a throw from here is not a spoiled frame, it is a spoiled frame every frame for
        /// the rest of the session, because the loop entry keeps being called.
        /// </summary>
        static void Beat()
        {
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogException(e);
                    Debug.LogError("[Haptics] the mixer threw and the motors have been stopped. " +
                                   "Rumble is off for the rest of this session.");
                }
                StopAll();
                _enabled = false;
            }
        }

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

        static bool Insert(ref PlayerLoopSystem parent, Type anchor, PlayerLoopSystem beat)
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
                    list.Add(beat);
                    kids[i].subSystemList = list.ToArray();
                    return true;
                }

                // Array elements are variables, so this recurses into the real struct rather than a
                // copy and the write above lands where it was meant to.
                if (Insert(ref kids[i], anchor, beat)) return true;
            }
            return false;
        }
    }
}
