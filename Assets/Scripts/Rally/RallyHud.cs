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
            if (resultGroup != null) resultGroup.alpha = 0f;
            if (tickerGroup != null) tickerGroup.alpha = 0f;
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
            UpdateArrows();
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
            _wantAward = me != null
                ? Tournament.RallyAwardForPlace(new RallyHandoff.Result
                {
                    integrity = me.Integrity,
                    knockouts = me.Knockouts,
                    landed = me.Landed,
                    perfects = me.Perfects
                }, place, Mathf.Max(order.Count, 1))
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
                    AudioDirector.Instance?.CrowdCheer(_wantAward >= 6 ? 0.9f : 0.45f, applaud: _wantAward >= 6);
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
                resultAward.text = _shownAward >= 0 ? $"+{_shownAward}" : _shownAward.ToString();
                resultAward.color = _shownAward >= 6 ? gold : _shownAward >= 0 ? timerNormal : timerLow;
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
