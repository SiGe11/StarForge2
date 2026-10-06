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

        static string Stage<T>(string name) where T : BugTrial
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<T>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject(name + "Trial").AddComponent<T>();
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
        protected readonly List<Unit> squad = new List<Unit>();
        protected int unprovoked, shotsBack, alarms, deserterAlarms;
        protected bool attacked;
        protected readonly List<string> said = new List<string>();

        protected void Listen(GameEvent e)
        {
            if (e.kind == GameEventKind.Fire && e.unit != null && e.unit.deserted)
            {
                if (attacked) shotsBack++; else unprovoked++;
            }
            if (e.kind == GameEventKind.UnderAttack && e.team == 0)
            {
                alarms++;
                if (e.unit != null && e.unit.deserted) deserterAlarms++;
            }
            if (e.kind == GameEventKind.MechAdvice && e.team == 0) said.Add($"{world.time:0}s {e.advice}: {e.text}");
            if (e.kind == GameEventKind.Desertion) said.Add($"{world.time:0}s DESERTION team {e.team}: {e.text} ({e.index})");
        }

        /// <summary>Team 0 soldiers on open ground a third of the way to the enemy.</summary>
        protected Vector2 SpawnSquad(int troopers, int maulers)
        {
            Vector2 home = map.StartPos(0), foe = map.StartPos(1);
            Vector2 at = world.NearestWalkable(Vector2.Lerp(home, foe, 0.35f), 20f);
            for (int i = 0; i < troopers + maulers; i++)
            {
                float a = i * 2.39996f;
                var p = world.NearestWalkable(at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (2f + 1.2f * Mathf.Sqrt(i)), 6f);
                squad.Add(world.Spawn(i < troopers ? UnitType.Trooper : UnitType.Mauler, 0, p));
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
            Vector2 camp = world.morale[0].camp;
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
            bool wasSuspended = Commander.Suspended;
            float scale = Time.timeScale;
            var sb = new StringBuilder();
            world.Event += Listen;
            var extras = new List<Unit>();
            try
            {
                Commander.Suspended = true;
                Time.timeScale = 3f;
                if (world.time < 5f) yield return Wait(5f - world.time);
                Vector2 at = SpawnSquad(6, 2);
                yield return Wait(1f);
                var F = world.factions[0];
                int supplyBefore = F.supplyUsed;
                int n = world.Desert(0, at, squad, 4);
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
                var rig = Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
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
                Commander.Suspended = wasSuspended;
                Time.timeScale = scale;
                foreach (var u in extras) Remove(u);
            }
            report = sb.ToString();
        }

        static Vector2 Norm(Vector2 v) => v.sqrMagnitude > 1e-6f ? v.normalized : Vector2.right;
    }

    public sealed class PunishTrial : MoraleTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var boot = GameBootstrap.Instance;
            bool wasSuspended = Commander.Suspended;
            float scale = Time.timeScale;
            var sb = new StringBuilder();
            world.Event += Listen;
            var loyal = new List<Unit>();
            try
            {
                Commander.Suspended = true;
                Time.timeScale = 3f;
                if (world.time < 5f) yield return Wait(5f - world.time);
                var brain = boot != null ? boot.MechBrains[0] : null;
                if (brain == null) { report = "no Mech brain for team 0"; yield break; }
                Vector2 home = map.StartPos(0), foe = map.StartPos(1);
                Unit mech = world.MechOf(0);
                if (mech == null)
                {
                    var design = MechParts.Generate(4711u);
                    mech = world.TrialMech(0, design, world.NearestReachable(home, home + (foe - home).normalized * 22f, true), 0f, false, asSides: true);
                }
                Vector2 at = SpawnSquad(6, 0);
                yield return Wait(1f);
                world.Desert(0, at, squad, 4);
                var deserters = Deserters();
                Vector2 camp = world.morale[0].camp;
                yield return WaitInCamp(deserters, 120f);
                // Two loyal Troopers holding beside the camp: the Mech's fire must not touch them.
                for (int i = 0; i < 2; i++)
                {
                    var p = world.NearestWalkable(camp + new Vector2(i == 0 ? 7f : -7f, 3f), 5f);
                    var u = world.Spawn(UnitType.Trooper, 0, p);
                    world.CmdHold(new List<Unit> { u });
                    loyal.Add(u);
                }
                yield return Wait(1f);
                float loyalHp = 0f;
                foreach (var u in loyal) loyalHp += u.hp;
                int enemyKills = world.factions[1].killed, alarmsBefore = alarms;
                sb.AppendLine($"{mech.mech.design} at {(mech.pos - camp).magnitude:0} m from the camp; {deserters.Count} deserters in it; loyal Troopers {loyal.Count} beside it");

                brain.PunishNow();
                float t0 = world.time, firstHit = -1f;
                int left = deserters.Count;
                while (world.time - t0 < 150f)
                {
                    left = 0;
                    foreach (var u in deserters) if (Unit.Live(u)) left++;
                    if (firstHit < 0f && left < deserters.Count) firstHit = world.time - t0;
                    if (left == 0 && !brain.Punishing) break;
                    yield return null;
                }
                float loyalAfter = 0f;
                int loyalAlive = 0;
                foreach (var u in loyal) if (Unit.Live(u)) { loyalAfter += u.hp; loyalAlive++; }
                sb.AppendLine($"verdict: {brain.LastVerdict}");
                sb.AppendLine($"first deserter down after {firstHit:0} s; {left} left after {world.time - t0:0} s; punishing now {brain.Punishing}, purge flag {world.Purging(0)}");
                sb.AppendLine($"loyal Troopers: {loyalAlive}/{loyal.Count} alive, hp {loyalHp:0} -> {loyalAfter:0} (want unchanged)");
                sb.AppendLine($"enemy kills credited {world.factions[1].killed - enemyKills} (want 0); alarms for team 0 {alarms - alarmsBefore} (want 0); " +
                              $"put down on record {world.morale[0].executed}");
                sb.AppendLine($"Mech now: {mech.mech.intent} \"{mech.mech.intentText}\"");
                foreach (var line in said) sb.AppendLine("  " + line);
            }
            finally
            {
                world.Event -= Listen;
                Commander.Suspended = wasSuspended;
                Time.timeScale = scale;
                foreach (var u in loyal) Remove(u);
            }
            report = sb.ToString();
        }
    }
}
