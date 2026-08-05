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
            int edgeGood = 0, edgeTotal = 0;

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
                    edgeTotal++;
                    if (isCut == isIn) edgeGood++;
                }
            }

            s.coverage = insideCount > 0 ? (float)cutInside / insideCount : 0f;
            s.spill = totalCut > 0 ? (float)cutOutside / totalCut : 0f;
            s.accuracy = Mathf.Clamp01(s.coverage * (1f - 0.65f * s.spill));
            s.edgeQuality = edgeTotal > 0 ? (float)edgeGood / edgeTotal : 0f;
            s.mownArea = totalCut * cellArea;

            float driftScore = Mathf.Clamp01(driftMetres / 90f);
            float boostScore = Mathf.Clamp01(boostMetres / 220f);
            float bonkScore = Mathf.Clamp01(bonks / 4f);
            s.style = Mathf.Clamp01(driftScore * 0.45f + boostScore * 0.40f + bonkScore * 0.15f);

            return s;
        }

        /// <summary>
        /// One judge's mark out of ten. Every station in the venue calls this, including the
        /// player's own panel — a rival's card and the player's card come off the same curve, so
        /// the standings mean something.
        /// </summary>
        public static float Mark(RoundScore s, JudgeBias bias, float severity, float floor, float ceiling)
        {
            float raw;
            switch (bias)
            {
                case JudgeBias.Coverage:
                    // Wants to see a lot of lawn cut and is not fussy about where.
                    raw = s.coverage * 0.70f + s.style * 0.30f;
                    break;
                case JudgeBias.Craft:
                    // Only really looks at the outline.
                    raw = s.edgeQuality * 0.60f + s.style * 0.20f + s.accuracy * 0.20f;
                    break;
                default:
                    // Wants the picture right, and punishes mowing outside it.
                    raw = s.accuracy * 0.75f + s.coverage * 0.25f;
                    raw *= 1f - 0.25f * s.spill;
                    break;
            }

            raw = Mathf.Clamp01(raw);
            float curved = Mathf.Pow(raw, Mathf.Max(severity, 0.05f));
            return Mathf.Clamp(Mathf.Round(Mathf.Lerp(floor, ceiling, curved)), 0f, 10f);
        }

        /// <summary>The three standard station biases, in the order every station seats them.</summary>
        public static readonly JudgeBias[] StationBiases =
            { JudgeBias.Accuracy, JudgeBias.Coverage, JudgeBias.Craft };

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
