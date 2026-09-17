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
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
        float start;
        readonly List<float> frames = new List<float>(20000);
        int maxEntities, maxProjectiles;
        float minScale = 1f;
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
                else if (args[i] == "-sfburstzoom" && i + 1 < args.Length &&
                         float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out float bz)) burstZoom = bz;
            }
            if (!active) { enabled = false; return; }
            MatchSettings.skipMenu = true;
            MatchSettings.spectate = true;
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
            start = Time.realtimeSinceStartup;
        }

        void Update()
        {
            float elapsed = Time.realtimeSinceStartup - start;
            if (!holdCamera) TrackAction();
            if (elapsed < Warmup) return;

            frames.Add(Time.unscaledDeltaTime * 1000f);
            if (world != null)
            {
                maxEntities = Mathf.Max(maxEntities, world.units.Count);
                maxProjectiles = Mathf.Max(maxProjectiles, world.projectiles.Count);
            }
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                minScale = Mathf.Min(minScale, urp.renderScale);

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
            string text = sb.ToString();
            Debug.Log("[Benchmark]\n" + text);
            try { File.WriteAllText(outPath, text); } catch { /* the log copy is enough */ }
            Application.Quit();
        }
    }
}
