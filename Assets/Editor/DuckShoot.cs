using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Headless frame sheet: opens the scene, enters play mode, runs a whole round on the scripted
    /// clock and writes a PNG per beat — all from one batch-mode invocation.
    ///
    ///   Unity.exe -batchmode -projectPath C:\Duck ^
    ///             -executeMethod DuckMow.EditorTools.DuckShoot.Frames ^
    ///             -logFile C:\Duck\Logs\shoot.log
    ///
    /// This exists because the only way to see a change was a full WebGL package plus a two minute
    /// browser capture, which is minutes of wall clock for what is usually a two-line UI tweak.
    /// A round simulates in a couple of seconds, so this turns the iteration loop from "package and
    /// wait" into "look at the frames". The WebGL build is still the thing that ships and still gets
    /// checked, just not on every edit.
    ///
    /// Note there is no -quit: play mode has to be entered and pumped, so the method exits the
    /// editor itself once the sheet is written.
    /// </summary>
    public static class DuckShoot
    {
        const string ScenePath = DuckSceneBuilder.ScenePath;
        const string Marker = "Duck.Shoot.Pending";

        /// <summary>Start poses for every picture, plus the numeric audit. Headless.</summary>
        public static void Starts()
        {
            _mode = Mode.Starts;
            Frames();
        }

        /// <summary>Judging and verdict only — the cheap loop for anything after the klaxon.</summary>
        public static void Judging()
        {
            _mode = Mode.Judging;
            Frames();
        }

        enum Mode { Full, Judging, Starts }
        static Mode _mode = Mode.Full;

        public static void Frames()
        {
            Debug.Log("[Duck] ===== SHOOT START =====");

            // Domain reload on entering play mode would throw away the fact that we are mid-shoot.
            // Everything in the game resolves its singletons lazily precisely so this is safe.
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(Marker, true);

            EditorApplication.update += Pump;
            EditorApplication.EnterPlaymode();
        }

        static int _framesWaited;

        static void Pump()
        {
            if (!SessionState.GetBool(Marker, false)) { EditorApplication.update -= Pump; return; }
            if (!EditorApplication.isPlaying) return;

            // Give the scene a handful of real frames so every Awake and Start has run and the
            // grass chunks have had a chance to build before anything is photographed.
            if (++_framesWaited < 12) return;

            EditorApplication.update -= Pump;
            SessionState.SetBool(Marker, false);

            int code = 0;
            try
            {
                switch (_mode)
                {
                    case Mode.Judging: DuckSimulator.CaptureJudging(); break;
                    case Mode.Starts:
                        DuckMeshAudit.AuditStartPoses();
                        DuckSimulator.CaptureStarts();
                        break;
                    default: DuckSimulator.RunRound(ShapeId.Duckling, true, "sheet"); break;
                }
                Debug.Log("[Duck] ===== SHOOT DONE =====");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Duck] Shoot failed: {e}");
                code = 1;
            }

            EditorApplication.Exit(code);
        }

        /// <summary>
        /// Builds the UI into the open scene and photographs it with the round faked out, without
        /// entering play mode at all. Canvases render in edit mode, so a UI-only change can be
        /// checked in a couple of seconds.
        ///
        ///   Unity.exe -batchmode -quit -projectPath C:\Duck ^
        ///             -executeMethod DuckMow.EditorTools.DuckShoot.UiOnly
        /// </summary>
        /// <summary>
        /// A survey of the whole championship ground from the air, plus the view from each plot's
        /// station. Runs in edit mode, so composition can be judged without simulating a round.
        /// </summary>
        [MenuItem("Duck/Sim · Capture venue survey", priority = 14)]
        public static void VenueSurvey()
        {
            if (!EditorApplication.isPlaying &&
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var cam = Camera.main;
            if (cam == null) { Debug.LogError("[Duck] No main camera."); return; }

            var hud = Object.FindFirstObjectByType<HUD>();
            if (hud != null)
            {
                // The survey is about the ground, not the interface.
                if (hud.resultsGroup != null) hud.resultsGroup.alpha = 0f;
                if (hud.bannerGroup != null) hud.bannerGroup.alpha = 0f;
                if (hud.roundGroup != null) hud.roundGroup.alpha = 0f;
            }

            Vector3 quadCentre = new Vector3(Venue.Spacing * 0.5f, 0f, Venue.Spacing * 0.5f);

            Frame(cam, quadCentre + new Vector3(0f, 230f, -150f), quadCentre, 46f);
            DuckSimulator.Shot(cam, "venue_01_overview", 1280, 720);

            Frame(cam, quadCentre + new Vector3(-130f, 96f, -170f), quadCentre + new Vector3(0f, 4f, 0f), 44f);
            DuckSimulator.Shot(cam, "venue_02_oblique", 1280, 720);

            // What the player actually sees of their neighbours while mowing: eye level, on their
            // own lawn, looking across the venue.
            Frame(cam, new Vector3(6f, 2.6f, -6f), new Vector3(Venue.Spacing * 0.55f, 3f, Venue.Spacing * 0.30f), 52f);
            DuckSimulator.Shot(cam, "venue_03_from_player", 1280, 720);

            var board = Object.FindFirstObjectByType<Scoreboard>();
            if (board != null)
            {
                Vector3 b = board.transform.position;
                Frame(cam, b - board.transform.forward * 16f + Vector3.up * 7.2f, b + Vector3.up * 5.6f, 36f);
                DuckSimulator.Shot(cam, "venue_04_scoreboard", 1280, 720);
            }

            for (int i = 1; i < Venue.Plots.Length; i++)
            {
                var spec = Venue.Plots[i];
                Vector3 c = new Vector3(spec.centre.x, 0f, spec.centre.y);
                Frame(cam, c + new Vector3(0f, 46f, -22f), c, 44f);
                DuckSimulator.Shot(cam, $"venue_0{4 + i}_plot_{spec.contestant}", 1280, 720);
            }

            // Close on one rival's station and their mower, to check the authored characters are
            // standing up and seated where they belong.
            var horace = Venue.Plots[1];
            Vector3 st = horace.StationPosition;
            Frame(cam, st + new Vector3(0.6f, 2.3f, 7.5f), st + new Vector3(0f, 1.35f, 0f), 34f);
            DuckSimulator.Shot(cam, "venue_08_station_close", 1280, 720);

            var rival = Object.FindFirstObjectByType<RivalContestant>();
            if (rival != null && rival.mowerVisual != null)
            {
                Vector3 m = rival.mowerVisual.position;
                Frame(cam, m + new Vector3(3.2f, 2.0f, 3.6f), m + Vector3.up * 0.6f, 32f);
                DuckSimulator.Shot(cam, "venue_09_rival_close", 1280, 720);
            }

            // Close on the player's own stand, to check every spectator is actually on seating.
            Frame(cam, new Vector3(28f, 5.2f, 6f), new Vector3(44f, 2.6f, 0f), 40f);
            DuckSimulator.Shot(cam, "venue_11_stand_close", 1280, 720);

            var millGO = GameObject.Find("Windmill");
            if (millGO != null)
            {
                Vector3 w = millGO.transform.position;
                Frame(cam, w + new Vector3(-2f, 9f, 34f), w + Vector3.up * 8f, 40f);
                DuckSimulator.Shot(cam, "venue_10_windmill", 1280, 720);
            }

            Debug.Log("[Duck] Venue survey written to Captures/.");
        }

        static void Frame(Camera cam, Vector3 pos, Vector3 lookAt, float fov)
        {
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation((lookAt - pos).normalized, Vector3.up));
            cam.fieldOfView = fov;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 900f);
        }

        [MenuItem("Duck/Sim · Capture UI only (no play mode)", priority = 13)]
        public static void UiOnly()
        {
            if (!EditorApplication.isPlaying &&
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var hud = Object.FindFirstObjectByType<HUD>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogError($"[Duck] No HUD in scene '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().path}'.");
                return;
            }

            var cam = Camera.main;
            if (cam == null) { Debug.LogError("[Duck] No main camera."); return; }

            // Longest plausible strings, so the capture shows the worst case rather than the
            // convenient one. Every overflow bug so far has only appeared on the long quip.
            string[] quips =
            {
                "It is a shape. I will grant you that much.",
                "Loads of grass cut! Loads! Marvellous stuff, genuinely!",
                "The outline and I are no longer speaking."
            };
            for (int i = 0; i < 3; i++)
            {
                if (hud.judgeScores[i] != null) hud.judgeScores[i].text = (i + 5).ToString();
                if (hud.judgeQuips[i] != null) hud.judgeQuips[i].text = quips[i];
            }
            if (hud.resultsRank != null) hud.resultsRank.text = "C";
            if (hud.resultsTotal != null) hud.resultsTotal.text = "16 / 30";
            if (hud.coverageStat != null) hud.coverageStat.text = "65%";
            if (hud.spillStat != null) hud.spillStat.text = "3%";
            if (hud.edgeStat != null) hud.edgeStat.text = "65%";
            if (hud.styleStat != null) hud.styleStat.text = "0%";
            // Kept in step with HUD.cs by hand, because this rig fakes the results screen rather
            // than reaching one — it sets the stats and the hint directly so a sheet can be shot
            // without playing a round. It was still advertising [N] NEW PICTURE long after that
            // feature and its key were removed, which is the failure mode of a mock: it goes on
            // confidently describing a game that has changed underneath it, and the frame sheet
            // looks authoritative while being wrong. This is the three-way branch's widest arm,
            // from HUD.cs:433 — the one with a retry and no venue tour waiting.
            if (hud.retryHint != null)
                hud.retryHint.text = "[R]  RETRY SAME PICTURE     [SPACE]  MAIN MENU     [ESC]  PAUSE";
            if (hud.resultsGroup != null) hud.resultsGroup.alpha = 1f;

            Canvas.ForceUpdateCanvases();
            DuckSimulator.Shot(cam, "ui_results", 1280, 720);

            // Then the banner on its own, with the two lines that kept escaping the ribbon.
            if (hud.resultsGroup != null) hud.resultsGroup.alpha = 0f;
            if (hud.bannerGroup != null) hud.bannerGroup.alpha = 1f;
            if (hud.bannerTitle != null) hud.bannerTitle.text = "PENCILS DOWN";
            if (hud.bannerSubtitle != null) hud.bannerSubtitle.text = "TIME!";
            Canvas.ForceUpdateCanvases();
            DuckSimulator.Shot(cam, "ui_banner_time", 1280, 720);

            if (hud.bannerTitle != null) hud.bannerTitle.text = "SCORING";
            if (hud.bannerSubtitle != null) hud.bannerSubtitle.text = "THE VERDICT AWAITS";
            Canvas.ForceUpdateCanvases();
            DuckSimulator.Shot(cam, "ui_banner_verdict", 1280, 720);

            Debug.Log("[Duck] UI frames written to Captures/.");
        }
    }
}
