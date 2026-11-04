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
                string file = $"{basePath}.{taken}.txt";
                try
                {
                    using (var p = Process.Start(new ProcessStartInfo("/usr/bin/sample", $"{pid} 2 -file \"{file}\"") { UseShellExecute = false, CreateNoWindow = true }))
                        p.WaitForExit(15000);
                }
                catch (Exception) { return; }
            }
        }
    }
}
