// TacticalCoverTrials.cs -- the checks for the new combat's map cover (phase 5): the walls and ruins a
// match places, what they stop, and a man behind one. Called from a fresh Play at the menu, like TacticalTrials.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class TacticalCover
    {
        static string Stage<T>(string name) where T : BugTrial
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<T>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject(name + "Trial").AddComponent<T>();
            return $"staging; call {name}Report in about a minute";
        }

        static string Report<T>() where T : BugTrial
        {
            var r = Object.FindAnyObjectByType<T>();
            return r == null ? "no trial running" : r.report;
        }

        /// <summary>The map's cover: how much, the bases still joined to each other and to ore, no wall a
        /// ground unit stands in, and what a wall stops. Target: 8+ clusters, 0 cut, 0 open middles, a round
        /// at a crouching man behind one stopped, one over it at a standing man's gun not, sight over it at a
        /// standing man's eye clear and under it not.</summary>
        public static string Map() => Stage<CoverMapTrial>("Map");
        public static string MapReport() => Report<CoverMapTrial>();

        /// <summary>Across a low wall: a soldier sent 3 m past it vaults it (vaultAtT moves) where that is his shorter way, and gets there;
        /// the vehicles' way round is longer; and a ditch: a man lying in it has its rim between him and a rifle
        /// 20 m off. Target: there on every wall tried, vaulted wherever the vehicles' way is the longer, the ditch
        /// stopping the round.</summary>
        public static string Vault() => Stage<CoverVaultTrial>("Vault");
        public static string VaultReport() => Report<CoverVaultTrial>();

        /// <summary>Pictures (Temp/cover_look_*.png): a ruin close and at mid zoom, men behind a wall line before
        /// and during a fight, a man in a ditch.</summary>
        public static string Look() => Stage<CoverLookTrial>("Look");
        public static string LookReport() => Report<CoverLookTrial>();

        /// <summary>Rounds (DecideShot, the real path) at a man on a low wall's cover spot, crouched in cover and
        /// ducked, from a standing rifle 10, 20 and 30 m off across it; the same man up over it; the same man on
        /// the shooter's side of it (the control); and lines at a man standing in a wall's own slab, from both
        /// faces and both ends. Target: ducked near 0, up and the control well above, every slab line open.</summary>
        public static string Shots() => Stage<CoverShotsTrial>("Shots");
        public static string ShotsReport() => Report<CoverShotsTrial>();

        /// <summary>A Mauler sent through a low wall's middle, a soldier crouched on the wall's band beside where it
        /// crosses. Target: every run of the piece smashed, the piece gone, the tank there, rounds and the ground
        /// NavMesh through the gap afterwards, the soldier unhurt; 0 exceptions.</summary>
        public static string Crush() => Stage<CoverCrushTrial>("Crush");
        public static string CrushReport() => Report<CoverCrushTrial>();
    }

    public sealed class CoverShotsTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;
        const int Rounds = 300;

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            float[] ranges = { 10f, 20f, 30f };
            var ducked = new int[3]; var up = new int[3]; var open = new int[3]; var tried = new int[3];
            int spots = 0, low = 0, slabMen = 0, side = 0;
            var slabLines = new int[2]; var slabOpen = new int[2];
            int frontLines = 0, frontOpen = 0, bothOpen = 0, frontRounds = 0, frontStruck = 0;
            var slabRounds = new int[2]; var slabStruck = new int[2];
            var runs = new List<CoverWall>();
            foreach (var w in world.Walls)
                if (!w.smashed && w.height <= MapGenerator.VaultHigh && (w.b - w.a).magnitude >= 2f) runs.Add(w);
            foreach (var w in runs)
            {
                if (spots >= 10) break;
                Vector2 ab = w.b - w.a;
                float len = ab.magnitude;
                Vector2 n = new Vector2(-ab.y, ab.x) / len * ((side++ & 1) == 0 ? 1f : -1f);
                float off = w.half + MapGenerator.WallPad + 0.08f;
                Vector2 spot = w.Mid + n * off;
                if (w.top - world.Sight.GroundAt(spot.x, spot.y) < MapGenerator.DuckedClear) { low++; continue; }
                var man = Soldier(0, spot);
                man.noMind = true;
                yield return null;
                if ((man.pos - spot).magnitude > 0.3f) { Remove(man); continue; }
                spots++;
                for (int r = 0; r < ranges.Length; r++)
                {
                    Vector2 from = w.Mid - n * (off + ranges[r]);
                    if (!map.InBounds(from, 4f)) continue;
                    var gun = Soldier(1, from);
                    gun.noMind = true;
                    gun.posture = Posture.Stand;
                    yield return null;
                    if ((gun.pos - from).magnitude > 1.5f) { Remove(gun); continue; }
                    tried[r] += Rounds;
                    // Ducked behind it.
                    Warp(man, spot);
                    man.mode = SoldierMode.InCover; man.coverAt = spot; man.leanSide = GameWorld.RiseOver; man.risen = false; man.posture = Posture.Crouch;
                    ducked[r] += world.ProbeShots(gun, man, Rounds);
                    // Up over it for his round.
                    man.risen = true; man.posture = Posture.Stand;
                    up[r] += world.ProbeShots(gun, man, Rounds);
                    // The control: crouched in the open on the shooter's side, the wall behind him.
                    man.risen = false; man.posture = Posture.Crouch; man.mode = SoldierMode.Free; man.leanSide = 0;
                    Warp(man, w.Mid - n * off);
                    yield return null;
                    open[r] += world.ProbeShots(gun, man, Rounds);
                    Warp(man, spot);
                    Remove(gun);
                    yield return null;
                }
                // In the wall's own slab: lines from both faces and both ends, crouched and prone.
                Warp(man, w.Mid);
                yield return null;
                if ((man.pos - w.Mid).magnitude <= w.half)
                {
                    slabMen++;
                    Vector2 u = ab / len;
                    var ats = new[] { w.Mid + n * 12f, w.Mid - n * 12f, w.b + u * 12f, w.a - u * 12f };
                    for (int k = 0; k < ats.Length; k++)
                    {
                        var at = ats[k];
                        int by = k < 2 ? 0 : 1;
                        if (!map.InBounds(at, 4f)) continue;
                        var g = Soldier(1, at);
                        g.noMind = true; g.posture = Posture.Stand;
                        yield return null;
                        bool[] inSlab = new bool[2];
                        var ps = new[] { Posture.Crouch, Posture.Prone };
                        for (int q = 0; q < 2; q++)
                        {
                            man.posture = ps[q];
                            slabLines[by]++;
                            inSlab[q] = world.LineOfFire(g, man, out _);
                            if (inSlab[q]) slabOpen[by]++;
                            slabRounds[by] += 100;
                            slabStruck[by] += world.ProbeShots(g, man, 100);
                        }
                        if (by == 0)
                        {
                            // The control: the same man just in front of the wall, on the shooter's side.
                            Vector2 toward = (at - w.Mid).normalized;
                            Warp(man, w.Mid + toward * (w.half + 0.45f));
                            yield return null;
                            for (int q = 0; q < 2; q++)
                            {
                                man.posture = ps[q];
                                frontLines++;
                                bool f = world.LineOfFire(g, man, out _);
                                if (f) frontOpen++;
                                frontRounds += 100;
                                frontStruck += world.ProbeShots(g, man, 100);
                                if (f && inSlab[q]) bothOpen++;
                            }
                            Warp(man, w.Mid);
                            yield return null;
                        }
                        Remove(g);
                    }
                }
                Remove(man);
                yield return null;
            }
            sb.AppendLine($"{runs.Count} low runs, {spots} cover spots tried ({low} where the wall stands too low over the spot, skipped as the cover search skips them)");
            for (int r = 0; r < ranges.Length; r++)
                sb.AppendLine($"  {ranges[r]:0} m: ducked behind it struck {Pct(ducked[r], tried[r])}, up over it {Pct(up[r], tried[r])}, in front of it (control) {Pct(open[r], tried[r])}");
            sb.AppendLine($"  a man in a wall's slab ({slabMen} put there): lines open from its faces {slabOpen[0]} of {slabLines[0]}, from along it {slabOpen[1]} of {slabLines[1]} (a piece's other runs and the ground can stand in those); the control, the same man just in front of it, open {frontOpen} of {frontLines}, open both in it and in front {bothOpen}; rounds struck him in it from its faces {Pct(slabStruck[0], slabRounds[0])}, from along it {Pct(slabStruck[1], slabRounds[1])}, in front of it {Pct(frontStruck, frontRounds)}");
            report = sb.ToString();
            ReleaseStatics();
            ClearSpawned();
        }

        static string Pct(int a, int b) => b > 0 ? $"{100f * a / b:0.0}% of {b}" : "none tried";

        void Warp(Unit u, Vector2 p) { if (u.agent != null) u.agent.Warp(map.Ground(p)); }
    }

    public sealed class CoverCrushTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            int tried = 0;
            foreach (var w in new List<CoverWall>(world.Walls))
            {
                if (tried >= 3) break;
                if (w.smashed || w.height > MapGenerator.VaultHigh || (w.b - w.a).magnitude < 2.5f) continue;
                Vector2 ab = w.b - w.a, along = ab.normalized, n = new Vector2(-along.y, along.x);
                Vector2 from = w.Mid + n * 9f, to = w.Mid - n * 9f;
                if (!UnityEngine.AI.NavMesh.SamplePosition(map.Ground(from), out _, 0.5f, GameWorld.GroundAreas) ||
                    !UnityEngine.AI.NavMesh.SamplePosition(map.Ground(to), out _, 0.5f, GameWorld.GroundAreas)) continue;
                tried++;
                Vector3 lowA = map.Ground(w.Mid + n * 3f) + Vector3.up * 0.6f, lowB = map.Ground(w.Mid - n * 3f) + Vector3.up * 0.6f;
                bool stoppedBefore = world.Sight.FirstStop(lowA, lowB, null, world.boulders) < 1f;
                // The tank's friend: the question is the crush, not the tank's gun.
                var man = Soldier(0, w.Mid + along * 1.1f);
                man.noMind = true;
                man.posture = Posture.Crouch;
                float hpMan = man.hp;
                var tank = Put(UnitType.Mauler, 0, from);
                tank.hp = 1e6f;
                yield return null;
                world.CmdMove(new List<Unit> { tank }, to, false);
                float t0 = world.time, crushedAt = -1f;
                while (world.time - t0 < 25f && (tank.pos - to).magnitude > 1.5f)
                {
                    if (crushedAt < 0f && w.smashed) crushedAt = world.time - t0;
                    yield return null;
                }
                if (crushedAt < 0f && w.smashed) crushedAt = world.time - t0;
                // The NavMesh rebuild (at most every 1.5 s, then in the background) and the sight map's refresh (2 s).
                float t1 = world.time;
                bool groundNow = false;
                while (world.time - t1 < 10f)
                {
                    groundNow = UnityEngine.AI.NavMesh.SamplePosition(map.Ground(w.Mid), out var hm, 0.3f, GameWorld.GroundAreas) &&
                                new Vector2(hm.position.x - w.Mid.x, hm.position.z - w.Mid.y).magnitude < 0.3f;
                    if (groundNow && world.time - t1 >= 2.5f) break;
                    yield return null;
                }
                float rebuiltIn = world.time - t1;
                int pieceRuns = 0, pieceSmashed = 0;
                foreach (var o in world.Walls) if (o.piece == w.piece) { pieceRuns++; if (o.smashed) pieceSmashed++; }
                bool goneView = w.piece == null || !w.piece.activeSelf;
                bool stoppedAfter = world.Sight.FirstStop(lowA, lowB, null, world.boulders) < 1f;
                sb.AppendLine($"  wall {tried} ({w.piece?.name}): {(crushedAt >= 0f ? $"smashed after {crushedAt:0.0} s" : "never smashed")}, {pieceSmashed} of {pieceRuns} runs of the piece, piece {(goneView ? "gone" : "still shown")}, the tank {(tank.pos - to).magnitude:0.0} m from its goal after {world.time - t0:0.0} s; a low round through it stopped before {stoppedBefore}, after {stoppedAfter}; ground NavMesh at its middle {(groundNow ? $"after {rebuiltIn:0.0} s" : "not in 10 s")}; the man on its band {(Unit.Live(man) ? $"alive, {hpMan - man.hp:0} hp lost" : "dead")}");
                Remove(tank);
                Remove(man);
                yield return null;
            }
            report = tried == 0 ? "no low wall with open ground 9 m either side" : sb.ToString();
            ReleaseStatics();
            ClearSpawned();
        }
    }

    public sealed class CoverMapTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            var walls = world.Walls;
            sb.AppendLine($"  map {map.seed}: {world.CoverPlaced}; {walls.Count} runs of wall (target 8+ clusters)");
            bool cut = MapGenerator.CoverCut(map, out var where);
            sb.AppendLine($"  a base cut off from the other or from ore: {cut}{(cut ? $" at {where}" : "")} (target False)");
            int open = 0, lowStopped = 0, overClear = 0, eyeClear = 0, underBlocked = 0, tried = 0;
            int mask = GameWorld.GroundAreas;
            foreach (var w in walls)
            {
                var mid = map.Ground(w.Mid);
                if (UnityEngine.AI.NavMesh.SamplePosition(mid, out var hm, 0.2f, mask) && new Vector2(hm.position.x - w.Mid.x, hm.position.z - w.Mid.y).magnitude < 0.2f) open++;
                if (w.height > 1.2f || (w.b - w.a).magnitude < 1f) continue;
                tried++;
                Vector2 ab = (w.b - w.a).normalized, n = new Vector2(-ab.y, ab.x);
                Vector2 near = w.Mid - n * (w.half + MapGenerator.WallPad + 0.1f), far = w.Mid + n * 20f;
                float gNear = map.HeightAt(near), gFar = map.HeightAt(far);
                // A rifle 20 m off at a crouching man's middle behind it; then at a standing man's chest over it.
                if (world.Sight.FirstStop(new Vector3(far.x, gFar + 1.25f, far.y), new Vector3(near.x, gNear + 0.6f, near.y), null, world.boulders) < 1f) lowStopped++;
                if (world.Sight.FirstStop(new Vector3(near.x, gNear + 1.25f, near.y), new Vector3(far.x, gFar + 1.25f, far.y), null, world.boulders) >= 1f) overClear++;
                if (world.Sight.Clear(new Vector3(far.x, gFar + 1.7f, far.y), new Vector3(near.x, gNear + 1.7f, near.y))) eyeClear++;
                if (!world.Sight.Clear(new Vector3(far.x, gFar + 0.5f, far.y), new Vector3(near.x, gNear + 0.5f, near.y))) underBlocked++;
            }
            sb.AppendLine($"  wall middles a ground unit stands on: {open} (target 0)");
            sb.AppendLine($"  {tried} low walls: a round at a crouching man behind stopped {lowStopped}, one from a standing gun over it clear {overClear}, sight at a standing eye clear {eyeClear}, at 0.5 m blocked {underBlocked} (targets all {tried})");
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class CoverVaultTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            var rng = new System.Random(808);
            int tried = 0, vaulted = 0, there = 0, longerRound = 0;
            var path = new UnityEngine.AI.NavMeshPath();
            for (int tries = 0; tries < 400 && tried < 4; tries++)
            {
                var walls = world.Walls;
                if (walls.Count == 0) break;
                var w = walls[rng.Next(walls.Count)];
                if (w.smashed || w.height > MapGenerator.VaultHigh || (w.b - w.a).magnitude < 2.5f) continue;
                Vector2 ab = (w.b - w.a).normalized, n = new Vector2(-ab.y, ab.x);
                Vector2 from = w.Mid - n * 3f, to = w.Mid + n * 3f;
                if (!world.Walkable(from, 0.4f) || !world.Walkable(to, 0.4f)) continue;
                tried++;
                // The vehicles' way and a soldier's.
                float LenOf(int mask)
                {
                    if (!UnityEngine.AI.NavMesh.CalculatePath(map.Ground(from), map.Ground(to), mask, path) || path.status != UnityEngine.AI.NavMeshPathStatus.PathComplete) return float.PositiveInfinity;
                    float l = 0f;
                    for (int i = 1; i < path.corners.Length; i++) l += (path.corners[i] - path.corners[i - 1]).magnitude;
                    return l;
                }
                float ground = LenOf(GameWorld.GroundAreas), foot = LenOf(GameWorld.FootAreas);
                if (ground > foot + 1f) longerRound++;
                var man = Put(UnitType.Trooper, 0, from);
                man.hp = 1e6f;
                yield return Wait(0.5f);
                float was = man.vaultAtT;
                world.CmdMove(new List<Unit> { man }, to, false);
                float t0 = world.time;
                while (world.time - t0 < 12f && (man.pos - to).magnitude > 1.2f) yield return null;
                bool v = man.vaultAtT != was, got = (man.pos - to).magnitude <= 1.5f;
                if (v) vaulted++;
                if (got) there++;
                sb.AppendLine($"  wall {tried}: {(v ? "vaulted" : "did not vault")}, {(got ? "there" : "not there")} after {world.time - t0:0.0} s; a soldier's way {foot:0.0} m, the vehicles' {(float.IsInfinity(ground) ? "none" : ground.ToString("0.0") + " m")}");
                ClearSpawned();
                yield return Wait(0.5f);
            }
            sb.Insert(0, $"  {world.CoverPlaced}\n  {tried} low walls: vaulted {vaulted}, got there {there}, the vehicles' way longer {longerRound} (targets all {tried})\n");
            // A ditch: a man lying on its bottom, a rifle 20 m off on the level beyond its edge.
            int ditchTried = 0, ditchStopped = 0, ditchOpenStopped = 0;
            foreach (var d in world.Ditches)
            {
                Vector2 dab = (d.b - d.a).normalized, dn = new Vector2(-dab.y, dab.x);
                Vector2 mid = (d.a + d.b) * 0.5f, far = mid + dn * 22f;
                if (!world.Walkable(far, 0.5f)) continue;
                ditchTried++;
                Vector3 gun = map.Ground(far) + Vector3.up * 1.25f;
                if (world.Sight.FirstStop(gun, map.Ground(mid) + Vector3.up * 0.3f, null, world.boulders) < 1f) ditchStopped++;
                // The same lying man on the level, for comparison.
                var level = mid + dab * ((d.b - d.a).magnitude * 0.5f + 6f);
                if (world.Sight.FirstStop(gun, map.Ground(level) + Vector3.up * 0.3f, null, world.boulders) < 1f) ditchOpenStopped++;
            }
            sb.AppendLine($"  {world.Ditches.Count} ditches, {ditchTried} tried: a round at a man lying on the bottom stopped {ditchStopped} (target {ditchTried}), at one lying on the level beside it {ditchOpenStopped}; ground in the ditch {(world.Ditches.Count > 0 ? (map.HeightAt((world.Ditches[0].a + world.Ditches[0].b) * 0.5f) - map.HeightAt((world.Ditches[0].a + world.Ditches[0].b) * 0.5f + new Vector2(-(world.Ditches[0].b - world.Ditches[0].a).normalized.y, (world.Ditches[0].b - world.Ditches[0].a).normalized.x) * 4f)).ToString("0.00") : "-")} m against 4 m off it");
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class CoverLookTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;

        IEnumerator Shot(StarForge.View.RTSCamera cam, Vector2 at, float zoom, float secs, string file)
        {
            for (float t = 0f; t < secs; t += Time.unscaledDeltaTime) { cam.JumpTo(at); cam.ZoomTo(zoom); yield return null; }
            if (file != null) ScreenCapture.CaptureScreenshot("Temp/cover_look_" + file + ".png", 2);
            yield return null;
        }

        Vector2 ClusterMiddle(int cluster)
        {
            Vector2 sum = Vector2.zero;
            int n = 0;
            foreach (var w in world.Walls) if (w.cluster == cluster) { sum += w.Mid; n++; }
            return n > 0 ? sum / n : Vector2.zero;
        }

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            var cam = Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            if (cam == null) { report = "no camera"; ReleaseStatics(); yield break; }
            int ruin = -1, line = -1;
            foreach (var w in world.Walls)
            {
                if (w.piece == null) continue;
                if (ruin < 0 && w.piece.name.StartsWith("RUIN_CORNER")) ruin = w.cluster;
                if (line < 0 && w.piece.name.StartsWith("WALL_LOW")) line = w.cluster;
            }
            if (ruin >= 0)
            {
                var c = ClusterMiddle(ruin);
                // Someone of ours there, or the ground is in the fog.
                var seer = Put(UnitType.Trooper, 0, world.NearestWalkable(c + new Vector2(0f, -6f), 6f));
                seer.hp = 1e6f;
                yield return Shot(cam, c, 16f, 2.5f, "ruin_close");
                yield return Shot(cam, c, 30f, 1f, "ruin_mid");
                sb.AppendLine($"  ruin at {c}");
                ClearSpawned();
            }
            if (line >= 0)
            {
                // Four men behind it, three rifles coming at it from its far side.
                CoverWall w0 = null;
                foreach (var w in world.Walls) if (w.cluster == line) { w0 = w; break; }
                Vector2 ab = (w0.b - w0.a).normalized, n = new Vector2(-ab.y, ab.x);
                var c = ClusterMiddle(line);
                for (int i = 0; i < 4; i++) { var u = Put(UnitType.Trooper, 0, c - n * 2.5f + ab * ((i - 1.5f) * 2.2f)); u.hp = 1e6f; }
                for (int i = 0; i < 3; i++) { var e = Put(UnitType.Trooper, 1, c + n * 30f + ab * ((i - 1f) * 3f)); e.hp = 1e6f; world.CmdMove(new List<Unit> { e }, c + n * 18f, true); }
                yield return Shot(cam, c, 18f, 1f, "wall_before");
                yield return Shot(cam, c, 22f, 9f, "wall_fight");
                yield return Shot(cam, c, 16f, 0.8f, "wall_fight_close");
                sb.AppendLine($"  wall line at {c}");
                ClearSpawned();
            }
            if (world.Ditches.Count > 0)
            {
                var d = world.Ditches[0];
                var mid = (d.a + d.b) * 0.5f;
                var man = Put(UnitType.Trooper, 0, mid);
                man.hp = 1e6f;
                yield return Shot(cam, mid, 20f, 2f, "ditch");
                sb.AppendLine($"  ditch at {mid}");
                ClearSpawned();
            }
            sb.AppendLine("  pictures in Temp/cover_look_*.png");
            report = sb.ToString();
            ReleaseStatics();
        }
    }
}
