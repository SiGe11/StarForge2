// CheatTrials.cs — the testing cheat and its price (Tools/editor.sh call
// StarForge.EditorTools.CheatTrials.Reckoning, then ReckoningReport): the key's gifts and its toggle,
// the mark on the HUD, and a staged win taken away: the desertions, the animals, the defeat, and the
// AI's memory file untouched.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using StarForge.AI;
using StarForge.Game;
using StarForge.Sim;
using StarForge.View;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class CheatTrials
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

        /// <summary>The key, the mark, and a staged win taken away. Each of these needs a fresh play at the menu.</summary>
        public static string Reckoning() => Start<CheatReckoningTrial>("reckoning");
        public static string ReckoningReport() => Report<CheatReckoningTrial>();
        /// <summary>A cheater who loses first: no animals, no line about them, nothing remembered.</summary>
        public static string LoseFirst() => Start<CheatLoseFirstTrial>("lose first");
        public static string LoseFirstReport() => Report<CheatLoseFirstTrial>();
        /// <summary>The control for the memory check: the same ending with no cheat does write the file.</summary>
        public static string MemoryControl() => Start<CheatMemoryControlTrial>("memory control");
        public static string MemoryControlReport() => Report<CheatMemoryControlTrial>();
        /// <summary>A reckoning at the end of a real match: AI against AI to the drop of both Mechs and past it, big
        /// armies on both sides, team 0 marked and team 1's base taken down at 8x.</summary>
        public static string Late() => Start<CheatLateTrial>("late");
        public static string LateReport() => Report<CheatLateTrial>();
    }

    /// <summary>Starts its own match with the AI's memory on, the real file copied aside and put back
    /// whatever happens.</summary>
    public abstract class CheatTrialBase : BugTrial
    {
        string memoryPath, memoryBackup;
        bool hadMemory;
        byte[] before;

        protected IEnumerator Begin(bool memory, bool spectate)
        {
            var boot = GameBootstrap.Instance;
            world = GameWorld.Instance;
            map = MapInfo.Instance;
            if (boot == null || world == null || world.running) { report = "needs a fresh play session at the menu"; yield break; }
            memoryPath = AIMemory.FilePath;
            hadMemory = File.Exists(memoryPath);
            if (hadMemory) { memoryBackup = memoryPath + ".cheatbak"; File.Copy(memoryPath, memoryBackup, true); }
            before = hadMemory ? File.ReadAllBytes(memoryPath) : null;
            MatchSettings.aiMemory = memory;
            MatchSettings.spectate = spectate;
            boot.StartMatch();
            yield return new WaitForSecondsRealtime(1.5f);
        }

        protected bool MemoryUntouched()
        {
            byte[] after = File.Exists(memoryPath) ? File.ReadAllBytes(memoryPath) : null;
            return (before == null) == (after == null) && (before == null || System.Linq.Enumerable.SequenceEqual(before, after));
        }

        protected void KillBase(int team)
        {
            foreach (var u in new List<Unit>(world.units))
                if (Unit.Live(u) && u.team == team && (u.def.building || u.def.builder)) world.Damage(u, 1e7f, u.pos, null);
        }

        protected virtual void OnDisable()
        {
            // Whatever happened, the player's own memory goes back.
            if (memoryBackup != null && File.Exists(memoryBackup)) { File.Copy(memoryBackup, memoryPath, true); File.Delete(memoryBackup); }
            else if (memoryPath != null && !hadMemory && File.Exists(memoryPath)) File.Delete(memoryPath);
            memoryBackup = null;
            MatchSettings.spectate = false;
        }
    }

    public sealed class CheatLoseFirstTrial : CheatTrialBase
    {
        IEnumerator Start()
        {
            yield return Begin(true, false);
            if (!Ready) yield break;
            var sb = new StringBuilder();
            using (var errors = new RosterTrials.ErrorCount())
            {
                world.Cheat(0);
                yield return new WaitForSecondsRealtime(0.5f);
                KillBase(0);
                float t0 = world.time;
                while (world.winner < 0 && world.time - t0 < 5f) yield return null;
                yield return new WaitForSecondsRealtime(1f);
                var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
                sb.AppendLine($"winner {world.winner}, reckoning {world.Reckoning}, animals {world.avengers.Count}; \"{root.Q<Label>("endTitle").text}\" / \"{root.Q<Label>("endSubtitle").text}\"");
                sb.AppendLine($"the memory file {(MemoryUntouched() ? "untouched" : "CHANGED")}");
                sb.Append($"errors {errors.count}" + (errors.first != null ? " first: " + errors.first.Split('\n')[0] : ""));
            }
            report = sb.ToString();
            OnDisable();
        }
    }

    public sealed class CheatMemoryControlTrial : CheatTrialBase
    {
        IEnumerator Start()
        {
            yield return Begin(true, false);
            if (!Ready) yield break;
            var sb = new StringBuilder();
            using (var errors = new RosterTrials.ErrorCount())
            {
                yield return new WaitForSecondsRealtime(0.5f);
                KillBase(1);
                float t0 = world.time;
                while (world.winner < 0 && world.time - t0 < 5f) yield return null;
                yield return new WaitForSecondsRealtime(1f);
                var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
                sb.AppendLine($"no cheat: winner {world.winner}, reckoning {world.Reckoning}; \"{root.Q<Label>("endTitle").text}\"; the memory file {(MemoryUntouched() ? "untouched (the check proves nothing)" : "written, as it should be")}");
                sb.Append($"errors {errors.count}" + (errors.first != null ? " first: " + errors.first.Split('\n')[0] : ""));
            }
            report = sb.ToString();
            OnDisable();
        }
    }

    public sealed class CheatLateTrial : CheatTrialBase
    {
        IEnumerator Start()
        {
            yield return Begin(false, true);
            if (!Ready) yield break;
            var sb = new StringBuilder();
            using (var errors = new RosterTrials.ErrorCount())
            {
                Time.timeScale = 8f;
                // Both Mechs down and fighting, or as late as a match gets.
                while (world.winner < 0 && world.time < 720f)
                {
                    if (world.time > 420f && world.factions[0].mech != null && world.factions[1].mech != null) break;
                    yield return null;
                }
                int Count(int t, bool mobile)
                {
                    int n = 0;
                    foreach (var u in world.units) if (Unit.Live(u) && u.team == t && !u.def.neutral && u.def.building != mobile) n++;
                    return n;
                }
                sb.AppendLine($"at {world.time:0} s: winner {world.winner}; team 0 {Count(0, true)} units, {Count(0, false)} structures, Mech {(Unit.Live(world.factions[0].mech) ? "up" : "none")}; team 1 {Count(1, true)} units, {Count(1, false)} structures, Mech {(Unit.Live(world.factions[1].mech) ? "up" : "none")}");
                if (world.winner >= 0) { report = sb + "decided before the staging; errors " + errors.count; yield break; }
                world.Cheat(0);
                var ourMech = world.factions[0].mech;
                KillBase(1);
                float worstFrame = 0f, t0 = world.time;
                int frames = 0;
                while (!world.Reckoning && world.winner < 0 && frames++ < 10) yield return null;
                worstFrame = Time.unscaledDeltaTime * 1000f;
                yield return null;
                worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime * 1000f);
                int deserted = 0, loyal = 0;
                foreach (var u in world.units) if (Unit.Live(u) && u.team == 0 && !u.def.building && !u.def.neutral && !u.def.Autonomous) { if (u.deserted) deserted++; else loyal++; }
                sb.AppendLine($"their base down: reckoning {world.Reckoning}, {deserted} deserted of {deserted + loyal}, animals {world.avengers.Count}, frame at the turn {worstFrame:0} ms; their army {Count(1, true)} left, its Commander still running");
                float mechFell = -1f;
                int enemyLeft = Count(1, true);
                while (world.winner < 0 && world.time - t0 < 400f)
                {
                    if (mechFell < 0f && !Unit.Live(ourMech)) mechFell = world.time - t0;
                    yield return null;
                }
                Time.timeScale = 1f;
                sb.AppendLine($"over after {world.time - t0:0} game s: winner {world.winner}; our Mech {(mechFell >= 0f ? $"down {mechFell:0} s in" : Unit.Live(ourMech) ? "still up" : "had none")}; their army {enemyLeft} -> {Count(1, true)}; deserters left {Count0Deserted()}");
                sb.Append($"errors {errors.count}" + (errors.first != null ? " first: " + errors.first.Split('\n')[0] : ""));
            }
            report = sb.ToString();
            OnDisable();
        }

        int Count0Deserted()
        {
            int n = 0;
            foreach (var u in world.units) if (Unit.Live(u) && u.team == 0 && u.deserted) n++;
            return n;
        }
    }

    /// <summary>On a match of its own with the AI's memory on, as a player's is (BugTrial.EnsureMatch turns
    /// it off, and a memory that never learns would prove nothing; MemoryControl is the control). The
    /// opponent's Commander is left running, as it would be.</summary>
    public sealed class CheatReckoningTrial : CheatTrialBase
    {
        IEnumerator Start()
        {
            var boot = GameBootstrap.Instance;
            yield return Begin(true, false);
            if (!Ready) yield break;

            var sb = new StringBuilder();
            using (var errors = new RosterTrials.ErrorCount())
            {
                const int Me = 0;
                var F = world.factions[Me];
                var hud = Object.FindAnyObjectByType<UIDocument>();
                var label = hud != null ? hud.rootVisualElement.Q<Label>("cheatLabel") : null;
                bool Shown() => label != null && label.resolvedStyle.display != DisplayStyle.None;
                yield return new WaitForSecondsRealtime(0.2f);
                sb.AppendLine($"before the key: label shown {Shown()}, cheated {world.cheated}");

                // The key: ore, supply, training x5; again: more of both, training back.
                int ore0 = F.ore, cap0 = world.SupplyCap(Me);
                world.Cheat(Me);
                yield return new WaitForSecondsRealtime(0.2f);
                sb.AppendLine($"first press: ore +{F.ore - ore0}, supply cap {cap0} -> {world.SupplyCap(Me)}, rate {world.TrainRate(Me)}, label shown {Shown()} \"{label?.text}\" at {label?.resolvedStyle.fontSize:0} px");

                // What the rate does: one Trooper from a Garrison, timed.
                var garrison = world.Spawn(UnitType.Garrison, Me, world.NearestWalkable(map.StartPos(Me) + (map.StartPos(1) - map.StartPos(Me)).normalized * 22f, 12f), true);
                float fast = 0f, slow = 0f;
                yield return Timed(garrison, s => fast = s);
                int ore1 = F.ore;
                world.Cheat(Me);
                sb.AppendLine($"second press: ore +{F.ore - ore1}, supply cap {world.SupplyCap(Me)}, rate {world.TrainRate(Me)}");
                yield return Timed(garrison, s => slow = s);
                sb.AppendLine($"a Trooper ({Defs.Get(UnitType.Trooper).buildTime:0} s) trained in {fast:0.0} s fast, {slow:0.0} s after");
                // Past the ceiling, on the cheat's supply.
                int used = F.supplyUsed;
                F.supplyUsed = GameWorld.MaxSupply;
                F.ore += 200;
                bool over = world.CmdTrain(garrison, UnitType.Trooper);
                sb.AppendLine($"at the {GameWorld.MaxSupply} ceiling with {world.SupplyCap(Me)} cap: train {(over ? "accepted" : "refused")}");
                F.supplyUsed = used + (over ? Defs.Get(UnitType.Trooper).supplyCost : 0);
                world.Cheat(Me);    // fast again, as a player leaves it

                // An army, all kinds of it.
                Vector2 home = map.StartPos(Me);
                var mine = new List<Unit>();
                void Add(UnitType t, int n)
                {
                    for (int i = 0; i < n; i++)
                    {
                        float a = mine.Count * 2.399f;
                        mine.Add(world.Spawn(t, Me, world.NearestReachable(home, home + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (14f + mine.Count * 0.35f)), true));
                    }
                }
                Add(UnitType.Trooper, 14); Add(UnitType.Mauler, 4); Add(UnitType.Scorcher, 4); Add(UnitType.Skitter, 3); Add(UnitType.Kestrel, 3);
                // And a base of a fair size, a match's end rather than its start.
                var built = new[] { UnitType.Bunkhouse, UnitType.Bunkhouse, UnitType.Bunkhouse, UnitType.Sentinel, UnitType.Sentinel, UnitType.Workshop, UnitType.Armory, UnitType.Airfield, UnitType.Garrison, UnitType.Biolab };
                for (int i = 0; i < built.Length; i++)
                {
                    float a = i * 2.399f + 1f;
                    world.Spawn(built[i], Me, world.NearestWalkable(home + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (24f + i * 2.5f), 10f), true);
                }
                // And something on ground no path joins, if the map has any: the animals must get there too.
                Unit cutOff = null;
                var way = new UnityEngine.AI.NavMeshPath();
                var pick = new System.Random(11);
                for (int tries = 0; tries < 300 && cutOff == null; tries++)
                {
                    Vector2 p = world.NearestWalkable(new Vector2((float)pick.NextDouble() * world.MapSize, (float)pick.NextDouble() * world.MapSize), 8f);
                    if (!map.InBounds(p, 12f) || !UnityEngine.AI.NavMesh.SamplePosition(map.Ground(p), out var there, 2f, GameWorld.GroundAreas)) continue;
                    if (!UnityEngine.AI.NavMesh.SamplePosition(map.Ground(home), out var from, 8f, GameWorld.GroundAreas)) continue;
                    UnityEngine.AI.NavMesh.CalculatePath(from.position, there.position, GameWorld.GroundAreas, way);
                    if (way.status != UnityEngine.AI.NavMeshPathStatus.PathComplete) cutOff = world.Spawn(UnitType.Bunkhouse, Me, p, true);
                }
                bool hadCutOff = cutOff != null;
                sb.AppendLine(hadCutOff ? $"a Bunkhouse on cut-off ground at {cutOff.pos}" : "no cut-off ground found on this map");
                yield return new WaitForSecondsRealtime(1f);
                int mobile = 0, walkers = 0;
                foreach (var u in world.units)
                    if (Unit.Live(u) && u.team == Me && !u.def.building && !u.def.neutral && !u.def.Autonomous)
                    {
                        mobile++;
                        if (!u.def.flying) walkers++;
                    }

                // Their last structure and Digger fall: the win it would have been.
                var rig = Object.FindAnyObjectByType<RTSCamera>();
                foreach (var u in new List<Unit>(world.units))
                    if (Unit.Live(u) && u.team == 1 && (u.def.building || u.def.builder)) world.Damage(u, 1e7f, u.pos, null);
                float t0 = world.time;
                while (!world.Reckoning && world.winner < 0 && world.time - t0 < 3f) yield return null;
                sb.AppendLine($"their base down at {world.time:0}: winner {world.winner}, state {boot.State}, reckoning {world.Reckoning} on team {world.ReckoningTeam}");
                if (!world.Reckoning) { report = sb + "NO RECKONING"; yield break; }
                yield return null;
                yield return null;
                int deserters = 0;
                foreach (var u in world.units) if (Unit.Live(u) && u.team == Me && u.deserted) deserters++;
                int ground = 0, air = 0;
                foreach (var a in world.avengers) { if (a.flies) air++; else ground++; }
                sb.AppendLine($"of {mobile} units ({walkers} walk): {deserters} deserted ({100f * deserters / Mathf.Max(1, mobile):0}%, target {GameWorld.ReckoningDeserters * 100f:0}%); animals {world.avengers.Count} ({ground} on the ground, {air} in the air)");
                yield return new WaitForSecondsRealtime(0.3f);
                sb.AppendLine($"label now {label?.resolvedStyle.fontSize:0} px");

                // Follow it through; nothing else touches deserters' health here but the enemy's
                // leftovers, so hp is read from what the animals did.
                var hpAt = new Dictionary<Unit, float>();
                foreach (var u in world.units) if (Unit.Live(u) && u.team == Me && u.deserted) hpAt[u] = u.hp;
                int bites = 0, biteOnDeserter = 0;
                var lastTarget = new Dictionary<Avenger, Unit>();
                ListenWhileHeld(e => { if (e.kind == GameEventKind.AnimalBite) bites++; });
                boot.SetSpeed(3f);
                float minDist = float.MaxValue, firstBite = -1f;
                var opacities = new HashSet<int>();
                int shots = 0;
                t0 = world.time;
                float nextShot = 0f;
                while (world.winner < 0 && world.time - t0 < 300f)
                {
                    foreach (var a in world.avengers)
                    {
                        if (a.target != null && a.target.deserted) biteOnDeserter++;
                        if (a.target != null) minDist = Mathf.Min(minDist, (a.target.pos - a.pos).magnitude);
                    }
                    if (firstBite < 0f && bites > 0)
                    {
                        firstBite = world.time - t0;
                        foreach (var a in world.avengers)
                            if (a.bitAt >= world.time - 0.5f) { rig.JumpTo(a.pos); break; }
                    }
                    if (label != null) opacities.Add(Mathf.RoundToInt(label.resolvedStyle.opacity * 10f));
                    if (bites > 0 && Time.unscaledTime >= nextShot && shots < 4)
                    {
                        nextShot = Time.unscaledTime + 1.2f;
                        foreach (var a in world.avengers)
                            if (a.bitAt >= world.time - 1f) { rig.JumpTo(a.pos); break; }
                        rig.ZoomTo(36f);
                        ScreenCapture.CaptureScreenshot($"Temp/reckoning_{shots++}.png", 2);
                    }
                    yield return null;
                }
                ListenWhileHeld(null);
                int stillDeserted = 0, deserterHurt = 0;
                foreach (var kv in hpAt)
                    if (Unit.Live(kv.Key)) { stillDeserted++; if (kv.Key.hp < kv.Value - 0.5f) deserterHurt++; }
                sb.AppendLine($"first bite {firstBite:0.0} s in, {bites} bites; ended after {world.time - t0:0} s, winner {world.winner}, state {boot.State}; the cut-off Bunkhouse {(!hadCutOff ? "(none on this map)" : Unit.Live(cutOff) ? "ALIVE" : "down")}");
                sb.AppendLine($"frames an animal was after a deserter {biteOnDeserter}; deserters alive {stillDeserted} of {hpAt.Count}, hurt {deserterHurt} (their army's leftovers may do that)");
                yield return new WaitForSecondsRealtime(1.2f);
                var title = hud != null ? hud.rootVisualElement.Q<Label>("endTitle") : null;
                var sub = hud != null ? hud.rootVisualElement.Q<Label>("endSubtitle") : null;
                sb.AppendLine($"end screen: \"{title?.text}\" / \"{sub?.text}\"; label shown {Shown()} at {label?.resolvedStyle.fontSize:0} px, opacity steps seen {opacities.Count}");
                ScreenCapture.CaptureScreenshot("Temp/reckoning_end.png", 2);
                yield return new WaitForSecondsRealtime(0.5f);

                sb.AppendLine($"memory on {MatchSettings.aiMemory}; the file {(MemoryUntouched() ? "untouched" : "CHANGED")}");
                sb.Append($"errors {errors.count}" + (errors.first != null ? " first: " + errors.first.Split('\n')[0] : ""));
            }
            report = sb.ToString();
            OnDisable();
        }

        IEnumerator Timed(Unit building, System.Action<float> done)
        {
            var F = world.factions[building.team];
            F.ore += 200;
            int count = world.factions[building.team].unitsProduced;
            building.queue.Clear();
            world.CmdTrain(building, UnitType.Trooper);
            float t0 = world.time;
            while (F.unitsProduced == count && world.time - t0 < 40f) yield return null;
            done(world.time - t0);
        }
    }
}
