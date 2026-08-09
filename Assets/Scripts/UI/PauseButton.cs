using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckMow.UI
{
    /// <summary>
    /// THE ONE THING ON A STAGE'S HUD THE PLAYER CAN PRESS: a small painted plate in the top corner
    /// that opens the pause board.
    ///
    /// Escape has opened that board for a while, and Escape is not enough. The game ships as WebGL
    /// and is played on machines with touchscreens through <see cref="TouchPointerBridge"/>, where
    /// there is no Escape key at all — so on a phone the pause board, the settings inside it and the
    /// way out of a stage were all unreachable. Even on a desktop, a keyboard-only door is a door
    /// nobody is told about: nothing on screen during a match has ever said the key exists, and the
    /// controls card that did say so is now shown once in a player's life.
    ///
    /// ---- why this is a class rather than three widgets ----
    ///
    /// It is on ALL THREE stages, which have three completely different HUDs: the lawn's is baked by
    /// DuckUIBuilder onto a ScreenSpaceCamera canvas, the rally's is baked by DuckRallyBuilder, and
    /// Bloom Rush builds its own ScreenSpaceOverlay canvas at runtime. Three copies of a hit test, a
    /// hover spring and a plate would be three things to keep in step, and this project's HUDs have
    /// already drifted apart once — the whole reason RallyHud was rewritten to bind a baked canvas.
    /// So the button is written once and each HUD attaches one and steps it.
    ///
    /// ---- and why it polls ----
    ///
    /// THIS PROJECT SHIPS NO EventSystem, NO InputModule AND NO GraphicRaycaster, deliberately, and
    /// that is stated in three places: <see cref="Curtain"/>'s canvas note, MainMenu's class note,
    /// and PopupView's. A <see cref="Button"/> with an onClick listener here would compile, build,
    /// look completely correct in the hierarchy and never fire once. So the pointer is hit-tested by
    /// hand against this plate's RectTransform every frame, exactly as MainMenu.PointerOver does,
    /// and the click is read off the device. Do not "fix" this by adding a raycaster — the two
    /// canvases it would have to go on are the two that are least able to afford one, and the hit
    /// test it would perform is the four lines below.
    ///
    /// Touch is not special-cased, and does not need to be: TouchPointerBridge mirrors the primary
    /// finger onto the Mouse device precisely so that screens which are correct for a pointer are
    /// correct for a tap without knowing anything about touch.
    /// </summary>
    public sealed class PauseButton
    {
        // ------------------------------------------------------------------ the palette
        //
        // Taken as NUMBERS from PopupView's own table rather than eyeballed against it — the
        // discipline TurfHud sets out at length, and the reason the pause board and the plate that
        // opens it are the same cream. They cannot be READ from there: they are protected statics on
        // a base class this is not, and widening them so one button can see them would be a worse
        // trade than four literals with this comment attached.

        static readonly Color Plate = new(0.95f, 0.92f, 0.84f);
        static readonly Color PlateHot = new(1.00f, 0.97f, 0.88f);
        static readonly Color Ink = new(0.16f, 0.12f, 0.09f);
        static readonly Color Shadow = new(0.05f, 0.11f, 0.06f, 0.55f);

        // ------------------------------------------------------------------ layout

        /// <summary>Side of the plate, in the 1920x1080 reference the HUD canvases all scale from.</summary>
        const float Size = 62f;

        /// <summary>
        /// The default corner: hard top right, one plate's own margin in from both edges.
        ///
        /// An ANCHOR plus an offset rather than a bare pixel inset, because the stages disagree about
        /// what is in that corner and a fixed inset would be right on two of them. See
        /// <see cref="Attach"/>.
        /// </summary>
        static readonly Vector2 CornerAnchor = new(1f, 1f);
        static readonly Vector2 CornerOffset = new(-34f, -34f);

        readonly Canvas _canvas;
        readonly Camera _hitCamera;
        readonly CanvasGroup _group;
        readonly RectTransform _rect;
        readonly Image _plate;

        float _hover;
        float _press;
        float _shown;
        Vector2 _lastPointer;
        bool _pointerSeen;

        PauseButton(Canvas canvas, RectTransform rect, CanvasGroup group, Image plate)
        {
            _canvas = canvas;
            _rect = rect;
            _group = group;
            _plate = plate;
            // Resolved ONCE, here, and this is the line that decides whether the button can be
            // clicked at all. RectTransformUtility.RectangleContainsScreenPoint wants the camera the
            // canvas is drawn through — and for a ScreenSpaceOverlay canvas it wants NULL, because
            // for an overlay the screen point IS the canvas point. Passing a camera to an overlay
            // silently returns false for every rect on screen, which is the exact shape of "the
            // button is there and nothing happens" this whole file exists to avoid.
            _hitCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
        }

        // ------------------------------------------------------------------ construction

        /// <summary>
        /// Put a pause plate on a HUD canvas.
        ///
        /// <paramref name="anchor"/> and <paramref name="offset"/> exist because the three stages
        /// genuinely do not have the same corner free, and pretending otherwise would put this plate
        /// on top of something on one of them. Bloom Rush keeps its map in the top LEFT and the goose
        /// rally's clock is top centre, so both leave the corner empty — but the lawn's HUD parks the
        /// SHAPE CARD there, at 0.815..0.985 of the frame, which is the one thing on that screen the
        /// player has to be able to see. So the lawn passes an anchor a little further in, and it
        /// passes an ANCHOR rather than a bigger pixel inset on purpose: the shape card is placed in
        /// fractions of the frame, so only a fraction stays clear of it at every aspect ratio a
        /// browser window can be dragged into.
        ///
        /// Returns null rather than throwing when there is no canvas to hang it on. A HUD without a
        /// pause button is a HUD; a HUD that threw during Awake is a stage that does not start.
        /// </summary>
        public static PauseButton Attach(Canvas canvas) => Attach(canvas, CornerAnchor, CornerOffset);

        public static PauseButton Attach(Canvas canvas, Vector2 anchor, Vector2 offset)
        {
            if (canvas == null) return null;

            var go = new GameObject("Pause button", typeof(RectTransform), typeof(CanvasGroup));
            var rect = (RectTransform)go.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(Size, Size);
            rect.anchoredPosition = offset;
            // Last child, so it is composited over anything the HUD builder laid down before it. It
            // is the only element on these canvases that has to be FOUND rather than merely read.
            rect.SetAsLastSibling();

            var group = go.GetComponent<CanvasGroup>();
            group.interactable = false;
            // Explicitly false, and worth the line: there is no raycaster on any of these canvases,
            // so this changes nothing today — but leaving it true would state that this plate expects
            // to be raycast at, which is the belief that puts an EventSystem into the project.
            group.blocksRaycasts = false;
            group.alpha = 0f;

            // The shadow, the plate and the two bars, in the order they are stacked. The same
            // generated plank every plate in the popup kit is cut from, so this reads as one more
            // piece of the venue's painted signage rather than as a browser control.
            var shadow = Piece(rect, "Shadow", Shadow, PopupView.RoundedSprite(), Image.Type.Sliced);
            shadow.rectTransform.offsetMin = new Vector2(4f, -4f);
            shadow.rectTransform.offsetMax = new Vector2(4f, -4f);

            var plate = Piece(rect, "Plate", Plate, PopupView.RoundedSprite(), Image.Type.Sliced);

            // TWO BARS RATHER THAN A WORD, and that is a decision about the font rather than about
            // taste. The project's TMP asset is LiberationSans SDF in STATIC atlas mode with 250
            // glyphs baked — PausePopup and ControlsPrimer both record what that costs — so a glyph
            // pause symbol would be a missing-glyph box, and the word PAUSE would be the one control
            // on screen that only reads in English. Two rectangles are neither.
            Bar(rect, "Bar L", -9f);
            Bar(rect, "Bar R", 9f);

            return new PauseButton(canvas, rect, group, plate);
        }

        static Image Piece(RectTransform parent, string name, Color colour, Sprite sprite,
                           Image.Type type)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = type;
            img.raycastTarget = false;
            img.color = colour;
            return img;
        }

        static void Bar(RectTransform parent, string name, float x)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(8f, 26f);
            rt.anchoredPosition = new Vector2(x, 0f);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = Ink;
        }

        // ------------------------------------------------------------------ the frame

        /// <summary>
        /// One frame: decide whether the plate belongs on screen, animate it, and answer a click.
        ///
        /// Driven by the HUD that owns it rather than by an Update of its own, because a
        /// MonoBehaviour has to live in a file named after itself and this is a widget, not a system
        /// — the same argument PopupStack.EnsureBeat makes for not building a hidden driver object.
        /// Each HUD already has a per-frame method and already knows when it is alive.
        ///
        /// EVERYTHING here is on the UNSCALED clock. The one moment the plate is most likely to be
        /// mid-animation is the moment it opens the pause board, and the board stops the scaled clock
        /// dead — a plate easing on Time.deltaTime would freeze halfway through its own press.
        /// </summary>
        public void Tick()
        {
            if (_rect == null || _group == null) return;

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            bool wanted = Wanted;
            _shown = Mathf.MoveTowards(_shown, wanted ? 1f : 0f, dt / 0.18f);
            _group.alpha = _shown;

            if (!wanted)
            {
                // Springs let go with the plate, so it does not come back wearing the hover it had
                // when the curtain took it away.
                _hover = Mathf.MoveTowards(_hover, 0f, dt * 6f);
                _press = Mathf.MoveTowards(_press, 0f, dt * 6f);
                _pointerSeen = false;
                Apply();
                return;
            }

            bool over = PollPointer(out bool clicked);

            _hover = Mathf.MoveTowards(_hover, over ? 1f : 0f, dt * 8f);
            _press = Mathf.MoveTowards(_press, 0f, dt * 4f);
            Apply();

            if (over && clicked)
            {
                _press = 1f;
                Open();
            }
        }

        /// <summary>
        /// Whether a pause plate can honestly be offered on this frame.
        ///
        /// The rule is one sentence and it is the same rule the whole project applies to a prompt:
        /// NEVER OFFER A BEAT THAT WILL NOT HAPPEN. A plate the player can press that opens nothing
        /// is worse than no plate, because the only conclusion available to them is that the game is
        /// broken.
        ///
        ///   no factory        Escape opens nothing either — PopupStack.PauseFactory is the single
        ///                     wire between the key and the board, and null is a legal, silent state
        ///                     that means the pause board is not in this build.
        ///   a popup is up     the board is already open, or something is. The plate lives on a HUD
        ///                     canvas at sorting order 40..100 and the popups sit at 25000, so it is
        ///                     DRAWN under them anyway — but a hit test does not care what is drawn
        ///                     over it, and a click that went through a scrim onto this plate would
        ///                     push a second pause board behind the first.
        ///   a seam is running the frame is covered, or about to be. The player is between two
        ///                     places and there is nothing to pause.
        ///   the opening story GameDirector ignores Escape during it, deliberately — the sequence
        ///                     reads its own keys and has its own skip. A plate that disagreed with
        ///                     the key would be the game offering two answers.
        ///   a scripted clock  the capture harness owns time and steps the game by hand. A pause
        ///                     plate in every frame sheet the review process produces is a review of
        ///                     a game nobody plays.
        /// </summary>
        bool Wanted
        {
            get
            {
                if (PopupStack.PauseFactory == null) return false;
                if (PopupStack.Any) return false;
                if (SimClock.Scripted) return false;
                if (StageSeam.InProgress) return false;
                var curtain = Curtain.Live;
                if (curtain != null && curtain.Busy) return false;
                var director = GameDirector.Instance;
                if (director != null && director.State == GameState.Intro) return false;
                return true;
            }
        }

        /// <summary>
        /// Is the pointer on the plate, and did it press this frame.
        ///
        /// Reads <see cref="Mouse"/> rather than <see cref="Pointer"/>, which is what every other
        /// clickable surface in this game reads and is the device TouchPointerBridge writes a finger
        /// into. Reading Pointer.current here would work on a desktop and quietly prefer the real
        /// Touchscreen on a phone, at which point this plate would be the only screen in the project
        /// not going through the bridge — two paths to the same place, which the bridge's own note
        /// asks nobody to create.
        /// </summary>
        bool PollPointer(out bool clicked)
        {
            clicked = false;
            var mouse = Mouse.current;
            if (mouse == null || _rect == null) return false;

            Vector2 p = mouse.position.ReadValue();
            // A pointer that has not moved since the plate appeared is not a pointer that chose it.
            // Same rule MainMenu enforces for its own plates, for the same reason: a mouse abandoned
            // over this corner would otherwise sit here lit for the whole match.
            bool moved = !_pointerSeen || (p - _lastPointer).sqrMagnitude > 4f;
            _lastPointer = p;
            _pointerSeen = true;

            bool over = RectTransformUtility.RectangleContainsScreenPoint(_rect, p, _hitCamera);
            clicked = over && mouse.leftButton.wasPressedThisFrame;
            // A click is honoured wherever the pointer is; only the HIGHLIGHT waits for movement.
            // A tap on a phone arrives as a press at a position the mouse has never been at before,
            // so gating the click on movement too would make the plate untappable on the one
            // platform that has no other way to pause.
            return over && (moved || clicked || _hover > 0.01f);
        }

        void Apply()
        {
            if (_rect == null) return;
            float scale = 1f + 0.08f * _hover - 0.10f * _press;
            _rect.localScale = new Vector3(scale, scale, 1f);
            // Nothing pinned to a board in this game is straight, and the plate straightens as the
            // pointer arrives — the same gesture the front page's and the pause board's plates make.
            _rect.localRotation = Quaternion.Euler(0f, 0f, -2.2f * (1f - 0.85f * _hover));
            if (_plate != null) _plate.color = Color.Lerp(Plate, PlateHot, _hover);
        }

        /// <summary>
        /// Open the board, through the same factory Escape goes through.
        ///
        /// Deliberately NOT a call into PausePopup. GameDirector does not know that type either — it
        /// asks the stack, and the stack asks the factory the popup registered on startup. A button
        /// that named the type would be a second wire into the same board, free to keep working after
        /// the first one broke, which is how a project ends up with two pause menus.
        /// </summary>
        void Open()
        {
            var factory = PopupStack.PauseFactory;
            if (factory == null) return;
            var popup = factory();
            if (popup != null) PopupStack.Push(popup);
        }
    }
}
