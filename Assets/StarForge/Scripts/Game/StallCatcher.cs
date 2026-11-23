// StallCatcher.cs -- -sfcatchstall <path>: when the main thread stops for over a second, ask macOS what
// every thread of the player is doing, while it is still stuck. Multi-second freezes come rarely and
// leave nothing in the log; a stack taken during one says where. Only with the flag, on the player.
using System;
using System.Diagnostics;
using System.Threading;

namespace StarForge.Game
{
    public static class StallCatcher
    {
        const long StallMs = 1000;
        const int MostSamples = 3;

        static long lastBeat;
        static int stalls, written, failed;
        static string lastError;
        static string basePath;
        static Thread watcher;
        static volatile bool stopped;

        public static void Start(string path)
        {
            if (watcher != null) return;
            basePath = path;
            watcher = new Thread(Watch) { IsBackground = true, Name = "StallCatcher" };
            watcher.Start();
        }

        /// <summary>The main thread is alive: called once a frame, from the first on, so loading the scene
        /// does not count.</summary>
        public static void Beat() => Interlocked.Exchange(ref lastBeat, Stopwatch.GetTimestamp());

        /// <summary>The run is over: quitting does not count either.</summary>
        public static void Stop() => stopped = true;

        /// <summary>For the report: "no stall" and "never sampled" must not read the same.</summary>
        public static string Summary() =>
            $"{stalls} stall(s) over {StallMs} ms, {written} sample(s) written ({basePath}.N.txt){(failed > 0 ? $", {failed} failed: {lastError}" : "")}";

        static void Watch()
        {
            int pid = Process.GetCurrentProcess().Id;
            int taken = 0;
            bool inStall = false;
            while (taken < MostSamples && !stopped)
            {
                Thread.Sleep(100);
                long beat = Interlocked.Read(ref lastBeat);
                if (beat == 0) continue;
                long ms = (Stopwatch.GetTimestamp() - beat) * 1000 / Stopwatch.Frequency;
                if (ms < StallMs) { inStall = false; continue; }
                if (inStall) continue;
                inStall = true;
                taken++;
                Interlocked.Increment(ref stalls);
                string file = $"{basePath}.{taken}.txt";
                try
                {
                    using (var p = Process.Start(new ProcessStartInfo("/usr/bin/sample", $"{pid} 2 -file \"{file}\"") { UseShellExecute = false, CreateNoWindow = true }))
                    {
                        if (!p.WaitForExit(15000))
                        {
                            try { p.Kill(); } catch (Exception) { }   // disposed is not killed: it outlived the run
                            failed++; lastError = "sample ran past 15 s";
                        }
                        else if (System.IO.File.Exists(file)) written++;
                        else { failed++; lastError = $"sample exited {p.ExitCode} with no file"; }
                    }
                }
                catch (Exception e) { failed++; lastError = e.Message; return; }
            }
        }
    }
}
