using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckMow
{
    /// <summary>
    /// Thin polling layer over the Input System's low-level devices. Deliberately not an
    /// .inputactions asset: the control set is tiny, and polling devices directly is one less
    /// thing to break in a WebGL build.
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
        public bool NextPressed { get; private set; }
        /// <summary>Escape. The way back to the front page — see GameDirector.BackToMenu.</summary>
        public bool MenuPressed { get; private set; }
        public bool AnyConfirmPressed { get; private set; }
        /// <summary>The one-per-round lift. F rather than Tab, which the browser steals for focus.</summary>
        public bool AerialPressed { get; private set; }

        /// <summary>Set false during countdown, klaxon and results so the mower ignores input.</summary>
        public bool DrivingEnabled { get; set; } = true;

        /// <summary>
        /// Whether the ROUND's own verbs are live: the horn on E, the overhead check on F, and skip
        /// on N. True everywhere by default, so nothing about the mowing round changes.
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
            bool horn = false, retry = false, next = false, confirm = false, aerial = false;
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
                next = kb.nKey.wasPressedThisFrame;
                menu = kb.escapeKey.wasPressedThisFrame;
                confirm = kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
                aerial = kb.fKey.wasPressedThisFrame;
                lookBack = kb.cKey.isPressed;
            }

            if (pad != null)
            {
                float padSteer = pad.leftStick.x.ReadValue();
                if (Mathf.Abs(padSteer) > 0.15f) steerRaw = Mathf.Clamp(steerRaw + padSteer, -1f, 1f);

                float trigger = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
                if (Mathf.Abs(trigger) > 0.05f) throttleRaw = Mathf.Clamp(throttleRaw + trigger, -1f, 1f);

                handbrake |= pad.buttonSouth.isPressed;
                boost |= pad.rightShoulder.isPressed;
                boostDown |= pad.rightShoulder.wasPressedThisFrame;
                horn |= pad.buttonWest.wasPressedThisFrame;
                retry |= pad.buttonNorth.wasPressedThisFrame;
                confirm |= pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame;
                aerial |= pad.leftShoulder.wasPressedThisFrame;
                lookBack |= pad.rightStickButton.isPressed;
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
            NextPressed = next && RoundActionsEnabled;
            // Read before the DrivingEnabled gate above would matter — the lift is not a driving
            // input, and it is deliberately still available in the moment the guide dissolves.
            AerialPressed = aerial && RoundActionsEnabled;
            // HELD, not an edge, and not gated with the round's verbs. Looking over your shoulder
            // is a camera control, not a game action — it has to work while a menu is up, while the
            // klaxon is sounding, and in every stage — and a glance that ends the moment you let go
            // is the only version that never leaves the player facing the wrong way.
            LookBack = lookBack;
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
