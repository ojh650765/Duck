using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The rules of the competition, in one place, applied to every contestant.
    ///
    /// The player's lawn is 64 m at a 256 grid and a rival's is 48 m at a 128 grid, so the two
    /// cannot share a rasteriser that reads <see cref="Field"/>'s constants. They can and must
    /// share the actual judging: rasterise the target the same way, measure coverage, spill and
    /// edge the same way, and turn the same numbers into the same marks. If the player's score
    /// and a rival's score came out of different code the standings would be a fiction.
    /// </summary>
    public static class Scoring
    {
        /// <summary>
        /// Rasterise a target shape over a square plot.
        ///
        /// <paramref name="shapeRadius"/> is the world radius that shape space [-1,1] maps onto,
        /// and <paramref name="swath"/> is the mower's cutting width — the outline band that edge
        /// quality is measured over is one swath wide, which is what makes a clean edge a matter
        /// of driving accurately rather than of grid resolution.
        /// </summary>
        public static void Rasterize(ShapeId shape, int gridRes, float plotSize, float shapeRadius,
                                     float swath, out bool[] inside, out bool[] boundary,
                                     out int insideCount, out int boundaryCount)
        {
            inside = new bool[gridRes * gridRes];
            boundary = new bool[inside.Length];
            insideCount = 0;
            boundaryCount = 0;

            float half = plotSize * 0.5f;
            float bandShapeUnits = swath / shapeRadius;

            for (int gz = 0; gz < gridRes; gz++)
            {
                int row = gz * gridRes;
                float wz = (gz + 0.5f) / gridRes * plotSize - half;
                for (int gx = 0; gx < gridRes; gx++)
                {
                    float wx = (gx + 0.5f) / gridRes * plotSize - half;
                    float d = TargetShapes.Sdf(shape, new Vector2(wx / shapeRadius, wz / shapeRadius));

                    bool isIn = d < 0f;
                    inside[row + gx] = isIn;
                    if (isIn) insideCount++;

                    bool isEdge = Mathf.Abs(d) < bandShapeUnits;
                    boundary[row + gx] = isEdge;
                    if (isEdge) boundaryCount++;
                }
            }
        }

        /// <summary>
        /// Measure a finished lawn against its target. Identical arithmetic for every contestant.
        /// </summary>
        public static RoundScore Evaluate(byte[] cut, bool[] inside, bool[] boundary, int insideCount,
                                          float cellArea, float driftMetres, float boostMetres, int bonks)
        {
            var s = new RoundScore();
            if (cut == null || inside == null) return s;

            const byte threshold = 128;
            int cutInside = 0, cutOutside = 0, totalCut = 0;
            // The two halves of the outline band, counted apart. Following the line is one thing and
            // slopping over it is another, and the mark below has to be able to tell them apart —
            // which a single edgeGood counter over the whole band could not, because it scored both
            // with the same "does cut match inside" test and then averaged them.
            int edgeInside = 0, edgeInsideCut = 0;
            int edgeOutside = 0, edgeOutsideCut = 0;

            for (int i = 0; i < cut.Length; i++)
            {
                bool isCut = cut[i] >= threshold;
                bool isIn = inside[i];

                if (isCut)
                {
                    totalCut++;
                    if (isIn) cutInside++; else cutOutside++;
                }

                if (boundary[i])
                {
                    if (isIn) { edgeInside++; if (isCut) edgeInsideCut++; }
                    else { edgeOutside++; if (isCut) edgeOutsideCut++; }
                }
            }

            s.coverage = insideCount > 0 ? (float)cutInside / insideCount : 0f;
            s.spill = totalCut > 0 ? (float)cutOutside / totalCut : 0f;
            s.accuracy = Mathf.Clamp01(s.coverage * (1f - 0.65f * s.spill));
            s.mownArea = totalCut * cellArea;
            s.legibility = Legibility(s.coverage);

            // ---- the two gates, and why a quality metric needs one ----
            //
            // TWO OF THE FOUR METRICS USED TO BE SATISFIED BY INACTION, and the owner found it by
            // asking why a player who drew nothing scored 9 out of 30.
            //
            // Neatness was 1 - spill, and spill's zero-guard cannot tell "did not spill" from "did
            // not mow": an untouched lawn divided nothing by nothing, got 0, and was handed a
            // PERFECT 1.0 for neatness. Edge quality counted a boundary cell as correct whenever
            // cut == inside, and on an untouched lawn every cell on the outer half of the band
            // passes that test for free — which is why it sat at 0.516 rather than at nothing.
            // Through the bench's weights those two paid roughly half marks each for doing nothing
            // at all, and mowing a five percent sliver immaculately scored HIGHER (10) than mowing
            // nothing (9), which made "do less" a strategy with a gradient to climb.
            //
            // The cause is not the zero-guard and it is not the number 0.516. It is that a quality
            // is a quality OF something, and both were being paid out before there was anything for
            // them to be a quality of. Neither guard below is load bearing any more.
            //
            // Deliberately NOT a threshold with a special case at zero. A threshold moves the
            // exploit to just above itself — mow one percent more than the test asks and collect
            // full neatness again — where making the credit proportional means every metre of the
            // discount has to be bought back with work.

            // NEATNESS: how little of your mowing missed, times whether you drew a picture at all.
            //
            // Precision is scale free — a single immaculate square metre scores 1.0 — so the gate
            // is supplying information the ratio genuinely does not contain rather than counting
            // the same fact twice. It saturates at the coverage where Legibility says a subject
            // begins to exist, because that is the same question and the game should not hold two
            // opinions about it. Past a quarter filled this is 1 and neatness is exactly what it
            // has always been, which is what keeps a small immaculate lawn beating a big scruffy
            // one — BRAMBLE's whole reason for being on the roster.
            float picture = Mathf.Clamp01(s.coverage / PictureExists);
            s.neatness = (1f - s.spill) * picture;

            // EDGE: how much of the outline you actually cut, discounted by how badly you slopped
            // over it.
            //
            // The old symmetric test asked "does cut match inside" across the whole band. Expand it
            // and it is a MEAN of two half-credits, (onLine + (1 - overLine)) / 2 — and a mean is
            // exactly the wrong operator here, because either term can carry the mark on its own.
            // The second one is "the outside of the shape is still standing", which is true of a
            // lawn nobody has touched, and that is where the free 0.516 came from.
            //
            // So the outline term MULTIPLIES instead of averaging: cut none of the line and no
            // amount of untouched outfield pays you for it. The slop term keeps the half weight the
            // mean gave it, which is not a fudge but the thing that holds the top of the scale
            // still — at onLine = 1 this is 1 - overLine/2, algebraically identical to the old
            // formula, so every contestant who actually got round the outline is marked exactly as
            // before and only the ones who did not are docked.
            //
            // Full weight was tried and is wrong. The outside band is one swath wide, so overLine
            // pins to 1.0 the moment anyone overshoots by a swath and stops carrying information;
            // used at full weight it collapsed the whole field's edge to ~0.02 and took three to
            // four marks off every contestant, which would have moved the yardstick the arenas were
            // just tuned against.
            //
            // Nor is this the symmetric test multiplied by a gate, which is what it was written as
            // first. That double counts — the symmetric test already docks every inside-band cell
            // left standing — and it cost BRAMBLE three marks for one mistake, dropping the
            // immaculate-but-unfinished contestant below the sloppy one and inverting the roster.
            const float slopWeight = 0.5f;
            float onLine = edgeInside > 0 ? (float)edgeInsideCut / edgeInside : 0f;
            float overLine = edgeOutside > 0 ? (float)edgeOutsideCut / edgeOutside : 0f;
            s.edgeQuality = Mathf.Clamp01(onLine * (1f - slopWeight * overLine));

            float driftScore = Mathf.Clamp01(driftMetres / 90f);
            float boostScore = Mathf.Clamp01(boostMetres / 220f);
            float bonkScore = Mathf.Clamp01(bonks / 4f);
            s.style = Mathf.Clamp01(driftScore * 0.45f + boostScore * 0.40f + bonkScore * 0.15f);

            return s;
        }

        /// <summary>
        /// How much of a picture is actually there, on a saturating curve.
        ///
        /// Nothing below a quarter filled — a few passes through a shape is not a heart, it is
        /// scribble, and a contestant who gives up must not be rewarded for the tidiness of what
        /// little they did. Effectively everything by two thirds, because past that point the
        /// subject is already unmistakable and further mowing is not artistry, it is labour.
        ///
        /// The flat top is the whole reform. Under linear coverage the winning strategy was to
        /// cut every square metre inside the outline and accept whatever mess that made, which is
        /// a mowing race rather than a lawn-art championship — and once the chalk started
        /// dissolving mid-round it also made the round unwinnable, because nobody can fill a shape
        /// accurately that they can no longer see. With the top flat, the last third of the
        /// picture is worth almost nothing and the contest moves to neatness and edge, where a
        /// player who remembered less but drew it cleanly genuinely deserves to win.
        /// </summary>
        public static float Legibility(float coverage)
            => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PictureExists, PictureComplete, coverage));

        /// <summary>
        /// The coverage at which there is a picture there at all, and the coverage past which more
        /// of it adds nothing.
        ///
        /// Named rather than typed into <see cref="Legibility"/>, because <see cref="Evaluate"/>
        /// gates neatness on the first of them and the two have to mean the same thing. "A few
        /// passes through a shape is not a heart, it is scribble" is one judgement about one number,
        /// and the day the knee moves without the gate moving with it is the day a contestant can
        /// farm a perfect neatness out of a lawn the same rulebook calls illegible.
        /// </summary>
        public const float PictureExists = 0.25f;
        public const float PictureComplete = 0.66f;

        /// <summary>
        /// One judge's mark out of ten. Every station in the venue calls this, including the
        /// player's own panel — a rival's card and the player's card come off the same curve, so
        /// the standings mean something.
        ///
        /// The three biases are deliberately NOT three weightings of the same quantity any more.
        /// They used to be: two of the three were dominated by coverage, so all three moved
        /// together and the panel had one opinion in three voices. Now they genuinely disagree —
        /// a big scruffy picture wins the Coverage chair and loses the other two, a small
        /// immaculate one does the reverse. That disagreement is the drama of the judging beat,
        /// and it is what lets a 70%-filled lawn beat a 99%-filled one without simply declaring
        /// that filling the shape does not matter.
        /// </summary>
        public static float Mark(RoundScore s, JudgeBias bias, float severity, float floor, float ceiling)
        {
            float raw;
            switch (bias)
            {
                case JudgeBias.Coverage:
                    // The traditionalist: wants to see a lot of lawn cut, and is the one chair
                    // where finishing the picture still pays linearly. Somebody has to reward the
                    // grind, or "mow less" becomes strictly optimal.
                    raw = s.coverage * 0.55f + s.legibility * 0.20f + s.style * 0.25f;
                    break;
                case JudgeBias.Craft:
                    // Only really looks at the outline, and barely cares how much is behind it.
                    raw = s.edgeQuality * 0.62f + s.neatness * 0.20f + s.style * 0.18f;
                    break;
                default:
                    // Wants a picture that reads, drawn without slopping over the edges. Coverage
                    // reaches this chair only through legibility, so filling the last third of the
                    // shape earns nothing here — but failing to make the subject recognisable at
                    // all still costs everything.
                    raw = s.legibility * 0.50f + s.neatness * 0.32f + s.edgeQuality * 0.18f;
                    break;
            }

            // There was a blend here that folded the round's LATER STAGES into every chair's mark —
            // the rally's gardens and Bloom Rush's share of the arena, at a weight a little under a
            // third — so that the arenas were marked by the same three animals, on the same curve, as
            // the mowing. The argument was good and it is no longer available: a round IS a stage
            // now, so there is no round in which a picture and an arena are both scored. The bench
            // marks the picture; a match round is closed by Tournament.CloseMatchRound and never
            // reaches this function at all.
            raw = Mathf.Clamp01(raw);
            float curved = Mathf.Pow(raw, Mathf.Max(severity, 0.05f));
            return Mathf.Clamp(Mathf.Round(Mathf.Lerp(floor, ceiling, curved)), 0f, 10f);
        }

        /// <summary>The three standard station biases, in the order every station seats them.</summary>
        public static readonly JudgeBias[] StationBiases =
            { JudgeBias.Accuracy, JudgeBias.Coverage, JudgeBias.Craft };

        /// <summary>
        /// Standard competition ranking over standings already sorted by total, descending.
        ///
        /// Equal totals share a place and the next place skips it — 1st, 2nd, 2nd, 4th. Numbering
        /// straight off the row index instead would have printed 1st, 2nd, 3rd, 4th for two
        /// contestants on identical marks, which the board sitting right next to them showing the
        /// same number would immediately contradict.
        ///
        /// Marks are whole numbers by the time they reach here (see <see cref="Mark"/>), so exact
        /// comparison would do; the epsilon is there because totals are carried as floats and a
        /// tie that fails by 0.0001 would be invisible on screen and inexplicable in the ranking.
        /// </summary>
        public static int[] CompetitionPlaces(System.Collections.Generic.IReadOnlyList<Standing> sorted)
        {
            if (sorted == null) return System.Array.Empty<int>();

            var places = new int[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                // RoundScore, not total. The list arrives sorted by RoundScore — picture plus the
                // rally's marks — while this compared the PICTURE alone, so the order and the place
                // labels were reading different numbers. Four contestants who mowed equally well
                // and defended completely differently all came out "1st" on a board that had
                // already put them in the right order underneath. Sort key and tie key have to be
                // the same key or the board contradicts itself.
                places[i] = (i > 0 && Mathf.Abs(sorted[i].RoundScore - sorted[i - 1].RoundScore) < 0.001f)
                    ? places[i - 1]
                    : i + 1;
            }
            return places;
        }

        /// <summary>Letter grade for a total out of thirty. Shared by the panel and the scoreboard.</summary>
        public static string Rank(float totalOutOfThirty)
        {
            if (totalOutOfThirty >= 27f) return "S";
            if (totalOutOfThirty >= 23f) return "A";
            if (totalOutOfThirty >= 18f) return "B";
            if (totalOutOfThirty >= 12f) return "C";
            return "D";
        }
    }
}
