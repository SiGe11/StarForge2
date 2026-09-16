// AIEvalRunner.cs — plays the adaptive AI against the four scripted archetypes
// in the real scene (NavMesh, fog, the full command API) and reports win rate,
// how often and how quickly it identifies the opponent, what it chose to do
// about it, and its APM. The Unity counterpart of the original's `make aieval`.
//
// Start it from StarForge > Evaluate AI. It runs with rendering cut to the
// minimum and the game clock accelerated, reloading the scene between matches.
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using StarForge.AI;
using StarForge.World;

namespace StarForge.Game
{
    [DefaultExecutionOrder(-60)]
    public sealed class AIEvalRunner : MonoBehaviour
    {
        public const string PrefGames = "sf_eval_games";
        public const string PrefSeconds = "sf_eval_seconds";

        sealed class KindStats
        {
            public int games, wins;
            public float length, beliefAcc, firstIdent, apm, peakApm, switches;
            public int identN;
            public readonly float[] stratUse = new float[(int)Strategy.Count];
            public readonly float[] labelUse = new float[(int)PlayerStrat.Count];
            public long actions, denied;
        }

        static bool active;
        static int kind, game, gamesPerKind;
        static float maxSeconds;
        static KindStats[] stats;
        static float wallStart;

        GameWorld world;
        GameBootstrap boot;
        ScriptedOpponent opponent;
        int samples, correct;
        float firstCorrect = -1f, apmSum;
        int apmN;
        float peakApm;
        readonly float[] stratUse = new float[(int)Strategy.Count];
        readonly float[] labelUse = new float[(int)PlayerStrat.Count];
        bool finished;

        void Awake()
        {
            if (!active && PlayerPrefs.GetInt(PrefGames, 0) > 0)
            {
                active = true;
                gamesPerKind = PlayerPrefs.GetInt(PrefGames);
                maxSeconds = PlayerPrefs.GetFloat(PrefSeconds, 600f);
                PlayerPrefs.DeleteKey(PrefGames);
                PlayerPrefs.Save();
                kind = 0;
                game = 0;
                stats = new KindStats[(int)ScriptedOpponent.Kind.Count];
                for (int i = 0; i < stats.Length; i++) stats[i] = new KindStats();
                wallStart = Time.realtimeSinceStartup;
                Debug.Log($"[AIEval] {gamesPerKind} games per opponent, {maxSeconds:0}s cap");
            }
            if (!active) { enabled = false; return; }

            MatchSettings.skipMenu = true;
            MatchSettings.spectate = false;
            MatchSettings.aiMemory = false;          // every match starts from doctrine
            MatchSettings.difficulty = AIDifficulty.Commander;
            MatchSettings.seed = 1000u + (uint)(game * 37);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }

        void Start()
        {
            world = FindAnyObjectByType<GameWorld>();
            boot = FindAnyObjectByType<GameBootstrap>();
            foreach (var cam in FindObjectsByType<Camera>()) cam.cullingMask = 0;
            boot.MatchStarted += OnMatchStarted;
            if (boot.State == MatchState.Playing) OnMatchStarted();
        }

        void OnMatchStarted()
        {
            opponent = new ScriptedOpponent(world, 0, (ScriptedOpponent.Kind)kind);
            world.Ticked += OnTick;
            // The world clamps each step to 0.1 s, so a high time scale buys speed
            // without coarsening the simulation beyond what it already tolerates.
            Time.timeScale = 8f;
        }

        void OnTick(float dt)
        {
            if (finished) return;
            opponent.Update(dt);
            var ai = boot.AI;
            if (ai == null) return;
            if (world.time > 30f)
            {
                samples++;
                if (ai.Dbg.believed == opponent.Truth)
                {
                    correct++;
                    if (firstCorrect < 0f) firstCorrect = world.time;
                }
                stratUse[(int)ai.Dbg.strategy]++;
                labelUse[(int)ai.Dbg.believed]++;
            }
            if (ai.Dbg.apm > 0f) { apmSum += ai.Dbg.apm; apmN++; }
            peakApm = Mathf.Max(peakApm, ai.Dbg.apmPeak);

            if (world.winner >= 0 || world.time >= maxSeconds) Finish();
        }

        void Finish()
        {
            finished = true;
            world.Ticked -= OnTick;
            var ai = boot.AI;
            var s = stats[kind];
            s.games++;
            if (world.winner == 1) s.wins++;
            s.length += world.time;
            s.beliefAcc += samples > 0 ? (float)correct / samples : 0f;
            if (firstCorrect >= 0f) { s.firstIdent += firstCorrect; s.identN++; }
            s.apm += apmN > 0 ? apmSum / apmN : 0f;
            s.peakApm = Mathf.Max(s.peakApm, peakApm);
            s.switches += ai.Dbg.switches;
            s.actions += ai.Dbg.actions;
            s.denied += ai.Dbg.denied;
            for (int i = 0; i < stratUse.Length; i++) s.stratUse[i] += stratUse[i];
            for (int i = 0; i < labelUse.Length; i++) s.labelUse[i] += labelUse[i];

            Debug.Log($"[AIEval] vs {(ScriptedOpponent.Kind)kind} game {game + 1}: " +
                      $"{(world.winner == 1 ? "AI won" : world.winner == 0 ? "AI lost" : "time cap")} at {world.time:0}s, " +
                      $"read correctly {(samples > 0 ? 100f * correct / samples : 0f):0}% of the time");

            game++;
            if (game >= gamesPerKind) { game = 0; kind++; }
            if (kind >= (int)ScriptedOpponent.Kind.Count) { Report(); return; }
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        static void Report()
        {
            active = false;
            Time.timeScale = 1f;
            var sb = new StringBuilder();
            sb.AppendLine($"Adaptive AI evaluation — {gamesPerKind} games per opponent, {maxSeconds:0}s cap");
            sb.AppendLine("opponent    wins   avg len  belief acc  1st ident  APM avg/peak  switches  top plans / read as");
            int totalWins = 0, totalGames = 0;
            for (int k = 0; k < stats.Length; k++)
            {
                var s = stats[k];
                if (s.games == 0) continue;
                totalWins += s.wins;
                totalGames += s.games;
                int a = 0, b = 1;
                for (int i = 0; i < s.stratUse.Length; i++) if (s.stratUse[i] > s.stratUse[a]) a = i;
                for (int i = 0; i < s.stratUse.Length; i++) if (i != a && s.stratUse[i] > s.stratUse[b]) b = i;
                float labels = 0f;
                foreach (var v in s.labelUse) labels += v;
                var read = new List<string>();
                for (int i = 0; i < s.labelUse.Length; i++)
                    if (labels > 0f && s.labelUse[i] / labels >= 0.1f)
                        read.Add($"{AINames.Of((PlayerStrat)i)} {100f * s.labelUse[i] / labels:0}%");
                sb.AppendLine(
                    $"{(ScriptedOpponent.Kind)k,-10}  {s.wins}/{s.games,-3}  {s.length / s.games,6:0}s  {100f * s.beliefAcc / s.games,8:0}%  " +
                    $"{(s.identN > 0 ? s.firstIdent / s.identN : -1f),7:0}s   {s.apm / s.games,4:0}/{s.peakApm,-4:0}      {s.switches / s.games,5:0.0}   " +
                    $"{AINames.Of((Strategy)a)}, {AINames.Of((Strategy)b)} / {string.Join(", ", read)}");
                sb.AppendLine($"            actions {s.actions / s.games} per game, {s.denied / s.games} refused by the APM cap " +
                              $"({100.0 * s.denied / System.Math.Max(1L, s.actions + s.denied):0}% of intents)");
            }
            sb.AppendLine($"overall: {totalWins}/{totalGames} wins ({100f * totalWins / Mathf.Max(1, totalGames):0}%)  " +
                          $"wall time {Time.realtimeSinceStartup - wallStart:0}s");
            string text = sb.ToString();
            Debug.Log("[AIEval]\n" + text);
            try { File.WriteAllText(Path.Combine(Application.persistentDataPath, "starforge_ai_eval.txt"), text); }
            catch { /* the console copy is enough */ }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        void OnDestroy()
        {
            if (world != null) world.Ticked -= OnTick;
            if (boot != null) boot.MatchStarted -= OnMatchStarted;
        }
    }
}
