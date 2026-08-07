using System;
using System.Collections;
using DuckMow.Flow;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DuckMow
{
    /// <summary>
    /// The front page: a slow move across the championship ground, four choices, and the hand-off
    /// into the game.
    ///
    /// TWO of those four start a game, and the split is the point. PLAY runs the championship —
    /// rounds chained together, the stages arriving when the chain says they do. THE CLASSES opens
    /// a board of the individual classes and hands one straight to <see cref="StageLauncher"/>,
    /// with no chain, no round number and no verdict waiting at the end. The second door exists
    /// because without it two thirds of this game can only be reached by playing through the third
    /// that is not being asked for.
    ///
    /// The menu is a camera in the real set rather than a screen of text over a colour. Menu.unity
    /// builds the same lawn, lighting, post profile, mower and judges' bench the game does, so this
    /// component only has to move the camera through it and draw the choices on top — there is no
    /// separate "menu background" that can drift out of step with how the game actually looks.
    ///
    /// Input is polled straight off the devices, exactly as InputReader does, so the scene needs no
    /// EventSystem, no input module and no Button components. Those are three more things that can
    /// arrive broken in a WebGL build, and the hit test they would be doing for us is the four lines
    /// in <see cref="PointerOver"/>.
    ///
    /// ---- what this file is doing beyond that ----
    ///
    /// Everything below the input section exists because a menu that is merely correct reads as dead.
    /// Nothing on this page snaps: the plates ride under-damped springs so a selection has weight, a
    /// confirm kicks the camera and pushes it toward the mower, the title's letters are arched and
    /// fanned per glyph and drop into place on load, and the near bunting stirs. All of it is driven
    /// from one clock and costs a few hundred vector operations a frame, because the alternative —
    /// an animation system, curves, a tweening library — is three more things to ship to a browser.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class MainMenu : MonoBehaviour
    {
        /// <summary>
        /// What the four plates on the front page do.
        ///
        /// APPENDED rather than slotted in next to Play, even though THE CLASSES sits second on the
        /// board. This enum is serialized into Menu.unity as an integer per plate, so inserting a
        /// value in the middle silently renames every plate below it — the saved CONTROLS plate
        /// would come back as Stages and the credits card would open the class list. The builder
        /// rewrites the scene anyway, but a value that only survives if somebody remembers to
        /// rebuild is a trap laid for whoever does not.
        /// </summary>
        public enum Choice { Play, Controls, Credits, Stages }

        [Serializable]
        public class Item
        {
            public Choice choice;
            public RectTransform rect;
            public Image plate;
            public TextMeshProUGUI label;
            [Tooltip("Degrees this plate sits off square when nothing is happening to it. Nothing " +
                     "pinned to a board in this game is straight, and three plates on an exact " +
                     "vertical with an exact gap is the one arrangement that reads as a web form.")]
            public float restTilt;
            [Tooltip("Pixels this plate sits off the column's left edge at rest, for the same reason.")]
            public float restShift;
            [Tooltip("The painted shadow under the plate. Offset grows as the plate is selected, so " +
                     "the selected plate reads as lifted off the board rather than merely bigger.")]
            public RectTransform shadow;
        }

        /// <summary>
        /// One line of the show schedule: a class the player can enter on its own.
        ///
        /// It carries a <see cref="StageId"/> and NOT a scene name, a title or a description. Those
        /// three all belong to <see cref="StageLauncher"/>, which is the only thing in the project
        /// that knows what a stage is called and which file it lives in; a copy of any of them here
        /// would be a second answer free to disagree with the first, and the one that disagreed
        /// would be the one on the front page.
        ///
        /// The text fields are therefore WRITTEN, not authored — see <see cref="LabelStages"/>. The
        /// builder seeds them so the saved scene can be looked at in the editor, and the menu
        /// overwrites them on load, which is the same arrangement the framing has for the same
        /// reason.
        /// </summary>
        [Serializable]
        public class StageChoice
        {
            public StageId stage;
            public RectTransform rect;
            public Image plate;
            [Tooltip("Written from StageLauncher.TitleFor. Anything typed here is overwritten.")]
            public TextMeshProUGUI title;
            [Tooltip("Written from StageLauncher.KickerFor. Anything typed here is overwritten.")]
            public TextMeshProUGUI kicker;
            [Tooltip("Degrees this row sits off square at rest. Same rule as the front page's " +
                     "plates: three rows on an exact vertical is a settings dialogue, and this " +
                     "board is meant to read as a class list pinned up at a county show.")]
            public float restTilt;
            public RectTransform shadow;
        }

        /// <summary>
        /// One complete framing of the set: where the camera stands, where the mower is parked, what
        /// is mown into the lawn and where the near dressing sits.
        ///
        /// It is one object rather than a page of loose fields because these numbers are only correct
        /// together. Raising the camera without moving the mower drops the machine out of the bottom
        /// of frame; moving the mower without moving the picture leaves the trail it drove in on
        /// starting nowhere. Every framing this menu has ever been shot on is in the array, so a
        /// change can be compared against what it replaced instead of argued about.
        /// </summary>
        [Serializable]
        public class Vista
        {
            public string name = "vista";

            [Header("Camera")]
            public Vector3 pivot = new Vector3(-2f, 0.9f, -21f);
            public float radius = 17f;
            [Tooltip("Degrees, measured from +Z. The camera sits on this bearing from the pivot.")]
            public float yaw = 30f;
            [Tooltip("How far either side of that bearing the move travels.\n\n" +
                     "Small, and smaller the closer the mower is parked. The camera re-aims at a " +
                     "fixed look-at point every frame, so a near object slides across the frame by " +
                     "the difference between its own parallax and the view's rotation — at two " +
                     "degrees the mower moves about eight percent of the frame width and the " +
                     "bunting four metres from the lens moves seventeen. That distribution is the " +
                     "point: the nearest layer carries the movement.")]
            public float yawSwing = 2f;
            public float height = 4.6f;
            public float heightSwing = 0.3f;
            [Tooltip("Seconds for one there-and-back.")]
            public float cycle = 30f;
            public Vector3 lookAt = new Vector3(-2.25f, 0f, -21.4f);
            public float fov = 46f;

            [Header("Mower")]
            public Vector3 mowerPos = new Vector3(2.24f, 0.45f, -10.26f);
            public float mowerYaw = 97f;

            [Header("Picture")]
            public ShapeId shape = ShapeId.Heart;
            public Vector2 pictureCentre = new Vector2(-2f, -21f);
            public float pictureRadius = 11f;
            public float pictureYaw = 180f;

            [Header("Near dressing")]
            [Tooltip("Metres in front of the mean camera position the bunting is strung.")]
            public float buntingForward = 4.6f;
            [Tooltip("Degrees the run is turned off square to the view. Oblique on purpose: a run " +
                     "at right angles to the lens is a horizontal line of identical pennants, and " +
                     "an oblique one has a near end and a far end, which is depth for free.")]
            public float buntingYaw = 26f;
            [Tooltip("World height of the cord where it leaves its posts.")]
            public float buntingHeight = 5.4f;

            [Tooltip("Metres in front of the mean camera the groundskeeper's kit stands, and how " +
                     "far to the camera's right of the axis.")]
            public float propsForward = 7.6f;
            public float propsLateral = -1.4f;
            public float propsYaw = -18f;
        }

        [Header("Framings")]
        [Tooltip("Every framing the menu has been shot on. Index 0 is the one it opens with.")]
        public Vista[] vistas = Array.Empty<Vista>();
        public int vista;

        [Header("The set")]
        [Tooltip("The camera this component flies. Its pose is written every frame, so nothing else " +
                 "may drive it.")]
        public Transform cameraTransform;
        [Tooltip("The parked mower. Moved by the framing, because where it stands is part of the " +
                 "composition rather than a property of the prefab.")]
        public Transform mower;
        [Tooltip("Mows and chalks the picture. Re-run when the framing moves the picture.")]
        public MenuLawnArt lawnArt;
        public Transform buntingRoot;
        public Transform propsRoot;

        // The orbit fields below are written by ApplyFraming from the chosen Vista. They are kept as
        // plain fields rather than read straight off the vista so ApplyVista stays a pure function of
        // the component's state — which is what lets the builder pose the saved scene without
        // running anything.
        [Header("Vista camera (written by ApplyFraming)")]
        public Vector3 orbitPivot = new Vector3(-2f, 0.9f, -21f);
        public float orbitRadius = 17f;
        public float orbitYaw = 30f;
        public float orbitYawSwing = 2f;
        public float orbitHeight = 4.6f;
        public float orbitHeightSwing = 0.3f;
        public float cycleSeconds = 30f;
        public Vector3 lookAt = new Vector3(-2.25f, 0f, -21.4f);

        [Header("Choices")]
        public Item[] items = Array.Empty<Item>();
        public Sprite plateIdle;
        public Sprite platePressed;
        public Color labelIdle = new Color(0.16f, 0.12f, 0.09f);
        public Color labelSelected = new Color(0.72f, 0.20f, 0.17f);
        public float selectedScale = 1.07f;
        [Tooltip("Pixels the selected plate slides toward the middle of the screen.")]
        public float selectedShift = 16f;
        [Tooltip("Degrees the selected plate straightens up out of its resting tilt.")]
        public float selectedStraighten = 0.75f;
        [Tooltip("Pixels the plate sinks when it is chosen. The button artwork has a red lip along " +
                 "its bottom edge that the pressed sprite collapses, so the plate has to travel " +
                 "down by roughly that lip or the sprite swap reads as a colour change.")]
        public float pressDepth = 7f;
        [Tooltip("Pixels the shadow sits down and right of a resting plate.")]
        public float shadowOffset = 3f;
        [Tooltip("Extra pixels the shadow drops away from the selected plate.")]
        public float selectedLift = 5f;
        [Tooltip("The chevron that points at whatever is selected.")]
        public RectTransform pointer;
        [Tooltip("Pixels the pointer sits to the left of the plate column.")]
        public float pointerGap = 30f;

        [Header("Springs")]
        [Tooltip("Stiffness of the plate springs. Higher is snappier.")]
        public float springStiffness = 150f;
        [Tooltip("Damping ratio. Under one on purpose — the overshoot IS the weight.")]
        public float springDamping = 0.52f;

        [Header("Title")]
        [Tooltip("The masthead, the rosette and the class ribbon, in the order they should land.\n\n" +
                 "The lettering itself is not text: it is Art/Blender/render_title.py's render of " +
                 "Rockwell Bold, arched and keylined as real geometry, because the project ships no " +
                 "display face and every attempt to make a title out of Liberation Sans was a " +
                 "heading with decoration around it. So there is nothing here to animate per glyph " +
                 "— what drops in is three painted pieces being pinned to a board.")]
        public RectTransform[] titlePieces = Array.Empty<RectTransform>();
        [Tooltip("Pixels each piece falls from as the page opens.")]
        public float titleDrop = 190f;
        [Tooltip("Degrees each piece is over-rotated at the top of its fall, so it swings level as " +
                 "it lands rather than sliding down flat.")]
        public float titleSwing = 7f;
        public float titlePieceDelay = 0.11f;
        public float titlePieceFall = 0.46f;

        [Header("Bunting")]
        public Transform[] pennants = Array.Empty<Transform>();
        [Tooltip("Degrees of swing either side of the lean.")]
        public float pennantSway = 8f;
        [Tooltip("Degrees every pennant leans downwind. Cloth on a windy day all leans the same " +
                 "way; the venue's own flags share one wind direction for exactly this reason.")]
        public float pennantLean = 7f;
        public float pennantSpeed = 1.4f;

        [Header("Cards")]
        public CanvasGroup controlsCard;
        public CanvasGroup creditsCard;
        public float cardFade = 0.16f;

        [Header("The classes")]
        [Tooltip("The class list. It is a CARD, on the same mechanism as CONTROLS and CREDITS, " +
                 "rather than a screen of its own — see the Card enum.")]
        public CanvasGroup stagesCard;
        [Tooltip("One row per class, in the order they are pinned up. The order here is the " +
                 "order the arrow keys walk; it does not have to match the enum.")]
        public StageChoice[] stageChoices = Array.Empty<StageChoice>();
        [Tooltip("The chevron that points at the class under the marker. The front page's pointer " +
                 "stays where it is — this board has its own, because both can be on screen at " +
                 "once while the card is fading in.")]
        public RectTransform stagePointer;
        [Tooltip("Pixels the class pointer sits to the left of the rows.")]
        public float stagePointerGap = 22f;

        [Header("Transition")]
        public Image fade;
        [Tooltip("Seconds to come up from black when the menu loads.")]
        public float fadeIn = 0.85f;
        [Tooltip("Seconds to go to black before the game scene is asked for.")]
        public float fadeOut = 0.55f;
        [Tooltip("Metres the camera pushes toward the set as PLAY leaves. The round is about to " +
                 "start; the last thing the menu does should be to move in on the machine rather " +
                 "than hold still while the picture dims.")]
        public float leaveDolly = 3.2f;
        [Tooltip("Degrees the lens narrows over the same move.")]
        public float leaveZoom = 5f;
        [Tooltip("Scene loaded by PLAY. It must be in the build settings or the load silently " +
                 "does nothing; DuckMenuBuilder.RegisterBuildScenes is what guarantees that.")]
        public string playScene = "Main";

        [Header("Audio")]
        public AudioSource music;
        public AudioSource sfx;
        public AudioClip hoverClip;
        public AudioClip clickClip;
        public AudioClip confirmClip;
        public AudioClip backClip;

        [Header("Framing review")]
        [Tooltip("Reports which framing is on screen. Only ever visible in the editor — see " +
                 "DebugTools.")]
        public TMP_Text framingLabel;

        // The framing switch exists so a composition can be judged from a screenshot rather than
        // from arithmetic, which is how every number in the Vista table was wrong the first time.
        // It compiles out of a player build entirely: a menu that responds to F1 in a browser is a
        // menu that will eventually be found responding to F1 in a browser.
#if UNITY_EDITOR
        const bool DebugTools = true;
#else
        const bool DebugTools = false;
#endif

        /// <summary>
        /// The overlays this page can put over itself.
        ///
        /// STAGES IS ONE OF THESE ON PURPOSE, and it was the whole design decision behind the class
        /// list. A stage picker is exactly what this mechanism already is — a plate is pressed, a
        /// board fades up over the set, Escape takes it away again — and building it as a second
        /// screen system would have meant a second fade, a second Escape path, a second thing to
        /// remember to hide when the menu leaves, and two overlays free to be up at once. The only
        /// thing it needs that CONTROLS and CREDITS do not is that it must not be dismissed by ANY
        /// key, because it has choices on it; that is one branch in HandleInput, not a subsystem.
        /// </summary>
        enum Card { None, Controls, Credits, Stages }

        Camera _uiCamera;
        Camera _camera;
        float _clock;
        int _index;
        int _pressed = -1;
        Card _card = Card.None;
        Card _shown = Card.None;
        float _cardAmount;
        float _fadeAmount;
        bool _leaving;
        bool _loadStarted;
        float _leaveAmount;
        Vector2 _lastPointer;
        bool _pointerSeen;

        // Plate springs. One position and one velocity per item per channel; the selection channel
        // rides toward 0 or 1 and the press channel is kicked to 1 and left to fall back.
        float[] _sel, _selVel, _press, _pressVel;

        // The same two channels again for the class list. Separate arrays rather than a shared set
        // indexed past the end of the plates, because the two columns are selected independently:
        // the front page keeps PLAY lit underneath while the card is up, and it has to, or closing
        // the card would drop the player onto a page with nothing selected.
        int _stageIndex;
        int _stagePressed = -1;
        float[] _stageSel, _stageSelVel, _stagePress, _stagePressVel;

        // Camera kick, in degrees and metres, and its velocities. Post-multiplied onto the vista
        // pose rather than folded into it, so the drift stays exactly what the framing says it is.
        Vector3 _kick, _kickVel;

        Vector2[] _pieceMin, _pieceMax;
        Quaternion[] _pieceRot;
        float _titleClock;
        bool _titleLanded;

        Quaternion[] _pennantRest;

        void Awake()
        {
            var canvas = GetComponentInParent<Canvas>();
            _uiCamera = canvas != null ? canvas.worldCamera : null;
            EnsureSprings();

            if (pennants.Length > 0)
            {
                _pennantRest = new Quaternion[pennants.Length];
                for (int i = 0; i < pennants.Length; i++)
                    _pennantRest[i] = pennants[i] != null ? pennants[i].localRotation : Quaternion.identity;
            }

            if (framingLabel != null) framingLabel.gameObject.SetActive(DebugTools);

            // Black on the first frame, not black in the saved scene. The builder leaves the fade
            // image transparent so the menu can be looked at — and screenshotted — in the editor
            // without entering play mode; the fade-up only exists once something is running.
            _fadeAmount = 1f;
            ApplyFade();
            // The class list is titled here rather than in Start, because the card can be opened on
            // the very first frame a player is allowed to press anything and a board that spends
            // that frame showing whatever the builder happened to bake is a board that flickers.
            LabelStages();
            ApplyCard();
            ApplyHighlight();
            ApplyStageHighlight();
        }

        void Start()
        {
            // The framing is re-applied on the first frame rather than trusted from the saved scene,
            // because the saved scene cannot contain the mown picture: the cut mask is a render
            // texture built at runtime, so the lawn art has to be laid every time the scene loads.
            ApplyFraming(vista);
            ApplyVista(0f);
            CacheTitlePieces();
            if (music != null && music.clip != null && !music.isPlaying) music.Play();

            // Arriving back from a round, the curtain that covered the load is still down over this
            // scene and raising it is now this scene's job — whatever closed it was destroyed by the
            // load. It is raised INSTEAD of the black fade-up rather than as well as it: two things
            // clearing the frame at once is how a transition ends up with a grey step in the middle
            // of it. A cold start has no curtain and keeps the fade, which is the app opening rather
            // than a stage boundary and is allowed to look like one.
            if (MatchState.TakeCurtainPending())
            {
                _fadeAmount = 0f;
                ApplyFade();
                StartCoroutine(StageSeam.End(0.1f));
            }
        }

        void Update()
        {
            // Clamped, and unscaled. A load spike or a browser tab regaining focus hands over a
            // delta of a quarter second or more, which an under-damped spring integrated explicitly
            // turns into a plate flying off the screen.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _clock += dt;
            _titleClock += dt;

            ApplyVista(_clock);
            TickCamera(dt);
            TickPlates(dt);
            AnimateTitle();
            StirBunting();

            // The alpha belongs to whichever card is on screen, which is not the same thing as the
            // card that has been asked for: a card that closes has to fade out of view before the
            // request can move on, or closing is a cut while opening is a fade.
            float cardTarget = _shown != Card.None && _shown == _card ? 1f : 0f;
            _cardAmount = Mathf.MoveTowards(_cardAmount, cardTarget, dt / Mathf.Max(cardFade, 0.01f));
            if (_cardAmount <= 0f) _shown = _card;
            ApplyCard();

            // Only while the class list is on screen or on its way off it. The rows are three rects
            // on a card that spends nearly all of the menu's life switched off, and springing them
            // against a target they already sit on for the whole of a thirty-second camera cycle is
            // work done for a board nobody is looking at.
            if (_shown == Card.Stages || _card == Card.Stages) TickStagePlates(dt);

            if (_leaving)
            {
                // The camera keeps pushing in and the tune keeps ebbing — that is the OUTRO, and it
                // runs underneath the curtain closing rather than instead of it.
                //
                // What used to be here as well was a fade to black, and it was the single worst
                // moment in the game: the front page went dark, stayed dark for as long as the
                // browser needed, and then a lawn appeared. The load is now hidden behind grass
                // growing up over the frame instead — see LeaveToGame — so the player never sees the
                // game stop. The fade image is left alone, at zero, for the cold start to use.
                _leaveAmount = Mathf.MoveTowards(_leaveAmount, 1f, dt / Mathf.Max(fadeOut, 0.01f));
                if (music != null) music.volume = Mathf.MoveTowards(music.volume, 0f, dt / Mathf.Max(fadeOut, 0.01f));

                if (!_loadStarted)
                {
                    _loadStarted = true;
                    StartCoroutine(LeaveToGame());
                }
                return;
            }

            if (_fadeAmount > 0f)
            {
                _fadeAmount = Mathf.MoveTowards(_fadeAmount, 0f, dt / Mathf.Max(fadeIn, 0.01f));
                ApplyFade();
            }

            HandleInput();
        }

        // ------------------------------------------------------------------ framing

        Camera Cam
        {
            get
            {
                if (_camera == null && cameraTransform != null) _camera = cameraTransform.GetComponent<Camera>();
                return _camera;
            }
        }

        /// <summary>
        /// Adopt one of the framings whole: camera arc, lens, mower pose, picture and near dressing.
        ///
        /// Called by the builder so the saved scene is already in the pose the player's first frame
        /// is in — without it, every editor screenshot of this menu is of a frame nobody ever sees.
        /// The picture is the one part that cannot be saved, so it is only laid when something is
        /// actually running.
        /// </summary>
        public void ApplyFraming(int index)
        {
            if (vistas == null || vistas.Length == 0) return;
            vista = ((index % vistas.Length) + vistas.Length) % vistas.Length;
            var v = vistas[vista];
            if (v == null) return;

            orbitPivot = v.pivot;
            orbitRadius = v.radius;
            orbitYaw = v.yaw;
            orbitYawSwing = v.yawSwing;
            orbitHeight = v.height;
            orbitHeightSwing = v.heightSwing;
            cycleSeconds = v.cycle;
            lookAt = v.lookAt;
            if (Cam != null) Cam.fieldOfView = v.fov;

            if (mower != null)
                mower.SetPositionAndRotation(v.mowerPos, Quaternion.Euler(0f, v.mowerYaw, 0f));

            PlaceNearDressing(v);

            if (lawnArt != null && Application.isPlaying)
                lawnArt.Apply(v.shape, v.pictureCentre, v.pictureRadius, v.pictureYaw,
                              new Vector2(v.mowerPos.x, v.mowerPos.z));

            ApplyVista(_clock);
            UpdateFramingLabel();
        }

        /// <summary>
        /// Stand the near dressing in front of wherever this framing's camera ends up.
        ///
        /// It is placed from the framing rather than parented to the camera because it has to be
        /// STILL: parallax against a moving lens is the entire reason the near layer exists, and
        /// dressing that travels with the camera has none.
        /// </summary>
        void PlaceNearDressing(Vista v)
        {
            float r = v.yaw * Mathf.Deg2Rad;
            // Mean pose over the cycle. The swing rides a sine so its average is the base bearing;
            // the rise rides a raised cosine so its average is half the swing.
            var cam = new Vector3(v.pivot.x + Mathf.Sin(r) * v.radius,
                                  v.height + v.heightSwing * 0.5f,
                                  v.pivot.z + Mathf.Cos(r) * v.radius);

            Vector3 fwd = v.lookAt - cam;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) return;
            fwd.Normalize();
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            float bearing = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;

            if (buntingRoot != null)
                buntingRoot.SetPositionAndRotation(
                    new Vector3(cam.x + fwd.x * v.buntingForward, v.buntingHeight,
                                cam.z + fwd.z * v.buntingForward),
                    Quaternion.Euler(0f, bearing + v.buntingYaw, 0f));

            if (propsRoot != null)
                propsRoot.SetPositionAndRotation(
                    new Vector3(cam.x + fwd.x * v.propsForward + right.x * v.propsLateral, 0f,
                                cam.z + fwd.z * v.propsForward + right.z * v.propsLateral),
                    Quaternion.Euler(0f, bearing + v.propsYaw, 0f));
        }

        /// <summary>
        /// Where the camera is at a given point in the cycle. Public so the builder can put the
        /// saved scene on the same pose the game will open on, rather than wherever the camera
        /// happened to be left.
        /// </summary>
        public void ApplyVista(float seconds)
        {
            if (cameraTransform == null) return;

            float phase = cycleSeconds > 0.01f ? seconds / cycleSeconds * Mathf.PI * 2f : 0f;
            float yaw = orbitYaw + Mathf.Sin(phase) * orbitYawSwing;
            // The rise rides a cosine while the swing rides a sine, so the two never turn round on
            // the same frame. Sharing a phase makes the whole move read as a pendulum, and a
            // pendulum reads as a machine rather than as somebody holding a camera.
            float height = orbitHeight + (0.5f - 0.5f * Mathf.Cos(phase)) * orbitHeightSwing;

            float r = yaw * Mathf.Deg2Rad;
            var pos = new Vector3(orbitPivot.x + Mathf.Sin(r) * orbitRadius,
                                  height,
                                  orbitPivot.z + Mathf.Cos(r) * orbitRadius);

            Vector3 dir = lookAt - pos;
            if (dir.sqrMagnitude < 1e-6f) return;
            cameraTransform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir, Vector3.up));
        }

        /// <summary>The kick from the last confirm, and the push-in as PLAY leaves.</summary>
        void TickCamera(float dt)
        {
            if (cameraTransform == null) return;

            Spring(ref _kick.x, ref _kickVel.x, 0f, dt, 60f, 0.42f);
            Spring(ref _kick.y, ref _kickVel.y, 0f, dt, 60f, 0.42f);
            Spring(ref _kick.z, ref _kickVel.z, 0f, dt, 60f, 0.42f);

            if (_kick.sqrMagnitude > 1e-6f || _leaveAmount > 0f)
            {
                var rot = cameraTransform.rotation * Quaternion.Euler(_kick.x, _kick.y, _kick.z);
                // Eased, not linear. A push-in that starts at full speed reads as a cut.
                float ease = _leaveAmount * _leaveAmount * (3f - 2f * _leaveAmount);
                var pos = cameraTransform.position + cameraTransform.forward * (leaveDolly * ease);
                cameraTransform.SetPositionAndRotation(pos, rot);
                if (Cam != null && vistas != null && vista < vistas.Length && vistas[vista] != null)
                    Cam.fieldOfView = vistas[vista].fov - leaveZoom * ease;
            }
        }

        /// <summary>
        /// Out of the front page and into the championship, with the load underneath the grass.
        ///
        /// The load is asked for only once the frame is genuinely opaque, which is the same rule the
        /// old fade-to-black followed and for the same reason: a scene this size locks a browser tab
        /// hard enough that a player who can still see the menu decides their click did not register
        /// and clicks again. The difference is what they are looking at while it happens.
        ///
        /// The curtain is left DOWN across the load. It is DontDestroyOnLoad, so it is alive on both
        /// sides of the swap, and GameDirector.Start raises it onto the venue — which is the whole
        /// mechanism by which a full scene change has no visible seam at all.
        /// </summary>
        /// <summary>
        /// Editor capture hook: take the Play choice without a keypress.
        ///
        /// The front page reads the keyboard directly rather than through InputReader — it is a
        /// different scene with no round in it — so an unattended review run has no way to get past
        /// it, and the boundary out of the menu is the one the player meets FIRST and the one most
        /// likely to be slow. A journey sheet that has to start at the second scene is not a journey
        /// sheet. Idempotent, so a driver polling it every frame cannot queue two loads.
        /// </summary>
        public void DebugPlayNow()
        {
            if (_leaving) return;
            _leaving = true;
        }

        IEnumerator LeaveToGame()
        {
            // A fresh run. The escalation starts from quiet, whatever the last session ended on.
            MatchState.BeginSession();

            yield return StageSeam.Begin(MatchState.Seam.MenuToRound, "THE CHAMPIONSHIP",
                                         $"ROUND 1 OF {Mathf.Max(MatchState.RoundsTotal, 1)}");

            MatchState.CurtainPending = true;
            SceneManager.LoadSceneAsync(playScene);
        }

        // ------------------------------------------------------------------ input

        void HandleInput()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;

            if (DebugTools) FramingKeys(kb);

            int move = 0;
            bool confirm = false, back = false;

            if (kb != null)
            {
                if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) move++;
                if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) move--;
                confirm = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame ||
                          kb.spaceKey.wasPressedThisFrame;
                back = kb.escapeKey.wasPressedThisFrame;
            }
            if (pad != null)
            {
                if (pad.dpad.down.wasPressedThisFrame) move++;
                if (pad.dpad.up.wasPressedThisFrame) move--;
                confirm |= pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame;
                back |= pad.buttonEast.wasPressedThisFrame;
            }

            bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;

            if (_card == Card.Stages)
            {
                // The one card with choices on it, so the "anything closes it" rule below cannot
                // apply — an arrow key here means "the next class", not "take this away".
                HandleStages(move, confirm, back, clicked, mouse);
                return;
            }

            if (_card != Card.None)
            {
                // Anything at all closes it. A card of key bindings is not a dialogue box and must
                // never be somewhere the player has to work out how to leave.
                if (back || confirm || clicked || move != 0) CloseCard();
                return;
            }

            if (move != 0 && items.Length > 0)
            {
                Select(((_index + move) % items.Length + items.Length) % items.Length);
                _pointerSeen = false;
            }

            int over = -1;
            if (mouse != null)
            {
                Vector2 p = mouse.position.ReadValue();
                // The pointer only takes the highlight while it is moving. Resting on a plate used
                // to re-select it every frame, which meant the arrow keys could not move off
                // whatever the mouse happened to be left sitting on.
                bool moved = !_pointerSeen || (p - _lastPointer).sqrMagnitude > 4f;
                _lastPointer = p;
                _pointerSeen = true;

                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i]?.rect == null || !PointerOver(items[i].rect, p)) continue;
                    over = i;
                    if (moved) Select(i);
                    break;
                }
            }

            if (over >= 0 && clicked) { Activate(over); return; }
            if (confirm && _index >= 0 && _index < items.Length) Activate(_index);
        }

        bool PointerOver(RectTransform rect, Vector2 screen)
            => RectTransformUtility.RectangleContainsScreenPoint(rect, screen, _uiCamera);

        void Select(int index)
        {
            if (index == _index) return;
            _index = index;
            Play(hoverClip, 0.19f);
        }

        void Activate(int index)
        {
            if (index < 0 || index >= items.Length || items[index] == null) return;

            _index = index;
            _pressed = index;
            EnsureSprings();
            // Kicked, not set. The plate is thrown past its pressed position and the spring brings
            // it back, which is the difference between a button that responds and one that changes
            // state.
            _press[index] = 1f;
            _pressVel[index] = 9f;

            switch (items[index].choice)
            {
                case Choice.Play:
                    Play(confirmClip, 0.31f);
                    KickCamera(-0.9f, 0.55f, 0.35f);
                    // Nothing else to do: the game runs its own opening from GameDirector.Start, so
                    // the menu's whole job is to get out of the way and load the scene. Deciding
                    // here whether the story plays would be a second copy of that flow.
                    _leaving = true;
                    break;

                case Choice.Stages:
                    Play(clickClip, 0.39f);
                    KickCamera(-0.4f, 0.22f, 0.14f);
                    // Re-titled on the way in as well as on load. StageLauncher owns those strings
                    // and this is the only moment at which a change to them could reach the board
                    // before the player reads it.
                    LabelStages();
                    // Snapped to whichever class was last looked at, rather than reset to the top.
                    // A player who comes here twice in a session is nearly always coming back for
                    // the same class, and a marker that slides down from the first row every time
                    // reads as the board having forgotten.
                    ApplyStageHighlight();
                    // _pointerSeen is deliberately LEFT ALONE. Clearing it would make the card's
                    // first frame count as a pointer move, and the mouse is at that instant resting
                    // on the plate that opened the card — under the rows, if a future layout ever
                    // puts the card over the column. The rule the rest of this page follows is that
                    // the pointer has to actually move before it takes a highlight, and the card is
                    // not a reason to suspend it.
                    _card = Card.Stages;
                    break;

                case Choice.Controls:
                    Play(clickClip, 0.39f);
                    KickCamera(-0.4f, 0.22f, 0.14f);
                    _card = Card.Controls;
                    break;

                case Choice.Credits:
                    Play(clickClip, 0.39f);
                    KickCamera(-0.4f, 0.22f, 0.14f);
                    _card = Card.Credits;
                    break;
            }
        }

        /// <summary>A shove on the lens, in degrees of pitch, yaw and roll.</summary>
        void KickCamera(float pitch, float yaw, float roll)
        {
            _kickVel += new Vector3(pitch, yaw, roll) * 26f;
        }

        void CloseCard()
        {
            _card = Card.None;
            Play(backClip, 0.25f);
        }

        void Play(AudioClip clip, float volume)
        {
            if (sfx == null || clip == null) return;
            sfx.PlayOneShot(clip, volume);
        }

        // ------------------------------------------------------------------ the classes
        //
        // THE CLASS LIST, and the one thing it is careful not to do.
        //
        // PLAY runs the championship: three rounds, chained, with the picture and the stages the
        // chain decides on and a verdict at the end. That chain is the game, and it is also the
        // reason two thirds of the game are hard to reach — the goose rally and the bloom rush only
        // come up when the round the player is on says so, so somebody wanting to look at the rally
        // has to play their way to it, every time, through a round they were not interested in.
        //
        // So this board is a SECOND, INDEPENDENT door. It knows three things: which classes exist,
        // what they are called, and how to ask for one. It does not know which scene a class lives
        // in, whether a championship is running, what round it is, or what happens when the class
        // ends — every one of those belongs to StageLauncher, which is the whole point of there
        // being a launcher rather than a second copy of the flow living on the front page. The
        // rule this file holds itself to is that the last thing any of these methods does is hand
        // over, and nothing here touches MatchState, StageSeam, the curtain, the scene manager or
        // the director. If a class ever needs to be set up differently, that is a change to the
        // launcher and this board never hears about it.

        /// <summary>
        /// Write the titles and the one-liners onto the rows, from the launcher.
        ///
        /// Public because the builder calls it: the saved scene is stored with the real class names
        /// on it so the card can be looked at — and screenshotted — in the editor without entering
        /// play mode. That is the same arrangement <see cref="ApplyFraming"/> has, and it has the
        /// same caveat: the SAVED strings are a convenience and are never trusted at runtime. This
        /// runs again in Awake and again as the card opens, so a rename in StageLauncher reaches the
        /// front page without anybody having to remember to rebuild the menu scene.
        /// </summary>
        public void LabelStages()
        {
            if (stageChoices == null) return;
            for (int i = 0; i < stageChoices.Length; i++)
            {
                var c = stageChoices[i];
                if (c == null) continue;
                if (c.title != null) c.title.text = StageLauncher.TitleFor(c.stage);
                if (c.kicker != null) c.kicker.text = StageLauncher.KickerFor(c.stage);
            }
        }

        /// <summary>
        /// The class list's own input: the same keyboard, the same mouse and the same hit test as
        /// the front page, because it is the same board and must feel like it.
        ///
        /// The one rule that is different is the way out. CONTROLS and CREDITS are dismissed by any
        /// key at all, which is right for a card that only has reading on it and wrong here — up,
        /// down and Enter all mean something now. So this card is left by Escape, by the pad's B
        /// button, or by a click that lands anywhere other than a class.
        ///
        /// THAT LAST ONE IS NOT A FLOURISH. This game ships to a browser, where a player may never
        /// touch the keyboard at all, and a card with three classes on it and no mouse-reachable way
        /// back would trap them on it exactly as surely as a pause menu with no exit does. The
        /// footer says so in words rather than leaving it to be discovered.
        /// </summary>
        void HandleStages(int move, bool confirm, bool back, bool clicked, Mouse mouse)
        {
            // The launcher has already been asked for a class and is running its transition. From
            // here the board is a picture: the scene it belongs to is about to go, and a second
            // press would either be a second launch or an Escape that closes the card out from
            // under a curtain that is already coming down.
            if (StageLauncher.Busy) return;

            if (back) { CloseCard(); return; }

            int n = stageChoices != null ? stageChoices.Length : 0;
            if (n == 0)
            {
                // An empty board is nothing but a trap. It cannot happen with a scene the builder
                // made, which is exactly why it is worth a line: it CAN happen to a scene somebody
                // is halfway through hand-editing, and the failure should be a card that shuts
                // rather than a page the player cannot leave.
                if (confirm || clicked) CloseCard();
                return;
            }

            if (move != 0)
            {
                SelectStage(((_stageIndex + move) % n + n) % n);
                _pointerSeen = false;
            }

            int over = -1;
            if (mouse != null)
            {
                Vector2 p = mouse.position.ReadValue();
                // The pointer only takes the highlight while it is moving — MainMenu's own rule,
                // and it matters more here than on the front page: the card opens under a mouse
                // that has just clicked, and a resting cursor that re-selected every frame would
                // pin the marker where the click happened to land.
                bool moved = !_pointerSeen || (p - _lastPointer).sqrMagnitude > 4f;
                _lastPointer = p;
                _pointerSeen = true;

                for (int i = 0; i < n; i++)
                {
                    if (stageChoices[i]?.rect == null || !PointerOver(stageChoices[i].rect, p)) continue;
                    over = i;
                    if (moved) SelectStage(i);
                    break;
                }
            }

            if (clicked)
            {
                if (over >= 0) { EnterStage(over); return; }
                CloseCard();
                return;
            }
            if (confirm) EnterStage(_stageIndex);
        }

        void SelectStage(int index)
        {
            if (index == _stageIndex) return;
            _stageIndex = index;
            Play(hoverClip, 0.19f);
        }

        /// <summary>
        /// Ask the launcher for a class, and then do nothing else ever again.
        ///
        /// The order of the last two lines is the whole method. Everything before the hand-over is
        /// this board's own business — the plate is kicked down, the horn note plays, the lens takes
        /// the same shove PLAY gives it — and everything after it belongs to somebody else. Launch
        /// may load a scene, and a scene load destroys this component; a line after it would be a
        /// line running on a destroyed object on some frames and not others, which is the worst
        /// possible kind of intermittent.
        ///
        /// <see cref="StageLauncher.Busy"/> is checked here as well as at the top of HandleStages,
        /// because the two answer different questions. There it stops a board that is already
        /// leaving from reading input at all; here it stops the one case that gets past it — a
        /// mouse click and an Enter landing on the same frame, which would otherwise be two calls
        /// to Launch and two scene loads racing each other.
        /// </summary>
        void EnterStage(int index)
        {
            if (StageLauncher.Busy) return;
            if (stageChoices == null || index < 0 || index >= stageChoices.Length) return;
            var choice = stageChoices[index];
            if (choice == null) return;

            _stageIndex = index;
            _stagePressed = index;
            EnsureStageSprings();
            _stagePress[index] = 1f;
            _stagePressVel[index] = 9f;

            Play(confirmClip, 0.31f);
            KickCamera(-0.9f, 0.55f, 0.35f);

            StageLauncher.Launch(choice.stage);
        }

        void EnsureStageSprings()
        {
            int n = stageChoices != null ? stageChoices.Length : 0;
            if (_stageSel != null && _stageSel.Length == n) return;
            _stageSel = new float[n];
            _stageSelVel = new float[n];
            _stagePress = new float[n];
            _stagePressVel = new float[n];
        }

        /// <summary>
        /// Settle the class rows on the current selection without animating into it.
        ///
        /// Public for the same reason <see cref="ApplyHighlight"/> is — the builder calls it so the
        /// saved card is the settled pose rather than a frame of an entrance — and called again
        /// every time the card opens, because a board that fades up with its marker still sliding
        /// from wherever it was left reads as two animations fighting.
        /// </summary>
        public void ApplyStageHighlight()
        {
            EnsureStageSprings();
            if (_stageIndex >= _stageSel.Length) _stageIndex = 0;
            for (int i = 0; i < _stageSel.Length; i++)
            {
                _stageSel[i] = i == _stageIndex ? 1f : 0f;
                _stageSelVel[i] = 0f;
                _stagePress[i] = 0f;
                _stagePressVel[i] = 0f;
            }
            _stagePressed = -1;
            ApplyStagePlates();
        }

        void TickStagePlates(float dt)
        {
            EnsureStageSprings();
            for (int i = 0; i < _stageSel.Length; i++)
            {
                Spring(ref _stageSel[i], ref _stageSelVel[i], i == _stageIndex ? 1f : 0f, dt,
                       springStiffness, springDamping);
                Spring(ref _stagePress[i], ref _stagePressVel[i], 0f, dt, springStiffness * 1.6f, 0.62f);
                if (_stagePress[i] < -0.35f) { _stagePress[i] = -0.35f; _stagePressVel[i] = 0f; }
            }
            if (_stagePressed >= 0 && _stagePressed < _stagePress.Length &&
                _stagePress[_stagePressed] < 0.25f) _stagePressed = -1;
            ApplyStagePlates();
        }

        /// <summary>
        /// The rows, driven off the springs.
        ///
        /// Deliberately gentler than the front page's plates, and the numbers say why. A class row
        /// is the better part of a thousand pixels wide against a plate's four hundred, so
        /// <see cref="selectedScale"/>'s seven percent would throw its ends sixty-odd pixels out
        /// past the edge of the card it is sitting on. The lift has to read as the same gesture at
        /// half the amount, which is what the shadow is doing most of the work for.
        /// </summary>
        void ApplyStagePlates()
        {
            const float rowScale = 1.028f;     // ~26 px of growth on a 950 px row
            const float rowShift = 11f;
            const float rowStraighten = 0.75f;
            const float rowPressDepth = 5f;

            if (stageChoices == null) return;

            for (int i = 0; i < stageChoices.Length; i++)
            {
                var c = stageChoices[i];
                if (c == null || c.rect == null) continue;

                float sel = _stageSel != null && i < _stageSel.Length ? _stageSel[i] : 0f;
                float press = _stagePress != null && i < _stagePress.Length ? _stagePress[i] : 0f;

                c.rect.localScale = Vector3.one * (1f + (rowScale - 1f) * sel - 0.03f * press);
                c.rect.localRotation = Quaternion.Euler(0f, 0f, c.restTilt * (1f - rowStraighten * sel));

                // Fractional anchors, so the nudge goes on the offsets — the same reason the front
                // page's plates do it this way.
                float x = rowShift * sel;
                float y = -rowPressDepth * press;
                c.rect.offsetMin = new Vector2(x, y);
                c.rect.offsetMax = new Vector2(x, y);

                if (c.title != null) c.title.color = Color.Lerp(labelIdle, labelSelected, sel);
                if (c.kicker != null)
                {
                    // The one-liner stays quieter than the title at every point in the transition,
                    // because it is a description and the title is the choice. Faded rather than
                    // greyed: a second ink on a cream plate is a second material, and the board
                    // already has two.
                    var idle = labelIdle; idle.a = 0.62f;
                    var hot = labelSelected; hot.a = 0.85f;
                    c.kicker.color = Color.Lerp(idle, hot, sel);
                }
                if (c.plate != null)
                {
                    var sprite = (i == _stagePressed && platePressed != null) ? platePressed : plateIdle;
                    if (sprite != null) c.plate.sprite = sprite;
                }
                if (c.shadow != null)
                {
                    float drop = shadowOffset + selectedLift * sel - (shadowOffset + selectedLift) * press;
                    c.shadow.offsetMin = new Vector2(drop, -drop);
                    c.shadow.offsetMax = new Vector2(drop, -drop);
                }
            }

            if (stagePointer != null && stageChoices.Length > 0)
            {
                float y = 0f, w = 0f;
                for (int i = 0; i < stageChoices.Length; i++)
                {
                    if (stageChoices[i]?.rect == null) continue;
                    float weight = Mathf.Max(_stageSel != null && i < _stageSel.Length ? _stageSel[i] : 0f, 0f);
                    y += stageChoices[i].rect.localPosition.y * weight;
                    w += weight;
                }
                if (w > 1e-3f) y /= w;
                stagePointer.anchoredPosition = new Vector2(-stagePointerGap, y);
                float pulse = 1f + Mathf.Sin(_clock * 4.4f) * 0.06f;
                stagePointer.localScale = new Vector3(pulse, pulse, 1f);
            }
        }

        // ------------------------------------------------------------------ plates

        void EnsureSprings()
        {
            int n = items != null ? items.Length : 0;
            if (_sel != null && _sel.Length == n) return;
            _sel = new float[n];
            _selVel = new float[n];
            _press = new float[n];
            _pressVel = new float[n];
        }

        /// <summary>
        /// One step of an explicit under-damped spring. Written out rather than pulled from a curve
        /// asset because a curve cannot be interrupted mid-flight — and every one of these is
        /// interrupted, constantly, by the next plate being selected before this one has settled.
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
            EnsureSprings();
            for (int i = 0; i < _sel.Length; i++)
            {
                Spring(ref _sel[i], ref _selVel[i], i == _index ? 1f : 0f, dt,
                       springStiffness, springDamping);
                Spring(ref _press[i], ref _pressVel[i], 0f, dt, springStiffness * 1.6f, 0.62f);
                // The press spring is kicked past 1 and comes back through 0, which is the pop back
                // up after the click. Floored so the pop is a pop rather than the plate leaping off
                // the board — the spring is under-damped and its first undershoot is large.
                if (_press[i] < -0.35f) { _press[i] = -0.35f; _pressVel[i] = 0f; }
            }
            // The pressed sprite is held only while the plate is actually down. Tying it to the
            // spring rather than to a timer means the artwork and the movement cannot disagree.
            if (_pressed >= 0 && _pressed < _press.Length && _press[_pressed] < 0.25f) _pressed = -1;
            ApplyPlates();
        }

        /// <summary>
        /// Public for the same reason ApplyVista is: the builder calls it so the saved scene already
        /// shows PLAY as the selected plate, rather than three identical buttons that only sort
        /// themselves out once something is running. It snaps the springs to rest, so the saved
        /// scene is the settled pose rather than a frame of an animation.
        /// </summary>
        public void ApplyHighlight()
        {
            EnsureSprings();
            for (int i = 0; i < _sel.Length; i++)
            {
                _sel[i] = i == _index ? 1f : 0f;
                _selVel[i] = 0f;
                _press[i] = 0f;
                _pressVel[i] = 0f;
            }
            _pressed = -1;
            ApplyPlates();
        }

        void ApplyPlates()
        {
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                if (item == null || item.rect == null) continue;

                float sel = _sel != null && i < _sel.Length ? _sel[i] : 0f;
                float press = _press != null && i < _press.Length ? _press[i] : 0f;

                item.rect.localScale = Vector3.one * (1f + (selectedScale - 1f) * sel - 0.055f * press);
                item.rect.localRotation = Quaternion.Euler(0f, 0f,
                    item.restTilt * (1f - selectedStraighten * sel));

                // Anchored purely by fraction, so the nudge has to go on the offsets rather than on
                // anchoredPosition, which a stretched rect ignores.
                float x = item.restShift + selectedShift * sel;
                float y = -pressDepth * press;
                item.rect.offsetMin = new Vector2(x, y);
                item.rect.offsetMax = new Vector2(x, y);

                if (item.label != null) item.label.color = Color.Lerp(labelIdle, labelSelected, sel);
                if (item.plate != null)
                {
                    var sprite = (i == _pressed && platePressed != null) ? platePressed : plateIdle;
                    if (sprite != null) item.plate.sprite = sprite;
                }
                if (item.shadow != null)
                {
                    // Down and to the right, matching the drop baked into the masthead render, and
                    // growing with selection: the gap between plate and shadow is what reads as the
                    // plate coming off the board. It closes up again when the plate is pressed,
                    // because a pressed plate is against the board.
                    float drop = shadowOffset + selectedLift * sel - (shadowOffset + selectedLift) * press;
                    item.shadow.offsetMin = new Vector2(drop, -drop);
                    item.shadow.offsetMax = new Vector2(drop, -drop);
                }
            }

            if (pointer != null && items.Length > 0)
            {
                // Follows the springs rather than the index, so it slides between plates instead of
                // teleporting, and overshoots with them.
                float y = 0f, w = 0f;
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i]?.rect == null) continue;
                    float weight = Mathf.Max(_sel != null && i < _sel.Length ? _sel[i] : 0f, 0f);
                    y += items[i].rect.localPosition.y * weight;
                    w += weight;
                }
                if (w > 1e-3f) y /= w;
                pointer.anchoredPosition = new Vector2(-pointerGap, y);
                // Breathes, so it is never a static arrow sitting on a moving page.
                float pulse = 1f + Mathf.Sin(_clock * 4.4f) * 0.06f;
                pointer.localScale = new Vector3(pulse, pulse, 1f);
            }
        }

        void ApplyCard()
        {
            SetCard(controlsCard, _shown == Card.Controls ? _cardAmount : 0f);
            SetCard(creditsCard, _shown == Card.Credits ? _cardAmount : 0f);
            SetCard(stagesCard, _shown == Card.Stages ? _cardAmount : 0f);
        }

        static void SetCard(CanvasGroup group, float amount)
        {
            if (group == null) return;
            group.alpha = amount;
            group.gameObject.SetActive(amount > 0.001f);
        }

        void ApplyFade()
        {
            if (fade == null) return;
            var c = fade.color;
            fade.color = new Color(c.r, c.g, c.b, _fadeAmount);
            // A full-screen transparent blend every frame for nothing, otherwise.
            fade.enabled = _fadeAmount > 0.001f;
        }

        // ------------------------------------------------------------------ title

        /// <summary>
        /// Remember where each title piece is meant to end up, so the entrance below can be a pure
        /// function of the clock rather than an accumulation. Accumulating it is how an entrance ends
        /// up half a screen from where the layout put it after one browser resize.
        /// </summary>
        void CacheTitlePieces()
        {
            int n = titlePieces != null ? titlePieces.Length : 0;
            _pieceMin = new Vector2[n];
            _pieceMax = new Vector2[n];
            _pieceRot = new Quaternion[n];
            for (int i = 0; i < n; i++)
            {
                if (titlePieces[i] == null) continue;
                _pieceMin[i] = titlePieces[i].offsetMin;
                _pieceMax[i] = titlePieces[i].offsetMax;
                _pieceRot[i] = titlePieces[i].localRotation;
            }
        }

        /// <summary>
        /// The masthead, the rosette and the ribbon being pinned to the board, one after another.
        ///
        /// It stops touching them the moment the last one has landed. A menu that keeps writing to
        /// its own layout every frame cannot be laid out by anything else — and the first thing that
        /// would break is the framing label, which lives on the same canvas.
        /// </summary>
        void AnimateTitle()
        {
            if (_titleLanded || _pieceMin == null) return;

            bool allDone = true;
            for (int i = 0; i < titlePieces.Length; i++)
            {
                var piece = titlePieces[i];
                if (piece == null) continue;

                float local = Mathf.Clamp01((_titleClock - i * titlePieceDelay) /
                                            Mathf.Max(titlePieceFall, 0.01f));
                if (local < 1f) allDone = false;

                // Ease-out-back, written out: the piece overshoots its resting place and settles,
                // which is what makes it land rather than arrive.
                float p = local - 1f;
                float ease = 1f + p * p * (2.70158f * p + 1.70158f);
                float fall = (1f - ease) * titleDrop;

                piece.offsetMin = _pieceMin[i] + new Vector2(0f, fall);
                piece.offsetMax = _pieceMax[i] + new Vector2(0f, fall);
                piece.localRotation = _pieceRot[i] * Quaternion.Euler(0f, 0f, (1f - ease) * -titleSwing);
                float s = Mathf.Lerp(0.88f, 1f, ease);
                piece.localScale = new Vector3(s, s, 1f);
            }

            if (!allDone) return;

            // Snapped, not left on the last frame of the ease. Ease-out-back reaches 1 exactly, but
            // only at exactly 1, and a scale of 0.99997 on a 1536-pixel masthead is a visible half
            // pixel of blur.
            for (int i = 0; i < titlePieces.Length; i++)
            {
                if (titlePieces[i] == null) continue;
                titlePieces[i].offsetMin = _pieceMin[i];
                titlePieces[i].offsetMax = _pieceMax[i];
                titlePieces[i].localRotation = _pieceRot[i];
                titlePieces[i].localScale = Vector3.one;
            }
            _titleLanded = true;
        }

        // ------------------------------------------------------------------ bunting

        void StirBunting()
        {
            if (_pennantRest == null) return;
            for (int i = 0; i < pennants.Length; i++)
            {
                if (pennants[i] == null) continue;
                // Local X is along the cord, so this is the pennant swinging on its peg. The phase
                // walks along the run rather than being random per pennant, which is what makes it
                // read as one gust crossing the line instead of seventeen separate flags.
                float phase = _clock * pennantSpeed - i * 0.42f;
                float swing = pennantLean + Mathf.Sin(phase) * pennantSway;
                float twist = Mathf.Sin(phase * 1.37f + 1.1f) * (pennantSway * 0.7f);
                pennants[i].localRotation = _pennantRest[i] * Quaternion.Euler(swing, twist, 0f);
            }
        }

        // ------------------------------------------------------------------ framing review

        void FramingKeys(Keyboard kb)
        {
            if (kb == null || vistas == null || vistas.Length == 0) return;

            if (kb.f1Key.wasPressedThisFrame) ApplyFraming(vista + 1);
            if (kb.f2Key.wasPressedThisFrame) ApplyFraming(vista - 1);

            if (kb.f3Key.wasPressedThisFrame && lawnArt != null)
            {
                // Next picture, on the framing's own placement. Every shape reads differently
                // through this much foreshortening and the only way to know which one survives it
                // is to look at all of them.
                var all = TargetShapes.All;
                var v = vistas[vista];
                int at = Array.IndexOf(all, v.shape);
                v.shape = all[((at < 0 ? 0 : at) + 1) % all.Length];
                lawnArt.Apply(v.shape, v.pictureCentre, v.pictureRadius, v.pictureYaw,
                              new Vector2(v.mowerPos.x, v.mowerPos.z));
                UpdateFramingLabel();
            }

            if (kb.f4Key.wasPressedThisFrame && lawnArt != null)
            {
                lawnArt.chalkGuide = !lawnArt.chalkGuide;
                lawnArt.Rebuild();
                UpdateFramingLabel();
            }

            if (kb.f5Key.wasPressedThisFrame)
            {
                if (buntingRoot != null) buntingRoot.gameObject.SetActive(!buntingRoot.gameObject.activeSelf);
                if (propsRoot != null) propsRoot.gameObject.SetActive(!propsRoot.gameObject.activeSelf);
                UpdateFramingLabel();
            }
        }

        void UpdateFramingLabel()
        {
            if (framingLabel == null || !DebugTools) return;
            if (vistas == null || vistas.Length == 0) { framingLabel.text = "no vistas"; return; }
            var v = vistas[vista];
            bool near = buntingRoot == null || buntingRoot.gameObject.activeSelf;
            framingLabel.text =
                $"F1/F2 FRAMING  {vista + 1}/{vistas.Length}  {v.name}   h {v.height:0.0} m  fov {v.fov:0}\n" +
                $"F3 PICTURE  {v.shape}    F4 CHALK  {(lawnArt != null && lawnArt.chalkGuide ? "on" : "off")}" +
                $"    F5 NEAR DRESSING  {(near ? "on" : "off")}";
        }
    }
}
