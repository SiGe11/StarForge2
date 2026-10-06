// FxLook.cs — photographs the fire, the explosions and the physics that follow them, in
// play mode, for judging the flipbooks and the blast shoves (Temp/fx_*.png, 2x):
//
//   Tools/editor.sh call StarForge.EditorTools.FxLook.Run      (then Report ~60 s later)
//
// A shell burst on open ground as it goes from flash to fireball to smoke; riflemen and
// a tank killed and left to settle, then a shell landing among the wrecks (how far each
// was thrown); a Bunkhouse destroyed, its chunks flying and landing (how many hard
// landings the ContactRelays reported); a tree set alight, close; and the Foundry's
// banner in the wind. Effect events are raised directly where damage does not matter,
// so the pictures repeat on a seed. Never restarts a running match.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.Sim;
using StarForge.View;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class FxLook
    {
        public static string Run()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var go = new GameObject("FxLook");
            go.AddComponent<FxLooker>();
            return "looking; call StarForge.EditorTools.FxLook.Report in about 60 seconds";
        }

        public static string Report()
        {
            var l = Object.FindAnyObjectByType<FxLooker>();
            return l == null ? "no look running" : l.report;
        }
    }

    public sealed class FxLooker : MonoBehaviour
    {
        public string report = "looking";

        IEnumerator Start()
        {
            var boot = StarForge.Game.GameBootstrap.Instance;
            var world = GameWorld.Instance;
            if (boot == null || world == null) { report = "no world"; yield break; }
            if (!world.running)
            {
                StarForge.Game.MatchSettings.spectate = false;
                StarForge.Game.MatchSettings.aiMemory = false;
                StarForge.Game.MatchSettings.fixedSeed = true;
                boot.StartMatch();
                yield return new WaitForSecondsRealtime(1.5f);
            }
            // An idle opponent, so nothing wanders into the pictures (cleared at the end:
            // it is a static and would outlive the play session).
            StarForge.AI.Commander.Suspended = true;
            var rig = Object.FindAnyObjectByType<RTSCamera>();
            var map = MapInfo.Instance;
            var veg = world.Plants;
            if (rig == null || map == null) { report = "no camera/map"; StarForge.AI.Commander.Suspended = false; yield break; }
            var sb = new StringBuilder();
            var basePos = map.StartPos(0);
            var toward = (map.StartPos(1) - basePos).normalized;
            var across = new Vector2(toward.y, -toward.x);
            Vector2 at = world.NearestWalkable(basePos + toward * 26f, 10f);
            rig.scripted = true;
            Time.timeScale = 1f;
            rig.JumpTo(at);
            rig.ZoomTo(28f);
            yield return new WaitForSecondsRealtime(4f);
            Vector3 p = map.Ground(at);

            // 1. A Mauler's shell bursting on open ground, slowed to a third.
            Time.timeScale = 0.35f;
            world.Raise(new GameEvent { kind = GameEventKind.Impact, team = 0, pos = p + Vector3.up * 0.4f,
                                        dir = Vector3.forward, scale = 1.35f, projectileKind = 1 });
            float t0 = Time.time;
            foreach (var t in new[] { 0.08f, 0.25f, 0.5f, 0.9f, 1.5f, 2.4f })
            {
                while (Time.time - t0 < t) yield return null;
                ScreenCapture.CaptureScreenshot($"Temp/fx_shell_{t:0.00}.png", 2);
            }
            Time.timeScale = 1f;
            yield return new WaitForSecondsRealtime(3f);

            // 2. Wrecks: three riflemen and a tank killed and left to settle, then a shell
            // among them. How far each is thrown is the blast's shove.
            var victims = new List<Unit>();
            var kinds = new[] { UnitType.Trooper, UnitType.Trooper, UnitType.Trooper, UnitType.Mauler };
            Vector2 wat = world.NearestWalkable(at + across * 16f, 8f);
            for (int n = 0; n < kinds.Length; n++)
            {
                var u = world.Spawn(kinds[n], 1, world.NearestWalkable(wat + across * ((n - 1.5f) * 3.2f), 4f), true, 0f);
                u.order = Order.Hold;
                victims.Add(u);
            }
            rig.JumpTo(wat);
            yield return new WaitForSeconds(1.2f);
            foreach (var u in victims) world.Damage(u, 1e6f, u.pos + toward * 2f, null, false);
            yield return new WaitForSeconds(4f);
            var before = new Dictionary<UnitWreck, Vector3>();
            foreach (var w in UnitWreck.Active)
                if (w != null && w.HullTransform != null) before[w] = w.HullTransform.position;
            ScreenCapture.CaptureScreenshot("Temp/fx_wrecks_before.png", 2);
            Time.timeScale = 0.5f;
            Vector3 wp = map.Ground(wat + toward * 1.5f);
            world.Raise(new GameEvent { kind = GameEventKind.Impact, team = 0, pos = wp + Vector3.up * 0.4f,
                                        dir = Vector3.forward, scale = 1.35f, projectileKind = 1 });
            t0 = Time.time;
            foreach (var t in new[] { 0.2f, 0.6f, 1.4f })
            {
                while (Time.time - t0 < t) yield return null;
                ScreenCapture.CaptureScreenshot($"Temp/fx_wrecks_{t:0.0}.png", 2);
            }
            Time.timeScale = 1f;
            yield return new WaitForSeconds(2f);
            sb.Append($"wrecks ({before.Count}) thrown by the shell:");
            foreach (var kv in before)
                if (kv.Key != null && kv.Key.HullTransform != null)
                    sb.Append($" {kv.Key.type} {Vector3.Distance(kv.Value, kv.Key.HullTransform.position):0.0} m;");

            // 3. A Bunkhouse destroyed: its chunks thrown, trailing smoke, landing.
            int landings0 = FXDirector.landings;
            Vector2 bat = world.NearestWalkable(at - across * 16f, 8f);
            var bunk = world.Spawn(UnitType.Bunkhouse, 1, bat, true, 0f);
            rig.JumpTo(bat);
            yield return new WaitForSeconds(1.5f);
            Time.timeScale = 0.4f;
            world.Damage(bunk, 1e6f, bat + toward * 3f, null, true);
            t0 = Time.time;
            foreach (var t in new[] { 0.2f, 0.5f, 1.0f, 1.8f, 3.0f })
            {
                while (Time.time - t0 < t) yield return null;
                ScreenCapture.CaptureScreenshot($"Temp/fx_bunk_{t:0.0}.png", 2);
            }
            Time.timeScale = 1f;
            yield return new WaitForSeconds(2f);
            sb.Append($"\nBunkhouse: {DebrisBurst.Active.Count} bursts live, {FXDirector.landings - landings0} hard landings reported");

            // 4. A tree burning, close.
            if (veg != null)
            {
                int tree = -1;
                float bestD = 1e9f;
                for (int i = 0; i < veg.plants.Length; i++)
                {
                    if (veg.KindOf(i).bush || veg.live[i].state != PlantState.Standing) continue;
                    float d = (veg.Pos2(i) - at).sqrMagnitude;
                    if (d < bestD) { bestD = d; tree = i; }
                }
                if (tree >= 0)
                {
                    rig.JumpTo(veg.Pos2(tree));
                    rig.ZoomTo(22f);
                    veg.Ignite(world, tree, 0, 1f);
                    yield return new WaitForSecondsRealtime(5f);
                    ScreenCapture.CaptureScreenshot("Temp/fx_tree_a.png", 2);
                    yield return new WaitForSecondsRealtime(5f);
                    ScreenCapture.CaptureScreenshot("Temp/fx_tree_b.png", 2);
                    sb.Append($"\nburnt the tree {Mathf.Sqrt(bestD):0} m off");
                }
            }

            // 5. The Foundry and its banner, close.
            rig.JumpTo(basePos);
            rig.ZoomTo(24f);
            yield return new WaitForSecondsRealtime(3f);
            ScreenCapture.CaptureScreenshot("Temp/fx_banner.png", 2);
            var banners = Object.FindAnyObjectByType<Banners>();
            sb.Append($"\nbanners: {(banners != null ? banners.Count : -1)}");

            StarForge.AI.Commander.Suspended = false;
            report = sb.ToString();
        }
    }
}
