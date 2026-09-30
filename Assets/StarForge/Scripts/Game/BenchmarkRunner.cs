// BenchmarkRunner.cs — command-line benchmark for the built player, the
// counterpart of the original's `--bench`:
//
//     StarForge.app/Contents/MacOS/StarForge -sfbench 120 [-sfbenchout /tmp/bench.txt] [-sfshot /tmp/shot.png] [-sfshotat 90]
//         [-sfquality high|balanced|battery] [-sfburst /tmp/burst.raw] [-sfburstat 20] [-sfburstframes 60] [-sfburstzoom 150]
//
// Plays an AI-vs-AI match with vsync off, the camera tracking the fighting,
// and after a warm-up records frame times, then writes average / percentile
// frame times, entity counts and the adaptive render scale, and quits.
//
// -sfburst holds the camera still and records consecutive final frames (the
// centre 1408x880 pixels, after upscaling) for shimmer analysis: a 12-byte
// header of width, height and frame count (int32), then RGB rows bottom-up per
// frame. The first full frame is saved alongside as a PNG.
//
// -sfgallery <dir> takes the same set of screenshots every run, for comparing
// art changes: the player's base close and mid, an ore field, a shore with
// plants, a grove before, during and after it is set alight, and the whole map
// early on (on the fixed seed), then the first big fire-fight close and mid.
// -sfgalleryquick stops after the early set.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using StarForge.Sim;
using StarForge.View;
using StarForge.World;

namespace StarForge.Game
{
    [DefaultExecutionOrder(-70)]
    public sealed class BenchmarkRunner : MonoBehaviour
    {
        const float Warmup = 8f;

        bool active;
        float seconds = 120f;
        string outPath, shotPath;
        float shotAt = -1f;   // seconds after warm-up; default is the middle of the run
        bool shotTaken;
        string burstPath;
        float burstAt = -1f;
        int burstFrames = 60;
        float burstZoom = -1f;
        bool burstStarted, holdCamera;
        string galleryDir;
        bool galleryQuick;
        float start;
        readonly List<float> frames = new List<float>(20000);
        int maxEntities, maxProjectiles;
        float minScale = 1f;
        Vector3 musicSum;
        int musicFrames;
        float combatMusicTime;
        AudioDirector audioDirector;
        GameWorld world;
        RTSCamera rig;

        void Awake()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-sfbench")
                {
                    active = true;
                    if (i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float s)) seconds = s;
                }
                else if (args[i] == "-sfbenchout" && i + 1 < args.Length) outPath = args[i + 1];
                else if (args[i] == "-sfshot" && i + 1 < args.Length) shotPath = args[i + 1];
                else if (args[i] == "-sfshotat" && i + 1 < args.Length &&
                         float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out float at)) shotAt = at;
                else if (args[i] == "-sfshadows" && i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sd)) QualityController.ShadowOverride = sd;
                else if (args[i] == "-sfminscale" && i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ms)) QualityController.MinScaleOverride = ms;
                else if (args[i] == "-sfdensity" && i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dn)) QualityController.DensityOverride = dn;
                else if (args[i] == "-sfquality" && i + 1 < args.Length &&
                         Enum.TryParse(args[i + 1], true, out SFQuality q)) QualityController.Override = q;
                else if (args[i] == "-sfburst" && i + 1 < args.Length) burstPath = args[i + 1];
                else if (args[i] == "-sfburstat" && i + 1 < args.Length &&
                         float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out float bat)) burstAt = bat;
                else if (args[i] == "-sfburstframes" && i + 1 < args.Length &&
                         int.TryParse(args[i + 1], out int bf)) burstFrames = Mathf.Max(3, bf);
                else if (args[i] == "-sfgallery" && i + 1 < args.Length) { galleryDir = args[i + 1]; active = true; }
                else if (args[i] == "-sfgalleryquick") galleryQuick = true;
                else if (args[i] == "-sfburstzoom" && i + 1 < args.Length &&
                         float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out float bz)) burstZoom = bz;
            }
            if (!active) { enabled = false; return; }
            AI.Commander.Suspended = false;   // never a trial's leftover
            AI.Commander.IgnoreSites = false;
            MatchSettings.skipMenu = true;
            MatchSettings.spectate = true;
            MatchSettings.fixedSeed = true;
            MatchSettings.aiMemory = false;
            MatchSettings.difficulty = AI.AIDifficulty.Commander;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            outPath ??= Path.Combine(Application.persistentDataPath, "starforge_bench.txt");
        }

        void Start()
        {
            world = FindAnyObjectByType<GameWorld>();
            rig = FindAnyObjectByType<RTSCamera>();
            audioDirector = FindAnyObjectByType<AudioDirector>();
            // Again here: GameBootstrap.Awake, which runs after this one's, caps the
            // frame rate at 60 for play, and on Unity 6000.6.1 the capped frames come
            // out quantised to whole display refreshes (16.7 / 33.3 ms).
            // -sfcap60 keeps the play cap instead, to compare with builds from before
            // this was done (they ran capped without meaning to).
            // -sfplay runs exactly as a match does -- vsync on and the 60 cap -- which is the
            // frame rate a player sees.
            var args = Environment.GetCommandLineArgs();
            if (active && System.Array.IndexOf(args, "-sfplay") >= 0)
            {
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = 60;
            }
            else if (active && System.Array.IndexOf(args, "-sfcap60") < 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
            }
            // A pointer resting at a screen edge would scroll the view away.
            if (active && rig != null) rig.scripted = true;
            start = Time.realtimeSinceStartup;
            if (active && galleryDir != null)
            {
                Directory.CreateDirectory(galleryDir);
                seconds = 1e6f;   // the gallery ends the run
                StartCoroutine(Gallery());
            }
        }

        void Update()
        {
            float elapsed = Time.realtimeSinceStartup - start;
            if (!holdCamera) TrackAction();
            if (elapsed < Warmup) return;

            frames.Add(Time.unscaledDeltaTime * 1000f);
            mechInView = false;
            // What the frame that just ended spent where (sampled in its LateUpdate), and
            // whether a long one had a slow simulation step, a GC or a NavMesh island build.
            simFrames.Add(lastSim);
            // What the frame cost the CPU and the GPU, and how long it waited to be shown. The
            // timings come back a few frames late, so the split of the long frames is approximate;
            // the averages are not.
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {
                var t = timing[0];
                cpuMain.Add((float)t.cpuMainThreadFrameTime);
                cpuRender.Add((float)t.cpuRenderThreadFrameTime);
                // Metal leaves the GPU time at zero on frames it could not time: count
                // only the ones it did, or the average means nothing.
                if (t.gpuFrameTime > 0.01) gpu.Add((float)t.gpuFrameTime);
                presentWait.Add((float)t.cpuMainThreadPresentWaitTime);
                timed++;
                if (Time.unscaledDeltaTime > 0.040f) { longWait += t.cpuMainThreadPresentWaitTime; longGpu += t.gpuFrameTime; longTimed++; }
            }
            tickFrames.Add(lastTicked);
            brainFrames.Add(lastBrain);
            if (Time.unscaledDeltaTime > 0.040f)
            {
                longFrames++;
                if (lastSim > 12f) longSim++;
                if (lastTicked > 12f) longTicked++;
                if (lastGc) longGc++;
                if (lastIsland) longIsland++;
            }
            // Frames with a Mech in the view, measured apart: they are the heaviest.
            if (world != null && rig != null)
                foreach (var v in StarForge.View.MechView.Active)
                    if (v != null && v.Unit != null && !v.Unit.dying && v.Core != null && v.Core.Landed &&
                        (v.Unit.pos - rig.Focus).sqrMagnitude < 60f * 60f) { mechFrames.Add(Time.unscaledDeltaTime * 1000f); mechInView = true; break; }
            // Name the hitches, so a stall can be matched to what happened then.
            if (Time.unscaledDeltaTime > 0.25f)
                Debug.Log($"[Benchmark] {Time.unscaledDeltaTime * 1000f:0} ms frame at {elapsed:0.0} s (game {(world != null ? world.time : 0f):0.0} s)");
            if (world != null)
            {
                maxEntities = Mathf.Max(maxEntities, world.units.Count);
                maxProjectiles = Mathf.Max(maxProjectiles, world.projectiles.Count);
            }
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                minScale = Mathf.Min(minScale, urp.renderScale);
                scaleSum += urp.renderScale;
                scaleFrames++;
                if (mechInView) mechScaleSum += urp.renderScale;
            }
            if (audioDirector != null)
            {
                var m = audioDirector.MusicLevels;
                musicSum += m;
                musicFrames++;
                if (m.z > 0.5f) combatMusicTime += Time.unscaledDeltaTime;
            }

            if (!shotTaken && shotPath != null && elapsed > Warmup + (shotAt >= 0f ? shotAt : seconds * 0.5f))
            {
                ScreenCapture.CaptureScreenshot(shotPath);
                shotTaken = true;
            }
            if (!burstStarted && burstPath != null && elapsed > Warmup + (burstAt >= 0f ? burstAt : seconds * 0.5f))
            {
                burstStarted = true;
                StartCoroutine(Burst());
            }
            if (elapsed >= Warmup + seconds) Finish();
        }

        readonly List<float> simFrames = new List<float>(20000), tickFrames = new List<float>(20000), brainFrames = new List<float>(20000);
        float lastSim, lastTicked, lastBrain;
        bool mechInView;
        double scaleSum, mechScaleSum;
        int scaleFrames;
        readonly FrameTiming[] timing = new FrameTiming[1];
        readonly List<float> cpuMain = new List<float>(20000), cpuRender = new List<float>(20000), gpu = new List<float>(20000), presentWait = new List<float>(20000);
        double longWait, longGpu;
        int longTimed, timed;
        bool lastGc, lastIsland;
        int gcCount, islandBuilds, longFrames, longSim, longTicked, longGc, longIsland, gcTotal, islandTotal;

        void LateUpdate()
        {
            lastSim = GameWorld.SimMs;
            lastTicked = GameWorld.TickedMs;
            lastBrain = GameBootstrap.MechBrainMs;
            GameWorld.SimMs = GameWorld.TickedMs = GameBootstrap.MechBrainMs = 0f;
            int gc = System.GC.CollectionCount(0);
            lastGc = gc != gcCount;
            if (lastGc && Time.realtimeSinceStartup - start > Warmup) gcTotal++;
            gcCount = gc;
            int ib = world != null && world.Islands != null ? world.Islands.Builds : 0;
            lastIsland = ib != islandBuilds;
            if (lastIsland && Time.realtimeSinceStartup - start > Warmup) islandTotal++;
            islandBuilds = ib;
        }

        static float Pct(List<float> v, float q)
        {
            if (v.Count == 0) return 0f;
            var c = new List<float>(v);
            c.Sort();
            return c[Mathf.Clamp(Mathf.RoundToInt(q * (c.Count - 1)), 0, c.Count - 1)];
        }

        IEnumerator Burst()
        {
            holdCamera = true;
            if (burstZoom > 0f && rig != null) rig.ZoomTo(burstZoom);
            yield return new WaitForSecondsRealtime(1.5f);   // let the camera settle
            const int CropW = 1408, CropH = 880;
            FileStream file = null;
            try
            {
                for (int f = 0; f < burstFrames; f++)
                {
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    int w = Mathf.Min(CropW, tex.width), h = Mathf.Min(CropH, tex.height);
                    if (file == null)
                    {
                        File.WriteAllBytes(Path.ChangeExtension(burstPath, ".png"), tex.EncodeToPNG());
                        file = new FileStream(burstPath, FileMode.Create, FileAccess.Write);
                        file.Write(BitConverter.GetBytes(w), 0, 4);
                        file.Write(BitConverter.GetBytes(h), 0, 4);
                        file.Write(BitConverter.GetBytes(burstFrames), 0, 4);
                    }
                    var px = tex.GetPixels32();
                    int x0 = (tex.width - w) / 2, y0 = (tex.height - h) / 2;
                    var rgb = new byte[w * h * 3];
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            var c = px[(y0 + y) * tex.width + x0 + x];
                            int o = (y * w + x) * 3;
                            rgb[o] = c.r; rgb[o + 1] = c.g; rgb[o + 2] = c.b;
                        }
                    file.Write(rgb, 0, rgb.Length);
                    Destroy(tex);
                }
            }
            finally
            {
                file?.Dispose();
                holdCamera = false;
            }
        }

        IEnumerator Gallery()
        {
            yield return new WaitForSecondsRealtime(Warmup + 22f);
            holdCamera = true;
            var map = MapInfo.Instance;
            Vector2 centre = new Vector2(world.MapSize * 0.5f, world.MapSize * 0.5f);
            Vector2 home = map.StartPos(0);
            foreach (var u in world.units)
                if (u != null && u.team == 0 && u.Type == UnitType.Foundry) { home = u.pos; break; }
            Vector2 ore = home;
            float best = float.MaxValue;
            foreach (var u in world.units)
                if (u != null && u.Type == UnitType.Ore && (u.pos - home).sqrMagnitude < best)
                { best = (u.pos - home).sqrMagnitude; ore = u.pos; }

            yield return Shot("1_base_close", home + new Vector2(4f, -4f), 30f);
            yield return Shot("2_base_mid", home, 62f);
            yield return Shot("3_ore", ore, 30f);
            yield return Shot("4_shore", Shore(map, centre), 42f);
            var grove = Grove(map);
            yield return Shot("5_trees", grove, 36f);
            // Set the grove alight and look again once it has caught.
            if (map.vegetation != null)
            {
                var veg = map.vegetation;
                for (int i = 0; i < veg.plants.Length; i++)
                    if ((new Vector2(veg.plants[i].pos.x, veg.plants[i].pos.z) - grove).sqrMagnitude < 5f * 5f)
                        veg.Ignite(world, i, 0);
                yield return new WaitForSecondsRealtime(7f);
                yield return Shot("5b_fire", grove, 30f, 0.8f);
                yield return new WaitForSecondsRealtime(25f);
                yield return Shot("5c_burnt", grove, 30f, 0.8f);
            }
            var fauna = FindAnyObjectByType<Fauna>();
            if (fauna != null && fauna.TryGetAnimal(false, out var pack)) yield return Shot("5d_animals", new Vector2(pack.x, pack.z), 22f, 1.2f);
            if (fauna != null && fauna.TryGetSongbirds(out var flock, out _))
            {
                // A flock feeding, then put up and photographed on the wing.
                yield return Shot("5e_birds", new Vector2(flock.x, flock.z), 26f, 1.2f);
                fauna.FlushSongbirds();
                yield return new WaitForSecondsRealtime(1.4f);
                if (fauna.TryGetSongbirds(out flock, out _, true))
                {
                    // Aim at the ground behind the bird along the view, so it sits mid-frame.
                    float h = flock.y - map.HeightAt(new Vector2(flock.x, flock.z));
                    float pitch = RTSCamera.PitchFor(30f) * Mathf.Deg2Rad, yaw = rig.yaw * Mathf.Deg2Rad;
                    var behind = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)) * (h / Mathf.Tan(pitch));
                    yield return Shot("5f_flight", new Vector2(flock.x, flock.z) + behind, 30f, 0.3f);
                }
                Debug.Log("[Gallery] " + fauna.FlightReport());
            }
            yield return Shot("6_overview", centre, rig.maxDistance);
            holdCamera = false;
            if (galleryQuick) { Finish(); yield break; }

            // The first fire-fight: wait until enough rounds are in the air.
            float deadline = Time.realtimeSinceStartup + 360f;
            Vector2 fight = centre;
            while (Time.realtimeSinceStartup < deadline)
            {
                int n = 0;
                Vector2 sum = Vector2.zero;
                foreach (var p in world.projectiles)
                    if (p.alive) { sum += new Vector2(p.pos.x, p.pos.z); n++; }
                if (n >= 6) { fight = sum / n; break; }
                yield return null;
            }
            holdCamera = true;
            yield return Shot("7_battle_close", fight, 32f, 0.6f);
            yield return Shot("8_battle_mid", fight, 72f, 0.6f);
            // A Mech, once one is down.
            deadline = Time.realtimeSinceStartup + 300f;
            while (Time.realtimeSinceStartup < deadline)
            {
                StarForge.View.MechView mv = null;
                foreach (var v in StarForge.View.MechView.Active)
                    if (v != null && v.Unit != null && !v.Unit.dying && v.Core != null && v.Core.Landed) { mv = v; break; }
                if (mv != null)
                {
                    yield return Shot("9_mech_close", mv.Unit.pos, 34f, 1.2f);
                    yield return Shot("9b_mech_mid", mv.Unit.pos, 60f, 0.8f);
                    break;
                }
                yield return null;
            }
            // The other side's, once it is down too.
            deadline = Time.realtimeSinceStartup + 200f;
            while (Time.realtimeSinceStartup < deadline)
            {
                StarForge.View.MechView other = null;
                int firstTeam = -1;
                foreach (var v in StarForge.View.MechView.Active)
                    if (v != null && v.Unit != null && !v.Unit.dying && v.Core != null && v.Core.Landed)
                    {
                        if (firstTeam < 0) firstTeam = v.Unit.team;
                        else if (v.Unit.team != firstTeam) { other = v; break; }
                    }
                if (other != null) { yield return Shot("9c_mech_other", other.Unit.pos, 30f, 1.2f); break; }
                yield return null;
            }
            // A Mech in a fight: framed between it and what it is shooting, three frames a
            // quarter of a second apart to catch the beams, the muzzle blasts and the hits.
            deadline = Time.realtimeSinceStartup + 240f;
            while (Time.realtimeSinceStartup < deadline)
            {
                StarForge.View.MechView shooter = null;
                Vector2 aim = default;
                foreach (var v in StarForge.View.MechView.Active)
                {
                    if (v == null || v.Unit == null || v.Unit.dying || v.Core == null || !v.Core.Landed) continue;
                    foreach (var g in v.Core.guns)
                        if (g.target != null && world.time - g.lastShot < 0.3f && (g.target.pos - v.Unit.pos).magnitude < 40f)
                        { shooter = v; aim = g.target.pos; break; }
                    if (shooter != null) break;
                }
                if (shooter != null)
                {
                    rig.JumpTo(Vector2.Lerp(shooter.Unit.pos, aim, 0.35f));
                    rig.ZoomTo(40f);
                    yield return new WaitForSecondsRealtime(0.7f);
                    for (int k = 0; k < 3; k++)
                    {
                        ScreenCapture.CaptureScreenshot(Path.Combine(galleryDir, $"9d_mech_fight_{k}.png"));
                        yield return new WaitForSecondsRealtime(0.25f);
                    }
                    break;
                }
                yield return null;
            }
            // Its bay.
            var shownBay = world.BayOf(0) ?? world.BayOf(1);
            if (shownBay != null) yield return Shot("9e_mech_bay", shownBay.pos, 30f, 1.2f);
            Finish();
        }

        IEnumerator Shot(string name, Vector2 at, float dist, float settle = 2.2f)
        {
            rig.JumpTo(at);
            rig.ZoomTo(dist);
            yield return new WaitForSecondsRealtime(settle);
            Debug.Log($"[Gallery] {name}: asked {at}, focus {rig.Focus}, camera {rig.cam.transform.position}, rigs {FindObjectsByType<RTSCamera>(FindObjectsSortMode.None).Length}");
            ScreenCapture.CaptureScreenshot(Path.Combine(galleryDir, name + ".png"));
            yield return new WaitForSecondsRealtime(0.4f);
        }

        /// <summary>Dry ground at the water's edge nearest the middle of the map.</summary>
        static Vector2 Shore(MapInfo map, Vector2 centre)
        {
            Vector2 best = centre;
            float bestD = float.MaxValue;
            for (float x = 20f; x < map.mapSize - 20f; x += 3f)
                for (float y = 20f; y < map.mapSize - 20f; y += 3f)
                {
                    var p = new Vector2(x, y);
                    float d = map.WaterDepth(p);
                    if (d > -0.1f || d < -0.8f) continue;
                    if (map.WaterDepth(p + new Vector2(8f, 0f)) < 0.4f && map.WaterDepth(p - new Vector2(8f, 0f)) < 0.4f &&
                        map.WaterDepth(p + new Vector2(0f, 8f)) < 0.4f && map.WaterDepth(p - new Vector2(0f, 8f)) < 0.4f) continue;
                    float dc = (p - centre).sqrMagnitude;
                    if (dc < bestD) { bestD = dc; best = p; }
                }
            return best;
        }

        /// <summary>The standing tree with the most trees round it.</summary>
        static Vector2 Grove(MapInfo map)
        {
            var plants = map.vegetation != null ? map.vegetation.plants : System.Array.Empty<Plant>();
            var kinds = map.vegetation != null ? map.vegetation.kinds : System.Array.Empty<PlantKind>();
            Vector2 best = new Vector2(map.mapSize * 0.5f, map.mapSize * 0.5f);
            int bestN = -1;
            for (int i = 0; i < plants.Length; i += 3)
            {
                if (kinds[plants[i].kind].bush) continue;
                int n = 0;
                for (int j = 0; j < plants.Length; j++)
                    if ((plants[j].pos - plants[i].pos).sqrMagnitude < 14f * 14f) n++;
                if (n > bestN) { bestN = n; best = new Vector2(plants[i].pos.x, plants[i].pos.z); }
            }
            return best;
        }

        void TrackAction()
        {
            if (world == null || rig == null) return;
            // Frame whichever army is largest, so the benchmark measures fights
            // rather than an empty corner of the map.
            // A Mech counts as a dozen units: where one walks, the fighting is.
            Vector2 sum = Vector2.zero;
            float n = 0f;
            foreach (var u in world.units)
                if (u != null && !u.dying && (u.def.IsArmy || (u.mech != null && u.mech.Landed)))
                {
                    float k = u.mech != null ? 12f : 1f;
                    sum += u.pos * k;
                    n += k;
                }
            rig.CenterOn(n > 0f ? sum / n : new Vector2(world.MapSize * 0.5f, world.MapSize * 0.5f));
        }

        readonly System.Collections.Generic.List<float> mechFrames = new System.Collections.Generic.List<float>(4096);

        void Finish()
        {
            enabled = false;
            frames.Sort();
            float avg = 0f;
            foreach (var f in frames) avg += f;
            avg /= Mathf.Max(1, frames.Count);
            float P(float q) => frames.Count == 0 ? 0f : frames[Mathf.Clamp((int)(q * (frames.Count - 1)), 0, frames.Count - 1)];

            var sb = new StringBuilder();
            sb.AppendLine($"StarForge benchmark — {SystemInfo.deviceModel}, {SystemInfo.processorType}, {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})");
            sb.AppendLine($"resolution {Screen.width}x{Screen.height}, {frames.Count} frames over {seconds:0}s after {Warmup:0}s warm-up; " +
                          $"vsync {QualitySettings.vSyncCount}, cap {(Application.targetFrameRate > 0 ? Application.targetFrameRate.ToString() : "none")}");
            sb.AppendLine($"avg {avg:0.00} ms ({1000f / Mathf.Max(0.01f, avg):0.0} fps) · p50 {P(0.5f):0.00} · p95 {P(0.95f):0.00} · p99 {P(0.99f):0.00} · worst {P(1f):0.00} ms");
            sb.AppendLine($"peak entities {maxEntities} · peak projectiles {maxProjectiles} · lowest render scale {minScale:0.00}, average {(scaleFrames > 0 ? scaleSum / scaleFrames : 1.0):0.00}" +
                          (mechFrames.Count > 0 ? $" ({mechScaleSum / mechFrames.Count:0.00} with a Mech in view)" : "") +
                          $" · shadows {(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset ua ? ua.shadowDistance : 0f):0} m");
            sb.AppendLine($"game time {(world != null ? world.time : 0f):0}s · winner {(world != null ? world.winner : -1)}");
            sb.AppendLine($"simulation p50 {Pct(simFrames, 0.5f):0.0} · p95 {Pct(simFrames, 0.95f):0.0} · p99 {Pct(simFrames, 0.99f):0.0} · max {Pct(simFrames, 1f):0.0} ms " +
                          $"(AIs p99 {Pct(tickFrames, 0.99f):0.0}, Mech brains p99 {Pct(brainFrames, 0.99f):0.0}, max {Pct(brainFrames, 1f):0.0} ms); " +
                          $"{gcTotal} GCs, {islandTotal} NavMesh island builds" +
                          (world != null && world.Islands != null ? $" (last {world.Islands.TriangulateMs + world.Islands.WalkMs + world.Islands.LabelMs:0} ms)" : ""));
            sb.AppendLine($"frames over 40 ms: {longFrames}; with the simulation over 12 ms {longSim} (AIs {longTicked}), with a GC {longGc}, with an island build {longIsland}");
            if (gpu.Count > 0)
            {
                // The game's own cost, apart from waiting on the display: the frame rate it could
                // hold is set by the slower of the main thread (less its wait), the render thread
                // and the GPU.
                float Avg(List<float> v) { double a = 0; foreach (var x in v) a += x; return (float)(a / v.Count); }
                float mainWork = Avg(cpuMain) - Avg(presentWait);
                float bound = Mathf.Max(mainWork, Mathf.Max(Avg(cpuRender), Avg(gpu)));
                sb.AppendLine($"frame timing ({timed} frames, GPU timed on {gpu.Count}): main thread {Avg(cpuMain):0.0} ms (p50 {Pct(cpuMain, 0.5f):0.0}) of which waiting to present {Avg(presentWait):0.0} (p95 {Pct(presentWait, 0.95f):0.0}), " +
                              $"render thread {Avg(cpuRender):0.0} (p95 {Pct(cpuRender, 0.95f):0.0}), GPU {Avg(gpu):0.0} (p50 {Pct(gpu, 0.5f):0.0}, p95 {Pct(gpu, 0.95f):0.0}) ms -> the game's own cost allows ~{1000f / Mathf.Max(0.1f, bound):0} fps");
                if (longTimed > 0)
                    sb.AppendLine($"frames over 40 ms averaged {longWait / longTimed:0.0} ms waiting to present and {longGpu / longTimed:0.0} ms of GPU");
            }
            else sb.AppendLine("frame timing: not available (build without enableFrameTimingStats)");
            if (mechFrames.Count > 0)
            {
                mechFrames.Sort();
                float mavg = 0f;
                foreach (var f in mechFrames) mavg += f;
                mavg /= mechFrames.Count;
                sb.AppendLine($"with a Mech in view: {mechFrames.Count} frames, avg {mavg:0.00} ms ({1000f / Mathf.Max(0.01f, mavg):0.0} fps) · p95 {mechFrames[(int)(0.95f * (mechFrames.Count - 1))]:0.00} ms");
            }
            if (world != null)
                for (int t = 0; t < 2; t++)
                {
                    var F = world.factions[t];
                    var m = world.MechOf(t);
                    sb.AppendLine($"team {t} Mech: {(F.design != null ? F.design.ToString() : "-")}; bay {(F.bay != null ? "standing" : F.bayLost ? "lost" : F.bayPlaced ? "?" : "never raised")}, " +
                                  $"mech {(m != null ? $"alive {m.hp:0}/{m.MaxHp:0}, {m.kills} kills" : F.mechLost ? "lost" : F.mechDropped ? "?" : "not dropped")}, upgrades {string.Join("", F.upgrades)}");
                }
            if (musicFrames > 0)
            {
                var avgMusic = musicSum / musicFrames;
                sb.AppendLine($"music layers avg calm {avgMusic.x:0.00} · tension {avgMusic.y:0.00} · combat {avgMusic.z:0.00} · combat layer up {combatMusicTime:0}s");
            }
            string text = sb.ToString();
            Debug.Log("[Benchmark]\n" + text);
            try { File.WriteAllText(outPath, text); } catch { /* the log copy is enough */ }
            Application.Quit();
        }
    }
}
