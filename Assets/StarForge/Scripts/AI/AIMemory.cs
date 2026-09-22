// AIMemory.cs — what the AI remembers about the player between matches.
//
// The original README listed this as a limitation: "the strategy weights adapt
// online within a single match and are not persisted between games". Here the
// learned bandit weights, the long-run picture of the player's style and the
// match record survive, which is the cross-game learning loop hardware.md
// section 11 describes. It stays a *prior*: every match still starts by
// scouting and updating on what is actually seen.
using System;
using System.IO;
using UnityEngine;

namespace StarForge.AI
{
    [Serializable]
    public sealed class AIMemoryData
    {
        public int version = 1;
        public int games;
        public int aiWins;
        public float[] weights = new float[0];
        public float[] styleHistogram = new float[(int)PlayerStrat.Count];
        public float aggression = 0.45f, expansion = 0.3f, defensive = 0.4f, harass = 0.25f, teching = 0.35f;
        public float[] strategySeconds = new float[(int)Strategy.Count];
        public string lastRead = "";
        // The last few matches, newest last: how the AI opened, the plan it spent
        // longest on, and whether it won. It steers away from repeating them.
        public int[] recentOpenings = new int[0];
        public int[] recentPlans = new int[0];
        public int[] recentWins = new int[0];
    }

    public static class AIMemory
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "starforge_ai_memory.json");

        public static AIMemoryData Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var d = JsonUtility.FromJson<AIMemoryData>(File.ReadAllText(FilePath));
                    if (d != null && d.version == 1) return Sanitize(d);
                }
            }
            catch (Exception e) { Debug.LogWarning("[StarForge] AI memory unreadable, starting fresh: " + e.Message); }
            return new AIMemoryData();
        }

        public static void Save(AIMemoryData d)
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(d, true)); }
            catch (Exception e) { Debug.LogWarning("[StarForge] could not save AI memory: " + e.Message); }
        }

        public static void Reset()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception e) { Debug.LogWarning("[StarForge] could not reset AI memory: " + e.Message); }
        }

        /// <summary>How much to trust the memory: grows with games played, capped so the
        /// hand-authored doctrine always keeps a say.</summary>
        public static float Trust(AIMemoryData d) => Mathf.Clamp01(d.games * 0.18f) * 0.7f;

        static AIMemoryData Sanitize(AIMemoryData d)
        {
            if (d.styleHistogram == null || d.styleHistogram.Length != (int)PlayerStrat.Count)
                d.styleHistogram = new float[(int)PlayerStrat.Count];
            if (d.strategySeconds == null || d.strategySeconds.Length != (int)Strategy.Count)
                d.strategySeconds = new float[(int)Strategy.Count];
            if (d.weights == null || d.weights.Length != (int)Strategy.Count * StrategySelector.NFEAT)
                d.weights = new float[0];
            d.recentOpenings ??= new int[0];
            d.recentPlans ??= new int[0];
            d.recentWins ??= new int[0];
            return d;
        }

        /// <summary>Append to a short history, keeping the newest `keep` entries.</summary>
        public static int[] Push(int[] history, int value, int keep = 4)
        {
            var list = new System.Collections.Generic.List<int>(history ?? new int[0]) { value };
            while (list.Count > keep) list.RemoveAt(0);
            return list.ToArray();
        }
    }
}
