using System.Collections.Generic;
using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The match: one 1v1v1v1 free-for-all, four gardens, a shared flock and a clock.
    ///
    /// Not four duels happening next to each other. There is one pool of geese and any of them can
    /// be sent at any of the four, so what the player does to the machine on their left is visible
    /// to the two who are not involved and changes what comes back at them. That is the difference
    /// between a party game and a tournament bracket, and it is why the goose count is global rather
    /// than per-competitor.
    ///
    /// The count is also the difficulty curve, and it is deliberately slow: ONE goose for the first
    /// third, so the rule — hit it, it comes back angrier, hit it again, it dies — is learned on a
    /// clean board. Two through the middle. Three only for the finish. Any more and the arena stops
    /// being readable, which is a different game from a hard one.
    ///
    /// Everything is stepped from here on the scaled clock, including the geese, so a hit stop
    /// freezes the whole board rather than freezing the camera while the birds carry on.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class RallyDirector : MonoBehaviour
    {
        public enum Phase { Idle, Intro, Live, Settle, Done }

        [Header("Cast")]
        public RallyCompetitor[] competitors = new RallyCompetitor[0];

        [Header("Flock")]
        public RallyGoose goosePrefab;
        [Tooltip("Where pooled geese are parented. Keeps the hierarchy readable during play.")]
        public Transform flockParent;
        [Tooltip("Instances made at load. One spare above the hard maximum, so a refill never allocates.")]
        public int poolSize = 10;
        [Tooltip("How much bigger than life the geese are.\n\n" +
                 "A goose is about 1.4 m across the wings and the arena is ninety metres wide, so at " +
                 "true scale the thing the entire mode is about is four pixels of grey on green from " +
                 "the far side. Oversizing is the standard arcade answer and it is the right one here: " +
                 "the bird has to be the most legible object on the pitch from any corner of it.")]
        [Range(1f, 3f)] public float gooseScale = 1.75f;

        [Header("Match")]
        [Tooltip("Seconds the opening overhead shot of the arena is held.")]
        public float introSeconds = 4.4f;
        [Tooltip("Seconds the camera takes to fall from the map shot onto the machine. Part of " +
                 "introSeconds, not on top of it.")]
        public float introDropSeconds = 1.9f;
        public float matchSeconds = 78f;
        public float settleSeconds = 3.2f;
        [Tooltip("Hard ceiling on geese in play at once. The rest of the curve builds up to this.")]
        [Range(1, 12)] public int maxActiveGeese = 9;
        [Tooltip("Fractions of the match at which each extra goose is let in. " +
                 "A list rather than two named thresholds, so the ceiling can be raised without " +
                 "inventing a 'fourthGooseAt' and a 'fifthGooseAt' beside the others. The first " +
                 "entry is when the SECOND bird arrives; there is always one from the start. " +
                 "The shape is what matters more than the numbers: one goose for long enough that " +
                 "the rule is learned on a clean board, then a steady build, then a genuinely busy " +
                 "finish. Every step is announced, so the arena getting harder is something the " +
                 "player is told rather than something that just happens to them.")]
        public float[] extraGooseAt = { 0.10f, 0.20f, 0.31f, 0.42f, 0.53f, 0.64f, 0.75f, 0.86f };
        [Tooltip("Seconds between a slot emptying and the next goose being served into it. Long " +
                 "enough that the player sees the board clear and knows why the next one arrived.")]
        public float refillDelay = 0.85f;
        [Tooltip("Extra delay on the very first serve, so the arena is established before it starts.")]
        public float firstServeDelay = 0.6f;
        [Tooltip("How much more of the flock is sent at the PLAYER than at a rival. 1 is an even " +
                 "four-way split, which measured fair and played wrong — see PickServeTarget.")]
        [Range(1f, 3f)] public float playerServeBias = 1.7f;

        [Header("Horn")]
        [Tooltip("Metres the horn drives geese back over. Matches the wave that is drawn for it.")]
        public float hornReach = 11f;
        [Tooltip("Half-width of the arc it works in. The same number the wave is drawn at, so what " +
                 "the player sees and what the horn does are one shape.")]
        public float hornHalfAngle = 42f;
        [Tooltip("Seconds before it can be sounded again. Without this it is a hold-to-win button.")]
        public float hornCooldown = 3.2f;

        [Header("Impact")]
        public float hitStopNormal = 0.05f;
        public float hitStopGood = 0.08f;
        public float hitStopPerfect = 0.12f;
        public float hitStopKnockout = 0.16f;
        [Range(0.02f, 1f)] public float slowMotionScale = 0.36f;
        public float slowMotionSeconds = 0.24f;
        [Tooltip("Timescale during the moment before a perfect strike. The dip that makes good " +
                 "timing feel like it was noticed.")]
        [Range(0.2f, 1f)] public float anticipationScale = 0.62f;

        [Header("Camera")]
        public CameraDirector cameraDirector;
        [Tooltip("Camera punch for a strike the PLAYER made. Nobody else's shakes the player's lens.")]
        public float punchNormal = 0.4f;
        public float punchGood = 0.7f;
        public float punchPerfect = 1.0f;

        [Header("Diagnostics")]
        public int seed = 4242;
        [Tooltip("Log every serve, strike and breach. On during development; noisy in a build.")]
        public bool verbose = true;
        [Tooltip("Put a bot on the PLAYER's machine too, so the match plays itself.\n\n" +
                 "For capture and for review, and it is not a luxury: with nobody at the keyboard the " +
                 "player's garden absorbs every redirect in the arena, so the sheet is four minutes of " +
                 "one contestant being demolished and nothing else is ever exercised. It is also the " +
                 "sharpest test of the bots there is — if a match between four of them is not worth " +
                 "watching, the mode is not finished.")]
        public bool autopilotPlayer;

        /// <summary>
        /// Editor-only override for the above, set by the capture tools just before play.
        ///
        /// Static and never serialized, which is the whole point: a debug driver that can be SAVED
        /// into the scene is a debug driver that eventually ships, and the one thing worse than a
        /// mode nobody can review is a mode that plays itself in front of a player.
        /// </summary>
        public static bool DebugAutopilotPlayer;
        [Tooltip("Seconds between full board readouts — every competitor's speed, target and distance, " +
                 "and every goose's phase. Zero is off. This is how a match that produces no parries " +
                 "is told apart from bots that never moved; the log of breaches alone cannot.")]
        public float traceInterval = 0f;

        // ------------------------------------------------------------------ live state

        public Phase State { get; private set; } = Phase.Idle;

        /// <summary>
        /// Is the pitch in play? False through the opening map shot and once the match is over.
        ///
        /// The one thing every driver has to agree on. Nothing moved this until now: the bots drove
        /// from the frame the scene loaded and the player could too, so the establishing shot looked
        /// down on four machines already scrambling around a match that had not started. An
        /// establishing shot exists to show an untouched board.
        /// </summary>
        public bool Running => State == Phase.Live || State == Phase.Settle;
        public float TimeRemaining { get; private set; }

        /// <summary>
        /// Run the clock out now. Review tooling only — the closing beats cost 78s to reach.
        ///
        /// Works from the intro too. It used to require Live and silently did nothing when called a
        /// second too early, while the caller logged success — which is worse than not having it.
        /// </summary>
        public string EndNow()
        {
            if (State == Phase.Intro)
            {
                State = Phase.Live;
                _phaseTimer = 0f;
                _droppedIn = true;
                cameraDirector?.SetMode(CameraMode.Chase, 0.4f);
            }
            if (State != Phase.Live) return $"not running (state {State})";
            TimeRemaining = 0.001f;
            return "clock run out";
        }
        public float Elapsed => Mathf.Max(0f, matchSeconds - TimeRemaining);
        public float MatchFraction => matchSeconds > 0f ? Mathf.Clamp01(Elapsed / matchSeconds) : 0f;
        public bool Finished => State == Phase.Done;
        public int ActiveGeese { get; private set; }
        public int AllowedGeese { get; private set; } = 1;
        public IReadOnlyList<RallyGoose> Flock => _flock;

        /// <summary>The player's competitor, or null in a scene wired wrong.</summary>
        public RallyCompetitor Player { get; private set; }

        /// <summary>Last thing worth shouting about, and how long ago. Read by the HUD.</summary>
        public string Callout { get; private set; } = "";
        public float CalloutAge { get; private set; } = 99f;
        public Color CalloutColour { get; private set; } = Color.white;

        public event System.Action<RallyCompetitor, RallyStrike.Tier, int> OnStrike;
        public event System.Action<RallyCompetitor, RallyCompetitor> OnKnockout;   // striker, victim (may be null)
        public event System.Action<RallyCompetitor, int> OnBreach;                 // defender, beds lost

        readonly List<RallyGoose> _flock = new(6);
        readonly RallyCompetitor[] _bySlot = new RallyCompetitor[RallyArena.Count];
        System.Random _rng;
        float _phaseTimer;
        float _serveTimer;
        float _freeze;          // unscaled seconds of hit stop left
        float _slow;            // unscaled seconds of slow motion left
        float _anticipation;    // unscaled seconds of pre-impact dip left
        int _lastServedAt = -1;
        readonly float[] _lastServeTime = new float[RallyArena.Count];
        bool _warpHeld;
        readonly float[] _serveWeights = new float[RallyArena.Count];

        // ------------------------------------------------------------------ setup

        void Awake()
        {
            _rng = new System.Random(seed);

            foreach (var c in competitors)
            {
                if (c == null) continue;
                _bySlot[Mathf.Clamp(c.slot, 0, RallyArena.Count - 1)] = c;
                if (c.isPlayer) Player = c;
            }

            BuildPool();
        }

        void BuildPool()
        {
            if (goosePrefab == null)
            {
                Debug.LogError("[Rally] no goose prefab — the match has nothing to play with.");
                return;
            }
            int want = Mathf.Max(poolSize, maxActiveGeese + 1);
            for (int i = 0; i < want; i++)
            {
                var g = Instantiate(goosePrefab, flockParent != null ? flockParent : transform);
                g.name = $"Goose {i}";
                g.transform.localScale = Vector3.one * gooseScale;
                // The ground clips are authored against a contact plane 0.43 m under the origin, so
                // scaling the bird scales that offset with it. Getting this wrong is a goose that
                // wades through the lawn up to its knees.
                g.deckOffset *= gooseScale;
                g.Init(this);
                g.SetGroundHeight(0f);
                g.OnRetired += HandleRetired;
                _flock.Add(g);
            }
        }

        public RallyCompetitor CompetitorAt(int slot)
            => (slot >= 0 && slot < _bySlot.Length) ? _bySlot[slot] : null;

        public RallyGarden GardenAt(int slot)
        {
            var c = CompetitorAt(slot);
            return c != null ? c.garden : null;
        }

        // ------------------------------------------------------------------ the match

        public void Begin()
        {
            ReleaseWarp();
            foreach (var g in _flock) g.Retire(false);
            ActiveGeese = 0;
            // A fresh strip. Last match's tracks are last match's history.
            RallyTracks.Instance?.Clear();

            int n = 0;
            foreach (var c in competitors)
            {
                if (c == null) continue;

                if ((autopilotPlayer || DebugAutopilotPlayer) && c.isPlayer && c.brain == null)
                {
                    // Added at Begin rather than baked, so the scene on disk is never one where the
                    // player is a bot — a debug driver that can be saved into the level is a debug
                    // driver somebody ships.
                    var bot = c.gameObject.AddComponent<RallyBrain>();
                    bot.competitor = c;
                    bot.skill = 0.62f;
                    c.brain = bot;
                    if (c.mower != null) c.mower.inputSource = bot;
                }

                c.ResetForMatch();
                if (c.brain != null) c.brain.Bind(this, seed + 977 * (++n));
            }

            TimeRemaining = matchSeconds;
            AllowedGeese = 1;
            _serveTimer = firstServeDelay + introSeconds;
            _lastServedAt = -1;
            for (int i = 0; i < _lastServeTime.Length; i++) _lastServeTime[i] = Time.time;
            Callout = "";
            CalloutAge = 99f;
            State = Phase.Intro;
            _phaseTimer = 0f;
            _droppedIn = false;
            System.Array.Clear(_hornCd, 0, _hornCd.Length);
            System.Array.Clear(_hornThink, 0, _hornThink.Length);

            // Open on the map. Snapped rather than blended — there is nothing to blend FROM at the
            // top of a match, and a one-second slide out of whatever the camera happened to be
            // looking at is the sort of thing that reads as a bug.
            if (cameraDirector != null)
            {
                cameraDirector.SetMode(CameraMode.Reveal, 0.01f);
                cameraDirector.SnapToCurrent();
            }

            AudioDirector.Instance?.PlayRallyMusic();

            if (verbose) Debug.Log($"[Rally] match begins: {matchSeconds:0}s, up to {maxActiveGeese} geese.");
        }

        /// <summary>Stop the match dead and put the world back. Safe to call at any time, twice.</summary>
        public void Abort()
        {
            foreach (var g in _flock) g.Retire(false);
            ActiveGeese = 0;
            ReleaseWarp();
            cameraDirector?.ClearDefenceSubject();
            cameraDirector?.SetRallyEnergy(0f);
            State = Phase.Idle;
        }

        void OnDisable() => ReleaseWarp();

        void Update()
        {
            if (State == Phase.Idle || State == Phase.Done) return;

            TickWarp(Time.unscaledDeltaTime);
            float dt = Time.deltaTime;
            _phaseTimer += dt;
            CalloutAge += Time.unscaledDeltaTime;

            switch (State)
            {
                case Phase.Intro:
                    // The establishing shot drops onto the machine, it does not cut. The blend is
                    // started before the phase ends so the descent is finished by the time the
                    // first goose is served rather than still travelling through it.
                    if (_phaseTimer >= introSeconds - introDropSeconds && !_droppedIn)
                    {
                        _droppedIn = true;
                        cameraDirector?.SetMode(CameraMode.Chase, introDropSeconds);
                    }
                    if (_phaseTimer >= introSeconds) { State = Phase.Live; _phaseTimer = 0f; }
                    break;

                case Phase.Live:
                    TimeRemaining -= dt;
                    UpdateAllowance();
                    UpdateServing(dt);
                    if (TimeRemaining <= 0f)
                    {
                        TimeRemaining = 0f;
                        State = Phase.Settle;
                        _phaseTimer = 0f;
                        // Whatever is in the air beats its way out over the barrier rather than being
                        // deleted on the horn. A goose vanishing mid-arc is the cheapest-looking thing
                        // a mode can do, and the settle beat exists to give them somewhere to go.
                        foreach (var g in _flock) if (g.Active) g.Dismiss();
                        Announce("TIME!", Color.white);
                        AudioDirector.Instance?.CrowdCheer(0.9f, applaud: true);
                    }
                    break;

                case Phase.Settle:
                    if (_phaseTimer >= settleSeconds) Finish();
                    break;
            }

            // The player's own controls, gated on the same flag the bots read. Written every frame
            // rather than latched on the transition: a latch is one missed state change away from
            // leaving a player unable to drive for the rest of the match.
            var input = InputReader.Instance;
            if (input != null) input.DrivingEnabled = Running;

            TickFlock(dt);
            TickStrikes();
            TickHorn();
            TickCamera();
            TickTrace(Time.unscaledDeltaTime);
        }

        float _traceTimer;

        /// <summary>
        /// The board, in one line per actor.
        ///
        /// Exists because "nobody parried anything" has at least four different causes that produce
        /// identical logs — bots not driving, bots driving to the wrong place, the strike test never
        /// firing, or the birds arriving somewhere nobody can reach — and no amount of staring at a
        /// list of breaches separates them.
        /// </summary>
        void TickTrace(float unscaled)
        {
            if (traceInterval <= 0f) return;
            _traceTimer -= unscaled;
            if (_traceTimer > 0f) return;
            _traceTimer = traceInterval;

            var sb = new System.Text.StringBuilder(256);
            sb.Append($"[Rally] t{TimeRemaining:00} geese {ActiveGeese}/{AllowedGeese} " +
                      $"ts{Time.timeScale:0.00} fdt{Time.fixedDeltaTime:0.0000} " +
                      $"scripted{(SimClock.Scripted ? 1 : 0)} " +
                      $"fps{(1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f)):0}");
            foreach (var c in competitors)
            {
                if (c == null) continue;
                var rb = c.mower != null ? c.mower.GetComponent<Rigidbody>() : null;
                float speed = rb != null ? new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z).magnitude : 0f;
                var threat = NearestThreatTo(c.slot);
                float range = threat != null ? Vector3.Distance(threat.transform.position, c.Position) : -1f;
                // The player's numbers come from the keyboard, not from a brain they do not have.
                // Reading c.brain for everybody printed a flat zero for the player whatever they
                // were doing, which is how a machine visibly driving itself across the dirt was
                // logged for twenty minutes as stationary with no throttle.
                var input = InputReader.Instance;
                float thr = c.brain != null ? c.brain.Throttle : (input != null ? input.Throttle : 0f);
                float st = c.brain != null ? c.brain.Steer : (input != null ? input.Steer : 0f);
                // Active state included because a competitor that has stopped moving and a competitor
                // whose GameObject has been switched off look identical in every other column, and
                // the two have completely different causes.
                string live = c.gameObject.activeInHierarchy ? "" : " OFF";
                sb.Append($" | {c.Name}{live} v{speed:0.0} thr{thr:+0.00;-0.00} st{st:+0.00;-0.00} " +
                          $"range{range:0.0}");
            }
            foreach (var g in _flock)
            {
                if (!g.Active) continue;
                sb.Append($" | goose {g.Phase} p{g.Parried}→{g.TargetSlot} " +
                          $"r{new Vector2(g.transform.position.x, g.transform.position.z).magnitude:0.0}");
            }
            Debug.Log(sb.ToString());
        }

        void UpdateAllowance()
        {
            float f = MatchFraction;
            int want = 1;
            if (extraGooseAt != null)
                foreach (float at in extraGooseAt)
                    if (f >= at) want++;
            want = Mathf.Min(want, maxActiveGeese);
            if (want == AllowedGeese) return;

            AllowedGeese = want;
            string[] words = { "", "", "TWO GEESE!", "THREE GEESE!", "FOUR GEESE!", "FIVE GEESE!" };
            Announce(want < words.Length ? words[want] : $"{want} GEESE!",
                     new Color(1f, 0.72f, 0.24f));
            AudioDirector.Instance?.CrowdCheer(0.6f);
            if (verbose) Debug.Log($"[Rally] allowance up to {AllowedGeese} at {f:0.00} of the match.");
        }

        void UpdateServing(float dt)
        {
            if (ActiveGeese >= AllowedGeese) { _serveTimer = Mathf.Max(_serveTimer, refillDelay * 0.5f); return; }
            _serveTimer -= dt;
            if (_serveTimer > 0f) return;
            Serve();
            _serveTimer = refillDelay;
        }

        void Serve()
        {
            var g = FreeGoose();
            if (g == null)
            {
                // Loud, because the silent version of this is a match that quietly stops producing
                // geese and runs its clock down over an empty arena. If the pool is genuinely
                // exhausted the allowance and the pool size disagree, which is a wiring fault.
                Debug.LogWarning($"[Rally] wanted to serve ({ActiveGeese}/{AllowedGeese} in play) but " +
                                 $"every one of the {_flock.Count} pooled geese is still in use.");
                _serveTimer = refillDelay;
                return;
            }

            int target = PickServeTarget();
            _lastServedAt = target;
            _lastServeTime[target] = Time.time;
            g.Serve(target);
            ActiveGeese++;
            GardenAt(target)?.Alarm(0.55f);
            if (verbose) Debug.Log($"[Rally] serve at {RallyArena.Get(target).contestant} " +
                                   $"({ActiveGeese}/{AllowedGeese} in play).");
        }

        RallyGoose FreeGoose()
        {
            foreach (var g in _flock) if (g != null && !g.Active) return g;
            return null;
        }

        /// <summary>
        /// Who the next goose is served at.
        ///
        /// Weighted toward whoever still has the most garden left, which is the only rule that keeps
        /// a four-way from turning into a rout: fall behind and the flock starts leaving you alone,
        /// pull ahead and it comes for you. Never the same garden twice running while there is a
        /// choice, because two serves at one corner reads as the game having it in for somebody.
        /// </summary>
        int PickServeTarget()
        {
            float total = 0f;
            var weights = _serveWeights;
            for (int i = 0; i < RallyArena.Count; i++)
            {
                var c = CompetitorAt(i);
                float integrity = c != null ? c.Integrity : 1f;
                // 0.6 .. 1.3, not 0.25 .. 1.25.
                //
                // The catch-up rule is right — fall behind and the flock eases off — but squaring the
                // integrity made it savage: a garden at 30 percent drew a ninth of the traffic of a
                // full one, so the moment a competitor started losing they were left with nothing to
                // do for the rest of the match. That is the opposite of a comeback mechanic; it
                // removes the player from their own game. A two-to-one spread still rewards
                // defending well without ever making anybody a spectator.
                float w = 0.6f + integrity * 0.7f;
                if (i == _lastServedAt) w *= 0.35f;
                // Already busy? Spread the flock out rather than piling on one corner. 0.75 rather
                // than 0.4 because this MULTIPLIES: at 0.4 a corner with two birds on it was down
                // to sixteen percent and one with three to six, so once a competitor drew a crowd
                // the flock refused to send them anything else for a long time afterwards.
                foreach (var g in _flock) if (g.Active && g.TargetSlot == i) w *= 0.75f;

                // Nobody starves.
                //
                // Everything above is a weighted roll, and a weighted roll can leave one corner
                // untouched for a long stretch purely by luck — which from that seat is
                // indistinguishable from the game having forgotten them. So the longer a garden has
                // gone unserved, the heavier it gets, up to double after twelve seconds. It is a
                // floor on attention rather than a thumb on the scale: a competitor who is being
                // served normally never accumulates enough wait for it to matter.
                w *= 1f + Mathf.Min((Time.time - _lastServeTime[i]) / 12f, 1f);

                // The player gets MORE than a fair share, deliberately.
                //
                // Counting forty serves after the weighting was evened up gave DUCK 8, MARGOT 8,
                // HORACE 11, BRAMBLE 13 — the player on twenty percent against a fair twenty-five,
                // which is inside one standard deviation of a weighted roll. So the distribution
                // was not the fault. The DESIGN was: an even split means three quarters of the
                // match is spent watching somebody else's goose, and in a mode whose only verb is
                // parrying that is most of the match spent not playing.
                //
                // Fair is the wrong target for the one seat with a person in it. At 1.7 the player
                // sees about a third of the flock — still plainly a four-way, with the other three
                // visibly under attack, but the human is the busiest defender on the pitch.
                if (playerServeBias > 0f && c != null && c.IsPlayer) w *= playerServeBias;
                weights[i] = w;
                total += w;
            }

            double roll = _rng.NextDouble() * total;
            for (int i = 0; i < RallyArena.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0d) return i;
            }
            return RallyArena.Count - 1;
        }

        void TickFlock(float dt)
        {
            // Counted from the flock every frame rather than tracked by events.
            //
            // It was event-tracked — incremented on a serve, recomputed on a retire — and it went
            // stale: a trace showed "geese 1/2" for twenty seconds with no bird anywhere in the
            // arena, which is the worst possible failure for this counter because it is what gates
            // the refill. One phantom goose and the flock never comes back for the rest of the
            // match, silently, with the timer running down over an empty pitch.
            //
            // There are four of them. Counting is not worth being clever about, and a number derived
            // from the truth cannot disagree with it.
            int live = 0;
            foreach (var g in _flock)
            {
                if (g == null || !g.Active) continue;
                live++;
                g.Tick(dt);
            }
            ActiveGeese = live;
        }

        void HandleRetired(RallyGoose g)
        {
            // The count is recomputed in TickFlock; this only exists so a retirement is not silent
            // while the mode is being tuned.
            if (verbose && g != null)
                Debug.Log($"[Rally] goose retired from {g.Phase} after {g.Parried} parries.");
        }

        // ------------------------------------------------------------------ strikes

        /// <summary>
        /// The one place a parry can happen.
        ///
        /// Every strikeable goose is tested against every competitor and the BEST tier wins, once.
        /// Running the test per competitor instead is what produces double parries: two machines
        /// nose to nose both connect on the same frame, the bird is launched twice, and the second
        /// launch overwrites the first — so a parry the player definitely made silently does nothing.
        /// One goose, one resolution, and the immunity window afterwards closes the rest of it.
        /// </summary>
        void TickStrikes()
        {
            if (State != Phase.Live) return;

            foreach (var g in _flock)
            {
                if (!g.Active || !g.Strikeable) continue;

                RallyCompetitor best = null;
                var bestTier = RallyStrike.Tier.Miss;
                Vector3 bestContact = g.transform.position;

                foreach (var c in competitors)
                {
                    if (c == null || c.mower == null) continue;
                    var tier = c.Evaluate(g.PreviousPosition, g.transform.position, out Vector3 contact);
                    if (tier <= bestTier) continue;
                    best = c; bestTier = tier; bestContact = contact;
                }

                if (best == null) continue;
                Resolve(g, best, bestTier, bestContact);
            }

            TickAnticipation();
        }

        void Resolve(RallyGoose g, RallyCompetitor by, RallyStrike.Tier tier, Vector3 contact)
        {
            int newTarget = by.ChooseTarget(g.Velocity, contact, out Vector3 launchDir);
            bool knockout = g.Parried >= 1;      // this strike will be its second

            g.Struck(by, tier, contact, newTarget, launchDir);
            by.RegisterStrike(tier, contact, knockout);

            bool byPlayer = by.isPlayer;
            float distanceToPlayer = Player != null ? Vector3.Distance(contact, Player.Position) : 99f;

            if (knockout)
            {
                var victim = CompetitorAt(g.LastStriker);
                by.RegisterLanded();
                OnKnockout?.Invoke(by, victim);
                Announce($"GOOSE DOWN — {by.Name}!", by.Livery, contact, 1.6f);
                AudioDirector.Instance?.CrowdCheer(byPlayer ? 1f : 0.7f, applaud: true);
                Impact(hitStopKnockout, byPlayer ? 1.1f : 0.45f, byPlayer, distanceToPlayer);
            }
            else
            {
                OnStrike?.Invoke(by, tier, newTarget);
                if (byPlayer)
                    Announce(RallyStrike.Label(tier), TierColour(tier), contact,
                             tier == RallyStrike.Tier.Perfect ? 1.0f : 0.6f);
                float stop = tier switch
                {
                    RallyStrike.Tier.Perfect => hitStopPerfect,
                    RallyStrike.Tier.Good => hitStopGood,
                    _ => hitStopNormal
                };
                float punch = tier switch
                {
                    RallyStrike.Tier.Perfect => punchPerfect,
                    RallyStrike.Tier.Good => punchGood,
                    _ => punchNormal
                };
                Impact(stop, punch, byPlayer, distanceToPlayer);
                if (tier == RallyStrike.Tier.Perfect && byPlayer)
                    AudioDirector.Instance?.CrowdCheer(0.75f);
            }

            if (verbose)
                Debug.Log($"[Rally] {by.Name} {(knockout ? "KNOCKED OUT" : tier.ToString())} a goose " +
                          $"(parry {g.Parried}) → {(knockout ? "—" : RallyArena.Get(newTarget).contestant)}");
        }

        static Color TierColour(RallyStrike.Tier t) => t switch
        {
            RallyStrike.Tier.Perfect => new Color(1f, 0.86f, 0.28f),
            RallyStrike.Tier.Good => new Color(0.62f, 0.92f, 0.5f),
            _ => Color.white
        };

        /// <summary>
        /// The dip before a perfect strike.
        ///
        /// Predictive, and only ever for the player: it looks a fraction of a second ahead and, if
        /// the bird is about to arrive in the sweet spot at speed, eases the world off. So good
        /// timing is rewarded BEFORE the hit rather than only after it, which is the difference
        /// between the game noticing what you did and the game reacting to it.
        ///
        /// Never for the bots. Three opponents each triggering a global slowdown across the arena
        /// would leave the match permanently in treacle.
        /// </summary>
        void TickAnticipation()
        {
            if (Player == null || _freeze > 0f) return;

            foreach (var g in _flock)
            {
                if (!g.Active || !g.Strikeable) continue;
                Vector3 rel = g.transform.position - Player.SweetSpot;
                Vector3 relVel = g.Velocity;
                float closing = -Vector3.Dot(rel.normalized, relVel.normalized) * relVel.magnitude;
                if (closing < 4f) continue;

                float eta = rel.magnitude / Mathf.Max(closing, 0.01f);
                if (eta > 0.13f) continue;
                if (rel.magnitude > Player.perfectRadius * 2.1f) continue;

                _anticipation = Mathf.Max(_anticipation, 0.1f);
                return;
            }
        }

        // ------------------------------------------------------------------ breaches

        /// <summary>A goose got past somebody. Called by the goose as it crosses the fence line.</summary>
        public void ReportBreach(RallyGoose g, int slot, Vector3 from, Vector3 impact)
        {
            var defender = CompetitorAt(slot);
            var garden = GardenAt(slot);
            int lost = garden != null ? garden.Breach(from, impact) : 0;

            defender?.RegisterConceded();
            var striker = CompetitorAt(g.LastStriker);
            if (striker != null && striker != defender) striker.RegisterLanded();

            OnBreach?.Invoke(defender, lost);
            RallyFX.Instance?.Crash(impact, g.Velocity, defender != null ? defender.Livery : Color.white);

            bool mine = defender != null && defender.isPlayer;
            Announce(mine ? "YOUR GARDEN!" : $"{RallyArena.Get(slot).contestant} HIT!",
                     mine ? new Color(1f, 0.36f, 0.3f) : new Color(0.85f, 0.85f, 0.9f),
                     impact, 1.2f);

            if (mine)
            {
                Impact(0.07f, 0.8f, true, 0f);
                cameraDirector?.AddShake(0.9f);
            }
            AudioDirector.Instance?.CrowdCheer(mine ? 0.25f : 0.55f);

            if (verbose)
                Debug.Log($"[Rally] breach at {RallyArena.Get(slot).contestant}: {lost} beds, " +
                          $"integrity now {(garden != null ? garden.Integrity : 1f):0.00}.");
        }

        // ------------------------------------------------------------------ time warp

        /// <summary>
        /// Freeze, then slow, then release. One entry point so nothing else in the mode is allowed to
        /// touch Time.timeScale — a global that two systems both write is a global that gets left in
        /// the wrong state after a scene change.
        /// </summary>
        void Impact(float freezeSeconds, float punch, bool forPlayer, float distanceToPlayer)
        {
            // A strike on the far side of the arena is somebody else's moment. It gets its noise and
            // its feathers; it does not get to stop the player's world.
            float reach = Mathf.Clamp01(1f - distanceToPlayer / 34f);
            float weight = forPlayer ? 1f : reach * 0.45f;
            if (weight < 0.05f) return;

            _freeze = Mathf.Max(_freeze, freezeSeconds * weight);
            _slow = Mathf.Max(_slow, slowMotionSeconds * weight);
            if (forPlayer) cameraDirector?.AddPunch(punch);
            cameraDirector?.AddShake(punch * weight * 0.8f);
        }

        void TickWarp(float unscaled)
        {
            if (_freeze > 0f)
            {
                _freeze -= unscaled;
                SetScale(0.02f);
                return;
            }
            if (_slow > 0f)
            {
                _slow -= unscaled;
                float k = Mathf.Clamp01(_slow / Mathf.Max(slowMotionSeconds, 1e-3f));
                SetScale(Mathf.Lerp(1f, slowMotionScale, k));
                return;
            }
            if (_anticipation > 0f)
            {
                _anticipation -= unscaled;
                SetScale(anticipationScale);
                return;
            }
            ReleaseWarp();
        }

        void SetScale(float s)
        {
            _warpHeld = true;
            Time.timeScale = s;
            Time.fixedDeltaTime = 0.02f * s;
        }

        void ReleaseWarp()
        {
            _freeze = _slow = _anticipation = 0f;
            if (!_warpHeld) return;
            _warpHeld = false;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
        }

        // ------------------------------------------------------------------ camera

        /// <summary>
        /// The horn: everybody's, not just the player's.
        ///
        /// It drives geese back inside the arc it is pointed down. A verb only the player has is a
        /// verb the player cannot be beaten with, and three rivals who never use the one button in
        /// the mode read as three rivals who are not really playing — the whole rule of this arena
        /// is that everyone runs on identical rules.
        ///
        /// The bots' judgement is deliberately imperfect. They will only sound it at something
        /// already coming for THEM, they need it roughly in front of them, and they hesitate first —
        /// the better the contestant, the shorter the hesitation and the wider the angle they will
        /// accept. A bot that horned every bird the instant it entered range would be using the
        /// button better than any human can.
        /// </summary>
        void TickHorn()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _hornCd.Length; i++)
                if (_hornCd[i] > 0f) _hornCd[i] -= dt;

            var input = InputReader.Instance;
            if (input != null && input.HornPressed && Player != null)
                SoundHorn(Player);

            for (int i = 0; i < RallyArena.Count; i++)
            {
                var c = CompetitorAt(i);
                if (c == null || c.IsPlayer) continue;
                if (_hornCd[i] > 0f) { _hornThink[i] = 0f; continue; }

                var threat = NearestThreatTo(i);
                if (threat == null || !threat.Threatening) { _hornThink[i] = 0f; continue; }

                Vector3 to = threat.transform.position - c.Position; to.y = 0f;
                float d = to.magnitude;
                if (d > hornReach * 0.9f || d < 1f) { _hornThink[i] = 0f; continue; }

                Vector3 face = c.Heading; face.y = 0f;
                if (face.sqrMagnitude < 1e-4f) continue;
                face.Normalize();

                float skill = Mathf.Clamp01(RallyArena.Get(i).skill);
                float half = Mathf.Lerp(hornHalfAngle * 0.5f, hornHalfAngle, skill);
                if (Vector3.Dot(face, to / d) < Mathf.Cos(half * Mathf.Deg2Rad)) { _hornThink[i] = 0f; continue; }

                _hornThink[i] += dt;
                if (_hornThink[i] < Mathf.Lerp(0.85f, 0.28f, skill)) continue;
                _hornThink[i] = 0f;
                SoundHorn(c);
            }
        }

        /// <summary>
        /// One horn, from one machine, down the way that machine is pointed.
        ///
        /// The direction is the SOUNDER's heading — read from the competitor passed in, not from the
        /// player — which is the whole of "check the direction". Both the arc that is drawn and the
        /// arc that is tested come from this one vector, so a rival's wave always points where their
        /// horn actually worked.
        /// </summary>
        public bool SoundHorn(RallyCompetitor c)
        {
            if (c == null || _hornCd[c.slot] > 0f) return false;
            _hornCd[c.slot] = hornCooldown;

            Vector3 from = c.Position;
            Vector3 dir = c.Heading; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();

            RallyWorldFX.Instance?.HornWave(from, dir, c.Livery);
            AudioDirector.Instance?.CrowdCheer(c.IsPlayer ? 0.18f : 0.1f);

            float cos = Mathf.Cos(hornHalfAngle * Mathf.Deg2Rad);
            int scared = 0;
            foreach (var g in _flock)
            {
                if (g == null || !g.Active) continue;
                Vector3 to = g.transform.position - from; to.y = 0f;
                float d = to.magnitude;
                if (d > hornReach || d < 0.2f) continue;
                if (Vector3.Dot(dir, to / d) < cos) continue;
                g.Startle(to / d, 1f - d / hornReach);
                scared++;
            }
            if (scared > 0 && c.IsPlayer) AudioDirector.Instance?.CrowdCheer(0.3f);
            return true;
        }

        /// <summary>Seconds left on a competitor's horn, and how full the meter is. Read by the HUD.</summary>
        public float HornCooldownLeft(int slot)
            => _hornCd[((slot % RallyArena.Count) + RallyArena.Count) % RallyArena.Count];

        public float HornCharge(int slot)
            => hornCooldown <= 0f ? 1f : 1f - Mathf.Clamp01(HornCooldownLeft(slot) / hornCooldown);

        readonly float[] _hornCd = new float[RallyArena.Count];
        readonly float[] _hornThink = new float[RallyArena.Count];

        bool _droppedIn;

        /// <summary>
        /// What the camera is told, and the whole of it.
        ///
        /// Two numbers and at most one transform. The energy is how busy the board is and only ever
        /// lengthens the boom and widens the lens; the subject is a bounded aim BIAS and is offered
        /// only for a bird actually coming at the player. The rig owns the pose — see
        /// <see cref="CameraDirector"/> — and nothing here is allowed to write a rotation.
        /// </summary>
        void TickCamera()
        {
            if (cameraDirector == null) return;

            // Held wide, not breathing.
            //
            // This used to ride the goose count and the match clock, so the boom and the lens crept
            // out as birds arrived and crept back in as they died — a zoom in and out, over and over,
            // for the whole match. Reading the description it sounds responsive; playing it, the
            // camera is never still and the player is never given a stable frame to judge distance
            // in, which is the one thing a chase camera in a wide arena has to provide.
            //
            // So: full width the moment the match is live, and it stays there. The shot answers
            // "where is everything" once and then stops having opinions. Impacts still punch the
            // lens — that is an event, not a state, and it is over in a tenth of a second.
            float energy = State == Phase.Live || State == Phase.Settle ? 1f : 0f;
            cameraDirector.SetRallyEnergy(energy);

            var threat = NearestThreatTo(Player != null ? Player.slot : 0);
            // Only while it is actually inbound. A goose milling about on the far side of the arena
            // has no claim on the player's frame, and giving it one is how the camera ends up
            // staring at a hedge while the player is being scored on from the other direction.
            bool inbound = threat != null && threat.Threatening &&
                           Player != null &&
                           Vector3.Distance(threat.transform.position, Player.Position) < 30f;

            // The bias subject only. Energy is set above and deliberately NOT passed through here —
            // SetDefenceSubject writes it too, and letting it do so would put the boom back on the
            // goose count and undo the fixed framing this method just established.
            cameraDirector.SetDefenceSubject(inbound ? threat.transform : null, 1f,
                                             inbound ? 1f : 0f);
        }

        /// <summary>
        /// The goose most worth worrying about, from one competitor's point of view. Used by the
        /// bots to decide where to stand and by the camera to decide what to lean toward.
        /// </summary>
        public RallyGoose NearestThreatTo(int slot)
        {
            var s = RallyArena.Get(slot);
            RallyGoose best = null;
            float bestScore = float.MaxValue;

            foreach (var g in _flock)
            {
                if (!g.Active) continue;
                if (g.TargetSlot != slot) continue;
                if (g.Phase == RallyGoose.State.Leaving || g.Phase == RallyGoose.State.Down) continue;

                float d = Vector3.Distance(g.transform.position, s.gardenCentre);
                // Airborne birds arrive sooner than their distance suggests; weight them up so a
                // defender does not stand watching a walker while one comes over the top.
                if (g.Phase == RallyGoose.State.Struck) d *= 0.6f;
                if (d >= bestScore) continue;
                bestScore = d; best = g;
            }
            return best;
        }

        /// <summary>Every live goose currently coming at a given competitor. For the HUD's arrows.</summary>
        public void ThreatsTo(int slot, List<RallyGoose> into)
        {
            into.Clear();
            foreach (var g in _flock)
                if (g.Active && g.TargetSlot == slot && g.Threatening) into.Add(g);
        }

        // ------------------------------------------------------------------ finishing

        /// <summary>
        /// Punctuate a moment — in the WORLD where it happened, and only secondarily as words.
        ///
        /// <paramref name="at"/> is where the thing occurred. When it is given, a star bursts there,
        /// which is the version the player actually reads: it has a position, so it says which corner
        /// of the arena is celebrating, and it does not cover the pitch to do it. The text is kept
        /// for the HUD's small ticker and for the capture tools, not for a banner across the middle
        /// of the screen — that banner sat in front of the geese at precisely the moments there was
        /// something worth watching.
        /// </summary>
        void Announce(string text, Color colour, Vector3? at = null, float weight = 1f)
        {
            Callout = text;
            CalloutColour = colour;
            CalloutAge = 0f;

            if (at.HasValue)
                RallyWorldFX.Instance?.Burst(at.Value + Vector3.up * 1.4f, colour, weight,
                                             Mathf.RoundToInt(6f * weight));
        }

        void Finish()
        {
            ReleaseWarp();
            cameraDirector?.ClearDefenceSubject();
            cameraDirector?.SetRallyEnergy(0f);
            State = Phase.Done;

            var results = new RallyHandoff.Result[RallyArena.Count];
            for (int i = 0; i < RallyArena.Count; i++)
            {
                var c = CompetitorAt(i);
                var slotInfo = RallyArena.Get(i);
                results[i] = new RallyHandoff.Result
                {
                    slot = i,
                    contestant = slotInfo.contestant,
                    isPlayer = slotInfo.isPlayer,
                    integrity = c != null ? c.Integrity : 1f,
                    bedsLost = c != null && c.garden != null ? c.garden.BedsLost : 0,
                    bedsTotal = c != null && c.garden != null ? c.garden.BedsTotal : 0,
                    parries = c != null ? c.Parries : 0,
                    perfects = c != null ? c.Perfects : 0,
                    knockouts = c != null ? c.Knockouts : 0,
                    landed = c != null ? c.Landed : 0,
                    conceded = c != null ? c.Conceded : 0,
                };
            }
            RallyHandoff.Post(results);

            if (!verbose) return;
            foreach (var r in results)
                Debug.Log($"[Rally] {r.contestant}: integrity {r.integrity:0.00} " +
                          $"({r.bedsLost}/{r.bedsTotal} lost), {r.parries} parries, " +
                          $"{r.knockouts} KOs, {r.landed} landed.");
        }
    }
}
