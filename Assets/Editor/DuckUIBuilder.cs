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

        // The 9-slice borders this importer writes now live in DuckMow.UI.CardArt, alongside the
        // measurement of where each sprite's DECORATION ends. They were two facts about the same
        // artwork kept in two places, and the layout needs both together: Unity shrinks a slice
        // border when the rect is too small to hold it, and the painting inside that border shrinks
        // with it, so a padding rule that could not see the border could not be right on a small
        // card. See CardArt, which is also where the difference between the two numbers — 57 for
        // the corner, 32 for the paint — is written down.

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
                if (DuckMow.UI.CardArt.HasBorder(key)) imp.spriteBorder = DuckMow.UI.CardArt.Border(key);
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

        /// <summary>Internal rather than private: DuckCutsceneBuilder builds its storybook page from
        /// the same four primitives, and the one thing worse than sharing them is a second set of
        /// slightly different ones drifting apart from these.</summary>
        internal static Sprite Spr(string name)
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

        // The three painted plates everything in this game is built out of. Named rather than typed
        // as literals at each call site, because a card's sprite and its padding have to be the same
        // sprite and a typo in one of two strings is exactly the drift Plate exists to prevent.
        internal const string CardDark = "panel_card_dark_256";
        internal const string CardLight = "panel_card_256";
        internal const string ScoreCard = "scorecard_blank_256";

        /// <summary>
        /// Paint a nine-sliced card onto <paramref name="rt"/> and hand back the part of it that may
        /// be printed on.
        ///
        /// ONE CALL, because the two things are one decision. Laying the plate down and working out
        /// how far in from its edge the paint stops were separate before, and every layout in the
        /// game got the second one wrong — see <see cref="DuckMow.UI.CardArt"/>, which has the
        /// measurements and the argument. A helper that returns the field makes the correct thing
        /// the easy thing: fractions inside the returned rect are bands of the writable card, which
        /// is what an author means by them, and there is no number for anybody to know.
        ///
        /// The plate is returned through <paramref name="plate"/> for the callers that animate it.
        /// </summary>
        internal static RectTransform Plate(RectTransform rt, string sprite, out Image plate,
                                            Color? tint = null)
        {
            plate = AddImage(rt, Spr(sprite), tint ?? Color.white, Image.Type.Sliced);
            return DuckMow.UI.CardArt.Inside("Field", rt, sprite);
        }

        /// <summary>The common case: a plate nobody needs a handle on.</summary>
        internal static RectTransform Plate(RectTransform rt, string sprite, Color? tint = null)
            => Plate(rt, sprite, out _, tint);

        internal static Image AddImage(RectTransform rt, Sprite sprite, Color color, Image.Type type = Image.Type.Simple)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = sprite != null && type == Image.Type.Sliced ? Image.Type.Sliced : type;
            img.raycastTarget = false;
            if (sprite == null) img.sprite = null;
            return img;
        }

        internal static TextMeshProUGUI AddText(RectTransform rt, string text, float size, TextAlignmentOptions align,
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
            EnsureFont(t);
            if (t.fontSharedMaterial == null)
            {
                t.characterSpacing = 4f;
                return t;      // readable, just unoutlined — better than aborting the build
            }
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

        /// <summary>
        /// The font asset a fresh TMP component will draw with, resolved eagerly.
        ///
        /// AddText builds its outline material from t.fontSharedMaterial, and that is null until
        /// TMP has a font. In the HUD path it always did, because those components are added under
        /// a canvas that has already been through TMP's own initialisation. Building a second scene
        /// from scratch hit it cold: fontSharedMaterial came back null, `new Material(null)` threw,
        /// and the exception took the whole content rebuild down with it — the menu, the main scene
        /// and every asset after it — from one text box on a controls card.
        ///
        /// Asserted here rather than guarded at the call site, because every future caller would
        /// have the same problem and none of them would expect a text label to be able to fail.
        /// </summary>
        static void EnsureFont(TMP_Text t)
        {
            if (t.font != null) return;
            t.font = TMP_Settings.defaultFontAsset;
            if (t.font == null)
                Debug.LogWarning("[Duck] No TMP default font asset; run 'Duck/1 · Import TMP Essentials'. " +
                                 "Text will build without its outline.");
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
            BuildCeremonyCard(root, hud);
            BuildResults(root, hud);

            return hud;
        }

        // There is no championship standing card. One was built here — a top-left panel carrying
        // "ROUND 2 OF 3", the points table and the placing needed to take the title — and it was cut
        // after a playthrough. It was meant to make the goal obvious and did the opposite: it read as
        // chrome, and the player who saw it still had to ask what the format was.
        //
        // If the structure ever needs stating again, it does not want another panel in a corner. The
        // only slots the player actually reads are the briefing ribbon and the line above the keys on
        // the closing card, both of which are already on screen at the moments that matter.

        /// <summary>
        /// The closing title card, laid out as a television lower third rather than as a full-screen
        /// panel.
        ///
        /// The ceremony's last beat is a portrait of the duck who just won, and the whole reason that
        /// framing exists is that it holds a character in shot. Covering it with a modal card would
        /// throw away the payoff in order to announce it.
        /// </summary>
        static void BuildCeremonyCard(RectTransform root, HUD hud)
        {
            var card = Frac("CeremonyCard", root, 0.13f, 0.075f, 0.87f, 0.325f);
            hud.ceremonyGroup = card.gameObject.AddComponent<CanvasGroup>();
            hud.ceremonyGroup.alpha = 0f;
            var field = Plate(card, CardDark);

            var rosette = Frac("Rosette", field, 0f, 0.02f, 0.135f, 0.98f);
            hud.ceremonyRosette = AddImage(rosette, null, Color.white);
            hud.ceremonyRosette.preserveAspect = true;
            hud.ceremonyRosette.enabled = false;

            var title = Frac("Title", field, 0.165f, 0.50f, 1f, 1f);
            hud.ceremonyTitle = AddText(title, "", 62f, TextAlignmentOptions.Left, Gold, 0.30f, false);
            hud.ceremonyTitle.fontStyle = FontStyles.Bold;

            var sub = Frac("Subtitle", field, 0.165f, 0.23f, 1f, 0.48f);
            hud.ceremonySubtitle = AddText(sub, "", 24f, TextAlignmentOptions.Left, Cream, 0.22f, false);
            hud.ceremonySubtitle.fontStyle = FontStyles.Bold;

            var prompt = Frac("Prompt", field, 0.165f, 0f, 1f, 0.21f);
            hud.ceremonyPrompt = AddText(prompt, "", 20f, TextAlignmentOptions.Left,
                                         new Color(0.86f, 0.82f, 0.72f), 0.20f, false);
            hud.ceremonyPrompt.fontStyle = FontStyles.Bold;
            hud.ceremonyPrompt.alpha = 0f;
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

            // ---- the entry card, top right ----
            //
            // This slot used to hold a live minimap that drew the target outline, the cut mask, the
            // spill and the mower's position together. With the ground guide now dissolving a third
            // of the way into the round, that was the answer key sitting in the corner of the
            // screen — everything the round takes away, handed straight back.
            //
            // What sits here now is the picture and nothing else, drawn as ink on paper. The paper
            // look is doing real work: an inset panel that resembles a display invites the player
            // to keep checking it for position, whereas a pinned sheet reads as a reference and
            // gets glanced at once.
            //
            // ---- and it comes DOWN the frame to leave the corner to the pause plate ----
            //
            // The card used to sit flush with the top edge at 0.985, and the plate that opens the
            // pause board was pushed inboard to a 0.795 anchor to pass beside it. That cleared the
            // collision and read badly: the only pressable thing on the screen floating in the
            // middle of the top edge with an empty corner next to it, which is not where anybody
            // looks for a pause button. The plate has the corner now and the card gives way, which
            // is the right way round — the card is a reference glanced at once, the plate is the
            // one control, and it is in the same corner on all three stages.
            //
            // The two are separated VERTICALLY, and that is what makes it safe at every aspect
            // ratio. This canvas matches on HEIGHT, so a fraction of the height is a fixed number of
            // reference pixels whatever shape the window is, while a fraction of the WIDTH walks as
            // the window is dragged — the horizontal separation this replaces would have had to be
            // re-argued for every window shape a browser can be dragged into.
            //
            // The clearance is read off PauseButton rather than copied, so resizing the plate moves
            // the card rather than quietly overlapping it. Card and aerial row shift by the SAME
            // amount, so the pair still reads as one pinned block, and the card keeps its exact
            // size and its left edge.
            const float RefHeight = 1080f;      // the CanvasScaler's reference height, above
            const float PlateGap = 16f;         // reference pixels of daylight under the plate
            const float WasTop = 0.985f;        // where the card used to be pinned
            float cardTop = 1f - (DuckMow.UI.PauseButton.ReservedTop + PlateGap) / RefHeight;
            float drop = WasTop - cardTop;

            var cardFrame = Frac("ShapeCard", group, 0.815f, 0.70f - drop, 0.985f, cardTop);
            var cardImg = Frac("Card", cardFrame, 0.08f, 0.08f, 0.92f, 0.92f);
            var raw = cardImg.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            if (Shader.Find("Duck/ShapeCardUI") != null)
            {
                var cardMat = DuckSceneBuilder.EnsureMaterial("M_ShapeCardUI", "Duck/ShapeCardUI");
                // Written here rather than left to the shader's property defaults.
                // EnsureMaterial reuses an existing .mat if one is on disk, and a material already
                // carries its own serialized copy of every property — so editing a default in the
                // shader changes nothing on any machine that has built this scene before, which is
                // every machine that matters.
                //
                // The fill is deliberately deep. At the pale value it started with, the card read
                // as a faint smudge on cream paper from across the room and the shape had to be
                // hunted for; the whole point of it is to be legible in the corner of the eye
                // while the player is looking at grass.
                cardMat.SetColor("_InkFill", new Color(0.30f, 0.25f, 0.16f));
                cardMat.SetColor("_Ink", new Color(0.15f, 0.11f, 0.07f));
                cardMat.SetFloat("_FillAmount", 1f);
                cardMat.SetFloat("_StrokeWidth", 0f);
                EditorUtility.SetDirty(cardMat);
                raw.material = cardMat;
            }
            hud.shapeCard = raw;
            AddImage(cardFrame, Spr("minimap_frame_256"), Color.white, Image.Type.Sliced);

            // ---- aerial check token, under the card ----
            //
            // Follows the card by the same drop, so the two stay one pinned block rather than the
            // card moving and its footnote staying behind.
            var aerial = Frac("AerialCheck", group, 0.815f, 0.628f - drop, 0.985f, 0.694f - drop);
            var token = Frac("Token", aerial, 0.02f, 0.05f, 0.30f, 0.95f);
            hud.aerialToken = AddImage(token, Spr("icon_coverage_128"), new Color(1f, 0.86f, 0.44f), Image.Type.Simple);
            var aerialHint = Frac("Hint", aerial, 0.34f, 0.05f, 1f, 0.95f);
            hud.aerialHint = AddText(aerialHint, "[F]  LOOK", 20f, TextAlignmentOptions.Left, Gold, 0.24f, false);
            hud.aerialHint.fontStyle = FontStyles.Bold;

            // ---- boost, bottom left ----
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

            // Nothing else goes in this corner. There was a "MOWN 81 m²" readout above the gauge and
            // it is gone, because it was never actionable: a player cannot tell whether 81 m² is good
            // or bad, and it only ever existed to fill the hole left by an earlier "PICTURE FILLED
            // 41%" that had to go for a much better reason (it measured cut cells against the hidden
            // target, so the shape could be solved by driving around watching a number move).
            //
            // The rule for this HUD is three things: the clock, the picture, the dash gauge. Anything
            // proposed for it has to beat that list, and a raw area figure does not.
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
            var field = Plate(card, CardDark);

            // The portrait. It exists because the contestants are modelled properly and the game
            // otherwise never shows them larger than a thumbnail — the tour is the one moment it
            // can say who this lawn belongs to.
            //
            // It is noticeably smaller than it was, and that is the fix rather than a cost: on a
            // 140 px card the painted rule sits 32 px up from the bottom and 26 down from the top,
            // and the frame used to be laid out to 0.06..0.94 of the whole card — 124 px of a 140 px
            // plate — so it was drawn straight through both rules. What is left is the part of the
            // card that was ever available.
            var portraitFrame = Frac("PortraitFrame", field, 0f, 0f, 0.127f, 1f);
            AddImage(portraitFrame, Spr(ScoreCard), Color.white, Image.Type.Sliced);
            var portrait = Frac("Portrait", portraitFrame, 0.10f, 0.09f, 0.90f, 0.91f);
            hud.tourPortrait = portrait.gameObject.AddComponent<RawImage>();
            hud.tourPortrait.raycastTarget = false;
            hud.tourPortrait.enabled = false;

            var nm = Frac("Name", field, 0.148f, 0.46f, 0.63f, 1f);
            hud.tourName = AddText(nm, "", 30f, TextAlignmentOptions.Left, Cream, 0.22f, false);
            hud.tourName.fontStyle = FontStyles.Bold;

            var sub = Frac("Subtitle", field, 0.148f, 0f, 0.63f, 0.44f);
            hud.tourSubtitle = AddText(sub, "", 18f, TextAlignmentOptions.Left,
                                       new Color(0.86f, 0.82f, 0.72f), 0.16f, false);

            var score = Frac("Score", field, 0.63f, 0.02f, 0.86f, 0.98f);
            hud.tourScore = AddText(score, "", 32f, TextAlignmentOptions.Right, Gold, 0.24f, false);
            hud.tourScore.fontStyle = FontStyles.Bold;

            var gradePlate = Frac("GradePlate", field, 0.878f, 0f, 1f, 1f);
            AddImage(gradePlate, Spr(ScoreCard), Color.white, Image.Type.Sliced);
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
            // Taller than it was, by a hundredth of the frame, and the extra goes to the PROMPT.
            //
            // This card carries two beats and the prompt is the only thing on screen in one of them:
            // during the reveal the placing line is deliberately blank — see HUD.UpdateOutro, which
            // clears it whenever the player is not at the board — so the card is one instruction on
            // a plank over a finished picture. That instruction was set at 20, which is the size
            // this HUD uses for FOOTNOTES: the aerial hint, the stat labels, the quiet line along
            // the bottom of the pause board. It is not a footnote. It is the thing the player is
            // waiting on, over a busy ground, read from a sitting distance, and it was smaller than
            // the word FILLED on a stat chip.
            //
            // 30 rather than 34, which is the placing's size: at the board the two are on screen
            // together and an instruction that matches the result in weight competes with it. Below
            // the result, above every label — which is where an instruction belongs.
            var card = Frac("OutroCard", root, 0.20f, 0.045f, 0.80f, 0.215f);
            hud.outroGroup = card.gameObject.AddComponent<CanvasGroup>();
            hud.outroGroup.alpha = 0f;
            var field = Plate(card, CardDark);

            // The card grew so this could, without taking the placing's room: the field is 124 px
            // where it was 113, and the split moved from half-and-half to 0.53, so both bands come
            // out at 58 px. The placing keeps more height than it had, and the prompt gains eleven.
            var placing = Frac("Placing", field, 0f, 0.53f, 1f, 1f);
            hud.outroPlacing = AddText(placing, "", 34f, TextAlignmentOptions.Center, Gold, 0.26f, false);
            hud.outroPlacing.fontStyle = FontStyles.Bold;

            var prompt = Frac("Prompt", field, 0f, 0f, 1f, 0.47f);
            hud.outroPrompt = AddText(prompt, "", 30f, TextAlignmentOptions.Center, Cream, 0.22f, false);
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
            var titleField = Plate(titlePill, CardDark);
            var title = Frac("Title", titleField, 0f, 0f, 1f, 1f);
            hud.bannerTitle = AddText(title, "TODAY'S SUBJECT", 22f, TextAlignmentOptions.Center, Cream, 0.14f, false);
            hud.bannerTitle.fontStyle = FontStyles.Bold;

            // A third plate, below the ribbon, for what this round is worth.
            //
            // This is where the championship states itself, and the only place it does during play.
            // It belongs on the banner rather than on a panel of its own precisely because the banner
            // LEAVES: the player is told what the round needs at the moment they are deciding how to
            // mow it, and then the screen is clear again. The version of this that lived in a
            // permanent top-left card was cut for being chrome, and the difference between the two is
            // entirely that this one goes away.
            //
            // Anchored on negative fractions so it hangs below the parent while still belonging to the
            // banner's CanvasGroup, which is what makes it fade in and out on the same beat.
            var goalPlate = Frac("GoalPlate", banner, 0.14f, -0.30f, 0.86f, -0.04f);
            // Its own group inside the banner's. The banner is also used for "STUDY IT", "TIME!" and
            // the guide-lost beat, none of which have a goal line — and without this the plate would
            // still be hanging under the ribbon on all of them, empty. Nested alphas multiply, so it
            // both fades with the banner and vanishes when there is nothing to say.
            hud.bannerGoalGroup = goalPlate.gameObject.AddComponent<CanvasGroup>();
            hud.bannerGoalGroup.alpha = 0f;
            var goalField = Plate(goalPlate, CardDark);
            var goal = Frac("Goal", goalField, 0f, 0f, 1f, 1f);
            // Wrapping on: the round-one line carries the scoring rule under it, and the tightest
            // final-round requirement — "WIN, AND HORACE MUST FINISH 3RD OR WORSE" — needs to be
            // allowed to break rather than shrink away to nothing.
            hud.bannerGoal = AddText(goal, "", 22f, TextAlignmentOptions.Center, Gold, 0.22f);
            hud.bannerGoal.fontStyle = FontStyles.Bold;

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
                var rowField = Plate(row, CardLight);

                // The score plate stays measured against the ROW, not its field, and that is the one
                // distinction this whole sweep turns on: OPAQUE FURNITURE MAY SIT ON THE FRAME,
                // TEXT MAY NOT. A card pinned over the row's painted rule hides it and reads as
                // layering; a letter crossed by it reads as a defect. The livery stripes and the
                // rally's horn meters are the same case.
                //
                // It is also the one plate in the game whose sprite does not fit its box, which is a
                // separate fault and is left standing deliberately. scorecard_blank is a PORTRAIT
                // card with a solid red band across the top and the bottom; at the width this row
                // can spare it comes out landscape and about 77 px tall, and the two bands alone
                // want 108. The number is therefore printed over red rather than on the cream, and
                // no padding rule can fix that — the plate needs to be taller or the sprite needs to
                // be a different one, and both are composition calls that want a frame in front of
                // them rather than a builder edit made blind.
                var card = Frac("Card", row, 0.02f, 0.12f, 0.155f, 0.88f);
                AddImage(card, Spr(ScoreCard), Color.white, Image.Type.Sliced);

                var score = Frac("Score", card, 0.1f, 0.08f, 0.9f, 0.92f);
                hud.judgeScores[i] = AddText(score, "", 40f, TextAlignmentOptions.Center, Ink, 0.10f, false);
                hud.judgeScores[i].fontStyle = FontStyles.Bold;

                var nm = Frac("Name", rowField, 0.175f, 0.55f, 1f, 1f);
                hud.judgeNames[i] = AddText(nm, names[i], 22f, TextAlignmentOptions.Left,
                                            new Color(0.62f, 0.26f, 0.18f), 0.10f, false);
                hud.judgeNames[i].fontStyle = FontStyles.Bold;

                var quip = Frac("Quip", rowField, 0.175f, 0f, 1f, 0.52f);
                hud.judgeQuips[i] = AddText(quip, "", 18f, TextAlignmentOptions.TopLeft, Ink, 0.06f);
            }

            // Summary: rosette, rank, running total. This is the line the player actually came for,
            // so it gets the tallest band and the biggest type on the screen.
            var summary = Frac("Summary", column, 0f, 0.375f, 1f, 0.605f);
            var summaryField = Plate(summary, CardDark);

            var rosette = Frac("Rosette", summaryField, 0f, 0f, 0.245f, 1f);
            hud.resultsRosette = AddImage(rosette, null, Color.white);
            hud.resultsRosette.preserveAspect = true;
            hud.resultsRosette.enabled = false;
            hud.rosetteByRank = new[]
            {
                Spr("rosette_S_256"), Spr("rosette_A_256"), Spr("rosette_B_256"),
                Spr("rosette_C_256"), Spr("rosette_D_256")
            };

            var rank = Frac("Rank", summaryField, 0.275f, 0.44f, 1f, 1f);
            hud.resultsRank = AddText(rank, "", 96f, TextAlignmentOptions.Left, Gold, 0.28f, false);
            hud.resultsRank.fontStyle = FontStyles.Bold;

            var total = Frac("Total", summaryField, 0.275f, 0f, 1f, 0.42f);
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
                // The worst of them, and the reason a fraction was never going to work here: at
                // 351x81 the old 0.06 of the chip was twenty pixels against a thirty pixel rule, and
                // 0.12 of its height was ten against twenty-two. The chip is the smallest card on
                // the screen and so had the least padding, which is exactly backwards.
                var cellField = Plate(cell, CardDark);

                var lab = Frac("Label", cellField, 0f, 0f, 0.56f, 1f);
                AddText(lab, statLabels[i], 22f, TextAlignmentOptions.Left,
                        new Color(0.97f, 0.94f, 0.86f), 0.16f, false).fontStyle = FontStyles.Bold;

                var val = Frac("Value", cellField, 0.56f, 0f, 1f, 1f);
                stats[i] = AddText(val, "0%", 34f, TextAlignmentOptions.Right, Gold, 0.24f, false);
                stats[i].fontStyle = FontStyles.Bold;
            }
            hud.coverageStat = stats[0];
            hud.spillStat = stats[1];
            hud.edgeStat = stats[2];
            hud.styleStat = stats[3];

            var hintPlate = Frac("RetryPlate", column, 0f, 0.045f, 1f, 0.145f);
            var hintField = Plate(hintPlate, CardDark);
            var hint = Frac("RetryHint", hintField, 0f, 0f, 1f, 1f);
            hud.retryHint = AddText(hint, "", 20f, TextAlignmentOptions.Center, Cream, 0.22f, false);
            hud.retryHint.fontStyle = FontStyles.Bold;
        }

        /// <summary>
        /// A child anchored purely by fraction of its parent, with no pixel offsets at all.
        /// This is the only positioning primitive the results screen uses, so nothing in it can
        /// depend on the canvas scale factor being what we guessed.
        /// </summary>
        internal static RectTransform Frac(string name, Transform parent, float x0, float y0, float x1, float y1)
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
