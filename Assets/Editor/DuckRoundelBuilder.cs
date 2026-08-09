using System.IO;
using UnityEditor;
using UnityEngine;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// The distributor's roundel: a duck in a ring, stamped on the front of the build.
    ///
    /// ---- drawn rather than generated ----
    ///
    /// This project makes its art in code wherever the art is geometry — the curtain's shape atlas,
    /// the card plates, the board — and a roundel is geometry. Three things follow from drawing it
    /// here that a generated image cannot offer: the palette is the GAME's, to the value, rather
    /// than something near it; it re-bakes at any size, so a 4K splash costs a number change; and
    /// there is no licence question about what is in the file.
    ///
    /// ---- the silhouette test ----
    ///
    /// ART_BIBLE §5: fill the model black, and if you cannot name it, redesign it. A logo is that
    /// test with nothing else to lean on, so the duck here is four masses and no detail: body, head,
    /// beak, tail. The eye is punched OUT of the silhouette rather than drawn on it, which is what
    /// keeps it a silhouette — a cream duck with a cream eye needs a line round it, and a line is
    /// the thing this shape cannot afford at 64 px.
    ///
    /// The ring is the "distributor" part and it is doing real work: a mark that fills its frame
    /// reads as a picture, and one held inside a ring reads as a STAMP. That is the whole difference
    /// between a splash and a title card.
    /// </summary>
    public static class DuckRoundelBuilder
    {
        const string Dir = "Assets/Art/Textures/Title";
        const string Path = Dir + "/title_roundel.png";
        const int Size = 1024;

        // The game's own paint, taken from DuckUIBuilder and the art bible rather than eyeballed.
        static readonly Color Brick = new Color(0.72f, 0.24f, 0.20f);
        static readonly Color Cream = new Color(0.97f, 0.94f, 0.86f);
        static readonly Color Green = new Color(0.16f, 0.34f, 0.21f);
        static readonly Color Gold = new Color(0.86f, 0.76f, 0.42f);

        [MenuItem("Duck/9 · Build the roundel logo", priority = 91)]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false);
            var px = new Color32[Size * Size];

            // One pixel in normalised units, used as the antialiasing width everywhere. Written once
            // so the whole mark softens by the same amount and no edge is crisper than its neighbour.
            float aa = 2f / Size;

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = (x + 0.5f) / Size * 2f - 1f;
                float v = (y + 0.5f) / Size * 2f - 1f;

                // ---- the ring, outside in ----
                float r = Mathf.Sqrt(u * u + v * v);

                float outer = Cover(0.96f - r, aa);          // the disc's outer limit
                float ringIn = Cover(0.80f - r, aa);          // where the brick band stops
                float keyline = Cover(0.775f - r, aa);        // a hair of gold inside the band
                float field = Cover(0.755f - r, aa);          // the green field the duck sits on

                // ---- the duck, four masses and a hole ----
                //
                // Facing left, because the beak is the one part that says "duck" on its own and a
                // mark is read left first. Sat slightly low and forward so the ring's empty air is
                // above and behind it, which is where a crest wants its space.
                // Sizes are deliberately unequal — one dominant mass, one secondary, then detail, per
                // ART_BIBLE §5. The first pass had a tail nearly as big as the head and sat at the
                // body's waist, so the silhouette read as two round lumps with a bump behind them
                // rather than as a duck, which is the "three similar-sized lumps is a failed prop"
                // failure exactly. The tail is now smaller, higher and further back, so it leaves the
                // body as a point instead of swelling it.
                float body = Ellipse(u - 0.06f, v + 0.10f, 0.40f, 0.29f);
                float head = Ellipse(u + 0.20f, v - 0.20f, 0.20f, 0.19f);
                // Thicker and pulled INTO the head. At 0.070 it met the head across so thin a band
                // that it read as a separate sliver stuck on the face — a beak has to be a
                // continuation of the head's mass, not an appendix to it.
                float beak = Ellipse(u + 0.41f, v - 0.155f, 0.17f, 0.092f);
                // Smaller than the beak and LOWER than it, which is what breaks the symmetry: the
                // head is lifted and the tail sits at body height, so the mark says which way the
                // animal faces. Matched in size and height they read as a pair of flippers and the
                // silhouette loses its direction, which is the one thing it has to carry.
                //
                // It also has to TOUCH. Two passes were spent finding that out: at v 0.15 it barely
                // grazed the body, and raising it to 0.225 to fix the symmetry floated it clear off
                // the back as a separate blob. The body's half-height at this x is only 0.15, so the
                // room here is much smaller than the middle of the shape suggests.
                float tail = Ellipse(u - 0.38f, v - 0.110f, 0.15f, 0.075f);

                float duck = Mathf.Min(Mathf.Min(body, head), Mathf.Min(beak, tail));
                float duckIn = Cover(-duck, aa);

                // Punched, not painted. See the class note.
                float eye = Cover(-Ellipse(u + 0.235f, v - 0.245f, 0.048f, 0.048f), aa);

                // ---- composite, back to front ----
                Color c = Brick;
                c = Color.Lerp(c, Gold, keyline);
                c = Color.Lerp(c, Green, field);
                c = Color.Lerp(c, Cream, duckIn);
                c = Color.Lerp(c, Green, eye);

                // The gap between band and field, drawn last so it cuts everything: a cream hairline
                // is what stops the brick and the green touching, and two saturated colours meeting
                // with no breath between them is the thing that reads as cheap.
                float gap = Mathf.Max(0f, ringIn - keyline);
                c = Color.Lerp(c, Cream, gap);

                c.a = outer;
                px[y * Size + x] = c;
            }

            tex.SetPixels32(px);
            tex.Apply(false);
            File.WriteAllBytes(Path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);

            // Imported as a Sprite because that is what the splash logo list takes, and with alpha
            // treated as transparency because the mark is a disc on nothing — without this the
            // corners come back as black rather than as absent.
            var importer = (TextureImporter)AssetImporter.GetAtPath(Path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            Debug.Log($"[Duck] roundel written to {Path} at {Size}x{Size} and imported as a Sprite. " +
                      "Run 'Duck/9 · Build splash screen' to put it on the front of the build.");
        }

        // ------------------------------------------------------------------ the splash card

        const string CardPath = Dir + "/title_splash.png";

        /// <summary>
        /// The label under the mark. Drawn, not typeset — see BuildCard.
        /// </summary>
        const string Label = "POND & GREEN";

        /// <summary>
        /// The mark with the label under it, as one image: the distributor's card.
        ///
        /// A bare disc on an empty field reads as a placeholder — a mark with nothing to be the mark
        /// OF. The name under it is what makes it a card.
        ///
        /// One image and not two splash logos in sequence, because Unity's floor is two seconds each
        /// and four seconds of logo in front of a lawn-mowing game is a joke at the game's expense.
        ///
        /// ---- the letters are DRAWN ----
        ///
        /// Not set in the game's own face, on the owner's explicit call that typeface consistency
        /// could go. What is bought for it is that this file has no font dependency at all: it needs
        /// no TMP asset, no temporary canvas, no render texture, and it cannot break when somebody
        /// changes the UI font. A geometric monoline capital is also the right register for a stamp —
        /// it is the lettering pressed into a maker's mark rather than the lettering on a poster.
        ///
        /// The cost is honest and worth stating: these glyphs will never quite match the masthead's,
        /// so the card and the front page are speaking in two hands. On a splash that is gone in two
        /// seconds and never seen beside the menu, that is a price worth paying; anywhere the two
        /// appeared together it would not be.
        /// </summary>
        [MenuItem("Duck/9 · Build the splash card", priority = 92)]
        public static void BuildCard()
        {
            var mark = LoadPng(Path);
            if (mark == null) return;

            const int W = 1100, H = 1120;
            const int MarkSize = 760;
            const int TopPad = 40;
            const int Gap = 58;

            var card = new Texture2D(W, H, TextureFormat.RGBA32, false, false);
            var px = new Color32[W * H];      // transparent by default

            Blit(px, W, H, mark, (W - MarkSize) / 2, TopPad, MarkSize, MarkSize);
            DrawLabel(px, W, H, Label, TopPad + MarkSize + Gap, 900, Brick);

            card.SetPixels32(px);
            card.Apply(false);
            File.WriteAllBytes(CardPath, card.EncodeToPNG());
            Object.DestroyImmediate(card);
            Object.DestroyImmediate(mark);

            AssetDatabase.ImportAsset(CardPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(CardPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            Debug.Log($"[Duck] splash card written to {CardPath} at {W}x{H}. " +
                      "Run 'Duck/9 · Build splash screen' to put it on the front of the build.");
        }

        // ------------------------------------------------------------------ the drawn lettering

        const float Stroke = 0.180f;   // of cap height
        const float Adv = 0.66f;       // glyph width
        const float Track = 0.21f;     // space between glyphs

        /// <summary>
        /// Set a word across a given width, centred, and stamp it into the buffer.
        ///
        /// The cap height falls out of the width rather than being chosen: a label that is always
        /// the same number of pixels tall would grow and shrink with the word, and the card's
        /// proportions are what has to stay put.
        /// </summary>
        static void DrawLabel(Color32[] dst, int dw, int dh, string text, int topY, int width, Color ink)
        {
            float units = 0f;
            foreach (char ch in text) units += (ch == ' ' ? Track * 2f : Adv) + Track;
            units -= Track;
            if (units <= 0f) return;

            float em = width / units;
            float x = (dw - width) * 0.5f;
            float baseline = dh - topY - em;      // buffer is bottom-up; topY is from the top

            foreach (char ch in text)
            {
                if (ch != ' ') Stamp(dst, dw, dh, ch, x, baseline, em, ink);
                x += (ch == ' ' ? Track * 2f : Adv) * em + Track * em;
            }
        }

        static void Stamp(Color32[] dst, int dw, int dh, char ch, float x0, float y0, float em, Color ink)
        {
            float half = Stroke * 0.5f * em;
            int px0 = Mathf.Max(0, Mathf.FloorToInt(x0 - half - 2f));
            int px1 = Mathf.Min(dw - 1, Mathf.CeilToInt(x0 + Adv * em + half + 2f));
            int py0 = Mathf.Max(0, Mathf.FloorToInt(y0 - half - 2f));
            int py1 = Mathf.Min(dh - 1, Mathf.CeilToInt(y0 + em + half + 2f));

            for (int py = py0; py <= py1; py++)
            for (int pxi = px0; pxi <= px1; pxi++)
            {
                // Into the glyph's own box: x across, y up, cap height 1.
                var p = new Vector2((pxi + 0.5f - x0) / em, (py + 0.5f - y0) / em);
                float d = Skeleton(ch, p);
                if (d > 1f) continue;

                float a = Cover(Stroke * 0.5f - d, 1.6f / em);
                if (a <= 0.002f) continue;

                Color dcol = dst[py * dw + pxi];
                float outA = a + dcol.a * (1f - a);
                dst[py * dw + pxi] = outA <= 0.0001f ? (Color)Color.clear
                    : new Color((ink.r * a + dcol.r * dcol.a * (1f - a)) / outA,
                                (ink.g * a + dcol.g * dcol.a * (1f - a)) / outA,
                                (ink.b * a + dcol.b * dcol.a * (1f - a)) / outA, outA);
            }
        }

        /// <summary>
        /// Distance to a letter's SKELETON — its centre lines, before any thickness. One monoline
        /// geometric alphabet, only the eight capitals this label needs.
        /// </summary>
        static float Skeleton(char ch, Vector2 p)
        {
            const float L = 0.09f, R = 0.57f;      // the stems
            switch (ch)
            {
                case 'P':
                    return Mathf.Min(Seg(p, new Vector2(L, 0f), new Vector2(L, 1f)),
                                     Clip(Ell(p, new Vector2(0.26f, 0.735f), 0.29f, 0.265f), p.x - 0.26f));
                case 'O':
                    return Ell(p, new Vector2(0.33f, 0.5f), 0.30f, 0.50f);
                case 'N':
                    return Mathf.Min(Mathf.Min(Seg(p, new Vector2(L, 0f), new Vector2(L, 1f)),
                                               Seg(p, new Vector2(R, 0f), new Vector2(R, 1f))),
                                     Seg(p, new Vector2(L, 1f), new Vector2(R, 0f)));
                case 'D':
                    return Mathf.Min(Seg(p, new Vector2(L, 0f), new Vector2(L, 1f)),
                                     Clip(Ell(p, new Vector2(0.11f, 0.5f), 0.48f, 0.50f), p.x - 0.11f));
                case 'E':
                    return Mathf.Min(Mathf.Min(Seg(p, new Vector2(L, 0f), new Vector2(L, 1f)),
                                               Seg(p, new Vector2(L, 1f), new Vector2(0.56f, 1f))),
                           Mathf.Min(Seg(p, new Vector2(L, 0.5f), new Vector2(0.48f, 0.5f)),
                                     Seg(p, new Vector2(L, 0f), new Vector2(0.56f, 0f))));
                case 'R':
                    return Mathf.Min(Mathf.Min(Seg(p, new Vector2(L, 0f), new Vector2(L, 1f)),
                                     Clip(Ell(p, new Vector2(0.26f, 0.735f), 0.29f, 0.265f), p.x - 0.26f)),
                                     Seg(p, new Vector2(0.28f, 0.47f), new Vector2(0.58f, 0f)));
                case 'G':
                {
                    // The ring with its mouth cut out on the right, plus the bar that makes it a G
                    // rather than a C — the bar is what stops it reading as an O with a nick in it.
                    float ring = Ell(p, new Vector2(0.33f, 0.5f), 0.30f, 0.50f);
                    var d = p - new Vector2(0.33f, 0.5f);
                    bool mouth = d.x > 0f && d.y > -0.16f && d.y < 0.13f;
                    if (mouth) ring = 9f;
                    return Mathf.Min(ring, Mathf.Min(
                        Seg(p, new Vector2(0.36f, 0.36f), new Vector2(0.61f, 0.36f)),
                        Seg(p, new Vector2(0.61f, 0.36f), new Vector2(0.61f, 0.12f))));
                }
                case '&':
                {
                    // Built rather than quoted: a loop over a loop with a leg thrown off to the
                    // right, which is the ampersand's actual construction and survives being drawn
                    // in one weight.
                    float top = Ell(p, new Vector2(0.27f, 0.775f), 0.185f, 0.185f);
                    float bot = Clip(Ell(p, new Vector2(0.29f, 0.275f), 0.26f, 0.265f),
                                     -(p.x - 0.29f) + 0.02f);
                    float join = Seg(p, new Vector2(0.145f, 0.62f), new Vector2(0.47f, 0.10f));
                    float leg = Seg(p, new Vector2(0.42f, 0.20f), new Vector2(0.63f, 0.02f));
                    return Mathf.Min(Mathf.Min(top, bot), Mathf.Min(join, leg));
                }
                default: return 9f;
            }
        }

        /// <summary>Distance to a segment: the one primitive every stroke here is made of.</summary>
        static float Seg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude;
        }

        /// <summary>Distance to an elliptical ring's centre line.</summary>
        static float Ell(Vector2 p, Vector2 c, float rx, float ry)
            => Mathf.Abs(Ellipse(p.x - c.x, p.y - c.y, rx, ry));

        /// <summary>Keep a shape only where <paramref name="keep"/> is positive.</summary>
        static float Clip(float d, float keep) => keep >= 0f ? d : Mathf.Max(d, -keep);

        static Texture2D LoadPng(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogError($"[Duck] {path} is missing. Run 'Duck/9 · Build the roundel logo' first " +
                               "if it is the roundel; the masthead is authored art and should be in git.");
                return null;
            }
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            t.LoadImage(File.ReadAllBytes(path));   // always readable, whatever the importer says
            return t;
        }

        /// <summary>
        /// Draw <paramref name="src"/> into the buffer at a size, over whatever is already there.
        ///
        /// Bilinear, and compositing rather than replacing: the wordmark's letters have soft edges
        /// and a nearest-neighbour scale down from 1536 would chew them. Source-over rather than a
        /// straight copy because both images are transparent outside their shapes and a copy would
        /// punch their bounding boxes into the card.
        /// </summary>
        static void Blit(Color32[] dst, int dw, int dh, Texture2D src, int x0, int y0, int w, int h)
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int dx = x0 + x;
                // The card is built top-down for readability; the buffer is bottom-up.
                int dy = dh - 1 - (y0 + y);
                if (dx < 0 || dx >= dw || dy < 0 || dy >= dh) continue;

                Color s = src.GetPixelBilinear((x + 0.5f) / w, 1f - (y + 0.5f) / h);
                if (s.a <= 0.001f) continue;

                Color d = dst[dy * dw + dx];
                float a = s.a + d.a * (1f - s.a);
                Color o = a <= 0.0001f ? Color.clear
                        : new Color((s.r * s.a + d.r * d.a * (1f - s.a)) / a,
                                    (s.g * s.a + d.g * d.a * (1f - s.a)) / a,
                                    (s.b * s.a + d.b * d.a * (1f - s.a)) / a, a);
                dst[dy * dw + dx] = o;
            }
        }

        /// <summary>
        /// Signed distance to an axis-aligned ellipse: negative inside, positive out. Approximate —
        /// it is the circle's distance in a squashed space — which is exact enough for a mark whose
        /// edges are all antialiased over two pixels anyway.
        /// </summary>
        static float Ellipse(float x, float y, float rx, float ry)
        {
            float k = Mathf.Sqrt(x * x / (rx * rx) + y * y / (ry * ry));
            return (k - 1f) * Mathf.Min(rx, ry);
        }

        /// <summary>Coverage from a signed distance: 1 well inside, 0 well outside, eased across.</summary>
        static float Cover(float d, float aa)
        {
            float t = Mathf.Clamp01(d / Mathf.Max(aa, 1e-6f) * 0.5f + 0.5f);
            return t * t * (3f - 2f * t);
        }
    }
}
