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
