// AgentAudio.cs — play mode an agent starts runs with the Editor's sound off.
using System;
using UnityEditor;
using UnityEngine;

namespace StarForge.EditorTools
{
    /// <summary>Play mode an agent starts is silent: the user sits beside a Mac that would
    /// otherwise play battles, music and comms for hours of trials nobody listens to (and the
    /// agent cannot hear). The inbox (<c>Tools/editor.sh</c>) marks play one of its commands
    /// started -- <c>play</c>, an evaluation menu, a <c>call</c> that enters play -- and entering
    /// it turns on the Game view's Mute Audio; the user's own setting comes back when play stops
    /// (or, after a restart mid-run, once the Editor has loaded). Play the user starts by hand is
    /// left alone however busy the agents are. A session that enters play another way (MCP) calls
    /// <see cref="Mute"/> first; a run that should be heard calls <see cref="Hear"/> first. Both
    /// hold for the next play entered within <see cref="WindowMinutes"/>, and only that one.</summary>
    [InitializeOnLoad]
    public static class AgentAudio
    {
        public const double WindowMinutes = 3.0;
        // EditorPrefs, not SessionState: a mute left by an Editor that quit mid-run is undone at the next load.
        const string MutedKey = "StarForge.AgentAudio.muted", HadKey = "StarForge.AgentAudio.userMute";
        const string PlayKey = "StarForge.AgentAudio.agentPlay", NextKey = "StarForge.AgentAudio.muteNext", HearKey = "StarForge.AgentAudio.hear";
        static bool inCommand, startedInCommand;

        static AgentAudio()
        {
            EditorApplication.playModeStateChanged += Changed;
            // After the Editor has loaded, so a mute it restores of its own comes before ours.
            EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) Restore(false); };
        }

        static void Stamp(string key) => SessionState.SetString(key, DateTime.UtcNow.Ticks.ToString());
        static bool Recent(string key, double minutes) =>
            long.TryParse(SessionState.GetString(key, ""), out long t) && (DateTime.UtcNow - new DateTime(t, DateTimeKind.Utc)).TotalMinutes < minutes;

        /// <summary>The inbox wraps every command in these: play entered while one runs, or
        /// asked for by it and entered on a later update, is the agent's.</summary>
        public static void BeginCommand() { inCommand = true; startedInCommand = false; }

        public static void EndCommand(bool wasPlaying)
        {
            inCommand = false;
            // Play mode can start inside the command (EnterPlaymode leaves edit mode at once):
            // then it is handled, and a fresh mark would mute the user's next play by hand.
            if (!startedInCommand && !wasPlaying && EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying) Stamp(PlayKey);
            else if (startedInCommand) SessionState.EraseString(PlayKey);
        }

        /// <summary>The inbox's <c>play</c> asks for play mode, which starts on a later update.</summary>
        public static void AgentStartsPlay() => Stamp(PlayKey);

        static void Changed(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.ExitingEditMode)
            {
                if (inCommand) startedInCommand = true;
                bool agent = inCommand || Recent(PlayKey, 1.0) || Recent(NextKey, WindowMinutes);
                SessionState.EraseString(PlayKey);
                SessionState.EraseString(NextKey);
                if (agent && !Recent(HearKey, WindowMinutes)) MuteNow();
            }
            else if (s == PlayModeStateChange.EnteredEditMode) Restore(true);
        }

        /// <summary>In play: mute until it stops. In edit mode: mute the next play entered within
        /// <see cref="WindowMinutes"/> (for a session that enters play through MCP).</summary>
        public static string Mute()
        {
            if (!EditorApplication.isPlaying) { Stamp(NextKey); return $"the next play mode entered within {WindowMinutes:0} minutes will be muted"; }
            MuteNow();
            return "muted until play stops";
        }

        static void MuteNow()
        {
            if (!EditorPrefs.GetBool(MutedKey, false))
            {
                EditorPrefs.SetBool(HadKey, EditorUtility.audioMasterMute);
                EditorPrefs.SetBool(MutedKey, true);
            }
            if (!EditorUtility.audioMasterMute)
            {
                EditorUtility.audioMasterMute = true;
                Debug.Log("[StarForge] Sound muted for an agent's run (Game view > Mute Audio); it comes back when play stops.");
            }
        }

        /// <summary>Sound for the next play entered within <see cref="WindowMinutes"/>, or the one
        /// running now; never over the user's own Mute Audio.</summary>
        public static string Hear()
        {
            Stamp(HearKey);
            if (EditorApplication.isPlaying && EditorPrefs.GetBool(MutedKey, false) && !EditorPrefs.GetBool(HadKey, false))
                EditorUtility.audioMasterMute = false;
            return "sound on for this play session (or the next one, within the window)";
        }

        static void Restore(bool playEnded)
        {
            if (playEnded) { SessionState.EraseString(HearKey); SessionState.EraseString(PlayKey); }
            if (!EditorPrefs.GetBool(MutedKey, false)) return;
            EditorUtility.audioMasterMute = EditorPrefs.GetBool(HadKey, false);
            EditorPrefs.DeleteKey(MutedKey);
            EditorPrefs.DeleteKey(HadKey);
        }

        public static string State() =>
            $"muted={EditorUtility.audioMasterMute} byAgent={EditorPrefs.GetBool(MutedKey, false)} agentPlayPending={Recent(PlayKey, 1.0)} " +
            $"muteNext={Recent(NextKey, WindowMinutes)} hear={Recent(HearKey, WindowMinutes)} listenerVolume={AudioListener.volume:0.##}";
    }
}
