using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DuckMow
{
    /// <summary>
    /// The in-round HUD and the results screen.
    ///
    /// Everything here fights the same problem: the game is played over a bright, busy, saturated
    /// lawn, so every element needs its own dark backing or outline or it disappears.
    ///
    /// What is deliberately NOT here matters more than what is. The round is built on the ground
    /// guide dissolving a third of the way in and the player finishing from memory, so anything on
    /// screen that answers "am I in the right place" undoes the whole thing:
    ///
    ///   - The corner minimap is gone. It drew the target outline, the cut mask, the spill and the
    ///     mower's position together, which is the complete answer key.
    ///   - The "picture filled" percentage is gone. It was worse than the map: it measured cut
    ///     cells against the hidden target, so the shape could be brute-forced by driving around
    ///     watching the number climb.
    ///   - The "MOWN 81 m²" readout that replaced it is gone too, for a different reason: it was
    ///     unreadable as information. A player cannot tell whether eighty-one square metres is good,
    ///     so the number confirmed only that the engine was running. It existed to fill the hole the
    ///     percentage left, which is not a reason for anything to be on a HUD.
    ///
    /// What is left is the clock, the shape you were asked for as a static card, the boost gauge, and
    /// whether the one aerial check is still in hand. Anything proposed for this screen has to beat
    /// that list rather than merely fit beside it.
    /// </summary>
    [DefaultExecutionOrder(30)]
    public class HUD : MonoBehaviour
    {
        [Header("Sources")]
        public GameDirector director;
        public MowerController mower;
        public CutMask cutMask;
        public RoundTarget target;
        public JudgePanel judges;

        [Header("Round")]
        public CanvasGroup roundGroup;
        public TextMeshProUGUI timerText;
        public Image timerRing;
        public Image boostFill;
        [Tooltip("The picture asked for, drawn as ink on paper. Static: no position, no progress.")]
        public RawImage shapeCard;

        [Header("Aerial check")]
        [Tooltip("Lit while the one-per-round lift is still available, spent once it is used.")]
        public Image aerialToken;
        public TextMeshProUGUI aerialHint;
        public Color aerialReady = new Color(1f, 0.86f, 0.44f);
        public Color aerialSpent = new Color(0.42f, 0.38f, 0.32f);

        [Header("Announcements")]
        public CanvasGroup bannerGroup;
        public TextMeshProUGUI bannerTitle;
        public TextMeshProUGUI bannerSubtitle;
        [Tooltip("What this round is worth, on a plate under the ribbon. Empty on every banner but " +
                 "the briefing, and it leaves when the banner does.")]
        public TextMeshProUGUI bannerGoal;
        [Tooltip("Hides the goal plate on the banners that have no goal line to carry.")]
        public CanvasGroup bannerGoalGroup;
        public TextMeshProUGUI countdownText;

        [Header("Results")]
        /// <summary>Set by VictoryCeremony to keep the round card up through the champion beat.</summary>
        [System.NonSerialized] public bool ceremonyResultsHold;
        public CanvasGroup resultsGroup;
        public TextMeshProUGUI resultsRank;
        public TextMeshProUGUI resultsTotal;
        public Image resultsRosette;
        public Sprite[] rosetteByRank;      // S, A, B, C, D
        public TextMeshProUGUI[] judgeNames = new TextMeshProUGUI[3];
        public TextMeshProUGUI[] judgeScores = new TextMeshProUGUI[3];
        public TextMeshProUGUI[] judgeQuips = new TextMeshProUGUI[3];
        public TextMeshProUGUI retryHint;

        [Header("Breakdown")]
        public TextMeshProUGUI coverageStat, spillStat, edgeStat, styleStat;

        [Header("Venue tour")]
        public CanvasGroup tourGroup;
        public TextMeshProUGUI tourName;
        public TextMeshProUGUI tourSubtitle;
        public TextMeshProUGUI tourScore;
        public TextMeshProUGUI tourGrade;
        [Tooltip("Face of whoever is being shown. Rendered from the real model at startup.")]
        public RawImage tourPortrait;
        [Tooltip("Keeps that face the shape it was rendered at.\n\n" +
                 "A RawImage has no preserveAspect — that is on Image — so without this it is drawn " +
                 "at whatever shape its rect happens to be. Its ratio is written from the TEXTURE " +
                 "every time one is assigned, so re-rendering the portraits at a different size " +
                 "needs no change here.")]
        public AspectRatioFitter tourPortraitFit;

        [Header("Outro")]
        public CanvasGroup outroGroup;
        public TextMeshProUGUI outroPlacing;
        public TextMeshProUGUI outroPrompt;

        // There is deliberately NO championship standing card here, and no round counter anywhere on
        // this HUD. There was one — a corner panel carrying "ROUND 2 OF 3", the points table and the
        // placing needed to take the title — and it was cut after a playthrough: it read as chrome
        // rather than as a goal, and the player looking at it still had to ask what the format was.
        //
        // The championship says its piece in exactly two places, both of them transient and both of
        // them beats the player is already reading: the goal plate on the briefing banner, which
        // leaves when the banner does, and the ceremony card below. Nothing about it is ever on screen
        // while the player is mowing.

        [Header("Ceremony")]
        public CanvasGroup ceremonyGroup;
        public TextMeshProUGUI ceremonyTitle;
        public TextMeshProUGUI ceremonySubtitle;
        public TextMeshProUGUI ceremonyPrompt;
        [Tooltip("Shown only to a champion. A rosette on a card that says you came third is a joke " +
                 "at the player's expense.")]
        public Image ceremonyRosette;

        [Header("Feel")]
        public Color timerNormal = new Color(1f, 0.97f, 0.88f);
        public Color timerLow = new Color(1f, 0.42f, 0.32f);
        public float lowTimePulseSpeed = 4.5f;

        float _clock;
        float _bannerTimer;
        float _shownTotal;

        /// <summary>
        /// The plate in the top corner that opens the pause board. Built at runtime rather than
        /// baked, and stepped from <see cref="Update"/>. See <see cref="DuckMow.UI.PauseButton"/>.
        /// </summary>
        DuckMow.UI.PauseButton _pause;

        void Awake()
        {
            // The shape card needs no material instancing and no per-frame work: it reads the
            // target distance field straight from the shader globals and never changes during a
            // round. That is the entire point of it — it is a printed reference, not a display.
            SetGroup(resultsGroup, 0f);
            SetGroup(bannerGroup, 0f);

            // The top-right corner, the same corner as the other two stages, with no position
            // argument. This HUD used to pass one: the shape card was parked in that corner at
            // 0.815..0.985 of the frame and the plate was pushed inboard to 0.795 to pass beside it,
            // which left the only pressable thing on the screen floating in the middle of the top
            // edge with an empty corner next to it. BuildRoundHud brings the card DOWN instead and
            // clears PauseButton.ReservedTop below the top edge for the plate.
            //
            // This component IS on the canvas object — DuckUIBuilder does canvasGO.AddComponent<HUD>
            // — so there is no walk up a hierarchy to get wrong here.
            _pause = DuckMow.UI.PauseButton.Attach(GetComponent<Canvas>());
        }

        void Start()
        {
            if (director != null)
            {
                director.OnStateChanged += HandleState;
                director.OnCountdownTick += HandleCountdown;
                director.OnContestantRevealed += ShowContestant;
                director.OnGuideLost += HandleGuideLost;
            }
            if (judges != null)
            {
                judges.OnJudgeScored += HandleJudgeScored;
                judges.OnVerdict += HandleVerdict;
            }
            ClearResults();
        }

        void Update()
        {
            // Stepped OUTSIDE the scripted-clock gate below and outside Tick, deliberately. Tick is
            // the HUD's own beat and the capture harness drives it by hand with its own delta; the
            // pause plate is chrome that answers a live pointer on the unscaled clock, and it hides
            // itself under SimClock.Scripted rather than being stepped by it. Feeding it the
            // harness's delta would put a pause button in every frame sheet the review produces.
            _pause?.Tick();

            if (SimClock.Scripted) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            _clock += dt;
            UpdateRoundHud(dt);
            UpdateBanner(dt);
            UpdateCountdown(dt);
            UpdateTourCard(dt);
            UpdateOutro(dt);
            UpdateAerialToken(dt);
            UpdateCeremonyCard(dt);
        }

        // ------------------------------------------------------------------ round HUD

        void UpdateRoundHud(float dt)
        {
            if (director == null) return;

            // The shape card is up through the preview as well, so the player learns from the first
            // round that the card is the thing that stays when the ground guide does not.
            bool showRound = director.State == GameState.Preview ||
                             director.State == GameState.Countdown ||
                             director.State == GameState.Mowing ||
                             director.State == GameState.Klaxon;
            if (roundGroup != null)
                roundGroup.alpha = Mathf.MoveTowards(roundGroup.alpha, showRound ? 1f : 0f, dt * 3f);

            if (timerText != null)
            {
                int secs = Mathf.CeilToInt(Mathf.Max(director.TimeRemaining, 0f));
                timerText.text = $"{secs / 60:0}:{secs % 60:00}";

                bool low = director.IsLowTime;
                float pulse = low ? (Mathf.Sin(_clock * lowTimePulseSpeed) * 0.5f + 0.5f) : 0f;
                timerText.color = Color.Lerp(timerNormal, timerLow, pulse);
                timerText.transform.localScale = Vector3.one * (1f + pulse * 0.07f);
            }

            if (timerRing != null) timerRing.fillAmount = director.TimeFraction;

            if (boostFill != null && mower != null)
                boostFill.fillAmount = Mathf.Lerp(boostFill.fillAmount, mower.BoostFuel, dt * 8f);
        }

        // The per-frame area count that fed the old "MOWN 81 m²" readout has gone with it. It sampled
        // every seventh cell of the cut mask on every frame of every round to produce a figure nobody
        // could act on; leaving it behind would have been a calculation running for no reader.

        /// <summary>
        /// The one-per-round aerial check, shown as a token that is either in hand or spent.
        ///
        /// Shown as an object rather than a number because there is only ever one of them: "1"
        /// reads as a counter the player expects to refill, a single token reads as something you
        /// are holding and will lose.
        /// </summary>
        void UpdateAerialToken(float dt)
        {
            if (director == null) return;

            bool inRound = director.State == GameState.Mowing;
            bool available = director.AerialChecksRemaining > 0;

            if (aerialToken != null)
            {
                Color want = available ? aerialReady : aerialSpent;
                want.a = inRound ? 1f : 0f;
                aerialToken.color = Color.Lerp(aerialToken.color, want, dt * 6f);

                // A slow pulse while it is still in hand and the guide has gone, which is exactly
                // the moment the player has forgotten they still have it.
                float urge = (available && director.GuideVisibility <= 0.01f) ? 1f : 0f;
                float pulse = urge * (Mathf.Sin(_clock * 3.2f) * 0.5f + 0.5f) * 0.12f;
                aerialToken.transform.localScale = Vector3.one * (1f + pulse);
            }

            if (aerialHint != null)
            {
                aerialHint.text = available ? "[F]  LOOK" : "USED";
                aerialHint.alpha = Mathf.MoveTowards(aerialHint.alpha, inRound ? 1f : 0f, dt * 4f);
            }
        }

        // ------------------------------------------------------------------ banner

        void UpdateBanner(float dt)
        {
            if (bannerGroup == null) return;
            _bannerTimer -= dt;
            float want = _bannerTimer > 0f ? 1f : 0f;
            bannerGroup.alpha = Mathf.MoveTowards(bannerGroup.alpha, want, dt * 3.5f);

            // The goal plate rides inside the banner group, so this only has to decide whether it has
            // anything to say. Nested alphas multiply: an empty goal is invisible even while the
            // banner around it is at full strength.
            if (bannerGoalGroup != null)
            {
                bool haveGoal = bannerGoal != null && !string.IsNullOrEmpty(bannerGoal.text);
                bannerGoalGroup.alpha = Mathf.MoveTowards(bannerGoalGroup.alpha, haveGoal ? 1f : 0f,
                                                          dt * 3.5f);
            }
        }

        /// <summary>
        /// Put an announcement on screen for a few seconds.
        ///
        /// <paramref name="goal"/> is the extra plate under the ribbon. It defaults to empty, which
        /// means every existing caller clears it without having to know it exists — and that is the
        /// mechanism that keeps the championship's goal line to the one beat it belongs on rather than
        /// letting it leak onto "STUDY IT", "TIME!" and the guide-lost banner.
        /// </summary>
        void ShowBanner(string title, string subtitle, float seconds, string goal = "")
        {
            if (bannerTitle != null) bannerTitle.text = title;
            if (bannerSubtitle != null) bannerSubtitle.text = subtitle;
            if (bannerGoal != null) bannerGoal.text = goal ?? "";
            _bannerTimer = seconds;
        }

        void HandleCountdown(int n)
        {
            if (countdownText == null) return;
            countdownText.text = n > 0 ? n.ToString() : "GO!";
            countdownText.transform.localScale = Vector3.one * 1.6f;
            StartCoroutineSafe();
        }

        void StartCoroutineSafe()
        {
            // Scale punch is done in Tick rather than a coroutine so it survives scripted stepping.
            _countdownPunch = 1f;
        }

        float _countdownPunch;

        void UpdateCountdown(float dt)
        {
            if (countdownText == null) return;
            _countdownPunch = Mathf.MoveTowards(_countdownPunch, 0f, dt * 2.4f);
            countdownText.transform.localScale = Vector3.one * (1f + _countdownPunch * 0.6f);
            bool show = director != null && director.State == GameState.Countdown;
            countdownText.alpha = Mathf.MoveTowards(countdownText.alpha, show ? 1f : 0f, dt * 4f);
        }

        // ------------------------------------------------------------------ results

        void HandleState(GameState s)
        {
            switch (s)
            {
                case GameState.Briefing:
                    ClearResults();
                    // The subject on the ribbon, and what the round is worth on the plate beneath it.
                    //
                    // The label stays "TODAY'S SUBJECT" rather than the round number: a counter in the
                    // headline is the presentation that was cut. What the championship needs to say is
                    // not which round this is, it is what this round has to deliver — and that goes
                    // below, once, at the moment the player is deciding how hard to push.
                    ShowBanner("TODAY'S SUBJECT",
                               target != null ? TargetShapes.DisplayName(target.Shape) : "",
                               3.6f, BriefingGoal());
                    break;

                case GameState.Preview:
                    // Name the bargain out loud, once, the first time it matters. A player who
                    // works out only at second 20 that the outline was going to vanish has already
                    // lost the round, and will read it as the game cheating rather than as a rule
                    // they were told.
                    ShowBanner("STUDY IT", "THE LINES WILL FADE", 3.6f);
                    break;

                case GameState.Klaxon:
                    ShowBanner("PENCILS DOWN", "TIME!", 1.6f);
                    break;

                case GameState.Reveal:
                    // The headline belongs on the ribbon, not the small plate above it — the plate
                    // is sized for a two-word label.
                    ShowBanner("SCORING", "THE VERDICT AWAITS", 1.4f);
                    FillBreakdown(director.LastScore);
                    // What the continue key will do, written now and shown later.
                    //
                    // Written HERE rather than per frame because the answer cannot change while the
                    // reveal is on screen — a reveal only ever happens in the Lawn Art round and the
                    // bench is the only thing on the far side of one. Shown later: see UpdateOutro,
                    // which holds it back until the reveal has stopped moving.
                    if (outroPrompt != null) outroPrompt.text = $"[SPACE]  {director.PressOnLabel}";
                    break;

                case GameState.Judging:
                    // Nothing on screen while the panel deliberates. The number that matters in
                    // this beat is the one standing on the judge's desk, and a column of empty
                    // score plates over the left of frame was both covering the judges and
                    // announcing the result before they had given it.
                    break;

                case GameState.VenueTour:
                    // The player's own card comes down. The tour is about the other four lawns, and
                    // a column of the player's marks over the top of somebody else's artwork is
                    // both a distraction and a misattribution.
                    SetGroup(resultsGroup, 0f);
                    // Nobody has been posted yet on this tour; the card stays down until the first
                    // plot is actually reached.
                    _tourCardShown = false;
                    break;

                case GameState.Scoreboard:
                    SetGroup(resultsGroup, 0f);
                    // Retire the card rather than only hiding it. Hiding alone is what produced the
                    // double blink — see UpdateTourCard.
                    _tourCardShown = false;
                    SetGroup(tourGroup, 0f);
                    // The prompt itself is held back until the board has settled — see UpdateOutro.
                    if (outroGroup != null) outroGroup.alpha = 0f;
                    if (outroPrompt != null)
                        // Nothing to offer on the last board: the ending page takes over by itself,
                        // and a prompt here would invite the player to skip their own payoff.
                        //
                        // IT NO LONGER NAMES THE STAGE, on the owner's instruction, and the reason
                        // is worth keeping because the argument for naming it was a good one.
                        //
                        // This has been "NEXT PICTURE", then "NEXT ROUND", then the stage's own name
                        // — GOOSE RALLY, BLOOM RUSH — on the grounds that the honest answer to "what
                        // does this key do" is the name of the thing about to happen. That is true
                        // of a SIGN. It is not what a prompt is for. A prompt is read in the second
                        // before a key is pressed and its job is to say that the key carries on;
                        // announcing the destination here spends the arrival before the player gets
                        // there, and the game already announces it properly a moment later, on the
                        // curtain, where a sign that reads GOOSE RALLY is doing exactly its job.
                        // Two announcements of the same thing, four seconds apart, and the first one
                        // is a caption on a menu.
                        //
                        // The names have not gone anywhere: RallyStage and TurfStage put them on the
                        // curtain, TurfDirector announces one in the arena, the controls card heads
                        // itself with one, and the front page's class list is made of them.
                        //
                        // The LAST board says nothing at all, and that is unchanged: the ending page
                        // takes over by itself, so a prompt here would invite the player to skip
                        // their own payoff. It is also why this cannot promise a stage that is not
                        // coming — the only board that offers to press on is one with a round left.
                        //
                        // R does reset the running points — it has to, because they are already
                        // banked by the time this board is up — but saying so would put the points
                        // table back on screen, and "START OVER" is true either way.
                        outroPrompt.text = Champ != null && Champ.IsComplete
                            ? ""
                            : "[SPACE]  NEXT STAGE     [R]  START OVER     [ESC]  PAUSE";
                    break;

                case GameState.Ceremony:
                    // The ceremony's champion beat re-uses the round card on the left of frame, which
                    // is the only reason this is not simply 0.
                    SetGroup(resultsGroup, ceremonyResultsHold ? 1f : 0f);
                    FillCeremonyCard();
                    break;

                case GameState.Verdict:
                    SetGroup(resultsGroup, 1f);
                    if (retryHint != null)
                    {
                        // TWO endings now, and the branch has to be the DIRECTOR'S or the card lies.
                        // See the Verdict case in GameDirector.Tick: solo is tested first, then
                        // whether there is a venue to tour, and the fallback is a venue with no
                        // rivals in it. Testing the tour first here — which is what this did —
                        // printed "see the gardens" over a solo round that was about to walk out to
                        // the front page, because the rivals array is authored on the component and
                        // is populated whether or not the round is using it. tourAhead keeps its
                        // !solo term for exactly that reason, so the order is still the director's
                        // even though only one test is left visible.
                        //
                        // It used to be three lines because the third offered [R] RETRY SAME
                        // PICTURE. The retry is gone — a round is a stage of a championship and
                        // re-mowing one in place was a fourth meaning for the end of a round — and
                        // with it went the only thing that made the solo case and the no-rivals case
                        // different. Both walk out to the front page and both now say so in the same
                        // words, which is the point: SPACE CONTINUES, and what it continues TO is
                        // the only thing that ever changes.
                        bool solo = director != null && director.SoloRound;
                        bool tourAhead = !solo && director != null && director.tournament != null &&
                                         director.tournament.rivals.Length > 0;

                        retryHint.text = ResultsHint(tourAhead);
                    }
                    break;
            }
        }

        /// <summary>
        /// What the results card offers at the end of a picture, in words.
        ///
        /// PUBLIC AND STATIC because it has a second reader, and that reader is the reason this is
        /// a method at all rather than two literals where they are used. DuckShoot fakes this screen
        /// to photograph it — it never plays a round, so it sets the hint by hand — and it hand-
        /// copied the string, and it went stale twice: once still advertising [N] NEW PICTURE after
        /// that key was removed, and again with [R] RETRY SAME PICTURE after that one was. A mock
        /// that drifts does not fail loudly, it produces an authoritative-looking frame sheet of a
        /// game that does not exist. One function, two callers, no copy to forget.
        ///
        /// SPACE CONTINUES is the whole of it; only the destination changes. There is no third arm:
        /// the retry that used to make one is gone, so a round with no tour ahead of it says exactly
        /// what a round played on its own says, because they do exactly the same thing.
        /// </summary>
        public static string ResultsHint(bool tourAhead)
            => tourAhead
                // With neighbours to visit the round is not over, so the only thing on offer is
                // pressing on. "THE VENUE" told the player nothing about what they were about to be
                // shown, which is three rivals' finished lawns.
                ? "[SPACE]  SEE THE OTHER GARDENS     [ESC]  PAUSE"
                // A round played on its own, or a venue with nothing left to show. Named plainly:
                // this is the one prompt in the game whose destination is a menu rather than a
                // place in the world.
                : "[SPACE]  MAIN MENU     [ESC]  PAUSE";

        /// <summary>The points table, or null in a scene assembled without a tournament.</summary>
        Championship Champ => director != null && director.tournament != null
            ? director.tournament.Championship : null;

        static readonly Color Gold = new Color(1f, 0.85f, 0.45f);
        static readonly Color Cream = new Color(0.97f, 0.94f, 0.86f);

        /// <summary>
        /// What this round is worth, for the plate under the briefing ribbon.
        ///
        /// On the first round the scoring rule comes with it, on a second line, because that is the
        /// only round on which the player does not already know it — and being told once that there
        /// are three rounds and what a win is worth is the difference between a championship and a
        /// sequence of unexplained lawns. On the last round the line is a computed guarantee; see
        /// <see cref="Championship.GoalLine"/>.
        /// </summary>
        string BriefingGoal()
        {
            var c = Champ;
            if (c == null) return "";
            string line = c.GoalLine();
            if (string.IsNullOrEmpty(line)) return "";
            return c.HasResults ? line : $"{line}\n{Championship.PointsRule()}";
        }

        // ------------------------------------------------------------------ ceremony

        /// <summary>
        /// The closing title card, and the only place in the whole game that says a championship was
        /// happening at all.
        ///
        /// It states the outcome and nothing that was never shown. It used to print the winning point
        /// total, which stopped making sense the moment the standings card was cut: a number the
        /// player has never once seen totted up is not evidence, it is trivia. Who took it and where
        /// the player came are both things three rounds of play have already made felt.
        ///
        /// A defeat is written out as plainly as a win, because the one thing a losing player must not
        /// be left with is a screen that simply stops.
        /// </summary>
        void FillCeremonyCard()
        {
            var c = Champ;
            if (c == null) return;

            bool won = c.PlayerIsChampion;

            if (ceremonyTitle != null)
            {
                ceremonyTitle.text = won ? "CHAMPION" : "CHAMPIONSHIP OVER";
                ceremonyTitle.color = won ? Gold : Cream;
            }

            if (ceremonySubtitle != null)
            {
                var lead = c.Leader;
                ceremonySubtitle.text = won
                    ? "COUNTY GARDENER OF THE YEAR"
                    : $"{lead.name} THE {lead.species.ToUpperInvariant()} TAKES THE COUNTY · " +
                      $"YOU FINISHED {Championship.Ordinal(c.PlayerPlace)}";
            }

            if (ceremonyRosette != null)
            {
                bool have = won && rosetteByRank != null && rosetteByRank.Length > 0 &&
                            rosetteByRank[0] != null;
                if (have) ceremonyRosette.sprite = rosetteByRank[0];
                ceremonyRosette.enabled = have;
            }

            if (ceremonyPrompt != null) ceremonyPrompt.text = "[SPACE]  PLAY AGAIN";
        }

        void UpdateCeremonyCard(float dt)
        {
            if (ceremonyGroup == null || director == null) return;

            var ceremony = director.Ceremony;
            bool show = director.State == GameState.Ceremony && ceremony != null && ceremony.CardUp;
            ceremonyGroup.alpha = Mathf.MoveTowards(ceremonyGroup.alpha, show ? 1f : 0f, dt * 2.2f);

            // The prompt trails the card. Offering the exit in the same frame as the title tells the
            // player the sequence is something to get past.
            if (ceremonyPrompt != null)
                ceremonyPrompt.alpha = Mathf.MoveTowards(ceremonyPrompt.alpha,
                    show && ceremony.PromptUp ? 1f : 0f, dt * 2.2f);
        }

        /// <summary>
        /// The moment the outline finishes dissolving. Called once per round.
        ///
        /// This gets a banner because it is the round's turning point and it happens somewhere in
        /// the middle of open lawn with no other punctuation — without it, players reported the
        /// guide "glitching out" rather than reading it as a beat they were meant to feel.
        /// </summary>
        void HandleGuideLost()
        {
            ShowBanner("FROM MEMORY", "THE CHALK IS GONE", 2.2f);
        }

        /// <summary>
        /// Name the contestant whose lawn the camera is currently over, with the mark their own
        /// station gave them. Without this the tour is four anonymous fields; with it, it is a
        /// results sequence.
        /// </summary>
        public void ShowContestant(Standing s)
        {
            if (tourName != null) tourName.text = s.isPlayer ? $"{s.name}  (YOU)" : s.name;
            if (tourSubtitle != null)
                tourSubtitle.text = s.isPlayer ? "YOUR LAWN" : $"{s.species.ToUpperInvariant()}";
            if (tourScore != null) tourScore.text = $"{s.total:0} / 30";
            if (tourGrade != null) { tourGrade.text = s.rank; tourGrade.color = s.livery; }
            if (tourPortrait != null)
            {
                var tex = ContestantPortraits.Instance != null
                    ? ContestantPortraits.Instance.Get(s.name) : null;
                tourPortrait.texture = tex;
                tourPortrait.enabled = tex != null;
                FitPortrait(tex);
            }
            _tourCardShown = true;
        }

        /// <summary>
        /// Keep the tour's portrait the shape it was rendered at.
        ///
        /// THE RATIO COMES OFF THE TEXTURE, which is the whole point of doing it here rather than
        /// baking a frame shape that happens to match today's render. ContestantPortraits makes four
        /// square targets at the moment; the day somebody wants taller ones, this reads the new
        /// number and the card is still right, because the only thing that was ever wrong was
        /// letting the LAYOUT decide the proportions of a PICTURE.
        ///
        /// The fitter is added on the spot when the scene predates it. A builder-baked reference
        /// only reaches a player once somebody re-runs the builder and saves, and a stretched face
        /// in the meantime is the fault this method exists to end — the same argument
        /// ComicSequence.GroundThePage makes for repairing a baked canvas from the runtime side. On
        /// an old scene the fitter lands on the RawImage's own rect rather than on a window inside
        /// it, so the picture covers the mount instead of sitting in it: differently framed, and
        /// correctly proportioned, which is the half that matters.
        /// </summary>
        void FitPortrait(Texture tex)
        {
            if (tourPortraitFit == null && tourPortrait != null)
            {
                // Written out rather than with ??, which is the one operator that must not be used
                // on a UnityEngine.Object: it tests for a real null and the engine's own null is a
                // live managed reference wearing an overloaded operator. It happens to be safe on a
                // fresh GetComponent and it is not a habit to leave lying in a file.
                var existing = tourPortrait.gameObject.GetComponent<AspectRatioFitter>();
                tourPortraitFit = existing != null
                    ? existing : tourPortrait.gameObject.AddComponent<AspectRatioFitter>();
            }
            if (tourPortraitFit == null) return;

            tourPortraitFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            // Guarded, because a zero height is a division by zero and the infinity that comes out
            // of it is written straight into a layout. A texture that has not finished being made
            // reads 0x0, and RenderTexture.Create is asynchronous enough on WebGL to make that a
            // real frame rather than a theoretical one.
            if (tex != null && tex.width > 0 && tex.height > 0)
                tourPortraitFit.aspectRatio = (float)tex.width / tex.height;
        }

        bool _tourCardShown;

        /// <summary>
        /// The contestant card is visible for as long as the tour is running, and not one moment
        /// longer.
        ///
        /// It used to be driven by a 3.4 s stopwatch, which was wrong at both ends. Between plots,
        /// a three-second hold plus a two-second flight to the next lawn outlasted the timer, so
        /// the card died in mid-air and came back — a blink between every contestant. And at the
        /// end of the tour it fought the Scoreboard state: HandleState snapped the group to zero
        /// while the timer still had time left on it, and this method faded it straight back up
        /// again on the very next frame before finally letting it go. That is the double blink on
        /// the last contestant.
        ///
        /// Tying it to the state instead means there is exactly one authority for whether the card
        /// is up, which is the same rule every other group on this HUD follows.
        /// </summary>
        void UpdateTourCard(float dt)
        {
            if (tourGroup == null) return;
            bool show = _tourCardShown && director != null && director.State == GameState.VenueTour;
            tourGroup.alpha = Mathf.MoveTowards(tourGroup.alpha, show ? 1f : 0f, dt * 3.2f);
        }

        /// <summary>
        /// The card that tells the player where they came and what to press.
        ///
        /// The results panel is deliberately hidden at the scoreboard so the board itself is the
        /// only thing on screen — which left the game ending on a wall with no way forward offered
        /// and no statement of how the player actually did. This is the closing beat: your placing,
        /// and the two keys.
        ///
        /// It waits for the board to finish sorting. Putting "press R" on screen while the rankings
        /// are still moving steps on the one dramatic moment the round has been building toward.
        ///
        /// ---- it also carries the reveal's continue prompt, and that is not a hack ----
        ///
        /// The reveal used to be SILENT. The picture finished drawing itself and the round then sat
        /// for six seconds waiting for a key that nothing on screen mentioned, while the only prompt
        /// stage one ever printed — at the verdict, a full minute later — advertised NEW PICTURE, a
        /// key that started a different round. A player who never guessed at Space experienced the
        /// hold as the game having stopped.
        ///
        /// This card is the right home for that prompt rather than a second one being built: it is
        /// already, by its own definition above, "the card that tells the player what to press", and
        /// it is the only such card in the HUD. Its placing line is written only at the board and
        /// blanked everywhere else, so at the reveal it shows exactly one line — the key and what
        /// the key does — which is all this beat has to say.
        /// </summary>
        void UpdateOutro(float dt)
        {
            if (outroGroup == null || director == null) return;

            bool atBoard = director.State == GameState.Scoreboard;
            bool settled = atBoard && (director.scoreboard == null || director.scoreboard.Finished);
            // Held back until the reveal stops moving — the director owns that timing, because the
            // same flag is what gates the keypress. A prompt printed over the ghost sweep would be
            // telling the player to skip the payoff while the payoff is still playing.
            bool pressOn = director.RevealHolding;

            if (settled && outroPlacing != null && string.IsNullOrEmpty(outroPlacing.text))
            {
                var t = director.tournament;
                if (t != null)
                {
                    // THE LINE HAS TO DESCRIBE THE BOARD IT IS UNDER, and the board moved.
                    //
                    // This used to read a single round's placing and call it "3RD OF 4 THIS TIME",
                    // which was right when the board above it showed one picture's result. It does
                    // not any more: GameDirector fills it from Tournament.ChampionshipStandings —
                    // the CUMULATIVE table — because an arena round used to end on two boards and
                    // the one that survived had to be the one saying something new. So the rows said
                    // "the evening so far" while the line under them said "this time", and worse,
                    // the NUMBER was a different number: Tournament.PlayerPlace comes off the round's
                    // standings and Championship.PlayerPlace comes off the table. The line could
                    // legitimately say 3RD under a board showing the player top.
                    //
                    // Both are read from the same place the board reads, and the branch is the same
                    // branch: cumulative when there is a table, the round when there is not.
                    var champ = t.Championship;
                    bool cumulative = champ != null && champ.Table != null && champ.Table.Count > 0;

                    if (cumulative)
                    {
                        // "OF 4" is dropped. It counted rivals, and the quantity the player is
                        // actually being told is the TOTAL — so the line states the total, in the
                        // same "N / 90" the board and the arena cards already use.
                        int place = champ.PlayerPlace;
                        int outOf = Mathf.Max(champ.RoundsTotal, 1) * Championship.RivalRoundMax;
                        string standing = champ.IsComplete
                            ? (place == 1 ? "CHAMPION" : $"{Championship.Ordinal(place)} OVERALL")
                            : (place == 1 ? "LEADING" : $"{Championship.Ordinal(place)} OVERALL");
                        outroPlacing.text = $"{standing}  ·  {champ.PlayerPoints} / {outOf}";
                        outroPlacing.color = place == 1 ? Gold : Cream;

                        // CHAMPION is only ever printed on a COMPLETE championship. The note this
                        // paragraph replaced recorded that an earlier version said "YOU WON THE
                        // CHAMPIONSHIP" off one round's placing and "was a lie every time it
                        // appeared"; the word is safe here for the two reasons it was not there —
                        // the place is cumulative, and IsComplete means there is nothing left to
                        // play. Before the last round it says LEADING, which is all that is known.
                    }
                    else
                    {
                        // No table yet: the board has fallen back to this round's standings, so the
                        // line falls back with it. This is the original copy, kept because it was
                        // never wrong about the board it was written for.
                        int place = t.PlayerPlace;
                        int of = Mathf.Max(t.Standings.Count, 1);
                        outroPlacing.text = place == 1
                            ? "YOU WON THIS ONE"
                            : $"{Championship.Ordinal(place)} OF {of} THIS TIME";
                        outroPlacing.color = place == 1 ? Gold : Cream;
                    }
                }
            }

            if (!atBoard && outroPlacing != null) outroPlacing.text = "";

            // The prompt takes the whole plate when there is nothing above it.
            //
            // This card is built as two bands — the placing on top, the prompt under it — and at the
            // BOARD that is right, because both lines are there. At the REVEAL the placing is
            // deliberately blanked by the line above, so the prompt was sitting in the lower 47% of a
            // plate whose upper half was empty: one instruction, pinned low, in a box centred on
            // nothing. That reads as a mistake even though every number in it is what was asked for.
            //
            // Done here rather than by moving the band in the builder, because the builder cannot
            // know: the split is correct for one of the two states this card appears in, and which
            // state it is in is a runtime fact. Anchors rather than a position so the band still
            // stretches with the plate at any aspect.
            if (outroPrompt != null)
            {
                bool alone = outroPlacing == null || string.IsNullOrEmpty(outroPlacing.text);
                var rt = outroPrompt.rectTransform;
                float top = alone ? 1f : 0.47f;
                if (!Mathf.Approximately(rt.anchorMax.y, top))
                    rt.anchorMax = new Vector2(rt.anchorMax.x, top);
            }

            float want = (settled || pressOn) ? 1f : 0f;
            outroGroup.alpha = Mathf.MoveTowards(outroGroup.alpha, want, dt * 2.2f);
        }

        void ClearResults()
        {
            SetGroup(resultsGroup, 0f);
            _shownTotal = 0f;
            for (int i = 0; i < 3; i++)
            {
                if (judgeScores.Length > i && judgeScores[i] != null) judgeScores[i].text = "";
                if (judgeQuips.Length > i && judgeQuips[i] != null) judgeQuips[i].text = "";
            }
            if (resultsRank != null) resultsRank.text = "";
            if (resultsTotal != null) resultsTotal.text = "";
            if (resultsRosette != null) resultsRosette.enabled = false;
            if (retryHint != null) retryHint.text = "";
        }

        void FillBreakdown(RoundScore s)
        {
            if (coverageStat != null) coverageStat.text = $"{Mathf.RoundToInt(s.coverage * 100f)}%";
            if (spillStat != null) spillStat.text = $"{Mathf.RoundToInt(s.spill * 100f)}%";
            if (edgeStat != null) edgeStat.text = $"{Mathf.RoundToInt(s.edgeQuality * 100f)}%";
            if (styleStat != null) styleStat.text = $"{Mathf.RoundToInt(s.style * 100f)}%";
        }

        void HandleJudgeScored(int index, float score, string quip)
        {
            if (index < 0 || index >= 3) return;
            if (judgeNames.Length > index && judgeNames[index] != null && judges != null)
                judgeNames[index].text = judges.judges[index]?.displayName ?? "";
            if (judgeScores.Length > index && judgeScores[index] != null)
                judgeScores[index].text = Mathf.RoundToInt(score).ToString();
            if (judgeQuips.Length > index && judgeQuips[index] != null)
                judgeQuips[index].text = quip;

            _shownTotal += score;
            if (resultsTotal != null) resultsTotal.text = $"{Mathf.RoundToInt(_shownTotal)} / 30";
        }

        /// <summary>
        /// Put the FINAL ROUND on the round card: where the player placed in it, and what it paid.
        ///
        /// Deliberately not the championship total — the board beat that follows this one is where
        /// the total lands, and printing it here would answer that shot before it happens.
        /// </summary>
        public void ShowFinalRound(int place, int pointsGained)
        {
            if (resultsRank != null) resultsRank.text = Championship.Ordinal(place);
            if (resultsTotal != null) resultsTotal.text = $"+{Mathf.Max(0, pointsGained)}";
            if (resultsRosette != null)
            {
                int idx = Mathf.Clamp(place - 1, 0, 4);
                if (rosetteByRank != null && idx < rosetteByRank.Length && rosetteByRank[idx] != null)
                {
                    resultsRosette.sprite = rosetteByRank[idx];
                    resultsRosette.enabled = true;
                }
            }
        }

        void HandleVerdict(float total, string rank)
        {
            if (resultsTotal != null) resultsTotal.text = $"{Mathf.RoundToInt(total)} / 30";
            if (resultsRank != null) resultsRank.text = rank;
            if (resultsRosette != null)
            {
                int idx = rank switch { "S" => 0, "A" => 1, "B" => 2, "C" => 3, _ => 4 };
                if (rosetteByRank != null && idx < rosetteByRank.Length && rosetteByRank[idx] != null)
                {
                    resultsRosette.sprite = rosetteByRank[idx];
                    resultsRosette.enabled = true;
                }
            }
        }

        static void SetGroup(CanvasGroup g, float alpha)
        {
            if (g == null) return;
            g.alpha = alpha;
            g.blocksRaycasts = alpha > 0.5f;
        }
    }
}
