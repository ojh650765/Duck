using UnityEngine;

namespace DuckMow
{
    public struct RoundScore
    {
        public float coverage;     // fraction of the picture that got mown
        public float spill;        // fraction of mowing that landed outside the picture
        public float accuracy;     // coverage penalised by spill — the headline number
        public float edgeQuality;  // how cleanly the outline was followed
        public float style;        // drifting, boosting, showmanship
        public float mownArea;     // square metres actually cut
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

        public RoundScore Evaluate(CutMask mask, float driftMetres, float boostMetres, int bonks)
        {
            var s = new RoundScore();
            if (mask == null || Inside == null) return s;

            byte[] cut = mask.Cut;
            const byte threshold = 128;

            int cutInside = 0, cutOutside = 0, totalCut = 0;
            int edgeGood = 0, edgeTotal = 0;

            for (int i = 0; i < cut.Length; i++)
            {
                bool isCut = cut[i] >= threshold;
                bool isIn = Inside[i];

                if (isCut)
                {
                    totalCut++;
                    if (isIn) cutInside++; else cutOutside++;
                }

                if (Boundary[i])
                {
                    edgeTotal++;
                    // On the outline band we want cut exactly where the picture is and not
                    // where it isn't. Getting this right is what separates a B from an S.
                    if (isCut == isIn) edgeGood++;
                }
            }

            s.coverage = InsideCount > 0 ? (float)cutInside / InsideCount : 0f;
            s.spill = totalCut > 0 ? (float)cutOutside / totalCut : 0f;
            s.accuracy = Mathf.Clamp01(s.coverage * (1f - 0.65f * s.spill));
            s.edgeQuality = edgeTotal > 0 ? (float)edgeGood / edgeTotal : 0f;
            s.mownArea = totalCut * Field.CellArea;

            // Style rewards commitment: long drifts, boost used while still cutting, and the
            // occasional gnome. It is capped so it can never rescue a bad picture.
            float driftScore = Mathf.Clamp01(driftMetres / 90f);
            float boostScore = Mathf.Clamp01(boostMetres / 220f);
            float bonkScore = Mathf.Clamp01(bonks / 4f);
            s.style = Mathf.Clamp01(driftScore * 0.45f + boostScore * 0.40f + bonkScore * 0.15f);

            return s;
        }

        /// <summary>
        /// Where the mower starts: just inside the picture, at its southern end, facing up into it.
        ///
        /// Starting outside the shape is a guaranteed loss — the drive in wastes seconds and lays
        /// a spill line across clean lawn before you have even begun, so every run opened at a
        /// deficit no skill could recover. Starting on the near edge of the picture means the
        /// first metre you cut already counts.
        /// </summary>
        public void GetStartPose(out Vector3 position, out Quaternion rotation)
        {
            // Fall back to the old south-edge spawn only if the shape somehow has no interior.
            position = new Vector3(0f, 0.4f, -Field.Half + 5.5f);
            rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            if (Inside == null || InsideCount == 0) return;

            // Walk north from the bottom of the field and take the first row that has enough
            // clearance for the mower to sit in, then centre on that row's widest span.
            float clearance = 1.2f;
            int clearCells = Mathf.CeilToInt(clearance / Field.MetresPerCell);

            for (int gz = 0; gz < Field.GridRes; gz++)
            {
                int bestStart = -1, bestLen = 0, runStart = -1, runLen = 0;
                int row = gz * Field.GridRes;

                for (int gx = 0; gx < Field.GridRes; gx++)
                {
                    if (Inside[row + gx])
                    {
                        if (runStart < 0) { runStart = gx; runLen = 0; }
                        runLen++;
                        if (runLen > bestLen) { bestLen = runLen; bestStart = runStart; }
                    }
                    else { runStart = -1; runLen = 0; }
                }

                if (bestLen < clearCells * 2) continue;

                int centreX = bestStart + bestLen / 2;
                // Nudge north so the mower's body is fully inside rather than straddling the edge.
                int startZ = Mathf.Min(gz + clearCells, Field.GridRes - 1);
                Vector3 w = Field.GridToWorld(centreX, startZ);
                position = new Vector3(w.x, 0.4f, w.z);
                rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                return;
            }
        }
    }
}
