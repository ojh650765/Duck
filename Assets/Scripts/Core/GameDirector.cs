using System;
using UnityEngine;

namespace DuckMow
{
    public enum GameState
    {
        Boot, Briefing,
        /// <summary>Overhead on the whole venue while every plot shows the picture. The study beat.</summary>
        Preview,
        Countdown, Mowing, Klaxon, Reveal, Judging, Verdict,
        /// <summary>Flying the venue, revealing each rival's finished work in turn.</summary>
        VenueTour,
        /// <summary>At the plaza board while the rankings settle and the winner is called.</summary>
        Scoreboard,
        /// <summary>
        /// The opening story page, before the first round of a session. Appended to the end of the
        /// enum rather than inserted in front of Briefing where it belongs in the flow, because the
        /// numeric values of the others are what the capture tools and any serialized reference
        /// hold — reordering this list to read nicely would silently repoint every one of them.
        /// </summary>
        Intro,
        /// <summary>
        /// The championship is decided: the bench reacts, the prize is shown, and a title card
        /// lands. Appended for the same reason Intro is — see above.
        /// </summary>
        Ceremony,
        /// <summary>
        /// The final defence: the picture is finished and a flock comes in over the venue. Sits
        /// between the klaxon and the reveal in the flow, and appended here rather than inserted
        /// there for the same reason Intro and Ceremony are — see above. Only entered when
        /// <see cref="GameDirector.defencePhaseEnabled"/> is set, which it is not by default.
        /// </summary>
        Defence
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
        [Tooltip("How solid the chalk outline is while the player is mowing.")]
        [Range(0f, 1f)] public float chalkLineAlpha = 0.62f;
        public Autopilot autopilot;
        public Tournament tournament;
        public Scoreboard scoreboard;
        [Tooltip("The opening story page. Null is fine — the game starts on the round instead.")]
        public ComicSequence intro;

        [Header("Opening story")]
        [Tooltip("Play the story page before the first round. Cleared by the capture tools, which " +
                 "want the round and not the forty seconds of scrapbook in front of it.")]
        public bool playIntro = true;

        [Header("Demo")]
        [Tooltip("Let the autopilot drive. Used to capture repeatable footage and to sanity-check scoring.")]
        public bool autopilotEnabled;

        [Header("Defence phase (GREYBOX, on probation)")]
        [Tooltip("Run the goose raid between the klaxon and the reveal. OFF by default: the phase is " +
                 "an unproven greybox and the round has to be exactly as it was until it earns its " +
                 "place. Turning this on is the whole of the wiring — see EnsureDefence.")]
        public bool defencePhaseEnabled;
        [Tooltip("The raid. Left empty on purpose: it is found or created on demand, so enabling the " +
                 "phase needs no scene rebuild and cutting it leaves nothing to unwire.")]
        public GooseDefence defence;

        [Header("Round")]
        public float roundDuration = 75f;
        [Tooltip("Extra seconds granted for the hardest pictures.")]
        public float difficultyTimeBonus = 22f;
        public bool randomiseFirstShape = true;
        public ShapeId startingShape = ShapeId.Heart;

        [Header("Beat lengths (seconds)")]
        public float briefingDuration = 3.4f;
        [Tooltip("Rounded to whole seconds at runtime — a count-in has to give every number the " +
                 "same beat. See the Countdown case in SetState.")]
        public float countdownDuration = 3f;
        public float klaxonDuration = 1.8f;
        public float revealPictureHold = 2.9f;
        public float revealGhostSweep = 1.6f;
        public float revealAnalysisHold = 1.5f;

        [Header("Warnings")]
        public float lowTimeWarning = 15f;

        [Header("The memory beat")]
        [Tooltip("Seconds the whole venue is shown from above before the count-in, so the picture " +
                 "can be studied. This is the only time it is ever presented as a whole.")]
        public float previewDuration = 4f;
        [Tooltip("Fraction of the round the ground guide stays fully drawn. Proportional rather " +
                 "than a fixed count of seconds: a 75 s heart and a 97 s star must feel the same.")]
        [Range(0f, 0.5f)] public float guideHoldFraction = 0.11f;
        [Tooltip("Fraction of the round the guide takes to dissolve away.")]
        [Range(0.02f, 0.5f)] public float guideFadeFraction = 0.13f;

        [Header("Aerial check")]
        [Tooltip("How many times a round the player may lift the camera to look at their own work.")]
        public int aerialChecksPerRound = 1;
        [Tooltip("Seconds held at the top of the rise. The clock keeps running throughout — that " +
                 "cost is the decision.")]
        public float aerialHold = 1.2f;
        public float aerialRise = 0.7f;
        public float aerialFall = 0.6f;

        public GameState State { get; private set; } = GameState.Boot;
        public float TimeRemaining { get; private set; }
        public float RoundLength { get; private set; }
        public float TimeFraction => RoundLength > 0f ? Mathf.Clamp01(TimeRemaining / RoundLength) : 0f;
        public bool IsLowTime => State == GameState.Mowing && TimeRemaining <= lowTimeWarning;
        public int CountdownNumber { get; private set; }
        public RoundScore LastScore { get; private set; }
        public int BonkCount { get; private set; }
        public int RoundNumber { get; private set; }

        /// <summary>1 while the ground guide is fully drawn, 0 once it has gone. Read by the HUD
        /// and by the rivals, so everybody on the field loses the picture at the same instant.</summary>
        public float GuideVisibility { get; private set; } = 1f;
        /// <summary>True on the frames the guide is actually dissolving, for the crowd and the bots.</summary>
        public bool GuideDissolving { get; private set; }
        public int AerialChecksRemaining { get; private set; }
        /// <summary>0 when on the chase camera, 1 at the top of an aerial check.</summary>
        public float AerialAmount { get; private set; }
        public bool AerialActive => _aerialPhase != AerialPhase.Idle;

        public event Action OnGuideLost;
        public event Action OnAerialCheckUsed;
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
        static readonly int IdDissolve = Shader.PropertyToID("_Dissolve");
        static readonly int IdAnchorAmount = Shader.PropertyToID("_AnchorAmount");

        int _countdownSeconds = 3;

        enum AerialPhase { Idle, Rising, Holding, Falling }
        AerialPhase _aerialPhase = AerialPhase.Idle;
        float _aerialTimer;
        bool _guideLostFired;

        float _chalkBaseAlpha = 0.62f;
        Material _chalkInstance;

        void Awake()
        {
            Instance = this;
            _rng = new System.Random(Environment.TickCount);
            // The chalk outline is animated by writing to a material, and that must never be the
            // shared asset.
            //
            // It was. The reveal fades _LineAlpha to zero, and on a shared material that zero is
            // written straight into the asset on disk — so the next time the game started, Awake
            // read the base alpha back out of a material the last session had already faded to
            // nothing, and the outline never appeared again. The player is then mowing a field
            // with no picture on it, which reads exactly like being dropped outside the shape, and
            // it only happens after a session that reached the reveal. Hence "sometimes".
            //
            // So: instance the material, and take the base value from a serialized field rather
            // than from the thing being animated.
            _chalkBaseAlpha = chalkLineAlpha;
            if (chalkMaterial != null)
            {
                _chalkInstance = new Material(chalkMaterial) { name = chalkMaterial.name + " (round)" };
                var chalkRenderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
                foreach (var r in chalkRenderers)
                    if (r.sharedMaterial == chalkMaterial) r.sharedMaterial = _chalkInstance;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_chalkInstance != null) Destroy(_chalkInstance);
        }

        void Start()
        {
            if (mower != null) mower.OnImpact += HandleImpact;
            if (judges != null)
            {
                judges.OnJudgeStartsDeliberating += FocusJudge;
                judges.OnVerdict += (total, rank) => cameraDirector?.SetJudgeFocus(null);
            }
            _currentShape = randomiseFirstShape ? RandomShape() : startingShape;

            // The story runs in front of the first round of a session and nowhere else.
            //
            // It is deliberately a gate on Start rather than a step inside BeginRound: RetrySameShape
            // and NextShape both go through BeginRound, and a player who has just finished a round
            // and pressed R wants the start line, not the duck's childhood. Putting the check here
            // means "never replays on retry" is a property of the shape of the code rather than a
            // flag somebody has to remember to clear.
            if (playIntro && intro != null)
            {
                // Budgeted, not trusted. If the sequence stalls — a null in the panel table, an
                // exception inside its own tick, art that never loaded — the round starts anyway.
                // An opening nobody can get past is strictly worse than an opening nobody sees.
                _introBudget = Mathf.Clamp(intro.TotalSeconds + 8f, 10f, 180f);
                SetState(GameState.Intro);
                intro.Begin();
                return;
            }

            BeginRound(_currentShape, true);
        }

        float _introBudget = 90f;

        /// <summary>
        /// Abandon the opening story and start the round now. For the capture tools, which have no
        /// business sitting through it, and as the hard escape if the sequence ever wedges.
        /// </summary>
        public void SkipIntro()
        {
            playIntro = false;
            if (State != GameState.Intro) return;
            intro?.Hide();
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
        ///
        /// Public because the ceremony ends on the same portrait and must not have to trust that
        /// some earlier beat left the machine on the mark.
        /// </summary>
        public void StageForPortrait()
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

        /// <summary>
        /// The defence phase, found or made.
        ///
        /// Created on demand rather than wired into Main.unity, and that is the point: the phase is a
        /// greybox on probation, so enabling it must not require a scene rebuild and cutting it must
        /// not leave a dangling reference in a saved scene for somebody to find later.
        /// </summary>
        GooseDefence EnsureDefence()
        {
            if (defence != null) return defence;

            defence = FindFirstObjectByType<GooseDefence>();
            if (defence != null) return defence;

            var go = new GameObject("~ GooseDefence (greybox)");
            go.transform.SetParent(transform, false);
            defence = go.AddComponent<GooseDefence>();
            return defence;
        }

        public void BeginRound(ShapeId shape, bool announce)
        {
            _currentShape = shape;
            RoundNumber++;

            // A retry can land in the middle of the raid, and the flock holds the camera and has
            // capsules standing on the lawn. Nothing about a fresh round should have to survive
            // either of those.
            defence?.Abort();

            target.Build(shape);
            cutMask.ClearAll();
            // Stand the ornaments back up. They no longer right themselves on a timer — a flattened
            // gnome stays flattened so the reveal shows the damage — so this is the one place a
            // plot gets tidied.
            Gnome.ResetAll();
            BonkCount = 0;
            LastScore = default;
            _lowTimeFired = false;

            RoundLength = roundDuration + difficultyTimeBonus * TargetShapes.Difficulty(shape);
            TimeRemaining = RoundLength;

            // The autopilot is a debug driver. If it is not wanted for this round, make sure it is
            // not still steering from the last one.
            if (!autopilotEnabled) autopilot?.Stop();

            target.GetStartPose(out Vector3 pos, out Quaternion rot);
            _roundStartPos = pos;
            _roundStartRot = rot;
            mower.ResetTo(pos, rot);
            // The machine has jumped. If the camera happened to be looking at it — the verdict
            // portrait, or a chase left over from the intro — snap rather than letting the rig
            // sail across the field after it.
            cameraDirector?.NotifyTargetTeleported();

            // Say out loud where the round started and whether that is inside the picture.
            //
            // This exists because "it starts outside" and "the audit says it starts 8 m inside"
            // were both being reported about the same build, and there was no way to tell which
            // session was wrong. Now the game states it, in the log, every round — so the claim
            // and the measurement come from the same machine.
            {
                var sp = new Vector2(pos.x / target.shapeRadius, pos.z / target.shapeRadius);
                float d = TargetShapes.Sdf(shape, sp);
                float clearance = -d * target.shapeRadius;
                Debug.Log($"[Duck] round {RoundNumber}: {shape} start ({pos.x:0.0}, {pos.z:0.0}) " +
                          $"{(d < 0f ? "INSIDE" : "OUTSIDE")} clearance {clearance:0.00} m");
            }
            InputReader.Instance?.ResetSmoothing();

            // Fresh picture, fresh memory: the guide is back to fully drawn and the aerial check
            // is re-armed. Both have to be reset here rather than on entering Mowing, because the
            // preview beat shows the guide before Mowing is ever entered.
            GuideVisibility = 1f;
            GuideDissolving = false;
            _guideLostFired = false;
            AerialChecksRemaining = Mathf.Max(0, aerialChecksPerRound);
            AerialAmount = 0f;
            _aerialPhase = AerialPhase.Idle;

            SetChalk(_chalkBaseAlpha, 0f, 0f, 1.2f);
            judges?.ResetPanel();
            scoreboard?.ResetBoard();
            tournament?.BeginRound(shape, guideHoldFraction + guideFadeFraction);
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
            // Entering a state you are already in re-runs its setup — which for Verdict means
            // parking the mower on the portrait mark a second time, potentially after a new round
            // has already placed it.
            if (State == s) return;

            GameState from = State;
            State = s;
            _stateTime = 0f;

            // Whatever the round does next, the defence phase must not be left holding the camera.
            // Its own tick shuts it down on the way to the reveal; this covers the routes that do
            // not go through that — DebugForceState, the capture tools, and a retry pressed while a
            // goose is still in the air. A phase that kept the camera would leave the next beat being
            // rendered by a lens pointed at a hedge.
            if (from == GameState.Defence) defence?.Abort();

            // Driving stays live through the defence phase, and that is the point of it: the player
            // keeps the machine and the controls they have spent the whole round with, so the phase
            // reads as the round continuing rather than as a minigame that borrowed the duck. The
            // blade is locked instead of the wheels — see MowerController.BladeLocked.
            bool driving = s == GameState.Mowing || s == GameState.Defence;
            if (InputReader.Instance != null) InputReader.Instance.DrivingEnabled = driving;

            switch (s)
            {
                case GameState.Briefing:
                {
                    // Cut, do not blend, when arriving from the end of the previous round.
                    //
                    // BeginRound puts the mower back on its starting mark the instant it is called,
                    // and the shot on screen at that moment is the verdict portrait — a camera
                    // framed ON the mower, parked on the staging mark. So the machine vanished out
                    // of its own close-up and reappeared across the field, and the camera, still
                    // tracking it, lurched after it. That is the teleport being reported, and it
                    // only happens on the announce path because the retry path already snaps.
                    //
                    // A cut hides it completely and is the right edit anyway: television does not
                    // dissolve from a portrait to a wide.
                    bool fromRoundEnd = from == GameState.Verdict || from == GameState.VenueTour ||
                                        from == GameState.Scoreboard || from == GameState.Ceremony;
                    cameraDirector?.SetMode(CameraMode.Briefing, fromRoundEnd ? 0f : 0.9f);
                    if (fromRoundEnd) cameraDirector?.SnapToCurrent();
                    break;
                }

                case GameState.Preview:
                    // The study beat. Every plot carries the same picture and the guide is at full
                    // strength, because this is the only moment the player is shown the whole
                    // problem at once. Everything after this is recall.
                    SetChalk(_chalkBaseAlpha, 0f, 0f, 1.2f);
                    cameraDirector?.SetMode(CameraMode.VenuePreview, 1.0f);
                    break;
                case GameState.Countdown:
                    // Put the mower back on its mark.
                    //
                    // The verdict parks it on a staging spot for the closing portrait, and nothing
                    // was undoing that reliably — a round could begin with the player sitting at
                    // (7.5, -14), which is outside every picture. It reads exactly like the spawn
                    // being broken: no outline anywhere nearby, nothing scoring, the whole round
                    // spent mowing blank lawn. Re-asserting here means the countdown is the single
                    // moment that decides where a round starts, whatever happened before it.
                    if (mower != null)
                    {
                        mower.ParkAt(_roundStartPos, _roundStartRot);
                        cameraDirector?.NotifyTargetTeleported();
                    }
                    cameraDirector?.SetMode(CameraMode.Chase, 1.1f);
                    // Whole seconds, always.
                    //
                    // The duration was 3.2 s and the number came from CeilToInt, so the count-in
                    // opened on "4" and held it for two tenths of a second before dropping to "3".
                    // It read as the countdown skipping a beat, and no amount of staring at the
                    // display logic explains it — the fault is that a 3.2 s window cannot be
                    // divided into equal one-second numbers. Rounding here means the beat is
                    // right whatever gets typed into the inspector.
                    _countdownSeconds = Mathf.Max(1, Mathf.RoundToInt(countdownDuration));
                    CountdownNumber = _countdownSeconds;
                    OnCountdownTick?.Invoke(CountdownNumber);
                    break;
                case GameState.Mowing:
                    if (autopilotEnabled && autopilot != null) autopilot.Begin();
                    break;
                case GameState.Klaxon:
                    mower?.CutEngine();
                    autopilot?.Stop();
                    // The klaxon can land while the player is still up in the air. Abandon the lift
                    // rather than letting its Falling phase hand driving back during the reveal.
                    _aerialPhase = AerialPhase.Idle;
                    AerialAmount = 0f;
                    // Everyone stops. Rival artworks are settled and marked here so the tour has
                    // finished work to fly over rather than lawns still being cut behind it.
                    tournament?.FlushMasks();
                    break;

                case GameState.Defence:
                    // Started from here rather than from the first Tick so the arena exists and the
                    // machine has been moved into it on the same frame the state changes. A frame
                    // later leaks one frame of the mower still standing on the finished lawn.
                    //
                    // The picture itself is already settled and is not touched again: the phase is
                    // played on a garden grown from it, 420 m off the map, and the score the reveal
                    // is about to compute is exactly what the player mowed.
                    defence = EnsureDefence();
                    defence?.Begin();
                    break;

                case GameState.Reveal:
                    LastScore = target.Evaluate(cutMask, mower.DriftMetres, mower.BoostMetres, BonkCount);
                    // Cut, do not blend, when arriving from the defence phase.
                    //
                    // That phase is played on a pitch 420 m off the map and it deliberately LEAVES the
                    // mower there, so a 2.4 s blend spends every one of those seconds sailing the camera
                    // across the landscape. A cut is also the right edit: the reveal is a hard change of
                    // subject from the duck to its picture.
                    {
                        bool fromDefence = from == GameState.Defence;
                        cameraDirector?.SetMode(CameraMode.Reveal, fromDefence ? 0f : 2.4f);
                        if (fromDefence) cameraDirector?.SnapToCurrent();
                    }
                    OnRevealStarted?.Invoke(LastScore);
                    break;
                case GameState.Judging:
                    // The reveal's diagnostic overlay has said its piece. Clear it before any
                    // ground-level camera has to look across the lawn again.
                    SetChalk(0f, 0f, 0f, 1.2f, 0f, 0f);
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
                    // The picture's marks and the defence award meet here and nowhere else. The award is
                    // read off the phase rather than recomputed, and it is zero unless the phase actually
                    // ran and finished — so a round without it scores exactly as it always did.
                    tournament?.CloseRound(judges != null ? judges.Total : 0f,
                                           judges != null ? judges.Rank : "D",
                                           Venue.Player.centre,
                                           defencePhaseEnabled && defence != null ? defence.Award : 0);
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
                    // The round's points join the championship here — see Tournament.BankRound for
                    // why this and not the verdict. It has to happen before the board is filled, so
                    // that whether this was the last round is already known by the time the board
                    // decides what to call the contestant on top of it.
                    tournament?.BankRound();
                    if (tournament != null)
                        scoreboard?.Settle(tournament.Standings,
                                           tournament.Championship.IsComplete ? "CHAMPION" : "WINNER");
                    AudioDirector.Instance?.CrowdCheer(0.85f, applaud: true);
                    break;

                case GameState.Ceremony:
                    // Put the machine back on the portrait mark before the sequence needs it. The
                    // verdict already parked it there and nothing since has moved it, but the
                    // ceremony's last beat is that portrait and it must not depend on which route
                    // through the round happened to get here.
                    StageForPortrait();
                    _ceremony ??= new VictoryCeremony();
                    _ceremony.Begin(tournament != null ? tournament.Championship : null,
                                    judges, cameraDirector);
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

            // Escape leaves, from any state except the opening — during the intro there is nothing on
            // screen the player would be trying to get out of, and the cutscene has its own skip.
            // Checked before the state switch so it works even in states that accept no other input.
            //
            // Menu.unity has to be in the build settings or this silently does nothing;
            // DuckMenuBuilder.RegisterBuildScenes is what guarantees that.
            if (input != null && input.MenuPressed && State != GameState.Intro)
            {
                BackToMenu();
                return;
            }

            switch (State)
            {
                case GameState.Intro:
                    // The sequence skips itself on any key; all this has to do is notice that it
                    // is over. The budget is the failure path, and it is checked here rather than
                    // trusted to the sequence because the whole point is that a broken sequence
                    // cannot be the thing that reports itself broken.
                    if (intro == null || intro.Finished || _stateTime >= _introBudget)
                    {
                        if (intro != null && !intro.Finished)
                        {
                            Debug.LogWarning($"[Duck] opening story did not finish within " +
                                             $"{_introBudget:0.0}s; starting the round without it.");
                            intro.Hide();
                        }
                        BeginRound(_currentShape, true);
                    }
                    break;

                case GameState.Briefing:
                    // Let an impatient player skip the announcement.
                    if (_stateTime >= briefingDuration || (input != null && input.AnyConfirmPressed))
                        SetState(GameState.Preview);
                    break;

                case GameState.Preview:
                    // Deliberately NOT skippable. Every other beat in the round can be cut short,
                    // but this one is the whole bargain: you get exactly this long to look, and
                    // letting an eager player skip it would hand them a round they cannot win and
                    // make the memory mechanic feel like a trick rather than a rule.
                    if (_stateTime >= previewDuration) SetState(GameState.Countdown);
                    break;

                case GameState.Countdown:
                {
                    // Counts against the rounded window, not the raw field, so every number holds
                    // for a full second.
                    int n = Mathf.CeilToInt(_countdownSeconds - _stateTime);
                    if (n != CountdownNumber)
                    {
                        CountdownNumber = n;
                        OnCountdownTick?.Invoke(Mathf.Max(n, 0));
                    }
                    if (_stateTime >= _countdownSeconds) SetState(GameState.Mowing);
                    break;
                }

                case GameState.Mowing:
                    TimeRemaining -= dt;
                    UpdateGuide();
                    UpdateAerial(dt, input);
                    // The venue works to the player's clock, so however long this picture granted,
                    // every contestant downs tools on the same klaxon.
                    tournament?.Tick(dt, RoundLength > 0f ? 1f - Mathf.Clamp01(TimeRemaining / RoundLength) : 1f,
                                     GuideVisibility);
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
                    if (_stateTime >= klaxonDuration)
                        SetState(defencePhaseEnabled ? GameState.Defence : GameState.Reveal);
                    break;

                case GameState.Defence:
                    // Stepped before the finish test, so the phase gets its last frame — putting the
                    // machine back and handing the clock over — before the reveal is entered.
                    defence?.Tick(dt);
                    // Budgeted, not trusted — the same rule the opening story is under. A phase that
                    // never reports itself over would strand the round one beat short of the reveal,
                    // with a finished picture nothing on screen is able to show.
                    if (defence == null || defence.Finished || _stateTime > 30f)
                    {
                        if (defence != null && !defence.Finished)
                            Debug.LogWarning($"[Duck] defence phase did not finish within " +
                                             $"{_stateTime:0.0}s; going to the reveal without it.");
                        SetState(GameState.Reveal);
                    }
                    break;

                case GameState.Reveal:
                    UpdateReveal();
                    break;

                case GameState.Judging:
                    if (judges == null || judges.Finished) SetState(GameState.Verdict);
                    break;

                case GameState.Verdict:
                {
                    // The player's own result gets a beat to itself before the venue opens up.
                    //
                    // With a venue to tour, the round is NOT over here and the only thing the player
                    // can do is press on to it. This used to accept R and N as well, which jumped
                    // straight into a fresh picture from the verdict — and now that the championship
                    // banks a round at the board, that shortcut would have thrown away a round the
                    // player had just finished, marks and all, without it ever counting.
                    bool tourAhead = tournament != null && tournament.rivals.Length > 0;
                    if (tourAhead)
                    {
                        bool skip = _stateTime > 1.2f && input != null && input.AnyConfirmPressed;
                        if (_stateTime >= verdictHold || skip) SetState(GameState.VenueTour);
                        break;
                    }
                    if (input != null)
                    {
                        if (input.RetryPressed) RetrySameShape();
                        else if (input.NextPressed || input.AnyConfirmPressed) NextShape();
                    }
                    break;
                }

                case GameState.VenueTour:
                    UpdateVenueTour(dt);
                    break;

                case GameState.Scoreboard:
                {
                    scoreboard?.Tick(dt);
                    // Budgeted, not trusted. A board that never reports itself settled — a row the
                    // builder failed to wire, an empty standings list — would otherwise leave the
                    // last round of a championship parked on a screen that accepts no input at all,
                    // which is the one failure worse than a bad ceremony.
                    bool settled = _stateTime > 2.5f &&
                                   (scoreboard == null || scoreboard.Finished || _stateTime > 12f);

                    // The last round resolves itself. The payoff for winning a championship cannot
                    // sit behind a keypress the player has no reason to expect is waiting for them.
                    if (tournament != null && tournament.Championship.IsComplete)
                    {
                        if (settled) SetState(GameState.Ceremony);
                        break;
                    }

                    if (input != null && _stateTime > 2.5f)
                    {
                        // R restarts the whole championship rather than re-mowing this round. The
                        // round's points are already banked by the time this board is on screen, so
                        // a same-picture retry would let one round be counted twice.
                        if (input.RetryPressed) RestartChampionship();
                        else if (input.NextPressed || input.AnyConfirmPressed) NextShape();
                    }
                    break;
                }

                case GameState.Ceremony:
                    _ceremony?.Tick(dt);
                    if (input != null && _ceremony != null && _ceremony.PromptUp &&
                        (input.RetryPressed || input.NextPressed || input.AnyConfirmPressed))
                        RestartChampionship();
                    break;
            }
        }

        VictoryCeremony _ceremony;

        /// <summary>The closing sequence, once there is one. Read by the HUD for its title card.</summary>
        public VictoryCeremony Ceremony => _ceremony;

        /// <summary>
        /// Wipe the points and start a fresh championship on a new picture.
        ///
        /// The only route back to round one, and the reason there is no longer a same-picture retry
        /// at the board: a round is banked the moment the board appears, so re-mowing it would award
        /// its points a second time. Restarting the championship is the honest version of that
        /// offer, and it is also what a player who has just lost actually wants.
        /// </summary>
        public void RestartChampionship()
        {
            tournament?.ResetChampionship();
            NextShape();
        }

        bool _leavingToMenu;

        /// <summary>
        /// Leave the championship and go back to the front page.
        ///
        /// A scene load rather than a state, unlike everything else in this class. A round resets in
        /// place because retry has to be instant, but the menu is a different scene with its own
        /// camera, lawn, canvas and mown picture — rebuilding all of that inside the game scene would
        /// be a second copy of DuckMenuBuilder free to drift from the first.
        /// </summary>
        public void BackToMenu()
        {
            // LoadSceneAsync is not idempotent and Escape is a key players hold down, so a second
            // call while the first is still loading would queue a redundant load.
            if (_leavingToMenu) return;
            _leavingToMenu = true;
            // Shut the defence phase down before the load rather than leaving it to OnDisable. The
            // load is ASYNCHRONOUS, so this scene keeps ticking for a while yet — and the phase holds
            // Time.timeScale down to two percent during a hit stop, which is a global that outlives a
            // scene change. Escape pressed on the frame a goose connects would otherwise drop the
            // player onto the menu in slow motion.
            defence?.Abort();
            UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Menu");
        }

        /// <summary>
        /// Cheat: jump straight into the ceremony, as champion or as runner-up.
        ///
        /// The victory sequence is by far the hardest thing in this game to reach — it needs three
        /// full rounds AND for the player to actually come first — so reviewing it otherwise means
        /// playing for an outcome and hoping. That is not a review process, and every pass over the
        /// ceremony's framing, timing and copy has to start by getting to it.
        ///
        /// Safe to enter cold: the Ceremony case stages the portrait itself and builds its own
        /// VictoryCeremony, so it depends on nothing that only a played-through round would leave
        /// behind. The standings do have to say something, though, or the title card has no result to
        /// report — hence the seeding below rather than a bare SetState.
        /// </summary>
        public void DebugJumpToCeremony(bool playerChampion)
        {
            var champ = tournament != null ? tournament.Championship : null;
            if (champ != null)
            {
                // Totals that read as a real three-round table rather than a placeholder: a clear
                // winner, a close second, and two trailing. The player swaps between first and
                // second depending on which ending is being looked at.
                //
                // These are CUMULATIVE SCORES now, not placement points — the championship is the sum
                // of round scores, so a three-round table lives in the sixties and seventies out of
                // ninety. The old values were 11 and 9, which under the current rule would be three
                // rounds of catastrophe and would have made the ceremony card announce a champion on
                // four marks a round.
                int[] pts = playerChampion ? new[] { 71, 66, 52, 38 } : new[] { 66, 71, 52, 38 };
                int[] wins = playerChampion ? new[] { 2, 1, 0, 0 } : new[] { 1, 2, 0, 0 };
                int i = 0;
                foreach (var e in champ.Table)
                {
                    // Walk the table by identity, not by index — Table is kept sorted, so the row
                    // order here is whatever the last result left behind.
                    int slot = e.isPlayer ? 0 : (++i);
                    champ.DebugSetPoints(e.name, pts[Mathf.Min(slot, pts.Length - 1)],
                                                 wins[Mathf.Min(slot, wins.Length - 1)]);
                }
                champ.DebugSetRoundsRecorded(champ.RoundsTotal);
            }
            SetState(GameState.Ceremony);
        }

        [Header("Venue tour")]
        [Tooltip("Seconds the player's own verdict holds before the venue tour begins.")]
        public float verdictHold = 4.2f;
        [Tooltip("Seconds each contestant's finished work is held on screen.")]
        public float tourHoldPerPlot = 3.0f;

        int _tourIndex = -1;
        float _tourHold;

        Vector3 _roundStartPos;
        Quaternion _roundStartRot = Quaternion.identity;

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

            // Dissolve is released here so the reveal's own beats are not fighting the round's
            // erosion mask — from this point the ghost fill is what answers the question the fade
            // asked, and it must be allowed to draw at full strength.
            //
            // The anchors come off with the chalk. They are a working aid, and leaving them over
            // the finished picture clutters the one shot that has to read as artwork.
            float anchor = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.8f));
            SetChalk(chalk, ghost, analysis, sweep, 0f, anchor);

            if (t >= analysisEnd) SetState(GameState.Judging);
        }

        // ------------------------------------------------------------------ the memory beat

        /// <summary>
        /// Eat the ground guide away on a schedule proportional to the round.
        ///
        /// Proportional rather than a fixed count of seconds, because the round length already
        /// varies with the picture — 75 s for a heart, 97 s for a star. A fixed eight-second hold
        /// would give the hardest shape the smallest share of its round to memorise the most
        /// complicated outline, which is exactly backwards.
        ///
        /// The anchors are left at full strength throughout. What is being taken away is the
        /// shape, never the registration.
        /// </summary>
        void UpdateGuide()
        {
            float elapsed = RoundLength - TimeRemaining;
            float hold = RoundLength * guideHoldFraction;
            float fade = Mathf.Max(RoundLength * guideFadeFraction, 0.01f);

            float dissolve = Mathf.Clamp01((elapsed - hold) / fade);
            GuideVisibility = 1f - dissolve;
            GuideDissolving = dissolve > 0.001f && dissolve < 0.999f;

            SetChalk(_chalkBaseAlpha, 0f, 0f, 1.2f, dissolve, 1f);

            if (!_guideLostFired && dissolve >= 0.999f)
            {
                _guideLostFired = true;
                OnGuideLost?.Invoke();
            }
        }

        /// <summary>
        /// The one-per-round lift.
        ///
        /// The clock keeps running the whole time, and that is the entire design: the player is
        /// buying information with the only currency the round has. Driving is cut for the
        /// duration so the cost is purely time — leaving the mower under power while the camera is
        /// ninety metres up means checking your work can destroy it, which turns a judgement call
        /// into a punishment.
        /// </summary>
        void UpdateAerial(float dt, InputReader input)
        {
            if (_aerialPhase == AerialPhase.Idle)
            {
                bool wants = input != null && input.AerialPressed;
                if (!wants || AerialChecksRemaining <= 0) return;

                AerialChecksRemaining--;
                _aerialPhase = AerialPhase.Rising;
                _aerialTimer = 0f;
                mower?.CutEngine();
                if (input != null) input.DrivingEnabled = false;
                cameraDirector?.SetMode(CameraMode.Aerial, aerialRise);
                OnAerialCheckUsed?.Invoke();
                return;
            }

            _aerialTimer += dt;

            switch (_aerialPhase)
            {
                case AerialPhase.Rising:
                    AerialAmount = Mathf.Clamp01(_aerialTimer / Mathf.Max(aerialRise, 1e-3f));
                    if (_aerialTimer >= aerialRise) { _aerialPhase = AerialPhase.Holding; _aerialTimer = 0f; }
                    break;

                case AerialPhase.Holding:
                    AerialAmount = 1f;
                    if (_aerialTimer >= aerialHold)
                    {
                        _aerialPhase = AerialPhase.Falling;
                        _aerialTimer = 0f;
                        cameraDirector?.SetMode(CameraMode.Chase, aerialFall);
                    }
                    break;

                case AerialPhase.Falling:
                    AerialAmount = 1f - Mathf.Clamp01(_aerialTimer / Mathf.Max(aerialFall, 1e-3f));
                    if (_aerialTimer >= aerialFall)
                    {
                        _aerialPhase = AerialPhase.Idle;
                        AerialAmount = 0f;
                        // Hand control back only if the round is still running — the klaxon can
                        // land mid-lift, and re-enabling driving after it would let the player
                        // carry on mowing into the reveal.
                        if (State == GameState.Mowing && input != null) input.DrivingEnabled = true;
                    }
                    break;
            }
        }

        // ---- hooks used by the editor capture tools, so shots are deterministic ----

        public void DebugForceState(GameState s) => SetState(s);
        public void DebugSetTimeRemaining(float seconds) => TimeRemaining = Mathf.Max(0f, seconds);
        public void DebugSetShape(ShapeId shape) => BeginRound(shape, true);

        void SetChalk(float lineAlpha, float ghost, float analysis, float sweep,
                      float dissolve = 0f, float anchor = 1f)
        {
            // Never the shared asset — see Awake.
            var m = _chalkInstance != null ? _chalkInstance : chalkMaterial;
            if (m == null) return;
            m.SetFloat(IdLineAlpha, lineAlpha);
            m.SetFloat(IdGhostAmount, ghost);
            m.SetFloat(IdAnalysisAmount, analysis);
            m.SetFloat(IdSweepPhase, sweep);
            m.SetFloat(IdDissolve, dissolve);
            m.SetFloat(IdAnchorAmount, anchor);
        }
    }
}
