using UnityEngine;

namespace DuckMow
{
    /// <summary>One contestant's ground: lawn, apron, station and spectator bank.</summary>
    public struct PlotSpec
    {
        public string contestant;
        public string species;
        public Vector2 centre;
        public float size;
        public Color livery;
        public bool isPlayer;
        public float skill;
        public float flair;
        /// <summary>How much of the picture this contestant gets finished before the klaxon.</summary>
        public float pace;
        /// <summary>Line discipline: spacing, overshoot and weave. Decides edge quality and spill.</summary>
        public float precision;
        /// <summary>How this contestant plans the job. The clearest difference between them.</summary>
        public MowStrategy strategy;
        /// <summary>How this contestant misremembers the picture once the chalk has gone.</summary>
        public MemoryStyle memoryStyle;
        /// <summary>How badly. Kept well clear of 1 — an unrecognisable lawn is not a joke, it is a bug.</summary>
        public float memoryError;

        public float Half => size * 0.5f;
        /// <summary>Where this plot's judging station sits — always on the plot's south edge.</summary>
        public Vector3 StationPosition => new Vector3(centre.x, 0f, centre.y - Half - 7.5f);
        /// <summary>Where this plot's spectators stand — always on the plot's west edge.</summary>
        public Vector3 StandPosition => new Vector3(centre.x - Half - 6.5f, 0f, centre.y);
    }

    /// <summary>
    /// The championship ground.
    ///
    /// Four plots in a quad with the scoreboard on their shared corner, so every contestant is a
    /// neighbour of every other and the whole thing reads as one venue from the air rather than as
    /// four levels that happen to be adjacent. The player keeps the origin: the cut mask, the
    /// chalk guide, the scoring grid and every shape in <see cref="TargetShapes"/> are authored
    /// around a lawn centred on (0,0), and moving the player would have meant rewriting all of it
    /// for no gain the camera could see.
    ///
    /// Every plot is laid out identically — station due south, spectators due west — because a
    /// real competition ground is standardised. The variety comes from the contestants and their
    /// pictures, not from the furniture being at different angles.
    /// </summary>
    public static class Venue
    {
        public const float RivalPlotSize = 48f;
        /// <summary>Centre-to-centre spacing of the quad. Leaves ~36 m of apron, hedge and path between lawns.</summary>
        public const float Spacing = 96f;

        /// <summary>The scoreboard plaza, on the corner all four plots share.</summary>
        public static readonly Vector3 PlazaCentre = new Vector3(Spacing * 0.5f, 0f, Spacing * 0.5f);
        public const float PlazaRadius = 19f;

        public static readonly PlotSpec[] Plots =
        {
            new PlotSpec
            {
                contestant = "DUCK", species = "duck",
                centre = new Vector2(0f, 0f), size = Field.Size,
                livery = new Color(0.78f, 0.24f, 0.20f), isPlayer = true
            },
            // The three rivals differ on two axes at once: HOW THEY MOW and HOW THEY FORGET.
            //
            // Neither alone was enough. Grading one "skill" number gave three versions of the same
            // contestant and a leaderboard that never changed order. Varying only the memory error
            // gave three identical mowers making different mistakes, which reads as randomness.
            // Together they produce three contestants who are each genuinely better than the player
            // at something and worse at something else — so beating them is a matter of which
            // judge you play to, not of out-mowing a difficulty slider.
            //
            // Each is built to WIN A DIFFERENT CHAIR on the panel (see Scoring.Mark):
            //
            //   HORACE   fast, sloppy      → takes the Coverage chair, last on Craft
            //   BRAMBLE  slow, immaculate  → takes the Craft chair, last on Coverage
            //   MARGOT   bold, wild        → takes the Style share, mid on everything else
            //
            // Memory errors are kept moderate on purpose. A lawn that no longer resembles the
            // subject reads as a broken game rather than as a rival having a bad day, and it robs
            // the player of the comparison the tour exists to offer.
            new PlotSpec
            {
                // The hare. Finishes the whole picture and finishes it badly: widest pass spacing,
                // biggest overshoot, so the outline is ragged and the spill is heavy. Losing the
                // Coverage chair to HORACE should be a decision the player made, not a failure.
                contestant = "HORACE", species = "hare",
                centre = new Vector2(Spacing, 0f), size = RivalPlotSize,
                livery = new Color(0.30f, 0.52f, 0.82f), skill = 0.62f, flair = 0.30f,
                pace = 1.00f, precision = 0.34f,
                // Gets the edge down before the chalk goes, then fills at speed. Produces the one
                // lawn in the venue with a crisp silhouette and a scruffy middle.
                strategy = MowStrategy.OutlineFirst,
                memoryStyle = MemoryStyle.Proportion, memoryError = 0.30f
            },
            new PlotSpec
            {
                // The badger. All flourish and no recall — wild lines, heavy style, and one side
                // of the picture confidently invented. The funniest of the four to fly over, and
                // the one whose lawn most clearly says "they could not see it either".
                contestant = "MARGOT", species = "badger",
                centre = new Vector2(0f, Spacing), size = RivalPlotSize,
                livery = new Color(0.94f, 0.72f, 0.24f), skill = 0.48f, flair = 0.85f,
                pace = 0.86f, precision = 0.22f,
                // No rows at all: big continuous sweeps in from the edge. Dense in the middle,
                // ragged everywhere it matters.
                strategy = MowStrategy.Spiral,
                memoryStyle = MemoryStyle.Lopsided, memoryError = 0.60f
            },
            new PlotSpec
            {
                // Purple rather than green: a green machine on a green lawn was invisible from
                // the tour camera, which is the one shot that has to tell four contestants apart.
                //
                // The tortoise of the field. Runs out of clock with a third of the picture still
                // standing, but what is there is immaculate — the living argument for the new
                // scoring, since BRAMBLE beats HORACE on two chairs out of three while mowing far
                // less lawn. Also draws it in slightly the wrong place, which is the exact failure
                // the player's corner anchors prevent: BRAMBLE's lawn quietly explains what those
                // marks are for without a tutorial ever mentioning them.
                contestant = "BRAMBLE", species = "sheep",
                centre = new Vector2(Spacing, Spacing), size = RivalPlotSize,
                livery = new Color(0.62f, 0.42f, 0.78f), skill = 0.70f, flair = 0.20f,
                pace = 0.60f, precision = 0.93f,
                // Straight, tight, patient rows — and the klaxon arrives with a third of the
                // picture still standing. The lawn that proves neatness can beat coverage.
                strategy = MowStrategy.Rows,
                memoryStyle = MemoryStyle.Drift, memoryError = 0.42f
            },
        };

        public static PlotSpec Player => Plots[0];

        /// <summary>
        /// A contestant's ground, by name. The closing ceremony has to fly over whoever won the
        /// championship, and the only thing it holds about them is what the points table calls them.
        /// Falls back to the player's plot so a name that has drifted out of sync frames a lawn
        /// rather than the middle of a hedge.
        /// </summary>
        public static PlotSpec PlotOf(string contestant)
        {
            foreach (var p in Plots)
                if (string.Equals(p.contestant, contestant)) return p;
            return Player;
        }

        /// <summary>
        /// The order the reveal tour visits the plots: the player first, then round the quad the
        /// short way, ending nearest the plaza so the last move onto the scoreboard is small.
        /// </summary>
        public static readonly int[] TourOrder = { 0, 1, 3, 2 };

        /// <summary>True if a world position is inside somebody else's lawn. Nothing may cross this.</summary>
        public static bool InsideRivalPlot(Vector3 world)
        {
            for (int i = 1; i < Plots.Length; i++)
            {
                var p = Plots[i];
                if (Mathf.Abs(world.x - p.centre.x) <= p.Half && Mathf.Abs(world.z - p.centre.y) <= p.Half)
                    return true;
            }
            return false;
        }
    }
}
