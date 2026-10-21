using System;
using UnityEngine;

namespace StarForge.Game
{
    /// <summary>A test run of the built player is silent unless asked: <c>-sfbench</c>,
    /// <c>-sfgallery</c>, <c>-sfgalleryquick</c>, <c>-sfburst</c>, <c>-sfshot</c> and
    /// <c>-sfscreentest</c> start muted, as does anything with <c>-sfmute</c>; <c>-sfsound</c>
    /// keeps the sound. Only the listener's volume goes to zero: every source, voice and the
    /// mixer still run, so frame times compare with runs made before it.</summary>
    static class TestAudio
    {
        static readonly string[] TestFlags = { "-sfbench", "-sfgallery", "-sfgalleryquick", "-sfburst", "-sfshot", "-sfscreentest" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            if (Application.isEditor) return;   // the Editor's agent runs: EditorTools.AgentAudio
            var args = Environment.GetCommandLineArgs();
            bool test = false;
            foreach (var f in TestFlags) test |= Array.IndexOf(args, f) >= 0;
            if (Array.IndexOf(args, "-sfmute") < 0 && (!test || Array.IndexOf(args, "-sfsound") >= 0)) return;
            AudioListener.volume = 0f;
            Debug.Log("[StarForge] Muted for a test run (-sfsound keeps the sound).");
        }
    }
}
