using System.Collections.Generic;
using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Runs a Bloom Rush match: the clock, the crown, the callouts, and the reveal.
    ///
    /// It owns almost no state. Ownership lives in <see cref="TurfMask"/>, driving lives in
    /// <see cref="MowerController"/>, and painting lives in <see cref="TurfCompetitor"/> — what is
    /// left here is the shape of the ninety seconds: when the roller comes down, what the arena is
    /// told about how close the finish is, who is holding the middle, and what happens the instant
    /// the clock hits zero.
    ///
    /// The one rule this is really enforcing is that the ending is honest. On the horn the mask is
    /// FROZEN before anything else happens — before the camera moves, before a percentage is read,
    /// before a mower has coasted to a stop. A machine still rolling during the lift would otherwise
    /// keep painting through the reveal and the number on the board would not be the number the
    /// player watched being earned.
    ///
    /// And it does NOT declare a winner. The overhead reveal measures the ground, shows all four
    /// percentages and says who covered the most — but "covered the most" is a measurement, not a
    /// verdict. The numbers go to <see cref="TurfHandoff"/>, the round folds them into its score,
    /// and the three judges mark them alongside everything else the duck did. A stage that ruled on
    /// itself would be deciding the championship on its own authority, and nothing in this game is
    /// allowed to do that.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class TurfDirector : MonoBehaviour
    {
        public enum Phase { Idle, Intro, Live, Lock, Reveal, Done }

        [Header("Cast")]
        public TurfCompetitor[] competitors = new TurfCompetitor[0];

        [Header("Clock")]
        [Tooltip("Seconds of countdown before the roller comes down. Nobody moves during it, and it " +
                 "does not begin until the camera has finished coming down.")]
        public float introSeconds = 3.2f;
        [Tooltip("Seconds held on the opening overhead, before the camera starts coming down.")]
        public float establishSeconds = 1.6f;
        [Tooltip("Seconds the camera takes to fly from the opening overhead down into the chase.")]
        public float openingSwoop = 1.4f;
        [Tooltip("Length of the match.")]
        public float matchSeconds = 100f;
        [Tooltip("Beat held on the frozen arena before the camera starts to lift.")]
        public float lockSeconds = 1.1f;
        [Tooltip("The whole ending: overhead lift, the bench reading it back, and the board.\n\n" +
                 "Long, because it is three beats and not one. The overhead counts the percentages " +
                 "up, three judges raise a card each in turn, and only then does the camera sweep " +
                 "to the board. Cut this below about twelve and the board is reached before the " +
                 "bench has finished, which shows the same result twice and reads as a mistake.")]
        public float revealSeconds = 19f;
        [Tooltip("Seconds left when the arena starts to wind up.")]
        public float urgencyWindow = 15f;

        [Header("The crown")]
        [Tooltip("Share of the hub needed to take the crown. Above a quarter, so it is genuinely held " +
                 "rather than merely led on a hub nobody has touched.")]
        [Range(0.1f, 0.9f)] public float crownThreshold = 0.3f;
        [Tooltip("Seconds between crown reviews. Slow enough that two mowers crossing in the middle " +
                 "do not hand it back and forth once a frame.")]
        public float crownInterval = 0.35f;
        [Tooltip("Extra share the challenger needs to take it off the current holder, so a dead heat " +
                 "in the hub does not strobe the HUD.")]
        [Range(0f, 0.2f)] public float crownHysteresis = 0.045f;

        [Header("Feel")]
        [Tooltip("Square metres of a single steal that counts as a big one, and earns the world-space " +
                 "reaction rather than just the trail effect.")]
        public float bigStealMetres = 22f;
        [Tooltip("Camera punch on a big steal, 0..1.")]
        [Range(0f, 1f)] public float stealPunch = 0.5f;
        [Tooltip("Camera punch when two machines shunt, scaled by the strength of the hit.")]
        [Range(0f, 1f)] public float shuntPunch = 0.7f;
        [Tooltip("Metres inside which a rival counts as crowding the player, which opens the lens.")]
        public float crowdRadius = 13f;

        [Header("Wiring")]
        public CameraDirector cameraDirector;
        public TurfFX fx;
        [Tooltip("The venue's own results board, standing at the far end of the arena. Null is fine " +
                 "— the reveal then finishes on the overhead and the HUD standings, as it did.")]
        public Scoreboard scoreboard;
        [Tooltip("The bench. Null is fine — the ending then skips straight from the overhead to the " +
                 "board, which is what it did before the judges were brought into this stage.")]
        public TurfVerdict verdict;
        [Tooltip("Earliest the camera may leave the overhead for the player's machine. It also waits " +
                 "for the bench to finish, so this is a floor rather than a cue.")]
        public float boardAt = 4.2f;
        [Tooltip("Seconds the result card is held on the player's machine before the board.\n\n" +
                 "Long enough to read four rows and find your own, which is the one thing the player " +
                 "actually came to this screen for. Short enough that it is a beat and not a menu.")]
        public float cardSeconds = 5.5f;

        [Header("Debug")]
        [Tooltip("Drive the player's mower with a bot too, so a whole match can be captured without " +
                 "hands on the keyboard. Never on in a build.")]
        public bool autopilotPlayer;
        [Tooltip("Seconds between board dumps to the console. 0 is off.")]
        public float traceInterval = 0f;
        public int seed = 8615;

        /// <summary>Set by the capture tools so a shot list can run a match unattended.</summary>
        public static bool DebugAutopilotPlayer;

        // ------------------------------------------------------------------ state

        public Phase State { get; private set; } = Phase.Idle;
        public float TimeRemaining { get; private set; }
        public float Elapsed => Mathf.Max(0f, matchSeconds - TimeRemaining);
        public float MatchFraction => matchSeconds > 0f ? Mathf.Clamp01(Elapsed / matchSeconds) : 0f;
        public bool Finished => State == Phase.Done;

        /// <summary>0 until the closing seconds, then ramps to 1. Everything that intensifies reads this.</summary>
        public float Urgency { get; private set; }

        /// <summary>Who holds the hub, or -1.</summary>
        public int Crown { get; private set; } = -1;
        /// <summary>Seconds since the crown last changed hands.</summary>
        public float CrownAge { get; private set; } = 99f;

        public TurfCompetitor Player { get; private set; }

        /// <summary>How far the reveal has run, 0..1. The HUD counts its percentages up on this.</summary>
        public float RevealProgress { get; private set; }
        /// <summary>True once the camera has left the overhead for the results board.</summary>
        public bool OnBoard { get; private set; }
        /// <summary>True once the camera has come down onto the player's machine with the card up.</summary>
        public bool OnCard { get; private set; }
        float _cardAt;
        /// <summary>Final standings, best first. Empty until the reveal begins.</summary>
        public IReadOnlyList<int> Standings => _standings;
        public int Winner => _standings.Count > 0 ? _standings[0] : -1;
        /// <summary>True when the top two finished within a cell of each other.</summary>
        public bool DeadHeat { get; private set; }

        public string Callout { get; private set; } = "";
        public float CalloutAge { get; private set; } = 99f;
        public Color CalloutColour { get; private set; } = Color.white;

        /// <summary>(thief, victim, square metres, where). Raised only for steals worth reacting to.</summary>
        public event System.Action<TurfCompetitor, TurfCompetitor, float, Vector3> OnBigSteal;
        /// <summary>(new holder, previous holder). Either may be -1.</summary>
        public event System.Action<int, int> OnCrownChange;

        static TurfDirector _live;

        readonly List<int> _standings = new(4);
        /// <summary>One slot per second of the closing countdown, so each is called exactly once.</summary>
        readonly int[] _countdownSpoken = new int[5];
        float _phaseTimer;
        float _crownTimer;
        float _traceTimer;
        int _lastLeader = -1;
        int _spokenCountdown = -1;
        bool _swooped;

        // ------------------------------------------------------------------ lifecycle

        void OnEnable() => _live = this;
        void OnDisable() { if (_live == this) _live = null; }

        public TurfCompetitor CompetitorAt(int slot)
        {
            foreach (var c in competitors) if (c != null && c.slot == slot) return c;
            return null;
        }

        /// <summary>
        /// Ground taken off a competitor, routed from whoever took it.
        ///
        /// Static because the claim happens deep inside a fixed step in <see cref="TurfCompetitor"/>
        /// and threading a director reference down to it would put a null check on the hottest path
        /// in the mode for the sake of a call that happens a few times a second.
        /// </summary>
        public static void NotifyLoss(int slot, float squareMetres)
            => _live?.CompetitorAt(slot)?.RegisterLoss(squareMetres);

        public void Begin()
        {
            if (competitors == null || competitors.Length == 0)
                competitors = FindObjectsByType<TurfCompetitor>(FindObjectsSortMode.None);

            System.Array.Sort(competitors, (a, b) => a.slot.CompareTo(b.slot));

            Player = null;
            foreach (var c in competitors)
            {
                if (c == null) continue;
                if (c.isPlayer) Player = c;

                c.ResetForMatch();
                c.OnSteal -= HandleSteal;
                c.OnSteal += HandleSteal;
                c.OnShunt -= HandleShunt;
                c.OnShunt += HandleShunt;

                var claimant = c;
                c.OnClaim = (metres, where) => fx?.Claim(claimant, metres, where);

                if (c.brain != null) c.brain.Bind(seed + c.slot * 7919);
            }

            BindAutopilot();

            TurfMask.Instance?.ClearAll();
            TurfMask.Instance?.PushConstants();

            _standings.Clear();
            OnBoard = false;
            OnCard = false;
            _swooped = false;
            // Cleared, or a retry runs its whole closing five seconds in silence because the first
            // match already spoke them. The kind of fault that only ever shows up on the second go.
            System.Array.Clear(_countdownSpoken, 0, _countdownSpoken.Length);
            DeadHeat = false;
            RevealProgress = 0f;
            Crown = -1;
            CrownAge = 99f;
            _lastLeader = -1;
            _spokenCountdown = -1;
            Urgency = 0f;

            TimeRemaining = matchSeconds;
            State = Phase.Intro;
            _phaseTimer = 0f;

            // Nobody moves until the horn. Both halves, because they are two different mechanisms:
            // the bots hand the wheel back through TurfBrain.Released, and the player is held by
            // the input gate. Setting one and not the other is three seconds of the opponents
            // taking the good ground while the player watches.
            foreach (var c in competitors) if (c?.brain != null) c.brain.Released = false;
            if (InputReader.Instance != null) InputReader.Instance.DrivingEnabled = false;

            // Open on the whole arena, looking straight down, and hold there through the count-in.
            //
            // The player has to be shown the board before they are asked to play it — where the
            // eight gateways are, where the middle is, and where the other three are sitting. Then
            // the rig flies down into the chase on the horn, which is one continuous move rather
            // than a cut, so the shot that establishes the arena and the shot the match is played
            // in are the same camera doing one thing. Snapped rather than blended INTO, because
            // there is nothing before it to blend from.
            cameraDirector?.SetMode(CameraMode.Reveal, 0f);
            cameraDirector?.SnapToCurrent();

            Announce("BLOOM RUSH", Color.white);
        }

        /// <summary>
        /// Put a bot on the player's machine as well, for unattended capture.
        ///
        /// It is the identical <see cref="TurfBrain"/> the opponents use, wired through the identical
        /// <see cref="IMowerInput"/>. A bespoke capture autopilot would be a fourth driving model
        /// nobody plays against, and the frames it produced would be of a game that does not exist.
        /// </summary>
        void BindAutopilot()
        {
            bool want = autopilotPlayer || DebugAutopilotPlayer;
            if (Player == null || Player.mower == null) return;

            if (!want)
            {
                if (Player.brain != null) { Player.brain.enabled = false; Player.mower.inputSource = null; }
                return;
            }

            if (Player.brain == null)
            {
                Player.brain = Player.gameObject.AddComponent<TurfBrain>();
                Player.brain.competitor = Player;
                Player.brain.plan = TurfBrain.Plan.Raider;
                Player.brain.skill = 0.72f;
            }
            Player.brain.enabled = true;
            Player.mower.inputSource = Player.brain;
            Player.brain.Bind(seed + 991);
            Debug.Log("[Bloom] autopilot is driving the player's mower.");
        }

        public void Abort()
        {
            TurfMask.Instance?.Freeze();
            foreach (var c in competitors) c?.StopPainting();
            State = Phase.Done;
        }

        void Update()
        {
            if (State == Phase.Idle || State == Phase.Done) return;

            float dt = Time.deltaTime;
            _phaseTimer += dt;
            CalloutAge += Time.unscaledDeltaTime;
            CrownAge += dt;

            switch (State)
            {
                case Phase.Intro: TickIntro(); break;
                case Phase.Live: TickLive(dt); break;

                case Phase.Lock:
                    if (_phaseTimer >= lockSeconds) BeginReveal();
                    break;

                case Phase.Reveal:
                    // The board is TICKED, and it has to be from here.
                    //
                    // Scoreboard has no Update of its own — it exposes Tick and is driven by
                    // whoever is staging it, GameDirector in the venue and RallyVerdict in stage
                    // two. Nothing in this scene was doing it, so Settle set the rows' target state
                    // and then nobody ever animated them towards it: the camera swept ninety metres
                    // onto a board that was correctly filled in and completely blank.
                    //
                    // Unscaled, like the rally's, so the board still counts up through a hit stop.
                    scoreboard?.Tick(Time.unscaledDeltaTime);

                    RevealProgress = Mathf.Clamp01(_phaseTimer / Mathf.Max(revealSeconds * 0.55f, 0.1f));
                    // The overhead has shown WHAT happened; the board says what it was worth. Two
                    // shots rather than one, and the sweep between them is most of the payoff —
                    // ninety metres of travel to a board the player has not been looking at, which
                    // is the same beat stage two ends on and for the same reason.
                    // THREE SHOTS, IN THIS ORDER, AND THE ORDER IS THE WHOLE POINT.
                    //
                    //     BENCH   the judges settle it. Nothing is decided before this.
                    //     CARD    down onto the player's own machine, standings on the left.
                    //     BOARD   the sweep to the scoreboard, where the round is added up.
                    //
                    // Each waits for the one before it to finish rather than for a timestamp. The
                    // card cannot arrive while a judge still has a card going up, or the screen is
                    // announcing a result the bench has not given yet; and the board cannot arrive
                    // while the player is still reading their own placing off the card.
                    bool benchDone = verdict == null || verdict.Finished;
                    if (!OnCard && benchDone && _phaseTimer >= boardAt) ShowCard();
                    if (OnCard && !OnBoard && _phaseTimer >= _cardAt + cardSeconds) ShowBoard();
                    if (_phaseTimer >= revealSeconds) State = Phase.Done;
                    break;
            }

            TickCrown(dt);
            TickCamera();
            TickTrace(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// The opening, in three beats and strictly in this order.
        ///
        ///     ESTABLISH   overhead on the whole arena, held
        ///     SWOOP       one continuous move down into the chase behind the player's machine
        ///     COUNT       3, 2, 1 — and only once the camera has arrived
        ///
        /// The count starting before the camera has landed was the first version and it is wrong in
        /// a way that only shows up with hands on the keyboard: the player spends "3, 2" watching
        /// the world rush past and arrives at "1" with no idea which way their machine is pointing.
        /// A countdown is a promise that you are ready, and you are not ready while the camera is
        /// still moving. Establishing the arena and preparing to drive it are two jobs, and doing
        /// them at once does neither.
        /// </summary>
        void TickIntro()
        {
            float swoopAt = establishSeconds;
            float countAt = establishSeconds + openingSwoop;

            // Down out of the establishing shot, once. Fired on the frame it is due rather than
            // driven every frame, because SetMode restarts the blend each time it is called and
            // calling it repeatedly would hold the camera permanently at the start of its descent.
            if (!_swooped && _phaseTimer >= swoopAt)
            {
                _swooped = true;
                cameraDirector?.SetMode(CameraMode.Chase, openingSwoop);
            }

            if (_phaseTimer < countAt) return;

            float left = countAt + introSeconds - _phaseTimer;
            int beat = Mathf.CeilToInt(left);
            if (beat != _spokenCountdown && beat >= 1 && beat <= 3)
            {
                _spokenCountdown = beat;
                Announce(beat.ToString(), Color.white);
            }

            if (_phaseTimer < countAt + introSeconds) return;

            State = Phase.Live;
            _phaseTimer = 0f;
            foreach (var c in competitors)
            {
                c?.BeginPainting();
                if (c?.brain != null) c.brain.Released = true;
            }
            if (InputReader.Instance != null) InputReader.Instance.DrivingEnabled = true;

            // The camera is already home — it came down during the count-in, not on the horn.
            Announce("GROW!", new Color(0.95f, 0.95f, 0.6f));
            AudioDirector.Instance?.PlayHorn();
            AudioDirector.Instance?.CrowdCheer(0.8f);
        }

        void TickLive(float dt)
        {
            TimeRemaining -= dt;

            Urgency = urgencyWindow > 0f
                ? Mathf.Clamp01(1f - TimeRemaining / urgencyWindow)
                : 0f;

            CountdownCalls();
            LeaderCalls();

            TurfMask.Instance?.SetPulse(Urgency, Crown, Mathf.Clamp01(1f - CrownAge * 0.7f));

            if (TimeRemaining > 0f) return;

            // The horn. Order matters and this is the order.
            TimeRemaining = 0f;
            TurfMask.Instance?.Freeze();
            foreach (var c in competitors)
            {
                c?.StopPainting();
                c?.mower?.CutEngine();
            }

            State = Phase.Lock;
            _phaseTimer = 0f;
            Announce("TIME!", Color.white);
            AudioDirector.Instance?.PlayHorn();
            AudioDirector.Instance?.CrowdCheer(1f, applaud: true);
            fx?.Horn();
        }

        void CountdownCalls()
        {
            int beat = Mathf.CeilToInt(TimeRemaining);
            if (beat > 5 || beat < 1) return;
            int idx = beat - 1;
            if (idx >= _countdownSpoken.Length || _countdownSpoken[idx] == 1) return;
            _countdownSpoken[idx] = 1;
            Announce(beat.ToString(), new Color(1f, 0.72f, 0.3f));
        }

        void LeaderCalls()
        {
            var mask = TurfMask.Instance;
            if (mask == null) return;
            int leader = mask.Leader;
            if (leader == _lastLeader) return;

            // Only once the board means something. In the first few seconds the lead changes on a
            // few square metres and calling every swap trains the player to ignore the callouts.
            if (mask.Share(leader) < 0.12f) return;

            _lastLeader = leader;
            var c = CompetitorAt(leader);
            if (c == null) return;
            Announce(c.isPlayer ? "YOU LEAD" : $"{c.Name} LEADS", c.Livery);
            AudioDirector.Instance?.CrowdCheer(c.isPlayer ? 0.7f : 0.35f);
        }

        // ------------------------------------------------------------------ the crown

        /// <summary>
        /// Who holds the middle.
        ///
        /// It is worth holding because it makes your roller half again as wide, which compounds:
        /// the hub is the one place on the map where being ahead makes you take ground faster. That
        /// is deliberately the only snowball in the mode, it is visible, it sits in the most
        /// contested and most crossable ground in the arena, and three opponents can all see who has
        /// it. A lead nobody can attack would be a different and much worse game.
        /// </summary>
        void TickCrown(float dt)
        {
            if (State != Phase.Live) return;
            _crownTimer -= dt;
            if (_crownTimer > 0f) return;
            _crownTimer = crownInterval;

            var mask = TurfMask.Instance;
            if (mask == null) return;

            int best = -1;
            float bestShare = crownThreshold;
            for (int i = 0; i < TurfArena.Count; i++)
            {
                float share = mask.PlazaShare(i);
                float need = bestShare + (i == Crown ? 0f : crownHysteresis);
                if (share <= need) continue;
                bestShare = share; best = i;
            }
            // The holder keeps it until somebody clears them by the hysteresis, so a hub split down
            // the middle does not hand the crown back and forth three times a second.
            if (best == -1 && Crown >= 0 && mask.PlazaShare(Crown) > crownThreshold * 0.72f)
                best = Crown;

            if (best == Crown) return;

            int previous = Crown;
            Crown = best;
            CrownAge = 0f;
            foreach (var c in competitors) if (c != null) c.HasCrown = c.slot == Crown;

            OnCrownChange?.Invoke(Crown, previous);

            if (Crown >= 0)
            {
                var c = CompetitorAt(Crown);
                Announce(c != null && c.isPlayer ? "YOU HOLD THE MIDDLE" : $"{c?.Name} HOLDS THE MIDDLE",
                         c != null ? c.Livery : Color.white);
                AudioDirector.Instance?.CrowdCheer(c != null && c.isPlayer ? 0.85f : 0.45f);
                fx?.CrownTaken(Crown);
            }
            else Announce("THE MIDDLE IS OPEN", Color.white);
        }

        // ------------------------------------------------------------------ reactions

        void HandleSteal(int victimSlot, float squareMetres, Vector3 where)
        {
            // Which of the four did it: the one whose roller was over that ground, found by asking
            // the mask rather than by trusting the caller, so a steal can never be credited to
            // somebody who was not there.
            var mask = TurfMask.Instance;
            int thiefSlot = mask != null ? mask.OwnerAt(where) : -1;
            var thief = thiefSlot >= 0 ? CompetitorAt(thiefSlot) : null;
            var victim = CompetitorAt(victimSlot);

            fx?.Steal(thief, victim, squareMetres, where);

            if (squareMetres < bigStealMetres) return;

            OnBigSteal?.Invoke(thief, victim, squareMetres, where);

            bool mine = thief != null && thief.isPlayer;
            bool onMe = victim != null && victim.isPlayer;
            if (mine) Announce("STOLEN!", thief.Livery);
            else if (onMe) Announce($"{thief?.Name} TOOK YOURS", thief != null ? thief.Livery : Color.white);

            if (mine || onMe) cameraDirector?.AddShake(stealPunch);
            AudioDirector.Instance?.CrowdCheer(mine ? 0.9f : onMe ? 0.4f : 0.3f);
        }

        void HandleShunt(TurfCompetitor other, float strength)
        {
            if (Player == null) return;
            bool involved = other == Player;
            // The player's own machine reports its shunt through its own event, so this fires for
            // the player either way; the camera punch is only for hits the player can feel.
            if (!involved && Vector3.Distance(other.Position, Player.Position) > crowdRadius) return;
            cameraDirector?.AddShake(shuntPunch * strength);
            fx?.Shunt(other.Position, strength);
        }

        // ------------------------------------------------------------------ the ending

        void BeginReveal()
        {
            State = Phase.Reveal;
            _phaseTimer = 0f;

            var mask = TurfMask.Instance;
            _standings.Clear();
            for (int i = 0; i < TurfArena.Count; i++) _standings.Add(i);
            if (mask != null)
                _standings.Sort((a, b) => mask.Owned[b].CompareTo(mask.Owned[a]));

            DeadHeat = mask != null && _standings.Count > 1 &&
                       Mathf.Abs(mask.Owned[_standings[0]] - mask.Owned[_standings[1]]) <= 1;

            // The one camera move in the whole mode that is not the chase rig doing its job. The
            // rig's own Reveal mode, unchanged — the same lift the round uses to show the player
            // what they cut, pointed at an arena instead of a lawn.
            cameraDirector?.SetMode(CameraMode.Reveal, 1.9f);

            // Everybody onto their mark, in front of the bench. The round does the same thing
            // before its closing portrait — GameDirector.StageForPortrait — and for the same
            // reason: a result shot composed around wherever four machines happened to stop is
            // not composed. It also buys the whole ending its pacing, because the bench, the
            // parked machines and the board now sit within a few metres of each other instead of
            // a hundred, so the camera moves between them are beats rather than journeys.
            ParkForVerdict();

            // And then the bench reads it back. The overhead says what happened; three animals at a
            // desk say what it was worth, which is this game's language for a verdict everywhere
            // else and had no business being a line of centred text here.
            verdict?.Begin();

            var winner = CompetitorAt(Winner);
            fx?.Reveal(Winner);
            AudioDirector.Instance?.PlayFanfare(winner != null && winner.isPlayer);
            AudioDirector.Instance?.CrowdCheer(1f, applaud: true);

            TurfHandoff.Post(mask, _standings, Player != null ? Player.slot : 0);

            if (mask != null)
            {
                var line = new System.Text.StringBuilder("[Bloom] final —");
                foreach (int s in _standings)
                    line.Append($" {TurfArena.Get(s).contestant} {mask.Share(s) * 100f:0.0}%");
                line.Append($" · neutral {mask.NeutralShare * 100f:0.0}%");
                Debug.Log(line.ToString());
            }
        }

        /// <summary>
        /// Leave the overhead and settle the results board.
        ///
        /// The board is the venue's own — the same object the championship posts to — so the four
        /// rows the player reads here are laid out exactly like the rows they will read after the
        /// judges. A second board with its own layout would be a second answer to the same
        /// question, and this stage is not entitled to one: it reports, the panel rules.
        ///
        /// The percentage goes in the RANK column rather than the marks column, because the marks
        /// column is out of thirty and belongs to the judges. What this stage knows is how much
        /// ground somebody covered, and that is what it says.
        /// </summary>
        [Header("Parc ferme")]
        [Tooltip("How far outside the core wall the machines are parked for the verdict.")]
        public float parkStandoff = 4.6f;
        [Tooltip("Degrees between parked machines, measured around the core.")]
        public float parkSpread = 15f;

        /// <summary>
        /// Park all four in an arc in front of the bench, facing it.
        ///
        /// Through <see cref="MowerController.ParkAt"/> rather than by setting transforms, because
        /// these bodies are physics-driven and interpolated: a transform write is undone by the next
        /// step and the machine snaps back to where it was. That exact bug cost the round a whole
        /// feature once, and the note on ParkAt is there because of it.
        /// </summary>
        void ParkForVerdict()
        {
            // The bench stands on the core's -Z face looking up +Z — see DuckTurfBuilder, where
            // that heading is forced so the round's own judges shot frames correctly. So the mark
            // is the +Z side of the core, and the machines face back down -Z at the desk.
            const float baseDeg = 0f;      // world +Z
            float radius = TurfArena.CoreRadius + parkStandoff;
            int n = competitors != null ? competitors.Length : 0;

            for (int i = 0; i < n; i++)
            {
                var c = competitors[i];
                if (c == null || c.mower == null) continue;

                // Placed in FINISHING order, winner nearest the bench's centre line, so the picture
                // agrees with the card beside it without anybody having to be told.
                int place = _standings.IndexOf(c.slot);
                if (place < 0) place = i;
                float deg = baseDeg + (place - (n - 1) * 0.5f) * parkSpread;

                Vector3 dir = TurfArena.Bearing(deg);
                Vector3 p = dir * radius;
                p.y = TurfArena.GroundHeight(p) + 0.4f;
                c.mower.ParkAt(p, Quaternion.LookRotation(-dir, Vector3.up));
            }

            // The rig is on the overhead at this point and will not act on it, but the chase and
            // verdict shots both smooth toward their target and would otherwise spend their first
            // second travelling from wherever the player used to be.
            cameraDirector?.NotifyTargetTeleported();
        }

        /// <summary>
        /// Off the overhead and down onto the player's own machine, with the standings beside it.
        ///
        /// CameraMode.Verdict is the round's closing shot and it already does exactly what this
        /// beat needs: it frames the subject to the RIGHT of centre and leaves the left of the
        /// frame deliberately empty, which is where the round puts its result card and where this
        /// stage now puts its own. Reusing the shot rather than authoring a fourth one is what
        /// makes the two stages read as the same game rather than as two games sharing a duck.
        /// </summary>
        void ShowCard()
        {
            OnCard = true;
            _cardAt = _phaseTimer;
            cameraDirector?.SetMode(CameraMode.Verdict, 1.6f);
        }

        void ShowBoard()
        {
            OnBoard = true;
            var mask = TurfMask.Instance;
            if (scoreboard == null || mask == null) return;

            // WHAT THE BOARD IS FOR, and it is not this stage.
            //
            // It used to print each gardener's share of the arena, as a percentage and as a mark
            // out of thirty — which is the same four numbers the card the player has just finished
            // reading already gave them, on a board they had to be flown ninety metres to see. A
            // board that repeats the previous shot is a board with nothing to say.
            //
            // What it says instead is the CHAMPIONSHIP: rounds one, two and three added up, with
            // this stage's ground folded in. That is the only number in the game the player cannot
            // work out from what is already on screen, it is the number the ending turns on — the
            // three-round total is what picks the winning or losing cutscene — and it is the number
            // this board prints after every other round, which is what makes it this board.
            var table = Tournament.Instance != null ? Tournament.Instance.Championship?.Table : null;

            var rows = new List<Standing>(_standings.Count);
            foreach (int slot in _standings)
            {
                var s = TurfArena.Get(slot);
                // This stage's contribution, on the competition's own scale rather than as a share.
                float stageMarks = mask.Share(slot) * 30f;
                float running = stageMarks;

                if (table != null)
                {
                    foreach (var e in table)
                        if (string.Equals(e.name, s.contestant, System.StringComparison.OrdinalIgnoreCase))
                        { running += e.points; break; }
                }

                rows.Add(new Standing
                {
                    name = s.contestant,
                    species = s.species,
                    isPlayer = s.isPlayer,
                    livery = s.livery,
                    total = running,
                    // rallyMarked makes the row print a bare number instead of "N / 30", which is
                    // what a cumulative total needs — three rounds do not fit in one round's
                    // denominator, and a board reading "71 / 30" is a board nobody trusts again.
                    rallyMarked = table != null,
                    // No grade and no percentage. The column that used to carry the share is left
                    // empty on purpose: the share is the card's job and it has just done it.
                    rank = "",
                });
            }

            if (rows.Count == 0) return;

            cameraDirector?.SetMode(CameraMode.Scoreboard, 1.6f);
            scoreboard.ResetBoard();
            scoreboard.Settle(rows, table != null ? "CHAMPIONSHIP" : "MOST GROUND");
            AudioDirector.Instance?.CrowdCheer(0.8f, applaud: true);
        }

        // ------------------------------------------------------------------ camera

        /// <summary>
        /// What the camera is told, and the whole of it.
        ///
        /// One number, and it only ever lengthens the boom and widens the lens. The rig keeps
        /// following the player's heading and velocity exactly as it does on the lawn — nothing here
        /// aims it, nothing points it at the middle of the arena, and nothing hands it an opponent
        /// to track. A territory mode is very tempting to film from above and doing so would take
        /// away the one thing that makes driving it feel like driving.
        /// </summary>
        void TickCamera()
        {
            if (cameraDirector == null) return;
            if (State != Phase.Live) { cameraDirector.SetRallyEnergy(0f); return; }
            if (Player == null) return;

            // How crowded it is around the player, which is the only thing that should open the
            // lens: three machines converging on a choke need to be in frame before they arrive.
            int near = 0;
            float closest = float.PositiveInfinity;
            foreach (var c in competitors)
            {
                if (c == null || c == Player) continue;
                float d = Vector3.Distance(c.Position, Player.Position);
                if (d < crowdRadius) near++;
                closest = Mathf.Min(closest, d);
            }

            float crowding = Mathf.Clamp01(near / 2f) * 0.7f
                           + Mathf.Clamp01(1f - closest / crowdRadius) * 0.3f;
            cameraDirector.SetRallyEnergy(Mathf.Max(crowding, Urgency * 0.35f));
        }

        // ------------------------------------------------------------------ review tools

        /// <summary>
        /// End the match now, so the closing sequence can be watched on its own.
        ///
        /// The evaluation is five separate beats — freeze, hold, lift, count the percentages up,
        /// sweep to the board — and every one of them is a timing decision that has to be looked at
        /// dozens of times. Reaching it honestly costs seventy-five seconds of driving, so reviewing
        /// it honestly costs an hour of driving, which is not a review process. This is the same
        /// argument DuckRallyShots.JumpToRally makes about the rally.
        ///
        /// It runs the REAL ending, not a preview of one: the mask is frozen, the standings are
        /// sorted from the live grid and the results are posted, exactly as they are when the clock
        /// runs out by itself. The only thing skipped is the waiting.
        /// </summary>
        public void DebugFinishNow()
        {
            if (State == Phase.Idle || State == Phase.Done) return;
            // Straight into Live first, so a skip pressed during the count-in still goes through
            // the horn rather than ending a match that never started.
            State = Phase.Live;
            TimeRemaining = 0.0001f;
            _phaseTimer = 0f;
        }

        /// <summary>
        /// Hand each gardener a plausible slice of the arena, for reviewing the ending on a board
        /// that has something on it.
        ///
        /// A match skipped to the end from the countdown finishes 0.0% / 0.0% / 0.0% / 0.0%, which
        /// tells you the sequence runs and nothing whatever about whether it READS — whether four
        /// close numbers are legible, whether the winner is obvious, whether the bars are
        /// distinguishable at a glance. So this paints one wedge per competitor, at deliberately
        /// uneven sizes, through the ordinary claim path — no special case, the same swaths a roller
        /// lays down, so what appears on the board is a real measurement of real ground.
        /// </summary>
        public void DebugSeedTerritory()
        {
            var mask = TurfMask.Instance;
            if (mask == null) return;

            // Uneven on purpose: a four-way dead heat is the one result that tells you least.
            float[] spread = { 1.0f, 0.78f, 0.55f, 0.32f };

            for (int i = 0; i < TurfArena.Count; i++)
            {
                float from = TurfArena.SpokeAngle(i) - 44f;
                float span = 88f * spread[i];
                for (float r = TurfArena.PlazaRadius * 0.3f; r < TurfArena.ArenaRadius - 2f; r += 2.2f)
                {
                    Vector3 prev = Vector3.zero;
                    bool have = false;
                    for (float a = from; a <= from + span; a += 3f)
                    {
                        Vector3 p = TurfArena.Bearing(a) * r;
                        if (have && TurfArena.IsPlayable(p) && TurfArena.IsPlayable(prev))
                            mask.Claim(prev, p, 3.2f, i);
                        prev = p;
                        have = true;
                    }
                }
            }
            Debug.Log($"[Bloom] seeded: " +
                      $"{mask.Share(0) * 100f:0.0} / {mask.Share(1) * 100f:0.0} / " +
                      $"{mask.Share(2) * 100f:0.0} / {mask.Share(3) * 100f:0.0} %");
        }

        // ------------------------------------------------------------------ callouts and trace

        public void Announce(string text, Color colour)
        {
            Callout = text;
            CalloutColour = colour;
            CalloutAge = 0f;
        }

        /// <summary>
        /// The board, one line per gardener.
        ///
        /// Exists because "the opponents are not taking any ground" has at least four causes that
        /// produce identical silence — bots not driving, bots driving into a hedge, the roller never
        /// coming down, or the claim rule rejecting everything — and no amount of staring at the
        /// percentages separates them. Share, metres stolen, plan and objective distance do.
        /// </summary>
        void TickTrace(float unscaled)
        {
            if (traceInterval <= 0f || State != Phase.Live) return;
            _traceTimer -= unscaled;
            if (_traceTimer > 0f) return;
            _traceTimer = traceInterval;

            var mask = TurfMask.Instance;
            if (mask == null) return;

            // ONE ENTRY PER GARDENER, not one entry with newlines in it.
            //
            // Unity keeps the whole string, but console readers — including the one this project is
            // reviewed through — show only the FIRST LINE of an entry. A multi-line board therefore
            // arrives as "[Bloom] t40s crown=none" and nothing else, which is exactly the
            // information the trace exists to carry. It cost most of a session to notice that one
            // gardener was claiming one percent of the arena, because every line saying why was
            // being cut off before it was read.
            foreach (var c in competitors)
            {
                if (c == null) continue;
                float r = new Vector2(c.Position.x, c.Position.z).magnitude;
                string plan = c.brain != null
                    ? $"{c.brain.plan,-8} obj {Vector3.Distance(c.Position, c.brain.Objective),4:0} m" +
                      (c.brain.RoutingVia >= 0 ? $" gap{c.brain.RoutingVia}" : " open") +
                      $" thr{c.brain.Throttle,5:0.00} str{c.brain.Steer,5:0.00}"
                    : "player";
                Debug.Log($"[Bloom] t{TimeRemaining:0}s {c.Name,-8} {mask.Share(c.slot) * 100f,5:0.0}%" +
                          $"  r{r,4:0}m  {c.Speed,4:0.0} m/s  stole {c.MetresStolen,5:0}" +
                          $"  lost {c.MetresLost,5:0}  {plan}");
            }
            Debug.Log($"[Bloom] t{TimeRemaining:0}s crown=" +
                      (Crown >= 0 ? TurfArena.Get(Crown).contestant : "none") +
                      $"  neutral {mask.NeutralShare * 100f:0.0}% of {mask.PlayableCells} cells");

            WriteTraceFile(mask);
        }

        /// <summary>
        /// The same board, appended to Captures/Bloom/trace.txt.
        ///
        /// Editor only, and it exists because the console cannot be relied on. This project is
        /// developed with more than one tool driving the same editor, and a console that is cleared
        /// — by a rebuild, by a domain reload, by whoever else is working — takes the entire match
        /// history with it. Twice now a match has been run purely to read this board and the board
        /// was gone before it could be read. A file outlives all of that, and diffing two runs of it
        /// is how a driving change is actually judged.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        void WriteTraceFile(TurfMask mask)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var c in competitors)
                {
                    if (c == null) continue;
                    float r = new Vector2(c.Position.x, c.Position.z).magnitude;
                    sb.Append($"t{TimeRemaining:00} {c.Name,-8} {mask.Share(c.slot) * 100f,5:0.0}%" +
                              $" r{r,4:0}m {c.Speed,4:0.0}m/s stole{c.MetresStolen,5:0}" +
                              $" lost{c.MetresLost,5:0}");
                    if (c.brain != null)
                        sb.Append($" {c.brain.plan,-8} obj{Vector3.Distance(c.Position, c.brain.Objective),4:0}m" +
                                  (c.brain.RoutingVia >= 0 ? $" gap{c.brain.RoutingVia}" : " open") +
                                  $" thr{c.brain.Throttle,5:0.00} str{c.brain.Steer,5:0.00}");
                    sb.AppendLine();
                }
                System.IO.Directory.CreateDirectory("Captures/Bloom");
                System.IO.File.AppendAllText("Captures/Bloom/trace.txt", sb.ToString());
            }
            catch (System.Exception e)
            {
                // A trace that throws must never take the match down with it.
                Debug.LogWarning($"[Bloom] could not write the trace file: {e.Message}");
            }
        }
    }
}
