using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckMow
{
    /// <summary>
    /// Thin polling layer over the Input System's low-level devices. Deliberately not an
    /// .inputactions asset: the control set is tiny, and polling devices directly is one less
    /// thing to break in a WebGL build.
    ///
    /// ---- THE WHOLE CONTROL SET, IN ONE PLACE ----
    ///
    /// Written down because it is the only complete copy. ControlsPrimer teaches the keyboard, per
    /// stage and only the keys that stage honours; nothing anywhere teaches the pad. Anyone adding
    /// a verb should add a row here and then go and ask whether the primer needs one too.
    ///
    ///   VERB          KEYBOARD              GAMEPAD                        GATED BY
    ///   steer         A/D, arrows           left stick X, D-pad L/R        DrivingEnabled
    ///   throttle      W/S, arrows           triggers, D-pad U/D            DrivingEnabled
    ///   handbrake     Space                 A / Cross                      DrivingEnabled
    ///   boost         Shift                 RB / R1                        DrivingEnabled
    ///   horn          E, Q                  X / Square                     RoundActionsEnabled
    ///   air check     F                     LB / L1                        RoundActionsEnabled
    ///   glance back   C (held)              right stick click (held)       nothing, on purpose
    ///   retry         R                     Y / Triangle                   nothing
    ///   confirm       Enter, Space          A / Cross                      nothing
    ///   pause         Escape                MENU / OPTIONS                 nothing
    ///
    /// B / Circle is deliberately unbound in a round. It is the pad's cancel everywhere else in the
    /// game — the front page uses it to close a card — and a cancel that does something else while
    /// driving is the one binding a player cannot learn.
    ///
    /// The pad's D-PAD is read here for driving and read again by the menus (MainMenu.HandleInput
    /// and PopupView.HandleInput both walk their rows with it). Those two never overlap: a menu is a
    /// popup, and a popup blocks driving.
    /// </summary>
    public class InputReader : MonoBehaviour
    {
        static InputReader _instance;

        /// <summary>Lazily resolved for the same reason as CutMask.Instance.</summary>
        public static InputReader Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<InputReader>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Range(0f, 30f)] public float steerSmoothing = 11f;
        [Range(0f, 30f)] public float throttleSmoothing = 14f;

        public float Steer { get; private set; }
        public float Throttle { get; private set; }
        public bool Handbrake { get; private set; }
        public bool Boost { get; private set; }
        public bool BoostPressed { get; private set; }
        public bool HornPressed { get; private set; }
        /// <summary>Held to look over the machine's shoulder. C on the keyboard, right stick click.</summary>
        public bool LookBack { get; private set; }
        public bool RetryPressed { get; private set; }
        /// <summary>
        /// Escape, or the pad's MENU / OPTIONS button. What opens the pause board — see
        /// <see cref="DuckMow.UI.PopupStack"/>, which reads this latch and owns the key from the
        /// moment anything is open.
        ///
        /// The pad binding is <see cref="Gamepad.startButton"/>, which is the Menu button on an Xbox
        /// controller and Options on a DualShock/DualSense, and it took confirm's place there. See
        /// the block in <see cref="Tick"/> for why that trade was the right way round.
        /// </summary>
        public bool MenuPressed { get; private set; }
        public bool AnyConfirmPressed { get; private set; }
        /// <summary>The one-per-round lift. F rather than Tab, which the browser steals for focus.</summary>
        public bool AerialPressed { get; private set; }

        /// <summary>Set false during countdown, klaxon and results so the mower ignores input.</summary>
        public bool DrivingEnabled { get; set; } = true;

        /// <summary>
        /// Whether the ROUND's own verbs are live: the horn on E and the overhead check on F. True
        /// everywhere by default, so nothing about the mowing round changes.
        ///
        /// It exists because a stage played in its own scene keeps the player's real input pipeline
        /// — that is the point of it, so the machine handles identically wherever they are driving —
        /// and inherits every key bound to the round along with it. Bloom Rush has no horn to sound
        /// and no picture to check from the air, and a key that silently does something in one stage
        /// and nothing in the next is worse than a key that does nothing in both.
        ///
        /// Driving is deliberately NOT in here. Steering, throttle, handbrake and boost are the
        /// machine, and the machine is the same machine everywhere.
        /// </summary>
        public bool RoundActionsEnabled { get; set; } = true;

        /// <summary>When set, the autopilot supplies the driving inputs instead of the player.</summary>
        public bool OverrideActive { get; set; }

        float _ovSteer, _ovThrottle;
        bool _ovHandbrake, _ovBoost;

        public void SetOverride(float steer, float throttle, bool handbrake, bool boost)
        {
            _ovSteer = steer; _ovThrottle = throttle;
            _ovHandbrake = handbrake; _ovBoost = boost;
        }

        float _rawSteer, _rawThrottle;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (SimClock.Scripted) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;

            float steerRaw = 0f, throttleRaw = 0f;
            bool handbrake = false, boost = false, boostDown = false;
            bool horn = false, retry = false, confirm = false, aerial = false;
            bool menu = false;
            bool lookBack = false;

            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steerRaw -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steerRaw += 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttleRaw += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) throttleRaw -= 1f;

                handbrake = kb.spaceKey.isPressed;
                boost = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                boostDown = kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame;
                horn = kb.eKey.wasPressedThisFrame || kb.qKey.wasPressedThisFrame;
                retry = kb.rKey.wasPressedThisFrame;
                menu = kb.escapeKey.wasPressedThisFrame;
                confirm = kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
                aerial = kb.fKey.wasPressedThisFrame;
                lookBack = kb.cKey.isPressed;
            }

            if (pad != null)
            {
                // ---- steering ----
                //
                // Read off the STICK, not off leftStick.x, and the difference is the entire deadzone
                // story. Both spellings compile and both look like "how far left or right is the
                // stick", but the deadzone processor this project relies on is declared on the
                // Vector2 — Gamepad.cs binds leftStick with processors = "stickDeadzone" — and it is
                // a RADIAL deadzone over the pair. Reaching past it into the child axis gets the
                // bare number with only the axis-level clamp on it, which is why this used to need a
                // hand-rolled 0.15 gate to keep a worn stick from creeping the mower sideways on the
                // start line.
                //
                // That gate worked and was still wrong in a way you can feel. It was a THRESHOLD,
                // not a rescale: at 0.149 the machine did nothing and at 0.151 it steered at 0.15,
                // so the first fifteen percent of steering authority arrived in one step the moment
                // the stick crossed the line. Every small correction — the ones that decide whether
                // a heart comes out as a heart — started with a jolt.
                //
                // ReadValue() on the stick applies the processor properly: a radial deadzone at
                // InputSettings.defaultDeadzoneMin (0.125) with the remaining travel RESCALED back
                // out to a full unit at defaultDeadzoneMax (0.925). So the response is continuous
                // from zero, drift below an eighth of travel is dead, and a stick that no longer
                // quite reaches its stops can still ask for full lock. The epsilon left below is not
                // a deadzone — the processor did that — it is only there so an idle pad contributes
                // nothing at all to the fold below rather than a denormal.
                float padSteer = pad.leftStick.ReadValue().x;
                if (Mathf.Abs(padSteer) > 0.02f) steerRaw = Mathf.Clamp(steerRaw + padSteer, -1f, 1f);

                // The D-pad drives, exactly as the arrow keys do.
                //
                // It was dead in a round until now, which is a strange thing for a driving game to
                // do with the one control on the pad that every player already knows means "go that
                // way". Nothing else reads it while a round is running — the menus read it, but a
                // menu is a popup, and a popup blocks driving — so there is no contest to lose.
                // Digital and therefore coarse, and that is fine: it is the same signal WASD sends,
                // through the same smoothing, and for a player whose stick has given up it is the
                // difference between a playable game and none.
                if (pad.dpad.left.isPressed) steerRaw = Mathf.Clamp(steerRaw - 1f, -1f, 1f);
                if (pad.dpad.right.isPressed) steerRaw = Mathf.Clamp(steerRaw + 1f, -1f, 1f);

                // ---- throttle ----
                //
                // Triggers rescaled off their own small deadzone for the same reason as the stick:
                // gates step, ramps do not. A trigger that rests a hair above zero — which is
                // ordinary on a used pad, and ordinary on the browser's "standard mapping" where the
                // value is a button pressure rather than a calibrated axis — must not creep the
                // machine forward, and a trigger pulled to its stop must reach exactly 1 rather than
                // 0.94. Read separately and differenced afterwards, so squeezing both is a genuine
                // cancel rather than whichever one happened to win.
                float trigger = Trigger(pad.rightTrigger.ReadValue()) - Trigger(pad.leftTrigger.ReadValue());
                if (Mathf.Abs(trigger) > 0.001f) throttleRaw = Mathf.Clamp(throttleRaw + trigger, -1f, 1f);

                if (pad.dpad.up.isPressed) throttleRaw = Mathf.Clamp(throttleRaw + 1f, -1f, 1f);
                if (pad.dpad.down.isPressed) throttleRaw = Mathf.Clamp(throttleRaw - 1f, -1f, 1f);

                handbrake |= pad.buttonSouth.isPressed;
                boost |= pad.rightShoulder.isPressed;
                boostDown |= pad.rightShoulder.wasPressedThisFrame;
                horn |= pad.buttonWest.wasPressedThisFrame;
                retry |= pad.buttonNorth.wasPressedThisFrame;
                aerial |= pad.leftShoulder.wasPressedThisFrame;
                lookBack |= pad.rightStickButton.isPressed;

                // ---- START IS PAUSE, AND A IS CONFIRM ----
                //
                // Start used to be a second confirm alongside A, and the pad had no pause at all.
                // That is a real hole rather than a missing nicety: Escape opens the pause board,
                // the pause board is the only way to reach BACK TO MENU or QUIT, and a player on a
                // controller could not open it. They could drive, honk, retry and confirm, and they
                // could not leave. On a browser build with no window chrome to fall back on, that is
                // a player shut in.
                //
                // So the button moved, and it moved this way round rather than the other because
                // every console this game's controls are modelled on has already taught the answer:
                // Menu on an Xbox pad and Options on a PlayStation pad are THE pause button, in
                // every game, and A / Cross is THE confirm. Binding pause to a spare shoulder or to
                // Select would have preserved start-as-confirm at the cost of making the one button
                // the player will actually press for a pause menu do something else.
                //
                // Confirm is not weakened by the move: buttonSouth is still bound to it on the line
                // below, it is the button players reach for anyway, and the on-screen controls push
                // their own confirm edge through PulseButtons. What is lost is "press start to
                // continue" on a results card, and what is gained is that start means the same
                // thing on every screen in the game — which is the trade every console makes.
                //
                // ONE THING TO KNOW IF YOU ARE CHANGING THIS: PopupView.HandleInput still reads
                // startButton as a confirm on an open popup. It cannot fire today, because
                // PopupStack.Tick consumes the menu edge and returns before the popup is stepped —
                // but the two now disagree on paper, and the popup's copy is the one that should go.
                confirm |= pad.buttonSouth.wasPressedThisFrame;
                menu |= pad.startButton.wasPressedThisFrame;
            }

            // Button edges pushed in from the on-screen controls, folded in alongside the devices.
            //
            // SetOverride carries the four HELD inputs and is enough for steering and throttle, but
            // horn, aerial and confirm are wasPressedThisFrame EDGES — a held value cannot express
            // "went down this frame", so a touch build had no way to honk, look, or advance past the
            // results card. That last one is not a missing nicety: a phone player reached the end of
            // a round and could not leave it.
            horn |= _pHorn; aerial |= _pAerial; confirm |= _pConfirm;
            retry |= _pRetry; menu |= _pMenu;
            _pHorn = _pAerial = _pConfirm = _pRetry = _pMenu = false;

            _rawSteer = Mathf.Clamp(steerRaw, -1f, 1f);
            _rawThrottle = Mathf.Clamp(throttleRaw, -1f, 1f);

            if (OverrideActive)
            {
                _rawSteer = Mathf.Clamp(_ovSteer, -1f, 1f);
                _rawThrottle = Mathf.Clamp(_ovThrottle, -1f, 1f);
                handbrake = _ovHandbrake;
                boost = _ovBoost;
            }

            if (!DrivingEnabled) { _rawSteer = 0f; _rawThrottle = 0f; handbrake = false; boost = false; }

            // Smoothing gives the mower weight without adding input lag you can feel.
            Steer = Mathf.MoveTowards(Steer, _rawSteer, steerSmoothing * dt);
            Throttle = Mathf.MoveTowards(Throttle, _rawThrottle, throttleSmoothing * dt);

            Handbrake = handbrake;
            Boost = boost;
            BoostPressed = boostDown;
            RetryPressed = retry;
            MenuPressed = menu;
            AnyConfirmPressed = confirm;

            // The round's own verbs, gated together. Escape and retry stay live whatever stage is
            // running — a player must always be able to get out — and driving is untouched.
            HornPressed = horn && RoundActionsEnabled;
            // Read before the DrivingEnabled gate above would matter — the lift is not a driving
            // input, and it is deliberately still available in the moment the guide dissolves.
            AerialPressed = aerial && RoundActionsEnabled;
            // HELD, not an edge, and not gated with the round's verbs. Looking over your shoulder
            // is a camera control, not a game action — it has to work while a menu is up, while the
            // klaxon is sounding, and in every stage — and a glance that ends the moment you let go
            // is the only version that never leaves the player facing the wrong way.
            LookBack = lookBack;

            // ---- the pad, answering back ----
            //
            // Two of the game's haptic effects belong HERE rather than to whatever consumes the
            // verb, and only two. The test is whether the feeling is about the PRESS or about the
            // consequence: a horn and a boost are things the player DID, they are felt the instant
            // the button goes down, and this is the only place in the project that knows that
            // instant for every input source at once — pad, keyboard and the on-screen buttons, all
            // folded together three lines above. Everything else in Haptics' vocabulary is about
            // something happening TO the player — a bonk, a goose turned, a verdict — and belongs
            // to whichever system decides that it happened. See Haptics for the rest of the list.
            //
            // Gated on the same flags the verbs themselves are, so a key that does nothing in a
            // stage does not still buzz. A rumble is a claim that something happened, and a rumble
            // with nothing behind it is worse than silence: it teaches the player the button works.
            if (HornPressed) Haptics.Horn();
            if (boostDown && DrivingEnabled) Haptics.BoostStart();
        }

        /// <summary>
        /// One trigger, deadzoned at the bottom and rescaled to reach 1 at the top.
        ///
        /// Analogue triggers are the least trustworthy axis on a pad. A worn one rests a little
        /// above zero and never quite arrives at one; the browser's "standard mapping" reports them
        /// as button PRESSURES rather than as calibrated axes, so the same pad reads differently in
        /// Chrome than it does through XInput. Both ends need work, and the cheap version — a bare
        /// threshold, which is what this file used to do at 0.05 — only fixes one of them, and does
        /// it by introducing a step exactly where the player is trying to feather.
        ///
        /// So: nothing below the floor, and everything from the floor to the ceiling stretched back
        /// out over the full range. The floor is low because a trigger is a deliberate squeeze
        /// rather than a resting thumb and does not need the eighth of travel a stick gives away;
        /// the ceiling is high because the value that must be reachable, on every pad, is full
        /// throttle.
        /// </summary>
        static float Trigger(float raw)
        {
            const float floor = 0.06f, ceiling = 0.95f;
            if (float.IsNaN(raw) || raw <= floor) return 0f;
            if (raw >= ceiling) return 1f;
            return (raw - floor) / (ceiling - floor);
        }

        /// <summary>
        /// One-frame button edges from a source that is not a device — the on-screen controls.
        ///
        /// ORed rather than assigned, so a pulse raised before this component ticks cannot be erased
        /// by a second caller in the same frame. Consumed and cleared inside Tick, which is what
        /// keeps a pulse exactly one frame long however often it is pushed.
        ///
        /// Retry and menu are in the signature although nothing sends them yet: a phone build wants a
        /// retry affordance next, and widening this method a second time for the same reason is worse
        /// than two parameters that are wired early.
        /// </summary>
        public void PulseButtons(bool horn, bool aerial, bool confirm, bool retry, bool menu)
        {
            _pHorn |= horn; _pAerial |= aerial; _pConfirm |= confirm;
            _pRetry |= retry; _pMenu |= menu;
        }
        bool _pHorn, _pAerial, _pConfirm, _pRetry, _pMenu;

        public void ResetSmoothing()
        {
            Steer = 0f; Throttle = 0f;
            _rawSteer = 0f; _rawThrottle = 0f;
        }
    }
}
