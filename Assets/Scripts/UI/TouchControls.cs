using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace DuckMow
{
    /// <summary>
    /// The on-screen driving controls, for a phone.
    ///
    /// Feeds <see cref="InputReader"/> through the same override seam the autopilot uses
    /// (<see cref="InputReader.OverrideActive"/> / <see cref="InputReader.SetOverride"/>) rather
    /// than being a second input reader. That matters for one specific reason: InputReader's
    /// smoothing, its DrivingEnabled gate and its clamping ARE the feel of the mower, and a touch
    /// layer that bypassed them would be a differently-handling game on a phone.
    ///
    /// Hit-testing is done here, against RectTransforms, rather than through an EventSystem and a
    /// GraphicRaycaster. Two reasons. Unity's UI button events are built around one pointer, and
    /// this needs a thumb on the stick and a thumb on the brake AT THE SAME TIME — exactly the
    /// case where pointer-based buttons drop one of them. And the scene has no EventSystem at all,
    /// because nothing else in this game is clicked; adding one to service four buttons would be a
    /// larger change than the buttons.
    ///
    /// Runs at -100 so its writes to InputReader land BEFORE InputReader.Update reads them. With
    /// both at the default order, which of the two goes first depends on Unity's internal
    /// component order — an extra frame of steering lag that appears and disappears between
    /// builds for no reason the player can see.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class TouchControls : MonoBehaviour
    {
        /// <summary>What a rectangle does when a thumb is inside it.</summary>
        public enum Action { Handbrake, Boost, Horn, Aerial }

        [System.Serializable]
        public class TouchButton
        {
            public Action action;
            public RectTransform rect;
            public Image image;
            public Sprite idle;
            public Sprite pressed;

            [System.NonSerialized] public bool held;
            [System.NonSerialized] public bool wasHeld;
        }

        [Header("Wiring (built by DuckTouchBuilder)")]
        public CanvasGroup group;
        /// <summary>The region a thumb may land in to start steering. Deliberately large.</summary>
        public RectTransform stickArea;
        /// <summary>The ring, which MOVES to wherever the thumb landed. See MoveRingTo.</summary>
        public RectTransform stickRing;
        public RectTransform stickKnob;
        public TouchButton[] buttons = new TouchButton[0];

        [Header("Feel")]
        [Tooltip("Full deflection distance, as a fraction of screen height. 0.13 is about a " +
                 "thumb's comfortable reach from where it landed, on a phone held in two hands.")]
        [Range(0.05f, 0.35f)] public float stickTravel = 0.13f;
        [Tooltip("Ignored deflection, as a fraction of travel. Stops a resting thumb from creeping.")]
        [Range(0f, 0.5f)] public float stickDeadZone = 0.14f;
        [Tooltip("Longest a background touch can last and still count as tap-to-continue.")]
        public float tapMaxDuration = 0.4f;

        [Header("Testing")]
        [Tooltip("Show the controls regardless of device. For inspecting the layout in the editor, " +
                 "where there is no touchscreen to reveal them and isMobilePlatform is false.")]
        public bool alwaysShow;

        // ---------------------------------------------------------------- state

        const int MaxTouches = 10;

        /// <summary>What each live touch was claimed by. Claimed once, on the frame it began.</summary>
        enum Claim { Free, Stick, Button, Background }

        struct Slot
        {
            public int id;
            public Claim claim;
            public int button;        // index into buttons, when claim == Button
            public Vector2 origin;    // screen position the touch began at
            public float began;       // unscaled time it began at
            public float drift;       // greatest distance from origin since, in pixels
        }

        readonly Slot[] _slots = new Slot[MaxTouches];

        Canvas _canvas;
        Camera _cam;
        RectTransform _canvasRect;
        Autopilot _autopilot;

        Vector2 _ringHome;
        bool _ringHomeCaptured;

        float _steer, _throttle;
        bool _handbrake, _boost;
        bool _engaged;          // any driving control currently held
        bool _wroteOverride;    // we are the one holding InputReader's override open

        /// <summary>
        /// Whether the controls are on screen at all.
        ///
        /// Two ways in, because neither is sufficient alone. Application.isMobilePlatform comes
        /// from a user-agent test in the WebGL loader, so it is true on a phone and FALSE on an
        /// iPad — iPadOS Safari reports itself as a Macintosh. The other way in is the first real
        /// touch: a machine that is pointed at with a mouse never sends one, so a laptop with a
        /// touchscreen never grows a thumbstick it has no use for, which is the failure this rule
        /// exists to prevent. A phone reveals them on the tap that starts the game.
        /// </summary>
        public bool Revealed { get; private set; }

        /// <summary>
        /// True while on-screen controls are showing anywhere. Static because the thing that will
        /// want it is the HUD's keyboard prompts — "[SPACE] NEXT PICTURE" on a device with no
        /// space bar — and the HUD has no reason to hold a reference to a control cluster.
        /// </summary>
        public static bool Active { get; private set; }

        // Button-shaped inputs, held for one frame, waiting on the InputReader addition
        // documented on PushButtons below.
        public bool HornPressed { get; private set; }
        public bool AerialPressed { get; private set; }
        public bool ConfirmPressed { get; private set; }

        void Awake()
        {
            _canvas = GetComponent<Canvas>();
            _canvasRect = (RectTransform)transform;
            if (_canvas != null) _cam = _canvas.worldCamera;

            for (int i = 0; i < _slots.Length; i++) { _slots[i].id = -1; _slots[i].claim = Claim.Free; }

            Revealed = alwaysShow || Application.isMobilePlatform;
            Active = Revealed;
            ApplyVisibility(Revealed ? 1f : 0f, true);
        }

        void Start()
        {
            _autopilot = FindFirstObjectByType<Autopilot>();
            if (stickRing != null && !_ringHomeCaptured)
            {
                _ringHome = stickRing.anchoredPosition;
                _ringHomeCaptured = true;
            }
        }

        void OnDisable()
        {
            // Never leave the override latched. If this component goes away mid-corner while it is
            // holding the override open, InputReader keeps applying the last steer value forever
            // and the mower drives off on its own.
            Release();
            Active = false;
        }

        void Update()
        {
            // The simulator drives the game from outside and there is no touchscreen in it; see
            // SimClock. Ticking here would also mean a captured frame could contain a thumbstick.
            if (SimClock.Scripted) return;
            Tick(Time.unscaledDeltaTime);
        }

        public void Tick(float dt)
        {
            var ts = Touchscreen.current;
            if (ts == null) { Release(); return; }

            // Read before the reveal check: the touch that reveals the cluster is found in here.
            ReadTouches(ts);
            if (!Revealed) return;
            Active = true;

            // The autopilot owns the override while it is driving — it is the same single seam, and
            // two writers would fight over it every frame. Yielding rather than merging is right:
            // the autopilot only runs when the player is not meant to be driving.
            if (_autopilot != null && _autopilot.Active) { Release(); return; }

            if (_engaged)
            {
                var input = InputReader.Instance;
                if (input != null)
                {
                    input.SetOverride(_steer, _throttle, _handbrake, _boost);
                    input.OverrideActive = true;
                    _wroteOverride = true;
                }
            }
            else if (_wroteOverride)
            {
                Release();
            }

            PushButtons();
            UpdateVisuals(dt);
        }

        // ---------------------------------------------------------------- touches

        void ReadTouches(Touchscreen ts)
        {
            HornPressed = false;
            AerialPressed = false;
            ConfirmPressed = false;

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                buttons[i].wasHeld = buttons[i].held;
                buttons[i].held = false;
            }

            bool stickHeld = false;
            Vector2 stickVector = Vector2.zero;

            var touches = ts.touches;
            for (int t = 0; t < touches.Count; t++)
            {
                TouchControl touch = touches[t];
                bool down = touch.press.isPressed;
                bool released = touch.press.wasReleasedThisFrame;
                if (!down && !released) continue;

                // The first real touch is what reveals the controls on a device the loader called
                // a desktop. It is consumed doing so rather than also being fed to the game: a tap
                // that both reveals a brake and presses it is not a tap the player can have meant.
                if (!Revealed)
                {
                    if (touch.press.wasPressedThisFrame) { Revealed = true; Active = true; }
                    continue;
                }

                int id = touch.touchId.ReadValue();
                Vector2 pos = touch.position.ReadValue();

                int slot = FindSlot(id);
                if (slot < 0 && touch.press.wasPressedThisFrame) slot = ClaimSlot(id, pos);
                if (slot < 0) continue;

                _slots[slot].drift = Mathf.Max(_slots[slot].drift,
                                               Vector2.Distance(pos, _slots[slot].origin));

                switch (_slots[slot].claim)
                {
                    case Claim.Stick:
                        if (down)
                        {
                            stickHeld = true;
                            stickVector = pos - _slots[slot].origin;
                        }
                        break;

                    case Claim.Button:
                        int b = _slots[slot].button;
                        // Held from the claim onwards, WITHOUT re-testing the rectangle. A thumb
                        // resting on a small button wanders several pixels, and re-testing every
                        // frame made the handbrake stutter on and off through a slide.
                        if (down && b >= 0 && b < buttons.Length && buttons[b] != null)
                            buttons[b].held = true;
                        break;

                    case Claim.Background:
                        // Tap anywhere that is not a control = the confirm key.
                        //
                        // This is the whole answer to "[SPACE] NEXT PICTURE" on a device with no
                        // space bar, and it is better than a button: it is the gesture a player
                        // already tries. Safe during play because GameDirector only reads a confirm
                        // during the briefing, the results and the ceremony — while mowing, nothing
                        // is listening for it.
                        if (released &&
                            Time.unscaledTime - _slots[slot].began <= tapMaxDuration &&
                            _slots[slot].drift < Screen.height * 0.05f)
                            ConfirmPressed = true;
                        break;
                }

                if (released) FreeSlot(slot);
            }

            ApplyStick(stickHeld, stickVector);

            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                if (b == null) continue;
                bool edge = b.held && !b.wasHeld;
                switch (b.action)
                {
                    case Action.Handbrake: _handbrake = b.held; break;
                    case Action.Boost: _boost = b.held; break;
                    case Action.Horn: if (edge) HornPressed = true; break;
                    case Action.Aerial: if (edge) AerialPressed = true; break;
                }
            }

            _engaged = stickHeld || _handbrake || _boost;
        }

        void ApplyStick(bool held, Vector2 delta)
        {
            if (!held) { _steer = 0f; _throttle = 0f; return; }

            // Travel measured against screen HEIGHT in both axes, so the stick has the same
            // physical reach sideways as forwards. Measuring x against width would make steering
            // feel slower than the throttle, on the same thumb, on the same screen.
            float travel = Mathf.Max(1f, Screen.height * stickTravel);
            _steer = Axis(delta.x / travel);
            _throttle = Axis(delta.y / travel);
        }

        /// <summary>Dead zone rescaled so full deflection still reads 1, not 1 minus the zone.</summary>
        float Axis(float v)
        {
            float a = Mathf.Abs(v);
            if (a <= stickDeadZone) return 0f;
            float scaled = (a - stickDeadZone) / Mathf.Max(0.001f, 1f - stickDeadZone);
            return Mathf.Clamp01(scaled) * Mathf.Sign(v);
        }

        int FindSlot(int id)
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].claim != Claim.Free && _slots[i].id == id) return i;
            return -1;
        }

        int ClaimSlot(int id, Vector2 pos)
        {
            int slot = -1;
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].claim == Claim.Free) { slot = i; break; }
            if (slot < 0) return -1;

            _slots[slot].id = id;
            _slots[slot].origin = pos;
            _slots[slot].began = Time.unscaledTime;
            _slots[slot].drift = 0f;
            _slots[slot].button = -1;

            // A faded cluster is not a claimable cluster.
            //
            // The cluster fades out whenever driving is off — the countdown, the reveal, the
            // judging, the ceremony — and those are exactly the screens where tap-to-continue is
            // the only control the player has. Testing invisible rectangles there would leave
            // dead patches on screen where a tap does nothing and the player has no way to know
            // why. Everything falls through to Background instead.
            bool cluster = group == null || group.alpha > 0.5f;

            if (cluster)
            {
                // Buttons first: they are small and sit inside the stick's generous area on a
                // narrow screen, and losing the brake to the steering region is the worse mistake.
                for (int i = 0; i < buttons.Length; i++)
                {
                    var b = buttons[i];
                    if (b == null || b.rect == null) continue;
                    if (!RectTransformUtility.RectangleContainsScreenPoint(b.rect, pos, _cam)) continue;
                    _slots[slot].claim = Claim.Button;
                    _slots[slot].button = i;
                    return slot;
                }

                if (stickArea != null && !StickBusy() &&
                    RectTransformUtility.RectangleContainsScreenPoint(stickArea, pos, _cam))
                {
                    _slots[slot].claim = Claim.Stick;
                    MoveRingTo(pos);
                    return slot;
                }
            }

            _slots[slot].claim = Claim.Background;
            return slot;
        }

        bool StickBusy()
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].claim == Claim.Stick) return true;
            return false;
        }

        void FreeSlot(int slot)
        {
            if (_slots[slot].claim == Claim.Stick && stickRing != null && _ringHomeCaptured)
                stickRing.anchoredPosition = _ringHome;
            _slots[slot].claim = Claim.Free;
            _slots[slot].id = -1;
            _slots[slot].button = -1;
        }

        /// <summary>
        /// Put the ring under the thumb that just landed.
        ///
        /// A fixed thumbstick has to be found by touch on a surface with no texture, so the first
        /// thing the player does is look down at their hands instead of at the lawn — and they
        /// still miss it, because where a thumb naturally falls depends on hand size and grip.
        /// Taking the origin from wherever the thumb actually landed removes the aiming problem
        /// entirely; the ring is then drawn there as feedback rather than as a target.
        /// </summary>
        void MoveRingTo(Vector2 screenPos)
        {
            if (stickRing == null || _canvasRect == null) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, screenPos, _cam, out Vector2 local))
                stickRing.anchoredPosition = local;
        }

        // ---------------------------------------------------------------- output

        void Release()
        {
            _steer = 0f; _throttle = 0f;
            _handbrake = false; _boost = false;
            _engaged = false;
            if (!_wroteOverride) return;
            var input = InputReader.Instance;
            if (input != null)
            {
                input.OverrideActive = false;
                input.SetOverride(0f, 0f, false, false);
            }
            _wroteOverride = false;
        }

        /// <summary>
        /// Where horn, aerial and confirm reach InputReader — once it can take them.
        ///
        /// SetOverride covers the four held driving inputs and nothing else, because the autopilot
        /// it was written for never needed to press a button. Horn, aerial and confirm are edges
        /// (wasPressedThisFrame), so they cannot be carried by a held override value: a flag that
        /// is true for one frame and read on the next is a flag that gets missed.
        ///
        /// The addition needed in InputReader is one method plus one consume-and-clear. The method:
        ///
        ///     /// &lt;summary&gt;One-frame button edges from a source that is not a device — the
        ///     /// on-screen controls. ORed, not assigned, so a pulse raised before this ticks
        ///     /// cannot be erased by a second caller in the same frame.&lt;/summary&gt;
        ///     public void PulseButtons(bool horn, bool aerial, bool confirm, bool retry, bool menu)
        ///     {
        ///         _pHorn |= horn; _pAerial |= aerial; _pConfirm |= confirm;
        ///         _pRetry |= retry; _pMenu |= menu;
        ///     }
        ///     bool _pHorn, _pAerial, _pConfirm, _pRetry, _pMenu;
        ///
        /// and inside Tick, immediately after the gamepad block and before the clamps:
        ///
        ///     horn |= _pHorn; aerial |= _pAerial; confirm |= _pConfirm;
        ///     retry |= _pRetry; menu |= _pMenu;
        ///     _pHorn = _pAerial = _pConfirm = _pRetry = _pMenu = false;
        ///
        /// Retry and menu are in the signature although nothing sends them yet, because the next
        /// thing a phone build needs is a retry affordance on the results screen, and a second
        /// edit to the same method for the same reason is worse than two extra parameters.
        ///
        /// It has landed — <see cref="InputReader.PulseButtons"/> exists and this is live. Before it
        /// did, the four held inputs worked and these three edges did not, which meant a phone player
        /// could drive a whole round and then had no way to leave the results card.
        /// </summary>
        void PushButtons()
        {
            InputReader.Instance?.PulseButtons(HornPressed, AerialPressed, ConfirmPressed, false, false);
        }

        // ---------------------------------------------------------------- visuals

        void UpdateVisuals(float dt)
        {
            // The driving cluster is only meaningful while the mower answers to it. During the
            // countdown, the reveal, the judging and the ceremony it fades out — which is also
            // what keeps a thumbstick out of the overhead reveal, the shot the whole round builds
            // towards. Faded rather than switched, because a control cluster that vanishes between
            // one frame and the next reads as a glitch.
            var input = InputReader.Instance;
            bool driving = input == null || input.DrivingEnabled;
            ApplyVisibility(driving ? 1f : 0f, false, dt);

            if (stickKnob != null)
            {
                // Knob travel is a share of the ring it sits in, so the visual and the input agree
                // at any screen size without a second tuning number to keep in step.
                float travel = stickKnob.parent is RectTransform p
                    ? Mathf.Min(p.rect.width, p.rect.height) * 0.30f
                    : 40f;
                stickKnob.anchoredPosition = new Vector2(_steer, _throttle) * travel;
            }

            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                if (b == null || b.image == null) continue;
                var want = b.held ? b.pressed : b.idle;
                if (want != null && b.image.sprite != want) b.image.sprite = want;
            }
        }

        void ApplyVisibility(float target, bool immediate, float dt = 0f)
        {
            if (group == null) return;
            group.alpha = immediate
                ? target
                : Mathf.MoveTowards(group.alpha, target, 4f * Mathf.Max(dt, 0f));

            // The Canvas component, not the GameObject: a disabled GameObject would stop this
            // component's Update, and then nothing would ever be left running to turn it back on.
            // Disabling the Canvas only stops the graphics being submitted to a batch.
            bool draw = group.alpha > 0.004f;
            if (_canvas != null && _canvas.enabled != draw) _canvas.enabled = draw;
        }
    }
}
