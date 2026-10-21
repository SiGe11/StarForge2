// MoraleTrials.cs — staged checks for morale (World/GameWorld.Morale.cs), run in play mode:
//   Tools/editor.sh call StarForge.EditorTools.MoraleTrials.CampSeeds    (answers at once; reloads the scene)
//   Tools/editor.sh call StarForge.EditorTools.MoraleTrials.Desert       (then DesertReport, ~2 min)
//   Tools/editor.sh call StarForge.EditorTools.MoraleTrials.Punish       (then PunishReport, ~2 min)
// The staged ones silence the opponent AI while they run (Commander.Suspended) and
// switch it back when they end; morale's own breaking is off then (GameWorld.moraleOn),
// so they force the desertion with GameWorld.Desert.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.AI;
using StarForge.Game;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class MoraleTrials
    {
        /// <summary>Regenerates the map (and NavMesh) on 40 seeds -- GroveBatch's list -- and
        /// places both sides' deserters' camp on each: how far from its base (aim 40-85 m),
        /// whether a soldier can walk there from the base, and how near the nearest ore is.
        /// A frame or two between maps: the NavMesh islands refresh only then (asked in one
        /// frame, all 40 read the first map's). Reloads the scene at the end.</summary>
        public static string CampSeeds()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<CampSeedsTrial>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject("CampSeedsTrial").AddComponent<CampSeedsTrial>();
            return "staging; call CampSeedsReport in about a minute";
        }
        public static string CampSeedsReport()
        {
            var r = Object.FindAnyObjectByType<CampSeedsTrial>();
            return r != null ? r.report : CampSeedsTrial.last ?? "no trial running";
        }

        /// <summary>Eight soldiers of team 0 in the field; four are made to desert. Reports:
        /// supply freed, the time to reach the camp, whether a move order moves them, shots
        /// at an enemy Digger parked by the camp (none wanted: they do not start fights), then
        /// three enemy Troopers attack the camp -- shots back, how far from the camp they
        /// went (leash 14 m), alarms raised for the side (none wanted).</summary>
        public static string Desert() => Stage<DesertTrial>("Desert");
        public static string DesertReport() => Report<DesertTrial>();

        /// <summary>Team 0's Mech and four deserters in the camp, two loyal Troopers holding
        /// beside it; the Mech is made to punish them. Reports what it said, how long it took,
        /// deserters left, damage to the loyal ones (none wanted), kills credited to the enemy
        /// (none) and alarms raised (none), and that the punishment ended.</summary>
        public static string Punish() => Stage<PunishTrial>("Punish");
        public static string PunishReport() => Report<PunishTrial>();

        /// <summary>The same with a Mech that mounts a mortar (it cannot fire inside 10 m), a
        /// railgun or a laser -- each in a fresh match (a team that already has its Mech keeps it).</summary>
        public static string PunishMortar() => StagePunish(MechWeapon.Mortar);
        public static string PunishRailgun() => StagePunish(MechWeapon.Railgun);
        public static string PunishLaser() => StagePunish(MechWeapon.Laser);

        /// <summary>The opponent's side (team 1) punishing its own deserters: same checks; its Mech
        /// says nothing (only the player's Mech talks), so no comms line is wanted.</summary>
        public static string PunishEnemy()
        {
            string r = Stage<PunishTrial>("Punish", out var t);
            if (t != null) t.side = 1;
            return r;
        }

        /// <summary>The Mech at 50% hull when they run: it decides and says so at once, keeps its
        /// guns off them while hurt, and goes once mended.</summary>
        public static string PunishHurt()
        {
            string r = Stage<PunishTrial>("Punish", out var t);
            if (t != null) t.hurtFirst = true;
            return r;
        }

        /// <summary>The Mech sent to its bay at 25% as they run: it decides in the gantry, keeps its
        /// guns off them while it mends, and goes once mended.</summary>
        public static string PunishRepairing()
        {
            string r = Stage<PunishTrial>("Punish", out var t);
            if (t != null) t.repairFirst = true;
            return r;
        }

        /// <summary>The Mech destroyed once its first deserter is down: the punishment must end with
        /// it (purge off, no errors) and the rest of the deserters stay.</summary>
        public static string PunishMechDies()
        {
            string r = Stage<PunishTrial>("Punish", out var t);
            if (t != null) t.killMech = true;
            return r;
        }

        /// <summary>Two enemy Troopers go at the Mech once it has set off: it must turn back saying so
        /// once (not every time it is hit), keep its guns off the deserters while it fights, and go
        /// again once free.</summary>
        public static string PunishInterrupted()
        {
            string r = Stage<PunishTrial>("Punish", out var t);
            if (t != null) t.interrupt = true;
            return r;
        }

        /// <summary>The deserters killed by someone else while the Mech, hurt, has decided to go but
        /// cannot: it must say the enemy got there first, and end the punishment, though hurt.</summary>
        public static string PunishGone()
        {
            string r = Stage<PunishTrial>("Punish", out var t);
            if (t != null) t.goneFirst = true;
            return r;
        }

        /// <summary>The Mech on the field when they run, and nobody forcing it: its own verdict must come
        /// 4-9 s after the break, unforced, and be said.</summary>
        public static string PunishNatural()
        {
            string r = Stage<PunishTrial>("Punish", out var t);
            if (t != null) t.natural = true;
            return r;
        }

        /// <summary>The Mech still coming down when they run: it must give no verdict on them, then or
        /// once landed, and the record must say it was not on the field.</summary>
        public static string PunishInbound()
        {
            string r = Stage<PunishTrial>("Punish", out var t);
            if (t != null) t.inbound = true;
            return r;
        }

        /// <summary>The Mech at 50% hull with a bay at home and nothing near: too hurt to go (60%+),
        /// above its repair line. It must top up at the bay and then put them down, not wait on them
        /// all match.</summary>
        public static string PunishTopUp()
        {
            string r = Stage<PunishTrial>("Punish", out var t);
            if (t != null) { t.repairFirst = true; t.repairHp = 0.5f; }
            return r;
        }

        /// <summary>PunishTopUp with an enemy hitting the Mech every 2 s from 45 m for 24 s (beyond the
        /// 34 m its repair rule looks at, as a mortar or railgun Mech would): it must not walk off to
        /// top up while hit, and must top up and go once the hits stop.</summary>
        public static string PunishTopUpUnderFire()
        {
            string r = Stage<PunishTrial>("Punish", out var t);
            if (t != null) { t.repairFirst = true; t.repairHp = 0.5f; t.underFire = true; }
            return r;
        }

        /// <summary>The Mech at 50% hull with no bay to mend at: never free to go. The "go" must end
        /// after GoLimit with a line saying so, the deserters untouched and the purge never on.</summary>
        public static string PunishNeverFree()
        {
            string r = Stage<PunishTrial>("Punish", out var t);
            if (t != null) t.neverFree = true;
            return r;
        }

        /// <summary>One deserter stranded where it broke (as one cut off from the camp's ground
        /// is), the rest in the camp far off; two enemy Troopers go at the stranded one. Wanted:
        /// it fires back and keeps near its spot; the camp, out of reach of the alarm, does not fire.</summary>
        public static string Stranded() => Stage<StrandedTrial>("Stranded");
        public static string StrandedReport() => Report<StrandedTrial>();

        /// <summary>Who breaks, and whether he gets clear: a knot of six -- three at its front under
        /// attack by six enemy rifles from 12 m, three at its rear 34 m from them -- breaks one
        /// (Desert, count 1), 20 times with the old pick and 20 with the new; each time, is the
        /// deserter from the rear, and alive 6 s later? Report with BreakPickReport.</summary>
        public static string BreakPick() => Stage<BreakPickTrial>("BreakPick");
        public static string BreakPickReport() => Report<BreakPickTrial>();

        /// <summary>The whole chain with nothing forced: a side losing its war, a fight going badly
        /// (both staged: their army holding far off, their rifles 27 m from ours, our dead and our
        /// soldiers' wounds credited to them), then its own morale reading, its own roll, the break
        /// and its Mech's own verdict. A crisis that rolls no break is let lapse (CrisisGap) and
        /// raised again, each with its own roll, up to eight.</summary>
        public static string NaturalChain() => Stage<NaturalChainTrial>("NaturalChain");
        public static string NaturalChainReport() => Report<NaturalChainTrial>();

        static string StagePunish(MechWeapon w)
        {
            // The new trial itself: a search could still find the one being replaced (Destroy waits for the frame's end).
            string r = Stage<PunishTrial>("Punish", out var t);
            if (t != null) t.mounting = w;
            return r;
        }

        /// <summary>Where each side's camp would go now, in the running match (chosen afresh,
        /// round whatever the side has built), then forgotten so a real desertion chooses again.</summary>
        public static string CampNow()
        {
            var world = GameWorld.Instance;
            if (!EditorApplication.isPlaying || world == null || !world.running) return "no match running";
            var sb = new StringBuilder($"t {world.time:0}s, map seed {MapInfo.Instance.seed}:");
            for (int t = 0; t < 2; t++)
            {
                var M = world.morale[t];
                if (M.deserted > 0) { sb.Append($" [{t}] has deserters, camp kept at ({M.camp.x:0},{M.camp.y:0});"); continue; }
                Vector2 c = world.DeserterCamp(t, fresh: true);
                int built = 0;
                foreach (var u in world.units) if (Unit.Live(u) && u.team == t && u.def.building) built++;
                sb.Append($" [{t}] ({c.x:0},{c.y:0}) {M.campFromHome:0} m from base, {M.campClearance:0} m clear of its {built} buildings;");
                M.campSet = false;
            }
            return sb.ToString();
        }

        static string Stage<T>(string name) where T : BugTrial => Stage<T>(name, out _);

        static string Stage<T>(string name, out T trial) where T : BugTrial
        {
            trial = null;
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<T>();
            if (old != null) Object.Destroy(old.gameObject);
            trial = new GameObject(name + "Trial").AddComponent<T>();
            return $"staging; call {name}Report in about two minutes";
        }

        static string Report<T>() where T : BugTrial
        {
            var r = Object.FindAnyObjectByType<T>();
            return r == null ? "no trial running" : r.report;
        }
    }

    public sealed class CampSeedsTrial : MonoBehaviour
    {
        public string report = "staging";
        /// <summary>The scene reloads at the end, taking this object with it: the report stays here.</summary>
        public static string last;

        IEnumerator Start()
        {
            last = null;
            var info = MapInfo.Instance;
            var world = GameWorld.Instance;
            var rt = info != null ? info.GetComponent<MapRuntime>() : null;
            if (rt == null || rt.kit == null || world == null) { report = "no map runtime"; yield break; }
            var seeds = new List<uint> { 1370439406, 4104226260, 3307429070, 986964719, 1252978921,
                                         2520572947, 862588067, 845192303, 1000 };
            var rng = new System.Random(20260929);
            while (seeds.Count < 40) seeds.Add((uint)rng.Next(1, int.MaxValue));
            var sb = new StringBuilder();
            int bad = 0, unreached = 0, wet = 0;
            float minD = float.MaxValue, maxD = 0f, sumD = 0f, minOre = float.MaxValue;
            foreach (var seed in seeds)
            {
                MapGenerator.Generate(info, rt.kit, seed, bakeNavMesh: true, sharedMaterials: false);
                // The islands rebuild at most every 2 s on their own: build them now, once the
                // new NavMesh has landed.
                for (int f = 0; f < 3; f++) yield return null;
                world.Islands?.Build();
                sb.Append($"\n{seed}:");
                for (int t = 0; t < 2; t++)
                {
                    Vector2 home = info.StartPos(t), camp = world.DeserterCamp(t, fresh: true);
                    float d = (camp - home).magnitude;
                    // Walked, not looked up: a full path on the ground areas from the base.
                    bool reach = false;
                    if (UnityEngine.AI.NavMesh.SamplePosition(info.Ground(home), out var a, 8f, GameWorld.GroundAreas) &&
                        UnityEngine.AI.NavMesh.SamplePosition(info.Ground(camp), out var b, 2f, GameWorld.GroundAreas))
                    {
                        var path = new UnityEngine.AI.NavMeshPath();
                        UnityEngine.AI.NavMesh.CalculatePath(a.position, b.position, GameWorld.GroundAreas, path);
                        reach = path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
                    }
                    float ore = float.MaxValue;
                    foreach (Transform o in info.oreRoot) ore = Mathf.Min(ore, (new Vector2(o.position.x, o.position.z) - camp).magnitude);
                    float depth = info.WaterDepth(camp);
                    sb.Append($" [{t}] ({camp.x:0},{camp.y:0}) {d:0} m, {(reach ? "walked" : "NOT WALKED")}, ore {ore:0} m{(depth > 0f ? $", wading {depth:0.0} m" : "")};");
                    if (!reach) unreached++;
                    if (d < GameWorld.CampNear - 0.5f || d > GameWorld.CampFar + 4f) bad++;
                    if (depth > 0.3f) wet++;
                    minD = Mathf.Min(minD, d); maxD = Mathf.Max(maxD, d); sumD += d;
                    minOre = Mathf.Min(minOre, ore);
                }
                report = $"{sb.Length} chars so far";
            }
            report = last = $"{seeds.Count} seeds, {seeds.Count * 2} camps: {unreached} not walked to from their base, {bad} outside {GameWorld.CampNear:0}-{GameWorld.CampFar:0} m, {wet} in water over 0.3 m; " +
                            $"distance from base {minD:0}-{maxD:0} m, mean {sumD / (seeds.Count * 2):0}; nearest ore to any camp {minOre:0} m" + sb;
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }
    }

    /// <summary>What the morale trials share: a squad of team 0 in the field, and counting
    /// what happens to it.</summary>
    public abstract class MoraleTrial : BugTrial
    {
        // The opponent silenced and the clock at 3x while any morale trial runs, put back when
        // the last one ends -- also when play stops or a trial is replaced mid-run (a
        // coroutine's finally does not run then). Counted across trials: each saving what the
        // other had set, two overlapping ones left Commander.Suspended on into the next match.
        static int holders;
        static bool savedSuspended;
        static float savedScale = 1f;
        bool holding;

        protected void Hold()
        {
            if (holding) return;
            holding = true;
            if (holders++ == 0) { savedSuspended = Commander.Suspended; savedScale = Time.timeScale; }
            Commander.Suspended = true;
            Time.timeScale = 3f;
        }

        protected void Release()
        {
            if (!holding) return;
            holding = false;
            if (--holders > 0) return;
            holders = 0;
            Commander.Suspended = savedSuspended;
            Time.timeScale = savedScale;
        }

        void OnDestroy() => Release();

        protected readonly List<Unit> squad = new List<Unit>();
        /// <summary>The side whose soldiers break (0 the player's, 1 the opponent's).</summary>
        public int side;
        protected int unprovoked, shotsBack, alarms, deserterAlarms, mechAlarms;
        protected bool attacked;
        protected readonly List<string> said = new List<string>();

        protected void Listen(GameEvent e)
        {
            if (e.kind == GameEventKind.Fire && e.unit != null && e.unit.deserted)
            {
                if (attacked) shotsBack++; else unprovoked++;
            }
            if (e.kind == GameEventKind.UnderAttack && e.team == side)
            {
                alarms++;
                if (e.unit != null && e.unit.deserted) deserterAlarms++;
                if (e.unit != null && e.unit.mech != null) mechAlarms++;
            }
            if (e.kind == GameEventKind.MechAdvice && e.team == side) said.Add($"{world.time:0}s {e.advice}: {e.text}");
            if (e.kind == GameEventKind.Desertion) said.Add($"{world.time:0}s DESERTION team {e.team}: {e.text} ({e.index})");
        }

        /// <summary>The side's soldiers on open ground a third of the way to the enemy.</summary>
        protected Vector2 SpawnSquad(int troopers, int maulers)
        {
            Vector2 home = map.StartPos(side), foe = map.StartPos(1 - side);
            // On ground joined to the base, as soldiers who walked out of it are (a plateau the
            // base's ground does not reach has no way to the camp).
            Vector2 at = world.NearestReachable(home, Vector2.Lerp(home, foe, 0.35f));
            for (int i = 0; i < troopers + maulers; i++)
            {
                float a = i * 2.39996f;
                var p = world.NearestReachable(home, at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (2f + 1.2f * Mathf.Sqrt(i)));
                squad.Add(world.Spawn(i < troopers ? UnitType.Trooper : UnitType.Mauler, side, p));
            }
            return at;
        }

        protected List<Unit> Deserters()
        {
            var d = new List<Unit>();
            foreach (var u in squad) if (Unit.Live(u) && u.deserted) d.Add(u);
            return d;
        }

        protected IEnumerator WaitInCamp(List<Unit> deserters, float limit)
        {
            float t0 = world.time;
            while (world.time - t0 < limit)
            {
                bool all = true;
                foreach (var u in deserters) if (Unit.Live(u) && u.fleeing) { all = false; break; }
                if (all) break;
                yield return null;
            }
        }

        protected float FarthestFromCamp(List<Unit> deserters)
        {
            Vector2 camp = world.morale[side].camp;
            float far = 0f;
            foreach (var u in deserters) if (Unit.Live(u)) far = Mathf.Max(far, (u.pos - camp).magnitude);
            return far;
        }
    }

    public sealed class DesertTrial : MoraleTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var sb = new StringBuilder();
            world.Event += Listen;
            var extras = new List<Unit>();
            try
            {
                Hold();
                if (world.time < 5f) yield return Wait(5f - world.time);
                Vector2 at = SpawnSquad(6, 2);
                yield return Wait(1f);
                var F = world.factions[0];
                int supplyBefore = F.supplyUsed;
                var rig = Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
                if (rig != null) rig.CenterOn(at);
                yield return new WaitForSecondsRealtime(1.5f);
                int n = world.Desert(0, at, squad, 4);
                // The warning is up for 8 s: photograph it (and the minimap's pulses) at once.
                yield return new WaitForSecondsRealtime(1.2f);
                ScreenCapture.CaptureScreenshot("Temp/morale_alert.png");
                var deserters = Deserters();
                Vector2 camp = world.morale[0].camp, home = map.StartPos(0);
                sb.AppendLine($"{n} deserted ({string.Join(", ", deserters.ConvertAll(u => u.def.displayName))}) of {squad.Count}; " +
                              $"camp ({camp.x:0},{camp.y:0}), {(camp - home).magnitude:0} m from base, {(camp - at).magnitude:0} m from the fight");
                yield return Wait(0.5f);
                sb.AppendLine($"supply {supplyBefore} -> {F.supplyUsed} (freed {supplyBefore - F.supplyUsed})");

                // An order to everyone: the loyal go, the deserters do not.
                Vector2 goal = world.NearestWalkable(Vector2.Lerp(at, home, 0.5f), 10f);
                world.CmdMove(squad, goal, false);
                yield return Wait(0.5f);
                int obeyed = 0;
                foreach (var u in deserters) if (Unit.Live(u) && (u.orderPos - goal).magnitude < 8f) obeyed++;
                sb.AppendLine($"move order to all: {obeyed} of {deserters.Count} deserters took it (want 0)");

                float t0 = world.time;
                yield return WaitInCamp(deserters, 120f);
                int still = 0;
                foreach (var u in deserters) if (Unit.Live(u) && u.fleeing) still++;
                sb.AppendLine($"in the camp after {world.time - t0:0} s ({still} still on the way); farthest {FarthestFromCamp(deserters):0.0} m from its middle");

                // An enemy Digger parked by the camp: they must not start a fight.
                // Holding: a Digger carries a pick (2 m), and an idle one walks up and swings it.
                var digger = world.Spawn(UnitType.Worker, 1, world.NearestWalkable(camp + Norm(home - camp) * 14f, 4f));
                world.CmdHold(new List<Unit> { digger });
                extras.Add(digger);
                if (rig != null) rig.CenterOn(camp);
                yield return Wait(3f);
                ScreenCapture.CaptureScreenshot("Temp/morale_camp.png");
                yield return Wait(20f);
                float far = FarthestFromCamp(deserters);
                sb.AppendLine($"enemy Digger holding {(digger.pos - camp).magnitude:0} m off for 20 s: {unprovoked} shots (want 0), Digger {(Unit.Live(digger) ? "alive" : "dead")}; farthest {far:0.0} m");

                // Then three enemy Troopers attack the camp, from along the map's edge (from the
                // base side they found the base's Diggers first).
                attacked = true;
                Vector2 from = camp;
                float bestAway = -1f;
                for (int k = 0; k < 16; k++)
                {
                    float ang = k * Mathf.PI / 8f;
                    Vector2 p = world.NearestReachable(camp, camp + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 22f);
                    float away = (p - home).magnitude;
                    if ((p - camp).magnitude > 16f && away > bestAway) { bestAway = away; from = p; }
                }
                var raiders = new List<Unit>();
                for (int i = 0; i < 3; i++)
                {
                    var r = world.Spawn(UnitType.Trooper, 1, world.NearestWalkable(from + new Vector2(i * 1.5f, 0f), 4f));
                    raiders.Add(r);
                    extras.Add(r);
                    // Each straight at a deserter: they are attacking the camp, nothing else.
                    var prey = deserters.Find(u => Unit.Live(u));
                    if (prey != null) world.CmdAttack(new List<Unit> { r }, deserters[i % deserters.Count]);
                }
                sb.AppendLine($"raiders from {(from - camp).magnitude:0} m off");
                float tA = world.time;
                far = 0f;
                while (world.time - tA < 40f)
                {
                    far = Mathf.Max(far, FarthestFromCamp(deserters));
                    yield return null;
                }
                int raidersLeft = 0, desertersLeft = 0;
                foreach (var r in raiders) if (Unit.Live(r)) raidersLeft++;
                foreach (var u in deserters) if (Unit.Live(u)) desertersLeft++;
                sb.AppendLine($"attacked by 3 Troopers: {shotsBack} shots back, farthest {far:0.0} m from the camp (leash {GameWorld.CampLeash:0} m + spread), " +
                              $"{desertersLeft} deserters and {raidersLeft} raiders left after 40 s");
                sb.AppendLine($"alarms for team 0: {alarms}, from deserters {deserterAlarms} (want 0); deserted on record {world.morale[0].deserted}");
                foreach (var line in said) sb.AppendLine("  " + line);
            }
            finally
            {
                world.Event -= Listen;
                Release();
                foreach (var u in extras) Remove(u);
            }
            report = sb.ToString();
        }

        static Vector2 Norm(Vector2 v) => v.sqrMagnitude > 1e-6f ? v.normalized : Vector2.right;
    }

    public sealed class PunishTrial : MoraleTrial
    {
        /// <summary>A weapon the trial's Mech must carry (MoraleTrials.PunishMortar ...), or none.</summary>
        public MechWeapon? mounting;
        /// <summary>Destroy the Mech once its first deserter is down (MoraleTrials.PunishMechDies).</summary>
        public bool killMech;
        /// <summary>Its hull at 50% when it decides (MoraleTrials.PunishHurt): it must decide and say
        /// so at once, keep its guns off them while hurt, and go once whole.</summary>
        public bool hurtFirst;
        /// <summary>Send it to its bay for repairs as they run (MoraleTrials.PunishRepairing): the
        /// verdict must still come at once (repairs return before Punish), the guns stay off them
        /// while it mends, and it goes once mended.</summary>
        public bool repairFirst;
        /// <summary>Attack the Mech once it has set off (MoraleTrials.PunishInterrupted).</summary>
        public bool interrupt;
        /// <summary>Kill the deserters while the hurt Mech has decided but cannot go (MoraleTrials.PunishGone).</summary>
        public bool goneFirst;
        /// <summary>Its hull when sent to the bay (repairFirst): 25% (under its repair line) or 50%
        /// (above it, but too hurt to go: MoraleTrials.PunishTopUp).</summary>
        public float repairHp = 0.25f;
        /// <summary>Its own verdict, unforced (MoraleTrials.PunishNatural).</summary>
        public bool natural;
        /// <summary>The Mech still coming down as they run (MoraleTrials.PunishInbound).</summary>
        public bool inbound;
        /// <summary>At 50% with no bay: never free to go (MoraleTrials.PunishNeverFree).</summary>
        public bool neverFree;
        /// <summary>Hit from 45 m while it owes them a visit (MoraleTrials.PunishTopUpUnderFire).</summary>
        public bool underFire;

        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var boot = GameBootstrap.Instance;
            var sb = new StringBuilder();
            world.Event += Listen;
            var loyal = new List<Unit>();
            try
            {
                Hold();
                if (world.time < 5f) yield return Wait(5f - world.time);
                var brain = boot != null ? boot.MechBrains[side] : null;
                if (brain == null) { report = $"no Mech brain for team {side}"; yield break; }
                Vector2 home = map.StartPos(side), foe = map.StartPos(1 - side);
                Unit MakeMech(bool drop = false)
                {
                    var design = MechParts.Generate(4711u);
                    if (mounting.HasValue)
                        for (uint seed = 1; seed < 5000; seed++)
                        {
                            var d = MechParts.Generate(seed);
                            if (d.weapons.Contains(mounting.Value)) { design = d; break; }
                        }
                    return world.TrialMech(side, design, world.NearestReachable(home, home + (foe - home).normalized * 22f, true), 0f, drop, asSides: true);
                }
                // A Mech on the field as they run gives its own verdict a few seconds later, before
                // the trial's: the plain cases bring theirs in after the deserters reach the camp.
                Unit mech = world.MechOf(side);
                if (mech == null && (hurtFirst || repairFirst || goneFirst || natural || neverFree)) mech = MakeMech();
                Vector2 at = SpawnSquad(6, 0);
                if (natural || inbound)
                {
                    // Nobody forces the verdict: its own must come 4-9 s after the break, or none
                    // from a Mech that was still coming down.
                    if (inbound) mech = MakeMech(drop: true);
                    else yield return Wait(1f);
                    string before = brain.LastVerdict;
                    int punishmentsBefore = brain.Punishments, atBreak = said.Count;
                    bool landedAtBreak = mech.mech.Landed;
                    world.Desert(side, at, squad, 4);
                    float breakT = world.time, verdictAfter = -1f;
                    while (world.time - breakT < 20f)
                    {
                        if (verdictAfter < 0f && brain.LastVerdict != before) verdictAfter = world.time - breakT;
                        yield return null;
                    }
                    int lines = 0;
                    for (int i = atBreak; i < said.Count; i++) if (said[i].Contains("Status:") || said[i].Contains("Morale:")) lines++;
                    var M = world.morale[side];
                    sb.AppendLine($"Mech landed at the break {landedAtBreak}, on record {M.mechAtBreak}; landed now {mech.mech.Landed}; {M.deserters.Count} deserted");
                    if (inbound)
                        sb.AppendLine($"verdict {(verdictAfter < 0f ? "none" : $"after {verdictAfter:0.0} s: {brain.LastVerdict}")} (want none); punishing {brain.Punishing}, " +
                                      $"went {brain.Punishments - punishmentsBefore} time(s) (want False, 0); {lines} line(s) since (its landing greeting, nothing about them)");
                    else
                        sb.AppendLine($"verdict {(verdictAfter < 0f ? "NONE in 20 s" : $"{verdictAfter:0.0} s after the break")} (want 4-9 s): {brain.LastVerdict} (want not forced); " +
                                      $"punishing {brain.Punishing}; {lines} line(s) said (a go is always said, a spare 60% of the time)");
                    for (int i = atBreak; i < said.Count; i++) sb.AppendLine("  " + said[i]);
                    report = sb.ToString();
                    yield break;
                }
                // Hurt, or in the gantry, before they run: the verdict must still come at once,
                // and its guns stay off them until it is fit to go.
                if (hurtFirst || goneFirst || neverFree) mech.hp = mech.MaxHp * 0.5f;
                if (repairFirst)
                {
                    var F = world.factions[side];
                    if (!Unit.Live(F.bay))
                    {
                        Vector2 dir = (foe - home).normalized;
                        Vector2 spot = world.NearestReachable(home, home + dir * 14f + new Vector2(-dir.y, dir.x) * 10f);
                        F.bay = world.Spawn(UnitType.MechBay, side, spot, true);
                        F.bayPlaced = true;
                    }
                    mech.hp = mech.MaxHp * repairHp;
                    yield return Wait(3f);
                    sb.AppendLine($"at {repairHp * 100f:0}% with a bay before they ran: intent {mech.mech.intent} \"{mech.mech.intentText}\"");
                }
                yield return Wait(1f);
                world.Desert(side, at, squad, 4);
                var deserters = Deserters();
                if (goneFirst)
                {
                    // Decided to go but hurt, so it cannot; then the enemy (no shooter) gets them all.
                    int atBreak = said.Count;
                    brain.PunishNow();
                    yield return Wait(3f);
                    foreach (var u in deserters) if (Unit.Live(u)) world.Damage(u, 1e4f, u.pos, null);
                    yield return Wait(3f);
                    var lines = new List<string>();
                    for (int i = atBreak; i < said.Count; i++) if (said[i].Contains("Status:")) lines.Add(said[i]);
                    sb.AppendLine($"hurt (50%), deserters killed by someone else: punishing now {brain.Punishing}, purge {world.Purging(side)}, put down by it {world.morale[side].executed} (want: False, False, 0)");
                    foreach (var l in lines) sb.AppendLine("  " + l);
                    sb.AppendLine(lines.Exists(l => l.Contains("enemy found them") || l.Contains("settled their account")) ? "said the enemy got there first (want yes)" : "DID NOT say the enemy got there first");
                    report = sb.ToString();
                    yield break;
                }
                if (neverFree)
                {
                    // No bay at all: hurt, it can neither go nor mend, so it must let them be in the end.
                    var F = world.factions[side];
                    if (Unit.Live(F.bay)) world.Damage(F.bay, 1e7f, F.bay.pos, null);
                    int atBreak = said.Count;
                    brain.PunishNow();
                    float t1 = world.time, endedAfter = -1f;
                    bool decided = false, purgeEver = false;
                    while (world.time - t1 < 340f)
                    {
                        if (brain.Punishing) decided = true;
                        if (world.Purging(side)) purgeEver = true;
                        if (decided && !brain.Punishing) { endedAfter = world.time - t1; break; }
                        yield return null;
                    }
                    int alive = 0;
                    foreach (var u in deserters) if (Unit.Live(u)) alive++;
                    sb.AppendLine($"hurt (50%), bay {(Unit.Live(F.bay) ? "STANDING" : "none")}: decided {decided}; the go {(endedAfter < 0f ? "STILL OPEN after 340 s" : $"ended after {endedAfter:0} s")} (want ~300); " +
                                  $"purge ever on {purgeEver} (want False); {alive}/{deserters.Count} deserters alive (want all); hull {mech.hp / mech.MaxHp * 100f:0}%, intent {mech.mech.intent}");
                    for (int i = atBreak; i < said.Count; i++) sb.AppendLine("  " + said[i]);
                    report = sb.ToString();
                    yield break;
                }
                if (hurtFirst || repairFirst)
                {
                    int atBreak = said.Count;
                    brain.PunishNow();   // a "go": so the guns-off-while-busy part is tested too
                    if (underFire)
                    {
                        // Hit from beyond the 34 m its repair rule looks at: it must hold, not walk off to
                        // mend. The blows come from 45 m; the enemy credited sits in its own base, out of
                        // the Mech's reach (one standing at 45 m it went and killed after two hits).
                        Vector2 from = mech.pos + (foe - home).normalized * 45f;
                        var shooter = world.Spawn(UnitType.Trooper, 1 - side, world.NearestWalkable(foe, 10f));
                        world.CmdHold(new List<Unit> { shooter });
                        float u0 = world.time, lastHit = -10f, fromM = 45f;
                        int hits = 0;
                        bool walkedOff = false;
                        while (world.time - u0 < 24f && Unit.Live(mech))
                        {
                            if (Unit.Live(shooter) && world.time - lastHit >= 2f) { world.Damage(mech, mech.MaxHp * 0.004f, from, shooter); lastHit = world.time; hits++; }
                            if (world.time - lastHit < 4f && (mech.mech.intent == MechIntent.Withdrawing || mech.mech.intent == MechIntent.Repairing)) walkedOff = true;
                            yield return null;
                        }
                        sb.AppendLine($"hit {hits} times from {fromM:0} m over 24 s at {mech.hp / mech.MaxHp * 100f:0}%: {(walkedOff ? "WALKED OFF to mend while hit" : "held while hit")} (want held); shooter {(Unit.Live(shooter) ? "alive" : "killed by it")}");
                        Remove(shooter);
                    }
                    else yield return Wait(hurtFirst ? 20f : 8f);
                    int alive = 0, lines = 0;
                    foreach (var u in deserters) if (Unit.Live(u)) alive++;
                    for (int i = atBreak; i < said.Count; i++) if (said[i].Contains("Status:") || said[i].Contains("Morale:")) lines++;
                    sb.AppendLine($"{(hurtFirst ? "hurt (50%)" : $"mending ({mech.hp / mech.MaxHp * 100f:0}%, {mech.mech.intent})")}: {(brain.Punishing ? "decided to go" : "no decision")}, {lines} line(s) said; " +
                                  $"{alive}/{deserters.Count} deserters alive, purge {world.Purging(side)} (want: decided and said so, all alive, purge off)");
                    if (hurtFirst) mech.hp = mech.MaxHp;
                }
                Vector2 camp = world.morale[side].camp;
                yield return WaitInCamp(deserters, 120f);
                if (mech == null) mech = MakeMech();
                // Two loyal Troopers holding beside the camp: the Mech's fire must not touch them.
                for (int i = 0; i < 2; i++)
                {
                    var p = world.NearestWalkable(camp + new Vector2(i == 0 ? 7f : -7f, 3f), 5f);
                    var u = world.Spawn(UnitType.Trooper, side, p);
                    world.CmdHold(new List<Unit> { u });
                    loyal.Add(u);
                }
                yield return Wait(1f);
                float loyalHp = 0f;
                foreach (var u in loyal) loyalHp += u.hp;
                int enemyKills = world.factions[1 - side].killed, alarmsBefore = alarms, mechAlarmsBefore = mechAlarms;
                sb.AppendLine($"team {side}: {mech.mech.design} at {(mech.pos - camp).magnitude:0} m from the camp; {deserters.Count} deserters in it; loyal Troopers {loyal.Count} beside it");

                int saidBefore = said.Count;
                if (!hurtFirst && !repairFirst) brain.PunishNow();
                var raiders = new List<Unit>();
                bool raided = false, offAfterRaid = false;
                int raids = 0, turnBacks = 0, purgeWhileRaided = 0;
                var cam = Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
                bool shot = false;
                float t0 = world.time, firstHit = -1f, mechKilledAt = -1f;
                int left = deserters.Count;
                while (world.time - t0 < (repairFirst ? 260f : 150f))
                {
                    left = 0;
                    foreach (var u in deserters) if (Unit.Live(u)) left++;
                    if (firstHit < 0f && left < deserters.Count) firstHit = world.time - t0;
                    // The Mech's call on the comms, photographed while it is up.
                    if (!shot && said.Count > saidBefore && said.FindIndex(saidBefore, l => l.Contains("Status:")) >= 0)
                    {
                        shot = true;
                        if (cam != null) cam.CenterOn(mech.pos);
                        ScreenCapture.CaptureScreenshot("Temp/morale_comms.png");
                    }
                    // Interrupted: once it has set off, two enemy Troopers go at it; once it has
                    // dealt with them and set off again, two more (it must say it turns back once).
                    if (raided && !world.Purging(side)) offAfterRaid = true;
                    if (interrupt && raids < 2 && world.Purging(side) && Unit.Live(mech) &&
                        (raids == 0 || (offAfterRaid && !raiders.Exists(r => Unit.Live(r)))))
                    {
                        raided = true;
                        offAfterRaid = false;
                        raids++;
                        Vector2 off = world.NearestReachable(mech.pos, mech.pos + (camp - mech.pos).normalized * -18f);
                        for (int i = 0; i < 2; i++)
                        {
                            var r = world.Spawn(UnitType.Trooper, 1 - side, world.NearestWalkable(off + new Vector2(i * 1.5f, 0f), 4f));
                            raiders.Add(r);
                            world.CmdAttack(new List<Unit> { r }, mech);
                        }
                    }
                    if (raided && raiders.Exists(r => Unit.Live(r)) && world.time - mech.lastDamagedT < 1f && world.Purging(side)) purgeWhileRaided++;
                    if (killMech && mechKilledAt < 0f && firstHit >= 0f && Unit.Live(mech))
                    {
                        mechKilledAt = world.time - t0;
                        world.Damage(mech, 1e7f, mech.pos, null);
                    }
                    if (left == 0 && !brain.Punishing) break;
                    if (mechKilledAt >= 0f && world.time - t0 > mechKilledAt + 5f) break;   // seen it end; the rest stay
                    yield return null;
                }
                float loyalAfter = 0f;
                int loyalAlive = 0;
                foreach (var u in loyal) if (Unit.Live(u)) { loyalAfter += u.hp; loyalAlive++; }
                if (interrupt)
                {
                    for (int i = saidBefore; i < said.Count; i++) if (said[i].Contains("will keep") || said[i].Contains("turning back")) turnBacks++;
                    int raidersLeft = 0;
                    foreach (var r in raiders) if (Unit.Live(r)) raidersLeft++;
                    sb.AppendLine($"interrupted: {(raided ? $"attacked {raids} time(s) once it had set off (want 2)" : "NEVER SET OFF")}; {turnBacks} turn-back line(s) (want 1); frames with the purge on just after a hit {purgeWhileRaided} (want 0 or a few); {raidersLeft} attackers left");
                    foreach (var r in raiders) Remove(r);
                }
                sb.AppendLine($"verdict: {brain.LastVerdict}");
                sb.AppendLine($"guns: {string.Join(", ", mech.mech.guns.ConvertAll(g => $"{g.part.name} {g.part.minRange:0}-{mech.mech.Range(g):0} m"))}; stands {MechBrain.PunishStand(mech.mech):0} m off");
                sb.AppendLine($"first deserter down after {firstHit:0} s; {left} left after {world.time - t0:0} s; punishing now {brain.Punishing}, purge flag {world.Purging(side)}");
                sb.AppendLine($"loyal Troopers: {loyalAlive}/{loyal.Count} alive, hp {loyalHp:0} -> {loyalAfter:0} (want unchanged)");
                // Killing the Mech here is the trial's own blow, with no shooter: it credits the
                // enemy one kill and raises one alarm, as any death from nowhere would.
                // The raiders' hits on the Mech raise alarms of their own; only the rest count here.
                int wantOwn = mechKilledAt >= 0f ? 1 : 0;
                int ownAlarms = alarms - alarmsBefore - (interrupt ? mechAlarms - mechAlarmsBefore : 0);
                sb.AppendLine($"enemy kills credited {world.factions[1 - side].killed - enemyKills} (want {wantOwn}); alarms for team {side} {ownAlarms} (want {wantOwn})" +
                              (interrupt ? $", besides {mechAlarms - mechAlarmsBefore} from the raiders' hits on the Mech" : "") + "; " +
                              $"put down on record {world.morale[side].executed}");
                sb.AppendLine(Unit.Live(mech) ? $"Mech now: {mech.mech.intent} \"{mech.mech.intentText}\"" : $"Mech destroyed at {mechKilledAt:0} s into the punishment");
                foreach (var line in said) sb.AppendLine("  " + line);
            }
            finally
            {
                world.Event -= Listen;
                Release();
                foreach (var u in loyal) Remove(u);
            }
            report = sb.ToString();
        }
    }

    public sealed class NaturalChainTrial : MoraleTrial
    {
        // GameBootstrap.OnTick turns morale off while a trial holds the opponent; this runs after it.
        void KeepMoraleOn(float dt) => world.moraleOn = true;
        readonly List<Unit> staged = new List<Unit>();

        // Also when cut off mid-run (re-staged, destroyed): the coroutine's finally does not run
        // then, and a handler left on Ticked would keep morale on for every later trial in the scene.
        void OnDisable()
        {
            if (world == null) return;
            world.Ticked -= KeepMoraleOn;
            world.Event -= Listen;
        }

        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var boot = GameBootstrap.Instance;
            var sb = new StringBuilder();
            world.Event += Listen;
            world.Ticked += KeepMoraleOn;
            try
            {
                Hold();
                var brain = boot != null ? boot.MechBrains[side] : null;
                if (brain == null) { report = $"no Mech brain for team {side}"; yield break; }
                Vector2 home = map.StartPos(side), foe = map.StartPos(1 - side), dir = (foe - home).normalized;
                // No reading before 300 s.
                Time.timeScale = 8f;
                while (world.time < 290f) yield return null;
                Time.timeScale = 3f;
                var M = world.morale[side];
                // The war: their army holding at their base.
                var far = new List<Unit>();
                for (int i = 0; i < 12; i++) far.Add(world.Spawn(UnitType.Mauler, 1 - side, world.NearestWalkable(foe + new Vector2((i % 4) * 4f - 6f, (i / 4) * 4f - 4f), 6f)));
                world.CmdHold(far);
                staged.AddRange(far);
                // The fight: our six, their twelve rifles 24 m off (out of a rifle's 16 m, inside the 32 m
                // the reading weighs), all holding, so nobody fires.
                Vector2 at = SpawnSquad(6, 0);
                world.CmdHold(new List<Unit>(squad));
                var near = new List<Unit>();
                for (int i = 0; i < 12; i++)
                    near.Add(world.Spawn(UnitType.Trooper, 1 - side, world.NearestWalkable(at + dir * 24f + new Vector2(-dir.y, dir.x) * ((i - 5.5f) * 1.1f), 4f)));
                world.CmdHold(near);
                staged.AddRange(near);
                // Its Mech, landed, mending in its bay's gantry at home: one standing in the fight
                // outguns it (1,800 against 300, no crisis), and one free went and killed the rifles.
                var F = world.factions[side];
                if (!Unit.Live(F.bay))
                {
                    Vector2 spot = world.NearestReachable(home, home + dir * 14f + new Vector2(-dir.y, dir.x) * 10f);
                    F.bay = world.Spawn(UnitType.MechBay, side, spot, true);
                    F.bayPlaced = true;
                }
                var mech = world.MechOf(side) ?? world.TrialMech(side, MechParts.Generate(4711u), world.NearestReachable(home, home + dir * 22f, true), 0f, false, asSides: true);
                mech.hp = mech.MaxHp * 0.25f;
                yield return Wait(4f);
                sb.AppendLine($"its Mech at 25%: {mech.mech.intent} \"{mech.mech.intentText}\", {(mech.pos - at).magnitude:0} m from the fight");
                string verdictBefore = brain.LastVerdict;
                int saidBefore = said.Count, rounds = 0;
                float broke = -1f;
                while (rounds < 8 && M.deserted == 0)
                {
                    rounds++;
                    // Still in the gantry: mended, it went out and killed their rifles, and the war's
                    // exchange no longer read as lost (750 lost to 600 dealt).
                    if (mech.hp > mech.MaxHp * 0.3f) mech.hp = mech.MaxHp * 0.25f;
                    var shooter = near.Find(u => Unit.Live(u));
                    if (shooter == null) { sb.AppendLine($"round {rounds}: their rifles are all dead (its Mech went out?) -- stopped"); break; }
                    // Seven of ours fall there to their rifles (350 lost, none dealt)...
                    for (int i = 0; i < 7; i++)
                    {
                        var u = world.Spawn(UnitType.Trooper, side, world.NearestReachable(home, at + new Vector2(i - 3f, -3f)));
                        world.Damage(u, 1e4f, shooter.pos, shooter);
                    }
                    // ...and three of the six take a graze a second until the reading calls it a crisis.
                    int crisesAt = M.crises;
                    float r0 = world.time, lastHit = -10f;
                    while (world.time - r0 < 12f && M.crises == crisesAt)
                    {
                        if (world.time - lastHit >= 1f)
                        {
                            lastHit = world.time;
                            int n = 0;
                            foreach (var u in squad) if (Unit.Live(u) && !u.deserted && n++ < 3) world.Damage(u, u.MaxHp * 0.01f, shooter.pos, shooter);
                        }
                        yield return null;
                    }
                    sb.AppendLine($"round {rounds} at {world.time:0}s: {(M.crises > crisesAt ? "crisis" : "NO CRISIS")} -- {M.reading}; roll {M.lastRoll:0.00} against {M.lastOdds:0.00}: " +
                                  $"{(M.deserted > 0 ? $"broke, {M.deserted} deserted" : M.unfitBreaks > 0 ? $"broke with nobody fit to run ({M.unfitBreaks})" : "held")}");
                    if (M.deserted > 0) { broke = world.time; break; }
                    if (M.crises == crisesAt) break;
                    yield return Wait(44f);   // past CrisisGap (30 s from the last crisis reading, up to 6 s after the last hit): the next is a new crisis, its own roll
                }
                if (broke < 0f)
                {
                    sb.AppendLine($"no break in {rounds} crisis round(s) (each rolls 35% at this war's odds)");
                    report = sb.ToString();
                    yield break;
                }
                var deserters = Deserters();
                sb.AppendLine($"Mech on the field at the break {M.mechAtBreak} (want True); deserters {deserters.Count}, hull {string.Join(", ", deserters.ConvertAll(d => $"{d.hp / d.MaxHp * 100f:0}%"))}");
                float verdictAfter = -1f, t1 = world.time;
                int executedBefore = M.executed;
                while (world.time - t1 < 150f)
                {
                    if (verdictAfter < 0f && brain.LastVerdict != verdictBefore) verdictAfter = world.time - broke;
                    bool alive = false;
                    foreach (var d in deserters) if (Unit.Live(d)) alive = true;
                    if (verdictAfter >= 0f && !brain.Punishing && (!alive || world.time - broke > 45f)) break;
                    yield return null;
                }
                int left = 0, inCamp = 0;
                foreach (var d in deserters) if (Unit.Live(d)) { left++; if (!d.fleeing && (d.pos - M.camp).magnitude < 15f) inCamp++; }
                sb.AppendLine($"verdict {(verdictAfter < 0f ? "NONE" : $"{verdictAfter:0.0} s after the break")} (want 4-9 s): {brain.LastVerdict}");
                sb.AppendLine($"after {world.time - broke:0} s: {left}/{deserters.Count} deserters alive ({inCamp} sitting in the camp), put down by its Mech {M.executed - executedBefore}; punishing {brain.Punishing}, purge {world.Purging(side)}");
                for (int i = saidBefore; i < said.Count; i++) sb.AppendLine("  " + said[i]);
            }
            finally
            {
                world.Ticked -= KeepMoraleOn;
                world.Event -= Listen;
                Release();
                foreach (var u in staged) Remove(u);
            }
            report = sb.ToString();
        }
    }

    public sealed class BreakPickTrial : MoraleTrial
    {
        // Never a match's setting, so put back to off rather than to what was saved: a trial
        // re-staged in its old-pick pass could otherwise save "on" and leave it on.
        void OnDisable() => GameWorld.LegacyBreakPick = false;

        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var made = new List<Unit>();
            try
            {
                Hold();
                Vector2 home = map.StartPos(side), foe = map.StartPos(1 - side), dir = (foe - home).normalized, side2 = new Vector2(-dir.y, dir.x);
                Vector2 at = world.NearestReachable(home, Vector2.Lerp(home, foe, 0.35f));
                // Pass 2: the new pick with an unarmed enemy building beside the rear (a knot fighting
                // by their base): it must not turn the rear into "front".
                for (int pass = 0; pass < 3; pass++)
                {
                    GameWorld.LegacyBreakPick = pass == 0;
                    int rear = 0, alive = 0, runs = 0;
                    for (int it = 0; it < 20; it++)
                    {
                        var knot = new List<Unit>();
                        var front = new List<Unit>();
                        for (int i = 0; i < 6; i++)
                        {
                            bool isFront = i < 3;
                            Vector2 p = at + (isFront ? Vector2.zero : -dir * 22f) + side2 * ((i % 3 - 1) * 2.5f);
                            var u = world.Spawn(UnitType.Trooper, side, world.NearestReachable(home, p));
                            knot.Add(u); made.Add(u);
                            if (isFront) front.Add(u);
                        }
                        world.CmdHold(knot);
                        // Six enemy rifles 12 m ahead, each going at one of the front three (holding, they
                        // never fired: every deserter, front or rear, lived).
                        var foes = new List<Unit>();
                        for (int i = 0; i < 6; i++)
                        {
                            var f = world.Spawn(UnitType.Trooper, 1 - side, world.NearestWalkable(at + dir * 12f + side2 * ((i - 2.5f) * 1.6f), 4f));
                            world.CmdAttack(new List<Unit> { f }, front[i % 3]);
                            foes.Add(f);
                        }
                        made.AddRange(foes);
                        if (pass == 2)
                            made.Add(world.Spawn(UnitType.Bunkhouse, 1 - side, world.NearestWalkable(at - dir * 22f - side2 * 8f, 6f), true));
                        yield return Wait(2f);   // the hash has them, and the front is trading shots
                        if (world.Desert(side, at, knot, 1) == 1)
                        {
                            var d = knot.Find(u => Unit.Live(u) && u.deserted);
                            if (d != null)
                            {
                                runs++;
                                if (!front.Contains(d)) rear++;
                                yield return Wait(6f);
                                if (Unit.Live(d)) alive++;
                            }
                        }
                        foreach (var u in made) Remove(u);
                        made.Clear();
                        yield return Wait(0.5f);
                    }
                    sb.AppendLine($"{(pass == 0 ? "old pick" : pass == 1 ? "new pick" : "new pick, an unarmed enemy building by the rear")}: {runs} broke; from the rear {rear}/{runs}, alive 6 s later {alive}/{runs}");
                }
                sb.AppendLine("want: the new pick mostly from the rear (about 4 in 5 by weight), and more of its deserters alive; the building changes neither");
            }
            finally
            {
                GameWorld.LegacyBreakPick = false;
                foreach (var u in made) Remove(u);
                Release();
            }
            report = sb.ToString();
        }
    }

    public sealed class StrandedTrial : MoraleTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var sb = new StringBuilder();
            var raiders = new List<Unit>();
            Unit lone = null;
            int loneShots = 0, campShots = 0;
            System.Action<GameEvent> count = e =>
            {
                if (e.kind != GameEventKind.Fire || e.unit == null || !e.unit.deserted) return;
                if (e.unit == lone) loneShots++; else campShots++;
            };
            world.Event += count;
            world.Event += Listen;
            try
            {
                Hold();
                if (world.time < 5f) yield return Wait(5f - world.time);
                Vector2 at = SpawnSquad(6, 0);
                yield return Wait(1f);
                world.Desert(side, at, squad, 4);
                var deserters = Deserters();
                // One stays where it broke, as one cut off from the camp's ground does.
                lone = deserters[0];
                lone.BeginDesertion(lone.pos);
                yield return WaitInCamp(deserters, 120f);
                Vector2 camp = world.morale[side].camp, spot = lone.orderPos;
                sb.AppendLine($"lone deserter settled {(lone.pos - spot).magnitude:0.0} m from its spot, {(spot - camp).magnitude:0} m from the camp (fleeing {lone.fleeing}); the other {deserters.Count - 1} in the camp");
                // Two enemy Troopers go at the lone one, from the side away from the camp.
                Vector2 away = (spot - camp).sqrMagnitude > 1f ? (spot - camp).normalized : Vector2.right;
                Vector2 from = world.NearestReachable(spot, spot + away * 18f);
                if ((from - spot).magnitude < 8f) from = world.NearestReachable(spot, spot + new Vector2(-away.y, away.x) * 18f);
                for (int i = 0; i < 2; i++)
                {
                    var r = world.Spawn(UnitType.Trooper, 1 - side, world.NearestWalkable(from + new Vector2(i * 1.5f, 0f), 4f));
                    raiders.Add(r);
                    world.CmdAttack(new List<Unit> { r }, lone);
                }
                attacked = true;
                float t0 = world.time, far = 0f, died = -1f;
                while (world.time - t0 < 40f)
                {
                    if (Unit.Live(lone)) far = Mathf.Max(far, (lone.pos - spot).magnitude);
                    else if (died < 0f) died = world.time - t0;
                    yield return null;
                }
                int raidersLeft = 0;
                foreach (var r in raiders) if (Unit.Live(r)) raidersLeft++;
                sb.AppendLine($"raiders from {(from - spot).magnitude:0} m: the lone deserter fired {loneShots} shots (want > 0), went at most {far:0.0} m from its spot (leash {GameWorld.CampLeash:0}), " +
                              $"{(died >= 0f ? $"died after {died:0} s" : "alive after 40 s")}; {raidersLeft} raiders left");
                sb.AppendLine($"the camp, {(spot - camp).magnitude:0} m off: {campShots} shots (want 0: the alarm is only for those within {GameWorld.AlarmReach:0} m)");
                sb.AppendLine($"alarms for team {side}: {alarms}, from deserters {deserterAlarms} (want 0)");
            }
            finally
            {
                world.Event -= count;
                world.Event -= Listen;
                Release();
                foreach (var r in raiders) Remove(r);
            }
            report = sb.ToString();
        }
    }
}
