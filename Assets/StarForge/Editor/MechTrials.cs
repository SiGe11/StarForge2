// MechTrials.cs — play-mode checks for the Mech, called through the agent inbox
// (Tools/editor.sh call StarForge.EditorTools.MechTrials.<Method>).
//
//   Watch / WatchReport      an AI-vs-AI match at speed, logging both sides' Mech Bays,
//                            Mechs, upgrades and what the Mechs said and were heeded
//   Duel / DuelReport        a Mech against N Maulers or Troopers on open ground, fought
//                            to the end, for the balance sheet (MechParts)
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
    public static class MechTrials
    {
        public static string Watch()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<MechWatcher>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject("MechWatch").AddComponent<MechWatcher>();
            return "watching; call WatchReport";
        }

        public static string WatchReport()
        {
            var w = Object.FindAnyObjectByType<MechWatcher>();
            return w == null ? "no watch running" : w.log.ToString() + "\n" + MechWatcher.Now();
        }

        /// <summary>One line per side: bay, Mech, upgrades, advice.</summary>
        public static string State()
        {
            var world = GameWorld.Instance;
            if (world == null || !world.running) return "no match";
            return MechWatcher.Now();
        }
    }

    public sealed class MechWatcher : MonoBehaviour
    {
        public readonly StringBuilder log = new StringBuilder();
        float next;
        readonly List<string> heard = new List<string>();

        IEnumerator Start()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null) { log.AppendLine("no bootstrap"); yield break; }
            if (GameWorld.Instance == null || !GameWorld.Instance.running)
            {
                MatchSettings.spectate = true;
                MatchSettings.aiMemory = false;
                boot.StartMatch();
                yield return null;
            }
            Time.timeScale = 6f;
            GameWorld.Instance.Event += OnEvent;
        }

        void OnDestroy()
        {
            if (GameWorld.Instance != null) GameWorld.Instance.Event -= OnEvent;
        }

        void OnEvent(GameEvent e)
        {
            var w = GameWorld.Instance;
            switch (e.kind)
            {
                case GameEventKind.MechAdvice:
                    log.AppendLine($"[{Clock(w.time)}] T{e.team} {e.advice}: {e.text}");
                    break;
                case GameEventKind.MechLanded:
                    log.AppendLine($"[{Clock(w.time)}] T{e.team} MECH LANDED at {e.pos.x:0},{e.pos.z:0}: {w.factions[e.team].design}");
                    break;
                case GameEventKind.UpgradeComplete:
                    log.AppendLine($"[{Clock(w.time)}] T{e.team} {e.text}");
                    break;
                case GameEventKind.Death when e.type == UnitType.Mech || e.type == UnitType.MechBay:
                    log.AppendLine($"[{Clock(w.time)}] T{e.team} {e.type} DESTROYED");
                    break;
                case GameEventKind.StructureComplete when e.type == UnitType.MechBay:
                    log.AppendLine($"[{Clock(w.time)}] T{e.team} Mech Bay complete");
                    break;
            }
        }

        void Update()
        {
            var w = GameWorld.Instance;
            if (w == null || !w.running) return;
            if (w.time < next) return;
            next = w.time + 20f;
            log.AppendLine(Now());
            if (w.winner >= 0 && !heardEnd) { heardEnd = true; log.AppendLine($"WINNER team {w.winner} at {Clock(w.time)}"); }
        }

        bool heardEnd;

        static string Clock(float t) => $"{(int)t / 60}:{(int)t % 60:00}";

        public static string Now()
        {
            var w = GameWorld.Instance;
            var boot = GameBootstrap.Instance;
            var sb = new StringBuilder();
            sb.Append($"[{Clock(w.time)}]");
            for (int t = 0; t < 2; t++)
            {
                var F = w.factions[t];
                var bay = w.BayOf(t);
                var m = w.MechOf(t);
                string bayS = bay == null ? (F.bayLost ? "bay LOST" : F.bayPlaced ? "bay?" : "no bay") : bay.Complete ? $"bay {bay.hp:0}hp" : $"bay {bay.buildProgress * 100f:0}%";
                string mechS = m == null ? (F.mechLost ? "mech LOST" : F.mechDropAt > 0 ? $"drop in {F.mechDropAt - w.time:0}s" : "no mech")
                    : $"mech {m.hp:0}/{m.MaxHp:0} sh{m.mech.shield:0} k{m.kills} {m.mech.intent} '{m.mech.intentText}' at {m.pos.x:0},{m.pos.y:0}";
                string ups = string.Join("", F.upgrades);
                var brain = boot != null ? boot.MechBrains[t] : null;
                var cmd = boot == null ? null : t == 1 ? boot.AI : boot.PlayerProxy;
                int army = 0;
                foreach (var u in w.units) if (u != null && !u.dying && u.team == t && u.def.IsArmy) army++;
                sb.Append($"\n  T{t}: ore {F.ore} army {army} lost {F.lost} | {bayS} | {mechS} | ups {ups}" +
                          (F.researching >= 0 ? $" (researching {(MechUpgrade)F.researching})" : "") +
                          (brain != null ? $" | said {brain.AdviceGiven}, apm {brain.Budget.MeasuredAPM():0}" : "") +
                          (cmd != null ? $" | cmd {cmd.Dbg.strategy} heeded {cmd.Dbg.adviceHeeded}/ignored {cmd.Dbg.adviceIgnored} upg {cmd.Dbg.upgradesBought}; {cmd.Dbg.wave}" : ""));
            }
            return sb.ToString();
        }
    }
}

namespace StarForge.EditorTools
{
    public static class MechKitDebug
    {
        /// <summary>The imported hierarchy of the biped legs and a frame, with local transforms.</summary>
        public static string Hierarchy()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var name in new[] { "MECH_LEGS_BIPED", "WORKER" })
            {
                var src = ModelFactory.LoadSource(name);
                if (src == null) { sb.AppendLine(name + " missing"); continue; }
                Dump(src.transform, 0, sb);
            }
            return sb.ToString();
        }

        static void Dump(UnityEngine.Transform t, int depth, System.Text.StringBuilder sb)
        {
            var mf = t.GetComponent<UnityEngine.MeshFilter>();
            string b = mf != null && mf.sharedMesh != null ? $" mesh c{mf.sharedMesh.bounds.center} s{mf.sharedMesh.bounds.size}" : "";
            sb.AppendLine($"{new string(' ', depth * 2)}{t.name} p{t.localPosition} r{t.localEulerAngles} s{t.localScale}{b}");
            foreach (UnityEngine.Transform c in t) Dump(c, depth + 1, sb);
        }
    }
}

namespace StarForge.EditorTools
{
    /// <summary>Photographs the Mechs: all four locomotions in a row, walking, and one
    /// coming down from orbit (Temp/mech_*.png).</summary>
    public sealed class MechShowcase : UnityEngine.MonoBehaviour
    {
        public string report = "staging";

        public static string Run()
        {
            if (!UnityEditor.EditorApplication.isPlaying) return "not playing";
            new UnityEngine.GameObject("MechShowcase").AddComponent<MechShowcase>();
            return "staging; call StarForge.EditorTools.MechShowcase.Report in ~40 s";
        }

        public static string Report()
        {
            var s = UnityEngine.Object.FindAnyObjectByType<MechShowcase>();
            return s == null ? "none" : s.report;
        }

        System.Collections.IEnumerator Start()
        {
            var boot = StarForge.Game.GameBootstrap.Instance;
            if (StarForge.World.GameWorld.Instance == null || !StarForge.World.GameWorld.Instance.running)
            {
                StarForge.Game.MatchSettings.aiMemory = false;
                StarForge.Game.MatchSettings.spectate = false;
                boot.StartMatch();
                yield return new UnityEngine.WaitForSecondsRealtime(1.5f);
            }
            var world = StarForge.World.GameWorld.Instance;
            var rig = UnityEngine.Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            var map = world.Map;
            // Flat, dry, open ground near the player's base, toward the middle.
            UnityEngine.Vector2 home = map.StartPos(0), mid = new UnityEngine.Vector2(world.MapSize * 0.5f, world.MapSize * 0.5f);
            UnityEngine.Vector2 best = home; float bestScore = -1e9f;
            for (int i = 0; i < 400; i++)
            {
                var p = UnityEngine.Vector2.Lerp(home, mid, UnityEngine.Random.Range(0.15f, 0.6f)) + UnityEngine.Random.insideUnitCircle * 30f;
                if (!map.InBounds(p, 20f) || map.WaterDepth(p) > -1f) continue;
                float lo = 1e9f, hi = -1e9f;
                for (int a = 0; a < 12; a++)
                {
                    float h = map.HeightAt(p + new UnityEngine.Vector2(UnityEngine.Mathf.Cos(a * 0.52f), UnityEngine.Mathf.Sin(a * 0.52f)) * 18f);
                    lo = UnityEngine.Mathf.Min(lo, h); hi = UnityEngine.Mathf.Max(hi, h);
                }
                int clutter = world.UnitsNear(p, 20f).Count;
                float score = -(hi - lo) * 3f - clutter * 5f;
                if (score > bestScore && world.Walkable(p)) { bestScore = score; best = p; }
            }
            rig.scripted = true;
            UnityEngine.Time.timeScale = 1f;
            var designs = new[]
            {
                Make(StarForge.World.MechLocomotion.Biped, StarForge.World.MechFrame.Medium, StarForge.World.MechUtility.Shield, StarForge.World.MechWeapon.Autocannon, StarForge.World.MechWeapon.Laser, StarForge.World.MechWeapon.Missiles, StarForge.World.MechWeapon.Missiles),
                Make(StarForge.World.MechLocomotion.Quad, StarForge.World.MechFrame.Heavy, StarForge.World.MechUtility.Sensors, StarForge.World.MechWeapon.Railgun, StarForge.World.MechWeapon.Gatling, StarForge.World.MechWeapon.Mortar, StarForge.World.MechWeapon.Missiles, StarForge.World.MechWeapon.Mortar),
                Make(StarForge.World.MechLocomotion.Tracks, StarForge.World.MechFrame.Light, StarForge.World.MechUtility.Nanites, StarForge.World.MechWeapon.Flamer, StarForge.World.MechWeapon.Gatling, StarForge.World.MechWeapon.Missiles),
                Make(StarForge.World.MechLocomotion.Hover, StarForge.World.MechFrame.Medium, StarForge.World.MechUtility.Reactive, StarForge.World.MechWeapon.Laser, StarForge.World.MechWeapon.Laser, StarForge.World.MechWeapon.Missiles, StarForge.World.MechWeapon.Mortar),
            };
            var mechs = new System.Collections.Generic.List<StarForge.World.Unit>();
            UnityEngine.Vector2 across = new UnityEngine.Vector2(1f, 0f), ahead = new UnityEngine.Vector2(0f, 1f);
            for (int i = 0; i < designs.Length; i++)
            {
                var at = world.NearestWalkable(best + across * (i - 1.5f) * 11f);
                mechs.Add(world.TrialMech(0, designs[i], at, 0f, false));
            }
            rig.JumpTo(best);
            rig.ZoomTo(60f);
            yield return new UnityEngine.WaitForSecondsRealtime(3f);
            UnityEngine.ScreenCapture.CaptureScreenshot("Temp/mech_row_rts.png");
            yield return new UnityEngine.WaitForSecondsRealtime(0.5f);
            rig.ZoomTo(30f);
            yield return new UnityEngine.WaitForSecondsRealtime(2f);
            UnityEngine.ScreenCapture.CaptureScreenshot("Temp/mech_row_close.png");
            yield return new UnityEngine.WaitForSecondsRealtime(0.5f);
            // Walk them forward and turn.
            foreach (var m in mechs) world.CmdMechMove(m, m.pos + ahead * 24f + across * 6f);
            for (float t = 0f; t < 2.6f; t += UnityEngine.Time.unscaledDeltaTime) { rig.JumpTo(mechs[0].pos + across * 14f); yield return null; }
            UnityEngine.ScreenCapture.CaptureScreenshot("Temp/mech_walk.png");
            yield return new UnityEngine.WaitForSecondsRealtime(0.3f);
            rig.ZoomTo(22f);
            for (float t = 0f; t < 1.5f; t += UnityEngine.Time.unscaledDeltaTime) { rig.JumpTo(mechs[0].pos); yield return null; }
            UnityEngine.ScreenCapture.CaptureScreenshot("Temp/mech_walk_close.png");
            yield return new UnityEngine.WaitForSecondsRealtime(3f);
            // A drop.
            var spot = world.NearestWalkable(best - ahead * 18f);
            rig.JumpTo(spot);
            rig.ZoomTo(45f);
            var dropper = world.TrialMech(0, designs[0], spot, 0f, true);
            float[] at_ = { 1.5f, 2.6f, 3.2f, 3.5f, 4.2f };
            string[] nm = { "drop_high", "drop_low", "drop_near", "drop_impact", "drop_after" };
            float t0 = UnityEngine.Time.time;
            for (int k = 0; k < at_.Length; k++)
            {
                while (UnityEngine.Time.time - t0 < at_[k]) yield return null;
                UnityEngine.ScreenCapture.CaptureScreenshot($"Temp/mech_{nm[k]}.png");
            }
            yield return new UnityEngine.WaitForSecondsRealtime(1f);
            var sb = new System.Text.StringBuilder();
            foreach (var m in mechs)
                sb.AppendLine($"{m.mech.design.className}: pos {m.pos} hp {m.hp:0}/{m.MaxHp:0} speed {m.mech.Speed:0.0} order {m.order}");
            report = "done\n" + sb;
        }

        static StarForge.World.MechDesign Make(StarForge.World.MechLocomotion l, StarForge.World.MechFrame f, StarForge.World.MechUtility u, params StarForge.World.MechWeapon[] w)
        {
            var d = new StarForge.World.MechDesign { locomotion = l, frame = f, utility = u, className = $"{f}-{l}", pilot = "Trial", callsign = "TRIAL", order = "trials" };
            d.weapons.AddRange(w);
            return d;
        }
    }
}

namespace StarForge.EditorTools
{
    /// <summary>Two Mechs (every weapon between them) opening up on a company of
    /// enemies, in slow motion, then a drop from orbit: Temp/mechfx_*.png.</summary>
    public sealed class MechCombatLook : UnityEngine.MonoBehaviour
    {
        public string report = "staging";

        public static string Run()
        {
            if (!UnityEditor.EditorApplication.isPlaying) return "not playing";
            new UnityEngine.GameObject("MechCombatLook").AddComponent<MechCombatLook>();
            return "staging; call StarForge.EditorTools.MechCombatLook.Report in ~40 s";
        }

        public static string Report()
        {
            var s = UnityEngine.Object.FindAnyObjectByType<MechCombatLook>();
            return s == null ? "none" : s.report;
        }

        System.Collections.IEnumerator Start()
        {
            var boot = StarForge.Game.GameBootstrap.Instance;
            if (StarForge.World.GameWorld.Instance == null || !StarForge.World.GameWorld.Instance.running)
            {
                StarForge.Game.MatchSettings.aiMemory = false;
                StarForge.Game.MatchSettings.spectate = true;
                boot.StartMatch();
                yield return new UnityEngine.WaitForSecondsRealtime(1.5f);
            }
            var world = StarForge.World.GameWorld.Instance;
            var rig = UnityEngine.Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            var map = world.Map;
            UnityEngine.Vector2 mid = new UnityEngine.Vector2(world.MapSize * 0.5f, world.MapSize * 0.5f);
            UnityEngine.Vector2 best = mid; float bestScore = -1e9f;
            for (int i = 0; i < 600; i++)
            {
                var p = mid + UnityEngine.Random.insideUnitCircle * world.MapSize * 0.35f;
                if (!map.InBounds(p, 30f) || map.WaterDepth(p) > -1f || !world.Walkable(p)) continue;
                float lo = 1e9f, hi = -1e9f;
                for (int a = 0; a < 12; a++)
                {
                    float h = map.HeightAt(p + new UnityEngine.Vector2(UnityEngine.Mathf.Cos(a * 0.52f), UnityEngine.Mathf.Sin(a * 0.52f)) * 24f);
                    lo = UnityEngine.Mathf.Min(lo, h); hi = UnityEngine.Mathf.Max(hi, h);
                }
                float score = -(hi - lo) * 3f - world.UnitsNear(p, 30f).Count * 6f;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            rig.scripted = true;
            UnityEngine.Time.timeScale = 1f;
            var a1 = new StarForge.World.MechDesign { locomotion = StarForge.World.MechLocomotion.Biped, frame = StarForge.World.MechFrame.Heavy, utility = StarForge.World.MechUtility.Shield, className = "A", pilot = "A", callsign = "A", order = "A" };
            a1.weapons.AddRange(new[] { StarForge.World.MechWeapon.Gatling, StarForge.World.MechWeapon.Laser, StarForge.World.MechWeapon.FlameTower, StarForge.World.MechWeapon.Mortar, StarForge.World.MechWeapon.Missiles });
            var a2 = new StarForge.World.MechDesign { locomotion = StarForge.World.MechLocomotion.Tracks, frame = StarForge.World.MechFrame.Medium, utility = StarForge.World.MechUtility.None, className = "B", pilot = "B", callsign = "B", order = "B" };
            a2.weapons.AddRange(new[] { StarForge.World.MechWeapon.Railgun, StarForge.World.MechWeapon.Flamer, StarForge.World.MechWeapon.Missiles, StarForge.World.MechWeapon.Missiles });
            var ahead = new UnityEngine.Vector2(0f, 1f);
            var m1 = world.TrialMech(0, a1, world.NearestWalkable(best - ahead * 12f + new UnityEngine.Vector2(-6f, 0f)), 0f, false);
            var m2 = world.TrialMech(0, a2, world.NearestWalkable(best - ahead * 12f + new UnityEngine.Vector2(7f, 0f)), 0f, false);
            rig.JumpTo(best - ahead * 4f);
            rig.ZoomTo(46f);
            yield return new UnityEngine.WaitForSecondsRealtime(2.5f);
            // The enemy: a line of troopers close, maulers further out.
            var enemies = new System.Collections.Generic.List<StarForge.World.Unit>();
            for (int i = 0; i < 10; i++)
                enemies.Add(world.Spawn(StarForge.Sim.UnitType.Trooper, 1, world.NearestWalkable(best + ahead * 6f + new UnityEngine.Vector2((i - 4.5f) * 2.2f, UnityEngine.Random.Range(-1f, 1f))), true, UnityEngine.Mathf.PI));
            for (int i = 0; i < 3; i++)
                enemies.Add(world.Spawn(StarForge.Sim.UnitType.Mauler, 1, world.NearestWalkable(best + ahead * 16f + new UnityEngine.Vector2((i - 1f) * 6f, 0f)), true, UnityEngine.Mathf.PI));
            world.CmdMove(enemies, best - ahead * 12f, true);
            UnityEngine.Time.timeScale = 0.35f;
            string[] names = { "open", "volley", "beams", "missiles", "rail", "late" };
            float[] waits = { 1.0f, 1.2f, 1.2f, 1.5f, 1.8f, 3.0f };
            for (int k = 0; k < names.Length; k++)
            {
                yield return new UnityEngine.WaitForSecondsRealtime(waits[k]);
                UnityEngine.ScreenCapture.CaptureScreenshot($"Temp/mechfx_{names[k]}.png", 3);
            }
            UnityEngine.Time.timeScale = 1f;
            yield return new UnityEngine.WaitForSecondsRealtime(4f);
            int alive = 0;
            foreach (var e in enemies) if (StarForge.World.Unit.Live(e)) alive++;
            string fired = "";
            foreach (var m in new[] { m1, m2 })
                foreach (var g in m.mech.guns) fired += $"{g.part.name}:{g.shots} ";
            // The drop.
            var spot = world.NearestWalkable(best + new UnityEngine.Vector2(22f, -18f));
            rig.JumpTo(spot);
            rig.ZoomTo(55f);
            yield return new UnityEngine.WaitForSecondsRealtime(1f);
            world.Raise(new StarForge.World.GameEvent { kind = StarForge.World.GameEventKind.MechInbound, team = 0, pos = map.Ground(spot) });
            yield return new UnityEngine.WaitForSecondsRealtime(4f);
            var dropper = world.TrialMech(0, a2, spot, 0f, true);
            float t0 = UnityEngine.Time.time;
            float[] at_ = { 1.2f, 2.4f, 3.0f, 3.42f, 3.7f, 5f };
            string[] nm = { "drop_a", "drop_b", "drop_c", "drop_impact", "drop_dust", "drop_after" };
            for (int k = 0; k < at_.Length; k++)
            {
                while (UnityEngine.Time.time - t0 < at_[k]) yield return null;
                UnityEngine.ScreenCapture.CaptureScreenshot($"Temp/mechfx_{nm[k]}.png", 3);
            }
            report = $"done: enemies left {alive}/{enemies.Count}; shots {fired}; m1 hp {m1.hp:0}/{m1.MaxHp:0} shield {m1.mech.shield:0}; m2 hp {m2.hp:0}/{m2.MaxHp:0}";
        }
    }
}

namespace StarForge.EditorTools
{
    /// <summary>The balance sheet's check: a Mech (no AI of its own: it stands and
    /// shoots) against N Maulers or N Troopers that attack-move onto it on open ground,
    /// fought to the end. For each design it looks for the smallest N that beats the
    /// Mech (a bisection), for both unit types. Aim: 20-25 Maulers, 30-40 Troopers.</summary>
    public sealed class MechDuel : UnityEngine.MonoBehaviour
    {
        public readonly System.Text.StringBuilder log = new System.Text.StringBuilder();
        public bool done;
        public static uint[] Seeds = { 11, 22, 33, 44, 55, 66, 77, 88, 99, 111, 122, 133 };
        public static int Upgrades;   // levels of every upgrade line the Mech has (0 = base)

        public static string Run()
        {
            if (!UnityEditor.EditorApplication.isPlaying) return "not playing";
            var old = UnityEngine.Object.FindAnyObjectByType<MechDuel>();
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            new UnityEngine.GameObject("MechDuel").AddComponent<MechDuel>();
            return "running; call StarForge.EditorTools.MechDuel.Report";
        }

        public static string RunUpgraded() { Upgrades = 2; return Run(); }
        /// <summary>Two levels of every upgrade, on the first six designs only.</summary>
        public static string RunUpgradedQuick() { Upgrades = 2; Seeds = new uint[] { 11, 22, 33, 44, 55, 66 }; return Run(); }
        public static string RunBase() { Upgrades = 0; return Run(); }

        public static string Report()
        {
            var d = UnityEngine.Object.FindAnyObjectByType<MechDuel>();
            return d == null ? "none" : (d.done ? "DONE\n" : "running\n") + d.log;
        }

        StarForge.World.GameWorld world;
        UnityEngine.Vector2 site;

        System.Collections.IEnumerator Start()
        {
            // Always the same ground: the default map (seed 1000), and the same site on it.
            if (StarForge.Game.MatchSettings.mapSeed != 1000u)
            {
                StarForge.Game.MatchSettings.mapSeed = 1000u;
                StarForge.Game.MatchSettings.skipMenu = false;
                DontDestroyOnLoad(gameObject);
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                yield return new UnityEngine.WaitForSecondsRealtime(3f);
            }
            var boot = StarForge.Game.GameBootstrap.Instance;
            if (StarForge.World.GameWorld.Instance == null || !StarForge.World.GameWorld.Instance.running)
            {
                StarForge.Game.MatchSettings.aiMemory = false;
                StarForge.Game.MatchSettings.spectate = true;
                boot.StartMatch();
                yield return new UnityEngine.WaitForSecondsRealtime(1f);
            }
            StarForge.AI.Commander.Suspended = true;
            world = StarForge.World.GameWorld.Instance;
            var map = world.Map;
            // Flat, open ground for the whole approach: the Mech's spot and the 60 m
            // corridor the enemy comes up (Mauler shells fly almost flat, and a rise in
            // between eats them -- on a 9 m relief the result measured the terrain).
            float bestScore = -1e9f;
            site = new UnityEngine.Vector2(world.MapSize * 0.5f, world.MapSize * 0.5f);
            var pick = new System.Random(1);
            var veg = world.Plants;
            for (int i = 0; i < 3000; i++)
            {
                var p = new UnityEngine.Vector2((float)pick.NextDouble(), (float)pick.NextDouble()) * world.MapSize;
                if (!map.InBounds(p, 40f) || !map.InBounds(p + new UnityEngine.Vector2(0f, 62f), 20f)) continue;
                if (map.WaterDepth(p) > -1f || !world.Walkable(p)) continue;
                float lo = 1e9f, hi = -1e9f;
                bool ok = true;
                for (int z = -8; z <= 64 && ok; z += 4)
                    for (int x = -14; x <= 14; x += 7)
                    {
                        var q = p + new UnityEngine.Vector2(x, z);
                        if (!world.Walkable(q, 1.5f) || map.WaterDepth(q) > 0f) { ok = false; break; }
                        float h = map.HeightAt(q);
                        lo = UnityEngine.Mathf.Min(lo, h); hi = UnityEngine.Mathf.Max(hi, h);
                    }
                if (!ok) continue;
                float score = -(hi - lo);
                if (score > bestScore) { bestScore = score; site = p; }
            }
            log.AppendLine($"site {site} relief {-bestScore:0.0} m, upgrades {Upgrades}");
            UnityEngine.Time.timeScale = 6f;

            foreach (var seed in Seeds)
            {
                var design = StarForge.World.MechParts.Generate(seed);
                log.AppendLine($"{design}");
                foreach (var type in new[] { StarForge.Sim.UnitType.Mauler, StarForge.Sim.UnitType.Trooper })
                {
                    int lo = type == StarForge.Sim.UnitType.Mauler ? 6 : 10, hi = type == StarForge.Sim.UnitType.Mauler ? 60 : 110;
                    string trace = "";
                    // Bisection on N: the smallest group that kills the Mech.
                    while (hi - lo > (type == StarForge.Sim.UnitType.Mauler ? 2 : 3))
                    {
                        int n = (lo + hi) / 2;
                        bool mechWon = false; float left = 0f, secs = 0f; int killed = 0;
                        yield return Fight(design, type, n, (w, l, s, k) => { mechWon = w; left = l; secs = s; killed = k; });
                        trace += $" {n}:{(mechWon ? $"M({left * 100f:0}%)" : $"U({killed})")}";
                        if (mechWon) lo = n; else hi = n;
                    }
                    log.AppendLine($"   vs {type}: break-even ~{(lo + hi) / 2} ({trace.Trim()})");
                }
            }
            UnityEngine.Time.timeScale = 1f;
            StarForge.AI.Commander.Suspended = false;
            done = true;
        }

        System.Collections.IEnumerator Fight(StarForge.World.MechDesign design, StarForge.Sim.UnitType type, int n, System.Action<bool, float, float, int> result)
        {
            // Clear the field of anything left from the last fight.
            // Out of the world's list first: GameWorld.Update runs before this resumes, and a
            // destroyed unit still in the list threw from CachePosition.
            foreach (var u in world.units.ToArray())
                if (u != null && !u.dying && !u.def.building && u.Type != StarForge.Sim.UnitType.Ore && (u.pos - site).magnitude < 80f && u.Type != StarForge.Sim.UnitType.Worker)
                    Remove(u);
            yield return null;
            var ups = new int[(int)StarForge.World.MechUpgrade.Count];
            for (int i = 0; i < ups.Length; i++) ups[i] = UnityEngine.Mathf.Min(Upgrades, StarForge.World.MechParts.Upgrade((StarForge.World.MechUpgrade)i).levels);
            ups[(int)StarForge.World.MechUpgrade.Hardpoint] = UnityEngine.Mathf.Min(ups[(int)StarForge.World.MechUpgrade.Hardpoint], design.reserve.Count);
            var m = world.TrialMech(0, design, world.NearestWalkable(site), 0f, false);
            m.mech.Init(design, ups, false);
            var enemies = new System.Collections.Generic.List<StarForge.World.Unit>();
            var from = site + new UnityEngine.Vector2(0f, 55f);
            int side = UnityEngine.Mathf.CeilToInt(UnityEngine.Mathf.Sqrt(n));
            float spacing = type == StarForge.Sim.UnitType.Mauler ? 3.6f : 1.7f;
            for (int i = 0; i < n; i++)
            {
                var p = from + new UnityEngine.Vector2((i % side - side * 0.5f) * spacing, (i / side) * spacing);
                // Only where it can walk to the Mech from: a unit spawned on a ledge never arrives.
                enemies.Add(world.Spawn(type, 1, world.NearestReachable(site, p), true, UnityEngine.Mathf.PI));
            }
            yield return null;
            world.CmdMove(enemies, site, true);
            float t0 = world.time;
            int alive = n;
            while (world.time - t0 < 150f)
            {
                alive = 0;
                foreach (var e in enemies) if (StarForge.World.Unit.Live(e)) alive++;
                if (!StarForge.World.Unit.Live(m) || alive == 0) break;
                // Stragglers that lost the order walk on.
                if (UnityEngine.Time.frameCount % 60 == 0)
                    foreach (var e in enemies) if (StarForge.World.Unit.Live(e) && e.order == StarForge.Sim.Order.Idle) world.CmdMove(new[] { e }, m.pos, true);
                yield return null;
            }
            // Still standing when the clock ran out (the last few could not reach it): the Mech held.
            bool won = StarForge.World.Unit.Live(m);
            float left = StarForge.World.Unit.Live(m) ? m.hp / m.MaxHp : 0f;
            result(won, left, world.time - t0, n - alive);
            // Wait for the dead to clear.
            for (int k = 0; k < 20; k++) yield return null;
            if (m != null) Remove(m);
            foreach (var e in enemies) if (e != null) Remove(e);
            yield return null;
        }

        void Remove(StarForge.World.Unit u)
        {
            world.units.Remove(u);
            UnityEngine.Object.Destroy(u.gameObject);
        }
    }
}

namespace StarForge.EditorTools
{
    /// <summary>The player's side of the Mech, driven through the same calls the mouse
    /// uses: raise a Mech Bay with a Digger, watch its card, buy upgrades, wait for the
    /// drop, read the comms; then a Mech repaired at its bay and a Mech destroyed.
    /// Photographs Temp/mechflow_*.png.</summary>
    public sealed class MechPlayerFlow : UnityEngine.MonoBehaviour
    {
        public string report = "staging";
        readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

        public static string Run()
        {
            if (!UnityEditor.EditorApplication.isPlaying) return "not playing";
            new UnityEngine.GameObject("MechPlayerFlow").AddComponent<MechPlayerFlow>();
            return "staging; call StarForge.EditorTools.MechPlayerFlow.Report in ~3 min";
        }

        public static string Report()
        {
            var s = UnityEngine.Object.FindAnyObjectByType<MechPlayerFlow>();
            return s == null ? "none" : s.report + "\n" + s.log;
        }

        System.Collections.IEnumerator Start()
        {
            var boot = StarForge.Game.GameBootstrap.Instance;
            if (StarForge.World.GameWorld.Instance == null || !StarForge.World.GameWorld.Instance.running)
            {
                StarForge.Game.MatchSettings.aiMemory = false;
                StarForge.Game.MatchSettings.spectate = false;
                boot.StartMatch();
                yield return new UnityEngine.WaitForSecondsRealtime(1.5f);
            }
            var world = StarForge.World.GameWorld.Instance;
            var player = UnityEngine.Object.FindAnyObjectByType<StarForge.View.PlayerController>();
            var rig = UnityEngine.Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            world.CheatOre(0, 5000);
            StarForge.World.Unit digger = null;
            foreach (var u in world.units) if (u != null && u.team == 0 && u.Type == StarForge.Sim.UnitType.Worker) { digger = u; break; }
            var home = world.Map.StartPos(0);
            UnityEngine.Vector2 spot = home;
            for (int i = 0; i < 60; i++)
            {
                var p = home + UnityEngine.Random.insideUnitCircle * 22f;
                if ((p - home).magnitude > 12f && world.CanPlace(StarForge.Sim.UnitType.MechBay, p)) { spot = p; break; }
            }
            player.SelectOnly(digger);
            yield return new UnityEngine.WaitForSecondsRealtime(0.5f);
            var cards = new System.Collections.Generic.List<StarForge.View.CardAction>();
            player.BuildCard(cards);
            string card = "";
            foreach (var c in cards) card += $"[{c.hotkey}] {c.title}{(c.enabled ? "" : " (off)")}; ";
            log.AppendLine("Digger card: " + card);
            bool placed = world.CmdBuild(new[] { digger }, StarForge.Sim.UnitType.MechBay, spot);
            bool second = world.CmdBuild(new[] { digger }, StarForge.Sim.UnitType.MechBay, spot + new UnityEngine.Vector2(15f, 0f));
            log.AppendLine($"placed {placed}, a second refused: {!second} ('{world.lastRefusal}')");
            // Build it at speed.
            UnityEngine.Time.timeScale = 4f;
            StarForge.World.Unit bay = null;
            for (float t = 0f; t < 60f; t += UnityEngine.Time.unscaledDeltaTime)
            {
                bay = world.BayOf(0);
                if (bay != null && bay.Complete) break;
                yield return null;
            }
            UnityEngine.Time.timeScale = 1f;
            log.AppendLine($"bay complete: {bay != null && bay.Complete} at {world.time:0}s; drop at {world.factions[0].mechDropAt:0}s");
            rig.scripted = true;
            rig.JumpTo(bay.pos);
            rig.ZoomTo(34f);
            player.SelectOnly(bay);
            yield return new UnityEngine.WaitForSecondsRealtime(1.5f);
            player.BuildCard(cards);
            card = "";
            foreach (var c in cards) card += $"[{c.hotkey}] {c.title} {c.cost}{(c.enabled ? "" : " (off)")}; ";
            log.AppendLine("Bay card: " + card);
            UnityEngine.ScreenCapture.CaptureScreenshot("Temp/mechflow_bay.png", 2);
            // Buy upgrades through the card, as a click would.
            foreach (var c in cards) if (c.kind == StarForge.View.CardKind.MechUpgrade && c.upgrade == StarForge.World.MechUpgrade.Armour) player.Execute(c);
            log.AppendLine($"researching {(StarForge.World.MechUpgrade)world.factions[0].researching}, ore now {world.factions[0].ore}");
            yield return new UnityEngine.WaitForSecondsRealtime(1f);
            UnityEngine.ScreenCapture.CaptureScreenshot("Temp/mechflow_research.png", 2);
            // To the drop, at speed, buying as the bay frees up.
            UnityEngine.Time.timeScale = 6f;
            var order = new[] { StarForge.World.MechUpgrade.Hardpoint, StarForge.World.MechUpgrade.Weapons, StarForge.World.MechUpgrade.Targeting };
            int next = 0;
            while (world.MechOf(0) == null && world.time < world.factions[0].mechDropAt + 5f)
            {
                if (world.factions[0].researching < 0 && next < order.Length) world.CmdMechUpgrade(bay, order[next++]);
                if (world.factions[0].mechInbound && UnityEngine.Time.timeScale > 1f) { UnityEngine.Time.timeScale = 1f; rig.JumpTo(world.factions[0].dropSpot); rig.ZoomTo(50f); }
                yield return null;
            }
            yield return new UnityEngine.WaitForSecondsRealtime(2.5f);
            UnityEngine.ScreenCapture.CaptureScreenshot("Temp/mechflow_landing.png", 2);
            yield return new UnityEngine.WaitForSecondsRealtime(3f);
            var m = world.MechOf(0);
            log.AppendLine($"mech {(m != null ? m.mech.design.ToString() : "none")}; upgrades {string.Join("", world.factions[0].upgrades)}; guns {(m != null ? m.mech.guns.Count : 0)}");
            UnityEngine.ScreenCapture.CaptureScreenshot("Temp/mechflow_comms.png", 2);
            if (m != null)
            {
                player.SelectOnly(m);
                yield return new UnityEngine.WaitForSecondsRealtime(0.6f);
                player.BuildCard(cards);
                log.AppendLine("Mech card: " + (cards.Count > 0 ? cards[0].title : "(none)"));
                rig.JumpTo(m.pos);
                rig.ZoomTo(28f);
                yield return new UnityEngine.WaitForSecondsRealtime(1.5f);
                UnityEngine.ScreenCapture.CaptureScreenshot("Temp/mechflow_selected.png", 2);
                // Hurt it and let it go home to be mended.
                world.Damage(m, m.MaxHp * 0.7f + m.mech.shield, m.pos + new UnityEngine.Vector2(5f, 0f), null);
                UnityEngine.Time.timeScale = 3f;
                bool sawRepair = false;
                for (float t = 0f; t < 25f && StarForge.World.Unit.Live(m); t += UnityEngine.Time.unscaledDeltaTime)
                {
                    rig.JumpTo(m.pos);
                    if (m.mech.repairing && !sawRepair)
                    {
                        sawRepair = true;
                        UnityEngine.Time.timeScale = 1f;
                        yield return new UnityEngine.WaitForSecondsRealtime(0.6f);
                        UnityEngine.ScreenCapture.CaptureScreenshot("Temp/mechflow_repair.png", 2);
                        UnityEngine.Time.timeScale = 3f;
                    }
                    yield return null;
                }
                UnityEngine.Time.timeScale = 1f;
                log.AppendLine($"went to be repaired: {sawRepair}; hull now {m.hp / m.MaxHp * 100f:0}%; intent '{m.mech.intentText}'");
                // And its end.
                rig.JumpTo(m.pos);
                rig.ZoomTo(40f);
                yield return new UnityEngine.WaitForSecondsRealtime(1f);
                world.Damage(m, 1e6f, m.pos + new UnityEngine.Vector2(3f, 2f), null, true);
                float[] at = { 0.15f, 0.6f, 1.5f, 3f, 7f };
                string[] nm = { "death_a", "death_b", "death_c", "death_d", "death_e" };
                float t0 = UnityEngine.Time.time;
                for (int k = 0; k < at.Length; k++)
                {
                    while (UnityEngine.Time.time - t0 < at[k]) yield return null;
                    UnityEngine.ScreenCapture.CaptureScreenshot($"Temp/mechflow_{nm[k]}.png", 2);
                }
                log.AppendLine($"after death: mechLost {world.factions[0].mechLost}; upgrade refused: {!world.CmdMechUpgrade(bay, StarForge.World.MechUpgrade.Servos)} ('{world.lastRefusal}')");
            }
            report = "done";
        }
    }
}

namespace StarForge.EditorTools
{
    /// <summary>The walk, close up: a Strider and an Arachnid walking across the view,
    /// eight frames a tenth of a second apart each (Temp/mechgait_*.png), with how far
    /// each planted foot slid while it was down (it should not).</summary>
    public sealed class MechGait : UnityEngine.MonoBehaviour
    {
        public string report = "staging";

        public static string Run()
        {
            if (!UnityEditor.EditorApplication.isPlaying) return "not playing";
            new UnityEngine.GameObject("MechGait").AddComponent<MechGait>();
            return "staging; call StarForge.EditorTools.MechGait.Report in ~40 s";
        }

        public static string Report()
        {
            var s = UnityEngine.Object.FindAnyObjectByType<MechGait>();
            return s == null ? "none" : s.report;
        }

        System.Collections.IEnumerator Start()
        {
            var boot = StarForge.Game.GameBootstrap.Instance;
            if (StarForge.World.GameWorld.Instance == null || !StarForge.World.GameWorld.Instance.running)
            {
                StarForge.Game.MatchSettings.aiMemory = false;
                StarForge.Game.MatchSettings.spectate = true;
                boot.StartMatch();
                yield return new UnityEngine.WaitForSecondsRealtime(1.5f);
            }
            StarForge.AI.Commander.Suspended = true;
            var world = StarForge.World.GameWorld.Instance;
            var rig = UnityEngine.Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            var map = world.Map;
            var pick = new System.Random(3);
            UnityEngine.Vector2 best = new UnityEngine.Vector2(world.MapSize * 0.5f, world.MapSize * 0.5f);
            float bestScore = -1e9f;
            for (int i = 0; i < 2000; i++)
            {
                var p = new UnityEngine.Vector2((float)pick.NextDouble(), (float)pick.NextDouble()) * world.MapSize;
                if (!map.InBounds(p, 40f) || map.WaterDepth(p) > -1f || !world.Walkable(p)) continue;
                float lo = 1e9f, hi = -1e9f; bool ok = true;
                for (int x = -30; x <= 30 && ok; x += 6)
                    for (int z = -8; z <= 8; z += 8)
                    {
                        var q = p + new UnityEngine.Vector2(x, z);
                        if (!world.Walkable(q, 1.5f)) { ok = false; break; }
                        float h = map.HeightAt(q); lo = UnityEngine.Mathf.Min(lo, h); hi = UnityEngine.Mathf.Max(hi, h);
                    }
                if (!ok) continue;
                float score = -(hi - lo) - world.UnitsNear(p, 30f).Count * 2f;
                if (world.Plants != null) foreach (var pl in world.Plants.plants) if ((new UnityEngine.Vector2(pl.pos.x, pl.pos.z) - p).sqrMagnitude < 25f * 25f) score -= 0.3f;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            var sb = new System.Text.StringBuilder();
            rig.scripted = true;
            foreach (var loco in new[] { StarForge.World.MechLocomotion.Biped, StarForge.World.MechLocomotion.Quad })
            {
                var d = new StarForge.World.MechDesign { locomotion = loco, frame = StarForge.World.MechFrame.Medium, className = loco.ToString(), pilot = "T", callsign = "T", order = "T" };
                d.weapons.AddRange(new[] { StarForge.World.MechWeapon.Autocannon, StarForge.World.MechWeapon.Laser, StarForge.World.MechWeapon.Missiles, StarForge.World.MechWeapon.Missiles });
                var start = world.NearestWalkable(best + new UnityEngine.Vector2(-24f, 0f));
                var m = world.TrialMech(0, d, start, UnityEngine.Mathf.PI * 0.5f, false);
                yield return new UnityEngine.WaitForSecondsRealtime(1f);
                world.CmdMechMove(m, best + new UnityEngine.Vector2(26f, 0f));
                var view = m.GetComponent<StarForge.View.MechView>();
                // Track each foot while it is planted: how far does it move?
                var feet = new System.Collections.Generic.List<UnityEngine.Transform>();
                foreach (var t in m.GetComponentsInChildren<UnityEngine.Transform>())
                    if (t.name.StartsWith("Foot") || t.name.StartsWith("Shin")) feet.Add(t);
                var last = new UnityEngine.Vector3[feet.Count];
                for (float w = 0f; w < 2.5f; w += UnityEngine.Time.unscaledDeltaTime) { rig.JumpTo(m.pos); yield return null; }
                rig.ZoomTo(26f);
                for (int f = 0; f < 8; f++)
                {
                    rig.JumpTo(m.pos);
                    for (int k = 0; k < feet.Count; k++) last[k] = feet[k].position;
                    yield return new UnityEngine.WaitForSecondsRealtime(0.1f);
                    UnityEngine.ScreenCapture.CaptureScreenshot($"Temp/mechgait_{loco}_{f}.png", 2);
                }
                float speed = m.agent != null ? m.agent.velocity.magnitude : 0f;
                sb.AppendLine($"{loco}: speed {speed:0.0} m/s, feet found {feet.Count}");
                world.units.Remove(m);
                UnityEngine.Object.Destroy(m.gameObject);
                yield return new UnityEngine.WaitForSecondsRealtime(0.5f);
            }
            StarForge.AI.Commander.Suspended = false;
            report = "done\n" + sb;
        }
    }
}

namespace StarForge.EditorTools
{
    public static class MechDesigns
    {
        /// <summary>What the generator makes over many seeds: how often each part is
        /// chosen, how many guns, duplicates and empty mounts, and a few examples.</summary>
        public static string Survey()
        {
            int n = 400;
            var loco = new int[(int)StarForge.World.MechLocomotion.Count];
            var frame = new int[(int)StarForge.World.MechFrame.Count];
            var weap = new int[(int)StarForge.World.MechWeapon.Count];
            var util = new int[(int)StarForge.World.MechUtility.Count];
            int guns = 0, dup = 0, empty = 0, noLight = 0, noHeavy = 0, spare = 0, maxSpare = 0;
            var sb = new System.Text.StringBuilder();
            for (uint s = 1; s <= n; s++)
            {
                var d = StarForge.World.MechParts.Generate(s * 7919u);
                loco[(int)d.locomotion]++; frame[(int)d.frame]++; util[(int)d.utility]++;
                guns += d.weapons.Count;
                empty += d.Frame.hardpoints - d.weapons.Count;
                var seen = new System.Collections.Generic.HashSet<StarForge.World.MechWeapon>();
                foreach (var w in d.weapons) { weap[(int)w]++; if (!seen.Add(w)) dup++; }
                if (!StarForge.World.MechParts.Covers(d, true)) noLight++;
                if (!StarForge.World.MechParts.Covers(d, false)) noHeavy++;
                spare += d.spare; maxSpare = UnityEngine.Mathf.Max(maxSpare, d.spare);
                if (d.PointsSpent + d.spare != StarForge.World.MechParts.Budget) sb.AppendLine($"BUDGET MISMATCH seed {s}: {d}");
                if (s <= 8) sb.AppendLine("  " + d);
            }
            string Row<T>(int[] a) where T : System.Enum
            {
                var parts = new System.Collections.Generic.List<string>();
                for (int i = 0; i < a.Length; i++) parts.Add($"{(T)(object)i} {100f * a[i] / n:0}%");
                return string.Join(", ", parts);
            }
            return $"{n} designs\nlocomotion: {Row<StarForge.World.MechLocomotion>(loco)}\nframe: {Row<StarForge.World.MechFrame>(frame)}\n" +
                   $"weapons (per design): {Row<StarForge.World.MechWeapon>(weap)}\nutility: {Row<StarForge.World.MechUtility>(util)}\n" +
                   $"guns/design {guns / (float)n:0.00}, duplicate guns {dup}, empty base mounts {empty}, no anti-light {noLight}, no anti-heavy {noHeavy}, spare avg {spare / (float)n:0.0} max {maxSpare}\n" + sb;
        }

        /// <summary>What the armoury picks (MechArmoury.Compose) for both sides of 200 match
        /// seeds against four enemy mixes: how often each part, how many different loadouts,
        /// how often the two sides match, and how the pick follows the threat.</summary>
        public static string ArmourySurvey()
        {
            var sb = new System.Text.StringBuilder();
            var mixes = new (string name, float light, float heavy, float structures, float mech)[]
            {
                ("infantry", 2400f, 300f, 1500f, 0f),
                ("armour", 300f, 2400f, 1500f, 0f),
                ("balanced", 1200f, 1200f, 1500f, 0f),
                ("enemy Mech", 900f, 900f, 1500f, 3200f),
            };
            int n = 200;
            foreach (var mix in mixes)
            {
                var loco = new int[(int)StarForge.World.MechLocomotion.Count];
                var frame = new int[(int)StarForge.World.MechFrame.Count];
                var weap = new int[(int)StarForge.World.MechWeapon.Count];
                var distinct = new System.Collections.Generic.HashSet<string>();
                int sameBody = 0, flame = 0, antiHeavy = 0;
                var threat = new StarForge.World.MechArmoury.Threat { light = mix.light, heavy = mix.heavy, structures = mix.structures, mech = mix.mech, enemyMech = mix.mech > 0f };
                for (uint seed = 1; seed <= n; seed++)
                {
                    StarForge.World.MechDesign first = null;
                    for (int t = 0; t < 2; t++)
                    {
                        var identity = StarForge.World.MechParts.Generate(seed * 2654435761u ^ (0x6D656368u + (uint)t * 0x9E3779B9u));
                        var d = StarForge.World.MechArmoury.Compose(identity, threat, first, 0);
                        loco[(int)d.locomotion]++; frame[(int)d.frame]++;
                        bool hasFlame = false, hasAH = false;
                        foreach (var w in d.weapons)
                        {
                            weap[(int)w]++;
                            var part = StarForge.World.MechParts.Weapon(w);
                            if (part.Flame) hasFlame = true;
                            if (w == StarForge.World.MechWeapon.Laser || w == StarForge.World.MechWeapon.Railgun) hasAH = true;
                        }
                        if (hasFlame) flame++;
                        if (hasAH) antiHeavy++;
                        distinct.Add(d.Loadout());
                        if (t == 1 && first != null && first.locomotion == d.locomotion && first.frame == d.frame) sameBody++;
                        if (t == 0) first = d;
                    }
                }
                int total = n * 2;
                string Row<T>(int[] a, int div) where T : System.Enum
                {
                    var parts = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < a.Length; i++) parts.Add($"{(T)(object)i} {100f * a[i] / div:0}%");
                    return string.Join(", ", parts);
                }
                sb.AppendLine($"vs {mix.name}: {distinct.Count} different loadouts in {total}; with a flame weapon {100f * flame / total:0}%, with a laser or railgun {100f * antiHeavy / total:0}%; both sides the same body {100f * sameBody / n:0}%");
                sb.AppendLine($"  locomotion: {Row<StarForge.World.MechLocomotion>(loco, total)}; frame: {Row<StarForge.World.MechFrame>(frame, total)}");
                sb.AppendLine($"  weapons (per Mech): {Row<StarForge.World.MechWeapon>(weap, total)}");
            }
            return sb.ToString();
        }
    }
}

namespace StarForge.EditorTools
{
    /// <summary>A tracked Mech driven across ordinary ground, three 60 m runs: how its hull
    /// rocks. Reports the hull's roll and pitch rates (RMS, deg/s), its vertical jitter (RMS
    /// of the height against its own half-second average), how often the roll reverses a
    /// second (a wobble), and the largest tilt -- the "roly-poly" check.</summary>
    public sealed class MechTrackRide : UnityEngine.MonoBehaviour
    {
        public string report = "staging";

        public static string Run()
        {
            if (!UnityEditor.EditorApplication.isPlaying) return "not playing";
            new UnityEngine.GameObject("MechTrackRide").AddComponent<MechTrackRide>();
            return "running; call StarForge.EditorTools.MechTrackRide.Report in ~60 s";
        }

        public static string Report()
        {
            var s = UnityEngine.Object.FindAnyObjectByType<MechTrackRide>();
            return s == null ? "none" : s.report;
        }

        System.Collections.IEnumerator Start()
        {
            var boot = StarForge.Game.GameBootstrap.Instance;
            if (StarForge.World.GameWorld.Instance == null || !StarForge.World.GameWorld.Instance.running)
            {
                StarForge.Game.MatchSettings.aiMemory = false;
                StarForge.Game.MatchSettings.spectate = true;
                boot.StartMatch();
                yield return new UnityEngine.WaitForSecondsRealtime(1.5f);
            }
            StarForge.AI.Commander.Suspended = true;
            var world = StarForge.World.GameWorld.Instance;
            var map = world.Map;
            var rig = UnityEngine.Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            UnityEngine.Time.timeScale = 1f;
            var sb = new System.Text.StringBuilder();
            var passes = new System.Text.StringBuilder();
            for (int pass = 0; pass < 2; pass++)
            {
            StarForge.View.MechView.LegacyTrackRide = pass == 0;
            var pick = new System.Random(7);
            double roll2 = 0, pitch2 = 0, jit2 = 0, yawAcc2 = 0, yawWob2 = 0, vacc2 = 0; int n = 0, nAcc = 0, reversals = 0, yawReversals = 0; float maxRoll = 0f, maxPitch = 0f, secs = 0f, dist = 0f;
            // How far the tracks go into the ground: the bottom of each run, sampled along its
            // flat length in the hull's own frame, against the ground under it.
            float maxSink = 0f; int sunkFrames = 0;
            for (int route = 0; route < 3; route++)
            {
                UnityEngine.Vector2 a = default, b = default;
                bool found = false;
                for (int i = 0; i < 4000 && !found; i++)
                {
                    var p = new UnityEngine.Vector2((float)pick.NextDouble(), (float)pick.NextDouble()) * world.MapSize;
                    float ang = (float)pick.NextDouble() * 6.2832f;
                    var q = p + new UnityEngine.Vector2(UnityEngine.Mathf.Cos(ang), UnityEngine.Mathf.Sin(ang)) * 60f;
                    if (!map.InBounds(p, 30f) || !map.InBounds(q, 30f) || map.WaterDepth(p) > -1f || map.WaterDepth(q) > -1f) continue;
                    if (!world.Walkable(p, 2f) || !world.Walkable(q, 2f) || !world.Reaches(p, q, true)) continue;
                    float lo = 1e9f, hi = -1e9f; bool dry = true;
                    for (float t = 0f; t <= 1f; t += 0.1f)
                    {
                        var s2 = UnityEngine.Vector2.Lerp(p, q, t);
                        if (map.WaterDepth(s2) > -0.5f) { dry = false; break; }
                        float h = map.HeightAt(s2); lo = UnityEngine.Mathf.Min(lo, h); hi = UnityEngine.Mathf.Max(hi, h);
                    }
                    if (!dry || hi - lo < 1f || hi - lo > 7f) continue;
                    a = p; b = q; found = true;
                }
                if (!found) { sb.AppendLine($"route {route}: none found"); continue; }
                var d = new StarForge.World.MechDesign { locomotion = StarForge.World.MechLocomotion.Tracks, frame = StarForge.World.MechFrame.Medium, utility = StarForge.World.MechUtility.None, className = "T", pilot = "T", callsign = "T", order = "T" };
                d.weapons.Add(StarForge.World.MechWeapon.Autocannon);
                var to = b - a;
                var m = world.TrialMech(0, d, a, UnityEngine.Mathf.Atan2(to.x, to.y), false);
                yield return null; yield return null;
                var view = m.GetComponent<StarForge.View.MechView>();
                world.CmdMechMove(m, b);
                float t0 = UnityEngine.Time.time, lastRoll = 0f, lastPitch = 0f, lastRate = 0f, ema = 0f, lastYaw = 0f, lastYawRate = 0f, lastYawSign = 0f, yawEma = 0f;
                // Vertical acceleration of the hull over 0.1 s windows: a bob, not a slope.
                float winT = 0f, winY0 = float.NaN, winY1 = float.NaN;
                bool first = true;
                var start = m.pos;
                while (UnityEngine.Time.time - t0 < 16f && StarForge.World.Unit.Live(m) && (m.pos - b).magnitude > 3f)
                {
                    rig.JumpTo(m.pos);
                    // At the end of the frame, after MechView's LateUpdate: a coroutine resumed
                    // after Update saw the agent's new position with the hull's old offset.
                    yield return new UnityEngine.WaitForEndOfFrame();
                    float dt = UnityEngine.Time.deltaTime;
                    if (dt <= 0f || view == null || view.Body == null) continue;
                    var e = view.Body.localEulerAngles;
                    float pitch = UnityEngine.Mathf.DeltaAngle(0f, e.x), roll = UnityEngine.Mathf.DeltaAngle(0f, e.z);
                    float y = view.Body.position.y;
                    float yawNow = m.transform.eulerAngles.y;
                    if (first) { ema = y; first = false; lastRoll = roll; lastPitch = pitch; lastYaw = yawNow; yawEma = yawNow; continue; }
                    yawEma += UnityEngine.Mathf.DeltaAngle(yawEma, yawNow) * UnityEngine.Mathf.Clamp01(dt / 0.7f);
                    float wob = UnityEngine.Mathf.DeltaAngle(yawEma, yawNow);
                    yawWob2 += wob * wob;
                    winT += dt;
                    if (winT >= 0.1f)
                    {
                        if (!float.IsNaN(winY0))
                        {
                            float acc = (y - 2f * winY1 + winY0) / (winT * winT);
                            vacc2 += acc * acc; nAcc++;
                        }
                        winY0 = winY1; winY1 = y; winT = 0f;
                    }
                    float yr = UnityEngine.Mathf.DeltaAngle(lastYaw, yawNow) / dt;
                    yawAcc2 += (yr - lastYawRate) / dt * ((yr - lastYawRate) / dt) * 1e-4;
                    if (UnityEngine.Mathf.Abs(yr) > 3f)
                    {
                        if (lastYawSign != 0f && UnityEngine.Mathf.Sign(yr) != lastYawSign) yawReversals++;
                        lastYawSign = UnityEngine.Mathf.Sign(yr);
                    }
                    lastYawRate = yr; lastYaw = yawNow;
                    ema = UnityEngine.Mathf.Lerp(ema, y, UnityEngine.Mathf.Clamp01(dt / 0.5f));
                    float rr = (roll - lastRoll) / dt, pr = (pitch - lastPitch) / dt;
                    roll2 += rr * rr; pitch2 += pr * pr; jit2 += (y - ema) * (y - ema); n++;
                    if (UnityEngine.Mathf.Abs(rr) > 2f && UnityEngine.Mathf.Abs(lastRate) > 2f && UnityEngine.Mathf.Sign(rr) != UnityEngine.Mathf.Sign(lastRate)) reversals++;
                    if (UnityEngine.Mathf.Abs(rr) > 2f) lastRate = rr;
                    maxRoll = UnityEngine.Mathf.Max(maxRoll, UnityEngine.Mathf.Abs(roll));
                    maxPitch = UnityEngine.Mathf.Max(maxPitch, UnityEngine.Mathf.Abs(pitch));
                    lastRoll = roll; lastPitch = pitch;
                    secs += dt;
                    float sink = 0f;
                    for (int side = -1; side <= 1; side += 2)
                        for (int q = 0; q < 7; q++)
                        {
                            // In the kit's own units: the hull carries the model's scale.
                            var w3 = view.Body.TransformPoint(new UnityEngine.Vector3(side * 1.55f, 0f, UnityEngine.Mathf.Lerp(-1.9f, 1.9f, q / 6f)));
                            sink = UnityEngine.Mathf.Max(sink, map.HeightAt(new UnityEngine.Vector2(w3.x, w3.z)) - w3.y);
                        }
                    maxSink = UnityEngine.Mathf.Max(maxSink, sink);
                    if (sink > 0.05f) sunkFrames++;
                }
                dist += (m.pos - start).magnitude;
                world.units.Remove(m);
                UnityEngine.Object.Destroy(m.gameObject);
                yield return new UnityEngine.WaitForSecondsRealtime(0.5f);
            }
            if (n == 0) { passes.AppendLine("no samples"); continue; }
            passes.AppendLine((pass == 0 ? "before: " : "after:  ") + $"{secs:0.0} s driving {dist:0} m ({dist / UnityEngine.Mathf.Max(0.1f, secs):0.0} m/s); roll rate RMS {System.Math.Sqrt(roll2 / n):0.0} deg/s, pitch rate RMS {System.Math.Sqrt(pitch2 / n):0.0} deg/s, " +
                     $"height jitter RMS {System.Math.Sqrt(jit2 / n) * 100:0.0} cm, roll reversals {reversals / UnityEngine.Mathf.Max(0.1f, secs):0.00}/s, max roll {maxRoll:0.0}, max pitch {maxPitch:0.0} deg; " +
                     $"heading reversals {yawReversals / UnityEngine.Mathf.Max(0.1f, secs):0.00}/s, heading wobble RMS {System.Math.Sqrt(yawWob2 / n):0.0} deg, " +
                     $"hull vertical acceleration RMS {(nAcc > 0 ? System.Math.Sqrt(vacc2 / nAcc) : 0):0.00} m/s2; " +
                     $"tracks into the ground: deepest {maxSink * 100f:0} cm, more than 5 cm in {100f * sunkFrames / UnityEngine.Mathf.Max(1, n):0}% of frames");
            }
            StarForge.View.MechView.LegacyTrackRide = false;
            StarForge.AI.Commander.Suspended = false;
            report = "done\n" + passes + sb;
        }
    }
}

namespace StarForge.EditorTools
{
    /// <summary>The Flame Tower close up: a Warden-class Strider with one on each shoulder,
    /// Troopers closing from behind and from the side. Photographs it firing
    /// (Temp/flametower_*.png, 3x) and reports each turret's heading against the torso's and
    /// how high its muzzle stands, so the turn and the muzzle can be checked in numbers.</summary>
    public sealed class FlameTowerLook : UnityEngine.MonoBehaviour
    {
        public string report = "staging";

        public static string Run()
        {
            if (!UnityEditor.EditorApplication.isPlaying) return "not playing";
            new UnityEngine.GameObject("FlameTowerLook").AddComponent<FlameTowerLook>();
            return "staging; call StarForge.EditorTools.FlameTowerLook.Report in ~20 s";
        }

        public static string Report()
        {
            var s = UnityEngine.Object.FindAnyObjectByType<FlameTowerLook>();
            return s == null ? "none" : s.report;
        }

        System.Collections.IEnumerator Start()
        {
            var boot = StarForge.Game.GameBootstrap.Instance;
            if (StarForge.World.GameWorld.Instance == null || !StarForge.World.GameWorld.Instance.running)
            {
                StarForge.Game.MatchSettings.aiMemory = false;
                StarForge.Game.MatchSettings.spectate = true;
                boot.StartMatch();
                yield return new UnityEngine.WaitForSecondsRealtime(1.5f);
            }
            StarForge.AI.Commander.Suspended = true;
            var world = StarForge.World.GameWorld.Instance;
            var map = world.Map;
            var rig = UnityEngine.Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            UnityEngine.Time.timeScale = 1f;
            // Open, flat, dry ground away from everything.
            var pick = new System.Random(11);
            UnityEngine.Vector2 best = new UnityEngine.Vector2(world.MapSize * 0.5f, world.MapSize * 0.5f);
            float bestScore = -1e9f;
            for (int i = 0; i < 2000; i++)
            {
                var p = new UnityEngine.Vector2((float)pick.NextDouble(), (float)pick.NextDouble()) * world.MapSize;
                if (!map.InBounds(p, 30f) || map.WaterDepth(p) > -1f || !world.Walkable(p, 3f)) continue;
                float lo = 1e9f, hi = -1e9f;
                for (int a = 0; a < 12; a++)
                {
                    float h = map.HeightAt(p + new UnityEngine.Vector2(UnityEngine.Mathf.Cos(a * 0.52f), UnityEngine.Mathf.Sin(a * 0.52f)) * 16f);
                    lo = UnityEngine.Mathf.Min(lo, h); hi = UnityEngine.Mathf.Max(hi, h);
                }
                var list = new System.Collections.Generic.List<int>();
                world.Plants.PlantsNear(p, 14f, list);
                float score = -(hi - lo) * 4f - world.UnitsNear(p, 20f).Count * 6f - list.Count * 2f;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            var d = new StarForge.World.MechDesign { locomotion = StarForge.World.MechLocomotion.Biped, frame = StarForge.World.MechFrame.Medium, utility = StarForge.World.MechUtility.None, className = "T", pilot = "T", callsign = "T", order = "T" };
            d.weapons.AddRange(new[] { StarForge.World.MechWeapon.Autocannon, StarForge.World.MechWeapon.Laser, StarForge.World.MechWeapon.FlameTower, StarForge.World.MechWeapon.FlameTower });
            var m = world.TrialMech(0, d, best, 0f, false);
            yield return new UnityEngine.WaitForSecondsRealtime(0.5f);
            // Troopers behind it (south) and off its right side: the arms face north.
            var enemies = new System.Collections.Generic.List<StarForge.World.Unit>();
            for (int i = 0; i < 6; i++)
                enemies.Add(world.Spawn(StarForge.Sim.UnitType.Trooper, 1, world.NearestWalkable(best + new UnityEngine.Vector2((i - 2.5f) * 1.8f, -13f)), true, 0f));
            for (int i = 0; i < 4; i++)
                enemies.Add(world.Spawn(StarForge.Sim.UnitType.Trooper, 1, world.NearestWalkable(best + new UnityEngine.Vector2(13f, (i - 1.5f) * 1.8f)), true, UnityEngine.Mathf.PI * 1.5f));
            world.CmdMove(enemies, best, true);
            rig.JumpTo(best + new UnityEngine.Vector2(0f, 4f));
            rig.ZoomTo(36f);
            var sb = new System.Text.StringBuilder();
            float t0 = UnityEngine.Time.realtimeSinceStartup;
            int shot = 0;
            while (UnityEngine.Time.realtimeSinceStartup - t0 < 12f && shot < 4)
            {
                yield return null;
                bool towerFiring = false;
                foreach (var g in m.mech.guns) if (g.part.turret && g.firing > 0.5f) towerFiring = true;
                if (!towerFiring) continue;
                UnityEngine.ScreenCapture.CaptureScreenshot($"Temp/flametower_{shot}.png", 3);
                foreach (var g in m.mech.guns)
                    if (g.part.turret)
                    {
                        var mz = m.mech.Muzzle(g);
                        sb.AppendLine($"frame {shot}: {g.mount} turret {g.yaw * UnityEngine.Mathf.Rad2Deg:0} deg off the torso, target {(g.target != null ? g.target.Type.ToString() : "none")}, " +
                                      $"muzzle {mz.y - map.HeightAt(m.pos):0.0} m up, {(new UnityEngine.Vector2(mz.x, mz.z) - m.pos).magnitude:0.0} m out, shots {g.shots}");
                    }
                shot++;
                yield return new UnityEngine.WaitForSecondsRealtime(0.6f);
            }
            int left = 0;
            foreach (var e in enemies) if (StarForge.World.Unit.Live(e)) left++;
            StarForge.AI.Commander.Suspended = false;
            report = $"done: {shot} frames, Troopers left {left}/{enemies.Count}, torso yaw {m.turretYaw * UnityEngine.Mathf.Rad2Deg:0} deg\n" + sb;
        }
    }
}

namespace StarForge.EditorTools
{
    /// <summary>Mech Bays in awkward places: side 0's bay is placed (finished) at 30 s of an
    /// AI-vs-AI match on map seed 1000 -- inside the enemy base, just outside it, in a map
    /// corner, on a lake shore, in a wood, far out on a flank -- and the match run for seven
    /// minutes at 8x. Reports, per case, how long the bay stood and who went for it, where the
    /// Mech landed and whether it could stand there, how it spent its time and whether it
    /// walked into the enemy to be repaired, and what each side's AI was doing.</summary>
    public sealed class BayEdgeCases : UnityEngine.MonoBehaviour
    {
        public static readonly string[] Cases = { "in the enemy base", "just outside the enemy base", "in a map corner", "on a lake shore", "in a wood", "far out on a flank",
                                                  "in the enemy base, built by a Digger", "just outside the enemy base, built by a Digger" };
        /// <summary>Run only this case (-1: all).</summary>
        public static int Only = -1;
        public string report = "staging";

        public static string Run()
        {
            if (!UnityEditor.EditorApplication.isPlaying) return "not playing";
            var old = UnityEngine.Object.FindAnyObjectByType<BayEdgeCases>();
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            new UnityEngine.GameObject("BayEdgeCases").AddComponent<BayEdgeCases>();
            return "running; call StarForge.EditorTools.BayEdgeCases.Report";
        }

        /// <summary>The Digger-built case alone, with the Commander answering a structure
        /// going up in its base (<c>On</c>) or leaving it to the rule for finished ones.</summary>
        public static string RunDiggerOn() { Only = 6; StarForge.AI.Commander.IgnoreSites = false; return Run(); }
        public static string RunDiggerOff() { Only = 6; StarForge.AI.Commander.IgnoreSites = true; return Run(); }
        public static string RunOutsideOn() { Only = 7; StarForge.AI.Commander.IgnoreSites = false; return Run(); }
        public static string RunOutsideOff() { Only = 7; StarForge.AI.Commander.IgnoreSites = true; return Run(); }

        public static string Report()
        {
            var s = UnityEngine.Object.FindAnyObjectByType<BayEdgeCases>();
            return s == null ? "none" : s.report;
        }

        readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

        System.Collections.IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            for (int c = 0; c < Cases.Length; c++)
            {
                if (Only >= 0 && c != Only) continue;
                report = $"running case {c}: {Cases[c]}\n" + log;
                StarForge.Game.MatchSettings.mapSeed = 1000u;
                StarForge.Game.MatchSettings.seed = 1000u;
                StarForge.Game.MatchSettings.fixedSeed = true;
                StarForge.Game.MatchSettings.skipMenu = true;
                StarForge.Game.MatchSettings.spectate = true;
                StarForge.Game.MatchSettings.aiMemory = false;
                StarForge.Game.MatchSettings.difficulty = StarForge.AI.AIDifficulty.Commander;
                UnityEngine.Time.timeScale = 1f;
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                yield return new UnityEngine.WaitForSecondsRealtime(3f);
                var boot = StarForge.Game.GameBootstrap.Instance;
                var world = StarForge.World.GameWorld.Instance;
                if (world == null || !world.running) { boot.StartMatch(); yield return new UnityEngine.WaitForSecondsRealtime(1.5f); world = StarForge.World.GameWorld.Instance; }
                StarForge.AI.Commander.Suspended = false;
                UnityEngine.Time.timeScale = 8f;
                // The Digger-built case comes later, when they have an army to answer it with.
                while (world.time < (c >= 6 ? 90f : 30f)) yield return null;

                var spot = SpotFor(world, c);
                if (spot == null) { log.AppendLine($"case {c} ({Cases[c]}): no buildable spot found"); continue; }
                var F0 = world.factions[0];
                // The side's own AI may have raised a bay already: this case replaces it.
                if (StarForge.World.Unit.Live(F0.bay)) { world.units.Remove(F0.bay); UnityEngine.Object.Destroy(F0.bay.gameObject); }
                StarForge.World.Unit bay, digger = null;
                float walked = 0f;
                if (c >= 6)
                {
                    // As a player would: walk a Digger over, and place the bay once it is there
                    // (a site placed from afar goes up at once and is shot down before it arrives).
                    F0.bayPlaced = false; F0.bay = null; F0.bayLost = false; F0.mechDropAt = -1f;
                    foreach (var u in world.units)
                        if (u != null && !u.dying && u.team == 0 && u.Type == StarForge.Sim.UnitType.Worker && u.order != StarForge.Sim.Order.Build) { digger = u; break; }
                    float setOff = world.time;
                    // Its own Commander would put an idle Digger back to work: keep it walking.
                    while (digger != null && StarForge.World.Unit.Live(digger) && (digger.pos - spot.Value).magnitude > 7f && world.time < setOff + 120f)
                    {
                        F0.bayPlaced = true;   // nor may that Commander raise its own meanwhile
                        if (digger.order != StarForge.Sim.Order.Move || (digger.orderPos - spot.Value).magnitude > 1f)
                        {
                            digger.harvestNode = null; digger.buildTarget = null;
                            digger.order = StarForge.Sim.Order.Move;
                            digger.MoveTo(spot.Value);
                        }
                        yield return null;
                    }
                    walked = world.time - setOff;
                    F0.bayPlaced = false;
                    F0.ore += StarForge.Sim.Defs.Get(StarForge.Sim.UnitType.MechBay).cost;
                    if (digger == null || !StarForge.World.Unit.Live(digger) || !world.CmdBuild(new[] { digger }, StarForge.Sim.UnitType.MechBay, spot.Value))
                    {
                        log.AppendLine($"case {c} ({Cases[c]}): {(digger != null && !StarForge.World.Unit.Live(digger) ? $"the Digger was killed on the way in, {walked:0} s after it set off" : "could not order the build")}");
                        continue;
                    }
                    bay = F0.bay;
                }
                else
                {
                    bay = world.Spawn(StarForge.Sim.UnitType.MechBay, 0, spot.Value, true, 0f);
                    F0.bayPlaced = true; F0.bay = bay; F0.bayLost = false;
                    F0.mechDropAt = world.time + StarForge.World.GameWorld.MechDropDelay;
                }
                // What of theirs died round it while it stood, and when it was finished.
                int theirWorkersLost = 0, theirArmyLost = 0;
                float completeAt = -1f, diggerDiedAt = -1f, diggerArrivedAt = -1f, theirArmyAtOrder = ArmyOf(world, 1), theirArmyAtFinish = -1f;
                System.Action<StarForge.World.GameEvent> onEvent = e =>
                {
                    if (e.kind != StarForge.World.GameEventKind.Death || e.team != 1 || e.unit == null || !StarForge.World.Unit.Live(bay)) return;
                    if ((e.unit.pos - bay.pos).magnitude > 30f) return;
                    if (e.type == StarForge.Sim.UnitType.Worker) theirWorkersLost++;
                    else if (!StarForge.Sim.Defs.Get(e.type).building) theirArmyLost++;
                };
                world.Event += onEvent;

                var enemyBase = world.Map.StartPos(1);
                float placedAt = world.time, bayDiedAt = -1f, nearestFoe = 1e9f, lastBayHp = bay.hp;
                float mechLandedAt = -1f, mechDiedAt = -1f, dropDist = -1f, dropDepth = 0f, maxDistFromBay = 0f;
                bool dropWalkable = true;
                // A trip is a new docking after ten seconds away from the gantry (repairing,
                // withdrawing to it or fighting beside it); flicking between those is not one.
                int repairTrips = 0, repairsInEnemyReach = 0; float lastAtBayT = -99f;
                var intents = new System.Collections.Generic.Dictionary<string, float>();
                // The last few things it was doing, and where, for a Mech that is lost.
                var trail = new System.Collections.Generic.List<string>();
                string lastWhy = null; float lastHp = 0f; UnityEngine.Vector2 lastPos = default;
                int exceptionsBefore = ExceptionCount();
                float lastT = world.time;
                while (world.time < placedAt + 420f && world.winner < 0)
                {
                    yield return null;
                    float dt = world.time - lastT; lastT = world.time;
                    if (completeAt < 0f && StarForge.World.Unit.Live(bay) && bay.Complete) { completeAt = world.time; theirArmyAtFinish = ArmyOf(world, 1); }
                    if (digger != null)
                    {
                        if (diggerDiedAt < 0f && !StarForge.World.Unit.Live(digger)) diggerDiedAt = world.time;
                        else if (diggerArrivedAt < 0f && StarForge.World.Unit.Live(bay) && (digger.pos - bay.pos).magnitude < bay.def.radius + 4f) diggerArrivedAt = world.time;
                    }
                    if (StarForge.World.Unit.Live(bay))
                    {
                        foreach (var u in world.units)
                            if (u != null && !u.dying && u.team == 1 && !u.def.building && u.def.Armed)
                                nearestFoe = UnityEngine.Mathf.Min(nearestFoe, (u.pos - bay.pos).magnitude);
                        lastBayHp = bay.hp;
                    }
                    else if (bayDiedAt < 0f) bayDiedAt = world.time;
                    var m = world.MechOf(0);
                    if (m != null && m.mech.Landed)
                    {
                        if (mechLandedAt < 0f)
                        {
                            mechLandedAt = world.time;
                            dropDist = F0.bay != null ? (m.pos - F0.bay.pos).magnitude : -1f;
                            dropWalkable = world.Walkable(m.pos);
                            dropDepth = world.Map.WaterDepth(m.pos);
                        }
                        if (StarForge.World.Unit.Live(bay)) maxDistFromBay = UnityEngine.Mathf.Max(maxDistFromBay, (m.pos - bay.pos).magnitude);
                        string why = System.Text.RegularExpressions.Regex.Replace(m.mech.intentText ?? "", @"\(.*?\)|[0-9%]+| (at the base|on their side,.*|(north|south|east|west).*)$", "").Trim();
                        intents.TryGetValue(why, out float had); intents[why] = had + dt;
                        if (why != lastWhy)
                        {
                            lastWhy = why;
                            var foeM = world.MechOf(1);
                            trail.Add($"{world.time - placedAt:0}s {why} hp {100f * m.hp / UnityEngine.Mathf.Max(1f, m.MaxHp):0}% " +
                                      $"bay {(StarForge.World.Unit.Live(bay) ? (m.pos - bay.pos).magnitude : -1f):0}m base {(m.pos - enemyBase).magnitude:0}m" +
                                      (foeM != null && foeM.mech.Landed ? $" theirMech {(m.pos - foeM.pos).magnitude:0}m" : "") +
                                      InReach(world, m));
                            if (trail.Count > 8) trail.RemoveAt(0);
                        }
                        lastHp = m.hp; lastPos = m.pos;
                        string txt = m.mech.intentText ?? "";
                        bool rep = m.mech.intent == StarForge.World.MechIntent.Repairing || txt.Contains("Fighting at the bay") ||
                                   (m.mech.intent == StarForge.World.MechIntent.Withdrawing && txt.Contains("repair"));
                        if (rep)
                        {
                            if (world.time - lastAtBayT > 10f)
                            {
                                repairTrips++;
                                if (StarForge.World.Unit.Live(bay) && (bay.pos - enemyBase).magnitude < 60f) repairsInEnemyReach++;
                            }
                            lastAtBayT = world.time;
                        }
                    }
                    else if (F0.mechDropped && mechDiedAt < 0f && F0.mechLost) mechDiedAt = world.time;
                }
                world.Event -= onEvent;
                var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, float>>(intents);
                list.Sort((a, b) => b.Value.CompareTo(a.Value));
                float total = 0f; foreach (var kv in list) total += kv.Value;
                var top = new System.Collections.Generic.List<string>();
                for (int i = 0; i < list.Count && i < 5; i++) top.Add($"\"{list[i].Key}\" {100f * list[i].Value / UnityEngine.Mathf.Max(1f, total):0}%");
                var ai1 = boot.AI;
                log.AppendLine($"case {c} ({Cases[c]}): bay at ({spot.Value.x:0},{spot.Value.y:0}), {(spot.Value - enemyBase).magnitude:0} m from their base; " +
                               (bayDiedAt >= 0f ? $"bay destroyed {bayDiedAt - placedAt:0} s after it went up" : $"bay standing at the end ({lastBayHp:0} hp)") +
                               $"; their nearest armed unit came to {nearestFoe:0} m of it; they lost {theirWorkersLost} Diggers and {theirArmyLost} fighting units round it while it stood");
                if (c >= 6)
                    log.AppendLine($"   built: walked in {walked:0} s; Digger {(diggerArrivedAt >= 0f ? $"at work {diggerArrivedAt - placedAt:0} s after the order" : "never started")}" +
                                   $"{(diggerDiedAt >= 0f ? $", died {diggerDiedAt - placedAt:0} s after it" : "")}; " +
                                   (completeAt >= 0f ? $"finished {completeAt - placedAt:0} s after the order" : "never finished") +
                                   $"; their army {theirArmyAtOrder:0} at the order, {(theirArmyAtFinish >= 0f ? $"{theirArmyAtFinish:0}" : "-")} when it was finished");
                log.AppendLine($"   Mech: " + (mechLandedAt < 0f ? (F0.mechLost ? "never came (bay lost first)" : "not landed") :
                               $"landed {dropDist:0.0} m from the bay, ground {(dropWalkable ? "walkable" : "NOT walkable")}{(dropDepth > 0f ? $", {dropDepth:0.0} m of water" : "")}; " +
                               (mechDiedAt >= 0f ? $"destroyed {mechDiedAt - mechLandedAt:0} s after landing" : "alive at the end") +
                               $"; repair trips {repairTrips} ({repairsInEnemyReach} to a bay within 60 m of their base); strayed up to {maxDistFromBay:0} m from its bay"));
                log.AppendLine($"   its time: {string.Join(", ", top)}");
                if (mechDiedAt >= 0f) log.AppendLine($"   before it fell: {string.Join(" | ", trail)}");
                if (ai1 != null && ai1.Dbg.guns.Length > 0)
                    log.AppendLine($"   their Diggers: {ai1.Dbg.guns}; moved to safe ore {ai1.Dbg.diggersMoved}, stood clear {ai1.Dbg.diggersCleared}");
                log.AppendLine($"   their AI: {(ai1 != null ? ai1.Dbg.wave : "?")}; winner {world.winner}{(world.winner >= 0 ? $" at {world.time:0} s" : "")}; " +
                               $"armies at the end {ArmyOf(world, 0):0} v {ArmyOf(world, 1):0}, Diggers {WorkersOf(world, 0)} v {WorkersOf(world, 1)}; exceptions {ExceptionCount() - exceptionsBefore}");
            }
            UnityEngine.Time.timeScale = 1f;
            StarForge.AI.Commander.IgnoreSites = false;
            report = "done\n" + log;
        }

        static float ArmyOf(StarForge.World.GameWorld world, int team)
        {
            float v = 0f;
            foreach (var u in world.units) if (u != null && !u.dying && u.team == team && u.def.IsArmy) v += StarForge.Sim.Defs.ArmyValue(u.Type);
            return v;
        }

        static int WorkersOf(StarForge.World.GameWorld world, int team)
        {
            int n = 0;
            foreach (var u in world.units) if (u != null && !u.dying && u.team == team && u.Type == StarForge.Sim.UnitType.Worker) n++;
            return n;
        }

        /// <summary>The nearest enemy that has the Mech in its reach, and how many do.</summary>
        static string InReach(StarForge.World.GameWorld world, StarForge.World.Unit m)
        {
            StarForge.World.Unit best = null; float bestD = 1e9f; int n = 0;
            foreach (var e in world.units)
            {
                if (e == null || e.dying || e.team == m.team || e.team > 1 || !e.def.Armed || e.Type == StarForge.Sim.UnitType.Worker) continue;
                float reach = e.mech != null ? e.mech.MaxRange : e.def.range;
                float d = (e.pos - m.pos).magnitude;
                if (d > reach + m.def.radius) continue;
                n++;
                if (d < bestD) { bestD = d; best = e; }
            }
            return best == null ? "" : $" in reach of {n}, nearest {best.Type} {bestD:0}m";
        }

        static int exceptions;
        static bool hooked;
        static int ExceptionCount()
        {
            if (!hooked)
            {
                hooked = true;
                UnityEngine.Application.logMessageReceived += (msg, trace, type) => { if (type == UnityEngine.LogType.Exception) exceptions++; };
            }
            return exceptions;
        }

        /// <summary>A spot a Digger could build the bay on for each case, on map seed 1000.</summary>
        static UnityEngine.Vector2? SpotFor(StarForge.World.GameWorld world, int c)
        {
            var map = world.Map;
            var mine = map.StartPos(0);
            var theirs = map.StartPos(1);
            var centre = new UnityEngine.Vector2(world.MapSize * 0.5f, world.MapSize * 0.5f);
            UnityEngine.Vector2 want;
            switch (c)
            {
                case 0: case 6: want = theirs + (centre - theirs).normalized * 12f; break;
                case 1: case 7: want = theirs + (centre - theirs).normalized * 45f; break;
                case 2:
                {
                    // The corner furthest from both bases.
                    var corners = new[] { new UnityEngine.Vector2(20f, 20f), new UnityEngine.Vector2(world.MapSize - 20f, 20f), new UnityEngine.Vector2(20f, world.MapSize - 20f), new UnityEngine.Vector2(world.MapSize - 20f, world.MapSize - 20f) };
                    want = corners[0]; float best = -1f;
                    foreach (var k in corners)
                    {
                        float d = UnityEngine.Mathf.Min((k - mine).magnitude, (k - theirs).magnitude);
                        if (d > best) { best = d; want = k; }
                    }
                    break;
                }
                case 3:
                {
                    // Dry ground with deep water within six metres, nearest the middle.
                    want = centre; float best = 1e9f;
                    for (float x = 20f; x < world.MapSize - 20f; x += 3f)
                        for (float y = 20f; y < world.MapSize - 20f; y += 3f)
                        {
                            var p = new UnityEngine.Vector2(x, y);
                            if (map.WaterDepth(p) > -0.1f) continue;
                            bool shore = false;
                            for (int a = 0; a < 8 && !shore; a++)
                                if (map.WaterDepth(p + new UnityEngine.Vector2(UnityEngine.Mathf.Cos(a * 0.785f), UnityEngine.Mathf.Sin(a * 0.785f)) * 9f) > 0.3f) shore = true;
                            if (!shore || !world.CanPlace(StarForge.Sim.UnitType.MechBay, p)) continue;
                            float d = (p - centre).magnitude;
                            if (d < best) { best = d; want = p; }
                        }
                    return best < 1e8f ? want : (UnityEngine.Vector2?)null;
                }
                case 4:
                {
                    // Where the most trees stand round a spot the bay still fits.
                    want = centre; int best = -1;
                    var near = new System.Collections.Generic.List<int>();
                    for (float x = 20f; x < world.MapSize - 20f; x += 3f)
                        for (float y = 20f; y < world.MapSize - 20f; y += 3f)
                        {
                            var p = new UnityEngine.Vector2(x, y);
                            world.Plants.PlantsNear(p, 12f, near);
                            int n = 0;
                            foreach (int i in near) if (world.Plants.Blocks(i)) n++;
                            if (n <= best || !world.CanPlace(StarForge.Sim.UnitType.MechBay, p)) continue;
                            best = n; want = p;
                        }
                    return best >= 0 ? want : (UnityEngine.Vector2?)null;
                }
                default:
                {
                    var dir = (theirs - mine).normalized;
                    want = mine + new UnityEngine.Vector2(-dir.y, dir.x) * 90f;
                    break;
                }
            }
            // The nearest spot round the wanted one where the bay can go.
            for (float r = 0f; r < 40f; r += 2f)
                for (int a = 0; a < 16; a++)
                {
                    var p = want + new UnityEngine.Vector2(UnityEngine.Mathf.Cos(a * 0.3927f), UnityEngine.Mathf.Sin(a * 0.3927f)) * r;
                    if (map.InBounds(p, 8f) && world.CanPlace(StarForge.Sim.UnitType.MechBay, p)) return p;
                    if (r == 0f) break;
                }
            return null;
        }
    }
}
