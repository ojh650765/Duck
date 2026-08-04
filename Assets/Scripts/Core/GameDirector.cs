using System;
using UnityEngine;

namespace DuckMow
{
    public enum GameState
    {
        Boot, Briefing, Countdown, Mowing, Klaxon, Reveal, Judging, Verdict,
        /// <summary>Flying the venue, revealing each rival's finished work in turn.</summary>
        VenueTour,
        /// <summary>At the plaza board while the rankings settle and the winner is called.</summary>
        Scoreboard
    }

    /// <summary>
    /// Runs the round: announces the subject, counts in, times the mowing, cuts the engine,
    /// lifts the camera for the reveal, hands over to the judges, then holds on the verdict
    /// until the player retries.
    ///
    /// Retry never reloads the scene. Everything that changes during a round is resettable in
    /// place, so pressing R puts you back on the start line in a single frame — which is the
    /// difference between a game you play twice and a game you play twenty times.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class GameDirector : MonoBehaviour
    {
        public static GameDirector Instance { get; private set; }

        [Header("References")]
        public RoundTarget target;
        public CutMask cutMask;
        public MowerController mower;
        public CameraDirector cameraDirector;
        public JudgePanel judges;
        public Material chalkMaterial;
        public Autopilot autopilot;
        public Tournament tournament;
        public Scoreboard scoreboard;

        [Header("Demo")]
        [Tooltip("Let the autopilot drive. Used to capture repeatable footage and to sanity-check scoring.")]
        public bool autopilotEnabled;

        [Header("Round")]
        public float roundDuration = 75f;
        [Tooltip("Extra seconds granted for the hardest pictures.")]
        public float difficultyTimeBonus = 22f;
        public bool randomiseFirstShape = true;
        public ShapeId startingShape = ShapeId.Heart;

        [Header("Beat lengths (seconds)")]
        public float briefingDuration = 3.4f;
        public float countdownDuration = 3.2f;
        public float klaxonDuration = 1.8f;
        public float revealPictureHold = 2.9f;
        public float revealGhostSweep = 1.6f;
        public float revealAnalysisHold = 1.5f;

        [Header("Warnings")]
        public float lowTimeWarning = 15f;

        public GameState State { get; private set; } = GameState.Boot;
        public float TimeRemaining { get; private set; }
        public float RoundLength { get; private set; }
        public float TimeFraction => RoundLength > 0f ? Mathf.Clamp01(TimeRemaining / RoundLength) : 0f;
        public bool IsLowTime => State == GameState.Mowing && TimeRemaining <= lowTimeWarning;
        public int CountdownNumber { get; private set; }
        public RoundScore LastScore { get; private set; }
        public int BonkCount { get; private set; }
        public int RoundNumber { get; private set; }
        /// <summary>Seconds spent in the current state. Exposed for the diagnostics.</summary>
        public float StateTime => _stateTime;
        public int UpdateTicks { get; private set; }

        public event Action<GameState> OnStateChanged;
        public event Action<int> OnCountdownTick;      // 3, 2, 1, then 0 for GO
        public event Action OnLowTimeStarted;
        public event Action<RoundScore> OnRevealStarted;
        public event Action<float> OnImpactFelt;

        float _stateTime;
        bool _lowTimeFired;
        System.Random _rng;
        ShapeId _currentShape;

        static readonly int IdGhostAmount = Shader.PropertyToID("_GhostAmount");
        static readonly int IdAnalysisAmount = Shader.PropertyToID("_AnalysisAmount");
        static readonly int IdSweepPhase = Shader.PropertyToID("_SweepPhase");
        static readonly int IdLineAlpha = Shader.PropertyToID("_LineAlpha");

        float _chalkBaseAlpha = 0.62f;

        void Awake()
        {
            Instance = this;
            _rng = new System.Random(Environment.TickCount);
            if (chalkMaterial != null) _chalkBaseAlpha = chalkMaterial.GetFloat(IdLineAlpha);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (mower != null) mower.OnImpact += HandleImpact;
            if (judges != null)
            {
                judges.OnJudgeStartsDeliberating += FocusJudge;
                judges.OnVerdict += (total, rank) => cameraDirector?.SetJudgeFocus(null);
            }
            _currentShape = randomiseFirstShape ? RandomShape() : startingShape;
            BeginRound(_currentShape, true);
        }

        ShapeId RandomShape()
        {
            var all = TargetShapes.All;
            return all[_rng.Next(all.Length)];
        }

        [Header("Closing portrait")]
        [Tooltip("Where the mower is parked for the verdict shot, in world space.")]
        public Vector3 portraitPosition = new Vector3(7.5f, 0.4f, -14f);
        [Tooltip("Heading the mower faces for the verdict shot, degrees.")]
        public float portraitYaw = 196f;

        /// <summary>
        /// Parks the mower on open lawn, well clear of the bench, the tent and the fence, so the
        /// closing portrait is composed rather than wherever the round happened to end.
        /// </summary>
        void StageForPortrait()
        {
            if (mower == null) return;
            Vector3 p = portraitPosition;
            // Sit it on the ground rather than trusting a hand-typed height.
            if (Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 12f,
                                ~0, QueryTriggerInteraction.Ignore))
                p.y = hit.point.y + 0.4f;
            mower.ParkAt(p, Quaternion.Euler(0f, portraitYaw, 0f));
        }

        void FocusJudge(int index)
        {
            if (cameraDirector == null || judges == null) return;
            var c = (index >= 0 && index < judges.judges.Length) ? judges.judges[index]?.character : null;
            cameraDirector.SetJudgeFocus(c != null ? c.transform : null);
        }

        void HandleImpact(float strength, Vector3 point)
        {
            if (State != GameState.Mowing) return;
            BonkCount++;
            cameraDirector?.AddShake(strength * 0.9f);
            OnImpactFelt?.Invoke(strength);
        }

        // ------------------------------------------------------------------ round flow

        public void BeginRound(ShapeId shape, bool announce)
        {
            _currentShape = shape;
            RoundNumber++;

            target.Build(shape);
            cutMask.ClearAll();
            BonkCount = 0;
            LastScore = default;
            _lowTimeFired = false;

            RoundLength = roundDuration + difficultyTimeBonus * TargetShapes.Difficulty(shape);
            TimeRemaining = RoundLength;

            // The autopilot is a debug driver. If it is not wanted for this round, make sure it is
            // not still steering from the last one.
            if (!autopilotEnabled) autopilot?.Stop();

            target.GetStartPose(out Vector3 pos, out Quaternion rot);
            mower.ResetTo(pos, rot);
            InputReader.Instance?.ResetSmoothing();

            SetChalk(_chalkBaseAlpha, 0f, 0f, 1.2f);
            judges?.ResetPanel();
            scoreboard?.ResetBoard();
            tournament?.BeginRound(shape);
            cameraDirector?.SetJudgeFocus(null);

            if (announce) SetState(GameState.Briefing);
            else { cameraDirector?.SnapToChase(); SetState(GameState.Countdown); }
        }

        /// <summary>Same picture, fresh lawn. The fast path the player will use most.</summary>
        public void RetrySameShape() => BeginRound(_currentShape, false);

        /// <summary>Roll a different picture and announce it.</summary>
        public void NextShape()
        {
            ShapeId next;
            int guard = 0;
            do { next = RandomShape(); } while (next == _currentShape && ++guard < 16);
            BeginRound(next, true);
        }

        void SetState(GameState s)
        {
            State = s;
            _stateTime = 0f;

            bool driving = s == GameState.Mowing;
            if (InputReader.Instance != null) InputReader.Instance.DrivingEnabled = driving;

            switch (s)
            {
                case GameState.Briefing:
                    cameraDirector?.SetMode(CameraMode.Briefing, 0.9f);
                    break;
                case GameState.Countdown:
                    cameraDirector?.SetMode(CameraMode.Chase, 1.1f);
                    CountdownNumber = Mathf.CeilToInt(countdownDuration);
                    OnCountdownTick?.Invoke(CountdownNumber);
                    break;
                case GameState.Mowing:
                    if (autopilotEnabled && autopilot != null) autopilot.Begin();
                    break;
                case GameState.Klaxon:
                    mower?.CutEngine();
                    autopilot?.Stop();
                    // Everyone stops. Rival artworks are settled and marked here so the tour has
                    // finished work to fly over rather than lawns still being cut behind it.
                    tournament?.FlushMasks();
                    break;
                case GameState.Reveal:
                    LastScore = target.Evaluate(cutMask, mower.DriftMetres, mower.BoostMetres, BonkCount);
                    cameraDirector?.SetMode(CameraMode.Reveal, 2.4f);
                    OnRevealStarted?.Invoke(LastScore);
                    break;
                case GameState.Judging:
                    // The reveal's diagnostic overlay has said its piece. Clear it before any
                    // ground-level camera has to look across the lawn again.
                    SetChalk(0f, 0f, 0f, 1.2f);
                    // A hard cut, not a blend. Interpolating from ninety metres straight down to a
                    // ground-level bench shot takes the shortest arc through an orientation facing
                    // the wrong way entirely, so the first second of the judging looked like the
                    // camera had wandered off across the meadow. Television cuts here; so do we.
                    cameraDirector?.SetMode(CameraMode.Judges, 0f);
                    cameraDirector?.SnapToCurrent();
                    judges?.BeginJudging(LastScore);
                    break;
                case GameState.Verdict:
                    // Every judge has spoken; pull off the bench and onto the duck.
                    cameraDirector?.SetJudgeFocus(null);
                    StageForPortrait();
                    // Cut, for the same reason the bench is a cut: blending from the bench to a
                    // portrait on the far side of the lawn spends two seconds sailing over grass.
                    cameraDirector?.SetMode(CameraMode.Verdict, 0f);
                    cameraDirector?.SnapToCurrent();
                    // The player's own marks close the venue: everybody is measured now, so the
                    // standings exist before the tour starts showing them.
                    tournament?.CloseRound(judges != null ? judges.Total : 0f,
                                           judges != null ? judges.Rank : "D",
                                           Venue.Player.centre);
                    _tourIndex = -1;
                    _tourHold = 0f;
                    break;

                case GameState.VenueTour:
                    cameraDirector?.SetMode(CameraMode.VenueTour, 0.9f);
                    _tourIndex = -1;
                    _tourHold = 0f;
                    break;

                case GameState.Scoreboard:
                    cameraDirector?.SetMode(CameraMode.Scoreboard, 1.1f);
                    if (tournament != null) scoreboard?.Settle(tournament.Standings);
                    AudioDirector.Instance?.CrowdCheer(0.85f, applaud: true);
                    break;
            }

            OnStateChanged?.Invoke(s);
        }

        void Update()
        {
            if (SimClock.Scripted) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            _stateTime += dt;
            UpdateTicks++;
            var input = InputReader.Instance;

            switch (State)
            {
                case GameState.Briefing:
                    // Let an impatient player skip the announcement.
                    if (_stateTime >= briefingDuration || (input != null && input.AnyConfirmPressed))
                        SetState(GameState.Countdown);
                    break;

                case GameState.Countdown:
                {
                    int n = Mathf.CeilToInt(countdownDuration - _stateTime);
                    if (n != CountdownNumber)
                    {
                        CountdownNumber = n;
                        OnCountdownTick?.Invoke(Mathf.Max(n, 0));
                    }
                    if (_stateTime >= countdownDuration) SetState(GameState.Mowing);
                    break;
                }

                case GameState.Mowing:
                    TimeRemaining -= dt;
                    // The venue works to the player's clock, so however long this picture granted,
                    // every contestant downs tools on the same klaxon.
                    tournament?.Tick(dt, RoundLength > 0f ? 1f - Mathf.Clamp01(TimeRemaining / RoundLength) : 1f);
                    if (!_lowTimeFired && TimeRemaining <= lowTimeWarning)
                    {
                        _lowTimeFired = true;
                        OnLowTimeStarted?.Invoke();
                    }
                    if (TimeRemaining <= 0f)
                    {
                        TimeRemaining = 0f;
                        SetState(GameState.Klaxon);
                    }
                    break;

                case GameState.Klaxon:
                    if (_stateTime >= klaxonDuration) SetState(GameState.Reveal);
                    break;

                case GameState.Reveal:
                    UpdateReveal();
                    break;

                case GameState.Judging:
                    if (judges == null || judges.Finished) SetState(GameState.Verdict);
                    break;

                case GameState.Verdict:
                    // The player's own result gets a beat to itself before the venue opens up.
                    if (_stateTime >= verdictHold && tournament != null && tournament.rivals.Length > 0)
                    {
                        SetState(GameState.VenueTour);
                        break;
                    }
                    if (input != null)
                    {
                        if (input.RetryPressed) RetrySameShape();
                        else if (input.NextPressed || input.AnyConfirmPressed) NextShape();
                    }
                    break;

                case GameState.VenueTour:
                    UpdateVenueTour(dt);
                    break;

                case GameState.Scoreboard:
                    scoreboard?.Tick(dt);
                    if (input != null && _stateTime > 2.5f)
                    {
                        if (input.RetryPressed) RetrySameShape();
                        else if (input.NextPressed || input.AnyConfirmPressed) NextShape();
                    }
                    break;
            }
        }

        [Header("Venue tour")]
        [Tooltip("Seconds the player's own verdict holds before the venue tour begins.")]
        public float verdictHold = 4.2f;
        [Tooltip("Seconds each contestant's finished work is held on screen.")]
        public float tourHoldPerPlot = 3.0f;

        int _tourIndex = -1;
        float _tourHold;

        public event Action<int> OnTourPlot;   // index into Venue.Plots

        /// <summary>
        /// Fly the venue, one plot at a time.
        ///
        /// The camera only moves on once it has actually arrived and held — timing it on a
        /// stopwatch instead meant a long leg across the quad ate the whole hold and the artwork
        /// at the far end was never really seen.
        /// </summary>
        void UpdateVenueTour(float dt)
        {
            if (cameraDirector == null || tournament == null) { SetState(GameState.Scoreboard); return; }

            if (_tourIndex < 0)
            {
                _tourIndex = 0;
                AimTourAt(_tourIndex);
                return;
            }

            if (!cameraDirector.TourArrived) return;

            if (_tourHold <= 0f) PostCurrentPlot();
            _tourHold += dt;
            if (_tourHold < tourHoldPerPlot) return;

            _tourIndex++;
            _tourHold = 0f;

            if (_tourIndex >= Venue.TourOrder.Length) { SetState(GameState.Scoreboard); return; }
            AimTourAt(_tourIndex);
        }

        void AimTourAt(int tourStep)
        {
            int plot = Venue.TourOrder[Mathf.Clamp(tourStep, 0, Venue.TourOrder.Length - 1)];
            var spec = Venue.Plots[plot];
            cameraDirector.SetTourTarget(new Vector3(spec.centre.x, 0f, spec.centre.y), spec.size);
            OnTourPlot?.Invoke(plot);
        }

        /// <summary>Put the contestant the camera is currently over onto the board.</summary>
        void PostCurrentPlot()
        {
            int plot = Venue.TourOrder[Mathf.Clamp(_tourIndex, 0, Venue.TourOrder.Length - 1)];
            var spec = Venue.Plots[plot];
            foreach (var s in tournament.Standings)
            {
                if ((s.plotCentre - spec.centre).sqrMagnitude > 1f) continue;
                scoreboard?.Post(s);
                OnContestantRevealed?.Invoke(s);
                // The crowd at that plot reacts to their own contestant's mark.
                AudioDirector.Instance?.CrowdCheer(Mathf.InverseLerp(6f, 26f, s.total));
                break;
            }
        }

        public event Action<Standing> OnContestantRevealed;

        /// <summary>
        /// The reveal is three beats, and the order matters: first you see what you made
        /// (payoff), then what you were asked for (comparison), then where you went wrong
        /// (the argument the judges are about to have).
        /// </summary>
        void UpdateReveal()
        {
            float t = _stateTime;
            float chalk = Mathf.Lerp(_chalkBaseAlpha, 0f, Mathf.Clamp01(t / 0.8f));

            float ghost = 0f, analysis = 0f, sweep = 1.2f;

            float ghostStart = revealPictureHold;
            float ghostEnd = ghostStart + revealGhostSweep;
            float analysisEnd = ghostEnd + revealAnalysisHold;

            if (t >= ghostStart)
            {
                float g = Mathf.Clamp01((t - ghostStart) / revealGhostSweep);
                ghost = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(g * 3f));
                sweep = Mathf.Lerp(1.2f, -0.25f, Mathf.SmoothStep(0f, 1f, g));
            }
            if (t >= ghostEnd)
            {
                analysis = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - ghostEnd) / 0.6f));
                ghost *= Mathf.Lerp(1f, 0.35f, analysis);
            }

            SetChalk(chalk, ghost, analysis, sweep);

            if (t >= analysisEnd) SetState(GameState.Judging);
        }

        // ---- hooks used by the editor capture tools, so shots are deterministic ----

        public void DebugForceState(GameState s) => SetState(s);
        public void DebugSetTimeRemaining(float seconds) => TimeRemaining = Mathf.Max(0f, seconds);
        public void DebugSetShape(ShapeId shape) => BeginRound(shape, true);

        void SetChalk(float lineAlpha, float ghost, float analysis, float sweep)
        {
            if (chalkMaterial == null) return;
            chalkMaterial.SetFloat(IdLineAlpha, lineAlpha);
            chalkMaterial.SetFloat(IdGhostAmount, ghost);
            chalkMaterial.SetFloat(IdAnalysisAmount, analysis);
            chalkMaterial.SetFloat(IdSweepPhase, sweep);
        }
    }
}
