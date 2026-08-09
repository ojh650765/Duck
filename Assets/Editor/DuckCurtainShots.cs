using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Frame sheets for the transitions.
    ///
    /// These capture through <see cref="ScreenCapture"/> rather than through a camera and a
    /// RenderTexture, which is what every other sheet in this project uses — and the difference is
    /// not a preference. The curtain is a SCREEN SPACE OVERLAY canvas, and a camera render does not
    /// include one. A sheet shot the usual way would show the transition working perfectly and be
    /// photographing the scene underneath it.
    ///
    /// The whole point of the system under test is that a boundary has no bad frame in it, so the
    /// review has to be frame-by-frame: the sheets step the curtain in small increments and catch
    /// the two moments that matter most — the last frame before the cut and the first frame after.
    /// </summary>
    public static class DuckCurtainShots
    {
        const string Dir = "Captures/Curtain";

        /// <summary>Multiplier on the Game View's own pixel size. See Shoot.</summary>
        const int SuperSize = 8;

        [MenuItem("Duck/Curtain · Style contact sheet (play mode)", priority = 60)]
        public static void StyleSheet()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Curtain] enter play mode first — the curtain only exists at runtime.");
                return;
            }
            var curtain = Curtain.Ensure();
            curtain.StartCoroutine(StyleRoutine(curtain));
        }

        static IEnumerator StyleRoutine(Curtain curtain)
        {
            Directory.CreateDirectory(Dir);
            string stamp = System.DateTime.Now.ToString("HHmmss");

            var styles = new[]
            {
                CurtainStyle.LeafSweep, CurtainStyle.GrassRise, CurtainStyle.BannerWipe,
                CurtainStyle.PetalCurtain, CurtainStyle.Fanfare
            };
            // The sign each style ACTUALLY carries in play, read off the seam that selects it in
            // MatchState.StyleFor and the strings passed at that seam's call site — not invented for
            // the sheet. A review harness that dresses the frame in wording the game never shows is
            // reviewing something else: this sheet was still captioning two of its five frames
            // "THE CHAMPIONSHIP", one of them "THE SASH IS AWARDED", for a championship that was
            // renamed and a ceremony that was cut. The sheet looked authoritative and was describing
            // a game that no longer existed.
            // The running order is LAWN ART, then GOOSE RALLY, then BLOOM RUSH last — so the round
            // number on a sign is decided by which STAGE it names, not by which curtain style
            // happens to be loudest. The sheet had GOOSE RALLY over "FINAL ROUND", which is the
            // second round wearing the third's kicker.
            var labels = new (string head, string kick)[]
            {
                ("LAWN ART",     StageSeam.RoundKicker(1)),
                // MenuToRound is wordless and always was — the grass rising already says a round is
                // starting. Kept in the sheet as a null pair on purpose: it is the only frame that
                // exercises Curtain's no-sign path, where _signWanted goes false and the plate is
                // never positioned or drawn.
                (null,           null),
                ("GOOSE RALLY",  StageSeam.RoundKicker(2)),
                ("BLOOM RUSH",   StageSeam.RoundKicker(3)),
                // Fanfare is OFF THE LIVE ROUTE. It was the IntoFinale seam, and that boundary no
                // longer runs a curtain at all — the scoreboard goes straight to the ending page.
                // Shot anyway, and wordless, because the style is still selectable and a style that
                // no tool ever photographs is a style that quietly rots. Not captioned "HOW IT
                // ENDED" any more: that sign does not exist, and a sheet that shows it would be
                // describing the game as it was for the third time in this file's life.
                (null,           null)
            };

            for (int i = 0; i < styles.Length; i++)
            {
                float intensity = i / (float)(styles.Length - 1);
                var (head, kick) = labels[i];

                // Not yielded on: the close has to be SAMPLED while it runs, and yielding on it
                // would hand back only once it had finished and there was nothing left to look at.
                curtain.StartCoroutine(curtain.Close(styles[i], intensity, head, kick));

                // Sampled by COVERAGE, not by stopwatch.
                //
                // A supersized capture renders the frame at sixty-four times the pixels, which drops
                // the editor to a handful of frames a second — and the curtain quite correctly
                // clamps its own delta so a hitch cannot teleport it. Together that means a shot
                // taken "0.9 seconds in" during a capture run is a fifth of the way through the
                // animation, and the sheet reports a transparent frame at the exact moment it is
                // supposed to be proving the frame is opaque. Which it did, on the first run.
                yield return ShootAt(curtain, $"{stamp}_{i}{styles[i]}_a_25pc", 0.25f, true);
                yield return ShootAt(curtain, $"{stamp}_{i}{styles[i]}_b_55pc", 0.55f, true);
                yield return ShootAt(curtain, $"{stamp}_{i}{styles[i]}_c_85pc", 0.85f, true);
                // The frame the scene would change on. This one has to be completely opaque, and it
                // is the single most important frame in the sheet.
                yield return ShootAt(curtain, $"{stamp}_{i}{styles[i]}_d_covered", 1f, true);
                yield return Shoot($"{stamp}_{i}{styles[i]}_e_sign", 0.45f);

                curtain.StartCoroutine(curtain.Open());
                yield return ShootAt(curtain, $"{stamp}_{i}{styles[i]}_f_75pc", 0.75f, false);
                yield return ShootAt(curtain, $"{stamp}_{i}{styles[i]}_g_35pc", 0.35f, false);
                yield return ShootAt(curtain, $"{stamp}_{i}{styles[i]}_h_clear", 0.02f, false);
            }

            Debug.Log($"[Curtain] style sheet {stamp} written to {Dir}/.");
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Wait, then photograph what is actually on the display, overlay canvases and all.
        ///
        /// The wait is on the UNSCALED clock because a transition is, and because these sheets are
        /// most useful in exactly the states where the game has warped the clock.
        /// </summary>
        /// <summary>
        /// Wait until the curtain has reached a given coverage, then photograph it.
        ///
        /// <paramref name="rising"/> says which way it is expected to be moving, so the same call
        /// works for the close and for the open. Budgeted, because a curtain that never reaches the
        /// asked-for coverage is a bug worth a sheet that says so rather than a hung editor.
        /// </summary>
        static IEnumerator ShootAt(Curtain curtain, string name, float amount, bool rising)
        {
            float guard = 0f;
            while (guard < 8f)
            {
                if (rising ? curtain.Amount >= amount - 0.001f : curtain.Amount <= amount + 0.001f)
                    break;
                guard += Time.unscaledDeltaTime;
                yield return null;
            }
            if (guard >= 8f)
                Debug.LogWarning($"[Curtain] {name}: never reached {amount:0.00} " +
                                 $"(stuck at {curtain.Amount:0.00}).");
            yield return Shoot(name, 0f);
        }

        static IEnumerator Shoot(string name, float delay)
        {
            float t = 0f;
            while (t < delay) { t += Time.unscaledDeltaTime; yield return null; }

            yield return new WaitForEndOfFrame();
            // Supersized, because the capture inherits the Game View's pixel size and a docked Game
            // View in a working editor layout is routinely a couple of hundred pixels across. A
            // frame sheet meant to be inspected for four-pixel gaps at the screen edge is worthless
            // at that size, and the alternative — driving the Game View's resolution from script —
            // is a different fight in every Unity version.
            var tex = ScreenCapture.CaptureScreenshotAsTexture(SuperSize);
            if (tex == null) yield break;

            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>
        /// Write the curtain's procedural atlas out as a PNG, so the four shapes can be looked at.
        ///
        /// Worth a menu item because the alternative is inferring a shape from a field of three
        /// hundred rotated, overlapping, tinted copies of it — which is how a blade of grass drawn
        /// upside down survives a review: in a mass, at speed, it just reads as "grass".
        /// </summary>
        [MenuItem("Duck/Curtain · Dump the shape atlas", priority = 62)]
        public static void DumpAtlas()
        {
            Directory.CreateDirectory(Dir);

            // The atlas is built non-readable so the runtime copy cannot be encoded directly. Blit
            // it through a RenderTexture, which is the one route that works for any texture.
            var src = new GameObject("~ atlas probe", typeof(RectTransform))
                { hideFlags = HideFlags.HideAndDontSave };
            var field = src.AddComponent<CurtainField>();
            var tex = field.mainTexture;

            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var flat = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            flat.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            flat.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            // Over a mid grey, because the shapes are white-ish with an alpha cut-out and a PNG of
            // them on transparency is unreadable in most viewers.
            var px = flat.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                float a = px[i].a / 255f;
                byte g = (byte)Mathf.RoundToInt(Mathf.Lerp(90f, px[i].r, a));
                px[i] = new Color32(g, g, g, 255);
            }
            flat.SetPixels32(px);
            flat.Apply();

            string path = Path.Combine(Dir, "atlas.png");
            File.WriteAllBytes(path, flat.EncodeToPNG());
            Object.DestroyImmediate(flat);
            Object.DestroyImmediate(src);

            Debug.Log($"[Curtain] shape atlas written to {path}. Tiles read bottom-left leaf, " +
                      "bottom-right petal, top-left blade, top-right cloth.");
            AssetDatabase.Refresh();
        }

        // ------------------------------------------------------------------ the whole journey

        /// <summary>
        /// Every seam in one run, sampled hard around each cut.
        ///
        /// Started from the front page so the sheet includes the boundary the player meets first,
        /// which is also the one most likely to be slow — a cold browser cache decompressing the
        /// venue is the worst load in the game and the transition over it is the one that has to
        /// work hardest.
        /// </summary>
        [MenuItem("Duck/Curtain · Journey frame sheet (play mode)", priority = 61)]
        public static void JourneySheet()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Curtain] enter play mode first.");
                return;
            }
            var curtain = Curtain.Ensure();
            curtain.StartCoroutine(JourneyRoutine(curtain));
        }


        /// <summary>Multiplier for journey frames. Lower than the style sheet's — a run this long
        /// cannot afford to render every transition frame at sixty-four times the pixels.</summary>
        const int JourneySuper = 6;

        static IEnumerator JourneyRoutine(Curtain curtain)
        {
            Directory.CreateDirectory(Dir);
            string stamp = System.DateTime.Now.ToString("HHmmss");
            int frame = 0;
            bool wasBusy = false;
            bool prepped = false;
            int faults = 0;
            var timeline = new System.Text.StringBuilder();
            GameState? lastState = null;
            string lastScene = null;

            // Long enough for three shortened rounds with two arenas in them, short enough that an
            // unattended run cannot fill a disk.
            const float Budget = 420f;
            float t = 0f;

            while (t < Budget)
            {
                t += Time.unscaledDeltaTime;

                // ---- drive ----------------------------------------------------------------
                //
                // The point of a JOURNEY sheet is that nothing about it is staged: the game plays
                // itself through its own state machine, across its own scene loads, and the sheet
                // photographs whatever that actually looks like. So this presses buttons and
                // shortens rounds, and touches nothing else.
                var menu = Object.FindFirstObjectByType<MainMenu>();
                if (menu != null && t > 1.5f) menu.DebugPlayNow();

                var d = GameDirector.Instance;
                if (d != null)
                {
                    if (!prepped)
                    {
                        prepped = true;
                        // The opening story is half a minute of comic panels with no seam in them.
                        d.SkipIntro();
                        d.autopilotEnabled = true;
                        // Shortens the ARENAS too, not just the lawn: TurfBootstrap takes Bloom
                        // Rush's match length off this field so a session's stages all run to one
                        // clock. Left alone, a single Bloom match is seventy-five seconds and a
                        // three-round run does not fit in any sane capture budget.
                        d.roundDuration = 16f;
                    }

                    // Rounds cut to a few seconds. The mowing is not what is under test; the six
                    // boundaries either side of it are, and at full length this run would take a
                    // quarter of an hour.
                    if (d.State == GameState.Mowing && d.TimeRemaining > 5f)
                        d.DebugSetTimeRemaining(5f);

                    // Press on wherever the game waits for a human. Everything else in the running
                    // order advances on its own budget.
                    bool waiting = d.State == GameState.Scoreboard || d.State == GameState.Ceremony
                                || d.State == GameState.Ending || d.State == GameState.Reveal
                                || d.State == GameState.Verdict;
                    if (waiting && d.StateTime > 3.2f)
                        InputReader.Instance?.PulseButtons(false, false, true, false, false);

                    if (lastState != d.State)
                    {
                        lastState = d.State;
                        timeline.AppendLine($"{t,7:0.0}s  state {d.State}");
                    }
                }

                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (scene != lastScene)
                {
                    lastScene = scene;
                    timeline.AppendLine($"{t,7:0.0}s  SCENE -> {scene}   curtain " +
                                        (curtain.Covered ? "DOWN" : $"{curtain.Amount:0.00}"));
                    // The line that matters most in the whole log. The active scene changing with
                    // the frame anything other than fully covered is a visible seam by definition,
                    // and it is the one fault this system exists to make impossible.
                    if (!curtain.Covered)
                    {
                        faults++;
                        timeline.AppendLine("           ^^^ SCENE CHANGED WITH THE FRAME UNCOVERED");
                    }
                }

                // ---- capture --------------------------------------------------------------
                //
                // Every frame of every transition, and one either side of it. A sheet that samples
                // on a timer photographs the middle of transitions and misses both edges, which are
                // the only frames where a fault can actually show.
                bool busy = curtain.Busy;
                if (busy || wasBusy)
                {
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture(JourneySuper);
                    if (tex != null)
                    {
                        File.WriteAllBytes(Path.Combine(Dir,
                            $"{stamp}_j{frame:000}_a{curtain.Amount:0.00}.png"), tex.EncodeToPNG());
                        Object.DestroyImmediate(tex);
                        frame++;
                    }
                }
                wasBusy = busy;
                yield return null;
            }

            timeline.AppendLine();
            timeline.AppendLine(faults == 0
                ? "No scene change happened on an uncovered frame."
                : $"{faults} scene change(s) happened on an uncovered frame.");

            File.WriteAllText(Path.Combine(Dir, $"{stamp}_timeline.txt"), timeline.ToString());
            Debug.Log($"[Curtain] journey {stamp}: {frame} transition frames, {faults} uncovered " +
                      $"scene change(s). Timeline in {Dir}/.");
            AssetDatabase.Refresh();
        }
    }
}
