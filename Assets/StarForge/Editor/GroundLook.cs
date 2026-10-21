// GroundLook.cs — photographs and measures the ragdolls, the Mech's footfalls on each
// kind of ground and the weather, in play mode (Temp/ground_*.png, 2x):
//
//   Tools/editor.sh call StarForge.EditorTools.GroundLook.Run      (then Report ~90 s later)
//
// 1. Riflemen killed by a rifle round, a shell beside them and a heavy shell: how far
//    over each torso went, how far each hip bent (inside its joint's limits?), whether a
//    leg came apart from its hip or went into the ground, and that each came to rest.
// 2. A footfall (FXDirector.GroundStrike) on meadow, gravel, sand and rock near the base,
//    with what the ground was taken to be, and one beside a settled body (the kick).
// 3. A clear sky, a shower forced on (streaks, then the ground wet after 25 s, rings on
//    a lake), and a dust devil spun up on dry ground.
// Never restarts a running match; puts back every static it sets.
using System.Collections;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.Sim;
using StarForge.View;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class GroundLook
    {
        public static string Run()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            new GameObject("GroundLook").AddComponent<GroundLooker>();
            return "looking; call StarForge.EditorTools.GroundLook.Report in about 90 seconds";
        }

        public static string Report()
        {
            var l = Object.FindAnyObjectByType<GroundLooker>();
            return l == null ? "no look running" : l.report;
        }
    }

    public sealed class GroundLooker : MonoBehaviour
    {
        public string report = "looking";
        bool wasSpectate, wasMemory, wasFixed, restored = true;
        RTSCamera rig;
        float wasMinDistance = -1f;

        void Restore()
        {
            if (restored) return;
            restored = true;
            StarForge.AI.Commander.Suspended = false;
            StarForge.Game.MatchSettings.spectate = wasSpectate;
            StarForge.Game.MatchSettings.aiMemory = wasMemory;
            StarForge.Game.MatchSettings.fixedSeed = wasFixed;
            Time.timeScale = 1f;
            if (rig != null) rig.scripted = false;
            if (rig != null && wasMinDistance > 0f) rig.minDistance = wasMinDistance;
            bool hadForce = FXDirector.ForceRain.HasValue || FXDirector.ForceShower.HasValue;
            FXDirector.ForceRain = null;
            FXDirector.ForceShower = null;
            if (hadForce && FXDirector.Current != null) FXDirector.Current.RedrawWeather();
        }

        void OnDestroy() => Restore();

        /// <summary>Each new rifleman: torso tilt; per leg the hip's bend (hip to the
        /// leg's middle against the torso's down, from the renderer: a frozen body's
        /// colliders are off), how far the leg came from its hip, its lowest point above
        /// the ground; and whether he has come to rest.</summary>
        static void Hips(StringBuilder sb, int from, MapInfo map)
        {
            for (int i = from; i < UnitWreck.Active.Count; i++)
            {
                var w = UnitWreck.Active[i];
                if (w == null || w.type != UnitType.Trooper || w.HullTransform == null) continue;
                var h = w.HullTransform;
                float faceDown = Vector3.Dot(h.forward, Vector3.down);
                sb.Append($"\n  tilt {Vector3.Angle(h.up, Vector3.up):0}deg {(faceDown > 0.5f ? "face down" : faceDown < -0.5f ? "on his back" : "on his side")}");
                if (w.Limbs == null) { sb.Append(" NO LIMBS"); continue; }
                foreach (var leg in w.Limbs)
                {
                    var j = leg.GetComponent<CharacterJoint>();
                    var r = leg.GetComponentInChildren<Renderer>();
                    Vector3 hip = leg.transform.TransformPoint(j.anchor);
                    float apart = Vector3.Distance(hip, h.TransformPoint(j.connectedAnchor));
                    // Signed about the torso's right: positive folds the leg forward toward
                    // the chest, negative kicks it back behind the body.
                    float flex = r != null ? -Vector3.SignedAngle(-h.up, r.bounds.center - hip, h.right) : 0f;
                    float g = r != null ? map.HeightAt(new Vector2(r.bounds.center.x, r.bounds.center.z)) : 0f;
                    float low = r != null ? r.bounds.min.y - g : 0f;
                    float top = r != null ? r.bounds.max.y - g : 0f;
                    sb.Append($", leg flex {flex:+0;-0}deg apart {apart:0.00}m low {low:+0.00;-0.00} top {top:0.00}m{(float.IsNaN(leg.position.x) ? " NaN" : "")}");
                }
                sb.Append(w.Frozen ? ", frozen" : ", moving");
            }
        }

        IEnumerator Shot(string name)
        {
            ScreenCapture.CaptureScreenshot($"Temp/ground_{name}.png", name.StartsWith("ragdoll") ? 3 : 2);
            yield return null;   // taken at the end of the frame: change nothing in it
        }

        IEnumerator Start()
        {
            var boot = StarForge.Game.GameBootstrap.Instance;
            var world = GameWorld.Instance;
            if (boot == null || world == null) { report = "no world"; yield break; }
            wasSpectate = StarForge.Game.MatchSettings.spectate;
            wasMemory = StarForge.Game.MatchSettings.aiMemory;
            wasFixed = StarForge.Game.MatchSettings.fixedSeed;
            restored = false;
            if (!world.running)
            {
                StarForge.Game.MatchSettings.spectate = true;
                StarForge.Game.MatchSettings.aiMemory = false;
                StarForge.Game.MatchSettings.fixedSeed = true;
                boot.StartMatch();
                yield return new WaitForSecondsRealtime(1.5f);
            }
            StarForge.AI.Commander.Suspended = true;
            rig = Object.FindAnyObjectByType<RTSCamera>();
            var map = MapInfo.Instance;
            var fx = FXDirector.Current;
            if (rig == null || map == null || fx == null) { report = "no camera/map/fx"; Restore(); yield break; }
            rig.scripted = true;
            Time.timeScale = 1f;
            var sb = new StringBuilder();
            var basePos = map.StartPos(0);
            var toward = (map.StartPos(1) - basePos).normalized;
            var across = new Vector2(toward.y, -toward.x);

            // Flat, dry, walkable ground near the base for the bodies.
            Vector2 flat = world.NearestWalkable(basePos + toward * 22f, 10f);
            float best = float.MaxValue;
            for (int ring = 0; ring < 5; ring++)
                for (int a = 0; a < 16; a++)
                {
                    var c = basePos + new Vector2(Mathf.Cos(a * Mathf.PI / 8f), Mathf.Sin(a * Mathf.PI / 8f)) * (18f + ring * 7f);
                    if (!world.Walkable(c, 1f) || map.WaterDepth(c) > -1f) continue;
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int x = -2; x <= 2; x++)
                        for (int z = -2; z <= 2; z++)
                        {
                            var q = c + across * (x * 3f) + toward * (z * 3f);
                            float hgt = map.HeightAt(q);
                            lo = Mathf.Min(lo, hgt); hi = Mathf.Max(hi, hgt);
                            if (!world.Walkable(q, 1f)) hi += 10f;
                        }
                    if (hi - lo < best) { best = hi - lo; flat = c; }
                }

            // 1. Ragdolls: a rifle round, a shell beside him, a heavy shell under him.
            var men = new Unit[3];
            for (int n = 0; n < 3; n++)
            {
                men[n] = world.Spawn(UnitType.Trooper, 1, world.NearestWalkable(flat + across * ((n - 1) * 4f), 3f), true, Mathf.Atan2(toward.x, toward.y));
                men[n].order = Order.Hold;
            }
            // Closer than a player may zoom (26 m), to inspect the bodies; put back after.
            wasMinDistance = rig.minDistance;
            rig.minDistance = 10f;
            rig.JumpTo(flat);
            rig.ZoomTo(12f);
            yield return new WaitForSeconds(1.5f);
            int before = UnitWreck.Active.Count;
            Time.timeScale = 0.5f;
            world.Damage(men[0], 1e6f, men[0].pos + toward * 12f, null, false);
            world.Damage(men[1], 1e6f, men[1].pos + toward * 1.5f, null, true);
            world.Damage(men[2], 1e6f, men[2].pos + across * 0.6f, null, true);
            float t0 = Time.time;
            foreach (var t in new[] { 0.15f, 0.4f, 0.8f, 1.5f })
            {
                while (Time.time - t0 < t) yield return null;
                yield return Shot($"ragdoll_{t:0.00}");
            }
            Time.timeScale = 1f;
            sb.Append("ragdolls mid-fall:");
            Hips(sb, before, map);
            yield return new WaitForSeconds(6f);
            yield return Shot("ragdoll_rest");
            sb.Append("\nragdolls at rest:");
            Hips(sb, before, map);
            rig.minDistance = wasMinDistance;
            // A shell bursting among the settled bodies: they are thrown again, legs flailing.
            {
                Vector3 mid = Vector3.zero; int cnt = 0;
                for (int i = before; i < UnitWreck.Active.Count; i++)
                    if (UnitWreck.Active[i] != null && UnitWreck.Active[i].type == UnitType.Trooper) { mid += UnitWreck.Active[i].Centre; cnt++; }
                if (cnt > 0)
                {
                    mid /= cnt;
                    var burst = map.Ground(new Vector2(mid.x, mid.z));
                    var start = new System.Collections.Generic.List<Vector3>();
                    for (int i = before; i < UnitWreck.Active.Count; i++) start.Add(UnitWreck.Active[i] != null ? UnitWreck.Active[i].Centre : Vector3.zero);
                    Time.timeScale = 0.5f;
                    world.Raise(new GameEvent { kind = GameEventKind.Impact, team = 0, pos = burst + Vector3.up * 0.4f,
                                                dir = Vector3.forward, scale = 1.35f, projectileKind = 1 });
                    float tb = Time.time;
                    while (Time.time - tb < 0.5f) yield return null;
                    yield return Shot("ragdoll_shell");
                    Time.timeScale = 1f;
                    yield return new WaitForSeconds(7f);
                    sb.Append("\nshell among them:");
                    for (int i = before, k = 0; i < UnitWreck.Active.Count && k < start.Count; i++, k++)
                        if (UnitWreck.Active[i] != null && UnitWreck.Active[i].type == UnitType.Trooper)
                            sb.Append($" {Vector3.Distance(start[k], burst):0.0} m off -> moved {Vector3.Distance(start[k], UnitWreck.Active[i].Centre):0.0} m;");
                    Hips(sb, before, map);
                }
            }
            // 2. Footfalls on each kind of ground near the base.
            var kinds = new (string name, int layer, float min)[] { ("meadow", 0, 0.7f), ("gravel", 1, 0.7f), ("sand", 3, 0.6f), ("rock", 2, 0.45f) };
            sb.Append("\nfootfalls:");
            foreach (var k in kinds)
            {
                Vector2 found = Vector2.zero; bool ok = false;
                for (int ring = 0; ring < 14 && !ok; ring++)
                    for (int a = 0; a < 24 && !ok; a++)
                    {
                        var c = basePos + new Vector2(Mathf.Cos(a * Mathf.PI / 12f), Mathf.Sin(a * Mathf.PI / 12f)) * (12f + ring * 6f);
                        if (map.WaterDepth(c) > -0.8f) continue;
                        var wgt = MapGenerator.SplatAt(c.x, c.y, map.HeightAt(c), map.NormalAt(c));
                        if (wgt[k.layer] >= k.min) { found = c; ok = true; }
                    }
                if (!ok) { sb.Append($" {k.name}: none near the base;"); continue; }
                var at = map.Ground(found);
                rig.JumpTo(found);
                rig.ZoomTo(14f);
                yield return new WaitForSecondsRealtime(1.2f);
                fx.GroundStrike(at, 1f, 0f);
                yield return new WaitForSeconds(0.35f);
                yield return Shot($"step_{k.name}");
                sb.Append($"\n  {k.name}: {fx.GroundInfo(at)}");
            }
            // The kick: a settled body beside a footfall.
            var body = world.Spawn(UnitType.Trooper, 1, world.NearestWalkable(flat + toward * 6f, 3f), true, 0f);
            body.order = Order.Hold;
            rig.JumpTo(flat + toward * 6f);
            rig.ZoomTo(16f);
            yield return new WaitForSeconds(1f);
            int bi = UnitWreck.Active.Count;
            world.Damage(body, 1e6f, body.pos + toward * 10f, null, false);
            yield return new WaitForSeconds(5f);
            if (bi < UnitWreck.Active.Count && UnitWreck.Active[bi] != null && UnitWreck.Active[bi].HullTransform != null)
            {
                var w = UnitWreck.Active[bi];
                // From his centre of mass, which is what a blast's reach is measured to.
                Vector3 p0 = w.Centre;
                bool wasFrozen = w.Frozen;
                Vector3 foot = p0 + new Vector3(1.6f, 0f, 0.4f);
                foot.y = map.HeightAt(new Vector2(foot.x, foot.z));
                fx.GroundStrike(foot, 1f, 0f);
                UnitWreck.Blast(foot, 2.8f, 2.2f);   // what OnFootfall adds after GroundStrike
                DebrisBurst.Blast(foot, 2.8f, 2f);
                yield return new WaitForSeconds(0.3f);
                yield return Shot("step_kick");
                yield return new WaitForSeconds(2.5f);
                sb.Append($"\n  kick: body {Vector3.Distance(p0, foot):0.0} m from the foot, {(wasFrozen ? "settled" : "not settled")}, moved {Vector3.Distance(p0, w.Centre):0.00} m; after the wake:");
                Hips(sb, bi, map);
            }

            // 3. Weather: clear, then a shower forced on, a lake in the rain, a dust devil.
            rig.JumpTo(basePos + toward * 14f);
            rig.ZoomTo(40f);
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot("weather_clear");
            sb.Append($"\nweather: seed's rain day {fx.RainDay} (would rain {fx.ShowerShare(900f) * 100f:0}% of 15 min), wind {Wind.Speed(Time.time):0.00} (day {Wind.Weather:0.00}), dust sheets so far {fx.DustSheets}");
            FXDirector.ForceRain = true;
            FXDirector.ForceShower = 1f;
            fx.RedrawWeather();
            yield return new WaitForSecondsRealtime(3f);
            yield return Shot("weather_rain_start");
            sb.Append($"; shower {fx.Rain:0.00}, {fx.RainParticles} streaks, wet {fx.Wetness:0.00}");
            yield return new WaitForSecondsRealtime(22f);
            yield return Shot("weather_rain_wet");
            sb.Append($"; after 25 s wet {fx.Wetness:0.00}");
            // A lake in the rain, if there is one near.
            for (int ring = 0; ring < 16; ring++)
            {
                bool hit = false;
                for (int a = 0; a < 24; a++)
                {
                    var c = basePos + new Vector2(Mathf.Cos(a * Mathf.PI / 12f), Mathf.Sin(a * Mathf.PI / 12f)) * (20f + ring * 8f);
                    if (map.WaterDepth(c) > 1f) { rig.JumpTo(c); rig.ZoomTo(24f); hit = true; break; }
                }
                if (hit)
                {
                    yield return new WaitForSecondsRealtime(2f);
                    yield return Shot("weather_rain_lake");
                    break;
                }
            }
            FXDirector.ForceShower = 0f;
            yield return new WaitForSecondsRealtime(4f);
            sb.Append($"; shower stopped: {fx.Rain:0.00}, still wet {fx.Wetness:0.00}");
            FXDirector.ForceRain = null;
            FXDirector.ForceShower = null;
            fx.RedrawWeather();
            yield return null;
            // A dust devil on dry ground (sand or gravel found above, else in front of the base).
            Vector2 dry = flat + toward * 10f;
            for (int ring = 0; ring < 12; ring++)
            {
                bool hit = false;
                for (int a = 0; a < 24; a++)
                {
                    var c = basePos + new Vector2(Mathf.Cos(a * Mathf.PI / 12f), Mathf.Sin(a * Mathf.PI / 12f)) * (14f + ring * 6f);
                    if (map.WaterDepth(c) > -1f) continue;
                    var wgt = MapGenerator.SplatAt(c.x, c.y, map.HeightAt(c), map.NormalAt(c));
                    if (wgt.y + wgt.w > 0.85f) { dry = c; hit = true; break; }
                }
                if (hit) break;
            }
            rig.JumpTo(dry);
            rig.ZoomTo(26f);
            yield return new WaitForSecondsRealtime(1.5f);
            fx.SpinUpDevil(map.Ground(dry));
            yield return new WaitForSeconds(2.5f);
            yield return Shot("weather_devil_a");
            yield return new WaitForSeconds(3f);
            yield return Shot("weather_devil_b");
            sb.Append($"\n  dust devils {fx.DevilCount}, ground there: {fx.GroundInfo(map.Ground(dry))}");

            Restore();
            report = sb.ToString();
        }
    }
}
