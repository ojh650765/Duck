using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Builds the HUD and results screen from the authored county-fair UI art.
    ///
    /// Two constraints shape all of it. The game is played over a bright saturated lawn, so every
    /// readable element sits on its own painted card or carries an outline. And the canvas is
    /// Screen Space - Camera rather than Overlay, because the capture rig renders the camera
    /// directly and an Overlay canvas would be missing from every screenshot and every review.
    /// </summary>
    public static class DuckUIBuilder
    {
        const string TexDir = "Assets/Art/Textures/UI";
        const string PartDir = "Assets/Art/Textures/Particles";

        // 9-slice borders (left, bottom, right, top) measured by the texture pass.
        static readonly Dictionary<string, Vector4> Borders = new()
        {
            { "panel_card_256", new Vector4(57, 57, 57, 61) },
            { "panel_card_dark_256", new Vector4(57, 57, 57, 61) },
            { "button_256", new Vector4(52, 44, 52, 52) },
            { "button_pressed_256", new Vector4(53, 45, 53, 53) },
            { "progress_bar_bg_256", new Vector4(24, 19, 24, 23) },
            { "progress_bar_fill_256", new Vector4(21, 17, 21, 17) },
            { "boost_gauge_256", new Vector4(45, 19, 21, 23) },
            { "minimap_frame_256", new Vector4(35, 35, 35, 39) },
            { "scorecard_blank_256", new Vector4(28, 30, 28, 42) },
            { "banner_ribbon_512", new Vector4(140, 0, 140, 0) },
        };

        [MenuItem("Duck/5 · Import UI sprites", priority = 4)]
        public static void ImportSprites()
        {
            foreach (string path in AllPngs(TexDir))
            {
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;

                string key = System.IO.Path.GetFileNameWithoutExtension(path);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.mipmapEnabled = false;
                imp.filterMode = FilterMode.Bilinear;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.alphaIsTransparency = true;
                imp.sRGBTexture = true;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.maxTextureSize = 512;
                if (Borders.TryGetValue(key, out var b)) imp.spriteBorder = b;
                imp.SaveAndReimport();
            }
            Debug.Log("[Duck] UI sprites imported.");
        }

        static List<string> AllPngs(string dir)
        {
            var list = new List<string>();
            if (!AssetDatabase.IsValidFolder(dir)) return list;
            foreach (string f in System.IO.Directory.GetFiles(dir, "*.png", System.IO.SearchOption.AllDirectories))
                list.Add(f.Replace(System.IO.Path.DirectorySeparatorChar, '/'));
            return list;
        }

        static Sprite Spr(string name)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>($"{TexDir}/{name}.png");
            if (s == null) Debug.LogWarning($"[Duck] UI sprite missing: {name}");
            return s;
        }

        // ------------------------------------------------------------------ construction

        static RectTransform Node(string name, Transform parent,
                                  Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                                  Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return rt;
        }

        static Image AddImage(RectTransform rt, Sprite sprite, Color color, Image.Type type = Image.Type.Simple)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = sprite != null && type == Image.Type.Sliced ? Image.Type.Sliced : type;
            img.raycastTarget = false;
            if (sprite == null) img.sprite = null;
            return img;
        }

        static TextMeshProUGUI AddText(RectTransform rt, string text, float size, TextAlignmentOptions align,
                                       Color color, float outline = 0.22f, bool wrap = true)
        {
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;

            // Order matters: wrapping has to be on before auto-sizing is asked to find a size,
            // or TMP measures the string as one unbroken line and happily runs it past the edge
            // of the plate. That was why the quips kept escaping their backgrounds.
            t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Truncate;
            t.fontSize = size;
            t.enableAutoSizing = true;
            t.fontSizeMax = size;
            t.fontSizeMin = Mathf.Max(9f, size * 0.4f);
            t.margin = new Vector4(10f, 6f, 10f, 6f);
            // Everything is set over bright grass, so every glyph carries its own dark edge.
            t.fontMaterial = new Material(t.fontSharedMaterial);
            t.fontMaterial.EnableKeyword("OUTLINE_ON");
            t.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, outline);
            t.fontMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.09f, 0.16f, 0.10f, 1f));
            t.fontMaterial.EnableKeyword("UNDERLAY_ON");
            t.fontMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.45f));
            t.fontMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.4f);
            t.fontMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.5f);
            t.fontMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.22f);
            t.characterSpacing = 4f;
            return t;
        }

        static readonly Color Cream = new Color(0.97f, 0.94f, 0.86f);
        static readonly Color Ink = new Color(0.16f, 0.12f, 0.09f);
        static readonly Color Gold = new Color(1f, 0.85f, 0.45f);

        public static HUD Build(Camera camera, GameDirector director, MowerController mower,
                                CutMask cutMask, RoundTarget target, JudgePanel judges)
        {
            var canvasGO = new GameObject("~ HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            // Screen Space - Camera so the capture rig, which renders the camera to a texture,
            // sees the UI. An Overlay canvas is invisible to every screenshot we take.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            canvas.sortingOrder = 100;

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var root = (RectTransform)canvasGO.transform;
            var hud = canvasGO.AddComponent<HUD>();
            hud.director = director;
            hud.mower = mower;
            hud.cutMask = cutMask;
            hud.target = target;
            hud.judges = judges;

            BuildRoundHud(root, hud);
            BuildBanner(root, hud);
            BuildTourCard(root, hud);
            BuildOutroCard(root, hud);
            BuildResults(root, hud);

            return hud;
        }

        static void BuildRoundHud(RectTransform root, HUD hud)
        {
            var group = Node("RoundHud", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            group.offsetMin = Vector2.zero; group.offsetMax = Vector2.zero;
            hud.roundGroup = group.gameObject.AddComponent<CanvasGroup>();

            // ---- timer, top centre ----
            var timer = Frac("Timer", group, 0.435f, 0.735f, 0.565f, 0.985f);
            var ring = Frac("Ring", timer, 0f, 0f, 1f, 1f);
            var ringImg = AddImage(ring, Spr("timer_ring_256"), Color.white);
            ringImg.type = Image.Type.Filled;
            ringImg.fillMethod = Image.FillMethod.Radial360;
            ringImg.fillOrigin = (int)Image.Origin360.Top;
            ringImg.fillClockwise = false;
            hud.timerRing = ringImg;

            var timerText = Frac("TimerText", timer, 0.12f, 0.3f, 0.88f, 0.72f);
            hud.timerText = AddText(timerText, "1:30", 54f, TextAlignmentOptions.Center, Cream, 0.26f, false);
            hud.timerText.fontStyle = FontStyles.Bold;

            // ---- minimap, top right ----
            var mapFrame = Frac("Minimap", group, 0.815f, 0.70f, 0.985f, 0.985f);
            var mapImg = Frac("Map", mapFrame, 0.08f, 0.08f, 0.92f, 0.92f);
            var raw = mapImg.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            var minimapShader = Shader.Find("Duck/MinimapUI");
            if (minimapShader != null)
            {
                var mat = DuckSceneBuilder.EnsureMaterial("M_MinimapUI", "Duck/MinimapUI");
                raw.material = mat;
            }
            hud.minimap = raw;
            AddImage(mapFrame, Spr("minimap_frame_256"), Color.white, Image.Type.Sliced);

            // ---- boost + coverage, bottom left ----
            var boost = Frac("Boost", group, 0.018f, 0.035f, 0.235f, 0.125f);
            var boostBg = Frac("Bg", boost, 0f, 0.02f, 1f, 0.50f);
            AddImage(boostBg, Spr("progress_bar_bg_256"), Color.white, Image.Type.Sliced);

            var boostFill = Frac("Fill", boostBg, 0.015f, 0.16f, 0.985f, 0.84f);
            var bf = AddImage(boostFill, Spr("progress_bar_fill_256"), new Color(1f, 0.78f, 0.32f), Image.Type.Filled);
            bf.type = Image.Type.Filled;
            bf.fillMethod = Image.FillMethod.Horizontal;
            hud.boostFill = bf;

            var boostLabel = Frac("Label", boost, 0.01f, 0.58f, 0.7f, 1f);
            AddText(boostLabel, "BOOST", 20f, TextAlignmentOptions.Left, Gold, 0.24f, false).fontStyle = FontStyles.Bold;

            var cover = Frac("Coverage", group, 0.018f, 0.14f, 0.235f, 0.23f);
            var coverBg = Frac("Bg", cover, 0f, 0.02f, 1f, 0.50f);
            AddImage(coverBg, Spr("progress_bar_bg_256"), Color.white, Image.Type.Sliced);

            var coverFill = Frac("Fill", coverBg, 0.015f, 0.16f, 0.985f, 0.84f);
            var cf = AddImage(coverFill, Spr("progress_bar_fill_256"), new Color(0.68f, 0.86f, 0.36f), Image.Type.Filled);
            cf.type = Image.Type.Filled;
            cf.fillMethod = Image.FillMethod.Horizontal;
            hud.coverageFill = cf;

            var coverLabel = Frac("Label", cover, 0.01f, 0.58f, 0.66f, 1f);
            AddText(coverLabel, "PICTURE FILLED", 20f, TextAlignmentOptions.Left, Cream, 0.24f, false).fontStyle = FontStyles.Bold;

            var coverPct = Frac("Pct", cover, 0.66f, 0.58f, 0.99f, 1f);
            hud.coverageText = AddText(coverPct, "0%", 22f, TextAlignmentOptions.Right, Gold, 0.24f, false);
            hud.coverageText.fontStyle = FontStyles.Bold;
        }

        /// <summary>
        /// The card that names whoever the venue tour is currently flying over.
        ///
        /// Bottom of frame, wide and shallow: the artwork it is captioning fills the middle of the
        /// screen and must not be covered. It carries the contestant, their species, their total
        /// and their grade — the same four facts the scoreboard will show, so arriving at the board
        /// confirms what you have already been told rather than introducing it.
        /// </summary>
        static void BuildTourCard(RectTransform root, HUD hud)
        {
            var card = Frac("TourCard", root, 0.24f, 0.045f, 0.76f, 0.175f);
            hud.tourGroup = card.gameObject.AddComponent<CanvasGroup>();
            hud.tourGroup.alpha = 0f;
            AddImage(card, Spr("panel_card_dark_256"), Color.white, Image.Type.Sliced);

            // The portrait. It exists because the contestants are modelled properly and the game
            // otherwise never shows them larger than a thumbnail — the tour is the one moment it
            // can say who this lawn belongs to.
            var portraitFrame = Frac("PortraitFrame", card, 0.012f, 0.06f, 0.135f, 0.94f);
            AddImage(portraitFrame, Spr("scorecard_blank_256"), Color.white, Image.Type.Sliced);
            var portrait = Frac("Portrait", portraitFrame, 0.10f, 0.09f, 0.90f, 0.91f);
            hud.tourPortrait = portrait.gameObject.AddComponent<RawImage>();
            hud.tourPortrait.raycastTarget = false;
            hud.tourPortrait.enabled = false;

            var nm = Frac("Name", card, 0.155f, 0.46f, 0.62f, 0.94f);
            hud.tourName = AddText(nm, "", 30f, TextAlignmentOptions.Left, Cream, 0.22f, false);
            hud.tourName.fontStyle = FontStyles.Bold;

            var sub = Frac("Subtitle", card, 0.155f, 0.08f, 0.62f, 0.44f);
            hud.tourSubtitle = AddText(sub, "", 18f, TextAlignmentOptions.Left,
                                       new Color(0.86f, 0.82f, 0.72f), 0.16f, false);

            var score = Frac("Score", card, 0.62f, 0.10f, 0.86f, 0.90f);
            hud.tourScore = AddText(score, "", 32f, TextAlignmentOptions.Right, Gold, 0.24f, false);
            hud.tourScore.fontStyle = FontStyles.Bold;

            var gradePlate = Frac("GradePlate", card, 0.875f, 0.14f, 0.975f, 0.86f);
            AddImage(gradePlate, Spr("scorecard_blank_256"), Color.white, Image.Type.Sliced);
            var grade = Frac("Grade", gradePlate, 0.1f, 0.08f, 0.9f, 0.92f);
            hud.tourGrade = AddText(grade, "", 34f, TextAlignmentOptions.Center, Ink, 0.12f, false);
            hud.tourGrade.fontStyle = FontStyles.Bold;
        }

        /// <summary>
        /// The closing card at the scoreboard: where you came, and what to press next.
        ///
        /// Bottom of frame and full width, because by this point the board is the picture and this
        /// is a caption under it. Without it the game ended on a wall — the results panel is hidden
        /// at the scoreboard by design, so there was nothing on screen telling the player how they
        /// had done overall or that there was any way to carry on.
        /// </summary>
        static void BuildOutroCard(RectTransform root, HUD hud)
        {
            var card = Frac("OutroCard", root, 0.20f, 0.045f, 0.80f, 0.205f);
            hud.outroGroup = card.gameObject.AddComponent<CanvasGroup>();
            hud.outroGroup.alpha = 0f;
            AddImage(card, Spr("panel_card_dark_256"), Color.white, Image.Type.Sliced);

            var placing = Frac("Placing", card, 0.04f, 0.46f, 0.96f, 0.94f);
            hud.outroPlacing = AddText(placing, "", 34f, TextAlignmentOptions.Center, Gold, 0.26f, false);
            hud.outroPlacing.fontStyle = FontStyles.Bold;

            var prompt = Frac("Prompt", card, 0.04f, 0.08f, 0.96f, 0.42f);
            hud.outroPrompt = AddText(prompt, "", 20f, TextAlignmentOptions.Center, Cream, 0.22f, false);
            hud.outroPrompt.fontStyle = FontStyles.Bold;
        }

        static void BuildBanner(RectTransform root, HUD hud)
        {
            // Anchored below centre: at 0.62 it collided with the timer ring at the top of the
            // screen and sat over the picture the player is being told to mow.
            var banner = Frac("Banner", root, 0.20f, 0.17f, 0.80f, 0.40f);
            hud.bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();

            var ribbon = Frac("Ribbon", banner, 0f, 0f, 1f, 0.76f);
            AddImage(ribbon, Spr("banner_ribbon_512"), Color.white, Image.Type.Sliced);

            // The ribbon's writable cream field is only the middle third of the sprite — the rest
            // is red stripe, tails and transparent margin. Text laid out against the whole rect
            // therefore sat well outside the part that looks like a background, which is what kept
            // making "TIME!" and the verdict line appear to escape their plate. These fractions
            // are measured off the artwork, so the text lands on the cream and nowhere else.
            var sub = Frac("Subtitle", ribbon, 0.25f, 0.40f, 0.75f, 0.68f);
            hud.bannerSubtitle = AddText(sub, "A SMILING FACE", 34f, TextAlignmentOptions.Center, new Color(0.72f, 0.20f, 0.17f), 0.16f, false);
            hud.bannerSubtitle.fontStyle = FontStyles.Bold;

            // The small label gets a plate of its own above the ribbon rather than being squeezed
            // onto the cream beside the headline; two lines never fitted that band.
            var titlePill = Frac("TitlePlate", banner, 0.29f, 0.78f, 0.71f, 1f);
            AddImage(titlePill, Spr("panel_card_dark_256"), Color.white, Image.Type.Sliced);
            var title = Frac("Title", titlePill, 0.05f, 0.14f, 0.95f, 0.86f);
            hud.bannerTitle = AddText(title, "TODAY'S SUBJECT", 22f, TextAlignmentOptions.Center, Cream, 0.14f, false);
            hud.bannerTitle.fontStyle = FontStyles.Bold;

            var count = Frac("Countdown", root, 0.34f, 0.36f, 0.66f, 0.62f);
            hud.countdownText = AddText(count, "3", 170f, TextAlignmentOptions.Center, Cream, 0.30f, false);
            hud.countdownText.fontStyle = FontStyles.Bold;
            hud.countdownText.alpha = 0f;
        }

        /// <summary>
        /// The results panel, laid out entirely in fractions of the screen: every score, name and
        /// remark on the left, the right half left clear for the verdict camera's portrait.
        ///
        /// Earlier versions positioned everything in absolute pixels against a reference
        /// resolution, and every time the canvas scale factor differed from what those numbers
        /// assumed the plates blew out to twice their intended size and the text spilled off
        /// them. Anchoring by fraction and letting the text auto-size inside removes the
        /// assumption entirely: the panel occupies the same share of the screen at any
        /// resolution or aspect.
        /// </summary>
        static void BuildResults(RectTransform root, HUD hud)
        {
            var results = Node("Results", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            results.offsetMin = Vector2.zero; results.offsetMax = Vector2.zero;
            hud.resultsGroup = results.gameObject.AddComponent<CanvasGroup>();

            // Left 38% of the screen. The right side stays clear for the duck.
            var column = Frac("Column", results, 0.025f, 0.06f, 0.40f, 0.95f);

            // Vertical bands inside the column, top to bottom.
            //   judges   0.62 -> 1.00
            //   summary  0.375 -> 0.605   (the headline: rosette, rank, total)
            //   stats    0.155 -> 0.355   (2x2, so each chip is wide enough to read)
            //   hint     0.045 -> 0.145
            string[] names = { "MILDRED", "BORIS", "PRISCILLA" };
            const float judgeTop = 1.00f, judgeBottom = 0.62f;
            float bandH = (judgeTop - judgeBottom) / 3f;

            for (int i = 0; i < 3; i++)
            {
                float top = judgeTop - i * bandH;
                var row = Frac($"Judge{i}", column, 0f, top - bandH + 0.012f, 1f, top - 0.012f);
                AddImage(row, Spr("panel_card_256"), Color.white, Image.Type.Sliced);

                var card = Frac("Card", row, 0.02f, 0.12f, 0.155f, 0.88f);
                AddImage(card, Spr("scorecard_blank_256"), Color.white, Image.Type.Sliced);

                var score = Frac("Score", card, 0.1f, 0.08f, 0.9f, 0.92f);
                hud.judgeScores[i] = AddText(score, "", 40f, TextAlignmentOptions.Center, Ink, 0.10f, false);
                hud.judgeScores[i].fontStyle = FontStyles.Bold;

                var nm = Frac("Name", row, 0.185f, 0.56f, 0.97f, 0.94f);
                hud.judgeNames[i] = AddText(nm, names[i], 22f, TextAlignmentOptions.Left,
                                            new Color(0.62f, 0.26f, 0.18f), 0.10f, false);
                hud.judgeNames[i].fontStyle = FontStyles.Bold;

                var quip = Frac("Quip", row, 0.185f, 0.08f, 0.97f, 0.54f);
                hud.judgeQuips[i] = AddText(quip, "", 18f, TextAlignmentOptions.TopLeft, Ink, 0.06f);
            }

            // Summary: rosette, rank, running total. This is the line the player actually came for,
            // so it gets the tallest band and the biggest type on the screen.
            var summary = Frac("Summary", column, 0f, 0.375f, 1f, 0.605f);
            AddImage(summary, Spr("panel_card_dark_256"), Color.white, Image.Type.Sliced);

            var rosette = Frac("Rosette", summary, 0.025f, 0.07f, 0.255f, 0.93f);
            hud.resultsRosette = AddImage(rosette, null, Color.white);
            hud.resultsRosette.preserveAspect = true;
            hud.resultsRosette.enabled = false;
            hud.rosetteByRank = new[]
            {
                Spr("rosette_S_256"), Spr("rosette_A_256"), Spr("rosette_B_256"),
                Spr("rosette_C_256"), Spr("rosette_D_256")
            };

            var rank = Frac("Rank", summary, 0.28f, 0.44f, 0.98f, 0.95f);
            hud.resultsRank = AddText(rank, "", 96f, TextAlignmentOptions.Left, Gold, 0.28f, false);
            hud.resultsRank.fontStyle = FontStyles.Bold;

            var total = Frac("Total", summary, 0.28f, 0.06f, 0.98f, 0.42f);
            hud.resultsTotal = AddText(total, "", 40f, TextAlignmentOptions.Left, Cream, 0.24f, false);
            hud.resultsTotal.fontStyle = FontStyles.Bold;

            // Breakdown chips, two by two. Four across the column left each one about a hundred
            // pixels wide, which forced the label down to a size nobody was going to read; half as
            // many per row doubles the width and lets the label sit beside the number instead of
            // above it, so both can be large.
            string[] statLabels = { "FILLED", "SPILLED", "EDGE", "STYLE" };
            var stats = new TextMeshProUGUI[4];
            const float statTop = 0.355f, statBottom = 0.155f;
            float statRowH = (statTop - statBottom) * 0.5f;

            for (int i = 0; i < 4; i++)
            {
                int col = i % 2, rowIdx = i / 2;
                float x0 = col * 0.5f, x1 = x0 + 0.5f;
                float y1 = statTop - rowIdx * statRowH;
                var cell = Frac($"Stat{i}", column, x0 + 0.006f, y1 - statRowH + 0.008f, x1 - 0.006f, y1 - 0.008f);
                AddImage(cell, Spr("panel_card_dark_256"), Color.white, Image.Type.Sliced);

                var lab = Frac("Label", cell, 0.06f, 0.12f, 0.58f, 0.88f);
                AddText(lab, statLabels[i], 22f, TextAlignmentOptions.Left,
                        new Color(0.97f, 0.94f, 0.86f), 0.16f, false).fontStyle = FontStyles.Bold;

                var val = Frac("Value", cell, 0.58f, 0.12f, 0.94f, 0.88f);
                stats[i] = AddText(val, "0%", 34f, TextAlignmentOptions.Right, Gold, 0.24f, false);
                stats[i].fontStyle = FontStyles.Bold;
            }
            hud.coverageStat = stats[0];
            hud.spillStat = stats[1];
            hud.edgeStat = stats[2];
            hud.styleStat = stats[3];

            var hintPlate = Frac("RetryPlate", column, 0f, 0.045f, 1f, 0.145f);
            AddImage(hintPlate, Spr("panel_card_dark_256"), Color.white, Image.Type.Sliced);
            var hint = Frac("RetryHint", hintPlate, 0.03f, 0.12f, 0.97f, 0.88f);
            hud.retryHint = AddText(hint, "", 20f, TextAlignmentOptions.Center, Cream, 0.22f, false);
            hud.retryHint.fontStyle = FontStyles.Bold;
        }

        /// <summary>
        /// A child anchored purely by fraction of its parent, with no pixel offsets at all.
        /// This is the only positioning primitive the results screen uses, so nothing in it can
        /// depend on the canvas scale factor being what we guessed.
        /// </summary>
        static RectTransform Frac(string name, Transform parent, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }
    }
}
