using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DuckMow
{
    /// <summary>
    /// What the player is told during the rally. Binds a baked canvas; builds nothing.
    ///
    /// It used to build its own, out of bare white rectangles, and that is why it looked like a
    /// different game from the round it sits inside. The mowing HUD is BAKED by
    /// <c>DuckUIBuilder</c> out of painted cards, ribbons, rings and rosettes — so a HUD that
    /// assembles untextured boxes at runtime cannot match it however carefully its colours are
    /// copied. The kit is the design.
    ///
    /// So this is now the same shape as <see cref="HUD"/>: the builder assembles the canvas from the
    /// same sprite set and drops the references in, and this drives them. Everything about how it
    /// LOOKS lives in DuckRallyBuilder.BuildHud, where it can be dragged and looked at without
    /// pressing play — which is the project's rule for everything else in the arena.
    ///
    /// What is deliberately NOT here:
    ///
    ///   - No big centre callout. It printed "GOOSE DOWN!" across the middle of the screen, which is
    ///     where the arena is, at the moment something worth watching was happening in it. The
    ///     callout is staged in the world now — see RallyWorldFX.Burst — where it gains the thing the
    ///     text never had: a position, so it says WHICH corner.
    ///
    ///   - No minimap. Four flat squares carrying no goose positions, no damage and no score, with a
    ///     marker in the wrong quadrant and two names drawn on top of each other.
    ///
    /// What replaced the minimap is the thing the mode could not previously answer: ALL FOUR gardens
    /// on cards, so "am I winning" is readable at a glance. In a free-for-all whose whole objective
    /// is "keep the most garden", a HUD showing only your own number is not a scoreboard.
    /// </summary>
    [DefaultExecutionOrder(30)]
    public class RallyHud : MonoBehaviour
    {
        [Header("Sources")]
        public RallyDirector director;
        [Tooltip("Camera the off-screen arrows are computed against. The main one if left empty.")]
        public Camera view;
        [Tooltip("The thing that knows whether a round is waiting on this stage, and therefore what " +
                 "the way out says. Found in the scene if left empty.")]
        public RallyBootstrap stage;

        [Header("Clock")]
        public TextMeshProUGUI timerText;
        public Image timerRing;

        /// <summary>One contestant's card. Four of these, baked in arena slot order.</summary>
        [System.Serializable]
        public class Card
        {
            public RectTransform root;
            public Image plate;
            public Image fill;
            public Image alarmGlow;
            public TextMeshProUGUI name;
            public TextMeshProUGUI percent;
            [HideInInspector] public float shown = 1f;
            [HideInInspector] public float punch;
            [HideInInspector] public int lastPercent = 100;
            [Tooltip("The horn meter down the left edge of the card. Fills as the horn recharges.")]
            public Image hornMeter;
            [HideInInspector] public bool hornWasReady = true;
        }
        public Card[] cards = new Card[0];

        [Header("Ticker")]
        [Tooltip("The small line under the clock, for the words a world burst cannot carry — " +
                 "'THREE GEESE!' and 'TIME!' have no position in the arena to burst at.")]
        public CanvasGroup tickerGroup;
        public TextMeshProUGUI tickerText;

        [Header("Result")]
        public CanvasGroup resultGroup;
        public TextMeshProUGUI resultPlacing;
        public TextMeshProUGUI resultDetail;
        [Tooltip("The points this rally is worth, slammed in last. The payoff line.")]
        public TextMeshProUGUI resultAward;
        public Image resultRosette;
        public Sprite[] rosetteByPlace = new Sprite[0];    // 1st..4th

        [Header("The way out")]
        [Tooltip("The line offering SPACE once the bench has finished. Left empty it is built at " +
                 "runtime just above the result card — see EnsureExitPrompt, which explains why " +
                 "the one piece of this HUD that is not baked is not baked.")]
        public CanvasGroup exitGroup;
        public TextMeshProUGUI exitPrompt;

        [Header("Threat arrows")]
        [Tooltip("Graphic rather than Image so a chevron can be a glyph — the UI kit has cards, " +
                 "ribbons, rings and rosettes but no arrow, and TextMeshPro already has one.")]
        public Graphic[] arrows = new Graphic[0];
        [Tooltip("Fraction of the half-screen the arrows sit in from the edge.")]
        [Range(0.6f, 0.98f)] public float arrowInset = 0.84f;

        [Header("Feel")]
        public Color timerNormal = new Color(1f, 0.97f, 0.88f);
        public Color timerLow = new Color(1f, 0.42f, 0.32f);
        public Color gold = new Color(1f, 0.85f, 0.45f);

        readonly List<RallyGoose> _threats = new(4);
        string _shownCallout = "";
        float _tickerTimer = 99f;
        float _clock;
        bool _resultFilled;

        void Awake()
        {
            if (director == null) director = FindFirstObjectByType<RallyDirector>();
            if (stage == null) stage = FindFirstObjectByType<RallyBootstrap>();
            if (resultGroup != null) resultGroup.alpha = 0f;
            if (tickerGroup != null) tickerGroup.alpha = 0f;
            EnsureExitPrompt();
            if (exitGroup != null) exitGroup.alpha = 0f;
        }

        /// <summary>
        /// Build the way-out line if the scene has not got one, just above the result card.
        ///
        /// ---- why this one thing is built at runtime in a HUD that is otherwise baked ----
        ///
        /// It should be baked, and the fields above are here so that it can be: point them at a
        /// rect in DuckRallyBuilder.BuildResult and this method does nothing. The class note at the
        /// top of this file is right and is not being argued with — a canvas that assembles bare
        /// rectangles at runtime cannot match one made of painted cards, and the kit IS the design.
        ///
        /// But a baked canvas only reaches the player after somebody re-runs the builder and saves
        /// the scene, and this is a line of TEXT on a plate that already exists two centimetres
        /// below it. Shipping it as a builder change alone would mean a GooseRally.unity in which
        /// the standalone player is still stranded at the results with no way off — the exact fault
        /// this is fixing — until an unrelated menu item happens to be clicked. So it stands itself
        /// up, once, and steps aside the moment the scene provides a better one.
        ///
        /// It is deliberately the plainest thing it could be: no plate, no sprite, no attempt at a
        /// card. A bare untextured box next to the painted result panel is precisely the mismatch
        /// this HUD was rebuilt to stop, whereas type on its own — set in the same cream, the same
        /// bold capitals, the same outline and drop shadow every other glyph in the arena carries —
        /// belongs to the same game as the panel beneath it.
        /// </summary>
        void EnsureExitPrompt()
        {
            if (exitPrompt != null)
            {
                // Spelt out rather than written with ??, which does not go through Unity's
                // overloaded null operator and would happily hand back a destroyed component.
                if (exitGroup == null) exitGroup = exitPrompt.GetComponent<CanvasGroup>();
                if (exitGroup == null) exitGroup = exitPrompt.gameObject.AddComponent<CanvasGroup>();
                return;
            }

            // The canvas is reached through a BAKED widget rather than through this component's own
            // transform, and that is load bearing. DuckRallyBuilder puts the RallyHud on the
            // "~ Systems" object and creates "~ Rally HUD" as a separate scene root, so a walk up
            // from here finds no canvas at all. The result panel is on the one we want, by
            // construction — it is the thing this prompt sits above.
            var anchorTo = resultGroup != null ? (RectTransform)resultGroup.transform : null;
            var canvas = anchorTo != null ? anchorTo.GetComponentInParent<Canvas>() : null;
            if (canvas == null)
            {
                Debug.LogWarning("[Rally] the HUD has no result panel to hang the way-out prompt " +
                                 "off, so the arena will end without one. Rebuild the scene with " +
                                 "'Duck/3 · Build goose rally scene'.");
                return;
            }

            var go = new GameObject("Exit prompt", typeof(RectTransform), typeof(CanvasGroup));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas.transform, false);
            // Directly over the result card, which occupies 0.045 -> 0.235 of the frame. Centred on
            // the same column so the two read as one block rather than as two notices.
            rt.anchorMin = new Vector2(0.18f, 0.245f);
            rt.anchorMax = new Vector2(0.82f, 0.315f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            exitGroup = go.GetComponent<CanvasGroup>();
            exitGroup.interactable = exitGroup.blocksRaycasts = false;
            exitPrompt = PromptText(rt);
        }

        /// <summary>
        /// Type set the way DuckUIBuilder.AddText sets it — wrap decided before auto-sizing is asked
        /// for a size, then a dark outline and a drop shadow, because every glyph in this game is
        /// read over bright grass or a lit arena floor.
        ///
        /// Written out here rather than called, because that helper lives in an editor assembly and
        /// this runs when the scene starts. The ordering inside it is load bearing and is copied
        /// rather than approximated; see the note on AddText for what goes wrong when it is not.
        /// </summary>
        static TextMeshProUGUI PromptText(RectTransform rt)
        {
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = "";
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color(1f, 0.97f, 0.88f);
            t.raycastTarget = false;
            t.fontStyle = FontStyles.Bold;

            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Truncate;
            t.fontSize = 26f;
            t.enableAutoSizing = true;
            t.fontSizeMax = 26f;
            t.fontSizeMin = 11f;
            t.margin = new Vector4(10f, 4f, 10f, 4f);

            if (t.font == null) t.font = TMP_Settings.defaultFontAsset;
            if (t.fontSharedMaterial == null) { t.characterSpacing = 4f; return t; }

            t.fontMaterial = new Material(t.fontSharedMaterial);
            t.fontMaterial.EnableKeyword("OUTLINE_ON");
            t.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.26f);
            t.fontMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.09f, 0.16f, 0.10f, 1f));
            t.fontMaterial.EnableKeyword("UNDERLAY_ON");
            t.fontMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.55f));
            t.fontMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.4f);
            t.fontMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.5f);
            t.fontMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.24f);
            t.characterSpacing = 4f;
            return t;
        }

        void LateUpdate()
        {
            if (director == null) return;
            if (view == null) view = Camera.main;

            float dt = Time.unscaledDeltaTime;
            _clock += dt;

            UpdateClock();
            UpdateCards(dt);
            UpdateTicker(dt);
            UpdateResult(dt);
            UpdateExitPrompt(dt);
            UpdateArrows();
        }

        /// <summary>
        /// The way out, once there is one.
        ///
        /// Every decision about WHEN belongs to <see cref="RallyBootstrap"/> and none of it is
        /// second-guessed here — this reads one bool and draws it. That is worth saying out loud
        /// because the tempting version is to test the director's phase from the HUD, which would
        /// put the rule "the prompt is up once the bench has finished" in two files free to
        /// disagree, and the disagreement would be a prompt offering a key that does nothing. The
        /// bootstrap is also the only one of the two that can see the verdict at all.
        ///
        /// The TEXT is re-read every frame rather than set once, for a duller reason: this
        /// component wakes before the bootstrap has asked <see cref="RallyHandoff.Active"/> whether
        /// a round is waiting, so anything latched at construction would be latched from the wrong
        /// answer.
        ///
        /// It breathes. A prompt at rest on the stillest frame in the stage — a finished bench with
        /// nothing moving on it — is furniture; one that swells on a slow beat is something asking
        /// to be pressed. Four percent, on the unscaled clock, so it keeps breathing through a hit
        /// stop and is noticed rather than watched.
        /// </summary>
        void UpdateExitPrompt(float dt)
        {
            if (exitGroup == null || exitPrompt == null) return;

            bool up = stage != null && stage.ExitPromptUp;
            exitGroup.alpha = Mathf.MoveTowards(exitGroup.alpha, up ? 1f : 0f, dt * 3f);
            if (exitGroup.alpha <= 0.001f) return;

            if (up) exitPrompt.text = stage.ExitPrompt;
            exitGroup.transform.localScale = Vector3.one * (1f + Mathf.Sin(_clock * 2.6f) * 0.04f);
        }

        void UpdateClock()
        {
            if (timerText == null) return;
            float t = Mathf.Max(0f, director.TimeRemaining);
            int secs = Mathf.CeilToInt(t);
            timerText.text = $"{secs / 60:0}:{secs % 60:00}";

            bool low = t <= 12f && director.State == RallyDirector.Phase.Live;
            float pulse = low ? (Mathf.Sin(_clock * 4.5f) * 0.5f + 0.5f) : 0f;
            timerText.color = Color.Lerp(timerNormal, timerLow, pulse);
            timerText.transform.localScale = Vector3.one * (1f + pulse * 0.07f);

            if (timerRing != null)
                timerRing.fillAmount = director.matchSeconds > 0f
                    ? Mathf.Clamp01(director.TimeRemaining / director.matchSeconds) : 0f;
        }

        /// <summary>
        /// The four gardens.
        ///
        /// The bar eases toward the truth rather than snapping, and the card PUNCHES on the frame the
        /// percentage actually changes — which is the only moment the player needs to look at it.
        /// A gauge that slides quietly is a gauge nobody notices moving.
        /// </summary>
        void UpdateCards(float dt)
        {
            for (int i = 0; i < cards.Length && i < RallyArena.Count; i++)
            {
                var card = cards[i];
                if (card == null || card.root == null) continue;

                var c = director.CompetitorAt(i);
                float integrity = c != null ? c.Integrity : 1f;
                int pct = Mathf.RoundToInt(integrity * 100f);

                if (pct != card.lastPercent)
                {
                    card.lastPercent = pct;
                    card.punch = 1f;
                }
                card.punch = Mathf.MoveTowards(card.punch, 0f, dt * 3.2f);
                card.root.localScale = Vector3.one * (1f + card.punch * 0.10f);

                card.shown = Mathf.MoveTowards(card.shown, integrity, dt * 0.6f);
                if (card.fill != null) card.fill.fillAmount = Mathf.Clamp01(card.shown);

                if (card.percent != null)
                {
                    card.percent.text = $"{pct}";
                    card.percent.color = pct <= 34 ? timerLow : pct <= 67 ? gold : timerNormal;
                }

                // The horn meter.
                //
                // On every card, not only the player's, because every competitor has the horn now
                // and a cooldown you can see on a rival is information you can play against —
                // driving at somebody who has just spent theirs is a different decision from
                // driving at somebody holding it. Dim while charging, full livery when it is ready,
                // and it flashes white on the frame it comes back so the player is told rather than
                // having to watch a bar.
                if (card.hornMeter != null)
                {
                    float charge = director.HornCharge(i);
                    card.hornMeter.fillAmount = charge;
                    bool ready = charge >= 0.999f;
                    var lit = c != null ? c.Livery : Color.white;
                    card.hornMeter.color = ready
                        ? Color.Lerp(lit, Color.white, card.hornWasReady ? 0.25f : 1f)
                        : new Color(lit.r, lit.g, lit.b, 0.38f);
                    card.hornWasReady = ready;
                }

                // The card lights while this quadrant is under attack, so the board and the world
                // agree about who is in trouble without the player having to find the goose first.
                if (card.alarmGlow != null)
                {
                    var garden = director.GardenAt(i);
                    float alarm = garden != null ? garden.AlarmLevel : 0f;
                    var col = card.alarmGlow.color;
                    col.a = alarm * 0.85f;
                    card.alarmGlow.color = col;
                }
            }
        }

        void UpdateTicker(float dt)
        {
            if (tickerText == null || tickerGroup == null) return;

            if (director.Callout != _shownCallout && director.CalloutAge < 0.2f)
            {
                _shownCallout = director.Callout;
                _tickerTimer = 0f;
                tickerText.text = _shownCallout;
                tickerText.color = director.CalloutColour;
            }

            _tickerTimer += dt;
            tickerGroup.alpha = Mathf.Clamp01(1.6f - _tickerTimer);
            // Lands rather than fades up. A word that eases in reads as a notification; one that
            // arrives at size reads as a call.
            float pop = 1f + Mathf.Max(0f, 0.28f - _tickerTimer) * 1.6f;
            tickerText.transform.localScale = Vector3.one * pop;
        }

        float _resultAge;
        int _shownIntact, _shownParried, _shownKo, _shownAward;
        int _wantIntact, _wantParried, _wantKo, _wantAward;
        int _beat = -1;

        /// <summary>
        /// Where the result card's colours and the crowd's reaction change, on the round's own scale
        /// of thirty.
        ///
        /// Derived rather than typed. These were 6 and 0 on the bench's old -6..+10 award, and the
        /// two figures are carried across to the same points on the new scale so the card reacts to
        /// exactly the rallies it always reacted to — GoodMark is what the old +6 converts to, and
        /// ParMark is what a break-even nought converts to.
        /// </summary>
        static readonly int GoodMark =
            Mathf.RoundToInt(Mathf.InverseLerp(Tournament.AwardFloor, Tournament.AwardCeiling, 6f)
                             * Championship.RivalRoundMax);
        static readonly int ParMark =
            Mathf.RoundToInt(Mathf.InverseLerp(Tournament.AwardFloor, Tournament.AwardCeiling, 0f)
                             * Championship.RivalRoundMax);

        /// <summary>
        /// The result, delivered in beats rather than printed.
        ///
        /// It used to appear complete and static — a placing and one line reading "87% INTACT
        /// 1 PARRIED 1 KNOCKED OUT" — which states the outcome and celebrates none of it. A card
        /// that arrives finished has nothing for the player to watch, and the end of a match is
        /// exactly when they are sitting still with nothing to do.
        ///
        /// So the numbers COUNT, one line at a time, each landing with a punch and a tick, and the
        /// points the rally is worth slam in last with a burst behind them. Same information, four
        /// seconds later, and the four seconds are the reward.
        /// </summary>
        void UpdateResult(float dt)
        {
            if (resultGroup == null) return;
            bool over = director.State == RallyDirector.Phase.Settle ||
                        director.State == RallyDirector.Phase.Done;
            resultGroup.alpha = Mathf.MoveTowards(resultGroup.alpha, over ? 1f : 0f, dt * 2.2f);

            if (over && _resultFilled)
            {
                _resultAge += dt;
                TickResultCount(dt);
                return;
            }
            if (!over || _resultFilled) return;
            _resultFilled = true;

            // Ranked off the live competitors rather than the handoff, so the card is right even in a
            // standalone review run where nothing is waiting to receive the results.
            var order = new List<RallyCompetitor>(4);
            for (int i = 0; i < RallyArena.Count; i++)
            {
                var c = director.CompetitorAt(i);
                if (c != null) order.Add(c);
            }
            order.Sort((a, b) => b.Integrity.CompareTo(a.Integrity));

            int place = 1;
            for (int i = 0; i < order.Count; i++)
                if (order[i].isPlayer) { place = i + 1; break; }

            if (resultPlacing != null)
            {
                resultPlacing.text = place == 1 ? "GARDEN SAVED" : $"{Championship.Ordinal(place)} OF {order.Count}";
                resultPlacing.color = place == 1 ? gold : timerNormal;
            }

            var me = director.Player;
            _wantIntact = me != null ? Mathf.RoundToInt(me.Integrity * 100f) : 0;
            _wantParried = me != null ? me.Parries : 0;
            _wantKo = me != null ? me.Knockouts : 0;
            // THE ROUND'S MARKS, not the bench's award.
            //
            // This printed "+7" — RallyAwardForPlace, on its own -6..+10 scale — back when the rally
            // was a beat hung off a mowing round and that award was added to the picture's thirty. A
            // rally is a whole ROUND now and is marked out of thirty like every other round, so the
            // card was about to hand the player a number that the board behind the bench would then
            // contradict thirty seconds later. Same curve, converted by the same static the
            // championship banks through — see Tournament.RallyMarks.
            _wantAward = me != null
                ? Mathf.RoundToInt(Tournament.RallyMarks(new RallyHandoff.Result
                {
                    integrity = me.Integrity,
                    knockouts = me.Knockouts,
                    landed = me.Landed,
                    perfects = me.Perfects
                }, place, Mathf.Max(order.Count, 1)))
                : 0;

            _shownIntact = _shownParried = _shownKo = _shownAward = 0;
            _resultAge = 0f;
            _beat = -1;
            if (resultDetail != null) resultDetail.text = "";
            if (resultAward != null) { resultAward.text = ""; resultAward.alpha = 0f; }

            if (resultRosette != null)
            {
                int idx = Mathf.Clamp(place - 1, 0, Mathf.Max(rosetteByPlace.Length - 1, 0));
                bool have = rosetteByPlace != null && idx < rosetteByPlace.Length && rosetteByPlace[idx] != null;
                if (have) resultRosette.sprite = rosetteByPlace[idx];
                resultRosette.enabled = have;
            }
        }

        /// <summary>
        /// Run the count-up. Four beats, each one landing before the next starts.
        /// </summary>
        void TickResultCount(float dt)
        {
            // Which beat we are on. The card holds a moment before the first number moves, so the
            // placing gets to land on its own rather than being stepped on.
            int beat = Mathf.Clamp(Mathf.FloorToInt((_resultAge - 0.55f) / 0.75f), -1, 3);
            if (beat != _beat)
            {
                _beat = beat;
                if (beat >= 0)
                {
                    AudioDirector.Instance?.PlayOne(AudioDirector.Instance.scoreTick, 0.55f);
                    if (resultDetail != null) resultDetail.transform.localScale = Vector3.one * 1.14f;
                }
                if (beat == 3)
                {
                    // The payout: the loudest thing on the card, and the last.
                    AudioDirector.Instance?.PlayOne(AudioDirector.Instance.stamp, 0.8f);
                    // GoodMark, not a bare 6 — the number this card carries is out of thirty now.
                    AudioDirector.Instance?.CrowdCheer(_wantAward >= GoodMark ? 0.9f : 0.45f,
                                                       applaud: _wantAward >= GoodMark);
                    if (resultAward != null) resultAward.transform.localScale = Vector3.one * 1.6f;
                    var me = director.Player;
                    if (me != null)
                        RallyWorldFX.Instance?.Burst(me.Position + Vector3.up * 2.2f, gold, 1.4f, 10);
                }
            }

            // Numbers roll rather than appear. Rolling is what makes a total feel counted.
            if (beat >= 0) _shownIntact = Roll(_shownIntact, _wantIntact, dt, 140f);
            if (beat >= 1) _shownParried = Roll(_shownParried, _wantParried, dt, 14f);
            if (beat >= 2) _shownKo = Roll(_shownKo, _wantKo, dt, 8f);
            if (beat >= 3) _shownAward = Roll(_shownAward, _wantAward, dt, 12f);

            if (resultDetail != null)
            {
                string line = "";
                if (beat >= 0) line += $"{_shownIntact}% INTACT";
                if (beat >= 1) line += $"     {_shownParried} PARRIED";
                if (beat >= 2) line += $"     {_shownKo} KNOCKED OUT";
                resultDetail.text = line;
                resultDetail.transform.localScale =
                    Vector3.Lerp(resultDetail.transform.localScale, Vector3.one, dt * 8f);
            }

            if (resultAward != null && beat >= 3)
            {
                // "21 / 30", the same shape the venue's board and the results card print. It read
                // "+7" while the number was an award added onto a picture; there is no picture in
                // this round to add it to, so the plus sign was claiming a sum that does not exist.
                resultAward.text = $"{_shownAward} / {Championship.RivalRoundMax}";
                resultAward.color = _shownAward >= GoodMark ? gold
                                  : _shownAward >= ParMark ? timerNormal : timerLow;
                resultAward.alpha = Mathf.MoveTowards(resultAward.alpha, 1f, dt * 5f);
                resultAward.transform.localScale =
                    Vector3.Lerp(resultAward.transform.localScale, Vector3.one, dt * 7f);
            }
        }

        static int Roll(int shown, int want, float dt, float perSecond)
        {
            if (shown == want) return want;
            int step = Mathf.Max(1, Mathf.CeilToInt(perSecond * dt));
            return shown < want ? Mathf.Min(want, shown + step) : Mathf.Max(want, shown - step);
        }

        /// <summary>
        /// Chevrons at the edge of frame for geese coming at the player that are not on screen.
        ///
        /// The reason the camera is allowed to stay on the mower. A threat the player cannot see is
        /// only unfair if they were not told about it, and an arrow tells them where without taking
        /// the frame away from what they are steering.
        /// </summary>
        void UpdateArrows()
        {
            foreach (var a in arrows) if (a != null) a.enabled = false;
            if (view == null || director.Player == null || arrows.Length == 0) return;

            director.ThreatsTo(director.Player.slot, _threats);
            int n = 0;

            foreach (var g in _threats)
            {
                if (n >= arrows.Length) break;
                var arrow = arrows[n];
                if (arrow == null) { n++; continue; }

                Vector3 sp = view.WorldToViewportPoint(g.transform.position);
                bool onScreen = sp.z > 0f && sp.x > 0.05f && sp.x < 0.95f && sp.y > 0.05f && sp.y < 0.95f;
                if (onScreen) continue;

                // Behind the camera comes back with a viewport point mirrored through the origin,
                // which points the arrow at exactly the wrong edge. Flip it before it is used.
                Vector2 v = new Vector2(sp.x - 0.5f, sp.y - 0.5f);
                if (sp.z < 0f) v = -v;
                if (v.sqrMagnitude < 1e-5f) v = Vector2.up;
                v.Normalize();

                n++;
                arrow.enabled = true;
                var rt = arrow.rectTransform;
                rt.anchoredPosition = new Vector2(v.x * 900f, v.y * 460f) * arrowInset;
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg - 90f);

                float urgency = Mathf.Clamp01(1f - Vector3.Distance(g.transform.position,
                                                                    director.Player.Position) / 28f);
                arrow.color = Color.Lerp(gold, timerLow, urgency);
                rt.localScale = Vector3.one * (0.85f + urgency * 0.45f
                                + Mathf.PingPong(_clock * (2f + urgency * 5f), 1f) * 0.12f);
            }
        }
    }
}
