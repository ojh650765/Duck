using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// Mows a finished picture into the menu lawn, chalks its outline, and lays the line the mower
    /// drove in on. Runs once on the frame the scene starts, and again whenever the framing changes.
    ///
    /// The menu needs the game's premise visible behind the title, and the premise is a shape cut
    /// into grass. It is stamped through <see cref="CutMask.CutSwath"/> — the same call the mower
    /// makes — rather than baked into a texture, for two reasons. The mask's field extent, flip and
    /// direction channel are set up by CutMask itself, so the only correct way to get a mark into it
    /// is to cut one; and a material carrying its own mask would not survive the SRP Batcher, which
    /// is the failure RivalLawn's class comment was written about.
    ///
    /// Because it is really mown, the blade layer shortens over it and the mow stripes fall out of
    /// the direction each row was cut in, exactly as they do in a round.
    ///
    /// ---- why the outline is also chalked ----
    ///
    /// A mown shape seen from a camera standing on the lawn is a shape squashed along the view axis,
    /// and the squash is severe: at the framing this menu was first written for the heart came out
    /// nine percent of the frame tall against twenty-five percent wide, which is not a heart, it is a
    /// band of pale grass. Raising the camera opens that out and is done — the vista now runs at
    /// thirty percent — but foreshortening never goes away, and a filled area is the worst possible
    /// thing to read through it because its silhouette IS the information.
    ///
    /// A line is not. A closed contour survives being squashed two to one because the eye recovers
    /// the shape from its curvature rather than from its area, which is why a heart drawn on a
    /// stretched sheet of paper is still obviously a heart. So the picture gets the groundskeeper's
    /// chalk outline as well as the mowing, and the shape is then DRAWN rather than inferred.
    ///
    /// It also happens to be what the fair would actually look like mid-competition: the subject is
    /// chalked on before anybody mows it, and the guide is only half worn away by the time the round
    /// is over. Nothing here is a UI overlay pretending to be scenery.
    /// </summary>
    public class MenuLawnArt : MonoBehaviour
    {
        [Tooltip("The picture. Any of the round shapes; it is only ever looked at, never scored.")]
        public ShapeId shape = ShapeId.Heart;
        [Tooltip("Centre of the picture in world XZ. Placed for the menu camera, not for the " +
                 "playfield — this is composition, so it is deliberately off the lawn's centre.")]
        public Vector2 centre = new Vector2(-2f, -21f);
        [Tooltip("Metres from the centre to the edge of the shape's bounding square.")]
        public float radius = 11f;
        [Tooltip("Degrees the picture is turned on the lawn, anticlockwise seen from above.\n\n" +
                 "Not decoration. Every one of these shapes was authored to be read from due south " +
                 "by the overhead reveal, so from a camera standing to the picture's north the " +
                 "feature that identifies it is on the FAR side — the heart's two lobes, the " +
                 "duckling's head — which is the half the perspective compresses hardest. Turning " +
                 "the picture through 180 degrees puts that feature nearest the lens, where the " +
                 "ground is most open, and costs nothing at all.")]
        public float pictureYaw = 180f;

        [Header("Mowing")]
        [Tooltip("Deck width, in metres. Matches the mower's so the rows read at the same scale as " +
                 "a round's.")]
        public float swathWidth = 1.5f;
        [Tooltip("Metres between row centres. Slightly under the deck width, or the rows leave " +
                 "uncut ribbons between them.")]
        public float rowSpacing = 1.28f;
        [Tooltip("Sampling step along a row. Finer than this buys nothing: the shader feathers the " +
                 "swath edge anyway.")]
        public float sampleStep = 0.3f;
        [Tooltip("Metres of uncut grass left inside the outline, so the picture has a crisp border " +
                 "instead of bleeding into the lawn around it.")]
        public float edgeInset = 0.34f;
        [Tooltip("How far the rows bend, in metres. Perfectly straight rows read as a print; a " +
                 "shared bend reads as somebody steering.")]
        public float rowWobble = 0.16f;
        public float rowWobbleScale = 0.34f;

        [Header("The line the mower drove in on")]
        [Tooltip("Cut a single swath from the parked mower to the edge of the picture.\n\n" +
                 "Two jobs, one swath. It explains why a machine is standing on finished work, and " +
                 "it is a bright diagonal running from the biggest object in the frame into the " +
                 "middle of it — a leading line, for the price of one CutSwath call.")]
        public bool approachTrail = true;
        [Tooltip("Where the trail starts, in world XZ. Written by MainMenu from the parked mower's " +
                 "position, so the two cannot disagree about where the machine is.")]
        public Vector2 approachFrom = new Vector2(2.24f, -10.26f);

        [Header("Chalk outline")]
        [Tooltip("Bake the shape into the signed-distance texture the chalk shader reads. Off " +
                 "leaves the menu lawn showing mown grass only.")]
        public bool chalkGuide = true;
        [Tooltip("Resolution of that texture. 256 over a 64 m field is 25 cm a texel, and the " +
                 "shader reconstructs the line from the distance value rather than from the texel, " +
                 "so the stroke stays sharp regardless.")]
        public int chalkRes = 256;
        [Tooltip("Half-width of the distance band stored in the texture, in shape units. Matches " +
                 "RoundTarget's, because the shader decodes both the same way.")]
        public float chalkBand = 0.25f;
        [Tooltip("The sheet the outline is drawn on. Built DISABLED and switched on here, once the " +
                 "texture exists.\n\n" +
                 "Not a nicety. _ShapeRadius and _SdfBand are shader globals with no material " +
                 "fallback, so before anything sets them they are zero — which makes every decoded " +
                 "distance zero, which puts the stroke test inside the line everywhere, which paints " +
                 "the whole 64-metre sheet solid chalk. That is what the saved scene looked like.")]
        public MeshRenderer chalkSheet;

        static readonly int IdTargetSdf = Shader.PropertyToID("_TargetSdf");
        static readonly int IdShapeRadius = Shader.PropertyToID("_ShapeRadius");
        static readonly int IdSdfBand = Shader.PropertyToID("_SdfBand");

        Texture2D _sdf;

        void Start() => Rebuild();

        void OnDestroy()
        {
            if (_sdf != null) Destroy(_sdf);
        }

        /// <summary>
        /// Put a different picture on the lawn. Used by the framing switch, which has to be able to
        /// move the picture because the picture is placed for one camera and the camera can change.
        /// </summary>
        public void Apply(ShapeId newShape, Vector2 newCentre, float newRadius, float newYaw,
                          Vector2 newApproachFrom)
        {
            shape = newShape;
            centre = newCentre;
            radius = newRadius;
            pictureYaw = newYaw;
            approachFrom = newApproachFrom;

            // The whole lawn, not just the old picture. Cutting is additive — there is no uncut
            // stamp — so the only way to remove a mark is to wipe the mask and lay the new one.
            CutMask.Instance?.ClearAll();
            Rebuild();
        }

        public void Rebuild()
        {
            BakeChalk();
            Mow();
        }

        // ------------------------------------------------------------------ shape space

        /// <summary>
        /// World XZ to the [-1,1] space the shapes are authored in, through the picture's placement
        /// and turn. The one function that knows the mapping, so the mowing and the chalk cannot
        /// disagree about where the outline is — which they would show up as a chalk line running
        /// beside the mown edge rather than along it.
        /// </summary>
        Vector2 ToShape(float wx, float wz)
        {
            float r = Mathf.Max(radius, 1e-3f);
            float dx = (wx - centre.x) / r, dz = (wz - centre.y) / r;
            float a = -pictureYaw * Mathf.Deg2Rad;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(dx * c - dz * s, dx * s + dz * c);
        }

        float Sdf(float wx, float wz) => TargetShapes.Sdf(shape, ToShape(wx, wz));

        // ------------------------------------------------------------------ chalk

        /// <summary>
        /// Bake the picture into the global signed-distance texture the chalk shader samples.
        ///
        /// This is what RoundTarget does for a round, and it is deliberately NOT RoundTarget: that
        /// component maps shape space onto the lawn's centre, and the menu picture is placed off
        /// centre and turned round for the camera. Adding an offset and a rotation to the scoring
        /// path to serve a menu would put two knobs on the one object in the game whose geometry the
        /// player's marks depend on.
        ///
        /// Failing safe matters here: with no texture set at all the shader samples white, decodes a
        /// distance of +band everywhere and draws no line — so a menu that cannot bake is a menu
        /// with no chalk, not a menu with a stripe through it.
        /// </summary>
        void BakeChalk()
        {
            if (!chalkGuide)
            {
                if (chalkSheet != null) chalkSheet.enabled = false;
                return;
            }

            int res = Mathf.Clamp(chalkRes, 32, 512);
            if (_sdf == null || _sdf.width != res)
            {
                if (_sdf != null) Destroy(_sdf);
                // Linear, not sRGB: these are distances, and letting Unity apply a colour transfer
                // curve to them bends the line off the outline.
                _sdf = new Texture2D(res, res, TextureFormat.R8, false, true)
                {
                    name = "MenuTargetSdf",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }

            float band = Mathf.Max(chalkBand, 1e-3f);
            var pixels = new Color32[res * res];
            for (int y = 0; y < res; y++)
            {
                float wz = (y + 0.5f) / res * Field.Size - Field.Half;
                int row = y * res;
                for (int x = 0; x < res; x++)
                {
                    float wx = (x + 0.5f) / res * Field.Size - Field.Half;
                    float d = Sdf(wx, wz);
                    byte v = (byte)Mathf.Clamp(Mathf.RoundToInt((d / band * 0.5f + 0.5f) * 255f), 0, 255);
                    pixels[row + x] = new Color32(v, v, v, 255);
                }
            }
            _sdf.SetPixels32(pixels);
            _sdf.Apply(false, false);

            Shader.SetGlobalTexture(IdTargetSdf, _sdf);
            // The shader multiplies the decoded band by this to get metres, so it has to be the
            // radius this picture was baked at rather than the round's 26.
            Shader.SetGlobalFloat(IdShapeRadius, radius);
            Shader.SetGlobalFloat(IdSdfBand, band);

            if (chalkSheet != null) chalkSheet.enabled = true;
        }

        // ------------------------------------------------------------------ mowing

        public void Mow()
        {
            var mask = CutMask.Instance;
            if (mask == null)
            {
                Debug.LogWarning("[Duck] MenuLawnArt: no CutMask in the scene; the menu lawn will " +
                                 "be blank grass.");
                return;
            }

            if (radius < 0.5f)
            {
                Debug.LogWarning($"[Duck] MenuLawnArt: radius {radius} is too small to mow.");
                return;
            }

            // Both steps are floored well above zero. They are inspector fields, and a zero in either
            // is a loop that never advances.
            float spacing = Mathf.Max(rowSpacing, 0.05f);
            float sample = Mathf.Max(sampleStep, 0.05f);

            int rows = Mathf.Max(1, Mathf.CeilToInt(radius * 2f / spacing));
            float inset = radius > 1e-3f ? edgeInset / radius : 0f;
            int spans = 0;

            for (int row = 0; row <= rows; row++)
            {
                float z = centre.y - radius + row * spacing;
                if (z > centre.y + radius) break;

                // Alternate the direction every row. The mask stores the heading each texel was cut
                // at and the ground shader lights a pass coming towards the camera differently from
                // one going away, so this is what produces the stripes — mowing every row the same
                // way gives a correctly shaped picture with no banding in it at all.
                bool rightwards = (row & 1) == 0;

                float spanStart = float.NaN;
                float x = -radius;
                float end = radius + sample;

                for (; x <= end; x += sample)
                {
                    float wx = centre.x + Mathf.Clamp(x, -radius, radius);
                    float wz = z + Mathf.Sin(wx * rowWobbleScale) * rowWobble;
                    bool inside = x <= radius && Sdf(wx, wz) < -inset;

                    if (inside && float.IsNaN(spanStart)) spanStart = wx;
                    if (inside || float.IsNaN(spanStart)) continue;

                    // Run ended: cut it, if it is long enough to be a pass rather than a dab.
                    float spanEnd = wx - sample;
                    if (spanEnd - spanStart >= swathWidth * 0.5f)
                    {
                        CutRow(mask, spanStart, spanEnd, z, rightwards);
                        spans++;
                    }
                    spanStart = float.NaN;
                }
            }

            if (approachTrail) CutApproach(mask);

            // Straight to the mask, rather than waiting for CutMask's own LateUpdate. The camera on
            // this scene is looking at the lawn from the first frame, and a frame of unmown grass is
            // a visible flash on load.
            mask.Flush();

            if (spans == 0)
                Debug.LogWarning($"[Duck] MenuLawnArt: {shape} produced no rows at radius {radius}; " +
                                 "the picture is missing from the menu lawn.");
        }

        void CutRow(CutMask mask, float x0, float x1, float z, bool rightwards)
        {
            Vector3 a = Point(rightwards ? x0 : x1, z);
            Vector3 b = Point(rightwards ? x1 : x0, z);
            mask.CutSwath(a, b, swathWidth);
            // Wheel tracks, pressed just inside the swath. Free depth: the picture stops being a
            // flat stencil and starts being something a machine drove over.
            float track = swathWidth * 0.30f;
            mask.PressTrack(a + Vector3.forward * track, b + Vector3.forward * track, swathWidth * 0.16f, 0.55f);
            mask.PressTrack(a - Vector3.forward * track, b - Vector3.forward * track, swathWidth * 0.16f, 0.55f);
        }

        /// <summary>
        /// The swath from the parked mower to the picture's edge.
        ///
        /// Walked in short steps rather than laid as one long capsule, because the step is what lets
        /// it stop the moment it reaches the outline. A single swath from the mower to the picture's
        /// centre would drive a bright stripe straight through the middle of the artwork.
        /// </summary>
        void CutApproach(CutMask mask)
        {
            Vector2 to = centre;
            Vector2 from = approachFrom;
            Vector2 run = to - from;
            float length = run.magnitude;
            if (length < 1.2f) return;

            Vector2 dir = run / length;
            // Sideways bow, so the trail is a curve somebody steered rather than a ruled line.
            Vector2 side = new Vector2(-dir.y, dir.x);
            const float step = 0.6f;
            Vector2 prev = from;

            for (float t = step; t <= length; t += step)
            {
                float bow = Mathf.Sin(t / length * Mathf.PI) * 0.9f;
                Vector2 p = from + dir * t + side * bow;
                // Stop half a deck inside the shape: the trail should meet the picture and end,
                // not carry on across it.
                if (Sdf(p.x, p.y) < -edgeInset - swathWidth * 0.5f) break;

                var a = new Vector3(prev.x, 0f, prev.y);
                var b = new Vector3(p.x, 0f, p.y);
                mask.CutSwath(a, b, swathWidth * 0.92f);
                Vector3 across = new Vector3(side.x, 0f, side.y) * (swathWidth * 0.30f);
                mask.PressTrack(a + across, b + across, swathWidth * 0.16f, 0.6f);
                mask.PressTrack(a - across, b - across, swathWidth * 0.16f, 0.6f);
                prev = p;
            }
        }

        Vector3 Point(float x, float z)
            => new Vector3(x, 0f, z + Mathf.Sin(x * rowWobbleScale) * rowWobble);
    }
}
