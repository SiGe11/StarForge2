// NavIslands.cs — which stretches of the NavMesh join up, so that a unit is only
// sent where it can get to.
//
// NavMesh.SamplePosition answers with the nearest polygon anywhere, including a
// patch no unit can reach from where it stands: a plateau top, a clearing ringed by
// trees and boulders that only a Mauler can open, a sliver between a building and a
// cliff. A unit sent there gets a partial path, walks to its end and stands; on
// attack-move it asks for the same partial path every 1.2 s, and each of those
// searches runs through the whole map before giving up. So every NavMesh triangle
// is labelled with the connected piece of it (the island) it belongs to -- once
// over the ground every unit walks (GameWorld.GroundAreas), once with the Rubble a
// Mauler drives through -- and GameWorld.NearestReachable moves a destination onto
// the island the unit stands on.
//
// Triangles join the way Detour joins its polygons: along a shared edge, and across
// a tile border, where the two tiles split the border at different points, wherever
// their edges overlap on the border line. Built from NavMesh.CalculateTriangulation
// on first use. A change to the NavMesh (a rebuild after a Mauler clears a way, a
// structure or an ore field carving its footprint or leaving it) marks it stale; it
// is rebuilt only when an answer would turn on it, that is when a point looks cut
// off. Rebuilds and departures only ever join islands, so an answer that two points
// are joined stays right on stale data.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.AI.Navigation.LowLevel;
using Unity.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace StarForge.World
{
    public sealed class NavIslands
    {
        /// <summary>Area masks the islands are worked out for.</summary>
        public const int Ground = 0, WithRubble = 1;

        const float Cell = 4f;       // lookup grid
        const float Inset = 0.3f;    // how far inside its triangle a moved point is put
        const int MaxVerts = 32, MaxNeighbours = 64;

        Vector2[] corner;                          // three per triangle, on the ground plane
        float[] height;                            // mean height per triangle
        readonly int[][] island = new int[2][];    // per mask and triangle; -1 where the mask leaves it out
        readonly float[][] area = new float[2][];  // per mask and island, m² on the ground plane
        readonly int[] main = new int[2];          // the largest island per mask
        int[] cellStart, cellTris;
        Vector2 origin;
        int cols, rows;

        bool built, stale;
        int settleFrame;
        float nextBuild;
        readonly Func<bool> rebuilding;
        readonly int agentType;

        public int Triangles { get; private set; }
        public int Builds { get; private set; }
        public float TriangulateMs { get; private set; }
        public float LabelMs { get; private set; }
        public int Polygons { get; private set; }
        public float WalkMs { get; private set; }
        /// <summary>Polygons with more neighbours than the query buffer holds (should be 0).</summary>
        public int Truncated { get; private set; }

        /// <param name="navRebuilding">True while a NavMesh rebuild is running (the
        /// islands wait for it to finish).</param>
        /// <param name="agentTypeID">The agent type the NavMesh was baked for.</param>
        public NavIslands(Func<bool> navRebuilding, int agentTypeID)
        {
            rebuilding = navRebuilding;
            agentType = agentTypeID;
        }

        /// <summary>The NavMesh changed (or will in a moment): work the islands out
        /// again before the next answer that depends on them.</summary>
        public void Invalidate()
        {
            stale = true;
            settleFrame = Time.frameCount + 2;   // carving lands a frame or so later
        }

        // ------------------------------------------------------------ queries
        /// <summary>Whether the NavMesh under (or nearest) <paramref name="to"/> joins
        /// the NavMesh under (or nearest) <paramref name="from"/>.</summary>
        public bool Connected(Vector2 from, Vector2 to, int mask)
        {
            if (!Ensure()) return true;
            int a = IslandAt(from, mask), b = IslandAt(to, mask);
            if (a == b || a < 0 || b < 0) return true;
            if (!Refresh()) return false;
            a = IslandAt(from, mask);
            b = IslandAt(to, mask);
            return a == b || a < 0 || b < 0;
        }

        /// <summary>The nearest point to <paramref name="p"/> on the island
        /// <paramref name="from"/> is on: <paramref name="p"/> itself when it lies on
        /// that island, else the closest point of it, set a little inside its triangle
        /// so the agent's own lookup lands on the same polygon. False when there is no
        /// NavMesh to go by.</summary>
        public bool TryNearest(Vector2 from, Vector2 p, int mask, out Vector2 q)
        {
            q = p;
            if (!Ensure()) return false;
            for (int pass = 0; ; pass++)
            {
                int home = IslandAt(from, mask);
                if (home < 0) return false;
                int t = Containing(p, mask);
                if (t >= 0 && island[mask][t] == home) { q = p; return true; }
                int best = Search(p, mask, home, out q, out float d2);
                if (best < 0) return false;
                // Moved off ground that looks cut off: make sure it still is.
                if (pass == 0 && stale)
                {
                    bool other = t >= 0 || (Search(p, mask, -1, out _, out float any) >= 0 && any + 0.01f < d2);
                    if (other && Refresh()) continue;
                }
                q = InsetInto(q, best);
                return true;
            }
        }

        /// <summary>The island under (or nearest) a point; -1 with no NavMesh.</summary>
        public int IslandAt(Vector2 p, int mask)
        {
            if (!Ensure()) return -1;
            int t = Containing(p, mask);
            if (t < 0) t = Search(p, mask, -1, out _, out _);
            return t < 0 ? -1 : island[mask][t];
        }

        public int IslandCount(int mask) { Ensure(); return area[mask] != null ? area[mask].Length : 0; }
        public int MainIsland(int mask) { Ensure(); return main[mask]; }
        public float IslandArea(int mask, int i) => i >= 0 && area[mask] != null && i < area[mask].Length ? area[mask][i] : 0f;
        public int TriangleIsland(int mask, int t) => island[mask][t];
        public Vector3 TriangleCentre(int t)
        {
            var c = (corner[3 * t] + corner[3 * t + 1] + corner[3 * t + 2]) / 3f;
            return new Vector3(c.x, height[t], c.y);
        }
        public float TriangleArea(int t) => Mathf.Abs(Cross(corner[3 * t], corner[3 * t + 1], corner[3 * t + 2])) * 0.5f;

        // ------------------------------------------------------------ building
        bool Ensure()
        {
            if (!built) Build();
            return Triangles > 0;
        }

        /// <summary>Rebuild if the NavMesh changed and has settled. True if it did.</summary>
        bool Refresh()
        {
            if (!stale || Time.frameCount < settleFrame || (rebuilding != null && rebuilding())) return false;
            // Every structure placed marks the islands stale, and a staging point or guard
            // post on a plateau then asks for them again: no more than once in two seconds.
            if (Time.time < nextBuild) return false;
            Build();
            return true;
        }

        public void Build()
        {
            built = true;
            // Asked before a change has landed (a structure placed this frame carves
            // its footprint on the next): still stale.
            stale = stale && Time.frameCount < settleFrame;
            nextBuild = Time.time + 2f;
            Builds++;
            var sw = Stopwatch.StartNew();
            var tri = NavMesh.CalculateTriangulation();
            TriangulateMs = (float)sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            // Walk Detour's own polygon graph, from a polygon under every triangle of the
            // triangulation so no stretch is missed. Polygons get their indices in the
            // order they are found and are taken off the queue in that same order.
            var nav = NavWorld.GetDefaultWorld();
            int rubble = NavMesh.GetAreaFromName("Rubble");
            var index = new Dictionary<NavNode, int>(4096);
            var nodes = new List<NavNode>(4096);
            var polyArea = new List<int>(4096);
            var vertStart = new List<int>(4096) { 0 };
            var verts = new List<Vector3>(16384);
            var nbrStart = new List<int>(4096) { 0 };
            var nbrs = new List<int>(16384);
            var vbuf = new NativeArray<Vector3>(MaxVerts, Allocator.Temp);
            var nbuf = new NativeArray<NavNode>(MaxNeighbours, Allocator.Temp);
            var ebuf = new NativeArray<byte>(MaxNeighbours, Allocator.Temp);
            var tv = tri.vertices;
            var ti = tri.indices;
            var extents = new Vector3(0.5f, 2f, 0.5f);
            Truncated = 0;
            int next = 0;
            for (int t = 0; t + 2 < ti.Length; t += 3)
            {
                // The triangulation fans each polygon out from its first corner: one
                // lookup a polygon is enough.
                if (t > 0 && ti[t] == ti[t - 3]) continue;
                var loc = nav.MapLocation((tv[ti[t]] + tv[ti[t + 1]] + tv[ti[t + 2]]) / 3f, extents, agentType, NavMesh.AllAreas);
                if (loc.node.IsNull() || index.ContainsKey(loc.node)) continue;
                index[loc.node] = nodes.Count;
                nodes.Add(loc.node);
                for (; next < nodes.Count; next++)
                {
                    var node = nodes[next];
                    polyArea.Add(nav.GetAreaIndexForNode(node));
                    var status = nav.GetEdgesAndNeighbors(node, vbuf, nbuf, ebuf, out int nv, out int nn);
                    if ((status & NavQueryStatus.MoreDataAvailable) != 0) Truncated++;
                    if (nav.GetNodeType(node) == NavNodeType.Polygon)
                    {
                        nav.GetInstanceTransform(node, out var at, out var rot);
                        for (int k = 0; k < nv; k++) verts.Add(at + rot * vbuf[k]);
                    }
                    for (int k = 0; k < nn; k++)
                    {
                        var nb = nbuf[k];
                        if (nb.IsNull()) continue;
                        if (!index.TryGetValue(nb, out int j))
                        {
                            j = nodes.Count;
                            index[nb] = j;
                            nodes.Add(nb);
                        }
                        nbrs.Add(j);
                    }
                    vertStart.Add(verts.Count);
                    nbrStart.Add(nbrs.Count);
                }
            }
            vbuf.Dispose();
            nbuf.Dispose();
            ebuf.Dispose();
            int np = nodes.Count;
            Polygons = np;
            WalkMs = (float)sw.Elapsed.TotalMilliseconds;

            // The polygons as fans of triangles, on the ground plane.
            int nt = 0;
            for (int i = 0; i < np; i++) nt += Mathf.Max(0, vertStart[i + 1] - vertStart[i] - 2);
            Triangles = nt;
            corner = new Vector2[nt * 3];
            height = new float[nt];
            var triPoly = new int[nt];
            for (int i = 0, t = 0; i < np; i++)
            {
                int v0 = vertStart[i], n = vertStart[i + 1] - v0;
                for (int k = 1; k + 1 < n; k++, t++)
                {
                    Vector3 a = verts[v0], b = verts[v0 + k], c = verts[v0 + k + 1];
                    corner[3 * t] = new Vector2(a.x, a.z);
                    corner[3 * t + 1] = new Vector2(b.x, b.z);
                    corner[3 * t + 2] = new Vector2(c.x, c.z);
                    height[t] = (a.y + b.y + c.y) / 3f;
                    triPoly[t] = i;
                }
            }

            // Islands: polygons joined through the graph, over the areas each mask uses.
            var label = new int[np];
            var stack = new Stack<int>();
            for (int m = 0; m < 2; m++)
            {
                bool Uses(int i) => m == WithRubble || polyArea[i] != rubble;
                for (int i = 0; i < np; i++) label[i] = -1;
                int count = 0;
                for (int s0 = 0; s0 < np; s0++)
                {
                    if (label[s0] >= 0 || !Uses(s0)) continue;
                    label[s0] = count;
                    stack.Push(s0);
                    while (stack.Count > 0)
                    {
                        int i = stack.Pop();
                        for (int k = nbrStart[i]; k < nbrStart[i + 1]; k++)
                        {
                            int j = nbrs[k];
                            if (label[j] >= 0 || !Uses(j)) continue;
                            label[j] = count;
                            stack.Push(j);
                        }
                    }
                    count++;
                }
                var lab = new int[nt];
                var areas = new float[count];
                for (int t = 0; t < nt; t++)
                {
                    lab[t] = label[triPoly[t]];
                    if (lab[t] >= 0) areas[lab[t]] += TriangleArea(t);
                }
                island[m] = lab;
                area[m] = areas;
                main[m] = 0;
                for (int i = 1; i < count; i++) if (areas[i] > areas[main[m]]) main[m] = i;
            }

            // Lookup grid: the triangles overlapping each cell.
            Vector2 lo = new Vector2(float.MaxValue, float.MaxValue), hi = -lo;
            foreach (var c in corner) { lo = Vector2.Min(lo, c); hi = Vector2.Max(hi, c); }
            if (nt == 0) lo = hi = Vector2.zero;
            origin = lo;
            cols = Mathf.Max(1, Mathf.CeilToInt((hi.x - lo.x) / Cell));
            rows = Mathf.Max(1, Mathf.CeilToInt((hi.y - lo.y) / Cell));
            cellStart = new int[cols * rows + 1];
            var fill = new int[cols * rows];
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    for (int i = 1; i <= cols * rows; i++) cellStart[i] += cellStart[i - 1];
                    cellTris = new int[cellStart[cols * rows]];
                }
                for (int t = 0; t < nt; t++)
                {
                    Vector2 a = corner[3 * t], b = corner[3 * t + 1], c = corner[3 * t + 2];
                    int x0 = CellX(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), x1 = CellX(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
                    int z0 = CellZ(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), z1 = CellZ(Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int ci = z * cols + x;
                            if (pass == 0) cellStart[ci + 1]++;
                            else cellTris[cellStart[ci] + fill[ci]++] = t;
                        }
                }
            }
            LabelMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        // ------------------------------------------------------------ geometry
        int CellX(float x) => Mathf.Clamp((int)((x - origin.x) / Cell), 0, cols - 1);
        int CellZ(float z) => Mathf.Clamp((int)((z - origin.y) / Cell), 0, rows - 1);

        int Containing(Vector2 p, int mask)
        {
            if (p.x < origin.x || p.y < origin.y || p.x > origin.x + cols * Cell || p.y > origin.y + rows * Cell) return -1;
            int ci = CellZ(p.y) * cols + CellX(p.x);
            var lab = island[mask];
            for (int k = cellStart[ci]; k < cellStart[ci + 1]; k++)
            {
                int t = cellTris[k];
                if (lab[t] >= 0 && Inside(p, corner[3 * t], corner[3 * t + 1], corner[3 * t + 2])) return t;
            }
            return -1;
        }

        /// <summary>The triangle nearest <paramref name="p"/> on island <paramref name="want"/>
        /// (any island when negative), searching outward ring by ring over the grid.</summary>
        int Search(Vector2 p, int mask, int want, out Vector2 q, out float bestD2)
        {
            q = p;
            bestD2 = float.MaxValue;
            int bestT = -1;
            var lab = island[mask];
            int cx = CellX(p.x), cz = CellZ(p.y), maxR = Mathf.Max(cols, rows);
            for (int r = 0; r <= maxR; r++)
            {
                // Every cell in ring r lies at least r - 1 cells from p.
                float reach = Mathf.Max(0, r - 1) * Cell;
                if (bestT >= 0 && bestD2 <= reach * reach) break;
                for (int z = cz - r; z <= cz + r; z++)
                {
                    if (z < 0 || z >= rows) continue;
                    bool fullRow = z == cz - r || z == cz + r;
                    for (int x = cx - r; x <= cx + r; x += fullRow ? 1 : 2 * r)
                    {
                        if (x >= 0 && x < cols)
                        {
                            int ci = z * cols + x;
                            for (int k = cellStart[ci]; k < cellStart[ci + 1]; k++)
                            {
                                int t = cellTris[k];
                                int l = lab[t];
                                if (l < 0 || (want >= 0 && l != want)) continue;
                                var c = Closest(p, t);
                                float d2 = (c - p).sqrMagnitude;
                                if (d2 < bestD2) { bestD2 = d2; bestT = t; q = c; }
                            }
                        }
                        if (r == 0) break;
                    }
                }
            }
            return bestT;
        }

        Vector2 Closest(Vector2 p, int t)
        {
            Vector2 a = corner[3 * t], b = corner[3 * t + 1], c = corner[3 * t + 2];
            if (Inside(p, a, b, c)) return p;
            Vector2 best = OnSegment(p, a, b), s = OnSegment(p, b, c);
            if ((s - p).sqrMagnitude < (best - p).sqrMagnitude) best = s;
            s = OnSegment(p, c, a);
            if ((s - p).sqrMagnitude < (best - p).sqrMagnitude) best = s;
            return best;
        }

        Vector2 InsetInto(Vector2 q, int t)
        {
            var centre = (corner[3 * t] + corner[3 * t + 1] + corner[3 * t + 2]) / 3f;
            Vector2 d = centre - q;
            float len = d.magnitude;
            return len < 1e-4f ? q : q + d * Mathf.Min(0.5f, Inset / len);
        }

        static Vector2 OnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float l2 = ab.sqrMagnitude;
            if (l2 < 1e-12f) return a;
            return a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
        }

        static float Cross(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(a, b, p), d2 = Cross(b, c, p), d3 = Cross(c, a, p);
            const float e = 1e-5f;
            bool neg = d1 < -e || d2 < -e || d3 < -e, pos = d1 > e || d2 > e || d3 > e;
            return !(neg && pos);
        }
    }
}
