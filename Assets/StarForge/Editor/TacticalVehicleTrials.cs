// TacticalVehicleTrials.cs -- the checks for the new combat's tanks (phase 6): the mounted machine gun and the main
// gun on separate targets, smoke popped against anti-tank fire and topped up at a depot, shells held off friends.
// Called from a fresh Play at the menu, like TacticalTrials.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class TacticalVehicles
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

        /// <summary>A Mauler against four riflemen and a Lancer 18-24 m off, three fields, 15 s each. Target: 80%+ of
        /// the machine gun's rounds at the men and 80%+ of the main gun's shells at the Lancer.</summary>
        public static string Guns() => Stage<TankGunsTrial>("Guns");
        public static string GunsReport() => Report<TankGunsTrial>();

        /// <summary>A Mauler with two Rocketeers 26 m off, five fields: seconds from the first rocket to its smoke, charges
        /// after; then beside a Workshop, its charges back. Target: smoke within 2 s 5 of 5, 2 charges then none, both back.</summary>
        public static string Smoke() => Stage<TankSmokeTrial>("Smoke");
        public static string SmokeReport() => Report<TankSmokeTrial>();

        /// <summary>A Mauler on an enemy rifleman 20 m off with a friend 2 m from him, then the friend walked off. Target: no
        /// shell and no friend hurt while he is by the man, shells once he is clear.</summary>
        public static string Friends() => Stage<TankFriendsTrial>("Friends");
        public static string FriendsReport() => Report<TankFriendsTrial>();

        /// <summary>An idle Mauler, two Rocketeers sent at it from 45 m, 25 s, five fields, with its tactics and without
        /// (`TacticalRules.NoTankTactics`). Target: it keeps out of their reach more of the time and takes fewer rockets,
        /// 4 of 5 fields.</summary>
        public static string Backoff() => Stage<TankBackoffTrial>("Backoff");
        public static string BackoffReport() => Report<TankBackoffTrial>();

        /// <summary>An idle Mauler, then a lone Sentinel, three riflemen sent at it from 40 m: does it answer them, and
        /// how soon? Target: the first round or shell back within 10 s of the first rifle round, in every field (the
        /// riflemen close in on their attack order, so this reads a tank's or tower's own reach, not an answer from
        /// beyond it: an idle tank out-reached at its edge still does not answer, see the World notes).</summary>
        public static string Outranged() => Stage<OutrangedTrial>("Outranged");
        public static string OutrangedReport() => Report<OutrangedTrial>();
        /// <summary>A Digger of ours at its work, an enemy rifleman put 12 m off, then taken away; and a Skitter at 25%
        /// attacking a rifleman. Target: the Digger runs (8+ m off him) and is back at its work, its order kept, 5 of 5;
        /// the Skitter breaks off (8+ m) and comes back.</summary>
        public static string Instinct() => Stage<InstinctTrial>("Instinct");
        /// <summary>A Lancer and a Skimmer firing at a rifleman ducked behind a low wall, and at the same man in front of
        /// it (12 s each, up to four walls). Target: behind the wall a fifth of the hull lost in front, or less.</summary>
        public static string CoverRounds() => Stage<CoverRoundsTrial>("CoverRounds");
        public static string CoverRoundsReport() => Report<CoverRoundsTrial>();
        public static string InstinctReport() => Report<InstinctTrial>();
        /// <summary>F7's probe: a Mauler on Hold, four riflemen ducked behind a low wall 22 m off (known through a spotter
        /// of its side; no round's way to them) and three in the open 13-14 m off, placed so the four come first in the
        /// spatial grid's order, and a rifleman of its side beside it; nobody fires. 20 looks over 10 s a field, up to
        /// three walls: the tank's own machine-gun target and the rifleman's (kept while a round reaches him, as FireBlocked does) read. Target with the budget spent best first: no look
        /// without a target while an open man is seen, and the pick an open man (the rifleman's the nearest). Before:
        /// up to every look without one.</summary>
        public static string LineBudget() { LineBudgetTrial.Mode = 0; return Stage<LineBudgetTrial>("LineBudget"); }
        /// <summary>LineBudget with the four behind the wall lying flat, crowd-pushing off: no line reaches them, so the
        /// bug hunt's silent machine gun is what it measures.</summary>
        public static string LineBudgetFlat() { LineBudgetTrial.Mode = 1; return Stage<LineBudgetTrial>("LineBudget"); }
        /// <summary>The mirror (the Fable review's case): the four lie flat behind the wall 11 m from the tank and the
        /// three stand in the open 20 m out the far side, first in the grid's order. A budget spent nearest first and
        /// stopping after four would find none; spent in grid order it finds them.</summary>
        public static string LineBudgetNear() { LineBudgetTrial.Mode = 2; return Stage<LineBudgetTrial>("LineBudget"); }
        /// <summary>The mirror's field with the rifleman hiding, his target the nearest open man, and an enemy Digger with
        /// a line 8 m from him (the final Fable review's case): his target must not change while he hides (target 0
        /// changes, 0 frames on the Digger).</summary>
        public static string LineBudgetHide() { LineBudgetTrial.Mode = 3; return Stage<LineBudgetTrial>("LineBudget"); }
        /// <summary>The map LineBudget's before and after runs use (53 low walls; map 1000 has no wall the staging fits).</summary>
        public static string UseLineBudgetSeed() { StarForge.Game.MatchSettings.mapSeed = 3469857940u; return "next Play on map 3469857940 (and every Play after it: TacticalTrials.ClearSeed frees it)"; }
        public static string LineBudgetReport() => Report<LineBudgetTrial>();
    }

    public sealed class LineBudgetTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;
        /// <summary>0 the four ducked, 1 lying flat, 2 lying flat and nearer than the open three.</summary>
        public static int Mode;
        static bool Flat => Mode >= 1;

        // A point's place in the spatial grid's order (GameWorld: 32 cells a side, walked row by row).
        int CellOrder(Vector2 p)
        {
            float c = world.MapSize / 32f;
            return Mathf.Clamp((int)(p.y / c), 0, 31) * 32 + Mathf.Clamp((int)(p.x / c), 0, 31);
        }

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            report = "running";
            var sb = new StringBuilder();
            HoldStatics();
            int fields = 0, looks = 0, openSeenLooks = 0, gunNone = 0, gunWall = 0, gunOpen = 0;
            int rifleNone = 0, rifleWall = 0, rifleOpen = 0, rifleNotNearest = 0;
            int nShort = 0, nClear = 0, nRoom = 0, nOrder = 0, nWalls = 0;
            foreach (var w in new List<CoverWall>(world.Walls))
            {
                if (fields >= 3) break;
                if (w.smashed || w.height > MapGenerator.VaultHigh) continue;
                nWalls++;
                if ((w.b - w.a).magnitude < 2.2f) { nShort++; continue; }
                Vector2 ab = (w.b - w.a).normalized;
                float off = w.half + MapGenerator.WallPad + 0.08f;
                foreach (float side in new[] { 1f, -1f })
                {
                    // n points from the wall to the men behind it; the tank stands 22 m out the other way, the men in
                    // the open 13 m beyond it: nearer the tank, and later in the grid's order.
                    Vector2 n = new Vector2(-ab.y, ab.x) * side;
                    var behindAt = new List<Vector2>();
                    foreach (float t in new[] { -0.9f, -0.3f, 0.3f, 0.9f }) behindAt.Add(w.Mid + n * off + ab * t);
                    float toTank = Mode >= 2 ? 11f : 22f, beyond = Mode >= 2 ? 20f : 13f;
                    Vector2 tankAt = w.Mid - n * (off + toTank), rifleAt = tankAt + ab * 3f, spotAt = w.Mid + n * off + ab * 7f;
                    var openAt = new List<Vector2>();
                    foreach (float t in new[] { -4f, 0f, 4f }) openAt.Add(tankAt - n * beyond + ab * t);
                    bool clear = true, room = map.InBounds(tankAt, 6f) && world.Walkable(tankAt, 2f) && world.Walkable(rifleAt, 1f) &&
                              map.InBounds(spotAt, 4f) && world.Walkable(spotAt, 1f);
                    foreach (var p in behindAt) clear &= w.top - world.Sight.GroundAt(p.x, p.y) >= MapGenerator.DuckedClear;
                    foreach (var p in openAt) room &= map.InBounds(p, 4f) && world.Walkable(p, 1f);
                    Vector2 diggerAt = rifleAt + ab * 8f;
                    if (Mode == 3) room &= map.InBounds(diggerAt, 4f) && world.Walkable(diggerAt, 1.5f);
                    int lastBehind = 0, firstOpen = int.MaxValue, firstBehind = int.MaxValue, lastOpen = 0;
                    foreach (var p in behindAt) { lastBehind = Mathf.Max(lastBehind, CellOrder(p)); firstBehind = Mathf.Min(firstBehind, CellOrder(p)); }
                    foreach (var p in openAt) { firstOpen = Mathf.Min(firstOpen, CellOrder(p)); lastOpen = Mathf.Max(lastOpen, CellOrder(p)); }
                    if (!clear) { nClear++; continue; }
                    if (!room) { nRoom++; continue; }
                    if (Mode >= 2 ? lastOpen >= firstBehind : firstOpen <= lastBehind) { nOrder++; continue; }

                    // The open men, the tank, the rifleman and the spotter first, and a control: with nobody behind the
                    // wall, both must take an open man (a field where the tank's roof or the rifle has no way to them
                    // says nothing about the budget, and is dropped).
                    var open = new List<Unit>();
                    foreach (var p in openAt) { var m = Soldier(0, p); m.noMind = true; open.Add(m); }
                    var tank = Put(UnitType.Mauler, 1, tankAt);
                    tank.hp = 1e6f;
                    world.CmdHold(new List<Unit> { tank });
                    var rifle = Soldier(1, rifleAt);
                    rifle.noMind = true;
                    var spotter = Soldier(1, spotAt);
                    spotter.noMind = true;
                    var all = new List<Unit>(open);
                    all.Add(rifle);
                    all.Add(spotter);
                    // Nobody fires (the tank's machine gun has its own reload and may: every man is at 1e6 hp).
                    float t0 = world.time;
                    while (world.time - t0 < 2f) { foreach (var m in all) m.cooldown = 99f; tank.cooldown = 99f; yield return null; }
                    int controlGun = 0, controlRifle = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        float tc = world.time;
                        while (world.time - tc < 0.5f) { foreach (var m in all) m.cooldown = 99f; tank.cooldown = 99f; yield return null; }
                        if (open.Contains(world.TankGunTarget(tank))) controlGun++;
                        if (open.Contains(world.NearestShootable(rifle, false))) controlRifle++;
                    }
                    if (controlGun == 0 || controlRifle == 0)
                    {
                        sb.AppendLine($"  a field dropped: with nobody behind the wall the gun took an open man {controlGun} of 3, the rifleman {controlRifle} of 3");
                        ClearSpawned();
                        yield return null;
                        continue;
                    }
                    fields++;

                    // Then the four behind the wall (other cells than the open men's, so the grid's order holds).
                    var behind = new List<Unit>();
                    foreach (var p in behindAt)
                    {
                        var m = Soldier(0, p);
                        m.noMind = true;
                        m.mode = SoldierMode.InCover; m.coverAt = p; m.leanSide = GameWorld.RiseOver; m.posture = Flat ? Posture.Prone : Posture.Crouch;
                        if (Flat && m.agent != null) m.agent.obstacleAvoidanceType = UnityEngine.AI.ObstacleAvoidanceType.NoObstacleAvoidance;
                        behind.Add(m);
                    }
                    all.AddRange(behind);
                    t0 = world.time;
                    while (world.time - t0 < 2f)
                    {
                        foreach (var m in all) m.cooldown = 99f;
                        tank.cooldown = 99f;
                        if (Flat) foreach (var m in behind) m.posture = Posture.Prone;
                        yield return null;
                    }

                    int linesBehind = 0, linesOpen = 0, fGunNone = 0, fRifleNone = 0, fLooks = 0;
                    float seenBehind = 0f;
                    foreach (var m in behind) if (world.LineOfFire(rifle, m)) linesBehind++;
                    foreach (var m in open) if (world.LineOfFire(rifle, m)) linesOpen++;
                    Unit nearestOpen = null;
                    float nd = float.MaxValue;
                    foreach (var m in open) { float d = (m.pos - rifle.pos).magnitude; if (d < nd) { nd = d; nearestOpen = m; } }
                    Unit rifleTarget = null, digger = null;
                    if (Mode == 3)
                    {
                        digger = Put(UnitType.Worker, 0, diggerAt);
                        digger.hp = 1e6f;
                        world.CmdHold(new List<Unit> { digger });
                        rifle.target = nearestOpen;
                        rifle.targetOrdered = false;
                    }
                    int swaps = 0, onDigger = 0, hideFrames = 0;
                    Unit lastTarget = rifle.target;
                    for (int k = 0; k < 20; k++)
                    {
                        float tl = world.time;
                        while (world.time - tl < 0.5f)
                        {
                            foreach (var m in all) m.cooldown = 99f;
                            tank.cooldown = 99f;
                            if (Flat) foreach (var m in behind) m.posture = Posture.Prone;
                            if (Mode == 3)
                            {
                                rifle.hiding = true;
                                hideFrames++;
                                if (rifle.target != lastTarget) { swaps++; lastTarget = rifle.target; }
                                if (rifle.target == digger) onDigger++;
                            }
                            yield return null;
                        }
                        bool openSeen = false;
                        foreach (var m in open) openSeen |= world.Sees(1, m);
                        foreach (var m in behind) if (world.Sees(1, m)) seenBehind++;
                        looks++;
                        fLooks++;
                        if (openSeen) openSeenLooks++;
                        var g = tank.tankGunTarget;   // its own look, as its machine gun uses it
                        if (g == null) { if (openSeen) { gunNone++; fGunNone++; } }
                        else if (behind.Contains(g)) gunWall++;
                        else if (open.Contains(g)) gunOpen++;
                        // Hiding, the rifleman's own looks are what is measured: no raw query to move his place in the list.
                        if (Mode != 3 && (rifleTarget == null || rifleTarget.dying || !world.LineOfFire(rifle, rifleTarget))) rifleTarget = world.NearestShootable(rifle, false);
                        var r = Mode == 3 ? rifle.target : rifleTarget;
                        if (r == null) { if (openSeen) { rifleNone++; fRifleNone++; } }
                        else if (behind.Contains(r)) rifleWall++;
                        else if (open.Contains(r)) { rifleOpen++; if (r != nearestOpen) rifleNotNearest++; }
                    }
                    float drift = 0f;
                    for (int i = 0; i < behind.Count; i++) drift = Mathf.Max(drift, (behind[i].pos - behindAt[i]).magnitude);
                    if (Mode == 3) sb.AppendLine($"  field {fields}: hiding, his target changed {swaps} times in {hideFrames} frames, on the Digger {onDigger} frames (target 0 and 0)");
                    sb.AppendLine($"  field {fields}: control (nobody behind) gun {controlGun} of 3, rifleman {controlRifle} of 3 on an open man; grid order behind {firstBehind}-{lastBehind}, open {firstOpen}-{lastOpen}; the rifleman's lines behind {linesBehind} of 4, open {linesOpen} of 3; " +
                        $"behind seen {seenBehind / Mathf.Max(1, fLooks):0.0} of 4 a look, moved off their places up to {drift:0.0} m; no target while an open man was seen: gun {fGunNone}, rifleman {fRifleNone} of {fLooks}");
                    ClearSpawned();
                    yield return null;
                    break;
                }
            }
            string tried = $"low walls {nWalls}: under 2.2 m {nShort}; sides by side, a man's place without the wall's clear rise {nClear}, no room for the tank and the men {nRoom}, the grid's order the wrong way {nOrder}";
            string how = Mode == 3 ? "the mirror with the rifleman hiding, a Digger 8 m from him" : Mode == 2 ? "the four lying flat behind the wall nearer than the open three" : Flat ? "the four behind lying flat" : "the four behind crouched (they get a line: they lean to look at the spotter)";
            report = fields == 0 ? "LineBudget: no low wall on this map fits the staging (" + tried + ")\n" + sb :
                $"LineBudget, {how} ({tried}), {fields} fields, {looks} looks (target with the budget spent best first: none without a target while an open man is seen, " +
                $"the pick an open man; before, up to every look): an open man seen in {openSeenLooks}; the machine gun none {gunNone}, " +
                $"a man behind the wall {gunWall}, an open man {gunOpen}; the rifleman none {rifleNone}, behind the wall {rifleWall}, an open man {rifleOpen} " +
                $"({rifleNotNearest} not the nearest)\n" + sb;
            ReleaseStatics();
        }
    }

    public sealed class TankGunsTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;
        Unit tank;
        int mgAtMen, mgOther, mainAtTank, mainOther;

        void OnFire(GameEvent e)
        {
            if (e.kind != GameEventKind.Fire || e.unit != tank || tank == null) return;
            if (e.projectileKind == GameWorld.ProjMG)
            {
                var t = tank.tankGunTarget;
                if (t != null && t.def.bodyRadius > 0f) mgAtMen++; else mgOther++;
            }
            else
            {
                var t = tank.target;
                if (t != null && t.Type == UnitType.Lancer) mainAtTank++; else mainOther++;
            }
        }

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            ListenWhileHeld(OnFire);
            var rng = new System.Random(606);
            int fields = 0;
            for (int f = 0; f < 3; f++)
            {
                if (!FindRange(rng, 30f, out var a, out var d)) continue;
                fields++;
                Vector2 side = new Vector2(-d.y, d.x);
                tank = Put(UnitType.Mauler, 0, a);
                tank.hp = 1e6f;
                var men = new List<Unit>();
                for (int i = 0; i < 4; i++) { var m = Soldier(1, a + d * 18f + side * ((i - 1.5f) * 2.5f)); men.Add(m); }
                var lancer = Put(UnitType.Lancer, 1, a + d * 24f + side * 6f);
                lancer.hp = 1e6f;
                // It only stands as the main gun's mark: since the range pass (Lancer 30 m) its fire made the Mauler
                // smoke itself in and lose sight of everything, which is the Smoke trial's business, not this one's.
                lancer.cooldown = 1e9f;
                world.CmdHold(new List<Unit> { lancer });
                int mg0 = mgAtMen + mgOther, main0 = mainAtTank + mainOther;
                float t0 = world.time;
                while (world.time - t0 < 15f) yield return null;
                sb.AppendLine($"  field {f}: machine gun {mgAtMen + mgOther - mg0} rounds, main gun {mainAtTank + mainOther - main0} shells; men alive {men.FindAll(Unit.Live).Count} of 4");
                ClearSpawned();
                tank = null;
                yield return null;
            }
            int mg = mgAtMen + mgOther, main = mainAtTank + mainOther;
            report = $"{fields} fields: machine gun at men {Pct(mgAtMen, mg)} of {mg} rounds, main gun at the Lancer {Pct(mainAtTank, main)} of {main} shells (targets 80%+ each)\n" + sb;
            ReleaseStatics();
        }

        static string Pct(int a, int b) => b > 0 ? $"{100f * a / b:0}%" : "-";
    }

    public sealed class TankSmokeTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;
        Unit tank;
        float firstRocketAt = -1f;

        void OnFire(GameEvent e)
        {
            if (e.kind == GameEventKind.Fire && e.unit != null && e.unit.team == 1 && e.unit.Type == UnitType.Rocketeer && firstRocketAt < 0f && world != null)
                firstRocketAt = world.time;
        }

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            ListenWhileHeld(OnFire);
            var rng = new System.Random(707);
            int fields = 0, inTime = 0;
            for (int f = 0; f < 5; f++)
            {
                if (!FindRange(rng, 30f, out var a, out var d)) continue;
                fields++;
                tank = Put(UnitType.Mauler, 0, a);
                tank.hp = 1e6f;
                world.CmdHold(new List<Unit> { tank });
                for (int i = 0; i < 2; i++)
                {
                    var r = Put(UnitType.Rocketeer, 1, a + d * 26f + new Vector2(-d.y, d.x) * (i * 3f - 1.5f));
                    r.hp = 1e6f;
                    world.CmdAttack(new List<Unit> { r }, tank);
                }
                firstRocketAt = -1f;
                int popped0 = world.TankSmokesPopped;
                float t0 = world.time, smokeAt = -1f;
                while (world.time - t0 < 20f)
                {
                    if (smokeAt < 0f && world.TankSmokesPopped > popped0) smokeAt = world.time;
                    yield return null;
                }
                float lag = smokeAt >= 0f && firstRocketAt >= 0f ? smokeAt - firstRocketAt : -1f;
                if (lag >= 0f && lag <= 2f) inTime++;
                int left = tank.tankSmokes;
                // Beside a Workshop of its own: its charges come back.
                var shop = Put(UnitType.Workshop, 0, a - d * 12f);
                float t1 = world.time;
                while (world.time - t1 < 25f && tank.tankSmokes < TacticalRules.TankSmokes) yield return null;
                sb.AppendLine($"  field {f}: first rocket at {(firstRocketAt >= 0f ? (firstRocketAt - t0).ToString("0.0") : "-")} s, smoke {(lag >= 0f ? lag.ToString("0.0") + " s after it" : "never")}, charges left {left}, popped {world.TankSmokesPopped - popped0}; by a Workshop back to {tank.tankSmokes} in {world.time - t1:0} s");
                ClearSpawned();
                tank = null;
                yield return null;
            }
            report = $"{fields} fields: smoke within 2 s of the first rocket {inTime} of {fields} (target 5 of 5)\n" + sb;
            ReleaseStatics();
        }

    }

    public sealed class TankBackoffTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;
        int rocketHits;
        Unit tank;

        void OnFire(GameEvent e) { }

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            int better = 0, fields = 0;
            for (int f = 0; f < 5; f++)
            {
                var res = new float[2, 2];
                bool ok = true;
                for (int pass = 0; pass < 2; pass++)
                {
                    var rng = new System.Random(900 + f);
                    if (!FindRange(rng, 40f, out var a, out var d)) { ok = false; break; }
                    TacticalRules.NoTankTactics = pass == 1;
                    tank = Put(UnitType.Mauler, 0, a);
                    float hp0 = tank.hp = 2000f;
                    var men = new List<Unit>();
                    for (int i = 0; i < 2; i++)
                    {
                        var r = Put(UnitType.Rocketeer, 1, a + d * 45f + new Vector2(-d.y, d.x) * (i * 4f - 2f));
                        r.hp = 1e6f;
                        men.Add(r);
                    }
                    world.CmdAttack(men, tank);
                    float t0 = world.time, inside = 0f, samples = 0f;
                    while (world.time - t0 < 25f && Unit.Live(tank))
                    {
                        float near = float.MaxValue;
                        foreach (var m in men) near = Mathf.Min(near, (m.pos - tank.pos).magnitude);
                        samples += 1f;
                        if (near <= men[0].def.range) inside += 1f;
                        yield return null;
                    }
                    res[pass, 0] = samples > 0f ? inside / samples : 1f;
                    res[pass, 1] = hp0 - tank.hp;
                    ClearSpawned();
                    yield return null;
                }
                TacticalRules.NoTankTactics = false;
                if (!ok) continue;
                fields++;
                if (res[0, 0] < res[1, 0] && res[0, 1] <= res[1, 1]) better++;
                sb.AppendLine($"  field {f}: in their reach {res[0, 0]:P0} of the time with tactics, {res[1, 0]:P0} without; hull lost {res[0, 1]:0} against {res[1, 1]:0}");
            }
            TacticalRules.NoTankTactics = false;
            report = $"{fields} fields: better with its tactics on {better} of {fields} (target 4 of 5); back-offs {world.TankBackOffs}\n" + sb;
            ReleaseStatics();
        }
    }

    public sealed class OutrangedTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;
        Unit guarded;
        float firstIn = -1f, firstBack = -1f, t0;
        int back;

        void OnFire(GameEvent e)
        {
            if (e.kind != GameEventKind.Fire || e.unit == null || guarded == null) return;
            if (e.unit == guarded) { back++; if (firstBack < 0f) firstBack = world.time - t0; }
            else if (e.unit.team == 1 && e.unit.target == guarded && firstIn < 0f) firstIn = world.time - t0;
        }

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            ListenWhileHeld(OnFire);
            var rng = new System.Random(4040);
            int fields = 0, answered = 0;
            foreach (var type in new[] { UnitType.Mauler, UnitType.Mauler, UnitType.Mauler, UnitType.Sentinel, UnitType.Sentinel })
            {
                if (!FindRange(rng, 44f, out var a, out var d)) continue;
                fields++;
                Vector2 side = new Vector2(-d.y, d.x);
                guarded = Put(type, 0, a);
                float hp0 = guarded.hp = type == UnitType.Sentinel ? guarded.hp : 2000f;
                var men = new List<Unit>();
                for (int i = 0; i < 3; i++) { var m = Soldier(1, a + d * 40f + side * ((i - 1f) * 3f)); m.hp = 1e6f; men.Add(m); }
                world.CmdAttack(men, guarded);
                t0 = world.time; firstIn = firstBack = -1f; back = 0;
                float nearest = float.MaxValue;
                while (world.time - t0 < 30f && Unit.Live(guarded))
                {
                    foreach (var m in men) nearest = Mathf.Min(nearest, (m.pos - guarded.pos).magnitude);
                    yield return null;
                }
                bool ok = firstIn >= 0f && firstBack >= 0f && firstBack - firstIn <= 10f;
                if (ok) answered++;
                sb.AppendLine($"  {type}: first rifle round at {(firstIn < 0f ? "never" : firstIn.ToString("0.0") + " s")}, first answer at {(firstBack < 0f ? "never" : firstBack.ToString("0.0") + " s")}, {back} rounds and shells back, riflemen nearest {nearest:0.0} m, hull lost {hp0 - (Unit.Live(guarded) ? guarded.hp : 0f):0}, order {(Unit.Live(guarded) ? guarded.order.ToString() : "down")}");
                ClearSpawned();
                guarded = null;
                yield return null;
            }
            report = $"{fields} fields: answered within 10 s of the first rifle round in {answered} (target all)\n" + sb;
            ReleaseStatics();
        }
    }

    public sealed class InstinctTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            var rng = new System.Random(1111);
            int ran = 0, back = 0, tried = 0, homeTried = 0;
            // Away from home first (by its Foundry a Digger works on, phase 8): one put to ore 35 m+ from every
            // Foundry of ours and 45 m+ from theirs, a rifleman of theirs 12 m off.
            string remote = "no ore 35 m from our Foundries";
            bool remoteRan = false, remoteBack = false;
            foreach (var n in new List<Unit>(world.units))
            {
                if (n == null || n.dying || n.Type != UnitType.Ore) continue;
                bool ok = true;
                foreach (var f in world.units)
                    if (f != null && !f.dying && f.Type == UnitType.Foundry && (f.pos - n.pos).magnitude < (f.team == 0 ? 35f : 45f)) { ok = false; break; }
                if (!ok) continue;
                var dig = Put(UnitType.Worker, 0, world.NearestWalkable(n.pos + new Vector2(3f, 0f), 6f));
                world.CmdHarvest(new List<Unit> { dig }, n);
                yield return Wait(4f);
                Vector2 at0 = dig.pos;
                var foe = Soldier(1, dig.pos + Random2(rng) * 12f);
                foe.noMind = true;
                int fl0 = world.WorkerFlights;
                float tr = world.time, farR = 0f;
                while (world.time - tr < 5f && Unit.Live(dig)) { farR = Mathf.Max(farR, (dig.pos - foe.pos).magnitude); yield return null; }
                Remove(foe);
                float tb = world.time, nearR = float.MaxValue;
                while (world.time - tb < 20f && Unit.Live(dig)) { nearR = Mathf.Min(nearR, (dig.pos - at0).magnitude); yield return null; }
                remoteRan = world.WorkerFlights > fl0;
                remoteBack = Unit.Live(dig) && nearR < 5f && (dig.order == Order.Harvest || dig.order == Order.Return);
                remote = $"flights {world.WorkerFlights - fl0}, {farR:0.0} m from the rifleman at most (12 at first), back within {nearR:0.0} m of where it worked, order {(Unit.Live(dig) ? dig.order.ToString() : "dead")}";
                if (Unit.Live(dig)) Remove(dig);
                break;
            }
            foreach (var u in new List<Unit>(world.units))
            {
                if (tried >= 5) break;
                if (u == null || u.dying || u.team != 0 || u.Type != UnitType.Worker || u.order != Order.Harvest) continue;
                tried++;
                Vector2 start = u.pos;
                Vector2 dir = Random2(rng);
                var foe = Soldier(1, u.pos + dir * 12f);
                foe.noMind = true;
                float t0 = world.time, far = 0f;
                while (world.time - t0 < 5f) { far = Mathf.Max(far, (u.pos - foe.pos).magnitude); yield return null; }
                Remove(foe);
                float t1 = world.time, nearest = float.MaxValue;
                while (world.time - t1 < 20f) { nearest = Mathf.Min(nearest, (u.pos - start).magnitude); yield return null; }
                bool r = far >= 20f, b = nearest < 5f && (u.order == Order.Harvest || u.order == Order.Return);
                // By its own Foundry it works on (phase 8's rule, TacticalRules.WorkerStaysNear): not a flight to expect.
                var depot = world.NearestDropoff(u);
                bool home = depot != null && (depot.pos - start).magnitude < TacticalRules.WorkerStaysNear;
                if (home) homeTried++;
                else if (r) ran++;
                if (b) back++;
                sb.AppendLine($"  Digger {tried}: {far:0.0} m from the rifleman at most (12 at first), back within {nearest:0.0} m of where it worked, order {u.order}{(home ? ", within 25 m of its Foundry (works on by rule)" : "")}");
            }
            // A creature at a quarter of its hull.
            int broke = 0, returned = 0, fields = 0;
            for (int f = 0; f < 3; f++)
            {
                if (!FindRange(rng, 30f, out var a, out var d)) continue;
                fields++;
                var man = Soldier(0, a);
                man.noMind = true;
                var sk = Put(UnitType.Skitter, 1, a + d * 10f);
                sk.hp = sk.MaxHp * 0.25f;
                world.CmdAttack(new List<Unit> { sk }, man);
                float t0 = world.time, far = 0f, closeAgain = float.MaxValue;
                while (world.time - t0 < 25f && Unit.Live(sk))
                {
                    man.cooldown = 99f;
                    float dd = (sk.pos - man.pos).magnitude;
                    far = Mathf.Max(far, dd);
                    if (world.time - t0 > 7f) closeAgain = Mathf.Min(closeAgain, dd);
                    yield return null;
                }
                if (far >= 18f) broke++;
                if (closeAgain < 4f) returned++;
                sb.AppendLine($"  Skitter {f}: {far:0.0} m off at most (10 at first), {(closeAgain < float.MaxValue ? closeAgain.ToString("0.0") : "-")} m at its nearest after 7 s");
                ClearSpawned();
                yield return null;
            }
            report = $"Away from home a Digger {(remoteRan ? "ran" : "did NOT run")} and {(remoteBack ? "went back to work" : "did NOT go back")} ({remote}); match Diggers: ran {ran} of {tried - homeTried} away from their Foundry ({homeTried} within 25 m of it, which work on by rule), back at work {back} of {tried} (target all); creatures: broke off {broke} of {fields}, came back {returned} of {fields}; flights {world.WorkerFlights} workers, {world.CreatureFlights} creatures\n" + sb;
            ReleaseStatics();
        }

        static Vector2 Random2(System.Random r)
        {
            float a = (float)r.NextDouble() * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
    }

    public sealed class CoverRoundsTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            float behind = 0f, front = 0f;
            int walls = 0;
            foreach (var w in new List<CoverWall>(world.Walls))
            {
                if (walls >= 4) break;
                if (w.smashed || w.height > MapGenerator.VaultHigh || (w.b - w.a).magnitude < 2.5f) continue;
                Vector2 ab = w.b - w.a, n = new Vector2(-ab.y, ab.x).normalized;
                float off = w.half + MapGenerator.WallPad + 0.08f;
                Vector2 spot = w.Mid + n * off;
                if (w.top - world.Sight.GroundAt(spot.x, spot.y) < MapGenerator.DuckedClear) continue;
                Vector2 gunAt = w.Mid - n * (off + 15f);
                if (!map.InBounds(gunAt, 4f)) continue;
                walls++;
                var lost = new float[2];
                for (int pass = 0; pass < 2; pass++)
                {
                    Vector2 at = pass == 0 ? spot : w.Mid - n * off;
                    var man = Soldier(0, at);
                    man.noMind = true;
                    man.mode = SoldierMode.InCover; man.coverAt = at; man.leanSide = pass == 0 ? GameWorld.RiseOver : 0; man.posture = Posture.Crouch;
                    float hp0 = man.hp;
                    // Held where they stand (on an attack order they drove round the wall's end): the Lancer 15 m off, the Skimmer 10.
                    var guns = new List<Unit> { Put(UnitType.Lancer, 1, gunAt), Put(UnitType.Skimmer, 1, w.Mid - n * (off + 10f) + new Vector2(-n.y, n.x) * 2f) };
                    foreach (var g in guns) g.hp = 1e6f;
                    world.CmdHold(guns);
                    // A spotter of theirs beside him, holding his fire: their side knows where he is.
                    var spot2 = Soldier(1, at + ab.normalized * 7f);
                    spot2.noMind = true;
                    float t0 = world.time;
                    while (world.time - t0 < 12f) { man.cooldown = 99f; spot2.cooldown = 99f; yield return null; }
                    lost[pass] = hp0 - man.hp;
                    ClearSpawned();
                    yield return null;
                }
                behind += lost[0];
                front += lost[1];
                sb.AppendLine($"  wall {walls}: lost behind it {lost[0]:0}, in front of it {lost[1]:0}");
            }
            report = $"{walls} walls: a Lancer and a Skimmer took {behind:0} hull off a man behind a wall against {front:0} in front (target a fifth or less); rounds stopped by cover {world.RoundsStoppedByCover}\n" + sb;
            ReleaseStatics();
        }
    }

    public sealed class TankFriendsTrial : TacticalTrial
    {
        protected override bool RulesOnly => true;
        Unit tank;
        int shells;

        void OnFire(GameEvent e)
        {
            if (e.kind == GameEventKind.Fire && e.unit == tank && tank != null && e.projectileKind != GameWorld.ProjMG) shells++;
        }

        IEnumerator Start()
        {
            yield return TacticalMatch();
            if (!Ready) { if (report == "staging") report = "no match running"; yield break; }
            var sb = new StringBuilder();
            HoldStatics();
            ListenWhileHeld(OnFire);
            var rng = new System.Random(808);
            int fields = 0;
            for (int f = 0; f < 3; f++)
            {
                if (!FindRange(rng, 30f, out var a, out var d)) continue;
                fields++;
                tank = Put(UnitType.Mauler, 0, a);
                tank.hp = 1e6f;
                var foe = Soldier(1, a + d * 20f);
                foe.noMind = true;
                var friend = Soldier(0, a + d * 20f + new Vector2(-d.y, d.x) * 2f);
                friend.noMind = true;
                friend.hp = 1e6f;
                world.CmdAttack(new List<Unit> { tank }, foe);
                int hurt0 = world.FriendHurt;
                shells = 0;
                float t0 = world.time;
                while (world.time - t0 < 10f) yield return null;
                int near = shells, hurtNear = world.FriendHurt - hurt0;
                friend.agent.Warp(map.Ground(a + d * 20f + new Vector2(-d.y, d.x) * 14f));
                shells = 0;
                float t1 = world.time;
                while (world.time - t1 < 10f && Unit.Live(foe)) yield return null;
                sb.AppendLine($"  field {f}: friend 2 m from the man: {near} shells, {hurtNear} friends hurt; friend 14 m off: {shells} shells, the man {(Unit.Live(foe) ? "alive" : "dead")}");
                ClearSpawned();
                tank = null;
                yield return null;
            }
            report = $"{fields} fields\n" + sb;
            ReleaseStatics();
        }

    }
}
