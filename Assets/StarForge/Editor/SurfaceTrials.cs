// SurfaceTrials.cs — play-mode check of how units' surface state reaches the shader
// (World/UnitView.ApplySurface): on materials of the unit's own, against the old property
// blocks (UnitView.LegacyBlocks), on one frozen moment of an AI-vs-AI match.
//
//   Tools/editor.sh play
//   Tools/editor.sh call StarForge.EditorTools.SurfaceTrials.Batch
//   Tools/editor.sh wait StarForge.EditorTools.SurfaceTrials.BatchReport "" 240
//
// Plays to 240 s at 8x, stops the clock with the camera on team 0's base (Diggers at work,
// sites going up, whatever is hurt), then twice each way: renderers of living units carrying
// a property block, renderers wearing own materials, set-pass calls, draw calls and batches
// a frame over 60 frames. The first pair's pictures (Temp/surface_blocks.png,
// Temp/surface_own.png) are compared pixel by pixel: the look must not change.
using System.Collections;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using StarForge.Game;
using StarForge.Sim;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class SurfaceTrials
    {
        /// <summary>The last run's report (kept here: the watcher removes itself when done).</summary>
        public static string last = "not run";

        public static string Batch()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<SurfaceBatchWatcher>();
            if (old != null) Object.Destroy(old.gameObject);
            last = "running";
            new GameObject("SurfaceBatch").AddComponent<SurfaceBatchWatcher>();
            return "running; call BatchReport in about a minute";
        }

        public static string BatchReport() => last;

        /// <summary>The same change over a running match: AI vs AI at 8x for 300 s of play, asked at
        /// the end of every frame: each unit's renderers wear what they should and its own materials
        /// hold what ApplySurface would write (UnitView.SurfaceFault), the count of renderers wearing
        /// own materials agrees with what the views say, and no copy outlives its unit.</summary>
        public static string Live()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<SurfaceLiveWatcher>();
            if (old != null) Object.Destroy(old.gameObject);
            last = "running";
            new GameObject("SurfaceLive").AddComponent<SurfaceLiveWatcher>();
            return "running; call LiveReport in about a minute";
        }

        public static string LiveReport() => last;
    }

    public sealed class SurfaceLiveWatcher : MonoBehaviour
    {
        bool savedSpectate, savedMemory, saved;

        void OnDestroy()
        {
            Time.timeScale = 1f;
            if (saved) { MatchSettings.spectate = savedSpectate; MatchSettings.aiMemory = savedMemory; }
            if (SurfaceTrials.last == "running") SurfaceTrials.last = "stopped part-way";
        }

        static int CopiesAlive()
        {
            int n = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && m.name.EndsWith(UnitView.OwnSuffix)) n++;
            return n;
        }

        IEnumerator Start()
        {
            var boot = GameBootstrap.Instance;
            var world = GameWorld.Instance;
            if (boot == null || world == null) { SurfaceTrials.last = "no world"; yield break; }
            if (world.running) { SurfaceTrials.last = "a match is running: run it from the menu"; yield break; }
            savedSpectate = MatchSettings.spectate;
            savedMemory = MatchSettings.aiMemory;
            saved = true;
            MatchSettings.spectate = true;
            MatchSettings.aiMemory = false;
            boot.StartMatch();
            yield return new WaitForSecondsRealtime(1f);
            Time.timeScale = 8f;
            int frames = 0, viewFrames = 0, faults = 0, drift = 0, peakWorn = 0, wornFrames = 0, leakChecks = 0, leaks = 0, peakCopies = 0;
            var firstFaults = new StringBuilder();
            float giveUp = Time.realtimeSinceStartup + 240f, nextLeak = 0f;
            var wait = new WaitForEndOfFrame();
            while (world.time < 300f && world.winner < 0 && Time.realtimeSinceStartup < giveUp)
            {
                yield return wait;
                frames++;
                int worn = 0, owned = 0;
                var views = Object.FindObjectsByType<UnitView>(FindObjectsSortMode.None);
                foreach (var v in views)
                {
                    viewFrames++;
                    worn += v.OwnWorn;
                    owned += v.OwnCopies;
                    if (v.OwnWorn > 0) wornFrames++;
                    string fault = v.SurfaceFault();
                    if (fault == null) continue;
                    faults++;
                    if (faults <= 4) firstFaults.Append($" [{world.time:0.0} s: {fault}]");
                }
                if (worn != UnitView.OwnSurfaces) drift++;
                if (worn > peakWorn) peakWorn = worn;
                if (Time.realtimeSinceStartup >= nextLeak)
                {
                    nextLeak = Time.realtimeSinceStartup + 2f;
                    leakChecks++;
                    int alive = CopiesAlive();
                    if (alive > peakCopies) peakCopies = alive;
                    // Destroy lands at the frame's end: copies of units destroyed this frame may still be counted.
                    if (alive > owned + 24) leaks++;
                }
            }
            Time.timeScale = 1f;
            yield return null;
            yield return wait;
            int endOwned = 0;
            foreach (var v in Object.FindObjectsByType<UnitView>(FindObjectsSortMode.None)) endOwned += v.OwnCopies;
            int endAlive = CopiesAlive();
            SurfaceTrials.last =
                $"{world.time:0} s of play in {frames} frames, {viewFrames} unit-frames ({wornFrames} wearing own materials, {peakWorn} renderers at most): " +
                $"{faults} faults{firstFaults}; the count of worn renderers off in {drift} frames; " +
                $"copies alive more than 24 over what living units hold in {leaks} of {leakChecks} looks (peak {peakCopies}); " +
                $"at the end {endAlive} copies alive, {endOwned} held by living units";
            Destroy(gameObject);
        }
    }

    public sealed class SurfaceBatchWatcher : MonoBehaviour
    {
        bool savedSpectate, savedMemory, saved;
        StarForge.View.RTSCamera rig;
        bool wasScripted;

        void OnDestroy()
        {
            UnitView.LegacyBlocks = false;
            Time.timeScale = 1f;
            if (rig != null) rig.scripted = wasScripted;
            if (saved) { MatchSettings.spectate = savedSpectate; MatchSettings.aiMemory = savedMemory; }
            if (SurfaceTrials.last == "running") SurfaceTrials.last = "stopped part-way";
        }

        static string Differ(Texture2D x, Texture2D y)
        {
            if (x == null || y == null) return "no picture";
            var a = x.GetPixels32();
            var b = y.GetPixels32();
            int w = x.width, h = x.height, bar = Mathf.CeilToInt(h * 0.12f);
            int differ = 0, worst = 0, n = 0;
            for (int row = 0; row < h - bar; row++)      // rows run from the bottom up
                for (int col = 0; col < w; col++)
                {
                    int i = row * w + col;
                    if (i >= b.Length) break;
                    n++;
                    int d = Mathf.Max(Mathf.Abs(a[i].r - b[i].r), Mathf.Max(Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b)));
                    if (d > 2) differ++;
                    if (d > worst) worst = d;
                }
            return $"{differ} of {n} (worst {worst})";
        }

        IEnumerator Start()
        {
            var boot = GameBootstrap.Instance;
            var world = GameWorld.Instance;
            rig = Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            if (boot == null || world == null || rig == null) { SurfaceTrials.last = "no world/camera"; yield break; }
            wasScripted = rig.scripted;
            if (!world.running)
            {
                savedSpectate = MatchSettings.spectate;
                savedMemory = MatchSettings.aiMemory;
                saved = true;
                MatchSettings.spectate = true;
                MatchSettings.aiMemory = false;
                boot.StartMatch();
                yield return new WaitForSecondsRealtime(1f);
            }
            Time.timeScale = 8f;
            float giveUp = Time.realtimeSinceStartup + 150f;
            while (world.time < 240f && world.winner < 0 && Time.realtimeSinceStartup < giveUp) yield return null;
            Time.timeScale = 1f;
            rig.scripted = true;
            Vector2 home = world.Map.StartPos(0);
            foreach (var u in world.units)
                if (u != null && !u.dying && u.team == 0 && u.Type == UnitType.Foundry) { home = u.pos; break; }
            rig.JumpTo(home);
            rig.ZoomTo(55f);
            yield return new WaitForSecondsRealtime(2.5f);
            Time.timeScale = 0f;
            for (int i = 0; i < 4; i++) yield return null;

            var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            var sb = new StringBuilder($"at {world.time:0} s, {world.units.Count} units: ");
            var shots = new Texture2D[4];
            for (int pass = 0; pass < 4; pass++)
            {
                bool legacy = pass % 2 == 0;
                UnitView.LegacyBlocks = legacy;
                for (int i = 0; i < 4; i++) yield return null;
                int withBlock = 0, renderers = 0, views = 0;
                foreach (var v in Object.FindObjectsByType<UnitView>(FindObjectsSortMode.None))
                {
                    var u = v.GetComponent<Unit>();
                    if (u == null || u.dying || u.Type == UnitType.Mech) continue;
                    views++;
                    foreach (var r in v.GetComponentsInChildren<Renderer>())
                    {
                        if (r is ParticleSystemRenderer) continue;
                        renderers++;
                        if (r.HasPropertyBlock()) withBlock++;
                    }
                }
                double sp = 0, dc = 0, bt = 0;
                const int Frames = 60;
                for (int f = 0; f < Frames; f++)
                {
                    yield return null;
                    sp += setPass.LastValue; dc += draws.LastValue; bt += batches.LastValue;
                }
                sb.Append($"[{(legacy ? "property blocks" : "own materials")}: {withBlock} of {renderers} renderers on {views} living units carry a block, " +
                          $"{UnitView.OwnSurfaces} wear own materials, set-pass {sp / Frames:0.0}, draw calls {dc / Frames:0.0}, batches {bt / Frames:0.0} a frame] ");
                yield return new WaitForEndOfFrame();
                shots[pass] = ScreenCapture.CaptureScreenshotAsTexture();
                if (pass < 2) System.IO.File.WriteAllBytes(legacy ? "Temp/surface_blocks.png" : "Temp/surface_own.png", shots[pass].EncodeToPNG());
                yield return null;
            }
            setPass.Dispose(); draws.Dispose(); batches.Dispose();
            // The look must not change: one way against the other, beside the same way against
            // itself a second later (the frame counter in the top bar and whatever runs on the
            // unscaled clock move by themselves), the top bar left out.
            sb.Append("| pictures, pixels more than 2 of 255 apart below the top bar: blocks against own " + Differ(shots[0], shots[1]) +
                      "; blocks against blocks " + Differ(shots[0], shots[2]) + "; own against own " + Differ(shots[1], shots[3]));
            foreach (var t in shots) if (t != null) Destroy(t);
            SurfaceTrials.last = sb.ToString();
            // Now, not after a delay: a delayed Destroy counts the game's clock, and it is stopped.
            Destroy(gameObject);         // OnDestroy puts the switch, the clock, the camera and the settings back
        }
    }
}
