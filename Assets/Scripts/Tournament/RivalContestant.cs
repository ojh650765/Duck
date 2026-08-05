using System;
using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// One rival working their own plot, for the whole round, at their own level of skill.
    ///
    /// This is not an AI driver and it is deliberately not a racer. It is a contestant: it follows
    /// a boustrophedon route through its own picture, makes the kind of mistakes its character
    /// would make, and finishes with an artwork that gets judged by the same rules as the player's.
    /// It has no collider, no rigidbody and no awareness of the player at all — the only thing it
    /// can affect is its own lawn, which is the point.
    ///
    /// Cost: one 256² render texture, a 128² byte grid, and a few dozen swath stamps a second.
    /// Three of these together are cheaper than the player's blade layer.
    /// </summary>
    public class RivalContestant : MonoBehaviour
    {
        [Header("Identity")]
        public string displayName = "Rival";
        public string species = "goose";
        public Color liveryColour = Color.white;

        [Header("Plot")]
        [Tooltip("Centre of this contestant's lawn in world XZ.")]
        public Vector2 plotCentre;
        public float plotSize = 48f;
        [Tooltip("World radius that shape space [-1,1] maps onto for this plot.")]
        public float shapeRadius = 19f;

        [Header("Contestant")]
        [Range(0f, 1f)]
        [Tooltip("General competence. Only decides how harshly this contestant's own station marks " +
                 "them and how long they falter when the chalk goes — the actual mowing comes from " +
                 "pace and precision below.")]
        public float skill = 0.6f;
        [Tooltip("How wildly this one wanders off the line. Character, not difficulty.")]
        public float flair = 0.4f;

        // Pace and precision used to be one number.
        //
        // That was the problem with the old field: a contestant good at mowing was good at ALL of
        // it, so the three rivals were one rival at three difficulties and the standings were
        // always in the same order. Splitting the axis is what lets HORACE finish the whole
        // picture badly while BRAMBLE finishes two thirds of it beautifully — and with the judges
        // now genuinely disagreeing about which of those is better, that produces a different
        // winner depending on the round instead of a fixed pecking order.
        [Range(0f, 1f)]
        [Tooltip("How much of the route gets finished before the klaxon. 1 completes the picture; " +
                 "0.6 runs out of clock with a third of it still standing.")]
        public float pace = 0.8f;
        [Range(0f, 1f)]
        [Tooltip("Line discipline: pass spacing, how far each pass overshoots the outline, and how " +
                 "much the machine weaves. High precision leaves a clean edge and little spill.")]
        public float precision = 0.6f;

        [Header("Memory")]
        [Tooltip("How this contestant misremembers the picture once the ground guide has gone.")]
        public MemoryStyle memoryStyle = MemoryStyle.Proportion;
        [Tooltip("How badly. 0 recalls perfectly, 1 makes a mess only a relative would praise.")]
        [Range(0f, 1f)] public float memoryError = 0.45f;
        [Tooltip("Fraction of the round at which the guide is gone. Set by the tournament so every " +
                 "contestant loses the picture on the same beat the player does.")]
        [Range(0f, 1f)] public float guideLostFraction = 0.24f;
        [Tooltip("Seconds this contestant stalls when the guide vanishes. Visible hesitation is how " +
                 "the player learns the rivals are under the same rule.")]
        public float stallOnGuideLost = 0.9f;

        [Header("Mowing")]
        public float swathWidth = 1.7f;
        public float baseSpeed = 8.5f;
        public float lineSpacing = 1.45f;
        [Tooltip("How this contestant approaches the picture. The single biggest reason two rivals " +
                 "look like different people rather than like the same bot at two difficulties.")]
        public MowStrategy strategy = MowStrategy.Rows;
        // Turning and acceleration deliberately mirror MowerController's numbers. A rival that is
        // more agile than the player is not a harder opponent, it is a different game being played
        // on the next lawn along — and it was why three contestants kept beating a player who was
        // driving well.
        [Tooltip("Degrees per second at a standstill. The player's machine gets 165.")]
        public float turnRateLow = 165f;
        [Tooltip("Degrees per second at cruising speed. The player's machine gets 88 — going fast " +
                 "costs accuracy, and that has to be true for everybody.")]
        public float turnRateHigh = 88f;
        // Lowered from 9. A mower is a heavy machine with a small engine, and at 9 m/s² the rivals
        // snapped to cruising speed out of every corner in about a second — which read as them
        // being on rails while the player's machine visibly laboured.
        [Tooltip("Metres per second squared. The machine has to wind back up out of every corner.")]
        public float acceleration = 4.5f;

        [Tooltip("Seconds spent looking at the picture before setting off. Nobody drops the blade " +
                 "on the klaxon — and a rival that does makes the player feel slow for hesitating.")]
        public float reactionDelay = 1.5f;

        [Header("Obstacles")]
        // The player's lawn has knockable gnomes and the rivals' lawns had nothing, which meant the
        // one hazard in the game applied only to the contestant being asked to avoid it. Rivals
        // have no colliders and never will — they are route followers, not driven machines — so
        // this is proximity tested against the route instead. The cost is the same one the player
        // pays: time, and a swerve in the middle of an outline.
        [Tooltip("Ornaments standing on this plot. Cleared and stood back up at the start of a round.")]
        public Transform[] obstacles = Array.Empty<Transform>();
        [Tooltip("How close the machine has to pass to clatter into one.")]
        public float obstacleRadius = 1.15f;
        [Tooltip("Seconds lost to a hit. Deliberately longer than the player's stagger — a rival " +
                 "has to get off the machine and stand the thing back up.")]
        public float obstacleStall = 0.8f;

        [Header("Presentation")]
        public Transform mowerVisual;
        public Transform bladeSpinner;
        [Tooltip("This plot's grass. Bound to the plot's own mask when the round starts.")]
        public RivalLawn lawn;
        [Tooltip("This plot's chalk outline. Bound to the same mask, so the guide scuffs as it mows.")]
        public RivalChalk chalk;

        public ShapeId Shape { get; private set; }
        public RoundScore Score { get; private set; }
        public float[] Marks { get; } = new float[3];
        public float Total { get; private set; }
        public string Rank { get; private set; } = "D";
        public bool Finished { get; private set; }
        public Vector3 MowerPosition => _pos;
        public float Coverage => _insideCount > 0 ? (float)_cutInsideEstimate / _insideCount : 0f;

        /// <summary>Fired when this rival does something the crowd would react to.</summary>
        public event Action<RivalEvent> OnEvent;

        PlotMask _mask;
        bool[] _inside, _boundary;
        int _insideCount, _boundaryCount;
        int _cutInsideEstimate;

        Vector3 _pos, _prevPos;
        float _heading;
        float _spin;
        float _driftMetres, _boostMetres;
        int _bonks;

        Vector3[] _route = Array.Empty<Vector3>();
        /// <summary>Leg i runs from _route[i] to _route[i+1]; true if the blade is down on it.</summary>
        bool[] _legCuts = Array.Empty<bool>();
        int _waypoint;
        float _waypointTimer;
        float _speed;
        float _reactionTimer;
        float _eventTimer;
        float _boostTimer;
        System.Random _rng;

        Vector2 _driftDir = Vector2.one;
        float _stallTimer;
        bool _guideWasUp = true;

        bool[] _obstacleDown;
        Vector3[] _obstacleHome;
        Quaternion[] _obstacleHomeRot;

        [Tooltip("Duck/CutStamp")]
        public Shader stampShader;

        public PlotMask Mask => _mask;

        void Awake()
        {
            _rng = new System.Random(displayName.GetHashCode() ^ Mathf.RoundToInt(plotCentre.x * 31f + plotCentre.y * 7f));
        }

        void OnDestroy() => _mask?.Dispose();

        /// <summary>Start a round on this plot. Called by the tournament, once, for everyone.</summary>
        public void Begin(ShapeId shape)
        {
            Shape = shape;
            Finished = false;
            Total = 0f;
            Rank = "D";
            Score = default;
            _cutInsideEstimate = 0;
            _driftMetres = 0f; _boostMetres = 0f; _bonks = 0;
            _waypoint = 0; _eventTimer = 1.5f + (float)_rng.NextDouble() * 4f; _boostTimer = 0f;
            _stallTimer = 0f;
            _guideWasUp = true;

            // Which way this contestant's memory of the picture wanders. Fixed per round rather
            // than per pass, because a drift that changes direction halfway reads as a wobble
            // rather than as a misplaced picture.
            float ang = (float)_rng.NextDouble() * Mathf.PI * 2f;
            _driftDir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));

            ResetObstacles();

            _mask ??= new PlotMask(plotCentre, plotSize, stampShader);
            _mask.Clear();
            if (lawn == null) lawn = FindLawn();
            lawn?.Bind(_mask.Texture);
            if (chalk == null) chalk = FindChalk();
            chalk?.Bind(_mask.Texture);

            Scoring.Rasterize(shape, PlotMask.GridRes, plotSize, shapeRadius, swathWidth * 0.5f,
                              out _inside, out _boundary, out _insideCount, out _boundaryCount);

            BuildRoute();

            _pos = _route.Length > 0 ? _route[0] : new Vector3(plotCentre.x, 0f, plotCentre.y);
            _prevPos = _pos;
            _waypointTimer = 0f;
            _speed = 0f;
            // A beat spent looking at the picture before setting off, longer for the ditherers.
            // Without it every rival dropped the blade on the exact frame of the klaxon, which is
            // not something a person does — and it made the player's own moment of orienting
            // themselves feel like a mistake they were being punished for.
            _reactionTimer = reactionDelay * Mathf.Lerp(1.5f, 0.6f, pace)
                           + (float)_rng.NextDouble() * 0.8f;
            // Start already pointing along the first leg, so the round does not open with every
            // contestant spinning on the spot to face their work.
            if (_route.Length > 1)
            {
                Vector3 first = _route[1] - _route[0]; first.y = 0f;
                if (first.sqrMagnitude > 1e-4f) _heading = Mathf.Atan2(first.x, first.z) * Mathf.Rad2Deg;
            }
            PlaceVisual();
        }

        /// <summary>
        /// The route: horizontal passes through the picture, spaced a little wider than the swath
        /// so a careless contestant leaves stripes of uncut grass inside their own outline.
        ///
        /// Skill decides the spacing and how far past the outline each pass runs. A poor contestant
        /// overshoots into clean lawn on every pass, which is exactly the mistake the player is
        /// trying not to make — so watching a rival is a lesson as well as decoration.
        ///
        /// The important part is that the shape being traced CHANGES partway down the route.
        ///
        /// Every contestant loses the ground guide at the same moment the player does, so a rival
        /// that kept querying the true distance field for the whole round would draw a flawless
        /// picture from memory — and three flawless neighbours next to the player's half-remembered
        /// one does not read as "they are good", it reads as "they can still see it and I cannot".
        /// That single impression would undo the round.
        ///
        /// So the passes laid down before the guide dies use the real shape, and everything after
        /// uses this contestant's misremembering of it, ramped in over a few passes rather than
        /// switched. What comes out is a picture that is confidently right at one end and
        /// confidently wrong at the other, which is exactly what a drawing from memory looks like.
        /// </summary>
        void BuildRoute()
        {
            var pts = new System.Collections.Generic.List<Vector3>(256);
            var cuts = new System.Collections.Generic.List<bool>(256);

            switch (strategy)
            {
                case MowStrategy.OutlineFirst:
                    // Secure the edge, then fill what is left. The outline goes down while the
                    // chalk is still there, which is why this contestant's silhouette survives
                    // even when the middle of their picture falls apart.
                    BuildOutlinePass(pts, cuts, 0f, 0.22f);
                    BuildRowPasses(pts, cuts, 0.26f, 1f);
                    break;

                case MowStrategy.Spiral:
                    BuildSpiralPasses(pts, cuts);
                    break;

                default:
                    BuildRowPasses(pts, cuts, 0f, 1f);
                    break;
            }

            // Everybody starts on the same mark.
            //
            // The player is spawned at the point furthest inside the picture (RoundTarget.
            // GetStartPose) precisely because starting outside it is an unrecoverable handicap:
            // seconds are spent driving in, and the drive lays a spill line across clean lawn
            // before the round has begun. The rivals were simply dropped at whatever their route
            // happened to start with — usually the bottom edge of the shape — so they began under
            // a different rule from the contestant they are being compared against.
            //
            // The approach leg is explicitly not a cutting one. They drive out to their first pass
            // with the blade up, exactly as a player would.
            if (pts.Count > 0)
            {
                Vector3 mark = DeepestInteriorPoint();
                if ((mark - pts[0]).sqrMagnitude > 0.25f)
                {
                    pts.Insert(0, mark);
                    cuts.Insert(0, false);
                }
            }

            _route = pts.ToArray();
            _legCuts = cuts.ToArray();
        }

        /// <summary>
        /// The point with the most picture around it in every direction — the same rule the
        /// player's spawn uses, applied to this plot's own shape and radius.
        /// </summary>
        Vector3 DeepestInteriorPoint()
        {
            float half = plotSize * 0.5f;
            const int res = 40;

            float deepest = 0f;
            Vector2 best = Vector2.zero;

            for (int gz = 0; gz <= res; gz++)
            {
                float z = Mathf.Lerp(-half, half, gz / (float)res);
                for (int gx = 0; gx <= res; gx++)
                {
                    float x = Mathf.Lerp(-half, half, gx / (float)res);
                    // Recall 0: at the start of the round the guide is still on the ground and
                    // every contestant can see exactly where the picture is.
                    float d = RememberedSdf(x, z, 0f);
                    if (d >= deepest) continue;
                    deepest = d;
                    best = new Vector2(x, z);
                }
            }

            return new Vector3(plotCentre.x + best.x, 0f, plotCentre.y + best.y);
        }

        /// <summary>Append a point, recording whether the leg that reaches it is a cutting pass.</summary>
        static void Add(System.Collections.Generic.List<Vector3> pts,
                        System.Collections.Generic.List<bool> cuts, Vector3 p, bool cutting)
        {
            if (pts.Count > 0) cuts.Add(cutting);
            pts.Add(p);
        }

        /// <summary>How far this contestant thinks the outline is, along one heading from the plot centre.</summary>
        float BoundaryRadius(float angle, float recall)
        {
            if (RememberedSdf(0f, 0f, recall) >= 0f) return -1f;   // centre is not inside this shape

            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float lo = 0f, hi = plotSize * 0.5f;
            for (int i = 0; i < 16; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (RememberedSdf(dir.x * mid, dir.y * mid, recall) < 0f) lo = mid; else hi = mid;
            }
            return lo;
        }

        /// <summary>Recall strength at a given fraction of the way through the route.</summary>
        float RecallAt(float t) => Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(guideLostFraction * 0.65f, guideLostFraction * 1.7f, t)) * memoryError;

        /// <summary>
        /// A lap of the outline, just inside it.
        ///
        /// This is the "get the edge down before you lose it" plan, and it is the single most
        /// legible difference between contestants: a lawn whose silhouette is crisp but whose
        /// middle is patchy reads completely differently from one that is solid but shapeless.
        /// </summary>
        void BuildOutlinePass(System.Collections.Generic.List<Vector3> pts,
                              System.Collections.Generic.List<bool> cuts, float tFrom, float tTo)
        {
            const int steps = 72;
            float inset = swathWidth * 0.5f;

            for (int i = 0; i <= steps; i++)
            {
                float f = i / (float)steps;
                float ang = f * Mathf.PI * 2f;
                float r = BoundaryRadius(ang, RecallAt(Mathf.Lerp(tFrom, tTo, f)));
                if (r < inset + 0.2f) continue;

                r -= inset;
                // Imprecise contestants cut the corner or swing wide on the bends.
                r += ((float)_rng.NextDouble() - 0.5f) * flair * (1f - precision) * 2.4f;

                Add(pts, cuts, new Vector3(plotCentre.x + Mathf.Cos(ang) * r, 0f,
                                           plotCentre.y + Mathf.Sin(ang) * r), true);
            }
        }

        /// <summary>
        /// Boustrophedon rows — but honouring every separate span on each row.
        ///
        /// The old version took the first and last interior sample on a row and drove straight
        /// between them. On any shape that is not convex that fills the gaps: the star lost its
        /// points, the heart lost its cleft, and the smiley was driven straight through both eyes
        /// and the mouth. Every contestant produced the same featureless blob, which is exactly
        /// why they were impossible to tell apart.
        /// </summary>
        void BuildRowPasses(System.Collections.Generic.List<Vector3> pts,
                            System.Collections.Generic.List<bool> cuts, float tFrom, float tTo)
        {
            float spacing = Mathf.Lerp(lineSpacing * 1.5f, lineSpacing * 0.82f, precision);
            float overshoot = Mathf.Lerp(2.6f, -0.15f, precision);
            float half = plotSize * 0.5f;
            float zFrom = -half + spacing, zTo = half - spacing;

            // A careless contestant keeps the blade down through the turn and scrubs a scar into
            // clean lawn; a careful one lifts. This is where most of the spill difference between
            // HORACE and BRAMBLE actually comes from.
            bool cutOnTurns = precision < 0.55f;

            var spans = new System.Collections.Generic.List<Vector2>(8);
            bool leftToRight = true;

            for (float z = zFrom; z <= zTo; z += spacing)
            {
                float t = Mathf.Lerp(tFrom, tTo, Mathf.InverseLerp(zFrom, zTo, z));
                float recall = RecallAt(t);

                CollectSpans(z, recall, half, spans);
                if (spans.Count == 0) continue;

                // Widen each span, but never far enough to swallow the gap to its neighbour —
                // that is what turned holes back into solid fill.
                for (int i = 0; i < spans.Count; i++)
                {
                    float a = spans[i].x - overshoot;
                    float b = spans[i].y + overshoot;
                    if (i > 0) a = Mathf.Max(a, spans[i - 1].y + (spans[i].x - spans[i - 1].y) * 0.35f);
                    if (i < spans.Count - 1) b = Mathf.Min(b, spans[i].y + (spans[i + 1].x - spans[i].y) * 0.65f);
                    spans[i] = new Vector2(a, b);
                }

                for (int k = 0; k < spans.Count; k++)
                {
                    int idx = leftToRight ? k : spans.Count - 1 - k;
                    var s = spans[idx];
                    if (s.y - s.x < swathWidth * 0.6f) continue;

                    float j0 = ((float)_rng.NextDouble() - 0.5f) * flair * 1.8f;
                    float j1 = ((float)_rng.NextDouble() - 0.5f) * flair * 1.8f;

                    Vector3 a = new Vector3(plotCentre.x + s.x + j0, 0f, plotCentre.y + z);
                    Vector3 b = new Vector3(plotCentre.x + s.y + j1, 0f, plotCentre.y + z);
                    Vector3 from = leftToRight ? a : b;
                    Vector3 to = leftToRight ? b : a;

                    // Swing round rather than reversing into the next pass.
                    if (pts.Count > 0) AddTurn(pts, cuts, pts[pts.Count - 1], from, cutOnTurns);
                    Add(pts, cuts, from, false);
                    Add(pts, cuts, to, true);
                }

                leftToRight = !leftToRight;
            }
        }

        /// <summary>
        /// Rings working inward from the outline, following the shape rather than a circle.
        ///
        /// The bold plan: big continuous sweeps, no rows, and no natural place to stop — which is
        /// why it produces a lawn that is dense in the middle and ragged at every edge.
        /// </summary>
        void BuildSpiralPasses(System.Collections.Generic.List<Vector3> pts,
                               System.Collections.Generic.List<bool> cuts)
        {
            float spacing = Mathf.Lerp(lineSpacing * 1.5f, lineSpacing * 0.82f, precision);
            const int steps = 56;

            // Rings are expressed as a fraction of the boundary radius, so they hug the picture.
            float ringCount = Mathf.Max(3f, shapeRadius / Mathf.Max(spacing, 0.4f));
            int rings = Mathf.Clamp(Mathf.RoundToInt(ringCount), 3, 22);

            for (int ring = 0; ring < rings; ring++)
            {
                float shrink = 1f - (ring + 0.5f) / rings;
                for (int i = 0; i <= steps; i++)
                {
                    float f = i / (float)steps;
                    float t = (ring + f) / rings;
                    float ang = f * Mathf.PI * 2f + ring * 0.35f;

                    float r = BoundaryRadius(ang, RecallAt(t));
                    if (r < 0.3f) continue;

                    r *= Mathf.Clamp01(shrink + 0.04f);
                    r += ((float)_rng.NextDouble() - 0.5f) * flair * (1f - precision) * 2.0f;
                    if (r < 0.2f) continue;

                    Add(pts, cuts, new Vector3(plotCentre.x + Mathf.Cos(ang) * r, 0f,
                                               plotCentre.y + Mathf.Sin(ang) * r), true);
                }
            }
        }

        /// <summary>Every separate interior interval along one row, in plot-local x.</summary>
        void CollectSpans(float z, float recall, float half, System.Collections.Generic.List<Vector2> spans)
        {
            spans.Clear();
            const int samples = 128;

            bool inSpan = false;
            float start = 0f, prev = -half;

            for (int i = 0; i <= samples; i++)
            {
                float x = Mathf.Lerp(-half, half, i / (float)samples);
                bool inside = RememberedSdf(x, z, recall) < 0f;

                if (inside && !inSpan) { inSpan = true; start = x; }
                else if (!inside && inSpan)
                {
                    inSpan = false;
                    if (prev - start >= swathWidth * 0.5f) spans.Add(new Vector2(start, prev));
                }
                prev = x;
            }
            if (inSpan && prev - start >= swathWidth * 0.5f) spans.Add(new Vector2(start, prev));
        }

        /// <summary>
        /// A turnaround arc between two passes.
        ///
        /// Nobody reverses a ride-on mower. Without this the machine's heading snapped through 180
        /// degrees at the end of every row, which reads as the model being rotated rather than as
        /// anything being driven — and it was the main reason the rivals looked like markers
        /// sliding along a grid instead of contestants.
        /// </summary>
        void AddTurn(System.Collections.Generic.List<Vector3> pts,
                     System.Collections.Generic.List<bool> cuts, Vector3 from, Vector3 to, bool cutting)
        {
            Vector3 centre = new Vector3(plotCentre.x, 0f, plotCentre.y);
            Vector3 mid = (from + to) * 0.5f;
            Vector3 outward = mid - centre;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-4f) outward = Vector3.right;
            outward.Normalize();

            float bulge = Mathf.Max(Vector3.Distance(from, to) * 0.45f, swathWidth * 0.9f);
            Vector3 apex = mid + outward * bulge;

            // Two points on the way round, so the turn is an arc rather than a corner.
            Add(pts, cuts, Vector3.Lerp(from, apex, 0.6f), cutting);
            Add(pts, cuts, Vector3.Lerp(apex, to, 0.4f), cutting);
        }

        /// <summary>
        /// The picture as this contestant thinks it went, at <paramref name="amount"/> strength.
        ///
        /// Works by pushing the query point through the INVERSE of the misremembering and asking
        /// the real shape — so a contestant who recalls the star as too wide draws a star that is
        /// too wide, rather than one that has been sampled wrongly. At amount 0 every style is the
        /// identity, which is what lets the ramp in <see cref="BuildRoute"/> be continuous.
        ///
        /// Each style is one specific, legible kind of wrong. That matters more than the amount:
        /// "MARGOT drew it squashed" is a story the player can read off the lawn from sixty metres,
        /// where a general noisy wrongness just looks like a bad mower.
        /// </summary>
        float RememberedSdf(float x, float z, float amount)
        {
            Vector2 s = new Vector2(x / shapeRadius, z / shapeRadius);

            // These coefficients are roughly double what they started at, and the reason is worth
            // recording because it is not obvious.
            //
            // The first pass used gentle, plausible-looking errors — a picture 12% too wide, a
            // couple of metres off centre — and the rivals sailed through them. The cause was the
            // scoring reform, not the errors: legibility saturates at two thirds coverage, so a
            // shape that still overlaps 80% of the target scores full marks for reading correctly,
            // and a small distortion does not push it below that line. Making the marks forgiving
            // of an UNFINISHED picture had quietly made them forgiving of a WRONG one too.
            //
            // So the errors have to be large enough to cost real coverage and push mowing outside
            // the true outline, where spill and edge quality can see it. The ceiling is
            // recognisability: past roughly these values the lawn stops looking like a bad attempt
            // at the subject and starts looking like a bug.
            if (amount > 1e-4f)
            {
                float e = amount;
                switch (memoryStyle)
                {
                    case MemoryStyle.Proportion:
                        // Right shape, wrong aspect: remembered too wide and too short. The most
                        // human of the errors, and still the one that scores best of the four.
                        s.x /= 1f + e * 0.80f;
                        s.y /= 1f - e * 0.45f;
                        break;

                    case MemoryStyle.Skew:
                        // Remembered on a tilt. Reads as a picture that slid round as it went.
                        float a = e * 0.55f;
                        float ca = Mathf.Cos(-a), sa = Mathf.Sin(-a);
                        s = new Vector2(s.x * ca - s.y * sa, s.x * sa + s.y * ca);
                        break;

                    case MemoryStyle.Drift:
                        // Right shape, wrong place — the error the player's anchors exist to
                        // prevent. A contestant with no anchors of their own walks it off centre.
                        // At a rival's 19 m shape radius this is about six metres of miss, which
                        // is enough to strand a third of the picture on clean lawn.
                        s -= new Vector2(_driftDir.x, _driftDir.y) * (e * 0.58f);
                        break;

                    case MemoryStyle.Lopsided:
                        // One half remembered, the other invented. Only the far side distorts, so
                        // the picture is recognisable right up until it is not.
                        if (s.x > 0f) s.x /= 1f + e * 1.40f;
                        break;
                }
            }

            return TargetShapes.Sdf(Shape, s);
        }

        /// <summary>
        /// Advance the contestant. <paramref name="progress01"/> is how far through the round the
        /// venue is, and it is what stops a rival finishing in twenty seconds and then sitting
        /// still for a minute: the route is paced to run out roughly when the klaxon goes, with
        /// skill deciding how much of it actually gets done.
        /// </summary>
        public void Tick(float dt, float progress01, float guideVisibility = 0f)
        {
            if (Finished || _route.Length < 2) return;

            _spin += dt * 18f;
            if (bladeSpinner != null) bladeSpinner.localRotation = Quaternion.Euler(0f, _spin * 57.3f, 0f);

            // Falter when the chalk goes.
            //
            // Fairness in this round is something the player has to SEE, not be told. A rival that
            // carries on at full speed through the one moment that just cost the player their
            // bearings looks like it is playing a different game — so they stop for a beat, look
            // up, and then carry on less certainly than before.
            bool guideUp = guideVisibility > 0.01f;
            if (_guideWasUp && !guideUp)
            {
                _stallTimer = stallOnGuideLost * Mathf.Lerp(1.4f, 0.6f, skill);
                OnEvent?.Invoke(new RivalEvent(RivalEventKind.Mistake, this, _pos));
            }
            _guideWasUp = guideUp;

            if (_reactionTimer > 0f)
            {
                _reactionTimer -= dt;
                // Still on the mark, still winding down whatever speed it had. Sitting there at
                // cruising speed with the wheels stopped would be worse than not waiting at all.
                _speed = Mathf.MoveTowards(_speed, 0f, acceleration * 2f * dt);
                PlaceVisual();
                return;
            }

            if (_stallTimer > 0f)
            {
                _stallTimer -= dt;
                _speed = Mathf.MoveTowards(_speed, 0f, acceleration * 3f * dt);
                PlaceVisual();
                return;
            }

            // Pace: a strong contestant covers the whole route in the time available, a weak one
            // gets three quarters of the way and runs out of clock.
            float wanted = Mathf.Lerp(0.55f, 1.05f, pace) * progress01 * (_route.Length - 1);
            if (_waypoint >= _route.Length - 1) { Finish(); return; }

            _boostTimer -= dt;
            bool boosting = _boostTimer > 0f;
            float topSpeed = baseSpeed * (boosting ? 1.55f : 1f);

            // Chase the paced target rather than free-running, so everyone crosses the line
            // together — but only just.
            //
            // The catch-up used to be 1.5x, which was a rubber band strong enough to rescue a
            // contestant from any amount of lost time: they could clatter into a gnome, stall for
            // the guide, take every corner badly, and still finish the whole picture. It made pace
            // and precision decorative, because the route always completed regardless. At 1.12x it
            // smooths the pacing without undoing the cost of a mistake.
            float lead = wanted - _waypoint;
            if (lead < -0.35f) topSpeed *= 0.35f;
            else if (lead > 1.2f) topSpeed *= 1.12f;

            Vector3 target = _route[_waypoint + 1];
            Vector3 flat = target - _pos; flat.y = 0f;
            float dist = flat.magnitude;

            // Turning authority falls off with speed, exactly as the player's does.
            //
            // This is the single biggest reason the rivals were beating the player on lawns they
            // had no business winning. The player's machine gets 165 deg/s at a standstill and
            // only 88 at top speed — the whole game is built on that trade, and the class comment
            // on MowerController says so: going fast is always the wrong choice for accuracy. The
            // rivals were exempt. They held a flat 150 deg/s at full speed, nearly twice the
            // player's authority, so they could take a corner at cruise that the player has to
            // slow right down for. Matching the curve puts every contestant under the same rule.
            float speedFrac = Mathf.Clamp01(_speed / Mathf.Max(baseSpeed, 0.01f));
            float turnRate = Mathf.Lerp(turnRateLow, turnRateHigh, speedFrac);

            // Arrival radius scales with the turning circle: a machine that cannot turn tightly
            // must be allowed to call a waypoint reached before it is exactly on it, or it orbits.
            float turnRadius = Mathf.Max(_speed, 0.5f) / Mathf.Max(turnRate * Mathf.Deg2Rad, 1e-3f);
            float arrive = Mathf.Max(0.4f, turnRadius * 0.55f);

            _waypointTimer += dt;
            // Give up on anything it cannot physically reach — a waypoint inside its own turning
            // circle can be circled forever, and one stuck contestant stalls the whole venue.
            if (dist < arrive || _waypointTimer > 3.5f)
            {
                _waypoint++;
                _waypointTimer = 0f;
                if (_waypoint >= _route.Length - 1) { Finish(); return; }
                return;
            }

            // Steer, do not teleport.
            //
            // The machine used to move straight at the next waypoint and set its heading from the
            // direction it happened to be travelling, which meant it could pivot on the spot and
            // trace corners no mower could physically cut. The mown result gave it away: shapes
            // with hard angles in them that no amount of driving could have produced. Now the
            // heading turns at a bounded rate and the machine only ever moves along it, so
            // everything on the lawn is something that could actually have been driven — including
            // the wide arcs at the end of every pass, which is where a mown lawn gets its character.
            float want = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            _heading = Mathf.MoveTowardsAngle(_heading, want, turnRate * dt);

            float rad = _heading * Mathf.Deg2Rad;
            Vector3 forward = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));

            // Slow into a hard turn, exactly as the player's machine does.
            float turnError = Mathf.Abs(Mathf.DeltaAngle(_heading, want));
            float cornerBrake = Mathf.Lerp(1f, 0.40f, Mathf.Clamp01(turnError / 90f));

            // Speed is built, not held.
            //
            // It used to be assigned outright every frame, so a contestant left every turnaround
            // already at cruise. That is not a mower, and more to the point it is not what the
            // player experiences: their machine has to wind back up out of every corner, which is
            // most of where a lap's time actually goes. Ramping it means a route with more turns in
            // it genuinely costs more — which is the whole reason the three strategies should not
            // score alike.
            float targetSpeed = topSpeed * cornerBrake;
            _speed = Mathf.MoveTowards(_speed, targetSpeed,
                                       (targetSpeed > _speed ? acceleration : acceleration * 2.2f) * dt);

            Vector3 step = forward * (_speed * dt);
            _prevPos = _pos;
            _pos += step;

            // Wobble across the line. Imprecise contestants weave; it shows in their outline.
            float wobble = Mathf.Sin(Time.time * (2.1f + flair * 3f) + plotCentre.x) * flair * (1f - precision) * 0.55f;
            Vector3 side = new Vector3(-forward.z, 0f, forward.x);
            _pos += side * wobble * dt * 6f;

            float moved = (_pos - _prevPos).magnitude;
            _driftMetres += moved * flair * 0.25f;
            if (boosting) _boostMetres += moved;

            // Only cut where the plan says the blade is down. Cutting on every leg is what let the
            // travel between two separate spans of a row fill in the hole between them — the eyes
            // of the smiley, the cleft of the heart — and turned every picture into a blob.
            bool blade = _legCuts.Length > _waypoint && _legCuts[_waypoint];
            if (blade)
            {
                _mask.CutSwath(_prevPos, _pos, swathWidth);
                AccumulateCoverage(_pos);
            }
            CheckObstacles();
            PlaceVisual();

            TickEvents(dt);
        }

        /// <summary>Stand every ornament back up. Called on Begin, so retries start from a tidy plot.</summary>
        void ResetObstacles()
        {
            if (obstacles == null || obstacles.Length == 0) return;

            if (_obstacleHome == null || _obstacleHome.Length != obstacles.Length)
            {
                _obstacleHome = new Vector3[obstacles.Length];
                _obstacleHomeRot = new Quaternion[obstacles.Length];
                for (int i = 0; i < obstacles.Length; i++)
                {
                    if (obstacles[i] == null) continue;
                    _obstacleHome[i] = obstacles[i].position;
                    _obstacleHomeRot[i] = obstacles[i].rotation;
                }
            }

            _obstacleDown = new bool[obstacles.Length];
            for (int i = 0; i < obstacles.Length; i++)
                if (obstacles[i] != null)
                    obstacles[i].SetPositionAndRotation(_obstacleHome[i], _obstacleHomeRot[i]);
        }

        /// <summary>
        /// Clatter into anything standing on the line.
        ///
        /// Tested against the machine's position rather than simulated, because a rival has no
        /// collider — but the ornament is still knocked flat and left flat, so the tour camera
        /// arrives to physical evidence of what went wrong on that lawn rather than to a number
        /// on a board claiming it.
        /// </summary>
        void CheckObstacles()
        {
            if (obstacles == null || _obstacleDown == null) return;

            float r2 = obstacleRadius * obstacleRadius;
            for (int i = 0; i < obstacles.Length; i++)
            {
                if (_obstacleDown[i] || obstacles[i] == null) continue;

                Vector3 d = obstacles[i].position - _pos;
                d.y = 0f;
                if (d.sqrMagnitude > r2) continue;

                _obstacleDown[i] = true;
                _bonks++;
                _stallTimer = Mathf.Max(_stallTimer, obstacleStall);

                // Tip it over away from the machine and drop it to the grass.
                Vector3 away = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward;
                Vector3 axis = Vector3.Cross(Vector3.up, away);
                obstacles[i].SetPositionAndRotation(
                    _obstacleHome[i] + away * 0.55f + Vector3.down * 0.16f,
                    Quaternion.AngleAxis(84f, axis) * _obstacleHomeRot[i]);

                OnEvent?.Invoke(new RivalEvent(RivalEventKind.Mistake, this, obstacles[i].position));
            }
        }

        void PlaceVisual()
        {
            if (mowerVisual == null) return;
            mowerVisual.SetPositionAndRotation(new Vector3(_pos.x, mowerVisual.position.y, _pos.z),
                                               Quaternion.Euler(0f, _heading, 0f));
        }

        /// <summary>
        /// A running estimate of coverage for the live standings, sampled rather than summed.
        /// The exact figure is computed once at the end; this only has to be good enough to drive
        /// a crowd reaction.
        /// </summary>
        void AccumulateCoverage(Vector3 world)
        {
            int gx = Mathf.FloorToInt((world.x - plotCentre.x + plotSize * 0.5f) / plotSize * PlotMask.GridRes);
            int gz = Mathf.FloorToInt((world.z - plotCentre.y + plotSize * 0.5f) / plotSize * PlotMask.GridRes);
            if (gx < 0 || gz < 0 || gx >= PlotMask.GridRes || gz >= PlotMask.GridRes) return;
            if (_inside != null && _inside[gz * PlotMask.GridRes + gx]) _cutInsideEstimate++;
        }

        /// <summary>
        /// The things the neighbours hear. Boosts, fumbles and the odd flourish, at a rate that
        /// keeps the venue alive without turning into a fairground.
        /// </summary>
        void TickEvents(float dt)
        {
            _eventTimer -= dt;
            if (_eventTimer > 0f) return;
            _eventTimer = Mathf.Lerp(9f, 4.5f, flair) + (float)_rng.NextDouble() * 6f;

            double roll = _rng.NextDouble();
            // Audible fumbles track line discipline, not general competence: HORACE is a strong
            // contestant who clatters about, and the crowd should hear that.
            if (roll < 0.30 * (1f - precision) + 0.05)
            {
                _bonks++;
                OnEvent?.Invoke(new RivalEvent(RivalEventKind.Mistake, this, _pos));
            }
            else if (roll < 0.62)
            {
                _boostTimer = 1.4f + (float)_rng.NextDouble();
                OnEvent?.Invoke(new RivalEvent(RivalEventKind.Boost, this, _pos));
            }
            else
            {
                OnEvent?.Invoke(new RivalEvent(RivalEventKind.CrowdCheer, this, _pos));
            }
        }

        /// <summary>Stop mowing and settle the artwork. Idempotent.</summary>
        public void Finish()
        {
            if (Finished) return;
            Finished = true;
            _mask?.Flush();
            Measure();
            OnEvent?.Invoke(new RivalEvent(RivalEventKind.Finished, this, _pos));
        }

        /// <summary>
        /// Mark this lawn again, because something changed it after this contestant downed tools.
        ///
        /// A strong contestant completes their route well before the klaxon and is scored on the
        /// frame they do — so anything that damages their picture afterwards is being compared
        /// against a number taken before the damage existed. Without this the tour flies over a
        /// visibly spoiled lawn carrying the marks of an untouched one, and the board quietly
        /// contradicts the evidence the player is looking at.
        ///
        /// A no-op before <see cref="Finish"/>, which is going to do this anyway. Measuring a mask
        /// that is still being mown into would publish a score for a half-finished picture.
        /// </summary>
        public void Rescore()
        {
            if (!Finished) return;
            _mask?.Flush();
            Measure();
        }

        void Measure()
        {
            if (_mask == null || _inside == null) return;
            Score = Scoring.Evaluate(_mask.Cut, _inside, _boundary, _insideCount,
                                     _mask.CellArea, _driftMetres, _boostMetres, _bonks);
            ComputeMarks();
        }

        /// <summary>
        /// This plot's station marks the work. Three judges, the same three biases as the player's
        /// bench, with this station's own temperament folded into severity — so a rival's station
        /// can be tough or soft without ever changing what is being measured.
        /// </summary>
        void ComputeMarks()
        {
            float severity = Mathf.Lerp(1.25f, 0.85f, skill * 0.5f + 0.25f);
            Total = 0f;
            for (int i = 0; i < 3; i++)
            {
                Marks[i] = Scoring.Mark(Score, Scoring.StationBiases[i], severity, 0f, 10f);
                Total += Marks[i];
            }
            Rank = Scoring.Rank(Total);
        }

        public void FlushMask() => _mask?.Flush();

        /// <summary>The lawn belonging to this plot, found by position rather than by wiring.</summary>
        RivalLawn FindLawn()
        {
            foreach (var l in FindObjectsByType<RivalLawn>(FindObjectsSortMode.None))
                if ((l.plotCentre - plotCentre).sqrMagnitude < 1f) return l;
            return null;
        }

        /// <summary>This plot's chalk outline, found the same way and for the same reason.</summary>
        RivalChalk FindChalk()
        {
            foreach (var c in FindObjectsByType<RivalChalk>(FindObjectsSortMode.None))
                if ((c.plotCentre - plotCentre).sqrMagnitude < 1f) return c;
            return null;
        }
    }

    /// <summary>
    /// How a contestant gets the picture wrong once they are working from memory.
    ///
    /// Four named failures rather than a noise amount, because the venue tour asks the player to
    /// look at four lawns and understand what happened on each. "Squashed", "on a tilt", "off
    /// centre" and "one side invented" all read from the air in a second; graded randomness reads
    /// as nothing at all.
    /// </summary>
    /// <summary>
    /// How a contestant plans the job before they start it.
    ///
    /// Varying spacing and overshoot on one shared zig-zag only ever produces "the same approach,
    /// done better or worse", and that is what made three rivals indistinguishable however
    /// differently their numbers were tuned. A real field contains somebody who secures the outline
    /// before anything else, somebody who works methodically in rows and runs out of clock, and
    /// somebody who attacks it in big sweeps from the middle. Those read as different people from
    /// sixty metres up, which is the altitude the tour actually judges them from.
    /// </summary>
    public enum MowStrategy
    {
        /// <summary>Methodical parallel passes. Tidy, and slow to cover ground.</summary>
        Rows,
        /// <summary>Lap the edge first, then fill. Keeps a crisp silhouette even when the fill fails.</summary>
        OutlineFirst,
        /// <summary>Continuous rings working inward. Dense in the middle, ragged everywhere else.</summary>
        Spiral
    }

    public enum MemoryStyle
    {
        /// <summary>Right shape, wrong aspect — remembered too wide and too short.</summary>
        Proportion,
        /// <summary>Remembered on a tilt, as though the picture slid round as it went.</summary>
        Skew,
        /// <summary>Right shape, wrong place. What happens with no registration marks.</summary>
        Drift,
        /// <summary>One half right, the far half invented.</summary>
        Lopsided
    }

    public enum RivalEventKind { Boost, Mistake, CrowdCheer, Finished }

    public readonly struct RivalEvent
    {
        public readonly RivalEventKind kind;
        public readonly RivalContestant who;
        public readonly Vector3 position;

        public RivalEvent(RivalEventKind kind, RivalContestant who, Vector3 position)
        {
            this.kind = kind;
            this.who = who;
            this.position = position;
        }
    }
}
