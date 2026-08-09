using UnityEngine;

namespace DuckMow
{
    public struct RoundScore
    {
        public float coverage;     // fraction of the picture that got mown
        public float spill;        // fraction of mowing that landed outside the picture
        public float accuracy;     // coverage penalised by spill
        public float edgeQuality;  // how cleanly the outline was followed
        public float style;        // drifting, boosting, showmanship
        public float mownArea;     // square metres actually cut

        /// <summary>
        /// Whether the picture READS, on a curve rather than a ratio.
        ///
        /// This is the number that stopped the competition being a mowing race. Coverage is
        /// linear, so under the old weights a contestant who filled 99% of the shape beat one who
        /// filled 70% no matter how badly the first one did it — which made "cut everything" the
        /// whole strategy and made the round unwinnable the moment the chalk faded and nobody
        /// could fill anything accurately any more.
        ///
        /// Legibility saturates instead. Below about a quarter filled there is no picture there
        /// and it pays nothing; by around two thirds the subject is unmistakable and further
        /// mowing earns nothing more. Above that line the contest is decided on neatness and
        /// edge, which is what a lawn-art championship would actually judge — and it is what
        /// makes a small, clean, half-remembered heart beat a big scruffy one.
        /// </summary>
        public float legibility;

        /// <summary>How little of the mowing landed outside the picture. Simply 1 - spill.</summary>
        public float neatness;

        // There was a `stages` field here, carrying how the duck did in the round's LATER STAGES so
        // the three judges could mark the goose rally and Bloom Rush along with the picture. It is
        // gone, and the reason is that a round IS a stage now: the arenas are rounds two and three,
        // with no picture in them and no bench at the end of them, so nothing can ever fill this in
        // and Scoring.Mark's blend behind it could never fire. A field that only a deleted code path
        // could write is worse than no field — the next author to find it would reasonably assume
        // something still feeds it. What replaced it is Tournament.CloseMatchRound.
    }

    /// <summary>
    /// The picture the duck is asked to mow. Owns three views of the same shape:
    /// a CPU grid for scoring, a signed-distance texture for the chalk guide and the reveal
    /// overlay, and the metadata the UI announces.
    /// </summary>
    [DefaultExecutionOrder(-45)]
    public class RoundTarget : MonoBehaviour
    {
        [Tooltip("Radius in metres that shape space [-1,1] maps onto.")]
        public float shapeRadius = 26f;

        [Tooltip("Half-width of the distance band stored in the SDF texture, in shape units.")]
        public float sdfBand = 0.25f;

        public int sdfTextureRes = 256;

        public ShapeId Shape { get; private set; } = ShapeId.Heart;
        public Texture2D SdfTexture { get; private set; }

        /// <summary>True where the cell centre is inside the picture.</summary>
        public bool[] Inside { get; private set; }
        /// <summary>True where the cell is within a swath-width of the picture's outline.</summary>
        public bool[] Boundary { get; private set; }

        public int InsideCount { get; private set; }
        public int BoundaryCount { get; private set; }
        public float TargetAreaSqm => InsideCount * Field.CellArea;

        static readonly int IdTargetSdf = Shader.PropertyToID("_TargetSdf");
        static readonly int IdShapeRadius = Shader.PropertyToID("_ShapeRadius");
        static readonly int IdSdfBand = Shader.PropertyToID("_SdfBand");

        void Awake()
        {
            Inside = new bool[Field.GridRes * Field.GridRes];
            Boundary = new bool[Inside.Length];
            SdfTexture = new Texture2D(sdfTextureRes, sdfTextureRes, TextureFormat.R8, false, true)
            {
                name = "TargetSdf",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        }

        void OnDestroy()
        {
            if (SdfTexture != null) Destroy(SdfTexture);
        }

        public void Build(ShapeId shape)
        {
            Shape = shape;
            RasterizeGrid();
            BakeSdfTexture();

            Shader.SetGlobalTexture(IdTargetSdf, SdfTexture);
            Shader.SetGlobalFloat(IdShapeRadius, shapeRadius);
            Shader.SetGlobalFloat(IdSdfBand, sdfBand);
        }

        void RasterizeGrid()
        {
            InsideCount = 0;
            BoundaryCount = 0;

            // A cell counts as "boundary" if it is within roughly one mower swath of the outline;
            // that band is what the edge-quality score is measured over.
            float bandShapeUnits = 0.9f / shapeRadius;

            for (int gz = 0; gz < Field.GridRes; gz++)
            {
                int row = gz * Field.GridRes;
                for (int gx = 0; gx < Field.GridRes; gx++)
                {
                    Vector3 w = Field.GridToWorld(gx, gz);
                    Vector2 sp = new Vector2(w.x / shapeRadius, w.z / shapeRadius);
                    float d = TargetShapes.Sdf(Shape, sp);

                    bool inside = d < 0f;
                    Inside[row + gx] = inside;
                    if (inside) InsideCount++;

                    bool boundary = Mathf.Abs(d) < bandShapeUnits;
                    Boundary[row + gx] = boundary;
                    if (boundary) BoundaryCount++;
                }
            }
        }

        void BakeSdfTexture()
        {
            int res = sdfTextureRes;
            var pixels = new Color32[res * res];
            for (int y = 0; y < res; y++)
            {
                float wz = ((y + 0.5f) / res) * Field.Size - Field.Half;
                for (int x = 0; x < res; x++)
                {
                    float wx = ((x + 0.5f) / res) * Field.Size - Field.Half;
                    float d = TargetShapes.Sdf(Shape, new Vector2(wx / shapeRadius, wz / shapeRadius));
                    // Narrow band gives ~5 cm of precision on the outline out of an 8-bit texture.
                    byte v = (byte)Mathf.Clamp(Mathf.RoundToInt((d / sdfBand * 0.5f + 0.5f) * 255f), 0, 255);
                    pixels[y * res + x] = new Color32(v, v, v, 255);
                }
            }
            SdfTexture.SetPixels32(pixels);
            SdfTexture.Apply(false, false);
        }

        // ------------------------------------------------------------------ scoring

        /// <summary>
        /// Measure the player's lawn — through the same code every rival is measured by.
        ///
        /// This used to be its own copy of the arithmetic, sitting a few files away from
        /// <see cref="Scoring.Evaluate"/> and quietly drifting from it. That is a fiction the
        /// standings cannot afford: the whole point of the board at the end is that the player and
        /// three rivals are compared, and they can only be compared if one rulebook produced all
        /// four numbers. The duplicate had already fallen behind — it knew nothing about
        /// legibility or neatness, so the player would have been marked on the old coverage-led
        /// curve while the rivals were marked on the new one, and the player would have lost
        /// every round for reasons nothing on screen could explain.
        ///
        /// The grids differ (the player's lawn is 64 m at 256, a rival's 48 m at 128) but that is
        /// exactly why the shared function takes them as arguments.
        /// </summary>
        public RoundScore Evaluate(CutMask mask, float driftMetres, float boostMetres, int bonks)
        {
            if (mask == null || Inside == null) return default;
            return Scoring.Evaluate(mask.Cut, Inside, Boundary, InsideCount, Field.CellArea,
                                    driftMetres, boostMetres, bonks);
        }

        /// <summary>
        /// Where the mower starts: the point furthest inside the picture, facing along it.
        ///
        /// Starting outside the shape is a guaranteed loss. The drive in wastes seconds and lays a
        /// spill line across clean lawn before the round has really begun, so the run opens at a
        /// deficit no amount of skill can recover — and under artistry-led scoring that opening
        /// line is not a small penalty, it is a permanent scar through the middle of the mark.
        ///
        /// The previous version took the widest interior run on the lowest interior ROW, then
        /// nudged the spawn 1.2 m north so the mower's body would clear the edge — without ever
        /// checking that the nudged position was still inside. Any shape that narrows, curves or
        /// notches on the way north walked the player straight back out of the picture: the
        /// duckling's tail, the heart's cleft, the anchor's shank.
        ///
        /// So it no longer reasons about rows at all. The signed distance field already knows how
        /// far every point is from the outline, so the deepest interior point is simply its
        /// minimum — the one place on the lawn with the most clearance in every direction at once.
        /// </summary>
        public void GetStartPose(out Vector3 position, out Quaternion rotation)
        {
            // Fall back to the old south-edge spawn only if the shape somehow has no interior.
            position = new Vector3(0f, 0.4f, -Field.Half + 5.5f);
            rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            if (Inside == null || InsideCount == 0) return;

            float deepest = 0f;
            int bestX = -1, bestZ = -1;

            for (int gz = 0; gz < Field.GridRes; gz++)
            {
                int row = gz * Field.GridRes;
                for (int gx = 0; gx < Field.GridRes; gx++)
                {
                    if (!Inside[row + gx]) continue;
                    Vector3 w = Field.GridToWorld(gx, gz);
                    float d = TargetShapes.Sdf(Shape, new Vector2(w.x / shapeRadius, w.z / shapeRadius));
                    // Inside is negative, so the most negative sample is the furthest in.
                    if (d >= deepest) continue;
                    deepest = d;
                    bestX = gx;
                    bestZ = gz;
                }
            }

            if (bestX < 0) return;

            Vector3 p = Field.GridToWorld(bestX, bestZ);
            position = new Vector3(p.x, 0.4f, p.z);

            // Face along the picture rather than at its nearest wall: whichever of the four
            // headings has the most room ahead of it before the outline. A mower pointed at an
            // edge spends its first move turning around, which is a wasted second and a scuffed
            // patch right where the player is standing.
            rotation = Quaternion.LookRotation(BestHeading(position), Vector3.up);
        }

        /// <summary>The compass direction with the most picture in front of it, from a point inside.</summary>
        Vector3 BestHeading(Vector3 from)
        {
            Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            Vector3 best = Vector3.forward;
            float bestRun = -1f;

            foreach (var dir in dirs)
            {
                float run = 0f;
                for (float t = 0.5f; t < shapeRadius * 2f; t += 0.5f)
                {
                    Vector3 s = from + dir * t;
                    if (TargetShapes.Sdf(Shape, new Vector2(s.x / shapeRadius, s.z / shapeRadius)) >= 0f) break;
                    run = t;
                }
                if (run > bestRun) { bestRun = run; best = dir; }
            }
            return best;
        }
    }
}
