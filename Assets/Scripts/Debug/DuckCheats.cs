#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckMow
{
    /// <summary>
    /// The keys that get you to the end of a stage without playing it.
    ///
    /// This exists for one measured cost. Every verdict in this game is a sequence of timed beats —
    /// the freeze, the lift, the percentages counting up, three animals raising a card each, the
    /// sweep to the board — and not one of those timings can be judged from a description. They have
    /// to be watched, and then watched again with one number changed. Reaching them honestly costs a
    /// hundred seconds of driving, so reviewing them honestly costs an hour of driving, which is not
    /// a review process. It is why the endings in this game are the least-looked-at part of it.
    ///
    /// So the whole design goal here is "press a key, see the thing, press it again". Everything
    /// below is subordinate to that.
    ///
    /// THREE RULES IT WILL NOT BREAK, and they are what separate a cheat from a lie.
    ///
    ///   1. It never shows you a presentation the real game cannot produce. Bloom Rush is ended by
    ///      driving its clock to zero so the horn fires for real — the mask freezes, the rollers
    ///      stop, the engines cut, the crowd applauds, and BeginReveal is reached the way it is
    ///      reached at the end of a match. Calling BeginReveal directly would have been one line
    ///      shorter and would have had you reviewing an ending that no player will ever see.
    ///      See <see cref="EndArenaNow"/>.
    ///   2. It never writes a result. It writes GROUND, through the same
    ///      <see cref="TurfMask.Claim"/> every roller in the arena goes through, and then lets the
    ///      director sort the standings out of the mask exactly as it always does. Nothing here
    ///      overrides the sort, stamps <see cref="TurfMask.Owned"/>, or touches
    ///      <see cref="TurfHandoff"/>. Same in the mowing round: it MOWS, through
    ///      <see cref="CutMask.CutSwath"/>, and the three judges mark what they find.
    ///   3. It is not in the game. The whole file is behind UNITY_EDITOR || DEVELOPMENT_BUILD, so a
    ///      release WebGL build does not contain it. To get it in a browser, tick "Development
    ///      Build" in Build Settings before exporting — that define is what turns this on, and it
    ///      is also the only build where the keys below are worth having.
    ///
    /// It hooks itself in and needs no scene edit, no prefab and no wiring, which matters because
    /// the four scenes this has to work in are opened and played independently of each other.
    /// </summary>
    public class DuckCheats : MonoBehaviour
    {
        // ------------------------------------------------------------------ the singleton

        static DuckCheats _instance;

        /// <summary>The live driver, or null. For code that must not create one just to ask.</summary>
        public static DuckCheats Live => _instance;

        /// <summary>
        /// Install after every scene load, with no edit to anything.
        ///
        /// AfterSceneLoad rather than BeforeSceneLoad because <see cref="Ensure"/> looks for a
        /// survivor from the previous scene before it builds one, and that search wants a loaded
        /// scene to search. It runs on every load and is idempotent; the second and later calls cost
        /// one null check.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            Ensure();
            Debug.Log("[Cheats] armed — F2 for the list. This is a development build; " +
                      "none of it ships.");
        }

        /// <summary>
        /// The driver, built if it is not already there. Modelled line for line on
        /// <see cref="Curtain.Ensure"/>, including the search for a survivor.
        /// </summary>
        public static DuckCheats Ensure()
        {
            if (_instance != null) return _instance;

            // A survivor from a previous scene that has not run Awake yet on this frame. Finding it
            // is cheaper than the duplicate it prevents.
            var found = FindFirstObjectByType<DuckCheats>(FindObjectsInactive.Include);
            if (found != null) { found.Init(); return _instance; }

            var go = new GameObject("~ DuckCheats");
            var c = go.AddComponent<DuckCheats>();
            c.Init();
            return _instance;
        }

        /// <summary>
        /// The duplicate guard, copied from <see cref="Curtain.Awake"/> deliberately.
        ///
        /// Most singletons in this project are written `void Awake() => Instance = this;` with no
        /// guard at all, which is a known defect: the second instance silently becomes the one
        /// everybody talks to and the first goes on ticking, invisible, holding stale references.
        /// A DontDestroyOnLoad object is the one place that fault is guaranteed rather than merely
        /// possible, because a scene load is exactly how a second one arrives. So the newcomer
        /// destroys itself, and the survivor is always the one that has been alive longest.
        /// </summary>
        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            Init();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void Init()
        {
            if (_instance == this) return;
            _instance = this;
            transform.SetParent(null, false);
            DontDestroyOnLoad(gameObject);
            // Deliberately NOT hidden with HideFlags. Curtain does not hide itself either, and for
            // the same reason: a debug object you cannot see in the hierarchy is a debug object you
            // cannot inspect when it is the thing that has gone wrong.
        }

        // ------------------------------------------------------------------ the key map

        /// <summary>
        /// One verb, bound twice.
        ///
        /// Every cheat has a top-row key and a numpad key, and that is not indecision. The numpad is
        /// the only part of the keyboard a browser has no opinion about at all, which makes it the
        /// right home for a tool whose entire purpose is reviewing a WebGL build — but this project
        /// is developed on a laptop, and half the laptops in the world do not have one. Binding both
        /// costs a single extra field per row.
        ///
        /// Nothing here collides with the game. <see cref="InputReader"/> owns WASD and the arrows,
        /// space, shift, E and Q, C, R, N, F, Enter and Escape; none of those appear below. Nor does
        /// anything the browser takes: F1 (help), F3 (find), F5 (reload), F6 (address bar), F7
        /// (caret browsing), F10 (menu bar), F11 (fullscreen) and F12 (dev tools) are all avoided,
        /// which leaves F2 as the one function key that is safe everywhere — so exactly one is used,
        /// for the overlay.
        /// </summary>
        struct Bind
        {
            public Key main, pad;
            public string label, help;
            public System.Action verb;
        }

        Bind[] _binds;

        void BuildBinds()
        {
            _binds = new[]
            {
                new Bind { main = Key.Digit1, pad = Key.Numpad1, label = "1",
                           help  = "judge it NOW — end what is running and play its verdict",
                           verb  = () => JudgeNow(Force.AsItStands) },

                new Bind { main = Key.Digit2, pad = Key.Numpad2, label = "2",
                           help  = "judge it as a WIN — force the duck first, then as above",
                           verb  = () => JudgeNow(Force.Win) },

                new Bind { main = Key.Digit3, pad = Key.Numpad3, label = "3",
                           help  = "judge it as a LOSS — force the duck last, then as above",
                           verb  = () => JudgeNow(Force.Loss) },

                new Bind { main = Key.Digit4, pad = Key.Numpad4, label = "4",
                           help  = "replay the verdict just watched, from the top",
                           verb  = Replay },

                new Bind { main = Key.Digit5, pad = Key.Numpad5, label = "5",
                           help  = "fill the board / mow the picture — nothing ends, just something to look at",
                           verb  = FillOnly },

                new Bind { main = Key.Digit6, pad = Key.Numpad6, label = "6",
                           help  = "skip the count-in / the opening story",
                           verb  = SkipTheWaiting },

                new Bind { main = Key.Minus, pad = Key.NumpadMinus, label = "-",
                           help  = "slower (halve, floor 0.1)",
                           verb  = () => Warp(0.5f) },

                new Bind { main = Key.Equals, pad = Key.NumpadPlus, label = "=",
                           help  = "faster (double, ceiling 8)",
                           verb  = () => Warp(2f) },

                new Bind { main = Key.Digit0, pad = Key.NumpadMultiply, label = "0",
                           help  = "back to normal speed",
                           verb  = () => Warp(0f) },
            };
        }

        bool _showList;

        // ------------------------------------------------------------------ the frame

        void Update()
        {
            // Nothing at all under the capture harness. It drives the game on a scripted clock to
            // produce reproducible frame sheets, and a stray key that ended a match halfway through
            // a shot list would make every sheet after it a lie — the same rule
            // GooseDefence.UpdateClock and PopupStack.ApplyGates are under.
            if (SimClock.Scripted) return;

            // Kept warm here rather than only on a keypress, so the overlay's header can name the
            // stage without OnGUI — which runs several times a frame — ever scanning the scene.
            _ = Arena;

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb[Key.F2].wasPressedThisFrame) _showList = !_showList;

            if (_binds == null) BuildBinds();
            for (int i = 0; i < _binds.Length; i++)
            {
                if (!kb[_binds[i].main].wasPressedThisFrame && !kb[_binds[i].pad].wasPressedThisFrame)
                    continue;
                _binds[i].verb();
            }

            RestoreIntroLengths();
        }

        // ------------------------------------------------------------------ which stage is running

        TurfDirector _arena;
        float _arenaLookup;

        /// <summary>
        /// The Bloom Rush director if one is loaded, or null.
        ///
        /// Resolved by search rather than through a static, because <see cref="TurfDirector"/>
        /// deliberately keeps its own live reference private and this file is not entitled to widen
        /// somebody else's API for a debug tool. Throttled to twice a second, because in the mowing
        /// round the answer is null and an unthrottled FindFirstObjectByType would be a whole-scene
        /// scan every frame for the rest of the session. Unity's null operator notices the moment
        /// the arena scene is unloaded, which is what makes the cache safe to hold at all.
        /// </summary>
        TurfDirector Arena
        {
            get
            {
                if (_arena != null) return _arena;
                if (Time.unscaledTime < _arenaLookup) return null;
                _arenaLookup = Time.unscaledTime + 0.5f;
                _arena = FindFirstObjectByType<TurfDirector>();
                return _arena;
            }
        }

        enum Force { AsItStands, Win, Loss }

        /// <summary>What the last verdict was run with, so <see cref="Replay"/> can run the same one.</summary>
        Force _lastForce = Force.AsItStands;
        /// <summary>The arena board the last verdict was played on, in degrees per slot. Null in the round.</summary>
        float[] _lastBoard;

        // ------------------------------------------------------------------ the headline cheat

        /// <summary>
        /// End whatever stage is running and play its verdict.
        ///
        /// Two stages have one, they are completely different sequences, and this picks by what is
        /// loaded rather than by anything the caller has to know:
        ///
        ///   BLOOM RUSH   the three judges' bench in the arena — TurfVerdict's Settle, LeanIn, one
        ///                card per judge, Hold — wrapped in TurfDirector's overhead lift, result
        ///                card and sweep to the board.
        ///   THE ROUND    the panel at the venue — JudgePanel deliberating and raising three cards —
        ///                and the verdict, tour and scoreboard that follow it.
        /// </summary>
        void JudgeNow(Force force)
        {
            var arena = Arena;
            if (arena != null) { JudgeArena(arena, force); return; }

            var round = GameDirector.Instance;
            if (round != null) { JudgeRound(round, force); return; }

            Debug.LogWarning("[Cheats] nothing to judge — there is no GameDirector and no " +
                             "TurfDirector in any loaded scene. Are you on the front page?");
        }

        void Replay()
        {
            var arena = Arena;
            if (arena != null) { JudgeArena(arena, _lastForce, replay: true); return; }

            var round = GameDirector.Instance;
            // In the mowing round a replay IS the verdict: see JudgeRound, which recomputes the
            // score off the lawn and re-enters the panel every time it is called.
            if (round != null) JudgeRound(round, _lastForce);
        }

        // ------------------------------------------------------------------ Bloom Rush

        /// <summary>
        /// The arena's ending, forced.
        ///
        /// The board is painted FIRST and the match ended second, and that order is the whole of the
        /// honesty argument. Ground has to be claimed while the mask is still live, because the horn
        /// freezes it — so a forced result is ground that was taken during the match, exactly like
        /// every other square metre in the arena, and the standings that come out of
        /// TurfDirector.BeginReveal are sorted from the same TurfMask.Owned counts as a real match's.
        ///
        /// A forced result RESTARTS the match before painting, and that is deliberate rather than
        /// lazy. Painting over a live board would leave whatever the player had already claimed
        /// sitting outside the wedges, so the final percentages would depend on how far into the
        /// match the key was pressed — and a review board that is different every time is a review
        /// board you cannot compare two runs of. Begin() clears the mask, unfreezes it and re-arms
        /// the whole cast, so a forced win looks the same on the tenth press as on the first.
        /// "As it stands" does not restart, because the entire point of it is the live board.
        /// </summary>
        void JudgeArena(TurfDirector d, Force force, bool replay = false)
        {
            var mask = TurfMask.Instance;
            if (mask == null)
            {
                Debug.LogWarning("[Cheats] the arena has no TurfMask; there is no ground to judge.");
                return;
            }

            bool spent = d.State == TurfDirector.Phase.Lock ||
                         d.State == TurfDirector.Phase.Reveal ||
                         d.State == TurfDirector.Phase.Done;

            // "As it stands" on a match that has already ended has nothing left to end, so it
            // becomes a replay. That is what makes key 1 pressable twice in a row, which is the
            // behaviour actually being asked for.
            if (force == Force.AsItStands && spent && !replay)
            {
                Debug.Log("[Cheats] that match is already over — replaying its result instead.");
                JudgeArena(d, _lastForce, replay: true);
                return;
            }

            float[] degrees = null;

            if (force != Force.AsItStands)
            {
                degrees = Allocation(force, PlayerSlot(d));
            }
            else if (replay)
            {
                // Replaying a board nobody authored. The geometry of what the four of them actually
                // painted is gone the moment the mask is cleared and cannot be recreated — but the
                // NUMBERS can, and the numbers are what the standings, the cards and the board are
                // made of. So the last shares are read back out and re-laid as four sectors of
                // proportional width. The percentages come back within a point or so; the shape of
                // the arena in the overhead does not. Named here because it is the one place this
                // file shows you something a real match would not have looked like.
                degrees = ProportionalTo(mask);
                if (degrees != null) _lastBoard = degrees;
                else degrees = _lastBoard;
            }

            if (force != Force.AsItStands || replay)
            {
                d.Begin();
                // Begin re-arms the match but knows nothing about the bench, so a replay pressed
                // while three judges are mid-sequence would leave two cards in the air and one
                // animal still leaning in through the next match's horn. Its own Begin puts the
                // cards down and clears the portraits; BeginReveal calls it again a beat later and
                // the second call is harmless.
                d.verdict?.Begin();
                if (degrees != null) { PaintArena(degrees); _lastBoard = degrees; }
            }

            _lastForce = force;
            EndArenaNow(d);
        }

        /// <summary>
        /// Bring the horn forward, and nothing else.
        ///
        /// <see cref="TurfDirector.DebugFinishNow"/> puts the phase machine into Live with a
        /// hair of clock left, so the very next TickLive runs the REAL ending: TimeRemaining hits
        /// zero, the mask freezes, every competitor stops painting and cuts its engine, "TIME!" is
        /// announced, the horn sounds, the crowd applauds and the arena effects fire — and only then
        /// does Lock hold for lockSeconds and hand over to BeginReveal.
        ///
        /// This is why nothing in this file calls BeginReveal. BeginReveal on its own would skip the
        /// freeze (so the rollers keep painting through the lift and the percentage on the board is
        /// not the percentage the reveal counted up to), skip the engine cut, skip the horn and skip
        /// the lock beat. You would be reviewing the pacing of an ending the game never plays.
        /// </summary>
        void EndArenaNow(TurfDirector d)
        {
            d.DebugFinishNow();
            Debug.Log($"[Cheats] arena: horn brought forward. The reveal, the bench and the board " +
                      $"run from here, in full — about {d.revealSeconds:0}s.");
        }

        /// <summary>
        /// Which slot the duck is driving.
        ///
        /// From the director when it has been through Begin, and from the arena's own seating table
        /// otherwise, so a forced result still lands on the right machine when the key is pressed
        /// before a match has started.
        /// </summary>
        static int PlayerSlot(TurfDirector d)
        {
            if (d != null && d.Player != null) return d.Player.slot;
            for (int i = 0; i < TurfArena.Count; i++) if (TurfArena.Get(i).isPlayer) return i;
            return 0;
        }

        /// <summary>
        /// How wide a sector each gardener gets, in degrees, indexed by slot.
        ///
        /// The four numbers always add to 360, so the wedges tile the whole disc and every playable
        /// cell is overwritten. That is what makes a forced result a result rather than a hope: no
        /// ground survives from before, so nothing left over in a corner can quietly outweigh the
        /// margin. The margins themselves are wide enough to read as a result and narrow enough to
        /// read as a match — a 3:1 rout tells you nothing about whether four close numbers are
        /// legible on the card, which is most of what the reveal has to get right.
        /// </summary>
        static float[] Allocation(Force force, int playerSlot)
        {
            // The player's share first, then the three rivals in descending order.
            float[] plan = force == Force.Win
                ? new[] { 126f, 90f, 76f, 68f }      // duck ~35%, best rival ~25%
                : new[] { 58f, 122f, 96f, 84f };     // duck ~16%, last of the rest ~23%

            var byPlace = new float[TurfArena.Count];
            byPlace[playerSlot] = plan[0];
            int next = 1;
            for (int i = 0; i < TurfArena.Count; i++)
                if (i != playerSlot) byPlace[i] = plan[next++];
            return byPlace;
        }

        /// <summary>Four sectors sized to the shares currently on the mask. See the note in JudgeArena.</summary>
        static float[] ProportionalTo(TurfMask mask)
        {
            var deg = new float[TurfArena.Count];
            float sum = 0f;
            for (int i = 0; i < TurfArena.Count; i++) sum += mask.Share(i);
            if (sum <= 0.02f) return null;                 // a blank board has nothing to reproduce
            for (int i = 0; i < TurfArena.Count; i++) deg[i] = mask.Share(i) / sum * 360f;
            return deg;
        }

        /// <summary>
        /// Lay the board down, one sector per gardener, through the ordinary claim path.
        ///
        /// The same technique as <see cref="TurfDirector.DebugSeedTerritory"/> — concentric arcs
        /// swept as swaths — and deliberately so, because the two have to produce comparable boards.
        /// It is not a call to it: that one hands out four fixed wedge sizes and cannot express "the
        /// duck wins", which is the entire requirement here.
        ///
        /// Every metre goes through <see cref="TurfMask.Claim"/> at a roller's width, so
        /// Owned, the sector table, the plaza table and the visible mask all agree with each other
        /// afterwards exactly as they do mid-match. Nothing is stamped and nothing is overridden.
        ///
        /// Costs about ten thousand vertices in one frame, which is inside the mask's 16-bit index
        /// budget with room to spare, and it happens once per keypress.
        /// </summary>
        static void PaintArena(float[] degrees)
        {
            var mask = TurfMask.Instance;
            if (mask == null || mask.Frozen || degrees == null) return;

            float at = 0f;
            for (int slot = 0; slot < TurfArena.Count && slot < degrees.Length; slot++)
            {
                PaintWedge(mask, slot, at, degrees[slot]);
                at += degrees[slot];
            }

            Debug.Log($"[Cheats] board painted — " +
                      $"{mask.Share(0) * 100f:0.0} / {mask.Share(1) * 100f:0.0} / " +
                      $"{mask.Share(2) * 100f:0.0} / {mask.Share(3) * 100f:0.0} % " +
                      $"· neutral {mask.NeutralShare * 100f:0.0}%");
        }

        static void PaintWedge(TurfMask mask, int slot, float fromDeg, float spanDeg)
        {
            // Ring spacing under the swath width and an arc step short enough that the outermost
            // ring still overlaps itself. Gaps here would show up as unclaimed speckle in the
            // overhead, which is the one shot the reveal is built around.
            const float ringStep = 2.0f, arcStep = 2.5f, swath = 3.2f;

            for (float r = TurfArena.CoreRadius; r < TurfArena.ArenaRadius - 1.5f; r += ringStep)
            {
                Vector3 prev = Vector3.zero;
                bool have = false;
                for (float a = fromDeg; a <= fromDeg + spanDeg; a += arcStep)
                {
                    Vector3 p = TurfArena.Bearing(a) * r;
                    // The same playability test the rollers are under, so hedges, the core and the
                    // blocked ground stay blocked and the neutral share stays honest.
                    if (have && TurfArena.IsPlayable(p) && TurfArena.IsPlayable(prev))
                        mask.Claim(prev, p, swath, slot);
                    prev = p;
                    have = true;
                }
            }
        }

        // ------------------------------------------------------------------ the mowing round

        /// <summary>
        /// The venue's panel, from wherever the round happens to be.
        ///
        /// Reveal, then Judging, in one frame. Reveal is not decoration: it is the ONLY place
        /// <see cref="GameDirector.LastScore"/> is computed, and it computes it by running
        /// <see cref="RoundTarget.Evaluate"/> over the real cut mask — so the three marks the bench
        /// raises are the marks the lawn on screen actually earns, off the same curve every rival in
        /// the venue is measured by. Nothing here writes a score.
        ///
        /// The panel is reset first so a replay does not open with two cards already in the air from
        /// the previous run. It is safe to do in this order: GameDirector runs at execution order
        /// -10 and has already ticked by the time this does, so it never sees the one instant where
        /// the panel reads finished while the state reads Judging.
        ///
        /// WHAT THIS SKIPS, and it is worth being straight about it: the klaxon and its hit stop,
        /// the reveal's own three beats over the picture, the stage chain out to the goose rally and
        /// Bloom Rush, and the blossom curtain that normally covers the cut to the bench. Reviewing
        /// any of those needs the real path — press 6, drive, and let the round end by itself.
        /// </summary>
        void JudgeRound(GameDirector g, Force force)
        {
            if (force != Force.AsItStands) MowFor(g, force);

            g.judges?.ResetPanel();
            g.DebugForceState(GameState.Reveal);
            g.DebugForceState(GameState.Judging);

            _lastForce = force;
            _lastBoard = null;

            var s = g.LastScore;
            Debug.Log($"[Cheats] the panel: coverage {s.coverage * 100f:0}% · spill " +
                      $"{s.spill * 100f:0}% · edge {s.edgeQuality * 100f:0}% · legibility " +
                      $"{s.legibility * 100f:0}%. The three of them mark that, not me.");
        }

        /// <summary>
        /// Put something on the lawn worth marking — well, or badly.
        ///
        /// Through <see cref="CutMask.CutSwath"/>, the one entry point the mower's own blade uses, so
        /// what the judges measure is genuinely mown grass on the CPU grid and genuinely cut turf in
        /// the render texture. The reveal's ghost fill and the analysis overlay therefore work over
        /// it too, which they would not if this had poked bytes into the grid.
        ///
        /// WELL is a scan of the picture: one pass per metre along every row the target says is
        /// inside, at a roller's width. It fills the shape and spills a little at the ends of each
        /// run, which is what a good round looks like — around an A on the bench.
        ///
        /// BADLY is not an empty lawn, and that distinction matters. An empty lawn scores zero
        /// coverage but PERFECT neatness, because neatness is one minus spill and nothing spilled —
        /// so the Craft chair marks a blank field respectably and the loss you are reviewing is not
        /// a loss anybody could actually have. So it lays a scruffy diagonal scribble across the
        /// whole field instead: some of the picture, a great deal of everything else, low coverage
        /// and high spill. A D, honestly earned.
        /// </summary>
        static void MowFor(GameDirector g, Force force)
        {
            var mask = g.cutMask;
            var target = g.target;
            if (mask == null || target == null || target.Inside == null)
            {
                Debug.LogWarning("[Cheats] no cut mask or no picture built yet; nothing to mow.");
                return;
            }

            mask.ClearAll();
            if (force == Force.Win) MowThePicture(mask, target);
            else MowAScribble(mask);
        }

        static void MowThePicture(CutMask mask, RoundTarget target)
        {
            const int rowStep = 4;             // 1 m at a 25 cm grid
            const float swath = 1.15f;         // just over the row pitch, so the rows meet

            for (int gz = rowStep / 2; gz < Field.GridRes; gz += rowStep)
            {
                int row = gz * Field.GridRes;
                int runStart = -1;

                for (int gx = 0; gx <= Field.GridRes; gx++)
                {
                    bool inside = gx < Field.GridRes && target.Inside[row + gx];
                    if (inside)
                    {
                        if (runStart < 0) runStart = gx;
                        continue;
                    }
                    if (runStart < 0) continue;

                    // One pass per contiguous interior run, so a shape with two lobes on a row —
                    // the heart's cleft, the anchor's flukes — gets two passes and no line joining
                    // them across the gap. Joining them would spill straight through the middle of
                    // the picture and cost the very marks this is trying to earn.
                    mask.CutSwath(Field.GridToWorld(runStart, gz), Field.GridToWorld(gx - 1, gz), swath);
                    runStart = -1;
                }
            }
        }

        static void MowAScribble(CutMask mask)
        {
            const float spacing = 3.4f, swath = 1.6f, degrees = 31f;
            float rad = degrees * Mathf.Deg2Rad;
            Vector3 along = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            Vector3 across = new Vector3(-along.z, 0f, along.x);

            // Run well past the edges at both ends and let the mask clip. CutMask.ForEachCell
            // clamps to the grid and the stamp shader is clipped by the field's own ortho
            // projection, so an overrunning swath is free and a short one leaves a suspicious
            // untouched border that reads as deliberate rather than as a bad round.
            for (float o = -Field.Size; o <= Field.Size; o += spacing)
            {
                Vector3 c = across * o;
                mask.CutSwath(c - along * Field.Size, c + along * Field.Size, swath);
            }
        }

        // ------------------------------------------------------------------ companions

        /// <summary>Put a board up, or a picture down, without ending anything.</summary>
        void FillOnly()
        {
            var arena = Arena;
            if (arena != null)
            {
                var mask = TurfMask.Instance;
                if (mask == null || mask.Frozen)
                {
                    Debug.LogWarning("[Cheats] the mask is frozen — this match is over. Press 4.");
                    return;
                }
                // Four plausible sizes handed out in a shuffled order, so the winner is not always
                // the same machine and the reveal is reviewed against a board somebody might
                // actually have painted rather than against a fixture.
                var plan = new[] { 104f, 92f, 84f, 80f };
                for (int i = plan.Length - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (plan[i], plan[j]) = (plan[j], plan[i]);
                }
                PaintArena(plan);
                _lastBoard = plan;
                return;
            }

            var round = GameDirector.Instance;
            if (round != null) MowFor(round, Force.Win);
        }

        TurfDirector _shortened;
        float[] _introSaved;

        /// <summary>
        /// Get past the part before the playing.
        ///
        /// In the arena that means the count-in, and there is no public way to end it — so the three
        /// fields that MEASURE it are shortened instead, and TickIntro reaches Live under its own
        /// power a frame later. The horn, the release of the four brains, the handover of the wheel
        /// and the start of painting all happen exactly as they always do; only the waiting is gone.
        /// The original lengths are put back the moment the phase leaves Intro, so the arena is not
        /// left permanently three seconds shorter for whoever looks at it next.
        ///
        /// In the round it means the opening story, or the briefing and count-in, depending on where
        /// the round has got to. SkipIntro is the director's own escape hatch and Mowing is a state
        /// the round enters by itself a moment later regardless.
        /// </summary>
        void SkipTheWaiting()
        {
            var arena = Arena;
            if (arena != null)
            {
                if (arena.State != TurfDirector.Phase.Intro)
                {
                    Debug.Log("[Cheats] the arena is past its count-in already.");
                    return;
                }
                if (_shortened != arena)
                {
                    _shortened = arena;
                    _introSaved = new[] { arena.establishSeconds, arena.openingSwoop, arena.introSeconds };
                }
                arena.establishSeconds = 0f;
                arena.openingSwoop = 0.12f;
                arena.introSeconds = 0.12f;
                return;
            }

            var round = GameDirector.Instance;
            if (round == null) return;

            if (round.State == GameState.Intro) { round.SkipIntro(); return; }
            if (round.State == GameState.Briefing || round.State == GameState.Preview ||
                round.State == GameState.Countdown)
                round.DebugForceState(GameState.Mowing);
        }

        void RestoreIntroLengths()
        {
            if (_shortened == null || _introSaved == null) return;
            if (_shortened.State == TurfDirector.Phase.Intro) return;
            _shortened.establishSeconds = _introSaved[0];
            _shortened.openingSwoop = _introSaved[1];
            _shortened.introSeconds = _introSaved[2];
            _shortened = null;
            _introSaved = null;
        }

        /// <summary>
        /// Fast-forward, without ever touching a clock somebody else is holding.
        ///
        /// <see cref="DuckMow.UI.PopupStack"/> BORROWS Time.timeScale while a popup is open: it
        /// snapshots the value on push, re-reads it every frame from PostLateUpdate so a hit stop
        /// that expires behind a pause menu is not the value handed back, writes zero, and restores
        /// on pop. It is the most careful code in the project and it does not need help.
        ///
        /// Two rules keep this entirely out of its way:
        ///
        ///   * NOTHING IS WRITTEN WHILE A POPUP IS OPEN. A write during the borrow would be
        ///     collected by ApplyGates as "what the world last wanted" and handed to the player when
        ///     they closed the menu — so closing a pause menu would silently leave the game at eight
        ///     times speed with nothing on screen to say why. Refusing to write inside the borrow
        ///     window means the stack can never collect a cheat value at all.
        ///   * ZERO IS NEVER WRITTEN. ApplyGates deliberately declines to collect a zero, because
        ///     zero is what it writes itself — so a zero from here would be a stopped game that
        ///     nothing is coming along to restart. The floor is 0.1.
        ///
        /// It is also a borrow the game itself will take back: GameDirector's klaxon hit stop writes
        /// the clock every frame and releases it to 1 on every state change, so a warp set before
        /// the end of a round does not survive the end of that round. That is correct behaviour and
        /// not worth fighting.
        /// </summary>
        void Warp(float factor)
        {
            if (SimClock.Scripted) return;
            if (DuckMow.UI.PopupStack.Any)
            {
                Debug.Log("[Cheats] a popup is holding the clock; close it first.");
                return;
            }
            Time.timeScale = factor <= 0f ? 1f : Mathf.Clamp(Time.timeScale * factor, 0.1f, 8f);
        }

        // ------------------------------------------------------------------ the list

        /// <summary>
        /// Drawn with OnGUI, and that is the right call here — please do not "fix" it.
        ///
        /// Every real screen in this game is a Canvas built in code by a scene builder, and it must
        /// stay that way: the HUD, the popups and the cards are art, they scale with the reference
        /// resolution, and they are reviewed in screenshots. This is none of those things. It is a
        /// list of keys that has to appear in four different scenes, two of which are opened and
        /// played on their own, in a build where nothing has been wired for it — and OnGUI is the
        /// only drawing surface in Unity that needs no canvas, no scaler, no raycaster, no font
        /// asset, no prefab and no scene edit at all. A canvas version would need building,
        /// destroying and re-parenting across every load, would fight the popup stack for sort
        /// order, and would put debug text in the same layer the game's real UI is judged in.
        ///
        /// It also draws at Time.timeScale zero and under a curtain, which is exactly when you most
        /// want to be able to read which key does what.
        ///
        /// Off by default and toggled by F2, because this project is reviewed through screenshots
        /// and a permanent overlay would be in every one of them.
        /// </summary>
        GUIStyle _style;

        void OnGUI()
        {
            if (!_showList) return;
            if (_binds == null) BuildBinds();

            const float w = 560f, pad = 10f, line = 19f;
            float h = pad * 2f + line * (_binds.Length + 3);
            var box = new Rect(pad, pad, w, h);

            GUI.color = new Color(0.05f, 0.09f, 0.06f, 0.86f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Built once. OnGUI runs several times a frame — layout and repaint at minimum — and a
            // GUIStyle allocated in it is garbage on every one of those passes, which shows up as a
            // GC sawtooth in exactly the profile capture somebody is probably taking.
            if (_style == null)
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    richText = false,
                    normal = { textColor = new Color(0.93f, 0.95f, 0.88f) }
                };
            var style = _style;

            float y = box.y + pad;
            GUI.Label(new Rect(box.x + pad, y, w - pad * 2f, line),
                      $"DUCK CHEATS · {Where()} · time x{Time.timeScale:0.##}", style);
            y += line;
            GUI.Label(new Rect(box.x + pad, y, w - pad * 2f, line),
                      "F2 hides this. Numpad equivalents in brackets.", style);
            y += line * 1.5f;

            for (int i = 0; i < _binds.Length; i++, y += line)
                GUI.Label(new Rect(box.x + pad, y, w - pad * 2f, line),
                          $"  {_binds[i].label,-2}  ({Pad(_binds[i].pad),-4})  {_binds[i].help}", style);
        }

        static string Pad(Key k) => k switch
        {
            Key.NumpadPlus => "num+",
            Key.NumpadMinus => "num-",
            Key.NumpadMultiply => "num*",
            _ => "num" + k.ToString().Replace("Numpad", "")
        };

        string Where()
        {
            var arena = _arena;                     // the cache, not the throttled lookup — OnGUI
            if (arena != null) return $"BLOOM RUSH · {arena.State}";   // must not scan the scene
            var round = GameDirector.Instance;
            return round != null ? $"THE ROUND · {round.State}" : "no director";
        }
    }
}
#endif
