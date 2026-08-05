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
        [Tooltip("0 is a liability, 1 is a machine. Drives speed, line accuracy and how much of the picture gets finished.")]
        public float skill = 0.6f;
        [Tooltip("How wildly this one wanders off the line. Character, not difficulty.")]
        public float flair = 0.4f;

        [Header("Mowing")]
        public float swathWidth = 1.7f;
        public float baseSpeed = 8.5f;
        public float lineSpacing = 1.45f;

        [Header("Presentation")]
        public Transform mowerVisual;
        public Transform bladeSpinner;
        [Tooltip("This plot's grass. Bound to the plot's own mask when the round starts.")]
        public RivalLawn lawn;

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
        int _waypoint;
        float _eventTimer;
        float _boostTimer;
        System.Random _rng;

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

            _mask ??= new PlotMask(plotCentre, plotSize, stampShader);
            _mask.Clear();
            if (lawn == null) lawn = FindLawn();
            lawn?.Bind(_mask.Texture);

            Scoring.Rasterize(shape, PlotMask.GridRes, plotSize, shapeRadius, swathWidth * 0.5f,
                              out _inside, out _boundary, out _insideCount, out _boundaryCount);

            BuildRoute();

            _pos = _route.Length > 0 ? _route[0] : new Vector3(plotCentre.x, 0f, plotCentre.y);
            _prevPos = _pos;
            _heading = 0f;
            PlaceVisual();
        }

        /// <summary>
        /// The route: horizontal passes through the picture, spaced a little wider than the swath
        /// so a careless contestant leaves stripes of uncut grass inside their own outline.
        ///
        /// Skill decides the spacing and how far past the outline each pass runs. A poor contestant
        /// overshoots into clean lawn on every pass, which is exactly the mistake the player is
        /// trying not to make — so watching a rival is a lesson as well as decoration.
        /// </summary>
        void BuildRoute()
        {
            var pts = new System.Collections.Generic.List<Vector3>(128);

            float spacing = Mathf.Lerp(lineSpacing * 1.5f, lineSpacing * 0.82f, skill);
            float overshoot = Mathf.Lerp(2.6f, -0.15f, skill);
            float half = plotSize * 0.5f;

            bool leftToRight = true;
            for (float z = -half + spacing; z <= half - spacing; z += spacing)
            {
                // Find where the picture starts and ends on this line, by walking the SDF.
                float x0 = float.NaN, x1 = float.NaN;
                const int samples = 96;
                for (int i = 0; i <= samples; i++)
                {
                    float x = Mathf.Lerp(-half, half, i / (float)samples);
                    float d = TargetShapes.Sdf(Shape, new Vector2(x / shapeRadius, z / shapeRadius));
                    if (d < 0f)
                    {
                        if (float.IsNaN(x0)) x0 = x;
                        x1 = x;
                    }
                }
                if (float.IsNaN(x0) || x1 - x0 < swathWidth) continue;

                x0 -= overshoot;
                x1 += overshoot;
                if (x1 <= x0) continue;

                // A wobble on the entry and exit, so no two passes line up perfectly.
                float j0 = ((float)_rng.NextDouble() - 0.5f) * flair * 1.8f;
                float j1 = ((float)_rng.NextDouble() - 0.5f) * flair * 1.8f;

                Vector3 a = new Vector3(plotCentre.x + x0 + j0, 0f, plotCentre.y + z);
                Vector3 b = new Vector3(plotCentre.x + x1 + j1, 0f, plotCentre.y + z);
                if (leftToRight) { pts.Add(a); pts.Add(b); } else { pts.Add(b); pts.Add(a); }
                leftToRight = !leftToRight;
            }

            _route = pts.ToArray();
        }

        /// <summary>
        /// Advance the contestant. <paramref name="progress01"/> is how far through the round the
        /// venue is, and it is what stops a rival finishing in twenty seconds and then sitting
        /// still for a minute: the route is paced to run out roughly when the klaxon goes, with
        /// skill deciding how much of it actually gets done.
        /// </summary>
        public void Tick(float dt, float progress01)
        {
            if (Finished || _route.Length < 2) return;

            _spin += dt * 18f;
            if (bladeSpinner != null) bladeSpinner.localRotation = Quaternion.Euler(0f, _spin * 57.3f, 0f);

            // Pace: a strong contestant covers the whole route in the time available, a weak one
            // gets three quarters of the way and runs out of clock.
            float wanted = Mathf.Lerp(0.62f, 1.0f, skill) * progress01 * (_route.Length - 1);
            if (_waypoint >= _route.Length - 1) { Finish(); return; }

            _boostTimer -= dt;
            bool boosting = _boostTimer > 0f;
            float speed = baseSpeed * (boosting ? 1.55f : 1f);

            // Chase the paced target rather than free-running, so everyone crosses the line together.
            float lead = wanted - _waypoint;
            if (lead < -0.35f) speed *= 0.35f;
            else if (lead > 1.2f) speed *= 1.5f;

            Vector3 target = _route[_waypoint + 1];
            Vector3 flat = target - _pos; flat.y = 0f;
            float dist = flat.magnitude;

            if (dist < 0.35f)
            {
                _waypoint++;
                if (_waypoint >= _route.Length - 1) { Finish(); return; }
                return;
            }

            Vector3 step = flat / Mathf.Max(dist, 1e-4f) * Mathf.Min(speed * dt, dist);
            _prevPos = _pos;
            _pos += step;

            // Wobble across the line. Low-skill contestants weave; it shows in their outline.
            float wobble = Mathf.Sin(Time.time * (2.1f + flair * 3f) + plotCentre.x) * flair * (1f - skill) * 0.55f;
            Vector3 side = new Vector3(-step.z, 0f, step.x).normalized;
            _pos += side * wobble * dt * 6f;

            _heading = Mathf.Atan2(step.x, step.z) * Mathf.Rad2Deg;

            float moved = (_pos - _prevPos).magnitude;
            _driftMetres += moved * flair * 0.25f;
            if (boosting) _boostMetres += moved;

            _mask.CutSwath(_prevPos, _pos, swathWidth);
            AccumulateCoverage(_pos);
            PlaceVisual();

            TickEvents(dt);
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
            if (roll < 0.30 * (1f - skill) + 0.05)
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

            Score = Scoring.Evaluate(_mask.Cut, _inside, _boundary, _insideCount,
                                     _mask.CellArea, _driftMetres, _boostMetres, _bonks);
            ComputeMarks();
            OnEvent?.Invoke(new RivalEvent(RivalEventKind.Finished, this, _pos));
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
