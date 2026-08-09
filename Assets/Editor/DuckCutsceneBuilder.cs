using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Builds the opening story: the scrapbook page, the ten panel cards, the emphasis bars, the
    /// narration band and the skip hint, plus the panel table itself.
    ///
    /// The panel *timings* live here rather than in the Inspector on purpose. The art does not
    /// exist yet, so the only thing that could be reviewed first was the shape of the sequence —
    /// how long the page sits on the asking price before it sits on the prize, whether the shock
    /// is a beat or a blink. Those are numbers, and numbers in a builder can be changed and re-run
    /// in seconds. Authoring them onto a component by hand would have made the one thing worth
    /// iterating on the most expensive thing to change.
    ///
    /// The story, for anyone editing the table: contentment, threat, despair, then a coincidence —
    /// the pond costs $10,000 and the championship pays $10,000 — then greed, resolve, the mower.
    /// Panels 5 and 6 are the hinge. They hold longest, they share an identical push into the
    /// lower-right of frame, and each ends on a narration line carrying the figure, so the number is
    /// read twice in the same band one panel apart and the player makes the connection before the
    /// duck does. Do not shorten them, and do not let their moves drift apart.
    ///
    /// There used to be a third thing holding that hinge up: a gold title-card plate under the hero
    /// card, stamping "7 DAYS" on panel 2 and "$10,000" on both hinge panels. It was cut on the
    /// player's own verdict — not wanted — so the narration band is now the only text in the
    /// sequence and carries the deadline and both figures alone. If a caption layer ever comes back,
    /// panels 5, 6 and 2 are where it belongs, and the narration lines that took over its job (see
    /// the table) would then be saying it twice.
    /// </summary>
    public static class DuckCutsceneBuilder
    {
        const string PanelDir = "Assets/Art/Textures/Cutscene";
        const string SfxDir = "Assets/Audio/Cutscene";
        const string NarrationDir = "Assets/Audio/Narration";
        const int PanelCount = 10;

        // What a spoken line needs on the page beyond the audio itself.
        //
        // ReadRate is the 17 characters a second quoted in the table's own comment, and it is still
        // the floor even for a line that has been rendered: a reading can be faster than a player
        // reads, and the band is a subtitle before it is a soundtrack. VoiceTail is the beat of
        // silence after the voice stops — without it the band dips on the last syllable, which makes
        // the narrator sound interrupted by the page.
        /// <summary>
        /// Seconds before the opening can be skipped, counted from the first frame of the fade-in.
        ///
        /// Three, down from the eight the default used to give it. The delay is there so the exit is
        /// not offered before the player knows what they would be leaving; one panel is enough for
        /// that, and the fade-in is 0.9 s against a first panel of 4.6 s, so three lands with that
        /// panel up and read. Eight was in the middle of the SECOND panel, which is no longer
        /// protecting the choice, only making the player wait to make it.
        /// </summary>
        const float SkipArm = 3f;

        const float VoiceTail = 0.45f;
        const float ReadRate = 17f;
        const float MinRead = 1.5f;

        // The two hinge panels are pinned to a common duration. Their moves are identical by design
        // (see the table) and duration is what sets a move's *speed*, so letting the voice stretch
        // them by different amounts would make the same camera arrive at the same place at two
        // different rates — which is precisely the drift the table's comment forbids.
        static readonly string[] HingeIds = { "price", "prize" };

        // The card is sized so the picture window inside the painted frame is exactly 16:9. Sizing
        // the *card* to 16:9 instead put a 1.93:1 window inside it, and a RawImage stretches its UV
        // rect to fill its rect — so every panel was quietly squashed by nine percent, which on
        // renders of a duck is the difference between the duck and a slightly wrong duck.
        // A MAT, not a margin, and not the nine-slice border either — that is 57/61, and the paint
        // on this card stops at 23/28. 46 sits between the two on purpose: it clears the rule with
        // room to spare, the way a mounted photograph does, and the window it leaves is what makes
        // the arithmetic below come out at 16:9. Nothing here needs CardArt: this is a picture
        // frame with a picture in it, and the number is a composition rather than a clearance.
        const float ArtInset = 46f;
        static readonly Vector2 ArtWindow = new Vector2(880f, 495f);
        static readonly Vector2 CardSize = new Vector2(ArtWindow.x + ArtInset * 2f,
                                                       ArtWindow.y + ArtInset * 2f);

        // Where a panel lives once it has had its turn. Two chronological columns down the margins,
        // clear of the hero card in the middle. The x is deliberately modest: the canvas matches on
        // height, so on a narrow window the page gets narrower while the hero stays the same size,
        // and slots pushed any further out would walk off the edge on anything squarer than 16:9.
        const float ColumnX = 625f;
        const float SlotTop = 400f, SlotStep = 200f;

        // The letterbox stopped animating (see ComicSequence.AnimateOverlays) and that is what makes
        // it usable as furniture: both the narration band and the skip line are placed *inside* the
        // bars. So the height is a const here and pushed onto the component, rather than left to the
        // component's field default — if the two ever drifted apart the subtitles would end up half
        // on the paper page, which is the one place they are not guaranteed to be legible.
        const float BarHeight = 130f;

        static readonly Color Cream = new Color(0.97f, 0.94f, 0.86f);

        // ------------------------------------------------------------------ the table

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
        /// Ten beats. Every crop is square in UV — width equal to height — because the source art
        /// is 16:9 and the window it lands in is 16:9, so any crop with a different ratio arrives
        /// stretched. If you edit a move, keep w == h.
        ///
        /// The narration is written to one rule: never say what the panel shows. The duck's face is
        /// already shocked, so the line under it is about what nobody asked him; the eyes already
        /// have dollar signs in them, so the line is the excuse he makes for it.
        ///
        /// The exceptions are the three facts no picture in this sequence can carry, and they are
        /// exceptions because the caption plate that used to carry them was cut: the length of the
        /// deadline (panel 2), the price of the pond (panel 5) and the size of the prize (panel 6).
        /// The last two are the hinge and they are deliberately verbatim — "$10,000" twice, in the
        /// same band, in the same wording, one panel apart, and both as the panel's *last* line so
        /// they are separated by a single beat of narration. That repetition is the whole coincidence
        /// and it is now the only thing making it. It survived one rewrite already: while the gold
        /// captions existed these lines were written to avoid the number, and panel 6's line was
        /// "He read the prize twice" — which, once the captions went, pointed at nothing.
        ///
        /// Lines are short enough to sit on one line of the band at 38 pt, and the three long panels
        /// — 5, 6 and 10 — carry two each so their holds are not one sentence sitting still. Keeping
        /// every line at or under about fifty characters is not a style preference: a two-line panel
        /// gives each line roughly 2.2 s of visible time, and the rate a subtitle can actually be
        /// read at is around 17 characters a second.
        /// </summary>
        static readonly Beat[] Beats =
        {
            new Beat
            {
                id = "pond", duration = 4.2f,
                transition = PanelTransition.CrossFade, transitionDuration = 0.9f,
                emphasis = 0f, sfxId = "pond_calm",
                narration = new[] { "The pond had always been there. So had the duck." },
                tint = DuckSceneBuilder.P.Pond, label = "THE POND · FEEDING THE FISH",
                // A slow push in on a duck that has no idea. Starts on the whole scene so the park
                // is established, ends on the duck so the next panel has somebody to happen to.
                from = new Rect(0.02f, 0.03f, 0.96f, 0.96f),
                to   = new Rect(0.13f, 0.11f, 0.76f, 0.76f)
            },
            new Beat
            {
                id = "notice", duration = 4.6f,
                transition = PanelTransition.CrossFade, transitionDuration = 0.7f,
                emphasis = 0.12f, sfxId = "sign_thud",
                // The line counts the days because nothing else does any more. It used to end on
                // "and a date", because a gold "7 DAYS" was stamped under this panel; with that gone,
                // a deadline whose length the player is never told is not a deadline, it is a mood.
                narration = new[] { "Then came men with paper, a machine, and seven days." },
                tint = DuckSceneBuilder.P.TentRed, label = "THE NOTICE · POND TO BE REMOVED",
                // Backwards: starts tight on the sign so it can be read, then pulls back to find
                // the excavator behind it. The reveal is the machine, not the words.
                from = new Rect(0.14f, 0.22f, 0.72f, 0.72f),
                to   = new Rect(0.04f, 0.03f, 0.92f, 0.92f)
            },
            new Beat
            {
                id = "shock", duration = 4.0f,
                transition = PanelTransition.CrossFade, transitionDuration = 0.7f,
                emphasis = 0.28f, sfxId = "shock_sting",
                narration = new[] { "Nobody had asked the duck what the pond was worth." },
                tint = DuckSceneBuilder.P.Bill, label = "SHOCK · THE DUCK'S FACE",
                // Short and hard. Everything else on the page goes dark and the bars close in, so
                // for under two seconds there is nothing to look at but the face.
                from = new Rect(0.12f, 0.12f, 0.80f, 0.80f),
                to   = new Rect(0.22f, 0.20f, 0.60f, 0.60f)
            },
            new Beat
            {
                id = "grief", duration = 4.0f,
                transition = PanelTransition.CrossFade, transitionDuration = 1.0f,
                emphasis = 0.18f, sfxId = "sad_swell",
                // Ties the grief back to panel one's crumbs and opens the money thread the two
                // hinge panels close, so the coincidence lands on a question already asked.
                narration = new[] { "And a pond cannot be bought back with breadcrumbs." },
                tint = "#5C86C8", label = "GRIEF · LOOKING OUT OVER THE WATER",
                // A drift rather than a push. Nothing is being discovered here; the panel is a
                // held breath between the shock and the plan, and it needs the longest transition
                // in the sequence to arrive on.
                from = new Rect(0.16f, 0.06f, 0.78f, 0.78f),
                to   = new Rect(0.05f, 0.11f, 0.78f, 0.78f)
            },
            new Beat
            {
                id = "price", duration = 5.8f,
                transition = PanelTransition.CrossFade, transitionDuration = 0.8f,
                emphasis = 0.35f, sfxId = "window_chime",
                // First half of the hinge, and the second line is the price the gold caption used to
                // carry. It is deliberately the shortest line in the sequence and it is deliberately
                // last: a bare figure held alone for two and a half seconds is the closest a subtitle
                // band gets to a stamp, which is what this beat lost when the plate went.
                narration = new[]
                {
                    "A pond, it turned out, could be bought.",
                    "It cost $10,000."
                },
                tint = DuckSceneBuilder.P.WoodWarm, label = "THE PRICE · LAND FOR SALE",
                // Half of the hinge. Ends hard on the lower-right of frame, where the price is.
                from = new Rect(0.02f, 0.06f, 0.92f, 0.92f),
                to   = new Rect(0.30f, 0.06f, 0.56f, 0.56f)
            },
            new Beat
            {
                id = "prize", duration = 5.8f,
                transition = PanelTransition.CrossFade, transitionDuration = 0.8f,
                emphasis = 0.35f, sfxId = "paper_flutter",
                // The other half, and it is the echo now. "$10,000" arrives in the same band, at the
                // same size, in the same words as three seconds ago, with one line of narration in
                // between — near enough to be recognised, far enough not to read as a typo. What it
                // still does not do is join the two up: nothing says "the same amount", because the
                // player noticing that is the only work the sequence asks of them.
                narration = new[]
                {
                    "Then the wind brought him a flyer.",
                    "First prize: $10,000."
                },
                tint = DuckSceneBuilder.P.Brass, label = "THE PRIZE · CHAMPIONSHIP FLYER",
                // The other half, and the move is identical to the panel before it on purpose. The
                // same camera arriving at the same place on the frame to find the same number is
                // the whole coincidence; making the two panels move differently would have left it
                // as two facts the player has to put together themselves.
                from = new Rect(0.02f, 0.06f, 0.92f, 0.92f),
                to   = new Rect(0.30f, 0.06f, 0.56f, 0.56f)
            },
            new Beat
            {
                id = "realise", duration = 4.2f,
                transition = PanelTransition.CrossFade, transitionDuration = 0.8f,
                emphasis = 0.32f, sfxId = "realise_ding",
                narration = new[] { "A duck cannot own land. But a duck can tend it." },
                tint = DuckSceneBuilder.P.CutTip, label = "REALISATION · LOOKING DOWN AT THE FLYER",
                // A cut, because a cross-fade here would blend the flyer into the duck reading it
                // and blur the join the sequence has spent eleven seconds setting up.
                from = new Rect(0.06f, 0.10f, 0.84f, 0.84f),
                to   = new Rect(0.17f, 0.17f, 0.66f, 0.66f)
            },
            new Beat
            {
                id = "greed", duration = 4.0f,
                transition = PanelTransition.CrossFade, transitionDuration = 0.7f,
                emphasis = 0.40f, sfxId = "cash_sparkle",
                narration = new[] { "It was not greed, he told himself. It was a deposit." },
                tint = DuckSceneBuilder.P.Brass, label = "GREED · DOLLAR SIGNS IN THE EYES",
                // The second flash, and the rhyme with the third panel is deliberate: the same
                // treatment for the thing that ruined the duck and the thing that saves it.
                from = new Rect(0.16f, 0.16f, 0.64f, 0.64f),
                to   = new Rect(0.25f, 0.23f, 0.50f, 0.50f)
            },
            new Beat
            {
                id = "resolve", duration = 4.2f,
                transition = PanelTransition.CrossFade, transitionDuration = 0.8f,
                emphasis = 0.45f, sfxId = "resolve_drum",
                // The picture is pure determination, so the line undercuts it. That gap is the
                // premise of the whole game and this is the only place it can be set up for free.
                narration = new[] { "He had never mown so much as a verge in his life." },
                tint = DuckSceneBuilder.P.MowerRed, label = "RESOLVE · JAW SET, WINGS CLENCHED",
                // Barely moves. After a flash and a push, a panel that is almost still reads as
                // somebody who has stopped reacting and started deciding.
                from = new Rect(0.10f, 0.08f, 0.78f, 0.78f),
                to   = new Rect(0.16f, 0.13f, 0.70f, 0.70f)
            },
            new Beat
            {
                id = "shed", duration = 5.0f,
                transition = PanelTransition.CrossFade, transitionDuration = 1.1f,
                emphasis = 0.5f, sfxId = "shed_door",
                // Hands over to the game. The last line is the last thing read before the lawn, so
                // it is an action rather than an observation.
                narration = new[]
                {
                    "The mower was older than he was.",
                    "Seven days left. He pulled the cord."
                },
                tint = DuckSceneBuilder.P.EngineGrey, label = "THE SHED · WALKING TO THE MOWER",
                // The only panel that pulls out rather than in. It starts on the mower and opens up
                // to the whole dim shed, which hands the game its first object and its first room
                // in one move.
                from = new Rect(0.21f, 0.17f, 0.60f, 0.60f),
                to   = new Rect(0.02f, 0.02f, 0.94f, 0.94f)
            }
        };

        // ------------------------------------------------------------------ menu

        [MenuItem("Duck/5 · Import cutscene panel art", priority = 4)]
        public static void ImportPanels()
        {
            EnsureFolder(PanelDir);
            int found = 0;
            for (int i = 0; i < PanelCount; i++)
            {
                string path = PanelPath(i);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                found++;

                // Default, not Sprite: the panels are drawn by RawImage so the Ken Burns move can
                // be a uvRect, which needs a plain texture. Mipmaps are on because a settled panel
                // is a fifth of its hero size, and an unmipped 1920-wide render shrunk to 185 units
                // aliases into a shimmering mess the moment it moves.
                imp.textureType = TextureImporterType.Default;
                imp.mipmapEnabled = true;
                imp.wrapMode = TextureWrapMode.Clamp;    // uvRect walks right up to the edge
                imp.filterMode = FilterMode.Bilinear;
                imp.sRGBTexture = true;
                imp.alphaSource = TextureImporterAlphaSource.None;
                imp.npotScale = TextureImporterNPOTScale.None;
                imp.maxTextureSize = 2048;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
            }
            Debug.Log($"[Duck] cutscene panel art: {found}/{PanelCount} present in {PanelDir}.");
        }

        [MenuItem("Duck/3 · Rebuild opening cutscene (open scene)", priority = 9)]
        public static void RebuildInOpenScene()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[Duck] no main camera in the open scene; cannot build the cutscene.");
                return;
            }
            var seq = Build(cam);
            var director = Object.FindFirstObjectByType<GameDirector>();
            if (director != null)
            {
                director.intro = seq;
                EditorUtility.SetDirty(director);
            }
            Debug.Log("[Duck] opening cutscene rebuilt.");
        }

        // ------------------------------------------------------------------ construction

        /// <summary>
        /// Build the whole page and return the component that plays it. Replaces any page already
        /// in the scene, so this is safe to run repeatedly.
        /// </summary>
        public static ComicSequence Build(Camera camera)
        {
            var existing = GameObject.Find("~ Cutscene");
            if (existing != null) Object.DestroyImmediate(existing);

            ImportPanels();

            var canvasGO = new GameObject("~ Cutscene", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGO.GetComponent<Canvas>();
            // Screen Space - Camera and not Overlay, for the same reason the HUD is: the capture
            // rig renders the camera to a texture, and an Overlay canvas is missing from every
            // screenshot and therefore from every review.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            // Just outside the lens's near clip, and NOT a round number chosen to sit in front of
            // the HUD's plane, which is what it used to be. A camera-space canvas is sorted by the
            // depth buffer, so at 0.9 m every blade of grass within 90 cm of the lens drew on top of
            // the story. See ComicSequence.PlaneDistanceFor, which owns the number so that the value
            // baked here and the floor the page applies to itself at runtime cannot disagree.
            canvas.planeDistance = ComicSequence.PlaneDistanceFor(camera);
            canvas.sortingOrder = 200;        // the HUD sits at 100

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;   // match height, as the HUD does

            var seq = canvasGO.AddComponent<ComicSequence>();

            // Said out loud rather than left to the C# default, the way DuckEndingBuilder has always
            // stated its own — see SkipArm there.
            //
            // This is not tidiness. Changing the default in ComicSequence does NOT reach a scene that
            // already has one saved: Unity keeps the serialized value, and a rebuild that reuses the
            // component keeps it too. The opening sat at eight seconds through two edits of the
            // default for exactly that reason, while the two endings — which set theirs here — moved
            // the moment they were asked to. A field the builder does not write is a field nobody can
            // change without hand-editing a scene.
            seq.skipArmDelay = SkipArm;

            var root = (RectTransform)canvasGO.transform;
            var page = DuckUIBuilder.Frac("Page", root, 0f, 0f, 1f, 1f);
            seq.page = page.gameObject;

            BuildBackdrop(page);
            var cards = DuckUIBuilder.Frac("Cards", page, 0f, 0f, 1f, 1f);

            var views = new ComicSequence.PanelView[PanelCount];
            var panels = new ComicPanel[PanelCount];
            var missing = new List<string>();

            for (int i = 0; i < PanelCount; i++)
            {
                panels[i] = MakePanel(i, missing);
                views[i] = MakeCard(cards, i, panels[i]);
            }

            // The dimmer is created after every card so it starts on top of all of them; the
            // sequence pushes it and the hero back to the top on each panel change.
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
            // Last, because it is the only step that changes a panel's duration — and it has to be
            // able to report the page's total time before and after the voice stretched it.
            LoadNarration(panels);

            if (missing.Count > 0)
            {
                // One warning, not ten. A missing-art run is the expected state right now, and a
                // wall of identical warnings is how a real one gets missed.
                Debug.LogWarning($"[Duck] cutscene panel art missing ({missing.Count}/{PanelCount}); " +
                                 $"placeholder cards will be shown for: {string.Join(", ", missing)}");
            }

            page.gameObject.SetActive(false);   // ComicSequence.Begin switches it back on
            return seq;
        }

        static ComicPanel MakePanel(int i, List<string> missing)
        {
            var b = Beats[i];
            string path = PanelPath(i);
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
        /// </summary>
        static ComicSequence.PanelView MakeCard(RectTransform parent, int i, ComicPanel panel)
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
            // the upper left, and one hard offset shadow sells "pinned on top of" far better than
            // a soft halo, which just looks like the card is glowing.
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

            // Deterministic scatter. Random.value would give a different page every time the scene
            // is rebuilt, which makes "did that panel move?" unanswerable when reviewing frames.
            float a = Hash(i * 2 + 1);
            float b = Hash(i * 2 + 2);

            int column = i < 5 ? -1 : 1;
            int row = i < 5 ? i : i - 5;

            return new ComicSequence.PanelView
            {
                card = card,
                group = group,
                art = raw,
                stamp = stamp,
                restPosition = new Vector2(ColumnX * column, SlotTop - row * SlotStep),
                restRotation = (b - 0.5f) * 8f,     // relaxed once it is just part of the page
                heroRotation = (a - 0.5f) * 3f      // barely off square while it is the beat
            };
        }

        static float Hash(int n)
        {
            float f = Mathf.Sin(n * 12.9898f) * 43758.5453f;
            return f - Mathf.Floor(f);
        }

        static void BuildBackdrop(RectTransform page)
        {
            // Warm parchment, a shade deeper than the cream the panel frames are painted in, so a
            // card always has an edge against the page it is lying on.
            var paper = DuckUIBuilder.Frac("Paper", page, 0f, 0f, 1f, 1f);
            DuckUIBuilder.AddImage(paper, null, DuckSceneBuilder.Hex(DuckSceneBuilder.P.DuckShadow));

            var vig = DuckUIBuilder.Frac("Vignette", page, 0f, 0f, 1f, 1f);
            DuckUIBuilder.AddImage(vig, DuckUIBuilder.Spr("vignette_soft_512"),
                                   new Color(0.16f, 0.22f, 0.16f, 0.55f));
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

        // There is no BuildCaption any more.
        //
        // It built a dark plate at y = -370 carrying 52 pt gold caps — "7 DAYS", then "$10,000"
        // twice — and it is gone at the player's request rather than because it failed. The plate
        // and the panel field went with it: a builder that still made an empty card, or a data field
        // nothing reads, is how a removed feature comes back as a bug. The narration band is now the
        // only text on the page, and it absorbed the three facts the plate was carrying.

        /// <summary>
        /// The narration band: the storybook voice, one line at a time, inside the bottom letterbox.
        ///
        /// It sits *in* the bar rather than above it, and that is why it needs no plate of its own —
        /// the bar is opaque near-black across the full width, so a cream line on it is legible over
        /// any panel, any page colour and any WebGL resolution without adding another rectangle to a
        /// screen that already has a painted frame on it.
        ///
        /// The band is now the whole text layer, which makes the placement more load-bearing than it
        /// was, not less: the deadline and both figures are read here or nowhere. It stays in the bar
        /// rather than moving up into the space the caption plate used to occupy at y = -370, because
        /// that space is over the paper page, where legibility depends on whatever the page and the
        /// settled cards happen to be doing behind it.
        /// </summary>
        static void BuildNarration(RectTransform page, ComicSequence seq)
        {
            var band = new GameObject("Narration", typeof(RectTransform));
            var rt = (RectTransform)band.transform;
            rt.SetParent(page, false);
            // Stretched on x and pinned to the bottom edge, so the band *is* the bar on every aspect
            // ratio. The canvas matches on height, so a narrower window narrows the page without
            // moving the letterbox, and a fixed-width plate would have to be sized for the squarest
            // window anybody might ever open the build in.
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(-300f, BarHeight);   // 150 units of margin at each end

            seq.narrationGroup = band.AddComponent<CanvasGroup>();
            seq.narrationGroup.alpha = 0f;

            // 44 pt with NO OUTLINE, and the outline is the important half of that.
            //
            // This was 38 pt at the HUD's standard 0.30 outline width, and it arrived on screen as a
            // pale cool grey rather than as cream. AddText's outline is dark green (0.09, 0.16, 0.10)
            // and it exists for a good reason — every HUD element in this game sits over bright
            // saturated grass and needs its own dark edge to survive. This text does not: it sits on
            // the letterbox bar, which is opaque near-black. So the outline had nothing to separate
            // the text from, and a 0.30 width on a thin face eats far enough into the glyph to pull
            // its apparent colour down toward the outline's. The narration was fighting an edge it
            // did not need.
            //
            // It matters more than it would have last week: the 7 DAYS / $10,000 caption plates were
            // removed on request, so this band is now the ONLY text telling the story.
            //
            // NoWrap stays on purpose. Every line in the table is short enough to sit on one line at
            // this size; with wrapping on, a line somebody lengthens later would quietly become two
            // stacked inside a 130-unit bar. NoWrap turns that into text that visibly shrinks
            // instead, which is a mistake you can catch in a screenshot.
            seq.narrationText = DuckUIBuilder.AddText(rt, "", 44f, TextAlignmentOptions.Center,
                                                      Cream, 0f, false);
        }

        static void BuildSkipHint(RectTransform page, ComicSequence seq)
        {
            var hint = new GameObject("SkipHint", typeof(RectTransform));
            var rt = (RectTransform)hint.transform;
            rt.SetParent(page, false);
            // Top bar, right end. It used to sit centred in the bottom bar, which now belongs to the
            // narration — and a pulsing prompt directly under a subtitle would either be read as
            // part of the story or make the story look like chrome. A skip prompt in a top corner is
            // where players already look for one, and it is the one piece of text up there.
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
            // Order matters and it is the order they are created in: the flash punches over the
            // page and the bars, and the fade goes over everything including the flash, so a skip
            // during a flash still ends in black rather than in white.
            var flash = DuckUIBuilder.Frac("Flash", page, 0f, 0f, 1f, 1f);
            seq.flash = DuckUIBuilder.AddImage(flash, null, new Color(1f, 0.98f, 0.92f, 0f));
            seq.flash.enabled = false;

            var fade = DuckUIBuilder.Frac("Fade", page, 0f, 0f, 1f, 1f);
            seq.fade = DuckUIBuilder.AddImage(fade, null, new Color(0.03f, 0.03f, 0.04f, 1f));
        }

        /// <summary>
        /// Panel sound, wired to whatever exists in <c>Assets/Audio/Cutscene</c>.
        ///
        /// Every id in the table is listed whether or not the clip is on disk, so the wiring is
        /// visible in the Inspector and the shopping list is the array itself. Playback goes
        /// through AudioDirector's existing one-shot bus — the cutscene does not get an AudioSource
        /// of its own, because a second audio path is a second mix nobody will remember to balance.
        /// </summary>
        static ComicSequence.PanelSfx[] LoadSfx(ComicPanel[] panels)
        {
            var list = new List<ComicSequence.PanelSfx>(panels.Length);
            var absent = new List<string>();

            foreach (var p in panels)
            {
                if (string.IsNullOrEmpty(p.sfxId)) continue;
                string path = $"{SfxDir}/{p.sfxId}.wav";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) absent.Add($"{p.sfxId}.wav");
                list.Add(new ComicSequence.PanelSfx { id = p.sfxId, clip = clip, volume = 0.6f });
            }

            if (absent.Count > 0)
                Debug.LogWarning($"[Duck] cutscene audio not present in {SfxDir} " +
                                 $"({absent.Count} clips): {string.Join(", ", absent)}. " +
                                 "The sequence plays silently until they land.");
            return list.ToArray();
        }

        // ------------------------------------------------------------------ narration voice

        /// <summary>The narration table, for the tool that renders it to speech. Exposed as a copy of
        /// the authoring data so the voice files cannot drift from the lines the page will show.</summary>
        public static string[][] NarrationTable()
        {
            var table = new string[PanelCount][];
            for (int i = 0; i < PanelCount; i++)
                table[i] = (string[])(Beats[i].narration ?? new string[0]).Clone();
            return table;
        }

        /// <summary>Where the render of one line belongs. Positional rather than keyed on the panel
        /// id, to match the <c>panel_01.png</c> convention the art already uses.</summary>
        public static string NarrationPath(int panel, int line)
            => $"{NarrationDir}/nar_{panel + 1:00}_{line + 1}.wav";

        public static string NarrationFolder => NarrationDir;

        /// <summary>
        /// Import settings for the narration, applied by path rather than by an AssetPostprocessor.
        ///
        /// A postprocessor would be the tidier-looking answer and it is the wrong one: it runs on
        /// every audio import in the project, so the 63 hand-tuned SFX and music clips next door
        /// would then depend on a folder test inside it staying correct forever. This can only ever
        /// touch <c>Assets/Audio/Narration</c>, and it is called from Build for the same reason
        /// ImportPanels is — a clip dropped in by hand gets the same treatment as a baked one.
        ///
        /// Compressed In Memory rather than Decompress On Load: thirteen clips of a few seconds is
        /// about 3.5 MB of PCM that would sit in the WebGL heap for the whole session to be used once,
        /// against roughly a third of a megabyte compressed. Decoding one short mono stream every few
        /// seconds, on a page that is drawing ten quads and no game, is not a frame-rate risk — and
        /// render targets already account for 50 MB on this project, so the heap is the budget worth
        /// protecting. Not Streaming under any circumstances: WebGL has no file handles to stream from
        /// and the setting silently degrades.
        ///
        /// The WebGL override is spelled out rather than left to the default, because the platform's
        /// only compression format is AAC. Letting Vorbis fall through works — Unity remaps it — but
        /// then the quality number in the Inspector is not the one the build used, and an audio budget
        /// nobody can read off the importer is an audio budget nobody checks.
        /// </summary>
        public static void ImportNarration()
        {
            if (!AssetDatabase.IsValidFolder(NarrationDir)) return;

            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { NarrationDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as AudioImporter;
                if (imp == null) continue;

                // AudioImporterSampleSettings is a struct, so it has to be read out, changed and
                // written back. Setting a field on the property directly compiles and does nothing.
                var s = imp.defaultSampleSettings;
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.5f;
                s.preloadAudioData = true;      // a line must not hitch on the frame it starts

                var web = s;
                web.compressionFormat = AudioCompressionFormat.AAC;

                if (Same(imp.defaultSampleSettings, s) && imp.forceToMono &&
                    imp.ContainsSampleSettingsOverride("WebGL") &&
                    Same(imp.GetOverrideSampleSettings("WebGL"), web)) continue;

                imp.defaultSampleSettings = s;
                imp.forceToMono = true;         // one narrator, in the middle, on every device
                if (!imp.SetOverrideSampleSettings("WebGL", web))
                    Debug.LogWarning($"[Duck] WebGL audio override refused for {path}; the build " +
                                     "will fall back to the default settings.");
                imp.SaveAndReimport();
            }
        }

        static bool Same(AudioImporterSampleSettings a, AudioImporterSampleSettings b)
            => a.loadType == b.loadType && a.compressionFormat == b.compressionFormat &&
               a.preloadAudioData == b.preloadAudioData && Mathf.Approximately(a.quality, b.quality);

        /// <summary>
        /// Wire the spoken lines to the panels and re-time the page around them.
        ///
        /// The clip lengths are measured here rather than pasted into the table above, and that is
        /// deliberate: a generated number sitting in source is a number that goes stale silently the
        /// first time the voice is re-baked. Measuring on every build means the holds cannot disagree
        /// with the files, while the things that are actually matters of taste — the authored
        /// duration, the tail, the reading rate — stay where the rest of this project keeps its
        /// timing, which is in this file as numbers.
        ///
        /// A panel with no rendered lines at all is left completely alone: no holds, no stretched
        /// duration. That is not a convenience, it is the contract — with nothing in
        /// <c>Assets/Audio/Narration</c> the page has to behave exactly as it did before there was a
        /// voice, and the way to guarantee that is for the data to be untouched rather than for the
        /// runtime to be clever.
        /// </summary>
        static void LoadNarration(ComicPanel[] panels)
        {
            ImportNarration();

            int voiced = 0, absent = 0;
            float before = 0f, after = 0f;
            var stretched = new List<string>();

            foreach (var p in panels) before += p.duration;

            for (int i = 0; i < panels.Length; i++)
            {
                var p = panels[i];
                int n = p.narration == null ? 0 : p.narration.Length;
                if (n == 0) continue;

                var clips = new AudioClip[n];
                int here = 0;
                for (int k = 0; k < n; k++)
                {
                    clips[k] = AssetDatabase.LoadAssetAtPath<AudioClip>(NarrationPath(i, k));
                    if (clips[k] != null) here++; else absent++;
                }
                voiced += here;
                if (here == 0) continue;    // untouched: the silent even split still applies

                p.narrationVoice = clips;
                p.narrationHold = new float[n];
                float sum = 0f;
                for (int k = 0; k < n; k++)
                {
                    // Only the first line pays the lead-in and only the last pays the lead-out;
                    // ComicSequence charges them at exactly those two edges, and a hold that budgeted
                    // for clearances the sequence does not apply would leave dead air mid-panel.
                    float lead = k == 0 ? ComicSequence.NarrationLeadIn : 0f;
                    float tail = k == n - 1 ? ComicSequence.NarrationLeadOut : 0f;
                    float spoken = clips[k] == null ? 0f : clips[k].length + VoiceTail;
                    float read = Mathf.Max((p.narration[k] ?? "").Length / ReadRate, MinRead);
                    p.narrationHold[k] = lead + tail + Mathf.Max(spoken, read);
                    sum += p.narrationHold[k];
                }

                if (sum > p.duration)
                {
                    stretched.Add($"{p.id} {p.duration:0.0}→{sum:0.0}s");
                    p.duration = sum;
                }
                else
                {
                    // The voice needed less than the beat was authored for, so the holds have to be
                    // padded back out to fill it — otherwise the band goes dark and the panel sits
                    // there finishing its move in silence.
                    //
                    // All of the slack to the LAST line, not spread across them. On every multi-line
                    // panel here the last line is the one meant to land: both halves of the hinge end
                    // on their figure, and the table's comment is explicit that "It cost $10,000."
                    // is held alone as long as it is *because* that is the closest this band gets to
                    // a stamp. Sharing the slack out would shorten exactly the line that must not
                    // shorten. On a single-line panel this restores the pre-voice behaviour exactly:
                    // one line, held for the whole beat.
                    p.narrationHold[n - 1] += p.duration - sum;
                }
            }

            EqualiseHinge(panels);
            foreach (var p in panels) after += p.duration;

            if (absent > 0)
                Debug.LogWarning($"[Duck] cutscene narration not present in {NarrationDir} " +
                                 $"({absent} of {absent + voiced} lines). Those lines are read and " +
                                 "not heard; run Duck/5 · Bake cutscene narration to render them.");
            if (voiced > 0)
                Debug.Log($"[Duck] cutscene narration: {voiced} spoken line(s). Panel time " +
                          $"{before:0.0}s → {after:0.0}s" +
                          (stretched.Count > 0 ? $"; stretched: {string.Join(", ", stretched)}" : ""));
        }

        /// <summary>
        /// Pin the two hinge panels to whichever of them the voice made longer, and give the extra to
        /// the line carrying the figure — so the two prices end up held for the same length of time as
        /// well as arriving on the same move. That is the coincidence; it should not be lopsided.
        /// </summary>
        static void EqualiseHinge(ComicPanel[] panels)
        {
            float longest = 0f;
            int found = 0;
            foreach (var p in panels)
                foreach (var id in HingeIds)
                    if (p.id == id) { longest = Mathf.Max(longest, p.duration); found++; }

            if (found != HingeIds.Length) return;    // the table was edited; do not guess

            foreach (var p in panels)
                foreach (var id in HingeIds)
                {
                    if (p.id != id) continue;
                    float extra = longest - p.duration;
                    p.duration = longest;
                    // Same reasoning as the slack above, and it has to be repeated here because this
                    // runs after the holds were sized: a duration raised without a hold raised to
                    // match is a panel that finishes its narration early and then waits.
                    int n = p.narrationHold == null ? 0 : p.narrationHold.Length;
                    if (extra > 0f && n > 0) p.narrationHold[n - 1] += extra;
                }
        }

        static string PanelPath(int i) => $"{PanelDir}/panel_{i + 1:00}.png";

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
