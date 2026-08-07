using System.Collections.Generic;
using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// How to get from anywhere in the arena to anywhere else, as a graph and an A* over it.
    ///
    /// The routing this replaces was one greedy step: pick the opening whose bearing is closest to
    /// the objective, drive at its mouth, drive through. That is enough to cross a wall ONCE, and it
    /// is wrong about almost everything else. It cannot tell that the near gate opens onto the wrong
    /// side of the hub. It cannot go out through one gate and in through another. It has no idea
    /// that a route exists at all — it only ever knows the direction it wishes it could go, which is
    /// why a gardener that failed to line up simply drove into the hedge again, and again.
    ///
    /// The arena is a wheel, so the graph is a wheel: a ring of nodes out on the loop, a ring inside
    /// the wall in the court, a pair of nodes at each of the eight gateways, and the plaza in the
    /// middle. Fifty-odd nodes, about a hundred and fifty edges. A* over that costs a few
    /// microseconds and returns a real multi-leg route — round the loop, through THAT gate, across
    /// the court, into the middle — which is the difference between an opponent that drives and one
    /// that heads in your general direction.
    ///
    /// EVERY EDGE IS VALIDATED against <see cref="TurfArena.IsPlayable"/> when the graph is built,
    /// by sampling along it. So an edge exists only where a mower can actually drive, and the graph
    /// cannot disagree with the collision geometry — which is exactly the disagreement that had the
    /// whole cast grinding against a wall whose eight doorways were sealed.
    ///
    /// Built once, lazily, and shared by all four gardeners. It is pure geometry: no state, nothing
    /// per-competitor, nothing that changes during a match.
    /// </summary>
    public static class TurfNavGraph
    {
        /// <summary>Nodes around the outer loop.</summary>
        const int LoopNodes = 20;
        /// <summary>Nodes around the court, inside the wall.</summary>
        const int CourtNodes = 16;
        /// <summary>Nodes inside the plaza, plus its centre.</summary>
        const int PlazaNodes = 6;

        /// <summary>Metres clear of the mouth a gate node stands, on each side of the wall.</summary>
        const float GateStandoff = 3.5f;

        static Vector3[] _pos;
        static int[][] _edges;
        static bool _built;

        /// <summary>Where each node is. Index into this is a node id.</summary>
        public static Vector3[] Positions { get { Build(); return _pos; } }
        public static int Count { get { Build(); return _pos.Length; } }

        // ------------------------------------------------------------------ construction

        public static void Build()
        {
            if (_built) return;
            _built = true;

            var pos = new List<Vector3>(64);

            // The loop ring, out where everybody starts.
            int loop0 = pos.Count;
            for (int i = 0; i < LoopNodes; i++)
                pos.Add(TurfArena.Bearing(i * 360f / LoopNodes) * TurfArena.LoopMid);

            // The court ring, inside the wall. Placed midway between the plaza kerb and the hedge
            // so a lap of it never scrapes either.
            float courtR = (TurfArena.PlazaRadius + TurfArena.HedgeInner) * 0.5f;
            int court0 = pos.Count;
            for (int i = 0; i < CourtNodes; i++)
                pos.Add(TurfArena.Bearing(i * 360f / CourtNodes) * courtR);

            // Two nodes per gateway, one either side of the wall, both on the centreline. These are
            // the only nodes that can be adjacent across the wall, so every route through it is
            // forced onto a real opening rather than through the shortest bit of hedge.
            int gate0 = pos.Count;
            for (int g = 0; g < TurfArena.GapCount; g++)
            {
                Vector3 dir = TurfArena.Bearing(TurfArena.GapAngle(g));
                pos.Add(dir * (TurfArena.HedgeOuter + GateStandoff));   // outer mouth
                pos.Add(dir * (TurfArena.HedgeInner - GateStandoff));   // inner mouth
            }

            // The middle: a ring around the core, because the middle is now a ROUNDABOUT.
            //
            // There used to be a node at dead centre. There is a walled fountain island there now
            // with the bench inside it, so that node is unreachable and every edge through it would
            // be dropped by Walkable — which would leave the plaza connected only around its rim by
            // accident rather than by design. Six nodes on a ring midway between the core wall and
            // the kerb is the same crossing, made explicit, and it has the useful property that two
            // gardeners entering the plaza from opposite gates no longer steer at the same point.
            int plaza0 = pos.Count;
            float plazaR = (TurfArena.CoreRadius + TurfArena.PlazaRadius) * 0.5f;
            for (int i = 0; i < PlazaNodes; i++)
                pos.Add(TurfArena.Bearing(i * 360f / PlazaNodes) * plazaR);

            _pos = pos.ToArray();

            var edges = new List<int>[_pos.Length];
            for (int i = 0; i < edges.Length; i++) edges[i] = new List<int>(8);

            void Link(int a, int b)
            {
                if (a == b || !Walkable(_pos[a], _pos[b])) return;
                if (!edges[a].Contains(b)) edges[a].Add(b);
                if (!edges[b].Contains(a)) edges[b].Add(a);
            }

            // Round the loop and round the court.
            for (int i = 0; i < LoopNodes; i++) Link(loop0 + i, loop0 + (i + 1) % LoopNodes);
            for (int i = 0; i < CourtNodes; i++) Link(court0 + i, court0 + (i + 1) % CourtNodes);

            for (int g = 0; g < TurfArena.GapCount; g++)
            {
                int outer = gate0 + g * 2, inner = outer + 1;

                // THROUGH the wall. The one edge that crosses it, per gateway.
                Link(outer, inner);

                // And onto the rings either side. Linked to the two nearest ring nodes rather than
                // to one, so a gardener arriving from either direction along the ring has a turn
                // into the gate rather than having to overshoot and come back.
                LinkNearest(edges, _pos, outer, loop0, LoopNodes, 2);
                LinkNearest(edges, _pos, inner, court0, CourtNodes, 2);
            }

            // The plaza is open ground: everything inside the kerb sees everything else, and the
            // court ring reaches it directly.
            for (int i = 0; i < PlazaNodes; i++)
            {
                for (int j = i + 1; j < PlazaNodes; j++) Link(plaza0 + i, plaza0 + j);
                for (int c = 0; c < CourtNodes; c++) Link(plaza0 + i, court0 + c);
            }

            _edges = new int[_pos.Length][];
            for (int i = 0; i < _pos.Length; i++) _edges[i] = edges[i].ToArray();
        }

        /// <summary>Join a node to the <paramref name="want"/> closest members of a ring.</summary>
        static void LinkNearest(List<int>[] edges, Vector3[] pos, int node,
                                int ringStart, int ringCount, int want)
        {
            for (int n = 0; n < want; n++)
            {
                int best = -1;
                float bestD = float.PositiveInfinity;
                for (int i = 0; i < ringCount; i++)
                {
                    int id = ringStart + i;
                    if (edges[node].Contains(id)) continue;
                    float d = (pos[id] - pos[node]).sqrMagnitude;
                    if (d >= bestD) continue;
                    bestD = d; best = id;
                }
                if (best < 0 || !Walkable(pos[node], pos[best])) return;
                edges[node].Add(best);
                edges[best].Add(node);
            }
        }

        /// <summary>
        /// Can a mower actually drive this straight line?
        ///
        /// Sampled every metre against the same playability test the score and the collision ring
        /// use. An edge that clips a hedge corner is worse than no edge: A* would return a route
        /// that reads as valid and drives into a wall, which is the failure this whole class exists
        /// to remove.
        /// </summary>
        static bool Walkable(Vector3 a, Vector3 b)
        {
            float len = Vector3.Distance(a, b);
            int steps = Mathf.Max(2, Mathf.CeilToInt(len));
            for (int i = 0; i <= steps; i++)
                if (!TurfArena.IsPlayable(Vector3.Lerp(a, b, i / (float)steps))) return false;
            return true;
        }

        // ------------------------------------------------------------------ queries

        /// <summary>The node nearest a world position that can be driven to in a straight line.</summary>
        public static int Nearest(Vector3 world)
        {
            Build();
            int best = -1, fallback = 0;
            float bestD = float.PositiveInfinity, fallbackD = float.PositiveInfinity;

            for (int i = 0; i < _pos.Length; i++)
            {
                float d = (_pos[i] - world).sqrMagnitude;
                if (d < fallbackD) { fallbackD = d; fallback = i; }
                if (d >= bestD || !Walkable(world, _pos[i])) continue;
                bestD = d; best = i;
            }
            // A machine wedged inside a hedge can see no node at all. The plain nearest keeps it
            // moving toward something rather than freezing, and TurfCompetitor.Recover is already
            // shoving it back onto open ground while it does.
            return best >= 0 ? best : fallback;
        }

        // Scratch, shared. Every query runs on the main thread inside one bot's Update, so a single
        // set of buffers is enough and it keeps the whole search allocation-free after the first
        // call — which matters because four gardeners re-path several times a second.
        static float[] _g, _f;
        static int[] _came;
        static bool[] _closed;
        static readonly List<int> _open = new(64);

        /// <summary>
        /// A route from <paramref name="from"/> to <paramref name="to"/>, as world waypoints.
        ///
        /// Returns false when there is no path — which on a symmetric wheel means the caller asked
        /// to reach somewhere unreachable, and the honest answer is to let it keep doing whatever it
        /// was doing rather than invent a straight line into a wall.
        /// </summary>
        public static bool FindPath(Vector3 from, Vector3 to, List<Vector3> into)
        {
            Build();
            into.Clear();

            int start = Nearest(from), goal = Nearest(to);
            if (start < 0 || goal < 0) return false;

            int n = _pos.Length;
            if (_g == null || _g.Length != n)
            {
                _g = new float[n]; _f = new float[n];
                _came = new int[n]; _closed = new bool[n];
            }

            for (int i = 0; i < n; i++)
            {
                _g[i] = float.PositiveInfinity;
                _f[i] = float.PositiveInfinity;
                _came[i] = -1;
                _closed[i] = false;
            }

            _g[start] = 0f;
            _f[start] = Vector3.Distance(_pos[start], _pos[goal]);
            _open.Clear();
            _open.Add(start);

            while (_open.Count > 0)
            {
                // Linear scan for the cheapest open node. A binary heap would be the textbook
                // answer and would be slower here: the graph is fifty nodes, the open set never
                // exceeds a handful, and a heap's bookkeeping costs more than the scan it saves.
                int bestAt = 0;
                for (int i = 1; i < _open.Count; i++)
                    if (_f[_open[i]] < _f[_open[bestAt]]) bestAt = i;

                int current = _open[bestAt];
                if (current == goal)
                {
                    for (int at = goal; at >= 0; at = _came[at]) into.Add(_pos[at]);
                    into.Reverse();
                    // The first node is behind the machine as often as not — it is simply the
                    // nearest one, which may be the one just passed. Dropping it stops a gardener
                    // turning back a metre to touch a waypoint it has already driven through.
                    if (into.Count > 1) into.RemoveAt(0);
                    into.Add(to);
                    return true;
                }

                _open.RemoveAt(bestAt);
                _closed[current] = true;

                foreach (int next in _edges[current])
                {
                    if (_closed[next]) continue;
                    float g = _g[current] + Vector3.Distance(_pos[current], _pos[next]);
                    if (g >= _g[next]) continue;

                    _came[next] = current;
                    _g[next] = g;
                    _f[next] = g + Vector3.Distance(_pos[next], _pos[goal]);
                    if (!_open.Contains(next)) _open.Add(next);
                }
            }
            return false;
        }
    }
}
