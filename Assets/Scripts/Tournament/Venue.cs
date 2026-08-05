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
            new PlotSpec
            {
                contestant = "HORACE", species = "hare",
                centre = new Vector2(Spacing, 0f), size = RivalPlotSize,
                livery = new Color(0.30f, 0.52f, 0.82f), skill = 0.78f, flair = 0.25f
            },
            new PlotSpec
            {
                contestant = "MARGOT", species = "badger",
                centre = new Vector2(0f, Spacing), size = RivalPlotSize,
                livery = new Color(0.94f, 0.72f, 0.24f), skill = 0.52f, flair = 0.72f
            },
            new PlotSpec
            {
                // Purple rather than green: a green machine on a green lawn was invisible from
                // the tour camera, which is the one shot that has to tell four contestants apart.
                contestant = "BRAMBLE", species = "sheep",
                centre = new Vector2(Spacing, Spacing), size = RivalPlotSize,
                livery = new Color(0.62f, 0.42f, 0.78f), skill = 0.64f, flair = 0.48f
            },
        };

        public static PlotSpec Player => Plots[0];

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
