using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DuckMow
{
    /// <summary>
    /// Which of the garden's own materials fills the frame while the game changes scene.
    ///
    /// Ordered by escalation, and that ordering is load-bearing: <see cref="MatchState"/> picks the
    /// style for a boundary off how far into the championship the player is, so the list reads as
    /// the tournament getting louder rather than as five interchangeable effects.
    /// </summary>
    public enum CurtainStyle
    {
        /// <summary>Hedge cuttings blown across the lens. The everyday wipe.</summary>
        LeafSweep,
        /// <summary>Uncut grass growing up over the frame. Used going INTO ground-level play.</summary>
        GrassRise,
        /// <summary>Tournament bunting and banner cloth dragged across camera. The arena wipe.</summary>
        BannerWipe,
        /// <summary>Blossom coming down. Used for the flower stages and the run into judging.</summary>
        PetalCurtain,
        /// <summary>Everything at once, plus confetti and stage lights. The finale.</summary>
        Fanfare
    }

    /// <summary>
    /// The curtain itself: a few hundred leaves, petals, blades and strips of banner cloth drawn as
    /// ONE mesh in ONE draw call, over a tinted backing that guarantees the frame is actually opaque
    /// at the moment the scene changes.
    ///
    /// A custom graphic rather than a few hundred UI Images, and that is not premature: this thing is
    /// on screen at exactly the moment the browser is also decompressing a scene, which is the worst
    /// frame in the game to spend three hundred draw calls and three hundred layout rebuilds on. One
    /// mesh, one procedurally-built atlas, no per-element transforms.
    ///
    /// The backing exists because particles alone cannot be trusted to cover every pixel — a gap of
    /// four pixels somewhere near a corner on an aspect ratio nobody tested is a flash of the old
    /// scene, and that flash is the entire failure this system exists to prevent. So the flecks do
    /// the acting and the backing does the covering, and the backing is a colour out of the garden
    /// rather than black, because a black frame is the specific thing that reads as "the game has
    /// stopped to load".
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class CurtainField : MaskableGraphic
    {
        // ------------------------------------------------------------------ the atlas

        const int TileSize = 256;
        const int AtlasSize = TileSize * 2;

        /// <summary>Tile indices into the 2x2 atlas.</summary>
        const int TileLeaf = 0, TilePetal = 1, TileBlade = 2, TileCloth = 3;

        /// <summary>
        /// The patch of atlas the opaque backing quad stretches across the whole screen, in atlas UV.
        ///
        /// It has to be solid, flat and well inside a shape, because it is the thing that guarantees
        /// the frame is actually opaque at the moment the scene changes — a translucent corner here
        /// is a flash of the outgoing scene, which is the entire failure this system exists to stop.
        /// It sits in the middle of the cloth tile, which is the broadest shape in the atlas.
        ///
        /// A named constant with a check behind it rather than two magic numbers inline, because it
        /// silently depends on the geometry of a shape function forty lines away: the window used to
        /// be ±0.24 in tile space, and reshaping the cloth from a rounded rectangle into a pennant
        /// pulled the cloth's upper edge in to ≈0.218 at the right of that window. Nothing would have
        /// complained. The backing would just have gone slightly see-through at two corners, on some
        /// aspect ratios, on the one frame nobody is looking at the curtain.
        /// </summary>
        const float SolidU = 0.75f, SolidV = 0.75f, SolidHalf = 0.025f;

        static Texture2D _atlas;

        public override Texture mainTexture => Atlas;

        static Texture2D Atlas
        {
            get
            {
                if (_atlas != null) return _atlas;
                _atlas = BuildAtlas();
                return _atlas;
            }
        }

        /// <summary>
        /// Four shapes drawn into one texture at load, so the curtain ships no art files and cannot
        /// go missing from a build. Signed-distance-ish coverage rather than hard tests, because a
        /// leaf with a stair-stepped edge sweeping past the lens at speed is the one artefact that
        /// makes a hand-made effect look procedural.
        /// </summary>
        static Texture2D BuildAtlas()
        {
            // No mip chain, deliberately. Four shapes packed two-by-two into one texture cannot be
            // mipped without them bleeding into each other — by the third level a leaf's midrib is
            // averaging with the banner cloth in the tile next door, and it shows up as a faint
            // diagonal scar across every piece of confetti in the finale. Mips would only ever be
            // sampled if a fleck were drawn smaller than 256 px, and the smallest one in any curtain
            // is a third of the screen height, so there is nothing to lose by dropping them.
            var tex = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = "Curtain atlas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 2,
                hideFlags = HideFlags.HideAndDontSave
            };

            var px = new Color32[AtlasSize * AtlasSize];

            for (int tile = 0; tile < 4; tile++)
            {
                int ox = (tile % 2) * TileSize;
                int oy = (tile / 2) * TileSize;

                for (int y = 0; y < TileSize; y++)
                for (int x = 0; x < TileSize; x++)
                {
                    // Tile space, -1..1 in both axes.
                    float u = (x + 0.5f) / TileSize * 2f - 1f;
                    float v = (y + 0.5f) / TileSize * 2f - 1f;

                    float a = 0f, shade = 1f;
                    switch (tile)
                    {
                        case TileLeaf:   Leaf(u, v, out a, out shade);  break;
                        case TilePetal:  Petal(u, v, out a, out shade); break;
                        case TileBlade:  Blade(u, v, out a, out shade); break;
                        default:         Cloth(u, v, out a, out shade); break;
                    }

                    byte s = (byte)Mathf.Clamp(Mathf.RoundToInt(shade * 255f), 0, 255);
                    byte al = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                    px[(oy + y) * AtlasSize + (ox + x)] = new Color32(s, s, s, al);
                }
            }

            VerifySolidWindow(px);

            tex.SetPixels32(px);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return tex;
        }

        /// <summary>
        /// Prove the backing's sample window is opaque and flat before the pixels become unreadable.
        ///
        /// Checked here rather than trusted, because the failure is invisible in every still: the
        /// backing is one quad stretched over the frame, so a stray transparent texel in this window
        /// does not draw a small artefact, it makes the ENTIRE curtain slightly see-through, and a
        /// non-uniform value draws one hard band across the whole screen. Both look like a bug in
        /// something else. Editor-only — a shipped build cannot have different pixels than the ones
        /// this ran against, since they are generated by this same code.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        static void VerifySolidWindow(Color32[] px)
        {
            int x0 = Mathf.FloorToInt((SolidU - SolidHalf) * AtlasSize);
            int x1 = Mathf.CeilToInt((SolidU + SolidHalf) * AtlasSize);
            int y0 = Mathf.FloorToInt((SolidV - SolidHalf) * AtlasSize);
            int y1 = Mathf.CeilToInt((SolidV + SolidHalf) * AtlasSize);

            byte firstShade = px[y0 * AtlasSize + x0].r;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var c = px[y * AtlasSize + x];
                if (c.a != 255)
                {
                    Debug.LogError($"[Curtain] The backing's sample window is not opaque at " +
                                   $"({x},{y}): alpha {c.a}. The curtain will not fully cover the " +
                                   $"frame and a scene change will flash. A shape function was " +
                                   $"changed without moving SolidU/SolidV/SolidHalf to match.");
                    return;
                }
                if (c.r != firstShade)
                {
                    Debug.LogError($"[Curtain] The backing's sample window is not one flat value " +
                                   $"({firstShade} vs {c.r} at {x},{y}). A value band crosses it, " +
                                   $"and it will be drawn as a hard line across the whole screen.");
                    return;
                }
            }
        }

        /// <summary>
        /// A soft edge: 0 below <paramref name="e0"/>, 1 above <paramref name="e1"/>, eased between.
        ///
        /// Written out rather than reaching for Mathf.SmoothStep, which is a DIFFERENT function
        /// despite the name — Unity's interpolates BETWEEN its first two arguments by the third,
        /// where every shading language's returns a 0..1 coverage across a threshold. Using the
        /// wrong one here returns roughly 1 for every pixel in the tile, so every leaf, petal and
        /// blade of grass in the curtain draws as an opaque rectangle with a nice gradient on it.
        /// Which is exactly what the first frame sheet showed.
        /// </summary>
        static float Edge(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / Mathf.Max(e1 - e0, 1e-6f));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Snap a smooth value ramp to flat steps across that shape's own range.
        ///
        /// This is the whole reason the curtain used to sit wrong against the rest of the game, and
        /// it is a MATERIAL problem rather than a composition one. ART_BIBLE §6: colour comes from
        /// vertex colours or per-material colour, no image textures on hero assets. Every prop, duck,
        /// judge and machine on screen therefore holds one flat value per facet. The curtain is the
        /// only thing in the project that fills the entire frame, and it was doing the opposite —
        /// a continuous airbrushed ramp baked into a bilinear atlas, so three hundred soft-shaded
        /// stickers flew across a hard-edged wooden-toy world. Nothing was misdrawn; it was speaking
        /// a different rendering language, which is exactly what "the transition doesn't match the
        /// game, is it a texture problem" describes.
        ///
        /// Only the VALUE is stepped. Coverage stays soft — a stair-stepped silhouette on a leaf
        /// sweeping past the lens is the one artefact that makes a hand-made effect look procedural,
        /// which is the note at the top of this file and still true. Hard facets, clean edges.
        ///
        /// Banded across <paramref name="lo"/>..<paramref name="hi"/> rather than over 0..1, because
        /// each shape's ramp occupies its own slice of the range: quantising all of them against the
        /// same absolute scale would give the leaf three bands and the petal two, for no reason
        /// anybody could see or tune.
        /// </summary>
        static float Facet(float shade, float lo, float hi, int bands)
        {
            float t = Mathf.Clamp01(Mathf.InverseLerp(lo, hi, shade));
            t = Mathf.Round(t * (bands - 1)) / (bands - 1);
            return Mathf.Clamp01(Mathf.Lerp(lo, hi, t));
        }

        /// <summary>A hedge cutting: pointed at both ends, with a midrib and a lit upper half.</summary>
        static void Leaf(float u, float v, out float a, out float shade)
        {
            // Half-width as a function of length. The exponent is what stops it being an ellipse:
            // below 1 the shoulders stay broad and the tips come to a point, which is a leaf.
            float t = Mathf.Clamp(v, -1f, 1f);
            float w = 0.66f * Mathf.Pow(Mathf.Max(0f, 1f - t * t), 0.62f);
            // A little asymmetry so a field of them never reads as one stamp repeated.
            w *= 1f + 0.10f * t;
            float d = Mathf.Abs(u) - w;
            a = 1f - Edge(-0.035f, 0.02f, d);

            // A lit side and a shadow side, meeting at the midrib — but as two flat planes with a
            // hard join, the way a carved leaf catches the light, not as an airbrush across it.
            float rib = 1f - Edge(0f, 0.045f, Mathf.Abs(u));
            shade = Mathf.Lerp(0.72f, 1.06f, 0.5f + 0.5f * u) - rib * 0.22f;
            // The veins survive banding as an undulation ALONG the band boundary rather than as
            // detail inside it, which reads as a painted stroke rather than as noise. That is worth
            // keeping: ART_BIBLE §5 asks for deliberate imperfection and explicitly not for noise.
            shade -= 0.05f * Mathf.Abs(Mathf.Sin((v * 7.5f) + u * 2.2f)) * (1f - Mathf.Abs(u));
            shade = Facet(shade, 0.50f, 1.06f, 3);
        }

        /// <summary>Blossom: broad and rounded at the top, pinched to a stem at the bottom.</summary>
        static void Petal(float u, float v, out float a, out float shade)
        {
            float t = Mathf.InverseLerp(-1f, 1f, v);                 // 0 at the stem, 1 at the tip
            float w = 0.70f * Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.72f));
            // A slight curl, so a falling field of them tumbles rather than flutters flat.
            float centre = 0.16f * (t - 0.5f) * (t - 0.5f) * 4f - 0.04f;
            float d = Mathf.Abs(u - centre) - w;
            a = 1f - Edge(-0.04f, 0.025f, d);

            // Three flat steps from stem to tip. A petal is thin enough that two would read as a
            // fold rather than as shading, and four starts to look like the ramp again.
            shade = Mathf.Lerp(0.66f, 1.10f, t) - 0.14f * Mathf.Abs(u);
            shade = Facet(shade, 0.52f, 1.10f, 3);
        }

        /// <summary>A blade of uncut grass: wide at the root, tapering to a point, leaning.</summary>
        static void Blade(float u, float v, out float a, out float shade)
        {
            float t = Mathf.InverseLerp(-1f, 1f, v);                 // 0 at the root, 1 at the tip
            float w = 0.30f * Mathf.Pow(1f - t, 1.15f) + 0.012f;
            float lean = 0.42f * t * t;                              // the bend, all of it near the tip
            float d = Mathf.Abs(u - lean) - w;
            a = 1f - Edge(-0.03f, 0.02f, d);

            // Bright at the tip where the light catches it, dark in the sward. Four steps rather
            // than three: a blade is the tallest thing in the atlas and carries the extra band
            // without the join reading as a stripe across it.
            shade = Mathf.Lerp(0.55f, 1.12f, t) - 0.18f * Mathf.Abs(u - lean) / Mathf.Max(w, 0.02f);
            shade = Facet(shade, 0.38f, 1.12f, 4);
        }

        /// <summary>
        /// A bunting pennant: broad at the hoist, tapering to a point, sagging on its own weight.
        ///
        /// This was a rounded rectangle, which ART_BIBLE §8 lists as an automatic rejection in its
        /// own right — and it is 62% of every BannerWipe and 44% of every Fanfare, so for two of the
        /// five curtains it was most of what was on screen. A rounded rectangle has no name: filled
        /// black it fails the silhouette test at §5, and a field of them tumbling past reads as
        /// confetti cut from card rather than as a showground's bunting.
        ///
        /// The taper is what gives it a nameable shape, and the sag is what makes it cloth. §5 asks
        /// for exactly this — "bunting sags unevenly" is in the list of things that sell character.
        /// </summary>
        static void Cloth(float u, float v, out float a, out float shade)
        {
            float t = Mathf.InverseLerp(-1f, 1f, u);                 // 0 at the hoist, 1 at the point
            // Below 1 the taper stays broad most of the way and then turns down to the tip, which is
            // a pennant. A straight lerp would give a wedge, and a wedge is a triangle, not cloth.
            float h = 0.80f * Mathf.Pow(1f - t, 0.70f) + 0.02f;
            // The whole pennant hangs off its line, deepest at mid-span.
            float sag = -0.22f * Mathf.Sin(Mathf.PI * t);
            float d = Mathf.Abs(v - sag) - h;
            a = 1f - Edge(-0.02f, 0.015f, d);

            // One fold along the length, banded flat, plus a darker underside. Two crossing ripples
            // became mush once the value was stepped — they were only ever legible as a soft wash,
            // which is the language this whole change is getting rid of.
            float fold = Mathf.Sin(u * 2.7f + 0.6f);
            float under = Edge(0.05f, 0.95f, (sag - v) / Mathf.Max(h, 0.02f));
            shade = 0.97f + 0.09f * fold - 0.20f * under;
            shade = Facet(shade, 0.68f, 1.06f, 3);
        }

        // ------------------------------------------------------------------ the flecks

        struct Fleck
        {
            public Vector2 home;      // packed position, in normalised rect space
            public Vector2 entry;     // where it comes in from
            public Vector2 exit;      // where it leaves to, which is NOT where it came from
            public Vector2 bow;       // mid-path bow, so nothing travels in a straight line
            public float rot, spin, restSpin;
            public float size, aspect;
            public float lead;        // stagger, 0..~0.5
            public float wobble;      // hold-time drift phase
            public Color tint;
            public int tile;
        }

        readonly List<Fleck> _flecks = new(320);

        /// <summary>0 clear, 1 fully covered. Driven by <see cref="Curtain"/>.</summary>
        public float Amount;
        /// <summary>True while the flecks should be flying OUT rather than in.</summary>
        public bool Leaving;
        /// <summary>Seconds of held-cover drift, so a long load never shows a frozen frame.</summary>
        public float HoldClock;
        /// <summary>The colour the frame settles to under the flecks. Never black.</summary>
        public Color Backing = new Color(0.10f, 0.18f, 0.09f, 1f);
        /// <summary>Extra brightness flashed through the backing on the loudest transitions.</summary>
        public float Glow;

        CurtainStyle _style;

        /// <summary>Which curtain is currently loaded. Read by the sign, which dresses to match.</summary>
        public CurtainStyle Style => _style;

        /// <summary>
        /// Lay out a curtain.
        ///
        /// <paramref name="sweepHint"/> is the direction, in screen terms, the player was already
        /// travelling — see StageSeam.SweepDirection. Honoured by the styles that are WEATHER, where
        /// a gust can come from anywhere; ignored by the two that are GRAVITY, because grass grows
        /// up and blossom falls down whichever way the mower happened to be pointing, and a
        /// sideways-falling petal is a worse artefact than a wipe that disagrees with the last shot.
        /// </summary>
        public void Rebuild(CurtainStyle style, float intensity, int seed, Vector2? sweepHint = null)
        {
            _style = style;
            var rng = new System.Random(seed);
            float Rand() => (float)rng.NextDouble();
            float Range(float a, float b) => a + (b - a) * Rand();

            _flecks.Clear();

            // A jittered grid, sized so neighbours overlap by roughly a third. The grid is what
            // guarantees even coverage; the jitter is what stops it looking like a grid.
            int cols, rows;
            float baseSize;
            switch (style)
            {
                case CurtainStyle.GrassRise:    cols = 26; rows = 9;  baseSize = 0.150f; break;
                case CurtainStyle.BannerWipe:   cols = 11; rows = 7;  baseSize = 0.225f; break;
                case CurtainStyle.PetalCurtain: cols = 20; rows = 13; baseSize = 0.125f; break;
                case CurtainStyle.Fanfare:      cols = 22; rows = 14; baseSize = 0.130f; break;
                default:                        cols = 20; rows = 13; baseSize = 0.135f; break;
            }

            // The sweep direction. Everything enters from one side and leaves towards the other, so
            // the curtain carries momentum across the cut instead of bouncing back the way it came.
            // Which way the flecks TRAVEL. Their entry point is placed a screen back along this, so
            // grass rising has to point up and blossom falling has to point down — the two that are
            // gravity rather than weather, and the two that look most wrong reversed.
            Vector2 inDir = style switch
            {
                CurtainStyle.GrassRise    => new Vector2(0f, 1f),
                CurtainStyle.BannerWipe   => new Vector2(-1f, 0.18f).normalized,
                CurtainStyle.PetalCurtain => new Vector2(0.25f, -1f).normalized,
                CurtainStyle.Fanfare      => new Vector2(-0.85f, 0.55f).normalized,
                _                         => new Vector2(1f, 0.35f).normalized
            };

            // The gust takes the player's own heading where the style allows it.
            bool weather = style == CurtainStyle.LeafSweep || style == CurtainStyle.BannerWipe
                        || style == CurtainStyle.Fanfare;
            if (weather && sweepHint.HasValue && sweepHint.Value.sqrMagnitude > 0.01f)
            {
                var hint = sweepHint.Value.normalized;
                // Blended rather than adopted whole, so the authored tilt of each style survives and
                // a mower pointed straight at the camera does not produce a curtain with no travel.
                inDir = Vector2.Lerp(inDir, hint, 0.75f).normalized;
            }
            Vector2 outDir = style switch
            {
                // Grass falls back down rather than continuing through the ceiling: it grew, and
                // what grew has to be cut or lie down, not fly away.
                CurtainStyle.GrassRise    => new Vector2(0f, -1f),
                CurtainStyle.PetalCurtain => new Vector2(-0.2f, -1f).normalized,
                _                         => -inDir
            };

            var palette = Palette(style);

            for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                var f = new Fleck();

                float gx = (x + 0.5f) / cols;
                float gy = (y + 0.5f) / rows;
                f.home = new Vector2(gx + Range(-0.5f, 0.5f) / cols,
                                     gy + Range(-0.5f, 0.5f) / rows);

                f.size = baseSize * Range(0.78f, 1.34f);
                f.aspect = 1f;
                f.tile = PickTile(style, Rand());

                // Aspect is height over width. Blades are tall and narrow; banner cloth is a wide
                // strip, and its height is trimmed so the extra width does not make it a bedsheet.
                if (f.tile == TileBlade) { f.aspect = Range(2.0f, 3.1f); f.size *= 1.30f; }
                if (f.tile == TileCloth) { f.aspect = Range(0.34f, 0.55f); f.size *= 0.86f; }

                // Entry and exit are pushed a full screen clear of the frame along the sweep, plus a
                // lateral scatter, so nothing is ever seen popping into existence at the edge.
                float scatterIn = Range(-0.55f, 0.55f);
                float scatterOut = Range(-0.55f, 0.55f);
                Vector2 lat = new Vector2(-inDir.y, inDir.x);
                f.entry = f.home - inDir * Range(1.25f, 2.15f) + lat * scatterIn;
                f.exit = f.home + outDir * Range(1.35f, 2.4f)
                       + new Vector2(-outDir.y, outDir.x) * scatterOut;

                // The bow is what makes the path an arc. Perpendicular to travel, signed per fleck.
                f.bow = lat * Range(-0.28f, 0.28f);

                f.rot = Range(0f, 360f);
                f.spin = Range(-330f, 330f) * (0.6f + intensity * 0.7f);
                f.restSpin = Range(-16f, 16f);
                if (f.tile == TileBlade) { f.spin *= 0.12f; f.rot = Range(-16f, 16f); f.restSpin *= 0.4f; }

                // Late arrivals in the reading direction of the sweep, so the frame fills as a wave
                // rather than everywhere at once.
                float along = Vector2.Dot(f.home - new Vector2(0.5f, 0.5f), inDir) + 0.5f;
                f.lead = Mathf.Clamp01(0.42f * (1f - along) + Range(0f, 0.16f)) * (1f - intensity * 0.35f);

                f.wobble = Range(0f, 6.283f);
                f.tint = palette[(int)(Rand() * (palette.Length - 0.0001f))];
                // A touch of per-fleck value spread. Flat tint over three hundred quads is what a
                // particle system looks like; a spread is what a hedge looks like.
                float k = Range(0.84f, 1.14f);
                f.tint = new Color(Mathf.Clamp01(f.tint.r * k), Mathf.Clamp01(f.tint.g * k),
                                   Mathf.Clamp01(f.tint.b * k), f.tint.a);

                _flecks.Add(f);
            }

            Backing = BackingFor(style);
            SetVerticesDirty();
        }

        static int PickTile(CurtainStyle style, float r) => style switch
        {
            CurtainStyle.GrassRise    => r < 0.86f ? TileBlade : TileLeaf,
            CurtainStyle.BannerWipe   => r < 0.62f ? TileCloth : (r < 0.86f ? TileLeaf : TilePetal),
            CurtainStyle.PetalCurtain => r < 0.80f ? TilePetal : TileLeaf,
            CurtainStyle.Fanfare      => r < 0.44f ? TileCloth : (r < 0.78f ? TilePetal : TileLeaf),
            _                         => r < 0.88f ? TileLeaf : TileBlade
        };

        static Color[] Palette(CurtainStyle style) => style switch
        {
            CurtainStyle.GrassRise => new[]
            {
                new Color(0.32f, 0.56f, 0.20f), new Color(0.24f, 0.46f, 0.16f),
                new Color(0.44f, 0.66f, 0.26f), new Color(0.19f, 0.37f, 0.14f),
                new Color(0.55f, 0.72f, 0.31f)
            },
            CurtainStyle.BannerWipe => new[]
            {
                // The tournament's own cloth: cream, hunting green, and the player's red.
                new Color(0.93f, 0.89f, 0.76f), new Color(0.20f, 0.42f, 0.26f),
                new Color(0.72f, 0.24f, 0.20f), new Color(0.88f, 0.72f, 0.30f),
                new Color(0.36f, 0.54f, 0.30f)
            },
            CurtainStyle.PetalCurtain => new[]
            {
                new Color(0.96f, 0.80f, 0.86f), new Color(0.92f, 0.62f, 0.72f),
                new Color(0.99f, 0.94f, 0.88f), new Color(0.86f, 0.74f, 0.94f),
                new Color(0.42f, 0.58f, 0.28f)
            },
            CurtainStyle.Fanfare => new[]
            {
                new Color(0.96f, 0.78f, 0.24f), new Color(0.86f, 0.26f, 0.24f),
                new Color(0.98f, 0.95f, 0.86f), new Color(0.30f, 0.60f, 0.86f),
                new Color(0.34f, 0.72f, 0.36f), new Color(0.90f, 0.52f, 0.78f)
            },
            _ => new[]
            {
                new Color(0.38f, 0.58f, 0.22f), new Color(0.27f, 0.45f, 0.18f),
                new Color(0.62f, 0.60f, 0.22f), new Color(0.74f, 0.48f, 0.18f),
                new Color(0.46f, 0.65f, 0.25f)
            }
        };

        static Color BackingFor(CurtainStyle style) => style switch
        {
            // Every one of these is a colour out of the game's own world. None of them is black,
            // and none is bright enough to read as a flash bulb on the way past.
            CurtainStyle.GrassRise    => new Color(0.088f, 0.170f, 0.070f),
            CurtainStyle.BannerWipe   => new Color(0.105f, 0.190f, 0.130f),
            CurtainStyle.PetalCurtain => new Color(0.150f, 0.090f, 0.140f),
            CurtainStyle.Fanfare      => new Color(0.140f, 0.085f, 0.045f),
            _                         => new Color(0.078f, 0.150f, 0.062f)
        };

        // ------------------------------------------------------------------ the mesh

        static readonly Vector2[] Corner =
        {
            new Vector2(-0.5f, -0.5f), new Vector2(-0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, -0.5f)
        };

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_flecks.Count == 0) return;

            var r = rectTransform.rect;
            // Square-ish working unit, so a fleck is the same physical size on a phone in portrait
            // as on a widescreen monitor rather than being stretched by the aspect.
            float unit = Mathf.Max(r.width, r.height);

            float amount = Mathf.Clamp01(Amount);
            if (amount <= 0f) return;

            // The backing, first, so every fleck draws over it. Opaque well before the flecks have
            // finished arriving, because the cut happens under a covered frame and the last third of
            // the animation is there to be watched, not to be relied on.
            float back = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.88f, amount));
            if (Leaving) back = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.80f, amount));
            if (back > 0.001f)
            {
                var c = Backing;
                c = Color.Lerp(c, Color.Lerp(c, Color.white, 0.75f), Mathf.Clamp01(Glow));
                c.a = back;
                AddQuad(vh, new Vector2(r.center.x, r.center.y),
                        new Vector2(r.width * 1.05f, r.height * 1.05f), 0f, c, TileCloth, solid: true);
            }

            for (int i = 0; i < _flecks.Count; i++)
            {
                var f = _flecks[i];

                float p;
                if (!Leaving)
                {
                    // Arriving. Each fleck runs its own remapped window, so the wave has depth.
                    float span = Mathf.Max(1f - f.lead, 0.05f);
                    p = Mathf.Clamp01((amount - f.lead) / span);
                    p = Mathf.SmoothStep(0f, 1f, p);
                }
                else
                {
                    // Leaving. The stagger is reversed — whatever arrived last goes first — which is
                    // what makes the exit look like the same gust continuing rather than a rewind.
                    float span = Mathf.Max(1f - f.lead, 0.05f);
                    float q = Mathf.Clamp01((1f - amount - (0.45f - f.lead * 0.9f)) / span);
                    p = 1f - Mathf.SmoothStep(0f, 1f, q);
                }

                if (p <= 0.0005f) continue;

                Vector2 from = Leaving ? f.home : f.entry;
                Vector2 to = Leaving ? f.exit : f.home;
                float travel = Leaving ? 1f - p : p;

                Vector2 n = Vector2.Lerp(from, to, Leaving ? 1f - p : p);
                // The bow: a parabola peaking mid-flight, zero at both ends.
                float bowT = Leaving ? 1f - p : p;
                n += f.bow * (4f * bowT * (1f - bowT));

                // Held cover is never still. A slow orbit plus a spin residual, on the unscaled
                // clock, so a two-second load reads as weather rather than as a paused game.
                float settled = Leaving ? Mathf.Clamp01(1f - travel * 2f) : Mathf.Clamp01(p * 2f - 1f);
                if (settled > 0f)
                {
                    float t = HoldClock;
                    n += settled * 0.035f * new Vector2(
                        Mathf.Sin(t * 0.9f + f.wobble),
                        Mathf.Cos(t * 0.7f + f.wobble * 1.3f));
                }

                float rot = f.rot + f.spin * (Leaving ? (1f - p) : p) + f.restSpin * HoldClock * settled;

                // Scale in slightly on arrival so nothing hard-cuts into frame at full size.
                float scale = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(p * 2.2f));
                float height = f.size * unit * scale;
                var size = new Vector2(height / Mathf.Max(f.aspect, 0.05f), height);

                var pos = new Vector2(r.xMin + n.x * r.width, r.yMin + n.y * r.height);

                var tint = f.tint;
                tint.a = Mathf.Clamp01(p * 3f);
                if (Glow > 0.001f) tint = Color.Lerp(tint, Color.white, Glow * 0.45f);

                AddQuad(vh, pos, size, rot, tint, f.tile, solid: false);
            }
        }

        void AddQuad(VertexHelper vh, Vector2 centre, Vector2 size, float degrees, Color colour,
                     int tile, bool solid)
        {
            if (colour.a <= 0.003f) return;
            if (vh.currentVertCount > 60000) return;   // a mesh has 65k vertices and no more

            float rad = degrees * Mathf.Deg2Rad;
            float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);

            // Solid quads sample a patch of the cloth tile that is opaque and one flat value — the
            // backing must not inherit any part of a shape's edge or the screen leaks. See SolidU,
            // and VerifySolidWindow, which proves it rather than assuming it.
            float u0, v0, u1, v1;
            if (solid)
            {
                u0 = SolidU - SolidHalf; v0 = SolidV - SolidHalf;
                u1 = SolidU + SolidHalf; v1 = SolidV + SolidHalf;
            }
            else
            {
                // A texel and a half in from the tile boundary, so bilinear filtering at the edge of
                // a rotated quad cannot reach across into the neighbouring shape.
                const float Pad = 1.5f / AtlasSize;
                u0 = (tile % 2) * 0.5f + Pad; v0 = (tile / 2) * 0.5f + Pad;
                u1 = u0 + 0.5f - Pad * 2f; v1 = v0 + 0.5f - Pad * 2f;
            }

            int start = vh.currentVertCount;
            for (int i = 0; i < 4; i++)
            {
                var c = Corner[i];
                var local = new Vector2(c.x * size.x, c.y * size.y);
                var rotated = new Vector2(local.x * cs - local.y * sn, local.x * sn + local.y * cs);
                float uu = i == 0 || i == 1 ? u0 : u1;
                float vv = i == 0 || i == 3 ? v0 : v1;
                vh.AddVert(new Vector3(centre.x + rotated.x, centre.y + rotated.y, 0f),
                           colour, new Vector2(uu, vv));
            }
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
