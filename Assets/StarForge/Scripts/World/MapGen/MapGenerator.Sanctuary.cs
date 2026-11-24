// MapGenerator.Sanctuary.cs -- where the kept Vesper twin house stands, and the ruined twin cloisters.
// Placed when a match begins, before the ditches and the cover (GameWorld.PlaceWalls), from streams of their
// own (Rng from the map seed ^ a salt), so the draws of the cover and the ditches are untouched where neither is
// drawn; and only on real matches (GameWorld.SanctuaryAllowed: NightRules.RealMatch, set by GameBootstrap), so
// every fixed seed, evaluation, benchmark and trial plays the map it played before.
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using static StarForge.SFMath;

namespace StarForge.World
{
    public static partial class MapGenerator
    {
        /// <summary>The NavMesh area of the sanctuary's grounds: on foot only (GameWorld.FootAreas and the
        /// Digger's mask have it; GroundAreas, the heavies' mask and the islands do not). Its own index above
        /// every other volume's, so it must never overlap the walls' Not Walkable volumes.</summary>
        public const int SanctuaryArea = 6;
        /// <summary>The house's object under the map's Cover (PlaceCover keeps it when it clears last match's pieces).</summary>
        public const string HouseName = "VESPER_HOUSE";
        const uint SanctSalt = 0x5A2C7Eu, CloisterSalt = 0xC1015Eu;

        /// <summary>Where the house reserves its ground for the ditches and the cover (Standing reads it).</summary>
        static readonly List<Vector3> reserved = new List<Vector3>(8);

        /// <summary>Why candidate spots failed on the last placement (base, ore, ground, slope, stands), for checks.</summary>
        public static readonly int[] SanctFails = new int[5];

        /// <summary>Whether this map has the house: about half of them (salt 0x5A2C7E).</summary>
        public static bool SanctuaryDrawn(uint mapSeed) => NightRules.Hash(mapSeed ^ SanctSalt) % 1000u < 500u;

        /// <summary>Places the house on open ground beside the line between the bases, its gates toward that
        /// line, clear of ore, rocks, trees and scenery, with its NavMesh volumes and its runs for sight
        /// (<paramref name="sightRuns"/>). Null when the kit has no house or no spot fits.</summary>
        public static Sanctuary PlaceSanctuary(MapInfo info, MapKit kit, uint mapSeed, List<CoverWall> sightRuns)
        {
            sightRuns.Clear();
            reserved.Clear();
            if (kit == null || kit.cover == null) return null;
            CoverPiece piece = null;
            foreach (var p in kit.cover) if (p != null && p.name == "VESPER_HOUSE" && p.prefab != null) piece = p;
            if (piece == null) return null;
            var rng = new Rng(mapSeed ^ SanctSalt);
            Vector2 A = info.StartPos(0), B = info.StartPos(1), mid = (A + B) * 0.5f;
            Vector2 axis = (B - A).normalized, perp = new Vector2(-axis.y, axis.x);
            float span = (B - A).magnitude;
            var stands = new List<Vector3>(1024);
            var ore = new List<Vector2>(64);
            // Rocks and plants are cleared off its ground (the user's call, 2026-11-24): only scenery and ore keep it
            // off, and the ground test takes their Rubble as ground. Ledges, slopes and water still do not fit.
            Standing(info, stands, ore, false);
            int mask = GameWorld.GroundAreas | (1 << RubbleArea);
            bool OnGround(Vector2 p)
            {
                if (!info.InBounds(p, 6f) || info.WaterDepth(p) > -0.35f) return false;
                if (!NavMesh.SamplePosition(info.Ground(p), out var hit, 0.8f, mask)) return false;
                return new Vector2(hit.position.x - p.x, hit.position.z - p.y).sqrMagnitude < 0.35f * 0.35f;
            }
            System.Array.Clear(SanctFails, 0, SanctFails.Length);
            for (int tries = 0; tries < 800; tries++)
            {
                float side = rng.F01() < 0.5f ? -1f : 1f;
                Vector2 c = mid + axis * (rng.Range(-0.3f, 0.3f) * span) + perp * (side * rng.Range(22f, 110f));
                if ((c - A).magnitude < 62f || (c - B).magnitude < 62f) { SanctFails[0]++; continue; }
                bool bad = false;
                foreach (var o in ore) if ((o - c).magnitude < 24f) { bad = true; break; }
                if (bad) { SanctFails[1]++; continue; }
                // Gates toward the line between the bases: local -Z points at it, so +Z points away.
                Vector2 away = perp * side;
                var s = new Sanctuary(c, Mathf.Atan2(away.x, away.y) * Mathf.Rad2Deg + rng.Range(-20f, 20f));
                // Open, dry, near level: every 2 m over the plan and 3 m round it.
                float lo = float.MaxValue, hi = float.MinValue;
                for (float x = -Sanctuary.HalfX - 3f; x <= Sanctuary.HalfX + 3f && !bad; x += 2f)
                    for (float z = -Sanctuary.HalfZ - 4f; z <= Sanctuary.HalfZ + 3f && !bad; z += 2f)
                    {
                        Vector2 q = s.ToWorld(x, z);
                        if (!OnGround(q)) { bad = true; break; }
                        if (Mathf.Abs(x) <= Sanctuary.HalfX + 0.5f && Mathf.Abs(z) <= Sanctuary.HalfZ + 0.5f)
                        {
                            float h = info.HeightAt(q);
                            lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                        }
                    }
                // Each gate's way in: open ground a metre at a time 8 m out before it, joined to base A's ground
                // (with the Rubble that will be cleared): a gate onto a ledge left its court cut off.
                for (int g = 0; g < 2 && !bad; g++)
                {
                    for (float z = -Sanctuary.HalfZ - 1f; z >= -Sanctuary.HalfZ - 8f && !bad; z -= 1f)
                        for (float dx = -1.5f; dx <= 1.5f && !bad; dx += 1.5f)
                            if (!OnGround(s.ToWorld(Sanctuary.GateX[g] + dx, z))) bad = true;
                    if (!bad && !Joined(info, s.ToWorld(Sanctuary.GateX[g], -Sanctuary.HalfZ - 6f), mask)) bad = true;
                }
                if (bad) { SanctFails[2]++; continue; }
                if (hi - lo > 1.2f) { SanctFails[3]++; continue; }
                foreach (var o in stands)
                {
                    Vector2 l = s.Local(new Vector2(o.x, o.y));
                    float dx = Mathf.Max(0f, Mathf.Abs(l.x) - Sanctuary.HalfX), dz = Mathf.Max(0f, Mathf.Abs(l.y) - Sanctuary.HalfZ);
                    if (Mathf.Sqrt(dx * dx + dz * dz) < o.z + 1.0f) { bad = true; break; }
                }
                if (bad) { SanctFails[4]++; continue; }
                Build(info, s, piece, lo, sightRuns);
                ClearGround(info, s);
                // Reserve its ground (circles along its length, with the walls' margin) for the ditches and cover.
                for (int k = -2; k <= 2; k++)
                {
                    Vector2 q = s.ToWorld(k * 5.5f, -1f);
                    reserved.Add(new Vector3(q.x, q.y, 11f));
                }
                return s;
            }
            return null;
        }

        static readonly NavMeshPath joinPath = new NavMeshPath();

        /// <summary>A ground path from base A's start to this point, on the given areas.</summary>
        static bool Joined(MapInfo info, Vector2 p, int mask)
        {
            if (!NavMesh.SamplePosition(info.Ground(info.StartPos(0)), out var ha, 8f, GameWorld.GroundAreas)) return true;
            if (!NavMesh.SamplePosition(info.Ground(p), out var hp, 1.5f, mask)) return false;
            NavMesh.CalculatePath(ha.position, hp.position, mask, joinPath);
            return joinPath.status == NavMeshPathStatus.PathComplete;
        }

        /// <summary>After the NavMesh is rebuilt with the house: whether a soldier from base A cannot walk into either
        /// court (GameWorld.PlaceWalls then takes the house away).</summary>
        public static bool CourtsCut(MapInfo info, Sanctuary s)
        {
            for (int c = 0; c < 2; c++)
            {
                Vector2 court = s.ToWorld(Sanctuary.GateX[c], -3f);
                if (!NavMesh.SamplePosition(info.Ground(court), out _, 1.5f, 1 << SanctuaryArea)) return true;
                if (!NavMesh.SamplePosition(info.Ground(info.StartPos(0)), out var ha, 8f, GameWorld.GroundAreas)) return false;
                NavMesh.SamplePosition(info.Ground(court), out var hc, 1.5f, 1 << SanctuaryArea);
                NavMesh.CalculatePath(ha.position, hc.position, GameWorld.FootAreas, joinPath);
                if (joinPath.status != NavMeshPathStatus.PathComplete) return true;
            }
            return false;
        }

        /// <summary>Clears the house's reserve (after the cover is placed, or when a match has no house).</summary>
        public static void ClearSanctuaryReserve() => reserved.Clear();

        /// <summary>Takes the house away again (a way it cut); the NavMesh is rebuilt by the caller.</summary>
        public static void RemoveSanctuary(Sanctuary s)
        {
            if (s == null) return;
            if (s.piece != null) KillCover(s.piece);
            reserved.Clear();
            RestoreCleared(s);
        }

        /// <summary>What was cleared for the house stands again (a bare scar where no house stands). Once only.</summary>
        public static void RestoreCleared(Sanctuary s)
        {
            if (s == null || s.restored) return;
            s.restored = true;
            var v = MapInfo.Instance != null ? MapInfo.Instance.vegetation : null;
            if (v != null) foreach (int i in s.plantsCleared) v.Restore(i);
            foreach (var b in s.bouldersCleared) if (b != null) b.Restore();
        }

        /// <summary>Takes the plants and rocks off the house's ground: the patch the ground test walks (the plan
        /// with 3 m round it and the gates' aprons), each with its blocking radius and a unit's pad, so no Rubble
        /// lies under its walls or its grounds' NavMesh and no crown stands through its roof.</summary>
        static void ClearGround(MapInfo info, Sanctuary s)
        {
            float hx = Sanctuary.HalfX + 3f, front = Sanctuary.HalfZ + 4f, back = Sanctuary.HalfZ + 3f;
            float Off(Vector2 p)
            {
                Vector2 l = s.Local(p);
                float dx = Mathf.Max(0f, Mathf.Abs(l.x) - hx), dz = Mathf.Max(0f, Mathf.Max(l.y - back, -front - l.y));
                return Mathf.Sqrt(dx * dx + dz * dz);
            }
            var v = info.vegetation;
            if (v != null && v.live.Length == v.plants.Length)
                for (int i = 0; i < v.plants.Length; i++)
                {
                    var k = v.KindOf(i);
                    float sc = v.plants[i].scale;
                    float r = Mathf.Max(0.5f, k.blockRadius * sc, k.bush ? 0.6f * sc : 0f, k.HasCrown ? 0.8f * Mathf.Max(k.crownRadii.x, k.crownRadii.z) * sc : 0f);
                    if (Off(new Vector2(v.plants[i].pos.x, v.plants[i].pos.z)) >= r + UnitPad) continue;
                    if (v.live[i].state == PlantState.Gone) continue;
                    v.Clear(i);
                    s.plantsCleared.Add(i);
                }
            if (info.boulderRoot != null)
                foreach (var b in info.boulderRoot.GetComponentsInChildren<Boulder>())
                    if (!b.smashed && Off(b.Pos) < b.Radius + UnitPad) { b.Smash(); s.bouldersCleared.Add(b); }
        }

        static void Build(MapInfo info, Sanctuary s, CoverPiece piece, float lowest, List<CoverWall> runs)
        {
            var root = CoverRoot(info, false);
            var go = Instantiate(piece.prefab, root);
            go.name = HouseName;
            var rot = Quaternion.Euler(0f, s.yawDeg, 0f);
            go.transform.SetPositionAndRotation(new Vector3(s.centre.x, lowest - 0.06f, s.centre.y), rot);
            Get<NavMeshModifier>(go).ignoreFromBuild = true;
            s.piece = go;
            // Not Walkable under every run of wall and the hall, with the walls' pad (no centre nearer a face).
            float pad = WallPad;
            for (int i = 0; i < piece.segments.Length; i++)
            {
                var sg = piece.segments[i];
                Vector2 la = new Vector2(sg.x, sg.y), lb = new Vector2(sg.z, sg.w), d = lb - la;
                // The dividing wall (along Z at X 0) is thinner than the outer wall (build_env.py).
                float thick = Mathf.Abs(sg.x) < 1e-3f && Mathf.Abs(sg.z) < 1e-3f ? Sanctuary.Thick * 0.8f : Sanctuary.Thick;
                Volume(go, "Wall", 1, (la + lb) * 0.5f, -Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, new Vector2(d.magnitude + 2f * pad, thick + 2f * pad));
                Vector3 wa = go.transform.TransformPoint(new Vector3(la.x, 0f, la.y)), wb = go.transform.TransformPoint(new Vector3(lb.x, 0f, lb.y));
                float hgt = i < piece.heights.Length ? piece.heights[i] : 2.6f;
                runs.Add(new CoverWall
                {
                    a = new Vector2(wa.x, wa.z), b = new Vector2(wb.x, wb.z), half = Sanctuary.Thick * 0.5f,
                    height = hgt, piece = go, cluster = -1, top = go.transform.position.y + hgt,
                });
            }
            float hx = Sanctuary.HalfX, hz = Sanctuary.HalfZ, t = Sanctuary.Thick * 0.5f;
            // The hall, solid from its facade to the back wall's band.
            float hallFront = Sanctuary.HallZ - 0.2f - pad, back = hz - t - pad;
            Volume(go, "Hall", 1, new Vector2(0f, (hallFront + back) * 0.5f), 0f, new Vector2(2f * (hx - t - pad), back - hallFront));
            // The grounds (area 6): the two courts and each gate with its apron, none overlapping a wall's volume.
            float inX = hx - t - pad, inZ0 = -hz + t + pad, divider = Sanctuary.Thick * 0.4f + pad;
            Volume(go, "Court0", SanctuaryArea, new Vector2(-(inX + divider) * 0.5f, (inZ0 + hallFront) * 0.5f), 0f, new Vector2(inX - divider, hallFront - inZ0));
            Volume(go, "Court1", SanctuaryArea, new Vector2((inX + divider) * 0.5f, (inZ0 + hallFront) * 0.5f), 0f, new Vector2(inX - divider, hallFront - inZ0));
            // Each gate: its opening (between the walls' pads), and an apron before it 7 m wide, so no vehicle's hull
            // (a Mauler's 1.6 m) stands over the opening: a Mauler sent in parked beside a 1.8 m apron and closed the gate.
            float gateIn = Sanctuary.GateWidth * 0.5f - pad - 0.02f, apron = -hz - Sanctuary.Apron, wallOut = -hz - t - pad;
            foreach (float gx in Sanctuary.GateX)
            {
                Volume(go, "Gate", SanctuaryArea, new Vector2(gx, (wallOut + inZ0) * 0.5f), 0f, new Vector2(2f * gateIn, inZ0 - wallOut));
                Volume(go, "Apron", SanctuaryArea, new Vector2(gx, (apron + wallOut) * 0.5f), 0f, new Vector2(Sanctuary.ApronWidth, wallOut - apron));
            }
        }

        static void Volume(GameObject parent, string name, int area, Vector2 at, float yawDeg, Vector2 size)
        {
            var v = new GameObject(name);
            v.transform.SetParent(parent.transform, false);
            v.transform.localPosition = new Vector3(at.x, 0f, at.y);
            v.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
            var vol = v.AddComponent<NavMeshModifierVolume>();
            vol.area = area;
            vol.center = new Vector3(0f, 1.5f, 0f);
            vol.size = new Vector3(size.x, 6f, size.y);
        }

        /// <summary>The ruined twin cloisters (lore section 4: the twin houses the Clearance emptied): 0-2 a map
        /// (40% none, 40% one, 20% two), drawn on their own stream; each takes the place of one plain ruin.</summary>
        public static int CloistersDrawn(uint mapSeed)
        {
            uint h = NightRules.Hash(mapSeed ^ CloisterSalt) % 10u;
            return h < 4u ? 0 : h < 8u ? 1 : 2;
        }
    }
}
