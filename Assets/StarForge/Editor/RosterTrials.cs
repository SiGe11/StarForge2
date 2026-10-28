// RosterTrials.cs — play-mode checks on the wider roster (Tools/editor.sh call
// StarForge.EditorTools.RosterTrials.<Method>; each has a ...Report to read):
//
//   Smoke     one of every new fighting unit a side, plus three Troopers, attack-moving at each
//             other on open ground for 45 s (opponent AI silenced), and a Hauler and a Prospector
//             mining at our Foundry: shots by type, deaths, how far each moved, the aircrafts'
//             height, ore each worker brought in, and every exception logged meanwhile.
//   Duels     equal-ore fights (600 ore a side, rounded to whole units) between every pair of
//             the fighting roster, each on its own open field, at 6x: who is left and what it is
//             worth. The balance target: every unit wins some and loses some, nothing wins all.
//   AIvsAI    both sides the Commander for 600 s at 8x: what each built of the wider roster,
//             its tech buildings and research, and who won.
//   Economy   ore brought in over 90 s by 600 ore of each worker type at our main Foundry (our own
//             Diggers taken off, the seams topped up between runs): ore a minute per worker, per
//             100 ore spent and per supply.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.AI;
using StarForge.Sim;
using StarForge.View;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class RosterTrials
    {
        static string Start<T>(string what) where T : BugTrial
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<T>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject(typeof(T).Name).AddComponent<T>();
            return what + " running; call its report";
        }

        static string Report<T>() where T : BugTrial
        {
            var p = Object.FindAnyObjectByType<T>();
            return p == null ? "not running" : p.report;
        }

        public static string Smoke() => Start<RosterSmoke>("smoke");
        public static string SmokeReport() => Report<RosterSmoke>();
        public static string Duels() => Start<RosterDuels>("duels");
        public static string DuelReport() => Report<RosterDuels>();
        /// <summary>Hits on an aircraft by speed: each anti-air gun on Hold, 12 m from a hovering Kestrel
        /// (its hull topped up every frame), 12 s at 1x and again at 8x: damage per shot, which must not
        /// depend on the frame length.</summary>
        public static string AirHits() => Start<RosterAirHits>("air hits");
        public static string AirHitsReport() => Report<RosterAirHits>();
        /// <summary>Short reach: a Scorcher and then a Skitter attack-moving at a Trooper on Hold 18 m off
        /// (the Trooper topped up): shots fired and how close they came.</summary>
        public static string Melee() => Start<RosterMelee>("melee");
        public static string MeleeReport() => Report<RosterMelee>();
        /// <summary>Photographs (Temp/roster_look_*.png): the new units of one side on Hold in a row on open
        /// ground, close in, then the Biolab's creatures alone, closer.</summary>
        public static string Look() => Start<RosterLook>("look");
        public static string LookReport() => Report<RosterLook>();
        /// <summary>The AI against aircraft: AI against AI (spectated, seed 1000, 8x); from 200 s to 560 s
        /// team 0 is given extra Kestrels, topped up to six and attack-moved at team 1's base whenever idle
        /// (team 0's own Commander may also take them into its squads). What team 1 trains meanwhile, against
        /// AntiAirControl's run without them, is its answer. Start from the menu.</summary>
        public static string AntiAir() { RosterAntiAir.Raid = true; return StartAntiAir(); }
        /// <summary>The same match with no Kestrels given: what team 1 trains on seed 1000 unraided.</summary>
        public static string AntiAirControl() { RosterAntiAir.Raid = false; return StartAntiAir(); }
        static string StartAntiAir()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<RosterAntiAir>();
            if (old != null) { old.Abort(); Object.Destroy(old.gameObject); }
            new GameObject("RosterAntiAir").AddComponent<RosterAntiAir>();
            return "anti-air running; call AntiAirReport";
        }
        public static string AntiAirReport() { var p = Object.FindAnyObjectByType<RosterAntiAir>(); return p == null ? "not running" : p.report; }
        /// <summary>Capes on promoted soldiers (InfantryCape): the 81% roll over 200 Troopers and again
        /// at the next stripe, cloth near the camera at 1x and none at 8x, the hem never in through
        /// the soldier while they walk, a hidden enemy's cape hidden, every cape mesh gone with its
        /// soldier; rear renders to Temp/cape_*.png.</summary>
        public static string Capes() => Start<RosterCapes>("capes");
        public static string CapesReport() => Report<RosterCapes>();
        public static string Economy() => Start<RosterEconomy>("economy");
        public static string EconomyReport() => Report<RosterEconomy>();
        /// <summary>An AI-vs-AI match (both sides the Commander, memory off) at 8x for <see cref="RosterAIvsAI.Seconds"/>:
        /// what each side built of the wider roster, its tech buildings and research, who won, and errors.
        /// Play must be fresh (menu): it starts the match itself, spectating.</summary>
        public static string AIvsAI() { RosterAIvsAI.Seconds = 600f; RosterAIvsAI.Seed = 1000; return StartAIvsAI(); }
        static string StartAIvsAI()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<RosterAIvsAI>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject("RosterAIvsAI").AddComponent<RosterAIvsAI>();
            return "AI vs AI running; call AIvsAIReport";
        }
        /// <summary>The same on the evaluation seeds whose personality leans to aircraft (1111) and to
        /// the Biolab (1074).</summary>
        public static string AIvsAIAir() { RosterAIvsAI.Seconds = 600f; RosterAIvsAI.Seed = 1111; return StartAIvsAI(); }
        public static string AIvsAIBio() { RosterAIvsAI.Seconds = 600f; RosterAIvsAI.Seed = 1074; return StartAIvsAI(); }
        /// <summary>What is on fire and alive in the effects now (for judging an fps difference between
        /// matches): game time, plants and grass cells burning, structures alight, particles alive.</summary>
        public static string Census()
        {
            var w = GameWorld.Instance;
            var v = StarForge.World.Vegetation.Instance;
            if (w == null || !w.running) return "no match";
            int alight = 0, scorchers = 0;
            foreach (var u in w.units)
                if (Unit.Live(u)) { if (u.def.building && u.burnUntil > w.time) alight++; if (u.Type == UnitType.Scorcher) scorchers++; }
            int particles = 0, systems = 0;
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                if (ps.particleCount > 0) { particles += ps.particleCount; systems++; }
            return $"t {w.time:0} burning plants {(v != null ? v.burning.Count : -1)} grass {(v != null ? v.burningGrass.Count : -1)} structures {alight} scorchers {scorchers} particles {particles} in {systems} systems";
        }
        public static string AIvsAI1074Long() { RosterAIvsAI.Seconds = 540f; RosterAIvsAI.Seed = 1074; return StartAIvsAI(); }
        public static string AIvsAI1000Long() { RosterAIvsAI.Seconds = 540f; RosterAIvsAI.Seed = 1000; return StartAIvsAI(); }
        public static string AIvsAIReport() { var p = Object.FindAnyObjectByType<RosterAIvsAI>(); return p == null ? "not running" : p.report; }

        /// <summary>The fighting units the duels pit against each other.</summary>
        public static readonly UnitType[] Fighters =
        {
            UnitType.Trooper, UnitType.Skimmer, UnitType.Mauler,
            UnitType.Thumper, UnitType.Scorcher, UnitType.Marksman, UnitType.Lancer, UnitType.Aegis,
            UnitType.Kestrel, UnitType.Swift, UnitType.Prospector, UnitType.Skitter, UnitType.Spitter, UnitType.Razorwing,
            UnitType.Rocketeer, UnitType.Ironclad, UnitType.Gorehorn,
        };

        /// <summary>Errors and exceptions logged while a trial runs.</summary>
        internal sealed class ErrorCount : System.IDisposable
        {
            public int count;
            public string first;
            public ErrorCount() => Application.logMessageReceived += On;
            void On(string msg, string stack, LogType t)
            {
                if (t != LogType.Exception && t != LogType.Error) return;
                if (msg.Contains("NoSubscription") || msg.Contains("SuperProxy")) return;   // Unity's AI package
                if (count++ == 0) first = msg + "\n" + stack;
            }
            public void Dispose() => Application.logMessageReceived -= On;
        }
    }

    public sealed class RosterSmoke : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            HoldStatics();
            var sb = new StringBuilder();
            var shots = new int[(int)UnitType.Count];
            var deaths = new int[(int)UnitType.Count];
            ListenWhileHeld(e =>
            {
                if ((e.kind == GameEventKind.Fire || e.kind == GameEventKind.Beam) && e.unit != null && e.type != UnitType.Mech) shots[(int)e.type]++;
                if (e.kind == GameEventKind.Death && (int)e.type >= 0 && e.type < UnitType.Count) deaths[(int)e.type]++;
            });
            using (var errors = new RosterTrials.ErrorCount())
            {
                var rng = new System.Random(77);
                if (!FindField(rng, out var b, out var d)) { report = "no open field"; ReleaseStatics(); yield break; }
                var mix = new List<UnitType>();
                foreach (var t in RosterTrials.Fighters) if (t > UnitType.Mech) mix.Add(t);
                mix.Add(UnitType.Trooper); mix.Add(UnitType.Trooper); mix.Add(UnitType.Trooper);
                var sides = new[] { new List<Unit>(), new List<Unit>() };
                var start = new Dictionary<Unit, Vector2>();
                for (int team = 0; team < 2; team++)
                {
                    Vector2 c = b + d * (team == 0 ? -16f : 16f);
                    for (int i = 0; i < mix.Count; i++)
                    {
                        Vector2 p = c + new Vector2(Mathf.Cos(i * 2.4f), Mathf.Sin(i * 2.4f)) * (2f + i * 0.35f);
                        var u = Put(mix[i], team, world.NearestWalkable(p, 8f));
                        sides[team].Add(u);
                        start[u] = u.pos;
                    }
                }
                world.CmdMove(sides[0], b + d * 16f, true);
                world.CmdMove(sides[1], b - d * 16f, true);

                // Workers at our main Foundry.
                Unit foundry = null;
                foreach (var u in world.units) if (Unit.Live(u) && u.team == 0 && u.Type == UnitType.Foundry) { foundry = u; break; }
                var workers = new List<Unit>();
                var brought = new Dictionary<Unit, int>();
                var lastLoad = new Dictionary<Unit, int>();
                if (foundry != null)
                    foreach (var t in new[] { UnitType.Hauler, UnitType.Prospector })
                    {
                        var w = Put(t, 0, world.NearestWalkable(foundry.pos + new Vector2(foundry.def.radius + 3f, 0f), 8f));
                        var node = world.NearestFreeNode(w.pos, 0);
                        if (node != null) world.CmdHarvest(new List<Unit> { w }, node);
                        workers.Add(w); brought[w] = 0; lastLoad[w] = 0;
                    }

                float maxAlt = 0f, minAlt = 99f;
                float until = world.time + 45f;
                while (world.time < until)
                {
                    foreach (var w in workers)
                    {
                        if (!Unit.Live(w)) continue;
                        if (w.carrying < lastLoad[w] && w.carrying == 0) brought[w] += lastLoad[w];
                        lastLoad[w] = w.carrying;
                    }
                    foreach (var side in sides)
                        foreach (var u in side)
                            if (Unit.Live(u) && u.def.flying && u.view != null && u.view.body != null)
                            {
                                float alt = u.view.body.position.y - world.Map.HeightAt(u.pos);
                                maxAlt = Mathf.Max(maxAlt, alt); minAlt = Mathf.Min(minAlt, alt);
                            }
                    yield return null;
                }

                sb.AppendLine($"field {b.x:0},{b.y:0}; 45 s; exceptions/errors {errors.count}");
                if (errors.first != null) sb.AppendLine("first: " + errors.first.Split('\n')[0] + " | " + (errors.first.Split('\n').Length > 1 ? errors.first.Split('\n')[1] : ""));
                sb.AppendLine("type          shots deaths  alive(0/1)  moved m (max)");
                var listed = new HashSet<UnitType>();
                foreach (var t in mix)
                {
                    if (!listed.Add(t)) continue;
                    int alive0 = 0, alive1 = 0; float moved = 0f;
                    for (int team = 0; team < 2; team++)
                        foreach (var u in sides[team])
                            if (u.Type == t)
                            {
                                if (Unit.Live(u)) { if (team == 0) alive0++; else alive1++; moved = Mathf.Max(moved, (u.pos - start[u]).magnitude); }
                            }
                    sb.AppendLine($"{t,-13} {shots[(int)t],5} {deaths[(int)t],6}  {alive0}/{alive1}        {moved:0.0}");
                }
                sb.AppendLine($"aircraft height over the ground {minAlt:0.0}-{maxAlt:0.0} m");
                foreach (var w in workers)
                    sb.AppendLine($"{w.Type}: {(Unit.Live(w) ? "alive" : "dead")}, order {w.order}, carrying {w.carrying}, brought in {brought[w]}");
            }
            ClearSpawned();
            ReleaseStatics();
            report = sb.ToString();
        }
    }

    public sealed class RosterDuels : BugTrial
    {
        public static int Budget = 600;
        public static float Cap = 80f;

        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            HoldStatics();
            Time.timeScale = 6f;
            var F = RosterTrials.Fighters;
            int n = F.Length;
            var score = new float[n, n];   // A's share of what was left, -1..1
            var lines = new StringBuilder();
            var rng = new System.Random(2024);
            using (var errors = new RosterTrials.ErrorCount())
            {
                int fights = 0;
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        report = $"fight {++fights} of {n * (n - 1) / 2}: {F[i]} vs {F[j]}";
                        if (!FindField(rng, out var b, out var d)) continue;
                        var a = Army(F[i], 0, b - d * 15f);
                        var c = Army(F[j], 1, b + d * 15f);
                        world.CmdMove(a, b + d * 15f, true);
                        world.CmdMove(c, b - d * 15f, true);
                        float t0 = world.time;
                        while (world.time - t0 < Cap && Worth(a) > 0f && Worth(c) > 0f)
                        {
                            // Anyone at the far end with nothing found goes on at the enemy.
                            if (((int)(world.time * 2f)) % 10 == 0)
                            {
                                Vector2 ca = Centre(a), cc = Centre(c);
                                Regroup(a, cc); Regroup(c, ca);
                            }
                            yield return null;
                        }
                        float wa = Worth(a), wc = Worth(c);
                        float s = (wa - wc) / Mathf.Max(1f, Mathf.Max(Spent(a), Spent(c)));
                        score[i, j] = s; score[j, i] = -s;
                        lines.AppendLine($"{F[i],-11} x{a.Count,-2} vs {F[j],-11} x{c.Count,-2}  left {wa,4:0} / {wc,4:0}  in {world.time - t0,3:0} s  -> {(s > 0.05f ? F[i].ToString() : s < -0.05f ? F[j].ToString() : "even")}");
                        ClearSpawned();
                        yield return Wait(2f);
                    }
                var sb = new StringBuilder();
                sb.AppendLine($"equal-ore duels, {Budget} ore a side, cap {Cap:0} s; errors {errors.count}" + (errors.first != null ? " first: " + errors.first.Split('\n')[0] : ""));
                sb.AppendLine("unit         wins losses  even   net (sum of shares)");
                for (int i = 0; i < n; i++)
                {
                    int w = 0, l = 0, e = 0; float net = 0f;
                    for (int j = 0; j < n; j++)
                    {
                        if (i == j) continue;
                        net += score[i, j];
                        if (score[i, j] > 0.05f) w++; else if (score[i, j] < -0.05f) l++; else e++;
                    }
                    sb.AppendLine($"{F[i],-12} {w,4} {l,6} {e,5}   {net,6:0.00}");
                }
                sb.Append(lines);
                report = sb.ToString();
            }
            ReleaseStatics();
        }

        List<Unit> Army(UnitType t, int team, Vector2 at)
        {
            var d = Defs.Get(t);
            int count = Mathf.Max(1, Mathf.RoundToInt(Budget / (float)d.cost));
            var list = new List<Unit>();
            for (int k = 0; k < count; k++)
            {
                Vector2 p = at + new Vector2(Mathf.Cos(k * 2.4f), Mathf.Sin(k * 2.4f)) * (1.5f + k * 0.45f);
                var u = Put(t, team, d.flying ? p : world.NearestWalkable(p, 8f));
                list.Add(u);
            }
            return list;
        }

        static float Worth(List<Unit> l)
        {
            float w = 0f;
            foreach (var u in l) if (Unit.Live(u)) w += u.def.cost * Mathf.Clamp01(u.hp / Mathf.Max(1f, u.MaxHp));
            return w;
        }

        static float Spent(List<Unit> l) { float s = 0f; foreach (var u in l) s += u.def.cost; return s; }

        static Vector2 Centre(List<Unit> l)
        {
            Vector2 c = Vector2.zero; int k = 0;
            foreach (var u in l) if (Unit.Live(u)) { c += u.pos; k++; }
            return k > 0 ? c / k : Vector2.zero;
        }

        void Regroup(List<Unit> l, Vector2 enemy)
        {
            if (enemy == Vector2.zero) return;
            var idle = new List<Unit>();
            foreach (var u in l) if (Unit.Live(u) && u.order == Order.Idle) idle.Add(u);
            if (idle.Count > 0) world.CmdMove(idle, enemy, true);
        }
    }

    public sealed class RosterEconomy : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            HoldStatics();
            Time.timeScale = 4f;
            Unit foundry = null;
            foreach (var u in world.units) if (Unit.Live(u) && u.team == 0 && u.Type == UnitType.Foundry) { foundry = u; break; }
            if (foundry == null) { report = "no Foundry"; ReleaseStatics(); yield break; }
            // Our own workers off the field, so only the trial's mine.
            foreach (var u in world.units) if (Unit.Live(u) && u.team == 0 && u.def.harvester) u.BeginDeath();
            var sb = new StringBuilder();
            sb.AppendLine("worker       n  ore in 90 s  per worker/min  per 100 ore spent/min  per supply/min");
            foreach (var t in new[] { UnitType.Worker, UnitType.Hauler, UnitType.Prospector })
            {
                foreach (var o in world.units)
                    if (Unit.Live(o) && o.Type == UnitType.Ore && (o.pos - foundry.pos).magnitude < 40f) o.oreLeft = Unit.NodeCapacity;
                var d = Defs.Get(t);
                int count = Mathf.Max(1, Mathf.RoundToInt(600f / d.cost));
                var list = new List<Unit>();
                for (int k = 0; k < count; k++)
                {
                    var w = Put(t, 0, world.NearestWalkable(foundry.pos + new Vector2(Mathf.Cos(k * 1.3f), Mathf.Sin(k * 1.3f)) * (foundry.def.radius + 3f), 8f));
                    var node = world.NearestFreeNode(w.pos, 0);
                    if (node != null) world.CmdHarvest(new List<Unit> { w }, node);
                    list.Add(w);
                }
                int before = world.factions[0].oreMined;
                yield return Wait(90f);
                int got = world.factions[0].oreMined - before;
                float perMin = got / 1.5f;
                sb.AppendLine($"{t,-11} {count,2}  {got,8}      {perMin / count,8:0.0}        {perMin / (count * d.cost) * 100f,10:0.0}          {perMin / (count * d.supplyCost),8:0.0}");
                ClearSpawned();
                yield return Wait(2f);
            }
            report = sb.ToString();
            ReleaseStatics();
        }
    }
}

namespace StarForge.EditorTools
{
    public sealed class RosterAIvsAI : MonoBehaviour
    {
        public static float Seconds = 600f;
        public static uint Seed = 1000;
        public string report = "staging";
        bool wasSpectate, wasMemory, wasFixed, restored = true;
        uint wasSeed;
        RosterTrials.ErrorCount errors;

        /// <summary>Puts back what the match set: the statics outlive the play session, so a run stopped
        /// half-way must not leave the next Play on a fixed seed with memory off.</summary>
        void Restore()
        {
            if (restored) return;
            restored = true;
            StarForge.Game.MatchSettings.spectate = wasSpectate;
            StarForge.Game.MatchSettings.aiMemory = wasMemory;
            StarForge.Game.MatchSettings.fixedSeed = wasFixed;
            StarForge.Game.MatchSettings.seed = wasSeed;
            Time.timeScale = 1f;
            errors?.Dispose(); errors = null;
        }

        void OnDestroy() => Restore();

        IEnumerator Start()
        {
            var boot = StarForge.Game.GameBootstrap.Instance;
            var world = GameWorld.Instance;
            if (boot == null || world == null) { report = "no bootstrap"; yield break; }
            if (world.running) { report = "a match is already running: start from the menu"; yield break; }
            wasSpectate = StarForge.Game.MatchSettings.spectate; wasMemory = StarForge.Game.MatchSettings.aiMemory;
            wasFixed = StarForge.Game.MatchSettings.fixedSeed; wasSeed = StarForge.Game.MatchSettings.seed;
            restored = false;
            Commander.Suspended = false;
            StarForge.Game.MatchSettings.aiMemory = false;
            StarForge.Game.MatchSettings.spectate = true;
            StarForge.Game.MatchSettings.fixedSeed = true;
            StarForge.Game.MatchSettings.seed = Seed;
            boot.StartMatch();
            yield return new WaitForSecondsRealtime(1.5f);
            Time.timeScale = 8f;
            errors = new RosterTrials.ErrorCount();
            {
                while (world.time < Seconds && world.winner < 0)
                {
                    report = $"running {world.time:0}/{Seconds:0} s";
                    yield return new WaitForSecondsRealtime(1f);
                }
                var sb = new StringBuilder();
                sb.AppendLine($"seed {Seed}, {world.time:0} s, winner {world.winner}, errors {errors.count}" + (errors.first != null ? " first: " + errors.first.Split('\n')[0] : ""));
                for (int t = 0; t < 2; t++)
                {
                    var ai = t == 1 ? boot.AI : boot.PlayerProxy;
                    if (ai == null) { sb.AppendLine($"team {t}: no Commander"); continue; }
                    var built = new StringBuilder();
                    for (int i = (int)UnitType.Mech + 1; i < (int)UnitType.Count; i++)
                        if (ai.Dbg.trained[i] > 0) built.Append(built.Length > 0 ? ", " : "").Append($"{(UnitType)i} {ai.Dbg.trained[i]}");
                    var standing = new StringBuilder();
                    foreach (var bt in new[] { UnitType.Armory, UnitType.Airfield, UnitType.Biolab, UnitType.Workshop, UnitType.Garrison })
                    {
                        int n = 0;
                        foreach (var u in world.units) if (Unit.Live(u) && u.team == t && u.Type == bt) n++;
                        standing.Append($"{bt} {n} ");
                    }
                    var ups = world.factions[t].armyUpgrades;
                    sb.AppendLine($"team {t} ({ai.Dbg.personality}): trained {(built.Length > 0 ? built.ToString() : "nothing new")}; " +
                                  $"original: Trooper {ai.Dbg.trained[(int)UnitType.Trooper]}, Mauler {ai.Dbg.trained[(int)UnitType.Mauler]}, Skimmer {ai.Dbg.trained[(int)UnitType.Skimmer]}, Digger {ai.Dbg.trained[(int)UnitType.Worker]}; " +
                                  $"standing {standing}; research {ai.Dbg.researchBought} (levels {string.Join("/", ups)}); ore mined {world.factions[t].oreMined}, lost {world.factions[t].lost}, killed {world.factions[t].killed}");
                }
                report = sb.ToString();
            }
            Restore();
        }
    }
}

namespace StarForge.EditorTools
{
    public sealed class RosterAirHits : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            HoldStatics();
            var sb = new StringBuilder("damage per shot on a hovering Kestrel, 12 m off\n");
            var rng = new System.Random(5);
            int shots = 0;
            ListenWhileHeld(e => { if (e.kind == GameEventKind.Fire && e.team == 0 && e.unit != null) shots++; });
            foreach (var t in new[] { UnitType.Trooper, UnitType.Marksman, UnitType.Aegis, UnitType.Spitter, UnitType.Swift, UnitType.Prospector, UnitType.Sentinel })
                foreach (float speed in new[] { 1f, 8f })
                {
                    if (!FindField(rng, out var b, out var d)) continue;
                    var kestrel = Put(UnitType.Kestrel, 1, b);
                    var gun = Put(t, 0, world.NearestWalkable(b + d * 12f, 6f));
                    if (!gun.def.building) world.CmdHold(new List<Unit> { gun });
                    Time.timeScale = speed;
                    shots = 0;
                    float dealt = 0f, until = world.time + 12f;
                    while (world.time < until)
                    {
                        if (Unit.Live(kestrel)) { dealt += kestrel.MaxHp - kestrel.hp; kestrel.hp = kestrel.MaxHp; }
                        yield return null;
                    }
                    sb.AppendLine($"{t,-11} x{speed:0}: {shots,3} shots, {dealt,6:0} damage, {(shots > 0 ? dealt / shots : 0f),5:0.0} a shot");
                    ClearSpawned();
                    yield return Wait(1f);
                }
            report = sb.ToString();
            ReleaseStatics();
        }
    }

    public sealed class RosterMelee : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            HoldStatics();
            var sb = new StringBuilder();
            var rng = new System.Random(9);
            int shots = 0;
            ListenWhileHeld(e => { if ((e.kind == GameEventKind.Fire || e.kind == GameEventKind.Beam) && e.team == 0 && e.unit != null) shots++; });
            foreach (var t in new[] { UnitType.Scorcher, UnitType.Skitter })
            {
                if (!FindField(rng, out var b, out var d)) continue;
                var dummy = Put(UnitType.Trooper, 1, b);
                world.CmdHold(new List<Unit> { dummy });
                // On the dummy's own ground (the nearest NavMesh anywhere can be a plateau or a pocket no
                // ground unit gets out of: put there, a Skitter never closed), and with a walk to it not
                // much longer than the 18 m between them (behind a cliff, a Scorcher walked 70 m round in
                // the 15 s and never arrived): the first of a ring of points round the dummy that has one.
                var path = new UnityEngine.AI.NavMeshPath();
                Vector2 at = world.NearestReachable(dummy.pos, b + d * 18f);
                float walk = 999f;
                for (int k = 0; k < 12 && walk > 26f; k++)
                {
                    float a = k * Mathf.PI / 6f;
                    var p = world.NearestReachable(dummy.pos, b + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 18f);
                    if (!UnityEngine.AI.NavMesh.CalculatePath(world.Map.Ground(p), dummy.transform.position, GameWorld.GroundAreas, path) ||
                        path.status != UnityEngine.AI.NavMeshPathStatus.PathComplete) continue;
                    float len = 0f;
                    for (int c = 1; c < path.corners.Length; c++) len += Vector3.Distance(path.corners[c - 1], path.corners[c]);
                    if (len < walk) { walk = len; at = p; }
                }
                var u = Put(t, 0, at);
                UnityEngine.AI.NavMesh.CalculatePath(u.transform.position, dummy.transform.position, GameWorld.GroundAreas, path);
                world.CmdMove(new List<Unit> { u }, b, true);
                shots = 0;
                float closest = 99f, dealt = 0f, until = world.time + 15f, nextTrace = world.time + 1f;
                var trace = new StringBuilder();
                while (world.time < until)
                {
                    if (Unit.Live(u)) { closest = Mathf.Min(closest, u.Dist(dummy) - dummy.def.radius); u.hp = u.MaxHp; }
                    if (Unit.Live(dummy)) { dealt += dummy.MaxHp - dummy.hp; dummy.hp = dummy.MaxHp; }
                    if (world.time >= nextTrace && Unit.Live(u))
                    {
                        nextTrace += 1f;
                        var ag = u.agent;
                        trace.Append($"  t{world.time - (until - 15f):0} d{u.Dist(dummy):0.0} {u.order} tgt {(u.target != null ? "y" : "n")} ");
                        if (ag != null && ag.enabled) trace.Append($"path {ag.pathStatus} has {ag.hasPath} pend {ag.pathPending} rem {ag.remainingDistance:0.0} v {ag.velocity.magnitude:0.0} ");
                        trace.Append($"seen {world.Visible(0, dummy.pos)}\n");
                    }
                    yield return null;
                }
                sb.AppendLine($"{t}: {shots} shots, {dealt:0} damage, closest {closest:0.0} m to the edge (reach {Defs.Get(t).range}), order {u.order}, target {(u.target != null ? u.target.Type.ToString() : "none")}, path at the start {path.status}, {walk:0} m to walk");
                if (shots == 0) sb.Append(trace);
                ClearSpawned();
                yield return Wait(1f);
            }
            report = sb.ToString();
            ReleaseStatics();
        }
    }
}

namespace StarForge.EditorTools
{
    public sealed class RosterCapes : BugTrial
    {
        static readonly UnitType[] Soldiers = { UnitType.Trooper, UnitType.Thumper, UnitType.Scorcher, UnitType.Marksman, UnitType.Rocketeer };

        static InfantryCape CapeOf(Unit u) => u != null ? u.GetComponentInChildren<InfantryCape>(true) : null;

        static int MeshesAlive()
        {
            int n = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<UnityEngine.Mesh>()) if (m.name == "SF_Cape") n++;
            return n;
        }

        IEnumerator Shot(Unit u, string name, float back)
        {
            yield return new WaitForEndOfFrame();
            var go = new GameObject("CapeCam");
            var cam = go.AddComponent<Camera>();
            var at = u.transform.position + Vector3.up * 1.1f;
            go.transform.position = at - u.transform.forward * back + Vector3.up * 1.4f + u.transform.right * 1.2f;
            go.transform.LookAt(at);
            cam.fieldOfView = 40f;
            var rt = new RenderTexture(960, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            System.IO.Directory.CreateDirectory("Temp");
            System.IO.File.WriteAllBytes("Temp/cape_" + name + ".png", tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.Destroy(rt); Object.Destroy(tex); Object.Destroy(go);
        }

        IEnumerator ShotAt(Vector3 at, string name)
        {
            yield return new WaitForEndOfFrame();
            var go = new GameObject("CapeCam");
            var cam = go.AddComponent<Camera>();
            go.transform.position = at + new Vector3(3.2f, 2.6f, 3.2f);
            go.transform.LookAt(at + Vector3.up * 0.3f);
            cam.fieldOfView = 40f;
            var rt = new RenderTexture(960, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            System.IO.File.WriteAllBytes("Temp/cape_" + name + ".png", tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.Destroy(rt); Object.Destroy(tex); Object.Destroy(go);
        }

        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            HoldStatics();
            Time.timeScale = 1f;
            var sb = new StringBuilder();
            using (var errors = new RosterTrials.ErrorCount())
            {
                var rng = new System.Random(7);
                if (!FindField(rng, out var b, out var d)) { report = "no field"; ReleaseStatics(); yield break; }
                var rig = Object.FindAnyObjectByType<RTSCamera>();
                if (rig != null) rig.JumpTo(b);
                Vector2 side = new Vector2(-d.y, d.x);
                var troops = new List<Unit>();
                int livedBefore = InfantryCape.Live;
                for (int i = 0; i < 200; i++)
                {
                    var p = world.NearestWalkable(b + side * ((i % 20) - 9.5f) * 1.6f - d * ((i / 20) - 4.5f) * 1.6f, 3f);
                    troops.Add(Put(Soldiers[i % Soldiers.Length], 0, p));
                }
                yield return Wait(0.5f);
                foreach (var u in troops) { u.CreditKill(); u.CreditKill(); }
                yield return null; yield return null;
                int caped1 = 0;
                foreach (var u in troops) if (CapeOf(u) != null) caped1++;
                sb.AppendLine($"200 soldiers, 40 each of {string.Join(", ", Soldiers)}");
                sb.AppendLine($"rank 1: {caped1} of 200 caped ({caped1 / 2f:0.0}%, roll {InfantryCape.Chance:P0}); capes in the scene {InfantryCape.Live - livedBefore}");
                foreach (var u in troops) { u.CreditKill(); u.CreditKill(); u.CreditKill(); }
                yield return null; yield return null;
                int caped2 = 0, trimmed = 0;
                foreach (var u in troops) { var c = CapeOf(u); if (c != null) { caped2++; if (c.Rank == 2) trimmed++; } }
                int uncaped1 = 200 - caped1;
                sb.AppendLine($"rank 2: {caped2 - caped1} of the {uncaped1} uncaped got one ({(uncaped1 > 0 ? 100f * (caped2 - caped1) / uncaped1 : 0f):0.0}%); {caped2} caped, {trimmed} trimmed for rank 2");

                // What the cloth costs at its budget: the frame time with 24 capes as cloth against
                // all at rest, in turn, twice (the soldiers standing in the wind, in view).
                yield return Wait(1.5f);
                // Vsync off for it (it holds the Editor's frame at 16.7 ms), put back after.
                float[] ms = new float[6];
                int clothOn = 0;
                int vsync = QualitySettings.vSyncCount, fpsCap = Application.targetFrameRate;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                for (int k = 0; k < 6; k++)
                {
                    InfantryCape.RestOnly = k % 2 == 1;
                    yield return new WaitForSecondsRealtime(1.5f);
                    if (k == 0) clothOn = InfantryCape.Simulating;
                    double sum = 0; int frames = 0;
                    float end = Time.realtimeSinceStartup + 4f;
                    while (Time.realtimeSinceStartup < end) { yield return null; sum += Time.unscaledDeltaTime; frames++; }
                    ms[k] = frames > 0 ? (float)(sum / frames * 1000.0) : 0f;
                }
                InfantryCape.RestOnly = false;
                QualitySettings.vSyncCount = vsync;
                Application.targetFrameRate = fpsCap;
                sb.AppendLine($"frame time (Editor, vsync off) with {clothOn} capes as cloth / all at rest, three times: {ms[0]:0.00} / {ms[1]:0.00}, {ms[2]:0.00} / {ms[3]:0.00}, {ms[4]:0.00} / {ms[5]:0.00} ms");

                // Cloth near the camera at 1x, none at 8x.
                yield return Wait(1.5f);
                int cloth1 = 0;
                foreach (var u in troops) { var c = CapeOf(u); if (c != null && c.IsCloth) cloth1++; }
                Time.timeScale = 8f;
                yield return null; yield return null;
                int cloth8 = 0;
                foreach (var u in troops) { var c = CapeOf(u); if (c != null && c.IsCloth) cloth8++; }
                sb.AppendLine($"cloth at 1x {cloth1} (budget 24, Simulating {InfantryCape.Simulating} at 8x), at 8x {cloth8}");
                // The rigid cape, leaning as the soldiers walk at 8x.
                var walkers = new List<Unit>();
                foreach (var u in troops) if (CapeOf(u) != null && walkers.Count < 30) walkers.Add(u);
                world.CmdMove(walkers, b + d * 22f, false);
                yield return new WaitForSecondsRealtime(0.6f);
                if (walkers.Count > 0) yield return Shot(walkers[0], "rigid_walk", 4.5f);
                Time.timeScale = 1f;
                yield return Wait(4f);
                if (walkers.Count > 0) yield return Shot(walkers[0], "rest", 4.5f);

                // Walking at 1x as cloth: how far any vertex comes forward of where it hangs at
                // rest (in through the soldier), and how near the ground the hem gets.
                world.CmdMove(walkers, b - d * 10f, false);
                // Every soldier walking about near the focus: how often a cape changes over between
                // cloth and rest while more than the budget are in reach.
                var all200 = new List<Unit>(troops);
                world.CmdMove(all200, b - d * 10f, false);
                int switches0 = 0;
                foreach (var u in troops) { var c = CapeOf(u); if (c != null) switches0 += c.Switches; }
                float flickFrom = world.time;
                int hitches0 = InfantryCape.Hitches;
                float intrude = 0f, lowest = 9f, far = 0f, deepest = 0f;
                int samples = 0, inside = 0, checkedVerts = 0;
                var restCache = new Dictionary<InfantryCape, Vector3[]>();
                float until = world.time + 6f;
                bool shotWalk = false;
                while (world.time < until)
                {
                    foreach (var u in walkers)
                    {
                        var c = CapeOf(u);
                        var v = c != null ? c.ClothVertices() : null;
                        if (v == null) continue;
                        if (!restCache.TryGetValue(c, out var rest)) restCache[c] = rest = c.Rest.vertices;
                        float ground = world.Map.HeightAt(u.pos);
                        // The soldier as solids: each leg a capsule from the hip down its own axis,
                        // the torso and pack a box in body space.
                        var view = u.view;
                        var body = view != null ? view.body : null;
                        Transform legL = null, legR = null;
                        if (body != null) foreach (var t in body.GetComponentsInChildren<Transform>()) { if (t.name == "LegL") legL = t; else if (t.name == "LegR") legR = t; }
                        for (int i = 0; i < v.Length && i < rest.Length; i++)
                        {
                            var w = c.transform.TransformPoint(v[i]);
                            intrude = Mathf.Max(intrude, v[i].z - rest[i].z);
                            lowest = Mathf.Min(lowest, w.y - ground);
                            far = Mathf.Max(far, v[i].magnitude);
                            float depth = 0f;
                            foreach (var leg in new[] { legL, legR })
                            {
                                if (leg == null) continue;
                                Vector3 a0 = leg.position, a1 = leg.position - leg.up * 0.72f;
                                Vector3 ab = a1 - a0;
                                float tt = Mathf.Clamp01(Vector3.Dot(w - a0, ab) / ab.sqrMagnitude);
                                depth = Mathf.Max(depth, 0.15f - Vector3.Distance(w, a0 + ab * tt));
                            }
                            if (body != null)
                            {
                                // The skirt against the waist (its back at z -0.16); above, the cape
                                // is meant to lie under the pack and the pauldrons.
                                var q = body.InverseTransformPoint(w);
                                if (q.y > 0.80f && q.y < 1.02f && Mathf.Abs(q.x) < 0.30f && q.z > -0.17f)
                                    depth = Mathf.Max(depth, q.z + 0.17f);
                            }
                            if (depth > 0f) { inside++; deepest = Mathf.Max(deepest, depth); }
                            checkedVerts++;
                        }
                        samples++;
                    }
                    if (!shotWalk && world.time > until - 3f && walkers.Count > 0) { shotWalk = true; yield return Shot(walkers[0], "cloth_walk", 4.5f); }
                    yield return null;
                }
                int switches1 = 0;
                foreach (var u in troops) { var c = CapeOf(u); if (c != null) switches1 += c.Switches; }
                sb.AppendLine($"while all 200 walked {world.time - flickFrom:0.0} s: {switches1 - switches0} changes between cloth and rest over {caped2} capes, {InfantryCape.Hitches - hitches0} hitches (frames over 0.1 s: each sends every cape to rest for a second, two changes apiece)");
                sb.AppendLine($"walking as cloth: {samples} cape-frames; furthest any vertex came in past its rest {intrude:0.000} (model units), hem lowest {lowest:0.00} m above the ground, furthest from the collar {far:0.00}; inside the soldier (legs as 0.15 capsules, the waist) {inside} of {checkedVerts} vertex-frames, deepest {deepest:0.000}; gave up {InfantryCape.GaveUp}");

                // A hidden enemy: promoted until caped, its cape must not be drawn.
                Unit enemy = null;
                for (int tries = 0; tries < 20 && enemy == null; tries++)
                    if (FindField(rng, out var e, out _) && (e - b).magnitude > 70f) enemy = Put(UnitType.Trooper, 1, e);
                if (enemy != null)
                {
                    for (int k = 0; k < 10; k++) enemy.CreditKill();
                    yield return null; yield return null;
                    var ec = CapeOf(enemy);
                    sb.AppendLine($"hidden enemy: visible {enemy.visibleToPlayer}, rank {enemy.rank}, caped {ec != null}, cape drawn {(ec != null && ec.Drawn)}");
                }

                // Gone with its soldier: kill them all, wait out the wrecks.
                int before = InfantryCape.Live;
                ClearSpawned();
                yield return Wait(25f);
                sb.AppendLine($"after the soldiers died: capes {before} -> {InfantryCape.Live} (was {livedBefore} before the trial), SF_Cape meshes alive {MeshesAlive()}");

                // The showcase, the crowd gone: one caped soldier of each type standing apart, seen
                // from behind as cloth; then two caped Troopers killed, one thrown forward, one back.
                if (rig != null) rig.JumpTo(b);
                var show = new List<Unit>();
                for (int i = 0; i < Soldiers.Length + 2; i++)
                {
                    var u = Put(i < Soldiers.Length ? Soldiers[i] : UnitType.Trooper, 0, world.NearestWalkable(b + side * (i - 3f) * 5f, 3f));
                    for (int k = 0; k < 10; k++) u.CreditKill();
                    show.Add(u);
                }
                yield return Wait(4f);
                for (int i = 0; i < Soldiers.Length; i++)
                    if (CapeOf(show[i]) != null) yield return Shot(show[i], Soldiers[i].ToString(), 4f);
                var f0 = show[Soldiers.Length]; var f1 = show[Soldiers.Length + 1];
                var c0 = CapeOf(f0); var c1 = CapeOf(f1);
                Vector2 fw0 = new Vector2(f0.transform.forward.x, f0.transform.forward.z), fw1 = new Vector2(f1.transform.forward.x, f1.transform.forward.z);
                world.Damage(f0, 1e6f, f0.pos - fw0 * 5f, null);   // shot from behind: thrown forward, face down
                world.Damage(f1, 1e6f, f1.pos + fw1 * 5f, null);   // shot from the front: thrown back
                yield return Wait(3f);
                if (c0 != null) yield return ShotAt(c0.transform.position, "death_forward");
                if (c1 != null) yield return ShotAt(c1.transform.position, "death_back");
                sb.AppendLine($"showcase: {show.FindAll(u => CapeOf(u) != null || !Unit.Live(u)).Count} of {show.Count} caped; fallers' capes kept {(c0 != null)}/{(c1 != null)}");
                sb.Append($"errors {errors.count}" + (errors.first != null ? " first: " + errors.first.Split('\n')[0] : ""));
            }
            report = sb.ToString();
            ReleaseStatics();
        }

        void OnDisable() => InfantryCape.RestOnly = false;
    }

    public sealed class RosterLook : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            HoldStatics();
            Time.timeScale = 1f;
            var rig = Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            var rng = new System.Random(3);
            var shots = new StringBuilder();
            var sets = new[]
            {
                ("units", new[] { UnitType.Rocketeer, UnitType.Thumper, UnitType.Scorcher, UnitType.Marksman, UnitType.Ironclad, UnitType.Lancer, UnitType.Aegis, UnitType.Hauler }, 4.2f, 30f),
                ("creatures", new[] { UnitType.Skitter, UnitType.Spitter, UnitType.Gorehorn, UnitType.Razorwing, UnitType.Skitter }, 4.0f, 20f),
            };
            // In our own base (explored and lit), in a row across the camera's view.
            Vector2 home = map.StartPos(0);
            Vector2 toThem = (map.StartPos(1) - home).normalized;
            foreach (var (name, types, gap, dist) in sets)
            {
                Vector2 b = world.NearestWalkable(home + toThem * 20f, 10f);
                Vector3 cr = rig != null ? rig.transform.right : Vector3.right;
                var side = new Vector2(cr.x, cr.z).normalized;
                var d = new Vector2(-side.y, side.x);
                var list = new List<Unit>();
                for (int i = 0; i < types.Length; i++)
                {
                    Vector2 p = b + side * ((i - (types.Length - 1) * 0.5f) * gap);
                    var u = Put(types[i], 0, Defs.Get(types[i]).flying ? p : world.NearestWalkable(p, 6f));
                    u.yaw = Mathf.Atan2(-d.x, -d.y);
                    list.Add(u);
                }
                world.CmdHold(list);
                if (rig != null) { rig.JumpTo(b); rig.ZoomTo(dist); }
                yield return new WaitForSecondsRealtime(3f);
                string path = System.IO.Path.GetFullPath($"Temp/roster_look_{name}.png");
                ScreenCapture.CaptureScreenshot(path, 2);
                yield return new WaitForSecondsRealtime(0.5f);
                shots.AppendLine(path);
                ClearSpawned();
                yield return new WaitForSecondsRealtime(1.5f);
            }
            report = "done\n" + shots;
            ReleaseStatics();
        }
    }
}


namespace StarForge.EditorTools
{
    public sealed class RosterAntiAir : MonoBehaviour
    {
        public static bool Raid = true;
        public string report = "staging";
        bool wasSpectate, wasMemory, wasFixed, restored = true;
        uint wasSeed;
        RosterTrials.ErrorCount errors;
        readonly List<Unit> raiders = new List<Unit>();

        /// <summary>Puts back what the match set, also when the run is stopped or restarted half-way (the
        /// statics outlive the play session); takes back the Kestrels it gave away while the match goes on
        /// (not when play stops: they go with the scene).</summary>
        void Restore(bool takeRaiders)
        {
            if (takeRaiders) foreach (var u in raiders) if (Unit.Live(u)) u.BeginDeath();
            raiders.Clear();
            if (restored) return;
            restored = true;
            StarForge.Game.MatchSettings.spectate = wasSpectate;
            StarForge.Game.MatchSettings.aiMemory = wasMemory;
            StarForge.Game.MatchSettings.fixedSeed = wasFixed;
            StarForge.Game.MatchSettings.seed = wasSeed;
            Time.timeScale = 1f;
            errors?.Dispose(); errors = null;
        }

        public void Abort() => Restore(true);
        void OnDestroy() => Restore(false);

        IEnumerator Start()
        {
            var boot = StarForge.Game.GameBootstrap.Instance;
            var world = GameWorld.Instance;
            if (boot == null || world == null) { report = "no bootstrap"; yield break; }
            if (world.running) { report = "a match is already running: start from the menu"; yield break; }
            wasSpectate = StarForge.Game.MatchSettings.spectate; wasMemory = StarForge.Game.MatchSettings.aiMemory;
            wasFixed = StarForge.Game.MatchSettings.fixedSeed; wasSeed = StarForge.Game.MatchSettings.seed;
            restored = false;
            Commander.Suspended = false;
            StarForge.Game.MatchSettings.aiMemory = false;
            StarForge.Game.MatchSettings.spectate = true;
            StarForge.Game.MatchSettings.fixedSeed = true;
            StarForge.Game.MatchSettings.seed = 1000;
            boot.StartMatch();
            yield return new WaitForSecondsRealtime(1.5f);
            Time.timeScale = 8f;
            errors = new RosterTrials.ErrorCount();
            Vector2 them = world.Map.StartPos(1), us = world.Map.StartPos(0);
            int sent = 0, lost = 0;
            while (world.time < 560f && world.winner < 0)
            {
                report = $"running {world.time:0}/560 s";
                if (Raid && world.time >= 200f)
                {
                    lost += raiders.RemoveAll(u => !Unit.Live(u));
                    while (raiders.Count < 6)
                    {
                        raiders.Add(world.Spawn(UnitType.Kestrel, 0, us + (them - us).normalized * 20f + new Vector2(raiders.Count * 3f, 0f), true));
                        sent++;
                    }
                    var idle = raiders.FindAll(u => u.order == Order.Idle);
                    if (idle.Count > 0) world.CmdMove(idle, them, true);
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }
            var t = boot.AI.Dbg.trained;
            int aegisNow = 0;
            foreach (var u in world.units)
                if (Unit.Live(u) && u.team == 1 && u.Type == UnitType.Aegis) aegisNow++;
            var snap = boot.AI.Perception.Snap;
            report = $"done ({(Raid ? "raided" : "control, no raid")}) at {world.time:0} s, winner {world.winner}, errors {errors.count}" + (errors.first != null ? " first: " + errors.first.Split('\n')[0] : "") +
                     (Raid ? $"; extra Kestrels given {sent}, died {lost}" : "") + "\n" +
                     $"team 1 ({boot.AI.Dbg.personality}) trained: Aegis {t[(int)UnitType.Aegis]}, Swift {t[(int)UnitType.Swift]}, Spitter {t[(int)UnitType.Spitter]}, Razorwing {t[(int)UnitType.Razorwing]}, Marksman {t[(int)UnitType.Marksman]}, " +
                     $"Trooper {t[(int)UnitType.Trooper]}, Ironclad {t[(int)UnitType.Ironclad]}, Mauler {t[(int)UnitType.Mauler]}, Lancer {t[(int)UnitType.Lancer]}; Aegis alive {aegisNow}; " +
                     $"its picture of team 0: air {snap.eAirValue:0}, light {snap.eLightValue:0}, heavy {snap.eHeavyValue:0}";
            Restore(true);
        }
    }
}
