// UXTrials.cs — the check for the RTS conveniences of 2026-11-24 (PlayerController.Hotkeys,
// HUDController.UX): control groups and the group bar, camera spots, the alert jump (Tab),
// the Foundry key, idle Diggers, the dispatches, and the bar's garbage. Through the public
// methods the keys call (the keys themselves need the Game view focused, which an agent's
// Editor in the background does not have).
//   Tools/editor.sh call StarForge.EditorTools.UXTrials.Run
//   Tools/editor.sh wait StarForge.EditorTools.UXTrials.RunReport
//   Tools/editor.sh call StarForge.EditorTools.UXTrials.Shot     (Temp/ux_hud.png, the HUD as it stands)
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.Game;
using StarForge.Sim;
using StarForge.UI;
using StarForge.View;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class UXTrials
    {
        /// <summary>Targets: groups 1:6 then 1:5 after a death, Shift+# keeps the selection; the bar
        /// shows what the groups hold, * on the selected one; a camera spot comes back within 0.5 m,
        /// 1 degree and 0.5 of zoom; an empty spot says so; Tab goes to the newest fight within 6 m
        /// and, again, to the one before; Backspace selects a Foundry; F1 steps through idle Diggers
        /// one by one, Shift/Ctrl+F1 takes them all; two fronts at once give 2-4 under-attack dispatches in 15 s, a raid inside the 3 s gap is told after it; the bar's steady refresh 0 B a call.</summary>
        public static string Run()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<UXRunTrial>();
            if (old != null) Object.Destroy(old.gameObject);
            new GameObject("UXRunTrial").AddComponent<UXRunTrial>();
            return "staging; call UXTrials.RunReport in about a minute";
        }

        public static string RunReport()
        {
            var r = Object.FindAnyObjectByType<UXRunTrial>();
            return r == null ? "no trial running" : r.report;
        }

        /// <summary>A screenshot of the Game view as it stands (Temp/ux_hud.png), at the end of the frame.</summary>
        public static string Shot()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            ScreenCapture.CaptureScreenshot("Temp/ux_hud.png");
            return "Temp/ux_hud.png at the end of the frame";
        }

        /// <summary>The restyle to look at (after Run, in the same match; never restarts one): one dispatch of
        /// each kind, a knight's call and a quoted line in the comms, group 1 selected on the bar, then
        /// Temp/ux_look.png. Calls the HUD's own Dispatch and Comms, so nothing reaches the world's events.</summary>
        public static string Look()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            UXLookShot.report = "looking";
            new GameObject("UXLookShot").AddComponent<UXLookShot>();
            return "looking; call UXTrials.LookReport in a few seconds";
        }

        public static string LookReport() => UXLookShot.report ?? "no look running";
    }

    /// <summary>UXTrials.Look's frames (a MonoBehaviour not named after its file: see Editor/CLAUDE.md).</summary>
    public sealed class UXLookShot : MonoBehaviour
    {
        public static string report;

        [InitializeOnEnterPlayMode]
        static void ClearOnPlay() => report = null;

        IEnumerator Start()
        {
            var world = GameWorld.Instance;
            var pc = FindAnyObjectByType<PlayerController>();
            var hud = FindAnyObjectByType<HUDController>();
            if (world == null || !world.running || pc == null || hud == null) { report = "no match running"; Destroy(gameObject); yield break; }
            int me = pc.team;
            // Group 1 for the bar: Run's (its Troopers are gone after it) or up to five of ours that move.
            bool hasGroup = false;
            foreach (var u in pc.Group(1)) if (Unit.Live(u)) { hasGroup = true; break; }
            if (!hasGroup)
            {
                var ours = new List<Unit>();
                foreach (var u in world.units) if (Unit.Live(u) && u.team == me && u.def.IsMobile && !u.def.Autonomous && ours.Count < 5) ours.Add(u);
                pc.SelectUnits(ours);
                pc.SetGroup(1);
            }
            pc.RecallGroup(1);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var dispatch = typeof(HUDController).GetMethod("Dispatch", flags);
            var comms = typeof(HUDController).GetMethod("Comms", flags);
            Vector2 at = MapInfo.Instance.StartPos(me);
            dispatch.Invoke(hud, new object[] { "FIELD NET · BY THE GARRISON", "Your base is under attack", "alert--warn", 40f, (Vector2?)at });
            dispatch.Invoke(hud, new object[] { "THE GUILD", "Bunkhouse complete", "alert--good", 40f, (Vector2?)at });
            dispatch.Invoke(hud, new object[] { null, "Not enough ore", "alert--refused", 40f, null });
            dispatch.Invoke(hud, new object[] { null, "Camera spot 2 saved", "alert--hint", 40f, null });
            comms.Invoke(hud, new object[] { new GameEvent { kind = GameEventKind.MechAdvice, team = me, advice = MechAdviceKind.Attack,
                text = "Commander, their Maulers are strung out along the north ford. Strike them now.", pos = new Vector3(at.x + 60f, 0f, at.y + 60f) } });
            comms.Invoke(hud, new object[] { new GameEvent { kind = GameEventKind.MechAdvice, team = me, advice = MechAdviceKind.Morale,
                text = "\"Steel does not possess courage. It possesses purpose.\"", pos = new Vector3(at.x, 0f, at.y) } });
            yield return new WaitForSecondsRealtime(1f);
            // At 2x: the Editor's Game view is small, and the HUD shrinks with it.
            ScreenCapture.CaptureScreenshot("Temp/ux_look.png", 2);
            yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            report = "Temp/ux_look.png written; dispatches: " + hud.DispatchesNow() + "; bar: " + hud.GroupBarNow();
            Destroy(gameObject);
        }
    }

    public sealed class UXRunTrial : BugTrial
    {
        readonly StringBuilder sb = new StringBuilder();
        int fails;

        void Check(bool ok, string line)
        {
            if (!ok) fails++;
            sb.AppendLine((ok ? "  PASS " : "  FAIL ") + line);
        }

        IEnumerator Start()
        {
            yield return EnsureMatch();
            if (!Ready) { report = "no match running"; yield break; }
            var pc = FindAnyObjectByType<PlayerController>();
            var hud = FindAnyObjectByType<HUDController>();
            var rig = FindAnyObjectByType<RTSCamera>();
            if (pc == null || hud == null || rig == null) { report = "no PlayerController, HUD or camera"; yield break; }
            report = "running";
            HoldStatics();
            var rng = new System.Random(2411);
            int me = pc.team;

            // ---------------------------------------------------------------- control groups
            Vector2 home = map.StartPos(me);
            Vector2 toMid = (new Vector2(world.MapSize, world.MapSize) * 0.5f - home).normalized;
            var men = new List<Unit>();
            for (int i = 0; i < 6; i++)
                men.Add(Put(UnitType.Trooper, me, home + toMid * 30f + new Vector2(i * 2.5f, (i % 2) * 2.5f)));
            var diggers = new List<Unit>();
            foreach (var u in world.units) if (Unit.Live(u) && u.team == me && u.Type == UnitType.Worker && diggers.Count < 2) diggers.Add(u);
            yield return null;

            Select(pc, men.GetRange(0, 4));
            pc.SetGroup(1);
            Select(pc, men.GetRange(4, 2));
            pc.AddToGroup(1);
            Check(pc.Group(1).Count == 6 && pc.Selection.Count == 2,
                  $"Ctrl+1 on 4 then Shift+1 on 2: group 1 holds {pc.Group(1).Count} (6), the selection still {pc.Selection.Count} (2)");
            Select(pc, diggers);
            pc.SetGroup(2);
            pc.RecallGroup(1);
            Check(pc.Selection.Count == 6, $"1 recalls group 1: {pc.Selection.Count} selected (6)");
            pc.RecallGroup(2, add: true);
            Check(pc.Selection.Count == 6 + diggers.Count, $"the bar's Shift-click adds group 2: {pc.Selection.Count} selected ({6 + diggers.Count})");
            pc.RecallGroup(1);
            yield return new WaitForSecondsRealtime(0.35f);
            string bar = hud.GroupBarNow();
            Check(bar.Contains("1:6 Trooper*") && bar.Contains($"2:{diggers.Count} Worker") && !bar.Contains("2:" + diggers.Count + " Worker*"),
                  $"the bar with group 1 selected: {bar}");
            Remove(men[0]);
            yield return Wait(3f);
            bar = hud.GroupBarNow();
            Check(bar.Contains("1:5 Trooper"), $"a man of group 1 dies: {bar}");

            // ---------------------------------------------------------------- camera spots
            var a = new RTSCamera.Shot { focus = home + toMid * 20f, yaw = 30f, distance = 60f };
            var b = new RTSCamera.Shot { focus = home + toMid * 120f, yaw = 150f, distance = 110f };
            rig.GoTo(a);
            yield return new WaitForSecondsRealtime(1.5f);
            pc.SaveCameraSpot(0);
            rig.GoTo(b);
            yield return new WaitForSecondsRealtime(1.5f);
            bool back = pc.RecallCameraSpot(0);
            yield return new WaitForSecondsRealtime(1.5f);
            float dFocus = (rig.Focus - a.focus).magnitude, dYaw = Mathf.Abs(Mathf.DeltaAngle(rig.yaw, a.yaw)), dDist = Mathf.Abs(rig.distance - a.distance);
            Check(back && dFocus < 0.5f && dYaw < 1f && dDist < 0.5f,
                  $"camera spot 1 saved, moved away, recalled: off by {dFocus:0.00} m, {dYaw:0.0} deg, {dDist:0.00} zoom");
            bool empty = pc.RecallCameraSpot(3);
            yield return null;
            Check(!empty && hud.DispatchesNow().Contains("Camera spot 4 is empty"), $"an empty spot: recalled {empty}, says: {Last(hud.DispatchesNow())}");

            // ---------------------------------------------------------------- the alert jump
            // Two real fights in two places 80 m apart, 4 s between: each an alert with its place.
            // Open dry ground away from both bases (FindField), the two at least 60 m apart.
            if (!FindField(rng, out Vector2 f1, out _)) { report = "no open ground for the fights"; ReleaseStatics(); yield break; }
            Vector2 f2 = f1;
            for (int tries = 0; tries < 40 && (f2 - f1).magnitude < 60f; tries++) FindField(rng, out f2, out _);
            var fightA = Fight(me, f1);
            // A man may run for cover before he is hit: the place counts as A's if it is within 30 m of A.
            yield return WaitFor(() => hud.AlertPlace(0).HasValue && (hud.AlertPlace(0).Value - f1).magnitude < 30f, 20f);
            Vector2? atA = hud.AlertPlace(0);
            Check(atA.HasValue && (atA.Value - f1).magnitude < 30f, $"a fight at A: the newest alert's place is {Dist(atA, f1)} m from it (30); dispatches: {hud.DispatchesNow()}");
            // Each fight ends once its alert is in: a promotion from it later would put its place first again.
            foreach (var u in fightA) Remove(u);
            // The under-attack alert comes again for a fight elsewhere (45 m off) after 3 s of real time.
            yield return new WaitForSecondsRealtime(4f);
            var fightB = Fight(me, f2);
            yield return WaitFor(() => hud.AlertPlace(0).HasValue && (hud.AlertPlace(0).Value - f2).magnitude < 30f, 20f);
            Vector2? atB = hud.AlertPlace(0);
            Check(atB.HasValue && (atB.Value - f2).magnitude < 30f, $"a fight at B, {(f2 - f1).magnitude:0} m off, 4 s later: newest place {Dist(atB, f2)} m from it (30)");
            foreach (var u in fightB) Remove(u);
            pc.JumpToAlert();
            yield return new WaitForSecondsRealtime(1.2f);
            float toB = atB.HasValue ? (rig.Focus - atB.Value).magnitude : 999f;
            pc.JumpToAlert();
            yield return new WaitForSecondsRealtime(1.2f);
            float toA = atA.HasValue ? (rig.Focus - atA.Value).magnitude : 999f;
            Check(toB < 3f && toA < 3f, $"Tab: to B's alert ({toB:0.0} m off), Tab again within 3 s: to A's ({toA:0.0} m off)");

            // Two fronts at once for 15 s of real time: a Workshop of ours (1,250 hp: a Bunkhouse's 400 fell
            // in ~3 s at 3x) under two rifles at each, 60 m+ apart. One alert a front per 10 s, 3 s apart at
            // least: 2-4 in the span when both fronts were fired on (the world's own events counted apart).
            Vector2 g1 = f1, g2 = f1;
            for (int tries = 0; tries < 40 && ((g1 - f1).magnitude < 60f || (g1 - f2).magnitude < 60f); tries++) FindField(rng, out g1, out _);
            for (int tries = 0; tries < 40 && ((g2 - g1).magnitude < 60f || (g2 - f1).magnitude < 60f || (g2 - f2).magnitude < 60f); tries++) FindField(rng, out g2, out _);
            var fronts = new List<Unit>();
            foreach (var g in new[] { g1, g2 })
            {
                fronts.Add(Put(UnitType.Workshop, me, g));
                fronts.Add(Put(UnitType.Trooper, 1 - me, g + new Vector2(9f, 0f)));
                fronts.Add(Put(UnitType.Trooper, 1 - me, g + new Vector2(9f, 3f)));
            }
            int shown0 = hud.UnderAttackShown, hit1 = 0, hit2 = 0;
            Unit w1 = fronts[0], w2 = fronts[3];
            ListenWhileHeld(e =>
            {
                if (e.kind != GameEventKind.UnderAttack || e.team != me) return;
                if (ReferenceEquals(e.unit, w1)) hit1++;
                else if (ReferenceEquals(e.unit, w2)) hit2++;
            });
            yield return new WaitForSecondsRealtime(15f);
            ListenWhileHeld(null);
            int fronts15 = hud.UnderAttackShown - shown0;
            Check(hit1 > 0 && hit2 > 0 && fronts15 >= 2 && fronts15 <= 4,
                  $"two fronts {(g2 - g1).magnitude:0} m apart for 15 s: the world's alarms {hit1} and {hit2}, {fronts15} under-attack dispatches (2-4; both fronts must be fired on)");
            foreach (var u in fronts) Remove(u);

            // A raid inside the gap, deterministically: the HUD's own handler fed two places 0.5 s apart.
            // The second is held (not dropped) and told once the 3 s gap has passed.
            yield return new WaitForSecondsRealtime(3.5f);
            var underAttack = (System.Action<GameEvent>)System.Delegate.CreateDelegate(typeof(System.Action<GameEvent>), hud,
                typeof(HUDController).GetMethod("UnderAttack", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
            // Two places as far as the map allows from every fight above and from each other (a place within
            // 45 m of one told in the last 10 s stays quiet). Nothing is spawned there, so any ground will do:
            // the farthest points of a 9 x 9 grid (picking open fields, a crowded map once left p2 38 m from p1).
            var told = new List<Vector2> { f1, f2, g1, g2 };
            Vector2 Farthest()
            {
                Vector2 best = default; float bestD = -1f;
                for (int gx = 1; gx <= 9; gx++)
                    for (int gy = 1; gy <= 9; gy++)
                    {
                        var q = new Vector2(gx, gy) * (world.MapSize / 10f);
                        float d = 1e9f;
                        foreach (var t in told) d = Mathf.Min(d, (q - t).magnitude);
                        if (d > bestD) { bestD = d; best = q; }
                    }
                return best;
            }
            Vector2 p1 = Farthest();
            told.Add(p1);
            Vector2 p2 = Farthest();
            int n0 = hud.UnderAttackShown;
            underAttack(new GameEvent { kind = GameEventKind.UnderAttack, team = me, pos = new Vector3(p1.x, 0f, p1.y) });
            yield return new WaitForSecondsRealtime(0.5f);
            underAttack(new GameEvent { kind = GameEventKind.UnderAttack, team = me, pos = new Vector3(p2.x, 0f, p2.y) });
            int inGap = hud.UnderAttackShown - n0;
            // Why, if it fails: held or not, and how near the places told in the last 10 s lie to p2.
            var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            bool held = (bool)typeof(HUDController).GetField("attackHeld", bf).GetValue(hud);
            float nearestTold = 1e9f;
            foreach (var (at, _) in (List<(Vector2 at, float t)>)typeof(HUDController).GetField("attackShown", bf).GetValue(hud))
                nearestTold = Mathf.Min(nearestTold, (at - p2).magnitude);
            string why = $"held {held}, nearest place told in 10 s {nearestTold:0} m (quiet under 45)";
            yield return new WaitForSecondsRealtime(3.2f);
            int afterGap = hud.UnderAttackShown - n0;
            Vector2? newest = hud.AlertPlace(0);
            Check(inGap == 1 && afterGap == 2 && newest.HasValue && (newest.Value - p2).magnitude < 1f,
                  $"a raid {(p2 - p1).magnitude:0} m off 0.5 s after an alert: told {inGap} at once (1), {afterGap} after the gap (2), newest place {Dist(newest, p2)} m from it; {why}");

            // ---------------------------------------------------------------- Foundry and idle Diggers
            bool foundry = pc.NextFoundry();
            yield return new WaitForSecondsRealtime(1f);
            var picked = pc.Selection.Count == 1 ? pc.Selection[0] : null;
            Check(foundry && picked != null && picked.Type == UnitType.Foundry && (rig.Focus - picked.pos).magnitude < 3f,
                  $"Backspace: selected {(picked != null ? picked.Type.ToString() : "nothing")}, camera {(picked != null ? (rig.Focus - picked.pos).magnitude : -1f):0.0} m from it");
            world.CmdStop(diggers);
            yield return null;
            int idle = pc.IdleDiggerCount();
            pc.NextIdleDigger();
            var first = pc.Selection.Count == 1 ? pc.Selection[0] : null;
            pc.NextIdleDigger();
            var second = pc.Selection.Count == 1 ? pc.Selection[0] : null;
            Check(idle >= 2 && first != null && second != null && first != second && pc.IdleDigger(first) && pc.IdleDigger(second),
                  $"F1 twice with {idle} idle: two different idle Diggers ({first?.id}, {second?.id})");
            int all = pc.SelectIdleDiggers();
            yield return new WaitForSecondsRealtime(0.35f);
            Check(all == idle && hud.GroupBarNow().Contains("idle " + idle), $"Ctrl+F1: {all} selected of {idle}; bar: {hud.GroupBarNow()}");

            // ---------------------------------------------------------------- garbage
            // A delegate bound once, so the calls themselves allocate nothing; 0.25 s a call forces a full refresh.
            var tick = (System.Action<float>)System.Delegate.CreateDelegate(typeof(System.Action<float>), hud,
                typeof(HUDController).GetMethod("UpdateUX", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
            pc.RecallGroup(1);
            tick(0.25f);
            long before = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            for (int i = 0; i < 200; i++) tick(0.25f);
            long d = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - before;
            Check(d >= 0 && d / 200f < 1f, d < 0 ? "a collection ran during the measure: run it again" :
                  $"the group bar's full refresh with nothing changed: {d / 200f:0.0} B a call (200 calls, target 0)");

            pc.Selection.Clear();
            // Its Troopers off the field again: left standing they filled the supply, and a HudAlloc run
            // after it could queue nothing at the Foundry.
            ClearSpawned();
            ReleaseStatics();
            report = $"UXTrials.Run: {(fails == 0 ? "all PASS" : fails + " FAIL")}\n" + sb;
        }

        static void Select(PlayerController pc, List<Unit> units)
        {
            pc.Selection.Clear();
            pc.SelectUnits(units);
        }

        /// <summary>One of ours and two of theirs a few metres apart: they fire on sight.</summary>
        List<Unit> Fight(int me, Vector2 at)
        {
            var l = new List<Unit>
            {
                Put(UnitType.Trooper, me, at),
                Put(UnitType.Trooper, 1 - me, at + new Vector2(6f, 0f)),
                Put(UnitType.Trooper, 1 - me, at + new Vector2(6f, 3f)),
            };
            return l;
        }

        IEnumerator WaitFor(System.Func<bool> done, float realSeconds)
        {
            float until = Time.realtimeSinceStartup + realSeconds;
            while (!done() && Time.realtimeSinceStartup < until) yield return null;
        }

        static string Dist(Vector2? p, Vector2 to) => p.HasValue ? (p.Value - to).magnitude.ToString("0.0") : "none";

        static string Last(string dispatches)
        {
            int i = dispatches.LastIndexOf(" // ");
            return i < 0 ? dispatches : dispatches.Substring(i + 4);
        }
    }
}
