using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DuckMow
{
    /// <summary>
    /// What the player is told during Bloom Rush.
    ///
    /// Four jobs, and the second is the one the mode lives or dies on:
    ///
    /// THE CLOCK, because everything is decided when it hits zero and nothing before then is final.
    ///
    /// THE BAR — one stacked strip carrying all four shares and the unclaimed remainder, in the
    /// contestants' own colours, at the size they actually are. Not four numbers in a list: the
    /// question a player asks eight times a minute is "am I winning and by how much", and a stacked
    /// bar answers it in peripheral vision while both hands are busy. Numbers on top for when the
    /// answer is close.
    ///
    /// THE MAP, which is the ownership mask itself decoded into liveries — see Duck/TurfMapUI. The
    /// central decision of the mode is spatial ("expand, steal, defend, or go to the middle") and
    /// the player is eighteen inches off the floor looking down a hedge. The bar says who is
    /// winning; only the map says where, and where is the actual question.
    ///
    /// THE CROWN, because a rule that silently makes somebody's roller half again as wide would be
    /// a rule the player loses to without ever learning.
    ///
    /// Built at runtime. Screen-space UI is pinned to frame coordinates rather than to the world, so
    /// unlike the arena there is nothing to drag and nothing gained by baking it.
    /// </summary>
    public class TurfHud : MonoBehaviour
    {
        [Tooltip("The match this reports on. Found in the scene if left empty.")]
        public TurfDirector director;
        [Tooltip("Camera the map's mower pips are placed against. The main one if left empty.")]
        public Camera view;
        [Tooltip("The thing that knows whether a round is waiting on this stage, and therefore what " +
                 "the way out says. Found in the scene if left empty.")]
        public TurfBootstrap stage;

        [Header("Layout")]
        [Tooltip("Diameter of the corner map, in reference pixels.")]
        public float mapSize = 260f;
        [Tooltip("Width of the stacked share bar, in reference pixels.")]
        public float barWidth = 940f;
        public float barHeight = 34f;
        [Tooltip("Seconds a callout stays up. Short: it is punctuation, not a message.")]
        public float calloutHold = 1.2f;

        [Header("Card kit")]
        [Tooltip("The ROUND's own UI sprites, assigned by DuckTurfBuilder so this stage's result " +
                 "card is made of literally the same pieces as stage one's rather than a lookalike.\n\n" +
                 "panel_card_256, panel_card_dark_256, scorecard_blank_256, and the two gauge parts. " +
                 "Null is survivable — the card falls back to plain plates — but it will not match.")]
        public Sprite cardPanel, cardPanelDark, scorecard, gaugeBg, gaugeFill;

        Canvas _canvas;
        TextMeshProUGUI _clock, _callout, _crown, _headline;
        RectTransform _bar;
        readonly Image[] _segment = new Image[TurfArena.Count + 1];
        readonly TextMeshProUGUI[] _segmentLabel = new TextMeshProUGUI[TurfArena.Count];
        readonly float[] _shown = new float[TurfArena.Count + 1];
        RawImage _map;
        Material _mapMat;
        readonly Image[] _pip = new Image[TurfArena.Count];
        CanvasGroup _results, _live;
        RectTransform _liveRoot;
        readonly TextMeshProUGUI[] _place = new TextMeshProUGUI[TurfArena.Count];
        readonly TextMeshProUGUI[] _who = new TextMeshProUGUI[TurfArena.Count];
        readonly TextMeshProUGUI[] _sub = new TextMeshProUGUI[TurfArena.Count];
        readonly TextMeshProUGUI[] _pct = new TextMeshProUGUI[TurfArena.Count];
        readonly Image[] _stripe = new Image[TurfArena.Count];
        readonly RectTransform[] _row = new RectTransform[TurfArena.Count];
        TextMeshProUGUI _resultsRank, _resultsTotal;
        Image _boostFill;
        CanvasGroup _exitGroup;
        TextMeshProUGUI _exitPrompt;

        float _calloutTimer = 99f;
        string _shownCallout = "";

        // The round's palette, copied value for value from DuckUIBuilder rather than eyeballed.
        // Two nearly-identical creams on two screens of the same game is the exact failure the
        // player was describing, and it is only avoided by taking the numbers rather than a look.
        static readonly Color Cream = new(0.97f, 0.94f, 0.86f);
        static readonly Color Ink = new(0.16f, 0.12f, 0.09f);
        static readonly Color Gold = new(1f, 0.85f, 0.45f);
        static readonly Color Nib = new(0.62f, 0.26f, 0.18f);

        static readonly Color Critical = new(1.00f, 0.32f, 0.26f);
        static readonly Color Warning = new(1.00f, 0.70f, 0.24f);
        static readonly Color Unclaimed = new(0.20f, 0.26f, 0.18f);

        void Awake()
        {
            if (director == null) director = FindFirstObjectByType<TurfDirector>();
            if (view == null) view = Camera.main;
            if (stage == null) stage = FindFirstObjectByType<TurfBootstrap>();
            Build();
        }

        void OnDestroy() { if (_mapMat != null) Destroy(_mapMat); }

        // ------------------------------------------------------------------ construction

        void Build()
        {
            var go = new GameObject("Bloom HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 40;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _clock = Text("Clock", 74f, new Vector2(0.5f, 1f), new Vector2(0f, -46f),
                          new Vector2(360f, 90f), TextAlignmentOptions.Center);
            _crown = Text("Crown", 30f, new Vector2(0.5f, 1f), new Vector2(0f, -120f),
                          new Vector2(760f, 42f), TextAlignmentOptions.Center);
            _crown.alpha = 0f;
            _callout = Text("Callout", 86f, new Vector2(0.5f, 0.5f), new Vector2(0f, 190f),
                            new Vector2(1300f, 130f), TextAlignmentOptions.Center);
            _callout.alpha = 0f;
            _headline = Text("Headline", 54f, new Vector2(0.5f, 0.5f), new Vector2(0f, 30f),
                             new Vector2(1400f, 260f), TextAlignmentOptions.Center);
            _headline.alpha = 0f;

            // Everything that belongs to the MATCH goes under one group, so the ending can take the
            // screen back in one move. The round does this too — see HUD.SetGroup: a result card is
            // read, and it cannot be read through a clock, a share bar, a minimap and a fuel gauge
            // still reporting on a match that finished nine seconds ago.
            var live = new GameObject("Live", typeof(RectTransform), typeof(CanvasGroup));
            live.transform.SetParent(_canvas.transform, false);
            var liveRt = live.GetComponent<RectTransform>();
            liveRt.anchorMin = Vector2.zero;
            liveRt.anchorMax = Vector2.one;
            liveRt.offsetMin = liveRt.offsetMax = Vector2.zero;
            _live = live.GetComponent<CanvasGroup>();
            _live.interactable = _live.blocksRaycasts = false;
            _liveRoot = liveRt;

            _clock.rectTransform.SetParent(liveRt, false);
            _crown.rectTransform.SetParent(liveRt, false);

            BuildBar();
            BuildMap();
            BuildBoost();
            BuildStandings();
            BuildExitPrompt();

            // Hard top right, which this HUD leaves empty on purpose: the territory map is anchored
            // to the top LEFT and the clock is centred. Parented to the CANVAS rather than to the
            // live group above, deliberately — that group stands down as the reveal takes the screen,
            // and the way out of a stage must not disappear with the match it was in.
            _pause = DuckMow.UI.PauseButton.Attach(_canvas);
        }

        /// <summary>
        /// The plate in the top corner that opens the pause board. One class shared by all three
        /// stages — see <see cref="DuckMow.UI.PauseButton"/>, which also explains why it is polled
        /// rather than given an onClick this project has no EventSystem to deliver.
        /// </summary>
        DuckMow.UI.PauseButton _pause;

        /// <summary>
        /// The way out, in the round's own dark plate at the foot of the frame.
        ///
        /// OUTSIDE the results group, and that is the one thing about this that had to be got
        /// right. The card belongs to one beat of the ending — it fades up on the player's machine
        /// and back down again the moment the camera leaves for the board — whereas the prompt
        /// belongs to the END of the ending, which is a beat LATER than the card's and is spent
        /// looking at the scoreboard. Parented to the card it would be invisible at exactly the
        /// moment it exists to be read. Outside the live group too, for the mirror-image reason:
        /// that whole group stands down as the reveal takes the screen.
        ///
        /// Bottom centre, where the share bar was. That is not a coincidence worth avoiding — the
        /// bar has faded by then, the strip is empty, and it is the one place in this frame the
        /// player's eye has already been returning to all match.
        ///
        /// Same plate, same cream, same bold twenty-point capitals as the round's own hint line
        /// under its result card (DuckUIBuilder's RetryPlate). This stage borrows the round's kit
        /// everywhere else on this screen; a prompt in a different dress would be the one element
        /// that gave away that two people built it.
        /// </summary>
        void BuildExitPrompt()
        {
            var root = new GameObject("Exit prompt", typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(_canvas.transform, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 46f);
            rt.sizeDelta = new Vector2(620f, 72f);

            _exitGroup = root.GetComponent<CanvasGroup>();
            _exitGroup.alpha = 0f;
            _exitGroup.interactable = _exitGroup.blocksRaycasts = false;

            Plate(rt, cardPanelDark, Color.white);

            var inner = Frac("Text", rt, 0.05f, 0.16f, 0.95f, 0.84f);
            _exitPrompt = CardText(inner, "", 24f, TextAlignmentOptions.Center, Cream, 0.24f, false);
            _exitPrompt.fontStyle = FontStyles.Bold;
        }

        void BuildBar()
        {
            var root = new GameObject("Share bar", typeof(RectTransform));
            root.transform.SetParent(_liveRoot, false);
            _bar = root.GetComponent<RectTransform>();
            _bar.anchorMin = _bar.anchorMax = new Vector2(0.5f, 0f);
            _bar.pivot = new Vector2(0.5f, 0f);
            _bar.anchoredPosition = new Vector2(0f, 42f);
            _bar.sizeDelta = new Vector2(barWidth, barHeight);

            var back = Box("Backing", _bar, Vector2.zero, Vector2.one);
            back.rectTransform.offsetMin = new Vector2(-4f, -4f);
            back.rectTransform.offsetMax = new Vector2(4f, 4f);
            back.color = new Color(0f, 0f, 0f, 0.55f);

            // Segments are left-anchored strips whose width and offset are both driven every frame,
            // so the whole strip is one continuous bar with no gaps to misread as somebody's ground.
            for (int i = 0; i <= TurfArena.Count; i++)
            {
                var seg = Box($"Segment {i}", _bar, new Vector2(0f, 0f), new Vector2(0f, 1f));
                seg.rectTransform.pivot = new Vector2(0f, 0.5f);
                seg.rectTransform.anchoredPosition = Vector2.zero;
                seg.rectTransform.sizeDelta = new Vector2(0f, 0f);
                seg.color = i < TurfArena.Count ? TurfArena.Livery(i) : Unclaimed;
                _segment[i] = seg;

                if (i >= TurfArena.Count) continue;
                var label = Text($"Share {i}", 22f, new Vector2(0.5f, 0.5f), Vector2.zero,
                                 new Vector2(140f, barHeight), TextAlignmentOptions.Center,
                                 seg.rectTransform);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                label.color = Color.white;
                label.fontStyle = FontStyles.Bold;
                _segmentLabel[i] = label;
            }
        }

        void BuildMap()
        {
            var go = new GameObject("Territory map", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(_liveRoot, false);
            _map = go.GetComponent<RawImage>();
            _map.raycastTarget = false;
            var rt = _map.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(38f, -38f);
            rt.sizeDelta = new Vector2(mapSize, mapSize);

            var shader = Shader.Find("Duck/TurfMapUI");
            if (shader != null)
            {
                _mapMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _map.material = _mapMat;
            }
            else
            {
                // No shader is a wiring fault, not a reason to ship a HUD with a white hole in the
                // corner where the map should be.
                _map.color = new Color(0f, 0f, 0f, 0.45f);
                Debug.LogWarning("[Bloom] Duck/TurfMapUI is missing; the HUD map is a flat panel.");
            }

            for (int i = 0; i < TurfArena.Count; i++)
            {
                var pip = Box($"Pip {i}", rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                pip.rectTransform.sizeDelta = new Vector2(13f, 13f);
                pip.color = Color.Lerp(TurfArena.Livery(i), Color.white, 0.55f);
                _pip[i] = pip;
            }
        }

        /// <summary>
        /// The BOOST GAUGE, lifted from the round rather than reinvented.
        ///
        /// Same corner, same sprites, same label, same amber. The mode grants boost for driving a
        /// clean line and the player had no way to know they had any — a resource with no readout
        /// is a resource that does not exist, and this one is most of what separates a good line
        /// from a fast one.
        /// </summary>
        void BuildBoost()
        {
            var boost = Frac("Boost", _liveRoot, 0.018f, 0.035f, 0.235f, 0.125f);

            var bg = Frac("Bg", boost, 0f, 0.02f, 1f, 0.50f);
            Plate(bg, gaugeBg, Color.white);

            var fill = Frac("Fill", bg, 0.015f, 0.16f, 0.985f, 0.84f);
            var bf = Plate(fill, gaugeFill, new Color(1f, 0.78f, 0.32f));
            bf.type = Image.Type.Filled;
            bf.fillMethod = Image.FillMethod.Horizontal;
            bf.fillAmount = 0f;
            _boostFill = bf;

            var label = Frac("Label", boost, 0.01f, 0.58f, 0.7f, 1f);
            CardText(label, "BOOST", 20f, TextAlignmentOptions.Left, Gold, 0.24f, false)
                .fontStyle = FontStyles.Bold;
        }

        /// <summary>
        /// The result, built out of the ROUND's card kit.
        ///
        /// Not a lookalike — the same sprites, the same nine-slice plates, the same type sizes, the
        /// same four colours, laid out on the same left-hand column. See DuckUIBuilder.BuildResults:
        /// the round takes the left forty percent of the frame and deliberately leaves the right
        /// clear for the duck, and the camera obliges by aiming off-centre. This stage now ends on
        /// exactly that shot (CameraMode.Verdict), so the card belongs in exactly that place.
        ///
        /// Four rows, one per gardener, in finishing order: a place on a scorecard, a name, the
        /// species under it, and the share of the arena. The livery survives as a stripe down the
        /// edge of each row rather than as the colour of the text, because four saturated colours
        /// on a cream plate is four different levels of legibility and one of them is always
        /// unreadable — the round makes the same choice with its judges' names.
        /// </summary>
        void BuildStandings()
        {
            var root = new GameObject("Results", typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(_canvas.transform, false);
            var full = root.GetComponent<RectTransform>();
            full.anchorMin = Vector2.zero;
            full.anchorMax = Vector2.one;
            full.offsetMin = full.offsetMax = Vector2.zero;
            _results = root.GetComponent<CanvasGroup>();
            _results.alpha = 0f;
            _results.interactable = _results.blocksRaycasts = false;

            var column = Frac("Column", full, 0.025f, 0.06f, 0.40f, 0.95f);

            //   title   0.90 -> 1.00
            //   rows    0.385 -> 0.885   (four bands)
            //   summary 0.235 -> 0.365
            var titlePlate = Frac("Title", column, 0f, 0.90f, 1f, 1.00f);
            Plate(titlePlate, cardPanelDark, Color.white);
            var title = Frac("Text", titlePlate, 0.04f, 0.10f, 0.96f, 0.90f);
            CardText(title, "GROUND COVERED", 30f, TextAlignmentOptions.Left, Cream, 0.22f, false)
                .fontStyle = FontStyles.Bold;

            const float rowTop = 0.885f;
            const float rowH = 0.125f;

            for (int i = 0; i < TurfArena.Count; i++)
            {
                float top = rowTop - i * rowH;
                var row = Frac($"Place{i}", column, 0f, top - rowH + 0.010f, 1f, top - 0.010f);
                Plate(row, cardPanel, Color.white);
                _row[i] = row;

                // The livery, down the leading edge. Small, but it is the only thing on the card
                // that says at a glance which of these rows is the machine you were driving.
                var stripe = Frac("Livery", row, 0.012f, 0.10f, 0.038f, 0.90f);
                _stripe[i] = Plate(stripe, null, Color.white);

                var card = Frac("Card", row, 0.055f, 0.12f, 0.185f, 0.88f);
                Plate(card, scorecard, Color.white);
                var num = Frac("Num", card, 0.1f, 0.08f, 0.9f, 0.92f);
                _place[i] = CardText(num, "", 40f, TextAlignmentOptions.Center, Ink, 0.10f, false);
                _place[i].fontStyle = FontStyles.Bold;

                var nm = Frac("Name", row, 0.215f, 0.54f, 0.66f, 0.94f);
                _who[i] = CardText(nm, "", 22f, TextAlignmentOptions.Left, Nib, 0.10f, false);
                _who[i].fontStyle = FontStyles.Bold;

                var sp = Frac("Species", row, 0.215f, 0.08f, 0.66f, 0.52f);
                _sub[i] = CardText(sp, "", 18f, TextAlignmentOptions.TopLeft, Ink, 0.06f);

                var share = Frac("Share", row, 0.66f, 0.14f, 0.965f, 0.86f);
                _pct[i] = CardText(share, "", 40f, TextAlignmentOptions.Right, Ink, 0.10f, false);
                _pct[i].fontStyle = FontStyles.Bold;
            }

            // The summary band, the round's dark plate with the round's gold on it: where the
            // player finished, and what that ground is worth to the competition.
            var summary = Frac("Summary", column, 0f, 0.235f, 1f, 0.365f);
            Plate(summary, cardPanelDark, Color.white);

            var rank = Frac("Rank", summary, 0.04f, 0.44f, 0.96f, 0.94f);
            _resultsRank = CardText(rank, "", 56f, TextAlignmentOptions.Left, Gold, 0.26f, false);
            _resultsRank.fontStyle = FontStyles.Bold;

            var total = Frac("Total", summary, 0.04f, 0.08f, 0.96f, 0.42f);
            _resultsTotal = CardText(total, "", 26f, TextAlignmentOptions.Left, Cream, 0.22f, false);
            _resultsTotal.fontStyle = FontStyles.Bold;
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            // Before the director guard — see RallyHud, which does the same for the same reason: the
            // pause plate is the one thing on this HUD that has to work in a scene whose director
            // never came up, because that is exactly when a player needs a way out.
            _pause?.Tick();

            if (director == null) return;
            var mask = TurfMask.Instance;

            TickClock();
            TickBar(mask);
            TickMap();
            TickCrown();
            TickCallout();
            TickBoost();
            TickReveal(mask);
            TickExitPrompt();
        }

        /// <summary>
        /// The way out, once there is one.
        ///
        /// Every decision about WHEN belongs to <see cref="TurfBootstrap"/> and none of it is
        /// second-guessed here — this reads one bool and draws it. That is worth saying out loud
        /// because the tempting version is to test the director's phase from the HUD, which would
        /// put the rule "the prompt is up once the presentation has finished" in two files that are
        /// free to disagree, and the disagreement would be a prompt offering a key that does
        /// nothing.
        ///
        /// The TEXT is re-read every frame rather than set once, for a duller reason: this HUD is
        /// built in Awake and the bootstrap does not know whether a round is waiting until its own
        /// Start, so anything latched at construction would be latched from the wrong answer.
        ///
        /// It breathes. A prompt at rest on a still frame — and this is the stillest frame in the
        /// stage, a finished board with nothing moving on it — is furniture; one that swells on a
        /// slow beat is something asking to be pressed. Unscaled, so it keeps breathing through a
        /// hit stop, and gentle enough at four percent that it is noticed rather than watched.
        /// </summary>
        void TickExitPrompt()
        {
            if (_exitGroup == null || _exitPrompt == null) return;

            bool up = stage != null && stage.ExitPromptUp;
            _exitGroup.alpha = Mathf.MoveTowards(_exitGroup.alpha, up ? 1f : 0f,
                                                 Time.unscaledDeltaTime * 3f);
            if (_exitGroup.alpha <= 0.001f) return;

            if (up) _exitPrompt.text = stage.ExitPrompt;
            float beat = 1f + Mathf.Sin(Time.unscaledTime * 2.6f) * 0.04f;
            _exitGroup.transform.localScale = Vector3.one * beat;
        }

        void TickClock()
        {
            float t = Mathf.Max(0f, director.TimeRemaining);
            _clock.text = $"{Mathf.FloorToInt(t / 60f)}:{Mathf.FloorToInt(t % 60f):00}";

            bool critical = t <= 10f && director.State == TurfDirector.Phase.Live;
            _clock.color = critical ? Critical : t <= 20f ? Warning : Color.white;
            // A heartbeat rather than a flash: the clock swells on the beat in the closing seconds,
            // which registers in peripheral vision without pulling the eye off the road.
            float beat = critical ? 1f + Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.PI)) * 0.12f : 1f;
            _clock.rectTransform.localScale = Vector3.one * beat;
        }

        void TickBar(TurfMask mask)
        {
            // The widgets, not just the mask. A domain reload in play mode — which happens every
            // time a script is touched while the arena is running, which during tuning is
            // constantly — restores this component with its arrays intact but the Images they point
            // at gone, and Awake is not run again to rebuild them. The result was an exception every
            // frame for the rest of the session, which buries whatever real fault came next.
            if (mask == null || _segment[0] == null) return;

            // Eased toward the truth rather than snapped to it. The underlying numbers move by
            // fractions of a percent per frame and a bar that tracks them exactly reads as noise;
            // a bar that arrives a moment late reads as a share growing.
            float k = 1f - Mathf.Exp(-9f * Time.deltaTime);
            for (int i = 0; i < TurfArena.Count; i++)
                _shown[i] = Mathf.Lerp(_shown[i], mask.Share(i), k);
            _shown[TurfArena.Count] = Mathf.Lerp(_shown[TurfArena.Count], mask.NeutralShare, k);

            float x = 0f;
            for (int i = 0; i <= TurfArena.Count; i++)
            {
                float w = _shown[i] * barWidth;
                var rt = _segment[i].rectTransform;
                rt.anchoredPosition = new Vector2(x, 0f);
                rt.sizeDelta = new Vector2(w, 0f);
                x += w;

                if (i >= TurfArena.Count) continue;
                var label = _segmentLabel[i];
                // Hidden below the width its own text needs, rather than shrunk. A 3% sliver with
                // "HORACE 3%" spilling out of both ends is less readable than a bare sliver.
                bool room = w > 96f;
                label.alpha = room ? 1f : 0f;
                if (room)
                {
                    var c = director.CompetitorAt(i);
                    label.text = $"{(c != null && c.isPlayer ? "YOU" : TurfArena.Get(i).contestant)}  " +
                                 $"{_shown[i] * 100f:0}%";
                }
            }
        }

        void TickMap()
        {
            if (_map == null) return;
            float radius = mapSize * 0.5f;
            // The map's own edge is the arena's edge, so a pip's distance from the middle of the
            // disc is its mower's distance from the middle of the pitch, to scale.
            float perMetre = radius / (TurfMask.Half);

            for (int i = 0; i < TurfArena.Count; i++)
            {
                // Same domain-reload guard the share bar carries, and for the same reason: touching
                // a script while the arena is running restores this component with its arrays intact
                // and the Images they point at gone, and Awake does not run again to rebuild them.
                if (_pip[i] == null) continue;
                var c = director.CompetitorAt(i);
                if (c == null) { _pip[i].enabled = false; continue; }
                _pip[i].enabled = true;
                Vector3 p = c.Position;
                _pip[i].rectTransform.anchoredPosition = new Vector2(p.x, p.z) * perMetre;
                _pip[i].rectTransform.sizeDelta = Vector2.one * (c.isPlayer ? 19f : 13f);
            }
        }

        void TickCrown()
        {
            int crown = director.Crown;
            if (crown < 0)
            {
                _crown.alpha = Mathf.MoveTowards(_crown.alpha, 0f, Time.deltaTime * 3f);
                return;
            }
            var c = director.CompetitorAt(crown);
            _crown.text = c != null && c.isPlayer
                ? "◆ YOU HOLD THE MIDDLE — WIDER ROLLER"
                : $"◆ {TurfArena.Get(crown).contestant} HOLDS THE MIDDLE";
            _crown.color = TurfArena.Livery(crown);
            _crown.alpha = Mathf.MoveTowards(_crown.alpha, 1f, Time.deltaTime * 4f);
        }

        void TickCallout()
        {
            if (director.Callout != _shownCallout && director.CalloutAge < 0.2f)
            {
                _shownCallout = director.Callout;
                _calloutTimer = 0f;
                _callout.text = _shownCallout;
                _callout.color = director.CalloutColour;
            }

            _calloutTimer += Time.unscaledDeltaTime;
            float a = _calloutTimer < calloutHold
                ? Mathf.Clamp01(_calloutTimer / 0.09f)
                : Mathf.Clamp01(1f - (_calloutTimer - calloutHold) / 0.32f);
            _callout.alpha = a;
            // Punches in and settles. Same overshoot the round's callouts use, so the two modes
            // read as one game rather than as two HUDs.
            float pop = _calloutTimer < 0.22f ? 1f + (1f - _calloutTimer / 0.22f) * 0.28f : 1f;
            _callout.rectTransform.localScale = Vector3.one * pop;
        }

        void TickBoost()
        {
            if (_boostFill == null) return;
            var p = director.Player;
            float fuel = p != null && p.mower != null ? p.mower.BoostFuel : 0f;
            // Eased exactly as the round eases it, so a tank filling looks the same in both stages.
            _boostFill.fillAmount = Mathf.Lerp(_boostFill.fillAmount, fuel, Time.deltaTime * 8f);
        }

        /// <summary>
        /// The ending, in the order it is read.
        ///
        ///     OVERHEAD   the whole arena, percentages counting up on the bar
        ///     BENCH      the judges decide it — see TurfVerdict
        ///     CARD       the camera comes down onto the player's machine, card on the left
        ///     BOARD      the sweep to the scoreboard
        ///
        /// The card is tied to the director's own beat rather than to reveal progress, because it
        /// has to arrive with the camera and the camera waits for three animals to finish raising
        /// cards. Fading rather than switching: this shot is a continuous move off the bench and a
        /// card that pops on mid-blend reads as a cut that did not happen.
        /// </summary>
        void TickReveal(TurfMask mask)
        {
            bool revealing = director.State == TurfDirector.Phase.Reveal
                          || director.State == TurfDirector.Phase.Done;
            if (!revealing || mask == null)
            {
                if (_results != null) _results.alpha = 0f;
                _headline.alpha = 0f;
                return;
            }

            // The match's own readouts stand down as the ending takes over, and they go before the
            // card arrives rather than with it, so the frame is clear when the camera lands.
            if (_live != null)
                _live.alpha = Mathf.MoveTowards(_live.alpha, director.OnCard ? 0f : 1f,
                                                Time.deltaTime * 2.2f);

            // Up on the player's machine, down again once the camera leaves for the board.
            float want = director.OnCard && !director.OnBoard ? 1f : 0f;
            if (_results != null)
                _results.alpha = Mathf.MoveTowards(_results.alpha, want, Time.deltaTime * 1.8f);

            float p = director.RevealProgress;
            var standings = director.Standings;

            if (_results != null && _results.alpha > 0.001f && _place[0] != null)
            {
                for (int i = 0; i < TurfArena.Count; i++)
                {
                    bool used = i < standings.Count;
                    if (_row[i] != null && _row[i].gameObject.activeSelf != used)
                        _row[i].gameObject.SetActive(used);
                    if (!used) continue;

                    int slot = standings[i];
                    var c = director.CompetitorAt(slot);
                    var spec = TurfArena.Get(slot);

                    _place[i].text = (i + 1).ToString();
                    _who[i].text = c != null && c.isPlayer ? $"{spec.contestant}  (YOU)" : spec.contestant;
                    _sub[i].text = spec.species;
                    _pct[i].text = $"{mask.Share(slot) * 100f:0.0}%";
                    _stripe[i].color = spec.livery;
                    // The player's own row is the one being looked for, so it is the one set in
                    // the round's gold instead of its brown.
                    _who[i].color = c != null && c.isPlayer ? Gold : Nib;
                }

                int me = -1;
                for (int i = 0; i < standings.Count; i++)
                {
                    var c = director.CompetitorAt(standings[i]);
                    if (c != null && c.isPlayer) { me = i; break; }
                }
                string[] ordinal = { "1ST", "2ND", "3RD", "4TH" };
                _resultsRank.text = me >= 0 && me < ordinal.Length ? ordinal[me] : "";
                float mine = me >= 0 ? mask.Share(standings[me]) : 0f;
                _resultsTotal.text = $"{mine * 100f:0.0}% OF THE ARENA  ·  {mine * 30f:0.0} TO THE ROUND";
            }

            // The headline belongs to the overhead only. Once the bench has it, the card speaks.
            if (director.OnCard) { _headline.alpha = Mathf.MoveTowards(_headline.alpha, 0f, Time.deltaTime * 2.4f); return; }

            if (p < 0.72f) { _headline.alpha = 0f; return; }

            // The measurement, and then who it goes to. Not a verdict: this stage reports its
            // numbers and the panel rules on them along with the rest of the round, so the card
            // says the ground has been measured rather than that somebody has won.
            int winner = director.Winner;
            var w = director.CompetitorAt(winner);
            // A handover line, not a verdict. It says where the answer is coming from, and then
            // gets out of the way for the bench that gives it.
            _headline.text = director.DeadHeat ? "TOO CLOSE TO CALL" : "OVER TO THE JUDGES";
            _headline.color = winner >= 0 ? TurfArena.Livery(winner) : Color.white;
            _headline.alpha = Mathf.Clamp01((p - 0.72f) / 0.18f);
            _headline.rectTransform.anchoredPosition = new Vector2(0f, 168f);
            // Smaller than a winner's card would be. It is a result being handed over, not a
            // trophy being presented.
            _headline.fontSize = 42f;
        }

        // ------------------------------------------------------------------ the round's kit

        /// <summary>
        /// A child anchored purely by fraction of its parent — DuckUIBuilder.Frac, at runtime.
        ///
        /// Duplicated rather than shared because that one lives in an editor assembly and this HUD
        /// is built when the scene starts. Three lines of RectTransform is a cheaper duplication
        /// than making the result card a baked asset just to reach a helper.
        /// </summary>
        static RectTransform Frac(string name, Transform parent, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>One of the round's nine-sliced plates. A flat box if the sprite is missing.</summary>
        static Image Plate(RectTransform rt, Sprite sprite, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// Type set the way the round sets it: wrap decided before auto-sizing is asked for a size,
        /// then a dark outline and a drop shadow, because every glyph in this game is read over
        /// bright grass. The ordering is load-bearing — see the note on DuckUIBuilder.AddText.
        /// </summary>
        static TextMeshProUGUI CardText(RectTransform rt, string text, float size,
                                        TextAlignmentOptions align, Color color,
                                        float outline = 0.22f, bool wrap = true)
        {
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;

            t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Truncate;
            t.fontSize = size;
            t.enableAutoSizing = true;
            t.fontSizeMax = size;
            t.fontSizeMin = Mathf.Max(9f, size * 0.4f);
            t.margin = new Vector4(10f, 6f, 10f, 6f);

            if (t.font == null) t.font = TMP_Settings.defaultFontAsset;
            if (t.fontSharedMaterial == null) { t.characterSpacing = 4f; return t; }

            t.fontMaterial = new Material(t.fontSharedMaterial);
            t.fontMaterial.EnableKeyword("OUTLINE_ON");
            t.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, outline);
            t.fontMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.09f, 0.16f, 0.10f, 1f));
            t.fontMaterial.EnableKeyword("UNDERLAY_ON");
            t.fontMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.45f));
            t.fontMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.4f);
            t.fontMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.5f);
            t.fontMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.22f);
            t.characterSpacing = 4f;
            return t;
        }

        // ------------------------------------------------------------------ widgets

        Image Box(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return img;
        }

        TextMeshProUGUI Text(string name, float size, Vector2 anchor, Vector2 pos, Vector2 rect,
                             TextAlignmentOptions align, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent != null ? parent : _canvas.transform, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.alignment = align;
            t.raycastTarget = false;
            t.color = Color.white;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = rect;
            return t;
        }
    }
}
