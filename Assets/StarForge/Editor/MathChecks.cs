// MathChecks.cs — play-mode check that the cached maths gives what the full maths did:
// FogOfWarRenderer's targets against a full resample of the visibility grid,
// VegetationRenderer's kept poses against Vegetation.Pose while trees fall, burn and are
// cratered, GameWorld.NearestDropoff against a scan of every unit (a second Foundry
// built, one still building, one killed), then the map regenerated in place with fires
// burning (Vegetation.PlantsReplaced). Exceptions and errors are counted throughout.
//   Tools/editor.sh call StarForge.EditorTools.MathChecks.Run
//   Tools/editor.sh call StarForge.EditorTools.MathChecks.Report   (about a minute later)
// Starts a match if none is running, and reloads the scene at the end.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarForge.Game;
using StarForge.Sim;
using StarForge.View;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class MathChecks
    {
        public static string Run()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = UnityEngine.Object.FindAnyObjectByType<MathCheckRunner>();
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            MathCheckRunner.last = null;
            new GameObject("MathChecks").AddComponent<MathCheckRunner>();
            return "staging; call MathChecks.Report in about a minute";
        }

        public static string Report()
        {
            var r = UnityEngine.Object.FindAnyObjectByType<MathCheckRunner>();
            return r != null ? r.report : MathCheckRunner.last ?? "no check running";
        }
    }

    /// <summary>The frame-by-frame half of MathChecks. Declared here, not in a file of its
    /// own name, or Unity treats it as an editor script it will not add; with no script asset
    /// it has no execution order either, so it checks from Application.onBeforeRender, after
    /// every LateUpdate, and counts any frame where the fog renderer had not yet run.</summary>
    public sealed class MathCheckRunner : MonoBehaviour
    {
        public string report = "running";
        public static string last;

        const float FogPhaseEnd = 20f, RegenAt = 40f, End = 47f;
        const uint RegenSeed = 4104226260;

        GameWorld w;
        Vegetation veg;
        FogOfWarRenderer fog;
        VegetationRenderer vr;
        FieldInfo fSeenT, fKnownT, fVersion, fSeenPose;
        float t0;
        bool oldSpectate, oldMemory;
        readonly HashSet<int> done = new HashSet<int>();

        // Results.
        int fogFrames, fogUpdates, fogMismatch, orderLate;
        long poseChecks, poseChanging, poseMismatch, poseMismatchChanging;
        long dropChecks, dropMismatch;
        int exceptions, errors;
        readonly List<string> firstLogs = new List<string>();
        string events = "";
        int regenPlantsBefore = -1, regenPlantsAfter = -1, burningBefore = -1, burningAfter = -1;
        bool regenLiveMatched, regenGrassBumped;
        long poseChecksAfterRegen;
        float[] prevKey;
        Unit extraFoundry, buildingFoundry;

        void OnEnable() { Application.logMessageReceived += OnLog; Application.onBeforeRender += Check; }
        void OnDisable() { Application.logMessageReceived -= OnLog; Application.onBeforeRender -= Check; }

        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Exception) exceptions++;
            else if (type == LogType.Error || type == LogType.Assert) errors++;
            else return;
            if (firstLogs.Count < 6)
            {
                string first = stack != null ? stack.Split('\n')[0] : "";
                firstLogs.Add($"{type}: {Short(msg)} @ {Short(first)}");
            }
        }

        static string Short(string s) => s == null ? "" : s.Length > 140 ? s.Substring(0, 140) : s;

        void Start()
        {
            t0 = Time.unscaledTime;
            oldSpectate = MatchSettings.spectate;
            oldMemory = MatchSettings.aiMemory;
            MatchSettings.spectate = false;     // the fog runs only for a side
            MatchSettings.aiMemory = false;
            var boot = GameBootstrap.Instance;
            if (boot != null) boot.StartMatch();   // nothing if one is already running
            Time.timeScale = 2f;
            w = GameWorld.Instance;
            veg = w != null ? w.Plants : null;
            fog = FindAnyObjectByType<FogOfWarRenderer>();
            vr = FindAnyObjectByType<VegetationRenderer>();
            const BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance;
            fSeenT = typeof(FogOfWarRenderer).GetField("seenTarget", bf);
            fKnownT = typeof(FogOfWarRenderer).GetField("knownTarget", bf);
            fVersion = typeof(FogOfWarRenderer).GetField("targetsVersion", bf);
            fSeenPose = typeof(VegetationRenderer).GetField("seenPose", bf);
            if (w == null || veg == null || fog == null || vr == null || fSeenT == null || fKnownT == null || fVersion == null || fSeenPose == null)
                Finish("missing: world/vegetation/fog/renderer or a field");
        }

        float T => Time.unscaledTime - t0;

        bool Once(int id) => done.Add(id);

        void Update()
        {
            if (w == null || report != "running") return;
            try { Script(); }
            catch (Exception e) { events += $" [script threw at {T:0.0}s: {e.GetType().Name} {Short(e.Message)}]"; }
        }

        void Script()
        {
            if (T > 2f && Once(1))
            {
                // Two Diggers on a long walk, so the player's sight sweeps across the map.
                var sel = new List<Unit>();
                foreach (var u in w.units)
                    if (Unit.Live(u) && u.team == 0 && u.Type == UnitType.Worker && sel.Count < 2) sel.Add(u);
                if (sel.Count > 0) w.CmdMove(sel, new Vector2(w.MapSize * 0.5f, w.MapSize * 0.5f), false);
                events += $" walk {sel.Count};";
            }
            if (T > 4f && Once(2))
            {
                // A second Foundry, finished, and a third still building.
                Vector2 home = w.Map.StartPos(0), mid = new Vector2(w.MapSize * 0.5f, w.MapSize * 0.5f);
                Vector2 dir = (mid - home).normalized;
                extraFoundry = w.Spawn(UnitType.Foundry, 0, w.NearestWalkable(home + dir * 30f), true);
                buildingFoundry = w.Spawn(UnitType.Foundry, 0, w.NearestWalkable(home + new Vector2(-dir.y, dir.x) * 26f), false);
                events += " foundries +2;";
            }
            if (T > 8f && Once(3))
            {
                // Trees the player can see go over.
                int felled = 0;
                for (int i = 0; i < veg.plants.Length && felled < 4; i++)
                {
                    if (veg.live[i].state != PlantState.Standing || veg.KindOf(i).bush || !w.Visible(0, veg.Pos2(i))) continue;
                    veg.Fell(w, i, new Vector2(Mathf.Cos(i), Mathf.Sin(i)), 0.6f);
                    felled++;
                }
                events += $" felled {felled} in sight;";
            }
            if (T > 14f && Once(4) && Unit.Live(extraFoundry))
            {
                w.Damage(extraFoundry, 1e9f, extraFoundry.pos, null);
                events += " foundry killed;";
            }
            if (T > FogPhaseEnd && Once(5))
            {
                // Everything remembered every frame from here: the pose check covers every plant.
                MatchSettings.spectate = true;
                int felled = 0, burnt = 0, blasts = 0;
                int n = veg.plants.Length;
                for (int k = 0; k < 12; k++)
                {
                    int i = (k * 7919) % n;
                    if (veg.live[i].state != PlantState.Standing) continue;
                    if (!veg.KindOf(i).bush) { veg.Fell(w, i, new Vector2(Mathf.Cos(k), Mathf.Sin(k)), k % 2 == 0 ? 0.25f : 1.2f); felled++; }
                }
                for (int k = 0; k < 6; k++)
                {
                    int i = (k * 104729 + 13) % n;
                    var p = veg.Pos2(i) + new Vector2(1.5f, 0.5f);
                    w.Blast(new Vector3(p.x, w.GroundY(p), p.y), 4f, 2.5f, 0.6f, 0.4f);
                    blasts++;
                }
                for (int k = 0; k < 8; k++)
                {
                    int i = (k * 15485863 + 101) % n;
                    if (!veg.live[i].burning && veg.live[i].state != PlantState.Gone) { veg.Ignite(w, i, 0); burnt++; }
                }
                events += $" spectate: felled {felled}, blasts {blasts}, lit {burnt};";
            }
            if (T > RegenAt && Once(6))
            {
                // The map made again in place while plants burn, the world held still.
                Time.timeScale = 0f;
                var info = MapInfo.Instance;
                var rt = info != null ? info.GetComponent<MapRuntime>() : null;
                if (rt == null || rt.kit == null) { events += " no map runtime;"; return; }
                regenPlantsBefore = veg.plants.Length;
                burningBefore = veg.burning.Count;
                int grassBefore = veg.GrassVersion;
                MapGenerator.Generate(info, rt.kit, RegenSeed, bakeNavMesh: true, sharedMaterials: false);
                regenPlantsAfter = veg.plants.Length;
                burningAfter = veg.burning.Count;
                regenLiveMatched = veg.live.Length == veg.plants.Length;
                regenGrassBumped = veg.GrassVersion != grassBefore;
                prevKey = null;
                events += $" regenerated ({regenPlantsBefore} -> {regenPlantsAfter} plants);";
            }
            if (T > RegenAt + 1f && Once(7)) w.Islands?.Build();
            if (T > End && Once(8)) Finish(null);
        }

        void Check()
        {
            if (this == null || w == null || report != "running") return;
            try
            {
                CheckFog();
                CheckPoses();
                CheckDropoffs();
            }
            catch (Exception e) { events += $" [check threw at {T:0.0}s: {e.GetType().Name} {Short(e.Message)}]"; }
        }

        // ---- the fog: targets against the full resample the renderer used to do every frame
        void CheckFog()
        {
            if (!w.running || !fog.fogEnabled || MatchSettings.spectate) return;
            var seenT = (float[])fSeenT.GetValue(fog);
            var knownT = (float[])fKnownT.GetValue(fog);
            if (seenT == null || knownT == null) return;
            if ((int)fVersion.GetValue(fog) != w.VisVersion) { orderLate++; return; }
            fogFrames++;
            if (w.VisVersion != lastVersion) { fogUpdates++; lastVersion = w.VisVersion; }
            const int R = 128;
            int team = fog.team;
            float scale = (float)GameWorld.VIS / R;
            for (int z = 0; z < R; z++)
                for (int x = 0; x < R; x++)
                {
                    int i = z * R + x;
                    float gx = (x + 0.5f) * scale - 0.5f, gz = (z + 0.5f) * scale - 0.5f;
                    float s = LegacySample(team, 0, gx, gz), k = LegacySample(team, 1, gx, gz);
                    if (BitConverter.SingleToInt32Bits(s) != BitConverter.SingleToInt32Bits(seenT[i]) ||
                        BitConverter.SingleToInt32Bits(k) != BitConverter.SingleToInt32Bits(knownT[i])) fogMismatch++;
                }
        }
        int lastVersion = -1;

        // FogOfWarRenderer.Sample as it was before the targets were kept (b0a6396).
        float LegacySample(int team, int kind, float gx, float gz)
        {
            int n = GameWorld.VIS;
            gx = Mathf.Clamp(gx, 0f, n - 1.001f);
            gz = Mathf.Clamp(gz, 0f, n - 1.001f);
            int x0 = (int)gx, z0 = (int)gz;
            float fx = gx - x0, fz = gz - z0;
            int x1 = Mathf.Min(n - 1, x0 + 1), z1 = Mathf.Min(n - 1, z0 + 1);
            float a, b, c, d;
            if (kind == 0)
            {
                a = w.VisibleCell(team, x0, z0); b = w.VisibleCell(team, x1, z0);
                c = w.VisibleCell(team, x0, z1); d = w.VisibleCell(team, x1, z1);
            }
            else
            {
                a = w.ExploredCell(team, x0, z0); b = w.ExploredCell(team, x1, z0);
                c = w.ExploredCell(team, x0, z1); d = w.ExploredCell(team, x1, z1);
            }
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
        }

        // ---- the plants: every pose the renderer kept this frame against Vegetation.Pose
        void CheckPoses()
        {
            if (Camera.main == null || veg.live.Length != veg.plants.Length) return;
            var seen = (Matrix4x4[])fSeenPose.GetValue(vr);
            int n = veg.plants.Length;
            if (seen == null || seen.Length != n) return;
            int team = vr.player != null ? vr.player.team : 0;
            bool all = !w.running || MatchSettings.spectate;
            if (prevKey == null || prevKey.Length != n * 7) prevKey = new float[n * 7];
            for (int i = 0; i < n; i++)
            {
                ref var s = ref veg.live[i];
                int o = i * 7;
                bool changed = prevKey[o] != s.groundShift || prevKey[o + 1] != s.sink || prevKey[o + 2] != s.crush ||
                               prevKey[o + 3] != s.fallAngle || prevKey[o + 4] != s.flatten ||
                               prevKey[o + 5] != s.fallDir.x || prevKey[o + 6] != s.fallDir.y;
                prevKey[o] = s.groundShift; prevKey[o + 1] = s.sink; prevKey[o + 2] = s.crush;
                prevKey[o + 3] = s.fallAngle; prevKey[o + 4] = s.flatten; prevKey[o + 5] = s.fallDir.x; prevKey[o + 6] = s.fallDir.y;
                if (!all && !w.Visible(team, veg.Pos2(i))) continue;
                poseChecks++;
                if (T > RegenAt) poseChecksAfterRegen++;
                if (changed) poseChanging++;
                var want = veg.Pose(i);
                for (int e = 0; e < 16; e++)
                    if (seen[i][e] != want[e]) { poseMismatch++; if (changed) poseMismatchChanging++; break; }
            }
        }

        // ---- the Diggers' way home: the Foundry list against a scan of every unit
        void CheckDropoffs()
        {
            foreach (var u in w.units)
            {
                if (!Unit.Live(u) || u.Type != UnitType.Worker) continue;
                dropChecks++;
                if (!ReferenceEquals(w.NearestDropoff(u), LegacyDropoff(u))) dropMismatch++;
            }
        }

        Unit LegacyDropoff(Unit u)
        {
            Unit best = null;
            float bestD = 1e30f;
            foreach (var o in w.units)
            {
                if (o == null || o.dying || o.team != u.team || o.Type != UnitType.Foundry || !o.Complete) continue;
                float d = (o.pos - u.pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = o; }
            }
            return best;
        }

        void Finish(string problem)
        {
            var sb = new StringBuilder();
            if (problem != null) sb.Append("NOT RUN: ").Append(problem).Append('\n');
            sb.Append($"fog: {fogFrames} frames checked ({fogUpdates} grid updates), {fogMismatch} texel mismatches, {orderLate} frames checked before the renderer (skipped)\n");
            sb.Append($"poses: {poseChecks} plant-frames checked, {poseChanging} with a keyed input changed since the last frame, {poseMismatch} mismatches ({poseMismatchChanging} of them changing); {poseChecksAfterRegen} after the regeneration\n");
            sb.Append($"dropoff: {dropChecks} Digger-frames checked, {dropMismatch} mismatches\n");
            sb.Append($"regeneration: plants {regenPlantsBefore} -> {regenPlantsAfter}, live matched {regenLiveMatched}, burning {burningBefore} -> {burningAfter}, grass version bumped {regenGrassBumped}\n");
            sb.Append($"log: {exceptions} exceptions, {errors} errors").Append(firstLogs.Count > 0 ? ": " + string.Join(" | ", firstLogs) : "").Append('\n');
            sb.Append("events:").Append(events);
            report = sb.ToString();
            last = report;
            Debug.Log("[MathChecks]\n" + report);
            MatchSettings.spectate = oldSpectate;
            MatchSettings.aiMemory = oldMemory;
            Time.timeScale = 1f;
            if (problem == null)
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }
    }
}
