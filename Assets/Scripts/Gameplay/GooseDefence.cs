using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuckMow
{
    /// <summary>
    /// GREYBOX — the goose rally. Capsules and coloured boxes on purpose.
    ///
    /// The round ends, the duck drives into a small arena beside its own flower garden, and a goose
    /// comes at the beds. Hit it and it goes back over the way it came — and comes back faster. The
    /// rally escalates until somebody misses, and a miss flattens flowerbeds that do not grow back.
    ///
    /// THE DESIGN, in the order the decisions matter:
    ///
    ///   * It runs on the SAME chase camera as the mowing, driving the SAME mower. No camera of its
    ///     own, no control scheme of its own — the phase is a continuation of the round rather than a
    ///     minigame. The framing work lives in CameraDirector as modifiers on Chase; see
    ///     SetDefenceSubject there.
    ///   * The three tiers fall out of ONE geometric rule instead of three hand-tuned timers: how
    ///     close the goose is when you honk. See <see cref="Tier"/>.
    ///   * Escalation is the goose getting faster. Because the tiers are radii, a faster goose spends
    ///     less time inside each of them, so every window tightens by itself as the rally grows.
    ///     Nothing schedules the difficulty — the rally IS the difficulty.
    ///   * What is defended is a garden planted along the outline the player actually mowed, so the
    ///     stake is their own work rather than a garden. See DefenceArena.PlantFromPicture.
    ///
    /// On probation, and built to be cut cheaply: two files, everything created at runtime, no
    /// prefab, no material asset, no mesh asset, no scene reference, no new scene, and it does not
    /// run at all unless <see cref="GameDirector.defencePhaseEnabled"/> is ticked.
    /// </summary>
    public class GooseDefence : MonoBehaviour
    {
        public enum Phase { Idle, Brief, Live, Settle, Awarding, Done }

        /// <summary>
        /// How well the goose was met, decided by how far away it was when the horn went.
        ///
        /// One radius set, three tiers, and the timing windows are a CONSEQUENCE rather than a setting:
        /// a goose closing at speed v is inside radius r for 2r/v seconds.
        ///
        /// The radii grew with the pitch, and the reason is the best thing in this phase. Closing speed
        /// is the goose PLUS the mower's own approach, so on open ground the player sets their own
        /// difficulty. Standing off and letting it come, at the opening dive speed, the windows are
        /// 0.94 / 0.54 / 0.28 s — generous. Charging it head-on at 10 m/s they collapse to
        /// 0.59 / 0.34 / 0.18 s. Driving at the goose is faster and more aggressive AND cuts your own
        /// window by a third. Nothing implements that trade; it falls out of measuring the tier as a
        /// distance on a pitch you are free to move around, which is exactly why the small box had to go.
        ///
        /// Escalation rides on top: every exchange raises v, so by the fifth the head-on windows are
        /// 0.38 / 0.22 / 0.11 s. Perfect becomes nearly impossible while Normal stays reachable, which
        /// is why a long rally ends in a scramble or a miss rather than in a wall.
        /// </summary>
        public enum Tier { Miss, Normal, Good, Perfect }

        enum GooseState { Gone, Diving, Launched, Returning, Crashing }

        // ------------------------------------------------------------------ tuning

        [Header("Shape of the phase")]
        public float briefSeconds = 1.4f;
        /// <summary>
        /// THE BUDGET, NOT THE LENGTH. The raid ends when a garden falls; this only stops it running
        /// forever if nothing ever does.
        ///
        /// It replaces `liveSeconds = 12f`, which was the single worst thing in this phase. Twelve
        /// seconds is about three exchanges at the tuned dive speeds, so the contest stopped mid-argument
        /// for no reason the player could see — the player's own verdict was "왜 거위는 3번만 오고 끝남?
        /// 게임스럽지 않아": why do only three geese come and then it ends, that is not game-like. They
        /// were right, and a stopwatch is simply the wrong shape for this beat. A raid on a clock has no
        /// win, no loss, and no reason for any single exchange to matter; nothing the player does can
        /// bring it closer to an end, so every exchange is worth exactly the same as every other one.
        ///
        /// So the clock stops being the condition and becomes the ceiling — the same rule every other
        /// beat here is under, and it is generous on purpose: a minute holds fourteen or fifteen
        /// exchanges, four times what the old timer allowed, and reaching it at all means neither garden
        /// was breaking. When it IS reached the raid is decided on beds and said so plainly in the log,
        /// so a budget finish is never mistaken for a real result.
        /// </summary>
        [Tooltip("Ceiling on the whole contest, in seconds. NOT the length — the raid normally ends when " +
                 "a garden reaches its damage cap. This only stops it running forever if the geese stop " +
                 "arriving or the player parks.")]
        public float raidBudgetSeconds = 60f;
        [Tooltip("Ceiling on the tail, so a goose mid-crash when the clock runs out still finishes. " +
                 "A cap rather than a duration: cutting a crash short would make the damage depend " +
                 "on when the timer happened to land.")]
        public float settleMaxSeconds = 3.5f;

        [Header("The parry")]
        [Tooltip("Metres. Outside this the horn does nothing.")]
        public float parryRadius = 8f;
        [Tooltip("Half-angle of the strike cone, in degrees, measured from the machine's nose on the " +
                 "horizontal. Direction decides whether the horn connects at all; distance decides the " +
                 "grade. 60 is forgiving enough to be fun and tight enough that facing the wrong way is " +
                 "a real mistake — a full 180 would be the old bubble with extra steps.")]
        [Range(20f, 120f)] public float parryHalfAngle = 60f;
        [Tooltip("Metres for a GOOD hit — honours the aim, so it can reach the opponent.")]
        public float goodRadius = 4.6f;
        [Tooltip("Metres for a PERFECT hit. Slow motion, the hardest launch, the loudest crowd.")]
        public float perfectRadius = 2.4f;
        [Tooltip("Seconds the horn is dead after honking at nothing. The anti-mash rule: without it " +
                 "the window is not a window, it is a key you can hold down.")]
        public float whiffRecovery = 0.4f;

        [Header("The rally")]
        [Tooltip("Metres per second the goose dives at on the first exchange.")]
        public float diveSpeed = 17f;
        [Tooltip("Added per successful parry. This is the escalation, and because the tiers are radii " +
                 "it tightens all three windows at once.")]
        public float diveSpeedPerRally = 3f;
        public float diveSpeedMax = 34f;
        [Tooltip("Metres out the goose turns in from. Sized to the pitch: at 52 m it enters from over " +
                 "the OPPONENT'S end and flies the length of the ground, so the player always sees it " +
                 "coming and always has room to get back. Flight is 3.06 s on the first exchange down " +
                 "to 1.53 s at the speed cap — never under a second, or the parry stops being a timing " +
                 "test and becomes a coin flip.")]
        public float approachDistance = 52f;
        [Tooltip("Degrees above the horizon it comes in at. Shallow enough to read as a charge " +
                 "rather than as a bombing run.")]
        public float diveElevation = 20f;
        /// <summary>
        /// Metres per second a SCRAMBLED goose is thrown at. Carries about 16 m on the arc below.
        ///
        /// Dropped from 22, and this is the change that makes a rally possible at all. At 22 a scramble
        /// flew nearly the whole way to the far garden, so every exchange cost a long outbound flight
        /// plus the return — a cycle near 5 s, which in a twelve-second phase is two exchanges and a
        /// bit. The escalation could not be seen because it never got to happen: rallyEnergyFull was
        /// six and the arithmetic could not reach three.
        ///
        /// So the two throws are now genuinely different shots. A scramble is a short pop that comes
        /// straight back, which is what keeps a rally rallying; a PLACED hit is the big clearance that
        /// reaches their flowers and costs most of a second of extra airtime to do it. That turns "keep
        /// the rally going" versus "cash it in on their garden" into a real trade with a real price,
        /// which it was not when both throws went the same distance.
        /// </summary>
        [Tooltip("Metres per second a scrambled goose is thrown at. See the summary — this number is " +
                 "what decides whether a rally can build.")]
        public float launchSpeed = 14f;
        [Tooltip("Downward acceleration on a thrown goose, m/s². Well under real gravity: full " +
                 "gravity put the arc down at 27 m, short of the far garden, and a bird punted by a " +
                 "mower wants to hang long enough to be watched anyway.")]
        public float launchGravity = 6.86f;
        [Tooltip("Seconds a PLACED throw spends in the air. Only used when the hit earned placement. " +
                 "1.8 s over the pitch's 50 m gives a 2.8 m arc — a clearance rather than a bunt, and " +
                 "still cheap enough that cashing a rally in does not eat the whole window.")]
        public float launchFlightSeconds = 1.8f;
        [Tooltip("Degrees of scatter on a scrambled throw. What makes a normal hit unreliable in " +
                 "DIRECTION rather than merely quieter than a good one.")]
        public float scrambleScatter = 14f;
        [Tooltip("Exchanges at which the camera is fully wound up.")]
        public int rallyEnergyFull = 6;
        [Tooltip("Seconds of breathing space after a crash before the next goose comes in.")]
        public float regroupSeconds = 1.1f;
        [Tooltip("Degrees off the mower's heading within which a launch genuinely reaches the " +
                 "opponent's garden. Aim the machine, not a cursor.")]
        public float redirectCone = 34f;

        [Header("The damage")]
        [Tooltip("Metres around a crash in which beds are flattened.")]
        public float crashRadius = 2.6f;
        /// <summary>
        /// Most beds one crash can flatten, before the cap is considered.
        ///
        /// Two rather than three, worked out against the cap rather than picked. A garden holds roughly
        /// 26 beds, so an 18% ceiling is about five — at three a crash the ceiling is reached after two
        /// crashes and every later one is free, which is most of a bad run costing nothing. At two it
        /// takes three crashes, which is about as many as twelve seconds allows.
        /// </summary>
        public int bedsPerCrash = 2;
        /// <summary>
        /// THE CEILING. A phase may flatten at most this fraction of a garden's beds.
        ///
        /// Counted in beds rather than in cut-mask cells, and that is the payoff of defending a
        /// garden instead of the mown picture: "four of your twenty-six beds are flat" is a number the
        /// player reads off the ground, where "the cut mask gained 229 cells" was invisible and turned
        /// out to be 3% of a cap that was never reached. The cap binds now — four crashes at three
        /// beds each is twelve, and 18% of a full garden is about five.
        /// </summary>
        [Range(0f, 0.5f)] public float damageCapFraction = 0.18f;

        [Header("Impact")]
        [Tooltip("Seconds of near-frozen time on a normal hit. Below the 70-110 ms band on purpose — " +
                 "that band is for STRONG impacts, and a normal hit that stops as hard as a perfect " +
                 "one leaves the tiers indistinguishable.")]
        public float hitStopNormal = 0.045f;
        public float hitStopGood = 0.075f;
        public float hitStopPerfect = 0.11f;
        [Range(0.05f, 1f)] public float slowMotionScale = 0.34f;
        [Tooltip("Seconds of REAL time the slow-motion beat lasts. Short: it is punctuation, and a " +
                 "long one eats the reaction time the next dive needs.")]
        public float slowMotionSeconds = 0.22f;
        [Tooltip("Strength handed to MowerController.Bonk. Reuses the gnome-impact model, which " +
                 "recoils and spins the machine while deliberately leaving grip and steering live — " +
                 "recoil without taking control away, which is exactly the brief.")]
        [Range(0f, 1f)] public float bonkNormal = 0.35f;
        [Range(0f, 1f)] public float bonkGood = 0.6f;
        [Range(0f, 1f)] public float bonkPerfect = 0.9f;

        [Header("Greybox")]
        public bool showReadout = true;
        [Tooltip("Fixed so two runs can be compared. 0 draws a fresh rally every time.")]
        public int seed = 1337;

        // ------------------------------------------------------------------ result

        public struct Outcome
        {
            public int parries, perfects, goods, normals;
            public int crashes, whiffs, longestRally;
            /// <summary>Throws that came down on the opponent's beds. The point of playing well.</summary>
            public int hitsOnOpponent;
            /// <summary>Throws that came down on the player's OWN beds. The cost of a scrappy hit.</summary>
            public int ownGoals;
            public int myBedsLost, myBedsTotal;
            public int theirBedsLost, theirBedsTotal;
            public string opponent;
        }

        public Phase State { get; private set; } = Phase.Idle;
        public bool Finished => State == Phase.Done;
        public Outcome Result => _outcome;

        /// <summary>
        /// The marks the bench adds to the round for how the defence went. Survives until the next
        /// <see cref="Begin"/>, because the verdict beat asks for it well after the phase has shut down.
        /// Zero if the phase never finished, so an aborted or skipped raid awards nothing.
        /// </summary>
        public int Award { get; private set; }

        [Header("The award")]
        [Tooltip("Most the defence can add to a round out of 30. Eight is a fifth of the artistry " +
                 "ceiling: enough that playing the phase well is worth a grade, not enough that it " +
                 "beats drawing the picture properly.")]
        public int awardCeiling = 8;
        [Tooltip("Worst the defence can cost. Negative on purpose — if the floor were zero, ignoring " +
                 "the geese would be exactly as good as never having faced them, and the phase would " +
                 "be a free bonus rather than something at stake.")]
        public int awardFloor = -3;

        /// <summary>
        /// What the bench makes of the defence, as one small integer.
        ///
        /// Every term is something the phase already counts, and each is one legible sentence:
        /// a perfect strike is worth two and a good one is worth one (skill), a goose put down on the
        /// leader's flowers is worth one (aggression that paid off), a rally of three or more earns one
        /// (showmanship), and every goose that got through or was punted into the duck's own beds costs
        /// one (failure). A normal parry scores nothing: surviving is not an achievement.
        ///
        /// Static and pure so the formula is one readable place rather than something assembled across
        /// a method — this number goes on a scorecard the player is entitled to be able to predict.
        /// </summary>
        public static int ComputeAward(Outcome o, int floor, int ceiling)
        {
            int raw = o.perfects * 2
                    + o.goods
                    + o.hitsOnOpponent
                    + (o.longestRally >= 3 ? 1 : 0)
                    - o.crashes
                    - o.ownGoals;
            return Mathf.Clamp(raw, floor, ceiling);
        }
        /// <summary>Successful parries in the rally currently running. The combo.</summary>
        public int Rally { get; private set; }

        // ---- state the capture tooling triggers on ----
        //
        // Exposed because a hit stop is 45-110 ms and the slow-motion beat is 220 ms, and the review
        // loop drives Unity over RPC calls that cost one to three seconds each. No sequence of external
        // commands can land a screenshot inside a window that short, so the capture has to be driven
        // from INSIDE the frame loop and it needs to be able to see these. Read-only, and nothing in the
        // phase itself consults them.

        /// <summary>Increments on every successful strike. A capture watches it for a change.</summary>
        public int StrikeCount { get; private set; }
        public Tier LastStrikeTier { get; private set; } = Tier.Miss;
        public bool HitStopActive => _hitStop > 0f;
        public bool SlowMotionActive => _slowMo > 0f;
        public bool GooseInPlay => _gooseState == GooseState.Diving || _gooseState == GooseState.Launched;
        /// <summary>Metres from the mower to a diving goose, or -1 when none is diving.</summary>
        public float GooseRange => _gooseState == GooseState.Diving && _mower != null
            ? Vector3.Distance(_goosePos, _mower.transform.position) : -1f;

        // ------------------------------------------------------------------ state

        Outcome _outcome;
        float _stateTime, _recovery, _regroup;

        DefenceArena _arena;
        RivalContestant _opponent;
        Vector3 _toOpponent = Vector3.forward;
        int _myCap, _theirCap;

        System.Random _rng;
        MowerController _mower;
        CameraDirector _camera;
        SpectatorCrowd _crowd;
        JudgePanel _judgePanel;
        float _npcKick;
        Quaternion _npcRest;
        DefenceImpactFX _fx;

        Vector3 _mowerHomePos;
        Quaternion _mowerHomeRot;
        bool _mowerParked;

        Transform _bench;
        Vector3 _benchHomePos;
        Quaternion _benchHomeRot;
        bool _benchMoved;

        [Header("The award beat")]
        [Tooltip("Seconds held on the duck and its garden while the bench delivers the award. Long " +
                 "enough to read the number and see which beds survived, short enough that it is a " +
                 "beat rather than a screen.")]
        public float awardHoldSeconds = 2.8f;

        float _timeScaleOnEntry = 1f;
        float _hitStop, _slowMo;
        bool _clockHeld;

        Transform _goose;
        GooseState _gooseState = GooseState.Gone;
        Vector3 _goosePos, _goosePrev, _gooseVel, _diveTarget;
        float _gooseTimer;
        Material _matGoose, _matGooseHot, _matGooseBill;
        Transform _gooseBody;
        Vector3 _gooseBodyScale = Vector3.one;
        readonly List<MeshRenderer> _gooseSkin = new List<MeshRenderer>(6);
        /// <summary>Impact squash, 1 on contact decaying to 0. Real time, so a hit stop holds it.</summary>
        float _squash;
        Transform _neck, _wingL, _wingR;
        Quaternion _neckRest, _wingRestL, _wingRestR;
        /// <summary>Counts up while the bird is in the air, so the wingbeat has a phase of its own.</summary>
        float _flapPhase;
        /// <summary>0 while the bird is far, 1 the instant it is in range. Drives the tell.</summary>
        float _tell;
        /// <summary>1 at contact, decaying. Drives the neck whip and the wings snapping back.</summary>
        float _whip;
        /// <summary>Metres flown since the last trail mark, so spacing tracks speed.</summary>
        float _trailRun;
        /// <summary>Ground touches since launch. A ball is allowed a few skips before it is dead.</summary>
        int _gooseBounces;
        /// <summary>Where the bird's own centre sits when it is resting on the pitch.</summary>
        const float GroundY = 0.32f;

        AudioSource _voiceHonk, _voiceLow, _voiceRally;

        // ------------------------------------------------------------------ lifecycle

        void OnDisable() => Abort();

        void OnDestroy()
        {
            Abort();
            if (_goose != null) Destroy(_goose.gameObject);
            if (_matGoose != null) Destroy(_matGoose);
            if (_matGooseHot != null) Destroy(_matGooseHot);
            if (_matGooseBill != null) Destroy(_matGooseBill);
        }

        /// <summary>
        /// Raise the arena, move the machine into it and start the first dive.
        ///
        /// Everything that depends on the round — which outline the garden is planted along, who the
        /// opponent is, how many beds may be lost — is resolved here, because none of it exists until
        /// the picture has been mown.
        /// </summary>
        public void Begin()
        {
            Abort();

            // Asked of the director first and FOUND IN THE SCENE otherwise, because the arena is a level
            // that can be opened and played on its own and there is no GameDirector in it.
            //
            // This was not cosmetic. GooseRange returns -1 unless the mower is known, so with a null
            // mower the phase ran, dived and swooped while every tool watching it — and every capture —
            // was told no goose had arrived. Judge() reads the same reference, so no parry could have
            // been scored either. One null reference made the whole phase unobservable and unplayable
            // in the only scene it actually lives in.
            var director = GameDirector.Instance;
            _mower = director != null && director.mower != null
                   ? director.mower : FindFirstObjectByType<MowerController>();
            _camera = director != null && director.cameraDirector != null
                    ? director.cameraDirector : FindFirstObjectByType<CameraDirector>();
            if (_crowd == null) _crowd = FindFirstObjectByType<SpectatorCrowd>();

            if (_mower == null)
            {
                Debug.LogError("[Duck] no MowerController in the scene; the phase cannot judge a parry " +
                               "without one. Rebuild the arena with 'Duck/2 · Build defence arena scene'.");
                State = Phase.Done;
                return;
            }

            _rng = new System.Random(seed != 0 ? seed : System.Environment.TickCount);
            _outcome = default;
            // Cleared here and set only in EndPhase, so a raid that was aborted or never reached the
            // end awards nothing rather than paying out the last one's marks a second time.
            Award = 0;
            Rally = 0;
            _recovery = 0f;
            _regroup = 0f;

            _opponent = PickOpponent();
            _outcome.opponent = _opponent != null ? _opponent.displayName : "THE FIELD";

            DebugClearArming();
            // FOUND, not created. The arena is a saved scene authored by DuckArenaBuilder, so if this
            // comes back null the scene has not been built rather than the phase having a bug — say so
            // plainly instead of silently conjuring geometry nobody can inspect.
            if (_arena == null) _arena = FindFirstObjectByType<DefenceArena>();
            if (_arena == null)
            {
                Debug.LogWarning("[Duck] no DefenceArena in the scene; run " +
                                 "'Duck/2 · Build defence arena scene'. The phase will not run.");
                State = Phase.Done;
                return;
            }
            _arena.Bind();
            _toOpponent = _arena.Toward;

            _outcome.myBedsTotal = _arena.PlayerBeds.Count;
            _outcome.theirBedsTotal = _arena.OpponentBeds.Count;
            _myCap = Mathf.Max(1, Mathf.RoundToInt(_outcome.myBedsTotal * damageCapFraction));
            _theirCap = Mathf.Max(1, Mathf.RoundToInt(_outcome.theirBedsTotal * damageCapFraction));

            // The clock is only ever bent from here, so remember what it WAS rather than assuming 1.
            // The review tools park the game at 6x, and restoring a hard 1 would silently undo a
            // reviewer's fast-forward in the middle of their own capture.
            _timeScaleOnEntry = Time.timeScale;
            _hitStop = 0f;
            _slowMo = 0f;
            _clockHeld = false;

            MoveMowerIn();
            BuildGoose();
            BuildVoices();
            EnsureFX();

            // Assert the chase shot. The klaxon abandons an aerial lift without putting the camera
            // back, so a player who was ninety metres up when time ran out would otherwise open the
            // phase looking straight down at a lawn 420 m from the arena.
            _camera?.SetMode(CameraMode.Chase, 0.6f);

            SetPhase(Phase.Brief);

            Debug.Log($"[Duck] rally: garden {_outcome.myBedsTotal} beds (cap {_myCap}), " +
                      $"{_outcome.opponent} {_outcome.theirBedsTotal} beds (cap {_theirCap}), " +
                      $"goals {_arena.GardenGap:0} m apart.");
        }

        /// <summary>
        /// Shut the phase down, give the clock back, and put the machine where it was.
        ///
        /// Safe from the state change, from OnDisable and from OnDestroy, because the debug jumps and
        /// the capture tools all change state from underneath this. Restoring the timescale is the one
        /// that must never be missed: a phase aborted during a hit stop would otherwise leave the
        /// whole game running at two percent speed with nothing on screen to explain it.
        /// </summary>
        public void Abort()
        {
            ReleaseClock();
            // Abort is the ABNORMAL exit — a retry, Escape to the menu, a debug jump — and those do need
            // the machine back on the lawn. A normal finish clears _mowerParked without moving anything
            // (see EndPhase), so this is a no-op on that path and the duck stays on the pitch for its
            // award.
            MoveMowerOut();
            RestoreBench();
            // Belt and braces on top of MoveMowerOut, which only lifts the lock if it was the thing
            // that set it. A BladeLocked left standing is the worst failure this file can cause: the
            // mower drives for the rest of the session and cuts nothing, so the player mows a whole
            // round and scores zero with no way to tell why.
            if (_mower != null) _mower.BladeLocked = false;
            _camera?.ClearDefenceSubject();
            if (_goose != null) _goose.gameObject.SetActive(false);
            _gooseState = GooseState.Gone;
            if (_voiceRally != null) _voiceRally.Stop();
            State = Phase.Idle;
        }

        void SetPhase(Phase p)
        {
            State = p;
            _stateTime = 0f;
        }

        void MoveMowerIn()
        {
            if (_mower == null || _arena == null) return;

            _mowerHomePos = _mower.transform.position;
            _mowerHomeRot = _mower.transform.rotation;
            _mowerParked = true;

            MoveBenchIn();
            _mower.ParkAt(_arena.SpawnPoint, _arena.SpawnRotation);
            // The deck comes up. The blade does nothing out here anyway — the cut mask is centred on
            // the playfield and clips everything at this range — but a spinning blade and a shredding
            // audio layer while the duck is defending is a machine doing the wrong job.
            _mower.BladeLocked = true;
            _camera?.NotifyTargetTeleported();
        }

        /// <summary>
        /// Bring the judges' bench onto the pitch, by MOVING THE REAL ONE.
        ///
        /// The bench and the three rigged judges are authored by editor script — `BuildJudgeBenchOnly`
        /// plus the `Judge_*` prefabs — and none of that can be called at runtime, so a second bench
        /// here would have meant re-authoring one in code and maintaining two. Instead the existing
        /// assembly is relocated for the duration and put back afterwards.
        ///
        /// It works because the whole thing hangs off one transform: `JudgePanel`'s own. The bench mesh,
        /// all three judges, their rigs and their animators are children of it, and CameraDirector's
        /// `judgesLookAt` points at that very transform — so the judging camera follows the move for
        /// free rather than having to be told about it. One SetPositionAndRotation relocates the lot.
        ///
        /// RestoreBench is therefore not optional housekeeping: the judging beat happens back at the
        /// venue a few seconds later, and a bench left out here would hold that entire beat 420 m from
        /// where the camera is looking.
        /// </summary>
        void MoveBenchIn()
        {
            var panel = GameDirector.Instance != null ? GameDirector.Instance.judges : null;
            if (panel == null || _arena == null || _benchMoved) return;

            _bench = panel.transform;
            _benchHomePos = _bench.position;
            _benchHomeRot = _bench.rotation;
            _benchMoved = true;
            _bench.SetPositionAndRotation(_arena.BenchPosition, _arena.BenchRotation);
        }

        void RestoreBench()
        {
            if (!_benchMoved || _bench == null) return;
            _benchMoved = false;
            _bench.SetPositionAndRotation(_benchHomePos, _benchHomeRot);
        }

        void MoveMowerOut()
        {
            if (!_mowerParked || _mower == null) return;
            _mowerParked = false;
            _mower.ParkAt(_mowerHomePos, _mowerHomeRot);
            _mower.BladeLocked = false;
            _camera?.NotifyTargetTeleported();
        }

        // ------------------------------------------------------------------ tick

        // No Update. The director drives this from its own Tick, which is the entry point the capture
        // harness also calls, so the phase steps identically under Unity's loop and under SimClock.
        public void Tick(float dt)
        {
            if (State == Phase.Idle || State == Phase.Done) return;

            // The clock is bent on UNSCALED time, because the whole point of a hit stop is that
            // scaled time has stopped moving. Everything else runs on the scaled dt the director
            // handed down, which is what makes a hit stop cost the player nothing off the phase timer.
            UpdateClock();

            // Debris runs on the SCALED dt, so a hit stop freezes the burst in mid-air with everything
            // else and it bursts outward as the clock releases. See DefenceImpactFX's class note: on
            // real time the whole burst would be spent during the frames nothing else is moving.
            _fx?.Tick(dt);

            _stateTime += dt;

            switch (State)
            {
                case Phase.Brief:
                    if (_stateTime >= briefSeconds) { SetPhase(Phase.Live); StartDive(); }
                    break;

                case Phase.Live:
                    // Input before the goose moves, so the frame it arrives is still a frame it can be
                    // met on. The other order quietly spends the last frame of every window.
                    ResolveInput(dt);
                    StepGoose(dt);
                    UpdateCameraEnergy(dt);
                    UpdateTell();
                    if (_stateTime >= liveSeconds) SetPhase(Phase.Settle);
                    break;

                case Phase.Settle:
                    ResolveInput(dt);
                    StepGoose(dt);
                    UpdateCameraEnergy(dt);
                    UpdateTell();
                    if (_gooseState == GooseState.Gone || _stateTime >= settleMaxSeconds) BeginAward();
                    break;

                case Phase.Awarding:
                    // Nothing moves and nobody is moved. The bench marks the defence here, on this
                    // pitch, with the flattened and surviving beds still on the ground in shot — which
                    // is the entire point of the beat: the evidence and the verdict in one frame.
                    if (_stateTime >= awardHoldSeconds) EndPhase();
                    break;
            }
        }

        /// <summary>
        /// The award beat, delivered here on the pitch rather than after the player has been moved.
        ///
        /// The old shape put the whole outcome into EndPhase, which also parked the mower back in the
        /// venue — so the instant the rally ended the chase camera was yanked 420 m across the map and
        /// the judging happened somewhere the player had never been. From the seat that read as the
        /// level snapping back, and it was the reason the flow felt wrong. Nothing is moved until the
        /// award has landed.
        /// </summary>
        void BeginAward()
        {
            _outcome.myBedsLost = _arena != null ? _arena.PlayerBedsLost : 0;
            _outcome.theirBedsLost = _arena != null ? _arena.OpponentBedsLost : 0;
            Award = ComputeAward(_outcome, awardFloor, awardCeiling);

            // Boris is the one who enjoys a rally — he marks on coverage and style and applauds
            // everything — so a positive award goes to him by name rather than to whichever judge the
            // random happened to pick. Mildred withholds, so she gets the disappointments.
            var panel = GameDirector.Instance != null ? GameDirector.Instance.judges : null;
            if (panel != null && panel.judges != null && panel.judges.Length >= 2)
                panel.judges[Award >= 0 ? 1 : 0]?.character?.Punch(Award >= 0 ? 0.85f : -0.7f);

            var audio = AudioDirector.Instance;
            if (audio != null)
            {
                audio.PlayOne(audio.cardRaise, 0.6f);
                audio.PlayOne(audio.stamp, 0.7f);
                if (Award > 0) audio.CrowdCheer(Mathf.InverseLerp(0f, awardCeiling, Award), applaud: true);
                else if (Award < 0) audio.CrowdGroan(0.6f);
                audio.PlayOne(Award >= 3 ? audio.quackProud : Award < 0 ? audio.quackAnnoyed : null, 0.6f);
            }
            _crowd?.Excite(Award > 0 ? 0.8f : 0.2f);

            // Hold the shot on the duck and its garden. The goose is gone, so without this the camera's
            // defence subject would still be the opponent's board at the far end and the award would be
            // delivered over an empty pitch.
            _camera?.ClearDefenceSubject();

            Debug.Log($"[Duck] defence award {(Award >= 0 ? "+" : "")}{Award} " +
                      $"({_outcome.perfects}P {_outcome.goods}G {_outcome.normals}N, " +
                      $"longest x{_outcome.longestRally}, {_outcome.hitsOnOpponent} on them, " +
                      $"{_outcome.crashes} through, {_outcome.ownGoals} own goals).");

            SetPhase(Phase.Awarding);
        }

        void EndPhase()
        {

            Debug.Log($"[Duck] rally over: {_outcome.parries} parries ({_outcome.perfects}P " +
                      $"{_outcome.goods}G {_outcome.normals}N), longest x{_outcome.longestRally}, " +
                      $"{_outcome.crashes} crashes, {_outcome.whiffs} wasted honks, " +
                      $"{_outcome.hitsOnOpponent} landed on them, {_outcome.ownGoals} own goals. " +
                      $"My beds {_outcome.myBedsLost}/{_outcome.myBedsTotal} flat (cap {_myCap}), " +
                      $"theirs {_outcome.theirBedsLost}/{_outcome.theirBedsTotal} (cap {_theirCap}). " +
                      $"AWARD {(Award >= 0 ? "+" : "")}{Award}.");

            var audio = AudioDirector.Instance;
            audio?.PlayOne(_outcome.myBedsLost <= _outcome.theirBedsLost
                               ? audio.fanfareGood : audio.fanfareBad, 0.7f);

            ReleaseClock();

            // The machine is deliberately LEFT on the pitch. Driving it back here is what produced the
            // 420 m snap under a live chase camera, and nothing needs it moved: the reveal is an
            // overhead of the lawn, where an absent mower is if anything a cleaner shot, and the
            // verdict's StageForPortrait parks it on its mark before any camera looks at it again. The
            // one thing that must go with this is the reveal's camera BLEND — see the Reveal case in
            // GameDirector, which now cuts rather than sailing across the map.
            //
            // The flag is cleared without moving anything so that Abort, which runs on the way out of
            // the state, finds nothing left to put back.
            _mowerParked = false;
            if (_mower != null) _mower.BladeLocked = false;

            RestoreBench();
            _camera?.ClearDefenceSubject();
            if (_goose != null) _goose.gameObject.SetActive(false);
            if (_voiceRally != null) _voiceRally.Stop();
            State = Phase.Done;
        }

        // ------------------------------------------------------------------ the clock

        /// <summary>
        /// Hit stop and slow motion, on Time.timeScale.
        ///
        /// Two things make this safe to do to a force-driven rigidbody. Time.fixedDeltaTime is NOT
        /// scaled by timeScale, so the suspension springs integrate with exactly the step they always
        /// had — a dip changes how MANY steps happen per real second, never how large one is, and
        /// fewer identical steps cannot destabilise a spring. And every dt-dependent expression in
        /// MowerController is already written dt-invariantly (MoveTowards for drive,
        /// 1-pow(1-g, dt*60) for grip), so the handling in game-time is unchanged. Interpolation is on,
        /// so the render stays smooth at fifteen steps a second.
        ///
        /// Skipped entirely under SimClock.Scripted: the harness owns the clock and steps physics by
        /// hand, so a timescale here would do nothing to the simulation while corrupting the frame
        /// sheet's timings — a hit stop would be captured as a stall.
        /// </summary>
        void UpdateClock()
        {
            if (SimClock.Scripted) return;

            float real = Time.unscaledDeltaTime;
            // Real time, so the squash HOLDS through a hit stop and releases as the world starts moving
            // again. Decaying it on scaled time would spend the whole deformation during the frames
            // nothing else is moving, which is the one moment it exists to be seen in.
            _squash = Mathf.Max(0f, _squash - real * 5.5f);
            // Whip decays slower than the squash: a neck snapping back is a longer gesture than a body
            // compressing, and matching them makes the whole bird read as one rubber blob.
            _whip = Mathf.Max(0f, _whip - real * 3.4f);
            StepOpponent(real);

            if (_hitStop > 0f)
            {
                _hitStop -= real;
                if (_hitStop > 0f) { Time.timeScale = 0.02f; _clockHeld = true; return; }
            }
            if (_slowMo > 0f)
            {
                _slowMo -= real;
                if (_slowMo > 0f)
                {
                    Time.timeScale = _timeScaleOnEntry * slowMotionScale;
                    _clockHeld = true;
                    return;
                }
            }
            if (_clockHeld) { Time.timeScale = _timeScaleOnEntry; _clockHeld = false; }
        }

        void ReleaseClock()
        {
            _hitStop = 0f;
            _slowMo = 0f;
            if (_clockHeld && !SimClock.Scripted) Time.timeScale = _timeScaleOnEntry;
            _clockHeld = false;
        }

        /// <summary>
        /// The debris rig, made once and kept. Parented to this component rather than to the goose, so
        /// a burst stays where the impact happened instead of being dragged along by the bird that has
        /// just been launched out of it.
        /// </summary>
        void EnsureFX()
        {
            if (_fx == null)
            {
                _fx = GetComponent<DefenceImpactFX>() ?? gameObject.AddComponent<DefenceImpactFX>();
                _fx.Setup(_rng, parryHalfAngle);
            }
            _fx.Clear();
        }

        // ------------------------------------------------------------------ the parry

        void ResolveInput(float dt)
        {
            if (_recovery > 0f) _recovery = Mathf.Max(0f, _recovery - dt);

            var input = InputReader.Instance;
            if (input == null || !input.HornPressed) return;

            Honk();
            // The shockwave goes out on EVERY press, before the hit test — including a honk during
            // recovery, which is the one the player most needs to see land. The horn was the phase's
            // only verb and it had no output of its own: a mistimed press produced nothing at all and
            // read as a dropped input rather than as a miss. Weaker while recovering, so a mashed horn
            // still says "not yet" rather than rewarding the mash.
            // Turned with the nose, so the wave shows the cone the horn actually reaches through.
            Vector3 nose = Vector3.ProjectOnPlane(_mower.transform.forward, Vector3.up);
            _fx?.Shock(_mower.transform.position,
                       Quaternion.LookRotation(nose.sqrMagnitude > 1e-4f ? nose.normalized : _toOpponent,
                                               Vector3.up),
                       _recovery > 0f ? 0.35f : 1f);

            if (_recovery > 0f) return;

            Tier tier = Judge();
            if (tier == Tier.Miss)
            {
                _recovery = whiffRecovery;
                _outcome.whiffs++;
                return;
            }
            Strike(tier);
        }

        /// <summary>
        /// How well the goose was met: purely how far away it was when the horn went.
        ///
        /// Only a DIVING goose can be met. A launched or returning one already belongs to the player,
        /// and letting the horn touch it would let them keep the same bird in the air forever without
        /// ever having to face it coming back.
        /// </summary>
        Tier Judge()
        {
            if (_gooseState != GooseState.Diving || _mower == null) return Tier.Miss;

            // A CONE out of the machine's nose, not a bubble around it.
            //
            // The test used to be distance alone, so the horn connected with a goose directly behind the
            // mower as readily as one in front of it. Aim changed only how accurately the bird was
            // returned — never whether it was hit — which is why the phase read as swatting rather than
            // as striking: there was no wrong way to be facing.
            //
            // Now direction decides contact and distance decides grade. Pointing at the bird is the
            // skill; being close to it is the reward.
            //
            // Measured on the HORIZONTAL only. A diving goose arrives from twenty metres up, so a true
            // 3D cone from a ground-level forward vector would exclude the very bird the player is
            // plainly aiming at — the player reads this as "am I facing it", and facing is a compass
            // question, not a spherical one.
            Vector3 toGoose = _goosePos - _mower.transform.position;
            float d = toGoose.magnitude;
            if (d > parryRadius) return Tier.Miss;

            Vector3 flatTo = toGoose; flatTo.y = 0f;
            Vector3 nose = _mower.transform.forward; nose.y = 0f;
            // Directly overhead: no meaningful bearing, so the cone cannot reject it. Treat a bird on
            // top of the machine as in front of it rather than as a miss nobody could explain.
            bool overhead = flatTo.sqrMagnitude < 0.36f;
            float bearing = overhead ? 0f : Vector3.Angle(nose, flatTo);
            if (bearing > parryHalfAngle) return Tier.Miss;

            // Grade on distance as before, but tightened by how square the hit was: a bird at the edge
            // of the cone cannot be a PERFECT even from point blank. That is what stops the widened cone
            // from being strictly more generous than the bubble it replaced.
            float square = 1f - Mathf.Clamp01(bearing / Mathf.Max(parryHalfAngle, 1f));
            if (d <= perfectRadius && square > 0.55f) return Tier.Perfect;
            if (d <= goodRadius && square > 0.25f) return Tier.Good;
            return Tier.Normal;
        }

        void Strike(Tier tier)
        {
            Rally++;
            StrikeCount++;
            LastStrikeTier = tier;
            _outcome.parries++;
            _outcome.longestRally = Mathf.Max(_outcome.longestRally, Rally);
            if (tier == Tier.Perfect) _outcome.perfects++;
            else if (tier == Tier.Good) _outcome.goods++;
            else _outcome.normals++;

            // Where the machine is pointing is where the goose goes. The most direct reading of the
            // arcade-car-soccer reference, and it makes aiming a driving problem rather than a second
            // control to learn.
            Vector3 heading = _mower.transform.forward;
            heading.y = 0f;
            if (heading.sqrMagnitude < 1e-4f) heading = _toOpponent;
            heading.Normalize();

            // A parried goose ALWAYS goes downfield. Tier decides how ACCURATELY, never whether.
            //
            // It used to decide whether: a Normal parry was excluded outright and Good/Perfect only
            // counted if the machine happened to be pointing within 34 degrees of the far goal. So most
            // successful parries flung the bird sideways or back over the player's own beds, and the one
            // thing the exchange is about — sending it to the opponent — was the exception rather than
            // the rule. From the seat that reads as the goose charging at you and being swatted away,
            // not as a rally.
            //
            // Now the launch is aimed at the opponent's garden and the player's heading only BENDS it.
            // A perfect hit with good aim lands on a bed; a normal hit sprays wide and may fall short in
            // the open. Skill still decides the outcome — LandLaunched reads it off where the bird
            // actually comes down, exactly as before — but the SILHOUETTE of the exchange is now always
            // "it went that way", which is what makes the pitch read as having two ends.
            Vector3 target = _arena != null
                ? (_arena.AnyAlive(_rng, playerSide: false)?.position ?? _arena.OpponentGardenCentre)
                : _goosePos + _toOpponent * 40f;   // no arena bound: still throw downfield

            // How much the player's aim is honoured. A perfect strike places it; a normal one keeps the
            // direction and loses the address.
            float placement = tier == Tier.Perfect ? 1f : tier == Tier.Good ? 0.72f : 0.35f;
            float offAim = Vector3.Angle(heading, _toOpponent) / Mathf.Max(redirectCone, 1f);
            // Aim outside the cone costs accuracy rather than cancelling the throw.
            placement *= Mathf.Clamp01(1.2f - offAim * 0.55f);

            // Scatter shrinks as placement rises, and is applied about the DOWNFIELD axis so even the
            // worst parry still travels toward the far end.
            float spread = scrambleScatter * (1f - placement);
            float wobble = ((float)_rng.NextDouble() * 2f - 1f) * spread;

            // AIMED AT MID-PITCH, not at the far goal.
            //
            // "E로 튕겼을 때 상대방쪽으로 날라가서 사라지는게 아니라. 한번 중앙 바닥에 튕겨서 상대방한테
            // 가는거지." — it should touch down once in the middle and travel on from there, not sail the
            // whole way and vanish. That is the difference between a clearance and a ball: one leaves the
            // pitch, the other uses it.
            //
            // So the arc is thrown roughly HALF the distance and the bounces carry the rest. The skip
            // retains 86% of its horizontal speed per touch (see the Launched case), so a well-struck
            // bird reaches the far beds on its second or third contact while a weak one dies in midfield
            // — which is a far better read of how well it was hit than the landing point of a single arc.
            Vector3 flat = target - _goosePos; flat.y = 0f;
            float reach = Mathf.Max(flat.magnitude, 6f) * Mathf.Lerp(0.34f, 0.55f, placement);
            Vector3 aimed = Quaternion.Euler(0f, wobble, 0f) * (flat.sqrMagnitude > 1e-4f
                                                                ? flat.normalized : _toOpponent);
            Vector3 land = _goosePos + aimed * reach;
            land.y = 0.3f;

            _gooseVel = ArcTo(_goosePos, land, launchFlightSeconds, launchGravity);

            _gooseState = GooseState.Launched;
            _gooseTimer = 0f;
            _trailRun = 1.1f;   // first mark on the frame it leaves, not a metre later
            SetGooseHot(true);

            // Feel, reused wholesale rather than rebuilt. Bonk was written for gnome impacts and is
            // already the mower half of this brief: it sheds speed, shoves the machine back, spins it,
            // and deliberately keeps grip and steering live so only the drive is cut.
            _mower.Bonk(_goosePos, tier == Tier.Perfect ? bonkPerfect
                                 : tier == Tier.Good ? bonkGood : bonkNormal);

            _squash = tier == Tier.Perfect ? 1f : tier == Tier.Good ? 0.75f : 0.5f;
            // The neck whip and the wing snap ride the same envelope as the squash, so the whole bird
            // deforms as one event rather than as three effects that happen to coincide.
            _whip = _squash;
            _hitStop = tier == Tier.Perfect ? hitStopPerfect
                     : tier == Tier.Good ? hitStopGood : hitStopNormal;
            if (tier == Tier.Perfect) _slowMo = slowMotionSeconds;

            _camera?.AddPunch(tier == Tier.Perfect ? 1.0f : tier == Tier.Good ? 0.62f : 0.34f);
            _camera?.AddShake(tier == Tier.Perfect ? 0.9f : tier == Tier.Good ? 0.55f : 0.3f);

            PlayStrikeAudio(tier);
            ReactCrowd(tier);
            // Struck well: the rival draws up, because this one is coming at them.
            ReactOpponent(tier == Tier.Perfect ? 1f : tier == Tier.Good ? 0.6f : 0.35f,
                          inTheirFavour: false);

            // Debris last, and thrown along the LAUNCH heading rather than along the goose's incoming
            // velocity. The burst is the clearest read of where the bird has been sent — it points at
            // the answer during the frames the hit stop is holding everything still.
            _fx?.Impact(_goosePos, heading,
                        tier == Tier.Perfect ? 1f : tier == Tier.Good ? 0.62f : 0.3f);

            // And the marks where the wheels let go. Laid along the machine's own travel rather than the
            // launch heading: the skid is where the mower WAS going when it was checked, which is what
            // makes it read as the machine having been stopped rather than as the goose having been sent.
            Vector3 roll = _mower.transform.forward;
            _fx?.Skid(_mower.transform.position, roll, 0.46f,
                      tier == Tier.Perfect ? 1f : tier == Tier.Good ? 0.6f : 0.35f);
        }

        // ------------------------------------------------------------------ the goose

        /// <summary>
        /// The goose, as a legible greybox rather than a capsule.
        ///
        /// The capsule failed on measurement, not on taste, and both failures are worth recording
        /// because they are easy to repeat.
        ///
        /// COLOUR. It was pale cream, and the frame that matters is the contact frame — which is
        /// always near the ground, over pale sage. A pale bird on pale ground at 30 px had to be
        /// HUNTED FOR in a still. Worse, the contact frame shows the STRUCK material, so the paler of
        /// the two was the one guarding the only moment the mechanic lives in. It is now dark: a dark
        /// value is the one choice that separates from pale sky AND pale ground at once, where any hue
        /// only separates from one of them. The struck state flares to saturated orange, which is both
        /// unmistakable and reads as an event rather than a state.
        ///
        /// HEADING. A capsule is an egg — it has no front, so neither its trajectory nor where it came
        /// from could be read at all, and "readable trajectories" is on the goal's own list. Neck, bill,
        /// tail and wings cost four boxes and give the silhouette a direction from any angle. The bill
        /// stays bright while the body is dark, so the sharpest value edge on the object points exactly
        /// where it is going.
        ///
        /// Built as a hierarchy with the parts pre-oriented so the ROOT's +Z is forward. That means
        /// placement is one LookRotation with no correction Euler, and the body can be squashed on
        /// impact independently of the heading.
        /// </summary>
        void BuildGoose()
        {
            _matGoose ??= Flat(new Color(0.17f, 0.15f, 0.14f));
            _matGooseHot ??= Flat(new Color(0.98f, 0.34f, 0.06f));
            _matGooseBill ??= Flat(new Color(1f, 0.58f, 0.06f));

            if (_goose != null) { _goose.gameObject.SetActive(false); return; }

            var root = new GameObject("~ DefenceGoose (greybox)");
            _goose = root.transform;
            _gooseSkin.Clear();

            // A capsule's long axis is Y, so the 90-degree turn lives here, once, rather than in every
            // placement. Body is 1.24 m long and 0.42 m across — a big bird.
            _gooseBody = Part(PrimitiveType.Capsule, "Body", _matGoose,
                              new Vector3(0.42f, 0.62f, 0.42f), Vector3.zero,
                              Quaternion.Euler(90f, 0f, 0f));
            _gooseBodyScale = _gooseBody.localScale;

            _neck = Part(PrimitiveType.Capsule, "Neck", _matGoose,
                 new Vector3(0.17f, 0.30f, 0.17f), new Vector3(0f, 0.17f, 0.50f),
                 Quaternion.Euler(58f, 0f, 0f));
            _neckRest = _neck.localRotation;
            Part(PrimitiveType.Cube, "Tail", _matGoose,
                 new Vector3(0.11f, 0.24f, 0.30f), new Vector3(0f, 0.10f, -0.60f),
                 Quaternion.Euler(-18f, 0f, 0f));
            _wingL = Part(PrimitiveType.Cube, "WingL", _matGoose,
                 new Vector3(0.60f, 0.07f, 0.34f), new Vector3(-0.40f, 0.06f, 0.02f),
                 Quaternion.Euler(0f, 0f, 13f));
            _wingR = Part(PrimitiveType.Cube, "WingR", _matGoose,
                 new Vector3(0.60f, 0.07f, 0.34f), new Vector3(0.40f, 0.06f, 0.02f),
                 Quaternion.Euler(0f, 0f, -13f));
            _wingRestL = _wingL.localRotation;
            _wingRestR = _wingR.localRotation;

            // Deliberately NOT added to the skin list: the bill keeps its bright colour through a
            // strike, so the object never loses the one edge that states its heading.
            Part(PrimitiveType.Cube, "Bill", _matGooseBill,
                 new Vector3(0.13f, 0.10f, 0.32f), new Vector3(0f, 0.31f, 0.78f),
                 Quaternion.identity, skin: false);

            _goose.gameObject.SetActive(false);
        }

        Transform Part(PrimitiveType type, string name, Material mat, Vector3 scale,
                       Vector3 localPos, Quaternion localRot, bool skin = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.SetParent(_goose, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = scale;

            var r = go.GetComponent<MeshRenderer>();
            if (r != null)
            {
                r.sharedMaterial = mat;
                // The bird casts. Its shadow crossing the beds is most of how the player reads where it
                // is going to land, and on a low chase camera it is often the clearer of the two reads.
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                if (skin) _gooseSkin.Add(r);
            }
            return go.transform;
        }

        void SetGooseHot(bool hot)
        {
            var m = hot ? _matGooseHot : _matGoose;
            foreach (var r in _gooseSkin) if (r != null) r.sharedMaterial = m;
        }

        /// <summary>
        /// Send a goose in at the flowerbed nearest the mower.
        ///
        /// Nearest to the player rather than anywhere in the garden, and that is a fairness rule
        /// rather than a convenience. The phase is a timing test, so the bird has to be reachable and
        /// visible from where the player already is; picking a bed at random would turn every dive
        /// into a drive across the garden, and beds would be lost to travel time instead of to bad
        /// timing. What the 18 m frontage is for is CHOOSING which part of the garden to stand in
        /// between exchanges, which is a decision — not a sprint the player cannot win.
        /// </summary>
        void StartDive()
        {
            if (_arena == null || _mower == null) { _gooseState = GooseState.Gone; return; }

            var bed = _arena.NearestAlive(_mower.transform.position, playerSide: true);
            // Nothing left to defend. Aim at the middle so the phase still plays out rather than
            // stopping dead on a technicality.
            _diveTarget = bed != null ? bed.position : _arena.PlayerGardenCentre;

            // Comes in over the opponent's side, give or take, so "back the way it came" is a
            // direction the player can see rather than a rule to learn.
            float spread = ((float)_rng.NextDouble() * 2f - 1f) * 25f;
            Vector3 azimuth = Quaternion.Euler(0f, spread, 0f) * _toOpponent;
            Vector3 dir = (azimuth + Vector3.up * Mathf.Tan(diveElevation * Mathf.Deg2Rad)).normalized;

            _goosePos = _diveTarget + dir * approachDistance;
            _goosePrev = _goosePos;
            _gooseVel = -dir * CurrentDiveSpeed();
            _gooseState = GooseState.Diving;
            _gooseTimer = 0f;

            SetGooseHot(false);
            _goose.gameObject.SetActive(true);
            PlaceGoose();
        }

        public float CurrentDiveSpeed() => Mathf.Min(diveSpeedMax, diveSpeed + diveSpeedPerRally * Rally);

        // ------------------------------------------------------------------ scripted parries
        //
        // THE REVIEW LOOP CANNOT PRESS A KEY. Unity is driven here through MCP, which can build, play,
        // run menu items and capture frames but cannot send keystrokes — so the horn is unreachable and
        // every captured frame of this phase was a miss. `0 parries (0P 0G 0N), longest x0`. That makes
        // the entire impact layer — tiers, hit stop, the perfect slow-motion, the camera punch, the
        // launch arc, an escalating rally, a goose landing on the opponent — invisible to the only
        // process that can look at it, and it is most of what the phase is.
        //
        // So the honk gets a second way in. These arm an automatic strike at a chosen tier and are
        // otherwise inert: no new gameplay, no change to what a real press does, and E is untouched.

        Tier _armedTier = Tier.Miss;
        int _armedRemaining;

        /// <summary>How many scripted parries are still queued. Shown in the readout.</summary>
        public int DebugArmedRemaining => _armedRemaining;

        /// <summary>
        /// Queue <paramref name="count"/> automatic parries at <paramref name="tier"/>.
        ///
        /// Each one fires the moment the diving goose is inside that tier's radius. If it never gets
        /// that close — a Perfect needs the machine parked on the threatened bed, and a scripted run
        /// does not drive — the strike fires on the frame the bird would otherwise have landed, so the
        /// requested tier is always what gets demonstrated. That is the right behaviour for an
        /// instrument whose job is to show what a tier looks like, and the wrong behaviour for
        /// gameplay, which is why nothing but the editor tools calls it.
        /// </summary>
        public void DebugArmParries(Tier tier, int count)
        {
            _armedTier = tier;
            _armedRemaining = Mathf.Max(0, count);
        }

        public void DebugClearArming()
        {
            _armedTier = Tier.Miss;
            _armedRemaining = 0;
        }

        float RadiusFor(Tier tier) => tier switch
        {
            Tier.Perfect => perfectRadius,
            Tier.Good => goodRadius,
            _ => parryRadius
        };

        /// <summary>
        /// Fire a queued scripted parry if this is its moment. True if it struck, so the caller knows
        /// not to also run the crash test on the same frame.
        /// </summary>
        bool TryArmedParry(bool aboutToLand)
        {
            if (_armedRemaining <= 0 || _armedTier == Tier.Miss || _mower == null) return false;

            float d = Vector3.Distance(_goosePos, _mower.transform.position);
            if (d > RadiusFor(_armedTier) && !aboutToLand) return false;

            _armedRemaining--;
            Honk();
            Strike(_armedTier);
            return true;
        }

        void StepGoose(float dt)
        {
            if (_regroup > 0f)
            {
                _regroup -= dt;
                if (_regroup <= 0f && State == Phase.Live) StartDive();
                return;
            }

            switch (_gooseState)
            {
                case GooseState.Gone:
                    return;

                case GooseState.Diving:
                {
                    Advance(dt);
                    // Judged on having PASSED the target rather than on proximity to it, so a fast
                    // goose on a long frame cannot tunnel through and orbit forever.
                    bool landing = Vector3.Dot(_goosePos - _diveTarget, _gooseVel) >= 0f ||
                                   _goosePos.y <= 0.3f;
                    if (TryArmedParry(landing)) break;
                    if (landing) Crash();
                    break;
                }

                case GooseState.Launched:
                    _gooseVel += Vector3.down * launchGravity * dt;
                    Advance(dt);
                    _gooseTimer += dt;
                    // The directional trail, and PERFECT ONLY — that is the whole point of it. The goal
                    // asks for it under "perfect parries should feel dramatically stronger", so putting
                    // it on every launch would spend the one signal that distinguishes the best hit.
                    //
                    // Shed at a fixed distance rather than on a timer, so the spacing reads as SPEED: a
                    // fast throw lays the same marks further apart, and the trail says how hard it was
                    // hit as well as where it went.
                    if (LastStrikeTier == Tier.Perfect)
                    {
                        _trailRun += (_goosePos - _goosePrev).magnitude;
                        if (_trailRun >= 1.1f)
                        {
                            _trailRun = 0f;
                            _fx?.TrailPuff(_goosePos);
                        }
                    }
                    // A minimum airtime before it can be called down. Without it, a goose met at ankle
                    // height was already under the landing line on the frame it was struck, so it
                    // "landed" instantly beside the player and got credited to whichever garden was
                    // nearest — damage from a throw that never travelled.
                    // IT BOUNCES. The player asked for "공처럼 튕기는 걸" — like a ball — and the old
                    // behaviour was the opposite of that: the first time it touched grass it stopped dead,
                    // scored, and the exchange was over. One touch, one outcome, nothing to chase.
                    //
                    // Now it rebounds with restitution and keeps its downfield momentum, so a good hit
                    // SKIPS toward the far goal and a weak one dribbles to a halt short of it. That single
                    // change is what makes the pitch read as a ball game rather than as a throw: the bird
                    // is live between bounces, it can still be reached, and where it finally settles is
                    // the result of how hard it was struck rather than of one instant of contact.
                    if (_goosePos.y <= GroundY && _gooseVel.y < 0f)
                    {
                        _gooseBounces++;
                        _goosePos.y = GroundY;

                        // Vertical energy comes back at 52%; horizontal is scrubbed only lightly, so the
                        // skip carries downfield instead of stalling on the spot.
                        _gooseVel.y = -_gooseVel.y * 0.52f;
                        _gooseVel.x *= 0.86f;
                        _gooseVel.z *= 0.86f;

                        // Each touch is an event: dirt, a thud, and a squash that shrinks as it settles.
                        float hard = Mathf.Clamp01(_gooseVel.magnitude / Mathf.Max(launchSpeed, 1f));
                        _fx?.Dirt(_goosePos, hard * 0.5f);
                        _squash = Mathf.Max(_squash, hard * 0.5f);
                        LowThud(hard * 0.45f);
                        if (_gooseBounces == 1) Honk(0.5f);

                        // Settled: too slow to skip again, out of bounces, or out of patience. THEN it
                        // scores, and it scores where it actually came to rest.
                        if (_gooseVel.magnitude < 3.2f || _gooseBounces >= 4 || _gooseTimer > 6f)
                        {
                            LandLaunched();
                            _gooseState = GooseState.Returning;
                            _gooseTimer = 0f;
                            _gooseBounces = 0;
                        }
                    }
                    else if (_gooseTimer > 6f)
                    {
                        // Budgeted, not trusted — the same rule every other beat here is under.
                        LandLaunched();
                        _gooseState = GooseState.Returning;
                        _gooseTimer = 0f;
                        _gooseBounces = 0;
                    }
                    break;

                case GooseState.Returning:
                    // Up and round for another go. A short pause so the rally has a pulse rather than
                    // being a continuous stream — trimmed from 0.55 s for the same reason the throw
                    // was: at three exchanges a phase, a fifth of a second of dead air each is a
                    // luxury.
                    _gooseTimer += dt;
                    if (_gooseTimer >= 0.35f)
                    {
                        _goose.gameObject.SetActive(false);
                        if (State == Phase.Live) StartDive();
                        else _gooseState = GooseState.Gone;
                    }
                    break;

                case GooseState.Crashing:
                    _gooseTimer += dt;
                    if (_gooseTimer >= 0.5f)
                    {
                        _goose.gameObject.SetActive(false);
                        _gooseState = GooseState.Gone;
                        if (State == Phase.Live) _regroup = regroupSeconds;
                    }
                    break;
            }
        }

        void Advance(float dt)
        {
            _goosePrev = _goosePos;
            _goosePos += _gooseVel * dt;
            PlaceGoose();
        }

        void PlaceGoose()
        {
            if (_goose == null) return;

            Vector3 travel = _goosePos - _goosePrev;
            Quaternion rot = travel.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(travel.normalized, Vector3.up)
                : _goose.rotation;
            // No correction Euler: the parts are pre-oriented in BuildGoose so the root's +Z is forward.
            // That is what lets the body be squashed below without also rolling the heading.
            _goose.SetPositionAndRotation(_goosePos, rot);

            if (_gooseBody == null) return;
            // Squash along the direction of travel and bulge across it — the classic impact read, and
            // the cheapest thing on the whole juice list. The body's own local Y runs along the root's
            // +Z (see the capsule turn in BuildGoose), so travel is Y here and the two widths are X and Z.
            float s = _squash;
            _gooseBody.localScale = new Vector3(_gooseBodyScale.x * (1f + s * 0.45f),
                                               _gooseBodyScale.y * (1f - s * 0.40f),
                                               _gooseBodyScale.z * (1f + s * 0.45f));

            // Time.deltaTime rather than a threaded-through dt: PlaceGoose is called from several places
            // and only one of them has one, and this IS the dt the phase is driven with — the host passes
            // Time.deltaTime into Tick. Reading it here keeps the flap frozen during a hit stop for the
            // same reason, since timeScale is what the freeze bends.
            AnimateLimbs(Time.deltaTime);
        }

        /// <summary>
        /// Wingbeat while it flies, and a neck whip when it is hit.
        ///
        /// The neck, tail and wings were built and then never moved by anything: the bird crossed fifty
        /// metres as a rigid ornament and took a mower to the chest without so much as a twitch. Two of
        /// the goal's own words — "neck whip" and "wing flapping" — had geometry and no motion, which is
        /// the same trap as the crowd that was never in the scene and the audio with no director. Present
        /// in the build, absent from the game.
        ///
        /// The FLAP is a plain sine on the wings' rest pose, faster while diving than while sailing,
        /// because a bird beating hard is a bird committed to arriving. The WHIP shares the strike
        /// envelope: the neck snaps past its rest angle and the wings fold back, so the hit reads as
        /// having gone THROUGH the animal rather than having stopped at its outline.
        ///
        /// Driven by the scaled dt like everything else, so the flap freezes with the world during a hit
        /// stop instead of carrying on and giving away that the freeze is only skin deep.
        /// </summary>
        /// <summary>
        /// The tell: how close the bird is to being hittable, as a number the pose and the cue both read.
        ///
        /// The parry had NO anticipation at all. Every signal fired after contact — freeze, punch, debris,
        /// a word on screen — so the window could only ever be found by trial, never read. Juice that only
        /// happens on success teaches nothing, and "a clear anticipation pose and readable timing cue" is
        /// first on the goal's list for that reason.
        ///
        /// Measured in TIME rather than in distance, because the dive speed climbs with the rally: at
        /// 20 m/s the outer window is 0.4 s wide and at 32 m/s it is 0.25 s, so a fixed-distance tell would
        /// give less and less warning exactly as the exchange got harder. A time-based one gives the same
        /// warning at every speed, which is what makes a long rally hard because it is FAST rather than
        /// because it became unreadable.
        /// </summary>
        void UpdateTell()
        {
            float range = GooseRange;
            if (range < 0f)
            {
                _tell = 0f;
                _fx?.ClearTimingRing();
                return;
            }

            float speed = Mathf.Max(_gooseVel.magnitude, 1f);
            float secondsOut = Mathf.Max(range - parryRadius, 0f) / speed;
            // Full tell from a third of a second out. Less than that and it arrives with the bird; much
            // more and it is on screen so long it stops meaning "now".
            _tell = 1f - Mathf.Clamp01(secondsOut / 0.34f);

            if (_mower != null && _tell > 0.02f)
                _fx?.TimingRing(_mower.transform.position,
                                Mathf.Lerp(parryRadius * 2.2f, parryRadius, _tell), _tell,
                                Quaternion.LookRotation(
                                    Vector3.ProjectOnPlane(_mower.transform.forward, Vector3.up)
                                        .sqrMagnitude > 1e-4f
                                    ? Vector3.ProjectOnPlane(_mower.transform.forward, Vector3.up).normalized
                                    : _toOpponent, Vector3.up));
            else
                _fx?.ClearTimingRing();
        }

        void AnimateLimbs(float dt)
        {
            bool flying = _gooseState == GooseState.Diving || _gooseState == GooseState.Launched;
            // Beat harder on the way in; a launched bird is tumbling, not flying.
            float rate = _gooseState == GooseState.Diving ? 13f : 7f;
            if (flying) _flapPhase += dt * rate;

            float beat = flying ? Mathf.Sin(_flapPhase) : 0f;
            // Struck wings fold back and stop beating, which is what makes the flap's ABSENCE readable.
            float fold = _whip * 62f;
            // The ANTICIPATION POSE: wings flare wide and the beat stills as the bird commits. A held
            // pose reads as a wind-up where a faster flap would only read as more of the same motion,
            // and stillness against a beating silhouette is the clearest change a shape this size can
            // make. Suppressed once struck, so the tell cannot fight the impact.
            float tell = _tell * (1f - _whip);
            float flare = tell * 38f;
            float amp = Mathf.Lerp(34f, 6f, Mathf.Max(_whip, tell * 0.8f));

            if (_wingL != null)
                _wingL.localRotation = _wingRestL * Quaternion.Euler(0f, 0f, beat * amp - fold + flare);
            if (_wingR != null)
                _wingR.localRotation = _wingRestR * Quaternion.Euler(0f, 0f, -beat * amp + fold - flare);

            if (_neck != null)
            {
                // Past the rest angle rather than toward it: a whip overshoots, and the overshoot is the
                // whole reason it reads as a whip and not as a nod.
                // Drawn BACK on the tell and thrown forward on the whip, so the two read as one
                // gesture: wind up, then snap through.
                float lash = -_whip * 54f + tell * 22f + beat * (flying ? 5f : 0f);
                _neck.localRotation = _neckRest * Quaternion.Euler(lash, 0f, 0f);
            }
        }

        /// <summary>
        /// The goose gets through: it comes down in the garden and flattens beds.
        ///
        /// This is the whole cost of the phase, and it is deliberately unrecoverable — a flattened bed
        /// stays flat, exactly as a knocked gnome now stays knocked, so the damage is on the ground to
        /// be seen rather than a number on a card claiming it.
        /// </summary>
        void Crash()
        {
            _outcome.crashes++;
            Rally = 0;

            int remaining = _myCap - (_arena != null ? _arena.PlayerBedsLost : 0);
            int killed = _arena != null
                ? _arena.Damage(_diveTarget, crashRadius, true, bedsPerCrash, remaining) : 0;

            _goosePos = new Vector3(_diveTarget.x, 0.35f, _diveTarget.z);
            PlaceGoose();
            _gooseState = GooseState.Crashing;
            _gooseTimer = 0f;

            // The machine feels it even though nothing hit it — a goose coming down beside the duck at
            // speed. Scaled by how close, so a crash across the garden is not a shove.
            if (_mower != null)
            {
                float near = 1f - Mathf.Clamp01(
                    Vector3.Distance(_diveTarget, _mower.transform.position) / 12f);
                if (near > 0.08f) _mower.Bonk(_diveTarget, near * 0.45f);
            }

            _camera?.AddShake(0.7f);
            _hitStop = 0.05f;

            var audio = AudioDirector.Instance;
            if (audio != null)
            {
                audio.PlayOne(Pick(audio.bonks), 0.55f);
                audio.PlayOne(Pick(audio.debrisPings), 0.5f);
                audio.PlayOne(audio.quackPanic, 0.6f);
                audio.CrowdGroan(killed > 0 ? 0.7f : 0.4f);
            }
            LowThud(0.7f);
            Honk(0.9f);
            PunchJudges(-0.6f);
            // A goose in the player's beds is the rival's good news.
            ReactOpponent(0.8f, inTheirFavour: true);

            // Earth and leaves, no feathers and no ring. The goose was not struck here — borrowing the
            // parry's vocabulary would tell the player they had hit something when they had not.
            _fx?.Dirt(_diveTarget, killed > 0 ? 0.95f : 0.55f);

            // And the fence it came through.
            //
            // The segment has to CROSS THE PERIMETER, which is the whole point and which _goosePrev alone
            // cannot give: that is one frame back, a third of a metre, so the segment was a dot at the
            // crater. The fence stands 5 m out and the beds are 1.5–3.4 m in, so nothing was ever within
            // reach and the first version toppled exactly zero boards while reporting success.
            //
            // Extended backwards along the flight direction instead, far enough to reach the fence line
            // from anywhere in the garden.
            if (_arena != null)
            {
                Vector3 travel = _diveTarget - _goosePrev;
                travel.y = 0f;
                if (travel.sqrMagnitude < 1e-4f) travel = _toOpponent;
                Vector3 entry = _diveTarget - travel.normalized * (_arena.gardenHalfWidth + 6f);

                int boards = _arena.SmashFence(entry, _diveTarget, 2.6f, 3);
                if (boards > 0) _fx?.Dirt(_diveTarget, 0.4f);
            }
        }

        /// <summary>
        /// Work out what a thrown goose hit, from where it came down.
        ///
        /// By position, never by intention. A throw that falls short lands in the open ground between
        /// the gardens and harms nobody; one sent into the player's own flowers harms the player. That
        /// second case is the real cost of a scrappy hit, and it is why the tiers matter beyond the
        /// noise they make — only a good or perfect strike gets to choose where the bird goes.
        /// </summary>
        void LandLaunched()
        {
            if (_arena == null) return;
            Vector3 flat = new Vector3(_goosePos.x, 0f, _goosePos.z);

            if (_arena.IsInsideGarden(flat, playerSide: false))
            {
                var bed = _arena.NearestAlive(flat, false);
                int killed = _arena.Damage(bed != null ? bed.position : flat, crashRadius, false,
                                           bedsPerCrash, _theirCap - _arena.OpponentBedsLost);
                if (killed <= 0) return;

                _outcome.hitsOnOpponent++;
                AudioDirector.Instance?.CrowdCheer(0.75f);
                _crowd?.Excite(0.7f);
                PunchJudges(0.7f);
                return;
            }

            if (!_arena.IsInsideGarden(flat, playerSide: true)) return;

            var mine = _arena.NearestAlive(flat, true);
            int lost = _arena.Damage(mine != null ? mine.position : flat, crashRadius, true,
                                     bedsPerCrash, _myCap - _arena.PlayerBedsLost);
            if (lost <= 0) return;

            _outcome.ownGoals++;
            var audio = AudioDirector.Instance;
            audio?.CrowdGroan(0.6f);
            audio?.PlayOne(audio.quackAnnoyed, 0.6f);
            PunchJudges(-0.5f);
        }

        /// <summary>
        /// The launch velocity that carries a ballistic arc from one point to another in a given time.
        ///
        /// Standard projectile solve. Used only where the strike earned placement — everywhere else the
        /// goose is thrown along the heading and lands wherever the arc drops it.
        /// </summary>
        static Vector3 ArcTo(Vector3 from, Vector3 to, float seconds, float gravity)
        {
            float t = Mathf.Max(seconds, 0.05f);
            Vector3 d = to - from;
            return new Vector3(d.x / t, d.y / t + 0.5f * gravity * t, d.z / t);
        }

        // ------------------------------------------------------------------ camera & music energy

        void UpdateCameraEnergy(float dt)
        {
            float energy = Mathf.Clamp01(Rally / (float)Mathf.Max(rallyEnergyFull, 1));

            if (_camera != null)
            {
                // The camera does NOT chase the incoming bird.
                //
                // This overrides the brief I was working to, which asked for the aim to bias toward the
                // goose while it closed. On the player's own verdict — "날라올때 카메라가 그걸 안봤으면
                // 좋겠고" — that framing is unwanted, and they are the authority on how it feels. It also
                // has a reason behind it: the lens drifting off the machine while a bird converges on you
                // takes the frame away from the thing you are steering at exactly the moment steering
                // matters, so the camera fights the player for the last third of a second.
                //
                // A LAUNCHED goose still gets followed, because that is the ball travelling and it costs
                // the player nothing — they are not aiming at anything while it is in the air.
                if (_gooseState == GooseState.Launched)
                {
                    _camera.SetDefenceSubject(_goose, energy);
                }
                else if (_gooseState == GooseState.Diving)
                {
                    // Held on the far goal, as it is between exchanges. The bird is readable in the frame
                    // because it is skylined against blue, not because the camera turned to look at it.
                    _camera.SetDefenceSubject(_arena != null ? _arena.OpponentMarker : null,
                                              energy, biasScale: 0.4f);
                }
                else
                {
                    // Between exchanges the camera holds the OPPONENT'S board instead of snapping back
                    // to the mower's tail. Without this the chase camera looks wherever the duck is
                    // pointing, which is usually at its own feet, and the far garden — the thing every
                    // throw is aimed at — was never in shot at all. At a third of the bias so the duck
                    // keeps the foreground: full bias pitched the lens so far up the field that the
                    // machine sat on the bottom edge.
                    _camera.SetDefenceSubject(_arena != null ? _arena.OpponentMarker : null,
                                              energy, biasScale: 0.55f);
                }
            }

            if (_voiceRally != null && _voiceRally.clip != null)
            {
                // The urgent layer is an authored overlay for the round loop, and a rally is what it
                // was made for. Faded rather than switched, so the escalation is felt before it is
                // noticed.
                float want = Mathf.Clamp01(Rally / 3f) * 0.55f;
                _voiceRally.volume = Mathf.MoveTowards(_voiceRally.volume, want, dt * 0.9f);
                // And the PITCH climbs with it. The volume ramp alone made a long rally louder without
                // making it more urgent, and "escalate through audio pitch" was the one escalation
                // channel on the goal's list with nothing behind it — the pitch was set once when the
                // voice was made and never touched again. A fifth up across a full rally is enough to
                // feel as tightening rather than to hear as a tape speeding up.
                _voiceRally.pitch = Mathf.Lerp(1f, 1.34f, energy);
                if (want > 0.01f && !_voiceRally.isPlaying) _voiceRally.Play();
            }
        }

        // ------------------------------------------------------------------ audio

        /// <summary>
        /// Three voices this phase owns, because each needs a pitch of its own and AudioDirector's
        /// one-shot sources are shared.
        ///
        /// Two sounds the brief asks for are not in the bank, and neither needs new art: a comic goose
        /// honk is the duck's annoyed quack pitched well down — both are waterfowl calls, and the
        /// register lands where the brief wants it — and the heavy low end under an impact is a second
        /// voice of the same bonk clip pitched down hard, which is the standard way to manufacture
        /// weight. Everything else routes through AudioDirector so the mix stays in one place.
        /// </summary>
        void BuildVoices()
        {
            var audio = AudioDirector.Instance;
            if (audio == null) return;

            _voiceHonk ??= MakeVoice("DefenceHonk", audio.quackAnnoyed, 0.55f, false);
            _voiceLow ??= MakeVoice("DefenceLowEnd", Pick(audio.bonks), 0.5f, false);
            _voiceRally ??= MakeVoice("DefenceRallyLayer", audio.musicRoundUrgent, 1f, true);
        }

        AudioSource MakeVoice(string name, AudioClip clip, float pitch, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = loop;
            src.playOnAwake = false;
            src.pitch = pitch;
            // 2D, matching AudioDirector: the camera moves a great deal and a spatialised mix swims.
            src.spatialBlend = 0f;
            src.volume = 0f;
            return src;
        }

        /// <summary>The honk itself — the parry input, whether or not it connects.</summary>
        void Honk(float volume = 0.7f)
        {
            AudioDirector.Instance?.PlayHorn();
            if (_voiceHonk == null || _voiceHonk.clip == null) return;
            _voiceHonk.pitch = 0.5f + (float)_rng.NextDouble() * 0.1f;
            _voiceHonk.volume = volume;
            _voiceHonk.Play();
        }

        void LowThud(float volume)
        {
            if (_voiceLow == null || _voiceLow.clip == null) return;
            _voiceLow.pitch = 0.45f + (float)_rng.NextDouble() * 0.1f;
            _voiceLow.volume = volume;
            _voiceLow.Play();
        }

        /// <summary>
        /// Graded by tier rather than fired wholesale, so the player can HEAR which tier they got
        /// without reading anything. That is also the cheapest answer to "do not confuse juice with
        /// noise": the loudest layers only exist on the hits that earned them.
        /// </summary>
        void PlayStrikeAudio(Tier tier)
        {
            var audio = AudioDirector.Instance;
            if (audio == null) return;

            audio.PlayOne(Pick(audio.bonks), tier == Tier.Perfect ? 0.85f : 0.6f);
            audio.PlayOne(Pick(audio.suspensionBumps), 0.4f);
            if (tier == Tier.Normal) return;

            audio.PlayOne(Pick(audio.debrisPings), 0.45f);
            LowThud(tier == Tier.Perfect ? 0.9f : 0.6f);
            if (tier != Tier.Perfect) return;

            audio.PlayOne(audio.quackProud, 0.7f);
        }

        /// <summary>
        /// The rival reacting — the other half of "a distinct character reaction from BOTH competitors".
        ///
        /// They stood in their own garden for this entire phase and never moved: not when a goose came
        /// down in their flowers, not when the player put one there on purpose. Eighth instance of the
        /// same fault in this phase, and the most conspicuous, because the rival is the thing every throw
        /// is aimed AT.
        ///
        /// A lean and a hop rather than an animation: the NPC is a greybox capsule and giving it a rig
        /// would be building an asset to solve a timing problem. Sign tells which way it read the event —
        /// recoiling backward when it is their beds, drawing up when it is yours.
        /// </summary>
        void ReactOpponent(float amount, bool inTheirFavour)
        {
            var npc = _arena != null ? _arena.opponentNpc : null;
            if (npc == null) return;
            _npcKick = Mathf.Clamp(amount * (inTheirFavour ? 1f : -1f), -1.2f, 1.2f);
            _npcRest = _npcRest == default ? npc.localRotation : _npcRest;
        }

        /// <summary>Runs the rival's lean down, on real time so a hit stop holds the pose.</summary>
        void StepOpponent(float real)
        {
            var npc = _arena != null ? _arena.opponentNpc : null;
            if (npc == null || Mathf.Abs(_npcKick) < 0.001f) return;

            if (_npcRest == default) _npcRest = npc.localRotation;
            _npcKick = Mathf.MoveTowards(_npcKick, 0f, real * 2.2f);

            // Tipped back on bad news, drawn upright and forward on good. Both about its own lateral
            // axis, so from the player's end of the pitch it reads as body language rather than as spin.
            npc.localRotation = _npcRest * Quaternion.Euler(_npcKick * 26f, 0f, _npcKick * 9f);
        }

        void ReactCrowd(Tier tier)
        {
            float amount = tier == Tier.Perfect ? 0.9f : tier == Tier.Good ? 0.55f : 0.3f;
            AudioDirector.Instance?.CrowdCheer(amount);
            _crowd?.Excite(amount);
            PunchJudges(amount);
        }

        /// <summary>The bench reacts. Already built for scoring a mark; a rally is the same gesture.</summary>
        void PunchJudges(float amount)
        {
            // Asked of the director first and FOUND IN THE SCENE otherwise — the seventh time this
            // exact fault has turned up in this phase. The arena has no GameDirector, so this returned
            // immediately and the bench never reacted to anything: three judges sitting in frame,
            // visibly present, watching a rally without moving. Cached because it is hit on every
            // parry and every crash.
            var panel = GameDirector.Instance != null ? GameDirector.Instance.judges : null;
            if (panel == null) panel = _judgePanel ??= FindFirstObjectByType<JudgePanel>();
            if (panel == null || panel.judges == null || panel.judges.Length == 0) return;

            // One judge, not all three. Three animals applauding in unison on every hit is a chorus
            // line; one reacting is a crowd of individuals.
            panel.judges[_rng.Next(panel.judges.Length)]?.character?.Punch(amount);
        }

        AudioClip Pick(AudioClip[] clips)
            => clips == null || clips.Length == 0 ? null : clips[_rng.Next(clips.Length)];

        // ------------------------------------------------------------------ opponent

        /// <summary>
        /// Whose garden is across the way: whoever leads the championship.
        ///
        /// Points first, because that is the contestant who can still beat the player, with live
        /// coverage as the tiebreak — which is what decides it in round one, when nobody has points
        /// and "the leader" can only mean whoever is ahead on this lawn right now.
        /// </summary>
        RivalContestant PickOpponent()
        {
            var t = Tournament.Instance;
            if (t == null || t.rivals == null) return null;

            var table = t.Championship != null ? t.Championship.Table : null;
            RivalContestant best = null;
            int bestPoints = int.MinValue;
            float bestCoverage = -1f;

            foreach (var r in t.rivals)
            {
                if (r == null) continue;
                int points = 0;
                if (table != null)
                    foreach (var e in table)
                        if (string.Equals(e.name, r.displayName)) { points = e.points; break; }

                if (points < bestPoints) continue;
                if (points == bestPoints && r.Coverage <= bestCoverage) continue;
                best = r; bestPoints = points; bestCoverage = r.Coverage;
            }
            return best;
        }

        Material Flat(Color c)
        {
            var sh = Shader.Find("Duck/Prop")
                  ?? Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Sprites/Default");
            if (sh == null) return null;

            var m = new Material(sh) { name = "DefenceGreybox", hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            // Unity's primitives carry no vertex colours and the workhorse shader multiplies by them,
            // which comes out black rather than coloured.
            if (m.HasProperty("_VertexColorAmount")) m.SetFloat("_VertexColorAmount", 0f);
            return m;
        }

        // ------------------------------------------------------------------ readout

        /// <summary>
        /// The review numbers, in the crudest form that works. IMGUI rather than the HUD because this
        /// is a measuring instrument, not presentation, and it leaves with the greybox. A real rally
        /// counter on screen is on the list for the presentation pass.
        /// </summary>
        void OnGUI()
        {
            if (!showReadout || State == Phase.Idle || State == Phase.Done) return;

            if (_readout == null)
            {
                _readout = new GUIStyle(GUI.skin.label) { fontSize = 16 };
                _readout.normal.textColor = Color.white;
            }

            float left = State == Phase.Live ? Mathf.Max(0f, liveSeconds - _stateTime) : 0f;
            Tier now = Judge();
            float dist = _gooseState == GooseState.Diving && _mower != null
                ? Vector3.Distance(_goosePos, _mower.transform.position) : -1f;
            float v = CurrentDiveSpeed();

            GUI.Box(new Rect(12f, 12f, 400f, 244f), GUIContent.none);
            Line(0, $"RALLY (greybox)   {State}   {left:0.0} s");
            Line(1, $"WASD drive · E honk    goose {_gooseState}");
            Line(2, $"rally x{Rally}   dive {v:0.0} m/s   " +
                    (_recovery > 0f ? "RECOVERING" : now != Tier.Miss ? now.ToString().ToUpper() : "—") +
                    (_armedRemaining > 0 ? $"   [armed {_armedTier} x{_armedRemaining}]" : ""));
            Line(3, $"range {(dist >= 0f ? dist.ToString("0.0") + " m" : "—")}   " +
                    $"P<{perfectRadius:0.0} G<{goodRadius:0.0} N<{parryRadius:0.0}");
            // The windows are not settings, they are 2r/v — so print them, because they are the thing
            // being judged and they change every exchange.
            Line(4, $"windows  P {2f * perfectRadius / v:0.00}s  G {2f * goodRadius / v:0.00}s  " +
                    $"N {2f * parryRadius / v:0.00}s");
            Line(5, $"parries {_outcome.parries} ({_outcome.perfects}P {_outcome.goods}G " +
                    $"{_outcome.normals}N)   best x{_outcome.longestRally}");
            Line(6, $"my beds {(_arena != null ? _arena.PlayerBedsLost : 0)}/{_outcome.myBedsTotal} " +
                    $"flat (cap {_myCap})");
            Line(7, $"{_outcome.opponent} beds {(_arena != null ? _arena.OpponentBedsLost : 0)}/" +
                    $"{_outcome.theirBedsTotal} flat (cap {_theirCap})");
            Line(8, $"crashes {_outcome.crashes}   wasted honks {_outcome.whiffs}   " +
                    $"on them {_outcome.hitsOnOpponent}   own goals {_outcome.ownGoals}");
            Line(9, State == Phase.Awarding
                        ? $"AWARD  {(Award >= 0 ? "+" : "")}{Award}   (added to this round's score)"
                        : $"award so far  {(ComputeAward(_outcome, awardFloor, awardCeiling) >= 0 ? "+" : "")}" +
                          $"{ComputeAward(_outcome, awardFloor, awardCeiling)}");
        }

        void Line(int row, string text)
            => GUI.Label(new Rect(22f, 18f + row * 23f, 384f, 22f), text, _readout);

        GUIStyle _readout;
    }
}
