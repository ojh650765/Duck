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
        Defence,
        /// <summary>
        /// The goose rally: four contestants, four gardens and a flock, played out at
        /// GooseRally.unity while this scene sleeps behind it. Sits between the klaxon and the
        /// reveal, and appended here for the same reason Intro, Ceremony and Defence are — the
        /// numeric values are what the capture tools and every serialized reference hold.
        /// </summary>
        Rally,
        /// <summary>
        /// Bloom Rush: four gardeners painting one arena, played out at BloomRush.unity while this
        /// scene sleeps behind it. Stage three, after the rally and before the reveal, and appended
        /// here for the same reason Intro, Ceremony, Defence and Rally are — the numeric values of
        /// this enum are what the capture tools and every serialized reference hold, so inserting
        /// it where it belongs in the running order would silently repoint all of them.
        /// </summary>
        Bloom,
        /// <summary>
        /// The last thing the game shows: a page of comic panels, chosen by the championship total
        /// over all three rounds. Appended for the same reason every state after Scoreboard is —
        /// the numeric values are what the capture tools and every serialized reference hold.
        /// </summary>
        Ending
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

        [Header("Endings")]
        [Tooltip("Played when the duck finishes the championship on top: the prize money, and the " +
                 "pond it buys. Null is fine — the game loops back to a fresh championship instead.")]
        public ComicSequence endingWin;
        [Tooltip("Played when it does not: the pond sold out from under them, and the diggers " +
                 "moving in. Null is fine.")]
        public ComicSequence endingLose;

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

        [Header("Goose rally (stage two)")]
        [Tooltip("Run the four-way goose rally between the klaxon and the reveal. This is the mode " +
                 "the old one-on-one defence phase above was a prototype for; do not turn both on.")]
        public bool rallyEnabled = true;
        [Tooltip("Which round of the championship the rally plays on. Two.\n\n" +
                 "A number rather than an 'on the final round' flag, which is what this was. That " +
                 "flag hard-coded the rally as the finale, and it is not the finale — it is stage " +
                 "two, with something else after it. A named round also means the stages can be " +
                 "re-ordered from the inspector instead of from a boolean that only knows how to say " +
                 "'last'.\n\n" +
                 "Zero or less plays it on every round, which is only useful for review.")]
        public int rallyOnRound = 2;

        [Header("Bloom Rush (stage three)")]
        [Tooltip("Run the four-way territory match between the rally and the reveal.\n\n" +
                 "Off by default, and deliberately: the round's flow through the klaxon is a " +
                 "finished, reviewed sequence, and a new stage that inserted itself into it without " +
                 "being asked would change every completed round in the game. Turn it on here, or " +
                 "open BloomRush.unity and press play — the stage runs standalone, which is how it " +
                 "is meant to be reviewed.")]
        public bool bloomEnabled;
        [Tooltip("Which round of the championship Bloom Rush plays on. Three, after the rally's two.\n\n" +
                 "Zero or less plays it on every round, which is only useful for review.")]
        public int bloomOnRound = 3;

        [Header("Stage chain")]
        [Tooltip("Play the round's later stages between the reveal and the judging, instead of " +
                 "splicing them into the middle of the round at the klaxon.\n\n" +
                 "This is the running order the game is built around: mow the picture, see it, " +
                 "press on to the goose rally, press on to Bloom Rush, and only THEN face the " +
                 "panel — so the three judges are marking the whole round rather than only the " +
                 "part of it that happened on a lawn.\n\n" +
                 "Off, the stages splice themselves in at the klaxon instead, which is where they " +
                 "started life and is still how a single stage is reviewed on its own.\n\n" +
                 "The two paths are mutually exclusive by construction — see StageChainNext — so a " +
                 "stage can never be played twice in one round.")]
        public bool stageChainOnConfirm = true;

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
        [Tooltip("Seconds the finished picture is held, waiting to be pressed on from, before the " +
                 "round moves itself along. Only used when the stage chain is on.")]
        public float revealStageHold = 6f;

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

        [Header("Round end")]
        [Tooltip("Seconds of slow motion on the klaxon. The round's last moment lands rather than " +
                 "simply stopping — the blade is still turning, the last cut is still finishing, and " +
                 "the world decelerates into the reveal instead of cutting to it.\n\n" +
                 "Short on purpose. Anything past half a second stops reading as emphasis and starts " +
                 "reading as the game having hitched.")]
        public float klaxonHitStop = 0.42f;
        [Tooltip("How far down the clock is pulled at the instant the klaxon sounds.")]
        [Range(0.05f, 1f)] public float klaxonHitStopScale = 0.24f;

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

        /// <summary>
        /// True when this round was asked for by the STAGE SELECT rather than by PLAY: one round,
        /// on its own, with no championship around it.
        ///
        /// Read-only, and taken once in <see cref="Start"/> — see the note there. Public because the
        /// answer is a fact about the round that the HUD, the pause board and the diagnostics all
        /// have a legitimate interest in, and because the alternative is each of them asking
        /// StageLauncher a question whose flag has already been spent by the time they get to it.
        /// </summary>
        public bool SoloRound { get; private set; }

        /// <summary>
        /// True while the reveal has finished its three beats and is parked, waiting to be pressed
        /// on from.
        ///
        /// Exists so the HUD can put the continue prompt on screen for exactly the window in which
        /// the key does something, and not a moment of it earlier. The reveal is a sequence — the
        /// picture, the ghost sweep, the analysis — and a prompt printed over the top of that
        /// sequence tells the player the payoff is something to skip. Printed when the sequence
        /// stops, it tells them the game is waiting for them, which it is.
        ///
        /// A property rather than the HUD timing the reveal itself, because there are four
        /// serialized durations behind that hold and a second copy of the arithmetic in the UI
        /// layer would be free to disagree with the one that actually gates the keypress.
        /// </summary>
        public bool RevealHolding { get; private set; }

        /// <summary>
        /// What pressing on from the reveal will actually do, in words a player can read.
        ///
        /// Deliberately answered by the same two gates <see cref="StageChainNext"/> uses, so the
        /// prompt can never offer a stage this round is not going to play. A prompt that promises
        /// the geese and then delivers the judges is worse than no prompt at all — it is the game
        /// being wrong about itself, in the one place the player is looking for instructions.
        /// </summary>
        public string PressOnLabel
        {
            get
            {
                if (RallyWanted && !Played(GameState.Rally)) return "GOOSE RALLY";
                if (BloomWanted && !Played(GameState.Bloom)) return "BLOOM RUSH";
                return "THE JUDGES";
            }
        }

        void Start()
        {
            // WHAT KIND OF ROUND IS THIS. Before anything at all, because two of the decisions below
            // depend on the answer and one of them is the opening story.
            //
            // Two things can open this scene and they mean different things by it. PLAY on the front
            // page means the CHAMPIONSHIP: the story page, three rounds, the goose rally spliced in
            // at round two, Bloom Rush at round three, a ceremony and an ending. The stage select's
            // first plate means STAGE ONE — one picture, mowed, judged, done — and has to mean that,
            // because the two plates beside it are exactly that and a board whose three rows keep
            // three different promises is not a stage select.
            //
            // The distinction cannot be worked out from this scene: Main.unity is identical on both
            // routes, which is the whole problem. So it is TOLD, by the only thing that knows, and
            // taken here exactly once — StageLauncher.TakeSoloRound clears the static as it hands it
            // over, so a later PLAY cannot inherit a stage select's intent. The answer is latched
            // into a field because the flag lasts one call and the behaviour has to last the round.
            //
            // Nothing below is allowed to read the flag directly. Every consequence hangs off this
            // one field, so "what does solo change" is answerable by searching for one identifier.
            SoloRound = DuckMow.Flow.StageLauncher.TakeSoloRound();
            if (SoloRound)
                Debug.Log("[Duck] stage select: playing Lawn Art as a single round — no opening " +
                          "story, no rally, no Bloom Rush, and the front page after the verdict.");

            // The evening's own bookkeeping, before anything that reads it. The championship knows
            // how many rounds there are; MatchState mirrors it so a stage scene can ask how far in
            // the player is without having to find a Tournament that its own scene does not contain.
            if (tournament != null && tournament.Championship != null)
            {
                MatchState.RoundsTotal = Mathf.Max(1, tournament.Championship.RoundsTotal);
                MatchState.Round = tournament.Championship.RoundNumber;
            }

            // Arriving from the front page, the curtain is already down over this scene and raising
            // it is now this scene's job — the object that closed it was destroyed by the load. See
            // MatchState.CurtainPending, which exists for precisely this handover and nothing else.
            if (MatchState.TakeCurtainPending()) StartCoroutine(RaiseSeam(0.2f));

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
            //
            // A solo round skips it, and the reason is the same one the retry path has: the story is
            // the GAME's opening, not the STAGE's. It is where the duck comes from and what the pond
            // is for — a frame around three rounds of championship — and a player who has clicked
            // one plate on a stage select to play one picture has not asked for the frame. The two
            // arenas have no equivalent in front of them and nobody has ever wanted one there.
            //
            // Note this is a SKIP and not a deletion. The sequence, its panels and its wiring are
            // untouched; PLAY still opens on it exactly as it always has. That matters because the
            // project's standing instruction is that the opening story is not to be BUILT yet, and
            // stepping over one that already exists is a different act from authoring it.
            //
            // The stage's own opening — briefing, preview, count-in, and the controls card the popup
            // stack now puts in front of all three — is deliberately kept. That sequence announces
            // the SUBJECT and hands the player the picture they are about to be marked on; without
            // it a solo stage would drop them onto a lawn with a shape they were never shown, which
            // is not a shorter stage, it is an unplayable one.
            if (playIntro && intro != null && !SoloRound)
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
            // Same for the arena, and more so: it is a LOADED SCENE with this one asleep behind it.
            // A round beginning while that is true would put the mower back on a lawn nobody can see
            // and start the count-in over an arena. Torn down synchronously enough that the rest of
            // BeginRound runs against a scene that is awake again.
            AbandonRally();

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
            _stagesPlayed = 0;
            AerialAmount = 0f;
            _aerialPhase = AerialPhase.Idle;

            SetChalk(_chalkBaseAlpha, 0f, 0f, 1.2f);
            judges?.ResetPanel();
            scoreboard?.ResetBoard();
            tournament?.BeginRound(shape, guideHoldFraction + guideFadeFraction);
            cameraDirector?.SetJudgeFocus(null);

            // Keep the journey's idea of the round in step with the championship's. Read off the
            // CHAMPIONSHIP rather than off RoundNumber, which counts retries — a player who has
            // restarted round one four times has not escalated into the final.
            if (tournament != null && tournament.Championship != null)
                MatchState.Round = tournament.Championship.RoundNumber;

            if (announce) SetState(GameState.Briefing);
            else { cameraDirector?.SnapToChase(); SetState(GameState.Countdown); }
        }

        // ------------------------------------------------------------------ the seams

        /// <summary>
        /// True while a transition coroutine owns the state machine.
        ///
        /// The switch in <see cref="Tick"/> is skipped for the duration. Without it the state the
        /// game is leaving keeps being stepped behind the curtain and can decide, quite reasonably,
        /// to move on again — so a transition into the judging would occasionally arrive to find the
        /// round had already gone somewhere else while the frame was covered.
        /// </summary>
        bool _seam;

        /// <summary>
        /// Change state behind a closed curtain: outro, wipe, then the new beat framed under cover
        /// and revealed once it has drawn itself.
        ///
        /// For the boundaries that are NOT scene loads. Judging and the ceremony are both hard cuts
        /// across ninety metres of lawn, and the code has always said so — see the Judging case. A
        /// cut is still the right edit; what was missing was anything covering it, so the player saw
        /// the camera arrive rather than seeing the panel appear.
        /// </summary>
        void EnterThrough(GameState next, MatchState.Seam seam, string headline, string kicker,
                          float extraHold = 0f)
        {
            if (_seam) return;
            StartCoroutine(SeamInto(next, seam, headline, kicker, extraHold));
        }

        System.Collections.IEnumerator SeamInto(GameState next, MatchState.Seam seam,
                                                string headline, string kicker, float extraHold)
        {
            _seam = true;
            yield return StageSeam.Begin(seam, headline, kicker);
            SetState(next);
            yield return StageSeam.End(extraHold);
            _seam = false;
        }

        /// <summary>Raise a curtain somebody else left down. See SetState's tail.</summary>
        System.Collections.IEnumerator RaiseSeam(float extraHold = 0f)
        {
            yield return StageSeam.End(extraHold);
        }

        /// <summary>
        /// What the sign should say on the way OUT of a stage, which depends on where the round is
        /// going next rather than on which stage is ending.
        ///
        /// Asked by RallyStage and TurfStage as they close the curtain behind themselves. If another
        /// stage follows, that stage re-dresses the sign a frame later and this label is never seen —
        /// so the answer only has to be right for the case where the arena was the last one.
        ///
        /// ---- why the last line stopped saying THE PANEL ----
        ///
        /// A sign is on screen for about a second and it is the worst possible place to teach a
        /// player a word. "THE PANEL" is the English village-fair term for a judging bench, and the
        /// rest of this game's copy was written in that register — the green, the gate, the classes —
        /// which is charming and completely opaque to somebody who has never been to a village fair.
        /// It also named the wrong thing: what is on the other side of this particular curtain is
        /// the LAWN, with the player's finished picture on it, and the judges are the beat AFTER
        /// that. So the board now names the destination and the kicker names what is coming, which
        /// is the job a sign has and the only job it can do in a second.
        /// </summary>
        public void NextSeamLabel(out string headline, out string kicker)
        {
            // The same two gates the chain itself uses, so the board can never name a stage this
            // round is not going to play.
            if (RallyWanted && !Played(GameState.Rally))
            { headline = "GOOSE RALLY"; kicker = StageSeam.RoundKicker(MatchState.Round); return; }
            if (BloomWanted && !Played(GameState.Bloom))
            { headline = "BLOOM RUSH"; kicker = StageSeam.RoundKicker(MatchState.Round); return; }
            headline = "YOUR PICTURE";
            kicker = MatchState.FinalRound ? "THE LAST JUDGING" : "THE JUDGES ARE NEXT";
        }

        /// <summary>Same picture, fresh lawn. The fast path the player will use most.</summary>
        public void RetrySameShape() => BeginRound(_currentShape, false);

        /// <summary>
        /// What pressing on at the end of a round should do.
        ///
        /// The stages in running order, skipping any that are switched off or already played, and
        /// falling through to a fresh picture when there are none left. Returns true when it took
        /// the round somewhere, so the caller knows not to roll a new subject as well.
        ///
        /// Deliberately driven off the SAME enable flags the mid-round splice reads, so there is one
        /// answer to "is stage three in this build" rather than two that can disagree.
        /// </summary>
        bool StageChainNext(GameState from)
        {
            if (!stageChainOnConfirm) return false;

            // Gated on RallyWanted and BloomWanted, not on the bare enable flags.
            //
            // This chain used to read rallyEnabled directly, which quietly made rallyOnRound and
            // bloomOnRound dead fields on the only path the game actually takes: every round played
            // every stage, and the inspector said otherwise. The result was three identical rounds
            // that each took eight minutes, and a championship with no shape to it — the geese
            // arrived in round one, so their arrival was never an event.
            //
            // Read through the same two properties the mid-round splice reads, so there is exactly
            // one answer to "does this round have the geese in it" however the round gets there.
            if (RallyWanted && !Played(GameState.Rally))
            {
                _stageReturn = from;
                SetState(GameState.Rally);
                return true;
            }
            if (BloomWanted && !Played(GameState.Bloom))
            {
                _stageReturn = from;
                SetState(GameState.Bloom);
                return true;
            }
            return false;
        }

        bool Played(GameState stage) => (_stagesPlayed & (1 << (int)stage)) != 0;
        void MarkPlayed(GameState stage) => _stagesPlayed |= 1 << (int)stage;

        /// <summary>
        /// Fold whatever the stages produced into the round's score, for the panel to mark.
        ///
        /// This is the ONLY route a stage result takes into the competition. Bloom Rush does not
        /// announce a winner and the rally does not award anything: each posts a number to its
        /// handoff, this reads them, and the three judges do what they do with every other part of
        /// a round. A stage that declared its own result would be deciding the championship on its
        /// own authority, which is not a thing any part of this game is allowed to do.
        ///
        /// The two are averaged rather than added, so playing both stages is not worth double, and
        /// the whole thing is normalised against an even split: hold your quarter of the arena and
        /// keep your garden intact and you score a half, which is exactly par.
        /// </summary>
        void BankStageResult()
        {
            float total = 0f;
            int count = 0;

            if (TurfHandoff.ResultsReady || TurfHandoff.Results.Length > 0)
            {
                // Share against an even four-way split. A quarter is par and scores 0.5; taking
                // half the arena is a rout and saturates.
                float share = TurfHandoff.PlayerResult.share;
                total += Mathf.Clamp01(Mathf.InverseLerp(0f, 0.5f, share));
                count++;
            }

            if (RallyHandoff.Results.Length > 0)
            {
                total += Mathf.Clamp01(RallyHandoff.PlayerResult.integrity);
                count++;
            }

            var score = LastScore;
            score.stages = count > 0 ? total / count : 0f;
            LastScore = score;
        }

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

            // The reveal's prompt belongs to the reveal and to nothing after it. Cleared here rather
            // than trusted to UpdateReveal's own exit, because half the ways out of that beat do not
            // go through it — a retry, Escape, DebugForceState — and a stale true would leave "press
            // on" printed over the judging.
            RevealHolding = false;

            // The klaxon's deceleration belongs to the klaxon and to nothing after it. Released here
            // rather than trusted to run out, because Time.timeScale is a GLOBAL that outlives this
            // director: a retry or an Escape pressed inside those four hundred milliseconds would
            // otherwise leave the whole game running at a quarter speed with nothing left alive that
            // knows to put it back.
            ReleaseHitStop();

            // Whatever the round does next, the defence phase must not be left holding the camera.
            // Its own tick shuts it down on the way to the reveal; this covers the routes that do
            // not go through that — DebugForceState, the capture tools, and a retry pressed while a
            // goose is still in the air. A phase that kept the camera would leave the next beat being
            // rendered by a lens pointed at a hedge.
            if (from == GameState.Defence) defence?.Abort();
            // Same guarantee for the arena, covering the routes that do not go through the Rally
            // case's own exit — DebugForceState and the capture tools. Leaving it up would render
            // the next beat through a camera in a different scene. A no-op on the normal path, which
            // has already cleared the stage before asking for the reveal.
            if (from == GameState.Rally) AbandonRally();
            if (from == GameState.Bloom) AbandonBloom();

            // Driving stays live through the defence phase, and that is the point of it: the player
            // keeps the machine and the controls they have spent the whole round with, so the phase
            // reads as the round continuing rather than as a minigame that borrowed the duck. The
            // blade is locked instead of the wheels — see MowerController.BladeLocked.
            bool driving = s == GameState.Mowing || s == GameState.Defence
                        || s == GameState.Rally || s == GameState.Bloom;
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
                    // Ending belongs on this list and was missing from it: RestartChampionship is
                    // reached from the ending page on every completed run, and a 0.9 s blend out of
                    // whatever shot the page was covering is the same lurch the rest of this list
                    // exists to prevent. It matters more now that the page is entered directly from
                    // the board rather than by way of the ceremony.
                    bool fromRoundEnd = from == GameState.Verdict || from == GameState.VenueTour ||
                                        from == GameState.Scoreboard || from == GameState.Ceremony ||
                                        from == GameState.Ending;
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
                    // The round LANDS. A brief deceleration on the horn, so the last cut the player
                    // made is the thing the beat is about rather than a frame that happened to be on
                    // screen when the timer expired.
                    if (!SimClock.Scripted && klaxonHitStop > 0.01f) _hitStop = klaxonHitStop;
                    // And the stand reacts, which nothing was doing: the round ended in front of four
                    // hundred spectators and they sat through it in silence.
                    AudioDirector.Instance?.CrowdCheer(0.55f);
                    // And the pad, on the same beat as the horn. This is the moment the player loses
                    // control of the round, so it is worth feeling rather than only hearing.
                    Haptics.Klaxon();
                    // The klaxon can land while the player is still up in the air. Abandon the lift
                    // rather than letting its Falling phase hand driving back during the reveal.
                    _aerialPhase = AerialPhase.Idle;
                    AerialAmount = 0f;
                    // Everyone stops. Rival artworks are settled and marked here so the tour has
                    // finished work to fly over rather than lawns still being cut behind it.
                    tournament?.FlushMasks();
                    break;

                case GameState.Rally:
                {
                    // The picture is already settled and is never touched again: the rally is played
                    // in its own scene on a garden grown from it, and the score the reveal is about
                    // to compute is exactly what the player mowed.
                    tournament?.FlushMasks();
                    // Indicative only — the arena prints it on its board and nothing else reads it.
                    // Not the real mark, because the real mark is what three judges are going to
                    // argue about after the reveal and does not exist yet.
                    float pictureSoFar = target != null && cutMask != null
                        ? target.Evaluate(cutMask, mower.DriftMetres, mower.BoostMetres, BonkCount)
                                .legibility * 30f
                        : 0f;
                    _rally = new RallyStage();
                    StartCoroutine(_rally.Run(RoundNumber, pictureSoFar, _currentShape));
                    break;
                }

                case GameState.Bloom:
                    // The picture is already settled and is never touched again — same contract the
                    // rally makes. Bloom Rush is played in its own scene on its own ground, and the
                    // score the reveal is about to compute is exactly what the player mowed.
                    tournament?.FlushMasks();
                    _bloom = new TurfStage();
                    StartCoroutine(_bloom.Run(RoundNumber));
                    break;

                case GameState.Ending:
                    BeginEnding();
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
                        // The rally is cut from for a stronger version of the same reason: it is not
                        // 420 m away, it is a DIFFERENT SCENE that has just been unloaded, so there is
                        // nothing on screen for a blend to leave from.
                        bool hardCut = from == GameState.Defence || from == GameState.Rally
                                    || from == GameState.Bloom;
                        cameraDirector?.SetMode(CameraMode.Reveal, hardCut ? 0f : 2.4f);
                        if (hardCut) cameraDirector?.SnapToCurrent();
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
                    // The pad editorialises here and nowhere else in the round — applause figure for
                    // a good mark, one flat press for a bad one. The favourable/unfavourable split
                    // is read off the same rank bands the results screen colours itself from
                    // (HUD.cs:710), so the hands and the eyes cannot disagree about how it went.
                    Haptics.Verdict(judges != null && (judges.Rank == "S" || judges.Rank == "A" || judges.Rank == "B"));
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
                    // Then the gardens, for all four. Applied after the pictures rather than passed
                    // into CloseRound, because each number answers to the beat that produced it —
                    // see Tournament.ApplyRallyResults. Does nothing at all in a round with no rally.
                    tournament?.ApplyRallyResults();
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
                    // OFF THE LIVE ROUTE. Nothing in a played round enters this state any more —
                    // the scoreboard goes straight to the ending page; see the Scoreboard case in
                    // Tick for the argument. Kept working rather than gutted, because
                    // DebugJumpToCeremony and the review harness still ask for it by name, and a
                    // state that is reachable by a tool but half-dismantled is a trap for whoever
                    // next runs a capture over it.
                    //
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

            // Whoever left the curtain down, the state that lands under it raises it.
            //
            // One rule rather than a raise written into the reveal, another into the judging and a
            // third into whatever gets added next — which is how a build ends up with exactly one
            // path that forgets, and a player stuck looking at leaves. The three stage states are
            // excluded because each of them closes its own curtain a frame later and a raise here
            // would flicker the arena's load into view between the two.
            bool ownsItsOwnSeam = s == GameState.Rally || s == GameState.Bloom || s == GameState.Defence;
            if (StageSeam.InProgress && !_seam && !ownsItsOwnSeam && isActiveAndEnabled)
                StartCoroutine(RaiseSeam());

            OnStateChanged?.Invoke(s);
        }

        void Update()
        {
            if (SimClock.Scripted) return;
            UpdateHitStop();
            Tick(Time.deltaTime);
        }

        /// <summary>Seconds of round-end slow motion still owed. Zero the rest of the time.</summary>
        float _hitStop;

        /// <summary>
        /// The round's last moment, decelerating.
        ///
        /// Driven on the UNSCALED clock, which is the whole trick: the thing being slowed cannot be
        /// the thing measuring how long to slow it for, or the effect takes four times as long as it
        /// was asked to and the klaxon beat runs over the top of the reveal.
        ///
        /// Deliberately not started when <see cref="SimClock.Scripted"/> is set. The capture tools
        /// step this director on a fixed clock to produce reproducible frames, and a global
        /// timeScale change underneath a run like that makes every sheet it produces a lie.
        /// </summary>
        void UpdateHitStop()
        {
            if (_hitStop <= 0f) return;

            _hitStop -= Time.unscaledDeltaTime;
            if (_hitStop <= 0f)
            {
                _hitStop = 0f;
                Time.timeScale = 1f;
                return;
            }

            // Squared, so the world is at its slowest for barely an instant and spends most of the
            // beat coming back. A linear ramp reads as a stutter; this reads as a landing.
            float k = 1f - Mathf.Clamp01(_hitStop / Mathf.Max(klaxonHitStop, 0.01f));
            Time.timeScale = Mathf.Lerp(Mathf.Clamp(klaxonHitStopScale, 0.05f, 1f), 1f, k * k);
        }

        /// <summary>Give the clock back, whatever was happening to it. Called on every state change.</summary>
        void ReleaseHitStop()
        {
            if (_hitStop <= 0f) return;
            _hitStop = 0f;
            Time.timeScale = 1f;
        }

        void OnDisable() => ReleaseHitStop();

        /// <summary>The frame this director last saw anything in the popup stack. See <see cref="UpdateConfirmGate"/>.</summary>
        int _popupFrame = -99;

        /// <summary>
        /// The confirm edge, with the one that dismissed a popup taken out of it.
        ///
        /// Everything in <see cref="Tick"/> and <see cref="UpdateReveal"/> reads THIS and nothing
        /// reads <see cref="InputReader.AnyConfirmPressed"/> directly any more. A field rather than
        /// a parameter because UpdateReveal is called from inside Tick and would otherwise need the
        /// value threaded through it; recomputed at the top of every Tick, so it is never stale.
        /// </summary>
        bool _confirm;

        /// <summary>
        /// How many frames a confirm stays swallowed after the popup stack empties. See below —
        /// the answer is exactly two, and it is arithmetic rather than taste.
        /// </summary>
        const int ConfirmBleedFrames = 2;

        /// <summary>
        /// Swallow the keypress that closed a popup, so it does not also press a button behind it.
        ///
        /// ---- the bug, frame by frame ----
        ///
        /// The controls card is dismissed with ENTER or SPACE. Dismissing it also skipped the
        /// briefing — the player read the controls, pressed START, and the announcement of the
        /// subject they are about to be marked on flashed past. One keypress, two actions, and the
        /// second one invisible.
        ///
        /// Three separate pieces of timing conspire, and none of them is wrong on its own:
        ///
        ///   * PopupStack runs its beat from the START of the player loop's Update phase, ahead of
        ///     every MonoBehaviour including this one, and the card reads the RAW device.
        ///   * InputReader latches AnyConfirmPressed in its own Update at execution order 0.
        ///   * This director runs at -10, so it always reads the PREVIOUS frame's latch. PopupStack
        ///     says so itself, at length, in the note on <see cref="DuckMow.UI.PopupStack.Consumed"/>.
        ///
        /// So on frame N the stack pops the card and the stack is empty again before this director
        /// has looked at anything; InputReader latches the edge later that same frame; and on frame
        /// N+1 the Briefing case reads a live AnyConfirmPressed and does what it is supposed to do
        /// with one. Every part behaves correctly and the round still loses a beat.
        ///
        /// PopupStack.Consumed does not cover it. That flag is about ESCAPE, is set only on frames
        /// the stack actually answered Escape, and is cleared at the top of the very next beat — by
        /// the time this stale confirm arrives it is long gone. The two mechanisms are solving the
        /// same shape of problem for two different keys.
        ///
        /// ---- the fix, and why two ----
        ///
        /// The last frame this director can OBSERVE the stack occupied is N-1, because on frame N
        /// the pop has already happened by the time it looks. The stale edge reaches it on N+1.
        /// That is two frames later, so two frames is what gets swallowed — 33 ms at sixty, which no
        /// player can press twice inside, and a genuine second press on N+2 is honoured normally.
        ///
        /// Fixed HERE rather than in ControlsPrimer deliberately. The card is only the loudest
        /// instance: closing the PAUSE board by pressing ENTER on RESUME bleeds the identical edge,
        /// and at the reveal that edge presses on to the judges — the player pauses, resumes, and
        /// the picture they were looking at is gone. Any popup this project ever adds with a confirm
        /// on it inherits the same fault. A guard on the RECEIVING side covers all of them at once;
        /// a guard inside one popup covers that popup, and is a thing the next author has to know to
        /// copy.
        ///
        /// Only the confirm edge is gated. RetryPressed is R, which dismisses no popup in this game,
        /// and swallowing a key nothing is pressing would be superstition. (N was the other one, and
        /// this director no longer reads it at all — see the verdict case.)
        /// </summary>
        void UpdateConfirmGate(InputReader input)
        {
            // Fully qualified for the reason the Escape block below gives: this file has
            // `using UnityEngine;`, which puts UnityEngine.UI in scope under the same short name.
            if (DuckMow.UI.PopupStack.Any) _popupFrame = Time.frameCount;

            _confirm = input != null && input.AnyConfirmPressed &&
                       Time.frameCount - _popupFrame > ConfirmBleedFrames;
        }

        public void Tick(float dt)
        {
            _stateTime += dt;

            UpdateTicks++;
            var input = InputReader.Instance;

            // Before every early return below, so the gate keeps watching the stack on the frames
            // this method does nothing else with — a popup that is up for the whole of a covered
            // seam would otherwise never be seen at all.
            UpdateConfirmGate(input);

            // Escape opens the pause menu, from any state except the opening — during the intro there
            // is nothing on screen the player would be trying to get out of, and the cutscene has its
            // own skip. Checked before the state switch so it works even in states that accept no
            // other input.
            //
            // It used to walk straight out to the front page from here, which meant one mistimed key
            // threw away a round in progress with no confirmation and nothing to press to get it
            // back. The exit still exists and is still BackToMenu — it is a button inside the pause
            // popup now, so leaving costs a deliberate second choice instead of an accidental first
            // one. Menu.unity still has to be in the build settings for that button to do anything;
            // DuckMenuBuilder.RegisterBuildScenes is what guarantees it.
            //
            // Three guards, and each is load bearing:
            //   * Consumed — the popup stack polls this same Escape latch one beat earlier in the
            //     player loop. On the frame it closes the LAST popup the stack is empty again by the
            //     time this runs, so without the flag that frame would reopen the menu the player
            //     just shut and every close would be invisible.
            //   * Any — while anything is open the stack owns Escape, including popups that refuse
            //     to close on it. Stacking a second pause menu on top of a confirmation is not a
            //     thing anybody wants.
            //   * PauseFactory — the popup registers itself there on startup rather than being named
            //     here, so this file and the popup do not have to know each other's types. Null means
            //     nothing registered and Escape does nothing at all: a dead key, not a crash.
            if (input != null && input.MenuPressed && State != GameState.Intro)
            {
                // Fully qualified: this file has `using UnityEngine;`, which puts UnityEngine.UI in
                // scope under the same short name, so a bare `UI.PopupStack` resolves against the
                // wrong namespace and does not compile.
                if (!DuckMow.UI.PopupStack.Consumed && !DuckMow.UI.PopupStack.Any &&
                    DuckMow.UI.PopupStack.PauseFactory != null)
                    DuckMow.UI.PopupStack.Push(DuckMow.UI.PopupStack.PauseFactory());
                return;
            }

            // A transition owns the state machine while it is running. Everything below decides what
            // to do NEXT, and behind a covered frame the answer is always "wait": a beat that moved
            // on under the curtain would be revealed already finished.
            if (_seam) return;

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
                    // Let an impatient player skip the announcement — but only an impatient player.
                    // Read through the gate, not off the reader: the controls card is dismissed with
                    // the same key one frame earlier and used to skip this beat on its way out. See
                    // UpdateConfirmGate.
                    if (_stateTime >= briefingDuration || _confirm)
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
                    {
                        // With the chain on, the stages are played off the END of the round and the
                        // klaxon goes straight to the reveal. The two paths are exclusive on
                        // purpose: a stage entered from both would run twice in one round.
                        if (!stageChainOnConfirm && RallyWanted) SetState(GameState.Rally);
                        else if (!stageChainOnConfirm && BloomWanted) SetState(GameState.Bloom);
                        else SetState(defencePhaseEnabled ? GameState.Defence : GameState.Reveal);
                    }
                    break;

                case GameState.Rally:
                    // Nothing to step. The arena is a loaded scene running its own clock, and this
                    // beat exists only to wait for it — which is the point of the arena being a
                    // scene rather than a phase: it does not need the round's permission to tick.
                    if (_rally == null || _rally.Finished)
                    {
                        if (_rally != null && _rally.Failed)
                            Debug.LogWarning("[Duck] the rally arena did not open; carrying on " +
                                             "without it.");
                        _rally = null;
                        MarkPlayed(GameState.Rally);
                        BankStageResult();
                        // Straight into the next stage if there is one, and only then onward. Going
                        // to a single remembered state instead would have skipped every stage after
                        // the first, which is the whole reason the chain is re-consulted here.
                        if (!StageChainNext(_stageReturn))
                            SetState(stageChainOnConfirm ? _stageReturn
                                   : BloomWanted ? GameState.Bloom : GameState.Reveal);
                    }
                    break;

                case GameState.Bloom:
                    // Nothing to step, for the same reason the rally has nothing to step: the arena
                    // is a loaded scene running its own clock, and this beat exists only to wait
                    // for it.
                    if (_bloom == null || _bloom.Finished)
                    {
                        if (_bloom != null && _bloom.Failed)
                            Debug.LogWarning("[Duck] the Bloom Rush arena did not open; carrying " +
                                             "on without it.");
                        _bloom = null;
                        MarkPlayed(GameState.Bloom);
                        BankStageResult();
                        if (!StageChainNext(_stageReturn))
                            SetState(stageChainOnConfirm ? _stageReturn : GameState.Reveal);
                    }
                    break;

                case GameState.Defence:
                    // Stepped before the finish test, so the phase gets its last frame — putting the
                    // machine back and handing the clock over — before the reveal is entered.
                    defence?.Tick(dt);
                    // Budgeted, not trusted — the same rule the opening story is under. A phase that
                    // never reports itself over would strand the round one beat short of the reveal,
                    // with a finished picture nothing on screen is able to show.
                    //
                    // Read off the PHASE rather than hard-coded, which it was: a flat 30 s, sized against a
                    // twelve-second raid. The raid now runs to a sixty-second budget and ends when a garden
                    // falls, so a fixed 30 would have guillotined a perfectly healthy match at the halfway
                    // point — and the warning below would have blamed the phase for it. GooseDefence adds up
                    // its own beats in WorstCaseSeconds so the two can never drift apart again.
                    float allowed = defence != null ? defence.WorstCaseSeconds : 30f;
                    if (defence == null || defence.Finished || _stateTime > allowed)
                    {
                        if (defence != null && !defence.Finished)
                            Debug.LogWarning($"[Duck] defence phase did not finish within " +
                                             $"{_stateTime:0.0}s of its {allowed:0.0}s allowance; " +
                                             $"going to the reveal without it.");
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
                    // A SOLO STAGE ENDS HERE, and this is the one place in the file where that is a
                    // decision rather than a consequence. Worth arguing properly.
                    //
                    // What comes next in a championship is the venue tour and the plaza board, and
                    // neither of them is about the player's picture: the tour flies three RIVALS'
                    // finished work, and the board is the standings of a three-round table with one
                    // row filled in. Shown after a single stage they would be a tour of strangers
                    // followed by a league nobody is playing in. The ceremony and the ending pages
                    // are worse than irrelevant — they are the payoff for winning a CHAMPIONSHIP,
                    // and firing them off one isolated round would be the game congratulating the
                    // player for something they did not do. So none of it runs.
                    //
                    // But the round is played to its own end first, and that is the other half of
                    // the decision. Everything up to this point — the klaxon, the reveal, the ghost
                    // sweep, the panel, the marks, the verdict — belongs to the PICTURE, and it is
                    // the whole reason to play stage one. A stage select that dropped the player at
                    // the horn, or at the scoreboard with the judging cut off, would be showing them
                    // most of a stage and calling it the stage. They mowed a heart; three animals
                    // are entitled to have an opinion about it before anyone goes anywhere.
                    //
                    // And then the front page, because there is nowhere else honest to go. The stage
                    // is over, the stage select is one scene away, and the two arenas end the same
                    // way — StageLauncher.ReturnToMenu, and BackToMenu underneath it, which is the
                    // exit that also drops the stage handles and puts the clock back.
                    if (SoloRound)
                    {
                        // The same skip contract the tour branch offers below, so pressing on out of
                        // a verdict means the same thing whichever kind of round produced it. The
                        // 1.2 s floor is there for the same reason too: the verdict lands ON a
                        // keypress often enough that accepting one immediately would make the
                        // portrait a single frame.
                        bool leave = _stateTime > 1.2f && _confirm;
                        if (_stateTime >= SoloVerdictHold || leave) BackToMenu();
                        break;
                    }

                    bool tourAhead = tournament != null && tournament.rivals.Length > 0;
                    if (tourAhead)
                    {
                        bool skip = _stateTime > 1.2f && _confirm;
                        if (_stateTime >= verdictHold || skip) SetState(GameState.VenueTour);
                        break;
                    }
                    // No rivals, so no tour and no board — a degenerate venue, and the only one of
                    // these three branches a shipping scene never takes.
                    //
                    // This used to offer R for the same picture and N for A NEW ONE, and N is the
                    // key that has been removed. "New picture" was a third meaning for the end of a
                    // round on top of "retry" and "carry on", it wiped the championship to do it,
                    // and it was the loudest thing in the only prompt stage one ever printed. What
                    // is left is the rule the whole game now follows: SPACE CONTINUES. With nothing
                    // to continue TO, continuing means the front page — the same answer the solo
                    // branch above gives, for the same reason, so a player who learns what the key
                    // does in one kind of round has learned it for all of them.
                    if (input != null)
                    {
                        if (input.RetryPressed) RetrySameShape();
                        else if (_confirm && _stateTime > 1.2f) BackToMenu();
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
                        // ---- straight to the ending page; the pageant in between is gone ----
                        //
                        // This used to enter GameState.Ceremony: a four-beat prize-giving — the
                        // bench applauding, a push-in on the trophy, the board counting up, the
                        // duck under a title card, the venue taken to night with fireworks — and
                        // only then the ending. It has been cut on the player's own reading of it,
                        // and the reading holds up: the pageant is a SECOND ending, playing before
                        // the real one, made of beats the player has already been shown. The bench
                        // has just marked them, the board behind the curtain is the board they were
                        // looking at a second ago, and the portrait is the same portrait the verdict
                        // ends on every single round. Fifteen seconds of ceremony for information
                        // the scoreboard already delivered, standing between the player and the two
                        // authored comic pages that are the actual payoff.
                        //
                        // What survives is what the ceremony was borrowing anyway: the loudest
                        // curtain in the game — confetti, brass, the lights up — because THIS is the
                        // moment the evening ends, and the board deserves to be got off with a bang
                        // whatever comes next. The extra hold stays too: the ending page builds its
                        // first panel on its first frame, and the curtain should not lift on that
                        // happening.
                        //
                        // The sign earns its place by the same rule that stripped the words off the
                        // two menu boards: it NAMES something a wipe cannot say on its own, and it
                        // is genuinely new — the game is over, and here is how it turned out. It is
                        // also the one sign in the game that arrives exactly once a session.
                        //
                        // VictoryCeremony and CeremonyNight are deliberately left in the project and
                        // GameState.Ceremony is deliberately still a working state. The enum is
                        // append-only (GameState's own note explains that reordering silently
                        // repoints serialized references and every capture ordinal), the state is
                        // still reachable through DebugJumpToCeremony, and the review tooling still
                        // asks for it by name — DuckSimulator and DuckCurtainShots both wait on it.
                        // Deleting the class would break the harness to remove code that costs
                        // nothing to leave sitting in an unreachable case.
                        if (settled)
                            EnterThrough(GameState.Ending, MatchState.Seam.IntoFinale,
                                         "HOW IT ENDED",
                                         tournament.Championship.PlayerIsChampion
                                             ? "YOU WON THE DAY" : "THE FINAL RESULT",
                                         extraHold: 0.45f);
                        break;
                    }

                    if (input != null && _stateTime > 2.5f)
                    {
                        // R restarts the whole championship rather than re-mowing this round. The
                        // round's points are already banked by the time this board is on screen, so
                        // a same-picture retry would let one round be counted twice.
                        // SPACE CONTINUES, and at the board what comes next is the next round of the
                        // championship — a fresh subject on a fresh lawn, with the points so far
                        // still standing. That is a different act from the NEW PICTURE that has been
                        // removed at the verdict, which threw the points away; this one is simply
                        // the evening carrying on, and it is the only thing this board can do.
                        //
                        // N used to be a second, unadvertised key for it. Dropped so that the whole
                        // game has one continue key and the binding can be retired.
                        if (input.RetryPressed) RestartChampionship();
                        else if (_confirm) NextShape();
                    }
                    break;
                }

                // Only ever entered by DebugJumpToCeremony and the review harness now — see the
                // SetState case. Left intact so both of those still work.
                case GameState.Ceremony:
                    _ceremony?.Tick(dt);
                    if (input != null && _ceremony != null && _ceremony.PromptUp &&
                        (input.RetryPressed || _confirm))
                    {
                        // The ceremony hands the trophy over; the ending says what it MEANT. R still
                        // skips straight to a fresh championship for anyone who has seen it.
                        if (input.RetryPressed || !HasEnding) RestartChampionship();
                        else SetState(GameState.Ending);
                    }
                    break;

                case GameState.Ending:
                    _ending?.Tick(dt);
                    // Budgeted, not trusted — the same rule the opening story is under. A page that
                    // never reports itself finished would strand the player on the last thing the
                    // game ever shows them, with no way back.
                    if (_ending == null || _ending.Finished || _stateTime >= _endingBudget)
                    {
                        if (_ending != null && !_ending.Finished)
                        {
                            Debug.LogWarning($"[Duck] the ending page did not finish within " +
                                             $"{_endingBudget:0.0}s; returning to the menu.");
                            _ending.Hide();
                        }
                        if (input != null && (_confirm || input.RetryPressed || _ending == null))
                            RestartChampionship();
                    }
                    break;
            }
        }

        /// <summary>
        /// The trip out to the rally arena, while it is happening. Null the rest of the time — the
        /// stage owns a loaded scene and a list of things it sent to sleep, and nothing that holds
        /// state like that should outlive the beat it belongs to.
        /// </summary>
        RallyStage _rally;

        /// <summary>
        /// Which stages this round has already played, as a bitmask over <see cref="GameState"/>
        /// values. Cleared by BeginRound.
        ///
        /// It exists so the end-of-round prompt can be pressed repeatedly and walk FORWARD through
        /// the running order rather than re-entering whichever stage is first in the list. Without
        /// it, space at the verdict sends the player to the goose rally, and then to the goose rally
        /// again, for ever.
        /// </summary>
        int _stagesPlayed;

        /// <summary>
        /// Where a chained stage hands control back to. Reveal when a stage was spliced into the
        /// middle of the round; the board the player pressed on FROM when it was chained off the
        /// end. Getting this wrong replays the reveal of a picture that has already been marked.
        /// </summary>
        GameState _stageReturn = GameState.Reveal;

        /// <summary>
        /// The trip out to Bloom Rush, while it is happening. Null the rest of the time, for the
        /// same reason <see cref="_rally"/> is: the stage owns a loaded scene and a list of things
        /// it sent to sleep, and nothing holding state like that should outlive its beat.
        /// </summary>
        TurfStage _bloom;

        /// <summary>
        /// Does this round finish at the arena?
        ///
        /// Stage two of the championship, and only stage two. Every round having the geese would
        /// make them furniture rather than an event; putting them last would claim a finale the
        /// mode does not own. Read off the CHAMPIONSHIP's round number rather than this director's,
        /// because <see cref="RoundNumber"/> counts retries — a player who restarted the first round
        /// four times would otherwise arrive at the arena in the middle of round one.
        ///
        /// A solo round never has it, and this property and <see cref="BloomWanted"/> are where that
        /// is decided for the whole class. They are the ONLY two questions the round asks about the
        /// later stages — the klaxon's mid-round splice, <see cref="StageChainNext"/> and
        /// <see cref="NextSeamLabel"/> all read them and nothing reads the enable flags behind them,
        /// which is exactly why that discipline was introduced (see StageChainNext's note about
        /// three identical eight-minute rounds). So one answer here switches off every route into
        /// the arenas AND stops the sign above the curtain naming a stage that is not coming, and
        /// there is no fourth place a stage could sneak in from.
        ///
        /// Deliberately NOT done by writing <c>rallyEnabled = false</c> at startup. Those are
        /// serialized fields on a component in Main.unity: setting them at runtime in the editor
        /// leaves the inspector showing the stage as off, and one absent-minded Apply later the
        /// championship has permanently lost round two in a scene file, with nothing in the diff to
        /// explain it. What a solo round changes is what the round WANTS, not what the build HAS.
        /// </summary>
        bool RallyWanted
        {
            get
            {
                if (SoloRound) return false;
                if (!rallyEnabled) return false;
                if (rallyOnRound <= 0) return true;
                var champ = tournament != null ? tournament.Championship : null;
                return champ == null || champ.RoundNumber == rallyOnRound;
            }
        }

        /// <summary>
        /// Tear the arena down from underneath whatever is happening.
        ///
        /// Two things have to be true afterwards and neither is optional. The scene must be unloaded,
        /// or the next round is mowed on a lawn with an arena floating over it. And Time.timeScale
        /// must be back to one — the rally holds it at two percent through a hit stop, and that is a
        /// global that outlives a scene change, so Escape pressed on the frame a goose connects would
        /// otherwise drop the player onto the menu in slow motion. The rally's own Abort does the
        /// second; the stage does the first.
        /// </summary>
        void AbandonRally()
        {
            if (_rally == null) return;
            // Cover the tear-down before any of it happens, and cover it INSTANTLY — there is no
            // outro to play out of when the player has just pressed Escape or R, and a half-unloaded
            // arena with a mower being teleported through it is the single ugliest frame this game
            // can produce. The cover also tells RallyStage.Leave that the seam is already owned, so
            // it does not start a second, polite one on top of this one.
            StageSeam.CoverNow(MatchState.Seam.StageToRound);
            StopAllCoroutines();
            // StopAllCoroutines has just killed any transition this director had in flight, so the
            // flag it was holding has to be cleared by hand or the state machine never ticks again.
            _seam = false;
            var stage = _rally;
            _rally = null;
            // Awake first, unloaded second. The caller's very next act is usually to set up a round
            // against Main's mower, mask and camera — see RallyStage.WakeNow.
            stage.WakeNow();
            StartCoroutine(stage.AbortNow());
            RallyHandoff.Clear();
        }

        /// <summary>
        /// Does this round finish at Bloom Rush?
        ///
        /// Stage three. Read off the CHAMPIONSHIP's round number rather than this director's,
        /// because <see cref="RoundNumber"/> counts retries — a player who restarted the first round
        /// four times would otherwise arrive at the arena in the middle of round one. Same rule the
        /// rally is under, for the same reason.
        ///
        /// And a solo round never has it, for the reason set out at length on <see cref="RallyWanted"/>
        /// — these two properties are the whole of that decision.
        /// </summary>
        bool BloomWanted
        {
            get
            {
                if (SoloRound) return false;
                if (!bloomEnabled) return false;
                if (bloomOnRound <= 0) return true;
                var champ = tournament != null ? tournament.Championship : null;
                return champ == null || champ.RoundNumber == bloomOnRound;
            }
        }

        /// <summary>
        /// Tear Bloom Rush down from underneath whatever is happening.
        ///
        /// The same two guarantees <see cref="AbandonRally"/> makes, and the second is the one worth
        /// naming: the arena must be unloaded or the next round is mowed on a lawn with a hedge ring
        /// floating over it. Bloom Rush never touches Time.timeScale, so there is no warp to undo.
        /// </summary>
        void AbandonBloom()
        {
            if (_bloom == null) return;
            // Instant cover before the tear-down, and the flag cleared by hand after
            // StopAllCoroutines — see AbandonRally, which does both for the same two reasons.
            StageSeam.CoverNow(MatchState.Seam.StageToRound);
            StopAllCoroutines();
            _seam = false;
            var stage = _bloom;
            _bloom = null;
            // Awake first, unloaded second — see TurfStage.WakeNow. The caller's very next act is
            // usually to set up a round against Main's mower, mask and camera.
            stage.WakeNow();
            StartCoroutine(stage.AbortNow());
            TurfHandoff.Clear();
        }

        ComicSequence _ending;
        float _endingBudget = 60f;

        /// <summary>True when there is an ending page to play at all.</summary>
        bool HasEnding => endingWin != null || endingLose != null;

        /// <summary>
        /// Which ending the duck earned, decided on the CHAMPIONSHIP TOTAL and nothing else.
        ///
        /// Not the last round, not the arena, not who covered the most ground in Bloom Rush. Three
        /// rounds of marks from three judges, added up — which is the only number in the game that
        /// has seen everything the player did. A stage that ended well after two bad rounds does not
        /// buy the pond, and that is the point of totalling it.
        /// </summary>
        bool WonChampionship
        {
            get
            {
                var champ = tournament != null ? tournament.Championship : null;
                if (champ == null) return false;
                return champ.PlayerIsChampion;
            }
        }

        void BeginEnding()
        {
            bool won = WonChampionship;
            _ending = won ? endingWin : endingLose;
            // Falls back to whichever page exists, so a build with only one of them authored still
            // ends rather than hanging on a black screen.
            if (_ending == null) _ending = won ? endingLose : endingWin;

            if (_ending == null)
            {
                Debug.LogWarning("[Duck] no ending page is wired; going straight back to the menu.");
                return;
            }

            _endingBudget = Mathf.Clamp(_ending.TotalSeconds + 8f, 10f, 180f);
            _ending.Begin();
            Debug.Log($"[Duck] ending: {(won ? "the pond is bought" : "the pond is sold")} " +
                      $"on {(tournament != null ? tournament.Championship.PlayerPoints : 0)} points.");
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
            StartCoroutine(LeaveToMenu());
        }

        System.Collections.IEnumerator LeaveToMenu()
        {
            // Cover BEFORE the tear-down, so the arena unloading and the mower being put back are
            // both behind the curtain rather than the last thing the player sees of the round.
            //
            // The curtain is deliberately left DOWN across the load: it is DontDestroyOnLoad, so it
            // survives into Menu.unity, and Menu raises it on its own first frame. That is the whole
            // trick that makes a full scene swap invisible — one object is alive on both sides of it
            // and is the only thing on screen while it happens.
            // No sign on this one, and that is the whole of the change.
            //
            // It used to read THE GREEN / BACK TO THE GATE, which is two pieces of English
            // village-fair vocabulary in a board the player has under a second to read, describing
            // the one journey in the game they cannot possibly be confused about: they just chose to
            // leave. A sign earns its place when it NAMES something that could not otherwise be
            // inferred — GOOSE RALLY does, BLOOM RUSH does. "You are going where you asked to go"
            // does not, and printing it is a caption over the world rather than part of it.
            //
            // Curtain supports this properly rather than by accident: an empty headline sets
            // _signWanted false, the sign group is held at zero alpha and the plate is never drawn.
            // What is left is the wipe itself, which was always the part carrying the feeling.
            yield return StageSeam.Begin(MatchState.Seam.RoundToMenu);

            // Deliberately NOT AbandonRally/AbandonBloom. Both call StopAllCoroutines, which would
            // kill this coroutine halfway through its own transition and leave the game covered by a
            // curtain with nothing behind it loading. A single-mode load unloads every additively
            // loaded scene by itself, so the arena needs no help going away.
            //
            // What a load does NOT undo has to be done by hand, and it is all globals and statics:
            _rally = null;
            _bloom = null;
            // Shut the defence phase down before the load rather than leaving it to OnDisable. The
            // load is ASYNCHRONOUS, so this scene keeps ticking for a while yet — and the phase holds
            // Time.timeScale down to two percent during a hit stop, which is a global that outlives a
            // scene change. Escape pressed on the frame a goose connects would otherwise drop the
            // player onto the menu in slow motion.
            defence?.Abort();
            // Belt and braces on the same hazard. Two different stages warp the clock and only one of
            // them is asked to put it back above; arriving at the front page in slow motion is a bug
            // with no recovery short of a restart.
            Time.timeScale = 1f;
            RallyHandoff.Clear();
            TurfHandoff.Clear();
            DefenceHandoff.Clear();
            // The session is over. The next run through starts quiet again rather than opening on
            // the escalation this one ended at.
            MatchState.BeginSession();

            // The front page raises what this coroutine will not be alive to raise.
            MatchState.CurtainPending = true;
            MatchState.PendingHeadline = null;
            MatchState.PendingKicker = null;
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

        /// <summary>
        /// Seconds a SOLO stage's verdict holds before the game returns to the front page.
        ///
        /// Longer than <see cref="verdictHold"/> because it is doing a different job. In a
        /// championship the verdict is a PAUSE — the marks land and the venue opens up a moment
        /// later, so four seconds is a breath between two things. In a solo stage it is the ENDING,
        /// the last thing the player sees before the curtain, and an ending that leaves after four
        /// seconds reads as the game having got bored of its own result.
        ///
        /// A const rather than a serialized field, and that is the point of it being here at all.
        /// Adding a public field would put a new row in GameDirector's inspector, in a scene this
        /// change is under instructions not to touch, to hold one number that no round played from
        /// the front page will ever read. Pacing that belongs to one code path can live in the code.
        /// </summary>
        const float SoloVerdictHold = 7f;
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

            if (t < analysisEnd) return;

            // The picture is finished and on screen. What happens next is the rest of the ROUND.
            //
            // With the chain on, the reveal holds here and waits to be pressed on from — to the
            // goose rally, then to Bloom Rush, and only when there is nothing left to play, to the
            // judges. The hold is what makes the running order a sequence the player walks through
            // rather than four things that happen at them; the timeout behind it is so an unattended
            // capture run still completes.
            if (!stageChainOnConfirm) { SetState(GameState.Judging); return; }

            // The finished picture is now a CARD waiting to be pressed on from, and the HUD has to
            // be able to say so. It was silent through this entire hold — six seconds of a player
            // looking at their own work with no indication that the game was waiting for them, and
            // the only prompt in the whole round advertised a key that started a different picture.
            // See HUD.UpdateOutro, which reads this and prints what the key actually does.
            RevealHolding = true;

            // Through the gate, not off the reader — see UpdateConfirmGate. This beat is the one the
            // pause board bleeds into hardest: ENTER on RESUME would press on from the finished
            // picture the player had paused to look at, and take the reveal away with it. Only
            // called from Tick, so _confirm is this frame's answer.
            if (!_confirm && t < analysisEnd + revealStageHold) return;

            RevealHolding = false;

            // ---- why there is no curtain over the way into the judging ----
            //
            // There used to be one, carrying a board that read THE PANEL, and it was wrong for a
            // reason worth writing down because it will be tempting to put back.
            //
            // A curtain in this game means A JOURNEY. Every other seam covers a scene being loaded
            // or unloaded, or the machine being teleported four hundred metres — StageSeam's own
            // class note is explicit that the middle of a boundary is the part that must not be
            // seen. This boundary loads NOTHING. It is a state change inside one scene, on one lawn,
            // with the bench already standing where it has stood all round. Wiping the frame and
            // sliding a sign across it told the player they were being taken somewhere, and then
            // put them back down in the venue they never left. That is what read as odd.
            //
            // What the curtain was covering is a hard camera cut — ninety metres straight down to a
            // ground-level bench shot, SetMode(Judges, 0f) plus SnapToCurrent; see the Judging case,
            // which chose a cut over a blend deliberately and explains why a blend looks worse. A
            // cut is a legitimate edit and needs no cover: television does exactly this. The proof
            // that it is safe is one beat further on — Judging to Verdict is the SAME hard cut and
            // it also PARKS THE MOWER on the portrait mark, and that transition has shipped
            // uncovered since the beginning without anybody calling it a fault.
            //
            // Nothing else moves here. The chalk overlay is zeroed on the same frame as the cut, so
            // it is not a flicker — it is a frame that changed completely, and one of the things
            // that changed was the overlay. And BeginJudging only sets numbers and starts the lean-
            // in from zero; the judges are already in the world and are not built on this frame.
            if (!StageChainNext(GameState.Judging)) SetState(GameState.Judging);
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
