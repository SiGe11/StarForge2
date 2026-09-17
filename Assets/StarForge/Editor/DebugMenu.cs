// DebugMenu.cs — editor switch for the AI's developer view (MatchSettings.debugAI):
// the inspector, its minimap marks, the memory toggle and reset, and the
// end-of-match dossier. Players never see these; a built player shows them only
// when launched with -sfdebug.
using UnityEditor;
using StarForge.Game;

namespace StarForge.EditorTools
{
    public static class DebugMenu
    {
        const string Path = "StarForge/Debug/AI Internals (inspector, memory controls)";
        const string Key = "sf_debug_ai";

        [MenuItem(Path, priority = 70)]
        static void Toggle()
        {
            bool on = !EditorPrefs.GetBool(Key, false);
            EditorPrefs.SetBool(Key, on);
            MatchSettings.debugAI = on;   // takes effect on the next play session's HUD
        }

        [MenuItem(Path, true)]
        static bool Validate()
        {
            Menu.SetChecked(Path, EditorPrefs.GetBool(Key, false));
            return true;
        }
    }
}
