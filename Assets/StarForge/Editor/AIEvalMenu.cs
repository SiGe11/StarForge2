// AIEvalMenu.cs — starts the play-mode AI evaluation (see AIEvalRunner).
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StarForge.Game;

namespace StarForge.EditorTools
{
    public static class AIEvalMenu
    {
        [MenuItem("StarForge/Evaluate AI/Quick (1 game per opponent, 420 s)", priority = 50)]
        public static void Quick() => Run(1, 420f);

        [MenuItem("StarForge/Evaluate AI/Full (5 games per opponent, 600 s)", priority = 51)]
        public static void Full() => Run(5, 600f);

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
