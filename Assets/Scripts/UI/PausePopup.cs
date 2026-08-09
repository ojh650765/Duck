using System;
using System.Collections.Generic;
using DuckMow.Flow;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckMow.UI
{
    /// <summary>
    /// THE CHROME every popup in this game wears, and the input it is driven by.
    ///
    /// Two types live in this file — this base and <see cref="PausePopup"/> — which is not the
    /// project's habit, and the reason is worth writing down: the popup views are owned as a pair
    /// (this file and ConfirmPopup.cs) and a third file for the shared board would have to be owned
    /// by somebody. The base sits with the popup that BOOTSTRAPS the subsystem, because the static
    /// hook at the bottom of this file is the seam by which Escape reaches any of it at all.
    ///
    /// ---- what a subclass gets ----
    ///
    /// A canvas of its own, built at runtime. Every screen in this game that is pinned to frame
    /// coordinates rather than to the world is built in code — see TurfHud and DefenceHud, whose
    /// construction idiom this copies almost line for line — because there is nothing to drag in a
    /// screen-space layout and nothing gained by baking it into a scene that four other agents are
    /// also editing.
    ///
    /// A scrim, a signboard, a column of menu items, and a selection that moves on springs.
    ///
    /// ---- and the two things that are actually hard ----
    ///
    /// INPUT. This project ships no EventSystem, no InputModule and no GraphicRaycaster anywhere,
    /// deliberately — Curtain.BuildCanvas says why, and MainMenu says it again: they are three more
    /// things that can arrive broken in a WebGL build, and the hit test they would be doing for us is
    /// four lines. So a Unity Button here would never receive a click. Every popup polls the devices
    /// itself, exactly as MainMenu.HandleInput does, and hit-tests the mouse against each item's
    /// RectTransform by hand. Copying the front page's approach is not laziness: it is the only way
    /// the pause menu can FEEL like the menu the player already learned.
    ///
    /// UNSCALED TIME, everywhere, without exception. A popup that pauses the game is a popup running
    /// at Time.timeScale == 0, where Time.deltaTime is exactly zero forever. Tick is handed an
    /// unscaled delta by the stack and that is the only clock this file is allowed to read.
    /// </summary>
    public abstract class PopupView : IPopup
    {
        // ------------------------------------------------------------------ the contract

        public abstract string Id { get; }

        /// <summary>Every popup this game has stops the world. Overridable for one that does not.</summary>
        public virtual bool PausesTime => true;
        public virtual bool BlocksDriving => true;
        public virtual bool ClosesOnEscape => true;

        /// <summary>Where this popup's canvas sits in the composite. See <see cref="BuildCanvas"/>.</summary>
        protected abstract int SortingOrder { get; }

        /// <summary>Build the scrim, the board and the items. Called once, from OnPush.</summary>
        protected abstract void Compose();

        /// <summary>
        /// Anything that needs the finished layout. Called once, after Compose and after the items
        /// have been dealt their positions, and before the first frame is drawn.
        ///
        /// It exists because the two are genuinely different phases: Compose says what is on the
        /// board, and the column's geometry does not exist until every AddItem has been counted. A
        /// subclass reaching for a row's position from inside Compose would be reading a rect that
        /// has not been placed.
        /// </summary>
        protected virtual void OnComposed() { }

        /// <summary>
        /// Anything a popup did to the rest of the game on the way in, undone.
        ///
        /// Called from OnPop, which the stack guarantees is the only teardown call there is: a popup
        /// can be pushed and popped inside one frame with no Tick between, and PopAll runs OnPop
        /// top-first and never calls OnRevealed. So this is the ONLY place a global may be put back —
        /// anything that waits for a tick, or for the popup to be uncovered, is a global left stuck.
        /// </summary>
        protected virtual void OnClosed() { }

        // ------------------------------------------------------------------ the palette
        //
        // Taken as NUMBERS from what is already on screen rather than eyeballed against it, which is
        // the discipline TurfHud sets out at length: two nearly-identical creams on two screens of
        // the same game is a specific, recognisable kind of wrong, and it is only avoided by copying
        // the values. The board is the venue's own painted signage — the same cream edge and deep
        // green face Curtain.BuildSign paints its transition plate with — and the plates are the
        // front page's buttons, in MainMenu's own labelIdle and labelSelected.

        protected static readonly Color Cream = new(0.95f, 0.92f, 0.84f);
        protected static readonly Color BoardEdge = new(0.93f, 0.89f, 0.75f);
        protected static readonly Color BoardFace = new(0.12f, 0.25f, 0.13f);
        protected static readonly Color Gold = new(0.86f, 0.76f, 0.42f);
        protected static readonly Color Ink = new(0.16f, 0.12f, 0.09f);
        protected static readonly Color InkHot = new(0.72f, 0.20f, 0.17f);
        protected static readonly Color Plate = new(0.95f, 0.92f, 0.84f);
        protected static readonly Color PlateHot = new(1.00f, 0.97f, 0.88f);
        protected static readonly Color WoodDark = new(0.43f, 0.29f, 0.17f);
        /// <summary>
        /// The scrim. Deep hedge green rather than black, and that is a house rule rather than a
        /// preference — Curtain's docs put it plainly: "It never shows black. Every backing colour is
        /// out of the garden." A pause screen is the single longest-held frame in the game and a
        /// black wash over a sunlit lawn is the one thing that would make it look like a browser
        /// error page.
        /// </summary>
        protected static readonly Color Scrim = new(0.05f, 0.11f, 0.06f);

        // ------------------------------------------------------------------ state

        /// <summary>One menu row: its plate, its label, its shadow and the two springs driving them.</summary>
        protected sealed class Item
        {
            public RectTransform rect;
            public RectTransform shadow;
            public Image plate;
            public TextMeshProUGUI label;
            public Action activate;
            public float restTilt;
            public float sel, selVel, press, pressVel;
        }

        protected readonly List<Item> Items = new();

        GameObject _root;
        Canvas _canvas;
        CanvasGroup _group;
        Image _scrim;
        RectTransform _board;
        RectTransform _pointer;
        float _scrimAlpha = 0.78f;

        int _index;
        int _pressed = -1;
        float _clock;
        float _open;                 // 0 on the frame it is pushed, 1 once the board has landed
        bool _covered;
        bool _closeRequested;
        bool _detached;              // a menu action tore the stack down; we are already gone
        bool _disposed;

        Vector2 _lastPointer;
        bool _pointerSeen;

        /// <summary>
        /// The frame, and then the tenth of a second, on which this popup refuses to read input.
        ///
        /// THIS IS THE MAIN CORRECTNESS TRAP IN A STACK OF POPUPS and it is worth being explicit
        /// about. Escape opens the pause menu on frame N — and on frame N, escapeKey.wasPressedThisFrame
        /// is still true for anything else that asks. Enter confirms a child popup on frame M, the
        /// child pops, this popup is revealed on frame M, and on frame M Enter is still pressed: the
        /// player would confirm QUIT and then immediately re-activate whatever this menu had selected.
        /// One keypress, two decisions, and the second one invisible.
        ///
        /// So a popup swallows input for the frame it was pushed or revealed on, and then for a short
        /// unscaled hold on top — long enough that a key held down across the boundary cannot cascade
        /// through two menus, short enough that nobody waiting to press RESUME ever notices.
        ///
        /// This is the WHOLE defence, not half of it. Being covered stops nothing, because a covered
        /// popup is not ticked in the first place; the entire hazard lives in the single frame on
        /// which a popup becomes the top one again, and that frame is this field.
        /// </summary>
        int _swallowFrame = -1;
        float _inputHold;

        // ------------------------------------------------------------------ lifecycle

        public void OnPush()
        {
            BuildCanvas(GetType().Name, SortingOrder);
            Compose();
            LayOutItems();
            OnComposed();
            ApplyPlates();
            // Put the board in its FIRST-FRAME pose here rather than waiting for the first Tick.
            // Between OnPush and the stack's next tick there is a rendered frame, and a board that
            // spends it at full size over a transparent scrim is a pop, not an entrance.
            AnimateBoard();

            // The frame Escape was pressed on is not a frame this menu may read.
            Deafen();
            ApplyCover();
        }

        /// <summary>
        /// The only teardown there is.
        ///
        /// The stack makes exactly one guarantee about a popup's end and this is it: OnPop runs, once,
        /// whatever happened in between. A popup may be pushed and popped inside a single frame
        /// without ever being ticked, and PopAll runs OnPop from the top down without calling
        /// OnRevealed on anything. So the canvas is destroyed here and nowhere else — a teardown that
        /// waited for a tick would leak a full-screen canvas on top of the game every time a
        /// confirmation was answered on the frame it appeared.
        /// </summary>
        public void OnPop()
        {
            _disposed = true;
            // The subclass's globals go back BEFORE the view goes away, so that anything it hands
            // control to next — a scene load, a transition — starts from a restored world.
            OnClosed();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _canvas = null;
            Items.Clear();
        }

        /// <summary>
        /// A popup has opened on top of us: sink back.
        ///
        /// The dim is what says WHICH board the player is being asked about. It is the emphasis idiom
        /// ComicSequence settled on for exactly this problem ("settled panels sink back and the hero
        /// holds the light"), and without it two boards of equal brightness stack up and the question
        /// on the top one reads as decoration on the bottom one.
        ///
        /// It is only a VISUAL change, and that is because the stack ticks its top popup and nothing
        /// else. Being covered already means not being ticked, so there is no input here to gate and
        /// no animation here to run — a covered board is a still image, which is exactly what a
        /// sunk-back board should be. The _covered flag below is kept anyway, as a cheap assertion
        /// rather than a mechanism: if the stack ever starts ticking the whole pile, this popup goes
        /// quiet instead of answering the same keypress as the one on top of it.
        /// </summary>
        public void OnCovered()
        {
            _covered = true;
            ApplyCover();
        }

        public void OnRevealed()
        {
            _covered = false;
            // Deafened again on the way back UP, not only on the way down: the key that dismissed the
            // popup above us is still down on this very frame.
            Deafen();
            ApplyCover();
            // The mouse may have been left resting on one of our items the whole time the child was
            // open. Forgetting where it was means it has to MOVE before it takes the highlight back,
            // which is the same rule MainMenu enforces for the same reason.
            _pointerSeen = false;
        }

        void Deafen()
        {
            _swallowFrame = Time.frameCount;
            _inputHold = 0.10f;
        }

        /// <summary>
        /// The covered look, applied at once rather than eased toward.
        ///
        /// Not a shortcut — the only thing that can work. The stack ticks its TOP popup and nothing
        /// else, so a covered popup is handed no time at all between OnCovered and OnRevealed and an
        /// animated dim would simply never advance a frame. Snapping is honest about that, and it
        /// reads fine anyway, because the board arriving on top is itself animating in and is where
        /// the eye already is.
        /// </summary>
        void ApplyCover()
        {
            if (_group == null) return;
            _group.alpha = _covered ? 0.45f : 1f;
        }

        // ------------------------------------------------------------------ the frame

        public bool Tick(float unscaledDt)
        {
            if (_disposed || _detached) return false;

            // Clamped, and the clamp is not paranoia. A browser tab regaining focus hands over a
            // quarter-second delta and the plate springs below are integrated explicitly — MainMenu
            // learned this the hard way and its comment is the one being obeyed here.
            float dt = Mathf.Min(unscaledDt, 0.05f);
            _clock += dt;

            _open = Mathf.MoveTowards(_open, 1f, dt / 0.20f);
            AnimateBoard();
            TickPlates(dt);

            // Belt and braces. The stack does not tick a covered popup, so this cannot fire today —
            // and if that ever changes, this is the line that stops one Enter being answered by two
            // boards. See OnCovered.
            if (_covered) return false;
            // The swallow, in the order the two halves expire.
            if (Time.frameCount <= _swallowFrame) return false;
            if (_inputHold > 0f) { _inputHold -= dt; return false; }

            HandleInput();
            // Nothing may follow HandleInput. An item's action can pop this popup — BACK TO MENU
            // does, via PopAll — which runs OnPop and destroys everything below us mid-call.
            //
            // The two guards are repeated here rather than trusted from the top of the method,
            // because HandleInput is exactly where they change: a popup that is off the stack must
            // never ALSO ask to be popped, or the stack pops something that belongs to somebody else.
            return _closeRequested && !_detached && !_disposed;
        }

        /// <summary>Ask the stack to pop us on this frame's return from Tick.</summary>
        protected void RequestClose() => _closeRequested = true;

        /// <summary>
        /// Which choice is under the marker when the board opens.
        ///
        /// Worth having as a seam rather than always starting at the top: a board asking a
        /// destructive question should open on the answer that changes nothing.
        /// </summary>
        protected void SetSelection(int index)
        {
            if (index < 0 || index >= Items.Count) return;
            _index = index;
            // Snapped, not sprung. The selection the board OPENS on is a fact about the layout, not
            // an animation — a marker that slides in from the first item reads as the board having
            // changed its mind on the player's behalf.
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i] == null) continue;
                Items[i].sel = i == index ? 1f : 0f;
                Items[i].selVel = 0f;
            }
        }

        /// <summary>
        /// Run a menu item's action, and then work out from the stack itself what just happened to us.
        ///
        /// A menu action in a stacked UI can do one of three things, and the popup that ran it has no
        /// other way to tell them apart:
        ///
        ///   the stack got SHORTER  — the action popped us (BACK TO MENU calls PopAll). We are gone;
        ///                            OnPop has already run and our canvas is destroyed. Never touch
        ///                            anything again, and above all never also return true from Tick,
        ///                            which would ask the stack to pop a popup that is not on it.
        ///   the stack got LONGER   — the action pushed a child (QUIT does). We stay, covered.
        ///   the stack is UNCHANGED — an ordinary action. Close afterwards if the caller wants that.
        ///
        /// Reading Depth on both sides is cheap and it is the only reading that cannot be wrong,
        /// because it asks the stack rather than assuming what the action did.
        /// </summary>
        protected void RunAction(Action action, bool closeAfter)
        {
            int before = PopupStack.Depth;
            action?.Invoke();
            int after = PopupStack.Depth;

            if (after < before)
            {
                _detached = true;
                // Cleared, not merely ignored. A subclass's action may legitimately have called
                // RequestClose before doing something that also popped the stack — ConfirmPopup's
                // CONFIRM does exactly that — and a stale request would be a second pop.
                _closeRequested = false;
                return;
            }
            if (after > before) return;
            if (closeAfter) _closeRequested = true;
        }

        // ------------------------------------------------------------------ input

        void HandleInput()
        {
            if (Items.Count == 0) return;

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;

            int move = 0;
            bool confirm = false;

            if (kb != null)
            {
                if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) move++;
                if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) move--;
                confirm = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame ||
                          kb.spaceKey.wasPressedThisFrame;
            }
            if (pad != null)
            {
                if (pad.dpad.down.wasPressedThisFrame) move++;
                if (pad.dpad.up.wasPressedThisFrame) move--;
                confirm |= pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame;
            }
            // Escape and B are deliberately NOT read here. ClosesOnEscape is true and the stack owns
            // that key: two systems both claiming to own "close the top popup" is how one of them
            // ends up closing two.

            if (move != 0)
            {
                Select(((_index + move) % Items.Count + Items.Count) % Items.Count);
                _pointerSeen = false;
            }

            int over = -1;
            bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;
            if (mouse != null)
            {
                Vector2 p = mouse.position.ReadValue();
                // The pointer only takes the highlight while it is MOVING. A mouse left resting on a
                // plate used to re-select it every frame, which meant the arrow keys could not move
                // off it — MainMenu's bug, and its fix.
                bool moved = !_pointerSeen || (p - _lastPointer).sqrMagnitude > 4f;
                _lastPointer = p;
                _pointerSeen = true;

                for (int i = 0; i < Items.Count; i++)
                {
                    if (Items[i]?.rect == null) continue;
                    // Null camera because the canvas is ScreenSpaceOverlay: for an overlay canvas the
                    // screen point IS the canvas point, and passing a camera here silently returns
                    // false for every rect on screen.
                    if (!RectTransformUtility.RectangleContainsScreenPoint(Items[i].rect, p, null)) continue;
                    over = i;
                    if (moved) Select(i);
                    break;
                }
            }

            if (over >= 0 && clicked) { Activate(over); return; }
            if (confirm) Activate(_index);
        }

        void Select(int index)
        {
            if (index < 0 || index >= Items.Count) return;
            _index = index;
        }

        void Activate(int index)
        {
            if (index < 0 || index >= Items.Count) return;
            var item = Items[index];
            if (item == null) return;

            _index = index;
            _pressed = index;
            // Kicked, not set. The plate is thrown past its pressed position and the spring brings it
            // back, which is the difference between a button that responds and one that changes state.
            item.press = 1f;
            item.pressVel = 9f;

            // Everything after this line may be running on a destroyed object, so there is nothing
            // after this line. See RunAction.
            RunAction(item.activate, closeAfter: false);
        }

        // ------------------------------------------------------------------ animation

        /// <summary>
        /// The board arriving. Ease-out-back on the scale and a short fall, which is the same landing
        /// the front page's masthead makes — a board that slides to a dead stop reads as a slide, and
        /// one that overshoots and settles reads as a board somebody put down.
        /// </summary>
        void AnimateBoard()
        {
            if (_board == null) return;

            float p = _open - 1f;
            float ease = 1f + p * p * (2.70158f * p + 1.70158f);
            float s = Mathf.Lerp(0.90f, 1f, ease);
            _board.localScale = new Vector3(s, s, 1f);
            _board.anchoredPosition = new Vector2(0f, (1f - ease) * -46f);
            // Nothing pinned to a board in this game is straight. The tilt unwinds as it lands.
            _board.localRotation = Quaternion.Euler(0f, 0f, -1.3f + (1f - ease) * 3.4f);

            if (_scrim != null)
            {
                var c = Scrim;
                c.a = _scrimAlpha * Mathf.Clamp01(_open * 1.6f);
                _scrim.color = c;
            }
        }

        /// <summary>
        /// One step of an explicit under-damped spring, lifted from MainMenu.
        ///
        /// Duplicated rather than reached for, because the front page's copy is private and this is
        /// four lines — the same trade TurfHud documents when it duplicates DuckUIBuilder.Frac. What
        /// matters is that the NUMBERS match, so a plate in the pause menu has the same weight as the
        /// identical-looking plate on the front page.
        /// </summary>
        static void Spring(ref float value, ref float velocity, float target, float dt,
                           float stiffness, float damping)
        {
            float d = 2f * Mathf.Sqrt(Mathf.Max(stiffness, 1e-4f)) * damping;
            velocity += ((target - value) * stiffness - velocity * d) * dt;
            value += velocity * dt;
        }

        void TickPlates(float dt)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                var it = Items[i];
                if (it == null) continue;
                Spring(ref it.sel, ref it.selVel, i == _index ? 1f : 0f, dt, 150f, 0.52f);
                Spring(ref it.press, ref it.pressVel, 0f, dt, 240f, 0.62f);
                // Floored: the press spring is under-damped and its first undershoot is large enough
                // to throw the plate off the board.
                if (it.press < -0.35f) { it.press = -0.35f; it.pressVel = 0f; }
            }
            if (_pressed >= 0 && _pressed < Items.Count && Items[_pressed] != null &&
                Items[_pressed].press < 0.25f) _pressed = -1;
            ApplyPlates();
        }

        void ApplyPlates()
        {
            float y = 0f, w = 0f;
            for (int i = 0; i < Items.Count; i++)
            {
                var it = Items[i];
                if (it == null || it.rect == null) continue;

                it.rect.localScale = Vector3.one * (1f + 0.06f * it.sel - 0.05f * it.press);
                it.rect.localRotation = Quaternion.Euler(0f, 0f, it.restTilt * (1f - 0.8f * it.sel));

                // Only the X is written. The Y is where LayOutItems put this row and nothing after
                // that is allowed to move it — a selection that slides sideways is a selection; a
                // column that also drifts vertically is a layout bug.
                it.rect.anchoredPosition = new Vector2(14f * it.sel, it.rect.anchoredPosition.y);

                if (it.label != null) it.label.color = Color.Lerp(Ink, InkHot, it.sel);
                if (it.plate != null) it.plate.color = Color.Lerp(Plate, PlateHot, it.sel);
                if (it.shadow != null)
                {
                    // The gap between plate and shadow is what reads as the plate coming off the
                    // board, and it closes when the plate is pressed, because a pressed plate is
                    // against the board.
                    float drop = 4f + 6f * it.sel - 10f * it.press;
                    it.shadow.offsetMin = new Vector2(drop, -drop);
                    it.shadow.offsetMax = new Vector2(drop, -drop);
                }

                float weight = Mathf.Max(it.sel, 0f);
                y += it.rect.anchoredPosition.y * weight;
                w += weight;
            }

            if (_pointer != null && Items.Count > 0)
            {
                // Follows the SPRINGS rather than the index, so it slides between plates and
                // overshoots with them instead of teleporting.
                if (w > 1e-3f) y /= w;
                _pointer.anchoredPosition = new Vector2(_pointer.anchoredPosition.x, y);
                float pulse = 1f + Mathf.Sin(_clock * 4.4f) * 0.09f;
                _pointer.localScale = new Vector3(pulse, pulse, 1f);
                _pointer.localRotation = Quaternion.Euler(0f, 0f, 45f + Mathf.Sin(_clock * 1.7f) * 4f);
            }
        }

        // ------------------------------------------------------------------ construction

        /// <summary>
        /// The popup's own canvas.
        ///
        /// SORTING ORDER is the one number here that is load-bearing. The round's HUD sits in the low
        /// hundreds, the comic pages in the low thousands, and the CURTAIN at 30000 — and the curtain
        /// must stay on top of everything, always, because it is the only object in the project whose
        /// entire job is to be the last thing composited while a scene is swapped underneath it. A
        /// popup that outranked it would sit over a transition it has no business being part of. So
        /// popups live at 25000 and above: clear of every HUD, under the curtain.
        ///
        /// No GraphicRaycaster, and no EventSystem is expected to exist. See the class comment.
        ///
        /// One known limitation, recorded rather than worked around: ScreenSpaceOverlay is composited
        /// by the UI system rather than drawn by a camera, so a Camera.Render() into a RenderTexture —
        /// which is how the project's review captures are taken — will not contain this popup.
        /// DefenceHud chose ScreenSpaceCamera to dodge exactly that. The curtain has the same
        /// limitation and keeps Overlay anyway, because a canvas bound to a camera dies with the
        /// camera, and the pause menu has to survive a camera being restaged underneath it.
        /// </summary>
        void BuildCanvas(string name, int sortingOrder)
        {
            _root = new GameObject($"~ {name}", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));

            // Left as a root object of the live scene ON PURPOSE, and not DontDestroyOnLoad. The only
            // action here that loads a scene tears the stack down first, so this is destroyed before
            // the load rather than surviving into the front page — which is precisely the failure a
            // DontDestroyOnLoad popup would produce, and there would be no way to close it.
            _canvas = _root.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = sortingOrder;

            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            // Even match: a browser window and a desktop window differ more in aspect than in size,
            // and biasing either axis walks the board across the frame.
            scaler.matchWidthOrHeight = 0.5f;

            _group = _root.GetComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;
        }

        protected Transform Root => _root != null ? _root.transform : null;

        /// <summary>The wash over the game behind. Alpha is the popup's own business.</summary>
        protected void BuildScrim(float alpha)
        {
            _scrimAlpha = alpha;
            var go = new GameObject("Scrim", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(Root, false);
            _scrim = go.GetComponent<Image>();
            _scrim.raycastTarget = false;
            var c = Scrim; c.a = 0f;
            _scrim.color = c;
            Stretch(_scrim.rectTransform);
        }

        /// <summary>
        /// The signboard: a cream-edged plank of the venue's own painted timber.
        ///
        /// Made of the same two pieces and the same two colours Curtain.BuildSign uses for the
        /// transition plate, so a pause board and a stage board read as having been carried in by the
        /// same groundskeeper rather than as two designers' idea of a panel.
        /// </summary>
        protected RectTransform BuildBoard(float width, float height)
        {
            var go = new GameObject("Board", typeof(RectTransform));
            go.transform.SetParent(Root, false);
            _board = (RectTransform)go.transform;
            _board.anchorMin = _board.anchorMax = new Vector2(0.5f, 0.5f);
            _board.pivot = new Vector2(0.5f, 0.5f);
            _board.sizeDelta = new Vector2(width, height);

            var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image));
            edge.transform.SetParent(_board, false);
            var er = (RectTransform)edge.transform;
            Stretch(er);
            er.offsetMin = new Vector2(-11f, -11f);
            er.offsetMax = new Vector2(11f, 11f);
            var ei = edge.GetComponent<Image>();
            ei.sprite = RoundedSprite();
            ei.type = Image.Type.Sliced;
            ei.raycastTarget = false;
            ei.color = BoardEdge;

            var face = new GameObject("Face", typeof(RectTransform), typeof(Image));
            face.transform.SetParent(_board, false);
            Stretch((RectTransform)face.transform);
            var fi = face.GetComponent<Image>();
            fi.sprite = RoundedSprite();
            fi.type = Image.Type.Sliced;
            fi.raycastTarget = false;
            fi.color = BoardFace;

            return _board;
        }

        /// <summary>A hairline of wood across the board, between the heading and the choices.</summary>
        protected void BuildRule(float y, float width)
        {
            if (_board == null) return;
            var go = new GameObject("Rule", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_board, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, 3f);
            rt.anchoredPosition = new Vector2(0f, y);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = new Color(BoardEdge.r, BoardEdge.g, BoardEdge.b, 0.30f);
        }

        /// <summary>A line of type on the board, centred at <paramref name="y"/>.</summary>
        protected TextMeshProUGUI BuildText(string name, string text, float size, float y,
                                            Vector2 rect, Color colour, bool wrap = false,
                                            float outline = 0.16f, float spacing = 6f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(_board != null ? (Transform)_board : Root, false);
            var t = go.GetComponent<TextMeshProUGUI>();

            // TMP_Settings' default rather than a serialized reference: this object is created at
            // runtime and has no inspector for anybody to assign a font in. Without it the text
            // renders as nothing at all and looks like the component never ran.
            if (t.font == null && TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;

            t.text = text;
            t.alignment = TextAlignmentOptions.Center;
            t.color = colour;
            t.raycastTarget = false;
            t.characterSpacing = spacing;
            // Wrap decided BEFORE auto-sizing is asked for a size. The ordering is load-bearing —
            // see the note on DuckUIBuilder.AddText and TurfHud.CardText.
            t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Truncate;
            t.fontSize = size;
            t.enableAutoSizing = true;
            t.fontSizeMax = size;
            t.fontSizeMin = Mathf.Max(11f, size * 0.55f);
            t.margin = new Vector4(8f, 4f, 8f, 4f);

            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = rect;
            rt.anchoredPosition = new Vector2(0f, y);

            Outline(t, outline);
            return t;
        }

        /// <summary>
        /// A dark contour on one label, set through TMP's per-object material INSTANCE.
        ///
        /// Touching fontSharedMaterial here would write the outline into the font ASSET and every
        /// other piece of text in the project would inherit it — DefenceHud's warning, obeyed.
        /// </summary>
        static void Outline(TextMeshProUGUI t, float width)
        {
            if (t == null || width <= 0f) return;
            var m = t.fontMaterial;
            if (m == null) return;
            m.EnableKeyword("OUTLINE_ON");
            t.outlineColor = new Color32(18, 24, 20, 235);
            t.outlineWidth = width;
        }

        /// <summary>
        /// One choice: shadow, plate, label. Added in the order they should be read; their vertical
        /// positions are dealt out afterwards by <see cref="LayOutItems"/>.
        /// </summary>
        protected void AddItem(string text, Action activate)
        {
            var go = new GameObject($"Item {Items.Count}", typeof(RectTransform));
            go.transform.SetParent(_board != null ? (Transform)_board : Root, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(ItemWidth, ItemHeight);

            var shadowGo = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
            shadowGo.transform.SetParent(rt, false);
            var sr = (RectTransform)shadowGo.transform;
            Stretch(sr);
            var si = shadowGo.GetComponent<Image>();
            si.sprite = RoundedSprite();
            si.type = Image.Type.Sliced;
            si.raycastTarget = false;
            si.color = new Color(0.05f, 0.11f, 0.06f, 0.55f);

            var plateGo = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            plateGo.transform.SetParent(rt, false);
            Stretch((RectTransform)plateGo.transform);
            var pi = plateGo.GetComponent<Image>();
            pi.sprite = RoundedSprite();
            pi.type = Image.Type.Sliced;
            pi.raycastTarget = false;
            pi.color = Plate;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(rt, false);
            Stretch((RectTransform)labelGo.transform);
            var t = labelGo.GetComponent<TextMeshProUGUI>();
            if (t.font == null && TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
            t.text = text;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Ink;
            t.raycastTarget = false;
            t.fontStyle = FontStyles.Bold;
            t.characterSpacing = 8f;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Truncate;
            t.fontSize = 34f;
            t.enableAutoSizing = true;
            t.fontSizeMax = 34f;
            t.fontSizeMin = 18f;
            // No outline on the plate labels: dark ink on a cream plate needs no help, and a contour
            // there would read as a sticker rather than as paint.

            Items.Add(new Item
            {
                rect = rt,
                shadow = sr,
                plate = pi,
                label = t,
                activate = activate,
                // Nothing on this board is square. Alternating a fraction of a degree is the whole
                // difference between three painted plates and three rows of a web form.
                restTilt = (Items.Count % 2 == 0 ? 0.5f : -0.6f)
            });
        }

        protected virtual float ItemWidth => 560f;
        protected virtual float ItemHeight => 84f;
        protected virtual float ItemStep => 104f;
        /// <summary>Where the middle of the column of choices sits, relative to the board's middle.</summary>
        protected virtual float ItemsCentreY => -70f;

        /// <summary>
        /// Deal the choices down the board, and put the marker beside them.
        ///
        /// Done after every AddItem rather than during, because the column is centred on its own
        /// middle: an item's position depends on how many there turned out to be, and on this board
        /// that number is decided at runtime — QUIT is not offered in a browser that has nothing
        /// to quit.
        /// </summary>
        void LayOutItems()
        {
            int n = Items.Count;
            for (int i = 0; i < n; i++)
            {
                if (Items[i]?.rect == null) continue;
                float y = ItemsCentreY + (n - 1) * 0.5f * ItemStep - i * ItemStep;
                Items[i].rect.anchoredPosition = new Vector2(0f, y);
            }

            if (n == 0 || _board == null) return;

            // The marker. A gold diamond made of GEOMETRY — the rounded plate, turned forty-five
            // degrees — rather than a glyph, and this one was checked rather than assumed.
            //
            // The project's TMP default is LiberationSans SDF with m_AtlasPopulationMode: 0, i.e.
            // STATIC: 250 glyphs are baked into the atlas and nothing else exists at runtime, no
            // matter what the source .ttf contains. U+25C6 is not one of them. (Nor is it in
            // TurfHud's crown line, which is a real missing-glyph box in the Bloom Rush HUD today —
            // worth someone's time, but not this file's to fix.) A pointer is the one element on
            // this board that must never be a hollow rectangle, so it is not made of type.
            var go = new GameObject("Marker", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_board, false);
            _pointer = (RectTransform)go.transform;
            _pointer.anchorMin = _pointer.anchorMax = new Vector2(0.5f, 0.5f);
            _pointer.pivot = new Vector2(0.5f, 0.5f);
            _pointer.sizeDelta = new Vector2(17f, 17f);
            _pointer.anchoredPosition = new Vector2(-ItemWidth * 0.5f - 40f, 0f);
            var img = go.GetComponent<Image>();
            img.sprite = RoundedSprite();
            // Simple, NOT sliced. The plate sprite carries a sixteen-pixel border on every edge and
            // this rect is seventeen pixels across — a nine-slice whose borders overlap in the middle
            // draws a smear rather than a shape. At this size the whole sprite is corner anyway.
            img.type = Image.Type.Simple;
            img.raycastTarget = false;
            img.color = Gold;
        }

        protected static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        // ------------------------------------------------------------------ the plank

        static Sprite _rounded;

        /// <summary>
        /// A nine-sliced rounded panel, generated once, so the popups ship no art file.
        ///
        /// This is Curtain.RoundedSprite, duplicated: that one is private, and the alternative — a
        /// texture asset — is a serialized file, which is the one thing these views are not allowed
        /// to own. Twenty lines of pixels is a cheaper duplication than a dependency.
        ///
        /// HideAndDontSave on both texture and sprite, so a domain reload in the editor cannot leave
        /// them behind in the scene.
        ///
        /// PROTECTED INTERNAL rather than protected, which is a widening worth one sentence: the
        /// pause BUTTON on the three stage HUDs is cut from this same plank and is not a popup, so
        /// it is not a subclass. The alternative was a third copy of these twenty lines — there are
        /// already two, this one and Curtain's — and three copies of a shape is how two of them stop
        /// matching. Internal keeps it inside the game assembly, which is as far as it has any
        /// business travelling.
        /// </summary>
        protected internal static Sprite RoundedSprite()
        {
            if (_rounded != null) return _rounded;

            const int n = 48, r = 14;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false, false)
            {
                name = "Popup plate",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Max(r - x - 0.5f, x + 0.5f - (n - r), 0f);
                float dy = Mathf.Max(r - y - 0.5f, y + 0.5f - (n - r), 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy) - r;
                float a = Mathf.Clamp01(0.5f - d);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);

            _rounded = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                                     SpriteMeshType.FullRect, new Vector4(r + 2, r + 2, r + 2, r + 2));
            _rounded.name = "Popup plate";
            _rounded.hideFlags = HideFlags.HideAndDontSave;
            return _rounded;
        }
    }

    // ======================================================================================

    /// <summary>
    /// THE PAUSE BOARD: what Escape now does instead of walking the player out of the championship.
    ///
    /// Escape used to lead, in a couple of presses, straight back to the front page — which is the
    /// wrong default for a game whose whole loop is "have another go". A player reaching for Escape
    /// mid-round is nearly always asking for a BREATH, not a resignation, and the previous behaviour
    /// made the most common intention the hardest one to express and the rarest one accidental.
    ///
    /// So Escape now stops the world and puts a board up. Leaving is still one press away — it is
    /// just no longer the only thing on offer, and the destructive half of it is behind a question.
    ///
    /// ---- what is on the board, and what is not ----
    ///
    ///   RESUME        closes this popup. Nothing else; the stack puts the clock back.
    ///   BACK TO MENU  hands over to StageLauncher, which runs the real transition. ALWAYS
    ///                 OFFERED, in every scene, without exception — see BuildItems, which used to
    ///                 leave it out and was wrong to.
    ///   QUIT          asks first, on a popup of its own, which is also the thing that proves the
    ///                 stack actually stacks.
    ///
    /// What is deliberately absent is a RESTART. The round already has retry flows of its own with
    /// their own transitions and their own scoring consequences, and a second door into them from a
    /// pause menu would be a second copy of that decision free to disagree with the first.
    /// </summary>
    public sealed class PausePopup : PopupView
    {
        public override string Id => "pause";
        public override bool PausesTime => true;
        public override bool BlocksDriving => true;
        public override bool ClosesOnEscape => true;

        /// <summary>
        /// Clear of every HUD in the game and five thousand below the curtain. See BuildCanvas.
        /// </summary>
        protected override int SortingOrder => 25000;

        // ------------------------------------------------------------------ the seam

        /// <summary>
        /// Hand the stack a way to make one of these, before any scene loads.
        ///
        /// THIS IS THE ONLY WIRE between Escape and this file, and it is worth being blunt about why
        /// it is a static hook rather than a reference somebody drags in an inspector.
        ///
        /// The pause board has to exist in FIVE scenes — the round, the menu's own game scene, and
        /// the standalone stage scenes, which are no longer only an editor's way in: the front
        /// page's class list hands a player straight into one — and it is built entirely in code,
        /// so there is no asset for anyone to drag anywhere. A
        /// serialized reference would mean wiring it into all five and remembering to on the sixth.
        /// GameDirector, which reads Escape, must not have to know this type exists; it asks the
        /// stack for "the pause popup" and the stack asks this factory.
        ///
        /// BeforeSceneLoad rather than AfterSceneLoad, because Escape has to work on the first frame
        /// of the first scene, and a player who has just alt-tabbed back into a loading game is
        /// exactly the player who presses it.
        ///
        /// If this method ever stops running, Escape does nothing at all — a silent, total failure
        /// with no symptom other than a dead key. So it is deliberately trivial: no scene lookups, no
        /// FindObjectOfType, no assumptions about anything having been created yet. Assigning a
        /// closure cannot fail.
        ///
        /// Two rules the factory itself has to obey, both of which a "cheaper" version breaks:
        ///
        ///   IT MUST BUILD A NEW ONE EVERY TIME. A cached static instance would be handed back on
        ///   the second play session pointing at a canvas Unity destroyed with the first, and the
        ///   pause menu would open as nothing at all. The stack deliberately does not clear
        ///   PauseFactory at session reset, because Unity gives no ordering guarantee between two
        ///   RuntimeInitializeOnLoadMethods and a clear that ran after this assignment would be the
        ///   dead-key failure above. So the closure must be safe to call at any age, which a
        ///   constructor is and a cache is not.
        ///
        ///   IT MUST NEVER RETURN NULL. Push(null) is a silent no-op, so a null here is once again
        ///   Escape appearing broken with nothing in the console.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterWithStack()
        {
            PopupStack.PauseFactory = () => new PausePopup();
        }

        // ------------------------------------------------------------------ the board

        protected override float ItemsCentreY => -78f;

        protected override void Compose()
        {
            BuildScrim(0.78f);

            // Sized from the number of choices rather than fixed, because that number is decided at
            // runtime — a board with an empty slot where BACK TO MENU would have been is a board with
            // a hole in it.
            int n = CountItems();
            float height = 268f + n * ItemStep;
            BuildBoard(860f, height);
            float half = height * 0.5f;

            BuildText("Kicker", "THE CHAMPIONSHIP WAITS", 28f, half - 56f,
                      new Vector2(700f, 40f), Gold, false, 0.20f, 14f);
            BuildText("Title", "PAUSED", 86f, half - 132f,
                      new Vector2(760f, 108f), Cream, false, 0.13f, 10f)
                .fontStyle = FontStyles.Bold;
            BuildRule(half - 196f, 660f);

            BuildItems();

            // A quiet line of instructions along the bottom edge. Small, and worth its space: this is
            // the first screen in the game that asks to be OPERATED rather than watched, and the
            // player has just learned that Escape opens it — they have not learned that it also
            // closes it.
            //
            // Spelled out in WORDS rather than in arrow glyphs, and that is not a style choice. The
            // project's TMP font asset is LiberationSans SDF in STATIC atlas mode with 250 glyphs
            // baked — ASCII plus a handful of punctuation. U+2191/U+2193 are not among them and
            // would render as the missing-glyph box on every platform. The middot below (U+00B7) IS
            // in the atlas, which is why it is the one ornament here.
            BuildText("Hint", "UP / DOWN  CHOOSE   ·   ENTER  CONFIRM   ·   ESC  RESUME", 20f,
                      -half + 40f, new Vector2(780f, 32f),
                      new Color(BoardEdge.r, BoardEdge.g, BoardEdge.b, 0.62f), false, 0.14f, 8f);
        }

        /// <summary>
        /// How many choices this scene can honestly offer. Kept next to BuildItems and in step with
        /// it: the board is sized from this and filled from that, and the two disagreeing is a board
        /// with a gap in it or a choice hanging off the bottom.
        /// </summary>
        static int CountItems()
        {
            int n = 2;                                  // RESUME and BACK TO MENU, always
            if (ControlsPrimer.HasCardHere) n++;
            if (CanOfferQuit) n++;
            return n;
        }

        void BuildItems()
        {
            // RESUME does nothing but ask to be popped. The stack owns putting Time.timeScale back
            // and letting the mower be driven again — this popup declared PausesTime and
            // BlocksDriving and has no business unwinding either of them by hand.
            AddItem("RESUME", RequestClose);

            // CONTROLS, second, and it is here because the primer stopped being unmissable.
            //
            // The controls card used to go up in front of every stage of every run, so the pause
            // board never had to answer "which key was the horn again" — the game had just said so,
            // twice. It is now shown ONCE IN A PLAYER'S LIFE (ControlsPrimer's own note argues why
            // a card the player has already read three times is chrome), and a game that teaches you
            // something once and then has nowhere to look it up has not taught you, it has told you.
            // So this row is not a convenience bolted on beside that change; it is the other half of
            // it, and the two would be wrong shipped apart.
            //
            // It PUSHES the primer rather than drawing a list of its own, which is the entire reason
            // the popups are a stack: the pause board stays exactly where it is, dimmed, with the
            // card in front of it, and Escape peels one layer back to the board rather than dropping
            // the player into the round. There is one control list in the project and both routes to
            // it are built from the same lines — see ControlsPrimer.ForPause, which is a factory
            // precisely so this file never learns what a control is.
            //
            // Offered only where there is a stage to describe. On the front page's pause board there
            // are no controls to list, and a plate opening a card headed LAWN ART over the menu would
            // be the board describing a place the player is not standing in.
            if (ControlsPrimer.HasCardHere) AddItem("CONTROLS", ShowControls);

            // BACK TO MENU IS ALWAYS HERE. It used to be omitted where GameDirector.Instance was
            // null, and that omission is worth recording because the reasoning behind it was sound
            // and the conclusion was a trap.
            //
            // The argument was: the standalone stage scenes have no director, so there is no
            // BackToMenu to call and nothing this item could honestly do — and a button that does
            // nothing is worse than no button, because the player concludes the game is broken. All
            // true. What it missed is the OTHER half of the board: in those same scenes RESUME is
            // then the only choice, and on the web QUIT is not offered either, so Escape opens a
            // menu whose every option is "carry on playing". The player is not merely denied a
            // door, they are shut in. That was survivable while the only way into a standalone
            // stage was an editor opening the scene by hand. The class list on the front page makes
            // it a place a real player can be, and being shut in a room is worse than being shown a
            // door that leads somewhere plainer than expected.
            //
            // The fix is not a special case here — it is that leaving is no longer GameDirector's
            // private business. StageLauncher.ReturnToMenu goes to the front page with a director
            // or without one, so this item has something honest to do in every scene the game can
            // reach, and this board no longer has to know which kind of scene it is standing in.
            AddItem("BACK TO MENU", BackToMenu);

            if (CanOfferQuit)
                AddItem(QuitLabel, AskToQuit);
        }

        // ------------------------------------------------------------------ the world behind

        /// <summary>
        /// What the game is turned down to while the board is up.
        ///
        /// DUCKED, not silenced, and the difference is the point. The popup stack does not touch
        /// AudioListener.pause and should not — a game that goes completely silent has stopped, and
        /// this one has not: the player is still in a round, the crowd is still there, and they are
        /// coming back to it in a moment. A duck says the game is waiting; silence says it is over,
        /// and the player who came here for a breath would be told they had quit.
        ///
        /// It also has to be done from HERE rather than left to the pause itself. Time.timeScale
        /// hitting zero does not quieten a single AudioSource — pitch and volume are not on that
        /// clock — so an engine drone whose pitch is driven by a road speed that has frozen simply
        /// holds one flat tone under the menu for as long as the player leaves it up, which is the
        /// most conspicuous possible way to sound broken.
        /// </summary>
        const float DuckedVolume = 0.30f;

        protected override void OnComposed()
        {
            // Routed through MasterAudio rather than written here, and the reason is the exact bug
            // the old comment was worried about arriving. This used to capture AudioListener.volume
            // and restore it on close — correct while nothing else touched that field, and wrong the
            // moment a master volume setting did. A player who opened this board and turned the
            // sound down would have had their new setting overwritten by the captured old one the
            // instant they pressed RESUME, silently, with the slider still showing the value they
            // chose. MasterAudio owns the field now and multiplies Master by Duck; this only has an
            // opinion about the duck, which is the only part that is ours.
            MasterAudio.Duck = DuckedVolume;
        }

        /// <summary>
        /// Put the sound back, in OnPop and only in OnPop.
        ///
        /// This is the reason the base calls OnClosed from there rather than anywhere more
        /// convenient: OnPop is the single call the stack guarantees. A restore hung off a tick, or
        /// off being uncovered, would leave a game running at a third of its volume with no board on
        /// screen to explain it and nothing the player could do about it — the worst class of bug
        /// this file can produce, because it survives the thing that caused it.
        /// </summary>
        protected override void OnClosed()
        {
            MasterAudio.Duck = 1f;
        }

        // ------------------------------------------------------------------ what the choices do

        /// <summary>
        /// Whether there is a championship in progress behind this board, as opposed to a single
        /// class being played on its own from the front page's class list.
        ///
        /// It no longer decides whether the player can LEAVE — that question is closed, and the
        /// answer is always yes. All it decides now is whether QUIT is a second, different thing to
        /// offer on the web; see CanOfferQuit.
        /// </summary>
        static bool HasChampionship => GameDirector.Instance != null;

        /// <summary>
        /// Whether QUIT can be made to mean anything here.
        ///
        /// Application.Quit() DOES NOTHING in a WebGL player, and WebGL is this game's actual
        /// shipping target — so on the browser the item only exists if there is somewhere honest to
        /// send the player instead, which is the front page. Everywhere else — editor, desktop
        /// player — quitting is real and the item always appears.
        ///
        /// On the web it is gated on there being a championship, and the reason has changed. It is
        /// no longer that a standalone stage has nowhere to go — it has, now that BACK TO MENU is
        /// unconditional. It is that in a standalone stage QUIT would go to exactly the same place
        /// as the row above it. Two plates on one board that do one thing, one of them behind a
        /// confirmation, is a board that makes the player wonder which one they wanted. Where there
        /// IS a championship the two are genuinely different acts — one leaves a class, the other
        /// abandons a run in progress — and the question the confirmation asks is what tells them
        /// apart.
        /// </summary>
        static bool CanOfferQuit
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            get => HasChampionship;
#else
            get => true;
#endif
        }

        /// <summary>
        /// The item is named after what it will actually do, which on the web is not "quit".
        ///
        /// Shipping a button labelled QUIT that silently fails to quit is worse than not shipping it:
        /// the player presses it, the game carries on, and the only conclusion available to them is
        /// that the game is broken. So in a browser the choice says where it goes.
        /// </summary>
        static string QuitLabel
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            get => "QUIT TO FRONT PAGE";
#else
            get => "QUIT GAME";
#endif
        }

        static string QuitPrompt
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            get => "LEAVE THE CHAMPIONSHIP AND GO BACK TO THE FRONT PAGE?";
#else
            get => "QUIT THE GAME? THIS ROUND WILL NOT BE SAVED.";
#endif
        }

        /// <summary>
        /// The word on the yes button, which has to name the same act the prompt describes. A board
        /// that asks "go back to the front page?" and answers it with "QUIT" is asking one question
        /// and offering the answer to a different one.
        /// </summary>
        static string QuitConfirmWord
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            get => "LEAVE";
#else
            get => "QUIT";
#endif
        }

        /// <summary>
        /// Out of whatever this is and back to the gate.
        ///
        /// StageLauncher owns the whole move — curtain down, statics cleared, timescale restored,
        /// scene load, and the choice between handing the job to GameDirector where there is one and
        /// doing it plainly where there is not. None of that is duplicated here, and the director is
        /// deliberately not named: this board is the same board in the championship and in a single
        /// class opened from the front page, and the moment it starts asking which one it is
        /// standing in, it grows two behaviours and one of them stops being tested.
        ///
        /// THE ORDER OF THESE TWO LINES IS THE WHOLE FUNCTION, and getting it backwards produces a
        /// bug that looks like anything but this. The leave sets Time.timeScale back to 1 on its way
        /// out, and it has to, because two different stages warp the clock and arriving at the front
        /// page in slow motion has no recovery short of a restart. But the popup stack re-applies
        /// its own pause gate in PostLateUpdate on every frame the stack is not empty — so with the
        /// popups still up, that restore is stomped back to zero later the same frame, and the
        /// entire leave-to-menu transition plays frozen until the scene actually swaps. Curtain
        /// down, sign sliding in, all of it, at timeScale zero.
        ///
        /// So the boards come off FIRST. Then the launcher is asked to leave, into a game whose
        /// clock nothing is holding.
        ///
        /// PopAll rather than a close request, for two reasons: a close request is only honoured
        /// when Tick returns, which is after this runs and therefore too late, and everything on the
        /// stack is about a round that is now over rather than just this one board.
        ///
        /// Nothing may follow the hand-over. It can load a scene, and a scene load destroys the
        /// canvas this popup is holding — see RunAction, which is watching the stack depth either
        /// side of this call and will notice that PopAll took us off it.
        /// </summary>
        void BackToMenu()
        {
            PopupStack.PopAll();
            StageLauncher.ReturnToMenu();
        }

        /// <summary>
        /// Put the controls card in front of this board.
        ///
        /// The same shape as <see cref="AskToQuit"/> and for the same reason: a push, so this board
        /// survives underneath and the player comes back to it rather than to the round. The base
        /// works out for itself that the stack got LONGER and stays put — see RunAction, which reads
        /// the depth either side of a menu action precisely so an item that opens a child does not
        /// also close its parent.
        ///
        /// Checked again rather than trusted, even though the row only exists when the answer is yes.
        /// The board is built once at push and a player can leave it open across a stage unloading
        /// underneath them; PopupStack.Push(null) is a silent no-op, which would present as a plate
        /// that does nothing at all.
        /// </summary>
        void ShowControls()
        {
            var card = ControlsPrimer.ForPause();
            if (card != null) PopupStack.Push(card);
        }

        /// <summary>
        /// QUIT does not quit. It asks.
        ///
        /// This is the choice the popup STACK exists for. A confirmation is not a different screen
        /// that replaces this one — the pause board stays exactly where it is, dimmed, with the
        /// question on a smaller board in front of it, and cancelling puts the player back where they
        /// were with no state to restore because nothing was ever unwound.
        /// </summary>
        void AskToQuit()
        {
            PopupStack.Push(new ConfirmPopup(QuitPrompt, Quit, QuitConfirmWord, "STAY"));
        }

        /// <summary>
        /// The honest quit, which on the web is a walk to the gate.
        ///
        /// Application.Quit() is a documented no-op in a WebGL player: there is no process to end and
        /// a page cannot close a tab it did not open. Since WebGL is what this game actually ships as,
        /// a quit path that only works in the editor is a quit path that only works for the people
        /// building it. The front page is the closest true equivalent the browser has to "I am done
        /// with this run", so that is where a browser quit goes — and the LABEL says so, above, rather
        /// than the player finding out by pressing it.
        ///
        /// PopAll comes first on every branch. On the web that is mandatory and for the same reason
        /// it is in BackToMenu — the stack holds Time.timeScale at zero for as long as it is not
        /// empty, and the walk to the front page would run frozen. On the desktop branch it is
        /// merely right: Application.Quit is not instant, and the last thing the player should be
        /// looking at while the process winds down is the game, not a menu they have finished with.
        ///
        /// The web branch used to read the director itself and log a warning where there was none —
        /// a dead button standing in for the case CanOfferQuit was supposed to have prevented. It
        /// goes through StageLauncher now, which has an answer in every scene, so the branch has no
        /// unreachable arm left to rot. CanOfferQuit still hides the item in a standalone class, but
        /// it does so because the row would DUPLICATE the one above it rather than because this code
        /// would have nowhere to go, and if that gate is ever loosened the button still works.
        /// </summary>
        static void Quit()
        {
#if UNITY_EDITOR
            // The editor ignores Application.Quit as well, which is how a broken quit survives review
            // all the way to a build.
            PopupStack.PopAll();
            UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
            PopupStack.PopAll();
            StageLauncher.ReturnToMenu();
#else
            PopupStack.PopAll();
            Application.Quit();
#endif
        }
    }
}
