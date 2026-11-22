// MaintTrials.cs -- checks for the bug hunt's fixes (2026-11-20 night); each is one `Tools/editor.sh call StarForge.EditorTools.MaintTrials.<Name>`.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using StarForge.AI;
using StarForge.Game;
using StarForge.UI;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class MaintTrials
    {
        /// <summary>F3: what a slider drag costs in preference writes. 200 setter calls, and 200 explicit saves (what each setter did before the fix).</summary>
        public static string SliderCost()
        {
            float keep = GameSettings.MasterVolume;
            double t0 = UnityEditor.EditorApplication.timeSinceStartup;
            for (int i = 0; i < 200; i++) GameSettings.MasterVolume = 0.1f + 0.004f * i;
            double setters = (UnityEditor.EditorApplication.timeSinceStartup - t0) * 1000.0;
            t0 = UnityEditor.EditorApplication.timeSinceStartup;
            for (int i = 0; i < 200; i++) PlayerPrefs.Save();
            double saves = (UnityEditor.EditorApplication.timeSinceStartup - t0) * 1000.0;
            GameSettings.MasterVolume = keep;
            GameSettings.Flush();
            return $"200 setter calls {setters:0.0} ms; 200 explicit PlayerPrefs.Save {saves:0.0} ms (the old setters did one each)";
        }

        /// <summary>F5: a second StartMatch from Paused must not stack a handler on the world's tick. Needs a fresh play at the menu.</summary>
        public static string StartTwice()
        {
            var boot = GameBootstrap.Instance;
            if (!Application.isPlaying || boot == null) return "needs play mode";
            if (boot.State != MatchState.Menu) return "needs a fresh play at the menu (state " + boot.State + ")";
            var world = UnityEngine.Object.FindAnyObjectByType<GameWorld>();
            var f = typeof(GameWorld).GetField("Ticked", BindingFlags.Instance | BindingFlags.NonPublic);
            int Handlers() => (f?.GetValue(world) as Delegate)?.GetInvocationList().Length ?? -1;
            bool memory = MatchSettings.aiMemory;
            MatchSettings.aiMemory = false;   // the match left running must not write the trial into the AI memory file
            int first, second;
            try
            {
                boot.StartMatch();
                first = Handlers();
                boot.TogglePause();
                boot.StartMatch();
                second = Handlers();
            }
            finally { MatchSettings.aiMemory = memory; }
            return $"world.Ticked handlers after the first start {first}, after a second from Paused {second} (target 1 and 1; two handlers = the AI ticks twice); state {boot.State}, AI {(boot.AI != null)}";
        }

        static readonly (string name, Func<bool> get, Action<bool> set)[] Switches =
        {
            ("Commander.IgnoreSites", () => Commander.IgnoreSites, v => Commander.IgnoreSites = v),
            ("Commander.MechGateInBase", () => Commander.MechGateInBase, v => Commander.MechGateInBase = v),
            ("Commander.RepositionOff", () => Commander.RepositionOff, v => Commander.RepositionOff = v),
            ("Commander.LegacyNoMaxOut", () => Commander.LegacyNoMaxOut, v => Commander.LegacyNoMaxOut = v),
            ("Commander.LegacySweep", () => Commander.LegacySweep, v => Commander.LegacySweep = v),
            ("Commander.LegacyDiggerSafety", () => Commander.LegacyDiggerSafety, v => Commander.LegacyDiggerSafety = v),
            ("Commander.LegacyWayHome", () => Commander.LegacyWayHome, v => Commander.LegacyWayHome = v),
            ("Perception.LegacyMechMemory", () => Perception.LegacyMechMemory, v => Perception.LegacyMechMemory = v),
            ("MechBrain.LegacyHunt", () => MechBrain.LegacyHunt, v => MechBrain.LegacyHunt = v),
            ("MechBrain.LegacyCommit", () => MechBrain.LegacyCommit, v => MechBrain.LegacyCommit = v),
            ("OpponentModel.LegacyRead", () => OpponentModel.LegacyRead, v => OpponentModel.LegacyRead = v),
            ("MechCore.LegacyBalance", () => MechCore.LegacyBalance, v => MechCore.LegacyBalance = v),
            ("GameWorld.LegacyBreakPick", () => GameWorld.LegacyBreakPick, v => GameWorld.LegacyBreakPick = v),
            ("MechView.LegacyTrackRide", () => StarForge.View.MechView.LegacyTrackRide, v => StarForge.View.MechView.LegacyTrackRide = v),
        };

        /// <summary>F6: sets every A/B switch the play-exit reset covers. Then stop play and read SwitchesState: all false.</summary>
        public static string SwitchesOn()
        {
            foreach (var s in Switches) s.set(true);
            return SwitchesState();
        }

        public static string SwitchesState()
        {
            var sb = new StringBuilder();
            int on = 0;
            foreach (var s in Switches) if (s.get()) { on++; sb.Append(s.name).Append(' '); }
            return $"{on} of {Switches.Length} switches on{(on > 0 ? ": " + sb : "")}";
        }

        /// <summary>F8: the UXML lint finds a double hyphen and an unclosed comment, and passes the project's own files.</summary>
        public static string UxmlLintSelfTest()
        {
            var sb = new StringBuilder();
            int a = UxmlLint.Scan("<a>\n<!-- fine: m-page -->\n<!-- bad m-portrait--0 -->\n<!-- ends in a hyphen --->\n<b/>\n<!-- never closed").Count;
            sb.AppendLine($"scratch text: {a} flagged (target 3: line 3, line 4 and the unclosed one)");
            foreach (var p in Directory.GetFiles("Assets/StarForge/UI", "*.uxml"))
                sb.AppendLine($"{p}: {UxmlLint.Scan(File.ReadAllText(p)).Count} flagged (target 0)");
            return sb.ToString();
        }

        /// <summary>F8: a menu whose layout is missing logs one error, switches itself off and has the match start without it (skipMenu),
        /// instead of throwing from Awake and then on every frame. Play mode; puts skipMenu back.</summary>
        public static string MenuAwakeFails()
        {
            if (!Application.isPlaying) return "needs play mode";
            int errors = 0, exceptions = 0;
            Application.LogCallback h = (msg, stack, type) =>
            {
                if (type == LogType.Error && msg.Contains("The menu could not be built")) errors++;
                else if (type == LogType.Exception) exceptions++;
            };
            Application.logMessageReceived += h;
            bool skipBefore = MatchSettings.skipMenu;
            var go = new GameObject("MenuAwakeFails");
            try
            {
                go.AddComponent<UIDocument>();   // no panel, so no root element: the first lookup finds nothing
                var m = go.AddComponent<MenuController>();
                return $"errors logged {errors} (target 1), exceptions {exceptions} (target 0), menu enabled {m.enabled} (target False), skipMenu {MatchSettings.skipMenu} (target True)";
            }
            finally
            {
                Application.logMessageReceived -= h;
                MatchSettings.skipMenu = skipBefore;
                UnityEngine.Object.Destroy(go);
            }
        }

        /// <summary>F2: the map the match is on and the pin a trial left; pin 2587, then reload by NEW MAP or REMATCH and read again.</summary>
        public static string MapSeedNow()
        {
            var m = UnityEngine.Object.FindAnyObjectByType<MapInfo>();
            return $"map seed {(m != null ? m.seed.ToString() : "none")}, MatchSettings.mapSeed {MatchSettings.mapSeed}";
        }

        public static string PinMap2587() { MatchSettings.mapSeed = 2587; return MapSeedNow(); }

        /// <summary>F2: the setup page's NEW MAP row itself (MenuTrials.NewMap reloads by its own action and skips the row's lambda): open the page, click the row, read the seed after the reload.</summary>
        public static string PressNewMap()
        {
            if (!UnityEditor.EditorApplication.isPlaying) return "not playing";
            var m = UnityEngine.Object.FindAnyObjectByType<MenuController>();
            if (m == null) return "no MenuController";
            var open = typeof(MenuController).GetMethod("OpenSetup", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (open == null) return "no OpenSetup";
            open.Invoke(m, new object[] { false });
            var root = m.GetComponent<UIDocument>().rootVisualElement;
            Label found = null;
            root.Query<Label>().ForEach(l => { if (found == null && l.text != null && l.text.StartsWith("NEW MAP")) found = l; });
            if (found == null) return "no NEW MAP row";
            using (var e = ClickEvent.GetPooled()) { e.target = found; found.SendEvent(e); }
            return "clicked NEW MAP; " + MapSeedNow();
        }
    }
}
