using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The Bloom Rush ground, as numbers.
    ///
    /// A wheel, not a square. Four competitors paint the floor by driving on it, so the only
    /// interesting question the layout can ask is "where do you spend the next four seconds", and a
    /// square asks it once — everywhere is equally good, everyone farms their own corner, nobody
    /// ever meets. The wheel asks it constantly:
    ///
    /// Four rings of ground, worth more the further in you go, separated by one thin hedge wall:
    ///
    ///   * the LOOP (33 m to 45.5 m) — twelve and a half metres wide, open, fast, and where
    ///     everyone starts. It runs all the way out to the barrier, because there is no sense in a
    ///     strip of visibly good lawn between the last scoring metre and the wall that stops you.
    ///     Safe ground: you can hold full throttle and paint it all day. It is also the furthest
    ///     possible place from anybody else's territory, so a match spent here is a match spent
    ///     taking the cheapest ground on the board.
    ///   * the WALL (30 m to 33 m) — the only unpaintable thing in the arena, and the reason the
    ///     rest of it has shape. Pierced by eight openings and nothing else.
    ///   * the COURT (19 m to 30 m) — the ring of open turf inside the wall. Reachable only through
    ///     an opening, so it is defensible, and big enough to be worth defending.
    ///   * the PLAZA (inside 19 m) — the hub. Whoever holds most of it wears the crown and rolls a
    ///     swath half again as wide, which is the only compounding advantage in the mode. It is
    ///     also the most exposed ground on the map, because all eight routes point at it.
    ///
    /// Those four numbers used to be 28/24/13/46, and the ring they described said the opposite of
    /// what every comment in this file claims. Score is counted per square metre with no weighting
    /// by ring — see <see cref="TurfMask.Share"/> — so the honest measure of "which ground is worth
    /// having" is simply area, and the old loop was 4185 m2 against the court's 1279 and the hub's
    /// 531. SEVENTY PER CENT of everything paintable was the outer ring: the safest, fastest,
    /// widest, least contested ground on the board was also, by a factor of three, the most of it.
    /// A player who never went in was not being timid, they were farming correctly, and the mode
    /// spent seventy-five seconds proving it. Moving the hedge ring out to 30/33 fixed that: the
    /// loop became a LANE rather than a field, and the ground inside the wall went from 30% of the
    /// score to 57%. Going in is where the ground is, which is the only argument a territory mode
    /// can actually make.
    ///
    /// It is a narrower argument today than that paragraph makes it sound, and the honest figures
    /// belong next to it. The rings measure 3083 loop / 1693 court / 933 hub, plus 206 m2 of open
    /// ground inside the eight gateways, against a total of 5915. Two things moved: the core took
    /// eight metres out of the middle of the hub, so the 1134 quoted above is now 933; and the
    /// touchline went out to the barrier at 45.5, which handed the loop another 962 m2. Inside the
    /// wall is 44% of the board rather than 57%. See <see cref="ArenaRadius"/> for why that trade
    /// was made and what to do about it if the mode starts reading as a rim-farm again — the short
    /// version is that the crown, not the acreage, is what has to make the middle worth entering,
    /// and if it cannot then the hedge ring moves outward rather than the touchline moving in.
    ///
    /// So the question the arena asks, continuously, is: how far in dare you go? Every metre inward
    /// is worth more and is harder to hold, and the only ways in are eight places everybody knows
    /// about. Four wide SPOKES on the diagonals are the roads — ten metres, drivable at speed, and
    /// obvious. Four narrow CHOKES on the cardinals are seven, are the shortest way to the hub, and
    /// are where two mowers arriving at once have to settle it.
    ///
    /// The loop is a ring and every opening is radial, so entering one is a ninety degree turn out
    /// of a tangent. That is not an accident to be designed away — it is the arena's one corner,
    /// repeated eight times, and it is why <c>DuckTurfBuilder</c> switches the mower's drift
    /// mini-turbo on for this mode alone. Handbrake off the loop, hold the slide through the gate,
    /// and the turbo fires down the spoke: the manoeuvre that gets you to the middle is also the
    /// fastest way to cross the court once you are through it.
    ///
    /// The hedge is NOT playable, and that is the layout's whole job: subtracting a little ground in
    /// exactly the right ring turns the rest of it into routes, and routes are what a territory mode
    /// is about. Everything here is 4-fold symmetric under a 90 degree rotation, so all four
    /// gardeners are playing the identical map from a different bearing.
    ///
    /// This is the authored layout rather than a description of one — <c>DuckTurfBuilder</c> bakes
    /// BloomRush.unity from these numbers, <see cref="TurfMask"/> scores against the same validity
    /// test, and the ground shader carves the same shapes. Change a number here, run the builder,
    /// look. Same split as <see cref="RallyArena"/> and <see cref="DefenceArena"/>.
    /// </summary>
    public static class TurfArena
    {
        public const int Count = 4;

        // ------------------------------------------------------------------ radii

        /// <summary>
        /// Where the spectator barrier ring sits, and the only thing in the mode that physically
        /// stops a mower. <c>DuckTurfBuilder</c> stands the wall's box colliders at
        /// BarrierRadius + 0.4 with 0.6 m of thickness, so the face a machine actually meets is
        /// 45.6 — a tenth of a metre of overrun past the touchline and then a wall.
        /// </summary>
        public const float BarrierRadius = 45.5f;

        /// <summary>
        /// Outer edge of paintable ground. The barrier, and nothing short of it.
        ///
        /// This was 42 for a long time, on an argument that was right about the wrong number. It
        /// ran: a lap of the old loop was 239 m, which at the mower's 10 m/s top speed is
        /// TWENTY-FOUR SECONDS — a third of a seventy-five second match to go round once, and an
        /// arena whose cheapest ring takes a third of the clock to circle has already decided what
        /// everybody is going to do with the clock. That reasoning is correct and it is still load
        /// bearing. It is simply an argument about LAP TIME, and lap time is set by
        /// <see cref="LoopMid"/> — the line a mower running the rim actually follows — which is
        /// 37.5 and is not changing here.
        ///
        /// The tell is in the arithmetic. A lap at LoopMid is 2*pi*37.5 = 236 m, or 23.6 seconds:
        /// three tenths of a second off the 239 m the paragraph above was complaining about.
        /// Pulling the touchline in from 46 to 42 never shortened the lap at all, because the
        /// touchline is not what anybody drives. What it actually bought was three and a half
        /// metres of ground the player could see, drive on, and not paint.
        ///
        /// And it IS visibly grass. TurfGround.shader:383 says so in as many words — beyond the
        /// touchline the ground is still lawn, only slightly cooler so the painted line itself
        /// reads — and it says so deliberately, because the version that rendered the run-off as
        /// soil put a black moat between the arena and its own barrier which from a chase camera
        /// read as the level running out. So the shipped arrangement was: a strip of obviously good
        /// turf, a machine that drives onto it happily, a wall three and a half metres further out
        /// that is the thing which really stops you, and a scoring rule that refused to count any of
        /// it. Somebody who can see lawn and can drive on it will try to paint it, and being unable
        /// to does not read as a boundary. It reads as a bug, and it was reported as one.
        ///
        /// So the touchline goes where the wall is. Written as <see cref="BarrierRadius"/> rather
        /// than retyping 45.5, because the whole point is that these two cannot be allowed to drift
        /// apart again: the painted ring, the score's outer bound, the shader's cutout and
        /// <c>TurfCompetitor.Recover</c>'s soft edge are now all the same number as the fence, and
        /// the fence is what stops you.
        ///
        /// The price is paid in the denominator, and it is not small. Score is counted per square
        /// metre with no weighting by ring, so widening the loop from nine metres to twelve and a
        /// half adds 962 m2 to a board that was 4953: every share is divided by 19% more ground,
        /// and the loop goes from 43% of the arena to 52%. That is a genuine pull back toward the
        /// outside and it is the one thing here worth watching. If the mode starts reading as a
        /// rim-farm again, the fix is to move <see cref="LoopMid"/> and the hedge ring outward so
        /// the loop is a lane again — not to fence the player off from grass they can see.
        /// </summary>
        public const float ArenaRadius = BarrierRadius;

        /// <summary>The hub: the plaza in the middle, bounded by a stone kerb.</summary>
        public const float PlazaRadius = 19f;

        /// <summary>
        /// The CORE: the fountain island at dead centre, walled off and driveable by nobody.
        ///
        /// The bench lives in here. It used to stand nine metres beyond the barrier, outside the
        /// fence and behind the crowd, which meant the ending had to fly the camera the better part
        /// of sixty metres to reach three judges who were never in a single frame of the match —
        /// and then sixty metres back again for the board. Three shots, two long blends, and a
        /// panel the player has no reason to believe was watching.
        ///
        /// In the middle it is visible from everywhere, every lap is driven around it, and the
        /// closing camera moves are short because the machines can be parked in front of it.
        ///
        /// The price is that the exact centre stops being paintable, and that price is worth
        /// paying twice over. The middle of a territory arena is the most contested ground on the
        /// board, and contested ground wants a thing to fight AROUND — a roundabout, not a
        /// clearing. Eight metres leaves the plaza a fourteen-metre-wide annulus of open turf,
        /// which is four mower widths and still the best ground in the arena.
        /// </summary>
        public const float CoreRadius = 8f;

        /// <summary>Inner face of the hedge ring.</summary>
        public const float HedgeInner = 30f;
        /// <summary>
        /// Outer face of the hedge ring. Three metres thick, which is a WALL and not a band.
        ///
        /// It was thirteen, and thirteen was the single worst decision in the layout. A ring that
        /// deep is not a divider, it is a donut: nineteen hundred square metres — a quarter of the
        /// whole arena — that nobody can drive on, paint, or do anything with, sitting exactly where
        /// the map most needs to be interesting. It also forced the hedge to be built three times
        /// over, as a generated mass with authored sections planted on each face, all occupying the
        /// same place and all fighting each other for the silhouette.
        ///
        /// At three metres it is one row of authored planting, one line of colliders, and the ground
        /// it used to swallow is now the inner court — the ring of open turf between the plaza and
        /// the hedge, which is the second most valuable ground in the mode. Three rather than four
        /// because the authored arc is modelled 2.85 m deep: at four it was stretched 40% across its
        /// own grain, and at three the prop IS the wall at the size somebody drew it.
        /// </summary>
        public const float HedgeOuter = 33f;
        public static float HedgeMid => (HedgeInner + HedgeOuter) * 0.5f;

        /// <summary>
        /// The racing line round the outer loop: where the spawns stand, where <c>TurfNavGraph</c>
        /// lays its outer ring, and the radius every lap-time argument in this file is really about.
        ///
        /// It is no longer the geometric middle of the loop — with the touchline out at the barrier
        /// the band is 33 to 45.5 and its centre is 39.25. Deliberately left at 37.5: this is the
        /// line a driver holds, and holding it a metre and three quarters inboard of centre leaves
        /// the run-off on the outside of the corner where a mower that overcooks one wants it,
        /// rather than putting the fence a metre closer than the driver expects.
        /// </summary>
        public const float LoopMid = 37.5f;

        // ------------------------------------------------------------------ gaps

        /// <summary>
        /// Half-width of a spoke gap, in metres measured at the hedge's mid radius.
        ///
        /// Held in metres rather than degrees because a gap specified as an angle is nearly twice as
        /// wide at its outer mouth as at its inner one, and a route that funnels is a route the
        /// player learns to enter from one end only.
        ///
        /// Ten metres, down from twelve, and the reason is that the wall stopped being a wall. With
        /// the hedge ring moved out to 30 m its circumference is 198 m; twelve metre spokes and
        /// seven metre chokes took 77 of that, and what was left read from above as eight loose
        /// hedges floating in a field rather than as a barrier with gates in it. A wall nobody
        /// perceives as a wall cannot make the ground behind it feel defensible, which is the only
        /// job it has. At ten the doors are still twice the mower's drifting turn circle — nothing
        /// about getting in got harder — and the wall is two thirds solid again.
        /// </summary>
        public const float SpokeHalfWidth = 5f;
        /// <summary>
        /// Half-width of a choke. Two mowers and no room to argue, and that is the point.
        ///
        /// Up from 2.9. The mower's tightest circle at full throttle is v/yaw = 10 / (88 deg/s), or
        /// 6.5 m of radius — so a 5.8 m doorway was narrower than the machine's own turn, which
        /// means it could only ever be entered by somebody already pointed straight down it. That is
        /// a doorway you have to stop for. At 7.2 m a driver who arrives half a metre off line
        /// still gets through, and being off line costs speed rather than the whole attempt.
        /// </summary>
        public const float ChokeHalfWidth = 3.6f;

        /// <summary>
        /// How high the choke climbs over the hedge line. ZERO — the arena is flat, and the climbs
        /// are switched off rather than deleted.
        ///
        /// They were 1.55 m over a 16 m run, which is an 18 degree face, and 18 degrees taken at
        /// 10 m/s is a JUMP. A mower that leaves the ground has 40 deg/s of air yaw against 165 on
        /// the deck and four unloaded springs, so it lands wherever it was pointed a third of a
        /// second ago — inside a seven metre slot with hedge down both sides. The short route was
        /// punishing the commitment it was supposed to be rewarding. Halving it to 0.95 over an
        /// 18 m run fixed the physics and did not fix the real problem, which turned out to be that
        /// NOBODY COULD SEE IT.
        ///
        /// The first person shown the gentler version read it as two rendering faults: the ground
        /// bulging for no reason, and the four choke arches hovering in mid air. Both were the ramp.
        /// The arch stands at the crest and is therefore a metre higher than the turf beside it, and
        /// there is nothing in this arena to make a metre of elevation legible — one flat green
        /// material everywhere, no kerbs, no edging, no cast shadow at that angle, and a slope of
        /// nine degrees that the shading cannot separate from level ground. A feature whose only
        /// perceptible effect is to make correct geometry look broken is worth less than nothing.
        ///
        /// What it was defended for, and where that went instead: costing speed is the choke's
        /// narrowness; the silhouette from across the arena is the arch, which is five metres tall
        /// and stands there whether the ground rises or not; and blocking the sight line at the
        /// crest turned out to be actively harmful in a mode whose reported failure was that nobody
        /// ever went to the middle — a route that conceals its own reward is a route nobody takes.
        ///
        /// The machinery below is intact and correct. Give this a number and the ground mesh, the
        /// collider, the prop seating and the bots' planner all pick it up together. It should not
        /// come back before the ramp has a material, an edge and a shadow of its own.
        /// </summary>
        public const float RampHeight = 0f;

        /// <summary>Bearing of spoke <paramref name="i"/>, degrees. The diagonals: 45, 135, 225, 315.</summary>
        public static float SpokeAngle(int i) => 45f + 90f * i;
        /// <summary>Bearing of choke <paramref name="i"/>, degrees. The cardinals: 0, 90, 180, 270.</summary>
        public static float ChokeAngle(int i) => 90f * i;

        /// <summary>The eight openings through the hedge: 0-3 the wide spokes, 4-7 the climbing chokes.</summary>
        public const int GapCount = 8;
        public static float GapAngle(int g) => g < 4 ? SpokeAngle(g) : ChokeAngle(g - 4);
        public static float GapHalfWidth(int g) => g < 4 ? SpokeHalfWidth : ChokeHalfWidth;

        /// <summary>
        /// The <paramref name="i"/>th opening going round the ring from north, as a gap index.
        ///
        /// Openings are numbered spokes-then-chokes, so gap INDEX order and gap BEARING order are
        /// not the same list: by index the bearings run 45, 135, 225, 315, 0, 90, 180, 270, and
        /// consecutive indices are ninety degrees apart with another opening sitting between them.
        ///
        /// This exists because that cost the mode its entire layout. <c>DuckTurfBuilder</c> lays a
        /// run of hedge "between opening g and opening g+1", which is the right idea and was reading
        /// the wrong list — so every run spanned two doorways instead of one and planted a solid,
        /// collidered hedge section across the opening in the middle. All eight. The wheel had no
        /// doors, in a mode whose whole subject is choosing which door to take: the loop was the
        /// only ground four mowers could physically reach, so four mowers drove the loop for
        /// seventy-five seconds and the hub finished untouched. <see cref="IsPlayable"/> disagreed
        /// the whole time and was right, which is why the score, the shader and the bots' planner
        /// all believed in eight routes nobody could drive through.
        ///
        /// Anything laying geometry BETWEEN openings has to walk them in this order. Anything
        /// placing geometry AT one — pillars, arches, posts — can use the index directly.
        /// </summary>
        public static int GapInBearingOrder(int i)
        {
            i = ((i % GapCount) + GapCount) % GapCount;
            // Cardinals and diagonals alternate: choke, spoke, choke, spoke, ... from north.
            return (i & 1) == 0 ? 4 + i / 2 : i / 2;
        }

        public static Vector3 Bearing(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        // ------------------------------------------------------------------ competitors

        /// <summary>Which contestant starts on which spoke. Indexes <see cref="Venue.Plots"/>.</summary>
        static readonly int[] PlotOf = { 0, 1, 2, 3 };

        public struct Slot
        {
            public int index;
            public string contestant;
            public string species;
            public Color livery;
            public bool isPlayer;
            /// <summary>How well this competitor drives, 0..1. The player's is unused.</summary>
            public float skill;

            /// <summary>Bearing of this competitor's own spoke, degrees.</summary>
            public float angle;
            /// <summary>Unit vector from the middle out along that spoke.</summary>
            public Vector3 outward;
            public Vector3 inward;
            /// <summary>Unit vector across the spoke, right-hand side when facing inward.</summary>
            public Vector3 right;

            /// <summary>Where this competitor's mower starts: out on the loop, on their own spoke.</summary>
            public Vector3 SpawnPosition => outward * LoopMid + Vector3.up * 0.42f;
            /// <summary>Facing along the loop rather than at the hub, so nobody is pointed at the
            /// centre on the horn and the first four seconds are a choice rather than a drag race.</summary>
            public Quaternion SpawnRotation => Quaternion.LookRotation(right, Vector3.up);

            /// <summary>The mouth of this competitor's spoke, out on the loop side of the hedge.</summary>
            public Vector3 SpokeMouth => outward * (HedgeOuter + 2.5f);
        }

        public static Slot Get(int i)
        {
            i = ((i % Count) + Count) % Count;
            float a = SpokeAngle(i);
            Vector3 outward = Bearing(a);
            var plot = Venue.Plots[PlotOf[i]];

            return new Slot
            {
                index = i,
                contestant = plot.contestant,
                species = plot.species,
                livery = plot.livery,
                isPlayer = plot.isPlayer,
                // The mowing rivals' grading is reused rather than re-invented, so a contestant who
                // was hard to beat on the lawn drives hard here too. The player's entry is 0 and
                // nothing reads it.
                skill = plot.isPlayer ? 0f : Mathf.Clamp01(plot.skill * 0.65f + plot.pace * 0.35f),
                angle = a,
                outward = outward,
                inward = -outward,
                right = Vector3.Cross(Vector3.up, -outward).normalized,
            };
        }

        /// <summary>
        /// Livery colours, indexed by slot. The four things every percentage, every strip of ground
        /// and every HUD bar is coloured by, resolved once so nothing can disagree.
        /// </summary>
        public static Color Livery(int slot) => Get(slot).livery;

        // ------------------------------------------------------------------ validity

        /// <summary>
        /// Is this point paintable ground?
        ///
        /// The single definition of "valid playable ground", and everything that could ever
        /// disagree about it asks this instead: the CPU scoring grid, the ground shader's cutout,
        /// the bots' route planner and the builder that lays the hedges down. A territory mode whose
        /// score counts ground the shader is drawing a hedge over is a mode nobody can trust.
        /// </summary>
        public static bool IsPlayable(float x, float z)
        {
            float rSq = x * x + z * z;
            if (rSq > ArenaRadius * ArenaRadius) return false;
            // The core is walled and nobody drives in it, so nobody paints in it either. Tested
            // here rather than at the paint sites so the shader, the CPU grid, the bot navigation
            // and the recovery push all agree about it from one definition — the last time this
            // stage had two answers to "can you be here", the mask happily painted ground the
            // player could not reach.
            if (rSq < CoreRadius * CoreRadius) return false;
            float r = Mathf.Sqrt(rSq);
            if (r < HedgeInner || r > HedgeOuter) return true;      // hub, or the outer loop
            return GapOpening(x, z, r) > 0f;
        }

        public static bool IsPlayable(Vector3 world) => IsPlayable(world.x, world.z);

        /// <summary>
        /// How far inside a gap this point is, in metres, at hedge radius <paramref name="r"/>.
        /// Zero or less means solid hedge. Positive means open, and the value is the clearance to
        /// the nearest hedge face — which is what the bots steer by when threading a choke.
        /// </summary>
        public static float GapOpening(float x, float z, float r)
        {
            if (r < 1e-3f) return SpokeHalfWidth;

            // Arc distance from this point to each gap's centre line, measured along the circle at
            // this radius. Metres, so a gap is the same width at both mouths.
            float ang = Mathf.Atan2(x, z) * Mathf.Rad2Deg;

            float best = float.NegativeInfinity;
            for (int g = 0; g < GapCount; g++)
                best = Mathf.Max(best, GapHalfWidth(g) - ArcDistance(ang, GapAngle(g), r));
            return best;
        }

        /// <summary>Metres along a circle of radius <paramref name="r"/> between two bearings.</summary>
        static float ArcDistance(float angA, float angB, float r)
        {
            float d = Mathf.Abs(Mathf.DeltaAngle(angA, angB));
            return d * Mathf.Deg2Rad * r;
        }

        /// <summary>Metres of run-up and run-off either side of the hedge band that a choke climbs over.</summary>
        public const float RampRun = 7.5f;

        /// <summary>
        /// Ground height at a point. Flat everywhere except across the four chokes, which climb over
        /// the hedge line and back down again.
        ///
        /// Analytic rather than a collider query, so the bots plan against the same surface the
        /// physics drives on without having to raycast a route, and so the ground mesh, the collider
        /// and the route planner are all reading one function.
        /// </summary>
        public static float GroundHeight(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            float from = HedgeInner - RampRun, to = HedgeOuter + RampRun;
            if (r < from || r > to) return 0f;

            float ang = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
            float best = 0f;
            for (int i = 0; i < 4; i++)
            {
                float across = ArcDistance(ang, ChokeAngle(i), r) / ChokeHalfWidth;
                if (across >= 1f) continue;
                // A full cosine hump along the run, so both approaches are a rise rather than a
                // step, and a cosine across it so the flanks fall away into the hedge instead of
                // ending in a kerb a wheel can catch on.
                float along = Mathf.InverseLerp(from, to, r);
                float lift = Mathf.Sin(along * Mathf.PI);
                float shape = Mathf.Cos(across * Mathf.PI * 0.5f);
                best = Mathf.Max(best, RampHeight * lift * shape);
            }
            return best;
        }

        public static float GroundHeight(Vector3 world) => GroundHeight(world.x, world.z);

        /// <summary>Inside the hub, the ground worth fighting over.</summary>
        public static bool InsidePlaza(Vector3 world)
            => world.x * world.x + world.z * world.z <= PlazaRadius * PlazaRadius;

        /// <summary>
        /// Push a point back onto playable ground.
        ///
        /// Used to place things the builder scatters and to un-wedge a mower that has ended up
        /// inside a hedge — never to steer one, which would be the bots being helped.
        /// </summary>
        public static Vector3 NearestPlayable(Vector3 world, float step = 1.0f)
        {
            if (IsPlayable(world)) return world;

            Vector3 best = world;
            float bestDist = float.PositiveInfinity;
            for (int a = 0; a < 16; a++)
            {
                Vector3 dir = Bearing(a * 22.5f);
                for (float d = step; d <= 14f; d += step)
                {
                    Vector3 p = world + dir * d;
                    if (!IsPlayable(p)) continue;
                    if (d >= bestDist) break;
                    bestDist = d; best = p;
                    break;
                }
            }
            return best;
        }
    }
}
