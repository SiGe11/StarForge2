// EditorThrottle.cs — reads and sets Preferences > General > Interaction Mode from the shell.
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace StarForge.EditorTools
{
    /// <summary>Interaction Mode is an Editor preference (this machine, not the project). At Default
    /// the Editor idles between updates, and behind another app a Quick evaluation ran 5.7-6.0x
    /// of its 8x (in front 7.9x); at No Throttling, behind another app, 7.6x. Idle in edit mode
    /// behind another app it costs 3-4% CPU against 2.3%. Set on this Mac on 2026-10-27; these
    /// write what the Preferences window writes, then apply it as the window does.
    /// <c>Tools/editor.sh call StarForge.EditorTools.EditorThrottle.State</c> (or NoThrottling, Default).</summary>
    public static class EditorThrottle
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        public static string State() =>
            $"InteractionMode={(EditorPrefs.HasKey("InteractionMode") ? Name(EditorPrefs.GetInt("InteractionMode")) : "Default (unset)")} " +
            $"ApplicationIdleTime={(EditorPrefs.HasKey("ApplicationIdleTime") ? EditorPrefs.GetInt("ApplicationIdleTime") + " ms" : "unset")}";

        /// <summary>The Preferences window's own enum name for a value.</summary>
        static string Name(int value)
        {
            var t = typeof(EditorApplication).Assembly.GetType("UnityEditor.PreferencesProvider+InteractionMode");
            return t == null ? value.ToString() : Enum.GetValues(t).Cast<object>().FirstOrDefault(v => (int)v == value)?.ToString() ?? value.ToString();
        }

        static string Apply()
        {
            var m = typeof(EditorApplication).GetMethod("UpdateInteractionModeSettings", Any);
            if (m == null) return "set (UpdateInteractionModeSettings not found: applies at the next Editor start)";
            m.Invoke(null, null);
            return "applied";
        }

        /// <summary>No Throttling: mode 1, no idle time.</summary>
        public static string NoThrottling()
        {
            EditorPrefs.SetInt("ApplicationIdleTime", 0);
            EditorPrefs.SetInt("InteractionMode", 1);
            return Apply() + "; " + State();
        }

        /// <summary>Default: the window deletes both keys.</summary>
        public static string Default()
        {
            EditorPrefs.DeleteKey("ApplicationIdleTime");
            EditorPrefs.DeleteKey("InteractionMode");
            return Apply() + "; " + State();
        }
    }
}
