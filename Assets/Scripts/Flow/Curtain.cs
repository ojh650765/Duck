using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DuckMow
{
    /// <summary>
    /// The one thing on screen while the game changes scene, and the only object in the project that
    /// is alive in two scenes at once on purpose.
    ///
    /// Every other cross-scene mechanism in this game is a static — see <see cref="RallyHandoff"/> on
    /// why a DontDestroyOnLoad carrier is normally the wrong answer. This is the exception that
    /// proves it: a static cannot draw, and the whole job here is to have PIXELS on screen through
    /// the frame where one scene's camera dies and another's has not rendered yet. Nothing else can
    /// cover that frame, so this survives the load, and it is guarded against duplicating itself the
    /// way those handoffs warned about.
    ///
    /// What it guarantees, and what every caller is entitled to assume:
    ///   * <see cref="Covered"/> means the frame is opaque. Load, unload, teleport and re-light
    ///     freely; nothing behind it can be seen.
    ///   * It keeps ANIMATING while covered, on the unscaled clock. A held frozen frame reads as a
    ///     hang even when the game is fine, and a browser decompressing a scene gives you plenty of
    ///     held frames to fill.
    ///   * It carries sound of its own, from an asset rather than a scene, so the boundary is never
    ///     silent even in the stretch where neither scene has an AudioDirector alive.
    ///   * It never shows black. Every backing colour is out of the garden.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class Curtain : MonoBehaviour
    {
        // ------------------------------------------------------------------ the singleton

        static Curtain _instance;

        /// <summary>The live curtain, or null. For code that must not create one just to ask.</summary>
        public static Curtain Live => _instance;

        /// <summary>
        /// The curtain, built if it is not already there.
        ///
        /// Built in code rather than dragged out of a prefab because it has to be reachable from
        /// four scenes, two of which are opened and played on their own during development. A prefab
        /// reference would mean wiring it into all four and remembering to on the fifth.
        /// </summary>
        public static Curtain Ensure()
        {
            if (_instance != null) return _instance;

            // A survivor from a previous scene that has not run Awake yet on this frame, or one that
            // an editor script left behind. Finding it is cheaper than the duplicate it prevents.
            var found = FindFirstObjectByType<Curtain>(FindObjectsInactive.Include);
            if (found != null) { found.Init(); return _instance; }

            var go = new GameObject("~ Curtain");
            var c = go.AddComponent<Curtain>();
            c.Init();
            return _instance;
        }

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            Init();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ------------------------------------------------------------------ state

        enum Phase { Clear, Closing, Held, Opening }

        /// <summary>Seconds of covered frame after which the curtain raises itself. See Update.</summary>
        const float HeldBudget = 10f;

        Phase _phase = Phase.Clear;
        float _amount;
        float _closeSeconds = 0.66f;
        float _openSeconds = 0.8f;
        float _intensity;
        float _holdClock;
        CurtainStyle _style = CurtainStyle.LeafSweep;
        int _seed;

        /// <summary>True once the frame is genuinely opaque and the scene behind can be replaced.</summary>
        public bool Covered => _phase == Phase.Held || (_phase == Phase.Closing && _amount >= 1f);

        /// <summary>0 clear, 1 covered. Read by anything that wants to duck under the curtain.</summary>
        public float Amount => _amount;

        /// <summary>True whenever the curtain is doing anything at all.</summary>
        public bool Busy => _phase != Phase.Clear;

        Canvas _canvas;
        CurtainField _field;
        RectTransform _sign;
        RectTransform _plate;
        TMP_Text _headline;
        TMP_Text _kicker;
        Image _plateImage, _plateEdge;
        CanvasGroup _signGroup;

        AudioSource _sweepSource, _stingSource, _bedSource;
        CurtainAudioSet _audio;

        float _signAmount;      // -1 offscreen right, 0 centred, +1 offscreen left
        float _signTarget;
        bool _signWanted;

        /// <summary>True once the watchdog has taken over. Stops it firing every frame.</summary>
        bool _selfRaising;

        // ------------------------------------------------------------------ construction

        void Init()
        {
            if (_instance == this && _canvas != null) return;
            _instance = this;

            transform.SetParent(null, false);
            DontDestroyOnLoad(gameObject);

            if (_canvas == null) BuildCanvas();
            if (_sweepSource == null) BuildAudio();

            // Hidden until asked for. A curtain that starts covered would eat the first frame of
            // whatever scene happens to be opened for review.
            Apply();
        }

        void BuildCanvas()
        {
            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above everything. The HUD sits in the low hundreds; the comic pages in the low
            // thousands. This is the last thing composited, always.
            _canvas.sortingOrder = 30000;

            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // Deliberately NO GraphicRaycaster. The curtain must never swallow a click, and input is
            // gated at the InputReader anyway — two systems both claiming to own "the player cannot
            // act right now" is how one of them ends up stuck on.

            var fieldGo = new GameObject("Field", typeof(RectTransform));
            fieldGo.transform.SetParent(transform, false);
            var fr = (RectTransform)fieldGo.transform;
            Stretch(fr);
            _field = fieldGo.AddComponent<CurtainField>();
            _field.raycastTarget = false;

            BuildSign();
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        void BuildSign()
        {
            var signGo = new GameObject("Sign", typeof(RectTransform));
            signGo.transform.SetParent(transform, false);
            _sign = (RectTransform)signGo.transform;
            Stretch(_sign);
            _signGroup = signGo.AddComponent<CanvasGroup>();
            _signGroup.blocksRaycasts = false;
            _signGroup.interactable = false;

            // The plate. A board of the same painted timber the venue's signage is made of, tilted
            // a couple of degrees so it reads as a thing somebody carried in rather than a UI panel.
            var plateGo = new GameObject("Plate", typeof(RectTransform));
            plateGo.transform.SetParent(_sign, false);
            _plate = (RectTransform)plateGo.transform;
            _plate.anchorMin = _plate.anchorMax = new Vector2(0.5f, 0.5f);
            _plate.pivot = new Vector2(0.5f, 0.5f);
            _plate.sizeDelta = new Vector2(1420f, 232f);
            _plate.localRotation = Quaternion.Euler(0f, 0f, -1.6f);

            var edgeGo = new GameObject("Edge", typeof(RectTransform));
            edgeGo.transform.SetParent(_plate, false);
            var er = (RectTransform)edgeGo.transform;
            Stretch(er);
            er.offsetMin = new Vector2(-9f, -9f);
            er.offsetMax = new Vector2(9f, 9f);
            _plateEdge = edgeGo.AddComponent<Image>();
            _plateEdge.sprite = RoundedSprite();
            _plateEdge.type = Image.Type.Sliced;
            _plateEdge.raycastTarget = false;
            _plateEdge.color = new Color(0.93f, 0.89f, 0.75f, 1f);

            var faceGo = new GameObject("Face", typeof(RectTransform));
            faceGo.transform.SetParent(_plate, false);
            Stretch((RectTransform)faceGo.transform);
            _plateImage = faceGo.AddComponent<Image>();
            _plateImage.sprite = RoundedSprite();
            _plateImage.type = Image.Type.Sliced;
            _plateImage.raycastTarget = false;
            _plateImage.color = new Color(0.13f, 0.28f, 0.17f, 1f);

            _kicker = MakeText("Kicker", _plate, 34f, FontStyles.Bold,
                               new Vector2(0f, 62f), new Vector2(1300f, 48f));
            _kicker.color = new Color(0.86f, 0.76f, 0.42f, 1f);
            _kicker.characterSpacing = 16f;

            _headline = MakeText("Headline", _plate, 92f, FontStyles.Bold,
                                 new Vector2(0f, -18f), new Vector2(1340f, 120f));
            _headline.color = new Color(0.97f, 0.95f, 0.88f, 1f);
            _headline.characterSpacing = 6f;
        }

        static TMP_Text MakeText(string name, Transform parent, float size, FontStyles style,
                                 Vector2 pos, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeDelta;

            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        static Sprite _rounded;

        /// <summary>A nine-sliced rounded panel, built once, so the sign ships no art file.</summary>
        static Sprite RoundedSprite()
        {
            if (_rounded != null) return _rounded;

            const int n = 48, r = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false, false)
            {
                name = "Curtain plate",
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
            _rounded.name = "Curtain plate";
            _rounded.hideFlags = HideFlags.HideAndDontSave;
            return _rounded;
        }

        void BuildAudio()
        {
            _audio = Resources.Load<CurtainAudioSet>("CurtainAudio");
            if (_audio == null)
                Debug.LogWarning("[Curtain] no Resources/CurtainAudio asset; transitions will be " +
                                 "silent. Run 'Duck/9 · Build transition audio set'.");

            _sweepSource = MakeSource("Sweep", 0.85f);
            _stingSource = MakeSource("Sting", 0.9f);
            _bedSource = MakeSource("Bed", 0.55f);
        }

        AudioSource MakeSource(string name, float volume)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;                 // the curtain is not in the world
            s.volume = volume;
            s.ignoreListenerPause = true;
            // Unaffected by the hit stop the defence phase runs the world at two percent for.
            s.ignoreListenerVolume = false;
            return s;
        }

        // ------------------------------------------------------------------ the public verbs

        /// <summary>
        /// Close the curtain and do not return until the frame is opaque.
        ///
        /// The caller's next line may safely load, unload, teleport or re-light anything at all.
        /// </summary>
        public IEnumerator Close(CurtainStyle style, float intensity,
                                 string headline = null, string kicker = null,
                                 Vector2? sweepHint = null)
        {
            intensity = Mathf.Clamp01(intensity);

            // Already shut, which is the chained case: a stage handing back to a round that is
            // immediately leaving for the next stage, so two seams meet under one covered frame.
            // Replaying the close would be a wipe on top of an opaque screen — invisible, and half a
            // second of the player waiting for it. Instead the BOARD changes: it slides out and the
            // next destination slides in, which is exactly what a real tournament does between
            // events and is the only part of the transition there is anything left to see.
            if (_phase == Phase.Held)
            {
                if (!string.IsNullOrEmpty(headline))
                {
                    Dress(headline, kicker);
                    _signWanted = true;
                    _signAmount = -1.25f;
                    _signTarget = 0f;
                }
                yield break;
            }

            _style = style;
            _intensity = intensity;
            _seed = Random.Range(1, int.MaxValue);
            // Later transitions are quicker. The pacing tightening as the championship escalates is
            // the cheapest way to make the back half feel more urgent than the front half.
            _closeSeconds = Mathf.Lerp(0.72f, 0.48f, intensity);
            _openSeconds = Mathf.Lerp(0.88f, 0.62f, intensity);

            _field.Rebuild(style, intensity, _seed, sweepHint);
            _field.Leaving = false;
            _field.Glow = 0f;
            _holdClock = 0f;
            _selfRaising = false;

            Dress(headline, kicker);
            _signWanted = !string.IsNullOrEmpty(headline);
            _signAmount = -1.25f;
            _signTarget = 0f;

            PlaySweep();

            _phase = Phase.Closing;
            while (_amount < 1f) yield return null;
            _phase = Phase.Held;

            PlaySting();
        }

        /// <summary>
        /// Open the curtain again. Yield on it if you want to know when the frame is clear; ignore it
        /// if you do not, because the curtain finishes the move on its own either way.
        /// </summary>
        public IEnumerator Open(float extraHold = 0f)
        {
            if (_phase == Phase.Clear) yield break;

            // Hold, so the sign has landed and been read before the world comes back. Extra hold is
            // for the callers that want a beat of held cover after the load — the run into a stage
            // finale, mostly.
            float hold = Mathf.Lerp(0.34f, 0.24f, _intensity) + Mathf.Max(0f, extraHold);
            float t = 0f;
            while (t < hold) { t += Mathf.Min(Time.unscaledDeltaTime, 0.1f); yield return null; }

            _field.Leaving = true;
            _signTarget = 1.35f;
            _phase = Phase.Opening;
            while (_amount > 0f) yield return null;
            _phase = Phase.Clear;
            _signWanted = false;
        }

        /// <summary>Close and open around a piece of work, whatever it is.</summary>
        public IEnumerator Around(CurtainStyle style, float intensity, IEnumerator work,
                                  string headline = null, string kicker = null, float extraHold = 0f)
        {
            yield return Close(style, intensity, headline, kicker);
            if (work != null) yield return work;
            yield return Open(extraHold);
        }

        /// <summary>
        /// Drop the curtain this instant with no animation. For a hard abort — Escape held down, or
        /// an editor script jumping states — where the alternative is showing the seam.
        /// </summary>
        public void CloseInstant(CurtainStyle style, float intensity)
        {
            _style = style;
            _intensity = Mathf.Clamp01(intensity);
            _seed = Random.Range(1, int.MaxValue);
            _field.Rebuild(style, _intensity, _seed);
            _field.Leaving = false;
            _amount = 1f;
            _phase = Phase.Held;
            _signWanted = false;
            _holdClock = 0f;
            _selfRaising = false;
            Apply();
        }

        /// <summary>Abandon a transition without animating it away. Used when the game is torn down.</summary>
        public void ClearInstant()
        {
            _amount = 0f;
            _phase = Phase.Clear;
            _signWanted = false;
            _selfRaising = false;
            _field.Leaving = false;
            Apply();
        }

        void Dress(string headline, string kicker)
        {
            if (_headline != null) _headline.text = headline ?? string.Empty;
            if (_kicker != null) _kicker.text = kicker ?? string.Empty;

            // The plate takes the curtain's own colour so the sign belongs to the wipe it arrives
            // in, rather than being one shared UI panel bolted onto five different effects.
            if (_plateImage != null)
                _plateImage.color = _style switch
                {
                    CurtainStyle.PetalCurtain => new Color(0.24f, 0.12f, 0.22f),
                    CurtainStyle.Fanfare      => new Color(0.28f, 0.13f, 0.07f),
                    CurtainStyle.BannerWipe   => new Color(0.14f, 0.30f, 0.20f),
                    _                         => new Color(0.12f, 0.25f, 0.13f)
                };
            if (_plateEdge != null)
                _plateEdge.color = _style == CurtainStyle.Fanfare
                    ? new Color(0.95f, 0.80f, 0.32f)
                    : new Color(0.93f, 0.89f, 0.75f);
        }

        // ------------------------------------------------------------------ the drive

        void Update()
        {
            // Unscaled throughout, and clamped. The defence phase holds Time.timeScale at two
            // percent during a hit stop and the browser hands over quarter-second deltas when a tab
            // regains focus; a transition must be immune to both or it either never finishes or
            // finishes in one frame.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            switch (_phase)
            {
                case Phase.Closing:
                    _amount = Mathf.MoveTowards(_amount, 1f, dt / Mathf.Max(_closeSeconds, 0.05f));
                    break;
                case Phase.Held:
                    _amount = 1f;
                    _holdClock += dt;
                    // Budgeted, not trusted — the same rule the opening story and the defence phase
                    // are under, and the stakes here are higher than either. Every other failure in
                    // this system is a frame that looked wrong; a curtain nobody raises is a game
                    // the player cannot see, with no way out and nothing on screen to explain it.
                    //
                    // Generous, because a cold browser cache genuinely can spend several seconds on
                    // a scene, and a curtain that gives up early would tear the load open — the
                    // exact fault it exists to prevent. Ten seconds is far past any honest load and
                    // far short of the player deciding the game has crashed.
                    if (_holdClock > HeldBudget && !_selfRaising)
                    {
                        _selfRaising = true;
                        Debug.LogError($"[Curtain] nothing raised the curtain within {HeldBudget:0}s. " +
                                       "Opening it anyway — something on the far side of a transition " +
                                       "did not finish. Check the console above this line.");
                        // The seam is abandoned as well as the curtain, or the next stage to hand
                        // back would see a transition still in progress and politely decline to
                        // start one of its own.
                        StageSeam.Forget();
                        StartCoroutine(Open());
                    }
                    break;
                case Phase.Opening:
                    _amount = Mathf.MoveTowards(_amount, 0f, dt / Mathf.Max(_openSeconds, 0.05f));
                    _holdClock += dt;
                    break;
                default:
                    return;                      // clear: nothing to draw and nothing to step
            }

            // The sign rides its own spring so it overshoots slightly on the way in — signage that
            // slides to a dead stop reads as a slide, and signage that lands reads as a sign.
            if (_signWanted)
            {
                float speed = _signTarget > 0.5f ? 3.4f : 5.2f;
                _signAmount = Mathf.Lerp(_signAmount, _signTarget, 1f - Mathf.Exp(-speed * dt));
            }

            // The flash under a loud transition, decaying across the hold. Only the top of the
            // escalation gets it; a flash on every boundary is a strobe.
            _field.Glow = _intensity < 0.66f ? 0f
                        : Mathf.Max(0f, 0.55f * (1f - _holdClock / 0.55f)) * (_phase == Phase.Clear ? 0f : 1f);

            Apply();
        }

        void Apply()
        {
            if (_field == null) return;

            _field.Amount = _amount;
            _field.HoldClock = _holdClock;
            _field.SetVerticesDirty();

            bool visible = _amount > 0.0005f;
            if (_field.enabled != visible) _field.enabled = visible;

            if (_sign != null)
            {
                bool signVisible = _signWanted && _amount > 0.5f;
                if (_signGroup != null)
                {
                    // Tied to the cover, not to its own timer, so the sign can never be caught half
                    // on screen over a transparent frame.
                    float a = signVisible ? Mathf.InverseLerp(0.55f, 0.85f, _amount) : 0f;
                    _signGroup.alpha = a;
                }
                if (_plate != null)
                {
                    // Travelling right to left across the frame, which is the direction the curtains
                    // themselves mostly sweep — the sign is carried by the same gust.
                    float w = _canvas != null ? ((RectTransform)_canvas.transform).rect.width : 1920f;
                    _plate.anchoredPosition = new Vector2(-_signAmount * w * 0.95f, 0f);
                    _plate.localRotation = Quaternion.Euler(0f, 0f, -1.6f + _signAmount * 5f);
                }
            }
        }

        // ------------------------------------------------------------------ sound

        void PlaySweep()
        {
            if (_audio == null) return;

            var sweep = _audio.SweepFor(_style);
            if (sweep != null && _sweepSource != null)
            {
                _sweepSource.pitch = Random.Range(0.94f, 1.07f);
                _sweepSource.volume = Mathf.Lerp(0.55f, 0.9f, _intensity);
                _sweepSource.PlayOneShot(sweep);
            }

            // The riser is scheduled so its END lands on the cut rather than its start on the
            // button — a riser that begins when the curtain does finishes early and leaves a hole
            // exactly where the downbeat is supposed to be.
            if (_audio.riser != null && _bedSource != null && _intensity > 0.15f)
            {
                float lead = _audio.riser.length;
                float delay = Mathf.Max(0f, _closeSeconds - lead);
                _bedSource.clip = _audio.riser;
                _bedSource.volume = Mathf.Lerp(0.25f, 0.6f, _intensity);
                _bedSource.PlayDelayed(delay);
            }
        }

        void PlaySting()
        {
            if (_audio == null || _stingSource == null) return;

            if (_audio.impact != null)
            {
                _stingSource.pitch = 1f;
                _stingSource.PlayOneShot(_audio.impact, Mathf.Lerp(0.55f, 0.95f, _intensity));
            }

            var sting = _audio.StingFor(_intensity);
            if (sting != null) _stingSource.PlayOneShot(sting, Mathf.Lerp(0.5f, 0.95f, _intensity));

            // The crowd only turns up once the championship has something to shout about.
            if (_audio.crowdSwell != null && _intensity >= 0.4f)
                _stingSource.PlayOneShot(_audio.crowdSwell, Mathf.Lerp(0.2f, 0.8f, _intensity));
        }

        /// <summary>The gate, for the transitions that are a drive-through rather than a wipe.</summary>
        public void PlayGate(float volume = 0.8f)
        {
            if (_audio != null && _audio.gateCreak != null && _stingSource != null)
                _stingSource.PlayOneShot(_audio.gateCreak, volume);
        }
    }
}
