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
            if (active && System.Array.IndexOf(Environment.GetCommandLineArgs(), "-sfcap60") < 0)
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
            // Name the hitches, so a stall can be matched to what happened then.
            if (Time.unscaledDeltaTime > 0.25f)
                Debug.Log($"[Benchmark] {Time.unscaledDeltaTime * 1000f:0} ms frame at {elapsed:0.0} s (game {(world != null ? world.time : 0f):0.0} s)");
            if (world != null)
            {
                maxEntities = Mathf.Max(maxEntities, world.units.Count);
                maxProjectiles = Mathf.Max(maxProjectiles, world.projectiles.Count);
            }
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                minScale = Mathf.Min(minScale, urp.renderScale);
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
            Vector2 sum = Vector2.zero;
            int n = 0;
            foreach (var u in world.units)
                if (u != null && !u.dying && u.def.IsArmy) { sum += u.pos; n++; }
            rig.CenterOn(n > 0 ? sum / n : new Vector2(world.MapSize * 0.5f, world.MapSize * 0.5f));
        }

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
            sb.AppendLine($"resolution {Screen.width}x{Screen.height}, {frames.Count} frames over {seconds:0}s after {Warmup:0}s warm-up");
            sb.AppendLine($"avg {avg:0.00} ms ({1000f / Mathf.Max(0.01f, avg):0.0} fps) · p50 {P(0.5f):0.00} · p95 {P(0.95f):0.00} · p99 {P(0.99f):0.00} · worst {P(1f):0.00} ms");
            sb.AppendLine($"peak entities {maxEntities} · peak projectiles {maxProjectiles} · lowest render scale {minScale:0.00}");
            sb.AppendLine($"game time {(world != null ? world.time : 0f):0}s · winner {(world != null ? world.winner : -1)}");
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
