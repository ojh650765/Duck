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
        public bool RetryPressed { get; private set; }
        public bool NextPressed { get; private set; }
        public bool AnyConfirmPressed { get; private set; }

        /// <summary>Set false during countdown, klaxon and results so the mower ignores input.</summary>
        public bool DrivingEnabled { get; set; } = true;

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
            bool horn = false, retry = false, next = false, confirm = false;

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
                confirm = kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
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
            }

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
            HornPressed = horn;
            RetryPressed = retry;
            NextPressed = next;
            AnyConfirmPressed = confirm;
        }

        public void ResetSmoothing()
        {
            Steer = 0f; Throttle = 0f;
            _rawSteer = 0f; _rawThrottle = 0f;
        }
    }
}
