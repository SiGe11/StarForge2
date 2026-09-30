// AIEvalMenu.cs — starts the play-mode AI evaluation (see AIEvalRunner).
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StarForge.Game;

namespace StarForge.EditorTools
{
    public static class AIEvalMenu
    {
        // Matches run longer since the Mechs (they land at four or five minutes and hold
        // a base for a long while), so the caps did too: 420 and 600 s ended every
        // match undecided.
        [MenuItem("StarForge/Evaluate AI/Quick (1 game per opponent, 720 s)", priority = 50)]
        public static void Quick() => Run(1, 720f);

        [MenuItem("StarForge/Evaluate AI/Full (5 games per opponent, 900 s)", priority = 51)]
        public static void Full() => Run(5, 900f);

        /// <summary>Replay one game of the full run (its opponent and its seed), traced:
        /// set <see cref="ReplayKind"/> and <see cref="ReplayGame"/> first.</summary>
        public static int ReplayKind = 3, ReplayGame = 2;
        public static void Replay()
        {
            PlayerPrefs.SetInt(AIEvalRunner.PrefOnly, ReplayKind);
            PlayerPrefs.SetInt(AIEvalRunner.PrefFirst, ReplayGame);
            Run(1, 900f);
        }

        public static void Run(int gamesPerOpponent, float secondsCap)
        {
            if (EditorApplication.isPlaying) return;
            if (EditorSceneManager.GetActiveScene().path != MapBuilder.ScenePath)
                EditorSceneManager.OpenScene(MapBuilder.ScenePath);
            PlayerPrefs.SetInt(AIEvalRunner.PrefGames, gamesPerOpponent);
            PlayerPrefs.SetFloat(AIEvalRunner.PrefSeconds, secondsCap);
            PlayerPrefs.Save();
            EditorApplication.isPlaying = true;
        }
    }
}
