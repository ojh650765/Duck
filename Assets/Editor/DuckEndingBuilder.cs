using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Builds the two ENDING pages — the one where the duck keeps the pond and the one where he
    /// does not — in the same scrapbook idiom as the opening story.
    ///
    /// Both are built every time, and both are wired, because which one plays is not a decision this
    /// builder is allowed to make: GameDirector.BeginEnding picks on the championship total, at the
    /// end of a session nobody is going to re-run the builder in the middle of. A page that only
    /// exists for the outcome the last editor session happened to be testing is a page that is
    /// missing for half the players.
    ///
    /// WHY THIS IS NOT DuckCutsceneBuilder WITH A PARAMETER
    ///
    /// The obvious move was to generalise the opening's builder over "a table of beats" and call it
    /// three times. It was not taken, and the reason is the page layout rather than the table. The
    /// opening's whole compositional argument is TEN cards accumulating into two chronological
    /// columns down the margins — the slot maths, the hero's clearance, the modest ColumnX that
    /// survives a squarer window, all of it is sized for ten. Three cards in that arrangement is one
    /// column of two and a lonely third, which reads as a page somebody abandoned. Three cards want
    /// a STRIP along the foot of the page, which is a different layout with different numbers and a
    /// different reason for existing (see SlotY). Parameterising over the one thing the two pages
    /// genuinely share — the frame, the bars, the band — would have left a "builder" whose body was
    /// an if-statement on which page it was building, and a table this small does not earn that.
    ///
    /// What IS shared is everything that would be a bug if it drifted: the card geometry constants
    /// are re-derived from the same 9-slice border and the same 16:9 window, and every widget comes
    /// from DuckUIBuilder's primitives. If the panel_card art is ever re-cut, both files change.
    ///
    /// The endings are SILENT for now, and that is a decision rather than an omission. The narration
    /// baker renders the opening's table to positional filenames (nar_01_1.wav ... nar_10_2.wav) in
    /// one flat folder; six more lines under that scheme would either collide with the opening's or
    /// require the baker to learn about a second table, which is a change to a tool that currently
    /// cannot render the wrong line. So the ending band is read and not heard — exactly the state
    /// the opening page was in and shipped in for weeks — and ComicPanel.narrationHold is left empty,
    /// which makes each panel's single line hold for the whole beat.
    /// </summary>
    public static class DuckEndingBuilder
    {
        const string PanelDir = "Assets/Art/Textures/Cutscene";
        const string SfxDir = "Assets/Audio/Cutscene";
        const int PanelCount = 3;

        // The card, re-derived from the same two facts as the opening page: the painted frame's
        // 9-slice border is 46 units, and the window inside it has to be 16:9 or a RawImage stretches
        // the render to fill it. These are duplicated rather than exposed from DuckCutsceneBuilder
        // because they are properties of the ART, not of that file — the day panel_card_256 is re-cut
        // both numbers change here too, and a constant borrowed across files hides that.
        const float ArtInset = 46f;
        static readonly Vector2 ArtWindow = new Vector2(880f, 495f);
        static readonly Vector2 CardSize = new Vector2(ArtWindow.x + ArtInset * 2f,
                                                       ArtWindow.y + ArtInset * 2f);

        // The strip. Three settled cards in a row along the foot of the page, left to right in the
        // order they played, which is how a three-panel comic is read and is not how a ten-panel
        // scrapbook is.
        //
        // The band is squeezed between two fixed edges and the numbers are what makes it fit rather
        // than a matter of taste: the hero card's bottom edge sits at 56 - 587/2 = -237, and the
        // bottom letterbox's inner edge is at -540 + 130 = -410. A card at RestScale is 123 units
        // tall, so centring the row on -322 leaves it clear of both by about 25 units. Push RestScale
        // past ~0.24 and the strip starts touching the hero it is supposed to be underneath.
        const float RestScale = 0.21f;
        const float SlotY = -322f;
        const float SlotX = 420f;

        // Same as the opening, and it has to be: the narration band and the skip hint are placed
        // INSIDE the bars, so a builder whose bar height disagreed with the component's would print
        // subtitles half on the paper page.
        const float BarHeight = 130f;

        // The ending page is a third of the opening's length, so the opening's 8 s arming delay would
        // leave a three-panel page skippable only during its final beat — which is the beat the whole
        // page exists for. Six seconds still costs a first-time player the first panel and a half
        // before the prompt appears, which is the point of the delay: nothing is thrown away by a
        // keypress that lands before there is anything on screen saying what a keypress does.
        const float SkipArm = 6f;

        // Longer than the opening's 2.2 s. The opening's composed page hands over to a lawn; this one
        // hands over to nothing — it is the last thing the game shows before the menu, and the three
        // cards sitting there together IS the ending. Cutting off it at the opening's pace reads as
        // the game being in a hurry to be over.
        const float PageHold = 3.2f;
        const float FadeOut = 1.6f;

        static readonly Color Cream = new Color(0.97f, 0.94f, 0.86f);

        // ------------------------------------------------------------------ the tables

        struct Beat
        {
            public string id;
            public float duration;
            public PanelTransition transition;
            public float transitionDuration;
            public float emphasis;
            public string[] narration;
            public string sfxId;
            public string tint;              // palette hex, for the placeholder card
            public string label;             // what the panel shows, for the placeholder card
            public Rect from, to;
        }

        /// <summary>
        /// THE WIN: the money, the pond, and the afternoon that follows.
        ///
        /// Three beats and no more, because a win that is explained stops being a win. The line under
        /// each panel obeys the opening's rule — never say what the picture shows — and two of the
        /// three are deliberately built out of the opening's own sentences:
        ///
        ///   · Panel 1 answers "Nobody had asked the duck what the pond was worth" with a judging
        ///     bench that never asks either. The joke of the whole game is that no one involved knows
        ///     he learned this in a week.
        ///   · Panel 2 says "$10,000" for the third and last time. The opening spent two panels making
        ///     the price and the prize the same number without ever saying they were the same number;
        ///     this is where they finally meet, and it is one line, in the same band, in the same
        ///     wording. Do not paraphrase it — it is the payoff of the sequence's only real trick.
        ///   · Panel 3 is a straight bookend of the opening's first line, "The pond had always been
        ///     there. So had the duck." Same two clauses, same order, past tense held. The player is
        ///     not told they won; they are told nothing changed, which is what winning meant here.
        ///
        /// Crops are square in UV (width == height) for the same reason as the opening's: the art is
        /// 16:9 and the window is 16:9, so any other ratio arrives stretched. If you edit a move, keep
        /// w == h.
        /// </summary>
        static readonly Beat[] WinBeats =
        {
            new Beat
            {
                id = "win_cheque", duration = 4.6f,
                transition = PanelTransition.CrossFade, transitionDuration = 0.9f,
                emphasis = 0.18f, sfxId = "cash_sparkle",
                narration = new[] { "The judges never asked where he had learned it." },
                tint = DuckSceneBuilder.P.Brass, label = "THE PRIZE · THE CHEQUE AT THE BENCH",
                // A push in, ending on the cheque in the duck's wings rather than on the applause.
                // The applause is what the panel opens on; the money is what it is about.
                from = new Rect(0.04f, 0.04f, 0.90f, 0.90f),
                to   = new Rect(0.16f, 0.14f, 0.68f, 0.68f)
            },
            new Beat
            {
                id = "win_pond", duration = 4.4f,
                transition = PanelTransition.CrossFade, transitionDuration = 0.8f,
                emphasis = 0.10f, sfxId = "window_chime",
                narration = new[] { "It cost $10,000. He had exactly that." },
                tint = DuckSceneBuilder.P.Pond, label = "THE POND, BOUGHT · SOLD ACROSS THE SIGN",
                // Backwards, and it is the same shape as the opening's notice panel on purpose:
                // start tight enough that the board can be read, then pull back to find what it is
                // standing in front of. There it revealed an excavator. Here it reveals a pond.
                from = new Rect(0.20f, 0.16f, 0.64f, 0.64f),
                to   = new Rect(0.03f, 0.03f, 0.94f, 0.94f)
            },
            new Beat
            {
                id = "win_life", duration = 6.0f,
                transition = PanelTransition.CrossFade, transitionDuration = 1.1f,
                emphasis = 0f, sfxId = "pond_calm",
                narration = new[] { "The pond was still there. So was the duck." },
                tint = "#E9A75C", label = "GOLDEN HOUR · A LIFE ON THE WATER",
                // A drift, not a move. Nothing is being discovered and nothing is being pointed at;
                // the panel is somebody's afternoon, and the longest hold in either ending. Same size
                // at both ends, sliding across the water — the one camera behaviour in the sequence
                // that has no destination.
                from = new Rect(0.14f, 0.10f, 0.76f, 0.76f),
                to   = new Rect(0.06f, 0.14f, 0.76f, 0.76f)
            }
        };

        /// <summary>
        /// THE LOSS: the sign, the works, and the duck who stays anyway.
        ///
        /// Melancholy, not punishment. Nothing in these three panels blames the player and nothing is
        /// cruel to the duck: no ruined machine, no jeering judges, no rubble. What happens is that
        /// the thing in the opening's second panel simply goes ahead, which the player was told in the
        /// first thirty seconds of the game would happen if nobody stopped it.
        ///
        /// The lines answer the opening's the same way the win's do:
        ///
        ///   · Panel 1 echoes "Then came men with paper, a machine, and seven days." Same men, same
        ///     paper, and the deadline is gone because it has run out.
        ///   · Panel 2 is the only line in either ending that is about the world rather than the duck.
        ///     It is a fact about ponds, said flatly, and flat is what makes it land.
        ///   · Panel 3 is the bookend, and it is the one place these two endings are allowed to sound
        ///     alike: the win says the duck is still there because the pond is, the loss says he is
        ///     still there anyway. Gentle, and it does not apologise for him.
        /// </summary>
        static readonly Beat[] LoseBeats =
        {
            new Beat
            {
                id = "lose_sold", duration = 4.6f,
                transition = PanelTransition.CrossFade, transitionDuration = 0.9f,
                emphasis = 0.20f, sfxId = "sign_thud",
                narration = new[] { "The men with the paper came back with more paper." },
                tint = DuckSceneBuilder.P.TentRed, label = "SOLD · THE NOTICE AT THE WATER'S EDGE",
                // Deliberately the opening's notice move, near enough to be recognised: tight on the
                // board so it can be read, then back to the whole bank. The rhyme is the point — this
                // is the same sign, and this time nobody stopped it.
                from = new Rect(0.16f, 0.24f, 0.68f, 0.68f),
                to   = new Rect(0.04f, 0.03f, 0.92f, 0.92f)
            },
            new Beat
            {
                id = "lose_works", duration = 4.6f,
                transition = PanelTransition.CrossFade, transitionDuration = 0.8f,
                emphasis = 0.30f, sfxId = "shed_door",
                narration = new[] { "A pond takes longer to fill than to empty." },
                tint = "#D9A22B", label = "THE WORKS · DIGGERS, HOARDING, PIPE",
                // In, hard, ending on the pipe where the water is going. The excavator is the
                // silhouette that arrives; the drain is what the beat is actually about, and it is
                // the one thing in the panel that is still moving.
                from = new Rect(0.03f, 0.05f, 0.92f, 0.92f),
                to   = new Rect(0.22f, 0.18f, 0.60f, 0.60f)
            },
            new Beat
            {
                id = "lose_alone", duration = 6.2f,
                transition = PanelTransition.CrossFade, transitionDuration = 1.1f,
                emphasis = 0.45f, sfxId = "sad_swell",
                narration = new[] { "He stayed anyway. It was the only place he knew." },
                tint = "#6E7E88", label = "ALONE · WATCHING FROM THE BANK",
                // The tightest crop in either ending, and the slowest arrival at it. The page goes
                // dark around it (emphasis 0.45, the highest number in this file) so the last thing
                // the losing player looks at is one small figure with the rest of the page pulled
                // away from it. That is the whole beat; it does not need a second idea.
                from = new Rect(0.06f, 0.06f, 0.86f, 0.86f),
                to   = new Rect(0.24f, 0.22f, 0.54f, 0.54f)
            }
        };

        // ------------------------------------------------------------------ menu

        /// <summary>
        /// Re-import the six renders on their own, without rebuilding the pages.
        ///
        /// The build already imports them, so this exists for the one case the build cannot serve:
        /// the art is re-rendered while a scene OTHER than the one holding the endings is open, which
        /// is most of the time on a project where two people are in the editor at once. Import is
        /// asset-only — it touches no scene and dirties nothing — so it is always safe to run.
        /// </summary>
        [MenuItem("Duck/5 · Import ending panel art", priority = 4)]
        public static void ImportEndingPanels()
        {
            ImportPanels("win");
            ImportPanels("lose");
        }

        [MenuItem("Duck/3 · Rebuild ending cutscenes (open scene)", priority = 9)]
        public static void RebuildInOpenScene()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[Duck] no main camera in the open scene; cannot build the endings.");
                return;
            }

            var win = Build(cam, "win");
            var lose = Build(cam, "lose");

            var director = Object.FindFirstObjectByType<GameDirector>();
            if (director == null)
            {
                // Not an error: the endings are legal furniture in a scene that has no director yet
                // (a capture scene, a fresh blockout). But an unwired page is a page that will be
                // reported as "the ending never plays", so it says so out loud.
                Debug.LogWarning("[Duck] ending pages built, but there is no GameDirector in this " +
                                 "scene to wire them to; they will never be played.");
            }
            else
            {
                director.endingWin = win;
                director.endingLose = lose;
                EditorUtility.SetDirty(director);
            }

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            // Saved by the scene's OWN path rather than a constant. This builds into whatever is
            // open, and a hard-coded Main.unity here would quietly overwrite the main scene with the
            // arena the moment somebody ran it from the wrong tab.
            if (string.IsNullOrEmpty(scene.path))
                Debug.LogWarning("[Duck] the open scene has never been saved, so the ending pages " +
                                 "are in memory only. Save the scene to keep them.");
            else
                EditorSceneManager.SaveScene(scene, scene.path);

            Debug.Log($"[Duck] ending cutscenes rebuilt into {scene.name}: " +
                      $"win {win.TotalSeconds:0.0}s, lose {lose.TotalSeconds:0.0}s.");
        }

        /// <summary>
        /// Build the endings into Main.unity from wherever you happen to be standing.
        ///
        /// This exists because the endings live in exactly one scene and the editor is frequently
        /// showing a different one — the arena, the rally, a capture scene, or somebody else's tab in
        /// a second session. The obvious workaround is "open Main first", and it is the wrong one: it
        /// throws away whatever the other scene was doing, and on a shared editor it does that to
        /// somebody who did not ask.
        ///
        /// So Main is loaded ADDITIVELY, made active only for the few lines that need it, saved, and
        /// unloaded again — and if it was already open, it is left open. Whatever scene was active
        /// before is active again afterwards, including on the failure path, which is why the restore
        /// sits in a finally rather than at the end of the happy path.
        ///
        /// The temporary switch is safe rather than merely brief: Unity runs this on the main thread,
        /// so nothing else in the editor can observe the intermediate state.
        /// </summary>
        [MenuItem("Duck/3 · Rebuild ending cutscenes (Main scene)", priority = 9)]
        public static void RebuildInMainScene()
        {
            const string MainScenePath = "Assets/Scenes/Main.unity";

            var already = SceneManager.GetSceneByPath(MainScenePath);
            bool wasLoaded = already.IsValid() && already.isLoaded;
            var main = wasLoaded
                ? already
                : EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
            if (!main.IsValid())
            {
                Debug.LogError($"[Duck] could not open {MainScenePath}; the endings were not built.");
                return;
            }

            var prev = SceneManager.GetActiveScene();
            bool switched = prev != main;

            // Found by walking Main's own roots rather than through Camera.main or
            // FindFirstObjectByType. Both of those search every loaded scene, and with a second scene
            // additively loaded they will happily hand back the OTHER scene's camera — which would be
            // written into this page's Canvas as a cross-scene reference that cannot be serialised.
            Camera cam = null;
            GameDirector director = null;
            foreach (var root in main.GetRootGameObjects())
            {
                if (director == null) director = root.GetComponentInChildren<GameDirector>(true);
                foreach (var c in root.GetComponentsInChildren<Camera>(true))
                    if (cam == null && c.CompareTag("MainCamera")) cam = c;
            }

            if (cam == null)
            {
                Debug.LogError($"[Duck] no MainCamera in {MainScenePath}; cannot build the endings.");
                if (!wasLoaded) EditorSceneManager.CloseScene(main, true);
                return;
            }

            if (switched) SceneManager.SetActiveScene(main);
            try
            {
                var win = Build(cam, "win");
                var lose = Build(cam, "lose");

                if (director == null)
                    Debug.LogWarning($"[Duck] ending pages built into {main.name}, but it has no " +
                                     "GameDirector to wire them to; they will never be played.");
                else
                {
                    director.endingWin = win;
                    director.endingLose = lose;
                    EditorUtility.SetDirty(director);
                }

                EditorSceneManager.MarkSceneDirty(main);
                EditorSceneManager.SaveScene(main);
                Debug.Log($"[Duck] ending cutscenes rebuilt into {main.name}: " +
                          $"win {win.TotalSeconds:0.0}s, lose {lose.TotalSeconds:0.0}s.");
            }
            finally
            {
                if (switched) SceneManager.SetActiveScene(prev);
                if (!wasLoaded) EditorSceneManager.CloseScene(main, true);
            }
        }

        // ------------------------------------------------------------------ construction

        /// <summary>
        /// Build one ending page and return the component that plays it. Replaces the page of the
        /// same name if it is already in the scene, so this is safe to run repeatedly — and it has to
        /// be, because it is re-run every time a panel render lands.
        /// </summary>
        public static ComicSequence Build(Camera camera, string which)
        {
            var beats = which == "win" ? WinBeats : LoseBeats;
            string rootName = which == "win" ? "~ Ending Win" : "~ Ending Lose";

            var existing = GameObject.Find(rootName);
            if (existing != null) Object.DestroyImmediate(existing);

            ImportPanels(which);

            var canvasGO = new GameObject(rootName, typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGO.GetComponent<Canvas>();
            // Screen Space - Camera, for the same reason as the HUD and the opening page: the capture
            // rig renders the camera to a texture, and an Overlay canvas is missing from every
            // screenshot and therefore from every review.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            // Just outside the lens's near clip — see ComicSequence.PlaneDistanceFor, which owns the
            // number. At the 0.9 m this used to be, a camera-space canvas is depth-sorted against
            // the world and anything within 90 cm of the lens draws over the page. The ending is
            // framed at the judges' bench with the trophy in shot, which is exactly the sort of
            // close geometry that fits inside a 90 cm shell.
            canvas.planeDistance = ComicSequence.PlaneDistanceFor(camera);
            // Above the opening page's 200. The two never coexist on screen — the intro's page is
            // switched off long before the ending is asked for, and each ending page switches its own
            // root off at the end of this method — but the ordering is stated rather than left equal,
            // because two full-screen opaque canvases at the same sorting order resolve by hierarchy
            // position, which is not a thing a builder should depend on.
            canvas.sortingOrder = 210;

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;   // match height, as the HUD and the opening page do

            var seq = canvasGO.AddComponent<ComicSequence>();
            seq.restScale = RestScale;
            seq.skipArmDelay = SkipArm;
            seq.pageHold = PageHold;
            seq.fadeOutDuration = FadeOut;

            var root = (RectTransform)canvasGO.transform;
            var page = DuckUIBuilder.Frac("Page", root, 0f, 0f, 1f, 1f);
            seq.page = page.gameObject;

            BuildBackdrop(page, which);
            var cards = DuckUIBuilder.Frac("Cards", page, 0f, 0f, 1f, 1f);

            var views = new ComicSequence.PanelView[PanelCount];
            var panels = new ComicPanel[PanelCount];
            var missing = new List<string>();

            for (int i = 0; i < PanelCount; i++)
            {
                panels[i] = MakePanel(beats[i], which, i, missing);
                views[i] = MakeCard(cards, which, i, panels[i]);
            }

            // After every card, so it starts on top of all of them; the sequence pushes it and the
            // hero back to the top of the pile on each panel change.
            var dim = DuckUIBuilder.Frac("Dimmer", cards, 0f, 0f, 1f, 1f);
            seq.dimmer = DuckUIBuilder.AddImage(dim, null, new Color(0.06f, 0.07f, 0.05f, 0f));
            seq.dimmer.enabled = false;

            seq.panels = panels;
            seq.views = views;
            BuildBars(page, seq);
            // After the bars, because both of these sit inside them and a sibling created earlier
            // would be painted over by the bar it is meant to be printed on.
            BuildNarration(page, seq);
            BuildSkipHint(page, seq);
            BuildOverlays(page, seq);
            seq.sfx = LoadSfx(panels);

            if (missing.Count > 0)
                Debug.LogWarning($"[Duck] {which} ending art missing ({missing.Count}/{PanelCount}); " +
                                 $"placeholder cards will be shown for: {string.Join(", ", missing)}");

            page.gameObject.SetActive(false);   // ComicSequence.Begin switches it back on
            return seq;
        }

        static ComicPanel MakePanel(Beat b, string which, int i, List<string> missing)
        {
            string path = PanelPath(which, i);
            var art = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (art == null) missing.Add(path);

            return new ComicPanel
            {
                id = b.id,
                art = art,
                duration = b.duration,
                moveFrom = b.from,
                moveTo = b.to,
                narration = b.narration ?? new string[0],
                transition = b.transition,
                transitionDuration = b.transitionDuration,
                emphasis = b.emphasis,
                sfxId = b.sfxId,
                placeholderTint = DuckSceneBuilder.Hex(b.tint),
                placeholderLabel = b.label
            };
        }

        /// <summary>
        /// One panel: a drop shadow, the painted card, the picture inside it, and a stamp that only
        /// ever appears while the picture is missing.
        ///
        /// Identical construction to the opening's cards — same frame sprite, same hard offset
        /// shadow, same inset window — because these are meant to be pages out of the same book. The
        /// only thing that differs is where they come to rest.
        /// </summary>
        static ComicSequence.PanelView MakeCard(RectTransform parent, string which, int i,
                                               ComicPanel panel)
        {
            var go = new GameObject($"Panel_{i + 1:00}_{panel.id}", typeof(RectTransform));
            var card = (RectTransform)go.transform;
            card.SetParent(parent, false);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = CardSize;

            var group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            // Offset down and right rather than centred and blurred: this is a paper page lit from
            // the upper left, and one hard offset shadow sells "pinned on top of" far better than a
            // soft halo, which just looks like the card is glowing.
            var shadow = DuckUIBuilder.Frac("Shadow", card, 0f, 0f, 1f, 1f);
            shadow.offsetMin = new Vector2(9f, -21f);
            shadow.offsetMax = new Vector2(19f, -11f);
            DuckUIBuilder.AddImage(shadow, DuckUIBuilder.Spr("panel_card_256"),
                                   new Color(0.16f, 0.12f, 0.08f, 0.30f), Image.Type.Sliced);

            var frame = DuckUIBuilder.Frac("Frame", card, 0f, 0f, 1f, 1f);
            DuckUIBuilder.AddImage(frame, DuckUIBuilder.Spr("panel_card_256"), Color.white,
                                   Image.Type.Sliced);

            var window = DuckUIBuilder.Frac("Art", card, 0f, 0f, 1f, 1f);
            window.offsetMin = new Vector2(ArtInset, ArtInset);
            window.offsetMax = new Vector2(-ArtInset, -ArtInset);
            var raw = window.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;

            var stampRt = DuckUIBuilder.Frac("Stamp", window, 0.06f, 0.06f, 0.94f, 0.94f);
            var stamp = DuckUIBuilder.AddText(stampRt, "", 40f, TextAlignmentOptions.Center,
                                              Cream, 0.30f);
            stamp.fontStyle = FontStyles.Bold;
            stamp.gameObject.SetActive(false);

            // Deterministic scatter, seeded off the page as well as the panel so the win and the loss
            // are not the same three tilts twice. Random.value would give a different page every time
            // the scene was rebuilt, which makes "did that card move?" unanswerable in a frame sheet.
            int seed = (which == "win" ? 0 : 64) + i * 2;
            float a = Hash(seed + 1);
            float b = Hash(seed + 2);

            return new ComicSequence.PanelView
            {
                card = card,
                group = group,
                art = raw,
                stamp = stamp,
                restPosition = new Vector2((i - 1) * SlotX, SlotY),
                restRotation = (b - 0.5f) * 7f,     // relaxed once it is just part of the strip
                heroRotation = (a - 0.5f) * 3f      // barely off square while it is the beat
            };
        }

        static float Hash(int n)
        {
            float f = Mathf.Sin(n * 12.9898f) * 43758.5453f;
            return f - Mathf.Floor(f);
        }

        /// <summary>
        /// The paper the cards lie on, and the one place the two endings are allowed to look
        /// different before a single panel has loaded.
        ///
        /// The opening's page is warm parchment under a green vignette, and the win keeps exactly
        /// that — this is the same book, and the win is the story going the way the book was written.
        /// The loss cools the vignette to a wet slate instead. It is a small difference and it is
        /// doing real work: with the art missing, both pages are otherwise identical hazard-striped
        /// placeholders, and a reviewer looking at two frame sheets has to be able to tell at a glance
        /// which ending they are looking at.
        /// </summary>
        static void BuildBackdrop(RectTransform page, string which)
        {
            var paper = DuckUIBuilder.Frac("Paper", page, 0f, 0f, 1f, 1f);
            DuckUIBuilder.AddImage(paper, null, DuckSceneBuilder.Hex(DuckSceneBuilder.P.DuckShadow));

            var vig = DuckUIBuilder.Frac("Vignette", page, 0f, 0f, 1f, 1f);
            DuckUIBuilder.AddImage(vig, DuckUIBuilder.Spr("vignette_soft_512"),
                                   which == "win" ? new Color(0.16f, 0.22f, 0.16f, 0.55f)
                                                  : new Color(0.15f, 0.18f, 0.22f, 0.62f));
        }

        static void BuildBars(RectTransform page, ComicSequence seq)
        {
            var top = new GameObject("BarTop", typeof(RectTransform));
            var trt = (RectTransform)top.transform;
            trt.SetParent(page, false);
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = Vector2.zero;
            trt.sizeDelta = Vector2.zero;
            DuckUIBuilder.AddImage(trt, null, new Color(0.07f, 0.06f, 0.05f, 1f));

            var bottom = new GameObject("BarBottom", typeof(RectTransform));
            var brt = (RectTransform)bottom.transform;
            brt.SetParent(page, false);
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = Vector2.zero;
            DuckUIBuilder.AddImage(brt, null, new Color(0.07f, 0.06f, 0.05f, 1f));

            seq.barTop = trt;
            seq.barBottom = brt;
            seq.barHeight = BarHeight;
        }

        /// <summary>
        /// The narration band, inside the bottom letterbox bar, exactly as on the opening page.
        ///
        /// Stretched on x and pinned to the bottom edge so the band IS the bar on every aspect ratio:
        /// the canvas matches on height, so a narrower window narrows the page without moving the
        /// letterbox, and a fixed-width plate would have to be sized for the squarest window anybody
        /// might ever open the build in.
        ///
        /// No outline, for the reason the opening page found the hard way: AddText's outline is a dark
        /// green sized for text over bright grass, and at 0.30 on a thin face it eats far enough into
        /// the glyph to drag cream toward grey. This text sits on opaque near-black, so it has nothing
        /// to be separated from. NoWrap stays on purpose — every line in both tables fits on one line
        /// at this size, and a line somebody lengthens later should visibly shrink (catchable in a
        /// screenshot) rather than quietly become two stacked inside a 130-unit bar.
        /// </summary>
        static void BuildNarration(RectTransform page, ComicSequence seq)
        {
            var band = new GameObject("Narration", typeof(RectTransform));
            var rt = (RectTransform)band.transform;
            rt.SetParent(page, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(-300f, BarHeight);   // 150 units of margin at each end

            seq.narrationGroup = band.AddComponent<CanvasGroup>();
            seq.narrationGroup.alpha = 0f;

            seq.narrationText = DuckUIBuilder.AddText(rt, "", 44f, TextAlignmentOptions.Center,
                                                      Cream, 0f, false);
        }

        static void BuildSkipHint(RectTransform page, ComicSequence seq)
        {
            var hint = new GameObject("SkipHint", typeof(RectTransform));
            var rt = (RectTransform)hint.transform;
            rt.SetParent(page, false);
            // Top bar, right end — the bottom bar belongs to the narration, and a pulsing prompt
            // directly under a subtitle either reads as part of the story or makes the story look
            // like chrome.
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-46f, -(BarHeight - 40f) * 0.5f);
            rt.sizeDelta = new Vector2(520f, 40f);

            seq.skipGroup = hint.AddComponent<CanvasGroup>();
            seq.skipGroup.alpha = 0f;
            var t = DuckUIBuilder.AddText(rt, "PRESS ANY KEY TO SKIP", 22f,
                                          TextAlignmentOptions.Right, Cream, 0.24f, false);
            t.fontStyle = FontStyles.Bold;
        }

        static void BuildOverlays(RectTransform page, ComicSequence seq)
        {
            // Order matters and it is the order they are created in: the flash punches over the page
            // and the bars, and the fade goes over everything including the flash, so a skip during a
            // flash still ends in black rather than in white. Neither ending uses the flash — there
            // is no shock left to punctuate — but the overlay is built anyway, because ComicSequence
            // drives both fields unconditionally and a page missing one is a page with a null check
            // waiting to be forgotten.
            var flash = DuckUIBuilder.Frac("Flash", page, 0f, 0f, 1f, 1f);
            seq.flash = DuckUIBuilder.AddImage(flash, null, new Color(1f, 0.98f, 0.92f, 0f));
            seq.flash.enabled = false;

            var fade = DuckUIBuilder.Frac("Fade", page, 0f, 0f, 1f, 1f);
            seq.fade = DuckUIBuilder.AddImage(fade, null, new Color(0.03f, 0.03f, 0.04f, 1f));
        }

        /// <summary>
        /// Panel sound, wired to whatever exists in <c>Assets/Audio/Cutscene</c>.
        ///
        /// The ids are deliberately drawn from the opening's own list rather than invented: the
        /// endings ask for cash_sparkle, window_chime, pond_calm, sign_thud, shed_door and sad_swell,
        /// all six of which the opening already asks for. That is not laziness about naming, it is a
        /// refusal to grow the shopping list — the folder is empty today, and the day somebody renders
        /// those ten clips the endings get their audio for free instead of adding six more.
        /// </summary>
        static ComicSequence.PanelSfx[] LoadSfx(ComicPanel[] panels)
        {
            var list = new List<ComicSequence.PanelSfx>(panels.Length);
            int absent = 0;

            foreach (var p in panels)
            {
                if (string.IsNullOrEmpty(p.sfxId)) continue;
                string path = $"{SfxDir}/{p.sfxId}.wav";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) absent++;
                list.Add(new ComicSequence.PanelSfx { id = p.sfxId, clip = clip, volume = 0.6f });
            }

            // Counted, not listed. Every id here is one the opening page already warns about by name,
            // and the same six filenames printed twice per rebuild is how the one that is genuinely
            // missing stops being read.
            if (absent > 0)
                Debug.Log($"[Duck] ending page: {absent} of its panel stings are not in {SfxDir} yet; " +
                          "it plays silently until they land.");
            return list.ToArray();
        }

        // ------------------------------------------------------------------ art

        /// <summary>
        /// Import settings for the ending renders, identical to the opening's.
        ///
        /// Default and not Sprite: the panels are drawn by RawImage so the Ken Burns move can be a
        /// uvRect, which needs a plain texture. Mipmaps are on because a settled card is a fifth of
        /// its hero size, and an unmipped 1920-wide render shrunk into the strip aliases into a
        /// shimmering mess the moment it moves. Clamp, because a uvRect walks right up to the edge.
        /// </summary>
        public static void ImportPanels(string which)
        {
            EnsureFolder(PanelDir);
            int found = 0;
            for (int i = 0; i < PanelCount; i++)
            {
                string path = PanelPath(which, i);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                found++;

                imp.textureType = TextureImporterType.Default;
                imp.mipmapEnabled = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.filterMode = FilterMode.Bilinear;
                imp.sRGBTexture = true;
                imp.alphaSource = TextureImporterAlphaSource.None;
                imp.npotScale = TextureImporterNPOTScale.None;
                imp.maxTextureSize = 2048;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
            }
            Debug.Log($"[Duck] {which} ending panel art: {found}/{PanelCount} present in {PanelDir}.");
        }

        /// <summary>Where a render belongs. Same folder as the opening's panels — this is one
        /// cutscene texture set, and splitting it into three folders would mean three importer
        /// configurations to keep in step — but a name that cannot collide with panel_NN.png.</summary>
        static string PanelPath(string which, int i) => $"{PanelDir}/ending_{which}_{i + 1:00}.png";

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
