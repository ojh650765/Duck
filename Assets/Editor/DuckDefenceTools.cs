using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Getting at the defence phase without mowing a seventy-five second round first.
    ///
    /// The phase is twelve seconds that arrive at the very end of a round, which makes it the most
    /// expensive thing in the game to look at twice — and it is a greybox whose whole purpose is to
    /// be looked at many times and then kept or cut. So: one item to arm it, one to fast-forward a
    /// real round into it, and one to run it again on the picture that is already on the lawn.
    ///
    /// Deliberately a separate file from <see cref="DuckPlayTools"/>. If the phase is cut, this file
    /// goes with it and nothing else has to be edited.
    ///
    /// ON CAPTURE: `Duck/Capture · Single frame` and the full frame sheet both work during this phase,
    /// and it is worth recording why they once did not. An earlier version of the phase ran on a
    /// dedicated camera and switched Camera.main off for the duration, which made every capture tool
    /// report "No main camera to capture" — the phase looked like it was not rendering at all. The
    /// phase now runs on the ordinary chase camera, so the tools see it like any other beat. Anything
    /// added here that takes the camera away must not reintroduce that.
    /// </summary>
    public static class DuckDefenceTools
    {
        static GameDirector Director => Object.FindFirstObjectByType<GameDirector>();
        static Autopilot Pilot => Object.FindFirstObjectByType<Autopilot>();

        /// <summary>Matches DuckPlayTools, so every capture in the project lands in one folder.</summary>
        const string CaptureDir = "Captures";

        [MenuItem("Duck/Play · Defence phase ON", priority = 33)]
        public static void On()
        {
            var d = Director;
            if (d == null) { Debug.LogWarning("[Duck] Enter play mode first."); return; }
            d.defencePhaseEnabled = true;
            Debug.Log("[Duck] Defence phase ARMED — it will run between the klaxon and the reveal.");
        }

        [MenuItem("Duck/Play · Defence phase OFF", priority = 34)]
        public static void Off()
        {
            var d = Director;
            if (d == null) return;
            d.defencePhaseEnabled = false;
            d.defence?.Abort();
            Debug.Log("[Duck] Defence phase off. The round is back to klaxon straight into reveal.");
        }

        /// <summary>
        /// Mow a whole round on fast-forward, then hand the player the defence phase at normal speed.
        ///
        /// The autopilot does the mowing because the phase needs a picture to defend and a hand-mown
        /// one takes over a minute to produce. It is switched off again before the flock arrives —
        /// the whole point is to have real hands on the twelve seconds.
        /// </summary>
        [MenuItem("Duck/Play · Fast-forward into the defence phase", priority = 35)]
        public static void FastForward()
        {
            var d = Director;
            if (d == null) { Debug.LogWarning("[Duck] Enter play mode first."); return; }
            d.StartCoroutine(Routine(d));
        }

        static IEnumerator Routine(GameDirector d)
        {
            d.defencePhaseEnabled = true;
            d.autopilotEnabled = true;
            d.SkipIntro();
            d.RetrySameShape();

            Time.timeScale = 6f;

            // Wound on until the mowing is genuinely over rather than for a measured number of
            // seconds: the round length varies with the picture, and a stopwatch would either cut a
            // star short or sit through eight seconds of finished lawn.
            float guard = 0f;
            while (d.State != GameState.Defence && guard < 60f)
            {
                guard += Time.unscaledDeltaTime;
                if (d.State == GameState.Mowing)
                {
                    var p = Pilot;
                    // Once the autopilot has run out of route there is nothing left to watch, so end
                    // the round rather than idling at 6x through the remaining clock.
                    if (p != null && p.WaypointCount > 0 && p.WaypointIndex >= p.WaypointCount)
                        d.DebugSetTimeRemaining(0.01f);
                }
                yield return null;
            }

            Time.timeScale = 1f;
            d.autopilotEnabled = false;
            Pilot?.Stop();

            if (d.State != GameState.Defence)
                Debug.LogWarning($"[Duck] never reached the defence phase (stuck in {d.State}).");
            else
                Debug.Log("[Duck] Defence phase live. A/D to move, E to honk.");
        }

        /// <summary>
        /// Run the raid again on the lawn that is already there.
        ///
        /// The fastest review loop there is: mow once, then replay the twelve seconds as often as it
        /// takes to judge them. Damage is NOT undone between runs — the mask is the picture and
        /// nothing resets it short of a new round — so each replay takes its budget as a fraction of
        /// an already-scuffed lawn. Fine for judging the feel, useless for judging the score.
        /// </summary>
        [MenuItem("Duck/Play · Replay the defence phase", priority = 36)]
        public static void Replay()
        {
            if (!EditorApplication.isPlaying)
            {
                // Guarded because the arena branch below uses FindFirstObjectByType, which finds scene
                // objects in EDIT mode too — so without this it reported "replaying" while starting a
                // phase nothing was ticking, and that false success was then read as proof of play mode.
                Debug.LogWarning("[Duck] Enter play mode first.");
                return;
            }
            Time.timeScale = 1f;

            // The standalone arena has no GameDirector to route through, and a raid there is over in
            // about twenty seconds — so replaying is the single most-used action in the review loop and
            // it silently did nothing in the scene the phase lives in. Begin() restarts the raid
            // wherever it is running.
            var boot = Object.FindFirstObjectByType<ArenaBootstrap>();
            if (boot != null && boot.defence != null)
            {
                boot.defence.Begin();
                Debug.Log("[Duck] Replaying the raid in the arena. Damage from earlier runs is still down.");
                return;
            }

            var d = Director;
            if (d == null) { Debug.LogWarning("[Duck] Enter play mode first."); return; }

            d.defencePhaseEnabled = true;
            d.autopilotEnabled = false;
            Pilot?.Stop();

            // Entering a state you are already in is a no-op by design (see GameDirector.SetState), so
            // re-running the phase from inside itself has to restart the raid directly rather than ask
            // for a state change that will be politely refused.
            if (d.State == GameState.Defence) d.defence?.Begin();
            else d.DebugForceState(GameState.Defence);

            Debug.Log("[Duck] Replaying the defence phase. Damage from earlier runs is still on the lawn.");
        }

        // ------------------------------------------------------------------ scripted parries
        //
        // These exist because the review loop drives Unity through MCP and CANNOT SEND KEYSTROKES. The
        // horn is therefore unreachable from it, so every frame ever captured of this phase was a miss
        // — and the tiers, the hit stop, the perfect slow-motion, the camera punch, the launch arc and
        // any rally at all were invisible to the only process able to look at them.
        //
        // They inject the honk at the right instant and change nothing else. E still belongs to the
        // player.

        /// <summary>
        /// The running phase, however it is being driven.
        ///
        /// Two drivers now, and this deliberately does not care which: GameDirector when a round is out
        /// at the arena, and ArenaBootstrap when Arena.unity has simply been opened and played. The
        /// second is the one that matters for review — it is the difference between having to run a
        /// whole round to look at one parry and pressing play on the level itself.
        ///
        /// The bootstrap is checked FIRST. When the arena is open standalone there is no GameDirector at
        /// all, and requiring one is exactly what made every tool below unusable in the scene the phase
        /// actually lives in.
        /// </summary>
        /// <summary>
        /// Run something once the editor is genuinely in play mode, entering it if it is not.
        ///
        /// Five capture attempts were lost to this and the cause is a race, not a mistake anybody could
        /// see: `manage_editor play` returns success, and the very next tool call runs a menu item — but
        /// EditorApplication.isPlaying is still FALSE, because entering play mode costs a domain reload
        /// and the reload has not finished. Retrying the same sequence produced the same result every
        /// time, and the fifth attempt failed for exactly the reason the first did.
        ///
        /// Worse, the failure mode lied. Object.FindFirstObjectByType finds scene objects in EDIT mode
        /// too, so a tool that skipped the isPlaying check appeared to work — it started the phase in the
        /// editor, where nothing ticks — and its success message was then read as proof that play mode
        /// was live.
        ///
        /// Polling EditorApplication.update is the fix rather than a longer sleep, because the wait is
        /// for a state change of unknown length, not for a duration.
        /// </summary>
        static void RunWhenPlaying(System.Action action)
        {
            if (EditorApplication.isPlaying) { action(); return; }

            // Deliberately does NOT enter play mode itself, and that is the whole finding here.
            //
            // The obvious version called EditorApplication.EnterPlaymode and waited on
            // EditorApplication.update for isPlaying to turn true. It can never work: entering play mode
            // triggers a DOMAIN RELOAD, and a domain reload destroys all static state — including the
            // update subscription holding the callback. The continuation is thrown away before the state
            // it was waiting for arrives, so the capture silently never runs.
            //
            // It also caused a regression that is worth recording: wrapping the parry burst in that
            // version broke a burst which had been working, and the failure looked identical to the bug
            // it was meant to fix. Surviving a domain reload needs SessionState, not a delegate.
            //
            // Entering play mode is one separate call away for whoever is driving, and two calls that
            // work beat one that cannot.
            Debug.LogWarning("[Duck] Enter play mode first, then run this again — a capture cannot " +
                             "start play mode for you (the domain reload would discard it).");
        }

        static GooseDefence Phase
        {
            get
            {
                if (!EditorApplication.isPlaying)
                {
                    Debug.LogWarning("[Duck] Enter play mode first.");
                    return null;
                }

                // Standalone arena: the phase is right there and already running.
                var boot = Object.FindFirstObjectByType<ArenaBootstrap>();
                if (boot != null && boot.defence != null) return boot.defence;

                var d = Director;
                if (d == null)
                {
                    Debug.LogWarning("[Duck] No GameDirector and no ArenaBootstrap — open Assets/Scenes/" +
                                     "Arena.unity and press play, or start a round in Main.");
                    return null;
                }
                if (d.defence == null || d.State != GameState.Defence)
                {
                    Debug.LogWarning("[Duck] Not in the defence phase — run " +
                                     "'Fast-forward into the defence phase' or 'Replay' first.");
                    return null;
                }
                return d.defence;
            }
        }

        [MenuItem("Duck/Play · Rally: force a PERFECT parry", priority = 40)]
        public static void ForcePerfect() => Arm(GooseDefence.Tier.Perfect, 1);

        [MenuItem("Duck/Play · Rally: force a GOOD parry", priority = 41)]
        public static void ForceGood() => Arm(GooseDefence.Tier.Good, 1);

        [MenuItem("Duck/Play · Rally: force a NORMAL parry", priority = 42)]
        public static void ForceNormal() => Arm(GooseDefence.Tier.Normal, 1);

        /// <summary>
        /// Auto-parry five in a row, so the escalation can actually be watched.
        ///
        /// The window has to be widened to fit, and that is a finding rather than a workaround: at the
        /// tuned dive speeds one exchange costs between three and four seconds, so twelve seconds holds
        /// about three of them and `rallyEnergyFull = 6` is unreachable in a real round. Five hits need
        /// roughly eighteen. The tool states what it changed so nobody reads the resulting capture as
        /// evidence that a default round can rally five times.
        /// </summary>
        [MenuItem("Duck/Play · Rally: run a 5-hit rally", priority = 43)]
        public static void RunRally()
        {
            var p = Phase;
            if (p == null) return;

            const int hits = 5;
            float needed = 19f;
            if (p.liveSeconds < needed)
            {
                Debug.Log($"[Duck] widening the phase from {p.liveSeconds:0.#}s to {needed:0.#}s so " +
                          $"{hits} exchanges fit — a default round has room for about three.");
                p.liveSeconds = needed;
            }
            p.DebugArmParries(GooseDefence.Tier.Good, hits);
            Debug.Log($"[Duck] armed {hits} GOOD parries. Watch the boom, the lens and the music layer " +
                      "wind up as the dive speed climbs.");
        }

        [MenuItem("Duck/Play · Rally: force a miss", priority = 44)]
        public static void ForceMiss()
        {
            var p = Phase;
            if (p == null) return;
            p.DebugClearArming();
            Debug.Log("[Duck] arming cleared — the next goose gets through and flattens beds.");
        }

        static void Arm(GooseDefence.Tier tier, int count)
        {
            var p = Phase;
            if (p == null) return;
            p.DebugArmParries(tier, count);
            Debug.Log($"[Duck] armed {count} {tier} parry. It fires when the goose reaches that " +
                      "radius, or on the frame it would have landed if it never gets that close.");
        }

        // ------------------------------------------------------------------ frame bursts
        //
        // ONE MENU ITEM HAS TO DO THE WHOLE THING, and that is the entire design constraint here.
        //
        // A hit stop is 45-110 ms and the slow-motion beat is 220 ms, while every command the review
        // loop issues is an RPC costing one to three seconds. Arming from one call and capturing from
        // the next cannot land inside a window that short — it is photographing a camera flash by
        // posting letters. So these items start the phase, arm the strike, watch for it from inside the
        // frame loop, and capture the burst themselves. No external timing is involved at any point.
        //
        // Frames are GRABBED TO MEMORY and written afterwards. Encoding a PNG and hitting the disk costs
        // tens of milliseconds, which is most of a hit stop — writing inside the window would have
        // distorted the very thing being measured, and the later frames would have landed late.

        const int ShotWidth = 1600, ShotHeight = 900;

        /// <summary>One captured frame, held until the burst is over.</summary>
        struct Grab
        {
            public string label;
            public Texture2D tex;
        }

        [MenuItem("Duck/Capture · Rally: PERFECT parry burst", priority = 45)]
        public static void BurstPerfect() => RunWhenPlaying(() => StartBurst(GooseDefence.Tier.Perfect));

        [MenuItem("Duck/Capture · Rally: GOOD parry burst", priority = 46)]
        public static void BurstGood() => RunWhenPlaying(() => StartBurst(GooseDefence.Tier.Good));

        [MenuItem("Duck/Capture · Rally: NORMAL parry burst", priority = 47)]
        public static void BurstNormal() => RunWhenPlaying(() => StartBurst(GooseDefence.Tier.Normal));

        static void StartBurst(GooseDefence.Tier tier)
        {
            // Whichever driver is running. The coroutine is hosted on the phase itself rather than on
            // GameDirector, because in the standalone arena there is no GameDirector to host it — and
            // hosting it on the phase means the capture dies with the thing it is capturing instead of
            // outliving it and photographing an empty pitch.
            var p = Phase;
            if (p == null) return;
            p.StartCoroutine(ParryBurst(p, tier));
        }

        /// <summary>
        /// Capture one parry end to end: the approach, the contact, the frozen frame, the slow-motion
        /// beat, the throw and the recovery.
        ///
        /// Every frame is triggered by a state the phase reports rather than by a stopwatch, because the
        /// interesting states are far shorter than a reliable wait. The contact frame is found by watching
        /// StrikeCount change, which cannot be missed: it is checked every frame from inside the loop.
        /// </summary>
        static IEnumerator ParryBurst(GooseDefence p, GooseDefence.Tier tier)
        {
            Time.timeScale = 1f;

            // Only a round needs steering into the phase. The standalone arena is already in it — its
            // bootstrap called Begin in Start — so forcing a state here would restart the raid under the
            // capture and throw away the goose it was about to photograph.
            var d = Director;
            if (d != null)
            {
                d.defencePhaseEnabled = true;
                if (d.State != GameState.Defence)
                {
                    d.autopilotEnabled = false;
                    Pilot?.Stop();
                    d.DebugForceState(GameState.Defence);
                    p = d.defence != null ? d.defence : p;
                }
            }

            if (p == null) { Debug.LogWarning("[Duck] no defence phase to capture."); yield break; }

            // Wait for a goose to actually be in the air before arming, so the arm cannot be spent on
            // the brief beat.
            float guard = 0f;
            while (p.GooseRange < 0f && guard < 12f) { guard += Time.unscaledDeltaTime; yield return null; }
            if (p.GooseRange < 0f) { Debug.LogWarning("[Duck] no goose arrived; nothing captured."); yield break; }

            p.DebugArmParries(tier, 1);

            var shots = new List<Grab>(8);
            int before = p.StrikeCount;

            // Approach: the first frame the bird is inside three times the widest tier radius, which is
            // roughly where it becomes the thing you are looking at.
            float approachAt = p.parryRadius * 3f;
            guard = 0f;
            while (p.StrikeCount == before && guard < 12f)
            {
                float r = p.GooseRange;
                if (r >= 0f && r <= approachAt) break;
                guard += Time.unscaledDeltaTime;
                yield return null;
            }
            if (p.StrikeCount == before) shots.Add(Snap("1_approach"));

            // Contact.
            guard = 0f;
            while (p.StrikeCount == before && guard < 12f) { guard += Time.unscaledDeltaTime; yield return null; }
            if (p.StrikeCount == before)
            {
                Debug.LogWarning("[Duck] the armed parry never fired; nothing captured.");
                foreach (var s in shots) if (s.tex != null) Object.DestroyImmediate(s.tex);
                yield break;
            }
            shots.Add(Snap($"2_contact_{p.LastStrikeTier}"));

            // Inside the freeze. Grabbed on the very next frame, because the hit stop is measured in
            // unscaled time and the grab itself costs some of it.
            yield return null;
            if (p.HitStopActive) shots.Add(Snap("3_hitstop"));

            // Inside the slow-motion beat, which only a perfect strike has.
            guard = 0f;
            while (p.HitStopActive && guard < 0.5f) { guard += Time.unscaledDeltaTime; yield return null; }
            if (p.SlowMotionActive) shots.Add(Snap("4_slowmotion"));

            yield return WaitReal(0.20f);
            shots.Add(Snap("5_launch"));

            yield return WaitReal(0.35f);
            shots.Add(Snap("6_arc"));

            yield return WaitReal(0.55f);
            shots.Add(Snap("7_recover"));

            WriteAll(shots, $"parry_{tier}".ToLower());
        }

        /// <summary>
        /// Capture a whole rally: one frame at every contact, so the escalation is visible as a sequence.
        /// A single frame cannot show a rally winding up, which is the whole reason this exists.
        /// </summary>
        /// <summary>
        /// Capture a MISS end to end: the goose coming down, the moment it lands in the flowers, and the
        /// recovery.
        ///
        /// A separate tool because ParryBurst watches StrikeCount, which by definition never moves on a
        /// miss — so the entire failure half of this phase had no capture path at all, and "misses must
        /// also feel physical and funny" was the one requirement nothing could even look at. The trigger
        /// is Result.crashes, which is already exposed and cannot be missed if it is checked every frame.
        /// </summary>
        /// <summary>
        /// Twelve CONSECUTIVE frames from the moment of contact.
        ///
        /// Exists because I called a tool limitation absolute and was wrong. The parry burst samples at
        /// fixed intervals — approach, contact, hit stop, slow motion, launch, arc, recover — and a camera
        /// punch, an FOV kick, a suspension recoil and a steering kick all live and die inside 70-110 ms.
        /// At 60 fps that is four to seven frames, so every one of them fell BETWEEN samples and I
        /// reported them as unprovable in stills. They are perfectly provable in stills; they just need
        /// stills taken every frame instead of every quarter second.
        ///
        /// One frame per Update, no waiting, so the sequence is the frames the game actually rendered.
        /// Names carry the field of view and the mower's pitch and roll, which is what makes the punch and
        /// the recoil readable as NUMBERS as well as pictures — a two-degree FOV kick is hard to see and
        /// impossible to misread when it is in the filename.
        /// </summary>
        [MenuItem("Duck/Capture · Rally: contact micro-burst (12 frames)", priority = 50)]
        public static void BurstMicro() => RunWhenPlaying(() =>
        {
            var p = Phase;
            if (p == null) return;
            p.StartCoroutine(MicroBurst(p));
        });

        static IEnumerator MicroBurst(GooseDefence p)
        {
            Time.timeScale = 1f;

            float guard = 0f;
            while (p.GooseRange < 0f && guard < 14f) { guard += Time.unscaledDeltaTime; yield return null; }

            p.DebugArmParries(GooseDefence.Tier.Perfect, 1);

            int before = p.StrikeCount;
            guard = 0f;
            while (p.StrikeCount == before && guard < 14f) { guard += Time.unscaledDeltaTime; yield return null; }
            if (p.StrikeCount == before)
            {
                Debug.LogWarning("[Duck] the armed parry never fired; nothing captured.");
                yield break;
            }

            var cam = Camera.main;
            var mower = Object.FindFirstObjectByType<MowerController>();
            var shots = new List<Grab>(12);

            for (int i = 0; i < 12; i++)
            {
                float fov = cam != null ? cam.fieldOfView : 0f;
                Vector3 e = mower != null ? mower.transform.eulerAngles : Vector3.zero;
                float pitch = Mathf.DeltaAngle(0f, e.x);
                float roll = Mathf.DeltaAngle(0f, e.z);
                shots.Add(Snap($"{i:00}_fov{fov:0.0}_pitch{pitch:+0.0;-0.0}_roll{roll:+0.0;-0.0}"));
                yield return null;
            }

            WriteAll(shots, "micro");
        }

        [MenuItem("Duck/Capture · Rally: MISS burst", priority = 49)]
        public static void BurstMiss() => RunWhenPlaying(() =>
        {
            var p = Phase;
            if (p == null) return;
            p.StartCoroutine(MissBurst(p));
        });

        static IEnumerator MissBurst(GooseDefence p)
        {
            Time.timeScale = 1f;
            // Disarm, so the next goose is guaranteed to get through rather than being met.
            p.DebugClearArming();

            var shots = new List<Grab>(5);
            int before = p.Result.crashes;

            // Inbound, framed once it is close enough to be the thing you are looking at.
            float guard = 0f;
            while (p.Result.crashes == before && guard < 14f)
            {
                float r = p.GooseRange;
                if (r >= 0f && r <= p.parryRadius * 3f) break;
                guard += Time.unscaledDeltaTime;
                yield return null;
            }
            if (p.Result.crashes == before) shots.Add(Snap("1_inbound"));

            // The landing.
            guard = 0f;
            while (p.Result.crashes == before && guard < 14f) { guard += Time.unscaledDeltaTime; yield return null; }
            if (p.Result.crashes == before)
            {
                Debug.LogWarning("[Duck] no goose came down; nothing captured.");
                foreach (var s in shots) if (s.tex != null) Object.DestroyImmediate(s.tex);
                yield break;
            }
            shots.Add(Snap("2_impact"));

            yield return null;
            if (p.HitStopActive) shots.Add(Snap("3_hitstop"));

            yield return WaitReal(0.25f);
            shots.Add(Snap("4_debris"));

            yield return WaitReal(0.8f);
            shots.Add(Snap("5_aftermath"));

            WriteAll(shots, "miss");
        }

        [MenuItem("Duck/Capture · Rally: 5-hit escalation burst", priority = 48)]
        public static void BurstRally() => RunWhenPlaying(() =>
        {
            var p = Phase;
            if (p == null) return;
            p.StartCoroutine(RallyBurst(p));
        });

        /// <summary>
        /// Five exchanges in a row, one frame at each contact.
        ///
        /// This required a GameDirector, and the STANDALONE ARENA HAS NONE — so it bailed out on a null
        /// Director and logged "Enter play mode first", which is not what was wrong. Five capture
        /// attempts were spent chasing a play-mode race that was never the cause; the misleading message
        /// was. It now takes the phase directly, exactly as the other bursts do.
        /// </summary>
        static IEnumerator RallyBurst(GooseDefence p)
        {
            Time.timeScale = 1f;

            // Only a round has to be steered into the phase. The arena is already in one.
            var d = Director;
            if (d != null)
            {
                d.defencePhaseEnabled = true;
                if (d.State != GameState.Defence) d.DebugForceState(GameState.Defence);
                else if (d.defence != null) d.defence.Begin();
                if (d.defence != null) p = d.defence;
            }
            else
            {
                p.Begin();
            }

            if (p == null) { Debug.LogWarning("[Duck] no defence phase to capture."); yield break; }

            const int hits = 5;
            // Widened, and it is a finding rather than a workaround: one exchange costs three to four
            // seconds at the tuned dive speeds, so a default twelve-second phase holds about three of
            // them. Nobody should read this capture as evidence that a normal round rallies five times.
            if (p.liveSeconds < 19f) p.liveSeconds = 19f;

            float guard = 0f;
            while (p.GooseRange < 0f && guard < 12f) { guard += Time.unscaledDeltaTime; yield return null; }

            p.DebugArmParries(GooseDefence.Tier.Good, hits);

            var shots = new List<Grab>(hits + 2);
            int seen = p.StrikeCount;

            for (int i = 0; i < hits; i++)
            {
                guard = 0f;
                while (p.StrikeCount == seen && guard < 14f) { guard += Time.unscaledDeltaTime; yield return null; }
                if (p.StrikeCount == seen) break;
                seen = p.StrikeCount;
                // One frame after contact, so the punch and the freeze are on the frame rather than the
                // instant before them.
                yield return null;
                shots.Add(Snap($"{i + 1}_rally_x{p.Rally}_dive{p.CurrentDiveSpeed():0}"));
            }

            // The award, with the surviving and flattened beds on the ground beside the verdict.
            guard = 0f;
            while (p.State != GooseDefence.Phase.Awarding && guard < 14f)
            { guard += Time.unscaledDeltaTime; yield return null; }
            yield return WaitReal(0.6f);
            shots.Add(Snap("9_award"));

            WriteAll(shots, "rally_escalation");
        }

        static IEnumerator WaitReal(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
        }

        /// <summary>
        /// Render the chase camera into memory. Deliberately not writing to disk — see the block comment
        /// above. Also releases its render texture, which the original single-frame capture does not.
        /// </summary>
        static Grab Snap(string label)
        {
            var cam = Camera.main;
            if (cam == null) return new Grab { label = label, tex = null };

            var rt = new RenderTexture(ShotWidth, ShotHeight, 24,
                                       RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { antiAliasing = 1 };

            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;

            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
            tex.Apply(false);

            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);

            return new Grab { label = label, tex = tex };
        }

        static void WriteAll(List<Grab> shots, string prefix)
        {
            Directory.CreateDirectory(CaptureDir);
            string stamp = System.DateTime.Now.ToString("HHmmss");
            int written = 0;

            foreach (var s in shots)
            {
                if (s.tex == null) continue;
                string name = $"{prefix}_{stamp}_{s.label}.png";
                File.WriteAllBytes(Path.Combine(CaptureDir, name), s.tex.EncodeToPNG());
                Object.DestroyImmediate(s.tex);
                written++;
            }
            Debug.Log($"[Duck] wrote {written} frames to {CaptureDir}/{prefix}_{stamp}_*.png");
        }
    }
}
