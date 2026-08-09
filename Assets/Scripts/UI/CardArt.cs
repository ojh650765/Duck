using System.Collections.Generic;
using UnityEngine;

namespace DuckMow.UI
{
    /// <summary>
    /// WHAT THE PAINTED CARDS LEAVE YOU ROOM FOR.
    ///
    /// Every plate in this game is a nine-sliced painting rather than a box: a rounded card with a
    /// decorative rule inset from its edge, studs at the corners, and a field in the middle that is
    /// the only part anything may be printed on. Text laid out to the rect's true bounds sits on
    /// the rule, and that is not a near miss — it is a line drawn through the glyphs.
    ///
    /// ---- why this could not be left to each layout ----
    ///
    /// It was, and every one of them got it wrong. The results panel inset its stat chips by 0.06
    /// of the chip; the rally's cards were hand-tuned to 0.06..0.93 with a comment explaining this
    /// exact problem; Bloom Rush's title plate used 0.04. Those are FRACTIONS and the decoration is
    /// a fixed number of PIXELS, so the same fraction is generous on a big card and nothing at all
    /// on a small one — the smaller the plate, the worse it gets, which is why the short chips look
    /// worst. Not one of the hand-tuned numbers actually cleared the rule; the rally's, which was
    /// written by somebody who had diagnosed the problem, was 20 px against a 30 px margin.
    ///
    /// So the number is stated once, per sprite, in pixels, and asked for rather than guessed.
    ///
    /// ---- and why it is NOT the nine-slice border ----
    ///
    /// The obvious answer is "inset by the sprite's border", and it is wrong by a factor of two.
    /// The border is cut so the CORNERS survive stretching, so it has to reach past the rounded
    /// corner and the corner studs: panel_card_dark_256 declares 57/57/57/61 while its rule sits at
    /// 20..23 px and its field is clean by 32. Insetting by 57 would put a 118 px hole in the
    /// vertical of every card, and five of the eight cards on the lawn's results panel are shorter
    /// than 118 px in total. The two numbers measure different things and only one of them is about
    /// where the paint is.
    ///
    /// ---- how the numbers below were arrived at ----
    ///
    /// Measured off the PNGs rather than eyeballed off a mock-up: walk in from each edge along the
    /// sprite's centre line and find the last run of samples that differs from the flat field
    /// colour by more than the field's own grain. These cards are painted with visible paper
    /// texture, so a fixed tolerance reports the grain as decoration and lands half way across the
    /// sprite; the tolerance has to be calibrated to the noise, and the reading has to want a run
    /// rather than one stray pixel. The numbers were then cross-checked by hand against a colour
    /// profile of each edge.
    /// </summary>
    public static class CardArt
    {
        /// <summary>
        /// Daylight between the decoration and the first glyph, in reference pixels.
        ///
        /// Deliberately separate from the measurements below, which are statements about the ART
        /// and should not be edited to taste. This one is taste: it is how close a letter may come
        /// to a line before the two read as touching.
        /// </summary>
        public const float Breath = 6f;

        /// <summary>
        /// Never take more than this share of a card for its own frame.
        ///
        /// A guard, and one that fires: the lawn's judge score plates are 77 px tall carrying a
        /// sprite whose red header and footer alone want 108. Clamping keeps the layout buildable
        /// and the WARNING is the actual output — a plate too small to hold its own artwork is a
        /// composition fault that has to be looked at, not a padding fault that can be computed
        /// away.
        /// </summary>
        const float MaxFrameShare = 0.75f;

        /// <summary>
        /// The nine-slice borders the importer writes onto the sprites, as (left, bottom, right,
        /// top) in the sprite's own pixels.
        ///
        /// Lives here rather than in the importer that consumes it because it is a fact about the
        /// ARTWORK, and because <see cref="Margin"/> needs it: Unity shrinks a slice border when
        /// the rect is too small to hold it, and the decoration inside that border shrinks with it.
        /// One table, read by the importer and by the layout, cannot drift from itself.
        /// </summary>
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

        /// <summary>
        /// How far in from each edge the DECORATION goes, as (left, bottom, right, top) in the
        /// sprite's own pixels. Contents start after this; see the class note for the method.
        ///
        /// Only the sprites that are used as backgrounds for something are here. A sprite asked for
        /// that is not in this table is answered with its nine-slice border and a warning, which is
        /// the over-inset direction — a card that suddenly has too much padding gets noticed and
        /// measured, whereas one with too little goes on quietly printing on its own frame, which is
        /// the fault this class exists to end.
        /// </summary>
        /// <remarks>
        /// The number wanted here is the RULE'S INNER EDGE, not the point at which the paint becomes
        /// perfectly flat field. Those are ten pixels apart on these cards and picking the wrong one
        /// matters: text does not need clean paper under it, it needs not to have a line drawn
        /// through it, and on a plate whose whole height is corner slice ten pixels is a third of
        /// everything there is.
        /// </remarks>
        static readonly Dictionary<string, Vector4> Paints = new()
        {
            // The same painting in two colourways, measured separately because the rule is red on
            // one and cream on the other and they do not sit at quite the same depth. Dark: the rule
            // occupies 20..23 in from the sides, 18..21 from the top and 23..25 from the bottom.
            // Light: 20..22, 17..19 and 25..27.
            { "panel_card_256", new Vector4(23, 28, 23, 20) },
            { "panel_card_dark_256", new Vector4(24, 26, 24, 22) },

            // A different shape of problem: this one is a portrait score card with a solid RED BAND
            // across the top and the bottom and a rule inside the cream between them. The bands run
            // to 34 and the rule sits just inside them, so the vertical margins are more than twice
            // the horizontal ones. Anything drawn on this sprite at less than about 140 px tall has
            // nowhere to put a glyph at all — see MaxFrameShare, which fires on exactly these.
            { "scorecard_blank_256", new Vector4(28, 52, 28, 50) },

            // A frame with nothing in the middle: the "decoration" is the whole of it, and what is
            // being asked for is the aperture.
            { "minimap_frame_256", new Vector4(30, 34, 30, 28) },
        };

        /// <summary>The nine-slice border for a sprite, or zero if it is not one of ours.</summary>
        public static Vector4 Border(string sprite)
            => sprite != null && Borders.TryGetValue(sprite, out var b) ? b : Vector4.zero;

        /// <summary>True when the importer has a border to write for this sprite.</summary>
        public static bool HasBorder(string sprite) => sprite != null && Borders.ContainsKey(sprite);

        /// <summary>
        /// The margin a card of this size has to keep clear on each side, in the canvas's own
        /// reference pixels, as (left, bottom, right, top).
        ///
        /// ---- the slice shrink, which is the whole reason this takes a size ----
        ///
        /// A sliced Image does not draw its border at native size when the rect is too small to
        /// hold it: Unity scales both borders on that axis down until they exactly fill the rect
        /// (Image.GetAdjustedBorders). The corner slices are then drawing artwork that was N pixels
        /// tall into a shorter space, so everything painted inside them — the rule, the studs, the
        /// bands — comes down by the same ratio.
        ///
        /// That is not an inconvenience to be worked around, it is the thing that makes the short
        /// plates work at all. Five of the eight cards on the lawn's results panel are shorter than
        /// their own 118 px vertical border, and on those the decoration really is drawn nearer the
        /// edge than the measurement says. A padding rule that ignored this would inset a 81 px
        /// chip by 32 top and bottom and leave seventeen pixels for a number.
        /// </summary>
        public static Vector4 Margin(string sprite, Vector2 cardSize, string cardName = null)
        {
            Vector4 border = Border(sprite);
            Vector4 paint = Vector4.zero;
            bool measured = sprite != null && Paints.TryGetValue(sprite, out paint);
            if (!measured)
            {
                paint = border;
                Debug.LogWarning($"[CardArt] no painted margin measured for '{sprite}'" +
                                 $"{(cardName != null ? $" (asked for by '{cardName}')" : "")}; " +
                                 "falling back to its nine-slice border, which is a corner-safety " +
                                 "number and will over-inset. Measure the sprite and add it to " +
                                 "CardArt.Paints.");
            }

            // Unity's own rule, reproduced: shrink both borders on an axis until they fit.
            float bx = border.x + border.z, by = border.y + border.w;
            float sx = bx > 0f && cardSize.x < bx ? cardSize.x / bx : 1f;
            float sy = by > 0f && cardSize.y < by ? cardSize.y / by : 1f;

            // The DAYLIGHT comes down with the ratio too, and that is deliberate rather than a
            // convenience. On a plate so short that the whole of it is corner slice, the rule itself
            // is drawn thinner and lighter along with everything else in that slice; six flat pixels
            // of clearance beside a squashed hairline is proportionally far more room than six
            // beside a full-weight one, and on a 55 px plate it is most of the writable height.
            var m = new Vector4((paint.x + Breath) * sx, (paint.y + Breath) * sy,
                                (paint.z + Breath) * sx, (paint.w + Breath) * sy);

            m = Clamp(m, cardSize, sprite, cardName);
            return m;
        }

        /// <summary>
        /// Keep the frame from eating the card, and say so when it tries.
        ///
        /// The warning names the card, its size and what it wanted, because the fix for this is
        /// never in this file — it is a plate that needs to be bigger or a sprite that is the wrong
        /// one for that plate, and both of those are decisions somebody has to look at a frame to
        /// take.
        /// </summary>
        static Vector4 Clamp(Vector4 m, Vector2 size, string sprite, string cardName)
        {
            for (int axis = 0; axis < 2; axis++)
            {
                float lo = axis == 0 ? m.x : m.y;
                float hi = axis == 0 ? m.z : m.w;
                float extent = axis == 0 ? size.x : size.y;
                float sum = lo + hi;
                if (extent <= 0f || sum <= extent * MaxFrameShare) continue;

                float k = extent * MaxFrameShare / sum;
                if (axis == 0) { m.x *= k; m.z *= k; }
                else { m.y *= k; m.w *= k; }

                Debug.LogWarning(
                    $"[CardArt] '{cardName ?? "a card"}' is {size.x:0}x{size.y:0} and '{sprite}' " +
                    $"wants {sum:0} px of frame on its {(axis == 0 ? "width" : "height")} — more " +
                    $"than {MaxFrameShare:P0} of it. The margin has been clamped so the card still " +
                    "builds, but the contents WILL sit on the decoration. Make the plate bigger or " +
                    "give it a sprite whose painting fits it.");
            }
            return m;
        }

        /// <summary>
        /// A child of <paramref name="card"/> covering exactly the part of it that may be printed
        /// on. Lay the card's contents out inside this rather than inside the card.
        ///
        /// This is the whole interface, and it is deliberately the ONLY thing a layout has to know:
        /// no pixel numbers at the call site, no fractions chosen to approximate a margin, and no
        /// way for a card to be made smaller later and silently start printing on its own frame.
        /// Fractions inside the returned rect mean what an author expects them to mean — which band
        /// of the field, which column of it — instead of secretly carrying the padding as well.
        ///
        /// Anchored to fill with PIXEL offsets, which is what makes it independent of the card's
        /// size in the one direction that matters: the margin stays the same number of reference
        /// pixels whatever the card is doing, so it cannot be diluted by a bigger card or
        /// overwhelmed by a smaller one.
        /// </summary>
        /// <summary>
        /// The same thing for callers holding a Sprite rather than its name — the runtime HUDs,
        /// which are handed their plates by a builder and never see the asset paths.
        ///
        /// Keyed off <c>sprite.name</c>, which is the asset's file name and is what the tables above
        /// are written in. A sprite renamed on disk without its entry being renamed gets the missing
        /// measurement warning rather than the wrong margin, which is the right way for that to fail.
        /// </summary>
        public static RectTransform Inside(string name, RectTransform card, Sprite sprite)
            => Inside(name, card, sprite != null ? sprite.name : null);

        public static RectTransform Inside(string name, RectTransform card, string sprite)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(card, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);

            Vector4 m = Margin(sprite, ReferenceSize(card), card.name);
            rt.offsetMin = new Vector2(m.x, m.y);
            rt.offsetMax = new Vector2(-m.z, -m.w);
            return rt;
        }

        /// <summary>
        /// How big a rect is, in the canvas's reference pixels, WITHOUT waiting for a layout pass.
        ///
        /// <c>RectTransform.rect</c> would answer this, and it is the wrong answer here for two
        /// reasons. It is only valid once the canvas has been driven, which in an editor builder
        /// means the sizes depend on whether a Game view happens to be open; and the root canvas's
        /// rect is the SCREEN divided by the scale factor, so a machine with a differently shaped
        /// Game view would bake different numbers into the scene. A builder has to produce the same
        /// scene everywhere.
        ///
        /// So the size is derived arithmetically, by Unity's own formula — size = (anchorMax -
        /// anchorMin) x parent + sizeDelta — walked up to the canvas, whose reference size is read
        /// off its scaler. That is exact for every rect in this project, because they are all either
        /// pure fractions of their parent or a fixed sizeDelta at a point anchor, and both are that
        /// same formula.
        ///
        /// One honest caveat, stated because it is load bearing. The reference resolution is the
        /// canvas's TRUE size only at the aspect the scaler is balanced for. The lawn's and the
        /// rally's canvases match on HEIGHT, so their reference height is exactly 1080 at every
        /// aspect and every vertical number is exact; their width is nominal, and that is harmless
        /// because the width is only used to ask whether a card is narrower than its own left-plus-
        /// right border and no card carrying these sprites comes close. Bloom Rush's canvas matches
        /// at 0.5, so BOTH of its axes are nominal and shrink a little as the window widens.
        ///
        /// The error that introduces is in the safe direction. A canvas smaller than its reference
        /// makes the real card smaller, which makes Unity shrink the slice harder, which puts the
        /// decoration NEARER the edge than the margin computed here — so the text is inset by a
        /// little more than it strictly needs rather than a little less. Over-inset is a layout that
        /// breathes; under-inset is the fault this class exists to end.
        /// </summary>
        public static Vector2 ReferenceSize(RectTransform rt)
        {
            var canvas = rt != null ? rt.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return rt != null ? rt.rect.size : Vector2.zero;

            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            Vector2 rootSize = scaler != null &&
                               scaler.uiScaleMode == UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize
                ? scaler.referenceResolution
                : ((RectTransform)canvas.transform).rect.size;

            return SizeOf(rt, (RectTransform)canvas.transform, rootSize);
        }

        static Vector2 SizeOf(RectTransform rt, RectTransform root, Vector2 rootSize)
        {
            if (rt == null || rt == root) return rootSize;
            Vector2 parent = SizeOf(rt.parent as RectTransform, root, rootSize);
            Vector2 span = rt.anchorMax - rt.anchorMin;
            return new Vector2(span.x * parent.x + rt.sizeDelta.x,
                               span.y * parent.y + rt.sizeDelta.y);
        }
    }
}
