using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The geometry of the goose rally ground, as numbers.
    ///
    /// Four competitors on a 2x2 grid. Each owns one quadrant; their garden sits on the outer
    /// corner of it, facing diagonally in at the middle of the arena, with a low fence in front and
    /// a strip of bare dirt between the two where they drive.
    ///
    /// Corners rather than sides, and that is the whole reason the aiming reads. Put the gardens on
    /// the four SIDES of a square and the three opponents sit at 90, 90 and 180 degrees from your
    /// facing — which is the same set of angles — but your own garden is then behind a flat wall you
    /// are driving parallel to, and "across" and "left" are only distinguishable by which way the
    /// grass is mown. On the corners, every competitor's inward axis points at the middle, the three
    /// opponents fall exactly at hard left, hard right and dead ahead, and the shot the player is
    /// looking down IS the shot they are aiming. One layout decision does the work an aiming UI
    /// would otherwise have to.
    ///
    /// This is the authored layout, not a description of one: <c>DuckRallyBuilder</c> bakes
    /// GooseRally.unity from these numbers and the runtime components bind to what it saved. Same
    /// split as <see cref="DefenceArena"/>, for the same reason — geometry nobody can select in the
    /// editor cannot be art-directed.
    /// </summary>
    public static class RallyArena
    {
        public const int Count = 4;

        /// <summary>Metres from the middle of the arena to the middle of a garden.</summary>
        public const float Reach = 30f;

        /// <summary>Half the frontage of a garden, across the inward axis.</summary>
        public const float GardenHalfWidth = 8.5f;
        /// <summary>Half the depth of a garden, along the inward axis.</summary>
        public const float GardenHalfDepth = 5f;

        /// <summary>Metres in front of the garden centre the fence line stands.</summary>
        public const float FenceStandoff = 6.2f;
        /// <summary>How wide the fence runs. A little past the beds, so nothing can be walked around.</summary>
        public const float FenceHalfWidth = 9.4f;
        public const int FenceSections = 9;

        /// <summary>
        /// The defending box: where it sits, and how big it is.
        ///
        /// Reshaped rather than resized. It was 25 m across and 9.2 m deep — an aspect ratio of
        /// nearly three to one, which is a CORRIDOR, and a corridor is a thing you shuttle along
        /// rather than drive around. There was no room to commit to an arc, so the only manoeuvre
        /// the box afforded was reversing, and a mode whose whole verb is "get the machine there,
        /// pointed the right way" cannot afford to make pointing the hard part.
        ///
        /// Now 19 m by 12 m: slightly more ground, in a shape a mower can turn a full circle in.
        /// The width it gives up is width it never needed — the fence it has to cover is 18.8 m —
        /// and the depth it gains is the depth a curve costs.
        ///
        /// Three constraints hold this in place, and all three are tight, so nothing here is a free
        /// parameter:
        ///   • outer edge inside the fence line:  Reach - FenceStandoff - (depth + slack) > 0
        ///   • boxes stay in their own quadrant:  reach >= width + depth   (they meet at the corner)
        ///   • inner edge clear of the touchdown, or geese land inside somebody's box.
        /// </summary>
        public const float BandCentreReach = 16.6f;
        public const float BandHalfWidth = 9.5f;
        public const float BandHalfDepth = 6f;

        /// <summary>
        /// Metres past the marked line a machine may lean before it is stopped dead.
        ///
        /// One number, read by BOTH the confinement and the thing that draws the boundary, and that
        /// is the entire point of it living here. It used to be a tuning field on the competitor
        /// while the marking was built from <see cref="BandHalfWidth"/> — so the line the player was
        /// shown and the line the game enforced were two different lines, and driving past the
        /// marking was not a bug in the physics, it was the marking being drawn in the wrong place.
        /// A boundary that can be crossed is not a boundary; it is decoration.
        /// </summary>
        public const float BandSlack = 0.6f;

        /// <summary>Half the box a machine can actually reach. What the boundary is drawn on.</summary>
        public const float DriveHalfWidth = BandHalfWidth + BandSlack;
        public const float DriveHalfDepth = BandHalfDepth + BandSlack;

        /// <summary>Radius of the grass the arena is played on. Everything above lives inside it.</summary>
        public const float ArenaRadius = 46f;

        /// <summary>Where a served goose comes down: open ground, short of anybody's box.</summary>
        public const float TouchdownReach = 5.5f;

        /// <summary>Beds per garden, laid out across the frontage.</summary>
        public const int BedColumns = 5;
        public const int BedRows = 3;
        public static int BedsPerGarden => BedColumns * BedRows;

        /// <summary>
        /// The four corner directions, counter-clockwise from the south-west.
        ///
        /// The player takes index 0 and the south-west corner, so the default camera looks
        /// north-east across the arena with the other three laid out left, ahead and right — the
        /// reading order the HUD and the off-screen indicators both assume.
        /// </summary>
        static readonly Vector2[] Corners =
        {
            new Vector2(-1f, -1f),   // 0  player
            new Vector2( 1f, -1f),   // 1  right-hand neighbour
            new Vector2( 1f,  1f),   // 2  across the arena
            new Vector2(-1f,  1f),   // 3  left-hand neighbour
        };

        /// <summary>Which contestant defends which corner. Indexes <see cref="Venue.Plots"/>.</summary>
        static readonly int[] PlotOf = { 0, 1, 2, 3 };

        public struct Slot
        {
            public int index;
            public string contestant;
            public string species;
            public Color livery;
            public bool isPlayer;
            /// <summary>How well this competitor defends, 0..1. Player's is unused.</summary>
            public float skill;
            /// <summary>Unit vector from the arena centre out to this garden.</summary>
            public Vector3 outward;
            /// <summary>Unit vector from this garden in toward the arena centre.</summary>
            public Vector3 inward;
            /// <summary>Unit vector along the garden frontage, right-hand side when facing inward.</summary>
            public Vector3 right;
            public Vector3 gardenCentre;
            public Vector3 fenceCentre;
            public Vector3 bandCentre;

            /// <summary>Where this competitor's mower starts: middle of their own dirt, facing in.</summary>
            public Vector3 SpawnPosition => bandCentre + Vector3.up * 0.42f;
            public Quaternion SpawnRotation => Quaternion.LookRotation(inward, Vector3.up);
        }

        public static Slot Get(int i)
        {
            i = ((i % Count) + Count) % Count;
            Vector2 c = Corners[i].normalized;
            Vector3 outward = new Vector3(c.x, 0f, c.y);
            Vector3 inward = -outward;
            Vector3 right = Vector3.Cross(Vector3.up, inward).normalized;

            var plot = Venue.Plots[PlotOf[i]];

            return new Slot
            {
                index = i,
                contestant = plot.contestant,
                species = plot.species,
                livery = plot.livery,
                isPlayer = plot.isPlayer,
                // The mowing rivals' skill grading is reused rather than re-invented, so a
                // contestant who was hard to beat on the lawn is hard to beat here too. The player's
                // entry is 0 and nothing reads it.
                skill = plot.isPlayer ? 0f : Mathf.Clamp01(plot.skill * 0.7f + plot.precision * 0.3f),
                outward = outward,
                inward = inward,
                right = right,
                gardenCentre = outward * Reach,
                fenceCentre = outward * (Reach - FenceStandoff),
                bandCentre = outward * BandCentreReach,
            };
        }

        /// <summary>Where a bed sits inside a garden, in world space. Column-major, row 0 nearest the fence.</summary>
        public static Vector3 BedPosition(in Slot s, int column, int row)
        {
            float u = BedColumns > 1 ? (column / (float)(BedColumns - 1)) * 2f - 1f : 0f;
            float v = BedRows > 1 ? (row / (float)(BedRows - 1)) * 2f - 1f : 0f;
            return s.gardenCentre
                 + s.right * (u * (GardenHalfWidth - 1.4f))
                 + s.outward * (v * (GardenHalfDepth - 1.3f));
        }

        /// <summary>Is a point standing inside a competitor's garden?</summary>
        public static bool InsideGarden(in Slot s, Vector3 world)
        {
            Vector3 d = world - s.gardenCentre;
            return Mathf.Abs(Vector3.Dot(d, s.right)) <= GardenHalfWidth &&
                   Mathf.Abs(Vector3.Dot(d, s.outward)) <= GardenHalfDepth;
        }

        /// <summary>
        /// Has a point crossed a competitor's fence line — that is, got past them?
        ///
        /// Measured against the line rather than against the garden box, because a goose arriving on
        /// a shallow diagonal clears the fence at the edge and only then curves into the beds. Asking
        /// "is it in the box" would let it through the barrier and only notice a metre later, which
        /// reads as the fence being scenery.
        /// </summary>
        public static bool PastFence(in Slot s, Vector3 world)
        {
            Vector3 d = world - s.fenceCentre;
            return Vector3.Dot(d, s.outward) > 0f &&
                   Mathf.Abs(Vector3.Dot(d, s.right)) <= FenceHalfWidth;
        }

        /// <summary>
        /// Did something CROSS the fence line outward between two points?
        ///
        /// The version that matters, and the reason the plain test above is not enough on its own: a
        /// goose that finds itself behind a garden — served long, bounced over the back, spawned
        /// badly — is standing past the fence without ever having gone through it, and treating that
        /// as a break-in scores a competitor who was never given anything to defend. A breach is an
        /// event, not a position, so it is measured as one.
        /// </summary>
        public static bool CrossedFence(in Slot s, Vector3 from, Vector3 to)
        {
            float before = Vector3.Dot(from - s.fenceCentre, s.outward);
            float after = Vector3.Dot(to - s.fenceCentre, s.outward);
            if (before > 0f || after <= 0f) return false;      // already behind it, or still short
            float lateral = Mathf.Abs(Vector3.Dot(to - s.fenceCentre, s.right));
            return lateral <= FenceHalfWidth;
        }

        /// <summary>Clamp a point into a competitor's dirt strip. What keeps a mower on its own ground.</summary>
        public static Vector3 ClampToBand(in Slot s, Vector3 world, float margin = 0f)
        {
            Vector3 d = world - s.bandCentre;
            float along = Mathf.Clamp(Vector3.Dot(d, s.outward), -(BandHalfDepth - margin), BandHalfDepth - margin);
            float across = Mathf.Clamp(Vector3.Dot(d, s.right), -(BandHalfWidth - margin), BandHalfWidth - margin);
            Vector3 p = s.bandCentre + s.outward * along + s.right * across;
            p.y = world.y;
            return p;
        }

        /// <summary>
        /// Did something cross the fence LINE but outside the fence itself — through the gap?
        ///
        /// The other half of <see cref="CrossedFence"/>. A goose sent past a garden but wide of its
        /// pickets has not broken in and never will: it is behind the defence with nothing in front
        /// of it, and turning round to walk back in through the front is not a thing an animal does.
        /// Reported so it can simply leave.
        /// </summary>
        public static bool MissedWide(in Slot s, Vector3 from, Vector3 to)
        {
            float before = Vector3.Dot(from - s.fenceCentre, s.outward);
            float after = Vector3.Dot(to - s.fenceCentre, s.outward);
            if (before > 0f || after <= 0f) return false;
            return Mathf.Abs(Vector3.Dot(to - s.fenceCentre, s.right)) > FenceHalfWidth;
        }

        /// <summary>
        /// Clamp a point to the line a machine genuinely cannot pass — the one the boundary is
        /// drawn on. Everything that enforces or draws the edge goes through here.
        /// </summary>
        public static Vector3 ClampToDrive(in Slot s, Vector3 world)
            => ClampToBand(s, world, -BandSlack);

        public static bool InsideBand(in Slot s, Vector3 world, float margin = 0f)
        {
            Vector3 d = world - s.bandCentre;
            return Mathf.Abs(Vector3.Dot(d, s.outward)) <= BandHalfDepth + margin &&
                   Mathf.Abs(Vector3.Dot(d, s.right)) <= BandHalfWidth + margin;
        }

        /// <summary>Where a served goose touches down on its way to <paramref name="target"/>.</summary>
        public static Vector3 Touchdown(in Slot target)
            => target.outward * TouchdownReach;

        /// <summary>
        /// Which opponent lies in the direction <paramref name="dir"/>, from <paramref name="from"/>.
        ///
        /// Broad sectors on purpose. The three opponents sit 90 degrees apart, so a 60 degree
        /// half-width means every heading in the open arena resolves to somebody and the player aims
        /// by pointing roughly rather than by threading a needle. Returns -1 only when the heading is
        /// genuinely behind the striker, into their own garden — which the caller treats as a fault
        /// rather than quietly redirecting, because silently choosing FOR the player is the thing
        /// that makes a redirect feel random.
        /// </summary>
        public static int SectorTarget(int from, Vector3 dir, float halfAngleDeg = 60f)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return -1;
            dir.Normalize();

            int best = -1;
            float bestDot = Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad);
            Vector3 origin = Get(from).bandCentre;

            for (int i = 0; i < Count; i++)
            {
                if (i == from) continue;
                Vector3 to = Get(i).gardenCentre - origin;
                to.y = 0f;
                if (to.sqrMagnitude < 1e-4f) continue;
                float dot = Vector3.Dot(dir, to.normalized);
                if (dot <= bestDot) continue;
                bestDot = dot;
                best = i;
            }
            return best;
        }

        /// <summary>
        /// The opponent nearest the heading, whatever the heading is. The fallback when the sector
        /// test finds nothing, so a struck goose always leaves with somebody's name on it.
        /// </summary>
        public static int NearestOpponent(int from, Vector3 dir)
        {
            int t = SectorTarget(from, dir, 180f);
            if (t >= 0) return t;
            return (from + 2) % Count;   // straight across, the only answer that is never "yourself"
        }
    }
}
