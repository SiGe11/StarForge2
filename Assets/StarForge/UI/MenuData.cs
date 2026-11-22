// MenuData.cs — what the menus keep between runs: the player's settings and their
// service record. Both live in PlayerPrefs. Nothing here is read by the simulation or
// the AI: the camera and audio values are applied to their components at scene start
// (MenuController), and the record is written only for matches the menu launched.
using System;
using System.Collections.Generic;
using UnityEngine;
using StarForge.AI;
using StarForge.View;

namespace StarForge.UI
{
    /// <summary>The player's settings. Absent keys mean the scene's own values, so a machine
    /// that never opened Settings plays exactly as before.</summary>
    public static class GameSettings
    {
        const string EdgeKey = "sf_edge_pan", PanKey = "sf_pan_speed", RotateKey = "sf_rotate_speed",
                     PerfKey = "sf_show_perf", UiKey = "sf_vol_ui", SpeedKey = "sf_game_speed", DiffKey = "sf_difficulty";

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(AudioDirector.MasterKey, 0.7f);
            set { PlayerPrefs.SetFloat(AudioDirector.MasterKey, Mathf.Clamp01(value)); }
        }
        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(AudioDirector.MusicKey, 1f);
            set { PlayerPrefs.SetFloat(AudioDirector.MusicKey, Mathf.Clamp01(value)); }
        }
        /// <summary>The menus' own clicks and hovers.</summary>
        public static float InterfaceVolume
        {
            get => PlayerPrefs.GetFloat(UiKey, 1f);
            set { PlayerPrefs.SetFloat(UiKey, Mathf.Clamp01(value)); }
        }
        public static bool EdgePan
        {
            get => PlayerPrefs.GetInt(EdgeKey, 1) != 0;
            set { PlayerPrefs.SetInt(EdgeKey, value ? 1 : 0); }
        }
        /// <summary>Multiplies the camera's pan speed, 0.5-2.</summary>
        public static float PanSpeed
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(PanKey, 1f), 0.5f, 2f);
            set { PlayerPrefs.SetFloat(PanKey, Mathf.Clamp(value, 0.5f, 2f)); }
        }
        /// <summary>Multiplies the camera's Q/E turn speed, 0.5-2.</summary>
        public static float RotateSpeed
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(RotateKey, 1f), 0.5f, 2f);
            set { PlayerPrefs.SetFloat(RotateKey, Mathf.Clamp(value, 0.5f, 2f)); }
        }
        /// <summary>The frame-rate readout in the top bar.</summary>
        public static bool ShowPerformance
        {
            get => PlayerPrefs.GetInt(PerfKey, 1) != 0;
            set { PlayerPrefs.SetInt(PerfKey, value ? 1 : 0); }
        }
        /// <summary>The speed a match starts at (Operation setup): index into <see cref="Speeds"/>.</summary>
        public static int StartSpeed
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(SpeedKey, 1), 0, Speeds.Length - 1);
            set { PlayerPrefs.SetInt(SpeedKey, Mathf.Clamp(value, 0, Speeds.Length - 1)); }
        }
        public static readonly float[] Speeds = { 0.75f, 1f, 1.5f, 2f };
        public static readonly string[] SpeedNames = { "SLOW", "NORMAL", "FAST", "FASTER" };

        /// <summary>The opponent last picked. Saved here, put back only in a built player
        /// (the Editor's evaluation sets <c>MatchSettings.difficulty</c> itself).</summary>
        public static int SavedDifficulty
        {
            get => PlayerPrefs.GetInt(DiffKey, -1);
            set { PlayerPrefs.SetInt(DiffKey, value); }
        }

        /// <summary>Writes the preferences. The setters above do not: a slider reports every step of a drag, and a write
        /// to the preferences file for each was the cost (the menu flushes when a page closes and when it goes away).</summary>
        public static void Flush() => PlayerPrefs.Save();

        /// <summary>Puts every key back to the scene's own value.</summary>
        public static void ResetAll()
        {
            foreach (var k in new[] { AudioDirector.MasterKey, AudioDirector.MusicKey, UiKey, EdgeKey, PanKey, RotateKey, PerfKey, SpeedKey })
                PlayerPrefs.DeleteKey(k);
            PlayerPrefs.Save();
        }
    }

    /// <summary>The player's career against the AI: matches the menu launched (Operation
    /// setup's Deploy, and their rematches), never spectated or cheated ones, nor anything a
    /// benchmark, evaluation or trial ends.</summary>
    [Serializable]
    public sealed class ServiceRecord
    {
        const string Key = "sf_service_record";
        public const int RecentKept = 8;

        [Serializable]
        public sealed class Entry
        {
            public int result;       // 1 won, 0 lost, 2 draw
            public int difficulty;   // AIDifficulty
            public float seconds;
            public bool tactical;
            public string when;      // yyyy-MM-dd HH:mm, local
        }

        public int matches, wins, losses, draws;
        public int[] winsBy = new int[3], lossesBy = new int[3];
        public float seconds;
        public long ore;
        public int produced, built, killed, lost;
        public float fastestWin;     // 0 = none yet
        public List<Entry> recent = new List<Entry>();

        public float WinRate => matches > 0 ? (float)wins / matches : 0f;

        public static ServiceRecord Load()
        {
            string json = PlayerPrefs.GetString(Key, "");
            ServiceRecord r = null;
            if (!string.IsNullOrEmpty(json))
            {
                try { r = JsonUtility.FromJson<ServiceRecord>(json); }
                catch (Exception) { r = null; }
            }
            r ??= new ServiceRecord();
            if (r.winsBy == null || r.winsBy.Length != 3) r.winsBy = new int[3];
            if (r.lossesBy == null || r.lossesBy.Length != 3) r.lossesBy = new int[3];
            r.recent ??= new List<Entry>();
            return r;
        }

        public void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        public static void Erase()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        /// <summary>Adds one finished match. <paramref name="result"/>: 1 won, 0 lost, 2 draw.</summary>
        public void Add(int result, AIDifficulty difficulty, float matchSeconds, bool tactical,
                        int oreMined, int unitsProduced, int structuresBuilt, int enemyLosses, int ownLosses)
        {
            int d = Mathf.Clamp((int)difficulty, 0, 2);
            matches++;
            if (result == 1) { wins++; winsBy[d]++; if (fastestWin <= 0f || matchSeconds < fastestWin) fastestWin = matchSeconds; }
            else if (result == 0) { losses++; lossesBy[d]++; }
            else draws++;
            seconds += matchSeconds;
            ore += oreMined;
            produced += unitsProduced;
            built += structuresBuilt;
            killed += enemyLosses;
            lost += ownLosses;
            recent.Insert(0, new Entry
            {
                result = result, difficulty = d, seconds = matchSeconds, tactical = tactical,
                when = DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            });
            if (recent.Count > RecentKept) recent.RemoveRange(RecentKept, recent.Count - RecentKept);
        }

        public static string Clock(float s)
        {
            int t = Mathf.Max(0, Mathf.FloorToInt(s));
            return t >= 3600 ? $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}" : $"{t / 60}:{t % 60:00}";
        }

        public static string Hours(float s)
        {
            int m = Mathf.FloorToInt(s / 60f);
            return m >= 60 ? $"{m / 60}h {m % 60:00}m" : $"{m}m";
        }
    }
}
