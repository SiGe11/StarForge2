// BugTrials.cs — staged checks for fixes to the rules, run in play mode:
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.Targeting   (then TargetingReport)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.Harvest     (then HarvestReport)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.PauseSpeed  (answers at once)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.Cards       (answers at once)
//   Tools/editor.sh call StarForge.EditorTools.BugTrials.Quality     (answers at once; title screen)
// Each silences the opponent AI while it runs (Commander.Suspended) and switches it
// back when it ends, and leaves the match running.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.AI;
using StarForge.Game;
using StarForge.Sim;
using StarForge.View;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class BugTrials
    {
        /// <summary>A Sentinel and a holding Trooper of team 1 pick up a Skimmer of team 0
        /// parked in their sight but out of their reach; then a Trooper of team 0 walks up
        /// and shoots the tower. Run twice: auto-acquired targets kept until they die (as
        /// before) and let go for an enemy in reach. Reports what each fired at.</summary>
        public static string Targeting() => Stage<TargetingTrial>("Targeting");
        public static string TargetingReport() => Report<TargetingTrial>();

        /// <summary>Team 0's Foundry is destroyed with its Diggers at work: how much ore they
        /// take from the nodes with nowhere to bring it, and whether they bring it in once a
        /// Foundry stands again.</summary>
        public static string Harvest() => Stage<HarvestTrial>("Harvest");
        public static string HarvestReport() => Report<HarvestTrial>();

        /// <summary>The game speed survives a pause: x2, pause, resume.</summary>
        public static string PauseSpeed()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.State != MatchState.Playing) return "no match playing";
            float was = boot.Speed;
            boot.SetSpeed(2f);
            boot.TogglePause();
            float paused = Time.timeScale;
            boot.TogglePause();
            float resumed = Time.timeScale;
            boot.SetSpeed(was);
            return $"x2, paused {paused:0.##}, resumed {resumed:0.##} (want 0 and 2); back to x{Time.timeScale:0.##}";
        }

        /// <summary>The command card for Diggers with a finished Mech Bay, the bay alone,
        /// and a Digger ordered from the minimap and the card while the game is paused.</summary>
        public static string Cards()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var boot = GameBootstrap.Instance;
            var world = GameWorld.Instance;
            var pc = Object.FindAnyObjectByType<PlayerController>();
            if (boot == null || world == null || pc == null || boot.State != MatchState.Playing) return "no match playing";
            Unit digger = null;
            foreach (var u in world.units)
                if (Unit.Live(u) && u.team == pc.team && u.Type == UnitType.Worker) { digger = u; break; }
            if (digger == null) return "no Digger";
            var home = world.Map.StartPos(pc.team);
            Vector2 spot = home;
            bool found = false;
            for (int i = 0; i < 48 && !found; i++)
            {
                float a = i * 0.7f, r = 14f + i * 0.5f;
                spot = home + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                found = world.CanPlace(UnitType.MechBay, spot);
            }
            if (!found) return "no room for a bay";
            var bay = world.Spawn(UnitType.MechBay, pc.team, spot, true);
            var cards = new List<CardAction>();
            var sb = new StringBuilder();
            try
            {
                pc.Selection.Clear(); pc.Selection.Add(digger); pc.Selection.Add(bay);
                pc.BuildCard(cards);
                sb.AppendLine($"Digger + bay: {cards.Count} cards (12 slots): {Kinds(cards)}");
                pc.Selection.Clear(); pc.Selection.Add(bay);
                pc.BuildCard(cards);
                sb.AppendLine($"bay alone: {cards.Count} cards: {Kinds(cards)}");

                pc.Selection.Clear(); pc.Selection.Add(digger);
                var before = digger.order;
                var beforePos = digger.orderPos;
                boot.TogglePause();
                pc.MinimapCommand(digger.pos + new Vector2(20f, 0f));
                pc.Execute(new CardAction { kind = CardKind.Stop });
                bool changed = digger.order != before || digger.orderPos != beforePos;
                boot.TogglePause();
                sb.AppendLine($"paused: minimap move and Stop {(changed ? "CHANGED the Digger's order" : "were ignored")} ({before} kept)");
            }
            finally
            {
                if (boot.State == MatchState.Paused) boot.TogglePause();
                pc.Selection.Clear();
                bay.BeginDeath();
            }
            return sb.ToString();
        }

        /// <summary>A preset picked on the title screen changes the grass there and then:
        /// the instances grown under each preset, then the player's own put back.</summary>
        public static string Quality()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var scatter = Object.FindAnyObjectByType<GroundScatter>();
            if (scatter == null) return "no GroundScatter";
            var was = QualityController.Current;
            var sb = new StringBuilder();
            try
            {
                foreach (var q in new[] { SFQuality.High, SFQuality.Balanced, SFQuality.Battery })
                {
                    QualityController.Current = q;
                    sb.Append($"{q} {scatter.InstanceCount} instances (density {scatter.density:0.##}); ");
                }
            }
            finally { QualityController.Current = was; }
            sb.Append($"back to {was}: {scatter.InstanceCount}");
            return sb.ToString();
        }

        static string Kinds(List<CardAction> cards)
        {
            var parts = new List<string>();
            foreach (var c in cards) parts.Add(c.kind == CardKind.Build || c.kind == CardKind.Train ? $"{c.kind} {c.type}" : c.kind == CardKind.MechUpgrade ? $"{c.upgrade}" : c.kind.ToString());
            return string.Join(", ", parts);
        }

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
    }

    public abstract class BugTrial : MonoBehaviour
    {
        public string report = "staging";

        protected GameWorld world;
        protected MapInfo map;

        protected IEnumerator EnsureMatch()
        {
            var boot = GameBootstrap.Instance;
            world = GameWorld.Instance;
            map = MapInfo.Instance;
            if (boot != null && world != null && !world.running)
            {
                MatchSettings.aiMemory = false;
                MatchSettings.spectate = false;
                boot.StartMatch();
                yield return new WaitForSecondsRealtime(1.5f);
            }
        }

        protected bool Ready => world != null && map != null && world.running;

        /// <summary>Wait this much match time.</summary>
        protected IEnumerator Wait(float seconds)
        {
            float until = world.time + seconds;
            while (world.time < until) yield return null;
        }

        /// <summary>Dry, open ground away from both bases, with room for a Sentinel.</summary>
        protected bool FindField(System.Random rng, out Vector2 at, out Vector2 dir)
        {
            at = dir = default;
            for (int tries = 0; tries < 200; tries++)
            {
                var b = new Vector2((float)rng.NextDouble() * world.MapSize, (float)rng.NextDouble() * world.MapSize);
                if (!map.InBounds(b, 40f) || map.WaterDepth(b) > -0.3f) continue;
                if ((b - map.StartPos(0)).magnitude < 60f || (b - map.StartPos(1)).magnitude < 60f) continue;
                if (!world.CanPlace(UnitType.Sentinel, b)) continue;
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                bool open = true;
                foreach (float r in new[] { -34f, -12f, 4f, 25f })
                    if (!world.Walkable(b + d * r, 1f)) { open = false; break; }
                if (!open || !world.Walkable(b + new Vector2(-d.y, d.x) * 4f, 1f)) continue;
                at = b; dir = d;
                return true;
            }
            return false;
        }

        protected static void Remove(Unit u)
        {
            if (Unit.Live(u)) u.BeginDeath();
        }
    }

    public sealed class TargetingTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            bool wasSuspended = Commander.Suspended, wasSticky = Unit.StickyTargets;
            float scale = Time.timeScale;
            var sb = new StringBuilder();
            var rng = new System.Random(4711);
            try
            {
                Commander.Suspended = true;
                Time.timeScale = 3f;
                foreach (bool sticky in new[] { true, false })
                {
                    Unit.StickyTargets = sticky;
                    if (!FindField(rng, out var b, out var d)) { sb.AppendLine("no open field found"); break; }
                    var tower = world.Spawn(UnitType.Sentinel, 1, b, true);
                    var holder = world.Spawn(UnitType.Trooper, 1, b + new Vector2(-d.y, d.x) * 4f);
                    world.CmdHold(new List<Unit> { holder });
                    var scout = world.Spawn(UnitType.Skimmer, 0, b + d * 25f);
                    world.CmdHold(new List<Unit> { scout });
                    yield return Wait(3f);
                    string locked = $"tower on {Name(tower.target, scout)}, holder on {Name(holder.target, scout)}";

                    var attacker = world.Spawn(UnitType.Trooper, 0, b - d * 34f);
                    world.CmdMove(new List<Unit> { attacker }, b - d * 12f, false);
                    int towerAtAttacker = 0, towerOther = 0, holderAtAttacker = 0, holderOther = 0;
                    System.Action<GameEvent> onFire = e =>
                    {
                        if (e.kind != GameEventKind.Fire) return;
                        if (e.unit == tower) { if (tower.target == attacker) towerAtAttacker++; else towerOther++; }
                        if (e.unit == holder) { if (holder.target == attacker) holderAtAttacker++; else holderOther++; }
                    };
                    world.Event += onFire;
                    float t0 = world.time, towerHp = tower.hp, killedAt = -1f;
                    while (world.time - t0 < 20f)
                    {
                        if (killedAt < 0f && !Unit.Live(attacker)) killedAt = world.time - t0;
                        yield return null;
                    }
                    world.Event -= onFire;
                    sb.AppendLine($"{(sticky ? "kept to the death (before)" : "let go for one in reach (after)")}: {locked}");
                    sb.AppendLine($"  tower fired {towerAtAttacker} at the attacker, {towerOther} elsewhere; " +
                                  $"holder {holderAtAttacker} at the attacker, {holderOther} elsewhere; " +
                                  $"attacker {(killedAt >= 0f ? $"down after {killedAt:0.0} s" : $"alive at {attacker.hp:0}/{attacker.MaxHp:0} hp")}; " +
                                  $"tower lost {towerHp - tower.hp:0} hp");
                    Remove(tower); Remove(holder); Remove(scout); Remove(attacker);
                    yield return Wait(3f);
                }
            }
            finally
            {
                Commander.Suspended = wasSuspended;
                Unit.StickyTargets = wasSticky;
                Time.timeScale = scale;
            }
            report = sb.ToString();
        }

        static string Name(Unit t, Unit scout) => t == null ? "nothing" : t == scout ? "the parked Skimmer" : t.def.displayName;
    }

    public sealed class HarvestTrial : BugTrial
    {
        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            bool wasSuspended = Commander.Suspended;
            float scale = Time.timeScale;
            var sb = new StringBuilder();
            try
            {
                Commander.Suspended = true;
                Time.timeScale = 3f;
                if (world.time < 20f) yield return Wait(20f - world.time);
                Unit foundry = null;
                foreach (var u in world.units)
                    if (Unit.Live(u) && u.team == 0 && u.Type == UnitType.Foundry) { foundry = u; break; }
                if (foundry == null) { report = "team 0 has no Foundry"; yield break; }
                Vector2 at = foundry.pos;
                float yaw = foundry.yaw;
                var F = world.factions[0];
                sb.AppendLine($"t {world.time:0}s: Foundry destroyed; nodes near it hold {OreNear(at)}, {Diggers()}");
                world.Damage(foundry, 1e7f, foundry.pos, null);
                int oreBefore = OreNear(at);
                for (int k = 0; k < 4; k++)
                {
                    yield return Wait(10f);
                    sb.AppendLine($"  +{10 * (k + 1)}s: nodes {OreNear(at)} ({oreBefore - OreNear(at)} taken), side's ore {F.ore}, {Diggers()}");
                }
                var rebuilt = world.Spawn(UnitType.Foundry, 0, at, true, yaw);
                int bankBefore = F.ore;
                yield return Wait(25f);
                sb.AppendLine($"Foundry back: {F.ore - bankBefore} ore brought in over 25 s, {Diggers()}");
                if (!Unit.Live(rebuilt)) sb.AppendLine("  (the rebuilt Foundry did not stand)");
            }
            finally
            {
                Commander.Suspended = wasSuspended;
                Time.timeScale = scale;
            }
            report = sb.ToString();
        }

        int OreNear(Vector2 at)
        {
            int sum = 0;
            foreach (var u in world.units)
                if (Unit.Live(u) && u.Type == UnitType.Ore && (u.pos - at).magnitude < 45f) sum += u.oreLeft;
            return sum;
        }

        string Diggers()
        {
            var counts = new Dictionary<string, int>();
            int carrying = 0, n = 0;
            foreach (var u in world.units)
            {
                if (!Unit.Live(u) || u.team != 0 || u.Type != UnitType.Worker) continue;
                n++;
                carrying += u.carrying;
                string k = u.order.ToString();
                counts.TryGetValue(k, out int c);
                counts[k] = c + 1;
            }
            var parts = new List<string>();
            foreach (var kv in counts) parts.Add($"{kv.Value} {kv.Key}");
            return $"{n} Diggers ({string.Join(", ", parts)}) carrying {carrying}";
        }
    }
}
