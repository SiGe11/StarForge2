// MapGenerator.Cover.cs -- low walls and ruins, for the new combat only.
//
// Placed when a match under the new combat begins (GameWorld.PlaceWalls), not when the map is made: the
// map is built at scene start, before anyone has chosen the combat, and the old combat's maps must not
// change. So nothing here touches the generator's grids or streams: it reads the finished map (the
// NavMesh, the rocks, the trees, the ore) and places pieces where they cannot close a way, then the
// match rebakes the NavMesh and checks the bases still join everything (CoverCut).
//
// Two kinds of cluster: a line of two to four low walls with gaps a tank gets through, and a ruin, the
// broken outline of a small house, its corners standing taller, its doorways open. Pieces and their
// shapes: Tools/blender/build_env.py (`cover` in models.json, read into MapKit.cover by MapBuilder).
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using static StarForge.SFMath;

namespace StarForge.World
{
    public static partial class MapGenerator
    {
        /// <summary>A unit's radius: no centre comes nearer a wall's face (its Rubble is this much wider).</summary>
        public const float WallPad = 0.6f;
        /// <summary>The NavMesh area under a low wall: infantry vault it, vehicles go round, a Mauler or a Mech
        /// breaks it. A ruin's taller runs stay Rubble.</summary>
        public const int VaultArea = 4;
        /// <summary>Under a ruin's taller runs: no one on foot or wheels, only a Mauler's or a Mech's path. Its own
        /// area, above Vault's: where a tall run's volume overlaps a low run's, the higher area wins, and on
        /// Rubble (3) soldiers walked through the tall end of 11 of 11 ruin walls.</summary>
        public const int WallArea = 5;
        /// <summary>A run this high or lower is a low wall (Vault); taller, a ruin's wall (Wall). The low pieces are
        /// 1.12-1.2 m: a crouching man (1.15 m) is behind one, standing (gun 1.25) he fires over it.</summary>
        public const float VaultHigh = 1.3f;
        /// <summary>A wall shelters a crouching man only where its top stands this far over the ground he crouches
        /// on: ducked he is 1.0 m (Unit.Ducked), and a round coming down at him from 5 m or more clears a top
        /// under 1.02 before it reaches him. 1.04 left a ruin's low run 2-5 cm of rise at its places.</summary>
        public const float DuckedClear = 1.02f;
        const int CoverLines = 11, CoverRuins = 6, CoverTries = 700;
        const float CoverFromBase = 42f, CoverFromOre = 16f, CoverApart = 20f;

        public sealed class CoverResult
        {
            public int clusters, pieces, walls, dropped, ditches;
            public override string ToString() => $"{clusters} clusters, {pieces} pieces, {walls} runs of wall, {ditches} ditches" + (dropped > 0 ? $", {dropped} dropped to keep the ways open" : "");
        }

        const int DitchCount = 5;
        const float DitchFlat = 0.6f, DitchSlope = 2.0f, DitchDepth = 0.9f, DitchFromBase = 45f;
        /// <summary>How far a ditch's ground reaches out from its line (the dug-soil marks).</summary>
        public const float DitchOuter = DitchFlat + DitchSlope;
        /// <summary>The most the ground may fall across a ditch, a metre for a metre: its bank is 35 degrees at the
        /// steepest on the level, and over about 0.13 the uphill bank passes the NavMesh's 40 and cuts the way.</summary>
        const float DitchAcross = 0.09f;

        /// <summary>What stands on the map already, as (x, z, radius), and the ore seams.</summary>
        static void Standing(MapInfo info, List<Vector3> stands, List<Vector2> ore)
        {
            if (info.boulderRoot != null)
                foreach (var b in info.boulderRoot.GetComponentsInChildren<Boulder>())
                    if (!b.smashed) stands.Add(new Vector3(b.Pos.x, b.Pos.y, b.Radius));
            if (info.sceneryRoot != null)
                foreach (Transform t in info.sceneryRoot)
                {
                    float r = 2f;
                    foreach (var rd in t.GetComponentsInChildren<Renderer>()) r = Mathf.Max(r, Mathf.Max(rd.bounds.extents.x, rd.bounds.extents.z));
                    stands.Add(new Vector3(t.position.x, t.position.z, r));
                }
            if (info.oreRoot != null)
                foreach (Transform o in info.oreRoot) ore.Add(new Vector2(o.position.x, o.position.z));
            var v = info.vegetation;
            if (v != null)
                for (int i = 0; i < v.plants.Length; i++)
                {
                    var k = v.KindOf(i);
                    float r = k.blockRadius > 0f ? k.blockRadius * v.plants[i].scale : k.bush ? 0.6f * v.plants[i].scale : 0f;
                    if (r > 0f) stands.Add(new Vector3(v.plants[i].pos.x, v.plants[i].pos.z, r));
                }
        }

        /// <summary>A few ditches across open meadow (the new combat's; when a match begins, before the walls and
        /// before the NavMesh is rebuilt): 10-16 m long, 0.9 m deep, sides a man walks down. Their bottoms' lines
        /// go into <paramref name="into"/>.</summary>
        public static void DigDitches(MapInfo info, GroundDeformer ground, uint seed, List<(Vector2 a, Vector2 b)> into, bool tell = true)
        {
            into.Clear();
            if (ground == null) return;
            var rng = new Rng(seed ^ 0xD17C4u);
            int mask = GameWorld.GroundAreas;
            var stands = new List<Vector3>(1024);
            var ore = new List<Vector2>(64);
            Standing(info, stands, ore);
            Vector2 baseA = info.StartPos(0), baseB = info.StartPos(1);
            bool OnGround(Vector2 p)
            {
                if (!info.InBounds(p, 8f) || info.WaterDepth(p) > -1.4f) return false;
                if (!NavMesh.SamplePosition(info.Ground(p), out var hit, 0.8f, mask)) return false;
                return new Vector2(hit.position.x - p.x, hit.position.z - p.y).sqrMagnitude < 0.35f * 0.35f;
            }
            float outer = DitchFlat + DitchSlope;
            for (int tries = 0; tries < 400 && into.Count < DitchCount; tries++)
            {
                var p = new Vector2(rng.Range(26f, Size - 26f), rng.Range(26f, Size - 26f));
                float ang = rng.Range(0f, TAU), len = rng.Range(10f, 16f);
                var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector2 a = p - d * len * 0.5f, b = p + d * len * 0.5f, n = new Vector2(-d.y, d.x);
                if ((p - baseA).magnitude < DitchFromBase || (p - baseB).magnitude < DitchFromBase) continue;
                bool bad = false;
                foreach (var o in ore) if ((o - p).magnitude < CoverFromOre + len * 0.5f) { bad = true; break; }
                foreach (var q in into) if (SegmentDistance(p, q.a, q.b) < CoverApart) { bad = true; break; }
                if (bad) continue;
                float lo = float.MaxValue, hi = float.MinValue;
                for (int s = 0; s <= 6 && !bad; s++)
                {
                    var c = a + (b - a) * (s / 6f);
                    float hgt = info.HeightAt(c);
                    lo = Mathf.Min(lo, hgt); hi = Mathf.Max(hi, hgt);
                    if (!OnGround(c) || !OnGround(c + n * (outer + 1f)) || !OnGround(c - n * (outer + 1f))) bad = true;
                    else if (Mathf.Abs(info.HeightAt(c + n * outer) - info.HeightAt(c - n * outer)) > DitchAcross * 2f * outer) bad = true;
                }
                if (bad || hi - lo > 1.0f) continue;
                foreach (var o in stands)
                    if (SegmentDistance(new Vector2(o.x, o.y), a, b) < o.z + outer + 0.5f) { bad = true; break; }
                if (bad) continue;
                if (ground.Trench(a, b, DitchFlat, DitchSlope, DitchDepth, tell)) into.Add((a, b));
            }
        }

        struct Plan { public CoverPiece piece; public Vector2 at; public float yaw; }

        /// <summary>The map's cover, from <paramref name="seed"/> (its own stream). Walls go into
        /// <paramref name="into"/>; their pieces under the Map's "Cover" child, each run of wall on Rubble.
        /// The NavMesh is not rebuilt here.</summary>
        public static CoverResult PlaceCover(MapInfo info, MapKit kit, uint seed, List<CoverWall> into, List<(Vector2 a, Vector2 b)> ditches = null)
        {
            into.Clear();
            var res = new CoverResult();
            var root = CoverRoot(info, true);
            if (kit == null || kit.cover == null || kit.cover.Length == 0) return res;
            CoverPiece Piece(string n)
            {
                foreach (var p in kit.cover) if (p != null && p.name == n && p.prefab != null && p.segments.Length > 0) return p;
                return null;
            }
            var low = Piece("WALL_LOW");
            var shortWall = Piece("WALL_SHORT") ?? low;
            var ruinWall = Piece("RUIN_WALL") ?? low;
            var corner = Piece("RUIN_CORNER");
            if (low == null) return res;

            var rng = new Rng(seed ^ 0xC07E5u);
            int mask = GameWorld.GroundAreas;
            Vector2 baseA = info.StartPos(0), baseB = info.StartPos(1);

            // What is there already, as (x, z, radius): the ditches too, sampled along them.
            var stands = new List<Vector3>(1024);
            var ore = new List<Vector2>(64);
            Standing(info, stands, ore);
            if (ditches != null)
                foreach (var dt in ditches)
                {
                    int n = Mathf.Max(2, Mathf.CeilToInt((dt.b - dt.a).magnitude));
                    for (int i = 0; i <= n; i++)
                    {
                        var q = dt.a + (dt.b - dt.a) * (i / (float)n);
                        stands.Add(new Vector3(q.x, q.y, DitchFlat + DitchSlope + 0.5f));
                    }
                }

            bool OnGround(Vector2 p)
            {
                if (!info.InBounds(p, 6f) || info.WaterDepth(p) > -0.35f) return false;
                if (!NavMesh.SamplePosition(info.Ground(p), out var hit, 0.8f, mask)) return false;
                return new Vector2(hit.position.x - p.x, hit.position.z - p.y).sqrMagnitude < 0.35f * 0.35f;
            }
            // Open ground all round: not a pass, not a ledge.
            bool Open(Vector2 p, float r)
            {
                if (!OnGround(p)) return false;
                for (int i = 0; i < 12; i++)
                {
                    float a = i * TAU / 12f;
                    if (!OnGround(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r)) return false;
                }
                return true;
            }
            bool Fits(CoverPiece pc, Vector2 at, float yaw)
            {
                var rot = Quaternion.Euler(0f, yaw, 0f);
                float half = pc.thickness * 0.5f, low0 = float.MaxValue, high0 = float.MinValue;
                foreach (var sg in pc.segments)
                {
                    Vector2 a = at + Flat(rot * new Vector3(sg.x, 0f, sg.y)), b = at + Flat(rot * new Vector3(sg.z, 0f, sg.w));
                    Vector2 ab = b - a;
                    float len = ab.magnitude;
                    Vector2 n = len > 1e-3f ? new Vector2(-ab.y, ab.x) / len : Vector2.zero;
                    int steps = Mathf.Max(2, Mathf.CeilToInt(len / 0.6f));
                    for (int s = 0; s <= steps; s++)
                    {
                        Vector2 c = a + ab * (s / (float)steps);
                        float h = info.HeightAt(c);
                        low0 = Mathf.Min(low0, h); high0 = Mathf.Max(high0, h);
                        if (!OnGround(c + n * (half + WallPad + 0.15f)) || !OnGround(c - n * (half + WallPad + 0.15f))) return false;
                    }
                    // Clear of a rock's or tree's own pad (UnitPad 0.75) too: where two volumes overlap the
                    // higher area wins, and a wall's would open a rock's edge to soldiers.
                    foreach (var o in stands)
                        if (SegmentDistance(new Vector2(o.x, o.y), a, b) < o.z + half + WallPad + 0.85f) return false;
                    foreach (var w in into)
                        if (SegmentDistance(w.Mid, a, b) < 1.2f + half) return false;
                }
                return high0 - low0 < 0.55f;
            }

            var plans = new List<Plan>(8);
            var clusters = new List<Vector2>();
            int wantLines = CoverLines, wantRuins = corner != null ? CoverRuins : 0;
            for (int tries = 0; tries < CoverTries && (wantLines > 0 || wantRuins > 0); tries++)
            {
                bool ruin = wantRuins > 0 && (wantLines == 0 || rng.F01() < 0.4f);
                var p = new Vector2(rng.Range(24f, Size - 24f), rng.Range(24f, Size - 24f));
                float yaw = rng.Range(0f, 360f);
                if ((p - baseA).magnitude < CoverFromBase || (p - baseB).magnitude < CoverFromBase) continue;
                bool near = false;
                foreach (var c in clusters) if ((c - p).magnitude < CoverApart) { near = true; break; }
                foreach (var o in ore) if ((o - p).magnitude < CoverFromOre) { near = true; break; }
                if (near || !Open(p, ruin ? 8f : 7f)) continue;

                plans.Clear();
                var rot = Quaternion.Euler(0f, yaw, 0f);
                Vector2 Along(float x, float z) => p + Flat(rot * new Vector3(x, 0f, z));
                if (!ruin)
                {
                    // Two to four lengths of field wall with gaps a tank gets through.
                    int n = rng.IRange(2, 5);
                    var parts = new List<(CoverPiece pc, float len)>();
                    float total = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        var pc = rng.F01() < 0.3f ? shortWall : low;
                        float len = PieceLength(pc);
                        parts.Add((pc, len));
                        total += len + (i > 0 ? 0f : 0f);
                    }
                    var gaps = new float[n];
                    for (int i = 1; i < n; i++) { gaps[i] = rng.Range(3.2f, 4.8f); total += gaps[i]; }
                    float x = -total * 0.5f;
                    for (int i = 0; i < n; i++)
                    {
                        x += gaps[i];
                        float mid = x + parts[i].len * 0.5f;
                        float side = rng.Range(-0.45f, 0.45f), turn = rng.Range(-9f, 9f);
                        plans.Add(new Plan { piece = parts[i].pc, at = Along(mid, side), yaw = yaw + turn + (rng.F01() < 0.5f ? 180f : 0f) });
                        x += parts[i].len;
                    }
                }
                else
                {
                    // The outline of a small house: two corners standing, a wall or two, doorways.
                    float w = rng.Range(8.6f, 10.2f), d = rng.Range(6.4f, 8.2f);
                    // A corner's angle is at (-1.5, -1.3) in its own frame, its arms along +x (3.0) and +z (2.6).
                    Vector2 angle = new Vector2(-1.5f, -1.3f);
                    plans.Add(new Plan { piece = corner, at = Along(-w * 0.5f - angle.x, -d * 0.5f - angle.y), yaw = yaw });
                    plans.Add(new Plan { piece = corner, at = Along(w * 0.5f + angle.x, d * 0.5f + angle.y), yaw = yaw + 180f });
                    // Along the -z side, from the first corner's arm to the far end: a short wall at the end.
                    if (w - 3f - 2f >= 3.4f && rng.F01() < 0.75f)
                        plans.Add(new Plan { piece = shortWall, at = Along(w * 0.5f - 1f, -d * 0.5f), yaw = yaw });
                    // Along the +z side, from the near end: the ruined house wall.
                    if (w - 3f - 3.2f >= 3.2f || rng.F01() < 0.5f)
                        plans.Add(new Plan { piece = ruinWall, at = Along(-w * 0.5f + 1.6f, d * 0.5f), yaw = yaw + 180f });
                }

                int before = into.Count;
                var placed = new List<GameObject>();
                foreach (var pl in plans)
                {
                    if (!Fits(pl.piece, pl.at, pl.yaw)) continue;
                    placed.Add(PlacePiece(info, root, pl, clusters.Count, into));
                }
                if (placed.Count < 2)
                {
                    // One piece alone is no line and no ruin.
                    foreach (var go in placed) KillCover(go);
                    into.RemoveRange(before, into.Count - before);
                    continue;
                }
                clusters.Add(p);
                res.clusters++;
                res.pieces += placed.Count;
                if (ruin) wantRuins--; else wantLines--;
            }
            res.walls = into.Count;
            return res;
        }

        static float PieceLength(CoverPiece pc)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var sg in pc.segments) { lo = Mathf.Min(lo, Mathf.Min(sg.x, sg.z)); hi = Mathf.Max(hi, Mathf.Max(sg.x, sg.z)); }
            return hi - lo;
        }

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        public static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        static GameObject PlacePiece(MapInfo info, Transform root, Plan pl, int cluster, List<CoverWall> into)
        {
            var go = Instantiate(pl.piece.prefab, root);
            go.name = pl.piece.name;
            var rot = Quaternion.Euler(0f, pl.yaw, 0f);
            // On the lowest ground under it, a little sunk, so no part of it stands on air (a dip mid-run too).
            float lowest = float.MaxValue;
            foreach (var sg in pl.piece.segments)
                for (int s = 0; s <= 4; s++)
                {
                    float t = s / 4f;
                    lowest = Mathf.Min(lowest, info.HeightAt(pl.at + Flat(rot * new Vector3(Mathf.Lerp(sg.x, sg.z, t), 0f, Mathf.Lerp(sg.y, sg.w, t)))));
                }
            go.transform.SetPositionAndRotation(new Vector3(pl.at.x, lowest - 0.06f, pl.at.y), rot);
            Get<NavMeshModifier>(go).ignoreFromBuild = true;
            for (int i = 0; i < pl.piece.segments.Length; i++)
            {
                var sg = pl.piece.segments[i];
                Vector2 la = new Vector2(sg.x, sg.y), lb = new Vector2(sg.z, sg.w);
                Vector2 d = lb - la;
                float len = d.magnitude;
                // Its Rubble, along the run: only a Mauler's or a Mech's path crosses it.
                var vgo = new GameObject("Rubble");
                vgo.transform.SetParent(go.transform, false);
                vgo.transform.localPosition = new Vector3((la.x + lb.x) * 0.5f, 0f, (la.y + lb.y) * 0.5f);
                vgo.transform.localRotation = Quaternion.Euler(0f, -Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, 0f);
                var vol = vgo.AddComponent<NavMeshModifierVolume>();
                float runHeight = i < pl.piece.heights.Length ? pl.piece.heights[i] : 1f;
                vol.area = runHeight <= VaultHigh ? VaultArea : WallArea;
                vol.center = new Vector3(0f, 1f, 0f);
                vol.size = new Vector3(len + 2f * WallPad, 4f, pl.piece.thickness + 2f * WallPad);
                Vector3 wa = go.transform.TransformPoint(new Vector3(la.x, 0f, la.y)), wb = go.transform.TransformPoint(new Vector3(lb.x, 0f, lb.y));
                into.Add(new CoverWall
                {
                    a = new Vector2(wa.x, wa.z), b = new Vector2(wb.x, wb.z), half = pl.piece.thickness * 0.5f,
                    height = i < pl.piece.heights.Length ? pl.piece.heights[i] : 1f, piece = go, cluster = cluster,
                    top = go.transform.position.y + (i < pl.piece.heights.Length ? pl.piece.heights[i] : 1f),
                });
            }
            return go;
        }

        static void KillCover(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            go.transform.SetParent(null);
            if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
        }

        /// <summary>The Map's "Cover" child, emptied first if asked.</summary>
        public static Transform CoverRoot(MapInfo info, bool clear)
        {
            var t = info.transform.Find("Cover");
            if (t == null)
            {
                t = new GameObject("Cover").transform;
                t.SetParent(info.transform, false);
            }
            if (clear)
                for (int i = t.childCount - 1; i >= 0; i--) KillCover(t.GetChild(i).gameObject);
            return t;
        }

        /// <summary>Takes a cluster's pieces away (its walls out of the list).</summary>
        public static void DropCluster(List<CoverWall> walls, int cluster)
        {
            for (int i = walls.Count - 1; i >= 0; i--)
            {
                if (walls[i].cluster != cluster) continue;
                KillCover(walls[i].piece);
                walls.RemoveAt(i);
            }
        }

        /// <summary>On the NavMesh as it stands: false if a ground unit still gets from either base to the
        /// other and to every ore seam; else true, with <paramref name="where"/> where the way it was
        /// trying for ended.</summary>
        public static bool CoverCut(MapInfo info, out Vector2 where)
        {
            where = default;
            int mask = GameWorld.GroundAreas;
            var path = new NavMeshPath();
            if (!NavMesh.SamplePosition(info.Ground(info.StartPos(0)), out var ha, 8f, mask) ||
                !NavMesh.SamplePosition(info.Ground(info.StartPos(1)), out var hb, 8f, mask)) return false;
            Vector2 end = default;
            bool Joined(Vector3 from, Vector3 to)
            {
                NavMesh.CalculatePath(from, to, mask, path);
                if (path.status == NavMeshPathStatus.PathComplete) return true;
                var c = path.corners.Length > 0 ? path.corners[path.corners.Length - 1] : from;
                end = new Vector2(c.x, c.z);
                return false;
            }
            bool cut = !Joined(ha.position, hb.position);
            if (!cut && info.oreRoot != null)
                foreach (Transform o in info.oreRoot)
                {
                    var g = new Vector2(o.position.x, o.position.z);
                    if (!NavMesh.SamplePosition(info.Ground(g), out var ho, 8f, mask)) continue;
                    if (Joined(ha.position, ho.position) || Joined(hb.position, ho.position)) continue;
                    cut = true;
                    break;
                }
            where = end;
            return cut;
        }

        /// <summary>Drop the clusters nearest where a way was cut until none is, rebuilding the NavMesh
        /// each time (at most four rounds; then all of them). A ditch is never filled in again: what cuts a way
        /// with every wall gone is logged (DigDitches keeps off ground that falls across one, which would).</summary>
        public static int OpenCutWays(MapInfo info, NavMeshSurface surface, List<CoverWall> walls)
        {
            int dropped = 0;
            for (int round = 0; round < 6; round++)
            {
                if (!CoverCut(info, out var where)) return dropped;
                if (walls.Count == 0 || round == 5)
                {
                    Debug.LogWarning($"[StarForge] cover: a way is still cut near {where} with {walls.Count} runs of wall left");
                    return dropped;
                }
                if (round == 4)
                {
                    var all = new HashSet<int>();
                    foreach (var w in walls) all.Add(w.cluster);
                    foreach (int c in all) DropCluster(walls, c);
                    dropped += all.Count;
                }
                else
                {
                    var near = new HashSet<int>();
                    foreach (var w in walls) if (w.Distance(where) < 30f) near.Add(w.cluster);
                    if (near.Count == 0)
                    {
                        // Not by any wall: nearest one only, the cut may be elsewhere.
                        CoverWall best = null;
                        foreach (var w in walls) if (best == null || w.Distance(where) < best.Distance(where)) best = w;
                        near.Add(best.cluster);
                    }
                    foreach (int c in near) DropCluster(walls, c);
                    dropped += near.Count;
                }
                Rebuild(surface);
            }
            return dropped;
        }

        /// <summary>BuildNavMesh, letting go of the data it replaces (built at run time: the match's copy or an
        /// earlier rebuild, never the scene's asset).</summary>
        public static void Rebuild(NavMeshSurface surface)
        {
            var old = surface.navMeshData;
            surface.BuildNavMesh();
            if (old != null && old != surface.navMeshData) UnityEngine.Object.Destroy(old);
        }
    }
}
