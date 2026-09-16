// BenchmarkRunner.cs — command-line benchmark for the built player, the
// counterpart of the original's `--bench`:
//
//     StarForge.app/Contents/MacOS/StarForge -sfbench 120 [-sfbenchout /tmp/bench.txt] [-sfshot /tmp/shot.png] [-sfshotat 90]
//
// Plays an AI-vs-AI match with vsync off, the camera tracking the fighting,
// and after a warm-up records frame times, then writes average / percentile
// frame times, entity counts and the adaptive render scale, and quits.
using System;
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
            TrackAction();
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
            if (elapsed >= Warmup + seconds) Finish();
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
