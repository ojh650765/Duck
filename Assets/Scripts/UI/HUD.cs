using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DuckMow
{
    /// <summary>
    /// The in-round HUD and the results screen.
    ///
    /// Everything here fights the same problem: the game is played over a bright, busy, saturated
    /// lawn, so every element needs its own dark backing or outline or it disappears. Nothing is
    /// on screen that the player does not need — the timer, how much of the picture is done, the
    /// boost they earned, and a minimap, because from the chase camera you genuinely cannot tell
    /// how much of the shape you have filled.
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
        public TextMeshProUGUI coverageText;
        public Image coverageFill;
        public RawImage minimap;

        [Header("Announcements")]
        public CanvasGroup bannerGroup;
        public TextMeshProUGUI bannerTitle;
        public TextMeshProUGUI bannerSubtitle;
        public TextMeshProUGUI countdownText;

        [Header("Results")]
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

        [Header("Outro")]
        public CanvasGroup outroGroup;
        public TextMeshProUGUI outroPlacing;
        public TextMeshProUGUI outroPrompt;

        [Header("Feel")]
        public Color timerNormal = new Color(1f, 0.97f, 0.88f);
        public Color timerLow = new Color(1f, 0.42f, 0.32f);
        public float lowTimePulseSpeed = 4.5f;

        Material _minimapMaterial;
        float _clock;
        float _bannerTimer;
        float _shownTotal;
        static readonly int IdMowerUV = Shader.PropertyToID("_MowerUV");

        void Awake()
        {
            if (minimap != null && minimap.material != null)
            {
                // Instance the material so editing it at runtime does not dirty the asset.
                _minimapMaterial = new Material(minimap.material);
                minimap.material = _minimapMaterial;
            }
            SetGroup(resultsGroup, 0f);
            SetGroup(bannerGroup, 0f);
        }

        void OnDestroy()
        {
            if (_minimapMaterial != null) Destroy(_minimapMaterial);
        }

        void Start()
        {
            if (director != null)
            {
                director.OnStateChanged += HandleState;
                director.OnCountdownTick += HandleCountdown;
                director.OnContestantRevealed += ShowContestant;
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
            UpdateMinimap();
        }

        // ------------------------------------------------------------------ round HUD

        void UpdateRoundHud(float dt)
        {
            if (director == null) return;

            bool showRound = director.State == GameState.Countdown ||
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

            // Coverage is the number that actually tells you how you are doing.
            float coverage = ComputeCoverage();
            if (coverageFill != null)
                coverageFill.fillAmount = Mathf.Lerp(coverageFill.fillAmount, coverage, dt * 5f);
            if (coverageText != null)
                coverageText.text = $"{Mathf.RoundToInt(coverage * 100f)}%";
        }

        float ComputeCoverage()
        {
            if (cutMask == null || target == null || target.Inside == null || target.InsideCount == 0) return 0f;

            // Sampled rather than exhaustive: 65k cells every frame is not worth it for a
            // progress bar, and every 7th cell is statistically identical at this resolution.
            var cut = cutMask.Cut;
            var inside = target.Inside;
            int hit = 0, total = 0;
            for (int i = 0; i < cut.Length; i += 7)
            {
                if (!inside[i]) continue;
                total++;
                if (cut[i] >= 128) hit++;
            }
            return total > 0 ? (float)hit / total : 0f;
        }

        void UpdateMinimap()
        {
            if (_minimapMaterial == null || mower == null) return;
            Vector2 uv = Field.WorldToUV(mower.transform.position);
            Vector3 fwd = mower.transform.forward;
            _minimapMaterial.SetVector(IdMowerUV, new Vector4(uv.x, uv.y, fwd.x, fwd.z));
        }

        // ------------------------------------------------------------------ banner

        void UpdateBanner(float dt)
        {
            if (bannerGroup == null) return;
            _bannerTimer -= dt;
            float want = _bannerTimer > 0f ? 1f : 0f;
            bannerGroup.alpha = Mathf.MoveTowards(bannerGroup.alpha, want, dt * 3.5f);
        }

        void ShowBanner(string title, string subtitle, float seconds)
        {
            if (bannerTitle != null) bannerTitle.text = title;
            if (bannerSubtitle != null) bannerSubtitle.text = subtitle;
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
                    ShowBanner("TODAY'S SUBJECT", target != null
                        ? TargetShapes.DisplayName(target.Shape) : "", 3.2f);
                    break;

                case GameState.Klaxon:
                    ShowBanner("PENCILS DOWN", "TIME!", 1.6f);
                    break;

                case GameState.Reveal:
                    // The headline belongs on the ribbon, not the small plate above it — the plate
                    // is sized for a two-word label.
                    ShowBanner("SCORING", "THE VERDICT AWAITS", 1.4f);
                    FillBreakdown(director.LastScore);
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
                    break;

                case GameState.Scoreboard:
                    SetGroup(resultsGroup, 0f);
                    SetGroup(tourGroup, 0f);
                    // The prompt itself is held back until the board has settled — see UpdateOutro.
                    if (outroGroup != null) outroGroup.alpha = 0f;
                    if (outroPrompt != null)
                        outroPrompt.text = "[R]  SAME PICTURE AGAIN          [N]  NEW PICTURE";
                    break;

                case GameState.Verdict:
                    SetGroup(resultsGroup, 1f);
                    if (retryHint != null)
                        retryHint.text = "[R]  RETRY SAME PICTURE        [N]  NEW PICTURE";
                    break;
            }
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
            }
            SetGroup(tourGroup, 1f);
            _tourCardTimer = 3.4f;
        }

        float _tourCardTimer;

        void UpdateTourCard(float dt)
        {
            if (tourGroup == null) return;
            if (_tourCardTimer > 0f) _tourCardTimer -= dt;
            float want = _tourCardTimer > 0f ? 1f : 0f;
            tourGroup.alpha = Mathf.MoveTowards(tourGroup.alpha, want, dt * 3.2f);
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
        /// </summary>
        void UpdateOutro(float dt)
        {
            if (outroGroup == null || director == null) return;

            bool atBoard = director.State == GameState.Scoreboard;
            bool settled = atBoard && (director.scoreboard == null || director.scoreboard.Finished);

            if (settled && outroPlacing != null && string.IsNullOrEmpty(outroPlacing.text))
            {
                var t = director.tournament;
                if (t != null)
                {
                    int place = t.PlayerPlace;
                    int of = Mathf.Max(t.Standings.Count, 1);
                    string ordinal = place switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => place + "th" };
                    outroPlacing.text = place == 1
                        ? $"YOU WON THE CHAMPIONSHIP"
                        : $"YOU PLACED {ordinal} OF {of}";
                    outroPlacing.color = place == 1 ? new Color(1f, 0.85f, 0.45f)
                                                    : new Color(0.97f, 0.94f, 0.86f);
                }
            }

            if (!atBoard && outroPlacing != null) outroPlacing.text = "";

            float want = settled ? 1f : 0f;
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
